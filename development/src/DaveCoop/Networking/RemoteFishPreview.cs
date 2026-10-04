using System;
using System.Text.Json;
using DaveCoop.Core.World;
using DaveCoop.Rendering;
using UnityEngine;

namespace DaveCoop.Networking
{
    internal sealed class RemoteFishPreview
    {
        private readonly FishPreviewBuffer _motion = new FishPreviewBuffer();
        private readonly FishDisplayNode _node = new FishDisplayNode("MultiDave.RemoteFishPreview");
        private string _lastTrace;
        private string _nodeWarning;
        private int _traceCount;
        public long SelectedEntity => _motion.EntityId;
        public bool Visible => _node.Visible;
        public bool UnknownResource => _node.UnknownResource;
        public bool InView => _node.InView;
        public int MeshVertices => _node.MeshVertices;
        public string DisplayStatus => _node.DisplayStatus;
        public string SelectionReason => _motion.LastSelectionReason;
        public double SnapshotAge { get; private set; }

        public void RequestReselect() => _motion.RequestReselect();

        public void Receive(WorldSnapshot snapshot, double now, LocalAvatarCapture local, bool loopback)
        {
            if (!local.IsAvailable) return;
            Camera camera = Camera.main; Vector3 position = local.Player.transform.position;
            var viewer = new System.Numerics.Vector3(position.x, position.y, position.z);
            long previous = _motion.EntityId;
            _motion.Push(snapshot, now, viewer, entity =>
            {
                if (camera == null) return System.Numerics.Vector3.DistanceSquared(entity.Root.Position, viewer) <= 400;
                var root = entity.Root.Position;
                Vector3 point = new Vector3(root.X + (loopback ? 3 : 0), root.Y, root.Z);
                Vector3 viewport = camera.WorldToViewportPoint(point);
                return (camera.cullingMask & (1 << entity.Visual.Layer)) != 0 && viewport.z > 0 &&
                    viewport.x >= 0.05f && viewport.x <= 0.95f && viewport.y >= 0.05f && viewport.y <= 0.95f;
            });
            if (previous != _motion.EntityId && _traceCount < 2048)
            {
                _traceCount++;
                NetworkDriver.Logger.LogInfo("DAVECOOP_FISH_PREVIEW_SELECTION: " + JsonSerializer.Serialize(new
                {
                    PreviousEntity = previous, SelectedEntity = _motion.EntityId, Reason = _motion.LastSelectionReason,
                    snapshot.SceneEpoch, snapshot.SceneKey, snapshot.Revision
                }));
            }
        }

        public void Render(double now, double delay, LocalAvatarCapture local, SpriteCatalog sprites, SpineCatalog spines, bool loopback)
        {
            SnapshotAge = _motion.SampleAge(now);
            if (_motion.EntityId == 0) { _node.Clear("NoSelection"); return; }
            if (!local.IsAvailable) { _node.Hide("LocalUnavailable"); return; }
            if (!_motion.Sample(now, delay, out EntityState from, out EntityState to, out float alpha))
            { _node.Hide(_motion.LastSampleStatus); return; }
            // Default culling matches the verified single-fish preview: retain
            // its selected identity and report OutsideCamera without reselection.
            try
            {
                _node.Render(from, to, alpha, local.Player.gameObject.scene, sprites, spines, loopback);
                _nodeWarning = null;
            }
            catch (Exception error)
            {
                // A display component failure must not clear the verified
                // selected identity, its history or the TCP session.
                _node.Clear("RenderError");
                string message = error.GetType().Name + ": " + error.Message;
                if (_node.CleanupError != null) message += "; cleanup=" + _node.CleanupError;
                if (message != _nodeWarning && _traceCount < 2048)
                {
                    _traceCount++;
                    NetworkDriver.Logger.LogWarning("DAVECOOP_FISH_PREVIEW_WARNING: " + message);
                }
                _nodeWarning = message;
            }
        }

        // State transitions are recorded immediately, instead of relying on the
        // two-second aggregate that misses short hides. Bounded per controller.
        public void Trace()
        {
            string signature = SelectedEntity + ":" + DisplayStatus + ":" + Visible + ":" + InView;
            if (signature == _lastTrace) return;
            _lastTrace = signature;
            if (_traceCount >= 2048) return;
            _traceCount++;
            Vector3 position = _node.DisplayPosition;
            NetworkDriver.Logger.LogInfo("DAVECOOP_FISH_PREVIEW_TRANSITION: " + JsonSerializer.Serialize(new
            {
                SelectedEntity, DisplayStatus, Visible, InView, UnknownResource, MeshVertices, SnapshotAge,
                CleanupError = _node.CleanupError,
                Position = new[] { position.x, position.y, position.z }
            }));
        }

        public void DrawMarker()
        {
            if (!Visible || !InView || !_node.HasNodes) return;
            Camera camera = Camera.main; if (camera == null) return;
            Vector3 screen = camera.WorldToScreenPoint(_node.DisplayPosition); if (screen.z <= 0) return;
            float x = screen.x, y = Screen.height - screen.y;
            Color previous = GUI.color;
            try
            {
                GUI.color = new Color(0.3f, 0.95f, 1, 1);
                GUI.DrawTexture(new Rect(x - 9, y - 1, 18, 2), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(x - 1, y - 9, 2, 18), Texture2D.whiteTexture);
                GUI.Box(new Rect(x - 112, y - 40, 224, 25), "MultiDave Fish Preview #" + SelectedEntity);
            }
            finally { GUI.color = previous; }
        }

        public void Clear(string reason = "Cleared")
        {
            _node.Clear(reason); _motion.Clear(); _nodeWarning = null; SnapshotAge = 0; Trace();
        }
    }
}
