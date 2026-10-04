using System;
using System.Numerics;

namespace DaveCoop.Core.World
{
    // A distance-input candidate only. The native adapter must independently
    // require original ordinary Move execution, force=false, minimum=0, a
    // positive fixed radius, a fresh scene/player/source and the actual pair.
    // This neither authorizes spawning nor models the native lazy conditions.
    public static class FishAllocatorInterestMath
    {
        public const double CoordinateBound = 1000000;

        public static bool TryProxyCenter(Vector3 local, Vector3 realCenter, Vector3 observed, out Vector3 proxy)
        {
            proxy = realCenter;
            if (!Bounded(local) || !Bounded(realCenter) || !Bounded(observed)) return false;
            double dx = (double)observed.X - realCenter.X, dy = (double)observed.Y - realCenter.Y, dz = (double)observed.Z - realCenter.Z;
            double remoteDistance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double hx = (double)local.X - realCenter.X, hy = (double)local.Y - realCenter.Y, hz = (double)local.Z - realCenter.Z;
            double localDistance = Math.Sqrt(hx * hx + hy * hy + hz * hz);
            if (!double.IsFinite(remoteDistance) || !double.IsFinite(localDistance)) return false;
            double margin = Math.Max(0.0001, remoteDistance * 0.00001);
            if (localDistance - remoteDistance <= margin * 8) return false;
            if (remoteDistance == 0)
            {
                // Identical binary32 points have an exact zero displacement.
                // Proxy==local also gives an exact zero local subtraction,
                // avoiding cancellation and an invented nonzero exclusion.
                proxy = local; return true;
            }
            // Slightly farther than the observation rather than rounding into
            // a false inside result at an unknown original upper boundary.
            double scale = (remoteDistance + margin) / remoteDistance;
            var candidate = new Vector3((float)(local.X - dx * scale), (float)(local.Y - dy * scale), (float)(local.Z - dz * scale));
            if (!Bounded(candidate)) return false;
            float x = local.X - candidate.X, y = local.Y - candidate.Y, z = local.Z - candidate.Z;
            float xx = x * x, yy = y * y, zz = z * z;
            float xy = xx + yy, sum = xy + zz;
            float effective = (float)Math.Sqrt(sum);
            if (!float.IsFinite(effective) || effective < remoteDistance + margin * 0.5 ||
                effective > remoteDistance + margin * 2 || effective >= localDistance - margin * 4) return false;
            proxy = candidate; return true;
        }

        public static bool Bounded(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) &&
            Math.Abs((double)value.X) <= CoordinateBound && Math.Abs((double)value.Y) <= CoordinateBound && Math.Abs((double)value.Z) <= CoordinateBound;
    }
}
