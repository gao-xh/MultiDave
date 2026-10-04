using System;
using System.Collections.Generic;
using System.Threading;
using BepInEx.Logging;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.SceneManagement;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using UnityObject = UnityEngine.Object;

namespace DaveCoop.Networking
{
    // Host-created physical body only. No native PlayerCharacter, scripts,
    // input listeners, combat callbacks, renderer, LootBox or save root clone.
    // The driver is an explicit cooperative kinematic movement rule, not a
    // claim of equivalence to every original player movement/trigger rule.
    internal sealed class HostEmployeeActorBody : IDisposable
    {
        public const int MaxHits = 32, MaxBodiesPerProcess = 64, MaxReferences = 16, MaxSteps = 8192;
        private const float Skin = 0.005f, MaxFixedDelta = 0.1f;
        private static readonly List<HostEmployeeActorBody> Retained = new List<HostEmployeeActorBody>();
        private static int _bodyAttempts;
        private readonly LocalAvatarCapture _local;
        private readonly int _thread;
        private readonly ManualLogSource _logger;
        private readonly Func<bool> _ownerCurrent;
        private readonly List<Reference> _references = new List<Reference>();
        private Source _source;
        private GameObject _root;
        private Transform _weaponTransform;
        private Rigidbody2D _body;
        private Collider2D _collider;
        private Il2CppStructArray<RaycastHit2D> _hits;
        private Il2CppReferenceArray<Collider2D> _overlaps;
        private IntPtr _rootPointer, _rootUnity, _bodyPointer, _bodyUnity, _colliderPointer, _colliderUnity, _weaponPointer, _weaponUnity;
        private ContactFilter2D _filter;
        private long _revision, _epoch, _serial, _operationSerial;
        private string _sceneKey;
        private bool _busy, _failed, _stopped, _createEntered, _rootConstructionEntered, _activated, _cleanup, _stopEntered, _stopVerified, _destroyEntered, _observedDestroyed;
        private int _steps, _logs;
        public string Status { get; private set; } = "Empty";
        public bool Alive => _rootPointer != IntPtr.Zero && !_observedDestroyed;
        public bool Active => _activated && !_failed && !_stopped;
        public bool Failed => _failed;
        public bool CleanupVerified => _stopVerified;
        public long Moves { get; private set; }
        public long Blocked { get; private set; }
        public bool NativeAbiVerified => false;
        public bool PartialConstructorAllocationRetentionVerified => false;
        private enum Shape { Capsule, Box, Circle }
        private sealed class Reference { public Il2CppObjectBase Value; public IntPtr Handle; }
        private sealed class Source
        {
            public InGameManager Manager;
            public PlayerCharacter Player;
            public CharacterController2D Controller;
            public Rigidbody2D Body;
            public Collider2D Collider;
            public IntPtr ManagerPointer, ManagerUnity, PlayerPointer, PlayerUnity, ControllerPointer, ControllerUnity;
            public IntPtr BodyPointer, BodyUnity, ColliderPointer, ColliderUnity, ColliderClass;
            public Scene Scene;
            public PhysicsScene2D Physics;
            public int SceneHandle, Layer, Mask;
            public Shape Kind;
            public Vector3 PlayerPosition, ColliderPosition, Scale;
            public Quaternion Rotation;
            public Vector2 Size, Offset;
            public float Radius, EdgeRadius, Angle;
            public CapsuleDirection2D Direction;
        }

        public HostEmployeeActorBody(LocalAvatarCapture local, int unityThreadId, ManualLogSource logger, Func<bool> ownerCurrent = null)
        {
            _local = local ?? throw new ArgumentNullException(nameof(local));
            if (unityThreadId < 1) throw new ArgumentOutOfRangeException(nameof(unityThreadId));
            _thread = unityThreadId; _logger = logger; _ownerCurrent = ownerCurrent;
        }

        public bool TryCreate(long actorRevision, long sceneEpoch, string sceneKey)
        {
            if (_createEntered || actorRevision < 1 || sceneEpoch < 1 || string.IsNullOrEmpty(sceneKey) || sceneKey.Length > 256) return false;
            if (!Begin()) return false;
            try
            {
                _createEntered = true; _revision = actorRevision; _epoch = sceneEpoch; _sceneKey = sceneKey;
                if (Interlocked.Increment(ref _bodyAttempts) > MaxBodiesPerProcess) throw Invalid();
                lock (Retained) Retained.Add(this); // Retain before any possible native allocation.
                _source = CaptureSource();
                if (!SameSource(_source, CaptureSource())) throw Invalid();
                Keep(_source.Manager); Keep(_source.Player); Keep(_source.Controller); Keep(_source.Body); Keep(_source.Collider);
                LayerMask mask = Read(() => (LayerMask)_source.Mask);
                _filter = new ContactFilter2D { useTriggers = false, useLayerMask = true, layerMask = mask };
                _hits = Read(() => { var array = new Il2CppStructArray<RaycastHit2D>(MaxHits); Retain(array); return array; });
                Keep(_hits);
                _overlaps = Read(() => { var array = new Il2CppReferenceArray<Collider2D>(MaxHits); Retain(array); return array; });
                Keep(_overlaps);
                // Unity's empty GameObject constructor starts active. It has no
                // added behaviour/physics; retain it and immediately deactivate
                // before adding any body or collider. Constructor-throw allocation
                // retention is not proven by the generated wrapper.
                _rootConstructionEntered = true;
                _root = Read(() => { _root = new GameObject("MultiDave Employee Body"); Retain(_root); return _root; });
                Keep(_root); Freeze(_root, out _rootPointer, out _rootUnity);
                Write(() => _root.SetActive(false));
                Write(() => SceneManager.MoveGameObjectToScene(_root, _source.Scene));
                Transform transform = Read(() => _root.transform);
                _weaponTransform = transform; Keep(_weaponTransform); Freeze(_weaponTransform, out _weaponPointer, out _weaponUnity);
                Write(() => transform.rotation = _source.Rotation);
                Write(() => transform.localScale = _source.Scale);
                Write(() => _root.layer = _source.Layer);
                _body = Read(() => { var body = _root.AddComponent<Rigidbody2D>(); Retain(body); return body; });
                Keep(_body); Freeze(_body, out _bodyPointer, out _bodyUnity);
                Write(() => _body.simulated = false);
                Write(() => _body.bodyType = RigidbodyType2D.Kinematic);
                Write(() => _body.gravityScale = 0);
                Write(() => _body.constraints = RigidbodyConstraints2D.FreezeRotation);
                Write(() => _body.useFullKinematicContacts = false);
                Write(() => _body.linearVelocity = default);
                _collider = Read(CreateCollider); Keep(_collider); Freeze(_collider, out _colliderPointer, out _colliderUnity);
                ConfigureCollider();
                Vector3 spawn = FindSpawn();
                Write(() => transform.position = spawn);
                Write(() => _body.position = V2(spawn.x, spawn.y));
                ValidateSource(); ValidateOwned(false);
                Write(() => _body.simulated = true);
                Write(() => _root.SetActive(true));
                _activated = true;
                ValidateSource(); ValidateOwned(true);
                Status = "Active"; return true;
            }
            catch (NotSupportedException) { Fault("UnsupportedTemplate"); return false; }
            catch (Exception) { Fault("CreateFailed"); return false; }
            finally { End(); }
        }

        public bool IsCurrent(long actorRevision, long sceneEpoch, string sceneKey)
        {
            if (!Active || actorRevision != _revision || sceneEpoch != _epoch || sceneKey != _sceneKey || !Begin()) return false;
            try { ValidateSource(); ValidateOwned(true); ValidateSource(); return true; }
            catch (Exception) { Fault("SourceExpired"); return false; }
            finally { End(); }
        }

        public bool TryReadPosition(out NVector3 position) => TryReadMotion(out position, out _);

        // Own actor/scene for an independently created Mod projectile. Neither
        // the observed guest pose nor the original host weapon supplies this.
        public bool TryReadWeaponScene(out Scene actualScene, out int collisionMask, out Transform ownedActor)
        {
            actualScene = default; collisionMask = 0; ownedActor = null;
            if (!Active || !Begin()) return false;
            try
            {
                ValidateSource(); ValidateOwned(true);
                ValidateOwnedIdentity(_weaponTransform, _weaponPointer, _weaponUnity);
                Transform current = Read(() => _root.transform);
                if (ReferenceEquals(current, null) || Read(() => current.Pointer) != _weaponPointer ||
                    Read(() => current.m_CachedPtr) != _weaponUnity) throw Invalid();
                Scene scene = Read(() => _root.scene);
                if (Read(() => scene.handle) != _source.SceneHandle || !Read(() => scene.IsValid()) || !Read(() => scene.isLoaded)) throw Invalid();
                ValidateOwned(true); ValidateSource();
                actualScene = scene; collisionMask = _source.Mask; ownedActor = _weaponTransform; return true;
            }
            catch (Exception) { Fault("WeaponSourceReadFailed"); return false; }
            finally { End(); }
        }

        public bool TryReadMotion(out NVector3 position, out NVector2 velocity)
        {
            position = default; velocity = default;
            if (!Active || !Begin()) return false;
            try
            {
                ValidateSource(); ValidateOwned(true);
                Vector2 p = Read(() => _body.position), v = Read(() => _body.linearVelocity);
                Transform transform = Read(() => _root.transform);
                Vector3 rootPosition = Read(() => transform.position); float z = rootPosition.z;
                ValidateOwned(true); ValidateSource();
                if (!Finite(p) || !Finite(v) || !float.IsFinite(z) || z != _source.PlayerPosition.z) throw Invalid();
                position = new NVector3(p.x, p.y, z); velocity = new NVector2(v.x, v.y); return true;
            }
            catch (Exception) { Fault("MotionReadFailed"); return false; }
            finally { End(); }
        }

        // Returns command acceptance, not predicted or completed physics state.
        // Every step starts from actual rb.position. A blocked command submits
        // only the conservative remaining distance, never a client pose.
        public bool TryMove(NVector2 velocity, float dt)
        {
            if (!Active || !float.IsFinite(velocity.X) || !float.IsFinite(velocity.Y) || !float.IsFinite(dt) || dt <= 0 || dt > MaxFixedDelta) return false;
            double dx = (double)velocity.X * dt, dy = (double)velocity.Y * dt, length = Math.Sqrt(dx * dx + dy * dy);
            if (!double.IsFinite(length) || length > 10 || !Begin()) return false;
            try
            {
                ValidateSource(); ValidateOwned(true);
                Vector2 start = Read(() => _body.position);
                if (!Finite(start)) throw Invalid();
                double allowed = length;
                if (length > 0)
                {
                    Vector2 direction = V2((float)(dx / length), (float)(dy / length));
                    int count = Read(() => _collider.Cast(direction, _filter, _hits, (float)length + Skin, true));
                    if (count < 0 || count >= MaxHits) throw Invalid(); // Full output may conceal more hits.
                    for (int i = 0; i < count; i++)
                    {
                        RaycastHit2D hit = Read(() => _hits[i]);
                        if (hit.m_Collider == 0 || !float.IsFinite(hit.m_Distance) || hit.m_Distance < 0) throw Invalid();
                        allowed = Math.Min(allowed, Math.Max(0, (double)hit.m_Distance - Skin));
                    }
                }
                if (allowed < length) Blocked++;
                Vector2 target = length == 0 ? start : V2(start.x + (float)(dx * allowed / length), start.y + (float)(dy * allowed / length));
                if (!Finite(target)) throw Invalid();
                ValidateOwned(true); ValidateSource();
                Write(() => _body.MovePosition(target)); // Exactly one dispatch; failures are not retried.
                ValidateOwned(true); ValidateSource(); Moves++; Status = "Active"; return true;
            }
            catch (Exception) { Fault("MovementUnknown"); return false; }
            finally { End(); }
        }

        public void Stop(string reason = "Stopped")
        {
            _stopped = true; _activated = false;
            if (_busy) { Fault("ReentrantStop"); return; }
            if (_stopEntered) return;
            if (!Begin(true)) return;
            _stopEntered = true;
            try
            {
                if (_rootPointer == IntPtr.Zero)
                {
                    if (!ReferenceEquals(_root, null)) Freeze(_root, out _rootPointer, out _rootUnity);
                    else
                    {
                        _stopVerified = !_rootConstructionEntered;
                        Status = _stopVerified ? "StoppedBeforeRoot" : "RootConstructionUnknownRetained";
                        return;
                    }
                }
                if (Read(() => _root.m_CachedPtr) == IntPtr.Zero) { _observedDestroyed = true; _stopVerified = true; Status = "OwnedRootDestroyed"; return; }
                ValidateOwnedIdentity(_root, _rootPointer, _rootUnity);
                if (_bodyPointer != IntPtr.Zero)
                {
                    ValidateOwnedIdentity(_body, _bodyPointer, _bodyUnity);
                    Read(() => { _body.simulated = false; return true; });
                    if (Read(() => _body.simulated)) throw Invalid();
                }
                Read(() => { _root.SetActive(false); return true; });
                ValidateOwnedIdentity(_root, _rootPointer, _rootUnity);
                if (Read(() => _root.activeSelf)) throw Invalid();
                _stopVerified = true; if (!_failed) Status = "Stopped";
            }
            catch (Exception) { Fault("StopUnknownRetained"); }
            finally { _cleanup = false; _busy = false; }
            if (_logs++ < 2) { try { _logger?.LogInfo("CREW_BODY status=" + Status + " retained=true nativeAbiVerified=false"); } catch (Exception) { } }
        }

        public void Dispose()
        {
            Stop();
            if (!_stopVerified || _destroyEntered || _busy || _rootPointer == IntPtr.Zero || !Begin(true)) return;
            _destroyEntered = true;
            try
            {
                if (Read(() => _root.m_CachedPtr) == IntPtr.Zero) { _observedDestroyed = true; return; }
                ValidateOwnedIdentity(_root, _rootPointer, _rootUnity);
                Read(() => { UnityObject.Destroy(_root); return true; });
                Status = "DestroyRequestedRetained"; // Destroy is deferred; keep all evidence/handles.
            }
            catch (Exception) { Fault("DestroyUnknownRetained"); }
            finally { _cleanup = false; _busy = false; }
        }

        private Source CaptureSource()
        {
            var s = new Source { Manager = _local.Manager, Player = _local.Player };
            Freeze(s.Manager, out s.ManagerPointer, out s.ManagerUnity); Freeze(s.Player, out s.PlayerPointer, out s.PlayerUnity);
            if (!Read(() => s.Manager.IsLoadedAll) || Read(() => s.Player.GetInstanceID()) != _local.PlayerId) throw Invalid();
            PlayerCharacter owner = Read(() => s.Manager._playerCharacter_k__BackingField);
            if (ReferenceEquals(owner, null) || Read(() => owner.Pointer) != s.PlayerPointer || Read(() => owner.m_CachedPtr) != s.PlayerUnity) throw Invalid();
            GameObject playerRoot = Read(() => s.Player.gameObject);
            if (ReferenceEquals(playerRoot, null) || !Read(() => playerRoot.activeInHierarchy)) throw Invalid();
            s.Scene = Read(() => playerRoot.scene); s.SceneHandle = s.Scene.handle;
            if (s.SceneHandle == 0 || s.SceneHandle != _local.SceneHandle || !Read(() => s.Scene.IsValid()) || !Read(() => s.Scene.isLoaded) || Read(() => s.Scene.name) != _sceneKey) throw Invalid();
            s.Physics = Read(() => s.Scene.GetPhysicsScene2D()); if (!Read(() => s.Physics.IsValid())) throw Invalid();
            Transform playerTransform = Read(() => s.Player.transform);
            if (ReferenceEquals(playerTransform, null)) throw Invalid();
            s.PlayerPosition = Read(() => playerTransform.position);
            s.Controller = Read(() => s.Player._Controller2D_k__BackingField); Freeze(s.Controller, out s.ControllerPointer, out s.ControllerUnity);
            s.Body = Read(() => s.Controller.m_Rigidbody); Freeze(s.Body, out s.BodyPointer, out s.BodyUnity);
            s.Collider = Read(() => s.Controller.m_CharacterCollider); Freeze(s.Collider, out s.ColliderPointer, out s.ColliderUnity);
            s.ColliderClass = Read(() => IL2CPP.il2cpp_object_get_class(s.ColliderPointer));
            if (s.ColliderClass == IntPtr.Zero) throw Invalid();
            if (s.ColliderClass == Read(() => Il2CppClassPointerStore<CapsuleCollider2D>.NativeClassPtr)) s.Kind = Shape.Capsule;
            else if (s.ColliderClass == Read(() => Il2CppClassPointerStore<BoxCollider2D>.NativeClassPtr)) s.Kind = Shape.Box;
            else if (s.ColliderClass == Read(() => Il2CppClassPointerStore<CircleCollider2D>.NativeClassPtr)) s.Kind = Shape.Circle;
            else throw new NotSupportedException("Employee collider template class unavailable.");
            GameObject colliderRoot = Read(() => s.Collider.gameObject);
            // The original collider supplies shape, not a promise that its
            // trigger/enabled policy matches this new cooperative body. The
            // owned body is explicitly non-trigger and performs its own sweep.
            if (Read(() => colliderRoot.scene.handle) != s.SceneHandle || Read(() => s.Collider.usedByComposite)) throw Invalid();
            Rigidbody2D attached = Read(() => s.Collider.attachedRigidbody);
            if (ReferenceEquals(attached, null) || Read(() => attached.Pointer) != s.BodyPointer || Read(() => attached.m_CachedPtr) != s.BodyUnity) throw Invalid();
            Transform transform = Read(() => s.Collider.transform);
            s.ColliderPosition = Read(() => transform.position); s.Scale = Read(() => transform.lossyScale); s.Rotation = Read(() => transform.rotation);
            Matrix4x4 matrix = Read(() => transform.localToWorldMatrix);
            if (!Finite(s.PlayerPosition) || !Finite(s.ColliderPosition) || !Finite(s.Scale) || Math.Abs(s.Scale.x) < 0.0001f || Math.Abs(s.Scale.y) < 0.0001f ||
                !Planar(s.Rotation, s.Scale, matrix) || s.ColliderPosition.z != s.PlayerPosition.z) throw Invalid();
            s.Angle = (float)(Math.Atan2(2.0 * s.Rotation.w * s.Rotation.z, 1.0 - 2.0 * s.Rotation.z * s.Rotation.z) * 180.0 / Math.PI);
            s.Offset = Read(() => s.Collider.offset);
            if (s.Kind == Shape.Capsule)
            {
                var c = Read(() => new CapsuleCollider2D(s.ColliderPointer)); s.Size = Read(() => c.size); s.Direction = Read(() => c.direction);
                if (s.Direction != CapsuleDirection2D.Vertical && s.Direction != CapsuleDirection2D.Horizontal) throw Invalid();
            }
            else if (s.Kind == Shape.Box)
            {
                var c = Read(() => new BoxCollider2D(s.ColliderPointer)); s.Size = Read(() => c.size); s.EdgeRadius = Read(() => c.edgeRadius);
                if (Read(() => c.autoTiling) || !float.IsFinite(s.EdgeRadius) || s.EdgeRadius < 0) throw Invalid();
            }
            else
            {
                var c = Read(() => new CircleCollider2D(s.ColliderPointer)); s.Radius = Read(() => c.radius);
                if (!float.IsFinite(s.Radius) || s.Radius <= 0 || Math.Abs(s.Scale.x) != Math.Abs(s.Scale.y)) throw Invalid();
                s.Size = V2(s.Radius * 2, s.Radius * 2);
            }
            if (!Finite(s.Size) || s.Size.x <= 0 || s.Size.y <= 0 || !Finite(s.Offset)) throw Invalid();
            s.Layer = Read(() => colliderRoot.layer); if (s.Layer < 0 || s.Layer > 31) throw Invalid();
            s.Mask = Read(() => Physics2D.GetLayerCollisionMask(s.Layer)); if (s.Mask == 0) throw Invalid();
            if (!ReferenceEquals(_local.Manager, s.Manager) || !ReferenceEquals(_local.Player, s.Player) ||
                !Read(() => s.Manager.IsLoadedAll) || !Read(() => playerRoot.activeInHierarchy) || Read(() => s.Player.GetInstanceID()) != _local.PlayerId) throw Invalid();
            return s;
        }

        private Collider2D CreateCollider()
        {
            Collider2D value = _source.Kind == Shape.Capsule ? _root.AddComponent<CapsuleCollider2D>() :
                _source.Kind == Shape.Box ? (Collider2D)_root.AddComponent<BoxCollider2D>() : _root.AddComponent<CircleCollider2D>();
            Retain(value); return value;
        }

        private Vector2 EffectiveOffset()
        {
            double radians = _source.Angle * Math.PI / 180.0, x = _source.ColliderPosition.x - _source.PlayerPosition.x, y = _source.ColliderPosition.y - _source.PlayerPosition.y;
            return V2(_source.Offset.x + (float)((Math.Cos(radians) * x + Math.Sin(radians) * y) / _source.Scale.x),
                _source.Offset.y + (float)((-Math.Sin(radians) * x + Math.Cos(radians) * y) / _source.Scale.y));
        }

        private void ConfigureCollider()
        {
            Vector2 offset = EffectiveOffset(); if (!Finite(offset)) throw Invalid();
            Write(() => _collider.offset = offset); Write(() => _collider.isTrigger = false);
            if (_source.Kind == Shape.Capsule)
            {
                var c = Read(() => new CapsuleCollider2D(_colliderPointer)); Write(() => c.size = _source.Size); Write(() => c.direction = _source.Direction);
            }
            else if (_source.Kind == Shape.Box)
            {
                var c = Read(() => new BoxCollider2D(_colliderPointer)); Write(() => c.size = _source.Size); Write(() => c.edgeRadius = _source.EdgeRadius);
            }
            else { var c = Read(() => new CircleCollider2D(_colliderPointer)); Write(() => c.radius = _source.Radius); }
        }

        private Vector3 FindSpawn()
        {
            Vector2 size = V2(_source.Size.x * Math.Abs(_source.Scale.x), _source.Size.y * Math.Abs(_source.Scale.y));
            if (!Finite(size) || size.x > 20 || size.y > 20) throw Invalid();
            double radians = _source.Angle * Math.PI / 180.0; Vector2 offset = EffectiveOffset();
            Vector2 worldOffset = V2((float)(Math.Cos(radians) * offset.x * _source.Scale.x - Math.Sin(radians) * offset.y * _source.Scale.y),
                (float)(Math.Sin(radians) * offset.x * _source.Scale.x + Math.Cos(radians) * offset.y * _source.Scale.y));
            float spacing = Math.Max(size.x, size.y) + 0.25f;
            Vector2[] candidates = { V2(spacing, 0), V2(-spacing, 0), V2(0, spacing), V2(0, -spacing) };
            foreach (Vector2 candidate in candidates)
            {
                Vector2 position = V2(_source.PlayerPosition.x + candidate.x, _source.PlayerPosition.y + candidate.y), center = V2(position.x + worldOffset.x, position.y + worldOffset.y);
                if (!Finite(position) || !Finite(center)) throw Invalid();
                int count = _source.Kind == Shape.Capsule ? Read(() => _source.Physics.OverlapCapsule(center, size, _source.Direction, _source.Angle, _filter, _overlaps)) :
                    _source.Kind == Shape.Box ? Read(() => _source.Physics.OverlapBox(center, size, _source.Angle, _filter, _overlaps)) :
                    Read(() => _source.Physics.OverlapCircle(center, _source.Radius * Math.Abs(_source.Scale.x), _filter, _overlaps));
                if (count < 0 || count >= MaxHits) throw Invalid();
                if (count == 0) return V3(position.x, position.y, _source.PlayerPosition.z);
            }
            throw Invalid();
        }

        private void ValidateSource() { if (_source == null || !SameSource(_source, CaptureSource())) throw Invalid(); }
        private static bool SameSource(Source a, Source b) => ReferenceEquals(a.Manager, b.Manager) && ReferenceEquals(a.Player, b.Player) &&
            a.ManagerPointer == b.ManagerPointer && a.ManagerUnity == b.ManagerUnity && a.PlayerPointer == b.PlayerPointer && a.PlayerUnity == b.PlayerUnity &&
            a.ControllerPointer == b.ControllerPointer && a.ControllerUnity == b.ControllerUnity && a.BodyPointer == b.BodyPointer && a.BodyUnity == b.BodyUnity &&
            a.ColliderPointer == b.ColliderPointer && a.ColliderUnity == b.ColliderUnity && a.ColliderClass == b.ColliderClass && a.SceneHandle == b.SceneHandle &&
            a.Layer == b.Layer && a.Mask == b.Mask && a.Kind == b.Kind && Math.Abs(a.Scale.x) == Math.Abs(b.Scale.x) && Math.Abs(a.Scale.y) == Math.Abs(b.Scale.y) && Equal(a.Size, b.Size) &&
            Equal(a.Offset, b.Offset) && a.Radius == b.Radius && a.EdgeRadius == b.EdgeRadius && a.Direction == b.Direction && a.PlayerPosition.z == b.PlayerPosition.z;

        private void ValidateOwned(bool active)
        {
            ValidateOwnedIdentity(_root, _rootPointer, _rootUnity); ValidateOwnedIdentity(_body, _bodyPointer, _bodyUnity); ValidateOwnedIdentity(_collider, _colliderPointer, _colliderUnity);
            GameObject bodyRoot = Read(() => _body.gameObject), colliderRoot = Read(() => _collider.gameObject);
            if (ReferenceEquals(bodyRoot, null) || ReferenceEquals(colliderRoot, null) || Read(() => bodyRoot.Pointer) != _rootPointer || Read(() => bodyRoot.m_CachedPtr) != _rootUnity ||
                Read(() => colliderRoot.Pointer) != _rootPointer || Read(() => colliderRoot.m_CachedPtr) != _rootUnity) throw Invalid();
            IntPtr expectedClass = _source.Kind == Shape.Capsule ? Read(() => Il2CppClassPointerStore<CapsuleCollider2D>.NativeClassPtr) :
                _source.Kind == Shape.Box ? Read(() => Il2CppClassPointerStore<BoxCollider2D>.NativeClassPtr) : Read(() => Il2CppClassPointerStore<CircleCollider2D>.NativeClassPtr);
            if (expectedClass == IntPtr.Zero || Read(() => IL2CPP.il2cpp_object_get_class(_colliderPointer)) != expectedClass) throw Invalid();
            if (Read(() => _root.scene.handle) != _source.SceneHandle || Read(() => _root.layer) != _source.Layer || Read(() => _body.bodyType) != RigidbodyType2D.Kinematic ||
                Read(() => _body.gravityScale) != 0 || Read(() => _body.constraints) != RigidbodyConstraints2D.FreezeRotation || Read(() => _body.useFullKinematicContacts) ||
                Read(() => _collider.isTrigger) || Read(() => _collider.usedByComposite) || !Read(() => _collider.enabled) ||
                (active && (!Read(() => _root.activeInHierarchy) || !Read(() => _body.simulated)))) throw Invalid();
            Transform transform = Read(() => _root.transform);
            Vector3 scale = Read(() => transform.lossyScale);
            Quaternion rotation = Read(() => transform.rotation);
            Matrix4x4 matrix = Read(() => transform.localToWorldMatrix);
            if (scale.x != _source.Scale.x || scale.y != _source.Scale.y || scale.z != _source.Scale.z || !Planar(rotation, scale, matrix) ||
                Math.Abs((1 - 2.0 * rotation.z * rotation.z) - (1 - 2.0 * _source.Rotation.z * _source.Rotation.z)) > 0.0001 ||
                Math.Abs(2.0 * rotation.w * rotation.z - 2.0 * _source.Rotation.w * _source.Rotation.z) > 0.0001 || !Equal(Read(() => _collider.offset), EffectiveOffset())) throw Invalid();
            if (_source.Kind == Shape.Capsule)
            {
                var c = Read(() => new CapsuleCollider2D(_colliderPointer));
                if (!Equal(Read(() => c.size), _source.Size) || Read(() => c.direction) != _source.Direction) throw Invalid();
            }
            else if (_source.Kind == Shape.Box)
            {
                var c = Read(() => new BoxCollider2D(_colliderPointer));
                if (!Equal(Read(() => c.size), _source.Size) || Read(() => c.edgeRadius) != _source.EdgeRadius || Read(() => c.autoTiling)) throw Invalid();
            }
            else { var c = Read(() => new CircleCollider2D(_colliderPointer)); if (Read(() => c.radius) != _source.Radius) throw Invalid(); }
            Rigidbody2D attached = Read(() => _collider.attachedRigidbody);
            if (ReferenceEquals(attached, null) || Read(() => attached.Pointer) != _bodyPointer || Read(() => attached.m_CachedPtr) != _bodyUnity) throw Invalid();
        }

        private void ValidateOwnedIdentity(UnityObject value, IntPtr pointer, IntPtr unity)
        { if (ReferenceEquals(value, null) || pointer == IntPtr.Zero || unity == IntPtr.Zero || Read(() => value.Pointer) != pointer || Read(() => value.m_CachedPtr) != unity) throw Invalid(); }
        private void Freeze(UnityObject value, out IntPtr pointer, out IntPtr unity)
        {
            if (ReferenceEquals(value, null)) throw Invalid(); pointer = Read(() => value.Pointer); unity = Read(() => value.m_CachedPtr);
            if (pointer == IntPtr.Zero || unity == IntPtr.Zero) throw Invalid();
        }

        private void Retain(Il2CppObjectBase value)
        {
            if (ReferenceEquals(value, null) || _references.Count >= MaxReferences) throw Invalid();
            foreach (Reference reference in _references) if (ReferenceEquals(reference.Value, value)) return;
            _references.Add(new Reference { Value = value });
        }
        private void Keep(Il2CppObjectBase value)
        {
            Retain(value); Reference owned = null;
            foreach (Reference reference in _references) if (ReferenceEquals(reference.Value, value)) { owned = reference; break; }
            if (owned == null || owned.Handle != IntPtr.Zero) return;
            IntPtr pointer = Read(() => value.Pointer); if (pointer == IntPtr.Zero) throw Invalid();
            Read(() => { owned.Handle = IL2CPP.il2cpp_gchandle_new(pointer, false); return owned.Handle; });
            if (owned.Handle == IntPtr.Zero) throw Invalid(); // Never free while outcome/source is unresolved.
        }

        private bool Begin(bool cleanup = false)
        {
            if (Environment.CurrentManagedThreadId != _thread) { Fault("WrongThread"); return false; }
            if (_busy) { Fault("ReentrantBodyCall"); return false; }
            if (!cleanup && (_failed || _stopped)) return false;
            _busy = true; _cleanup = cleanup; _steps = 0; _operationSerial = Interlocked.Read(ref _serial); return true;
        }
        private void Check()
        {
            if (!_busy || Environment.CurrentManagedThreadId != _thread || Interlocked.Read(ref _serial) != _operationSerial || (!_cleanup && (_failed || _stopped)) || ++_steps > MaxSteps) throw Invalid();
            if (!_cleanup && _ownerCurrent != null && !_ownerCurrent()) throw Invalid();
            if (Interlocked.Read(ref _serial) != _operationSerial) throw Invalid();
            if (!_cleanup && _source != null && (!ReferenceEquals(_local.Manager, _source.Manager) || !ReferenceEquals(_local.Player, _source.Player))) throw Invalid();
        }
        private T Read<T>(Func<T> read) { Check(); T value = read(); Check(); return value; }
        private void Write(Action write)
        {
            ValidateSource(); if (_rootPointer != IntPtr.Zero) ValidateOwnedIdentity(_root, _rootPointer, _rootUnity);
            Read(() => { write(); return true; }); ValidateSource();
        }
        private void End() { _cleanup = false; _busy = false; if (_failed && !_stopEntered) Stop(); }
        private void Fault(string reason) { Interlocked.Increment(ref _serial); _failed = true; _stopped = true; _activated = false; Status = reason; }
        private static InvalidOperationException Invalid() => new InvalidOperationException("Employee physical body proof unavailable.");
        // Literal field construction avoids the generated native Vector ctor/operators.
        private static Vector2 V2(float x, float y) => new Vector2 { x = x, y = y };
        private static Vector3 V3(float x, float y, float z) => new Vector3 { x = x, y = y, z = z };
        private static bool Finite(Vector2 v) => float.IsFinite(v.x) && float.IsFinite(v.y);
        private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
        private static bool Equal(Vector2 a, Vector2 b) => a.x == b.x && a.y == b.y;
        private static bool Planar(Quaternion q, Vector3 scale, Matrix4x4 m)
        {
            if (!float.IsFinite(q.x) || !float.IsFinite(q.y) || !float.IsFinite(q.z) || !float.IsFinite(q.w) || Math.Abs(q.x) > 0.00001f || Math.Abs(q.y) > 0.00001f) return false;
            if (Math.Abs((double)q.x * q.x + (double)q.y * q.y + (double)q.z * q.z + (double)q.w * q.w - 1) > 0.0001) return false;
            double c = 1 - 2.0 * q.z * q.z, s = 2.0 * q.w * q.z;
            return Math.Abs(m.m00 - c * scale.x) <= 0.0001 && Math.Abs(m.m10 - s * scale.x) <= 0.0001 &&
                Math.Abs(m.m01 + s * scale.y) <= 0.0001 && Math.Abs(m.m11 - c * scale.y) <= 0.0001 && Math.Abs(m.m20) <= 0.00001 && Math.Abs(m.m21) <= 0.00001;
        }
    }
}
