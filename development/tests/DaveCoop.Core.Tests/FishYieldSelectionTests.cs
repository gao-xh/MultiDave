using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DaveCoop.Core.Cargo;

internal static class FishYieldSelectionTests
{
    // These fixtures run the production coordinator and ledger. Backend calls
    // and retained resources are CLR records, not native ABI or yield proofs.
    internal static void GradeMainAndPlusAreOrderedOnceAfterLedgerEntry()
    {
        var f = new Fixture();
        var backend = new RecordingBackend(f) { Grade = 2, MainIds = new[] { 101, 102, 103 }, PlusId = 201 };
        var selection = f.Selection(backend, tiers: 3, noneBonus: -1, lift: 2);
        Assert(selection.SelectOnce(f.Facts(), f.Now), "complete raw selection did not finish");
        Assert(string.Join("|", backend.Calls) == "grade|main:1|hold:0|main:2|hold:1|main:3|hold:2|plus:3|hold:3",
            "grade, main tiers, resource holds or one plus selection ran out of order");
        FishYieldSelectionSnapshot held = selection.Snapshot;
        Assert(held.Stage == FishYieldSelectionStage.RawPlanHeld && held.PickupBonusGrade == 2 && held.PlusSelectionCompleted &&
            held.Drops.Length == 4 && held.Drops[0].Tier == 1 && held.Drops[2].Tier == 3 && held.Drops[3].IsPlus &&
            held.Drops[0].BonusGrade == 2 && held.Drops[3].BonusGrade == -1 && held.Drops.All(drop => drop.ResourceHeld && drop.LiftType == 2 && drop.Count == 1),
            "raw results lost their original tier, grade, count, lift or resource retention arguments");
        int calls = backend.Calls.Count;
        Assert(!selection.SelectOnce(f.Facts(), f.Now) && backend.Calls.Count == calls, "completed batch was selected again");
        held.Drops[0] = held.Drops[3];
        Assert(selection.Snapshot.Drops[0].Tier == 1, "a caller modified the coordinator's ordered batch through its snapshot");
        AssertUnconfirmed(f, selection);
    }

    internal static void CompetingCoordinatorsOnOneLeaseCannotRunAnotherSelection()
    {
        var f = new Fixture();
        var firstBackend = new RecordingBackend(f);
        var secondBackend = new RecordingBackend(f);
        var first = f.Selection(firstBackend);
        var second = f.Selection(secondBackend);
        Assert(first.SelectOnce(f.Facts(), f.Now), "first lease owner could not select");
        Assert(!second.SelectOnce(f.Facts(), f.Now) && second.LastEntryReason == CargoReason.InvalidStage &&
            secondBackend.Calls.Count == 0 && secondBackend.Guards == 0, "another coordinator consumed grade/drop/pity or resource calls on the same lease");
        CargoSourceFacts notEntered = f.Facts(); notEntered.NativeNotEntered = true;
        Assert(f.Ledger.CancelSelectionNotEntered(f.Lease, notEntered, f.Now).Reason == CargoReason.InvalidStage,
            "selected source became cancellable as not entered");
        f.Ledger.SetConnected(f.Employee, false);
        Assert(f.Ledger.FreezeReturn(Guid.NewGuid().ToString("N")).Accepted &&
            f.Ledger.CompleteReturn().Reason == CargoReason.ReturnIncomplete && f.Capture.Stage == CargoCaptureStage.EnteredUnknown,
            "disconnect or empty return items discarded the selected unknown source");
        AssertUnconfirmed(f, first);
    }

    internal static void PartialBusinessFailuresKeepUnknownResultsAndNeverRetry()
    {
        foreach (string failure in new[] { "grade", "main:2", "plus:3", "hold:0" })
        {
            var f = new Fixture();
            var backend = new RecordingBackend(f) { Grade = 2, MainIds = new[] { 101, 102 }, PlusId = 201, FailCall = failure };
            var selection = f.Selection(backend, tiers: 2);
            Assert(!selection.SelectOnce(f.Facts(), f.Now), "controlled " + failure + " exception became success");
            FishYieldSelectionSnapshot partial = selection.Snapshot;
            Assert(partial.Stage == FishYieldSelectionStage.EnteredUnknown && partial.Failure == nameof(InvalidOperationException),
                "exception after irreversible selection entry lost its unknown state");
            if (failure == "grade") Assert(!partial.PickupBonusKnown && partial.Drops.Length == 0, "failed grade call invented a return value");
            if (failure == "main:2") Assert(partial.Drops.Length == 1 && partial.Drops[0].ResourceHeld && !partial.PlusSelectionCompleted,
                "later main failure dropped the earlier retained result or performed plus selection");
            if (failure == "plus:3") Assert(partial.Drops.Length == 2 && partial.Drops.All(drop => drop.ResourceHeld) && !partial.PlusSelectionCompleted,
                "plus exception invented a result or discarded the main batch");
            if (failure == "hold:0") Assert(partial.Drops.Length == 1 && partial.Drops[0].ItemId == 101 && !partial.Drops[0].ResourceHeld && backend.Held.Count == 1,
                "partial hold was falsely confirmed, or an already retained CLR resource was lost");
            int calls = backend.Calls.Count, guards = backend.Guards, held = backend.Held.Count;
            Assert(!selection.SelectOnce(f.Facts(), f.Now) && backend.Calls.Count == calls && backend.Guards == guards && backend.Held.Count == held,
                "an entered exception caused another selection, hold or cleanup attempt");
            AssertUnconfirmed(f, selection);
        }
    }

    internal static void SourceGuardFailuresStopLaterCallsWithoutErasingEarlierResults()
    {
        foreach (int failedGuard in new[] { 1, 4, 8 })
        {
            var f = new Fixture();
            var backend = new RecordingBackend(f) { FailGuard = failedGuard, MainIds = new[] { 101, 102 }, PlusId = 201 };
            var selection = f.Selection(backend, tiers: 2);
            Assert(!selection.SelectOnce(f.Facts(), f.Now) && selection.Snapshot.Stage == FishYieldSelectionStage.EnteredUnknown,
                "source guard loss became not-entered or raw success");
            if (failedGuard == 1) Assert(backend.Calls.Count == 0 && selection.Snapshot.Drops.Length == 0,
                "failed initial source guard allowed a grade/drop call");
            if (failedGuard == 4) Assert(string.Join("|", backend.Calls) == "grade|main:1|hold:0" && selection.Snapshot.Drops.Length == 1,
                "guard loss before tier two dispatched later selection or erased the held first tier");
            if (failedGuard == 8) Assert(selection.Snapshot.PlusSelectionCompleted && selection.Snapshot.Drops.Length == 3 && backend.Held.Count == 3,
                "final guard failure erased original plus/main results");
            int calls = backend.Calls.Count;
            Assert(!selection.SelectOnce(f.Facts(), f.Now) && backend.Calls.Count == calls, "failed source guard was retried after entry");
            AssertUnconfirmed(f, selection);
        }
    }

    internal static void MissingStaleAndForeignEntryFactsDispatchNothingUntilFresh()
    {
        var f = new Fixture();
        var backend = new RecordingBackend(f);
        var selection = f.Selection(backend);
        AssertDenied(selection, backend, f, null, CargoReason.MissingCapability);
        CargoSourceFacts facts = f.Facts(); facts.YieldSelectionIsolationVerified = false;
        AssertDenied(selection, backend, f, facts, CargoReason.MissingCapability);
        facts = f.Facts(); facts.NativeEntryCapabilityVerified = false;
        AssertDenied(selection, backend, f, facts, CargoReason.MissingCapability);
        facts = f.Facts(); facts.SampledAt = f.Now - 1;
        AssertDenied(selection, backend, f, facts, CargoReason.StaleFacts);
        facts = f.Facts(); facts.OperationId++;
        AssertDenied(selection, backend, f, facts, CargoReason.WrongIdentity);
        facts = f.Facts(); facts.BagRevision = f.Lease.Intent.BagRevision;
        AssertDenied(selection, backend, f, facts, CargoReason.Conflict);
        var foreign = new Fixture();
        var foreignSelection = new FishYieldSelection(f.Ledger, foreign.Lease, Recipe(), backend);
        AssertDenied(foreignSelection, backend, f, f.Facts(), CargoReason.WrongIdentity);
        Assert(selection.SelectOnce(f.Facts(), f.Now), "not-entered coordinator could not recheck genuinely fresh facts");
        AssertUnconfirmed(f, selection);
    }

    internal static void DeadBodyRecipeUsesOneTierAndDoesNotSelectPickupGrade()
    {
        var f = new Fixture();
        var backend = new RecordingBackend(f) { Grade = 99, MainIds = new[] { 101 }, PlusId = 201 };
        var recipe = new FishYieldRecipe(25, FishYieldRecipeKind.DeadFishBody, int.MaxValue, -1, 1);
        var selection = new FishYieldSelection(f.Ledger, f.Lease, recipe, backend);
        Assert(selection.SelectOnce(f.Facts(), f.Now), "single-tier dead-body recipe failed");
        Assert(string.Join("|", backend.Calls) == "main:1|hold:0|plus:0|hold:1" && selection.Snapshot.PickupBonusGrade == -1 &&
            selection.Snapshot.Drops.Length == 2 && selection.Snapshot.Drops.All(drop => drop.BonusGrade == -1),
            "dead-body path called the ordinary grade selector, used carvable tiers or guessed another plus grade");
        AssertUnconfirmed(f, selection);
    }

    internal static void TierBoundsPreserveAtLeastOneMainAndPreflightThePlusSlot()
    {
        foreach (int carvable in new[] { int.MinValue, -1, 0 })
        {
            var f = new Fixture();
            var backend = new RecordingBackend(f);
            var selection = f.Selection(backend, tiers: carvable);
            Assert(selection.SelectOnce(f.Facts(), f.Now) && selection.Snapshot.Drops.Length == 2 &&
                backend.Calls.Contains("main:1") && !backend.Calls.Contains("main:2"), "non-positive carvable count became a proved empty ordinary yield");
        }
        var maximum = new Fixture();
        var maxBackend = new RecordingBackend(maximum) { MainIds = new[] { 101, 102, 103, 104, 105, 106, 107 } };
        var maxSelection = maximum.Selection(maxBackend, tiers: 7);
        Assert(maxSelection.SelectOnce(maximum.Facts(), maximum.Now) && maxSelection.Snapshot.Drops.Length == 8 &&
            maxSelection.Snapshot.Drops[7].IsPlus, "seven main tiers did not leave the eighth slot for one possible plus item");
        var refused = new Fixture(); var unused = new RecordingBackend(refused);
        Throws<ArgumentException>(() => refused.Selection(unused, tiers: 8));
        Throws<ArgumentException>(() => new FishYieldRecipe(25, FishYieldRecipeKind.DeadFishBody, 1, int.MaxValue, 0));
        Assert(unused.Calls.Count == 0 && unused.Guards == 0 && refused.Capture.Stage == CargoCaptureStage.Reserved,
            "unsupported complete batch budget consumed selection or marked entry");
        var overflow = new Fixture(); var overflowBackend = new RecordingBackend(overflow) { Grade = int.MaxValue };
        var overflowSelection = overflow.Selection(overflowBackend);
        Assert(!overflowSelection.SelectOnce(overflow.Facts(), overflow.Now) && string.Join("|", overflowBackend.Calls) == "grade" &&
            overflowSelection.Snapshot.Stage == FishYieldSelectionStage.EnteredUnknown && overflowSelection.Snapshot.PickupBonusGrade == int.MaxValue,
            "overflow after the original grade return retried grade or dispatched main/plus with wrapped arithmetic");
    }

    internal static void ExplicitNoDropAndUnexpectedResultsNeverLookupOrReroll()
    {
        var f = new Fixture();
        var backend = new RecordingBackend(f) { MainIds = new[] { -1, 101, -1 }, PlusId = -1 };
        var selection = f.Selection(backend, tiers: 3);
        Assert(selection.SelectOnce(f.Facts(), f.Now), "explicit no-drop results were treated as an exception or reroll");
        FishYieldSelectionSnapshot result = selection.Snapshot;
        Assert(result.Drops.Length == 4 && backend.Held.Count == 1 && result.Drops[0].ItemId == -1 && result.Drops[3].ItemId == -1 &&
            !result.Drops[0].ResourceHeld && !result.Drops[0].HasProduct && result.PlusSelectionCompleted &&
            string.Join("|", backend.Calls) == "grade|main:1|main:2|hold:1|main:3|plus:3",
            "sentinel result caused a resource lookup, extra roll or loss of its tier position");
        foreach (int invalid in new[] { 0, -2 })
        {
            var bad = new Fixture(); var badBackend = new RecordingBackend(bad) { MainIds = new[] { invalid } };
            var badSelection = bad.Selection(badBackend);
            Assert(!badSelection.SelectOnce(bad.Facts(), bad.Now) && badSelection.Snapshot.Stage == FishYieldSelectionStage.EnteredUnknown &&
                badSelection.Snapshot.Drops.Length == 1 && badSelection.Snapshot.Drops[0].ItemId == invalid && badBackend.Held.Count == 0 &&
                string.Join("|", badBackend.Calls) == "grade|main:1", "unexpected main result was normalized, looked up or followed by another roll");
            int calls = badBackend.Calls.Count;
            Assert(!badSelection.SelectOnce(bad.Facts(), bad.Now) && badBackend.Calls.Count == calls, "unexpected result triggered another selection");
        }
        var plus = new Fixture(); var plusBackend = new RecordingBackend(plus) { PlusId = 0 };
        var plusSelection = plus.Selection(plusBackend);
        Assert(!plusSelection.SelectOnce(plus.Facts(), plus.Now) && plusSelection.Snapshot.PlusSelectionCompleted &&
            plusSelection.Snapshot.Drops.Length == 2 && plusSelection.Snapshot.Drops[1].ItemId == 0 && !plusSelection.Snapshot.Drops[1].ResourceHeld &&
            plusBackend.Held.Count == 1, "unexpected plus return was erased or represented as a retained resource");
        AssertUnconfirmed(f, selection);
    }

    internal static void ReentrantAndWrongThreadAttemptsCannotDispatchMoreBusiness()
    {
        var wrong = new Fixture(); var wrongBackend = new RecordingBackend(wrong); var wrongSelection = wrong.Selection(wrongBackend);
        CargoSourceFacts fresh = wrong.Facts(); bool workerResult = true; Exception workerError = null;
        var worker = new Thread(() =>
        {
            try { workerResult = wrongSelection.SelectOnce(fresh, wrong.Now); }
            catch (Exception error) { workerError = error; }
        });
        worker.Start(); worker.Join();
        Assert(workerError == null && !workerResult && wrongBackend.Calls.Count == 0 && wrongBackend.Guards == 0 &&
            wrong.Capture.Stage == CargoCaptureStage.Reserved, "wrong thread entered the ledger or invoked any backend business");
        Assert(wrongSelection.SelectOnce(wrong.Facts(), wrong.Now), "wrong-thread rejection prevented a legitimate first main-thread entry");
        var f = new Fixture(); var backend = new RecordingBackend(f); var selection = f.Selection(backend);
        var rivalBackend = new RecordingBackend(f); var rival = f.Selection(rivalBackend); int rejectedReentries = 0;
        backend.OnCall = call =>
        {
            if (call != "grade") return;
            int calls = backend.Calls.Count;
            Assert(!selection.SelectOnce(f.Facts(), f.Now) && !rival.SelectOnce(f.Facts(), f.Now) &&
                backend.Calls.Count == calls && rivalBackend.Calls.Count == 0 && rivalBackend.Guards == 0,
                "same or competing coordinator reentry dispatched another grade/drop/pity call");
            rejectedReentries++;
        };
        Assert(selection.SelectOnce(f.Facts(), f.Now) && rejectedReentries == 1 && backend.Calls.Count == 5,
            "reentrant attempt changed the one valid outer selection order");
        AssertUnconfirmed(f, selection);
    }

    internal static void CapacityRejectionKeepsTheRawBatchAndDuplicateResourcesStayDistinct()
    {
        var f = new Fixture(); var backend = new RecordingBackend(f) { MainIds = new[] { 101, 101 }, PlusId = 101 };
        var selection = f.Selection(backend, tiers: 2);
        Assert(selection.SelectOnce(f.Facts(), f.Now), "duplicate resources prevented the valid ordered raw batch");
        Assert(selection.Snapshot.Drops.Length == 3 && backend.Held.Count == 3 && selection.Snapshot.Drops.All(drop => drop.ItemId == 101) &&
            selection.Snapshot.Drops[0].Tier == 1 && selection.Snapshot.Drops[1].Tier == 2 && selection.Snapshot.Drops[2].IsPlus,
            "resource identity deduplication lost separate tier/plus quantities or parameters");
        // These normalized products are independently supplied synthetic ledger
        // facts; the coordinator has not normalized a native resource or grade.
        var products = new[]
        {
            new CargoProduct { ProductId = 501, Grade = 1, Count = 1, TotalWeight = 2 },
            new CargoProduct { ProductId = 502, Grade = 1, Count = 1, TotalWeight = 2 },
            new CargoProduct { ProductId = 503, Grade = 1, Count = 1, TotalWeight = 2 }
        };
        Assert(f.Ledger.LateSeal(f.Lease, products, f.LateFacts(products), f.Now).Reason == CargoReason.CapacityExceeded &&
            f.Capture.SelectedYieldKnown && !f.Capture.YieldBound && f.Member(false).ReservedWeight == 0,
            "capacity rejection materialized the synthetic batch or forgot its pinned selection");
        int calls = backend.Calls.Count, held = backend.Held.Count;
        Assert(!selection.SelectOnce(f.Facts(), f.Now) && backend.Calls.Count == calls && backend.Held.Count == held,
            "capacity failure reran a native-selector seam or dropped retained resources");
        CargoLateYieldFacts allow = f.LateFacts(products); allow.CapacityPolicy = CargoCapacityPolicy.AllowPersonalOverweight;
        Assert(f.Ledger.LateSeal(f.Lease, products, allow, f.Now).Accepted && f.Member(false).ReservedWeight == 6 &&
            f.Member(false).Weight == 0 && f.Member(true).Weight == 4 && f.Capture.Stage == CargoCaptureStage.EnteredUnknown,
            "same synthetic plan could not recheck personal policy, or charged the host bag/confirmed capture");
        AssertUnconfirmed(f, selection);
    }

    private static FishYieldRecipe Recipe(int tiers = 1, int noneBonus = -1, int lift = 0) =>
        new FishYieldRecipe(25, FishYieldRecipeKind.OrdinaryPickup, tiers, noneBonus, lift);

    private static void AssertDenied(FishYieldSelection selection, RecordingBackend backend, Fixture f, CargoSourceFacts facts, CargoReason reason)
    {
        Assert(!selection.SelectOnce(facts, f.Now) && selection.LastEntryReason == reason && backend.Calls.Count == 0 && backend.Guards == 0 &&
            selection.Snapshot.Stage == FishYieldSelectionStage.NotEntered && f.Capture.Stage == CargoCaptureStage.Reserved,
            "denied " + reason + " facts dispatched business or destroyed a not-entered lease");
    }

    private static void AssertUnconfirmed(Fixture f, FishYieldSelection selection)
    {
        Assert(!selection.Snapshot.FinalProductsVerified && !selection.Snapshot.CaptureConfirmed &&
            f.Capture.Stage == CargoCaptureStage.EnteredUnknown && !f.Capture.ReceiptKind.HasValue &&
            f.Member(false).Inventory.Length == 0 && f.Member(true).Inventory.Length == 0 &&
            !f.Lease.NativePermission && !f.Lease.SourceOperationBound && !f.Ledger.Snapshot.NativeExecutionImplemented,
            "raw selection or fixture backend granted final products, bag materialization, member proof or a native receipt");
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }

    private sealed class RecordingBackend : IFishYieldSelectionBackend
    {
        private readonly Fixture _fixture;
        public readonly List<string> Calls = new List<string>();
        public readonly List<FishSelectedDrop> Held = new List<FishSelectedDrop>();
        public int Guards { get; private set; }
        public int Grade { get; set; } = 2;
        public int[] MainIds { get; set; } = new[] { 101 };
        public int PlusId { get; set; } = 201;
        public string FailCall { get; set; }
        public int FailGuard { get; set; }
        public Action<string> OnCall { get; set; }
        public RecordingBackend(Fixture fixture) { _fixture = fixture; }
        public void ValidateSource()
        {
            Guards++;
            Assert(_fixture.Capture.Stage == CargoCaptureStage.EnteredUnknown, "source validation occurred before ledger entry");
            if (Guards == FailGuard) throw new InvalidOperationException("Controlled source guard loss.");
        }
        public int SelectPickupBonusGrade() { Call("grade"); return Grade; }
        public int SelectMainItem(int fishDataTid, int tier)
        { Assert(fishDataTid == 25 && tier >= 1 && tier <= MainIds.Length, "selector used an unsupported fish/tier"); Call("main:" + tier); return MainIds[tier - 1]; }
        public int SelectPlusItem(int fishDataTid, int selectionGrade)
        { Assert(fishDataTid == 25, "plus selector used another recipe fish"); Call("plus:" + selectionGrade); return PlusId; }
        public void HoldSelectedResource(FishSelectedDrop drop)
        {
            // Model a retain that has happened before an exception is observed.
            // ResourceHeld in the public snapshot must still remain unconfirmed.
            Held.Add(drop); Call("hold:" + drop.Ordinal);
        }
        private void Call(string call)
        {
            Assert(_fixture.Capture.Stage == CargoCaptureStage.EnteredUnknown, "business ran before the irreversible ledger mark");
            Calls.Add(call); OnCall?.Invoke(call);
            if (call == FailCall) throw new InvalidOperationException("Controlled business failure.");
        }
    }

    private sealed class Fixture
    {
        public readonly string Expedition = Guid.NewGuid().ToString("N");
        public readonly string Host = Guid.NewGuid().ToString("N");
        public readonly string Employee = Guid.NewGuid().ToString("N");
        public readonly string Room = Guid.NewGuid().ToString("N");
        public readonly ExpeditionCargoLedger Ledger;
        public readonly CargoSourceLease Lease;
        public readonly double Now = 10;
        public Fixture()
        {
            Ledger = new ExpeditionCargoLedger(Expedition, new[]
            {
                new CargoMemberSetup { MemberId = Host, BagMode = CargoBagMode.HostNative, Capacity = 10, InitialWeight = 4 },
                new CargoMemberSetup { MemberId = Employee, BagMode = CargoBagMode.EmployeeVirtual, Capacity = 5 }
            });
            var intent = new CargoSourceIntent
            {
                ExpeditionId = Expedition, MemberId = Employee, RequestId = 1, BagRevision = Member(false).BagRevision,
                Source = new CargoSource { RoomId = Room, SceneEpoch = 1, EntityId = 7, LocalGeneration = 1 },
                ActorRevision = 2, LoadoutRevision = 3, GateOperationId = 1
            };
            CargoResult reserve = Ledger.SourceReserve(intent, Fill(new CargoSourceFacts(), intent, null), Now, out CargoSourceLease lease);
            Assert(reserve.Accepted && lease != null, "fixture did not mint a production source lease"); Lease = lease;
        }
        public CargoCaptureSnapshot Capture => Ledger.Snapshot.Captures.Single(capture => capture.CaptureId == Lease.CaptureId);
        public CargoMemberSnapshot Member(bool host) => Ledger.Snapshot.Members.Single(member => member.MemberId == (host ? Host : Employee));
        public FishYieldSelection Selection(RecordingBackend backend, int tiers = 1, int noneBonus = -1, int lift = 0) =>
            new FishYieldSelection(Ledger, Lease, Recipe(tiers, noneBonus, lift), backend);
        public CargoSourceFacts Facts() => Fill(new CargoSourceFacts(), Lease.Intent, Lease);
        public CargoLateYieldFacts LateFacts(CargoProduct[] products)
        {
            CargoLateYieldFacts facts = Fill(new CargoLateYieldFacts(), Lease.Intent, Lease);
            facts.ProductsFingerprint = CargoValues.ProductsFingerprint(products); facts.CompleteSelectedYield = true;
            facts.MaterializationBoundaryHeld = true; facts.NoBagWriteYet = true; return facts;
        }
        private T Fill<T>(T facts, CargoSourceIntent intent, CargoSourceLease lease) where T : CargoSourceFacts
        {
            facts.ExpeditionId = Expedition; facts.MemberId = Employee; facts.BoundPlayerId = 2;
            facts.RequestId = intent.RequestId; facts.OperationId = lease?.OperationId ?? 0; facts.IntentFingerprint = lease?.IntentFingerprint;
            facts.CurrentRoomId = Room; facts.Source = CargoValues.Copy(intent.Source); facts.BagRevision = Member(false).BagRevision;
            facts.ActorRevision = intent.ActorRevision; facts.LoadoutRevision = intent.LoadoutRevision; facts.SampledAt = Now;
            facts.HostAuthority = true; facts.SourceIdentityVerified = true; facts.OrdinaryFishVerified = true;
            facts.ActorPermitted = true; facts.SourceAvailable = true; facts.CapacityPolicyVerified = true;
            facts.CapacityPolicy = CargoCapacityPolicy.RejectOverCapacity; facts.CapacityRoutingVerified = true;
            facts.NativeEntryCapabilityVerified = true; facts.YieldSelectionIsolationVerified = true; return facts;
        }
    }
}
