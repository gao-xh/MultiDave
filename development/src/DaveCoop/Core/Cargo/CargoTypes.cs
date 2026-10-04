using System;
using System.Globalization;
using DaveCoop.Core.World;

namespace DaveCoop.Core.Cargo
{
    public enum CargoBagMode { HostNative = 1, EmployeeVirtual = 2 }
    public enum CargoCapacityPolicy { RejectOverCapacity = 1, AllowPersonalOverweight = 2 }
    public enum CargoCaptureStage { Reserved = 1, EnteredUnknown = 2, Confirmed = 3, NativeNotEntered = 4 }
    public enum CargoReceiptKind { HostNativeBagDelta = 1, EmployeeDivertedYield = 2 }
    public enum CargoReturnStage { Unclaimed = 1, Leased = 2, EnteredUnknown = 3, StorageObserved = 4, SaveConfirmed = 5, NotRequired = 6 }
    public enum CargoExpeditionPhase { Active = 1, Returning = 2, Returned = 3, Aborted = 4 }
    public enum CargoReason
    {
        None, InvalidInput, UnknownMember, WrongIdentity, StaleFacts, MissingCapability, Disconnected,
        Replay, Conflict, Duplicate, SourceBusy, CapacityExceeded, QuotaExceeded, InvalidStage,
        NotEnteredProofRequired, ReturnFrozen, ReturnIncomplete, NotFound, WrongBagMode
    }

    public sealed class CargoResult
    {
        public CargoReason Reason { get; }
        public long CaptureId { get; }
        public bool Accepted => Reason == CargoReason.None;
        internal CargoResult(CargoReason reason, long captureId = 0) { Reason = reason; CaptureId = captureId; }
    }

    public sealed class CargoMemberSetup
    {
        public string MemberId { get; set; }
        public CargoBagMode BagMode { get; set; }
        public double Capacity { get; set; }
        public double InitialWeight { get; set; }
    }

    public sealed class CargoSource
    {
        public string RoomId { get; set; }
        public long SceneEpoch { get; set; }
        public long EntityId { get; set; }
        public long LocalGeneration { get; set; }
    }

    // Supplied by a verified yield adapter; never inferred from a species TID.
    // TotalWeight is the verified effective weight. UnitWeight is optional data.
    public sealed class CargoProduct
    {
        public int ProductId { get; set; }
        // Captured raw slot grade. Return/storage quality is a separate plan;
        // it must not replace the grade in this capture's fixed fingerprint.
        public int Grade { get; set; }
        public int Count { get; set; }
        public double? UnitWeight { get; set; }
        public double TotalWeight { get; set; }
    }

    public sealed class CargoCaptureRequest
    {
        public string ExpeditionId { get; set; }
        public string MemberId { get; set; }
        public long RequestId { get; set; }
        public long OperationId { get; set; }
        public long BagRevision { get; set; }
        public CargoSource Source { get; set; }
        public CargoProduct[] Products { get; set; }
    }

    // Host-local, freshly sampled adapter facts, not a packet or player claim.
    // A true fixture flag exercises the CLR fence; it enables no native code.
    public sealed class CargoCaptureFacts
    {
        public string ExpeditionId { get; set; }
        public string MemberId { get; set; }
        public int BoundPlayerId { get; set; }
        public long RequestId { get; set; }
        public long OperationId { get; set; }
        public string ProductsFingerprint { get; set; }
        public string CurrentRoomId { get; set; }
        public CargoSource Source { get; set; }
        public long BagRevision { get; set; }
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
        public bool NativeNotEntered { get; set; }
        public bool YieldVerified { get; set; }
        public bool CaptureTerminal { get; set; }
        public bool ActualBagDeltaVerified { get; set; }
        public double NativeBagWeightBefore { get; set; }
        public double NativeBagWeightAfter { get; set; }
        public bool HostBagWeightVerified { get; set; }
        public double NativeCurrentWeight { get; set; }
        public bool DiversionVerified { get; set; }
        public bool NoHostBagWrite { get; set; }
    }

    public sealed class CargoStorageFacts
    {
        public string ExpeditionId { get; set; }
        public string ReturnId { get; set; }
        public string MemberId { get; set; }
        public long CaptureId { get; set; }
        public int ProductIndex { get; set; }
        public string ProductFingerprint { get; set; }
        public string ReturnPlanFingerprint { get; set; }
        public double SampledAt { get; set; }
        public bool HostAuthority { get; set; }
        public bool ReturnConversionVerified { get; set; }
        public bool EmployeeStorageAdapterVerified { get; set; }
        public bool NativeEntryCapabilityVerified { get; set; }
        public bool HostNativeStorageChainVerified { get; set; }
        public bool StorageDeltaVerified { get; set; }
        public bool SaveConfirmed { get; set; }
    }

    public sealed class CargoInventoryItem
    {
        public long CaptureId { get; set; }
        public int ProductIndex { get; set; }
        public CargoProduct Product { get; set; }
    }
    public sealed class CargoMemberSnapshot
    {
        public string MemberId { get; set; }
        public CargoBagMode BagMode { get; set; }
        public double Capacity { get; set; }
        public double Weight { get; set; }
        public double ReservedWeight { get; set; }
        public bool IsOverweight => Weight > Capacity;
        public long BagRevision { get; set; }
        public bool Connected { get; set; }
        public long HighestRequestId { get; set; }
        public CargoInventoryItem[] Inventory { get; set; }
    }
    public sealed class CargoCaptureSnapshot
    {
        public long CaptureId { get; set; }
        // Source-only captures have no request/products until a complete yield
        // is sealed. Null is unknown, never a proved empty capture.
        public CargoSourceIntent Intent { get; set; }
        public CargoCaptureRequest Request { get; set; }
        public bool YieldBound { get; set; }
        // A full selected plan can be known while capacity is refused. It is
        // fixed evidence, not reserved weight, bag inventory or materialization.
        public bool SelectedYieldKnown { get; set; }
        public string SelectedYieldFingerprint { get; set; }
        public long OperationId { get; set; }
        public string Fingerprint { get; set; }
        public CargoCaptureStage Stage { get; set; }
        public CargoReceiptKind? ReceiptKind { get; set; }
    }
    public sealed class CargoReturnItemSnapshot
    {
        public long CaptureId { get; set; }
        public int ProductIndex { get; set; }
        public string MemberId { get; set; }
        public CargoBagMode BagMode { get; set; }
        public CargoProduct Product { get; set; }
        public CargoReturnStage Stage { get; set; }
        public CargoEmployeeReturnPlan Plan { get; set; }
        public string PlanFingerprint { get; set; }
    }
    public sealed class CargoLedgerSnapshot
    {
        public string ExpeditionId { get; set; }
        public string ReturnId { get; set; }
        public CargoExpeditionPhase Phase { get; set; }
        public string SourceRoomId { get; set; }
        public CargoMemberSnapshot[] Members { get; set; }
        public CargoCaptureSnapshot[] Captures { get; set; }
        public CargoReturnItemSnapshot[] ReturnItems { get; set; }
        public bool NativeExecutionImplemented => false;
        public bool CrashSafeExactlyOnce => false;
        public bool NativeBagInventoryComplete => false;
    }

    public static class CargoValues
    {
        public const int MaxMembers = 2;
        public const int MaxCaptures = 256;
        public const int MaxProductsPerCapture = 8;
        public const double MaxWeight = 1000000;
        public const double MaxFactsAge = 0.25;

        internal static string GuidKey(string value)
        {
            if (!Guid.TryParse(value, out Guid parsed) || parsed == Guid.Empty) throw new ArgumentException("Missing cargo identity.");
            return parsed.ToString("N");
        }
        internal static void Weight(double value)
        {
            if (!double.IsFinite(value) || value < 0 || value > MaxWeight) throw new ArgumentException("Invalid cargo weight.");
        }
        public static CargoSource Copy(CargoSource source)
        {
            if (source == null || source.SceneEpoch < 1 || source.EntityId < 1 || source.LocalGeneration < 1)
                throw new ArgumentException("Invalid cargo source.");
            return new CargoSource { RoomId = GuidKey(source.RoomId), SceneEpoch = source.SceneEpoch, EntityId = source.EntityId, LocalGeneration = source.LocalGeneration };
        }
        public static CargoProduct Copy(CargoProduct product)
        {
            if (product == null || product.ProductId < 1 || product.Grade < 0 || product.Grade > 1000 || product.Count < 1 || product.Count > 1000000)
                throw new ArgumentException("Invalid cargo product.");
            Weight(product.TotalWeight); if (product.UnitWeight.HasValue) Weight(product.UnitWeight.Value);
            return new CargoProduct
            {
                ProductId = product.ProductId, Grade = product.Grade, Count = product.Count,
                UnitWeight = product.UnitWeight.HasValue ? Zero(product.UnitWeight.Value) : (double?)null, TotalWeight = Zero(product.TotalWeight)
            };
        }
        public static CargoProduct[] CopyProducts(CargoProduct[] products)
        {
            if (products == null || products.Length < 1 || products.Length > MaxProductsPerCapture) throw new ArgumentException("Invalid cargo product count.");
            var owned = new CargoProduct[products.Length]; double weight = 0;
            for (int i = 0; i < products.Length; i++) { owned[i] = Copy(products[i]); weight += owned[i].TotalWeight; }
            Weight(weight); return owned;
        }
        public static CargoCaptureRequest Copy(CargoCaptureRequest request)
        {
            if (request == null || request.RequestId < 1 || request.OperationId < 1 || request.BagRevision < 1)
                throw new ArgumentException("Invalid cargo request identity.");
            return new CargoCaptureRequest
            {
                ExpeditionId = GuidKey(request.ExpeditionId), MemberId = GuidKey(request.MemberId), RequestId = request.RequestId,
                OperationId = request.OperationId, BagRevision = request.BagRevision, Source = Copy(request.Source), Products = CopyProducts(request.Products)
            };
        }
        public static string Fingerprint(CargoCaptureRequest request)
        {
            CargoCaptureRequest owned = Copy(request);
            var hash = new CanonicalHash("cargo-capture-v1").Add(owned.ExpeditionId).Add(owned.MemberId)
                .Add(Number(owned.RequestId)).Add(Number(owned.OperationId)).Add(Number(owned.BagRevision)).Add(SourceKey(owned.Source));
            AddProducts(hash, owned.Products); return "cargo-capture-v1/" + hash.Finish();
        }
        public static string ProductFingerprint(CargoProduct product)
        {
            var hash = new CanonicalHash("cargo-product-v1"); AddProducts(hash, new[] { Copy(product) });
            return "cargo-product-v1/" + hash.Finish();
        }
        public static string ProductsFingerprint(CargoProduct[] products)
        {
            var hash = new CanonicalHash("cargo-products-v1"); AddProducts(hash, CopyProducts(products)); return hash.Finish();
        }
        internal static string SourceKey(CargoSource source)
        {
            CargoSource owned = Copy(source);
            return owned.RoomId + "/" + Number(owned.SceneEpoch) + "/" + Number(owned.EntityId) + "/" + Number(owned.LocalGeneration);
        }
        internal static double ProductWeight(CargoProduct[] products)
        { double total = 0; foreach (CargoProduct item in products) total += item.TotalWeight; return total; }
        private static void AddProducts(CanonicalHash hash, CargoProduct[] products)
        {
            hash.Add(products.Length);
            foreach (CargoProduct item in products)
                hash.Add(item.ProductId).Add(item.Grade).Add(item.Count).Add(item.UnitWeight.HasValue ? 1 : 0)
                    .Add(Bits(item.UnitWeight ?? 0)).Add(Bits(item.TotalWeight));
        }
        private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
        private static string Bits(double value) => BitConverter.DoubleToInt64Bits(Zero(value)).ToString("X16", CultureInfo.InvariantCulture);
        private static double Zero(double value) => value == 0 ? 0 : value;
    }
}
