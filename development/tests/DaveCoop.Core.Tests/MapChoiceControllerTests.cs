using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.Session;
using DaveCoop.Core.Transport;
using DaveCoop.Core.World;
using DaveCoop.Networking;

// Actual production controller and real loopback TCP; only logging is replaced.
// Origin inputs are synthetic CLR evidence, never native ABI/adoption proof.
internal static class MapChoiceControllerTests
{
    public static async Task TcpCallbackFloorAndRoomIsolation()
    {
        using var first = await TcpPair.Open();
        var source = new MapChoiceController(); var received = new MapChoiceController();
        Guid run = Guid.NewGuid();
        source.BindRoom(first.Host, 100, run, 10); received.BindRoom(first.Guest, 100);
        source.Observe(RouteObservation(100), first.Host);
        source.Observe(ChoiceObservation(101, "pre-route"), first.Host);
        source.Observe(RouteObservation(102), first.Host);
        source.ObserveOrigin(Frame(run, 10), first.Host);
        Assert(source.PublishedRoutes == 0 && source.SuppressedLegacyObservations == 3 && source.UnboundChoices == 1 &&
            first.Host.Snapshot.MapChoiceGeneration == 0, "legacy callbacks or the room origin floor published an old source");
        source.ObserveOrigin(Frame(run, 11), first.Host);
        await Remote(received, first, () => received.RemoteGeneration == 1 && received.RemoteChoiceCount == 1);
        source.Retire(first.Host, 110, "observer closed");
        source.Observe(RouteObservation(109), first.Host);
        source.Observe(ChoiceObservation(110, "queued before close"), first.Host);
        source.ObserveOrigin(Frame(run, 11), first.Host);
        await Remote(received, first, () => received.RemoteRouteSceneCount == 0);
        Assert(source.PublishedRoutes == 1 && source.CallbackFloor == 110 && first.Host.Snapshot.MapChoiceFingerprint == null,
            "closed legacy or origin identities restarted the retired generation");
        source.Clear(120); received.Clear(120);
        using var second = await TcpPair.Open();
        source.BindRoom(second.Host, 125, run, 11); received.BindRoom(second.Guest, 125);
        source.Observe(RouteObservation(124), second.Host);
        source.Observe(ChoiceObservation(125, "old room"), second.Host);
        source.ObserveOrigin(Frame(run, 12), first.Host);
        source.ObserveOrigin(Frame(run, 11), second.Host);
        Assert(source.PublishedRoutes == 1 && second.Host.Snapshot.MapChoiceGeneration == 0, "old room peer or owner crossed the room floor");
        source.Observe(RouteObservation(127), second.Host);
        source.ObserveOrigin(Frame(run, 12), second.Host);
        await Remote(received, second, () => received.RemoteGeneration == 1 && received.RemoteChoiceCount == 1);
        Assert(source.PublishedRoutes == 2 && source.PublishedChoices == 2 && source.CallbackFloor == 125 &&
            source.HighestCallback == 127 && source.DroppedObservations >= 5 && first.Host.Snapshot.RoomId != second.Host.Snapshot.RoomId,
            "room reset lost fences/counters or blocked a new fixed-origin entry");
    }

    public static async Task TcpNaturalGenerationsAndChoiceRevisions()
    {
        using var pair = await TcpPair.Open();
        var source = new MapChoiceController(); var received = new MapChoiceController(); Guid run = Guid.NewGuid();
        source.BindRoom(pair.Host, 0); received.BindRoom(pair.Guest, 0);
        source.Observe(ChoiceObservation(1, "before-route"), pair.Host);
        var factory = ChoiceObservation(2, "factory"); factory.Stage = "IgpPrefabFactoryBefore";
        source.Observe(factory, pair.Host);
        source.ObserveOrigin(Frame(run, 1, 9, 0), pair.Host);
        await Remote(received, pair, () => received.RemoteGeneration == 1 && received.RemoteRouteSceneCount == 9);
        string fingerprint = pair.Host.Snapshot.MapChoiceFingerprint;
        source.ObserveOrigin(Frame(run, 1, 9, 0), pair.Host);
        Assert(source.PublishedRoutes == 1 && source.UnboundChoices == 2 && received.RemoteChoiceCount == 0, "legacy result/factory or identical source created another generation");
        source.ObserveOrigin(Frame(run, 1, 9), pair.Host);
        source.ObserveOrigin(Frame(run, 1, 9), pair.Host);
        Assert(source.PublishedChoices == 1 && pair.Host.Snapshot.MapChoiceRevision == 1, "identical source consumed a wire revision");
        var update = Frame(run, 1, 9); update.Source.Choices[0].CallbackSequence = 2; update.Source.Choices[0].Choice.SelectedPrefabName = "IGP_B";
        source.ObserveOrigin(update, pair.Host);
        await Remote(received, pair, () => received.RemoteChoiceCount == 1 && pair.Guest.Snapshot.MapChoiceRevision == 2);
        source.ObserveOrigin(Frame(run, 2, 9, 0), pair.Host);
        await Remote(received, pair, () => received.RemoteGeneration == 2 && received.RemoteChoiceCount == 0);
        Assert(pair.Host.Snapshot.MapChoiceFingerprint == fingerprint && pair.Host.Snapshot.MapChoiceRevision == 0, "same-fingerprint new entry reused old controller identity");
        source.Observe(ChoiceObservation(7, "late same address"), pair.Host);
        source.ObserveOrigin(Frame(run, 1, 9), pair.Host);
        source.ObserveOrigin(Frame(run, 2, 9), pair.Host);
        await Remote(received, pair, () => received.RemoteGeneration == 2 && received.RemoteChoiceCount == 1);
        source.ObserveOrigin(Frame(run, 3, 9, 0), pair.Host);
        await Remote(received, pair, () => received.RemoteGeneration == 3 && received.RemoteChoiceCount == 0);
        Assert(received.TryCaptureRemoteChoices(pair.Guest, out MapChoiceSnapshot candidate) &&
            candidate.Generation == 3 && candidate.Route != null && candidate.Choices.Length == 0 &&
            candidate.ObservationOnly && !candidate.HostSelectionApplied,
            "fixed-origin candidates changed their observation-only adoption contract");
        Assert(source.PublishedRoutes == 3 && source.PublishedChoices == 3 && source.SuppressedLegacyObservations == 3 &&
            pair.Host.Snapshot.Phase == SessionPhase.WaitingForScene && pair.Guest.Snapshot.SceneEpoch == 0,
            "fixed-origin candidates elevated readiness or admitted late legacy evidence");
    }

    public static async Task TcpUnavailableSamplesRetireAndRecover()
    {
        using var pair = await TcpPair.Open();
        var source = new MapChoiceController(); var received = new MapChoiceController(); Guid run = Guid.NewGuid();
        source.BindRoom(pair.Host, 0); received.BindRoom(pair.Guest, 0);
        for (int fault = 0; fault < 5; fault++)
        {
            long owner = fault + 1;
            source.ObserveOrigin(Frame(run, owner), pair.Host);
            await Remote(received, pair, () => received.RemoteGeneration == owner && received.RemoteChoiceCount == 1);
            var bad = Frame(run, owner);
            if (fault == 0) bad.Source.Choices[0].CallbackSequence = 0;
            if (fault == 1) bad.Source.RouteFingerprint = "map-route-v1/" + new string('0', 64);
            if (fault == 2) { bad.Source.Route = null; bad.Source.RouteFingerprint = null; bad.Source.Choices = Array.Empty<MapOriginChoiceEvidence>(); }
            if (fault == 3) bad.Source.Choices[0].OperationLife = 0;
            if (fault == 4) bad.Source.Choices = null;
            source.ObserveOrigin(bad, pair.Host);
            Assert(source.SourceGeneration == 0 && pair.Host.Snapshot.MapChoiceFingerprint == null, "unusable source retained a wire candidate");
            await Remote(received, pair, () => received.RemoteGeneration == owner && received.RemoteRouteSceneCount == 0);
            source.ObserveOrigin(Frame(run, owner), pair.Host);
            source.Observe(ChoiceObservation(100 + fault, "legacy recovery"), pair.Host);
            Assert(pair.Host.Snapshot.MapChoiceFingerprint == null, "old owner or legacy result restarted a retired source");
        }
        source.ObserveOrigin(Frame(run, 6), pair.Host);
        await Remote(received, pair, () => received.RemoteGeneration == 6 && received.RemoteChoiceCount == 1);
        Assert(source.PublishedRoutes == 6 && source.PublishedChoices == 6 && source.PublicationErrors == 0 && pair.Host.Snapshot.Phase != SessionPhase.Closed,
            "safe retirement prevented a new natural entry from recovering in the same room");
    }

    public static async Task TcpGuestObserverStopRetainsHostEvidence()
    {
        using var pair = await TcpPair.Open();
        var source = new MapChoiceController(); var received = new MapChoiceController(); Guid run = Guid.NewGuid();
        source.BindRoom(pair.Host, 0); received.BindRoom(pair.Guest, 0);
        source.ObserveOrigin(Frame(run, 1), pair.Host);
        await Remote(received, pair, () => received.RemoteGeneration == 1 && received.RemoteChoiceCount == 1);
        string fingerprint = pair.Host.Snapshot.MapChoiceFingerprint;
        received.Retire(pair.Guest, 50, "guest local observer disabled");
        received.Observe(RouteObservation(51), pair.Guest);
        received.ObserveOrigin(new MapOriginSourceFrame { RunId = run, Healthy = false }, pair.Guest);
        Assert(received.RemoteChoiceCount == 1 && received.RemoteRouteSceneCount == 3 && pair.Guest.Snapshot.MapChoiceFingerprint == fingerprint &&
            pair.Host.Snapshot.MapChoiceFingerprint == fingerprint && received.PublishedRoutes == 0 && received.Status.Contains("local observer stopped"),
            "guest observer stop retracted host evidence");
        source.Retire(pair.Host, 10, "host observer disabled");
        await Remote(received, pair, () => received.RemoteRouteSceneCount == 0);
        source.ObserveOrigin(Frame(run, 2), pair.Host);
        await Remote(received, pair, () => received.RemoteGeneration == 2 && received.RemoteChoiceCount == 1);
        Assert(received.CallbackFloor == 50 && received.HighestCallback == 51, "guest legacy floor blocked remote host recovery");
        received.Clear(60);
        Assert(received.RemoteGeneration == 0 && received.CallbackFloor == 60, "clear retained received evidence or lost the callback fence");
        received.BindRoom(pair.Guest, 60);
        source.ObserveOrigin(Frame(run, 3, 3, 0), pair.Host);
        await Remote(received, pair, () => received.RemoteGeneration == 3 && received.RemoteRouteSceneCount == 3 && received.RemoteChoiceCount == 0);
        var frame = Frame(run, 3);
        Assert(frame.ObservationOnly && !frame.NativeGenerationBound && !frame.HostSelectionApplied && !frame.WorldAuthority && !frame.CargoAuthority &&
            !frame.Source.Choices[0].NativePermission && source.PublicationErrors == 0, "synthetic source asserted native permission");
        foreach (string trace in NetworkDriver.Logger.Snapshot())
            Assert(trace.Contains("\"CandidateEvidence\":true") && trace.Contains("\"NativeAdoptionImplemented\":false") && trace.Contains("\"HostSelectionApplied\":false"),
                "adapter trace implied native adoption");
    }

    public static async Task TcpOwnedConsumerRefreshesAndCopiesCurrentChoices()
    {
        using var pair = await TcpPair.Open();
        using var other = await TcpPair.Open();
        var source = new MapChoiceController(); var received = new MapChoiceController(); Guid run = Guid.NewGuid();
        source.BindRoom(pair.Host, 0); received.BindRoom(pair.Guest, 0);
        source.ObserveOrigin(Frame(run, 1), pair.Host);
        MapChoiceSnapshot first = null;
        await Until(() => received.TryCaptureRemoteChoices(pair.Guest, out first) && first.Route != null && first.LastChoiceRevision == 1,
            pair.Cancellation);
        Assert(first.Generation == pair.Guest.Snapshot.MapChoiceGeneration && first.RouteFingerprint == pair.Guest.Snapshot.MapChoiceFingerprint &&
            first.ObservationOnly && !first.HostSelectionApplied && pair.Guest.Snapshot.Phase == SessionPhase.WaitingForScene,
            "callback consumption elevated readiness or returned a different live generation");
        MapChoiceSnapshot original = null;
        Assert(!received.TryCaptureRemoteChoices(pair.Host, out _) && !received.TryCaptureRemoteChoices(other.Guest, out _) &&
            received.TryCaptureRemoteChoices(pair.Guest, out original), "host or another room became the bound guest consumer");
        string fingerprint = original.RouteFingerprint;
        first.Route.Scenes[0].SceneName = "caller changed scene";
        first.Choices[0].SelectedPrefabName = "caller changed choice";
        first.Choices[0] = null;
        first.Generation = 99; first.Retired = true;
        Assert(received.TryCaptureRemoteChoices(pair.Guest, out MapChoiceSnapshot copied) && copied.Generation == 1 && !copied.Retired &&
            copied.Route.Scenes[0].SceneName == "dive-0" && copied.Choices[0].SelectedPrefabName == "IGP_A0" &&
            !ReferenceEquals(copied.Route, original.Route) && !ReferenceEquals(copied.Route.Scenes[0], original.Route.Scenes[0]) &&
            !ReferenceEquals(copied.Choices[0], original.Choices[0]), "consumer exposed controller-owned mutable route or choice objects");

        var update = Frame(run, 1); update.Source.Choices[0].CallbackSequence = 2;
        update.Source.Choices[0].Choice.SelectedPrefabName = "IGP_B";
        source.ObserveOrigin(update, pair.Host);
        // Wait only for the actual network session, never the controller Update.
        await Until(() => pair.Guest.Snapshot.MapChoiceRevision == 2, pair.Cancellation);
        Assert(received.TryCaptureRemoteChoices(pair.Guest, out MapChoiceSnapshot latest) && latest.LastChoiceRevision == 2 &&
            latest.Choices[0].SelectedPrefabName == "IGP_B" && original.Choices[0].SelectedPrefabName == "IGP_A0" &&
            latest.RouteFingerprint == fingerprint, "natural callback reused historical drain state or rewrote an earlier owned snapshot");
        received.Retire(pair.Guest, 20, "guest local observer disabled");
        Assert(received.TryCaptureRemoteChoices(pair.Guest, out latest) && latest.LastChoiceRevision == 2,
            "guest local observer stop erased current host candidate evidence");
        source.Retire(pair.Host, 20, "host origin stopped");
        await Until(() => pair.Guest.Snapshot.MapChoiceFingerprint == null, pair.Cancellation);
        Assert(received.TryCaptureRemoteChoices(pair.Guest, out MapChoiceSnapshot retired) && retired.Retired && retired.Route == null &&
            retired.Choices.Length == 0 && retired.LastChoiceRevision == 2 && received.RemoteRouteSceneCount == 0 &&
            !original.Retired && original.Route != null, "host retirement retained usable cached evidence or mutated historical snapshots");
    }

    public static async Task TcpOwnedConsumerRevokesPartialRoutesAndClearedRooms()
    {
        // The host sends real framed packets one slice at a time so the partial
        // route interval is deterministic, rather than relying on TCP timing.
        using var pair = await ManualMapPair.Open();
        var received = new MapChoiceController(); received.BindRoom(pair.Guest, 0);
        foreach (MapRouteSlice slice in MapChoiceFrames.SplitRoute(Route(), 1)) await pair.Send(slice);
        MapChoiceSnapshot old = null;
        await Until(() => received.TryCaptureRemoteChoices(pair.Guest, out old) && old.Route != null, pair.Cancellation);
        Assert(old.Generation == 1 && old.Choices.Length == 0 && !old.HostSelectionApplied,
            "complete route was treated as complete IGP inventory or adoption");

        MapRouteSlice[] replacement = MapChoiceFrames.SplitRoute(Route(9), 2);
        await pair.Send(replacement[0]);
        await Until(() => pair.Guest.Snapshot.MapChoiceGeneration == 2, pair.Cancellation);
        Assert(received.TryCaptureRemoteChoices(pair.Guest, out MapChoiceSnapshot pending) && pending.Generation == 2 && pending.Route == null &&
            pending.Choices.Length == 0 && received.RemoteRouteSceneCount == 0 && old.Route.Scenes.Length == 3,
            "first replacement slice exposed a previous usable route");
        await pair.Send(replacement[1]);
        await Until(() => received.TryCaptureRemoteChoices(pair.Guest, out pending) && pending.Route != null, pair.Cancellation);
        Assert(pending.Generation == 2 && pending.Route.Scenes.Length == 9 && pending.Choices.Length == 0 && pending.LastChoiceRevision == 0,
            "atomic route completion invented a received group or choice revision");

        MapRouteSlice[] next = MapChoiceFrames.SplitRoute(Route(9), 3);
        await pair.Send(next[0]);
        await Until(() => pair.Guest.Snapshot.MapChoiceGeneration == 3, pair.Cancellation);
        Assert(pair.Guest.TryTakeRemoteMapChoices(out MapChoiceSnapshot stolen) && stolen.Route == null,
            "fixture did not consume the new pending mailbox before the callback");
        Assert(!received.TryCaptureRemoteChoices(pair.Guest, out _) && received.RemoteGeneration == 0,
            "missing latest mailbox allowed cached data with an old session header");
        await pair.Send(next[1]);
        await Until(() => received.TryCaptureRemoteChoices(pair.Guest, out pending) && pending.Route != null, pair.Cancellation);
        Assert(pending.Generation == 3, "fresh completed candidate could not recover after stale cache invalidation");
        received.Clear(30);
        Assert(!received.TryCaptureRemoteChoices(pair.Guest, out _) && received.RemoteGeneration == 0, "Clear retained a live consumer room binding");
        received.BindRoom(pair.Guest, 30);
        Assert(!received.TryCaptureRemoteChoices(pair.Guest, out _), "rebind replayed an already consumed historical candidate");
        foreach (MapRouteSlice slice in MapChoiceFrames.SplitRoute(Route(), 4)) await pair.Send(slice);
        await Until(() => received.TryCaptureRemoteChoices(pair.Guest, out pending) && pending.Route != null, pair.Cancellation);
        Assert(pending.Generation == 4 && pair.Guest.Snapshot.Phase == SessionPhase.WaitingForScene,
            "fresh bound candidate elevated scene readiness");
        pair.Guest.Dispose();
        Assert(!received.TryCaptureRemoteChoices(pair.Guest, out _) && received.RemoteGeneration == 0 && pending.Generation == 4 && pending.Route != null,
            "closed peer retained usable consumer state or destroyed a caller-owned historical copy");
    }

    private static async Task Until(Func<bool> condition, CancellationToken cancellation)
    {
        while (!condition()) await Task.Delay(5, cancellation);
    }

    internal static MapOriginSourceFrame Frame(Guid run, long owner, int count = 3, int choices = 1)
    {
        MapRouteSelection route = Route(count);
        string fingerprint = MapSelections.FingerprintRoute(route);
        var inventory = new MapOriginChoiceEvidence[choices];
        for (int i = 0; i < choices; i++) inventory[i] = new MapOriginChoiceEvidence
        {
            OwnerLife = owner, ContextPointer = 100 + owner, RouteFingerprint = fingerprint,
            OperationLife = owner * 1000 + 1, LoadKey = "Addressables/dive-0", SceneLife = owner * 1000 + 2,
            SceneHandle = 100 + (int)owner, ControllerLife = owner * 1000 + 10 + i, ControllerSceneName = "dive-0", CallbackSequence = 1,
            Choice = new MapGroupSelection { SceneId = 1000, ControllerAddress = "RuntimeObjects/group-" + i, Addressable = true, SelectedPrefabName = "IGP_A" + i, PrefabObjectName = "" }
        };
        return new MapOriginSourceFrame { RunId = run, Healthy = true, Source = new MapOriginSourceSnapshot { OwnerLife = owner, Route = route, RouteFingerprint = fingerprint, Choices = inventory } };
    }
    internal static MapRouteSelection Route(int count = 3)
    {
        var scenes = new MapRouteScene[count];
        for (int i = 0; i < count; i++) scenes[i] = new MapRouteScene
        {
            SceneId = 1000 + i, SceneName = "dive-" + i, Layer = 'A', MapHeight = 500, TopY = 0, BottomY = -500, Offset = i * -500,
            PreviousSceneId = i == 0 ? 0 : 999 + i, NextSceneId = i + 1 == count ? 0 : 1001 + i
        };
        return new MapRouteSelection { EntrySceneId = 1000, Scenes = scenes };
    }
    private static MapSelectionCallObservation RouteObservation(long sequence) => new MapSelectionCallObservation
    {
        ProcessSequence = sequence, Stage = "RouteCachedAfter", MainThread = true, CallbackThreadId = Environment.CurrentManagedThreadId,
        Route = Route(), RouteFingerprint = MapSelections.FingerprintRoute(Route())
    };
    private static MapSelectionCallObservation ChoiceObservation(long sequence, string prefab) => new MapSelectionCallObservation
    {
        ProcessSequence = sequence, Stage = "IgpSelectedAfter", MainThread = true, CallbackThreadId = Environment.CurrentManagedThreadId,
        ControllerSceneName = "dive-0", ControllerAddress = "RuntimeObjects/group-0", SelectionPresent = true,
        Addressable = true, SelectedPrefabName = prefab, PrefabObjectName = ""
    };
    internal static async Task Remote(MapChoiceController received, TcpPair pair, Func<bool> condition)
    {
        while (true)
        {
            received.Update(pair.Guest, null);
            if (condition()) return;
            if (pair.Host.Snapshot.Phase == SessionPhase.Closed || pair.Guest.Snapshot.Phase == SessionPhase.Closed)
                throw new InvalidOperationException("Adapter TCP fixture closed: " + pair.Host.Snapshot.Reason + "; " + pair.Guest.Snapshot.Reason);
            await Task.Delay(5, pair.Cancellation);
        }
    }
    internal static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    internal sealed class TcpPair : IDisposable
    {
        private readonly CancellationTokenSource _timeout;
        private readonly LanHost _listener;
        public SessionPeer Host { get; }
        public SessionPeer Guest { get; }
        public CancellationToken Cancellation => _timeout.Token;
        private TcpPair(CancellationTokenSource timeout, LanHost listener, SessionPeer host, SessionPeer guest) { _timeout = timeout; _listener = listener; Host = host; Guest = guest; }
        public static async Task<TcpPair> Open()
        {
            var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); var listener = new LanHost(IPAddress.Loopback, 0);
            SessionPeer host = null, guest = null;
            try
            {
                Task<SessionPeer> accepting = listener.AcceptOneAsync(Identity("Host"), timeout.Token);
                guest = await LanGuest.ConnectAsync("127.0.0.1", listener.Port, Identity("Guest"), timeout.Token);
                host = await accepting; return new TcpPair(timeout, listener, host, guest);
            }
            catch { host?.Dispose(); guest?.Dispose(); listener.Dispose(); timeout.Dispose(); throw; }
        }
        private static PeerIdentity Identity(string name) => new PeerIdentity { ModVersion = "0.1.17-dev", SteamBuildId = "25315876", UnityVersion = "6000.0.52f1", Name = name };
        public void Dispose() { Host.Dispose(); Guest.Dispose(); _listener.Dispose(); _timeout.Dispose(); }
    }

    private sealed class ManualMapPair : IDisposable
    {
        private readonly CancellationTokenSource _timeout;
        private readonly FramedConnection _host;
        private readonly TcpClient _hostSocket;
        public SessionPeer Guest { get; }
        public string RoomId { get; }
        public CancellationToken Cancellation => _timeout.Token;
        private ManualMapPair(CancellationTokenSource timeout, FramedConnection host, TcpClient hostSocket, SessionPeer guest, string room)
        { _timeout = timeout; _host = host; _hostSocket = hostSocket; Guest = guest; RoomId = room; }
        public static async Task<ManualMapPair> Open()
        {
            var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(1);
            TcpClient socket = null; FramedConnection host = null; SessionPeer guest = null;
            try
            {
                var identity = new PeerIdentity { ModVersion = "fixture-owned-map-consumer", SteamBuildId = "25315876", UnityVersion = "6000.0.52f1", Name = "Guest" };
                Task<SessionPeer> joining = LanGuest.ConnectAsync("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, identity, timeout.Token);
                socket = await listener.AcceptTcpClientAsync(timeout.Token); socket.NoDelay = true;
                host = new FramedConnection(socket.GetStream()); string room = Guid.NewGuid().ToString("N");
                await Handshake.AcceptAsync(host, identity, room, timeout.Token); guest = await joining;
                return new ManualMapPair(timeout, host, socket, guest, room);
            }
            catch { guest?.Dispose(); host?.Dispose(); socket?.Dispose(); timeout.Dispose(); throw; }
            finally { listener.Stop(); }
        }
        public Task Send(MapRouteSlice slice) => _host.SendAsync(new WirePacket { Kind = PacketKind.MapRouteSlice, RoomId = RoomId, MapRoute = slice }, Cancellation);
        public void Dispose() { Guest.Dispose(); _host.Dispose(); _hostSocket.Dispose(); _timeout.Dispose(); }
    }
}

namespace DaveCoop.Networking
{
    internal static class NetworkDriver { internal static readonly MapChoiceTestLogger Logger = new MapChoiceTestLogger(); }
    internal sealed class MapChoiceTestLogger
    {
        private readonly object _gate = new object();
        private readonly List<string> _messages = new List<string>();
        public void LogInfo(string message) { lock (_gate) _messages.Add(message); }
        public string[] Snapshot() { lock (_gate) return _messages.ToArray(); }
    }
}
