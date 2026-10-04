using System;
using System.Collections.Generic;
using System.Text.Json;
using DaveCoop.Core.World;
using DaveCoop.Rendering;
using UnityEngine;

namespace DaveCoop.Networking
{
    // Displays the host's current active observation roster. Original guest
    // fish, AI, physics and rewards are untouched; this is not world takeover.
    internal sealed class RemoteFishWorld : IDisposable
    {
        private const int MaxTransitions = 4096;
        private readonly FishWorldBuffer _motion = new FishWorldBuffer();
        private readonly Dictionary<long, FishDisplayNode> _nodes = new Dictionary<long, FishDisplayNode>();
        private readonly Dictionary<long, string> _entityTrace = new Dictionary<long, string>();
        private readonly Dictionary<long, string> _warnings = new Dictionary<long, string>();
        private int _traceCount;
        private string _lastTrace;
        public int ReceivedEntityCount => _motion.ReceivedEntityCount;
        public int ReceivedFishCount => _motion.Count;
        public int AliveFishCount => _motion.AliveFishCount;
        public long Revision => _motion.Revision;
        public long SceneEpoch => _motion.SceneEpoch;
        public int RenderableCount { get; private set; }
        public int VisibleCount { get; private set; }
        public int InViewCount { get; private set; }
        public int UnknownResourceCount { get; private set; }
        public int MissingVisualCount { get; private set; }
        public int SourceInvisibleCount { get; private set; }
        public int RenderErrorCount { get; private set; }
        public int MeshVertices { get; private set; }
        public string FirstRenderError { get; private set; }
        public string DisplayStatus { get; private set; } = "Cleared";
        public double SnapshotAge { get; private set; }
        public int NodeCount
        {
            get { int count = 0; foreach (FishDisplayNode node in _nodes.Values) if (node.HasNodes) count++; return count; }
        }

        public void Receive(WorldSnapshot snapshot, double now)
        {
            long previousEpoch = _motion.SceneEpoch, previousRevision = _motion.Revision;
            if (!_motion.Push(snapshot, now)) return;
            if (previousEpoch != _motion.SceneEpoch)
                ClearNodes("EpochChanged", previousEpoch, previousRevision);
            else
            {
                var removed = new List<long>();
                foreach (long id in _nodes.Keys) if (!_motion.Contains(id)) removed.Add(id);
                foreach (long id in removed)
                {
                    FishDisplayNode node = _nodes[id]; node.Clear("RosterRemoved");
                    TraceEntity(id, node, "RosterRemoved"); _nodes.Remove(id);
                }
            }
            PruneTraceAndWarnings();
        }

        public void Render(double now, double delay, LocalAvatarCapture local, SpriteCatalog sprites, SpineCatalog spines, bool loopback)
        {
            ResetMetrics(); SnapshotAge = _motion.SampleAge(now);
            if (!local.IsAvailable) { HideAll("LocalUnavailable"); return; }
            string sampling = _motion.SamplingStatus(now, delay);
            if (sampling != "Ready")
            {
                HideAll(sampling == "EmptyHistory" ? (ReceivedEntityCount == 0 ? "EmptyWorld" : "NoFish") : sampling);
                return;
            }
            foreach (long id in _motion.GetEntityIds())
            {
                _nodes.TryGetValue(id, out FishDisplayNode node);
                if (!_motion.TryGetLatest(id, out EntityState latest)) continue;
                if (latest.Dead || latest.Captured)
                {
                    string reason = latest.Dead ? "Dead" : "Captured";
                    node?.Hide(reason); TraceEntity(id, node, reason); continue;
                }
                if (!_motion.Sample(id, now, delay, out EntityState from, out EntityState to, out float alpha))
                {
                    node?.Hide(_motion.LastSampleStatus); TraceEntity(id, node, _motion.LastSampleStatus); continue;
                }
                if (node == null) { node = new FishDisplayNode("MultiDave.RemoteFishWorld"); _nodes.Add(id, node); }
                try
                {
                    node.Render(from, to, alpha, local.Player.gameObject.scene, sprites, spines, loopback, true);
                    _warnings.Remove(id);
                    if (node.Renderable) RenderableCount++;
                    if (node.Visible) VisibleCount++;
                    if (node.InView) InViewCount++;
                    if (node.UnknownResource) UnknownResourceCount++;
                    if (node.DisplayStatus == "MissingVisual") MissingVisualCount++;
                    if (node.DisplayStatus == "SourceInvisible") SourceInvisibleCount++;
                    MeshVertices += node.MeshVertices;
                    TraceEntity(id, node, node.DisplayStatus);
                }
                catch (Exception error)
                {
                    string message = error.GetType().Name + ": " + error.Message;
                    RenderErrorCount++; if (FirstRenderError == null) FirstRenderError = message;
                    node.Clear("RenderError"); TraceEntity(id, node, "RenderError");
                    if ((!_warnings.TryGetValue(id, out string previous) || previous != message) && _traceCount < MaxTransitions)
                    {
                        _traceCount++;
                        NetworkDriver.Logger.LogWarning("DAVECOOP_FISH_WORLD_WARNING: entity=" + id + "; " + message);
                    }
                    _warnings[id] = message;
                }
            }
            DisplayStatus = VisibleCount > 0 ? "Visible" : "NoVisibleFish"; Trace();
        }

        public void Trace()
        {
            string signature = SceneEpoch + ":" + DisplayStatus + ":" + ReceivedEntityCount + ":" + ReceivedFishCount + ":" +
                AliveFishCount + ":" + RenderableCount + ":" + VisibleCount + ":" + InViewCount + ":" +
                UnknownResourceCount + ":" + MissingVisualCount + ":" + SourceInvisibleCount + ":" + RenderErrorCount + ":" + NodeCount;
            if (_lastTrace == signature) return;
            _lastTrace = signature;
            if (_traceCount >= MaxTransitions) return;
            _traceCount++;
            NetworkDriver.Logger.LogInfo("DAVECOOP_FISH_WORLD_STATE: " + JsonSerializer.Serialize(new
            {
                SceneEpoch, Revision, ReceivedEntityCount, ReceivedFishCount, AliveFishCount,
                RenderableCount, VisibleCount, InViewCount, UnknownResourceCount, MissingVisualCount, SourceInvisibleCount,
                RenderErrorCount, FirstRenderError, NodeCount, MeshVertices, DisplayStatus,
                SnapshotAge = double.IsFinite(SnapshotAge) ? (double?)SnapshotAge : null,
                DisplayOnly = true, ActiveObservationRosterOnly = true
            }));
        }

        private void TraceEntity(long id, FishDisplayNode node, string reason, long? scopeEpoch = null, long? scopeRevision = null)
        {
            bool visible = node != null && node.Visible, inView = node != null && node.InView;
            bool unknown = node != null && node.UnknownResource;
            string signature = reason + ":" + visible + ":" + inView + ":" + unknown;
            if (_entityTrace.TryGetValue(id, out string previous) && previous == signature) return;
            _entityTrace[id] = signature;
            if (_traceCount >= MaxTransitions) return;
            _traceCount++;
            Vector3 position = node == null ? Vector3.zero : node.DisplayPosition;
            NetworkDriver.Logger.LogInfo("DAVECOOP_FISH_WORLD_TRANSITION: " + JsonSerializer.Serialize(new
            {
                EntityId = id, SceneEpoch = scopeEpoch ?? _motion.SceneEpoch, Revision = scopeRevision ?? _motion.Revision,
                DisplayStatus = reason, Visible = visible, InView = inView, UnknownResource = unknown,
                MeshVertices = node == null ? 0 : node.MeshVertices,
                CleanupError = node?.CleanupError,
                Position = new[] { position.x, position.y, position.z }
            }));
        }

        private void HideAll(string reason)
        {
            foreach (var pair in _nodes) { pair.Value.Hide(reason); TraceEntity(pair.Key, pair.Value, reason); }
            DisplayStatus = reason; Trace();
        }

        private void ResetMetrics()
        {
            RenderableCount = 0; VisibleCount = 0; InViewCount = 0; UnknownResourceCount = 0;
            MissingVisualCount = 0; SourceInvisibleCount = 0; RenderErrorCount = 0; MeshVertices = 0; FirstRenderError = null;
        }

        private void PruneTraceAndWarnings()
        {
            var removed = new List<long>();
            foreach (long id in _entityTrace.Keys) if (!_motion.Contains(id)) removed.Add(id);
            foreach (long id in removed) _entityTrace.Remove(id);
            removed.Clear();
            foreach (long id in _warnings.Keys) if (!_motion.Contains(id)) removed.Add(id);
            foreach (long id in removed) _warnings.Remove(id);
        }

        private void ClearNodes(string reason, long scopeEpoch, long scopeRevision)
        {
            foreach (var pair in _nodes)
            {
                pair.Value.Clear(reason); TraceEntity(pair.Key, pair.Value, reason, scopeEpoch, scopeRevision);
            }
            _nodes.Clear(); _entityTrace.Clear(); _warnings.Clear();
        }

        public void Clear(string reason = "Cleared")
        {
            ClearNodes(reason, _motion.SceneEpoch, _motion.Revision); _motion.Clear(); ResetMetrics();
            SnapshotAge = 0; DisplayStatus = reason; Trace();
        }
        public void Dispose() => Clear();
    }
}
