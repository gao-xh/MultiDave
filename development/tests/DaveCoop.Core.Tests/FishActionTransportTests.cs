using System;
using System.Collections.Generic;
using System.Net;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using DaveCoop.Core;
using DaveCoop.Core.Actions;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.Session;
using DaveCoop.Core.World;

internal static class FishActionTransportTests
{
    internal static void CodecAndSinglePayload()
    {
        string room = Guid.NewGuid().ToString("N");
        FishActionRequest request = Request(1);
        var packet = RequestPacket(room, request);
        WirePacket decoded = PacketCodec.Decode(PacketCodec.Encode(packet));
        Assert(new PeerIdentity().ProtocolVersion == 5 && decoded.Kind == PacketKind.FishActionRequest &&
            FishActions.Fingerprint(decoded.ActionRequest) == FishActions.Fingerprint(request), "action request lost fields or protocol version");
        FishActionResult result = Result(request, FishActionStatus.DryRunValidated);
        packet = ResultPacket(room, result);
        decoded = PacketCodec.Decode(PacketCodec.Encode(packet));
        Assert(decoded.ActionResult.Status == FishActionStatus.DryRunValidated && decoded.ActionResult.OperationId == 0 &&
            decoded.ActionResult.RequestFingerprint == FishActions.Fingerprint(request), "dry run became a native outcome");
        packet.ActionRequest = request;
        Throws<ProtocolException>(() => PacketCodec.Encode(packet));
        packet = RequestPacket(room, request); packet.ActionRequest.PlayerId = 1;
        Throws<ProtocolException>(() => PacketCodec.Encode(packet));
        packet = ResultPacket(room, Result(Request(2), FishActionStatus.DryRunValidated));
        packet.ActionResult.Status = (FishActionStatus)6;
        Throws<ProtocolException>(() => PacketCodec.Encode(packet));
        packet.ActionResult.Status = FishActionStatus.NativeStarted; packet.ActionResult.OperationId = 42;
        Throws<ProtocolException>(() => PacketCodec.Encode(packet));
        packet = RequestPacket(room, Request(3)); packet.ActionRequest.AimX = float.NaN;
        Throws<ProtocolException>(() => PacketCodec.Encode(packet));
    }

    internal static void SessionOwnershipAndProvenance()
    {
        var pair = Pair.Ready();
        FishActionRequest source = Request(1);
        Assert(pair.Guest.PublishFishAction(source, 0.1), "guest action not admitted");
        source.TargetEntityId = 999;
        pair.Pump(0.1);
        Assert(pair.Host.TryTakeRemoteFishAction(out ReceivedFishAction received) && received.Request.TargetEntityId == 44 &&
            received.RoomId == pair.Room && received.BoundPlayerId == 2 && received.PacketSequence > 0 && received.ReceivedAt == 0.1,
            "request ownership or connection-bound provenance was lost");
        FishActionResult queued = Result(received.Request, FishActionStatus.Queued);
        Assert(pair.Host.PublishFishActionResult(queued, 0.2), "host result not admitted");
        queued.TargetEntityId = 999;
        pair.Pump(0.2);
        Assert(pair.Guest.TryTakeRemoteFishActionResult(out FishActionResult observed) && observed.TargetEntityId == 44 &&
            observed.Status == FishActionStatus.Queued, "result caller mutated a queued DTO");
        FishActionResult terminal = Result(received.Request, FishActionStatus.Rejected, FishActionReason.NativeAdapterUnavailable);
        pair.Host.PublishFishActionResult(terminal, 0.3); pair.Host.PublishFishActionResult(terminal, 0.3); pair.Pump(0.3);
        Assert(pair.Guest.TryTakeRemoteFishActionResult(out observed) && observed.Reason == FishActionReason.NativeAdapterUnavailable &&
            !pair.Guest.TryTakeRemoteFishActionResult(out _), "identical terminal retry became two outcomes or an unsolicited result");
        Throws<ProtocolException>(() => pair.Guest.PublishFishAction(Request(1), 0.4));
    }

    internal static void AuthorityAndForgedResults()
    {
        var pair = Pair.Ready();
        FishActionRequest request = Request(1);
        pair.Guest.PublishFishAction(request, 0.1);
        Throws<ProtocolException>(() => pair.Host.PublishFishAction(request, 0.1));
        Throws<ProtocolException>(() => pair.Guest.PublishFishActionResult(Result(request, FishActionStatus.Queued), 0.1));
        Throws<ProtocolException>(() => pair.Guest.Receive(RequestPacket(pair.Room, request), 0.1));
        Throws<ProtocolException>(() => pair.Host.Receive(ResultPacket(pair.Room, Result(request, FishActionStatus.Queued)), 0.1));
        Throws<ProtocolException>(() => pair.Host.Receive(RequestPacket(Guid.NewGuid().ToString("N"), request), 0.1));
        FishActionResult forged = Result(request, FishActionStatus.Queued); forged.TargetEntityId++;
        Throws<ProtocolException>(() => pair.Guest.Receive(ResultPacket(pair.Room, forged), 0.1));
        forged = Result(request, FishActionStatus.Queued); forged.RequestFingerprint = new string('a', 64);
        Throws<ProtocolException>(() => pair.Guest.Receive(ResultPacket(pair.Room, forged), 0.1));
        forged = Result(Request(99), FishActionStatus.Queued);
        Throws<ProtocolException>(() => pair.Guest.Receive(ResultPacket(pair.Room, forged), 0.1));
        FishActionRequest future = Request(2); future.SceneEpoch = 2;
        Throws<ProtocolException>(() => pair.Host.Receive(RequestPacket(pair.Room, future), 0.1));
        FishActionRequest wrongScene = Request(2); wrongScene.SceneKey = "different";
        Throws<ProtocolException>(() => pair.Host.Receive(RequestPacket(pair.Room, wrongScene), 0.1));
        Assert(!pair.Guest.TryTakeRemoteFishActionResult(out _), "forged result entered the game-adapter mailbox");
    }

    internal static void SceneInvalidationAndRetiredRequests()
    {
        var pair = Pair.Ready();
        FishActionRequest old = Request(1);
        pair.Guest.PublishFishAction(old, 0.1); pair.Pump(0.1);
        pair.Host.PublishFishActionResult(Result(old, FishActionStatus.Queued), 0.1);
        pair.Host.SetLocalScene(null, 0.2); pair.Pump(0.2);
        Assert(!pair.Host.TryTakeRemoteFishAction(out _) && !pair.Guest.TryTakeRemoteFishActionResult(out _) &&
            !pair.Guest.TryTakePacket(out _), "scene suspension retained action queues or guest outstanding sends");
        pair.Host.Receive(RequestPacket(pair.Room, old), 0.3);
        Assert(pair.Host.TryTakeRemoteFishAction(out ReceivedFishAction retired) && retired.Request.SceneEpoch == 1 &&
            pair.Host.Snapshot.Phase != SessionPhase.Ready, "retired intent did not reach the gate for ID accounting and rejection");
        pair.Host.PublishFishActionResult(Result(old, FishActionStatus.Rejected, FishActionReason.StaleScene), 0.3); pair.Pump(0.3);
        Assert(!pair.Guest.TryTakeRemoteFishActionResult(out _), "retired result reached the new scene");
        pair.EnterReady(0.4);
        Assert(pair.Host.Snapshot.SceneEpoch == 3, "fixture did not advance through suspension");
        FishActionRequest fresh = Request(2, 3);
        pair.Guest.PublishFishAction(fresh, 0.5); pair.Pump(0.5);
        Assert(pair.Host.TryTakeRemoteFishAction(out ReceivedFishAction current) && current.Request.SceneEpoch == 3,
            "fresh scene could not carry a new action");
        pair.Guest.Receive(ResultPacket(pair.Room, Result(old, FishActionStatus.Queued)), 0.5);
        Assert(!pair.Guest.TryTakeRemoteFishActionResult(out _), "late old result invalidated a fresh request");
        pair.Host.PublishFishActionResult(Result(current.Request, FishActionStatus.DryRunValidated), 0.5); pair.Pump(0.5);
        Assert(pair.Guest.TryTakeRemoteFishActionResult(out FishActionResult result) && result.RequestId == 2,
            "old result canceled the unrelated fresh request");
        pair.Host.Close("done"); pair.Guest.Close("done");
        Assert(!pair.Host.TryTakeRemoteFishAction(out _) && !pair.Guest.TryTakeRemoteFishActionResult(out _) &&
            !pair.Host.TryTakePacket(out _) && !pair.Guest.PublishFishAction(Request(3, 3), 0.6), "closed room retained action work");
    }

    internal static void FifoBoundsAndNoOverwrite()
    {
        var pair = Pair.Ready();
        for (int i = 1; i <= SessionMachine.MaxOutgoingFishActions; i++) pair.Guest.PublishFishAction(Request(i), 0.1);
        Throws<ProtocolException>(() => pair.Guest.PublishFishAction(Request(33), 0.1));
        for (int i = 1; i <= SessionMachine.MaxOutgoingFishActions; i++)
        {
            Assert(pair.Guest.TryTakePacket(out WirePacket packet) && packet.ActionRequest.RequestId == i, "send overflow overwrote or reordered intent");
        }
        Assert(!pair.Guest.TryTakePacket(out _), "send FIFO exceeded its bound");
        Throws<ProtocolException>(() => pair.Guest.PublishFishAction(Request(33), 0.1));
        var receiver = Pair.Ready();
        for (int i = 1; i <= SessionMachine.MaxRemoteFishActions; i++) receiver.Host.Receive(RequestPacket(receiver.Room, Request(i)), 0.1);
        Throws<ProtocolException>(() => receiver.Host.Receive(RequestPacket(receiver.Room, Request(17)), 0.1));
        for (int i = 1; i <= SessionMachine.MaxRemoteFishActions; i++)
            Assert(receiver.Host.TryTakeRemoteFishAction(out ReceivedFishAction action) && action.Request.RequestId == i,
                "receive overflow overwrote or reordered intent");
        Assert(!receiver.Host.TryTakeRemoteFishAction(out _), "receive FIFO exceeded its bound");
    }

    internal static void ResultBoundsAndPendingRetries()
    {
        var pair = Pair.Ready();
        FishActionRequest request = Request(1);
        pair.Guest.PublishFishAction(request, 0.1); pair.Guest.PublishFishAction(request, 0.1);
        pair.Pump(0.1);
        Assert(pair.Host.TryTakeRemoteFishAction(out ReceivedFishAction first) && pair.Host.TryTakeRemoteFishAction(out ReceivedFishAction retry) &&
            first.Request.RequestId == retry.Request.RequestId && first.PacketSequence != retry.PacketSequence,
            "transport incorrectly replaced an intent or treated packet sequence as request identity");
        FishActionResult queued = Result(request, FishActionStatus.Queued);
        for (int i = 0; i < SessionMachine.MaxOutgoingFishActions; i++) pair.Host.PublishFishActionResult(queued, 0.2);
        Throws<ProtocolException>(() => pair.Host.PublishFishActionResult(queued, 0.2));
        pair.Pump(0.2);
        Throws<ProtocolException>(() => pair.Guest.Receive(ResultPacket(pair.Room, queued), 0.2));
        for (int i = 0; i < SessionMachine.MaxRemoteFishActionResults; i++)
            Assert(pair.Guest.TryTakeRemoteFishActionResult(out FishActionResult received) && received.Status == FishActionStatus.Queued,
                "result receive overflow replaced earlier evidence");
        Assert(!pair.Guest.TryTakeRemoteFishActionResult(out _), "result FIFO exceeded its bound");
    }

    internal static void ControlPriorityAndGameplayFairness()
    {
        var pair = Pair.Ready();
        for (int i = 1; i <= 20; i++) pair.Host.PublishFishActionResult(Result(Request(i), FishActionStatus.Queued), 0.1);
        pair.Host.PublishFrame(Frame(0.1), 0.1); pair.Host.PublishWorld(World(65, 0.1), 0.1); pair.Host.Tick(0.1);
        Assert(pair.Host.TryTakePacket(out WirePacket control) && control.Kind == PacketKind.Ping, "action backlog starved heartbeat control");
        var lanes = new HashSet<PacketKind>();
        for (int i = 0; i < 3; i++)
        { Assert(pair.Host.TryTakePacket(out WirePacket packet), "fair scheduler lost a populated lane"); lanes.Add(packet.Kind); }
        Assert(lanes.Contains(PacketKind.FishActionResult) && lanes.Contains(PacketKind.PlayerFrame) && lanes.Contains(PacketKind.WorldSlice),
            "populated action/frame/world lanes did not each receive a turn");
        int expectedAction = 2, actions = 1, frames = 1, worlds = 1;
        for (int i = 0; i < 57; i++)
        {
            double now = 0.2 + i * 0.01;
            pair.Host.PublishFrame(Frame(now), now); pair.Host.PublishWorld(World(65, now), now);
            Assert(pair.Host.TryTakePacket(out WirePacket packet), "continuous production lost gameplay work");
            if (packet.Kind == PacketKind.FishActionResult)
            { Assert(packet.ActionResult.RequestId == expectedAction++, "fair scheduling reordered the result FIFO"); actions++; }
            else if (packet.Kind == PacketKind.PlayerFrame) frames++;
            else if (packet.Kind == PacketKind.WorldSlice) worlds++;
        }
        Assert(actions == 20 && frames >= 19 && worlds >= 19, "continuous snapshots starved actions or movement/world lanes");
        pair.Host.PublishFishActionResult(Result(Request(21), FishActionStatus.Queued), 1);
        pair.Host.SetLocalScene(null, 1);
        Assert(pair.Host.TryTakePacket(out control) && control.Kind == PacketKind.SceneSuspend && !pair.Host.TryTakePacket(out _),
            "scene control did not clear/precede outgoing gameplay");
    }

    internal static void NativeEntryCannotBecomeCaptureSuccess()
    {
        var pair = Pair.Ready();
        FishActionRequest request = Request(1); request.Action = FishActionKind.FireGun; request.LoadoutRevision = 1; request.AimX = 1;
        pair.Guest.PublishFishAction(request, 0.1);
        FishActionResult started = Result(request, FishActionStatus.NativeStarted); started.OperationId = 42;
        pair.Guest.Receive(ResultPacket(pair.Room, started), 0.1);
        Assert(pair.Guest.TryTakeRemoteFishActionResult(out FishActionResult received) && received.Status == FishActionStatus.NativeStarted,
            "native entry observation lost its explicit limited status");
        FishActionResult changed = FishActions.Copy(started); changed.OperationId = 43;
        Throws<ProtocolException>(() => pair.Guest.Receive(ResultPacket(pair.Room, changed), 0.1));
        Throws<ProtocolException>(() => pair.Guest.Receive(ResultPacket(pair.Room, Result(request, FishActionStatus.Queued)), 0.1));
        FishActionResult unknown = FishActions.Copy(started); unknown.Status = FishActionStatus.OutcomeUnknown;
        pair.Guest.Receive(ResultPacket(pair.Room, unknown), 0.1);
        Assert(pair.Guest.TryTakeRemoteFishActionResult(out received) && received.Status == FishActionStatus.OutcomeUnknown,
            "uncertain native entry became a successful catch");
    }

    internal static async Task TcpRequestResultRoundTrip()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener = new LanHost(IPAddress.Loopback, 0);
        Task<SessionPeer> accepting = listener.AcceptOneAsync(Identity("Host"), cancellation.Token);
        using SessionPeer guest = await LanGuest.ConnectAsync("127.0.0.1", listener.Port, Identity("Guest"), cancellation.Token);
        using SessionPeer host = await accepting;
        var scene = new SceneDescriptor("dive", "layout"); host.SetLocalScene(scene); guest.SetLocalScene(scene);
        await Until(() => host.Snapshot.Phase == SessionPhase.Ready && guest.Snapshot.Phase == SessionPhase.Ready, cancellation.Token);
        FishActionRequest request = Request(1, guest.Snapshot.SceneEpoch);
        Assert(guest.PublishFishAction(request), "TCP guest did not queue the request"); request.TargetEntityId = 999;
        ReceivedFishAction incoming = null;
        await Until(() => host.TryTakeRemoteFishAction(out incoming), cancellation.Token);
        Assert(incoming.RoomId == listener.RoomId && incoming.BoundPlayerId == 2 && incoming.PacketSequence > 0 &&
            incoming.Request.TargetEntityId == 44, "TCP provenance/DTO ownership was not preserved");
        Assert(host.PublishFishActionResult(Result(incoming.Request, FishActionStatus.DryRunValidated)), "TCP host did not queue a dry-run result");
        FishActionResult received = null;
        await Until(() => guest.TryTakeRemoteFishActionResult(out received), cancellation.Token);
        Assert(received.Status == FishActionStatus.DryRunValidated && received.OperationId == 0 &&
            received.RequestFingerprint == FishActions.Fingerprint(incoming.Request), "TCP reported native execution for a target probe");
        await guest.StopAsync(); await host.Completion.WaitAsync(cancellation.Token);
    }

    internal static async Task RejectProtocolThree()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener = new LanHost(IPAddress.Loopback, 0);
        PeerIdentity legacy = Identity("Guest"); legacy.ProtocolVersion = 3;
        Task<SessionPeer> accepting = listener.AcceptOneAsync(Identity("Host"), cancellation.Token);
        bool guestRejected = false, hostRejected = false;
        try { using SessionPeer unexpected = await LanGuest.ConnectAsync("127.0.0.1", listener.Port, legacy, cancellation.Token); }
        catch (ProtocolException error) { guestRejected = error.Message.Contains("Protocol version mismatch"); }
        try { using SessionPeer unexpected = await accepting; }
        catch (ProtocolException error) { hostRejected = error.Message.Contains("Protocol version mismatch"); }
        Assert(guestRejected && hostRejected, "protocol 3 silently ignored the action request/result contract");
    }

    internal static void SceneChangeBetweenTakeAndPublication()
    {
        var pair = Pair.Ready();
        FishActionRequest old = Request(1);
        pair.Guest.PublishFishAction(old, 0.1); pair.Pump(0.1);
        Assert(pair.Host.TryTakeRemoteFishAction(out ReceivedFishAction taken), "fixture did not take the admitted request");
        FishActionResult admission = Result(taken.Request, FishActionStatus.Queued);
        FishActionResult decision = Result(taken.Request, FishActionStatus.DryRunValidated);
        // Both CLR decisions predate the real suspend/epoch transition.
        pair.Host.SetLocalScene(null, 0.2);
        Assert(!pair.Host.PublishFishActionResult(admission, 0.2) && !pair.Host.PublishFishActionResult(decision, 0.2),
            "retired take/admission decisions were sent or closed the room");
        FishActionResult spoofed = FishActions.Copy(admission); spoofed.PlayerId = 1;
        Throws<ProtocolException>(() => pair.Host.PublishFishActionResult(spoofed, 0.2));
        pair.Pump(0.2);
        Assert(pair.Host.Snapshot.Phase != SessionPhase.Closed && pair.Guest.Snapshot.Phase != SessionPhase.Closed &&
            !pair.Guest.TryTakeRemoteFishActionResult(out _), "scene cancellation leaked a result or closed the room");
        FishActionRequest canceled = Request(100);
        Assert(!pair.Guest.PublishFishAction(canceled, 0.2), "unavailable guest scene admitted a retired intent");
        spoofed = FishActions.Copy(admission); spoofed.SceneEpoch = 3;
        Throws<ProtocolException>(() => pair.Host.PublishFishActionResult(spoofed, 0.2));
        FishActionRequest spoofedRequest = FishActions.Copy(canceled); spoofedRequest.PlayerId = 1;
        Throws<ProtocolException>(() => pair.Guest.PublishFishAction(spoofedRequest, 0.2));
        pair.EnterReady(0.3);
        Assert(!pair.Guest.PublishFishAction(canceled, 0.3) && !pair.Host.PublishFishActionResult(decision, 0.3),
            "a new Ready epoch revived an old snapshot's intent or probe decision");
        FishActionRequest future = Request(2, 4);
        Throws<ProtocolException>(() => pair.Guest.PublishFishAction(future, 0.3));
        FishActionRequest wrongScene = Request(2, 3); wrongScene.SceneKey = "different";
        Throws<ProtocolException>(() => pair.Guest.PublishFishAction(wrongScene, 0.3));
        FishActionResult wrongResultScene = FishActions.Copy(decision); wrongResultScene.SceneEpoch = 3; wrongResultScene.SceneKey = "different";
        Throws<ProtocolException>(() => pair.Host.PublishFishActionResult(wrongResultScene, 0.3));
        // Canceled ID 100 was never enqueued; it must not consume the publisher
        // high-water or prevent this next real request ID 2.
        Assert(pair.Guest.PublishFishAction(Request(2, 3), 0.4), "retired unsent intent consumed request IDs");
        pair.Pump(0.4);
        Assert(pair.Host.TryTakeRemoteFishAction(out taken) && taken.Request.RequestId == 2, "recovered room could not carry a new intent");
        pair.Host.PublishFishActionResult(Result(taken.Request, FishActionStatus.DryRunValidated), 0.4); pair.Pump(0.4);
        Assert(pair.Guest.TryTakeRemoteFishActionResult(out FishActionResult received) && received.RequestId == 2 &&
            pair.Host.Snapshot.Phase == SessionPhase.Ready && pair.Guest.Snapshot.Phase == SessionPhase.Ready,
            "room did not recover after canceled take/admission publication");
    }

    internal static async Task TcpSceneChangeBetweenTakeAndPublication()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener = new LanHost(IPAddress.Loopback, 0);
        Task<SessionPeer> accepting = listener.AcceptOneAsync(Identity("Host"), cancellation.Token);
        using SessionPeer guest = await LanGuest.ConnectAsync("127.0.0.1", listener.Port, Identity("Guest"), cancellation.Token);
        using SessionPeer host = await accepting;
        var scene = new SceneDescriptor("dive", "layout"); host.SetLocalScene(scene); guest.SetLocalScene(scene);
        await Until(() => host.Snapshot.Phase == SessionPhase.Ready && guest.Snapshot.Phase == SessionPhase.Ready, cancellation.Token);
        FishActionRequest old = Request(1, guest.Snapshot.SceneEpoch);
        guest.PublishFishAction(old);
        ReceivedFishAction taken = null;
        await Until(() => host.TryTakeRemoteFishAction(out taken), cancellation.Token);
        FishActionResult admission = Result(taken.Request, FishActionStatus.Queued);
        FishActionResult decision = Result(taken.Request, FishActionStatus.DryRunValidated);
        host.SetLocalScene(null);
        Assert(!host.PublishFishActionResult(admission) && !host.PublishFishActionResult(decision),
            "SessionPeer treated a stale host decision as a fatal protocol error");
        await Until(() => guest.Snapshot.SceneEpoch > old.SceneEpoch && guest.Snapshot.Phase != SessionPhase.Ready, cancellation.Token);
        Assert(!guest.PublishFishAction(Request(100, old.SceneEpoch)) && host.Snapshot.Phase != SessionPhase.Closed &&
            guest.Snapshot.Phase != SessionPhase.Closed, "SessionPeer treated a stale guest snapshot as a fatal protocol error");
        host.SetLocalScene(scene);
        await Until(() => host.Snapshot.Phase == SessionPhase.Ready && guest.Snapshot.Phase == SessionPhase.Ready, cancellation.Token);
        Assert(!guest.PublishFishAction(Request(100, old.SceneEpoch)), "new Ready scene admitted a stale TCP request");
        Assert(guest.PublishFishAction(Request(2, guest.Snapshot.SceneEpoch)), "same TCP room could not resume new requests");
        await Until(() => host.TryTakeRemoteFishAction(out taken), cancellation.Token);
        host.PublishFishActionResult(Result(taken.Request, FishActionStatus.DryRunValidated));
        FishActionResult received = null;
        await Until(() => guest.TryTakeRemoteFishActionResult(out received), cancellation.Token);
        Assert(received.RequestId == 2 && received.SceneEpoch > old.SceneEpoch && host.Snapshot.RoomId == listener.RoomId &&
            guest.Snapshot.RoomId == listener.RoomId, "scene race forced reconnect or lost the recovered result");
        await guest.StopAsync(); await host.Completion.WaitAsync(cancellation.Token);
    }

    private static FishActionRequest Request(long id, long epoch = 1) => new FishActionRequest
    {
        RequestId = id, PlayerId = 2, SceneEpoch = epoch, SceneKey = "dive", Action = FishActionKind.ProbeTarget, TargetEntityId = 44
    };
    private static FishActionResult Result(FishActionRequest request, FishActionStatus status, FishActionReason reason = FishActionReason.None) => new FishActionResult
    {
        RequestId = request.RequestId, PlayerId = request.PlayerId, SceneEpoch = request.SceneEpoch, SceneKey = request.SceneKey,
        Action = request.Action, TargetEntityId = request.TargetEntityId, Status = status, Reason = reason,
        RequestFingerprint = FishActions.Fingerprint(request)
    };
    private static WirePacket RequestPacket(string room, FishActionRequest request) => new WirePacket
    { Kind = PacketKind.FishActionRequest, Sequence = 1, RoomId = room, ActionRequest = FishActions.Copy(request) };
    private static WirePacket ResultPacket(string room, FishActionResult result) => new WirePacket
    { Kind = PacketKind.FishActionResult, Sequence = 1, RoomId = room, ActionResult = FishActions.Copy(result) };
    private static PlayerFrame Frame(double time) => new PlayerFrame
    { SceneKey = "dive", SampleTime = time, Root = new Pose { Position = Vector3.Zero, Rotation = Quaternion.Identity, Scale = Vector3.One } };
    private static WorldSnapshot World(int count, double time)
    {
        var entities = new EntityState[count];
        for (int i = 0; i < count; i++) entities[i] = new EntityState
        {
            Id = i + 1, Kind = EntityKind.Fish, DataTid = 2010007, Hp = 10, MaxHp = 10,
            Root = new Pose { Position = new Vector3(i, 0, 0), Rotation = Quaternion.Identity, Scale = Vector3.One }
        };
        return new WorldSnapshot { SceneEpoch = 1, SceneKey = "dive", Revision = 1, SampleTime = time, Entities = entities };
    }
    private static PeerIdentity Identity(string name) => new PeerIdentity
    { ModVersion = "0.1.12-dev", SteamBuildId = "25315876", UnityVersion = "6000.0.52f1", Name = name };
    private static async Task Until(Func<bool> condition, CancellationToken cancellation)
    { while (!condition()) await Task.Delay(5, cancellation); }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private sealed class Pair
    {
        public readonly string Room = Guid.NewGuid().ToString("N");
        public readonly SessionMachine Host;
        public readonly SessionMachine Guest;
        private long _sequence;
        public Pair()
        {
            Host = new SessionMachine(SessionRole.Host, new HandshakeResult { RoomId = Room, LocalPlayerId = 1, RemotePlayerId = 2 }, 0);
            Guest = new SessionMachine(SessionRole.Guest, new HandshakeResult { RoomId = Room, LocalPlayerId = 2, RemotePlayerId = 1 }, 0);
        }
        public static Pair Ready() { var pair = new Pair(); pair.EnterReady(0); return pair; }
        public void EnterReady(double now)
        {
            var scene = new SceneDescriptor("dive", "layout"); Host.SetLocalScene(scene, now); Guest.SetLocalScene(scene, now); Pump(now);
            Assert(Host.Snapshot.Phase == SessionPhase.Ready && Guest.Snapshot.Phase == SessionPhase.Ready, "fixture did not commit the scene");
        }
        public void Pump(double now)
        {
            bool progress;
            do
            {
                progress = false;
                while (Host.TryTakePacket(out WirePacket packet)) { packet.Sequence = ++_sequence; Guest.Receive(packet, now); progress = true; }
                while (Guest.TryTakePacket(out WirePacket packet)) { packet.Sequence = ++_sequence; Host.Receive(packet, now); progress = true; }
            } while (progress);
        }
    }
}
