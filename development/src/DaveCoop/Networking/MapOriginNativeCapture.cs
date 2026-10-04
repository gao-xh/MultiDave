using System;
using System.Collections.Generic;
using System.Threading;
using DaveCoop.Core.World;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace DaveCoop.Networking
{
    internal sealed class MapOriginNativeObservation
    {
        public long ProcessSequence { get; internal set; }
        public long CallId { get; internal set; }
        public string Method { get; internal set; }
        public string Stage { get; internal set; }
        public int CallbackThreadId { get; internal set; }
        public bool MainThread { get; internal set; }
        public int? UnityFrame { get; internal set; }
        public string Status { get; internal set; }
        public long OwnerLife { get; internal set; }
        public long IteratorLife { get; internal set; }
        public long OperationLife { get; internal set; }
        public long ControllerLife { get; internal set; }
        public long SceneLife { get; internal set; }
        public int? SceneHandle { get; internal set; }
        public int? OperationVersion { get; internal set; }
        public string SceneName { get; internal set; }
        public string LoadKey { get; internal set; }
        public string RouteFingerprint { get; internal set; }
        public bool? OriginalMoveNext { get; internal set; }
        public string OriginalException { get; internal set; }
        public string UnavailableReason { get; internal set; }
        public string ReadError { get; internal set; }
        public bool CopiedObservation => true;
        public bool ObservationOnly => true;
        public bool NativeTypedReturnAbiVerified => false;
        public bool NativeGenerationBound => false;
        public bool HostSelectionApplied => false;
        public bool WorldAuthority => false;
        public bool CargoAuthority => false;
    }

    // This is the sole owner of retained native operation wrappers. They remain
    // on the confirmed Unity thread, are bounded/version-fenced, and never enter
    // a CLR observation, Core registry, file queue, or network callback.
    internal sealed class MapOriginNativeCapture
    {
        public const int MaxQueued = 64;
        public const int MaxDrainPerUpdate = 16;
        public const int MaxOperations = 64;
        private readonly object _gate = new object();
        private readonly Queue<MapOriginNativeObservation> _pending = new Queue<MapOriginNativeObservation>(MaxQueued);
        private readonly Dictionary<long, Prefix> _prefixes = new Dictionary<long, Prefix>();
        private readonly Stack<long> _scopes = new Stack<long>();
        private readonly Dictionary<long, long> _contextOwners = new Dictionary<long, long>();
        private readonly Dictionary<long, RetainedOperation> _operations = new Dictionary<long, RetainedOperation>();
        private readonly Dictionary<long, ControllerBirth> _liveControllerBirths = new Dictionary<long, ControllerBirth>();
        private readonly Dictionary<long, long> _iteratorControllers = new Dictionary<long, long>();
        private readonly HashSet<long> _loggedMoveIterators = new HashSet<long>();
        private readonly int _unityThreadId;
        private readonly MapOriginRegistry _registry;
        private readonly MapSelectionCapture _maps;
        private bool _accepting = true;
        private static long _dropped, _unexpectedThreads, _readErrors, _discarded;
        private static int _failed;
        private sealed class Prefix
        {
            public MapOriginMethod Method;
            public long ScopeToken, ParentScope, OwnerLife, IteratorLife, ControllerLife, ContextPointer;
            public long FactoryOwner;
            public string ControllerAddress, SceneName, LoadKey;
            public int SceneHandle;
            public bool OwnScope, Log;
        }
        private sealed class RetainedOperation
        {
            public AsyncOperationBase<SceneInstance> Native;
            public long Life, Pointer, OwnerLife, CallId;
            public int Version;
            public string LoadKey;
        }
        private sealed class ControllerBirth
        {
            public long OwnerBoundary;
            public int SceneHandle;
        }
        public long Dropped => Interlocked.Read(ref _dropped);
        public long UnexpectedThreads => Interlocked.Read(ref _unexpectedThreads);
        public long ReadErrors => Interlocked.Read(ref _readErrors);
        public long Discarded => Interlocked.Read(ref _discarded);
        public bool Failed => Volatile.Read(ref _failed) != 0;
        public int PendingCount { get { lock (_gate) return _pending.Count; } }
        public int RetainedOperations { get { lock (_gate) return _operations.Count; } }

        public MapOriginNativeCapture(int unityThreadId, MapOriginRegistry registry)
        {
            if (unityThreadId < 1 || Environment.CurrentManagedThreadId != unityThreadId)
                throw new ArgumentException("Map origin capture must start on the confirmed Unity thread.");
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (Failed) throw new InvalidOperationException("Map origin native copying previously failed; restart before retrying.");
            _unityThreadId = unityThreadId; _registry = registry; _maps = new MapSelectionCapture(unityThreadId);
        }

        public void Capture(MapOriginCallback call)
        {
            if (call == null) throw new ArgumentNullException(nameof(call));
            lock (_gate)
            {
                if (!_accepting || Failed) return;
                var observation = NewObservation(call);
                if (!observation.MainThread)
                {
                    Interlocked.Increment(ref _unexpectedThreads);
                    Fail(observation, "Callback is outside the confirmed Unity thread.");
                }
                try
                {
                    observation.UnityFrame = Time.frameCount;
                    if (call.Stage == MapOriginStage.Before) Before(call, observation);
                    else if (call.Stage == MapOriginStage.After) After(call, observation);
                    else Finally(call, observation);
                    RequireHealthy();
                }
                catch (Exception error)
                {
                    Fail(observation, error.GetType().Name + ": " + error.Message);
                }
            }
        }

        private void Before(MapOriginCallback call, MapOriginNativeObservation observation)
        {
            if (_prefixes.Count >= MapOriginHooks.MaxPendingCalls) throw new InvalidOperationException("Native origin prefix quota exceeded.");
            var prefix = new Prefix { Method = call.Method, ParentScope = CurrentScope, OwnerLife = _registry.CurrentOwnerLife, Log = true };
            _prefixes.Add(call.CallId, prefix);
            if (call.Method == MapOriginMethod.Entry)
            {
                long pointer = Pointer(call.Instance);
                MapOriginStatus status = _registry.BeginOwner(pointer, out long owner);
                prefix.OwnerLife = owner; EnterOwner(prefix, owner);
                // Old operations cannot complete into a newly started entry.
                Interlocked.Add(ref _discarded, _operations.Count); _operations.Clear();
                _liveControllerBirths.Clear();
                _iteratorControllers.Clear();
                observation.Status = status.ToString();
            }
            else if (call.Method == MapOriginMethod.ChangeScene || call.Method == MapOriginMethod.ChangeLevel)
            {
                EnterOwner(prefix, prefix.OwnerLife);
            }
            else if (IsMoveNext(call.Method))
            {
                long pointer = Pointer(call.Instance);
                _registry.TryGetIteratorLife(pointer, out long life);
                prefix.IteratorLife = life;
                // The controller token only revokes evidence on failure. It
                // never supplies an owner to this permanently bound iterator.
                if (call.Method == MapOriginMethod.ControllerMoveNext &&
                    _iteratorControllers.TryGetValue(life, out long controller)) prefix.ControllerLife = controller;
                MapOriginStatus status = _registry.EnterMoveNext(pointer, life, out long scope);
                Push(prefix, scope); prefix.OwnerLife = _registry.CurrentOwnerLife;
                prefix.Log = !_loggedMoveIterators.Contains(pointer);
                if (prefix.Log)
                {
                    if (_loggedMoveIterators.Count >= MapOriginRegistry.MaxIterators) throw new InvalidOperationException("MoveNext diagnostic quota exceeded.");
                    _loggedMoveIterators.Add(pointer);
                }
                observation.Status = status.ToString();
                if (life == 0) observation.UnavailableReason = "Factory/iterator owner was not observed; unbound scope masks its caller.";
            }
            else if (IsFactory(call.Method))
            {
                prefix.FactoryOwner = prefix.OwnerLife;
                if (call.Method == MapOriginMethod.ManagerFactory || call.Method == MapOriginMethod.ControllerFactory)
                {
                    PollOperationsLocked();
                    Component component = call.Instance as Component ?? call.Instance?.TryCast<Component>();
                    if (component == null) throw new InvalidOperationException("Native component factory instance missing.");
                    // Fixed Unity structure reads, as in the existing map reader;
                    // no original game getter/save interface is invoked.
                    var nativeScene = component.gameObject.scene;
                    prefix.SceneHandle = nativeScene.m_Handle;
                    prefix.SceneName = Text(nativeScene.name, MapSelections.MaxSceneName);
                    _registry.TryGetSceneOwner(prefix.SceneHandle, out long sceneOwner);
                    prefix.FactoryOwner = sceneOwner;
                    prefix.OwnerLife = sceneOwner;
                    if (sceneOwner == 0) observation.UnavailableReason = "Birth scene lacks an exact completed operation owner; iterator remains unbound.";
                    if (call.Method == MapOriginMethod.ControllerFactory)
                    {
                        MapOriginStatus birthStatus = _registry.RegisterControllerBirth(Pointer(call.Instance), prefix.SceneHandle, prefix.SceneName, out long controller);
                        prefix.ControllerLife = controller;
                        if (controller != 0 && (birthStatus == MapOriginStatus.Accepted || birthStatus == MapOriginStatus.Pending || birthStatus == MapOriginStatus.Duplicate))
                        {
                            if (_liveControllerBirths.Count >= MapOriginRegistry.MaxControllers && !_liveControllerBirths.ContainsKey(controller))
                                throw new InvalidOperationException("Controller birth diagnostic quota exceeded.");
                            _liveControllerBirths[controller] = new ControllerBirth { OwnerBoundary = _registry.ActiveOwnerLife, SceneHandle = prefix.SceneHandle };
                        }
                    }
                }
                observation.Status = prefix.FactoryOwner == 0 ? MapOriginStatus.Unbound.ToString() : "FactoryOwnerFrozen";
            }
            else if (call.Method == MapOriginMethod.AddressablesLoad)
            {
                var key = call.Arguments != null && call.Arguments.Length > 0 ? call.Arguments[0] as Il2CppSystem.Object : null;
                Il2CppSystem.String nativeText = key?.TryCast<Il2CppSystem.String>();
                prefix.LoadKey = nativeText == null ? null : Text((string)nativeText, MapOriginRegistry.MaxLoadKey);
                observation.Status = prefix.ParentScope == 0 || prefix.OwnerLife == 0 ? "UnboundLoad" : "LoadCallScopeFrozen";
                if (prefix.LoadKey == null) observation.UnavailableReason = "Addressable key is not an observed string; it is not converted with an original ToString method.";
            }
            else if (call.Method == MapOriginMethod.RouteCached || call.Method == MapOriginMethod.RouteRestored)
            {
                prefix.ContextPointer = Pointer(call.Instance);
            }
            else if (IsContextRetirement(call.Method))
            {
                long pointer = Pointer(call.Instance);
                if (_contextOwners.TryGetValue(pointer, out long owner)) observation.Status = _registry.RetireOwner(owner).ToString();
                else observation.Status = "ContextOwnerNotObserved";
            }
            else if (call.Method == MapOriginMethod.ControllerDestroy)
            {
                if (_registry.TryGetControllerLife(Pointer(call.Instance), out long life))
                { prefix.ControllerLife = life; _liveControllerBirths.Remove(life); observation.Status = _registry.RetireController(life).ToString(); }
                else observation.Status = "ControllerBirthNotObserved";
            }
            else if (call.Method == MapOriginMethod.Choice)
            {
                long pointer = Pointer(call.Instance);
                if (_registry.TryGetControllerLife(pointer, out long controller) &&
                    _liveControllerBirths.TryGetValue(controller, out ControllerBirth birth) &&
                    birth.OwnerBoundary != 0 && birth.OwnerBoundary == _registry.ActiveOwnerLife)
                {
                    prefix.ControllerLife = controller;
                    var nativeController = call.Instance as IGPSetController ?? call.Instance?.TryCast<IGPSetController>();
                    if (nativeController == null) throw new InvalidOperationException("Native IGP choice instance missing.");
                    prefix.ControllerAddress = _maps.ReadControllerAddress(nativeController);
                    observation.Status = "PrefixControllerLifeFrozen";
                }
                else { observation.Status = "ControllerBirthNotLive"; observation.UnavailableReason = "Choice cannot create a missing, destroyed, unloaded or previous-entry controller origin; no hierarchy fields were read."; }
            }
            else if (call.SceneValue.HasValue)
            {
                PollOperationsLocked();
                prefix.SceneHandle = call.SceneValue.Value.m_Handle;
                if (call.Method == MapOriginMethod.SceneUnloaded)
                {
                    _registry.TryGetSceneLife(prefix.SceneHandle, out long sceneLife);
                    observation.SceneLife = sceneLife;
                    observation.Status = _registry.RetireScene(prefix.SceneHandle, sceneLife).ToString();
                    foreach (var birth in new List<KeyValuePair<long, ControllerBirth>>(_liveControllerBirths))
                        if (birth.Value.SceneHandle == prefix.SceneHandle) _liveControllerBirths.Remove(birth.Key);
                }
                else
                {
                    _registry.TryGetSceneOwner(prefix.SceneHandle, out long sceneOwner);
                    observation.Status = sceneOwner == 0 ? "ActualSceneObservedUnbound" : "ActualSceneObservedWithOperationOwner";
                    observation.OwnerLife = sceneOwner;
                }
            }
            Fill(prefix, observation);
            if (prefix.Log) Enqueue(observation);
        }

        private void After(MapOriginCallback call, MapOriginNativeObservation observation)
        {
            if (!_prefixes.TryGetValue(call.CallId, out Prefix prefix)) throw new InvalidOperationException("Native origin postfix has no frozen prefix.");
            if (prefix.Method != call.Method) throw new InvalidOperationException("Native origin prefix method changed.");
            if (IsFactory(call.Method))
            {
                if (call.Iterator == null) { observation.Status = "OriginalIteratorMissing"; observation.UnavailableReason = "No iterator was returned; no inferred registration."; }
                else
                {
                    MapOriginStatus iteratorStatus = _registry.RegisterIterator(Pointer(call.Iterator), prefix.FactoryOwner, out long iterator);
                    observation.Status = iteratorStatus.ToString();
                    prefix.IteratorLife = iterator;
                    if (call.Method == MapOriginMethod.ControllerFactory && iterator != 0 && prefix.ControllerLife != 0 &&
                        (iteratorStatus == MapOriginStatus.Accepted || iteratorStatus == MapOriginStatus.Unbound || iteratorStatus == MapOriginStatus.Duplicate) &&
                        _liveControllerBirths.TryGetValue(prefix.ControllerLife, out ControllerBirth birth) &&
                        birth.OwnerBoundary != 0 && birth.OwnerBoundary == _registry.ActiveOwnerLife)
                    {
                        if (_iteratorControllers.TryGetValue(iterator, out long previousController))
                        {
                            if (previousController != prefix.ControllerLife) throw new InvalidOperationException("Iterator controller identity cannot change.");
                        }
                        else
                        {
                            if (_iteratorControllers.Count >= MapOriginRegistry.MaxIterators) throw new InvalidOperationException("Iterator controller quota exceeded.");
                            _iteratorControllers.Add(iterator, prefix.ControllerLife);
                        }
                    }
                }
            }
            else if (call.Method == MapOriginMethod.AddressablesLoad)
            {
                RegisterOperation(call, prefix, observation);
            }
            else if (call.Method == MapOriginMethod.RouteCached || call.Method == MapOriginMethod.RouteRestored)
            {
                var context = call.Instance as SceneContext ?? call.Instance?.TryCast<SceneContext>();
                if (context == null || Pointer(context) != prefix.ContextPointer) throw new InvalidOperationException("Route prefix/postfix instance changed.");
                MapRouteSelection route = _maps.ReadRouteCandidate(context);
                if (route == null)
                {
                    observation.UnavailableReason = Text(_maps.UnavailableReason, 256);
                    observation.Status = prefix.OwnerLife == 0 ? "UnboundRouteUnavailable" : _registry.RetireOwner(prefix.OwnerLife).ToString();
                }
                else
                {
                    observation.RouteFingerprint = MapSelections.FingerprintRoute(route);
                    MapOriginStatus routeStatus = _registry.BindRoute(prefix.ParentScope, prefix.ContextPointer, route);
                    observation.Status = routeStatus.ToString();
                    if (routeStatus == MapOriginStatus.Accepted && prefix.OwnerLife != 0)
                    {
                        if (_contextOwners.Count >= MapOriginRegistry.MaxOwners && !_contextOwners.ContainsKey(prefix.ContextPointer)) throw new InvalidOperationException("Context owner quota exceeded.");
                        _contextOwners[prefix.ContextPointer] = prefix.OwnerLife;
                    }
                }
            }
            else if (call.Method == MapOriginMethod.Choice)
            {
                if (prefix.ControllerLife == 0) observation.Status = "UnboundChoice";
                else if (call.Selection == null)
                {
                    observation.Status = _registry.RetireController(prefix.ControllerLife).ToString();
                    _liveControllerBirths.Remove(prefix.ControllerLife);
                    observation.UnavailableReason = "Original IGP selection was null; previous choice evidence is retired.";
                }
                else
                {
                    IGPSetObject prefab = call.Selection.Prefab;
                    var choice = new MapGroupSelection
                    {
                        SceneId = 0, ControllerAddress = prefix.ControllerAddress,
                        Addressable = call.Selection.isAddressableMode,
                        SelectedPrefabName = Text(call.Selection.prefabName, MapSelections.MaxPrefabName),
                        PrefabObjectName = prefab == null ? null : Text(prefab.name, MapSelections.MaxPrefabName)
                    };
                    observation.Status = _registry.ObserveChoice(prefix.ControllerLife, call.ProcessSequence, choice).ToString();
                }
            }
            else if (IsMoveNext(call.Method))
            {
                observation.Status = call.OriginalMoveNext == false ? "OriginalIteratorCompleted" : "OriginalIteratorYielded";
            }
            Fill(prefix, observation);
            if (!IsMoveNext(call.Method) || call.OriginalMoveNext == false) Enqueue(observation);
        }

        private void Finally(MapOriginCallback call, MapOriginNativeObservation observation)
        {
            if (!_prefixes.TryGetValue(call.CallId, out Prefix prefix)) return;
            _prefixes.Remove(call.CallId);
            if (prefix.OwnScope)
            {
                if (_scopes.Count == 0 || _scopes.Peek() != prefix.ScopeToken) throw new InvalidOperationException("Native origin scope is not LIFO.");
                _scopes.Pop(); _registry.ExitScope(prefix.ScopeToken);
            }
            if (call.OriginalException != null)
            {
                if (prefix.ControllerLife != 0)
                {
                    _registry.RetireController(prefix.ControllerLife);
                    _liveControllerBirths.Remove(prefix.ControllerLife);
                }
                if (prefix.OwnerLife != 0) _registry.RetireOwner(prefix.OwnerLife);
                observation.Status = "OriginalExceptionObservedEvidenceRetired"; Fill(prefix, observation); Enqueue(observation);
            }
        }

        private void RegisterOperation(MapOriginCallback call, Prefix prefix, MapOriginNativeObservation observation)
        {
            if (prefix.LoadKey == null || prefix.OwnerLife == 0 || prefix.ParentScope == 0)
            { observation.Status = "UnboundLoadHandle"; return; }
            if (call.Handle == null) throw new InvalidOperationException("Original typed scene handle is missing.");
            var native = call.Handle.m_InternalOp;
            if (native == null) throw new InvalidOperationException("Original scene operation is missing.");
            long pointer = Pointer(native); int version = call.Handle.m_Version;
            observation.OperationVersion = version;
            observation.Status = _registry.RegisterOperation(prefix.ParentScope, pointer, version, prefix.LoadKey, out long life).ToString();
            observation.OperationLife = life;
            if (life == 0 || observation.Status == MapOriginStatus.Retired.ToString()) return;
            if (!_operations.ContainsKey(life))
            {
                if (_operations.Count >= MaxOperations) throw new InvalidOperationException("Retained native operation quota exceeded.");
                _operations.Add(life, new RetainedOperation { Native = native, Life = life, Pointer = pointer,
                    Version = version, OwnerLife = prefix.OwnerLife, LoadKey = prefix.LoadKey, CallId = call.CallId });
            }
            PollOperationsLocked();
        }

        public void PollOperations()
        {
            lock (_gate)
            {
                if (!_accepting || Failed) return;
                if (Environment.CurrentManagedThreadId != _unityThreadId)
                { Interlocked.Increment(ref _unexpectedThreads); Fail(NewPoll(null), "Native operations polled outside the confirmed Unity thread."); }
                try { PollOperationsLocked(); RequireHealthy(); }
                catch (Exception error) { Fail(NewPoll(null), error.GetType().Name + ": " + error.Message); }
            }
        }
        private void PollOperationsLocked()
        {
            if (Environment.CurrentManagedThreadId != _unityThreadId) throw new InvalidOperationException("Native polling thread is unknown.");
            foreach (RetainedOperation operation in new List<RetainedOperation>(_operations.Values))
            {
                if (operation.OwnerLife != _registry.ActiveOwnerLife)
                { _operations.Remove(operation.Life); Interlocked.Increment(ref _discarded); continue; }
                var native = operation.Native;
                int before = native.m_Version;
                if (before != operation.Version || Pointer(native) != operation.Pointer)
                { RetireOperation(operation, "OriginalOperationVersionChanged"); continue; }
                AsyncOperationStatus status = native.m_Status;
                if (status == AsyncOperationStatus.None) continue;
                if (status == AsyncOperationStatus.Failed)
                { RetireOperation(operation, "OriginalOperationFailed"); continue; }
                if (status != AsyncOperationStatus.Succeeded) throw new InvalidOperationException("Unexpected native scene operation status.");
                SceneInstance result = native._Result_k__BackingField;
                if (result == null) throw new InvalidOperationException("Successful native operation has no direct scene result.");
                int handle = result.m_Scene.m_Handle;
                int after = native.m_Version;
                if (after != before || Pointer(native) != operation.Pointer || native.m_Status != AsyncOperationStatus.Succeeded)
                { RetireOperation(operation, "OriginalOperationChangedDuringCopy"); continue; }
                if (handle == 0) throw new InvalidOperationException("Successful native scene operation has a zero scene handle.");
                var observation = NewPoll(operation); observation.SceneHandle = handle;
                observation.Status = _registry.CompleteOperation(operation.Life, operation.Pointer, operation.Version, handle, out long sceneLife).ToString();
                observation.SceneLife = sceneLife;
                _operations.Remove(operation.Life); Enqueue(observation);
            }
        }
        private void RetireOperation(RetainedOperation operation, string reason)
        {
            var observation = NewPoll(operation); observation.Status = _registry.RetireOperation(operation.Life).ToString();
            observation.UnavailableReason = reason; _operations.Remove(operation.Life); Enqueue(observation);
        }
        private void EnterOwner(Prefix prefix, long owner)
        { _registry.EnterOwner(owner, out long scope); Push(prefix, scope); }
        private void Push(Prefix prefix, long scope)
        {
            if (scope == 0) return;
            prefix.ScopeToken = scope; prefix.OwnScope = true; _scopes.Push(scope);
        }
        private long CurrentScope => _scopes.Count == 0 ? 0 : _scopes.Peek();
        private static long Pointer(Il2CppObjectBase value) => value == null ? 0 : value.Pointer.ToInt64();
        private static string Text(string value, int maximum)
        {
            if (value == null) return null;
            if (value.Length > maximum) throw new InvalidOperationException("Native origin text exceeds its bound; no truncation may establish identity.");
            return value;
        }
        private static bool IsFactory(MapOriginMethod kind) => kind >= MapOriginMethod.CoChangeFactory && kind <= MapOriginMethod.ControllerFactory;
        private static bool IsMoveNext(MapOriginMethod kind) => kind >= MapOriginMethod.CoChangeMoveNext && kind <= MapOriginMethod.ControllerMoveNext;
        private static bool IsContextRetirement(MapOriginMethod kind) => kind >= MapOriginMethod.ContextClear && kind <= MapOriginMethod.ContextCreated;
        private void RequireHealthy()
        { if (!_registry.Healthy) throw new InvalidOperationException("Native origin registry failed: " + _registry.FaultReason); }
        private static void Fill(Prefix prefix, MapOriginNativeObservation observation)
        {
            if (observation.OwnerLife == 0) observation.OwnerLife = prefix.OwnerLife;
            observation.IteratorLife = prefix.IteratorLife; observation.ControllerLife = prefix.ControllerLife;
            if (prefix.SceneHandle != 0) observation.SceneHandle = prefix.SceneHandle;
            observation.SceneName = prefix.SceneName; observation.LoadKey = prefix.LoadKey;
        }
        private MapOriginNativeObservation NewObservation(MapOriginCallback call) => new MapOriginNativeObservation
        {
            ProcessSequence = call.ProcessSequence, CallId = call.CallId, Method = call.Method.ToString(), Stage = call.Stage.ToString(),
            CallbackThreadId = call.ManagedThreadId, MainThread = call.ManagedThreadId == _unityThreadId && Environment.CurrentManagedThreadId == _unityThreadId,
            OriginalMoveNext = call.OriginalMoveNext, OriginalException = call.OriginalException
        };
        private MapOriginNativeObservation NewPoll(RetainedOperation operation) => new MapOriginNativeObservation
        {
            CallId = operation?.CallId ?? 0, Method = "OperationCompletion", Stage = "MainThreadPoll",
            CallbackThreadId = Environment.CurrentManagedThreadId, MainThread = Environment.CurrentManagedThreadId == _unityThreadId,
            UnityFrame = Environment.CurrentManagedThreadId == _unityThreadId ? Time.frameCount : (int?)null,
            OwnerLife = operation?.OwnerLife ?? 0, OperationLife = operation?.Life ?? 0,
            OperationVersion = operation?.Version, LoadKey = operation?.LoadKey
        };
        private void Enqueue(MapOriginNativeObservation observation)
        {
            if (_pending.Count >= MaxQueued)
            { Interlocked.Increment(ref _dropped); throw new InvalidOperationException("Native origin observation queue overflow; ownership cannot survive missing evidence."); }
            _pending.Enqueue(observation);
        }
        private void Fail(MapOriginNativeObservation observation, string reason)
        {
            // The same failed copy can reach a callback and polling catch. One
            // process-wide failure latch records its diagnostic once.
            if (Interlocked.Exchange(ref _failed, 1) == 0) Interlocked.Increment(ref _readErrors);
            _registry.Invalidate(reason); _scopes.Clear(); _prefixes.Clear(); _operations.Clear();
            _liveControllerBirths.Clear();
            _iteratorControllers.Clear();
            observation.ReadError = reason.Length > 256 ? reason.Substring(0, 256) : reason;
            if (_pending.Count < MaxQueued) _pending.Enqueue(observation);
            throw new InvalidOperationException("Map origin native copying stopped: " + observation.ReadError);
        }
        public bool TryTake(out MapOriginNativeObservation observation)
        {
            lock (_gate)
            {
                if (_pending.Count == 0) { observation = null; return false; }
                observation = _pending.Dequeue(); return true;
            }
        }
        public void Stop()
        {
            lock (_gate)
            {
                if (!_accepting) return;
                _accepting = false; _registry.Invalidate("Native origin observer stopped; its evidence is revoked.");
                Interlocked.Add(ref _discarded, _pending.Count + _prefixes.Count + _operations.Count);
                _pending.Clear(); _prefixes.Clear(); _operations.Clear(); _scopes.Clear();
                _liveControllerBirths.Clear();
                _iteratorControllers.Clear();
            }
        }
    }
}
