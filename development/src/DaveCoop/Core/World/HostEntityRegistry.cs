using System;
using System.Collections.Generic;

namespace DaveCoop.Core.World
{
    // Local tokens never go onto the wire. Pool respawns must explicitly Unbind.
    public sealed class HostEntityRegistry
    {
        private sealed class Binding { public long Id; public EntityKind Kind; public int Tid; }
        private readonly Dictionary<long, Binding> _bindings = new Dictionary<long, Binding>();
        private long _nextId;
        public long Epoch { get; private set; }
        public int Count => _bindings.Count;

        public void BeginEpoch(long epoch)
        {
            if (epoch < 1 || epoch < Epoch) throw new ArgumentException("Entity epoch must advance.");
            if (epoch == Epoch) return;
            Epoch = epoch; _nextId = 0; _bindings.Clear();
        }

        public long Bind(long localToken, EntityKind kind, int dataTid)
        {
            if (Epoch == 0 || localToken == 0 || dataTid < 1 || !Enum.IsDefined(typeof(EntityKind), kind))
                throw new ArgumentException("Invalid entity binding.");
            if (_bindings.TryGetValue(localToken, out Binding found) && found.Kind == kind && found.Tid == dataTid) return found.Id;
            if (found == null && _bindings.Count == WorldFrames.MaxEntities) throw new InvalidOperationException("Host entity registry capacity exceeded.");
            if (_nextId == long.MaxValue) throw new InvalidOperationException("Host entity identity exhausted.");
            long id = ++_nextId; _bindings[localToken] = new Binding { Id = id, Kind = kind, Tid = dataTid }; return id;
        }

        public bool Unbind(long localToken) => _bindings.Remove(localToken);
        // Clearing within an epoch does not permit entity ID reuse.
        public void Clear() { _bindings.Clear(); }
    }
}
