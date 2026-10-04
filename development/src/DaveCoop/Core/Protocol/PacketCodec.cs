using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DaveCoop.Core.Protocol
{
    public static class PacketCodec
    {
        public const int MaxPacketBytes = 131072;
        public const int MaxParts = 128;
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            IncludeFields = true,
            IgnoreReadOnlyProperties = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            MaxDepth = 16
        };

        public static byte[] Encode(WirePacket packet)
        {
            Validate(packet);
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(packet, Options);
            if (bytes.Length > MaxPacketBytes) throw new ProtocolException("Packet exceeds maximum size.");
            return bytes;
        }

        public static WirePacket Decode(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length == 0 || bytes.Length > MaxPacketBytes) throw new ProtocolException("Invalid packet size.");
            WirePacket packet;
            try { packet = JsonSerializer.Deserialize<WirePacket>(bytes, Options); }
            catch (JsonException) { throw new ProtocolException("Invalid packet JSON."); }
            Validate(packet);
            return packet;
        }

        public static void RequireCompatible(PeerIdentity expected, PeerIdentity actual)
        {
            ValidateIdentity(expected); ValidateIdentity(actual);
            if (actual.ProtocolVersion != expected.ProtocolVersion) throw new ProtocolException("Protocol version mismatch.");
            if (actual.ModVersion != expected.ModVersion) throw new ProtocolException("Mod version mismatch.");
            if (actual.SteamBuildId != expected.SteamBuildId) throw new ProtocolException("Steam build mismatch.");
            if (actual.UnityVersion != expected.UnityVersion) throw new ProtocolException("Unity version mismatch.");
        }

        public static void RequireRoom(WirePacket packet, string roomId)
        {
            RequireGuid(roomId);
            if (packet == null || !string.Equals(packet.RoomId, roomId, StringComparison.Ordinal)) throw new ProtocolException("Room identity mismatch.");
        }

        public static void Validate(WirePacket packet)
        {
            if (packet == null || !Enum.IsDefined(typeof(PacketKind), packet.Kind)) throw new ProtocolException("Unknown packet kind.");
            if (packet.Sequence < 1) throw new ProtocolException("Invalid packet sequence.");
            int payloads = (packet.Hello == null ? 0 : 1) + (packet.Welcome == null ? 0 : 1) +
                (packet.Reason == null ? 0 : 1) + (packet.Frame == null ? 0 : 1) +
                (packet.Scene == null ? 0 : 1) + (packet.Clock == null ? 0 : 1);
            if (payloads != 1) throw new ProtocolException("Expected exactly one packet payload.");
            switch (packet.Kind)
            {
                case PacketKind.Hello:
                    if (packet.Hello == null || packet.RoomId != null) throw new ProtocolException("Invalid hello.");
                    ValidateIdentity(packet.Hello);
                    break;
                case PacketKind.Welcome:
                    if (packet.Welcome == null) throw new ProtocolException("Missing welcome.");
                    ValidateIdentity(packet.Welcome.Identity);
                    RequireGuid(packet.Welcome.RoomId);
                    if (packet.RoomId != packet.Welcome.RoomId || packet.Welcome.HostPlayerId != 1 || packet.Welcome.AssignedPlayerId != 2)
                        throw new ProtocolException("Invalid assigned player identities.");
                    break;
                case PacketKind.Reject:
                case PacketKind.Leave:
                    RequireText(packet.Reason, 160, "reason");
                    if (packet.Kind == PacketKind.Leave) RequireGuid(packet.RoomId);
                    break;
                case PacketKind.PlayerFrame:
                    RequireGuid(packet.RoomId); ValidateFrame(packet.Frame);
                    break;
                case PacketKind.SceneChange:
                case PacketKind.SceneAck:
                case PacketKind.SceneCommit:
                case PacketKind.SceneSuspend:
                case PacketKind.ScenePause:
                    RequireGuid(packet.RoomId);
                    if (packet.Scene == null || packet.Scene.Epoch < 1) throw new ProtocolException("Invalid scene epoch.");
                    RequireText(packet.Scene.SceneKey, 160, "scene key");
                    RequireText(packet.Scene.WorldFingerprint, 160, "world fingerprint");
                    break;
                case PacketKind.Ping:
                case PacketKind.Pong:
                    RequireGuid(packet.RoomId);
                    if (packet.Clock == null || packet.Clock.Id < 1 || !double.IsFinite(packet.Clock.Time) || packet.Clock.Time < 0)
                        throw new ProtocolException("Invalid clock message.");
                    break;
            }
        }

        public static void ValidateFrame(PlayerFrame frame)
        {
            if (frame == null || (frame.PlayerId != 1 && frame.PlayerId != 2) || frame.SceneEpoch < 1)
                throw new ProtocolException("Invalid player frame identity.");
            RequireText(frame.SceneKey, 160, "scene key");
            if (!double.IsFinite(frame.SampleTime) || frame.SampleTime < 0 || frame.SampleTime > 100000000)
                throw new ProtocolException("Invalid frame time.");
            ValidatePose(frame.Root);
            if (frame.Parts == null || frame.Parts.Length > MaxParts) throw new ProtocolException("Invalid sprite part count.");
            var slots = new HashSet<string>(StringComparer.Ordinal);
            foreach (SpritePartFrame part in frame.Parts)
            {
                if (part == null) throw new ProtocolException("Missing sprite part.");
                RequireText(part.Slot, 256, "sprite slot");
                if (!slots.Add(part.Slot)) throw new ProtocolException("Duplicate sprite slot.");
                if (part.SpriteKey != null) RequireText(part.SpriteKey, 512, "sprite key");
                if (part.Visible && part.SpriteKey == null) throw new ProtocolException("Visible sprite has no asset key.");
                ValidatePose(part.Pose);
                if (part.Layer < 0 || part.Layer > 31 || part.SortingOrder < -32768 || part.SortingOrder > 32767)
                    throw new ProtocolException("Invalid sprite rendering order.");
                if (!ColorValue(part.Color.X) || !ColorValue(part.Color.Y) || !ColorValue(part.Color.Z) || !ColorValue(part.Color.W))
                    throw new ProtocolException("Invalid sprite color.");
            }
        }

        private static void ValidatePose(Pose pose)
        {
            if (!pose.IsValid() || !Bound(pose.Position.X, 100000) || !Bound(pose.Position.Y, 100000) ||
                !Bound(pose.Position.Z, 100000) || !Bound(pose.Scale.X, 64) || !Bound(pose.Scale.Y, 64) || !Bound(pose.Scale.Z, 64))
                throw new ProtocolException("Invalid or out-of-range pose.");
        }

        private static bool Bound(float value, float limit) => Math.Abs(value) <= limit;
        private static bool ColorValue(float value) => float.IsFinite(value) && value >= 0 && value <= 4;

        private static void ValidateIdentity(PeerIdentity identity)
        {
            if (identity == null || identity.ProtocolVersion < 1 || identity.ProtocolVersion > 1000)
                throw new ProtocolException("Invalid protocol identity.");
            RequireText(identity.ModVersion, 64, "mod version");
            RequireText(identity.UnityVersion, 64, "Unity version");
            RequireText(identity.Name, 32, "player name");
            if (identity.SteamBuildId == null || identity.SteamBuildId.Length > 32 ||
                !ulong.TryParse(identity.SteamBuildId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong build) || build == 0)
                throw new ProtocolException("Invalid Steam build identity.");
        }

        private static void RequireText(string text, int maxLength, string name)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > maxLength) throw new ProtocolException("Invalid " + name + ".");
            foreach (char c in text) if (char.IsControl(c)) throw new ProtocolException("Control character in " + name + ".");
        }

        private static void RequireGuid(string value)
        {
            if (!Guid.TryParse(value, out Guid id) || id == Guid.Empty) throw new ProtocolException("Invalid room identity.");
        }
    }
}
