using System;
using System.Numerics;

namespace DaveCoop.Core.World
{
    // Conservative geometry only. Native source, actual camera, original
    // getter execution and fish/renderer identity remain the adapter's job.
    // This is not Unity visibility, shadow or multiple-camera equivalence.
    public static class FishVisibilityInterestMath
    {
        public const double CoordinateBound = 1000000;
        public const float ViewportInset = 0.001f;
        public const int CornerCount = 8;

        public static bool TryCreateShiftedCorners(Vector3 center, Vector3 extents, Vector3 local,
            Vector3 observed, out Vector3[] corners)
        {
            corners = null;
            if (!Bounded(center) || !Bounded(extents) || !Bounded(local) || !Bounded(observed) ||
                extents.X <= 0 || extents.Y <= 0 || extents.Z < 0 || local.Z != observed.Z) return false;
            // Outward rounding encloses the exact translated binary32 inputs;
            // large-coordinate cancellation cannot shrink the candidate box.
            if (!Axis(center.X, extents.X, local.X, observed.X, out float minX, out float maxX) ||
                !Axis(center.Y, extents.Y, local.Y, observed.Y, out float minY, out float maxY) ||
                !Axis(center.Z, extents.Z, local.Z, observed.Z, out float minZ, out float maxZ)) return false;
            var owned = new Vector3[CornerCount];
            for (int i = 0; i < CornerCount; i++)
                owned[i] = new Vector3((i & 1) == 0 ? minX : maxX, (i & 2) == 0 ? minY : maxY, (i & 4) == 0 ? minZ : maxZ);
            corners = owned; return true;
        }

        public static bool FullyInsideViewport(Vector3[] viewportCorners, float near, float far)
        {
            if (viewportCorners == null || viewportCorners.Length != CornerCount ||
                !float.IsFinite(near) || !float.IsFinite(far) || near <= 0 || far <= near || far > CoordinateBound) return false;
            double depthInset = Math.Max(0.0001, ((double)far - near) * 0.00001);
            double first = near + depthInset, last = far - depthInset;
            if (first >= last) return false;
            foreach (Vector3 point in viewportCorners)
                if (!Bounded(point) || point.X <= ViewportInset || point.X >= 1f - ViewportInset ||
                    point.Y <= ViewportInset || point.Y >= 1f - ViewportInset || point.Z <= first || point.Z >= last) return false;
            return true;
        }

        public static bool Bounded(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) &&
            Math.Abs((double)value.X) <= CoordinateBound && Math.Abs((double)value.Y) <= CoordinateBound && Math.Abs((double)value.Z) <= CoordinateBound;

        private static bool Axis(float center, float extent, float local, float observed, out float min, out float max)
        {
            if (extent == 0 && local == observed) { min = center; max = center; return true; }
            // Directed intervals at every operation also enclose terms that
            // binary64 would absorb (tiny motion or extent near a large point).
            double difference = (double)local - observed;
            double first = Math.BitDecrement((double)center + Math.BitDecrement(difference));
            double last = Math.BitIncrement((double)center + Math.BitIncrement(difference));
            double lower = Math.BitDecrement(first - extent), upper = Math.BitIncrement(last + extent);
            min = (float)lower; max = (float)upper;
            if ((double)min > lower) min = MathF.BitDecrement(min);
            if ((double)max < upper) max = MathF.BitIncrement(max);
            return float.IsFinite(min) && float.IsFinite(max) && min <= max &&
                Math.Abs((double)min) <= CoordinateBound && Math.Abs((double)max) <= CoordinateBound;
        }
    }
}
