using System;
using DaveCoop.Core.Cargo;
using DaveCoop.Core.Session;

namespace DaveCoop.Core.Crew
{
    // The virtual employee bag is owned by the ledger. This immutable reading
    // excludes reservations and unknown operations from confirmed load. It is
    // not the host's native bag, a receipt or native source authorization.
    public sealed class CrewCargoLoad
    {
        public string ExpeditionId { get; }
        public string RoomId { get; }
        public string MemberId { get; }
        public long ActorRevision { get; }
        public long SceneEpoch { get; }
        public string SceneKey { get; }
        public long BagRevision { get; }
        public double CapacityKg { get; }
        public double ConfirmedWeightKg { get; }
        public double ReservedWeightKg { get; }
        public int UnresolvedCaptureCount { get; }
        public float MovementFactor { get; }
        public bool IsOverweight => ConfirmedWeightKg > CapacityKg;

        internal CrewCargoLoad(CrewCargoBinding binding, HostCrewControl actor, CargoMemberLoadReading member)
        {
            ExpeditionId = binding.ExpeditionId; RoomId = binding.RoomId; MemberId = binding.EmployeeMemberId;
            ActorRevision = actor.ActorRevision; SceneEpoch = actor.SceneEpoch; SceneKey = actor.SceneKey;
            BagRevision = member.BagRevision; CapacityKg = member.Capacity; ConfirmedWeightKg = member.Weight;
            ReservedWeightKg = member.ReservedWeight; UnresolvedCaptureCount = member.UnresolvedCaptureCount;
            // A host-approved Mod rule for this employee, not a reconstruction
            // of the original player's overload/debuff parameters.
            MovementFactor = member.Weight <= member.Capacity ? 1f :
                (float)Math.Max(CrewCargoBinding.MinimumMovementFactor, member.Capacity / member.Weight);
        }
    }

    // One expedition/member binding, independent of a replaceable actor or
    // scene. The production caller validates its real peer/body before and
    // after use. This class never builds capability facts or calls native code.
    public sealed class CrewCargoBinding
    {
        public const float MinimumMovementFactor = 0.25f;
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private HostCrewControl _actor;
        public ExpeditionCargoLedger Ledger { get; }
        public string ExpeditionId { get; }
        public string RoomId { get; }
        public string HostMemberId { get; }
        public string EmployeeMemberId { get; }
        public HostCrewControl BoundActor => _actor;
        public long HighestActorRevision { get; private set; }
        public bool Disconnected { get; private set; }
        public string Status { get; private set; } = "WaitingForActor";

        public CrewCargoBinding(ExpeditionCargoLedger ledger, string roomId, string employeeMemberId)
        {
            Ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            RoomId = CargoValues.GuidKey(roomId); EmployeeMemberId = CargoValues.GuidKey(employeeMemberId);
            CargoLedgerSnapshot snapshot = ledger.Snapshot;
            if (snapshot.SourceRoomId != RoomId || snapshot.Phase != CargoExpeditionPhase.Active)
                throw new ArgumentException("The active expedition must already own this room.");
            ExpeditionId = snapshot.ExpeditionId;
            CargoMemberSnapshot employee = FindEmployee(snapshot);
            if (employee == null || !employee.Connected) throw new ArgumentException("The expedition employee does not match.");
            foreach (CargoMemberSnapshot member in snapshot.Members)
                if (member.BagMode == CargoBagMode.HostNative) HostMemberId = member.MemberId;
        }

        public bool BindActor(HostCrewControl actor, SessionSnapshot current)
        {
            if (!CreatorThread() || Disconnected) return false;
            if (!Current(actor, current, out _)) { Status = "ActorBindingUnavailable"; return false; }
            if (ReferenceEquals(actor, _actor)) return true;
            if (actor.ActorRevision <= HighestActorRevision) { Status = "ActorRevisionReplay"; return false; }
            _actor = actor; HighestActorRevision = actor.ActorRevision; Status = "ActorBound"; return true;
        }

        public bool TryReadLoad(HostCrewControl actor, SessionSnapshot current, out CrewCargoLoad load)
        {
            load = null;
            if (!CreatorThread() || Disconnected || !ReferenceEquals(actor, _actor) ||
                !Current(actor, current, out CargoMemberLoadReading member)) return false;
            load = new CrewCargoLoad(this, actor, member); return true;
        }

        // Native consumers use this exact binding check between read steps;
        // immutable load objects are allocated only for movement/state reads.
        internal bool IsActorCurrent(HostCrewControl actor, SessionSnapshot current) =>
            CreatorThread() && !Disconnected && ReferenceEquals(actor, _actor) && Current(actor, current, out _);

        public void UnbindActor(string reason = "ActorRetired")
        {
            RequireThread(); _actor = null; Status = reason ?? "ActorRetired";
        }

        public void Disconnect(string reason = "Disconnected")
        {
            RequireThread();
            if (Disconnected) return;
            Disconnected = true; _actor = null; Ledger.SetConnected(EmployeeMemberId, false);
            Status = reason ?? "Disconnected";
            // Keep the same ledger, captures, request fences and return resources.
            // A new socket/room cannot reactivate this member binding.
        }

        private bool Current(HostCrewControl actor, SessionSnapshot current, out CargoMemberLoadReading member)
        {
            member = default;
            if (actor == null || current == null || !actor.Active || !actor.Alive || current.Role != SessionRole.Host ||
                current.Phase != SessionPhase.Ready || current.LocalPlayerId != 1 || current.RemotePlayerId != 2 ||
                !current.LocalUsesCrewActor || !current.RemoteUsesCrewActor || current.RoomId != RoomId || actor.RoomId != RoomId ||
                actor.MemberId != EmployeeMemberId || current.SceneEpoch != actor.SceneEpoch || current.SceneKey != actor.SceneKey ||
                current.CrewActorRevision != actor.ActorRevision) return false;
            return Ledger.TryReadMemberLoad(EmployeeMemberId, out member) &&
                member.ExpeditionId == ExpeditionId && member.SourceRoomId == RoomId && member.Phase == CargoExpeditionPhase.Active &&
                member.MemberId == EmployeeMemberId && member.BagMode == CargoBagMode.EmployeeVirtual &&
                member.Connected && member.Capacity == (double)actor.Profile.CapacityKg;
        }

        private CargoMemberSnapshot FindEmployee(CargoLedgerSnapshot snapshot)
        {
            foreach (CargoMemberSnapshot member in snapshot.Members)
                if (member.MemberId == EmployeeMemberId && member.BagMode == CargoBagMode.EmployeeVirtual) return member;
            return null;
        }
        private bool CreatorThread() => Environment.CurrentManagedThreadId == _thread;
        private void RequireThread()
        { if (!CreatorThread()) throw new InvalidOperationException("Cargo actor binding requires its creator thread."); }
    }
}
