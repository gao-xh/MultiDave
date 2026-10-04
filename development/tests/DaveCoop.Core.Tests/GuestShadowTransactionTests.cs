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
            backend.Calls.IndexOf("prepare") < backend.Calls.IndexOf("install-GameData") && backend.Calls.IndexOf("install-UserOption") < backend.Calls.IndexOf("validate"),
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
        string[] failures = { "fence", "capture", "prepare", "install-GameData", "install-PlayerData", "install-PlayerInteraction",
            "install-PhotoData", "install-UserOption", "validate" };
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

    private static GuestShadowRoot[] Roots() => new[] { GuestShadowRoot.GameData, GuestShadowRoot.PlayerData,
        GuestShadowRoot.PlayerInteraction, GuestShadowRoot.PhotoData, GuestShadowRoot.UserOption };
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
        public readonly object[] Original = Enumerable.Range(0, 5).Select(_ => new object()).ToArray();
        public readonly object[] Detached = Enumerable.Range(0, 5).Select(_ => new object()).ToArray();
        public readonly object[] Current;
        public readonly List<string> Calls = new List<string>();
        public readonly List<GuestShadowRoot> Restored = new List<GuestShadowRoot>();
        public readonly int[] RestoreCounts = new int[5];
        public readonly HashSet<GuestShadowRoot> BlockRestore = new HashSet<GuestShadowRoot>();
        public readonly HashSet<GuestShadowRoot> Unreadable = new HashSet<GuestShadowRoot>();
        public bool BindingCurrent = true, EntryBoundary = true, FenceHealthy = true, Quiescent = true, OriginalManagersMatch = true;
        public string FailStage;
        public bool ThrowFailure;
        public Action<string> AfterStep;
        private bool _claimed, _fenced, _captured, _prepared;
        public int Reads;
        public int MutatingCalls => Calls.Count(call => call != "quiescent" && call != "validate");
        public bool AllOriginal => Enumerable.Range(0, 5).All(i => ReferenceEquals(Current[i], Original[i]));
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
            Current[(int)root - 1] = Detached[(int)root - 1]; return Step("install-" + root);
        }
        public GuestShadowRootReadback ReadRoot(GuestShadowRoot root)
        {
            Reads++; if (!_captured || Unreadable.Contains(root)) return GuestShadowRootReadback.Unknown;
            int index = (int)root - 1;
            return ReferenceEquals(Current[index], Original[index]) ? GuestShadowRootReadback.Original :
                ReferenceEquals(Current[index], Detached[index]) ? GuestShadowRootReadback.Detached : GuestShadowRootReadback.Foreign;
        }
        public bool ValidateRoots() => Step("validate") && Enumerable.Range(0, 5).All(i => ReferenceEquals(Current[i], Detached[i]));
        public bool RestoreRoot(GuestShadowRoot root)
        {
            int index = (int)root - 1; RestoreCounts[index]++; Restored.Add(root);
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
