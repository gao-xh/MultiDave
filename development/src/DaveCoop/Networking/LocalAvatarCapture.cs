using System;
using System.Collections.Generic;
using DaveCoop.Core;
using DaveCoop.Core.Protocol;
using DaveCoop.Rendering;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityObject = UnityEngine.Object;
using Pose = DaveCoop.Core.Pose;

namespace DaveCoop.Networking
{
    internal sealed class LocalAvatarCapture
    {
        private sealed class Part { public string Slot; public SpriteRenderer Source; }
        private readonly List<Part> _parts = new List<Part>();
        private readonly Dictionary<string, SpriteRenderer> _templates = new Dictionary<string, SpriteRenderer>(StringComparer.Ordinal);
        public InGameManager Manager { get; private set; }
        public PlayerCharacter Player { get; private set; }
        public int PlayerId { get; private set; }
        public int SceneHandle { get; private set; }
        public int PartCount => _parts.Count;
        public int UnkeyedVisibleParts { get; private set; }

        public static InGameManager FindLocalManager()
        {
            InGameManager selected = null;
            int activeScene = SceneManager.GetActiveScene().handle;
            bool selectedActive = false;
            foreach (InGameManager manager in UnityObject.FindObjectsOfType<InGameManager>())
            {
                if (manager == null || manager.playerCharacter == null || !manager.playerCharacter.gameObject.activeInHierarchy) continue;
                bool active = manager.playerCharacter.gameObject.scene.handle == activeScene;
                if (selected == null || (active && !selectedActive)) { selected = manager; selectedActive = active; }
                else if (active == selectedActive && selected.playerCharacter.GetInstanceID() != manager.playerCharacter.GetInstanceID()) return null;
            }
            return selected;
        }

        public bool IsAvailable => Manager != null && Manager.IsLoadedAll && Player != null && Player.gameObject.activeInHierarchy &&
            Player.GetInstanceID() == PlayerId && Player.gameObject.scene.handle == SceneHandle;

        public void Bind(InGameManager manager)
        {
            Clear(); Manager = manager; Player = manager.playerCharacter;
            PlayerId = Player.GetInstanceID(); SceneHandle = Player.gameObject.scene.handle;
            RefreshVisualParts();
        }

        public bool VisualPartsMatch()
        {
            var sprites = Player.GetComponentsInChildren<SpriteRenderer>(true);
            if (sprites.Length != _parts.Count) return false;
            for (int i = 0; i < sprites.Length; i++)
                if (_parts[i].Source == null || sprites[i].GetInstanceID() != _parts[i].Source.GetInstanceID()) return false;
            return true;
        }

        public void RefreshVisualParts()
        {
            var sprites = Player.GetComponentsInChildren<SpriteRenderer>(true);
            if (sprites.Length == 0 || sprites.Length > PacketCodec.MaxParts) throw new InvalidOperationException("Unsupported avatar sprite count: " + sprites.Length);
            _parts.Clear(); _templates.Clear();
            for (int i = 0; i < sprites.Length; i++)
            {
                int componentIndex = 0;
                var nodeRenderers = sprites[i].GetComponents<SpriteRenderer>();
                while (componentIndex < nodeRenderers.Length && nodeRenderers[componentIndex].GetInstanceID() != sprites[i].GetInstanceID()) componentIndex++;
                string slot = SlotPath(sprites[i].transform, Player.transform) + "#renderer-" + componentIndex;
                if (slot.Length > 256) throw new InvalidOperationException("Avatar hierarchy path exceeds protocol limit.");
                _parts.Add(new Part { Slot = slot, Source = sprites[i] }); _templates.Add(slot, sprites[i]);
            }
        }

        public PlayerFrame Capture(double now, SpriteCatalog catalog)
        {
            if (!IsAvailable) throw new InvalidOperationException("Local player is no longer available.");
            Transform origin = Player.transform;
            Quaternion inverse = Quaternion.Inverse(origin.rotation);
            Vector3 rootScale = origin.lossyScale;
            var parts = new SpritePartFrame[_parts.Count];
            UnkeyedVisibleParts = 0;
            for (int i = 0; i < _parts.Count; i++)
            {
                Part binding = _parts[i]; SpriteRenderer source = binding.Source;
                if (source == null) throw new InvalidOperationException("Local avatar part was destroyed.");
                string key = null;
                try { key = catalog.Register(source.sprite); } catch (ArgumentException) { }
                bool visible = source.enabled && source.gameObject.activeInHierarchy && source.sprite != null;
                if (visible && key == null) UnkeyedVisibleParts++;
                Vector3 scale = source.transform.lossyScale; Color color = source.color;
                parts[i] = new SpritePartFrame
                {
                    Slot = binding.Slot, SpriteKey = key, Visible = visible && key != null,
                    Pose = PoseOf(origin.InverseTransformPoint(source.transform.position), inverse * source.transform.rotation,
                        new Vector3(Divide(scale.x, rootScale.x), Divide(scale.y, rootScale.y), Divide(scale.z, rootScale.z))),
                    Color = new System.Numerics.Vector4(color.r, color.g, color.b, color.a),
                    FlipX = source.flipX, FlipY = source.flipY, Layer = source.gameObject.layer,
                    SortingLayer = source.sortingLayerID, SortingOrder = source.sortingOrder
                };
            }
            return new PlayerFrame
            {
                PlayerId = 1, SceneEpoch = 1, SceneKey = Player.gameObject.scene.name, SampleTime = now,
                Root = PoseOf(origin.position, origin.rotation, rootScale), Parts = parts
            };
        }

        public SpriteRenderer Template(string slot)
        {
            if (_templates.TryGetValue(slot, out SpriteRenderer matched) && matched != null) return matched;
            foreach (Part part in _parts) if (part.Source != null) return part.Source;
            return null;
        }

        public void Clear()
        {
            Manager = null; Player = null; PlayerId = 0; SceneHandle = 0;
            _parts.Clear(); _templates.Clear(); UnkeyedVisibleParts = 0;
        }

        internal static Pose PoseOf(Vector3 position, Quaternion rotation, Vector3 scale) => new Pose
        {
            Position = new System.Numerics.Vector3(position.x, position.y, position.z),
            Rotation = new System.Numerics.Quaternion(rotation.x, rotation.y, rotation.z, rotation.w),
            Scale = new System.Numerics.Vector3(scale.x, scale.y, scale.z)
        };

        internal static void ApplyPose(Transform transform, Pose pose, bool local)
        {
            Vector3 position = new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z);
            Quaternion rotation = new Quaternion(pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W);
            if (local) { transform.localPosition = position; transform.localRotation = rotation; }
            else transform.SetPositionAndRotation(position, rotation);
            transform.localScale = new Vector3(pose.Scale.X, pose.Scale.Y, pose.Scale.Z);
        }

        private static float Divide(float value, float scale) => Math.Abs(scale) < 0.00001f ? 0 : value / scale;

        private static string SlotPath(Transform node, Transform root)
        {
            var names = new List<string>();
            for (int depth = 0; node != null && node != root && depth < 32; depth++, node = node.parent)
                names.Add(node.name + "[" + node.GetSiblingIndex() + "]");
            names.Reverse(); return string.Join("/", names);
        }
    }
}
