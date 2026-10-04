using System;
using System.Linq;

namespace DaveCoop.Core.World
{
    // One-entity display acceptance buffer. Full guest-world rendering follows
    // map/spawn/AI takeover, and must not be claimed from this preview.
    public sealed class FishPreviewBuffer
    {
        private readonly SnapshotTimeline<EntityState> _timeline = new SnapshotTimeline<EntityState>(16);
        private long _epoch;
        private long _revision;
        private string _scene;
        private double _anchor;
        private double _lastReceived;
        public long EntityId { get; private set; }
        public int Count => _timeline.Count;

        public bool Push(WorldSnapshot snapshot, double receivedAt)
        {
            WorldFrames.ValidateSnapshot(snapshot);
            if (!double.IsFinite(receivedAt) || receivedAt < 0) throw new ArgumentException("Invalid world arrival clock.");
            if (_epoch != 0 && receivedAt < _lastReceived) throw new ArgumentException("Fish arrival clock moved backwards.");
            if (_epoch > snapshot.SceneEpoch || (_epoch == snapshot.SceneEpoch && snapshot.Revision <= _revision)) return false;
            if (_epoch != snapshot.SceneEpoch || _scene != snapshot.SceneKey) Clear();
            _epoch = snapshot.SceneEpoch; _scene = snapshot.SceneKey; _revision = snapshot.Revision;
            EntityState selected = snapshot.Entities.FirstOrDefault(entity => entity.Id == EntityId && entity.Visual != null);
            if (selected == null) selected = snapshot.Entities.FirstOrDefault(entity => entity.Kind == EntityKind.Fish && entity.Visual != null);
            if (selected == null) { EntityId = 0; _timeline.Clear(); return false; }
            if (EntityId != selected.Id)
            {
                _timeline.Clear(); EntityId = selected.Id; _anchor = receivedAt - snapshot.SampleTime;
            }
            if (!_timeline.TryPush(snapshot.SampleTime + _anchor, selected.Copy())) return false;
            _lastReceived = receivedAt; return true;
        }

        public bool Sample(double now, double delay, out EntityState from, out EntityState to, out float alpha)
        {
            from = to = null; alpha = 0;
            return double.IsFinite(now) && double.IsFinite(delay) && delay >= 0 && delay <= 1 &&
                now >= _lastReceived && now - _lastReceived <= 1 && _timeline.TrySample(now - delay, out from, out to, out alpha);
        }

        public void Clear()
        {
            _timeline.Clear(); EntityId = 0; _epoch = 0; _revision = 0; _scene = null; _anchor = 0; _lastReceived = 0;
        }
    }
}
