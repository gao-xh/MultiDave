using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using DaveCoop.Core.Session;
using DaveCoop.Core.World;

namespace DaveCoop.Networking
{
    // Transport of scalar candidates only; a synthetic caller does not gain
    // native authority by supplying the same values as the origin registry.
    internal sealed class MapChoiceController
    {
        private const int MaxTraces = 1024, MaxReceivedPerUpdate = 8;
        public const int MaxPublishedPerFrame = 8, MaxOriginRuns = 64;
        private readonly Dictionary<Guid, RunFence> _runs = new Dictionary<Guid, RunFence>();
        private readonly Dictionary<(int Scene, string Address), MapOriginChoiceEvidence> _inventory = new Dictionary<(int, string), MapOriginChoiceEvidence>();
        private readonly Dictionary<(int Scene, string Address), MapIgpChoice> _publishedChoices = new Dictionary<(int, string), MapIgpChoice>();
        private readonly Dictionary<long, MapOriginChoiceEvidence> _controllerHistory = new Dictionary<long, MapOriginChoiceEvidence>();
        private readonly HashSet<long> _removedControllers = new HashSet<long>();
        private SessionPeer _main;
        private string _roomId, _sourceFingerprint;
        private MapRouteSelection _sourceRoute;
        private long _sourceGeneration, _sourceOwner, _callbackFloor, _highestCallback;
        private Guid _currentRun, _sourceRun;
        private Guid _historyRun;
        private long _historyOwner;
        private bool _runQuotaFailed;
        private MapChoiceSnapshot _remote;
        private int _traces;
        private sealed class RunFence { public long HighestOwner, RetiredOwner; public bool Retired; }

        public long PublishedRoutes { get; private set; }
        public long PublishedChoices { get; private set; }
        public long ReceivedSnapshots { get; private set; }
        public long UnboundChoices { get; private set; }
        public long DroppedObservations { get; private set; }
        public long InvalidObservations { get; private set; }
        public long DuplicateObservations { get; private set; }
        public long PublicationErrors { get; private set; }
        public long SuppressedLegacyObservations { get; private set; }
        public long CallbackFloor => _callbackFloor;
        public long HighestCallback => _highestCallback;
        public long SourceGeneration => _sourceGeneration;
        public string SourceFingerprint => _sourceFingerprint;
        public Guid SourceOriginRunId => _sourceRun;
        public long SourceOriginOwnerLife => _sourceOwner;
        public int PendingOriginChoices => _inventory.Count(item => !_publishedChoices.TryGetValue(item.Key, out MapIgpChoice previous) || !SameSelection(previous, item.Value.Choice));
        public long RemoteGeneration => _remote?.Generation ?? 0;
        public int RemoteRouteSceneCount => _remote?.Route?.Scenes?.Length ?? 0;
        public int RemoteChoiceCount => _remote?.Choices?.Length ?? 0;
        public string Status { get; private set; } = "Map candidate evidence only; adoption not implemented.";

        public void BindRoom(SessionPeer main, long callbackFloor) => BindRoom(main, callbackFloor, Guid.Empty, 0);
        public void BindRoom(SessionPeer main, long callbackFloor, Guid originRunFloor, long originOwnerFloor)
        {
            if (originOwnerFloor < 0 || (originRunFloor == Guid.Empty && originOwnerFloor != 0)) throw new ArgumentException("Invalid origin room floor.");
            Clear(callbackFloor);
            if (originRunFloor != Guid.Empty)
            {
                RunFence fence = FindRun(originRunFloor);
                if (fence != null)
                {
                    fence.HighestOwner = Math.Max(fence.HighestOwner, originOwnerFloor);
                    fence.RetiredOwner = Math.Max(fence.RetiredOwner, originOwnerFloor);
                }
                if (_currentRun != Guid.Empty && _currentRun != originRunFloor && _runs.TryGetValue(_currentRun, out RunFence previous)) previous.Retired = true;
                _currentRun = originRunFloor;
            }
            if (main == null) return;
            SessionSnapshot state = main.Snapshot;
            if (state.Phase == SessionPhase.Closed || string.IsNullOrEmpty(state.RoomId)) return;
            _main = main; _roomId = state.RoomId;
            Status = state.Role == SessionRole.Host ? "Awaiting a fresh fixed-origin map candidate; adoption not implemented." : "Awaiting host map candidate evidence; adoption not implemented.";
            Trace("BOUND", new { CallbackFloor = _callbackFloor, OriginRunFloor = originRunFloor, OriginOwnerFloor = originOwnerFloor, Role = state.Role.ToString() });
        }

        // The old observer keeps independent watermarks and diagnostics. It
        // can neither publish nor retract a candidate from the origin source.
        public void Observe(MapSelectionCallObservation observation, SessionPeer main)
        {
            if (observation == null) return;
            SuppressedLegacyObservations++;
            long sequence = observation.ProcessSequence;
            if (sequence <= _callbackFloor || sequence <= _highestCallback) { DroppedObservations++; return; }
            _highestCallback = sequence;
            if (!MatchesRoom(main, out SessionSnapshot state) || state.Role != SessionRole.Host) { DroppedObservations++; return; }
            if (observation.Stage == "IgpSelectedAfter" || observation.Stage == "IgpPrefabFactoryBefore") UnboundChoices++;
            Trace("LEGACY_SUPPRESSED", new { sequence, observation.Stage, Reason = "Legacy callback has no fixed origin source; publication and retirement are suppressed." });
        }

        public void ObserveOrigin(MapOriginSourceFrame frame, SessionPeer main)
        {
            if (!MatchesRoom(main, out SessionSnapshot state) || state.Role != SessionRole.Host) { DroppedObservations++; return; }
            if (_runQuotaFailed) { RetireSource(main, "Origin run tombstone capacity was exhausted."); return; }
            if (frame == null || frame.RunId == Guid.Empty)
            {
                // A disabled observer normally has no run. It still revokes a
                // source, without reporting one invalid sample every Update.
                if (frame == null || frame.Healthy) InvalidObservations++;
                SealCurrentRun(); RetireSource(main, "Origin observer is unavailable."); return;
            }
            RunFence run = FindRun(frame.RunId);
            if (run == null) { RetireSource(main, "Origin run tombstone capacity was exhausted."); return; }
            if (run.Retired) { DroppedObservations++; return; }
            if (_currentRun != frame.RunId)
            {
                SealCurrentRun(); RetireSource(main, "Origin observer run changed.");
                _currentRun = frame.RunId;
            }
            if (!frame.Healthy || frame.Source == null)
            { run.Retired = true; RetireSource(main, "Origin observer stopped or failed; its candidate is revoked."); return; }
            MapOriginSourceSnapshot supplied = frame.Source;
            long owner = supplied.OwnerLife;
            if (owner < 0) { InvalidOrigin(main, run, owner, "Origin owner identity is invalid."); return; }
            if (owner == 0)
            {
                run.RetiredOwner = Math.Max(run.RetiredOwner, run.HighestOwner);
                if (supplied.Route != null || (supplied.Choices != null && supplied.Choices.Length != 0)) InvalidObservations++;
                RetireSource(main, "Origin entry ended; no active owner."); return;
            }
            if (owner <= run.RetiredOwner || owner < run.HighestOwner) { DroppedObservations++; return; }
            if (owner > run.HighestOwner)
            {
                if (run.HighestOwner != 0) run.RetiredOwner = Math.Max(run.RetiredOwner, run.HighestOwner);
                run.HighestOwner = owner; RetireSource(main, "Natural origin entry changed.");
            }
            if (supplied.Route == null)
            {
                if (_sourceRoute != null) run.RetiredOwner = Math.Max(run.RetiredOwner, owner);
                if (supplied.Choices == null || supplied.Choices.Length != 0 || supplied.RouteFingerprint != null)
                { InvalidOrigin(main, run, owner, "An incomplete route cannot carry choices or a route fingerprint."); return; }
                RetireSource(main, "Origin entry has not bound a complete route."); return;
            }
            MapRouteSelection route;
            Dictionary<(int Scene, string Address), MapOriginChoiceEvidence> owned;
            if (_historyRun != frame.RunId || _historyOwner != owner)
            {
                _controllerHistory.Clear(); _removedControllers.Clear(); _historyRun = frame.RunId; _historyOwner = owner;
            }
            try
            {
                route = MapSelections.CopyRoute(supplied.Route);
                if (supplied.RouteFingerprint != MapSelections.FingerprintRoute(route)) throw new ArgumentException("Source route fingerprint disagrees with its fields.");
                owned = CopyInventory(supplied, route);
                if (_sourceRun == frame.RunId && _sourceOwner == owner && _sourceFingerprint != supplied.RouteFingerprint) throw new ArgumentException("A fixed origin owner cannot change its route.");
                ValidateContinuity(owned);
                if (_controllerHistory.Keys.Union(owned.Values.Select(item => item.ControllerLife)).Count() > MapOriginRegistry.MaxControllers)
                    throw new ArgumentException("Controller history reached its non-evicting origin bound.");
            }
            catch (ArgumentException error) { InvalidOrigin(main, run, owner, error.Message); return; }
            if (_sourceRoute != null && !MatchesSource(state))
            { InvalidOrigin(main, run, owner, "The wire generation was canceled; it is not silently restarted."); return; }
            bool replacement = _sourceRoute != null && _inventory.Any(previous => !owned.TryGetValue(previous.Key, out MapOriginChoiceEvidence next) || next.ControllerLife != previous.Value.ControllerLife);
            var liveControllers = new HashSet<long>(owned.Values.Select(item => item.ControllerLife));
            foreach (MapOriginChoiceEvidence previous in _inventory.Values)
                if (!liveControllers.Contains(previous.ControllerLife)) _removedControllers.Add(previous.ControllerLife);
            if (replacement) RetireSource(main, "Eligible inventory removed or replaced a controller; rebuilding the candidate generation.");
            if (_sourceRoute == null && !PublishRoute(main, route, supplied.RouteFingerprint, frame.RunId, owner))
            { run.RetiredOwner = Math.Max(run.RetiredOwner, owner); return; }
            _inventory.Clear();
            foreach (var item in owned)
            {
                _inventory.Add(item.Key, item.Value);
                _controllerHistory[item.Value.ControllerLife] = item.Value;
            }
            PublishPending(main, run);
        }

        private Dictionary<(int Scene, string Address), MapOriginChoiceEvidence> CopyInventory(MapOriginSourceSnapshot supplied, MapRouteSelection route)
        {
            if (supplied.Choices == null || supplied.Choices.Length > MapChoiceFrames.MaxChoices) throw new ArgumentException("Origin inventory exceeds the wire schema or is missing.");
            var scenes = route.Scenes.ToDictionary(item => item.SceneId);
            var result = new Dictionary<(int, string), MapOriginChoiceEvidence>();
            var controllers = new HashSet<long>();
            var chains = new Dictionary<int, MapOriginChoiceEvidence>();
            var handles = new Dictionary<int, int>(); var lives = new Dictionary<long, int>(); var operations = new Dictionary<long, int>();
            long context = 0;
            foreach (MapOriginChoiceEvidence item in supplied.Choices)
            {
                if (item == null || item.Choice == null || item.OwnerLife != supplied.OwnerLife || item.RouteFingerprint != supplied.RouteFingerprint ||
                    item.ContextPointer == 0 || item.OperationLife < 1 || item.SceneLife < 1 || item.SceneHandle == 0 || item.ControllerLife < 1 ||
                    item.CallbackSequence < 1 || string.IsNullOrWhiteSpace(item.LoadKey) || item.LoadKey.Length > MapOriginRegistry.MaxLoadKey ||
                    item.LoadKey.Any(char.IsControl) || !scenes.TryGetValue(item.Choice.SceneId, out MapRouteScene scene) || scene.SceneName != item.ControllerSceneName)
                    throw new ArgumentException("Choice lacks a complete owner/operation/actual-scene/controller chain.");
                if (!controllers.Add(item.ControllerLife)) throw new ArgumentException("A live controller appears twice.");
                if (context != 0 && context != item.ContextPointer) throw new ArgumentException("One owner has several context identities.");
                context = item.ContextPointer;
                if (chains.TryGetValue(scene.SceneId, out MapOriginChoiceEvidence prior) && !SameSceneChain(prior, item)) throw new ArgumentException("One scene has conflicting operation or scene lives.");
                chains[scene.SceneId] = item;
                CheckSceneIdentity(handles, item.SceneHandle, scene.SceneId); CheckSceneIdentity(lives, item.SceneLife, scene.SceneId); CheckSceneIdentity(operations, item.OperationLife, scene.SceneId);
                MapChoiceFrames.Validate(Wire(item, 1, supplied.RouteFingerprint, 1));
                var key = (item.Choice.SceneId, item.Choice.ControllerAddress);
                if (result.ContainsKey(key)) throw new ArgumentException("Several live controllers share one scene/address; address does not establish origin.");
                result.Add(key, CopyEvidence(item));
            }
            return result;
        }
        private static void CheckSceneIdentity<T>(Dictionary<T, int> table, T life, int scene)
        {
            if (table.TryGetValue(life, out int previous) && previous != scene) throw new ArgumentException("Exact scene or operation identity belongs to several route scenes.");
            table[life] = scene;
        }
        private void ValidateContinuity(Dictionary<(int Scene, string Address), MapOriginChoiceEvidence> owned)
        {
            foreach (MapOriginChoiceEvidence item in owned.Values)
            {
                if (_removedControllers.Contains(item.ControllerLife)) throw new ArgumentException("A removed controller life cannot be revived by a later frame or address reuse.");
                if (!_controllerHistory.TryGetValue(item.ControllerLife, out MapOriginChoiceEvidence previous)) continue;
                if (!SameSceneChain(previous, item) || previous.ContextPointer != item.ContextPointer || previous.ControllerSceneName != item.ControllerSceneName) throw new ArgumentException("A live controller changed its fixed scalar chain.");
                if (item.CallbackSequence < previous.CallbackSequence || (item.CallbackSequence == previous.CallbackSequence &&
                    (item.Choice.ControllerAddress != previous.Choice.ControllerAddress || !SameSelection(Wire(previous, 1, previous.RouteFingerprint, 1), item.Choice))))
                    throw new ArgumentException("An old or conflicting controller selection was replayed.");
            }
        }
        private bool PublishRoute(SessionPeer main, MapRouteSelection route, string fingerprint, Guid run, long owner)
        {
            try
            {
                SessionSnapshot before = main.Snapshot;
                if (!main.PublishMapRoute(route)) { RetireSource(main, "Route publication was canceled."); return false; }
                SessionSnapshot after = main.Snapshot;
                if (after.Phase == SessionPhase.Closed || after.RoomId != _roomId || after.MapChoiceGeneration <= before.MapChoiceGeneration || after.MapChoiceFingerprint != fingerprint)
                { RetireSource(main, "Route publication no longer has a matching room generation."); return false; }
                ResetSource(); _sourceRoute = route; _sourceFingerprint = fingerprint; _sourceGeneration = after.MapChoiceGeneration;
                _sourceRun = run; _sourceOwner = owner; PublishedRoutes++;
                Status = "Host fixed-origin route candidate evidence sent; adoption not implemented.";
                Trace("ROUTE_SENT", new { OriginRun = run, OwnerLife = owner, Generation = _sourceGeneration, RouteFingerprint = fingerprint, route.EntrySceneId, SceneCount = route.Scenes.Length });
                return true;
            }
            catch (Exception error) { PublicationErrors++; RetireSource(main, "Map candidate publication failed."); Trace("PUBLISH_ERROR", new { Error = Reason(error.GetType().Name + ": " + error.Message) }); return false; }
        }
        private void PublishPending(SessionPeer main, RunFence run)
        {
            int sent = 0;
            foreach (var item in _inventory.OrderBy(item => item.Value.ControllerLife).ToArray())
            {
                if (_publishedChoices.TryGetValue(item.Key, out MapIgpChoice previous) && SameSelection(previous, item.Value.Choice)) continue;
                if (sent >= MaxPublishedPerFrame) break;
                SessionSnapshot state = main.Snapshot;
                if (!MatchesSource(state) || state.MapChoiceRevision == long.MaxValue) { InvalidOrigin(main, run, _sourceOwner, "The source or its wire revision is unavailable."); return; }
                MapIgpChoice choice = Wire(item.Value, _sourceGeneration, _sourceFingerprint, state.MapChoiceRevision + 1);
                try
                {
                    if (!main.PublishMapIgpChoice(choice))
                    {
                        long owner = _sourceOwner; run.RetiredOwner = Math.Max(run.RetiredOwner, owner);
                        RetireSource(main, "Choice publication canceled or overflowed; the origin candidate will not be replayed.");
                        Trace("CHOICE_CANCELED", new { choice.Generation, choice.Revision, OwnerLife = owner }); return;
                    }
                    _publishedChoices[item.Key] = choice; PublishedChoices++; sent++;
                    Status = "Host fixed-origin IGP candidate evidence sent; adoption not implemented.";
                    Trace("CHOICE_SENT", new { OriginRun = _sourceRun, OwnerLife = _sourceOwner, item.Value.ControllerLife, Choice = choice });
                }
                catch (Exception error)
                {
                    PublicationErrors++; run.RetiredOwner = Math.Max(run.RetiredOwner, _sourceOwner);
                    RetireSource(main, "Map choice publication failed."); Trace("PUBLISH_ERROR", new { Error = Reason(error.GetType().Name + ": " + error.Message) }); return;
                }
            }
            if (sent == 0 && PendingOriginChoices == 0) DuplicateObservations++;
        }

        public void Update(SessionPeer main, SessionPeer loopback)
        {
            if (!MatchesRoom(main, out SessionSnapshot state)) return;
            if (_sourceRoute != null && !MatchesSource(state)) { SealCurrentOwner(); ResetSource(); Status = "Map candidate source canceled; adoption not implemented."; }
            SessionPeer receiver = state.Role == SessionRole.Guest ? main : loopback;
            if (receiver == null) return;
            SessionSnapshot receiving = receiver.Snapshot;
            if (receiving.Role != SessionRole.Guest || receiving.Phase == SessionPhase.Closed || receiving.RoomId != _roomId) return;
            for (int i = 0; i < MaxReceivedPerUpdate && receiver.TryTakeRemoteMapChoices(out MapChoiceSnapshot evidence); i++)
            {
                _remote = evidence; ReceivedSnapshots++;
                Status = evidence.Retired ? "Host map candidate evidence retired; adoption not implemented." : evidence.Route == null ? "Host map candidate route assembling; adoption not implemented." : "Host map candidate evidence received; adoption not implemented.";
                Trace("RECEIVED", new { evidence.Generation, evidence.RouteFingerprint, evidence.LastChoiceRevision, evidence.Retired, RouteComplete = evidence.Route != null, SceneCount = RemoteRouteSceneCount, ChoiceCount = RemoteChoiceCount });
            }
        }
        public void Retire(SessionPeer main, long floor, string reason) { AdvanceFloor(floor); SealCurrentOwner(); RetireSource(main, reason); }
        private void RetireSource(SessionPeer main, string reason)
        {
            bool hadSource = _sourceRoute != null;
            ResetSource(); bool sent = false; string boundedReason = Reason(reason);
            try
            {
                if (MatchesRoom(main, out SessionSnapshot state) && state.Role == SessionRole.Host && state.MapChoiceFingerprint != null) sent = main.RetireMapChoices(boundedReason);
            }
            catch (Exception error) { PublicationErrors++; Trace("RETIRE_ERROR", new { Error = Reason(error.GetType().Name + ": " + error.Message) }); }
            Status = _remote != null && !_remote.Retired && _remote.Route != null ? "Host map candidate evidence retained; local observer stopped; adoption not implemented." : "Local map candidate source retired; adoption not implemented.";
            if (hadSource || sent) Trace("RETIRED", new { CallbackFloor = _callbackFloor, Reason = boundedReason, Sent = sent });
        }
        public void Clear(long floor = 0)
        {
            AdvanceFloor(floor); SealCurrentOwner(); ResetSource(); _remote = null; _main = null; _roomId = null;
            Status = "Map candidate evidence only; adoption not implemented.";
        }
        private void AdvanceFloor(long floor) => _callbackFloor = Math.Max(_callbackFloor, Math.Max(_highestCallback, floor));
        private RunFence FindRun(Guid run)
        {
            if (_runs.TryGetValue(run, out RunFence known)) return known;
            if (_runs.Count >= MaxOriginRuns) { _runQuotaFailed = true; InvalidObservations++; SealCurrentRun(); return null; }
            var created = new RunFence(); _runs.Add(run, created); return created;
        }
        private void SealCurrentOwner() { if (_currentRun != Guid.Empty && _runs.TryGetValue(_currentRun, out RunFence run)) run.RetiredOwner = Math.Max(run.RetiredOwner, run.HighestOwner); }
        private void SealCurrentRun() { if (_currentRun != Guid.Empty && _runs.TryGetValue(_currentRun, out RunFence run)) { run.Retired = true; run.RetiredOwner = Math.Max(run.RetiredOwner, run.HighestOwner); } }
        private void InvalidOrigin(SessionPeer main, RunFence run, long owner, string reason)
        {
            InvalidObservations++; run.RetiredOwner = Math.Max(run.RetiredOwner, Math.Max(run.HighestOwner, owner));
            RetireSource(main, "Origin snapshot unusable: " + Reason(reason)); Trace("ORIGIN_UNUSABLE", new { OwnerLife = owner, Reason = Reason(reason) });
        }
        private bool MatchesRoom(SessionPeer main, out SessionSnapshot state)
        {
            state = main?.Snapshot;
            return ReferenceEquals(main, _main) && _roomId != null && state != null && state.Phase != SessionPhase.Closed && state.RoomId == _roomId;
        }
        private bool MatchesSource(SessionSnapshot state) => _sourceRoute != null && state.MapChoiceGeneration == _sourceGeneration && state.MapChoiceFingerprint == _sourceFingerprint;
        private void ResetSource()
        {
            _sourceRoute = null; _sourceFingerprint = null; _sourceGeneration = 0; _sourceRun = Guid.Empty; _sourceOwner = 0;
            _inventory.Clear(); _publishedChoices.Clear();
        }
        private static bool SameSceneChain(MapOriginChoiceEvidence left, MapOriginChoiceEvidence right) => left.OwnerLife == right.OwnerLife && left.OperationLife == right.OperationLife && left.LoadKey == right.LoadKey && left.SceneLife == right.SceneLife && left.SceneHandle == right.SceneHandle && left.Choice.SceneId == right.Choice.SceneId;
        private static bool SameSelection(MapIgpChoice left, MapGroupSelection right) => left.Addressable == right.Addressable && (left.SelectedPrefabName ?? "") == (right.SelectedPrefabName ?? "") && (left.PrefabObjectName ?? "") == (right.PrefabObjectName ?? "");
        private static MapIgpChoice Wire(MapOriginChoiceEvidence item, long generation, string fingerprint, long revision) => new MapIgpChoice
        {
            Generation = generation, RouteFingerprint = fingerprint, Revision = revision, SceneId = item.Choice.SceneId, ControllerAddress = item.Choice.ControllerAddress,
            Addressable = item.Choice.Addressable, SelectedPrefabName = item.Choice.SelectedPrefabName, PrefabObjectName = item.Choice.PrefabObjectName
        };
        private static MapOriginChoiceEvidence CopyEvidence(MapOriginChoiceEvidence item) => new MapOriginChoiceEvidence
        {
            OwnerLife = item.OwnerLife, ContextPointer = item.ContextPointer, RouteFingerprint = item.RouteFingerprint, OperationLife = item.OperationLife, LoadKey = item.LoadKey,
            SceneLife = item.SceneLife, SceneHandle = item.SceneHandle, ControllerLife = item.ControllerLife, ControllerSceneName = item.ControllerSceneName, CallbackSequence = item.CallbackSequence,
            Choice = new MapGroupSelection { SceneId = item.Choice.SceneId, ControllerAddress = item.Choice.ControllerAddress, Addressable = item.Choice.Addressable, SelectedPrefabName = item.Choice.SelectedPrefabName ?? "", PrefabObjectName = item.Choice.PrefabObjectName ?? "" }
        };
        private static string Reason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) return "Local map observation stopped.";
            var result = new StringBuilder(Math.Min(reason.Length, MapChoiceFrames.MaxRetireReason));
            foreach (char item in reason) { if (result.Length >= MapChoiceFrames.MaxRetireReason) break; result.Append(char.IsControl(item) || char.IsSurrogate(item) ? '?' : item); }
            return result.ToString();
        }
        private void Trace(string stage, object value)
        {
            if (_traces >= MaxTraces) return;
            _traces++;
            NetworkDriver.Logger.LogInfo("DAVECOOP_MAP_CHOICE_" + stage + ": " + JsonSerializer.Serialize(new
            {
                Room = _roomId, Value = value, CandidateEvidence = true, EvidenceOnly = true, HostSelectionApplied = false, NativeGenerationBound = false,
                CrossMachineAddressVerified = false, NativeAdoptionImplemented = false, NativeAdoptionEnabled = false
            }));
        }
    }
}
