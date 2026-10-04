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
        FishAddDropItem = 1, LootBoxAdd = 2, CaughtFishAdd = 3, IngredientsAddFromLootBox = 4,
        FishPickup = 5, FishDropWithPlus = 6, FishDropPlus = 7, LootBoxAddIgnoreOverloaded = 8,
        LootBoxAddImpl = 9, LootBoxCheckOverloaded = 10, LootBoxRefreshOverweight = 11,
        SaveDataAddLooting = 12, FishBodySuccessInteract = 13, FishBodyCheckAvailable = 14,
        FishPlusItemRoll = 15, SaveDataAddLootBox = 16
    }
    internal enum LootObservationStage { Before = 1, After = 2, Finalizer = 3 }

    // Scalars and presence only; reference arguments never enter a queue/context.
    internal sealed class LootObservationArguments
    {
        public int? ItemId { get; internal set; }
        public int? Count { get; internal set; }
        public int? BonusGrade { get; internal set; }
        public int? Tier { get; internal set; }
        public int? CollectionId { get; internal set; }
        public int? CollectionGrade { get; internal set; }
        public int? LiftType { get; internal set; }
        public int? RollFishTid { get; internal set; }
        public int? BagType { get; internal set; }
        public float? TargetWeight { get; internal set; }
        public bool? IgnoreOverloaded { get; internal set; }
        public bool? UpdateMissionCount { get; internal set; }
        public bool? IsForce { get; internal set; }
        public bool? IsNew { get; internal set; }
        public bool GetTimesArgumentPresent { get; internal set; }
        public bool ExchangeCallbackArgumentPresent { get; internal set; }
        public bool ItemDataArgumentPresent { get; internal set; }
        public bool ActorArgumentPresent { get; internal set; }
        public bool OtherInstanceWrapperPresent { get; internal set; }
    }

    // This view exists only during a synchronous callback. Capture checks the
    // confirmed thread before Pointer/direct proxies, then freezes CLR values.
    internal sealed class LootObservationCallback
    {
        public long ProcessSequence { get; }
        public long CallId { get; }
        public int ManagedThreadId { get; }
        public LootObservationMethod Method { get; }
        public LootObservationStage Stage { get; }
        public LootObservationArguments Arguments { get; }
        public FishAISystem Fish { get; }
        public FishInteractionBody Body { get; }
        public LootBox Bag { get; }
        public IngredientsStorage Storage { get; }
        public SaveData Save { get; }
        // Original slot argument is offered only to the synchronous prefix
        // copier. Postfix/finalizer reuse its owned CLR candidates.
        public LootBoxSlot Slot { get; }
        // The original Add_Impl argument is held only for this synchronous
        // prefix callback. It never enters CallContext or a diagnostic queue.
        public DR.IItemBase ItemResource { get; }
        public string Key { get; }
        public bool? OriginalReturn { get; }
        public int? OriginalIntReturn { get; }
        public bool OriginalException { get; }
        public bool PrefixContextMatched => true;
        internal LootObservationCallback(long sequence, long callId, int threadId,
            LootObservationMethod method, LootObservationStage stage, LootObservationArguments arguments,
            FishAISystem fish, FishInteractionBody body, LootBox bag, IngredientsStorage storage,
            SaveData save, LootBoxSlot slot, string key, bool? originalReturn, int? originalIntReturn, bool originalException,
            DR.IItemBase itemResource = null)
        {
            ProcessSequence = sequence; CallId = callId; ManagedThreadId = threadId;
            Method = method; Stage = stage; Arguments = arguments;
            Fish = fish; Body = body; Bag = bag; Storage = storage; Save = save; Slot = slot; Key = key;
            ItemResource = itemResource;
            OriginalReturn = originalReturn; OriginalIntReturn = originalIntReturn; OriginalException = originalException;
        }
    }

    // Metadata/registration checks do not prove native ABI or startup coverage.
    // Prefix/postfix/finalizer never replace original inputs, results or errors.
    internal sealed class LootObservationHooks : IDisposable
    {
        private const string Owner = Plugin.Id + ".loot-observation";
        public const int TargetCount = 16;
        public const int MaxProcessEvents = 8192;
        public const int MaxPendingCalls = 128;
        private static LootObservationHooks _active;
        private static long _processAccepted, _processNextCallId, _processDropped;
        private static long _processUnmatchedAfter, _processDiscardedCalls;
        private static int _processCallbackErrors, _processFailed;
        private readonly object _gate = new object();
        private readonly Dictionary<long, CallContext> _calls = new Dictionary<long, CallContext>();
        private List<(MethodInfo Original, MethodInfo Prefix, MethodInfo Postfix)> _targets;
        private Action<LootObservationCallback> _copy;
        private Action<string> _invalidate;
        private Harmony _harmony;
        private bool _accepting, _started;
        private int _cleanupAttempted;
        private bool _cleanupVerified = true;
        private sealed class CallContext
        {
            public LootObservationMethod Method;
            public int ThreadId;
            public LootObservationArguments Arguments;
            public bool PostfixObserved;
            public bool? OriginalReturn;
            public int? OriginalIntReturn;
        }
        public bool Installed => _harmony != null && !Failed && Volatile.Read(ref _accepting);
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

        public void Enable(Action<LootObservationCallback> copy, Action<string> invalidate)
        {
            if (copy == null) throw new ArgumentNullException(nameof(copy));
            if (invalidate == null) throw new ArgumentNullException(nameof(invalidate));
            if (Installed) return;
            if (Failed || CallbackErrors != 0 || !_cleanupVerified)
                throw new InvalidOperationException("Loot observer unavailable; restart required.");
            if (_started) throw new InvalidOperationException("Loot hook instances are single-use.");
            if (ProcessLimitReached) throw new InvalidOperationException("Loot process quota exhausted.");
            LootObservationHooks previous = Interlocked.CompareExchange(ref _active, this, null);
            if (previous != null && !ReferenceEquals(previous, this))
                throw new InvalidOperationException("Another loot observer is active.");
            try
            {
                _started = true; _invalidate = invalidate;
                _targets = CreateTargets();
                _harmony = new Harmony(Owner); _cleanupVerified = false;
                foreach (var target in _targets)
                {
                    _harmony.Patch(target.Original, prefix: new HarmonyMethod(target.Prefix),
                        postfix: new HarmonyMethod(target.Postfix), finalizer: new HarmonyMethod(CallbackMethod(nameof(CallFinally))));
                    VerifyTarget(target);
                }
                lock (_gate) { _copy = copy; _accepting = true; }
            }
            catch
            {
                LatchFailure(); StopAccepting();
                try { Dispose(); } catch { }
                throw;
            }
        }
        private static List<(MethodInfo Original, MethodInfo Prefix, MethodInfo Postfix)> CreateTargets()
        {
            var targets = new List<(MethodInfo Original, MethodInfo Prefix, MethodInfo Postfix)>();
            Type lifted = typeof(LootBox.AutoLiftedType), times = typeof(Il2CppSystem.Collections.Generic.List<string>);
            Type[] bagArgs = { typeof(int), typeof(int), typeof(int), lifted, times, typeof(bool) };
            Add(targets, typeof(FishAISystem), "AddDropItem_Impl", false, typeof(void),
                new[] { typeof(int), lifted, typeof(int), typeof(bool) }, nameof(FishDropBefore), nameof(FishDropAfter));
            Add(targets, typeof(LootBox), "Add", false, typeof(bool), bagArgs, nameof(BagAddBefore), nameof(BagAddAfter));
            Add(targets, typeof(SaveDataCaughtFishRouter), "AddCaughtFish", true, typeof(void),
                new[] { typeof(int), typeof(int), typeof(bool) }, nameof(CollectionBefore), nameof(CollectionAfter));
            Add(targets, typeof(IngredientsStorage), "AddFromLootBox", false, typeof(void),
                new[] { typeof(LootBoxSlot), typeof(Il2CppSystem.Func<int>) }, nameof(StorageBefore), nameof(StorageAfter));
            Add(targets, typeof(FishAISystem), "SuccessPickupFish", false, typeof(void),
                new[] { typeof(int), typeof(bool) }, nameof(PickupBefore), nameof(PickupAfter));
            Add(targets, typeof(FishAISystem), "AddDropItemLootBoxWithPlus", false, typeof(void),
                new[] { typeof(int), lifted, typeof(int) }, nameof(DropWithPlusBefore), nameof(DropWithPlusAfter));
            Add(targets, typeof(FishAISystem), "AddDropPlusItem_Impl", false, typeof(void),
                new[] { typeof(int), lifted }, nameof(DropPlusBefore), nameof(DropPlusAfter));
            Add(targets, typeof(LootBox), "AddIgnoreOverloaded", false, typeof(bool), bagArgs, nameof(BagIgnoreBefore), nameof(BagIgnoreAfter));
            Add(targets, typeof(LootBox), "Add_Impl", false, typeof(void),
                new[] { typeof(DR.IItemBase), typeof(int), typeof(int), lifted, times, typeof(bool) }, nameof(BagImplBefore), nameof(BagImplAfter));
            Add(targets, typeof(LootBox), "CheckOverloadedState", false, typeof(bool), new[] { typeof(int) }, nameof(CapacityBefore), nameof(CapacityAfter));
            Add(targets, typeof(LootBox), "RefreshOverweight", false, typeof(void), new[] { typeof(float) }, nameof(OverweightBefore), nameof(OverweightAfter));
            Add(targets, typeof(SaveData), "AddLootingSaveData", false, typeof(void), new[] { typeof(int), typeof(bool) }, nameof(LootingBefore), nameof(LootingAfter));
            Add(targets, typeof(FishInteractionBody), "SuccessInteract", false, typeof(void), new[] { typeof(BaseCharacter) }, nameof(BodySuccessBefore), nameof(BodySuccessAfter));
            Add(targets, typeof(FishInteractionBody), "CheckAvailableInteraction", false, typeof(bool), new[] { typeof(BaseCharacter) }, nameof(BodyAvailableBefore), nameof(BodyAvailableAfter));
            Add(targets, typeof(FishPlusItemPity), "RollPlusItem", false, typeof(int), new[] { typeof(int), typeof(int) }, nameof(RollBefore), nameof(RollAfter));
            Add(targets, typeof(SaveData), "AddLootBox", false, typeof(void), new[] { typeof(SaveData.LootBoxType), typeof(string), typeof(LootBoxSlot) }, nameof(SavedSlotBefore), nameof(SavedSlotAfter));
            if (targets.Count != TargetCount) throw new InvalidOperationException("Loot target count mismatch.");
            return targets;
        }
        private static void Add(List<(MethodInfo Original, MethodInfo Prefix, MethodInfo Postfix)> targets,
            Type type, string name, bool isStatic, Type returnType, Type[] parameters, string prefixName, string postfixName)
        {
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly |
                (isStatic ? BindingFlags.Static : BindingFlags.Instance);
            MethodInfo original = type.GetMethod(name, flags, null, parameters, null);
            if (original == null || original.DeclaringType != type || original.IsStatic != isStatic || original.IsGenericMethod || original.ReturnType != returnType)
                throw new InvalidOperationException("Expected loot hook signature unavailable.");
            targets.Add((original, CallbackMethod(prefixName), CallbackMethod(postfixName)));
        }
        private static MethodInfo CallbackMethod(string name)
        {
            MethodInfo method = typeof(LootObservationHooks).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null || method.ReturnType != typeof(void)) throw new InvalidOperationException("Expected void loot callback unavailable.");
            return method;
        }
        private static void VerifyTarget((MethodInfo Original, MethodInfo Prefix, MethodInfo Postfix) target)
        {
            var info = Harmony.GetPatchInfo(target.Original);
            if (info == null || !HasOwn(info.Prefixes, target.Prefix) || !HasOwn(info.Postfixes, target.Postfix) ||
                !HasOwn(info.Finalizers, CallbackMethod(nameof(CallFinally)))) throw new InvalidOperationException("Own loot registration unavailable.");
        }
        private static bool HasOwn(IEnumerable<Patch> patches, MethodInfo method)
        {
            foreach (Patch patch in patches) if (patch.owner == Owner && patch.PatchMethod == method) return true;
            return false;
        }

        // No ref original argument/result and no bool prefix/exception-returning
        // finalizer. The finalizer is the sole successful call-context removal.
        private static void FishDropBefore(FishAISystem __instance, int __0, LootBox.AutoLiftedType __1, int __2, bool __3, out long __state)
            => __state = Begin(LootObservationMethod.FishAddDropItem, new LootObservationArguments { BonusGrade = __0, LiftType = (int)__1, Tier = __2, IgnoreOverloaded = __3 }, fish: __instance);
        private static void FishDropAfter(FishAISystem __instance, long __state) => Postfix(__state, LootObservationMethod.FishAddDropItem, fish: __instance);
        private static void PickupBefore(FishAISystem __instance, int __0, bool __1, out long __state)
            => __state = Begin(LootObservationMethod.FishPickup, new LootObservationArguments { BonusGrade = __0, IgnoreOverloaded = __1 }, fish: __instance);
        private static void PickupAfter(FishAISystem __instance, long __state) => Postfix(__state, LootObservationMethod.FishPickup, fish: __instance);
        private static void DropWithPlusBefore(FishAISystem __instance, int __0, LootBox.AutoLiftedType __1, int __2, out long __state)
            => __state = Begin(LootObservationMethod.FishDropWithPlus, new LootObservationArguments { BonusGrade = __0, LiftType = (int)__1, Tier = __2 }, fish: __instance);
        private static void DropWithPlusAfter(FishAISystem __instance, long __state) => Postfix(__state, LootObservationMethod.FishDropWithPlus, fish: __instance);
        private static void DropPlusBefore(FishAISystem __instance, int __0, LootBox.AutoLiftedType __1, out long __state)
            => __state = Begin(LootObservationMethod.FishDropPlus, new LootObservationArguments { BonusGrade = __0, LiftType = (int)__1 }, fish: __instance);
        private static void DropPlusAfter(FishAISystem __instance, long __state) => Postfix(__state, LootObservationMethod.FishDropPlus, fish: __instance);
        private static void BodySuccessBefore(FishInteractionBody __instance, BaseCharacter __0, out long __state)
            => __state = Begin(LootObservationMethod.FishBodySuccessInteract, new LootObservationArguments { ActorArgumentPresent = !ReferenceEquals(__0, null) }, body: __instance);
        private static void BodySuccessAfter(FishInteractionBody __instance, long __state) => Postfix(__state, LootObservationMethod.FishBodySuccessInteract, body: __instance);
        private static void BodyAvailableBefore(FishInteractionBody __instance, BaseCharacter __0, out long __state)
            => __state = Begin(LootObservationMethod.FishBodyCheckAvailable, new LootObservationArguments { ActorArgumentPresent = !ReferenceEquals(__0, null) }, body: __instance);
        private static void BodyAvailableAfter(FishInteractionBody __instance, long __state, bool __result) => Postfix(__state, LootObservationMethod.FishBodyCheckAvailable, body: __instance, originalReturn: __result);
        private static LootObservationArguments BagArguments(int id, int count, int grade, LootBox.AutoLiftedType type, Il2CppSystem.Collections.Generic.List<string> times, bool mission)
            => new LootObservationArguments { ItemId = id, Count = count, BonusGrade = grade, LiftType = (int)type, GetTimesArgumentPresent = !ReferenceEquals(times, null), UpdateMissionCount = mission };
        private static void BagAddBefore(LootBox __instance, int __0, int __1, int __2, LootBox.AutoLiftedType __3, Il2CppSystem.Collections.Generic.List<string> __4, bool __5, out long __state)
            => __state = Begin(LootObservationMethod.LootBoxAdd, BagArguments(__0, __1, __2, __3, __4, __5), bag: __instance);
        private static void BagAddAfter(LootBox __instance, long __state, bool __result) => Postfix(__state, LootObservationMethod.LootBoxAdd, bag: __instance, originalReturn: __result);
        private static void BagIgnoreBefore(LootBox __instance, int __0, int __1, int __2, LootBox.AutoLiftedType __3, Il2CppSystem.Collections.Generic.List<string> __4, bool __5, out long __state)
            => __state = Begin(LootObservationMethod.LootBoxAddIgnoreOverloaded, BagArguments(__0, __1, __2, __3, __4, __5), bag: __instance);
        private static void BagIgnoreAfter(LootBox __instance, long __state, bool __result) => Postfix(__state, LootObservationMethod.LootBoxAddIgnoreOverloaded, bag: __instance, originalReturn: __result);
        private static void BagImplBefore(LootBox __instance, DR.IItemBase __0, int __1, int __2, LootBox.AutoLiftedType __3, Il2CppSystem.Collections.Generic.List<string> __4, bool __5, out long __state)
            => __state = Begin(LootObservationMethod.LootBoxAddImpl, new LootObservationArguments { ItemDataArgumentPresent = !ReferenceEquals(__0, null), Count = __1, BonusGrade = __2, LiftType = (int)__3, GetTimesArgumentPresent = !ReferenceEquals(__4, null), UpdateMissionCount = __5 }, bag: __instance, itemResource: __0);
        private static void BagImplAfter(LootBox __instance, long __state) => Postfix(__state, LootObservationMethod.LootBoxAddImpl, bag: __instance);
        private static void CapacityBefore(LootBox __instance, int __0, out long __state) => __state = Begin(LootObservationMethod.LootBoxCheckOverloaded, new LootObservationArguments { ItemId = __0 }, bag: __instance);
        private static void CapacityAfter(LootBox __instance, long __state, bool __result) => Postfix(__state, LootObservationMethod.LootBoxCheckOverloaded, bag: __instance, originalReturn: __result);
        private static void OverweightBefore(LootBox __instance, float __0, out long __state) => __state = Begin(LootObservationMethod.LootBoxRefreshOverweight, new LootObservationArguments { TargetWeight = __0 }, bag: __instance);
        private static void OverweightAfter(LootBox __instance, long __state) => Postfix(__state, LootObservationMethod.LootBoxRefreshOverweight, bag: __instance);
        private static void LootingBefore(SaveData __instance, int __0, bool __1, out long __state) => __state = Begin(LootObservationMethod.SaveDataAddLooting, new LootObservationArguments { ItemId = __0, IsNew = __1 }, save: __instance);
        private static void LootingAfter(SaveData __instance, long __state) => Postfix(__state, LootObservationMethod.SaveDataAddLooting, save: __instance);
        private static void CollectionBefore(int __0, int __1, bool __2, out long __state) => __state = Begin(LootObservationMethod.CaughtFishAdd, new LootObservationArguments { CollectionId = __0, CollectionGrade = __1, IsForce = __2 });
        private static void CollectionAfter(long __state) => Postfix(__state, LootObservationMethod.CaughtFishAdd);
        private static void StorageBefore(IngredientsStorage __instance, LootBoxSlot __0, Il2CppSystem.Func<int> __1, out long __state)
            => __state = Begin(LootObservationMethod.IngredientsAddFromLootBox, new LootObservationArguments { ExchangeCallbackArgumentPresent = !ReferenceEquals(__1, null) }, storage: __instance, slot: __0);
        private static void StorageAfter(IngredientsStorage __instance, LootBoxSlot __0, long __state) => Postfix(__state, LootObservationMethod.IngredientsAddFromLootBox, storage: __instance);
        private static void RollBefore(FishPlusItemPity __instance, int __0, int __1, out long __state)
            => __state = Begin(LootObservationMethod.FishPlusItemRoll, new LootObservationArguments { RollFishTid = __0, BonusGrade = __1, OtherInstanceWrapperPresent = !ReferenceEquals(__instance, null) });
        private static void RollAfter(long __state, int __result) => Postfix(__state, LootObservationMethod.FishPlusItemRoll, originalIntReturn: __result);
        private static void SavedSlotBefore(SaveData __instance, SaveData.LootBoxType __0, string __1, LootBoxSlot __2, out long __state)
            => __state = Begin(LootObservationMethod.SaveDataAddLootBox, new LootObservationArguments { BagType = (int)__0 }, save: __instance, slot: __2, key: __1);
        private static void SavedSlotAfter(SaveData __instance, long __state) => Postfix(__state, LootObservationMethod.SaveDataAddLootBox, save: __instance);
        private static void CallFinally(long __state, Exception __exception)
            => Complete(__state, LootObservationStage.Finalizer, null, null, null, null, null, null, null, !ReferenceEquals(__exception, null));

        private static long Begin(LootObservationMethod method, LootObservationArguments arguments,
            FishAISystem fish = null, FishInteractionBody body = null, LootBox bag = null, IngredientsStorage storage = null, SaveData save = null, LootBoxSlot slot = null, string key = null,
            DR.IItemBase itemResource = null)
        {
            LootObservationHooks active = Volatile.Read(ref _active);
            if (active == null || active.Failed || !Volatile.Read(ref active._accepting)) return 0;
            try
            {
                lock (active._gate)
                {
                    if (!active._accepting || active._copy == null || active.Failed) return 0;
                    if (active._calls.Count >= MaxPendingCalls || !TrySequence(out long sequence) || !TryCallId(out long callId))
                    { Increment(ref _processDropped); throw new InvalidOperationException("Loot callback quota exhausted."); }
                    int threadId = Environment.CurrentManagedThreadId;
                    active._calls.Add(callId, new CallContext { Method = method, ThreadId = threadId, Arguments = arguments });
                    active._copy(new LootObservationCallback(sequence, callId, threadId, method, LootObservationStage.Before, arguments, fish, body, bag, storage, save, slot, key, null, null, false, itemResource));
                    return callId;
                }
            }
            catch { active.CallbackFailed(); return 0; }
        }
        private static void Postfix(long callId, LootObservationMethod method, FishAISystem fish = null, FishInteractionBody body = null,
            LootBox bag = null, IngredientsStorage storage = null, SaveData save = null, LootBoxSlot slot = null, bool? originalReturn = null, int? originalIntReturn = null)
            => Complete(callId, LootObservationStage.After, method, fish, body, bag, storage, save, slot, false, originalReturn, originalIntReturn);
        private static void Complete(long callId, LootObservationStage stage, LootObservationMethod? method,
            FishAISystem fish, FishInteractionBody body, LootBox bag, IngredientsStorage storage, SaveData save, LootBoxSlot slot,
            bool originalException, bool? originalReturn = null, int? originalIntReturn = null)
        {
            LootObservationHooks active = Volatile.Read(ref _active);
            if (active == null || callId == 0 || active.Failed || !Volatile.Read(ref active._accepting)) return;
            try
            {
                lock (active._gate)
                {
                    if (!active._calls.TryGetValue(callId, out CallContext context))
                    { Increment(ref _processUnmatchedAfter); throw new InvalidOperationException("Loot call context unavailable."); }
                    int threadId = Environment.CurrentManagedThreadId;
                    if (context.ThreadId != threadId || (method.HasValue && context.Method != method.Value) || (stage == LootObservationStage.After && context.PostfixObserved))
                        throw new InvalidOperationException("Loot call completion mismatch.");
                    if (!TrySequence(out long sequence))
                    { Increment(ref _processDropped); throw new InvalidOperationException("Loot callback quota exhausted."); }
                    if (stage == LootObservationStage.After)
                    { context.PostfixObserved = true; context.OriginalReturn = originalReturn; context.OriginalIntReturn = originalIntReturn; }
                    active._copy(new LootObservationCallback(sequence, callId, threadId, context.Method, stage, context.Arguments,
                        fish, body, bag, storage, save, slot, null, context.OriginalReturn, context.OriginalIntReturn, originalException));
                    if (stage == LootObservationStage.Finalizer) active._calls.Remove(callId);
                }
            }
            catch { active.CallbackFailed(); }
        }
        private static bool TrySequence(out long sequence) => TryNext(ref _processAccepted, MaxProcessEvents, out sequence);
        private static bool TryCallId(out long callId) => TryNext(ref _processNextCallId, long.MaxValue, out callId);
        private static bool TryNext(ref long value, long maximum, out long next)
        {
            while (true)
            {
                long current = Interlocked.Read(ref value);
                if (current >= maximum) { next = 0; return false; }
                next = current + 1;
                if (Interlocked.CompareExchange(ref value, next, current) == current) return true;
            }
        }
        private void CallbackFailed()
        {
            while (true)
            {
                int current = Volatile.Read(ref _processCallbackErrors);
                if (current == int.MaxValue || Interlocked.CompareExchange(ref _processCallbackErrors, current + 1, current) == current) break;
            }
            LatchFailure(); RevokeCopyEvidence(); StopAccepting();
        }
        private void RevokeCopyEvidence()
        {
            // Pure CLR invalidation is synchronous even if a hook quota or
            // context fails before the next Capture callback/Unity Update.
            try { _invalidate?.Invoke("Loot hook failure revoked synchronous evidence."); } catch { }
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
            try
            {
                if (!Healthy || _targets == null) throw new InvalidOperationException("Loot observer unavailable.");
                foreach (var target in _targets) VerifyTarget(target);
            }
            catch { LatchFailure(); RevokeCopyEvidence(); StopAccepting(); throw; }
        }
        private void StopAccepting()
        {
            lock (_gate)
            {
                _accepting = false;
                for (int i = 0; i < _calls.Count; i++) Increment(ref _processDiscardedCalls);
                _calls.Clear();
            }
            // Keep the owner/capture reference until one own cleanup is verified.
        }
        public void Dispose()
        {
            StopAccepting();
            if (_cleanupVerified)
            { _copy = null; _invalidate = null; Interlocked.CompareExchange(ref _active, null, this); return; }
            if (Interlocked.CompareExchange(ref _cleanupAttempted, 1, 0) != 0)
                throw new InvalidOperationException("Loot cleanup not verified.");
            try
            {
                if (_harmony != null) _harmony.UnpatchSelf();
                if (_targets != null)
                    foreach (var target in _targets)
                    {
                        var info = Harmony.GetPatchInfo(target.Original);
                        if (info != null && info.Owners.Contains(Owner)) throw new InvalidOperationException("Own loot removal not verified.");
                    }
                _harmony = null; _targets = null; _copy = null; _invalidate = null; _cleanupVerified = true;
                Interlocked.CompareExchange(ref _active, null, this);
            }
            catch { LatchFailure(); _cleanupVerified = false; throw; }
        }
    }
}
