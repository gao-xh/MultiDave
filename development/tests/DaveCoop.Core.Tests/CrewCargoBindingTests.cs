using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using DaveCoop.Core.Cargo;
using DaveCoop.Core.Crew;
using DaveCoop.Core.Session;

internal static class CrewCargoBindingTests
{
    internal static void ProductionRoomAndMemberBindingRejectsUnpinnedOrForeignLedger()
    {
        var f = new Fixture(hostWeight: 80, employeeCapacity: 8);
        Assert(f.Ledger.Snapshot.SourceRoomId == f.Room && f.Ledger.Snapshot.Captures.Length == 0,
            "the production room was not fixed before the first capture");
        Assert(f.Binding.TryReadLoad(f.Actor, f.Session, out CrewCargoLoad load) && load.ConfirmedWeightKg == 0 &&
            load.CapacityKg == 8 && load.MovementFactor == 1 && f.HostMember.Weight == 80,
            "the host's original weight leaked into the employee's initially empty personal bag");
        Throws<ArgumentException>(() => new CrewCargoBinding(f.Ledger, Guid.NewGuid().ToString("N"), f.Employee));
        Throws<ArgumentException>(() => new CrewCargoBinding(f.Ledger, f.Room, f.Host));
        Throws<ArgumentException>(() => new ExpeditionCargoLedger(f.Expedition, f.Setups(), "not a room"));
        var legacy = new ExpeditionCargoLedger(Guid.NewGuid().ToString("N"), f.Setups());
        Assert(legacy.Snapshot.SourceRoomId == null, "optional constructor binding changed legacy semantics");
        Throws<ArgumentException>(() => new CrewCargoBinding(legacy, f.Room, f.Employee));
        CargoCaptureRequest wrongRoom = f.Request(1, 1, 2);
        wrongRoom.Source.RoomId = Guid.NewGuid().ToString("N");
        Assert(f.Ledger.Reserve(wrongRoom, f.Facts(wrongRoom), f.Now).Reason == CargoReason.WrongIdentity &&
            f.EmployeeMember.HighestRequestId == 0, "an empty production ledger borrowed a different room namespace");
    }

    internal static void ConfirmedPersonalCargoAloneControlsMovementFactor()
    {
        var f = new Fixture(hostWeight: 80, employeeCapacity: 8);
        CargoCaptureRequest request = f.Request(1, 1, 12);
        long capture = Accept(f.Ledger.Reserve(request, f.Facts(request), f.Now));
        Accept(f.Ledger.EnterCapture(capture, f.Facts(request), f.Now));
        Assert(f.Binding.TryReadLoad(f.Actor, f.Session, out CrewCargoLoad pending) && pending.ConfirmedWeightKg == 0 &&
            pending.ReservedWeightKg == 12 && pending.UnresolvedCaptureCount == 1 && pending.MovementFactor == 1,
            "an entered unknown reservation was treated as confirmed weight");
        CargoCaptureFacts incomplete = f.Facts(request); incomplete.CaptureTerminal = false;
        Assert(f.Ledger.ConfirmCapture(capture, CargoReceiptKind.EmployeeDivertedYield, request.Products, incomplete, f.Now).Reason == CargoReason.MissingCapability,
            "reading personal load granted a terminal capture receipt");
        Accept(f.Ledger.ConfirmCapture(capture, CargoReceiptKind.EmployeeDivertedYield, request.Products, f.Facts(request), f.Now));
        Assert(f.Binding.TryReadLoad(f.Actor, f.Session, out CrewCargoLoad confirmed) && confirmed.ConfirmedWeightKg == 12 &&
            confirmed.ReservedWeightKg == 0 && confirmed.UnresolvedCaptureCount == 0 && Close(confirmed.MovementFactor, 2f / 3f) &&
            confirmed.IsOverweight && f.HostMember.Weight == 80, "independent confirmed load did not apply the approved Mod rule");
        long revision = confirmed.BagRevision;
        Assert(f.Ledger.ConfirmCapture(capture, CargoReceiptKind.EmployeeDivertedYield, request.Products, f.Facts(request), f.Now).Reason == CargoReason.Duplicate &&
            f.EmployeeMember.Weight == 12 && f.EmployeeMember.BagRevision == revision, "a duplicate receipt added personal load twice");
        f.Commit(f.Request(2, 2, 48));
        Assert(f.Binding.TryReadLoad(f.Actor, f.Session, out CrewCargoLoad heavier) && heavier.ConfirmedWeightKg == 60 &&
            heavier.MovementFactor == .25f && confirmed.ConfirmedWeightKg == 12 && pending.ConfirmedWeightKg == 0,
            "the minimum movement factor or immutable earlier readings changed");
    }

    internal static void SourceOnlyUnknownCannotBecomeWeightOrCaptureProof()
    {
        var f = new Fixture();
        CargoSourceIntent intent = f.Intent(1, 1);
        CargoSourceFacts facts = f.SourceFacts(intent);
        Accept(f.Ledger.SourceReserve(intent, facts, f.Now, out CargoSourceLease lease));
        facts = f.SourceFacts(intent); facts.OperationId = lease.OperationId; facts.IntentFingerprint = lease.IntentFingerprint;
        facts.YieldSelectionIsolationVerified = false;
        Assert(f.Ledger.EnterSelection(lease, facts, f.Now).Reason == CargoReason.MissingCapability,
            "binding an empty personal bag fabricated selection isolation");
        facts.YieldSelectionIsolationVerified = true;
        Accept(f.Ledger.EnterSelection(lease, facts, f.Now));
        CargoCaptureSnapshot capture = f.Ledger.Snapshot.Captures.Single();
        Assert(capture.Request == null && !capture.YieldBound && capture.Stage == CargoCaptureStage.EnteredUnknown &&
            f.Binding.TryReadLoad(f.Actor, f.Session, out CrewCargoLoad load) && load.ConfirmedWeightKg == 0 &&
            load.ReservedWeightKg == 0 && load.UnresolvedCaptureCount == 1 && load.MovementFactor == 1,
            "unknown products were silently normalized into an empty successful capture");
        CargoSourceIntent owned = lease.Intent; owned.MemberId = f.Host; owned.Source.EntityId = 999;
        Assert(lease.Intent.MemberId == f.Employee && lease.Intent.Source.EntityId == 1,
            "the actor binding altered the opaque selection's fixed member/source");
        Accept(f.Ledger.FreezeReturn(Guid.NewGuid().ToString("N")));
        Assert(f.Ledger.CompleteReturn().Reason == CargoReason.ReturnIncomplete &&
            !f.Binding.TryReadLoad(f.Actor, f.Session, out _), "an unresolved empty-yield operation became normal return or live load");
    }

    internal static void PauseAndSceneReplacementKeepCargoAndActorTombstones()
    {
        var f = new Fixture(); f.Commit(f.Request(1, 1, 3));
        Assert(f.Binding.TryReadLoad(f.Actor, f.Session, out CrewCargoLoad oldLoad), "initial cargo reading missing");
        f.Session.Phase = SessionPhase.WaitingForScene;
        f.Binding.UnbindActor("ScenePause"); f.Actor.Stop("ScenePause");
        Assert(!f.Binding.TryReadLoad(f.Actor, f.Session, out _) && f.EmployeeMember.Weight == 3 && f.EmployeeMember.Connected &&
            f.Binding.HighestActorRevision == 7, "pause erased the bag, membership or actor fence");
        f.Session.Phase = SessionPhase.Ready; f.Session.SceneEpoch = 4; f.Session.SceneKey = "deeper"; f.Session.CrewActorRevision = 8;
        HostCrewControl next = f.NewActor(8, 4, "deeper");
        Assert(f.Binding.BindActor(next, f.Session) && f.Binding.BindActor(next, f.Session) &&
            f.Binding.TryReadLoad(next, f.Session, out CrewCargoLoad load) && load.ConfirmedWeightKg == 3 && load.SceneEpoch == 4 &&
            oldLoad.ActorRevision == 7 && f.EmployeeMember.HighestRequestId == 1, "scene replacement created a new personal bag");
        HostCrewControl sameNumber = f.NewActor(8, 4, "deeper");
        Assert(!f.Binding.BindActor(sameNumber, f.Session) && !f.Binding.TryReadLoad(sameNumber, f.Session, out _),
            "a different control object borrowed an existing actor revision");
        f.Binding.UnbindActor();
        Assert(!f.Binding.BindActor(next, f.Session) && f.Binding.HighestActorRevision == 8,
            "retired same-number actor was revived by rebinding the old object");
        f.Session.CrewActorRevision = 9;
        Assert(f.Binding.BindActor(f.NewActor(9, 4, "deeper"), f.Session), "a genuinely newer same-room actor could not continue");
    }

    internal static void DisconnectRetainsConfirmedAndUnknownCargoWithoutRebinding()
    {
        var f = new Fixture(); f.Commit(f.Request(1, 1, 3));
        CargoCaptureRequest unknown = f.Request(2, 2, 2);
        long id = Accept(f.Ledger.Reserve(unknown, f.Facts(unknown), f.Now));
        Accept(f.Ledger.EnterCapture(id, f.Facts(unknown), f.Now));
        f.Binding.Disconnect(); f.Binding.Disconnect();
        Assert(f.Binding.Disconnected && !f.EmployeeMember.Connected && f.EmployeeMember.Weight == 3 && f.EmployeeMember.ReservedWeight == 2 &&
            f.Ledger.Snapshot.Captures.Single(item => item.CaptureId == id).Stage == CargoCaptureStage.EnteredUnknown,
            "disconnect cleared cargo or guessed a pending native call never entered");
        CargoCaptureFacts late = f.Facts(unknown); late.ActorPermitted = false; late.SourceAvailable = false;
        Accept(f.Ledger.ConfirmCapture(id, CargoReceiptKind.EmployeeDivertedYield, unknown.Products, late, f.Now));
        Assert(f.EmployeeMember.Weight == 5 && f.EmployeeMember.ReservedWeight == 0 && f.EmployeeMember.HighestRequestId == 2 &&
            !f.Binding.TryReadLoad(f.Actor, f.Session, out _), "late historical receipt restored transport authority or lost cargo");
        Accept(f.Ledger.SetConnected(f.Employee, true)); // A ledger flag is not a new peer/source binding.
        f.Session.CrewActorRevision = 8;
        Assert(!f.Binding.BindActor(f.NewActor(8), f.Session) && f.Binding.Disconnected,
            "changing Connected revived a disconnected actor binding");
        Throws<ArgumentException>(() => new CrewCargoBinding(f.Ledger, Guid.NewGuid().ToString("N"), f.Employee));
    }

    internal static void ReturningAndAbortedSnapshotsAreNotLiveLoad()
    {
        var returning = new Fixture(); returning.Commit(returning.Request(1, 1, 3));
        Accept(returning.Ledger.FreezeReturn(Guid.NewGuid().ToString("N")));
        Assert(!returning.Binding.TryReadLoad(returning.Actor, returning.Session, out _) && returning.EmployeeMember.Weight == 3 &&
            returning.Ledger.CompleteReturn().Reason == CargoReason.ReturnIncomplete,
            "returning history was used as a live bag or skipped per-item settlement");
        var emptyReturned = new Fixture(); Accept(emptyReturned.Ledger.FreezeReturn(Guid.NewGuid().ToString("N")));
        Accept(emptyReturned.Ledger.CompleteReturn());
        Assert(!emptyReturned.Binding.TryReadLoad(emptyReturned.Actor, emptyReturned.Session, out _) &&
            !emptyReturned.Ledger.Snapshot.NativeBagInventoryComplete && !emptyReturned.Ledger.Snapshot.NativeExecutionImplemented &&
            !emptyReturned.Ledger.Snapshot.CrashSafeExactlyOnce, "empty CLR return granted full native cargo/save evidence");
        Throws<ArgumentException>(() => new CrewCargoBinding(emptyReturned.Ledger, emptyReturned.Room, emptyReturned.Employee));
        var aborted = new Fixture(); aborted.Commit(aborted.Request(1, 1, 2)); Accept(aborted.Ledger.Abort());
        Assert(!aborted.Binding.TryReadLoad(aborted.Actor, aborted.Session, out _) && aborted.EmployeeMember.Weight == 2,
            "aborted history silently dropped or presented cargo as active");
    }

    internal static void FreshActorSessionAndProfileIdentityCannotBorrowPersonalBag()
    {
        var f = new Fixture(); f.Commit(f.Request(1, 1, 3));
        var mutations = new Action<SessionSnapshot>[]
        {
            s => s.Role = SessionRole.Guest, s => s.Phase = SessionPhase.WaitingForScene,
            s => s.LocalPlayerId = 2, s => s.RemotePlayerId = 1,
            s => s.LocalUsesCrewActor = false, s => s.RemoteUsesCrewActor = false,
            s => s.RoomId = Guid.NewGuid().ToString("N"), s => s.SceneEpoch++, s => s.SceneKey = "foreign",
            s => s.CrewActorRevision = 0, s => s.CrewActorRevision++
        };
        foreach (Action<SessionSnapshot> mutate in mutations)
        {
            SessionSnapshot bad = f.CurrentSession(); mutate(bad);
            Assert(!f.Binding.TryReadLoad(f.Actor, bad, out _) && !f.Binding.BindActor(f.Actor, bad), "stale/foreign session borrowed personal cargo");
        }
        f.Session.CrewActorRevision = 8;
        HostCrewControl capacityChanged = f.NewActor(8, profile: new HostCrewProfile(capacityKg: 19));
        HostCrewControl memberChanged = f.NewActor(8, member: f.Host);
        Assert(!f.Binding.BindActor(capacityChanged, f.Session) && !f.Binding.BindActor(memberChanged, f.Session) &&
            f.Binding.HighestActorRevision == 7 && f.EmployeeMember.Capacity == 20,
            "a replacement actor changed the expedition's frozen capacity/member");
        f.Session.CrewActorRevision = 7;
        Assert(f.Binding.TryReadLoad(f.Actor, f.Session, out _), "rejected replacement corrupted the live binding");
        f.Actor.ApplyDamage(f.Actor.Profile.MaxHP);
        Assert(!f.Binding.TryReadLoad(f.Actor, f.Session, out _) && !f.Binding.BindActor(f.Actor, f.Session) && f.EmployeeMember.Weight == 3,
            "a dead actor could use load or its death erased retained cargo");
    }

    internal static void CreatorThreadAndOwnedReadingsCannotMutateBinding()
    {
        var f = new Fixture(); f.Commit(f.Request(1, 1, 2));
        Assert(f.Binding.TryReadLoad(f.Actor, f.Session, out CrewCargoLoad old), "initial reading unavailable");
        Task.Run(() =>
        {
            Assert(!f.Binding.BindActor(f.Actor, f.Session) && !f.Binding.TryReadLoad(f.Actor, f.Session, out _), "another thread used an actor binding");
            Throws<InvalidOperationException>(() => f.Binding.UnbindActor());
            Throws<InvalidOperationException>(() => f.Binding.Disconnect());
        }).GetAwaiter().GetResult();
        CargoLedgerSnapshot copy = f.Ledger.Snapshot;
        copy.Members[1].Weight = 999; copy.Members[1].Capacity = 1; copy.Members[1].Connected = false;
        copy.Captures[0].Request.Products[0].TotalWeight = 999;
        f.Commit(f.Request(2, 2, 1));
        Assert(f.Binding.TryReadLoad(f.Actor, f.Session, out CrewCargoLoad current) && current.ConfirmedWeightKg == 3 &&
            current.CapacityKg == 20 && old.ConfirmedWeightKg == 2 && current.BagRevision > old.BagRevision &&
            f.Binding.HighestActorRevision == 7 && !f.Binding.Disconnected,
            "a worker or mutable snapshot changed live binding/immutable load ownership");
    }

    // Synthetic host facts exercise the real ledger's existing fences. They
    // are not native observations, terminal evidence or game execution.
    private sealed class Fixture
    {
        internal readonly string Room = Guid.NewGuid().ToString("N"), Expedition = Guid.NewGuid().ToString("N");
        internal readonly string Host = Guid.NewGuid().ToString("N"), Employee = Guid.NewGuid().ToString("N");
        internal readonly double HostWeight;
        internal readonly float Capacity;
        internal readonly ExpeditionCargoLedger Ledger;
        internal readonly CrewCargoBinding Binding;
        internal readonly HostCrewControl Actor;
        internal readonly SessionSnapshot Session;
        internal double Now = 10;
        internal Fixture(double hostWeight = 0, float employeeCapacity = 20)
        {
            HostWeight = hostWeight; Capacity = employeeCapacity;
            Ledger = new ExpeditionCargoLedger(Expedition, Setups(), Room);
            Session = CurrentSession(); Actor = NewActor(7);
            Binding = new CrewCargoBinding(Ledger, Room, Employee);
            Assert(Binding.BindActor(Actor, Session), "fixture actual control/ledger could not bind");
        }
        internal CargoMemberSetup[] Setups() => new[]
        {
            new CargoMemberSetup { MemberId = Host, BagMode = CargoBagMode.HostNative, Capacity = 20, InitialWeight = HostWeight },
            new CargoMemberSetup { MemberId = Employee, BagMode = CargoBagMode.EmployeeVirtual, Capacity = Capacity }
        };
        internal SessionSnapshot CurrentSession() => new SessionSnapshot
        {
            Role = SessionRole.Host, Phase = SessionPhase.Ready, RoomId = Room, LocalPlayerId = 1, RemotePlayerId = 2,
            LocalUsesCrewActor = true, RemoteUsesCrewActor = true, SceneEpoch = 3, SceneKey = "dive", CrewActorRevision = 7
        };
        internal HostCrewControl NewActor(long revision, long epoch = 3, string key = "dive", HostCrewProfile profile = null, string member = null)
        {
            var actor = new HostCrewControl(Room, member ?? Employee, epoch, key, revision,
                profile ?? new HostCrewProfile(capacityKg: Capacity), new Vector3(2, -8, 0));
            Assert(actor.ObserveBody(new Vector3(2, -8, 0), Vector2.Zero, true), "fixture body readback rejected"); return actor;
        }
        internal CargoMemberSnapshot EmployeeMember => Ledger.Snapshot.Members.Single(m => m.MemberId == Employee);
        internal CargoMemberSnapshot HostMember => Ledger.Snapshot.Members.Single(m => m.MemberId == Host);
        internal CargoCaptureRequest Request(long requestId, long operation, double weight) => new CargoCaptureRequest
        {
            ExpeditionId = Expedition, MemberId = Employee, RequestId = requestId, OperationId = operation,
            BagRevision = EmployeeMember.BagRevision,
            Source = new CargoSource { RoomId = Room, SceneEpoch = 3, EntityId = requestId, LocalGeneration = 1 },
            Products = new[] { new CargoProduct { ProductId = 101, Grade = 2, Count = 1, UnitWeight = weight, TotalWeight = weight } }
        };
        internal CargoCaptureFacts Facts(CargoCaptureRequest request) => new CargoCaptureFacts
        {
            ExpeditionId = Expedition, MemberId = Employee, BoundPlayerId = 2, RequestId = request.RequestId,
            OperationId = request.OperationId, ProductsFingerprint = CargoValues.ProductsFingerprint(request.Products),
            CurrentRoomId = request.Source.RoomId, Source = CargoValues.Copy(request.Source), BagRevision = EmployeeMember.BagRevision, SampledAt = Now,
            HostAuthority = true, SourceIdentityVerified = true, OrdinaryFishVerified = true, ActorPermitted = true, SourceAvailable = true,
            CapacityPolicyVerified = true, CapacityPolicy = CargoCapacityPolicy.AllowPersonalOverweight, CapacityRoutingVerified = true,
            NativeEntryCapabilityVerified = true, YieldVerified = true, CaptureTerminal = true, DiversionVerified = true, NoHostBagWrite = true
        };
        internal CargoSourceIntent Intent(long requestId, long entityId) => new CargoSourceIntent
        {
            ExpeditionId = Expedition, MemberId = Employee, RequestId = requestId, BagRevision = EmployeeMember.BagRevision,
            ActorRevision = 7, LoadoutRevision = Actor.Profile.LoadoutRevision,
            Source = new CargoSource { RoomId = Room, SceneEpoch = 3, EntityId = entityId, LocalGeneration = 1 }
        };
        internal CargoSourceFacts SourceFacts(CargoSourceIntent intent) => new CargoSourceFacts
        {
            ExpeditionId = Expedition, MemberId = Employee, BoundPlayerId = 2, RequestId = intent.RequestId,
            ActorRevision = intent.ActorRevision, LoadoutRevision = intent.LoadoutRevision,
            CurrentRoomId = Room, Source = CargoValues.Copy(intent.Source), BagRevision = EmployeeMember.BagRevision, SampledAt = Now,
            HostAuthority = true, SourceIdentityVerified = true, OrdinaryFishVerified = true, ActorPermitted = true, SourceAvailable = true,
            CapacityPolicyVerified = true, CapacityPolicy = CargoCapacityPolicy.RejectOverCapacity, CapacityRoutingVerified = true,
            NativeEntryCapabilityVerified = true, YieldSelectionIsolationVerified = true
        };
        internal long Commit(CargoCaptureRequest request)
        {
            long id = Accept(Ledger.Reserve(request, Facts(request), Now)); Accept(Ledger.EnterCapture(id, Facts(request), Now));
            Accept(Ledger.ConfirmCapture(id, CargoReceiptKind.EmployeeDivertedYield, request.Products, Facts(request), Now)); return id;
        }
    }
    private static long Accept(CargoResult result)
    { Assert(result.Accepted, "cargo transition rejected: " + result.Reason); return result.CaptureId; }
    private static bool Close(float a, float b) => Math.Abs(a - b) < .000001f;
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
