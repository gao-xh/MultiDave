using System;
using System.Collections.Generic;
using System.Threading;
using DaveCoop.Core.Guest;
using DR.Save;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;

namespace DaveCoop.Networking
{
    // Actual native root primitives, deliberately not connected to a game or
    // network entry point. Native boundary/cache/output coverage remains unverified.
    internal sealed class NativeGuestShadowBridge : IGuestShadowBackend
    {
        public const int MaxOwnedHandles = 15;
        public const int MaxJsonCharactersPerRoot = 8 * 1024 * 1024;
        public const int MaxJsonCharactersPerLease = 16 * 1024 * 1024;
        private const int MaxVersionCharacters = 256;
        private static NativeGuestShadowBridge _processLease;
        private readonly GuestOutputFence _fence;
        private readonly List<OwnedReference> _references = new List<OwnedReference>(MaxOwnedHandles);
        private readonly bool[] _installAttempted = new bool[5], _restoreAttempted = new bool[5];
        private Managers _managers;
        private Roots _original, _detached;
        private NativeGuestInteractionShadowResult _interactionShadow;
        private NativeGuestInteractionBaseline _interactionBaseline;
        private DataStamp[] _originalStamps;
        private bool _claimAttempted, _claimed, _captureAttempted, _captured, _prepareAttempted, _prepared;
        private bool _fenceAttempted, _removeAttempted, _releaseAttempted, _released, _busy;
        private int _ownedHandleCount;
        private long _jsonCharacters, _exceptions, _unexpectedThreads;
        private string _lastReason = "Native pre-generation and quiescent boundaries are not verified.";

        private sealed class OwnedReference
        {
            public Il2CppObjectBase Wrapper;
            public IntPtr Handle, Pointer;
            public bool FreeAttempted, Freed;
        }

        private sealed class Managers
        {
            public SaveSystem System;
            public SaveSystemGameDataManager Game;
            public SaveSystemPlayerDataManager Player;
            public SaveSystemPhotoDataManager Photo;
            public SaveSystemUserOptionManager Options;
            public IntPtr SystemPointer, GamePointer, PlayerPointer, PhotoPointer, OptionsPointer;
            public IntPtr SystemCached, GameCached, PlayerCached, PhotoCached, OptionsCached;
            public bool GameNew, PlayerNew, PhotoNew, OptionsNew;
        }

        private sealed class Roots
        {
            public SaveData Game;
            public SavePlayerData Player;
            public SaveSystemPlayerDataManager.InstanceInteractionData Interaction;
            public SavePhotoData Photo;
            public SaveUserOptions Options;
        }

        // Scalar readback can detect some original-data changes. It cannot
        // certify unchanged private state or undo mutable cache/subtree changes.
        private sealed class DataStamp
        {
            public SaveDataBase Data;
            public string Version, BuildVersion;
            public long UpdatedAt;
            public bool Updated, Corrupted;
        }

        public Guid HostBindingId { get; }
        public Guid LeaseId { get; }
        public int UnityThreadId { get; }
        public string LastReason => Volatile.Read(ref _lastReason);
        public int OwnedNativeHandleCount => Volatile.Read(ref _ownedHandleCount);
        public long SerializedCharacters => Interlocked.Read(ref _jsonCharacters);
        public long ExceptionFailures => Interlocked.Read(ref _exceptions);
        public long UnexpectedThreads => Interlocked.Read(ref _unexpectedThreads);
        public bool RootsCaptured => _captured;
        public bool DetachedRootsPrepared => _prepared;
        public bool KnownInteractionReferencesDisjoint => _interactionShadow?.KnownReferencesDisjoint ?? false;
        public bool GuestStateIsolated => false;
        public bool NativePermission => false;
        public bool WorldAuthority => false;
        public bool CargoAuthority => false;
        public bool NativeCloneAbiVerified => false;
        public bool DeepCloneVerified => false;
        public bool RuntimeCachesIsolated => false;
        public bool InteractionSynchronized => false;

        public NativeGuestShadowBridge(int unityThreadId, Guid hostBindingId, Guid leaseId)
        {
            if (unityThreadId < 1 || Environment.CurrentManagedThreadId != unityThreadId)
                throw new ArgumentException("Construct the bridge on the confirmed Unity thread.", nameof(unityThreadId));
            if (hostBindingId == Guid.Empty) throw new ArgumentException("A local host binding is required.", nameof(hostBindingId));
            if (leaseId == Guid.Empty) throw new ArgumentException("A single-use local lease is required.", nameof(leaseId));
            UnityThreadId = unityThreadId; HostBindingId = hostBindingId; LeaseId = leaseId;
            _fence = new GuestOutputFence(unityThreadId, leaseId);
        }

        public bool TryClaimLease()
        {
            if (!MainThread() || _claimAttempted) return Reject("This bridge lease cannot be claimed again.");
            _claimAttempted = true;
            if (Volatile.Read(ref _processLease) != null)
                return Reject("Another native shadow lease still owns this process.");
            // Pre-boundary rejection reserves this instance only. It neither
            // patches nor pins, and must not retain a global inactive backend.
            _claimed = true;
            return true;
        }

        public bool BindingIsCurrent()
        {
            if (!LeaseCurrent()) return false;
            if (!_captured) return true;
            try { return ReferencesValid() && ManagersCurrent(); }
            catch (Exception) { return ExceptionFailure("Native manager identity could not be confirmed."); }
        }

        public bool CanEnterBoundary()
        {
            if (!LeaseCurrent()) return false;
            // A Room, loaded flag or zero known writers is not an original
            // game pre-generation boundary. No automatic native entry in 0.1.18.
            return Reject("No verified original native pre-generation boundary; transaction entry remains disabled.");
        }

        public bool InstallOutputFence()
        {
            if (!LeaseCurrent() || _fenceAttempted) return Reject("Output fence installation is single-use.");
            if (!CanEnterBoundary()) return false;
            if (Interlocked.CompareExchange(ref _processLease, this, null) != null)
                return Reject("Another native shadow bridge owns the active process fence.");
            // Own the managed backend before the first possible patch. Failed
            // native writes must not strand handles without restore metadata.
            _fenceAttempted = true;
            try { return _fence.Install() && _fence.Active && _fence.Healthy || Reject("Owned output fence could not be installed and confirmed."); }
            catch (Exception) { return ExceptionFailure("Output fence installation failed; cleanup outcome must be inspected."); }
        }

        public bool FenceIsHealthy()
        {
            if (!LeaseCurrent()) return false;
            try { return _fence.Active && _fence.Healthy; }
            catch (Exception) { return ExceptionFailure("Output fence health is unknown."); }
        }

        public bool FenceIsActive()
        {
            if (!LeaseCurrent()) return false;
            try { return _fence.Active; }
            catch (Exception) { return ExceptionFailure("Output fence active state is unknown."); }
        }

        public bool CaptureOriginal()
        {
            if (!BeginNativeWork(requireEntryBoundary: true)) return false;
            try
            {
                if (_captureAttempted) return Reject("Original root capture is single-use.");
                _captureAttempted = true;
                var system = Singleton<SaveSystem>._instance;
                RequireLive(system);
                if (!system._IsInitialized_k__BackingField || !system._IsLoadFinished_k__BackingField || !system._IsGameLoaded_k__BackingField)
                    return Reject("SaveSystem loading flags do not permit original root capture.");
                _managers = new Managers
                {
                    System = system, Game = system._GameDataManager, Player = system._PlayerDataManager,
                    Photo = system._PhotoDataManager, Options = system._UserOptionManager
                };
                RequireLive(_managers.Game); RequireLive(_managers.Player); RequireLive(_managers.Photo); RequireLive(_managers.Options);
                _managers.SystemPointer = Keep(system); _managers.SystemCached = system.m_CachedPtr;
                _managers.GamePointer = Keep(_managers.Game); _managers.GameCached = _managers.Game.m_CachedPtr;
                _managers.PlayerPointer = Keep(_managers.Player); _managers.PlayerCached = _managers.Player.m_CachedPtr;
                _managers.PhotoPointer = Keep(_managers.Photo); _managers.PhotoCached = _managers.Photo.m_CachedPtr;
                _managers.OptionsPointer = Keep(_managers.Options); _managers.OptionsCached = _managers.Options.m_CachedPtr;
                _managers.GameNew = _managers.Game._IsNewData_k__BackingField;
                _managers.PlayerNew = _managers.Player._IsNewData_k__BackingField;
                _managers.PhotoNew = _managers.Photo._IsNewData_k__BackingField;
                _managers.OptionsNew = _managers.Options._IsNewData_k__BackingField;
                _original = new Roots
                {
                    Game = _managers.Game._Data_k__BackingField, Player = _managers.Player._Data_k__BackingField,
                    Interaction = _managers.Player._InstanceData_k__BackingField,
                    Photo = _managers.Photo._Data_k__BackingField, Options = _managers.Options._Data_k__BackingField
                };
                Keep(_original.Game); Keep(_original.Player); Keep(_original.Interaction); Keep(_original.Photo); Keep(_original.Options);
                _originalStamps = new[] { CaptureStamp(_original.Game), CaptureStamp(_original.Player),
                    CaptureStamp(_original.Photo), CaptureStamp(_original.Options) };
                _captured = true;
                // Snapshot known original interaction state before the first
                // native serializer can run callbacks or mutate a child.
                _interactionBaseline = NativeGuestInteractionShadow.CaptureOriginal(
                    _original.Player, _original.Interaction, RequireCloneWindow);
                return CanEnterBoundary() && ReferencesValid() && ManagersCurrent() && OriginalScalarsUnchanged() && AllRoots(GuestShadowRootReadback.Original) && FenceReady();
            }
            catch (Exception) { return ExceptionFailure("Original root capture failed; owned references and fence remain retained."); }
            finally { _busy = false; }
        }

        public bool PrepareDetached()
        {
            if (!BeginNativeWork(requireEntryBoundary: true)) return false;
            try
            {
                if (_prepareAttempted || !_captured) return Reject("Detached root preparation requires a captured, unused lease.");
                _prepareAttempted = true;
                if (!ReferencesValid() || !ManagersCurrent() || !OriginalScalarsUnchanged() || !AllRoots(GuestShadowRootReadback.Original))
                    return Reject("Original manager/root identity changed before native cloning.");
                _detached = new Roots();
                _detached.Game = Clone(_original.Game, SaveDataType.GameData);
                _detached.Player = Clone(_original.Player, SaveDataType.PlayerData);
                _detached.Photo = Clone(_original.Photo, SaveDataType.PhotoData);
                _detached.Options = Clone(_original.Options, SaveDataType.UserOption);
                RequireCloneWindow();
                // Bind only the cloned player's declared containers, after
                // checking the original known baseline and mutable aliases.
                // This is not a complete source/cache isolation certificate.
                _interactionShadow = NativeGuestInteractionShadow.Prepare(
                    _original.Player, _original.Interaction, _detached.Player, _interactionBaseline, RequireCloneWindow);
                _detached.Interaction = _interactionShadow.Interaction;
                Keep(_detached.Interaction);
                RequireCloneWindow();
                if (!_interactionShadow.ValidateKnownBinding(RequireCloneWindow))
                    return Reject("Detached interaction binding could not be confirmed.");
                _prepared = true;
                return true;
            }
            catch (Exception) { return ExceptionFailure("Native detached preparation failed; it will not be rerolled or repeated."); }
            finally { _busy = false; }
        }

        public bool InstallRoot(GuestShadowRoot root)
        {
            if (!BeginNativeWork(requireEntryBoundary: true)) return false;
            try
            {
                int index = RootIndex(root);
                if (!_prepared || _installAttempted[index]) return Reject("Root installation requires prepared data and is single-use.");
                if (!ReferencesValid() || !ManagersCurrent() || !OriginalScalarsUnchanged() || ReadRootCore(root) != GuestShadowRootReadback.Original)
                    return Reject("Install will not overwrite a foreign or unknown root.");
                if (!CanEnterBoundary() || !FenceReady()) return false;
                if (_interactionShadow == null || !_interactionShadow.ValidateKnownBinding(RequireInteractionInstallationWindow))
                    return Reject("Prepared interaction binding changed before root installation.");
                if (!CanEnterBoundary() || !FenceReady()) return false;
                _installAttempted[index] = true;
                WriteRoot(root, _detached);
                return ReadRootCore(root) == GuestShadowRootReadback.Detached && FenceReady();
            }
            catch (Exception) { return ExceptionFailure("Root installation outcome is unknown; inspect exact readback before compensation."); }
            finally { _busy = false; }
        }

        public GuestShadowRootReadback ReadRoot(GuestShadowRoot root)
        {
            if (!LeaseCurrent() || !_captured) return GuestShadowRootReadback.Unknown;
            try { return ReferencesValid() ? ReadRootCore(root) : GuestShadowRootReadback.Unknown; }
            catch (Exception)
            {
                ExceptionFailure("Native root readback failed; no write or completion is inferred.");
                return GuestShadowRootReadback.Unknown;
            }
        }

        public bool ValidateRoots()
        {
            if (!BeginNativeWork()) return false;
            try { return _prepared && ReferencesValid() && ManagersCurrent() && OriginalScalarsUnchanged() &&
                    AllRoots(GuestShadowRootReadback.Detached) && FenceReady() && _interactionShadow != null &&
                    _interactionShadow.ValidateKnownReferences(RequireInteractionBindingWindow); }
            catch (Exception) { return ExceptionFailure("Installed root validation failed; fence and references remain retained."); }
            finally { _busy = false; }
        }

        public bool RestoreRoot(GuestShadowRoot root)
        {
            if (!BeginNativeWork()) return false;
            try
            {
                int index = RootIndex(root);
                if (!_captured || _restoreAttempted[index]) return Reject("Root compensation is single-use.");
                if (!ReferencesValid() || !ManagersCurrent()) return Reject("Changed managers will not be overwritten during restore.");
                GuestShadowRootReadback current = ReadRootCore(root);
                if (current == GuestShadowRootReadback.Original) return true;
                if (current != GuestShadowRootReadback.Detached) return Reject("Foreign or unknown root will not be overwritten during restore.");
                _restoreAttempted[index] = true;
                WriteRoot(root, _original);
                return ReadRootCore(root) == GuestShadowRootReadback.Original && FenceReady();
            }
            catch (Exception) { return ExceptionFailure("Root compensation outcome is unknown and will not be dispatched again."); }
            finally { _busy = false; }
        }

        public bool ConfirmOriginalManagersAndRoots()
        {
            if (!LeaseCurrent() || !_captured) return false;
            try { return ReferencesValid() && ManagersCurrent() && OriginalScalarsUnchanged() &&
                    AllRoots(GuestShadowRootReadback.Original) && (_interactionBaseline == null ||
                    _interactionBaseline.ConfirmOriginalKnownGraph(RequireOriginalReadWindow)); }
            catch (Exception) { return ExceptionFailure("Original roots/managers could not be confirmed."); }
        }

        public bool HasQuiescentBoundary()
        {
            if (!LeaseCurrent()) return false;
            return Reject("Original native quiescence is not verified; restored roots do not permit fence/reference release.");
        }

        public bool RemoveOutputFence()
        {
            if (!LeaseCurrent() || _removeAttempted || !HasQuiescentBoundary() || !ConfirmOriginalManagersAndRoots() || !FenceReady()) return false;
            _removeAttempted = true;
            try { return _fence.Remove() && !_fence.Active && _fence.OwnHooksRemoved; }
            catch (Exception) { return ExceptionFailure("Owned fence removal is unknown; it will not be repeated."); }
        }

        public bool ReleaseReferences()
        {
            if (!LeaseCurrent() || _releaseAttempted || !HasQuiescentBoundary() || !ConfirmOriginalManagersAndRoots()) return false;
            try { if (_fence.Active || !_fence.OwnHooksRemoved) return Reject("References cannot be released while owned hooks remain."); }
            catch (Exception) { return ExceptionFailure("Fence removal is not confirmed; references remain retained."); }
            _releaseAttempted = true;
            try
            {
                for (int i = _references.Count - 1; i >= 0; i--)
                {
                    OwnedReference reference = _references[i];
                    if (reference.FreeAttempted) return Reject("A native handle release is unresolved and will not be repeated.");
                    reference.FreeAttempted = true;
                    IL2CPP.il2cpp_gchandle_free(reference.Handle);
                    reference.Freed = true;
                    Volatile.Write(ref _ownedHandleCount, _ownedHandleCount - 1);
                }
                _references.Clear(); _original = null; _detached = null; _managers = null; _originalStamps = null;
                _interactionShadow = null; _interactionBaseline = null;
                _captured = false; _prepared = false; _released = true;
                Interlocked.CompareExchange(ref _processLease, null, this);
                return true;
            }
            catch (Exception) { return ExceptionFailure("Native handle release is incomplete; surviving references remain owned."); }
        }

        private T Clone<T>(T original, SaveDataType type) where T : SaveDataBase
        {
            RequireCloneWindow();
            string json = null;
            try
            {
                json = SaveDataBase.Serialize<T>(original);
                if (string.IsNullOrEmpty(json) || json.Length > MaxJsonCharactersPerRoot || _jsonCharacters > MaxJsonCharactersPerLease - json.Length)
                    throw new InvalidOperationException("Native JSON exceeds the lease's bounded in-memory budget.");
                Interlocked.Add(ref _jsonCharacters, json.Length);
                RequireCloneWindow();
                T result = SaveDataBase.Deserialize<T>(type, json);
                Keep(result);
                if (!string.Equals(original.Version, result.Version, StringComparison.Ordinal))
                    throw new InvalidOperationException("Detached native data version changed.");
                RequireCloneWindow();
                return result;
            }
            finally { json = null; }
        }

        private void RequireCloneWindow()
        {
            if (!CanEnterBoundary() || !FenceReady() || !ReferencesValid() || !ManagersCurrent() || !OriginalScalarsUnchanged() || !AllRoots(GuestShadowRootReadback.Original))
                throw new InvalidOperationException("Native clone window lost its manager/root/fence binding.");
        }

        private void RequireInteractionInstallationWindow()
        {
            if (!CanEnterBoundary()) throw new InvalidOperationException("Interaction installation lost its original entry boundary.");
            RequireInteractionBindingWindow();
        }

        private void RequireOriginalReadWindow()
        {
            // Cleanup rereads originals both before unpatch and before freeing
            // handles afterwards. It does not require a still-active fence,
            // does not construct business data or change roots, and never
            // grants permission to save. Interop entry reads can value-box.
            if (!LeaseCurrent() || !ReferencesValid() || !ManagersCurrent() || !OriginalScalarsUnchanged() ||
                !AllRoots(GuestShadowRootReadback.Original))
                throw new InvalidOperationException("Original interaction readback lost its original manager/root binding.");
        }

        private void RequireInteractionBindingWindow()
        {
            // Roots can be mixed during installation or wholly detached during
            // active validation. This guard must not demand all-original roots
            // or the pre-generation entry phase after world execution begins.
            // Actual permission remains false independently of this read check.
            if (!LeaseCurrent() || !FenceReady() || !ReferencesValid() || !ManagersCurrent() || !OriginalScalarsUnchanged())
                throw new InvalidOperationException("Interaction validation lost its manager/reference/fence binding.");
        }

        private static DataStamp CaptureStamp(SaveDataBase data)
        {
            string version = data.Version, buildVersion = data.BuildVersion;
            if ((version != null && version.Length > MaxVersionCharacters) || (buildVersion != null && buildVersion.Length > MaxVersionCharacters))
                throw new InvalidOperationException("Original native version metadata exceeds its budget.");
            return new DataStamp { Data = data, Version = version, BuildVersion = buildVersion,
                UpdatedAt = data.lastUpdateLocalTime, Updated = data._IsUpdated_k__BackingField, Corrupted = data.isPrevDataCorrupted };
        }

        private bool OriginalScalarsUnchanged()
        {
            if (_originalStamps == null || _originalStamps.Length != 4) return false;
            if (_managers.Game._IsNewData_k__BackingField != _managers.GameNew || _managers.Player._IsNewData_k__BackingField != _managers.PlayerNew ||
                _managers.Photo._IsNewData_k__BackingField != _managers.PhotoNew || _managers.Options._IsNewData_k__BackingField != _managers.OptionsNew)
                return Reject("Original manager new-data state changed; it will not be blindly reset.");
            foreach (DataStamp stamp in _originalStamps)
                if (!string.Equals(stamp.Data.Version, stamp.Version, StringComparison.Ordinal) ||
                    !string.Equals(stamp.Data.BuildVersion, stamp.BuildVersion, StringComparison.Ordinal) ||
                    stamp.Data.lastUpdateLocalTime != stamp.UpdatedAt || stamp.Data._IsUpdated_k__BackingField != stamp.Updated || stamp.Data.isPrevDataCorrupted != stamp.Corrupted)
                    return Reject("An original native data scalar changed; pointer restoration does not prove unchanged progress.");
            return true;
        }

        private IntPtr Keep(Il2CppObjectBase value)
        {
            IntPtr pointer = Pointer(value);
            if (pointer == IntPtr.Zero || _references.Count >= MaxOwnedHandles)
                throw new InvalidOperationException("Native reference is absent or the handle budget is exhausted.");
            foreach (OwnedReference reference in _references)
                if (reference.Pointer == pointer) throw new InvalidOperationException("Original and detached native objects must be distinct.");
            IntPtr handle = IL2CPP.il2cpp_gchandle_new(pointer, false);
            if (handle == IntPtr.Zero) throw new InvalidOperationException("A native strong GC handle could not be acquired.");
            // Store ownership before readback: even uncertain handle outcomes
            // remain retained rather than being silently freed or forgotten.
            _references.Add(new OwnedReference { Wrapper = value, Pointer = pointer, Handle = handle });
            Volatile.Write(ref _ownedHandleCount, _references.Count);
            if (IL2CPP.il2cpp_gchandle_get_target(handle) != pointer)
                throw new InvalidOperationException("Native strong handle target did not match.");
            return pointer;
        }

        private bool ReferencesValid()
        {
            foreach (OwnedReference reference in _references)
                if (reference.Freed || reference.FreeAttempted || IL2CPP.il2cpp_gchandle_get_target(reference.Handle) != reference.Pointer || Pointer(reference.Wrapper) != reference.Pointer)
                    return Reject("An owned native strong reference changed or is unresolved.");
            return true;
        }

        private bool ManagersCurrent()
        {
            if (_managers == null) return false;
            var system = Singleton<SaveSystem>._instance;
            if (Pointer(system) != _managers.SystemPointer || system.m_CachedPtr == IntPtr.Zero || system.m_CachedPtr != _managers.SystemCached)
                return Reject("The SaveSystem native instance changed or was destroyed.");
            return ManagerMatches(system._GameDataManager, _managers.GamePointer, _managers.GameCached) &&
                ManagerMatches(system._PlayerDataManager, _managers.PlayerPointer, _managers.PlayerCached) &&
                ManagerMatches(system._PhotoDataManager, _managers.PhotoPointer, _managers.PhotoCached) &&
                ManagerMatches(system._UserOptionManager, _managers.OptionsPointer, _managers.OptionsCached);
        }

        private bool ManagerMatches(UnityEngine.Object manager, IntPtr pointer, IntPtr cached)
        {
            return Pointer(manager) == pointer && manager.m_CachedPtr != IntPtr.Zero && manager.m_CachedPtr == cached ||
                Reject("A bound native save manager changed or was destroyed.");
        }

        private GuestShadowRootReadback ReadRootCore(GuestShadowRoot root)
        {
            RootIndex(root);
            if (!ManagersCurrent()) return GuestShadowRootReadback.Foreign;
            IntPtr current = Pointer(GetCurrentRoot(root));
            if (current == IntPtr.Zero) return GuestShadowRootReadback.Unknown;
            if (current == Pointer(GetRoot(_original, root))) return GuestShadowRootReadback.Original;
            if (_detached != null && current == Pointer(GetRoot(_detached, root))) return GuestShadowRootReadback.Detached;
            return GuestShadowRootReadback.Foreign;
        }

        private Il2CppObjectBase GetCurrentRoot(GuestShadowRoot root)
        {
            switch (root)
            {
                case GuestShadowRoot.GameData: return _managers.Game._Data_k__BackingField;
                case GuestShadowRoot.PlayerData: return _managers.Player._Data_k__BackingField;
                case GuestShadowRoot.PlayerInteraction: return _managers.Player._InstanceData_k__BackingField;
                case GuestShadowRoot.PhotoData: return _managers.Photo._Data_k__BackingField;
                case GuestShadowRoot.UserOption: return _managers.Options._Data_k__BackingField;
                default: throw new ArgumentOutOfRangeException(nameof(root));
            }
        }

        private static Il2CppObjectBase GetRoot(Roots roots, GuestShadowRoot root)
        {
            switch (root)
            {
                case GuestShadowRoot.GameData: return roots.Game;
                case GuestShadowRoot.PlayerData: return roots.Player;
                case GuestShadowRoot.PlayerInteraction: return roots.Interaction;
                case GuestShadowRoot.PhotoData: return roots.Photo;
                case GuestShadowRoot.UserOption: return roots.Options;
                default: throw new ArgumentOutOfRangeException(nameof(root));
            }
        }

        private void WriteRoot(GuestShadowRoot root, Roots roots)
        {
            // Generated native field proxies, not original Data setters.
            switch (root)
            {
                case GuestShadowRoot.GameData: _managers.Game._Data_k__BackingField = roots.Game; break;
                case GuestShadowRoot.PlayerData: _managers.Player._Data_k__BackingField = roots.Player; break;
                case GuestShadowRoot.PlayerInteraction: _managers.Player._InstanceData_k__BackingField = roots.Interaction; break;
                case GuestShadowRoot.PhotoData: _managers.Photo._Data_k__BackingField = roots.Photo; break;
                case GuestShadowRoot.UserOption: _managers.Options._Data_k__BackingField = roots.Options; break;
                default: throw new ArgumentOutOfRangeException(nameof(root));
            }
        }

        private bool AllRoots(GuestShadowRootReadback expected)
        {
            for (int i = 1; i <= 5; i++) if (ReadRootCore((GuestShadowRoot)i) != expected) return false;
            return true;
        }

        private bool BeginNativeWork(bool requireEntryBoundary = false)
        {
            if (!LeaseCurrent() || _busy) return Reject("Native primitive requires its idle current lease.");
            if (requireEntryBoundary && !CanEnterBoundary()) return false;
            try
            {
                if (!FenceReady()) return Reject("Native primitive requires a healthy active owned output fence.");
                _busy = true; return true;
            }
            catch (Exception) { return ExceptionFailure("Native primitive fence state is unknown; no native action was dispatched."); }
        }

        private bool FenceReady() => LeaseCurrent() && _fenceAttempted && _fence.Active && _fence.Healthy && _fence.PendingCalls == 0;

        private bool LeaseCurrent()
        {
            NativeGuestShadowBridge owner = Volatile.Read(ref _processLease);
            return MainThread() && _claimed && !_released &&
                (ReferenceEquals(owner, this) || (!_fenceAttempted && ReferenceEquals(owner, null))) ||
                Reject("Native shadow lease/thread binding is not current.");
        }

        private bool MainThread()
        {
            if (Environment.CurrentManagedThreadId == UnityThreadId) return true;
            Interlocked.Increment(ref _unexpectedThreads);
            return Reject("Native access was rejected before dereference on a different managed thread.");
        }

        private static void RequireLive(UnityEngine.Object value)
        {
            if (Pointer(value) == IntPtr.Zero || value.m_CachedPtr == IntPtr.Zero)
                throw new InvalidOperationException("A native save manager is unavailable.");
        }

        private static IntPtr Pointer(Il2CppObjectBase value) => ReferenceEquals(value, null) ? IntPtr.Zero : value.Pointer;
        private static int RootIndex(GuestShadowRoot root)
        {
            int index = (int)root - 1;
            if (index < 0 || index >= 5) throw new ArgumentOutOfRangeException(nameof(root));
            return index;
        }
        private bool Reject(string reason) { Volatile.Write(ref _lastReason, reason); return false; }
        private bool ExceptionFailure(string reason)
        {
            // Never forward an original exception Message: it may contain save JSON.
            Interlocked.Increment(ref _exceptions); return Reject(reason);
        }
    }
}
