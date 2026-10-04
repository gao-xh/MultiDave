using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DaveCoop.Core.Cargo;

// The production schema/assembler consumes synthetic CLR ledger evidence.
// No fixture invokes a native bag, writer, loot adapter or networking hook.
internal static class CargoTransportTests
{
    internal static void LedgerProjectionKeepsPendingAndHistoricalWeights()
    {
        var f = new Fixture();
        CargoInventorySnapshot empty = f.Project(1);
        Assert(empty.Members[0].Weight == 17 && empty.Members[0].Inventory.Length == 0 &&
            empty.Members[1].Weight == 0 && empty.Members[1].Capacity == 8 && empty.SourceRoomId == f.Room &&
            f.Ledger.Snapshot.SourceRoomId == null, "projection synthesized host items, shared capacity or native source binding");
        CargoCaptureRequest request = f.Request(1, 2);
        long id = Accept(f.Ledger.Reserve(request, f.Facts(request), 10));
        CargoInventorySnapshot reserved = f.Project(2);
        Assert(reserved.ReservedCaptureCount == 1 && reserved.UnknownCaptureCount == 0 &&
            reserved.Members[1].ReservedWeight == 2 && reserved.Members[1].Inventory.Length == 0,
            "reserved products were sent as confirmed cargo");
        Accept(f.Ledger.EnterCapture(id, f.Facts(request), 10));
        CargoInventorySnapshot entered = f.Project(3);
        Assert(entered.ReservedCaptureCount == 0 && entered.UnknownCaptureCount == 1 && entered.Members[1].Weight == 0,
            "an unknown native result was hidden as successful or empty settled cargo");
        Throws(() => CargoInventoryFrames.FromLedger(f.Ledger.Snapshot, 1, 3, Guid.NewGuid().ToString("N")));
        string returnId = Guid.NewGuid().ToString("N"); Accept(f.Ledger.FreezeReturn(returnId));
        CargoInventorySnapshot returning = f.Project(4);
        Assert(returning.ReturnId == returnId && returning.PendingReturnProductCount == 1 && returning.UnknownCaptureCount == 1,
            "frozen unknown product vanished from return diagnostics");
        Accept(f.Ledger.Abort());
        CargoInventorySnapshot aborted = f.Project(5);
        Assert(aborted.Phase == CargoExpeditionPhase.Aborted && aborted.ReturnId == returnId && aborted.PendingReturnProductCount == 1,
            "abort manufactured normal return or erased unknown cargo");
        var assembler = new CargoInventoryAssembler(f.Room);
        foreach (CargoInventorySnapshot value in new[] { empty, reserved, entered, returning, aborted })
        { Assert(assembler.Add(CargoInventoryFrames.Split(value)[0], out _), "legal ledger phase progression was rejected"); NoAuthority(value); }
        var activeAbort = new Fixture(); Accept(activeAbort.Ledger.Abort());
        Assert(activeAbort.Project(1).ReturnId == null, "abort before return invented a return identity");
    }

    internal static void ReturnedLedgerRetainsFloatingReservationResidue()
    {
        var f = new Fixture();
        CargoCaptureRequest one = f.Request(1, 0.1);
        long oneId = Accept(f.Ledger.Reserve(one, f.Facts(one), 10));
        CargoCaptureRequest two = f.Request(2, 0.2);
        long twoId = Accept(f.Ledger.Reserve(two, f.Facts(two), 10));
        foreach ((long id, CargoCaptureRequest request) in new[] { (oneId, one), (twoId, two) })
        {
            Accept(f.Ledger.EnterCapture(id, f.Facts(request), 10));
            Accept(f.Ledger.ConfirmCapture(id, CargoReceiptKind.EmployeeDivertedYield, request.Products, f.Facts(request), 10));
        }
        double residue = f.Member.ReservedWeight;
        Assert(residue > 0 && residue < 1e-15, "fixture did not exercise actual ledger floating reservation residue");
        Accept(f.Ledger.FreezeReturn(Guid.NewGuid().ToString("N")));
        foreach ((long id, CargoCaptureRequest request) in new[] { (oneId, one), (twoId, two) })
        {
            CargoStorageFacts facts = f.Storage(id, request);
            Accept(f.Ledger.BindEmployeeReturnPlan(CargoReturnPlanTests.FixturePlan(f.Ledger.Snapshot, id, 0), facts, 10));
            Accept(f.Ledger.LeaseMaterialization(id, 0, facts, 10));
            Accept(f.Ledger.EnterMaterialization(id, 0, facts, 10));
            Accept(f.Ledger.ObserveEmployeeStorage(id, 0, facts, 10));
            Accept(f.Ledger.ConfirmStorageSave(id, 0, facts, 10));
        }
        Accept(f.Ledger.CompleteReturn());
        CargoInventorySnapshot returned = f.Project(1);
        Assert(returned.Phase == CargoExpeditionPhase.Returned && returned.Members.All(member => member.Inventory.Length == 0) &&
            returned.Members[1].ReservedWeight == residue && returned.Members[1].Weight == 0.1 + 0.2 && returned.Members[0].Weight == 17 &&
            returned.PendingReturnProductCount == 0, "returned historical ledger weights were normalized, cleared or presented as live cargo");
        var assembler = new CargoInventoryAssembler(f.Room);
        Assert(assembler.Add(CargoInventoryFrames.Split(returned)[0], out CargoInventorySnapshot copied) &&
            copied.Members[1].ReservedWeight == residue, "legal returned residue caused projection or transport failure");
        NoAuthority(copied);
    }

    internal static void CanonicalFingerprintAndDeepCopiesOwnTheirData()
    {
        CargoInventorySnapshot value = Snapshot(40); string original = CargoInventoryFrames.Fingerprint(value);
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            value.Members = value.Members.Reverse().ToArray();
            value.Members[0].Inventory = value.Members[0].Inventory.Reverse().ToArray();
            Assert(CargoInventoryFrames.Fingerprint(value) == original, "inventory ordering or culture changed canonical identity");
        }
        finally { CultureInfo.CurrentCulture = previous; }
        CargoInventorySnapshot owned = CargoInventoryFrames.Copy(value);
        value.Members[0].Inventory[0].Product.Count = 999;
        Assert(owned.Members[1].Inventory.All(item => item.Product.Count == 1), "snapshot copy shared products or mutable member arrays");
        CargoInventorySlice[] slices = CargoInventoryFrames.Split(owned);
        slices[0].Members[0].Capacity = 123; slices[0].Entries[0].Product.Count = 777;
        Assert(slices[1].Members[0].Capacity == 20 && slices[1].Entries[0].Product.Count == 1 && owned.Members[1].Inventory[0].Product.Count == 1,
            "split pages shared metadata/products with each other or their full snapshot");
        CargoInventorySnapshot changed = CargoInventoryFrames.Copy(owned); changed.Generation++; changed.Revision++;
        Assert(CargoInventoryFrames.Fingerprint(changed) != CargoInventoryFrames.Fingerprint(owned) &&
            CargoInventoryFrames.ContentFingerprint(changed) == CargoInventoryFrames.ContentFingerprint(owned),
            "semantic identity included transport counters or full batch identity excluded them");
        changed.Members[1].Connected = false;
        Assert(CargoInventoryFrames.ContentFingerprint(changed) != CargoInventoryFrames.ContentFingerprint(owned), "Connected-only change did not advance semantic identity");
        changed = CargoInventoryFrames.Copy(owned); changed.UnknownCaptureCount = 1;
        Assert(CargoInventoryFrames.ContentFingerprint(changed) != CargoInventoryFrames.ContentFingerprint(owned), "unknown result count was absent from semantic identity");
        changed = CargoInventoryFrames.Copy(owned); changed.Phase = CargoExpeditionPhase.Returning; changed.ReturnId = Guid.NewGuid().ToString("N");
        changed.PendingReturnProductCount = 40;
        Assert(CargoInventoryFrames.ContentFingerprint(changed) != CargoInventoryFrames.ContentFingerprint(owned), "phase/return-only change did not advance semantic identity");
        NoAuthority(owned);
    }

    internal static void AtomicPagesRetractionAndOutputOwnership()
    {
        CargoInventorySnapshot value = Snapshot(64); CargoInventorySlice[] pages = CargoInventoryFrames.Split(value);
        var assembler = new CargoInventoryAssembler(value.SourceRoomId);
        Assert(!assembler.Add(pages[0], out CargoInventorySnapshot partial) && partial == null && assembler.Current == null && assembler.Pending && assembler.PendingCount == 1,
            "first page exposed a partial personal inventory");
        pages[0].Entries[0].Product.Count = 999;
        Assert(assembler.Add(pages[1], out CargoInventorySnapshot complete) && !assembler.Pending &&
            complete.Members[1].Inventory.Length == 64 && complete.Members[1].Inventory[0].Product.Count == 1,
            "caller mutation changed saved first-page data or complete atomic roster");
        complete.Members[1].Inventory[0].Product.Count = 777;
        CargoInventorySnapshot current = assembler.Current; current.Members[1].Capacity = 999;
        Assert(assembler.Current.Members[1].Inventory[0].Product.Count == 1 && assembler.Current.Members[1].Capacity == 8,
            "returned snapshot or Current getter shared internal ownership");
        CargoInventorySnapshot replacement = CargoInventoryFrames.Copy(value); replacement.Revision = 2; replacement.Members[1].Connected = false;
        CargoInventorySlice[] next = CargoInventoryFrames.Split(replacement);
        Assert(!assembler.Add(next[0], out _) && assembler.Current == null && assembler.Pending &&
            assembler.HighestRevision == 2 && !assembler.Add(next[0], out _) && assembler.PendingCount == 1,
            "new first page retained old view or identical partial replay advanced the cursor");
        Assert(assembler.Add(next[1], out _) && !assembler.Add(next[1], out _) && !assembler.Current.Members[1].Connected,
            "completed identical replay republished or ignored a membership-only change");
        NoAuthority(assembler.Current);
    }

    internal static void ReplayClearAndExpeditionFencesDoNotReset()
    {
        CargoInventorySnapshot first = Snapshot(0); var assembler = new CargoInventoryAssembler(first.SourceRoomId);
        CargoInventorySlice initial = CargoInventoryFrames.Split(first)[0]; Assert(assembler.Add(initial, out _), "initial empty roster was not committed");
        assembler.Clear(); Assert(assembler.Current == null && !assembler.Add(initial, out _) && assembler.HighestGeneration == 1 && assembler.HighestRevision == 1,
            "Clear reset room high water or resurrected the same cargo batch");
        CargoInventorySnapshot next = CargoInventoryFrames.Copy(first); next.Revision = 2;
        Assert(assembler.Add(CargoInventoryFrames.Split(next)[0], out _) && !assembler.Add(initial, out _), "new revision was rejected or old revision replaced it");
        CargoInventorySnapshot newExpedition = CargoInventoryFrames.Copy(next); newExpedition.Generation = 2; newExpedition.Revision = 3;
        newExpedition.ExpeditionId = Guid.NewGuid().ToString("N");
        Assert(assembler.Add(CargoInventoryFrames.Split(newExpedition)[0], out _), "new expedition could not advance room lifetime counters");
        CargoInventorySnapshot resurrection = CargoInventoryFrames.Copy(first); resurrection.Generation = 3; resurrection.Revision = 4;
        Throws(() => assembler.Add(CargoInventoryFrames.Split(resurrection)[0], out _));
        Assert(assembler.Faulted && assembler.Current == null, "old expedition resurrected under a new generation");
        assembler.Clear(); Throws(() => assembler.Add(CargoInventoryFrames.Split(newExpedition)[0], out _));

        var quota = new CargoInventoryAssembler(first.SourceRoomId);
        for (int index = 1; index <= CargoInventoryFrames.MaxExpeditions; index++)
        {
            CargoInventorySnapshot batch = CargoInventoryFrames.Copy(first); batch.Generation = index; batch.Revision = index;
            batch.ExpeditionId = Guid.NewGuid().ToString("N");
            Assert(quota.Add(CargoInventoryFrames.Split(batch)[0], out _), "expedition fence capacity was exhausted early"); quota.Clear();
        }
        CargoInventorySnapshot overflow = CargoInventoryFrames.Copy(first); overflow.Generation = CargoInventoryFrames.MaxExpeditions + 1;
        overflow.Revision = overflow.Generation; overflow.ExpeditionId = Guid.NewGuid().ToString("N");
        Throws(() => quota.Add(CargoInventoryFrames.Split(overflow)[0], out _));
        Assert(quota.Faulted && quota.HighestGeneration == CargoInventoryFrames.MaxExpeditions, "quota evicted old expedition identities or advanced invalid high water");
    }

    internal static void InvalidSchemaRejectsMixedMembersAndProducts()
    {
        var changes = new Action<CargoInventorySnapshot>[]
        {
            value => value.Generation = 0, value => value.Revision = 0, value => value.SourceRoomId = "not-a-room",
            value => value.ExpeditionId = Guid.Empty.ToString("N"), value => value.Phase = (CargoExpeditionPhase)99,
            value => value.Members[1].MemberId = value.Members[0].MemberId, value => value.Members[1].BagMode = CargoBagMode.HostNative,
            value => value.Members[1].MemberId = Guid.Parse(value.Members[1].MemberId).ToString("D"),
            value => value.Members[0].Capacity = 0, value => value.Members[0].Weight = double.NaN,
            value => value.Members[0].ReservedWeight = double.PositiveInfinity, value => value.Members[0].BagRevision = 0,
            value => value.Members[0].HighestRequestId = -1, value => value.Members[1].Inventory[0].Product.Count = 0,
            value => value.Members[1].Inventory[0].Product.UnitWeight = double.NaN,
            value => value.Members[1].Inventory[0].Product.TotalWeight = -1, value => value.Members[1].Inventory[0].Product.Grade = 1001,
            value => value.Members[1].Inventory[0].CaptureId = 0, value => value.Members[1].Inventory[0].ProductIndex = 8,
            value => value.Members[1].Inventory[0].ProductIndex = 1,
            value => value.ReturnId = Guid.NewGuid().ToString("N"), value => value.Phase = CargoExpeditionPhase.Returning,
            value => { value.Phase = CargoExpeditionPhase.Returned; value.ReturnId = Guid.NewGuid().ToString("N"); },
            value => value.UnknownCaptureCount = 256, value => value.PendingReturnProductCount = 1,
            value => value.Members[1].Inventory = new[] { value.Members[1].Inventory[0], value.Members[1].Inventory[0] },
            value => value.Members[0].Inventory = value.Members[1].Inventory
        };
        foreach (Action<CargoInventorySnapshot> change in changes)
        { CargoInventorySnapshot value = Snapshot(1); change(value); Throws(() => CargoInventoryFrames.Validate(value)); }
        CargoInventorySnapshot aborted = Snapshot(0); aborted.Phase = CargoExpeditionPhase.Aborted; CargoInventoryFrames.Validate(aborted);
        aborted.ReturnId = Guid.NewGuid().ToString("N"); aborted.UnknownCaptureCount = 1; aborted.PendingReturnProductCount = 1; CargoInventoryFrames.Validate(aborted);
        CargoInventorySnapshot overweight = Snapshot(1); overweight.Members[1].Weight = 9; CargoInventoryFrames.Validate(overweight);
        Assert(overweight.Members[1].IsOverweight && !overweight.Members[0].IsOverweight, "valid personal overweight was constrained to shared capacity");
    }

    internal static void ExactSliceBoundsAndCorruptAssembliesFailClosed()
    {
        foreach (int count in new[] { 0, 1, 32, 33, CargoInventoryFrames.MaxEntries })
        {
            CargoInventorySnapshot value = Snapshot(count); CargoInventorySlice[] pages = CargoInventoryFrames.Split(value);
            Assert(pages.Length == Math.Max(1, (count + 31) / 32) && pages.All(page => page.Members.All(member => member.Inventory.Length == 0)),
                "split page count or metadata-only shape was incorrect");
            var assembler = new CargoInventoryAssembler(value.SourceRoomId);
            foreach (CargoInventorySlice page in pages) { CargoInventoryFrames.Validate(page); assembler.Add(page, out _); }
            Assert(assembler.Current.Members[1].Inventory.Length == count, "maximum/empty exact roster lost entries");
        }
        Throws(() => CargoInventoryFrames.Validate(Snapshot(CargoInventoryFrames.MaxEntries + 1)));
        CargoInventorySlice[] shortLast = CargoInventoryFrames.Split(Snapshot(33)); shortLast[1].Entries = Array.Empty<CargoInventoryEntry>();
        Throws(() => CargoInventoryFrames.Validate(shortLast[1]));
        CargoInventorySlice mixed = CargoInventoryFrames.Split(Snapshot(1))[0]; mixed.Members[0].Inventory = Snapshot(1).Members[1].Inventory;
        Throws(() => CargoInventoryFrames.Validate(mixed));

        foreach (int failure in new[] { 0, 1, 2, 3, 4 })
        {
            CargoInventorySnapshot value = Snapshot(64); CargoInventorySlice[] pages = CargoInventoryFrames.Split(value);
            var assembler = new CargoInventoryAssembler(value.SourceRoomId);
            if (failure == 0) { Throws(() => assembler.Add(pages[1], out _)); }
            else
            {
                Assert(!assembler.Add(pages[0], out _), "corrupt-assembly setup was not pending");
                if (failure == 1) pages[1].Entries[0] = CargoInventoryFrames.Copy(pages[0]).Entries[0];
                if (failure == 2) pages[1].Members[0].Weight++;
                if (failure == 3) { pages[1].Entries[0].Product.Count++; }
                if (failure == 4) pages[1].SourceRoomId = Guid.NewGuid().ToString("N");
                Throws(() => assembler.Add(pages[1], out _));
            }
            Assert(assembler.Faulted && !assembler.Pending && assembler.Current == null, "corrupt page exposed partial cargo or retained stale view");
        }
        CargoInventorySnapshot empty = Snapshot(0); var replay = new CargoInventoryAssembler(empty.SourceRoomId);
        CargoInventorySlice original = CargoInventoryFrames.Split(empty)[0]; Assert(replay.Add(original, out _), "replay setup failed");
        original.Members[0].Weight++;
        Throws(() => replay.Add(original, out _)); Assert(replay.Faulted && replay.Current == null, "changed identical-revision replay retained a cargo view");
    }

    internal static void StableMemberPhaseAndRoomRevisionContinuity()
    {
        CargoInventorySnapshot original = Snapshot(0); original.Members[0].BagRevision = 3; original.Members[0].HighestRequestId = 5;
        foreach (int failure in new[] { 0, 1, 2, 3 })
        {
            var assembler = new CargoInventoryAssembler(original.SourceRoomId); Assert(assembler.Add(CargoInventoryFrames.Split(original)[0], out _), "continuity setup failed");
            CargoInventorySnapshot next = CargoInventoryFrames.Copy(original); next.Revision = 2;
            if (failure == 0) next.Members[0].MemberId = Guid.NewGuid().ToString("N");
            if (failure == 1) next.Members[0].BagRevision = 2;
            if (failure == 2) next.Members[0].HighestRequestId = 4;
            if (failure == 3) { next.Generation = 2; next.Revision = 1; next.ExpeditionId = Guid.NewGuid().ToString("N"); }
            Throws(() => assembler.Add(CargoInventoryFrames.Split(next)[0], out _)); Assert(assembler.Faulted, "identity/revision regression was accepted");
        }
        var phases = new CargoInventoryAssembler(original.SourceRoomId); Assert(phases.Add(CargoInventoryFrames.Split(original)[0], out _), "phase setup failed");
        CargoInventorySnapshot returning = CargoInventoryFrames.Copy(original); returning.Revision = 2; returning.Phase = CargoExpeditionPhase.Returning;
        returning.ReturnId = Guid.NewGuid().ToString("N"); Assert(phases.Add(CargoInventoryFrames.Split(returning)[0], out _), "legal frozen return was rejected");
        CargoInventorySnapshot regressed = CargoInventoryFrames.Copy(original); regressed.Revision = 3;
        Throws(() => phases.Add(CargoInventoryFrames.Split(regressed)[0], out _)); Assert(phases.Faulted, "frozen return reverted to active or cleared return identity");
    }

    private static CargoInventorySnapshot Snapshot(int entries)
    {
        var items = new CargoInventoryItem[entries];
        for (int index = 0; index < entries; index++) items[index] = new CargoInventoryItem
        { CaptureId = index / 8 + 1, ProductIndex = index % 8, Product = Product(0.25) };
        return new CargoInventorySnapshot
        {
            Generation = 1, Revision = 1, ExpeditionId = Guid.NewGuid().ToString("N"), SourceRoomId = Guid.NewGuid().ToString("N"), Phase = CargoExpeditionPhase.Active,
            Members = new[]
            {
                new CargoMemberSnapshot { MemberId = Guid.NewGuid().ToString("N"), BagMode = CargoBagMode.HostNative, Capacity = 20, Weight = 17, BagRevision = 1, Connected = true, Inventory = Array.Empty<CargoInventoryItem>() },
                new CargoMemberSnapshot { MemberId = Guid.NewGuid().ToString("N"), BagMode = CargoBagMode.EmployeeVirtual, Capacity = 8, Weight = entries * 0.25, BagRevision = 1, Connected = true, Inventory = items }
            }
        };
    }
    private static CargoProduct Product(double weight) => new CargoProduct { ProductId = 101, Grade = 2, Count = 1, UnitWeight = weight, TotalWeight = weight };
    private static long Accept(CargoResult result)
    { Assert(result.Accepted, "actual CLR ledger setup rejected: " + result.Reason); return result.CaptureId; }
    private static void Throws(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Expected invalid cargo transport input."); }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void NoAuthority(CargoInventorySnapshot value)
    {
        Assert(value.LedgerObservationOnly && !value.NativeBagInventoryComplete && !value.NativeExecutionImplemented && !value.CrashSafeExactlyOnce,
            "tracked-only transport granted live inventory, execution or durable exactly-once authority");
    }
    private sealed class Fixture
    {
        public readonly string Room = Guid.NewGuid().ToString("N"), Expedition = Guid.NewGuid().ToString("N"), Host = Guid.NewGuid().ToString("N"), Employee = Guid.NewGuid().ToString("N");
        public readonly ExpeditionCargoLedger Ledger;
        public Fixture() { Ledger = new ExpeditionCargoLedger(Expedition, new[]
        { new CargoMemberSetup { MemberId = Host, BagMode = CargoBagMode.HostNative, Capacity = 20, InitialWeight = 17 },
          new CargoMemberSetup { MemberId = Employee, BagMode = CargoBagMode.EmployeeVirtual, Capacity = 8 } }); }
        public CargoMemberSnapshot Member => Ledger.Snapshot.Members.Single(member => member.MemberId == Employee);
        public CargoInventorySnapshot Project(long revision) => CargoInventoryFrames.FromLedger(Ledger.Snapshot, 1, revision, Room);
        public CargoCaptureRequest Request(long requestId, double weight) => new CargoCaptureRequest
        {
            ExpeditionId = Expedition, MemberId = Employee, RequestId = requestId, OperationId = requestId, BagRevision = Member.BagRevision,
            Source = new CargoSource { RoomId = Room, SceneEpoch = 1, EntityId = requestId, LocalGeneration = 1 }, Products = new[] { Product(weight) }
        };
        public CargoCaptureFacts Facts(CargoCaptureRequest request) => new CargoCaptureFacts
        {
            ExpeditionId = Expedition, MemberId = Employee, BoundPlayerId = 2, RequestId = request.RequestId, OperationId = request.OperationId,
            ProductsFingerprint = CargoValues.ProductsFingerprint(request.Products), CurrentRoomId = Room, Source = CargoValues.Copy(request.Source),
            BagRevision = Member.BagRevision, SampledAt = 10, HostAuthority = true, SourceIdentityVerified = true, OrdinaryFishVerified = true,
            ActorPermitted = true, SourceAvailable = true, CapacityPolicyVerified = true, CapacityPolicy = CargoCapacityPolicy.RejectOverCapacity,
            CapacityRoutingVerified = true, NativeEntryCapabilityVerified = true, YieldVerified = true, CaptureTerminal = true,
            DiversionVerified = true, NoHostBagWrite = true
        };
        public CargoStorageFacts Storage(long id, CargoCaptureRequest request) => new CargoStorageFacts
        {
            ExpeditionId = Expedition, ReturnId = Ledger.Snapshot.ReturnId, MemberId = Employee, CaptureId = id, ProductIndex = 0,
            ProductFingerprint = CargoValues.ProductFingerprint(request.Products[0]), SampledAt = 10, HostAuthority = true,
            ReturnPlanFingerprint = CargoReturnPlanTests.FixturePlan(Ledger.Snapshot, id, 0).Fingerprint, ReturnConversionVerified = true,
            EmployeeStorageAdapterVerified = true, NativeEntryCapabilityVerified = true, StorageDeltaVerified = true, SaveConfirmed = true
        };
    }
}
