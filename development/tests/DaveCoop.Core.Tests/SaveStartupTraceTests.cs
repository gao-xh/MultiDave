using System;
using System.Collections.Generic;
using System.Threading;
using DaveCoop.Core.Guest;

internal static class SaveStartupTraceTests
{
    internal static void NestedReentrantCallsKeepFixedPairing()
    {
        var trace = Ready(); TakeAll(trace);
        Assert(trace.BeginCall("SaveSystem.InitSaveSystem", 3, out long parent), "outer natural call was rejected");
        Assert(trace.BeginCall("SaveSystem.InitSaveSystem", 4, out long child), "reentrant same-method call was rejected");
        Assert(trace.CompleteCall(child, 5, false) && trace.CompleteCall(child, 6, false), "postfix and normal finalizer did not pair idempotently");
        Assert(trace.CompleteCall(parent, 7, false), "parent could not finish after its fixed child");
        List<SaveStartupCallObservation> rows = TakeAll(trace);
        Assert(rows.Count == 4 && rows[0].CallId == parent && rows[0].ParentCallId == 0 && rows[0].Depth == 0 &&
            rows[1].CallId == child && rows[1].ParentCallId == parent && rows[1].Depth == 1 &&
            rows[2].CallId == child && rows[2].Stage == SaveStartupTraceStage.Postfix &&
            rows[3].CallId == parent && rows[3].Stage == SaveStartupTraceStage.Postfix,
            "same method name rebound a child/parent return or changed nesting order");
        for (int index = 0; index < rows.Count; index++)
        {
            Assert(rows[index].Sequence == index + 4 && rows[index].RunId == trace.RunId && rows[index].UnityThreadCandidate &&
                !rows[index].BeforeFirstUnityUpdate, "callback sequence, Run or observed Update thread was not fixed");
            NoAuthority(rows[index]);
        }
        SaveStartupCallObservation held = rows[1];
        Assert(trace.BeginCall("Base.LoadData", 8, out long next) && trace.CompleteCall(next, 9, false) && next > child,
            "new call reused an earlier process identity");
        Assert(held.CallId == child && held.ParentCallId == parent && held.MethodKey == "SaveSystem.InitSaveSystem" && held.Depth == 1,
            "later callbacks mutated a queued immutable observation");
        Assert(trace.Healthy && !trace.IntegrityLost && trace.PendingCount == 0 && trace.CompleteCalls == 3 && trace.DuplicateCompletions == 1,
            "normal finalizer invented a new result, event or pending scope");
        NoAuthority(trace);
    }

    internal static void ExceptionFinalizersAndMissingPairsLoseEvidence()
    {
        var trace = Ready(); TakeAll(trace);
        Assert(trace.BeginCall("Base.LoadData", 3, out long call) && trace.CompleteCall(call, 4, true), "exception finalizer was not observed");
        List<SaveStartupCallObservation> exception = TakeAll(trace);
        Assert(exception.Count == 2 && exception[1].CallId == call && exception[1].OriginalException &&
            exception[1].Stage == SaveStartupTraceStage.Finalizer && trace.OriginalExceptions == 1 &&
            !trace.Healthy && trace.IntegrityLost && !trace.CanReadBoundThread(),
            "an original exception retained a complete chain or typed read candidate");
        Assert(trace.CompleteCall(call, 5, true) && trace.OriginalExceptions == 1 && trace.QueuedCount == 0,
            "duplicate exception finalizer changed identity or recorded another exception");

        var late = Ready(); TakeAll(late);
        Assert(late.BeginCall("SaveSystem.LoadAllData", 3, out call) && late.CompleteCall(call, 4, false), "return was not captured");
        Assert(late.CompleteCall(call, 5, true), "exception after recorded return was discarded as a normal duplicate");
        List<SaveStartupCallObservation> upgraded = TakeAll(late);
        Assert(upgraded.Count == 3 && upgraded[1].Stage == SaveStartupTraceStage.Postfix &&
            upgraded[2].Stage == SaveStartupTraceStage.Finalizer && upgraded[2].CallId == call &&
            late.CompleteCalls == 1 && late.OriginalExceptions == 1 && !late.Healthy,
            "late pipeline exception duplicated a successful call or failed to revoke evidence");

        var unknown = Ready(); TakeAll(unknown);
        Assert(!unknown.CompleteCall(long.MaxValue, 3, true) && unknown.UnmatchedCalls == 1 && unknown.IntegrityLost && !unknown.Healthy,
            "a finalizer without prefix invented an original call");
        var missingChild = Ready(); TakeAll(missingChild);
        Assert(missingChild.BeginCall("SaveSystem.Init", 3, out long outer) && missingChild.BeginCall("Base.LoadData", 4, out _), "nested setup failed");
        Assert(!missingChild.CompleteCall(outer, 5, false) && missingChild.UnmatchedCalls == 1 && missingChild.AbandonedCalls == 2 && !missingChild.Healthy,
            "parent return silently repaired a missing child/finalizer");
        NoAuthority(trace); NoAuthority(late); NoAuthority(unknown); NoAuthority(missingChild);
    }

    internal static void QueueContextAndRunBoundsLatchLossAfterDrain()
    {
        var queue = Ready(); double at = 3;
        while (queue.Healthy)
        {
            if (!queue.BeginCall("Base.LoadData", at++, out long id)) break;
            if (!queue.CompleteCall(id, at++, false)) break;
        }
        Assert(queue.QueuedCount == SaveStartupTrace.MaxEvents && queue.DroppedEvents == 1 && queue.IntegrityLost && !queue.Healthy,
            "undrained startup burst silently evicted evidence or exceeded queue bound");
        long high = queue.HighestCallId; long sequence = queue.LastSequence;
        Assert(TakeAll(queue).Count == SaveStartupTrace.MaxEvents && !queue.Healthy && queue.IntegrityLost &&
            !queue.BeginCall("SaveSystem.LoadGame", at, out _) && queue.HighestCallId == high && queue.LastSequence == sequence,
            "draining loss resurrected the same Run or reset its replay fence");

        var contexts = Ready(); TakeAll(contexts); at = 3;
        for (int index = 0; index < SaveStartupTrace.MaxPendingCalls; index++)
        {
            Assert(contexts.BeginCall("SaveSystem.Init", at++, out _), "valid bounded nesting was rejected before capacity");
            TakeAll(contexts);
        }
        Assert(contexts.PendingCount == SaveStartupTrace.MaxPendingCalls && !contexts.BeginCall("Base.LoadData", at, out _) &&
            contexts.AbandonedCalls == SaveStartupTrace.MaxPendingCalls && contexts.PendingCount == 0 && contexts.DroppedEvents == 1 && !contexts.Healthy,
            "context capacity evicted or reassigned live prefix identities");

        var quota = Ready(); TakeAll(quota); at = 3;
        while (quota.RunEvents < SaveStartupTrace.MaxRunEvents - 1)
        {
            Assert(quota.BeginCall("Base.LoadData", at++, out long id) && quota.CompleteCall(id, at++, false), "bounded drained Run failed early");
            TakeAll(quota);
        }
        long last = 0;
        Assert(quota.RunEvents == SaveStartupTrace.MaxRunEvents - 1 && quota.BeginCall("Base.LoadData", at++, out last),
            "exact Run boundary was not reached with bounded completed calls");
        Assert(!quota.CompleteCall(last, at, false) && quota.RunEvents == SaveStartupTrace.MaxRunEvents && quota.DroppedEvents == 1 && !quota.Healthy,
            "draining events bypassed lifetime quota or hid the missing terminal event");
        TakeAll(quota);
        Assert(!quota.Healthy && quota.IntegrityLost && !quota.CanReadBoundThread(), "quota loss healed after its final queued evidence was drained");
        NoAuthority(queue); NoAuthority(contexts); NoAuthority(quota);
    }

    internal static void ThreadBindingRequiresActualUpdateMarkerAndRejectsWrongThread()
    {
        var trace = New(); trace.MarkHooksInstalled(1);
        Assert(!trace.CanReadBoundThread() && trace.UnityThreadId == 0, "Plugin.Load thread was promoted to Unity without Update");
        OnWorker(() =>
        {
            Assert(trace.BeginCall("Base.LoadData", 2, out long id) && trace.CompleteCall(id, 3, false), "pre-Update scalar-only callbacks were rejected");
            Assert(!trace.CanReadBoundThread(), "worker obtained typed native read candidate before Unity was observed");
        });
        List<SaveStartupCallObservation> before = TakeAll(trace);
        SaveStartupCallObservation workerPrefix = before.Find(row => row.Stage == SaveStartupTraceStage.Prefix);
        Assert(workerPrefix != null && workerPrefix.BeforeFirstUnityUpdate && !workerPrefix.UnityThreadCandidate &&
            workerPrefix.ThreadId != Environment.CurrentManagedThreadId, "early worker scalar evidence was labeled as Unity-typed");
        trace.ObserveFirstUnityUpdate(4);
        Assert(trace.CanReadBoundThread() && trace.UnityThreadId == Environment.CurrentManagedThreadId, "first observed Update did not bind its actual current thread");
        int main = trace.UnityThreadId;
        OnWorker(() => Assert(!trace.CanReadBoundThread(), "off-thread typed read candidate was accepted"));
        Assert(!trace.Healthy && trace.IntegrityLost && trace.WrongThreadCalls == 1 && trace.UnityThreadId == main,
            "wrong thread rebound Unity or failed to revoke evidence");

        var pending = Ready(); TakeAll(pending);
        Assert(pending.BeginCall("SaveSystem.LoadAllData", 3, out long call), "fixed prefix setup failed");
        OnWorker(() => Assert(!pending.CompleteCall(call, 4, false), "different thread completed a fixed prefix"));
        Assert(pending.WrongThreadCalls == 1 && pending.AbandonedCalls == 1 && pending.PendingCount == 0 && !pending.Healthy,
            "different-thread postfix guessed the original thread's call");
        NoAuthority(trace); NoAuthority(pending);
    }

    internal static void StoppedRunsRetainIdentityAndRejectOldCompletions()
    {
        var old = Ready(); TakeAll(old);
        Assert(old.BeginCall("SaveSystem.InitSaveSystem", 3, out long oldCall), "outstanding original setup failed");
        old.Retire("Observer stopped while an original call was outstanding.");
        long sequence = old.LastSequence;
        Assert(old.AbandonedCalls == 1 && old.PendingCount == 0 && !old.Healthy && old.IntegrityLost &&
            !old.CompleteCall(oldCall, 4, false) && !old.BeginCall("Base.LoadData", 4, out _) && old.HighestCallId == oldCall,
            "stop treated an unknown pending result as returned or permitted token reuse");
        TakeAll(old); old.Retire("duplicate stop"); old.ObserveFirstUnityUpdate(5);
        Assert(old.LastSequence == sequence && !old.Healthy && !old.CanReadBoundThread(), "duplicate stop/Update restarted a retired Run");

        var fresh = Ready(); TakeAll(fresh);
        Assert(fresh.RunId != old.RunId && fresh.BeginCall("Base.LoadData", 3, out long next) && next > oldCall,
            "new Run reused an old prefix __state identity");
        Assert(!fresh.CompleteCall(oldCall, 4, false) && fresh.UnmatchedCalls == 1 && fresh.AbandonedCalls == 1 && !fresh.Healthy,
            "late old completion attached to the new Run's current original call");
        var gap = Ready(); TakeAll(gap);
        gap.Invalidate("An adapter read/partial installation reported a gap."); TakeAll(gap);
        Assert(!gap.Healthy && gap.IntegrityLost && !gap.CanReadBoundThread() && !gap.BeginCall("Base.LoadData", 3, out _),
            "explicit adapter loss was cleared by diagnostic draining");
        NoAuthority(old); NoAuthority(fresh); NoAuthority(gap);
    }

    internal static void LateAbsentAndIncompleteEarlyBoundariesNeverGrantLoadSafety()
    {
        var empty = Ready();
        Assert(empty.Healthy && !empty.IntegrityLost && empty.CompleteCalls == 0 && empty.PendingCount == 0,
            "empty trace scalar state was inconsistent");
        // No Load event is evidence of absence only within the observed trace;
        // Plugin.Load may follow earlier game loads or partial hook setup.
        NoAuthority(empty);
        foreach (SaveStartupCallObservation observation in TakeAll(empty)) NoAuthority(observation);

        var late = New(); late.ObserveFirstUnityUpdate(1);
        Assert(late.LateInstallation && late.IntegrityLost && !late.CanReadBoundThread(), "late hook installation was labeled complete");
        late.MarkHooksInstalled(2);
        Assert(late.Healthy && late.IntegrityLost && late.CanReadBoundThread() && late.BeginCall("Base.LoadData", 3, out _),
            "late scalar recording lost its diagnostic role or recovered complete evidence");
        foreach (SaveStartupCallObservation observation in TakeAll(late)) NoAuthority(observation);

        var absent = new SaveStartupTrace(0, 0); absent.MarkHooksInstalled(1); absent.ObserveFirstUnityUpdate(2);
        Assert(!absent.PluginLoadObserved && absent.LateInstallation && absent.IntegrityLost && absent.Healthy,
            "missing earliest marker was inferred from later timestamps or zero calls");
        NoAuthority(absent);

        var partial = New();
        Assert(partial.BeginCall("SaveSystem.Init", 1, out long id) && partial.CompleteCall(id, 2, false), "partial-install scalar callback disappeared");
        partial.MarkHooksInstalled(3); partial.ObserveFirstUnityUpdate(4);
        Assert(partial.InstallationGaps == 1 && partial.IntegrityLost && partial.Healthy && partial.CanReadBoundThread(),
            "later successful install healed calls outside complete detour coverage");
        NoAuthority(partial);

        var clock = Ready(); TakeAll(clock);
        Assert(clock.BeginCall("Base.LoadData", 3, out id) && !clock.CompleteCall(id, 2, false) && clock.IntegrityLost && !clock.Healthy,
            "clock regression was silently reordered into a complete loading sequence");
        NoAuthority(clock);
    }

    private static SaveStartupTrace New() => new SaveStartupTrace(Environment.CurrentManagedThreadId, 0);
    private static SaveStartupTrace Ready()
    { var trace = New(); trace.MarkHooksInstalled(1); trace.ObserveFirstUnityUpdate(2); return trace; }
    private static List<SaveStartupCallObservation> TakeAll(SaveStartupTrace trace)
    {
        var rows = new List<SaveStartupCallObservation>();
        while (trace.TryTake(out SaveStartupCallObservation row)) rows.Add(row);
        return rows;
    }
    private static void OnWorker(Action action)
    {
        Exception error = null;
        var worker = new Thread(() => { try { action(); } catch (Exception caught) { error = caught; } });
        worker.Start(); worker.Join();
        if (error != null) throw new InvalidOperationException("Scalar trace worker fixture failed.", error);
    }
    private static void NoAuthority(SaveStartupTrace trace)
    {
        Assert(!trace.StartEarlyCoverageVerified && !trace.NativeOrderVerified && !trace.FirstLoadSafe && !trace.PathIsolationVerified &&
            !trace.GuestStateIsolated && !trace.NativePermission && !trace.WorldAuthority && !trace.CargoAuthority,
            "scalar trace/thread markers granted native loading, profile isolation or gameplay authority");
    }
    private static void NoAuthority(SaveStartupCallObservation row)
    {
        Assert(row.ObservationOnly && !row.StartEarlyCoverageVerified && !row.NativeOrderVerified && !row.FirstLoadSafe && !row.PathIsolationVerified &&
            !row.GuestStateIsolated && !row.NativePermission && !row.WorldAuthority && !row.CargoAuthority,
            "a copied callback granted loading, profile isolation or gameplay authority");
    }
    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
