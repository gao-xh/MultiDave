using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using DaveCoop.Core.Cargo;
using DaveCoop.Core.World;
using DR.AI;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace DaveCoop.Networking
{
    // Immutable CLR values from two matching direct-field samples. These are
    // resource defaults, not slot quality, effective weight or product identity.
    internal sealed class LootResourceObservation
    {
        public string ResourceSampleStage => "Before";
        public string ExactClassKind { get; }
        public int? ResourceTid { get; }
        public int? ItemDataId { get; }
        public int? BaseItemGrade { get; }
        public float? BaseItemWeight { get; }
        public bool Available => SamplesMatch;
        public bool SamplesMatch { get; }
        public string UnavailableReason { get; }
        public bool ObservationOnly => true;
        public bool FinalGradeVerified => false;
        public bool EffectiveWeightVerified => false;
        public bool ResourceProductMappingVerified => false;
        public bool NativeFieldAbiVerified => false;
        public bool NativePermission => false;
        public bool FullYield => false;

        internal LootResourceObservation(string kind, int tid, int itemDataId, int grade, float weight)
        {
            ExactClassKind = kind; ResourceTid = tid; ItemDataId = itemDataId;
            BaseItemGrade = grade; BaseItemWeight = weight; SamplesMatch = true;
        }
        private LootResourceObservation(string reason) { UnavailableReason = reason; }
        internal static LootResourceObservation Unavailable(string reason) => new LootResourceObservation(reason);
    }

    // Only decoded CLR candidates survive the callback. Encrypted snapshots,
    // native wrappers and pointers never enter a prefix context or the queue.
    internal sealed class LootSlotObservation
    {
        public string SlotSampleStage => "Before";
        public string ExactClassKind { get; }
        public LootObscuredIntCandidate ItemId { get; }
        public LootObscuredIntCandidate Grade { get; }
        public LootObscuredIntCandidate FinalGrade { get; }
        public LootObscuredIntCandidate TotalCount { get; }
        public bool SamplesMatch { get; }
        public bool CandidatesAvailable => ItemId?.Available == true || Grade?.Available == true ||
            FinalGrade?.Available == true || TotalCount?.Available == true;
        public bool AllCandidatesAvailable => ItemId?.Available == true && Grade?.Available == true &&
            FinalGrade?.Available == true && TotalCount?.Available == true;
        public string UnavailableReason { get; }
        public bool ObservationOnly => true;
        public bool NoNativeDecodeCalls => true;
        public bool NativeFieldAbiVerified => false;
        public bool FinalGradeVerified => false;
        public bool EffectiveWeightVerified => false;
        public bool ResourceProductMappingVerified => false;
        public bool BagDelta => false;
        public bool FullYield => false;
        public bool Permission => false;

        internal LootSlotObservation(LootObscuredIntCandidate itemId, LootObscuredIntCandidate grade,
            LootObscuredIntCandidate finalGrade, LootObscuredIntCandidate totalCount)
        {
            ExactClassKind = "LootBoxSlot"; ItemId = itemId; Grade = grade;
            FinalGrade = finalGrade; TotalCount = totalCount; SamplesMatch = true;
        }
        private LootSlotObservation(string reason) { UnavailableReason = reason; }
        internal static LootSlotObservation Unavailable(string reason) => new LootSlotObservation(reason);
    }

    // Owned CLR diagnostics only. A source is an observed synchronous enclosure,
    // not a direct caller, employee binding, complete yield or cargo receipt.
    internal sealed class LootCallObservation
    {
        public long ProcessSequence { get; internal set; }
        public Guid RunId { get; internal set; }
        public long CallId { get; internal set; }
        public string OriginalMethod { get; internal set; }
        public string Stage { get; internal set; }
        public int CallbackThreadId { get; internal set; }
        public bool MainThread { get; internal set; }
        public int? UnityFrame { get; internal set; }
        public bool PrefixContextMatched { get; internal set; }
        public bool PrefixObservationAvailable { get; internal set; }
        public bool? OriginalReturn { get; internal set; }
        public int? OriginalIntReturn { get; internal set; }
        public bool OriginalException { get; internal set; }
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
        public LootResourceObservation Resource { get; internal set; }
        public string ResourceSampleStage => Resource?.ResourceSampleStage;
        public LootSlotObservation SlotCandidates { get; internal set; }
        public string SlotSampleStage => SlotCandidates?.SlotSampleStage;
        public bool SlotCandidatesAvailable => SlotCandidates?.CandidatesAvailable == true;
        public bool NoNativeDecodeCalls => true;
        public bool ActorArgumentPresent { get; internal set; }
        public bool InstanceWrapperPresent { get; internal set; }
        public bool SlotWrapperPresent { get; internal set; }
        public bool? FishCapturedStateWrapperPresent { get; internal set; }
        public int? KeyLength { get; internal set; }
        public string KeyHash { get; internal set; }
        public bool SlotDataReadable => false;
        public string SlotDataUnavailableReason { get; internal set; }
        public float? NativeWeightAtCallback { get; internal set; }
        public float? NativeCapacityAtCallback { get; internal set; }
        public float? NativeWeightParameterAtCallback { get; internal set; }
        public float? NativeOverloadedThresholdAtCallback { get; internal set; }
        public long? LocalFishOrdinal { get; internal set; }
        public bool FishIdentityResolvedAtPrefix { get; internal set; }
        public long? ObservedFishSceneEpoch { get; internal set; }
        public long? ObservedFishEntityId { get; internal set; }
        public long? ObservedFishGeneration { get; internal set; }
        public int? ObservedFishDataTid { get; internal set; }
        public LootLineageRecord Lineage { get; internal set; }
        public long ObservedParentCallId => Lineage?.ObservedParentCallId ?? 0;
        public long SourceRootCallId => Lineage?.SourceRootCallId ?? 0;
        public int? ObservedDepth => Lineage?.Depth;
        public bool ChainMatched => Lineage != null && Lineage.HealthyAtCapture;
        public bool ObservedSynchronousEnclosure => Lineage != null && Lineage.ObservedParentCallId != 0;
        public string ReadError { get; internal set; }
        public bool CopiedObservation => true;
        public bool ObservationOnly => true;
        public bool ActualBagDeltaProven => false;
        public bool ActualBagDelta => false;
        public bool SourceOperationBound => false;
        public bool NativeSourceOperationBound => false;
        public bool MemberOwnershipVerified => false;
        public bool CaptureSuccess => false;
        public bool FullYield => false;
        public bool YieldComplete => false;
        public bool NativeGenerationVerified => false;
        public bool NativeHookAbiVerified => false;
        public bool FinalGradeVerified => false;
        public bool EffectiveWeightVerified => false;
        public bool ResourceProductMappingVerified => false;
        public bool CargoPermission => false;
        public bool StorageDeltaProven => false;
        public bool HostSelectionApplied => false;
        public bool NativeRewards => false;
    }

    internal sealed class LootObservationCapture
    {
        public const int MaxQueued = 512;
        public const int MaxDrainPerUpdate = 16;
        public const int MaxFishOrdinals = 256;
        public const int MaxKeyUtf16 = 512;
        public const int MaxProcessKeyUtf16 = 65536;
        public const int MaxResourceReadsPerPrefix = 32;
        public const int MaxProcessResourceReads = 65536;
        public const int MaxSlotReadsPerPrefix = 32;
        public const int MaxProcessSlotReads = 65536;
        private readonly object _gate = new object();
        private readonly Queue<LootCallObservation> _pending = new Queue<LootCallObservation>(MaxQueued);
        private readonly Dictionary<long, PrefixContext> _prefixes = new Dictionary<long, PrefixContext>();
        private readonly Dictionary<long, long> _fishOrdinals = new Dictionary<long, long>();
        private readonly int _unityThreadId;
        private readonly Func<long, HostEntityTarget?> _fishResolver;
        private readonly LootCallLineage _lineage;
        private bool _accepting = true, _insideCapture;
        private static long _processDropped, _processUnexpectedThreads, _processReadErrors, _processDiscarded, _processKeyUtf16;
        private static long _processResourceReads;
        private static long _processSlotReads;
        private static int _processFailed;
        private sealed class PrefixContext
        {
            public LootObservationMethod Method;
            public long LocalInstancePointer;
            public long? LocalFishOrdinal;
            public LootLineageToken Token;
            public int? UnityFrame;
            public int? KeyLength;
            public string KeyHash;
            public bool InstancePresent, SlotPresent;
            public LootResourceObservation Resource;
            public LootSlotObservation SlotCandidates;
        }
        public Guid RunId => _lineage.RunId;
        public LootCallLineage Lineage => _lineage;
        public long Dropped => Interlocked.Read(ref _processDropped);
        public long UnexpectedThreads => Interlocked.Read(ref _processUnexpectedThreads);
        public long ReadErrors => Interlocked.Read(ref _processReadErrors);
        public long Discarded => Interlocked.Read(ref _processDiscarded);
        public long ProcessKeyUtf16 => Interlocked.Read(ref _processKeyUtf16);
        public long ProcessResourceReads => Interlocked.Read(ref _processResourceReads);
        public long ProcessSlotReads => Interlocked.Read(ref _processSlotReads);
        public bool Failed => Volatile.Read(ref _processFailed) != 0;
        public bool Healthy => !Failed && _accepting && _lineage.Healthy;
        public int PendingCount { get { lock (_gate) return _pending.Count; } }
        public int PendingContexts { get { lock (_gate) return _prefixes.Count; } }
        public int FishOrdinalCount { get { lock (_gate) return _fishOrdinals.Count; } }

        public LootObservationCapture(int unityThreadId, Func<long, HostEntityTarget?> fishResolver = null)
        {
            if (unityThreadId < 1 || Environment.CurrentManagedThreadId != unityThreadId)
                throw new ArgumentException("Loot capture requires the confirmed Unity thread.", nameof(unityThreadId));
            if (Failed) throw new InvalidOperationException("Loot copying previously failed; restart required.");
            _unityThreadId = unityThreadId; _fishResolver = fishResolver;
            _lineage = new LootCallLineage(unityThreadId);
        }

        public void Capture(LootObservationCallback call)
        {
            if (call == null) throw new ArgumentNullException(nameof(call));
            lock (_gate)
            {
                if (!_accepting || Failed) return;
                if (_insideCapture)
                {
                    Fail("Loot field freezing was reentered.", true);
                    throw new InvalidOperationException("Loot copying was reentered.");
                }
                _insideCapture = true;
                LootCallObservation observed = CopyScalars(call);
                try
                {
                    // Before every native read, not merely before Pointer. The
                    // post-read fence also stops a callback-induced new failure.
                    if (!observed.MainThread)
                    {
                        Increment(ref _processUnexpectedThreads);
                        throw new InvalidOperationException("Loot callback thread is unbound.");
                    }
                    CheckReadWindow();
                    if (_pending.Count >= MaxQueued)
                    {
                        Increment(ref _processDropped);
                        throw new InvalidOperationException("Loot copy queue exhausted.");
                    }
                    PrefixContext prefix;
                    if (call.Stage == LootObservationStage.Before)
                    {
                        if (_prefixes.Count >= LootObservationHooks.MaxPendingCalls)
                            throw new InvalidOperationException("Loot prefix context quota exhausted.");
                        prefix = new PrefixContext
                        {
                            Method = call.Method, LocalInstancePointer = ReadInstancePointer(call),
                            UnityFrame = Read(() => Time.frameCount), InstancePresent = observed.InstanceWrapperPresent,
                            SlotPresent = observed.SlotWrapperPresent
                        };
                        LootLineageSource source = null;
                        if (IsFishBoundary(call.Method))
                        {
                            FishAISystem fish = call.Fish;
                            // Actual metadata type SABaseFishSystem derives from
                            // FishAISystem. No cast/getter/singleton fallback.
                            if (!ReferenceEquals(call.Body, null)) fish = Read(() => call.Body._ownerFish);
                            if (!ReferenceEquals(fish, null))
                            {
                                long pointer = Read(() => fish.Pointer.ToInt64());
                                if (pointer == 0) throw new InvalidOperationException("Loot fish wrapper has no identity.");
                                prefix.LocalFishOrdinal = FishOrdinal(pointer);
                                source = ResolveFish(pointer, prefix.LocalFishOrdinal.Value);
                            }
                        }
                        if (call.Method == LootObservationMethod.SaveDataAddLootBox) FreezeKey(call.Key, prefix);
                        prefix.Token = _lineage.Begin(call.CallId, (int)call.Method, IsFishBoundary(call.Method), source, call.ManagedThreadId);
                        if (prefix.Token == null || !_lineage.Healthy) throw new InvalidOperationException("Loot prefix lineage rejected.");
                        if (call.Method == LootObservationMethod.LootBoxAddImpl)
                            prefix.Resource = FreezeResource(call.ItemResource);
                        if (IsSlotBoundary(call.Method)) prefix.SlotCandidates = FreezeSlot(call.Slot);
                        _prefixes.Add(call.CallId, prefix);
                    }
                    else
                    {
                        if (!_prefixes.TryGetValue(call.CallId, out prefix) || prefix.Method != call.Method)
                            throw new InvalidOperationException("Loot fixed prefix unavailable.");
                        if (call.Stage == LootObservationStage.After)
                        {
                            if (prefix.LocalInstancePointer != ReadInstancePointer(call))
                                throw new InvalidOperationException("Loot original instance changed.");
                            if (!_lineage.RecordPostfix(prefix.Token, call.OriginalReturn, call.OriginalIntReturn))
                                throw new InvalidOperationException("Loot postfix lineage rejected.");
                        }
                        else if (call.Stage == LootObservationStage.Finalizer)
                        {
                            // Only stored CLR identity/frame/arguments are used.
                            // The original exception object is never retained.
                            if (!_lineage.FinalizeCall(prefix.Token, call.OriginalException))
                                throw new InvalidOperationException("Loot finalizer lineage rejected.");
                            _prefixes.Remove(call.CallId);
                        }
                        else throw new InvalidOperationException("Loot stage unavailable.");
                    }
                    CopyPrefix(prefix, observed);
                    if (call.Stage != LootObservationStage.Finalizer)
                    {
                        observed.UnityFrame = Read(() => Time.frameCount);
                        if (!ReferenceEquals(call.Bag, null)) CopyBag(call.Bag, observed);
                    }
                    if (!_lineage.TryTake(out LootLineageRecord record) || record.CallId != call.CallId ||
                        record.RunId != RunId || record.MethodCode != (int)call.Method || (int)record.Stage != (int)call.Stage)
                        throw new InvalidOperationException("Loot copied lineage event mismatch.");
                    observed.Lineage = record;
                    CopySource(record.Source, observed);
                    _pending.Enqueue(observed);
                    if (!_lineage.Healthy)
                    {
                        Fail("Original loot exception or lineage fault revoked evidence.", false);
                        throw new InvalidOperationException("Loot lineage revoked.");
                    }
                }
                catch
                {
                    // Even a synchronous reentry may already have latched the
                    // fault. Never leave a partial/resource candidate on this
                    // failing event, including a previously queued finalizer.
                    observed.Resource = call.Method == LootObservationMethod.LootBoxAddImpl
                        ? LootResourceObservation.Unavailable("Resource candidate discarded after callback evidence failure.") : null;
                    observed.SlotCandidates = IsSlotBoundary(call.Method)
                        ? LootSlotObservation.Unavailable("Slot candidates discarded after callback evidence failure.") : null;
                    if (!Failed)
                    {
                        observed.ReadError = observed.MainThread ? "Loot scalar/direct-field freezing rejected." : "Unbound callback thread; native fields were not read.";
                        observed.NativeWeightAtCallback = null; observed.NativeCapacityAtCallback = null;
                        observed.NativeWeightParameterAtCallback = null; observed.NativeOverloadedThresholdAtCallback = null;
                        observed.FishCapturedStateWrapperPresent = null;
                        Fail("Loot copy failure revoked synchronous evidence.", true);
                        if (_pending.Count < MaxQueued) _pending.Enqueue(observed);
                        else Increment(ref _processDiscarded);
                    }
                    throw new InvalidOperationException("Loot diagnostic copying stopped.");
                }
                finally { _insideCapture = false; }
            }
        }

        private void Fail(string reason, bool readError)
        {
            if (readError) Increment(ref _processReadErrors);
            Volatile.Write(ref _processFailed, 1); _accepting = false;
            _lineage.Invalidate(reason); _prefixes.Clear();
        }
        public void Invalidate(string reason)
        { lock (_gate) Fail(reason, false); }
        private void CheckReadWindow()
        {
            if (Environment.CurrentManagedThreadId != _unityThreadId || !_accepting || Failed || !_lineage.Healthy)
                throw new InvalidOperationException("Loot direct read window lost.");
        }
        private T Read<T>(Func<T> read)
        { CheckReadWindow(); T value = read(); CheckReadWindow(); return value; }
        private sealed class ResourceReader
        {
            private readonly LootObservationCapture _capture;
            private int _reads;
            public ResourceReader(LootObservationCapture capture) { _capture = capture; }
            public T Read<T>(Func<T> read)
            {
                _capture.CheckReadWindow();
                if (_reads >= MaxResourceReadsPerPrefix || !ClaimResourceRead())
                    throw new InvalidOperationException("Loot resource read quota exhausted.");
                _reads++;
                return _capture.Read(read);
            }
        }
        private static bool ClaimResourceRead()
        {
            while (true)
            {
                long current = Interlocked.Read(ref _processResourceReads);
                if (current >= MaxProcessResourceReads) return false;
                if (Interlocked.CompareExchange(ref _processResourceReads, current + 1, current) == current) return true;
            }
        }
        private sealed class SlotReader
        {
            private readonly LootObservationCapture _capture;
            private int _reads;
            public SlotReader(LootObservationCapture capture) { _capture = capture; }
            public T Read<T>(Func<T> read)
            {
                _capture.CheckReadWindow();
                if (_reads >= MaxSlotReadsPerPrefix || !ClaimSlotRead())
                    throw new InvalidOperationException("Loot slot read quota exhausted.");
                _reads++;
                return _capture.Read(read);
            }
        }
        private static bool ClaimSlotRead()
        {
            while (true)
            {
                long current = Interlocked.Read(ref _processSlotReads);
                if (current >= MaxProcessSlotReads) return false;
                if (Interlocked.CompareExchange(ref _processSlotReads, current + 1, current) == current) return true;
            }
        }
        private LootSlotObservation FreezeSlot(LootBoxSlot slot)
        {
            if (ReferenceEquals(slot, null))
                return LootSlotObservation.Unavailable("Original slot argument is null.");
            var reader = new SlotReader(this);
            long pointer = reader.Read(() => slot.Pointer.ToInt64());
            if (pointer == 0) throw new InvalidOperationException("Loot slot identity unavailable.");
            IntPtr actual = reader.Read(() => IL2CPP.il2cpp_object_get_class(new IntPtr(pointer)));
            if (actual == IntPtr.Zero) throw new InvalidOperationException("Loot slot native class unavailable.");
            // Class-store initialization may perform native work. Exact class
            // checks and all direct struct reads stay inside the same window.
            IntPtr expected = reader.Read(() => Il2CppClassPointerStore<LootBoxSlot>.NativeClassPtr);
            if (expected == IntPtr.Zero || actual != expected)
                return LootSlotObservation.Unavailable("Exact slot class is outside the supported LootBoxSlot schema.");
            LootObscuredIntSnapshot itemId = reader.Read(() => CopyObscured(slot.m_ItemID));
            LootObscuredIntSnapshot grade = reader.Read(() => CopyObscured(slot.m_Grade));
            LootObscuredIntSnapshot finalGrade = reader.Read(() => CopyObscured(slot.m_FinalGrade));
            LootObscuredIntSnapshot totalCount = reader.Read(() => CopyObscured(slot.m_TotalCount));
            LootObscuredIntSnapshot itemIdAgain = reader.Read(() => CopyObscured(slot.m_ItemID));
            LootObscuredIntSnapshot gradeAgain = reader.Read(() => CopyObscured(slot.m_Grade));
            LootObscuredIntSnapshot finalGradeAgain = reader.Read(() => CopyObscured(slot.m_FinalGrade));
            LootObscuredIntSnapshot totalCountAgain = reader.Read(() => CopyObscured(slot.m_TotalCount));
            if (!SamplesEqual(itemId, itemIdAgain) || !SamplesEqual(grade, gradeAgain) ||
                !SamplesEqual(finalGrade, finalGradeAgain) || !SamplesEqual(totalCount, totalCountAgain) ||
                reader.Read(() => slot.Pointer.ToInt64()) != pointer ||
                reader.Read(() => IL2CPP.il2cpp_object_get_class(new IntPtr(pointer))) != expected ||
                reader.Read(() => Il2CppClassPointerStore<LootBoxSlot>.NativeClassPtr) != expected)
                throw new InvalidOperationException("Loot slot samples did not match.");
            // Decode only the copied CLR scalars. No getter, conversion,
            // initialization, detector query or static-key lookup is invoked.
            var observed = new LootSlotObservation(LootSlotSnapshot.DecodeInt(itemId), LootSlotSnapshot.DecodeInt(grade),
                LootSlotSnapshot.DecodeInt(finalGrade), LootSlotSnapshot.DecodeInt(totalCount));
            CheckReadWindow(); GC.KeepAlive(slot);
            return observed;
        }
        private static LootObscuredIntSnapshot CopyObscured(CodeStage.AntiCheat.ObscuredTypes.ObscuredInt value)
            => new LootObscuredIntSnapshot(value.currentCryptoKey, value.hiddenValue, value.inited,
                value.fakeValue, value.fakeValueActive);
        private static bool SamplesEqual(LootObscuredIntSnapshot first, LootObscuredIntSnapshot second)
            => first.CurrentCryptoKey == second.CurrentCryptoKey && first.HiddenValue == second.HiddenValue &&
                first.Inited == second.Inited && first.FakeValue == second.FakeValue && first.FakeValueActive == second.FakeValueActive;
        private LootResourceObservation FreezeResource(DR.IItemBase resource)
        {
            if (ReferenceEquals(resource, null))
                return LootResourceObservation.Unavailable("Original item resource argument is null.");
            var reader = new ResourceReader(this);
            long pointer = reader.Read(() => resource.Pointer.ToInt64());
            if (pointer == 0) throw new InvalidOperationException("Loot resource identity unavailable.");
            IntPtr actual = reader.Read(() => IL2CPP.il2cpp_object_get_class(new IntPtr(pointer)));
            if (actual == IntPtr.Zero) throw new InvalidOperationException("Loot resource native class unavailable.");
            // Class-store initialization and typed wrapper construction can
            // perform native work. Both stay inside the fresh guarded budget.
            IntPtr expected = reader.Read(() => Il2CppClassPointerStore<DR.Items>.NativeClassPtr);
            if (expected != IntPtr.Zero && actual == expected)
            {
                var item = reader.Read(() => new DR.Items(new IntPtr(pointer)));
                return SampleResource(reader, resource, item, pointer, expected, "DR.Items",
                    () => Il2CppClassPointerStore<DR.Items>.NativeClassPtr,
                    () => item._TID_k__BackingField, () => item._ItemDataID_k__BackingField,
                    () => item._ItemGrade_k__BackingField, () => item._ItemWeight_k__BackingField);
            }
            expected = reader.Read(() => Il2CppClassPointerStore<IntegratedItem>.NativeClassPtr);
            if (expected != IntPtr.Zero && actual == expected)
            {
                var item = reader.Read(() => new IntegratedItem(new IntPtr(pointer)));
                return SampleResource(reader, resource, item, pointer, expected, "IntegratedItem",
                    () => Il2CppClassPointerStore<IntegratedItem>.NativeClassPtr,
                    () => item._TID_k__BackingField, () => item._ItemDataID_k__BackingField,
                    () => item._ItemGrade_k__BackingField, () => item._ItemWeight_k__BackingField);
            }
            return LootResourceObservation.Unavailable("Exact item resource class is outside the supported two-class schema.");
        }
        private LootResourceObservation SampleResource(ResourceReader reader, DR.IItemBase original, Il2CppObjectBase view,
            long pointer, IntPtr expected, string kind, Func<IntPtr> classStore,
            Func<int> tidRead, Func<int> dataIdRead, Func<int> gradeRead, Func<float> weightRead)
        {
            if (reader.Read(() => view.Pointer.ToInt64()) != pointer)
                throw new InvalidOperationException("Loot typed resource identity mismatch.");
            int tid = reader.Read(tidRead), dataId = reader.Read(dataIdRead), grade = reader.Read(gradeRead);
            float weight = reader.Read(weightRead);
            if (!Finite(weight) || weight < 0)
                throw new InvalidOperationException("Loot base resource weight is invalid.");
            // Raw IDs/grade may contain sentinels. Preserve them without
            // validating a CargoProduct or adding bonus grade/weight factors.
            int tidAgain = reader.Read(tidRead), dataIdAgain = reader.Read(dataIdRead), gradeAgain = reader.Read(gradeRead);
            float weightAgain = reader.Read(weightRead);
            if (tid != tidAgain || dataId != dataIdAgain || grade != gradeAgain ||
                BitConverter.SingleToInt32Bits(weight) != BitConverter.SingleToInt32Bits(weightAgain) ||
                reader.Read(() => original.Pointer.ToInt64()) != pointer || reader.Read(() => view.Pointer.ToInt64()) != pointer ||
                reader.Read(() => IL2CPP.il2cpp_object_get_class(new IntPtr(pointer))) != expected || reader.Read(classStore) != expected)
                throw new InvalidOperationException("Loot resource samples did not match.");
            GC.KeepAlive(original); GC.KeepAlive(view);
            // Matching sequential samples do not prove an atomic read, native
            // stability, ABI, final slot quality, weight or yield completeness.
            return new LootResourceObservation(kind, tid, dataId, grade, weight);
        }
        private long ReadInstancePointer(LootObservationCallback call)
        {
            if (!ReferenceEquals(call.Fish, null)) return Read(() => call.Fish.Pointer.ToInt64());
            if (!ReferenceEquals(call.Body, null)) return Read(() => call.Body.Pointer.ToInt64());
            if (!ReferenceEquals(call.Bag, null)) return Read(() => call.Bag.Pointer.ToInt64());
            if (!ReferenceEquals(call.Storage, null)) return Read(() => call.Storage.Pointer.ToInt64());
            if (!ReferenceEquals(call.Save, null)) return Read(() => call.Save.Pointer.ToInt64());
            return 0; // static/router or Roll: only CLR presence, no identity guess.
        }
        private long FishOrdinal(long pointer)
        {
            if (_fishOrdinals.TryGetValue(pointer, out long ordinal)) return ordinal;
            if (_fishOrdinals.Count >= MaxFishOrdinals) throw new InvalidOperationException("Loot fish identity quota exhausted.");
            ordinal = _fishOrdinals.Count + 1; _fishOrdinals.Add(pointer, ordinal); return ordinal;
        }
        private LootLineageSource ResolveFish(long pointer, long ordinal)
        {
            HostEntityTarget? binding = Read(() => _fishResolver?.Invoke(pointer));
            if (!binding.HasValue) return null; // unknown fish boundary masks its parent
            HostEntityTarget target = binding.Value;
            // ResolveObservedPointer already matches the native pointer and
            // lifecycle generation. LocalToken is Unity's GetInstanceID value,
            // which is a different namespace and can legitimately be negative.
            if (target.Kind != EntityKind.Fish || target.LocalToken == 0 || target.SceneEpoch < 1 ||
                target.EntityId < 1 || target.Generation < 1 || target.DataTid < 1)
                throw new InvalidOperationException("Copied fish source fields invalid.");
            return new LootLineageSource(ordinal, target.SceneEpoch, target.EntityId, target.Generation, target.DataTid);
        }
        private void CopyBag(LootBox bag, LootCallObservation observed)
        {
            float weight = Read(() => bag._weight_k__BackingField);
            float capacity = Read(() => bag.m_WeightMax);
            float parameter = Read(() => bag._WeightParameter_k__BackingField);
            float threshold = Read(() => bag._overloadedThreshold_k__BackingField);
            if (!Finite(weight) || !Finite(capacity) || !Finite(parameter) || !Finite(threshold))
                throw new InvalidOperationException("Loot direct weight fields non-finite.");
            observed.NativeWeightAtCallback = weight; observed.NativeCapacityAtCallback = capacity;
            observed.NativeWeightParameterAtCallback = parameter; observed.NativeOverloadedThresholdAtCallback = threshold;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private void FreezeKey(string key, PrefixContext context)
        {
            if (key == null) return;
            context.KeyLength = key.Length;
            if (key.Length > MaxKeyUtf16 || !ClaimKeyUnits(key.Length))
                throw new InvalidOperationException("Loot key hashing quota exhausted.");
            // Encode exact UTF-16 code units. UTF-8 replacement would collapse
            // distinct unpaired surrogates; the private log never includes key.
            var bytes = new byte[key.Length * 2];
            for (int i = 0; i < key.Length; i++)
            { bytes[i * 2] = (byte)key[i]; bytes[i * 2 + 1] = (byte)(key[i] >> 8); }
            using (SHA256 sha = SHA256.Create()) context.KeyHash = Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
        }
        private static bool ClaimKeyUnits(int count)
        {
            while (true)
            {
                long current = Interlocked.Read(ref _processKeyUtf16);
                if (count > MaxProcessKeyUtf16 - current) return false;
                if (Interlocked.CompareExchange(ref _processKeyUtf16, current + count, current) == current) return true;
            }
        }
        private static bool IsFishBoundary(LootObservationMethod method)
            => method == LootObservationMethod.FishAddDropItem || method == LootObservationMethod.FishPickup ||
                method == LootObservationMethod.FishDropWithPlus || method == LootObservationMethod.FishDropPlus ||
                method == LootObservationMethod.FishBodySuccessInteract || method == LootObservationMethod.FishBodyCheckAvailable;
        private static bool IsSlotBoundary(LootObservationMethod method)
            => method == LootObservationMethod.IngredientsAddFromLootBox || method == LootObservationMethod.SaveDataAddLootBox;
        private LootCallObservation CopyScalars(LootObservationCallback call)
        {
            LootObservationArguments args = call.Arguments;
            return new LootCallObservation
            {
                ProcessSequence = call.ProcessSequence, RunId = RunId, CallId = call.CallId,
                OriginalMethod = MethodName(call.Method), Stage = call.Stage.ToString(), CallbackThreadId = call.ManagedThreadId,
                MainThread = call.ManagedThreadId == _unityThreadId && Environment.CurrentManagedThreadId == _unityThreadId,
                PrefixContextMatched = call.PrefixContextMatched, OriginalReturn = call.OriginalReturn,
                OriginalIntReturn = call.OriginalIntReturn, OriginalException = call.OriginalException,
                ItemId = args.ItemId, Count = args.Count, BonusGrade = args.BonusGrade, Tier = args.Tier,
                CollectionId = args.CollectionId, CollectionGrade = args.CollectionGrade, LiftType = args.LiftType,
                RollFishTid = args.RollFishTid, BagType = args.BagType,
                TargetWeight = args.TargetWeight.HasValue && Finite(args.TargetWeight.Value) ? args.TargetWeight : null,
                IgnoreOverloaded = args.IgnoreOverloaded, UpdateMissionCount = args.UpdateMissionCount, IsForce = args.IsForce, IsNew = args.IsNew,
                GetTimesArgumentPresent = args.GetTimesArgumentPresent, ExchangeCallbackArgumentPresent = args.ExchangeCallbackArgumentPresent,
                ItemDataArgumentPresent = args.ItemDataArgumentPresent, ActorArgumentPresent = args.ActorArgumentPresent,
                InstanceWrapperPresent = !ReferenceEquals(call.Fish, null) || !ReferenceEquals(call.Body, null) || !ReferenceEquals(call.Bag, null) ||
                    !ReferenceEquals(call.Storage, null) || !ReferenceEquals(call.Save, null) || args.OtherInstanceWrapperPresent,
                SlotWrapperPresent = !ReferenceEquals(call.Slot, null),
                SlotDataUnavailableReason = IsSlotBoundary(call.Method)
                    ? "Before scalar candidates only; native ABI, final quality, bag delta and full yield are not verified." : null
            };
        }
        private static void CopyPrefix(PrefixContext prefix, LootCallObservation observed)
        {
            observed.PrefixObservationAvailable = true; observed.LocalFishOrdinal = prefix.LocalFishOrdinal;
            observed.UnityFrame = prefix.UnityFrame; observed.KeyLength = prefix.KeyLength; observed.KeyHash = prefix.KeyHash;
            observed.InstanceWrapperPresent = prefix.InstancePresent; observed.SlotWrapperPresent = prefix.SlotPresent;
            observed.Resource = prefix.Resource;
            observed.SlotCandidates = prefix.SlotCandidates;
            // No IsFishCaptured/ReactiveProperty.Value getter is invoked. The
            // terminal state is deliberately unavailable in this observation.
            observed.FishCapturedStateWrapperPresent = null;
        }
        private static void CopySource(LootLineageSource source, LootCallObservation observed)
        {
            if (source == null) return;
            observed.FishIdentityResolvedAtPrefix = true; observed.LocalFishOrdinal = source.LocalFishOrdinal;
            observed.ObservedFishSceneEpoch = source.SceneEpoch; observed.ObservedFishEntityId = source.EntityId;
            observed.ObservedFishGeneration = source.Generation; observed.ObservedFishDataTid = source.DataTid;
        }
        private static string MethodName(LootObservationMethod method)
        {
            switch (method)
            {
                case LootObservationMethod.FishAddDropItem: return "DR.AI.FishAISystem.AddDropItem_Impl";
                case LootObservationMethod.LootBoxAdd: return "LootBox.Add";
                case LootObservationMethod.CaughtFishAdd: return "SaveDataCaughtFishRouter.AddCaughtFish(int,int,bool)";
                case LootObservationMethod.IngredientsAddFromLootBox: return "IngredientsStorage.AddFromLootBox";
                case LootObservationMethod.FishPickup: return "DR.AI.FishAISystem.SuccessPickupFish";
                case LootObservationMethod.FishDropWithPlus: return "DR.AI.FishAISystem.AddDropItemLootBoxWithPlus";
                case LootObservationMethod.FishDropPlus: return "DR.AI.FishAISystem.AddDropPlusItem_Impl";
                case LootObservationMethod.LootBoxAddIgnoreOverloaded: return "LootBox.AddIgnoreOverloaded";
                case LootObservationMethod.LootBoxAddImpl: return "LootBox.Add_Impl";
                case LootObservationMethod.LootBoxCheckOverloaded: return "LootBox.CheckOverloadedState";
                case LootObservationMethod.LootBoxRefreshOverweight: return "LootBox.RefreshOverweight(float)";
                case LootObservationMethod.SaveDataAddLooting: return "SaveData.AddLootingSaveData";
                case LootObservationMethod.FishBodySuccessInteract: return "FishInteractionBody.SuccessInteract";
                case LootObservationMethod.FishBodyCheckAvailable: return "FishInteractionBody.CheckAvailableInteraction";
                case LootObservationMethod.FishPlusItemRoll: return "FishPlusItemPity.RollPlusItem";
                case LootObservationMethod.SaveDataAddLootBox: return "SaveData.AddLootBox";
                default: return "Unknown";
            }
        }
        public bool TryTake(out LootCallObservation observation)
        { lock (_gate) { observation = _pending.Count == 0 ? null : _pending.Dequeue(); return observation != null; } }
        public void Stop()
        {
            lock (_gate)
            {
                _accepting = false; _lineage.Stop("Loot observer stopped; no asynchronous source inheritance.");
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
