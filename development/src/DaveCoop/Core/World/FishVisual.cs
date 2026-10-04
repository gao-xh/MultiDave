using System;
using System.Numerics;
using DaveCoop.Core.Protocol;

namespace DaveCoop.Core.World
{
    public enum FishVisualKind { Sprite = 1, Spine = 2 }

    public sealed class FishVisual
    {
        public FishVisualKind Kind { get; set; }
        public string AssetKey { get; set; }
        public Pose LocalPose { get; set; }
        public Vector4 Color { get; set; }
        public bool Visible { get; set; }
        public bool FlipX { get; set; }
        public bool FlipY { get; set; }
        public int Layer { get; set; }
        public int SortingLayer { get; set; }
        public int SortingOrder { get; set; }
        public string Skin { get; set; }
        public string Animation { get; set; }
        public float AnimationTime { get; set; }
        public float AnimationRate { get; set; }
        public bool Loop { get; set; }
        public float SkeletonScaleX { get; set; } = 1;
        public float SkeletonScaleY { get; set; } = 1;

        public FishVisual Copy() => (FishVisual)MemberwiseClone();

        public void Validate()
        {
            if (!Enum.IsDefined(typeof(FishVisualKind), Kind)) throw new ProtocolException("Unknown fish visual kind.");
            string prefix = Kind == FishVisualKind.Sprite ? "sprite-v1:" : "spine-v1:";
            if (AssetKey == null || AssetKey.Length != prefix.Length + 64 || !AssetKey.StartsWith(prefix, StringComparison.Ordinal))
                throw new ProtocolException("Invalid fish visual resource key.");
            for (int i = prefix.Length; i < AssetKey.Length; i++)
                if (!((AssetKey[i] >= '0' && AssetKey[i] <= '9') || (AssetKey[i] >= 'a' && AssetKey[i] <= 'f')))
                    throw new ProtocolException("Invalid fish visual resource hash.");
            PacketCodec.ValidatePose(LocalPose);
            if (!ColorValue(Color.X) || !ColorValue(Color.Y) || !ColorValue(Color.Z) || !ColorValue(Color.W) ||
                Layer < 0 || Layer > 31 || SortingOrder < -32768 || SortingOrder > 32767)
                throw new ProtocolException("Invalid fish visual rendering values.");
            if (Skin != null) PacketCodec.RequireText(Skin, 128, "fish skin");
            if (Animation != null) PacketCodec.RequireText(Animation, 128, "fish animation");
            if (!float.IsFinite(AnimationTime) || AnimationTime < 0 || AnimationTime > 1000000 ||
                !float.IsFinite(AnimationRate) || Math.Abs(AnimationRate) > 16 ||
                !float.IsFinite(SkeletonScaleX) || Math.Abs(SkeletonScaleX) > 64 ||
                !float.IsFinite(SkeletonScaleY) || Math.Abs(SkeletonScaleY) > 64)
                throw new ProtocolException("Invalid fish animation values.");
            if (Kind == FishVisualKind.Sprite && (Skin != null || Animation != null || AnimationTime != 0 || AnimationRate != 0))
                throw new ProtocolException("Sprite fish includes skeletal animation data.");
        }

        private static bool ColorValue(float value) => float.IsFinite(value) && value >= 0 && value <= 4;
    }
}
