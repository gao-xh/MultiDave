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

    internal static void PreviewViewportAndNearestSelection()
    {
        var buffer = new FishPreviewBuffer(); WorldSnapshot source = Snapshot(1, 1);
        source.Entities = new[] { Entity(1), Entity(2), Entity(3), Entity(4) };
        source.Entities[0].Root = PoseAt(100); source.Entities[1].Root = PoseAt(2);
        source.Entities[2].Root = PoseAt(1); source.Entities[3].Visual.Visible = false;
        buffer.Push(source, 10, Vector3.Zero, entity => entity.Id != 1 && entity.Id != 3);
        Assert(buffer.EntityId == 2, "preview chose the first/offscreen/invisible fish instead of an eligible nearby fish");
        source.Revision = 2; source.SampleTime = 2; source.Entities[1].Dead = true;
        buffer.Push(source, 11, Vector3.Zero, entity => entity.Id != 1);
        Assert(buffer.EntityId == 3 && buffer.Count == 1, "dead selected fish retained its display/history");
        source.Revision = 3; source.SampleTime = 3;
        buffer.Push(source, 12, Vector3.Zero, entity => false);
        Assert(buffer.EntityId == 3 && buffer.Count == 2, "camera eligibility released a live selected fish or reset its history");
        source.Revision = 4; source.SampleTime = 4; source.Entities[2].Captured = true;
        buffer.Push(source, 13, Vector3.Zero, entity => false);
        Assert(buffer.EntityId == 0 && buffer.Count == 0, "captured selection survived without an eligible replacement");
    }

    internal static void PreviewSelectionIdentityRetention()
    {
        var buffer = new FishPreviewBuffer(); WorldSnapshot source = Snapshot(1, 1);
        source.Entities = new[] { Entity(1), Entity(2) };
        source.Entities[0].Root = PoseAt(2); source.Entities[1].Root = PoseAt(2.1f);
        buffer.Push(source, 10, Vector3.Zero);
        source.Revision = 2; source.SampleTime = 2; source.Entities[0].Root = PoseAt(2.1f); source.Entities[1].Root = PoseAt(2);
        buffer.Push(source, 11, Vector3.Zero);
        Assert(buffer.EntityId == 1 && buffer.Count == 2, "nearby fish alternation caused selection flicker");
        source.Revision = 3; source.SampleTime = 3; source.Entities[0].Root = PoseAt(20); source.Entities[1].Root = PoseAt(1);
        buffer.Push(source, 12, Vector3.Zero);
        Assert(buffer.EntityId == 1 && buffer.Count == 3, "a nearer fish replaced the selected identity or reset its history");
        Assert(buffer.LastSelectionReason == "retained", "identity retention was not diagnosed");
    }

    internal static void PreviewTemporaryVisualsRetainIdentity()
    {
        var buffer = new FishPreviewBuffer(); WorldSnapshot source = Snapshot(1, 1);
        source.Entities = new[] { Entity(1), Entity(2) };
        source.Entities[0].Root = PoseAt(1); source.Entities[1].Root = PoseAt(2);
        buffer.Push(source, 10, Vector3.Zero);
        Assert(buffer.LastSelectionReason == "initial-selection", "initial nearest selection was not diagnosed");
        source.Revision = 2; source.SampleTime = 2; source.Entities[0].Visual.Visible = false;
        source.Entities[0].Root = PoseAt(10); source.Entities[1].Root = PoseAt(1);
        buffer.Push(source, 11, Vector3.Zero, entity => entity.Id == 2);
        Assert(buffer.EntityId == 1 && buffer.Count == 2, "temporary source invisibility switched to a different fish");
        Assert(buffer.Sample(11, 0, out _, out EntityState invisible, out _) && !invisible.Visual.Visible,
            "temporary visibility state did not remain part of the selected identity's history");
        source.Revision = 3; source.SampleTime = 3; source.Entities[0].Visual = null;
        buffer.Push(source, 12, Vector3.Zero, entity => false);
        Assert(buffer.EntityId == 1 && buffer.Count == 3 && buffer.LastSelectionReason == "retained",
            "missing display metadata or camera rejection released the live selected identity");
        Assert(buffer.Sample(12, 0, out _, out EntityState missing, out _) && missing.Visual == null,
            "missing display metadata did not update the retained entity state");
        source.Revision = 4; source.SampleTime = 4; source.Entities[0].Visual = Visual();
        buffer.Push(source, 13, Vector3.Zero);
        Assert(buffer.EntityId == 1 && buffer.Count == 4 && buffer.Sample(13, 0, out _, out EntityState restored, out _) && restored.Visual.Visible,
            "display recovery selected another fish or lost the existing history");
    }

    internal static void PreviewManualReselectionPreservesReplayFence()
    {
        var buffer = new FishPreviewBuffer(); WorldSnapshot source = Snapshot(1, 1); source.SceneEpoch = 2;
        source.Entities = new[] { Entity(1), Entity(2) };
        source.Entities[0].Root = PoseAt(1); source.Entities[1].Root = PoseAt(3);
        buffer.Push(source, 10, Vector3.Zero);
        source.Revision = 2; source.SampleTime = 2; source.Entities[0].Root = PoseAt(100); source.Entities[1].Root = PoseAt(1);
        buffer.Push(source, 11, Vector3.Zero); buffer.RequestReselect();
        Assert(buffer.EntityId == 1 && buffer.Count == 2, "manual reselection destroyed the current display before a fresh world arrived");
        Assert(!buffer.Push(source, 12, Vector3.Zero), "manual reselection accepted the previously applied revision");
        Assert(!buffer.Push(Snapshot(999, 8), 12.1, Vector3.Zero), "manual reselection accepted a retired epoch");
        var replay = Snapshot(1, 1); replay.SceneEpoch = 2;
        Assert(!buffer.Push(replay, 12.2, Vector3.Zero), "manual reselection accepted an older revision");
        Assert(buffer.EntityId == 1 && buffer.Count == 2, "replayed world mutated a pending manual reselection");
        source.Revision = 3; source.SampleTime = 3;
        buffer.Push(source, 13, Vector3.Zero);
        Assert(buffer.EntityId == 2 && buffer.Count == 1 && buffer.LastSelectionReason == "manual-nearest",
            "manual reselection did not pick the fresh nearest eligible fish or retained old identity history");
        Assert(buffer.Sample(13, 0, out EntityState selected, out _, out _) && selected.Id == 2,
            "old selected fish leaked into the manually selected fish's first frame");
    }

    internal static void PreviewTerminalSelectionReasons()
    {
        var buffer = new FishPreviewBuffer(); WorldSnapshot source = Snapshot(1, 1);
        source.Entities = new[] { Entity(1), Entity(2), Entity(3), Entity(4) };
        for (int i = 0; i < source.Entities.Length; i++) source.Entities[i].Root = PoseAt(i + 1);
        buffer.Push(source, 10, Vector3.Zero);
        source.Revision = 2; source.SampleTime = 2; source.Entities[0].Dead = true;
        buffer.Push(source, 11, Vector3.Zero);
        Assert(buffer.EntityId == 2 && buffer.Count == 1 && buffer.LastSelectionReason == "selected-dead",
            "death did not release the selected fish and diagnose its replacement");
        source.Revision = 3; source.SampleTime = 3; source.Entities[1].Captured = true;
        buffer.Push(source, 12, Vector3.Zero);
        Assert(buffer.EntityId == 3 && buffer.Count == 1 && buffer.LastSelectionReason == "selected-captured",
            "capture did not release the selected fish and diagnose its replacement");
        source.Revision = 4; source.SampleTime = 4;
        source.Entities = new[] { source.Entities[0], source.Entities[1], source.Entities[3] };
        buffer.Push(source, 13, Vector3.Zero);
        Assert(buffer.EntityId == 4 && buffer.Count == 1 && buffer.LastSelectionReason == "selected-removed",
            "complete-roster removal did not release the selected fish and diagnose its replacement");
        var unavailable = new FishPreviewBuffer(); WorldSnapshot hidden = Snapshot(1, 1); hidden.Entities[0].Visual.Visible = false;
        unavailable.Push(hidden, 10, Vector3.Zero);
        Assert(unavailable.EntityId == 0 && unavailable.Count == 0 && unavailable.LastSelectionReason == "no-visible-candidate",
            "an initially invisible fish became selected or lacked a no-candidate diagnosis");
    }

    internal static void PreviewSamplingReasonsAndRecovery()
    {
        var buffer = new FishPreviewBuffer();
        Assert(!buffer.Sample(0, 0.1, out _, out _, out _) && buffer.LastSampleStatus == "EmptyHistory",
            "empty preview sampling lacked its own diagnosis");
        buffer.Push(Snapshot(1, 1), 10);
        Assert(buffer.Sample(10, 0.1, out _, out _, out _) && buffer.LastSampleStatus == "Ready" && buffer.SampleAge(10) == 0,
            "first selected frame was unavailable or had an incorrect arrival age");
        Assert(!buffer.Sample(9.9, 0.1, out _, out _, out _) && buffer.LastSampleStatus == "BeforeArrival",
            "sampling before arrival lacked its own diagnosis");
        Assert(buffer.Sample(11, 0.1, out _, out _, out _) && buffer.LastSampleStatus == "Ready" && buffer.SampleAge(11) == 1,
            "the exact one-second freshness boundary was rejected");
        Assert(!buffer.Sample(11.001, 0.1, out _, out _, out _) && buffer.LastSampleStatus == "Stale",
            "a stale selected fish lacked its own diagnosis");
        Assert(!buffer.Sample(double.NaN, 0.1, out _, out _, out _) && buffer.LastSampleStatus == "InvalidClock",
            "non-finite sampling clock lacked its own diagnosis");
        Assert(!buffer.Sample(11, -0.1, out _, out _, out _) && buffer.LastSampleStatus == "InvalidClock",
            "invalid interpolation delay lacked its own diagnosis");
        buffer.Push(Snapshot(2, 1.2), 11.2);
        Assert(buffer.EntityId == 1 && buffer.Count == 2 && buffer.Sample(11.3, 0.1, out _, out _, out _) &&
            buffer.LastSampleStatus == "Ready" && Math.Abs(buffer.SampleAge(11.3) - 0.1) < 0.0001,
            "fresh world did not recover the same identity after a stale display");
    }

    internal static void PreviewInvalidViewer()
    {
        var buffer = new FishPreviewBuffer();
        Throws<ArgumentException>(() => buffer.Push(Snapshot(1, 1), 10, new Vector3(float.NaN, 0, 0)));
        Assert(buffer.EntityId == 0 && buffer.Count == 0, "invalid viewer mutated preview state");
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
