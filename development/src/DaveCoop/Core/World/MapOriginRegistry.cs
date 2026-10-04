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

    // A copied, immutable local chain. Capturing it does not prove that the
    // caller supplied native observations or grant permission to adopt a world.
    public sealed class MapOriginControllerSource
    {
        public long ControllerLife { get; }
        public long ControllerPointer { get; }
        public long OwnerLife { get; }
        public long OperationLife { get; }
        public long OperationPointer { get; }
        public int OperationVersion { get; }
        public long SceneLife { get; }
        public int SceneHandle { get; }
        public string SceneName { get; }
        public string LoadKey { get; }
        public bool ObservationOnly => true;
        public bool NativeGenerationBound => false;
        public bool HostSelectionApplied => false;
        public bool NativePermission => false;
        public bool CrossMachineAddressVerified => false;

        internal MapOriginControllerSource(long controllerLife, long controllerPointer, long ownerLife,
            long operationLife, long operationPointer, int operationVersion, long sceneLife,
            int sceneHandle, string sceneName, string loadKey)
        {
            ControllerLife = controllerLife; ControllerPointer = controllerPointer; OwnerLife = ownerLife;
            OperationLife = operationLife; OperationPointer = operationPointer; OperationVersion = operationVersion;
            SceneLife = sceneLife; SceneHandle = sceneHandle; SceneName = sceneName; LoadKey = loadKey;
        }
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
        public const int MaxManagers = 32;
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
        private readonly Dictionary<long, Manager> _managers = new Dictionary<long, Manager>();
        private readonly Dictionary<long, Manager> _managerPointers = new Dictionary<long, Manager>();
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
        public int ManagerCount { get { lock (_gate) return _managers.Count; } }
        public int PendingManagerCount
        {
            get { lock (_gate) { int count = 0; foreach (Manager item in _managers.Values)
                if (!item.Retired && item.OwnerLife == 0 && item.BoundaryOwnerLife != 0) count++; return count; } }
        }
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
                    Scope scope = _scopes[_scopes.Count - 1];
                    return Active(scope.OwnerLife) && ScopeIteratorLive(scope) ? scope.OwnerLife : 0;
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

        // Only operations already observed when the actual manager was born
        // are eligible. Their fixed scopes supply an exclusion boundary, not a
        // current-owner fallback. Completion must also match the birth handle.
        public MapOriginStatus RegisterManagerBirth(long pointer, int sceneHandle,
            long[] eligibleOperationLives, out long managerLife)
        {
            lock (_gate)
            {
                managerLife = 0; MapOriginStatus status = Guard();
                if (status != MapOriginStatus.Accepted) return status;
                if (pointer == 0 || sceneHandle == 0 || eligibleOperationLives == null || eligibleOperationLives.Length > MaxOperations)
                    return Fail(MapOriginStatus.Invalid, "Invalid manager birth.");
                var candidates = (long[])eligibleOperationLives.Clone(); Array.Sort(candidates);
                long boundary = 0;
                for (int index = 0; index < candidates.Length; index++)
                {
                    if (candidates[index] <= 0 || (index > 0 && candidates[index] == candidates[index - 1]) ||
                        !_operations.TryGetValue(candidates[index], out Operation operation))
                        return Fail(MapOriginStatus.Invalid, "Invalid manager operation set.");
                    if (operation.Retired || !Active(operation.OwnerLife)) return MapOriginStatus.Retired;
                    if (boundary != 0 && boundary != operation.OwnerLife)
                        return Fail(MapOriginStatus.Conflict, "Manager operation set spans owners.");
                    boundary = operation.OwnerLife;
                }
                if (_managerPointers.TryGetValue(pointer, out Manager previous))
                {
                    managerLife = previous.Life;
                    if (previous.Retired) return MapOriginStatus.Retired;
                    if (previous.SceneHandle != sceneHandle || !EqualLives(previous.EligibleOperations, candidates))
                        return Fail(MapOriginStatus.Conflict, "Manager birth cannot change.");
                    return previous.OwnerLife != 0 ? MapOriginStatus.Duplicate :
                        previous.BoundaryOwnerLife == 0 ? MapOriginStatus.Unbound : MapOriginStatus.Pending;
                }
                if (_retiredSceneHandles.Contains(sceneHandle)) return MapOriginStatus.Retired;
                if (_managers.Count == MaxManagers) return Fail(MapOriginStatus.LimitExceeded, "Manager quota.");
                if (!Next(out managerLife)) return MapOriginStatus.LimitExceeded;
                var item = new Manager { Life = managerLife, Pointer = pointer, SceneHandle = sceneHandle,
                    BoundaryOwnerLife = boundary, EligibleOperations = candidates };
                _managers.Add(managerLife, item); _managerPointers.Add(pointer, item);
                if (_scenes.TryGetValue(sceneHandle, out Scene scene) && !scene.Retired && Active(scene.OwnerLife))
                {
                    // Already-completed actual scene ownership needs no pending
                    // operation. A nonempty frozen set must agree with it.
                    if (boundary != 0 && (boundary != scene.OwnerLife || !Contains(candidates, scene.OperationLife)))
                    { RetireManagerLocked(item, false); return MapOriginStatus.Retired; }
                    item.BoundaryOwnerLife = scene.OwnerLife; BindManager(item, scene);
                }
                return item.Retired ? MapOriginStatus.Retired : item.OwnerLife != 0 ? MapOriginStatus.Accepted :
                    boundary == 0 ? MapOriginStatus.Unbound : MapOriginStatus.Pending;
            }
        }

        public MapOriginStatus RegisterManagerIterator(long managerLife, long managerPointer,
            long iteratorPointer, out long iteratorLife)
        {
            lock (_gate)
            {
                iteratorLife = 0; MapOriginStatus status = Guard(); if (status != MapOriginStatus.Accepted) return status;
                if (!_managers.TryGetValue(managerLife, out Manager manager)) return MapOriginStatus.Unbound;
                if (manager.Pointer != managerPointer || iteratorPointer == 0)
                    return Fail(MapOriginStatus.Conflict, "Manager iterator identity differs from birth.");
                if (manager.Retired || (manager.BoundaryOwnerLife != 0 && !Active(manager.BoundaryOwnerLife))) return MapOriginStatus.Retired;
                if (manager.IteratorLife != 0)
                {
                    Iterator previous = _iterators[manager.IteratorLife]; iteratorLife = previous.Life;
                    if (previous.Pointer != iteratorPointer) return Fail(MapOriginStatus.Conflict, "Manager returned another iterator.");
                    return previous.Retired ? MapOriginStatus.Retired : MapOriginStatus.Duplicate;
                }
                if (_iteratorPointers.ContainsKey(iteratorPointer))
                    return Fail(MapOriginStatus.Conflict, "Manager iterator pointer was already registered.");
                if (_iterators.Count == MaxIterators) return Fail(MapOriginStatus.LimitExceeded, "Iterator quota.");
                if (!Next(out iteratorLife)) return MapOriginStatus.LimitExceeded;
                var item = new Iterator { Life = iteratorLife, Pointer = iteratorPointer,
                    OwnerLife = manager.OwnerLife, ManagerLife = managerLife };
                _iterators.Add(iteratorLife, item); _iteratorPointers.Add(iteratorPointer, item); manager.IteratorLife = iteratorLife;
                return manager.OwnerLife != 0 ? MapOriginStatus.Accepted :
                    manager.BoundaryOwnerLife != 0 ? MapOriginStatus.Pending : MapOriginStatus.Unbound;
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
                long owner = 0, fixedIterator = 0;
                if (_iterators.TryGetValue(iteratorLife, out Iterator item) && item.Pointer == pointer &&
                    !item.Retired && ControllerIteratorLive(item))
                { fixedIterator = item.Life; if (Active(item.OwnerLife)) owner = item.OwnerLife; }
                return PushScope(owner, out scopeToken, fixedIterator);
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
                Owner owner = ScopeOwner(scopeToken);
                Manager manager = ScopeManager(scopeToken);
                if (owner == null)
                {
                    if (manager == null || manager.Retired || !Active(manager.BoundaryOwnerLife)) return MapOriginStatus.Unbound;
                    if (manager.PendingRoute != null || manager.RouteCommitted)
                    { RetireOwnerLocked(manager.BoundaryOwnerLife); return MapOriginStatus.Retired; }
                    if (contextPointer == 0) return Fail(MapOriginStatus.Invalid, "Missing route context.");
                    try { manager.PendingRoute = MapSelections.CopyRoute(route); }
                    catch (ArgumentException) { return Fail(MapOriginStatus.Invalid, "Invalid pending route copy."); }
                    manager.ContextPointer = contextPointer;
                    if (manager.OwnerLife == 0) return MapOriginStatus.Pending;
                    return CommitManagerRoute(manager);
                }
                // A cache/restore is a new boundary even when the bytes match.
                // An existing iterator never acquires the replacement context.
                if (owner.Route != null) { RetireOwnerLocked(owner.Life); return MapOriginStatus.Retired; }
                if (contextPointer == 0) return Fail(MapOriginStatus.Invalid, "Missing route context.");
                MapRouteSelection copy;
                try { copy = MapSelections.CopyRoute(route); }
                catch (ArgumentException) { return Fail(MapOriginStatus.Invalid, "Invalid route copy."); }
                owner.ContextPointer = contextPointer; owner.Route = copy;
                owner.Fingerprint = MapSelections.FingerprintRoute(copy);
                if (manager != null) { manager.RouteCommitted = true; manager.ContextPointer = contextPointer; }
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
                foreach (Manager manager in _managers.Values)
                    if (!manager.Retired && manager.SceneHandle == sceneHandle) BindManager(manager, _scenes[sceneHandle]);
                foreach (Controller controller in _controllers.Values)
                    if (!controller.Retired && controller.SceneHandle == sceneHandle) BindController(controller);
                FlushPending(); return _fault != null ? MapOriginStatus.Faulted :
                    Active(operation.OwnerLife) ? MapOriginStatus.Accepted : MapOriginStatus.Retired;
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
                        previous.EligibleOperations.Length == 0 ? MapOriginStatus.Unbound : MapOriginStatus.Pending;
                }
                if (_retiredSceneHandles.Contains(sceneHandle)) return MapOriginStatus.Retired;
                if (_controllers.Count == MaxControllers) return Fail(MapOriginStatus.LimitExceeded, "Controller quota.");
                if (!Next(out controllerLife)) return MapOriginStatus.LimitExceeded;
                // Freeze only the operations already observed at first birth.
                // A repeated Start/Init never grows this set with later loads.
                var eligible = new List<long>();
                foreach (Operation operation in _operations.Values)
                    if (!operation.Retired && operation.OwnerLife == _activeOwnerLife && Active(operation.OwnerLife))
                        eligible.Add(operation.Life);
                eligible.Sort();
                var item = new Controller { Life = controllerLife, Pointer = pointer, SceneHandle = sceneHandle,
                    SceneName = sceneName, BirthBoundary = _activeOwnerLife, EligibleOperations = eligible.ToArray() };
                _controllers.Add(controllerLife, item); _controllerPointers.Add(pointer, item);
                BindController(item);
                if (item.Retired) return MapOriginStatus.Retired;
                return item.OwnerLife != 0 ? MapOriginStatus.Accepted :
                    item.EligibleOperations.Length == 0 ? MapOriginStatus.Unbound : MapOriginStatus.Pending;
            }
        }

        // The actual controller Init factory may return before its scene load
        // completes. Only this dedicated association can later acquire that
        // controller's once-proven owner; a generic owner-zero iterator cannot.
        public MapOriginStatus RegisterControllerIterator(long controllerLife, long controllerPointer,
            long iteratorPointer, out long iteratorLife)
        {
            lock (_gate)
            {
                iteratorLife = 0; MapOriginStatus status = Guard(); if (status != MapOriginStatus.Accepted) return status;
                if (!_controllers.TryGetValue(controllerLife, out Controller controller)) return MapOriginStatus.Unbound;
                if (controller.Pointer != controllerPointer || iteratorPointer == 0)
                    return Fail(MapOriginStatus.Conflict, "Controller iterator identity differs from birth.");
                if (controller.Retired || (controller.BirthBoundary != 0 && !Active(controller.BirthBoundary))) return MapOriginStatus.Retired;
                if (controller.IteratorLife != 0)
                {
                    Iterator previous = _iterators[controller.IteratorLife]; iteratorLife = previous.Life;
                    if (previous.Pointer != iteratorPointer) return Fail(MapOriginStatus.Conflict, "Controller returned another iterator.");
                    return previous.Retired ? MapOriginStatus.Retired : MapOriginStatus.Duplicate;
                }
                if (_iteratorPointers.ContainsKey(iteratorPointer))
                    return Fail(MapOriginStatus.Conflict, "Controller iterator pointer was already registered.");
                if (_iterators.Count == MaxIterators) return Fail(MapOriginStatus.LimitExceeded, "Iterator quota.");
                if (!Next(out iteratorLife)) return MapOriginStatus.LimitExceeded;
                var item = new Iterator { Life = iteratorLife, Pointer = iteratorPointer,
                    OwnerLife = controller.OwnerLife, ControllerLife = controllerLife };
                _iterators.Add(iteratorLife, item); _iteratorPointers.Add(iteratorPointer, item); controller.IteratorLife = iteratorLife;
                return controller.OwnerLife != 0 ? MapOriginStatus.Accepted :
                    controller.EligibleOperations.Length == 0 ? MapOriginStatus.Unbound : MapOriginStatus.Pending;
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

        public bool TryGetControllerSource(long controllerLife, out MapOriginControllerSource source)
        {
            lock (_gate)
            {
                source = null;
                if (Guard() != MapOriginStatus.Accepted || !_controllers.TryGetValue(controllerLife, out Controller controller) ||
                    !ControllerHasSource(controller)) return false;
                Operation operation = _operations[controller.OperationLife];
                source = new MapOriginControllerSource(controller.Life, controller.Pointer, controller.OwnerLife,
                    operation.Life, operation.Pointer, operation.Version, controller.SceneLife, controller.SceneHandle,
                    controller.SceneName, operation.LoadKey);
                return true;
            }
        }

        public bool IsControllerRetired(long controllerLife)
        {
            lock (_gate) return Guard() != MapOriginStatus.Accepted ||
                !_controllers.TryGetValue(controllerLife, out Controller item) || item.Retired;
        }

        public bool TryGetManagerLife(long pointer, out long life)
        {
            lock (_gate) { life = 0; if (Guard() != MapOriginStatus.Accepted ||
                !_managerPointers.TryGetValue(pointer, out Manager item)) return false; life = item.Life; return true; }
        }

        public bool TryGetManagerOwner(long managerLife, out long ownerLife)
        {
            lock (_gate) { ownerLife = 0; if (Guard() != MapOriginStatus.Accepted ||
                !_managers.TryGetValue(managerLife, out Manager item) || item.Retired || !Active(item.OwnerLife)) return false;
                ownerLife = item.OwnerLife; return true; }
        }

        public bool IsManagerRetired(long managerLife)
        {
            lock (_gate) return Guard() != MapOriginStatus.Accepted ||
                !_managers.TryGetValue(managerLife, out Manager item) || item.Retired;
        }

        // Also exposes the fixed operation boundary of a staged route for
        // retirement only. It does not make that route a published source.
        public bool TryGetRouteOwner(long contextPointer, out long ownerLife)
        {
            lock (_gate)
            {
                ownerLife = 0; if (Guard() != MapOriginStatus.Accepted || contextPointer == 0) return false;
                foreach (Owner owner in _owners.Values)
                    if (!owner.Retired && owner.ContextPointer == contextPointer) { ownerLife = owner.Life; return true; }
                foreach (Manager manager in _managers.Values)
                    if (!manager.Retired && manager.ContextPointer == contextPointer && Active(manager.BoundaryOwnerLife))
                    { ownerLife = manager.BoundaryOwnerLife; return true; }
                return false;
            }
        }

        public MapOriginStatus RetireManager(long managerLife)
        {
            lock (_gate)
            {
                MapOriginStatus status = Guard(); if (status != MapOriginStatus.Accepted) return status;
                if (!_managers.TryGetValue(managerLife, out Manager item)) return MapOriginStatus.Unbound;
                if (item.Retired) return MapOriginStatus.Duplicate;
                // A failed pending manager also invalidates its fixed entry;
                // its partially captured route must never become current later.
                if (Active(item.BoundaryOwnerLife)) RetireOwnerLocked(item.BoundaryOwnerLife);
                else RetireManagerLocked(item, false);
                return MapOriginStatus.Accepted;
            }
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
                RetireControllerLocked(item); RemoveSelections(selection => selection.ControllerLife == controllerLife);
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
                foreach (Manager manager in _managers.Values)
                    if (manager.SceneHandle == sceneHandle) RetireManagerLocked(manager, true);
                foreach (Controller controller in _controllers.Values)
                    if (controller.SceneHandle == sceneHandle) RetireControllerLocked(controller);
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
            foreach (Controller controller in _controllers.Values) RetireControllerLocked(controller);
            foreach (Manager manager in _managers.Values) RetireManagerLocked(manager, false);
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

        private MapOriginStatus PushScope(long ownerLife, out long scopeToken, long iteratorLife = 0)
        {
            scopeToken = 0;
            if (_scopes.Count == MaxScopes) return Fail(MapOriginStatus.LimitExceeded, "Scope quota.");
            if (!Next(out scopeToken)) return MapOriginStatus.LimitExceeded;
            _scopes.Add(new Scope { Token = scopeToken, OwnerLife = ownerLife, IteratorLife = iteratorLife });
            return ownerLife == 0 ? MapOriginStatus.Unbound : MapOriginStatus.Accepted;
        }

        private Owner ScopeOwner(long scopeToken)
        {
            if (_scopes.Count == 0 || scopeToken == 0 || _scopes[_scopes.Count - 1].Token != scopeToken) return null;
            Scope scope = _scopes[_scopes.Count - 1];
            return Active(scope.OwnerLife) && ScopeIteratorLive(scope) ? _owners[scope.OwnerLife] : null;
        }

        private bool ScopeIteratorLive(Scope scope) => scope.IteratorLife == 0 ||
            (_iterators.TryGetValue(scope.IteratorLife, out Iterator iterator) && !iterator.Retired && ControllerIteratorLive(iterator));

        private bool ControllerIteratorLive(Iterator iterator)
        {
            if (iterator.ControllerLife == 0) return true;
            if (!_controllers.TryGetValue(iterator.ControllerLife, out Controller controller) || controller.Retired ||
                controller.IteratorLife != iterator.Life || controller.OwnerLife != iterator.OwnerLife) return false;
            return controller.OwnerLife == 0 ? controller.BirthBoundary == 0 || Active(controller.BirthBoundary) : ControllerHasSource(controller);
        }

        private Manager ScopeManager(long scopeToken)
        {
            if (_scopes.Count == 0 || scopeToken == 0 || _scopes[_scopes.Count - 1].Token != scopeToken) return null;
            long iteratorLife = _scopes[_scopes.Count - 1].IteratorLife;
            if (!_iterators.TryGetValue(iteratorLife, out Iterator iterator) || iterator.Retired ||
                !_managers.TryGetValue(iterator.ManagerLife, out Manager manager) || manager.IteratorLife != iteratorLife) return null;
            return manager;
        }

        private void RetireOwnerLocked(long ownerLife)
        {
            if (!_owners.TryGetValue(ownerLife, out Owner owner)) return;
            owner.Retired = true;
            foreach (Manager manager in _managers.Values)
                if (manager.BoundaryOwnerLife == ownerLife || manager.OwnerLife == ownerLife) RetireManagerLocked(manager, false);
            foreach (Iterator iterator in _iterators.Values) if (iterator.OwnerLife == ownerLife) iterator.Retired = true;
            foreach (Operation operation in _operations.Values) if (operation.OwnerLife == ownerLife) operation.Retired = true;
            foreach (Scene scene in _scenes.Values)
                if (scene.OwnerLife == ownerLife) { scene.Retired = true; _retiredSceneHandles.Add(scene.Handle); }
            foreach (Controller controller in _controllers.Values)
                if (controller.BirthBoundary == ownerLife || controller.OwnerLife == ownerLife) RetireControllerLocked(controller);
            RemoveSelections(selection => _controllers[selection.ControllerLife].Retired);
            if (_activeOwnerLife == ownerLife) _activeOwnerLife = 0;
        }

        private void RetireManagerLocked(Manager manager, bool retireRouteOwner)
        {
            if (manager.Retired) return;
            manager.Retired = true; manager.PendingRoute = null;
            if (manager.IteratorLife != 0) _iterators[manager.IteratorLife].Retired = true;
            if (retireRouteOwner && manager.RouteCommitted && Active(manager.OwnerLife)) RetireOwnerLocked(manager.OwnerLife);
        }

        private void BindManager(Manager manager, Scene scene)
        {
            if (manager.Retired || manager.OwnerLife != 0) return;
            if (scene.Retired || !Active(scene.OwnerLife) || manager.BoundaryOwnerLife == 0 ||
                manager.BoundaryOwnerLife != scene.OwnerLife ||
                (manager.EligibleOperations.Length != 0 && !Contains(manager.EligibleOperations, scene.OperationLife)))
            { RetireManagerLocked(manager, false); return; }
            manager.OwnerLife = scene.OwnerLife; manager.OperationLife = scene.OperationLife; manager.SceneLife = scene.Life;
            if (manager.IteratorLife != 0)
            {
                Iterator iterator = _iterators[manager.IteratorLife];
                if (iterator.Retired || iterator.ManagerLife != manager.Life || iterator.OwnerLife != 0)
                { Fail(MapOriginStatus.Conflict, "Pending manager iterator cannot be rebound."); return; }
                iterator.OwnerLife = scene.OwnerLife;
            }
            if (manager.PendingRoute != null) CommitManagerRoute(manager);
        }

        private MapOriginStatus CommitManagerRoute(Manager manager)
        {
            if (manager.Retired || !Active(manager.OwnerLife)) return MapOriginStatus.Retired;
            Owner owner = _owners[manager.OwnerLife];
            if (owner.Route != null || manager.RouteCommitted)
            { RetireOwnerLocked(owner.Life); return MapOriginStatus.Retired; }
            owner.ContextPointer = manager.ContextPointer; owner.Route = manager.PendingRoute;
            owner.Fingerprint = MapSelections.FingerprintRoute(owner.Route);
            manager.PendingRoute = null; manager.RouteCommitted = true;
            FlushPending(); return _fault == null ? MapOriginStatus.Accepted : MapOriginStatus.Faulted;
        }

        private static bool Contains(long[] values, long value) => Array.BinarySearch(values, value) >= 0;
        private static bool EqualLives(long[] first, long[] second)
        {
            if (first.Length != second.Length) return false;
            for (int index = 0; index < first.Length; index++) if (first[index] != second[index]) return false;
            return true;
        }

        private void BindController(Controller controller)
        {
            if (controller.Retired || controller.OwnerLife != 0 || !_scenes.TryGetValue(controller.SceneHandle, out Scene scene)) return;
            // BirthBoundary is an exclusion fence, never an ownership fallback.
            if (scene.Retired || !Active(scene.OwnerLife) || controller.BirthBoundary == 0 || controller.BirthBoundary != scene.OwnerLife ||
                !Contains(controller.EligibleOperations, scene.OperationLife))
            { RetireControllerLocked(controller); return; }
            controller.OwnerLife = scene.OwnerLife; controller.SceneLife = scene.Life; controller.OperationLife = scene.OperationLife;
            if (controller.IteratorLife != 0)
            {
                Iterator iterator = _iterators[controller.IteratorLife];
                if (iterator.Retired || iterator.ControllerLife != controller.Life || iterator.OwnerLife != 0)
                { Fail(MapOriginStatus.Conflict, "Pending controller iterator cannot be rebound."); return; }
                iterator.OwnerLife = scene.OwnerLife;
            }
        }

        private void RetireControllerLocked(Controller controller)
        {
            controller.Retired = true;
            if (controller.IteratorLife != 0) _iterators[controller.IteratorLife].Retired = true;
        }

        private bool ControllerHasSource(Controller controller)
        {
            return !controller.Retired && controller.OwnerLife == _activeOwnerLife && Active(controller.OwnerLife) &&
                controller.BirthBoundary == controller.OwnerLife && controller.SceneLife != 0 && controller.OperationLife != 0 &&
                Contains(controller.EligibleOperations, controller.OperationLife) &&
                _scenes.TryGetValue(controller.SceneHandle, out Scene scene) && !scene.Retired &&
                scene.Life == controller.SceneLife && scene.OwnerLife == controller.OwnerLife && scene.OperationLife == controller.OperationLife &&
                _operations.TryGetValue(controller.OperationLife, out Operation operation) && !operation.Retired &&
                operation.OwnerLife == controller.OwnerLife && operation.SceneLife == controller.SceneLife && operation.SceneHandle == controller.SceneHandle;
        }

        private MapOriginStatus Resolve(Selection selection, out MapOriginChoiceEvidence evidence)
        {
            evidence = null; Controller controller = _controllers[selection.ControllerLife];
            if (controller.Retired || !Active(controller.BirthBoundary)) return MapOriginStatus.Retired;
            if (controller.OwnerLife == 0) return controller.EligibleOperations.Length == 0 ? MapOriginStatus.Unbound : MapOriginStatus.Pending;
            if (!ControllerHasSource(controller)) return MapOriginStatus.Retired;
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
        private sealed class Iterator { public long Life, Pointer, OwnerLife, ManagerLife, ControllerLife; public bool Retired; }
        private sealed class Manager
        {
            public long Life, Pointer, BoundaryOwnerLife, OwnerLife, OperationLife, SceneLife, IteratorLife, ContextPointer;
            public int SceneHandle; public bool Retired, RouteCommitted;
            public long[] EligibleOperations; public MapRouteSelection PendingRoute;
        }
        private sealed class Operation { public long Life, Pointer, OwnerLife, SceneLife; public int Version, SceneHandle; public string LoadKey; public bool Retired; }
        private sealed class Scene { public long Life, OwnerLife, OperationLife; public int Handle; public bool Retired; }
        private sealed class Controller
        {
            public long Life, Pointer, BirthBoundary, OwnerLife, SceneLife, OperationLife, LastSequence, IteratorLife;
            public int SceneHandle; public string SceneName; public bool Retired; public MapGroupSelection LastChoice;
            public long[] EligibleOperations;
        }
        private sealed class Scope { public long Token, OwnerLife, IteratorLife; }
        private sealed class Selection { public long ControllerLife, Sequence; public MapGroupSelection Choice; }
    }
}
