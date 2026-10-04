using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using DaveCoop.Core.Guest;
using DaveCoop.Core.Session;
using DR.Save;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;

namespace DaveCoop.Networking
{
    // One experimental guest initialization per process. The default plugin
    // path does not construct this controller, patch outputs, or swap roots.
    internal sealed class NativeGuestInitializationController
    {
        private const int MaxTrace = 32, MaxSourceHandles = 3;
        private readonly ManualLogSource _logger;
        private readonly int _installationThread;
        private readonly GuestOutputFence _fence;
        private readonly NativeGuestInitializationHooks _hooks;
        private readonly List<IntPtr> _sourceHandles = new List<IntPtr>();
        private readonly List<Il2CppObjectBase> _sourceReferences = new List<Il2CppObjectBase>();
        private int _unityThread, _candidateThread, _traces;
        private long _gamePointer, _loadedSystemPointer, _iteratorPointer;
        private GameBase _game, _loadGame, _factoryGame;
        private SaveSystem _loadedSystem;
        private Il2CppSystem.Collections.IEnumerator _returnedIterator;
        private GameBase._InitAfterSaveSystem_d__45 _iterator;
        private SessionPeer _peer;
        private string _room;
        private bool _awakeReturned, _loadStarted, _loadReturned, _allLoadReturned;
        private bool _factorySeen, _beforeMoveBusy, _insideFirstMove, _installAttempted, _nativeReleased;
        private volatile bool _failed;
        private long[] _managerIdentity, _coldCacheIdentity;
        private NativeGuestInitializationLease _lease;
        private NativeGuestShadowBridge _bridge;
        private GuestShadowTransaction _transaction;
        private string _lastTrace;

        public static NativeGuestInitializationController Current { get; private set; }
        public string Status { get; private set; } = "Awaiting natural guest initialization.";
        public bool Failed => _failed;
        public bool StartupHeld => !_nativeReleased;
        public bool RootsInstalled => _transaction?.Snapshot.RootShadowInstalled ?? false;
        public bool GuestStateIsolated => false;
        public bool NativePermission => false;
        public int SourceHandleCount => _sourceHandles.Count;
        internal GuestOutputFence Fence => _fence;

        private NativeGuestInitializationController(ManualLogSource logger, int installationThread)
        {
            _logger = logger; _installationThread = installationThread;
            _fence = new GuestOutputFence(installationThread, Guid.NewGuid(), GuestOutputFenceProfile.NaturalInitialization);
            _hooks = new NativeGuestInitializationHooks(this);
        }

        public static void Start(ManualLogSource logger, int installationThread)
        {
            if (Current != null) throw new Rejected("Guest initialization is process-only and single-use.");
            var controller = new NativeGuestInitializationController(logger, installationThread);
            Current = controller; // Retain partial patches and source ownership on failure.
            try
            {
                // Patching metadata does not certify Plugin.Load as a Unity
                // callback. No save/root/cache is read until actual Update.
                if (!controller._fence.Install()) throw new Rejected("Initial output fence is not healthy.");
                controller._hooks.Install();
                controller.Trace("ARMED", "Guest startup mode armed; restart is required to return to personal progress.");
            }
            catch (Exception error) { controller.Fail("Startup installation failed: " + error.GetType().Name); }
        }

        public void ConfirmUnityUpdate()
        {
            int thread = Environment.CurrentManagedThreadId;
            if (thread != _installationThread || (_candidateThread != 0 && thread != _candidateThread))
            { Fail("Startup and actual Unity callback threads differ."); return; }
            if (_unityThread == 0) _unityThread = thread;
            if (thread != _unityThread) { Fail("Actual Unity callback thread changed."); return; }
            if (_transaction != null && _nativeReleased && !_failed)
            {
                GuestShadowResult result = _transaction.ValidateActive();
                if (!result.Accepted) Fail("Temporary root validation failed: " + result.Reason);
            }
        }

        public bool BindPeer(SessionPeer peer)
        {
            if (_failed || !OnUnityThread() || peer == null) return false;
            SessionSnapshot state = peer.Snapshot;
            if (state.Role != SessionRole.Guest || state.Phase == SessionPhase.Closed ||
                !Guid.TryParseExact(state.RoomId, "N", out Guid room) || room == Guid.Empty)
            { Fail("Startup requires a live guest room binding."); return false; }
            if (_peer != null && (!ReferenceEquals(_peer, peer) || _room != state.RoomId))
            { Fail("A temporary-progress process cannot change its source room."); return false; }
            _peer = peer; _room = state.RoomId;
            Trace("BOUND", "Guest room bound; awaiting the fixed natural initialization iterator.");
            return true;
        }

        public void NetworkDisconnected()
        {
            if (_peer != null) Fail("Guest connection ended; temporary roots, fence and references remain retained until process exit.");
        }

        internal void BeforeAwake(GameBase game)
        {
            if (!AcceptCallbackThread()) return;
            if (ReferenceEquals(game, null) || !ReferenceEquals(_game, null))
            { Fail("GameBase Awake origin was missing or repeated."); return; }
            // Before actual Update, retain opaque CLR wrappers only. Pointer
            // itself can call IL2CPP and is not a managed identity accessor.
            _game = game;
        }
        internal void AfterAwake()
        {
            if (!AcceptCallbackThread()) return;
            if (ReferenceEquals(_game, null) || _awakeReturned) { Fail("GameBase Awake return was missing or repeated."); return; }
            _awakeReturned = true;
        }
        internal void BeforeLoadSaved(GameBase game)
        {
            if (!AcceptCallbackThread()) return;
            if (ReferenceEquals(_game, null) || ReferenceEquals(game, null) || _loadStarted || _factorySeen)
            { Fail("LoadSavedData did not follow the observed GameBase Awake."); return; }
            _loadGame = game; _loadStarted = true;
        }
        internal void AfterLoadSaved()
        {
            if (!AcceptCallbackThread()) return;
            if (!_loadStarted || _loadReturned) { Fail("LoadSavedData return was missing or repeated."); return; }
            _loadReturned = true;
        }
        internal void BeforeLoadAll(SaveSystem system)
        {
            if (!AcceptCallbackThread()) return;
            if (!_loadStarted || !ReferenceEquals(_loadedSystem, null) || ReferenceEquals(system, null))
            { Fail("LoadAllData source is missing, repeated or outside the startup lineage."); return; }
            _loadedSystem = system;
        }
        internal void AfterLoadAll()
        {
            if (!AcceptCallbackThread()) return;
            if (ReferenceEquals(_loadedSystem, null) || _allLoadReturned) { Fail("LoadAllData return was missing or repeated."); return; }
            _allLoadReturned = true;
        }
        internal void BeforeFactory(GameBase game)
        {
            if (!AcceptCallbackThread()) return;
            if (ReferenceEquals(_game, null) || !_loadStarted || ReferenceEquals(_loadedSystem, null) ||
                _factorySeen || ReferenceEquals(game, null))
            { Fail("InitAfterSaveSystem factory lacks the original load lineage."); return; }
            _factoryGame = game; _factorySeen = true;
        }
        internal void AfterFactory(Il2CppSystem.Collections.IEnumerator iterator)
        {
            if (!AcceptCallbackThread()) return;
            if (!_factorySeen || ReferenceEquals(iterator, null) || !ReferenceEquals(_returnedIterator, null))
            { Fail("Initialization factory did not return one fixed iterator."); return; }
            _returnedIterator = iterator;
        }

        internal bool AcceptCallbackThread()
        {
            if (_failed) return false;
            int thread = Environment.CurrentManagedThreadId;
            if (thread != _installationThread || (_unityThread != 0 && thread != _unityThread) ||
                (_candidateThread != 0 && thread != _candidateThread))
            { Fail("Natural initialization callback arrived on an unexpected thread."); return false; }
            _candidateThread = thread;
            return true;
        }

        // Called only by the typed MoveNext prefix, never from a DTO or GUI.
        internal bool BeforeMove(GameBase._InitAfterSaveSystem_d__45 iterator, ref bool result)
        {
            result = true;
            if (!AcceptCallbackThread()) return false;
            if (_beforeMoveBusy) { Fail("Natural initialization reentered its MoveNext prefix."); return false; }
            if (_unityThread == 0 || (!_nativeReleased && (_peer == null || !_awakeReturned || !_loadReturned ||
                !_allLoadReturned || ReferenceEquals(_returnedIterator, null))))
            { Trace("WAITING", "Holding natural initialization until actual Unity Update, paired load returns and the guest handshake are observed."); return false; }
            // The reentry guard precedes every native read, including Pointer,
            // class-store initialization and retaining native strong handles.
            _beforeMoveBusy = true;
            try
            {
                if (_nativeReleased)
                {
                    if (Pointer(iterator) != _iteratorPointer || _iteratorPointer == 0 || !ActiveSource())
                        throw new Rejected("Guest initialization source expired or its iterator changed after native execution began.");
                    return true;
                }
                if (!_factorySeen || _installAttempted)
                    throw new Rejected("Initialization iterator is unbound, late or already used.");
                _gamePointer = Pointer(_game);
                _loadedSystemPointer = Pointer(_loadedSystem);
                _iteratorPointer = Pointer(_returnedIterator);
                if (_gamePointer == 0 || Pointer(_loadGame) != _gamePointer || Pointer(_factoryGame) != _gamePointer ||
                    _loadedSystemPointer == 0 || _iteratorPointer == 0 || Pointer(iterator) != _iteratorPointer)
                    throw new Rejected("Natural startup wrappers do not identify one GameBase, SaveSystem and factory iterator.");
                RequireCurrentPreflight();
                _iterator = iterator;
                if (IL2CPP.il2cpp_object_get_class(iterator.Pointer) != Il2CppClassPointerStore<GameBase._InitAfterSaveSystem_d__45>.NativeClassPtr)
                    throw new Rejected("Unexpected natural initialization iterator class.");
                if (iterator.__1__state != 0 || iterator.__2__current != null || Pointer(iterator.__4__this) != _gamePointer)
                    throw new Rejected("The original initialization iterator has already advanced.");
                RequireCurrentPreflight();
                if (_fence.BlockedFileOperations != 0)
                    throw new Rejected("Initial loading attempted a blocked file copy or deletion.");
                _managerIdentity = ReadManagerIdentity(requireRoots: true);
                RequireCurrentPreflight();
                _coldCacheIdentity = ReadColdCaches();
                RequireCurrentPreflight();
                if (!Equal(_managerIdentity, ReadManagerIdentity(true)) || !Equal(_coldCacheIdentity, ReadColdCaches()))
                    throw new Rejected("Initial managers or cold caches changed during preflight.");
                RequireCurrentPreflight();
                KeepSource(_game); RequireCurrentPreflight();
                KeepSource(_returnedIterator); RequireCurrentPreflight();
                KeepSource(_loadedSystem); RequireCurrentPreflight();
                _insideFirstMove = true;
                if (!_fence.SealInitialization() || !_fence.InitializationSealed || !_fence.Healthy)
                    throw new Rejected("Initial loading cannot be sealed safely.");
                _lease = NativeGuestInitializationLease.Create(this, _unityThread, Guid.ParseExact(_room, "N"));
                _installAttempted = true;
                _bridge = new NativeGuestShadowBridge(_lease, _fence);
                _transaction = new GuestShadowTransaction(_bridge);
                GuestShadowResult installed = _transaction.Install();
                if (!installed.Accepted) throw new Rejected("Five-root bootstrap failed: " + installed.Reason);
                if (!WindowCurrent(_bridge)) throw new Rejected("Initial source window expired after root installation.");
                // Mark the irreversible consumer boundary before allowing the
                // original MoveNext. No disconnect or failure restores roots.
                _nativeReleased = true;
                Trace("ROOTS_INSTALLED", "Five temporary save roots installed; original cache initialization is continuing. Full guest isolation is unverified.");
                if (!ActiveSource()) throw new Rejected("Guest source expired before the original initializer was released.");
                return true;
            }
            catch (Rejected error) { Fail("Guest initialization blocked: " + error.Reason); return false; }
            catch (Exception error) { Fail("Guest initialization native read failed: " + error.GetType().Name); return false; }
            finally { _insideFirstMove = false; _beforeMoveBusy = false; }
        }

        internal void AfterMove(bool ranOriginal, bool result)
        {
            if (!AcceptCallbackThread() || !ranOriginal || !_nativeReleased) return;
            if (!ActiveSource()) { Fail("Native initialization returned with an expired source."); return; }
            if (!result) Trace("INITIALIZER_RETURNED", "The fixed native initialization iterator returned false; full cache graph and world adoption remain unverified.");
        }
        internal void OriginalException(Exception exception)
        {
            if (!AcceptCallbackThread()) return;
            if (exception != null) Fail("Original guest initialization exception: " + exception.GetType().Name);
        }

        internal bool BindBridge(NativeGuestInitializationLease lease, NativeGuestShadowBridge bridge)
        {
            if (!_insideFirstMove || !ReferenceEquals(lease, _lease) || bridge == null || _bridge != null) return false;
            _bridge = bridge;
            return true;
        }
        internal bool WindowCurrent(NativeGuestShadowBridge bridge)
        {
            if (!_insideFirstMove || _nativeReleased || _failed || !ReferenceEquals(bridge, _bridge) || !OnUnityThread() || !PeerCurrent()) return false;
            try
            {
                bool current = _fence.Healthy && _fence.InitializationSealed && _fence.BlockedFileOperations == 0 && Pointer(_iterator) == _iteratorPointer &&
                    _iterator.__1__state == 0 && _iterator.__2__current == null && Pointer(_iterator.__4__this) == _gamePointer &&
                    Equal(_managerIdentity, ReadManagerIdentity(false)) && Equal(_coldCacheIdentity, ReadColdCaches());
                return current && !_failed && PeerCurrent() && _fence.Healthy;
            }
            catch { return false; }
        }
        internal bool ActiveCurrent(NativeGuestShadowBridge bridge) => ReferenceEquals(bridge, _bridge) &&
            (_insideFirstMove ? WindowCurrent(bridge) : _nativeReleased && ActiveSource());
        private bool ActiveSource()
        {
            if (_failed || !OnUnityThread() || !PeerCurrent() || !_fence.Healthy || !_fence.InitializationSealed) return false;
            try
            {
                bool current = Pointer(_game) == _gamePointer && Equal(_managerIdentity, ReadManagerIdentity(false));
                return current && !_failed && PeerCurrent() && _fence.Healthy;
            }
            catch { return false; }
        }
        private bool PeerCurrent()
        {
            SessionSnapshot state = _peer?.Snapshot;
            return state != null && state.Role == SessionRole.Guest && state.Phase != SessionPhase.Closed && state.RoomId == _room;
        }
        private bool OnUnityThread() => _unityThread > 0 && Environment.CurrentManagedThreadId == _unityThread;

        private void RequireCurrentPreflight()
        {
            if (_failed || !OnUnityThread() || !PeerCurrent() || !_fence.Healthy)
                throw new Rejected("The guest source expired during initialization preflight.");
        }

        private long[] ReadManagerIdentity(bool requireRoots)
        {
            if (!OnUnityThread()) throw new Rejected("Native startup reads require actual Unity Update thread.");
            if (_game == null || _game.m_CachedPtr == IntPtr.Zero || Pointer(Singleton<GameBase>._instance) != _gamePointer)
                throw new Rejected("The observed GameBase is no longer the current live instance.");
            SaveSystem system = Singleton<SaveSystem>._instance;
            if (Pointer(system) == 0 || Pointer(system) != _loadedSystemPointer || system.m_CachedPtr == IntPtr.Zero)
                throw new Rejected("The loaded SaveSystem is no longer current.");
            var game = system._GameDataManager; var player = system._PlayerDataManager;
            var photo = system._PhotoDataManager; var option = system._UserOptionManager;
            var identities = new[] { Pointer(system), Pointer(game), Pointer(player), Pointer(photo), Pointer(option) };
            if (identities.Any(pointer => pointer == 0) || identities.Distinct().Count() != identities.Length)
                throw new Rejected("Four distinct loaded data managers are required.");
            if (requireRoots)
            {
                var roots = new[] { Pointer(game._Data_k__BackingField), Pointer(player._Data_k__BackingField),
                    Pointer(player._InstanceData_k__BackingField), Pointer(photo._Data_k__BackingField), Pointer(option._Data_k__BackingField) };
                if (roots.Any(pointer => pointer == 0) || roots.Distinct().Count() != roots.Length)
                    throw new Rejected("Natural loading has not supplied five distinct save roots.");
            }
            return identities;
        }

        private long[] ReadColdCaches()
        {
            if (!OnUnityThread() || Singleton<InGameManager>._instance != null || MissionManager._IsLoaded_k__BackingField)
                throw new Rejected("A loaded world or mission cache cannot enter cold bootstrap.");
            var stamp = new List<long>();
            var ingredients = SingletonNoMono<IngredientsStorage>._s_Instance_k__BackingField;
            stamp.Add(Pointer(ingredients));
            if (ingredients != null)
            {
                if (ingredients.m_IsLoaded) throw new Rejected("Ingredients are already loaded.");
                Empty(stamp, ingredients.m_Storage);
            }
            var ingame = SingletonNoMono<IngameSaveDataManager>._s_Instance_k__BackingField;
            stamp.Add(Pointer(ingame));
            if (ingame != null) Empty(stamp, ingame.ingameSaveDatas);
            var interior = SingletonNoMono<DR.InteriorStorage>._s_Instance_k__BackingField;
            stamp.Add(Pointer(interior));
            if (interior != null)
            {
                if (interior.m_IsLoaded || interior.m_GameDataManager != null) throw new Rejected("Interior storage already holds loaded data.");
                Empty(stamp, interior.m_InteriorDatas); Empty(stamp, interior.m_InteriorSubItems);
            }
            var missions = Singleton<MissionManager>._instance;
            stamp.Add(Pointer(missions));
            if (missions != null)
            {
                if (missions.m_MissionProcessRoutine != null || missions.m_MissionSequenceQueue != null)
                    throw new Rejected("An existing mission routine or sequence is outside this cold profile.");
                Empty(stamp, missions.m_MissionList); Empty(stamp, missions.m_ClearList);
                Empty(stamp, missions.m_ProcessedList); Empty(stamp, missions.m_MissionProcessQueue);
                Empty(stamp, missions.m_DeferredPostRoutineClear);
                Empty(stamp, missions.m_MissionEventListeners); Empty(stamp, missions.m_MissionEventPersistentListeners);
            }
            return stamp.ToArray();
        }
        private static void Empty<TKey, TValue>(List<long> stamp, Il2CppSystem.Collections.Generic.Dictionary<TKey, TValue> dictionary)
        {
            stamp.Add(Pointer(dictionary));
            if (dictionary == null) return;
            if (dictionary._count != 0 || dictionary._freeCount != 0 || dictionary._version != 0)
                throw new Rejected("An existing or previously used dictionary cannot enter cold bootstrap.");
            stamp.Add(Pointer(dictionary._entries)); stamp.Add(Pointer(dictionary._buckets));
        }
        private static void Empty<T>(List<long> stamp, Il2CppSystem.Collections.Generic.List<T> list)
        {
            stamp.Add(Pointer(list)); if (list == null) return;
            if (list._size != 0 || list._version != 0) throw new Rejected("A used list cannot enter cold bootstrap.");
            stamp.Add(Pointer(list._items));
        }
        private static void Empty<T>(List<long> stamp, Il2CppSystem.Collections.Generic.Queue<T> queue)
        {
            stamp.Add(Pointer(queue)); if (queue == null) return;
            if (queue._size != 0 || queue._version != 0 || queue._head != 0 || queue._tail != 0)
                throw new Rejected("A used queue cannot enter cold bootstrap.");
            stamp.Add(Pointer(queue._array));
        }
        private static void Empty<T>(List<long> stamp, Il2CppSystem.Collections.Generic.HashSet<T> set)
        {
            stamp.Add(Pointer(set)); if (set == null) return;
            if (set._count != 0 || set._lastIndex != 0 || set._version != 0)
                throw new Rejected("A used set cannot enter cold bootstrap.");
            stamp.Add(Pointer(set._slots)); stamp.Add(Pointer(set._buckets));
        }
        private void KeepSource(Il2CppObjectBase value)
        {
            if (ReferenceEquals(value, null) || _sourceHandles.Count >= MaxSourceHandles) throw new Rejected("Source reference limit.");
            _sourceReferences.Add(value);
            IntPtr handle = IL2CPP.il2cpp_gchandle_new(value.Pointer, false);
            if (handle == IntPtr.Zero) throw new Rejected("Source native reference could not be retained.");
            _sourceHandles.Add(handle);
        }
        private static long Pointer(Il2CppObjectBase value) => ReferenceEquals(value, null) ? 0 : value.Pointer.ToInt64();
        private static bool Equal(long[] left, long[] right) => left != null && right != null && left.SequenceEqual(right);
        private sealed class Rejected : Exception
        {
            public string Reason { get; }
            public Rejected(string reason) { Reason = reason; }
        }
        internal void Fail(string reason)
        {
            _failed = true;
            Trace("BLOCKED", reason + " Temporary state is retained; restart the process to leave guest startup mode.");
        }
        private void Trace(string stage, string message)
        {
            Status = message;
            string trace = stage + ": " + message;
            if (_lastTrace == trace || _traces >= MaxTrace) return;
            _lastTrace = trace; _traces++;
            try { _logger.LogInfo("DAVECOOP_GUEST_STARTUP_" + stage + ": " + message); }
            catch { _failed = true; }
        }
    }

    // This source cannot be constructed from flags or transport evidence.
    // Its controller retains the actual factory/first-MoveNext lineage.
    internal sealed class NativeGuestInitializationLease
    {
        private readonly NativeGuestInitializationController _source;
        public int UnityThreadId { get; }
        public Guid HostBindingId { get; }
        public Guid LeaseId => Fence.LeaseId;
        public GuestOutputFence Fence => _source.Fence;
        private NativeGuestInitializationLease(NativeGuestInitializationController source, int thread, Guid host)
        { _source = source; UnityThreadId = thread; HostBindingId = host; }
        internal static NativeGuestInitializationLease Create(NativeGuestInitializationController source, int thread, Guid host) =>
            new NativeGuestInitializationLease(source, thread, host);
        public bool TryBindBridge(NativeGuestShadowBridge bridge) => _source.BindBridge(this, bridge);
        public bool IsCurrentWindow(NativeGuestShadowBridge bridge) => _source.WindowCurrent(bridge);
        public bool AllowsActiveValidation(NativeGuestShadowBridge bridge) => _source.ActiveCurrent(bridge);
        public bool HasQuiescentBoundary(NativeGuestShadowBridge bridge) => false;
    }
}
