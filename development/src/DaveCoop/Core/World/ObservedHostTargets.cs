using System;
using System.Collections.Generic;
using System.Threading;

namespace DaveCoop.Core.World
{
    public readonly struct HostPointerTarget
    {
        public long Pointer { get; }
        public HostEntityTarget Target { get; }
        public HostPointerTarget(long pointer, HostEntityTarget target) { Pointer = pointer; Target = target; }
    }

    // Published on the Unity thread; native observation callbacks read a frozen
    // CLR snapshot. Pointers and this lookup never enter the network protocol.
    public sealed class ObservedHostTargets
    {
        private sealed class Snapshot
        {
            public readonly long Epoch;
            public readonly Dictionary<long, HostEntityTarget> Targets;
            public Snapshot(long epoch, Dictionary<long, HostEntityTarget> targets) { Epoch = epoch; Targets = targets; }
        }
        private Snapshot _snapshot = new Snapshot(0, new Dictionary<long, HostEntityTarget>());
        public int Count => Volatile.Read(ref _snapshot).Targets.Count;

        public void Publish(long epoch, IEnumerable<HostPointerTarget> targets)
        {
            if (epoch < 1 || targets == null) throw new ArgumentException("Invalid observed target snapshot.");
            var byPointer = new Dictionary<long, HostEntityTarget>();
            var identities = new HashSet<long>();
            foreach (HostPointerTarget entry in targets)
            {
                HostEntityTarget target = entry.Target;
                if (entry.Pointer == 0 || target.SceneEpoch != epoch || target.EntityId < 1 || target.LocalToken == 0 ||
                    target.Kind != EntityKind.Fish || target.DataTid < 1 || target.Generation < 1 ||
                    byPointer.ContainsKey(entry.Pointer) || !identities.Add(target.EntityId))
                    throw new ArgumentException("Invalid or ambiguous observed fish target.");
                if (byPointer.Count == WorldFrames.MaxEntities) throw new InvalidOperationException("Observed targets exceed capacity.");
                byPointer.Add(entry.Pointer, target);
            }
            Volatile.Write(ref _snapshot, new Snapshot(epoch, byPointer));
        }

        public bool TryResolve(long pointer, long activeGeneration, out HostEntityTarget target)
        {
            target = default;
            Snapshot snapshot = Volatile.Read(ref _snapshot);
            if (pointer == 0 || activeGeneration < 1 || !snapshot.Targets.TryGetValue(pointer, out HostEntityTarget candidate) ||
                candidate.SceneEpoch != snapshot.Epoch || candidate.Generation != activeGeneration) return false;
            target = candidate; return true;
        }

        public void Clear() => Volatile.Write(ref _snapshot, new Snapshot(0, new Dictionary<long, HostEntityTarget>()));
    }
}
