using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using DaveCoop.Core.World;
using DR.AI;
using HarmonyLib;

namespace DaveCoop.Networking
{
    internal sealed class FishLifecycleHooks : IDisposable
    {
        private const string Owner = Plugin.Id + ".fish-lifecycle";
        private static FishLifecycleTracker _active;
        private static int _callbackErrors;
        private Harmony _harmony;
        private List<(MethodInfo Original, MethodInfo Prefix)> _targets;
        private bool _failed;
        public readonly FishLifecycleTracker Tracker = new FishLifecycleTracker();
        public bool Installed => _harmony != null && !_failed;
        public bool Healthy => Installed && Volatile.Read(ref _callbackErrors) == 0;
        internal bool OwnsActiveTracker => Healthy && ReferenceEquals(Volatile.Read(ref _active), Tracker);
        public int CallbackErrors => Volatile.Read(ref _callbackErrors);

        public void Enable()
        {
            if (Installed) return;
            if (_failed) throw new InvalidOperationException("Fish lifecycle hooks previously failed; restart before retrying.");
            var targets = new List<(MethodInfo Original, MethodInfo Prefix)>();
            Add(targets, "DR.AI.FishAISystem", "OnEnable", nameof(Enabled));
            Add(targets, "DR.AI.FishAISystem", "OnDisable", nameof(Disabled));
            Add(targets, "DR.AI.SABaseFishSystem", "OnDisable", nameof(Disabled));
            foreach (string name in new[] { "DR.AI.FishAISystem", "DR.AI.SABaseFishSystem", "SAMahoniCommon", "SAMahoniGeneral", "SAXiphactinus", "SASnappingTurtle" })
                Add(targets, name, "OnDestroy", nameof(Destroyed));
            var harmony = new Harmony(Owner);
            try
            {
                Volatile.Write(ref _callbackErrors, 0); Volatile.Write(ref _active, Tracker);
                foreach (var target in targets)
                {
                    harmony.Patch(target.Original, prefix: new HarmonyMethod(target.Prefix));
                    var info = Harmony.GetPatchInfo(target.Original);
                    if (info == null || !info.Owners.Contains(Owner)) throw new InvalidOperationException("Fish lifecycle patch was not registered.");
                }
                _harmony = harmony; _targets = targets;
                NetworkDriver.Logger.LogInfo("DAVECOOP_FISH_LIFECYCLE_READY: read-only observation hooks installed; original methods still run.");
            }
            catch
            {
                Volatile.Write(ref _active, null); _failed = true;
                try { harmony.UnpatchSelf(); } catch { }
                throw;
            }
        }

        private static void Add(List<(MethodInfo Original, MethodInfo Prefix)> targets, string typeName, string method, string prefix)
        {
            Type type = typeof(FishAISystem).Assembly.GetType(typeName, true);
            MethodInfo original = type.GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
            if (original == null || original.ReturnType != typeof(void) || !typeof(FishAISystem).IsAssignableFrom(type))
                throw new InvalidOperationException("Expected fish lifecycle signature is unavailable: " + typeName + "." + method);
            targets.Add((original, typeof(FishLifecycleHooks).GetMethod(prefix, BindingFlags.Static | BindingFlags.NonPublic)));
        }

        // Prefixes only read the managed wrapper's pointer and record CLR state.
        // No Unity access, logging, cancellation or exception reaches the game.
        private static void Enabled(FishAISystem __instance) => Record(__instance, FishLifecycleSignal.Enable);
        private static void Disabled(FishAISystem __instance) => Record(__instance, FishLifecycleSignal.Disable);
        private static void Destroyed(FishAISystem __instance) => Record(__instance, FishLifecycleSignal.Destroy);
        private static void Record(FishAISystem instance, FishLifecycleSignal signal)
        {
            FishLifecycleTracker active = Volatile.Read(ref _active);
            if (active == null || ReferenceEquals(instance, null)) return;
            try { active.Signal(instance.Pointer.ToInt64(), signal); }
            catch { Interlocked.CompareExchange(ref _callbackErrors, 1, 0); }
        }

        public void ClearObserved() => Tracker.Clear();
        public void Dispose()
        {
            Volatile.Write(ref _active, null); Tracker.Clear();
            Harmony harmony = _harmony; _harmony = null;
            if (harmony == null) return;
            try
            {
                harmony.UnpatchSelf();
                foreach (var target in _targets)
                {
                    var info = Harmony.GetPatchInfo(target.Original);
                    if (info != null && info.Owners.Contains(Owner)) throw new InvalidOperationException("Own fish hook is still registered.");
                }
                NetworkDriver.Logger.LogInfo("DAVECOOP_FISH_LIFECYCLE_STOPPED: own patch registrations removed; observer disabled.");
                _targets = null;
            }
            catch (Exception error)
            {
                _failed = true;
                NetworkDriver.Logger.LogWarning("DAVECOOP_FISH_LIFECYCLE_WARNING: own hooks could not be removed: " + error.Message);
            }
        }
    }
}
