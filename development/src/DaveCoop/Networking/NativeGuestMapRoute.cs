using System;
using System.Collections.Generic;
using DaveCoop.Core.World;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using NativeLayers = Il2CppSystem.Collections.Generic.List<SceneMapLayerData>;
using NativeCaches = Il2CppSystem.Collections.Generic.List<SceneMapLayerDataCache>;
using NativeRoadmap = Il2CppSystem.Collections.Generic.Dictionary<int, SceneContext.SceneRoadmapData>;

namespace DaveCoop.Networking
{
    // Materializes only the declared route graph. Its actual hook lease owns
    // the adoption window; a wire candidate alone cannot enter these methods.
    // Native constructors/field ABI and complete world isolation are unverified.
    internal sealed class NativeGuestMapRoute
    {
        public const int MaxOwnedHandles = 106;
        public const int MaxRetainedRoutes = 32;
        private const int MaxNativeSteps = 8192;
        private static readonly object OwnersGate = new object();
        private static readonly Dictionary<Guid, NativeGuestMapRoute> Owners = new Dictionary<Guid, NativeGuestMapRoute>();
        private readonly NativeGuestMapLease _source;
        private readonly MapRouteSelection _route;
        private readonly MapRouteScene[] _chain;
        private readonly List<Reference> _references = new List<Reference>();
        private readonly SceneMapLayerData[] _layers;
        private readonly SceneMapLayerDataCache[] _caches;
        private readonly SceneContext.SceneRoadmapData[] _roads;
        private readonly bool[] _writesAttempted = new bool[6];
        private readonly SceneContext _context;
        private IntPtr _contextPointer;
        private long _ownedLayerListPointer;
        private NativeLayers _layerList;
        private NativeCaches _cacheList;
        private NativeRoadmap _roadmap;
        private Il2CppSystem.Collections.Generic.GenericEqualityComparer<int> _comparer;
        private IntPtr[] _originalRoots;
        private float _originalHeight;
        private bool _ownerRetained, _prepareAttempted, _prepared, _installAttempted, _installed, _busy, _failed;
        private int _steps;
        private string _reason = "Awaiting the actual guest route window.";

        private sealed class Reference
        {
            public IntPtr Pointer, Handle;
            public Il2CppObjectBase Wrapper;
        }
        private sealed class Rejected : InvalidOperationException
        { public string Reason { get; } public Rejected(string reason) { Reason = reason; } }

        public int UnityThreadId { get; }
        public Guid LeaseId { get; }
        public string RouteFingerprint { get; }
        public bool Prepared => _prepared;
        public bool InstallAttempted => _installAttempted;
        public bool Installed => _installed;
        public bool Failed => _failed;
        public string LastReason => _reason;
        public int OwnedHandleCount => _references.Count;
        public bool NativeFieldAbiVerified => false;
        public bool NativeAllocationAbiVerified => false;
        public bool PartialConstructorAllocationRetentionVerified => false;
        public bool CompleteMapGraphVerified => false;
        public bool GuestStateIsolated => false;
        public bool WorldAuthority => false;
        public bool CargoAuthority => false;
        internal bool OwnsLayerList(long pointer) => pointer != 0 && pointer == _ownedLayerListPointer;

        public NativeGuestMapRoute(NativeGuestMapLease source, MapRouteSelection route)
        {
            if (ReferenceEquals(source, null)) throw new ArgumentNullException(nameof(source));
            if (source.UnityThreadId < 1 || source.LeaseId == Guid.Empty ||
                Environment.CurrentManagedThreadId != source.UnityThreadId || ReferenceEquals(source.Context, null))
                throw new ArgumentException("The route requires its actual source lease and confirmed Unity thread.", nameof(source));
            _route = MapSelections.CopyRoute(route);
            RouteFingerprint = MapSelections.FingerprintRoute(_route);
            UnityThreadId = source.UnityThreadId; LeaseId = source.LeaseId;
            _source = source; _context = source.Context;
            var byId = new Dictionary<int, MapRouteScene>();
            foreach (MapRouteScene scene in _route.Scenes) byId.Add(scene.SceneId, scene);
            _chain = new MapRouteScene[_route.Scenes.Length];
            int sceneId = _route.EntrySceneId;
            for (int i = 0; i < _chain.Length; i++)
            { _chain[i] = byId[sceneId]; sceneId = _chain[i].NextSceneId; }
            _layers = new SceneMapLayerData[_chain.Length];
            _caches = new SceneMapLayerDataCache[_chain.Length];
            _roads = new SceneContext.SceneRoadmapData[_chain.Length];
            if (!source.TryBindRoute(this)) throw new InvalidOperationException("The actual route lease is already bound.");
        }

        public bool TryPrepare()
        {
            if (!Enter()) return false;
            try
            {
                if (_prepareAttempted) return Reject("Route preparation is single-use.");
                RequireWindow();
                RetainOwner();
                _prepareAttempted = true;
                _contextPointer = Read(() => Pointer(_context));
                Exact(_context);
                Keep(_context);
                CaptureOriginalRoots();
                // Each ordinary allocation is explicitly held before wrapping
                // and before its post-call source check. No game record ctor.
                for (int i = 0; i < _chain.Length; i++)
                {
                    _layers[i] = Allocate(ptr => new SceneMapLayerData(ptr));
                    _caches[i] = Allocate(ptr => new SceneMapLayerDataCache(ptr));
                    _roads[i] = Allocate(ptr => new SceneContext.SceneRoadmapData(ptr));
                    CopyLayer(_layers[i], _chain[i]);
                    CopyLayer(_caches[i], _chain[i]);
                    int index = i;
                    Step(() => _caches[index].IsSceneLoaded = false);
                    Step(() => _caches[index].PreloadAndNotUnloadable = _chain[index].PreloadAndNotUnloadable);
                    Step(() => _roads[index].sceneID = _chain[index].SceneId);
                    Step(() => _roads[index].offset = _chain[index].Offset);
                }
                for (int i = 0; i < _chain.Length; i++)
                {
                    int index = i;
                    Step(() => _roads[index].previous = index == 0 ? null : _roads[index - 1]);
                    Step(() => _roads[index].next = index + 1 == _chain.Length ? null : _roads[index + 1]);
                }
                _comparer = Allocate(ptr => new Il2CppSystem.Collections.Generic.GenericEqualityComparer<int>(ptr));
                var comparer = Read(() => new Il2CppSystem.Collections.Generic.IEqualityComparer<int>(Pointer(_comparer)));
                // These are real native BCL constructors. If a ctor throws
                // before returning its wrapper, its internal allocation cannot
                // be claimed retained. Known references/owner remain retained.
                RequireWindow(); _layerList = Keep(new NativeLayers(_chain.Length)); RequireWindow();
                _ownedLayerListPointer = Read(() => Pointer(_layerList).ToInt64());
                RequireWindow(); _cacheList = Keep(new NativeCaches(_chain.Length)); RequireWindow();
                RequireWindow(); _roadmap = Keep(new NativeRoadmap(_chain.Length, comparer)); RequireWindow();
                Exact(_layerList); Exact(_cacheList); Exact(_roadmap);
                for (int i = 0; i < _chain.Length; i++)
                {
                    int index = i;
                    Step(() => _layerList.Add(_layers[index]));
                    Step(() => _cacheList.Add(_caches[index]));
                    Step(() => _roadmap.Add(_chain[index].SceneId, _roads[index]));
                }
                VerifyPrepared(requireUnloaded: true);
                RequireOriginalRoots();
                RequireWindow();
                _prepared = true;
                _reason = "Declared route graph prepared; native effects and complete map isolation remain unverified.";
                return true;
            }
            catch (Rejected error) { return Reject(error.Reason); }
            catch (Exception) { return Reject("Native route preparation outcome is unknown; retained allocations will not be retried."); }
            finally { _busy = false; }
        }

        public bool TryInstall()
        {
            if (!Enter()) return false;
            try
            {
                if (!_prepared || _installAttempted) return Reject("Route installation requires its prepared, unused lease.");
                RequireWindow(); RequireOriginalRoots(); VerifyPrepared(requireUnloaded: true);
                _installAttempted = true;
                Install(0, () => _context.sceneLayerDataList = _layerList);
                Install(1, () => _context.selectedMapLayerCacheList = _cacheList);
                Install(2, () => _context.m_SceneRoadmap = _roadmap);
                Install(3, () => _context._firstData_k__BackingField = _roads[0]);
                Install(4, () => _context._lastData_k__BackingField = _roads[_roads.Length - 1]);
                Install(5, () => _context._TotalSceneHeight_k__BackingField = _route.TotalSceneHeight);
                VerifyInstalledRoots(); VerifyPrepared(requireUnloaded: true); RequireWindow();
                _installed = true;
                _reason = "Six declared route roots installed; world authority remains unverified.";
                return true;
            }
            catch (Rejected error) { return Reject(error.Reason); }
            catch (Exception) { return Reject("Native route installation outcome is unknown; partial roots and references are retained."); }
            finally { _busy = false; }
        }

        public bool ValidateInstalled()
        {
            if (!Enter()) return false;
            try
            {
                if (!_installed) return false;
                RequireWindow(installed: true);
                VerifyInstalledRoots(installed: true);
                // Local scene-loading flags may change after the original
                // loader resumes. Never overwrite them with host run state.
                VerifyPrepared(requireUnloaded: false, installed: true);
                RequireWindow(installed: true);
                return true;
            }
            catch (Rejected error) { return Reject(error.Reason); }
            catch (Exception) { return Reject("Installed route validation is unknown; no restoration or release is inferred."); }
            finally { _busy = false; }
        }

        private void CaptureOriginalRoots()
        {
            _originalRoots = new[] { KeepOptional(Read(() => _context.sceneLayerDataList)),
                KeepOptional(Read(() => _context.selectedMapLayerCacheList)), KeepOptional(Read(() => _context.m_SceneRoadmap)),
                KeepOptional(Read(() => _context._firstData_k__BackingField)), KeepOptional(Read(() => _context._lastData_k__BackingField)) };
            _originalHeight = Read(() => _context._TotalSceneHeight_k__BackingField);
            RequireOriginalRoots();
        }
        private void RequireOriginalRoots()
        {
            if (_originalRoots == null || Read(() => Pointer(_context.sceneLayerDataList)) != _originalRoots[0] ||
                Read(() => Pointer(_context.selectedMapLayerCacheList)) != _originalRoots[1] ||
                Read(() => Pointer(_context.m_SceneRoadmap)) != _originalRoots[2] ||
                Read(() => Pointer(_context._firstData_k__BackingField)) != _originalRoots[3] ||
                Read(() => Pointer(_context._lastData_k__BackingField)) != _originalRoots[4] ||
                !Same(Read(() => _context._TotalSceneHeight_k__BackingField), _originalHeight))
                throw new Rejected("The original route roots changed before installation.");
        }
        private void VerifyInstalledRoots(bool installed = false)
        {
            if (Read(() => Pointer(_context.sceneLayerDataList), installed) != Read(() => Pointer(_layerList), installed) ||
                Read(() => Pointer(_context.selectedMapLayerCacheList), installed) != Read(() => Pointer(_cacheList), installed) ||
                Read(() => Pointer(_context.m_SceneRoadmap), installed) != Read(() => Pointer(_roadmap), installed) ||
                Read(() => Pointer(_context._firstData_k__BackingField), installed) != Read(() => Pointer(_roads[0]), installed) ||
                Read(() => Pointer(_context._lastData_k__BackingField), installed) != Read(() => Pointer(_roads[_roads.Length - 1]), installed) ||
                !Same(Read(() => _context._TotalSceneHeight_k__BackingField, installed), _route.TotalSceneHeight))
                throw new Rejected("The installed route roots do not match their prepared objects.");
        }
        private void VerifyPrepared(bool requireUnloaded, bool installed = false)
        {
            if (Read(() => _layerList._size, installed) != _chain.Length || Read(() => _cacheList._size, installed) != _chain.Length ||
                Read(() => _roadmap._count, installed) != _chain.Length || Read(() => _roadmap._freeCount, installed) != 0 ||
                Read(() => Pointer(_roadmap._comparer), installed) != Read(() => Pointer(_comparer), installed))
                throw new Rejected("Prepared route container shape or comparer changed.");
            for (int i = 0; i < _chain.Length; i++)
            {
                int index = i;
                if (Read(() => Pointer(_layerList[index]), installed) != Read(() => Pointer(_layers[index]), installed) ||
                    Read(() => Pointer(_cacheList[index]), installed) != Read(() => Pointer(_caches[index]), installed) ||
                    Read(() => Pointer(_roadmap[_chain[index].SceneId]), installed) != Read(() => Pointer(_roads[index]), installed))
                    throw new Rejected("Prepared route container entries changed.");
                CheckLayer(_layers[index], _chain[index], installed);
                CheckLayer(_caches[index], _chain[index], installed);
                if ((requireUnloaded && Read(() => _caches[index].IsSceneLoaded, installed)) ||
                    Read(() => _caches[index].PreloadAndNotUnloadable, installed) != _chain[index].PreloadAndNotUnloadable ||
                    Read(() => _roads[index].sceneID, installed) != _chain[index].SceneId ||
                    !Same(Read(() => _roads[index].offset, installed), _chain[index].Offset) ||
                    Read(() => Pointer(_roads[index].previous), installed) != Read(() => Pointer(index == 0 ? null : _roads[index - 1]), installed) ||
                    Read(() => Pointer(_roads[index].next), installed) != Read(() => Pointer(index + 1 == _chain.Length ? null : _roads[index + 1]), installed))
                    throw new Rejected("Prepared route links or cache fields changed.");
            }
        }
        private void CopyLayer(SceneMapLayerData layer, MapRouteScene scene)
        {
            Step(() => layer.SceneID = scene.SceneId); Step(() => layer.TopYCoord = scene.TopY);
            Step(() => layer.BottomYCoord = scene.BottomY); Step(() => layer.LayerChar = scene.Layer);
            Step(() => layer.TopConnIdStr = scene.TopConnection); Step(() => layer.BottomConnIdStr = scene.BottomConnection);
            Step(() => layer.MapHeight = scene.MapHeight); Step(() => layer.Priority = scene.Priority);
            Step(() => layer.PreferenceWeight = scene.PreferenceWeight); Step(() => layer.SceneName = scene.SceneName);
            Step(() => layer.bSelected = true);
        }
        private void CheckLayer(SceneMapLayerData layer, MapRouteScene scene, bool installed)
        {
            if (Read(() => layer.SceneID, installed) != scene.SceneId || !Same(Read(() => layer.TopYCoord, installed), scene.TopY) ||
                !Same(Read(() => layer.BottomYCoord, installed), scene.BottomY) || Read(() => layer.LayerChar, installed) != scene.Layer ||
                !string.Equals(Read(() => layer.TopConnIdStr, installed), scene.TopConnection, StringComparison.Ordinal) ||
                !string.Equals(Read(() => layer.BottomConnIdStr, installed), scene.BottomConnection, StringComparison.Ordinal) ||
                !Same(Read(() => layer.MapHeight, installed), scene.MapHeight) || Read(() => layer.Priority, installed) != scene.Priority ||
                Read(() => layer.PreferenceWeight, installed) != scene.PreferenceWeight ||
                !string.Equals(Read(() => layer.SceneName, installed), scene.SceneName, StringComparison.Ordinal) ||
                !Read(() => layer.bSelected, installed)) throw new Rejected("Prepared route layer configuration changed.");
        }

        private T Allocate<T>(Func<IntPtr, T> wrap) where T : Il2CppObjectBase
        {
            IntPtr klass = Read(() => Il2CppClassPointerStore<T>.NativeClassPtr);
            if (klass == IntPtr.Zero) throw new Rejected("A required exact native map class is unavailable.");
            RequireWindow();
            IntPtr pointer = IL2CPP.il2cpp_object_new(klass);
            Reference reference = RetainRaw(pointer); // Retain before any post-call source check.
            T result = wrap(pointer); reference.Wrapper = result;
            RequireWindow(); Exact(result); RequireWindow();
            return result;
        }
        private T Keep<T>(T value) where T : Il2CppObjectBase
        {
            // A constructor has already returned. Holding its known result is
            // cleanup ownership, even if the source expired during that call.
            IntPtr pointer = Pointer(value);
            foreach (Reference reference in _references)
                if (reference.Pointer == pointer) return value;
            Reference owned = RetainRaw(pointer); owned.Wrapper = value;
            return value;
        }
        private IntPtr KeepOptional(Il2CppObjectBase value)
        { if (ReferenceEquals(value, null)) return IntPtr.Zero; Keep(value); return Read(() => Pointer(value)); }
        private Reference RetainRaw(IntPtr pointer)
        {
            if (pointer == IntPtr.Zero || _references.Count >= MaxOwnedHandles)
                throw new Rejected("A native map allocation is absent or exceeds its reference limit.");
            foreach (Reference reference in _references)
                if (reference.Pointer == pointer) throw new Rejected("A fresh native map allocation aliases an owned object.");
            var owned = new Reference { Pointer = pointer };
            _references.Add(owned); // Preserve a failed/unknown handle attempt too.
            owned.Handle = IL2CPP.il2cpp_gchandle_new(pointer, false);
            if (owned.Handle == IntPtr.Zero || IL2CPP.il2cpp_gchandle_get_target(owned.Handle) != pointer)
                throw new Rejected("A native map strong-reference outcome is unknown.");
            return owned;
        }
        private void Exact<T>(T value) where T : Il2CppObjectBase
        {
            IntPtr pointer = Read(() => Pointer(value));
            IntPtr klass = Read(() => Il2CppClassPointerStore<T>.NativeClassPtr);
            if (pointer == IntPtr.Zero || klass == IntPtr.Zero || Read(() => IL2CPP.il2cpp_object_get_class(pointer)) != klass)
                throw new Rejected("An unexpected native map class cannot enter this route profile.");
        }
        private T Read<T>(Func<T> action, bool installed = false)
        { RequireWindow(installed); T result = action(); RequireWindow(installed); return result; }
        private void Step(Action action)
        { RequireWindow(); action(); RequireWindow(); }
        private void Install(int index, Action action)
        {
            RequireWindow();
            if (_writesAttempted[index]) throw new Rejected("A route-root write cannot be repeated.");
            _writesAttempted[index] = true; action(); RequireWindow();
        }
        private void RequireWindow(bool installed = false)
        {
            if (_failed || !_busy || Environment.CurrentManagedThreadId != UnityThreadId || _source.UnityThreadId != UnityThreadId ||
                _source.LeaseId != LeaseId || !ReferenceEquals(_source.Context, _context) ||
                !(installed ? _source.AllowsInstalledValidation(this) : _source.IsRouteWindow(this)))
                throw new Rejected("The actual guest route lease/window is no longer current.");
            if (++_steps > MaxNativeSteps) throw new Rejected("Native route work exceeded its bounded step limit.");
            if (_contextPointer != IntPtr.Zero && Pointer(_context) != _contextPointer)
                throw new Rejected("The bound native route context changed.");
            if (_failed) throw new Rejected("The route source was invalidated during its window check.");
        }
        private bool Enter()
        {
            if (_failed || _busy || Environment.CurrentManagedThreadId != UnityThreadId)
                return Reject("Native route entry requires its idle, healthy confirmed Unity thread.");
            _busy = true; _steps = 0; return true;
        }
        private void RetainOwner()
        {
            lock (OwnersGate)
            {
                if (_ownerRetained) return;
                if (Owners.Count >= MaxRetainedRoutes || Owners.ContainsKey(LeaseId))
                    throw new Rejected("A retained map lease or process route limit prevents another allocation.");
                Owners.Add(LeaseId, this); _ownerRetained = true;
            }
        }
        private bool Reject(string reason) { _failed = true; _reason = reason; return false; }
        private static IntPtr Pointer(Il2CppObjectBase value) => ReferenceEquals(value, null) ? IntPtr.Zero : value.Pointer;
        private static bool Same(float left, float right) => BitConverter.SingleToInt32Bits(left) == BitConverter.SingleToInt32Bits(right);
        // No Dispose, restoration, unpatch or handle release: live consumers and
        // partial/unknown writes are retained until a separately verified boundary.
    }
}
