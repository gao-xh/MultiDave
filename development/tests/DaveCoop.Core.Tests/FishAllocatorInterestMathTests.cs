using System;
using System.Numerics;
using DaveCoop.Core.World;

internal static class FishAllocatorInterestMathTests
{
    internal static void ObservationAtAllocatorCenterHasExactZeroDistanceInput()
    {
        var local = new Vector3(900000, -700000, 3);
        var center = new Vector3(25000, -15000, 3);
        Assert(FishAllocatorInterestMath.TryProxyCenter(local, center, center, out Vector3 proxy),
            "an observation exactly at the real allocator center was omitted");
        Assert(proxy == local && Binary32Distance(local, proxy) == 0 && center == new Vector3(25000, -15000, 3),
            "zero-distance input was perturbed or the true center value changed");
    }

    internal static void NearerObservationProducesConservativeTransientDistance()
    {
        var local = new Vector3(100, -50, 0);
        var center = new Vector3(10, -10, 0);
        var remote = new Vector3(12, -7, 0);
        Assert(FishAllocatorInterestMath.TryProxyCenter(local, center, remote, out Vector3 proxy),
            "a bounded clearly nearer observation could not supply the original distance input");
        double actual = Math.Sqrt(13);
        float effective = Binary32Distance(local, proxy);
        Assert(effective > actual && effective - actual < 0.001 && effective < Binary32Distance(local, center) && proxy != center,
            "proxy rounded nearer than the observation, crossed its conservative budget or was not nearer than local");
        Assert(local == new Vector3(100, -50, 0) && center == new Vector3(10, -10, 0) && remote == new Vector3(12, -7, 0),
            "distance input construction changed one of its source points");
    }

    internal static void EqualFartherAndNearTieKeepOriginalCenter()
    {
        var local = new Vector3(10, 0, 0);
        foreach (Vector3 remote in new[] { new Vector3(-10, 0, 0), new Vector3(11, 0, 0), new Vector3(9.99999f, 0, 0), local })
            Assert(!FishAllocatorInterestMath.TryProxyCenter(local, Vector3.Zero, remote, out Vector3 proxy) && proxy == Vector3.Zero,
                "a farther, equal or numerically ambiguous observation replaced the center");
        Assert(!FishAllocatorInterestMath.TryProxyCenter(Vector3.Zero, Vector3.Zero, Vector3.Zero, out _),
            "two zero-distance observations were incorrectly classified as nearer");
    }

    internal static void CancellationAndNonfiniteInputsCannotInventNearDistance()
    {
        Assert(!FishAllocatorInterestMath.TryProxyCenter(new Vector3(1000000, 0, 0), Vector3.Zero,
            new Vector3(0.001f, 0, 0), out Vector3 cancelled) && cancelled == Vector3.Zero,
            "binary32 cancellation invented a zero distance for a nonzero observed displacement");
        foreach (Vector3 invalid in new[] { new Vector3(float.NaN, 0, 0), new Vector3(0, float.PositiveInfinity, 0),
            new Vector3(0, 0, float.NegativeInfinity), new Vector3(1000001, 0, 0) })
        {
            Assert(!FishAllocatorInterestMath.TryProxyCenter(invalid, Vector3.Zero, Vector3.One, out _), "invalid local input passed");
            Assert(!FishAllocatorInterestMath.TryProxyCenter(new Vector3(100, 0, 0), invalid, Vector3.One, out _), "invalid center input passed");
            Assert(!FishAllocatorInterestMath.TryProxyCenter(new Vector3(100, 0, 0), Vector3.Zero, invalid, out _), "invalid observation input passed");
        }
    }

    private static float Binary32Distance(Vector3 a, Vector3 b)
    {
        float x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z;
        float xx = x * x, yy = y * y, zz = z * z;
        float xy = xx + yy, sum = xy + zz;
        return (float)Math.Sqrt(sum);
    }
    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
