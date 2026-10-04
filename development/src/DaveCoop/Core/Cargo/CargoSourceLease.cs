using System;
using System.Globalization;
using DaveCoop.Core.World;

namespace DaveCoop.Core.Cargo
{
    // No products or caller-chosen operation number exist at source reservation.
    // GateOperationId is a participant-local diagnostic, never the cargo ID.
    public sealed class CargoSourceIntent
    {
        public string ExpeditionId { get; set; }
        public string MemberId { get; set; }
        public long RequestId { get; set; }
        public long BagRevision { get; set; }
        public CargoSource Source { get; set; }
        public long ActorRevision { get; set; }
        public long LoadoutRevision { get; set; }
        public long GateOperationId { get; set; }
    }

    // Host-local observations from a future verified adapter, not wire claims
    // or LootCallLineage's observed synchronous enclosure. Defaults deny entry.
    public class CargoSourceFacts
    {
        public string ExpeditionId { get; set; }
        public string MemberId { get; set; }
        public int BoundPlayerId { get; set; }
        public long RequestId { get; set; }
        public long OperationId { get; set; }
        public string IntentFingerprint { get; set; }
        public string CurrentRoomId { get; set; }
        public CargoSource Source { get; set; }
        public long BagRevision { get; set; }
        public long ActorRevision { get; set; }
        public long LoadoutRevision { get; set; }
        public double SampledAt { get; set; }
        public bool HostAuthority { get; set; }
        public bool SourceIdentityVerified { get; set; }
        public bool OrdinaryFishVerified { get; set; }
        public bool ActorPermitted { get; set; }
        public bool SourceAvailable { get; set; }
        public bool CapacityPolicyVerified { get; set; }
        public CargoCapacityPolicy CapacityPolicy { get; set; }
        public bool CapacityRoutingVerified { get; set; }
        public bool NativeEntryCapabilityVerified { get; set; }
        // Must be checked BEFORE native selection. A late NoBagWriteYet flag
        // cannot retroactively prove the earlier ordinary Add was intercepted.
        public bool YieldSelectionIsolationVerified { get; set; }
        public bool NativeNotEntered { get; set; }
        public bool HostBagWeightVerified { get; set; }
        public double NativeCurrentWeight { get; set; }
    }

    public sealed class CargoLateYieldFacts : CargoSourceFacts
    {
        public string ProductsFingerprint { get; set; }
        public bool CompleteSelectedYield { get; set; }
        public bool MaterializationBoundaryHeld { get; set; }
        public bool NoBagWriteYet { get; set; }
    }

    // Only its minting ledger may consume this exact object. Owned snapshots
    // cannot recreate a lease; closing a peer does not release it or its source.
    public sealed class CargoSourceLease
    {
        private readonly CargoSourceIntent _intent;
        public long CaptureId { get; }
        public long OperationId { get; }
        public int BoundPlayerId { get; }
        public string IntentFingerprint { get; }
        public CargoSourceIntent Intent => CargoSourceValues.Copy(_intent);
        public bool NativePermission => false;
        public bool SourceOperationBound => false;
        public bool NativeExecutionImplemented => false;
        internal CargoSourceLease(long captureId, long operationId, int player, CargoSourceIntent intent, string fingerprint)
        {
            CaptureId = captureId; OperationId = operationId; BoundPlayerId = player;
            _intent = CargoSourceValues.Copy(intent); IntentFingerprint = fingerprint;
        }
    }

    public static class CargoSourceValues
    {
        public static CargoSourceIntent Copy(CargoSourceIntent intent)
        {
            if (intent == null || intent.RequestId < 1 || intent.BagRevision < 1 || intent.ActorRevision < 1 ||
                intent.LoadoutRevision < 1 || intent.GateOperationId < 0) throw new ArgumentException("Invalid cargo source intent.");
            return new CargoSourceIntent
            {
                ExpeditionId = CargoValues.GuidKey(intent.ExpeditionId), MemberId = CargoValues.GuidKey(intent.MemberId),
                RequestId = intent.RequestId, BagRevision = intent.BagRevision, Source = CargoValues.Copy(intent.Source),
                ActorRevision = intent.ActorRevision, LoadoutRevision = intent.LoadoutRevision, GateOperationId = intent.GateOperationId
            };
        }
        public static string Fingerprint(CargoSourceIntent intent)
        {
            CargoSourceIntent owned = Copy(intent);
            var hash = new CanonicalHash("cargo-source-intent-v1").Add(owned.ExpeditionId).Add(owned.MemberId)
                .Add(Number(owned.RequestId)).Add(Number(owned.BagRevision)).Add(CargoValues.SourceKey(owned.Source))
                .Add(Number(owned.ActorRevision)).Add(Number(owned.LoadoutRevision)).Add(Number(owned.GateOperationId));
            return "cargo-source-intent-v1/" + hash.Finish();
        }
        private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
