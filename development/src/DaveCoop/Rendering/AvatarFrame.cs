using DaveCoop.Core;
using UnityEngine;
using Pose = DaveCoop.Core.Pose;

namespace DaveCoop.Rendering
{
    // Local visual cache. Native sprite references stay on the Unity thread.
    // The transport will send asset keys and numeric poses, never these references.
    internal sealed class AvatarFrame
    {
        public double Time;
        public Pose Root;
        public SpriteFrame[] Parts;
    }

    internal struct SpriteFrame
    {
        public Pose Pose;
        public Sprite Sprite;
        public Color Color;
        public bool Visible;
        public bool FlipX;
        public bool FlipY;
        public int Layer;
        public int SortingLayer;
        public int SortingOrder;
    }
}
