using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DaveCoop.Core;
using DaveCoop.Core.Actions;
using DaveCoop.Core.Crew;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.Session;
using DaveCoop.Core.Transport;
using DaveCoop.Core.World;

internal static class CrewTransportTests
{
    internal static void CodecRequiresUniqueOptInAndCompleteInputStateSchema()
    {
        string room = Guid.NewGuid().ToString("N");
        Assert(new PeerIdentity().ProtocolVersion == 9 && (int)PacketKind.CrewInput == 90 && (int)PacketKind.CrewActorState == 91,
            "crew channel did not advance its exact protocol contract");
        string hello = Json(new WirePacket { Kind = PacketKind.Hello, Sequence = 1, Hello = Identity("Guest", true) });
        Assert(Decode(hello).Hello.UsesCrewActor, "explicit opt-in was lost");
        const string opt = "\"usesCrewActor\":true";
        foreach (string invalid in new[] { "\"usesCrewActor\":null", "\"usesCrewActor\":1", "\"usesCrewActor\":\"true\"", opt + ",\"usesCrewActor\":false" })
            Throws<ProtocolException>(() => Decode(Change(hello, opt, invalid)));
        Throws<ProtocolException>(() => Decode(Change(hello, "," + opt, "")));
        string welcome = Json(new WirePacket { Kind = PacketKind.Welcome, Sequence = 1, RoomId = room,
            Welcome = new Welcome { RoomId = room, Identity = Identity("Host", true) } });
        Throws<ProtocolException>(() => Decode(Change(welcome, "," + opt, "")));
        Throws<ProtocolException>(() => Decode(Change(welcome, opt, opt + "," + opt)));

        string input = Json(InputPacket(room, Input(1)));
        Assert(Decode(input).CrewInput.InputSequence == 1, "input roundtrip lost sequence");
        Throws<ProtocolException>(() => Decode(Change(input, "\"moveX\":0,", "")));
        Throws<ProtocolException>(() => Decode(Change(input, "\"moveX\":0", "\"moveX\":0,\"moveX\":1")));
        Throws<ProtocolException>(() => Decode(Change(input, "\"crewInput\":{", "\"crewInput\":{\"hp\":99,")));
        Throws<ProtocolException>(() => Decode(Change(input, "\"roomId\":", "\"sequence\":1,\"roomId\":")));
        string state = Json(StatePacket(room, State()));
        Assert(Decode(state).CrewActorState.HP == 30 && Decode(state).CrewActorState.BagWeightKg == null,
            "state became a cargo weight or lost survival values");
        Throws<ProtocolException>(() => Decode(Change(state, "\"alive\":true,", "")));
        Throws<ProtocolException>(() => Decode(Change(state, "\"position\":{", "\"position\":{\"x\":0,")));
        Throws<ProtocolException>(() => Decode(Change(state, "\"crewActorState\":{", "\"crewActorState\":{\"bagWeightKg\":null,")));
        var mixed = InputPacket(room, Input(1)); mixed.CrewActorState = State();
        Throws<ProtocolException>(() => PacketCodec.Encode(mixed));
        CrewInputFrame bad = Input(1); bad.AimX = float.NaN;
        Throws<ProtocolException>(() => PacketCodec.Encode(InputPacket(room, bad)));
        bad = Input(1); bad.Buttons = (CrewButtons)16;
        Throws<ProtocolException>(() => PacketCodec.Encode(InputPacket(room, bad)));
        CrewActorState malformed = State(); malformed.HP = -1;
        Throws<ProtocolException>(() => PacketCodec.Encode(StatePacket(room, malformed)));
    }

    internal static void SessionFreezesOptInAndConnectionOwnedReceipts()
    {
        var pair = new Pair(); pair.Ready();
        pair.HostIdentity.LocalUsesCrewActor = false; pair.HostIdentity.Peer.UsesCrewActor = false;
        pair.GuestIdentity.LocalUsesCrewActor = false; pair.GuestIdentity.Peer.UsesCrewActor = false;
        Assert(pair.Host.Snapshot.LocalUsesCrewActor && pair.Host.Snapshot.RemoteUsesCrewActor &&
            pair.Guest.Snapshot.LocalUsesCrewActor && pair.Guest.Snapshot.RemoteUsesCrewActor, "mutable handshake caller changed negotiated mode");
        CrewActorState source = State(); pair.Host.PublishCrewActorState(source, 0.1); source.HP = 99; pair.Pump(0.1);
        Assert(pair.Guest.TryTakeRemoteCrewActorState(out ReceivedCrewActorState state) && state.Frame.HP == 30 &&
            state.RoomId == pair.Room && state.BoundPlayerId == 1 && state.Frame.PlayerId == 2 && state.ReceivedAt == 0.1 && state.PacketSequence > 0,
            "state did not bind the actual host envelope or borrowed a caller value");
        CrewInputFrame input = Input(1); input.Buttons = CrewButtons.Fire;
        pair.Guest.PublishCrewInput(input, 0.2); input.Buttons = CrewButtons.None; input.MoveX = 1; pair.Pump(0.2);
        Assert(pair.Host.TryTakeRemoteCrewInput(out ReceivedCrewInput receipt) && receipt.Frame.Buttons == CrewButtons.Fire && receipt.Frame.MoveX == 0 &&
            receipt.RoomId == pair.Room && receipt.BoundPlayerId == 2 && receipt.ReceivedAt == 0.2 && receipt.PacketSequence > state.PacketSequence,
            "input lost the frozen edge or connection-bound source");
        receipt.Frame.InputSequence = 999;
        Assert(pair.Host.Snapshot.CrewInputSequence == 1 && pair.Guest.Snapshot.CrewStateRevision == 1,
            "taking a mutable owned copy changed internal transport high-water marks");
    }

    internal static void RolesBothOptInsAndActorBindingsRejectForgedInputs()
    {
        var noActor = new Pair(); noActor.Ready();
        Assert(!noActor.Guest.PublishCrewInput(Input(1), 0.1), "Ready alone fabricated a published host actor");
        var pair = Pair.Active();
        Throws<ProtocolException>(() => pair.Host.PublishCrewInput(Input(1), 0.1));
        Throws<ProtocolException>(() => pair.Guest.PublishCrewActorState(State(), 0.1));
        Throws<ProtocolException>(() => pair.Guest.Receive(InputPacket(pair.Room, Input(1)), 0.1));
        Throws<ProtocolException>(() => pair.Host.Receive(StatePacket(pair.Room, State()), 0.1));
        Throws<ProtocolException>(() => pair.Host.Receive(InputPacket(Guid.NewGuid().ToString("N"), Input(1)), 0.1));
        Throws<ProtocolException>(() => pair.Host.Receive(InputPacket(pair.Room, Input(1, actor: 2)), 0.1));
        Throws<ProtocolException>(() => pair.Guest.PublishCrewInput(Input(1, actor: 2), 0.1));
        CrewInputFrame bad = Input(1); bad.SceneKey = "different";
        Throws<ProtocolException>(() => pair.Host.Receive(InputPacket(pair.Room, bad), 0.1));
        bad = Input(1); bad.SceneEpoch = 2;
        Throws<ProtocolException>(() => pair.Host.Receive(InputPacket(pair.Room, bad), 0.1));
        foreach (var mode in new[] { (false, true), (true, false), (false, false) })
        {
            var ordinary = new Pair(mode.Item1, mode.Item2); ordinary.Ready();
            Throws<ProtocolException>(() => ordinary.Guest.PublishCrewInput(Input(1), 0.1));
            Throws<ProtocolException>(() => ordinary.Host.PublishCrewActorState(State(), 0.1));
            Throws<ProtocolException>(() => ordinary.Host.Receive(InputPacket(ordinary.Room, Input(1)), 0.1));
            Assert(ordinary.Host.Snapshot.Phase == SessionPhase.Ready && ordinary.Guest.Snapshot.Phase == SessionPhase.Ready,
                "pure transport rejection falsely granted actor mode or changed scene agreement");
        }
    }

    internal static void InputFifosPreserveEdgesAndFailAtTheirBound()
    {
        var pair = Pair.Active();
        for (int i = 1; i <= SessionMachine.MaxCrewInputs; i++) pair.Guest.PublishCrewInput(Input(i), 0.1);
        Throws<ProtocolException>(() => pair.Guest.PublishCrewInput(Input(33), 0.1));
        for (int i = 1; i <= SessionMachine.MaxCrewInputs; i++)
            Assert(pair.Guest.TryTakePacket(out WirePacket packet) && packet.CrewInput.InputSequence == i,
                "send bound overwrote or reordered a prior input edge");
        for (int i = 1; i <= SessionMachine.MaxCrewInputs; i++) pair.Host.Receive(InputPacket(pair.Room, Input(i)), 0.1);
        Throws<ProtocolException>(() => pair.Host.Receive(InputPacket(pair.Room, Input(33)), 0.1));
        for (int i = 1; i <= SessionMachine.MaxCrewInputs; i++)
            Assert(pair.Host.TryTakeRemoteCrewInput(out ReceivedCrewInput receipt) && receipt.Frame.InputSequence == i,
                "receive bound overwrote or reordered an input edge");
        Assert(!pair.Host.TryTakeRemoteCrewInput(out _) && pair.Host.Snapshot.CrewInputSequence == 33,
            "overflow was silently retried or added an unaccepted edge");
    }

    internal static void PauseRetiresActorAndPreservesInputHighWaterAcrossResume()
    {
        var pair = Pair.Active(); pair.Guest.PublishCrewInput(Input(1), 0.1); pair.Pump(0.1);
        pair.Host.SetLocalScene(null, 0.2); pair.Pump(0.2);
        Assert(!pair.Host.TryTakeRemoteCrewInput(out _) && !pair.Guest.TryTakeRemoteCrewActorState(out _) &&
            pair.Host.Snapshot.CrewActorRevision == 0 && pair.Guest.Snapshot.CrewActorRevision == 0, "pause retained an actor or queued input");
        Assert(!pair.Guest.PublishCrewInput(Input(100), 0.2) && !pair.Host.PublishCrewActorState(State(), 0.2),
            "legal old-scene publications became new work or protocol faults");
        pair.Ready(0.3); long epoch = pair.Host.Snapshot.SceneEpoch;
        Assert(epoch == 3 && !pair.Host.PublishCrewActorState(State(epoch: epoch), 0.3), "old actor was resurrected in a new scene");
        pair.Host.PublishCrewActorState(State(actor: 2, epoch: epoch), 0.3); pair.Pump(0.3);
        Throws<ProtocolException>(() => pair.Guest.PublishCrewInput(Input(1, actor: 2, epoch: epoch), 0.4));
        pair.Host.Receive(InputPacket(pair.Room, Input(10)), 0.4); // valid delayed old-scene input consumes its sequence
        pair.Host.Receive(InputPacket(pair.Room, Input(10, actor: 2, epoch: epoch)), 0.4);
        Assert(!pair.Host.TryTakeRemoteCrewInput(out _), "retired ingress sequence was replayed under a new actor");
        Assert(pair.Guest.PublishCrewInput(Input(11, actor: 2, epoch: epoch), 0.4), "canceled unsent sequence 100 poisoned later real input");
        pair.Pump(0.4);
        Assert(pair.Host.TryTakeRemoteCrewInput(out ReceivedCrewInput receipt) && receipt.Frame.InputSequence == 11 &&
            receipt.Frame.SceneEpoch == epoch && pair.Host.Snapshot.Phase == SessionPhase.Ready,
            "same room did not resume with a fresh actor and monotonic input");
        pair.Host.Close("done"); pair.Guest.Close("done");
        Assert(!pair.Host.TryTakeRemoteCrewInput(out _) && !pair.Guest.TryTakeRemoteCrewActorState(out _) &&
            !pair.Host.TryTakePacket(out _), "closed room retained crew work");
    }

    internal static void LatestStateRevisionAndAcknowledgementCannotReviveOldActor()
    {
        var pair = Pair.Active();
        pair.Host.PublishCrewActorState(State(revision: 2), 0.1);
        pair.Host.PublishCrewActorState(State(revision: 3), 0.1); pair.Pump(0.1);
        Assert(pair.Guest.TryTakeRemoteCrewActorState(out ReceivedCrewActorState latest) && latest.Frame.StateRevision == 3 &&
            !pair.Guest.TryTakeRemoteCrewActorState(out _), "state mailbox did not coalesce to its newest revision");
        Assert(!pair.Host.PublishCrewActorState(State(revision: 3), 0.2), "identical state revision was duplicated");
        CrewActorState conflict = State(revision: 3); conflict.HP = 29;
        Throws<ProtocolException>(() => pair.Host.PublishCrewActorState(conflict, 0.2));
        pair.Guest.PublishCrewInput(Input(1), 0.2); pair.Pump(0.2);
        CrewActorState acknowledgement = State(revision: 4); acknowledgement.LastInputSequence = 1;
        pair.Host.PublishCrewActorState(acknowledgement, 0.2); pair.Pump(0.2);
        CrewActorState regressed = State(revision: 5);
        Throws<ProtocolException>(() => pair.Host.PublishCrewActorState(regressed, 0.2));
        pair.Host.PublishCrewActorState(State(actor: 2), 0.3); pair.Pump(0.3);
        pair.Guest.Receive(StatePacket(pair.Room, State(revision: 999)), 0.3);
        Assert(pair.Guest.Snapshot.CrewActorRevision == 2 && pair.Guest.TryTakeRemoteCrewActorState(out latest) && latest.Frame.ActorRevision == 2,
            "late old actor state replaced a newer actor");
        Assert(!pair.Guest.PublishCrewInput(Input(2), 0.3) && !pair.Host.TryTakeRemoteCrewInput(out _),
            "old actor input or its queued predecessor survived actor replacement");
        CrewActorState forgedAck = State(actor: 2, revision: 2); forgedAck.LastInputSequence = 99;
        Throws<ProtocolException>(() => pair.Guest.Receive(StatePacket(pair.Room, forgedAck), 0.3));
    }

    internal static void ControlsLeadAndInputStateLanesRemainFair()
    {
        var pair = Pair.Active();
        for (int i = 1; i <= 6; i++)
        {
            FishActionRequest request = Request(i); pair.Guest.PublishFishAction(request, 0.1);
            pair.Guest.PublishCrewInput(Input(i), 0.1);
        }
        pair.Guest.PublishFrame(Frame(0.1), 0.1); pair.Guest.Tick(0.1);
        Assert(pair.Guest.TryTakePacket(out WirePacket first) && first.Kind == PacketKind.Ping, "control lost priority to input");
        int lastInput = -1;
        for (int index = 0; index < 13; index++)
        {
            Assert(pair.Guest.TryTakePacket(out WirePacket packet), "fair lane fixture lost queued work");
            if (packet.Kind == PacketKind.CrewInput)
            {
                Assert(lastInput < 0 || index - lastInput <= 3, "action/frame backlog starved input lane"); lastInput = index;
            }
        }
        Assert(lastInput >= 0, "input lane never ran");
        pair.Host.PublishWorld(World(65, 0.2), 0.2); pair.Host.PublishFrame(Frame(0.2), 0.2);
        pair.Host.PublishCrewActorState(State(revision: 2), 0.2);
        var kinds = new HashSet<PacketKind>();
        for (int i = 0; i < 3; i++) { Assert(pair.Host.TryTakePacket(out WirePacket packet), "host lane was missing"); kinds.Add(packet.Kind); }
        Assert(kinds.Contains(PacketKind.WorldSlice) && kinds.Contains(PacketKind.PlayerFrame) && kinds.Contains(PacketKind.CrewActorState),
            "paged world/frame traffic starved the newest actor state");
    }

    internal static async Task TcpCrewIdentityInputEdgesAndStateRoundTrip()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var listener = new LanHost(IPAddress.Loopback, 0);
        PeerIdentity hostIdentity = Identity("Host", true), guestIdentity = Identity("Guest", true);
        Task<SessionPeer> accepting = listener.AcceptOneAsync(hostIdentity, cancellation.Token); hostIdentity.UsesCrewActor = false;
        Task<SessionPeer> joining = LanGuest.ConnectAsync("127.0.0.1", listener.Port, guestIdentity, cancellation.Token); guestIdentity.UsesCrewActor = false;
        using SessionPeer guest = await joining; using SessionPeer host = await accepting;
        Assert(host.Snapshot.LocalUsesCrewActor && host.Snapshot.RemoteUsesCrewActor && guest.Snapshot.LocalUsesCrewActor &&
            guest.Snapshot.RemoteUsesCrewActor, "asynchronous handshake borrowed mutable opt-in values");
        await Ready(host, guest, cancellation.Token);
        CrewActorState source = State(epoch: host.Snapshot.SceneEpoch); host.PublishCrewActorState(source); source.HP = 99;
        ReceivedCrewActorState state = null;
        await Until(() => guest.TryTakeRemoteCrewActorState(out state), cancellation.Token);
        Assert(state.Frame.HP == 30 && state.RoomId == listener.RoomId && state.BoundPlayerId == 1 && state.Frame.PlayerId == 2 &&
            state.PacketSequence > 1 && state.ReceivedAt >= 0, "actual state TCP source or ownership was lost");
        CrewButtons[] edges = { CrewButtons.Fire, CrewButtons.None, CrewButtons.Recall };
        for (int i = 0; i < edges.Length; i++)
        {
            CrewInputFrame frame = Input(i + 1, epoch: guest.Snapshot.SceneEpoch); frame.Buttons = edges[i];
            Assert(guest.PublishCrewInput(frame), "actual TCP input not queued"); frame.Buttons = CrewButtons.Interact;
        }
        var received = new List<ReceivedCrewInput>();
        await Until(() => { while (host.TryTakeRemoteCrewInput(out ReceivedCrewInput receipt)) received.Add(receipt); return received.Count == 3; }, cancellation.Token);
        for (int i = 0; i < received.Count; i++)
            Assert(received[i].Frame.InputSequence == i + 1 && received[i].Frame.Buttons == edges[i] && received[i].RoomId == listener.RoomId &&
                received[i].BoundPlayerId == 2 && received[i].PacketSequence > 1 && (i == 0 || received[i].PacketSequence > received[i - 1].PacketSequence),
                "TCP collapsed an input edge or invented its sender");
        CrewActorState next = State(revision: 2, epoch: host.Snapshot.SceneEpoch); next.LastInputSequence = 3;
        host.PublishCrewActorState(next); await Until(() => guest.TryTakeRemoteCrewActorState(out state), cancellation.Token);
        Assert(state.Frame.LastInputSequence == 3 && state.Frame.HasConfirmedCargoWeight == false && state.Frame.BagWeightKg == null,
            "input acknowledgement fabricated a cargo receipt");
        await guest.StopAsync(); await host.Completion.WaitAsync(cancellation.Token);
    }

    internal static async Task TcpTakeThenPauseCancelsOldWorkAndResumesSameRoom()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var listener = new LanHost(IPAddress.Loopback, 0);
        Task<SessionPeer> accepting = listener.AcceptOneAsync(Identity("Host", true), cancellation.Token);
        using SessionPeer guest = await LanGuest.ConnectAsync("127.0.0.1", listener.Port, Identity("Guest", true), cancellation.Token);
        using SessionPeer host = await accepting; await Ready(host, guest, cancellation.Token);
        long oldEpoch = host.Snapshot.SceneEpoch;
        host.PublishCrewActorState(State(epoch: oldEpoch)); ReceivedCrewActorState state = null;
        await Until(() => guest.TryTakeRemoteCrewActorState(out state), cancellation.Token);
        guest.PublishCrewInput(Input(1, epoch: oldEpoch)); ReceivedCrewInput taken = null;
        await Until(() => host.TryTakeRemoteCrewInput(out taken), cancellation.Token);
        host.SetLocalScene(null);
        Assert(host.Snapshot.Phase != SessionPhase.Ready && host.Snapshot.CrewActorRevision == 0 &&
            taken.Frame.SceneEpoch != host.Snapshot.SceneEpoch && !host.PublishCrewActorState(State(revision: 2, epoch: oldEpoch)),
            "take was treated as durable permission or stale state publication faulted the room");
        await Until(() => guest.Snapshot.Phase != SessionPhase.Ready && guest.Snapshot.SceneEpoch > oldEpoch, cancellation.Token);
        Assert(!guest.PublishCrewInput(Input(100, epoch: oldEpoch)) && guest.Snapshot.Phase != SessionPhase.Closed,
            "a legal retired guest publication closed the TCP room");
        host.SetLocalScene(Scene());
        await Until(() => host.Snapshot.Phase == SessionPhase.Ready && guest.Snapshot.Phase == SessionPhase.Ready, cancellation.Token);
        long epoch = host.Snapshot.SceneEpoch;
        Assert(!host.PublishCrewActorState(State(epoch: epoch)), "resume reused the retired actor");
        host.PublishCrewActorState(State(actor: 2, epoch: epoch));
        await Until(() => guest.TryTakeRemoteCrewActorState(out state), cancellation.Token);
        Assert(guest.PublishCrewInput(Input(2, actor: 2, epoch: epoch)), "canceled send consumed the input high-water mark");
        await Until(() => host.TryTakeRemoteCrewInput(out taken), cancellation.Token);
        Assert(taken.Frame.ActorRevision == 2 && taken.Frame.InputSequence == 2 && host.Snapshot.RoomId == listener.RoomId &&
            guest.Snapshot.RoomId == listener.RoomId, "same TCP room failed to resume a fresh actor");
        await guest.StopAsync(); await host.Completion.WaitAsync(cancellation.Token);
    }

    internal static async Task TcpProtocolEightRejectedAndOneSidedOptInCannotSendCrew()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using (var listener = new LanHost(IPAddress.Loopback, 0))
        {
            Task<SessionPeer> accepting = listener.AcceptOneAsync(Identity("Host", true), cancellation.Token);
            PeerIdentity legacy = Identity("Guest", true); legacy.ProtocolVersion = 8;
            await ThrowsAsync<ProtocolException>(async () => { using SessionPeer ignored = await LanGuest.ConnectAsync("127.0.0.1", listener.Port, legacy, cancellation.Token); });
            await ThrowsAsync<ProtocolException>(async () => { using SessionPeer ignored = await accepting; });
        }
        using (var listener = new LanHost(IPAddress.Loopback, 0))
        {
            Task<SessionPeer> accepting = listener.AcceptOneAsync(Identity("Host", true), cancellation.Token);
            using SessionPeer guest = await LanGuest.ConnectAsync("127.0.0.1", listener.Port, Identity("Guest", false), cancellation.Token);
            using SessionPeer host = await accepting; await Ready(host, guest, cancellation.Token);
            Assert(host.Snapshot.LocalUsesCrewActor && !host.Snapshot.RemoteUsesCrewActor && !guest.Snapshot.LocalUsesCrewActor &&
                guest.Snapshot.RemoteUsesCrewActor, "one party's request opted in the other party");
            Throws<ProtocolException>(() => guest.PublishCrewInput(Input(1, epoch: guest.Snapshot.SceneEpoch)));
            await host.Completion.WaitAsync(cancellation.Token);
            Assert(guest.Snapshot.Phase == SessionPhase.Closed && !host.TryTakeRemoteCrewInput(out _), "non-negotiated crew input reached a consumer");
        }
    }

    internal static async Task TcpReceiveQueueOverflowClosesWithoutDroppingInputEdges()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var listener = new LanHost(IPAddress.Loopback, 0);
        Task<SessionPeer> accepting = listener.AcceptOneAsync(Identity("Host", true), cancellation.Token);
        using var client = new TcpClient(); await client.ConnectAsync(IPAddress.Loopback, listener.Port, cancellation.Token);
        using var raw = new FramedConnection(client.GetStream());
        await Handshake.JoinAsync(raw, Identity("Guest", true), cancellation.Token); using SessionPeer host = await accepting;
        host.SetLocalScene(Scene());
        WirePacket scene = await ReceiveKind(raw, PacketKind.SceneChange, listener.RoomId, cancellation.Token);
        await raw.SendAsync(new WirePacket { Kind = PacketKind.SceneAck, RoomId = listener.RoomId, Scene = scene.Scene }, cancellation.Token);
        await Until(() => host.Snapshot.Phase == SessionPhase.Ready, cancellation.Token);
        host.PublishCrewActorState(State(epoch: host.Snapshot.SceneEpoch));
        await ReceiveKind(raw, PacketKind.CrewActorState, listener.RoomId, cancellation.Token);
        for (int i = 1; i <= SessionMachine.MaxCrewInputs + 1; i++)
            await raw.SendAsync(InputPacket(listener.RoomId, Input(i, epoch: host.Snapshot.SceneEpoch)), cancellation.Token);
        await host.Completion.WaitAsync(cancellation.Token);
        Assert(host.Snapshot.Phase == SessionPhase.Closed && host.Snapshot.Reason.Contains("queue overflow", StringComparison.OrdinalIgnoreCase) &&
            !host.TryTakeRemoteCrewInput(out _), "actual receive overflow silently replaced an edge or left executable input queued");
    }

    private static async Task<WirePacket> ReceiveKind(FramedConnection raw, PacketKind kind, string room, CancellationToken cancellation)
    {
        while (true)
        {
            WirePacket packet = await raw.ReceiveAsync(cancellation);
            if (packet.Kind == PacketKind.Ping)
                await raw.SendAsync(new WirePacket { Kind = PacketKind.Pong, RoomId = room, Clock = new ClockMessage { Id = packet.Clock.Id, Time = 0 } }, cancellation);
            if (packet.Kind == kind) return packet;
        }
    }
    private static CrewInputFrame Input(long sequence, long actor = 1, long epoch = 1) => new CrewInputFrame
    { PlayerId = 2, SceneEpoch = epoch, SceneKey = "dive", ActorRevision = actor, InputSequence = sequence, AimX = 1 };
    private static CrewActorState State(long actor = 1, long revision = 1, long epoch = 1) => new CrewActorState
    {
        PlayerId = 2, SceneEpoch = epoch, SceneKey = "dive", ActorRevision = actor, StateRevision = revision,
        Position = new Vector3(1, 2, 0), Velocity = Vector2.Zero, HP = 30, MaxHP = 100,
        Oxygen = 90, MaxOxygen = 100, Alive = true, Active = true, LoadoutRevision = 1, CapacityKg = 20
    };
    private static WirePacket InputPacket(string room, CrewInputFrame frame) => new WirePacket
    { Kind = PacketKind.CrewInput, Sequence = 1, RoomId = room, CrewInput = CrewFrames.Copy(frame) };
    private static WirePacket StatePacket(string room, CrewActorState frame) => new WirePacket
    { Kind = PacketKind.CrewActorState, Sequence = 1, RoomId = room, CrewActorState = CrewFrames.Copy(frame) };
    private static PeerIdentity Identity(string name, bool crew) => new PeerIdentity
    { ModVersion = "0.1.44-dev", SteamBuildId = "25315876", UnityVersion = "6000.0.52f1", Name = name, UsesCrewActor = crew };
    private static SceneDescriptor Scene() => new SceneDescriptor("dive", "layout");
    private static async Task Ready(SessionPeer host, SessionPeer guest, CancellationToken cancellation)
    { host.SetLocalScene(Scene()); guest.SetLocalScene(Scene()); await Until(() => host.Snapshot.Phase == SessionPhase.Ready && guest.Snapshot.Phase == SessionPhase.Ready, cancellation); }
    private static FishActionRequest Request(long id) => new FishActionRequest
    { RequestId = id, PlayerId = 2, SceneEpoch = 1, SceneKey = "dive", Action = FishActionKind.ProbeTarget, TargetEntityId = 44 };
    private static PlayerFrame Frame(double time) => new PlayerFrame
    { SceneKey = "dive", SampleTime = time, Root = new Pose { Position = Vector3.Zero, Rotation = Quaternion.Identity, Scale = Vector3.One } };
    private static WorldSnapshot World(int count, double time)
    {
        var entities = new EntityState[count];
        for (int i = 0; i < count; i++) entities[i] = new EntityState { Id = i + 1, Kind = EntityKind.Fish, DataTid = 2010007,
            Hp = 10, MaxHp = 10, Root = new Pose { Position = Vector3.Zero, Rotation = Quaternion.Identity, Scale = Vector3.One } };
        return new WorldSnapshot { SceneEpoch = 1, SceneKey = "dive", Revision = 1, SampleTime = time, Entities = entities };
    }
    private static string Json(WirePacket packet) => Encoding.UTF8.GetString(PacketCodec.Encode(packet));
    private static WirePacket Decode(string json) => PacketCodec.Decode(Encoding.UTF8.GetBytes(json));
    private static string Change(string text, string before, string after)
    { Assert(text.Contains(before, StringComparison.Ordinal), "fixture expected field missing"); return text.Replace(before, after, StringComparison.Ordinal); }
    private static async Task Until(Func<bool> condition, CancellationToken cancellation)
    { while (!condition()) await Task.Delay(5, cancellation); }
    private static void Assert(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }

    private sealed class Pair
    {
        public readonly string Room = Guid.NewGuid().ToString("N");
        public readonly SessionMachine Host, Guest;
        public readonly HandshakeResult HostIdentity, GuestIdentity;
        private long _sequence;
        public Pair(bool host = true, bool guest = true)
        {
            HostIdentity = new HandshakeResult { RoomId = Room, LocalPlayerId = 1, RemotePlayerId = 2,
                LocalUsesCrewActor = host, Peer = Identity("Guest", guest) };
            GuestIdentity = new HandshakeResult { RoomId = Room, LocalPlayerId = 2, RemotePlayerId = 1,
                LocalUsesCrewActor = guest, Peer = Identity("Host", host) };
            Host = new SessionMachine(SessionRole.Host, HostIdentity, 0); Guest = new SessionMachine(SessionRole.Guest, GuestIdentity, 0);
        }
        public static Pair Active() { var pair = new Pair(); pair.Ready(); pair.Host.PublishCrewActorState(State(), 0); pair.Pump(0); pair.Guest.TryTakeRemoteCrewActorState(out _); return pair; }
        public void Ready(double now = 0) { Host.SetLocalScene(Scene(), now); Guest.SetLocalScene(Scene(), now); Pump(now); Assert(Host.Snapshot.Phase == SessionPhase.Ready && Guest.Snapshot.Phase == SessionPhase.Ready, "fixture scene not committed"); }
        public void Pump(double now)
        {
            bool progress;
            do
            {
                progress = false;
                while (Host.TryTakePacket(out WirePacket packet)) { packet.Sequence = ++_sequence; Guest.Receive(PacketCodec.Decode(PacketCodec.Encode(packet)), now); progress = true; }
                while (Guest.TryTakePacket(out WirePacket packet)) { packet.Sequence = ++_sequence; Host.Receive(PacketCodec.Decode(PacketCodec.Encode(packet)), now); progress = true; }
            } while (progress);
        }
    }
}
