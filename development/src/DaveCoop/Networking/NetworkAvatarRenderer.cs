using System;
using System.Collections.Generic;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.Session;
using DaveCoop.Rendering;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
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
        private readonly Dictionary<string, HeadDefaults> _headDefaults = new Dictionary<string, HeadDefaults>(StringComparer.Ordinal);
        private GameObject _root;
        private int _sceneHandle;
        public int VisibleParts { get; private set; }
        public int UnknownAssets { get; private set; }
        public long HarpoonRenderErrors { get; private set; }
        public int HistoryCount => _motion.Count;
        public bool HasRoot => _root != null;

        public void Receive(ReceivedFrame frame) { _motion.TryPush(frame); }

        public void Render(double now, double delay, LocalAvatarCapture local, SpriteCatalog catalog,
            System.Numerics.Vector3? authoritativePosition = null)
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
            Pose rootPose = teleport ? to.Root : Pose.Interpolate(from.Root, to.Root, alpha);
            if (authoritativePosition.HasValue) rootPose.Position = authoritativePosition.Value;
            LocalAvatarCapture.ApplyPose(_root.transform, rootPose, false);
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
            foreach (string key in removed) { _parts.Remove(key); _headDefaults.Remove(key); }
            // Pre-register loaded assets before resolving this frame so any same-key
            // collisions discovered during a scan are rejected immediately.
            bool needsScan = false;
            foreach (SpritePartFrame part in discrete.Parts)
                if (part.Visible && !catalog.TryResolve(part.SpriteKey, out _)) { needsScan = true; break; }
            if (needsScan) catalog.ScanLoadedSprites(Time.unscaledTime);
            foreach (SpritePartFrame part in discrete.Parts)
            {
                if (LocalAvatarCapture.IsHarpoonSlot(part.Slot))
                {
                    RenderHarpoonPart(part, discrete == from && !teleport, alpha, local, catalog);
                    continue;
                }
                if (!_parts.TryGetValue(part.Slot, out SpriteRenderer display))
                {
                    var node = new GameObject("part-" + _parts.Count); node.hideFlags = HideFlags.DontSave;
                    node.transform.SetParent(_root.transform, false); display = node.AddComponent<SpriteRenderer>(); display.enabled = false;
                    SpriteRenderer template = local.Template(part.Slot, part.SpriteKey);
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

        private sealed class HeadDefaults
        {
            public Il2CppReferenceArray<Material> Materials;
            public SpriteDrawMode DrawMode;
            public Vector2 Size;
            public SpriteTileMode TileMode;
            public float AdaptiveModeThreshold;
            public SpriteSortPoint SortPoint;
            public SpriteMaskInteraction Mask;
            public HeadDefaults(SpriteRenderer display)
            {
                Materials = display.sharedMaterials; DrawMode = display.drawMode; Size = display.size;
                TileMode = display.tileMode; AdaptiveModeThreshold = display.adaptiveModeThreshold;
                SortPoint = display.spriteSortPoint; Mask = display.maskInteraction;
            }
            public void Apply(SpriteRenderer display)
            {
                display.sharedMaterials = Materials; display.drawMode = DrawMode; display.size = Size;
                display.tileMode = TileMode; display.adaptiveModeThreshold = AdaptiveModeThreshold;
                display.spriteSortPoint = SortPoint; display.maskInteraction = Mask;
            }
        }

        private void RenderHarpoonPart(SpritePartFrame part, bool interpolate, float alpha,
            LocalAvatarCapture local, SpriteCatalog catalog)
        {
            SpriteRenderer display = null;
            GameObject pendingNode = null;
            bool insertedDefaults = false, insertedPart = false;
            try
            {
                if (!_parts.TryGetValue(part.Slot, out display))
                {
                    pendingNode = new GameObject("harpoon-head-visual"); pendingNode.hideFlags = HideFlags.DontSave;
                    pendingNode.transform.SetParent(_root.transform, false);
                    display = pendingNode.AddComponent<SpriteRenderer>(); display.enabled = false;
                    var defaults = new HeadDefaults(display);
                    _headDefaults.Add(part.Slot, defaults); insertedDefaults = true;
                    _parts.Add(part.Slot, display); insertedPart = true;
                    pendingNode = null;
                }
                // Re-evaluate the actual head template every frame. A different
                // SpriteKey, missing local head or failed read restores the new
                // renderer's defaults instead of keeping an old weapon material.
                SpriteRenderer template = local.Template(part.Slot, part.SpriteKey);
                if (template == null) _headDefaults[part.Slot].Apply(display);
                else
                {
                    display.sharedMaterials = template.sharedMaterials; display.drawMode = template.drawMode;
                    display.size = template.size; display.tileMode = template.tileMode;
                    display.adaptiveModeThreshold = template.adaptiveModeThreshold;
                    display.spriteSortPoint = template.spriteSortPoint; display.maskInteraction = template.maskInteraction;
                }
                if (!part.Visible) return;
                if (!catalog.TryResolve(part.SpriteKey, out Sprite sprite)) { UnknownAssets++; return; }
                Pose pose = part.Pose;
                if (interpolate && _next.TryGetValue(part.Slot, out SpritePartFrame next)) pose = Pose.Interpolate(part.Pose, next.Pose, alpha);
                LocalAvatarCapture.ApplyPose(display.transform, pose, true);
                display.sprite = sprite; display.flipX = part.FlipX; display.flipY = part.FlipY;
                display.color = new Color(part.Color.X * 0.6f, part.Color.Y * 0.9f, part.Color.Z, part.Color.W);
                display.gameObject.layer = part.Layer; display.sortingLayerID = part.SortingLayer; display.sortingOrder = part.SortingOrder;
                display.enabled = true; VisibleParts++;
            }
            catch (Exception)
            {
                HarpoonRenderErrors++;
                // Roll back only entries inserted by this attempt. In particular,
                // failing the second dictionary Add must not strand a defaults
                // entry or destroy a previously registered display node.
                if (insertedPart) _parts.Remove(part.Slot);
                if (insertedDefaults) _headDefaults.Remove(part.Slot);
                try
                {
                    if (display != null) display.enabled = false;
                    if (pendingNode != null) UnityObject.Destroy(pendingNode);
                    else if (insertedPart && display != null) UnityObject.Destroy(display.gameObject);
                }
                catch (Exception) { HarpoonRenderErrors++; }
            }
        }

        public void Clear()
        {
            if (_root != null) { _root.SetActive(false); UnityObject.Destroy(_root); }
            _root = null; _sceneHandle = 0; _parts.Clear(); _next.Clear(); _headDefaults.Clear(); _motion.Clear();
            VisibleParts = 0; UnknownAssets = 0;
        }

        public void Dispose() => Clear();
    }
}
