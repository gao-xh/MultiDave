using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using DaveCoop.Core.World;
using DR.AI;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace DaveCoop.Networking
{
    // Process-only experimental suppression. A natural fish Awake, its exact
    // native scene birth and a distinct fish GameObject are required. This is
    // not a complete audit of every script, animator, task or fish-like boss.
    // No original constructor, reward, AI initialization or cleanup is invoked.
    internal sealed class NativeGuestFishQuarantine
    {
        public const int MaxFish = 4096;
        public const int MaxHandles = MaxFish * 3;
        public const int MaxCollidersPerFish = 128;
        public const int MaxRigidbodiesPerFish = 64;
        public const int MaxComponentsPerFish = 256;
        public const int MaxRetainedComponents = 65536;
        public const int MaxNativeReadsPerCall = 8192;
        public const int MaxUpdateFish = 16;
        public const int MaxLogs = 128;
        private const string Owner = Plugin.Id + ".guest-fish-quarantine";
        private static NativeGuestFishQuarantine _active;
        private readonly NativeGuestInitializationController _source;
        private readonly NativeGuestSceneController _scenes;
        private readonly ManualLogSource _logger;
        private readonly Dictionary<long, Record> _actors = new Dictionary<long, Record>();
        private readonly Dictionary<long, Record> _roots = new Dictionary<long, Record>();
        // Suppression-only tombstones, never a source or permission. Even after
        // native retirement, skipped-Awake child cleanup must stay suppressed.
        // Pointer reuse can conservatively block a later unrelated component.
        private readonly Dictionary<long, Record> _components = new Dictionary<long, Record>();
        private readonly List<Record> _ordered = new List<Record>();
        private readonly List<Il2CppObjectBase> _references = new List<Il2CppObjectBase>();
        private readonly List<IntPtr> _handles = new List<IntPtr>();
        private readonly HashSet<MethodBase> _targets = new HashSet<MethodBase>();
        private Harmony _harmony;
        private bool _installAttempted, _installed, _busy, _nativeStep, _identityProbe, _failed;
        private Record _deactivating;
        private int _steps, _cursor, _logs;
        private long _totalReads;
        private string _lastLog;

        private sealed class Record
        {
            public NativeGuestFishBirth Birth;
            public FishAISystem Actor;
            public GameObject Root;
            public Transform Transform;
            public long ActorPointer, RootPointer, TransformPointer;
            public IntPtr ActorUnity, RootUnity, TransformUnity, NativeClass;
            public int SceneHandle;
            public bool Inert, AwakeObserved, WriteAttempted, Quarantined, Retired;
        }

        private sealed class FishType
        {
            public Type Managed;
            public Func<IntPtr> NativeClass;
        }

        // Creating these delegates/types does not read a native class store.
        private static readonly FishType[] FishTypes = {
            TypeOf<FishAISystem>(), TypeOf<SpecialAttackerFishAISystem>(), TypeOf<SABaseFishSystem>(),
            TypeOf<SABaseAI>(), TypeOf<SAMahoniCommon>(), TypeOf<SAMahoniGeneral>(),
            TypeOf<SAXiphactinus>(), TypeOf<SASnappingTurtle>(), TypeOf<SAGroundCrawlerFish>()
        };
        private static FishType TypeOf<T>() where T : Il2CppObjectBase => new FishType {
            Managed = typeof(T), NativeClass = () => Il2CppClassPointerStore<T>.NativeClassPtr };

        public int Count => _ordered.Count;
        public int RetainedHandles => _handles.Count;
        public int RetainedComponentIdentities => _components.Count;
        public int TargetCount => _targets.Count;
        public long ObservedNativeReads => _totalReads;
        public bool Installed => _installed;
        public bool Failed => _failed || _source.Failed;
        public bool Healthy => _installed && !Failed;
        public string Status { get; private set; } = "Fish quarantine: awaiting experimental guest startup.";
        public bool FullCoverageVerified => false;
        public bool NativeRuntimeVerified => false;
        public bool NativeFieldAbiVerified => false;
        public bool PartialConstructorAllocationRetentionVerified => false;
        public bool GuestStateIsolated => false;
        public bool WorldAuthority => false;
        public bool CargoAuthority => false;

        public NativeGuestFishQuarantine(NativeGuestInitializationController source, ManualLogSource logger)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _scenes = source.MapController.SceneSources;
        }

        public void Install()
        {
            if (_installAttempted || _active != null)
                throw new InvalidOperationException("Native guest fish quarantine is process-only and single-use.");
            _installAttempted = true; _active = this;
            try
            {
                // Reflection verifies declarations only. Pointer, class stores,
                // native roots and Unity APIs are never read during installation.
                var targets = CreateTargets();
                _harmony = new Harmony(Owner);
                foreach (var target in targets)
                {
                    _targets.Add(target.Key);
                    _harmony.Patch(target.Key, prefix: new HarmonyMethod(target.Value) { priority = Priority.First });
                    if (Harmony.GetPatchInfo(target.Key)?.Owners.Contains(Owner) != true)
                        throw new InvalidOperationException("Own fish quarantine patch was not registered.");
                }
                _installed = true;
                Log("ARMED", "Experimental fish birth quarantine armed; complete native coverage and ABI remain unverified.");
            }
            catch (Exception error)
            {
                Fail("Fish quarantine installation failed: " + error.GetType().Name);
                throw new InvalidOperationException("Native guest fish quarantine installation failed.");
            }
        }

        public void Update()
        {
            if (!_installed || Failed) return;
            if (!Enter()) return;
            try
            {
                if (!GenerationCurrent()) return;
                int remaining = Math.Min(MaxUpdateFish, _ordered.Count);
                while (remaining-- > 0)
                {
                    if (_cursor >= _ordered.Count) _cursor = 0;
                    Record record = _ordered[_cursor++];
                    if (record.Retired) continue;
                    if (_scenes.IsRetiredFishBirth(record.Birth)) { record.Retired = true; continue; }
                    RequireBirth(record);
                    AuditInactive(record, true);
                    // Pending pre-return births remain inert; only the actual
                    // successful operation/Scene may complete their source.
                    _scenes.TryReadFishSource(record.Birth, out MapOriginControllerSource ignored);
                    RequireBirth(record);
                }
                Status = "Fish quarantine: retained " + Count + " native births; complete coverage unverified.";
            }
            catch (Exception error) { Fail("Fish quarantine update failed: " + error.GetType().Name); }
            finally { Exit(); }
        }

        public bool CanDisplayHostFish(int sceneHandle)
        {
            if (!Healthy || sceneHandle == 0 || !Enter()) return false;
            try
            {
                if (!GenerationCurrent() || !_scenes.HasFishIsolationScene(sceneHandle)) return false;
                foreach (Record record in _ordered)
                {
                    if (record.SceneHandle != sceneHandle || record.Retired) continue;
                    if (_scenes.IsRetiredFishBirth(record.Birth)) { record.Retired = true; continue; }
                    RequireBirth(record); AuditInactive(record, true);
                    if (!_scenes.TryReadFishSource(record.Birth, out MapOriginControllerSource source) || source == null ||
                        !source.IsFishBirth || source.ControllerPointer != record.ActorPointer ||
                        source.SceneHandle != sceneHandle || source.ControllerLife != record.Birth.BirthLife) return false;
                    RequireBirth(record);
                }
                // An empty native population needs no invented birth. The
                // scene's actual completed operation is still mandatory.
                return !_failed && _scenes.HasFishIsolationScene(sceneHandle);
            }
            catch (Exception error) { Fail("Fish display source failed: " + error.GetType().Name); return false; }
            finally { Exit(); }
        }

        private bool FishCallback(FishAISystem actor, MethodBase method)
        {
            if (Failed) return false;
            if (_busy)
            {
                // SetActive may synchronously call the uninitialized fish's
                // OnDisable. Only this already-marked exact actor may nest.
                if (_deactivating != null && method.Name == "OnDisable")
                {
                    try
                    {
                        RequireConfirmedThread();
                        if (ReferenceEquals(actor, null) || actor.Pointer.ToInt64() != _deactivating.ActorPointer)
                            throw new InvalidOperationException("Unexpected deactivation actor.");
                        return false;
                    }
                    catch (Exception error) { Fail("Fish deactivation callback failed: " + error.GetType().Name); return false; }
                }
                Fail("Fish quarantine reentered an identity or source read."); return false;
            }
            if (!Enter()) return false;
            try
            {
                // This narrow handle-target identity read does not require or
                // authorize a live generation. It only finds retained cleanup
                // tombstones before the ordinary-generation check below.
                long pointer = ProbeRetainedPointer(actor);
                if (_actors.TryGetValue(pointer, out Record record))
                {
                    if (method.Name == "Awake") throw new InvalidOperationException("A quarantined fish repeated its Awake.");
                    if (record.Retired || _scenes.IsRetiredFishBirth(record.Birth)) { record.Retired = true; return false; }
                    if (method.Name == "OnDestroy")
                    { _scenes.RetireFishBirth(record.Birth); record.Retired = true; return false; }
                    // These fish never ran Awake; original cleanup remains
                    // forbidden even when the route/Context has retired.
                    if (method.Name == "OnDisable") return false;
                    if (!GenerationCurrent()) return false;
                    RequireBirth(record);
                    if (!record.Quarantined) throw new InvalidOperationException("Fish initialization raced its quarantine write.");
                    AuditInactive(record, false); return false;
                }
                if (!GenerationCurrent()) return true;
                if (method.Name != "Awake")
                    throw new InvalidOperationException("A fish callback has no intercepted natural Awake.");
                Record born = PrepareBirth(actor);
                born.AwakeObserved = true;
                Quarantine(born);
                Log("BORN", "Original fish initialization suppressed; the scene source may still be pending.");
                return false;
            }
            catch (Exception error) { Fail("Natural fish quarantine failed: " + error.GetType().Name); return false; }
            finally { Exit(); }
        }

        private Record PrepareBirth(FishAISystem actor)
        {
            if (ReferenceEquals(actor, null) || _ordered.Count >= MaxFish)
                throw new InvalidOperationException("Fish actor is missing or exceeds the retained birth bound.");
            ExactFish(actor);
            // These direct false values are necessary checks, not a proof that
            // every unrelated component has never initialized.
            if (Read(() => actor.isInitInStartFinished) || Read(() => actor.initInteractionBody))
                throw new InvalidOperationException("An already-initialized fish cannot enter birth quarantine.");
            if (Read(() => actor.TryCast<SABaseFishSystem>()) is SABaseFishSystem special &&
                (Read(() => special._IsInitDefaultMoveable_k__BackingField) ||
                 !ReferenceEquals(Read(() => special.AIUpdates), null) ||
                 !ReferenceEquals(Read(() => special._onFixedUpdateExternal), null)))
                throw new InvalidOperationException("An existing special-fish update binding cannot enter quarantine.");
            NativeGuestFishBirth birth = _scenes.RegisterFishBirth(actor);
            if (birth == null || !ReferenceEquals(birth.Producer, _scenes) || birth.ActorPointer == 0 ||
                birth.RootPointer == 0 || birth.UnityPointer == IntPtr.Zero || birth.RootUnityPointer == IntPtr.Zero ||
                _actors.ContainsKey(birth.ActorPointer) || _roots.ContainsKey(birth.RootPointer))
                throw new InvalidOperationException("Fish birth is missing, duplicated or shares another root.");
            var record = new Record { Birth = birth, Actor = birth.Actor, Root = birth.Root,
                ActorPointer = birth.ActorPointer, RootPointer = birth.RootPointer, ActorUnity = birth.UnityPointer,
                RootUnity = birth.RootUnityPointer, NativeClass = birth.NativeClass, SceneHandle = birth.SceneHandle, Inert = true };
            // Retain the inert record before the first handle/native step. If a
            // later step throws, its source and restoration facts stay reachable.
            _actors.Add(record.ActorPointer, record); _roots.Add(record.RootPointer, record); _ordered.Add(record);
            RequireBirth(record); Keep(record.Actor, record); Keep(record.Root, record);
            record.Transform = Read(() => record.Root.transform, record);
            record.TransformPointer = Read(() => Pointer(record.Transform), record);
            record.TransformUnity = Read(() => record.Transform.m_CachedPtr, record);
            if (record.TransformPointer == 0 || record.TransformUnity == IntPtr.Zero)
                throw new InvalidOperationException("Fish root has no live distinct transform.");
            Keep(record.Transform, record);
            AuditRootShape(record);
            RememberChildIdentities(record);
            RequireBirth(record); return record;
        }

        private void RememberChildIdentities(Record record)
        {
            var components = Read(() => record.Root.GetComponentsInChildren<Component>(true), record);
            int count = Read(() => components.Length, record);
            if (count > MaxComponentsPerFish) throw new InvalidOperationException("Fish component subtree exceeds its identity bound.");
            for (int index = 0; index < count; index++)
            {
                Component component = Read(() => components[index], record);
                RequireContained(record, component);
                RememberChildIdentity(record, Read(() => Pointer(component), record));
            }
        }
        private void RememberChildIdentity(Record record, long pointer)
        {
            if (pointer == 0) throw new InvalidOperationException("Fish component has no retained identity.");
            if (_components.TryGetValue(pointer, out Record previous))
            {
                if (!ReferenceEquals(previous, record)) throw new InvalidOperationException("Fish component identity belongs to another retained root.");
                return;
            }
            if (_components.Count >= MaxRetainedComponents) throw new InvalidOperationException("Fish component identity retention bound exceeded.");
            _components.Add(pointer, record);
        }

        private void Quarantine(Record record)
        {
            if (record.WriteAttempted) throw new InvalidOperationException("Fish quarantine writes cannot be retried.");
            RequireBirth(record); record.WriteAttempted = true;
            var colliders = Read(() => record.Root.GetComponentsInChildren<Collider2D>(true), record);
            int colliderCount = Read(() => colliders.Length, record);
            if (colliderCount > MaxCollidersPerFish) throw new InvalidOperationException("Fish collider subtree exceeds its bound.");
            for (int index = 0; index < colliderCount; index++)
            {
                Collider2D collider = Read(() => colliders[index], record);
                RequireContained(record, collider);
                Write(() => collider.enabled = false, record);
                if (Read(() => collider.enabled, record)) throw new InvalidOperationException("A fish collider remained enabled.");
            }
            var bodies = Read(() => record.Root.GetComponentsInChildren<Rigidbody2D>(true), record);
            int bodyCount = Read(() => bodies.Length, record);
            if (bodyCount > MaxRigidbodiesPerFish) throw new InvalidOperationException("Fish rigidbody subtree exceeds its bound.");
            for (int index = 0; index < bodyCount; index++)
            {
                Rigidbody2D body = Read(() => bodies[index], record);
                RequireContained(record, body);
                Write(() => body.simulated = false, record);
                if (Read(() => body.simulated, record)) throw new InvalidOperationException("Fish physics remained simulated.");
            }
            // No fish OnDisable cleanup may run after its original Awake was
            // skipped. The narrowly identified nested callback above only skips.
            _deactivating = record;
            try { Write(() => record.Root.SetActive(false), record); }
            finally { _deactivating = null; }
            AuditInactive(record, true);
            RequireBirth(record); record.Quarantined = true;
        }

        private void AuditRootShape(Record record)
        {
            var fish = Read(() => record.Root.GetComponentsInChildren<FishAISystem>(true), record);
            if (Read(() => fish.Length, record) != 1 || Read(() => Pointer(fish[0]), record) != record.ActorPointer)
                throw new InvalidOperationException("Fish quarantine root is shared by another fish.");
            if (Read(() => record.Root.GetComponentsInChildren<PlayerCharacter>(true).Length, record) != 0 ||
                Read(() => record.Root.GetComponentsInChildren<Camera>(true).Length, record) != 0 ||
                Read(() => record.Root.GetComponentsInChildren<SceneContext>(true).Length, record) != 0 ||
                Read(() => record.Root.GetComponentsInChildren<Terrain>(true).Length, record) != 0)
                throw new InvalidOperationException("Fish quarantine root contains player, camera, map or terrain ownership.");
            if (Read(() => Pointer(record.Actor.gameObject), record) != record.RootPointer)
                throw new InvalidOperationException("Fish quarantine cannot deactivate an ancestor map root.");
        }

        private void AuditInactive(Record record, bool completePhysics)
        {
            RequireBirth(record);
            if (Read(() => Pointer(record.Root.transform), record) != record.TransformPointer ||
                Read(() => record.Transform.m_CachedPtr, record) != record.TransformUnity ||
                Read(() => record.Root.activeSelf, record) || Read(() => record.Root.activeInHierarchy, record))
                throw new InvalidOperationException("A quarantined fish root was replaced or reactivated.");
            // Unity root inactivity is observed, not used as a claim that all
            // externally registered work stopped. Writes are never repeated.
            if (!completePhysics) return;
            var colliders = Read(() => record.Root.GetComponentsInChildren<Collider2D>(true), record);
            int count = Read(() => colliders.Length, record);
            if (count > MaxCollidersPerFish) throw new InvalidOperationException("Fish collider subtree grew beyond its bound.");
            for (int index = 0; index < count; index++)
            { Collider2D collider = Read(() => colliders[index], record); RequireContained(record, collider);
                if (Read(() => collider.enabled, record)) throw new InvalidOperationException("Fish collider isolation was lost."); }
            var bodies = Read(() => record.Root.GetComponentsInChildren<Rigidbody2D>(true), record);
            count = Read(() => bodies.Length, record);
            if (count > MaxRigidbodiesPerFish) throw new InvalidOperationException("Fish rigidbody subtree grew beyond its bound.");
            for (int index = 0; index < count; index++)
            { Rigidbody2D body = Read(() => bodies[index], record); RequireContained(record, body);
                if (Read(() => body.simulated, record)) throw new InvalidOperationException("Fish physics isolation was lost."); }
        }

        private bool ComponentCallback(Component component, MethodBase method)
        {
            // Unrelated player/global component callbacks can precede first
            // Update. Do not inspect native ownership on that early path.
            if (_source.ConfirmedUnityThreadId == 0) return _components.Count == 0;
            long componentPointer;
            Record retained;
            try
            {
                componentPointer = ProbeRetainedPointer(component);
                _components.TryGetValue(componentPointer, out retained);
            }
            catch (Exception error)
            { Fail("Fish child identity probe failed: " + error.GetType().Name); return false; }
            // Failure cannot authorize native ownership reads. Known children
            // remain blocked; unrelated player/NPC/global callbacks stay original.
            if (Failed) return retained == null;
            if (_busy)
            {
                // A different player's callback is not made a fish callback
                // merely because SetActive dispatched it synchronously. No
                // containment/native ownership read is attempted in this branch.
                if (retained == null) return true;
                if (_deactivating != null && ReferenceEquals(retained, _deactivating)) return false;
                Fail("Fish child callback reentered a native quarantine step."); return false;
            }
            if (!Enter()) return false;
            try
            {
                if (retained != null && (retained.Retired || _scenes.IsRetiredFishBirth(retained.Birth)))
                { retained.Retired = true; return false; }
                if (!GenerationCurrent()) return retained == null;
                if (ReferenceEquals(component, null)) throw new InvalidOperationException("Native child callback has no instance.");
                if (retained != null)
                { RequireBirth(retained); RequireContained(retained, component); return false; }
                FishAISystem actor = Read(() => component.GetComponentInParent<FishAISystem>(true));
                if (ReferenceEquals(actor, null)) return true;
                ExactFish(actor);
                long pointer = Read(() => Pointer(actor));
                if (!_actors.TryGetValue(pointer, out Record record))
                    throw new InvalidOperationException("Fish child initialization preceded the supported fish Awake boundary.");
                if (record.Retired || _scenes.IsRetiredFishBirth(record.Birth)) { record.Retired = true; return false; }
                RequireBirth(record); RequireContained(record, component);
                if (!record.Inert) throw new InvalidOperationException("Fish child has no retained inert root.");
                RememberChildIdentity(record, componentPointer);
                return false;
            }
            catch (Exception error) { Fail("Fish child callback isolation failed: " + error.GetType().Name); return false; }
            finally { Exit(); }
        }

        private void RequireContained(Record record, Component component)
        {
            if (ReferenceEquals(component, null)) throw new InvalidOperationException("Fish subtree component is missing.");
            Transform transform = Read(() => component.transform, record);
            long pointer = Read(() => Pointer(transform), record);
            if (pointer != record.TransformPointer && !Read(() => transform.IsChildOf(record.Transform), record))
                throw new InvalidOperationException("Fish component is outside its retained root.");
            if (Read(() => component.gameObject.scene.m_Handle, record) != record.SceneHandle)
                throw new InvalidOperationException("Fish component belongs to another actual scene.");
        }

        private void ExactFish(FishAISystem actor)
        {
            IntPtr pointer = Read(() => actor.Pointer);
            IntPtr native = Read(() => IL2CPP.il2cpp_object_get_class(pointer));
            bool found = false;
            foreach (FishType type in FishTypes)
            { IntPtr known = Read(type.NativeClass); if (known != IntPtr.Zero && known == native) { found = true; break; } }
            if (!found) throw new InvalidOperationException("Unknown native fish subtype is outside this quarantine profile.");
        }

        private bool Enter()
        {
            if (_busy || _nativeStep) { Fail("Fish quarantine validation reentered."); return false; }
            if (Failed) return false;
            _busy = true; _steps = 0;
            try { RequireConfirmedThread(); return true; }
            catch (Exception error) { Fail("Fish quarantine thread is unconfirmed: " + error.GetType().Name); Exit(); return false; }
        }
        private void Exit() { _nativeStep = false; _busy = false; }
        private bool GenerationCurrent()
        {
            RequireConfirmedThread();
            if (!ReferenceEquals(NativeGuestInitializationController.Current, _source) || Failed)
                throw new InvalidOperationException("Fish quarantine lost its actual startup source.");
            return _scenes.VerifyGenerationWindow();
        }
        private void RequireConfirmedThread()
        {
            if (_source.ConfirmedUnityThreadId <= 0 || Environment.CurrentManagedThreadId != _source.ConfirmedUnityThreadId)
                throw new InvalidOperationException("Fish reads require the confirmed Unity Update thread.");
        }
        private long ProbeRetainedPointer(Il2CppObjectBase instance)
        {
            // Only the wrapper's strong-handle target is read. No Unity object,
            // scene, class store, source callback or new birth is inspected.
            // This survives a failed/retired source solely to suppress cleanup.
            RequireConfirmedThread();
            if (_identityProbe || !ReferenceEquals(NativeGuestInitializationController.Current, _source))
                throw new InvalidOperationException("Retained fish identity probe lost its fixed process source.");
            if (_busy && ++_steps > MaxNativeReadsPerCall)
                throw new InvalidOperationException("Retained fish identity probe exceeded its call bound.");
            _identityProbe = true; _totalReads++;
            try { return Pointer(instance); }
            finally { _identityProbe = false; }
        }
        private void RequireBirth(Record record)
        {
            RequireConfirmedThread();
            if (Failed || record == null || record.Retired || !record.Inert ||
                !_actors.TryGetValue(record.ActorPointer, out Record known) || !ReferenceEquals(known, record) ||
                !ReferenceEquals(NativeGuestInitializationController.Current, _source) || !_scenes.ValidateFishBirth(record.Birth))
                throw new InvalidOperationException("Retained fish birth is no longer current.");
        }
        private void Step(Record record)
        {
            RequireConfirmedThread();
            if (_nativeStep || Failed || !_busy || ++_steps > MaxNativeReadsPerCall)
                throw new InvalidOperationException("Fish native read window was lost or exceeded its bound.");
            if (record != null) RequireBirth(record);
            else if (!GenerationCurrent()) throw new InvalidOperationException("Fish native read has no actual entry window.");
        }
        private T Read<T>(Func<T> read, Record record = null)
        {
            Step(record); _nativeStep = true; _totalReads++;
            T value;
            try { value = read(); }
            finally { _nativeStep = false; }
            Step(record); return value;
        }
        private void Write(Action write, Record record)
        {
            Step(record); _nativeStep = true; _totalReads++;
            try { write(); }
            finally { _nativeStep = false; }
            Step(record);
        }
        private void Keep(Il2CppObjectBase value, Record record)
        {
            if (ReferenceEquals(value, null) || _handles.Count >= MaxHandles)
                throw new InvalidOperationException("Fish strong-reference bound exceeded.");
            // Keep the wrapper reachable before new native handle allocation.
            _references.Add(value);
            IntPtr pointer = Read(() => value.Pointer, record);
            Step(record); _nativeStep = true;
            IntPtr handle;
            try { handle = IL2CPP.il2cpp_gchandle_new(pointer, false); }
            finally { _nativeStep = false; }
            if (handle == IntPtr.Zero) throw new InvalidOperationException("Fish native reference retention failed.");
            _handles.Add(handle); // Store even if the post-allocation source guard fails.
            Step(record);
        }
        private void Fail(string reason)
        {
            _failed = true; Status = "Fish quarantine unavailable; native roots and patches retained until process exit.";
            try { _source.Fail(reason); } catch { }
            Log("BLOCKED", reason);
        }
        private void Log(string stage, string text)
        {
            string key = stage + ": " + text;
            if (_logs >= MaxLogs || key == _lastLog) return;
            _logs++; _lastLog = key;
            try { _logger.LogInfo("DAVECOOP_GUEST_FISH_QUARANTINE_" + stage + ": " + text); }
            catch { _failed = true; }
        }
        private static long Pointer(Il2CppObjectBase value) => ReferenceEquals(value, null) ? 0 : value.Pointer.ToInt64();

        private static Dictionary<MethodInfo, MethodInfo> CreateTargets()
        {
            var targets = new Dictionary<MethodInfo, MethodInfo>();
            Type[] actual = typeof(FishAISystem).Assembly.GetTypes().Where(type => typeof(FishAISystem).IsAssignableFrom(type)).ToArray();
            if (actual.Length != FishTypes.Length || actual.Any(type => !FishTypes.Any(known => known.Managed == type)))
                throw new InvalidOperationException("Generated fish class coverage differs from the checked profile.");
            string[] fishVoid = { "Awake", "OnEnable", "OnDisable", "OnDestroy", "Start", "Update", "LateUpdate",
                "InitAfterDataLoad", "InitDamageSystem", "InitInteractionBody", "EnableCollider", "EnableDamager",
                "EnableBiteDamager", "OnDie", "DestroySelf", "ReadyForInteractionCollider", "OnSuccessPickUp", "LootDeadFishBody",
                "SuccessNetPickupFish", "SuccessNetDroneLiftFish", "SuccessBodyDroneLiftFish", "SuccessPickupFish",
                "SuccessLiftFishByDrone", "AddDropItemLootBoxWithPlus", "AddDropItem_Impl", "AddDropPlusItem_Impl",
                "HookedByProjectile", "WinFromProjectileinFight", "OnLoseFromProjectile", "StartHookingSequence",
                "OnStartRecallHarpoonWithHooked", "SetHPDamage", "SetHPDamageQTE", "SetTrueHPDamage", "OnDoAttack", "OnDamaged",
                "InitializeFlockFish", "EnableBehaviorTree", "InitFromOriginAI", "RequestManageUpdateByLOD", "InitSkillSystem" };
            foreach (FishType type in FishTypes)
            {
                AddNamed(targets, type.Managed, fishVoid, typeof(void), nameof(FishVoidBefore));
                AddNamed(targets, type.Managed, new[] { "OnTakeDamage", "CheckEnableCollsion" }, typeof(bool), nameof(FishBoolBefore));
                foreach (string name in new[] { "FishInitRoutine", "CoSleepMode", "UpdateAsMoveable", "FixedUpdateAsMoveable" })
                {
                    MethodInfo method = type.Managed.GetMethod(name, Flags, null, Type.EmptyTypes, null);
                    if (method == null) continue;
                    if (method.IsStatic || method.IsGenericMethod ||
                        (method.ReturnType != typeof(Il2CppSystem.Collections.IEnumerator) &&
                         method.ReturnType != typeof(Il2CppSystem.IObservable<UniRx.Unit>)))
                        throw new InvalidOperationException("Unexpected fish factory declaration.");
                    Add(targets, method, nameof(FishFactoryBefore));
                }
            }
            string[] childTypes = { "CatchableObject", "FishInteractionBody", "Damageable", "WeakDamageable",
                "BossGardonBodyDamager", "NPCDamager", "BodyDamager", "Damager", "DamagerAbility", "DamagerByCollision",
                "DamagerFromParentCollider", "DR.AI.DamagerDot" };
            string[] childVoid = { "Awake", "OnEnable", "OnDestroy", "Update", "FixedUpdate", "CheckDamagerAttack",
                "AttackCollisionCheck", "OnCollisionWithHarpoon", "HookedByProjectile", "StartHookingSequence",
                "OnTriggerEnter2D", "OnTriggerStay2D", "OnTriggerExit2D", "OnTriggerEnter_Imple", "OnCollisionEnter2D",
                "OnCollisionStay2D", "OnDie", "SuccessInteract", "SuccessSubInteract", "OnActivateCommand", "OnDeactivateCommand" };
            foreach (string name in childTypes)
            {
                Type type = typeof(FishAISystem).Assembly.GetType(name, true);
                if (!typeof(Component).IsAssignableFrom(type)) throw new InvalidOperationException("Fish child declaration is not a component.");
                AddNamed(targets, type, childVoid, typeof(void), nameof(ComponentVoidBefore));
                AddNamed(targets, type, new[] { "TakeDamage", "CheckAvailableInteraction", "CheckAvailableSubInteraction",
                    "IsPlayerInInteractionZone", "CheckEnableCollsion" }, typeof(bool), nameof(ComponentBoolBefore));
            }
            if (!targets.Keys.Any(method => method.DeclaringType == typeof(FishAISystem) && method.Name == "Awake") ||
                !targets.Keys.Any(method => method.DeclaringType == typeof(Damageable) && method.Name == "TakeDamage") ||
                !targets.Keys.Any(method => method.DeclaringType == typeof(FishInteractionBody) && method.Name == "SuccessInteract"))
                throw new InvalidOperationException("Required fish quarantine declaration is absent.");
            return targets;
        }
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private static void AddNamed(Dictionary<MethodInfo, MethodInfo> targets, Type type, string[] names, Type result, string before)
        {
            foreach (MethodInfo method in type.GetMethods(Flags).Where(method => names.Contains(method.Name)))
            {
                if (method.IsStatic || method.IsGenericMethod || method.ReturnType != result || method.GetParameters().Any(arg => arg.ParameterType.IsByRef))
                    throw new InvalidOperationException("Exact fish callback declaration differs from the checked profile.");
                Add(targets, method, before);
            }
        }
        private static void Add(Dictionary<MethodInfo, MethodInfo> targets, MethodInfo method, string before)
        {
            if (targets.ContainsKey(method)) throw new InvalidOperationException("Fish quarantine target was repeated.");
            targets.Add(method, typeof(NativeGuestFishQuarantine).GetMethod(before, BindingFlags.Static | BindingFlags.NonPublic));
        }
        private static bool FishVoidBefore(FishAISystem __instance, MethodBase __originalMethod) =>
            _active == null || _active.FishCallback(__instance, __originalMethod);
        private static bool FishBoolBefore(FishAISystem __instance, MethodBase __originalMethod, ref bool __result)
        { if (_active == null || _active.FishCallback(__instance, __originalMethod)) return true; __result = false; return false; }
        private static bool FishFactoryBefore(FishAISystem __instance, MethodBase __originalMethod)
        {
            if (_active == null || _active.FishCallback(__instance, __originalMethod)) return true;
            _active.Fail("A quarantined fish was asked to construct native AI work.");
            throw new InvalidOperationException("Native guest fish AI factory is blocked.");
        }
        private static bool ComponentVoidBefore(Component __instance, MethodBase __originalMethod) =>
            _active == null || _active.ComponentCallback(__instance, __originalMethod);
        private static bool ComponentBoolBefore(Component __instance, MethodBase __originalMethod, ref bool __result)
        { if (_active == null || _active.ComponentCallback(__instance, __originalMethod)) return true; __result = false; return false; }
    }
}
