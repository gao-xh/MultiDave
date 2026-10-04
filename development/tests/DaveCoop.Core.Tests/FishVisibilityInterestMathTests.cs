using System;
using System.Numerics;
using DaveCoop.Core.World;

internal static class FishVisibilityInterestMathTests
{
    internal static void RemoteBoundsTranslateIntoActualHostCameraWithoutChangingInputs()
    {
        var center = new Vector3(110, 42, 5); var extents = new Vector3(2, 1, 0);
        var local = new Vector3(10, 20, 0); var remote = new Vector3(110, 40, 0);
        Assert(FishVisibilityInterestMath.TryCreateShiftedCorners(center, extents, local, remote, out Vector3[] corners), "same-plane shifted bounds unavailable");
        Assert(corners.Length == 8 && corners[0].X <= 8 && corners[0].Y <= 21 && corners[7].X >= 12 && corners[7].Y >= 23 &&
            corners[0].Z == 5 && corners[7].Z == 5 && Math.Abs(corners[0].X - 8) < 0.0001f && Math.Abs(corners[7].Y - 23) < 0.0001f,
            "shift failed to enclose the exact bounds with a small outward allowance");
        Assert(center == new Vector3(110, 42, 5) && extents == new Vector3(2, 1, 0) && remote == new Vector3(110, 40, 0), "geometry preparation changed a source point");
        Assert(FishVisibilityInterestMath.FullyInsideViewport(ViewportBox(0.4f, 0.6f, 5), 0.3f, 100), "strict interior box rejected");
    }

    internal static void PartialViewportAndDepthBoundariesNeverGrantVisibility()
    {
        Vector3[] points = ViewportBox(0.4f, 0.6f, 5);
        points[7] = new Vector3(1.0001f, 0.5f, 5);
        Assert(!FishVisibilityInterestMath.FullyInsideViewport(points, 0.3f, 100), "one outside corner granted visibility");
        foreach (Vector3 invalid in new[] { new Vector3(0.001f, 0.5f, 5), new Vector3(0.5f, 0.999f, 5),
            new Vector3(0.5f, 0.5f, 0.3f), new Vector3(0.5f, 0.5f, 100), new Vector3(0.5f, 0.5f, -1) })
        {
            points = ViewportBox(0.4f, 0.6f, 5); points[3] = invalid;
            Assert(!FishVisibilityInterestMath.FullyInsideViewport(points, 0.3f, 100), "viewport or clip-plane boundary granted visibility");
        }
    }

    internal static void LargeCoordinatesRoundBoundsOutwardInsteadOfShrinking()
    {
        var center = new Vector3(900000, 700000, 5); var extents = new Vector3(0.001f, 0.003f, 0);
        var local = new Vector3(0.01f, -0.01f, 0); var remote = new Vector3(0.003f, -0.003f, 0);
        Assert(FishVisibilityInterestMath.TryCreateShiftedCorners(center, extents, local, remote, out Vector3[] points), "finite large-coordinate bounds rejected");
        double x = (double)center.X + ((double)local.X - remote.X), y = (double)center.Y + ((double)local.Y - remote.Y);
        Assert(points[0].X <= x - extents.X && points[7].X >= x + extents.X &&
            points[0].Y <= y - extents.Y && points[7].Y >= y + extents.Y && points[0].X < points[7].X,
            "binary32 cancellation shrank an actual box to a point");
        Assert(FishVisibilityInterestMath.TryCreateShiftedCorners(new Vector3(900000, 700000, 5),
            new Vector3(1e-20f, 1, 0), Vector3.Zero, Vector3.Zero, out Vector3[] tiny) && tiny[0].X < 900000 && tiny[7].X > 900000,
            "binary64 absorption of a positive extent invented a point box");
        Assert(FishVisibilityInterestMath.TryCreateShiftedCorners(new Vector3(900000, 700000, 5),
            new Vector3(0.0625f, 1, 0), new Vector3(1e-20f, 0, 0), Vector3.Zero, out Vector3[] moved) && moved[7].X > 900000.0625f,
            "a tiny translation at an exact binary32 edge was rounded into the interior");
    }

    internal static void DifferentPlanesMalformedSamplesAndNonfiniteGeometryKeepOriginal()
    {
        Assert(!FishVisibilityInterestMath.TryCreateShiftedCorners(Vector3.One, Vector3.One, Vector3.Zero, new Vector3(0, 0, 1), out _), "different observation plane accepted");
        foreach (Vector3 bad in new[] { new Vector3(float.NaN, 0, 0), new Vector3(float.PositiveInfinity, 0, 0), new Vector3(1000001, 0, 0) })
            Assert(!FishVisibilityInterestMath.TryCreateShiftedCorners(bad, Vector3.One, Vector3.Zero, Vector3.Zero, out _), "invalid native geometry accepted");
        Assert(!FishVisibilityInterestMath.TryCreateShiftedCorners(Vector3.One, new Vector3(-1, 1, 0), Vector3.Zero, Vector3.Zero, out _), "negative bounds accepted");
        Assert(!FishVisibilityInterestMath.FullyInsideViewport(new Vector3[7], 0.3f, 100) &&
            !FishVisibilityInterestMath.FullyInsideViewport(ViewportBox(0.4f, 0.6f, 5), 100, 0.3f), "partial or reversed-clip samples accepted");
        Vector3[] points = ViewportBox(0.4f, 0.6f, 5); points[0] = new Vector3(float.NaN, 0.5f, 5);
        Assert(!FishVisibilityInterestMath.FullyInsideViewport(points, 0.3f, 100), "nonfinite projected point accepted");
    }

    private static Vector3[] ViewportBox(float min, float max, float z)
    {
        var points = new Vector3[8];
        for (int i = 0; i < 8; i++) points[i] = new Vector3((i & 1) == 0 ? min : max, (i & 2) == 0 ? min : max, z);
        return points;
    }
    private static void Assert(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
}
