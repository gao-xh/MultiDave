using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.Session;
using DaveCoop.Core.World;
using DaveCoop.Networking;

// Compile the production CLR adapter and observation DTO. Only its logging
// sink is replaced; these fixtures never invoke Unity or a native callback.
internal static class MapChoiceControllerTests
{
    public static async Task TcpCallbackFloorAndRoomIsolation()
    {
        using var first = await TcpPair.Open();
        var source = new MapChoiceController();
        var received = new MapChoiceController();
        source.BindRoom(first.Host, 100); received.BindRoom(first.Guest, 100);
        source.Observe(RouteObservation(100), first.Host);
        source.Observe(ChoiceObservation(101, "pre-route"), first.Host);
        Assert(source.PublishedRoutes == 0 && source.UnboundChoices == 1 && first.Host.Snapshot.MapChoiceGeneration == 0,
            "room callback floor or pre-route discard admitted stale evidence");
        source.Observe(RouteObservation(102), first.Host);
        await Remote(received, first, () => received.RemoteGeneration == 1 && received.RemoteRouteSceneCount == 3);
        Assert(received.RemoteChoiceCount == 0, "a pre-route IGP was buffered and rebound to the new route");
        source.Observe(ChoiceObservation(103, "current"), first.Host);
        await Remote(received, first, () => received.RemoteChoiceCount == 1);
        source.Retire(first.Host, 110, "observer closed");
        source.Observe(RouteObservation(109), first.Host);
        source.Observe(ChoiceObservation(110, "queued before close"), first.Host);
        await Remote(received, first, () => received.RemoteRouteSceneCount == 0 && received.RemoteChoiceCount == 0);
        Assert(source.PublishedRoutes == 1 && source.CallbackFloor == 110 && first.Host.Snapshot.MapChoiceFingerprint == null,
            "queued callbacks crossed the observer close floor");

        // This models old, already queued observations only. A later native
        // callback with unknown origin/context is not proven by sequence alone.
        source.Clear(120); received.Clear(120);
        using var second = await TcpPair.Open();
        source.BindRoom(second.Host, 125); received.BindRoom(second.Guest, 125);
        source.Observe(RouteObservation(124), second.Host);
        source.Observe(ChoiceObservation(125, "old room"), second.Host);
        source.Observe(RouteObservation(126), first.Host);
        Assert(source.PublishedRoutes == 1 && second.Host.Snapshot.MapChoiceGeneration == 0 && source.CallbackFloor == 125,
            "clear/bind reset the process floor or accepted the old room peer");
        source.Observe(RouteObservation(127), second.Host);
        await Remote(received, second, () => received.RemoteGeneration == 1 && received.RemoteRouteSceneCount == 3);
        Assert(source.PublishedRoutes == 2 && source.PublishedChoices == 1 && source.UnboundChoices == 1 &&
            source.DroppedObservations >= 5 && source.HighestCallback == 127 && received.RemoteChoiceCount == 0 &&
            first.Host.Snapshot.RoomId != second.Host.Snapshot.RoomId,
            "new room replayed old choices, reset counters, or failed to accept a fresh natural route");
    }

    public static async Task TcpNaturalGenerationsAndChoiceRevisions()
    {
        using var pair = await TcpPair.Open();
        var source = new MapChoiceController(); var received = new MapChoiceController();
        source.BindRoom(pair.Host, 0); received.BindRoom(pair.Guest, 0);
        source.Observe(ChoiceObservation(1, "before-route"), pair.Host);
        MapSelectionCallObservation factory = ChoiceObservation(2, "factory");
        factory.Stage = "IgpPrefabFactoryBefore"; factory.ControllerAddress = null; factory.ControllerSceneName = null;
        source.Observe(factory, pair.Host);
        source.Observe(RouteObservation(3, "RouteCachedAfter", 9), pair.Host);
        await Remote(received, pair, () => received.RemoteGeneration == 1 && received.RemoteRouteSceneCount == 9);
        string fingerprint = pair.Host.Snapshot.MapChoiceFingerprint;
        Assert(source.UnboundChoices == 2 && received.RemoteChoiceCount == 0 && pair.Host.Snapshot.MapChoiceRevision == 0,
            "unbound original result or factory was promoted into route choices");
        source.Observe(RouteObservation(4, "SceneLoadCallBefore", 9), pair.Host);
        Assert(source.PublishedRoutes == 1 && source.DuplicateObservations == 1 && pair.Host.Snapshot.MapChoiceGeneration == 1,
            "same active load route created a new generation");
        source.Observe(ChoiceObservation(5, "IGP_A"), pair.Host);
        source.Observe(ChoiceObservation(6, "IGP_A"), pair.Host);
        Assert(source.PublishedChoices == 1 && pair.Host.Snapshot.MapChoiceRevision == 1,
            "identical callback consumed another choice revision");
        source.Observe(ChoiceObservation(7, "IGP_B"), pair.Host);
        await Remote(received, pair, () => received.RemoteChoiceCount == 1 && pair.Guest.Snapshot.MapChoiceRevision == 2);
        Assert(source.PublishedChoices == 2 && source.DuplicateObservations == 2, "same-controller update did not advance exactly once");

        source.Observe(RouteObservation(8, "RouteCachedAfter", 9), pair.Host);
        await Remote(received, pair, () => received.RemoteGeneration == 2 && received.RemoteRouteSceneCount == 9 && received.RemoteChoiceCount == 0);
        Assert(pair.Host.Snapshot.MapChoiceFingerprint == fingerprint && pair.Host.Snapshot.MapChoiceRevision == 0,
            "same hash cache reused the prior generation's IGP identity");
        source.Observe(ChoiceObservation(7, "queued old IGP"), pair.Host);
        source.Observe(ChoiceObservation(9, "IGP_C"), pair.Host);
        await Remote(received, pair, () => received.RemoteGeneration == 2 && received.RemoteChoiceCount == 1);
        Assert(pair.Host.Snapshot.MapChoiceRevision == 1 && source.PublishedChoices == 3, "old queued callback entered the rearmed generation");
        source.Observe(RouteObservation(10, "RouteRestoredAfter", 9), pair.Host);
        await Remote(received, pair, () => received.RemoteGeneration == 3 && received.RemoteRouteSceneCount == 9 && received.RemoteChoiceCount == 0);
        Assert(source.PublishedRoutes == 3 && pair.Host.Snapshot.MapChoiceFingerprint == fingerprint &&
            pair.Host.Snapshot.MapChoiceRevision == 0 && pair.Host.Snapshot.Phase == SessionPhase.WaitingForScene &&
            pair.Guest.Snapshot.SceneEpoch == 0 && received.Status.Contains("candidate evidence") && received.Status.Contains("not implemented"),
            "restore reused old choices or promoted pre-load evidence into scene/native authority");
    }

    public static async Task TcpUnavailableSamplesRetireAndRecover()
    {
        using var pair = await TcpPair.Open();
        var source = new MapChoiceController(); var received = new MapChoiceController();
        source.BindRoom(pair.Host, 0); received.BindRoom(pair.Guest, 0);
        long sequence = 0;
        for (int fault = 0; fault < 5; fault++)
        {
            long generation = fault + 1;
            source.Observe(RouteObservation(++sequence), pair.Host);
            source.Observe(ChoiceObservation(++sequence, "current"), pair.Host);
            await Remote(received, pair, () => received.RemoteGeneration == generation && received.RemoteChoiceCount == 1);
            MapSelectionCallObservation unusable = fault == 2 || fault == 3
                ? RouteObservation(++sequence, "SceneLoadCallBefore")
                : ChoiceObservation(++sequence, "replacement");
            if (fault == 0 || fault == 3) unusable.Truncated = true;
            if (fault == 1) unusable.ReadError = "original choice read failed";
            if (fault == 2)
            {
                unusable.Route = null; unusable.RouteFingerprint = null;
                unusable.RouteUnavailableReason = "selected route missing";
            }
            if (fault == 4)
            {
                // The previously published controller now returned no original
                // choice. Keeping its earlier prefab would be stale evidence.
                unusable.SelectionPresent = false; unusable.Addressable = null;
                unusable.SelectedPrefabName = null; unusable.PrefabObjectName = null;
            }
            source.Observe(unusable, pair.Host);
            Assert(pair.Host.Snapshot.MapChoiceFingerprint == null && source.SourceGeneration == 0,
                "unavailable route/choice retained the prior local candidate (fault " + fault + ")");
            await Remote(received, pair, () => received.RemoteGeneration == generation && received.RemoteRouteSceneCount == 0 && received.RemoteChoiceCount == 0);
            long publishedChoices = source.PublishedChoices;
            source.Observe(ChoiceObservation(++sequence, "post-retire without route"), pair.Host);
            Assert(source.PublishedChoices == publishedChoices && pair.Host.Snapshot.MapChoiceGeneration == generation &&
                pair.Host.Snapshot.MapChoiceFingerprint == null, "IGP alone restarted a retired generation");
        }
        source.Observe(RouteObservation(++sequence, "RouteRestoredAfter"), pair.Host);
        source.Observe(ChoiceObservation(++sequence, "recovered"), pair.Host);
        await Remote(received, pair, () => received.RemoteGeneration == 6 && received.RemoteRouteSceneCount == 3 && received.RemoteChoiceCount == 1);
        Assert(source.PublishedRoutes == 6 && source.PublishedChoices == 6 && source.UnboundChoices >= 5 &&
            source.PublicationErrors == 0 && pair.Host.Snapshot.MapChoiceRevision == 1 && pair.Host.Snapshot.Phase != SessionPhase.Closed,
            "safe retirement prevented natural recovery in the same TCP room");
    }

    public static async Task TcpGuestObserverStopRetainsHostEvidence()
    {
        using var pair = await TcpPair.Open();
        var source = new MapChoiceController(); var received = new MapChoiceController();
        source.BindRoom(pair.Host, 0); received.BindRoom(pair.Guest, 0);
        source.Observe(RouteObservation(1), pair.Host);
        source.Observe(ChoiceObservation(2, "host-choice"), pair.Host);
        await Remote(received, pair, () => received.RemoteGeneration == 1 && received.RemoteChoiceCount == 1);
        string fingerprint = pair.Host.Snapshot.MapChoiceFingerprint;
        received.Retire(pair.Guest, 50, "guest local observer disabled");
        received.Observe(RouteObservation(51), pair.Guest);
        Assert(received.RemoteRouteSceneCount == 3 && received.RemoteChoiceCount == 1 &&
            pair.Guest.Snapshot.MapChoiceFingerprint == fingerprint && pair.Host.Snapshot.MapChoiceFingerprint == fingerprint &&
            received.PublishedRoutes == 0 && received.Status.Contains("local observer stopped"),
            "stopping a guest observer retracted the host's received candidate");
        source.Retire(pair.Host, 10, "host observer disabled");
        await Remote(received, pair, () => received.RemoteGeneration == 1 && received.RemoteRouteSceneCount == 0 && received.RemoteChoiceCount == 0);
        source.Observe(RouteObservation(11), pair.Host);
        source.Observe(ChoiceObservation(12, "new-host-choice"), pair.Host);
        await Remote(received, pair, () => received.RemoteGeneration == 2 && received.RemoteChoiceCount == 1);
        Assert(received.CallbackFloor == 50 && received.HighestCallback == 51 && pair.Guest.Snapshot.MapChoiceRevision == 1,
            "guest local callback floor blocked fresh remote evidence or reset on host recovery");
        received.Clear(60);
        Assert(received.RemoteGeneration == 0 && received.RemoteRouteSceneCount == 0 && received.RemoteChoiceCount == 0 && received.CallbackFloor == 60,
            "disconnect clear retained the received candidate or lost its floor");
        received.BindRoom(pair.Guest, 60);
        source.Observe(RouteObservation(13, "RouteRestoredAfter"), pair.Host);
        await Remote(received, pair, () => received.RemoteGeneration == 3 && received.RemoteRouteSceneCount == 3 && received.RemoteChoiceCount == 0);
        MapSelectionCallObservation dto = ChoiceObservation(14, "flags");
        Assert(dto.ObservationOnly && !dto.HostSelectionApplied && !dto.CrossMachineAddressVerified && !dto.ResourceLoadCompletionObserved &&
            source.PublicationErrors == 0 && received.PublicationErrors == 0,
            "CLR evidence asserted adoption, cross-machine identity, or resource completion");
        foreach (string trace in NetworkDriver.Logger.Snapshot())
            Assert(trace.Contains("\"CandidateEvidence\":true") && trace.Contains("\"NativeAdoptionImplemented\":false") &&
                trace.Contains("\"HostSelectionApplied\":false"), "adapter trace implied native adoption permission");
    }

    private static MapSelectionCallObservation RouteObservation(long sequence, string stage = "RouteCachedAfter", int count = 3)
    {
        var scenes = new MapRouteScene[count];
        for (int i = 0; i < count; i++) scenes[i] = new MapRouteScene
        {
            SceneId = 1000 + i, SceneName = "dive-" + i, Layer = 'A', MapHeight = 500, TopY = 0, BottomY = -500, Offset = i * -500,
            PreviousSceneId = i == 0 ? 0 : 999 + i, NextSceneId = i + 1 == count ? 0 : 1001 + i
        };
        var route = new MapRouteSelection { EntrySceneId = 1000, Scenes = scenes };
        return new MapSelectionCallObservation
        {
            ProcessSequence = sequence, Stage = stage, MainThread = true, CallbackThreadId = Environment.CurrentManagedThreadId,
            Route = route, RouteFingerprint = MapSelections.FingerprintRoute(route)
        };
    }

    private static MapSelectionCallObservation ChoiceObservation(long sequence, string prefab) => new MapSelectionCallObservation
    {
        ProcessSequence = sequence, Stage = "IgpSelectedAfter", MainThread = true, CallbackThreadId = Environment.CurrentManagedThreadId,
        ControllerSceneName = "dive-0", ControllerAddress = "RuntimeObjects/group-a", SelectionPresent = true,
        Addressable = true, SelectedPrefabName = prefab, PrefabObjectName = ""
    };

    private static async Task Remote(MapChoiceController received, TcpPair pair, Func<bool> condition)
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

    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class TcpPair : IDisposable
    {
        private readonly CancellationTokenSource _timeout;
        private readonly LanHost _listener;
        public SessionPeer Host { get; }
        public SessionPeer Guest { get; }
        public CancellationToken Cancellation => _timeout.Token;
        private TcpPair(CancellationTokenSource timeout, LanHost listener, SessionPeer host, SessionPeer guest)
        { _timeout = timeout; _listener = listener; Host = host; Guest = guest; }

        public static async Task<TcpPair> Open()
        {
            var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var listener = new LanHost(IPAddress.Loopback, 0);
            SessionPeer host = null, guest = null;
            try
            {
                Task<SessionPeer> accepting = listener.AcceptOneAsync(Identity("Host"), timeout.Token);
                guest = await LanGuest.ConnectAsync("127.0.0.1", listener.Port, Identity("Guest"), timeout.Token);
                host = await accepting;
                return new TcpPair(timeout, listener, host, guest);
            }
            catch { host?.Dispose(); guest?.Dispose(); listener.Dispose(); timeout.Dispose(); throw; }
        }
        private static PeerIdentity Identity(string name) => new PeerIdentity
        { ModVersion = "0.1.14-dev", SteamBuildId = "25315876", UnityVersion = "6000.0.52f1", Name = name };
        public void Dispose() { Host.Dispose(); Guest.Dispose(); _listener.Dispose(); _timeout.Dispose(); }
    }
}

namespace DaveCoop.Networking
{
    internal static class NetworkDriver
    {
        internal static readonly MapChoiceTestLogger Logger = new MapChoiceTestLogger();
    }

    internal sealed class MapChoiceTestLogger
    {
        private readonly object _gate = new object();
        private readonly List<string> _messages = new List<string>();
        public void LogInfo(string message) { lock (_gate) _messages.Add(message); }
        public string[] Snapshot() { lock (_gate) return _messages.ToArray(); }
    }
}
