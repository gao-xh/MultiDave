using System;
using System.Collections.Generic;
using DaveCoop.Core.World;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using NativeInfoList = Il2CppSystem.Collections.Generic.List<IGPSetInfo>;

namespace DaveCoop.Networking
{
    // Resolves an existing local info only inside its actual controller lease.
    // No condition, save, random-selection or prefab-loading method is called.
    internal sealed class NativeGuestIgpSelection
    {
        public const int MaxOwnedHandles = 5;
        public const int MaxRetainedSelections = 256;
        public const int MaxStepsPerCall = 8192;
        private const int MaxArrayCapacity = MapSelections.MaxGroups * 8;
        private static readonly object OwnersGate = new object();
        private static readonly HashSet<NativeGuestIgpSelection> Owners = new HashSet<NativeGuestIgpSelection>();
        private readonly IGPSetController _controller;
        private readonly MapIgpChoice _choice;
        private readonly Func<bool> _source;
        private readonly int _thread;
        private readonly List<Reference> _references = new List<Reference>();
        private NativeInfoList _list;
        private Il2CppArrayBase<IGPSetInfo> _items;
        private Record[] _records;
        private Record _selected;
        private IntPtr _controllerPointer, _controllerUnityPointer, _listPointer, _itemsPointer, _itemsClass;
        private int _size, _version, _capacity, _steps, _selectedIndex = -1;
        private long _totalSteps;
        private bool _busy, _attempted, _resolved, _failed, _retained, _prefabTransitionAttempted;
        private string _reason = "Awaiting the fixed local IGP selection window.";

        private sealed class Record
        {
            public IGPSetInfo Native;
            public IGPSetObject Prefab;
            public IntPtr Pointer, PrefabPointer, PrefabClass, PrefabUnityPointer;
            public bool Addressable;
            public string Key, PrefabName;
        }
        private sealed class Reference
        {
            public Il2CppObjectBase Wrapper;
            public IntPtr Pointer, Handle;
        }
        private sealed class Rejected : InvalidOperationException
        { public Rejected(string reason) : base(reason) { } }

        public bool Resolved => _resolved && !_failed;
        public bool Failed => _failed;
        public string LastReason => _reason;
        public int OwnedHandleCount => _references.Count;
        public long NativeSteps => _totalSteps;
        internal long SelectedPointer => _selected == null ? 0 : _selected.Pointer.ToInt64();
        public bool NativeFieldAbiVerified => false;
        public bool NativeRuntimeVerified => false;
        public bool PartialConstructorAllocationRetentionVerified => false;
        public bool AddressableAssetOriginVerified => false;
        public bool GuestStateIsolated => false;
        public bool WorldAuthority => false;
        public bool CargoAuthority => false;

        // Construction copies CLR schema only. The delegate must itself check
        // the actual lease, scene/controller birth and exact current wire choice;
        // it must not call this helper, which would reenter a validation window.
        public NativeGuestIgpSelection(IGPSetController controller, MapIgpChoice choice, Func<bool> source)
        {
            if (ReferenceEquals(controller, null)) throw new ArgumentNullException(nameof(controller));
            _choice = MapChoiceFrames.Copy(choice);
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _controller = controller; _thread = Environment.CurrentManagedThreadId;
        }

        public bool TryResolve(out IGPSetInfo selected)
        {
            selected = null;
            if (!Enter()) return false;
            try
            {
                if (_attempted) throw Reject("Local IGP resolution is single-use; it is not retried.");
                RetainOwner(); _attempted = true;
                Exact<IGPSetController>(_controller);
                _controllerPointer = Read(() => _controller.Pointer);
                _controllerUnityPointer = Read(() => _controller.m_CachedPtr);
                if (_controllerPointer == IntPtr.Zero || _controllerUnityPointer == IntPtr.Zero)
                    throw Reject("The fixed local IGP controller is not live.");
                Keep(_controller);
                Read(() => { _list = _controller.IGPSetInfoList; return true; });
                if (ReferenceEquals(_list, null)) throw Reject("The fixed controller has no local IGP list.");
                Exact<NativeInfoList>(_list); _listPointer = Read(() => _list.Pointer); Keep(_list);
                _size = Read(() => _list._size); _version = Read(() => _list._version);
                Read(() => { _items = _list._items; return true; });
                if (_size < 1 || _size > MapSelections.MaxGroups || ReferenceEquals(_items, null))
                    throw Reject("The local IGP list is empty or exceeds its bounded schema.");
                _itemsPointer = Read(() => _items.Pointer);
                if (_itemsPointer == IntPtr.Zero) throw Reject("The native IGP array has no identity.");
                _capacity = Read(() => _items.Length);
                _itemsClass = Read(() => IL2CPP.il2cpp_object_get_class(_itemsPointer));
                if (_itemsPointer == IntPtr.Zero || _itemsClass == IntPtr.Zero || _capacity < _size || _capacity > MaxArrayCapacity)
                    throw Reject("The native IGP array has an unsupported bounded shape.");
                Keep(_items);
                _records = new Record[_size];
                int matches = 0;
                for (int index = 0; index < _size; index++)
                {
                    Record record = ReadRecord(index);
                    _records[index] = record;
                    if (!Matches(record)) continue;
                    matches++; _selected = record; _selectedIndex = index;
                }
                if (matches != 1) throw Reject("The host choice does not identify exactly one local IGP info.");
                Keep(_selected.Native);
                if (!ReferenceEquals(_selected.Prefab, null)) Keep(_selected.Prefab);
                ValidateCore(false);
                RequireSource();
                _resolved = true; selected = _selected.Native;
                _reason = "One existing local info resolved; native loading and complete world adoption remain unverified.";
                return true;
            }
            catch (Rejected error) { return Fail(error.Message); }
            catch (Exception) { return Fail("Native IGP resolution is unknown; retained references will not be retried."); }
            finally { _busy = false; }
        }

        // Strict validation before replacing the original random call. Any
        // prefab change, including null -> live, is rejected in this stage.
        public bool Validate() => ValidateSelection(false);

        // Only the fixed original Init may select this info. An addressable
        // info may then receive its first live Prefab from the natural loader.
        // That single field transition is not an asset-origin permission.
        public bool ValidateAfterOriginalChoice() => ValidateSelection(true);

        private bool ValidateSelection(bool afterOriginal)
        {
            if (!Enter()) return false;
            try
            {
                if (!_resolved || _selected == null) throw Reject("No resolved local selection is available to validate.");
                ValidateCore(afterOriginal); RequireSource();
                return true;
            }
            catch (Rejected error) { return Fail(error.Message); }
            catch (Exception) { return Fail("Native IGP validation is unknown; fixed selection evidence is revoked."); }
            finally { _busy = false; }
        }

        private void ValidateCore(bool afterOriginal)
        {
            ValidateStructure();
            if (afterOriginal)
            {
                IGPSetInfo current = Read(() => _controller.CurrIGPSetInfo);
                if (ReferenceEquals(current, null) || Read(() => current.Pointer) != _selected.Pointer)
                    throw Reject("The original controller did not select its fixed resolved info.");
                Exact<IGPSetInfo>(current);
                Record currentSelected = ReadSelectedRecord();
                if (currentSelected.Pointer != _selected.Pointer || currentSelected.Addressable != _selected.Addressable || currentSelected.Key != _selected.Key)
                    throw Reject("The original selected info changed its fixed membership, mode or key.");
                ValidateSelectedAfter(currentSelected);
            }
            else
            {
                int matches = 0;
                for (int index = 0; index < _size; index++)
                {
                    Record current = ReadRecord(index), original = _records[index];
                    if (current.Pointer != original.Pointer || current.Addressable != original.Addressable || current.Key != original.Key || !SamePrefab(original, current))
                        throw Reject("The fixed local candidate list changed before original selection.");
                    if (Matches(current)) matches++;
                }
                if (matches != 1) throw Reject("The fixed host choice is no longer unique in the local list.");
            }
            // No list version is treated as a substitute for field rechecks.
            Record selected = ReadSelectedRecord();
            if (selected.Pointer != _selected.Pointer || selected.Addressable != _selected.Addressable || selected.Key != _selected.Key ||
                !SamePrefab(_selected, selected) || !Matches(selected))
                throw Reject("The selected local info changed during its validation window.");
            ValidateStructure();
            if (afterOriginal)
            {
                IGPSetInfo current = Read(() => _controller.CurrIGPSetInfo);
                if (ReferenceEquals(current, null) || Read(() => current.Pointer) != _selected.Pointer)
                    throw Reject("The original selected-info identity changed during validation.");
            }
        }

        private Record ReadSelectedRecord()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _size) throw Reject("The selected info has no frozen list position.");
            return ReadRecord(_selectedIndex);
        }

        private void ValidateSelectedAfter(Record current)
        {
            if (SamePrefab(_selected, current)) return;
            if (!_selected.Addressable || _selected.PrefabPointer != IntPtr.Zero || current.PrefabPointer == IntPtr.Zero || _prefabTransitionAttempted)
                throw Reject("The original selected prefab was replaced or lost.");
            // Preserve the attempted transition before handle allocation or its
            // post-source guard can throw. Unknown retention is not retried.
            _prefabTransitionAttempted = true;
            Keep(current.Prefab);
            _selected.Prefab = current.Prefab; _selected.PrefabPointer = current.PrefabPointer;
            _selected.PrefabClass = current.PrefabClass; _selected.PrefabUnityPointer = current.PrefabUnityPointer;
            _selected.PrefabName = current.PrefabName;
            RequireSource();
        }

        private void ValidateStructure()
        {
            Exact<IGPSetController>(_controller);
            if (Read(() => _controller.Pointer) != _controllerPointer || Read(() => _controller.m_CachedPtr) != _controllerUnityPointer ||
                _controllerUnityPointer == IntPtr.Zero)
                throw Reject("The fixed IGP controller identity is no longer live.");
            NativeInfoList list = Read(() => _controller.IGPSetInfoList);
            if (ReferenceEquals(list, null) || Read(() => list.Pointer) != _listPointer)
                throw Reject("The controller's original IGP list was replaced.");
            Exact<NativeInfoList>(list);
            if (Read(() => _list.Pointer) != _listPointer || Read(() => _list._size) != _size || Read(() => _list._version) != _version)
                throw Reject("The original IGP list structure or version changed.");
            var items = Read(() => _list._items);
            if (ReferenceEquals(items, null) || Read(() => items.Pointer) != _itemsPointer || Read(() => _items.Pointer) != _itemsPointer ||
                Read(() => _items.Length) != _capacity || Read(() => IL2CPP.il2cpp_object_get_class(_itemsPointer)) != _itemsClass)
                throw Reject("The original IGP array identity or capacity changed.");
        }

        private Record ReadRecord(int index)
        {
            var record = new Record();
            record.Native = Read(() => _items[index]);
            if (ReferenceEquals(record.Native, null)) throw Reject("The local IGP list contains an unsupported null info.");
            Exact<IGPSetInfo>(record.Native);
            record.Pointer = Read(() => record.Native.Pointer);
            record.Addressable = Read(() => record.Native.isAddressableMode);
            record.Key = Name(Read(() => record.Native.prefabName));
            record.Prefab = Read(() => record.Native.Prefab);
            if (!ReferenceEquals(record.Prefab, null))
            {
                record.PrefabPointer = Read(() => record.Prefab.Pointer);
                if (record.PrefabPointer == IntPtr.Zero) throw Reject("A local IGP prefab has no native identity.");
                record.PrefabClass = Read(() => IL2CPP.il2cpp_object_get_class(record.PrefabPointer));
                record.PrefabUnityPointer = Read(() => record.Prefab.m_CachedPtr);
                if (record.PrefabPointer == IntPtr.Zero || record.PrefabClass == IntPtr.Zero || record.PrefabUnityPointer == IntPtr.Zero)
                    throw Reject("A local IGP prefab reference is not live.");
                record.PrefabName = Name(Read(() => record.Prefab.name)); // Actual Unity call, not a direct field.
            }
            else record.PrefabName = "";
            return record;
        }

        private bool Matches(Record record) => record.Addressable == _choice.Addressable && record.Key == _choice.SelectedPrefabName &&
            (record.Addressable || (record.PrefabPointer != IntPtr.Zero && record.PrefabName == _choice.PrefabObjectName));
        private static bool SamePrefab(Record left, Record right) => left.PrefabPointer == right.PrefabPointer &&
            left.PrefabClass == right.PrefabClass && left.PrefabUnityPointer == right.PrefabUnityPointer && left.PrefabName == right.PrefabName;

        private static string Name(string value)
        {
            value = value ?? "";
            if (value.Length > MapSelections.MaxPrefabName) throw Reject("A local prefab key or name exceeds its bounded schema.");
            for (int index = 0; index < value.Length; index++)
            {
                if (char.IsControl(value[index])) throw Reject("A local prefab key or name contains a control character.");
                if (!char.IsSurrogate(value[index])) continue;
                if (!char.IsHighSurrogate(value[index]) || index + 1 >= value.Length || !char.IsLowSurrogate(value[++index]))
                    throw Reject("A local prefab key or name contains invalid Unicode.");
            }
            return value;
        }

        private void Exact<T>(Il2CppObjectBase value) where T : Il2CppObjectBase
        {
            IntPtr pointer = Read(() => value.Pointer);
            if (pointer == IntPtr.Zero) throw Reject("An IGP object has no native identity.");
            IntPtr expected = Read(() => Il2CppClassPointerStore<T>.NativeClassPtr);
            if (expected == IntPtr.Zero) throw Reject("An IGP object's expected native class is unavailable.");
            IntPtr actual = Read(() => IL2CPP.il2cpp_object_get_class(pointer));
            if (pointer == IntPtr.Zero || expected == IntPtr.Zero || actual != expected)
                throw Reject("An IGP object has an unsupported exact native class.");
        }

        private void Keep(Il2CppObjectBase value)
        {
            if (ReferenceEquals(value, null)) throw Reject("An IGP retained reference is missing.");
            IntPtr pointer = Read(() => value.Pointer);
            foreach (Reference old in _references) if (old.Pointer == pointer) return;
            if (pointer == IntPtr.Zero || _references.Count >= MaxOwnedHandles) throw Reject("IGP strong-reference retention exceeded its bound.");
            var reference = new Reference { Pointer = pointer, Wrapper = value };
            _references.Add(reference);
            // Save the handle before the post-read source check. Do not free or
            // retry an allocation after that check loses its actual lease.
            Read(() => { reference.Handle = IL2CPP.il2cpp_gchandle_new(pointer, false); return true; });
            if (reference.Handle == IntPtr.Zero || Read(() => IL2CPP.il2cpp_gchandle_get_target(reference.Handle)) != pointer)
                throw Reject("The native IGP strong handle did not retain its exact object.");
        }

        private void RetainOwner()
        {
            lock (OwnersGate)
            {
                if (_retained) return;
                if (Owners.Count >= MaxRetainedSelections) throw Reject("The process IGP retained-owner bound is exhausted.");
                Owners.Add(this); _retained = true;
            }
        }
        private bool Enter()
        {
            if (_failed) return false;
            if (Environment.CurrentManagedThreadId != _thread) return Fail("IGP selection changed its construction thread.");
            if (_busy) return Fail("IGP selection reentered a native read window.");
            _busy = true; _steps = 0; return true;
        }
        private T Read<T>(Func<T> read)
        {
            RequireSource();
            if (_steps >= MaxStepsPerCall) throw Reject("The IGP native-step budget is exhausted.");
            _steps++;
            if (_totalSteps < long.MaxValue) _totalSteps++;
            T value = read();
            RequireSource();
            return value;
        }
        private void RequireSource()
        {
            if (_failed || !_busy || Environment.CurrentManagedThreadId != _thread || !_source() || _failed)
                throw Reject("The actual IGP lease, controller origin or fixed wire choice is unavailable.");
        }
        private bool Fail(string reason)
        { _failed = true; _reason = reason; return false; }
        private static Rejected Reject(string reason) => new Rejected(reason);
    }
}
