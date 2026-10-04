using System;
using System.Collections.Generic;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.World;

namespace DaveCoop.Core.Actions
{
    // The Unity-thread adapter supplies copied, freshly sampled facts. Defaults
    // are unavailable; a visible remote avatar or current layout is insufficient.
    public sealed class HostActionFacts
    {
        public long SceneEpoch { get; set; }
        public string SceneKey { get; set; }
        public long WorldRevision { get; set; }
        public double SampledAt { get; set; }
        public bool TargetAvailable { get; set; }
        public HostEntityTarget Target { get; set; }
        public bool TargetSupported { get; set; }
        public bool TargetDead { get; set; }
        public bool TargetCaptured { get; set; }
        public bool MapAuthorityReady { get; set; }
        public bool GuestStateIsolated { get; set; }
        public bool LocalActorArbitrated { get; set; }
        public bool ActorAvailable { get; set; }
        public bool ActorPermitted { get; set; }
        public bool ActorPositionTrusted { get; set; }
        public int ActorPlayerId { get; set; }
        public long ActorRevision { get; set; }
        public bool LoadoutAvailable { get; set; }
        public long LoadoutRevision { get; set; }
        public int EquipmentSlot { get; set; }
        public bool CooldownReady { get; set; }
        public bool ResourceAvailable { get; set; }
        public bool SpatialChecksAvailable { get; set; }
        public bool WithinRange { get; set; }
        public bool LineOfSight { get; set; }
        public bool StageValid { get; set; }
        public long InteractionId { get; set; }
        public int InteractionPlayerId { get; set; }
        public long InteractionTargetId { get; set; }
        public bool NativeAdapterReady { get; set; }
    }

    // Local-only permission candidate. Its identity and short lease must be
    // checked against new facts at dispatch; it never enters a wire message.
    public sealed class HostFishActionPlan
    {
        private readonly FishActionRequest _request;
        public FishActionRequest Request => FishActions.Copy(_request);
        public long RequestId => _request.RequestId;
        public int PlayerId => _request.PlayerId;
        public string RequestFingerprint { get; }
        public long OperationId { get; }
        public HostEntityTarget Target { get; }
        public long ActorRevision { get; }
        public long LoadoutRevision { get; }
        public double ExpiresAt { get; }

        internal HostFishActionPlan(FishActionRequest request, string fingerprint, long operationId,
            HostActionFacts facts, double expiresAt)
        {
            _request = FishActions.Copy(request); RequestFingerprint = fingerprint; OperationId = operationId;
            Target = request.TargetEntityId == 0 ? default : facts.Target;
            ActorRevision = facts.ActorRevision; LoadoutRevision = facts.LoadoutRevision;
            ExpiresAt = expiresAt;
        }
    }

    // Serialize calls on the host consumer thread. This gate never calls native
    // code and has no success/capture status. Started/unknown are not rewards.
    public sealed class HostFishActionGate
    {
        public const int MaxPending = 16;
        public const int MaxCachedResults = 1024;
        public const double MaxRequestAge = 1;
        public const double MaxFactsAge = 0.25;
        public const double PlanLeaseSeconds = 0.25;
        private const double RatePerSecond = 8;
        private const double BurstRequests = 16;

        private enum EntryStage { Queued, Taken, Planned, Dispatching, Started, Terminal }
        private sealed class Entry
        {
            public ReceivedFishAction Received;
            public string Fingerprint;
            public FishActionResult Result;
            public EntryStage Stage;
            public HostFishActionPlan Plan;
            public bool CountedPending;
        }

        private readonly string _roomId;
        private readonly int _boundPlayer;
        private readonly Dictionary<long, Entry> _entries = new Dictionary<long, Entry>();
        private readonly Queue<long> _queue = new Queue<long>();
        private readonly Queue<long> _terminalOrder = new Queue<long>();
        private readonly Dictionary<string, long> _targetLeases = new Dictionary<string, long>();
        private long _actorLease;
        private long _nextOperation;
        private long _epoch;
        private string _scene;
        private bool _ready;
        private bool _closed;
        private double _lastNow = -1;
        private double _tokens = BurstRequests;
        public int PendingCount { get; private set; }
        public long HighestRequestId { get; private set; }
        public int CachedResults => _terminalOrder.Count;

        public HostFishActionGate(string room, int boundPlayer)
        {
            if (!Guid.TryParse(room, out Guid parsedRoom) || parsedRoom == Guid.Empty || (boundPlayer != 1 && boundPlayer != 2))
                throw new ArgumentException("Invalid fish action room or bound participant.");
            _roomId = room; _boundPlayer = boundPlayer;
        }

        public FishActionResult Accept(ReceivedFishAction received, bool ready, long epoch, string scene, double hostNow)
        {
            ValidateEnvelope(received, hostNow);
            AdvanceClock(hostNow);
            if (ready && (epoch < 1 || string.IsNullOrWhiteSpace(scene) || scene.Length > 160))
                throw new ArgumentException("Invalid host fish action scene.");
            if (!_closed)
            {
                if (_epoch != 0 && (!ready || epoch != _epoch || !string.Equals(scene, _scene, StringComparison.Ordinal)))
                    InvalidateScene();
                _ready = ready; _epoch = ready ? epoch : 0; _scene = ready ? scene : null;
            }
            FishActionRequest request = received.Request;
            string fingerprint = FishActions.Fingerprint(request);
            if (_entries.TryGetValue(request.RequestId, out Entry existing))
            {
                if (existing.Fingerprint != fingerprint)
                    return MakeResult(request, fingerprint, FishActionStatus.Rejected, FishActionReason.RequestConflict);
                return FishActions.Copy(existing.Result);
            }
            if (request.RequestId <= HighestRequestId)
                return MakeResult(request, fingerprint, FishActionStatus.Rejected, FishActionReason.ReplayExpired);

            // Even a valid new request rejected by business rules consumes its
            // ID; terminal eviction and scene changes cannot allow later replay.
            HighestRequestId = request.RequestId;
            var entry = new Entry
            {
                Received = received.Copy(), Fingerprint = fingerprint, Stage = EntryStage.Queued,
                Result = MakeResult(request, fingerprint, FishActionStatus.Queued, FishActionReason.None)
            };
            _entries.Add(request.RequestId, entry);
            FishActionReason rejected = FishActionReason.None;
            if (_closed) rejected = FishActionReason.RoomClosed;
            else if (!ready || hostNow - received.ReceivedAt > MaxRequestAge) rejected = FishActionReason.SceneUnavailable;
            else if (request.SceneEpoch != epoch || request.SceneKey != scene) rejected = FishActionReason.StaleScene;
            else if (PendingCount >= MaxPending) rejected = FishActionReason.QueueFull;
            else if (_tokens < 1) rejected = FishActionReason.RateLimited;
            if (_tokens >= 1) _tokens -= 1;
            if (rejected != FishActionReason.None) return Terminal(entry, FishActionStatus.Rejected, rejected);
            PendingCount++; entry.CountedPending = true; _queue.Enqueue(request.RequestId);
            return FishActions.Copy(entry.Result);
        }

        public bool TryTake(out ReceivedFishAction received)
        {
            received = null;
            while (_queue.Count != 0)
            {
                long id = _queue.Dequeue();
                if (!_entries.TryGetValue(id, out Entry entry) || entry.Stage != EntryStage.Queued) continue;
                entry.Stage = EntryStage.Taken; received = entry.Received.Copy(); return true;
            }
            return false;
        }

        // ProbeTarget completes only a read-only validation. All effects require
        // explicit authority, arbitration, actor/loadout/spatial facts and native
        // capability; unknown facts do not grant a plan.
        public FishActionResult ValidateFresh(long requestId, HostActionFacts facts, double hostNow, out HostFishActionPlan plan)
        {
            plan = null; AdvanceClock(hostNow);
            Entry entry = RequireEntry(requestId);
            if (entry.Stage == EntryStage.Terminal || entry.Stage == EntryStage.Started || entry.Stage == EntryStage.Dispatching)
                return FishActions.Copy(entry.Result);
            if (entry.Stage != EntryStage.Taken && entry.Stage != EntryStage.Planned)
                throw new InvalidOperationException("Take a fish action before validating facts.");
            FishActionReason rejected = CheckFacts(entry, facts, hostNow);
            if (rejected != FishActionReason.None) return Terminal(entry, FishActionStatus.Rejected, rejected,
                facts == null ? 0 : Math.Max(0, facts.WorldRevision));
            if (entry.Received.Request.Action == FishActionKind.ProbeTarget)
                return Terminal(entry, FishActionStatus.DryRunValidated, FishActionReason.None, facts.WorldRevision);
            if (entry.Stage == EntryStage.Planned) { plan = entry.Plan; return FishActions.Copy(entry.Result); }
            string lease = CaptureLease(entry.Received.Request, entry.Received.Request.TargetEntityId == 0 ? default : facts.Target);
            if (lease != null && _targetLeases.ContainsKey(lease))
                return Terminal(entry, FishActionStatus.Rejected, FishActionReason.TargetBusy, facts.WorldRevision);
            if (_actorLease != 0) return Terminal(entry, FishActionStatus.Rejected, FishActionReason.ActorUnavailable, facts.WorldRevision);
            if (_nextOperation == long.MaxValue)
                return Terminal(entry, FishActionStatus.Rejected, FishActionReason.NativeAdapterUnavailable, facts.WorldRevision);
            long operation = ++_nextOperation;
            plan = new HostFishActionPlan(entry.Received.Request, entry.Fingerprint, operation, facts, hostNow + PlanLeaseSeconds);
            entry.Plan = plan; entry.Stage = EntryStage.Planned; _actorLease = operation;
            if (lease != null) _targetLeases.Add(lease, operation);
            return FishActions.Copy(entry.Result);
        }

        // The caller must obtain fresh facts again immediately before native
        // entry. Marking happens before the call and cannot happen twice.
        public bool TryMarkDispatching(HostFishActionPlan plan, HostActionFacts freshFacts, double hostNow, out FishActionResult result)
        {
            AdvanceClock(hostNow); result = null;
            if (plan == null || !_entries.TryGetValue(plan.RequestId, out Entry entry) ||
                !ReferenceEquals(entry.Plan, plan)) return false;
            result = FishActions.Copy(entry.Result);
            if (entry.Stage != EntryStage.Planned) return false;
            FishActionReason rejected = hostNow > plan.ExpiresAt ? FishActionReason.InvalidStage : CheckFacts(entry, freshFacts, hostNow);
            if (rejected == FishActionReason.None &&
                (freshFacts.ActorRevision != plan.ActorRevision || freshFacts.LoadoutRevision != plan.LoadoutRevision ||
                (plan.Request.TargetEntityId != 0 && !SameTarget(freshFacts.Target, plan.Target)))) rejected = FishActionReason.InvalidStage;
            if (rejected != FishActionReason.None)
            {
                result = Terminal(entry, FishActionStatus.Rejected, rejected,
                    freshFacts == null ? 0 : Math.Max(0, freshFacts.WorldRevision)); return false;
            }
            entry.Stage = EntryStage.Dispatching; return true;
        }

        // Only an adapter that positively knows it did not enter native code can
        // release a dispatched reservation through this method.
        public FishActionResult FinishNotStarted(HostFishActionPlan plan, FishActionReason reason)
        {
            if (plan == null || reason == FishActionReason.None || !Enum.IsDefined(typeof(FishActionReason), reason))
                throw new ArgumentException("Invalid not-started fish action.");
            Entry entry = RequireEntry(plan.RequestId);
            if (!ReferenceEquals(entry.Plan, plan) ||
                (entry.Stage != EntryStage.Planned && entry.Stage != EntryStage.Dispatching))
                throw new InvalidOperationException("Fish action has already entered or completed.");
            return Terminal(entry, FishActionStatus.Rejected, reason);
        }

        public FishActionResult Complete(long requestId, FishActionStatus status, FishActionReason reason,
            long worldRevision = 0, long operationId = 0)
        {
            Entry entry = RequireEntry(requestId);
            if (entry.Stage == EntryStage.Terminal) return FishActions.Copy(entry.Result);
            if (worldRevision < 0) throw new ArgumentException("Invalid fish action world revision.");
            if (status == FishActionStatus.NativeStarted || status == FishActionStatus.OutcomeUnknown)
            {
                if (entry.Plan == null || operationId != entry.Plan.OperationId ||
                    (entry.Stage != EntryStage.Dispatching && entry.Stage != EntryStage.Started))
                    throw new InvalidOperationException("Fish action was not dispatched with this operation identity.");
                if (status == FishActionStatus.NativeStarted)
                {
                    entry.Result = MakeResult(entry.Received.Request, entry.Fingerprint, status, reason, worldRevision, operationId);
                    entry.Stage = EntryStage.Started; return FishActions.Copy(entry.Result);
                }
                // Uncertain native entry is terminal for retry, but keeps its
                // reservations until room closure. A future native cleanup bridge
                // must prove cancellation before allowing earlier release.
                return Terminal(entry, status, reason, worldRevision, operationId, false);
            }
            if (entry.Stage == EntryStage.Dispatching || entry.Stage == EntryStage.Started)
                throw new InvalidOperationException("Entered native actions cannot become not-started rejections.");
            if (status != FishActionStatus.Rejected && status != FishActionStatus.DryRunValidated)
                throw new ArgumentException("Invalid completion phase.");
            if (operationId != 0) throw new ArgumentException("Non-native completion has no operation identity.");
            return Terminal(entry, status, reason, worldRevision);
        }

        public void InvalidateScene() => Invalidate(FishActionReason.SceneChanged);

        private void Invalidate(FishActionReason reason)
        {
            // Snapshot prevents terminal-cache eviction from invalidating this
            // traversal. Native entry is never turned into a retryable rejection.
            var pending = new List<Entry>();
            foreach (Entry entry in _entries.Values) if (entry.Stage != EntryStage.Terminal) pending.Add(entry);
            foreach (Entry entry in pending)
            {
                if (entry.Stage == EntryStage.Dispatching || entry.Stage == EntryStage.Started)
                    Terminal(entry, FishActionStatus.OutcomeUnknown, reason, 0, entry.Plan.OperationId, false);
                else Terminal(entry, FishActionStatus.Rejected, reason);
            }
            _queue.Clear(); _ready = false; _epoch = 0; _scene = null;
        }

        public void Close()
        {
            if (_closed) return;
            Invalidate(FishActionReason.RoomClosed); _closed = true; _actorLease = 0; _targetLeases.Clear();
            // Keep high-water and cached evidence on a closed gate. A new Room
            // gets a new instance; reopening this old gate is intentionally absent.
        }

        private FishActionReason CheckFacts(Entry entry, HostActionFacts facts, double now)
        {
            FishActionRequest request = entry.Received.Request;
            if (_closed) return FishActionReason.RoomClosed;
            if (!_ready || request.SceneEpoch != _epoch || request.SceneKey != _scene) return FishActionReason.SceneChanged;
            if (now - entry.Received.ReceivedAt > MaxRequestAge) return FishActionReason.SceneUnavailable;
            if (facts == null || !double.IsFinite(facts.SampledAt) || facts.SampledAt < 0 ||
                facts.SampledAt > now || now - facts.SampledAt > MaxFactsAge || facts.WorldRevision < 1 ||
                facts.SceneEpoch != request.SceneEpoch || facts.SceneKey != request.SceneKey)
                return FishActionReason.SceneUnavailable;
            if (request.TargetEntityId != 0 && (!facts.TargetAvailable || facts.Target.SceneEpoch != request.SceneEpoch ||
                facts.Target.EntityId != request.TargetEntityId || facts.Target.Kind != EntityKind.Fish ||
                facts.Target.Generation < 1 || facts.Target.DataTid < 1 || facts.TargetDead || facts.TargetCaptured))
                return FishActionReason.TargetUnavailable;
            if (request.Action == FishActionKind.ProbeTarget) return FishActionReason.None;
            if (!facts.MapAuthorityReady || !facts.GuestStateIsolated || !facts.LocalActorArbitrated)
                return FishActionReason.WorldAuthorityUnavailable;
            if (!facts.ActorAvailable || !facts.ActorPermitted || !facts.ActorPositionTrusted ||
                facts.ActorPlayerId != _boundPlayer || facts.ActorRevision < 1)
                return FishActionReason.ActorUnavailable;
            if (!facts.LoadoutAvailable || facts.LoadoutRevision < 1 || facts.LoadoutRevision != request.LoadoutRevision ||
                facts.EquipmentSlot != request.EquipmentSlot || !facts.CooldownReady || !facts.ResourceAvailable)
                return FishActionReason.LoadoutUnavailable;
            if (request.TargetEntityId != 0 && !facts.TargetSupported) return FishActionReason.TargetUnavailable;
            if (!facts.SpatialChecksAvailable || !facts.WithinRange || !facts.LineOfSight) return FishActionReason.OutOfRange;
            if (!facts.StageValid || (request.InteractionId != 0 && (facts.InteractionId != request.InteractionId ||
                facts.InteractionPlayerId != _boundPlayer || (request.TargetEntityId != 0 && facts.InteractionTargetId != request.TargetEntityId))))
                return FishActionReason.InvalidStage;
            if (!facts.NativeAdapterReady) return FishActionReason.NativeAdapterUnavailable;
            return FishActionReason.None;
        }

        private FishActionResult Terminal(Entry entry, FishActionStatus status, FishActionReason reason,
            long revision = 0, long operation = 0, bool release = true)
        {
            FishActionResult result = MakeResult(entry.Received.Request, entry.Fingerprint, status, reason, revision, operation);
            if (entry.Stage != EntryStage.Terminal)
            {
                if (entry.CountedPending) { PendingCount--; entry.CountedPending = false; }
                entry.Stage = EntryStage.Terminal; _terminalOrder.Enqueue(entry.Received.Request.RequestId);
            }
            entry.Result = result;
            if (release) Release(entry);
            while (_terminalOrder.Count > MaxCachedResults)
            {
                long old = _terminalOrder.Dequeue(); _entries.Remove(old);
            }
            return FishActions.Copy(result);
        }

        private void Release(Entry entry)
        {
            if (entry.Plan == null) return;
            long operation = entry.Plan.OperationId;
            if (_actorLease == operation) _actorLease = 0;
            string lease = CaptureLease(entry.Received.Request, entry.Plan.Target);
            if (lease != null && _targetLeases.TryGetValue(lease, out long owner) && owner == operation) _targetLeases.Remove(lease);
        }

        private void ValidateEnvelope(ReceivedFishAction received, double now)
        {
            if (received == null || received.RoomId != _roomId || received.BoundPlayerId != _boundPlayer ||
                received.PacketSequence < 1 || !double.IsFinite(received.ReceivedAt) || received.ReceivedAt < 0 ||
                !double.IsFinite(now) || now < 0 || received.ReceivedAt > now)
                throw new ProtocolException("Invalid fish action source or arrival clock.");
            FishActions.ValidateRequest(received.Request);
            if (received.Request.PlayerId != _boundPlayer) throw new ProtocolException("Fish action actor differs from bound participant.");
        }

        private void AdvanceClock(double now)
        {
            if (!double.IsFinite(now) || now < 0 || now < _lastNow) throw new ArgumentException("Fish action host clock regressed.");
            if (_lastNow >= 0) _tokens = Math.Min(BurstRequests, _tokens + (now - _lastNow) * RatePerSecond);
            _lastNow = now;
        }

        private Entry RequireEntry(long id)
        {
            if (!_entries.TryGetValue(id, out Entry entry)) throw new InvalidOperationException("Unknown or expired fish action.");
            return entry;
        }

        private static string CaptureLease(FishActionRequest request, HostEntityTarget target)
        {
            if (request.Action != FishActionKind.SubmitQteInput && request.Action != FishActionKind.RequestPickup &&
                request.Action != FishActionKind.RecallHarpoon) return null;
            if (target.EntityId == 0) return null;
            return target.SceneEpoch + ":" + target.EntityId + ":" + target.Generation;
        }

        private static bool SameTarget(HostEntityTarget left, HostEntityTarget right) =>
            left.SceneEpoch == right.SceneEpoch && left.EntityId == right.EntityId && left.LocalToken == right.LocalToken &&
            left.Kind == right.Kind && left.DataTid == right.DataTid && left.Generation == right.Generation;

        private static FishActionResult MakeResult(FishActionRequest request, string fingerprint, FishActionStatus status,
            FishActionReason reason, long revision = 0, long operation = 0)
        {
            var result = new FishActionResult
            {
                RequestId = request.RequestId, PlayerId = request.PlayerId, SceneEpoch = request.SceneEpoch,
                SceneKey = request.SceneKey, Action = request.Action, TargetEntityId = request.TargetEntityId,
                Status = status, Reason = reason, WorldRevision = revision, OperationId = operation,
                RequestFingerprint = fingerprint
            };
            FishActions.ValidateResult(result); return result;
        }
    }
}
