using System;
using System.Globalization;
using DaveCoop.Core.Actions;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.World;

internal static class FishActionTests
{
    private const string Room = "0123456789abcdef0123456789abcdef";
    private const string OtherRoom = "abcdef0123456789abcdef0123456789";

    internal static void SchemaAndCanonicalFingerprint()
    {
        FishActionRequest probe = Request(1);
        FishActions.ValidateRequest(probe);
        string fingerprint = FishActions.Fingerprint(probe);
        Assert(fingerprint.Length == 64 && fingerprint == FishActions.Fingerprint(FishActions.Copy(probe)), "copy changed canonical intent");
        FishActionRequest changed = FishActions.Copy(probe); changed.AimX = -0.0f;
        Assert(FishActions.Fingerprint(changed) == fingerprint, "signed zero created another logical intent");
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            changed.AimX = 0.5f; string invariant = FishActions.Fingerprint(changed);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert(FishActions.Fingerprint(changed) == invariant, "machine culture changed intent fingerprint");
        }
        finally { CultureInfo.CurrentCulture = original; }
        changed = FishActions.Copy(probe); changed.SceneKey = "A|scene";
        Assert(FishActions.Fingerprint(changed) != fingerprint, "scene was omitted from canonical intent");
        changed = FishActions.Copy(probe); changed.TargetEntityId++;
        Assert(FishActions.Fingerprint(changed) != fingerprint, "target was omitted from canonical intent");
        changed = FishActions.Copy(probe); changed.EquipmentSlot = 1;
        Assert(FishActions.Fingerprint(changed) != fingerprint, "equipment slot was omitted from canonical intent");

        FishActionRequest fire = Request(2, FishActionKind.FireHarpoon); fire.TargetEntityId = 0;
        FishActions.ValidateRequest(fire); fire.Action = FishActionKind.FireGun; FishActions.ValidateRequest(fire);
        FishActionRequest qte = Request(3, FishActionKind.SubmitQteInput); qte.InteractionId = 8; qte.AimX = 0;
        FishActions.ValidateRequest(qte);
        FishActionRequest recall = Request(4, FishActionKind.RecallHarpoon); recall.InteractionId = 8; recall.TargetEntityId = 0;
        FishActions.ValidateRequest(recall); FishActions.ValidateRequest(Request(5, FishActionKind.RequestPickup));

        InvalidRequest(r => r.RequestId = 0); InvalidRequest(r => r.PlayerId = 3); InvalidRequest(r => r.SceneEpoch = 0);
        InvalidRequest(r => r.SceneKey = new string('x', 161)); InvalidRequest(r => r.Action = (FishActionKind)99);
        InvalidRequest(r => r.TargetEntityId = 0); InvalidRequest(r => r.EquipmentSlot = 2);
        InvalidRequest(r => r.LoadoutRevision = -1); InvalidRequest(r => r.InteractionId = -1);
        InvalidRequest(r => r.AimX = float.NaN); InvalidRequest(r => r.AimY = float.PositiveInfinity); InvalidRequest(r => r.AimX = 1.01f);
        fire.AimX = 0; Throws<ProtocolException>(() => FishActions.ValidateRequest(fire));
        fire.AimX = 1; fire.LoadoutRevision = 0; Throws<ProtocolException>(() => FishActions.ValidateRequest(fire));
        qte.InteractionId = 0; Throws<ProtocolException>(() => FishActions.ValidateRequest(qte));
        recall.InteractionId = 0; Throws<ProtocolException>(() => FishActions.ValidateRequest(recall));

        FishActionResult result = Result(probe, FishActionStatus.Queued, FishActionReason.None);
        FishActions.ValidateResult(result); result.Status = FishActionStatus.Rejected;
        Throws<ProtocolException>(() => FishActions.ValidateResult(result));
        result.Reason = FishActionReason.TargetUnavailable; FishActions.ValidateResult(result);
        result.OperationId = 1; Throws<ProtocolException>(() => FishActions.ValidateResult(result));
        result = Result(probe, FishActionStatus.DryRunValidated, FishActionReason.None); FishActions.ValidateResult(result);
        result.Action = FishActionKind.FireGun; Throws<ProtocolException>(() => FishActions.ValidateResult(result));
        result = Result(Request(2, FishActionKind.FireGun), FishActionStatus.NativeStarted, FishActionReason.None);
        Throws<ProtocolException>(() => FishActions.ValidateResult(result)); result.OperationId = 1; FishActions.ValidateResult(result);
        result.Status = FishActionStatus.OutcomeUnknown; FishActions.ValidateResult(result);
        result.RequestFingerprint = new string('z', 64); Throws<ProtocolException>(() => FishActions.ValidateResult(result));
    }

    internal static void BoundSourceAndCopyOwnership()
    {
        var gate = new HostFishActionGate(Room, 2);
        ReceivedFishAction incoming = Received(Request(1));
        ReceivedFishAction bad = incoming.Copy(); bad.RoomId = OtherRoom; Throws<ProtocolException>(() => gate.Accept(bad, true, 1, "sea", 1));
        bad = incoming.Copy(); bad.BoundPlayerId = 1; Throws<ProtocolException>(() => gate.Accept(bad, true, 1, "sea", 1));
        bad = incoming.Copy(); bad.Request.PlayerId = 1; Throws<ProtocolException>(() => gate.Accept(bad, true, 1, "sea", 1));
        bad = incoming.Copy(); bad.PacketSequence = 0; Throws<ProtocolException>(() => gate.Accept(bad, true, 1, "sea", 1));
        bad = incoming.Copy(); bad.ReceivedAt = 2; Throws<ProtocolException>(() => gate.Accept(bad, true, 1, "sea", 1));
        Assert(gate.HighestRequestId == 0 && gate.PendingCount == 0, "invalid provenance consumed a participant request ID");
        string dashedRoom = Guid.Parse(Room).ToString("D");
        var formattedGate = new HostFishActionGate(dashedRoom, 2);
        ReceivedFishAction formatted = incoming.Copy(); formatted.RoomId = dashedRoom;
        Assert(formattedGate.Accept(formatted, true, 1, "sea", 1).Status == FishActionStatus.Queued,
            "gate rejected a room format accepted by the wire schema");
        formatted.RoomId = Room;
        Throws<ProtocolException>(() => formattedGate.Accept(formatted, true, 1, "sea", 1));
        Throws<ArgumentException>(() => new HostFishActionGate(Guid.Empty.ToString("N"), 2));
        FishActionResult accepted = gate.Accept(incoming, true, 1, "sea", 1);
        string expected = accepted.RequestFingerprint; incoming.Request.TargetEntityId = 99; accepted.TargetEntityId = 100;
        Assert(gate.TryTake(out ReceivedFishAction taken) && taken.Request.TargetEntityId == 7, "ingress mutation changed queued action");
        taken.Request.TargetEntityId = 101; taken.RoomId = OtherRoom;
        FishActionResult terminal = gate.Complete(1, FishActionStatus.Rejected, FishActionReason.TargetUnavailable);
        Assert(terminal.TargetEntityId == 7 && terminal.RequestFingerprint == expected && gate.PendingCount == 0,
            "egress mutation changed pending action identity");
        terminal.Reason = FishActionReason.None;
        FishActionResult replay = gate.Accept(Received(Request(1)), true, 1, "sea", 1);
        Assert(replay.Reason == FishActionReason.TargetUnavailable && replay.TargetEntityId == 7,
            "caller changed cached terminal result");
    }

    internal static void DuplicateConflictAndMonotonicReplay()
    {
        var gate = new HostFishActionGate(Room, 2);
        FishActionResult first = gate.Accept(Received(Request(1)), true, 1, "sea", 1);
        ReceivedFishAction retry = Received(Request(1)); retry.PacketSequence = 2;
        Assert(gate.Accept(retry, true, 1, "sea", 1).Status == FishActionStatus.Queued && gate.PendingCount == 1,
            "same pending intent was queued twice");
        FishActionRequest conflict = Request(1); conflict.TargetEntityId = 8;
        Assert(gate.Accept(Received(conflict), true, 1, "sea", 1).Reason == FishActionReason.RequestConflict,
            "same request ID changed its target");
        Assert(gate.TryTake(out _) && !gate.TryTake(out _), "duplicate created a second FIFO item");
        FishActionResult completed = gate.Complete(1, FishActionStatus.Rejected, FishActionReason.NativeAdapterUnavailable);
        Assert(gate.Accept(retry, true, 1, "sea", 1).Reason == completed.Reason && gate.HighestRequestId == 1,
            "terminal retry changed result or consumed another ID");
        gate.Accept(Received(Request(5)), true, 1, "sea", 1);
        Assert(gate.Accept(Received(Request(4)), true, 1, "sea", 1).Reason == FishActionReason.ReplayExpired &&
            gate.HighestRequestId == 5 && gate.PendingCount == 1 && first.RequestFingerprint == completed.RequestFingerprint,
            "a forward request gap allowed an older ID to execute");
    }

    internal static void BusinessRejectionAndSceneFence()
    {
        var gate = new HostFishActionGate(Room, 2);
        Assert(gate.Accept(Received(Request(1)), false, 0, null, 1).Reason == FishActionReason.SceneUnavailable &&
            gate.HighestRequestId == 1 && gate.PendingCount == 0, "non-ready rejection did not consume its new ID");
        gate.Accept(Received(Request(2)), true, 1, "sea", 1); Assert(gate.TryTake(out _), "ready action was not available");
        gate.InvalidateScene();
        Assert(gate.PendingCount == 0 && !gate.TryTake(out _) && gate.HighestRequestId == 2,
            "scene invalidation retained FIFO work or reset replay fence");
        FishActionRequest next = Request(3); next.SceneEpoch = 2; next.SceneKey = "deep";
        Assert(gate.Accept(Received(next), true, 2, "deep", 1).Status == FishActionStatus.Queued, "new scene failed to accept a new ID");
        Assert(gate.Accept(Received(Request(2)), true, 2, "deep", 1).Reason == FishActionReason.SceneChanged,
            "old pending result became executable after scene change");
        Assert(gate.Accept(Received(Request(4)), true, 2, "deep", 1).Reason == FishActionReason.StaleScene && gate.HighestRequestId == 4,
            "stale scene rejection bypassed high-water consumption");
        FishActionRequest future = Request(5); future.SceneEpoch = 3;
        Assert(gate.Accept(Received(future), true, 2, "deep", 1).Reason == FishActionReason.StaleScene,
            "future scene request established its own authority");
    }

    internal static void BoundedCacheAndClosedRoomReplay()
    {
        var gate = new HostFishActionGate(Room, 2);
        for (int i = 1; i <= HostFishActionGate.MaxCachedResults + 1; i++)
            gate.Accept(Received(Request(i)), false, 0, null, 1);
        Assert(gate.CachedResults == HostFishActionGate.MaxCachedResults && gate.PendingCount == 0 &&
            gate.HighestRequestId == HostFishActionGate.MaxCachedResults + 1, "terminal cache grew unbounded or reset high water");
        Assert(gate.Accept(Received(Request(1)), true, 1, "sea", 1).Reason == FishActionReason.ReplayExpired,
            "terminal eviction allowed native intent replay");
        gate.Close(); long next = gate.HighestRequestId + 1;
        Assert(gate.Accept(Received(Request(next)), true, 1, "sea", 1).Reason == FishActionReason.RoomClosed &&
            gate.HighestRequestId == next && gate.PendingCount == 0, "closed gate accepted work or forgot its fence");
        var newRoom = new HostFishActionGate(OtherRoom, 2); ReceivedFishAction fresh = Received(Request(1)); fresh.RoomId = OtherRoom;
        Assert(newRoom.Accept(fresh, true, 1, "sea", 1).Status == FishActionStatus.Queued,
            "a genuinely new room could not start a new request sequence");
    }

    internal static void QueueRateAndArrivalFreshness()
    {
        var gate = new HostFishActionGate(Room, 2);
        for (int i = 1; i <= HostFishActionGate.MaxPending; i++)
            Assert(gate.Accept(Received(Request(i)), true, 1, "sea", 1).Status == FishActionStatus.Queued, "FIFO capacity rejected too early");
        Assert(gate.Accept(Received(Request(17)), true, 1, "sea", 1).Reason == FishActionReason.QueueFull && gate.PendingCount == 16,
            "full queue dropped an old operation or accepted another");
        for (int i = 1; i <= 16; i++)
        {
            Assert(gate.TryTake(out ReceivedFishAction taken) && taken.Request.RequestId == i, "actions did not retain FIFO order");
            gate.Complete(i, FishActionStatus.Rejected, FishActionReason.TargetUnavailable);
        }
        Assert(gate.PendingCount == 0 && gate.Accept(Received(Request(18)), true, 1, "sea", 1).Reason == FishActionReason.RateLimited,
            "draining FIFO bypassed independent request rate budget");
        ReceivedFishAction refill = Received(Request(19), 1.125);
        Assert(gate.Accept(refill, true, 1, "sea", 1.125).Status == FishActionStatus.Queued && gate.TryTake(out _),
            "host monotonic refill did not restore one rate token");
        Assert(gate.ValidateFresh(19, Facts(2.13), 2.13, out _).Reason == FishActionReason.SceneUnavailable && gate.PendingCount == 0,
            "fresh facts revived an expired received request");
        Throws<ArgumentException>(() => gate.Accept(Received(Request(20), 1), true, 1, "sea", 1));
        var stale = new HostFishActionGate(Room, 2);
        Assert(stale.Accept(Received(Request(1)), true, 1, "sea", 2.01).Reason == FishActionReason.SceneUnavailable &&
            stale.HighestRequestId == 1, "stale ingress bypassed freshness or ID consumption");
    }

    internal static void ProbeFactsAndExplicitMissingAuthority()
    {
        var gate = new HostFishActionGate(Room, 2);
        gate.Accept(Received(Request(1)), true, 1, "sea", 1); gate.TryTake(out _);
        HostActionFacts facts = Facts(1);
        FishActionResult result = gate.ValidateFresh(1, facts, 1, out HostFishActionPlan plan);
        Assert(result.Status == FishActionStatus.DryRunValidated && result.OperationId == 0 && plan == null && gate.PendingCount == 0,
            "probe validation became native dispatch or retained work");
        gate.Accept(Received(Request(2, FishActionKind.FireHarpoon)), true, 1, "sea", 1); gate.TryTake(out _);
        result = gate.ValidateFresh(2, Facts(1), 1, out plan);
        Assert(result.Status == FishActionStatus.Rejected && result.Reason == FishActionReason.WorldAuthorityUnavailable && plan == null,
            "valid fish identity authorized effects without M4/guest/local arbitration");
        gate.Accept(Received(Request(3, FishActionKind.FireGun)), true, 1, "sea", 1); gate.TryTake(out _);
        facts = Facts(1, true); facts.NativeAdapterReady = false;
        result = gate.ValidateFresh(3, facts, 1, out plan);
        Assert(result.Reason == FishActionReason.NativeAdapterUnavailable && plan == null,
            "unavailable native capability produced an executable plan");
        gate.Accept(Received(Request(4)), true, 1, "sea", 1); gate.TryTake(out _);
        facts = Facts(1); facts.TargetCaptured = true;
        Assert(gate.ValidateFresh(4, facts, 1, out _).Reason == FishActionReason.TargetUnavailable,
            "captured target was mistaken for live probe permission");
    }

    internal static void FreshLoadoutStageAndSpatialGuards()
    {
        RejectFacts(f => f.ActorPositionTrusted = false, FishActionReason.ActorUnavailable);
        RejectFacts(f => f.ActorPlayerId = 1, FishActionReason.ActorUnavailable);
        RejectFacts(f => f.LocalActorArbitrated = false, FishActionReason.WorldAuthorityUnavailable);
        RejectFacts(f => f.GuestStateIsolated = false, FishActionReason.WorldAuthorityUnavailable);
        RejectFacts(f => f.LoadoutRevision = 2, FishActionReason.LoadoutUnavailable);
        RejectFacts(f => f.ResourceAvailable = false, FishActionReason.LoadoutUnavailable);
        RejectFacts(f => f.CooldownReady = false, FishActionReason.LoadoutUnavailable);
        RejectFacts(f => f.SpatialChecksAvailable = false, FishActionReason.OutOfRange);
        RejectFacts(f => f.LineOfSight = false, FishActionReason.OutOfRange);
        RejectFacts(f => f.StageValid = false, FishActionReason.InvalidStage);
        RejectFacts(f => f.TargetSupported = false, FishActionReason.TargetUnavailable);
        RejectFacts(f => f.SampledAt = 0.7, FishActionReason.SceneUnavailable);
        RejectFacts(f => f.SampledAt = 1.01, FishActionReason.SceneUnavailable);
        var gate = new HostFishActionGate(Room, 2); FishActionRequest qte = Request(1, FishActionKind.SubmitQteInput); qte.InteractionId = 8;
        gate.Accept(Received(qte), true, 1, "sea", 1); gate.TryTake(out _);
        HostActionFacts facts = Facts(1, true); facts.InteractionId = 8; facts.InteractionPlayerId = 1; facts.InteractionTargetId = 7;
        Assert(gate.ValidateFresh(1, facts, 1, out _).Reason == FishActionReason.InvalidStage,
            "client stage used another actor's native interaction");
    }

    internal static void TargetRetirementBetweenQueueAndDrain()
    {
        var registry = new HostEntityRegistry(); registry.BeginEpoch(1);
        long original = registry.Bind(101, EntityKind.Fish, 2010007, 1);
        FishActionRequest request = Request(1); request.TargetEntityId = original;
        var gate = new HostFishActionGate(Room, 2);
        gate.Accept(Received(request), true, 1, "sea", 1); gate.TryTake(out _);
        long replacement = registry.Bind(101, EntityKind.Fish, 2010007, 2);
        HostActionFacts facts = Facts(1);
        facts.TargetAvailable = registry.TryResolve(1, original, out HostEntityTarget retired); facts.Target = retired;
        FishActionResult rejected = gate.ValidateFresh(1, facts, 1, out _);
        Assert(replacement > original && rejected.Reason == FishActionReason.TargetUnavailable && gate.PendingCount == 0,
            "queued action followed a pooled instance into its new entity identity");
        Assert(gate.Accept(Received(request), true, 1, "sea", 1).Reason == FishActionReason.TargetUnavailable && !gate.TryTake(out _),
            "target retirement rejection was retried after its native object returned");
        FishActionRequest next = Request(2); next.TargetEntityId = replacement;
        gate.Accept(Received(next), true, 1, "sea", 1); gate.TryTake(out _);
        facts.TargetAvailable = registry.TryResolve(1, replacement, out HostEntityTarget current); facts.Target = current;
        Assert(gate.ValidateFresh(2, facts, 1, out _).Status == FishActionStatus.DryRunValidated,
            "a new intent could not observe the new explicitly addressed pool generation");
    }

    internal static void DispatchRechecksGenerationAndLease()
    {
        var gate = new HostFishActionGate(Room, 2);
        gate.Accept(Received(Request(1, FishActionKind.FireHarpoon)), true, 1, "sea", 1); gate.TryTake(out _);
        HostActionFacts facts = Facts(1, true);
        gate.ValidateFresh(1, facts, 1, out HostFishActionPlan plan);
        Assert(plan != null && plan.OperationId >= 1 && plan.Request.TargetEntityId == 7, "validated facts produced no local plan");
        FishActionRequest exposed = plan.Request; exposed.TargetEntityId = 99;
        Assert(plan.Request.TargetEntityId == 7, "mutable plan request escaped ownership");
        facts.Target = Target(2);
        Assert(!gate.TryMarkDispatching(plan, facts, 1, out FishActionResult rejected) &&
            rejected.Reason == FishActionReason.InvalidStage && gate.PendingCount == 0,
            "pooled target generation changed between plan and dispatch");
        gate.Accept(Received(Request(2, FishActionKind.FireHarpoon)), true, 1, "sea", 1); gate.TryTake(out _);
        facts = Facts(1, true); gate.ValidateFresh(2, facts, 1, out plan);
        facts = Facts(1.26, true);
        Assert(!gate.TryMarkDispatching(plan, facts, 1.26, out rejected) && rejected.Reason == FishActionReason.InvalidStage,
            "expired permission candidate entered native code with fresh target facts");
        gate.Accept(Received(Request(3, FishActionKind.FireHarpoon), 1.26), true, 1, "sea", 1.26); gate.TryTake(out _);
        facts = Facts(1.26, true); gate.ValidateFresh(3, facts, 1.26, out plan); facts.LoadoutRevision = 2;
        Assert(!gate.TryMarkDispatching(plan, facts, 1.26, out rejected) && rejected.Reason == FishActionReason.LoadoutUnavailable,
            "equipment changed after planning but old permission remained valid");
    }

    internal static void NativeUnknownNeverDispatchesTwice()
    {
        var gate = new HostFishActionGate(Room, 2);
        gate.Accept(Received(Request(1, FishActionKind.FireHarpoon)), true, 1, "sea", 1); gate.TryTake(out _);
        HostActionFacts facts = Facts(1, true); gate.ValidateFresh(1, facts, 1, out HostFishActionPlan plan);
        Assert(gate.TryMarkDispatching(plan, facts, 1, out _) && !gate.TryMarkDispatching(plan, facts, 1, out _),
            "native entry could be marked twice");
        Throws<InvalidOperationException>(() => gate.Complete(1, FishActionStatus.Rejected, FishActionReason.TargetUnavailable));
        FishActionResult started = gate.Complete(1, FishActionStatus.NativeStarted, FishActionReason.None, 5, plan.OperationId);
        Assert(started.Status == FishActionStatus.NativeStarted && gate.PendingCount == 1, "native start was recorded as completion");
        FishActionResult unknown = gate.Complete(1, FishActionStatus.OutcomeUnknown, FishActionReason.NativeAdapterUnavailable, 5, plan.OperationId);
        Assert(unknown.OperationId == plan.OperationId && gate.PendingCount == 0 &&
            !gate.TryMarkDispatching(plan, facts, 1, out _) && gate.Accept(Received(Request(1, FishActionKind.FireHarpoon)), true, 1, "sea", 1).Status == FishActionStatus.OutcomeUnknown,
            "uncertain entered operation became retryable or lost its native operation identity");
        gate.Accept(Received(Request(2, FishActionKind.FireHarpoon)), true, 1, "sea", 1); gate.TryTake(out _);
        Assert(gate.ValidateFresh(2, facts, 1, out _).Reason == FishActionReason.ActorUnavailable,
            "uncertain native entry released its resource reservation");
        gate.Close(); Assert(!gate.TryTake(out _), "closed room retained dispatchable work");
    }

    internal static void NotStartedReleaseAndTargetOwnership()
    {
        var gate = new HostFishActionGate(Room, 2); FishActionRequest pickup = Request(1, FishActionKind.RequestPickup);
        gate.Accept(Received(pickup), true, 1, "sea", 1); gate.TryTake(out _);
        HostActionFacts facts = Facts(1, true); gate.ValidateFresh(1, facts, 1, out HostFishActionPlan first);
        FishActionRequest rival = Request(2, FishActionKind.RequestPickup);
        gate.Accept(Received(rival), true, 1, "sea", 1); gate.TryTake(out _);
        Assert(gate.ValidateFresh(2, facts, 1, out _).Reason == FishActionReason.TargetBusy,
            "a second capture phase shared target ownership");
        Assert(gate.TryMarkDispatching(first, facts, 1, out _), "first target owner could not start dispatch");
        Assert(gate.FinishNotStarted(first, FishActionReason.NativeAdapterUnavailable).Status == FishActionStatus.Rejected,
            "positively not-started adapter did not produce explicit rejection");
        gate.Accept(Received(Request(3, FishActionKind.RequestPickup)), true, 1, "sea", 1); gate.TryTake(out _);
        gate.ValidateFresh(3, facts, 1, out HostFishActionPlan next);
        Assert(next != null && next.OperationId > first.OperationId, "not-started release retained stale target/actor leases");
        Assert(gate.TryMarkDispatching(next, facts, 1, out _), "replacement operation could not mark its own entry");
        gate.InvalidateScene();
        Assert(gate.Complete(3, FishActionStatus.Rejected, FishActionReason.TargetUnavailable).Status == FishActionStatus.OutcomeUnknown &&
            gate.HighestRequestId == 3 && gate.PendingCount == 0, "scene invalidation converted native uncertainty into not-started retry");
    }

    private static void RejectFacts(Action<HostActionFacts> mutation, FishActionReason expected)
    {
        var gate = new HostFishActionGate(Room, 2);
        gate.Accept(Received(Request(1, FishActionKind.FireHarpoon)), true, 1, "sea", 1); gate.TryTake(out _);
        HostActionFacts facts = Facts(1, true); mutation(facts);
        Assert(gate.ValidateFresh(1, facts, 1, out HostFishActionPlan plan).Reason == expected && plan == null && gate.PendingCount == 0,
            "fresh facts failed to reject " + expected);
    }

    private static HostActionFacts Facts(double at, bool effects = false) => new HostActionFacts
    {
        SceneEpoch = 1, SceneKey = "sea", WorldRevision = 5, SampledAt = at,
        TargetAvailable = true, Target = Target(1), TargetSupported = true,
        MapAuthorityReady = effects, GuestStateIsolated = effects, LocalActorArbitrated = effects,
        ActorAvailable = effects, ActorPermitted = effects, ActorPositionTrusted = effects, ActorPlayerId = 2, ActorRevision = 3,
        LoadoutAvailable = effects, LoadoutRevision = 1, EquipmentSlot = 0,
        CooldownReady = effects, ResourceAvailable = effects,
        SpatialChecksAvailable = effects, WithinRange = effects, LineOfSight = effects,
        StageValid = effects, NativeAdapterReady = effects
    };

    private static HostEntityTarget Target(long generation) => new HostEntityTarget(1, 7, 101, EntityKind.Fish, 2010007, generation);
    private static FishActionRequest Request(long id, FishActionKind kind = FishActionKind.ProbeTarget) => new FishActionRequest
    {
        RequestId = id, PlayerId = 2, SceneEpoch = 1, SceneKey = "sea", Action = kind,
        TargetEntityId = 7, EquipmentSlot = 0, LoadoutRevision = 1,
        AimX = kind == FishActionKind.FireHarpoon || kind == FishActionKind.FireGun ? 1 : 0
    };
    private static ReceivedFishAction Received(FishActionRequest request, double at = 1) => new ReceivedFishAction
    {
        RoomId = Room, BoundPlayerId = 2, PacketSequence = request.RequestId, ReceivedAt = at, Request = request
    };
    private static FishActionResult Result(FishActionRequest request, FishActionStatus status, FishActionReason reason) => new FishActionResult
    {
        RequestId = request.RequestId, PlayerId = request.PlayerId, SceneEpoch = request.SceneEpoch, SceneKey = request.SceneKey,
        Action = request.Action, TargetEntityId = request.TargetEntityId, Status = status, Reason = reason,
        RequestFingerprint = FishActions.Fingerprint(request)
    };
    private static void InvalidRequest(Action<FishActionRequest> mutation)
    {
        FishActionRequest request = Request(1); mutation(request); Throws<ProtocolException>(() => FishActions.ValidateRequest(request));
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
