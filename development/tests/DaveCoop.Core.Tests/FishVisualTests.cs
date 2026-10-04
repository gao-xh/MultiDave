using System;
using System.Numerics;
using DaveCoop.Core;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.World;

internal static class FishVisualTests
{
    internal static void CodecAndOwnership()
    {
        WorldSnapshot source = Snapshot(1, 1); WorldSlice slice = WorldFrames.Split(source)[0];
        source.Entities[0].Visual.Animation = "changed";
        Assert(slice.Entities[0].Visual.Animation == "swim", "visual data was not copied with entity state");
        var packet = new WirePacket { Kind = PacketKind.WorldSlice, Sequence = 1, RoomId = Guid.NewGuid().ToString("N"), World = slice };
        FishVisual decoded = PacketCodec.Decode(PacketCodec.Encode(packet)).World.Entities[0].Visual;
        Assert(decoded.Kind == FishVisualKind.Spine && decoded.Skin == "default" && decoded.Animation == "swim" && decoded.SkeletonScaleX == -1,
            "skeletal identity, skin, animation or facing lost on the wire");
        decoded = Visual(); decoded.Kind = FishVisualKind.Sprite; decoded.AssetKey = "sprite-v1:" + new string('a', 64);
        decoded.Skin = null; decoded.Animation = null; decoded.AnimationRate = 0; decoded.AnimationTime = 0; decoded.FlipX = true;
        source.Entities[0].Visual = decoded; source.Revision = 2;
        slice = WorldFrames.Split(source)[0]; packet.World = slice;
        Assert(PacketCodec.Decode(PacketCodec.Encode(packet)).World.Entities[0].Visual.FlipX, "sprite facing was lost");
    }

    internal static void InvalidVisuals()
    {
        FishVisual visual = Visual(); visual.AssetKey = "spine-v1:" + new string('G', 64); Reject(visual);
        visual = Visual(); visual.LocalPose = default; Reject(visual);
        visual = Visual(); visual.Animation = "swim\n"; Reject(visual);
        visual = Visual(); visual.AnimationTime = float.NaN; Reject(visual);
        visual = Visual(); visual.Color = new Vector4(float.PositiveInfinity); Reject(visual);
        visual = Visual(); visual.Layer = 32; Reject(visual);
        visual = Visual(); visual.SkeletonScaleX = 65; Reject(visual);
        visual = Visual(); visual.AnimationRate = 17; Reject(visual);
        visual = Visual(); visual.Kind = FishVisualKind.Sprite; visual.AssetKey = "sprite-v1:" + new string('a', 64); Reject(visual);
    }

    internal static void MaximumLegalPacketFits()
    {
        var entities = new EntityState[WorldFrames.EntitiesPerSlice];
        for (int i = 0; i < entities.Length; i++)
        {
            entities[i] = Entity(i + 1); entities[i].Visual.Animation = new string('\u4e00', 128);
            entities[i].Visual.Skin = new string('\u4e00', 128);
            entities[i].Id = long.MaxValue - i; entities[i].DataTid = int.MaxValue;
        }
        var snapshot = new WorldSnapshot { SceneEpoch = long.MaxValue, SceneKey = new string('\u4e00', 160), Revision = long.MaxValue, SampleTime = 100000000, Entities = entities };
        byte[] bytes = PacketCodec.Encode(new WirePacket
        {
            Kind = PacketKind.WorldSlice, Sequence = long.MaxValue, RoomId = Guid.NewGuid().ToString("N"), World = WorldFrames.Split(snapshot)[0]
        });
        Assert(bytes.Length < PacketCodec.MaxPacketBytes, "legal visual fields exceeded the framed transport limit");
    }

    internal static void PreviewInterpolationAndStaleness()
    {
        var buffer = new FishPreviewBuffer(); WorldSnapshot source = Snapshot(1, 10);
        buffer.Push(source, 100); source.Entities[0].Hp = 2; source.Entities[0].Visual.Animation = "changed";
        source = Snapshot(2, 10.2); source.Entities[0].Root = PoseAt(2); buffer.Push(source, 100.2);
        Assert(buffer.Sample(100.2, 0.1, out EntityState from, out EntityState to, out float alpha) && Math.Abs(alpha - 0.5f) < 0.001f,
            "fish source clock was not anchored or interpolation bracket was wrong");
        Assert(from.Hp == 10 && from.Visual.Animation == "swim" && to.Root.Position.X == 2, "preview retained mutable caller data");
        Assert(!buffer.Sample(101.21, 0.1, out _, out _, out _), "stale fish remained visible");
        for (int i = 3; i <= 50; i++) buffer.Push(Snapshot(i, 10 + i * 0.2), 100 + i * 0.2);
        Assert(buffer.Count == 16, "fish preview history exceeded the cap");
    }

    internal static void PreviewRemovalAndEpoch()
    {
        var buffer = new FishPreviewBuffer(); buffer.Push(Snapshot(1, 1), 10);
        var empty = Snapshot(2, 2); empty.Entities = Array.Empty<EntityState>(); buffer.Push(empty, 11);
        Assert(buffer.EntityId == 0 && !buffer.Sample(11, 0.1, out _, out _, out _), "removed fish remained selected");
        WorldSnapshot fresh = Snapshot(1, 0.1); fresh.SceneEpoch = 2; fresh.Entities[0].Id = 2;
        buffer.Push(fresh, 12); Assert(buffer.EntityId == 2 && buffer.Count == 1, "new epoch reused old fish history");
        Assert(!buffer.Push(Snapshot(99, 5), 13), "old epoch resurrected the first fish");
        var newer = Snapshot(2, 0.2); newer.SceneEpoch = 2; newer.Entities[0].Id = 2;
        Throws<ArgumentException>(() => buffer.Push(newer, 11));
        buffer.Clear(); Assert(buffer.EntityId == 0 && buffer.Count == 0, "disconnect retained preview selection");
    }

    private static void Reject(FishVisual visual) => Throws<ProtocolException>(() => visual.Validate());
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static Pose PoseAt(float x) => new Pose { Position = new Vector3(x, 0, 0), Rotation = Quaternion.Identity, Scale = Vector3.One };
    private static FishVisual Visual() => new FishVisual
    {
        Kind = FishVisualKind.Spine, AssetKey = "spine-v1:" + new string('a', 64), LocalPose = PoseAt(0), Color = Vector4.One,
        Visible = true, Skin = "default", Animation = "swim", AnimationRate = 1, SkeletonScaleX = -1
    };
    private static EntityState Entity(long id) => new EntityState
    { Id = id, Kind = EntityKind.Fish, DataTid = 2010007, Hp = 10, MaxHp = 10, Root = PoseAt(0), Visual = Visual() };
    private static WorldSnapshot Snapshot(long revision, double time) => new WorldSnapshot
    { SceneEpoch = 1, SceneKey = "dive", Revision = revision, SampleTime = time, Entities = new[] { Entity(1) } };
}
