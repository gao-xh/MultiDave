using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using DaveCoop.Core.Guest;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using IngredientDictionary = Il2CppSystem.Collections.Generic.Dictionary<int, IngredientsData>;

namespace DaveCoop.Networking
{
    // Candidate native primitives only. The backend supplies fresh bound
    // windows, keeps explicit handles and remains responsible for its fence.
    internal sealed class NativeGuestIngredientCache
    {
        public const int MaxDictionaryStorage = 4096;
        public const int MaxCounts = 16;
        public const int MaxStorageReads = 65536;
        public const int MaxStringCharacters = 512;
        public const int MaxSnapshotCharacters = 512 * 1024;
        private readonly int _thread;
        private readonly Action<Il2CppObjectBase> _keep;
        private readonly IngredientsStorage _instance;
        private readonly long _instancePointer;
        private readonly IngredientDictionary _originalStorage;
        private readonly bool _originalLoaded;
        private Snapshot _original, _preparedSnapshot;
        private IngredientDictionary _detachedStorage;
        private IngredientsData _pendingData;
        private DR.IngredientsEntity _pendingEntity;
        private Il2CppStructArray<int> _pendingCounts;
        private Il2CppObjectBase _pendingComparer;
        private bool _busy, _readingCurrent, _failed, _prepared, _prepareAttempted, _installAttempted, _restoreAttempted;
        private bool _storageInstallEntered, _storageRestoreEntered;
        private long _faultSerial, _operationSerial;

        public IngredientsStorage OriginalInstance => _instance;
        public IngredientDictionary OriginalStorage => _originalStorage;
        public IngredientDictionary DetachedStorage => _detachedStorage;
        public bool OriginalLoaded => _originalLoaded;
        public bool Prepared => _prepared;
        public bool Failed => _failed;
        public bool StorageInstallEntered => _storageInstallEntered;
        public bool StorageRestoreEntered => _storageRestoreEntered;
        public bool LoadedWriteDispatched => false;
        public bool KnownReferencesDisjoint { get; private set; }
        public bool NativeCloneAbiVerified => false;
        public bool PartialConstructorAllocationRetentionVerified => false;
        public bool EntityReadonlyVerified => false;
        public bool ResourceGraphIsolated => false;
        public bool SourceBaselineVerified => false;
        public bool CompleteGraphVerified => false;
        public bool GuestStateIsolated => false;
        public bool NativePermission => false;
        public bool WorldAuthority => false;
        public bool CargoAuthority => false;
        public string Reason { get; private set; } = "Ingredient cache has not been prepared.";

        private NativeGuestIngredientCache(IngredientsStorage instance, IngredientDictionary storage,
            bool loaded, int thread, Action<Il2CppObjectBase> keep)
        {
            _instance = instance; _instancePointer = Pointer(instance); _originalStorage = storage;
            _originalLoaded = loaded; _thread = thread; _keep = keep;
        }

        // Call before any serializer. Null storage is a valid captured absence,
        // but Prepare deliberately refuses to invent an initialized empty cache.
        public static NativeGuestIngredientCache CaptureOriginal(Action requireReadWindow, Action<Il2CppObjectBase> keep)
        {
            try
            {
                if (keep == null) Fail("Ingredient capture requires an owned-reference keeper.");
                var reader = new Reader(Environment.CurrentManagedThreadId, requireReadWindow);
                var instance = reader.Read(() => SingletonNoMono<IngredientsStorage>._s_Instance_k__BackingField);
                Pointer(instance);
                reader.Step(() => keep(instance));
                var storage = reader.Read(() => instance.m_Storage);
                bool loaded = reader.Read(() => instance.m_IsLoaded);
                if (storage == null && loaded) Fail("An ingredient cache marked loaded has no dictionary.");
                if (storage != null) reader.Step(() => keep(storage));
                var cache = new NativeGuestIngredientCache(instance, storage, loaded, Environment.CurrentManagedThreadId, keep);
                var audit = new GuestReferenceAudit();
                cache._original = reader.Graph(storage, value => Audit(audit.AddOriginal(Pointer(value)), audit));
                if (cache.Current(reader) != GuestShadowRootReadback.Original) Fail("Ingredient source changed during capture.");
                return cache;
            }
            catch (CacheFailure) { throw; }
            catch (Exception) { throw new CacheFailure("Ingredient capture could not be completed."); }
        }

        public bool Prepare(Action requirePrepareWindow)
        {
            if (!Begin(requirePrepareWindow, allowFailed: false)) return false;
            try
            {
                if (_prepareAttempted) return Reject("Ingredient preparation is single-use.");
                _prepareAttempted = true;
                var reader = OperationReader(requirePrepareWindow);
                if (Current(reader) != GuestShadowRootReadback.Original) return Reject("Ingredient preparation requires the captured original fields.");
                if (_originalStorage == null) return Reject("An absent ingredient dictionary cannot be guessed or initialized.");
                var audit = new GuestReferenceAudit();
                Snapshot current = reader.Graph(_originalStorage, value => Audit(audit.AddOriginal(Pointer(value)), audit));
                RequireUnchanged(_original, current);
                NativeGuestDictionaryComparer.RequireCloneable(current.Comparer);
                foreach (DataStamp row in current.Rows.Values)
                    if (row.Counts == null) return Reject("An ingredient count array is absent; initialization is not permitted.");
                Audit(audit.BeginDetached(), audit);

                // Returned wrappers are assigned before the post-call guard;
                // a throwing public constructor can still hide its allocation.
                var comparer = NativeGuestDictionaryComparer.Copy<int>(current.Comparer, reader.CheckWindow, value => _pendingComparer = value);
                if (reader.Read(() => OptionalPointer(_pendingComparer)) != reader.Read(() => OptionalPointer(comparer)))
                    Fail("The fresh ingredient comparer is not retained by its helper.");
                reader.Step(() => _detachedStorage = new IngredientDictionary(current.Rows.Count, comparer));
                reader.Step(() => _keep(_detachedStorage));
                reader.VerifyComparer(_detachedStorage, current.Comparer, comparer);
                foreach (KeyValuePair<int, DataStamp> entry in current.Rows)
                {
                    DataStamp row = entry.Value;
                    reader.Step(() => _pendingData = new IngredientsData(row.Id));
                    reader.Step(() => _pendingCounts = new Il2CppStructArray<int>(row.Counts.Length));
                    for (int i = 0; i < row.Counts.Length; i++)
                    {
                        int index = i;
                        reader.Step(() => _pendingCounts[index] = row.Counts[index]);
                    }
                    _pendingEntity = null;
                    if (row.Entity != null)
                    {
                        reader.Step(() => _pendingEntity = new DR.IngredientsEntity());
                        CopyEntity(reader, _pendingEntity, row.Entity);
                    }
                    CopyData(reader, _pendingData, row, _pendingCounts, _pendingEntity);
                    // Preserve the actual dictionary key, independently of Id.
                    reader.Step(() => _detachedStorage.Add(entry.Key, _pendingData));
                }
                _preparedSnapshot = reader.Graph(_detachedStorage, value => Audit(audit.AddDetached(Pointer(value)), audit));
                RequireValues(_original, _preparedSnapshot);
                NativeGuestDictionaryComparer.RequireSameSemantics(_original.Comparer, _preparedSnapshot.Comparer);
                if (!audit.KnownReferencesDisjoint) return Reject("Known ingredient references are not disjoint.");
                _prepared = true;
                if (!Validate(reader, active: false)) return false;
                KnownReferencesDisjoint = true;
                Reason = "Declared ingredient cache and Entity fields were prepared; full isolation remains unverified.";
                return !_failed;
            }
            catch (CacheFailure error) { return Reject(error.Message); }
            catch (NativeGuestDictionaryComparer.Failure error) { return Reject(error.Message); }
            catch (Exception) { return Reject("Ingredient preparation failed or its native outcome is unknown."); }
            finally { _busy = false; }
        }

        public bool ValidateKnownBinding(Action requireReadWindow) => ValidatePublic(requireReadWindow, active: false);
        public bool ValidateKnownReferences(Action requireReadWindow) => ValidatePublic(requireReadWindow, active: true);

        private bool ValidatePublic(Action window, bool active)
        {
            if (!Begin(window, allowFailed: false)) return false;
            try { return Validate(OperationReader(window), active); }
            catch (CacheFailure error) { return Reject(error.Message); }
            catch (NativeGuestDictionaryComparer.Failure error) { return Reject(error.Message); }
            catch (Exception) { return Reject("Known ingredient graph could not be read or changed."); }
            finally { _busy = false; }
        }

        private bool Validate(Reader reader, bool active)
        {
            if (!_prepared) return Reject("Ingredient cache has not been prepared.");
            GuestShadowRootReadback state = Current(reader);
            if (state == GuestShadowRootReadback.Unknown || state == GuestShadowRootReadback.Foreign ||
                (active && state != GuestShadowRootReadback.Detached)) return Reject("Ingredient cache fields are outside the owned state.");
            var audit = new GuestReferenceAudit();
            Snapshot original = reader.Graph(_originalStorage, value => Audit(audit.AddOriginal(Pointer(value)), audit));
            RequireUnchanged(_original, original);
            Audit(audit.BeginDetached(), audit);
            Snapshot detached = reader.Graph(_detachedStorage, value => Audit(audit.AddDetached(Pointer(value)), audit));
            NativeGuestDictionaryComparer.RequireSameSemantics(_original.Comparer, detached.Comparer);
            if (!active) { RequireUnchanged(_preparedSnapshot, detached); RequireValues(original, detached); }
            if (Current(reader) != state) return Reject("Ingredient fields changed during validation.");
            if (_failed || !audit.KnownReferencesDisjoint) return Reject("Known ingredient reference audit failed.");
            KnownReferencesDisjoint = true;
            return true;
        }

        // Recovery does not inspect the detached graph, clear a previous fault
        // or demand an active fence. The production window owns those policies.
        public bool ConfirmOriginalKnownGraph(Action requireOriginalReadWindow)
        {
            if (!Begin(requireOriginalReadWindow, allowFailed: true)) return false;
            try
            {
                var reader = OperationReader(requireOriginalReadWindow);
                if (Current(reader) != GuestShadowRootReadback.Original) return Reject("Original ingredient cache fields are not restored.");
                var audit = new GuestReferenceAudit();
                Snapshot original = reader.Graph(_originalStorage, value => Audit(audit.AddOriginal(Pointer(value)), audit));
                RequireUnchanged(_original, original);
                if (Current(reader) != GuestShadowRootReadback.Original) return Reject("Original ingredient fields changed during confirmation.");
                return true;
            }
            catch (Exception) { return Reject("Original known ingredient graph could not be confirmed."); }
            finally { _busy = false; }
        }

        // A separately guarded raw read may be used inside the backend's fresh
        // window. It cannot reenter itself or dispatch any graph/mutation API.
        public GuestShadowRootReadback ReadCurrent(Action requireReadWindow)
        {
            if (_readingCurrent || Environment.CurrentManagedThreadId != _thread || requireReadWindow == null)
            { Reject("Ingredient readback was reentrant or on the wrong thread."); return GuestShadowRootReadback.Unknown; }
            _readingCurrent = true;
            try
            {
                long serial = Interlocked.Read(ref _faultSerial);
                GuestShadowRootReadback state = Current(new Reader(_thread, requireReadWindow,
                    () => Interlocked.Read(ref _faultSerial), serial));
                if (state == GuestShadowRootReadback.Unknown || state == GuestShadowRootReadback.Foreign)
                    Reject("Ingredient current fields are unknown or foreign.");
                return state;
            }
            catch (Exception) { Reject("Ingredient field identity could not be read."); return GuestShadowRootReadback.Unknown; }
            finally { _readingCurrent = false; }
        }

        private GuestShadowRootReadback Current(Reader reader)
        {
            var instance = reader.Read(() => SingletonNoMono<IngredientsStorage>._s_Instance_k__BackingField);
            if (OptionalPointer(instance) != _instancePointer) return GuestShadowRootReadback.Foreign;
            var storage = reader.Read(() => _instance.m_Storage);
            bool loaded = reader.Read(() => _instance.m_IsLoaded);
            long pointer = OptionalPointer(storage);
            if (pointer != OptionalPointer(reader.Read(() => _instance.m_Storage)) || loaded != reader.Read(() => _instance.m_IsLoaded) ||
                OptionalPointer(reader.Read(() => SingletonNoMono<IngredientsStorage>._s_Instance_k__BackingField)) != _instancePointer)
                return GuestShadowRootReadback.Unknown;
            bool originalStorage = pointer == OptionalPointer(_originalStorage);
            bool detachedStorage = _prepared && pointer == OptionalPointer(_detachedStorage);
            // Current policy preserves the original loaded value. Do not turn
            // an opposite bool into proof of a new detached life or force true.
            bool originalLoaded = loaded == _originalLoaded;
            bool detachedLoaded = _prepared && loaded == _originalLoaded;
            if (originalStorage && originalLoaded) return GuestShadowRootReadback.Original;
            if (detachedStorage && detachedLoaded) return GuestShadowRootReadback.Detached;
            if ((originalStorage || detachedStorage) && (originalLoaded || detachedLoaded)) return GuestShadowRootReadback.OwnedMixed;
            return GuestShadowRootReadback.Foreign;
        }

        public bool Install(Action strictWriteWindow)
        {
            if (!Begin(strictWriteWindow, allowFailed: false)) return false;
            try
            {
                if (_installAttempted || !_prepared) return Reject("Ingredient installation is unavailable or already attempted.");
                _installAttempted = true;
                var reader = OperationReader(strictWriteWindow);
                if (Current(reader) != GuestShadowRootReadback.Original || !Validate(reader, active: false)) return Reject("Ingredient installation lost its original binding.");
                reader.Step(() => { _storageInstallEntered = true; _instance.m_Storage = _detachedStorage; });
                if (Current(reader) != GuestShadowRootReadback.Detached) return Reject("Ingredient storage installation readback is incomplete.");
                // Expected loaded values are equal, so no meaningless setter is
                // dispatched. It is still independently freshly read/checked.
                if (reader.Read(() => _instance.m_IsLoaded) != _originalLoaded) return Reject("Ingredient loaded field changed during installation.");
                if (Current(reader) != GuestShadowRootReadback.Detached || _failed) return Reject("Ingredient installation could not be confirmed.");
                return true;
            }
            catch (Exception) { return Reject("Ingredient installation failed or its write outcome is unknown."); }
            finally { _busy = false; }
        }

        public bool Restore(Action restoreWindow)
        {
            if (!Begin(restoreWindow, allowFailed: true)) return false;
            try
            {
                var reader = OperationReader(restoreWindow);
                GuestShadowRootReadback state = Current(reader);
                if (state == GuestShadowRootReadback.Original) return true;
                if (state != GuestShadowRootReadback.Detached && state != GuestShadowRootReadback.OwnedMixed)
                    return Reject("Unknown or foreign ingredient fields will not be overwritten.");
                if (_restoreAttempted) return Reject("An entered ingredient restoration cannot be redispatched.");
                _restoreAttempted = true;
                // Reverse order: loaded then storage. In the current preservation
                // policy loaded is already original, so it is checked, not set.
                if (reader.Read(() => _instance.m_IsLoaded) != _originalLoaded) return Reject("Ingredient loaded field is not owned.");
                if (Current(reader) != state) return Reject("Ingredient fields changed before restoration.");
                reader.Step(() => { _storageRestoreEntered = true; _instance.m_Storage = _originalStorage; });
                if (Current(reader) != GuestShadowRootReadback.Original) return Reject("Ingredient restoration outcome remains unknown.");
                return true;
            }
            catch (Exception) { return Reject("Ingredient restoration failed or its write outcome is unknown; do not retry."); }
            finally { _busy = false; }
        }

        private bool Begin(Action window, bool allowFailed)
        {
            if (_busy || _readingCurrent || Environment.CurrentManagedThreadId != _thread || window == null)
                return Reject("Ingredient operation was reentrant, unbound or on the wrong thread.");
            long serial = Interlocked.Read(ref _faultSerial);
            if (!allowFailed && (_failed || serial != 0)) return false;
            _operationSerial = serial;
            _busy = true; return true;
        }

        private Reader OperationReader(Action window) => new Reader(_thread, window,
            () => Interlocked.Read(ref _faultSerial), _operationSerial);

        private bool Reject(string reason)
        {
            Interlocked.Increment(ref _faultSerial);
            _failed = true; KnownReferencesDisjoint = false; Reason = reason; return false;
        }
        private static long OptionalPointer(Il2CppObjectBase value) => value == null ? 0 : Pointer(value);
        private static long Pointer(Il2CppObjectBase value)
        {
            if (value == null || value.Pointer == IntPtr.Zero) Fail("A required ingredient reference is missing.");
            return value.Pointer.ToInt64();
        }
        private static void Audit(bool accepted, GuestReferenceAudit audit)
        { if (!accepted) Fail("Known ingredient reference audit rejected: " + audit.Reason + "."); }
        private static void Fail(string reason) => throw new CacheFailure(reason);
        private sealed class CacheFailure : InvalidOperationException { public CacheFailure(string reason) : base(reason) { } }
        private static void RequireValues(Snapshot before, Snapshot now)
        { if (!string.Equals(before.Values, now.Values, StringComparison.Ordinal)) Fail("Known ingredient values differ."); }
        private static void RequireUnchanged(Snapshot before, Snapshot now)
        {
            RequireValues(before, now);
            if (!string.Equals(before.Identity, now.Identity, StringComparison.Ordinal)) Fail("Known ingredient references or versions changed.");
        }

        private sealed class Snapshot
        {
            public string Values, Identity;
            public NativeGuestDictionaryComparer.Stamp Comparer;
            public SortedDictionary<int, DataStamp> Rows = new SortedDictionary<int, DataStamp>();
        }
        private sealed class DataStamp
        {
            public int Id, Level, ParentId, Rank, PlaceTag;
            public IngredientsType Type;
            public bool IsNew;
            public Il2CppSystem.DateTime GainTime, GainGameTime;
            public int[] Counts;
            public EntityStamp Entity;
        }
        private sealed class EntityStamp
        {
            public int ItemsTid, Tid, Type, TidConnect;
            public string Name, Description, Color, Thumbnail;
            public bool IsUse, NotUseParent, Deliverable, Compoundable;
            public DR.IngredientsCategoryType Category;
        }

        private static void CopyData(Reader r, IngredientsData target, DataStamp value, Il2CppStructArray<int> counts, DR.IngredientsEntity entity)
        {
            r.Step(() => target.ingredientsID = value.Id); r.Step(() => target.level = value.Level);
            r.Step(() => target.parentID = value.ParentId); r.Step(() => target.rank = value.Rank);
            r.Step(() => target.type = value.Type); r.Step(() => target.counts = counts);
            r.Step(() => target.isNew = value.IsNew); r.Step(() => target.lastGainTime = value.GainTime);
            r.Step(() => target.lastGainGameTime = value.GainGameTime); r.Step(() => target.placeTagMask = value.PlaceTag);
            r.Step(() => target._Entity_k__BackingField = entity);
        }
        private static void CopyEntity(Reader r, DR.IngredientsEntity target, EntityStamp value)
        {
            r.Step(() => target._ItemsTID_k__BackingField = value.ItemsTid); r.Step(() => target._TID_k__BackingField = value.Tid);
            r.Step(() => target._Type_k__BackingField = value.Type); r.Step(() => target._NameID_k__BackingField = value.Name);
            r.Step(() => target._DescriptionID_k__BackingField = value.Description); r.Step(() => target._IsUse_k__BackingField = value.IsUse);
            r.Step(() => target._NotUseParentTID_k__BackingField = value.NotUseParent);
            r.Step(() => target._TIDNumberConnect_k__BackingField = value.TidConnect);
            r.Step(() => target._DeliverableToBranch_k__BackingField = value.Deliverable);
            r.Step(() => target._MaterialColor_k__BackingField = value.Color); r.Step(() => target._IsCompoundable_k__BackingField = value.Compoundable);
            r.Step(() => target._ContentsThumbnail_k__BackingField = value.Thumbnail); r.Step(() => target._CategoryType_k__BackingField = value.Category);
        }

        private sealed class Reader
        {
            private readonly int _thread;
            private readonly Action _window;
            private readonly Func<long> _faultSerial;
            private readonly long _expectedSerial;
            private readonly StringBuilder _identity = new StringBuilder();
            private Action<Il2CppObjectBase> _audit;
            private int _storageReads, _characters;
            public Reader(int thread, Action window, Func<long> faultSerial = null, long expectedSerial = 0)
            {
                _thread = thread; _window = window ?? throw new ArgumentNullException(nameof(window));
                _faultSerial = faultSerial; _expectedSerial = expectedSerial;
            }
            private void Check()
            {
                if (_faultSerial != null && _faultSerial() != _expectedSerial) Fail("Ingredient operation acquired a new fault.");
                if (Environment.CurrentManagedThreadId != _thread) Fail("Ingredient access requires its captured Unity thread.");
                _window();
                if (Environment.CurrentManagedThreadId != _thread) Fail("Ingredient access thread changed.");
                if (_faultSerial != null && _faultSerial() != _expectedSerial) Fail("Ingredient operation acquired a new fault.");
            }
            public T Read<T>(Func<T> read) { Check(); T value = read(); Check(); return value; }
            public void Step(Action action) { Check(); action(); Check(); }
            public void CheckWindow() => Check();
            public void VerifyComparer(IngredientDictionary dictionary, NativeGuestDictionaryComparer.Stamp source,
                Il2CppSystem.Collections.Generic.IEqualityComparer<int> expected)
            {
                var actual = NativeGuestDictionaryComparer.Capture(Read(() => dictionary._comparer), Check);
                var supplied = NativeGuestDictionaryComparer.Capture(expected, Check);
                NativeGuestDictionaryComparer.RequireSameIdentity(supplied, actual);
                NativeGuestDictionaryComparer.RequireSameSemantics(source, actual);
            }
            private void DictionaryAuxiliaryEmpty(IngredientDictionary dictionary)
            {
                if (Read(() => dictionary._keys) != null || Read(() => dictionary._values) != null || Read(() => dictionary._syncRoot) != null)
                    Fail("Missing proof for an ingredient dictionary view or synchronization reference.");
                _identity.Append("no-dictionary-view-or-sync;");
            }
            private T Slot<T>(Func<T> read)
            {
                if (_storageReads >= MaxStorageReads) Fail("Ingredient storage read budget exceeded.");
                _storageReads++; return Read(read);
            }
            private void Reference(Il2CppObjectBase value) { Check(); long pointer = Pointer(value); _audit(value); _identity.Append(Number(pointer)); Check(); }
            private void Storage(Il2CppObjectBase value, int length, int limit)
            {
                if (length < 0 || length > limit) Fail("Ingredient storage length exceeds its bound.");
                if (value != null && length != 0) Reference(value);
            }
            private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture) + ";";
            private string Text(string value)
            {
                if (value == null) return "-1:";
                if (value.Length > MaxStringCharacters) Fail("An ingredient resource string exceeds its bound.");
                _characters += value.Length;
                if (_characters > MaxSnapshotCharacters) Fail("Ingredient snapshot exceeds its bound.");
                return value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
            }

            public Snapshot Graph(IngredientDictionary dictionary, Action<Il2CppObjectBase> audit)
            {
                _audit = audit; _identity.Clear();
                var result = new Snapshot();
                if (dictionary == null) { result.Values = "absent"; result.Identity = "0;"; return result; }
                IntPtr expectedClass = Read(() => Il2CppClassPointerStore<IngredientDictionary>.NativeClassPtr);
                if (expectedClass == IntPtr.Zero || Read(() => IL2CPP.il2cpp_object_get_class(new IntPtr(Pointer(dictionary)))) != expectedClass ||
                    expectedClass != Read(() => Il2CppClassPointerStore<IngredientDictionary>.NativeClassPtr))
                    Fail("Missing proof for an exact ingredient dictionary class.");
                _identity.Append(Number(expectedClass.ToInt64()));
                Reference(dictionary);
                DictionaryAuxiliaryEmpty(dictionary);
                result.Comparer = NativeGuestDictionaryComparer.Capture(Read(() => dictionary._comparer), Check);
                if (result.Comparer.Reference != null) Reference(result.Comparer.Reference);
                _identity.Append(Number((int)result.Comparer.ClassKind)).Append(Number(result.Comparer.NativeClass.ToInt64())).Append(Number(result.Comparer.Pointer)).Append(Number(result.Comparer.DefaultComparerPointer));
                int count = Read(() => dictionary._count), free = Read(() => dictionary._freeCount), version = Read(() => dictionary._version);
                int freeList = Read(() => dictionary._freeList);
                var entries = Read(() => dictionary._entries); var buckets = Read(() => dictionary._buckets);
                int length = entries == null ? 0 : Read(() => entries.Length);
                int bucketLength = buckets == null ? 0 : Read(() => buckets.Length);
                Storage(entries, length, MaxDictionaryStorage); Storage(buckets, bucketLength, MaxDictionaryStorage);
                if (count < 0 || count > length || free < 0 || free > count || (free > 0 && (freeList < 0 || freeList >= count)) ||
                    (length == 0) != (bucketLength == 0) || (count > 0 && bucketLength == 0)) Fail("Ingredient dictionary shape is invalid.");
                _identity.Append(Number(OptionalPointer(entries))).Append(Number(OptionalPointer(buckets))).Append(Number(count))
                    .Append(Number(free)).Append(Number(freeList)).Append(Number(version)).Append(Number(length)).Append(Number(bucketLength));
                for (int i = 0; i < bucketLength; i++)
                {
                    int slot = Slot(() => buckets[i]);
                    if (slot < -1 || slot > count) Fail("An ingredient dictionary bucket is invalid.");
                    _identity.Append(Number(slot));
                }
                for (int i = 0; i < length; i++)
                {
                    // Entry is a native ValueType wrapper. Array access can
                    // allocate native boxes; the temporary box is not graph ownership.
                    var entry = Slot(() => entries[i]);
                    if (entry == null) Fail("An ingredient dictionary entry could not be read.");
                    int hash = Read(() => entry.hashCode), next = Read(() => entry.next), key = Read(() => entry.key);
                    var value = Read(() => entry.value);
                    _identity.Append(Number(hash)).Append(Number(next)).Append(Number(key));
                    if (i >= count || hash < 0)
                    {
                        if (value != null) Fail("An unused ingredient dictionary slot retains a mutable reference.");
                        continue;
                    }
                    if (next < -1 || next >= count || result.Rows.ContainsKey(key)) Fail("An ingredient entry is invalid or ambiguous.");
                    result.Rows.Add(key, Data(value));
                }
                if (result.Rows.Count != count - free) Fail("Ingredient dictionary live count differs.");
                if (count != Read(() => dictionary._count) || free != Read(() => dictionary._freeCount) || version != Read(() => dictionary._version) ||
                    freeList != Read(() => dictionary._freeList) || OptionalPointer(entries) != OptionalPointer(Read(() => dictionary._entries)) ||
                    OptionalPointer(buckets) != OptionalPointer(Read(() => dictionary._buckets))) Fail("Ingredient dictionary changed during capture.");
                DictionaryAuxiliaryEmpty(dictionary);
                NativeGuestDictionaryComparer.RequireSameIdentity(result.Comparer, NativeGuestDictionaryComparer.Capture(Read(() => dictionary._comparer), Check));
                var values = new StringBuilder();
                foreach (KeyValuePair<int, DataStamp> entry in result.Rows) values.Append(Number(entry.Key)).Append(Encode(entry.Value));
                result.Values = values.ToString(); result.Identity = _identity.ToString(); return result;
            }

            private DataStamp Data(IngredientsData value)
            {
                Reference(value);
                var stamp = new DataStamp
                {
                    Id = Read(() => value.ingredientsID), Level = Read(() => value.level), ParentId = Read(() => value.parentID),
                    Rank = Read(() => value.rank), Type = Read(() => value.type), IsNew = Read(() => value.isNew),
                    GainTime = Read(() => value.lastGainTime), GainGameTime = Read(() => value.lastGainGameTime), PlaceTag = Read(() => value.placeTagMask)
                };
                var counts = Read(() => value.counts);
                int length = counts == null ? 0 : Read(() => counts.Length);
                Storage(counts, length, MaxCounts);
                _identity.Append(Number(OptionalPointer(counts))).Append(Number(length));
                if (counts != null)
                {
                    stamp.Counts = new int[length];
                    for (int i = 0; i < length; i++) stamp.Counts[i] = Slot(() => counts[i]);
                }
                var entity = Read(() => value._Entity_k__BackingField);
                stamp.Entity = entity == null ? null : Entity(entity);
                _identity.Append(Number(OptionalPointer(entity)));
                if (OptionalPointer(counts) != OptionalPointer(Read(() => value.counts)) || OptionalPointer(entity) != OptionalPointer(Read(() => value._Entity_k__BackingField)))
                    Fail("Ingredient child binding changed during capture.");
                return stamp;
            }

            private EntityStamp Entity(DR.IngredientsEntity value)
            {
                Reference(value);
                return new EntityStamp
                {
                    ItemsTid = Read(() => value._ItemsTID_k__BackingField), Tid = Read(() => value._TID_k__BackingField),
                    Type = Read(() => value._Type_k__BackingField), Name = Read(() => value._NameID_k__BackingField),
                    Description = Read(() => value._DescriptionID_k__BackingField), IsUse = Read(() => value._IsUse_k__BackingField),
                    NotUseParent = Read(() => value._NotUseParentTID_k__BackingField), TidConnect = Read(() => value._TIDNumberConnect_k__BackingField),
                    Deliverable = Read(() => value._DeliverableToBranch_k__BackingField), Color = Read(() => value._MaterialColor_k__BackingField),
                    Compoundable = Read(() => value._IsCompoundable_k__BackingField), Thumbnail = Read(() => value._ContentsThumbnail_k__BackingField),
                    Category = Read(() => value._CategoryType_k__BackingField)
                };
            }

            private string Encode(DataStamp value)
            {
                var output = new StringBuilder().Append(Number(value.Id)).Append(Number(value.Level)).Append(Number(value.ParentId)).Append(Number(value.Rank))
                    .Append(Number((int)value.Type)).Append(Number(value.IsNew ? 1 : 0)).Append(Number(value.PlaceTag))
                    .Append(value.GainTime._dateData.ToString(CultureInfo.InvariantCulture)).Append(';')
                    .Append(value.GainGameTime._dateData.ToString(CultureInfo.InvariantCulture)).Append(';');
                output.Append(Number(value.Counts == null ? -1 : value.Counts.Length));
                if (value.Counts != null) foreach (int count in value.Counts) output.Append(Number(count));
                EntityStamp entity = value.Entity;
                if (entity == null) output.Append("no-entity;");
                else output.Append("entity;").Append(Number(entity.ItemsTid)).Append(Number(entity.Tid)).Append(Number(entity.Type)).Append(Number(entity.TidConnect))
                    .Append(Text(entity.Name)).Append(Text(entity.Description)).Append(Text(entity.Color)).Append(Text(entity.Thumbnail))
                    .Append(Number(entity.IsUse ? 1 : 0)).Append(Number(entity.NotUseParent ? 1 : 0)).Append(Number(entity.Deliverable ? 1 : 0))
                    .Append(Number(entity.Compoundable ? 1 : 0)).Append(Number((int)entity.Category));
                return output.ToString();
            }
        }
    }
}
