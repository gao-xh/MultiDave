using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using SceneLoadHandle = UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<UnityEngine.ResourceManagement.ResourceProviders.SceneInstance>;

namespace DaveCoop.Networking
{
    internal enum MapOriginMethod
    {
        Entry, ChangeScene, ChangeLevel, CoChangeFactory, CoLevelFactory,
        AdditiveFactory, ChildAdditiveFactory, CoLoadFactory, ManagerFactory, ControllerFactory,
        CoChangeMoveNext, CoLevelMoveNext, AdditiveMoveNext, ChildAdditiveMoveNext,
        CoLoadMoveNext, ManagerMoveNext, ControllerMoveNext, AddressablesLoad,
        RouteCached, RouteRestored, ContextClear, ContextReset, ContextClearCache, ContextCreated,
        SceneLoaded, SceneLoadedCustom, SceneUnloaded, ControllerDestroy, Choice
    }
    internal enum MapOriginStage { Before, After, Finally }

    // Synchronous view only. Capture must check the Unity thread before reading
    // pointers/fields. None of these native arguments may enter its CLR queue.
    internal sealed class MapOriginCallback
    {
        public long ProcessSequence { get; internal set; }
        public long CallId { get; internal set; }
        public int ManagedThreadId { get; internal set; }
        public MapOriginMethod Method { get; internal set; }
        public MapOriginStage Stage { get; internal set; }
        public Il2CppObjectBase Instance { get; internal set; }
        public object[] Arguments { get; internal set; }
        public Il2CppSystem.Collections.IEnumerator Iterator { get; internal set; }
        public SceneLoadHandle Handle { get; internal set; }
        public IGPSetInfo Selection { get; internal set; }
        public Scene? SceneValue { get; internal set; }
        public bool? OriginalMoveNext { get; internal set; }
        public string OriginalException { get; internal set; }
    }

    // Observes natural calls. No original input/result is changed, no execution
    // is skipped and no framework completion delegate is installed. Reflection
    // matches declarations only; it does not invoke any original getter.
    internal sealed class MapOriginHooks : IDisposable
    {
        private const string Owner = Plugin.Id + ".map-origin";
        public const int MaxProcessEvents = 8192;
        public const int MaxPendingCalls = 256;
        private static MapOriginHooks _active;
        private static long _accepted, _nextCallId, _dropped, _unmatched, _discarded;
        private static int _failed, _callbackErrors;
        private readonly object _gate = new object();
        private readonly Dictionary<long, Call> _calls = new Dictionary<long, Call>();
        private readonly Dictionary<MethodBase, MapOriginMethod> _methods = new Dictionary<MethodBase, MapOriginMethod>();
        private List<Target> _targets;
        private Harmony _harmony;
        private Action<MapOriginCallback> _copy;
        private bool _accepting, _cleanupVerified = true;
        private sealed class Call { public MapOriginMethod Method; public int Thread; }
        private sealed class Target
        {
            public MethodInfo Original, Prefix, Postfix, Finalizer;
        }
        public bool Installed => _harmony != null && !Failed;
        public bool Healthy => Installed && CallbackErrors == 0;
        public bool Failed => Volatile.Read(ref _failed) != 0;
        public int CallbackErrors => Volatile.Read(ref _callbackErrors);
        public long ProcessAccepted => Interlocked.Read(ref _accepted);
        public long Dropped => Interlocked.Read(ref _dropped);
        public long UnmatchedAfter => Interlocked.Read(ref _unmatched);
        public long DiscardedCalls => Interlocked.Read(ref _discarded);
        public bool ProcessLimitReached => ProcessAccepted >= MaxProcessEvents;
        public bool CleanupVerified => _cleanupVerified;
        public int TargetCount => _targets?.Count ?? 0;
        public bool NativeTypedReturnAbiVerified => false;
        public int PendingCalls { get { lock (_gate) return _calls.Count; } }

        public void Enable(Action<MapOriginCallback> copy)
        {
            if (copy == null) throw new ArgumentNullException(nameof(copy));
            if (Failed || ProcessLimitReached) throw new InvalidOperationException("Map origin observer needs a process restart after failure/quota exhaustion.");
            if (Installed) return;
            MapOriginHooks previous = Interlocked.CompareExchange(ref _active, this, null);
            if (previous != null && !ReferenceEquals(previous, this)) throw new InvalidOperationException("Another map origin observer is active.");
            try
            {
                _targets = CreateTargets();
                _harmony = new Harmony(Owner); _cleanupVerified = false;
                foreach (Target target in _targets)
                {
                    _harmony.Patch(target.Original, new HarmonyMethod(target.Prefix),
                        new HarmonyMethod(target.Postfix), finalizer: new HarmonyMethod(target.Finalizer));
                    var info = Harmony.GetPatchInfo(target.Original);
                    if (info == null || !info.Owners.Contains(Owner)) throw new InvalidOperationException("Own origin hook registration is missing: " + target.Original.Name);
                }
                lock (_gate) { _copy = copy; _accepting = true; }
            }
            catch (Exception installError)
            {
                Volatile.Write(ref _failed, 1); StopAccepting();
                try { RemoveOwnPatches(); }
                catch (Exception cleanupError) { throw new InvalidOperationException("Map origin installation failed and own cleanup was not verified.", new AggregateException(installError, cleanupError)); }
                throw;
            }
        }

        private List<Target> CreateTargets()
        {
            var targets = new List<Target>(); _methods.Clear();
            Type action = typeof(Il2CppSystem.Action), scene = typeof(DR.GameScene), transition = typeof(SceneTransitionType);
            Add(targets, MapOriginMethod.Entry, typeof(SceneLoader), "GoToInGameEntry", false, typeof(void), new[] { typeof(string), transition, typeof(bool) });
            Add(targets, MapOriginMethod.ChangeScene, typeof(SceneLoader), "ChangeSceneAsync", false, typeof(void),
                new[] { typeof(string), transition, typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), action, action, typeof(bool), typeof(bool), typeof(bool), typeof(bool) });
            Add(targets, MapOriginMethod.ChangeLevel, typeof(SceneLoader), "ChangeLevelSceneAsync", false, typeof(void), new[] { typeof(string), transition, typeof(SceneType), action });
            Type iterator = typeof(Il2CppSystem.Collections.IEnumerator);
            Add(targets, MapOriginMethod.CoChangeFactory, typeof(SceneLoader), "CoChangeSceneAsync", false, iterator,
                new[] { scene, transition, typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), action, action, typeof(bool), typeof(bool), typeof(bool) }, factory: true);
            Add(targets, MapOriginMethod.CoLevelFactory, typeof(SceneLoader), "CoChangeLevelSceneAsync", false, iterator,
                new[] { scene, transition, typeof(bool), action }, factory: true);
            Add(targets, MapOriginMethod.AdditiveFactory, typeof(SceneLoader), "LoadAdditiveScene", false, iterator,
                new[] { typeof(SceneContext.SceneRoadmapData) }, factory: true);
            Add(targets, MapOriginMethod.ChildAdditiveFactory, typeof(SceneLoader), "coLoadAdditiveScene", false, iterator,
                new[] { typeof(int), typeof(Il2CppSystem.Action<UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle>) }, factory: true);
            Add(targets, MapOriginMethod.CoLoadFactory, typeof(SceneLoader), "CoLoadSceneAsync", true, iterator,
                new[] { typeof(string), typeof(LoadSceneMode), typeof(bool) }, factory: true);
            Add(targets, MapOriginMethod.ManagerFactory, typeof(InGameManager), "Start", false, iterator, Type.EmptyTypes, factory: true);
            Add(targets, MapOriginMethod.ControllerFactory, typeof(IGPSetController), "Init", false, iterator, Type.EmptyTypes, factory: true);
            AddMove(targets, MapOriginMethod.CoChangeMoveNext, typeof(SceneLoader._CoChangeSceneAsync_d__111));
            AddMove(targets, MapOriginMethod.CoLevelMoveNext, typeof(SceneLoader._CoChangeLevelSceneAsync_d__114));
            AddMove(targets, MapOriginMethod.AdditiveMoveNext, typeof(SceneLoader._LoadAdditiveScene_d__116));
            AddMove(targets, MapOriginMethod.ChildAdditiveMoveNext, typeof(SceneLoader._coLoadAdditiveScene_d__191));
            AddMove(targets, MapOriginMethod.CoLoadMoveNext, typeof(SceneLoader._CoLoadSceneAsync_d__108));
            AddMove(targets, MapOriginMethod.ManagerMoveNext, typeof(InGameManager._Start_d__111));
            AddMove(targets, MapOriginMethod.ControllerMoveNext, typeof(IGPSetController._Init_d__16));
            Add(targets, MapOriginMethod.AddressablesLoad, typeof(Addressables), "LoadSceneAsync", true, typeof(SceneLoadHandle),
                new[] { typeof(Il2CppSystem.Object), typeof(LoadSceneMode), typeof(bool), typeof(int), typeof(SceneReleaseMode) },
                nameof(AddressablesBefore), nameof(AddressablesAfter));
            Add(targets, MapOriginMethod.RouteCached, typeof(SceneContext), "cacheSelectedScenePath", false, typeof(void), Type.EmptyTypes);
            Add(targets, MapOriginMethod.RouteRestored, typeof(SceneContext), "LoadSceneMapCacheFromSave", false, typeof(void), Type.EmptyTypes);
            Add(targets, MapOriginMethod.ContextClear, typeof(SceneContext), "Clear", false, typeof(void), Type.EmptyTypes);
            Add(targets, MapOriginMethod.ContextReset, typeof(SceneContext), "Reset", false, typeof(void), Type.EmptyTypes);
            Add(targets, MapOriginMethod.ContextClearCache, typeof(SceneContext), "ClearAllCache", false, typeof(void), Type.EmptyTypes);
            Add(targets, MapOriginMethod.ContextCreated, typeof(SceneContext), "OnCreated", false, typeof(void), Type.EmptyTypes);
            Add(targets, MapOriginMethod.SceneLoaded, typeof(SceneLoader), "OnSceneLoaded", false, typeof(void), new[] { typeof(Scene), typeof(LoadSceneMode) }, nameof(SceneBefore));
            Add(targets, MapOriginMethod.SceneLoadedCustom, typeof(SceneLoader), "OnSceneLoadedCustom", false, typeof(void), new[] { typeof(Scene), typeof(LoadSceneMode) }, nameof(SceneBefore));
            Add(targets, MapOriginMethod.SceneUnloaded, typeof(SceneLoader), "OnSceneUnloadedCustom", false, typeof(void), new[] { typeof(Scene) }, nameof(SceneBefore));
            Add(targets, MapOriginMethod.ControllerDestroy, typeof(IGPSetController), "OnDestroy", false, typeof(void), Type.EmptyTypes);
            Add(targets, MapOriginMethod.Choice, typeof(IGPSetController), "GetRandomIGPSetInfo", false, typeof(IGPSetInfo), Type.EmptyTypes,
                nameof(GeneralBefore), nameof(ChoiceAfter));
            return targets;
        }
        private void AddMove(List<Target> targets, MapOriginMethod kind, Type type)
            => Add(targets, kind, type, "MoveNext", false, typeof(bool), Type.EmptyTypes, nameof(GeneralBefore), nameof(MoveAfter));
        private void Add(List<Target> targets, MapOriginMethod kind, Type type, string name, bool isStatic,
            Type result, Type[] parameters, string prefix = null, string postfix = null, bool factory = false)
        {
            MethodInfo method = type.GetMethod(name, BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic |
                (isStatic ? BindingFlags.Static : BindingFlags.Instance), null, parameters, null);
            if (method == null || method.ReturnType != result || method.IsStatic != isStatic || method.IsGenericMethod)
                throw new InvalidOperationException("Exact origin declaration unavailable: " + type.FullName + "." + name);
            _methods.Add(method, kind);
            targets.Add(new Target
            {
                Original = method,
                Prefix = Callback(prefix ?? (isStatic ? nameof(StaticBefore) : nameof(GeneralBefore))),
                Postfix = Callback(postfix ?? (factory ? nameof(FactoryAfter) : nameof(GeneralAfter))),
                Finalizer = Callback(nameof(CallFinally))
            });
        }
        private static MethodInfo Callback(string name)
            => typeof(MapOriginHooks).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
               ?? throw new InvalidOperationException("Own origin callback missing: " + name);

        private static void GeneralBefore(MethodBase __originalMethod, Il2CppObjectBase __instance, object[] __args, out long __state)
            => __state = Begin(__originalMethod, __instance, __args);
        private static void StaticBefore(MethodBase __originalMethod, object[] __args, out long __state)
            => __state = Begin(__originalMethod, null, __args);
        private static void SceneBefore(MethodBase __originalMethod, Il2CppObjectBase __instance, Scene __0, out long __state)
            => __state = Begin(__originalMethod, __instance, null, __0);
        private static void AddressablesBefore(MethodBase __originalMethod, Il2CppSystem.Object __0, LoadSceneMode __1,
            bool __2, int __3, SceneReleaseMode __4, out long __state)
            => __state = Begin(__originalMethod, null, new object[] { __0, __1, __2, __3, __4 });
        private static void GeneralAfter(Il2CppObjectBase __instance, long __state) => After(__state, instance: __instance);
        private static void FactoryAfter(Il2CppSystem.Collections.IEnumerator __result, long __state) => After(__state, iterator: __result);
        private static void AddressablesAfter(SceneLoadHandle __result, long __state) => After(__state, handle: __result);
        private static void ChoiceAfter(IGPSetInfo __result, long __state) => After(__state, selection: __result);
        private static void MoveAfter(bool __result, long __state) => After(__state, move: __result);
        private static void CallFinally(Exception __exception, long __state)
            => Finish(__state, __exception);

        private static long Begin(MethodBase method, Il2CppObjectBase instance, object[] args, Scene? scene = null)
        {
            MapOriginHooks active = Volatile.Read(ref _active);
            if (active == null) return 0;
            try
            {
                lock (active._gate)
                {
                    if (!active._accepting || active.Failed || active._copy == null) return 0;
                    if (active._calls.Count >= MaxPendingCalls || !NextSequence(out long sequence))
                    { Interlocked.Increment(ref _dropped); return 0; }
                    if (!active._methods.TryGetValue(method, out MapOriginMethod kind)) throw new InvalidOperationException("Unregistered origin callback.");
                    long id = Interlocked.Increment(ref _nextCallId);
                    if (id <= 0) throw new InvalidOperationException("Origin call IDs exhausted.");
                    int thread = Environment.CurrentManagedThreadId;
                    active._calls.Add(id, new Call { Method = kind, Thread = thread });
                    active._copy(new MapOriginCallback { ProcessSequence = sequence, CallId = id, ManagedThreadId = thread,
                        Method = kind, Stage = MapOriginStage.Before, Instance = instance, Arguments = args, SceneValue = scene });
                    return id;
                }
            }
            catch { active.CallbackFailed(); return 0; }
        }
        private static void After(long id, Il2CppSystem.Collections.IEnumerator iterator = null,
            SceneLoadHandle handle = null, IGPSetInfo selection = null, bool? move = null, Il2CppObjectBase instance = null)
        {
            MapOriginHooks active = Volatile.Read(ref _active);
            if (active == null || id == 0) return;
            try
            {
                lock (active._gate)
                {
                    if (!active._calls.TryGetValue(id, out Call call)) { Interlocked.Increment(ref _unmatched); return; }
                    if (!active._accepting || active.Failed || active._copy == null) return;
                    if (call.Thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Origin call changed callback thread.");
                    if (!NextSequence(out long sequence)) { Interlocked.Increment(ref _dropped); return; }
                    active._copy(new MapOriginCallback { ProcessSequence = sequence, CallId = id, ManagedThreadId = call.Thread,
                        Method = call.Method, Stage = MapOriginStage.After, Instance = instance, Iterator = iterator, Handle = handle,
                        Selection = selection, OriginalMoveNext = move });
                }
            }
            catch { active.CallbackFailed(); }
        }
        private static void Finish(long id, Exception exception)
        {
            MapOriginHooks active = Volatile.Read(ref _active);
            if (active == null || id == 0) return;
            try
            {
                lock (active._gate)
                {
                    if (!active._calls.TryGetValue(id, out Call call)) return;
                    active._calls.Remove(id);
                    if (!active._accepting || active.Failed || active._copy == null) return;
                    if (call.Thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Origin finalizer changed callback thread.");
                    // Scope cleanup must run even if the observational quota was
                    // reached after a successfully copied prefix.
                    NextSequence(out long sequence);
                    active._copy(new MapOriginCallback { ProcessSequence = sequence, CallId = id, ManagedThreadId = call.Thread,
                        Method = call.Method, Stage = MapOriginStage.Finally,
                        OriginalException = exception == null ? null : exception.GetType().Name });
                }
            }
            catch { active.CallbackFailed(); }
        }
        private static bool NextSequence(out long sequence)
        {
            while (true)
            {
                long current = Interlocked.Read(ref _accepted);
                if (current >= MaxProcessEvents) { sequence = 0; return false; }
                sequence = current + 1;
                if (Interlocked.CompareExchange(ref _accepted, sequence, current) == current) return true;
            }
        }
        private void CallbackFailed()
        {
            Interlocked.Increment(ref _callbackErrors); Volatile.Write(ref _failed, 1); StopAccepting();
        }
        public void CheckHealthy()
        {
            if (!Healthy) throw new InvalidOperationException("Map origin hooks failed or are unavailable.");
        }
        private void StopAccepting()
        {
            lock (_gate)
            {
                _accepting = false; _copy = null;
                Interlocked.Add(ref _discarded, _calls.Count); _calls.Clear();
            }
            Interlocked.CompareExchange(ref _active, null, this);
        }
        private void RemoveOwnPatches()
        {
            _cleanupVerified = false;
            if (_harmony != null) _harmony.UnpatchSelf();
            if (_targets != null)
                foreach (Target target in _targets)
                {
                    var info = Harmony.GetPatchInfo(target.Original);
                    if (info != null && info.Owners.Contains(Owner)) throw new InvalidOperationException("Own origin hook remains: " + target.Original.Name);
                }
            _harmony = null; _targets = null; _methods.Clear(); _cleanupVerified = true;
        }
        public void Dispose()
        {
            StopAccepting();
            if (_harmony == null && _targets == null) return;
            try { RemoveOwnPatches(); }
            catch { Volatile.Write(ref _failed, 1); _cleanupVerified = false; throw; }
        }
    }
}
