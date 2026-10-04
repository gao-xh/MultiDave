using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DaveCoop.Core.World;

namespace DaveCoop.Core.Cargo
{
    // Tracked ledger observations only. Weight is the ledger's reported value,
    // possibly an initial/historical native sample; it is not live bag UI data.
    public sealed class CargoInventorySnapshot
    {
        public long Generation { get; set; }
        public long Revision { get; set; }
        public string ExpeditionId { get; set; }
        public string ReturnId { get; set; }
        public CargoExpeditionPhase Phase { get; set; }
        public string SourceRoomId { get; set; }
        public int ReservedCaptureCount { get; set; }
        public int UnknownCaptureCount { get; set; }
        public int PendingReturnProductCount { get; set; }
        public CargoMemberSnapshot[] Members { get; set; }
        public bool LedgerObservationOnly => true;
        public bool NativeBagInventoryComplete => false;
        public bool NativeExecutionImplemented => false;
        public bool CrashSafeExactlyOnce => false;
    }

    public sealed class CargoInventoryEntry
    {
        public string MemberId { get; set; }
        public long CaptureId { get; set; }
        public int ProductIndex { get; set; }
        public CargoProduct Product { get; set; }
    }

    public sealed class CargoInventorySlice
    {
        public long Generation { get; set; }
        public long Revision { get; set; }
        public string ExpeditionId { get; set; }
        public string ReturnId { get; set; }
        public CargoExpeditionPhase Phase { get; set; }
        public string SourceRoomId { get; set; }
        public int ReservedCaptureCount { get; set; }
        public int UnknownCaptureCount { get; set; }
        public int PendingReturnProductCount { get; set; }
        public CargoMemberSnapshot[] Members { get; set; }
        public int EntryCount { get; set; }
        public int Index { get; set; }
        public int Count { get; set; }
        public string Fingerprint { get; set; }
        public CargoInventoryEntry[] Entries { get; set; }
        public bool LedgerObservationOnly => true;
        public bool NativeBagInventoryComplete => false;
        public bool NativeExecutionImplemented => false;
        public bool CrashSafeExactlyOnce => false;
    }

    public static class CargoInventoryFrames
    {
        public const int MaxEntries = CargoValues.MaxCaptures * CargoValues.MaxProductsPerCapture;
        public const int ItemsPerSlice = 32;
        public const int MaxSlices = MaxEntries / ItemsPerSlice;
        public const int MaxExpeditions = 64;
        private const string Prefix = "cargo-inventory-v1/";

        public static CargoInventorySnapshot FromLedger(CargoLedgerSnapshot ledger, long generation, long revision, string roomId)
        {
            if (ledger == null || ledger.Captures == null || ledger.ReturnItems == null ||
                ledger.Captures.Length > CargoValues.MaxCaptures || ledger.ReturnItems.Length > MaxEntries)
                throw new ArgumentException("Invalid cargo ledger observation.");
            string room = CargoValues.GuidKey(roomId);
            if (ledger.SourceRoomId != null && CargoValues.GuidKey(ledger.SourceRoomId) != room)
                throw new ArgumentException("The cargo ledger belongs to another source room.");
            var captureIds = new HashSet<long>(); var returns = new HashSet<(long, int)>();
            int reserved = 0, unknown = 0, pendingReturn = 0;
            foreach (CargoCaptureSnapshot capture in ledger.Captures)
            {
                if (capture == null || capture.CaptureId < 1 || !captureIds.Add(capture.CaptureId) ||
                    !Enum.IsDefined(typeof(CargoCaptureStage), capture.Stage))
                    throw new ArgumentException("Invalid cargo capture observation.");
                if (capture.Stage == CargoCaptureStage.Reserved) reserved++;
                if (capture.Stage == CargoCaptureStage.EnteredUnknown) unknown++;
            }
            foreach (CargoReturnItemSnapshot item in ledger.ReturnItems)
            {
                if (item == null || item.CaptureId < 1 || item.ProductIndex < 0 || item.ProductIndex >= CargoValues.MaxProductsPerCapture ||
                    !captureIds.Contains(item.CaptureId) || !returns.Add((item.CaptureId, item.ProductIndex)) ||
                    !Enum.IsDefined(typeof(CargoReturnStage), item.Stage))
                    throw new ArgumentException("Invalid cargo return observation.");
                if (item.Stage != CargoReturnStage.SaveConfirmed && item.Stage != CargoReturnStage.NotRequired) pendingReturn++;
            }
            return Copy(new CargoInventorySnapshot
            {
                Generation = generation, Revision = revision, ExpeditionId = ledger.ExpeditionId,
                ReturnId = ledger.ReturnId, Phase = ledger.Phase, SourceRoomId = room, Members = ledger.Members,
                ReservedCaptureCount = reserved, UnknownCaptureCount = unknown, PendingReturnProductCount = pendingReturn
            });
        }

        public static void Validate(CargoInventorySnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            Header(snapshot.Generation, snapshot.Revision, snapshot.ExpeditionId, snapshot.ReturnId, snapshot.Phase,
                snapshot.SourceRoomId, snapshot.ReservedCaptureCount, snapshot.UnknownCaptureCount, snapshot.PendingReturnProductCount);
            Members(snapshot.Members, false);
            var entries = new List<CargoInventoryEntry>();
            foreach (CargoMemberSnapshot member in snapshot.Members)
                foreach (CargoInventoryItem item in member.Inventory)
                {
                    if (item == null) throw new ArgumentException("Missing cargo inventory item.");
                    entries.Add(new CargoInventoryEntry { MemberId = member.MemberId, CaptureId = item.CaptureId, ProductIndex = item.ProductIndex, Product = item.Product });
                    if (entries.Count > MaxEntries) throw new ArgumentException("Cargo inventory entry capacity exceeded.");
                }
            ValidateEntries(entries, snapshot.Members, true, snapshot.ReservedCaptureCount + snapshot.UnknownCaptureCount);
            if (snapshot.PendingReturnProductCount > entries.Count +
                (snapshot.ReservedCaptureCount + snapshot.UnknownCaptureCount) * CargoValues.MaxProductsPerCapture)
                throw new ArgumentException("Pending return products exceed the tracked confirmed and unresolved cargo.");
            if (snapshot.Phase == CargoExpeditionPhase.Returned && entries.Count != 0)
                throw new ArgumentException("Returned cargo cannot retain a tracked inventory.");
        }

        public static void Validate(CargoInventorySlice slice)
        {
            if (slice == null) throw new ArgumentNullException(nameof(slice));
            Header(slice.Generation, slice.Revision, slice.ExpeditionId, slice.ReturnId, slice.Phase,
                slice.SourceRoomId, slice.ReservedCaptureCount, slice.UnknownCaptureCount, slice.PendingReturnProductCount);
            Members(slice.Members, true);
            if (slice.EntryCount < 0 || slice.EntryCount > MaxEntries || slice.Count != SliceCount(slice.EntryCount) ||
                slice.Count < 1 || slice.Count > MaxSlices || slice.Index < 0 || slice.Index >= slice.Count || slice.Entries == null ||
                slice.Entries.Length != Math.Min(ItemsPerSlice, slice.EntryCount - slice.Index * ItemsPerSlice))
                throw new ArgumentException("Invalid cargo slice boundaries or exact entry count.");
            if (slice.Fingerprint == null || !slice.Fingerprint.StartsWith(Prefix, StringComparison.Ordinal) ||
                slice.Fingerprint.Length != Prefix.Length + 64 || slice.Fingerprint.Substring(Prefix.Length).Any(c => c < '0' || c > '9' && c < 'a' || c > 'f'))
                throw new ArgumentException("Invalid cargo inventory fingerprint.");
            ValidateEntries(slice.Entries, slice.Members, false, slice.ReservedCaptureCount + slice.UnknownCaptureCount);
            if (slice.PendingReturnProductCount > slice.EntryCount +
                (slice.ReservedCaptureCount + slice.UnknownCaptureCount) * CargoValues.MaxProductsPerCapture)
                throw new ArgumentException("Pending return products exceed the tracked cargo header.");
            if (slice.Phase == CargoExpeditionPhase.Returned && slice.EntryCount != 0)
                throw new ArgumentException("Returned cargo cannot contain tracked inventory.");
        }

        public static CargoInventorySnapshot Copy(CargoInventorySnapshot snapshot)
        {
            Validate(snapshot);
            return new CargoInventorySnapshot
            {
                Generation = snapshot.Generation, Revision = snapshot.Revision, ExpeditionId = snapshot.ExpeditionId,
                ReturnId = snapshot.ReturnId, Phase = snapshot.Phase, SourceRoomId = snapshot.SourceRoomId,
                ReservedCaptureCount = snapshot.ReservedCaptureCount, UnknownCaptureCount = snapshot.UnknownCaptureCount,
                PendingReturnProductCount = snapshot.PendingReturnProductCount,
                Members = snapshot.Members.OrderBy(member => member.BagMode).Select(member => CopyMember(member, false)).ToArray()
            };
        }

        public static CargoInventorySlice Copy(CargoInventorySlice slice)
        {
            Validate(slice);
            return new CargoInventorySlice
            {
                Generation = slice.Generation, Revision = slice.Revision, ExpeditionId = slice.ExpeditionId, ReturnId = slice.ReturnId,
                Phase = slice.Phase, SourceRoomId = slice.SourceRoomId, ReservedCaptureCount = slice.ReservedCaptureCount,
                UnknownCaptureCount = slice.UnknownCaptureCount, PendingReturnProductCount = slice.PendingReturnProductCount,
                Members = slice.Members.OrderBy(member => member.BagMode).Select(member => CopyMember(member, true)).ToArray(),
                EntryCount = slice.EntryCount, Index = slice.Index, Count = slice.Count, Fingerprint = slice.Fingerprint,
                Entries = slice.Entries.Select(CopyEntry).ToArray()
            };
        }

        public static string Fingerprint(CargoInventorySnapshot snapshot) => Hash(Copy(snapshot), true);
        // Excludes transport generation/revision, includes Connected/phase/return
        // and pending counts as well as bag revisions and confirmed products.
        public static string ContentFingerprint(CargoInventorySnapshot snapshot) => Hash(Copy(snapshot), false);

        public static CargoInventorySlice[] Split(CargoInventorySnapshot snapshot)
        {
            CargoInventorySnapshot owned = Copy(snapshot);
            // Avoid a nested generic lambda: the game's generated interop
            // compiler references cannot supply its emitted nullable attribute.
            var flattened = new List<CargoInventoryEntry>();
            foreach (CargoMemberSnapshot member in owned.Members)
                foreach (CargoInventoryItem item in member.Inventory)
                    flattened.Add(new CargoInventoryEntry { MemberId = member.MemberId, CaptureId = item.CaptureId,
                        ProductIndex = item.ProductIndex, Product = item.Product });
            CargoInventoryEntry[] entries = flattened.ToArray();
            int count = SliceCount(entries.Length); string fingerprint = Hash(owned, true);
            var slices = new CargoInventorySlice[count];
            for (int index = 0; index < count; index++)
            {
                slices[index] = new CargoInventorySlice
                {
                    Generation = owned.Generation, Revision = owned.Revision, ExpeditionId = owned.ExpeditionId,
                    ReturnId = owned.ReturnId, Phase = owned.Phase, SourceRoomId = owned.SourceRoomId,
                    ReservedCaptureCount = owned.ReservedCaptureCount, UnknownCaptureCount = owned.UnknownCaptureCount,
                    PendingReturnProductCount = owned.PendingReturnProductCount,
                    Members = owned.Members.Select(member => CopyMember(member, true)).ToArray(),
                    EntryCount = entries.Length, Index = index, Count = count, Fingerprint = fingerprint,
                    Entries = entries.Skip(index * ItemsPerSlice).Take(ItemsPerSlice).Select(CopyEntry).ToArray()
                };
            }
            return slices;
        }

        internal static CargoMemberSnapshot CopyMember(CargoMemberSnapshot member, bool metadataOnly) => new CargoMemberSnapshot
        {
            MemberId = member.MemberId, BagMode = member.BagMode, Capacity = Zero(member.Capacity), Weight = Zero(member.Weight),
            ReservedWeight = Zero(member.ReservedWeight), BagRevision = member.BagRevision, Connected = member.Connected,
            HighestRequestId = member.HighestRequestId, Inventory = metadataOnly ? Array.Empty<CargoInventoryItem>() :
                member.Inventory.OrderBy(item => item.CaptureId).ThenBy(item => item.ProductIndex).Select(item => new CargoInventoryItem
                { CaptureId = item.CaptureId, ProductIndex = item.ProductIndex, Product = CargoValues.Copy(item.Product) }).ToArray()
        };
        internal static CargoInventoryEntry CopyEntry(CargoInventoryEntry entry) => new CargoInventoryEntry
        { MemberId = entry.MemberId, CaptureId = entry.CaptureId, ProductIndex = entry.ProductIndex, Product = CargoValues.Copy(entry.Product) };

        internal static string SliceIdentity(CargoInventorySlice slice)
        {
            var hash = HeaderHash(slice, "cargo-slice-v1").Add(slice.Index).Add(slice.Entries.Length);
            foreach (CargoInventoryEntry entry in slice.Entries) AddEntry(hash, entry);
            return hash.Finish();
        }
        internal static string HeaderIdentity(CargoInventorySlice slice) => HeaderHash(slice, "cargo-header-v1").Finish();
        private static CanonicalHash HeaderHash(CargoInventorySlice slice, string schema)
        {
            var hash = AddHeader(new CanonicalHash(schema), slice.Generation, slice.Revision, slice.ExpeditionId,
                slice.ReturnId, slice.Phase, slice.SourceRoomId, slice.ReservedCaptureCount, slice.UnknownCaptureCount, slice.PendingReturnProductCount)
                .Add(slice.Fingerprint).Add(slice.EntryCount).Add(slice.Count);
            foreach (CargoMemberSnapshot member in slice.Members) AddMember(hash, member);
            return hash;
        }
        private static string Hash(CargoInventorySnapshot snapshot, bool transport)
        {
            string schema = transport ? "cargo-inventory-v1" : "cargo-inventory-content-v1";
            var hash = AddHeader(new CanonicalHash(schema), transport ? snapshot.Generation : 0, transport ? snapshot.Revision : 0,
                snapshot.ExpeditionId, snapshot.ReturnId, snapshot.Phase, snapshot.SourceRoomId,
                snapshot.ReservedCaptureCount, snapshot.UnknownCaptureCount, snapshot.PendingReturnProductCount);
            foreach (CargoMemberSnapshot member in snapshot.Members)
            {
                AddMember(hash, member); hash.Add(member.Inventory.Length);
                foreach (CargoInventoryItem item in member.Inventory)
                    AddEntry(hash, new CargoInventoryEntry { MemberId = member.MemberId, CaptureId = item.CaptureId, ProductIndex = item.ProductIndex, Product = item.Product });
            }
            return schema + "/" + hash.Finish();
        }
        private static CanonicalHash AddHeader(CanonicalHash hash, long generation, long revision, string expedition,
            string returnId, CargoExpeditionPhase phase, string room, int reserved, int unknown, int pendingReturn) =>
            hash.Add(Number(generation)).Add(Number(revision)).Add(expedition).Add(returnId ?? "").Add((int)phase)
                .Add(room).Add(reserved).Add(unknown).Add(pendingReturn);
        private static void AddMember(CanonicalHash hash, CargoMemberSnapshot member) => hash.Add(member.MemberId).Add((int)member.BagMode)
            .Add(Bits(member.Capacity)).Add(Bits(member.Weight)).Add(Bits(member.ReservedWeight))
            .Add(Number(member.BagRevision)).Add(member.Connected ? 1 : 0).Add(Number(member.HighestRequestId));
        private static void AddEntry(CanonicalHash hash, CargoInventoryEntry entry) => hash.Add(entry.MemberId).Add(Number(entry.CaptureId))
            .Add(entry.ProductIndex).Add(CargoValues.ProductFingerprint(entry.Product));
        private static int SliceCount(int entries) => Math.Max(1, (entries + ItemsPerSlice - 1) / ItemsPerSlice);
        private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
        private static string Bits(double value) => BitConverter.DoubleToInt64Bits(Zero(value)).ToString("X16", CultureInfo.InvariantCulture);
        private static double Zero(double value) => value == 0 ? 0 : value;

        private static void Header(long generation, long revision, string expedition, string returnId, CargoExpeditionPhase phase,
            string room, int reserved, int unknown, int pendingReturn)
        {
            if (generation < 1 || revision < 1 || !Enum.IsDefined(typeof(CargoExpeditionPhase), phase))
                throw new ArgumentException("Invalid cargo transport identity or phase.");
            GuidIdentity(expedition); GuidIdentity(room);
            if (returnId != null) GuidIdentity(returnId);
            if (phase == CargoExpeditionPhase.Active && returnId != null ||
                (phase == CargoExpeditionPhase.Returning || phase == CargoExpeditionPhase.Returned) && returnId == null)
                throw new ArgumentException("Cargo phase and return identity disagree.");
            if (reserved < 0 || unknown < 0 || reserved > CargoValues.MaxCaptures || unknown > CargoValues.MaxCaptures ||
                reserved + unknown > CargoValues.MaxCaptures || pendingReturn < 0 || pendingReturn > MaxEntries ||
                returnId == null && pendingReturn != 0 || phase == CargoExpeditionPhase.Returned && (reserved != 0 || unknown != 0 || pendingReturn != 0))
                throw new ArgumentException("Invalid pending cargo observation counts.");
        }
        internal static void GuidIdentity(string value)
        {
            if (value == null || !Guid.TryParseExact(value, "N", out Guid parsed) || parsed == Guid.Empty || parsed.ToString("N") != value)
                throw new ArgumentException("Cargo wire identities must be canonical nonempty GUIDs.");
        }
        private static void Members(CargoMemberSnapshot[] members, bool metadataOnly)
        {
            if (members == null || members.Length != CargoValues.MaxMembers) throw new ArgumentException("Cargo requires exactly two members.");
            var ids = new HashSet<string>(StringComparer.Ordinal); var modes = new HashSet<CargoBagMode>();
            foreach (CargoMemberSnapshot member in members)
            {
                if (member == null) throw new ArgumentException("Missing cargo member.");
                GuidIdentity(member.MemberId);
                if (!Enum.IsDefined(typeof(CargoBagMode), member.BagMode) || !ids.Add(member.MemberId) || !modes.Add(member.BagMode) ||
                    member.BagRevision < 1 || member.HighestRequestId < 0 || member.Capacity <= 0 || member.Inventory == null ||
                    member.Inventory.Length > MaxEntries || metadataOnly && member.Inventory.Length != 0)
                    throw new ArgumentException("Invalid or mixed cargo member metadata.");
                CargoValues.Weight(member.Capacity); CargoValues.Weight(member.Weight); CargoValues.Weight(member.ReservedWeight);
                CargoValues.Weight(member.Weight + member.ReservedWeight);
            }
        }
        private static void ValidateEntries(IEnumerable<CargoInventoryEntry> entries, CargoMemberSnapshot[] members, bool complete, int pendingCaptures)
        {
            var memberIds = new HashSet<string>(members.Select(member => member.MemberId), StringComparer.Ordinal);
            var captures = new Dictionary<long, string>(); var indexes = new Dictionary<long, HashSet<int>>();
            foreach (CargoInventoryEntry entry in entries)
            {
                if (entry == null || entry.CaptureId < 1 || entry.ProductIndex < 0 || entry.ProductIndex >= CargoValues.MaxProductsPerCapture ||
                    !memberIds.Contains(entry.MemberId)) throw new ArgumentException("Invalid cargo entry identity or member.");
                CargoValues.Copy(entry.Product);
                if (captures.TryGetValue(entry.CaptureId, out string owner) && owner != entry.MemberId)
                    throw new ArgumentException("A capture cannot belong to both personal bags.");
                if (!captures.ContainsKey(entry.CaptureId)) { captures.Add(entry.CaptureId, entry.MemberId); indexes.Add(entry.CaptureId, new HashSet<int>()); }
                if (!indexes[entry.CaptureId].Add(entry.ProductIndex)) throw new ArgumentException("Duplicate capture product in cargo inventory.");
            }
            if (captures.Count + pendingCaptures > CargoValues.MaxCaptures) throw new ArgumentException("Cargo capture capacity exceeded.");
            if (complete)
                foreach (HashSet<int> set in indexes.Values)
                    for (int index = 0; index < set.Count; index++)
                        if (!set.Contains(index)) throw new ArgumentException("Confirmed capture products must be complete and contiguous.");
        }
    }
}
