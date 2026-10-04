using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DaveCoop.Core.Cargo;

internal static class FishYieldProductTests
{
    // Production coordinator/ledger with CLR getter results. These fixtures
    // exercise sequencing and ownership, not native ABI, receipts or return.
    internal static void OriginalTidCaptureGradeAndFloatWeightsStayFixed()
    {
        var f = new Fixture(); var backend = new ProductBackend(f)
        {
            MainIds = new[] { 101, 102 }, PlusId = 201,
            Values = new[]
            {
                new ResourceValues(777, 4, 0.1f, 12),
                new ResourceValues(777, 5, 0.2f, 12),
                new ResourceValues(778, 3, 0.5f, 91)
            }
        };
        FishYieldSelection selection = f.Select(backend, tiers: 2);
        Assert(selection.NormalizeOnce(), "the complete held resources did not normalize");
        FishProductNormalizationSnapshot snapshot = selection.Normalization;
        Assert(string.Join("|", backend.ProductCalls) ==
            "tid:0|grade:0|weight:0|type:0|tid:1|grade:1|weight:1|type:1|tid:2|grade:2|weight:2|type:2",
            "resource getters did not run once in original ordinal order");
        Assert(snapshot.Products.Length == 3 && snapshot.Products[0].ProductId == 777 && snapshot.Products[1].ProductId == 777 &&
            snapshot.Products[2].ProductId == 778 && snapshot.Samples[0].SelectedLookupId == 101 && snapshot.Samples[1].SelectedLookupId == 102,
            "lookup identity became product TID, or equal TIDs merged different original quantities");
        Assert(snapshot.Products[0].Grade == 6 && snapshot.Products[1].Grade == 7 && snapshot.Products[2].Grade == 2 &&
            snapshot.Samples[2].BaseGrade == 3 && snapshot.Samples[2].ItemType == 91 && snapshot.Products.All(product => product.Count == 1),
            "capture grade lost its original base/bonus or was replaced by an inferred return grade");
        // Independent literals for the widened binary32 values, not another
        // implementation of the multiplication used by production code.
        Assert(snapshot.Products[0].TotalWeight == 0.10000000149011612 && snapshot.Products[0].UnitWeight == 0.10000000149011612 &&
            snapshot.Products[1].TotalWeight == 0.20000000298023224 && snapshot.Products[2].TotalWeight == 0.5,
            "single-product float weight was rounded to a decimal double or host sequential bag sum");
        AssertUnconfirmed(f, selection);
    }

    internal static void LiftPolicyAndSnapshotsPreserveRawValuesAndOwnedProducts()
    {
        foreach (int lift in new[] { 0, 1, 2, 3 })
        {
            var f = new Fixture(); var backend = new ProductBackend(f) { Values = new[] { new ResourceValues(501, 2, 0.25f, 5) } };
            FishYieldSelection selection = f.Select(backend, lift: lift);
            Assert(selection.NormalizeOnce(), "supported lift policy rejected the original resource");
            FishProductNormalizationSnapshot owned = selection.Normalization;
            double effective = lift == 0 || lift == 3 ? 0.25 : 0;
            Assert(owned.Samples[0].BaseWeight == 0.25f && owned.Products[0].UnitWeight == effective && owned.Products[0].TotalWeight == effective,
                "effective lift weight replaced the preserved original weight sample");
            string fingerprint = owned.ProductsFingerprint;
            owned.Products[0].ProductId = 999; owned.Products[0].Grade = 999; owned.Products[0].TotalWeight = 999;
            owned.Samples[0] = null; owned.Products[0] = null;
            backend.Values[0].Tid = 900; backend.Values[0].BaseGrade = 800; backend.Values[0].Weight = 700;
            FishProductNormalizationSnapshot again = selection.Normalization;
            Assert(again.Products[0].ProductId == 501 && again.Products[0].Grade == 4 && again.Products[0].TotalWeight == effective &&
                again.Samples[0].ProductTid == 501 && again.Samples[0].BaseWeight == 0.25f && again.ProductsFingerprint == fingerprint &&
                CargoValues.ProductsFingerprint(again.Products) == fingerprint,
                "mutable caller snapshots or later backend values changed the first owned normalized batch");
            AssertUnconfirmed(f, selection);
        }
    }

    internal static void GetterFailuresRetainEveryReturnedScalarWithoutRetry()
    {
        string[] getters = { "tid:0", "grade:0", "weight:0", "type:0" };
        for (int failure = 0; failure < getters.Length; failure++)
        {
            var f = new Fixture(); var backend = new ProductBackend(f) { FailProductCall = getters[failure] };
            FishYieldSelection selection = f.Select(backend);
            Assert(!selection.NormalizeOnce(), "a failed resource getter produced a complete batch");
            FishProductNormalizationSnapshot snapshot = selection.Normalization;
            FishProductSample sample = snapshot.Samples.Single();
            Assert(snapshot.Stage == FishProductNormalizationStage.EnteredUnknown && snapshot.Products.Length == 0 &&
                snapshot.ProductsFingerprint == null && snapshot.CurrentOrdinal == null && snapshot.Failure != null &&
                sample.ProductTid.HasValue == (failure > 0) && sample.BaseGrade.HasValue == (failure > 1) &&
                sample.BaseWeight.HasValue == (failure > 2) && !sample.ItemType.HasValue && !sample.CaptureGrade.HasValue,
                "failure erased an earlier getter result or fabricated one that never returned");
            Assert(backend.ProductCalls.SequenceEqual(getters.Take(failure + 1)) && backend.Held.Count == 1,
                "a getter failure called later getters or discarded the selected resource");
            AssertNoRetry(f, backend, selection);
        }
    }

    internal static void SourceLossStopsGettersAndPreservesTheReturnedPrefix()
    {
        foreach (int failedGuard in new[] { 1, 2, 5, 6 })
        {
            var f = new Fixture(); var backend = new ProductBackend(f) { FailNormalizationGuard = failedGuard };
            FishYieldSelection selection = f.Select(backend);
            Assert(!selection.NormalizeOnce(), "a lost source window still published capture products");
            FishProductNormalizationSnapshot snapshot = selection.Normalization;
            FishProductSample sample = snapshot.Samples.Single();
            int expectedCalls = failedGuard == 1 ? 0 : failedGuard == 2 ? 1 : 4;
            Assert(backend.ProductCalls.Count == expectedCalls && sample.ProductTid.HasValue == (failedGuard > 1) &&
                sample.ItemType.HasValue == (failedGuard >= 5) && sample.CaptureGrade.HasValue == (failedGuard == 6) &&
                snapshot.Stage == FishProductNormalizationStage.EnteredUnknown && snapshot.Products.Length == 0 && snapshot.ProductsFingerprint == null,
                "source invalidation lost a returned scalar, issued a later getter or exposed a partial complete plan");
            AssertNoRetry(f, backend, selection);
        }
    }

    internal static void InvalidScalarsAndGradeOverflowRemainUnknown()
    {
        var invalid = new[]
        {
            new ResourceValues(0, 2, 1, 1), new ResourceValues(-1, 2, 1, 1),
            new ResourceValues(501, int.MaxValue, 1, 1), new ResourceValues(501, -3, 1, 1),
            new ResourceValues(501, 999, 1, 1), new ResourceValues(501, 2, float.NaN, 1),
            new ResourceValues(501, 2, float.PositiveInfinity, 1), new ResourceValues(501, 2, -0.25f, 1),
            new ResourceValues(501, 2, 1000001f, 1)
        };
        foreach (ResourceValues value in invalid)
        {
            var f = new Fixture(); var backend = new ProductBackend(f) { Values = new[] { value } };
            FishYieldSelection selection = f.Select(backend);
            Assert(!selection.NormalizeOnce() && selection.Normalization.Stage == FishProductNormalizationStage.EnteredUnknown &&
                selection.Normalization.Products.Length == 0 && selection.Normalization.Samples.Single().ProductTid == value.Tid &&
                selection.Normalization.Samples.Single().ItemType == value.Type && backend.ProductCalls.Count == 4,
                "invalid original fields were clamped, filtered out or converted to a complete batch");
            AssertNoRetry(f, backend, selection);
        }
    }

    internal static void EmptyOriginalResultsCannotBecomeAnEmptyCaptureReceipt()
    {
        var f = new Fixture(); var backend = new ProductBackend(f) { MainIds = new[] { -1 }, PlusId = -1 };
        FishYieldSelection selection = f.Select(backend);
        Assert(selection.Snapshot.Drops.Length == 2 && !selection.NormalizeOnce() && backend.ProductCalls.Count == 0 && backend.Held.Count == 0 &&
            selection.Normalization.Stage == FishProductNormalizationStage.EnteredUnknown && selection.Normalization.Samples.Length == 0 &&
            selection.Normalization.Products.Length == 0 && selection.Normalization.ProductsFingerprint == null,
            "no-drop sentinels became zero-weight products or a proved empty capture");
        Assert(selection.TrySealCaptureProducts(new CargoLateYieldFacts(), f.Now).Reason == CargoReason.InvalidStage &&
            f.Ledger.FreezeReturn(Guid.NewGuid().ToString("N")).Accepted && f.Ledger.Snapshot.ReturnItems.Length == 0 &&
            f.Ledger.CompleteReturn().Reason == CargoReason.ReturnIncomplete,
            "an unproved empty selection released the source or completed a return");
        AssertNoRetry(f, backend, selection);
    }

    internal static void CreatorThreadAndReentryCannotDuplicateNormalization()
    {
        var f = new Fixture(); var backend = new ProductBackend(f); FishYieldSelection selection = f.Select(backend);
        Exception threadFailure = null;
        var worker = new Thread(() =>
        {
            try
            {
                Assert(!selection.NormalizeOnce() && selection.TrySealCaptureProducts(new CargoLateYieldFacts(), f.Now).Reason == CargoReason.InvalidStage,
                    "a foreign thread could normalize or seal the creator's batch");
            }
            catch (Exception error) { threadFailure = error; }
        });
        worker.Start(); worker.Join(); if (threadFailure != null) throw threadFailure;
        Assert(backend.ProductCalls.Count == 0 && selection.Normalization.Stage == FishProductNormalizationStage.NotAttempted,
            "a wrong-thread attempt consumed getters or the valid normalization window");
        int reentries = 0;
        backend.OnProductCall = call =>
        {
            if (call != "tid:0") return;
            int calls = backend.ProductCalls.Count;
            Assert(selection.Normalization.Stage == FishProductNormalizationStage.Normalizing && selection.Normalization.CurrentOrdinal == 0 &&
                !selection.NormalizeOnce() && !selection.SelectOnce(f.Facts(), f.Now) &&
                selection.TrySealCaptureProducts(new CargoLateYieldFacts(), f.Now).Reason == CargoReason.InvalidStage && backend.ProductCalls.Count == calls,
                "getter reentry started another normalization, selection or seal");
            reentries++;
        };
        Assert(selection.NormalizeOnce() && reentries == 1 && backend.ProductCalls.Count == 4,
            "the rejected inner call changed the valid outer getter sequence");
        AssertUnconfirmed(f, selection);
    }

    internal static void MissingBackendAndPreselectionCallsDoNotConsumeAnAttempt()
    {
        var f = new Fixture(); var backend = new SelectionBackend(f); FishYieldSelection selection = f.Create(backend);
        Assert(!selection.NormalizeOnce() && selection.Normalization.Stage == FishProductNormalizationStage.NotAttempted && backend.SelectionCalls.Count == 0,
            "normalization dispatched before source selection entered");
        Assert(selection.SelectOnce(f.Facts(), f.Now), "selection-only backend could not hold its raw plan");
        int calls = backend.SelectionCalls.Count, guards = backend.SourceGuards;
        Assert(!selection.NormalizeOnce() && !selection.NormalizeOnce() && selection.Normalization.Stage == FishProductNormalizationStage.NotAttempted &&
            backend.SelectionCalls.Count == calls && backend.SourceGuards == guards && selection.Normalization.Samples.Length == 0,
            "missing resource-getter backend falsely entered or consumed a one-shot attempt");
        AssertUnconfirmed(f, selection);

        var supported = new Fixture(); var productBackend = new ProductBackend(supported); FishYieldSelection ready = supported.Create(productBackend);
        Assert(!ready.NormalizeOnce() && ready.Normalization.Stage == FishProductNormalizationStage.NotAttempted &&
            ready.SelectOnce(supported.Facts(), supported.Now) && ready.NormalizeOnce() && productBackend.ProductCalls.Count == 4,
            "a harmless preselection check prevented a later legitimate normalization");
        AssertUnconfirmed(supported, ready);
    }

    internal static void CapacityRetryUsesOneCachedPlanAndNeverReadsResourcesAgain()
    {
        var f = new Fixture(); var backend = new ProductBackend(f)
        {
            MainIds = new[] { 101, 102 },
            Values = new[] { new ResourceValues(601, 2, 3, 1), new ResourceValues(601, 2, 3, 1) }
        };
        FishYieldSelection selection = f.Select(backend, tiers: 2);
        Assert(selection.NormalizeOnce(), "complete over-capacity batch did not normalize");
        string fingerprint = selection.Normalization.ProductsFingerprint;
        int calls = backend.ProductCalls.Count, guards = backend.SourceGuards, selections = backend.SelectionCalls.Count;
        Assert(selection.TrySealCaptureProducts(f.LateFacts(selection), f.Now).Reason == CargoReason.CapacityExceeded &&
            f.Capture.SelectedYieldKnown && f.Capture.SelectedYieldFingerprint == fingerprint && !f.Capture.YieldBound &&
            f.Member(false).ReservedWeight == 0 && f.Member(false).Weight == 0,
            "personal capacity failure forgot the fixed batch or charged inventory");
        FishProductNormalizationSnapshot caller = selection.Normalization;
        caller.Products[0].TotalWeight = 0; caller.Products[0].Grade = 0;
        backend.Values[0].Weight = 0; backend.Values[0].BaseGrade = 0;
        Assert(!selection.NormalizeOnce() && !selection.SelectOnce(f.Facts(), f.Now) &&
            selection.TrySealCaptureProducts(f.LateFacts(selection), f.Now).Reason == CargoReason.CapacityExceeded,
            "capacity failure allowed replacing or normalizing a lighter selected plan");
        Assert(f.Ledger.SetConnected(f.Employee, false).Accepted && f.Ledger.FreezeReturn(Guid.NewGuid().ToString("N")).Accepted,
            "fixture could not freeze the existing disconnected capture");
        CargoLateYieldFacts allowed = f.LateFacts(selection); allowed.ActorPermitted = false; allowed.SourceAvailable = false;
        allowed.CapacityPolicy = CargoCapacityPolicy.AllowPersonalOverweight;
        Assert(selection.TrySealCaptureProducts(allowed, f.Now).Accepted && f.Capture.YieldBound &&
            f.Member(false).ReservedWeight == 6 && f.Member(false).Weight == 0 && f.Member(true).Weight == 4 &&
            f.Ledger.Snapshot.ReturnItems.Length == 2 && f.Capture.SelectedYieldFingerprint == fingerprint,
            "fresh policy could not seal the same existing batch, or used the host capacity/weight");
        CargoLateYieldFacts duplicate = f.LateFacts(selection); duplicate.CapacityPolicy = CargoCapacityPolicy.AllowPersonalOverweight;
        Assert(selection.TrySealCaptureProducts(duplicate, f.Now).Reason == CargoReason.Duplicate && f.Member(false).ReservedWeight == 6 &&
            backend.ProductCalls.Count == calls && backend.SourceGuards == guards && backend.SelectionCalls.Count == selections &&
            selection.Normalization.ProductsFingerprint == fingerprint,
            "capacity/duplicate recheck repeated getters, rerolled, rewrote the plan or reserved it twice");
        AssertUnconfirmed(f, selection);
    }

    internal static void FreshLateFactsSealProductsWithoutGrantingAReceipt()
    {
        var f = new Fixture(); var backend = new ProductBackend(f); FishYieldSelection selection = f.Select(backend);
        Assert(selection.NormalizeOnce(), "valid raw capture products were not held");
        int calls = backend.ProductCalls.Count, guards = backend.SourceGuards;
        Assert(selection.TrySealCaptureProducts(null, f.Now).Reason == CargoReason.MissingCapability, "null facts granted a seal");
        CargoLateYieldFacts stale = f.LateFacts(selection); stale.SampledAt = f.Now - 1;
        Assert(selection.TrySealCaptureProducts(stale, f.Now).Reason == CargoReason.StaleFacts, "stale facts granted a seal");
        CargoLateYieldFacts forged = f.LateFacts(selection); forged.ProductsFingerprint = new string('0', 64);
        Assert(selection.TrySealCaptureProducts(forged, f.Now).Reason == CargoReason.WrongIdentity, "another product fingerprint was accepted");
        CargoLateYieldFacts incomplete = f.LateFacts(selection); incomplete.CompleteSelectedYield = false;
        Assert(selection.TrySealCaptureProducts(incomplete, f.Now).Reason == CargoReason.MissingCapability && !f.Capture.SelectedYieldKnown,
            "raw samples or incomplete caller facts established complete selected yield");
        Assert(selection.TrySealCaptureProducts(f.LateFacts(selection), f.Now).Accepted && f.Capture.YieldBound &&
            f.Member(false).ReservedWeight == 1 && f.Member(false).Weight == 0 && f.Member(true).Weight == 4 &&
            backend.ProductCalls.Count == calls && backend.SourceGuards == guards,
            "fresh late facts could not seal the exact cache without new resource work");
        AssertUnconfirmed(f, selection);
        Assert(f.Ledger.FreezeReturn(Guid.NewGuid().ToString("N")).Accepted && f.Ledger.CompleteReturn().Reason == CargoReason.ReturnIncomplete,
            "normalization or capacity acceptance masqueraded as capture/storage/save receipt");
    }

    private static void AssertNoRetry(Fixture f, ProductBackend backend, FishYieldSelection selection)
    {
        int calls = backend.ProductCalls.Count, guards = backend.SourceGuards, selections = backend.SelectionCalls.Count, held = backend.Held.Count;
        Assert(!selection.NormalizeOnce() && !selection.SelectOnce(f.Facts(), f.Now) &&
            selection.TrySealCaptureProducts(new CargoLateYieldFacts(), f.Now).Reason == CargoReason.InvalidStage &&
            backend.ProductCalls.Count == calls && backend.SourceGuards == guards && backend.SelectionCalls.Count == selections && backend.Held.Count == held,
            "unknown normalization replayed selection/getters/retention or fabricated a complete plan");
        AssertUnconfirmed(f, selection);
    }

    private static void AssertUnconfirmed(Fixture f, FishYieldSelection selection)
    {
        Assert(!selection.Normalization.CaptureConfirmed && !selection.Normalization.FinalReturnGradeKnown &&
            !selection.Snapshot.FinalProductsVerified && !selection.Snapshot.CaptureConfirmed &&
            f.Capture.Stage == CargoCaptureStage.EnteredUnknown && !f.Capture.ReceiptKind.HasValue &&
            f.Member(false).Inventory.Length == 0 && f.Member(true).Inventory.Length == 0 &&
            !f.Lease.NativePermission && !f.Lease.SourceOperationBound && !f.Ledger.Snapshot.NativeExecutionImplemented,
            "resource normalization granted capture, return grade, inventory or native permission");
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class ResourceValues
    {
        public int Tid, BaseGrade, Type;
        public float Weight;
        public ResourceValues(int tid, int grade, float weight, int type) { Tid = tid; BaseGrade = grade; Weight = weight; Type = type; }
    }

    private class SelectionBackend : IFishYieldSelectionBackend
    {
        protected readonly Fixture Fixture;
        public FishYieldSelection Coordinator;
        public readonly List<string> SelectionCalls = new List<string>();
        public readonly List<FishSelectedDrop> Held = new List<FishSelectedDrop>();
        public int[] MainIds = { 101 };
        public int PlusId = -1;
        public int SourceGuards { get; private set; }
        public SelectionBackend(Fixture fixture) { Fixture = fixture; }
        public virtual void ValidateSource()
        {
            SourceGuards++;
            Assert(Fixture.Capture.Stage == CargoCaptureStage.EnteredUnknown, "source/getter work occurred before the ledger entered");
        }
        public int SelectPickupBonusGrade() { SelectionCalls.Add("bonus"); return 2; }
        public int SelectMainItem(int fishDataTid, int tier)
        { Assert(fishDataTid == 25 && tier >= 1 && tier <= MainIds.Length, "main selection changed the fixed recipe"); SelectionCalls.Add("main:" + tier); return MainIds[tier - 1]; }
        public int SelectPlusItem(int fishDataTid, int selectionGrade)
        { Assert(fishDataTid == 25 && selectionGrade == 3, "plus selection changed the fixed grade"); SelectionCalls.Add("plus"); return PlusId; }
        public void HoldSelectedResource(FishSelectedDrop drop) { Held.Add(drop); SelectionCalls.Add("hold:" + drop.Ordinal); }
    }

    private sealed class ProductBackend : SelectionBackend, IFishYieldProductBackend
    {
        public ResourceValues[] Values = { new ResourceValues(501, 2, 1, 1) };
        public readonly List<string> ProductCalls = new List<string>();
        public string FailProductCall;
        public int FailNormalizationGuard;
        public Action<string> OnProductCall;
        private int _normalizationGuards;
        public ProductBackend(Fixture fixture) : base(fixture) { }
        public override void ValidateSource()
        {
            base.ValidateSource();
            if (Coordinator.Normalization.Stage == FishProductNormalizationStage.Normalizing && ++_normalizationGuards == FailNormalizationGuard)
                throw new InvalidOperationException("Controlled normalization source loss.");
        }
        public int ReadProductTid(FishSelectedDrop drop) { Read("tid", drop); return Values[drop.Ordinal].Tid; }
        public int ReadBaseGrade(FishSelectedDrop drop) { Read("grade", drop); return Values[drop.Ordinal].BaseGrade; }
        public float ReadBaseWeight(FishSelectedDrop drop) { Read("weight", drop); return Values[drop.Ordinal].Weight; }
        public int ReadItemType(FishSelectedDrop drop) { Read("type", drop); return Values[drop.Ordinal].Type; }
        private void Read(string getter, FishSelectedDrop drop)
        {
            Assert(Coordinator.Normalization.Stage == FishProductNormalizationStage.Normalizing &&
                Fixture.Capture.Stage == CargoCaptureStage.EnteredUnknown && drop.ResourceHeld && Held.Any(held => held.Ordinal == drop.Ordinal && held.ItemId == drop.ItemId),
                "resource getter ran before unknown/retention or for another selected resource");
            string call = getter + ":" + drop.Ordinal; ProductCalls.Add(call); OnProductCall?.Invoke(call);
            if (call == FailProductCall) throw new InvalidOperationException("Controlled original getter failure.");
        }
    }

    private sealed class Fixture
    {
        public readonly string Expedition = Guid.NewGuid().ToString("N"), Host = Guid.NewGuid().ToString("N"), Employee = Guid.NewGuid().ToString("N"), Room = Guid.NewGuid().ToString("N");
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
                Source = new CargoSource { RoomId = Room, SceneEpoch = 1, EntityId = 7, LocalGeneration = 1 }, ActorRevision = 2, LoadoutRevision = 3, GateOperationId = 1
            };
            CargoResult reserve = Ledger.SourceReserve(intent, Fill(new CargoSourceFacts(), intent, null), Now, out CargoSourceLease lease);
            Assert(reserve.Accepted && lease != null, "fixture failed to mint the existing ledger's source lease"); Lease = lease;
        }
        public CargoCaptureSnapshot Capture => Ledger.Snapshot.Captures.Single(capture => capture.CaptureId == Lease.CaptureId);
        public CargoMemberSnapshot Member(bool host) => Ledger.Snapshot.Members.Single(member => member.MemberId == (host ? Host : Employee));
        public FishYieldSelection Create(SelectionBackend backend, int tiers = 1, int lift = 0)
        {
            var selection = new FishYieldSelection(Ledger, Lease, new FishYieldRecipe(25, FishYieldRecipeKind.OrdinaryPickup, tiers, -1, lift), backend);
            backend.Coordinator = selection; return selection;
        }
        public FishYieldSelection Select(SelectionBackend backend, int tiers = 1, int lift = 0)
        {
            FishYieldSelection selection = Create(backend, tiers, lift);
            Assert(selection.SelectOnce(Facts(), Now), "fixture failed to hold the raw batch"); return selection;
        }
        public CargoSourceFacts Facts() => Fill(new CargoSourceFacts(), Lease.Intent, Lease);
        public CargoLateYieldFacts LateFacts(FishYieldSelection selection)
        {
            CargoLateYieldFacts facts = Fill(new CargoLateYieldFacts(), Lease.Intent, Lease);
            facts.ProductsFingerprint = selection.Normalization.ProductsFingerprint; facts.CompleteSelectedYield = true;
            facts.MaterializationBoundaryHeld = true; facts.NoBagWriteYet = true; return facts;
        }
        private T Fill<T>(T facts, CargoSourceIntent intent, CargoSourceLease lease) where T : CargoSourceFacts
        {
            facts.ExpeditionId = Expedition; facts.MemberId = Employee; facts.BoundPlayerId = 2; facts.RequestId = intent.RequestId;
            facts.OperationId = lease?.OperationId ?? 0; facts.IntentFingerprint = lease?.IntentFingerprint;
            facts.CurrentRoomId = Room; facts.Source = CargoValues.Copy(intent.Source); facts.BagRevision = Member(false).BagRevision;
            facts.ActorRevision = intent.ActorRevision; facts.LoadoutRevision = intent.LoadoutRevision; facts.SampledAt = Now;
            // Synthetic capability facts permit ledger branches only. They are
            // not evidence that native isolation/materialization was executed.
            facts.HostAuthority = true; facts.SourceIdentityVerified = true; facts.OrdinaryFishVerified = true;
            facts.ActorPermitted = true; facts.SourceAvailable = true; facts.CapacityPolicyVerified = true;
            facts.CapacityPolicy = CargoCapacityPolicy.RejectOverCapacity; facts.CapacityRoutingVerified = true;
            facts.NativeEntryCapabilityVerified = true; facts.YieldSelectionIsolationVerified = true; return facts;
        }
    }
}
