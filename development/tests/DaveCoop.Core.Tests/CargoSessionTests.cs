using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using DaveCoop.Core;
using DaveCoop.Core.Actions;
using DaveCoop.Core.Cargo;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.Session;
using DaveCoop.Core.World;

internal static class CargoSessionTests
{
    internal static void CargoCodecRolesAndRoomIdentity()
    {
        var pair = new Pair(); CargoInventorySnapshot source = Inventory(pair.Room, 33);
        WirePacket packet = Packet(pair.Room, CargoInventoryFrames.Split(source)[0]);
        WirePacket decoded = PacketCodec.Decode(PacketCodec.Encode(packet));
        Assert(new PeerIdentity().ProtocolVersion == 11 && (int)PacketKind.CargoInventorySlice == 80 &&
            decoded.CargoInventory.EntryCount == 33 && decoded.CargoInventory.SourceRoomId == pair.Room,
            "cargo wire contract or protocol default was lost");
        Throws<ProtocolException>(() => pair.Guest.PublishCargoInventory(source, 0.1));
        Throws<ProtocolException>(() => pair.Host.TryTakeRemoteCargoInventory(out _));
        Throws<ProtocolException>(() => pair.Host.Receive(decoded, 0.1));
        decoded.RoomId = Guid.NewGuid().ToString("N");
        Throws<ProtocolException>(() => PacketCodec.Encode(decoded));
        decoded.CargoInventory.SourceRoomId = decoded.RoomId;
        Throws<ProtocolException>(() => pair.Guest.Receive(decoded, 0.1));
        packet.Hello = Identity("extra");
        Throws<ProtocolException>(() => PacketCodec.Encode(packet));
        packet.Hello = null; packet.CargoInventory.Entries[0].Product.TotalWeight = double.NaN;
        Throws<ProtocolException>(() => PacketCodec.Encode(packet));
        source.Members[1].MemberId = source.Members[0].MemberId;
        Throws<ProtocolException>(() => pair.Host.PublishCargoInventory(source, 0.1));

        ExpeditionCargoLedger ledger = EmptyLedger();
        CargoLedgerSnapshot ledgerSnapshot = ledger.Snapshot;
        Assert(ledgerSnapshot.SourceRoomId == null &&
            CargoInventoryFrames.FromLedger(ledgerSnapshot, 1, 1, pair.Room).SourceRoomId == pair.Room,
            "an unbound ledger could not be observed in the explicitly supplied room");
        ledgerSnapshot.SourceRoomId = Guid.NewGuid().ToString("N");
        Throws<ArgumentException>(() => CargoInventoryFrames.FromLedger(ledgerSnapshot, 1, 1, pair.Room));
    }

    internal static void CargoAtomicOwnedSnapshotsAndMailboxRetraction()
    {
        var pair = new Pair(); CargoInventorySnapshot source = Inventory(pair.Room, 65);
        Assert(pair.Host.PublishCargoInventory(source, 0.1), "host did not publish cargo while WaitingForScene");
        source.Members[0].Inventory[0].Product.ProductId = 9999;
        source.Members[0].Capacity = 9999;
        pair.TakeCargo(0.1);
        Assert(pair.Guest.Snapshot.CargoPending && !pair.Guest.Snapshot.CargoInventoryCurrent &&
            pair.Guest.Snapshot.CargoRevision == 1 && !pair.Guest.TryTakeRemoteCargoInventory(out _),
            "first page exposed a partial inventory");
        pair.Pump(0.1);
        Assert(pair.Guest.TryTakeRemoteCargoInventory(out CargoInventorySnapshot received) &&
            Items(received) == 65 && received.Members[0].Capacity == 10 && received.Members[0].Inventory[0].Product.ProductId == 101,
            "publisher mutation changed the owned cargo pages");
        Assert(received.LedgerObservationOnly && !received.NativeBagInventoryComplete && !received.NativeExecutionImplemented &&
            !received.CrashSafeExactlyOnce && pair.Guest.Snapshot.Phase == SessionPhase.WaitingForScene && pair.Guest.Snapshot.SceneEpoch == 0,
            "cargo observations granted readiness or native authority");
        received.Members[0].Inventory[0].Product.Count = 500;
        pair.Host.PublishCargoInventory(Inventory(pair.Room, 97, 2), 0.2); pair.Pump(0.2);
        // Leave revision 2 unread: slice zero of 3 must revoke that mailbox.
        pair.Host.PublishCargoInventory(Inventory(pair.Room, 33, 3), 0.3); pair.TakeCargo(0.3);
        Assert(!pair.Guest.TryTakeRemoteCargoInventory(out _) && !pair.Guest.Snapshot.CargoInventoryCurrent &&
            pair.Guest.Snapshot.CargoPending && pair.Guest.Snapshot.CargoRevision == 3,
            "new batch retained the prior unread complete mailbox");
        pair.Pump(0.3);
        Assert(pair.Guest.TryTakeRemoteCargoInventory(out received) && received.Revision == 3 && Items(received) == 33 &&
            received.Members[0].Inventory[0].Product.Count == 1 && pair.Guest.Snapshot.CargoInventoryCurrent,
            "atomic replacement retained a consumer mutation or mixed pages");
        foreach (CargoInventorySlice slice in CargoInventoryFrames.Split(Inventory(pair.Room, 33, 3)))
            pair.Guest.Receive(Packet(pair.Room, slice), 0.4);
        Assert(!pair.Guest.TryTakeRemoteCargoInventory(out _), "identical completed replay republished a snapshot");
    }

    internal static void CargoSlowConsumerFinishesStartedBatchAndCoalescesLatest()
    {
        var pair = new Pair(); pair.Host.PublishCargoInventory(Inventory(pair.Room, 2048), 0.1);
        WirePacket first = pair.TakeCargo(0.1);
        Assert(first.CargoInventory.Count == 64 && first.CargoInventory.Index == 0, "maximum inventory was not 64 bounded pages");
        pair.Host.PublishCargoInventory(Inventory(pair.Room, 96, 2), 0.2);
        pair.Host.PublishCargoInventory(Inventory(pair.Room, 33, 3), 0.3);
        for (int index = 1; index < 64; index++)
        {
            WirePacket page = pair.TakeCargo(0.3);
            Assert(page.CargoInventory.Revision == 1 && page.CargoInventory.Index == index,
                "continuous publication canceled or reordered the started batch");
        }
        Assert(pair.Guest.TryTakeRemoteCargoInventory(out CargoInventorySnapshot completed) && completed.Revision == 1 && Items(completed) == 2048,
            "slow consumer never obtained a complete first batch");
        first = pair.TakeCargo(0.3);
        Assert(first.CargoInventory.Revision == 3 && first.CargoInventory.Index == 0,
            "next-latest retained an intermediate revision");
        pair.Pump(0.3);
        Assert(pair.Guest.TryTakeRemoteCargoInventory(out completed) && completed.Revision == 3 && Items(completed) == 33 &&
            !pair.Host.Snapshot.CargoPending && !pair.Guest.Snapshot.CargoPending,
            "coalesced batch failed to complete");
        pair.Host.PublishCargoInventory(Inventory(pair.Room, 65, 4), 0.4);
        pair.Host.PublishCargoInventory(Inventory(pair.Room, 1, 5), 0.5); pair.Pump(0.5);
        Assert(pair.Guest.TryTakeRemoteCargoInventory(out completed) && completed.Revision == 5 && Items(completed) == 1,
            "an unstarted batch was not replaced by the newest complete candidate");
    }

    internal static void CargoRevisionExpeditionAndLifecycleFences()
    {
        var pair = new Pair(); var source = Inventory(pair.Room, 1);
        pair.Host.PublishCargoInventory(source, 0.1); pair.Pump(0.1);
        Assert(!pair.Host.PublishCargoInventory(source, 0.2), "identical source revision was republished");
        source.Members[0].Weight++;
        Throws<ProtocolException>(() => pair.Host.PublishCargoInventory(source, 0.2));
        source = Inventory(pair.Room, 1, 2); source.Members[0].BagRevision = 2; source.Members[0].HighestRequestId = 10;
        pair.Host.PublishCargoInventory(source, 0.3);
        var regression = Inventory(pair.Room, 1, 3);
        Throws<ProtocolException>(() => pair.Host.PublishCargoInventory(regression, 0.3));
        regression.Members[0].BagRevision = 2; regression.Members[0].HighestRequestId = 10;
        regression.Members[1].MemberId = Guid.NewGuid().ToString("N");
        Throws<ProtocolException>(() => pair.Host.PublishCargoInventory(regression, 0.3));
        source = Inventory(pair.Room, 1, 3); source.Members[0].BagRevision = 2; source.Members[0].HighestRequestId = 10;
        source.Phase = CargoExpeditionPhase.Returning; source.ReturnId = Guid.NewGuid().ToString("N");
        pair.Host.PublishCargoInventory(source, 0.4);
        regression = CargoInventoryFrames.Copy(source); regression.Revision = 4; regression.ReturnId = Guid.NewGuid().ToString("N");
        Throws<ProtocolException>(() => pair.Host.PublishCargoInventory(regression, 0.4));
        regression.ReturnId = null; regression.Phase = CargoExpeditionPhase.Active;
        Throws<ProtocolException>(() => pair.Host.PublishCargoInventory(regression, 0.4));
        string secondExpedition = Guid.NewGuid().ToString("N");
        var next = Inventory(pair.Room, 1, 4, 2, secondExpedition); pair.Host.PublishCargoInventory(next, 0.5); pair.Pump(0.5);
        Assert(pair.Guest.Snapshot.CargoGeneration == 2 && pair.Guest.Snapshot.CargoRevision == 4 &&
            pair.Guest.Snapshot.CargoExpeditionId == secondExpedition, "new expedition reset room revision high water");
        Throws<ProtocolException>(() => pair.Host.PublishCargoInventory(Inventory(pair.Room, 1, 5, 3), 0.6));
        Throws<ProtocolException>(() => pair.Host.PublishCargoInventory(Inventory(pair.Room, 1, 4, 3, Guid.NewGuid().ToString("N")), 0.6));
        Throws<ProtocolException>(() => pair.Host.PublishCargoInventory(Inventory(pair.Room, 1, 5, 2, Guid.NewGuid().ToString("N")), 0.6));
        Assert(!pair.Host.PublishCargoInventory(Inventory(pair.Room, 1), 0.6), "old cargo generation replayed");

        var malformed = new Pair(); malformed.Host.PublishCargoInventory(Inventory(malformed.Room, 65), 0.1);
        WirePacket page = malformed.TakeCargo(0.1); page.CargoInventory.Index = 1;
        Throws<ProtocolException>(() => malformed.Guest.Receive(page, 0.2));
        Assert(!malformed.Guest.Snapshot.CargoInventoryCurrent && !malformed.Guest.TryTakeRemoteCargoInventory(out _),
            "out-of-order/conflicting page retained a usable mailbox");
        var quota = new Pair();
        for (int i = 1; i <= CargoInventoryFrames.MaxExpeditions; i++)
            Assert(quota.Host.PublishCargoInventory(Inventory(quota.Room, 0, i, i, Guid.NewGuid().ToString("N")), i), "expedition fence quota rejected within bound");
        Throws<ProtocolException>(() => quota.Host.PublishCargoInventory(Inventory(quota.Room, 0, 65, 65, Guid.NewGuid().ToString("N")), 65));
        Assert(quota.Host.Snapshot.CargoGeneration == 64, "quota failure evicted identity fences or advanced state");
    }

    internal static void CargoSceneRetentionAndCloseDoesNotTouchLedger()
    {
        var pair = new Pair(); pair.Host.PublishCargoInventory(Inventory(pair.Room, 65), 0.1); pair.TakeCargo(0.1);
        var scene = new SceneDescriptor("dive", "layout"); pair.Host.SetLocalScene(scene, 0.2); pair.Guest.SetLocalScene(scene, 0.2); pair.Pump(0.2);
        Assert(pair.Guest.TryTakeRemoteCargoInventory(out CargoInventorySnapshot received) && Items(received) == 65 &&
            pair.Host.Snapshot.Phase == SessionPhase.Ready && pair.Guest.Snapshot.Phase == SessionPhase.Ready,
            "scene frame reset canceled a started cargo batch");
        pair.Guest.SetLocalScene(null, 0.3); pair.Pump(0.3);
        Assert(pair.Guest.Snapshot.CargoInventoryCurrent && pair.Guest.Snapshot.CargoRevision == 1 &&
            pair.Host.Snapshot.CargoExpeditionId == Expedition, "ScenePause cleared room cargo transport");
        ExpeditionCargoLedger ledger = ConfirmedEmployeeLedger(pair.Room);
        CargoLedgerSnapshot before = ledger.Snapshot;
        string fingerprint = CargoInventoryFrames.ContentFingerprint(CargoInventoryFrames.FromLedger(before, 1, 1, pair.Room));
        var ledgerPair = new Pair(pair.Room);
        ledgerPair.Host.PublishCargoInventory(CargoInventoryFrames.FromLedger(before, 1, 1, pair.Room), 0.1); ledgerPair.Pump(0.1);
        ledgerPair.Host.Close("done"); ledgerPair.Guest.Close("done");
        Assert(!ledgerPair.Host.TryTakePacket(out _) && !ledgerPair.Guest.TryTakeRemoteCargoInventory(out _) &&
            ledgerPair.Host.Snapshot.CargoGeneration == 0 && ledgerPair.Guest.Snapshot.CargoRevision == 0 &&
            !ledgerPair.Guest.Snapshot.CargoInventoryCurrent && !ledgerPair.Host.PublishCargoInventory(Inventory(pair.Room, 0), 0.2),
            "close retained cargo packets, mailbox or transport high water");
        CargoLedgerSnapshot after = ledger.Snapshot;
        Assert(CargoInventoryFrames.ContentFingerprint(CargoInventoryFrames.FromLedger(after, 1, 1, pair.Room)) == fingerprint &&
            after.Captures.Length == 1 && after.Captures[0].Stage == CargoCaptureStage.Confirmed &&
            after.Members.Single(m => m.BagMode == CargoBagMode.EmployeeVirtual).Inventory.Length == 1,
            "closing a transport mutated or cleared the confirmed employee bag ledger");
    }

    internal static void CargoControlPriorityAndFiveLaneFairness()
    {
        var pair = Pair.Ready(); pair.Host.PublishCargoInventory(Inventory(pair.Room, 2048), 0.1);
        pair.Host.PublishMapRoute(Route(), 0.1); string routeHash = pair.Host.Snapshot.MapChoiceFingerprint;
        for (int i = 1; i <= 20; i++)
        {
            pair.Host.PublishMapIgpChoice(new MapIgpChoice { Generation = 1, Revision = i, RouteFingerprint = routeHash, SceneId = 1000,
                ControllerAddress = "Runtime/group-" + i, Addressable = true, SelectedPrefabName = "IGP", PrefabObjectName = "" }, 0.1);
            var request = new FishActionRequest { RequestId = i, PlayerId = 2, SceneEpoch = 1, SceneKey = "dive", Action = FishActionKind.ProbeTarget, TargetEntityId = 44 };
            pair.Host.PublishFishActionResult(new FishActionResult { RequestId = i, PlayerId = 2, SceneEpoch = 1, SceneKey = "dive", Action = request.Action,
                TargetEntityId = 44, Status = FishActionStatus.Queued, RequestFingerprint = FishActions.Fingerprint(request) }, 0.1);
        }
        pair.Host.PublishFrame(Frame(0.1), 0.1); pair.Host.PublishWorld(World(0.1), 0.1); pair.Host.Tick(0.1);
        Assert(pair.Host.TryTakePacket(out WirePacket first) && first.Kind == PacketKind.Ping, "cargo data starved heartbeat controls");
        var turns = new int[5];
        for (int i = 0; i < 100; i++)
        {
            double at = 0.2 + i * 0.01; pair.Host.PublishFrame(Frame(at), at); pair.Host.PublishWorld(World(at), at);
            Assert(pair.Host.TryTakePacket(out WirePacket packet), "populated fair lane was lost");
            if (packet.Kind == PacketKind.FishActionResult) turns[0]++;
            else if (packet.Kind == PacketKind.PlayerFrame) turns[1]++;
            else if (packet.Kind == PacketKind.WorldSlice) turns[2]++;
            else if (packet.Kind == PacketKind.MapRouteSlice || packet.Kind == PacketKind.MapIgpChoice) turns[3]++;
            else if (packet.Kind == PacketKind.CargoInventorySlice) turns[4]++;
        }
        Assert(turns.All(count => count == 20), "five populated lanes did not each obtain a bounded fair turn");
        pair.Host.SetLocalScene(null, 1.3);
        Assert(pair.Host.TryTakePacket(out first) && first.Kind == PacketKind.SceneSuspend,
            "started cargo pages delayed a scene control barrier");
    }

    internal static async Task CargoTcpCompleteSnapshotRoundTrip()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var listener = new LanHost(IPAddress.Loopback, 0);
        Task<SessionPeer> accepting = listener.AcceptOneAsync(Identity("Host"), cancellation.Token);
        using SessionPeer guest = await LanGuest.ConnectAsync("127.0.0.1", listener.Port, Identity("Guest"), cancellation.Token);
        using SessionPeer host = await accepting;
        CargoInventorySnapshot source = Inventory(listener.RoomId, 2048);
        Assert(host.PublishCargoInventory(source), "TCP did not accept WaitingForScene inventory");
        source.Members[0].Inventory[0].Product.ProductId = 9999;
        CargoInventorySnapshot received = null;
        await Until(() => guest.TryTakeRemoteCargoInventory(out received), cancellation.Token);
        Assert(Items(received) == 2048 && received.Members[0].Inventory[0].Product.ProductId == 101 && received.Generation == 1 && received.Revision == 1 &&
            received.LedgerObservationOnly && !received.NativeExecutionImplemented && !received.NativeBagInventoryComplete &&
            host.Snapshot.Phase == SessionPhase.WaitingForScene && guest.Snapshot.SceneEpoch == 0, "TCP mixed pages, aliased input or raised scene/native authority");
        received.Members[0].Inventory[0].Product.ProductId = 8888;
        host.PublishCargoInventory(Inventory(listener.RoomId, 33, 2));
        await Until(() => guest.TryTakeRemoteCargoInventory(out received) && received.Revision == 2, cancellation.Token);
        Assert(Items(received) == 33 && received.Members[0].Inventory[0].Product.ProductId == 101 && guest.Snapshot.CargoInventoryCurrent,
            "TCP subsequent complete snapshot retained a consumer mutation");
        await guest.StopAsync(); await host.Completion.WaitAsync(cancellation.Token);
        Assert(host.Snapshot.Phase == SessionPhase.Closed && host.Snapshot.CargoGeneration == 0 && !guest.TryTakeRemoteCargoInventory(out _),
            "TCP close retained cargo transport state");
    }

    internal static async Task CargoRejectsProtocolFive()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener = new LanHost(IPAddress.Loopback, 0);
        PeerIdentity legacy = Identity("Guest"); legacy.ProtocolVersion = 5;
        Task<SessionPeer> accepting = listener.AcceptOneAsync(Identity("Host"), cancellation.Token);
        bool guestRejected = false, hostRejected = false;
        try { using SessionPeer unexpected = await LanGuest.ConnectAsync("127.0.0.1", listener.Port, legacy, cancellation.Token); }
        catch (ProtocolException error) { guestRejected = error.Message.Contains("Protocol version mismatch"); }
        try { using SessionPeer unexpected = await accepting; }
        catch (ProtocolException error) { hostRejected = error.Message.Contains("Protocol version mismatch"); }
        Assert(guestRejected && hostRejected, "protocol 5 accepted an incompatible inventory payload stream");
    }

    private static readonly string Expedition = Guid.NewGuid().ToString("N");
    private static readonly string HostMember = Guid.NewGuid().ToString("N");
    private static readonly string EmployeeMember = Guid.NewGuid().ToString("N");
    private static CargoInventorySnapshot Inventory(string room, int count, long revision = 1, long generation = 1, string expedition = null)
    {
        var host = new List<CargoInventoryItem>(); var employee = new List<CargoInventoryItem>();
        for (int i = 0; i < count; i++)
        {
            long capture = i / 8 + 1;
            (capture % 2 == 1 ? host : employee).Add(new CargoInventoryItem { CaptureId = capture, ProductIndex = i % 8,
                Product = new CargoProduct { ProductId = 101, Grade = 1, Count = 1, UnitWeight = 1, TotalWeight = 1 } });
        }
        return new CargoInventorySnapshot { Generation = generation, Revision = revision, ExpeditionId = expedition ?? Expedition,
            Phase = CargoExpeditionPhase.Active, SourceRoomId = room,
            Members = new[] { Member(HostMember, CargoBagMode.HostNative, 10, host), Member(EmployeeMember, CargoBagMode.EmployeeVirtual, 20, employee) } };
    }
    private static CargoMemberSnapshot Member(string id, CargoBagMode mode, double capacity, List<CargoInventoryItem> items) => new CargoMemberSnapshot
    { MemberId = id, BagMode = mode, Capacity = capacity, Weight = items.Count, BagRevision = 1, Connected = true, Inventory = items.ToArray() };
    private static int Items(CargoInventorySnapshot snapshot) => snapshot.Members.Sum(member => member.Inventory.Length);
    private static WirePacket Packet(string room, CargoInventorySlice slice) => new WirePacket
    { Kind = PacketKind.CargoInventorySlice, RoomId = room, Sequence = 1, CargoInventory = CargoInventoryFrames.Copy(slice) };
    private static PeerIdentity Identity(string name) => new PeerIdentity
    { ModVersion = "cargo-session-fixture", SteamBuildId = "25315876", UnityVersion = "6000.0.52f1", Name = name };
    private static async Task Until(Func<bool> condition, CancellationToken token)
    { while (!condition()) await Task.Delay(5, token); }
    private static void Assert(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static ExpeditionCargoLedger EmptyLedger() => new ExpeditionCargoLedger(Expedition, new[]
    { new CargoMemberSetup { MemberId = HostMember, BagMode = CargoBagMode.HostNative, Capacity = 10, InitialWeight = 3 },
      new CargoMemberSetup { MemberId = EmployeeMember, BagMode = CargoBagMode.EmployeeVirtual, Capacity = 20 } });
    private static ExpeditionCargoLedger ConfirmedEmployeeLedger(string room)
    {
        ExpeditionCargoLedger ledger = EmptyLedger();
        var request = new CargoCaptureRequest { ExpeditionId = Expedition, MemberId = EmployeeMember, RequestId = 1, OperationId = 1, BagRevision = 1,
            Source = new CargoSource { RoomId = room, SceneEpoch = 1, EntityId = 1, LocalGeneration = 1 },
            Products = new[] { new CargoProduct { ProductId = 101, Grade = 1, Count = 1, TotalWeight = 1 } } };
        // Synthetic host-local fixture facts exercise CLR transitions only.
        var facts = new CargoCaptureFacts { ExpeditionId = Expedition, MemberId = EmployeeMember, BoundPlayerId = 2,
            RequestId = 1, OperationId = 1, BagRevision = 1, ProductsFingerprint = CargoValues.ProductsFingerprint(request.Products),
            CurrentRoomId = room, Source = CargoValues.Copy(request.Source), SampledAt = 1, HostAuthority = true,
            SourceIdentityVerified = true, OrdinaryFishVerified = true, ActorPermitted = true, SourceAvailable = true,
            CapacityPolicyVerified = true, CapacityPolicy = CargoCapacityPolicy.RejectOverCapacity, CapacityRoutingVerified = true,
            NativeEntryCapabilityVerified = true, YieldVerified = true, CaptureTerminal = true, DiversionVerified = true, NoHostBagWrite = true };
        CargoResult reserved = ledger.Reserve(request, facts, 1); Assert(reserved.Accepted, "ledger fixture reserve rejected");
        facts.BagRevision = ledger.Snapshot.Members.Single(member => member.MemberId == EmployeeMember).BagRevision;
        Assert(ledger.EnterCapture(reserved.CaptureId, facts, 1).Accepted, "ledger fixture entry rejected");
        Assert(ledger.ConfirmCapture(reserved.CaptureId, CargoReceiptKind.EmployeeDivertedYield, request.Products, facts, 1).Accepted,
            "ledger fixture confirmation rejected");
        return ledger;
    }
    private static PlayerFrame Frame(double time) => new PlayerFrame
    { SceneKey = "dive", SampleTime = time, Root = PoseAt(0) };
    private static Pose PoseAt(int x) => new Pose { Position = new Vector3(x, 0, 0), Rotation = Quaternion.Identity, Scale = Vector3.One };
    private static WorldSnapshot World(double time)
    {
        var entities = new EntityState[65];
        for (int i = 0; i < entities.Length; i++) entities[i] = new EntityState { Id = i + 1, Kind = EntityKind.Fish, DataTid = 2010007, Hp = 10, MaxHp = 10, Root = PoseAt(i) };
        return new WorldSnapshot { SceneEpoch = 1, SceneKey = "dive", Revision = 1, SampleTime = time, Entities = entities };
    }
    private static MapRouteSelection Route()
    {
        var scenes = new MapRouteScene[17];
        for (int i = 0; i < scenes.Length; i++) scenes[i] = new MapRouteScene { SceneId = 1000 + i, SceneName = "dive-" + i, Layer = 'A', MapHeight = 500,
            TopY = 0, BottomY = -500, Offset = i * -500, PreviousSceneId = i == 0 ? 0 : 999 + i, NextSceneId = i == scenes.Length - 1 ? 0 : 1001 + i };
        return new MapRouteSelection { EntrySceneId = 1000, Scenes = scenes };
    }
    private sealed class Pair
    {
        public readonly string Room;
        public readonly SessionMachine Host, Guest;
        private long _sequence;
        public Pair(string room = null)
        {
            Room = room ?? Guid.NewGuid().ToString("N");
            Host = new SessionMachine(SessionRole.Host, new HandshakeResult { RoomId = Room, LocalPlayerId = 1, RemotePlayerId = 2 }, 0);
            Guest = new SessionMachine(SessionRole.Guest, new HandshakeResult { RoomId = Room, LocalPlayerId = 2, RemotePlayerId = 1 }, 0);
        }
        public static Pair Ready()
        {
            var pair = new Pair(); var scene = new SceneDescriptor("dive", "layout");
            pair.Host.SetLocalScene(scene, 0); pair.Guest.SetLocalScene(scene, 0); pair.Pump(0); return pair;
        }
        public WirePacket TakeCargo(double now)
        {
            Assert(Host.TryTakePacket(out WirePacket packet) && packet.Kind == PacketKind.CargoInventorySlice, "expected the next cargo page");
            packet.Sequence = ++_sequence; Guest.Receive(packet, now); return packet;
        }
        public void Pump(double now)
        {
            bool progress;
            do
            {
                progress = false;
                while (Host.TryTakePacket(out WirePacket packet)) { packet.Sequence = ++_sequence; Guest.Receive(packet, now); progress = true; }
                while (Guest.TryTakePacket(out WirePacket packet)) { packet.Sequence = ++_sequence; Host.Receive(packet, now); progress = true; }
            } while (progress);
        }
    }
}
