using System;
using System.Numerics;
using DaveCoop.Core.World;

internal static class FishInterestLodMathTests
{
    internal static void SeparateRegionsKeepBothCentersWithoutActivatingTheCorridor()
    {
        var host = new FishInterestLodRegion(Matrix4x4.CreateScale(0.01f, 0.01f, 1), Vector3.Zero, 0, 2, 0, 0, 0, 0);
        Assert(FishInterestLodMath.TryTranslate(host, new Vector3(2000, 0, 0), out FishInterestLodRegion remote),
            "same-plane remote region could not be translated");
        Assert(remote.CameraPosition == new Vector3(2000, 0, 0) && host.CameraPosition == Vector3.Zero,
            "translation mutated the host region or lost the independent center");
        CheckUnion(host, remote, Vector3.Zero, FishInterestLodLayer.Inside);
        CheckUnion(host, remote, new Vector3(2000, 0, 0), FishInterestLodLayer.Inside);
        CheckUnion(host, remote, new Vector3(1000, 0, 0), FishInterestLodLayer.Outside);
        CheckUnion(host, remote, new Vector3(2000, 500, 0), FishInterestLodLayer.Outside);

        var unit = Region(Matrix4x4.Identity, Vector3.Zero);
        Assert(Evaluate(unit, new Vector3(1.22f, 0, 0)).Layer == FishInterestLodLayer.Inside,
            "the inclusive inner boundary became outside");
        Assert(Evaluate(unit, new Vector3(1.3f, 0, 0)).Layer == FishInterestLodLayer.Hysteresis &&
            Evaluate(unit, new Vector3(1.32f, 0, 0)).Layer == FishInterestLodLayer.Outside,
            "outer boundary/hysteresis order differed from the finite model");
        Assert(FishInterestLodMath.TryUnion(FishInterestLodLayer.Hysteresis, FishInterestLodLayer.Outside, out FishInterestLodLayer layer) &&
            layer == FishInterestLodLayer.Hysteresis, "union weakened an already retained host region");
    }

    internal static void SizeCustomAndWorldPaddingUseTheFixedProjectionInputs()
    {
        var region = new FishInterestLodRegion(Matrix4x4.CreateTranslation(-10, -4, 0), new Vector3(10, 4, 0),
            0.1f, 0, 0, 0, 0, 0);
        Assert(FishInterestLodMath.TryEvaluate(region, new FishInterestLodRequest(new Vector3(10.5f, 4, 0), 0.2f, 0, 0), out var size) &&
            size.Layer == FishInterestLodLayer.Hysteresis && size.ProjectedX == 0.25f && size.ProjectedY == 0,
            "size override failed to retain the literal projected hysteresis sample");
        Assert(FishInterestLodMath.TryEvaluate(region, new FishInterestLodRequest(new Vector3(10.7f, 4, 0), 0.2f, 0.4f, 0), out var custom) &&
            custom.Layer == FishInterestLodLayer.Hysteresis, "custom outer range did not override the size-based outer range");
        Assert(FishInterestLodMath.TryEvaluate(region, new FishInterestLodRequest(new Vector3(13.26f, 4, 0), 0, 0.1f, 2), out var world) &&
            world.Layer == FishInterestLodLayer.Hysteresis,
            "world-size camera probe and final padding did not override the earlier custom outer range");
        Assert(FishInterestLodMath.TryEvaluate(region, new FishInterestLodRequest(new Vector3(13.4f, 4, 0), 0, 0.1f, 2), out world) &&
            world.Layer == FishInterestLodLayer.Outside, "world-size outer padding activated an excessive interval");
        var margins = new FishInterestLodRegion(Matrix4x4.Identity, Vector3.Zero, 0, 0, 0.1f, 0.2f, 0, 0);
        Assert(Evaluate(margins, new Vector3(1.8f, 0, 0)).Layer == FishInterestLodLayer.Hysteresis &&
            Evaluate(margins, new Vector3(0, 1.8f, 0)).Layer == FishInterestLodLayer.Outside,
            "independent x/y inner and outer margins were conflated");
    }

    internal static void UnsupportedProjectionDepthAndNonfiniteInputsDoNotInventAUnion()
    {
        var unit = Region(Matrix4x4.Identity, Vector3.Zero);
        Matrix4x4 perspective = Matrix4x4.Identity; perspective.M14 = 0.1f;
        Assert(!FishInterestLodMath.TryTranslate(Region(perspective, Vector3.Zero), Vector3.UnitX, out _),
            "a perspective projection was approximated as a translated orthographic region");
        Assert(!FishInterestLodMath.TryTranslate(unit, Vector3.UnitZ, out _) &&
            !FishInterestLodMath.TryTranslate(unit, new Vector3(float.NaN, 0, 0), out _),
            "different plane or invalid center generated a remote region");
        Matrix4x4 zeroW = Matrix4x4.Identity; zeroW.M44 = 0;
        Assert(!FishInterestLodMath.TryTranslate(Region(zeroW, Vector3.Zero), Vector3.UnitX, out _) &&
            !FishInterestLodMath.TryEvaluate(Region(zeroW, Vector3.Zero), Request(Vector3.Zero), out _),
            "zero projection divisor generated a classification");
        Assert(!FishInterestLodMath.TryEvaluate(unit, Request(new Vector3(float.PositiveInfinity, 0, 0)), out _) &&
            !FishInterestLodMath.TryEvaluate(unit, new FishInterestLodRequest(Vector3.Zero, float.NaN, 0, 0), out _) &&
            !FishInterestLodMath.TryEvaluate(unit, new FishInterestLodRequest(Vector3.Zero, 0, -1, 0), out _),
            "nonfinite input or reversed effective boundaries were accepted");
        Matrix4x4 overflow = Matrix4x4.Identity; overflow.M11 = float.MaxValue;
        Assert(!FishInterestLodMath.TryEvaluate(Region(overflow, Vector3.Zero), Request(new Vector3(float.MaxValue, 0, 0)), out _),
            "overflow during projection was classified as a usable region");
        var depth = new FishInterestLodRegion(Matrix4x4.Identity, Vector3.Zero, 0, 2, 0, 0, 0, 0);
        FishInterestLodEvaluation culled = Evaluate(depth, new Vector3(0, 0, 3));
        Assert(culled.Layer == FishInterestLodLayer.Inside && culled.ZCulled,
            "z culling was lost or silently substituted for the independent x/y layer");
        Assert(!FishInterestLodMath.TryUnion(FishInterestLodLayer.Hysteresis, (FishInterestLodLayer)99, out FishInterestLodLayer original) &&
            original == FishInterestLodLayer.Hysteresis, "an undefined remote classification changed the host result");
    }

    private static FishInterestLodRegion Region(Matrix4x4 matrix, Vector3 camera) =>
        new FishInterestLodRegion(matrix, camera, 0, 0, 0, 0, 0, 0);
    private static FishInterestLodRequest Request(Vector3 position) => new FishInterestLodRequest(position, 0, 0, 0);
    private static FishInterestLodEvaluation Evaluate(FishInterestLodRegion region, Vector3 position)
    {
        Assert(FishInterestLodMath.TryEvaluate(region, Request(position), out FishInterestLodEvaluation result), "finite literal sample was unavailable");
        return result;
    }
    private static void CheckUnion(FishInterestLodRegion host, FishInterestLodRegion remote, Vector3 position, FishInterestLodLayer expected)
    {
        var a = Evaluate(host, position); var b = Evaluate(remote, position);
        Assert(FishInterestLodMath.TryUnion(a.Layer, b.Layer, out FishInterestLodLayer actual) && actual == expected,
            "two bounded regions changed the literal center/corridor classification");
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
