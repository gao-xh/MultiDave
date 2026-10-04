using System;
using System.Linq;
using DaveCoop.Core.Cargo;

// These host-fact flags exercise the actual CLR ledger, not a native selector,
// materialization barrier, save, callback, capacity adapter or capture receipt.
internal static class CargoLateYieldTests
{
    internal static void SourceOnlyLeasesMintAcrossMembersWithoutInventingYield()
    {
        var f = new Fixture(); CargoSourceIntent hostIntent = f.Intent(true, 1, 1);
        CargoSourceLease host = f.Reserve(hostIntent), employee = f.Reserve(f.Intent(false, 1, 2));
        Assert(host.OperationId == 1 && employee.OperationId == 2 && host.Intent.GateOperationId == 1 && employee.Intent.GateOperationId == 1 &&
            f.Ledger.GlobalOperationHighWater == 2, "participant-local Gate IDs became competing cargo IDs");
        CargoLedgerSnapshot snapshot = f.Ledger.Snapshot;
        Assert(snapshot.Captures.All(capture => capture.Stage == CargoCaptureStage.Reserved && capture.Request == null && !capture.YieldBound && capture.Intent != null) &&
            f.Member(true).Weight == 4 && f.Member(false).Weight == 0 && f.Member(false).ReservedWeight == 0 &&
            f.Member(true).Inventory.Length == 0 && f.Member(false).Inventory.Length == 0,
            "source-only reservation fabricated products, charged another bag or inferred empty completion");
        CargoInventorySnapshot projection = CargoInventoryFrames.FromLedger(snapshot, 1, 1, f.Room);
        Assert(projection.ReservedCaptureCount == 2 && projection.UnknownCaptureCount == 0 && projection.Members.All(member => member.Inventory.Length == 0),
            "nullable product request broke the production tracked projection");
        hostIntent.Source.LocalGeneration = 99;
        Assert(host.Intent.Source.LocalGeneration == 1 && f.Capture(host).Intent.Source.LocalGeneration == 1, "caller changed a minted source identity");
        NoNative(host, snapshot);
    }

    internal static void SelectionEntryNeedsAnIsolationBarrierBeforeAnyNativeBusiness()
    {
        var f = new Fixture(); CargoSourceLease lease = f.Reserve(f.Intent(false, 1, 1));
        CargoSourceFacts facts = f.SourceFacts(lease.Intent, lease); facts.YieldSelectionIsolationVerified = false;
        Reject(f.Ledger.EnterSelection(lease, facts, f.Now), CargoReason.MissingCapability);
        facts = f.SourceFacts(lease.Intent, lease); facts.NativeEntryCapabilityVerified = false;
        Reject(f.Ledger.EnterSelection(lease, facts, f.Now), CargoReason.MissingCapability);
        CargoProduct[] products = Products();
        Reject(f.Ledger.LateSeal(lease, products, f.LateFacts(lease, products), f.Now), CargoReason.InvalidStage);
        facts = f.SourceFacts(lease.Intent, lease); facts.ActorRevision++;
        Reject(f.Ledger.EnterSelection(lease, facts, f.Now), CargoReason.WrongIdentity);
        facts = f.SourceFacts(lease.Intent, lease); facts.Source.LocalGeneration++;
        Reject(f.Ledger.EnterSelection(lease, facts, f.Now), CargoReason.WrongIdentity);
        facts = f.SourceFacts(lease.Intent, lease); facts.SampledAt = f.Now - 1;
        Reject(f.Ledger.EnterSelection(lease, facts, f.Now), CargoReason.StaleFacts);
        Assert(f.Capture(lease).Stage == CargoCaptureStage.Reserved && !f.Capture(lease).YieldBound, "failed entry used a later yield claim as retroactive permission");
        Accept(f.Ledger.EnterSelection(lease, f.SourceFacts(lease.Intent, lease), f.Now));
        Reject(f.Ledger.EnterSelection(lease, f.SourceFacts(lease.Intent, lease), f.Now), CargoReason.InvalidStage);
        facts = f.SourceFacts(lease.Intent, lease); facts.NativeNotEntered = true;
        Reject(f.Ledger.CancelSelectionNotEntered(lease, facts, f.Now), CargoReason.InvalidStage);
        Assert(f.Capture(lease).Stage == CargoCaptureStage.EnteredUnknown, "entered selection became retryable or not-entered");
        NoNative(lease, f.Ledger.Snapshot);
    }

    internal static void CompleteLateYieldReservesPersonalWeightButNeedsAReceipt()
    {
        var f = new Fixture(hostWeight: 10); CargoSourceLease lease = f.Enter(f.Intent(false, 1, 1));
        CargoProduct[] products = Products(); long revision = f.Member(false).BagRevision;
        Accept(f.Ledger.LateSeal(lease, products, f.LateFacts(lease, products), f.Now));
        Assert(f.Capture(lease).Stage == CargoCaptureStage.EnteredUnknown && f.Capture(lease).YieldBound &&
            f.Capture(lease).Request.OperationId == lease.OperationId && f.Member(false).ReservedWeight == 3 &&
            f.Member(false).BagRevision == revision + 1 && f.Member(false).Weight == 0 && f.Member(true).Weight == 10 && f.Member(false).Inventory.Length == 0,
            "late selection was a bag commit, used host capacity, or skipped its revision");
        products[0].Count = 999;
        CargoCaptureRequest owned = f.Capture(lease).Request;
        Assert(owned.Products[0].Count == 1, "mutable selected product escaped copy ownership");
        Reject(f.Ledger.EnterCapture(lease.CaptureId, f.Receipt(lease), f.Now), CargoReason.InvalidStage);
        CargoCaptureFacts receipt = f.Receipt(lease); receipt.CaptureTerminal = false;
        Reject(f.Ledger.ConfirmCapture(lease.CaptureId, CargoReceiptKind.EmployeeDivertedYield, owned.Products, receipt, f.Now), CargoReason.MissingCapability);
        receipt = f.Receipt(lease); receipt.NoHostBagWrite = false;
        Reject(f.Ledger.ConfirmCapture(lease.CaptureId, CargoReceiptKind.EmployeeDivertedYield, owned.Products, receipt, f.Now), CargoReason.MissingCapability);
        Accept(f.Ledger.ConfirmCapture(lease.CaptureId, CargoReceiptKind.EmployeeDivertedYield, owned.Products, f.Receipt(lease), f.Now));
        Assert(f.Member(false).Weight == 3 && f.Member(false).ReservedWeight == 0 && f.Member(false).Inventory.Length == 2 && f.Member(true).Weight == 10,
            "verified synthetic employee receipt mirrored host bag weight or confirmed twice");
        Reject(f.Ledger.LateSeal(lease, owned.Products, f.LateFacts(lease, owned.Products), f.Now), CargoReason.Duplicate);
        CargoProduct[] changed = CargoValues.CopyProducts(owned.Products); changed[1].Grade++;
        Reject(f.Ledger.LateSeal(lease, changed, f.LateFacts(lease, changed), f.Now), CargoReason.Conflict);
        Assert(f.Member(false).Weight == 3, "late duplicate or conflict re-reserved confirmed weight");
        var zero = new Fixture(); CargoSourceLease zeroLease = zero.Enter(zero.Intent(false, 1, 1));
        long beforeZero = zero.Member(false).BagRevision; CargoProduct[] zeroProducts = new[] { Product(101, 0) };
        Accept(zero.Ledger.LateSeal(zeroLease, zeroProducts, zero.LateFacts(zeroLease, zeroProducts), zero.Now));
        Assert(zero.Member(false).BagRevision == beforeZero + 1 && zero.Capture(zeroLease).YieldBound &&
            zero.Capture(zeroLease).Stage == CargoCaptureStage.EnteredUnknown && zero.Member(false).Inventory.Length == 0,
            "known zero-weight yield omitted its revision or was treated as an empty completed operation");
    }

    internal static void MissingYieldCoverageAndOverCapacityKeepTheEnteredSourceUnknown()
    {
        var f = new Fixture(); CargoSourceLease lease = f.Enter(f.Intent(false, 1, 1)); CargoProduct[] products = Products();
        for (int missing = 0; missing < 6; missing++)
        {
            CargoLateYieldFacts facts = f.LateFacts(lease, products);
            if (missing == 0) facts.CompleteSelectedYield = false;
            if (missing == 1) facts.MaterializationBoundaryHeld = false;
            if (missing == 2) facts.NoBagWriteYet = false;
            if (missing == 3) facts.YieldSelectionIsolationVerified = false;
            if (missing == 4) facts.CapacityRoutingVerified = false;
            if (missing == 5) facts.CapacityPolicyVerified = false;
            Reject(f.Ledger.LateSeal(lease, products, facts, f.Now), CargoReason.MissingCapability);
            if (missing < 4) Assert(!f.Capture(lease).SelectedYieldKnown, "incomplete selected proof pinned a purported yield");
        }
        CargoLateYieldFacts wrong = f.LateFacts(lease, products); wrong.OperationId++;
        Reject(f.Ledger.LateSeal(lease, products, wrong, f.Now), CargoReason.WrongIdentity);
        wrong = f.LateFacts(lease, products); wrong.ProductsFingerprint = new string('0', 64);
        Reject(f.Ledger.LateSeal(lease, products, wrong, f.Now), CargoReason.WrongIdentity);
        Reject(f.Ledger.LateSeal(lease, Array.Empty<CargoProduct>(), wrong, f.Now), CargoReason.InvalidInput);
        CargoProduct[] excessive = new[] { Product(100, 6) };
        Reject(f.Ledger.LateSeal(lease, excessive, f.LateFacts(lease, excessive), f.Now), CargoReason.Conflict);
        CargoProduct[] invalid = new[] { Product(100, double.NaN) };
        Reject(f.Ledger.LateSeal(lease, invalid, wrong, f.Now), CargoReason.InvalidInput);
        Assert(f.Capture(lease).Stage == CargoCaptureStage.EnteredUnknown && !f.Capture(lease).YieldBound &&
            f.Member(false).ReservedWeight == 0 && f.Member(false).Weight == 0, "missing/denied yield released source, fabricated empty cargo or partially reserved weight");
        CargoSourceIntent competing = f.Intent(true, 1, 1);
        Reject(f.Ledger.SourceReserve(competing, f.SourceFacts(competing), f.Now, out _), CargoReason.SourceBusy);
        Assert(f.Member(true).HighestRequestId == 1 && f.Ledger.GlobalOperationHighWater == 1, "business refusal failed request dedupe or minted a denied cargo operation");
        var over = new Fixture(); CargoSourceLease overLease = over.Enter(over.Intent(false, 1, 1));
        Reject(over.Ledger.LateSeal(overLease, excessive, over.LateFacts(overLease, excessive), over.Now), CargoReason.CapacityExceeded);
        CargoLateYieldFacts overweight = over.LateFacts(overLease, excessive); overweight.CapacityPolicy = CargoCapacityPolicy.AllowPersonalOverweight;
        Accept(over.Ledger.LateSeal(overLease, excessive, overweight, over.Now));
        Assert(over.Member(false).ReservedWeight == 6 && over.Member(true).ReservedWeight == 0 && over.Capture(overLease).Stage == CargoCaptureStage.EnteredUnknown,
            "explicit personal overweight used the host capacity or became capture terminal");
    }

    internal static void CapacityDeniedSelectedYieldCannotBeReplacedOrRerolled()
    {
        var f = new Fixture(); CargoSourceLease lease = f.Enter(f.Intent(false, 1, 1));
        CargoProduct[] chosen = new[] { Product(101, 4), Product(202, 2) };
        CargoProduct[] original = CargoValues.CopyProducts(chosen); string fingerprint = CargoValues.ProductsFingerprint(original);
        CargoLateYieldFacts incomplete = f.LateFacts(lease, chosen); incomplete.NoBagWriteYet = false;
        Reject(f.Ledger.LateSeal(lease, chosen, incomplete, f.Now), CargoReason.MissingCapability);
        CargoLateYieldFacts wrong = f.LateFacts(lease, chosen); wrong.OperationId++;
        Reject(f.Ledger.LateSeal(lease, chosen, wrong, f.Now), CargoReason.WrongIdentity);
        Reject(f.Ledger.LateSeal(lease, new[] { Product(101, double.NaN) }, f.LateFacts(lease, chosen), f.Now), CargoReason.InvalidInput);
        Assert(!f.Capture(lease).SelectedYieldKnown && f.Capture(lease).SelectedYieldFingerprint == null,
            "invalid identity, products or incomplete proof pinned a native yield");
        Reject(f.Ledger.LateSeal(lease, chosen, f.LateFacts(lease, chosen), f.Now), CargoReason.CapacityExceeded);
        CargoCaptureSnapshot held = f.Capture(lease);
        Assert(held.SelectedYieldKnown && held.SelectedYieldFingerprint == fingerprint && held.Stage == CargoCaptureStage.EnteredUnknown &&
            !held.YieldBound && held.Request == null && f.Member(false).ReservedWeight == 0 && f.Member(false).Weight == 0 && f.Member(false).Inventory.Length == 0,
            "capacity denial forgot the already-chosen plan or charged/confirmed its bag");
        chosen[0].Grade = 99; chosen[0].Count = 999;
        CargoProduct[] smaller = Products();
        Reject(f.Ledger.LateSeal(lease, smaller, f.LateFacts(lease, smaller), f.Now), CargoReason.Conflict);
        CargoProduct[] differentQuality = CargoValues.CopyProducts(original); differentQuality[1].Grade++;
        CargoLateYieldFacts changedPolicy = f.LateFacts(lease, differentQuality); changedPolicy.CapacityPolicy = CargoCapacityPolicy.AllowPersonalOverweight;
        Reject(f.Ledger.LateSeal(lease, differentQuality, changedPolicy, f.Now), CargoReason.Conflict);
        wrong = f.LateFacts(lease, original); wrong.CurrentRoomId = Guid.NewGuid().ToString("N");
        Reject(f.Ledger.LateSeal(lease, original, wrong, f.Now), CargoReason.WrongIdentity);
        Assert(f.Capture(lease).SelectedYieldFingerprint == fingerprint && f.Ledger.GlobalOperationHighWater == 1 && f.Member(false).ReservedWeight == 0,
            "caller mutation, new room or rejected replacement changed the pinned selection");
        string returnId = Guid.NewGuid().ToString("N"); Accept(f.Ledger.FreezeReturn(returnId)); Accept(f.Ledger.SetConnected(f.Employee, false));
        Reject(f.Ledger.CompleteReturn(), CargoReason.ReturnIncomplete);
        f.Now += 0.1; CargoLateYieldFacts samePlan = f.LateFacts(lease, original);
        samePlan.CapacityPolicy = CargoCapacityPolicy.AllowPersonalOverweight; samePlan.ActorPermitted = false; samePlan.SourceAvailable = false;
        samePlan.NativeEntryCapabilityVerified = false;
        Accept(f.Ledger.LateSeal(lease, original, samePlan, f.Now));
        Assert(f.Capture(lease).Request.Products[0].Grade == 2 && f.Capture(lease).Request.Products[0].Count == 1 &&
            f.Capture(lease).SelectedYieldFingerprint == fingerprint && f.Member(false).ReservedWeight == 6 &&
            f.Capture(lease).Stage == CargoCaptureStage.EnteredUnknown && !f.Member(false).Connected &&
            f.Ledger.Snapshot.ReturnId == returnId && f.Ledger.Snapshot.ReturnItems.Length == 2,
            "fresh personal policy rerolled products, revived an actor or added to another return batch");
        CargoCaptureFacts receipt = f.Receipt(lease); receipt.CapacityPolicy = CargoCapacityPolicy.AllowPersonalOverweight;
        Accept(f.Ledger.ConfirmCapture(lease.CaptureId, CargoReceiptKind.EmployeeDivertedYield, original, receipt, f.Now));
        receipt = f.Receipt(lease); receipt.CapacityPolicy = CargoCapacityPolicy.AllowPersonalOverweight;
        Reject(f.Ledger.ConfirmCapture(lease.CaptureId, CargoReceiptKind.EmployeeDivertedYield, original, receipt, f.Now), CargoReason.Duplicate);
        Assert(f.Member(false).Weight == 6 && f.Member(false).ReservedWeight == 0 && f.Member(true).Weight == 4 && f.Ledger.Snapshot.ReturnItems.Length == 2,
            "same selected batch was committed twice or copied into the host bag");
        NoNative(lease, f.Ledger.Snapshot);
    }

    internal static void SourceReplayAndNotEnteredCancellationKeepAllTombstones()
    {
        var f = new Fixture(); CargoSourceIntent intent = f.Intent(false, 1, 1); CargoSourceLease lease = f.Reserve(intent);
        CargoResult duplicate = f.Ledger.SourceReserve(intent, f.SourceFacts(intent), f.Now, out CargoSourceLease same);
        Reject(duplicate, CargoReason.Duplicate); Assert(ReferenceEquals(lease, same), "exact request did not return its fixed opaque lease");
        CargoSourceIntent changed = CargoSourceValues.Copy(intent); changed.ActorRevision++;
        Reject(f.Ledger.SourceReserve(changed, f.SourceFacts(changed), f.Now, out _), CargoReason.Conflict);
        CargoSourceIntent competing = f.Intent(true, 1, 1);
        Reject(f.Ledger.SourceReserve(competing, f.SourceFacts(competing), f.Now, out _), CargoReason.SourceBusy);
        CargoSourceFacts cancel = f.SourceFacts(lease.Intent, lease);
        Reject(f.Ledger.CancelSelectionNotEntered(lease, cancel, f.Now), CargoReason.NotEnteredProofRequired);
        cancel.NativeNotEntered = true; Accept(f.Ledger.CancelSelectionNotEntered(lease, cancel, f.Now));
        CargoSourceLease next = f.Reserve(f.Intent(true, 2, 1));
        Assert(next.OperationId == 2 && f.Capture(lease).Stage == CargoCaptureStage.NativeNotEntered &&
            f.Member(false).HighestRequestId == 1 && f.Capture(lease).Request == null, "cancel rewound operation/request identity or invented products");
        Reject(f.Ledger.SourceReserve(intent, f.SourceFacts(intent), f.Now, out same), CargoReason.Duplicate);
        Reject(f.Ledger.EnterSelection(same, f.SourceFacts(same.Intent, same), f.Now), CargoReason.InvalidStage);
        CargoSourceIntent rejected = f.Intent(false, 10, 1);
        Reject(f.Ledger.SourceReserve(rejected, f.SourceFacts(rejected), f.Now, out _), CargoReason.SourceBusy);
        CargoSourceIntent old = f.Intent(false, 9, 2);
        Reject(f.Ledger.SourceReserve(old, f.SourceFacts(old), f.Now, out _), CargoReason.Replay);
        Assert(f.Member(false).HighestRequestId == 10 && f.Ledger.GlobalOperationHighWater == 2, "business denial or replay reset request high water");
    }

    internal static void UnselectedUnknownSurvivesEpochRoomDisconnectReturnAndAbort()
    {
        var f = new Fixture(); CargoSourceLease lease = f.Enter(f.Intent(false, 1, 1));
        CargoSourceIntent otherEpoch = f.Intent(true, 1, 2); otherEpoch.Source.SceneEpoch = 2;
        Reject(f.Ledger.SourceReserve(otherEpoch, f.SourceFacts(otherEpoch), f.Now, out _), CargoReason.SourceBusy);
        CargoSourceIntent otherRoom = f.Intent(true, 2, 2); otherRoom.Source.RoomId = Guid.NewGuid().ToString("N");
        Reject(f.Ledger.SourceReserve(otherRoom, f.SourceFacts(otherRoom), f.Now, out _), CargoReason.WrongIdentity);
        Accept(f.Ledger.SetConnected(f.Employee, false)); string returnId = Guid.NewGuid().ToString("N");
        Accept(f.Ledger.FreezeReturn(returnId));
        Assert(f.Ledger.Snapshot.ReturnItems.Length == 0 && f.Ledger.Snapshot.ReturnId == returnId, "return invented products for unknown yield");
        Reject(f.Ledger.CompleteReturn(), CargoReason.ReturnIncomplete);
        CargoSourceIntent frozen = f.Intent(true, 3, 3);
        Reject(f.Ledger.SourceReserve(frozen, f.SourceFacts(frozen), f.Now, out _), CargoReason.ReturnFrozen);
        Reject(f.Ledger.EnterCapture(lease.CaptureId, new CargoCaptureFacts(), f.Now), CargoReason.InvalidStage);
        Reject(f.Ledger.CaptureNotEntered(lease.CaptureId, new CargoCaptureFacts(), f.Now), CargoReason.InvalidStage);
        Reject(f.Ledger.ConfirmCapture(lease.CaptureId, CargoReceiptKind.EmployeeDivertedYield, Products(), new CargoCaptureFacts(), f.Now), CargoReason.InvalidStage);
        Accept(f.Ledger.Abort());
        Assert(!f.Member(false).Connected && f.Capture(lease).Stage == CargoCaptureStage.EnteredUnknown && !f.Capture(lease).YieldBound,
            "lifecycle reset guessed source cancellation or employee reconnect");
        CargoInventorySnapshot projection = CargoInventoryFrames.FromLedger(f.Ledger.Snapshot, 1, 1, f.Room);
        Assert(projection.UnknownCaptureCount == 1 && projection.PendingReturnProductCount == 0 && projection.Phase == CargoExpeditionPhase.Aborted,
            "unknown with no return products projected as complete or broke protocol schema"); NoNative(lease, f.Ledger.Snapshot);
    }

    internal static void FrozenDisconnectedCaptureCanSealAndSettleOnlyItsOriginalBatch()
    {
        var f = new Fixture(); CargoSourceLease lease = f.Enter(f.Intent(false, 1, 1));
        string returnId = Guid.NewGuid().ToString("N"); Accept(f.Ledger.FreezeReturn(returnId)); Accept(f.Ledger.SetConnected(f.Employee, false));
        CargoProduct[] products = Products(); CargoLateYieldFacts late = f.LateFacts(lease, products);
        late.ActorPermitted = false; late.SourceAvailable = false; late.NativeEntryCapabilityVerified = false;
        Accept(f.Ledger.LateSeal(lease, products, late, f.Now));
        Assert(f.Ledger.Snapshot.ReturnItems.Length == 2 && f.Ledger.Snapshot.ReturnItems.All(item => item.CaptureId == lease.CaptureId && item.MemberId == f.Employee &&
            item.Stage == CargoReturnStage.Unclaimed) && f.Ledger.Snapshot.ReturnId == returnId && f.Capture(lease).Stage == CargoCaptureStage.EnteredUnknown,
            "late held products were added as a new capture, batch or storage success");
        Reject(f.Ledger.CompleteReturn(), CargoReason.ReturnIncomplete);
        CargoCaptureFacts receipt = f.Receipt(lease); receipt.ActorPermitted = false; receipt.SourceAvailable = false;
        Accept(f.Ledger.ConfirmCapture(lease.CaptureId, CargoReceiptKind.EmployeeDivertedYield, products, receipt, f.Now));
        Assert(!f.Member(false).Connected && f.Member(false).Weight == 3 && f.Member(true).Weight == 4, "late receipt restored actor dispatch or changed the host bag");
        for (int index = 0; index < 2; index++)
        {
            CargoStorageFacts storage = f.Storage(lease, index);
            Accept(f.Ledger.LeaseMaterialization(lease.CaptureId, index, storage, f.Now));
            Accept(f.Ledger.EnterMaterialization(lease.CaptureId, index, storage, f.Now));
            Accept(f.Ledger.ObserveEmployeeStorage(lease.CaptureId, index, storage, f.Now));
            Accept(f.Ledger.ConfirmStorageSave(lease.CaptureId, index, storage, f.Now));
        }
        Accept(f.Ledger.CompleteReturn());
        Assert(f.Ledger.Snapshot.Phase == CargoExpeditionPhase.Returned && f.Member(false).Inventory.Length == 0 && f.Member(false).Weight == 3,
            "tracked return was completed early or historical weight was guessed as zero");
        Reject(f.Ledger.LateSeal(lease, products, f.LateFacts(lease, products), f.Now), CargoReason.Duplicate);
        Assert(f.Ledger.Snapshot.ReturnItems.Length == 2, "late identical replay appended original batch products twice");
    }

    internal static void HostLateYieldRequiresFreshBaselineAndUsesNativeTotalOnce()
    {
        var f = new Fixture(); CargoSourceLease lease = f.Reserve(f.Intent(true, 1, 1));
        CargoSourceFacts old = f.SourceFacts(lease.Intent, lease);
        Accept(f.Ledger.ObserveHostBagWeight(f.Host, f.HostWeight(5), f.Now));
        Reject(f.Ledger.EnterSelection(lease, old, f.Now), CargoReason.Conflict);
        Accept(f.Ledger.EnterSelection(lease, f.SourceFacts(lease.Intent, lease), f.Now));
        CargoProduct[] products = Products(); CargoLateYieldFacts stale = f.LateFacts(lease, products);
        f.Now += 0.1; Reject(f.Ledger.ObserveHostBagWeight(f.Host, f.HostWeight(5), f.Now), CargoReason.Duplicate);
        stale.BagRevision = f.Member(true).BagRevision;
        Reject(f.Ledger.LateSeal(lease, products, stale, f.Now), CargoReason.StaleFacts);
        Accept(f.Ledger.LateSeal(lease, products, f.LateFacts(lease, products), f.Now));
        CargoCaptureFacts receipt = f.Receipt(lease); receipt.NativeCurrentWeight = receipt.NativeBagWeightAfter;
        Accept(f.Ledger.ConfirmCapture(lease.CaptureId, CargoReceiptKind.HostNativeBagDelta, products, receipt, f.Now));
        Assert(f.Member(true).Weight == 8 && f.Member(true).ReservedWeight == 0 && f.Member(false).Weight == 0,
            "host native total was replaced by planned total or incremented twice");
    }

    internal static void OpaqueLeaseAndSnapshotCopiesCannotChangeSourceOrOwnership()
    {
        var f = new Fixture(); CargoSourceLease lease = f.Reserve(f.Intent(false, 1, 1));
        var other = new Fixture(); CargoSourceLease foreign = other.Reserve(other.Intent(false, 1, 1));
        Reject(f.Ledger.EnterSelection(foreign, f.SourceFacts(lease.Intent, lease), f.Now), CargoReason.WrongIdentity);
        var copy = new CargoSourceLease(lease.CaptureId, lease.OperationId, lease.BoundPlayerId, lease.Intent, lease.IntentFingerprint);
        Reject(f.Ledger.EnterSelection(copy, f.SourceFacts(lease.Intent, lease), f.Now), CargoReason.WrongIdentity);
        CargoSourceIntent exposed = lease.Intent; exposed.MemberId = f.Host; exposed.Source.LocalGeneration = 99;
        CargoLedgerSnapshot snapshot = f.Ledger.Snapshot; snapshot.Captures[0].Intent.Source.EntityId = 999;
        Assert(lease.Intent.MemberId == f.Employee && lease.Intent.Source.LocalGeneration == 1 && f.Capture(lease).Intent.Source.EntityId == 1,
            "copied snapshot or lease readback rebound a mutable source/member");
        CargoSourceFacts wrongMember = f.SourceFacts(lease.Intent, lease); wrongMember.BoundPlayerId = 1;
        Reject(f.Ledger.EnterSelection(lease, wrongMember, f.Now), CargoReason.WrongIdentity);
        CargoSourceIntent canonical = CargoSourceValues.Copy(lease.Intent); canonical.Source.RoomId = Guid.Parse(f.Room).ToString("D").ToUpperInvariant();
        Assert(CargoSourceValues.Fingerprint(canonical) == lease.IntentFingerprint, "canonical source intent identity changed with GUID representation");
        Reject(f.Ledger.EnterSelection(null, new CargoSourceFacts(), f.Now), CargoReason.InvalidInput);
        NoNative(lease, snapshot);
    }

    internal static void LegacyReservationsAndSourceLeasesSharePermanentGlobalQuota()
    {
        var f = new Fixture(); CargoCaptureRequest legacy = f.Legacy(true, 1, 40, 1);
        Accept(f.Ledger.Reserve(legacy, f.LegacyFacts(legacy), f.Now));
        CargoSourceLease late = f.Reserve(f.Intent(false, 1, 2));
        Assert(late.OperationId == 41 && f.Ledger.GlobalOperationHighWater == 41, "legacy accepted number was reused by a source-only capture");
        CargoCaptureRequest collision = f.Legacy(true, 2, 41, 3);
        Reject(f.Ledger.Reserve(collision, f.LegacyFacts(collision), f.Now), CargoReason.Conflict);
        CargoCaptureRequest lower = f.Legacy(true, 3, 10, 3);
        Reject(f.Ledger.Reserve(lower, f.LegacyFacts(lower), f.Now), CargoReason.Conflict);
        CargoCaptureRequest deniedHigh = f.Legacy(true, 4, 90, 2);
        Reject(f.Ledger.Reserve(deniedHigh, f.LegacyFacts(deniedHigh), f.Now), CargoReason.SourceBusy);
        Assert(f.Ledger.GlobalOperationHighWater == 41, "business-denied legacy operation pushed the allocator beyond later valid requests");
        CargoCaptureRequest next = f.Legacy(true, 5, 42, 3); Accept(f.Ledger.Reserve(next, f.LegacyFacts(next), f.Now));
        Assert(f.Reserve(f.Intent(false, 2, 4)).OperationId == 43, "global capture allocator did not advance over accepted legacy operation");
        Reject(f.Ledger.Reserve(legacy, f.LegacyFacts(legacy), f.Now), CargoReason.Duplicate);
        var bounded = new Fixture(); CargoSourceIntent first = null; CargoSourceLease firstLease = null;
        for (int index = 1; index <= CargoValues.MaxCaptures; index++)
        {
            CargoSourceIntent intent = bounded.Intent(false, index, index); CargoSourceLease lease = bounded.Reserve(intent);
            if (index == 1) { first = intent; firstLease = lease; }
            CargoSourceFacts cancel = bounded.SourceFacts(lease.Intent, lease); cancel.NativeNotEntered = true;
            Accept(bounded.Ledger.CancelSelectionNotEntered(lease, cancel, bounded.Now));
        }
        CargoSourceIntent overflow = bounded.Intent(false, 257, 257);
        Reject(bounded.Ledger.SourceReserve(overflow, bounded.SourceFacts(overflow), bounded.Now, out _), CargoReason.QuotaExceeded);
        Reject(bounded.Ledger.SourceReserve(overflow, bounded.SourceFacts(overflow), bounded.Now, out _), CargoReason.Replay);
        Reject(bounded.Ledger.SourceReserve(first, bounded.SourceFacts(first), bounded.Now, out CargoSourceLease remembered), CargoReason.Duplicate);
        Assert(ReferenceEquals(remembered, firstLease) && bounded.Ledger.Snapshot.Captures.Length == 256 &&
            bounded.Ledger.GlobalOperationHighWater == 256 && bounded.Member(false).HighestRequestId == 257,
            "closed source quota evicted tombstones, reused IDs or reopened rejected requests");
    }

    private static CargoProduct Product(int id, double weight) => new CargoProduct { ProductId = id, Grade = 2, Count = 1, TotalWeight = weight };
    private static CargoProduct[] Products() => new[] { Product(101, 2), Product(202, 1) };
    private static long Accept(CargoResult result)
    { Assert(result.Accepted, "Expected cargo acceptance, got " + result.Reason); return result.CaptureId; }
    private static void Reject(CargoResult result, CargoReason reason) => Assert(!result.Accepted && result.Reason == reason,
        "Expected " + reason + ", got " + result.Reason);
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void NoNative(CargoSourceLease lease, CargoLedgerSnapshot snapshot) => Assert(!lease.NativePermission && !lease.SourceOperationBound &&
        !lease.NativeExecutionImplemented && !snapshot.NativeExecutionImplemented && !snapshot.CrashSafeExactlyOnce && !snapshot.NativeBagInventoryComplete,
        "caller facts or source-only stages granted native/capture/save authority");

    private sealed class Fixture
    {
        public readonly string Expedition = Guid.NewGuid().ToString("N");
        public readonly string Host = Guid.NewGuid().ToString("N");
        public readonly string Employee = Guid.NewGuid().ToString("N");
        public readonly string Room = Guid.NewGuid().ToString("N");
        public readonly ExpeditionCargoLedger Ledger;
        public double Now = 10;
        public Fixture(double hostWeight = 4)
        {
            Ledger = new ExpeditionCargoLedger(Expedition, new[]
            {
                new CargoMemberSetup { MemberId = Host, BagMode = CargoBagMode.HostNative, Capacity = 10, InitialWeight = hostWeight },
                new CargoMemberSetup { MemberId = Employee, BagMode = CargoBagMode.EmployeeVirtual, Capacity = 5 }
            });
        }
        public CargoMemberSnapshot Member(bool host) => Ledger.Snapshot.Members.Single(member => member.MemberId == (host ? Host : Employee));
        public CargoCaptureSnapshot Capture(CargoSourceLease lease) => Ledger.Snapshot.Captures.Single(capture => capture.CaptureId == lease.CaptureId);
        public CargoSourceIntent Intent(bool host, long request, long entity) => new CargoSourceIntent
        {
            ExpeditionId = Expedition, MemberId = host ? Host : Employee, RequestId = request, BagRevision = Member(host).BagRevision,
            Source = new CargoSource { RoomId = Room, SceneEpoch = 1, EntityId = entity, LocalGeneration = 1 },
            ActorRevision = 1, LoadoutRevision = 1, GateOperationId = 1
        };
        public T Fill<T>(T facts, CargoSourceIntent intent, CargoSourceLease lease = null) where T : CargoSourceFacts
        {
            bool host = intent.MemberId == Host; CargoMemberSnapshot member = Member(host);
            facts.ExpeditionId = Expedition; facts.MemberId = intent.MemberId; facts.BoundPlayerId = host ? 1 : 2;
            facts.RequestId = intent.RequestId; facts.OperationId = lease?.OperationId ?? 0; facts.IntentFingerprint = lease?.IntentFingerprint;
            facts.CurrentRoomId = intent.Source.RoomId; facts.Source = CargoValues.Copy(intent.Source); facts.BagRevision = member.BagRevision;
            facts.ActorRevision = intent.ActorRevision; facts.LoadoutRevision = intent.LoadoutRevision; facts.SampledAt = Now;
            facts.HostAuthority = true; facts.SourceIdentityVerified = true; facts.OrdinaryFishVerified = true; facts.ActorPermitted = true; facts.SourceAvailable = true;
            facts.CapacityPolicyVerified = true; facts.CapacityPolicy = CargoCapacityPolicy.RejectOverCapacity; facts.CapacityRoutingVerified = true;
            facts.NativeEntryCapabilityVerified = true; facts.YieldSelectionIsolationVerified = true; facts.HostBagWeightVerified = true; facts.NativeCurrentWeight = member.Weight;
            return facts;
        }
        public CargoSourceFacts SourceFacts(CargoSourceIntent intent, CargoSourceLease lease = null) => Fill(new CargoSourceFacts(), intent, lease);
        public CargoLateYieldFacts LateFacts(CargoSourceLease lease, CargoProduct[] products)
        {
            CargoLateYieldFacts facts = Fill(new CargoLateYieldFacts(), lease.Intent, lease);
            facts.ProductsFingerprint = CargoValues.ProductsFingerprint(products); facts.CompleteSelectedYield = true;
            facts.MaterializationBoundaryHeld = true; facts.NoBagWriteYet = true; return facts;
        }
        public CargoSourceLease Reserve(CargoSourceIntent intent)
        { Accept(Ledger.SourceReserve(intent, SourceFacts(intent), Now, out CargoSourceLease lease)); Assert(lease != null, "accepted source lacked an opaque lease"); return lease; }
        public CargoSourceLease Enter(CargoSourceIntent intent)
        { CargoSourceLease lease = Reserve(intent); Accept(Ledger.EnterSelection(lease, SourceFacts(lease.Intent, lease), Now)); return lease; }
        public CargoCaptureRequest Legacy(bool host, long request, long operation, long entity) => new CargoCaptureRequest
        {
            ExpeditionId = Expedition, MemberId = host ? Host : Employee, RequestId = request, OperationId = operation, BagRevision = Member(host).BagRevision,
            Source = new CargoSource { RoomId = Room, SceneEpoch = 1, EntityId = entity, LocalGeneration = 1 }, Products = new[] { Product(101, 1) }
        };
        public CargoCaptureFacts LegacyFacts(CargoCaptureRequest request)
        {
            bool host = request.MemberId == Host; CargoMemberSnapshot member = Member(host);
            return new CargoCaptureFacts
            {
                ExpeditionId = Expedition, MemberId = request.MemberId, BoundPlayerId = host ? 1 : 2, RequestId = request.RequestId,
                OperationId = request.OperationId, ProductsFingerprint = CargoValues.ProductsFingerprint(request.Products), Source = CargoValues.Copy(request.Source),
                CurrentRoomId = request.Source.RoomId, BagRevision = member.BagRevision, SampledAt = Now, HostAuthority = true,
                SourceIdentityVerified = true, OrdinaryFishVerified = true, ActorPermitted = true, SourceAvailable = true,
                CapacityPolicyVerified = true, CapacityPolicy = CargoCapacityPolicy.RejectOverCapacity, CapacityRoutingVerified = true,
                NativeEntryCapabilityVerified = true, YieldVerified = true, CaptureTerminal = true, ActualBagDeltaVerified = true,
                HostBagWeightVerified = true, NativeCurrentWeight = member.Weight, NativeBagWeightBefore = member.Weight,
                NativeBagWeightAfter = member.Weight + request.Products.Sum(product => product.TotalWeight), DiversionVerified = true, NoHostBagWrite = true
            };
        }
        public CargoCaptureFacts Receipt(CargoSourceLease lease) => LegacyFacts(Capture(lease).Request);
        public CargoCaptureFacts HostWeight(double value) => new CargoCaptureFacts
        {
            ExpeditionId = Expedition, MemberId = Host, BoundPlayerId = 1, BagRevision = Member(true).BagRevision,
            SampledAt = Now, HostAuthority = true, HostBagWeightVerified = true, NativeCurrentWeight = value
        };
        public CargoStorageFacts Storage(CargoSourceLease lease, int index) => new CargoStorageFacts
        {
            ExpeditionId = Expedition, ReturnId = Ledger.Snapshot.ReturnId, MemberId = lease.Intent.MemberId, CaptureId = lease.CaptureId, ProductIndex = index,
            ProductFingerprint = CargoValues.ProductFingerprint(Capture(lease).Request.Products[index]), SampledAt = Now, HostAuthority = true,
            EmployeeStorageAdapterVerified = true, NativeEntryCapabilityVerified = true, StorageDeltaVerified = true, SaveConfirmed = true
        };
    }
}
