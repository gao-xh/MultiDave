using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using DaveCoop.Core.Session;
using DaveCoop.Core.World;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine.SceneManagement;
using NativeCatalog = Il2CppSystem.Collections.Generic.Dictionary<int, DR.GameScene>;

namespace DaveCoop.Networking
{
    // A source-owned experimental consumer, not a general world permission.
    // Original factory results and callback scopes bind entry -> reset -> load.
    // Unknown bootstrap scenes retain their original native loading parameters.
    internal sealed class NativeGuestMapController
    {
        private const int MaxEntries = 32, MaxTraces = 64, MaxCatalogItems = 4096;
        private const int MaxSourceHandles = 7 * MaxEntries;
        private const string Owner = Plugin.Id + ".guest-map-route";
        private static NativeGuestMapController _active;
        private readonly NativeGuestInitializationController _source;
        private readonly ManualLogSource _logger;
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly List<IntPtr> _handles = new List<IntPtr>();
        private readonly List<Il2CppObjectBase> _references = new List<Il2CppObjectBase>();
        private SessionPeer _peer;
        private MapChoiceController _maps;
        private Entry _insideEntry, _insideMove, _insideLoad;
        private bool _bindingBusy, _loadBusy;
        private int _traces;
        private string _lastTrace;
        private Harmony _harmony;
        public long InstalledRoutes { get; private set; }
        public string Status { get; private set; } = "Awaiting a natural diving entry and host route.";
        public int SourceHandleCount => _handles.Count;
        public bool InitialSceneProfileVerified => false;

        private sealed class SceneStamp
        {
            public int Id, Type;
            public string Name;
            public bool Additive, Diving;
        }
        private sealed class Entry
        {
            public SceneLoader Loader;
            public Il2CppSystem.Collections.IEnumerator Returned, LoadReturned;
            public SceneLoader._CoChangeSceneAsync_d__111 Iterator;
            public SceneLoader._CoLoadSceneAsync_d__108 LoadIterator;
            public DR.GameScene OriginalScene, Scene;
            public DataManager Data;
            public NativeCatalog Catalog;
            public SceneStamp Stamp, OriginalStamp;
            public long LoaderPointer, IteratorPointer, OriginalScenePointer, ScenePointer, DataPointer, CatalogPointer, LoadPointer;
            public long CatalogEntries, CatalogBuckets;
            public int CatalogCount, CatalogFreeCount, CatalogVersion;
            public IntPtr LoaderUnityPointer, DataUnityPointer;
            public bool FactoryStarted, FactoryReturned, EntryReturned, FirstApproved, ResetStarted, ResetReturned;
            public bool SceneWriteAttempted, SceneBound, LayerProven, Retired, InstallAttempted, LoadFactoryReturned, LoadFirstApproved;
            public string LoadKey;
            public LoadSceneMode LoadMode;
            public bool Activate;
            public SceneContext Context;
            public NativeGuestMapLease Lease;
            public NativeGuestMapRoute Route;
            public MapChoiceSnapshot Choice;
        }
        private sealed class MoveCall { public Entry Entry, Previous; public bool Approved, Finished; }
        private sealed class ResetCall { public Entry Entry; public SceneLoader Loader; public bool Finished; }
        private sealed class LoadCall { public Entry Entry; public bool Finished; }

        public NativeGuestMapController(NativeGuestInitializationController source, ManualLogSource logger)
        { _source = source ?? throw new ArgumentNullException(nameof(source)); _logger = logger; }

        public void Install()
        {
            if (_active != null) throw new InvalidOperationException("Another experimental map consumer owns this process.");
            _active = this; // Partial patches and retained objects require a process restart.
            var targets = new List<(MethodInfo Method, string Prefix, string Postfix, string Finalizer)>();
            Type enumerator = typeof(Il2CppSystem.Collections.IEnumerator), action = typeof(Il2CppSystem.Action);
            Add(targets, typeof(SceneLoader), "GoToInGameEntry", false, typeof(void), new[] { typeof(string), typeof(SceneTransitionType), typeof(bool) }, nameof(EntryBefore), nameof(EntryAfter), nameof(EntryFinally));
            Add(targets, typeof(SceneLoader), "CoChangeSceneAsync", false, enumerator,
                new[] { typeof(DR.GameScene), typeof(SceneTransitionType), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), action, action, typeof(bool), typeof(bool), typeof(bool) },
                nameof(FactoryBefore), nameof(FactoryAfter), nameof(FactoryFinally));
            Add(targets, typeof(SceneLoader._CoChangeSceneAsync_d__111), "MoveNext", false, typeof(bool), Type.EmptyTypes, nameof(MoveBefore), nameof(MoveAfter), nameof(MoveFinally));
            Add(targets, typeof(SceneLoader), "ResetForNewScene", false, typeof(void), new[] { typeof(bool), typeof(bool), typeof(DR.GameScene) }, nameof(ResetBefore), nameof(ResetAfter), nameof(ResetFinally));
            Add(targets, typeof(SceneLoader), "CoLoadSceneAsync", true, enumerator, new[] { typeof(string), typeof(LoadSceneMode), typeof(bool) }, nameof(LoadBefore), nameof(LoadAfter), nameof(LoadFinally));
            Add(targets, typeof(SceneLoader._CoLoadSceneAsync_d__108), "MoveNext", false, typeof(bool), Type.EmptyTypes, nameof(ResourceBefore), nameof(ResourceAfter), nameof(ResourceFinally));
            foreach (string name in new[] { "Clear", "Reset", "ClearAllCache" })
                Add(targets, typeof(SceneContext), name, false, typeof(void), Type.EmptyTypes, nameof(ContextRetired), null, null);
            _harmony = new Harmony(Owner);
            foreach (var target in targets)
                _harmony.Patch(target.Method, prefix: Hook(target.Prefix, Priority.First),
                    postfix: Hook(target.Postfix, Priority.Last), finalizer: Hook(target.Finalizer, Priority.Last));
            if (!targets.All(target => Harmony.GetPatchInfo(target.Method)?.Owners.Contains(Owner) == true))
                throw new InvalidOperationException("Experimental map consumer patches are incomplete.");
        }

        public bool BindTransport(SessionPeer peer, MapChoiceController maps)
        {
            if (!_source.AcceptCallbackThread() || _source.ConfirmedUnityThreadId == 0 || peer == null || maps == null) return false;
            if (_peer != null && (!ReferenceEquals(peer, _peer) || !ReferenceEquals(maps, _maps))) return false;
            if (peer.Snapshot.Role != SessionRole.Guest || peer.Snapshot.Phase == SessionPhase.Closed) return false;
            _peer = peer; _maps = maps; return true;
        }

        private Entry BeginEntry(SceneLoader loader)
        {
            if (!_source.AcceptCallbackThread() || _bindingBusy || _insideEntry != null || _insideMove != null || _insideLoad != null)
                throw Block("The diving entry reentered or changed its callback thread.");
            _bindingBusy = true;
            try
            {
                if (!SourceCurrent() || ReferenceEquals(loader, null) || _entries.Count >= MaxEntries)
                    throw Block("A diving entry requires live temporary roots and a bounded source slot.");
                foreach (Entry old in _entries) old.Retired = true;
                var entry = new Entry { Loader = loader, LoaderPointer = Pointer(loader), LoaderUnityPointer = loader.m_CachedPtr };
                if (entry.LoaderPointer == 0 || entry.LoaderUnityPointer == IntPtr.Zero || !SourceCurrent())
                    throw Block("The original entry loader is unavailable.");
                _entries.Add(entry); Keep(loader); _insideEntry = entry; return entry;
            }
            finally { _bindingBusy = false; }
        }
        private Entry BeginFactory(SceneLoader loader, DR.GameScene scene)
        {
            Entry entry = _insideEntry;
            if (entry == null) return null; // Ordinary title/lobby transitions have no diving owner.
            if (_bindingBusy || _loadBusy) throw Block("The natural change factory reentered a source check.");
            _bindingBusy = true;
            try
            {
                if (!SourceCurrent() || !LoaderCurrent(entry) || Pointer(loader) != entry.LoaderPointer || entry.FactoryStarted || ReferenceEquals(scene, null))
                    throw Block("The natural diving entry has no unique original change factory.");
                entry.FactoryStarted = true; entry.OriginalScene = scene; Keep(scene); return entry;
            }
            finally { _bindingBusy = false; }
        }
        private void EndEntry(Entry entry, bool ranOriginal)
        {
            if (_bindingBusy || _loadBusy) throw Block("The diving entry return reentered a source check.");
            _bindingBusy = true;
            try
            {
                if (!ranOriginal || !entry.FactoryReturned || !SourceCurrent() || !ReferenceEquals(_insideEntry, entry) || !LoaderCurrent(entry))
                    throw Block("The original diving entry did not return its paired change factory.");
                entry.EntryReturned = true;
            }
            finally { _bindingBusy = false; }
        }
        private void EndFactory(Entry entry, Il2CppSystem.Collections.IEnumerator result, bool ranOriginal)
        {
            if (_bindingBusy || _loadBusy) throw Block("The change factory return reentered a source check.");
            _bindingBusy = true;
            try
            {
                if (!ranOriginal || entry.FactoryReturned || ReferenceEquals(result, null) || !ReferenceEquals(_insideEntry, entry) || !SourceCurrent() || !LoaderCurrent(entry))
                    throw Block("The original change factory did not return one fixed iterator.");
                entry.Returned = result; Keep(result); entry.FactoryReturned = true;
            }
            finally { _bindingBusy = false; }
        }

        private bool BeginMove(SceneLoader._CoChangeSceneAsync_d__111 iterator, ref bool result, out MoveCall call)
        {
            call = new MoveCall { Previous = _insideMove };
            _insideMove = null; // Unknown/nested iterators never inherit a parent's identity.
            if (!_source.AcceptCallbackThread()) throw Block("A scene-change callback changed threads.");
            if (_source.ConfirmedUnityThreadId == 0) return true; // No native reads before actual Update.
            if (_bindingBusy || _loadBusy) throw Block("Scene-change binding reentered.");
            _bindingBusy = true;
            try
            {
                long pointer = Pointer(iterator);
                Entry entry = Unique(pointer, false);
                if (entry == null) return true;
                call.Entry = entry;
                // StartCoroutine may synchronously invoke its first MoveNext
                // before the original GoToInGameEntry body returns.
                if (entry.Retired || (!entry.EntryReturned && !ReferenceEquals(_insideEntry, entry)) || !SourceCurrent())
                    throw Block("The fixed scene-change source is no longer active.");
                if (entry.IteratorPointer == 0)
                {
                    entry.Iterator = iterator; entry.IteratorPointer = pointer;
                    if (pointer == 0 || IL2CPP.il2cpp_object_get_class(iterator.Pointer) != Il2CppClassPointerStore<SceneLoader._CoChangeSceneAsync_d__111>.NativeClassPtr ||
                        Pointer(iterator.__4__this) != entry.LoaderPointer || Pointer(iterator.sceneData) != Pointer(entry.OriginalScene) ||
                        iterator.__1__state != 0 || iterator.__2__current != null)
                        throw Block("The fixed original scene-change iterator already advanced or disagrees with its factory.");
                }
                if (!LoaderCurrent(entry)) throw Block("The original scene-change loader or iterator expired.");
                if (!entry.FirstApproved)
                {
                    if (iterator.__1__state != 0 || iterator.__2__current != null) throw Block("The held original iterator advanced unexpectedly.");
                    if (!CaptureChoice(out MapChoiceSnapshot choice))
                    { result = true; Trace("WAITING_ROUTE", "Holding the natural diving change iterator for a complete current host route."); return false; }
                    entry.Choice = choice;
                    BindScene(entry);
                    entry.FirstApproved = true;
                }
                if (!Current(entry)) throw Block("The host route or temporary source expired before original scene loading.");
                if (call.Previous != null) throw Block("A second bound diving iterator nested inside an original move.");
                _insideMove = entry; call.Approved = true; return true;
            }
            finally { _bindingBusy = false; }
        }

        private void BindScene(Entry entry)
        {
            entry.OriginalScenePointer = Pointer(entry.OriginalScene);
            SceneStamp original = ReadScene(entry.OriginalScene);
            entry.OriginalStamp = original;
            entry.Data = Singleton<DataManager>._instance;
            if (ReferenceEquals(entry.Data, null)) throw Block("The actual local scene catalog is absent.");
            entry.DataPointer = Pointer(entry.Data); entry.DataUnityPointer = entry.Data.m_CachedPtr;
            entry.Catalog = entry.Data._SceneDataDic_k__BackingField; entry.CatalogPointer = Pointer(entry.Catalog);
            if (entry.CatalogPointer == 0) throw Block("The actual local scene dictionary is absent.");
            entry.CatalogCount = entry.Catalog._count; entry.CatalogFreeCount = entry.Catalog._freeCount; entry.CatalogVersion = entry.Catalog._version;
            entry.CatalogEntries = Pointer(entry.Catalog._entries); entry.CatalogBuckets = Pointer(entry.Catalog._buckets);
            if (entry.DataPointer == 0 || entry.DataUnityPointer == IntPtr.Zero || entry.CatalogCount < 0 || entry.CatalogCount > MaxCatalogItems ||
                entry.CatalogFreeCount < 0 || entry.CatalogFreeCount > entry.CatalogCount || !CatalogCurrent(entry) ||
                !entry.Catalog.TryGetValue(original.Id, out DR.GameScene canonical) || Pointer(canonical) != entry.OriginalScenePointer ||
                !Same(original, ReadScene(entry.OriginalScene)) || !SourceCurrent() || !LoaderCurrent(entry))
                throw Block("The original scene data does not match a stable local catalog entry.");
            Keep(entry.Data); Keep(entry.Catalog);
            SceneContext context = SceneContext._s_Instance_k__BackingField;
            entry.LayerProven = HasLayer(context, original.Id, original.Name);
            entry.Scene = entry.OriginalScene; entry.Stamp = original;
            MapRouteScene hostEntry = entry.Choice.Route.Scenes.Single(scene => scene.SceneId == entry.Choice.Route.EntrySceneId);
            if (entry.LayerProven && (original.Id != hostEntry.SceneId || original.Name != hostEntry.SceneName))
            {
                if (!entry.Catalog.TryGetValue(hostEntry.SceneId, out DR.GameScene selected))
                    throw Block("The host entry layer is absent from the local catalog.");
                SceneStamp target = ReadScene(selected);
                if (target.Id != hostEntry.SceneId || target.Name != hostEntry.SceneName ||
                    target.Type != original.Type || target.Additive != original.Additive || target.Diving != original.Diving ||
                    !HasLayer(context, target.Id, target.Name) || !Same(target, ReadScene(selected)))
                    throw Block("The host entry does not identify one compatible local native layer scene.");
                Keep(selected); entry.Scene = selected; entry.Stamp = target;
            }
            entry.ScenePointer = Pointer(entry.Scene);
            if (!SceneInputsCurrent(entry, requireBound: false) || !ChoiceCurrent(entry) || !SourceCurrent())
                throw Block("Initial scene inputs expired before binding.");
            if (entry.ScenePointer != entry.OriginalScenePointer)
            {
                entry.SceneWriteAttempted = true;
                entry.Iterator.sceneData = entry.Scene; // Before original Reset and key derivation.
            }
            entry.SceneBound = true;
            if (!SceneInputsCurrent(entry, requireBound: true) || !SourceCurrent() || !ChoiceCurrent(entry))
                throw Block("The original scene-change input did not bind consistently.");
        }

        private bool HasLayer(SceneContext context, int id, string name)
        {
            if (ReferenceEquals(context, null)) return false; // Original reset may create this context.
            if (Pointer(SceneContext._s_Instance_k__BackingField) != Pointer(context))
                throw Block("The native layer classification context changed.");
            var list = context.sceneLayerDataList;
            if (ReferenceEquals(list, null)) return false;
            long pointer = Pointer(list);
            if (pointer == 0) throw Block("A non-null native layer catalog has no identity.");
            // A prior adopted list is our own wire-derived allocation, not an
            // independent native source for classifying a later entry scene.
            if (_entries.Any(other => other.Route?.OwnsLayerList(pointer) == true))
            {
                if (Pointer(context.sceneLayerDataList) != pointer || Pointer(SceneContext._s_Instance_k__BackingField) != Pointer(context) || !SourceCurrent())
                    throw Block("The previously adopted layer list changed during classification.");
                return false;
            }
            var items = list._items;
            long itemsPointer = Pointer(items);
            int count = list._size, version = list._version;
            if (count < 0 || count > MaxCatalogItems || (count > 0 && (itemsPointer == 0 || items.Length < count)))
                throw Block("The native layer catalog exceeds its read bound.");
            int matches = 0;
            for (int i = 0; i < count; i++)
            {
                var item = list[i];
                if (ReferenceEquals(item, null) || IL2CPP.il2cpp_object_get_class(item.Pointer) != Il2CppClassPointerStore<SceneMapLayerData>.NativeClassPtr)
                    throw Block("The native layer catalog has an unsupported record.");
                int itemId = item.SceneID; string itemName = item.SceneName;
                if ((itemId == id) != (itemName == name)) throw Block("The local layer name and identity disagree.");
                if (itemId == id && itemName == name) matches++;
                if (item.SceneID != itemId || item.SceneName != itemName || !SourceCurrent()) throw Block("A native layer changed while classifying its initial scene.");
            }
            if (matches > 1 || Pointer(context.sceneLayerDataList) != pointer || list._size != count || list._version != version ||
                Pointer(list._items) != itemsPointer || Pointer(SceneContext._s_Instance_k__BackingField) != Pointer(context) || !SourceCurrent())
                throw Block("The local layer catalog changed or duplicated its initial scene.");
            return matches == 1; // Absence never certifies a particular bootstrap profile.
        }

        private ResetCall BeginReset(SceneLoader loader, DR.GameScene scene)
        {
            Entry entry = _insideMove;
            if (entry == null) return null;
            if (_bindingBusy || _loadBusy) throw Block("The original reset reentered a source check.");
            _bindingBusy = true;
            try
            {
                if (!Current(entry) || entry.ResetStarted || Pointer(scene) != entry.ScenePointer || Pointer(loader) != entry.LoaderPointer)
                    throw Block("The original reset is not paired with its fixed diving change iterator.");
                entry.ResetStarted = true; return new ResetCall { Entry = entry, Loader = loader };
            }
            finally { _bindingBusy = false; }
        }
        private void EndReset(ResetCall call, bool ranOriginal)
        {
            if (call == null || call.Finished) return;
            call.Finished = true;
            Entry entry = call.Entry;
            if (_bindingBusy || _loadBusy) throw Block("The paired reset return reentered a source check.");
            _bindingBusy = true;
            try
            {
                SceneContext context = SceneContext._s_Instance_k__BackingField;
                if (!ranOriginal || !ReferenceEquals(_insideMove, entry) || !Current(entry) || Pointer(call.Loader) != entry.LoaderPointer ||
                    ReferenceEquals(context, null) || Pointer(context) == 0 || !SourceCurrent())
                    throw Block("The paired original reset did not return with its current native context.");
                entry.Context = context; entry.ResetReturned = true;
            }
            finally { _bindingBusy = false; }
        }

        private void BeginLoad(string key, LoadSceneMode mode, bool activate, out LoadCall call)
        {
            call = null;
            Entry entry = _insideMove;
            if (entry == null) return;
            if (_bindingBusy || _loadBusy) throw Block("The actual load factory reentered a source check.");
            _bindingBusy = true;
            try
            {
                if (!Current(entry) || !entry.ResetReturned || entry.InstallAttempted || key != entry.Stamp.Name || !ContextCurrent(entry))
                    throw Block("The actual load factory is not the current reset's first original scene load.");
                entry.LoadKey = key; entry.LoadMode = mode; entry.Activate = activate;
                entry.InstallAttempted = true; _loadBusy = true; _insideLoad = entry;
                // The actual call token exists before any preparation that
                // can throw. An unknown nested factory cannot close this window.
                call = new LoadCall { Entry = entry };
                entry.Lease = new NativeGuestMapLease(this, entry, _source.ConfirmedUnityThreadId, entry.Context);
                entry.Route = new NativeGuestMapRoute(entry.Lease, entry.Choice.Route);
                if (!entry.Route.TryPrepare() || !entry.Route.TryInstall() || !entry.Route.ValidateInstalled() || !Current(entry) || !ContextCurrent(entry))
                    throw Block("The six temporary route roots could not be installed before original resource loading.");
                InstalledRoutes++;
                Trace("INSTALLED", entry.LayerProven ?
                    "Host route fields and a catalog-matched initial layer bound before loading; IGPs and full world isolation remain unverified." :
                    "Host route fields installed before loading; original unclassified bootstrap parameters preserved and full initial-scene profile remains unverified.");
            }
            finally { _bindingBusy = false; }
        }
        private void EndLoad(LoadCall call, Il2CppSystem.Collections.IEnumerator returned, bool ranOriginal)
        {
            if (call == null || call.Finished) return;
            call.Finished = true;
            Entry entry = call.Entry;
            if (_bindingBusy) throw Block("The paired load return reentered a source check.");
            _bindingBusy = true;
            try
            {
                if (!ranOriginal || ReferenceEquals(returned, null) || !ReferenceEquals(_insideLoad, entry) || !Current(entry) || !ContextCurrent(entry) || !entry.Route.ValidateInstalled())
                    throw Block("The fixed original scene-load factory did not return after route installation.");
                entry.LoadReturned = returned; entry.LoadPointer = Pointer(returned);
                if (entry.LoadPointer == 0 || IL2CPP.il2cpp_object_get_class(returned.Pointer) != Il2CppClassPointerStore<SceneLoader._CoLoadSceneAsync_d__108>.NativeClassPtr)
                    throw Block("The fixed original load factory returned an unsupported iterator.");
                Keep(returned); entry.LoadFactoryReturned = true;
            }
            finally { _bindingBusy = false; }
        }
        private bool BeginResource(SceneLoader._CoLoadSceneAsync_d__108 iterator, out MoveCall call)
        {
            call = new MoveCall { Previous = _insideMove }; _insideMove = null;
            if (!_source.AcceptCallbackThread()) throw Block("A resource-load callback changed threads.");
            if (_source.ConfirmedUnityThreadId == 0) return true;
            if (_bindingBusy || _loadBusy) throw Block("Resource-load binding reentered.");
            _bindingBusy = true;
            try
            {
                Entry entry = Unique(Pointer(iterator), true);
                if (entry == null) return true;
                call.Entry = entry;
                if (!entry.LoadFactoryReturned || !Current(entry) || !ContextCurrent(entry) || !entry.Route.ValidateInstalled())
                    throw Block("The fixed resource-load source expired before its original move.");
                if (!entry.LoadFirstApproved)
                {
                    if (iterator.__1__state != 0 || iterator.__2__current != null) throw Block("The fixed resource-load iterator advanced before validation.");
                    entry.LoadIterator = iterator; entry.LoadFirstApproved = true;
                }
                if (Pointer(iterator) != entry.LoadPointer || iterator.key != entry.LoadKey || iterator.loadMode != entry.LoadMode || iterator.activateOnLoad != entry.Activate ||
                    !Current(entry) || !ContextCurrent(entry)) throw Block("The original resource-load parameters changed.");
                call.Approved = true; return true;
            }
            finally { _bindingBusy = false; }
        }

        private Entry Unique(long pointer, bool resource)
        {
            Entry found = null;
            foreach (Entry candidate in _entries)
                if ((resource ? candidate.LoadFactoryReturned && candidate.LoadPointer == pointer : candidate.FactoryReturned && Pointer(candidate.Returned) == pointer))
                { if (found != null || pointer == 0) throw Block("An original iterator has ambiguous factory sources."); found = candidate; }
            return found;
        }
        private bool CaptureChoice(out MapChoiceSnapshot choice)
        {
            choice = null;
            return _maps != null && _maps.TryCaptureRemoteChoices(_peer, out choice) && choice != null && !choice.Retired &&
                choice.Generation > 0 && choice.Route != null && MapSelections.FingerprintRoute(choice.Route) == choice.RouteFingerprint;
        }
        private bool ChoiceCurrent(Entry entry) => entry.Choice != null && CaptureChoice(out MapChoiceSnapshot choice) &&
            choice.Generation == entry.Choice.Generation && choice.RouteFingerprint == entry.Choice.RouteFingerprint;
        private bool SourceCurrent() => !_source.Failed && _source.ConfirmedUnityThreadId > 0 &&
            Environment.CurrentManagedThreadId == _source.ConfirmedUnityThreadId && _source.MapSourceCurrent(_peer) && !_source.Failed;
        private bool LoaderCurrent(Entry entry) => !entry.Retired && Pointer(entry.Loader) == entry.LoaderPointer &&
            entry.Loader.m_CachedPtr == entry.LoaderUnityPointer && entry.LoaderUnityPointer != IntPtr.Zero &&
            (entry.Iterator == null || (Pointer(entry.Iterator) == entry.IteratorPointer && Pointer(entry.Iterator.__4__this) == entry.LoaderPointer)) && !entry.Retired && !_source.Failed;
        private bool SceneInputsCurrent(Entry entry, bool requireBound)
        {
            long expected = requireBound ? entry.ScenePointer : entry.OriginalScenePointer;
            return Pointer(entry.Iterator.sceneData) == expected && Pointer(Singleton<DataManager>._instance) == entry.DataPointer &&
                entry.Data.m_CachedPtr == entry.DataUnityPointer && entry.DataUnityPointer != IntPtr.Zero &&
                Pointer(entry.Data._SceneDataDic_k__BackingField) == entry.CatalogPointer && CatalogCurrent(entry) &&
                entry.Catalog.TryGetValue(entry.OriginalScene._TID_k__BackingField, out DR.GameScene original) && Pointer(original) == entry.OriginalScenePointer &&
                entry.Catalog.TryGetValue(entry.Stamp.Id, out DR.GameScene selected) && Pointer(selected) == entry.ScenePointer &&
                Pointer(entry.Scene) == entry.ScenePointer &&
                Same(entry.OriginalStamp, ReadScene(entry.OriginalScene)) && Same(entry.Stamp, ReadScene(entry.Scene)) &&
                CatalogCurrent(entry) && !entry.Retired && !_source.Failed;
        }
        private bool CatalogCurrent(Entry entry) => entry.Catalog._count == entry.CatalogCount && entry.Catalog._freeCount == entry.CatalogFreeCount &&
            entry.Catalog._version == entry.CatalogVersion && Pointer(entry.Catalog._entries) == entry.CatalogEntries &&
            Pointer(entry.Catalog._buckets) == entry.CatalogBuckets && !entry.Retired && !_source.Failed;
        private bool ResourceInputsCurrent(Entry entry) => Pointer(entry.LoadIterator) == entry.LoadPointer &&
            entry.LoadIterator.key == entry.LoadKey && entry.LoadIterator.loadMode == entry.LoadMode &&
            entry.LoadIterator.activateOnLoad == entry.Activate && !entry.Retired && !_source.Failed;
        private bool Current(Entry entry) => entry != null && SourceCurrent() && LoaderCurrent(entry) && entry.SceneBound &&
            SceneInputsCurrent(entry, true) && ChoiceCurrent(entry) && !entry.Retired && !_source.Failed;
        private bool ContextCurrent(Entry entry)
        {
            if (entry == null || ReferenceEquals(entry.Context, null)) return false;
            long pointer = Pointer(entry.Context), actual = Pointer(SceneContext._s_Instance_k__BackingField);
            return pointer != 0 && pointer == actual && !entry.Retired && !_source.Failed;
        }
        private SceneStamp ReadScene(DR.GameScene scene)
        {
            if (ReferenceEquals(scene, null) || IL2CPP.il2cpp_object_get_class(scene.Pointer) != Il2CppClassPointerStore<DR.GameScene>.NativeClassPtr)
                throw Block("The local native scene record has an unsupported class.");
            var value = new SceneStamp { Id = scene._TID_k__BackingField, Name = scene._SceneName_k__BackingField,
                Type = scene._SceneType_k__BackingField, Additive = scene._IsAdditive_k__BackingField, Diving = scene._SceneWithDiving_k__BackingField };
            if (string.IsNullOrEmpty(value.Name) || value.Name.Length > 256 || !SourceCurrent())
                throw Block("The local native scene record changed or has unsupported identity metadata.");
            return value;
        }
        private static bool Same(SceneStamp a, SceneStamp b) => a != null && b != null && a.Id == b.Id && a.Name == b.Name &&
            a.Type == b.Type && a.Additive == b.Additive && a.Diving == b.Diving;

        internal bool BindRoute(object identity, NativeGuestMapLease lease, NativeGuestMapRoute route)
        {
            var entry = identity as Entry;
            if (!_loadBusy || !ReferenceEquals(_insideLoad, entry) || !ReferenceEquals(entry?.Lease, lease) || entry.Route != null || route == null) return false;
            entry.Route = route; return true;
        }
        internal bool RouteWindow(object identity, NativeGuestMapLease lease, NativeGuestMapRoute route)
        {
            var entry = identity as Entry;
            if (entry == null || !_loadBusy || !ReferenceEquals(entry, _insideLoad) || !ReferenceEquals(entry, _insideMove) ||
                !entry.ResetReturned || !ReferenceEquals(entry.Lease, lease) || !ReferenceEquals(entry.Route, route)) return false;
            try { return Current(entry) && ContextCurrent(entry) && !_source.Failed && !entry.Retired && _loadBusy &&
                ReferenceEquals(entry, _insideLoad) && ReferenceEquals(entry, _insideMove); }
            catch { return false; }
        }
        internal bool InstalledWindow(object identity, NativeGuestMapLease lease, NativeGuestMapRoute route)
        {
            var entry = identity as Entry;
            if (entry == null || !ReferenceEquals(entry.Lease, lease) || !ReferenceEquals(entry.Route, route)) return false;
            try { return Current(entry) && ContextCurrent(entry) && !_source.Failed && !entry.Retired; }
            catch { return false; }
        }

        private void EndMove(MoveCall call, bool ranOriginal, bool result, bool resource)
        {
            if (call == null || call.Finished) return;
            call.Finished = true;
            bool guarded = false;
            try
            {
                Entry entry = call.Entry;
                if (entry == null || !call.Approved) return;
                if (_bindingBusy || _loadBusy) throw Block("The original loading move return reentered a source check.");
                _bindingBusy = true; guarded = true;
                if (!ranOriginal || !Current(entry)) throw Block("The approved original loading move was skipped or lost its source.");
                if (resource && !ResourceInputsCurrent(entry)) throw Block("The original resource move returned with changed source parameters.");
                if (entry.Route != null && (!ContextCurrent(entry) || !entry.Route.ValidateInstalled())) throw Block("The original loader changed its installed temporary route.");
                if (!resource && !result && !entry.LoadFactoryReturned) throw Block("The original diving change completed without its paired host-route load.");
            }
            finally { if (guarded) _bindingBusy = false; _insideMove = call.Previous; }
        }
        private void Keep(Il2CppObjectBase value)
        {
            if (_handles.Count >= MaxSourceHandles || ReferenceEquals(value, null) || !SourceCurrent()) throw Block("Native loading-source retention is unavailable.");
            _references.Add(value);
            IntPtr handle = IL2CPP.il2cpp_gchandle_new(value.Pointer, false);
            if (handle == IntPtr.Zero) throw Block("A native loading-source reference could not be retained.");
            _handles.Add(handle);
            if (!SourceCurrent()) throw Block("Temporary roots expired while retaining a loading source.");
        }
        private Exception Block(string reason)
        { _source.Fail("Experimental map initialization blocked: " + reason); Status = reason; return new InvalidOperationException("MultiDave temporary map initialization was blocked."); }
        private void Trace(string stage, string text)
        {
            Status = text; string trace = stage + ": " + text;
            if (_traces >= MaxTraces || _lastTrace == trace) return;
            _lastTrace = trace; _traces++;
            try { _logger.LogInfo("DAVECOOP_GUEST_MAP_" + stage + ": " + text); }
            catch { _source.Fail("Experimental map diagnostic callback failed."); }
        }
        private static long Pointer(Il2CppObjectBase value) => ReferenceEquals(value, null) ? 0 : value.Pointer.ToInt64();
        private static HarmonyMethod Hook(string name, int priority) => name == null ? null :
            new HarmonyMethod(typeof(NativeGuestMapController).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)) { priority = priority };
        private static void Add(List<(MethodInfo, string, string, string)> targets, Type type, string name, bool isStatic, Type result, Type[] arguments, string prefix, string postfix, string finalizer)
        {
            MethodInfo method = type.GetMethod(name, BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic |
                (isStatic ? BindingFlags.Static : BindingFlags.Instance), null, arguments, null);
            if (method == null || method.IsStatic != isStatic || method.IsGenericMethod || method.ReturnType != result || method.DeclaringType != type)
                throw new InvalidOperationException("Unsupported experimental loading declaration: " + type.Name + "." + name);
            targets.Add((method, prefix, postfix, finalizer));
        }

        private static void EntryBefore(SceneLoader __instance, out Entry __state)
        {
            __state = null;
            if (_active == null) return;
            try { __state = _active.BeginEntry(__instance); }
            catch (Exception error) { _active._source.Fail("Diving entry source callback failed: " + error.GetType().Name); throw; }
        }
        private static void EntryAfter(bool __runOriginal, Entry __state)
        {
            if (__state == null) return;
            _active.EndEntry(__state, __runOriginal);
        }
        private static Exception EntryFinally(Exception __exception, Entry __state)
        {
            if (__state != null && _active != null)
            { if (__exception != null) _active._source.Fail("Original diving entry exception: " + __exception.GetType().Name);
                if (ReferenceEquals(_active._insideEntry, __state)) _active._insideEntry = null; }
            return __exception;
        }
        private static void FactoryBefore(SceneLoader __instance, DR.GameScene __0, out Entry __state)
        {
            __state = null;
            if (_active == null) return;
            try { __state = _active.BeginFactory(__instance, __0); }
            catch (Exception error) { _active._source.Fail("Change factory source callback failed: " + error.GetType().Name); throw; }
        }
        private static void FactoryAfter(Il2CppSystem.Collections.IEnumerator __result, bool __runOriginal, Entry __state)
        {
            if (__state == null) return;
            _active.EndFactory(__state, __result, __runOriginal);
        }
        private static Exception FactoryFinally(Exception __exception, Entry __state)
        { if (__state != null && __exception != null) _active?._source.Fail("Original change factory exception: " + __exception.GetType().Name); return __exception; }
        private static bool MoveBefore(SceneLoader._CoChangeSceneAsync_d__111 __instance, ref bool __result, out MoveCall __state)
        {
            __state = null;
            if (_active == null) return true;
            try { return _active.BeginMove(__instance, ref __result, out __state); }
            catch (Exception error) { _active._source.Fail("Scene-change source callback failed: " + error.GetType().Name); throw; }
        }
        private static void MoveAfter(bool __runOriginal, bool __result, MoveCall __state) => _active?.EndMove(__state, __runOriginal, __result, false);
        private static Exception MoveFinally(Exception __exception, MoveCall __state)
        {
            if (_active != null && __state != null)
            { if (__exception != null && __state.Entry != null) _active._source.Fail("Original loading move exception: " + __exception.GetType().Name);
                _active._insideMove = __state.Previous; }
            return __exception;
        }
        private static void ResetBefore(SceneLoader __instance, DR.GameScene __2, out ResetCall __state)
        {
            __state = null;
            if (_active == null) return;
            try { __state = _active.BeginReset(__instance, __2); }
            catch (Exception error) { _active._source.Fail("Paired reset source callback failed: " + error.GetType().Name); throw; }
        }
        private static void ResetAfter(bool __runOriginal, ResetCall __state) => _active?.EndReset(__state, __runOriginal);
        private static Exception ResetFinally(Exception __exception, ResetCall __state)
        { if (__state != null && __exception != null) _active?._source.Fail("Original paired reset exception: " + __exception.GetType().Name); return __exception; }
        private static void LoadBefore(string __0, LoadSceneMode __1, bool __2, out LoadCall __state)
        {
            __state = null;
            if (_active == null) return;
            try { _active.BeginLoad(__0, __1, __2, out __state); }
            catch (Exception error) { _active._source.Fail("Paired load source callback failed: " + error.GetType().Name); throw; }
        }
        private static void LoadAfter(Il2CppSystem.Collections.IEnumerator __result, bool __runOriginal, LoadCall __state) => _active?.EndLoad(__state, __result, __runOriginal);
        private static Exception LoadFinally(Exception __exception, LoadCall __state)
        {
            if (_active != null && __state != null)
            { if (__exception != null) _active._source.Fail("Original paired load factory exception: " + __exception.GetType().Name);
                if (ReferenceEquals(_active._insideLoad, __state.Entry)) { _active._insideLoad = null; _active._loadBusy = false; } }
            return __exception;
        }
        private static bool ResourceBefore(SceneLoader._CoLoadSceneAsync_d__108 __instance, out MoveCall __state)
        {
            __state = null;
            if (_active == null) return true;
            try { return _active.BeginResource(__instance, out __state); }
            catch (Exception error) { _active._source.Fail("Resource-load source callback failed: " + error.GetType().Name); throw; }
        }
        private static void ResourceAfter(bool __runOriginal, bool __result, MoveCall __state) => _active?.EndMove(__state, __runOriginal, __result, true);
        private static Exception ResourceFinally(Exception __exception, MoveCall __state) => MoveFinally(__exception, __state);
        private static void ContextRetired(SceneContext __instance)
        {
            if (_active == null || !_active._source.AcceptCallbackThread() || _active._source.ConfirmedUnityThreadId == 0) return;
            long pointer = Pointer(__instance);
            foreach (Entry entry in _active._entries)
                if (entry.ResetReturned && !entry.Retired && Pointer(entry.Context) == pointer) entry.Retired = true;
        }
    }

    // A private actual-source identity, not constructible from a DTO or GUI flag.
    internal sealed class NativeGuestMapLease
    {
        private readonly NativeGuestMapController _owner;
        private readonly object _identity;
        internal int UnityThreadId { get; }
        internal Guid LeaseId { get; } = Guid.NewGuid();
        internal SceneContext Context { get; }
        internal NativeGuestMapLease(NativeGuestMapController owner, object identity, int thread, SceneContext context)
        { _owner = owner; _identity = identity; UnityThreadId = thread; Context = context; }
        internal bool TryBindRoute(NativeGuestMapRoute route) => _owner.BindRoute(_identity, this, route);
        internal bool IsRouteWindow(NativeGuestMapRoute route) => _owner.RouteWindow(_identity, this, route);
        internal bool AllowsInstalledValidation(NativeGuestMapRoute route) => _owner.InstalledWindow(_identity, this, route);
    }
}
