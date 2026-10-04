using System;
using System.Globalization;
using DaveCoop.Core.World;

namespace DaveCoop.Core.Cargo
{
    // Host-local, already converted output for one ingredient. It does not
    // calculate policy, read a native slot, or prove storage/save execution.
    public sealed class CargoEmployeeReturnPlan
    {
        public string ExpeditionId { get; }
        public string ReturnId { get; }
        public string MemberId { get; }
        public long CaptureId { get; }
        public int ProductIndex { get; }
        public string RawProductFingerprint { get; }
        public string PolicyFingerprint { get; }
        public int IngredientId { get; }
        public int ParentId { get; }
        public int Rank { get; }
        public int FinalGrade { get; }
        public int StorageCount { get; }
        public int Place { get; }
        public string Fingerprint { get; }
        public bool NativePermission => false;

        public CargoEmployeeReturnPlan(string expeditionId, string returnId, string memberId, long captureId, int productIndex,
            string rawProductFingerprint, string policyFingerprint, int ingredientId, int parentId, int rank,
            int finalGrade, int storageCount, int place)
        {
            ExpeditionId = CargoValues.GuidKey(expeditionId); ReturnId = CargoValues.GuidKey(returnId); MemberId = CargoValues.GuidKey(memberId);
            if (captureId < 1 || productIndex < 0 || productIndex >= CargoValues.MaxProductsPerCapture || ingredientId < 1 ||
                parentId < 0 || rank < 0 || finalGrade < 0 || finalGrade > 1000 || storageCount < 1 || storageCount > 1000000 || place < 0)
                throw new ArgumentException("Invalid employee ingredient return output.");
            RawProductFingerprint = FingerprintValue(rawProductFingerprint, "cargo-product-v1/");
            PolicyFingerprint = FingerprintValue(policyFingerprint, "cargo-return-policy-v1/");
            CaptureId = captureId; ProductIndex = productIndex; IngredientId = ingredientId; ParentId = parentId;
            Rank = rank; FinalGrade = finalGrade; StorageCount = storageCount; Place = place;
            Fingerprint = "cargo-employee-return-v1/" + new CanonicalHash("cargo-employee-return-v1")
                .Add(ExpeditionId).Add(ReturnId).Add(MemberId).Add(CaptureId.ToString(CultureInfo.InvariantCulture))
                .Add(ProductIndex).Add(RawProductFingerprint).Add(PolicyFingerprint).Add(IngredientId).Add(ParentId)
                .Add(Rank).Add(FinalGrade).Add(StorageCount).Add(Place).Finish();
        }

        private static string FingerprintValue(string value, string prefix)
        {
            if (value == null || !value.StartsWith(prefix, StringComparison.Ordinal) || value.Length != prefix.Length + 64)
                throw new ArgumentException("Invalid return fingerprint.");
            for (int i = prefix.Length; i < value.Length; i++)
                if (!(value[i] >= '0' && value[i] <= '9') && !(value[i] >= 'a' && value[i] <= 'f'))
                    throw new ArgumentException("Invalid return fingerprint encoding.");
            return value;
        }
    }
}
