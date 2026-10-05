using System;
using System.Numerics;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.Session;

namespace DaveCoop.Core.World
{
    public enum HostFishInterestKind { RemoteObservation = 0, HostEmployeeBody = 1 }

    // Owned host readback supplied by the body adapter, not a received pose or
    // a native action grant. Invalid candidates are rejected by the buffer.
    public sealed class HostFishBodySample
    {
        public string RoomId { get; }
        public string MemberId { get; }
        public long ActorRevision { get; }
        public long SampleRevision { get; }
        public long SceneEpoch { get; }
        public string SceneKey { get; }
        public double SampledAt { get; }
        public Vector3 Position { get; }

        public HostFishBodySample(string roomId, string memberId, long actorRevision, long sampleRevision,
            long sceneEpoch, string sceneKey, double sampledAt, Vector3 position)
        {
            RoomId = roomId; MemberId = memberId; ActorRevision = actorRevision; SampleRevision = sampleRevision;
            SceneEpoch = sceneEpoch; SceneKey = sceneKey; SampledAt = sampledAt; Position = position;
        }
    }

    // A frozen interest position. Source kind describes the producer; neither
    // kind proves native identity, collision, action or world authority.
    public sealed class HostFishInterest
    {
        public HostFishInterestKind SourceKind { get; }
        public string RoomId { get; }
        public string MemberId { get; }
        public long ActorRevision { get; }
        public long HostSampleRevision { get; }
        public double HostSampledAt { get; }
        public int BoundPlayerId { get; }
        public long PacketSequence { get; }
        public long SceneEpoch { get; }
        public string SceneKey { get; }
        public double ReceivedAt { get; }
        public double RemoteSampleTime { get; }
        public Vector3 Position { get; }

        internal HostFishInterest(ReceivedFrame receipt)
        {
            SourceKind = HostFishInterestKind.RemoteObservation;
            RoomId = receipt.RoomId; BoundPlayerId = receipt.BoundPlayerId;
            PacketSequence = receipt.PacketSequence; SceneEpoch = receipt.Frame.SceneEpoch;
            SceneKey = receipt.Frame.SceneKey; ReceivedAt = receipt.ReceivedAt;
            RemoteSampleTime = receipt.Frame.SampleTime; Position = receipt.Frame.Root.Position;
        }

        internal HostFishInterest(HostFishBodySample sample)
        {
            SourceKind = HostFishInterestKind.HostEmployeeBody;
            RoomId = sample.RoomId; BoundPlayerId = 2; MemberId = sample.MemberId;
            ActorRevision = sample.ActorRevision; HostSampleRevision = sample.SampleRevision;
            HostSampledAt = sample.SampledAt; SceneEpoch = sample.SceneEpoch; SceneKey = sample.SceneKey;
            Position = sample.Position;
            // PacketSequence/ReceivedAt/RemoteSampleTime remain zero. A host
            // body read must never be disguised as a received remote packet.
        }
    }

    // One latest observation per actual peer. Clearing a view does not permit a
    // drained receipt to be replayed; a new peer must use a new buffer instance.
    public sealed class HostFishInterestBuffer
    {
        public const double DefaultStaleSeconds = 1;
        private readonly string _room;
        private string _bodyMember;
        private double _lastBodySampledAt = -1;
        private HostFishInterest _current;
        public HostFishInterestKind SourceKind { get; }
        public double StaleSeconds { get; }
        public long HighestPacketSequence { get; private set; }
        public long HighestActorRevision { get; private set; }
        public long HighestBodySampleRevision { get; private set; }
        public string Status { get; private set; } = "Empty";

        public HostFishInterestBuffer(string roomId, double staleSeconds = DefaultStaleSeconds,
            HostFishInterestKind sourceKind = HostFishInterestKind.RemoteObservation)
        {
            if (!Guid.TryParse(roomId, out Guid room) || room == Guid.Empty)
                throw new ArgumentException("Invalid interest room.", nameof(roomId));
            if (!double.IsFinite(staleSeconds) || staleSeconds <= 0 || staleSeconds > 5)
                throw new ArgumentOutOfRangeException(nameof(staleSeconds));
            if (sourceKind != HostFishInterestKind.RemoteObservation && sourceKind != HostFishInterestKind.HostEmployeeBody)
                throw new ArgumentOutOfRangeException(nameof(sourceKind));
            _room = roomId; SourceKind = sourceKind;
            StaleSeconds = sourceKind == HostFishInterestKind.HostEmployeeBody ? Math.Min(staleSeconds, DefaultStaleSeconds) : staleSeconds;
        }

        public bool TryAccept(ReceivedFrame receipt, SessionSnapshot state, double hostNow)
        {
            if (SourceKind != HostFishInterestKind.RemoteObservation) { Status = "WrongSourceKind"; return false; }
            if (!SessionCurrent(state)) { Clear("SessionUnavailable"); return false; }
            if (receipt == null || receipt.Frame == null || receipt.RoomId != _room ||
                receipt.BoundPlayerId != state.RemotePlayerId || receipt.Frame.PlayerId != receipt.BoundPlayerId ||
                receipt.Frame.SceneEpoch != state.SceneEpoch || receipt.Frame.SceneKey != state.SceneKey ||
                receipt.PacketSequence < 1)
            { Clear("ReceiptIdentityMismatch"); return false; }
            if (receipt.PacketSequence <= HighestPacketSequence)
            { Status = "ReceiptReplay"; return false; }
            if (!Fresh(receipt.ReceivedAt, hostNow)) { Clear("ReceiptStaleOrInvalidClock"); return false; }
            try { PacketCodec.ValidateFrame(receipt.Frame); }
            catch (ProtocolException) { Clear("InvalidFrame"); return false; }
            _current = new HostFishInterest(receipt);
            HighestPacketSequence = receipt.PacketSequence; Status = "Current";
            return true;
        }

        public bool TryAcceptHostBody(HostFishBodySample sample, SessionSnapshot state, double hostNow)
        {
            if (SourceKind != HostFishInterestKind.HostEmployeeBody) { Status = "WrongSourceKind"; return false; }
            if (!BodySessionCurrent(state)) { Clear("BodySessionUnavailable"); return false; }
            if (sample == null || sample.RoomId != _room || !CanonicalGuid(sample.RoomId) ||
                !CanonicalGuid(sample.MemberId) || (_bodyMember != null && sample.MemberId != _bodyMember) ||
                sample.ActorRevision < 1 || sample.ActorRevision != state.CrewActorRevision ||
                sample.SampleRevision < 1 || sample.SceneEpoch != state.SceneEpoch || sample.SceneKey != state.SceneKey ||
                !float.IsFinite(sample.Position.X) || !float.IsFinite(sample.Position.Y) || !float.IsFinite(sample.Position.Z))
            { Clear("BodyIdentityOrPositionMismatch"); return false; }
            if (sample.ActorRevision < HighestActorRevision || sample.SampleRevision <= HighestBodySampleRevision)
            { Status = "BodySampleReplay"; return false; }
            if (!Fresh(sample.SampledAt, hostNow) || sample.SampledAt < _lastBodySampledAt)
            { Clear("BodySampleStaleOrInvalidClock"); return false; }
            _bodyMember = sample.MemberId; _lastBodySampledAt = sample.SampledAt;
            _current = new HostFishInterest(sample);
            HighestActorRevision = sample.ActorRevision; HighestBodySampleRevision = sample.SampleRevision;
            Status = "Current"; return true;
        }

        public bool TryRead(SessionSnapshot state, double hostNow, out HostFishInterest interest)
        {
            interest = null;
            if (SourceKind == HostFishInterestKind.HostEmployeeBody ? !BodySessionCurrent(state) : !SessionCurrent(state))
            { Clear("SessionUnavailable"); return false; }
            if (_current == null) return false;
            if (_current.SceneEpoch != state.SceneEpoch || _current.SceneKey != state.SceneKey)
            { Clear("SceneChanged"); return false; }
            if (SourceKind == HostFishInterestKind.HostEmployeeBody)
            {
                if (_current.ActorRevision != state.CrewActorRevision) { Clear("ActorChanged"); return false; }
                if (!Fresh(_current.HostSampledAt, hostNow)) { Clear("BodySampleStaleOrInvalidClock"); return false; }
            }
            else if (!Fresh(_current.ReceivedAt, hostNow)) { Clear("ReceiptStaleOrInvalidClock"); return false; }
            interest = _current; return true;
        }

        public void Clear(string reason = "Cleared")
        { _current = null; Status = reason ?? "Cleared"; }

        private bool SessionCurrent(SessionSnapshot state) => state != null && state.Role == SessionRole.Host &&
            state.Phase == SessionPhase.Ready && state.RoomId == _room && state.LocalPlayerId == 1 &&
            state.RemotePlayerId == 2 && state.SceneEpoch >= 1 && !string.IsNullOrWhiteSpace(state.SceneKey);

        private bool BodySessionCurrent(SessionSnapshot state) => SessionCurrent(state) &&
            state.LocalUsesCrewActor && state.RemoteUsesCrewActor && state.CrewActorRevision >= 1;

        private static bool CanonicalGuid(string value) => Guid.TryParseExact(value, "N", out Guid id) &&
            id != Guid.Empty && value == id.ToString("N");

        private bool Fresh(double receivedAt, double now) => double.IsFinite(receivedAt) && receivedAt >= 0 &&
            double.IsFinite(now) && now >= receivedAt && now - receivedAt <= StaleSeconds;
    }
}
