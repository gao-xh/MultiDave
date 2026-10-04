using System;
using DaveCoop.Core.World;
using DaveCoop.Rendering;
using DR.AI;
using Spine;
using Spine.Unity;
using UnityEngine;

namespace DaveCoop.Networking
{
    internal static class FishVisualCapture
    {
        public static FishVisual Capture(FishAISystem fish, SpriteCatalog sprites, SpineCatalog spines)
        {
            Renderer body = fish.FishBodyRenderer;
            var sprite = body == null ? null : body.TryCast<SpriteRenderer>();
            if (sprite != null)
            {
                string key = sprites.Register(sprite.sprite); if (key == null) return null;
                FishVisual visual = Common(fish.transform, sprite);
                visual.Kind = FishVisualKind.Sprite; visual.AssetKey = key;
                Color color = sprite.color; visual.Color = new System.Numerics.Vector4(color.r, color.g, color.b, color.a);
                visual.FlipX = sprite.flipX; visual.FlipY = sprite.flipY; visual.Validate(); return visual;
            }
            SkeletonRenderer renderer = null; TrackEntry track = null;
            var custom = fish.FishSpineAnimator;
            if (custom != null && custom.SpineAnimation != null)
            {
                renderer = custom.SpineAnimation;
                if (custom.SpineAnimationState != null) track = custom.SpineAnimationState.GetCurrent(0);
            }
            var animator = fish.FishAnimator;
            if (renderer == null && animator != null) renderer = animator.skeletonMecanim;
            if (renderer == null || renderer.Skeleton == null) return null;
            string assetKey = spines.Register(renderer.skeletonDataAsset); if (assetKey == null) return null;
            var mesh = renderer.GetComponent<MeshRenderer>(); if (mesh == null) return null;
            FishVisual result = Common(fish.transform, mesh); Skeleton skeleton = renderer.Skeleton;
            result.Kind = FishVisualKind.Spine; result.AssetKey = assetKey; result.Skin = skeleton.Skin?.Name;
            result.SkeletonScaleX = skeleton.ScaleX; result.SkeletonScaleY = skeleton.ScaleY;
            result.Color = new System.Numerics.Vector4(skeleton.R, skeleton.G, skeleton.B, skeleton.A);
            if (track != null && track.Animation != null)
            {
                result.Animation = track.Animation.Name; result.AnimationTime = track.TrackTime;
                result.AnimationRate = track.TimeScale * custom.SpineAnimation.timeScale; result.Loop = track.Loop;
            }
            else if (animator != null && animator.FishAnimator != null && animator.FishAnimator.layerCount > 0)
            {
                Animator native = animator.FishAnimator; var clips = native.GetCurrentAnimatorClipInfo(0);
                AnimationClip selected = null; float weight = -1;
                foreach (AnimatorClipInfo clip in clips) if (clip.clip != null && clip.weight > weight) { selected = clip.clip; weight = clip.weight; }
                if (selected != null && skeleton.Data.FindAnimation(selected.name) != null)
                {
                    AnimatorStateInfo state = native.GetCurrentAnimatorStateInfo(0);
                    result.Animation = selected.name; result.AnimationTime = Math.Max(0, state.normalizedTime * selected.length);
                    result.AnimationRate = native.speed * state.speed * state.speedMultiplier; result.Loop = state.loop;
                }
            }
            result.Validate(); return result;
        }

        private static FishVisual Common(Transform root, Renderer renderer)
        {
            Vector3 rootScale = root.lossyScale, scale = renderer.transform.lossyScale;
            return new FishVisual
            {
                LocalPose = LocalAvatarCapture.PoseOf(root.InverseTransformPoint(renderer.transform.position),
                    Quaternion.Inverse(root.rotation) * renderer.transform.rotation,
                    new Vector3(Divide(scale.x, rootScale.x), Divide(scale.y, rootScale.y), Divide(scale.z, rootScale.z))),
                Visible = renderer.enabled && renderer.gameObject.activeInHierarchy, Layer = renderer.gameObject.layer,
                SortingLayer = renderer.sortingLayerID, SortingOrder = renderer.sortingOrder
            };
        }
        private static float Divide(float value, float scale) => Math.Abs(scale) < 0.00001f ? 0 : value / scale;
    }
}
