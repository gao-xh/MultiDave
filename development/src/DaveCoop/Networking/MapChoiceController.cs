using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using DaveCoop.Core.Session;
using DaveCoop.Core.World;

namespace DaveCoop.Networking
{
    // Main-thread adapter for already copied callback evidence. No native
    // wrapper, scene epoch binding or map-adoption permission enters this path.
    internal sealed class MapChoiceController
    {
        private const int MaxTraces = 1024;
        private const int MaxReceivedPerUpdate = 8;
        private readonly Dictionary<string, int> _routeScenes = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<(int Scene, string Address), MapIgpChoice> _publishedChoices = new Dictionary<(int, string), MapIgpChoice>();
        private SessionPeer _main;
        private string _roomId;
        private MapRouteSelection _sourceRoute;
        private string _sourceFingerprint;
        private long _sourceGeneration;
        private MapChoiceSnapshot _remote;
        private long _callbackFloor;
        private long _highestCallback;
        private int _traces;

        public long PublishedRoutes { get; private set; }
        public long PublishedChoices { get; private set; }
        public long ReceivedSnapshots { get; private set; }
        public long UnboundChoices { get; private set; }
        public long DroppedObservations { get; private set; }
        public long InvalidObservations { get; private set; }
        public long DuplicateObservations { get; private set; }
        public long PublicationErrors { get; private set; }
        public long CallbackFloor => _callbackFloor;
        public long HighestCallback => _highestCallback;
        public long SourceGeneration => _sourceGeneration;
        public string SourceFingerprint => _sourceFingerprint;
        public long RemoteGeneration => _remote?.Generation ?? 0;
        public int RemoteRouteSceneCount => _remote?.Route?.Scenes?.Length ?? 0;
        public int RemoteChoiceCount => _remote?.Choices?.Length ?? 0;
        public string Status { get; private set; } = "Map candidate evidence only; adoption not implemented.";

        public void BindRoom(SessionPeer main, long callbackFloor)
        {
            Clear(callbackFloor);
            if (main == null) return;
            SessionSnapshot state = main.Snapshot;
            if (state.Phase == SessionPhase.Closed || string.IsNullOrEmpty(state.RoomId)) return;
            _main = main; _roomId = state.RoomId;
            Status = state.Role == SessionRole.Host
                ? "Awaiting natural map candidate evidence; adoption not implemented."
                : "Awaiting host map candidate evidence; adoption not implemented.";
            Trace("BOUND", new { CallbackFloor = _callbackFloor, Role = state.Role.ToString() });
        }

        public void Observe(MapSelectionCallObservation observation, SessionPeer main)
        {
            if (observation == null) return;
            long sequence = observation.ProcessSequence;
            if (sequence <= _callbackFloor || sequence <= _highestCallback)
            {
                DroppedObservations++;
                return;
            }
            _highestCallback = sequence;
            if (!MatchesRoom(main, out SessionSnapshot state) || state.Role != SessionRole.Host)
            {
                DroppedObservations++;
                return;
            }
            if (_sourceRoute != null && !MatchesSource(state)) ResetSource();

            bool routeBoundary = observation.Stage == "RouteCachedAfter" || observation.Stage == "RouteRestoredAfter";
            if (!observation.MainThread || observation.CallbackThreadId != Environment.CurrentManagedThreadId ||
                !string.IsNullOrEmpty(observation.ReadError) || observation.Truncated)
            {
                InvalidObservations++;
                Trace("UNUSABLE", new { sequence, observation.Stage, observation.MainThread, observation.Truncated, observation.ReadError });
                if (routeBoundary || observation.Stage == "SceneLoadCallBefore" || observation.Stage == "IgpSelectedAfter")
                    Retire(main, sequence, "Map selection observation could not be copied safely.");
                return;
            }

            switch (observation.Stage)
            {
                case "RouteCachedAfter":
                case "RouteRestoredAfter":
                    PublishRoute(observation, main, true);
                    break;
                case "SceneLoadCallBefore":
                    PublishRoute(observation, main, false);
                    break;
                case "IgpSelectedAfter":
                    PublishChoice(observation, main);
                    break;
                case "IgpPrefabFactoryBefore":
                    // The factory has no unique controller, and does not
                    // establish which scene requested or completed loading.
                    Unbound(observation, "Prefab factory has no controller binding.");
                    break;
                default:
                    DroppedObservations++;
                    break;
            }
        }

        private void PublishRoute(MapSelectionCallObservation observation, SessionPeer main, bool newSelectionBoundary)
        {
            MapRouteSelection owned;
            string fingerprint;
            try
            {
                if (observation.Route == null || !string.IsNullOrEmpty(observation.RouteUnavailableReason))
                    throw new ArgumentException(observation.RouteUnavailableReason ?? "Route was not copied.");
                owned = MapSelections.CopyRoute(observation.Route);
                fingerprint = MapSelections.FingerprintRoute(owned);
                if (observation.RouteFingerprint != fingerprint)
                    throw new ArgumentException("Copied route fingerprint does not match its fields.");
            }
            catch (ArgumentException error)
            {
                InvalidObservations++;
                Trace("ROUTE_UNAVAILABLE", new { observation.ProcessSequence, observation.Stage, Reason = Reason(error.Message) });
                Retire(main, observation.ProcessSequence, "Route boundary has no valid route candidate.");
                return;
            }
            // Cache/restore is a fresh selection boundary even when its hash
            // repeats. A load call alone does not rearm the same active route.
            if (!newSelectionBoundary && _sourceRoute != null && fingerprint == _sourceFingerprint)
            {
                DuplicateObservations++;
                return;
            }
            try
            {
                SessionSnapshot before = main.Snapshot;
                if (!main.PublishMapRoute(owned))
                {
                    Retire(main, observation.ProcessSequence, "Route candidate publication was canceled.");
                    Trace("ROUTE_CANCELED", new { observation.ProcessSequence, observation.Stage });
                    return;
                }
                PublishedRoutes++;
                SessionSnapshot after = main.Snapshot;
                if (after.Phase == SessionPhase.Closed || after.RoomId != _roomId ||
                    after.MapChoiceGeneration <= before.MapChoiceGeneration || after.MapChoiceFingerprint != fingerprint)
                {
                    ResetSource();
                    Status = "Map candidate source ended; adoption not implemented.";
                    return;
                }
                ResetSource();
                _sourceRoute = owned; _sourceFingerprint = fingerprint; _sourceGeneration = after.MapChoiceGeneration;
                foreach (MapRouteScene scene in owned.Scenes) _routeScenes.Add(scene.SceneName, scene.SceneId);
                Status = "Host route candidate evidence sent; adoption not implemented.";
                Trace("ROUTE_SENT", new
                {
                    observation.ProcessSequence, observation.Stage, Generation = _sourceGeneration,
                    RouteFingerprint = fingerprint, owned.EntrySceneId, SceneCount = owned.Scenes.Length
                });
            }
            catch (Exception error) { PublicationFailed(main, observation, error); }
        }

        private void PublishChoice(MapSelectionCallObservation observation, SessionPeer main)
        {
            if (_sourceRoute == null ||
                string.IsNullOrWhiteSpace(observation.ControllerAddress) ||
                string.IsNullOrEmpty(observation.ControllerSceneName) ||
                !_routeScenes.TryGetValue(observation.ControllerSceneName, out int sceneId))
            {
                // A pre-route result is deliberately discarded. Its native
                // context/epoch cannot be proven to belong to a later route.
                Unbound(observation, "Original result has no current route/controller binding.");
                return;
            }
            if (observation.SelectionPresent != true || !observation.Addressable.HasValue)
            {
                // A later null/unknown result for an observed group invalidates
                // its previous candidate; a missing new group is unbound evidence.
                if (_publishedChoices.ContainsKey((sceneId, observation.ControllerAddress)))
                    Retire(main, observation.ProcessSequence, "Previously observed IGP group returned no usable selection.");
                else Unbound(observation, "Original result has no usable selection.");
                return;
            }
            SessionSnapshot state = main.Snapshot;
            if (!MatchesSource(state))
            {
                ResetSource();
                Unbound(observation, "Published source generation is no longer active.");
                return;
            }
            if (state.MapChoiceRevision == long.MaxValue)
            {
                Retire(main, observation.ProcessSequence, "Map choice revisions exhausted.");
                return;
            }
            MapIgpChoice choice;
            try
            {
                choice = MapChoiceFrames.Copy(new MapIgpChoice
                {
                    Generation = state.MapChoiceGeneration, RouteFingerprint = state.MapChoiceFingerprint,
                    Revision = state.MapChoiceRevision + 1, SceneId = sceneId,
                    ControllerAddress = observation.ControllerAddress, Addressable = observation.Addressable.Value,
                    SelectedPrefabName = observation.SelectedPrefabName, PrefabObjectName = observation.PrefabObjectName
                });
            }
            catch (ArgumentException error)
            {
                InvalidObservations++;
                Trace("CHOICE_UNAVAILABLE", new { observation.ProcessSequence, Reason = Reason(error.Message) });
                Retire(main, observation.ProcessSequence, "Original IGP choice could not be represented safely.");
                return;
            }
            var key = (sceneId, choice.ControllerAddress);
            if (_publishedChoices.TryGetValue(key, out MapIgpChoice previous))
            {
                if (SameSelection(previous, choice)) { DuplicateObservations++; return; }
            }
            else if (_publishedChoices.Count >= MapChoiceFrames.MaxChoices)
            {
                DroppedObservations++;
                Retire(main, observation.ProcessSequence, "Map choice candidate capacity exceeded.");
                return;
            }
            try
            {
                if (!main.PublishMapIgpChoice(choice))
                {
                    // The peer atomically rejects a retired/old source; queue
                    // overflow also sends explicit retirement. Never replay.
                    ResetSource();
                    Status = "Map choice candidate publication canceled; adoption not implemented.";
                    Trace("CHOICE_CANCELED", new { observation.ProcessSequence, choice.Generation, choice.Revision });
                    return;
                }
                PublishedChoices++; _publishedChoices[key] = choice;
                Status = "Host IGP candidate evidence sent; adoption not implemented.";
                Trace("CHOICE_SENT", new { observation.ProcessSequence, Choice = choice });
            }
            catch (Exception error) { PublicationFailed(main, observation, error); }
        }

        public void Update(SessionPeer main, SessionPeer loopback)
        {
            if (!MatchesRoom(main, out SessionSnapshot state)) return;
            if (_sourceRoute != null && !MatchesSource(state)) ResetSource();
            SessionPeer receiver = state.Role == SessionRole.Guest ? main : loopback;
            if (receiver == null) return;
            SessionSnapshot receiving = receiver.Snapshot;
            if (receiving.Role != SessionRole.Guest || receiving.Phase == SessionPhase.Closed || receiving.RoomId != _roomId) return;
            for (int i = 0; i < MaxReceivedPerUpdate && receiver.TryTakeRemoteMapChoices(out MapChoiceSnapshot evidence); i++)
            {
                // SessionPeer transfers an owned CLR snapshot. Partial new
                // generations and explicit retirements revoke the old route.
                _remote = evidence; ReceivedSnapshots++;
                Status = evidence.Retired
                    ? "Host map candidate evidence retired; adoption not implemented."
                    : evidence.Route == null
                        ? "Host map candidate route assembling; adoption not implemented."
                        : "Host map candidate evidence received; adoption not implemented.";
                Trace("RECEIVED", new
                {
                    evidence.Generation, evidence.RouteFingerprint, evidence.LastChoiceRevision, evidence.Retired,
                    RouteComplete = evidence.Route != null, SceneCount = RemoteRouteSceneCount, ChoiceCount = RemoteChoiceCount
                });
            }
        }

        public void Retire(SessionPeer main, long floor, string reason)
        {
            AdvanceFloor(floor);
            ResetSource();
            bool sent = false;
            string boundedReason = Reason(reason);
            try
            {
                if (MatchesRoom(main, out SessionSnapshot state) && state.Role == SessionRole.Host && state.MapChoiceFingerprint != null)
                    sent = main.RetireMapChoices(boundedReason);
            }
            catch (Exception error)
            {
                PublicationErrors++;
                Trace("RETIRE_ERROR", new { Error = Reason(error.GetType().Name + ": " + error.Message) });
            }
            // Disabling a guest's local observer never retracts evidence that
            // it received from the host. Only the host's retire can do that.
            Status = _remote != null && !_remote.Retired && _remote.Route != null
                ? "Host map candidate evidence retained; local observer stopped; adoption not implemented."
                : "Local map candidate source retired; adoption not implemented.";
            Trace("RETIRED", new { CallbackFloor = _callbackFloor, Reason = boundedReason, Sent = sent });
        }

        public void Clear(long floor = 0)
        {
            AdvanceFloor(floor);
            ResetSource(); _remote = null; _main = null; _roomId = null;
            // Process callback watermarks, counters and trace quotas persist
            // across rooms, so old queued callbacks cannot be replayed.
            Status = "Map candidate evidence only; adoption not implemented.";
        }

        private void AdvanceFloor(long floor) => _callbackFloor = Math.Max(_callbackFloor, Math.Max(_highestCallback, floor));

        private bool MatchesRoom(SessionPeer main, out SessionSnapshot state)
        {
            state = main?.Snapshot;
            return ReferenceEquals(main, _main) && _roomId != null && state != null &&
                state.Phase != SessionPhase.Closed && state.RoomId == _roomId;
        }

        private bool MatchesSource(SessionSnapshot state) => _sourceRoute != null && state.MapChoiceGeneration == _sourceGeneration &&
            state.MapChoiceFingerprint == _sourceFingerprint;

        private void ResetSource()
        {
            _sourceRoute = null; _sourceFingerprint = null; _sourceGeneration = 0;
            _routeScenes.Clear(); _publishedChoices.Clear();
        }

        private void Unbound(MapSelectionCallObservation observation, string reason)
        {
            UnboundChoices++;
            Trace("UNBOUND", new { observation.ProcessSequence, observation.Stage, Reason = reason });
        }

        private void PublicationFailed(SessionPeer main, MapSelectionCallObservation observation, Exception error)
        {
            PublicationErrors++;
            Retire(main, observation.ProcessSequence, "Map candidate publication failed.");
            Status = "Map candidate publication failed; adoption not implemented.";
            Trace("PUBLISH_ERROR", new { observation.ProcessSequence, observation.Stage, Error = Reason(error.GetType().Name + ": " + error.Message) });
        }

        private static bool SameSelection(MapIgpChoice left, MapIgpChoice right) => left.Addressable == right.Addressable &&
            left.SelectedPrefabName == right.SelectedPrefabName && left.PrefabObjectName == right.PrefabObjectName;

        private static string Reason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) return "Local map observation stopped.";
            var result = new StringBuilder(Math.Min(reason.Length, MapChoiceFrames.MaxRetireReason));
            foreach (char item in reason)
            {
                if (result.Length >= MapChoiceFrames.MaxRetireReason) break;
                result.Append(char.IsControl(item) || char.IsSurrogate(item) ? '?' : item);
            }
            return result.ToString();
        }

        private void Trace(string stage, object value)
        {
            if (_traces >= MaxTraces) return;
            _traces++;
            NetworkDriver.Logger.LogInfo("DAVECOOP_MAP_CHOICE_" + stage + ": " + JsonSerializer.Serialize(new
            {
                Room = _roomId, Value = value, CandidateEvidence = true, EvidenceOnly = true, HostSelectionApplied = false,
                NativeGenerationBound = false, CrossMachineAddressVerified = false,
                NativeAdoptionImplemented = false, NativeAdoptionEnabled = false
            }));
        }
    }
}
