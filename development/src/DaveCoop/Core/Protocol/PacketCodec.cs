using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DaveCoop.Core.World;
using DaveCoop.Core.Actions;
using DaveCoop.Core.Cargo;
using DaveCoop.Core.Crew;

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
            if (packet.Kind == PacketKind.MapRouteSlice) RequireRouteWireFields(bytes);
            if (packet.Kind == PacketKind.Hello && packet.Hello.ProtocolVersion >= 8)
                RequireObservationRequestWireField(bytes, false);
            if (packet.Kind == PacketKind.Welcome && packet.Welcome.Identity.ProtocolVersion >= 8)
                RequireObservationRequestWireField(bytes, true);
            if (packet.Kind == PacketKind.Hello && packet.Hello.ProtocolVersion >= 9)
                RequireCrewIdentityWireField(bytes, false);
            if (packet.Kind == PacketKind.Welcome && packet.Welcome.Identity.ProtocolVersion >= 9)
                RequireCrewIdentityWireField(bytes, true);
            if (packet.Kind == PacketKind.CrewInput || packet.Kind == PacketKind.CrewActorState)
                RequireCrewWireFields(bytes, packet.Kind);
            return packet;
        }

        private static void RequireObservationRequestWireField(ReadOnlySpan<byte> bytes, bool welcome)
        {
            using JsonDocument document = JsonDocument.Parse(bytes.ToArray());
            JsonElement identity = RequiredWireProperty(document.RootElement, welcome ? "welcome" : "hello");
            if (welcome) identity = RequiredWireProperty(identity, "identity");
            JsonElement request = RequiredWireProperty(identity, "requestsHostFishDisplay");
            if (request.ValueKind != JsonValueKind.True && request.ValueKind != JsonValueKind.False)
                throw new ProtocolException("Invalid host fish display observation request.");
        }

        public static PeerIdentity CopyIdentity(PeerIdentity identity)
        {
            ValidateIdentity(identity);
            return new PeerIdentity
            {
                ProtocolVersion = identity.ProtocolVersion, ModVersion = identity.ModVersion,
                SteamBuildId = identity.SteamBuildId, UnityVersion = identity.UnityVersion,
                Name = identity.Name, RequestsHostFishDisplay = identity.RequestsHostFishDisplay,
                UsesCrewActor = identity.UsesCrewActor
            };
        }

        // Protocol 7 requires the native route inputs to be present on the
        // wire. CLR defaults remain legal values, so deserialization alone
        // cannot distinguish an old payload from an explicitly sampled zero.
        private static void RequireRouteWireFields(ReadOnlySpan<byte> bytes)
        {
            using JsonDocument document = JsonDocument.Parse(bytes.ToArray());
            JsonElement route = RequiredWireProperty(document.RootElement, "mapRoute");
            if (route.ValueKind != JsonValueKind.Object) throw new ProtocolException("Invalid route JSON object.");
            JsonElement totalHeight = RequiredWireProperty(route, "totalSceneHeight");
            if (totalHeight.ValueKind != JsonValueKind.Number || !totalHeight.TryGetSingle(out float height) ||
                !float.IsFinite(height) || Math.Abs(height) > MapSelections.MaxTotalSceneHeight)
                throw new ProtocolException("Invalid route total height field.");
            JsonElement scenes = RequiredWireProperty(route, "scenes");
            if (scenes.ValueKind != JsonValueKind.Array) throw new ProtocolException("Invalid route scenes field.");
            foreach (JsonElement scene in scenes.EnumerateArray())
            {
                JsonElement priority = RequiredWireProperty(scene, "priority");
                JsonElement preference = RequiredWireProperty(scene, "preferenceWeight");
                JsonElement preload = RequiredWireProperty(scene, "preloadAndNotUnloadable");
                if (priority.ValueKind != JsonValueKind.Number || !priority.TryGetInt32(out _) ||
                    preference.ValueKind != JsonValueKind.Number || !preference.TryGetInt32(out _) ||
                    (preload.ValueKind != JsonValueKind.True && preload.ValueKind != JsonValueKind.False))
                    throw new ProtocolException("Invalid native route input fields.");
            }
        }

        private static JsonElement RequiredWireProperty(JsonElement value, string name)
        {
            if (value.ValueKind != JsonValueKind.Object) throw new ProtocolException("Invalid required wire JSON object.");
            JsonElement result = default; int count = 0;
            foreach (JsonProperty property in value.EnumerateObject())
                if (string.Equals(property.Name, name, StringComparison.Ordinal)) { result = property.Value; count++; }
            if (count != 1) throw new ProtocolException("Missing or duplicate required wire field: " + name);
            return result;
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
                (packet.Scene == null ? 0 : 1) + (packet.Clock == null ? 0 : 1) + (packet.World == null ? 0 : 1) +
                (packet.ActionRequest == null ? 0 : 1) + (packet.ActionResult == null ? 0 : 1) +
                (packet.MapRoute == null ? 0 : 1) + (packet.MapChoice == null ? 0 : 1) + (packet.MapRetire == null ? 0 : 1) +
                (packet.CargoInventory == null ? 0 : 1) + (packet.CrewInput == null ? 0 : 1) +
                (packet.CrewActorState == null ? 0 : 1);
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
                case PacketKind.WorldSlice:
                    RequireGuid(packet.RoomId); WorldFrames.ValidateSlice(packet.World);
                    break;
                case PacketKind.FishActionRequest:
                    RequireGuid(packet.RoomId); FishActions.ValidateRequest(packet.ActionRequest);
                    if (packet.ActionRequest.PlayerId != 2) throw new ProtocolException("Only a guest may send a fish action request.");
                    break;
                case PacketKind.FishActionResult:
                    RequireGuid(packet.RoomId); FishActions.ValidateResult(packet.ActionResult);
                    if (packet.ActionResult.PlayerId != 2) throw new ProtocolException("Fish action results must address the guest.");
                    break;
                case PacketKind.MapRouteSlice:
                    RequireGuid(packet.RoomId); ValidateMapPayload(() => MapChoiceFrames.Validate(packet.MapRoute));
                    break;
                case PacketKind.MapIgpChoice:
                    RequireGuid(packet.RoomId); ValidateMapPayload(() => MapChoiceFrames.Validate(packet.MapChoice));
                    break;
                case PacketKind.MapChoiceRetire:
                    RequireGuid(packet.RoomId); ValidateMapPayload(() => MapChoiceFrames.Validate(packet.MapRetire));
                    break;
                case PacketKind.CargoInventorySlice:
                    RequireGuid(packet.RoomId); ValidateCargoPayload(() => CargoInventoryFrames.Validate(packet.CargoInventory));
                    if (!Guid.TryParse(packet.RoomId, out Guid cargoRoom) ||
                        packet.CargoInventory.SourceRoomId != cargoRoom.ToString("N"))
                        throw new ProtocolException("Cargo inventory source room does not match its packet.");
                    break;
                case PacketKind.CrewInput:
                    RequireGuid(packet.RoomId); ValidateCrewPayload(() => CrewFrames.Validate(packet.CrewInput));
                    break;
                case PacketKind.CrewActorState:
                    RequireGuid(packet.RoomId); ValidateCrewPayload(() => CrewFrames.Validate(packet.CrewActorState));
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

        public static void ValidatePose(Pose pose)
        {
            if (!pose.IsValid() || !Bound(pose.Position.X, 100000) || !Bound(pose.Position.Y, 100000) ||
                !Bound(pose.Position.Z, 100000) || !Bound(pose.Scale.X, 64) || !Bound(pose.Scale.Y, 64) || !Bound(pose.Scale.Z, 64))
                throw new ProtocolException("Invalid or out-of-range pose.");
        }

        private static bool Bound(float value, float limit) => Math.Abs(value) <= limit;
        private static bool ColorValue(float value) => float.IsFinite(value) && value >= 0 && value <= 4;

        internal static void ValidateMapPayload(Action validate)
        {
            try { validate(); }
            catch (ArgumentException error) { throw new ProtocolException("Invalid map choice payload: " + error.Message); }
        }

        internal static void ValidateCargoPayload(Action validate)
        {
            try { validate(); }
            catch (ArgumentException) { throw new ProtocolException("Invalid cargo inventory payload."); }
        }

        internal static void ValidateCrewPayload(Action validate)
        {
            try { validate(); }
            catch (ArgumentException) { throw new ProtocolException("Invalid crew frame payload."); }
        }

        private static void RequireCrewIdentityWireField(ReadOnlySpan<byte> bytes, bool welcome)
        {
            using JsonDocument document = JsonDocument.Parse(bytes.ToArray());
            JsonElement identity = RequiredWireProperty(document.RootElement, welcome ? "welcome" : "hello");
            if (welcome) identity = RequiredWireProperty(identity, "identity");
            JsonElement optIn = RequiredWireProperty(identity, "usesCrewActor");
            if (optIn.ValueKind != JsonValueKind.True && optIn.ValueKind != JsonValueKind.False)
                throw new ProtocolException("Invalid crew actor handshake opt-in.");
        }

        private static readonly string[] CrewInputFields = { "playerId", "sceneEpoch", "sceneKey", "inputSequence", "actorRevision",
            "moveX", "moveY", "aimX", "aimY", "buttons" };
        private static readonly string[] CrewStateFields = { "playerId", "sceneEpoch", "sceneKey", "actorRevision", "stateRevision",
            "lastInputSequence", "position", "velocity", "hp", "maxHP", "oxygen", "maxOxygen", "alive", "active", "loadoutRevision",
            "capacityKg", "hasConfirmedCargoWeight" };
        private static void RequireCrewWireFields(ReadOnlySpan<byte> bytes, PacketKind kind)
        {
            using JsonDocument document = JsonDocument.Parse(bytes.ToArray());
            string payloadName = kind == PacketKind.CrewInput ? "crewInput" : "crewActorState";
            RequireExactWireFields(document.RootElement, new[] { "kind", "sequence", "roomId", payloadName });
            JsonElement payload = RequiredWireProperty(document.RootElement, payloadName);
            RequireExactWireFields(payload, kind == PacketKind.CrewInput ? CrewInputFields : CrewStateFields);
            if (kind == PacketKind.CrewActorState)
            {
                RequireExactWireFields(RequiredWireProperty(payload, "position"), new[] { "x", "y", "z" });
                RequireExactWireFields(RequiredWireProperty(payload, "velocity"), new[] { "x", "y" });
            }
        }
        private static void RequireExactWireFields(JsonElement value, string[] names)
        {
            if (value.ValueKind != JsonValueKind.Object) throw new ProtocolException("Invalid crew wire object.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                bool known = false;
                foreach (string name in names) if (property.Name == name) { known = true; break; }
                if (!known || !seen.Add(property.Name)) throw new ProtocolException("Unknown or duplicate crew wire field.");
            }
            if (seen.Count != names.Length) throw new ProtocolException("Missing required crew wire field.");
        }

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

        internal static void RequireText(string text, int maxLength, string name)
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
