using System;
using System.Collections.Generic;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.Session;
using DaveCoop.Rendering;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityObject = UnityEngine.Object;
using Pose = DaveCoop.Core.Pose;

namespace DaveCoop.Networking
{
    internal sealed class NetworkAvatarRenderer : IDisposable
    {
        private readonly RemoteMotionBuffer _motion = new RemoteMotionBuffer();
        private readonly Dictionary<string, SpriteRenderer> _parts = new Dictionary<string, SpriteRenderer>(StringComparer.Ordinal);
        private readonly Dictionary<string, SpritePartFrame> _next = new Dictionary<string, SpritePartFrame>(StringComparer.Ordinal);
        private GameObject _root;
        private int _sceneHandle;
        public int VisibleParts { get; private set; }
        public int UnknownAssets { get; private set; }
        public int HistoryCount => _motion.Count;
        public bool HasRoot => _root != null;

        public void Receive(ReceivedFrame frame) { _motion.TryPush(frame); }

        public void Render(double now, double delay, LocalAvatarCapture local, SpriteCatalog catalog)
        {
            VisibleParts = 0; UnknownAssets = 0;
            if (!local.IsAvailable || !_motion.TrySample(now, delay, out PlayerFrame from, out PlayerFrame to, out float alpha))
            {
                if (_root != null) _root.SetActive(false);
                return;
            }
            if (_root != null && _sceneHandle != local.SceneHandle) Clear();
            if (_root == null)
            {
                _root = new GameObject("MultiDave.RemotePlayer-" + from.PlayerId);
                _root.hideFlags = HideFlags.DontSave; _sceneHandle = local.SceneHandle;
                SceneManager.MoveGameObjectToScene(_root, local.Player.gameObject.scene);
            }
            _root.SetActive(true);
            bool teleport = System.Numerics.Vector3.DistanceSquared(from.Root.Position, to.Root.Position) > 144;
            LocalAvatarCapture.ApplyPose(_root.transform, teleport ? to.Root : Pose.Interpolate(from.Root, to.Root, alpha), false);
            PlayerFrame discrete = alpha >= 1 || teleport ? to : from;
            _next.Clear(); foreach (SpritePartFrame part in to.Parts) _next.Add(part.Slot, part);
            var required = new HashSet<string>(StringComparer.Ordinal);
            foreach (SpritePartFrame part in discrete.Parts) required.Add(part.Slot);
            var removed = new List<string>();
            foreach (var pair in _parts)
            {
                pair.Value.enabled = false;
                if (!required.Contains(pair.Key)) { UnityObject.Destroy(pair.Value.gameObject); removed.Add(pair.Key); }
            }
            foreach (string key in removed) _parts.Remove(key);
            // Pre-register loaded assets before resolving this frame so any same-key
            // collisions discovered during a scan are rejected immediately.
            bool needsScan = false;
            foreach (SpritePartFrame part in discrete.Parts)
                if (part.Visible && !catalog.TryResolve(part.SpriteKey, out _)) { needsScan = true; break; }
            if (needsScan) catalog.ScanLoadedSprites(Time.unscaledTime);
            foreach (SpritePartFrame part in discrete.Parts)
            {
                if (!_parts.TryGetValue(part.Slot, out SpriteRenderer display))
                {
                    var node = new GameObject("part-" + _parts.Count); node.hideFlags = HideFlags.DontSave;
                    node.transform.SetParent(_root.transform, false); display = node.AddComponent<SpriteRenderer>(); display.enabled = false;
                    SpriteRenderer template = local.Template(part.Slot);
                    if (template != null)
                    {
                        display.sharedMaterials = template.sharedMaterials; display.drawMode = template.drawMode;
                        display.size = template.size; display.tileMode = template.tileMode;
                        display.adaptiveModeThreshold = template.adaptiveModeThreshold;
                        display.spriteSortPoint = template.spriteSortPoint; display.maskInteraction = template.maskInteraction;
                    }
                    _parts.Add(part.Slot, display);
                }
                if (!part.Visible) continue;
                if (!catalog.TryResolve(part.SpriteKey, out Sprite sprite)) { UnknownAssets++; continue; }
                Pose pose = part.Pose;
                // Slot-based interpolation tolerates a peer changing its visual parts.
                if (!teleport && discrete == from && _next.TryGetValue(part.Slot, out SpritePartFrame next))
                    pose = Pose.Interpolate(part.Pose, next.Pose, alpha);
                LocalAvatarCapture.ApplyPose(display.transform, pose, true);
                display.sprite = sprite; display.flipX = part.FlipX; display.flipY = part.FlipY;
                display.color = new Color(part.Color.X * 0.6f, part.Color.Y * 0.9f, part.Color.Z, part.Color.W);
                display.gameObject.layer = part.Layer; display.sortingLayerID = part.SortingLayer; display.sortingOrder = part.SortingOrder;
                display.enabled = true; VisibleParts++;
            }
        }

        public void Clear()
        {
            if (_root != null) { _root.SetActive(false); UnityObject.Destroy(_root); }
            _root = null; _sceneHandle = 0; _parts.Clear(); _next.Clear(); _motion.Clear();
            VisibleParts = 0; UnknownAssets = 0;
        }

        public void Dispose() => Clear();
    }
}
