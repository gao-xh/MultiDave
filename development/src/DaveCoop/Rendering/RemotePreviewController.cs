using System;
using System.Collections.Generic;
using System.Text.Json;
using DaveCoop.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using NumericsQuaternion = System.Numerics.Quaternion;
using NumericsVector3 = System.Numerics.Vector3;
using UnityObject = UnityEngine.Object;
using Pose = DaveCoop.Core.Pose;

namespace DaveCoop.Rendering
{
    internal sealed class RemotePreviewController : IDisposable
    {
        private sealed class Part
        {
            public SpriteRenderer Source;
            public SpriteRenderer Display;
            public string Path;
            public readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
        }

        private readonly SnapshotTimeline<AvatarFrame> _history = new SnapshotTimeline<AvatarFrame>(120);
        private readonly List<Part> _parts = new List<Part>();
        private readonly HashSet<string> _warned = new HashSet<string>();
        private PlayerCharacter _source;
        private GameObject _root;
        private int _sourceId;
        private int _sourceScene;
        private float _nextOwnerCheck;
        private float _nextCapture;
        private float _nextLog;
        private bool _ready;

        public void Update()
        {
            try
            {
                if (!_ready)
                {
                    _ready = true;
                    RemotePreview.Logger.LogInfo("DAVECOOP_PREVIEW_READY: independent sprite-only actor; F10 toggles local replay.");
                }
                if (Input.GetKeyDown(KeyCode.F10)) RemotePreview.Enabled.Value = !RemotePreview.Enabled.Value;
                if (!RemotePreview.Enabled.Value)
                {
                    Cleanup("disabled");
                    RemotePreview.Status = "Second actor: disabled (F10)";
                    return;
                }
                float now = Time.unscaledTime;
                if (now < _nextOwnerCheck) return;
                _nextOwnerCheck = now + 0.5f;
                PlayerCharacter candidate = FindLocalPlayer();
                if (candidate == null)
                {
                    Cleanup("local player unavailable");
                    RemotePreview.Status = "Second actor: waiting for dive";
                    return;
                }
                int id = candidate.GetInstanceID();
                int scene = candidate.gameObject.scene.handle;
                if (_root != null && _source != null && id == _sourceId && scene == _sourceScene) return;
                Cleanup("owner or scene changed");
                Create(candidate);
            }
            catch (Exception error)
            {
                Warn("update", error);
                Cleanup("update failed");
                RemotePreview.Status = "Second actor: error (see log)";
            }
        }

        public void LateUpdate()
        {
            if (_root == null) return;
            try
            {
                if (_source == null || !_source.gameObject.activeInHierarchy ||
                    _source.gameObject.scene.handle != _sourceScene)
                {
                    Cleanup("source destroyed or left scene");
                    return;
                }
                float now = Time.unscaledTime;
                if (now >= _nextCapture)
                {
                    _nextCapture = now + 1f / 30f;
                    _history.TryPush(now, Capture(now));
                }
                double target = now - Math.Clamp(RemotePreview.Delay.Value, 0.1f, 2.5f);
                if (!_history.TrySample(target, out AvatarFrame from, out AvatarFrame to, out float amount)) return;
                Render(from, to, amount);
                RemotePreview.Status = $"Second actor: local replay | parts {_parts.Count} | F10 toggle";
                if (now >= _nextLog)
                {
                    _nextLog = now + 2f;
                    LogState(from.Time + (to.Time - from.Time) * amount);
                }
            }
            catch (Exception error)
            {
                Warn("render", error);
                Cleanup("render failed");
                RemotePreview.Status = "Second actor: error (see log)";
            }
        }

        private static PlayerCharacter FindLocalPlayer()
        {
            PlayerCharacter selected = null;
            int activeScene = SceneManager.GetActiveScene().handle;
            bool selectedInActiveScene = false;
            var managers = UnityObject.FindObjectsOfType<InGameManager>();
            foreach (InGameManager manager in managers)
            {
                if (manager == null) continue;
                PlayerCharacter player = manager.playerCharacter;
                if (player == null || !player.gameObject.activeInHierarchy) continue;
                bool inActiveScene = player.gameObject.scene.handle == activeScene;
                if (selected == null || (inActiveScene && !selectedInActiveScene))
                {
                    selected = player;
                    selectedInActiveScene = inActiveScene;
                }
                else if (inActiveScene == selectedInActiveScene && player.GetInstanceID() != selected.GetInstanceID())
                    return null;
            }
            return selected;
        }

        private void Create(PlayerCharacter source)
        {
            var sprites = source.GetComponentsInChildren<SpriteRenderer>(true);
            if (sprites.Length == 0 || sprites.Length > 128)
                throw new InvalidOperationException($"Unsupported avatar sprite count: {sprites.Length}.");
            _source = source;
            _sourceId = source.GetInstanceID();
            _sourceScene = source.gameObject.scene.handle;
            _root = new GameObject("MultiDave.RemotePreview");
            _root.hideFlags = HideFlags.DontSave;
            SceneManager.MoveGameObjectToScene(_root, source.gameObject.scene);
            for (int i = 0; i < sprites.Length; i++)
            {
                SpriteRenderer original = sprites[i];
                var node = new GameObject("part-" + i.ToString("D3"));
                node.hideFlags = HideFlags.DontSave;
                node.transform.SetParent(_root.transform, false);
                SpriteRenderer display = node.AddComponent<SpriteRenderer>();
                display.enabled = false;
                display.sharedMaterials = original.sharedMaterials;
                display.drawMode = original.drawMode;
                display.size = original.size;
                display.tileMode = original.tileMode;
                display.adaptiveModeThreshold = original.adaptiveModeThreshold;
                display.spriteSortPoint = original.spriteSortPoint;
                display.maskInteraction = original.maskInteraction;
                _parts.Add(new Part { Source = original, Display = display, Path = RelativePath(original.transform, source.transform) });
            }
            _nextCapture = 0f;
            _nextLog = 0f;
            RemotePreview.Logger.LogInfo("DAVECOOP_PREVIEW_SPAWN: " + JsonSerializer.Serialize(new
            {
                SourceId = _sourceId, Scene = source.gameObject.scene.name, SceneHandle = _sourceScene,
                RootId = _root.GetInstanceID(), Parts = _parts.Count,
                Components = "Transform, SpriteRenderer", DelaySeconds = RemotePreview.Delay.Value,
                Paths = _parts.ConvertAll(part => part.Path)
            }));
        }

        private AvatarFrame Capture(double time)
        {
            Transform origin = _source.transform;
            var frame = new AvatarFrame
            {
                Time = time,
                Root = PoseOf(origin.position, origin.rotation, origin.lossyScale),
                Parts = new SpriteFrame[_parts.Count]
            };
            Quaternion inverse = Quaternion.Inverse(origin.rotation);
            Vector3 rootScale = origin.lossyScale;
            for (int i = 0; i < _parts.Count; i++)
            {
                SpriteRenderer source = _parts[i].Source;
                if (source == null) continue;
                Vector3 scale = source.transform.lossyScale;
                frame.Parts[i] = new SpriteFrame
                {
                    Pose = PoseOf(origin.InverseTransformPoint(source.transform.position), inverse * source.transform.rotation,
                        new Vector3(Divide(scale.x, rootScale.x), Divide(scale.y, rootScale.y), Divide(scale.z, rootScale.z))),
                    Sprite = source.sprite,
                    Color = source.color,
                    Visible = source.enabled && source.gameObject.activeInHierarchy && source.sprite != null,
                    FlipX = source.flipX, FlipY = source.flipY, Layer = source.gameObject.layer,
                    SortingLayer = source.sortingLayerID, SortingOrder = source.sortingOrder
                };
            }
            return frame;
        }

        private void Render(AvatarFrame from, AvatarFrame to, float amount)
        {
            if (!from.Root.IsValid() || !to.Root.IsValid()) throw new InvalidOperationException("Invalid root pose.");
            // Avoid interpolating through a scripted teleport.
            bool teleport = NumericsVector3.DistanceSquared(from.Root.Position, to.Root.Position) > 144f;
            Pose root = teleport ? to.Root : Pose.Interpolate(from.Root, to.Root, amount);
            root.Position += new NumericsVector3(Math.Clamp(RemotePreview.OffsetX.Value, -10f, 10f), 0f, 0f);
            ApplyPose(_root.transform, root, false);
            for (int i = 0; i < _parts.Count; i++)
            {
                Part binding = _parts[i];
                SpriteFrame first = from.Parts[i];
                SpriteFrame second = to.Parts[i];
                SpriteFrame discrete = amount >= 1f || teleport ? second : first;
                binding.Display.enabled = false;
                if (!discrete.Visible || !first.Pose.IsValid() || !second.Pose.IsValid()) continue;
                ApplyPose(binding.Display.transform, teleport ? second.Pose : Pose.Interpolate(first.Pose, second.Pose, amount), true);
                binding.Display.sprite = discrete.Sprite;
                binding.Display.flipX = discrete.FlipX;
                binding.Display.flipY = discrete.FlipY;
                binding.Display.color = discrete.Color * new Color(0.45f, 0.85f, 1f, 0.8f);
                binding.Display.gameObject.layer = discrete.Layer;
                binding.Display.sortingLayerID = discrete.SortingLayer;
                binding.Display.sortingOrder = discrete.SortingOrder;
                if (binding.Source != null)
                {
                    binding.Source.GetPropertyBlock(binding.Properties);
                    binding.Display.SetPropertyBlock(binding.Properties);
                }
                binding.Display.enabled = true;
            }
        }

        private void LogState(double displayedTime)
        {
            int visible = 0;
            foreach (Part part in _parts) if (part.Display != null && part.Display.enabled) visible++;
            var players = UnityObject.FindObjectsOfType<PlayerCharacter>();
            var cameras = UnityObject.FindObjectsOfType<CameraManager>();
            bool cameraBound = false;
            foreach (CameraManager camera in cameras)
            {
                if (camera == null || camera.PrimaryTargetTransform == null) continue;
                if (camera.PrimaryTargetTransform.gameObject.GetInstanceID() == _source.gameObject.GetInstanceID()) cameraBound = true;
            }
            Vector3 sourcePosition = _source.transform.position;
            Vector3 displayPosition = _root.transform.position;
            RemotePreview.Logger.LogInfo("DAVECOOP_PREVIEW_STATE: " + JsonSerializer.Serialize(new
            {
                SourceId = _sourceId, RootId = _root.GetInstanceID(), SceneHandle = _sourceScene,
                Scene = _source.gameObject.scene.name, NativePlayers = players.Length,
                Parts = _parts.Count, VisibleParts = visible, LocalCameraBound = cameraBound,
                DisplayedTime = displayedTime, HistoryCount = _history.Count,
                SourcePosition = new[] { sourcePosition.x, sourcePosition.y, sourcePosition.z },
                DisplayPosition = new[] { displayPosition.x, displayPosition.y, displayPosition.z }
            }));
        }

        private void Cleanup(string reason)
        {
            bool existed = _root != null || _parts.Count > 0;
            if (_root != null)
            {
                _root.SetActive(false);
                UnityObject.Destroy(_root);
            }
            foreach (Part part in _parts) part.Properties.Dispose();
            _root = null; _source = null; _sourceId = 0; _sourceScene = 0;
            _parts.Clear(); _history.Clear();
            if (existed) RemotePreview.Logger.LogInfo("DAVECOOP_PREVIEW_CLEANUP: " + reason);
        }

        private void Warn(string key, Exception error)
        {
            if (_warned.Add(key)) RemotePreview.Logger.LogWarning($"DAVECOOP_PREVIEW_WARNING: {key}: {error.GetType().Name}: {error.Message}");
        }

        private static Pose PoseOf(Vector3 position, Quaternion rotation, Vector3 scale) => new Pose
        {
            Position = new NumericsVector3(position.x, position.y, position.z),
            Rotation = new NumericsQuaternion(rotation.x, rotation.y, rotation.z, rotation.w),
            Scale = new NumericsVector3(scale.x, scale.y, scale.z)
        };

        private static void ApplyPose(Transform transform, Pose pose, bool local)
        {
            var position = new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z);
            var rotation = new Quaternion(pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W);
            if (local) { transform.localPosition = position; transform.localRotation = rotation; }
            else transform.SetPositionAndRotation(position, rotation);
            transform.localScale = new Vector3(pose.Scale.X, pose.Scale.Y, pose.Scale.Z);
        }

        private static float Divide(float value, float scale) => Math.Abs(scale) < 0.00001f ? 0f : value / scale;

        private static string RelativePath(Transform node, Transform root)
        {
            var names = new List<string>();
            for (int depth = 0; node != null && node != root && depth < 32; depth++, node = node.parent) names.Add(node.name);
            names.Reverse();
            return string.Join("/", names);
        }

        public void Dispose() { Cleanup("plugin destroyed"); }
    }
}
