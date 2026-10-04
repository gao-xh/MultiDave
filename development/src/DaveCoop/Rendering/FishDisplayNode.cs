using System;
using DaveCoop.Core.World;
using DaveCoop.Networking;
using Spine;
using Spine.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityObject = UnityEngine.Object;
using Pose = DaveCoop.Core.Pose;

namespace DaveCoop.Rendering
{
    // Main-thread display only. Every GameObject below belongs to this Mod;
    // no fish AI, collider, damage, pickup, reward or original node is copied.
    internal sealed class FishDisplayNode : IDisposable
    {
        private readonly string _namePrefix;
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
        public bool HasNodes => _root != null;
        public bool Visible { get; private set; }
        public bool Renderable { get; private set; }
        public bool UnknownResource { get; private set; }
        public bool InView { get; private set; }
        public int MeshVertices { get; private set; }
        public Vector3 DisplayPosition { get; private set; }
        public string DisplayStatus { get; private set; } = "Cleared";
        public string CleanupError { get; private set; }

        public FishDisplayNode(string namePrefix) { _namePrefix = namePrefix; }

        public void Render(EntityState from, EntityState to, float alpha, Scene scene,
            SpriteCatalog sprites, SpineCatalog spines, bool loopback, bool cullOutsideCamera = false)
        {
            ResetFlags(); CleanupError = null;
            bool teleport = System.Numerics.Vector3.DistanceSquared(from.Root.Position, to.Root.Position) > 144;
            EntityState discrete = alpha >= 1 || teleport ? to : from; FishVisual visual = discrete.Visual;
            if (visual == null) { Hide("MissingVisual"); return; }
            if (!visual.Visible) { Hide("SourceInvisible"); return; }
            Sprite sprite = null; SkeletonDataAsset skeleton = null;
            if (visual.Kind == FishVisualKind.Sprite)
            {
                if (!sprites.TryResolve(visual.AssetKey, out sprite)) sprites.ScanLoadedSprites(Time.unscaledTime);
                if (!sprites.TryResolve(visual.AssetKey, out sprite)) { Hide("UnknownSprite"); UnknownResource = true; return; }
            }
            else
            {
                if (!spines.TryResolve(visual.AssetKey, out skeleton)) spines.Scan(Time.unscaledTime);
                if (!spines.TryResolve(visual.AssetKey, out skeleton)) { Hide("UnknownSkeleton"); UnknownResource = true; return; }
            }
            if (_root == null || _entityId != discrete.Id || _assetKey != visual.AssetKey || _kind != visual.Kind || _sceneHandle != scene.handle)
            {
                DestroyNodes(); _entityId = discrete.Id; _assetKey = visual.AssetKey; _kind = visual.Kind; _sceneHandle = scene.handle;
                _root = new GameObject(_namePrefix + "-" + discrete.Id) { hideFlags = HideFlags.DontSave };
                _root.SetActive(false); SceneManager.MoveGameObjectToScene(_root, scene);
                _model = new GameObject("Display") { hideFlags = HideFlags.DontSave }; _model.transform.SetParent(_root.transform, false);
                if (_kind == FishVisualKind.Sprite) { _sprite = _model.AddComponent<SpriteRenderer>(); _renderer = _sprite; }
                else
                {
                    _skeleton = SkeletonAnimation.AddToGameObject(_model, skeleton, false);
                    if (_skeleton.Skeleton == null) _skeleton.Initialize(false, false);
                    _skeleton.enabled = false; _renderer = _model.GetComponent<MeshRenderer>();
                }
            }
            if (_renderer == null) throw new InvalidOperationException("Fish display renderer was not created.");
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
                        if (data.Data.FindSkin(visual.Skin) == null) { Hide("MissingSkin"); UnknownResource = true; return; }
                        data.SetSkin(visual.Skin);
                    }
                    else data.SetSkin((Skin)null);
                    data.SetSlotsToSetupPose(); _skin = visual.Skin;
                }
                data.ScaleX = visual.SkeletonScaleX; data.ScaleY = visual.SkeletonScaleY;
                data.R = color.r; data.G = color.g; data.B = color.b; data.A = color.a;
                if (visual.Animation != null)
                {
                    if (data.Data.FindAnimation(visual.Animation) == null) { Hide("MissingAnimation"); UnknownResource = true; return; }
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
                else
                {
                    // A source without a primary animation must not keep the
                    // preceding track's pose. This node owns track zero only.
                    if (_animation != null || _skeleton.AnimationState.GetCurrent(0) != null)
                        _skeleton.AnimationState.ClearTrack(0);
                    data.SetToSetupPose(); _animation = null; _loop = false;
                }
                _skeleton.Update(0); _skeleton.LateUpdateMesh();
            }
            DisplayPosition = _model.transform.position;
            Camera camera = Camera.main;
            if (camera != null)
            {
                Vector3 viewport = camera.WorldToViewportPoint(DisplayPosition);
                InView = viewport.z > 0 && viewport.x >= 0 && viewport.x <= 1 && viewport.y >= 0 && viewport.y <= 1;
                if (cullOutsideCamera && (camera.cullingMask & (1 << visual.Layer)) == 0) InView = false;
            }
            if (_skeleton != null)
            {
                MeshFilter filter = _model.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null) MeshVertices = filter.sharedMesh.vertexCount;
            }
            Renderable = true;
            if (cullOutsideCamera && camera != null && !InView)
            {
                _root.SetActive(false); DisplayStatus = "OutsideCamera"; return;
            }
            _root.SetActive(true); _renderer.enabled = true; Visible = true;
            DisplayStatus = InView ? "Visible" : "OutsideCamera";
        }

        public void Hide(string reason)
        {
            ResetFlags(); DisplayStatus = reason;
            if (_root != null) _root.SetActive(false);
        }

        private void ResetFlags() { Visible = false; Renderable = false; UnknownResource = false; InView = false; MeshVertices = 0; }
        private void DestroyNodes()
        {
            GameObject ownedRoot = _root;
            // Drop every managed renderer/mesh owner reference even if Unity's
            // deferred destruction reports an error. Never destroy shared assets.
            _root = null; _model = null; _sprite = null; _skeleton = null; _renderer = null; _assetKey = null; _skin = null; _animation = null;
            if (ownedRoot != null)
            {
                try { ownedRoot.SetActive(false); }
                finally { UnityObject.Destroy(ownedRoot); }
            }
        }
        public void Clear(string reason = "Cleared")
        {
            CleanupError = null;
            try { DestroyNodes(); }
            catch (Exception error) { CleanupError = error.GetType().Name + ": " + error.Message; }
            ResetFlags(); DisplayStatus = CleanupError == null ? reason : "CleanupError";
        }
        public void Dispose() => Clear();
    }
}
