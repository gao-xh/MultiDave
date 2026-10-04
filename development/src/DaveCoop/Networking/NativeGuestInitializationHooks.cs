using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using DR.Save;
using HarmonyLib;

namespace DaveCoop.Networking
{
    internal sealed class NativeGuestInitializationHooks
    {
        private const string Owner = Plugin.Id + ".guest-initialization";
        private static NativeGuestInitializationHooks _active;
        private readonly NativeGuestInitializationController _source;
        private readonly Dictionary<MethodBase, Kind> _targets = new Dictionary<MethodBase, Kind>();
        private Harmony _harmony;
        private int _calls;
        private enum Kind { Awake, LoadSaved, LoadAll, Factory, Move }
        private sealed class Call
        {
            public Kind Kind;
            public bool RanOriginal, Returned, Finalized;
        }

        public NativeGuestInitializationHooks(NativeGuestInitializationController source) { _source = source; }

        public void Install()
        {
            if (Interlocked.CompareExchange(ref _active, this, null) != null)
                throw new InvalidOperationException("Another guest initialization source owns this process.");
            Add(typeof(GameBase._InitAfterSaveSystem_d__45), "MoveNext", typeof(bool), Kind.Move);
            Add(typeof(GameBase), "Awake_Impl", typeof(void), Kind.Awake);
            Add(typeof(GameBase), "LoadSavedData", typeof(void), Kind.LoadSaved);
            Add(typeof(SaveSystem), "LoadAllData", typeof(void), Kind.LoadAll);
            Add(typeof(GameBase), "InitAfterSaveSystem", typeof(Il2CppSystem.Collections.IEnumerator), Kind.Factory);
            _harmony = new Harmony(Owner);
            foreach (var target in _targets)
            {
                string before = target.Value == Kind.Move ? nameof(BeforeMove) : nameof(Before);
                string after = target.Value == Kind.Move ? nameof(AfterMove) : target.Value == Kind.Factory ? nameof(AfterFactory) : nameof(After);
                _harmony.Patch(target.Key,
                    prefix: new HarmonyMethod(Callback(before)) { priority = Priority.First },
                    postfix: new HarmonyMethod(Callback(after)) { priority = Priority.Last },
                    finalizer: new HarmonyMethod(Callback(nameof(FinalizeCall))) { priority = Priority.Last });
            }
            if (!_targets.Keys.All(method => Harmony.GetPatchInfo(method)?.Owners.Contains(Owner) == true))
                throw new InvalidOperationException("Natural initialization source patches are incomplete.");
            // No automatic Dispose/unpatch: a failed or disconnected guest may
            // still have live native consumers using its temporary roots.
        }

        private void Add(Type type, string name, Type result, Kind kind)
        {
            MethodInfo method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null, Type.EmptyTypes, null);
            if (method == null || method.IsStatic || method.IsGenericMethod || method.ReturnType != result || method.DeclaringType != type)
                throw new InvalidOperationException("Unsupported initialization declaration: " + type.Name + "." + name);
            _targets.Add(method, kind);
        }
        private static MethodInfo Callback(string name) => typeof(NativeGuestInitializationHooks).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
        private Call Begin(Kind kind)
        {
            if (!_source.AcceptCallbackThread()) return null;
            if (kind != Kind.Move && Interlocked.Increment(ref _calls) > 64)
            { _source.Fail("Natural initialization callback limit."); return null; }
            return new Call { Kind = kind, RanOriginal = true };
        }
        private static void Before(object __instance, MethodBase __originalMethod, out Call __state)
        {
            __state = null;
            NativeGuestInitializationHooks active = Volatile.Read(ref _active);
            if (active == null) return;
            try
            {
                if (!active._targets.TryGetValue(__originalMethod, out Kind kind)) throw new InvalidOperationException("Unknown startup target.");
                __state = active.Begin(kind); if (__state == null) return;
                if (kind == Kind.Awake) active._source.BeforeAwake((GameBase)__instance);
                else if (kind == Kind.LoadSaved) active._source.BeforeLoadSaved((GameBase)__instance);
                else if (kind == Kind.LoadAll) active._source.BeforeLoadAll((SaveSystem)__instance);
                else if (kind == Kind.Factory) active._source.BeforeFactory((GameBase)__instance);
            }
            catch (Exception error) { active._source.Fail("Startup prefix failed: " + error.GetType().Name); }
        }
        private static bool BeforeMove(GameBase._InitAfterSaveSystem_d__45 __instance, ref bool __result, out Call __state)
        {
            NativeGuestInitializationHooks active = Volatile.Read(ref _active);
            __state = null;
            if (active == null) return true;
            try
            {
                __state = active.Begin(Kind.Move);
                if (__state == null) { __result = true; return false; }
                __state.RanOriginal = false;
                bool allow = active._source.BeforeMove(__instance, ref __result);
                __state.RanOriginal = allow;
                return allow;
            }
            catch (Exception error)
            {
                active._source.Fail("Startup MoveNext prefix failed: " + error.GetType().Name);
                __result = true;
                return false;
            }
        }
        private static void After(bool __runOriginal, Call __state)
        {
            if (__state == null || __state.Returned) return;
            var source = Volatile.Read(ref _active)?._source;
            if (source == null || !source.AcceptCallbackThread()) return;
            __state.Returned = true;
            if (!__runOriginal) { source?.Fail("An original startup method was skipped by another patch."); return; }
            if (__state.Kind == Kind.Awake) source?.AfterAwake();
            else if (__state.Kind == Kind.LoadSaved) source?.AfterLoadSaved();
            else if (__state.Kind == Kind.LoadAll) source?.AfterLoadAll();
        }
        private static void AfterFactory(Il2CppSystem.Collections.IEnumerator __result, bool __runOriginal, Call __state)
        {
            if (__state == null || __state.Returned) return;
            var source = Volatile.Read(ref _active)?._source;
            if (source == null || !source.AcceptCallbackThread()) return;
            __state.Returned = true;
            if (!__runOriginal) { source?.Fail("The original initialization factory was skipped."); return; }
            try { source?.AfterFactory(__result); }
            catch (Exception error) { source?.Fail("Startup factory read failed: " + error.GetType().Name); }
        }
        private static void AfterMove(bool __result, bool __runOriginal, Call __state)
        {
            if (__state == null || __state.Returned) return;
            var source = Volatile.Read(ref _active)?._source;
            if (source == null || !source.AcceptCallbackThread()) return;
            __state.Returned = true;
            try
            {
                if (__state.RanOriginal && !__runOriginal) { source?.Fail("The approved original initializer was skipped by another patch."); return; }
                source?.AfterMove(__state.RanOriginal && __runOriginal, __result);
            }
            catch (Exception error) { source?.Fail("Startup MoveNext read failed: " + error.GetType().Name); }
        }
        private static Exception FinalizeCall(Exception __exception, Call __state)
        {
            if (__state == null || __state.Finalized) return __exception;
            var source = Volatile.Read(ref _active)?._source;
            if (source == null || !source.AcceptCallbackThread()) return __exception;
            __state.Finalized = true;
            source.OriginalException(__exception);
            return __exception;
        }
    }
}
