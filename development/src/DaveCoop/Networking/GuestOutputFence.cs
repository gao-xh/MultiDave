using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;

namespace DaveCoop.Networking
{
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
        private Dictionary<MethodBase, GuestOutputTarget> _targets;
        private Dictionary<Type, object> _failureByReturnType;
        private Harmony _harmony;
        private bool _blocking, _healthy, _ownHooksRemoved = true;
        private int _pendingCalls;
        private long _blockedCalls, _unknownCalls, _unexpectedThreads;

        public GuestOutputFence(int unityThreadId, Guid leaseId)
        {
            if (unityThreadId < 1 || leaseId == Guid.Empty) throw new ArgumentException("Invalid guest output lease.");
            _unityThreadId = unityThreadId; _leaseId = leaseId;
        }
        public Guid LeaseId => _leaseId;
        public bool Healthy { get { lock (_gate) return _healthy && _blocking && _harmony != null && Volatile.Read(ref _processFailed) == 0; } }
        public bool Active { get { lock (_gate) return _blocking; } }
        public bool OwnHooksRemoved { get { lock (_gate) return _ownHooksRemoved; } }
        public int PendingCalls { get { lock (_gate) return _pendingCalls; } }
        public int TargetCount { get { lock (_gate) return _targets?.Count ?? 0; } }
        public long BlockedCalls { get { lock (_gate) return _blockedCalls; } }
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
                if (_harmony != null) return _healthy && _blocking;
                if (!_ownHooksRemoved) return false;
            }
            // Resolve and validate every target before touching the patch table.
            List<GuestOutputTarget> targets;
            try { targets = GuestOutputTargetManifest.Resolve(); }
            catch { Interlocked.Exchange(ref _processFailed, 1); return false; }
            if (Interlocked.CompareExchange(ref _activeFence, this, null) != null) return false;
            try
            {
                lock (_gate)
                {
                    _targets = targets.ToDictionary(target => (MethodBase)target.Method);
                    _failureByReturnType = targets.GroupBy(target => target.Method.ReturnType)
                        .ToDictionary(group => group.Key, group => group.FirstOrDefault(target => !(target.FailureResult is ulong value && value == ulong.MaxValue))?.FailureResult ?? group.First().FailureResult);
                    _blocking = true; _healthy = false; _ownHooksRemoved = false;
                    _harmony = new Harmony(Owner);
                }
                foreach (GuestOutputTarget target in targets)
                {
                    bool invalidUlong = target.Method.ReturnType == typeof(ulong) && target.FailureResult is ulong invalid && invalid == ulong.MaxValue;
                    string prefixName = target.DataOutIndex == 1 ? nameof(BlockLoadJson) : target.DataOutIndex == 2 ? nameof(BlockLoadSlot) :
                        target.Method.ReturnType == typeof(void) ? nameof(BlockVoid) : invalidUlong ? nameof(BlockInvalidUlong) : nameof(BlockResult);
                    MethodInfo prefix = typeof(GuestOutputFence).GetMethod(prefixName, BindingFlags.Static | BindingFlags.NonPublic);
                    if (prefixName == nameof(BlockResult)) prefix = prefix.MakeGenericMethod(target.Method.ReturnType);
                    else if (target.DataOutIndex >= 0) prefix = prefix.MakeGenericMethod(target.Method.GetParameters()[target.DataOutIndex].ParameterType.GetElementType());
                    _harmony.Patch(target.Method, prefix: new HarmonyMethod(prefix) { priority = Priority.First });
                }
                bool matched = targets.All(target => Harmony.GetPatchInfo(target.Method)?.Owners.Contains(Owner) == true);
                lock (_gate) _healthy = matched;
                if (!matched) { Interlocked.Exchange(ref _processFailed, 1); return false; }
                return true;
            }
            catch
            {
                // A partial fence is retained, never called healthy. Its owner
                // alone may remove it after original-root readback/quiescence.
                Interlocked.Exchange(ref _processFailed, 1);
                lock (_gate) _healthy = false;
                return false;
            }
        }

        // Caller is the owning backend, after transaction root and boundary
        // checks. No Dispose/finalizer silently unpatches an active guest fence.
        public bool Remove()
        {
            if (Environment.CurrentManagedThreadId != _unityThreadId) return false;
            lock (_gate)
            {
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
                    if (_targets != null && _targets.TryGetValue(original, out GuestOutputTarget target)) failure = target.FailureResult;
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
        public GuestOutputTarget(MethodInfo method, object failureResult, int dataOutIndex = -1)
        { Method = method; FailureResult = failureResult; DataOutIndex = dataOutIndex; }
    }
}
