using System;
using System.Collections.Generic;
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
    // A pool disable+enable wholly between polls still needs a spawn lifecycle hook.
    internal sealed class FishStateCapture
    {
        private sealed class LocalBinding { public long Pointer; public int Tid; }
        private sealed class Candidate { public long Token; public long Pointer; public EntityState State; }
        private HostEntityRegistry _registry = new HostEntityRegistry();
        private readonly Dictionary<long, LocalBinding> _bindings = new Dictionary<long, LocalBinding>();
        public int ObservedFish { get; private set; }
        public int UninitializedFish { get; private set; }
        public int UnresolvedVisuals { get; private set; }
        public string FirstVisualError { get; private set; }

        public WorldSnapshot Capture(Scene scene, long epoch, string sceneKey, double sampleTime, SpriteCatalog sprites, SpineCatalog spines)
        {
            if (_registry.Epoch != epoch) { _registry.BeginEpoch(epoch); _bindings.Clear(); }
            var found = UnityObject.FindObjectsOfType<FishAISystem>();
            if (found.Length > WorldFrames.MaxEntities) throw new InvalidOperationException("Too many fish objects for bounded world observation.");
            var candidates = new List<Candidate>(); var seen = new HashSet<long>(); UninitializedFish = 0; UnresolvedVisuals = 0; FirstVisualError = null;
            foreach (FishAISystem fish in found)
            {
                if (fish == null || !fish.gameObject.activeInHierarchy || fish.gameObject.scene.handle != scene.handle) continue;
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
                candidates.Add(new Candidate { Token = token, Pointer = pointer, State = state });
            }
            var gone = new List<long>();
            foreach (long token in _bindings.Keys) if (!seen.Contains(token)) gone.Add(token);
            foreach (long token in gone) { _bindings.Remove(token); _registry.Unbind(token); }
            // Validate the complete observation and prune gone bindings before
            // allocating IDs; a failed read cannot accumulate partial new bindings.
            var states = new List<EntityState>(candidates.Count);
            foreach (Candidate candidate in candidates)
            {
                if (_bindings.TryGetValue(candidate.Token, out LocalBinding previous) &&
                    (previous.Pointer != candidate.Pointer || previous.Tid != candidate.State.DataTid)) _registry.Unbind(candidate.Token);
                candidate.State.Id = _registry.Bind(candidate.Token, EntityKind.Fish, candidate.State.DataTid);
                _bindings[candidate.Token] = new LocalBinding { Pointer = candidate.Pointer, Tid = candidate.State.DataTid };
                states.Add(candidate.State);
            }
            states.Sort((a, b) => a.Id.CompareTo(b.Id)); ObservedFish = states.Count;
            return new WorldSnapshot { SceneEpoch = epoch, SceneKey = sceneKey, SampleTime = sampleTime, Entities = states.ToArray() };
        }

        public void Clear()
        {
            _registry = new HostEntityRegistry(); _bindings.Clear(); ObservedFish = 0; UninitializedFish = 0; UnresolvedVisuals = 0; FirstVisualError = null;
        }
    }
}
