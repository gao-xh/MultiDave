using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using BepInEx.Logging;
using DaveCoop.Core.World;
using DR.AI;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;
using NumVector = System.Numerics.Vector3;

namespace DaveCoop.Networking
{
    // Default-off eligibility for the original fish avoidance branch only.
    // No actual renderer, fish state, transform, AI task or capture is written.
    // Synchronous enclosure does not prove a direct caller or full coverage.
    internal sealed class NativeHostFishVisibilityArea : IDisposable
    {
        public const int MaxScopes = 32;
        public const int MaxParentDepth = 32;
        public const int MaxReadStepsPerGetter = 8192;
        public const int MaxLogs = 32;
        private const string Owner = Plugin.Id + ".host-fish-visibility-area";
        private static NativeHostFishVisibilityArea _active;
        private static int _processFailed;
        private readonly HostFishInterestSource _source;
        private readonly ManualLogSource _logger;
        private readonly Stack<Scope> _scopes = new Stack<Scope>();
        private readonly List<MethodInfo> _targets = new List<MethodInfo>();
        private Harmony _harmony;
        private bool _accepting, _installAttempted, _reading, _cleanupVerified = true;
        private int _cleanupAttempted, _logs;
        private long _proxyReturns, _unsupported, _missingInterest, _errors;

        private sealed class Scope
        {
            public Scope Parent;
            public SABaseFishSystem Fish;
            public Record Record;
            public bool Update, OriginalWillRun, Consumed, After, Eligible;
        }
        // These wrappers remain in this exact synchronous call. Framework
        // wrappers hold their own strong native GC handles. Nothing is queued
        // over yield, and there is no retained pointer-to-life/owner mapping.
        private sealed class Record
        {
            public Scope Scope;
            public HostFishInterestWindow Window;
            public SABaseFishSystem Fish;
            public GameObject Root, RendererRoot;
            public Transform RootTransform, RendererTransform;
            public Renderer Renderer;
            public IntPtr FishPointer, FishClass, FishUnity, RootPointer, RootUnity, RootTransformPointer, RootTransformUnity;
            public IntPtr RendererPointer, RendererClass, RendererUnity, RendererRootPointer, RendererRootUnity;
            public IntPtr RendererTransformPointer, RendererTransformUnity;
            public int FishId, RendererId, RendererLayer, Reads;
        }
        private sealed class CameraSample
        {
            public Camera Camera;
            public GameObject Root;
            public IntPtr Pointer, Unity, RootPointer, RootUnity;
            public int Id, SceneHandle, Mask;
            public float Near, Far;
            public Matrix4x4 View, Projection;
            public Bounds Bounds;
        }
        private sealed class Expired : Exception { }

        public bool Installed => _harmony != null && _accepting;
        public bool Failed => Volatile.Read(ref _processFailed) != 0 || _source.Failed;
        public bool Healthy => Installed && !Failed;
        public bool CleanupVerified => _cleanupVerified;
        public string Status { get; private set; } = "Fish visibility area: disabled.";
        public int TargetCount => _targets.Count;
        public int PendingScopes => _scopes.Count;
        public long ProxyReturns => Interlocked.Read(ref _proxyReturns);
        public long Unsupported => Interlocked.Read(ref _unsupported);
        public long MissingInterest => Interlocked.Read(ref _missingInterest);
        public long CallbackErrors => Interlocked.Read(ref _errors);
        public bool NativeTypedReturnAbiVerified => false;
        public bool NativeRuntimeVerified => false;
        public bool FullFishCoverageVerified => false;
        public bool DirectCallerVerified => false;
        public bool UnityVisibilityEquivalenceVerified => false;
        public bool WorldAuthority => false;
        public bool CargoAuthority => false;

        public NativeHostFishVisibilityArea(HostFishInterestSource source, ManualLogSource logger)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void Install()
        {
            if (Healthy) return;
            if (_installAttempted || Failed) throw new InvalidOperationException("Fish visibility area installation is single-use.");
            if (Interlocked.CompareExchange(ref _active, this, null) != null)
                throw new InvalidOperationException("Another fish visibility area remains installed.");
            _installAttempted = true;
            try
            {
                _harmony = new Harmony(Owner); _cleanupVerified = false;
                Patch(typeof(SABaseFishSystem), "Update_Imple", typeof(void), nameof(UpdateBefore), nameof(UpdateAfter));
                Patch(typeof(Renderer), "get_isVisible", typeof(bool), nameof(VisibleBefore), nameof(VisibleAfter));
                _accepting = true; Status = "Fish visibility area: installed; requires current actual host observation.";
            }
            catch (Exception error)
            {
                Fail("InstallationFailed", error); Dispose();
                throw new InvalidOperationException("Exact fish visibility declarations or own registrations unavailable.");
            }
        }
        private void Patch(Type type, string name, Type returns, string before, string after)
        {
            MethodInfo method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null, Type.EmptyTypes, null);
            if (method == null || method.IsStatic || method.IsGenericMethod || method.ReturnType != returns)
                throw new InvalidOperationException("Exact fish visibility declaration unavailable.");
            _targets.Add(method);
            _harmony.Patch(method, prefix: Hook(before, Priority.First), postfix: Hook(after, Priority.Last), finalizer: Hook(nameof(Finally), Priority.Last));
            if (Harmony.GetPatchInfo(method)?.Owners.Contains(Owner) != true) throw new InvalidOperationException("Own fish visibility registration unavailable.");
        }
        private static HarmonyMethod Hook(string name, int priority)
            => new HarmonyMethod(typeof(NativeHostFishVisibilityArea).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)) { priority = priority };

        private bool ThreadKnown() => _source.UnityThreadId != 0 && Environment.CurrentManagedThreadId == _source.UnityThreadId;
        private Scope Push()
        {
            // Global renderer reads on other/unknown threads preserve original
            // behavior before any pointer, native field or source read.
            if (!_accepting || Failed || !ThreadKnown()) return null;
            if (_scopes.Count >= MaxScopes) { Fail("ScopeQuota", null); return null; }
            var scope = new Scope { Parent = _scopes.Count == 0 ? null : _scopes.Peek() };
            _scopes.Push(scope); return scope;
        }
        private Scope BeginUpdate(SABaseFishSystem fish, bool originalWillRun)
        {
            Scope scope = Push();
            if (scope == null) return null;
            // Unknown/nested fish always masks the earlier actor. No native
            // read is made during this prefix or a source's own synchronous read.
            if (scope.Parent != null || _reading || _source.IsReading || !originalWillRun) return scope;
            scope.Update = true; scope.Fish = fish; scope.OriginalWillRun = true; return scope;
        }
        private Scope BeginVisible(Renderer renderer, bool originalWillRun)
        {
            if (!ThreadKnown() || _scopes.Count == 0) return null;
            Scope scope = Push();
            if (scope == null || _reading || _source.IsReading || !originalWillRun || !scope.Parent.Update ||
                !scope.Parent.OriginalWillRun || scope.Parent.Consumed) return scope;
            // Once-only pairing is consumed before source or native reads.
            scope.Parent.Consumed = true;
            // Opaque wrappers only. An original true result never captures an
            // interest window, reads a pointer/field or invokes a Unity API.
            scope.Record = new Record { Scope = scope, Fish = scope.Parent.Fish, Renderer = renderer };
            scope.Eligible = true;
            return scope;
        }

        private static readonly Func<IntPtr>[] FishClasses = {
            () => Il2CppClassPointerStore<SABaseFishSystem>.NativeClassPtr, () => Il2CppClassPointerStore<SABaseAI>.NativeClassPtr,
            () => Il2CppClassPointerStore<SAMahoniCommon>.NativeClassPtr, () => Il2CppClassPointerStore<SAMahoniGeneral>.NativeClassPtr,
            () => Il2CppClassPointerStore<SAXiphactinus>.NativeClassPtr, () => Il2CppClassPointerStore<SASnappingTurtle>.NativeClassPtr,
            () => Il2CppClassPointerStore<SAGroundCrawlerFish>.NativeClassPtr };
        private static readonly Func<IntPtr>[] RendererClasses = {
            () => Il2CppClassPointerStore<SpriteRenderer>.NativeClassPtr, () => Il2CppClassPointerStore<MeshRenderer>.NativeClassPtr,
            () => Il2CppClassPointerStore<SkinnedMeshRenderer>.NativeClassPtr };

        private bool Freeze(Record r)
        {
            if (ReferenceEquals(r.Fish, null) || ReferenceEquals(r.Renderer, null)) return false;
            r.FishPointer = Read(r, () => r.Fish.Pointer);
            r.RendererPointer = Read(r, () => r.Renderer.Pointer);
            if (r.FishPointer == IntPtr.Zero || r.RendererPointer == IntPtr.Zero) return false;
            r.FishClass = Read(r, () => IL2CPP.il2cpp_object_get_class(r.FishPointer));
            r.RendererClass = Read(r, () => IL2CPP.il2cpp_object_get_class(r.RendererPointer));
            if (!Known(r, r.FishClass, FishClasses) || !Known(r, r.RendererClass, RendererClasses)) return false;
            if (Pointer(Read(r, () => r.Fish._fishBodyRenderer), r) != r.RendererPointer) return false;
            r.FishUnity = Read(r, () => r.Fish.m_CachedPtr); r.RendererUnity = Read(r, () => r.Renderer.m_CachedPtr);
            r.FishId = Read(r, () => r.Fish.GetInstanceID()); r.RendererId = Read(r, () => r.Renderer.GetInstanceID());
            if (r.FishUnity == IntPtr.Zero || r.RendererUnity == IntPtr.Zero || r.FishId == 0 || r.RendererId == 0) return false;
            r.Root = Read(r, () => r.Fish.gameObject); r.RootTransform = Read(r, () => r.Fish.transform);
            r.RendererRoot = Read(r, () => r.Renderer.gameObject); r.RendererTransform = Read(r, () => r.Renderer.transform);
            r.RootPointer = Pointer(r.Root, r); r.RootTransformPointer = Pointer(r.RootTransform, r);
            r.RendererRootPointer = Pointer(r.RendererRoot, r); r.RendererTransformPointer = Pointer(r.RendererTransform, r);
            if (r.RootPointer == IntPtr.Zero || r.RootTransformPointer == IntPtr.Zero || r.RendererRootPointer == IntPtr.Zero || r.RendererTransformPointer == IntPtr.Zero) return false;
            r.RootUnity = Read(r, () => r.Root.m_CachedPtr); r.RootTransformUnity = Read(r, () => r.RootTransform.m_CachedPtr);
            r.RendererRootUnity = Read(r, () => r.RendererRoot.m_CachedPtr); r.RendererTransformUnity = Read(r, () => r.RendererTransform.m_CachedPtr);
            r.RendererLayer = Read(r, () => r.RendererRoot.layer);
            return r.RootUnity != IntPtr.Zero && r.RootTransformUnity != IntPtr.Zero && r.RendererRootUnity != IntPtr.Zero &&
                r.RendererTransformUnity != IntPtr.Zero && r.RendererLayer >= 0 && r.RendererLayer <= 31 && Contained(r);
        }
        private bool Known(Record r, IntPtr native, Func<IntPtr>[] classes)
        {
            if (native == IntPtr.Zero) return false;
            foreach (Func<IntPtr> candidate in classes) if (Read(r, candidate) == native) return true;
            return false;
        }
        private IntPtr Pointer(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase wrapper, Record r)
            => ReferenceEquals(wrapper, null) ? IntPtr.Zero : Read(r, () => wrapper.Pointer);

        private bool Contained(Record r)
        {
            Transform node = r.RendererTransform;
            for (int depth = 0; depth < MaxParentDepth && !ReferenceEquals(node, null); depth++)
            {
                IntPtr pointer = Pointer(node, r);
                GameObject root = Read(r, () => node.gameObject);
                if (ReferenceEquals(root, null)) return false;
                var actors = Read(r, () => root.GetComponents<FishAISystem>());
                if (ReferenceEquals(actors, null)) return false;
                int count = Read(r, () => actors.Length);
                if (count < 0 || count > 1) return false;
                if (count == 1 && Pointer(Read(r, () => actors[0]), r) != r.FishPointer) return false;
                if (pointer == r.RootTransformPointer) return count == 1 && Read(r, () => node.m_CachedPtr) == r.RootTransformUnity;
                node = Read(r, () => node.parent);
            }
            return false;
        }
        private bool Scene(Record r, GameObject root, bool sourceScene)
        {
            var scene = Read(r, () => root.scene);
            return Read(r, () => scene.IsValid()) && Read(r, () => scene.isLoaded) &&
                (!sourceScene || (Read(r, () => scene.handle) == r.Window.SceneHandle && Read(r, () => scene.name) == r.Window.Interest.SceneKey));
        }
        private bool Fresh(Record r)
        {
            return SourceFresh(r) && Pointer(r.Fish, r) == r.FishPointer && Pointer(r.Renderer, r) == r.RendererPointer &&
                Read(r, () => IL2CPP.il2cpp_object_get_class(r.FishPointer)) == r.FishClass &&
                Read(r, () => IL2CPP.il2cpp_object_get_class(r.RendererPointer)) == r.RendererClass &&
                Read(r, () => r.Fish.m_CachedPtr) == r.FishUnity && Read(r, () => r.Renderer.m_CachedPtr) == r.RendererUnity &&
                Read(r, () => r.Fish.GetInstanceID()) == r.FishId && Read(r, () => r.Renderer.GetInstanceID()) == r.RendererId &&
                Pointer(Read(r, () => r.Fish._fishBodyRenderer), r) == r.RendererPointer &&
                Pointer(Read(r, () => r.Fish.gameObject), r) == r.RootPointer && Pointer(Read(r, () => r.Fish.transform), r) == r.RootTransformPointer &&
                Pointer(Read(r, () => r.Renderer.gameObject), r) == r.RendererRootPointer && Pointer(Read(r, () => r.Renderer.transform), r) == r.RendererTransformPointer &&
                Read(r, () => r.Root.m_CachedPtr) == r.RootUnity && Read(r, () => r.RootTransform.m_CachedPtr) == r.RootTransformUnity &&
                Read(r, () => r.RendererRoot.m_CachedPtr) == r.RendererRootUnity && Read(r, () => r.RendererTransform.m_CachedPtr) == r.RendererTransformUnity &&
                Read(r, () => r.Fish.isActiveAndEnabled) && Read(r, () => r.Root.activeInHierarchy) &&
                Read(r, () => r.Renderer.enabled) && Read(r, () => r.RendererRoot.activeInHierarchy) &&
                Read(r, () => r.RendererRoot.layer) == r.RendererLayer && Scene(r, r.Root, true) && Scene(r, r.RendererRoot, true) && Contained(r) && SourceFresh(r);
        }

        private CameraSample FreezeCamera(Record r)
        {
            if (!SourceFresh(r)) throw new Expired();
            Camera camera = Read(r, () => Camera.main);
            if (ReferenceEquals(camera, null)) return null;
            var sample = new CameraSample { Camera = camera, Pointer = Pointer(camera, r), Unity = Read(r, () => camera.m_CachedPtr) };
            if (sample.Pointer == IntPtr.Zero || sample.Unity == IntPtr.Zero ||
                Read(r, () => IL2CPP.il2cpp_object_get_class(sample.Pointer)) != Read(r, () => Il2CppClassPointerStore<Camera>.NativeClassPtr) ||
                !Read(r, () => camera.isActiveAndEnabled) || !Read(r, () => camera.orthographic)) return null;
            sample.Id = Read(r, () => camera.GetInstanceID()); sample.Root = Read(r, () => camera.gameObject);
            sample.RootPointer = Pointer(sample.Root, r);
            if (sample.Id == 0 || sample.RootPointer == IntPtr.Zero) return null;
            sample.RootUnity = Read(r, () => sample.Root.m_CachedPtr); sample.SceneHandle = Read(r, () => sample.Root.scene.handle);
            // The main camera can live in the bootstrap/persistent scene; its
            // actual loaded scene is frozen, not asserted to equal the fish layer.
            if (sample.RootUnity == IntPtr.Zero || sample.SceneHandle == 0 || !Scene(r, sample.Root, false)) return null;
            sample.Near = Read(r, () => camera.nearClipPlane); sample.Far = Read(r, () => camera.farClipPlane);
            sample.Mask = Read(r, () => camera.cullingMask);
            if ((sample.Mask & (1 << r.RendererLayer)) == 0) return null;
            sample.View = Read(r, () => camera.worldToCameraMatrix); sample.Projection = Read(r, () => camera.projectionMatrix);
            // The boolean alone does not rule out another mod's custom
            // perspective projection. Preserve originals for nonfinite or
            // non-affine matrices before relying on corner containment.
            if (!FiniteAffine(sample.View) || !FiniteAffine(sample.Projection)) return null;
            sample.Bounds = Read(r, () => r.Renderer.bounds);
            if (!SourceFresh(r)) throw new Expired();
            return sample;
        }
        private bool CameraFresh(Record r, CameraSample sample)
        {
            return SourceFresh(r) && Pointer(Read(r, () => Camera.main), r) == sample.Pointer && Pointer(sample.Camera, r) == sample.Pointer &&
                Read(r, () => sample.Camera.m_CachedPtr) == sample.Unity && Read(r, () => sample.Camera.GetInstanceID()) == sample.Id &&
                Read(r, () => IL2CPP.il2cpp_object_get_class(sample.Pointer)) == Read(r, () => Il2CppClassPointerStore<Camera>.NativeClassPtr) &&
                Read(r, () => sample.Camera.isActiveAndEnabled) && Read(r, () => sample.Camera.orthographic) &&
                Pointer(Read(r, () => sample.Camera.gameObject), r) == sample.RootPointer && Read(r, () => sample.Root.m_CachedPtr) == sample.RootUnity &&
                Read(r, () => sample.Root.scene.handle) == sample.SceneHandle && Scene(r, sample.Root, false) &&
                Read(r, () => sample.Camera.nearClipPlane) == sample.Near && Read(r, () => sample.Camera.farClipPlane) == sample.Far &&
                Read(r, () => sample.Camera.cullingMask) == sample.Mask && SameMatrix(Read(r, () => sample.Camera.worldToCameraMatrix), sample.View) &&
                SameMatrix(Read(r, () => sample.Camera.projectionMatrix), sample.Projection) && SameBounds(Read(r, () => r.Renderer.bounds), sample.Bounds) && SourceFresh(r);
        }
        private void EndVisible(Scope scope, bool originalRan, ref bool result)
        {
            if (!After(scope) || !scope.Eligible || !originalRan || result) return;
            Record r = scope.Record;
            try
            {
                HostFishInterestWindow window;
                _reading = true;
                try { if (!_source.TryCapture(out window)) { Interlocked.Increment(ref _missingInterest); return; } }
                finally { _reading = false; }
                r.Window = window;
                if (window == null || !SourceFresh(r)) { Interlocked.Increment(ref _missingInterest); return; }
                if (!Freeze(r)) { Interlocked.Increment(ref _unsupported); return; }
                if (!Fresh(r)) { Interlocked.Increment(ref _unsupported); return; }
                CameraSample camera = FreezeCamera(r);
                var interest = r.Window.Interest;
                if (camera == null || !FishVisibilityInterestMath.TryCreateShiftedCorners(ToNumerics(camera.Bounds.m_Center),
                    ToNumerics(camera.Bounds.m_Extents), ToNumerics(r.Window.LocalPosition), interest.Position, out NumVector[] corners))
                { Interlocked.Increment(ref _unsupported); return; }
                var projected = new NumVector[FishVisibilityInterestMath.CornerCount];
                if (!SourceFresh(r)) throw new Expired();
                for (int i = 0; i < projected.Length; i++)
                {
                    NumVector point = corners[i];
                    projected[i] = ToNumerics(Read(r, () => camera.Camera.WorldToViewportPoint(new Vector3(point.X, point.Y, point.Z))));
                }
                if (!SourceFresh(r)) throw new Expired();
                if (!FishVisibilityInterestMath.FullyInsideViewport(projected, camera.Near, camera.Far))
                { Interlocked.Increment(ref _unsupported); return; }
                if (!Fresh(r) || !CameraFresh(r, camera) || !SourceFresh(r)) { Interlocked.Increment(ref _unsupported); return; }
                // Sole write: a transient original getter result, preserving the
                // following original dead/captured checks and task submission.
                result = true; Interlocked.Increment(ref _proxyReturns);
                Status = "Fish visibility area: original avoidance branch eligible in current employee camera region.";
            }
            catch (Expired) { scope.Eligible = false; Interlocked.Increment(ref _missingInterest); }
            catch (Exception error) { Fail("VisibilityResultReadFailed", error); }
        }

        private bool Guard(Record r)
        {
            if (!Healthy || !ThreadKnown() || _reading || _source.IsReading || _scopes.Count == 0 || _scopes.Peek() != r.Scope ||
                r.Scope.Parent == null || !r.Scope.Parent.Update || !ReferenceEquals(r.Scope.Parent.Fish, r.Fish)) return false;
            return true;
        }
        private bool SourceFresh(Record r)
        {
            if (!Guard(r) || r.Window == null) return false;
            _reading = true;
            bool fresh;
            try { fresh = _source.IsCurrent(r.Window); }
            finally { _reading = false; }
            return fresh && Guard(r);
        }
        private T Read<T>(Record r, Func<T> action)
        {
            // Every primitive has pure CLR stopped/thread/scope/reentry fences.
            // Full source-native checks surround read groups and the final write.
            // Budget excludes internal work in full SourceFresh/GetComponents
            // and Unity composite APIs; runtime cost has not been measured.
            if (!Guard(r)) throw new Expired();
            if (++r.Reads > MaxReadStepsPerGetter) throw new InvalidOperationException("Fish visibility read-step quota.");
            T value; _reading = true;
            try { value = action(); }
            finally { _reading = false; }
            if (!Guard(r)) throw new Expired(); return value;
        }
        private bool After(Scope scope)
        {
            if (scope == null || !ThreadKnown()) return false;
            if (_scopes.Count == 0 || _scopes.Peek() != scope || scope.After) { Fail("PostfixScopeMismatch", null); return false; }
            scope.After = true; return Healthy;
        }
        private void End(Scope scope, Exception exception)
        {
            if (scope == null) return;
            // Pure CLR finalizer: no source/native reads and no exception/result
            // substitution. A stopped adapter can still pop its retained scopes.
            if (!ThreadKnown() || _scopes.Count == 0 || _scopes.Peek() != scope) { Fail("FinalizerScopeMismatch", null); return; }
            if (exception != null && scope.Parent != null) scope.Parent.Consumed = true;
            _scopes.Pop();
        }
        private static NumVector ToNumerics(Vector3 v) => new NumVector(v.x, v.y, v.z);
        private static bool Same(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
        private static bool SameBounds(Bounds a, Bounds b) => Same(a.m_Center, b.m_Center) && Same(a.m_Extents, b.m_Extents);
        private static bool FiniteAffine(Matrix4x4 m) => m.m30 == 0 && m.m31 == 0 && m.m32 == 0 && m.m33 != 0 &&
            float.IsFinite(m.m00) && float.IsFinite(m.m01) && float.IsFinite(m.m02) && float.IsFinite(m.m03) &&
            float.IsFinite(m.m10) && float.IsFinite(m.m11) && float.IsFinite(m.m12) && float.IsFinite(m.m13) &&
            float.IsFinite(m.m20) && float.IsFinite(m.m21) && float.IsFinite(m.m22) && float.IsFinite(m.m23) && float.IsFinite(m.m33);
        private static bool SameMatrix(Matrix4x4 a, Matrix4x4 b) =>
            a.m00 == b.m00 && a.m01 == b.m01 && a.m02 == b.m02 && a.m03 == b.m03 &&
            a.m10 == b.m10 && a.m11 == b.m11 && a.m12 == b.m12 && a.m13 == b.m13 &&
            a.m20 == b.m20 && a.m21 == b.m21 && a.m22 == b.m22 && a.m23 == b.m23 &&
            a.m30 == b.m30 && a.m31 == b.m31 && a.m32 == b.m32 && a.m33 == b.m33;

        private void Fail(string reason, Exception error)
        {
            Interlocked.Exchange(ref _processFailed, 1); _accepting = false; Interlocked.Increment(ref _errors);
            Status = "Fish visibility area failed: " + reason + "; original behavior retained.";
            if (_logs++ < MaxLogs)
                try { _logger.LogWarning("DAVECOOP_HOST_FISH_VISIBILITY_FAILED: " + reason + (error == null ? "" : "; " + error.GetType().Name)); }
                catch { }
        }
        public void CheckHealthy()
        {
            if (_source.Failed && Volatile.Read(ref _processFailed) == 0) Fail("InterestSourceFailed", null);
            if (!Healthy) throw new InvalidOperationException("Host fish visibility adapter unavailable.");
        }
        public void Dispose()
        {
            _accepting = false;
            if (_harmony == null) return;
            if (_scopes.Count != 0) { Status = "Fish visibility area stopped with synchronous scopes retained; own cleanup deferred."; return; }
            if (Interlocked.CompareExchange(ref _cleanupAttempted, 1, 0) != 0) return;
            try
            {
                _harmony.UnpatchSelf();
                foreach (MethodInfo target in _targets)
                    if (Harmony.GetPatchInfo(target)?.Owners.Contains(Owner) == true) throw new InvalidOperationException("Own fish visibility hook remains.");
                _cleanupVerified = true; _harmony = null; _targets.Clear(); Interlocked.CompareExchange(ref _active, null, this);
                Status = "Fish visibility area stopped; own registrations removed.";
            }
            catch (Exception error) { _cleanupVerified = false; Fail("OwnCleanupUnknown", error); }
        }
        private static void UpdateBefore(SABaseFishSystem __instance, bool __runOriginal, out Scope __state)
        {
            __state = null; var active = _active;
            try { __state = active?.BeginUpdate(__instance, __runOriginal); }
            catch (Exception error) { active?.Fail("UpdatePrefixFailed", error); }
        }
        private static void VisibleBefore(Renderer __instance, bool __runOriginal, out Scope __state)
        {
            __state = null; var active = _active;
            try { __state = active?.BeginVisible(__instance, __runOriginal); }
            catch (Exception error) { active?.Fail("GetterPrefixFailed", error); }
        }
        private static void UpdateAfter(Scope __state)
        {
            var active = _active; try { active?.After(__state); } catch (Exception error) { active?.Fail("UpdatePostfixFailed", error); }
        }
        private static void VisibleAfter(bool __runOriginal, Scope __state, ref bool __result)
        {
            var active = _active; try { active?.EndVisible(__state, __runOriginal, ref __result); } catch (Exception error) { active?.Fail("GetterPostfixFailed", error); }
        }
        private static void Finally(Exception __exception, Scope __state)
        {
            var active = _active; try { active?.End(__state, __exception); } catch (Exception error) { active?.Fail("FinalizerFailed", error); }
        }
    }
}
