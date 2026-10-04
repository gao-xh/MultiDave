using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using DaveCoop.Core.Cargo;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.Session;
using DaveCoop.Networking;

// Production ledger, CargoInventoryController and actual loopback TCP. Facts
// are synthetic CLR fixtures: no Unity/native call, yield roll, bag or save runs.
internal static class CargoLateYieldTransportTests
{
    public static async Task UnselectedUnknownSurvivesSceneDisconnectAndReturnBarrier()
    {
        using var pair = await Pair.Open();
        await Ready(pair, "source-a", "layout-a");
        var fixture = new Fixture(pair.Host.Snapshot.RoomId);
        var source = new CargoInventoryController(); var remote = new CargoInventoryController();
        source.BindRoom(pair.Host); remote.BindRoom(pair.Guest);
        CargoSourceLease lease = fixture.EnterEmployee(pair.Host.Snapshot.SceneEpoch);
        Assert(source.AttachHostLedgerEvidence(fixture.Ledger, pair.Host), "unselected source ledger was rejected by the actual adapter");
        await Receive(source, remote, pair, 1);
        CargoInventorySnapshot unknown = remote.RemoteSnapshot;
        Assert(unknown.UnknownCaptureCount == 1 && unknown.ReservedCaptureCount == 0 && unknown.PendingReturnProductCount == 0 &&
            unknown.Members[1].Inventory.Length == 0 && unknown.Members[1].Weight == 0 && unknown.Members[1].ReservedWeight == 0,
            "unknown unselected yield became confirmed items, an invented weight or no pending operation");
        CargoCaptureSnapshot capture = fixture.Ledger.Snapshot.Captures.Single();
        Assert(capture.Request == null && !capture.YieldBound && capture.Intent.MemberId == fixture.Employee &&
            capture.Intent.GateOperationId == 700 && lease.OperationId != 700 && !lease.NativePermission,
            "unselected snapshot required products, reused Gate operation identity or granted native permission");

        await Ready(pair, "source-b", "layout-b");
        source.Update(pair.Host); remote.Update(pair.Guest);
        Assert(remote.RemoteRevision == 1 && remote.RemoteSnapshot.UnknownCaptureCount == 1 &&
            source.RetainedHostLedger.Captures.Single().Stage == CargoCaptureStage.EnteredUnknown,
            "ordinary scene change cleared the pending source or retransmitted unchanged cargo");
        CargoSourceIntent changedEpoch = fixture.Intent(fixture.Employee, 2, pair.Host.Snapshot.SceneEpoch, 1);
        CargoResult blocked = fixture.Ledger.SourceReserve(changedEpoch, fixture.SourceFacts(changedEpoch), 10, out CargoSourceLease absent);
        Assert(blocked.Reason == CargoReason.SourceBusy && absent == null && fixture.Ledger.Snapshot.Captures.Length == 1,
            "a new scene namespace bypassed the existing unknown physical-source fence");
        await Receive(source, remote, pair, 2); // rejected request still advances its member request high water
        string returnId = Guid.NewGuid().ToString("N");
        Accepted(fixture.Ledger.FreezeReturn(returnId));
        Assert(fixture.Ledger.CompleteReturn().Reason == CargoReason.ReturnIncomplete, "zero known products falsely completed an unknown-source return");
        await Receive(source, remote, pair, 3);
        Assert(remote.RemoteSnapshot.Phase == CargoExpeditionPhase.Returning && remote.RemoteSnapshot.ReturnId == returnId &&
            remote.RemoteSnapshot.UnknownCaptureCount == 1 && remote.RemoteSnapshot.PendingReturnProductCount == 0,
            "frozen return lost the unselected operation or fabricated return products");

        source.Disconnect();
        CargoLedgerSnapshot retained = source.RetainedHostLedger;
        Assert(source.HostLedgerRetained && !source.HostLedgerAttached && !retained.Members[1].Connected &&
            retained.Captures.Single().Request == null && retained.Captures.Single().Stage == CargoCaptureStage.EnteredUnknown &&
            retained.ReturnId == returnId && fixture.Ledger.CompleteReturn().Reason == CargoReason.ReturnIncomplete,
            "disconnect released source unknown, replaced the member or marked a normal return");
        pair.Host.Dispose();
        await GuestClosed(pair);
        Assert(remote.RemoteSnapshot == null, "closed transport continued exposing its old received view");
        using var other = await Pair.Open();
        source.BindRoom(other.Host);
        Assert(!source.AttachHostLedgerEvidence(fixture.Ledger, other.Host), "new peer silently adopted an old member/source room");
        source.Update(other.Host);
        Assert(other.Host.Snapshot.CargoRevision == 0 && source.RetainedHostLedger.Captures.Single().Stage == CargoCaptureStage.EnteredUnknown,
            "reconnection replayed cargo or erased the retained unknown operation");
    }

    public static async Task LateSelectedYieldPublishesOnlyEmployeeBagAndOriginalReturnBatch()
    {
        using var pair = await Pair.Open();
        var fixture = new Fixture(pair.Host.Snapshot.RoomId);
        var source = new CargoInventoryController(); var remote = new CargoInventoryController();
        source.BindRoom(pair.Host); remote.BindRoom(pair.Guest);
        CargoSourceLease lease = fixture.EnterEmployee(1);
        Assert(source.AttachHostLedgerEvidence(fixture.Ledger, pair.Host), "late-yield ledger attachment failed");
        await Receive(source, remote, pair, 1); // cargo is allowed while WaitingForScene
        Assert(pair.Host.Snapshot.Phase == SessionPhase.WaitingForScene && remote.RemoteSnapshot.UnknownCaptureCount == 1,
            "scene-independent pending cargo needed Ready or changed the scene phase");
        string returnId = Guid.NewGuid().ToString("N");
        Accepted(fixture.Ledger.FreezeReturn(returnId));
        await Receive(source, remote, pair, 2);
        long bagRevision = remote.RemoteSnapshot.Members[1].BagRevision;
        Accepted(fixture.Ledger.SetConnected(fixture.Employee, false));
        await Receive(source, remote, pair, 3);
        Assert(!remote.RemoteSnapshot.Members[1].Connected && remote.RemoteSnapshot.Members[1].BagRevision == bagRevision,
            "whole projection revision omitted a connection change without a bag revision");
        var selected = new[]
        {
            new CargoProduct { ProductId = 101, Count = 2, Grade = 1, UnitWeight = 1.5, TotalWeight = 3 },
            new CargoProduct { ProductId = 102, Count = 1, Grade = 2, UnitWeight = 2, TotalWeight = 2 }
        };
        Accepted(fixture.Ledger.LateSeal(lease, selected, fixture.LateFacts(lease, selected), 10));
        selected[0].Count = 999; selected[0].TotalWeight = 999; // caller cannot mutate the frozen selected yield
        CargoCaptureSnapshot sealedCapture = fixture.Ledger.Snapshot.Captures.Single();
        Assert(sealedCapture.YieldBound && sealedCapture.Stage == CargoCaptureStage.EnteredUnknown &&
            sealedCapture.Request.OperationId == lease.OperationId && sealedCapture.Request.Products[0].Count == 2 &&
            fixture.Ledger.Snapshot.ReturnItems.Length == 2 && fixture.Ledger.Snapshot.ReturnId == returnId,
            "late sealing committed a capture, leaked products or created another frozen return batch");
        await Receive(source, remote, pair, 4);
        CargoInventorySnapshot held = remote.RemoteSnapshot;
        Assert(held.UnknownCaptureCount == 1 && held.PendingReturnProductCount == 2 && held.Members[1].ReservedWeight == 5 &&
            held.Members[1].Weight == 0 && held.Members[1].Inventory.Length == 0 && held.Members[0].Weight == 8,
            "selected unknown products became confirmed cargo or consumed the host's independent capacity");
        Accepted(fixture.Ledger.ConfirmCapture(lease.CaptureId, CargoReceiptKind.EmployeeDivertedYield,
            sealedCapture.Request.Products, fixture.ReceiptFacts(sealedCapture.Request), 10));
        await Receive(source, remote, pair, 5);
        CargoInventorySnapshot confirmed = remote.RemoteSnapshot;
        Assert(confirmed.UnknownCaptureCount == 0 && confirmed.PendingReturnProductCount == 2 && confirmed.ReturnId == returnId &&
            confirmed.Members[1].Weight == 5 && confirmed.Members[1].ReservedWeight == 0 && !confirmed.Members[1].Connected &&
            confirmed.Members[1].Inventory.Length == 2 && confirmed.Members[1].Inventory[0].Product.Count == 2 &&
            confirmed.Members[1].Inventory[0].Product.TotalWeight == 3 && confirmed.Members[1].Inventory[1].Product.TotalWeight == 2 &&
            confirmed.Members[0].Capacity == 10 && confirmed.Members[0].Weight == 8 && confirmed.Members[0].Inventory.Length == 0 &&
            confirmed.Members[1].Capacity == 20 && confirmed.LedgerObservationOnly && !confirmed.NativeExecutionImplemented &&
            !source.CargoGameplayEnabled && !confirmed.NativeBagInventoryComplete,
            "receipt duplicated host weight, pooled bags, revived the employee or granted gameplay/native inventory proof");
        Assert(fixture.Ledger.CompleteReturn().Reason == CargoReason.ReturnIncomplete, "tracked capture skipped its two pending storage/save confirmations");
        CargoResult duplicate = fixture.Ledger.ConfirmCapture(lease.CaptureId, CargoReceiptKind.EmployeeDivertedYield,
            sealedCapture.Request.Products, fixture.ReceiptFacts(sealedCapture.Request), 10);
        Assert(duplicate.Reason == CargoReason.Duplicate, "same receipt was committed again");
        source.Update(pair.Host); remote.Update(pair.Guest);
        Assert(source.PublishedSnapshots == 5 && remote.RemoteRevision == 5 && fixture.Ledger.Snapshot.Members[1].Weight == 5,
            "duplicate receipt republished or doubled employee cargo");
        confirmed.Members[1].Inventory[0].Product.Count = 123;
        Assert(remote.RemoteSnapshot.Members[1].Inventory[0].Product.Count == 2, "TCP adapter returned a mutable alias of its current snapshot");
    }

    private sealed class Fixture
    {
        public readonly ExpeditionCargoLedger Ledger;
        public readonly string Employee, Host, Room;
        public Fixture(string room)
        {
            Room = room; Host = Guid.NewGuid().ToString("N"); Employee = Guid.NewGuid().ToString("N");
            Ledger = new ExpeditionCargoLedger(Guid.NewGuid().ToString("N"), new[]
            {
                new CargoMemberSetup { MemberId = Host, BagMode = CargoBagMode.HostNative, Capacity = 10, InitialWeight = 8 },
                new CargoMemberSetup { MemberId = Employee, BagMode = CargoBagMode.EmployeeVirtual, Capacity = 20 }
            });
        }
        public CargoSourceIntent Intent(string member, long request, long epoch, long entity) => new CargoSourceIntent
        {
            ExpeditionId = Ledger.Snapshot.ExpeditionId, MemberId = member, RequestId = request,
            BagRevision = Ledger.Snapshot.Members.Single(item => item.MemberId == member).BagRevision,
            Source = new CargoSource { RoomId = Room, SceneEpoch = epoch, EntityId = entity, LocalGeneration = 1 },
            ActorRevision = 1, LoadoutRevision = 1, GateOperationId = 700
        };
        public CargoSourceLease EnterEmployee(long epoch)
        {
            CargoSourceIntent intent = Intent(Employee, 1, epoch, 1);
            Accepted(Ledger.SourceReserve(intent, SourceFacts(intent), 10, out CargoSourceLease lease));
            Accepted(Ledger.EnterSelection(lease, SourceFacts(lease.Intent, lease), 10));
            return lease;
        }
        public CargoSourceFacts SourceFacts(CargoSourceIntent intent, CargoSourceLease lease = null) => new CargoSourceFacts
        {
            ExpeditionId = intent.ExpeditionId, MemberId = intent.MemberId, RequestId = intent.RequestId,
            BoundPlayerId = intent.MemberId == Host ? 1 : 2, OperationId = lease?.OperationId ?? 0,
            IntentFingerprint = lease?.IntentFingerprint, CurrentRoomId = Room, Source = CargoValues.Copy(intent.Source),
            BagRevision = Ledger.Snapshot.Members.Single(item => item.MemberId == intent.MemberId).BagRevision,
            ActorRevision = intent.ActorRevision, LoadoutRevision = intent.LoadoutRevision, SampledAt = 10,
            HostAuthority = true, SourceIdentityVerified = true, OrdinaryFishVerified = true, ActorPermitted = true,
            SourceAvailable = true, CapacityPolicyVerified = true, CapacityPolicy = CargoCapacityPolicy.RejectOverCapacity,
            CapacityRoutingVerified = true, NativeEntryCapabilityVerified = true, YieldSelectionIsolationVerified = true,
            HostBagWeightVerified = intent.MemberId == Host, NativeCurrentWeight = intent.MemberId == Host ? 8 : 0
        };
        public CargoLateYieldFacts LateFacts(CargoSourceLease lease, CargoProduct[] products) => new CargoLateYieldFacts
        {
            ExpeditionId = lease.Intent.ExpeditionId, MemberId = Employee, RequestId = lease.Intent.RequestId,
            BoundPlayerId = 2, OperationId = lease.OperationId, IntentFingerprint = lease.IntentFingerprint,
            CurrentRoomId = Room, Source = CargoValues.Copy(lease.Intent.Source), BagRevision = Ledger.Snapshot.Members[1].BagRevision,
            ActorRevision = lease.Intent.ActorRevision, LoadoutRevision = lease.Intent.LoadoutRevision, SampledAt = 10,
            HostAuthority = true, SourceIdentityVerified = true, OrdinaryFishVerified = true,
            ActorPermitted = false, SourceAvailable = false, // late evidence does not revive dispatch or the disconnected actor
            CapacityPolicyVerified = true, CapacityPolicy = CargoCapacityPolicy.RejectOverCapacity, CapacityRoutingVerified = true,
            YieldSelectionIsolationVerified = true, ProductsFingerprint = CargoValues.ProductsFingerprint(products),
            CompleteSelectedYield = true, MaterializationBoundaryHeld = true, NoBagWriteYet = true
        };
        public CargoCaptureFacts ReceiptFacts(CargoCaptureRequest request) => new CargoCaptureFacts
        {
            ExpeditionId = request.ExpeditionId, MemberId = request.MemberId, BoundPlayerId = 2,
            RequestId = request.RequestId, OperationId = request.OperationId, ProductsFingerprint = CargoValues.ProductsFingerprint(request.Products),
            CurrentRoomId = Room, Source = CargoValues.Copy(request.Source), BagRevision = Ledger.Snapshot.Members[1].BagRevision,
            SampledAt = 10, HostAuthority = true, SourceIdentityVerified = true, OrdinaryFishVerified = true,
            CapacityPolicyVerified = true, CapacityPolicy = CargoCapacityPolicy.RejectOverCapacity, CapacityRoutingVerified = true,
            YieldVerified = true, CaptureTerminal = true, DiversionVerified = true, NoHostBagWrite = true
        };
    }
    private static void Accepted(CargoResult result)
    { Assert(result.Accepted, "synthetic late-yield fixture rejected: " + result.Reason); }
    private static async Task Receive(CargoInventoryController source, CargoInventoryController remote, Pair pair, long revision)
    {
        for (int i = 0; i < 600; i++)
        {
            source.Update(pair.Host); remote.Update(pair.Guest);
            if (remote.RemoteRevision == revision) return;
            await Task.Delay(5);
        }
        throw new InvalidOperationException("Late-yield cargo snapshot did not arrive over TCP.");
    }
    private static async Task Ready(Pair pair, string scene, string layout)
    {
        pair.Host.SetLocalScene(new SceneDescriptor(scene, layout)); pair.Guest.SetLocalScene(new SceneDescriptor(scene, layout));
        for (int i = 0; i < 600; i++)
        {
            if (pair.Host.Snapshot.Phase == SessionPhase.Ready && pair.Guest.Snapshot.Phase == SessionPhase.Ready &&
                pair.Host.Snapshot.SceneKey == scene && pair.Guest.Snapshot.SceneKey == scene) return;
            await Task.Delay(5);
        }
        throw new InvalidOperationException("Late-yield fixture scene agreement did not complete.");
    }
    private static async Task GuestClosed(Pair pair)
    {
        for (int i = 0; i < 600 && pair.Guest.Snapshot.Phase != SessionPhase.Closed; i++) await Task.Delay(5);
        Assert(pair.Guest.Snapshot.Phase == SessionPhase.Closed, "disposed host did not close the remote cargo transport");
    }
    private static void Assert(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private sealed class Pair : IDisposable
    {
        public SessionPeer Host, Guest;
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        public static async Task<Pair> Open()
        {
            var pair = new Pair();
            try
            {
                using var listener = new LanHost(IPAddress.Loopback, 0);
                var identity = new PeerIdentity { ModVersion = "late-yield-fixture", SteamBuildId = "1", UnityVersion = "test", Name = "crew" };
                Task<SessionPeer> accepted = listener.AcceptOneAsync(identity, pair._cancel.Token);
                pair.Guest = await LanGuest.ConnectAsync("127.0.0.1", listener.Port, identity, pair._cancel.Token);
                pair.Host = await accepted; return pair;
            }
            catch { pair.Dispose(); throw; }
        }
        public void Dispose() { Host?.Dispose(); Guest?.Dispose(); _cancel.Cancel(); _cancel.Dispose(); }
    }
}