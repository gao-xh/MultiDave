using System;
using System.Numerics;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.Session;

namespace DaveCoop.Core.World
{
    // An observed remote position, never a native employee actor or an action grant.
    public sealed class HostFishInterest
    {
        public string RoomId { get; }
        public int BoundPlayerId { get; }
        public long PacketSequence { get; }
        public long SceneEpoch { get; }
        public string SceneKey { get; }
        public double ReceivedAt { get; }
        public double RemoteSampleTime { get; }
        public Vector3 Position { get; }

        internal HostFishInterest(ReceivedFrame receipt)
        {
            RoomId = receipt.RoomId; BoundPlayerId = receipt.BoundPlayerId;
            PacketSequence = receipt.PacketSequence; SceneEpoch = receipt.Frame.SceneEpoch;
            SceneKey = receipt.Frame.SceneKey; ReceivedAt = receipt.ReceivedAt;
            RemoteSampleTime = receipt.Frame.SampleTime; Position = receipt.Frame.Root.Position;
        }
    }

    // One latest observation per actual peer. Clearing a view does not permit a
    // drained receipt to be replayed; a new peer must use a new buffer instance.
    public sealed class HostFishInterestBuffer
    {
        public const double DefaultStaleSeconds = 1;
        private readonly string _room;
        private HostFishInterest _current;
        public double StaleSeconds { get; }
        public long HighestPacketSequence { get; private set; }
        public string Status { get; private set; } = "Empty";

        public HostFishInterestBuffer(string roomId, double staleSeconds = DefaultStaleSeconds)
        {
            if (!Guid.TryParse(roomId, out Guid room) || room == Guid.Empty)
                throw new ArgumentException("Invalid interest room.", nameof(roomId));
            if (!double.IsFinite(staleSeconds) || staleSeconds <= 0 || staleSeconds > 5)
                throw new ArgumentOutOfRangeException(nameof(staleSeconds));
            _room = roomId; StaleSeconds = staleSeconds;
        }

        public bool TryAccept(ReceivedFrame receipt, SessionSnapshot state, double hostNow)
        {
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

        public bool TryRead(SessionSnapshot state, double hostNow, out HostFishInterest interest)
        {
            interest = null;
            if (!SessionCurrent(state)) { Clear("SessionUnavailable"); return false; }
            if (_current == null) return false;
            if (_current.SceneEpoch != state.SceneEpoch || _current.SceneKey != state.SceneKey)
            { Clear("SceneChanged"); return false; }
            if (!Fresh(_current.ReceivedAt, hostNow)) { Clear("ReceiptStaleOrInvalidClock"); return false; }
            interest = _current; return true;
        }

        public void Clear(string reason = "Cleared")
        { _current = null; Status = reason ?? "Cleared"; }

        private bool SessionCurrent(SessionSnapshot state) => state != null && state.Role == SessionRole.Host &&
            state.Phase == SessionPhase.Ready && state.RoomId == _room && state.LocalPlayerId == 1 &&
            state.RemotePlayerId == 2 && state.SceneEpoch >= 1 && !string.IsNullOrWhiteSpace(state.SceneKey);

        private bool Fresh(double receivedAt, double now) => double.IsFinite(receivedAt) && receivedAt >= 0 &&
            double.IsFinite(now) && now >= receivedAt && now - receivedAt <= StaleSeconds;
    }
}
