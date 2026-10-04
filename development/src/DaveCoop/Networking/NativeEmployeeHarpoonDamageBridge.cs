using System;
using System.Collections.Generic;
using BepInEx.Logging;
using DaveCoop.Core.Crew;
using DaveCoop.Core.World;
using DR.AI;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DaveCoop.Networking
{
    // A synchronous consumer of an actual host-owned projectile collision.
    // The native damage pipeline can invoke unknown callbacks. Once entered,
    // a false result, exception or lost source must never cause another call.
    // No host PlayerCharacter, weapon handler, skill/spec or client target is
    // used. The profile is the fixed host-approved Mod base damage policy.
    internal sealed class NativeEmployeeHarpoonDamageBridge
    {
        public const int MaxOwners = 64, MaxShots = 64, MaxReferencesPerShot = 16, MaxSteps = 8192;
        private static readonly List<NativeEmployeeHarpoonDamageBridge> Retained =
            new List<NativeEmployeeHarpoonDamageBridge>();
        private static int _shotAttempts;
        private readonly FishStateCapture _capture;
        private readonly FishLifecycleHooks _lifecycle;
        private readonly int _thread;
        private readonly Func<bool> _sourceCurrent;
        private readonly ManualLogSource _logger;
        private readonly Dictionary<HostEmployeeHarpoonHit, Attempt> _attempts =
            new Dictionary<HostEmployeeHarpoonHit, Attempt>();
        private Attempt _current;
        private int _steps, _logs;
        private bool _busy, _checking, _failed, _stopped, _retained;
        public string Status { get; private set; } = "Empty";
        public bool Failed => _failed;
        public bool NativeAbiVerified => false;
        public bool PartialConstructorAllocationRetentionVerified => false;
        public bool CaptureConfirmed => false;
        public bool DamageDeltaProven => false;
        public bool RewardsGranted => false;
        public bool? DamageReturnObserved { get; private set; }
        public long ResolvedTargets { get; private set; }
        public long RejectedTargets { get; private set; }
        public long DispatchesEntered { get; private set; }
        public long TakeDamageCalls { get; private set; }
        public long TakeDamageReturns { get; private set; }
        public int RetainedShotCount => _attempts.Count;

        private sealed class Reference
        {
            public Il2CppObjectBase Wrapper;
            public IntPtr Pointer, Handle;
        }
        private sealed class Attempt
        {
            public HostEmployeeHarpoonHit Hit;
            public HostEntityTarget Target;
            public FishAISystem Fish;
            public Damageable Damageable;
            public Transform ActorTransform, ProjectileTransform, AttackTransform;
            public IntPtr FishPointer, FishUnity, FishClass, DamageablePointer, DamageableUnity;
            public IntPtr ColliderPointer, ColliderUnity, ActorPointer, ActorUnity, ProjectilePointer, ProjectileUnity;
            public IntPtr RootPointer, RootUnity, AttackPointer, AttackUnity, DamagerPointer, DamagerUnity;
            public GameObject Root;
            public Damager Damager;
            public HarpoonIDamager Attacker;
            public IDamager AttackerInterface;
            public AttackData Data;
            public Il2CppReferenceArray<BuffDebuffEffectData> Buffs;
            public readonly List<Reference> References = new List<Reference>(MaxReferencesPerShot);
            public bool Resolved, Rejected, Entered, DamagerInitEntered, TakeDamageEntered, Returned;
            public bool? ReturnObserved;
        }

        // Pure CLR construction. The caller's predicate must check the real
        // host room/actor/scene; it must not depend on Core.ActiveShot after
        // TryHit has consumed that shot, nor call back into this bridge.
        public NativeEmployeeHarpoonDamageBridge(FishStateCapture capture, FishLifecycleHooks lifecycle,
            int confirmedUnityThreadId, Func<bool> sourceCurrent, ManualLogSource logger = null)
        {
            _capture = capture ?? throw new ArgumentNullException(nameof(capture));
            _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
            if (confirmedUnityThreadId < 1) throw new ArgumentOutOfRangeException(nameof(confirmedUnityThreadId));
            _thread = confirmedUnityThreadId;
            _sourceCurrent = sourceCurrent ?? throw new ArgumentNullException(nameof(sourceCurrent));
            _logger = logger;
        }

        public bool TryResolveTarget(HostEmployeeHarpoonHit actualHit, out HostEntityTarget target)
        {
            target = default;
            if (ReferenceEquals(actualHit, null) || !Begin(actualHit)) return false;
            try
            {
                Check();
                if (_attempts.TryGetValue(actualHit, out Attempt old))
                {
                    _current = old;
                    if (!old.Resolved || old.Rejected || old.Entered) return false;
                    ValidateTarget(old); target = old.Target; return true;
                }
                if (_attempts.Count >= MaxShots || _shotAttempts >= MaxShots)
                    throw new InvalidOperationException("ShotQuota");
                RetainOwner();
                _current = new Attempt { Hit = actualHit };
                _attempts.Add(actualHit, _current); _shotAttempts++;
                Attempt item = _current;
                Check();
                if (actualHit.Command.Kind != CrewHarpoonCommandKind.Fire || actualHit.Command.Profile == null ||
                    !WholeDamage(actualHit.Command.Profile.Damage) || actualHit.SceneEpoch < 1 || actualHit.ShotId < 1)
                    return Reject(item, "UnsupportedProfile");
                Collider2D collider = actualHit.Collider;
                if (ReferenceEquals(collider, null)) return Reject(item, "MissingCollider");
                item.ColliderPointer = Read(() => collider.Pointer);
                item.ColliderUnity = Read(() => collider.m_CachedPtr);
                if (item.ColliderPointer == IntPtr.Zero || item.ColliderUnity == IntPtr.Zero)
                    return Reject(item, "ColliderUnavailable");
                item.Fish = Read(() => collider.GetComponentInParent<FishAISystem>());
                if (ReferenceEquals(item.Fish, null)) return Reject(item, "NotFish");
                item.FishPointer = Read(() => item.Fish.Pointer);
                item.FishUnity = Read(() => item.Fish.m_CachedPtr);
                if (item.FishPointer == IntPtr.Zero || item.FishUnity == IntPtr.Zero)
                    return Reject(item, "FishUnavailable");
                item.FishClass = Read(() => IL2CPP.il2cpp_object_get_class(item.FishPointer));
                IntPtr baseClass = Read(() => Il2CppClassPointerStore<FishAISystem>.NativeClassPtr);
                IntPtr ordinaryClass = Read(() => Il2CppClassPointerStore<SABaseFishSystem>.NativeClassPtr);
                if (item.FishClass == IntPtr.Zero || (item.FishClass != baseClass && item.FishClass != ordinaryClass))
                    return Reject(item, "UnsupportedFishClass");
                HostEntityTarget? observed = _capture.ResolveObservedPointer(item.FishPointer.ToInt64());
                if (!observed.HasValue || observed.Value.Kind != EntityKind.Fish ||
                    observed.Value.SceneEpoch != actualHit.SceneEpoch || observed.Value.LocalToken == 0)
                    return Reject(item, "FishNotTracked");
                item.Target = observed.Value;
                item.Damageable = Read(() => item.Fish._fishDamageable);
                if (ReferenceEquals(item.Damageable, null)) return Reject(item, "MissingDamageable");
                item.DamageablePointer = Read(() => item.Damageable.Pointer);
                item.DamageableUnity = Read(() => item.Damageable.m_CachedPtr);
                if (item.DamageablePointer == IntPtr.Zero || item.DamageableUnity == IntPtr.Zero)
                    return Reject(item, "DamageableUnavailable");
                IntPtr damageClass = Read(() => IL2CPP.il2cpp_object_get_class(item.DamageablePointer));
                if (damageClass != Read(() => Il2CppClassPointerStore<Damageable>.NativeClassPtr))
                    return Reject(item, "UnsupportedDamageableClass");
                if (!Read(() => item.Damageable.isInit) || Read(() => item.Damageable.m_IsDead) ||
                    Ptr(Read(() => item.Damageable._HitCollider_k__BackingField)) != item.ColliderPointer)
                    return Reject(item, "UnsupportedFishCollider");
                if (!Read(() => actualHit.Owner.TryReadActorTransform(out item.ActorTransform)) ||
                    !Read(() => actualHit.Owner.TryReadAttackTransform(out item.ProjectileTransform)))
                    return Reject(item, "ProjectileUnavailable");
                item.ActorPointer = Read(() => item.ActorTransform.Pointer);
                item.ActorUnity = Read(() => item.ActorTransform.m_CachedPtr);
                item.ProjectilePointer = Read(() => item.ProjectileTransform.Pointer);
                item.ProjectileUnity = Read(() => item.ProjectileTransform.m_CachedPtr);
                if (item.ActorPointer == IntPtr.Zero || item.ActorUnity == IntPtr.Zero ||
                    item.ProjectilePointer == IntPtr.Zero || item.ProjectileUnity == IntPtr.Zero ||
                    item.ActorPointer == item.ProjectilePointer) return Reject(item, "InvalidOwnedTransforms");
                ValidateTarget(item);
                Keep(item, collider); Keep(item, item.Fish); Keep(item, item.Damageable);
                Keep(item, item.ActorTransform); Keep(item, item.ProjectileTransform);
                ValidateTarget(item);
                item.Resolved = true; ResolvedTargets++; target = item.Target;
                Status = "TargetResolved"; return true;
            }
            catch (Exception error) { Fail(error); return false; }
            finally { End(); }
        }

        public bool TryDispatch(HostEmployeeHarpoonHit actualHit, CrewHarpoonHit onceCoreHit)
        {
            if (ReferenceEquals(actualHit, null) || ReferenceEquals(onceCoreHit, null) || !Begin(actualHit)) return false;
            try
            {
                if (!_attempts.TryGetValue(actualHit, out Attempt item) || !item.Resolved || item.Rejected || item.Entered)
                    return false;
                _current = item;
                CrewHarpoonCollision collision = onceCoreHit.Collision;
                if (collision == null || !ReferenceEquals(collision.Shot, actualHit.Command) ||
                    onceCoreHit.ShotId != actualHit.ShotId || collision.EntityId != item.Target.EntityId ||
                    collision.Generation != item.Target.Generation || collision.DataTid != item.Target.DataTid ||
                    collision.Position != actualHit.Point || onceCoreHit.Damage != actualHit.Command.Profile.Damage)
                    return false;
                ValidateTarget(item);
                // One-way claim precedes every constructor/Init/TakeDamage.
                // Partial construction itself is unknown and cannot be retried.
                item.Entered = true; DispatchesEntered++; DamageReturnObserved = null;
                Status = "NativePreparationEnteredUnknown";
                PrepareAttack(item);
                ValidateTarget(item); ValidateAttack(item);
                Check();
                item.TakeDamageEntered = true; TakeDamageCalls++;
                Status = "TakeDamageEnteredUnknown";
                bool originalResult = item.Damageable.TakeDamage(item.Data);
                // Freeze the original result before any post-call validation.
                item.ReturnObserved = originalResult; item.Returned = true;
                DamageReturnObserved = originalResult; TakeDamageReturns++;
                // Normal damage may retire the fish. Do not reinterpret that
                // as another usable target or lose the observed original bool.
                Check();
                Status = originalResult ? "OriginalTrueObserved" : "OriginalFalseObserved";
                return true;
            }
            catch (Exception error) { Fail(error); return false; }
            finally { End(); }
        }

        public void Stop(string reason = null)
        {
            _stopped = true;
            if (!_failed) Status = "StoppedReferencesRetained";
            // Own source roots are never active. No hot destroy/free can be
            // proved safe for unknown native observers holding AttackData.
        }

        private void PrepareAttack(Attempt item)
        {
            Check();
            item.Root = new GameObject("MultiDave_EmployeeDamageSource");
            Keep(item, item.Root);
            item.RootPointer = Read(() => item.Root.Pointer); item.RootUnity = Read(() => item.Root.m_CachedPtr);
            if (item.RootUnity == IntPtr.Zero || Read(() => IL2CPP.il2cpp_object_get_class(item.RootPointer)) !=
                Read(() => Il2CppClassPointerStore<GameObject>.NativeClassPtr))
                throw new InvalidOperationException("OwnedRootUnavailable");
            Write(() => item.Root.SetActive(false));
            if (Read(() => item.Root.activeSelf)) throw new InvalidOperationException("OwnedRootActive");
            Scene scene = Read(() => item.ProjectileTransform.gameObject.scene);
            Write(() => SceneManager.MoveGameObjectToScene(item.Root, scene));
            item.AttackTransform = Read(() => item.Root.transform); Keep(item, item.AttackTransform);
            item.AttackPointer = Read(() => item.AttackTransform.Pointer);
            item.AttackUnity = Read(() => item.AttackTransform.m_CachedPtr);
            // Keep this inactive source independent of the flying root. Its
            // later retirement must not destroy an unknown retained attacker.
            Write(() => item.AttackTransform.position = new Vector3 { x = item.Hit.Point.X, y = item.Hit.Point.Y, z = item.Hit.Point.Z });
            Check(); item.Damager = item.Root.AddComponent<Damager>(); Keep(item, item.Damager);
            item.DamagerPointer = Read(() => item.Damager.Pointer);
            item.DamagerUnity = Read(() => item.Damager.m_CachedPtr);
            ValidateOwnComponent(item);
            Write(() => item.Damager.enabled = false);
            if (Read(() => item.Damager.enabled) || Read(() => item.Root.activeInHierarchy))
                throw new InvalidOperationException("OwnedDamagerActive");
            // The actual native component constructor initializes these
            // collections. Do not silently invent missing constructor state.
            var debuffs = Read(() => item.Damager.DebuffToTarget);
            if (ReferenceEquals(debuffs, null) || Read(() => debuffs._size) != 0 ||
                ReferenceEquals(Read(() => item.Damager.hitCountPerDamageable), null))
                throw new InvalidOperationException("OwnedDamagerStateUnavailable");
            Check(); item.Attacker = new HarpoonIDamager(); Keep(item, item.Attacker);
            Write(() => item.Attacker._Owner_k__BackingField = item.ActorTransform);
            Write(() => item.Attacker._transform_k__BackingField = item.AttackTransform);
            item.AttackerInterface = Read(() => item.Attacker.Cast<IDamager>()); Keep(item, item.AttackerInterface);
            Check(); item.Buffs = new Il2CppReferenceArray<BuffDebuffEffectData>(0); Keep(item, item.Buffs);
            Check();
            item.Data = new AttackData(item.AttackerInterface, item.Damager,
                (int)item.Hit.Command.Profile.Damage, AttackType.Player_Harpoon, item.Buffs);
            Keep(item, item.Data);
            // Explicit basic Mod policy: no native head/skills/element/buff
            // resource is borrowed. The empty array is an actual owned array.
            Write(() => item.Data.element = EElement.None);
            Write(() => item.Data.direction = new Vector2 { x = item.Hit.Direction.X, y = item.Hit.Direction.Y });
            Check(); item.Data.SetHitPos(new Vector3 { x = item.Hit.Point.X, y = item.Hit.Point.Y, z = item.Hit.Point.Z }); Check();
            Write(() => item.Damager.GetLastOtherCollider = item.Hit.Collider);
            item.DamagerInitEntered = true;
            ValidateOwnComponent(item);
            Check(); item.Damager.Init(item.Data); Check();
            ValidateAttack(item);
        }

        private void ValidateTarget(Attempt item)
        {
            Check();
            if (!Read(() => item.Hit.Owner.ValidateHit(item.Hit)))
                throw new InvalidOperationException("PhysicalCollisionChanged");
            if (!_lifecycle.OwnsActiveTracker || !_capture.UsesLifecycle(_lifecycle.Tracker) ||
                !_lifecycle.Tracker.TryGetActiveGeneration(item.FishPointer.ToInt64(), out long generation) ||
                generation != item.Target.Generation) throw new InvalidOperationException("FishLifeChanged");
            FishAISystem current = null;
            if (!Read(() => _capture.TryResolveNativeFish(item.Target.SceneEpoch, item.Target.EntityId, out current)) ||
                ReferenceEquals(current, null) || Read(() => current.Pointer) != item.FishPointer)
                throw new InvalidOperationException("FishBindingChanged");
            HostEntityTarget? target = _capture.ResolveObservedPointer(item.FishPointer.ToInt64());
            if (!target.HasValue || !SameTarget(target.Value, item.Target)) throw new InvalidOperationException("TargetChanged");
            if (Read(() => item.Fish.Pointer) != item.FishPointer || Read(() => item.Fish.m_CachedPtr) != item.FishUnity ||
                Read(() => IL2CPP.il2cpp_object_get_class(item.FishPointer)) != item.FishClass ||
                Ptr(Read(() => item.Fish._fishDamageable)) != item.DamageablePointer ||
                Read(() => item.Damageable.Pointer) != item.DamageablePointer ||
                Read(() => item.Damageable.m_CachedPtr) != item.DamageableUnity ||
                Read(() => item.Hit.Collider.Pointer) != item.ColliderPointer ||
                Read(() => item.Hit.Collider.m_CachedPtr) != item.ColliderUnity ||
                !Read(() => item.Hit.Collider.enabled) || !Read(() => item.Hit.Collider.gameObject.activeInHierarchy) ||
                Read(() => item.Fish.gameObject.scene.handle) != item.Hit.SceneHandle ||
                Read(() => item.Hit.Collider.gameObject.scene.handle) != item.Hit.SceneHandle ||
                !Read(() => item.Damageable.isInit) || Read(() => item.Damageable.m_IsDead) ||
                Ptr(Read(() => item.Damageable._HitCollider_k__BackingField)) != item.ColliderPointer)
                throw new InvalidOperationException("NativeTargetChanged");
            // This field getter boxes native DefenseData. Its box pointer is
            // not a stable defense identity: validate the two owned references.
            DefenseData defense = Read(() => item.Damageable.m_DefenseData);
            if (ReferenceEquals(defense, null) || Ptr(Read(() => defense.defender)) != item.FishPointer ||
                Ptr(Read(() => defense.damageable)) != item.DamageablePointer)
                throw new InvalidOperationException("DefenseBindingChanged");
            ValidateTransforms(item);
        }

        private void ValidateTransforms(Attempt item)
        {
            Transform actor = null, projectile = null;
            if (!Read(() => item.Hit.Owner.TryReadActorTransform(out actor)) ||
                !Read(() => item.Hit.Owner.TryReadAttackTransform(out projectile)) ||
                Ptr(actor) != item.ActorPointer || Ptr(projectile) != item.ProjectilePointer ||
                Read(() => actor.m_CachedPtr) != item.ActorUnity ||
                Read(() => projectile.m_CachedPtr) != item.ProjectileUnity)
                throw new InvalidOperationException("OwnedTransformChanged");
        }

        private void ValidateAttack(Attempt item)
        {
            Check(); ValidateTransforms(item); ValidateOwnComponent(item);
            if (!item.DamagerInitEntered || ReferenceEquals(item.Root, null) ||
                Read(() => item.Root.activeSelf) || Read(() => item.Damager.enabled) ||
                Ptr(Read(() => item.Attacker._Owner_k__BackingField)) != item.ActorPointer ||
                Ptr(Read(() => item.Attacker._transform_k__BackingField)) != Ptr(item.AttackTransform) ||
                Ptr(Read(() => item.Data.attacker)) != Ptr(item.Attacker) ||
                Ptr(Read(() => item.Data.damager)) != Ptr(item.Damager) ||
                Read(() => item.Data.damage) != (int)item.Hit.Command.Profile.Damage ||
                Read(() => item.Data.attackType) != AttackType.Player_Harpoon ||
                Read(() => item.Data.element) != EElement.None ||
                Ptr(Read(() => item.Data.buff)) != Ptr(item.Buffs) || Read(() => item.Buffs.Length) != 0)
                throw new InvalidOperationException("OwnedAttackChanged");
            AttackData copied = Read(() => item.Damager.m_AttackData);
            if (ReferenceEquals(copied, null) || Ptr(Read(() => copied.attacker)) != Ptr(item.Attacker) ||
                Ptr(Read(() => copied.damager)) != Ptr(item.Damager) ||
                Read(() => copied.damage) != (int)item.Hit.Command.Profile.Damage ||
                Read(() => copied.attackType) != AttackType.Player_Harpoon)
                throw new InvalidOperationException("OwnDamagerInitChanged");
        }

        private void ValidateOwnComponent(Attempt item)
        {
            Check();
            if (Read(() => item.Root.Pointer) != item.RootPointer || Read(() => item.Root.m_CachedPtr) != item.RootUnity ||
                item.RootUnity == IntPtr.Zero || Read(() => item.Damager.Pointer) != item.DamagerPointer ||
                Read(() => item.Damager.m_CachedPtr) != item.DamagerUnity || item.DamagerUnity == IntPtr.Zero ||
                Read(() => IL2CPP.il2cpp_object_get_class(item.DamagerPointer)) !=
                Read(() => Il2CppClassPointerStore<Damager>.NativeClassPtr) ||
                Ptr(Read(() => item.Damager.gameObject)) != item.RootPointer ||
                Ptr(Read(() => item.Damager.transform)) != item.AttackPointer ||
                Read(() => item.AttackTransform.Pointer) != item.AttackPointer ||
                Read(() => item.AttackTransform.m_CachedPtr) != item.AttackUnity || item.AttackUnity == IntPtr.Zero ||
                Ptr(Read(() => item.AttackTransform.gameObject)) != item.RootPointer ||
                Read(() => item.Root.scene.handle) != item.Hit.SceneHandle || Read(() => item.Root.activeSelf))
                throw new InvalidOperationException("ForeignDamagerComponent");
            foreach (Reference reference in item.References)
                if (reference.Handle == IntPtr.Zero || Read(() => IL2CPP.il2cpp_gchandle_get_target(reference.Handle)) != reference.Pointer)
                    throw new InvalidOperationException("OwnedReferenceChanged");
        }

        private bool Begin(HostEmployeeHarpoonHit hit)
        {
            if (Environment.CurrentManagedThreadId != _thread || _failed || _stopped) return false;
            if (_busy || _checking) { Fail(new InvalidOperationException("ReentrantDamage")); return false; }
            _busy = true; _steps = 0;
            _current = new Attempt { Hit = hit };
            return true;
        }
        private void End() { _current = null; _busy = false; }
        private void Check()
        {
            if (!_busy || _failed || _stopped || Environment.CurrentManagedThreadId != _thread ||
                ++_steps > MaxSteps || _current == null || _current.Hit == null || _checking)
                throw new InvalidOperationException("DamageWindowClosed");
            _checking = true;
            try
            {
                if (!_sourceCurrent() || !_lifecycle.OwnsActiveTracker || !_capture.UsesLifecycle(_lifecycle.Tracker) ||
                    !_current.Hit.Owner.ValidateHitSource(_current.Hit)) throw new InvalidOperationException("DamageSourceChanged");
            }
            finally { _checking = false; }
            if (_failed || _stopped) throw new InvalidOperationException("DamageWindowClosed");
        }
        private T Read<T>(Func<T> read) { Check(); T value = read(); Check(); return value; }
        private void Write(Action write) { Check(); write(); Check(); }
        private IntPtr Ptr(Il2CppObjectBase value) => ReferenceEquals(value, null) ? IntPtr.Zero : Read(() => value.Pointer);
        private void Keep(Attempt item, Il2CppObjectBase value)
        {
            Check();
            if (ReferenceEquals(value, null)) throw new InvalidOperationException("MissingReference");
            IntPtr pointer = Read(() => value.Pointer);
            if (pointer == IntPtr.Zero) throw new InvalidOperationException("CollectedReference");
            foreach (Reference reference in item.References) if (reference.Pointer == pointer) return;
            if (item.References.Count >= MaxReferencesPerShot) throw new InvalidOperationException("ReferenceQuota");
            Reference owned = new Reference { Wrapper = value, Pointer = pointer };
            item.References.Add(owned);
            Check(); owned.Handle = IL2CPP.il2cpp_gchandle_new(pointer, false); Check();
            if (owned.Handle == IntPtr.Zero) throw new InvalidOperationException("StrongHandleUnavailable");
        }
        private void RetainOwner()
        {
            if (_retained) return;
            if (Retained.Count >= MaxOwners) throw new InvalidOperationException("OwnerQuota");
            Retained.Add(this); _retained = true;
        }
        private bool Reject(Attempt item, string status)
        { item.Rejected = true; RejectedTargets++; Status = status; return false; }
        private void Fail(Exception error)
        {
            _failed = true; _stopped = true; Status = "UnknownReferencesRetained";
            if (_logs++ < 8)
            {
                try { _logger?.LogWarning("DAVECOOP_EMPLOYEE_DAMAGE_STOP: " + error.GetType().Name); }
                catch { }
            }
        }
        private static bool WholeDamage(float damage) => float.IsFinite(damage) && damage >= 1 &&
            damage <= 100000 && damage == Math.Truncate((double)damage);
        private static bool SameTarget(HostEntityTarget a, HostEntityTarget b) =>
            a.SceneEpoch == b.SceneEpoch && a.EntityId == b.EntityId && a.Generation == b.Generation &&
            a.DataTid == b.DataTid && a.LocalToken == b.LocalToken && a.Kind == b.Kind;
    }
}
