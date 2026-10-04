using System;
using System.Collections.Generic;

namespace DaveCoop.Core.Cargo
{
    public enum FishProductNormalizationStage { NotAttempted = 1, Normalizing = 2, CaptureProductsHeld = 3, EnteredUnknown = 4 }

    public interface IFishYieldProductBackend
    {
        int ReadProductTid(FishSelectedDrop drop);
        int ReadBaseGrade(FishSelectedDrop drop);
        float ReadBaseWeight(FishSelectedDrop drop);
        int ReadItemType(FishSelectedDrop drop);
    }

    public sealed class FishProductSample
    {
        public int Ordinal { get; }
        public int SelectedLookupId { get; }
        public int? ProductTid { get; }
        public int? BaseGrade { get; }
        public float? BaseWeight { get; }
        public int? ItemType { get; }
        public int? CaptureGrade { get; }
        internal FishProductSample(int ordinal, int lookupId, int? tid, int? grade, float? weight, int? type, int? captureGrade)
        { Ordinal = ordinal; SelectedLookupId = lookupId; ProductTid = tid; BaseGrade = grade; BaseWeight = weight; ItemType = type; CaptureGrade = captureGrade; }
    }

    public sealed class FishProductNormalizationSnapshot
    {
        public FishProductNormalizationStage Stage { get; internal set; }
        public int? CurrentOrdinal { get; internal set; }
        public FishProductSample[] Samples { get; internal set; }
        public CargoProduct[] Products { get; internal set; }
        public string ProductsFingerprint { get; internal set; }
        public string Failure { get; internal set; }
        public bool CaptureConfirmed => false;
        public bool FinalReturnGradeKnown => false;
    }

    public sealed partial class FishYieldSelection
    {
        private sealed class ProductSampleBuilder
        {
            public FishSelectedDrop Drop;
            public int? Tid, Grade, Type, CaptureGrade;
            public float? Weight;
            public FishProductSample Copy() => new FishProductSample(Drop.Ordinal, Drop.ItemId, Tid, Grade, Weight, Type, CaptureGrade);
        }
        private readonly List<ProductSampleBuilder> _productSamples = new List<ProductSampleBuilder>(CargoValues.MaxProductsPerCapture);
        private FishProductNormalizationStage _productStage = FishProductNormalizationStage.NotAttempted;
        private CargoProduct[] _captureProducts;
        private string _captureProductsFingerprint, _productFailure;
        private int? _currentProductOrdinal;
        public FishProductNormalizationSnapshot Normalization
        {
            get
            {
                var samples = new FishProductSample[_productSamples.Count];
                for (int i = 0; i < samples.Length; i++) samples[i] = _productSamples[i].Copy();
                return new FishProductNormalizationSnapshot
                {
                    Stage = _productStage, CurrentOrdinal = _currentProductOrdinal, Samples = samples,
                    Products = _captureProducts == null ? Array.Empty<CargoProduct>() : CargoValues.CopyProducts(_captureProducts),
                    ProductsFingerprint = _captureProductsFingerprint, Failure = _productFailure
                };
            }
        }

        // Original resource getters may do native work. This one-way attempt
        // starts only after the exact lease was entered and all selection held.
        public bool NormalizeOnce()
        {
            if (Environment.CurrentManagedThreadId != _threadId || _busy || _stage != FishYieldSelectionStage.RawPlanHeld ||
                _productStage != FishProductNormalizationStage.NotAttempted || !_ledger.IsSelectionEntered(_lease) ||
                !(_backend is IFishYieldProductBackend productBackend)) return false;
            _busy = true; _productStage = FishProductNormalizationStage.Normalizing;
            try
            {
                var products = new List<CargoProduct>(CargoValues.MaxProductsPerCapture);
                foreach (FishSelectedDrop drop in _drops)
                {
                    if (!drop.HasProduct) continue; // Only the frozen -1 no-drop result can be absent here.
                    if (!drop.ResourceHeld) throw new InvalidOperationException("An original selected resource is not held.");
                    _currentProductOrdinal = drop.Ordinal;
                    var sample = new ProductSampleBuilder { Drop = drop };
                    _productSamples.Add(sample);
                    _backend.ValidateSource();
                    sample.Tid = productBackend.ReadProductTid(drop);
                    _backend.ValidateSource();
                    sample.Grade = productBackend.ReadBaseGrade(drop);
                    _backend.ValidateSource();
                    sample.Weight = productBackend.ReadBaseWeight(drop);
                    _backend.ValidateSource();
                    sample.Type = productBackend.ReadItemType(drop);
                    _backend.ValidateSource();
                    sample.CaptureGrade = checked(sample.Grade.Value + drop.BonusGrade);
                    float rawWeight = sample.Weight.Value;
                    if (!float.IsFinite(rawWeight) || rawWeight < 0) throw new ArgumentException("Unsupported original resource weight.");
                    // This is each original product's float multiplication.
                    // The virtual employee bag accumulates these in double;
                    // it does not claim the host bag's sequential float total.
                    float unitWeight = drop.LiftType == 0 || drop.LiftType == 3 ? rawWeight : 0f;
                    float totalWeight = (float)drop.Count * unitWeight;
                    products.Add(CargoValues.Copy(new CargoProduct
                    {
                        ProductId = sample.Tid.Value, Grade = sample.CaptureGrade.Value, Count = drop.Count,
                        UnitWeight = (double)unitWeight, TotalWeight = (double)totalWeight
                    }));
                }
                // A wholly empty raw result is not a proved empty capture. The
                // source stays unknown; there is no fabricated receipt or seal.
                CargoProduct[] complete = CargoValues.CopyProducts(products.ToArray());
                _backend.ValidateSource();
                _captureProducts = complete;
                _captureProductsFingerprint = CargoValues.ProductsFingerprint(complete);
                _productStage = FishProductNormalizationStage.CaptureProductsHeld;
                return true;
            }
            catch (Exception error)
            {
                _productStage = FishProductNormalizationStage.EnteredUnknown;
                _productFailure = error.GetType().Name;
                return false;
            }
            finally { _currentProductOrdinal = null; _busy = false; }
        }

        // No caller can supply or replace the selected products. Rechecking
        // fresh personal capacity uses the same cached plan and never getters.
        public CargoResult TrySealCaptureProducts(CargoLateYieldFacts freshFacts, double now)
        {
            if (Environment.CurrentManagedThreadId != _threadId || _busy ||
                _productStage != FishProductNormalizationStage.CaptureProductsHeld || _captureProducts == null)
                return new CargoResult(CargoReason.InvalidStage, _lease.CaptureId);
            _busy = true;
            try { return _ledger.LateSeal(_lease, _captureProducts, freshFacts, now); }
            finally { _busy = false; }
        }
    }
}
