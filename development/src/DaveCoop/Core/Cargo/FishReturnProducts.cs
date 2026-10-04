using System;
using System.Collections.Generic;

namespace DaveCoop.Core.Cargo
{
    public enum FishReturnCountMode { DirectCount = 1, ExchangeWholeOnce = 2 }
    public enum FishReturnMappingStage { NotAttempted = 1, Mapping = 2, MappingReady = 3, EnteredUnknown = 4 }

    // Minted from this selection's owned capture products. An original drop
    // ordinal includes no-drop sentinels; ProductIndex is the compact ledger
    // index. SelectedLookupId is never substituted for the original product TID.
    public sealed class FishReturnProductRequest
    {
        public int ProductIndex { get; }
        public int DropOrdinal { get; }
        public int SelectedLookupId { get; }
        public int ProductTid { get; }
        public int RawGrade { get; }
        public int RawCount { get; }
        public int ItemType { get; }
        public string RawProductFingerprint { get; }
        public FishReturnCountMode CountMode { get; }

        internal FishReturnProductRequest(int productIndex, int dropOrdinal, int lookupId, int productTid,
            int rawGrade, int rawCount, int itemType, string rawProductFingerprint, FishReturnCountMode countMode)
        {
            ProductIndex = productIndex; DropOrdinal = dropOrdinal; SelectedLookupId = lookupId; ProductTid = productTid;
            RawGrade = rawGrade; RawCount = rawCount; ItemType = itemType; RawProductFingerprint = rawProductFingerprint; CountMode = countMode;
        }
    }

    public sealed class FishReturnProductSample
    {
        public FishReturnProductRequest Request { get; }
        public bool ReturnItemHeld { get; }
        public int? ReturnItemTid { get; }
        public int? ItemDataId { get; }
        public int? ItemRank { get; }
        public int? ReturnItemType { get; }
        public bool IngredientsHeld { get; }
        public int? IngredientTid { get; }
        public int? StorageCount { get; }

        internal FishReturnProductSample(FishReturnProductRequest request, bool itemHeld, int? itemTid, int? itemDataId,
            int? rank, int? type, bool ingredientsHeld, int? ingredientTid, int? count)
        {
            Request = request; ReturnItemHeld = itemHeld; ReturnItemTid = itemTid; ItemDataId = itemDataId; ItemRank = rank;
            ReturnItemType = type; IngredientsHeld = ingredientsHeld; IngredientTid = ingredientTid; StorageCount = count;
        }
    }

    // Complete mapping/count output only. Final grade, place and conversion
    // policy still need independent host evidence before a return plan is bound.
    public sealed class FishReturnProductMapping
    {
        public FishReturnProductRequest Request { get; }
        public int IngredientId { get; }
        public int ParentId { get; }
        public int Rank { get; }
        public int StorageCount { get; }

        internal FishReturnProductMapping(FishReturnProductRequest request, int ingredientId, int parentId, int rank, int count)
        { Request = request; IngredientId = ingredientId; ParentId = parentId; Rank = rank; StorageCount = count; }
    }

    public sealed class FishReturnMappingSnapshot
    {
        public FishReturnMappingStage Stage { get; }
        public FishReturnProductRequest CurrentRequest { get; }
        public FishReturnProductSample[] Samples { get; }
        public FishReturnProductMapping[] Results { get; }
        public string RawProductsFingerprint { get; }
        public string Failure { get; }
        public bool FinalReturnGradeKnown => false;
        public bool ReturnConversionVerified => false;
        public bool CaptureConfirmed => false;

        internal FishReturnMappingSnapshot(FishReturnMappingStage stage, FishReturnProductRequest current,
            FishReturnProductSample[] samples, FishReturnProductMapping[] results, string fingerprint, string failure)
        { Stage = stage; CurrentRequest = current; Samples = samples; Results = results; RawProductsFingerprint = fingerprint; Failure = failure; }
    }

    // Native references and lookup/handle attempts remain inside the actual
    // backend. Every callback receives the exact active immutable request.
    public interface IFishYieldReturnMappingBackend
    {
        void HoldReturnItem(FishReturnProductRequest request);
        int ReadReturnItemTid(FishReturnProductRequest request);
        int ReadItemDataId(FishReturnProductRequest request);
        int ReadItemRank(FishReturnProductRequest request);
        int ReadReturnItemType(FishReturnProductRequest request);
        void HoldIngredients(FishReturnProductRequest request, int itemDataId);
        int ReadIngredientTid(FishReturnProductRequest request);
        int ExchangeWholeOnce(FishReturnProductRequest request);
    }

    public sealed partial class FishYieldSelection
    {
        private sealed class ReturnSampleBuilder
        {
            public FishReturnProductRequest Request;
            public bool ItemHeld, IngredientsHeld;
            public int? ItemTid, DataId, Rank, Type, IngredientTid, Count;
            public FishReturnProductSample Copy() => new FishReturnProductSample(Request, ItemHeld, ItemTid, DataId, Rank, Type, IngredientsHeld, IngredientTid, Count);
        }

        private readonly List<ReturnSampleBuilder> _returnSamples = new List<ReturnSampleBuilder>(CargoValues.MaxProductsPerCapture);
        private FishReturnMappingStage _returnStage = FishReturnMappingStage.NotAttempted;
        private FishReturnProductRequest _currentReturnRequest;
        private FishReturnProductMapping[] _returnResults;
        private string _returnRawProductsFingerprint, _returnFailure;

        public FishReturnMappingSnapshot ReturnMapping
        {
            get
            {
                var samples = new FishReturnProductSample[_returnSamples.Count];
                for (int i = 0; i < samples.Length; i++) samples[i] = _returnSamples[i].Copy();
                var results = _returnResults == null ? Array.Empty<FishReturnProductMapping>() : (FishReturnProductMapping[])_returnResults.Clone();
                return new FishReturnMappingSnapshot(_returnStage, _currentReturnRequest, samples, results, _returnRawProductsFingerprint, _returnFailure);
            }
        }

        // Modes come from a future verified category producer; this method
        // freezes them but does not prove their policy. Failed business work is
        // never retried, even when the original capture is later confirmed.
        public bool MapReturnOnce(FishReturnCountMode[] perProductModes)
        {
            if (Environment.CurrentManagedThreadId != _threadId || _busy || _returnStage != FishReturnMappingStage.NotAttempted ||
                _productStage != FishProductNormalizationStage.CaptureProductsHeld || _captureProducts == null ||
                !_ledger.IsSelectionEntered(_lease) || !(_backend is IFishYieldReturnMappingBackend returnBackend) ||
                perProductModes == null || perProductModes.Length != _captureProducts.Length) return false;
            var modes = (FishReturnCountMode[])perProductModes.Clone();
            foreach (FishReturnCountMode mode in modes) if (!Enum.IsDefined(typeof(FishReturnCountMode), mode)) return false;
            foreach (CargoProduct product in _captureProducts) if (product.Count != 1) return false;

            _busy = true; _returnStage = FishReturnMappingStage.Mapping; _returnRawProductsFingerprint = _captureProductsFingerprint;
            try
            {
                FishReturnProductRequest[] requests = FreezeReturnRequests(modes);
                var complete = new FishReturnProductMapping[requests.Length];
                for (int i = 0; i < requests.Length; i++)
                {
                    FishReturnProductRequest request = requests[i];
                    _currentReturnRequest = request;
                    var sample = new ReturnSampleBuilder { Request = request };
                    _returnSamples.Add(sample);

                    RequireReturnWindow();
                    returnBackend.HoldReturnItem(request);
                    sample.ItemHeld = true;
                    RequireReturnWindow();

                    RequireReturnWindow();
                    sample.ItemTid = returnBackend.ReadReturnItemTid(request);
                    RequireReturnWindow();
                    if (sample.ItemTid.Value != request.ProductTid) throw new InvalidOperationException("Return item TID differs from the fixed captured product.");

                    RequireReturnWindow();
                    sample.DataId = returnBackend.ReadItemDataId(request);
                    RequireReturnWindow();
                    if (sample.DataId.Value < 1) throw new ArgumentException("Original item data ID is unavailable.");

                    RequireReturnWindow();
                    sample.Rank = returnBackend.ReadItemRank(request);
                    RequireReturnWindow();
                    if (sample.Rank.Value < 0) throw new ArgumentException("Original item rank is unavailable.");

                    RequireReturnWindow();
                    sample.Type = returnBackend.ReadReturnItemType(request);
                    RequireReturnWindow();
                    if (sample.Type.Value != request.ItemType) throw new InvalidOperationException("Return item type differs from the fixed captured resource.");

                    RequireReturnWindow();
                    returnBackend.HoldIngredients(request, sample.DataId.Value);
                    sample.IngredientsHeld = true;
                    RequireReturnWindow();

                    RequireReturnWindow();
                    sample.IngredientTid = returnBackend.ReadIngredientTid(request);
                    RequireReturnWindow();
                    if (sample.IngredientTid.Value < 1) throw new ArgumentException("Original ingredient identity is unavailable.");

                    if (request.CountMode == FishReturnCountMode.ExchangeWholeOnce)
                    {
                        RequireReturnWindow();
                        sample.Count = returnBackend.ExchangeWholeOnce(request);
                        RequireReturnWindow();
                    }
                    else sample.Count = request.RawCount;
                    if (sample.Count.Value < 1 || sample.Count.Value > 1000000) throw new ArgumentException("Original storage count is unsupported.");

                    complete[i] = new FishReturnProductMapping(request, sample.IngredientTid.Value, sample.ItemTid.Value, sample.Rank.Value, sample.Count.Value);
                }
                RequireReturnWindow();
                _returnResults = complete;
                _returnStage = FishReturnMappingStage.MappingReady;
                return true;
            }
            catch (Exception error)
            {
                _returnStage = FishReturnMappingStage.EnteredUnknown;
                _returnFailure = error.GetType().Name;
                return false;
            }
            finally { _currentReturnRequest = null; _busy = false; }
        }

        private FishReturnProductRequest[] FreezeReturnRequests(FishReturnCountMode[] modes)
        {
            if (_productSamples.Count != _captureProducts.Length) throw new InvalidOperationException("Captured product/sample membership differs.");
            var requests = new FishReturnProductRequest[_captureProducts.Length];
            int index = 0;
            foreach (FishSelectedDrop drop in _drops)
            {
                if (!drop.HasProduct) continue;
                if (!drop.ResourceHeld || index >= requests.Length) throw new InvalidOperationException("An original product resource is not held.");
                CargoProduct product = _captureProducts[index]; ProductSampleBuilder original = _productSamples[index];
                if (original.Drop.Ordinal != drop.Ordinal || original.Tid != product.ProductId || original.CaptureGrade != product.Grade ||
                    !original.Type.HasValue || drop.Count != product.Count) throw new InvalidOperationException("Captured product provenance differs from its original selection.");
                requests[index] = new FishReturnProductRequest(index, drop.Ordinal, drop.ItemId, product.ProductId, product.Grade, product.Count,
                    original.Type.Value, CargoValues.ProductFingerprint(product), modes[index]);
                index++;
            }
            if (index != requests.Length) throw new InvalidOperationException("Captured product/drop membership differs.");
            return requests;
        }

        private void RequireReturnWindow()
        {
            if (Environment.CurrentManagedThreadId != _threadId || !_busy || _returnStage != FishReturnMappingStage.Mapping ||
                _currentReturnRequest == null || !_ledger.IsSelectionEntered(_lease))
                throw new InvalidOperationException("Return mapping requires this exact entered selection.");
            _backend.ValidateSource();
            if (!_ledger.IsSelectionEntered(_lease)) throw new InvalidOperationException("Capture closed during return mapping.");
        }

        // A candidate plan from fixed mapping/count outputs. Caller-supplied
        // grade/place/policy are not converted or authorized here. Binding still
        // requires the existing ledger's independently verified fresh facts.
        public CargoEmployeeReturnPlan CreateMappedReturnPlan(int index, string returnId, string policyFingerprint, int finalGrade, int place)
        {
            if (Environment.CurrentManagedThreadId != _threadId || _busy || _returnStage != FishReturnMappingStage.MappingReady || _returnResults == null)
                throw new InvalidOperationException("A complete idle return mapping is required.");
            if (index < 0 || index >= _returnResults.Length) throw new ArgumentOutOfRangeException(nameof(index));
            FishReturnProductMapping result = _returnResults[index]; CargoSourceIntent intent = _lease.Intent;
            return new CargoEmployeeReturnPlan(intent.ExpeditionId, returnId, intent.MemberId, _lease.CaptureId, result.Request.ProductIndex,
                result.Request.RawProductFingerprint, policyFingerprint, result.IngredientId, result.ParentId, result.Rank,
                finalGrade, result.StorageCount, place);
        }
    }
}
