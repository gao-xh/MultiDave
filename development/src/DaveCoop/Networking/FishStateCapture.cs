using System;
using System.Collections.Generic;
using System.Threading;
using DaveCoop.Core.World;
using DaveCoop.Rendering;
using DR.AI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityObject = UnityEngine.Object;
using Pose = DaveCoop.Core.Pose;

namespace DaveCoop.Networking
{
    // Main-thread observation adapter. No AI/physics/health/interaction writes.
    // Lifecycle generations fence pool reuse even between successive polls.
    internal sealed class FishStateCapture
    {
        private sealed class LocalBinding { public long Pointer; public int Tid; public FishAISystem Fish; }
        private sealed class Candidate { public long Token; public long Pointer; public FishAISystem Fish; public EntityState State; }
        private HostEntityRegistry _registry = new HostEntityRegistry();
        private readonly Dictionary<long, LocalBinding> _bindings = new Dictionary<long, LocalBinding>();
        public int ObservedFish { get; private set; }
        public int UninitializedFish { get; private set; }
        public int UnresolvedVisuals { get; private set; }
        public string FirstVisualError { get; private set; }
        private FishLifecycleTracker _lifecycle;
        private int _sceneHandle;
        public int BindableTargets { get; private set; }
        private readonly ObservedHostTargets _observedTargets = new ObservedHostTargets();
        internal bool UsesLifecycle(FishLifecycleTracker tracker) => tracker != null && ReferenceEquals(Volatile.Read(ref _lifecycle), tracker);

        public WorldSnapshot Capture(Scene scene, long epoch, string sceneKey, double sampleTime, SpriteCatalog sprites, SpineCatalog spines, FishLifecycleTracker lifecycle)
        {
            if (_registry.Epoch != epoch) { _registry.BeginEpoch(epoch); _bindings.Clear(); }
            var found = UnityObject.FindObjectsOfType<FishAISystem>();
            if (found.Length > WorldFrames.MaxEntities) throw new InvalidOperationException("Too many fish objects for bounded world observation.");
            var candidates = new List<Candidate>(); var seen = new HashSet<long>(); UninitializedFish = 0; UnresolvedVisuals = 0; FirstVisualError = null;
            foreach (FishAISystem fish in found)
            {
                if (fish == null || !fish.isActiveAndEnabled || fish.gameObject.scene.handle != scene.handle) continue;
                long token = fish.GetInstanceID(); float hp = fish.HP, maxHp = fish.MaxHP; int tid = fish.FishDataTID;
                if (tid < 1 || !float.IsFinite(hp) || !float.IsFinite(maxHp) || maxHp <= 0 || hp < 0 || hp > maxHp || maxHp > 1000000)
                { UninitializedFish++; continue; }
                long pointer = fish.Pointer.ToInt64();
                var transform = fish.transform; Vector3 position = transform.position, scale = transform.lossyScale; Quaternion rotation = transform.rotation;
                var damageable = fish.FishDamageable;
                var state = new EntityState
                {
                    Id = 1, Kind = EntityKind.Fish, DataTid = tid, Hp = hp, MaxHp = maxHp,
                    Dead = damageable != null && damageable.m_IsDead, Captured = fish.IsFishCaptured,
                    Root = new Pose
                    {
                        Position = new System.Numerics.Vector3(position.x, position.y, position.z),
                        Rotation = new System.Numerics.Quaternion(rotation.x, rotation.y, rotation.z, rotation.w),
                        Scale = new System.Numerics.Vector3(scale.x, scale.y, scale.z)
                    }
                };
                try { state.Visual = FishVisualCapture.Capture(fish, sprites, spines); }
                catch (Exception error) { state.Visual = null; if (FirstVisualError == null) FirstVisualError = error.GetType().Name + ": " + error.Message; }
                if (state.Visual == null) UnresolvedVisuals++;
                WorldFrames.ValidateEntity(state);
                if (token == 0 || !seen.Add(token)) throw new InvalidOperationException("Ambiguous local fish identity.");
                candidates.Add(new Candidate { Token = token, Pointer = pointer, Fish = fish, State = state });
            }
            var gone = new List<long>();
            foreach (long token in _bindings.Keys) if (!seen.Contains(token)) gone.Add(token);
            foreach (long token in gone) { _bindings.Remove(token); _registry.Unbind(token); }
            var livePointers = new HashSet<long>();
            foreach (Candidate candidate in candidates) livePointers.Add(candidate.Pointer);
            lifecycle.Retain(livePointers);
            Volatile.Write(ref _lifecycle, lifecycle); _sceneHandle = scene.handle;
            // Validate the complete observation and prune gone bindings before
            // allocating IDs; a failed read cannot accumulate partial new bindings.
            var states = new List<EntityState>(candidates.Count);
            foreach (Candidate candidate in candidates)
            {
                if (_bindings.TryGetValue(candidate.Token, out LocalBinding previous) &&
                    (previous.Pointer != candidate.Pointer || previous.Tid != candidate.State.DataTid)) _registry.Unbind(candidate.Token);
                long generation = lifecycle.ObserveActive(candidate.Pointer);
                candidate.State.Id = _registry.Bind(candidate.Token, EntityKind.Fish, candidate.State.DataTid, generation);
                _bindings[candidate.Token] = new LocalBinding { Pointer = candidate.Pointer, Tid = candidate.State.DataTid, Fish = candidate.Fish };
                states.Add(candidate.State);
            }
            states.Sort((a, b) => a.Id.CompareTo(b.Id)); ObservedFish = states.Count;
            BindableTargets = 0;
            foreach (EntityState state in states) if (TryResolveNativeFish(epoch, state.Id, out _)) BindableTargets++;
            var observedTargets = new List<HostPointerTarget>(states.Count);
            foreach (Candidate candidate in candidates)
                if (_registry.TryResolve(epoch, candidate.State.Id, out HostEntityTarget target))
                    observedTargets.Add(new HostPointerTarget(candidate.Pointer, target));
            _observedTargets.Publish(epoch, observedTargets);
            return new WorldSnapshot { SceneEpoch = epoch, SceneKey = sceneKey, SampleTime = sampleTime, Entities = states.ToArray() };
        }

        // Safe from native observation callbacks: frozen CLR map + CLR lifecycle
        // tracker only. Never touches a Unity object or creates another identity.
        public HostEntityTarget? ResolveObservedPointer(long pointer)
        {
            FishLifecycleTracker lifecycle = Volatile.Read(ref _lifecycle);
            if (lifecycle == null || !lifecycle.TryGetActiveGeneration(pointer, out long generation) ||
                !_observedTargets.TryResolve(pointer, generation, out HostEntityTarget target)) return null;
            if (!ReferenceEquals(lifecycle, Volatile.Read(ref _lifecycle)) ||
                !lifecycle.TryGetActiveGeneration(pointer, out long currentGeneration) || currentGeneration != generation) return null;
            return target;
        }

        // Unity-thread lookup only. Identity is checked again against current
        // native ownership and lifecycle, including changes between snapshots.
        // This is not attack/capture authorization; those operation rules follow.
        public bool TryResolveNativeFish(long epoch, long entityId, out FishAISystem fish)
        {
            fish = null;
            if (_lifecycle == null || !_registry.TryResolve(epoch, entityId, out HostEntityTarget target) || target.Kind != EntityKind.Fish ||
                !_bindings.TryGetValue(target.LocalToken, out LocalBinding local) || local.Tid != target.DataTid ||
                !_lifecycle.TryGetActiveGeneration(local.Pointer, out long generation) || generation != target.Generation) return false;
            FishAISystem native = local.Fish;
            if (native == null || !native.isActiveAndEnabled || native.Pointer.ToInt64() != local.Pointer ||
                native.GetInstanceID() != target.LocalToken || native.FishDataTID != target.DataTid || native.gameObject.scene.handle != _sceneHandle) return false;
            fish = native; return true;
        }

        public void Clear()
        {
            _registry.Clear(); _bindings.Clear(); Volatile.Write(ref _lifecycle, null); _observedTargets.Clear(); _sceneHandle = 0;
            ObservedFish = 0; BindableTargets = 0; UninitializedFish = 0; UnresolvedVisuals = 0; FirstVisualError = null;
        }

        public void ResetRoom() { Clear(); _registry = new HostEntityRegistry(); }
    }
}
