using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using DaveCoop.Core;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.Session;

internal static class SessionTests
{
    internal static void SceneAgreement()
    {
        var pair = new Machines();
        pair.Host.SetLocalScene(Scene("a", "world-1"), 1);
        pair.Guest.SetLocalScene(Scene("a", "world-2"), 1);
        pair.Pump(1);
        Assert(pair.Host.Snapshot.Phase == SessionPhase.WaitingForPeer && pair.Guest.Snapshot.Phase == SessionPhase.WaitingForScene,
            "same scene name bypassed world fingerprint agreement");
        Assert(!pair.Host.PublishFrame(Frame("a", 1), 1), "frame accepted before scene commit");
        pair.Guest.SetLocalScene(Scene("a", "world-1"), 2);
        pair.Pump(2); pair.AssertReady(1);
    }

    internal static void SceneTransitions()
    {
        var pair = Machines.Ready();
        pair.Host.PublishFrame(Frame("a", 1), 1); pair.Pump(1);
        Assert(pair.Guest.TryTakeRemoteFrame(out _), "initial frame missing");
        pair.Host.PublishFrame(Frame("a", 2), 2); pair.Pump(2);
        pair.Host.SetLocalScene(null, 3); pair.Pump(3);
        Assert(!pair.Guest.TryTakeRemoteFrame(out _), "old mailbox survived scene suspension");
        pair.Guest.Receive(FramePacket(pair.Room, 1, 1, "a", 3), 3);
        Assert(!pair.Guest.TryTakeRemoteFrame(out _), "old epoch displayed while loading");
        pair.Guest.SetLocalScene(null, 3);
        pair.Host.SetLocalScene(Scene("b", "next-world"), 4); pair.Pump(4);
        pair.Guest.SetLocalScene(Scene("b", "next-world"), 5); pair.Pump(5);
        pair.AssertReady(3);
        pair.Guest.Receive(FramePacket(pair.Room, 1, 1, "a", 6), 6);
        Assert(!pair.Guest.TryTakeRemoteFrame(out _), "old scene leaked after new commit");
    }

    internal static void GuestReload()
    {
        var pair = Machines.Ready();
        pair.Guest.SetLocalScene(null, 2); pair.Pump(2);
        Assert(pair.Host.Snapshot.SceneEpoch == 2 && pair.Host.Snapshot.Phase == SessionPhase.WaitingForPeer,
            "guest reload did not establish a new epoch");
        pair.Guest.SetLocalScene(Scene("a", "world"), 3); pair.Pump(3); pair.AssertReady(2);
        pair.Host.Receive(FramePacket(pair.Room, 2, 1, "a", 3), 3);
        Assert(!pair.Host.TryTakeRemoteFrame(out _), "guest's old player frame survived reload");
    }

    internal static void InvalidSessionPackets()
    {
        var pair = Machines.Ready();
        Throws<ProtocolException>(() => pair.Host.Receive(FramePacket(Guid.NewGuid().ToString("N"), 2, 1, "a", 2), 2));
        Throws<ProtocolException>(() => pair.Host.Receive(FramePacket(pair.Room, 1, 1, "a", 2), 2));
        Throws<ProtocolException>(() => pair.Host.Receive(FramePacket(pair.Room, 2, 9, "a", 2), 2));
        Throws<ProtocolException>(() => pair.Host.Receive(new WirePacket
        {
            Kind = PacketKind.SceneCommit, Sequence = 1, RoomId = pair.Room,
            Scene = new SceneNotice { Epoch = 1, SceneKey = "a", WorldFingerprint = "world" }
        }, 2));
        Throws<ProtocolException>(() => pair.Guest.Receive(new WirePacket
        {
            Kind = PacketKind.SceneChange, Sequence = 1, RoomId = pair.Room,
            Scene = new SceneNotice { Epoch = 1, SceneKey = "a", WorldFingerprint = "world" }
        }, 2));
    }

    internal static void ClockEstimate()
    {
        var pair = new Machines();
        pair.Host.Tick(10);
        Assert(pair.Host.TryTakePacket(out WirePacket ping) && ping.Kind == PacketKind.Ping, "heartbeat missing");
        ping.Sequence = 1; pair.Guest.Receive(ping, 100.1);
        Assert(pair.Guest.TryTakePacket(out WirePacket pong) && pong.Kind == PacketKind.Pong, "clock reply missing");
        pong.Sequence = 1; pair.Host.Receive(pong, 10.2);
        SessionSnapshot snapshot = pair.Host.Snapshot;
        Assert(snapshot.HasClockEstimate && Math.Abs(snapshot.RoundTripSeconds - 0.2) < 0.00001 &&
            Math.Abs(snapshot.RemoteClockOffsetSeconds - 90) < 0.00001, "clock estimate ignored different process origins");
        Throws<ProtocolException>(() => pair.Host.Receive(pong, 10.3));
    }

    internal static void MailboxAndOwnership()
    {
        var pair = Machines.Ready();
        var frame = Frame("a", 1);
        pair.Host.PublishFrame(frame, 1);
        frame.Root = new Pose { Position = new Vector3(999, 0, 0), Rotation = Quaternion.Identity, Scale = Vector3.One };
        frame.Parts[0].SpriteKey = "mutated";
        pair.Pump(1);
        Assert(pair.Guest.TryTakeRemoteFrame(out ReceivedFrame received) && received.Frame.Root.Position.X == 1 &&
            received.Frame.Parts[0].SpriteKey == "fixture/sprite", "caller mutated queued DTOs");
        for (int i = 2; i <= 100; i++) pair.Host.PublishFrame(Frame("a", i), i);
        pair.Pump(100);
        Assert(pair.Guest.TryTakeRemoteFrame(out received) && received.Frame.SampleTime == 100 &&
            !pair.Guest.TryTakeRemoteFrame(out _), "unsent frames were not coalesced");
        for (int i = 101; i <= 200; i++) { pair.Host.PublishFrame(Frame("a", i), i); pair.Pump(i); }
        Assert(pair.Guest.TryTakeRemoteFrame(out received) && received.Frame.SampleTime == 200 &&
            !pair.Guest.TryTakeRemoteFrame(out _), "inbound mailbox retained a backlog");
        pair.Guest.Receive(FramePacket(pair.Room, 1, 1, "a", 199), 201);
        Assert(!pair.Guest.TryTakeRemoteFrame(out _), "older sample overwrote latest frame");
        pair.Host.Close("done");
        Assert(!pair.Host.TryTakePacket(out _) && !pair.Host.TryTakeRemoteFrame(out _) &&
            !pair.Host.PublishFrame(Frame("a", 202), 202), "closed session retained gameplay data");
    }

    internal static void TimeoutsAndQueueBounds()
    {
        var pair = new Machines();
        pair.Host.Tick(0);
        pair.Host.Receive(new WirePacket
        {
            Kind = PacketKind.Ping, Sequence = 1, RoomId = pair.Room, Clock = new ClockMessage { Id = 1, Time = 11 }
        }, 11);
        Throws<ProtocolException>(() => pair.Host.Tick(12)); // A peer sending ping but never pong also times out.
        var options = new SessionOptions { PeerTimeoutSeconds = 100, SceneTimeoutSeconds = 3 };
        var host = new SessionMachine(SessionRole.Host, IdentityResult(pair.Room, true), 0, options);
        host.SetLocalScene(Scene("a", "world"), 0);
        Throws<ProtocolException>(() => host.Tick(4));
        var guest = new SessionMachine(SessionRole.Guest, IdentityResult(pair.Room, false), 0);
        for (int i = 1; i <= 100; i++) guest.SetLocalScene(Scene("scene-" + i, "world"), i);
        int events = 0; while (guest.TryTakeEvent(out _)) events++;
        Assert(events == 64, "UI event queue was not bounded");
        host = new SessionMachine(SessionRole.Host, IdentityResult(pair.Room, true), 0);
        for (int i = 1; i <= 32; i++) host.SetLocalScene(Scene("scene-" + i, "world"), i);
        Throws<ProtocolException>(() => host.SetLocalScene(Scene("overflow", "world"), 33));
        Throws<ArgumentException>(() => new SessionMachine(SessionRole.Host, IdentityResult(pair.Room, true), 0,
            new SessionOptions { PeerTimeoutSeconds = double.NaN }));
    }

    internal static async Task LanSessionLifecycle()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener = new LanHost(IPAddress.Loopback, 0);
        Task<SessionPeer> accepted = listener.AcceptOneAsync(Identity("Host"), cancellation.Token);
        using SessionPeer guest = await LanGuest.ConnectAsync("127.0.0.1", listener.Port, Identity("Guest"), cancellation.Token);
        using SessionPeer host = await accepted;
        host.SetLocalScene(Scene("a", "world")); guest.SetLocalScene(Scene("a", "world"));
        await Until(() => host.Snapshot.Phase == SessionPhase.Ready && guest.Snapshot.Phase == SessionPhase.Ready, cancellation.Token);
        Assert(host.PublishFrame(Frame("a", host.Now)), "host frame rejected");
        Assert(guest.PublishFrame(Frame("a", guest.Now)), "guest frame rejected");
        ReceivedFrame atGuest = null, atHost = null;
        await Until(() =>
        {
            if (atGuest == null) guest.TryTakeRemoteFrame(out atGuest);
            if (atHost == null) host.TryTakeRemoteFrame(out atHost);
            return atGuest != null && atHost != null;
        }, cancellation.Token);
        Assert(atGuest.Frame.PlayerId == 1 && atHost.Frame.PlayerId == 2, "LAN peer identity assignment failed");
        await Until(() => host.Snapshot.HasClockEstimate && guest.Snapshot.HasClockEstimate, cancellation.Token);
        guest.SetLocalScene(null);
        await Until(() => host.Snapshot.SceneEpoch > 1 && host.Snapshot.Phase != SessionPhase.Ready, cancellation.Token);
        guest.SetLocalScene(Scene("a", "world"));
        await Until(() => host.Snapshot.Phase == SessionPhase.Ready && guest.Snapshot.Phase == SessionPhase.Ready, cancellation.Token);
        await guest.StopAsync("Test finished.");
        await host.Completion.WaitAsync(cancellation.Token);
        Assert(host.Snapshot.Phase == SessionPhase.Closed && host.Snapshot.Reason.Contains("Test finished"), "graceful leave reason missing");
    }

    internal static async Task SceneMismatchTimeout()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var options = new SessionOptions { SceneTimeoutSeconds = 0.2, HeartbeatIntervalSeconds = 0.03, PeerTimeoutSeconds = 1, TickIntervalSeconds = 0.01 };
        using var listener = new LanHost(IPAddress.Loopback, 0);
        Task<SessionPeer> accepted = listener.AcceptOneAsync(Identity("Host"), cancellation.Token, options);
        using SessionPeer guest = await LanGuest.ConnectAsync("127.0.0.1", listener.Port, Identity("Guest"), cancellation.Token, options);
        using SessionPeer host = await accepted;
        host.SetLocalScene(Scene("a", "one")); guest.SetLocalScene(Scene("a", "two"));
        await host.Completion.WaitAsync(cancellation.Token); await guest.Completion.WaitAsync(cancellation.Token);
        Assert(host.Snapshot.Phase == SessionPhase.Closed && host.Snapshot.Reason.Contains("Scene acknowledgement timed out"),
            "different worlds stayed connected without a scene deadline");
    }

    internal static async Task SilentHandshakeTimeout()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener = new LanHost(IPAddress.Loopback, 0);
        Task<SessionPeer> accepted = listener.AcceptOneAsync(Identity("Host"), cancellation.Token,
            new SessionOptions { HandshakeTimeoutSeconds = 0.1 });
        using var silent = new TcpClient();
        await silent.ConnectAsync(IPAddress.Loopback, listener.Port, cancellation.Token);
        await ThrowsAsync<OperationCanceledException>(async () => { using SessionPeer peer = await accepted; });
        int read = await silent.GetStream().ReadAsync(new byte[4], cancellation.Token);
        Assert(read == 0, "handshake timeout left socket open");
    }

    internal static async Task CancelListeningAndConnected()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        using var listener = new LanHost(IPAddress.Loopback, 0);
        int port = listener.Port;
        Task<SessionPeer> pending = listener.AcceptOneAsync(Identity("Host"), cancellation.Token);
        cancellation.Cancel();
        await ThrowsAsync<OperationCanceledException>(async () => { using SessionPeer peer = await pending; });
        using var replacement = new LanHost(IPAddress.Loopback, port);
        using var lifetime = new CancellationTokenSource();
        Task<SessionPeer> accepted = replacement.AcceptOneAsync(Identity("Host"), lifetime.Token);
        using SessionPeer guest = await LanGuest.ConnectAsync("127.0.0.1", port, Identity("Guest"), timeout.Token);
        using SessionPeer host = await accepted;
        lifetime.Cancel();
        await host.Completion.WaitAsync(timeout.Token); await guest.Completion.WaitAsync(timeout.Token);
        Assert(host.Snapshot.Phase == SessionPhase.Closed && guest.Snapshot.Phase == SessionPhase.Closed, "cancelled session did not terminate");
    }

    private static SceneDescriptor Scene(string key, string fingerprint) => new SceneDescriptor(key, fingerprint);
    private static PeerIdentity Identity(string name) => new PeerIdentity
    {
        ModVersion = "core-tests", SteamBuildId = "25315876", UnityVersion = "6000.0.52f1", Name = name
    };

    private static HandshakeResult IdentityResult(string room, bool host) => new HandshakeResult
    {
        RoomId = room, LocalPlayerId = host ? 1 : 2, RemotePlayerId = host ? 2 : 1
    };

    private static PlayerFrame Frame(string scene, double time) => new PlayerFrame
    {
        PlayerId = 1, SceneEpoch = 1, SceneKey = scene, SampleTime = time,
        Root = new Pose { Position = Vector3.UnitX, Rotation = Quaternion.Identity, Scale = Vector3.One },
        Parts = new[] { new SpritePartFrame
        {
            Slot = "body", SpriteKey = "fixture/sprite", Visible = true, Color = Vector4.One,
            Pose = new Pose { Rotation = Quaternion.Identity, Scale = Vector3.One }
        } }
    };

    private static WirePacket FramePacket(string room, int player, long epoch, string scene, double time)
    {
        PlayerFrame frame = Frame(scene, time); frame.PlayerId = player; frame.SceneEpoch = epoch;
        return new WirePacket { Kind = PacketKind.PlayerFrame, Sequence = 1, RoomId = room, Frame = frame };
    }

    private static async Task Until(Func<bool> condition, CancellationToken cancellation)
    {
        while (!condition()) await Task.Delay(5, cancellation);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<T>(Action run) where T : Exception
    {
        try { run(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private static async Task ThrowsAsync<T>(Func<Task> run) where T : Exception
    {
        try { await run(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private sealed class Machines
    {
        public string Room = Guid.NewGuid().ToString("N");
        public SessionMachine Host;
        public SessionMachine Guest;
        private long _hostSequence;
        private long _guestSequence;

        public Machines()
        {
            Host = new SessionMachine(SessionRole.Host, IdentityResult(Room, true), 0);
            Guest = new SessionMachine(SessionRole.Guest, IdentityResult(Room, false), 0);
        }

        public static Machines Ready()
        {
            var pair = new Machines();
            pair.Host.SetLocalScene(Scene("a", "world"), 0); pair.Guest.SetLocalScene(Scene("a", "world"), 0);
            pair.Pump(0); pair.AssertReady(1); return pair;
        }

        public void Pump(double now)
        {
            bool progress;
            do
            {
                progress = false;
                while (Host.TryTakePacket(out WirePacket toGuest))
                {
                    toGuest.Sequence = ++_hostSequence; Guest.Receive(toGuest, now); progress = true;
                }
                while (Guest.TryTakePacket(out WirePacket toHost))
                {
                    toHost.Sequence = ++_guestSequence; Host.Receive(toHost, now); progress = true;
                }
            } while (progress);
        }

        public void AssertReady(long epoch) => Assert(Host.Snapshot.Phase == SessionPhase.Ready &&
            Guest.Snapshot.Phase == SessionPhase.Ready && Host.Snapshot.SceneEpoch == epoch && Guest.Snapshot.SceneEpoch == epoch,
            "scene commit or epoch disagreed");
    }
}
