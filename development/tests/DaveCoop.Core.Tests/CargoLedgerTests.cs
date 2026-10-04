using System;
using System.Globalization;
using System.Linq;
using DaveCoop.Core.Cargo;

// Synthetic host facts test CLR accounting/fences only, never native permission.
internal static class CargoLedgerTests
{
    internal static void IndependentCapacityReservationsAndNativeWeight()
    {
        var f = new Fixture(10, 8, 10);
        CargoCaptureRequest fullHost = f.Request(true, 1, 1, 1, 3);
        Reject(f.Ledger.Reserve(fullHost, f.Facts(fullHost), f.Now), CargoReason.CapacityExceeded);
        CargoCaptureRequest employee = f.Request(false, 1, 2, 2, 3);
        long employeeId = Accept(f.Ledger.Reserve(employee, f.Facts(employee), f.Now));
        CargoCaptureRequest host = f.Request(true, 2, 3, 3, 2);
        long hostId = Accept(f.Ledger.Reserve(host, f.Facts(host), f.Now));
        Assert(f.Member(true).ReservedWeight == 2 && f.Member(false).ReservedWeight == 3, "member reservations shared capacity");
        CargoCaptureFacts changedNative = f.Facts(host); changedNative.NativeCurrentWeight = 10;
        Reject(f.Ledger.EnterCapture(hostId, changedNative, f.Now), CargoReason.Conflict);
        Accept(f.Ledger.ObserveHostBagWeight(f.Host, changedNative, f.Now));
        Reject(f.Ledger.EnterCapture(hostId, f.Facts(host), f.Now), CargoReason.CapacityExceeded);
        CargoCaptureFacts notEntered = f.Facts(host); notEntered.NativeNotEntered = true;
        Accept(f.Ledger.CaptureNotEntered(hostId, notEntered, f.Now));
        Accept(f.Ledger.EnterCapture(employeeId, f.Facts(employee), f.Now));
        Accept(f.Ledger.ConfirmCapture(employeeId, CargoReceiptKind.EmployeeDivertedYield, employee.Products, f.Facts(employee), f.Now));
        Assert(f.Member(true).Weight == 10 && f.Member(false).Weight == 3 && f.Member(false).Inventory.Length == 1,
            "employee yield changed host native weight or used its full capacity");
        CargoCaptureRequest overweight = f.Request(false, 2, 4, 4, 8);
        CargoCaptureFacts ownPolicy = f.Facts(overweight); ownPolicy.CapacityPolicy = CargoCapacityPolicy.AllowPersonalOverweight;
        long overweightId = Accept(f.Ledger.Reserve(overweight, ownPolicy, f.Now));
        ownPolicy = f.Facts(overweight); ownPolicy.CapacityPolicy = CargoCapacityPolicy.AllowPersonalOverweight;
        Accept(f.Ledger.EnterCapture(overweightId, ownPolicy, f.Now));
        Accept(f.Ledger.ConfirmCapture(overweightId, CargoReceiptKind.EmployeeDivertedYield, overweight.Products, ownPolicy, f.Now));
        Assert(f.Member(false).Weight == 11 && f.Member(false).IsOverweight && !f.Member(true).IsOverweight,
            "personal overweight punished or consumed capacity of the other member");
    }

    internal static void OperationProvenanceAndAtomicProducts()
    {
        var f = new Fixture();
        CargoCaptureRequest request = f.Request(false, 1, 10, 1, 2);
        CargoCaptureFacts facts = f.Facts(request); facts.OperationId++;
        Reject(f.Ledger.Reserve(request, facts, f.Now), CargoReason.WrongIdentity);
        facts = f.Facts(request); facts.RequestId++;
        Reject(f.Ledger.Reserve(request, facts, f.Now), CargoReason.WrongIdentity);
        facts = f.Facts(request); facts.BoundPlayerId = 1;
        Reject(f.Ledger.Reserve(request, facts, f.Now), CargoReason.WrongIdentity);
        facts = f.Facts(request); facts.ProductsFingerprint = new string('0', 64);
        Reject(f.Ledger.Reserve(request, facts, f.Now), CargoReason.MissingCapability);
        Assert(f.Member(false).HighestRequestId == 0 && f.Ledger.Snapshot.Captures.Length == 0, "unbound facts consumed the member request fence");
        long captureId = Accept(f.Ledger.Reserve(request, f.Facts(request), f.Now));
        CargoCaptureRequest frozen = f.Ledger.Snapshot.Captures[0].Request;
        request.Products[0].Count = 999; request.Source.LocalGeneration = 999;
        Assert(f.Ledger.Snapshot.Captures[0].Request.Products[0].Count == 1 && f.Ledger.Snapshot.Captures[0].Request.Source.LocalGeneration == 1,
            "caller mutation changed the reserved source or products");
        facts = f.Facts(frozen); facts.Source.LocalGeneration++;
        Reject(f.Ledger.EnterCapture(captureId, facts, f.Now), CargoReason.WrongIdentity);
        facts = f.Facts(frozen); facts.SampledAt = f.Now - 1;
        Reject(f.Ledger.EnterCapture(captureId, facts, f.Now), CargoReason.StaleFacts);
        Accept(f.Ledger.EnterCapture(captureId, f.Facts(frozen), f.Now));
        CargoProduct[] replaced = CargoValues.CopyProducts(frozen.Products); replaced[0].Count++;
        Reject(f.Ledger.ConfirmCapture(captureId, CargoReceiptKind.EmployeeDivertedYield, replaced, f.Facts(frozen), f.Now), CargoReason.Conflict);
        replaced = CargoValues.CopyProducts(frozen.Products); replaced[0].TotalWeight = double.NaN;
        Reject(f.Ledger.ConfirmCapture(captureId, CargoReceiptKind.EmployeeDivertedYield, replaced, f.Facts(frozen), f.Now), CargoReason.InvalidInput);
        foreach (int missing in new[] { 0, 1, 2, 3, 4 })
        {
            facts = f.Facts(frozen);
            if (missing == 0) facts.NoHostBagWrite = false;
            if (missing == 1) facts.DiversionVerified = false;
            if (missing == 2) facts.CapacityRoutingVerified = false;
            if (missing == 3) facts.CaptureTerminal = false;
            if (missing == 4) facts.YieldVerified = false;
            Reject(f.Ledger.ConfirmCapture(captureId, CargoReceiptKind.EmployeeDivertedYield, frozen.Products, facts, f.Now), CargoReason.MissingCapability);
        }
        facts = f.Facts(frozen); facts.OperationId++;
        Reject(f.Ledger.ConfirmCapture(captureId, CargoReceiptKind.EmployeeDivertedYield, frozen.Products, facts, f.Now), CargoReason.WrongIdentity);
        Assert(f.Member(false).Weight == 0 && f.Member(false).ReservedWeight == 2 && f.Ledger.Snapshot.Captures[0].Stage == CargoCaptureStage.EnteredUnknown,
            "failed receipt partially committed inventory or released the source");
        Accept(f.Ledger.ConfirmCapture(captureId, CargoReceiptKind.EmployeeDivertedYield, frozen.Products, f.Facts(frozen), f.Now));
        Reject(f.Ledger.ConfirmCapture(captureId, CargoReceiptKind.EmployeeDivertedYield, frozen.Products, f.Facts(frozen), f.Now), CargoReason.Duplicate);
        frozen.Products[0].Count = 999;
        Assert(f.Member(false).Inventory[0].Product.Count == 1 && f.Member(false).Weight == 2, "receipt or snapshot ownership leaked into cargo");
    }

    internal static void SourceFenceReplayCancellationAndQuota()
    {
        var f = new Fixture();
        CargoCaptureRequest original = f.Request(false, 1, 1, 1, 1);
        long entered = Accept(f.Ledger.Reserve(original, f.Facts(original), f.Now));
        Accept(f.Ledger.EnterCapture(entered, f.Facts(original), f.Now));
        CargoCaptureRequest competing = f.Request(true, 1, 2, 1, 1);
        Reject(f.Ledger.Reserve(competing, f.Facts(competing), f.Now), CargoReason.SourceBusy);
        Reject(f.Ledger.Reserve(original, f.Facts(original), f.Now), CargoReason.Duplicate);
        CargoCaptureRequest changed = CargoValues.Copy(original); changed.Products[0].Grade++;
        Reject(f.Ledger.Reserve(changed, f.Facts(changed), f.Now), CargoReason.Conflict);
        CargoCaptureRequest second = f.Request(false, 2, 3, 1, 1);
        Reject(f.Ledger.Reserve(second, f.Facts(second), f.Now), CargoReason.SourceBusy);
        Reject(f.Ledger.Reserve(second, f.Facts(second), f.Now), CargoReason.Replay);
        CargoCaptureFacts proof = f.Facts(original); proof.NativeNotEntered = true;
        Reject(f.Ledger.CaptureNotEntered(entered, proof, f.Now), CargoReason.InvalidStage);
        CargoCaptureRequest unentered = f.Request(false, 3, 4, 2, 1);
        long reserved = Accept(f.Ledger.Reserve(unentered, f.Facts(unentered), f.Now));
        Reject(f.Ledger.CaptureNotEntered(reserved, f.Facts(unentered), f.Now), CargoReason.NotEnteredProofRequired);
        proof = f.Facts(unentered); proof.NativeNotEntered = true;
        Accept(f.Ledger.CaptureNotEntered(reserved, proof, f.Now));
        CargoCaptureRequest releasedSource = f.Request(true, 2, 5, 2, 1);
        Accept(f.Ledger.Reserve(releasedSource, f.Facts(releasedSource), f.Now));
        CargoCaptureRequest reusedOperation = f.Request(false, 4, 4, 3, 1);
        Reject(f.Ledger.Reserve(reusedOperation, f.Facts(reusedOperation), f.Now), CargoReason.Conflict);
        Assert(f.Ledger.Snapshot.Captures[0].Stage == CargoCaptureStage.EnteredUnknown && f.Member(false).ReservedWeight == 1,
            "a cancellation/replay released entered unknown cargo");

        var bounded = new Fixture();
        for (int i = 1; i <= CargoValues.MaxCaptures; i++)
        {
            CargoCaptureRequest request = bounded.Request(false, i, i, i, 1);
            long id = Accept(bounded.Ledger.Reserve(request, bounded.Facts(request), bounded.Now));
            proof = bounded.Facts(request); proof.NativeNotEntered = true;
            Accept(bounded.Ledger.CaptureNotEntered(id, proof, bounded.Now));
        }
        CargoCaptureRequest overflow = bounded.Request(false, 257, 257, 257, 1);
        Reject(bounded.Ledger.Reserve(overflow, bounded.Facts(overflow), bounded.Now), CargoReason.QuotaExceeded);
        CargoCaptureRequest old = bounded.Ledger.Snapshot.Captures[127].Request;
        Reject(bounded.Ledger.Reserve(old, bounded.Facts(old), bounded.Now), CargoReason.Duplicate);
        Reject(bounded.Ledger.Reserve(overflow, bounded.Facts(overflow), bounded.Now), CargoReason.Replay);
        Assert(bounded.Ledger.Snapshot.Captures.Length == 256 && bounded.Member(false).HighestRequestId == 257 && bounded.Member(false).ReservedWeight == 0,
            "quota eviction or rejected request reopened a dedup fence");
    }

    internal static void DisconnectSceneAndRoomFences()
    {
        var f = new Fixture();
        CargoCaptureRequest oldScene = f.Request(false, 1, 1, 1, 2);
        long captureId = Accept(f.Ledger.Reserve(oldScene, f.Facts(oldScene), f.Now));
        Accept(f.Ledger.EnterCapture(captureId, f.Facts(oldScene), f.Now));
        Accept(f.Ledger.SetConnected(f.Employee, false));
        f.Now = 100;
        CargoCaptureRequest epochBypass = f.Request(true, 1, 90, 1, 2); epochBypass.Source.SceneEpoch = 2;
        Reject(f.Ledger.Reserve(epochBypass, f.Facts(epochBypass), f.Now), CargoReason.SourceBusy);
        Assert(f.Ledger.Snapshot.Captures.Length == 1 && f.Member(false).ReservedWeight == 2,
            "a same-room epoch change reassigned an unresolved physical source to the other member");
        CargoCaptureFacts late = f.Facts(oldScene); late.ActorPermitted = false; late.SourceAvailable = false;
        Accept(f.Ledger.ConfirmCapture(captureId, CargoReceiptKind.EmployeeDivertedYield, oldScene.Products, late, f.Now));
        Assert(!f.Member(false).Connected && f.Member(false).Weight == 2 && f.Member(false).Inventory.Length == 1,
            "disconnect/live-target expiry destroyed an entered operation's late receipt");
        CargoCaptureRequest offline = f.Request(false, 2, 2, 2, 1);
        Reject(f.Ledger.Reserve(offline, f.Facts(offline), f.Now), CargoReason.Disconnected);
        Accept(f.Ledger.SetConnected(f.Employee, true));
        CargoCaptureRequest nextScene = f.Request(false, 3, 3, 3, 4); nextScene.Source.SceneEpoch = 2;
        long nextId = Accept(f.Ledger.Reserve(nextScene, f.Facts(nextScene), f.Now));
        CargoCaptureFacts changedGeneration = f.Facts(nextScene); changedGeneration.Source.LocalGeneration++;
        Reject(f.Ledger.EnterCapture(nextId, changedGeneration, f.Now), CargoReason.WrongIdentity);
        CargoCaptureFacts wrongRoomBeforeEntry = f.Facts(nextScene); wrongRoomBeforeEntry.CurrentRoomId = Guid.NewGuid().ToString("N");
        Reject(f.Ledger.EnterCapture(nextId, wrongRoomBeforeEntry, f.Now), CargoReason.WrongIdentity);
        Accept(f.Ledger.EnterCapture(nextId, f.Facts(nextScene), f.Now));
        CargoCaptureRequest anotherEpoch = f.Request(false, 4, 4, 3, 4); anotherEpoch.Source.SceneEpoch = 3;
        Reject(f.Ledger.Reserve(anotherEpoch, f.Facts(anotherEpoch), f.Now), CargoReason.SourceBusy);
        Accept(f.Ledger.SetConnected(f.Employee, false));
        CargoCaptureRequest reconnected = f.Request(false, 999, 999, 3, 4);
        reconnected.Source.RoomId = Guid.NewGuid().ToString("N"); reconnected.Source.SceneEpoch = 2;
        Accept(f.Ledger.SetConnected(f.Employee, true));
        Reject(f.Ledger.Reserve(reconnected, f.Facts(reconnected), f.Now), CargoReason.WrongIdentity);
        CargoCaptureFacts newRoomEntry = f.Facts(nextScene); newRoomEntry.CurrentRoomId = reconnected.Source.RoomId;
        Reject(f.Ledger.EnterCapture(nextId, newRoomEntry, f.Now), CargoReason.InvalidStage);
        Assert(f.Ledger.Snapshot.SourceRoomId == f.Room && f.Ledger.Snapshot.Captures.Length == 2 &&
            f.Ledger.Snapshot.Captures[1].Stage == CargoCaptureStage.EnteredUnknown && f.Member(false).ReservedWeight == 4 && f.Member(false).Weight == 2,
            "new room/scene reset cargo or bypassed an old unknown native source");

        var unentered = new Fixture();
        CargoCaptureRequest oldReservation = unentered.Request(false, 1, 1, 1, 1);
        long reservedId = Accept(unentered.Ledger.Reserve(oldReservation, unentered.Facts(oldReservation), unentered.Now));
        CargoCaptureRequest newEpoch = unentered.Request(false, 2, 2, 1, 1); newEpoch.Source.SceneEpoch = 2;
        Reject(unentered.Ledger.Reserve(newEpoch, unentered.Facts(newEpoch), unentered.Now), CargoReason.SourceBusy);
        CargoCaptureFacts noEntry = unentered.Facts(oldReservation); noEntry.NativeNotEntered = true;
        Accept(unentered.Ledger.CaptureNotEntered(reservedId, noEntry, unentered.Now));
        newEpoch = unentered.Request(false, 3, 3, 1, 1); newEpoch.Source.SceneEpoch = 2;
        Accept(unentered.Ledger.Reserve(newEpoch, unentered.Facts(newEpoch), unentered.Now));
    }

    internal static void HostReceiptUsesNativeTotalAndCapabilities()
    {
        var unchanged = new Fixture(20, 5, 20);
        CargoCaptureRequest unreserved = unchanged.Request(true, 1, 1, 1, 1);
        CargoCaptureFacts oldSameTimestamp = unchanged.Facts(unreserved); oldSameTimestamp.NativeCurrentWeight = 4;
        CargoCaptureFacts unchangedNewSample = unchanged.Facts(unreserved);
        Reject(unchanged.Ledger.ObserveHostBagWeight(unchanged.Host, unchangedNewSample, unchanged.Now), CargoReason.Duplicate);
        Reject(unchanged.Ledger.ObserveHostBagWeight(unchanged.Host, oldSameTimestamp, unchanged.Now), CargoReason.Conflict);
        Assert(unchanged.Member(true).Weight == 5 && unchanged.Member(true).BagRevision == 2,
            "unchanged newer observation left a same-timestamp old native total usable");

        var f = new Fixture(20, 5, 20);
        CargoCaptureRequest request = f.Request(true, 1, 1, 1, 2);
        long id = Accept(f.Ledger.Reserve(request, f.Facts(request), f.Now));
        Accept(f.Ledger.EnterCapture(id, f.Facts(request), f.Now));
        CargoCaptureFacts native = f.Facts(request); native.NativeCurrentWeight = 7;
        Accept(f.Ledger.ObserveHostBagWeight(f.Host, native, f.Now));
        native = f.Facts(request); native.NativeBagWeightBefore = 5; native.NativeBagWeightAfter = 7; native.NativeCurrentWeight = 7;
        native.ActualBagDeltaVerified = false;
        Reject(f.Ledger.ConfirmCapture(id, CargoReceiptKind.HostNativeBagDelta, request.Products, native, f.Now), CargoReason.MissingCapability);
        native.ActualBagDeltaVerified = true;
        Reject(f.Ledger.ConfirmCapture(id, CargoReceiptKind.EmployeeDivertedYield, request.Products, native, f.Now), CargoReason.WrongBagMode);
        f.Now = 10.1;
        Reject(f.Ledger.ObserveHostBagWeight(f.Host, f.Facts(request), f.Now), CargoReason.Duplicate);
        native = f.Facts(request); native.NativeBagWeightBefore = 5; native.NativeBagWeightAfter = 7; native.SampledAt = 10.05;
        Reject(f.Ledger.ConfirmCapture(id, CargoReceiptKind.HostNativeBagDelta, request.Products, native, f.Now), CargoReason.StaleFacts);
        CargoCaptureFacts olderTotal = f.Facts(request); olderTotal.SampledAt = 10.05; olderTotal.NativeCurrentWeight = 6;
        Reject(f.Ledger.ObserveHostBagWeight(f.Host, olderTotal, f.Now), CargoReason.StaleFacts);
        native.SampledAt = f.Now;
        Accept(f.Ledger.ConfirmCapture(id, CargoReceiptKind.HostNativeBagDelta, request.Products, native, f.Now));
        Assert(f.Member(true).Weight == 7 && f.Member(true).ReservedWeight == 0 && f.Member(true).Inventory[0].Product.TotalWeight == 2 &&
            f.Member(false).Weight == 0 && !f.Ledger.Snapshot.NativeBagInventoryComplete,
            "host receipt mirrored native weight twice or claimed a complete native inventory");
        CargoCaptureFacts sameFrameOld = f.Facts(request);
        CargoCaptureFacts sameFrameNew = f.Facts(request); sameFrameNew.NativeCurrentWeight = 8;
        Accept(f.Ledger.ObserveHostBagWeight(f.Host, sameFrameNew, f.Now));
        Reject(f.Ledger.ObserveHostBagWeight(f.Host, sameFrameOld, f.Now), CargoReason.Conflict);
        Assert(f.Member(true).Weight == 8, "same-timestamp old bag revision replaced the new native total");
        CargoCaptureRequest full = f.Request(true, 2, 2, 2, 1);
        CargoCaptureFacts unobserved = f.Facts(full); unobserved.NativeCurrentWeight = 20;
        Reject(f.Ledger.Reserve(full, unobserved, f.Now), CargoReason.Conflict);
        Accept(f.Ledger.ObserveHostBagWeight(f.Host, unobserved, f.Now));
        CargoCaptureRequest tooHeavy = f.Request(true, 3, 3, 3, 1);
        Reject(f.Ledger.Reserve(tooHeavy, f.Facts(tooHeavy), f.Now), CargoReason.CapacityExceeded);

        var entry = new Fixture(20, 5, 20);
        CargoCaptureRequest planned = entry.Request(true, 1, 1, 1, 1);
        long plannedId = Accept(entry.Ledger.Reserve(planned, entry.Facts(planned), entry.Now));
        entry.Now = 10.1;
        Reject(entry.Ledger.ObserveHostBagWeight(entry.Host, entry.Facts(planned), entry.Now), CargoReason.Duplicate);
        CargoCaptureFacts staleEntry = entry.Facts(planned); staleEntry.SampledAt = 10.05;
        Reject(entry.Ledger.EnterCapture(plannedId, staleEntry, entry.Now), CargoReason.StaleFacts);
        CargoCaptureFacts oldRevision = entry.Facts(planned);
        CargoCaptureFacts addedElsewhere = entry.Facts(planned); addedElsewhere.NativeCurrentWeight = 6;
        Accept(entry.Ledger.ObserveHostBagWeight(entry.Host, addedElsewhere, entry.Now));
        Reject(entry.Ledger.EnterCapture(plannedId, oldRevision, entry.Now), CargoReason.Conflict);
        Accept(entry.Ledger.EnterCapture(plannedId, entry.Facts(planned), entry.Now));
        CargoCaptureFacts oldReceipt = entry.Facts(planned); oldReceipt.NativeBagWeightBefore = 6; oldReceipt.NativeBagWeightAfter = 7; oldReceipt.NativeCurrentWeight = 7;
        CargoCaptureFacts laterBag = entry.Facts(planned); laterBag.NativeCurrentWeight = 8;
        Accept(entry.Ledger.ObserveHostBagWeight(entry.Host, laterBag, entry.Now));
        Reject(entry.Ledger.ConfirmCapture(plannedId, CargoReceiptKind.HostNativeBagDelta, planned.Products, oldReceipt, entry.Now), CargoReason.Conflict);
        CargoCaptureFacts reboundReceipt = entry.Facts(planned); reboundReceipt.NativeBagWeightBefore = 6; reboundReceipt.NativeBagWeightAfter = 7;
        Accept(entry.Ledger.ConfirmCapture(plannedId, CargoReceiptKind.HostNativeBagDelta, planned.Products, reboundReceipt, entry.Now));
        Assert(entry.Member(true).Weight == 8 && entry.Member(true).ReservedWeight == 0,
            "late receipt overwrote a newer native total instead of using its fresh revision-bound weight");
    }

    internal static void ReturnPerItemLeasePartialSuccessAndHostObservation()
    {
        var f = new Fixture(20, 5, 20);
        CargoCaptureRequest host = f.Request(true, 1, 1, 1, 2);
        long hostId = f.CommitHost(host);
        CargoCaptureRequest employee = f.Request(false, 1, 2, 2, 1);
        employee.Products = new[] { Product(101, 1), Product(102, 2) };
        long employeeId = f.CommitEmployee(employee);
        string returnId = Guid.NewGuid().ToString("N");
        Accept(f.Ledger.FreezeReturn(returnId));
        Reject(f.Ledger.FreezeReturn(returnId), CargoReason.Duplicate);
        Reject(f.Ledger.FreezeReturn(Guid.NewGuid().ToString("N")), CargoReason.Conflict);
        CargoCaptureRequest afterFreeze = f.Request(false, 2, 3, 3, 1);
        Reject(f.Ledger.Reserve(afterFreeze, f.Facts(afterFreeze), f.Now), CargoReason.ReturnFrozen);
        CargoStorageFacts hostStorage = f.Storage(hostId, 0);
        Reject(f.Ledger.LeaseMaterialization(hostId, 0, hostStorage, f.Now), CargoReason.WrongBagMode);
        hostStorage.HostNativeStorageChainVerified = false;
        Reject(f.Ledger.ObserveHostStorage(hostId, 0, hostStorage, f.Now), CargoReason.MissingCapability);
        hostStorage.HostNativeStorageChainVerified = true;
        Accept(f.Ledger.ObserveHostStorage(hostId, 0, hostStorage, f.Now));
        Reject(f.Ledger.CompleteReturn(), CargoReason.ReturnIncomplete);
        Accept(f.Ledger.ConfirmStorageSave(hostId, 0, hostStorage, f.Now));
        CargoStorageFacts storage = f.Storage(employeeId, 0); storage.ProductFingerprint = "wrong";
        Reject(f.Ledger.LeaseMaterialization(employeeId, 0, storage, f.Now), CargoReason.WrongIdentity);
        storage = f.Storage(employeeId, 0);
        Accept(f.Ledger.LeaseMaterialization(employeeId, 0, storage, f.Now));
        Reject(f.Ledger.LeaseMaterialization(employeeId, 0, storage, f.Now), CargoReason.InvalidStage);
        storage.SampledAt = f.Now - 1;
        Reject(f.Ledger.EnterMaterialization(employeeId, 0, storage, f.Now), CargoReason.StaleFacts);
        storage = f.Storage(employeeId, 0); storage.NativeEntryCapabilityVerified = false;
        Reject(f.Ledger.EnterMaterialization(employeeId, 0, storage, f.Now), CargoReason.MissingCapability);
        storage.NativeEntryCapabilityVerified = true;
        Accept(f.Ledger.EnterMaterialization(employeeId, 0, storage, f.Now));
        Reject(f.Ledger.EnterMaterialization(employeeId, 0, storage, f.Now), CargoReason.InvalidStage);
        storage.StorageDeltaVerified = false;
        Reject(f.Ledger.ObserveEmployeeStorage(employeeId, 0, storage, f.Now), CargoReason.MissingCapability);
        storage.StorageDeltaVerified = true;
        Accept(f.Ledger.ObserveEmployeeStorage(employeeId, 0, storage, f.Now));
        storage.SaveConfirmed = false;
        Reject(f.Ledger.ConfirmStorageSave(employeeId, 0, storage, f.Now), CargoReason.MissingCapability);
        storage.SaveConfirmed = true;
        Accept(f.Ledger.ConfirmStorageSave(employeeId, 0, storage, f.Now));
        storage = f.Storage(employeeId, 1);
        Accept(f.Ledger.LeaseMaterialization(employeeId, 1, storage, f.Now));
        Accept(f.Ledger.EnterMaterialization(employeeId, 1, storage, f.Now));
        Reject(f.Ledger.CompleteReturn(), CargoReason.ReturnIncomplete);
        Reject(f.Ledger.LeaseMaterialization(employeeId, 0, f.Storage(employeeId, 0), f.Now), CargoReason.InvalidStage);
        CargoLedgerSnapshot snapshot = f.Ledger.Snapshot; snapshot.ReturnItems[0].Product.Count = 999;
        Assert(f.Ledger.Snapshot.ReturnItems[0].Product.Count == 1 && f.Ledger.Snapshot.ReturnItems.Count(item => item.Stage == CargoReturnStage.SaveConfirmed) == 2,
            "partial return replayed saved products or aliased its frozen product plan");
        Accept(f.Ledger.ObserveEmployeeStorage(employeeId, 1, storage, f.Now));
        Accept(f.Ledger.ConfirmStorageSave(employeeId, 1, storage, f.Now));
        Accept(f.Ledger.CompleteReturn());
        Assert(f.Ledger.Snapshot.Phase == CargoExpeditionPhase.Returned && f.Member(false).Inventory.Length == 0 &&
            !f.Ledger.Snapshot.NativeExecutionImplemented && !f.Ledger.Snapshot.CrashSafeExactlyOnce && !f.Ledger.Snapshot.NativeBagInventoryComplete,
            "tracked return completion claimed native execution, full inventory or crash-safe save transactions");
    }

    internal static void ReturnPendingCaptureAndAbortKeepUnknown()
    {
        var f = new Fixture();
        CargoCaptureRequest reserved = f.Request(false, 1, 1, 1, 1);
        long reservedId = Accept(f.Ledger.Reserve(reserved, f.Facts(reserved), f.Now));
        CargoCaptureRequest entered = f.Request(false, 2, 2, 2, 2);
        long enteredId = Accept(f.Ledger.Reserve(entered, f.Facts(entered), f.Now));
        Accept(f.Ledger.EnterCapture(enteredId, f.Facts(entered), f.Now));
        Accept(f.Ledger.FreezeReturn(Guid.NewGuid().ToString("N")));
        Reject(f.Ledger.CompleteReturn(), CargoReason.ReturnIncomplete);
        Reject(f.Ledger.EnterCapture(reservedId, f.Facts(reserved), f.Now), CargoReason.ReturnFrozen);
        CargoCaptureFacts cancellation = f.Facts(reserved); cancellation.NativeNotEntered = true;
        Accept(f.Ledger.CaptureNotEntered(reservedId, cancellation, f.Now));
        Accept(f.Ledger.SetConnected(f.Employee, false));
        CargoCaptureFacts late = f.Facts(entered); late.SourceAvailable = false; late.ActorPermitted = false;
        Accept(f.Ledger.ConfirmCapture(enteredId, CargoReceiptKind.EmployeeDivertedYield, entered.Products, late, f.Now));
        CargoStorageFacts storage = f.Storage(enteredId, 0);
        Accept(f.Ledger.LeaseMaterialization(enteredId, 0, storage, f.Now));
        Accept(f.Ledger.EnterMaterialization(enteredId, 0, storage, f.Now));
        Accept(f.Ledger.Abort());
        Reject(f.Ledger.CompleteReturn(), CargoReason.InvalidStage);
        Reject(f.Ledger.LeaseMaterialization(enteredId, 0, storage, f.Now), CargoReason.InvalidStage);
        Assert(f.Ledger.Snapshot.ReturnItems.Single(item => item.CaptureId == reservedId).Stage == CargoReturnStage.NotRequired &&
            f.Ledger.Snapshot.ReturnItems.Single(item => item.CaptureId == enteredId).Stage == CargoReturnStage.EnteredUnknown &&
            f.Member(false).Inventory.Length == 1, "abort deleted confirmed/offline cargo or its unknown materialization");
        Accept(f.Ledger.ObserveEmployeeStorage(enteredId, 0, storage, f.Now));
        Accept(f.Ledger.ConfirmStorageSave(enteredId, 0, storage, f.Now));
        Assert(f.Ledger.Snapshot.Phase == CargoExpeditionPhase.Aborted, "late storage evidence manufactured normal return");
        Reject(f.Ledger.CompleteReturn(), CargoReason.InvalidStage);

        var unknown = new Fixture();
        CargoCaptureRequest request = unknown.Request(false, 1, 1, 1, 1);
        long id = Accept(unknown.Ledger.Reserve(request, unknown.Facts(request), unknown.Now));
        Accept(unknown.Ledger.EnterCapture(id, unknown.Facts(request), unknown.Now));
        Accept(unknown.Ledger.Abort());
        CargoCaptureFacts neverEnteredClaim = unknown.Facts(request); neverEnteredClaim.NativeNotEntered = true;
        Reject(unknown.Ledger.CaptureNotEntered(id, neverEnteredClaim, unknown.Now), CargoReason.InvalidStage);
        Assert(unknown.Member(false).ReservedWeight == 1 && unknown.Ledger.Snapshot.Captures[0].Stage == CargoCaptureStage.EnteredUnknown,
            "abort treated an entered unknown as safe to release/retry");
        Reject(unknown.Ledger.FreezeReturn(Guid.NewGuid().ToString("N")), CargoReason.InvalidStage);
    }

    internal static void CanonicalOwnershipAndInvalidInputBounds()
    {
        var f = new Fixture(); CargoCaptureRequest request = f.Request(false, 1, 1, 1, 0);
        request.Products[0].UnitWeight = -0.0;
        string fingerprint = CargoValues.Fingerprint(request);
        CargoCaptureRequest equivalent = CargoValues.Copy(request);
        equivalent.ExpeditionId = Guid.Parse(equivalent.ExpeditionId).ToString("D").ToUpperInvariant(); equivalent.Products[0].UnitWeight = 0;
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR"); Assert(CargoValues.Fingerprint(equivalent) == fingerprint, "cargo fingerprint changed with GUID format/culture/negative zero"); }
        finally { CultureInfo.CurrentCulture = originalCulture; }
        Assert(fingerprint.StartsWith("cargo-capture-v1/") && fingerprint.Length == "cargo-capture-v1/".Length + 64, "cargo fingerprint domain/boundary changed");
        foreach (int invalid in new[] { 0, 1, 2, 3, 4, 5 })
        {
            CargoCaptureRequest broken = CargoValues.Copy(request);
            if (invalid == 0) broken.Products = new[] { Product(1, 1), new CargoProduct { ProductId = 2, Count = 1, TotalWeight = double.NaN } };
            if (invalid == 1) broken.Products[0].UnitWeight = double.PositiveInfinity;
            if (invalid == 2) broken.Products[0].Count = 0;
            if (invalid == 3) broken.Products = new CargoProduct[CargoValues.MaxProductsPerCapture + 1];
            if (invalid == 4) broken.Source.LocalGeneration = 0;
            if (invalid == 5) broken.OperationId = 0;
            Reject(f.Ledger.Reserve(broken, null, f.Now), CargoReason.InvalidInput);
        }
        CargoCaptureFacts malformedFacts = f.Facts(request); malformedFacts.CurrentRoomId = "bad room";
        Reject(f.Ledger.Reserve(request, malformedFacts, f.Now), CargoReason.InvalidInput);
        Assert(f.Member(false).HighestRequestId == 0 && f.Member(false).ReservedWeight == 0 && f.Ledger.Snapshot.SourceRoomId == null,
            "invalid batch/facts partially consumed inventory, room binding or request high water");
        long id = Accept(f.Ledger.Reserve(request, f.Facts(request), f.Now));
        CargoLedgerSnapshot snapshot = f.Ledger.Snapshot;
        snapshot.Captures[0].Request.Source.RoomId = Guid.NewGuid().ToString("N"); snapshot.Captures[0].Request.Products[0].Count = 999;
        snapshot.Members[1].Capacity = 999; snapshot.Members[1].HighestRequestId = 0;
        Assert(f.Ledger.Snapshot.Captures[0].CaptureId == id && f.Ledger.Snapshot.Captures[0].Request.Products[0].Count == 1 &&
            f.Ledger.Snapshot.Captures[0].Request.Source.RoomId == f.Room && f.Member(false).Capacity == 20 && f.Member(false).HighestRequestId == 1,
            "snapshot leaked mutable source, product or member ownership");
        Throws(() => new ExpeditionCargoLedger(f.Expedition, new[]
        {
            new CargoMemberSetup { MemberId = f.Host, BagMode = CargoBagMode.HostNative, Capacity = 1 },
            new CargoMemberSetup { MemberId = f.Host, BagMode = CargoBagMode.EmployeeVirtual, Capacity = 1 }
        }));
        Throws(() => new ExpeditionCargoLedger(f.Expedition, new[]
        {
            new CargoMemberSetup { MemberId = f.Host, BagMode = CargoBagMode.HostNative, Capacity = 1 },
            new CargoMemberSetup { MemberId = f.Employee, BagMode = CargoBagMode.EmployeeVirtual, Capacity = 1, InitialWeight = 1 }
        }));
    }

    private static CargoProduct Product(int id, double weight) => new CargoProduct { ProductId = id, Grade = 1, Count = 1, UnitWeight = weight, TotalWeight = weight };
    private static long Accept(CargoResult result)
    { Assert(result.Accepted, "cargo transition rejected: " + result.Reason); return result.CaptureId; }
    private static void Reject(CargoResult result, CargoReason reason)
    { Assert(!result.Accepted && result.Reason == reason, "expected cargo rejection " + reason + ", got " + result.Reason); }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Throws(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Expected invalid cargo constructor."); }

    private sealed class Fixture
    {
        public readonly string Expedition = Guid.NewGuid().ToString("N");
        public readonly string Host = Guid.NewGuid().ToString("N");
        public readonly string Employee = Guid.NewGuid().ToString("N");
        public readonly string Room = Guid.NewGuid().ToString("N");
        public readonly ExpeditionCargoLedger Ledger;
        public double Now = 10;
        public Fixture(double hostCapacity = 20, double hostWeight = 0, double employeeCapacity = 20)
        {
            Ledger = new ExpeditionCargoLedger(Expedition, new[]
            {
                new CargoMemberSetup { MemberId = Host, BagMode = CargoBagMode.HostNative, Capacity = hostCapacity, InitialWeight = hostWeight },
                new CargoMemberSetup { MemberId = Employee, BagMode = CargoBagMode.EmployeeVirtual, Capacity = employeeCapacity }
            });
        }
        public CargoMemberSnapshot Member(bool host) => Ledger.Snapshot.Members.Single(member => member.MemberId == (host ? Host : Employee));
        public CargoCaptureRequest Request(bool host, long requestId, long operationId, long entityId, double weight) => new CargoCaptureRequest
        {
            ExpeditionId = Expedition, MemberId = host ? Host : Employee, RequestId = requestId, OperationId = operationId,
            BagRevision = Member(host).BagRevision,
            Source = new CargoSource { RoomId = Room, SceneEpoch = 1, EntityId = entityId, LocalGeneration = 1 }, Products = new[] { Product(101, weight) }
        };
        public CargoCaptureFacts Facts(CargoCaptureRequest request)
        {
            bool host = request.MemberId == Host; CargoMemberSnapshot member = Member(host);
            return new CargoCaptureFacts
            {
                ExpeditionId = Expedition, MemberId = request.MemberId, BoundPlayerId = host ? 1 : 2,
                RequestId = request.RequestId, OperationId = request.OperationId, ProductsFingerprint = CargoValues.ProductsFingerprint(request.Products),
                CurrentRoomId = request.Source.RoomId, Source = CargoValues.Copy(request.Source), BagRevision = member.BagRevision, SampledAt = Now,
                HostAuthority = true, SourceIdentityVerified = true, OrdinaryFishVerified = true, ActorPermitted = true, SourceAvailable = true,
                CapacityPolicyVerified = true, CapacityPolicy = CargoCapacityPolicy.RejectOverCapacity, CapacityRoutingVerified = true,
                NativeEntryCapabilityVerified = true, YieldVerified = true, CaptureTerminal = true, ActualBagDeltaVerified = true,
                HostBagWeightVerified = true, NativeCurrentWeight = member.Weight, NativeBagWeightBefore = member.Weight,
                NativeBagWeightAfter = member.Weight + request.Products.Sum(product => product.TotalWeight), DiversionVerified = true, NoHostBagWrite = true
            };
        }
        public long CommitEmployee(CargoCaptureRequest request)
        {
            long id = Accept(Ledger.Reserve(request, Facts(request), Now));
            Accept(Ledger.EnterCapture(id, Facts(request), Now));
            Accept(Ledger.ConfirmCapture(id, CargoReceiptKind.EmployeeDivertedYield, request.Products, Facts(request), Now));
            return id;
        }
        public long CommitHost(CargoCaptureRequest request)
        {
            long id = Accept(Ledger.Reserve(request, Facts(request), Now));
            Accept(Ledger.EnterCapture(id, Facts(request), Now));
            CargoCaptureFacts facts = Facts(request); facts.NativeCurrentWeight = facts.NativeBagWeightAfter;
            Accept(Ledger.ConfirmCapture(id, CargoReceiptKind.HostNativeBagDelta, request.Products, facts, Now));
            return id;
        }
        public CargoStorageFacts Storage(long captureId, int productIndex)
        {
            CargoLedgerSnapshot snapshot = Ledger.Snapshot;
            CargoCaptureSnapshot capture = snapshot.Captures.Single(item => item.CaptureId == captureId);
            return new CargoStorageFacts
            {
                ExpeditionId = Expedition, ReturnId = snapshot.ReturnId, MemberId = capture.Request.MemberId, CaptureId = captureId, ProductIndex = productIndex,
                ProductFingerprint = CargoValues.ProductFingerprint(capture.Request.Products[productIndex]), SampledAt = Now, HostAuthority = true,
                EmployeeStorageAdapterVerified = true, NativeEntryCapabilityVerified = true, HostNativeStorageChainVerified = true,
                StorageDeltaVerified = true, SaveConfirmed = true
            };
        }
    }
}
