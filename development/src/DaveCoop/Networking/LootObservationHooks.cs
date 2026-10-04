using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using DR.AI;
using HarmonyLib;

namespace DaveCoop.Networking
{
    internal enum LootObservationMethod
    {
        FishAddDropItem = 1,
        LootBoxAdd = 2,
        CaughtFishAdd = 3,
        IngredientsAddFromLootBox = 4
    }

    internal enum LootObservationStage { Before = 1, After = 2 }

    // Pure CLR arguments copied at the prefix. Reference arguments are only
    // represented by presence; no list, delegate or native wrapper is retained.
    internal sealed class LootObservationArguments
    {
        public int? ItemId { get; internal set; }
        public int? Count { get; internal set; }
        public int? BonusGrade { get; internal set; }
        public int? Tier { get; internal set; }
        public int? CollectionId { get; internal set; }
        public int? CollectionGrade { get; internal set; }
        public int? LiftType { get; internal set; }
        public bool? IgnoreOverloaded { get; internal set; }
        public bool? UpdateMissionCount { get; internal set; }
        public bool? IsForce { get; internal set; }
        public bool GetTimesArgumentPresent { get; internal set; }
        public bool ExchangeCallbackArgumentPresent { get; internal set; }
    }

    // Synchronous view only. A consumer must confirm Unity thread ownership and
    // freeze bounded CLR values immediately. Never enqueue this view/wrappers.
    internal sealed class LootObservationCallback
    {
        public long ProcessSequence { get; }
        public long CallId { get; }
        public int ManagedThreadId { get; }
        public LootObservationMethod Method { get; }
        public LootObservationStage Stage { get; }
        public LootObservationArguments Arguments { get; }
        public FishAISystem Fish { get; }
        public LootBox Bag { get; }
        public IngredientsStorage Storage { get; }
        public LootBoxSlot Slot { get; }
        public bool? OriginalReturn { get; }
        public bool PrefixContextMatched { get; }

        internal LootObservationCallback(long sequence, long callId, int threadId,
            LootObservationMethod method, LootObservationStage stage, LootObservationArguments arguments,
            FishAISystem fish, LootBox bag, IngredientsStorage storage, LootBoxSlot slot,
            bool? originalReturn, bool contextMatched)
        {
            ProcessSequence = sequence; CallId = callId; ManagedThreadId = threadId;
            Method = method; Stage = stage; Arguments = arguments;
            Fish = fish; Bag = bag; Storage = storage; Slot = slot;
            OriginalReturn = originalReturn; PrefixContextMatched = contextMatched;
        }
    }

    // Metadata/signature validation is not native ABI acceptance. These hooks
    // observe original calls only: no argument/result replacement, skipping,
    // random selection, inventory write, constructor or original getter calls.
    internal sealed class LootObservationHooks : IDisposable
    {
        private const string Owner = Plugin.Id + ".loot-observation";
        public const int MaxProcessEvents = 1024;
        public const int MaxPendingCalls = 128;
        private static LootObservationHooks _active;
        private static long _processAccepted;
        private static long _processNextCallId;
        private static long _processDropped;
        private static long _processUnmatchedAfter;
        private static long _processDiscardedCalls;
        private static int _processCallbackErrors;
        private static int _processFailed;
        private readonly object _gate = new object();
        private readonly Dictionary<long, CallContext> _calls = new Dictionary<long, CallContext>();
        private List<(MethodInfo Original, MethodInfo Prefix, MethodInfo Postfix)> _targets;
        private Action<LootObservationCallback> _copy;
        private Harmony _harmony;
        private bool _accepting;
        private bool _cleanupVerified = true;

        private sealed class CallContext
        {
            public LootObservationMethod Method;
            public int ThreadId;
            public LootObservationArguments Arguments;
        }

        public bool Installed => _harmony != null && !Failed;
        public bool Healthy => Installed && CallbackErrors == 0;
        public bool Failed => Volatile.Read(ref _processFailed) != 0;
        public int CallbackErrors => Volatile.Read(ref _processCallbackErrors);
        public long ProcessAccepted => Interlocked.Read(ref _processAccepted);
        public bool ProcessLimitReached => ProcessAccepted >= MaxProcessEvents;
        public long Dropped => Interlocked.Read(ref _processDropped);
        public long UnmatchedAfter => Interlocked.Read(ref _processUnmatchedAfter);
        public long DiscardedCalls => Interlocked.Read(ref _processDiscardedCalls);
        public bool CleanupVerified => _cleanupVerified;
        public int PendingCalls { get { lock (_gate) return _calls.Count; } }

        // Lifecycle belongs on the confirmed Unity thread. Failures and quotas
        // are process-wide, never reset by toggling or a new capture instance.
        public void Enable(Action<LootObservationCallback> copy)
        {
            if (copy == null) throw new ArgumentNullException(nameof(copy));
            if (Failed || CallbackErrors != 0)
                throw new InvalidOperationException("Loot observation previously failed; restart before retrying.");
            if (Installed) return;
            if (ProcessLimitReached)
                throw new InvalidOperationException("Loot observation process quota is exhausted; restart before installing again.");
            LootObservationHooks previous = Interlocked.CompareExchange(ref _active, this, null);
            if (previous != null && !ReferenceEquals(previous, this))
                throw new InvalidOperationException("Another loot observer is already active.");
            try
            {
                _targets = CreateTargets();
                _harmony = new Harmony(Owner); _cleanupVerified = false;
                foreach (var target in _targets)
                {
                    _harmony.Patch(target.Original,
                        prefix: new HarmonyMethod(target.Prefix), postfix: new HarmonyMethod(target.Postfix));
                    var info = Harmony.GetPatchInfo(target.Original);
                    if (info == null || !info.Owners.Contains(Owner))
                        throw new InvalidOperationException("Loot observation patch was not registered: " + target.Original.DeclaringType.FullName + "." + target.Original.Name);
                }
                lock (_gate) { _copy = copy; _accepting = true; }
            }
            catch (Exception installError)
            {
                LatchFailure(); StopAccepting();
                try { RemoveOwnPatches(); }
                catch (Exception cleanupError)
                {
                    throw new InvalidOperationException("Loot observation installation failed and own patch removal was not verified.",
                        new AggregateException(installError, cleanupError));
                }
                throw;
            }
        }

        private static List<(MethodInfo Original, MethodInfo Prefix, MethodInfo Postfix)> CreateTargets()
        {
            var targets = new List<(MethodInfo Original, MethodInfo Prefix, MethodInfo Postfix)>();
            Add(targets, typeof(FishAISystem), "AddDropItem_Impl", false, typeof(void),
                new[] { typeof(int), typeof(LootBox.AutoLiftedType), typeof(int), typeof(bool) }, nameof(FishDropBefore), nameof(FishDropAfter));
            Add(targets, typeof(LootBox), "Add", false, typeof(bool),
                new[] { typeof(int), typeof(int), typeof(int), typeof(LootBox.AutoLiftedType), typeof(Il2CppSystem.Collections.Generic.List<string>), typeof(bool) },
                nameof(BagAddBefore), nameof(BagAddAfter));
            Add(targets, typeof(SaveDataCaughtFishRouter), "AddCaughtFish", true, typeof(void),
                new[] { typeof(int), typeof(int), typeof(bool) }, nameof(CollectionBefore), nameof(CollectionAfter));
            Add(targets, typeof(IngredientsStorage), "AddFromLootBox", false, typeof(void),
                new[] { typeof(LootBoxSlot), typeof(Il2CppSystem.Func<int>) }, nameof(StorageBefore), nameof(StorageAfter));
            return targets;
        }

        private static void Add(List<(MethodInfo Original, MethodInfo Prefix, MethodInfo Postfix)> targets,
            Type type, string name, bool isStatic, Type returnType, Type[] parameters, string prefixName, string postfixName)
        {
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly |
                (isStatic ? BindingFlags.Static : BindingFlags.Instance);
            MethodInfo original = type.GetMethod(name, flags, null, parameters, null);
            if (original == null || original.DeclaringType != type || original.IsStatic != isStatic ||
                original.IsGenericMethod || original.ReturnType != returnType)
                throw new InvalidOperationException("Expected loot observation signature is unavailable: " + type.FullName + "." + name);
            targets.Add((original, CallbackMethod(prefixName), CallbackMethod(postfixName)));
        }

        private static MethodInfo CallbackMethod(string name)
        {
            MethodInfo method = typeof(LootObservationHooks).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null || method.ReturnType != typeof(void))
                throw new InvalidOperationException("Missing own read-only loot observation callback: " + name);
            return method;
        }

        // __state is our own CLR CallId. All native inputs and original bool are
        // passed by value; no native ref/out argument or __result is changed.
        private static void FishDropBefore(FishAISystem __instance, int __0, LootBox.AutoLiftedType __1, int __2, bool __3, out long __state)
        {
            __state = 0;
            try
            {
                if (!CanBegin()) return;
                __state = Begin(LootObservationMethod.FishAddDropItem,
                    new LootObservationArguments { BonusGrade = __0, LiftType = (int)__1, Tier = __2, IgnoreOverloaded = __3 }, fish: __instance);
            }
            catch { FailActiveCallback(); }
        }
        private static void FishDropAfter(FishAISystem __instance, long __state)
            => End(__state, LootObservationMethod.FishAddDropItem, fish: __instance);

        private static void BagAddBefore(LootBox __instance, int __0, int __1, int __2,
            LootBox.AutoLiftedType __3, Il2CppSystem.Collections.Generic.List<string> __4, bool __5, out long __state)
        {
            __state = 0;
            try
            {
                if (!CanBegin()) return;
                __state = Begin(LootObservationMethod.LootBoxAdd,
                    new LootObservationArguments { ItemId = __0, Count = __1, BonusGrade = __2, LiftType = (int)__3,
                        GetTimesArgumentPresent = !ReferenceEquals(__4, null), UpdateMissionCount = __5 }, bag: __instance);
            }
            catch { FailActiveCallback(); }
        }
        private static void BagAddAfter(LootBox __instance, long __state, bool __result)
            => End(__state, LootObservationMethod.LootBoxAdd, bag: __instance, originalReturn: __result);

        private static void CollectionBefore(int __0, int __1, bool __2, out long __state)
        {
            __state = 0;
            try
            {
                if (!CanBegin()) return;
                __state = Begin(LootObservationMethod.CaughtFishAdd,
                    new LootObservationArguments { CollectionId = __0, CollectionGrade = __1, IsForce = __2 });
            }
            catch { FailActiveCallback(); }
        }
        private static void CollectionAfter(long __state) => End(__state, LootObservationMethod.CaughtFishAdd);

        private static void StorageBefore(IngredientsStorage __instance, LootBoxSlot __0,
            Il2CppSystem.Func<int> __1, out long __state)
        {
            __state = 0;
            try
            {
                if (!CanBegin()) return;
                __state = Begin(LootObservationMethod.IngredientsAddFromLootBox,
                    new LootObservationArguments { ExchangeCallbackArgumentPresent = !ReferenceEquals(__1, null) }, storage: __instance, slot: __0);
            }
            catch { FailActiveCallback(); }
        }
        private static void StorageAfter(IngredientsStorage __instance, LootBoxSlot __0, long __state)
            => End(__state, LootObservationMethod.IngredientsAddFromLootBox, storage: __instance, slot: __0);

        private static bool CanBegin()
        {
            LootObservationHooks active = Volatile.Read(ref _active);
            if (active == null || active.Failed || !Volatile.Read(ref active._accepting)) return false;
            if (!active.ProcessLimitReached) return true;
            Increment(ref _processDropped); return false;
        }

        private static void FailActiveCallback()
        {
            LootObservationHooks active = Volatile.Read(ref _active);
            if (active != null) active.CallbackFailed();
        }

        private static long Begin(LootObservationMethod method, LootObservationArguments arguments,
            FishAISystem fish = null, LootBox bag = null, IngredientsStorage storage = null, LootBoxSlot slot = null)
        {
            LootObservationHooks active = Volatile.Read(ref _active);
            if (active == null) return 0;
            try
            {
                lock (active._gate)
                {
                    if (!active._accepting || active._copy == null || active.Failed) return 0;
                    if (active._calls.Count >= MaxPendingCalls || !TrySequence(out long sequence))
                    { Increment(ref _processDropped); return 0; }
                    long callId = Interlocked.Increment(ref _processNextCallId);
                    int threadId = Environment.CurrentManagedThreadId;
                    active._calls.Add(callId, new CallContext { Method = method, ThreadId = threadId, Arguments = arguments });
                    active._copy(new LootObservationCallback(sequence, callId, threadId, method, LootObservationStage.Before,
                        arguments, fish, bag, storage, slot, null, true));
                    return callId;
                }
            }
            catch { active.CallbackFailed(); return 0; }
        }

        private static void End(long callId, LootObservationMethod method, FishAISystem fish = null,
            LootBox bag = null, IngredientsStorage storage = null, LootBoxSlot slot = null, bool? originalReturn = null)
        {
            LootObservationHooks active = Volatile.Read(ref _active);
            if (active == null || callId == 0) return;
            try
            {
                lock (active._gate)
                {
                    if (!active._calls.TryGetValue(callId, out CallContext context))
                    { Increment(ref _processUnmatchedAfter); return; }
                    // Remove even when quota is reached, so a dropped postfix
                    // cannot leave its call context behind indefinitely.
                    active._calls.Remove(callId);
                    if (!active._accepting || active._copy == null || active.Failed) return;
                    int threadId = Environment.CurrentManagedThreadId;
                    if (context.Method != method || context.ThreadId != threadId)
                        throw new InvalidOperationException("Loot observation prefix/postfix context mismatch.");
                    if (!TrySequence(out long sequence)) { Increment(ref _processDropped); return; }
                    active._copy(new LootObservationCallback(sequence, callId, threadId, method, LootObservationStage.After,
                        context.Arguments, fish, bag, storage, slot, originalReturn, true));
                }
            }
            catch { active.CallbackFailed(); }
        }

        private static bool TrySequence(out long sequence)
        {
            while (true)
            {
                long current = Interlocked.Read(ref _processAccepted);
                if (current >= MaxProcessEvents) { sequence = 0; return false; }
                sequence = current + 1;
                if (Interlocked.CompareExchange(ref _processAccepted, sequence, current) == current) return true;
            }
        }

        private void CallbackFailed()
        {
            while (true)
            {
                int current = Volatile.Read(ref _processCallbackErrors);
                if (current == int.MaxValue || Interlocked.CompareExchange(ref _processCallbackErrors, current + 1, current) == current) break;
            }
            LatchFailure(); StopAccepting();
            // Only a later Unity lifecycle operation may unpatch. Callback
            // errors never escape into the original game execution.
        }

        private static void LatchFailure() => Volatile.Write(ref _processFailed, 1);
        private static void Increment(ref long value)
        {
            while (true)
            {
                long current = Interlocked.Read(ref value);
                if (current == long.MaxValue || Interlocked.CompareExchange(ref value, current + 1, current) == current) return;
            }
        }

        public void CheckHealthy()
        {
            if (CallbackErrors != 0) { LatchFailure(); StopAccepting(); }
            if (!Healthy) throw new InvalidOperationException("Loot observer is unavailable or failed; restart after a failure.");
        }

        private void StopAccepting()
        {
            lock (_gate)
            {
                _accepting = false; _copy = null;
                for (int i = 0; i < _calls.Count; i++) Increment(ref _processDiscardedCalls);
                _calls.Clear();
            }
            Interlocked.CompareExchange(ref _active, null, this);
        }

        private void RemoveOwnPatches()
        {
            _cleanupVerified = false;
            if (_harmony != null) _harmony.UnpatchSelf();
            if (_targets != null)
            {
                foreach (var target in _targets)
                {
                    var info = Harmony.GetPatchInfo(target.Original);
                    if (info != null && info.Owners.Contains(Owner))
                        throw new InvalidOperationException("Own loot observation hook is still registered: " + target.Original.DeclaringType.FullName + "." + target.Original.Name);
                }
            }
            _harmony = null; _targets = null; _cleanupVerified = true;
        }

        public void Dispose()
        {
            StopAccepting();
            if (_harmony == null && _targets == null) return;
            try { RemoveOwnPatches(); }
            catch { LatchFailure(); _cleanupVerified = false; throw; }
        }
    }
}
