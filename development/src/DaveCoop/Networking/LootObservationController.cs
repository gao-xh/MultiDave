using System;
using System.Text.Json;
using DaveCoop.Core.World;

namespace DaveCoop.Networking
{
    // This diagnostic belongs to the Unity lifecycle, independently of TCP and
    // fish transmission. Copied native values never become cargo receipts here.
    internal sealed class LootObservationController : IDisposable
    {
        private readonly LootObservationHooks _hooks = new LootObservationHooks();
        private readonly int _unityThreadId;
        private readonly Func<long, HostEntityTarget?> _resolver;
        private LootObservationCapture _capture;
        private bool _running;
        private bool _blocked;
        private float _nextLog;
        private string _warning;

        public LootObservationController(int unityThreadId, Func<long, HostEntityTarget?> resolver)
        {
            _unityThreadId = unityThreadId; _resolver = resolver;
            _capture = new LootObservationCapture(unityThreadId, resolver);
        }

        public bool Installed => _hooks.Installed;
        public bool Healthy => _hooks.Healthy && !_capture.Failed;
        public long Events { get; private set; }
        public long ReadErrors => _capture.ReadErrors;
        public long UnexpectedThreads => _capture.UnexpectedThreads;
        public string Status { get; private set; } = "Loot observer: off";

        public void Update(bool enabled, float now)
        {
            if (!enabled) { Stop(); return; }
            if (_blocked) return;
            try
            {
                if (!_running)
                {
                    // Stop permanently closes a copy queue. The replacement
                    // retains process-wide limits and failure counters.
                    _capture = new LootObservationCapture(_unityThreadId, _resolver);
                    _hooks.Enable(_capture.Capture);
                    _running = true;
                    Status = "Loot observer: read-only";
                    NetworkDriver.Logger.LogInfo("DAVECOOP_LOOT_HOOKS_READY: " + JsonSerializer.Serialize(new
                    {
                        HookCount = 4, ObservationOnly = true, NativeRewardsEnabled = false,
                        ProcessLimit = LootObservationHooks.MaxProcessEvents,
                        QueueLimit = LootObservationCapture.MaxQueued,
                        DrainPerUpdate = LootObservationCapture.MaxDrainPerUpdate
                    }));
                }
                Drain();
                _hooks.CheckHealthy();
                if (_capture.Failed) throw new InvalidOperationException("Loot value copying failed; restart before retrying.");
                if (now >= _nextLog)
                {
                    _nextLog = now + 5;
                    NetworkDriver.Logger.LogInfo("DAVECOOP_LOOT_OBSERVER_STATE: " + JsonSerializer.Serialize(new
                    {
                        _hooks.Installed, _hooks.Healthy, Events, _hooks.ProcessAccepted,
                        _hooks.ProcessLimitReached, _hooks.CallbackErrors,
                        HookDropped = _hooks.Dropped, _hooks.UnmatchedAfter, _hooks.PendingCalls,
                        CopyDropped = _capture.Dropped, _capture.UnexpectedThreads, _capture.ReadErrors,
                        CopyPending = _capture.PendingCount, _capture.Discarded,
                        ObservationOnly = true, ActualBagDeltaProven = false,
                        SourceOperationBound = false, CaptureSuccess = false,
                        StorageDeltaProven = false, NativeRewardsEnabled = false
                    }));
                }
                if (_hooks.ProcessLimitReached)
                {
                    _blocked = true;
                    Stop();
                    Status = _hooks.CleanupVerified
                        ? "Loot observer: process limit reached; restart to observe again"
                        : "Loot observer: cleanup failed; restart required";
                }
            }
            catch (Exception error)
            {
                _blocked = true;
                _warning = error.GetType().Name + ": " + error.Message;
                NetworkDriver.Logger.LogWarning("DAVECOOP_LOOT_OBSERVER_WARNING: " + _warning);
                Stop();
                Status = _hooks.CleanupVerified
                    ? "Loot observer: unavailable; restart after failure"
                    : "Loot observer: cleanup failed; restart required";
            }
        }

        private void Drain()
        {
            int drained = 0;
            while (drained++ < LootObservationCapture.MaxDrainPerUpdate && _capture.TryTake(out LootCallObservation observed))
            {
                Events++;
                NetworkDriver.Logger.LogInfo("DAVECOOP_LOOT_CALL: " + JsonSerializer.Serialize(observed));
            }
        }

        public void Stop()
        {
            bool hadObserver = _running || _hooks.Installed || !_hooks.CleanupVerified;
            _running = false;
            try { _hooks.Dispose(); }
            catch (Exception error)
            {
                _blocked = true;
                Status = "Loot observer: cleanup failed; restart required";
                string message = error.GetType().Name + ": " + error.Message;
                if (_warning != message)
                    NetworkDriver.Logger.LogWarning("DAVECOOP_LOOT_OBSERVER_WARNING: " + message);
                _warning = message;
            }
            finally { _capture.Stop(); }
            if (!_blocked) Status = "Loot observer: off";
            if (hadObserver)
                NetworkDriver.Logger.LogInfo("DAVECOOP_LOOT_HOOKS_STOPPED: " + JsonSerializer.Serialize(new
                {
                    OwnHooksRemoved = _hooks.CleanupVerified, _hooks.CallbackErrors,
                    _hooks.DiscardedCalls, _capture.Discarded, Events,
                    ObservationOnly = true, NativeRewardsEnabled = false
                }));
        }

        public void Dispose() { Stop(); }
    }
}
