using System;
using System.Collections.Generic;
using DaveCoop.Core.Cargo;
using DR;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;

namespace DaveCoop.Networking
{
    // Mapping/count primitives only. Category, final-grade policy, capture
    // receipts and native entry permission still require real host producers.
    internal sealed partial class NativeEmployeeFishSelectionBridge : IFishYieldReturnMappingBackend
    {
        private sealed class ReturnResource
        {
            public FishReturnProductRequest Request;
            public Items Item;
            public IngredientsEntity Ingredients;
            public IntPtr ItemPointer, IngredientsPointer;
            public int? Tid, DataId, Rank, Type, IngredientTid, Count;
        }

        // This owner outlives the selected fish and its thirteen source holds.
        // Partial allocation and unknown calls are retained, never recreated.
        private sealed class ReturnMappingOwner
        {
            public readonly NativeEmployeeFishSelectionBridge Backend;
            public readonly DataManager Data;
            public readonly IntPtr DataPointer;
            public readonly List<ReturnResource> Resources = new List<ReturnResource>(CargoValues.MaxProductsPerCapture);
            public readonly List<OwnedReference> References = new List<OwnedReference>(1 + 2 * CargoValues.MaxProductsPerCapture);
            public bool ReleaseAttempted, Released;

            public ReturnMappingOwner(NativeEmployeeFishSelectionBridge backend, DataManager data)
            { Backend = backend; Data = data; DataPointer = Pointer(data); }

            public void Keep(Il2CppObjectBase value)
            {
                IntPtr pointer = Pointer(value);
                if (pointer == IntPtr.Zero) throw new InvalidOperationException("A return dependency is unavailable.");
                foreach (OwnedReference existing in References) if (existing.Pointer == pointer) return;
                if (References.Count >= 1 + 2 * CargoValues.MaxProductsPerCapture)
                    throw new InvalidOperationException("Return reference budget exceeded.");
                var reference = new OwnedReference { Wrapper = value, Pointer = pointer };
                References.Add(reference); // Root before even an uncertain handle attempt.
                reference.Handle = IL2CPP.il2cpp_gchandle_new(pointer, false);
                if (reference.Handle == IntPtr.Zero || IL2CPP.il2cpp_gchandle_get_target(reference.Handle) != pointer)
                    throw new InvalidOperationException("Return dependency retention is unresolved.");
            }

            public void Validate()
            {
                if (ReleaseAttempted || Released || DataPointer == IntPtr.Zero || Pointer(Data) != DataPointer ||
                    Pointer(Singleton<DataManager>._instance) != DataPointer)
                    throw new InvalidOperationException("The return provider changed.");
                foreach (OwnedReference reference in References)
                    if (reference.Handle == IntPtr.Zero || reference.FreeAttempted || reference.Freed ||
                        Pointer(reference.Wrapper) != reference.Pointer || IL2CPP.il2cpp_gchandle_get_target(reference.Handle) != reference.Pointer)
                        throw new InvalidOperationException("A return strong reference changed.");
                foreach (ReturnResource resource in Resources)
                {
                    if (Pointer(resource.Item) != resource.ItemPointer || resource.ItemPointer == IntPtr.Zero)
                        throw new InvalidOperationException("The mapped item changed.");
                    if (resource.Tid.HasValue && (resource.Tid.Value != resource.Request.ProductTid || resource.Item._TID_k__BackingField != resource.Tid.Value) ||
                        resource.DataId.HasValue && (resource.DataId.Value < 1 || resource.Item._ItemDataID_k__BackingField != resource.DataId.Value) ||
                        resource.Rank.HasValue && (resource.Rank.Value < 0 || resource.Item._ItemRank_k__BackingField != resource.Rank.Value) ||
                        resource.Type.HasValue && (resource.Type.Value != resource.Request.ItemType || resource.Item._ItemType_k__BackingField != resource.Type.Value))
                        throw new InvalidOperationException("A frozen return item scalar changed or is unsupported.");
                    if (!ReferenceEquals(resource.Ingredients, null) && (resource.IngredientsPointer == IntPtr.Zero || Pointer(resource.Ingredients) != resource.IngredientsPointer))
                        throw new InvalidOperationException("The mapped ingredient changed.");
                    // Conservative exact-ID profile. The observed candidate is
                    // retained before the core post-call guard can reject it.
                    if (resource.IngredientTid.HasValue && (!resource.DataId.HasValue ||
                        resource.IngredientTid.Value != resource.DataId.Value ||
                        resource.Ingredients._TID_k__BackingField != resource.IngredientTid.Value))
                        throw new InvalidOperationException("Ingredient identity differs from the queried data ID.");
                }
            }
        }

        private static readonly Dictionary<CargoSourceLease, ReturnMappingOwner> ReturnOwners =
            new Dictionary<CargoSourceLease, ReturnMappingOwner>();
        private readonly Dictionary<int, int> _returnStepAttempts = new Dictionary<int, int>();
        private ReturnMappingOwner _returnOwner;
        private bool _returnMappingInFlight;
        public FishReturnMappingSnapshot ReturnProducts => _selection?.ReturnMapping;

        public bool MapReturnProductsOnce(FishReturnCountMode[] perProductModes)
        {
            if (Environment.CurrentManagedThreadId != _unityThreadId || !_prepared || _released || _releaseAttempted || _returnMappingInFlight) return false;
            _returnMappingInFlight = true;
            try { return _selection.MapReturnOnce(perProductModes); }
            finally { _returnMappingInFlight = false; }
        }

        public CargoEmployeeReturnPlan CreateMappedReturnPlan(int index, string returnId, string policyFingerprint, int finalGrade, int place)
        {
            RequireThread();
            if (!_prepared || _returnMappingInFlight) throw new InvalidOperationException("An idle prepared selection is required.");
            // Cached CLR outputs remain readable after fish/source release.
            return _selection.CreateMappedReturnPlan(index, returnId, policyFingerprint, finalGrade, place);
        }

        void IFishYieldReturnMappingBackend.HoldReturnItem(FishReturnProductRequest request)
        {
            BeginReturnStep(request, 1);
            if (_returnOwner == null)
            {
                if (ReturnOwners.ContainsKey(_lease) || ReturnOwners.Count >= CargoValues.MaxCaptures)
                    throw new InvalidOperationException("This return owner cannot be allocated.");
                _returnOwner = new ReturnMappingOwner(this, _data);
                ReturnOwners.Add(_lease, _returnOwner);
                _returnOwner.Keep(_data);
            }
            ValidateReturnSource();
            var resource = new ReturnResource { Request = request };
            _returnOwner.Resources.Add(resource);
            resource.Item = _returnOwner.Data.GetItems(request.ProductTid);
            resource.ItemPointer = Pointer(resource.Item);
            _returnOwner.Keep(resource.Item);
            IntPtr expected = Il2CppClassPointerStore<Items>.NativeClassPtr;
            if (expected == IntPtr.Zero || IL2CPP.il2cpp_object_get_class(resource.ItemPointer) != expected)
                throw new InvalidOperationException("Return item class is outside the supported profile.");
            ValidateReturnSource();
        }

        int IFishYieldReturnMappingBackend.ReadReturnItemTid(FishReturnProductRequest request)
        { ReturnResource resource = ReturnResourceFor(request, 2); int value = resource.Item._TID_k__BackingField; resource.Tid = value; return value; }
        int IFishYieldReturnMappingBackend.ReadItemDataId(FishReturnProductRequest request)
        { ReturnResource resource = ReturnResourceFor(request, 4); int value = resource.Item._ItemDataID_k__BackingField; resource.DataId = value; return value; }
        int IFishYieldReturnMappingBackend.ReadItemRank(FishReturnProductRequest request)
        { ReturnResource resource = ReturnResourceFor(request, 8); int value = resource.Item._ItemRank_k__BackingField; resource.Rank = value; return value; }
        int IFishYieldReturnMappingBackend.ReadReturnItemType(FishReturnProductRequest request)
        { ReturnResource resource = ReturnResourceFor(request, 16); int value = resource.Item._ItemType_k__BackingField; resource.Type = value; return value; }

        void IFishYieldReturnMappingBackend.HoldIngredients(FishReturnProductRequest request, int itemDataId)
        {
            ReturnResource resource = ReturnResourceFor(request, 32);
            if (!resource.DataId.HasValue || resource.DataId.Value != itemDataId || itemDataId < 1)
                throw new InvalidOperationException("The queried ingredient source changed.");
            resource.Ingredients = _returnOwner.Data.GetIngredients(itemDataId);
            resource.IngredientsPointer = Pointer(resource.Ingredients);
            _returnOwner.Keep(resource.Ingredients);
            IntPtr expected = Il2CppClassPointerStore<IngredientsEntity>.NativeClassPtr;
            if (expected == IntPtr.Zero || IL2CPP.il2cpp_object_get_class(resource.IngredientsPointer) != expected)
                throw new InvalidOperationException("Ingredient class is outside the supported profile.");
            ValidateReturnSource();
        }

        int IFishYieldReturnMappingBackend.ReadIngredientTid(FishReturnProductRequest request)
        {
            ReturnResource resource = ReturnResourceFor(request, 64);
            int value = resource.Ingredients._TID_k__BackingField; // Inherited Ingredients.TID, not ItemsTID.
            resource.IngredientTid = value;
            return value;
        }

        int IFishYieldReturnMappingBackend.ExchangeWholeOnce(FishReturnProductRequest request)
        {
            ReturnResource resource = ReturnResourceFor(request, 128);
            if (request.CountMode != FishReturnCountMode.ExchangeWholeOnce || request.RawCount != 1)
                throw new InvalidOperationException("Unsupported count conversion request.");
            // This helper internally looks up Items again. Identical resource,
            // formula/config and complete semantics remain unproved.
            int value = ItemsUtils.ExchangeCountFromWholeItems(request.ProductTid, request.RawCount, request.RawGrade);
            resource.Count = value;
            return value;
        }

        private void BeginReturnStep(FishReturnProductRequest request, int step)
        {
            RequireThread();
            FishReturnMappingSnapshot mapping = _selection?.ReturnMapping;
            if (!_prepared || _released || _releaseAttempted || !_returnMappingInFlight || mapping == null ||
                mapping.Stage != FishReturnMappingStage.Mapping || request == null || !ReferenceEquals(mapping.CurrentRequest, request) ||
                !_ledger.IsSelectionEntered(_lease)) throw new InvalidOperationException("The exact return mapping entry is required.");
            _returnStepAttempts.TryGetValue(request.ProductIndex, out int attempted);
            if (attempted != step - 1) throw new InvalidOperationException("Return work cannot repeat or reorder.");
            _returnStepAttempts[request.ProductIndex] = attempted | step; // Before guards/class stores/lookup work.
            ValidateReturnSource();
        }

        private ReturnResource ReturnResourceFor(FishReturnProductRequest request, int step)
        {
            BeginReturnStep(request, step);
            foreach (ReturnResource resource in _returnOwner.Resources)
                if (ReferenceEquals(resource.Request, request)) return resource;
            throw new InvalidOperationException("The exact held return resource is unavailable.");
        }

        private void ValidateReturnSource()
        {
            ValidateSource();
            if (!_ledger.IsSelectionEntered(_lease)) throw new InvalidOperationException("Capture closed during return work.");
            if (_returnOwner != null)
            {
                if (!ReturnOwners.TryGetValue(_lease, out ReturnMappingOwner owner) || !ReferenceEquals(owner, _returnOwner) || !ReferenceEquals(owner.Backend, this))
                    throw new InvalidOperationException("Return ownership changed.");
                _returnOwner.Validate();
            }
            if (!_ledger.IsSelectionEntered(_lease)) throw new InvalidOperationException("Capture closed during dependency reads.");
        }

        // No live-fish reads here: all products must have same-ledger, matching
        // frozen plans and actual SaveConfirmed receipts before handles retire.
        public bool ReleaseReturnResourcesAfterSave()
        {
            if (Environment.CurrentManagedThreadId != _unityThreadId || _returnMappingInFlight || _returnOwner == null ||
                _returnOwner.ReleaseAttempted || !ReturnOwners.TryGetValue(_lease, out ReturnMappingOwner owner) ||
                !ReferenceEquals(owner, _returnOwner) || !ReferenceEquals(owner.Backend, this)) return false;
            FishReturnMappingSnapshot mapping = _selection.ReturnMapping;
            CargoLedgerSnapshot ledger = _ledger.Snapshot;
            if (mapping.Stage != FishReturnMappingStage.MappingReady || mapping.Results.Length == 0 || ledger.ReturnId == null ||
                ledger.ExpeditionId != _lease.Intent.ExpeditionId) return false;
            CargoCaptureSnapshot capture = null;
            foreach (CargoCaptureSnapshot candidate in ledger.Captures) if (candidate.CaptureId == _lease.CaptureId) { capture = candidate; break; }
            if (capture == null || capture.Stage != CargoCaptureStage.Confirmed || capture.OperationId != _lease.OperationId ||
                capture.Fingerprint != _lease.IntentFingerprint) return false;
            foreach (FishReturnProductMapping result in mapping.Results)
            {
                CargoReturnItemSnapshot item = null;
                foreach (CargoReturnItemSnapshot candidate in ledger.ReturnItems)
                    if (candidate.CaptureId == _lease.CaptureId && candidate.ProductIndex == result.Request.ProductIndex) { item = candidate; break; }
                CargoEmployeeReturnPlan plan = item?.Plan;
                if (item == null || item.Stage != CargoReturnStage.SaveConfirmed || item.BagMode != CargoBagMode.EmployeeVirtual ||
                    item.MemberId != _lease.Intent.MemberId || plan == null || item.PlanFingerprint != plan.Fingerprint ||
                    plan.ReturnId != ledger.ReturnId || plan.ExpeditionId != ledger.ExpeditionId || plan.MemberId != item.MemberId ||
                    plan.CaptureId != _lease.CaptureId || plan.ProductIndex != result.Request.ProductIndex ||
                    plan.RawProductFingerprint != result.Request.RawProductFingerprint || CargoValues.ProductFingerprint(item.Product) != plan.RawProductFingerprint ||
                    plan.IngredientId != result.IngredientId || plan.ParentId != result.ParentId || plan.Rank != result.Rank || plan.StorageCount != result.StorageCount) return false;
            }
            owner.ReleaseAttempted = true;
            try
            {
                foreach (OwnedReference reference in owner.References)
                {
                    if (reference.Handle == IntPtr.Zero) continue;
                    reference.FreeAttempted = true; IL2CPP.il2cpp_gchandle_free(reference.Handle); reference.Freed = true;
                }
                owner.References.Clear(); owner.Resources.Clear(); owner.Released = true;
                ReturnOwners.Remove(_lease); _returnOwner = null;
                return true;
            }
            catch (Exception) { return false; } // Unknown free is retained, never retried.
        }
    }
}
