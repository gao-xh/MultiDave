using System;
using System.Globalization;
using System.Linq;
using DaveCoop.Core.Cargo;
using DaveCoop.Core.World;

internal static class CargoReturnPlanTests
{
    // Plans and adapter facts here are synthetic CLR evidence. No fixture
    // converts a native slot, writes storage or confirms an actual save.
    internal static void CaptureAndReturnGradesRemainIndependentAndOwned()
    {
        var f = new Fixture();
        string rawFingerprint = CargoValues.ProductsFingerprint(f.Capture.Request.Products);
        CargoEmployeeReturnPlan plan = f.Plan(0, finalGrade: 2, count: 4);
        Accept(f.Ledger.BindEmployeeReturnPlan(plan, f.Storage(plan), f.Now));
        CargoReturnItemSnapshot returned = f.Item(0);
        Assert(returned.Stage == CargoReturnStage.Unclaimed && returned.Product.Grade == 5 && returned.Product.Count == 1 &&
            returned.Plan.FinalGrade == 2 && returned.Plan.StorageCount == 4 && returned.Plan.IngredientId == 901 &&
            returned.PlanFingerprint == plan.Fingerprint && !returned.Plan.NativePermission &&
            CargoValues.ProductsFingerprint(f.Capture.Request.Products) == rawFingerprint && f.Member(false).Inventory[0].Product.Grade == 5,
            "return conversion replaced the capture grade/count/fingerprint or entered storage");
        Assert(typeof(CargoEmployeeReturnPlan).GetProperties().All(property => !property.CanWrite), "employee return plan is mutable");
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CargoEmployeeReturnPlan equivalent = Rebuild(plan);
            Assert(equivalent.Fingerprint == plan.Fingerprint &&
                f.Ledger.BindEmployeeReturnPlan(equivalent, f.Storage(equivalent), f.Now).Reason == CargoReason.Duplicate,
                "culture or equivalent immutable copies changed the fixed return plan identity");
        }
        finally { CultureInfo.CurrentCulture = previous; }
        CargoLedgerSnapshot caller = f.Ledger.Snapshot;
        caller.ReturnItems[0].Product.Grade = 900;
        caller.ReturnItems[0].Plan = Rebuild(plan, finalGrade: 9);
        caller.ReturnItems[0].PlanFingerprint = "caller-replaced";
        Assert(f.Item(0).PlanFingerprint == plan.Fingerprint && f.Item(0).Plan.FinalGrade == 2 && f.Item(0).Product.Grade == 5 &&
            CargoValues.ProductsFingerprint(f.Capture.Request.Products) == rawFingerprint && f.Member(false).Weight == 2,
            "a mutable snapshot changed the owned plan, historical bag weight or capture fingerprint");
        NoNativeAuthority(f);
        Reject(f.Ledger.CompleteReturn(), CargoReason.ReturnIncomplete);
    }

    internal static void BindingRejectsInvalidIdentityAndUnverifiedConversion()
    {
        var f = new Fixture(); CargoEmployeeReturnPlan plan = f.Plan(0);
        foreach (CargoEmployeeReturnPlan wrong in new[]
        {
            Rebuild(plan, expedition: Guid.NewGuid().ToString("N")),
            Rebuild(plan, returnId: Guid.NewGuid().ToString("N")),
            Rebuild(plan, member: f.Host),
            Rebuild(plan, rawFingerprint: CargoValues.ProductFingerprint(f.Capture.Request.Products[1]))
        })
        {
            Reject(f.Ledger.BindEmployeeReturnPlan(wrong, f.Storage(wrong), f.Now), CargoReason.WrongIdentity);
            Assert(f.Item(0).Plan == null && f.Item(0).Stage == CargoReturnStage.Unclaimed, "wrong identity pinned a plan");
        }
        Reject(f.Ledger.BindEmployeeReturnPlan(Rebuild(plan, captureId: 9999), f.Storage(plan), f.Now), CargoReason.NotFound);
        Reject(f.Ledger.BindEmployeeReturnPlan(Rebuild(plan, productIndex: 7), f.Storage(plan), f.Now), CargoReason.NotFound);
        CargoStorageFacts stale = f.Storage(plan); stale.SampledAt = f.Now - 1;
        Reject(f.Ledger.BindEmployeeReturnPlan(plan, stale, f.Now), CargoReason.StaleFacts);
        CargoStorageFacts untrusted = f.Storage(plan); untrusted.HostAuthority = false;
        Reject(f.Ledger.BindEmployeeReturnPlan(plan, untrusted, f.Now), CargoReason.MissingCapability);
        CargoStorageFacts unconverted = f.Storage(plan); unconverted.ReturnConversionVerified = false;
        Reject(f.Ledger.BindEmployeeReturnPlan(plan, unconverted, f.Now), CargoReason.MissingCapability);
        CargoStorageFacts wrongPlan = f.Storage(plan); wrongPlan.ReturnPlanFingerprint = Rebuild(plan, count: 9).Fingerprint;
        Reject(f.Ledger.BindEmployeeReturnPlan(plan, wrongPlan, f.Now), CargoReason.WrongIdentity);
        Reject(f.Ledger.BindEmployeeReturnPlan(null, f.Storage(plan), f.Now), CargoReason.InvalidInput);
        Assert(f.Item(0).Plan == null, "rejected conversion evidence left an authoritative plan");

        Throws(() => Rebuild(plan, ingredient: 0)); Throws(() => Rebuild(plan, parent: -1)); Throws(() => Rebuild(plan, rank: -1));
        Throws(() => Rebuild(plan, finalGrade: -1)); Throws(() => Rebuild(plan, finalGrade: 1001));
        Throws(() => Rebuild(plan, count: 0)); Throws(() => Rebuild(plan, count: 1000001)); Throws(() => Rebuild(plan, place: -1));
        Throws(() => Rebuild(plan, policy: "fixture-name-is-not-a-policy-fingerprint"));
        Throws(() => Rebuild(plan, rawFingerprint: "capture-id-is-not-a-product-fingerprint"));
        Assert(f.Item(0).Plan == null && f.Capture.Stage == CargoCaptureStage.Confirmed, "invalid output affected confirmed cargo");
        NoNativeAuthority(f);
    }

    internal static void FirstPolicyAndOutputStayFixedThroughUnknownMaterialization()
    {
        var f = new Fixture(); CargoEmployeeReturnPlan plan = f.Plan(0); CargoStorageFacts facts = f.Storage(plan);
        Accept(f.Ledger.BindEmployeeReturnPlan(plan, facts, f.Now));
        Accept(f.Ledger.LeaseMaterialization(f.CaptureId, 0, facts, f.Now));
        Accept(f.Ledger.EnterMaterialization(f.CaptureId, 0, facts, f.Now));
        foreach (CargoEmployeeReturnPlan changed in new[]
        {
            Rebuild(plan, policy: Policy("other-policy")), Rebuild(plan, ingredient: 902), Rebuild(plan, parent: 1),
            Rebuild(plan, rank: 1), Rebuild(plan, finalGrade: 3), Rebuild(plan, count: 5), Rebuild(plan, place: 1)
        })
        {
            Assert(changed.Fingerprint != plan.Fingerprint, "a changed policy/output was omitted from the plan fingerprint");
            Reject(f.Ledger.BindEmployeeReturnPlan(changed, f.Storage(changed), f.Now), CargoReason.Conflict);
            Assert(f.Item(0).Stage == CargoReturnStage.EnteredUnknown && f.Item(0).PlanFingerprint == plan.Fingerprint,
                "changed plan released or replaced uncertain materialization");
        }
        CargoStorageFacts duplicate = f.Storage(plan); duplicate.ReturnConversionVerified = false;
        Reject(f.Ledger.BindEmployeeReturnPlan(Rebuild(plan), duplicate, f.Now), CargoReason.Duplicate);
        Reject(f.Ledger.EnterMaterialization(f.CaptureId, 0, facts, f.Now), CargoReason.InvalidStage);
        Reject(f.Ledger.CompleteReturn(), CargoReason.ReturnIncomplete);
        Assert(f.Member(false).Inventory.Length == 2 && f.Member(false).Weight == 2, "uncertain storage discarded confirmed employee cargo");
        NoNativeAuthority(f);
    }

    internal static void EveryEmployeeStageRequiresItsPlanWhileHostKeepsTheOriginalChain()
    {
        var f = new Fixture(productCount: 1, freeze: false); long hostId = f.CommitHost(); f.Freeze();
        CargoEmployeeReturnPlan plan = f.Plan(0); CargoStorageFacts facts = f.Storage(plan);
        Reject(f.Ledger.LeaseMaterialization(f.CaptureId, 0, facts, f.Now), CargoReason.MissingCapability);
        Accept(f.Ledger.BindEmployeeReturnPlan(plan, facts, f.Now));
        CargoStorageFacts wrong = f.Storage(plan); wrong.ReturnPlanFingerprint = null;
        Reject(f.Ledger.LeaseMaterialization(f.CaptureId, 0, wrong, f.Now), CargoReason.WrongIdentity);
        Assert(f.Item(0).Stage == CargoReturnStage.Unclaimed, "missing plan identity moved the employee lease");
        Accept(f.Ledger.LeaseMaterialization(f.CaptureId, 0, facts, f.Now));
        wrong = f.Storage(plan); wrong.ReturnPlanFingerprint = Rebuild(plan, count: 8).Fingerprint;
        Reject(f.Ledger.EnterMaterialization(f.CaptureId, 0, wrong, f.Now), CargoReason.WrongIdentity);
        Assert(f.Item(0).Stage == CargoReturnStage.Leased, "wrong plan entered native materialization");
        Accept(f.Ledger.EnterMaterialization(f.CaptureId, 0, facts, f.Now));
        Reject(f.Ledger.ObserveEmployeeStorage(f.CaptureId, 0, wrong, f.Now), CargoReason.WrongIdentity);
        Assert(f.Item(0).Stage == CargoReturnStage.EnteredUnknown, "wrong output fingerprint established storage");
        Accept(f.Ledger.ObserveEmployeeStorage(f.CaptureId, 0, facts, f.Now));
        Reject(f.Ledger.ConfirmStorageSave(f.CaptureId, 0, wrong, f.Now), CargoReason.WrongIdentity);
        Assert(f.Item(0).Stage == CargoReturnStage.StorageObserved, "wrong output fingerprint established a save");
        Accept(f.Ledger.ConfirmStorageSave(f.CaptureId, 0, facts, f.Now));

        CargoCaptureSnapshot host = f.Ledger.Snapshot.Captures.Single(capture => capture.CaptureId == hostId);
        var hostFacts = new CargoStorageFacts
        {
            ExpeditionId = f.Expedition, ReturnId = f.Return, MemberId = f.Host, CaptureId = hostId, ProductIndex = 0,
            ProductFingerprint = CargoValues.ProductFingerprint(host.Request.Products[0]), SampledAt = f.Now,
            HostAuthority = true, HostNativeStorageChainVerified = true, StorageDeltaVerified = true, SaveConfirmed = true
        };
        Reject(f.Ledger.LeaseMaterialization(hostId, 0, hostFacts, f.Now), CargoReason.WrongBagMode);
        CargoEmployeeReturnPlan hostPlan = FixturePlan(f.Ledger.Snapshot, hostId, 0);
        Reject(f.Ledger.BindEmployeeReturnPlan(hostPlan, hostFacts, f.Now), CargoReason.WrongBagMode);
        Accept(f.Ledger.ObserveHostStorage(hostId, 0, hostFacts, f.Now));
        Accept(f.Ledger.ConfirmStorageSave(hostId, 0, hostFacts, f.Now));
        Assert(f.Ledger.Snapshot.ReturnItems.Single(item => item.CaptureId == hostId).Plan == null && f.Member(true).Weight == 6,
            "host observation created an employee plan or added its bag contents twice");
        Accept(f.Ledger.CompleteReturn());
        Assert(f.Ledger.Snapshot.Phase == CargoExpeditionPhase.Returned, "complete fixed employee/host evidence did not finish tracked return");
        NoNativeAuthority(f);
    }

    internal static void PartialSaveDisconnectAndAbortKeepTheUnknownPlan()
    {
        var f = new Fixture(); CargoEmployeeReturnPlan one = f.Plan(0), two = f.Plan(1);
        CargoStorageFacts first = f.Storage(one), second = f.Storage(two);
        Accept(f.Ledger.BindEmployeeReturnPlan(one, first, f.Now)); Accept(f.Ledger.BindEmployeeReturnPlan(two, second, f.Now));
        Accept(f.Ledger.LeaseMaterialization(f.CaptureId, 0, first, f.Now)); Accept(f.Ledger.EnterMaterialization(f.CaptureId, 0, first, f.Now));
        Accept(f.Ledger.ObserveEmployeeStorage(f.CaptureId, 0, first, f.Now)); Accept(f.Ledger.ConfirmStorageSave(f.CaptureId, 0, first, f.Now));
        Accept(f.Ledger.LeaseMaterialization(f.CaptureId, 1, second, f.Now)); Accept(f.Ledger.EnterMaterialization(f.CaptureId, 1, second, f.Now));
        Accept(f.Ledger.SetConnected(f.Employee, false)); Accept(f.Ledger.Abort());
        Assert(f.Item(0).Stage == CargoReturnStage.SaveConfirmed && f.Item(1).Stage == CargoReturnStage.EnteredUnknown &&
            f.Item(0).PlanFingerprint == one.Fingerprint && f.Item(1).PlanFingerprint == two.Fingerprint &&
            !f.Member(false).Connected && f.Member(false).Inventory.Length == 2,
            "disconnect/abort forgot partial saves, unknown output plans or tracked cargo");
        CargoEmployeeReturnPlan changed = Rebuild(two, count: 8);
        Reject(f.Ledger.BindEmployeeReturnPlan(changed, f.Storage(changed), f.Now), CargoReason.Conflict);
        Reject(f.Ledger.BindEmployeeReturnPlan(two, second, f.Now), CargoReason.Duplicate);
        Reject(f.Ledger.LeaseMaterialization(f.CaptureId, 1, second, f.Now), CargoReason.InvalidStage);
        Reject(f.Ledger.EnterMaterialization(f.CaptureId, 1, second, f.Now), CargoReason.InvalidStage);
        Accept(f.Ledger.ObserveEmployeeStorage(f.CaptureId, 1, second, f.Now)); Accept(f.Ledger.ConfirmStorageSave(f.CaptureId, 1, second, f.Now));
        Assert(f.Item(1).Stage == CargoReturnStage.SaveConfirmed && f.Ledger.Snapshot.Phase == CargoExpeditionPhase.Aborted &&
            f.Item(1).PlanFingerprint == two.Fingerprint, "late same-plan evidence replayed or manufactured a normal return");
        Reject(f.Ledger.CompleteReturn(), CargoReason.InvalidStage);
        NoNativeAuthority(f);
    }

    internal static void FrozenLateConfirmedCaptureBindsOnlyItsOriginalProducts()
    {
        var f = new Fixture(confirm: false); CargoEmployeeReturnPlan plan = f.Plan(0);
        string rawFingerprint = CargoValues.ProductsFingerprint(f.Capture.Request.Products);
        Reject(f.Ledger.BindEmployeeReturnPlan(plan, f.Storage(plan), f.Now), CargoReason.InvalidStage);
        Assert(f.Item(0).Plan == null, "an unknown capture gained a converted return plan");
        Accept(f.Ledger.SetConnected(f.Employee, false));
        CargoCaptureFacts late = f.CaptureFacts(f.Capture.Request); late.ActorPermitted = false; late.SourceAvailable = false;
        Accept(f.Ledger.ConfirmCapture(f.CaptureId, CargoReceiptKind.EmployeeDivertedYield, f.Capture.Request.Products, late, f.Now));
        Accept(f.Ledger.BindEmployeeReturnPlan(plan, f.Storage(plan), f.Now));
        CargoEmployeeReturnPlan swapped = Rebuild(plan, productIndex: 1);
        Reject(f.Ledger.BindEmployeeReturnPlan(swapped, f.Storage(swapped), f.Now), CargoReason.WrongIdentity);
        Assert(f.Ledger.Snapshot.ReturnItems.Length == 2 && f.Ledger.Snapshot.ReturnId == f.Return &&
            f.Item(0).PlanFingerprint == plan.Fingerprint && f.Item(1).Plan == null && !f.Member(false).Connected &&
            CargoValues.ProductsFingerprint(f.Capture.Request.Products) == rawFingerprint,
            "late confirmation appended membership, rebound another product or changed capture data");
        var active = new Fixture(freeze: false);
        CargoEmployeeReturnPlan premature = new CargoEmployeeReturnPlan(active.Expedition, active.Return, active.Employee, active.CaptureId, 0,
            CargoValues.ProductFingerprint(active.Capture.Request.Products[0]), Policy("fixture-policy"), 901, 0, 0, 2, 4, 0);
        Reject(active.Ledger.BindEmployeeReturnPlan(premature, active.Storage(premature), active.Now), CargoReason.NotFound);
        Assert(active.Ledger.Snapshot.Phase == CargoExpeditionPhase.Active && active.Ledger.Snapshot.ReturnItems.Length == 0,
            "a plan before the frozen return created batch membership");
        NoNativeAuthority(f); NoNativeAuthority(active);
    }

    // Shared by the three existing fixtures' Storage helpers. This is a pure
    // candidate constructor/read; the call sites bind explicitly before Lease.
    internal static CargoEmployeeReturnPlan FixturePlan(CargoLedgerSnapshot snapshot, long captureId, int productIndex)
    {
        CargoReturnItemSnapshot item = snapshot.ReturnItems.Single(value => value.CaptureId == captureId && value.ProductIndex == productIndex);
        if (item.Plan != null) return item.Plan;
        CargoCaptureSnapshot capture = snapshot.Captures.Single(value => value.CaptureId == captureId);
        CargoProduct product = capture.Request.Products[productIndex];
        return new CargoEmployeeReturnPlan(snapshot.ExpeditionId, snapshot.ReturnId, capture.Request.MemberId, captureId, productIndex,
            CargoValues.ProductFingerprint(product), Policy("fixture-policy"), product.ProductId, 0, 0, product.Grade, product.Count, 0);
    }

    private static string Policy(string value) => "cargo-return-policy-v1/" + new CanonicalHash("fixture-only").Add(value).Finish();
    private static CargoEmployeeReturnPlan Rebuild(CargoEmployeeReturnPlan plan, string expedition = null, string returnId = null, string member = null,
        long? captureId = null, int? productIndex = null, string rawFingerprint = null, string policy = null,
        int? ingredient = null, int? parent = null, int? rank = null, int? finalGrade = null, int? count = null, int? place = null) =>
        new CargoEmployeeReturnPlan(expedition ?? plan.ExpeditionId, returnId ?? plan.ReturnId, member ?? plan.MemberId,
            captureId ?? plan.CaptureId, productIndex ?? plan.ProductIndex, rawFingerprint ?? plan.RawProductFingerprint,
            policy ?? plan.PolicyFingerprint, ingredient ?? plan.IngredientId, parent ?? plan.ParentId, rank ?? plan.Rank,
            finalGrade ?? plan.FinalGrade, count ?? plan.StorageCount, place ?? plan.Place);
    private static void Accept(CargoResult result) { Assert(result.Accepted, "return transition rejected: " + result.Reason); }
    private static void Reject(CargoResult result, CargoReason reason)
    { Assert(!result.Accepted && result.Reason == reason, "expected return rejection " + reason + ", got " + result.Reason); }
    private static void Throws(Action action) { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Expected invalid return plan."); }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void NoNativeAuthority(Fixture f)
    {
        Assert(!f.Ledger.Snapshot.NativeExecutionImplemented && !f.Ledger.Snapshot.CrashSafeExactlyOnce && !f.Ledger.Snapshot.NativeBagInventoryComplete &&
            f.Ledger.Snapshot.ReturnItems.All(item => item.Plan == null || !item.Plan.NativePermission),
            "CLR return plan granted native storage execution, complete bag or crash-safe save proof");
    }

    private sealed class Fixture
    {
        public readonly string Expedition = Guid.NewGuid().ToString("N"), Host = Guid.NewGuid().ToString("N"), Employee = Guid.NewGuid().ToString("N"),
            Room = Guid.NewGuid().ToString("N"), Return = Guid.NewGuid().ToString("N");
        public readonly ExpeditionCargoLedger Ledger;
        public readonly long CaptureId;
        public readonly double Now = 10;
        public Fixture(int productCount = 2, bool confirm = true, bool freeze = true)
        {
            Ledger = new ExpeditionCargoLedger(Expedition, new[]
            {
                new CargoMemberSetup { MemberId = Host, BagMode = CargoBagMode.HostNative, Capacity = 20, InitialWeight = 4 },
                new CargoMemberSetup { MemberId = Employee, BagMode = CargoBagMode.EmployeeVirtual, Capacity = 20 }
            });
            var products = new CargoProduct[productCount];
            for (int i = 0; i < products.Length; i++) products[i] = new CargoProduct { ProductId = 201 + i, Grade = 5 + i, Count = 1, UnitWeight = 1, TotalWeight = 1 };
            var request = new CargoCaptureRequest
            {
                ExpeditionId = Expedition, MemberId = Employee, RequestId = 1, OperationId = 1, BagRevision = Member(false).BagRevision,
                Source = new CargoSource { RoomId = Room, SceneEpoch = 1, EntityId = 7, LocalGeneration = 1 }, Products = products
            };
            CargoResult reserved = Ledger.Reserve(request, CaptureFacts(request), Now); Accept(reserved); CaptureId = reserved.CaptureId;
            Accept(Ledger.EnterCapture(CaptureId, CaptureFacts(request), Now));
            if (confirm) Accept(Ledger.ConfirmCapture(CaptureId, CargoReceiptKind.EmployeeDivertedYield, products, CaptureFacts(request), Now));
            if (freeze) Freeze();
        }
        public CargoCaptureSnapshot Capture => Ledger.Snapshot.Captures.Single(item => item.CaptureId == CaptureId);
        public CargoMemberSnapshot Member(bool host) => Ledger.Snapshot.Members.Single(item => item.MemberId == (host ? Host : Employee));
        public CargoReturnItemSnapshot Item(int index) => Ledger.Snapshot.ReturnItems.Single(item => item.CaptureId == CaptureId && item.ProductIndex == index);
        public void Freeze() => Accept(Ledger.FreezeReturn(Return));
        public CargoEmployeeReturnPlan Plan(int index, int finalGrade = 2, int count = 4)
        {
            CargoProduct product = Capture.Request.Products[index];
            return new CargoEmployeeReturnPlan(Expedition, Return, Employee, CaptureId, index, CargoValues.ProductFingerprint(product),
                Policy("fixture-policy"), 901 + index, 0, 0, finalGrade, count, 0);
        }
        public CargoStorageFacts Storage(CargoEmployeeReturnPlan plan) => new CargoStorageFacts
        {
            ExpeditionId = plan.ExpeditionId, ReturnId = plan.ReturnId, MemberId = plan.MemberId, CaptureId = plan.CaptureId, ProductIndex = plan.ProductIndex,
            ProductFingerprint = plan.RawProductFingerprint, ReturnPlanFingerprint = plan.Fingerprint, ReturnConversionVerified = true,
            SampledAt = Now, HostAuthority = true, EmployeeStorageAdapterVerified = true, NativeEntryCapabilityVerified = true,
            StorageDeltaVerified = true, SaveConfirmed = true
        };
        public CargoCaptureFacts CaptureFacts(CargoCaptureRequest request)
        {
            bool host = request.MemberId == Host; CargoMemberSnapshot member = Member(host); double weight = request.Products.Sum(product => product.TotalWeight);
            return new CargoCaptureFacts
            {
                ExpeditionId = Expedition, MemberId = request.MemberId, BoundPlayerId = host ? 1 : 2, RequestId = request.RequestId, OperationId = request.OperationId,
                ProductsFingerprint = CargoValues.ProductsFingerprint(request.Products), CurrentRoomId = Room, Source = CargoValues.Copy(request.Source),
                BagRevision = member.BagRevision, SampledAt = Now, HostAuthority = true, SourceIdentityVerified = true, OrdinaryFishVerified = true,
                ActorPermitted = true, SourceAvailable = true, CapacityPolicyVerified = true, CapacityPolicy = CargoCapacityPolicy.RejectOverCapacity,
                CapacityRoutingVerified = true, NativeEntryCapabilityVerified = true, YieldVerified = true, CaptureTerminal = true,
                DiversionVerified = true, NoHostBagWrite = true, ActualBagDeltaVerified = true, HostBagWeightVerified = true,
                NativeBagWeightBefore = member.Weight, NativeBagWeightAfter = member.Weight + weight, NativeCurrentWeight = member.Weight
            };
        }
        public long CommitHost()
        {
            var request = new CargoCaptureRequest
            {
                ExpeditionId = Expedition, MemberId = Host, RequestId = 1, OperationId = 2, BagRevision = Member(true).BagRevision,
                Source = new CargoSource { RoomId = Room, SceneEpoch = 1, EntityId = 8, LocalGeneration = 1 },
                Products = new[] { new CargoProduct { ProductId = 202, Grade = 1, Count = 1, UnitWeight = 2, TotalWeight = 2 } }
            };
            CargoResult reserved = Ledger.Reserve(request, CaptureFacts(request), Now); Accept(reserved);
            Accept(Ledger.EnterCapture(reserved.CaptureId, CaptureFacts(request), Now));
            CargoCaptureFacts receipt = CaptureFacts(request); receipt.NativeCurrentWeight = receipt.NativeBagWeightAfter;
            Accept(Ledger.ConfirmCapture(reserved.CaptureId, CargoReceiptKind.HostNativeBagDelta, request.Products, receipt, Now)); return reserved.CaptureId;
        }
    }
}
