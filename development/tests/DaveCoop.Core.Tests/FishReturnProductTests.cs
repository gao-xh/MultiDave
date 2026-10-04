using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DaveCoop.Core.Cargo;

// Actual selection/product/mapping coordinators and ledger, with synthetic
// lookup results only. No native formula, resource, storage or save is run.
internal static class FishReturnProductTests
{
    internal static void MixedCountModesKeepExactOrderedMappings()
    {
        var f = new Fixture();
        var backend = new MappingBackend(f)
        {
            MainIds = new[] { 101, 102 }, PlusId = 201,
            Products = new[] { new ProductValues(501, 2, 7), new ProductValues(502, 3, 7), new ProductValues(503, 4, 7) },
            Mappings = new[] { new MappingValues(501, 701, 3, 7, 901, 5), new MappingValues(502, 702, 4, 7, 902, 6), new MappingValues(503, 703, 5, 7, 903, 7) }
        };
        FishYieldSelection selection = f.Ready(backend, 2);
        Assert(selection.MapReturnOnce(new[] { FishReturnCountMode.DirectCount, FishReturnCountMode.ExchangeWholeOnce, FishReturnCountMode.DirectCount }),
            "the complete held batch did not map");
        FishReturnMappingSnapshot mapped = selection.ReturnMapping;
        Assert(mapped.Stage == FishReturnMappingStage.MappingReady && mapped.CurrentRequest == null && mapped.Results.Length == 3 && mapped.Samples.Length == 3,
            "a complete mapping omitted entries or kept a dispatch request active");
        Assert(string.Join("|", backend.MappingCalls) ==
            "hold:0|tid:0|data:0|rank:0|type:0|ingredients:0|ingredient:0|hold:1|tid:1|data:1|rank:1|type:1|ingredients:1|ingredient:1|exchange:1|hold:2|tid:2|data:2|rank:2|type:2|ingredients:2|ingredient:2",
            "mapping reordered or repeated original resource work, or exchanged a direct-count product");
        for (int i = 0; i < mapped.Results.Length; i++)
        {
            FishReturnProductMapping result = mapped.Results[i]; CargoProduct raw = selection.Normalization.Products[i];
            Assert(result.Request.ProductIndex == i && result.Request.DropOrdinal == i && result.Request.SelectedLookupId == backend.MainOrPlusId(i) &&
                result.Request.ProductTid == raw.ProductId && result.Request.RawGrade == raw.Grade && result.Request.RawCount == 1 &&
                result.Request.RawProductFingerprint == CargoValues.ProductFingerprint(raw) && result.ParentId == raw.ProductId &&
                result.IngredientId == 901 + i && result.Rank == 3 + i && result.StorageCount == (i == 1 ? 6 : 1),
                "return output substituted selected lookup ID for product/ingredient identity or lost the fixed raw input");
        }
        AssertUnconfirmed(f, selection);
    }

    internal static void NoDropSentinelsDoNotConsumeMappingModes()
    {
        var f = new Fixture();
        var backend = new MappingBackend(f)
        {
            MainIds = new[] { -1, 101 }, PlusId = 201,
            Products = new[] { new ProductValues(999, 2, 7), new ProductValues(501, 2, 7), new ProductValues(501, 2, 7) },
            Mappings = new[] { new MappingValues(501, 701, 3, 7, 901, 5), new MappingValues(501, 701, 3, 7, 901, 5) }
        };
        FishYieldSelection selection = f.Ready(backend, 2);
        Assert(selection.MapReturnOnce(new[] { FishReturnCountMode.ExchangeWholeOnce, FishReturnCountMode.DirectCount }),
            "compact positive-only modes rejected a frozen no-drop sentinel");
        FishReturnProductMapping[] results = selection.ReturnMapping.Results;
        Assert(results.Length == 2 && results[0].Request.ProductIndex == 0 && results[0].Request.DropOrdinal == 1 &&
            results[1].Request.ProductIndex == 1 && results[1].Request.DropOrdinal == 2 &&
            results[0].Request.SelectedLookupId == 101 && results[1].Request.SelectedLookupId == 201 &&
            results[0].StorageCount == 5 && results[1].StorageCount == 1 &&
            backend.MappingCalls.Count(call => call.StartsWith("exchange:", StringComparison.Ordinal)) == 1,
            "an absent drop consumed a mode, duplicate IDs merged products, or modes followed drop ordinal instead of product index");
        AssertUnconfirmed(f, selection);
    }

    internal static void ModePreflightRejectsWithoutConsumingMapping()
    {
        var f = new Fixture(); var backend = new MappingBackend(f); FishYieldSelection selection = f.Ready(backend);
        foreach (FishReturnCountMode[] invalid in new[]
        {
            null, Array.Empty<FishReturnCountMode>(), new[] { FishReturnCountMode.DirectCount, FishReturnCountMode.DirectCount },
            new[] { (FishReturnCountMode)0 }, new[] { (FishReturnCountMode)99 }
        })
        {
            Assert(!selection.MapReturnOnce(invalid) && selection.ReturnMapping.Stage == FishReturnMappingStage.NotAttempted &&
                selection.ReturnMapping.Samples.Length == 0 && selection.ReturnMapping.Results.Length == 0 &&
                backend.MappingCalls.Count == 0 && backend.MappingGuards == 0,
                "invalid mode input consumed a mapping attempt or native-shaped work");
        }
        Assert(selection.MapReturnOnce(new[] { FishReturnCountMode.DirectCount }) && backend.MappingCalls.Count == 7,
            "a corrected mode could not use the untouched batch");
        AssertNoRetry(f, backend, selection);
    }

    internal static void MissingBackendAndUnpreparedProductsDoNotStartMapping()
    {
        var f = new Fixture(); var backend = new MappingBackend(f); FishYieldSelection selection = f.Create(backend);
        Assert(!selection.MapReturnOnce(new[] { FishReturnCountMode.DirectCount }) && selection.ReturnMapping.Stage == FishReturnMappingStage.NotAttempted,
            "mapping started before selection entry");
        Assert(selection.SelectOnce(f.Facts(), f.Now) && !selection.MapReturnOnce(new[] { FishReturnCountMode.DirectCount }) &&
            selection.ReturnMapping.Stage == FishReturnMappingStage.NotAttempted && backend.MappingCalls.Count == 0,
            "raw selection without normalized products reached return lookups");
        Assert(selection.NormalizeOnce() && selection.MapReturnOnce(new[] { FishReturnCountMode.DirectCount }),
            "preflight rejection prevented later mapping of the same ready products");

        var missing = new Fixture(); var productOnly = new ProductBackend(missing); FishYieldSelection productSelection = missing.Ready(productOnly);
        Assert(!productSelection.MapReturnOnce(new[] { FishReturnCountMode.DirectCount }) &&
            productSelection.ReturnMapping.Stage == FishReturnMappingStage.NotAttempted && productSelection.ReturnMapping.Samples.Length == 0,
            "a product-only backend invented a return mapping capability");
        AssertUnconfirmed(missing, productSelection);
    }

    internal static void BusinessFailuresRetainPartialMappingWithoutRetry()
    {
        string[] calls = { "hold:0", "tid:0", "data:0", "rank:0", "type:0", "ingredients:0", "ingredient:0", "exchange:0" };
        for (int failure = 0; failure < calls.Length; failure++)
        {
            var f = new Fixture(); var backend = new MappingBackend(f) { FailCall = calls[failure] }; FishYieldSelection selection = f.Ready(backend);
            Assert(!selection.MapReturnOnce(new[] { FishReturnCountMode.ExchangeWholeOnce }), "a failed mapping business call produced a complete result");
            FishReturnMappingSnapshot mapped = selection.ReturnMapping;
            FishReturnProductSample sample = mapped.Samples.Single();
            Assert(mapped.Stage == FishReturnMappingStage.EnteredUnknown && mapped.Results.Length == 0 && mapped.CurrentRequest == null &&
                mapped.Failure == nameof(InvalidOperationException) && sample.ReturnItemHeld == (failure > 0) &&
                sample.ReturnItemTid.HasValue == (failure > 1) && sample.ItemDataId.HasValue == (failure > 2) &&
                sample.ItemRank.HasValue == (failure > 3) && sample.ReturnItemType.HasValue == (failure > 4) &&
                sample.IngredientsHeld == (failure > 5) && sample.IngredientTid.HasValue == (failure > 6) && !sample.StorageCount.HasValue,
                "mapping failure erased returned data or fabricated a result that never returned");
            Assert(backend.MappingCalls.SequenceEqual(calls.Take(failure + 1)), "mapping called business after an earlier exception");
            AssertNoRetry(f, backend, selection);
        }
    }

    internal static void SourceGuardsPreserveReturnedEvidenceAndStopLaterWork()
    {
        string[] calls = { "hold:0", "tid:0", "data:0", "rank:0", "type:0", "ingredients:0", "ingredient:0", "exchange:0" };
        for (int failure = 0; failure < calls.Length; failure++)
        {
            var f = new Fixture(); var backend = new MappingBackend(f) { LoseSourceAfterCall = calls[failure] }; FishYieldSelection selection = f.Ready(backend);
            Assert(!selection.MapReturnOnce(new[] { FishReturnCountMode.ExchangeWholeOnce }), "a failed post-call source guard completed mapping");
            FishReturnProductSample sample = selection.ReturnMapping.Samples.Single();
            Assert(sample.ReturnItemHeld && sample.ReturnItemTid.HasValue == (failure >= 1) && sample.ItemDataId.HasValue == (failure >= 2) &&
                sample.ItemRank.HasValue == (failure >= 3) && sample.ReturnItemType.HasValue == (failure >= 4) &&
                sample.IngredientsHeld == (failure >= 5) && sample.IngredientTid.HasValue == (failure >= 6) &&
                sample.StorageCount.HasValue == (failure >= 7) && backend.MappingCalls.SequenceEqual(calls.Take(failure + 1)),
                "postguard failure lost the original returned prefix or allowed a later lookup");
            AssertNoRetry(f, backend, selection);
        }

        var lost = new Fixture(); var before = new MappingBackend(lost) { FailFirstMappingGuard = true }; FishYieldSelection untouched = lost.Ready(before);
        Assert(!untouched.MapReturnOnce(new[] { FishReturnCountMode.DirectCount }) && before.MappingCalls.Count == 0,
            "a failed initial source guard invoked the resource holder");
        AssertNoRetry(lost, before, untouched);

        // A synchronous callback can change the actual ledger stage while a
        // lookup returns. Its returned scalar survives, but no later work may
        // borrow a lease that the ledger has already resolved.
        var closed = new Fixture(); var closingBackend = new MappingBackend(closed); FishYieldSelection closing = closed.Ready(closingBackend);
        closingBackend.OnMappingCall = call =>
        {
            if (call != "data:0") return;
            Assert(closing.TrySealCaptureProducts(closed.LateFacts(closing), closed.Now).Reason == CargoReason.InvalidStage,
                "a mapping callback entered another coordinator operation");
            Assert(closed.Ledger.LateSeal(closed.Lease, closing.Normalization.Products, closed.LateFacts(closing), closed.Now).Accepted,
                "fixture could not seal its already selected raw batch");
            Assert(closed.Ledger.ConfirmCapture(closed.Lease.CaptureId, CargoReceiptKind.EmployeeDivertedYield,
                closing.Normalization.Products, closed.ReceiptFacts(closing), closed.Now).Accepted,
                "fixture could not resolve the existing capture during the callback");
        };
        Assert(!closing.MapReturnOnce(new[] { FishReturnCountMode.ExchangeWholeOnce }) &&
            closing.ReturnMapping.Stage == FishReturnMappingStage.EnteredUnknown && closing.ReturnMapping.Samples.Single().ItemDataId == 701 &&
            closingBackend.MappingCalls.SequenceEqual(calls.Take(3)) && closed.Capture.Stage == CargoCaptureStage.Confirmed,
            "the ledger's resolved selection did not stop mapping or erased the lookup that already returned");
        int closedCalls = closingBackend.MappingCalls.Count, closedGuards = closingBackend.MappingGuards;
        Assert(!closing.MapReturnOnce(new[] { FishReturnCountMode.ExchangeWholeOnce }) && closingBackend.MappingCalls.Count == closedCalls &&
            closingBackend.MappingGuards == closedGuards && !closing.ReturnMapping.ReturnConversionVerified,
            "capture confirmation revived a failed mapping or proved return conversion");
    }

    internal static void InvalidMetadataAndExchangeCountsStayUnknown()
    {
        foreach (int bad in new[] { 0, 1, 2, 3, 4, 5, 6, 7 })
        {
            var f = new Fixture(); var backend = new MappingBackend(f);
            MappingValues values = backend.Mappings[0];
            if (bad == 0) values.Tid = 999;
            if (bad == 1) values.DataId = 0;
            if (bad == 2) values.Rank = -1;
            if (bad == 3) values.Type = 99;
            if (bad == 4) values.IngredientId = 0;
            if (bad == 5) values.ExchangeCount = 0;
            if (bad == 6) values.ExchangeCount = -1;
            if (bad == 7) values.ExchangeCount = int.MaxValue;
            FishYieldSelection selection = f.Ready(backend);
            Assert(!selection.MapReturnOnce(new[] { FishReturnCountMode.ExchangeWholeOnce }) &&
                selection.ReturnMapping.Stage == FishReturnMappingStage.EnteredUnknown && selection.ReturnMapping.Results.Length == 0,
                "unsupported mapping scalar invented an ingredient output");
            AssertNoRetry(f, backend, selection);
        }
    }

    internal static void CreatorThreadAndReentryCannotDuplicateMapping()
    {
        var f = new Fixture(); var backend = new MappingBackend(f); FishYieldSelection selection = f.Ready(backend);
        bool workerResult = true; Exception workerError = null;
        var worker = new Thread(() =>
        {
            try { workerResult = selection.MapReturnOnce(new[] { FishReturnCountMode.ExchangeWholeOnce }); }
            catch (Exception error) { workerError = error; }
        });
        worker.Start(); worker.Join();
        Assert(workerError == null && !workerResult && selection.ReturnMapping.Stage == FishReturnMappingStage.NotAttempted &&
            backend.MappingCalls.Count == 0 && backend.MappingGuards == 0,
            "another thread consumed an attempt or used a native-shaped return backend");
        int reentries = 0;
        backend.OnMappingCall = call =>
        {
            if (call != "exchange:0") return;
            reentries++;
            Assert(!selection.MapReturnOnce(new[] { FishReturnCountMode.ExchangeWholeOnce }) &&
                !selection.NormalizeOnce() && !selection.SelectOnce(f.Facts(), f.Now),
                "a synchronous conversion callback restarted mapping, getters or selection");
        };
        Assert(selection.MapReturnOnce(new[] { FishReturnCountMode.ExchangeWholeOnce }) && reentries == 1 &&
            backend.MappingCalls.Count(call => call == "exchange:0") == 1,
            "creator-thread mapping failed or reentry repeated original conversion");
        AssertNoRetry(f, backend, selection);
    }

    internal static void OwnedMappingSnapshotsAndModesKeepFirstScalars()
    {
        var f = new Fixture(); var backend = new MappingBackend(f); FishYieldSelection selection = f.Ready(backend);
        var modes = new[] { FishReturnCountMode.ExchangeWholeOnce };
        backend.OnMappingCall = call => { if (call == "hold:0") modes[0] = FishReturnCountMode.DirectCount; };
        Assert(selection.MapReturnOnce(modes), "fixed initial mode did not complete mapping");
        FishReturnMappingSnapshot owned = selection.ReturnMapping;
        FishReturnProductMapping original = owned.Results.Single(); FishReturnProductSample sample = owned.Samples.Single();
        Assert(original.Request.CountMode == FishReturnCountMode.ExchangeWholeOnce && original.StorageCount == 5 &&
            ReferenceEquals(original.Request, sample.Request), "caller mode mutation changed the in-flight request");
        owned.Results[0] = null; owned.Samples[0] = null;
        backend.Mappings[0].IngredientId = 999; backend.Mappings[0].Rank = 999; backend.Mappings[0].ExchangeCount = 999;
        CargoProduct[] externalProducts = selection.Normalization.Products; externalProducts[0].Grade = 999;
        FishReturnMappingSnapshot again = selection.ReturnMapping;
        Assert(again.Results[0].IngredientId == 901 && again.Results[0].Rank == 3 && again.Results[0].StorageCount == 5 &&
            again.Samples[0].IngredientTid == 901 && again.Samples[0].StorageCount == 5 &&
            again.Results[0].Request.RawGrade == 4 && again.RawProductsFingerprint == selection.Normalization.ProductsFingerprint,
            "an owned snapshot or later backend changes replaced the first mapping/raw product evidence");
        AssertNoRetry(f, backend, selection);
    }

    internal static void MappedPlansKeepRawCaptureAndGrantNoReturnReceipt()
    {
        var f = new Fixture(); var backend = new MappingBackend(f); FishYieldSelection selection = f.Ready(backend);
        string returnId = Guid.NewGuid().ToString("N"), policy = "cargo-return-policy-v1/" + new string('a', 64);
        Throws(() => selection.CreateMappedReturnPlan(0, returnId, policy, 6, 0));
        Assert(selection.MapReturnOnce(new[] { FishReturnCountMode.ExchangeWholeOnce }), "complete mapping failed");
        int calls = backend.MappingCalls.Count, guards = backend.MappingGuards;
        string rawFingerprint = selection.Normalization.ProductsFingerprint;
        CargoEmployeeReturnPlan first = selection.CreateMappedReturnPlan(0, returnId, policy, 6, 0);
        CargoEmployeeReturnPlan same = selection.CreateMappedReturnPlan(0, returnId, policy, 6, 0);
        CargoEmployeeReturnPlan changed = selection.CreateMappedReturnPlan(0, returnId, policy, 7, 0);
        Assert(first.Fingerprint == same.Fingerprint && first.Fingerprint != changed.Fingerprint && first.FinalGrade == 6 &&
            first.StorageCount == 5 && first.IngredientId == 901 && first.ParentId == 501 && first.Rank == 3 &&
            first.ExpeditionId == f.Expedition && first.MemberId == f.Employee && first.CaptureId == f.Lease.CaptureId &&
            first.RawProductFingerprint == CargoValues.ProductFingerprint(selection.Normalization.Products[0]) &&
            selection.Normalization.Products[0].Grade == 4 && selection.Normalization.ProductsFingerprint == rawFingerprint &&
            backend.MappingCalls.Count == calls && backend.MappingGuards == guards && !first.NativePermission,
            "pure plan creation changed capture quality, repeated conversion or invented bound storage permission");
        Throws(() => selection.CreateMappedReturnPlan(-1, returnId, policy, 6, 0));
        Throws(() => selection.CreateMappedReturnPlan(1, returnId, policy, 6, 0));
        Throws(() => selection.CreateMappedReturnPlan(0, "invalid", policy, 6, 0));
        Assert(f.Capture.Request == null && f.Ledger.Snapshot.ReturnId == null && f.Ledger.Snapshot.ReturnItems.Length == 0,
            "creating a candidate plan implicitly sealed capture, froze return or bound an employee output");
        AssertUnconfirmed(f, selection);
        Assert(selection.TrySealCaptureProducts(f.LateFacts(selection), f.Now).Accepted, "mapping obstructed sealing the same raw batch");
        Assert(f.Ledger.FreezeReturn(returnId).Accepted && f.Ledger.Snapshot.ReturnItems.Single().Plan == null &&
            f.Ledger.CompleteReturn().Reason == CargoReason.ReturnIncomplete,
            "mapping/plan creation bound output or supplied a capture/storage/save receipt");
    }

    internal static void CapacityRetryAndResolvedSourceKeepTheCachedReturnMapping()
    {
        var f = new Fixture(employeeCapacity: 0.5); var backend = new MappingBackend(f); FishYieldSelection selection = f.Ready(backend);
        Assert(selection.MapReturnOnce(new[] { FishReturnCountMode.ExchangeWholeOnce }), "fixture failed to freeze mapping before capacity arbitration");
        FishReturnProductMapping mapped = selection.ReturnMapping.Results.Single();
        string rawFingerprint = selection.Normalization.ProductsFingerprint;
        int calls = backend.MappingCalls.Count, guards = backend.SourceChecks, getters = backend.ProductCalls.Count, rolls = backend.SelectionCalls.Count;

        // A separate unentered reservation temporarily leaves only 0.1 of this
        // member's 0.5 capacity. Cancelling it with explicit no-entry evidence
        // makes room for the SAME already selected 0.25 capture.
        var occupying = new CargoCaptureRequest
        {
            ExpeditionId = f.Expedition, MemberId = f.Employee, RequestId = 2, OperationId = 2, BagRevision = f.Member(false).BagRevision,
            Source = new CargoSource { RoomId = f.Room, SceneEpoch = 1, EntityId = 8, LocalGeneration = 1 },
            Products = new[] { new CargoProduct { ProductId = 600, Grade = 1, Count = 1, UnitWeight = 0.4, TotalWeight = 0.4 } }
        };
        CargoCaptureFacts occupyingFacts = f.ReceiptFacts(selection);
        occupyingFacts.RequestId = occupying.RequestId; occupyingFacts.OperationId = occupying.OperationId;
        occupyingFacts.Source = CargoValues.Copy(occupying.Source); occupyingFacts.ProductsFingerprint = CargoValues.ProductsFingerprint(occupying.Products);
        CargoResult reservation = f.Ledger.Reserve(occupying, occupyingFacts, f.Now);
        Assert(reservation.Accepted, "fixture failed to occupy independent employee capacity");
        Assert(selection.TrySealCaptureProducts(f.LateFacts(selection), f.Now).Reason == CargoReason.CapacityExceeded &&
            f.Capture.SelectedYieldKnown && !f.Capture.YieldBound && f.Capture.SelectedYieldFingerprint == rawFingerprint &&
            selection.ReturnMapping.Stage == FishReturnMappingStage.MappingReady && selection.ReturnMapping.Results.Single().StorageCount == 5,
            "capacity rejection erased mapping, replaced the fixed selected batch or supplied a capture receipt");
        occupyingFacts.NativeNotEntered = true; occupyingFacts.BagRevision = f.Member(false).BagRevision;
        Assert(f.Ledger.CaptureNotEntered(reservation.CaptureId, occupyingFacts, f.Now).Accepted,
            "explicit unentered cancellation failed to release only the other reservation");
        Assert(selection.TrySealCaptureProducts(f.LateFacts(selection), f.Now).Accepted && f.Capture.YieldBound &&
            f.Member(false).ReservedWeight == 0.25 && f.Member(false).Weight == 0 &&
            f.Capture.SelectedYieldFingerprint == rawFingerprint,
            "fresh capacity facts did not seal the original raw capture without another conversion");
        Assert(f.Ledger.ConfirmCapture(f.Lease.CaptureId, CargoReceiptKind.EmployeeDivertedYield,
            selection.Normalization.Products, f.ReceiptFacts(selection), f.Now).Accepted &&
            f.Capture.Stage == CargoCaptureStage.Confirmed && f.Member(false).Weight == 0.25 && f.Member(true).Weight == 4,
            "synthetic terminal receipt changed bag ownership or failed to settle the exact capture");

        backend.SourceValid = false; backend.Mappings[0].ExchangeCount = 999;
        Assert(!selection.MapReturnOnce(new[] { FishReturnCountMode.ExchangeWholeOnce }) && !selection.NormalizeOnce() &&
            !selection.SelectOnce(f.Facts(), f.Now), "resolved source restarted selection, getters or conversion");
        string returnId = Guid.NewGuid().ToString("N");
        Assert(f.Ledger.FreezeReturn(returnId).Accepted, "confirmed source could not enter its normal return batch");
        CargoEmployeeReturnPlan plan = selection.CreateMappedReturnPlan(0, returnId, "cargo-return-policy-v1/" + new string('a', 64), 6, 0);
        Assert(plan.StorageCount == 5 && plan.IngredientId == mapped.IngredientId && plan.ParentId == mapped.ParentId && plan.Rank == mapped.Rank &&
            plan.RawProductFingerprint == mapped.Request.RawProductFingerprint && plan.FinalGrade == 6 &&
            selection.ReturnMapping.Results.Single().StorageCount == 5 && selection.Normalization.ProductsFingerprint == rawFingerprint &&
            f.Capture.Request.Products[0].Grade == 4 && backend.MappingCalls.Count == calls && backend.SourceChecks == guards &&
            backend.ProductCalls.Count == getters && backend.SelectionCalls.Count == rolls,
            "pure cached plan creation after source loss reread native-shaped resources or replaced frozen count/raw quality");
        Assert(!plan.NativePermission && !selection.ReturnMapping.FinalReturnGradeKnown && !selection.ReturnMapping.ReturnConversionVerified &&
            !selection.ReturnMapping.CaptureConfirmed && f.Ledger.Snapshot.ReturnItems.Single(item => item.CaptureId == f.Lease.CaptureId).Plan == null &&
            f.Ledger.CompleteReturn().Reason == CargoReason.ReturnIncomplete,
            "a resolved capture or pure candidate plan revived native return permission/storage/save receipt");
    }

    private static void AssertNoRetry(Fixture f, MappingBackend backend, FishYieldSelection selection)
    {
        int calls = backend.MappingCalls.Count, guards = backend.MappingGuards, selected = backend.SelectionCalls.Count, getters = backend.ProductCalls.Count;
        Assert(!selection.MapReturnOnce(new[] { FishReturnCountMode.ExchangeWholeOnce }) && !selection.MapReturnOnce(new[] { FishReturnCountMode.DirectCount }) &&
            backend.MappingCalls.Count == calls && backend.MappingGuards == guards && backend.SelectionCalls.Count == selected && backend.ProductCalls.Count == getters,
            "an attempted mapping repeated/replaced its conversion, selection or resource getters");
        Assert(selection.ReturnMapping.CurrentRequest == null, "a completed or failed mapping retained a current dispatch request");
        AssertUnconfirmed(f, selection);
    }

    private static void AssertUnconfirmed(Fixture f, FishYieldSelection selection)
    {
        Assert(!selection.ReturnMapping.FinalReturnGradeKnown && !selection.ReturnMapping.ReturnConversionVerified &&
            !selection.ReturnMapping.CaptureConfirmed && f.Capture.Stage == CargoCaptureStage.EnteredUnknown &&
            !f.Capture.YieldBound && f.Member(false).Weight == 0 && f.Member(false).ReservedWeight == 0 && f.Member(true).Weight == 4,
            "synthetic mapping established final quality, capture, bag delta or native return authority");
    }

    private sealed class ProductValues
    {
        public readonly int Tid, Grade, Type;
        public ProductValues(int tid, int grade, int type) { Tid = tid; Grade = grade; Type = type; }
    }
    private sealed class MappingValues
    {
        public int Tid, DataId, Rank, Type, IngredientId, ExchangeCount;
        public MappingValues(int tid, int dataId, int rank, int type, int ingredientId, int count)
        { Tid = tid; DataId = dataId; Rank = rank; Type = type; IngredientId = ingredientId; ExchangeCount = count; }
    }

    private class ProductBackend : IFishYieldSelectionBackend, IFishYieldProductBackend
    {
        protected readonly Fixture Fixture;
        public FishYieldSelection Coordinator;
        public int[] MainIds = { 101 };
        public int PlusId = -1;
        public ProductValues[] Products = { new ProductValues(501, 2, 7) };
        public readonly List<string> SelectionCalls = new List<string>(), ProductCalls = new List<string>();
        public ProductBackend(Fixture fixture) { Fixture = fixture; }
        public virtual void ValidateSource()
        { Assert(Fixture.Ledger.IsSelectionEntered(Fixture.Lease), "native-shaped backend work ran outside the actual entered source lease"); }
        public int SelectPickupBonusGrade() { SelectionCalls.Add("bonus"); return 2; }
        public int SelectMainItem(int fishDataTid, int tier)
        { Assert(fishDataTid == 25 && tier >= 1 && tier <= MainIds.Length, "selection changed the fixed recipe"); SelectionCalls.Add("main:" + tier); return MainIds[tier - 1]; }
        public int SelectPlusItem(int fishDataTid, int grade)
        { Assert(fishDataTid == 25 && grade == 3, "plus used another fish or grade"); SelectionCalls.Add("plus"); return PlusId; }
        public void HoldSelectedResource(FishSelectedDrop drop) { SelectionCalls.Add("hold:" + drop.Ordinal); }
        public int MainOrPlusId(int ordinal) => ordinal < MainIds.Length ? MainIds[ordinal] : PlusId;
        public int ReadProductTid(FishSelectedDrop drop) { ProductCalls.Add("tid:" + drop.Ordinal); return Products[drop.Ordinal].Tid; }
        public int ReadBaseGrade(FishSelectedDrop drop) { ProductCalls.Add("grade:" + drop.Ordinal); return Products[drop.Ordinal].Grade; }
        public float ReadBaseWeight(FishSelectedDrop drop) { ProductCalls.Add("weight:" + drop.Ordinal); return 0.25f; }
        public int ReadItemType(FishSelectedDrop drop) { ProductCalls.Add("type:" + drop.Ordinal); return Products[drop.Ordinal].Type; }
    }

    private sealed class MappingBackend : ProductBackend, IFishYieldReturnMappingBackend
    {
        public MappingValues[] Mappings = { new MappingValues(501, 701, 3, 7, 901, 5) };
        public readonly List<string> MappingCalls = new List<string>();
        public string FailCall, LoseSourceAfterCall;
        public bool FailFirstMappingGuard;
        public bool SourceValid = true;
        public int MappingGuards, SourceChecks;
        public Action<string> OnMappingCall;
        private bool _loseSource;
        public MappingBackend(Fixture fixture) : base(fixture) { }
        public override void ValidateSource()
        {
            SourceChecks++;
            if (!SourceValid) throw new InvalidOperationException("Controlled source disappearance.");
            base.ValidateSource();
            if (Coordinator.ReturnMapping.Stage != FishReturnMappingStage.Mapping) return;
            MappingGuards++;
            if (_loseSource || (FailFirstMappingGuard && MappingGuards == 1)) throw new InvalidOperationException("Controlled mapping source loss.");
        }
        public void HoldReturnItem(FishReturnProductRequest request) { Call("hold", request); }
        public int ReadReturnItemTid(FishReturnProductRequest request) { Call("tid", request); return Mappings[request.ProductIndex].Tid; }
        public int ReadItemDataId(FishReturnProductRequest request) { Call("data", request); return Mappings[request.ProductIndex].DataId; }
        public int ReadItemRank(FishReturnProductRequest request) { Call("rank", request); return Mappings[request.ProductIndex].Rank; }
        public int ReadReturnItemType(FishReturnProductRequest request) { Call("type", request); return Mappings[request.ProductIndex].Type; }
        public void HoldIngredients(FishReturnProductRequest request, int dataId)
        { Assert(dataId == Mappings[request.ProductIndex].DataId, "ingredient lookup used product TID instead of the held item data ID"); Call("ingredients", request); }
        public int ReadIngredientTid(FishReturnProductRequest request) { Call("ingredient", request); return Mappings[request.ProductIndex].IngredientId; }
        public int ExchangeWholeOnce(FishReturnProductRequest request)
        { Assert(request.RawCount == 1 && request.CountMode == FishReturnCountMode.ExchangeWholeOnce, "exchange used a guessed merged count or mode"); Call("exchange", request); return Mappings[request.ProductIndex].ExchangeCount; }
        private void Call(string method, FishReturnProductRequest request)
        {
            FishReturnMappingSnapshot mapping = Coordinator.ReturnMapping;
            CargoProduct raw = Coordinator.Normalization.Products[request.ProductIndex];
            Assert(mapping.Stage == FishReturnMappingStage.Mapping && ReferenceEquals(mapping.CurrentRequest, request) &&
                Fixture.Ledger.IsSelectionEntered(Fixture.Lease) && request.ProductTid == raw.ProductId &&
                request.RawGrade == raw.Grade && request.RawCount == raw.Count && request.RawProductFingerprint == CargoValues.ProductFingerprint(raw),
                "return backend ran without its exact active request or with changed normalized inputs");
            string call = method + ":" + request.ProductIndex; MappingCalls.Add(call); OnMappingCall?.Invoke(call);
            if (call == FailCall) throw new InvalidOperationException("Controlled mapping business failure.");
            if (call == LoseSourceAfterCall) _loseSource = true;
        }
    }

    private sealed class Fixture
    {
        public readonly string Expedition = Guid.NewGuid().ToString("N"), Host = Guid.NewGuid().ToString("N"),
            Employee = Guid.NewGuid().ToString("N"), Room = Guid.NewGuid().ToString("N");
        public readonly ExpeditionCargoLedger Ledger;
        public readonly CargoSourceLease Lease;
        public readonly double Now = 10;
        public Fixture(double employeeCapacity = 10)
        {
            Ledger = new ExpeditionCargoLedger(Expedition, new[]
            {
                new CargoMemberSetup { MemberId = Host, BagMode = CargoBagMode.HostNative, Capacity = 10, InitialWeight = 4 },
                new CargoMemberSetup { MemberId = Employee, BagMode = CargoBagMode.EmployeeVirtual, Capacity = employeeCapacity }
            });
            var intent = new CargoSourceIntent
            {
                ExpeditionId = Expedition, MemberId = Employee, RequestId = 1, BagRevision = 1,
                Source = new CargoSource { RoomId = Room, SceneEpoch = 1, EntityId = 7, LocalGeneration = 1 }, ActorRevision = 2, LoadoutRevision = 3
            };
            CargoResult reserved = Ledger.SourceReserve(intent, Fill(new CargoSourceFacts(), intent, null), Now, out CargoSourceLease lease);
            Assert(reserved.Accepted && lease != null, "fixture could not mint the actual ledger source lease"); Lease = lease;
        }
        public CargoCaptureSnapshot Capture => Ledger.Snapshot.Captures.Single(capture => capture.CaptureId == Lease.CaptureId);
        public CargoMemberSnapshot Member(bool host) => Ledger.Snapshot.Members.Single(member => member.MemberId == (host ? Host : Employee));
        public FishYieldSelection Create(ProductBackend backend, int tiers = 1)
        {
            var selection = new FishYieldSelection(Ledger, Lease, new FishYieldRecipe(25, FishYieldRecipeKind.OrdinaryPickup, tiers, -1, 0), backend);
            backend.Coordinator = selection; return selection;
        }
        public FishYieldSelection Ready(ProductBackend backend, int tiers = 1)
        {
            FishYieldSelection selection = Create(backend, tiers);
            Assert(selection.SelectOnce(Facts(), Now) && selection.NormalizeOnce(), "fixture could not select and normalize the complete held batch");
            return selection;
        }
        public CargoSourceFacts Facts() => Fill(new CargoSourceFacts(), Lease.Intent, Lease);
        public CargoLateYieldFacts LateFacts(FishYieldSelection selection)
        {
            CargoLateYieldFacts facts = Fill(new CargoLateYieldFacts(), Lease.Intent, Lease);
            facts.ProductsFingerprint = selection.Normalization.ProductsFingerprint; facts.CompleteSelectedYield = true;
            facts.MaterializationBoundaryHeld = true; facts.NoBagWriteYet = true; return facts;
        }
        public CargoCaptureFacts ReceiptFacts(FishYieldSelection selection)
        {
            CargoSourceIntent intent = Lease.Intent;
            return new CargoCaptureFacts
            {
                ExpeditionId = Expedition, MemberId = Employee, BoundPlayerId = 2, RequestId = intent.RequestId,
                OperationId = Lease.OperationId, ProductsFingerprint = selection.Normalization.ProductsFingerprint,
                CurrentRoomId = Room, Source = CargoValues.Copy(intent.Source), BagRevision = Member(false).BagRevision, SampledAt = Now,
                HostAuthority = true, SourceIdentityVerified = true, OrdinaryFishVerified = true, ActorPermitted = true,
                SourceAvailable = true, CapacityPolicyVerified = true, CapacityPolicy = CargoCapacityPolicy.RejectOverCapacity,
                CapacityRoutingVerified = true, NativeEntryCapabilityVerified = true, YieldVerified = true,
                CaptureTerminal = true, DiversionVerified = true, NoHostBagWrite = true
            };
        }
        private T Fill<T>(T facts, CargoSourceIntent intent, CargoSourceLease lease) where T : CargoSourceFacts
        {
            facts.ExpeditionId = Expedition; facts.MemberId = Employee; facts.BoundPlayerId = 2; facts.RequestId = intent.RequestId;
            facts.OperationId = lease?.OperationId ?? 0; facts.IntentFingerprint = lease?.IntentFingerprint;
            facts.CurrentRoomId = Room; facts.Source = CargoValues.Copy(intent.Source); facts.BagRevision = Member(false).BagRevision;
            facts.ActorRevision = intent.ActorRevision; facts.LoadoutRevision = intent.LoadoutRevision; facts.SampledAt = Now;
            facts.HostAuthority = true; facts.SourceIdentityVerified = true; facts.OrdinaryFishVerified = true;
            facts.ActorPermitted = true; facts.SourceAvailable = true; facts.CapacityPolicyVerified = true;
            facts.CapacityPolicy = CargoCapacityPolicy.RejectOverCapacity; facts.CapacityRoutingVerified = true;
            facts.NativeEntryCapabilityVerified = true; facts.YieldSelectionIsolationVerified = true; return facts;
        }
    }

    private static void Throws(Action action)
    { try { action(); } catch (ArgumentException) { return; } catch (InvalidOperationException) { return; } throw new InvalidOperationException("Expected invalid mapping plan input."); }
    private static void Assert(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
}
