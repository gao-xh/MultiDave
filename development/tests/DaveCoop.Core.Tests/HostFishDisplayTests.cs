using System;
using System.Net;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DaveCoop.Core;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.Session;
using DaveCoop.Core.World;

internal static class HostFishDisplayTests
{
    internal static void ObservationFlagWirePresenceAndCopy()
    {
        PeerIdentity identity = Identity("Guest", true);
        var hello = new WirePacket { Kind = PacketKind.Hello, Sequence = 1, Hello = identity };
        string json = Encoding.UTF8.GetString(PacketCodec.Encode(hello));
        Assert(PacketCodec.Decode(Encoding.UTF8.GetBytes(json)).Hello.RequestsHostFishDisplay,
            "explicit observation request did not survive the codec");
        const string property = "\"requestsHostFishDisplay\":true";
        foreach (string replacement in new[] { "\"requestsHostFishDisplay\":null", "\"requestsHostFishDisplay\":1",
            "\"requestsHostFishDisplay\":\"true\"", property + ",\"requestsHostFishDisplay\":false" })
            Throws<ProtocolException>(() => PacketCodec.Decode(Encoding.UTF8.GetBytes(json.Replace(property, replacement))));
        Throws<ProtocolException>(() => PacketCodec.Decode(Encoding.UTF8.GetBytes(json.Replace("," + property, ""))));

        string room = Guid.NewGuid().ToString("N");
        string welcome = Encoding.UTF8.GetString(PacketCodec.Encode(new WirePacket
        {
            Kind = PacketKind.Welcome, Sequence = 1, RoomId = room,
            Welcome = new Welcome { Identity = identity, RoomId = room }
        }));
        Throws<ProtocolException>(() => PacketCodec.Decode(Encoding.UTF8.GetBytes(welcome.Replace("," + property, ""))));
        Throws<ProtocolException>(() => PacketCodec.Decode(Encoding.UTF8.GetBytes(welcome.Replace(property, property + "," + property))));

        PeerIdentity copy = PacketCodec.CopyIdentity(identity);
        var bound = new HandshakeResult { RoomId = room, LocalPlayerId = 1, RemotePlayerId = 2, Peer = identity };
        var machine = new SessionMachine(SessionRole.Host, bound, 0);
        identity.RequestsHostFishDisplay = false; identity.Name = "mutated"; bound.Peer = Identity("replacement", false);
        Assert(copy.RequestsHostFishDisplay && copy.Name == "Guest" && machine.Snapshot.RemoteRequestsHostFishDisplay &&
            machine.Snapshot.Phase == SessionPhase.WaitingForScene && machine.Snapshot.SceneEpoch == 0,
            "caller mutation changed the bound request or the request fabricated scene readiness");
        PeerIdentity ordinary = Identity("ordinary", false);
        Assert(!PacketCodec.Decode(PacketCodec.Encode(new WirePacket { Kind = PacketKind.Hello, Sequence = 1, Hello = ordinary })).Hello.RequestsHostFishDisplay,
            "an explicit false request became an automatic display request");
    }

    internal static async Task TcpNegotiatedIdentityIsFrozenBeforeAcceptAndJoinWait()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener = new LanHost(IPAddress.Loopback, 0);
        PeerIdentity hostIdentity = Identity("Host", false), guestIdentity = Identity("Guest", true);
        Task<SessionPeer> accepting = listener.AcceptOneAsync(hostIdentity, cancellation.Token);
        hostIdentity.ModVersion = "caller changed host"; hostIdentity.RequestsHostFishDisplay = true;
        Task<SessionPeer> joining = LanGuest.ConnectAsync("127.0.0.1", listener.Port, guestIdentity, cancellation.Token);
        guestIdentity.ModVersion = "caller changed guest"; guestIdentity.RequestsHostFishDisplay = false;
        using SessionPeer guest = await joining;
        using SessionPeer host = await accepting;
        Assert(host.Snapshot.RemoteRequestsHostFishDisplay && !guest.Snapshot.RemoteRequestsHostFishDisplay &&
            host.Snapshot.RoomId == guest.Snapshot.RoomId && host.Snapshot.Phase == SessionPhase.WaitingForScene &&
            guest.Snapshot.Phase == SessionPhase.WaitingForScene,
            "the asynchronous TCP handshake borrowed mutable identity inputs or granted Ready");
        await guest.StopAsync("Identity copy verified.");
        await host.Completion.WaitAsync(cancellation.Token);
    }

    internal static async Task TcpRequestedRosterRetainsSceneRoleAndEpochGates()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener = new LanHost(IPAddress.Loopback, 0);
        Task<SessionPeer> accepting = listener.AcceptOneAsync(Identity("Host", false), cancellation.Token);
        using SessionPeer guest = await LanGuest.ConnectAsync("127.0.0.1", listener.Port, Identity("Guest", true), cancellation.Token);
        using SessionPeer host = await accepting;
        Assert(host.Snapshot.RemoteRequestsHostFishDisplay && !host.PublishWorld(World(1, "dive", 1, host.Now)),
            "a request started world transmission before scene agreement");
        host.SetLocalScene(new SceneDescriptor("dive", "host-layout"));
        guest.SetLocalScene(new SceneDescriptor("dive", "other-layout"));
        Assert(!host.PublishWorld(World(1, "dive", 1, host.Now)) && !guest.TryTakeRemoteWorld(out _),
            "observation negotiation bypassed the world fingerprint check");
        guest.SetLocalScene(new SceneDescriptor("dive", "host-layout"));
        await Until(() => host.Snapshot.Phase == SessionPhase.Ready && guest.Snapshot.Phase == SessionPhase.Ready, cancellation.Token);
        long epoch = host.Snapshot.SceneEpoch;
        Throws<ProtocolException>(() => guest.PublishWorld(World(epoch, "dive", 1, guest.Now)));
        WorldSnapshot source = World(epoch, "dive", 65, host.Now);
        Assert(host.Snapshot.RemoteRequestsHostFishDisplay && host.PublishWorld(source), "requested host roster was rejected");
        source.Entities[0].Hp = 99;
        WorldSnapshot received = null;
        await Until(() => guest.TryTakeRemoteWorld(out received), cancellation.Token);
        Assert(received.SceneEpoch == epoch && received.Entities.Length == 65 && received.Entities[0].Hp == 10,
            "the requested TCP roster lost an atomic page or borrowed caller-owned entities");
        host.SetLocalScene(null);
        await Until(() => guest.Snapshot.Phase != SessionPhase.Ready, cancellation.Token);
        Assert(!guest.TryTakeRemoteWorld(out _) && host.Snapshot.RemoteRequestsHostFishDisplay,
            "pause retained a retired roster or erased the room's negotiated request");
        host.SetLocalScene(new SceneDescriptor("dive", "host-layout"));
        await Until(() => host.Snapshot.Phase == SessionPhase.Ready && guest.Snapshot.Phase == SessionPhase.Ready, cancellation.Token);
        long nextEpoch = host.Snapshot.SceneEpoch;
        Assert(nextEpoch > epoch, "scene resume reused the retired epoch");
        Throws<ProtocolException>(() => host.PublishWorld(World(epoch, "dive", 1, host.Now)));
        Assert(host.PublishWorld(World(nextEpoch, "dive", 1, host.Now)), "current scene roster was not accepted after resume");
        received = null;
        await Until(() => guest.TryTakeRemoteWorld(out received), cancellation.Token);
        Assert(received.SceneEpoch == nextEpoch && received.Entities.Length == 1, "old scene entities survived the new roster");
        await guest.StopAsync("World request lifecycle verified.");
        await host.Completion.WaitAsync(cancellation.Token);
        Assert(host.Snapshot.Phase == SessionPhase.Closed && !guest.TryTakeRemoteWorld(out _), "close retained the fish mailbox");
    }

    internal static async Task TcpOrdinaryPeersRemainManualAndProtocolSevenIsRejected()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using (var listener = new LanHost(IPAddress.Loopback, 0))
        {
            Task<SessionPeer> accepting = listener.AcceptOneAsync(Identity("Host", false), cancellation.Token);
            using SessionPeer guest = await LanGuest.ConnectAsync("127.0.0.1", listener.Port, Identity("Guest", false), cancellation.Token);
            using SessionPeer host = await accepting;
            Assert(!host.Snapshot.RemoteRequestsHostFishDisplay && !guest.Snapshot.RemoteRequestsHostFishDisplay,
                "ordinary peers silently requested the experimental display mode");
            host.SetLocalScene(new SceneDescriptor("dive", "layout")); guest.SetLocalScene(new SceneDescriptor("dive", "layout"));
            await Until(() => host.Snapshot.Phase == SessionPhase.Ready && guest.Snapshot.Phase == SessionPhase.Ready, cancellation.Token);
            Assert(host.PublishWorld(World(host.Snapshot.SceneEpoch, "dive", 1, host.Now)), "the existing manual observation channel was disabled");
            WorldSnapshot received = null;
            await Until(() => guest.TryTakeRemoteWorld(out received), cancellation.Token);
            Assert(received.Entities.Length == 1 && !host.Snapshot.RemoteRequestsHostFishDisplay, "manual roster changed the negotiated mode");
            await guest.StopAsync(); await host.Completion.WaitAsync(cancellation.Token);
        }
        using (var listener = new LanHost(IPAddress.Loopback, 0))
        {
            PeerIdentity legacy = Identity("legacy", true); legacy.ProtocolVersion = 7;
            Task<SessionPeer> accepting = listener.AcceptOneAsync(Identity("Host", false), cancellation.Token);
            await ThrowsAsync<ProtocolException>(async () =>
            { using SessionPeer ignored = await LanGuest.ConnectAsync("127.0.0.1", listener.Port, legacy, cancellation.Token); });
            await ThrowsAsync<ProtocolException>(async () => { using SessionPeer ignored = await accepting; });
        }
    }

    private static PeerIdentity Identity(string name, bool request) => new PeerIdentity
    {
        ModVersion = "fish-display-tests", SteamBuildId = "25315876", UnityVersion = "6000.0.52f1",
        Name = name, RequestsHostFishDisplay = request
    };

    private static WorldSnapshot World(long epoch, string scene, int count, double at)
    {
        var entities = new EntityState[count];
        for (int index = 0; index < count; index++) entities[index] = new EntityState
        {
            Id = index + 1, Kind = EntityKind.Fish, DataTid = 2010007, Hp = 10, MaxHp = 10,
            Root = new Pose { Position = new Vector3(index, 0, 0), Rotation = Quaternion.Identity, Scale = Vector3.One }
        };
        return new WorldSnapshot { SceneEpoch = epoch, SceneKey = scene, Revision = 1, SampleTime = at, Entities = entities };
    }

    private static async Task Until(Func<bool> ready, CancellationToken cancellation)
    { while (!ready()) await Task.Delay(5, cancellation); }
    private static void Assert(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
