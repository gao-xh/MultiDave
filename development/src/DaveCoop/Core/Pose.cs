using System;
using System.Numerics;

namespace DaveCoop.Core
{
    public struct Pose
    {
        public Vector3 Position { get; set; }
        public Quaternion Rotation { get; set; }
        public Vector3 Scale { get; set; }

        public bool IsValid()
        {
            float rotationLength = Rotation.LengthSquared();
            return Finite(Position.X, Position.Y, Position.Z, Rotation.X, Rotation.Y,
                Rotation.Z, Rotation.W, Scale.X, Scale.Y, Scale.Z) &&
                float.IsFinite(rotationLength) && rotationLength > 0.000001f;
        }

        public static Pose Interpolate(Pose from, Pose to, float amount)
        {
            amount = Math.Clamp(amount, 0f, 1f);
            return new Pose
            {
                Position = Vector3.Lerp(from.Position, to.Position, amount),
                Rotation = Quaternion.Slerp(Quaternion.Normalize(from.Rotation), Quaternion.Normalize(to.Rotation), amount),
                Scale = Vector3.Lerp(from.Scale, to.Scale, amount)
            };
        }

        private static bool Finite(params float[] values)
        {
            foreach (float value in values) if (!float.IsFinite(value)) return false;
            return true;
        }
    }
}
