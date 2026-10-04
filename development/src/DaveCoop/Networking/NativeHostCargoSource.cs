using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BepInEx.Logging;
using CodeStage.AntiCheat.ObscuredTypes;
using DaveCoop.Core.Cargo;
using DR.Save;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;
using UnityEngine.SceneManagement;
using BagDictionary = Il2CppSystem.Collections.Generic.Dictionary<string, LootBoxSlot>;
using ReadOnlyBag = Il2CppSystem.Collections.Generic.IReadOnlyDictionary<string, LootBoxSlot>;

namespace DaveCoop.Networking
{
    internal sealed class NativeHostCargoSlot
    {
        public string KeyFingerprint { get; }
        public int? ItemId { get; }
        public int? RawGrade { get; }
        public int? FinalGrade { get; }
        public int? TotalCount { get; }
        public int ItemType { get; }
        public int AutoLiftedType { get; }
        public int GetTimeCount { get; }
        public bool CandidatesAvailable => ItemId.HasValue && RawGrade.HasValue && TotalCount.HasValue;
        public bool FinalReturnGradeVerified => false;
        public bool NativePermission => false;

        internal NativeHostCargoSlot(string key, int? item, int? grade, int? final, int? count, int type, int lift, int times)
        { KeyFingerprint = key; ItemId = item; RawGrade = grade; FinalGrade = final; TotalCount = count;
            ItemType = type; AutoLiftedType = lift; GetTimeCount = times; }
    }

    // Owned CLR values only. The private seal authenticates this producer's
    // sample, not a native receipt. Encrypted fields, pointers and wrappers are
    // never retained inside this DTO or sent to the transport.
    internal sealed class NativeHostCargoSnapshot
    {
        private readonly NativeHostCargoSlot[] _slots;
        internal object Seal { get; }
        public Guid RootIdentity { get; }
        public long Generation { get; }
        public long Revision { get; }
        public string ExpeditionId => RootIdentity.ToString("N");
        public long DiveOrdinal => Generation;
        public double HostCapacity { get; }
        public double HostWeight { get; }
        public double CapacityKg => HostCapacity;
        public double WeightKg => HostWeight;
        public float WeightParameter { get; }
        public float OverloadedThreshold { get; }
        public string Fingerprint { get; }
        public int SceneHandle { get; }
        public string SceneKey { get; }
        public int NativeSceneType { get; }
        public int SlotsCount => _slots.Length;
        public NativeHostCargoSlot[] Slots => (NativeHostCargoSlot[])_slots.Clone();
        public bool NaturalDiveEntryObserved => true;
        public bool SamplesMatch => true;
        public bool NativeFieldAbiVerified => false;
        public bool NativeBagInventoryComplete => false;
        public bool NormalReturnLoadedVerified => false;
        public bool CaptureConfirmed => false;
        public bool CargoPermission => false;

        internal NativeHostCargoSnapshot(object seal, Guid identity, long generation, long revision, ImageValues image)
        {
            Seal = seal; RootIdentity = identity; Generation = generation; Revision = revision;
            HostCapacity = image.Capacity; HostWeight = image.Weight; WeightParameter = image.Parameter;
            OverloadedThreshold = image.Threshold; Fingerprint = image.Fingerprint;
            SceneHandle = image.SceneHandle; SceneKey = image.SceneKey; NativeSceneType = image.SceneType;
            _slots = (NativeHostCargoSlot[])image.Slots.Clone();
        }

        internal sealed class ImageValues
        {
            internal float Weight, Capacity, Parameter, Threshold;
            internal string Fingerprint, SceneKey;
            internal int SceneHandle, SceneType;
            internal NativeHostCargoSlot[] Slots;
        }
    }

    // This is an observer of the real host bag, never an employee bag or bag
    // constructor. A natural Load, natural getter result and natural StartDiving
    // must pair to the same retained roots before an active sample can be minted.
    internal sealed class NativeHostCargoSource : IDisposable
    {
        public const int ExpectedTargets = 4;
        public const int MaxContexts = 128;
        public const int MaxNaturalCalls = 8192;
        public const int MaxGenerations = 64;
        public const int MaxSlots = 2048;
        public const int MaxDictionaryStorage = 4096;
        public const int MaxTimeListStorage = 32;
        public const int MaxStorageVisits = 65536;
        public const int MaxSteps = 524288;
        public const int MaxStringCharacters = 512;
        public const int MaxSampleStringCharacters = 1024 * 1024;
        public const int MaxRetainedReferences = 16384;

        private enum Kind { Load, Dictionary, Start, End }
        private sealed class Root
        {
            internal InGameManager Manager;
            internal PlayerCharacter Player;
            internal LootBox Bag;
            internal BagDictionary Dictionary;
            internal SceneContext Context;
            internal SaveSystem System;
            internal SaveSystemGameDataManager GameManager;
            internal SaveSystemPlayerDataManager PlayerManager;
            internal SaveSystemPhotoDataManager PhotoManager;
            internal SaveSystemUserOptionManager OptionManager;
            internal SaveData Game;
            internal Il2CppObjectBase[] SaveRoots;
            internal string BagIdentity, ManagerIdentity, PlayerIdentity;
            internal int SceneHandle, SceneType;
            internal string SceneKey;
        }
        private sealed class Context
        {
            internal Kind Kind;
            internal Root Before, After;
            internal bool Posted, OriginalReturned;
            internal long Serial;
        }
        private sealed class RetainedReference
        {
            internal Il2CppObjectBase Object;
            internal IntPtr Handle;
        }

        private static NativeHostCargoSource _active;
        private static readonly List<NativeHostCargoSource> RetainedOwners = new List<NativeHostCargoSource>();
        private static int _installationAttempted;
        private readonly LocalAvatarCapture _local;
        private readonly int _thread;
        private readonly Func<bool> _ownerCurrent;
        private readonly ManualLogSource _logger;
        private readonly object _seal = new object();
        private readonly HashSet<Context> _contexts = new HashSet<Context>();
        private readonly Dictionary<long, RetainedReference> _references = new Dictionary<long, RetainedReference>();
        private Harmony _harmony;
        private Root _loaded, _dictionaryBinding, _started, _pendingStart;
        private Guid _rootIdentity;
        private long _generation, _revision, _calls, _faultSerial, _operationSerial;
        private string _lastFingerprint;
        private bool _installed, _busy, _failed, _stopped, _activeDive, _endObserved, _requiresOwner;
        private int _steps, _storageVisits, _characters, _cleanupAttempted;
        public bool Healthy => _installed && !_failed && !_stopped;
        public bool Installed => _installed;
        public bool Failed => _failed;
        public bool ActiveDive => Healthy && _activeDive;
        public bool EndObserved => _endObserved;
        public long SourceGeneration => _generation;
        public int RetainedReferences => _references.Count;
        public bool NativeFieldAbiVerified => false;
        public bool NormalReturnLoadedVerified => false;
        public bool CargoPermission => false;
        public string Status { get; private set; } = "Host cargo source is not installed.";

        public NativeHostCargoSource(LocalAvatarCapture local, int unityThreadId, Func<bool> ownerCurrent, ManualLogSource logger)
        {
            _local = local ?? throw new ArgumentNullException(nameof(local));
            if (unityThreadId < 1) throw new ArgumentOutOfRangeException(nameof(unityThreadId));
            _thread = unityThreadId; _ownerCurrent = ownerCurrent ?? throw new ArgumentNullException(nameof(ownerCurrent));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void Install()
        {
            if (_installed || _stopped || _failed) return;
            if (Environment.CurrentManagedThreadId != _thread || Interlocked.CompareExchange(ref _installationAttempted, 1, 0) != 0)
            { Fail("Host cargo source installation is single-use on its confirmed thread."); return; }
            RetainedOwners.Add(this); _active = this;
            try
            {
                _harmony = new Harmony("DaveCoop.HostCargoSource." + Guid.NewGuid().ToString("N"));
                Patch(typeof(LootBox), "Load", typeof(ILootBoxEventListener), nameof(LoadBefore), nameof(VoidAfter));
                Patch(typeof(LootBox), "get_m_Box", null, nameof(DictionaryBefore), nameof(DictionaryAfter), typeof(ReadOnlyBag));
                Patch(typeof(PlayerCharacter), "StartDiving", null, nameof(StartBefore), nameof(VoidAfter));
                Patch(typeof(PlayerCharacter), "EndDiving", null, nameof(EndBefore), nameof(VoidAfter));
                _installed = true; Status = "Waiting for natural bag load, dictionary return and dive start.";
                _logger.LogInfo("HOST_CARGO_SOURCE_READY Targets=4 NativeExecuted=False NativeABI=False CargoPermission=False");
            }
            catch (Exception) { Fail("Host cargo source installation is incomplete; its owner is retained."); }
        }

        private void Patch(Type owner, string name, Type parameter, string prefix, string postfix, Type result = null)
        {
            MethodInfo method = owner.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly, null, parameter == null ? Type.EmptyTypes : new[] { parameter }, null);
            if (method == null || method.IsStatic || method.ContainsGenericParameters || method.ReturnType != (result ?? typeof(void)))
                throw new InvalidOperationException("A host cargo declaration is unavailable.");
            _harmony.Patch(method, prefix: new HarmonyMethod(typeof(NativeHostCargoSource), prefix),
                postfix: new HarmonyMethod(typeof(NativeHostCargoSource), postfix),
                finalizer: new HarmonyMethod(typeof(NativeHostCargoSource), nameof(Finally)));
        }

        private static void LoadBefore(LootBox __instance, out Context __state) => __state = _active?.Before(Kind.Load, __instance);
        private static void DictionaryBefore(LootBox __instance, out Context __state) => __state = _active?.Before(Kind.Dictionary, __instance);
        private static void StartBefore(PlayerCharacter __instance, out Context __state) => __state = _active?.Before(Kind.Start, __instance);
        private static void EndBefore(PlayerCharacter __instance, out Context __state) => __state = _active?.Before(Kind.End, __instance);
        private static void VoidAfter(Context __state, bool __runOriginal) => _active?.After(__state, __runOriginal, null);
        private static void DictionaryAfter(Context __state, ReadOnlyBag __result, bool __runOriginal)
            => _active?.After(__state, __runOriginal, __result);
        // No native reads or replacement exception: commit only the already
        // frozen post-call values when the original call returned normally.
        private static void Finally(Context __state, Exception __exception) => _active?.Finish(__state, __exception != null);

        private Context Before(Kind kind, Il2CppObjectBase instance)
        {
            if (!_installed || _stopped || _failed) return null;
            if (!Begin(false)) return null;
            try
            {
                if (++_calls > MaxNaturalCalls || _contexts.Count >= MaxContexts) throw new SourceFailure();
                Root root = ReadRoot(kind == Kind.Start || kind == Kind.End);
                if (kind == Kind.Load || kind == Kind.Dictionary)
                {
                    if (Pointer(instance) != Pointer(root.Bag)) return null;
                }
                else
                {
                    if (Pointer(instance) != Pointer(root.Player)) return null;
                    if (kind == Kind.Start && (_loaded == null || !SameBag(_loaded, root) ||
                        _loaded.ManagerIdentity != root.ManagerIdentity)) return null;
                    if (kind == Kind.End)
                    {
                        Root entry = _activeDive ? _started : _pendingStart;
                        if (entry == null || !SameBag(entry, root) || entry.PlayerIdentity != root.PlayerIdentity) return null;
                    }
                }
                var context = new Context { Kind = kind, Before = root, Serial = _operationSerial };
                _contexts.Add(context);
                if (kind == Kind.End) { _activeDive = false; _pendingStart = null; _endObserved = true;
                    Status = "Natural EndDiving observed; no return or save receipt."; }
                return context;
            }
            catch (Exception) { Fail("A natural host cargo prefix could not be frozen."); return null; }
            finally { _busy = false; }
        }

        private void After(Context context, bool originalReturned, ReadOnlyBag dictionary)
        {
            if (context != null && Environment.CurrentManagedThreadId != _thread)
            { Fail("A natural host cargo postfix changed thread."); return; }
            if (context == null || !_contexts.Contains(context) || _failed || _stopped) return;
            if (!Begin(false)) return;
            try
            {
                if (context.Serial != Interlocked.Read(ref _faultSerial) || context.Posted) throw new SourceFailure();
                context.Posted = true; context.OriginalReturned = originalReturned;
                if (!originalReturned) { Fail("A natural host cargo original was skipped."); return; }
                Root root = ReadRoot(context.Kind == Kind.Start || context.Kind == Kind.End ||
                    context.Kind == Kind.Dictionary && _pendingStart != null);
                if (!SameBag(context.Before, root) || context.Before.ManagerIdentity != root.ManagerIdentity ||
                    context.Before.PlayerIdentity != root.PlayerIdentity && context.Kind != Kind.Load && context.Kind != Kind.Dictionary)
                    throw new SourceFailure();
                if (context.Kind == Kind.Dictionary)
                {
                    if (dictionary == null || Pointer(dictionary) != Pointer(root.Dictionary))
                        throw new SourceFailure();
                    Exact<BagDictionary>(dictionary); Keep(dictionary);
                }
                context.After = root;
            }
            catch (Exception) { Fail("A natural host cargo postfix changed its fixed roots."); }
            finally { _busy = false; }
        }

        private void Finish(Context context, bool originalException)
        {
            if (context != null && Environment.CurrentManagedThreadId != _thread)
            { Fail("A natural host cargo finalizer changed thread."); return; }
            if (context != null && _busy)
            { Fail("A natural host cargo finalizer reentered a source read."); return; }
            if (context == null || !_contexts.Remove(context)) return;
            if (originalException) { Fail("A natural host cargo original threw; its source is retained."); return; }
            if (_failed || _stopped || context.Serial != Interlocked.Read(ref _faultSerial) || !context.Posted || !context.OriginalReturned || context.After == null) return;
            switch (context.Kind)
            {
                case Kind.Load:
                    if (_activeDive && !SameBag(_started, context.After))
                    { Fail("An open natural dive changed its retained host bag roots."); return; }
                    _loaded = context.After;
                    break;
                case Kind.Dictionary:
                    _dictionaryBinding = context.After;
                    if (_pendingStart != null && SameBag(_pendingStart, context.After) &&
                        _pendingStart.ManagerIdentity == context.After.ManagerIdentity &&
                        _pendingStart.PlayerIdentity == context.After.PlayerIdentity) ActivateStart(_pendingStart);
                    break;
                case Kind.Start:
                    _pendingStart = context.After;
                    if (_dictionaryBinding == null || !SameBag(_dictionaryBinding, context.After))
                    { Status = "Natural dive start lacks an observed original bag dictionary return."; return; }
                    ActivateStart(context.After);
                    break;
                case Kind.End: Status = "Natural EndDiving returned; normal return and save remain unverified."; break;
            }
        }

        private void ActivateStart(Root root)
        {
            if (_activeDive && !SameBag(_started, root))
            { Fail("A repeated natural dive start changed its original bag roots."); return; }
            if (!_activeDive)
            {
                if (_generation >= MaxGenerations) { Fail("Host cargo source generation budget exhausted."); return; }
                _generation++; _rootIdentity = Guid.NewGuid(); _revision = 0; _lastFingerprint = null;
            }
            _started = root; _pendingStart = null; _activeDive = true; _endObserved = false;
            Status = "Natural host dive source is bound; active snapshots require fresh host ownership.";
        }

        public bool TryReadActiveDive(out NativeHostCargoSnapshot snapshot)
        {
            snapshot = null;
            if (!Healthy || !_activeDive || _started == null || _dictionaryBinding == null) return false;
            if (!Begin(true)) return false;
            try
            {
                Root firstRoot = ReadActiveRoot();
                NativeHostCargoSnapshot.ImageValues first = ReadImage(firstRoot);
                Root secondRoot = ReadActiveRoot();
                NativeHostCargoSnapshot.ImageValues second = ReadImage(secondRoot);
                Root finalRoot = ReadActiveRoot();
                if (!SameBag(firstRoot, secondRoot) || firstRoot.PlayerIdentity != secondRoot.PlayerIdentity ||
                    first.Fingerprint != second.Fingerprint || !SameBag(secondRoot, finalRoot) ||
                    secondRoot.ManagerIdentity != finalRoot.ManagerIdentity || secondRoot.PlayerIdentity != finalRoot.PlayerIdentity)
                    throw new SourceFailure();
                Check(true);
                if (_lastFingerprint != second.Fingerprint)
                {
                    if (_revision == long.MaxValue) throw new SourceFailure();
                    _revision++; _lastFingerprint = second.Fingerprint;
                }
                snapshot = new NativeHostCargoSnapshot(_seal, _rootIdentity, _generation, _revision, second);
                return true;
            }
            catch (Exception) { Fail("Host bag samples are unavailable or changed during their bounded read."); return false; }
            finally { _busy = false; }
        }

        public bool IsCurrent(NativeHostCargoSnapshot snapshot) => TryConfirmUnchanged(snapshot, out _);

        // A cheap root/lifecycle check for each guarded native step. It does not
        // read slots or assert that a previous bag fingerprint is unchanged.
        public bool IsSameActiveDive(NativeHostCargoSnapshot snapshot)
        {
            if (!OwnsActiveSnapshot(snapshot) || !Begin(true)) return false;
            try
            {
                Root first = ReadActiveRoot(), second = ReadActiveRoot();
                if (!SameActiveRoot(first, second) || second.SceneHandle != snapshot.SceneHandle ||
                    second.SceneKey != snapshot.SceneKey || second.SceneType != snapshot.NativeSceneType) throw new SourceFailure();
                Check(); return true;
            }
            catch (Exception) { Fail("Host dive roots changed during their bounded read."); return false; }
            finally { _busy = false; }
        }

        public bool TryConfirmUnchanged(NativeHostCargoSnapshot before, out NativeHostCargoSnapshot current)
        {
            current = null;
            if (!OwnsActiveSnapshot(before) || !TryReadActiveDive(out NativeHostCargoSnapshot read)) return false;
            if (read.Generation != before.Generation || read.RootIdentity != before.RootIdentity ||
                read.Fingerprint != before.Fingerprint || read.SceneHandle != before.SceneHandle || read.SceneKey != before.SceneKey) return false;
            current = read;
            return true; // Equality of the sampled known fields, never proof of all native side effects.
        }

        public bool TryReadCommitRoots(NativeHostCargoSnapshot snapshot, out SaveData save, out LootBox bag)
        {
            save = null; bag = null;
            if (!OwnsActiveSnapshot(snapshot) || !Begin(true)) return false;
            try
            {
                Root first = ReadActiveRoot(), second = ReadActiveRoot();
                if (!SameActiveRoot(first, second) || second.SceneHandle != snapshot.SceneHandle ||
                    second.SceneKey != snapshot.SceneKey || second.SceneType != snapshot.NativeSceneType) throw new SourceFailure();
                Check(); save = second.Game; bag = second.Bag;
                return true;
            }
            catch (Exception) { Fail("Host cargo commit roots changed during their bounded read."); save = null; bag = null; return false; }
            finally { _busy = false; }
        }

        private bool OwnsActiveSnapshot(NativeHostCargoSnapshot snapshot)
            => snapshot != null && ReferenceEquals(snapshot.Seal, _seal) && Healthy && _activeDive &&
                snapshot.Generation == _generation && snapshot.RootIdentity == _rootIdentity;

        private Root ReadActiveRoot()
        {
            Root root = ReadRoot(true);
            if (!_activeDive || !SameBag(_started, root) || _started.ManagerIdentity != root.ManagerIdentity ||
                _started.PlayerIdentity != root.PlayerIdentity || !SameBag(_dictionaryBinding, root)) throw new SourceFailure();
            if (ReferenceEquals(_local.Manager, null) || ReferenceEquals(_local.Player, null) || Pointer(_local.Manager) != Pointer(root.Manager) ||
                Pointer(_local.Player) != Pointer(root.Player) || _local.SceneHandle != root.SceneHandle) throw new SourceFailure();
            return root;
        }

        private Root ReadRoot(bool needPlayer)
        {
            var root = new Root
            {
                Manager = Read(() => Singleton<InGameManager>._instance),
                System = Read(() => Singleton<SaveSystem>._instance),
                Bag = Read(() => SingletonNoMono<LootBox>._s_Instance_k__BackingField),
                Context = Read(() => SingletonNoMono<SceneContext>._s_Instance_k__BackingField)
            };
            Required(root.Manager); Required(root.System); Required(root.Bag); Required(root.Context);
            Exact<LootBox>(root.Bag);
            if (!Read(() => root.System._IsInitialized_k__BackingField) || !Read(() => root.System._IsLoadFinished_k__BackingField) ||
                !Read(() => root.System._IsGameLoaded_k__BackingField)) throw new SourceFailure();
            root.GameManager = Read(() => root.System._GameDataManager);
            root.PlayerManager = Read(() => root.System._PlayerDataManager);
            root.PhotoManager = Read(() => root.System._PhotoDataManager);
            root.OptionManager = Read(() => root.System._UserOptionManager);
            Required(root.GameManager); Required(root.PlayerManager); Required(root.PhotoManager); Required(root.OptionManager);
            root.Game = Read(() => root.GameManager._Data_k__BackingField);
            root.SaveRoots = new Il2CppObjectBase[] { root.Game,
                Read(() => root.PlayerManager._Data_k__BackingField), Read(() => root.PlayerManager._InstanceData_k__BackingField),
                Read(() => root.PhotoManager._Data_k__BackingField), Read(() => root.OptionManager._Data_k__BackingField) };
            foreach (Il2CppObjectBase value in root.SaveRoots) Required(value);
            root.Dictionary = Read(() => root.Game.m_Box); Required(root.Dictionary); Exact<BagDictionary>(root.Dictionary);
            root.Player = Read(() => root.Manager._playerCharacter_k__BackingField);
            root.SceneType = (int)Read(() => root.Context._SceneType);
            root.ManagerIdentity = UnityIdentity(root.Manager);
            var identity = new StringBuilder();
            foreach (UnityEngine.Object value in new UnityEngine.Object[] { root.System, root.GameManager,
                root.PlayerManager, root.PhotoManager, root.OptionManager }) identity.Append(UnityIdentity(value));
            foreach (Il2CppObjectBase value in new Il2CppObjectBase[] { root.System, root.GameManager, root.PlayerManager,
                root.PhotoManager, root.OptionManager, root.Bag, root.Dictionary, root.Context }) Identity(identity, value);
            foreach (Il2CppObjectBase value in root.SaveRoots) Identity(identity, value);
            root.BagIdentity = Hash(identity.ToString());
            if (needPlayer)
            {
                Required(root.Player); root.PlayerIdentity = UnityIdentity(root.Player);
                if (!Read(() => root.Player._IsInitialized_k__BackingField)) throw new SourceFailure();
                var go = Read(() => root.Player.gameObject); Required(go);
                if (!Read(() => go.activeInHierarchy)) throw new SourceFailure();
                Scene scene = Read(() => go.scene);
                if (!Read(() => scene.IsValid()) || !Read(() => scene.isLoaded)) throw new SourceFailure();
                root.SceneHandle = scene.handle; root.SceneKey = Read(() => scene.name);
                Text(root.SceneKey);
                Required(Read(() => root.Player.m_InstanceItemInven));
            }
            // Freeze actual containers before this operation can end. Keep also
            // retains a handle already returned if a post-call guard fails.
            foreach (Il2CppObjectBase value in new Il2CppObjectBase[] { root.Manager, root.System, root.GameManager,
                root.PlayerManager, root.PhotoManager, root.OptionManager, root.Bag, root.Dictionary, root.Context }) Keep(value);
            foreach (Il2CppObjectBase value in root.SaveRoots) Keep(value);
            if (needPlayer) Keep(root.Player);
            return root;
        }

        private NativeHostCargoSnapshot.ImageValues ReadImage(Root root)
        {
            var stamp = new StringBuilder(root.BagIdentity).Append(root.ManagerIdentity).Append(root.PlayerIdentity);
            Append(stamp, root.SceneHandle); Append(stamp, root.SceneType); stamp.Append(Text(root.SceneKey));
            float weight = Read(() => root.Bag._weight_k__BackingField), capacity = Read(() => root.Bag.m_WeightMax);
            float parameter = Read(() => root.Bag._WeightParameter_k__BackingField), threshold = Read(() => root.Bag._overloadedThreshold_k__BackingField);
            if (!float.IsFinite(weight) || weight < 0 || !float.IsFinite(capacity) || capacity <= 0 ||
                !float.IsFinite(parameter) || !float.IsFinite(threshold)) throw new SourceFailure();
            Append(stamp, BitConverter.SingleToInt32Bits(weight)); Append(stamp, BitConverter.SingleToInt32Bits(capacity));
            Append(stamp, BitConverter.SingleToInt32Bits(parameter)); Append(stamp, BitConverter.SingleToInt32Bits(threshold));
            var status = Read(() => root.Bag.m_CharacterStatus); var listener = Read(() => root.Bag.m_LootBoxEventListener);
            Identity(stamp, status); Identity(stamp, listener); Required(status); Keep(status); Keep(listener);
            Append(stamp, Read(() => root.Bag.currentDebuffTid));
            IntList(Read(() => root.Bag.AppliedWeightBuffs), stamp);
            var inventory = Read(() => root.Player.m_InstanceItemInven); Identity(stamp, inventory); Keep(inventory);

            BagDictionary dictionary = root.Dictionary;
            int count = Read(() => dictionary._count), free = Read(() => dictionary._freeCount),
                version = Read(() => dictionary._version), freeList = Read(() => dictionary._freeList);
            var entries = Read(() => dictionary._entries); var buckets = Read(() => dictionary._buckets);
            int length = entries == null ? 0 : Read(() => entries.Length), bucketLength = buckets == null ? 0 : Read(() => buckets.Length);
            if (count < 0 || free < 0 || free > count || count > length || count - free > MaxSlots ||
                length > MaxDictionaryStorage || bucketLength > MaxDictionaryStorage || (length == 0) != (bucketLength == 0)) throw new SourceFailure();
            Identity(stamp, entries); Identity(stamp, buckets); Keep(entries); Keep(buckets);
            var aux = new StringBuilder(); DictionaryAux(dictionary, aux); stamp.Append(aux);
            Append(stamp, count); Append(stamp, free); Append(stamp, version); Append(stamp, freeList);
            Append(stamp, length); Append(stamp, bucketLength);
            for (int i = 0; i < bucketLength; i++) Append(stamp, Visit(() => buckets[i]));
            var slots = new List<NativeHostCargoSlot>(); var keys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < length; i++)
            {
                // Entry array reads can box native value types. This is bounded
                // storage sampling, not zero-native-allocation or ABI evidence.
                var entry = Visit(() => entries[i]);
                int hash = Read(() => entry.hashCode), next = Read(() => entry.next);
                string key = Read(() => entry.key); LootBoxSlot slot = Read(() => entry.value);
                Append(stamp, hash); Append(stamp, next); stamp.Append(Text(key)); Identity(stamp, slot);
                if (i >= count || hash < 0)
                {
                    if (key != null || slot != null) throw new SourceFailure();
                    continue;
                }
                if (key == null || slot == null || next < -1 || next >= count || !keys.Add(key)) throw new SourceFailure();
                Exact<LootBoxSlot>(slot); Keep(slot);
                LootObscuredIntSnapshot item = Int(Read(() => slot.m_ItemID)), grade = Int(Read(() => slot.m_Grade)),
                    final = Int(Read(() => slot.m_FinalGrade)), total = Int(Read(() => slot.m_TotalCount));
                Encoded(stamp, item); Encoded(stamp, grade); Encoded(stamp, final); Encoded(stamp, total);
                ObscuredBool isNew = Read(() => slot.m_IsNew);
                Append(stamp, isNew.currentCryptoKey); Append(stamp, isNew.hiddenValue); Append(stamp, isNew.inited ? 1 : 0);
                Append(stamp, isNew.fakeValue ? 1 : 0); Append(stamp, isNew.fakeValueActive ? 1 : 0);
                int type = (int)Read(() => slot.m_Type), lift = (int)Read(() => slot.m_AutoLiftedType);
                Append(stamp, type); Append(stamp, lift);
                int times = StringList(Read(() => slot.m_GetTimes), stamp);
                slots.Add(new NativeHostCargoSlot(Text(key), LootSlotSnapshot.DecodeInt(item).Value,
                    LootSlotSnapshot.DecodeInt(grade).Value, LootSlotSnapshot.DecodeInt(final).Value,
                    LootSlotSnapshot.DecodeInt(total).Value, type, lift, times));
            }
            if (slots.Count != count - free || count != Read(() => dictionary._count) || free != Read(() => dictionary._freeCount) ||
                version != Read(() => dictionary._version) || freeList != Read(() => dictionary._freeList) ||
                Pointer(entries) != Pointer(Read(() => dictionary._entries)) || Pointer(buckets) != Pointer(Read(() => dictionary._buckets))) throw new SourceFailure();
            var auxAgain = new StringBuilder(); DictionaryAux(dictionary, auxAgain);
            if (aux.ToString() != auxAgain.ToString()) throw new SourceFailure();
            stamp.Append(auxAgain);
            return new NativeHostCargoSnapshot.ImageValues { Weight = weight, Capacity = capacity, Parameter = parameter,
                Threshold = threshold, Fingerprint = "host-native-bag-v1/" + Hash(stamp.ToString()),
                Slots = slots.ToArray(), SceneHandle = root.SceneHandle, SceneKey = root.SceneKey, SceneType = root.SceneType };
        }

        private void DictionaryAux(BagDictionary dictionary, StringBuilder stamp)
        {
            foreach (Il2CppObjectBase value in new Il2CppObjectBase[] { Read(() => dictionary._comparer),
                Read(() => dictionary._keys), Read(() => dictionary._values), Read(() => dictionary._syncRoot) })
            { Identity(stamp, value); Keep(value); }
        }

        private int StringList(Il2CppSystem.Collections.Generic.List<string> list, StringBuilder stamp)
        {
            Identity(stamp, list); if (list == null) { Append(stamp, -1); return 0; }
            Keep(list);
            int size = Read(() => list._size), version = Read(() => list._version); var items = Read(() => list._items);
            int length = items == null ? 0 : Read(() => items.Length);
            if (size < 0 || size > length || length > MaxTimeListStorage) throw new SourceFailure();
            var sync = Read(() => list._syncRoot);
            Identity(stamp, items); Identity(stamp, sync); Keep(items); Keep(sync);
            Append(stamp, size); Append(stamp, version); Append(stamp, length);
            for (int i = 0; i < length; i++) stamp.Append(Text(Visit(() => items[i])));
            if (size != Read(() => list._size) || version != Read(() => list._version) ||
                Pointer(items) != Pointer(Read(() => list._items)) || Pointer(sync) != Pointer(Read(() => list._syncRoot))) throw new SourceFailure();
            return size;
        }
        private void IntList(Il2CppSystem.Collections.Generic.List<int> list, StringBuilder stamp)
        {
            Identity(stamp, list); if (list == null) { Append(stamp, -1); return; }
            Keep(list);
            int size = Read(() => list._size), version = Read(() => list._version); var items = Read(() => list._items);
            int length = items == null ? 0 : Read(() => items.Length);
            if (size < 0 || size > length || length > MaxDictionaryStorage) throw new SourceFailure();
            var sync = Read(() => list._syncRoot);
            Identity(stamp, items); Identity(stamp, sync); Keep(items); Keep(sync);
            Append(stamp, size); Append(stamp, version); Append(stamp, length);
            for (int i = 0; i < length; i++) Append(stamp, Visit(() => items[i]));
            if (size != Read(() => list._size) || version != Read(() => list._version) ||
                Pointer(items) != Pointer(Read(() => list._items)) || Pointer(sync) != Pointer(Read(() => list._syncRoot))) throw new SourceFailure();
        }

        private bool Begin(bool requireOwner)
        {
            if (_failed || _stopped) return false;
            if (_busy || Environment.CurrentManagedThreadId != _thread)
            { Fail("Host cargo source thread or reentry integrity was lost."); return false; }
            _busy = true; _requiresOwner = requireOwner; _steps = _storageVisits = _characters = 0;
            _operationSerial = Interlocked.Read(ref _faultSerial);
            try
            {
                if (requireOwner && !_ownerCurrent() || _failed || _stopped ||
                    _operationSerial != Interlocked.Read(ref _faultSerial)) { _busy = false; return false; }
            }
            catch (Exception) { _busy = false; Fail("Host cargo source ownership check failed."); return false; }
            return true;
        }
        private void Check(bool requireOwner = false)
        {
            if (++_steps > MaxSteps || !_busy || _failed || _stopped || Environment.CurrentManagedThreadId != _thread ||
                Interlocked.Read(ref _faultSerial) != _operationSerial) throw new SourceFailure();
            if ((_requiresOwner || requireOwner) && !_ownerCurrent()) throw new SourceFailure();
            if (_failed || _stopped || Interlocked.Read(ref _faultSerial) != _operationSerial) throw new SourceFailure();
        }
        private T Read<T>(Func<T> read)
        {
            Check(); T value = read(); Check(); return value;
        }
        private T Visit<T>(Func<T> read)
        { if (++_storageVisits > MaxStorageVisits) throw new SourceFailure(); return Read(read); }
        private long Pointer(Il2CppObjectBase value) => value == null ? 0 : Read(() => value.Pointer.ToInt64());
        private void Required(Il2CppObjectBase value) { if (Pointer(value) == 0) throw new SourceFailure(); }
        private IntPtr Class(Il2CppObjectBase value)
        {
            long pointer = Pointer(value);
            return pointer == 0 ? IntPtr.Zero : Read(() => IL2CPP.il2cpp_object_get_class(new IntPtr(pointer)));
        }
        private void Exact<T>(Il2CppObjectBase value) where T : Il2CppObjectBase
        {
            IntPtr expected = Read(() => Il2CppClassPointerStore<T>.NativeClassPtr);
            if (expected == IntPtr.Zero || Class(value) != expected || Read(() => Il2CppClassPointerStore<T>.NativeClassPtr) != expected)
                throw new SourceFailure();
        }
        private string UnityIdentity(UnityEngine.Object value)
        {
            Required(value); IntPtr cached = Read(() => value.m_CachedPtr);
            if (cached == IntPtr.Zero) throw new SourceFailure();
            return Pointer(value).ToString(CultureInfo.InvariantCulture) + ":" + cached.ToInt64().ToString(CultureInfo.InvariantCulture) +
                ":" + Class(value).ToInt64().ToString(CultureInfo.InvariantCulture) + ";";
        }
        private void Identity(StringBuilder stamp, Il2CppObjectBase value)
        { Append(stamp, Pointer(value)); Append(stamp, Class(value).ToInt64()); }
        private void Keep(Il2CppObjectBase value)
        {
            long pointer = Pointer(value); if (pointer == 0 || _references.ContainsKey(pointer)) return;
            if (_references.Count >= MaxRetainedReferences) throw new SourceFailure();
            var reference = new RetainedReference { Object = value }; _references.Add(pointer, reference);
            Read(() => reference.Handle = IL2CPP.il2cpp_gchandle_new(new IntPtr(pointer), false));
            if (reference.Handle == IntPtr.Zero) throw new SourceFailure();
        }
        private string Text(string text)
        {
            if (text == null) return "null;";
            if (text.Length > MaxStringCharacters || (_characters += text.Length) > MaxSampleStringCharacters) throw new SourceFailure();
            // Exact UTF-16 code units: UTF-8 replacement must not merge keys.
            var bytes = new byte[text.Length * 2];
            for (int i = 0; i < text.Length; i++) { bytes[i * 2] = (byte)text[i]; bytes[i * 2 + 1] = (byte)(text[i] >> 8); }
            return text.Length.ToString(CultureInfo.InvariantCulture) + ":" + Convert.ToHexString(SHA256.HashData(bytes)) + ";";
        }
        private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(text))).ToLowerInvariant();
        private static void Append(StringBuilder stamp, long value) => stamp.Append(value.ToString(CultureInfo.InvariantCulture)).Append(';');
        private static LootObscuredIntSnapshot Int(ObscuredInt value)
            => new LootObscuredIntSnapshot(value.currentCryptoKey, value.hiddenValue, value.inited, value.fakeValue, value.fakeValueActive);
        private static void Encoded(StringBuilder stamp, LootObscuredIntSnapshot value)
        { Append(stamp, value.CurrentCryptoKey); Append(stamp, value.HiddenValue); Append(stamp, value.Inited ? 1 : 0);
            Append(stamp, value.FakeValue); Append(stamp, value.FakeValueActive ? 1 : 0); }
        private static bool SameBag(Root first, Root second) => first != null && second != null && first.BagIdentity == second.BagIdentity;
        private static bool SameActiveRoot(Root first, Root second)
            => SameBag(first, second) && first.ManagerIdentity == second.ManagerIdentity && first.PlayerIdentity == second.PlayerIdentity &&
                first.SceneHandle == second.SceneHandle && first.SceneKey == second.SceneKey && first.SceneType == second.SceneType;

        private void Fail(string reason)
        { Interlocked.Increment(ref _faultSerial); _failed = true; _activeDive = false; Status = reason; }
        public void Stop(string reason)
        { _stopped = true; _activeDive = false; Status = "Host cargo source stopped; retained native references remain."; }
        public void Dispose()
        {
            Stop("Dispose");
            if (Environment.CurrentManagedThreadId != _thread || _busy || _contexts.Count != 0 ||
                Interlocked.CompareExchange(ref _cleanupAttempted, 1, 0) != 0) return;
            try
            {
                _harmony?.UnpatchSelf(); _installed = false;
                if (ReferenceEquals(_active, this)) _active = null;
            }
            catch (Exception) { Fail("Host cargo observer cleanup outcome is unknown; no retry or handle release."); }
        }
        private sealed class SourceFailure : Exception { }
    }
}
