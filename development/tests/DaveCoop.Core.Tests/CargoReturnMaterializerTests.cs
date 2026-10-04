using System;
using System.Linq;
using System.Threading;
using DaveCoop.Core.Cargo;

// Synthetic backend/facts exercise the actual ledger/coordinator, not native
// storage calls, runtime ABI, output conversion, or a durable save receipt.
internal static class CargoReturnMaterializerTests
{
    internal static void BoundPlanDispatchEntersLedgerAndArbitratesOnce()
    {
        var f = new Fixture();
        var backend = new StorageBackend(f);
        var first = new CargoReturnMaterializer(f.Ledger, f.Plan, backend);
        var second = new CargoReturnMaterializer(f.Ledger, f.Plan, backend);
        backend.Coordinator = first;

        Assert(first.DispatchOnce(f.StorageFacts(), f.Now), "the bound leased output did not dispatch");
        Assert(backend.TargetChecks == 2 && backend.AddCalls == 1 && ReferenceEquals(backend.AddedPlan, f.Plan),
            "dispatch did not guard and pass the exact immutable plan once");
        Assert(first.Snapshot.Stage == CargoMaterializerStage.EnteredUnknown && first.Snapshot.AddAttempted &&
            first.Snapshot.OriginalCallReturned && first.Snapshot.Failure == null,
            "a returned original call was not distinguished from a completed storage receipt");
        Assert(!first.DispatchOnce(f.StorageFacts(), f.Now) && !second.DispatchOnce(f.StorageFacts(), f.Now) &&
            second.LastEntryReason == CargoReason.InvalidStage && backend.TargetChecks == 2 && backend.AddCalls == 1,
            "another coordinator replayed the same entered product");
        AssertUnknown(f, first);

        // No returned void value generates delta/save facts. Only separate
        // synthetic observations may advance the existing return item.
        CargoStorageFacts observed = f.StorageFacts(); observed.StorageDeltaVerified = true;
        Accept(f.Ledger.ObserveEmployeeStorage(f.CaptureId, 0, observed, f.Now));
        Assert(f.Ledger.CompleteReturn().Reason == CargoReason.ReturnIncomplete,
            "storage observation alone completed a durable return");
        CargoStorageFacts saved = f.StorageFacts(); saved.SaveConfirmed = true;
        Accept(f.Ledger.ConfirmStorageSave(f.CaptureId, 0, saved, f.Now));
        Accept(f.Ledger.CompleteReturn());
        Assert(!first.Snapshot.StorageDeltaVerified && !first.Snapshot.SaveConfirmed &&
            f.Capture.Request.Products[0].Grade == 2 && f.RawFingerprint == CargoValues.ProductFingerprint(f.Capture.Request.Products[0]),
            "a final return plan changed the raw capture or granted coordinator receipt flags");
    }

    internal static void GuardAndAddFailuresRemainUnknownWithoutRetry()
    {
        // Failure 1 is before Add, 2 is inside Add, and 3 is the post-call guard.
        foreach (int failure in new[] { 1, 2, 3 })
        {
            var f = new Fixture();
            var backend = new StorageBackend(f) { FailurePoint = failure };
            var coordinator = new CargoReturnMaterializer(f.Ledger, f.Plan, backend);
            var competitor = new CargoReturnMaterializer(f.Ledger, f.Plan, backend);
            backend.Coordinator = coordinator;
            Assert(!coordinator.DispatchOnce(f.StorageFacts(), f.Now), "a controlled backend exception was reported as a completed call");
            CargoMaterializerSnapshot state = coordinator.Snapshot;
            Assert(state.Stage == CargoMaterializerStage.EnteredUnknown && state.Failure == nameof(InvalidOperationException) &&
                state.AddAttempted == (failure != 1) && state.OriginalCallReturned == (failure == 3),
                "failure timing lost the original Add attempt/return distinction");
            Assert(backend.AddCalls == (failure == 1 ? 0 : 1) && backend.TargetChecks == (failure == 3 ? 2 : 1),
                "a failed preguard/Add/postguard ran an unexpected later backend step");
            int calls = backend.AddCalls, checks = backend.TargetChecks;
            Assert(!coordinator.DispatchOnce(f.StorageFacts(), f.Now) && !competitor.DispatchOnce(f.StorageFacts(), f.Now) &&
                backend.AddCalls == calls && backend.TargetChecks == checks,
                "an unknown entered result was retried by this or another coordinator");
            AssertUnknown(f, coordinator);
        }
    }

    internal static void CreatorThreadAndReentrantCallsCannotDispatchAgain()
    {
        var f = new Fixture();
        var backend = new StorageBackend(f);
        var coordinator = new CargoReturnMaterializer(f.Ledger, f.Plan, backend);
        var competitor = new CargoReturnMaterializer(f.Ledger, f.Plan, backend);
        backend.Coordinator = coordinator;
        bool workerResult = true;
        Exception workerError = null;
        var worker = new Thread(() =>
        {
            try { workerResult = coordinator.DispatchOnce(f.StorageFacts(), f.Now); }
            catch (Exception error) { workerError = error; }
        });
        worker.Start(); worker.Join();
        Assert(workerError == null && !workerResult && backend.AddCalls == 0 && backend.TargetChecks == 0 &&
            f.Item.Stage == CargoReturnStage.Leased && coordinator.Snapshot.Stage == CargoMaterializerStage.NotEntered,
            "a foreign thread consumed the lease or entered the backend");

        int reentrantAttempts = 0;
        backend.OnAdd = () =>
        {
            reentrantAttempts++;
            Assert(coordinator.IsInvokingAdd, "the original Add callback lost its exact invoking window");
            Assert(!coordinator.DispatchOnce(f.StorageFacts(), f.Now) && !competitor.DispatchOnce(f.StorageFacts(), f.Now),
                "a synchronous Add callback dispatched this product again");
            Assert(coordinator.IsInvokingAdd && !competitor.IsInvokingAdd,
                "rejected reentry altered the outer original Add window");
        };
        Assert(coordinator.DispatchOnce(f.StorageFacts(), f.Now) && reentrantAttempts == 1 &&
            backend.AddCalls == 1 && backend.TargetChecks == 2,
            "creator-thread dispatch failed or reentry repeated backend work");
        AssertUnknown(f, coordinator);
    }

    internal static void FreshFactsAndExactPlanIdentityRejectBeforeBackend()
    {
        var f = new Fixture(lease: false);
        var backend = new StorageBackend(f);
        var coordinator = new CargoReturnMaterializer(f.Ledger, f.Plan, backend);
        backend.Coordinator = coordinator;
        Assert(!coordinator.DispatchOnce(f.StorageFacts(), f.Now) && coordinator.LastEntryReason == CargoReason.InvalidStage,
            "a bound but unleased product reached the backend");
        Accept(f.Ledger.LeaseMaterialization(f.CaptureId, 0, f.StorageFacts(), f.Now));

        var foreign = new CargoEmployeeReturnPlan(f.Plan.ExpeditionId, f.Plan.ReturnId, f.Plan.MemberId, f.Plan.CaptureId,
            f.Plan.ProductIndex, f.Plan.RawProductFingerprint, f.Plan.PolicyFingerprint, f.Plan.IngredientId,
            f.Plan.ParentId, f.Plan.Rank, f.Plan.FinalGrade, f.Plan.StorageCount, f.Plan.Place);
        Assert(foreign.Fingerprint == f.Plan.Fingerprint && !ReferenceEquals(foreign, f.Plan), "foreign-plan fixture changed content");
        var foreignCoordinator = new CargoReturnMaterializer(f.Ledger, foreign, backend);
        Assert(!foreignCoordinator.DispatchOnce(f.StorageFacts(), f.Now) && foreignCoordinator.LastEntryReason == CargoReason.InvalidStage,
            "an equal-fingerprint object replaced the exact ledger-owned plan");

        Assert(!coordinator.DispatchOnce(null, f.Now) && coordinator.LastEntryReason == CargoReason.MissingCapability,
            "missing fresh facts reached storage");
        CargoStorageFacts stale = f.StorageFacts(); stale.SampledAt = f.Now - 1;
        Assert(!coordinator.DispatchOnce(stale, f.Now) && coordinator.LastEntryReason == CargoReason.StaleFacts,
            "stale facts entered storage");
        CargoStorageFacts wrongRaw = f.StorageFacts(); wrongRaw.ProductFingerprint = "cargo-product-v1/" + new string('0', 64);
        Assert(!coordinator.DispatchOnce(wrongRaw, f.Now) && coordinator.LastEntryReason == CargoReason.WrongIdentity,
            "another raw product reached storage");
        CargoStorageFacts wrongPlan = f.StorageFacts(); wrongPlan.ReturnPlanFingerprint = "cargo-employee-return-v1/" + new string('0', 64);
        Assert(!coordinator.DispatchOnce(wrongPlan, f.Now) && coordinator.LastEntryReason == CargoReason.WrongIdentity,
            "another converted output reached storage");
        CargoStorageFacts missingEntry = f.StorageFacts(); missingEntry.NativeEntryCapabilityVerified = false;
        Assert(!coordinator.DispatchOnce(missingEntry, f.Now) && coordinator.LastEntryReason == CargoReason.MissingCapability,
            "missing native-entry evidence entered the backend");
        Assert(backend.TargetChecks == 0 && backend.AddCalls == 0 && f.Item.Stage == CargoReturnStage.Leased &&
            coordinator.Snapshot.Stage == CargoMaterializerStage.NotEntered && !coordinator.Snapshot.AddAttempted &&
            !coordinator.IsInvokingAdd,
            "a pre-entry rejection consumed arbitration or invoked native-shaped backend work");
        Assert(coordinator.DispatchOnce(f.StorageFacts(), f.Now) && backend.AddCalls == 1 && backend.TargetChecks == 2,
            "correct fresh facts could not use the untouched exact lease");
        AssertUnknown(f, coordinator);
    }

    private static void AssertUnknown(Fixture f, CargoReturnMaterializer coordinator)
    {
        Assert(!coordinator.IsInvokingAdd && !coordinator.IsDispatching,
            "a returned or throwing dispatch retained its backend invocation window");
        Assert(f.Item.Stage == CargoReturnStage.EnteredUnknown && f.Ledger.Snapshot.Phase == CargoExpeditionPhase.Returning &&
            f.Ledger.CompleteReturn().Reason == CargoReason.ReturnIncomplete &&
            !coordinator.Snapshot.StorageDeltaVerified && !coordinator.Snapshot.SaveConfirmed,
            "returned or failed native-shaped work fabricated delta/save or released unknown return state");
        CargoStorageFacts noDelta = f.StorageFacts();
        Assert(f.Ledger.ObserveEmployeeStorage(f.CaptureId, 0, noDelta, f.Now).Reason == CargoReason.MissingCapability &&
            f.Ledger.ConfirmStorageSave(f.CaptureId, 0, noDelta, f.Now).Reason == CargoReason.InvalidStage,
            "backend completion bypassed independent storage/save confirmation");
    }

    private sealed class StorageBackend : ICargoIngredientStorageBackend
    {
        private readonly Fixture _fixture;
        public CargoReturnMaterializer Coordinator;
        public int TargetChecks, AddCalls, FailurePoint;
        public CargoEmployeeReturnPlan AddedPlan;
        public Action OnAdd;
        public StorageBackend(Fixture fixture) { _fixture = fixture; }
        public void ValidateTarget(CargoEmployeeReturnPlan plan)
        {
            TargetChecks++;
            RequireEntered(plan);
            Assert(!Coordinator.IsInvokingAdd, "a target guard was allowed inside the original Add-only window");
            if ((FailurePoint == 1 && TargetChecks == 1) || (FailurePoint == 3 && TargetChecks == 2))
                throw new InvalidOperationException("Controlled target-window failure.");
        }
        public void AddIngredients(CargoEmployeeReturnPlan plan)
        {
            RequireEntered(plan);
            Assert(Coordinator.IsInvokingAdd, "the backend Add lacked the exact original-call window");
            AddCalls++; AddedPlan = plan; OnAdd?.Invoke();
            if (FailurePoint == 2) throw new InvalidOperationException("Controlled original-call failure.");
        }
        private void RequireEntered(CargoEmployeeReturnPlan plan)
        {
            Assert(ReferenceEquals(plan, _fixture.Plan) && Coordinator.IsDispatching &&
                _fixture.Ledger.OwnsEmployeeReturnPlan(plan, CargoReturnStage.EnteredUnknown),
                "the backend ran without its coordinator and the actual exact entered ledger plan");
        }
    }

    private sealed class Fixture
    {
        public readonly string Expedition = Guid.NewGuid().ToString("N"), Host = Guid.NewGuid().ToString("N"),
            Employee = Guid.NewGuid().ToString("N"), Room = Guid.NewGuid().ToString("N"), ReturnId = Guid.NewGuid().ToString("N");
        public readonly ExpeditionCargoLedger Ledger;
        public readonly CargoEmployeeReturnPlan Plan;
        public readonly long CaptureId;
        public readonly string RawFingerprint;
        public readonly double Now = 10;
        public CargoCaptureSnapshot Capture => Ledger.Snapshot.Captures.Single(capture => capture.CaptureId == CaptureId);
        public CargoReturnItemSnapshot Item => Ledger.Snapshot.ReturnItems.Single(item => item.CaptureId == CaptureId && item.ProductIndex == 0);

        public Fixture(bool lease = true)
        {
            Ledger = new ExpeditionCargoLedger(Expedition, new[]
            {
                new CargoMemberSetup { MemberId = Host, BagMode = CargoBagMode.HostNative, Capacity = 10, InitialWeight = 3 },
                new CargoMemberSetup { MemberId = Employee, BagMode = CargoBagMode.EmployeeVirtual, Capacity = 10 }
            });
            var request = new CargoCaptureRequest
            {
                ExpeditionId = Expedition, MemberId = Employee, RequestId = 1, OperationId = 1, BagRevision = 1,
                Source = new CargoSource { RoomId = Room, SceneEpoch = 1, EntityId = 7, LocalGeneration = 1 },
                Products = new[] { new CargoProduct { ProductId = 101, Grade = 2, Count = 1, UnitWeight = 1, TotalWeight = 1 } }
            };
            CargoCaptureFacts facts = CaptureFacts(request);
            CargoResult reserved = Ledger.Reserve(request, facts, Now); Accept(reserved); CaptureId = reserved.CaptureId;
            Accept(Ledger.EnterCapture(CaptureId, CaptureFacts(request), Now));
            Accept(Ledger.ConfirmCapture(CaptureId, CargoReceiptKind.EmployeeDivertedYield, request.Products, CaptureFacts(request), Now));
            Accept(Ledger.FreezeReturn(ReturnId));
            RawFingerprint = CargoValues.ProductFingerprint(request.Products[0]);
            Plan = new CargoEmployeeReturnPlan(Expedition, ReturnId, Employee, CaptureId, 0, RawFingerprint,
                "cargo-return-policy-v1/" + new string('a', 64), 501, 101, 3, 4, 5, 0);
            Accept(Ledger.BindEmployeeReturnPlan(Plan, StorageFacts(), Now));
            if (lease) Accept(Ledger.LeaseMaterialization(CaptureId, 0, StorageFacts(), Now));
        }

        private CargoCaptureFacts CaptureFacts(CargoCaptureRequest request) => new CargoCaptureFacts
        {
            ExpeditionId = Expedition, MemberId = Employee, BoundPlayerId = 2, RequestId = request.RequestId,
            OperationId = request.OperationId, ProductsFingerprint = CargoValues.ProductsFingerprint(request.Products),
            CurrentRoomId = Room, Source = CargoValues.Copy(request.Source),
            BagRevision = Ledger.Snapshot.Members.Single(member => member.MemberId == Employee).BagRevision, SampledAt = Now,
            HostAuthority = true, SourceIdentityVerified = true, OrdinaryFishVerified = true, ActorPermitted = true,
            SourceAvailable = true, CapacityPolicyVerified = true, CapacityPolicy = CargoCapacityPolicy.RejectOverCapacity,
            CapacityRoutingVerified = true, NativeEntryCapabilityVerified = true, YieldVerified = true,
            CaptureTerminal = true, DiversionVerified = true, NoHostBagWrite = true
        };

        public CargoStorageFacts StorageFacts() => new CargoStorageFacts
        {
            ExpeditionId = Expedition, ReturnId = ReturnId, MemberId = Employee, CaptureId = CaptureId, ProductIndex = 0,
            ProductFingerprint = RawFingerprint, ReturnPlanFingerprint = Plan.Fingerprint, SampledAt = Now,
            HostAuthority = true, ReturnConversionVerified = true, EmployeeStorageAdapterVerified = true,
            NativeEntryCapabilityVerified = true, StorageDeltaVerified = false, SaveConfirmed = false
        };
    }

    private static void Accept(CargoResult result)
    { Assert(result.Accepted, "fixture transition rejected: " + result.Reason); }
    private static void Assert(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
}
