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
