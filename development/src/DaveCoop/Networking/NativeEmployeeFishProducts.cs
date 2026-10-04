using System;
using System.Collections.Generic;
using DaveCoop.Core.Cargo;
using DR;
using Il2CppInterop.Runtime;

namespace DaveCoop.Networking
{
    internal sealed partial class NativeEmployeeFishSelectionBridge : IFishYieldProductBackend
    {
        private readonly Dictionary<int, int> _productGetterAttempts = new Dictionary<int, int>();
        public FishProductNormalizationSnapshot Products => _selection?.Normalization;

        public bool NormalizeCaptureProductsOnce()
        {
            if (Environment.CurrentManagedThreadId != _unityThreadId || !_prepared || _released || _releaseAttempted) return false;
            return _selection.NormalizeOnce();
        }

        public CargoResult TrySealCaptureProducts(CargoLateYieldFacts freshFacts, double now)
        {
            if (Environment.CurrentManagedThreadId != _unityThreadId || !_prepared || _released || _releaseAttempted)
                return new CargoResult(CargoReason.InvalidStage, _lease.CaptureId);
            // The real host producer must supply current complete/held/no-write
            // and personal capacity evidence. This helper fabricates no flags.
            return _selection.TrySealCaptureProducts(freshFacts, now);
        }

        int IFishYieldProductBackend.ReadProductTid(FishSelectedDrop drop) => ProductResource(drop, 1).TID;
        int IFishYieldProductBackend.ReadBaseGrade(FishSelectedDrop drop) => ProductResource(drop, 2).ItemGrade;
        float IFishYieldProductBackend.ReadBaseWeight(FishSelectedDrop drop) => ProductResource(drop, 4).ItemWeight;
        int IFishYieldProductBackend.ReadItemType(FishSelectedDrop drop) => ProductResource(drop, 8).ItemType;

        private IItemBase ProductResource(FishSelectedDrop drop, int field)
        {
            RequireThread();
            FishProductNormalizationSnapshot normalization = _selection?.Normalization;
            FishSelectedDrop[] selected = _selection?.Snapshot.Drops;
            if (!_prepared || _released || _releaseAttempted || normalization == null ||
                normalization.Stage != FishProductNormalizationStage.Normalizing || drop == null ||
                normalization.CurrentOrdinal != drop.Ordinal || !drop.HasProduct || !drop.ResourceHeld ||
                drop.Ordinal < 0 || selected == null || drop.Ordinal >= selected.Length ||
                !ReferenceEquals(selected[drop.Ordinal], drop))
                throw new InvalidOperationException("A resource getter requires the active normalization entry.");
            _productGetterAttempts.TryGetValue(drop.Ordinal, out int attempted);
            if (attempted != field - 1) throw new InvalidOperationException("Original resource getters cannot repeat or reorder.");
            _productGetterAttempts[drop.Ordinal] = attempted | field; // Mark BEFORE guards/getter native work.
            ValidateSource();
            HeldResource resource = null;
            foreach (HeldResource held in _resources)
                if (held.Drop.Ordinal == drop.Ordinal) { resource = held; break; }
            // HoldResult replaces its snapshot record to mark ResourceHeld, so
            // compare original parameters, not the earlier record's reference.
            if (resource == null || resource.Drop.ItemId != drop.ItemId || resource.Drop.Tier != drop.Tier ||
                resource.Drop.IsPlus != drop.IsPlus || resource.Drop.BonusGrade != drop.BonusGrade ||
                resource.Drop.LiftType != drop.LiftType || resource.Drop.Count != drop.Count ||
                Pointer(resource.Item) != resource.Pointer)
                throw new InvalidOperationException("The held resource or original parameters changed.");
            // Original metadata verifies this concrete class implements IItemBase.
            // IntegratedItem has similar scalar names but does not implement it.
            IntPtr expected = Il2CppClassPointerStore<Items>.NativeClassPtr;
            if (expected == IntPtr.Zero || IL2CPP.il2cpp_object_get_class(resource.Pointer) != expected)
                throw new InvalidOperationException("The selected resource is outside the supported IItemBase class profile.");
            ValidateSource();
            return resource.Item; // Exactly one original virtual getter at the call site.
        }
    }
}
