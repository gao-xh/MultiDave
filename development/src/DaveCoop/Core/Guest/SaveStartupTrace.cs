using System;
using System.Collections.Generic;
using System.Threading;

namespace DaveCoop.Core.Guest
{
    public enum SaveStartupTraceStage
    {
        PluginLoad, HooksInstalled, FirstUnityUpdate, Prefix, Postfix,
        Finalizer, Invalidated, Retired
    }

    // Immutable CLR evidence only. Thread and marker observations are supplied
    // by the adapter; they cannot prove the game had not loaded earlier.
    public sealed class SaveStartupCallObservation
    {
        public Guid RunId { get; }
        public long Sequence { get; }
        public long CallId { get; }
        public long ParentCallId { get; }
        public int Depth { get; }
        public string MethodKey { get; }
        public SaveStartupTraceStage Stage { get; }
        public int ThreadId { get; }
        public double At { get; }
        public bool OriginalException { get; }
        public bool BeforeFirstUnityUpdate { get; }
        public bool UnityThreadCandidate { get; }
        public bool ObservationOnly => true;
        public bool StartEarlyCoverageVerified => false;
        public bool NativeOrderVerified => false;
        public bool FirstLoadSafe => false;
        public bool PathIsolationVerified => false;
        public bool GuestStateIsolated => false;
        public bool NativePermission => false;
        public bool WorldAuthority => false;
        public bool CargoAuthority => false;

        internal SaveStartupCallObservation(Guid runId, long sequence, Call call,
            SaveStartupTraceStage stage, int threadId, double at, bool exception,
            bool beforeUpdate, bool threadCandidate)
        {
            RunId = runId; Sequence = sequence; CallId = call?.Id ?? 0;
            ParentCallId = call?.Parent ?? 0; Depth = call?.Depth ?? 0;
            MethodKey = call?.Key ?? stage.ToString(); Stage = stage;
            ThreadId = threadId; At = at; OriginalException = exception;
            BeforeFirstUnityUpdate = beforeUpdate; UnityThreadCandidate = threadCandidate;
        }

        internal sealed class Call
        {
            public long Id, Parent;
            public int Depth, Thread;
            public string Key;
            public TerminalState State;
        }
        internal enum TerminalState { Pending, Returned, Exception, Abandoned }
    }

    // This registry never reads native objects or paths and never changes an
    // original result. Loss is sticky even after drain. A new instance has a
    // new RunId but cannot reuse process-wide CallIds from old hook __state.
    public sealed class SaveStartupTrace
    {
        public const int MaxPendingCalls = 128;
        public const int MaxEvents = 512;
        public const int MaxRunEvents = 4096;
        public const int MaxMethodKeyLength = 512;
        private static long _processCallId;
        private readonly object _gate = new object();
        private readonly Queue<SaveStartupCallObservation> _events = new Queue<SaveStartupCallObservation>();
        private readonly Dictionary<long, SaveStartupCallObservation.Call> _calls = new Dictionary<long, SaveStartupCallObservation.Call>();
        private readonly Dictionary<int, List<long>> _stacks = new Dictionary<int, List<long>>();
        private bool _faulted, _retired, _integrityLost, _installed, _update, _late;
        private int _unityThread, _pending, _runEvents;
        private long _highestCallId, _sequence, _dropped, _unmatched, _exceptions, _wrongThread, _rejected, _complete, _duplicates, _gaps, _abandoned;
        private double _lastAt;
        private string _reason = "Scalar startup observations do not prove earliest load or isolated paths.";
        public Guid RunId { get; } = Guid.NewGuid();
        public int PluginLoadThreadId { get; }
        public bool PluginLoadObserved { get; }
        public bool Healthy { get { lock (_gate) return !_faulted && !_retired; } }
        public bool IntegrityLost { get { lock (_gate) return _integrityLost; } }
        public string Reason { get { lock (_gate) return _reason; } }
        public bool HooksInstalled { get { lock (_gate) return _installed; } }
        public bool FirstUnityUpdateObserved { get { lock (_gate) return _update; } }
        public bool LateInstallation { get { lock (_gate) return _late; } }
        public int UnityThreadId { get { lock (_gate) return _unityThread; } }
        public int PendingCount { get { lock (_gate) return _pending; } }
        public int QueuedCount { get { lock (_gate) return _events.Count; } }
        public long HighestCallId { get { lock (_gate) return _highestCallId; } }
        public long LastSequence { get { lock (_gate) return _sequence; } }
        public int RunEvents { get { lock (_gate) return _runEvents; } }
        public long DroppedEvents { get { lock (_gate) return _dropped; } }
        public long UnmatchedCalls { get { lock (_gate) return _unmatched; } }
        public long OriginalExceptions { get { lock (_gate) return _exceptions; } }
        public long WrongThreadCalls { get { lock (_gate) return _wrongThread; } }
        public long RejectedCalls { get { lock (_gate) return _rejected; } }
        public long CompleteCalls { get { lock (_gate) return _complete; } }
        public long DuplicateCompletions { get { lock (_gate) return _duplicates; } }
        public long InstallationGaps { get { lock (_gate) return _gaps; } }
        public long AbandonedCalls { get { lock (_gate) return _abandoned; } }
        public bool StartEarlyCoverageVerified => false;
        public bool NativeOrderVerified => false;
        public bool FirstLoadSafe => false;
        public bool PathIsolationVerified => false;
        public bool GuestStateIsolated => false;
        public bool NativePermission => false;
        public bool WorldAuthority => false;
        public bool CargoAuthority => false;

        // Zero explicitly denotes an unavailable Plugin.Load marker. Positive
        // inputs still only describe a caller-provided scalar observation.
        public SaveStartupTrace(int pluginLoadThreadId, double pluginLoadAt)
        {
            if (pluginLoadThreadId < 0) throw new ArgumentOutOfRangeException(nameof(pluginLoadThreadId));
            if (!ValidTime(pluginLoadAt)) throw new ArgumentOutOfRangeException(nameof(pluginLoadAt));
            PluginLoadThreadId = pluginLoadThreadId; PluginLoadObserved = pluginLoadThreadId != 0;
            _lastAt = pluginLoadAt;
            if (!PluginLoadObserved) LoseIntegrity("Plugin.Load marker unavailable; earlier game loading is unknown.");
            else if (pluginLoadThreadId != Environment.CurrentManagedThreadId)
                LoseIntegrity("Plugin.Load marker was supplied from a different managed thread.");
            Emit(null, SaveStartupTraceStage.PluginLoad, pluginLoadAt, false);
        }

        public void MarkHooksInstalled(double at)
        {
            lock (_gate)
            {
                if (!Active() || !Time(at) || !BoundThread()) return;
                if (_installed) return;
                _installed = true;
                if (_update || !PluginLoadObserved)
                { _late = true; LoseIntegrity("Hooks installed after the observed early boundary or without its marker."); }
                Emit(null, SaveStartupTraceStage.HooksInstalled, at, false);
            }
        }

        public void ObserveFirstUnityUpdate(double at)
        {
            lock (_gate)
            {
                if (!Active() || !Time(at)) return;
                int thread = Environment.CurrentManagedThreadId;
                if (_update) { BoundThread(); return; }
                _update = true; _unityThread = thread;
                if (!_installed)
                { _late = true; LoseIntegrity("The first observed Unity Update preceded complete hook installation."); }
                foreach (var call in _calls.Values)
                    if (call.State == SaveStartupCallObservation.TerminalState.Pending && call.Thread != thread)
                    { _wrongThread++; Fault("A pending startup call belongs to another thread at Unity binding."); break; }
                if (Active()) Emit(null, SaveStartupTraceStage.FirstUnityUpdate, at, false);
            }
        }

        public bool BeginCall(string methodKey, double at, out long callId)
        {
            lock (_gate)
            {
                callId = 0;
                if (!Active() || !Time(at) || !BoundThread()) { _rejected++; return false; }
                if (string.IsNullOrWhiteSpace(methodKey) || methodKey.Length > MaxMethodKeyLength)
                { _rejected++; Fault("A startup method declaration key is invalid or truncated."); return false; }
                if (_pending >= MaxPendingCalls)
                { _rejected++; _dropped++; Fault("Startup pending-call capacity exceeded."); return false; }
                if (!_installed)
                { _gaps++; LoseIntegrity("A startup call occurred during incomplete hook installation."); }
                int thread = Environment.CurrentManagedThreadId;
                if (!_stacks.TryGetValue(thread, out List<long> stack)) { stack = new List<long>(); _stacks.Add(thread, stack); }
                long id = NextCallId();
                if (id == 0) { _rejected++; Fault("Process startup CallId high water is exhausted."); return false; }
                var call = new SaveStartupCallObservation.Call
                { Id=id, Parent=stack.Count == 0 ? 0 : stack[stack.Count-1], Depth=stack.Count, Thread=thread, Key=methodKey };
                _highestCallId = id; _calls.Add(id, call); stack.Add(id); _pending++;
                if (!Emit(call, SaveStartupTraceStage.Prefix, at, false)) { _rejected++; return false; }
                callId = id; return true;
            }
        }

        public bool CompleteCall(long callId, double at, bool originalException)
        {
            lock (_gate)
            {
                if (!_calls.TryGetValue(callId, out SaveStartupCallObservation.Call call))
                {
                    if (Active()) { _unmatched++; Fault("Startup completion has no fixed prefix in this Run."); }
                    _rejected++; return false;
                }
                if (Environment.CurrentManagedThreadId != call.Thread)
                { _wrongThread++; _rejected++; Fault("Startup completion arrived on a different prefix thread."); return false; }
                if (Active() && (!Time(at) || !BoundThread())) { _rejected++; return false; }
                // A null finalizer after postfix is idempotent. It cannot
                // revive health, reuse identity, consume quota or change data.
                if (call.State == SaveStartupCallObservation.TerminalState.Exception ||
                    call.State == SaveStartupCallObservation.TerminalState.Returned && !originalException)
                { _duplicates++; return true; }
                if (!Active()) { _rejected++; return false; }
                if (call.State == SaveStartupCallObservation.TerminalState.Returned)
                {
                    call.State = SaveStartupCallObservation.TerminalState.Exception; _exceptions++;
                    bool emitted = Emit(call, SaveStartupTraceStage.Finalizer, at, true);
                    Fault("An exception finalizer followed a recorded return; successful loading cannot be inferred.");
                    return emitted;
                }
                if (call.State != SaveStartupCallObservation.TerminalState.Pending ||
                    !_stacks.TryGetValue(call.Thread, out List<long> stack) || stack.Count == 0 || stack[stack.Count-1] != call.Id)
                { _unmatched++; _rejected++; Fault("Startup completion violated fixed nested prefix order."); return false; }
                stack.RemoveAt(stack.Count-1); if (stack.Count == 0) _stacks.Remove(call.Thread);
                _pending--; _complete++;
                call.State = originalException ? SaveStartupCallObservation.TerminalState.Exception : SaveStartupCallObservation.TerminalState.Returned;
                if (originalException) _exceptions++;
                bool accepted = Emit(call, originalException ? SaveStartupTraceStage.Finalizer : SaveStartupTraceStage.Postfix, at, originalException);
                if (originalException) Fault("An original exception was observed; the startup call chain is incomplete.");
                return accepted;
            }
        }

        // A confirmed Update thread is only a typed read candidate. Early or
        // missing Update markers never inherit the Plugin.Load thread as Unity.
        public bool CanReadBoundThread()
        {
            lock (_gate) return Active() && _installed && _update && BoundThread();
        }

        // Draining CLR evidence is safe on any thread and never heals loss.
        public bool TryTake(out SaveStartupCallObservation observation)
        {
            lock (_gate)
            { observation = _events.Count == 0 ? null : _events.Dequeue(); return observation != null; }
        }

        public void Invalidate(string reason)
        {
            lock (_gate)
            { if (Active()) Emit(null, SaveStartupTraceStage.Invalidated, _lastAt, false); Fault(BoundedReason(reason)); }
        }

        public void Retire(string reason)
        {
            lock (_gate)
            {
                if (_retired) return;
                if (Active()) Emit(null, SaveStartupTraceStage.Retired, _lastAt, false);
                _retired = true; _integrityLost = true;
                if (!_faulted) _reason = BoundedReason(reason);
                AbandonPending();
            }
        }

        private bool Active() => !_faulted && !_retired;
        private bool BoundThread()
        {
            if (!_update || Environment.CurrentManagedThreadId == _unityThread) return true;
            _wrongThread++; Fault("Startup observation occurred off the bound Unity Update thread."); return false;
        }
        private bool Time(double at)
        {
            if (!ValidTime(at) || at < _lastAt) { Fault("Startup clock is invalid or moved before an observed event."); return false; }
            _lastAt = at; return true;
        }
        private static bool ValidTime(double at) => !double.IsNaN(at) && !double.IsInfinity(at) && at >= 0;
        private bool Emit(SaveStartupCallObservation.Call call, SaveStartupTraceStage stage, double at, bool exception)
        {
            if (_events.Count >= MaxEvents || _runEvents >= MaxRunEvents)
            { _dropped++; Fault(_events.Count >= MaxEvents ? "Startup observation queue overflowed." : "Startup Run event quota exhausted."); return false; }
            _runEvents++; _sequence++;
            int thread = Environment.CurrentManagedThreadId;
            _events.Enqueue(new SaveStartupCallObservation(RunId, _sequence, call, stage, thread, at, exception,
                !_update, _update && _installed && thread == _unityThread));
            return true;
        }
        private void LoseIntegrity(string reason)
        { if (!_integrityLost) _reason = reason; _integrityLost = true; }
        private void Fault(string reason)
        {
            if (!_faulted) _reason = reason;
            _faulted = true; _integrityLost = true; AbandonPending();
        }
        private void AbandonPending()
        {
            if (_pending != 0)
                foreach (var call in _calls.Values)
                    if (call.State == SaveStartupCallObservation.TerminalState.Pending)
                    { call.State = SaveStartupCallObservation.TerminalState.Abandoned; _abandoned++; }
            _pending = 0; _stacks.Clear();
        }
        private static string BoundedReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) return "Startup evidence was stopped or invalidated.";
            return reason.Length <= MaxMethodKeyLength ? reason : reason.Substring(0, MaxMethodKeyLength);
        }
        private static long NextCallId()
        {
            while (true)
            {
                long previous = Interlocked.Read(ref _processCallId);
                if (previous == long.MaxValue) return 0;
                if (Interlocked.CompareExchange(ref _processCallId, previous+1, previous) == previous) return previous+1;
            }
        }
    }
}
