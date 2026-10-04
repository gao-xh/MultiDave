using System;
using System.Numerics;

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
        private bool _reselect;
        public long EntityId { get; private set; }
        public int Count => _timeline.Count;
        public string LastSelectionReason { get; private set; } = "no-visible-candidate";
        public string LastSampleStatus { get; private set; } = "EmptyHistory";

        // Selecting another nearby fish is an explicit action. Camera movement
        // and temporary source-renderer visibility never change entity identity.
        public void RequestReselect() { _reselect = true; }
        public double SampleAge(double now) => double.IsFinite(now) ? now - _lastReceived : double.NaN;

        public bool Push(WorldSnapshot snapshot, double receivedAt, Vector3? viewer = null, Func<EntityState, bool> eligible = null)
        {
            WorldFrames.ValidateSnapshot(snapshot);
            if (!double.IsFinite(receivedAt) || receivedAt < 0) throw new ArgumentException("Invalid world arrival clock.");
            if (viewer.HasValue && (!float.IsFinite(viewer.Value.X) || !float.IsFinite(viewer.Value.Y) || !float.IsFinite(viewer.Value.Z)))
                throw new ArgumentException("Invalid fish preview viewer position.");
            if (_epoch != 0 && receivedAt < _lastReceived) throw new ArgumentException("Fish arrival clock moved backwards.");
            if (_epoch > snapshot.SceneEpoch || (_epoch == snapshot.SceneEpoch && snapshot.Revision <= _revision)) return false;
            if (_epoch != snapshot.SceneEpoch || _scene != snapshot.SceneKey) Clear();
            _epoch = snapshot.SceneEpoch; _scene = snapshot.SceneKey; _revision = snapshot.Revision;
            EntityState nearest = null, current = null; float nearestDistance = float.PositiveInfinity;
            foreach (EntityState entity in snapshot.Entities)
            {
                if (entity.Id == EntityId) current = entity;
                if (entity.Kind != EntityKind.Fish || entity.Visual == null || !entity.Visual.Visible || entity.Dead || entity.Captured ||
                    (eligible != null && !eligible(entity))) continue;
                float distance = viewer.HasValue ? Vector3.DistanceSquared(entity.Root.Position, viewer.Value) : 0;
                if (nearest == null || distance < nearestDistance) { nearest = entity; nearestDistance = distance; }
            }
            bool retain = !_reselect && current != null && current.Kind == EntityKind.Fish && !current.Dead && !current.Captured;
            LastSelectionReason = _reselect ? "manual-nearest" : retain ? "retained" : EntityId == 0 ? "initial-selection" :
                current == null ? "selected-removed" : current.Dead ? "selected-dead" : current.Captured ? "selected-captured" : "selected-kind-changed";
            _reselect = false;
            EntityState selected = retain ? current : nearest;
            if (selected == null)
            {
                if (EntityId == 0) LastSelectionReason = "no-visible-candidate";
                EntityId = 0; _timeline.Clear(); _lastReceived = receivedAt; return false;
            }
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
            if (!double.IsFinite(now) || !double.IsFinite(delay) || delay < 0 || delay > 1) { LastSampleStatus = "InvalidClock"; return false; }
            if (now < _lastReceived) { LastSampleStatus = "BeforeArrival"; return false; }
            if (now - _lastReceived > 1) { LastSampleStatus = "Stale"; return false; }
            bool sampled = _timeline.TrySample(now - delay, out from, out to, out alpha);
            LastSampleStatus = sampled ? "Ready" : "EmptyHistory";
            return sampled;
        }

        public void Clear()
        {
            _timeline.Clear(); EntityId = 0; _epoch = 0; _revision = 0; _scene = null; _anchor = 0; _lastReceived = 0; _reselect = false;
            LastSelectionReason = "no-visible-candidate"; LastSampleStatus = "EmptyHistory";
        }
    }
}
