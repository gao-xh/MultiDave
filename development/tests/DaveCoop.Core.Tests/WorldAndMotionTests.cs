using System;
using System.Linq;
using System.Numerics;
using DaveCoop.Core;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.Session;
using DaveCoop.Core.World;

internal static class WorldAndMotionTests
{
    internal static void LayoutOrderAndMultiplicity()
    {
        string a = new CanonicalHash("fixture").Add("terrain-a").Add(1.25f).Finish();
        string b = new CanonicalHash("fixture").Add("terrain-b").Add(2.5f).Finish();
        string first = LayoutFingerprint.Create("scene", new[] { a, b });
        Assert(first == LayoutFingerprint.Create("scene", new[] { b, a }), "object enumeration changed layout identity");
        Assert(first != LayoutFingerprint.Create("scene", new[] { a, a, b }), "duplicate geometry disappeared from layout identity");
        Assert(first != LayoutFingerprint.Create("other-scene", new[] { a, b }), "scene identity was omitted");
    }

    internal static void LayoutGeometryAndFieldBoundaries()
    {
        string first = new CanonicalHash("fixture").Add("a:b").Add("c").Add(0f).Finish();
        string second = new CanonicalHash("fixture").Add("a").Add("b:c").Add(0f).Finish();
        Assert(first != second, "layout string boundaries collided");
        Assert(first == new CanonicalHash("fixture").Add("a:b").Add("c").Add(-0f).Finish(), "signed zero changed layout");
        Assert(first != new CanonicalHash("fixture").Add("a:b").Add("c").Add(1f).Finish(), "geometry change retained fingerprint");
        string selected = new CanonicalHash("selected-node-v1").Add("prefab-A").Finish();
        string other = new CanonicalHash("selected-node-v1").Add("prefab-B").Finish();
        Assert(LayoutFingerprint.Create("scene", new[] { selected }) != LayoutFingerprint.Create("scene", new[] { other }),
            "different selected map nodes were accepted as the same layout");
    }

    internal static void LayoutRejectsInvalidInput()
    {
        Throws<ArgumentException>(() => new CanonicalHash("fixture").Add(float.NaN));
        Throws<ArgumentException>(() => new CanonicalHash("fixture").Add("line\nbreak"));
        Throws<ArgumentException>(() => LayoutFingerprint.Create("scene", Array.Empty<string>()));
        Throws<ArgumentException>(() => LayoutFingerprint.Create("scene", new[] { "not-a-shape-hash" }));
        string entry = new CanonicalHash("fixture").Add("terrain").Finish();
        Throws<ArgumentException>(() => LayoutFingerprint.Create("scene", Enumerable.Repeat(entry, LayoutFingerprint.MaxEntries + 1)));
    }

    internal static void MotionClockAndInterpolation()
    {
        var buffer = new RemoteMotionBuffer();
        Assert(buffer.TryPush(Receipt(100, 1, 0.9, 1, 0)), "first motion frame rejected");
        Assert(buffer.TryPush(Receipt(100.2, 1.2, 1.15, 1, 10)), "second motion frame rejected");
        Assert(buffer.TrySample(1.2, 0.2, out PlayerFrame from, out PlayerFrame to, out float alpha), "motion sample missing");
        Pose middle = Pose.Interpolate(from.Root, to.Root, alpha);
        Assert(Math.Abs(middle.Position.X - 5) < 0.001, "changing clock estimate reordered interpolation timeline");
        Assert(!buffer.TryPush(Receipt(100.1, 1.3, 1.25, 1, 99)), "old remote sample overwrote history");
        Assert(!buffer.TrySample(2.3, 0.1, out _, out _, out _), "stale remote actor remained visible");
    }

    internal static void MotionEpochCapacityAndReset()
    {
        var buffer = new RemoteMotionBuffer(2);
        buffer.TryPush(Receipt(1, 1, 1, 1, 1)); buffer.TryPush(Receipt(2, 2, 2, 1, 2)); buffer.TryPush(Receipt(3, 3, 3, 1, 3));
        Assert(buffer.Count == 2 && buffer.TrySample(3, 100, out PlayerFrame from, out _, out _) && from.Root.Position.X == 2,
            "motion history exceeded capacity or kept old frame");
        buffer.TryPush(Receipt(4, 4, 4, 2, 20));
        Assert(buffer.Count == 1 && buffer.TrySample(4, 0.1, out from, out _, out _) && from.Root.Position.X == 20, "new epoch blended old scene");
        Assert(!buffer.TryPush(Receipt(5, 5, 5, 1, 99)), "previous epoch entered new history");
        buffer.Clear();
        Assert(buffer.Count == 0 && !buffer.TrySample(5, 0.1, out _, out _, out _), "cleared scene still sampled");
    }

    internal static void MotionRejectsInvalidData()
    {
        var buffer = new RemoteMotionBuffer();
        Throws<ArgumentException>(() => buffer.TryPush(Receipt(1, double.NaN, 1, 1, 1)));
        buffer.TryPush(Receipt(1, 1, 1, 1, 1));
        ReceivedFrame mismatch = Receipt(2, 2, 2, 1, 2); mismatch.Frame.SceneKey = "other";
        Throws<ProtocolException>(() => buffer.TryPush(mismatch));
        Throws<ArgumentException>(() => buffer.TrySample(2, double.NaN, out _, out _, out _));
    }

    private static ReceivedFrame Receipt(double remoteTime, double receivedAt, double localSample, long epoch, float x) => new ReceivedFrame
    {
        ReceivedAt = receivedAt, LocalSampleTime = localSample,
        Frame = new PlayerFrame
        {
            PlayerId = 2, SceneEpoch = epoch, SceneKey = "fixture-scene", SampleTime = remoteTime,
            Root = new Pose { Position = new Vector3(x, 0, 0), Rotation = Quaternion.Identity, Scale = Vector3.One }
        }
    };

    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
}
