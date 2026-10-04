using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaveCoop.Core.Guest;

internal static class GuestShadowTransactionTests
{
    internal static void InstallationUsesFenceBeforeCloneAndNeverGrantsIsolation()
    {
        var backend = new Backend(); var transaction = new GuestShadowTransaction(backend);
        Accepted(transaction.Install());
        GuestShadowSnapshot active = transaction.Snapshot;
        Assert(active.Stage == GuestShadowStage.ShadowInstalled && active.RootShadowInstalled && active.FenceRetained && active.ReferencesRetained &&
            !active.GuestStateIsolated && !active.NativePermission && !active.WorldAuthority && !active.CargoAuthority,
            "installed roots or synthetic backend facts granted guest/gameplay authority");
        Assert(backend.Calls.IndexOf("fence") < backend.Calls.IndexOf("capture") && backend.Calls.IndexOf("capture") < backend.Calls.IndexOf("prepare") &&
            backend.Calls.IndexOf("prepare") < backend.Calls.IndexOf("install-GameData") &&
            backend.Calls.IndexOf("install-UserOption") < backend.Calls.IndexOf("install-IngredientsCache") &&
            backend.Calls.IndexOf("install-IngredientsCache") < backend.Calls.IndexOf("install-IngameCache") &&
            backend.Calls.IndexOf("install-IngameCache") < backend.Calls.IndexOf("validate") && active.Roots.Length == Roots().Length,
            "native clone/install entered before output fence, baseline, or detached preparation");
        int writes = backend.MutatingCalls;
        Assert(transaction.Install().Reason == GuestShadowReason.Duplicate && backend.MutatingCalls == writes,
            "duplicate lease installation called its backend again");
        Accepted(transaction.ValidateActive());
        active.Roots[0] = GuestShadowRootReadback.Foreign;
        Assert(transaction.Snapshot.Roots[0] == GuestShadowRootReadback.Detached, "snapshot root array aliases transaction state");
        Accepted(transaction.RestoreAndRelease());
        Assert(transaction.Snapshot.Stage == GuestShadowStage.Released && transaction.Snapshot.OriginalsRestored &&
            !transaction.Snapshot.FenceRetained && !transaction.Snapshot.ReferencesRetained && !transaction.Snapshot.RootShadowInstalled &&
            backend.Calls.IndexOf("remove") > backend.Calls.IndexOf("quiescent") && backend.Calls.IndexOf("release") > backend.Calls.IndexOf("remove"),
            "cleanup released fence/references before confirmed original roots and boundary");
        Assert(backend.Restored.SequenceEqual(Roots().Reverse()) && transaction.RestoreAndRelease().Reason == GuestShadowReason.Duplicate,
            "restore dependency order or completed lease idempotence changed");
    }

    internal static void InstallFailuresCompensateEveryPossiblyWrittenRoot()
    {
        string[] failures = new[] { "fence", "capture", "prepare" }
            .Concat(Roots().Select(root => "install-" + root)).Concat(new[] { "validate" }).ToArray();
        foreach (string failure in failures)
        foreach (bool throws in new[] { false, true })
        {
            var backend = new Backend { FailStage = failure, ThrowFailure = throws };
            var transaction = new GuestShadowTransaction(backend);
            Assert(!transaction.Install().Accepted && transaction.Snapshot.Fault != null && transaction.Snapshot.FenceRetained &&
                transaction.Snapshot.ReferencesRetained == (failure != "fence") &&
                !transaction.Snapshot.RootShadowInstalled && !backend.Calls.Contains("remove") && !backend.Calls.Contains("release"),
                "a failed/throwing install released its compensation fence: " + failure);
            Assert(backend.AllOriginal && backend.RestoreCounts.All(count => count <= 1),
                "possibly entered partial root writes were left installed or restored twice: " + failure);
            if (failure.StartsWith("install-", StringComparison.Ordinal))
            {
                GuestShadowRoot failed = (GuestShadowRoot)Enum.Parse(typeof(GuestShadowRoot), failure.Substring(8));
                Assert(backend.Restored.Count > 0 && backend.Restored[0] == failed,
                    "throw/false after a field write did not read back and compensate the failed root first");
            }
            int attempts = backend.MutatingCalls;
            Assert(transaction.Install().Reason == GuestShadowReason.Duplicate && backend.MutatingCalls == attempts,
                "failed prepare/install was retried under the same lease");
        }
        var changing = new Backend(); var changedTransaction = new GuestShadowTransaction(changing);
        changing.AfterStep = stage => { if (stage == "validate") changing.FenceHealthy = false; };
        Assert(!changedTransaction.Install().Accepted && changedTransaction.Snapshot.FenceIntegrityLost && changing.AllOriginal &&
            !changing.Calls.Contains("remove"), "validation callback lost the fence but installation still succeeded");
    }

    internal static void UnknownRestoreNeverRepeatsNativeWrites()
    {
        var backend = new Backend { FailStage = "install-PlayerInteraction" };
        backend.BlockRestore.Add(GuestShadowRoot.PlayerInteraction);
        var transaction = new GuestShadowTransaction(backend);
        Assert(!transaction.Install().Accepted && backend.RestoreCounts[2] == 1 && !backend.AllOriginal,
            "fixture did not preserve an entered, unresolved compensation");
        Assert(!transaction.RestoreAndRelease().Accepted && !transaction.RestoreAndRelease().Accepted && backend.RestoreCounts[2] == 1 &&
            transaction.Snapshot.FenceRetained && transaction.Snapshot.ReferencesRetained && !backend.Calls.Contains("remove"),
            "unknown restore was dispatched again or silently timed out");
        // A later exact readback can confirm the old operation. It does not
        // authorize a second restore/clone or infer success from elapsed time.
        backend.Current[2] = backend.Original[2];
        Accepted(transaction.RestoreAndRelease());
        Assert(backend.RestoreCounts[2] == 1 && backend.AllOriginal && transaction.Snapshot.Fault != null &&
            transaction.Snapshot.Stage == GuestShadowStage.Released, "late original evidence retried work or erased the failure history");

        var throwing = new Backend { FailStage = "restore-PhotoData", ThrowFailure = true };
        var afterWrite = new GuestShadowTransaction(throwing); Accepted(afterWrite.Install());
        Accepted(afterWrite.RestoreAndRelease());
        Assert(throwing.RestoreCounts[3] == 1 && afterWrite.Snapshot.Fault != null,
            "a restore exception after a verified original write caused repetition or discarded the fault");
    }

    internal static void ForeignRootsAndManagerReplacementRemainFenced()
    {
        var backend = new Backend(); var transaction = new GuestShadowTransaction(backend); Accepted(transaction.Install());
        object replacement = new object(); backend.Current[2] = replacement;
        Assert(!transaction.RestoreAndRelease().Accepted && ReferenceEquals(backend.Current[2], replacement) && backend.RestoreCounts[2] == 0 &&
            backend.RestoreCounts[0] == 1 && backend.RestoreCounts[4] == 1 && transaction.Snapshot.FenceRetained && transaction.Snapshot.ReferencesRetained,
            "foreign root was overwritten or prevented compensation of independent owned roots");
        backend.Current[2] = backend.Original[2]; Accepted(transaction.RestoreAndRelease());
        Assert(backend.RestoreCounts[2] == 0, "foreign root resolution dispatched a guessed restore");

        var manager = new Backend(); var managerTransaction = new GuestShadowTransaction(manager); Accepted(managerTransaction.Install());
        manager.BindingCurrent = false;
        Assert(!managerTransaction.RestoreAndRelease().Accepted && manager.Restored.Count == 0 && !manager.Calls.Contains("remove") &&
            managerTransaction.Snapshot.ReferencesRetained, "manager replacement was treated as the same native source");
        var unknown = new Backend(); var unknownTransaction = new GuestShadowTransaction(unknown); Accepted(unknownTransaction.Install());
        unknown.Unreadable.Add(GuestShadowRoot.PhotoData);
        Assert(!unknownTransaction.RestoreAndRelease().Accepted && unknown.RestoreCounts[3] == 0 && unknown.RestoreCounts[4] == 1 &&
            unknownTransaction.Snapshot.Roots[3] == GuestShadowRootReadback.Unknown && unknownTransaction.Snapshot.FenceRetained,
            "unknown readback authorized a blind field write or hid independent compensation");
        unknown.Unreadable.Clear(); Accepted(unknownTransaction.RestoreAndRelease());
        Assert(unknown.RestoreCounts[3] == 1 && unknown.RestoreCounts.All(count => count == 1),
            "new exact readback repeated a previously entered compensation or skipped the previously unreadable owned root");
    }

    internal static void QuiescenceAndFenceFailureKeepStrongReferences()
    {
        var backend = new Backend { Quiescent = false }; var transaction = new GuestShadowTransaction(backend); Accepted(transaction.Install());
        Assert(transaction.RestoreAndRelease().Reason == GuestShadowReason.QuiescenceUnavailable && backend.AllOriginal &&
            transaction.Snapshot.Stage == GuestShadowStage.RestoredFenced && transaction.Snapshot.OriginalsRestored &&
            transaction.Snapshot.FenceRetained && transaction.Snapshot.ReferencesRetained && !backend.Calls.Contains("remove"),
            "root restoration or known writer count was promoted to complete native quiescence");
        int writes = backend.MutatingCalls;
        Assert(transaction.RestoreAndRelease().Reason == GuestShadowReason.QuiescenceUnavailable && backend.MutatingCalls == writes,
            "waiting for a boundary repeated root compensation");
        backend.Quiescent = true; Accepted(transaction.RestoreAndRelease());

        var lost = new Backend(); var lostTransaction = new GuestShadowTransaction(lost); Accepted(lostTransaction.Install());
        lost.FenceHealthy = false;
        Assert(!lostTransaction.ValidateActive().Accepted && lost.AllOriginal && lostTransaction.Snapshot.FenceIntegrityLost &&
            !lostTransaction.Snapshot.RootShadowInstalled, "lost output fence kept active shadow evidence");
        lost.FenceHealthy = true;
        Assert(lostTransaction.RestoreAndRelease().Reason == GuestShadowReason.FenceUnavailable && !lost.Calls.Contains("remove") &&
            lostTransaction.Snapshot.ReferencesRetained, "later healthy flag erased a latched fence failure");

        var dirty = new Backend { OriginalManagersMatch = false }; var dirtyTransaction = new GuestShadowTransaction(dirty); Accepted(dirtyTransaction.Install());
        Assert(!dirtyTransaction.RestoreAndRelease().Accepted && dirty.AllOriginal && !dirtyTransaction.Snapshot.OriginalsRestored &&
            !dirty.Calls.Contains("quiescent") && !dirty.Calls.Contains("remove"), "root pointers alone bypassed manager/cache confirmation");
    }

    internal static void ThreadLeaseAndReentrantCallsCannotReuseSource()
    {
        var backend = new Backend(); var transaction = new GuestShadowTransaction(backend); Accepted(transaction.Install());
        int actions = backend.MutatingCalls, reads = backend.Reads;
        GuestShadowResult wrong = Task.Factory.StartNew(transaction.ValidateActive, CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default).GetAwaiter().GetResult();
        Assert(wrong.Reason == GuestShadowReason.WrongThread && backend.MutatingCalls == actions && backend.Reads == reads &&
            transaction.Snapshot.Fault != null && transaction.Snapshot.FenceRetained, "wrong-thread guard touched native backend state");
        Accepted(transaction.RestoreAndRelease());

        var reused = new Backend(); var first = new GuestShadowTransaction(reused); Accepted(first.Install());
        int before = reused.MutatingCalls;
        var second = new GuestShadowTransaction(reused);
        Assert(second.Install().Reason == GuestShadowReason.LeaseAlreadyUsed && reused.MutatingCalls == before + 1 &&
            !second.RestoreAndRelease().Accepted && first.Snapshot.RootShadowInstalled,
            "another transaction claimed or compensated the first transaction's lease");

        var source = new Backend(); var bound = new GuestShadowTransaction(source);
        source.OverrideHostBinding = Guid.NewGuid();
        Assert(bound.Install().Reason == GuestShadowReason.BindingChanged && !source.Calls.Contains("fence") && !source.Calls.Contains("prepare"),
            "changed source binding entered native preparation");
        var noBoundary = new Backend { EntryBoundary = false }; var rejected = new GuestShadowTransaction(noBoundary);
        Assert(rejected.Install().Reason == GuestShadowReason.BoundaryUnavailable && !noBoundary.Calls.Contains("fence") &&
            !noBoundary.Calls.Contains("capture") && !noBoundary.Calls.Contains("prepare") && !rejected.Snapshot.FenceRetained &&
            !rejected.Snapshot.ReferencesRetained && rejected.Install().Reason == GuestShadowReason.Duplicate,
            "unproved source boundary installed hooks, retained native roots, allowed cloning, or retried a lease");

        var recursive = new Backend(); var reentrant = new GuestShadowTransaction(recursive);
        recursive.AfterStep = stage => { if (stage == "prepare") Assert(reentrant.Install().Reason == GuestShadowReason.Reentrant, "recursive entry was accepted"); };
        Assert(!reentrant.Install().Accepted && !recursive.Calls.Any(call => call.StartsWith("install-", StringComparison.Ordinal)) &&
            reentrant.Snapshot.Fault != null && reentrant.Snapshot.FenceRetained, "reentrant preparation continued into native root writes");
    }

    internal static void CleanupFailuresNeverRedispatchOrReleaseEarly()
    {
        foreach (bool throws in new[] { false, true })
        {
            var backend = new Backend { FailStage = "remove", ThrowFailure = throws };
            var transaction = new GuestShadowTransaction(backend); Accepted(transaction.Install());
            Assert(transaction.RestoreAndRelease().Reason == GuestShadowReason.FenceRemovalFailed && backend.AllOriginal &&
                backend.Calls.Count(call => call == "remove") == 1 && !backend.Calls.Contains("release") && transaction.Snapshot.ReferencesRetained,
                "uncertain fence removal released references");
            Assert(!transaction.RestoreAndRelease().Accepted && backend.Calls.Count(call => call == "remove") == 1,
                "uncertain removal was dispatched again");
        }
        var references = new Backend { FailStage = "release", ThrowFailure = true };
        var releasing = new GuestShadowTransaction(references); Accepted(releasing.Install());
        Assert(releasing.RestoreAndRelease().Reason == GuestShadowReason.ReferenceReleaseFailed && references.AllOriginal &&
            !releasing.Snapshot.FenceRetained && releasing.Snapshot.ReferencesRetained && references.Calls.Count(call => call == "release") == 1,
            "failed reference release was hidden or occurred before safe unfencing");
        Assert(!releasing.RestoreAndRelease().Accepted && references.Calls.Count(call => call == "release") == 1,
            "partially released references were released a second time");
        var noFence = new Backend { FenceHealthy = false }; var preparing = new GuestShadowTransaction(noFence);
        Assert(preparing.Install().Reason == GuestShadowReason.FenceUnavailable && !noFence.Calls.Contains("capture") &&
            !noFence.Calls.Contains("prepare") && preparing.Snapshot.FenceIntegrityLost, "unhealthy fence permitted native original capture/clone");
        var enteredBoundary = new Backend(); var changing = new GuestShadowTransaction(enteredBoundary);
        enteredBoundary.AfterStep = stage => { if (stage == "fence") enteredBoundary.EntryBoundary = false; };
        Assert(changing.Install().Reason == GuestShadowReason.BoundaryUnavailable && enteredBoundary.Calls.Contains("fence") &&
            !enteredBoundary.Calls.Contains("capture") && !enteredBoundary.Calls.Contains("prepare") && changing.Snapshot.FenceRetained &&
            !changing.Snapshot.ReferencesRetained, "a successful precheck substituted for the fresh post-fence boundary");
    }

    internal static void OwnedMixedCacheIsCompensatedBeforeSaveRoots()
    {
        foreach (bool throws in new[] { false, true })
        {
            var backend = new Backend { MixedInstall = GuestShadowRoot.IngredientsCache,
                FailStage = "install-IngredientsCache", ThrowFailure = throws };
            var transaction = new GuestShadowTransaction(backend);
            Assert(!transaction.Install().Accepted && backend.AllOriginal && backend.RestoreCounts[5] == 1 &&
                backend.Restored.SequenceEqual(Roots().Take(6).Reverse()) && transaction.Snapshot.Fault != null &&
                transaction.Snapshot.OriginalsRestored && transaction.Snapshot.FenceRetained && transaction.Snapshot.ReferencesRetained &&
                !backend.Calls.Contains("remove"), "known partial cache write was skipped, repeated, or unfenced before original confirmation");
            Accepted(transaction.RestoreAndRelease());
            Assert(backend.RestoreCounts.Take(6).All(count => count == 1) && backend.RestoreCounts[6] == 0,
                "cleanup repeated a confirmed partial cache compensation or restored an uninstalled later root");
        }
        // Even a backend returning true cannot turn an incomplete pair into
        // installed state; production readback must still match both fields.
        var partial = new Backend { MixedInstall = GuestShadowRoot.IngredientsCache };
        var incomplete = new GuestShadowTransaction(partial);
        Assert(!incomplete.Install().Accepted && partial.AllOriginal && partial.RestoreCounts[5] == 1 &&
            !incomplete.Snapshot.RootShadowInstalled, "partial cache pair passed post-write confirmation");
    }

    internal static void CacheUnknownAndForeignResultsCannotOverwriteOrRetry()
    {
        var mixed = new Backend { MixedInstall = GuestShadowRoot.IngredientsCache, FailStage = "install-IngredientsCache" };
        mixed.BlockRestore.Add(GuestShadowRoot.IngredientsCache);
        var pending = new GuestShadowTransaction(mixed);
        Assert(!pending.Install().Accepted && mixed.RestoreCounts[5] == 1 && pending.Snapshot.Roots[5] == GuestShadowRootReadback.OwnedMixed,
            "partial cache fixture did not retain the entered compensation");
        Assert(!pending.RestoreAndRelease().Accepted && !pending.RestoreAndRelease().Accepted && mixed.RestoreCounts[5] == 1 &&
            pending.Snapshot.ReferencesRetained && pending.Snapshot.FenceRetained, "unresolved cache restoration was dispatched again");
        mixed.Current[5] = mixed.Original[5]; Accepted(pending.RestoreAndRelease());
        Assert(mixed.RestoreCounts[5] == 1 && pending.Snapshot.Fault != null, "later exact cache evidence repeated work or cleared fault history");

        // The pair starts fully installed. The reverse scalar write succeeds,
        // while the following pointer write enters but returns unknown. The
        // production transaction must retain the partial pair and never call
        // the backend again for that composite step.
        var partialRestore = new Backend(); var restoring = new GuestShadowTransaction(partialRestore); Accepted(restoring.Install());
        partialRestore.BlockRestore.Add(GuestShadowRoot.IngredientsCache);
        Assert(!restoring.RestoreAndRelease().Accepted && partialRestore.CurrentLoaded == partialRestore.OriginalLoaded &&
            ReferenceEquals(partialRestore.Current[5], partialRestore.Detached[5]) &&
            restoring.Snapshot.Roots[5] == GuestShadowRootReadback.OwnedMixed && !restoring.Snapshot.OriginalsRestored &&
            partialRestore.CacheRestoreFieldAttempts.SequenceEqual(new[] { 1, 1 }) &&
            partialRestore.RestoreCounts.Take(5).All(count => count == 1) && !partialRestore.Calls.Contains("remove"),
            "partial cache restoration passed whole-transaction confirmation or skipped independent root compensation");
        Assert(!restoring.RestoreAndRelease().Accepted && partialRestore.CacheRestoreFieldAttempts.SequenceEqual(new[] { 1, 1 }) &&
            partialRestore.RestoreCounts[5] == 1, "partial cache restoration repeated a subfield attempt");
        partialRestore.Current[5] = partialRestore.Original[5]; Accepted(restoring.RestoreAndRelease());

        foreach (bool unreadable in new[] { false, true })
        {
            var backend = new Backend(); var transaction = new GuestShadowTransaction(backend); Accepted(transaction.Install());
            object foreign = new object();
            if (unreadable) backend.Unreadable.Add(GuestShadowRoot.IngredientsCache);
            else backend.Current[5] = foreign;
            Assert(!transaction.ValidateActive().Accepted && backend.RestoreCounts[5] == 0 &&
                backend.RestoreCounts.Take(5).All(count => count == 1) && transaction.Snapshot.FenceRetained &&
                transaction.Snapshot.ReferencesRetained && !backend.Calls.Contains("remove"),
                "unknown or foreign cache permitted blind writes or prevented independent save-root compensation");
            if (!unreadable) Assert(ReferenceEquals(backend.Current[5], foreign), "foreign cache identity was overwritten");
            backend.Unreadable.Clear(); backend.Current[5] = backend.Original[5]; backend.CurrentLoaded = backend.OriginalLoaded;
            Accepted(transaction.RestoreAndRelease());
            Assert(backend.RestoreCounts[5] == 0, "exact original evidence guessed a cache compensation");
        }
    }

    internal static void MixedReadbackIsRejectedForSingleFieldRoots()
    {
        var backend = new Backend(); var transaction = new GuestShadowTransaction(backend); Accepted(transaction.Install());
        backend.Current[0] = backend.OwnedMixedState;
        Assert(!transaction.ValidateActive().Accepted && transaction.Snapshot.Roots[0] == GuestShadowRootReadback.Unknown &&
            backend.RestoreCounts[0] == 0 && backend.RestoreCounts.Skip(1).All(count => count == 1) &&
            transaction.Snapshot.FenceRetained && transaction.Snapshot.ReferencesRetained,
            "a composite readback authorized restoration of an unexplained single save root");
        backend.Current[0] = backend.Original[0]; Accepted(transaction.RestoreAndRelease());
        Assert(backend.RestoreCounts[0] == 0, "later single-root confirmation issued a guessed compensation");
    }

    internal static void CacheScalarAndNullPointerDoNotSupplyIdentity()
    {
        var same = new Backend { DetachedLoaded = false }; var sameTransaction = new GuestShadowTransaction(same);
        Accepted(sameTransaction.Install());
        Assert(sameTransaction.Snapshot.Roots[5] == GuestShadowRootReadback.Detached && same.CurrentLoaded == same.OriginalLoaded,
            "equal loaded flags prevented pointer-based composite identity confirmation");
        same.CurrentLoaded = true;
        Assert(!sameTransaction.ValidateActive().Accepted && sameTransaction.Snapshot.Roots[5] == GuestShadowRootReadback.Foreign &&
            same.RestoreCounts[5] == 0 && sameTransaction.Snapshot.ReferencesRetained,
            "an unexplained loaded flag was called an owned partial pair or silently reset");
        same.CurrentLoaded = same.OriginalLoaded; same.Current[5] = same.Original[5]; Accepted(sameTransaction.RestoreAndRelease());

        var replaced = new Backend(); var replacedTransaction = new GuestShadowTransaction(replaced); Accepted(replacedTransaction.Install());
        replaced.CacheInstanceCurrent = false;
        Assert(!replacedTransaction.ValidateActive().Accepted && replaced.RestoreCounts[5] == 0 &&
            replacedTransaction.Snapshot.Roots[5] == GuestShadowRootReadback.Foreign && replacedTransaction.Snapshot.FenceRetained,
            "a replacement cache singleton was treated as the captured source");
        replaced.CacheInstanceCurrent = true; replaced.Current[5] = replaced.Original[5]; replaced.CurrentLoaded = replaced.OriginalLoaded;
        Accepted(replacedTransaction.RestoreAndRelease());

        var absent = new Backend { FailStage = "prepare" }; absent.Original[5] = null; absent.Current[5] = null;
        var rejected = new GuestShadowTransaction(absent);
        Assert(!rejected.Install().Accepted && rejected.Snapshot.Roots[5] == GuestShadowRootReadback.Original &&
            rejected.Snapshot.OriginalsRestored && absent.RestoreCounts[5] == 0 && !absent.Calls.Contains("install-IngredientsCache"),
            "captured null cache was guessed to be detached, unknown, or writable after preparation rejection");
    }

    internal static void IngameCacheUnknownRestorationRetainsBothCacheOwners()
    {
        foreach (bool throws in new[] { false, true })
        {
            var backend = new Backend { FailStage = "install-IngameCache", ThrowFailure = throws };
            backend.BlockRestore.Add(GuestShadowRoot.IngameCache);
            var transaction = new GuestShadowTransaction(backend);
            Assert(!transaction.Install().Accepted && backend.RestoreCounts[6] == 1 && backend.Restored.First() == GuestShadowRoot.IngameCache &&
                backend.RestoreCounts.Take(6).All(count => count == 1) && !transaction.Snapshot.OriginalsRestored &&
                transaction.Snapshot.FenceRetained && transaction.Snapshot.ReferencesRetained && !backend.Calls.Contains("remove"),
                "entered seventh-cache write was skipped or earlier-cache restoration authorized cleanup");
            Assert(!transaction.RestoreAndRelease().Accepted && !transaction.RestoreAndRelease().Accepted && backend.RestoreCounts[6] == 1,
                "unknown seventh-cache restore was redispatched");
            backend.Current[6] = backend.Original[6]; Accepted(transaction.RestoreAndRelease());
            Assert(backend.RestoreCounts.All(count => count == 1) && transaction.Snapshot.Fault != null,
                "late exact seventh-cache evidence repeated work or erased the fault");
        }
    }

    internal static void IngameCacheForeignUnknownAndMixedDoNotAuthorizeWrites()
    {
        for (int mode = 0; mode < 4; mode++)
        {
            var backend = new Backend(); var transaction = new GuestShadowTransaction(backend); Accepted(transaction.Install());
            object foreign = new object();
            if (mode == 0) backend.Current[6] = foreign;
            if (mode == 1) backend.Unreadable.Add(GuestShadowRoot.IngameCache);
            if (mode == 2) backend.Current[6] = backend.OwnedMixedState;
            if (mode == 3) backend.IngameInstanceCurrent = false;
            Assert(!transaction.ValidateActive().Accepted && backend.RestoreCounts[6] == 0 &&
                backend.RestoreCounts.Take(6).All(count => count == 1) && !transaction.Snapshot.OriginalsRestored &&
                transaction.Snapshot.FenceRetained && transaction.Snapshot.ReferencesRetained && !backend.Calls.Contains("remove"),
                "unexplained seventh-cache identity overwrote state or prevented known independent compensation");
            if (mode == 0) Assert(ReferenceEquals(backend.Current[6], foreign), "foreign seventh-cache pointer was overwritten");
            if (mode == 2) Assert(transaction.Snapshot.Roots[6] == GuestShadowRootReadback.Unknown,
                "single-field seventh-cache accepted the sixth cache's composite readback");
            backend.Unreadable.Clear(); backend.IngameInstanceCurrent = true; backend.Current[6] = backend.Original[6];
            Accepted(transaction.RestoreAndRelease());
            Assert(backend.RestoreCounts[6] == 0, "exact original seventh-cache evidence caused a guessed write");
        }
        var absent = new Backend { FailStage = "prepare" }; absent.Original[6] = null; absent.Current[6] = null;
        var rejected = new GuestShadowTransaction(absent);
        Assert(!rejected.Install().Accepted && rejected.Snapshot.Roots[6] == GuestShadowRootReadback.Original &&
            rejected.Snapshot.OriginalsRestored && absent.RestoreCounts[6] == 0 && !absent.Calls.Contains("install-IngameCache"),
            "captured absent seventh table was treated as detached or replaced with guessed empty state");
    }

    private static GuestShadowRoot[] Roots() => new[] { GuestShadowRoot.GameData, GuestShadowRoot.PlayerData,
        GuestShadowRoot.PlayerInteraction, GuestShadowRoot.PhotoData, GuestShadowRoot.UserOption, GuestShadowRoot.IngredientsCache, GuestShadowRoot.IngameCache };
    private static void Accepted(GuestShadowResult result) => Assert(result.Accepted, result.Reason + ": " + result.Message);
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    // Pure CLR native-behavior fixture: reference identity and enter-then-fail
    // outcomes are real within this model; none supplies a gameplay capability.
    private sealed class Backend : IGuestShadowBackend
    {
        private readonly Guid _binding = Guid.NewGuid(), _lease = Guid.NewGuid();
        public Guid OverrideHostBinding;
        public Guid HostBindingId => OverrideHostBinding == Guid.Empty ? _binding : OverrideHostBinding;
        public Guid LeaseId => _lease;
        public int UnityThreadId { get; } = Environment.CurrentManagedThreadId;
        public readonly object[] Original = Enumerable.Range(0, Roots().Length).Select(_ => new object()).ToArray();
        public readonly object[] Detached = Enumerable.Range(0, Roots().Length).Select(_ => new object()).ToArray();
        public readonly object OwnedMixedState = new object();
        public GuestShadowRoot? MixedInstall;
        // Two independent fields model the composite step, including equal
        // scalar values. This is not native field/ABI or helper execution.
        public bool OriginalLoaded = false, DetachedLoaded = true, CurrentLoaded, CacheInstanceCurrent = true;
        public bool IngameInstanceCurrent = true;
        public readonly int[] CacheInstallFieldAttempts = new int[2], CacheRestoreFieldAttempts = new int[2];
        public readonly object[] Current;
        public readonly List<string> Calls = new List<string>();
        public readonly List<GuestShadowRoot> Restored = new List<GuestShadowRoot>();
        public readonly int[] RestoreCounts = new int[Roots().Length];
        public readonly HashSet<GuestShadowRoot> BlockRestore = new HashSet<GuestShadowRoot>();
        public readonly HashSet<GuestShadowRoot> Unreadable = new HashSet<GuestShadowRoot>();
        public bool BindingCurrent = true, EntryBoundary = true, FenceHealthy = true, Quiescent = true, OriginalManagersMatch = true;
        public string FailStage;
        public bool ThrowFailure;
        public Action<string> AfterStep;
        private bool _claimed, _fenced, _captured, _prepared;
        public int Reads;
        public int MutatingCalls => Calls.Count(call => call != "quiescent" && call != "validate");
        public bool AllOriginal => Enumerable.Range(0, 5).All(i => ReferenceEquals(Current[i], Original[i])) &&
            ReadCachePair() == GuestShadowRootReadback.Original && IngameInstanceCurrent && ReferenceEquals(Current[6], Original[6]);
        public Backend() { Current = (object[])Original.Clone(); }
        public bool TryClaimLease() { Calls.Add("claim"); if (_claimed) return false; _claimed = true; return true; }
        public bool BindingIsCurrent() { Reads++; return BindingCurrent; }
        public bool CanEnterBoundary() { Reads++; return EntryBoundary; }
        public bool InstallOutputFence() { _fenced = true; return Step("fence"); }
        public bool FenceIsHealthy() { Reads++; return FenceHealthy; }
        public bool FenceIsActive() { Reads++; return _fenced; }
        public bool CaptureOriginal()
        {
            Assert(_fenced && FenceHealthy, "capture entered without healthy fence"); _captured = true; return Step("capture");
        }
        public bool PrepareDetached()
        {
            Assert(_fenced && FenceHealthy && _captured, "clone entered before fenced original capture"); _prepared = true; return Step("prepare");
        }
        public bool InstallRoot(GuestShadowRoot root)
        {
            Assert(_fenced && FenceHealthy && _prepared, "write entered before detached preparation");
            if (root == GuestShadowRoot.IngredientsCache)
            {
                CacheInstallFieldAttempts[0]++; Current[5] = Detached[5];
                if (MixedInstall != root) { CacheInstallFieldAttempts[1]++; CurrentLoaded = DetachedLoaded; }
                return Step("install-" + root);
            }
            Current[(int)root - 1] = MixedInstall == root ? OwnedMixedState : Detached[(int)root - 1]; return Step("install-" + root);
        }
        public GuestShadowRootReadback ReadRoot(GuestShadowRoot root)
        {
            Reads++; if (!_captured || Unreadable.Contains(root)) return GuestShadowRootReadback.Unknown;
            if (root == GuestShadowRoot.IngredientsCache) return ReadCachePair();
            if (root == GuestShadowRoot.IngameCache && !IngameInstanceCurrent) return GuestShadowRootReadback.Foreign;
            int index = (int)root - 1;
            return ReferenceEquals(Current[index], Original[index]) ? GuestShadowRootReadback.Original :
                ReferenceEquals(Current[index], Detached[index]) ? GuestShadowRootReadback.Detached :
                ReferenceEquals(Current[index], OwnedMixedState) ? GuestShadowRootReadback.OwnedMixed : GuestShadowRootReadback.Foreign;
        }
        private GuestShadowRootReadback ReadCachePair()
        {
            if (!CacheInstanceCurrent) return GuestShadowRootReadback.Foreign;
            if (ReferenceEquals(Current[5], Original[5]))
                return CurrentLoaded == OriginalLoaded ? GuestShadowRootReadback.Original :
                    CurrentLoaded == DetachedLoaded ? GuestShadowRootReadback.OwnedMixed : GuestShadowRootReadback.Foreign;
            if (ReferenceEquals(Current[5], Detached[5]))
                return CurrentLoaded == DetachedLoaded ? GuestShadowRootReadback.Detached :
                    CurrentLoaded == OriginalLoaded ? GuestShadowRootReadback.OwnedMixed : GuestShadowRootReadback.Foreign;
            return GuestShadowRootReadback.Foreign;
        }
        public bool ValidateRoots() => Step("validate") && Enumerable.Range(0, 5).All(i => ReferenceEquals(Current[i], Detached[i])) &&
            ReadCachePair() == GuestShadowRootReadback.Detached && IngameInstanceCurrent && ReferenceEquals(Current[6], Detached[6]);
        public bool RestoreRoot(GuestShadowRoot root)
        {
            int index = (int)root - 1; RestoreCounts[index]++; Restored.Add(root);
            if (root == GuestShadowRoot.IngredientsCache)
            {
                if (CurrentLoaded != OriginalLoaded) { CacheRestoreFieldAttempts[1]++; CurrentLoaded = OriginalLoaded; }
                if (!ReferenceEquals(Current[5], Original[5]))
                {
                    CacheRestoreFieldAttempts[0]++;
                    if (BlockRestore.Contains(root)) { Calls.Add("restore-" + root); return false; }
                    Current[5] = Original[5];
                }
                return Step("restore-" + root);
            }
            if (BlockRestore.Contains(root)) { Calls.Add("restore-" + root); return false; }
            Current[index] = Original[index]; return Step("restore-" + root);
        }
        public bool ConfirmOriginalManagersAndRoots() { Reads++; return _captured && BindingCurrent && OriginalManagersMatch && AllOriginal; }
        public bool HasQuiescentBoundary() { Calls.Add("quiescent"); return Quiescent; }
        public bool RemoveOutputFence() { Assert(AllOriginal && Quiescent, "fence removed before restoration/quiescence"); _fenced = false; return Step("remove"); }
        public bool ReleaseReferences() { Assert(!_fenced && AllOriginal && Quiescent, "references released before safe fence removal"); return Step("release"); }
        private bool Step(string stage)
        {
            Calls.Add(stage); AfterStep?.Invoke(stage);
            if (FailStage != stage) return true;
            if (ThrowFailure) throw new InvalidOperationException("Synthetic entered failure");
            return false;
        }
    }
}
