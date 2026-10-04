using System;
using System.Text.Json;
using DaveCoop.Core.World;
using DaveCoop.Rendering;
using Spine;
using Spine.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityObject = UnityEngine.Object;
using Pose = DaveCoop.Core.Pose;

namespace DaveCoop.Networking
{
    internal sealed class RemoteFishPreview
    {
        private readonly FishPreviewBuffer _motion = new FishPreviewBuffer();
        private GameObject _root;
        private GameObject _model;
        private SpriteRenderer _sprite;
        private SkeletonAnimation _skeleton;
        private Renderer _renderer;
        private string _assetKey;
        private FishVisualKind _kind;
        private long _entityId;
        private int _sceneHandle;
        private string _skin;
        private string _animation;
        private bool _loop;
        public long SelectedEntity => _motion.EntityId;
        public bool Visible { get; private set; }
        public bool UnknownResource { get; private set; }
        public bool InView { get; private set; }
        public int MeshVertices { get; private set; }
        private Vector3 _displayPosition;
        private string _lastTrace;
        private int _traceCount;
        public string DisplayStatus { get; private set; } = "Cleared";
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
            Visible = false; UnknownResource = false; InView = false; MeshVertices = 0;
            SnapshotAge = _motion.SampleAge(now);
            if (_motion.EntityId == 0) { DisplayStatus = "NoSelection"; DestroyNodes(); return; }
            if (!local.IsAvailable) { Hide("LocalUnavailable"); return; }
            if (!_motion.Sample(now, delay, out EntityState from, out EntityState to, out float alpha)) { Hide(_motion.LastSampleStatus); return; }
            bool teleport = System.Numerics.Vector3.DistanceSquared(from.Root.Position, to.Root.Position) > 144;
            EntityState discrete = alpha >= 1 || teleport ? to : from; FishVisual visual = discrete.Visual;
            if (visual == null) { Hide("MissingVisual"); return; }
            if (!visual.Visible) { Hide("SourceInvisible"); return; }
            Sprite sprite = null; SkeletonDataAsset skeleton = null;
            if (visual.Kind == FishVisualKind.Sprite)
            {
                if (!sprites.TryResolve(visual.AssetKey, out sprite)) sprites.ScanLoadedSprites(Time.unscaledTime);
                if (!sprites.TryResolve(visual.AssetKey, out sprite)) { UnknownResource = true; Hide("UnknownSprite"); return; }
            }
            else
            {
                if (!spines.TryResolve(visual.AssetKey, out skeleton)) spines.Scan(Time.unscaledTime);
                if (!spines.TryResolve(visual.AssetKey, out skeleton)) { UnknownResource = true; Hide("UnknownSkeleton"); return; }
            }
            if (_root == null || _entityId != discrete.Id || _assetKey != visual.AssetKey || _kind != visual.Kind || _sceneHandle != local.SceneHandle)
            {
                DestroyNodes(); _entityId = discrete.Id; _assetKey = visual.AssetKey; _kind = visual.Kind; _sceneHandle = local.SceneHandle;
                _root = new GameObject("MultiDave.RemoteFishPreview-" + discrete.Id) { hideFlags = HideFlags.DontSave };
                _root.SetActive(false); SceneManager.MoveGameObjectToScene(_root, local.Player.gameObject.scene);
                _model = new GameObject("Display") { hideFlags = HideFlags.DontSave }; _model.transform.SetParent(_root.transform, false);
                if (_kind == FishVisualKind.Sprite) { _sprite = _model.AddComponent<SpriteRenderer>(); _renderer = _sprite; }
                else
                {
                    _skeleton = SkeletonAnimation.AddToGameObject(_model, skeleton, false);
                    if (_skeleton.Skeleton == null) _skeleton.Initialize(false, false);
                    _skeleton.enabled = false; _renderer = _model.GetComponent<MeshRenderer>();
                }
            }
            Pose root = teleport ? to.Root : Pose.Interpolate(from.Root, to.Root, alpha);
            if (loopback) root.Position += new System.Numerics.Vector3(3, 0, 0);
            LocalAvatarCapture.ApplyPose(_root.transform, root, false); LocalAvatarCapture.ApplyPose(_model.transform, visual.LocalPose, true);
            _model.layer = visual.Layer; _renderer.sortingLayerID = visual.SortingLayer; _renderer.sortingOrder = visual.SortingOrder;
            Color color = new Color(visual.Color.X, visual.Color.Y, visual.Color.Z, visual.Color.W);
            if (loopback) color *= new Color(0.55f, 0.9f, 1, 0.75f);
            if (_sprite != null) { _sprite.sprite = sprite; _sprite.color = color; _sprite.flipX = visual.FlipX; _sprite.flipY = visual.FlipY; }
            if (_skeleton != null)
            {
                Skeleton data = _skeleton.Skeleton;
                if (_skin != visual.Skin)
                {
                    if (visual.Skin != null)
                    {
                        if (data.Data.FindSkin(visual.Skin) == null) { UnknownResource = true; Hide("MissingSkin"); return; }
                        data.SetSkin(visual.Skin);
                    }
                    else data.SetSkin((Skin)null);
                    data.SetSlotsToSetupPose(); _skin = visual.Skin;
                }
                data.ScaleX = visual.SkeletonScaleX; data.ScaleY = visual.SkeletonScaleY;
                data.R = color.r; data.G = color.g; data.B = color.b; data.A = color.a;
                if (visual.Animation != null)
                {
                    if (data.Data.FindAnimation(visual.Animation) == null) { UnknownResource = true; Hide("MissingAnimation"); return; }
                    if (_animation != visual.Animation || _loop != visual.Loop)
                    {
                        TrackEntry started = _skeleton.AnimationState.SetAnimation(0, visual.Animation, visual.Loop);
                        started.MixDuration = 0;
                        _animation = visual.Animation; _loop = visual.Loop;
                    }
                    TrackEntry track = _skeleton.AnimationState.GetCurrent(0);
                    float animationTime = visual.AnimationTime;
                    if (!teleport && from.Visual?.Animation == to.Visual?.Animation && from.Visual != null && to.Visual != null &&
                        to.Visual.AnimationTime >= from.Visual.AnimationTime)
                        animationTime = from.Visual.AnimationTime + (to.Visual.AnimationTime - from.Visual.AnimationTime) * alpha;
                    if (track != null) { track.TrackTime = animationTime; track.TimeScale = 0; }
                }
                _skeleton.Update(0); _skeleton.LateUpdateMesh();
            }
            _root.SetActive(true); _renderer.enabled = true; Visible = true;
            _displayPosition = _model.transform.position;
            Camera activeCamera = Camera.main;
            if (activeCamera != null)
            {
                Vector3 viewport = activeCamera.WorldToViewportPoint(_displayPosition);
                InView = viewport.z > 0 && viewport.x >= 0 && viewport.x <= 1 && viewport.y >= 0 && viewport.y <= 1;
            }
            if (_skeleton != null)
            {
                MeshFilter filter = _model.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null) MeshVertices = filter.sharedMesh.vertexCount;
            }
            DisplayStatus = InView ? "Visible" : "OutsideCamera";
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
            NetworkDriver.Logger.LogInfo("DAVECOOP_FISH_PREVIEW_TRANSITION: " + JsonSerializer.Serialize(new
            {
                SelectedEntity, DisplayStatus, Visible, InView, UnknownResource, MeshVertices, SnapshotAge,
                Position = new[] { _displayPosition.x, _displayPosition.y, _displayPosition.z }
            }));
        }

        public void DrawMarker()
        {
            if (!Visible || !InView || _root == null) return;
            Camera camera = Camera.main; if (camera == null) return;
            Vector3 screen = camera.WorldToScreenPoint(_displayPosition); if (screen.z <= 0) return;
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

        private void Hide(string reason) { DisplayStatus = reason; if (_root != null) _root.SetActive(false); }
        private void DestroyNodes()
        {
            if (_root != null) UnityObject.Destroy(_root);
            _root = null; _model = null; _sprite = null; _skeleton = null; _renderer = null; _assetKey = null; _skin = null; _animation = null;
        }
        public void Clear(string reason = "Cleared")
        {
            DestroyNodes(); _motion.Clear(); Visible = false; UnknownResource = false; InView = false; MeshVertices = 0;
            DisplayStatus = reason; SnapshotAge = 0; Trace();
        }
    }
}
