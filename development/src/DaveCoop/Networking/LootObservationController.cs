using System;
using System.Text.Json;
using System.Threading;
using DaveCoop.Core.World;

namespace DaveCoop.Networking
{
    // Independent of TCP/Transmit and the cargo ledger. These copied native
    // observations never become CargoFacts, receipts or employee permissions.
    internal sealed class LootObservationController : IDisposable
    {
        public const int MaxStateLogs = 128;
        public const int MaxLifecycleLogs = 128;
        private static long _processStateLogs, _processLifecycleLogs;
        private LootObservationHooks _hooks = new LootObservationHooks();
        private readonly int _unityThreadId;
        private readonly Func<long, HostEntityTarget?> _resolver;
        private LootObservationCapture _capture;
        private bool _running, _blocked, _stopRequested;
        private float _nextLog;
        private string _warning;

        public LootObservationController(int unityThreadId, Func<long, HostEntityTarget?> resolver)
        {
            _unityThreadId = unityThreadId; _resolver = resolver;
            _capture = new LootObservationCapture(unityThreadId, resolver);
        }
        public bool Installed => _hooks.Installed;
        public bool Healthy => _hooks.Healthy && _capture.Healthy;
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
                    // A verified stop gets fresh single-use hook/capture/Core
                    // instances. Global quotas and failures never reset.
                    if (!_hooks.CleanupVerified) throw new InvalidOperationException("Loot cleanup remains unknown.");
                    _hooks = new LootObservationHooks();
                    _capture = new LootObservationCapture(_unityThreadId, _resolver);
                    _stopRequested = false;
                    _hooks.Enable(_capture.Capture, _capture.Invalidate);
                    _running = true; _nextLog = now;
                    Status = "Loot observer: synchronous candidates";
                    LifecycleLog("DAVECOOP_LOOT_HOOKS_READY: ", new
                    {
                        HookCount = LootObservationHooks.TargetCount, _capture.RunId,
                        ProcessLimit = LootObservationHooks.MaxProcessEvents,
                        QueueLimit = LootObservationCapture.MaxQueued, ContextLimit = LootObservationHooks.MaxPendingCalls,
                        FishOrdinalLimit = LootObservationCapture.MaxFishOrdinals,
                        DrainPerUpdate = LootObservationCapture.MaxDrainPerUpdate,
                        KeyUtf16Limit = LootObservationCapture.MaxKeyUtf16,
                        ProcessKeyUtf16Limit = LootObservationCapture.MaxProcessKeyUtf16,
                        ResourceReadLimitPerPrefix = LootObservationCapture.MaxResourceReadsPerPrefix,
                        ProcessResourceReadLimit = LootObservationCapture.MaxProcessResourceReads,
                        ResourceSampleStage = "Before", SupportedResourceClasses = new[] { "DR.Items", "IntegratedItem" },
                        StateLogLimit = MaxStateLogs, LifecycleLogLimit = MaxLifecycleLogs,
                        ObservedSynchronousEnclosureOnly = true, DirectCallerVerified = false,
                        ObservationOnly = true, SourceOperationBound = false, FullYield = false,
                        NativeGenerationVerified = false, CaptureSuccess = false, NativeRewardsEnabled = false,
                        FinalGradeVerified = false, EffectiveWeightVerified = false, ResourceProductMappingVerified = false,
                        CargoPermission = false
                    });
                }
                // A missing hook/queue gap first revokes Core evidence. Only
                // historical diagnostics are then drained, never a live chain.
                _hooks.CheckHealthy();
                if (!_capture.Healthy) throw new InvalidOperationException("Loot caller evidence is unavailable.");
                Drain();
                if (now >= _nextLog) { _nextLog = now + 5; LogState(false); }
                if (_hooks.ProcessLimitReached)
                {
                    _capture.Invalidate("Loot process quota exhausted; synchronous evidence retired.");
                    _blocked = true; Stop();
                    Status = _hooks.CleanupVerified ? "Loot observer: process limit; restart required" : "Loot observer: cleanup unknown; restart required";
                }
            }
            catch (Exception error)
            {
                _capture.Invalidate("Loot hooks/capture unhealthy; prior records are historical candidates only.");
                _blocked = true;
                // Fixed category or CLR type only; no exception message/path.
                Warn(error.GetType().Name);
                Drain();
                Stop();
                Status = _hooks.CleanupVerified ? "Loot observer: unavailable; restart required" : "Loot observer: cleanup unknown; restart required";
            }
        }
        private void Drain()
        {
            int drained = 0;
            while (drained++ < LootObservationCapture.MaxDrainPerUpdate && _capture.TryTake(out LootCallObservation observed))
            {
                Events++;
                NetworkDriver.Logger.LogInfo("DAVECOOP_LOOT_CALL: " + JsonSerializer.Serialize(new
                {
                    CurrentLineageHealthy = _capture.Healthy && _hooks.Healthy,
                    CurrentLineageIntegrityLost = _capture.Lineage.IntegrityLost,
                    Observed = observed
                }));
            }
        }
        private void LogState(bool terminal)
        {
            // Reserve the final process slot for an explicit stop/failure.
            if (!Claim(ref _processStateLogs, terminal ? MaxStateLogs : MaxStateLogs - 1)) return;
            var lineage = _capture.Lineage;
            NetworkDriver.Logger.LogInfo("DAVECOOP_LOOT_OBSERVER_STATE: " + JsonSerializer.Serialize(new
            {
                Terminal = terminal, _capture.RunId, Installed, Healthy, Events,
                _hooks.ProcessAccepted, _hooks.ProcessLimitReached, _hooks.CallbackErrors,
                HookDropped = _hooks.Dropped, _hooks.UnmatchedAfter, _hooks.PendingCalls,
                CopyDropped = _capture.Dropped, _capture.UnexpectedThreads, _capture.ReadErrors,
                CopyPending = _capture.PendingCount, _capture.PendingContexts, _capture.Discarded,
                _capture.FishOrdinalCount, _capture.ProcessKeyUtf16, _capture.ProcessResourceReads,
                LineageHealthy = lineage.Healthy, lineage.IntegrityLost, lineage.Reason,
                lineage.HighestCallId, lineage.LastSequence, lineage.RunEvents, lineage.PendingCount,
                lineage.Unmatched, lineage.ReplayRejected, lineage.OriginalExceptions, lineage.WrongThreadCalls,
                LineageDropped = lineage.Dropped, lineage.DiscardedQueue, lineage.DiscardedPending,
                StateLogs = Interlocked.Read(ref _processStateLogs), LifecycleLogs = Interlocked.Read(ref _processLifecycleLogs),
                ObservationOnly = true, DirectCallerVerified = false, ActualBagDeltaProven = false,
                NativeSourceOperationBound = false, SourceOperationBound = false, MemberOwnershipVerified = false,
                FullYield = false, YieldComplete = false, NativeGenerationVerified = false,
                NativeHookAbiVerified = false, CaptureSuccess = false, StorageDeltaProven = false, NativeRewardsEnabled = false,
                FinalGradeVerified = false, EffectiveWeightVerified = false, ResourceProductMappingVerified = false, CargoPermission = false
            }));
        }
        private static void LifecycleLog(string marker, object value)
        {
            if (Claim(ref _processLifecycleLogs, MaxLifecycleLogs)) NetworkDriver.Logger.LogInfo(marker + JsonSerializer.Serialize(value));
        }
        private void Warn(string category)
        {
            if (_warning == category) return;
            _warning = category;
            if (Claim(ref _processLifecycleLogs, MaxLifecycleLogs)) NetworkDriver.Logger.LogWarning("DAVECOOP_LOOT_OBSERVER_WARNING: " + category);
        }
        private static bool Claim(ref long used, int maximum)
        {
            while (true)
            {
                long current = Interlocked.Read(ref used);
                if (current >= maximum) return false;
                if (Interlocked.CompareExchange(ref used, current + 1, current) == current) return true;
            }
        }
        public void Stop()
        {
            if (_stopRequested) return; // unknown own cleanup is never retried
            _stopRequested = true;
            bool hadObserver = _running || _hooks.Installed || !_hooks.CleanupVerified;
            _running = false;
            _capture.Stop(); // revoke enclosure before any patch removal/drain
            try { _hooks.Dispose(); }
            catch (Exception error)
            {
                _blocked = true;
                Status = "Loot observer: cleanup unknown; restart required";
                Warn(error.GetType().Name);
            }
            if (!_blocked) Status = "Loot observer: off";
            if (hadObserver)
            {
                LogState(true);
                LifecycleLog("DAVECOOP_LOOT_HOOKS_STOPPED: ", new
                {
                    OwnHooksRemoved = _hooks.CleanupVerified, _hooks.CallbackErrors,
                    _hooks.DiscardedCalls, _capture.Discarded, Events,
                    _capture.RunId, LineageRetired = _capture.Lineage.IntegrityLost,
                    ObservationOnly = true, NativeRewardsEnabled = false, SourceOperationBound = false,
                    FullYield = false, CaptureSuccess = false
                });
            }
        }
        public void Dispose() => Stop();
    }
}
