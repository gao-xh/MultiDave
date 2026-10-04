using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using DaveCoop.Core.World;
using DR.AI;
using HarmonyLib;
using UnityEngine;

namespace DaveCoop.Networking
{
    // Observes call boundaries and the original damage return only. The optional
    // resolver must query an immutable/thread-safe CLR identity snapshot without
    // Unity/native access. HP and native ownership are inspected by the consumer.
    internal sealed class FishInteractionHooks : IDisposable
    {
        private sealed class CallContext
        {
            public FishInteractionObservedKind Kind;
            public FishInteractionMethodCode Code;
            public long FishPointer;
            public long ProjectilePointer;
            public HostEntityTarget? OriginalBinding;
        }

        private const string Owner = Plugin.Id + ".fish-interaction";
        public const int MaxQueuedEvents = 4096;
        public const int MaxPendingCalls = 1024;
        public const int MaxProcessEvents = 8192;
        private static FishInteractionHooks _active;
        private static long _processAccepted;
        private static long _nextCallId;
        private readonly object _gate = new object();
        private readonly Queue<FishInteractionEvent> _events = new Queue<FishInteractionEvent>(MaxQueuedEvents);
        private readonly Dictionary<long, CallContext> _calls = new Dictionary<long, CallContext>();
        private List<(MethodInfo Original, MethodInfo Prefix, MethodInfo Postfix)> _targets;
        private Func<long, HostEntityTarget?> _resolver;
        private Harmony _harmony;
        private bool _accepting;
        private bool _failed;
        private int _expectedThreadId;
        private long _accepted;
        private long _dropped;
        private long _discarded;
        private long _discardedCalls;
        private long _unresolvedCalls;
        private int _callbackErrors;
        private int _resolverErrors;
        private int _unexpectedThreads;
        private int _unmatchedAfter;
        private bool _cleanupVerified;

        public bool Installed => _harmony != null && !_failed;
        public bool Healthy => Installed && CallbackErrors == 0;
        public int CallbackErrors => Volatile.Read(ref _callbackErrors);
        public int ResolverErrors => Volatile.Read(ref _resolverErrors);
        public int UnexpectedThreadCallbacks => Volatile.Read(ref _unexpectedThreads);
        public int UnmatchedAfterCallbacks => Volatile.Read(ref _unmatchedAfter);
        public long ProcessAccepted => Interlocked.Read(ref _processAccepted);
        public bool ProcessLimitReached => ProcessAccepted >= MaxProcessEvents;
        public long Accepted { get { lock (_gate) return _accepted; } }
        public long Dropped { get { lock (_gate) return _dropped; } }
        public long Discarded { get { lock (_gate) return _discarded; } }
        public long DiscardedCalls { get { lock (_gate) return _discardedCalls; } }
        public long UnresolvedCalls { get { lock (_gate) return _unresolvedCalls; } }
        public int Queued { get { lock (_gate) return _events.Count; } }
        public int PendingCalls { get { lock (_gate) return _calls.Count; } }
        public bool CleanupVerified => _cleanupVerified;

        // Lifecycle operations run on the controller's Unity thread. No resolver
        // invoking Unity APIs may be supplied here. Process quotas survive toggles.
        public void Enable(Func<long, HostEntityTarget?> resolver = null)
        {
            // Errors survive Dispose and scene/toggle changes. Refuse a new
            // installation even if the controller disposed before CheckHealthy.
            if (CallbackErrors != 0)
            {
                _failed = true;
                StopAccepting();
            }
            if (_failed) throw new InvalidOperationException("Fish interaction hooks previously failed; restart before retrying.");
            if (Installed) return;
            FishInteractionHooks previous = Interlocked.CompareExchange(ref _active, this, null);
            if (previous != null && !ReferenceEquals(previous, this))
                throw new InvalidOperationException("Another fish interaction observer is already active.");
            try
            {
                _expectedThreadId = Environment.CurrentManagedThreadId;
                Volatile.Write(ref _resolver, resolver);
                _targets = CreateTargets();
                _harmony = new Harmony(Owner); _cleanupVerified = false;
                foreach (var target in _targets)
                {
                    _harmony.Patch(target.Original, prefix: new HarmonyMethod(target.Prefix), postfix: new HarmonyMethod(target.Postfix));
                    var info = Harmony.GetPatchInfo(target.Original);
                    if (info == null || !info.Owners.Contains(Owner))
                        throw new InvalidOperationException("Fish interaction patch was not registered: " + target.Original.DeclaringType.FullName + "." + target.Original.Name);
                }
                lock (_gate) _accepting = true;
                NetworkDriver.Logger.LogInfo("DAVECOOP_FISH_INTERACTION_READY: 8 read-only call observers installed; original damage results and CLR target bindings are observed, never replaced.");
            }
            catch (Exception installError)
            {
                StopAccepting(); _failed = true;
                try { RemoveOwnPatches(); }
                catch (Exception cleanupError)
                {
                    throw new InvalidOperationException("Fish interaction installation failed and own patch removal was not verified.",
                        new AggregateException(installError, cleanupError));
                }
                throw;
            }
        }

        private static List<(MethodInfo Original, MethodInfo Prefix, MethodInfo Postfix)> CreateTargets()
        {
            var result = new List<(MethodInfo Original, MethodInfo Prefix, MethodInfo Postfix)>();
            Add(result, "DR.AI.FishAISystem", "OnTakeDamage", typeof(bool), new[] { typeof(AttackData), typeof(DefenseData) }, nameof(FishDamageBefore), nameof(FishDamageAfter), true);
            Add(result, "DR.AI.SABaseFishSystem", "OnTakeDamage", typeof(bool), new[] { typeof(AttackData), typeof(DefenseData) }, nameof(SpecialDamageBefore), nameof(SpecialDamageAfter), true);
            Add(result, "DR.AI.FishAISystem", "HookedByProjectile", typeof(void), new[] { typeof(ProjectileInfo) }, nameof(FishHookBefore), nameof(FishHookAfter), true);
            Add(result, "DR.AI.FishAISystem", "WinFromProjectileinFight", typeof(void), Type.EmptyTypes, nameof(FishWinBefore), nameof(FishWinAfter), true);
            Add(result, "SAMahoniCommon", "WinFromProjectileinFight", typeof(void), Type.EmptyTypes, nameof(MahoniCommonWinBefore), nameof(MahoniCommonWinAfter), true);
            Add(result, "SAMahoniGeneral", "WinFromProjectileinFight", typeof(void), Type.EmptyTypes, nameof(MahoniGeneralWinBefore), nameof(MahoniGeneralWinAfter), true);
            Add(result, "DR.AI.FishAISystem", "SuccessPickupFish", typeof(void), new[] { typeof(int), typeof(bool) }, nameof(FishPickupBefore), nameof(FishPickupAfter), true);
            Add(result, "HarpoonProjectile", "Fire", typeof(void), new[] { typeof(Vector3) }, nameof(HarpoonFireBefore), nameof(HarpoonFireAfter), false);
            return result;
        }

        private static void Add(List<(MethodInfo Original, MethodInfo Prefix, MethodInfo Postfix)> targets,
            string typeName, string methodName, Type returnType, Type[] parameters, string prefixName, string postfixName, bool fish)
        {
            Type type = typeof(FishAISystem).Assembly.GetType(typeName, true);
            MethodInfo original = type.GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null, parameters, null);
            if (original == null || original.ReturnType != returnType ||
                (fish ? !typeof(FishAISystem).IsAssignableFrom(type) : type != typeof(HarpoonProjectile)))
                throw new InvalidOperationException("Expected fish interaction signature is unavailable: " + typeName + "." + methodName);
            MethodInfo prefix = typeof(FishInteractionHooks).GetMethod(prefixName, BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo postfix = typeof(FishInteractionHooks).GetMethod(postfixName, BindingFlags.Static | BindingFlags.NonPublic);
            if (prefix == null || postfix == null) throw new InvalidOperationException("Missing own fish interaction observer callback.");
            targets.Add((original, prefix, postfix));
        }

        // No native arguments or Unity properties are read. __state is our own
        // CLR CallId; bool __result is observed by value, never passed by ref.
        private static void FishDamageBefore(FishAISystem __instance, out long __state) => __state = BeginFish(__instance, FishInteractionObservedKind.DamageCall, FishInteractionMethodCode.FishOnTakeDamage);
        private static void FishDamageAfter(FishAISystem __instance, long __state, bool __result) => EndFish(__instance, __state, FishInteractionMethodCode.FishOnTakeDamage, __result);
        private static void SpecialDamageBefore(FishAISystem __instance, out long __state) => __state = BeginFish(__instance, FishInteractionObservedKind.DamageCall, FishInteractionMethodCode.SpecialFishOnTakeDamage);
        private static void SpecialDamageAfter(FishAISystem __instance, long __state, bool __result) => EndFish(__instance, __state, FishInteractionMethodCode.SpecialFishOnTakeDamage, __result);
        private static void FishHookBefore(FishAISystem __instance, out long __state) => __state = BeginFish(__instance, FishInteractionObservedKind.HookCall, FishInteractionMethodCode.FishHookedByProjectile);
        private static void FishHookAfter(FishAISystem __instance, long __state) => EndFish(__instance, __state, FishInteractionMethodCode.FishHookedByProjectile, null);
        private static void FishWinBefore(FishAISystem __instance, out long __state) => __state = BeginFish(__instance, FishInteractionObservedKind.FightWinCall, FishInteractionMethodCode.FishWinFromProjectileFight);
        private static void FishWinAfter(FishAISystem __instance, long __state) => EndFish(__instance, __state, FishInteractionMethodCode.FishWinFromProjectileFight, null);
        private static void MahoniCommonWinBefore(FishAISystem __instance, out long __state) => __state = BeginFish(__instance, FishInteractionObservedKind.FightWinCall, FishInteractionMethodCode.MahoniCommonWinFromProjectileFight);
        private static void MahoniCommonWinAfter(FishAISystem __instance, long __state) => EndFish(__instance, __state, FishInteractionMethodCode.MahoniCommonWinFromProjectileFight, null);
        private static void MahoniGeneralWinBefore(FishAISystem __instance, out long __state) => __state = BeginFish(__instance, FishInteractionObservedKind.FightWinCall, FishInteractionMethodCode.MahoniGeneralWinFromProjectileFight);
        private static void MahoniGeneralWinAfter(FishAISystem __instance, long __state) => EndFish(__instance, __state, FishInteractionMethodCode.MahoniGeneralWinFromProjectileFight, null);
        private static void FishPickupBefore(FishAISystem __instance, out long __state) => __state = BeginFish(__instance, FishInteractionObservedKind.PickupCall, FishInteractionMethodCode.FishSuccessPickup);
        private static void FishPickupAfter(FishAISystem __instance, long __state) => EndFish(__instance, __state, FishInteractionMethodCode.FishSuccessPickup, null);
        private static void HarpoonFireBefore(HarpoonProjectile __instance, out long __state) => __state = BeginProjectile(__instance);
        private static void HarpoonFireAfter(HarpoonProjectile __instance, long __state) => EndProjectile(__instance, __state);

        private static long BeginFish(FishAISystem instance, FishInteractionObservedKind kind, FishInteractionMethodCode code)
        {
            FishInteractionHooks active = Volatile.Read(ref _active);
            if (active == null || ReferenceEquals(instance, null)) return 0;
            try { return active.Begin(kind, code, instance.Pointer.ToInt64(), 0, Environment.CurrentManagedThreadId); }
            catch { active.CallbackFailed(); return 0; }
        }

        private static void EndFish(FishAISystem instance, long callId, FishInteractionMethodCode code, bool? damageReturn)
        {
            FishInteractionHooks active = Volatile.Read(ref _active);
            if (active == null || callId == 0 || ReferenceEquals(instance, null)) return;
            try { active.End(callId, code, instance.Pointer.ToInt64(), 0, Environment.CurrentManagedThreadId, damageReturn); }
            catch { active.CallbackFailed(); }
        }

        private static long BeginProjectile(HarpoonProjectile instance)
        {
            FishInteractionHooks active = Volatile.Read(ref _active);
            if (active == null || ReferenceEquals(instance, null)) return 0;
            try { return active.Begin(FishInteractionObservedKind.ProjectileFireCall, FishInteractionMethodCode.HarpoonFire,
                0, instance.Pointer.ToInt64(), Environment.CurrentManagedThreadId); }
            catch { active.CallbackFailed(); return 0; }
        }

        private static void EndProjectile(HarpoonProjectile instance, long callId)
        {
            FishInteractionHooks active = Volatile.Read(ref _active);
            if (active == null || callId == 0 || ReferenceEquals(instance, null)) return;
            try { active.End(callId, FishInteractionMethodCode.HarpoonFire, 0, instance.Pointer.ToInt64(), Environment.CurrentManagedThreadId, null); }
            catch { active.CallbackFailed(); }
        }

        private long Begin(FishInteractionObservedKind kind, FishInteractionMethodCode code, long fishPointer, long projectilePointer, int threadId)
        {
            if ((fishPointer == 0 && projectilePointer == 0) || threadId < 1)
                throw new InvalidOperationException("Missing native observation correlation.");
            lock (_gate)
            {
                if (!_accepting) return 0;
                if (_events.Count >= MaxQueuedEvents || _calls.Count >= MaxPendingCalls || ProcessLimitReached)
                { Increment(ref _dropped); return 0; }
            }

            HostEntityTarget? binding = null;
            if (threadId != _expectedThreadId)
            {
                IncrementAtomic(ref _unexpectedThreads); IncrementAtomic(ref _callbackErrors);
            }
            else if (fishPointer != 0)
            {
                // Resolve at the prefix, not later against a potentially reused
                // pointer. The same immutable binding is preserved in the postfix.
                Func<long, HostEntityTarget?> resolver = Volatile.Read(ref _resolver);
                try
                {
                    binding = resolver?.Invoke(fishPointer);
                    if (binding.HasValue)
                    {
                        HostEntityTarget target = binding.Value;
                        if (target.Kind != EntityKind.Fish || target.SceneEpoch < 1 || target.EntityId < 1 ||
                            target.DataTid < 1 || target.Generation < 1 || target.LocalToken == 0)
                            throw new InvalidOperationException("Invalid CLR fish binding snapshot.");
                    }
                }
                catch
                {
                    binding = null; IncrementAtomic(ref _resolverErrors); IncrementAtomic(ref _callbackErrors);
                }
            }
            lock (_gate)
            {
                if (!_accepting) return 0;
                if (_events.Count >= MaxQueuedEvents || _calls.Count >= MaxPendingCalls || !TrySequence(out long sequence))
                { Increment(ref _dropped); return 0; }
                long callId = Interlocked.Increment(ref _nextCallId);
                var context = new CallContext { Kind = kind, Code = code, FishPointer = fishPointer,
                    ProjectilePointer = projectilePointer, OriginalBinding = binding };
                _calls.Add(callId, context);
                _events.Enqueue(new FishInteractionEvent(sequence, callId, kind, code, FishInteractionCallStage.Before,
                    fishPointer, projectilePointer, threadId, binding, null, true));
                Increment(ref _accepted);
                if (fishPointer != 0 && !binding.HasValue) Increment(ref _unresolvedCalls);
                return callId;
            }
        }

        private void End(long callId, FishInteractionMethodCode code, long fishPointer, long projectilePointer, int threadId, bool? damageReturn)
        {
            lock (_gate)
            {
                // Contexts are removed even if the queue or process quota is full,
                // so lost postfix events cannot retain native correlation records.
                if (!_calls.TryGetValue(callId, out CallContext context))
                { IncrementAtomic(ref _unmatchedAfter); return; }
                _calls.Remove(callId);
                if (!_accepting) return;
                bool matched = context.Code == code && context.FishPointer == fishPointer && context.ProjectilePointer == projectilePointer;
                if (!matched) IncrementAtomic(ref _callbackErrors);
                if (threadId != _expectedThreadId)
                { IncrementAtomic(ref _unexpectedThreads); IncrementAtomic(ref _callbackErrors); }
                if (_events.Count >= MaxQueuedEvents || !TrySequence(out long sequence))
                { Increment(ref _dropped); return; }
                _events.Enqueue(new FishInteractionEvent(sequence, callId, context.Kind, code, FishInteractionCallStage.After,
                    fishPointer, projectilePointer, threadId, matched ? context.OriginalBinding : null, damageReturn, matched));
                Increment(ref _accepted);
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

        private void CallbackFailed()
        {
            IncrementAtomic(ref _callbackErrors);
            Volatile.Write(ref _accepting, false);
        }

        public bool TryTake(out FishInteractionEvent observed)
        {
            lock (_gate)
            {
                if (_events.Count == 0) { observed = null; return false; }
                observed = _events.Dequeue(); return true;
            }
        }

        // Called before a new scene/session correlation begins. Never reset the
        // process quota/counters or allow an old call to acquire a new fish ID.
        public void ClearPending()
        {
            lock (_gate)
            {
                AddSaturating(ref _discarded, _events.Count); AddSaturating(ref _discardedCalls, _calls.Count);
                _events.Clear(); _calls.Clear();
            }
        }

        public void CheckHealthy()
        {
            if (CallbackErrors != 0)
            {
                _failed = true;
                StopAccepting();
            }
            if (!Healthy) throw new InvalidOperationException("Fish interaction observer is unavailable or a callback failed.");
        }

        private void StopAccepting()
        {
            Interlocked.CompareExchange(ref _active, null, this);
            Volatile.Write(ref _accepting, false); Volatile.Write(ref _resolver, null);
            ClearPending();
        }

        private void RemoveOwnPatches()
        {
            Harmony harmony = _harmony;
            if (harmony != null) harmony.UnpatchSelf();
            if (_targets != null)
            {
                foreach (var target in _targets)
                {
                    var info = Harmony.GetPatchInfo(target.Original);
                    if (info != null && info.Owners.Contains(Owner))
                        throw new InvalidOperationException("Own fish interaction hook is still registered: " + target.Original.DeclaringType.FullName + "." + target.Original.Name);
                }
            }
            _harmony = null; _targets = null; _cleanupVerified = true;
        }

        public void Dispose()
        {
            StopAccepting();
            if (_harmony == null && _targets == null) return;
            try
            {
                RemoveOwnPatches();
                NetworkDriver.Logger.LogInfo("DAVECOOP_FISH_INTERACTION_STOPPED: own registrations removed; pending local observations/call contexts discarded.");
            }
            catch (Exception error)
            {
                _failed = true; _cleanupVerified = false;
                NetworkDriver.Logger.LogWarning("DAVECOOP_FISH_INTERACTION_WARNING: own hook removal was not verified: " + error.Message);
            }
        }

        private static void Increment(ref long value) { if (value < long.MaxValue) value++; }
        private static void AddSaturating(ref long value, int count) { value = value <= long.MaxValue - count ? value + count : long.MaxValue; }
        private static void IncrementAtomic(ref int value)
        {
            while (true)
            {
                int current = Volatile.Read(ref value);
                if (current == int.MaxValue || Interlocked.CompareExchange(ref value, current + 1, current) == current) return;
            }
        }
    }
}
