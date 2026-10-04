using DaveCoop.Core.Cargo;
using DaveCoop.Core.Crew;
using DaveCoop.Core.Session;
using System;
using NVector3 = System.Numerics.Vector3;

namespace DaveCoop.Networking
{
    // The production owner must still hold this exact object while dispatching.
    // A constructor call, copied input or a caller-selected target is no permit.
    internal sealed class NativeEmployeeCaptureCommand
    {
        private readonly CrewActorController _owner;
        internal SessionPeer Peer { get; }
        internal HostCrewControl Control { get; }
        internal HostEmployeeActorBody Body { get; }
        public CrewCargoBinding Binding { get; }
        public long InputSequence { get; }
        public long PacketSequence { get; }
        public double ReceivedAt { get; }
        public string RoomId { get; }
        public string MemberId => Binding.EmployeeMemberId;
        public long ActorRevision { get; }
        public long SceneEpoch { get; }
        public string SceneKey { get; }
        public long LoadoutRevision => Control.Profile.LoadoutRevision;
        public CargoCapacityPolicy CapacityPolicy { get; }
        public Guid HostDiveRootIdentity { get; }
        public long HostDiveGeneration { get; }
        public bool AdmissionEntered { get; private set; }
        public double Now => Peer.Now;

        internal NativeEmployeeCaptureCommand(CrewActorController owner, SessionPeer peer, HostCrewControl control,
            HostEmployeeActorBody body, CrewCargoBinding binding, ReceivedCrewInput input, CargoCapacityPolicy policy,
            NativeHostCargoSnapshot source)
        {
            _owner = owner; Peer = peer; Control = control; Body = body; Binding = binding;
            InputSequence = input.Frame.InputSequence; PacketSequence = input.PacketSequence; ReceivedAt = input.ReceivedAt;
            RoomId = input.RoomId; ActorRevision = input.Frame.ActorRevision;
            SceneEpoch = input.Frame.SceneEpoch; SceneKey = input.Frame.SceneKey; CapacityPolicy = policy;
            HostDiveRootIdentity = source.RootIdentity; HostDiveGeneration = source.Generation;
        }

        public bool IsCurrent() => _owner.IsCaptureCommandCurrent(this);
        public bool TryReadPosition(out NVector3 position) => _owner.TryReadCapturePosition(this, out position);
        internal bool TryAdmit()
        {
            if (AdmissionEntered || !IsCurrent()) return false;
            AdmissionEntered = true; // Fresh queued intent consumed before native preparation.
            return true;
        }
    }
}
