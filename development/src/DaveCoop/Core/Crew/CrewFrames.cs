using System;
using System.Numerics;
using DaveCoop.Core.Protocol;

namespace DaveCoop.Core.Crew
{
    [Flags]
    public enum CrewButtons { None = 0, Boost = 1, Fire = 2, Recall = 4, Interact = 8 }

    // The employee sends controls, never an authoritative position or damage.
    public sealed class CrewInputFrame
    {
        public int PlayerId { get; set; }
        public long SceneEpoch { get; set; }
        public string SceneKey { get; set; }
        public long InputSequence { get; set; }
        public long ActorRevision { get; set; }
        public float MoveX { get; set; }
        public float MoveY { get; set; }
        public float AimX { get; set; }
        public float AimY { get; set; }
        public CrewButtons Buttons { get; set; }
    }

    // Position/velocity are read back from the host's body by the production
    // controller. This transport value is not a collision or capture receipt.
    public sealed class CrewActorState
    {
        public int PlayerId { get; set; }
        public long SceneEpoch { get; set; }
        public string SceneKey { get; set; }
        public long ActorRevision { get; set; }
        public long StateRevision { get; set; }
        public long LastInputSequence { get; set; }
        public Vector3 Position { get; set; }
        public Vector2 Velocity { get; set; }
        public float HP { get; set; }
        public float MaxHP { get; set; }
        public float Oxygen { get; set; }
        public float MaxOxygen { get; set; }
        public bool Alive { get; set; }
        public bool Active { get; set; }
        public long LoadoutRevision { get; set; }
        public float CapacityKg { get; set; }
        public double? BagWeightKg { get; set; }
        public bool HasConfirmedCargoWeight { get; set; }
        public string BagExpeditionId { get; set; }
        public string BagMemberId { get; set; }
        public long BagRevision { get; set; }
        // Read back from the host's own projectile. Inactive retains the shot
        // fence; this display state does not report damage, capture or rewards.
        public long HarpoonShotId { get; set; }
        public bool HarpoonActive { get; set; }
        public Vector3 HarpoonPosition { get; set; }
        public Vector2 HarpoonDirection { get; set; }
    }

    public sealed class ReceivedCrewInput
    {
        public string RoomId { get; set; }
        public int BoundPlayerId { get; set; }
        public long PacketSequence { get; set; }
        public double ReceivedAt { get; set; }
        public CrewInputFrame Frame { get; set; }
    }

    public sealed class ReceivedCrewActorState
    {
        public string RoomId { get; set; }
        public int BoundPlayerId { get; set; }
        public long PacketSequence { get; set; }
        public double ReceivedAt { get; set; }
        public CrewActorState Frame { get; set; }
    }

    public static class CrewFrames
    {
        public const int MaxSceneKeyLength = 160;
        public const float MaxPositionMagnitude = 100000;
        public const float MaxVelocityMagnitude = 10000;
        public const CrewButtons AllowedButtons = CrewButtons.Boost | CrewButtons.Fire | CrewButtons.Recall | CrewButtons.Interact;

        public static void Validate(CrewInputFrame frame)
        {
            if (frame == null) throw new ProtocolException("Missing crew input.");
            Identity(frame.PlayerId, frame.SceneEpoch, frame.SceneKey, frame.ActorRevision);
            if (frame.InputSequence < 1 || !Axis(frame.MoveX) || !Axis(frame.MoveY) ||
                !Axis(frame.AimX) || !Axis(frame.AimY) || (frame.Buttons & ~AllowedButtons) != 0)
                throw new ProtocolException("Invalid crew input sequence, axes or buttons.");
        }

        public static void Validate(CrewActorState frame)
        {
            if (frame == null) throw new ProtocolException("Missing crew actor state.");
            Identity(frame.PlayerId, frame.SceneEpoch, frame.SceneKey, frame.ActorRevision);
            if (frame.StateRevision < 1 || frame.LastInputSequence < 0 || frame.LoadoutRevision < 1 ||
                !PositionValid(frame.Position) || !VelocityValid(frame.Velocity) ||
                !Positive(frame.MaxHP) || !Positive(frame.MaxOxygen) || !Positive(frame.CapacityKg) ||
                !InRange(frame.HP, frame.MaxHP) || !InRange(frame.Oxygen, frame.MaxOxygen) ||
                frame.Alive != (frame.HP > 0) || (!frame.Alive && frame.Active))
                throw new ProtocolException("Invalid crew actor state or survival values.");
            // Only a host-owned employee ledger reading supplies this identity
            // and confirmed weight. A wire value does not authorize native loot.
            if (frame.HasConfirmedCargoWeight)
            {
                if (!Guid.TryParse(frame.BagExpeditionId, out Guid expedition) || expedition == Guid.Empty ||
                    !Guid.TryParse(frame.BagMemberId, out Guid member) || member == Guid.Empty ||
                    frame.BagExpeditionId != expedition.ToString("N") || frame.BagMemberId != member.ToString("N") ||
                    frame.BagRevision < 1 || !frame.BagWeightKg.HasValue || !double.IsFinite(frame.BagWeightKg.Value) ||
                    frame.BagWeightKg.Value < 0 || frame.BagWeightKg.Value > Cargo.CargoValues.MaxWeight)
                    throw new ProtocolException("Invalid confirmed employee bag reading.");
            }
            else if (frame.BagWeightKg.HasValue || frame.BagExpeditionId != null || frame.BagMemberId != null || frame.BagRevision != 0)
                throw new ProtocolException("Unknown employee bag cannot carry a weight or identity.");
            if (frame.HarpoonShotId < 0 || !PositionValid(frame.HarpoonPosition) || !VelocityValid(frame.HarpoonDirection) ||
                (frame.HarpoonShotId == 0 && (frame.HarpoonActive || frame.HarpoonPosition != Vector3.Zero || frame.HarpoonDirection != Vector2.Zero)) ||
                (frame.HarpoonActive && (!frame.Alive || !frame.Active)))
                throw new ProtocolException("Invalid host harpoon display state.");
            if (frame.HarpoonShotId > 0)
            {
                double length = (double)frame.HarpoonDirection.X * frame.HarpoonDirection.X +
                    (double)frame.HarpoonDirection.Y * frame.HarpoonDirection.Y;
                if (!double.IsFinite(length) || Math.Abs(length - 1) > 0.0001)
                    throw new ProtocolException("Invalid host harpoon direction.");
            }
        }

        public static void Validate(ReceivedCrewInput receipt)
        {
            if (receipt == null) throw new ProtocolException("Missing crew input receipt.");
            Envelope(receipt.RoomId, receipt.BoundPlayerId, 2, receipt.PacketSequence, receipt.ReceivedAt);
            Validate(receipt.Frame);
        }

        public static void Validate(ReceivedCrewActorState receipt)
        {
            if (receipt == null) throw new ProtocolException("Missing crew actor receipt.");
            // The host (1) reports the employee body's state (2).
            Envelope(receipt.RoomId, receipt.BoundPlayerId, 1, receipt.PacketSequence, receipt.ReceivedAt);
            Validate(receipt.Frame);
        }

        public static CrewInputFrame Copy(CrewInputFrame frame) => frame == null ? null : new CrewInputFrame
        {
            PlayerId = frame.PlayerId, SceneEpoch = frame.SceneEpoch, SceneKey = frame.SceneKey,
            InputSequence = frame.InputSequence, ActorRevision = frame.ActorRevision,
            MoveX = frame.MoveX, MoveY = frame.MoveY, AimX = frame.AimX, AimY = frame.AimY, Buttons = frame.Buttons
        };

        public static CrewActorState Copy(CrewActorState frame) => frame == null ? null : new CrewActorState
        {
            PlayerId = frame.PlayerId, SceneEpoch = frame.SceneEpoch, SceneKey = frame.SceneKey,
            ActorRevision = frame.ActorRevision, StateRevision = frame.StateRevision, LastInputSequence = frame.LastInputSequence,
            Position = frame.Position, Velocity = frame.Velocity, HP = frame.HP, MaxHP = frame.MaxHP,
            Oxygen = frame.Oxygen, MaxOxygen = frame.MaxOxygen, Alive = frame.Alive, Active = frame.Active,
            LoadoutRevision = frame.LoadoutRevision, CapacityKg = frame.CapacityKg,
            BagWeightKg = frame.BagWeightKg, HasConfirmedCargoWeight = frame.HasConfirmedCargoWeight,
            BagExpeditionId = frame.BagExpeditionId, BagMemberId = frame.BagMemberId, BagRevision = frame.BagRevision,
            HarpoonShotId = frame.HarpoonShotId, HarpoonActive = frame.HarpoonActive,
            HarpoonPosition = frame.HarpoonPosition, HarpoonDirection = frame.HarpoonDirection
        };

        public static ReceivedCrewInput Copy(ReceivedCrewInput receipt) => receipt == null ? null : new ReceivedCrewInput
        {
            RoomId = receipt.RoomId, BoundPlayerId = receipt.BoundPlayerId, PacketSequence = receipt.PacketSequence,
            ReceivedAt = receipt.ReceivedAt, Frame = Copy(receipt.Frame)
        };

        public static ReceivedCrewActorState Copy(ReceivedCrewActorState receipt) => receipt == null ? null : new ReceivedCrewActorState
        {
            RoomId = receipt.RoomId, BoundPlayerId = receipt.BoundPlayerId, PacketSequence = receipt.PacketSequence,
            ReceivedAt = receipt.ReceivedAt, Frame = Copy(receipt.Frame)
        };

        internal static bool PositionValid(Vector3 value) => Scalar(value.X, MaxPositionMagnitude) &&
            Scalar(value.Y, MaxPositionMagnitude) && Scalar(value.Z, MaxPositionMagnitude);
        internal static bool VelocityValid(Vector2 value) => Scalar(value.X, MaxVelocityMagnitude) && Scalar(value.Y, MaxVelocityMagnitude);
        private static bool Axis(float value) => Scalar(value, 1);
        private static bool Scalar(float value, float max) => float.IsFinite(value) && Math.Abs(value) <= max;
        private static bool Positive(float value) => float.IsFinite(value) && value > 0 && value <= 1000000;
        private static bool InRange(float value, float max) => float.IsFinite(value) && value >= 0 && value <= max;
        private static void Identity(int player, long epoch, string scene, long revision)
        {
            if (player != 2 || epoch < 1 || revision < 1 || string.IsNullOrWhiteSpace(scene) || scene.Length > MaxSceneKeyLength)
                throw new ProtocolException("Invalid crew actor identity.");
            foreach (char value in scene)
                if (char.IsControl(value)) throw new ProtocolException("Invalid crew scene key.");
        }
        private static void Envelope(string roomId, int bound, int expected, long sequence, double at)
        {
            if (!Guid.TryParse(roomId, out Guid room) || room == Guid.Empty || bound != expected || sequence < 1 ||
                !double.IsFinite(at) || at < 0) throw new ProtocolException("Invalid crew receipt provenance.");
        }
    }
}
