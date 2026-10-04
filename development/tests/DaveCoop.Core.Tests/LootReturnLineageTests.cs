using System;
using System.Collections.Generic;
using System.Linq;
using DaveCoop.Core.Cargo;

internal static class LootReturnLineageTests
{
    internal static void ReturnScopeMasksFishAndKeepsIndependentOriginalScalars()
    {
        var lineage = New();
        LootLineageToken fish = Begin(lineage, 1, 1, true, Fish());
        // ApplyFinalGrade operates on the bag. Its adapter deliberately opens
        // an unknown source boundary, even inside a known fish callback.
        LootLineageToken apply = Begin(lineage, 2, 20, true);
        var first = new LootReturnGradeFields(1, 0, 3);
        var repeated = new LootReturnGradeFields(1, 0, 3);
        LootReturnGradeContextCandidate context = LootReturnGradeContextCandidate.Freeze(2, first, repeated);
        Assert(context.CandidatesAvailable && context.BoundsOrdered &&
            !ReferenceEquals(context.FirstRead, first) && !ReferenceEquals(context.RepeatedRead, repeated),
            "return field samples were not owned candidate observations");

        LootLineageToken predicate = Begin(lineage, 3, 21);
        Finish(lineage, predicate, true);
        LootLineageToken setFinal = Begin(lineage, 4, 19);
        Finish(lineage, setFinal);
        LootLineageToken exchange = Begin(lineage, 5, 22);
        Assert(lineage.RecordPostfix(exchange, null, 7), "original exchange integer was not recorded");
        // The fixed scope persists until finalization, including callbacks
        // occurring after an original postfix. This is enclosure, not proof
        // that this storage call used the exchange callback's result.
        LootLineageToken storage = Begin(lineage, 6, 4);
        Finish(lineage, storage);
        Assert(lineage.FinalizeCall(exchange, false), "exchange finalizer lost its fixed token");
        Finish(lineage, apply);

        LootLineageToken restoredFishChild = Begin(lineage, 7, 2);
        Assert(restoredFishChild.ParentCallId == fish.CallId && restoredFishChild.SourceKnown &&
            restoredFishChild.SourceRootCallId == fish.CallId &&
            restoredFishChild.Source.EntityId == fish.Source.EntityId,
            "normal return-scope finalization did not restore the still-live outer fish scope");
        Finish(lineage, restoredFishChild);
        Finish(lineage, fish);
        LootLineageToken standalone = Begin(lineage, 8, 21);
        Assert(standalone.ParentCallId == 0 && !standalone.SourceKnown && !standalone.SourceShadowed,
            "a standalone return predicate reused the earlier bag or fish scope");
        Finish(lineage, standalone, false);

        List<LootLineageRecord> records = Drain(lineage);
        Assert(lineage.Healthy && lineage.PendingCount == 0 && records.Count == 24 &&
            records.Where(row => row.Stage == LootLineageStage.Prefix).Select(row => row.MethodCode)
                .SequenceEqual(new[] { 1, 20, 21, 19, 22, 4, 2, 21 }),
            "return callbacks changed the exact prefix sequence or failed to close");
        foreach (LootLineageRecord row in records.Where(row => row.CallId >= 2 && row.CallId <= 6))
            Assert(!row.SourceKnown && row.Source == null && row.SourceRootCallId == 0 && row.SourceShadowed,
                "all-bag return descendants borrowed a single fish source");
        Assert(apply.ParentCallId == fish.CallId && predicate.ParentCallId == apply.CallId &&
            setFinal.ParentCallId == apply.CallId && exchange.ParentCallId == apply.CallId &&
            storage.ParentCallId == exchange.CallId,
            "return scopes replaced their fixed observed parent with an invented direct caller");
        foreach (LootLineageStage stage in new[] { LootLineageStage.Postfix, LootLineageStage.Finalizer })
        {
            LootLineageRecord boolean = records.Single(row => row.CallId == predicate.CallId && row.Stage == stage);
            LootLineageRecord integer = records.Single(row => row.CallId == exchange.CallId && row.Stage == stage);
            Assert(boolean.OriginalReturn == true && boolean.OriginalIntReturn == null &&
                integer.OriginalReturn == null && integer.OriginalIntReturn == 7,
                "predicate and exchange original returns were mixed or substituted at finalization");
        }
        LootReturnGradeContextCandidate absent = LootReturnGradeContextCandidate.Unavailable(null,
            "No original return-grade call was observed.");
        Assert(!absent.CandidatesAvailable && absent.FirstRead == null && absent.RepeatedRead == null &&
            context.AdditiveGrade == 2 && context.FirstRead.MinItemGrade == 0,
            "an absent return sample reused or changed a held historical candidate");
        foreach (LootLineageRecord row in records) NoAuthority(row);
        NoPolicy(context); NoPolicy(absent);
    }

    internal static void ReturnExceptionsAndHistoricalReplayCannotRestoreOwnership()
    {
        var lineage = New();
        LootLineageToken fish = Begin(lineage, 1, 1, true, Fish());
        LootLineageToken apply = Begin(lineage, 2, 20, true);
        var fields = new LootReturnGradeFields(1, 0, 3);
        LootReturnGradeContextCandidate context = LootReturnGradeContextCandidate.Freeze(2, fields, fields);
        LootLineageToken predicate = Begin(lineage, 3, 21);
        Finish(lineage, predicate, false);
        LootLineageToken exchange = Begin(lineage, 4, 22);
        Assert(lineage.RecordPostfix(exchange, null, 0) && lineage.FinalizeCall(exchange, true),
            "original exchange sentinel and exception were not retained together");
        Assert(!lineage.Healthy && lineage.IntegrityLost && lineage.PendingCount == 0 &&
            lineage.DiscardedPending == 2 && lineage.OriginalExceptions == 1,
            "return exception left its bag/fish parent available for later inheritance");
        List<LootLineageRecord> history = Drain(lineage);
        LootLineageRecord finalizer = history.Last();
        Assert(finalizer.CallId == exchange.CallId && finalizer.Stage == LootLineageStage.Finalizer &&
            finalizer.OriginalException && finalizer.OriginalReturn == null && finalizer.OriginalIntReturn == 0 &&
            !finalizer.HealthyAtCapture && finalizer.Source == null && finalizer.SourceShadowed &&
            history.Any(row => row.HealthyAtCapture),
            "fault diagnosis erased the original sentinel or relabeled queued history as current evidence");
        long sequence = lineage.LastSequence;
        Assert(lineage.FinalizeCall(exchange, false) && lineage.FinalizeCall(exchange, true) &&
            lineage.LastSequence == sequence && lineage.OriginalExceptions == 1 &&
            !lineage.RecordPostfix(apply, null) && !lineage.FinalizeCall(fish, false) &&
            lineage.Begin(5, 4, false, null, ThreadId) == null,
            "duplicate completion or drain healed the failed return source boundary");

        var replacement = New();
        LootLineageToken newScope = Begin(replacement, 5, 20, true);
        Assert(newScope.RunId != apply.RunId && newScope.ParentCallId == 0 && newScope.Source == null &&
            newScope.SourceRootCallId == 0 && newScope.SourceShadowed &&
            !replacement.RecordPostfix(exchange, null, 0) && replacement.Unmatched == 1 &&
            !replacement.Healthy && replacement.DiscardedPending == 1,
            "a historical token completed or supplied ownership to a different observer Run");
        var replay = New();
        LootLineageToken standalone = Begin(replay, 8, 22);
        Finish(replay, standalone, null, 7);
        Assert(replay.Begin(8, 20, true, null, ThreadId) == null && replay.ReplayRejected == 1 &&
            !replay.Healthy && replay.HighestCallId == 8,
            "an already completed call ID reopened a return scope");
        foreach (LootLineageRecord row in history) NoAuthority(row);
        foreach (LootLineageRecord row in Drain(replacement)) NoAuthority(row);
        foreach (LootLineageRecord row in Drain(replay)) NoAuthority(row);
        NoPolicy(context);
    }

    private static int ThreadId => Environment.CurrentManagedThreadId;
    private static LootCallLineage New() => new LootCallLineage(ThreadId);
    private static LootLineageSource Fish() => new LootLineageSource(1, 2, 100, 3, 101);
    private static LootLineageToken Begin(LootCallLineage lineage, long id, int method,
        bool sourceBoundary = false, LootLineageSource source = null)
    {
        LootLineageToken token = lineage.Begin(id, method, sourceBoundary, source, ThreadId);
        Assert(token != null, "valid fixed-thread callback was rejected");
        return token;
    }
    private static void Finish(LootCallLineage lineage, LootLineageToken token, bool? boolean = null, int? integer = null)
        => Assert(lineage.RecordPostfix(token, boolean, integer) && lineage.FinalizeCall(token, false),
            "fixed original callback did not close normally");
    private static List<LootLineageRecord> Drain(LootCallLineage lineage)
    {
        var records = new List<LootLineageRecord>();
        while (lineage.TryTake(out LootLineageRecord row)) records.Add(row);
        return records;
    }
    private static void NoPolicy(LootReturnGradeContextCandidate context)
        => Assert(context.ObservationOnly && !context.NativeFieldAbiVerified && !context.FinalGradeVerified &&
            !context.ReceiverCollectionBound && !context.EmployeePolicyApplicable &&
            !context.ReturnConversionVerified && !context.Permission,
            "matched return field samples granted a quality, employee or storage policy");
    private static void NoAuthority(LootLineageRecord row)
        => Assert(row.ObservationOnly && !row.NativeSourceOperationBound && !row.SourceOperationBound &&
            !row.MemberOwnershipVerified && !row.NativeRewards && !row.FullYield && !row.YieldComplete &&
            !row.ActualBagDelta && !row.CaptureSuccess && !row.NativeGenerationVerified && !row.NativeHookAbiVerified,
            "an original return or observed parent granted native receipt or cargo authority");
    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
