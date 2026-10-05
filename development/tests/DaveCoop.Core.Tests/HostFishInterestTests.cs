using System;
using System.Net;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using DaveCoop.Core;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.Session;
using DaveCoop.Core.World;

internal static class HostFishInterestTests
{
    internal static async Task TcpReceiptCarriesBoundIdentityAndOwnsInterestPosition()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener = new LanHost(IPAddress.Loopback, 0);
        Task<SessionPeer> accepting = listener.AcceptOneAsync(Identity("Host"), cancellation.Token);
        using SessionPeer guest = await LanGuest.ConnectAsync("127.0.0.1", listener.Port, Identity("Guest"), cancellation.Token);
        using SessionPeer host = await accepting;
        await Ready(host, guest, cancellation.Token);
        PlayerFrame source = Frame(guest.Now, new Vector3(2500, -30, 0));
        Assert(guest.PublishFrame(source), "guest frame was not published");
        source.Root = PoseAt(new Vector3(999, 999, 0)); source.SceneKey = "mutated";
        ReceivedFrame receipt = null;
        await Until(() => host.TryTakeRemoteFrame(out receipt), cancellation.Token);
        Assert(receipt.RoomId == host.Snapshot.RoomId && receipt.BoundPlayerId == 2 &&
            receipt.PacketSequence > 0 && receipt.Frame.PlayerId == 2 && receipt.Frame.Root.Position.X == 2500,
            "TCP ingress did not freeze the actual room/player/sequence and owned position");
        var interest = new HostFishInterestBuffer(host.Snapshot.RoomId);
        Assert(interest.TryAccept(receipt, host.Snapshot, host.Now), "real host receipt did not produce an interest");
        long sequence = receipt.PacketSequence;
        receipt.Frame.Root = PoseAt(new Vector3(-999, -999, 0)); receipt.Frame.SceneKey = "mutated-after-take";
        Assert(interest.TryRead(host.Snapshot, host.Now, out HostFishInterest snapshot) &&
            snapshot.Position == new Vector3(2500, -30, 0) && snapshot.PacketSequence == sequence && snapshot.SceneKey == "dive",
            "a consumed frame changed the frozen observation or remote distance was treated as following permission");
        // Reconstruct the consumed receipt's scene solely to test the sequence
        // fence. It cannot turn the original packet into a new observation.
        receipt.Frame.SceneKey = "dive";
        Assert(!interest.TryAccept(receipt, host.Snapshot, host.Now) &&
            interest.TryRead(host.Snapshot, host.Now, out HostFishInterest retained) && ReferenceEquals(snapshot, retained),
            "a replay changed the fixed position or discarded the current valid observation");
        interest.Clear("ManualClear");
        Assert(!interest.TryAccept(receipt, host.Snapshot, host.Now) && !interest.TryRead(host.Snapshot, host.Now, out _),
            "clearing a view reset the packet replay fence");
        Assert(guest.PublishFrame(Frame(guest.Now, new Vector3(-2500, -31, 0))), "new remote center was not published");
        await Until(() => host.TryTakeRemoteFrame(out receipt), cancellation.Token);
        Assert(receipt.PacketSequence > sequence && interest.TryAccept(receipt, host.Snapshot, host.Now) &&
            interest.TryRead(host.Snapshot, host.Now, out retained) && retained.Position.X == -2500,
            "the next actual packet could not replace a cleared observation with a distant center");
        await guest.StopAsync(); await host.Completion.WaitAsync(cancellation.Token);
    }

    internal static async Task TcpTakenReceiptCannotSurvivePauseSameSceneResumeOrClose()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener = new LanHost(IPAddress.Loopback, 0);
        Task<SessionPeer> accepting = listener.AcceptOneAsync(Identity("Host"), cancellation.Token);
        using SessionPeer guest = await LanGuest.ConnectAsync("127.0.0.1", listener.Port, Identity("Guest"), cancellation.Token);
        using SessionPeer host = await accepting;
        await Ready(host, guest, cancellation.Token);
        var interest = new HostFishInterestBuffer(host.Snapshot.RoomId);
        Assert(guest.PublishFrame(Frame(guest.Now, Vector3.One)), "first observation did not publish");
        ReceivedFrame taken = null;
        await Until(() => host.TryTakeRemoteFrame(out taken), cancellation.Token);
        Assert(interest.TryAccept(taken, host.Snapshot, host.Now), "initial observation rejected");
        long epoch = taken.Frame.SceneEpoch;
        guest.SetLocalScene(null);
        await Until(() => host.Snapshot.Phase != SessionPhase.Ready, cancellation.Token);
        Assert(!interest.TryRead(host.Snapshot, host.Now, out _) &&
            !interest.TryAccept(taken, host.Snapshot, host.Now) && !host.TryTakeRemoteFrame(out _),
            "taking a receipt before scene pause allowed stale native-area work afterward");
        guest.SetLocalScene(new SceneDescriptor("dive", "same-layout"));
        await Until(() => host.Snapshot.Phase == SessionPhase.Ready && guest.Snapshot.Phase == SessionPhase.Ready, cancellation.Token);
        Assert(host.Snapshot.SceneEpoch > epoch && !interest.TryAccept(taken, host.Snapshot, host.Now),
            "resuming the same scene name reused the old observation");
        Assert(guest.PublishFrame(Frame(guest.Now, new Vector3(45, -20, 0))), "resumed player could not publish");
        ReceivedFrame current = null;
        await Until(() => host.TryTakeRemoteFrame(out current), cancellation.Token);
        Assert(interest.TryAccept(current, host.Snapshot, host.Now), "new epoch was rejected");
        await guest.StopAsync(); await host.Completion.WaitAsync(cancellation.Token);
        Assert(!interest.TryRead(host.Snapshot, host.Now, out _) && !interest.TryAccept(current, host.Snapshot, host.Now),
            "closed peer retained a usable area observation");
    }

    internal static async Task TcpDifferentRoomAndGuestRoleCannotBorrowHostInterest()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listenerA = new LanHost(IPAddress.Loopback, 0);
        Task<SessionPeer> acceptA = listenerA.AcceptOneAsync(Identity("HostA"), cancellation.Token);
        using SessionPeer guestA = await LanGuest.ConnectAsync("127.0.0.1", listenerA.Port, Identity("GuestA"), cancellation.Token);
        using SessionPeer hostA = await acceptA;
        using var listenerB = new LanHost(IPAddress.Loopback, 0);
        Task<SessionPeer> acceptB = listenerB.AcceptOneAsync(Identity("HostB"), cancellation.Token);
        using SessionPeer guestB = await LanGuest.ConnectAsync("127.0.0.1", listenerB.Port, Identity("GuestB"), cancellation.Token);
        using SessionPeer hostB = await acceptB;
        await Ready(hostA, guestA, cancellation.Token); await Ready(hostB, guestB, cancellation.Token);
        var interest = new HostFishInterestBuffer(hostA.Snapshot.RoomId);
        Assert(guestA.PublishFrame(Frame(guestA.Now, Vector3.UnitX)) && guestB.PublishFrame(Frame(guestB.Now, Vector3.UnitY)),
            "both real peers must publish");
        ReceivedFrame a = null, b = null;
        await Until(() => hostA.TryTakeRemoteFrame(out a), cancellation.Token);
        await Until(() => hostB.TryTakeRemoteFrame(out b), cancellation.Token);
        Assert(a.RoomId != b.RoomId && interest.TryAccept(a, hostA.Snapshot, hostA.Now), "first room did not bind");
        Assert(!interest.TryAccept(b, hostB.Snapshot, hostB.Now) && !interest.TryRead(hostA.Snapshot, hostA.Now, out _),
            "same scene names and player numbers allowed another room to borrow the host buffer");
        Assert(hostA.PublishFrame(Frame(hostA.Now, new Vector3(5, 0, 0))), "host display frame did not publish");
        ReceivedFrame hostFrame = null;
        await Until(() => guestA.TryTakeRemoteFrame(out hostFrame), cancellation.Token);
        var guestBuffer = new HostFishInterestBuffer(guestA.Snapshot.RoomId);
        Assert(hostFrame.BoundPlayerId == 1 && !guestBuffer.TryAccept(hostFrame, guestA.Snapshot, guestA.Now),
            "a guest receiving host display frames obtained host-area observations");
        await guestA.StopAsync(); await hostA.Completion.WaitAsync(cancellation.Token);
        await guestB.StopAsync(); await hostB.Completion.WaitAsync(cancellation.Token);
    }

    internal static void ReceiptClockAndSequenceBoundTheLatestObservation()
    {
        string room = Guid.NewGuid().ToString("N");
        SessionSnapshot state = HostState(room, 1);
        var interest = new HostFishInterestBuffer(room);
        ReceivedFrame receipt = Receipt(room, 20, 10, 90000000, new Vector3(100000, -100000, 0));
        Assert(interest.TryAccept(receipt, state, 10) && interest.TryRead(state, 11, out HostFishInterest observed) &&
            observed.RemoteSampleTime == 90000000 && observed.Position.X == 100000,
            "remote process clock or distance was incorrectly used as actor/movement evidence");
        Assert(!interest.TryRead(state, 11.001, out _) && interest.HighestPacketSequence == 20,
            "remote sample time kept a stale host receipt alive or expiry erased its sequence fence");
        Assert(!interest.TryAccept(receipt, state, 10) && !interest.TryRead(state, 10, out _), "expired receipt replay revived a region");
        ReceivedFrame next = Receipt(room, 21, 12, 90000001, new Vector3(-100000, 100000, 0));
        Assert(!interest.TryAccept(next, state, 11.9), "future host receipt time was accepted");
        Assert(interest.TryAccept(next, state, 12), "current receipt was rejected after a pre-accept clock failure");
        Assert(!interest.TryRead(state, double.NaN, out _) && !interest.TryRead(state, 12, out _),
            "invalid read clock retained an active view");
    }

    internal static void InvalidIdentityPositionAndSceneNeverBecomeAnInterest()
    {
        string room = Guid.NewGuid().ToString("N");
        SessionSnapshot state = HostState(room, 3);
        var interest = new HostFishInterestBuffer(room);
        ReceivedFrame receipt = Receipt(room, 1, 2, 2, Vector3.One); receipt.Frame.SceneEpoch = 3;
        receipt.BoundPlayerId = 1;
        Assert(!interest.TryAccept(receipt, state, 2), "bound-player mismatch was accepted");
        receipt.BoundPlayerId = 2; receipt.Frame.Root = PoseAt(new Vector3(float.NaN, 0, 0));
        Assert(!interest.TryAccept(receipt, state, 2), "nonfinite position was accepted");
        receipt.Frame.Root = PoseAt(new Vector3(100001, 0, 0));
        Assert(!interest.TryAccept(receipt, state, 2), "position exceeded the existing wire bounds");
        receipt.Frame.Root = PoseAt(Vector3.One);
        Assert(interest.TryAccept(receipt, state, 2), "failed validation consumed a valid packet sequence");
        state.SceneEpoch++;
        Assert(!interest.TryRead(state, 2, out _), "new epoch retained the old position");
        state.SceneEpoch--; receipt.PacketSequence++;
        Assert(interest.TryAccept(receipt, state, 2), "new actual sequence was rejected");
        state.SceneKey = "other";
        Assert(!interest.TryRead(state, 2, out _) && !interest.TryAccept(receipt, state, 2),
            "same epoch but changed scene retained or admitted the prior source");
    }

    internal static void HostBodyWinsOverConflictingPoseWithoutForgingAReceipt()
    {
        string room = Guid.NewGuid().ToString("N"), member = Guid.NewGuid().ToString("N");
        SessionSnapshot state = BodyState(room);
        var body = BodyBuffer(room);
        ReceivedFrame pose = Receipt(room, 100, 10, 90000000, new Vector3(2500, -30, 0));
        Assert(!body.TryAccept(pose, state, 10) && body.HighestPacketSequence == 0,
            "body mode consumed a remote pose before an actual body sample");
        HostFishBodySample sample = BodySample(state, member, 1, 10, new Vector3(8, -2, 0));
        HostFishInterest interest = null;
        Assert(body.TryAcceptHostBody(sample, state, 10) && body.TryRead(state, 10, out interest) &&
            interest.SourceKind == HostFishInterestKind.HostEmployeeBody && interest.Position == sample.Position &&
            interest.MemberId == member && interest.ActorRevision == 7 && interest.HostSampleRevision == 1 &&
            interest.HostSampledAt == 10 && interest.BoundPlayerId == 2 && interest.PacketSequence == 0 &&
            interest.ReceivedAt == 0 && interest.RemoteSampleTime == 0,
            "host readback was replaced by a client pose or disguised as packet/remote-clock evidence");
        Assert(!body.TryAccept(pose, state, 10) && body.TryRead(state, 10, out HostFishInterest retained) &&
            ReferenceEquals(interest, retained), "conflicting remote pose overwrote the valid body interest");
        var legacy = new HostFishInterestBuffer(room);
        Assert(!legacy.TryAcceptHostBody(sample, state, 10) && legacy.TryAccept(pose, state, 10) &&
            legacy.TryRead(state, 10, out HostFishInterest observed) && observed.SourceKind == HostFishInterestKind.RemoteObservation &&
            observed.Position == pose.Frame.Root.Position && observed.MemberId == null && observed.ActorRevision == 0 &&
            observed.HostSampleRevision == 0 && observed.HostSampledAt == 0 && observed.PacketSequence == 100,
            "legacy mode switched to body data or lost the original receipt identity");
    }

    internal static void BodyModeRequiresNegotiatedHostActorAndNeverFallsBack()
    {
        string room = Guid.NewGuid().ToString("N"), member = Guid.NewGuid().ToString("N");
        var body = BodyBuffer(room);
        long sequence = 0;
        foreach (Action<SessionSnapshot> invalidate in new Action<SessionSnapshot>[]
        {
            s => s.LocalUsesCrewActor = false, s => s.RemoteUsesCrewActor = false,
            s => s.Role = SessionRole.Guest, s => s.LocalPlayerId = 2,
            s => s.RemotePlayerId = 1, s => s.CrewActorRevision = 0,
            s => s.Phase = SessionPhase.Closed
        })
        {
            SessionSnapshot state = BodyState(room);
            Assert(body.TryAcceptHostBody(BodySample(state, member, ++sequence, 10, Vector3.Zero), state, 10),
                "valid host body could not refresh between rejected sessions");
            invalidate(state);
            Assert(!body.TryRead(state, 10, out _) &&
                !body.TryAcceptHostBody(BodySample(state, member, sequence + 1, 10, Vector3.One), state, 10) &&
                !body.TryAccept(Receipt(room, sequence, 10, 10, Vector3.One), BodyState(room), 10) &&
                !body.TryRead(BodyState(room), 10, out _),
                "a missing host/crew/actor binding retained a view or fell back to a remote frame");
        }
        bool invalidKindRejected = false;
        try { _ = new HostFishInterestBuffer(room, sourceKind: (HostFishInterestKind)2); }
        catch (ArgumentOutOfRangeException) { invalidKindRejected = true; }
        Assert(invalidKindRejected, "an unknown source mode was silently accepted");
    }

    internal static void BodyPauseSceneRoomAndActorLossRevokeTheCurrentView()
    {
        string room = Guid.NewGuid().ToString("N"), member = Guid.NewGuid().ToString("N");
        SessionSnapshot state = BodyState(room);
        var body = BodyBuffer(room);
        HostFishBodySample first = BodySample(state, member, 1, 10, Vector3.Zero);
        Assert(body.TryAcceptHostBody(first, state, 10), "initial body missing");
        state.Phase = SessionPhase.WaitingForScene;
        Assert(!body.TryRead(state, 10, out _), "pause retained the body interest");
        state.Phase = SessionPhase.Ready;
        Assert(!body.TryAcceptHostBody(first, state, 10) &&
            body.TryAcceptHostBody(BodySample(state, member, 2, 10, Vector3.One), state, 10),
            "pause reset the sample fence or blocked a genuinely new sample");
        state.SceneEpoch++;
        Assert(!body.TryRead(state, 10, out _) && !body.TryAcceptHostBody(first, state, 10),
            "new epoch reused the old body's scene sample");
        state.SceneKey = "deeper"; state.CrewActorRevision = 8;
        Assert(body.TryAcceptHostBody(BodySample(state, member, 3, 10, Vector3.UnitY), state, 10), "new scene/actor rejected");
        state.RoomId = Guid.NewGuid().ToString("N");
        Assert(!body.TryRead(state, 10, out _), "same scene in a different room borrowed the old source");
        state.RoomId = room;
        Assert(body.TryAcceptHostBody(BodySample(state, member, 4, 10, Vector3.UnitZ), state, 10), "same-room fresh sample rejected");
        state.CrewActorRevision = 0;
        Assert(!body.TryRead(state, 10, out _), "missing actor retained a view");
        state.CrewActorRevision = 9;
        Assert(body.TryAcceptHostBody(BodySample(state, member, 5, 10, Vector3.Zero), state, 10), "replacement actor rejected");
        state.CrewActorRevision = 10;
        Assert(!body.TryRead(state, 10, out _), "current session's replacement actor reused the earlier actor interest");
    }

    internal static void BodyActorSampleAndMemberFencesSurviveClear()
    {
        string room = Guid.NewGuid().ToString("N"), member = Guid.NewGuid().ToString("N");
        SessionSnapshot state = BodyState(room);
        var body = BodyBuffer(room);
        HostFishBodySample first = BodySample(state, member, 20, 10, Vector3.One);
        Assert(body.TryAcceptHostBody(first, state, 10), "initial fenced body rejected");
        body.Clear();
        Assert(body.HighestActorRevision == 7 && body.HighestBodySampleRevision == 20 &&
            !body.TryAcceptHostBody(first, state, 10), "clear erased either host replay fence");
        state.CrewActorRevision = 8;
        Assert(!body.TryAcceptHostBody(BodySample(state, member, 20, 10, Vector3.Zero), state, 10),
            "a newer actor reset the globally monotonic body sample sequence");
        state.CrewActorRevision = 6;
        Assert(!body.TryAcceptHostBody(BodySample(state, member, 21, 10, Vector3.Zero), state, 10),
            "a newer sample revived an older actor");
        state.CrewActorRevision = 8;
        Assert(body.TryAcceptHostBody(BodySample(state, member, 21, 10, Vector3.Zero), state, 10), "new actor/sample rejected");
        body.Clear("SourceUnavailable");
        Assert(!body.TryAcceptHostBody(BodySample(state, Guid.NewGuid().ToString("N"), 22, 10, Vector3.Zero), state, 10) &&
            !body.TryAcceptHostBody(BodySample(state, "11111111-1111-1111-1111-111111111111", 22, 10, Vector3.Zero), state, 10) &&
            body.HighestBodySampleRevision == 21 && body.TryAcceptHostBody(BodySample(state, member, 22, 10, Vector3.Zero), state, 10),
            "clear allowed a foreign/noncanonical member or invalid admission consumed the sample revision");
    }

    internal static void BodyHostClockAndFinitePositionsBoundFreshness()
    {
        string room = Guid.NewGuid().ToString("N"), member = Guid.NewGuid().ToString("N");
        SessionSnapshot state = BodyState(room);
        var body = new HostFishInterestBuffer(room, 5, HostFishInterestKind.HostEmployeeBody);
        Assert(body.StaleSeconds == 1 && body.TryAcceptHostBody(BodySample(state, member, 1, 10, Vector3.Zero), state, 10) &&
            body.TryRead(state, 11, out _) && !body.TryRead(state, 11.001, out _) && body.HighestBodySampleRevision == 1,
            "host-body TTL exceeded one second or expiry reset its sample fence");
        HostFishBodySample future = BodySample(state, member, 2, 12, Vector3.One);
        Assert(!body.TryAcceptHostBody(future, state, 11.9) && body.TryAcceptHostBody(future, state, 12),
            "future host time passed or its pre-admission rejection consumed the sequence");
        Assert(!body.TryAcceptHostBody(BodySample(state, member, 3, 11.99, Vector3.One), state, 12.5) &&
            body.TryAcceptHostBody(BodySample(state, member, 3, 12.5, Vector3.One), state, 12.5),
            "a newer sequence moved the host sample clock backward or valid retry failed");
        foreach (Vector3 bad in new[] { new Vector3(float.NaN, 0, 0), new Vector3(0, float.PositiveInfinity, 0), new Vector3(0, 0, float.NegativeInfinity) })
            Assert(!body.TryAcceptHostBody(BodySample(state, member, 4, 13, bad), state, 13), "nonfinite host body position admitted");
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, -1 })
            Assert(!body.TryAcceptHostBody(BodySample(state, member, 4, bad, Vector3.Zero), state, 13), "invalid host clock admitted");
        Assert(body.HighestBodySampleRevision == 3 && body.TryAcceptHostBody(BodySample(state, member, 4, 13, Vector3.Zero), state, 13) &&
            !body.TryRead(state, double.NaN, out _) && !body.TryRead(state, 13, out _),
            "invalid samples consumed the sequence or an invalid read clock retained the active view");
    }

    internal static void BodyAtRestAndOwnedSamplesDoNotDependOnGuestInput()
    {
        string room = Guid.NewGuid().ToString("N"), member = Guid.NewGuid().ToString("N");
        SessionSnapshot state = BodyState(room); state.CrewInputSequence = 0;
        var body = BodyBuffer(room);
        Vector3 original = Vector3.Zero;
        HostFishBodySample sample = BodySample(state, member, 1, 0, original);
        original = new Vector3(999, 999, 999);
        HostFishInterest frozen = null;
        Assert(body.TryAcceptHostBody(sample, state, 0) && body.TryRead(state, 0, out frozen) &&
            frozen.Position == Vector3.Zero && sample.Position == Vector3.Zero && frozen.HostSampledAt == 0 &&
            state.CrewInputSequence == 0 && original.X == 999, "body at rest was mistaken for a missing guest input/pose");
        Vector3 returnedPosition = frozen.Position; returnedPosition.X = 25;
        Assert(returnedPosition.X == 25 && sample.Position == Vector3.Zero && frozen.Position == Vector3.Zero,
            "a returned vector mutated the owned sample");
        state.RoomId = Guid.NewGuid().ToString("N"); state.SceneKey = "mutated"; state.CrewActorRevision = 99;
        Assert(frozen.RoomId == room && frozen.SceneKey == "dive" && frozen.ActorRevision == 7 && frozen.MemberId == member &&
            sample.RoomId == room && sample.SceneKey == "dive" && sample.ActorRevision == 7 &&
            !body.TryRead(state, 0, out _), "later session mutation changed historical owned fields or kept them current");
    }

    private static HostFishInterestBuffer BodyBuffer(string room) => new HostFishInterestBuffer(room,
        sourceKind: HostFishInterestKind.HostEmployeeBody);
    private static SessionSnapshot BodyState(string room)
    {
        SessionSnapshot state = HostState(room, 1);
        state.LocalUsesCrewActor = true; state.RemoteUsesCrewActor = true; state.CrewActorRevision = 7;
        return state;
    }
    private static HostFishBodySample BodySample(SessionSnapshot state, string member, long sequence, double at, Vector3 position) =>
        new HostFishBodySample(state.RoomId, member, state.CrewActorRevision, sequence, state.SceneEpoch, state.SceneKey, at, position);

    private static PeerIdentity Identity(string name) => new PeerIdentity
    { ModVersion = "host-interest-tests", SteamBuildId = "25315876", UnityVersion = "6000.0.52f1", Name = name };
    private static PlayerFrame Frame(double sample, Vector3 position) => new PlayerFrame
    { PlayerId = 2, SceneEpoch = 1, SceneKey = "dive", SampleTime = sample, Root = PoseAt(position) };
    private static Pose PoseAt(Vector3 position) => new Pose
    { Position = position, Rotation = Quaternion.Identity, Scale = Vector3.One };
    private static ReceivedFrame Receipt(string room, long sequence, double receivedAt, double sample, Vector3 position) => new ReceivedFrame
    { RoomId = room, BoundPlayerId = 2, PacketSequence = sequence, ReceivedAt = receivedAt, LocalSampleTime = -12345, Frame = Frame(sample, position) };
    private static SessionSnapshot HostState(string room, long epoch) => new SessionSnapshot
    { RoomId = room, Role = SessionRole.Host, Phase = SessionPhase.Ready, LocalPlayerId = 1, RemotePlayerId = 2, SceneEpoch = epoch, SceneKey = "dive" };
    private static async Task Ready(SessionPeer host, SessionPeer guest, CancellationToken cancellation)
    {
        host.SetLocalScene(new SceneDescriptor("dive", "same-layout")); guest.SetLocalScene(new SceneDescriptor("dive", "same-layout"));
        await Until(() => host.Snapshot.Phase == SessionPhase.Ready && guest.Snapshot.Phase == SessionPhase.Ready, cancellation);
    }
    private static async Task Until(Func<bool> ready, CancellationToken cancellation)
    { while (!ready()) await Task.Delay(5, cancellation); }
    private static void Assert(bool condition, string reason)
    { if (!condition) throw new InvalidOperationException(reason); }
}
