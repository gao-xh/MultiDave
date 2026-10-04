using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DaveCoop.Core.Session;
using DaveCoop.Core.World;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using SceneHandle = UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<UnityEngine.ResourceManagement.ResourceProviders.SceneInstance>;

namespace DaveCoop.Networking
{
    // Native objects remain in this process-owned producer, never in Core/wire.
    // Construction alone is no capability: every use requires this exact token
    // to remain registered with its actual entry, native birth and iterator.
    internal sealed class NativeGuestControllerBirth
    {
        internal NativeGuestSceneController Producer { get; }
        internal object Entry { get; }
        internal IntPtr NativeClass { get; }
        internal string SceneName { get; }
        internal Il2CppSystem.Collections.IEnumerator Iterator { get; set; }
        internal long IteratorLife { get; set; }
        internal bool Retired { get; set; }
        public IGPSetController Controller { get; }
        public long ControllerPointer { get; }
        public IntPtr UnityPointer { get; }
        public int SceneHandle { get; }
        public long ControllerLife { get; }
        public long OwnerBoundary { get; }
        public long IteratorPointer { get; internal set; }
        internal NativeGuestControllerBirth(NativeGuestSceneController producer, object entry, IGPSetController controller,
            long pointer, IntPtr unityPointer, IntPtr nativeClass, int sceneHandle, string sceneName, long life, long ownerBoundary)
        { Producer = producer; Entry = entry; Controller = controller; ControllerPointer = pointer; UnityPointer = unityPointer;
            NativeClass = nativeClass; SceneHandle = sceneHandle; SceneName = sceneName; ControllerLife = life; OwnerBoundary = ownerBoundary; }
    }

    // Exact natural factory/returned-iterator/operation/Scene lineage for one
    // installed guest route. This is experimental native source wiring; no
    // full world, cache isolation, lifetime ABI or gameplay permission is proved.
    internal sealed class NativeGuestSceneController
    {
        public const int MaxOperations = 64, MaxIterators = 256, MaxControllers = 256;
        public const int MaxReferences = MaxOperations + MaxIterators + 2 * MaxControllers;
        public const int MaxNativeReads = 8192; // Per public callback/validation invocation, not per process lifetime.
        private const string Owner = Plugin.Id + ".guest-scene-source";
        private static NativeGuestSceneController _active;
        private readonly NativeGuestMapController _maps;
        private readonly Dictionary<long, Iterator> _iterators = new Dictionary<long, Iterator>();
        private readonly Dictionary<long, Operation> _operations = new Dictionary<long, Operation>();
        private readonly Dictionary<long, NativeGuestControllerBirth> _controllers = new Dictionary<long, NativeGuestControllerBirth>();
        private readonly Dictionary<int, long> _roads = new Dictionary<int, long>();
        private readonly Dictionary<int, SceneProof> _sceneRecords = new Dictionary<int, SceneProof>();
        private readonly List<Il2CppObjectBase> _references = new List<Il2CppObjectBase>();
        private readonly List<IntPtr> _handles = new List<IntPtr>();
        private readonly Stack<Scope> _scopes = new Stack<Scope>();
        private readonly HashSet<Load> _loadingCalls = new HashSet<Load>();
        private MapOriginRegistry _registry;
        private object _entry;
        private SceneLoader _loader;
        private SceneContext _context;
        private NativeGuestMapRoute _route;
        private MapChoiceSnapshot _choice;
        private long _ownerLife, _loaderPointer, _contextPointer, _roadmapPointer;
        private IntPtr _loaderUnityPointer;
        private bool _busy, _reading, _failed;
        private int _reads;
        private long _totalReads;
        private Harmony _harmony;

        private enum Kind { Level, Additive, ChildAdditive, Load }
        private sealed class Iterator
        {
            public Kind Kind;
            public object Entry;
            public Il2CppSystem.Collections.IEnumerator Native;
            public long Pointer, Life, OwnerLife, ArgumentPointer;
            public int SceneId;
            public string Key;
            public LoadSceneMode Mode;
            public bool Activate;
        }
        private sealed class Factory
        {
            public Kind Kind;
            public object Entry;
            public long Scope, OwnerLife, ArgumentPointer;
            public int SceneId;
            public string Key;
            public LoadSceneMode Mode;
            public bool Activate;
        }
        private sealed class Scope { public long Token; public string Key; public object Entry; public Iterator Iterator; public bool Move; }
        private sealed class SceneProof
        { public DR.GameScene Native; public long Pointer; public int Id, Type; public string Name; public bool Additive, Diving; }
        private sealed class Move { public long Scope; public Iterator Iterator; public bool Approved; }
        private sealed class Load
        { public long Scope, OwnerLife; public object Entry; public string Key; public LoadSceneMode Mode; public bool Activate; public int Priority; public SceneReleaseMode ReleaseMode; }
        private sealed class Operation
        {
            public AsyncOperationBase<SceneInstance> Native;
            public long Pointer, Life, OwnerLife;
            public int Version;
            public object Entry;
            public bool Completed;
            public int SceneHandle;
            public string Key;
        }

        public NativeGuestSceneController(NativeGuestMapController maps)
        { _maps = maps ?? throw new ArgumentNullException(nameof(maps)); }
        public int UnityThreadId => _maps.SceneUnityThreadId;
        public bool Available
        {
            get
            {
                if (Failed || _registry == null || !_registry.Healthy || _entry == null || _route == null ||
                    UnityThreadId <= 0 || Environment.CurrentManagedThreadId != UnityThreadId) return false;
                if (_busy || _reading) { SourceFailure("Guest scene availability check reentered."); return false; }
                _reading = true;
                try { return _maps.SceneEntryCurrent(_entry, true) && !Failed; }
                catch { SourceFailure("Guest scene availability check failed."); return false; }
                finally { _reading = false; }
            }
        }
        public bool Failed => _failed || _maps.SceneFailed;
        internal bool VerifyGenerationWindow()
        {
            if (Failed) throw Revoke("A failed guest generation cannot enter another native callback.");
            if (UnityThreadId > 0 && Environment.CurrentManagedThreadId != UnityThreadId)
                throw Revoke("A guest generation callback changed its confirmed thread.");
            if (_registry == null || _entry == null || _route == null || _registry.ActiveOwnerLife != _ownerLife) return false;
            if (!Available) throw Revoke("The installed guest generation source is no longer current.");
            return true;
        }
        public bool IsCurrentEntry(NativeGuestControllerBirth token) => token != null && ReferenceEquals(token.Producer, this) &&
            ReferenceEquals(token.Entry, _entry) && token.OwnerBoundary == _ownerLife &&
            _registry != null && _registry.ActiveOwnerLife == _ownerLife;
        public int RetainedReferences => _handles.Count;
        public long ObservedNativeReads => _totalReads;
        public bool NativeGenerationBound => false;
        public bool NativePermission => false;
        public bool WorldAuthority => false;
        public bool CargoAuthority => false;

        internal void Install()
        {
            if (_active != null) throw new InvalidOperationException("Another guest scene source owns this process.");
            _active = this;
            var targets = new List<(MethodInfo Method, string Before, string After, string Finally)>();
            Type iterator = typeof(Il2CppSystem.Collections.IEnumerator);
            Add(targets, typeof(SceneLoader), "CoChangeLevelSceneAsync", false, iterator,
                new[] { typeof(DR.GameScene), typeof(SceneTransitionType), typeof(bool), typeof(Il2CppSystem.Action) }, nameof(LevelBefore), nameof(FactoryAfter), nameof(FactoryFinally));
            Add(targets, typeof(SceneLoader), "LoadAdditiveScene", false, iterator,
                new[] { typeof(SceneContext.SceneRoadmapData) }, nameof(AdditiveBefore), nameof(FactoryAfter), nameof(FactoryFinally));
            Add(targets, typeof(SceneLoader), "coLoadAdditiveScene", false, iterator,
                new[] { typeof(int), typeof(Il2CppSystem.Action<AsyncOperationHandle>) }, nameof(ChildBefore), nameof(FactoryAfter), nameof(FactoryFinally));
            Add(targets, typeof(SceneLoader), "CoLoadSceneAsync", true, iterator,
                new[] { typeof(string), typeof(LoadSceneMode), typeof(bool) }, nameof(LoadFactoryBefore), nameof(FactoryAfter), nameof(FactoryFinally));
            foreach (Type type in new[] { typeof(SceneLoader._CoChangeLevelSceneAsync_d__114), typeof(SceneLoader._LoadAdditiveScene_d__116),
                typeof(SceneLoader._coLoadAdditiveScene_d__191), typeof(SceneLoader._CoLoadSceneAsync_d__108) })
                Add(targets, type, "MoveNext", false, typeof(bool), Type.EmptyTypes, nameof(MoveBefore), nameof(MoveAfter), nameof(MoveFinally));
            Add(targets, typeof(Addressables), "LoadSceneAsync", true, typeof(SceneHandle),
                new[] { typeof(Il2CppSystem.Object), typeof(LoadSceneMode), typeof(bool), typeof(int), typeof(SceneReleaseMode) },
                nameof(AddressablesBefore), nameof(AddressablesAfter), nameof(AddressablesFinally));
            Add(targets, typeof(SceneLoader), "OnSceneUnloadedCustom", false, typeof(void), new[] { typeof(Scene) }, nameof(UnloadedBefore), null, null);
            foreach (string name in new[] { "OnSceneLoaded", "OnSceneLoadedCustom" })
                Add(targets, typeof(SceneLoader), name, false, typeof(void), new[] { typeof(Scene), typeof(LoadSceneMode) }, nameof(LoadedBefore), null, null);
            _harmony = new Harmony(Owner);
            foreach (var target in targets) _harmony.Patch(target.Method, prefix: Hook(target.Before), postfix: Hook(target.After), finalizer: Hook(target.Finally));
            if (!targets.All(target => Harmony.GetPatchInfo(target.Method)?.Owners.Contains(Owner) == true))
                throw new InvalidOperationException("Guest scene source hooks are incomplete.");
        }

        internal void BeginEntry(object identity, SceneLoader loader)
        {
            if (_busy || _reading || _scopes.Count != 0) throw Revoke("A new entry overlapped a live native scene scope.");
            _entry = identity; _loader = loader; _route = null; _choice = null; _context = null; _roads.Clear(); _sceneRecords.Clear();
            Execute(() =>
            {
                if (_registry == null) _registry = new MapOriginRegistry(UnityThreadId);
                _loaderPointer = Read(() => Pointer(loader)); _loaderUnityPointer = Read(() => loader.m_CachedPtr);
                RequireStatus(_registry.BeginOwner(_loaderPointer, out _ownerLife));
                foreach (NativeGuestControllerBirth old in _controllers.Values) old.Retired = true;
                // Completed and retired operation identities remain bounded process
                // tombstones; old entries are never polled/rebound to this owner.
                return true;
            }, installed: false);
        }
        internal void RegisterInitialIterator(object identity, Il2CppSystem.Collections.IEnumerator iterator)
        {
            Execute(() =>
            {
                if (!ReferenceEquals(identity, _entry)) throw Revoke("Initial iterator has another entry.");
                long pointer = Read(() => Pointer(iterator));
                Exact<SceneLoader._CoChangeSceneAsync_d__111>(iterator);
                RequireStatus(_registry.RegisterIterator(pointer, _ownerLife, out long life));
                if (_iterators.ContainsKey(pointer)) throw Revoke("Initial iterator was already registered.");
                Keep(iterator);
                _iterators.Add(pointer, new Iterator { Entry = identity, Native = iterator, Pointer = pointer, Life = life, OwnerLife = _ownerLife });
                return true;
            }, installed: false);
        }
        internal void EnterInitialMove(object identity, Il2CppObjectBase iterator, string expectedKey, out long scope)
        {
            long captured = 0;
            Execute(() =>
            {
                long pointer = Read(() => Pointer(iterator));
                if (!ReferenceEquals(identity, _entry) || !_iterators.TryGetValue(pointer, out Iterator item) || !ReferenceEquals(item.Entry, _entry))
                    PushUnknown(out captured);
                else { RequireStatus(_registry.EnterMoveNext(pointer, item.Life, out captured)); Push(captured, expectedKey, move: true); }
                return true;
            }, installed: false);
            scope = captured;
        }
        internal void BindInstalled(object identity, SceneContext context, NativeGuestMapRoute route, MapChoiceSnapshot choice)
        {
            Execute(() =>
            {
                if (!ReferenceEquals(identity, _entry) || _route != null || _scopes.Count == 0 || choice == null || route == null)
                    throw Revoke("Installed route has no fixed entry move.");
                _route = route; _context = context; _choice = MapChoiceCopy(choice);
                _contextPointer = Read(() => Pointer(context));
                RequireStatus(_registry.BindRoute(_scopes.Peek().Token, _contextPointer, choice.Route));
                var roads = Read(() => context.m_SceneRoadmap); _roadmapPointer = Read(() => Pointer(roads));
                foreach (MapRouteScene scene in choice.Route.Scenes)
                {
                    var road = Read(() => roads[scene.SceneId]);
                    long pointer = Read(() => Pointer(road));
                    if (pointer == 0 || _roads.Values.Contains(pointer) || !_route.OwnsRoadmapRecord(scene.SceneId, pointer))
                        throw Revoke("Owned roadmap allocation identities are missing or aliased.");
                    _roads.Add(scene.SceneId, pointer);
                    var catalog = _maps.SceneCatalog(_entry);
                    var data = Read(() => catalog[scene.SceneId]);
                    SceneProof proof = ReadScene(data);
                    if (proof.Id != scene.SceneId || proof.Name != scene.SceneName) throw Revoke("A route scene lacks its exact local catalog record.");
                    _sceneRecords.Add(scene.SceneId, proof);
                }
                return true;
            }, installed: false);
        }
        internal void RetireEntry(object identity)
        {
            if (!ReferenceEquals(identity, _entry) || _registry == null) return;
            if (Environment.CurrentManagedThreadId != UnityThreadId) { SourceFailure("Guest entry retirement changed threads."); return; }
            _registry.RetireOwner(_ownerLife);
            foreach (NativeGuestControllerBirth token in _controllers.Values) token.Retired = true;
        }

        public NativeGuestControllerBirth RegisterControllerBirth(IGPSetController controller)
        {
            if (!Available) return null;
            return Execute(() =>
            {
                Poll();
                long pointer = Read(() => Pointer(controller)); IntPtr unity = Read(() => controller.m_CachedPtr);
                IntPtr nativeClass = Read(() => IL2CPP.il2cpp_object_get_class(controller.Pointer));
                var gameObject = Read(() => controller.gameObject); Scene scene = Read(() => gameObject.scene);
                string name = Read(() => scene.name);
                if (!_choice.Route.Scenes.Any(item => item.SceneName == name)) return null;
                Exact<IGPSetController>(controller);
                if (pointer == 0 || unity == IntPtr.Zero || scene.m_Handle == 0 || string.IsNullOrEmpty(name) || name.Length > MapSelections.MaxSceneName)
                    throw Revoke("Controller birth has no live native scene.");
                MapOriginStatus status = _registry.RegisterControllerBirth(pointer, scene.m_Handle, name, out long life);
                RequireStatus(status, pending: true);
                if (_controllers.TryGetValue(life, out NativeGuestControllerBirth previous))
                { if (!ValidateBirth(previous)) throw Revoke("Controller birth cannot revive."); return previous; }
                if (_controllers.Count >= MaxControllers) throw Revoke("Controller birth quota exceeded.");
                Keep(controller);
                var token = new NativeGuestControllerBirth(this, _entry, controller, pointer, unity, nativeClass, scene.m_Handle, name, life, _ownerLife);
                _controllers.Add(life, token);
                if (!ValidateBirth(token)) throw Revoke("Controller birth changed during registration.");
                return token;
            });
        }
        public void RegisterControllerIterator(NativeGuestControllerBirth token, Il2CppSystem.Collections.IEnumerator iterator)
        {
            Execute(() =>
            {
                if (!ValidateBirth(token)) throw Revoke("Controller iterator has no current birth.");
                Exact<IGPSetController._Init_d__16>(iterator);
                var typed = Read(() => new IGPSetController._Init_d__16(iterator.Pointer));
                long pointer = Read(() => Pointer(typed));
                if (Read(() => Pointer(typed.__4__this)) != token.ControllerPointer || pointer == 0)
                    throw Revoke("Controller iterator has another actor.");
                if (token.Iterator != null)
                { if (token.IteratorPointer != pointer) throw Revoke("Controller returned another iterator."); return true; }
                RequireStatus(_registry.RegisterControllerIterator(token.ControllerLife, token.ControllerPointer, pointer, out long life), pending: true);
                Keep(iterator); token.Iterator = iterator; token.IteratorPointer = pointer; token.IteratorLife = life;
                return true;
            });
        }
        public bool ValidateControllerBirth(NativeGuestControllerBirth token)
        { if (_failed || token == null || token.Retired || !Available) return false; return Execute(() => ValidateBirth(token)); }
        private bool ValidateBirth(NativeGuestControllerBirth token)
        {
            if (token == null || token.Retired || !ReferenceEquals(token.Producer, this) || !ReferenceEquals(token.Entry, _entry) ||
                !_controllers.TryGetValue(token.ControllerLife, out NativeGuestControllerBirth known) || !ReferenceEquals(known, token) ||
                _registry.IsControllerRetired(token.ControllerLife)) return false;
            if (Read(() => Pointer(token.Controller)) != token.ControllerPointer || Read(() => token.Controller.m_CachedPtr) != token.UnityPointer || token.UnityPointer == IntPtr.Zero ||
                Read(() => IL2CPP.il2cpp_object_get_class(token.Controller.Pointer)) != token.NativeClass) return false;
            var gameObject = Read(() => token.Controller.gameObject); Scene scene = Read(() => gameObject.scene);
            if (scene.m_Handle != token.SceneHandle || Read(() => scene.name) != token.SceneName) return false;
            if (token.Iterator != null)
            {
                Exact<IGPSetController._Init_d__16>(token.Iterator);
                var typed = Read(() => new IGPSetController._Init_d__16(token.Iterator.Pointer));
                if (Read(() => Pointer(typed)) != token.IteratorPointer || Read(() => Pointer(typed.__4__this)) != token.ControllerPointer) return false;
            }
            return Read(() => token.Controller.m_CachedPtr) == token.UnityPointer;
        }
        public bool TryReadControllerSource(NativeGuestControllerBirth token, out MapOriginControllerSource source)
        {
            source = null;
            if (!Available || token == null || token.Retired) return false;
            MapOriginControllerSource copied = null;
            bool found = Execute(() =>
            {
                if (!ValidateBirth(token)) return false;
                Poll();
                if (!_registry.TryGetControllerSource(token.ControllerLife, out copied)) return false;
                if (!_operations.TryGetValue(copied.OperationLife, out Operation operation) || !operation.Completed ||
                    !ReferenceEquals(operation.Entry, _entry) || operation.Pointer != copied.OperationPointer || operation.Version != copied.OperationVersion ||
                    !ReadOperation(operation, out int operationScene) || operationScene != copied.SceneHandle) throw Revoke("Controller source lost its actual completed operation.");
                return copied.ControllerPointer == token.ControllerPointer && copied.OwnerLife == _ownerLife && copied.SceneHandle == token.SceneHandle &&
                    copied.SceneName == token.SceneName && _choice.Route.Scenes.Any(scene => scene.SceneName == copied.SceneName) && ValidateBirth(token) &&
                    ReadOperation(operation, out int afterScene) && afterScene == copied.SceneHandle;
            });
            if (found) source = copied;
            return found;
        }
        public bool EnterControllerMove(NativeGuestControllerBirth token, IGPSetController._Init_d__16 iterator, out long scope)
        {
            scope = 0;
            if (!TryReadControllerSource(token, out MapOriginControllerSource source)) return false;
            long captured = 0;
            Execute(() =>
            {
                if (!ValidateBirth(token) || Read(() => Pointer(iterator)) != token.IteratorPointer || source.OwnerLife != _ownerLife)
                    throw Revoke("Controller move does not match its original iterator.");
                RequireStatus(_registry.EnterMoveNext(token.IteratorPointer, token.IteratorLife, out captured));
                Push(captured, null, move: true); return true;
            });
            scope = captured; return true;
        }
        public void ExitControllerMove(NativeGuestControllerBirth token, long scope) => ExitUnknownScope(scope);
        public void EnterUnknownScope(out long scope)
        {
            scope = 0; if (_registry == null || _entry == null) return;
            if (Failed) throw Revoke("A failed guest source cannot enter another native callback.");
            long captured = 0; Execute(() => { PushUnknown(out captured); return true; }, installed: _route != null); scope = captured;
        }
        public void ExitUnknownScope(long scope)
        {
            if (scope == 0 || _failed || _registry == null) return;
            if (Environment.CurrentManagedThreadId != UnityThreadId || _scopes.Count == 0 || _scopes.Peek().Token != scope)
            { SourceFailure("Guest scene scope exit is not LIFO."); return; }
            RequireStatus(_registry.ExitScope(scope)); _scopes.Pop();
        }
        public void RetireController(NativeGuestControllerBirth token)
        {
            if (token == null || !ReferenceEquals(token.Producer, this) || token.Retired) return;
            if (Environment.CurrentManagedThreadId != UnityThreadId) { SourceFailure("Controller retirement changed threads."); return; }
            token.Retired = true; if (_registry != null) _registry.RetireController(token.ControllerLife);
        }
        public bool TryCaptureChoice(NativeGuestControllerBirth token, string address, out MapIgpChoice choice)
        {
            choice = null;
            if (!TryReadControllerSource(token, out MapOriginControllerSource source) || string.IsNullOrEmpty(address)) return false;
            if (!_maps.TryCaptureSceneChoices(_entry, out MapChoiceSnapshot current) || current == null || current.Retired || current.Route == null ||
                current.Generation != _choice.Generation || current.RouteFingerprint != _choice.RouteFingerprint) return false;
            MapRouteScene scene = current.Route.Scenes.SingleOrDefault(item => item.SceneName == source.SceneName);
            if (scene == null) return false;
            var matches = current.Choices.Where(item => item.SceneId == scene.SceneId && item.ControllerAddress == address).ToArray();
            if (matches.Length > 1) throw Revoke("Host choices contain an ambiguous controller address.");
            if (matches.Length == 0) return false;
            MapIgpChoice copied = MapChoiceFrames.Copy(matches[0]);
            if (!ValidateControllerBirth(token) || !_maps.SceneEntryCurrent(_entry, true)) return false;
            choice = copied; return true;
        }
        public void SourceFailure(string reason)
        { _failed = true; _registry?.Invalidate("Guest scene source failed."); _maps.SceneSourceFailure(reason); }

        public bool RequiresIgpWait()
        {
            if (Failed) throw Revoke("A failed guest source cannot report loading complete.");
            if (_registry == null || _entry == null || _registry.ActiveOwnerLife != _ownerLife) return false;
            if (!Available)
            { if (Failed || _route != null) throw Revoke("Installed scene completion source is no longer current."); return false; }
            return Execute(() =>
            {
                Poll();
                if (_loadingCalls.Any(call => ReferenceEquals(call.Entry, _entry) && call.OwnerLife == _ownerLife)) return true;
                if (_operations.Values.Any(operation => ReferenceEquals(operation.Entry, _entry) && operation.OwnerLife == _ownerLife && !operation.Completed)) return true;
                if (!_maps.TryCaptureSceneChoices(_entry, out MapChoiceSnapshot current) || current == null || current.Retired || current.Route == null ||
                    current.Generation != _choice.Generation || current.RouteFingerprint != _choice.RouteFingerprint) return false;
                foreach (Operation operation in _operations.Values)
                {
                    if (!ReferenceEquals(operation.Entry, _entry) || operation.OwnerLife != _ownerLife || !operation.Completed) continue;
                    if (!ReadOperation(operation, out int handle)) throw Revoke("Completed scene operation lost its source.");
                    MapRouteScene layer = current.Route.Scenes.SingleOrDefault(item => item.SceneName == operation.Key);
                    // An unclassified original bootstrap may use an address alias.
                    // Its exact operation/handle remains evidence, never a layer wait.
                    if (layer == null) continue;
                    SceneInstance result = Read(() => operation.Native._Result_k__BackingField);
                    Scene scene = Read(() => result.m_Scene);
                    string name = Read(() => scene.name);
                    if (scene.m_Handle != handle || name != layer.SceneName) throw Revoke("Completed selected layer does not match its fixed scene name.");
                    if (!current.Choices.Any(choice => choice.SceneId == layer.SceneId)) continue;
                    if (!_controllers.Values.Any(token => IsCurrentEntry(token) && token.SceneHandle == handle && token.SceneName == name)) return true;
                }
                return false;
            });
        }

        private Factory BeginFactory(Kind kind, SceneLoader loader, Il2CppObjectBase argument, int sceneId, string key, LoadSceneMode mode, bool activate)
        {
            if (Failed) throw Revoke("A failed guest source cannot resume a scene factory.");
            if (_registry == null || _entry == null) return null;
            if (Environment.CurrentManagedThreadId != UnityThreadId) throw Revoke("Scene factory changed threads.");
            if (_route != null && !Available)
                throw Revoke("Installed scene factory source is no longer current.");
            return Execute(() =>
            {
                Scope parent = _scopes.Count == 0 ? null : _scopes.Peek();
                long parentOwner = _registry.CurrentOwnerLife;
                var call = new Factory { Kind = kind, Entry = _entry, Mode = mode, Activate = activate };
                // This callback always masks its parent until the void finalizer.
                // Freeze the real parent before installing our own unknown mask.
                PushUnknown(out call.Scope);
                if (_route == null || (parent != null && (!parent.Move || parentOwner == 0))) return call;
                Poll();
                if (kind == Kind.Load)
                {
                    if (parent == null || !parent.Move || parentOwner != _ownerLife) return call;
                    if (parent.Iterator != null) { ValidateIterator(parent.Iterator); parent.Key = parent.Iterator.Key; }
                    if (key == null || key.Length > MapOriginRegistry.MaxLoadKey || parent.Key != key)
                        throw Revoke("Scene load key differs from its fixed original move.");
                    call.OwnerLife = parentOwner; call.Key = key; BindFactoryScope(call); return call;
                }
                if (Read(() => Pointer(loader)) != _loaderPointer || Read(() => loader.m_CachedPtr) != _loaderUnityPointer)
                    return call;
                if (kind == Kind.Additive)
                {
                    call.ArgumentPointer = Read(() => Pointer(argument));
                    var matches = _roads.Where(item => item.Value == call.ArgumentPointer).ToArray();
                    if (matches.Length != 1) return call;
                    sceneId = matches[0].Key;
                    if (!OwnedRoad(sceneId, call.ArgumentPointer)) throw Revoke("Additive record is no longer helper-owned.");
                }
                else if (kind == Kind.Level)
                {
                    Exact<DR.GameScene>(argument);
                    var scene = Read(() => new DR.GameScene(argument.Pointer));
                    call.ArgumentPointer = Read(() => Pointer(scene)); sceneId = Read(() => scene._TID_k__BackingField);
                    string name = Read(() => scene._SceneName_k__BackingField);
                    if (!_choice.Route.Scenes.Any(item => item.SceneId == sceneId && item.SceneName == name)) return call;
                }
                if (!_roads.TryGetValue(sceneId, out long road) || !OwnedRoad(sceneId, road)) return call;
                call.SceneId = sceneId; call.Key = _choice.Route.Scenes.Single(item => item.SceneId == sceneId).SceneName;
                if (parentOwner != _ownerLife && !CurrentLoaderSceneOwned())
                    throw Revoke("A selected layer factory has no exact loaded manager-scene source.");
                if (!_sceneRecords.TryGetValue(sceneId, out SceneProof proof) || !SceneCurrent(proof) ||
                    (kind == Kind.Level && proof.Pointer != call.ArgumentPointer)) throw Revoke("The selected layer has no fixed local catalog identity.");
                call.OwnerLife = _ownerLife; BindFactoryScope(call); return call;
            }, installed: _route != null);
        }
        private void BindFactoryScope(Factory call)
        {
            ExitUnknownScope(call.Scope);
            RequireStatus(_registry.EnterOwner(call.OwnerLife, out call.Scope));
            Push(call.Scope, call.Key);
        }
        private bool CurrentLoaderSceneOwned()
        {
            var manager = Read(() => _loader._CurrentSceneManager_k__BackingField);
            if (ReferenceEquals(manager, null)) return false;
            long pointer = Read(() => Pointer(manager)); IntPtr unity = Read(() => manager.m_CachedPtr);
            if (pointer == 0 || unity == IntPtr.Zero) return false;
            var gameObject = Read(() => manager.gameObject); Scene scene = Read(() => gameObject.scene);
            if (!_registry.TryGetSceneOwner(scene.m_Handle, out long owner) || owner != _ownerLife) return false;
            var afterObject = Read(() => manager.gameObject); Scene after = Read(() => afterObject.scene);
            return Read(() => Pointer(_loader._CurrentSceneManager_k__BackingField)) == pointer && Read(() => manager.m_CachedPtr) == unity && after.m_Handle == scene.m_Handle;
        }
        private SceneProof ReadScene(DR.GameScene native)
        {
            Exact<DR.GameScene>(native);
            return new SceneProof { Native = native, Pointer = Read(() => Pointer(native)), Id = Read(() => native._TID_k__BackingField),
                Name = Read(() => native._SceneName_k__BackingField), Type = Read(() => native._SceneType_k__BackingField),
                Additive = Read(() => native._IsAdditive_k__BackingField), Diving = Read(() => native._SceneWithDiving_k__BackingField) };
        }
        private bool SceneCurrent(SceneProof proof)
        {
            var catalog = _maps.SceneCatalog(_entry); var value = Read(() => catalog[proof.Id]);
            SceneProof after = ReadScene(value);
            return after.Pointer == proof.Pointer && after.Id == proof.Id && after.Name == proof.Name && after.Type == proof.Type &&
                after.Additive == proof.Additive && after.Diving == proof.Diving;
        }
        private bool OwnedRoad(int id, long pointer)
        {
            var roads = Read(() => _context.m_SceneRoadmap);
            if (Read(() => Pointer(roads)) != _roadmapPointer || !_roads.TryGetValue(id, out long owned) || owned != pointer ||
                !_route.OwnsRoadmapRecord(id, pointer)) return false;
            var value = Read(() => roads[id]); return Read(() => Pointer(value)) == pointer;
        }
        private void EndFactory(Factory call, Il2CppSystem.Collections.IEnumerator returned, bool ranOriginal)
        {
            if (call == null || call.OwnerLife == 0) return;
            Execute(() =>
            {
                if (!ranOriginal || !ReferenceEquals(call.Entry, _entry) || returned == null) throw Revoke("The fixed scene factory did not return normally.");
                long pointer = Read(() => Pointer(returned));
                if (_iterators.Count >= MaxIterators || _iterators.ContainsKey(pointer)) throw Revoke("Scene iterator quota or identity was reused.");
                if (call.Kind == Kind.Level) Exact<SceneLoader._CoChangeLevelSceneAsync_d__114>(returned);
                else if (call.Kind == Kind.Additive) Exact<SceneLoader._LoadAdditiveScene_d__116>(returned);
                else if (call.Kind == Kind.ChildAdditive) Exact<SceneLoader._coLoadAdditiveScene_d__191>(returned);
                else Exact<SceneLoader._CoLoadSceneAsync_d__108>(returned);
                RequireStatus(_registry.RegisterIterator(pointer, call.OwnerLife, out long life));
                Keep(returned);
                _iterators.Add(pointer, new Iterator { Entry = call.Entry, Kind = call.Kind, Native = returned, Pointer = pointer,
                    Life = life, OwnerLife = call.OwnerLife, ArgumentPointer = call.ArgumentPointer, SceneId = call.SceneId,
                    Key = call.Key, Mode = call.Mode, Activate = call.Activate });
                return true;
            });
        }
        private Move BeginMove(Il2CppObjectBase native)
        {
            if (Failed) throw Revoke("A failed guest source cannot resume a loading iterator.");
            if (_registry == null) return null;
            if (Environment.CurrentManagedThreadId != UnityThreadId) throw Revoke("Scene iterator changed threads.");
            if (_route != null && !Available)
                throw Revoke("Installed loading iterator source is no longer current.");
            return Execute(() =>
            {
                long pointer = Read(() => Pointer(native));
                var call = new Move();
                if (!_iterators.TryGetValue(pointer, out Iterator item) || !ReferenceEquals(item.Entry, _entry))
                { PushUnknown(out call.Scope); return call; }
                ValidateIterator(item);
                Poll(); RequireStatus(_registry.EnterMoveNext(pointer, item.Life, out call.Scope));
                Push(call.Scope, item.Key, item, true); call.Iterator = item; call.Approved = true; return call;
            }, installed: _route != null);
        }
        private void ValidateIterator(Iterator item)
        {
            if (Read(() => Pointer(item.Native)) != item.Pointer || item.OwnerLife != _ownerLife) throw Revoke("Scene iterator has another fixed entry.");
            if (item.Kind == Kind.Load)
            {
                Exact<SceneLoader._CoLoadSceneAsync_d__108>(item.Native);
                var native = Read(() => new SceneLoader._CoLoadSceneAsync_d__108(item.Native.Pointer));
                if (Read(() => native.key) != item.Key || Read(() => native.loadMode) != item.Mode || Read(() => native.activateOnLoad) != item.Activate)
                    throw Revoke("Original resource parameters changed.");
            }
            else if (item.Kind == Kind.Level)
            {
                Exact<SceneLoader._CoChangeLevelSceneAsync_d__114>(item.Native);
                var native = Read(() => new SceneLoader._CoChangeLevelSceneAsync_d__114(item.Native.Pointer));
                if (Read(() => Pointer(native.__4__this)) != _loaderPointer || Read(() => Pointer(native.sceneData)) != item.ArgumentPointer)
                    throw Revoke("Original level iterator actor or scene changed.");
                if (!_sceneRecords.TryGetValue(item.SceneId, out SceneProof proof) || !SceneCurrent(proof)) throw Revoke("Level iterator catalog values changed.");
            }
            else if (item.Kind == Kind.Additive)
            {
                Exact<SceneLoader._LoadAdditiveScene_d__116>(item.Native);
                var native = Read(() => new SceneLoader._LoadAdditiveScene_d__116(item.Native.Pointer));
                if (Read(() => Pointer(native.__4__this)) != _loaderPointer || Read(() => Pointer(native.firstAdditiveData)) != item.ArgumentPointer || !OwnedRoad(item.SceneId, item.ArgumentPointer))
                    throw Revoke("Original additive iterator lost its owned record.");
                var loadMap = Read(() => native._loadMap_5__2);
                if (loadMap != null)
                {
                    long loadPointer = Read(() => Pointer(loadMap));
                    var matches = _roads.Where(pair => pair.Value == loadPointer).ToArray();
                    if (matches.Length != 1 || !OwnedRoad(matches[0].Key, loadPointer)) throw Revoke("Additive iterator current record is unowned.");
                    item.Key = _choice.Route.Scenes.Single(scene => scene.SceneId == matches[0].Key).SceneName;
                }
            }
            else
            {
                Exact<SceneLoader._coLoadAdditiveScene_d__191>(item.Native);
                var native = Read(() => new SceneLoader._coLoadAdditiveScene_d__191(item.Native.Pointer));
                if (Read(() => native.sceneId) != item.SceneId || !_roads.TryGetValue(item.SceneId, out long pointer) || !OwnedRoad(item.SceneId, pointer))
                    throw Revoke("Original child-additive scene changed.");
                if (!_sceneRecords.TryGetValue(item.SceneId, out SceneProof proof) || !SceneCurrent(proof)) throw Revoke("Child-additive catalog values changed.");
                var selected = Read(() => native._sceneInfo_5__2);
                if (!ReferenceEquals(selected, null) && Read(() => Pointer(selected)) != proof.Pointer)
                    throw Revoke("Child-additive iterator selected another catalog record.");
                string name = Read(() => native._sceneName_5__3);
                if (name != null && name != proof.Name) throw Revoke("Child-additive iterator selected another scene name.");
            }
        }
        private void EndMove(Move call, bool ranOriginal)
        {
            if (call?.Iterator == null || _failed) return;
            Execute(() => { if (!ranOriginal) throw Revoke("A fixed loading move was skipped."); ValidateIterator(call.Iterator); Poll(); return true; });
        }
        private Load BeginAddressables(Il2CppSystem.Object key, LoadSceneMode mode, bool activate, int priority, SceneReleaseMode releaseMode)
        {
            if (Failed) throw Revoke("A failed guest source cannot resume a scene operation factory.");
            if (_registry != null && Environment.CurrentManagedThreadId != UnityThreadId) throw Revoke("Scene operation factory changed threads.");
            if (!Available)
            { if (Failed || _route != null) throw Revoke("Installed scene operation source is no longer current."); return null; }
            return Execute(() =>
            {
                if (_scopes.Count == 0 || _registry.CurrentOwnerLife != _ownerLife) return null;
                Scope frame = _scopes.Peek();
                if (!frame.Move) throw Revoke("Addressables has no approved fixed original move.");
                if (frame.Iterator != null)
                {
                    ValidateIterator(frame.Iterator); frame.Key = frame.Iterator.Key;
                    if (frame.Iterator.Kind == Kind.Load && (mode != frame.Iterator.Mode || activate != frame.Iterator.Activate))
                        throw Revoke("Addressables loading mode changed from its original factory.");
                }
                var text = Read(() => key?.TryCast<Il2CppSystem.String>());
                string value = text == null ? null : Read(() => (string)text);
                if (string.IsNullOrEmpty(value) || value.Length > MapOriginRegistry.MaxLoadKey || _scopes.Peek().Key != value)
                    throw Revoke("Addressables has no matching fixed original scene key.");
                if (_loadingCalls.Count >= MaxOperations) throw Revoke("In-flight scene call quota exceeded.");
                var call = new Load { Entry = _entry, Scope = frame.Token, OwnerLife = _ownerLife, Key = value,
                    Mode = mode, Activate = activate, Priority = priority, ReleaseMode = releaseMode };
                _loadingCalls.Add(call); return call;
            });
        }
        private void EndAddressables(Load call, SceneHandle handle, bool ranOriginal)
        {
            if (call == null) return;
            Execute(() =>
            {
                if (!ranOriginal || !ReferenceEquals(call.Entry, _entry) || _scopes.Count == 0 || _scopes.Peek().Token != call.Scope)
                    throw Revoke("Addressables returned without its exact original scope.");
                var operation = Read(() => handle.m_InternalOp); int version = Read(() => handle.m_Version);
                long pointer = Read(() => Pointer(operation));
                if (pointer == 0 || version < 0) throw Revoke("Original typed Addressables operation is absent.");
                RequireStatus(_registry.RegisterOperation(call.Scope, pointer, version, call.Key, out long life));
                if (!_operations.ContainsKey(life))
                {
                    if (_operations.Count >= MaxOperations) throw Revoke("Scene operation retention quota exceeded.");
                    Keep(operation); _operations.Add(life, new Operation { Entry = _entry, Native = operation, Pointer = pointer, Version = version, Life = life, OwnerLife = _ownerLife, Key = call.Key });
                }
                Poll(); return true;
            });
        }
        private void Poll()
        {
            foreach (Operation operation in _operations.Values.ToArray())
            {
                if (!ReferenceEquals(operation.Entry, _entry) || operation.OwnerLife != _ownerLife) continue;
                if (operation.Completed) continue;
                if (!ReadOperation(operation, out int handle)) continue;
                RequireStatus(_registry.CompleteOperation(operation.Life, operation.Pointer, operation.Version, handle, out long ignored));
                operation.Completed = true; operation.SceneHandle = handle;
            }
        }
        private bool ReadOperation(Operation operation, out int handle)
        {
            handle = 0;
            int version = Read(() => operation.Native.m_Version);
            if (version != operation.Version || Read(() => Pointer(operation.Native)) != operation.Pointer) throw Revoke("Original scene operation version changed.");
            AsyncOperationStatus status = Read(() => operation.Native.m_Status);
            if (status == AsyncOperationStatus.None && !operation.Completed) return false;
            if (status != AsyncOperationStatus.Succeeded) throw Revoke("Original scene operation failed or lost its success.");
            SceneInstance result = Read(() => operation.Native._Result_k__BackingField);
            if (ReferenceEquals(result, null)) throw Revoke("Scene success lacks its actual result.");
            Scene scene = Read(() => result.m_Scene); handle = scene.m_Handle;
            if (handle == 0 || (operation.Completed && operation.SceneHandle != handle) || Read(() => operation.Native.m_Version) != version ||
                Read(() => operation.Native.m_Status) != AsyncOperationStatus.Succeeded || Read(() => Pointer(operation.Native)) != operation.Pointer)
                throw Revoke("Original scene result changed during copying.");
            return true;
        }
        private void PushUnknown(out long scope) { RequireStatus(_registry.EnterOwner(0, out scope), unbound: true); Push(scope, null); }
        private void Push(long scope, string key, Iterator iterator = null, bool move = false)
        { if (scope == 0) throw Revoke("Scene scope was not allocated."); _scopes.Push(new Scope { Token = scope, Entry = _entry, Key = key, Iterator = iterator, Move = move }); }
        private T Execute<T>(Func<T> action, bool installed = true)
        {
            if (_busy || _reading) throw Revoke("Guest scene source reentered.");
            _busy = true;
            _reads = 0;
            try
            {
                Check(installed);
                if (installed && !_maps.ValidateSceneEntry(_entry)) throw Revoke("Installed route roots changed before source use.");
                T value = action(); Check(installed);
                if (installed && !_maps.ValidateSceneEntry(_entry)) throw Revoke("Installed route roots changed during source use.");
                Check(installed); return value;
            }
            catch { SourceFailure("Guest scene source operation failed."); throw; }
            finally { _busy = false; }
        }
        private void Check(bool installed)
        {
            if (Failed || UnityThreadId <= 0 || Environment.CurrentManagedThreadId != UnityThreadId ||
                (_entry != null && !_maps.SceneEntryCurrent(_entry, installed)) || (_registry != null && !_registry.Healthy))
                throw Revoke("Guest scene source window is unavailable.");
        }
        private T Read<T>(Func<T> action)
        {
            if (_reading || ++_reads > MaxNativeReads) throw Revoke("Guest scene read quota or reentry failed.");
            _totalReads++;
            _reading = true;
            try { Check(_route != null); T value = action(); Check(_route != null); return value; }
            finally { _reading = false; }
        }
        private void Exact<T>(Il2CppObjectBase native) where T : Il2CppObjectBase
        {
            IntPtr expected = Read(() => Il2CppClassPointerStore<T>.NativeClassPtr);
            IntPtr actual = Read(() => native == null ? IntPtr.Zero : IL2CPP.il2cpp_object_get_class(native.Pointer));
            if (expected == IntPtr.Zero || actual != expected || Read(() => Il2CppClassPointerStore<T>.NativeClassPtr) != expected)
                throw Revoke("Unsupported exact native source class.");
        }
        private void Keep(Il2CppObjectBase native)
        {
            if (_references.Count >= MaxReferences || native == null) throw Revoke("Guest scene reference quota exceeded.");
            _references.Add(native); // Keep returned wrapper even if explicit handle acquisition/postguard fails.
            IntPtr handle = Read(() => { IntPtr created = IL2CPP.il2cpp_gchandle_new(native.Pointer, false); _handles.Add(created); return created; });
            if (handle == IntPtr.Zero) throw Revoke("Guest scene strong reference failed.");
        }
        private Exception Revoke(string reason) { SourceFailure(reason); return new InvalidOperationException("Guest scene source was revoked."); }
        private void RequireStatus(MapOriginStatus status, bool pending = false, bool unbound = false)
        {
            if (status == MapOriginStatus.Accepted || status == MapOriginStatus.Duplicate || (pending && status == MapOriginStatus.Pending) || (unbound && status == MapOriginStatus.Unbound)) return;
            throw Revoke("Guest origin registry rejected its fixed source.");
        }
        private static MapChoiceSnapshot MapChoiceCopy(MapChoiceSnapshot source) => new MapChoiceSnapshot {
            Generation = source.Generation, RouteFingerprint = source.RouteFingerprint, Route = MapSelections.CopyRoute(source.Route),
            Choices = source.Choices.Select(MapChoiceFrames.Copy).ToArray(), LastChoiceRevision = source.LastChoiceRevision, Retired = source.Retired };
        private static long Pointer(Il2CppObjectBase value) => ReferenceEquals(value, null) ? 0 : value.Pointer.ToInt64();
        private static HarmonyMethod Hook(string name) => name == null ? null : new HarmonyMethod(typeof(NativeGuestSceneController).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)) { priority = Priority.Last };
        private static void Add(List<(MethodInfo, string, string, string)> targets, Type type, string name, bool isStatic, Type result, Type[] arguments, string before, string after, string finalizer)
        {
            MethodInfo method = type.GetMethod(name, BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | (isStatic ? BindingFlags.Static : BindingFlags.Instance), null, arguments, null);
            if (method == null || method.ReturnType != result || method.IsStatic != isStatic || method.IsGenericMethod) throw new InvalidOperationException("Exact guest scene declaration unavailable.");
            targets.Add((method, before, after, finalizer));
        }
        private static void LevelBefore(SceneLoader __instance, DR.GameScene __0, out Factory __state) => __state = _active?.BeginFactory(Kind.Level, __instance, __0, 0, null, 0, false);
        private static void AdditiveBefore(SceneLoader __instance, SceneContext.SceneRoadmapData __0, out Factory __state) => __state = _active?.BeginFactory(Kind.Additive, __instance, __0, 0, null, 0, false);
        private static void ChildBefore(SceneLoader __instance, int __0, out Factory __state) => __state = _active?.BeginFactory(Kind.ChildAdditive, __instance, null, __0, null, 0, false);
        private static void LoadFactoryBefore(string __0, LoadSceneMode __1, bool __2, out Factory __state) => __state = _active?.BeginFactory(Kind.Load, null, null, 0, __0, __1, __2);
        private static void FactoryAfter(Il2CppSystem.Collections.IEnumerator __result, bool __runOriginal, Factory __state) => _active?.EndFactory(__state, __result, __runOriginal);
        private static Exception FactoryFinally(Exception __exception, Factory __state)
        { if (__state != null) { if (__state.OwnerLife != 0 && __exception != null) _active?.SourceFailure("Original scene factory threw."); _active?.ExitUnknownScope(__state.Scope); } return __exception; }
        private static void MoveBefore(Il2CppObjectBase __instance, out Move __state) => __state = _active?.BeginMove(__instance);
        private static void MoveAfter(bool __runOriginal, Move __state) => _active?.EndMove(__state, __runOriginal);
        private static Exception MoveFinally(Exception __exception, Move __state)
        { if (__state != null) { if (__exception != null && __state.Approved) _active?.SourceFailure("Original scene move threw."); _active?.ExitUnknownScope(__state.Scope); } return __exception; }
        private static void AddressablesBefore(Il2CppSystem.Object __0, LoadSceneMode __1, bool __2, int __3, SceneReleaseMode __4, out Load __state)
            => __state = _active?.BeginAddressables(__0, __1, __2, __3, __4);
        private static void AddressablesAfter(SceneHandle __result, bool __runOriginal, Load __state) => _active?.EndAddressables(__state, __result, __runOriginal);
        private static Exception AddressablesFinally(Exception __exception, Load __state)
        { if (__state != null) { if (__exception != null) _active?.SourceFailure("Original scene operation factory threw."); _active?._loadingCalls.Remove(__state); } return __exception; }
        private static void UnloadedBefore(Scene __0)
        {
            if (_active?._registry == null || _active._failed) return;
            if (Environment.CurrentManagedThreadId != _active.UnityThreadId) { _active.SourceFailure("Scene unload changed threads."); return; }
            MapOriginStatus status = _active._registry.RetireScene(__0.m_Handle, 0);
            _active.RequireStatus(status, unbound: true);
            foreach (NativeGuestControllerBirth token in _active._controllers.Values) if (token.SceneHandle == __0.m_Handle) token.Retired = true;
        }
        private static void LoadedBefore() { if (_active?.Available == true) _active.Execute(() => { _active.Poll(); return true; }); }
    }
}
