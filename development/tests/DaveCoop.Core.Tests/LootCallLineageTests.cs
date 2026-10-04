using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DaveCoop.Core.Cargo;

internal static class LootCallLineageTests
{
    internal static void SameFishDropPlusBagAndRollKeepFixedEnclosure()
    {
        var registry = New(); LootLineageSource fish = Fish();
        LootLineageToken drop = Begin(registry, 1, 1, true, fish);
        LootLineageToken plus = Begin(registry, 2, 5, true, Fish());
        LootLineageToken roll = Begin(registry, 3, 15);
        Assert(registry.RecordPostfix(roll, null, 207) && registry.FinalizeCall(roll, false), "natural integer return did not pair");
        LootLineageToken bag = Begin(registry, 4, 2);
        LootLineageToken setCount = Begin(registry, 5, 17);
        LootLineageToken setGrade = Begin(registry, 6, 18);
        LootLineageToken setFinalGrade = Begin(registry, 7, 19);
        Assert(setCount.ParentCallId == bag.CallId && setGrade.ParentCallId == setCount.CallId &&
            setFinalGrade.ParentCallId == setGrade.CallId && setCount.SourceRootCallId == drop.CallId &&
            setGrade.SourceRootCallId == drop.CallId && setFinalGrade.SourceRootCallId == drop.CallId &&
            !setCount.SourceShadowed && !setGrade.SourceShadowed && !setFinalGrade.SourceShadowed,
            "non-fish slot callbacks changed the fixed main/plus synchronous enclosure");
        Assert(registry.RecordPostfix(setFinalGrade, null), "slot setter postfix did not retain its fixed token");
        // A later postfix callback can still enclose another natural call before
        // finalizers run. Its source must remain the setter's fixed parent chain.
        LootLineageToken postfixChild = Begin(registry, 8, 17);
        Assert(postfixChild.ParentCallId == setFinalGrade.CallId && postfixChild.SourceRootCallId == drop.CallId &&
            postfixChild.Source.EntityId == fish.EntityId && postfixChild.Depth == setFinalGrade.Depth + 1,
            "postfix popped the setter before its finalizer or rebound a nested slot callback");
        Finish(registry, postfixChild);
        Assert(registry.FinalizeCall(setFinalGrade, false), "slot setter finalizer could not close after its child");
        Finish(registry, setGrade); Finish(registry, setCount);
        Finish(registry, bag, true); Finish(registry, plus); Finish(registry, drop);
        List<LootLineageRecord> records = Drain(registry);
        Assert(records.Count == 24 && registry.Healthy && registry.PendingCount == 0, "valid nested scopes did not close exactly once");
        Assert(drop.SourceRootCallId == 1 && plus.ParentCallId == 1 && plus.SourceRootCallId == 1 && plus.Depth == 1 &&
            roll.ParentCallId == 2 && roll.Depth == 2 && bag.ParentCallId == 2 && bag.SourceRootCallId == 1 && !bag.SourceShadowed,
            "same fish plus branch or nested bag was rebound to a new fish root");
        for (int index = 0; index < records.Count; index++)
        {
            LootLineageRecord record = records[index];
            Assert(record.Sequence == index + 1 && record.RunId == registry.RunId && record.SourceKnown &&
                record.SourceRootCallId == 1 && record.CandidateSource.EntityId == fish.EntityId && record.HealthyAtCapture,
                "immutable record lost its fixed scalar chain"); NoAuthority(record);
        }
        LootLineageRecord integer = records.Single(record => record.CallId == 3 && record.Stage == LootLineageStage.Postfix);
        Assert(integer.OriginalReturn == null && integer.OriginalIntReturn == 207 &&
            records.Single(record => record.CallId == 3 && record.Stage == LootLineageStage.Finalizer).OriginalIntReturn == 207,
            "roll return was converted into bool, rerolled or lost at finalizer");
        Assert(records.Where(record => record.Stage == LootLineageStage.Finalizer).Select(record => record.CallId)
            .SequenceEqual(new long[] { 3, 8, 7, 6, 5, 4, 2, 1 }),
            "new slot callbacks did not retain finalizer LIFO order within the original enclosure");
        NoAuthority(registry);
    }

    internal static void DifferentFishAndUnknownScopesMaskTheOuterCandidate()
    {
        var registry = New(); LootLineageToken outer = Begin(registry, 1, 1, true, Fish());
        LootLineageToken other = Begin(registry, 2, 1, true, Fish(ordinal: 2, entity: 200));
        LootLineageToken bagB = Begin(registry, 3, 2);
        Assert(other.SourceRootCallId == 2 && other.ParentCallId == 1 && other.SourceShadowed &&
            bagB.SourceRootCallId == 2 && bagB.Source.EntityId == 200, "different fish borrowed the outer fish's candidate");
        Finish(registry, bagB); Finish(registry, other);
        // Unknown fish/async boundaries are deliberately the same unbound mask.
        // There is no iterator ownership or late singleton source recovery.
        LootLineageToken unknown = Begin(registry, 4, 6, true, null);
        LootLineageToken unboundBag = Begin(registry, 5, 2);
        LootLineageToken setCount = Begin(registry, 6, 17);
        LootLineageToken setGrade = Begin(registry, 7, 18);
        LootLineageToken setFinalGrade = Begin(registry, 8, 19);
        Assert(unknown.ParentCallId == 1 && unknown.SourceShadowed && !unknown.SourceKnown && unknown.SourceRootCallId == 0 &&
            unboundBag.SourceShadowed && unboundBag.Source == null && unboundBag.SourceRootCallId == 0,
            "unknown boundary or its descendants inherited a nearby/outer source");
        foreach (LootLineageToken slot in new[] { setCount, setGrade, setFinalGrade })
            Assert(slot.SourceShadowed && !slot.SourceKnown && slot.Source == null && slot.SourceRootCallId == 0,
                "non-fish slot mutation recovered an outer source through an unknown boundary");
        Assert(setCount.ParentCallId == unboundBag.CallId && setGrade.ParentCallId == setCount.CallId &&
            setFinalGrade.ParentCallId == setGrade.CallId && registry.RecordPostfix(setFinalGrade, null),
            "unknown slot callbacks lost fixed parent identity or their original postfix");
        LootLineageToken postfixChild = Begin(registry, 9, 17);
        Assert(postfixChild.ParentCallId == setFinalGrade.CallId && postfixChild.SourceShadowed &&
            postfixChild.Source == null && postfixChild.SourceRootCallId == 0,
            "unknown setter postfix removed its source mask before finalization");
        Finish(registry, postfixChild);
        Assert(registry.FinalizeCall(setFinalGrade, false), "unknown setter did not close in fixed LIFO order");
        Finish(registry, setGrade); Finish(registry, setCount);
        Finish(registry, unboundBag); Finish(registry, unknown);
        LootLineageToken restored = Begin(registry, 10, 2);
        Assert(restored.ParentCallId == 1 && restored.SourceRootCallId == 1 && restored.Source.EntityId == outer.Source.EntityId,
            "normal mask cleanup changed the still-active outer prefix source");
        Finish(registry, restored); Finish(registry, outer);
        LootLineageToken standalone = Begin(registry, 11, 2);
        Assert(!standalone.SourceKnown && standalone.ParentCallId == 0 && standalone.SourceRootCallId == 0 && !standalone.SourceShadowed,
            "standalone bag call guessed a previous fish from time or TID");
        Finish(registry, standalone); Assert(registry.Healthy, "unbound candidate was incorrectly treated as capture permission or fatal loss");
        foreach (LootLineageRecord record in Drain(registry)) NoAuthority(record);
    }

    internal static void FrozenSourceAndPoolGenerationNeverRebindAfterPrefix()
    {
        var registry = New(); LootLineageSource supplied = Fish();
        LootLineageToken old = Begin(registry, 1, 1, true, supplied);
        LootLineageRecord prefix = Drain(registry).Single();
        Assert(!ReferenceEquals(old.Source, supplied) && !ReferenceEquals(prefix.Source, old.Source), "source copy ownership was shared across callback boundaries");
        supplied = Fish(generation: 2);
        LootLineageToken reused = Begin(registry, 2, 1, true, supplied);
        Assert(reused.SourceRootCallId == 2 && reused.Source.Generation == 2 && reused.SourceShadowed &&
            old.Source.Generation == 1 && prefix.Source.Generation == 1 && prefix.SourceRootCallId == 1,
            "same ordinal/entity with a new pool generation changed a fixed old prefix");
        Finish(registry, reused);
        LootLineageToken child = Begin(registry, 3, 2);
        Assert(child.Source.Generation == 1 && child.SourceRootCallId == old.CallId, "post-drain child resolved the old source using a new generation");
        Finish(registry, child); Finish(registry, old);
        Assert(prefix.Source.DataTid == 101 && prefix.CallId == 1 && prefix.Stage == LootLineageStage.Prefix,
            "later callbacks mutated a held observation");
        Throws(() => new LootLineageSource(0, 1, 1, 1, 1)); Throws(() => new LootLineageSource(1, 0, 1, 1, 1));
        Throws(() => new LootLineageSource(1, 1, 0, 1, 1)); Throws(() => new LootLineageSource(1, 1, 1, 0, 1));
        Throws(() => new LootLineageSource(1, 1, 1, 1, 0)); NoAuthority(prefix);
    }

    internal static void PostfixAndOriginalExceptionNeverMeanCaptureTerminal()
    {
        var registry = New(); LootLineageToken outer = Begin(registry, 1, 1, true, Fish());
        LootLineageToken bag = Begin(registry, 2, 2);
        Assert(registry.RecordPostfix(bag, true) && registry.PendingCount == 2, "postfix popped the scope before a later original exception");
        Assert(registry.FinalizeCall(bag, true) && !registry.Healthy && registry.IntegrityLost && registry.OriginalExceptions == 1 &&
            registry.DiscardedPending == 1 && registry.PendingCount == 0, "late exception retained complete evidence or left an outer candidate live");
        List<LootLineageRecord> rows = Drain(registry);
        LootLineageRecord finalizer = rows.Last();
        Assert(finalizer.Stage == LootLineageStage.Finalizer && finalizer.CallId == bag.CallId && finalizer.OriginalReturn == true &&
            finalizer.OriginalException && !finalizer.HealthyAtCapture, "original bool or exception was overwritten at finalizer");
        long sequence = registry.LastSequence;
        Assert(registry.FinalizeCall(bag, false) && registry.FinalizeCall(bag, true) && registry.LastSequence == sequence &&
            registry.OriginalExceptions == 1 && !registry.FinalizeCall(outer, true), "duplicate finalizer changed evidence or revived an abandoned parent");
        Assert(registry.Begin(3, 1, true, Fish(generation: 2), Main) == null, "a new known fish root healed exception loss");
        var noPost = New(); LootLineageToken thrown = Begin(noPost, 1, 1, true, Fish());
        Assert(noPost.FinalizeCall(thrown, true) && noPost.Unmatched == 0 && noPost.OriginalExceptions == 1 && !noPost.Healthy,
            "exception-only completion required an original postfix that never ran");
        foreach (LootLineageRecord row in rows) NoAuthority(row); NoAuthority(noPost);
    }

    internal static void MissingPostfixAndChangedScalarResultsLoseIntegrity()
    {
        var missing = New(); LootLineageToken token = Begin(missing, 1, 2);
        Assert(missing.FinalizeCall(token, false) && missing.Unmatched == 1 && !missing.Healthy &&
            !Drain(missing).Last().HealthyAtCapture, "normal finalizer repaired an absent postfix");
        var duplicate = New(); token = Begin(duplicate, 1, 2);
        Assert(duplicate.RecordPostfix(token, false) && duplicate.RecordPostfix(token, false) && duplicate.RunEvents == 2,
            "identical postfix replay emitted another original return");
        Assert(!duplicate.RecordPostfix(token, true) && !duplicate.Healthy && duplicate.DiscardedPending == 1,
            "conflicting postfix changed the recorded original bool");
        var integer = New(); token = Begin(integer, 1, 15);
        Assert(integer.RecordPostfix(token, null, -1) && integer.RecordPostfix(token, null, -1), "original integer sentinel was reinterpreted or rejected");
        Assert(!integer.RecordPostfix(token, null, 1) && !integer.Healthy, "conflicting integer return silently replaced the old scalar");
        var mixed = New(); token = Begin(mixed, 1, 2);
        Assert(!mixed.RecordPostfix(token, true, 207) && !mixed.Healthy, "one exact original return was both bool and integer");
        var absentFinal = New(); Begin(absentFinal, 1, 1, true, Fish());
        absentFinal.Invalidate("Own finalizer was lost before this original scope could be closed.");
        Assert(absentFinal.DiscardedPending == 1 && !absentFinal.Healthy && absentFinal.IntegrityLost,
            "missing finalizer was inferred as normal return");
        NoAuthority(missing); NoAuthority(duplicate); NoAuthority(integer); NoAuthority(mixed); NoAuthority(absentFinal);
    }

    internal static void WrongThreadCannotSupplyOrCloseAParentScope()
    {
        var registry = New(); LootLineageToken parent = Begin(registry, 1, 1, true, Fish());
        int main = Main;
        OnWorker(() => Assert(registry.Begin(2, 2, false, null, main) == null, "worker supplied main ID to inherit a fixed main-thread source"));
        Assert(!registry.Healthy && registry.WrongThreadCalls == 1 && registry.DiscardedPending == 1 && registry.HighestCallId == 1,
            "wrong-thread prefix rebound identity or advanced the accepted source fence");
        var ending = New(); LootLineageToken token = Begin(ending, 1, 2);
        OnWorker(() => Assert(!ending.FinalizeCall(token, true), "worker finalized a main prefix"));
        Assert(ending.WrongThreadCalls == 1 && ending.DiscardedPending == 1 && ending.OriginalExceptions == 0 && !ending.Healthy,
            "wrong-thread finalizer invented a paired original exception");
        var supplied = New();
        Assert(supplied.Begin(1, 2, false, null, 0) == null && supplied.WrongThreadCalls == 1, "invalid supplied callback thread was accepted");
        Assert(!registry.RecordPostfix(parent, true), "main postfix restored health after cross-thread loss");
        NoAuthority(registry); NoAuthority(ending); NoAuthority(supplied);
    }

    internal static void HighWaterForeignRunAndLifoChecksRejectRebinding()
    {
        var replay = New(); LootLineageToken old = Begin(replay, 20, 2); Finish(replay, old);
        Assert(replay.Begin(19, 2, false, null, Main) == null && replay.ReplayRejected == 1 && replay.HighestCallId == 20 && !replay.Healthy,
            "lower CallId replay reopened a completed scope");
        var fresh = New(); LootLineageToken replacement = Begin(fresh, 20, 2);
        Assert(fresh.RunId != replay.RunId && !fresh.FinalizeCall(old, false) && fresh.Unmatched == 1 && fresh.DiscardedPending == 1 &&
            replacement.CallId == old.CallId && !fresh.Healthy, "same CallId old token attached to a new Run's parent");
        foreach (bool postfix in new[] { false, true })
        {
            var lifo = New(); LootLineageToken outer = Begin(lifo, 1, 1, true, Fish()); Begin(lifo, 2, 2);
            bool accepted = postfix ? lifo.RecordPostfix(outer, true) : lifo.FinalizeCall(outer, true);
            Assert(!accepted && lifo.Unmatched == 1 && lifo.DiscardedPending == 2 && !lifo.Healthy,
                "non-top parent completion silently repaired a missing child scope");
        }
        var declaration = New(); Assert(declaration.Begin(1, 0, false, null, Main) == null && !declaration.Healthy, "missing exact declaration was accepted");
        var mixed = New(); Assert(mixed.Begin(1, 2, false, Fish(), Main) == null && !mixed.Healthy, "non-fish callback injected an arbitrary fish source");
        NoAuthority(replay); NoAuthority(fresh); NoAuthority(mixed);
    }

    internal static void QueueDepthAndRunQuotaLossNeverHealAfterDrain()
    {
        var queue = New(); long id = 0;
        while (queue.Healthy)
        {
            LootLineageToken token = queue.Begin(++id, 2, false, null, Main);
            if (token == null || !queue.RecordPostfix(token, true) || !queue.FinalizeCall(token, false)) break;
        }
        Assert(queue.QueuedCount == LootCallLineage.MaxQueued && queue.Dropped == 1 && !queue.Healthy, "queue overflow silently evicted caller evidence");
        long high = queue.HighestCallId, sequence = queue.LastSequence;
        Assert(Drain(queue).Count == LootCallLineage.MaxQueued && !queue.Healthy &&
            queue.Begin(id + 1, 1, true, Fish(), Main) == null && queue.HighestCallId == high && queue.LastSequence == sequence,
            "draining a broken queue reset replay fences or allowed a new known root");
        var depth = New();
        for (int index = 1; index <= LootCallLineage.MaxDepth; index++) { Begin(depth, index, 2); Drain(depth); }
        Assert(depth.PendingCount == LootCallLineage.MaxDepth && depth.Begin(LootCallLineage.MaxDepth + 1, 2, false, null, Main) == null &&
            depth.DiscardedPending == LootCallLineage.MaxDepth && depth.PendingCount == 0 && depth.Dropped == 1 && !depth.Healthy,
            "depth capacity evicted or aliased a pending parent");
        var run = New(); id = 0;
        while (run.RunEvents + 3 <= LootCallLineage.MaxRunEvents)
        { LootLineageToken token = Begin(run, ++id, 2); Finish(run, token); Drain(run); }
        Assert(run.RunEvents == LootCallLineage.MaxRunEvents - 2, "fixture did not reach the exact three-stage lifetime boundary");
        LootLineageToken last = Begin(run, ++id, 2);
        Assert(run.RecordPostfix(last, false) && !run.FinalizeCall(last, false) && run.RunEvents == LootCallLineage.MaxRunEvents &&
            run.Dropped == 1 && !run.Healthy, "drained Run bypassed its permanent event quota");
        Drain(run); Assert(!run.Healthy && run.IntegrityLost, "empty queue healed lifetime quota loss");
        Assert(LootCallLineage.MaxContexts >= LootCallLineage.MaxDepth, "context ceiling cannot accommodate permitted depth");
        NoAuthority(queue); NoAuthority(depth); NoAuthority(run);
    }

    internal static void StopCountsPendingAndQueuedEvidenceWithoutRestart()
    {
        var registry = New(); LootLineageToken outer = Begin(registry, 1, 1, true, Fish());
        Assert(registry.RecordPostfix(outer, true), "stop setup original return failed");
        LootLineageToken child = Begin(registry, 2, 2);
        registry.Stop("Observer stopped with original scopes outstanding.");
        Assert(registry.DiscardedQueue == 3 && registry.DiscardedPending == 2 && registry.QueuedCount == 0 && registry.PendingCount == 0 &&
            !registry.Healthy && registry.IntegrityLost && registry.HighestCallId == 2 && registry.LastSequence == 3,
            "stop erased unknown counts or invented terminal outcomes");
        registry.Stop("duplicate stop");
        Assert(registry.DiscardedQueue == 3 && registry.DiscardedPending == 2 && !registry.TryTake(out _) &&
            !registry.FinalizeCall(child, true) && !registry.RecordPostfix(outer, true) && registry.Begin(3, 1, true, Fish(generation: 2), Main) == null,
            "stopped token completed, duplicate stop changed counters or registry restarted");
        NoAuthority(registry);
    }

    private static int Main => Environment.CurrentManagedThreadId;
    private static LootCallLineage New() => new LootCallLineage(Main);
    private static LootLineageSource Fish(long ordinal = 1, long entity = 100, long generation = 1) => new LootLineageSource(ordinal, 1, entity, generation, 101);
    private static LootLineageToken Begin(LootCallLineage registry, long id, int method, bool fish = false, LootLineageSource source = null)
    { LootLineageToken token = registry.Begin(id, method, fish, source, Main); Assert(token != null, "valid scalar prefix was rejected: " + registry.Reason); return token; }
    private static void Finish(LootCallLineage registry, LootLineageToken token, bool? result = null)
    { Assert(registry.RecordPostfix(token, result) && registry.FinalizeCall(token, false), "valid original call could not finish: " + registry.Reason); }
    private static List<LootLineageRecord> Drain(LootCallLineage registry)
    { var records = new List<LootLineageRecord>(); while (registry.TryTake(out LootLineageRecord record)) records.Add(record); return records; }
    private static void OnWorker(Action action)
    {
        Exception error = null; var worker = new Thread(() => { try { action(); } catch (Exception caught) { error = caught; } });
        worker.Start(); worker.Join(); if (error != null) throw new InvalidOperationException("Scalar worker fixture failed.", error);
    }
    private static void NoAuthority(LootLineageRecord record) => Assert(record.ObservationOnly && !record.NativeSourceOperationBound &&
        !record.SourceOperationBound && !record.MemberOwnershipVerified && !record.NativeRewards && !record.FullYield && !record.YieldComplete &&
        !record.ActualBagDelta && !record.CaptureSuccess && !record.NativeGenerationVerified && !record.NativeHookAbiVerified,
        "synchronous enclosure or original scalar granted capture/member/reward authority");
    private static void NoAuthority(LootCallLineage registry) => Assert(!registry.NativeSourceOperationBound && !registry.SourceOperationBound &&
        !registry.MemberOwnershipVerified && !registry.NativeRewards && !registry.FullYield && !registry.YieldComplete && !registry.ActualBagDelta &&
        !registry.CaptureSuccess && !registry.NativeGenerationVerified && !registry.NativeHookAbiVerified,
        "pure caller registry granted native execution or cargo receipt authority");
    private static void Throws(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Expected incomplete source rejection."); }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
