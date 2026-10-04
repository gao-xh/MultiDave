using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using DaveCoop.Core.Guest;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using IngameCacheDictionary = Il2CppSystem.Collections.Generic.Dictionary<InGameSaveType, InGameSaveData>;

namespace DaveCoop.Networking
{
    // Known ordinary records only. Unity resources/live devices are rejected,
    // not shared or replaced with an empty table. Native entry remains disabled.
    internal sealed class NativeGuestIngameCache
    {
        public const int MaxContainerStorage = 1024;
        public const int MaxStorageReads = 65536;
        public const int MaxStringCharacters = 512;
        public const int MaxSnapshotCharacters = 512 * 1024;
        private readonly int _thread;
        private readonly Action<Il2CppObjectBase> _keep;
        private readonly IngameSaveDataManager _instance;
        private readonly long _instancePointer;
        private readonly IngameCacheDictionary _originalStorage;
        private IngameCacheDictionary _detachedStorage;
        private Snapshot _original, _preparedSnapshot;
        // Framework wrappers hold temporary native children even if a post-call
        // guard fails before they are attached to the detached dictionary.
        private readonly List<Il2CppObjectBase> _temporaries = new List<Il2CppObjectBase>();
        private bool _busy, _readingCurrent, _failed, _prepared, _prepareAttempted, _installAttempted, _restoreAttempted;
        private bool _storageInstallEntered, _storageRestoreEntered;
        private long _faultSerial, _operationSerial;
        public IngameSaveDataManager OriginalInstance => _instance;
        public IngameCacheDictionary OriginalStorage => _originalStorage;
        public IngameCacheDictionary DetachedStorage => _detachedStorage;
        public bool Prepared => _prepared;
        public bool Failed => _failed;
        public bool StorageInstallEntered => _storageInstallEntered;
        public bool StorageRestoreEntered => _storageRestoreEntered;
        public bool KnownReferencesDisjoint { get; private set; }
        public bool NativeAllocationAbiVerified => false;
        public bool NativeCloneAbiVerified => false;
        public bool PartialConstructorAllocationRetentionVerified => false;
        public bool ResourceGraphIsolated => false;
        public bool SourceBaselineVerified => false;
        public bool CompleteGraphVerified => false;
        public bool GuestStateIsolated => false;
        public bool NativePermission => false;
        public bool WorldAuthority => false;
        public bool CargoAuthority => false;
        public string Reason { get; private set; } = "Ingame cache has not been prepared.";

        private NativeGuestIngameCache(IngameSaveDataManager instance, IngameCacheDictionary storage,
            int thread, Action<Il2CppObjectBase> keep)
        { _instance = instance; _instancePointer = Pointer(instance); _originalStorage = storage; _thread = thread; _keep = keep; }

        public static NativeGuestIngameCache CaptureOriginal(Action nativeWindow, Action<Il2CppObjectBase> keep)
        {
            try
            {
                if (keep == null) Fail("Ingame capture requires an owned-reference keeper.");
                var reader = new Reader(Environment.CurrentManagedThreadId, nativeWindow);
                var instance = reader.Read(() => SingletonNoMono<IngameSaveDataManager>._s_Instance_k__BackingField);
                reader.Exact<IngameSaveDataManager>(instance);
                reader.Step(() => keep(instance));
                var storage = reader.Read(() => instance.ingameSaveDatas);
                if (storage != null) reader.Step(() => keep(storage));
                var cache = new NativeGuestIngameCache(instance, storage, Environment.CurrentManagedThreadId, keep);
                var audit = new GuestReferenceAudit();
                cache._original = reader.Graph(storage, value => Audit(audit.AddOriginal(Pointer(value)), audit));
                if (cache.Current(reader) != GuestShadowRootReadback.Original) Fail("Ingame source changed during capture.");
                return cache;
            }
            catch (CacheFailure) { throw; }
            catch (Exception) { throw new CacheFailure("Ingame known graph capture could not be completed."); }
        }

        public bool Prepare(Action nativeWindow)
        {
            if (!Begin(nativeWindow, false)) return false;
            try
            {
                if (_prepareAttempted) return Reject("Ingame preparation is single-use.");
                _prepareAttempted = true;
                var reader = OperationReader(nativeWindow);
                if (Current(reader) != GuestShadowRootReadback.Original) return Reject("Ingame preparation lost its original field.");
                if (_originalStorage == null) return Reject("An absent ingame dictionary cannot be guessed or initialized.");
                var audit = new GuestReferenceAudit();
                Snapshot original = reader.Graph(_originalStorage, value => Audit(audit.AddOriginal(Pointer(value)), audit));
                RequireUnchanged(_original, original);
                RequireCloneableComparers(original);
                // Graph rejects every unsupported resource/live device before
                // any explicit preparation allocation, including nonempty input.
                Audit(audit.BeginDetached(), audit);
                var comparer = NativeGuestDictionaryComparer.Copy<InGameSaveType>(original.Comparer, reader.CheckWindow, value => Hold(value));
                reader.Step(() => _detachedStorage = new IngameCacheDictionary(original.Rows.Count, comparer));
                reader.Step(() => _keep(_detachedStorage));
                reader.VerifyComparer(_detachedStorage, original.Comparer, comparer);
                foreach (KeyValuePair<InGameSaveType, Record> row in original.Rows)
                {
                    InGameSaveData data = CloneRecord(reader, row.Key, row.Value);
                    reader.Step(() => _detachedStorage.Add(row.Key, data));
                }
                _preparedSnapshot = reader.Graph(_detachedStorage, value => Audit(audit.AddDetached(Pointer(value)), audit));
                RequireValues(_original, _preparedSnapshot);
                RequireComparerSemantics(_original, _preparedSnapshot);
                if (!audit.KnownReferencesDisjoint) return Reject("Known ingame references are not disjoint.");
                _prepared = true;
                if (!Validate(reader, false)) return false;
                Reason = "Supported ingame records were copied; native initialization and full isolation remain unverified.";
                return !_failed;
            }
            catch (CacheFailure error) { return Reject(error.Message); }
            catch (NativeGuestDictionaryComparer.Failure error) { return Reject(error.Message); }
            catch (Exception) { return Reject("Ingame preparation failed, is unsupported or has an unknown native outcome."); }
            finally { _busy = false; }
        }

        public bool ValidateKnownBinding(Action nativeWindow) => ValidatePublic(nativeWindow, false);
        public bool ValidateKnownReferences(Action nativeWindow) => ValidatePublic(nativeWindow, true);
        private bool ValidatePublic(Action window, bool active)
        {
            if (!Begin(window, false)) return false;
            try { return Validate(OperationReader(window), active); }
            catch (CacheFailure error) { return Reject(error.Message); }
            catch (NativeGuestDictionaryComparer.Failure error) { return Reject(error.Message); }
            catch (Exception) { return Reject("Known ingame graph changed or could not be read."); }
            finally { _busy = false; }
        }
        private bool Validate(Reader reader, bool active)
        {
            if (!_prepared) return Reject("Ingame cache has not been prepared.");
            GuestShadowRootReadback state = Current(reader);
            if (state != GuestShadowRootReadback.Original && state != GuestShadowRootReadback.Detached ||
                active && state != GuestShadowRootReadback.Detached) return Reject("Ingame dictionary is outside its owned state.");
            var audit = new GuestReferenceAudit();
            Snapshot original = reader.Graph(_originalStorage, value => Audit(audit.AddOriginal(Pointer(value)), audit));
            RequireUnchanged(_original, original);
            Audit(audit.BeginDetached(), audit);
            Snapshot detached = reader.Graph(_detachedStorage, value => Audit(audit.AddDetached(Pointer(value)), audit));
            RequireComparerSemantics(_original, detached);
            if (!active) { RequireUnchanged(_preparedSnapshot, detached); RequireValues(original, detached); }
            if (Current(reader) != state || _failed || !audit.KnownReferencesDisjoint) return Reject("Ingame binding or reference audit failed.");
            KnownReferencesDisjoint = true; return true;
        }

        public bool ConfirmOriginalKnownGraph(Action nativeWindow)
        {
            if (!Begin(nativeWindow, true)) return false;
            try
            {
                var reader = OperationReader(nativeWindow);
                if (Current(reader) != GuestShadowRootReadback.Original) return Reject("Original ingame dictionary is not restored.");
                var audit = new GuestReferenceAudit();
                RequireUnchanged(_original, reader.Graph(_originalStorage, value => Audit(audit.AddOriginal(Pointer(value)), audit)));
                if (Current(reader) != GuestShadowRootReadback.Original) return Reject("Original ingame field changed during confirmation.");
                return true;
            }
            catch (Exception) { return Reject("Original known ingame graph could not be confirmed."); }
            finally { _busy = false; }
        }

        public GuestShadowRootReadback ReadCurrent(Action nativeWindow)
        {
            if (_readingCurrent || Environment.CurrentManagedThreadId != _thread || nativeWindow == null)
            { Reject("Ingame readback was reentrant or on the wrong thread."); return GuestShadowRootReadback.Unknown; }
            _readingCurrent = true;
            try
            {
                long serial = Interlocked.Read(ref _faultSerial);
                var state = Current(new Reader(_thread, nativeWindow, () => Interlocked.Read(ref _faultSerial), serial));
                if (state == GuestShadowRootReadback.Unknown || state == GuestShadowRootReadback.Foreign) Reject("Ingame field is unknown or foreign.");
                return state;
            }
            catch (Exception) { Reject("Ingame field identity could not be read."); return GuestShadowRootReadback.Unknown; }
            finally { _readingCurrent = false; }
        }
        private GuestShadowRootReadback Current(Reader reader)
        {
            var instance = reader.Read(() => SingletonNoMono<IngameSaveDataManager>._s_Instance_k__BackingField);
            if (OptionalPointer(instance) != _instancePointer) return GuestShadowRootReadback.Foreign;
            long storage = OptionalPointer(reader.Read(() => _instance.ingameSaveDatas));
            if (storage != OptionalPointer(reader.Read(() => _instance.ingameSaveDatas)) ||
                OptionalPointer(reader.Read(() => SingletonNoMono<IngameSaveDataManager>._s_Instance_k__BackingField)) != _instancePointer)
                return GuestShadowRootReadback.Unknown;
            if (storage == OptionalPointer(_originalStorage)) return GuestShadowRootReadback.Original;
            if (_prepared && storage == OptionalPointer(_detachedStorage)) return GuestShadowRootReadback.Detached;
            return GuestShadowRootReadback.Foreign;
        }

        public bool Install(Action nativeWindow)
        {
            if (!Begin(nativeWindow, false)) return false;
            try
            {
                if (_installAttempted || !_prepared) return Reject("Ingame installation is unavailable or already attempted.");
                _installAttempted = true;
                var reader = OperationReader(nativeWindow);
                if (Current(reader) != GuestShadowRootReadback.Original || !Validate(reader, false)) return Reject("Ingame installation lost its original binding.");
                reader.Step(() => { _storageInstallEntered = true; _instance.ingameSaveDatas = _detachedStorage; });
                if (Current(reader) != GuestShadowRootReadback.Detached || _failed) return Reject("Ingame installation outcome is unknown.");
                return true;
            }
            catch (Exception) { return Reject("Ingame installation failed or its write outcome is unknown."); }
            finally { _busy = false; }
        }
        public bool Restore(Action nativeWindow)
        {
            if (!Begin(nativeWindow, true)) return false;
            try
            {
                var reader = OperationReader(nativeWindow);
                var state = Current(reader);
                if (state == GuestShadowRootReadback.Original) return true;
                if (state != GuestShadowRootReadback.Detached) return Reject("Unknown or foreign ingame dictionaries will not be overwritten.");
                if (_restoreAttempted) return Reject("An ingame restoration cannot be redispatched.");
                _restoreAttempted = true;
                reader.Step(() => { _storageRestoreEntered = true; _instance.ingameSaveDatas = _originalStorage; });
                if (Current(reader) != GuestShadowRootReadback.Original) return Reject("Ingame restoration outcome remains unknown.");
                return true;
            }
            catch (Exception) { return Reject("Ingame restoration failed or is unknown; do not retry."); }
            finally { _busy = false; }
        }
        private bool Begin(Action window, bool allowFailed)
        {
            if (_busy || _readingCurrent || Environment.CurrentManagedThreadId != _thread || window == null)
                return Reject("Ingame operation was reentrant, unbound or on the wrong thread.");
            long serial = Interlocked.Read(ref _faultSerial);
            if (!allowFailed && (_failed || serial != 0)) return false;
            _operationSerial = serial; _busy = true; return true;
        }
        private Reader OperationReader(Action window) => new Reader(_thread, window, () => Interlocked.Read(ref _faultSerial), _operationSerial);
        private bool Reject(string reason) { Interlocked.Increment(ref _faultSerial); _failed = true; KnownReferencesDisjoint = false; Reason = reason; return false; }
        private static long OptionalPointer(Il2CppObjectBase value) => value == null ? 0 : Pointer(value);
        private static long Pointer(Il2CppObjectBase value)
        { if (value == null || value.Pointer == IntPtr.Zero) Fail("A required ingame reference is missing."); return value.Pointer.ToInt64(); }
        private static void Audit(bool accepted, GuestReferenceAudit audit)
        { if (!accepted) Fail("Known ingame reference audit rejected: " + audit.Reason + "."); }
        private static void Fail(string reason) => throw new CacheFailure(reason);
        private sealed class CacheFailure : InvalidOperationException { public CacheFailure(string reason) : base(reason) { } }
        private static void RequireValues(Snapshot before, Snapshot now)
        { if (!string.Equals(before.Values, now.Values, StringComparison.Ordinal)) Fail("Known ingame values differ."); }
        private static void RequireUnchanged(Snapshot before, Snapshot now)
        { RequireValues(before, now); if (!string.Equals(before.Identity, now.Identity, StringComparison.Ordinal)) Fail("Known ingame references or versions changed."); }
        private static void RequireCloneableComparers(Snapshot snapshot)
        {
            NativeGuestDictionaryComparer.RequireCloneable(snapshot.Comparer);
            foreach (Record row in snapshot.Rows.Values)
            {
                if (row.Puzzles != null) NativeGuestDictionaryComparer.RequireCloneable(row.PuzzleComparer);
                if (row.Objects != null) NativeGuestDictionaryComparer.RequireCloneable(row.ObjectComparer);
            }
        }
        private static void RequireComparerSemantics(Snapshot before, Snapshot after)
        {
            NativeGuestDictionaryComparer.RequireSameSemantics(before.Comparer, after.Comparer);
            foreach (var item in after.Rows)
            {
                Record original;
                if (item.Value.Puzzles != null)
                {
                    if (!before.Rows.TryGetValue(item.Key, out original)) Fail("An active puzzle comparer has no captured source semantics.");
                    NativeGuestDictionaryComparer.RequireSameSemantics(original.PuzzleComparer, item.Value.PuzzleComparer);
                }
                if (item.Value.Objects != null)
                {
                    if (!before.Rows.TryGetValue(item.Key, out original)) Fail("An active object comparer has no captured source semantics.");
                    NativeGuestDictionaryComparer.RequireSameSemantics(original.ObjectComparer, item.Value.ObjectComparer);
                }
            }
        }

        private T Hold<T>(T value) where T : Il2CppObjectBase
        {
            if (_temporaries.Count >= GuestReferenceAudit.MaxReferences) Fail("Ingame temporary reference budget exceeded.");
            Pointer(value); _temporaries.Add(value); return value;
        }
        private T Allocate<T>(Reader reader, Func<IntPtr, T> wrap) where T : Il2CppObjectBase
        {
            // Class-store initialization is itself a guarded native boundary.
            IntPtr klass = reader.Read(() => Il2CppClassPointerStore<T>.NativeClassPtr);
            if (klass == IntPtr.Zero) Fail("An exact ingame native class is unavailable.");
            T result = null;
            reader.Step(() =>
            {
                IntPtr pointer = IL2CPP.il2cpp_object_new(klass);
                if (pointer == IntPtr.Zero) Fail("Ingame allocation returned no object.");
                // Retain the returned allocation before its post-call guard;
                // no class read or business field write precedes that guard.
                result = Hold(wrap(pointer));
            });
            if (reader.Read(() => Il2CppClassPointerStore<T>.NativeClassPtr) != klass) Fail("Ingame allocation class changed.");
            reader.Exact<T>(result); return result;
        }
        private InGameSaveData CloneRecord(Reader r, InGameSaveType key, Record value)
        {
            switch (key)
            {
                case InGameSaveType.CharacterHealth:
                    var hp = Allocate(r, ptr => new CharacterHealthData(ptr)); r.Step(() => hp.HP = value.Health); return hp;
                case InGameSaveType.CharcterEquip:
                    var equipment = Allocate(r, ptr => new CharacterEquipData(ptr));
                    Il2CppSystem.Collections.Generic.List<int> ints = null;
                    if (value.Ints != null)
                    {
                        r.Step(() => ints = Hold(new Il2CppSystem.Collections.Generic.List<int>(value.Ints.Length)));
                        foreach (int item in value.Ints) r.Step(() => ints.Add(item));
                    }
                    r.Step(() => equipment._currentEquipInInventory_k__BackingField = ints); r.Step(() => equipment.gunAmmo = value.Ammo); return equipment;
                case InGameSaveType.CharacterSubHelper:
                    var helper = Allocate(r, ptr => new CharacterSubHelperData(ptr));
                    Il2CppReferenceArray<SubHelperSlotData> slots = null;
                    if (value.Slots != null)
                    {
                        r.Step(() => slots = Hold(new Il2CppReferenceArray<SubHelperSlotData>(value.Slots.Length)));
                        for (int i = 0; i < value.Slots.Length; i++)
                        {
                            int index = i; SlotRecord item = value.Slots[i];
                            if (item == null) continue;
                            var slot = Allocate(r, ptr => new SubHelperSlotData(ptr));
                            Il2CppSystem.Collections.Generic.Queue<IInstalledDevice> queue = null;
                            if (item.Queue != null)
                            {
                                r.Step(() => queue = Hold(new Il2CppSystem.Collections.Generic.Queue<IInstalledDevice>(item.Queue.Capacity)));
                                r.Step(() => queue._head = item.Queue.Head); r.Step(() => queue._tail = item.Queue.Tail);
                                r.Step(() => queue._version = item.Queue.Version);
                            }
                            r.Step(() => slot.subHelper = null); r.Step(() => slot.remainCount = item.Count);
                            r.Step(() => slot.remainTime = item.Remain); r.Step(() => slot.lastActiveTime = item.Last);
                            r.Step(() => slot.unfocusdTime = item.Unfocused); r.Step(() => slot.isAvailable = item.Available);
                            r.Step(() => slot.gearQueue = queue); r.Step(() => slots[index] = slot);
                        }
                    }
                    r.Step(() => helper._subHelperSlots_k__BackingField = slots); return helper;
                case InGameSaveType.CharacterInstallDevice:
                    var installed = Allocate(r, ptr => new CharacterInstallDeviceData(ptr));
                    Il2CppSystem.Collections.Generic.List<InstallDeviceSaveSlot> devices = null;
                    if (value.Devices != null)
                    {
                        r.Step(() => devices = Hold(new Il2CppSystem.Collections.Generic.List<InstallDeviceSaveSlot>(value.Devices.Length)));
                        foreach (DeviceRecord item in value.Devices)
                        {
                            var device = Allocate(r, ptr => new InstallDeviceSaveSlot(ptr));
                            r.Step(() => device._DeviceType_k__BackingField = item.Type); r.Step(() => device._UID_k__BackingField = item.Uid);
                            r.Step(() => device._InstalledPosition_k__BackingField = item.Position); r.Step(() => devices.Add(device));
                        }
                    }
                    r.Step(() => installed._currentInstalledDevices_k__BackingField = devices); return installed;
                case InGameSaveType.PuzzleState:
                    var puzzle = Allocate(r, ptr => new PuzzleStateSaveData(ptr));
                    Il2CppSystem.Collections.Generic.Dictionary<string, bool> solved = null;
                    if (value.Puzzles != null)
                    {
                        var puzzleComparer = NativeGuestDictionaryComparer.Copy<string>(value.PuzzleComparer, r.CheckWindow, item => Hold(item));
                        r.Step(() => solved = Hold(new Il2CppSystem.Collections.Generic.Dictionary<string, bool>(value.Puzzles.Count, puzzleComparer)));
                        r.VerifyComparer(solved, value.PuzzleComparer, puzzleComparer);
                        foreach (var item in value.Puzzles) r.Step(() => solved.Add(item.Key, item.Value));
                    }
                    r.Step(() => puzzle._keyToSolves = solved); return puzzle;
                case InGameSaveType.InGameObject:
                    var objects = Allocate(r, ptr => new InGameObjectSaveData(ptr));
                    Il2CppSystem.Collections.Generic.Dictionary<string, InGameObjectSaveData.Data> saved = null;
                    if (value.Objects != null)
                    {
                        var objectComparer = NativeGuestDictionaryComparer.Copy<string>(value.ObjectComparer, r.CheckWindow, item => Hold(item));
                        r.Step(() => saved = Hold(new Il2CppSystem.Collections.Generic.Dictionary<string, InGameObjectSaveData.Data>(value.Objects.Count, objectComparer)));
                        r.VerifyComparer(saved, value.ObjectComparer, objectComparer);
                        foreach (var item in value.Objects)
                        {
                            var data = Allocate(r, ptr => new InGameObjectSaveData.Data(ptr));
                            r.Step(() => data.key = item.Value.Key); r.Step(() => data.isSaved = item.Value.Saved);
                            r.Step(() => saved.Add(item.Key, data));
                        }
                    }
                    r.Step(() => objects._keyToDatas = saved); return objects;
                default: Fail("Missing proof for an unknown ingame record kind."); return null;
            }
        }

        private sealed class Snapshot
        { public string Values, Identity; public NativeGuestDictionaryComparer.Stamp Comparer; public SortedDictionary<InGameSaveType, Record> Rows; }
        private sealed class Record
        {
            public float Health; public int Ammo; public int[] Ints; public SlotRecord[] Slots; public DeviceRecord[] Devices;
            public SortedDictionary<string, bool> Puzzles; public SortedDictionary<string, ObjectRecord> Objects;
            public NativeGuestDictionaryComparer.Stamp PuzzleComparer, ObjectComparer;
        }
        private sealed class SlotRecord { public int Count; public float Remain, Last, Unfocused; public bool Available; public QueueRecord Queue; }
        private sealed class QueueRecord { public int Capacity, Head, Tail, Version; }
        private sealed class DeviceRecord { public InGameSaveInstallDeviceType Type; public string Uid; public UnityEngine.Vector3 Position; }
        private sealed class ObjectRecord { public string Key; public bool Saved; }

        private sealed class Reader
        {
            private readonly int _thread; private readonly Action _window; private readonly Func<long> _serial; private readonly long _expected;
            private readonly StringBuilder _identity = new StringBuilder();
            private readonly HashSet<long> _records = new HashSet<long>();
            private Action<Il2CppObjectBase> _audit;
            private int _visits, _characters;
            public Reader(int thread, Action window, Func<long> serial = null, long expected = 0)
            { _thread = thread; _window = window ?? throw new ArgumentNullException(nameof(window)); _serial = serial; _expected = expected; }
            private void Check()
            {
                if (_serial != null && _serial() != _expected) Fail("Ingame operation acquired a new fault.");
                if (Environment.CurrentManagedThreadId != _thread) Fail("Ingame access requires its captured Unity thread.");
                _window();
                if (Environment.CurrentManagedThreadId != _thread || _serial != null && _serial() != _expected) Fail("Ingame operation lost its thread or fault boundary.");
            }
            public T Read<T>(Func<T> read) { Check(); T value = read(); Check(); return value; }
            public void Step(Action action) { Check(); action(); Check(); }
            public void CheckWindow() => Check();
            public void VerifyComparer<TKey, TValue>(Il2CppSystem.Collections.Generic.Dictionary<TKey, TValue> dictionary,
                NativeGuestDictionaryComparer.Stamp source, Il2CppSystem.Collections.Generic.IEqualityComparer<TKey> expected)
            {
                var actual = NativeGuestDictionaryComparer.Capture(Read(() => dictionary._comparer), Check);
                var supplied = NativeGuestDictionaryComparer.Capture(expected, Check);
                NativeGuestDictionaryComparer.RequireSameIdentity(supplied, actual);
                NativeGuestDictionaryComparer.RequireSameSemantics(source, actual);
            }
            private T Visit<T>(Func<T> read) { if (_visits >= MaxStorageReads) Fail("Ingame storage read budget exceeded."); _visits++; return Read(read); }
            public void Exact<T>(Il2CppObjectBase value) where T : Il2CppObjectBase
            {
                IntPtr expected = Read(() => Il2CppClassPointerStore<T>.NativeClassPtr);
                if (expected == IntPtr.Zero || Read(() => IL2CPP.il2cpp_object_get_class(new IntPtr(Pointer(value)))) != expected ||
                    expected != Read(() => Il2CppClassPointerStore<T>.NativeClassPtr)) Fail("Missing proof for an unexpected exact native ingame class.");
                _identity.Append(N(expected.ToInt64()));
            }
            private void Reference(Il2CppObjectBase value, bool record = false)
            {
                Check(); long pointer = Pointer(value);
                if (record && !_records.Add(pointer)) Fail("Missing proof for shared ingame record alias semantics.");
                _audit(value); _identity.Append(N(pointer)); Check();
            }
            private void Storage(Il2CppObjectBase value, int length)
            { if (length < 0 || length > MaxContainerStorage) Fail("Ingame container storage exceeds its bound."); if (value != null && length != 0) Reference(value); }
            private static string N(long value) => value.ToString(CultureInfo.InvariantCulture) + ";";
            private static string F(float value)
            { if (float.IsNaN(value) || float.IsInfinity(value)) Fail("An ingame numeric value is not finite."); return N(BitConverter.SingleToInt32Bits(value)); }
            private string Text(string value)
            {
                if (value == null) return "-1:";
                if (value.Length > MaxStringCharacters) Fail("An ingame string exceeds its bound.");
                _characters += value.Length; if (_characters > MaxSnapshotCharacters) Fail("Ingame snapshot exceeds its bound.");
                return value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
            }
            public Snapshot Graph(IngameCacheDictionary dictionary, Action<Il2CppObjectBase> audit)
            {
                _audit = audit; _identity.Clear(); _records.Clear();
                if (dictionary == null) return new Snapshot { Values = "absent", Identity = "0;", Rows = new SortedDictionary<InGameSaveType, Record>() };
                NativeGuestDictionaryComparer.Stamp comparer;
                var rows = Dictionary(dictionary, Comparer<InGameSaveType>.Default, (key, value) => ReadRecord(key, value), out comparer);
                var values = new StringBuilder();
                foreach (var item in rows) values.Append(N((int)item.Key)).Append(Encode(item.Key, item.Value));
                return new Snapshot { Rows = rows, Comparer = comparer, Values = values.ToString(), Identity = _identity.ToString() };
            }
            private Record ReadRecord(InGameSaveType key, InGameSaveData value)
            {
                Reference(value, true);
                var record = new Record();
                switch (key)
                {
                    case InGameSaveType.CharacterHealth:
                        Exact<CharacterHealthData>(value); var hp = Read(() => new CharacterHealthData(new IntPtr(Pointer(value))));
                        record.Health = Read(() => hp.HP); F(record.Health); break;
                    case InGameSaveType.CharcterEquip:
                        Exact<CharacterEquipData>(value); var equip = Read(() => new CharacterEquipData(new IntPtr(Pointer(value))));
                        record.Ammo = Read(() => equip.gunAmmo); record.Ints = IntList(Read(() => equip._currentEquipInInventory_k__BackingField)); break;
                    case InGameSaveType.CharacterSubHelper:
                        Exact<CharacterSubHelperData>(value); var helper = Read(() => new CharacterSubHelperData(new IntPtr(Pointer(value))));
                        record.Slots = Slots(Read(() => helper._subHelperSlots_k__BackingField)); break;
                    case InGameSaveType.CharacterInstallDevice:
                        Exact<CharacterInstallDeviceData>(value); var installed = Read(() => new CharacterInstallDeviceData(new IntPtr(Pointer(value))));
                        record.Devices = ReferenceList(Read(() => installed._currentInstalledDevices_k__BackingField), Device); break;
                    case InGameSaveType.PuzzleState:
                        Exact<PuzzleStateSaveData>(value); var puzzle = Read(() => new PuzzleStateSaveData(new IntPtr(Pointer(value))));
                        record.Puzzles = Dictionary(Read(() => puzzle._keyToSolves), StringComparer.Ordinal, (name, solved) => solved, out record.PuzzleComparer); break;
                    case InGameSaveType.InGameObject:
                        Exact<InGameObjectSaveData>(value); var objects = Read(() => new InGameObjectSaveData(new IntPtr(Pointer(value))));
                        record.Objects = Dictionary(Read(() => objects._keyToDatas), StringComparer.Ordinal, (name, data) => Object(data), out record.ObjectComparer); break;
                    default: Fail("Missing proof for an unknown ingame enum kind."); break;
                }
                return record;
            }
            private SortedDictionary<TKey, TImage> Dictionary<TKey, TValue, TImage>(Il2CppSystem.Collections.Generic.Dictionary<TKey, TValue> dictionary,
                IComparer<TKey> comparer, Func<TKey, TValue, TImage> capture, out NativeGuestDictionaryComparer.Stamp comparerStamp)
            {
                comparerStamp = null;
                if (dictionary == null) return null;
                Exact<Il2CppSystem.Collections.Generic.Dictionary<TKey, TValue>>(dictionary); Reference(dictionary);
                DictionaryAuxiliaryEmpty(dictionary);
                comparerStamp = NativeGuestDictionaryComparer.Capture(Read(() => dictionary._comparer), Check);
                if (comparerStamp.Reference != null) Reference(comparerStamp.Reference);
                _identity.Append(N((int)comparerStamp.ClassKind)).Append(N(comparerStamp.NativeClass.ToInt64())).Append(N(comparerStamp.Pointer)).Append(N(comparerStamp.DefaultComparerPointer));
                int count = Read(() => dictionary._count), free = Read(() => dictionary._freeCount), version = Read(() => dictionary._version), freeList = Read(() => dictionary._freeList);
                var entries = Read(() => dictionary._entries); var buckets = Read(() => dictionary._buckets);
                int length = entries == null ? 0 : Read(() => entries.Length), bucketLength = buckets == null ? 0 : Read(() => buckets.Length);
                Storage(entries, length); Storage(buckets, bucketLength);
                if (count < 0 || count > length || free < 0 || free > count || (free > 0 && (freeList < 0 || freeList >= count)) ||
                    (length == 0) != (bucketLength == 0) || count > 0 && bucketLength == 0) Fail("Ingame dictionary shape is invalid.");
                _identity.Append(N(OptionalPointer(entries))).Append(N(OptionalPointer(buckets))).Append(N(count)).Append(N(free)).Append(N(freeList)).Append(N(version)).Append(N(length)).Append(N(bucketLength));
                for (int i = 0; i < bucketLength; i++) { int slot = Visit(() => buckets[i]); if (slot < -1 || slot > count) Fail("An ingame dictionary bucket is invalid."); _identity.Append(N(slot)); }
                var result = new SortedDictionary<TKey, TImage>(comparer);
                for (int i = 0; i < length; i++)
                {
                    var entry = Visit(() => entries[i]); if (entry == null) Fail("An ingame entry could not be read.");
                    int hash = Read(() => entry.hashCode), next = Read(() => entry.next);
                    TKey key = Read(() => entry.key); TValue value = Read(() => entry.value);
                    _identity.Append(N(hash)).Append(N(next));
                    if (key is InGameSaveType kind) _identity.Append(N((int)kind));
                    else if (key is string text) _identity.Append(Text(text));
                    else if ((object)key == null) _identity.Append("null-key;");
                    else Fail("Missing proof for an unexpected ingame dictionary key type.");
                    if (value is bool flag) _identity.Append(N(flag ? 1 : 0));
                    if (i >= count || hash < 0)
                    {
                        if (value is Il2CppObjectBase || key is string stale && stale != null) Fail("An unused ingame entry retains a reference.");
                        continue;
                    }
                    if ((object)key == null || next < -1 || next >= count || result.ContainsKey(key)) Fail("An ingame entry is invalid or ambiguous.");
                    result.Add(key, capture(key, value));
                }
                if (result.Count != count - free) Fail("Ingame dictionary live count differs.");
                if (count != Read(() => dictionary._count) || free != Read(() => dictionary._freeCount) || version != Read(() => dictionary._version) || freeList != Read(() => dictionary._freeList) ||
                    OptionalPointer(entries) != OptionalPointer(Read(() => dictionary._entries)) || OptionalPointer(buckets) != OptionalPointer(Read(() => dictionary._buckets))) Fail("Ingame dictionary changed during capture.");
                DictionaryAuxiliaryEmpty(dictionary);
                NativeGuestDictionaryComparer.RequireSameIdentity(comparerStamp, NativeGuestDictionaryComparer.Capture(Read(() => dictionary._comparer), Check));
                return result;
            }
            private void DictionaryAuxiliaryEmpty<TKey, TValue>(Il2CppSystem.Collections.Generic.Dictionary<TKey, TValue> dictionary)
            {
                if (Read(() => dictionary._keys) != null || Read(() => dictionary._values) != null || Read(() => dictionary._syncRoot) != null)
                    Fail("Missing proof for an ingame dictionary view or synchronization reference.");
                _identity.Append("no-dictionary-view-or-sync;");
            }
            private int[] IntList(Il2CppSystem.Collections.Generic.List<int> list)
            {
                if (list == null) return null;
                Exact<Il2CppSystem.Collections.Generic.List<int>>(list); Reference(list);
                if (Read(() => list._syncRoot) != null) Fail("Missing proof for an ingame list synchronization reference.");
                int size = Read(() => list._size), version = Read(() => list._version); var array = Read(() => list._items);
                if (array == null) Fail("Ingame list storage is absent."); int length = Read(() => array.Length); Storage(array, length);
                if (size < 0 || size > length) Fail("Ingame list size is invalid.");
                _identity.Append(N(OptionalPointer(array))).Append(N(size)).Append(N(version)).Append(N(length));
                var result = new int[size];
                for (int i = 0; i < length; i++) { int value = Visit(() => array[i]); _identity.Append(N(value)); if (i < size) result[i] = value; }
                if (size != Read(() => list._size) || version != Read(() => list._version) || OptionalPointer(array) != OptionalPointer(Read(() => list._items)) ||
                    Read(() => list._syncRoot) != null) Fail("Ingame list changed during capture.");
                return result;
            }
            private TImage[] ReferenceList<T, TImage>(Il2CppSystem.Collections.Generic.List<T> list, Func<T, TImage> capture) where T : Il2CppObjectBase
            {
                if (list == null) return null;
                Exact<Il2CppSystem.Collections.Generic.List<T>>(list); Reference(list);
                if (Read(() => list._syncRoot) != null) Fail("Missing proof for an ingame list synchronization reference.");
                int size = Read(() => list._size), version = Read(() => list._version); var array = Read(() => list._items);
                if (array == null) Fail("Ingame list storage is absent."); int length = Read(() => array.Length); Storage(array, length);
                if (size < 0 || size > length) Fail("Ingame list size is invalid.");
                _identity.Append(N(OptionalPointer(array))).Append(N(size)).Append(N(version)).Append(N(length));
                var result = new TImage[size];
                for (int i = 0; i < length; i++)
                {
                    T value = Visit(() => array[i]);
                    if (i >= size) { if (value != null) Fail("An unused ingame list slot retains a mutable reference."); continue; }
                    result[i] = capture(value);
                }
                if (size != Read(() => list._size) || version != Read(() => list._version) || OptionalPointer(array) != OptionalPointer(Read(() => list._items)) ||
                    Read(() => list._syncRoot) != null) Fail("Ingame list changed during capture.");
                return result;
            }
            private SlotRecord[] Slots(Il2CppReferenceArray<SubHelperSlotData> array)
            {
                if (array == null) return null; int length = Read(() => array.Length); Storage(array, length);
                _identity.Append(N(OptionalPointer(array))).Append(N(length)); var result = new SlotRecord[length];
                for (int i = 0; i < length; i++)
                {
                    var value = Visit(() => array[i]); if (value == null) { _identity.Append("null-slot;"); continue; }
                    Exact<SubHelperSlotData>(value); Reference(value, true);
                    if (Read(() => value.subHelper) != null) Fail("Missing proof for Unity subhelper resource and serialization graph isolation.");
                    var slot = new SlotRecord { Count = Read(() => value.remainCount), Remain = Read(() => value.remainTime), Last = Read(() => value.lastActiveTime),
                        Unfocused = Read(() => value.unfocusdTime), Available = Read(() => value.isAvailable), Queue = EmptyQueue(Read(() => value.gearQueue)) };
                    F(slot.Remain); F(slot.Last); F(slot.Unfocused); result[i] = slot;
                }
                return result;
            }
            private QueueRecord EmptyQueue(Il2CppSystem.Collections.Generic.Queue<IInstalledDevice> queue)
            {
                if (queue == null) return null;
                Exact<Il2CppSystem.Collections.Generic.Queue<IInstalledDevice>>(queue); Reference(queue, true);
                int size = Read(() => queue._size), head = Read(() => queue._head), tail = Read(() => queue._tail), version = Read(() => queue._version);
                if (size != 0) Fail("Missing proof for live installed-device queue isolation.");
                if (Read(() => queue._syncRoot) != null) Fail("Missing proof for a queue synchronization reference.");
                var array = Read(() => queue._array); if (array == null) Fail("Ingame queue storage is absent."); int length = Read(() => array.Length); Storage(array, length);
                if (head < 0 || tail < 0 || head != tail || length == 0 && head != 0 || length > 0 && head >= length) Fail("Ingame empty queue shape is invalid.");
                _identity.Append(N(OptionalPointer(array))).Append(N(head)).Append(N(tail)).Append(N(version)).Append(N(length));
                for (int i = 0; i < length; i++) if (Visit(() => array[i]) != null) Fail("Missing proof for retained devices in an empty queue.");
                if (size != Read(() => queue._size) || head != Read(() => queue._head) || tail != Read(() => queue._tail) || version != Read(() => queue._version) ||
                    OptionalPointer(array) != OptionalPointer(Read(() => queue._array)) || Read(() => queue._syncRoot) != null) Fail("Ingame queue changed during capture.");
                return new QueueRecord { Capacity = length, Head = head, Tail = tail, Version = version };
            }
            private DeviceRecord Device(InstallDeviceSaveSlot value)
            {
                Exact<InstallDeviceSaveSlot>(value); Reference(value, true);
                var result = new DeviceRecord { Type = Read(() => value._DeviceType_k__BackingField), Uid = Read(() => value._UID_k__BackingField), Position = Read(() => value._InstalledPosition_k__BackingField) };
                F(result.Position.x); F(result.Position.y); F(result.Position.z); Text(result.Uid); return result;
            }
            private ObjectRecord Object(InGameObjectSaveData.Data value)
            { Exact<InGameObjectSaveData.Data>(value); Reference(value, true); return new ObjectRecord { Key = Read(() => value.key), Saved = Read(() => value.isSaved) }; }
            private string Encode(InGameSaveType key, Record value)
            {
                var output = new StringBuilder();
                switch (key)
                {
                    case InGameSaveType.CharacterHealth: output.Append(F(value.Health)); break;
                    case InGameSaveType.CharcterEquip:
                        output.Append(N(value.Ammo)).Append(N(value.Ints == null ? -1 : value.Ints.Length)); if (value.Ints != null) foreach (int item in value.Ints) output.Append(N(item)); break;
                    case InGameSaveType.CharacterSubHelper:
                        output.Append(N(value.Slots == null ? -1 : value.Slots.Length));
                        if (value.Slots != null) foreach (SlotRecord slot in value.Slots)
                        {
                            if (slot == null) { output.Append("null-slot;"); continue; }
                            output.Append("slot;").Append(N(slot.Count)).Append(F(slot.Remain)).Append(F(slot.Last)).Append(F(slot.Unfocused)).Append(N(slot.Available ? 1 : 0));
                            if (slot.Queue == null) output.Append("no-queue;"); else output.Append("queue;").Append(N(slot.Queue.Capacity)).Append(N(slot.Queue.Head)).Append(N(slot.Queue.Tail)).Append(N(slot.Queue.Version));
                        }
                        break;
                    case InGameSaveType.CharacterInstallDevice:
                        output.Append(N(value.Devices == null ? -1 : value.Devices.Length));
                        if (value.Devices != null) foreach (var item in value.Devices) output.Append(N((int)item.Type)).Append(Text(item.Uid)).Append(F(item.Position.x)).Append(F(item.Position.y)).Append(F(item.Position.z)); break;
                    case InGameSaveType.PuzzleState:
                        output.Append(N(value.Puzzles == null ? -1 : value.Puzzles.Count)); if (value.Puzzles != null) foreach (var item in value.Puzzles) output.Append(Text(item.Key)).Append(N(item.Value ? 1 : 0)); break;
                    case InGameSaveType.InGameObject:
                        output.Append(N(value.Objects == null ? -1 : value.Objects.Count)); if (value.Objects != null) foreach (var item in value.Objects) output.Append(Text(item.Key)).Append(Text(item.Value.Key)).Append(N(item.Value.Saved ? 1 : 0)); break;
                }
                return output.ToString();
            }
        }
    }
}
