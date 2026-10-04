using System;
using System.Collections.Generic;
using System.Threading;
using BepInEx.Logging;
using DaveCoop.Core.Crew;
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
    // Synchronous native collision carrier. Only its owner's private minted
    // instance passes ValidateHit; neither this wrapper nor its pointers go on wire.
    internal sealed class HostEmployeeHarpoonHit
    {
        public HostEmployeeHarpoonProjectile Owner { get; }
        public CrewHarpoonCommand Command => Owner.Command;
        public Collider2D Collider { get; }
        public NVector3 Point { get; }
        public NVector2 Direction { get; }
        public long ShotId => Command.ShotId;
        public long ActorRevision => Command.ActorRevision;
        public long SceneEpoch => Command.SceneEpoch;
        public string SceneKey => Command.SceneKey;
        public int SceneHandle => Owner.SceneHandle;
        internal HostEmployeeHarpoonHit(HostEmployeeHarpoonProjectile owner, Collider2D collider, NVector3 point, NVector2 direction)
        { Owner = owner; Collider = collider; Point = point; Direction = direction; }
    }

    // One host-approved shot, from actual body readback. No native Projectile,
    // player/input/weapon handler, damage script or LootBox is cloned or invoked.
    // The CircleCast is scene-specific and precedes each own transform write.
    internal sealed class HostEmployeeHarpoonProjectile : IDisposable
    {
        public const int MaxHits = 32, MaxReferences = 8, MaxSteps = 8192, MaxShotsPerProcess = 4096;
        private static readonly List<HostEmployeeHarpoonProjectile> Retained = new List<HostEmployeeHarpoonProjectile>();
        private static int _shotAttempts;
        private readonly HostEmployeeActorBody _body;
        private readonly Scene _scene;
        private readonly int _thread;
        private readonly Func<bool> _sourceCurrent;
        private readonly ManualLogSource _logger;
        private readonly List<Reference> _references = new List<Reference>();
        private GameObject _root;
        private Transform _transform, _actor;
        private LineRenderer _line;
        private PhysicsScene2D _physics;
        private ContactFilter2D _filter;
        private Il2CppStructArray<RaycastHit2D> _hits;
        private HostEmployeeHarpoonHit _hit;
        private IntPtr _rootPointer, _rootUnity, _transformPointer, _transformUnity, _actorPointer, _actorUnity;
        private IntPtr _actorRootPointer, _actorRootUnity, _linePointer, _lineUnity;
        private IntPtr _hitPointer, _hitUnity, _hitClass, _hitRootPointer, _hitRootUnity;
        private int _mask, _hitId, _steps, _logs;
        private long _serial, _operationSerial;
        private float _speed, _radius, _range, _z;
        private double _travelled;
        private Vector3 _lastPosition;
        private NVector2 _direction;
        private bool _busy, _cleanup, _failed, _stopped, _fireAttempted, _rootConstructionEntered;
        private bool _fired, _flying, _stopEntered, _stopVerified, _destroyEntered, _observedDestroyed;
        public CrewHarpoonCommand Command { get; }
        public long ShotId => Command.ShotId;
        public long ActorRevision => Command.ActorRevision;
        public long SceneEpoch => Command.SceneEpoch;
        public string SceneKey => Command.SceneKey;
        public int SceneHandle => _scene.m_Handle;
        public bool Alive => _rootPointer != IntPtr.Zero && !_observedDestroyed;
        public bool Active => _flying && !_failed && !_stopped;
        public bool Failed => _failed;
        public bool CleanupVerified => _stopVerified;
        public bool NativeAbiVerified => false;
        public bool PartialConstructorAllocationRetentionVerified => false;
        public string Status { get; private set; } = "NotFired";
        public long Sweeps { get; private set; }
        public long Moves { get; private set; }
        public long Hits { get; private set; }
        public double TravelledDistance => _travelled;
        private sealed class Reference { public Il2CppObjectBase Value; public IntPtr Handle; }
        private sealed class Collision
        {
            public Collider2D Collider;
            public IntPtr Pointer, Unity, Class, RootPointer, RootUnity;
            public int Id;
            public float Distance;
            public Vector2 Point;
        }

        public HostEmployeeHarpoonProjectile(HostEmployeeActorBody body, Scene actualScene, CrewHarpoonCommand command,
            int unityThreadId, Func<bool> sourceCurrent, ManualLogSource logger)
        {
            _body = body ?? throw new ArgumentNullException(nameof(body));
            Command = command ?? throw new ArgumentNullException(nameof(command));
            if (command.Kind != CrewHarpoonCommandKind.Fire || command.ShotId < 1 || command.ActorRevision < 1 ||
                command.SceneEpoch < 1 || string.IsNullOrEmpty(command.SceneKey) || command.SceneKey.Length > 256 ||
                actualScene.m_Handle == 0 || unityThreadId < 1) throw Invalid();
            _scene = actualScene; _thread = unityThreadId;
            _sourceCurrent = sourceCurrent ?? throw new ArgumentNullException(nameof(sourceCurrent)); _logger = logger;
        }

        public bool TryFire(int actualCollisionMask)
        {
            if (_fireAttempted || !Begin()) return false;
            try
            {
                _fireAttempted = true;
                if (Interlocked.Increment(ref _shotAttempts) > MaxShotsPerProcess) throw Invalid();
                lock (Retained) Retained.Add(this); // Before any wrapper/native allocation.
                HostHarpoonProfile profile = Command.Profile;
                if (profile == null || !Positive(profile.Speed, 200) || !Positive(profile.Radius, 2) ||
                    !Positive(profile.MaxDistance, 1000) || actualCollisionMask == 0 || !Finite(Command.Direction)) throw Invalid();
                double length = Math.Sqrt((double)Command.Direction.X * Command.Direction.X + (double)Command.Direction.Y * Command.Direction.Y);
                if (!double.IsFinite(length) || length < 0.000001) throw Invalid();
                _direction = new NVector2((float)(Command.Direction.X / length), (float)(Command.Direction.Y / length));
                _speed = profile.Speed; _radius = profile.Radius; _range = profile.MaxDistance; _mask = actualCollisionMask;
                ValidateSource();
                NVector3 origin = ReadOrigin();
                if (origin != Command.Origin || !Finite(origin)) throw Invalid();
                _z = origin.Z; _lastPosition = V3(origin.X, origin.Y, _z);
                Keep(_actor);
                LayerMask mask = Read(() => (LayerMask)_mask);
                // All hits, including unknown triggers, are conservative barriers.
                _filter = new ContactFilter2D { useTriggers = true, useLayerMask = true, layerMask = mask };
                _hits = Read(() => { _hits = new Il2CppStructArray<RaycastHit2D>(MaxHits); Retain(_hits); return _hits; }); Keep(_hits);
                _rootConstructionEntered = true;
                // Empty GameObject initially has no scripts/physics; deactivate
                // immediately, before adding the small owned visual component.
                _root = Read(() => { _root = new GameObject("MultiDave Employee Harpoon"); Retain(_root); return _root; });
                Keep(_root); Freeze(_root, out _rootPointer, out _rootUnity);
                Write(() => _root.SetActive(false));
                Write(() => SceneManager.MoveGameObjectToScene(_root, _scene));
                _transform = Read(() => { _transform = _root.transform; Retain(_transform); return _transform; });
                Keep(_transform); Freeze(_transform, out _transformPointer, out _transformUnity);
                Write(() => _transform.position = _lastPosition);
                double angle = Math.Atan2(_direction.Y, _direction.X) / 2;
                Quaternion rotation = new Quaternion { z = (float)Math.Sin(angle), w = (float)Math.Cos(angle) };
                Write(() => _transform.rotation = rotation);
                _line = Read(() => { _line = _root.AddComponent<LineRenderer>(); Retain(_line); return _line; });
                Keep(_line); Freeze(_line, out _linePointer, out _lineUnity);
                Write(() => _line.useWorldSpace = false); Write(() => _line.loop = false); Write(() => _line.positionCount = 2);
                Write(() => _line.startWidth = 0.04f); Write(() => _line.endWidth = 0.04f);
                Color color = new Color { r = 0.1f, g = 0.8f, b = 1, a = 1 };
                Write(() => _line.startColor = color); Write(() => _line.endColor = color);
                Write(() => _line.SetPosition(0, V3(-_radius, 0, 0))); Write(() => _line.SetPosition(1, V3(_radius, 0, 0)));
                ValidateSource(); ValidateOwned(false);
                Write(() => _root.SetActive(true)); _fired = true; _flying = true;
                ValidateOwned(true); ValidateSource(); Status = "Flying"; return true;
            }
            catch (Exception) { Fault("FireUnknown"); return false; }
            finally { End(); }
        }

        public bool FixedStep(float dt, out HostEmployeeHarpoonHit hit)
        {
            hit = null;
            if (!Active || !float.IsFinite(dt) || dt <= 0 || dt > HostCrewControl.MaxFixedStepSeconds || !Begin()) return false;
            try
            {
                ValidateSource(); ValidateOwned(true);
                Vector3 start = Read(() => _transform.position);
                if (!Equal(start, _lastPosition)) throw Invalid();
                double remaining = _range - _travelled;
                if (remaining <= 0) { _flying = false; Status = "RangeEnded"; return true; }
                double commanded = Math.Min(remaining, (double)_speed * dt);
                Vector3 target = V3((float)(start.x + _direction.X * commanded), (float)(start.y + _direction.Y * commanded), _z);
                double dx = (double)target.x - start.x, dy = (double)target.y - start.y, distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance > remaining)
                {
                    target.x = Toward(target.x, start.x); target.y = Toward(target.y, start.y);
                    dx = (double)target.x - start.x; dy = (double)target.y - start.y; distance = Math.Sqrt(dx * dx + dy * dy);
                }
                if (!Finite(target) || !double.IsFinite(distance)) throw Invalid();
                if (distance <= 0 || distance > remaining) { _flying = false; Status = "RangeEnded"; return true; }
                Vector2 direction = V2((float)(dx / distance), (float)(dy / distance));
                float castDistance = (float)distance;
                if (castDistance < distance) castDistance = MathF.BitIncrement(castDistance);
                int count = Read(() => _physics.CircleCast(V2(start.x, start.y), _radius, direction, castDistance, _filter, _hits)); Sweeps++;
                if (count < 0 || count >= MaxHits) throw Invalid(); // Full array cannot prove no hidden nearer obstruction.
                Collision nearest = null;
                for (int i = 0; i < count; i++)
                {
                    RaycastHit2D raw = Read(() => _hits[i]);
                    if (raw.m_Collider == 0 || !float.IsFinite(raw.m_Distance) || raw.m_Distance < 0 ||
                        raw.m_Distance > castDistance || !Finite(raw.m_Point) || !Finite(raw.m_Normal)) throw Invalid();
                    Collider2D collider = Read(() => raw.collider);
                    Collision sample = ReadCollision(collider, raw);
                    if (sample.RootPointer == _actorRootPointer && sample.RootUnity == _actorRootUnity) continue;
                    if (nearest != null && sample.Distance == nearest.Distance && sample.Pointer != nearest.Pointer) throw Invalid();
                    if (nearest == null || sample.Distance < nearest.Distance) nearest = sample;
                }
                double advance = nearest == null ? distance : Math.Min(distance, nearest.Distance);
                if (nearest != null) target = V3((float)(start.x + direction.x * advance), (float)(start.y + direction.y * advance), _z);
                if (!Finite(target)) throw Invalid();
                ValidateSource(); ValidateOwned(true);
                Write(() => _transform.position = target); // One own position dispatch; unknown results are never retried.
                Vector3 actual = Read(() => _transform.position);
                if (!Equal(actual, target)) throw Invalid();
                ValidateOwned(true); ValidateSource();
                double ax = (double)actual.x - start.x, ay = (double)actual.y - start.y;
                _travelled += Math.Sqrt(ax * ax + ay * ay); _lastPosition = actual; Moves++;
                if (nearest != null)
                {
                    _hitPointer = nearest.Pointer; _hitUnity = nearest.Unity; _hitClass = nearest.Class;
                    _hitRootPointer = nearest.RootPointer; _hitRootUnity = nearest.RootUnity; _hitId = nearest.Id;
                    Keep(nearest.Collider);
                    _hit = new HostEmployeeHarpoonHit(this, nearest.Collider, new NVector3(nearest.Point.x, nearest.Point.y, _z), new NVector2(direction.x, direction.y));
                    ValidateHitCollider(); ValidateSource(); _flying = false; Hits++; Status = "CollisionHeld"; hit = _hit;
                }
                else if (_travelled >= _range) { _flying = false; Status = "RangeEnded"; }
                return true;
            }
            catch (Exception) { Fault("SweepOrMoveUnknown"); return false; }
            finally { End(); }
        }

        public bool ValidateHit(HostEmployeeHarpoonHit hit)
        {
            if (!ReferenceEquals(hit, _hit) || hit == null || !ReferenceEquals(hit.Owner, this) || !Begin()) return false;
            try
            {
                ValidateSource(); ValidateOwned(true); ValidateHitCollider();
                if (!Equal(Read(() => _transform.position), _lastPosition)) throw Invalid();
                ValidateSource(); return true;
            }
            catch (Exception) { Fault("HitSourceChanged"); return false; }
            finally { End(); }
        }

        // Post-damage source checks deliberately do not read the target. A
        // normally returned damage call may have disabled/destroyed that fish;
        // only ValidateHit can supply fresh pre-dispatch target evidence.
        public bool ValidateHitSource(HostEmployeeHarpoonHit hit)
        {
            if (!ReferenceEquals(hit, _hit) || hit == null || !ReferenceEquals(hit.Owner, this) || !Begin()) return false;
            try
            {
                ValidateSource(); ValidateOwned(true);
                if (!Equal(Read(() => _transform.position), _lastPosition)) throw Invalid();
                ValidateSource(); return true;
            }
            catch (Exception) { Fault("HitProducerSourceChanged"); return false; }
            finally { End(); }
        }

        public bool TryReadPosition(out NVector3 position)
        {
            position = default; if (!_fired || !Begin()) return false;
            try
            {
                ValidateSource(); ValidateOwned(true); Vector3 actual = Read(() => _transform.position);
                ValidateOwned(true); ValidateSource(); if (!Equal(actual, _lastPosition)) throw Invalid();
                position = new NVector3(actual.x, actual.y, actual.z); return true;
            }
            catch (Exception) { Fault("PositionSourceChanged"); return false; }
            finally { End(); }
        }

        public bool TryReadAttackTransform(out Transform ownProjectile) => TryReadTransform(false, out ownProjectile);
        public bool TryReadActorTransform(out Transform ownedActor) => TryReadTransform(true, out ownedActor);
        private bool TryReadTransform(bool actor, out Transform value)
        {
            value = null; if (!_fired || !Begin()) return false;
            try
            {
                ValidateSource(); ValidateOwned(true);
                if (!Equal(Read(() => _transform.position), _lastPosition)) throw Invalid();
                ValidateSource(); value = actor ? _actor : _transform; return true;
            }
            catch (Exception) { Fault("TransformSourceChanged"); return false; }
            finally { End(); }
        }

        private NVector3 ReadOrigin()
        {
            NVector3 position = default;
            if (!Read(() => _body.TryReadMotion(out position, out _))) throw Invalid();
            return position;
        }

        private void ValidateSource()
        {
            if (!Read(() => _body.IsCurrent(ActorRevision, SceneEpoch, SceneKey))) throw Invalid();
            Scene actual = default; int mask = 0; Transform actor = null;
            if (!Read(() => _body.TryReadWeaponScene(out actual, out mask, out actor)) || actual.m_Handle != SceneHandle || mask != _mask || ReferenceEquals(actor, null)) throw Invalid();
            Freeze(actor, out IntPtr pointer, out IntPtr unity);
            IntPtr expected = Read(() => Il2CppClassPointerStore<Transform>.NativeClassPtr);
            if (expected == IntPtr.Zero || Read(() => IL2CPP.il2cpp_object_get_class(pointer)) != expected) throw Invalid();
            GameObject root = Read(() => actor.gameObject); Freeze(root, out IntPtr rootPointer, out IntPtr rootUnity);
            if (_actorPointer == IntPtr.Zero) { _actor = actor; _actorPointer = pointer; _actorUnity = unity; _actorRootPointer = rootPointer; _actorRootUnity = rootUnity; }
            else if (pointer != _actorPointer || unity != _actorUnity || rootPointer != _actorRootPointer || rootUnity != _actorRootUnity) throw Invalid();
            if (!Read(() => _scene.IsValid()) || !Read(() => _scene.isLoaded) || Read(() => _scene.name) != SceneKey) throw Invalid();
            PhysicsScene2D physics = Read(() => _scene.GetPhysicsScene2D());
            if (!Read(() => physics.IsValid()) || (_physics.m_Handle != 0 && _physics.m_Handle != physics.m_Handle)) throw Invalid();
            _physics = physics;
        }

        private Collision ReadCollision(Collider2D collider, RaycastHit2D raw)
        {
            Freeze(collider, out IntPtr pointer, out IntPtr unity);
            IntPtr nativeClass = Read(() => IL2CPP.il2cpp_object_get_class(pointer));
            if (nativeClass == IntPtr.Zero || !Read(() => collider.enabled) || Read(() => collider.GetInstanceID()) != raw.m_Collider) throw Invalid();
            GameObject root = Read(() => collider.gameObject); Freeze(root, out IntPtr rootPointer, out IntPtr rootUnity);
            Scene scene = Read(() => root.scene);
            if (scene.m_Handle != SceneHandle || !Read(() => root.activeInHierarchy)) throw Invalid();
            if (Read(() => collider.Pointer) != pointer || Read(() => collider.m_CachedPtr) != unity ||
                Read(() => IL2CPP.il2cpp_object_get_class(pointer)) != nativeClass) throw Invalid();
            return new Collision { Collider = collider, Pointer = pointer, Unity = unity, Class = nativeClass,
                RootPointer = rootPointer, RootUnity = rootUnity, Id = raw.m_Collider, Distance = raw.m_Distance, Point = raw.m_Point };
        }

        private void ValidateHitCollider()
        {
            if (_hit == null) throw Invalid();
            Collider2D collider = _hit.Collider;
            Identity(collider, _hitPointer, _hitUnity);
            if (Read(() => IL2CPP.il2cpp_object_get_class(_hitPointer)) != _hitClass ||
                Read(() => collider.GetInstanceID()) != _hitId || !Read(() => collider.enabled)) throw Invalid();
            GameObject root = Read(() => collider.gameObject);
            Identity(root, _hitRootPointer, _hitRootUnity);
            Scene scene = Read(() => root.scene);
            if (scene.m_Handle != SceneHandle || !Read(() => root.activeInHierarchy)) throw Invalid();
        }

        private void ValidateOwned(bool active)
        {
            Identity(_root, _rootPointer, _rootUnity); Identity(_transform, _transformPointer, _transformUnity); Identity(_line, _linePointer, _lineUnity);
            IntPtr expected = Read(() => Il2CppClassPointerStore<LineRenderer>.NativeClassPtr);
            if (expected == IntPtr.Zero || Read(() => IL2CPP.il2cpp_object_get_class(_linePointer)) != expected) throw Invalid();
            GameObject transformRoot = Read(() => _transform.gameObject), lineRoot = Read(() => _line.gameObject);
            Identity(transformRoot, _rootPointer, _rootUnity); Identity(lineRoot, _rootPointer, _rootUnity);
            Scene scene = Read(() => _root.scene);
            if (scene.m_Handle != SceneHandle || (active && !Read(() => _root.activeInHierarchy))) throw Invalid();
            if (Read(() => _line.positionCount) != 2 || Read(() => _line.useWorldSpace) || Read(() => _line.loop)) throw Invalid();
        }

        public void Stop(string reason = "Stopped")
        {
            _stopped = true; _flying = false;
            if (_busy) { Fault("ReentrantStop"); return; }
            if (_stopEntered || !Begin(true)) return;
            _stopEntered = true;
            try
            {
                if (_rootPointer == IntPtr.Zero)
                {
                    if (!ReferenceEquals(_root, null)) Freeze(_root, out _rootPointer, out _rootUnity);
                    else { _stopVerified = !_rootConstructionEntered; Status = _stopVerified ? "StoppedBeforeRoot" : "RootConstructionUnknownRetained"; return; }
                }
                if (Read(() => _root.Pointer) != _rootPointer) throw Invalid();
                IntPtr unity = Read(() => _root.m_CachedPtr);
                if (unity == IntPtr.Zero) { _observedDestroyed = true; _stopVerified = true; Status = "OwnRootDestroyed"; return; }
                if (unity != _rootUnity) throw Invalid();
                Read(() => { _root.SetActive(false); return true; }); Identity(_root, _rootPointer, _rootUnity);
                if (Read(() => _root.activeSelf)) throw Invalid();
                _stopVerified = true; Status = reason ?? "Stopped";
                if (_logs++ < 2) try { _logger?.LogInfo("DAVECOOP_CREW_HARPOON_STOPPED: owned root inactive; references retained."); } catch { }
            }
            catch (Exception) { Fault("CleanupUnknownRetained"); }
            finally { _cleanup = false; _busy = false; }
        }

        public void Dispose()
        {
            Stop(); if (!_stopVerified || _destroyEntered || _observedDestroyed || !Begin(true)) return;
            try
            {
                Identity(_root, _rootPointer, _rootUnity);
                if (Read(() => _root.activeSelf)) throw Invalid();
                _destroyEntered = true; Read(() => { UnityObject.Destroy(_root); return true; }); Status = "DestroyRequestedRetained";
            }
            catch (Exception) { Fault("DestroyUnknownRetained"); }
            finally { _cleanup = false; _busy = false; }
        }

        private void Freeze(UnityObject value, out IntPtr pointer, out IntPtr unity)
        {
            if (ReferenceEquals(value, null)) throw Invalid(); pointer = Read(() => value.Pointer); unity = Read(() => value.m_CachedPtr);
            if (pointer == IntPtr.Zero || unity == IntPtr.Zero) throw Invalid();
        }
        private void Identity(UnityObject value, IntPtr pointer, IntPtr unity)
        { if (ReferenceEquals(value, null) || pointer == IntPtr.Zero || unity == IntPtr.Zero || Read(() => value.Pointer) != pointer || Read(() => value.m_CachedPtr) != unity) throw Invalid(); }
        private void Retain(Il2CppObjectBase value)
        {
            if (ReferenceEquals(value, null)) throw Invalid();
            foreach (Reference reference in _references) if (ReferenceEquals(reference.Value, value)) return;
            if (_references.Count >= MaxReferences) throw Invalid();
            _references.Add(new Reference { Value = value });
        }
        private void Keep(Il2CppObjectBase value)
        {
            Retain(value); Reference owned = null;
            foreach (Reference reference in _references) if (ReferenceEquals(reference.Value, value)) { owned = reference; break; }
            if (owned == null || owned.Handle != IntPtr.Zero) return;
            IntPtr pointer = Read(() => value.Pointer); if (pointer == IntPtr.Zero) throw Invalid();
            Read(() => { owned.Handle = IL2CPP.il2cpp_gchandle_new(pointer, false); return owned.Handle; });
            if (owned.Handle == IntPtr.Zero) throw Invalid();
        }
        private bool Begin(bool cleanup = false)
        {
            if (Environment.CurrentManagedThreadId != _thread) { Fault("WrongThread"); return false; }
            if (_busy) { Fault("ReentrantProjectileCall"); return false; }
            if (!cleanup && (_failed || _stopped)) return false;
            _busy = true; _cleanup = cleanup; _steps = 0; _operationSerial = Interlocked.Read(ref _serial); return true;
        }
        private void Check()
        {
            if (!_busy || Environment.CurrentManagedThreadId != _thread || Interlocked.Read(ref _serial) != _operationSerial ||
                (!_cleanup && (_failed || _stopped)) || ++_steps > MaxSteps) throw Invalid();
            if (!_cleanup && !_sourceCurrent()) throw Invalid();
            if (Interlocked.Read(ref _serial) != _operationSerial) throw Invalid();
        }
        private T Read<T>(Func<T> read) { Check(); T value = read(); Check(); return value; }
        private void Write(Action write)
        {
            ValidateSource(); if (_rootPointer != IntPtr.Zero) Identity(_root, _rootPointer, _rootUnity);
            Read(() => { write(); return true; }); ValidateSource();
        }
        private void End() { _cleanup = false; _busy = false; if (_failed && !_stopEntered) Stop(); }
        private void Fault(string reason) { Interlocked.Increment(ref _serial); _failed = true; _flying = false; Status = reason; }
        private static InvalidOperationException Invalid() => new InvalidOperationException("Employee harpoon source or collision unavailable.");
        private static bool Positive(float value, float max) => float.IsFinite(value) && value > 0 && value <= max;
        private static bool Finite(NVector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
        private static bool Finite(NVector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
        private static bool Finite(Vector2 value) => float.IsFinite(value.x) && float.IsFinite(value.y);
        private static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        private static bool Equal(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
        private static float Toward(float value, float start) => value > start ? MathF.BitDecrement(value) : value < start ? MathF.BitIncrement(value) : value;
        private static Vector2 V2(float x, float y) => new Vector2 { x = x, y = y };
        private static Vector3 V3(float x, float y, float z) => new Vector3 { x = x, y = y, z = z };
    }
}
