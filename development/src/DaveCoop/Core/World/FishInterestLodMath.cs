using System;
using System.Numerics;

namespace DaveCoop.Core.World
{
    public enum FishInterestLodLayer { Inside = 0, Hysteresis = 1, Outside = 2 }

    public readonly struct FishInterestLodRequest
    {
        public Vector3 Position { get; }
        public float SizeInViewPort { get; }
        public float CustomOutRange { get; }
        public float WorldSize { get; }
        public FishInterestLodRequest(Vector3 position, float sizeInViewPort, float customOutRange, float worldSize)
        { Position = position; SizeInViewPort = sizeInViewPort; CustomOutRange = customOutRange; WorldSize = worldSize; }
    }

    // Row-vector System.Numerics matrix, copied from the native column-vector
    // matrix without calling any Unity operator. These are geometry inputs,
    // not an actor, hit, world or capture permission.
    public readonly struct FishInterestLodRegion
    {
        public Matrix4x4 ViewProjection { get; }
        public Vector3 CameraPosition { get; }
        public float OutRangeAdded { get; }
        public float CullZ { get; }
        public float InMarginX { get; }
        public float OutMarginX { get; }
        public float InMarginY { get; }
        public float OutMarginY { get; }
        public FishInterestLodRegion(Matrix4x4 viewProjection, Vector3 cameraPosition, float outRangeAdded,
            float cullZ, float inMarginX, float outMarginX, float inMarginY, float outMarginY)
        {
            ViewProjection = viewProjection; CameraPosition = cameraPosition; OutRangeAdded = outRangeAdded;
            CullZ = cullZ; InMarginX = inMarginX; OutMarginX = outMarginX;
            InMarginY = inMarginY; OutMarginY = outMarginY;
        }
    }

    public readonly struct FishInterestLodEvaluation
    {
        public FishInterestLodLayer Layer { get; }
        public bool ZCulled { get; }
        public float ProjectedX { get; }
        public float ProjectedY { get; }
        internal FishInterestLodEvaluation(FishInterestLodLayer layer, bool zCulled, float x, float y)
        { Layer = layer; ZCulled = zCulled; ProjectedX = x; ProjectedY = y; }
    }

    public static class FishInterestLodMath
    {
        // Literals read from the exact known native Execute body and its
        // file-backed constants. This limited model is checked against each
        // original host result before the native adapter changes that row.
        public const float ProjectionScale = 0.5f;
        public const float DefaultInnerRange = 0.61f;
        public const float DefaultOuterRange = 0.65f;
        public const float WorldSizeOuterPadding = 0.05f;

        public static bool TryEvaluate(FishInterestLodRegion region, FishInterestLodRequest request,
            out FishInterestLodEvaluation evaluation)
        {
            evaluation = default;
            if (!Valid(region) || !Finite(request.Position) || !float.IsFinite(request.SizeInViewPort) ||
                !float.IsFinite(request.CustomOutRange) || !float.IsFinite(request.WorldSize)) return false;
            if (!Project(region.ViewProjection, request.Position, out float x, out float y)) return false;
            float inner = DefaultInnerRange, outer = DefaultOuterRange;
            if (request.SizeInViewPort != 0)
            { inner = request.SizeInViewPort; outer = inner + region.OutRangeAdded; }
            if (request.CustomOutRange != 0) outer = request.CustomOutRange;
            if (request.WorldSize != 0)
            {
                // The original probe uses z=0 and the camera's x/y, including
                // any camera translation. Do not replace this with a radius
                // guessed from sprite size or divide by an assumed viewport.
                var probe = new Vector3(region.CameraPosition.X + request.WorldSize, region.CameraPosition.Y, 0);
                if (!Finite(probe) || !Project(region.ViewProjection, probe, out float extent, out _)) return false;
                inner += extent; outer = inner + WorldSizeOuterPadding;
            }
            float outerX = (outer + region.OutMarginX) + region.InMarginX;
            float outerY = (outer + region.OutMarginY) + region.InMarginY;
            float innerX = inner + region.InMarginX, innerY = inner + region.InMarginY;
            if (!FiniteNonnegative(inner) || !FiniteNonnegative(outer) || !FiniteNonnegative(innerX) ||
                !FiniteNonnegative(innerY) || !FiniteNonnegative(outerX) || !FiniteNonnegative(outerY) ||
                outerX < innerX || outerY < innerY) return false;
            var layer = x > outerX || y > outerY ? FishInterestLodLayer.Outside :
                x <= innerX && y <= innerY ? FishInterestLodLayer.Inside : FishInterestLodLayer.Hysteresis;
            float dz = Math.Abs(region.CameraPosition.Z - request.Position.Z);
            if (!float.IsFinite(dz)) return false;
            evaluation = new FishInterestLodEvaluation(layer, region.CullZ > 0 && dz > region.CullZ, x, y);
            return true;
        }

        public static bool TryTranslate(FishInterestLodRegion region, Vector3 delta,
            out FishInterestLodRegion translated)
        {
            translated = default;
            // This source profile supports two translated, same-plane
            // orthographic regions. A perspective or different-z camera is an
            // explicit unsupported profile, never silently approximated.
            Matrix4x4 matrix = region.ViewProjection;
            if (!Valid(region) || !Finite(delta) || delta.Z != 0 || matrix.M14 != 0 ||
                matrix.M24 != 0 || matrix.M34 != 0 || matrix.M44 == 0) return false;
            matrix.M41 = ((matrix.M41 - delta.X * matrix.M11) - delta.Y * matrix.M21) - delta.Z * matrix.M31;
            matrix.M42 = ((matrix.M42 - delta.X * matrix.M12) - delta.Y * matrix.M22) - delta.Z * matrix.M32;
            matrix.M43 = ((matrix.M43 - delta.X * matrix.M13) - delta.Y * matrix.M23) - delta.Z * matrix.M33;
            matrix.M44 = ((matrix.M44 - delta.X * matrix.M14) - delta.Y * matrix.M24) - delta.Z * matrix.M34;
            Vector3 camera = region.CameraPosition + delta;
            translated = new FishInterestLodRegion(matrix, camera, region.OutRangeAdded, region.CullZ,
                region.InMarginX, region.OutMarginX, region.InMarginY, region.OutMarginY);
            if (Valid(translated)) return true;
            translated = default; return false;
        }

        public static bool TryUnion(FishInterestLodLayer originalHostLayer, FishInterestLodLayer remoteLayer,
            out FishInterestLodLayer layer)
        {
            layer = originalHostLayer;
            if ((int)originalHostLayer < 0 || (int)originalHostLayer > 2 || (int)remoteLayer < 0 || (int)remoteLayer > 2)
                return false;
            layer = (FishInterestLodLayer)Math.Min((int)originalHostLayer, (int)remoteLayer); return true;
        }

        private static bool Project(Matrix4x4 matrix, Vector3 position, out float x, out float y)
        {
            // Scalar operations avoid calling generated Unity/Mathematics
            // operators and preserve the known native multiplication order.
            float px = ((matrix.M11 * position.X + matrix.M21 * position.Y) + matrix.M31 * position.Z) + matrix.M41;
            float py = ((matrix.M12 * position.X + matrix.M22 * position.Y) + matrix.M32 * position.Z) + matrix.M42;
            float pw = ((matrix.M14 * position.X + matrix.M24 * position.Y) + matrix.M34 * position.Z) + matrix.M44;
            x = y = 0;
            if (!float.IsFinite(px) || !float.IsFinite(py) || !float.IsFinite(pw) || pw == 0) return false;
            x = Math.Abs(px / -pw * ProjectionScale); y = Math.Abs(py / -pw * ProjectionScale);
            return float.IsFinite(x) && float.IsFinite(y);
        }

        private static bool Valid(FishInterestLodRegion region) => Finite(region.ViewProjection) &&
            Finite(region.CameraPosition) && float.IsFinite(region.OutRangeAdded) &&
            float.IsFinite(region.CullZ) && float.IsFinite(region.InMarginX) &&
            float.IsFinite(region.OutMarginX) && float.IsFinite(region.InMarginY) && float.IsFinite(region.OutMarginY);
        private static bool FiniteNonnegative(float value) => float.IsFinite(value) && value >= 0;
        private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
        private static bool Finite(Matrix4x4 m) => float.IsFinite(m.M11) && float.IsFinite(m.M12) && float.IsFinite(m.M13) && float.IsFinite(m.M14) &&
            float.IsFinite(m.M21) && float.IsFinite(m.M22) && float.IsFinite(m.M23) && float.IsFinite(m.M24) &&
            float.IsFinite(m.M31) && float.IsFinite(m.M32) && float.IsFinite(m.M33) && float.IsFinite(m.M34) &&
            float.IsFinite(m.M41) && float.IsFinite(m.M42) && float.IsFinite(m.M43) && float.IsFinite(m.M44);
    }
}
