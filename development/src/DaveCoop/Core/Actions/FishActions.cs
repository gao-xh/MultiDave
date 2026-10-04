using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DaveCoop.Core.Protocol;

namespace DaveCoop.Core.Actions
{
    public enum FishActionKind
    {
        ProbeTarget = 1, FireHarpoon = 2, FireGun = 3, SubmitQteInput = 4,
        RecallHarpoon = 5, RequestPickup = 6
    }

    public enum FishActionStatus
    {
        Queued = 1, Rejected = 2, DryRunValidated = 3, NativeStarted = 4, OutcomeUnknown = 5
    }

    public enum FishActionReason
    {
        None, SceneUnavailable, StaleScene, QueueFull, RateLimited, ReplayExpired, RequestConflict,
        WorldAuthorityUnavailable, TargetUnavailable, LoadoutUnavailable, ActorUnavailable, OutOfRange,
        NativeAdapterUnavailable, TargetBusy, InvalidStage, SceneChanged, RoomClosed
    }

    // An intent, never a client-supplied damage/capture result. Local pointers,
    // target generations and native wrappers have no place in this wire value.
    public sealed class FishActionRequest
    {
        public long RequestId { get; set; }
        public int PlayerId { get; set; }
        public long SceneEpoch { get; set; }
        public string SceneKey { get; set; }
        public FishActionKind Action { get; set; }
        public long TargetEntityId { get; set; }
        public int EquipmentSlot { get; set; }
        public long LoadoutRevision { get; set; }
        public float AimX { get; set; }
        public float AimY { get; set; }
        public long InteractionId { get; set; }
    }

    public sealed class FishActionResult
    {
        public long RequestId { get; set; }
        public int PlayerId { get; set; }
        public long SceneEpoch { get; set; }
        public string SceneKey { get; set; }
        public FishActionKind Action { get; set; }
        public long TargetEntityId { get; set; }
        public FishActionStatus Status { get; set; }
        public FishActionReason Reason { get; set; }
        public long WorldRevision { get; set; }
        public long OperationId { get; set; }
        public string RequestFingerprint { get; set; }
    }

    // Transport fills provenance from its handshake/envelope, never the payload.
    // All consumers copy this mutable DTO at ownership boundaries.
    public sealed class ReceivedFishAction
    {
        public string RoomId { get; set; }
        public int BoundPlayerId { get; set; }
        public long PacketSequence { get; set; }
        public double ReceivedAt { get; set; }
        public FishActionRequest Request { get; set; }

        public ReceivedFishAction Copy() => new ReceivedFishAction
        {
            RoomId = RoomId, BoundPlayerId = BoundPlayerId, PacketSequence = PacketSequence,
            ReceivedAt = ReceivedAt, Request = FishActions.Copy(Request)
        };
    }

    public static class FishActions
    {
        public static void ValidateRequest(FishActionRequest request)
        {
            if (request == null) throw new ProtocolException("Missing fish action request.");
            ValidateIdentity(request.RequestId, request.PlayerId, request.SceneEpoch, request.SceneKey,
                request.Action, request.TargetEntityId);
            if (request.EquipmentSlot < 0 || request.EquipmentSlot > 1 || request.LoadoutRevision < 0 ||
                request.InteractionId < 0 || !float.IsFinite(request.AimX) || !float.IsFinite(request.AimY) ||
                Math.Abs(request.AimX) > 1 || Math.Abs(request.AimY) > 1)
                throw new ProtocolException("Invalid fish action equipment, stage or aim.");
            switch (request.Action)
            {
                case FishActionKind.FireHarpoon:
                case FishActionKind.FireGun:
                    double length = Math.Sqrt((double)request.AimX * request.AimX + (double)request.AimY * request.AimY);
                    if (request.LoadoutRevision < 1 || length < 0.99 || length > 1.01)
                        throw new ProtocolException("Invalid fish action firing direction or loadout.");
                    break;
                case FishActionKind.ProbeTarget:
                case FishActionKind.RequestPickup:
                    if (request.TargetEntityId < 1) throw new ProtocolException("Fish action requires a target.");
                    break;
                case FishActionKind.SubmitQteInput:
                    if (request.TargetEntityId < 1 || request.InteractionId < 1 || request.LoadoutRevision < 1)
                        throw new ProtocolException("Fish QTE input requires a target, interaction and loadout.");
                    break;
                case FishActionKind.RecallHarpoon:
                    if (request.InteractionId < 1) throw new ProtocolException("Fish recall requires an interaction.");
                    break;
            }
        }

        public static void ValidateResult(FishActionResult result)
        {
            if (result == null) throw new ProtocolException("Missing fish action result.");
            ValidateIdentity(result.RequestId, result.PlayerId, result.SceneEpoch, result.SceneKey,
                result.Action, result.TargetEntityId);
            if (!Enum.IsDefined(typeof(FishActionStatus), result.Status) ||
                !Enum.IsDefined(typeof(FishActionReason), result.Reason) || result.WorldRevision < 0 || result.OperationId < 0 ||
                !HexFingerprint(result.RequestFingerprint))
                throw new ProtocolException("Invalid fish action result status or evidence.");
            if ((result.Action == FishActionKind.ProbeTarget || result.Action == FishActionKind.SubmitQteInput ||
                result.Action == FishActionKind.RequestPickup) && result.TargetEntityId < 1)
                throw new ProtocolException("Fish action result lost its required target.");
            switch (result.Status)
            {
                case FishActionStatus.Queued:
                    if (result.Reason != FishActionReason.None || result.OperationId != 0)
                        throw new ProtocolException("Queued action is not native execution.");
                    break;
                case FishActionStatus.Rejected:
                    if (result.Reason == FishActionReason.None || result.OperationId != 0)
                        throw new ProtocolException("Rejected action needs a reason and no native operation.");
                    break;
                case FishActionStatus.DryRunValidated:
                    if (result.Action != FishActionKind.ProbeTarget || result.Reason != FishActionReason.None || result.OperationId != 0)
                        throw new ProtocolException("Only a target probe can be a dry run.");
                    break;
                case FishActionStatus.NativeStarted:
                case FishActionStatus.OutcomeUnknown:
                    if (result.OperationId < 1 || result.Action == FishActionKind.ProbeTarget)
                        throw new ProtocolException("Native entry requires a real action and operation identity.");
                    break;
            }
        }

        public static FishActionRequest Copy(FishActionRequest request) => request == null ? null : new FishActionRequest
        {
            RequestId = request.RequestId, PlayerId = request.PlayerId, SceneEpoch = request.SceneEpoch,
            SceneKey = request.SceneKey, Action = request.Action, TargetEntityId = request.TargetEntityId,
            EquipmentSlot = request.EquipmentSlot, LoadoutRevision = request.LoadoutRevision,
            AimX = request.AimX, AimY = request.AimY, InteractionId = request.InteractionId
        };

        public static FishActionResult Copy(FishActionResult result) => result == null ? null : new FishActionResult
        {
            RequestId = result.RequestId, PlayerId = result.PlayerId, SceneEpoch = result.SceneEpoch,
            SceneKey = result.SceneKey, Action = result.Action, TargetEntityId = result.TargetEntityId,
            Status = result.Status, Reason = result.Reason, WorldRevision = result.WorldRevision,
            OperationId = result.OperationId, RequestFingerprint = result.RequestFingerprint
        };

        public static string Fingerprint(FishActionRequest request)
        {
            ValidateRequest(request);
            // Fixed field order and a length-prefixed scene make this independent
            // of JSON formatting. Signed zero has the same intent as positive zero.
            var text = new StringBuilder();
            Add(text, request.RequestId); Add(text, request.PlayerId); Add(text, request.SceneEpoch);
            Add(text, request.SceneKey.Length); text.Append(request.SceneKey).Append('|');
            Add(text, (int)request.Action); Add(text, request.TargetEntityId); Add(text, request.EquipmentSlot);
            Add(text, request.LoadoutRevision); Add(text, request.AimX == 0 ? 0 : request.AimX);
            Add(text, request.AimY == 0 ? 0 : request.AimY); Add(text, request.InteractionId);
            using SHA256 hash = SHA256.Create();
            byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()));
            var result = new StringBuilder(64);
            foreach (byte value in bytes) result.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return result.ToString();
        }

        private static void ValidateIdentity(long requestId, int playerId, long epoch, string scene, FishActionKind action, long target)
        {
            if (requestId < 1 || (playerId != 1 && playerId != 2) || epoch < 1 || target < 0 ||
                !Enum.IsDefined(typeof(FishActionKind), action)) throw new ProtocolException("Invalid fish action identity.");
            PacketCodec.RequireText(scene, 160, "fish action scene");
        }

        private static bool HexFingerprint(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char c in value)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
            return true;
        }

        private static void Add(StringBuilder text, long value) => text.Append(value.ToString(CultureInfo.InvariantCulture)).Append('|');
        private static void Add(StringBuilder text, float value) => text.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('|');
    }
}
