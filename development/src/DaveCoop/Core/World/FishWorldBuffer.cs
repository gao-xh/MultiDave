using System;
using System.Collections.Generic;
using DaveCoop.Core.Protocol;

namespace DaveCoop.Core.World
{
    // A complete received observation roster, not the original game's entire
    // ocean. Serialize calls on the consumer thread; no camera participates in
    // membership. A missing visual or terminal state still retains numeric ID.
    public sealed class FishWorldBuffer
    {
        private sealed class Entry
        {
            public readonly SnapshotTimeline<EntityState> History = new SnapshotTimeline<EntityState>(16);
            public EntityState Latest;
        }

        private Dictionary<long, Entry> _fish = new Dictionary<long, Entry>();
        private double _anchor;
        private double _lastReceived;
        private double _lastSample = -1;
        private double _lastLocalSample = -1;
        public long SceneEpoch { get; private set; }
        public string SceneKey { get; private set; }
        public long Revision { get; private set; }
        public int ReceivedEntityCount { get; private set; }
        public int Count => _fish.Count;
        public int AliveFishCount { get; private set; }
        public string LastSampleStatus { get; private set; } = "EmptyHistory";

        public bool Push(WorldSnapshot snapshot, double receivedAt)
        {
            WorldFrames.ValidateSnapshot(snapshot);
            if (!double.IsFinite(receivedAt) || receivedAt < 0) throw new ArgumentException("Invalid world arrival clock.");
            if (SceneEpoch != 0 && receivedAt < _lastReceived) throw new ArgumentException("Fish world arrival clock moved backwards.");
            if (snapshot.SceneEpoch < SceneEpoch || (snapshot.SceneEpoch == SceneEpoch && snapshot.Revision <= Revision)) return false;
            bool newEpoch = snapshot.SceneEpoch != SceneEpoch;
            if (!newEpoch && snapshot.SceneKey != SceneKey) throw new ProtocolException("Fish world scene changed without a new epoch.");
            if (!newEpoch && snapshot.SampleTime <= _lastSample) throw new ProtocolException("Fish world sampling clock regressed.");
            double anchor = newEpoch ? receivedAt - snapshot.SampleTime : _anchor;
            double localSample = snapshot.SampleTime + anchor;
            if (!double.IsFinite(localSample) || (!newEpoch && localSample <= _lastLocalSample))
                throw new ProtocolException("Fish world local sampling clock is not monotonic.");

            // Validate and copy the whole next roster, and allocate new histories,
            // before mutating existing entries. Invalid snapshots cannot remove
            // an old entity or leave a partially advanced history behind.
            var next = new Dictionary<long, Entry>();
            var states = new Dictionary<long, EntityState>();
            int alive = 0;
            foreach (EntityState entity in snapshot.Entities)
            {
                if (entity.Kind != EntityKind.Fish) continue;
                Entry entry = null;
                if (!newEpoch && _fish.TryGetValue(entity.Id, out entry) && entry.Latest.DataTid != entity.DataTid)
                    throw new ProtocolException("Fish species changed without a new entity identity.");
                if (entry == null) entry = new Entry();
                next.Add(entity.Id, entry); states.Add(entity.Id, entity.Copy());
                if (!entity.Dead && !entity.Captured) alive++;
            }
            foreach (var pair in states)
            {
                Entry entry = next[pair.Key];
                // All histories share the checked, fixed epoch clock anchor, so
                // their next timestamp is known to be strictly increasing.
                entry.History.TryPush(localSample, pair.Value); entry.Latest = pair.Value;
            }
            _fish = next; _anchor = anchor; _lastReceived = receivedAt;
            _lastSample = snapshot.SampleTime; _lastLocalSample = localSample;
            SceneEpoch = snapshot.SceneEpoch; SceneKey = snapshot.SceneKey; Revision = snapshot.Revision;
            ReceivedEntityCount = snapshot.Entities.Length; AliveFishCount = alive;
            return true;
        }

        // Copies isolate both callers' ingress DTOs and render consumers' values.
        public bool TryGetLatest(long entityId, out EntityState state)
        {
            state = null;
            if (!_fish.TryGetValue(entityId, out Entry entry)) return false;
            state = entry.Latest.Copy(); return true;
        }

        public bool Contains(long entityId) => _fish.ContainsKey(entityId);
        public int HistoryCount(long entityId) => _fish.TryGetValue(entityId, out Entry entry) ? entry.History.Count : 0;
        public long[] GetEntityIds()
        {
            var result = new long[_fish.Count]; _fish.Keys.CopyTo(result, 0); Array.Sort(result); return result;
        }

        public double SampleAge(double now) => double.IsFinite(now) ? now - _lastReceived : double.NaN;
        public string SamplingStatus(double now, double delay)
        {
            if (!double.IsFinite(now) || !double.IsFinite(delay) || now < 0 || delay < 0 || delay > 1) return "InvalidClock";
            if (_fish.Count == 0) return "EmptyHistory";
            if (now < _lastReceived) return "BeforeArrival";
            if (now - _lastReceived > 1) return "Stale";
            return "Ready";
        }

        public bool Sample(long entityId, double now, double delay, out EntityState from, out EntityState to, out float alpha)
        {
            from = to = null; alpha = 0;
            LastSampleStatus = SamplingStatus(now, delay);
            if (LastSampleStatus != "Ready") return false;
            if (!_fish.TryGetValue(entityId, out Entry entry)) { LastSampleStatus = "UnknownEntity"; return false; }
            if (!entry.History.TrySample(now - delay, out EntityState first, out EntityState second, out alpha))
            { LastSampleStatus = "EmptyHistory"; return false; }
            from = first.Copy(); to = second.Copy(); return true;
        }

        public void Clear()
        {
            _fish.Clear(); SceneEpoch = 0; SceneKey = null; Revision = 0; ReceivedEntityCount = 0; AliveFishCount = 0;
            _anchor = 0; _lastReceived = 0; _lastSample = -1; _lastLocalSample = -1; LastSampleStatus = "EmptyHistory";
        }
    }
}
