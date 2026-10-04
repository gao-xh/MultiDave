using System;
using System.Linq;
using DaveCoop.Core.Cargo;
using DaveCoop.Core.Session;

namespace DaveCoop.Networking
{
    // Main-thread, read-only projection of an externally owned expedition.
    // Attaching CLR evidence cannot authorize fishing, bag diversion or storage.
    // No game adapter currently creates/attaches an authoritative expedition.
    internal sealed class CargoInventoryController
    {
        private SessionPeer _main, _authorityPeer;
        private ExpeditionCargoLedger _ledger;
        private string _ledgerRoom, _lastContent;
        private long _revision;
        private CargoInventorySnapshot _remote;
        private SessionPeer _remotePeer;

        public long PublishedSnapshots { get; private set; }
        public long ReceivedSnapshots { get; private set; }
        public bool HostLedgerRetained => _ledger != null;
        public bool HostLedgerAttached => _ledger != null && ReferenceEquals(_main, _authorityPeer) &&
            _main?.Snapshot.Phase != SessionPhase.Closed;
        public long RemoteRevision => CurrentRemote?.Revision ?? 0;
        public int RemoteTrackedProducts => CurrentRemote?.Members.Sum(member => member.Inventory.Length) ?? 0;
        public CargoInventorySnapshot RemoteSnapshot
        {
            get { CargoInventorySnapshot current = CurrentRemote; return current == null ? null : CargoInventoryFrames.Copy(current); }
        }
        private CargoInventorySnapshot CurrentRemote
        {
            get
            {
                if (_remote == null || _remotePeer == null) return null;
                SessionSnapshot state = _remotePeer.Snapshot;
                if (state.Phase == SessionPhase.Closed || !state.CargoInventoryCurrent ||
                    state.CargoGeneration != _remote.Generation || state.CargoRevision != _remote.Revision)
                { _remote = null; return null; }
                return _remote;
            }
        }
        public CargoLedgerSnapshot RetainedHostLedger => _ledger?.Snapshot;
        public bool CargoGameplayEnabled => false;

        public void BindRoom(SessionPeer main)
        {
            if (!ReferenceEquals(_main, main)) Disconnect();
            _main = main;
        }

        // The native lifecycle/yield bridge must separately establish authority.
        // One exact peer owns this evidence source. A newly connected player 2,
        // a new RoomId, or another ledger does not inherit a retained expedition.
        public bool AttachHostLedgerEvidence(ExpeditionCargoLedger ledger, SessionPeer main)
        {
            if (ledger == null || main == null || !ReferenceEquals(_main, main)) return false;
            SessionSnapshot state = main.Snapshot;
            if (state.Role != SessionRole.Host || state.Phase == SessionPhase.Closed) return false;
            if (_ledger != null) return ReferenceEquals(_ledger, ledger) && ReferenceEquals(_authorityPeer, main);
            try { CargoInventoryFrames.FromLedger(ledger.Snapshot, 1, 1, state.RoomId); }
            catch (ArgumentException) { return false; }
            _ledger = ledger; _authorityPeer = main; _ledgerRoom = state.RoomId;
            return true;
        }

        public void Update(SessionPeer main, SessionPeer loopback = null)
        {
            if (main == null || !ReferenceEquals(main, _main)) return;
            SessionSnapshot state = main.Snapshot;
            if (state.Phase == SessionPhase.Closed) { Disconnect(); return; }
            if (state.Role == SessionRole.Host && _ledger != null &&
                ReferenceEquals(main, _authorityPeer) && state.RoomId == _ledgerRoom)
            {
                // Deep-copy the whole projection before publication. An independent
                // room revision covers connection/phase changes without BagRevision.
                if (_revision == long.MaxValue) throw new InvalidOperationException("Cargo evidence revision exhausted.");
                CargoInventorySnapshot next = CargoInventoryFrames.FromLedger(_ledger.Snapshot, 1, _revision + 1, _ledgerRoom);
                string content = CargoInventoryFrames.ContentFingerprint(next);
                if (content != _lastContent)
                {
                    _revision++;
                    if (main.PublishCargoInventory(next)) { _lastContent = content; PublishedSnapshots++; }
                }
            }
            SessionPeer receiver = state.Role == SessionRole.Guest ? main : loopback;
            if (!ReferenceEquals(receiver, _remotePeer)) { _remote = null; _remotePeer = receiver; }
            if (receiver == null) return;
            SessionSnapshot receiving = receiver.Snapshot;
            if (receiving.Phase == SessionPhase.Closed || !receiving.CargoInventoryCurrent ||
                (_remote != null && (receiving.CargoGeneration != _remote.Generation || receiving.CargoRevision != _remote.Revision)))
                _remote = null;
            if (receiver.TryTakeRemoteCargoInventory(out CargoInventorySnapshot complete))
            {
                _remote = CargoInventoryFrames.Copy(complete); ReceivedSnapshots++;
            }
            // Recheck after mailbox consumption: the receive loop may have
            // retracted a revision between Snapshot and TryTake's two locks.
            _ = CurrentRemote;
        }

        public void Disconnect()
        {
            _remote = null; _remotePeer = null;
            if (_ledger != null && ReferenceEquals(_main, _authorityPeer))
            {
                string employee = _ledger.Snapshot.Members.Single(member => member.BagMode == CargoBagMode.EmployeeVirtual).MemberId;
                _ledger.SetConnected(employee, false);
            }
            // Keep confirmed items, reservations, unknown outcomes and return state.
            // Rebinding transport alone cannot resume the old member's authority.
            _main = null;
        }
    }
}
