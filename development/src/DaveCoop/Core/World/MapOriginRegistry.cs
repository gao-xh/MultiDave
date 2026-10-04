using System;
using System.Collections.Generic;
using System.Threading;

namespace DaveCoop.Core.World
{
    public enum MapOriginStatus
    {
        Accepted, Pending, Duplicate, Unbound, Retired, Invalid, Conflict,
        WrongThread, LimitExceeded, Faulted
    }

    // Local scalar evidence only. Registry validation is not proof that its
    // caller read native data, and never grants adoption or execution permission.
    public sealed class MapOriginChoiceEvidence
    {
        public long OwnerLife { get; set; }
        public long ContextPointer { get; set; }
        public string RouteFingerprint { get; set; }
        public long OperationLife { get; set; }
        public string LoadKey { get; set; }
        public long SceneLife { get; set; }
        public int SceneHandle { get; set; }
        public long ControllerLife { get; set; }
        public string ControllerSceneName { get; set; }
        public long CallbackSequence { get; set; }
        public MapGroupSelection Choice { get; set; }
        public bool ObservationOnly => true;
        public bool NativeGenerationBound => false;
        public bool HostSelectionApplied => false;
        public bool NativePermission => false;
        public bool CrossMachineAddressVerified => false;
    }

    // A current, owned view of one local entry's evidence. It does not consume
    // the diagnostic event queue or establish a complete/adopted world.
    public sealed class MapOriginSourceSnapshot
    {
        public long OwnerLife { get; set; }
        public string RouteFingerprint { get; set; }
        public MapRouteSelection Route { get; set; }
        public MapOriginChoiceEvidence[] Choices { get; set; }
        public bool ObservationOnly => true;
        public bool NativeGenerationBound => false;
        public bool HostSelectionApplied => false;
        public bool NativePermission => false;
        public bool CrossMachineAddressVerified => false;
    }

    // Caller supplies values frozen at the natural callback. Native wrappers,
    // singleton lookups, scene-name ownership guesses and network state are absent.
    // Tombstones remain for this instance; a quota never evicts a replay fence.
    public sealed class MapOriginRegistry
    {
        public const int MaxOwners = 32;
        public const int MaxIterators = 256;
        public const int MaxOperations = 128;
        public const int MaxScenes = 128;
        public const int MaxControllers = 256;
        public const int MaxChoices = 128;
        public const int MaxScopes = 32;
        public const int MaxLoadKey = 512;

        private readonly object _gate = new object();
        private readonly int _mainThreadId;
        private readonly Dictionary<long, Owner> _owners = new Dictionary<long, Owner>();
        private readonly Dictionary<long, Iterator> _iterators = new Dictionary<long, Iterator>();
        private readonly Dictionary<long, Iterator> _iteratorPointers = new Dictionary<long, Iterator>();
        private readonly Dictionary<long, Operation> _operations = new Dictionary<long, Operation>();
        private readonly Dictionary<(long, int), Operation> _operationKeys = new Dictionary<(long, int), Operation>();
        private readonly Dictionary<int, Scene> _scenes = new Dictionary<int, Scene>();
        private readonly HashSet<int> _retiredSceneHandles = new HashSet<int>();
        private readonly Dictionary<long, Controller> _controllers = new Dictionary<long, Controller>();
        private readonly Dictionary<long, Controller> _controllerPointers = new Dictionary<long, Controller>();
        private readonly List<Scope> _scopes = new List<Scope>();
        private readonly List<Selection> _pending = new List<Selection>();
        private readonly Queue<MapOriginChoiceEvidence> _ready = new Queue<MapOriginChoiceEvidence>();
        private long _nextLife;
        private long _activeOwnerLife;
        private int _sceneHandleCount;
        private string _fault;
        private long _unboundChoices;

        public MapOriginRegistry(int mainThreadId)
        {
            if (mainThreadId <= 0) throw new ArgumentException("Invalid main thread.");
            _mainThreadId = mainThreadId;
        }

        public bool Healthy { get { lock (_gate) return _fault == null; } }
        public string FaultReason { get { lock (_gate) return _fault; } }
        public long ActiveOwnerLife { get { lock (_gate) return _activeOwnerLife; } }
        public int OwnerCount { get { lock (_gate) return _owners.Count; } }
        public int IteratorCount { get { lock (_gate) return _iterators.Count; } }
        public int OperationCount { get { lock (_gate) return _operations.Count; } }
        public int SceneCount { get { lock (_gate) return _scenes.Count; } }
        public int SceneHandleCount { get { lock (_gate) return _sceneHandleCount; } }
        public int ControllerCount { get { lock (_gate) return _controllers.Count; } }
        public int PendingChoiceCount { get { lock (_gate) return _pending.Count; } }
        public int ReadyChoiceCount { get { lock (_gate) return _ready.Count; } }
        public long UnboundChoices { get { lock (_gate) return _unboundChoices; } }
        public long CurrentOwnerLife
        {
            get
            {
                lock (_gate)
                {
                    if (Guard() != MapOriginStatus.Accepted || _scopes.Count == 0) return 0;
                    long life = _scopes[_scopes.Count - 1].OwnerLife;
                    return Active(life) ? life : 0;
                }
            }
        }

        // Only the actual entry intent creates an owner. Children inherit a
        // fixed scope; creating an owner from a current singleton is unsupported.
        public MapOriginStatus BeginOwner(long ownerPointer, out long ownerLife)
        {
            lock (_gate)
            {
                ownerLife = 0; MapOriginStatus status = Guard();
                if (status != MapOriginStatus.Accepted) return status;
                if (ownerPointer == 0) return Fail(MapOriginStatus.Invalid, "Missing entry pointer.");
                if (_owners.Count == MaxOwners) return Fail(MapOriginStatus.LimitExceeded, "Owner quota.");
                foreach (Owner previous in _owners.Values) RetireOwnerLocked(previous.Life);
                if (!Next(out ownerLife)) return MapOriginStatus.LimitExceeded;
                _owners.Add(ownerLife, new Owner { Life = ownerLife, Pointer = ownerPointer });
                _activeOwnerLife = ownerLife;
                return MapOriginStatus.Accepted;
            }
        }

        public MapOriginStatus EnterOwner(long ownerLife, out long scopeToken)
        {
            lock (_gate)
            {
                scopeToken = 0; MapOriginStatus status = Guard();
                if (status != MapOriginStatus.Accepted) return status;
                return PushScope(Active(ownerLife) ? ownerLife : 0, out scopeToken);
            }
        }

        public MapOriginStatus RegisterIterator(long pointer, long ownerLife, out long iteratorLife)
        {
            lock (_gate)
            {
                iteratorLife = 0; MapOriginStatus status = Guard();
                if (status != MapOriginStatus.Accepted) return status;
                if (pointer == 0 || ownerLife < 0) return Fail(MapOriginStatus.Invalid, "Invalid iterator identity.");
                if (_iteratorPointers.TryGetValue(pointer, out Iterator previous))
                {
                    iteratorLife = previous.Life;
                    if (previous.Retired || (previous.OwnerLife != 0 && !Active(previous.OwnerLife))) return MapOriginStatus.Retired;
                    if (previous.OwnerLife != ownerLife) return Fail(MapOriginStatus.Conflict, "Iterator owner cannot change.");
                    return previous.OwnerLife == 0 ? MapOriginStatus.Unbound : MapOriginStatus.Duplicate;
                }
                if (ownerLife != 0 && !Active(ownerLife)) return MapOriginStatus.Retired;
                if (_iterators.Count == MaxIterators) return Fail(MapOriginStatus.LimitExceeded, "Iterator quota.");
                if (!Next(out iteratorLife)) return MapOriginStatus.LimitExceeded;
                var item = new Iterator { Life = iteratorLife, Pointer = pointer, OwnerLife = ownerLife };
                _iterators.Add(iteratorLife, item); _iteratorPointers.Add(pointer, item);
                return ownerLife == 0 ? MapOriginStatus.Unbound : MapOriginStatus.Accepted;
            }
        }

        // Even an unknown/retired iterator pushes an UNBOUND scope. A prefix
        // receiving Unbound and a nonzero token must still call ExitScope.
        public MapOriginStatus EnterMoveNext(long pointer, long iteratorLife, out long scopeToken)
        {
            lock (_gate)
            {
                scopeToken = 0; MapOriginStatus status = Guard();
                if (status != MapOriginStatus.Accepted) return status;
                long owner = 0;
                if (_iterators.TryGetValue(iteratorLife, out Iterator item) && item.Pointer == pointer &&
                    !item.Retired && Active(item.OwnerLife)) owner = item.OwnerLife;
                return PushScope(owner, out scopeToken);
            }
        }

        public MapOriginStatus ExitScope(long scopeToken)
        {
            lock (_gate)
            {
                MapOriginStatus status = Guard(); if (status != MapOriginStatus.Accepted) return status;
                if (_scopes.Count == 0 || scopeToken == 0 || _scopes[_scopes.Count - 1].Token != scopeToken)
                    return Fail(MapOriginStatus.Conflict, "Scope exit is not LIFO.");
                _scopes.RemoveAt(_scopes.Count - 1); return MapOriginStatus.Accepted;
            }
        }

        public MapOriginStatus BindRoute(long scopeToken, long contextPointer, MapRouteSelection route)
        {
            lock (_gate)
            {
                MapOriginStatus status = Guard(); if (status != MapOriginStatus.Accepted) return status;
                Owner owner = ScopeOwner(scopeToken); if (owner == null) return MapOriginStatus.Unbound;
                // A cache/restore is a new boundary even when the bytes match.
                // An existing iterator never acquires the replacement context.
                if (owner.Route != null) { RetireOwnerLocked(owner.Life); return MapOriginStatus.Retired; }
                if (contextPointer == 0) return Fail(MapOriginStatus.Invalid, "Missing route context.");
                MapRouteSelection copy;
                try { copy = MapSelections.CopyRoute(route); }
                catch (ArgumentException) { return Fail(MapOriginStatus.Invalid, "Invalid route copy."); }
                owner.ContextPointer = contextPointer; owner.Route = copy;
                owner.Fingerprint = MapSelections.FingerprintRoute(copy);
                FlushPending(); return _fault == null ? MapOriginStatus.Accepted : MapOriginStatus.Faulted;
            }
        }

        public MapOriginStatus RegisterOperation(long scopeToken, long pointer, int version, string loadKey,
            out long operationLife)
        {
            lock (_gate)
            {
                operationLife = 0; MapOriginStatus status = Guard();
                if (status != MapOriginStatus.Accepted) return status;
                Owner owner = ScopeOwner(scopeToken); if (owner == null) return MapOriginStatus.Unbound;
                if (pointer == 0 || version < 0 || !Text(loadKey, MaxLoadKey, false))
                    return Fail(MapOriginStatus.Invalid, "Invalid operation identity/key.");
                if (_operationKeys.TryGetValue((pointer, version), out Operation previous))
                {
                    operationLife = previous.Life;
                    if (previous.Retired || !Active(previous.OwnerLife)) return MapOriginStatus.Retired;
                    if (previous.OwnerLife != owner.Life || previous.LoadKey != loadKey)
                        return Fail(MapOriginStatus.Conflict, "Operation key has conflicting provenance.");
                    return MapOriginStatus.Duplicate;
                }
                if (_operations.Count == MaxOperations) return Fail(MapOriginStatus.LimitExceeded, "Operation quota.");
                if (!Next(out operationLife)) return MapOriginStatus.LimitExceeded;
                var item = new Operation { Life = operationLife, Pointer = pointer, Version = version,
                    OwnerLife = owner.Life, LoadKey = loadKey };
                _operations.Add(operationLife, item); _operationKeys.Add((pointer, version), item);
                return MapOriginStatus.Accepted;
            }
        }

        // Caller has rechecked the original typed operation's version, success
        // status and direct result. No Result/IsDone getter is invoked here.
        public MapOriginStatus CompleteOperation(long operationLife, long pointer, int version,
            int sceneHandle, out long sceneLife)
        {
            lock (_gate)
            {
                sceneLife = 0; MapOriginStatus status = Guard();
                if (status != MapOriginStatus.Accepted) return status;
                if (!_operations.TryGetValue(operationLife, out Operation operation)) return MapOriginStatus.Unbound;
                if (operation.Retired || !Active(operation.OwnerLife)) return MapOriginStatus.Retired;
                if (pointer != operation.Pointer || version != operation.Version || sceneHandle == 0)
                    return Fail(MapOriginStatus.Conflict, "Completion differs from the exact operation.");
                if (_retiredSceneHandles.Contains(sceneHandle)) return MapOriginStatus.Retired;
                if (operation.SceneHandle != 0)
                {
                    if (operation.SceneHandle != sceneHandle) return Fail(MapOriginStatus.Conflict, "Operation result changed.");
                    sceneLife = operation.SceneLife; return MapOriginStatus.Duplicate;
                }
                if (_scenes.TryGetValue(sceneHandle, out Scene previous))
                {
                    if (previous.Retired) return MapOriginStatus.Retired;
                    if (previous.OperationLife != operationLife)
                        return Fail(MapOriginStatus.Conflict, "Scene handle has multiple operation owners.");
                    sceneLife = previous.Life; return MapOriginStatus.Duplicate;
                }
                if (_sceneHandleCount == MaxScenes)
                    return Fail(MapOriginStatus.LimitExceeded, "Scene quota.");
                if (!Next(out sceneLife)) return MapOriginStatus.LimitExceeded;
                operation.SceneHandle = sceneHandle; operation.SceneLife = sceneLife;
                _scenes.Add(sceneHandle, new Scene { Life = sceneLife, Handle = sceneHandle,
                    OwnerLife = operation.OwnerLife, OperationLife = operationLife });
                _sceneHandleCount++;
                foreach (Controller controller in _controllers.Values)
                    if (!controller.Retired && controller.SceneHandle == sceneHandle) BindController(controller);
                FlushPending(); return _fault == null ? MapOriginStatus.Accepted : MapOriginStatus.Faulted;
            }
        }

        public MapOriginStatus RegisterControllerBirth(long pointer, int sceneHandle, string sceneName,
            out long controllerLife)
        {
            lock (_gate)
            {
                controllerLife = 0; MapOriginStatus status = Guard();
                if (status != MapOriginStatus.Accepted) return status;
                if (pointer == 0 || sceneHandle == 0 || !Text(sceneName, MapSelections.MaxSceneName, false))
                    return Fail(MapOriginStatus.Invalid, "Invalid controller birth.");
                if (_controllerPointers.TryGetValue(pointer, out Controller previous))
                {
                    controllerLife = previous.Life;
                    if (previous.Retired) return MapOriginStatus.Retired;
                    if (previous.SceneHandle != sceneHandle || previous.SceneName != sceneName)
                        return Fail(MapOriginStatus.Conflict, "Controller birth cannot change.");
                    return previous.OwnerLife != 0 ? MapOriginStatus.Duplicate :
                        previous.BirthBoundary == 0 ? MapOriginStatus.Unbound : MapOriginStatus.Pending;
                }
                if (_retiredSceneHandles.Contains(sceneHandle)) return MapOriginStatus.Retired;
                if (_controllers.Count == MaxControllers) return Fail(MapOriginStatus.LimitExceeded, "Controller quota.");
                if (!Next(out controllerLife)) return MapOriginStatus.LimitExceeded;
                var item = new Controller { Life = controllerLife, Pointer = pointer, SceneHandle = sceneHandle,
                    SceneName = sceneName, BirthBoundary = _activeOwnerLife };
                _controllers.Add(controllerLife, item); _controllerPointers.Add(pointer, item);
                BindController(item);
                if (item.Retired) return MapOriginStatus.Retired;
                return item.OwnerLife != 0 ? MapOriginStatus.Accepted :
                    item.BirthBoundary == 0 ? MapOriginStatus.Unbound : MapOriginStatus.Pending;
            }
        }

        public MapOriginStatus ObserveChoice(long controllerLife, long callbackSequence, MapGroupSelection choice)
        {
            lock (_gate)
            {
                MapOriginStatus status = Guard(); if (status != MapOriginStatus.Accepted) return status;
                if (!_controllers.TryGetValue(controllerLife, out Controller controller)) return UnboundChoice();
                if (controller.Retired || !Active(controller.BirthBoundary)) return MapOriginStatus.Retired;
                if (callbackSequence <= 0 || !ValidChoice(choice)) return Fail(MapOriginStatus.Invalid, "Invalid selection copy.");
                if (callbackSequence < controller.LastSequence) return MapOriginStatus.Retired;
                if (callbackSequence == controller.LastSequence)
                    return EqualChoice(controller.LastChoice, choice) ? MapOriginStatus.Duplicate :
                        Fail(MapOriginStatus.Conflict, "Selection callback changed.");
                if (_pending.Count + _ready.Count == MaxChoices) return Fail(MapOriginStatus.LimitExceeded, "Selection quota.");
                var selection = new Selection { ControllerLife = controllerLife, Sequence = callbackSequence, Choice = CopyChoice(choice) };
                status = Resolve(selection, out MapOriginChoiceEvidence evidence);
                if (status == MapOriginStatus.Unbound) return UnboundChoice();
                if (status != MapOriginStatus.Accepted && status != MapOriginStatus.Pending) return status;
                controller.LastSequence = callbackSequence; controller.LastChoice = CopyChoice(choice);
                if (evidence == null) _pending.Add(selection); else _ready.Enqueue(evidence);
                return status;
            }
        }

        public bool TryTakeBoundChoice(out MapOriginChoiceEvidence evidence)
        {
            lock (_gate)
            {
                evidence = null; if (Guard() != MapOriginStatus.Accepted) return false;
                if (_ready.Count == 0) return false;
                evidence = CopyEvidence(_ready.Dequeue()); return true;
            }
        }

        public bool TryCaptureSource(out MapOriginSourceSnapshot snapshot)
        {
            lock (_gate)
            {
                snapshot = null;
                if (Guard() != MapOriginStatus.Accepted) return false;
                if (!Active(_activeOwnerLife))
                {
                    snapshot = new MapOriginSourceSnapshot { Choices = Array.Empty<MapOriginChoiceEvidence>() };
                    return true;
                }
                if (_controllers.Count > MaxControllers)
                { Fail(MapOriginStatus.LimitExceeded, "Source controller quota."); return false; }
                Owner owner = _owners[_activeOwnerLife];
                // Resolve may fail closed and retire records. Traverse a bounded
                // owned list, and never FlushPending or iterate its mutable queue.
                var controllers = new List<Controller>(_controllers.Values);
                controllers.Sort((first, second) => first.Life.CompareTo(second.Life));
                var choices = new List<MapOriginChoiceEvidence>();
                foreach (Controller controller in controllers)
                {
                    if (controller.Retired || controller.BirthBoundary != owner.Life || controller.LastChoice == null) continue;
                    var selection = new Selection { ControllerLife = controller.Life,
                        Sequence = controller.LastSequence, Choice = controller.LastChoice };
                    MapOriginStatus status = Resolve(selection, out MapOriginChoiceEvidence evidence);
                    if (_fault != null) return false;
                    if (status == MapOriginStatus.Pending || status == MapOriginStatus.Unbound || status == MapOriginStatus.Retired) continue;
                    if (status != MapOriginStatus.Accepted || evidence == null || evidence.OwnerLife != owner.Life)
                    { Fail(MapOriginStatus.Conflict, "Source choice has conflicting provenance."); return false; }
                    if (choices.Count == MaxControllers)
                    { Fail(MapOriginStatus.LimitExceeded, "Source choice quota."); return false; }
                    // Resolve constructs new evidence and a new mutable choice.
                    choices.Add(evidence);
                }
                MapRouteSelection route;
                try { route = owner.Route == null ? null : MapSelections.CopyRoute(owner.Route); }
                catch (ArgumentException) { Fail(MapOriginStatus.Conflict, "Source route copy is unavailable."); return false; }
                snapshot = new MapOriginSourceSnapshot
                {
                    OwnerLife = owner.Life, RouteFingerprint = owner.Fingerprint,
                    Route = route, Choices = choices.ToArray()
                };
                return true;
            }
        }

        public bool TryGetIteratorLife(long pointer, out long life)
        {
            lock (_gate) { life = 0; if (Guard() != MapOriginStatus.Accepted ||
                !_iteratorPointers.TryGetValue(pointer, out Iterator item)) return false; life = item.Life; return true; }
        }

        public bool TryGetControllerLife(long pointer, out long life)
        {
            lock (_gate) { life = 0; if (Guard() != MapOriginStatus.Accepted ||
                !_controllerPointers.TryGetValue(pointer, out Controller item)) return false; life = item.Life; return true; }
        }

        public bool TryGetSceneOwner(int sceneHandle, out long ownerLife)
        {
            lock (_gate) { ownerLife = 0; if (Guard() != MapOriginStatus.Accepted ||
                !_scenes.TryGetValue(sceneHandle, out Scene item) || item.Retired || !Active(item.OwnerLife)) return false;
                ownerLife = item.OwnerLife; return true; }
        }

        public bool TryGetSceneLife(int sceneHandle, out long sceneLife)
        {
            lock (_gate) { sceneLife = 0; if (Guard() != MapOriginStatus.Accepted ||
                !_scenes.TryGetValue(sceneHandle, out Scene item)) return false; sceneLife = item.Life; return true; }
        }

        public MapOriginStatus RetireController(long controllerLife)
        {
            lock (_gate)
            {
                MapOriginStatus status = Guard(); if (status != MapOriginStatus.Accepted) return status;
                if (!_controllers.TryGetValue(controllerLife, out Controller item)) return MapOriginStatus.Unbound;
                if (item.Retired) return MapOriginStatus.Duplicate;
                item.Retired = true; RemoveSelections(selection => selection.ControllerLife == controllerLife);
                return MapOriginStatus.Accepted;
            }
        }

        // sceneLife=0 is for an actual unload callback preceding operation
        // completion. Unknown handles also need a fence: a bootstrap/empty
        // scene can unload before any controller birth or completion is copied.
        public MapOriginStatus RetireScene(int sceneHandle, long sceneLife = 0)
        {
            lock (_gate)
            {
                MapOriginStatus status = Guard(); if (status != MapOriginStatus.Accepted) return status;
                if (sceneHandle == 0 || sceneLife < 0) return Fail(MapOriginStatus.Invalid, "Invalid scene retirement.");
                bool found = _scenes.TryGetValue(sceneHandle, out Scene scene);
                if (found && sceneLife != 0 && scene.Life != sceneLife) return MapOriginStatus.Retired;
                if (_retiredSceneHandles.Contains(sceneHandle)) return MapOriginStatus.Duplicate;
                if (!found && _sceneHandleCount == MaxScenes)
                    return Fail(MapOriginStatus.LimitExceeded, "Scene tombstone quota.");
                if (!found) _sceneHandleCount++;
                _retiredSceneHandles.Add(sceneHandle); if (found) scene.Retired = true;
                foreach (Controller controller in _controllers.Values)
                    if (controller.SceneHandle == sceneHandle) controller.Retired = true;
                RemoveSelections(selection => _controllers[selection.ControllerLife].SceneHandle == sceneHandle);
                return MapOriginStatus.Accepted;
            }
        }

        public MapOriginStatus RetireOwner(long ownerLife)
        {
            lock (_gate)
            {
                MapOriginStatus status = Guard(); if (status != MapOriginStatus.Accepted) return status;
                if (!_owners.TryGetValue(ownerLife, out Owner owner)) return MapOriginStatus.Unbound;
                if (owner.Retired) return MapOriginStatus.Duplicate;
                RetireOwnerLocked(ownerLife); return MapOriginStatus.Accepted;
            }
        }

        public MapOriginStatus RetireOperation(long operationLife)
        {
            lock (_gate)
            {
                MapOriginStatus status = Guard(); if (status != MapOriginStatus.Accepted) return status;
                if (!_operations.TryGetValue(operationLife, out Operation operation)) return MapOriginStatus.Unbound;
                if (operation.Retired) return MapOriginStatus.Duplicate;
                RetireOwnerLocked(operation.OwnerLife); return MapOriginStatus.Accepted;
            }
        }

        public MapOriginStatus Invalidate(string reason)
        {
            lock (_gate) return Fail(MapOriginStatus.Faulted,
                Text(reason, 256, false) ? reason : "Origin evidence lost or unreadable.");
        }

        private MapOriginStatus Guard()
        {
            if (Thread.CurrentThread.ManagedThreadId != _mainThreadId)
                return Fail(MapOriginStatus.WrongThread, "Origin callback on an unexpected CLR thread.");
            return _fault == null ? MapOriginStatus.Accepted : MapOriginStatus.Faulted;
        }

        private MapOriginStatus Fail(MapOriginStatus status, string reason)
        {
            if (_fault == null) _fault = reason;
            foreach (Owner owner in _owners.Values) RetireOwnerLocked(owner.Life);
            foreach (Controller controller in _controllers.Values) controller.Retired = true;
            _pending.Clear(); _ready.Clear(); _scopes.Clear(); _activeOwnerLife = 0;
            return status;
        }

        private bool Next(out long life)
        {
            life = 0;
            if (_nextLife == long.MaxValue) { Fail(MapOriginStatus.LimitExceeded, "Origin life exhausted."); return false; }
            life = ++_nextLife; return true;
        }

        private bool Active(long ownerLife) => ownerLife != 0 && _owners.TryGetValue(ownerLife, out Owner owner) && !owner.Retired;

        private MapOriginStatus PushScope(long ownerLife, out long scopeToken)
        {
            scopeToken = 0;
            if (_scopes.Count == MaxScopes) return Fail(MapOriginStatus.LimitExceeded, "Scope quota.");
            if (!Next(out scopeToken)) return MapOriginStatus.LimitExceeded;
            _scopes.Add(new Scope { Token = scopeToken, OwnerLife = ownerLife });
            return ownerLife == 0 ? MapOriginStatus.Unbound : MapOriginStatus.Accepted;
        }

        private Owner ScopeOwner(long scopeToken)
        {
            if (_scopes.Count == 0 || scopeToken == 0 || _scopes[_scopes.Count - 1].Token != scopeToken) return null;
            long ownerLife = _scopes[_scopes.Count - 1].OwnerLife;
            return Active(ownerLife) ? _owners[ownerLife] : null;
        }

        private void RetireOwnerLocked(long ownerLife)
        {
            if (!_owners.TryGetValue(ownerLife, out Owner owner)) return;
            owner.Retired = true;
            foreach (Iterator iterator in _iterators.Values) if (iterator.OwnerLife == ownerLife) iterator.Retired = true;
            foreach (Operation operation in _operations.Values) if (operation.OwnerLife == ownerLife) operation.Retired = true;
            foreach (Scene scene in _scenes.Values)
                if (scene.OwnerLife == ownerLife) { scene.Retired = true; _retiredSceneHandles.Add(scene.Handle); }
            foreach (Controller controller in _controllers.Values)
                if (controller.BirthBoundary == ownerLife || controller.OwnerLife == ownerLife) controller.Retired = true;
            RemoveSelections(selection => _controllers[selection.ControllerLife].Retired);
            if (_activeOwnerLife == ownerLife) _activeOwnerLife = 0;
        }

        private void BindController(Controller controller)
        {
            if (controller.Retired || controller.OwnerLife != 0 || !_scenes.TryGetValue(controller.SceneHandle, out Scene scene)) return;
            // BirthBoundary is an exclusion fence, never an ownership fallback.
            if (scene.Retired || !Active(scene.OwnerLife) || controller.BirthBoundary == 0 || controller.BirthBoundary != scene.OwnerLife)
            { controller.Retired = true; return; }
            controller.OwnerLife = scene.OwnerLife; controller.SceneLife = scene.Life; controller.OperationLife = scene.OperationLife;
        }

        private MapOriginStatus Resolve(Selection selection, out MapOriginChoiceEvidence evidence)
        {
            evidence = null; Controller controller = _controllers[selection.ControllerLife];
            if (controller.Retired || !Active(controller.BirthBoundary)) return MapOriginStatus.Retired;
            if (controller.OwnerLife == 0) return MapOriginStatus.Pending;
            if (!Active(controller.OwnerLife) || !_scenes.TryGetValue(controller.SceneHandle, out Scene scene) || scene.Retired)
                return MapOriginStatus.Retired;
            Owner owner = _owners[controller.OwnerLife]; if (owner.Route == null) return MapOriginStatus.Pending;
            MapRouteScene selectedScene = null;
            foreach (MapRouteScene candidate in owner.Route.Scenes)
                if (candidate.SceneName == controller.SceneName) { selectedScene = candidate; break; }
            if (selectedScene == null) return MapOriginStatus.Unbound;
            if (selection.Choice.SceneId != 0 && selection.Choice.SceneId != selectedScene.SceneId)
                return Fail(MapOriginStatus.Conflict, "Selection scene differs from its birth scene.");
            MapGroupSelection choice = CopyChoice(selection.Choice); choice.SceneId = selectedScene.SceneId;
            evidence = new MapOriginChoiceEvidence
            {
                OwnerLife = owner.Life, ContextPointer = owner.ContextPointer, RouteFingerprint = owner.Fingerprint,
                OperationLife = controller.OperationLife, LoadKey = _operations[controller.OperationLife].LoadKey,
                SceneLife = controller.SceneLife, SceneHandle = controller.SceneHandle, ControllerLife = controller.Life,
                ControllerSceneName = controller.SceneName, CallbackSequence = selection.Sequence, Choice = choice
            };
            return MapOriginStatus.Accepted;
        }

        private void FlushPending()
        {
            int index = 0;
            while (index < _pending.Count && _fault == null)
            {
                MapOriginStatus status = Resolve(_pending[index], out MapOriginChoiceEvidence evidence);
                if (status == MapOriginStatus.Pending) { index++; continue; }
                if (_fault != null) return;
                _pending.RemoveAt(index);
                if (status == MapOriginStatus.Accepted) _ready.Enqueue(evidence);
                else if (status == MapOriginStatus.Unbound) _unboundChoices++;
            }
        }

        private void RemoveSelections(Predicate<Selection> remove)
        {
            _pending.RemoveAll(remove);
            int count = _ready.Count;
            for (int index = 0; index < count; index++)
            {
                MapOriginChoiceEvidence item = _ready.Dequeue();
                if (!remove(new Selection { ControllerLife = item.ControllerLife })) _ready.Enqueue(item);
            }
        }

        private MapOriginStatus UnboundChoice() { _unboundChoices++; return MapOriginStatus.Unbound; }

        private static bool ValidChoice(MapGroupSelection choice) => choice != null && choice.SceneId >= 0 &&
            Text(choice.ControllerAddress, MapSelections.MaxControllerAddress, false) &&
            Text(choice.SelectedPrefabName, MapSelections.MaxPrefabName, !choice.Addressable) &&
            Text(choice.PrefabObjectName, MapSelections.MaxPrefabName, choice.Addressable);

        private static bool Text(string value, int maximum, bool optional)
        {
            if (value == null) return optional;
            if (value.Length > maximum || (!optional && string.IsNullOrWhiteSpace(value))) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index]; if (char.IsControl(character)) return false;
                if (!char.IsSurrogate(character)) continue;
                if (!char.IsHighSurrogate(character) || index + 1 >= value.Length || !char.IsLowSurrogate(value[++index])) return false;
            }
            return true;
        }

        private static bool EqualChoice(MapGroupSelection first, MapGroupSelection second) => first != null && second != null &&
            first.SceneId == second.SceneId && first.ControllerAddress == second.ControllerAddress && first.Addressable == second.Addressable &&
            (first.SelectedPrefabName ?? "") == (second.SelectedPrefabName ?? "") && (first.PrefabObjectName ?? "") == (second.PrefabObjectName ?? "");

        private static MapGroupSelection CopyChoice(MapGroupSelection choice) => new MapGroupSelection
        {
            SceneId = choice.SceneId, ControllerAddress = choice.ControllerAddress, Addressable = choice.Addressable,
            SelectedPrefabName = choice.SelectedPrefabName ?? "", PrefabObjectName = choice.PrefabObjectName ?? ""
        };

        private static MapOriginChoiceEvidence CopyEvidence(MapOriginChoiceEvidence item) => new MapOriginChoiceEvidence
        {
            OwnerLife = item.OwnerLife, ContextPointer = item.ContextPointer, RouteFingerprint = item.RouteFingerprint,
            OperationLife = item.OperationLife, LoadKey = item.LoadKey, SceneLife = item.SceneLife, SceneHandle = item.SceneHandle,
            ControllerLife = item.ControllerLife, ControllerSceneName = item.ControllerSceneName,
            CallbackSequence = item.CallbackSequence, Choice = CopyChoice(item.Choice)
        };

        private sealed class Owner { public long Life, Pointer, ContextPointer; public bool Retired; public string Fingerprint; public MapRouteSelection Route; }
        private sealed class Iterator { public long Life, Pointer, OwnerLife; public bool Retired; }
        private sealed class Operation { public long Life, Pointer, OwnerLife, SceneLife; public int Version, SceneHandle; public string LoadKey; public bool Retired; }
        private sealed class Scene { public long Life, OwnerLife, OperationLife; public int Handle; public bool Retired; }
        private sealed class Controller
        {
            public long Life, Pointer, BirthBoundary, OwnerLife, SceneLife, OperationLife, LastSequence;
            public int SceneHandle; public string SceneName; public bool Retired; public MapGroupSelection LastChoice;
        }
        private sealed class Scope { public long Token, OwnerLife; }
        private sealed class Selection { public long ControllerLife, Sequence; public MapGroupSelection Choice; }
    }
}
