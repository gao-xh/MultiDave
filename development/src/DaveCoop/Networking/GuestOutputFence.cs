using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;

namespace DaveCoop.Networking
{
    internal enum GuestOutputFenceProfile { ExistingCaches, NaturalInitialization }

    // A lease-owned fence for explicitly enumerated output declarations. It is
    // dormant until the shadow backend installs it. This is not an exhaustive
    // writer/ABI or quiescence proof and never grants GuestStateIsolated.
    internal sealed class GuestOutputFence
    {
        private const string Owner = Plugin.Id + ".guest-output";
        private static GuestOutputFence _activeFence;
        private static int _processFailed;
        private readonly object _gate = new object();
        private readonly int _unityThreadId;
        private readonly Guid _leaseId;
        private readonly GuestOutputFenceProfile _profile;
        private readonly HashSet<MethodBase> _installedTargets = new HashSet<MethodBase>();
        private Dictionary<MethodBase, GuestOutputTarget> _targets;
        private Dictionary<Type, object> _failureByReturnType;
        private Harmony _harmony;
        private bool _blocking, _healthy, _ownHooksRemoved = true;
        private bool _installing, _installationAttempted, _sealing, _sealAttempted, _initializationSealed;
        private int _patchAttemptedCount;
        private int _pendingCalls;
        private long _blockedCalls, _blockedFileOperations, _unknownCalls, _unexpectedThreads;

        public GuestOutputFence(int unityThreadId, Guid leaseId) : this(unityThreadId, leaseId, GuestOutputFenceProfile.ExistingCaches) { }

        public GuestOutputFence(int unityThreadId, Guid leaseId, GuestOutputFenceProfile profile)
        {
            if (unityThreadId < 1 || leaseId == Guid.Empty) throw new ArgumentException("Invalid guest output lease.");
            if (profile != GuestOutputFenceProfile.ExistingCaches && profile != GuestOutputFenceProfile.NaturalInitialization)
                throw new ArgumentException("Invalid guest output profile.", nameof(profile));
            _unityThreadId = unityThreadId; _leaseId = leaseId; _profile = profile;
            _initializationSealed = profile == GuestOutputFenceProfile.ExistingCaches;
        }
        public Guid LeaseId => _leaseId;
        public GuestOutputFenceProfile Profile => _profile;
        public bool InitializationSealed { get { lock (_gate) return _initializationSealed; } }
        public bool SealAttempted { get { lock (_gate) return _sealAttempted; } }
        public bool Healthy { get { lock (_gate) return _healthy && _blocking && _harmony != null && Volatile.Read(ref _processFailed) == 0; } }
        public bool Active { get { lock (_gate) return _blocking; } }
        public bool OwnHooksRemoved { get { lock (_gate) return _ownHooksRemoved; } }
        public int PendingCalls { get { lock (_gate) return _pendingCalls; } }
        public int TargetCount { get { lock (_gate) return _targets?.Count ?? 0; } }
        public int InitialBlockedTargetCount { get { lock (_gate) return _targets?.Values.Count(target => _profile == GuestOutputFenceProfile.ExistingCaches || !target.AllowNaturalInitialization) ?? 0; } }
        public int DeferredTargetCount { get { lock (_gate) return _targets?.Values.Count(target => _profile == GuestOutputFenceProfile.NaturalInitialization && target.AllowNaturalInitialization && !_installedTargets.Contains(target.Method)) ?? 0; } }
        public int InstalledTargetCount { get { lock (_gate) return _installedTargets.Count; } }
        public int PatchAttemptedCount { get { lock (_gate) return _patchAttemptedCount; } }
        public long BlockedCalls { get { lock (_gate) return _blockedCalls; } }
        public long BlockedFileOperations { get { lock (_gate) return _blockedFileOperations; } }
        public long UnknownCalls { get { lock (_gate) return _unknownCalls; } }
        public long UnexpectedThreads { get { lock (_gate) return _unexpectedThreads; } }
        public bool AllPersistentOutputsVerified => false;
        public bool NativeBlockingAbiVerified => false;
        public bool NativeQuiescenceVerified => false;

        public bool Install()
        {
            if (Environment.CurrentManagedThreadId != _unityThreadId || Volatile.Read(ref _processFailed) != 0) return false;
            lock (_gate)
            {
                if (_installing || _sealing) { FailLocked(); return false; }
                if (_harmony != null) return _healthy && _blocking;
                if (!_ownHooksRemoved) return false;
                if (_profile == GuestOutputFenceProfile.NaturalInitialization && _installationAttempted) return false;
                _installationAttempted = true;
                _installing = true;
            }
            try
            {
                // Resolve the complete inventory even when only the persistent
                // phase is installed. Resolution may initialize interop types;
                // callback failures remain latched before any later patch.
                List<GuestOutputTarget> targets = GuestOutputTargetManifest.Resolve(_profile);
                if (Volatile.Read(ref _processFailed) != 0) return false;
                List<GuestOutputTarget> initial = targets.Where(target => _profile == GuestOutputFenceProfile.ExistingCaches || !target.AllowNaturalInitialization).ToList();
                if (Interlocked.CompareExchange(ref _activeFence, this, null) != null) return false;
                lock (_gate)
                {
                    _targets = targets.ToDictionary(target => (MethodBase)target.Method);
                    _failureByReturnType = targets.GroupBy(target => target.Method.ReturnType)
                        .ToDictionary(group => group.Key, group => group.FirstOrDefault(target => !(target.FailureResult is ulong value && value == ulong.MaxValue))?.FailureResult ?? group.First().FailureResult);
                    _blocking = true; _healthy = false; _ownHooksRemoved = false;
                    _harmony = new Harmony(Owner);
                }
                foreach (GuestOutputTarget target in initial)
                {
                    RequirePatchWindow(sealing: false);
                    Patch(target, sealing: false);
                    RequirePatchWindow(sealing: false);
                }
                bool matched = initial.All(HasOwnPatch);
                RequirePatchWindow(sealing: false);
                lock (_gate) _healthy = matched && Volatile.Read(ref _processFailed) == 0;
                if (!matched) { Interlocked.Exchange(ref _processFailed, 1); return false; }
                return Healthy;
            }
            catch
            {
                // A partial fence is retained, never called healthy. Its owner
                // alone may remove it after original-root readback/quiescence.
                Interlocked.Exchange(ref _processFailed, 1);
                lock (_gate) _healthy = false;
                return false;
            }
            finally { lock (_gate) _installing = false; }
        }

        // The source-bound caller seals only after the observed original load
        // has returned, before clone/install. No existing persistent hook is
        // removed. A partial extension is retained, unhealthy and never retried.
        public bool SealInitialization()
        {
            if (_profile == GuestOutputFenceProfile.ExistingCaches) return Healthy;
            List<GuestOutputTarget> deferred;
            lock (_gate)
            {
                if (_sealAttempted)
                {
                    if (_sealing) FailLocked();
                    return false;
                }
                _sealAttempted = true;
                if (Environment.CurrentManagedThreadId != _unityThreadId || _installing || !_healthy || !_blocking ||
                    _harmony == null || _targets == null || _pendingCalls != 0 || Volatile.Read(ref _processFailed) != 0)
                { FailLocked(); return false; }
                _sealing = true; _healthy = false;
                deferred = _targets.Values.Where(target => target.AllowNaturalInitialization).ToList();
            }
            try
            {
                foreach (GuestOutputTarget target in deferred)
                {
                    RequirePatchWindow(sealing: true);
                    Patch(target, sealing: true);
                    RequirePatchWindow(sealing: true);
                }
                bool matched = _targets.Values.All(HasOwnPatch);
                RequirePatchWindow(sealing: true);
                lock (_gate)
                {
                    _initializationSealed = matched && Volatile.Read(ref _processFailed) == 0;
                    _healthy = _initializationSealed;
                }
                if (!matched) { Interlocked.Exchange(ref _processFailed, 1); return false; }
                return Healthy;
            }
            catch
            {
                lock (_gate) FailLocked();
                return false;
            }
            finally { lock (_gate) _sealing = false; }
        }

        private void RequirePatchWindow(bool sealing)
        {
            lock (_gate)
                if (Environment.CurrentManagedThreadId != _unityThreadId || Volatile.Read(ref _processFailed) != 0 ||
                    !ReferenceEquals(Volatile.Read(ref _activeFence), this) || !_blocking || _ownHooksRemoved || _harmony == null ||
                    (sealing ? !_sealing || _installing : !_installing || _sealing))
                    throw new InvalidOperationException("Guest output patch phase lost its owned window.");
        }

        private void Patch(GuestOutputTarget target, bool sealing)
        {
            bool invalidUlong = target.Method.ReturnType == typeof(ulong) && target.FailureResult is ulong invalid && invalid == ulong.MaxValue;
            string prefixName = target.DataOutIndex == 1 ? nameof(BlockLoadJson) : target.DataOutIndex == 2 ? nameof(BlockLoadSlot) :
                target.Method.ReturnType == typeof(void) ? nameof(BlockVoid) : invalidUlong ? nameof(BlockInvalidUlong) : nameof(BlockResult);
            MethodInfo prefix = typeof(GuestOutputFence).GetMethod(prefixName, BindingFlags.Static | BindingFlags.NonPublic);
            if (prefixName == nameof(BlockResult)) prefix = prefix.MakeGenericMethod(target.Method.ReturnType);
            else if (target.DataOutIndex >= 0) prefix = prefix.MakeGenericMethod(target.Method.GetParameters()[target.DataOutIndex].ParameterType.GetElementType());
            RequirePatchWindow(sealing);
            lock (_gate) _patchAttemptedCount++;
            _harmony.Patch(target.Method, prefix: new HarmonyMethod(prefix) { priority = Priority.First });
            if (!HasOwnPatch(target)) throw new InvalidOperationException("Guest output patch did not read back its owner.");
            lock (_gate) _installedTargets.Add(target.Method);
        }

        private static bool HasOwnPatch(GuestOutputTarget target) => Harmony.GetPatchInfo(target.Method)?.Owners.Contains(Owner) == true;

        private void FailLocked()
        { _healthy = false; Interlocked.Exchange(ref _processFailed, 1); }

        // Caller is the owning backend, after transaction root and boundary
        // checks. No Dispose/finalizer silently unpatches an active guest fence.
        public bool Remove()
        {
            if (Environment.CurrentManagedThreadId != _unityThreadId) return false;
            lock (_gate)
            {
                if (_installing || _sealing) { FailLocked(); return false; }
                if (_harmony == null) return _ownHooksRemoved && !_blocking;
                if (_pendingCalls != 0) return false;
            }
            try
            {
                foreach (GuestOutputTarget target in _targets.Values)
                    _harmony.Unpatch(target.Method, HarmonyPatchType.All, Owner);
                bool removed = _targets.Values.All(target => Harmony.GetPatchInfo(target.Method)?.Owners.Contains(Owner) != true);
                if (!removed) throw new InvalidOperationException("Own guest output hooks remain installed.");
                lock (_gate)
                {
                    _healthy = false; _blocking = false; _ownHooksRemoved = true; _harmony = null;
                    _installedTargets.Clear();
                }
                Interlocked.CompareExchange(ref _activeFence, null, this);
                return true;
            }
            catch
            {
                Interlocked.Exchange(ref _processFailed, 1);
                lock (_gate) { _healthy = false; _blocking = true; _ownHooksRemoved = false; }
                return false;
            }
        }

        private static bool BlockVoid(MethodBase __originalMethod)
        {
            GuestOutputFence fence = Volatile.Read(ref _activeFence);
            return fence == null || !fence.ShouldBlock(__originalMethod, typeof(void), out _);
        }
        private static bool BlockInvalidUlong(MethodBase __originalMethod, ref ulong __result)
        {
            GuestOutputFence fence = Volatile.Read(ref _activeFence);
            if (fence == null || !fence.ShouldBlock(__originalMethod, typeof(ulong), out _)) return true;
            __result = ulong.MaxValue; return false;
        }
        private static bool BlockLoadJson<T>(MethodBase __originalMethod, ref T __1, ref bool __result)
        {
            GuestOutputFence fence = Volatile.Read(ref _activeFence);
            if (fence == null || !fence.ShouldBlock(__originalMethod, typeof(bool), out _)) return true;
            __1 = default; __result = false; return false;
        }
        private static bool BlockLoadSlot<T>(MethodBase __originalMethod, ref T __2, ref bool __result)
        {
            GuestOutputFence fence = Volatile.Read(ref _activeFence);
            if (fence == null || !fence.ShouldBlock(__originalMethod, typeof(bool), out _)) return true;
            __2 = default; __result = false; return false;
        }
        private static bool BlockResult<T>(MethodBase __originalMethod, ref T __result)
        {
            GuestOutputFence fence = Volatile.Read(ref _activeFence);
            if (fence == null || !fence.ShouldBlock(__originalMethod, typeof(T), out object failure)) return true;
            // All return values are prebuilt CLR primitive/enum/value wrappers.
            // No game constructor, original getter or JSON is used in a prefix.
            __result = failure is T typed ? typed : default;
            return false;
        }
        private bool ShouldBlock(MethodBase original, Type returnType, out object failure)
        {
            failure = null;
            lock (_gate)
            {
                if (!_blocking) return false;
                _pendingCalls++;
                try
                {
                    _blockedCalls++;
                    if (Environment.CurrentManagedThreadId != _unityThreadId)
                    { _unexpectedThreads++; _healthy = false; Interlocked.Exchange(ref _processFailed, 1); }
                    if (_targets != null && _targets.TryGetValue(original, out GuestOutputTarget target))
                    {
                        failure = target.FailureResult;
                        // The exact natural-startup File declarations are void.
                        // Skipping them cannot mean successful copy/delete: the
                        // source must refuse startup after any such attempt.
                        if (target.IsFileOperation) { _blockedFileOperations++; FailLocked(); }
                    }
                    else
                    {
                        _failureByReturnType?.TryGetValue(returnType, out failure);
                        _unknownCalls++; _healthy = false; Interlocked.Exchange(ref _processFailed, 1);
                    }
                    return true;
                }
                finally { _pendingCalls--; }
            }
        }
    }

    internal sealed class GuestOutputTarget
    {
        public MethodInfo Method { get; }
        public object FailureResult { get; }
        public int DataOutIndex { get; }
        public bool AllowNaturalInitialization { get; }
        public bool IsFileOperation { get; }
        public GuestOutputTarget(MethodInfo method, object failureResult, int dataOutIndex = -1, bool allowNaturalInitialization = false, bool isFileOperation = false)
        { Method = method; FailureResult = failureResult; DataOutIndex = dataOutIndex; AllowNaturalInitialization = allowNaturalInitialization; IsFileOperation = isFileOperation; }
    }
}
