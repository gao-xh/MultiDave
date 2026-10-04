using System;
using System.Text.Json;
using DaveCoop.Core.World;

namespace DaveCoop.Networking
{
    // Unity-thread diagnostic only. The origin registry has its own lifetime;
    // its local identifiers are not session epochs or network authority facts.
    internal sealed class MapOriginController : IDisposable
    {
        private readonly int _unityThreadId;
        private readonly MapOriginHooks _hooks = new MapOriginHooks();
        private MapOriginRegistry _registry;
        private MapOriginNativeCapture _capture;
        private Guid _runId;
        private bool _running, _blocked;
        private float _nextLog;
        private string _warning;

        public MapOriginController(int unityThreadId)
        {
            if (unityThreadId < 1 || Environment.CurrentManagedThreadId != unityThreadId)
                throw new ArgumentException("Map origin controller needs the confirmed Unity thread.");
            _unityThreadId = unityThreadId;
        }

        public bool Installed => _hooks.Installed;
        public bool Healthy => _running && _hooks.Healthy && _registry != null && _registry.Healthy &&
            _capture != null && !_capture.Failed && !TraceLost;
        public long Events { get; private set; }
        public long BoundChoices { get; private set; }
        public long ReadErrors => _capture?.ReadErrors ?? 0;
        public long UnexpectedThreads => _capture?.UnexpectedThreads ?? 0;
        public Guid RunId => _runId;
        public long ActiveOwnerLife => _registry?.ActiveOwnerLife ?? 0;
        public string Status { get; private set; } = "Map origin observer: off";
        private bool TraceLost => _hooks.Dropped != 0 || _hooks.UnmatchedAfter != 0 ||
            _hooks.ProcessLimitReached || (_capture != null &&
                (_capture.Dropped != 0 || _capture.ReadErrors != 0 || _capture.UnexpectedThreads != 0));

        public MapOriginSourceFrame CaptureSourceFrame()
        {
            var frame = new MapOriginSourceFrame { RunId = _runId, Healthy = false };
            // Fresh CLR inventory from the same fixed registry; diagnostic
            // call queues and the old observer's global cache are not sources.
            if (Environment.CurrentManagedThreadId != _unityThreadId || !Healthy) return frame;
            if (!_registry.TryCaptureSource(out MapOriginSourceSnapshot source) || !Healthy) return frame;
            frame.Source = source; frame.Healthy = true;
            return frame;
        }

        public void Update(bool enabled, float now)
        {
            if (!enabled) { Stop(); return; }
            if (_blocked) return;
            try
            {
                if (Environment.CurrentManagedThreadId != _unityThreadId)
                    throw new InvalidOperationException("Origin observer update changed Unity thread.");
                if (!_running) Start();
                RequireHealthy();
                _capture.PollOperations();
                RequireHealthy();
                DrainCalls();
                for (int i = 0; i < MapOriginNativeCapture.MaxDrainPerUpdate; i++)
                {
                    RequireHealthy();
                    if (!_registry.TryTakeBoundChoice(out MapOriginChoiceEvidence choice)) break;
                    BoundChoices++;
                    // Native pointers stay local in the registry and never
                    // become log identities or cross-machine addresses.
                    NetworkDriver.Logger.LogInfo("DAVECOOP_MAP_ORIGIN_BOUND_CHOICE: " + JsonSerializer.Serialize(new
                    {
                        RunId = _runId, choice.OwnerLife, choice.RouteFingerprint, choice.OperationLife,
                        choice.LoadKey, choice.SceneLife, choice.SceneHandle, choice.ControllerLife,
                        choice.ControllerSceneName, choice.CallbackSequence, choice.Choice,
                        ScalarOriginChainMatched = true, NativeGenerationBound = false,
                        NativeTypedReturnAbiVerified = false, CrossMachineAddressVerified = false,
                        ObservationOnly = true, HostSelectionApplied = false,
                        WorldAuthority = false, CargoAuthority = false
                    }));
                }
                Status = "Map origin observer: read-only; choices " + BoundChoices + ", unbound " + _registry.UnboundChoices;
                if (now >= _nextLog) { _nextLog = now + 5; LogState(); }
            }
            catch (Exception error)
            {
                _blocked = true;
                _registry?.Invalidate("Origin trace is unavailable; copied ownership is revoked.");
                // Preserve a bounded diagnostic sample, never promote choices
                // from a registry after a read, pairing or quota failure.
                DrainCalls();
                Warn(error);
                Stop();
                Status = _hooks.CleanupVerified
                    ? "Map origin observer: trace unavailable; restart required"
                    : "Map origin observer: cleanup failed; restart required";
            }
        }

        private void Start()
        {
            _runId = Guid.NewGuid();
            _registry = new MapOriginRegistry(_unityThreadId);
            _capture = new MapOriginNativeCapture(_unityThreadId, _registry);
            _hooks.Enable(CopyCallback);
            _running = true;
            RequireHealthy();
            NetworkDriver.Logger.LogInfo("DAVECOOP_MAP_ORIGIN_HOOKS_READY: " + JsonSerializer.Serialize(new
            {
                RunId = _runId, HookCount = _hooks.TargetCount,
                ProcessLimit = MapOriginHooks.MaxProcessEvents, CallLimit = MapOriginHooks.MaxPendingCalls,
                QueueLimit = MapOriginNativeCapture.MaxQueued,
                DrainPerUpdate = MapOriginNativeCapture.MaxDrainPerUpdate,
                RetainedOperationLimit = MapOriginNativeCapture.MaxOperations,
                RetainedManagerLimit = MapOriginNativeCapture.MaxManagers,
                ObservationOnly = true, NativeTypedReturnAbiVerified = false,
                NativeGenerationBound = false, HostSelectionApplied = false,
                WorldAuthority = false, CargoAuthority = false
            }));
        }

        private void CopyCallback(MapOriginCallback call)
        {
            // A dropped nested prefix must not let its child inherit an outer
            // scope. Revoke synchronously before any later callback is copied;
            // unpatching itself remains an Update/Stop operation.
            if (TraceLost || _hooks.Failed)
            {
                _registry.Invalidate("Origin callback trace lost a pairing, thread, read or quota boundary.");
                _capture.Stop();
                return;
            }
            _capture.Capture(call);
        }

        private void RequireHealthy()
        {
            _hooks.CheckHealthy();
            if (!Healthy) throw new InvalidOperationException("Origin copying or provenance is incomplete; restart before retrying.");
        }

        private void DrainCalls()
        {
            if (_capture == null) return;
            for (int i = 0; i < MapOriginNativeCapture.MaxDrainPerUpdate &&
                _capture.TryTake(out MapOriginNativeObservation call); i++)
            {
                Events++;
                NetworkDriver.Logger.LogInfo("DAVECOOP_MAP_ORIGIN_CALL: " + JsonSerializer.Serialize(new
                {
                    RunId = _runId, TraceHealthyAtDrain = Healthy, Observation = call
                }));
            }
        }

        private void LogState()
        {
            NetworkDriver.Logger.LogInfo("DAVECOOP_MAP_ORIGIN_OBSERVER_STATE: " + JsonSerializer.Serialize(new
            {
                RunId = _runId, Installed, Healthy, Events, BoundChoices, _hooks.ProcessAccepted,
                _hooks.ProcessLimitReached, _hooks.CallbackErrors,
                HookDropped = _hooks.Dropped, _hooks.UnmatchedAfter, _hooks.PendingCalls,
                CopyDropped = _capture.Dropped, _capture.ReadErrors, _capture.UnexpectedThreads,
                _capture.Discarded, CopyPending = _capture.PendingCount, _capture.RetainedOperations,
                _capture.RetainedManagers, _registry.ManagerCount, _registry.PendingManagerCount,
                _registry.ActiveOwnerLife, _registry.OwnerCount, _registry.IteratorCount,
                _registry.OperationCount, _registry.SceneCount, _registry.SceneHandleCount, _registry.ControllerCount,
                _registry.PendingChoiceCount, _registry.ReadyChoiceCount, _registry.UnboundChoices,
                _registry.FaultReason, ObservationOnly = true, NativeGenerationBound = false,
                NativeTypedReturnAbiVerified = false, HostSelectionApplied = false,
                WorldAuthority = false, CargoAuthority = false
            }));
        }

        private void Warn(Exception error)
        {
            string message = error.GetType().Name + ": " + error.Message;
            if (message == _warning) return;
            _warning = message;
            NetworkDriver.Logger.LogWarning("DAVECOOP_MAP_ORIGIN_OBSERVER_WARNING: " + JsonSerializer.Serialize(new
            {
                RunId = _runId, Error = message, ObservationOnly = true,
                NativeGenerationBound = false, WorldAuthority = false, CargoAuthority = false
            }));
        }

        public void Stop()
        {
            bool hadObserver = _running || _hooks.Installed || !_hooks.CleanupVerified;
            _running = false;
            try { _hooks.Dispose(); }
            catch (Exception error)
            {
                _blocked = true;
                Status = "Map origin observer: cleanup failed; restart required";
                Warn(error);
            }
            finally
            {
                _capture?.Stop();
                _registry?.Invalidate("Origin observer stopped; its evidence is revoked.");
            }
            if (!_blocked) Status = "Map origin observer: off";
            if (hadObserver)
                NetworkDriver.Logger.LogInfo("DAVECOOP_MAP_ORIGIN_HOOKS_STOPPED: " + JsonSerializer.Serialize(new
                {
                    RunId = _runId, OwnHooksRemoved = _hooks.CleanupVerified, _hooks.CallbackErrors,
                    _hooks.DiscardedCalls, CopyDiscarded = _capture?.Discarded ?? 0,
                    CopyPending = _capture?.PendingCount ?? 0,
                    RetainedOperations = _capture?.RetainedOperations ?? 0,
                    RetainedManagers = _capture?.RetainedManagers ?? 0,
                    Events, BoundChoices, EvidenceRevoked = true, ObservationOnly = true,
                    NativeGenerationBound = false, HostSelectionApplied = false,
                    WorldAuthority = false, CargoAuthority = false
                }));
        }

        public void Dispose() { Stop(); }
    }
}
