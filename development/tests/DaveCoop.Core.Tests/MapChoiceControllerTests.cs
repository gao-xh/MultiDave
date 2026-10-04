using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.Session;
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
        Assert(source.PublishedRoutes == 3 && source.PublishedChoices == 3 && source.SuppressedLegacyObservations == 3 &&
            pair.Host.Snapshot.Phase == SessionPhase.WaitingForScene && pair.Guest.Snapshot.SceneEpoch == 0 &&
            received.Status.Contains("not implemented"), "fixed-origin candidates elevated readiness or admitted late legacy evidence");
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
