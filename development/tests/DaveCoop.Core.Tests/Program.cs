using System;
using System.Numerics;
using DaveCoop.Core;

internal static class Program
{
    private static int Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("timeline boundaries and interpolation", TimelineBoundaries),
            ("timeline capacity, ordering and reset", TimelineCapacity),
            ("pose interpolation and quaternion hemisphere", PoseInterpolation),
            ("invalid pose data", InvalidPoses),
            ("network frame numeric round trip", NetworkTests.CodecRoundTrip),
            ("network malformed data rejection", NetworkTests.CodecRejectsInvalidData),
            ("fragmented and truncated stream frames", () => NetworkTests.FragmentedPackets().GetAwaiter().GetResult()),
            ("TCP handshake and bidirectional frames", () => NetworkTests.TcpHandshakeAndFrames().GetAwaiter().GetResult()),
            ("TCP incompatible build rejection", () => NetworkTests.TcpRejectsIncompatibleBuild().GetAwaiter().GetResult()),
            ("TCP concurrent writes", () => NetworkTests.ConcurrentWrites().GetAwaiter().GetResult()),
            ("invalid outbound frame sequence recovery", () => NetworkTests.InvalidSendDoesNotConsumeSequence().GetAwaiter().GetResult()),
            ("TCP replay rejection and disconnect", () => NetworkTests.ReplayAndDisconnect().GetAwaiter().GetResult()),
            ("TCP blocked read cancellation", () => NetworkTests.CancelBlockedRead().GetAwaiter().GetResult()),
            ("scene and world agreement before frames", SessionTests.SceneAgreement),
            ("scene suspension and old epoch cleanup", SessionTests.SceneTransitions),
            ("guest reload establishes new epoch", SessionTests.GuestReload),
            ("session room, player and authority rejection", SessionTests.InvalidSessionPackets),
            ("heartbeat round trip and process clock offset", SessionTests.ClockEstimate),
            ("bounded frame mailboxes and DTO ownership", SessionTests.MailboxAndOwnership),
            ("session timeouts and queue bounds", SessionTests.TimeoutsAndQueueBounds),
            ("LAN session scene, movement and graceful leave", () => SessionTests.LanSessionLifecycle().GetAwaiter().GetResult()),
            ("LAN scene mismatch timeout", () => SessionTests.SceneMismatchTimeout().GetAwaiter().GetResult()),
            ("LAN silent handshake timeout", () => SessionTests.SilentHandshakeTimeout().GetAwaiter().GetResult()),
            ("LAN listening and connected cancellation", () => SessionTests.CancelListeningAndConnected().GetAwaiter().GetResult()),
            ("sprite asset key culture and signed zero stability", AssetTests.StableKeys),
            ("sprite asset key descriptor identity", AssetTests.DistinctDescriptors),
            ("sprite asset invalid descriptor rejection", AssetTests.InvalidDescriptors),
            ("sprite asset metadata collision rejection", AssetTests.RegistryCollisions),
            ("sprite asset capacity and scene reset", AssetTests.RegistryBoundsAndClear),
            ("layout ordering and multiplicity", WorldAndMotionTests.LayoutOrderAndMultiplicity),
            ("layout geometry and field boundaries", WorldAndMotionTests.LayoutGeometryAndFieldBoundaries),
            ("layout invalid and excessive input rejection", WorldAndMotionTests.LayoutRejectsInvalidInput),
            ("remote motion clock anchoring and staleness", WorldAndMotionTests.MotionClockAndInterpolation),
            ("remote motion epoch, capacity and reset", WorldAndMotionTests.MotionEpochCapacityAndReset),
            ("remote motion malformed data rejection", WorldAndMotionTests.MotionRejectsInvalidData),
            ("host entity identity and pool reuse", EntityWorldTests.EntityIdentityAndReuse),
            ("host entity capacity and replacement", EntityWorldTests.RegistryCapacity),
            ("world codec and malformed entity rejection", EntityWorldTests.CodecAndInvalidEntities),
            ("world atomic assembly and DTO ownership", EntityWorldTests.AtomicAssemblyAndOwnership),
            ("world slice ordering, replacement and empty roster", EntityWorldTests.AssemblyRejectionAndReplacement),
            ("world session authority and epoch cleanup", EntityWorldTests.SessionAuthorityAndEpoch),
            ("world mailbox capacity and player fairness", EntityWorldTests.SnapshotFairnessAndBounds),
            ("TCP world bootstrap, update and removal", () => EntityWorldTests.TcpWorldLifecycle().GetAwaiter().GetResult()),
            ("TCP legacy world protocol rejection", () => EntityWorldTests.RejectLegacyProtocol().GetAwaiter().GetResult()),
            ("fish visual numeric wire and DTO ownership", FishVisualTests.CodecAndOwnership),
            ("fish visual malformed data rejection", FishVisualTests.InvalidVisuals),
            ("fish visual maximum fields fit packet limit", FishVisualTests.MaximumLegalPacketFits),
            ("fish preview clock, interpolation and staleness", FishVisualTests.PreviewInterpolationAndStaleness),
            ("fish preview removal and epoch reset", FishVisualTests.PreviewRemovalAndEpoch),
            ("slow world producer cannot starve atomic commit", EntityWorldTests.SlowWorldProducerCannotStarveCommit)
        };
        int failures = 0;
        foreach (var test in tests)
        {
            try { test.Run(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + test.Name + ": " + error.Message); }
        }
        Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void TimelineBoundaries()
    {
        var timeline = new SnapshotTimeline<string>(4);
        Assert(!timeline.TrySample(0, out _, out _, out _), "empty buffer should not sample");
        Assert(timeline.TryPush(10, "a") && timeline.TryPush(12, "b"), "push rejected");
        Assert(timeline.TrySample(11, out string from, out string to, out float alpha), "sample rejected");
        Assert(from == "a" && to == "b" && Math.Abs(alpha - 0.5f) < 0.0001, "wrong interpolation interval");
        timeline.TrySample(0, out from, out to, out alpha);
        Assert(from == "a" && to == "a" && alpha == 0, "before range should clamp");
        timeline.TrySample(100, out from, out to, out alpha);
        Assert(from == "b" && to == "b" && alpha == 0, "after range should freeze");
        Assert(!timeline.TrySample(double.NaN, out _, out _, out _), "NaN query accepted");
    }

    private static void TimelineCapacity()
    {
        var timeline = new SnapshotTimeline<int>(2);
        timeline.TryPush(1, 1); timeline.TryPush(2, 2); timeline.TryPush(3, 3); timeline.TryPush(4, 4);
        Assert(timeline.Count == 2, "buffer not bounded");
        timeline.TrySample(0, out int from, out int to, out _);
        Assert(from == 3 && to == 3, "old frame retained after wrap");
        timeline.TrySample(3.5, out from, out to, out float alpha);
        Assert(from == 3 && to == 4 && Math.Abs(alpha - 0.5f) < 0.0001, "wrapped interpolation wrong");
        Assert(!timeline.TryPush(4, 99) && !timeline.TryPush(2, 99), "duplicate/out-of-order frame accepted");
        Assert(!timeline.TryPush(double.PositiveInfinity, 99), "non-finite timestamp accepted");
        timeline.Clear();
        Assert(timeline.Count == 0 && timeline.TryPush(0, 7), "reset did not accept new scene clock");
        timeline.TrySample(0, out from, out to, out _);
        Assert(from == 7 && to == 7, "previous scene leaked into new buffer");
    }

    private static void PoseInterpolation()
    {
        Pose from = MakePose(Vector3.Zero, Quaternion.Identity);
        Pose to = MakePose(new Vector3(10, 4, 2), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2));
        Pose halfway = Pose.Interpolate(from, to, 0.5f);
        Assert(Vector3.Distance(halfway.Position, new Vector3(5, 2, 1)) < 0.0001, "wrong position");
        Vector3 facing = Vector3.Transform(Vector3.UnitX, halfway.Rotation);
        Assert(Math.Abs(facing.X - facing.Y) < 0.0001 && facing.X > 0.7, "wrong rotation interpolation");
        to.Rotation = new Quaternion(0, 0, 0, -1);
        halfway = Pose.Interpolate(from, to, 0.5f);
        Assert(halfway.IsValid() && Vector3.Distance(Vector3.Transform(Vector3.UnitX, halfway.Rotation), Vector3.UnitX) < 0.0001,
            "equivalent quaternion signs caused a spin");
    }

    private static void InvalidPoses()
    {
        Pose pose = MakePose(Vector3.Zero, Quaternion.Identity);
        Assert(pose.IsValid(), "identity pose invalid");
        pose.Position = new Vector3(float.NaN, 0, 0);
        Assert(!pose.IsValid(), "NaN position accepted");
        pose = MakePose(Vector3.Zero, new Quaternion(0, 0, 0, 0));
        Assert(!pose.IsValid(), "zero quaternion accepted");
        pose = MakePose(Vector3.Zero, new Quaternion(float.MaxValue, 0, 0, 1));
        Assert(!pose.IsValid(), "overflowing quaternion accepted");
    }

    private static Pose MakePose(Vector3 position, Quaternion rotation) => new Pose
    {
        Position = position, Rotation = rotation, Scale = Vector3.One
    };

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
