using System;
using System.Collections.Generic;
using System.Linq;

namespace DaveCoop.Core.Cargo
{
    // Caller-serialized, room-lifetime replay fences. Clear retracts a view,
    // never resets identities or revives the same batch. New room => new object.
    public sealed class CargoInventoryAssembler
    {
        private readonly string _room;
        private readonly HashSet<string> _expeditions = new HashSet<string>(StringComparer.Ordinal);
        private CargoInventorySlice[] _pages;
        private string[] _pageIdentities;
        private CargoInventorySlice _header;
        private string _headerIdentity;
        private CargoInventorySnapshot _current;
        private int _next;
        public long HighestGeneration { get; private set; }
        public long HighestRevision { get; private set; }
        public bool Faulted { get; private set; }
        public bool Pending => !Faulted && _pages != null && _next < _pages.Length;
        public int PendingCount => Pending ? _next : 0;
        public CargoInventorySnapshot Current => _current == null ? null : CargoInventoryFrames.Copy(_current);

        public CargoInventoryAssembler(string roomId) { _room = CargoValues.GuidKey(roomId); }

        public bool Add(CargoInventorySlice slice, out CargoInventorySnapshot snapshot)
        {
            snapshot = null;
            if (Faulted) throw new ArgumentException("Cargo assembly is faulted; a new room is required.");
            try
            {
                CargoInventorySlice owned = CargoInventoryFrames.Copy(slice);
                if (owned.SourceRoomId != _room) throw new ArgumentException("Cargo source room mismatch.");
                if (owned.Generation < HighestGeneration || owned.Generation == HighestGeneration && owned.Revision < HighestRevision)
                    return false;
                bool sameBatch = owned.Generation == HighestGeneration && owned.Revision == HighestRevision;
                if (sameBatch && _pages == null) return false; // explicitly cleared, never resurrect it
                if (!sameBatch) Begin(owned);
                if (_headerIdentity != CargoInventoryFrames.HeaderIdentity(owned))
                    throw new ArgumentException("Cargo assembly headers changed within one revision.");
                string identity = CargoInventoryFrames.SliceIdentity(owned);
                if (owned.Index < _next)
                {
                    if (_pageIdentities[owned.Index] != identity) throw new ArgumentException("Cargo slice replay changed its payload.");
                    return false; // identical completed or partial replay is idempotent, no second publication
                }
                if (owned.Index != _next) throw new ArgumentException("Cargo slices must arrive in exact order.");

                // Validate all cross-page identities before saving a new page.
                // A partially received roster is never exposed as Current.
                var entries = new List<CargoInventoryEntry>(owned.EntryCount);
                for (int i = 0; i < _next; i++) entries.AddRange(_pages[i].Entries);
                entries.AddRange(owned.Entries);
                var keys = new HashSet<(long, int)>(); var owners = new Dictionary<long, string>();
                foreach (CargoInventoryEntry entry in entries)
                {
                    if (!keys.Add((entry.CaptureId, entry.ProductIndex))) throw new ArgumentException("Duplicate cargo product across pages.");
                    if (owners.TryGetValue(entry.CaptureId, out string owner) && owner != entry.MemberId)
                        throw new ArgumentException("A cargo capture changed personal bag across pages.");
                    owners[entry.CaptureId] = entry.MemberId;
                }
                if (owners.Count + owned.ReservedCaptureCount + owned.UnknownCaptureCount > CargoValues.MaxCaptures)
                    throw new ArgumentException("Cargo capture capacity exceeded across pages.");
                CargoInventorySnapshot complete = null;
                if (owned.Index + 1 == owned.Count)
                {
                    complete = Assemble(owned, entries);
                    if (CargoInventoryFrames.Fingerprint(complete) != owned.Fingerprint)
                        throw new ArgumentException("Cargo snapshot fingerprint differs from the complete inventory.");
                }
                _pages[_next] = owned; _pageIdentities[_next] = identity; _next++;
                if (complete == null) return false;
                _current = complete; snapshot = CargoInventoryFrames.Copy(complete); return true;
            }
            catch (ArgumentException)
            {
                Faulted = true; DropView(); throw;
            }
        }

        public void Clear() { DropView(); }

        private void Begin(CargoInventorySlice first)
        {
            if (first.Index != 0 || first.Revision <= HighestRevision)
                throw new ArgumentException("New cargo batches require slice zero and a higher room revision.");
            if (first.Generation == HighestGeneration)
            {
                if (_header == null || first.ExpeditionId != _header.ExpeditionId)
                    throw new ArgumentException("A cargo generation cannot change expedition identity.");
                Continuity(_header, first);
            }
            else
            {
                if (first.Generation <= HighestGeneration || _expeditions.Contains(first.ExpeditionId))
                    throw new ArgumentException("New cargo generations require a new expedition identity.");
                if (_expeditions.Count >= CargoInventoryFrames.MaxExpeditions)
                    throw new ArgumentException("Cargo room expedition quota exhausted; identities are not evicted.");
                _expeditions.Add(first.ExpeditionId);
            }
            HighestGeneration = first.Generation; HighestRevision = first.Revision;
            _header = first; _headerIdentity = CargoInventoryFrames.HeaderIdentity(first);
            _pages = new CargoInventorySlice[first.Count]; _pageIdentities = new string[first.Count];
            _next = 0; _current = null; // immediate retraction before a complete replacement exists
        }

        private static void Continuity(CargoInventorySlice previous, CargoInventorySlice next)
        {
            if (previous.ReturnId != null && previous.ReturnId != next.ReturnId ||
                previous.Phase == CargoExpeditionPhase.Returned && next.Phase != CargoExpeditionPhase.Returned ||
                previous.Phase == CargoExpeditionPhase.Aborted && next.Phase != CargoExpeditionPhase.Aborted ||
                previous.Phase == CargoExpeditionPhase.Returning && next.Phase == CargoExpeditionPhase.Active)
                throw new ArgumentException("Cargo expedition phase or frozen return identity regressed.");
            foreach (CargoMemberSnapshot member in previous.Members)
            {
                CargoMemberSnapshot nextMember = next.Members.SingleOrDefault(candidate => candidate.MemberId == member.MemberId);
                if (nextMember == null || nextMember.BagMode != member.BagMode || nextMember.BagRevision < member.BagRevision ||
                    nextMember.HighestRequestId < member.HighestRequestId)
                    throw new ArgumentException("Cargo member identity or request/bag revision regressed.");
            }
        }

        private static CargoInventorySnapshot Assemble(CargoInventorySlice header, List<CargoInventoryEntry> entries)
        {
            if (entries.Count != header.EntryCount) throw new ArgumentException("Cargo complete entry count differs from its header.");
            CargoMemberSnapshot[] members = header.Members.Select(member => CargoInventoryFrames.CopyMember(member, true)).ToArray();
            foreach (CargoMemberSnapshot member in members)
                member.Inventory = entries.Where(entry => entry.MemberId == member.MemberId).Select(entry => new CargoInventoryItem
                { CaptureId = entry.CaptureId, ProductIndex = entry.ProductIndex, Product = CargoValues.Copy(entry.Product) }).ToArray();
            return CargoInventoryFrames.Copy(new CargoInventorySnapshot
            {
                Generation = header.Generation, Revision = header.Revision, ExpeditionId = header.ExpeditionId,
                ReturnId = header.ReturnId, Phase = header.Phase, SourceRoomId = header.SourceRoomId, Members = members,
                ReservedCaptureCount = header.ReservedCaptureCount, UnknownCaptureCount = header.UnknownCaptureCount,
                PendingReturnProductCount = header.PendingReturnProductCount
            });
        }
        private void DropView() { _current = null; _pages = null; _pageIdentities = null; _next = 0; }
    }
}
