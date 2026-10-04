using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using BepInEx.Logging;
using DaveCoop.Core.World;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;
using OrdinaryMove = FishAllocator._InstanceCheckRoutine_d__59;
using WaveMove = FishWaveAllocator._WaveInstanceCheckRoutine_d__2;

namespace DaveCoop.Networking
{
    // Experimental ordinary-allocator distance input only. The original center
    // getter runs, including its real cache writes. No transform, cache, force,
    // radius, coroutine, saved UID, selection, spawn or stop method is written
    // or actively called. A synchronous scope is not a direct-caller proof.
    internal sealed class NativeHostFishAllocatorArea : IDisposable
    {
        public const int MaxScopes = 32;
        public const int MaxReadsPerMove = 8192;
        public const int MaxLogs = 32;
        private const double CoordinateBound = FishAllocatorInterestMath.CoordinateBound;
        private const string Owner = Plugin.Id + ".host-fish-allocator-area";
        private static NativeHostFishAllocatorArea _active;
        private static int _processFailed;
        private readonly HostFishInterestSource _source;
        private readonly ManualLogSource _logger;
        private readonly Stack<Scope> _scopes = new Stack<Scope>();
        private readonly List<MethodInfo> _targets = new List<MethodInfo>();
        private Harmony _harmony;
        private bool _installAttempted, _accepting, _reading, _cleanupVerified = true;
        private int _cleanupAttempted, _logs;
        private long _callbackErrors, _proxyReturns;

        private enum ScopeKind { Unknown, Ordinary, Position, Center }
        private sealed class Scope
        {
            public ScopeKind Kind;
            public Scope Parent;
            public MoveRecord Move;
            public bool Eligible, After;
        }
        // Native wrappers remain in this synchronous call only. Framework
        // wrappers own strong GC handles; none is queued or retained over yield.
        // There is no async pointer->owner table, allocation or raw-native lease.
        private sealed class MoveRecord
        {
            public Scope Scope;
            public OrdinaryMove Iterator;
            public FishAllocator Allocator;
            public HostFishInterestWindow Window;
            public IntPtr IteratorPointer, IteratorClass, AllocatorPointer, AllocatorClass, AllocatorUnity;
            public IntPtr TransformPointer, TransformUnity;
            public int AllocatorId, Reads;
            public float Radius;
            public bool PositionSeen, PositionConsumed;
            public Vector3 Position;
        }
        private sealed class Expired : Exception { }

        public bool Installed => _harmony != null && _accepting;
        public bool Failed => Volatile.Read(ref _processFailed) != 0 || _source.Failed;
        public bool Healthy => Installed && !Failed;
        public bool CleanupVerified => _cleanupVerified;
        public long CallbackErrors => Interlocked.Read(ref _callbackErrors);
        public long ProxyReturns => Interlocked.Read(ref _proxyReturns);
        public int TargetCount => _targets.Count;
        public int PendingScopes => _scopes.Count;
        public string Status { get; private set; } = "Allocator area: disabled until explicitly installed.";
        public bool NativeTypedReturnAbiVerified => false;
        public bool NativeRuntimeVerified => false;
        public bool FullAllocatorCoverageVerified => false;
        public bool DirectCallerVerified => false;
        public bool FloatDistanceEquivalenceVerified => false;
        public bool GuestStateIsolated => false;
        public bool WorldAuthority => false;
        public bool CargoAuthority => false;

        public NativeHostFishAllocatorArea(HostFishInterestSource source, ManualLogSource logger)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // Only the default-off controller calls this. Reflection is declaration
        // checking, not an original method invocation or a native field read.
        public void Install()
        {
            if (Healthy) return;
            if (_installAttempted || Failed)
                throw new InvalidOperationException("Allocator area installation is single-use after failure or disposal.");
            if (Interlocked.CompareExchange(ref _active, this, null) != null)
                throw new InvalidOperationException("Another allocator area adapter remains installed.");
            _installAttempted = true;
            try
            {
                _harmony = new Harmony(Owner); _cleanupVerified = false;
                Patch(typeof(OrdinaryMove), "MoveNext", typeof(bool), Type.EmptyTypes,
                    nameof(MoveBefore), nameof(ScopeAfter));
                Patch(typeof(Transform), "get_position", typeof(Vector3), Type.EmptyTypes,
                    nameof(PositionBefore), nameof(PositionAfter));
                Patch(typeof(FishAllocator), "get_GetCenterPos", typeof(Vector3), Type.EmptyTypes,
                    nameof(CenterBefore), nameof(CenterAfter));
                // Every declaration below was checked in the offline allocator
                // metadata. Masks neither skip nor alter these originals.
                Mask(typeof(FishAllocator), "InstanceCheckRoutine", typeof(Il2CppSystem.Collections.IEnumerator), typeof(bool));
                Mask(typeof(WaveMove), "MoveNext", typeof(bool));
                Mask(typeof(FishWaveAllocator), "WaveInstanceCheckRoutine", typeof(Il2CppSystem.Collections.IEnumerator), typeof(bool));
                Mask(typeof(FishWaveAllocator), "Spawn", typeof(void), typeof(bool));
                foreach (string name in new[] { "FindSetWayPoint", "FindSetWayPointGlobal", "OnEnable", "Awake", "OnDestroy",
                    "Reset", "Spawn", "Despawn", "OnDisable", "OnDrawGizmosSelected", "OnDrawGizmos", "SetBoundCenter",
                    "TempCheckSpawn", "ResetCenterPos", "_DelayedSpawn_b__63_0" })
                    Mask(typeof(FishAllocator), name, typeof(void));
                Mask(typeof(FishAllocator), "Reset", typeof(void), typeof(bool));
                Mask(typeof(FishAllocator), "DelayedSpawn", typeof(void), typeof(int));
                Mask(typeof(FishAllocator), "Spawn", typeof(void), typeof(bool));
                Mask(typeof(FishAllocator), "StopAlloc", typeof(void), typeof(bool));
                Mask(typeof(FishAllocator), "GetAllocatorUID", typeof(string));
                Mask(typeof(FishAllocator), "get_GetCenterPosByWayPoint", typeof(Vector3));
                Mask(typeof(FishAllocator), "GetRandomFishGroup", typeof(FishGroupsSelector));
                Mask(typeof(FishAllocator), "DoInstanceFishOrGroup", typeof(GameObject),
                    typeof(GameObject), typeof(Il2CppSystem.Action<DR.AI.FishAISystem>), typeof(bool));
                _accepting = true;
                Status = "Allocator area: installed; requires a current real host observation.";
            }
            catch (Exception error)
            {
                Fail("InstallationFailed", error); Dispose();
                throw new InvalidOperationException("Own allocator area declarations or registrations were unavailable.");
            }
        }

        private void Mask(Type type, string name, Type returns, params Type[] parameters)
            => Patch(type, name, returns, parameters, nameof(MaskBefore), nameof(ScopeAfter));

        private void Patch(Type type, string name, Type returns, Type[] parameters, string before, string after)
        {
            MethodInfo method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly, null, parameters, null);
            if (method == null || method.IsStatic || method.IsGenericMethod || method.ReturnType != returns || _targets.Contains(method))
                throw new InvalidOperationException("Exact allocator area declaration is unavailable.");
            _targets.Add(method);
            _harmony.Patch(method, prefix: Hook(before, Priority.First), postfix: Hook(after, Priority.Last),
                finalizer: Hook(nameof(ScopeFinally), Priority.Last));
            if (Harmony.GetPatchInfo(method)?.Owners.Contains(Owner) != true)
                throw new InvalidOperationException("Own allocator area registration is unavailable.");
        }

        private static HarmonyMethod Hook(string name, int priority)
            => new HarmonyMethod(typeof(NativeHostFishAllocatorArea).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)) { priority = priority };

        private bool CallbackThread()
        {
            if (!_accepting || Failed || _source.UnityThreadId == 0) return false;
            if (Environment.CurrentManagedThreadId == _source.UnityThreadId) return true;
            Fail("CallbackChangedThread", null); return false;
        }

        private Scope Push(ScopeKind kind, MoveRecord move = null)
        {
            if (!CallbackThread()) return null;
            if (_scopes.Count >= MaxScopes) { Fail("ScopeQuota", null); return null; }
            var scope = new Scope { Kind = kind, Parent = _scopes.Count == 0 ? null : _scopes.Peek(), Move = move };
            _scopes.Push(scope); return scope;
        }

        private Scope BeginMove(OrdinaryMove iterator, bool originalWillRun)
        {
            Scope scope = Push(ScopeKind.Unknown);
            if (scope == null || scope.Parent != null || _reading || _source.IsReading || !originalWillRun) return scope;
            try
            {
                HostFishInterestWindow window = null;
                _reading = true;
                try { if (!_source.TryCapture(out window)) return scope; }
                finally { _reading = false; }
                if (Failed || window == null) return scope;
                var record = new MoveRecord { Scope = scope, Iterator = iterator, Window = window };
                scope.Move = record; scope.Kind = ScopeKind.Ordinary;
                record.IteratorPointer = Read(record, () => iterator.Pointer);
                if (record.IteratorPointer == IntPtr.Zero) return scope;
                record.IteratorClass = Read(record, () => IL2CPP.il2cpp_object_get_class(record.IteratorPointer));
                if (record.IteratorClass != Read(record, () => Il2CppClassPointerStore<OrdinaryMove>.NativeClassPtr)) return scope;
                int state = Read(record, () => iterator.__1__state);
                if (state < 0 || state > 5 || Read(record, () => iterator.force)) return scope;
                record.Allocator = Read(record, () => iterator.__4__this);
                if (ReferenceEquals(record.Allocator, null)) return scope;
                record.AllocatorPointer = Read(record, () => record.Allocator.Pointer);
                if (record.AllocatorPointer == IntPtr.Zero) return scope;
                record.AllocatorClass = Read(record, () => IL2CPP.il2cpp_object_get_class(record.AllocatorPointer));
                if (record.AllocatorClass != Read(record, () => Il2CppClassPointerStore<FishAllocator>.NativeClassPtr)) return scope;
                record.AllocatorUnity = Read(record, () => record.Allocator.m_CachedPtr);
                if (record.AllocatorUnity == IntPtr.Zero) return scope;
                record.AllocatorId = Read(record, () => record.Allocator.GetInstanceID());
                record.TransformPointer = Read(record, () => window.LocalPlayerTransform.Pointer);
                record.TransformUnity = Read(record, () => window.LocalPlayerTransform.m_CachedPtr);
                record.Radius = Read(record, () => record.Allocator.spawnCheckDistance);
                scope.Eligible = record.AllocatorUnity != IntPtr.Zero && record.AllocatorId != 0 &&
                    record.TransformPointer != IntPtr.Zero && record.TransformUnity != IntPtr.Zero &&
                    float.IsFinite(record.Radius) && record.Radius > 0 && record.Radius <= CoordinateBound &&
                    Read(record, () => record.Allocator.spawnCheckMinDistance) == 0 && SameAllocatorScene(record) && Guard(record);
            }
            catch (Expired) { scope.Eligible = false; }
            catch (Exception error) { Fail("MoveSourceReadFailed", error); }
            return scope;
        }

        private Scope BeginPosition(Transform transform)
        {
            // Source capture/validation itself reads position. Those calls are
            // not the original allocator body's input and must remain invisible.
            // A worker's unrelated global Transform query has no main-thread
            // synchronous ancestry. Do not read its pointer or fault the host.
            if (_reading || _source.IsReading || _source.UnityThreadId == 0 ||
                Environment.CurrentManagedThreadId != _source.UnityThreadId || _scopes.Count == 0 || !CallbackThread()) return null;
            Scope parent = _scopes.Peek();
            Scope scope = Push(ScopeKind.Position, parent.Move);
            if (scope == null || parent.Kind != ScopeKind.Ordinary || !parent.Eligible) return scope;
            MoveRecord record = parent.Move;
            try
            {
                if (record.PositionConsumed) return scope;
                // A second natural position query makes the pair ambiguous.
                if (record.PositionSeen) { record.PositionSeen = false; record.PositionConsumed = true; return scope; }
                if (Read(record, () => transform.Pointer) != record.TransformPointer ||
                    Read(record, () => transform.m_CachedPtr) != record.TransformUnity || !Fresh(record, true)) return scope;
                scope.Eligible = true;
            }
            catch (Expired) { parent.Eligible = false; }
            catch (Exception error) { Fail("PositionIdentityReadFailed", error); }
            return scope;
        }

        private void EndPosition(Scope scope, Vector3 original, bool originalRan)
        {
            if (!ReadyAfter(scope) || !scope.Eligible || !originalRan) return;
            MoveRecord record = scope.Move;
            try
            {
                if (!Bounded(original) || !Same(original, record.Window.LocalPosition) || !Fresh(record, true)) return;
                record.Position = original; record.PositionSeen = true;
            }
            catch (Expired) { scope.Parent.Eligible = false; }
            catch (Exception error) { Fail("PositionResultReadFailed", error); }
        }

        private Scope BeginCenter(FishAllocator allocator)
        {
            if (_reading || _source.IsReading || _source.UnityThreadId == 0 ||
                Environment.CurrentManagedThreadId != _source.UnityThreadId || _scopes.Count == 0 || !CallbackThread()) return null;
            Scope parent = _scopes.Peek();
            Scope scope = Push(ScopeKind.Center, parent.Move);
            if (scope == null || parent.Kind != ScopeKind.Ordinary || !parent.Eligible) return scope;
            MoveRecord record = parent.Move;
            // Consumed before any new native read; no second attempt in this Move.
            bool paired = record.PositionSeen && !record.PositionConsumed;
            if (!paired) return scope;
            record.PositionConsumed = true; record.PositionSeen = false;
            try
            {
                scope.Eligible = Read(record, () => allocator.Pointer) == record.AllocatorPointer &&
                    Read(record, () => allocator.m_CachedPtr) == record.AllocatorUnity && Fresh(record, true);
            }
            catch (Expired) { parent.Eligible = false; }
            catch (Exception error) { Fail("CenterIdentityReadFailed", error); }
            return scope;
        }

        private void EndCenter(Scope scope, FishAllocator allocator, bool originalRan, ref Vector3 result)
        {
            if (!ReadyAfter(scope) || !scope.Eligible || !originalRan) return;
            MoveRecord record = scope.Move;
            try
            {
                if (!Bounded(result) || Read(record, () => allocator.Pointer) != record.AllocatorPointer ||
                    !Fresh(record, true)) return;
                var observed = record.Window.Interest.Position;
                var remote = new Vector3(observed.X, observed.Y, observed.Z);
                if (!FishAllocatorInterestMath.TryProxyCenter(ToNumerics(record.Position), ToNumerics(result), ToNumerics(remote), out System.Numerics.Vector3 candidate)) return;
                var proxy = new Vector3(candidate.X, candidate.Y, candidate.Z);
                // Native getter/cache has already run. This is the sole write,
                // to the transient return only, after the final source check.
                if (!Fresh(record, true) || !Guard(record)) return;
                result = proxy; Interlocked.Increment(ref _proxyReturns);
                Status = "Allocator area: ordinary distance return used a nearer current observation.";
            }
            catch (Expired) { scope.Parent.Eligible = false; }
            catch (Exception error) { Fail("CenterResultReadFailed", error); }
        }

        private bool SameAllocatorScene(MoveRecord record)
        {
            GameObject root = Read(record, () => record.Allocator.gameObject);
            if (ReferenceEquals(root, null)) return false;
            var scene = Read(record, () => root.scene);
            return Read(record, () => scene.handle) == record.Window.SceneHandle &&
                Read(record, () => scene.name) == record.Window.Interest.SceneKey &&
                Read(record, () => record.Allocator.isActiveAndEnabled);
        }

        private bool Fresh(MoveRecord record, bool executing)
        {
            if (!Guard(record)) return false;
            if (Read(record, () => record.Iterator.Pointer) != record.IteratorPointer ||
                Read(record, () => IL2CPP.il2cpp_object_get_class(record.IteratorPointer)) != record.IteratorClass ||
                Read(record, () => record.Iterator.force) ||
                (executing && Read(record, () => record.Iterator.__1__state) != -1)) return false;
            FishAllocator owner = Read(record, () => record.Iterator.__4__this);
            return !ReferenceEquals(owner, null) && Read(record, () => owner.Pointer) == record.AllocatorPointer &&
                Read(record, () => record.Allocator.Pointer) == record.AllocatorPointer &&
                Read(record, () => IL2CPP.il2cpp_object_get_class(record.AllocatorPointer)) == record.AllocatorClass &&
                Read(record, () => record.Allocator.m_CachedPtr) == record.AllocatorUnity &&
                Read(record, () => record.Allocator.GetInstanceID()) == record.AllocatorId &&
                Read(record, () => record.Allocator.spawnCheckMinDistance) == 0 &&
                Read(record, () => record.Allocator.spawnCheckDistance) == record.Radius &&
                Read(record, () => record.Window.LocalPlayerTransform.Pointer) == record.TransformPointer &&
                Read(record, () => record.Window.LocalPlayerTransform.m_CachedPtr) == record.TransformUnity &&
                SameAllocatorScene(record) && Guard(record);
        }

        private bool Guard(MoveRecord record)
        {
            if (!Healthy || _reading || _source.IsReading || _source.UnityThreadId == 0 ||
                Environment.CurrentManagedThreadId != _source.UnityThreadId || _scopes.Count == 0) return false;
            Scope top = _scopes.Peek();
            if (top != record.Scope && (top.Parent != record.Scope || top.Move != record ||
                (top.Kind != ScopeKind.Position && top.Kind != ScopeKind.Center))) return false;
            _reading = true;
            try { return _source.IsCurrent(record.Window) && Healthy; }
            finally { _reading = false; }
        }

        private T Read<T>(MoveRecord record, Func<T> read)
        {
            // This limits our primitive read/guard steps, not every native read
            // made inside Source.IsCurrent or composite Unity APIs. Its managed
            // validation path is finite; internal business-getter work and the
            // total native runtime cost have not been bounded or timed.
            if (!Guard(record)) throw new Expired();
            if (++record.Reads > MaxReadsPerMove) throw new InvalidOperationException("Allocator area native read quota.");
            _reading = true;
            T value;
            try { value = read(); }
            finally { _reading = false; }
            if (!Guard(record)) throw new Expired();
            return value;
        }

        private bool ReadyAfter(Scope scope)
        {
            if (scope == null || !CallbackThread()) return false;
            if (_scopes.Count == 0 || _scopes.Peek() != scope || scope.After)
            { Fail("PostfixScopeMismatch", null); return false; }
            scope.After = true; return true;
        }

        private void EndScope(Scope scope, Exception originalException)
        {
            if (scope == null) return;
            // Finalizer only reads CLR scope/thread facts; no native identity,
            // source validation, GC free or original exception substitution.
            if (_source.UnityThreadId == 0 || Environment.CurrentManagedThreadId != _source.UnityThreadId ||
                _scopes.Count == 0 || _scopes.Peek() != scope)
            { Fail("FinalizerScopeMismatch", null); return; }
            if (originalException != null && scope.Move != null) scope.Move.Scope.Eligible = false;
            _scopes.Pop();
            if (scope.Kind == ScopeKind.Unknown && scope.Parent?.Move != null && scope.Parent.Move.PositionSeen)
            {
                // Returning from an unknown nested call cannot revive a pending
                // position/center pair captured before that call.
                scope.Parent.Move.PositionSeen = false; scope.Parent.Move.PositionConsumed = true;
            }
        }

        private static System.Numerics.Vector3 ToNumerics(Vector3 value) => new System.Numerics.Vector3(value.x, value.y, value.z);
        private static bool Bounded(Vector3 value) => FishAllocatorInterestMath.Bounded(ToNumerics(value));
        private static bool Same(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;

        private void Fail(string reason, Exception error)
        {
            Interlocked.Exchange(ref _processFailed, 1); _accepting = false;
            Interlocked.Increment(ref _callbackErrors); Status = "Allocator area failed: " + reason + "; original behavior retained.";
            if (_source.UnityThreadId != 0 && Environment.CurrentManagedThreadId == _source.UnityThreadId)
                foreach (Scope scope in _scopes) scope.Eligible = false;
            if (_logs++ < MaxLogs)
            {
                try { _logger.LogWarning("DAVECOOP_HOST_ALLOCATOR_AREA_FAILED: " + reason + (error == null ? "" : "; " + error.GetType().Name)); }
                catch { /* Diagnostic failure must not escape an original call. */ }
            }
        }

        public void CheckHealthy()
        {
            if (_source.Failed && Volatile.Read(ref _processFailed) == 0) Fail("InterestSourceFailed", null);
            if (!Healthy) throw new InvalidOperationException("Host allocator area adapter is unavailable.");
        }

        public void Dispose()
        {
            _accepting = false;
            if (_harmony == null) return;
            if (_scopes.Count != 0)
            { Status = "Allocator area stopped with synchronous scopes retained; own cleanup deferred."; return; }
            if (Interlocked.CompareExchange(ref _cleanupAttempted, 1, 0) != 0) return;
            try
            {
                _harmony.UnpatchSelf();
                foreach (MethodInfo target in _targets)
                    if (Harmony.GetPatchInfo(target)?.Owners.Contains(Owner) == true)
                        throw new InvalidOperationException("Own allocator area hook remains registered.");
                _cleanupVerified = true; _harmony = null; _targets.Clear();
                Interlocked.CompareExchange(ref _active, null, this);
                Status = "Allocator area stopped; own registrations removed.";
            }
            catch (Exception error) { _cleanupVerified = false; Fail("OwnCleanupUnknown", error); }
        }

        private static void MoveBefore(OrdinaryMove __instance, bool __runOriginal, out Scope __state)
        { __state = _active?.BeginMove(__instance, __runOriginal); }
        private static void MaskBefore(out Scope __state)
        {
            NativeHostFishAllocatorArea active = _active;
            __state = active == null || active._source.UnityThreadId == 0 ||
                Environment.CurrentManagedThreadId != active._source.UnityThreadId || active._scopes.Count == 0
                ? null : active.Push(ScopeKind.Unknown);
        }
        private static void PositionBefore(Transform __instance, out Scope __state)
        { __state = _active?.BeginPosition(__instance); }
        private static void PositionAfter(Vector3 __result, bool __runOriginal, Scope __state)
        { _active?.EndPosition(__state, __result, __runOriginal); }
        private static void CenterBefore(FishAllocator __instance, out Scope __state)
        { __state = _active?.BeginCenter(__instance); }
        private static void CenterAfter(FishAllocator __instance, bool __runOriginal, Scope __state, ref Vector3 __result)
        { _active?.EndCenter(__state, __instance, __runOriginal, ref __result); }
        private static void ScopeAfter(Scope __state) { _active?.ReadyAfter(__state); }
        private static void ScopeFinally(Exception __exception, Scope __state) { _active?.EndScope(__state, __exception); }
    }
}
