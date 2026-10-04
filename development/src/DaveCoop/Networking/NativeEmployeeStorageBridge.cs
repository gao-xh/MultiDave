using System;
using System.Collections.Generic;
using DaveCoop.Core.Cargo;
using DR.Save;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using IngredientDictionary = Il2CppSystem.Collections.Generic.Dictionary<int, IngredientsData>;

namespace DaveCoop.Networking
{
    // Typed ingredient execution primitive. No GUI/network producer calls it.
    // It consumes an already proved, frozen conversion; it never guesses policy.
    internal sealed class NativeEmployeeStorageBridge : ICargoIngredientStorageBackend
    {
        private sealed class OwnedReference
        {
            public Il2CppObjectBase Wrapper;
            public IntPtr Pointer, Handle;
            public bool FreeAttempted, Freed;
        }
        private static readonly Dictionary<CargoEmployeeReturnPlan, NativeEmployeeStorageBridge> Owners =
            new Dictionary<CargoEmployeeReturnPlan, NativeEmployeeStorageBridge>();
        private readonly ExpeditionCargoLedger _ledger;
        private readonly CargoEmployeeReturnPlan _plan;
        private readonly int _unityThreadId;
        private readonly List<OwnedReference> _references = new List<OwnedReference>(5);
        private IngredientsStorage _storage;
        private IngredientDictionary _dictionary;
        private SaveSystem _system;
        private SaveSystemGameDataManager _manager;
        private SaveData _save;
        private IntPtr _systemCached, _managerCached;
        private CargoReturnMaterializer _materializer;
        private bool _prepared, _dispatchInFlight, _addAttempted, _releaseAttempted, _released;
        public string PreparationFailure { get; private set; }
        public CargoMaterializerSnapshot Snapshot => _materializer?.Snapshot;

        private NativeEmployeeStorageBridge(ExpeditionCargoLedger ledger, CargoEmployeeReturnPlan plan, int thread)
        { _ledger = ledger; _plan = plan; _unityThreadId = thread; }

        public static bool TryPrepare(ExpeditionCargoLedger ledger, CargoEmployeeReturnPlan plan, int confirmedUnityThreadId,
            out NativeEmployeeStorageBridge bridge)
        {
            bridge = null;
            if (confirmedUnityThreadId < 1 || Environment.CurrentManagedThreadId != confirmedUnityThreadId || ledger == null || plan == null ||
                !ledger.OwnsEmployeeReturnPlan(plan, CargoReturnStage.Leased) ||
                (plan.Place != (int)SushiBar.Place.Main && plan.Place != (int)SushiBar.Place.Branch)) return false;
            if (Owners.TryGetValue(plan, out bridge) || Owners.Count >= CargoValues.MaxCaptures * CargoValues.MaxProductsPerCapture) return false;
            bridge = new NativeEmployeeStorageBridge(ledger, plan, confirmedUnityThreadId);
            Owners.Add(plan, bridge); // Retain partial/unknown native handle attempts too.
            try
            {
                bridge.Prepare(); bridge._prepared = true; return true;
            }
            catch (Exception error) { bridge.PreparationFailure = error.GetType().Name; return false; }
        }

        private void Prepare()
        {
            RequireThread();
            _storage = SingletonNoMono<IngredientsStorage>._s_Instance_k__BackingField;
            _system = Singleton<SaveSystem>._instance;
            if (Pointer(_storage) == IntPtr.Zero || Pointer(_system) == IntPtr.Zero) throw new InvalidOperationException("Existing storage/save provider missing.");
            Keep(_storage); Keep(_system);
            _manager = _system._GameDataManager;
            if (Pointer(_manager) == IntPtr.Zero) throw new InvalidOperationException("Existing game-save manager missing.");
            Keep(_manager);
            _save = _manager._Data_k__BackingField; _dictionary = _storage.m_Storage;
            Keep(_save); Keep(_dictionary);
            _systemCached = _system.m_CachedPtr; _managerCached = _manager.m_CachedPtr;
            _materializer = new CargoReturnMaterializer(_ledger, _plan, this);
            ValidateBindings();
            if (!_ledger.OwnsEmployeeReturnPlan(_plan, CargoReturnStage.Leased)) throw new InvalidOperationException("Bound return lease changed during preparation.");
        }

        public bool DispatchOnce(CargoStorageFacts freshFacts, double now)
        {
            if (Environment.CurrentManagedThreadId != _unityThreadId || !_prepared || _dispatchInFlight || _released || _releaseAttempted) return false;
            // Caller facts still require the real host adapter's proof. This
            // helper does not set capability, delta or save flags for the caller.
            _dispatchInFlight = true;
            try { return _materializer.DispatchOnce(freshFacts, now); }
            finally { _dispatchInFlight = false; }
        }

        void ICargoIngredientStorageBackend.ValidateTarget(CargoEmployeeReturnPlan plan)
        {
            RequireDispatch(plan); ValidateBindings(); RequireDispatch(plan);
        }

        void ICargoIngredientStorageBackend.AddIngredients(CargoEmployeeReturnPlan plan)
        {
            RequireDispatch(plan);
            if (!_materializer.IsInvokingAdd) throw new InvalidOperationException("Original Add requires its precise invocation window.");
            if (_addAttempted) throw new InvalidOperationException("Ingredient Add was already attempted.");
            _addAttempted = true; // Before guards or the original virtual/native call.
            ValidateBindings(); RequireDispatch(plan);
            _storage.Add(_plan.IngredientId, _plan.ParentId, _plan.Rank, _plan.FinalGrade, _plan.StorageCount, (SushiBar.Place)_plan.Place);
            // The coordinator preserves the returned call before post-validation.
            // A void return is not a bucket delta or durable save receipt.
        }

        private void RequireDispatch(CargoEmployeeReturnPlan plan)
        {
            RequireThread();
            if (!_prepared || _released || _releaseAttempted || !ReferenceEquals(plan, _plan) || _materializer == null ||
                !_materializer.IsDispatching || !_ledger.OwnsEmployeeReturnPlan(_plan, CargoReturnStage.EnteredUnknown))
                throw new InvalidOperationException("Original Add requires this entered per-product coordinator.");
        }

        private void ValidateBindings()
        {
            RequireThread();
            if (_released || _releaseAttempted || !Owners.TryGetValue(_plan, out NativeEmployeeStorageBridge owner) || !ReferenceEquals(owner, this))
                throw new InvalidOperationException("Return resource owner changed.");
            foreach (OwnedReference reference in _references)
                if (reference.Freed || reference.FreeAttempted || Pointer(reference.Wrapper) != reference.Pointer ||
                    reference.Handle == IntPtr.Zero || IL2CPP.il2cpp_gchandle_get_target(reference.Handle) != reference.Pointer)
                    throw new InvalidOperationException("A storage strong reference changed.");
            IntPtr expected = Il2CppClassPointerStore<IngredientsStorage>.NativeClassPtr;
            if (expected == IntPtr.Zero || IL2CPP.il2cpp_object_get_class(Pointer(_storage)) != expected ||
                Pointer(SingletonNoMono<IngredientsStorage>._s_Instance_k__BackingField) != Pointer(_storage) ||
                !_storage.m_IsLoaded || Pointer(_storage.m_Storage) != Pointer(_dictionary) ||
                Pointer(Singleton<SaveSystem>._instance) != Pointer(_system) || _systemCached == IntPtr.Zero || _system.m_CachedPtr != _systemCached ||
                !_system._IsInitialized_k__BackingField || !_system._IsLoadFinished_k__BackingField || !_system._IsGameLoaded_k__BackingField ||
                Pointer(_system._GameDataManager) != Pointer(_manager) || _managerCached == IntPtr.Zero || _manager.m_CachedPtr != _managerCached ||
                Pointer(_manager._Data_k__BackingField) != Pointer(_save))
                throw new InvalidOperationException("Existing storage, loaded state or game-save root changed.");
            // No active fish requirement: a confirmed catch may have left its pool.
            // Complete native dependency/quiescence and ABI coverage remain unproved.
        }

        private void Keep(Il2CppObjectBase value)
        {
            IntPtr pointer = Pointer(value);
            if (pointer == IntPtr.Zero) throw new InvalidOperationException("An absent dependency cannot be retained.");
            foreach (OwnedReference existing in _references) if (existing.Pointer == pointer) return;
            if (_references.Count >= 5) throw new InvalidOperationException("Storage reference budget exceeded.");
            var reference = new OwnedReference { Wrapper = value, Pointer = pointer };
            _references.Add(reference);
            reference.Handle = IL2CPP.il2cpp_gchandle_new(pointer, false);
            if (reference.Handle == IntPtr.Zero || IL2CPP.il2cpp_gchandle_get_target(reference.Handle) != pointer)
                throw new InvalidOperationException("Storage dependency retention unresolved.");
        }

        public bool ReleaseAfterSave()
        {
            // This window is independent of ledger stage: a synchronous callback
            // may supply late receipts while the original call is still running.
            if (Environment.CurrentManagedThreadId != _unityThreadId || _dispatchInFlight || _releaseAttempted) return false;
            CargoReturnItemSnapshot found = null;
            foreach (CargoReturnItemSnapshot item in _ledger.Snapshot.ReturnItems)
                if (item.CaptureId == _plan.CaptureId && item.ProductIndex == _plan.ProductIndex) { found = item; break; }
            if (found == null || !ReferenceEquals(found.Plan, _plan) || found.PlanFingerprint != _plan.Fingerprint || found.Stage != CargoReturnStage.SaveConfirmed) return false;
            _releaseAttempted = true;
            try
            {
                foreach (OwnedReference reference in _references)
                {
                    if (reference.Handle == IntPtr.Zero) continue;
                    reference.FreeAttempted = true; IL2CPP.il2cpp_gchandle_free(reference.Handle); reference.Freed = true;
                }
                _references.Clear();
                _storage = null; _dictionary = null; _system = null; _manager = null; _save = null;
                _released = true; Owners.Remove(_plan); return true;
            }
            catch (Exception) { return false; } // Unknown free is retained, never retried.
        }

        private void RequireThread()
        { if (Environment.CurrentManagedThreadId != _unityThreadId) throw new InvalidOperationException("Storage requires the confirmed Unity thread."); }
        private static IntPtr Pointer(Il2CppObjectBase value) => ReferenceEquals(value, null) ? IntPtr.Zero : value.Pointer;
    }
}
