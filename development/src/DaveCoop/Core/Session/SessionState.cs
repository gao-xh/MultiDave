using System;
using DaveCoop.Core.Protocol;

namespace DaveCoop.Core.Session
{
    public enum SessionRole { Host, Guest }
    public enum SessionPhase { WaitingForScene, WaitingForPeer, Ready, Closed }

    public sealed class SceneDescriptor
    {
        public string Key { get; }
        public string WorldFingerprint { get; }

        public SceneDescriptor(string key, string worldFingerprint)
        {
            // Reuse the wire validator without opening a connection.
            PacketCodec.Validate(new WirePacket
            {
                Kind = PacketKind.SceneChange, Sequence = 1, RoomId = Guid.NewGuid().ToString("N"),
                Scene = new SceneNotice { Epoch = 1, SceneKey = key, WorldFingerprint = worldFingerprint }
            });
            Key = key; WorldFingerprint = worldFingerprint;
        }

        internal bool Matches(SceneNotice notice) => notice != null && Key == notice.SceneKey && WorldFingerprint == notice.WorldFingerprint;
        internal bool SameAs(SceneDescriptor other) => other != null && Key == other.Key && WorldFingerprint == other.WorldFingerprint;
    }

    public sealed class SessionOptions
    {
        public double HandshakeTimeoutSeconds { get; set; } = 5;
        public double HeartbeatIntervalSeconds { get; set; } = 2;
        public double PeerTimeoutSeconds { get; set; } = 12;
        public double SceneTimeoutSeconds { get; set; } = 90;
        public double TickIntervalSeconds { get; set; } = 0.05;

        internal SessionOptions CopyValidated()
        {
            if (!Positive(HandshakeTimeoutSeconds) || !Positive(HeartbeatIntervalSeconds) ||
                !Positive(PeerTimeoutSeconds) || PeerTimeoutSeconds <= HeartbeatIntervalSeconds ||
                !Positive(SceneTimeoutSeconds) || !Positive(TickIntervalSeconds) ||
                TickIntervalSeconds > HeartbeatIntervalSeconds || TickIntervalSeconds < 0.001)
                throw new ArgumentException("Invalid session timeouts.");
            return new SessionOptions
            {
                HandshakeTimeoutSeconds = HandshakeTimeoutSeconds, HeartbeatIntervalSeconds = HeartbeatIntervalSeconds,
                PeerTimeoutSeconds = PeerTimeoutSeconds, SceneTimeoutSeconds = SceneTimeoutSeconds, TickIntervalSeconds = TickIntervalSeconds
            };
        }

        private static bool Positive(double value) => double.IsFinite(value) && value > 0 && value <= 3600;
    }

    public sealed class SessionSnapshot
    {
        public SessionRole Role { get; internal set; }
        public SessionPhase Phase { get; internal set; }
        public string RoomId { get; internal set; }
        public string Reason { get; internal set; }
        public int LocalPlayerId { get; internal set; }
        public int RemotePlayerId { get; internal set; }
        public bool RemoteRequestsHostFishDisplay { get; internal set; }
        public long SceneEpoch { get; internal set; }
        public string SceneKey { get; internal set; }
        public bool HasClockEstimate { get; internal set; }
        public double RemoteClockOffsetSeconds { get; internal set; }
        public double RoundTripSeconds { get; internal set; }
        public long MapChoiceGeneration { get; internal set; }
        public long MapChoiceRevision { get; internal set; }
        public string MapChoiceFingerprint { get; internal set; }
        public long CargoGeneration { get; internal set; }
        public long CargoRevision { get; internal set; }
        public string CargoExpeditionId { get; internal set; }
        public bool CargoPending { get; internal set; }
        public bool CargoInventoryCurrent { get; internal set; }
    }

    public sealed class SessionEvent
    {
        public string Code { get; internal set; }
        public string Message { get; internal set; }
        public SessionPhase Phase { get; internal set; }
    }

    public sealed class ReceivedFrame
    {
        // Frozen by the successful ingress, not supplied by an avatar renderer.
        public string RoomId { get; internal set; }
        public int BoundPlayerId { get; internal set; }
        public long PacketSequence { get; internal set; }
        public PlayerFrame Frame { get; internal set; }
        public double ReceivedAt { get; internal set; }
        public double LocalSampleTime { get; internal set; }
    }
}
