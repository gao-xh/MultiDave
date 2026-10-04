using System.Collections.Generic;
using DaveCoop.Core.Protocol;

namespace DaveCoop.Core.World
{
    // Called under SessionPeer's lock. It retains at most one incomplete snapshot.
    public sealed class WorldAssembler
    {
        private WorldSlice _header;
        private EntityState[] _entities;
        private readonly HashSet<long> _ids = new HashSet<long>();
        private int _nextIndex;
        private long _latestRevision;
        private double _latestTime = -1;

        public long CommittedRevision { get; private set; }

        public bool Accept(WorldSlice slice, out WorldSnapshot complete)
        {
            complete = null; WorldFrames.ValidateSlice(slice);
            if (slice.Revision <= CommittedRevision || slice.Revision < _latestRevision) return false;
            if (slice.Index == 0)
            {
                if (slice.Revision <= _latestRevision || slice.SampleTime <= _latestTime)
                    throw new ProtocolException("World snapshot replay or clock regression.");
                _latestRevision = slice.Revision; _latestTime = slice.SampleTime;
                _header = new WorldSlice
                {
                    SceneEpoch = slice.SceneEpoch, SceneKey = slice.SceneKey, Revision = slice.Revision,
                    SampleTime = slice.SampleTime, Count = slice.Count, TotalEntities = slice.TotalEntities
                };
                _entities = new EntityState[slice.TotalEntities]; _ids.Clear(); _nextIndex = 0;
            }
            if (_header == null || slice.Revision != _header.Revision || slice.Index != _nextIndex ||
                slice.SceneEpoch != _header.SceneEpoch || slice.SceneKey != _header.SceneKey ||
                slice.SampleTime != _header.SampleTime || slice.Count != _header.Count || slice.TotalEntities != _header.TotalEntities)
                throw new ProtocolException("World slices do not form an ordered complete snapshot.");
            foreach (EntityState entity in slice.Entities)
            {
                if (!_ids.Add(entity.Id)) throw new ProtocolException("Duplicate entity across world slices.");
            }
            for (int i = 0; i < slice.Entities.Length; i++) _entities[slice.Index * WorldFrames.EntitiesPerSlice + i] = slice.Entities[i].Copy();
            _nextIndex++;
            if (_nextIndex != _header.Count) return false;
            complete = new WorldSnapshot
            {
                SceneEpoch = _header.SceneEpoch, SceneKey = _header.SceneKey, Revision = _header.Revision,
                SampleTime = _header.SampleTime, Entities = _entities
            };
            CommittedRevision = _header.Revision; _header = null; _entities = null; _ids.Clear(); _nextIndex = 0;
            return true;
        }

        public void Clear()
        {
            _header = null; _entities = null; _ids.Clear(); _nextIndex = 0;
            _latestRevision = 0; CommittedRevision = 0; _latestTime = -1;
        }
    }
}
