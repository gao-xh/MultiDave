using System;
using System.Collections.Generic;
using DaveCoop.Core.Cargo;
using DaveCoop.Core.World;
using DR;
using DR.AI;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;

namespace DaveCoop.Networking
{
    // Actual typed selection primitives. There is no network/GUI entry yet:
    // trusted member/actor/world facts must come from the future host producer.
    // No direct LootBox/AddDrop/reward-commit/terminal calls. Grade/drop/pity
    // selection itself can consume RNG and write pending pity/dirty progress.
    internal sealed class NativeEmployeeFishSelectionBridge : IFishYieldSelectionBackend
    {
        private sealed class OwnedReference
        {
            public Il2CppObjectBase Wrapper;
            public IntPtr Pointer, Handle;
            public bool FreeAttempted, Freed;
        }
        private sealed class HeldResource
        {
            public FishSelectedDrop Drop;
            public IItemBase Item;
            public IntPtr Pointer;
        }
        // Retain uncertain work independently of a socket or scene controller.
        // Resolved owners can leave this map; the ledger retains their tombstone.
        private static readonly Dictionary<CargoSourceLease, NativeEmployeeFishSelectionBridge> Owners =
            new Dictionary<CargoSourceLease, NativeEmployeeFishSelectionBridge>();
        private const int MaxOwnedReferences = CargoValues.MaxProductsPerCapture + 5;
        private readonly ExpeditionCargoLedger _ledger;
        private readonly CargoSourceLease _lease;
        private readonly CargoSource _source;
        private readonly FishStateCapture _capture;
        private readonly FishLifecycleHooks _lifecycle;
        private readonly int _unityThreadId;
        private readonly List<OwnedReference> _references = new List<OwnedReference>(MaxOwnedReferences);
        private readonly List<HeldResource> _resources = new List<HeldResource>(CargoValues.MaxProductsPerCapture);
        private readonly HashSet<int> _resourceAttempts = new HashSet<int>();
        private FishAISystem _fish;
        private FishInfoData _info;
        private FishInteractionBody _body;
        private DataManager _data;
        private FishPlusItemPity _pity;
        private FishYieldRecipe _recipe;
        private FishYieldSelection _selection;
        private int _carvableCount;
        private int _mainAttempts;
        private int? _rolledBonus;
        private bool _gradeAttempted, _plusAttempted;
        private bool _prepared, _releaseAttempted, _released;
        public string PreparationFailure { get; private set; }
        public int HeldResourceCount => _resources.Count;
        public int OwnedHandleCount
        {
            get { int count = 0; foreach (OwnedReference reference in _references) if (reference.Handle != IntPtr.Zero) count++; return count; }
        }
        public FishYieldSelectionSnapshot Selection => _selection?.Snapshot;

        private NativeEmployeeFishSelectionBridge(ExpeditionCargoLedger ledger, CargoSourceLease lease,
            FishStateCapture capture, FishLifecycleHooks lifecycle, int unityThreadId)
        {
            _ledger = ledger; _lease = lease; _source = lease.Intent.Source;
            _capture = capture; _lifecycle = lifecycle; _unityThreadId = unityThreadId;
        }

        public static bool TryPrepare(ExpeditionCargoLedger ledger, CargoSourceLease lease, FishStateCapture capture,
            FishLifecycleHooks lifecycle, int confirmedUnityThreadId, FishYieldRecipeKind kind, out NativeEmployeeFishSelectionBridge bridge)
        {
            bridge = null;
            if (confirmedUnityThreadId < 1 || Environment.CurrentManagedThreadId != confirmedUnityThreadId ||
                ledger == null || lease == null || capture == null || lifecycle == null || lease.BoundPlayerId != 2 ||
                !ledger.IsSelectionReserved(lease) || !Enum.IsDefined(typeof(FishYieldRecipeKind), kind)) return false;
            if (Owners.TryGetValue(lease, out bridge)) return false; // Never prepare a second owner for this exact lease.
            if (Owners.Count >= CargoValues.MaxCaptures) return false;
            bridge = new NativeEmployeeFishSelectionBridge(ledger, lease, capture, lifecycle, confirmedUnityThreadId);
            Owners.Add(lease, bridge); // Keep partial/unknown strong handle acquisition alive too.
            try
            {
                bridge.Prepare(kind);
                bridge._prepared = true;
                return true;
            }
            catch (Exception error)
            {
                bridge.PreparationFailure = error.GetType().Name;
                return false;
            }
        }

        private void Prepare(FishYieldRecipeKind kind)
        {
            RequireThread();
            RequireLifecycle();
            if (!_capture.TryResolveNativeFish(_source.SceneEpoch, _source.EntityId, out _fish))
                throw new InvalidOperationException("The exact reserved fish is unavailable.");
            _info = _fish._FishInfoData;
            _body = _fish._fishInteractionBody;
            _data = Singleton<DataManager>._instance;
            _pity = SingletonNoMono<FishPlusItemPity>._s_Instance_k__BackingField;
            if (Pointer(_info) == IntPtr.Zero || Pointer(_data) == IntPtr.Zero || Pointer(_pity) == IntPtr.Zero ||
                (kind == FishYieldRecipeKind.OrdinaryPickup && Pointer(_body) == IntPtr.Zero))
                throw new InvalidOperationException("Already available selection resources are required.");
            _carvableCount = _info._CarvableCount_k__BackingField;
            // No GetFishData/provider getter is used to initialize missing state.
            _recipe = new FishYieldRecipe(_fish.FishDataTID, kind, _carvableCount,
                LootBox.k_NoneBonusGrade, (int)LootBox.AutoLiftedType.None);
            ValidateIdentity(); // Oversized recipes were rejected before grade/drop/pity selection.
            Keep(_fish); Keep(_info); Keep(_data); Keep(_pity);
            if (kind == FishYieldRecipeKind.OrdinaryPickup) Keep(_body);
            _selection = new FishYieldSelection(_ledger, _lease, _recipe, this);
            ValidateSource();
        }

        public bool SelectOnce(CargoSourceFacts freshFacts, double now)
        {
            if (Environment.CurrentManagedThreadId != _unityThreadId || !_prepared || _released || _releaseAttempted) return false;
            return _selection.SelectOnce(freshFacts, now);
        }

        void IFishYieldSelectionBackend.ValidateSource() => ValidateSource();
        int IFishYieldSelectionBackend.SelectPickupBonusGrade()
        {
            RequireDispatch();
            if (_recipe.Kind != FishYieldRecipeKind.OrdinaryPickup || _gradeAttempted || _mainAttempts != 0 || _plusAttempted)
                throw new InvalidOperationException("This grade selection is not available.");
            _gradeAttempted = true;
            int bonus = _body.GetPickUpGrade();
            _rolledBonus = bonus;
            return bonus;
        }
        int IFishYieldSelectionBackend.SelectMainItem(int fishDataTid, int tier)
        {
            RequireDispatch();
            if (fishDataTid != _recipe.FishDataTid || tier != _mainAttempts + 1 || tier > _recipe.MainTierCount || _plusAttempted ||
                (_recipe.Kind == FishYieldRecipeKind.OrdinaryPickup && !_rolledBonus.HasValue))
                throw new InvalidOperationException("Main selection arguments changed.");
            _mainAttempts++;
            return _data.GetFishDropItemID(fishDataTid, tier);
        }
        int IFishYieldSelectionBackend.SelectPlusItem(int fishDataTid, int selectionGrade)
        {
            RequireDispatch();
            int bonus = _recipe.Kind == FishYieldRecipeKind.OrdinaryPickup
                ? (_rolledBonus ?? throw new InvalidOperationException("Pickup bonus is unknown.")) : _recipe.NoneBonusGrade;
            if (fishDataTid != _recipe.FishDataTid || _plusAttempted || _mainAttempts != _recipe.MainTierCount ||
                selectionGrade != checked(bonus + 1)) throw new InvalidOperationException("Plus source or selection arguments changed.");
            _plusAttempted = true;
            return _pity.RollPlusItem(fishDataTid, selectionGrade);
        }
        void IFishYieldSelectionBackend.HoldSelectedResource(FishSelectedDrop drop)
        {
            RequireDispatch();
            FishSelectedDrop[] selected = _selection.Snapshot.Drops;
            if (drop == null || !drop.HasProduct || drop.Count != 1 || drop.LiftType != _recipe.LiftType ||
                _resources.Count >= CargoValues.MaxProductsPerCapture || drop.Ordinal != selected.Length - 1 ||
                !ReferenceEquals(selected[drop.Ordinal], drop) || !_resourceAttempts.Add(drop.Ordinal))
                throw new InvalidOperationException("Unsupported selected resource.");
            foreach (HeldResource held in _resources)
                if (held.Drop.Ordinal == drop.Ordinal) throw new InvalidOperationException("An original selection was already resolved.");
            IItemBase item = _data.GetItemV2(drop.ItemId); // Exactly once per positive original result.
            if (Pointer(item) == IntPtr.Zero) throw new InvalidOperationException("An original selected resource is missing.");
            // Retain the wrapper and selected arguments before handle readback.
            _resources.Add(new HeldResource { Drop = drop, Item = item, Pointer = Pointer(item) });
            Keep(item);
            ValidateSource();
        }

        private void RequireDispatch()
        {
            RequireThread();
            if (!_prepared || _selection == null || _selection.Snapshot.Stage != FishYieldSelectionStage.Selecting)
                throw new InvalidOperationException("A business selector requires the active entered coordinator.");
            ValidateSource();
        }

        private void ValidateSource()
        {
            RequireThread();
            if (_released || _releaseAttempted || !Owners.TryGetValue(_lease, out NativeEmployeeFishSelectionBridge owner) ||
                !ReferenceEquals(owner, this)) throw new InvalidOperationException("Selection owner is not current.");
            ValidateIdentity();
            foreach (OwnedReference reference in _references)
                if (reference.Freed || reference.FreeAttempted || Pointer(reference.Wrapper) != reference.Pointer ||
                    IL2CPP.il2cpp_gchandle_get_target(reference.Handle) != reference.Pointer)
                    throw new InvalidOperationException("A selected resource strong handle changed.");
            RequireLifecycle();
            HostEntityTarget? current = _capture.ResolveObservedPointer(_fish.Pointer.ToInt64());
            if (!MatchesSource(current)) throw new InvalidOperationException("Source generation changed during resource validation.");
        }

        private void ValidateIdentity()
        {
            RequireLifecycle();
            if (!_capture.TryResolveNativeFish(_source.SceneEpoch, _source.EntityId, out FishAISystem current) ||
                Pointer(current) != Pointer(_fish)) throw new InvalidOperationException("Reserved source binding changed.");
            HostEntityTarget? target = _capture.ResolveObservedPointer(_fish.Pointer.ToInt64());
            if (!target.HasValue || target.Value.SceneEpoch != _source.SceneEpoch || target.Value.EntityId != _source.EntityId ||
                target.Value.Generation != _source.LocalGeneration || target.Value.DataTid != _recipe.FishDataTid ||
                _fish.FishDataTID != _recipe.FishDataTid || Pointer(_fish._FishInfoData) != Pointer(_info) ||
                _info._TID_k__BackingField != _recipe.FishDataTid || _info._CarvableCount_k__BackingField != _carvableCount ||
                Pointer(Singleton<DataManager>._instance) != Pointer(_data) ||
                Pointer(SingletonNoMono<FishPlusItemPity>._s_Instance_k__BackingField) != Pointer(_pity) ||
                LootBox.k_NoneBonusGrade != _recipe.NoneBonusGrade ||
                (_recipe.Kind == FishYieldRecipeKind.OrdinaryPickup && Pointer(_fish._fishInteractionBody) != Pointer(_body)))
                throw new InvalidOperationException("Source generation, recipe or selection provider changed.");
            if (_recipe.Kind == FishYieldRecipeKind.OrdinaryPickup &&
                (Pointer(_body._ownerFish) != Pointer(_fish) || _body.m_CachedPtr == IntPtr.Zero))
                throw new InvalidOperationException("The pickup body is not owned by the exact source.");
            HostEntityTarget? after = _capture.ResolveObservedPointer(_fish.Pointer.ToInt64());
            RequireLifecycle();
            if (!MatchesSource(after) || !after.Value.Equals(target.Value))
                throw new InvalidOperationException("Source lifetime changed during identity reads.");
        }

        private bool MatchesSource(HostEntityTarget? target) => target.HasValue &&
            target.Value.SceneEpoch == _source.SceneEpoch && target.Value.EntityId == _source.EntityId &&
            target.Value.Generation == _source.LocalGeneration && target.Value.DataTid == _recipe.FishDataTid;

        private void RequireLifecycle()
        {
            if (!_lifecycle.OwnsActiveTracker || !_capture.UsesLifecycle(_lifecycle.Tracker))
                throw new InvalidOperationException("Source lifecycle observation is not the healthy active owner.");
        }

        private void Keep(Il2CppObjectBase value)
        {
            IntPtr pointer = Pointer(value);
            if (pointer == IntPtr.Zero) throw new InvalidOperationException("An absent reference cannot be held.");
            foreach (OwnedReference existing in _references)
                if (existing.Pointer == pointer) return; // Shared item resources do not merge ordered drop entries.
            if (_references.Count >= MaxOwnedReferences) throw new InvalidOperationException("Strong reference limit reached.");
            var reference = new OwnedReference { Wrapper = value, Pointer = pointer };
            _references.Add(reference); // Keep even a failed/uncertain handle attempt rooted.
            reference.Handle = IL2CPP.il2cpp_gchandle_new(pointer, false);
            if (reference.Handle == IntPtr.Zero || IL2CPP.il2cpp_gchandle_get_target(reference.Handle) != pointer)
                throw new InvalidOperationException("Strong reference acquisition is unresolved.");
        }

        // Unknown selection/capture survives Disconnect/return/scene cleanup.
        // No caller bool can release it: resolution must exist in the same ledger.
        public bool ReleaseAfterResolution()
        {
            if (Environment.CurrentManagedThreadId != _unityThreadId || _releaseAttempted) return false;
            CargoCaptureSnapshot found = null;
            foreach (CargoCaptureSnapshot capture in _ledger.Snapshot.Captures)
                if (capture.CaptureId == _lease.CaptureId) { found = capture; break; }
            if (found == null || found.OperationId != _lease.OperationId || found.Fingerprint != _lease.IntentFingerprint ||
                (found.Stage != CargoCaptureStage.Confirmed && found.Stage != CargoCaptureStage.NativeNotEntered)) return false;
            _releaseAttempted = true;
            try
            {
                foreach (OwnedReference reference in _references)
                {
                    if (reference.Handle == IntPtr.Zero) continue;
                    reference.FreeAttempted = true;
                    IL2CPP.il2cpp_gchandle_free(reference.Handle);
                    reference.Freed = true;
                }
                _resources.Clear(); _references.Clear(); _released = true;
                Owners.Remove(_lease);
                return true;
            }
            catch (Exception) { return false; } // Retain uncertain ownership; never repeat free.
        }

        private void RequireThread()
        {
            if (Environment.CurrentManagedThreadId != _unityThreadId) throw new InvalidOperationException("Native selection requires its confirmed Unity thread.");
        }
        private static IntPtr Pointer(Il2CppObjectBase value) => ReferenceEquals(value, null) ? IntPtr.Zero : value.Pointer;
    }
}
