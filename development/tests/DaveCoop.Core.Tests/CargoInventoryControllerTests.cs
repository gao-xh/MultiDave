using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using DaveCoop.Core.Cargo;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.Session;
using DaveCoop.Networking;

// The actual production adapter and loopback TCP, with synthetic ledger facts.
// No game method, native inventory, save or Unity lifecycle executes here.
internal static class CargoInventoryControllerTests
{
    public static async Task IndependentBagsAndNonBagRevisionChanges()
    {
        using var pair = await Pair.Open();
        var source = new CargoInventoryController(); var remote = new CargoInventoryController();
        var ledger = NewLedger(out _, out string employee);
        source.BindRoom(pair.Host); remote.BindRoom(pair.Guest);
        Assert(!remote.AttachHostLedgerEvidence(ledger, pair.Guest), "guest claimed host evidence producer");
        Assert(source.AttachHostLedgerEvidence(ledger, pair.Host), "host ledger attachment rejected");
        await Receive(source, remote, pair, 1);
        CargoInventorySnapshot first = remote.RemoteSnapshot;
        Assert(first.Members[0].Capacity == 10 && first.Members[0].Weight == 8 &&
            first.Members[1].Capacity == 20 && first.Members[1].Weight == 0 &&
            first.LedgerObservationOnly && !first.NativeBagInventoryComplete && !source.CargoGameplayEnabled,
            "personal capacity/weight was pooled or transport granted native authority");
        long bagRevision = first.Members[1].BagRevision;
        ledger.SetConnected(employee, false);
        await Receive(source, remote, pair, 2);
        Assert(!remote.RemoteSnapshot.Members[1].Connected && remote.RemoteSnapshot.Members[1].BagRevision == bagRevision,
            "connection change was lost because it did not increment BagRevision");
        remote.RemoteSnapshot.Members[0].Capacity = 999;
        Assert(remote.RemoteSnapshot.Members[0].Capacity == 10, "remote adapter leaked mutable snapshot ownership");
        source.Update(pair.Host); source.Update(pair.Host);
        Assert(source.PublishedSnapshots == 2, "unchanged ledger generated repeated snapshots");
        pair.Host.SetLocalScene(new SceneDescriptor("next", "layout"));
        pair.Guest.SetLocalScene(new SceneDescriptor("next", "layout"));
        for (int i = 0; i < 100 && pair.Host.Snapshot.Phase != SessionPhase.Ready; i++) await Task.Delay(5);
        source.Update(pair.Host); remote.Update(pair.Guest);
        Assert(pair.Host.Snapshot.Phase == SessionPhase.Ready && remote.RemoteRevision == 2 && source.RetainedHostLedger != null,
            "scene transition cleared expedition evidence");
        pair.Guest.Dispose();
        Assert(remote.RemoteSnapshot == null && remote.RemoteRevision == 0, "closed transport exposed cached cargo before the next adapter Update");
    }

    public static async Task DisconnectRetainsConfirmedAndUnknownCargo()
    {
        using var pair = await Pair.Open();
        var source = new CargoInventoryController(); source.BindRoom(pair.Host);
        ExpeditionCargoLedger ledger = NewLedger(out string host, out string employee);
        Assert(source.AttachHostLedgerEvidence(ledger, pair.Host), "host ledger rejected");
        CargoCaptureRequest confirmed = Request(ledger, employee, pair.Host.Snapshot.RoomId, 1, 2);
        long id = Accepted(ledger.Reserve(confirmed, Facts(ledger, confirmed), 10));
        Accepted(ledger.EnterCapture(id, Facts(ledger, confirmed), 10));
        Accepted(ledger.ConfirmCapture(id, CargoReceiptKind.EmployeeDivertedYield, confirmed.Products, Facts(ledger, confirmed), 10));
        CargoCaptureRequest unknown = Request(ledger, employee, pair.Host.Snapshot.RoomId, 2, 3);
        id = Accepted(ledger.Reserve(unknown, Facts(ledger, unknown), 10));
        Accepted(ledger.EnterCapture(id, Facts(ledger, unknown), 10));
        source.Update(pair.Host);
        source.Disconnect();
        Assert(!source.HostLedgerAttached && source.HostLedgerRetained, "disconnect still presented retained evidence as an active producer");
        CargoLedgerSnapshot retained = source.RetainedHostLedger;
        Assert(retained.Captures.Length == 2 && retained.Members[1].Inventory.Length == 1 &&
            retained.Members[1].Weight == 2 && retained.Members[1].ReservedWeight == 3 && !retained.Members[1].Connected &&
            retained.Captures[1].Stage == CargoCaptureStage.EnteredUnknown && retained.Members[0].MemberId == host && retained.Members[0].Weight == 8,
            "disconnect cleared receipts, unknown fences or modified the host's bag");
        using var other = await Pair.Open();
        source.BindRoom(other.Host);
        Assert(!source.AttachHostLedgerEvidence(ledger, other.Host), "new peer inherited the old employee's expedition");
        Assert(!source.AttachHostLedgerEvidence(NewLedger(out _, out _), other.Host), "new ledger silently replaced unresolved cargo");
        source.Update(other.Host);
        Assert(other.Host.Snapshot.CargoRevision == 0 && source.RetainedHostLedger.Captures.Length == 2,
            "unproven room migration published old receipts or cleared the retained ledger");
    }

    private static ExpeditionCargoLedger NewLedger(out string host, out string employee)
    {
        host = Guid.NewGuid().ToString("N"); employee = Guid.NewGuid().ToString("N");
        return new ExpeditionCargoLedger(Guid.NewGuid().ToString("N"), new[]
        {
            new CargoMemberSetup { MemberId = host, BagMode = CargoBagMode.HostNative, Capacity = 10, InitialWeight = 8 },
            new CargoMemberSetup { MemberId = employee, BagMode = CargoBagMode.EmployeeVirtual, Capacity = 20 }
        });
    }
    private static CargoCaptureRequest Request(ExpeditionCargoLedger ledger, string member, string room, long request, double weight)
    {
        CargoLedgerSnapshot state = ledger.Snapshot;
        return new CargoCaptureRequest
        {
            ExpeditionId = state.ExpeditionId, MemberId = member, RequestId = request, OperationId = request,
            BagRevision = state.Members.Single(item => item.MemberId == member).BagRevision,
            Source = new CargoSource { RoomId = room, SceneEpoch = 1, EntityId = request, LocalGeneration = 1 },
            Products = new[] { new CargoProduct { ProductId = 101, Count = 1, Grade = 1, TotalWeight = weight } }
        };
    }
    private static CargoCaptureFacts Facts(ExpeditionCargoLedger ledger, CargoCaptureRequest request) => new CargoCaptureFacts
    {
        ExpeditionId = request.ExpeditionId, MemberId = request.MemberId, BoundPlayerId = 2,
        RequestId = request.RequestId, OperationId = request.OperationId, ProductsFingerprint = CargoValues.ProductsFingerprint(request.Products),
        CurrentRoomId = request.Source.RoomId, Source = CargoValues.Copy(request.Source),
        BagRevision = ledger.Snapshot.Members[1].BagRevision, SampledAt = 10,
        HostAuthority = true, SourceIdentityVerified = true, OrdinaryFishVerified = true, ActorPermitted = true, SourceAvailable = true,
        CapacityPolicyVerified = true, CapacityPolicy = CargoCapacityPolicy.RejectOverCapacity, CapacityRoutingVerified = true,
        NativeEntryCapabilityVerified = true, YieldVerified = true, CaptureTerminal = true, DiversionVerified = true, NoHostBagWrite = true
    };
    private static long Accepted(CargoResult result)
    { Assert(result.Accepted, "synthetic cargo fixture rejected: " + result.Reason); return result.CaptureId; }
    private static async Task Receive(CargoInventoryController source, CargoInventoryController remote, Pair pair, long revision)
    {
        for (int i = 0; i < 200; i++)
        {
            source.Update(pair.Host); remote.Update(pair.Guest);
            if (remote.RemoteRevision == revision) return;
            await Task.Delay(5);
        }
        throw new InvalidOperationException("Cargo adapter TCP snapshot did not arrive.");
    }
    private static void Assert(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private sealed class Pair : IDisposable
    {
        public SessionPeer Host, Guest;
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        public static async Task<Pair> Open()
        {
            var pair = new Pair();
            try
            {
                using var listener = new LanHost(IPAddress.Loopback, 0);
                var identity = new PeerIdentity { ModVersion = "cargo-fixture", SteamBuildId = "1", UnityVersion = "test", Name = "crew" };
                Task<SessionPeer> accepted = listener.AcceptOneAsync(identity, pair._cancel.Token);
                pair.Guest = await LanGuest.ConnectAsync("127.0.0.1", listener.Port, identity, pair._cancel.Token);
                pair.Host = await accepted;
                return pair;
            }
            catch { pair.Dispose(); throw; }
        }
        public void Dispose() { Host?.Dispose(); Guest?.Dispose(); _cancel.Cancel(); _cancel.Dispose(); }
    }
}
