using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DaveCoop.Core.Guest;
using DR.Save;
using Il2CppInterop.Runtime.InteropTypes;
using PlayerInteractionCache = DR.Save.SaveSystemPlayerDataManager.InstanceInteractionData;

namespace DaveCoop.Networking
{
    // Only the declared interaction graph is read. Generic array/entry ABI,
    // other caches and source completeness still require native validation.
    internal static class NativeGuestInteractionShadow
    {
        public const int MaxContainerStorage = 1024;
        public const int MaxStringCharacters = 512;
        public const int MaxSnapshotCharacters = 512 * 1024;
        public const int MaxStorageReads = 16384;

        public static NativeGuestInteractionBaseline CaptureOriginal(SavePlayerData originalPlayer,
            PlayerInteractionCache originalInteraction, Action requireCloneWindow)
        {
            try
            {
                var reader = new Reader(requireCloneWindow, Environment.CurrentManagedThreadId);
                var audit = new GuestReferenceAudit();
                Action<Il2CppObjectBase> original = value => Audit(audit.AddOriginal(Pointer(value)), audit);
                var player = reader.Player(originalPlayer, original);
                var runtime = reader.Runtime(originalInteraction, original);
                RequireSameValues(player, runtime);
                requireCloneWindow();
                return new NativeGuestInteractionBaseline(originalPlayer, originalInteraction, player, runtime,
                    Environment.CurrentManagedThreadId);
            }
            catch (KnownReadFailure) { throw; }
            catch (Exception) { throw new KnownReadFailure("Known original interaction capture could not be completed."); }
        }

        public static NativeGuestInteractionShadowResult Prepare(SavePlayerData originalPlayer,
            PlayerInteractionCache originalInteraction, SavePlayerData detachedPlayer,
            NativeGuestInteractionBaseline capturedBaseline, Action requireCloneWindow)
        {
            try
            {
                var reader = new Reader(requireCloneWindow, Environment.CurrentManagedThreadId);
                if (capturedBaseline == null || capturedBaseline.UnityThreadId != Environment.CurrentManagedThreadId)
                    Fail("A captured original interaction baseline is required.");
                if (capturedBaseline.ReadFailureLatched) Fail("The captured original interaction baseline has a latched read failure.");
                if (reader.Read(() => Pointer(originalPlayer)) != capturedBaseline.PlayerBefore.RootPointer ||
                    reader.Read(() => Pointer(originalInteraction)) != capturedBaseline.InteractionBefore.RootPointer)
                    Fail("Original interaction source does not match the captured baseline.");
                var audit = new GuestReferenceAudit();
                Action<Il2CppObjectBase> original = value => Audit(audit.AddOriginal(Pointer(value)), audit);
                Action<Il2CppObjectBase> detached = value => Audit(audit.AddDetached(Pointer(value)), audit);
                var playerBefore = reader.Player(originalPlayer, original);
                var interactionBefore = reader.Runtime(originalInteraction, original);
                RequireUnchanged(capturedBaseline.PlayerBefore, playerBefore);
                RequireUnchanged(capturedBaseline.InteractionBefore, interactionBefore);
                RequireSameValues(playerBefore, interactionBefore);
                Audit(audit.BeginDetached(), audit);
                var playerDetached = reader.Player(detachedPlayer, detached);
                RequireSameValues(playerBefore, playerDetached);

                // Allocation and each Add are single attempts inside the real
                // backend's fenced clone window; a failed attempt is not retried.
                var interaction = reader.Read(() => new PlayerInteractionCache(false));
                var roots = reader.Bindings(detachedPlayer);
                reader.Step(() => interaction._InstalledCargoBoxes_k__BackingField = roots.Cargo);
                reader.Step(() => interaction._InstalledSensorDevices_k__BackingField = roots.Sensor);
                reader.Step(() => interaction._InstalledTriggerDevices_k__BackingField = roots.Trigger);
                reader.Step(() => interaction._InstalledFunctionalDevices_k__BackingField = roots.Functional);
                reader.Step(() => interaction._UsedInGameInteractionItems_k__BackingField = roots.Items);
                reader.Step(() => interaction._UsedInGameBreakableItems_k__BackingField = roots.Breakable);
                reader.Step(() => interaction._UsedCrabTrapZone_k__BackingField = roots.Crab);
                reader.Step(() => interaction._UsedRandomActivator_k__BackingField = roots.Random);
                reader.Step(() => interaction._SpawnedInGameExclusiveItems_k__BackingField = roots.Exclusive);
                reader.Step(() => interaction._UsedIGPSetObjects_k__BackingField = roots.Igp);
                var runtimeSet = reader.Read(() => new Il2CppSystem.Collections.Generic.HashSet<string>());
                foreach (string value in playerDetached.IgpValues)
                    if (!reader.Read(() => runtimeSet.Add(value))) Fail("Detached IGP insertion was not confirmed.");
                reader.Step(() => interaction._usedIGPSetRuntimeSet_k__BackingField = runtimeSet);
                reader.Step(() => interaction._IsCargoBoxUpdated_k__BackingField = false);
                reader.Step(() => interaction._IsSensorDevicesUpdated_k__BackingField = false);
                reader.Step(() => interaction._IsTriggerDevicesUpdated_k__BackingField = false);
                reader.Step(() => interaction._IsFunctionalDevicesUpdated_k__BackingField = false);
                reader.Step(() => interaction._IsInteractionItemsUpdated_k__BackingField = false);
                reader.Step(() => interaction._IsBreakableItemsUpdated_k__BackingField = false);
                reader.Step(() => interaction._IsUsedCrabTrapZoneUpdated_k__BackingField = false);
                reader.Step(() => interaction._IsUsedRandomActivatorUpdated_k__BackingField = false);
                reader.Step(() => interaction._IsSpawnedExclusiveItemsUpdated_k__BackingField = false);
                reader.Step(() => interaction._IsIGPSetObjectsUpdated_k__BackingField = false);
                var interactionDetached = reader.Runtime(interaction, detached);
                RequireSameValues(playerDetached, interactionDetached);
                RequireSameBindings(playerDetached, interactionDetached);
                if (!audit.KnownReferencesDisjoint) Fail("Known interaction references were not proven disjoint.");
                var result = new NativeGuestInteractionShadowResult(originalPlayer, originalInteraction,
                    detachedPlayer, interaction, capturedBaseline, playerDetached, interactionDetached,
                    Environment.CurrentManagedThreadId);
                if (!result.ValidateKnownBinding(requireCloneWindow)) Fail("Known interaction binding validation failed.");
                return result;
            }
            catch (KnownReadFailure) { throw; }
            catch (Exception) { throw new KnownReadFailure("Known interaction preparation could not be completed."); }
        }

        internal static long Pointer(Il2CppObjectBase value)
        {
            if (value == null || value.Pointer == IntPtr.Zero) Fail("A required interaction reference is missing.");
            return value.Pointer.ToInt64();
        }

        internal static void Audit(bool accepted, GuestReferenceAudit audit)
        {
            if (!accepted) Fail("Known interaction reference audit rejected: " + audit.Reason + ".");
        }

        internal static void RequireSameValues(Graph first, Graph second)
        {
            for (int i = 0; i < first.Values.Length; i++)
                if (!string.Equals(first.Values[i], second.Values[i], StringComparison.Ordinal))
                    Fail("Known interaction baseline values differ.");
        }

        internal static void RequireSameBindings(Graph first, Graph second)
        {
            for (int i = 0; i < first.Roots.Length; i++)
                if (first.Roots[i] != second.Roots[i]) Fail("Detached interaction container binding changed.");
        }

        internal static void RequireUnchanged(Graph before, Graph current)
        {
            RequireSameValues(before, current);
            if (!string.Equals(before.Identity, current.Identity, StringComparison.Ordinal))
                Fail("Known interaction references or versions changed.");
        }

        internal static void Fail(string reason) => throw new KnownReadFailure(reason);
        private sealed class KnownReadFailure : InvalidOperationException
        {
            public KnownReadFailure(string message) : base(message) { }
        }

        internal sealed class Graph
        {
            public long RootPointer;
            public string[] Values;
            public long[] Roots;
            public string Identity;
            public string[] IgpValues;
        }

        internal sealed class Containers
        {
            public Il2CppSystem.Collections.Generic.List<PlayerInstalledCargoBoxData> Cargo;
            public Il2CppSystem.Collections.Generic.List<PlayerInstalledSensorDeviceData> Sensor;
            public Il2CppSystem.Collections.Generic.List<PlayerInstalledTriggerDeviceData> Trigger;
            public Il2CppSystem.Collections.Generic.List<PlayerInstalledFunctionalDeviceData> Functional;
            public Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Collections.Generic.List<string>> Items;
            public Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Collections.Generic.List<PlayerInteractionObjectData>> Breakable;
            public Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Collections.Generic.List<PlayerCrabTrapData>> Crab;
            public Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Collections.Generic.List<PlayerRandomActivatorData>> Random;
            public Il2CppSystem.Collections.Generic.Dictionary<string, string> Exclusive;
            public Il2CppSystem.Collections.Generic.List<string> Igp;
        }

        internal sealed class Reader
        {
            private readonly Action _window;
            private readonly int _thread;
            private readonly StringBuilder _identity = new StringBuilder();
            private Action<Il2CppObjectBase> _audit;
            private int _characters;
            private int _storageReads;

            public Reader(Action window, int thread)
            {
                _window = window ?? throw new ArgumentNullException(nameof(window));
                _thread = thread;
            }

            private void Check()
            {
                if (Environment.CurrentManagedThreadId != _thread) Fail("Interaction access requires its captured Unity thread.");
                _window();
                if (Environment.CurrentManagedThreadId != _thread) Fail("Interaction access thread changed.");
            }

            public T Read<T>(Func<T> read) { Check(); T value = read(); Check(); return value; }
            public void Step(Action write) { Check(); write(); Check(); }
            private T SlotRead<T>(Func<T> read)
            {
                if (_storageReads >= MaxStorageReads) Fail("Interaction storage read budget exceeded.");
                _storageReads++;
                return Read(read);
            }

            private long Reference(Il2CppObjectBase value)
            {
                Check(); long pointer = Pointer(value); _audit(value); _identity.Append(pointer).Append(';'); Check();
                return pointer;
            }

            private void Storage(Il2CppObjectBase array, int length)
            {
                if (length < 0 || length > MaxContainerStorage) Fail("Interaction container storage exceeds its bound.");
                // Native empty-array singletons contain no mutable elements.
                // Nonempty arrays are mutable graph references and are audited.
                if (array != null && length != 0) Reference(array);
            }

            private string Token(string value)
            {
                if (value == null) return "-1:";
                if (value.Length > MaxStringCharacters) Fail("An interaction string exceeds its bound.");
                _characters += value.Length;
                if (_characters > MaxSnapshotCharacters) Fail("Interaction snapshot exceeds its bound.");
                return value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
            }

            private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture) + ";";
            private static string Real(float value)
            {
                if (float.IsNaN(value) || float.IsInfinity(value)) Fail("An interaction numeric value is not finite.");
                return Number(BitConverter.SingleToInt32Bits(value));
            }

            // These are native field proxies, not the runtime-invoking public
            // getters. Every root read is guarded, including the final reread.
            private Containers Bindings(PlayerInteractionCache value) => new Containers
            {
                Cargo = Read(() => value._InstalledCargoBoxes_k__BackingField),
                Sensor = Read(() => value._InstalledSensorDevices_k__BackingField),
                Trigger = Read(() => value._InstalledTriggerDevices_k__BackingField),
                Functional = Read(() => value._InstalledFunctionalDevices_k__BackingField),
                Items = Read(() => value._UsedInGameInteractionItems_k__BackingField),
                Breakable = Read(() => value._UsedInGameBreakableItems_k__BackingField),
                Crab = Read(() => value._UsedCrabTrapZone_k__BackingField),
                Random = Read(() => value._UsedRandomActivator_k__BackingField),
                Exclusive = Read(() => value._SpawnedInGameExclusiveItems_k__BackingField),
                Igp = Read(() => value._UsedIGPSetObjects_k__BackingField)
            };

            internal Containers Bindings(SavePlayerData value) => new Containers
            {
                Cargo = Read(() => value._InstalledCargoBoxs_k__BackingField),
                Sensor = Read(() => value._InstalledSensorDevices_k__BackingField),
                Trigger = Read(() => value._InstalledTriggerDevices_k__BackingField),
                Functional = Read(() => value._InstalledFunctionalDevices_k__BackingField),
                Items = Read(() => value._UsedInGameInteractionItems_k__BackingField),
                Breakable = Read(() => value._UsedInGameBreakableItems_k__BackingField),
                Crab = Read(() => value._UsedCrabTrapZone_k__BackingField),
                Random = Read(() => value._UsedRandomActivator_k__BackingField),
                Exclusive = Read(() => value._SpawnedInGameExclusiveItems_k__BackingField),
                Igp = Read(() => value._UsedIGPSetObjects_k__BackingField)
            };

            public Graph Player(SavePlayerData value, Action<Il2CppObjectBase> audit)
            {
                Start(value, audit);
                Containers roots = Bindings(value);
                Graph graph = Capture(roots);
                RequireRoots(roots, Bindings(value));
                graph.RootPointer = Pointer(value);
                graph.Identity = _identity.ToString();
                return graph;
            }

            public Graph Runtime(PlayerInteractionCache value, Action<Il2CppObjectBase> audit, bool requireClean = true)
            {
                Start(value, audit); int dirty = Dirty(value, requireClean);
                Containers roots = Bindings(value);
                Graph graph = Capture(roots);
                var runtime = Read(() => value._usedIGPSetRuntimeSet_k__BackingField);
                string[] items = Set(runtime);
                if (!SameStrings(items, graph.IgpValues)) Fail("Runtime IGP cache differs from its source list.");
                if (Pointer(runtime) != Pointer(Read(() => value._usedIGPSetRuntimeSet_k__BackingField)))
                    Fail("Runtime IGP cache changed during capture.");
                RequireRoots(roots, Bindings(value));
                if (dirty != Dirty(value, requireClean)) Fail("Interaction dirty flags changed during capture.");
                graph.RootPointer = Pointer(value);
                _identity.Append(Number(dirty));
                graph.Identity = _identity.ToString();
                return graph;
            }

            private void Start(Il2CppObjectBase root, Action<Il2CppObjectBase> audit)
            {
                _audit = audit; _identity.Clear(); Reference(root);
            }

            private int Dirty(PlayerInteractionCache value, bool requireClean)
            {
                int mask = (Read(() => value._IsCargoBoxUpdated_k__BackingField) ? 1 : 0) |
                    (Read(() => value._IsSensorDevicesUpdated_k__BackingField) ? 2 : 0) |
                    (Read(() => value._IsTriggerDevicesUpdated_k__BackingField) ? 4 : 0) |
                    (Read(() => value._IsFunctionalDevicesUpdated_k__BackingField) ? 8 : 0) |
                    (Read(() => value._IsInteractionItemsUpdated_k__BackingField) ? 16 : 0) |
                    (Read(() => value._IsBreakableItemsUpdated_k__BackingField) ? 32 : 0) |
                    (Read(() => value._IsUsedCrabTrapZoneUpdated_k__BackingField) ? 64 : 0) |
                    (Read(() => value._IsUsedRandomActivatorUpdated_k__BackingField) ? 128 : 0) |
                    (Read(() => value._IsSpawnedExclusiveItemsUpdated_k__BackingField) ? 256 : 0) |
                    (Read(() => value._IsIGPSetObjectsUpdated_k__BackingField) ? 512 : 0);
                if (requireClean && mask != 0) Fail("Original or detached interaction has uncommitted dirty data.");
                return mask;
            }

            private static Il2CppObjectBase[] Objects(Containers value) => new Il2CppObjectBase[]
                { value.Cargo, value.Sensor, value.Trigger, value.Functional, value.Items, value.Breakable, value.Crab, value.Random, value.Exclusive, value.Igp };

            private static void RequireRoots(Containers first, Containers second)
            {
                var before = Objects(first); var after = Objects(second);
                for (int i = 0; i < before.Length; i++) if (Pointer(before[i]) != Pointer(after[i])) Fail("Interaction root container changed during capture.");
            }

            private Graph Capture(Containers roots)
            {
                var igp = new SortedSet<string>(StringComparer.Ordinal);
                string igpContent = List(roots.Igp, value =>
                {
                    if (value == null) Fail("An IGP cache identifier is missing.");
                    igp.Add(value); return Token(value);
                });
                var graph = new Graph
                {
                    Roots = Array.ConvertAll(Objects(roots), Pointer),
                    Values = new[]
                    {
                        List(roots.Cargo, Cargo), List(roots.Sensor, value => Device(value) + Number((int)Read(() => value._SensorDeviceType_k__BackingField))),
                        List(roots.Trigger, value => Device(value) + Number((int)Read(() => value._TriggerDeviceType_k__BackingField))),
                        List(roots.Functional, value => Device(value) + Number((int)Read(() => value._FunctionalDeviceType_k__BackingField))),
                        Dictionary(roots.Items, value => List(value, Token)), Dictionary(roots.Breakable, value => List(value, Breakable)),
                        Dictionary(roots.Crab, value => List(value, Crab)), Dictionary(roots.Random, value => List(value, Random)),
                        Dictionary(roots.Exclusive, Token), igpContent
                    },
                    IgpValues = new string[igp.Count]
                };
                igp.CopyTo(graph.IgpValues);
                return graph;
            }

            private string List<T>(Il2CppSystem.Collections.Generic.List<T> list, Func<T, string> item)
            {
                Reference(list);
                int size = Read(() => list._size), version = Read(() => list._version);
                var array = Read(() => list._items);
                if (array == null) Fail("An interaction list storage array is missing.");
                int length = Read(() => array.Length);
                Storage(array, length);
                if (size < 0 || size > length) Fail("An interaction list has an invalid size.");
                _identity.Append(Number(Pointer(array))).Append(Number(size)).Append(Number(version)).Append(Number(length));
                var output = new StringBuilder().Append(Number(size));
                for (int i = 0; i < length; i++)
                {
                    T value = SlotRead(() => array[i]);
                    if (i >= size)
                    {
                        if ((object)value != null) Fail("An unused interaction list slot retains a reference.");
                        continue;
                    }
                    if (value is Il2CppObjectBase native) Reference(native);
                    else if ((object)value == null && typeof(T) != typeof(string)) Fail("An interaction row is missing.");
                    string content = item(value);
                    output.Append(Number(content.Length)).Append(content);
                }
                if (size != Read(() => list._size) || version != Read(() => list._version) ||
                    Pointer(array) != Pointer(Read(() => list._items))) Fail("An interaction list changed during capture.");
                return output.ToString();
            }

            private string Dictionary<T>(Il2CppSystem.Collections.Generic.Dictionary<string, T> dictionary, Func<T, string> item)
            {
                Reference(dictionary);
                int count = Read(() => dictionary._count), free = Read(() => dictionary._freeCount), version = Read(() => dictionary._version);
                int freeList = Read(() => dictionary._freeList);
                var entries = Read(() => dictionary._entries); var buckets = Read(() => dictionary._buckets);
                int length = entries == null ? 0 : Read(() => entries.Length);
                int bucketLength = buckets == null ? 0 : Read(() => buckets.Length);
                Storage(entries, length); Storage(buckets, bucketLength);
                if (count < 0 || count > length || free < 0 || free > count ||
                    (free > 0 && (freeList < 0 || freeList >= count)) ||
                    (length == 0) != (bucketLength == 0) || (count > 0 && bucketLength == 0))
                    Fail("An interaction dictionary has an invalid storage shape.");
                _identity.Append(Number(entries == null ? 0 : Pointer(entries))).Append(Number(buckets == null ? 0 : Pointer(buckets)))
                    .Append(Number(count)).Append(Number(free)).Append(Number(freeList)).Append(Number(version)).Append(Number(length)).Append(Number(bucketLength));
                for (int i = 0; i < bucketLength; i++)
                {
                    int slot = SlotRead(() => buckets[i]);
                    if (slot < -1 || slot > count) Fail("An interaction dictionary bucket is invalid.");
                    _identity.Append(Number(slot));
                }
                var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
                for (int i = 0; i < length; i++)
                {
                    // Entry is a native ValueType wrapper; its temporary box is
                    // not a persistent graph reference. Storage/value refs are.
                    var entry = SlotRead(() => entries[i]);
                    if (entry == null) Fail("An interaction dictionary entry could not be read.");
                    int hash = Read(() => entry.hashCode), next = Read(() => entry.next);
                    string key = Read(() => entry.key); T value = Read(() => entry.value);
                    _identity.Append(Number(hash)).Append(Number(next));
                    if (i >= count || hash < 0)
                    {
                        if (key != null || (object)value != null) Fail("An unused interaction dictionary slot retains a reference.");
                        continue;
                    }
                    if (key == null || next < -1 || next >= count || values.ContainsKey(key))
                        Fail("An interaction dictionary entry is invalid or ambiguous.");
                    string content = item(value);
                    values.Add(key, Token(key) + Number(content.Length) + content);
                }
                if (values.Count != count - free) Fail("An interaction dictionary live count differs.");
                if (count != Read(() => dictionary._count) || free != Read(() => dictionary._freeCount) ||
                    version != Read(() => dictionary._version) || freeList != Read(() => dictionary._freeList) ||
                    (entries == null ? 0 : Pointer(entries)) != OptionalPointer(Read(() => dictionary._entries)) ||
                    (buckets == null ? 0 : Pointer(buckets)) != OptionalPointer(Read(() => dictionary._buckets)))
                    Fail("An interaction dictionary changed during capture.");
                return Number(values.Count) + string.Concat(values.Values);
            }

            private string[] Set(Il2CppSystem.Collections.Generic.HashSet<string> set)
            {
                Reference(set);
                int count = Read(() => set._count), last = Read(() => set._lastIndex), version = Read(() => set._version);
                int freeList = Read(() => set._freeList);
                var slots = Read(() => set._slots); var buckets = Read(() => set._buckets);
                int length = slots == null ? 0 : Read(() => slots.Length);
                int bucketLength = buckets == null ? 0 : Read(() => buckets.Length);
                Storage(slots, length); Storage(buckets, bucketLength);
                if (count < 0 || count > last || last > length || last < 0 ||
                    (length == 0) != (bucketLength == 0) || (last > 0 && bucketLength == 0) || freeList < -1 || freeList >= Math.Max(1, last))
                    Fail("An interaction hash cache has an invalid storage shape.");
                _identity.Append(Number(OptionalPointer(slots))).Append(Number(OptionalPointer(buckets))).Append(Number(count))
                    .Append(Number(last)).Append(Number(version)).Append(Number(freeList)).Append(Number(length)).Append(Number(bucketLength));
                for (int i = 0; i < bucketLength; i++)
                {
                    int slot = SlotRead(() => buckets[i]);
                    if (slot < 0 || slot > last) Fail("An interaction hash bucket is invalid.");
                    _identity.Append(Number(slot));
                }
                var values = new SortedSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < length; i++)
                {
                    var slot = SlotRead(() => slots[i]);
                    if (slot == null) Fail("An interaction hash slot could not be read.");
                    int hash = Read(() => slot.hashCode), next = Read(() => slot.next);
                    string value = Read(() => slot.value);
                    _identity.Append(Number(hash)).Append(Number(next));
                    if (i >= last || hash < 0)
                    {
                        if (value != null) Fail("An unused interaction hash slot retains a value.");
                        continue;
                    }
                    if (value == null || next < -1 || next >= last || !values.Add(value)) Fail("An interaction hash value is invalid or ambiguous.");
                    Token(value);
                }
                if (values.Count != count) Fail("An interaction hash live count differs.");
                if (count != Read(() => set._count) || last != Read(() => set._lastIndex) || version != Read(() => set._version) ||
                    freeList != Read(() => set._freeList) || OptionalPointer(slots) != OptionalPointer(Read(() => set._slots)) ||
                    OptionalPointer(buckets) != OptionalPointer(Read(() => set._buckets))) Fail("An interaction hash cache changed during capture.");
                var result = new string[values.Count]; values.CopyTo(result); return result;
            }

            private static long OptionalPointer(Il2CppObjectBase value) => value == null ? 0 : Pointer(value);
            private static bool SameStrings(string[] first, string[] second)
            {
                if (first.Length != second.Length) return false;
                for (int i = 0; i < first.Length; i++) if (!string.Equals(first[i], second[i], StringComparison.Ordinal)) return false;
                return true;
            }

            private string Device(PlayerInstalledDeviceDataBase value) =>
                Number((int)Read(() => value._DeviceSubHelperType_k__BackingField)) + Number((int)Read(() => value._DeviceType_k__BackingField)) +
                Token(Read(() => value._DeviceInstalledSceneType_k__BackingField)) + Token(Read(() => value._DeviceUID_k__BackingField)) +
                Number(Read(() => value._DeviceTID_k__BackingField)) + Real(Read(() => value._DevicePositionX_k__BackingField)) +
                Real(Read(() => value._DevicePositionY_k__BackingField)) + Real(Read(() => value._DevicePositionZ_k__BackingField)) +
                Number(Read(() => value._SavedSlotIndex_k__BackingField));

            private string Cargo(PlayerInstalledCargoBoxData value) => Device(value) + Real(Read(() => value._Weight_k__BackingField)) +
                List(Read(() => value._CargoProductList_k__BackingField), slot => Number(Read(() => slot.itemID)) + Number(Read(() => slot.itemGrade)));
            private string Breakable(PlayerInteractionObjectData value) => Token(Read(() => value._UniqueID_k__BackingField)) +
                Number(Read(() => value._RemainCount_k__BackingField)) + Number(Read(() => value._MaxCount_k__BackingField));
            private string Crab(PlayerCrabTrapData value) => Token(Read(() => value._UniqueID_k__BackingField)) +
                Read(() => value._SetUpTime_k__BackingField)._dateData.ToString(CultureInfo.InvariantCulture) + ";" +
                Number(Read(() => value._BaitLevel_k__BackingField)) + Number((int)Read(() => value._TrapState_k__BackingField));
            private string Random(PlayerRandomActivatorData value) => Token(Read(() => value._UniqueID_k__BackingField)) +
                Number(Read(() => value._RandIdx_k__BackingField));
        }
    }

    // Captured before Serialize/Deserialize. These are bounded owned CLR
    // snapshots of declared fields, not proof of the entire source baseline.
    internal sealed class NativeGuestInteractionBaseline
    {
        private readonly SavePlayerData _player;
        private readonly PlayerInteractionCache _interaction;
        private bool _busy, _readFailed;
        internal NativeGuestInteractionShadow.Graph PlayerBefore { get; }
        internal NativeGuestInteractionShadow.Graph InteractionBefore { get; }
        internal int UnityThreadId { get; }
        public bool ReadFailureLatched { get; private set; }
        public bool SourceBaselineVerified => false;
        public bool CompleteGraphVerified => false;
        public bool NativePermission => false;
        public bool GuestStateIsolated => false;

        internal NativeGuestInteractionBaseline(SavePlayerData player, PlayerInteractionCache interaction,
            NativeGuestInteractionShadow.Graph playerBefore, NativeGuestInteractionShadow.Graph interactionBefore, int thread)
        {
            _player = player; _interaction = interaction; PlayerBefore = playerBefore;
            InteractionBefore = interactionBefore; UnityThreadId = thread;
        }

        public bool ConfirmOriginalKnownGraph(Action requireOriginalReadWindow)
        {
            if (_busy || Environment.CurrentManagedThreadId != UnityThreadId || requireOriginalReadWindow == null) return Reject();
            _busy = true; _readFailed = false;
            try
            {
                var reader = new NativeGuestInteractionShadow.Reader(requireOriginalReadWindow, UnityThreadId);
                var audit = new GuestReferenceAudit();
                Action<Il2CppObjectBase> original = value => NativeGuestInteractionShadow.Audit(audit.AddOriginal(NativeGuestInteractionShadow.Pointer(value)), audit);
                var player = reader.Player(_player, original);
                var runtime = reader.Runtime(_interaction, original);
                NativeGuestInteractionShadow.RequireUnchanged(PlayerBefore, player);
                NativeGuestInteractionShadow.RequireUnchanged(InteractionBefore, runtime);
                requireOriginalReadWindow();
                return !_readFailed;
            }
            catch (Exception) { return Reject(); }
            finally { _busy = false; }
        }

        private bool Reject() { ReadFailureLatched = true; _readFailed = true; return false; }
    }

    internal sealed class NativeGuestInteractionShadowResult
    {
        private readonly SavePlayerData _originalPlayer, _detachedPlayer;
        private readonly PlayerInteractionCache _originalInteraction;
        private readonly NativeGuestInteractionBaseline _baseline;
        private readonly NativeGuestInteractionShadow.Graph _playerBefore, _interactionBefore, _playerDetached, _interactionDetached;
        private readonly int _thread;
        private bool _busy, _failed, _readFailed, _knownReferencesDisjoint;
        public PlayerInteractionCache Interaction { get; }
        public bool KnownReferencesDisjoint
        {
            get => _knownReferencesDisjoint && !_failed && !_baseline.ReadFailureLatched;
            private set => _knownReferencesDisjoint = value;
        }
        public bool SourceBaselineVerified => false;
        public bool CompleteGraphVerified => false;
        public bool NativeCloneAbiVerified => false;
        public bool GuestStateIsolated => false;
        public bool NativePermission => false;
        public bool WorldAuthority => false;
        public bool CargoAuthority => false;
        public string Reason { get; private set; } = "Known interaction binding has not been read back.";

        internal NativeGuestInteractionShadowResult(SavePlayerData originalPlayer, PlayerInteractionCache originalInteraction,
            SavePlayerData detachedPlayer, PlayerInteractionCache interaction, NativeGuestInteractionBaseline baseline, NativeGuestInteractionShadow.Graph playerDetached,
            NativeGuestInteractionShadow.Graph interactionDetached, int thread)
        {
            _originalPlayer = originalPlayer; _originalInteraction = originalInteraction; _detachedPlayer = detachedPlayer;
            Interaction = interaction; _baseline = baseline; _playerBefore = baseline.PlayerBefore; _interactionBefore = baseline.InteractionBefore;
            _playerDetached = playerDetached; _interactionDetached = interactionDetached; _thread = thread;
        }

        // Window is supplied by the bound backend. During staged installation
        // it checks lease/fence/managers/original scalars, not all-original roots.
        // No business constructors, Add, setters, Sync or root writes occur
        // here. Native ValueType array access can allocate temporary boxes.
        public bool ValidateKnownBinding(Action requireCloneWindow) => Validate(requireCloneWindow, prepared: true);

        // Active detached data can legitimately change. This rereads only the
        // declared current graph; it never repairs bindings or clears dirty flags.
        public bool ValidateKnownReferences(Action requireInteractionBindingWindow) => Validate(requireInteractionBindingWindow, prepared: false);

        // Recovery can inspect the frozen original graph even after a latched
        // detached failure. Success never clears that failure or grants authority.
        public bool ConfirmOriginalKnownGraph(Action requireOriginalReadWindow)
        {
            if (_busy || Environment.CurrentManagedThreadId != _thread || requireOriginalReadWindow == null) return Reject();
            _busy = true; _readFailed = false;
            try
            {
                if (!_baseline.ConfirmOriginalKnownGraph(requireOriginalReadWindow)) return Reject();
                return !_readFailed;
            }
            catch (Exception) { return Reject(); }
            finally { _busy = false; }
        }

        private bool Validate(Action requireCloneWindow, bool prepared)
        {
            if (_failed) return false;
            if (_baseline.ReadFailureLatched) return Reject();
            if (_busy || Environment.CurrentManagedThreadId != _thread || requireCloneWindow == null) return Reject();
            _busy = true;
            try
            {
                var reader = new NativeGuestInteractionShadow.Reader(requireCloneWindow, _thread);
                var audit = new GuestReferenceAudit();
                Action<Il2CppObjectBase> original = value => NativeGuestInteractionShadow.Audit(audit.AddOriginal(NativeGuestInteractionShadow.Pointer(value)), audit);
                Action<Il2CppObjectBase> detached = value => NativeGuestInteractionShadow.Audit(audit.AddDetached(NativeGuestInteractionShadow.Pointer(value)), audit);
                var player = reader.Player(_originalPlayer, original);
                var runtime = reader.Runtime(_originalInteraction, original);
                NativeGuestInteractionShadow.RequireUnchanged(_playerBefore, player);
                NativeGuestInteractionShadow.RequireUnchanged(_interactionBefore, runtime);
                NativeGuestInteractionShadow.Audit(audit.BeginDetached(), audit);
                var shadow = reader.Player(_detachedPlayer, detached);
                var shadowRuntime = reader.Runtime(Interaction, detached, requireClean: prepared);
                if (_playerDetached.RootPointer != shadow.RootPointer || _interactionDetached.RootPointer != shadowRuntime.RootPointer)
                    NativeGuestInteractionShadow.Fail("Detached interaction parent identity changed.");
                if (prepared)
                {
                    NativeGuestInteractionShadow.RequireUnchanged(_playerDetached, shadow);
                    NativeGuestInteractionShadow.RequireUnchanged(_interactionDetached, shadowRuntime);
                    NativeGuestInteractionShadow.RequireSameValues(player, shadow);
                }
                NativeGuestInteractionShadow.RequireSameValues(player, runtime);
                NativeGuestInteractionShadow.RequireSameValues(shadow, shadowRuntime);
                NativeGuestInteractionShadow.RequireSameBindings(shadow, shadowRuntime);
                requireCloneWindow();
                if (_failed || !audit.KnownReferencesDisjoint) return Reject();
                KnownReferencesDisjoint = true;
                Reason = "Only declared interaction references and bindings were read back; complete isolation is unverified.";
                return true;
            }
            catch (Exception) { return Reject(); }
            finally { _busy = false; }
        }

        private bool Reject()
        {
            _failed = true; _readFailed = true; KnownReferencesDisjoint = false;
            Reason = "Known interaction binding changed or could not be read; the failure is latched.";
            return false;
        }
    }
}
