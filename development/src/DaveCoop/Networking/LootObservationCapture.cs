using System;
using System.Collections.Generic;
using System.Threading;
using DaveCoop.Core.World;
using UnityEngine;

namespace DaveCoop.Networking
{
    // Local copied diagnostics, never a Core receipt, authorization fact,
    // verified product, capture outcome or instruction to write native storage.
    internal sealed class LootCallObservation
    {
        public long ProcessSequence { get; internal set; }
        public long CallId { get; internal set; }
        public string OriginalMethod { get; internal set; }
        public string Stage { get; internal set; }
        public int CallbackThreadId { get; internal set; }
        public bool MainThread { get; internal set; }
        public int? UnityFrame { get; internal set; }
        public bool PrefixContextMatched { get; internal set; }
        public bool PrefixObservationAvailable { get; internal set; }
        public bool? OriginalReturn { get; internal set; }
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
        public bool InstanceWrapperPresent { get; internal set; }
        public bool SlotWrapperPresent { get; internal set; }
        public bool SlotDataReadable => false;
        public string SlotDataUnavailableReason { get; internal set; }
        public float? NativeWeightAtCallback { get; internal set; }
        public float? NativeCapacityAtCallback { get; internal set; }
        public bool FishIdentityResolvedAtPrefix { get; internal set; }
        public long? ObservedFishSceneEpoch { get; internal set; }
        public long? ObservedFishEntityId { get; internal set; }
        public long? ObservedFishGeneration { get; internal set; }
        public int? ObservedFishDataTid { get; internal set; }
        public string ReadError { get; internal set; }
        public bool CopiedObservation => true;
        public bool ObservationOnly => true;
        public bool ActualBagDeltaProven => false;
        public bool SourceOperationBound => false;
        public bool CaptureSuccess => false;
        public bool StorageDeltaProven => false;
        public bool HostSelectionApplied => false;
    }

    internal sealed class LootObservationCapture
    {
        public const int MaxQueued = 64;
        public const int MaxDrainPerUpdate = 16;
        private const int MaxPrefixContexts = LootObservationHooks.MaxPendingCalls;
        private readonly object _gate = new object();
        private readonly Queue<LootCallObservation> _pending = new Queue<LootCallObservation>(MaxQueued);
        private readonly Dictionary<long, PrefixContext> _prefixes = new Dictionary<long, PrefixContext>();
        private readonly int _unityThreadId;
        private readonly Func<long, HostEntityTarget?> _fishResolver;
        private bool _accepting = true;
        private static long _processDropped;
        private static long _processUnexpectedThreads;
        private static long _processReadErrors;
        private static long _processDiscarded;
        private static int _processFailed;

        // Pure CLR local correlation only. No native wrapper or Unity object is
        // retained across callbacks, even when an original postfix is missing.
        private sealed class PrefixContext
        {
            public LootObservationMethod Method;
            public long LocalInstancePointer;
            public long? FishSceneEpoch;
            public long? FishEntityId;
            public long? FishGeneration;
            public int? FishDataTid;
        }

        public long Dropped => Interlocked.Read(ref _processDropped);
        public long UnexpectedThreads => Interlocked.Read(ref _processUnexpectedThreads);
        public long ReadErrors => Interlocked.Read(ref _processReadErrors);
        public long Discarded => Interlocked.Read(ref _processDiscarded);
        public bool Failed => Volatile.Read(ref _processFailed) != 0;
        public int PendingCount { get { lock (_gate) return _pending.Count; } }

        public LootObservationCapture(int unityThreadId, Func<long, HostEntityTarget?> fishResolver = null)
        {
            if (unityThreadId < 1 || Environment.CurrentManagedThreadId != unityThreadId)
                throw new ArgumentException("Loot observation must be bound on the confirmed Unity thread.", nameof(unityThreadId));
            if (Failed) throw new InvalidOperationException("Loot snapshot capture previously failed; restart before retrying.");
            _unityThreadId = unityThreadId; _fishResolver = fishResolver;
        }

        // Invoked synchronously by Hooks. If copying fails, retain a bounded
        // diagnostic then throw to Hooks, which latches/stops without allowing
        // the exception into the original game method. No native calls at Drain.
        public void Capture(LootObservationCallback call)
        {
            if (call == null) throw new ArgumentNullException(nameof(call));
            lock (_gate)
            {
                if (!_accepting || Failed) return;
                PrefixContext prefix = null;
                if (call.Stage == LootObservationStage.After)
                {
                    _prefixes.TryGetValue(call.CallId, out prefix);
                    _prefixes.Remove(call.CallId);
                }
                if (_pending.Count >= MaxQueued)
                {
                    Increment(ref _processDropped);
                    return;
                }
                LootCallObservation observation = CopyScalars(call);
                try
                {
                    // Thread fence precedes Pointer, Time.frameCount or any
                    // direct generated field access. ReferenceEquals and scalar
                    // copies above only inspect CLR values and wrapper presence.
                    if (!observation.MainThread)
                    {
                        Increment(ref _processUnexpectedThreads);
                        throw new InvalidOperationException("Loot callback is outside the confirmed Unity thread.");
                    }
                    observation.UnityFrame = Time.frameCount;
                    long instancePointer = ReadInstancePointer(call);
                    if (call.Stage == LootObservationStage.Before)
                    {
                        if (_prefixes.Count >= MaxPrefixContexts)
                            throw new InvalidOperationException("Loot observation prefix context limit reached.");
                        prefix = new PrefixContext { Method = call.Method, LocalInstancePointer = instancePointer };
                        if (call.Method == LootObservationMethod.FishAddDropItem && instancePointer != 0)
                            ResolveFishAtPrefix(instancePointer, prefix);
                        _prefixes.Add(call.CallId, prefix);
                        observation.PrefixObservationAvailable = true;
                    }
                    else
                    {
                        observation.PrefixObservationAvailable = prefix != null;
                        if (prefix != null && (prefix.Method != call.Method || prefix.LocalInstancePointer != instancePointer))
                            throw new InvalidOperationException("Loot snapshot prefix/postfix instance mismatch.");
                    }
                    // Preserve prefix binding; never resolve an after against a
                    // reused fish pointer or infer fish ownership for bag writes.
                    CopyFishIdentity(prefix, observation);
                    if (call.Method == LootObservationMethod.LootBoxAdd && instancePointer != 0)
                    {
                        // Confirmed generated field proxies, not native weight,
                        // weightMax, m_Box or AllBoxSlots property getters.
                        float weight = call.Bag._weight_k__BackingField;
                        float capacity = call.Bag.m_WeightMax;
                        if (float.IsNaN(weight) || float.IsInfinity(weight) || float.IsNaN(capacity) || float.IsInfinity(capacity))
                            throw new InvalidOperationException("Native loot weight fields are non-finite.");
                        observation.NativeWeightAtCallback = weight;
                        observation.NativeCapacityAtCallback = capacity;
                    }
                }
                catch (Exception error)
                {
                    observation.ReadError = observation.MainThread
                        ? error.GetType().Name + " while freezing direct loot fields or CLR prefix correlation."
                        : "Callback is outside the confirmed Unity thread; native values were not read.";
                    // Scalars/original return are retained as diagnostics, but
                    // failed partial reads cannot be presented as weight proof.
                    observation.NativeWeightAtCallback = null;
                    observation.NativeCapacityAtCallback = null;
                    Increment(ref _processReadErrors);
                    Volatile.Write(ref _processFailed, 1);
                    _accepting = false;
                    _prefixes.Clear();
                    _pending.Enqueue(observation);
                    throw new InvalidOperationException("Loot observation snapshot copying failed; restart before retrying.", error);
                }
                _pending.Enqueue(observation);
            }
        }

        private LootCallObservation CopyScalars(LootObservationCallback call)
        {
            LootObservationArguments args = call.Arguments;
            return new LootCallObservation
            {
                ProcessSequence = call.ProcessSequence, CallId = call.CallId,
                OriginalMethod = MethodName(call.Method), Stage = call.Stage.ToString(), CallbackThreadId = call.ManagedThreadId,
                MainThread = call.ManagedThreadId == _unityThreadId && Environment.CurrentManagedThreadId == _unityThreadId,
                PrefixContextMatched = call.PrefixContextMatched, OriginalReturn = call.OriginalReturn,
                ItemId = args.ItemId, Count = args.Count, BonusGrade = args.BonusGrade, Tier = args.Tier,
                CollectionId = args.CollectionId, CollectionGrade = args.CollectionGrade, LiftType = args.LiftType,
                IgnoreOverloaded = args.IgnoreOverloaded, UpdateMissionCount = args.UpdateMissionCount, IsForce = args.IsForce,
                GetTimesArgumentPresent = args.GetTimesArgumentPresent, ExchangeCallbackArgumentPresent = args.ExchangeCallbackArgumentPresent,
                InstanceWrapperPresent = !ReferenceEquals(call.Fish, null) || !ReferenceEquals(call.Bag, null) || !ReferenceEquals(call.Storage, null),
                SlotWrapperPresent = !ReferenceEquals(call.Slot, null),
                SlotDataUnavailableReason = call.Method == LootObservationMethod.IngredientsAddFromLootBox
                    ? "Slot item/count/quality use unverified ObscuredInt wrappers; only CLR wrapper presence is observed."
                    : null
            };
        }

        private static long ReadInstancePointer(LootObservationCallback call)
        {
            switch (call.Method)
            {
                case LootObservationMethod.FishAddDropItem:
                    return ReferenceEquals(call.Fish, null) ? 0 : call.Fish.Pointer.ToInt64();
                case LootObservationMethod.LootBoxAdd:
                    return ReferenceEquals(call.Bag, null) ? 0 : call.Bag.Pointer.ToInt64();
                case LootObservationMethod.IngredientsAddFromLootBox:
                    return ReferenceEquals(call.Storage, null) ? 0 : call.Storage.Pointer.ToInt64();
                default: return 0; // The collection router's selected overload is static.
            }
        }

        private void ResolveFishAtPrefix(long pointer, PrefixContext context)
        {
            HostEntityTarget? binding = _fishResolver?.Invoke(pointer);
            if (!binding.HasValue) return;
            HostEntityTarget target = binding.Value;
            if (target.Kind != EntityKind.Fish || target.LocalToken != pointer || target.SceneEpoch < 1 ||
                target.EntityId < 1 || target.Generation < 1 || target.DataTid < 1)
                throw new InvalidOperationException("Invalid CLR observed fish identity snapshot.");
            context.FishSceneEpoch = target.SceneEpoch; context.FishEntityId = target.EntityId;
            context.FishGeneration = target.Generation; context.FishDataTid = target.DataTid;
        }

        private static void CopyFishIdentity(PrefixContext context, LootCallObservation observation)
        {
            if (context == null) return;
            observation.FishIdentityResolvedAtPrefix = context.FishEntityId.HasValue;
            observation.ObservedFishSceneEpoch = context.FishSceneEpoch;
            observation.ObservedFishEntityId = context.FishEntityId;
            observation.ObservedFishGeneration = context.FishGeneration;
            observation.ObservedFishDataTid = context.FishDataTid;
        }

        private static string MethodName(LootObservationMethod method)
        {
            switch (method)
            {
                case LootObservationMethod.FishAddDropItem: return "DR.AI.FishAISystem.AddDropItem_Impl";
                case LootObservationMethod.LootBoxAdd: return "LootBox.Add";
                case LootObservationMethod.CaughtFishAdd: return "SaveDataCaughtFishRouter.AddCaughtFish(int,int,bool)";
                case LootObservationMethod.IngredientsAddFromLootBox: return "IngredientsStorage.AddFromLootBox";
                default: return "Unknown";
            }
        }

        public bool TryTake(out LootCallObservation observation)
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
                _accepting = false;
                for (int i = 0; i < _pending.Count; i++) Increment(ref _processDiscarded);
                _pending.Clear(); _prefixes.Clear();
            }
        }

        private static void Increment(ref long value)
        {
            while (true)
            {
                long current = Interlocked.Read(ref value);
                if (current == long.MaxValue || Interlocked.CompareExchange(ref value, current + 1, current) == current) return;
            }
        }
    }
}
