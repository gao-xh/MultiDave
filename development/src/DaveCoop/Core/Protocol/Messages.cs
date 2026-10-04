using System;
using System.Numerics;
using DaveCoop.Core.World;
using DaveCoop.Core.Actions;
using DaveCoop.Core.Cargo;

namespace DaveCoop.Core.Protocol
{
    public enum PacketKind
    {
        Hello = 1, Welcome = 2, Reject = 3, PlayerFrame = 10,
        SceneChange = 20, SceneAck = 21, SceneCommit = 22, SceneSuspend = 23, ScenePause = 24,
        Ping = 30, Pong = 31, Leave = 40, WorldSlice = 50,
        FishActionRequest = 60, FishActionResult = 61,
        MapRouteSlice = 70, MapIgpChoice = 71, MapChoiceRetire = 72,
        CargoInventorySlice = 80
    }

    public sealed class PeerIdentity
    {
        public int ProtocolVersion { get; set; } = 6;
        public string ModVersion { get; set; }
        public string SteamBuildId { get; set; }
        public string UnityVersion { get; set; }
        public string Name { get; set; }
    }

    public sealed class Welcome
    {
        public PeerIdentity Identity { get; set; }
        public string RoomId { get; set; }
        public int HostPlayerId { get; set; } = 1;
        public int AssignedPlayerId { get; set; } = 2;
    }

    public sealed class PlayerFrame
    {
        public int PlayerId { get; set; }
        public long SceneEpoch { get; set; }
        public string SceneKey { get; set; }
        public double SampleTime { get; set; }
        public Pose Root { get; set; }
        public SpritePartFrame[] Parts { get; set; } = Array.Empty<SpritePartFrame>();
    }

    public sealed class SpritePartFrame
    {
        public string Slot { get; set; }
        public string SpriteKey { get; set; }
        public Pose Pose { get; set; }
        public Vector4 Color { get; set; }
        public bool Visible { get; set; }
        public bool FlipX { get; set; }
        public bool FlipY { get; set; }
        public int Layer { get; set; }
        public int SortingLayer { get; set; }
        public int SortingOrder { get; set; }
    }

    public sealed class SceneNotice
    {
        public long Epoch { get; set; }
        public string SceneKey { get; set; }
        public string WorldFingerprint { get; set; }
    }

    public sealed class ClockMessage
    {
        public long Id { get; set; }
        public double Time { get; set; }
    }

    public sealed class WirePacket
    {
        public PacketKind Kind { get; set; }
        public long Sequence { get; set; }
        public string RoomId { get; set; }
        public PeerIdentity Hello { get; set; }
        public Welcome Welcome { get; set; }
        public string Reason { get; set; }
        public PlayerFrame Frame { get; set; }
        public SceneNotice Scene { get; set; }
        public ClockMessage Clock { get; set; }
        public WorldSlice World { get; set; }
        public FishActionRequest ActionRequest { get; set; }
        public FishActionResult ActionResult { get; set; }
        public MapRouteSlice MapRoute { get; set; }
        public MapIgpChoice MapChoice { get; set; }
        public MapChoiceRetire MapRetire { get; set; }
        public CargoInventorySlice CargoInventory { get; set; }
    }

    public sealed class ProtocolException : Exception
    {
        public ProtocolException(string message) : base(message) { }
    }
}
