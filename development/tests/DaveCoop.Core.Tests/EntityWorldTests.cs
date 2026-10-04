using System;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using DaveCoop.Core;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.Session;
using DaveCoop.Core.World;

internal static class EntityWorldTests
{
    internal static void EntityIdentityAndReuse()
    {
        var registry = new HostEntityRegistry();
        Throws<ArgumentException>(() => registry.Bind(9, EntityKind.Fish, 3));
        registry.BeginEpoch(1);
        long a = registry.Bind(-9, EntityKind.Fish, 3), b = registry.Bind(10, EntityKind.Fish, 3);
        Assert(a != b && registry.Bind(-9, EntityKind.Fish, 3) == a, "same species or local token confused entity identity");
        registry.Unbind(-9); long respawn = registry.Bind(-9, EntityKind.Fish, 3);
        Assert(respawn > b, "pooled local token reused an old entity identity");
        long changed = registry.Bind(-9, EntityKind.Fish, 4);
        Assert(changed > respawn, "different species retained the old entity identity");
        registry.Clear(); Assert(registry.Bind(10, EntityKind.Fish, 3) > changed, "clear reused IDs within an epoch");
        registry.BeginEpoch(1); Assert(registry.Count == 1, "same epoch erased live bindings");
        registry.BeginEpoch(2); Assert(registry.Count == 0 && registry.Bind(10, EntityKind.Fish, 3) == 1, "new epoch retained old bindings");
        Throws<ArgumentException>(() => registry.BeginEpoch(1));
    }

    internal static void RegistryCapacity()
    {
        var registry = new HostEntityRegistry(); registry.BeginEpoch(1);
        for (int i = 1; i <= WorldFrames.MaxEntities; i++) registry.Bind(i, EntityKind.Fish, 3);
        Throws<InvalidOperationException>(() => registry.Bind(5000, EntityKind.Fish, 3));
        Assert(registry.Count == WorldFrames.MaxEntities && registry.Bind(1, EntityKind.Fish, 4) > WorldFrames.MaxEntities,
            "capacity failure broke existing bindings or blocked a replacement");
        registry.Unbind(1); registry.Bind(5000, EntityKind.Item, 5);
        Assert(registry.Count == WorldFrames.MaxEntities, "freed registry capacity was not reusable");
    }

    internal static void CodecAndInvalidEntities()
    {
        WorldSlice slice = WorldFrames.Split(Snapshot(2))[0];
        var packet = new WirePacket { Kind = PacketKind.WorldSlice, Sequence = 1, RoomId = Guid.NewGuid().ToString("N"), World = slice };
        WirePacket decoded = PacketCodec.Decode(PacketCodec.Encode(packet));
        Assert(decoded.World.Entities[1].Root.Position.X == 2 && decoded.World.Entities[0].DataTid == 2010007,
            "world numeric wire round trip changed identity or position");
        decoded.World.Entities[0].Hp = float.NaN; Throws<ProtocolException>(() => PacketCodec.Encode(decoded));
        var bad = Snapshot(2); bad.Entities[1].Id = bad.Entities[0].Id;
        Throws<ProtocolException>(() => WorldFrames.Split(bad));
        bad = Snapshot(1); bad.Entities[0].Hp = 11; Throws<ProtocolException>(() => WorldFrames.Split(bad));
        bad = Snapshot(1); bad.Entities[0].Root = default; Throws<ProtocolException>(() => WorldFrames.Split(bad));
        slice = WorldFrames.Split(Snapshot(1))[0]; slice.TotalEntities = int.MaxValue;
        Throws<ProtocolException>(() => WorldFrames.ValidateSlice(slice));
        bad = Snapshot(WorldFrames.MaxEntities + 1); Throws<ProtocolException>(() => WorldFrames.Split(bad));
    }

    internal static void AtomicAssemblyAndOwnership()
    {
        WorldSnapshot source = Snapshot(129); WorldSlice[] slices = WorldFrames.Split(source);
        source.Entities[0].Hp = 1;
        Assert(slices.Length == 9 && slices[0].Entities[0].Hp == 10, "split did not bound or copy the source");
        var assembler = new WorldAssembler();
        Assert(!assembler.Accept(slices[0], out _), "partial world exposed entities");
        slices[0].Entities[0].Hp = 2;
        for (int i = 1; i < slices.Length - 1; i++) Assert(!assembler.Accept(slices[i], out _), "partial world committed early");
        Assert(assembler.Accept(slices[slices.Length - 1], out WorldSnapshot complete), "complete world did not commit");
        Assert(complete.Entities.Length == 129 && complete.Entities[0].Hp == 10, "incoming data ownership was not isolated");
        Assert(!assembler.Accept(slices[0], out _), "committed revision replayed");
        assembler.Clear(); Assert(assembler.CommittedRevision == 0 && !assembler.Accept(WorldFrames.Split(Snapshot(129))[0], out _), "reset retained previous scene state");
    }

    internal static void AssemblyRejectionAndReplacement()
    {
        var assembler = new WorldAssembler(); WorldSlice[] first = WorldFrames.Split(Snapshot(65));
        Throws<ProtocolException>(() => assembler.Accept(first[1], out _));
        assembler.Accept(first[0], out _); first[1].Entities[0].Id = 1;
        Throws<ProtocolException>(() => assembler.Accept(first[1], out _));
        var replacement = Snapshot(1); replacement.Revision = 2; replacement.SampleTime = 0.2;
        Assert(assembler.Accept(WorldFrames.Split(replacement)[0], out WorldSnapshot completed) && completed.Entities.Length == 1,
            "new complete revision could not replace an incomplete old one");
        Assert(!assembler.Accept(first[1], out _), "late old revision replaced the completed world");
        var empty = Snapshot(0); empty.Revision = 3; empty.SampleTime = 0.3;
        Assert(assembler.Accept(WorldFrames.Split(empty)[0], out completed) && completed.Entities.Length == 0,
            "empty authoritative world could not remove all entities");
        replacement.Revision = 4; replacement.SampleTime = 0.1;
        Throws<ProtocolException>(() => assembler.Accept(WorldFrames.Split(replacement)[0], out _));
    }

    internal static void SessionAuthorityAndEpoch()
    {
        var pair = new Pair(); pair.Ready();
        Throws<ProtocolException>(() => pair.Guest.PublishWorld(Snapshot(1), 0.1));
        Assert(pair.Host.PublishWorld(Snapshot(65), 0.1), "host refused matching world state");
        Assert(pair.Host.TryTakePacket(out WirePacket first), "world slice not queued"); pair.Deliver(first);
        Assert(!pair.Guest.TryTakeRemoteWorld(out _), "session exposed incomplete snapshot");
        var newer = Snapshot(1); newer.SampleTime = 0.2; pair.Host.PublishWorld(newer, 0.2); pair.Pump();
        Assert(pair.Guest.TryTakeRemoteWorld(out WorldSnapshot received) && received.Revision == 2 && received.Entities.Length == 1,
            "coalesced snapshot did not commit at the guest");
        pair.Host.PublishWorld(Snapshot(2, 0.3), 0.3); pair.Host.SetLocalScene(null, 0.3); pair.Pump();
        Assert(!pair.Guest.TryTakeRemoteWorld(out _), "old scene leaked a world mailbox");
        pair.Ready(0.4); first.Sequence = 50; pair.Guest.Receive(first, 0.4);
        Assert(!pair.Guest.TryTakeRemoteWorld(out _), "old epoch leaked world entities");
        Throws<ProtocolException>(() => pair.Host.PublishWorld(Snapshot(1, 0.5), 0.5));
        var spoofed = new WirePacket
        {
            Kind = PacketKind.WorldSlice, Sequence = 51, RoomId = pair.Room,
            World = WorldFrames.Split(Snapshot(1, 0.5))[0]
        };
        Throws<ProtocolException>(() => pair.Host.Receive(spoofed, 0.5));
        spoofed.World.SceneEpoch = pair.Guest.Snapshot.SceneEpoch + 1;
        Throws<ProtocolException>(() => pair.Guest.Receive(spoofed, 0.5));
    }

    internal static void SnapshotFairnessAndBounds()
    {
        var pair = new Pair(); pair.Ready();
        WorldSnapshot full = Snapshot(WorldFrames.MaxEntities); pair.Host.PublishWorld(full, 0.1);
        var frame = new PlayerFrame
        {
            SceneKey = "dive", SampleTime = 0.1,
            Root = new Pose { Position = Vector3.Zero, Rotation = Quaternion.Identity, Scale = Vector3.One }
        };
        pair.Host.PublishFrame(frame, 0.1);
        pair.Host.TryTakePacket(out WirePacket a); pair.Host.TryTakePacket(out WirePacket b);
        Assert(a.Kind != b.Kind && (a.Kind == PacketKind.PlayerFrame || b.Kind == PacketKind.PlayerFrame), "large world starved player movement");
        int slices = a.Kind == PacketKind.WorldSlice ? 1 : 0; slices += b.Kind == PacketKind.WorldSlice ? 1 : 0;
        while (pair.Host.TryTakePacket(out WirePacket packet)) if (packet.Kind == PacketKind.WorldSlice) slices++;
        Assert(slices == 256, "world queue exceeded its bounded chunk count");
        full.SampleTime = 0.2; pair.Host.PublishWorld(full, 0.2); full.Entities[0].DataTid = -1;
        pair.Host.TryTakePacket(out a); Assert(a.World.Entities[0].DataTid == 2010007, "publisher retained mutable caller entity data");
    }

    internal static async Task TcpWorldLifecycle()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener = new LanHost(IPAddress.Loopback, 0);
        Task<SessionPeer> accepting = listener.AcceptOneAsync(Identity("Host"), cancellation.Token);
        using SessionPeer guest = await LanGuest.ConnectAsync("127.0.0.1", listener.Port, Identity("Guest"), cancellation.Token);
        using SessionPeer host = await accepting;
        var scene = new SceneDescriptor("dive", "geometry"); host.SetLocalScene(scene); guest.SetLocalScene(scene);
        await Until(() => host.Snapshot.Phase == SessionPhase.Ready && guest.Snapshot.Phase == SessionPhase.Ready, cancellation.Token);
        host.PublishWorld(Snapshot(129, host.Now));
        WorldSnapshot received = null;
        await Until(() => guest.TryTakeRemoteWorld(out received), cancellation.Token);
        Assert(received.Entities.Length == 129 && received.Revision == 1, "TCP world bootstrap lost entities or revision");
        var changed = Snapshot(1, host.Now); changed.Entities[0].Hp = 4; host.PublishWorld(changed);
        await Until(() => guest.TryTakeRemoteWorld(out received), cancellation.Token);
        Assert(received.Revision == 2 && received.Entities.Length == 1 && received.Entities[0].Hp == 4,
            "TCP final roster did not replicate state change and removal");
        host.SetLocalScene(null); await Until(() => guest.Snapshot.Phase != SessionPhase.Ready, cancellation.Token);
        Assert(!guest.TryTakeRemoteWorld(out _), "scene exit retained native-adapter world mailbox");
        await guest.StopAsync(); await host.Completion.WaitAsync(cancellation.Token);
    }

    internal static void SlowWorldProducerCannotStarveCommit()
    {
        var pair = new Pair(); pair.Ready();
        pair.Host.PublishWorld(Snapshot(65, 0.1), 0.1);
        WorldSnapshot received = null;
        for (int i = 0; i < 5; i++)
        {
            Assert(pair.Host.TryTakePacket(out WirePacket packet), "started snapshot vanished"); pair.Deliver(packet);
            pair.Guest.TryTakeRemoteWorld(out received);
            double now = 0.2 + i * 0.1; pair.Host.PublishWorld(Snapshot(65, now), now);
        }
        Assert(received != null && received.Revision == 1 && received.Entities.Length == 65,
            "continuous updates prevented a slow peer from completing the first world");
        pair.Pump();
        Assert(pair.Guest.TryTakeRemoteWorld(out received) && received.Revision == 6,
            "latest coalesced world did not follow the completed world");
    }

    internal static async Task RejectLegacyProtocol()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener = new LanHost(IPAddress.Loopback, 0);
        PeerIdentity current = Identity("Host"), legacy = Identity("Guest"); legacy.ProtocolVersion = 2;
        Assert(current.ProtocolVersion == 10, "crew actor negotiation did not advance the protocol");
        Task<SessionPeer> accepting = listener.AcceptOneAsync(current, cancellation.Token);
        bool rejectedGuest = false, rejectedHost = false;
        try { using SessionPeer unexpected = await LanGuest.ConnectAsync("127.0.0.1", listener.Port, legacy, cancellation.Token); }
        catch (ProtocolException error) { rejectedGuest = error.Message.Contains("Protocol version mismatch"); }
        try { using SessionPeer unexpected = await accepting; }
        catch (ProtocolException error) { rejectedHost = error.Message.Contains("Protocol version mismatch"); }
        Assert(rejectedGuest && rejectedHost, "same mod version silently accepted a legacy peer without world packets");
    }

    private static WorldSnapshot Snapshot(int count, double time = 0.1) => new WorldSnapshot
    {
        SceneEpoch = 1, SceneKey = "dive", Revision = 1, SampleTime = time,
        Entities = Enumerable.Range(1, count).Select(i => new EntityState
        {
            Id = i, Kind = EntityKind.Fish, DataTid = 2010007, Hp = 10, MaxHp = 10,
            Root = new Pose { Position = new Vector3(i, 0, 0), Rotation = Quaternion.Identity, Scale = Vector3.One }
        }).ToArray()
    };

    private static PeerIdentity Identity(string name) => new PeerIdentity
    { ModVersion = "0.1.14-dev", SteamBuildId = "25315876", UnityVersion = "6000.0.52f1", Name = name };
    private static async Task Until(Func<bool> condition, CancellationToken cancellation)
    {
        while (!condition()) await Task.Delay(5, cancellation);
    }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private sealed class Pair
    {
        public string Room = Guid.NewGuid().ToString("N");
        public SessionMachine Host;
        public SessionMachine Guest;
        private long _sequence;
        private double _now;
        public Pair()
        {
            Host = new SessionMachine(SessionRole.Host, new HandshakeResult { RoomId = Room, LocalPlayerId = 1, RemotePlayerId = 2 }, 0);
            Guest = new SessionMachine(SessionRole.Guest, new HandshakeResult { RoomId = Room, LocalPlayerId = 2, RemotePlayerId = 1 }, 0);
        }
        public void Ready(double now = 0)
        {
            _now = now; var scene = new SceneDescriptor("dive", "geometry"); Host.SetLocalScene(scene, now); Guest.SetLocalScene(scene, now); Pump();
            Assert(Host.Snapshot.Phase == SessionPhase.Ready && Guest.Snapshot.Phase == SessionPhase.Ready, "fixture could not enter matching world");
        }
        public void Deliver(WirePacket packet)
        {
            _now = Math.Max(_now, packet.World?.SampleTime ?? 0); packet.Sequence = ++_sequence; Guest.Receive(packet, _now);
        }
        public void Pump()
        {
            bool progress;
            do
            {
                progress = false;
                while (Host.TryTakePacket(out WirePacket packet)) { Deliver(packet); progress = true; }
                while (Guest.TryTakePacket(out WirePacket packet)) { packet.Sequence = ++_sequence; Host.Receive(packet, _now); progress = true; }
            } while (progress);
        }
    }
}
