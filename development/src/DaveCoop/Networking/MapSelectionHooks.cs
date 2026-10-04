using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using UnityEngine.SceneManagement;

namespace DaveCoop.Networking
{
    internal enum MapSelectionStage
    {
        RouteCachedAfter = 1,
        RouteRestoredAfter = 2,
        IgpSelectedAfter = 3,
        IgpPrefabFactoryBefore = 4,
        SceneLoadCallBefore = 5
    }

    // A synchronous callback view, not a frozen/queued observation. Native
    // wrappers are valid only for the consumer's immediate, thread-checked copy.
    // The consumer must not retain this view or its wrappers for Drain/network use.
    internal sealed class MapSelectionCallback
    {
        public long ProcessSequence { get; }
        public int ManagedThreadId { get; }
        public MapSelectionStage Stage { get; }
        public SceneContext Context { get; }
        public IGPSetController Controller { get; }
        public IGPSetInfo SelectedInfo { get; }
        public IGPSetInfo LoadingInfo { get; }
        public string SceneKey { get; }
        public LoadSceneMode LoadMode { get; }
        public bool ActivateOnLoad { get; }

        internal MapSelectionCallback(long sequence, int threadId, MapSelectionStage stage,
            SceneContext context, IGPSetController controller, IGPSetInfo selectedInfo,
            IGPSetInfo loadingInfo, string sceneKey, LoadSceneMode loadMode, bool activateOnLoad)
        {
            ProcessSequence = sequence; ManagedThreadId = threadId; Stage = stage;
            Context = context; Controller = controller; SelectedInfo = selectedInfo;
            LoadingInfo = loadingInfo; SceneKey = sceneKey; LoadMode = loadMode;
            ActivateOnLoad = activateOnLoad;
        }
    }

    // Only observes natural call boundaries. No native getter, object property,
    // pointer, IEnumerator, or return handle is inspected here. Metadata matching
    // does not establish native patch ABI or the game's original control flow.
    internal sealed class MapSelectionHooks : IDisposable
    {
        private const string Owner = Plugin.Id + ".map-selection";
        public const int MaxProcessEvents = 1024;
        private static MapSelectionHooks _active;
        private static long _processAccepted;
        private static long _processDropped;
        private static int _processCallbackErrors;
        private static int _processFailed;
        private readonly object _gate = new object();
        private List<(MethodInfo Original, MethodInfo Prefix, MethodInfo Postfix)> _targets;
        private Action<MapSelectionCallback> _copy;
        private Harmony _harmony;
        private bool _accepting;
        private bool _cleanupVerified = true;

        public bool Installed => _harmony != null && !Failed;
        // Exhausting the observation quota is a separate normal state; it does
        // not make installed patches unhealthy or cause automatic reinstalling.
        public bool Healthy => Installed && CallbackErrors == 0;
        public bool Failed => Volatile.Read(ref _processFailed) != 0;
        public int CallbackErrors => Volatile.Read(ref _processCallbackErrors);
        public long ProcessAccepted => Interlocked.Read(ref _processAccepted);
        public bool ProcessLimitReached => ProcessAccepted >= MaxProcessEvents;
        public long Dropped => Interlocked.Read(ref _processDropped);
        public bool CleanupVerified => _cleanupVerified;

        // Lifecycle operations belong on the consumer's Unity thread. The copy
        // delegate must verify that same thread before reading native fields and
        // synchronously freeze a bounded CLR snapshot; no wrapper is queued here.
        public void Enable(Action<MapSelectionCallback> copy)
        {
            if (copy == null) throw new ArgumentNullException(nameof(copy));
            if (Failed || CallbackErrors != 0)
                throw new InvalidOperationException("Map selection observation previously failed; restart before retrying.");
            if (Installed) return;
            if (ProcessLimitReached)
                throw new InvalidOperationException("Map selection observation process quota is exhausted; restart before installing again.");
            MapSelectionHooks previous = Interlocked.CompareExchange(ref _active, this, null);
            if (previous != null && !ReferenceEquals(previous, this))
                throw new InvalidOperationException("Another map selection observer is already active.");
            try
            {
                _targets = CreateTargets();
                _harmony = new Harmony(Owner); _cleanupVerified = false;
                foreach (var target in _targets)
                {
                    _harmony.Patch(target.Original,
                        prefix: target.Prefix == null ? null : new HarmonyMethod(target.Prefix),
                        postfix: target.Postfix == null ? null : new HarmonyMethod(target.Postfix));
                    var info = Harmony.GetPatchInfo(target.Original);
                    if (info == null || !info.Owners.Contains(Owner))
                        throw new InvalidOperationException("Map selection patch was not registered: " + target.Original.DeclaringType.FullName + "." + target.Original.Name);
                }
                lock (_gate) { _copy = copy; _accepting = true; }
            }
            catch (Exception installError)
            {
                LatchFailure(); StopAccepting();
                try { RemoveOwnPatches(); }
                catch (Exception cleanupError)
                {
                    throw new InvalidOperationException("Map selection installation failed and own patch removal was not verified.",
                        new AggregateException(installError, cleanupError));
                }
                throw;
            }
        }

        private static List<(MethodInfo Original, MethodInfo Prefix, MethodInfo Postfix)> CreateTargets()
        {
            var targets = new List<(MethodInfo Original, MethodInfo Prefix, MethodInfo Postfix)>();
            Add(targets, typeof(SceneContext), "cacheSelectedScenePath", false, typeof(void), Type.EmptyTypes, null, nameof(RouteCachedAfter));
            Add(targets, typeof(SceneContext), "LoadSceneMapCacheFromSave", false, typeof(void), Type.EmptyTypes, null, nameof(RouteRestoredAfter));
            Add(targets, typeof(IGPSetController), "GetRandomIGPSetInfo", false, typeof(IGPSetInfo), Type.EmptyTypes, null, nameof(IgpSelectedAfter));
            Add(targets, typeof(IGPSetInfo), "LoadPrefab", false, typeof(Il2CppSystem.Collections.IEnumerator), Type.EmptyTypes, nameof(IgpPrefabFactoryBefore), null);
            // Reflection-only return validation avoids adding a compile-time
            // dependency on ResourceManager merely to observe these arguments.
            Add(targets, typeof(SceneLoader), "LoadSceneAsync", true, null,
                new[] { typeof(string), typeof(LoadSceneMode), typeof(bool) }, nameof(SceneLoadCallBefore), null);
            return targets;
        }

        private static void Add(List<(MethodInfo Original, MethodInfo Prefix, MethodInfo Postfix)> targets,
            Type type, string name, bool isStatic, Type returnType, Type[] parameters, string prefixName, string postfixName)
        {
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly |
                (isStatic ? BindingFlags.Static : BindingFlags.Instance);
            MethodInfo original = type.GetMethod(name, flags, null, parameters, null);
            if (original == null || original.IsStatic != isStatic || original.IsGenericMethod || !ReturnTypeMatches(original.ReturnType, returnType))
                throw new InvalidOperationException("Expected map selection signature is unavailable: " + type.FullName + "." + name);
            MethodInfo prefix = CallbackMethod(prefixName), postfix = CallbackMethod(postfixName);
            targets.Add((original, prefix, postfix));
        }

        private static bool ReturnTypeMatches(Type actual, Type expected)
        {
            if (expected != null) return actual == expected;
            // Null expected is used only for SceneLoader.LoadSceneAsync. Validate
            // the complete generic shape; no original method/handle is accessed.
            if (!actual.IsGenericType || actual.ContainsGenericParameters ||
                actual.GetGenericTypeDefinition().FullName != "UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle`1") return false;
            Type[] arguments = actual.GetGenericArguments();
            return arguments.Length == 1 && arguments[0].FullName == "UnityEngine.ResourceManagement.ResourceProviders.SceneInstance";
        }

        private static MethodInfo CallbackMethod(string name)
        {
            if (name == null) return null;
            MethodInfo method = typeof(MapSelectionHooks).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null || method.ReturnType != typeof(void))
                throw new InvalidOperationException("Missing own read-only map selection callback: " + name);
            return method;
        }

        // All callbacks return void, take original inputs/result by value, and
        // have no ref/out return or argument. Original execution is never skipped.
        private static void RouteCachedAfter(SceneContext __instance)
            => Observe(MapSelectionStage.RouteCachedAfter, context: __instance);

        private static void RouteRestoredAfter(SceneContext __instance)
            => Observe(MapSelectionStage.RouteRestoredAfter, context: __instance);

        private static void IgpSelectedAfter(IGPSetController __instance, IGPSetInfo __result)
            => Observe(MapSelectionStage.IgpSelectedAfter, controller: __instance, selectedInfo: __result);

        // This is the IEnumerator factory boundary, not proof of an addressable
        // request, execution of MoveNext, or completion of prefab loading.
        private static void IgpPrefabFactoryBefore(IGPSetInfo __instance)
            => Observe(MapSelectionStage.IgpPrefabFactoryBefore, loadingInfo: __instance);

        // The original AsyncOperationHandle return is checked in metadata only.
        // It is neither an injected callback parameter nor read or replaced.
        private static void SceneLoadCallBefore(string __0, LoadSceneMode __1, bool __2)
            => Observe(MapSelectionStage.SceneLoadCallBefore, sceneKey: __0, loadMode: __1, activateOnLoad: __2);

        private static void Observe(MapSelectionStage stage, SceneContext context = null,
            IGPSetController controller = null, IGPSetInfo selectedInfo = null, IGPSetInfo loadingInfo = null,
            string sceneKey = null, LoadSceneMode loadMode = LoadSceneMode.Single, bool activateOnLoad = false)
        {
            MapSelectionHooks active = Volatile.Read(ref _active);
            if (active == null) return;
            try
            {
                lock (active._gate)
                {
                    if (!active._accepting || active._copy == null || active.Failed) return;
                    if (!TrySequence(out long sequence)) { IncrementDropped(); return; }
                    active._copy(new MapSelectionCallback(sequence, Environment.CurrentManagedThreadId, stage,
                        context, controller, selectedInfo, loadingInfo, sceneKey, loadMode, activateOnLoad));
                }
            }
            catch
            {
                // Consumer failures must not cross an original game callback.
                // Do not attempt native unpatching from this callback; the
                // controller's later lifecycle operation removes only our owner.
                IncrementCallbackErrors(); LatchFailure(); active.StopAccepting();
            }
        }

        private static bool TrySequence(out long sequence)
        {
            while (true)
            {
                long accepted = Interlocked.Read(ref _processAccepted);
                if (accepted >= MaxProcessEvents) { sequence = 0; return false; }
                sequence = accepted + 1;
                if (Interlocked.CompareExchange(ref _processAccepted, sequence, accepted) == accepted) return true;
            }
        }

        private static void IncrementDropped()
        {
            while (true)
            {
                long count = Interlocked.Read(ref _processDropped);
                if (count == long.MaxValue || Interlocked.CompareExchange(ref _processDropped, count + 1, count) == count) return;
            }
        }

        private static void IncrementCallbackErrors()
        {
            while (true)
            {
                int count = Volatile.Read(ref _processCallbackErrors);
                if (count == int.MaxValue || Interlocked.CompareExchange(ref _processCallbackErrors, count + 1, count) == count) return;
            }
        }

        private static void LatchFailure() => Volatile.Write(ref _processFailed, 1);

        public void CheckHealthy()
        {
            if (CallbackErrors != 0) { LatchFailure(); StopAccepting(); }
            if (!Healthy) throw new InvalidOperationException("Map selection observer is unavailable or failed; restart after a failure.");
        }

        private void StopAccepting()
        {
            lock (_gate) { _accepting = false; _copy = null; }
            Interlocked.CompareExchange(ref _active, null, this);
        }

        private void RemoveOwnPatches()
        {
            _cleanupVerified = false;
            Harmony harmony = _harmony;
            if (harmony != null) harmony.UnpatchSelf();
            if (_targets != null)
            {
                foreach (var target in _targets)
                {
                    var info = Harmony.GetPatchInfo(target.Original);
                    if (info != null && info.Owners.Contains(Owner))
                        throw new InvalidOperationException("Own map selection hook is still registered: " + target.Original.DeclaringType.FullName + "." + target.Original.Name);
                }
            }
            _harmony = null; _targets = null; _cleanupVerified = true;
        }

        public void Dispose()
        {
            StopAccepting();
            if (_harmony == null && _targets == null) return;
            try { RemoveOwnPatches(); }
            catch
            {
                LatchFailure(); _cleanupVerified = false;
                throw;
            }
        }
    }
}
