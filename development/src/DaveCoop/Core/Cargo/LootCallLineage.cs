using System;
using System.Collections.Generic;

namespace DaveCoop.Core.Cargo
{
    // A copied observed identity. Ordinal is local to the native adapter's Run;
    // it does not prove a native object's lifetime or permission to mutate it.
    public sealed class LootLineageSource
    {
        public long LocalFishOrdinal { get; }
        public long SceneEpoch { get; }
        public long EntityId { get; }
        public long Generation { get; }
        public int DataTid { get; }
        public LootLineageSource(long localFishOrdinal, long sceneEpoch, long entityId, long generation, int dataTid)
        {
            if (localFishOrdinal < 1 || sceneEpoch < 1 || entityId < 1 || generation < 1 || dataTid < 1)
                throw new ArgumentException("Incomplete observed loot fish identity.");
            LocalFishOrdinal = localFishOrdinal; SceneEpoch = sceneEpoch; EntityId = entityId; Generation = generation; DataTid = dataTid;
        }
        internal LootLineageSource Copy() => new LootLineageSource(LocalFishOrdinal, SceneEpoch, EntityId, Generation, DataTid);
        internal bool Same(LootLineageSource other) => other != null && LocalFishOrdinal == other.LocalFishOrdinal &&
            SceneEpoch == other.SceneEpoch && EntityId == other.EntityId && Generation == other.Generation && DataTid == other.DataTid;
    }

    // Exact object identity is checked against the minting registry. CallId is
    // an adapter identifier, never a substitute for ownership of this token.
    public sealed class LootLineageToken
    {
        public Guid RunId { get; }
        public long CallId { get; }
        public int MethodCode { get; }
        public long ParentCallId { get; }
        public long ObservedParentCallId => ParentCallId;
        public long SourceRootCallId { get; }
        public int Depth { get; }
        public LootLineageSource Source { get; }
        public LootLineageSource CandidateSource => Source;
        public bool SourceKnown => Source != null;
        public bool SourceFrozenAtPrefix => Source != null;
        public bool SourceShadowed { get; }
        internal bool Posted, Finalized, Abandoned, ExceptionObserved;
        internal bool? Returned;
        internal int? ReturnedInt;
        internal LootLineageToken(Guid run, long callId, int method, long parent, long root, int depth, LootLineageSource source, bool shadowed)
        {
            RunId = run; CallId = callId; MethodCode = method; ParentCallId = parent; SourceRootCallId = root;
            Depth = depth; Source = source?.Copy(); SourceShadowed = shadowed;
        }
    }

    public enum LootLineageStage { Prefix = 1, Postfix = 2, Finalizer = 3 }

    public sealed class LootLineageRecord
    {
        public Guid RunId { get; }
        public long Sequence { get; }
        public long CallId { get; }
        public int MethodCode { get; }
        public long ParentCallId { get; }
        public long ObservedParentCallId => ParentCallId;
        public long SourceRootCallId { get; }
        public int Depth { get; }
        public LootLineageSource Source { get; }
        public LootLineageSource CandidateSource => Source;
        public bool SourceKnown => Source != null;
        public bool SourceFrozenAtPrefix => Source != null;
        public bool SourceShadowed { get; }
        public LootLineageStage Stage { get; }
        public bool? OriginalReturn { get; }
        public int? OriginalIntReturn { get; }
        public bool OriginalException { get; }
        public bool HealthyAtCapture { get; }
        public bool ObservationOnly => true;
        public bool NativeSourceOperationBound => false;
        public bool SourceOperationBound => false;
        public bool MemberOwnershipVerified => false;
        public bool NativeRewards => false;
        public bool FullYield => false;
        public bool YieldComplete => false;
        public bool ActualBagDelta => false;
        public bool CaptureSuccess => false;
        public bool NativeGenerationVerified => false;
        public bool NativeHookAbiVerified => false;
        internal LootLineageRecord(LootLineageToken token, long sequence, LootLineageStage stage, bool? originalReturn, bool exception, bool healthy)
        {
            RunId = token.RunId; Sequence = sequence; CallId = token.CallId; MethodCode = token.MethodCode;
            ParentCallId = token.ParentCallId; SourceRootCallId = token.SourceRootCallId; Depth = token.Depth;
            Source = token.Source?.Copy(); SourceShadowed = token.SourceShadowed; Stage = stage;
            OriginalReturn = originalReturn; OriginalIntReturn = stage == LootLineageStage.Prefix ? null : token.ReturnedInt;
            OriginalException = exception; HealthyAtCapture = healthy;
        }
    }

    // Consumer-thread call scopes only. There is no operation/member binding,
    // time-window association, asynchronous inheritance or cargo transition.
    public sealed class LootCallLineage
    {
        public const int MaxQueued = 512;
        public const int MaxContexts = 128;
        public const int MaxDepth = 32;
        public const int MaxRunEvents = 8192;
        private readonly object _gate = new object();
        private readonly int _thread;
        private readonly Queue<LootLineageRecord> _records = new Queue<LootLineageRecord>();
        private readonly Dictionary<long, LootLineageToken> _tokens = new Dictionary<long, LootLineageToken>();
        private readonly List<LootLineageToken> _stack = new List<LootLineageToken>();
        private bool _failed, _stopped;
        private string _reason = "Synchronous copied caller evidence only; capture and member ownership remain unknown.";
        private long _highest, _sequence, _dropped, _unmatched, _replays, _exceptions, _wrongThread, _discardedQueue, _discardedPending;
        private int _runEvents;
        public Guid RunId { get; } = Guid.NewGuid();
        public bool Healthy { get { lock (_gate) return !_failed && !_stopped; } }
        public bool IntegrityLost { get { lock (_gate) return _failed || _stopped; } }
        public string Reason { get { lock (_gate) return _reason; } }
        public long HighestCallId { get { lock (_gate) return _highest; } }
        public long LastSequence { get { lock (_gate) return _sequence; } }
        public int RunEvents { get { lock (_gate) return _runEvents; } }
        public int QueuedCount { get { lock (_gate) return _records.Count; } }
        public int PendingCount { get { lock (_gate) return _stack.Count; } }
        public long DiscardedQueue { get { lock (_gate) return _discardedQueue; } }
        public long DiscardedPending { get { lock (_gate) return _discardedPending; } }
        public long ReplayRejected { get { lock (_gate) return _replays; } }
        public long Unmatched { get { lock (_gate) return _unmatched; } }
        public long OriginalExceptions { get { lock (_gate) return _exceptions; } }
        public long WrongThreadCalls { get { lock (_gate) return _wrongThread; } }
        public long Dropped { get { lock (_gate) return _dropped; } }
        public bool NativeSourceOperationBound => false;
        public bool SourceOperationBound => false;
        public bool MemberOwnershipVerified => false;
        public bool NativeRewards => false;
        public bool FullYield => false;
        public bool YieldComplete => false;
        public bool ActualBagDelta => false;
        public bool CaptureSuccess => false;
        public bool NativeGenerationVerified => false;
        public bool NativeHookAbiVerified => false;

        public LootCallLineage(int unityThreadId)
        {
            if (unityThreadId < 1 || Environment.CurrentManagedThreadId != unityThreadId)
                throw new ArgumentException("Loot caller registry requires its adapter's confirmed thread.");
            _thread = unityThreadId;
        }

        public LootLineageToken Begin(long callId, int methodCode, bool fishBoundary, LootLineageSource frozenFish, int callbackThreadId)
        {
            lock (_gate)
            {
                if (!Active() || !Thread(callbackThreadId)) return null;
                if (callId < 1 || methodCode < 1 || methodCode > 64 || !fishBoundary && frozenFish != null)
                { Fault("Invalid exact loot prefix declaration or mixed source fields."); return null; }
                if (callId <= _highest)
                { _replays++; Fault("Loot prefix CallId replayed or regressed within this Run."); return null; }
                _highest = callId; // a failed capacity admission does not reset this fence
                if (_stack.Count >= MaxContexts || _stack.Count >= MaxDepth)
                { _dropped++; Fault("Loot pending-context or synchronous-depth capacity exceeded."); return null; }
                LootLineageToken parent = _stack.Count == 0 ? null : _stack[_stack.Count - 1];
                LootLineageSource source = fishBoundary ? frozenFish : parent?.Source;
                bool shadowed = fishBoundary ? frozenFish == null || parent?.Source != null && !frozenFish.Same(parent.Source)
                    : parent?.SourceShadowed ?? false;
                long root = source == null ? 0 : fishBoundary && (parent?.Source == null || !source.Same(parent.Source))
                    ? callId : parent?.SourceRootCallId ?? callId;
                var token = new LootLineageToken(RunId, callId, methodCode, parent?.CallId ?? 0, root, _stack.Count, source, shadowed);
                _tokens.Add(callId, token); _stack.Add(token);
                return Emit(token, LootLineageStage.Prefix, null, false) ? token : null;
            }
        }

        public bool RecordPostfix(LootLineageToken token, bool? originalReturn, int? originalIntReturn = null)
        {
            lock (_gate)
            {
                if (!Active() || !Thread(_thread) || !Own(token)) return false;
                if (token.Finalized || token.Abandoned || !Top(token))
                { _unmatched++; Fault("Loot postfix did not match its fixed top prefix."); return false; }
                if (originalReturn.HasValue && originalIntReturn.HasValue)
                { Fault("One exact loot return cannot be both bool and integer."); return false; }
                if (token.Posted)
                {
                    if (token.Returned == originalReturn && token.ReturnedInt == originalIntReturn) return true;
                    Fault("Duplicate loot postfix changed the original result."); return false;
                }
                token.Posted = true; token.Returned = originalReturn; token.ReturnedInt = originalIntReturn;
                return Emit(token, LootLineageStage.Postfix, originalReturn, false);
            }
        }

        public bool FinalizeCall(LootLineageToken token, bool originalException)
        {
            lock (_gate)
            {
                if (_stopped || !Thread(_thread) || !Own(token)) return false;
                if (token.Finalized)
                {
                    if (!originalException || token.ExceptionObserved) return true;
                    token.ExceptionObserved = true; _exceptions++;
                    Fault("An original exception arrived after a finalized loot call.");
                    return Emit(token, LootLineageStage.Finalizer, token.Returned, true);
                }
                if (!Active() || token.Abandoned) return false;
                if (!Top(token))
                { _unmatched++; Fault("Loot finalizer violated fixed synchronous prefix order."); return false; }
                _stack.RemoveAt(_stack.Count - 1); token.Finalized = true;
                if (originalException)
                { token.ExceptionObserved = true; _exceptions++; Fault("An original loot exception revoked the caller chain."); }
                else if (!token.Posted)
                { _unmatched++; Fault("Normal loot finalizer has no recorded original postfix."); }
                return Emit(token, LootLineageStage.Finalizer, token.Returned, originalException);
            }
        }

        public bool TryTake(out LootLineageRecord record)
        { lock (_gate) { record = _records.Count == 0 ? null : _records.Dequeue(); return record != null; } }

        public void Invalidate(string reason) { lock (_gate) Fault(Bounded(reason)); }
        public void Stop(string reason)
        {
            lock (_gate)
            {
                if (_stopped) return;
                if (!_failed) _reason = Bounded(reason);
                _stopped = true; _discardedQueue += _records.Count; _records.Clear(); Abandon();
            }
        }

        private bool Active() => !_failed && !_stopped;
        private bool Thread(int supplied)
        {
            if (supplied == _thread && Environment.CurrentManagedThreadId == _thread) return true;
            _wrongThread++; Fault("Loot callback is outside the fixed adapter thread."); return false;
        }
        private bool Own(LootLineageToken token)
        {
            if (token != null && token.RunId == RunId && _tokens.TryGetValue(token.CallId, out LootLineageToken own) && ReferenceEquals(token, own)) return true;
            _unmatched++; Fault("Loot callback token belongs to no prefix in this registry Run."); return false;
        }
        private bool Top(LootLineageToken token) => _stack.Count != 0 && ReferenceEquals(_stack[_stack.Count - 1], token);
        private bool Emit(LootLineageToken token, LootLineageStage stage, bool? originalReturn, bool exception)
        {
            if (_records.Count >= MaxQueued || _runEvents >= MaxRunEvents)
            { _dropped++; Fault(_records.Count >= MaxQueued ? "Loot lineage queue overflowed." : "Loot lineage Run event quota exhausted."); return false; }
            _runEvents++; _sequence++;
            _records.Enqueue(new LootLineageRecord(token, _sequence, stage, originalReturn, exception, Active())); return true;
        }
        private void Fault(string reason)
        { if (!_failed && !_stopped) _reason = reason; _failed = true; Abandon(); }
        private void Abandon()
        { foreach (LootLineageToken token in _stack) { token.Abandoned = true; _discardedPending++; } _stack.Clear(); }
        private static string Bounded(string reason) => string.IsNullOrWhiteSpace(reason) ? "Loot caller evidence stopped or was lost." :
            reason.Length <= 256 ? reason : reason.Substring(0, 256);
    }
}
