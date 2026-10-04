using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using BepInEx.Logging;
using DaveCoop.Core.World;
using DR.AI;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using NumMatrix = System.Numerics.Matrix4x4;
using NumVector = System.Numerics.Vector3;

namespace DaveCoop.Networking
{
    // A default-off extension of the original completed LOD classification.
    // It neither calls SetActive/RP setters nor runs another job/AI tick.
    // Unknown targets, shared group records and unsupported projections retain
    // the original result. None of these observations authorize fish actions.
    internal sealed class NativeHostFishLodArea : IDisposable
    {
        public const int MaxFishRecords = 4096;
        public const int MaxManagers = 32;
        public const int MaxScopes = 32;
        public const int MaxRowsPerBatch = 512;
        public const int MaxParentDepth = 32;
        public const int MaxRetainedHandles = 24576;
        public const int MaxNativeStepsPerCall = 65536;
        public const int MaxLogs = 128;
        private const string Owner = Plugin.Id + ".host-fish-lod-area";
        private static NativeHostFishLodArea _active;
        private static int _processOwnerClaimed;
        // Retain wrappers/handles after a verified own unpatch as well. No hot
        // scene/room transition is a native-reference release boundary here.
        private static readonly List<NativeHostFishLodArea> Retired = new List<NativeHostFishLodArea>();
        private readonly HostFishInterestSource _source;
        private readonly ManualLogSource _logger;
        private readonly Dictionary<long, ManagerIdentity> _managers = new Dictionary<long, ManagerIdentity>();
        private readonly Dictionary<long, Record> _records = new Dictionary<long, Record>();
        private readonly Dictionary<long, Il2CppObjectBase> _references = new Dictionary<long, Il2CppObjectBase>();
        private readonly List<IntPtr> _handles = new List<IntPtr>();
        private readonly List<FishCall> _fishScopes = new List<FishCall>();
        private readonly List<CompletionCall> _completionScopes = new List<CompletionCall>();
        private readonly List<MethodInfo> _targets = new List<MethodInfo>();
        private Harmony _harmony;
        private bool _installAttempted, _installed, _failed, _busy, _nativeStep;
        private int _steps, _cleanupAttempted, _logs;
        private HostFishInterestWindow _readWindow;
        private long _nativeReads;

        private sealed class FishIdentity
        {
            public FishAISystem Fish;
            public GameObject Root;
            public Transform RootTransform;
            public long Pointer, RootPointer, TransformPointer;
            public IntPtr Class, Unity, RootUnity, TransformUnity;
            public int UnityId, SceneHandle, Tid;
        }
        private sealed class ManagerIdentity
        { public AutoActivatorJob Manager; public long Pointer; public IntPtr Class, Unity; public int UnityId; }
        private sealed class ComponentIdentity
        {
            public Component Target;
            public Transform Transform;
            public long Pointer, TransformPointer;
            public IntPtr Class, Unity, TransformUnity;
            public int UnityId, SceneHandle;
        }
        private sealed class Record
        {
            public ManagerIdentity Manager;
            public FishIdentity Fish;
            public ComponentIdentity Target;
            public AutoActivatorJob.LODData Data;
            public long Pointer;
            public IntPtr Class;
            public bool Retired;
        }
        private sealed class FishCall { public FishIdentity Fish; }
        private sealed class RegistrationCall
        { public ManagerIdentity Manager; public FishIdentity Fish; public ComponentIdentity Target; }
        private sealed class CompletionCall
        {
            public ManagerIdentity Manager;
            public HostFishInterestWindow Window;
            public NativeArray<DRLODJob.RequestedData> Requests;
            public NativeArray<DRLODJob.ResultData> Results;
            public IntPtr RequestBuffer, ResultBuffer;
            public Allocator RequestAllocator, ResultAllocator;
            public Il2CppSystem.Collections.Generic.List<AutoActivatorJob.LODData> List;
            public Il2CppArrayBase<AutoActivatorJob.LODData> Items;
            public long ListPointer, ItemsPointer;
            public int Count, ListVersion;
            public JobHandle Handle;
            public FishInterestLodRegion Region;
            public long[] OrderedDataPointers;
            public DRLODJob.RequestedData[] ProducerRows;
            public bool[] ProducerRowsKnown;
            public bool Usable, Joined, WriteAttempted;
        }
        private sealed class JoinCall { public CompletionCall Completion; public bool Returned; }
        private sealed class Change
        { public int Index; public Record Record; public DRLODJob.RequestedData Request; public DRLODJob.ResultData Original, Replacement; }
        private sealed class WindowExpired : Exception { }

        public bool Installed => _installed;
        public bool Healthy => _installed && !_failed && !_source.Failed;
        public bool Failed => _failed || _source.Failed;
        public string Status { get; private set; } = "Host fish LOD area: disabled.";
        public int TargetCount => _targets.Count;
        public int BoundRecords => _records.Count;
        public int RetainedHandles => _handles.Count;
        public long ObservedNativeReads => _nativeReads;
        public long JoinedBatches { get; private set; }
        public long AppliedRows { get; private set; }
        public long UnsupportedTargets { get; private set; }
        public long UnsupportedRows { get; private set; }
        public long MissingInterest { get; private set; }
        public long EarlyCallbacks { get; private set; }
        public long UnknownWriteOutcomes { get; private set; }
        public bool NativeFieldAbiVerified => false;
        public bool FullCoverageVerified => false;
        public bool WorldAuthority => false;
        public bool CargoAuthority => false;

        public NativeHostFishLodArea(HostFishInterestSource source, ManualLogSource logger)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source)); _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (Interlocked.CompareExchange(ref _processOwnerClaimed, 1, 0) != 0)
                throw new InvalidOperationException("Host LOD area has one retained owner per process; restart before recreating it.");
        }

        public void Install()
        {
            if (_installAttempted || _active != null) throw new InvalidOperationException("Host fish LOD hooks are single-use.");
            _installAttempted = true; _active = this;
            try
            {
                _harmony = new Harmony(Owner);
                Patch(typeof(SABaseFishSystem), "RequestManageUpdateByLOD", Type.EmptyTypes, typeof(void), nameof(FishBefore), null, nameof(FishFinally));
                Patch(typeof(AutoActivatorJob), "RequestManagement", new[] { typeof(Component) }, typeof(AutoActivatorJob.LODData), nameof(RegisterBefore), nameof(RegisterAfter), nameof(RegisterFinally));
                Patch(typeof(AutoActivatorJob), "Complete", Type.EmptyTypes, typeof(void), nameof(CompleteBefore), null, nameof(CompleteFinally));
                Patch(typeof(JobHandle), "Complete", Type.EmptyTypes, typeof(void), nameof(JoinBefore), nameof(JoinAfter), nameof(JoinFinally));
                _installed = true; Status = "Host fish LOD area armed; native ABI and complete coverage unverified."; Log("ARMED");
            }
            catch { Fault("InstallFailed"); throw; }
        }

        private void Patch(Type type, string name, Type[] args, Type result, string before, string after, string final)
        {
            MethodInfo method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, args, null);
            if (method == null || method.IsStatic || method.IsGenericMethod || method.ReturnType != result || method.GetParameters().Any(p => p.ParameterType.IsByRef))
                throw new InvalidOperationException("Exact host LOD declaration is unavailable.");
            _targets.Add(method);
            _harmony.Patch(method, prefix: before == null ? null : Hook(before, Priority.First), postfix: after == null ? null : Hook(after, Priority.Last),
                finalizer: final == null ? null : Hook(final, Priority.Last));
            if (Harmony.GetPatchInfo(method)?.Owners.Contains(Owner) != true) throw new InvalidOperationException("Own host LOD hook was not registered.");
        }
        private static HarmonyMethod Hook(string method, int priority) => new HarmonyMethod(typeof(NativeHostFishLodArea).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)) { priority = priority };

        public void Update()
        {
            // No active/native work or second job is dispatched by Update.
            if (_source.Failed && !_failed) Fault("InterestSourceFailed");
        }

        private bool ThreadKnown()
        {
            if (_source.UnityThreadId == 0) { EarlyCallbacks++; return false; }
            if (Environment.CurrentManagedThreadId == _source.UnityThreadId) return true;
            Fault("WrongThread"); return false;
        }
        private bool CanObserve() => _installed && !Failed && !_source.IsReading && ThreadKnown();
        private void Execute(HostFishInterestWindow window, Action action)
        {
            if (_busy || _nativeStep) { Fault("ReentrantNativeRead"); throw new InvalidOperationException("Host LOD read reentered."); }
            _busy = true; _steps = 0; _readWindow = window;
            try { Check(); action(); Check(); }
            finally { _nativeStep = false; _readWindow = null; _busy = false; }
        }
        private void Check()
        {
            if (!_busy || !_installed || Failed || !ThreadKnown()) throw new InvalidOperationException("Host LOD native window is unavailable.");
            if (_readWindow != null && !_source.IsCurrent(_readWindow)) throw new WindowExpired();
            if (Failed) throw new InvalidOperationException("Host LOD source failed during validation.");
        }
        private T Read<T>(Func<T> read)
        {
            Check(); if (_nativeStep || ++_steps > MaxNativeStepsPerCall) throw new InvalidOperationException("Host LOD read bound exceeded.");
            _nativeStep = true; _nativeReads++;
            T value;
            try { value = read(); } finally { _nativeStep = false; }
            Check(); return value;
        }
        private void Write(Action write, CompletionCall call)
        {
            Check(); if (_nativeStep || ++_steps > MaxNativeStepsPerCall) throw new InvalidOperationException("Host LOD write bound exceeded.");
            call.WriteAttempted = true; _nativeStep = true; _nativeReads++;
            try { write(); } finally { _nativeStep = false; }
            Check();
        }
        private void Keep(Il2CppObjectBase wrapper)
        {
            long pointer = Read(() => Pointer(wrapper));
            if (pointer == 0) throw new InvalidOperationException("Host LOD retained reference is missing.");
            if (_references.ContainsKey(pointer)) return;
            if (_handles.Count >= MaxRetainedHandles) throw new InvalidOperationException("Host LOD retained-reference bound exceeded.");
            _references.Add(pointer, wrapper);
            Check(); _nativeStep = true;
            IntPtr handle;
            try { handle = IL2CPP.il2cpp_gchandle_new(new IntPtr(pointer), false); }
            finally { _nativeStep = false; }
            if (handle != IntPtr.Zero) _handles.Add(handle); // before post-allocation validation
            Check(); if (handle == IntPtr.Zero) throw new InvalidOperationException("Host LOD reference retention failed.");
        }
        private static long Pointer(Il2CppObjectBase value) => ReferenceEquals(value, null) ? 0 : value.Pointer.ToInt64();
        private IntPtr ClassOf(Il2CppObjectBase value)
        { long pointer = Read(() => Pointer(value)); return pointer == 0 ? IntPtr.Zero : Read(() => IL2CPP.il2cpp_object_get_class(new IntPtr(pointer))); }
        private long FieldPointer(Func<Il2CppObjectBase> field)
        { Il2CppObjectBase value = Read(field); return Read(() => Pointer(value)); }
        private int SceneHandle(Component component)
        { GameObject root = Read(() => component.gameObject); var scene = Read(() => root.scene); return scene.m_Handle; }

        private FishIdentity FreezeFish(Component component)
        {
            if (ReferenceEquals(component, null)) return null;
            long pointer = Read(() => Pointer(component)); if (pointer == 0) return null;
            IntPtr native = ClassOf(component);
            bool exact = false;
            foreach (Func<IntPtr> known in FishClasses)
                if (Read(known) == native && native != IntPtr.Zero) { exact = true; break; }
            if (!exact) return null;
            FishAISystem fish = Read(() => component.TryCast<FishAISystem>());
            if (ReferenceEquals(fish, null)) throw new InvalidOperationException("Exact fish class did not produce its expected wrapper.");
            var id = new FishIdentity { Fish = fish, Pointer = pointer, Class = native,
                Unity = Read(() => fish.m_CachedPtr), UnityId = Read(() => fish.GetInstanceID()), Tid = Read(() => fish.FishDataTID) };
            if (id.Unity == IntPtr.Zero || id.UnityId == 0) return null;
            id.Root = Read(() => fish.gameObject); id.RootTransform = Read(() => fish.transform);
            id.RootPointer = Read(() => Pointer(id.Root)); id.TransformPointer = Read(() => Pointer(id.RootTransform));
            id.RootUnity = Read(() => id.Root.m_CachedPtr); id.TransformUnity = Read(() => id.RootTransform.m_CachedPtr);
            id.SceneHandle = Read(() => id.Root.scene.m_Handle);
            if (id.RootPointer == 0 || id.TransformPointer == 0 || id.RootUnity == IntPtr.Zero || id.TransformUnity == IntPtr.Zero || id.SceneHandle == 0) return null;
            Keep(id.Fish); Keep(id.Root); Keep(id.RootTransform); ValidateFish(id); return id;
        }
        private static readonly Func<IntPtr>[] FishClasses = {
            () => Il2CppClassPointerStore<FishAISystem>.NativeClassPtr, () => Il2CppClassPointerStore<SpecialAttackerFishAISystem>.NativeClassPtr,
            () => Il2CppClassPointerStore<SABaseFishSystem>.NativeClassPtr, () => Il2CppClassPointerStore<SABaseAI>.NativeClassPtr,
            () => Il2CppClassPointerStore<SAMahoniCommon>.NativeClassPtr, () => Il2CppClassPointerStore<SAMahoniGeneral>.NativeClassPtr,
            () => Il2CppClassPointerStore<SAXiphactinus>.NativeClassPtr, () => Il2CppClassPointerStore<SASnappingTurtle>.NativeClassPtr,
            () => Il2CppClassPointerStore<SAGroundCrawlerFish>.NativeClassPtr };

        private void ValidateFish(FishIdentity id)
        {
            if (Read(() => Pointer(id.Fish)) != id.Pointer || Read(() => id.Fish.m_CachedPtr) != id.Unity || ClassOf(id.Fish) != id.Class ||
                Read(() => id.Fish.GetInstanceID()) != id.UnityId || Read(() => id.Fish.FishDataTID) != id.Tid ||
                FieldPointer(() => id.Fish.gameObject) != id.RootPointer || Read(() => id.Root.m_CachedPtr) != id.RootUnity ||
                FieldPointer(() => id.Fish.transform) != id.TransformPointer || Read(() => id.RootTransform.m_CachedPtr) != id.TransformUnity ||
                Read(() => id.Root.scene.m_Handle) != id.SceneHandle)
                throw new InvalidOperationException("Retained fish identity changed.");
        }
        private ComponentIdentity FreezeTarget(Component target, FishIdentity fish)
        {
            if (ReferenceEquals(target, null)) return null;
            var id = new ComponentIdentity { Target = target, Pointer = Read(() => Pointer(target)), Class = ClassOf(target),
                Unity = Read(() => target.m_CachedPtr), UnityId = Read(() => target.GetInstanceID()), Transform = Read(() => target.transform) };
            id.TransformPointer = Read(() => Pointer(id.Transform)); id.TransformUnity = Read(() => id.Transform.m_CachedPtr);
            id.SceneHandle = SceneHandle(target);
            if (id.Pointer == 0 || id.Class == IntPtr.Zero || id.Unity == IntPtr.Zero || id.UnityId == 0 ||
                id.TransformPointer == 0 || id.TransformUnity == IntPtr.Zero || id.SceneHandle != fish.SceneHandle || !Contained(id, fish)) return null;
            Keep(target); Keep(id.Transform); return id;
        }
        private bool Contained(ComponentIdentity target, FishIdentity fish)
        {
            Transform node = Read(() => target.Target.transform);
            for (int depth = 0; depth < MaxParentDepth && !ReferenceEquals(node, null); depth++)
            {
                long pointer = Read(() => Pointer(node));
                GameObject root = Read(() => node.gameObject);
                // A transform ancestor alone does not make a grouped actor
                // unique. Reject an intervening fish, or two fish components
                // on the alleged owner root, rather than borrowing its scope.
                var actors = Read(() => root.GetComponents<FishAISystem>());
                if (ReferenceEquals(actors, null)) return false;
                int count = Read(() => actors.Length);
                if (count < 0 || count > 1) return false;
                if (count == 1)
                {
                    FishAISystem owner = Read(() => actors[0]);
                    if (Read(() => Pointer(owner)) != fish.Pointer) return false;
                }
                if (pointer == fish.TransformPointer)
                    return count == 1 && Read(() => node.m_CachedPtr) == fish.TransformUnity;
                node = Read(() => node.parent);
            }
            return false;
        }
        private ManagerIdentity FreezeManager(AutoActivatorJob manager)
        {
            if (ReferenceEquals(manager, null)) throw new InvalidOperationException("Host LOD manager is missing.");
            long pointer = Read(() => Pointer(manager)); IntPtr native = ClassOf(manager);
            if (native == IntPtr.Zero || native != Read(() => Il2CppClassPointerStore<AutoActivatorJob>.NativeClassPtr))
                throw new InvalidOperationException("Unknown host LOD manager class.");
            if (_managers.TryGetValue(pointer, out ManagerIdentity existing)) { ValidateManager(existing); return existing; }
            if (_managers.Count >= MaxManagers) throw new InvalidOperationException("Host LOD manager bound exceeded.");
            var id = new ManagerIdentity { Manager = manager, Pointer = pointer, Class = native,
                Unity = Read(() => manager.m_CachedPtr), UnityId = Read(() => manager.GetInstanceID()) };
            if (pointer == 0 || id.Unity == IntPtr.Zero || id.UnityId == 0) throw new InvalidOperationException("Host LOD manager identity is unavailable.");
            _managers.Add(pointer, id); Keep(manager); ValidateManager(id); return id;
        }
        private void ValidateManager(ManagerIdentity id)
        {
            if (Read(() => Pointer(id.Manager)) != id.Pointer || Read(() => id.Manager.m_CachedPtr) != id.Unity ||
                Read(() => id.Manager.GetInstanceID()) != id.UnityId || ClassOf(id.Manager) != id.Class)
                throw new InvalidOperationException("Retained host LOD manager changed.");
        }

        private FishCall BeginFish(SABaseFishSystem fish)
        {
            if (!_installed || !ThreadKnown()) return null;
            if (_fishScopes.Count >= MaxScopes) { Fault("FishScopeQuota"); return null; }
            var call = new FishCall(); _fishScopes.Add(call); // unknown child masks its parent
            if (Failed || _source.IsReading) return call;
            try { Execute(null, () => call.Fish = FreezeFish(fish)); }
            catch { Fault("FishScopeReadFailed"); }
            return call;
        }
        private void EndFish(FishCall call, bool exception)
        {
            if (call == null) return;
            if (_fishScopes.Count == 0 || !ReferenceEquals(_fishScopes[_fishScopes.Count - 1], call)) { Fault("FishScopePairingFailed"); return; }
            _fishScopes.RemoveAt(_fishScopes.Count - 1);
            if (exception) Fault("OriginalFishRegistrationException");
        }
        private RegistrationCall BeginRegistration(AutoActivatorJob manager, Component target)
        {
            if (!CanObserve()) return null;
            RegistrationCall call = null;
            try
            {
                Execute(null, () => {
                    FishIdentity fish = _fishScopes.Count == 0 ? FreezeFish(target) : _fishScopes[_fishScopes.Count - 1].Fish;
                    if (fish == null) { UnsupportedTargets++; return; }
                    ValidateFish(fish);
                    ComponentIdentity bound = FreezeTarget(target, fish);
                    if (bound == null) { UnsupportedTargets++; return; }
                    call = new RegistrationCall { Manager = FreezeManager(manager), Fish = fish, Target = bound };
                });
            }
            catch { Fault("RegistrationPrefixReadFailed"); }
            return call;
        }
        private void EndRegistration(RegistrationCall call, AutoActivatorJob.LODData data, bool ran)
        {
            if (call == null || !ran || Failed || _source.IsReading) return;
            try
            {
                Execute(null, () => {
                    ValidateManager(call.Manager); ValidateFish(call.Fish);
                    if (ReferenceEquals(data, null)) { UnsupportedTargets++; return; }
                    long pointer = Read(() => Pointer(data)); IntPtr native = ClassOf(data);
                    if (pointer == 0 || native == IntPtr.Zero || native != Read(() => Il2CppClassPointerStore<AutoActivatorJob.LODData>.NativeClassPtr) ||
                FieldPointer(() => data.mono) != call.Target.Pointer) { UnsupportedTargets++; return; }
                    if (_records.TryGetValue(pointer, out Record prior))
                    {
                        if (prior.Retired || prior.Manager.Pointer != call.Manager.Pointer || prior.Fish.Pointer != call.Fish.Pointer ||
                            prior.Target.Pointer != call.Target.Pointer || prior.Fish.Unity != call.Fish.Unity || prior.Fish.SceneHandle != call.Fish.SceneHandle)
                        { prior.Retired = true; UnsupportedTargets++; return; }
                        ValidateRecord(prior); return;
                    }
                    if (_records.Count >= MaxFishRecords) throw new InvalidOperationException("Host LOD fish record bound exceeded.");
                    var record = new Record { Manager = call.Manager, Fish = call.Fish, Target = call.Target, Data = data, Pointer = pointer, Class = native };
                    _records.Add(pointer, record); Keep(data); ValidateRecord(record);
                });
            }
            catch { Fault("RegistrationResultReadFailed"); }
        }
        private void ValidateRecord(Record record)
        {
            if (record.Retired) throw new InvalidOperationException("Host LOD record is retired.");
            ValidateFish(record.Fish);
            ComponentIdentity target = record.Target;
            if (Read(() => Pointer(target.Target)) != target.Pointer || Read(() => target.Target.m_CachedPtr) != target.Unity || ClassOf(target.Target) != target.Class ||
                Read(() => target.Target.GetInstanceID()) != target.UnityId || FieldPointer(() => target.Target.transform) != target.TransformPointer ||
                Read(() => target.Transform.m_CachedPtr) != target.TransformUnity || SceneHandle(target.Target) != target.SceneHandle ||
                !Contained(target, record.Fish) || Read(() => Pointer(record.Data)) != record.Pointer || ClassOf(record.Data) != record.Class ||
                FieldPointer(() => record.Data.mono) != target.Pointer)
                throw new InvalidOperationException("Retained fish LOD association changed.");
        }

        private CompletionCall BeginCompletion(AutoActivatorJob manager)
        {
            if (!_installed || !ThreadKnown()) return null;
            if (_completionScopes.Count >= MaxScopes) { Fault("CompletionScopeQuota"); return null; }
            var call = new CompletionCall(); _completionScopes.Add(call); // unrelated/unknown complete masks parent
            if (Failed || _source.IsReading) return call;
            try
            {
                Execute(null, () => {
                    if (!_source.TryCapture(out HostFishInterestWindow window)) { MissingInterest++; return; }
                    Check(); _readWindow = window; Check();
                    if (Read(() => manager.isRequestCompleted)) return;
                    long pointer = Read(() => Pointer(manager));
                    if (!_managers.TryGetValue(pointer, out ManagerIdentity known)) { UnsupportedTargets++; return; }
                    ValidateManager(known); call.Manager = known; call.Window = window;
                    call.Handle = Read(() => manager.jobHandle);
                    // A zero input may be a trivial Complete, not evidence that
                    // this batch's workers ever ran or produced these rows.
                    if (call.Handle.jobGroup == 0) { UnsupportedRows++; return; }
                    call.Requests = Read(() => manager.RequestDatas); call.Results = Read(() => manager.ResultDatas);
                    if (ReferenceEquals(call.Requests, null) || ReferenceEquals(call.Results, null)) { UnsupportedRows++; return; }
                    call.Count = Read(() => call.Requests.m_Length);
                    if (call.Count <= 0 || call.Count > MaxRowsPerBatch || Read(() => call.Results.m_Length) != call.Count) { UnsupportedRows++; return; }
                    call.RequestBuffer = Read(() => Buffer(call.Requests)); call.ResultBuffer = Read(() => Buffer(call.Results));
                    call.RequestAllocator = Read(() => call.Requests.m_AllocatorLabel); call.ResultAllocator = Read(() => call.Results.m_AllocatorLabel);
                    if (call.RequestBuffer == IntPtr.Zero || call.ResultBuffer == IntPtr.Zero || call.RequestBuffer == call.ResultBuffer) { UnsupportedRows++; return; }
                    call.List = Read(() => manager.lodDataForJob);
                    if (ReferenceEquals(call.List, null) || Read(() => call.List._size) != call.Count) { UnsupportedRows++; return; }
                    call.ListPointer = Read(() => Pointer(call.List)); call.ListVersion = Read(() => call.List._version);
                    call.Items = Read(() => call.List._items); call.ItemsPointer = Read(() => Pointer(call.Items));
                    if (ReferenceEquals(call.Items, null) || Read(() => call.Items.Length) < call.Count) { UnsupportedRows++; return; }
                    call.Region = ReadRegion(manager);
                    call.OrderedDataPointers = new long[call.Count]; call.ProducerRows = new DRLODJob.RequestedData[call.Count]; call.ProducerRowsKnown = new bool[call.Count];
                    var orderedRecords = new HashSet<long>();
                    // Freeze the actual original consumer's order and the known
                    // producer inputs before joining. This reads LOD/component
                    // fields, never the worker-owned NativeArray entries.
                    for (int i = 0; i < call.Count; i++)
                    {
                        int index = i; AutoActivatorJob.LODData data = Read(() => call.Items[index]);
                        long dataPointer = Read(() => Pointer(data)); call.OrderedDataPointers[i] = dataPointer;
                        if (!_records.TryGetValue(dataPointer, out Record record) || record.Retired || record.Manager.Pointer != known.Pointer || record.Fish.SceneHandle != window.SceneHandle) continue;
                        if (!orderedRecords.Add(dataPointer)) { record.Retired = true; UnsupportedRows++; continue; }
                        ValidateRecord(record);
                        Vector3 position = Read(() => record.Target.Transform.position);
                        DRLODJob.RequestedData row = default; row.position.x = position.x; row.position.y = position.y; row.position.z = position.z;
                        row.sizeInViewPort = Read(() => data.sizeInViewPort); row.customOutRange = Read(() => data.customOutRange); row.worldSize = Read(() => data.worldSize);
                        call.ProducerRows[i] = row; call.ProducerRowsKnown[i] = true;
                    }
                    CheckBatch(call); call.Usable = true;
                });
            }
            catch (WindowExpired) { call.Usable = false; MissingInterest++; }
            catch { call.Usable = false; Fault("CompletionPrefixReadFailed"); }
            return call;
        }

        private FishInterestLodRegion ReadRegion(AutoActivatorJob manager)
        {
            DRLODJob.LODDataJob job = Read(() => manager.lodDataJob);
            if (ReferenceEquals(job, null)) throw new InvalidOperationException("Original LOD job inputs are missing.");
            var vp = Read(() => job.vp); var pos = Read(() => job.camPos);
            var matrix = new NumMatrix(vp.c0.x, vp.c0.y, vp.c0.z, vp.c0.w, vp.c1.x, vp.c1.y, vp.c1.z, vp.c1.w,
                vp.c2.x, vp.c2.y, vp.c2.z, vp.c2.w, vp.c3.x, vp.c3.y, vp.c3.z, vp.c3.w);
            return new FishInterestLodRegion(matrix, new NumVector(pos.x, pos.y, pos.z), Read(() => job.outRangeAdded), Read(() => job.cullZ),
                Read(() => job.k_AddModifiedInRange_X), Read(() => job.k_AddModifiedOutRange_X), Read(() => job.k_AddModifiedInRange_Y), Read(() => job.k_AddModifiedOutRange_Y));
        }
        private unsafe static IntPtr Buffer<T>(NativeArray<T> array) where T : new() => (IntPtr)array.m_Buffer;
        private void CheckBatch(CompletionCall call)
        {
            ValidateManager(call.Manager);
            NativeArray<DRLODJob.RequestedData> request = Read(() => call.Manager.Manager.RequestDatas);
            NativeArray<DRLODJob.ResultData> result = Read(() => call.Manager.Manager.ResultDatas);
            if (ReferenceEquals(request, null) || ReferenceEquals(result, null) || Read(() => Buffer(request)) != call.RequestBuffer ||
                Read(() => Buffer(result)) != call.ResultBuffer || Read(() => request.m_Length) != call.Count || Read(() => result.m_Length) != call.Count ||
                Read(() => request.m_AllocatorLabel) != call.RequestAllocator || Read(() => result.m_AllocatorLabel) != call.ResultAllocator ||
                FieldPointer(() => call.Manager.Manager.lodDataForJob) != call.ListPointer || Read(() => call.List._version) != call.ListVersion ||
                Read(() => call.List._size) != call.Count || FieldPointer(() => call.List._items) != call.ItemsPointer || !SameRegion(ReadRegion(call.Manager.Manager), call.Region))
                throw new InvalidOperationException("Original LOD batch changed.");
        }
        private JoinCall BeginJoin(JobHandle handle)
        {
            // JobHandle is global. Unrelated worker joins must not fault the
            // observer, confirm a Unity thread or touch a native source at all.
            if (_completionScopes.Count == 0 || _source.UnityThreadId == 0 || Environment.CurrentManagedThreadId != _source.UnityThreadId) return null;
            if (!CanObserve()) return null;
            CompletionCall call = _completionScopes[_completionScopes.Count - 1];
            if (!call.Usable || call.Joined || call.Handle.jobGroup != handle.jobGroup || call.Handle.version != handle.version) return null;
            if (_busy || _nativeStep) { Fault("ReentrantJobJoin"); return null; }
            return new JoinCall { Completion = call };
        }
        private void AfterJoined(JoinCall join, bool exception)
        {
            if (join == null) return;
            CompletionCall call = join.Completion;
            if (exception || !join.Returned) { call.Usable = false; Fault("OriginalJobJoinException"); return; }
            if (_completionScopes.Count == 0 || !ReferenceEquals(_completionScopes[_completionScopes.Count - 1], call) || !call.Usable || call.Joined)
            { Fault("JobJoinScopeMismatch"); return; }
            call.Joined = true; // one normal return, never dispatch another join
            if (Failed || _source.IsReading) return;
            try { Execute(call.Window, () => ApplyJoined(call)); }
            catch (WindowExpired) { MissingInterest++; if (call.WriteAttempted) { UnknownWriteOutcomes++; Fault("SourceLostAfterResultWrite"); } }
            catch { if (call.WriteAttempted) UnknownWriteOutcomes++; Fault("JoinedBatchReadOrWriteFailed"); }
        }
        private void ApplyJoined(CompletionCall call)
        {
            CheckBatch(call);
            if (!Read(() => call.Manager.Manager.isRequestCompleted)) throw new InvalidOperationException("Original Complete did not enter its joined consumer.");
            NumVector local = new NumVector(call.Window.LocalPosition.x, call.Window.LocalPosition.y, call.Window.LocalPosition.z);
            NumVector delta = call.Window.Interest.Position - local;
            if (!FishInterestLodMath.TryTranslate(call.Region, delta, out FishInterestLodRegion remote)) { UnsupportedRows += call.Count; return; }
            var changes = new List<Change>(); var seen = new HashSet<long>();
            // Arrays are read only after the matching original join has returned
            // normally and its finalizer has no exception. All changes are
            // prepared before the first typed setter; no unmanaged offset write.
            for (int i = 0; i < call.Count; i++)
            {
                int index = i; AutoActivatorJob.LODData data = Read(() => call.Items[index]);
                long pointer = Read(() => Pointer(data));
                if (pointer != call.OrderedDataPointers[i]) throw new InvalidOperationException("Original LOD row order changed during join.");
                if (!_records.TryGetValue(pointer, out Record record) || record.Retired || record.Manager.Pointer != call.Manager.Pointer ||
                    record.Fish.SceneHandle != call.Window.SceneHandle || !call.ProducerRowsKnown[i]) continue;
                if (!seen.Add(pointer)) { record.Retired = true; changes.RemoveAll(change => change.Record.Pointer == pointer); UnsupportedRows++; continue; }
                ValidateRecord(record);
                DRLODJob.RequestedData request = Read(() => call.Requests[index]); DRLODJob.ResultData original = Read(() => call.Results[index]);
                var model = new FishInterestLodRequest(new NumVector(request.position.x, request.position.y, request.position.z), request.sizeInViewPort, request.customOutRange, request.worldSize);
                // The original producer zero-initializes its id field; it is
                // not a LODData object hash. Ordered pointer and scalar checks
                // bind this row, while the copied id is only worker sanity.
                if (!SameProducer(request, call.ProducerRows[i]) || request.lodDataHashID != original.lodDataHashID || !FishInterestLodMath.TryEvaluate(call.Region, model, out FishInterestLodEvaluation host) ||
                    (int)host.Layer != original.newLayer || host.ZCulled != original.isZCullOver || original.isZCullOver ||
                    !FishInterestLodMath.TryEvaluate(remote, model, out FishInterestLodEvaluation other) || other.ZCulled ||
                    !FishInterestLodMath.TryUnion(host.Layer, other.Layer, out FishInterestLodLayer union)) { UnsupportedRows++; continue; }
                if ((int)union == original.newLayer) continue;
                var replacement = original; replacement.newLayer = (int)union;
                changes.Add(new Change { Index = index, Record = record, Request = request, Original = original, Replacement = replacement });
            }
            CheckBatch(call); Check();
            foreach (Change change in changes)
            {
                CheckBatch(call); ValidateRecord(change.Record);
                AutoActivatorJob.LODData currentData = Read(() => call.Items[change.Index]);
                if (Read(() => Pointer(currentData)) != change.Record.Pointer ||
                    !SameRequest(Read(() => call.Requests[change.Index]), change.Request) || !SameResult(Read(() => call.Results[change.Index]), change.Original))
                    throw new InvalidOperationException("Original row changed before union classification.");
                Write(() => call.Results[change.Index] = change.Replacement, call);
                if (!SameResult(Read(() => call.Results[change.Index]), change.Replacement)) throw new InvalidOperationException("Union classification readback is unknown.");
                AppliedRows++;
            }
            CheckBatch(call); JoinedBatches++; Status = "Host fish LOD union: original completed rows extended; native ABI/coverage unverified.";
        }
        private void EndCompletion(CompletionCall call, bool exception)
        {
            if (call == null) return;
            if (_completionScopes.Count == 0 || !ReferenceEquals(_completionScopes[_completionScopes.Count - 1], call)) { Fault("CompletionScopePairingFailed"); return; }
            _completionScopes.RemoveAt(_completionScopes.Count - 1); call.Usable = false;
            if (exception) Fault("OriginalLodConsumerException");
        }
        private static bool Bits(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
        private static bool SameRequest(DRLODJob.RequestedData a, DRLODJob.RequestedData b) => a.lodDataHashID == b.lodDataHashID &&
            Bits(a.position.x, b.position.x) && Bits(a.position.y, b.position.y) && Bits(a.position.z, b.position.z) &&
            Bits(a.sizeInViewPort, b.sizeInViewPort) && Bits(a.customOutRange, b.customOutRange) && Bits(a.worldSize, b.worldSize);
        private static bool SameProducer(DRLODJob.RequestedData a, DRLODJob.RequestedData b) =>
            Bits(a.position.x, b.position.x) && Bits(a.position.y, b.position.y) && Bits(a.position.z, b.position.z) &&
            Bits(a.sizeInViewPort, b.sizeInViewPort) && Bits(a.customOutRange, b.customOutRange) && Bits(a.worldSize, b.worldSize);
        private static bool SameResult(DRLODJob.ResultData a, DRLODJob.ResultData b) => a.lodDataHashID == b.lodDataHashID && a.beforeLayer == b.beforeLayer &&
            a.newLayer == b.newLayer && Bits(a.sqrRange, b.sqrRange) && Bits(a.result.x, b.result.x) && Bits(a.result.y, b.result.y) && Bits(a.result.z, b.result.z) && a.isZCullOver == b.isZCullOver;
        private static bool SameRegion(FishInterestLodRegion a, FishInterestLodRegion b) => a.ViewProjection == b.ViewProjection && a.CameraPosition == b.CameraPosition &&
            Bits(a.OutRangeAdded, b.OutRangeAdded) && Bits(a.CullZ, b.CullZ) && Bits(a.InMarginX, b.InMarginX) && Bits(a.OutMarginX, b.OutMarginX) && Bits(a.InMarginY, b.InMarginY) && Bits(a.OutMarginY, b.OutMarginY);
        private void Fault(string reason) { _failed = true; Status = "Host fish LOD extension unavailable: " + reason; Log(reason); }
        private void Log(string category)
        { if (_logs >= MaxLogs) return; _logs++; try { _logger.LogInfo("DAVECOOP_HOST_FISH_LOD: " + category + "; NativeFieldAbiVerified=false FullCoverageVerified=false WorldAuthority=false CargoAuthority=false"); } catch { _failed = true; } }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _cleanupAttempted, 1) != 0) return;
            _installed = false; Retired.Add(this);
            if (_busy || _nativeStep || _fishScopes.Count != 0 || _completionScopes.Count != 0)
            { Fault("StoppedWithActiveCallbacks"); return; } // keep owner/hooks; never retry hot unpatch
            try
            {
                _harmony?.UnpatchSelf();
                foreach (MethodInfo target in _targets) if (Harmony.GetPatchInfo(target)?.Owners.Contains(Owner) == true)
                    throw new InvalidOperationException("Own host LOD hook remains registered.");
                if (ReferenceEquals(_active, this)) _active = null;
                Status = "Host fish LOD hooks removed; references retained for process lifetime."; Log("STOPPED");
            }
            catch { Fault("CleanupUnknown"); } // retained owner, no repeated unpatch
        }

        private static void FishBefore(SABaseFishSystem __instance, out FishCall __state) => __state = _active?.BeginFish(__instance);
        private static void FishFinally(FishCall __state, Exception __exception) => _active?.EndFish(__state, __exception != null);
        private static void RegisterBefore(AutoActivatorJob __instance, Component __0, out RegistrationCall __state) => __state = _active?.BeginRegistration(__instance, __0);
        private static void RegisterAfter(RegistrationCall __state, AutoActivatorJob.LODData __result, bool __runOriginal) => _active?.EndRegistration(__state, __result, __runOriginal);
        private static void RegisterFinally(Exception __exception) { if (__exception != null) _active?.Fault("OriginalRegistrationException"); }
        private static void CompleteBefore(AutoActivatorJob __instance, out CompletionCall __state) => __state = _active?.BeginCompletion(__instance);
        private static void CompleteFinally(CompletionCall __state, Exception __exception) => _active?.EndCompletion(__state, __exception != null);
        private static void JoinBefore(JobHandle __instance, out JoinCall __state) => __state = _active?.BeginJoin(__instance);
        private static void JoinAfter(JoinCall __state, bool __runOriginal) { if (__state != null) __state.Returned = __runOriginal; }
        // Unlike the earlier read-only loot finalizers, this new control hook
        // deliberately performs its bounded result write in the normal finalizer,
        // after join and before the caller's original consumer. Exceptions remain
        // unchanged. Later foreign finalizer effects are not verified.
        private static void JoinFinally(JoinCall __state, Exception __exception) => _active?.AfterJoined(__state, __exception != null);
    }
}
