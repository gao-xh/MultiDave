using System;
using System.Collections.Generic;
using BepInEx.Logging;
using DaveCoop.Core.Crew;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;
using UnityEngine.SceneManagement;
using UObj = UnityEngine.Object;

namespace DaveCoop.Networking
{
    // Display only: no collider, weapon handler, input, damage or inventory.
    internal sealed class RemoteEmployeeHarpoonVisual : IDisposable
    {
        private static readonly List<RemoteEmployeeHarpoonVisual> Retained = new List<RemoteEmployeeHarpoonVisual>();
        private static int _attempts;
        private readonly int _thread;
        private readonly ManualLogSource _logger;
        private readonly List<(Il2CppObjectBase Value, IntPtr Handle)> _references = new List<(Il2CppObjectBase, IntPtr)>();
        private GameObject _root;
        private LineRenderer _line;
        private Material _material;
        private Shader _shader;
        private IntPtr _rootPointer, _rootUnity, _linePointer, _lineUnity;
        private Func<bool> _current;
        private long _epoch, _actor;
        private int _scene, _steps;
        private bool _entered, _busy, _failed, _stopEntered, _rootConstructionEntered;
        public bool Failed => _failed;
        public bool RetirementEntered => _stopEntered;
        public string Status { get; private set; } = "Empty";
        public long SceneEpoch => _epoch;
        public long ActorRevision => _actor;
        public RemoteEmployeeHarpoonVisual(int thread, ManualLogSource logger) { _thread = thread; _logger = logger; }

        public bool Show(CrewActorState frame, Scene scene, Func<bool> current)
        {
            if (_failed || _stopEntered || _busy || Environment.CurrentManagedThreadId != _thread || frame == null || current == null) return false;
            CrewFrames.Validate(frame);
            if (!frame.HarpoonActive) { Hide(); return true; }
            _busy = true; _steps = 0; _current = current;
            try
            {
                if (!_entered)
                {
                    _entered = true;
                    if (++_attempts > 64) throw new InvalidOperationException("Visual creation budget exhausted.");
                    Retained.Add(this); _epoch = frame.SceneEpoch; _actor = frame.ActorRevision;
                    _scene = Read(() => scene.handle);
                    if (_scene == 0 || !Read(() => scene.IsValid()) || !Read(() => scene.isLoaded)) throw Expired();
                    _rootConstructionEntered = true;
                    _root = Read(() => { _root = new GameObject("MultiDave Employee Harpoon Display"); Hold(_root); return _root; });
                    _rootPointer = Read(() => _root.Pointer); _rootUnity = Read(() => _root.m_CachedPtr);
                    Write(() => _root.SetActive(false)); Write(() => SceneManager.MoveGameObjectToScene(_root, scene));
                    _line = Read(() => { _line = _root.AddComponent<LineRenderer>(); Hold(_line); return _line; });
                    _linePointer = Read(() => _line.Pointer); _lineUnity = Read(() => _line.m_CachedPtr);
                    _shader = Read(() => Shader.Find("Sprites/Default"));
                    if (ReferenceEquals(_shader, null) || Read(() => _shader.m_CachedPtr) == IntPtr.Zero) throw Expired();
                    Hold(_shader);
                    _material = Read(() => { _material = new Material(_shader); Hold(_material); return _material; });
                    Write(() => _line.sharedMaterial = _material); Write(() => _line.positionCount = 2);
                    Write(() => _line.useWorldSpace = true); Write(() => _line.startWidth = 0.06f); Write(() => _line.endWidth = 0.02f);
                    Color tint = new Color { r = 0.25f, g = 0.9f, b = 1f, a = 1f };
                    Write(() => _line.startColor = tint); Write(() => _line.endColor = tint);
                    Write(() => _line.sortingOrder = 100);
                }
                if (frame.SceneEpoch != _epoch || frame.ActorRevision != _actor || Read(() => scene.handle) != _scene) throw Expired();
                Owned();
                Vector3 p = new Vector3 { x = frame.HarpoonPosition.X, y = frame.HarpoonPosition.Y, z = frame.HarpoonPosition.Z };
                Vector3 tail = new Vector3 { x = p.x - frame.HarpoonDirection.X * 0.35f, y = p.y - frame.HarpoonDirection.Y * 0.35f, z = p.z };
                Write(() => _line.SetPosition(0, tail)); Write(() => _line.SetPosition(1, p));
                Write(() => _line.enabled = true); Write(() => _root.SetActive(true)); Owned();
                Status = "HostProjectileDisplayed"; return true;
            }
            catch (Exception error)
            {
                _failed = true; Status = "DisplayUnknown";
                try { _logger?.LogWarning("DAVECOOP_CREW_HARPOON_DISPLAY_UNAVAILABLE: " + error.GetType().Name); } catch { }
                return false;
            }
            finally { _current = null; _busy = false; }
        }

        public void Hide()
        {
            if (_failed || _busy || _stopEntered || Environment.CurrentManagedThreadId != _thread || ReferenceEquals(_root, null)) return;
            try
            {
                if (_root.Pointer != _rootPointer || _root.m_CachedPtr != _rootUnity || _rootUnity == IntPtr.Zero) throw Expired();
                _root.SetActive(false); Status = "Hidden";
            }
            catch { _failed = true; Status = "HideUnknown"; }
        }
        public void Dispose()
        {
            if (_busy || _stopEntered || Environment.CurrentManagedThreadId != _thread) return;
            _stopEntered = true;
            try
            {
                if (ReferenceEquals(_root, null) && _rootConstructionEntered) throw Expired();
                if (!ReferenceEquals(_root, null))
                {
                    if (_root.Pointer != _rootPointer || _root.m_CachedPtr != _rootUnity || _rootUnity == IntPtr.Zero) throw Expired();
                    _root.SetActive(false); UObj.Destroy(_root);
                }
                if (!ReferenceEquals(_material, null) && _material.m_CachedPtr != IntPtr.Zero) UObj.Destroy(_material);
                Status = "DestroyRequested"; // Unity destroy is deferred; retain all handles.
            }
            catch { _failed = true; Status = "CleanupUnknown"; }
        }
        private void Owned()
        {
            if (ReferenceEquals(_root, null) || ReferenceEquals(_line, null) || _rootPointer == IntPtr.Zero || _rootUnity == IntPtr.Zero ||
                _linePointer == IntPtr.Zero || _lineUnity == IntPtr.Zero || Read(() => _root.Pointer) != _rootPointer ||
                Read(() => _root.m_CachedPtr) != _rootUnity || Read(() => _line.Pointer) != _linePointer || Read(() => _line.m_CachedPtr) != _lineUnity ||
                Read(() => _root.scene.handle) != _scene) throw Expired();
            GameObject lineRoot = Read(() => _line.gameObject);
            if (ReferenceEquals(lineRoot, null) || Read(() => lineRoot.Pointer) != _rootPointer || Read(() => lineRoot.m_CachedPtr) != _rootUnity) throw Expired();
        }
        private void Hold(Il2CppObjectBase value)
        {
            if (ReferenceEquals(value, null) || _references.Count >= 4) throw Expired();
            _references.Add((value, IntPtr.Zero));
            IntPtr pointer = value.Pointer;
            if (pointer == IntPtr.Zero) throw Expired();
            Check(); IntPtr handle = IL2CPP.il2cpp_gchandle_new(pointer, false);
            _references[_references.Count - 1] = (value, handle); Check();
            if (handle == IntPtr.Zero) throw Expired();
        }
        private T Read<T>(Func<T> read) { Check(); T value = read(); Check(); return value; }
        private void Write(Action write) { Check(); write(); Check(); }
        private void Check()
        {
            if (++_steps > 4096 || Environment.CurrentManagedThreadId != _thread || _current == null || !_current()) throw Expired();
        }
        private static InvalidOperationException Expired() => new InvalidOperationException("Harpoon display source expired.");
    }
}
