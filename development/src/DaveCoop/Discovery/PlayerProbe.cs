using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using UnityEngine.SceneManagement;
using GameObjectFinder = UnityEngine.Object;

namespace DaveCoop
{
    // This component only reads existing objects. Every Unity API call is made in Update.
    public sealed class PlayerProbe : MonoBehaviour
    {
        internal static ManualLogSource Logger;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> MaxSnapshots;
        internal static string Status = "Player discovery: starting";

        private PlayerCharacter[] _players = Array.Empty<PlayerCharacter>();
        private InGameManager[] _managers = Array.Empty<InGameManager>();
        private CameraManager[] _cameras = Array.Empty<CameraManager>();
        private PerspectiveCameraManager[] _perspectiveCameras = Array.Empty<PerspectiveCameraManager>();
        private OrthographicCameraManager[] _orthographicCameras = Array.Empty<OrthographicCameraManager>();
        private readonly HashSet<string> _warned = new HashSet<string>();
        private readonly List<string> _scanErrors = new List<string>();
        private StreamWriter _writer;
        private bool _started;
        private bool _fileLoggingStopped;
        private float _nextScan;
        private float _nextSample;
        private float _nextSummary;
        private int _sampleCount;
        private int _writtenSnapshots;
        private string _lastTopology;

        public PlayerProbe(IntPtr pointer) : base(pointer) { }

        public void Update()
        {
            try { Tick(); }
            catch (Exception error)
            {
                Status = "Player discovery: error (see log)";
                Warn("update", error, _scanErrors);
            }
        }

        [HideFromIl2Cpp]
        private void Tick()
        {
            if (Enabled == null || !Enabled.Value)
            {
                Status = "Player discovery: disabled";
                CloseLog();
                _started = false;
                return;
            }

            if (!_started)
            {
                _started = true;
                StartLog();
                Logger.LogInfo("DAVECOOP_PROBE_READY: read-only player/camera discovery; F9 captures a snapshot.");
            }

            bool requested = Input.GetKeyDown(KeyCode.F9);
            float now = Time.unscaledTime;
            if (requested || now >= _nextScan)
            {
                _nextScan = now + 2f;
                Discover();
            }
            if (!requested && now < _nextSample)
                return;
            _nextSample = now + 0.5f;

            ProbeSnapshot snapshot = Capture();
            string topology = Topology(snapshot);
            if (topology != _lastTopology)
            {
                _lastTopology = topology;
                Logger.LogInfo($"DAVECOOP_OBJECTS: {topology}");
            }
            int boundPlayers = 0;
            foreach (PlayerObservation player in snapshot.Players)
                if (player.IsManagerPlayer) boundPlayers++;
            Status = $"Players: {snapshot.Players.Count} | Manager-bound: {boundPlayers} | Cameras: {snapshot.Cameras.Count}";
            if (requested || (snapshot.Players.Count > 0 && now >= _nextSummary))
            {
                _nextSummary = now + 5f;
                Logger.LogInfo("DAVECOOP_PLAYER_SNAPSHOT: " + JsonSerializer.Serialize(snapshot));
            }
            WriteSnapshot(snapshot);
        }

        [HideFromIl2Cpp]
        private void Discover()
        {
            _scanErrors.Clear();
            _players = Find<PlayerCharacter>();
            _managers = Find<InGameManager>();
            _cameras = Find<CameraManager>();
            _perspectiveCameras = Find<PerspectiveCameraManager>();
            _orthographicCameras = Find<OrthographicCameraManager>();
        }

        [HideFromIl2Cpp]
        private T[] Find<T>() where T : UnityEngine.Object
        {
            try
            {
                var found = GameObjectFinder.FindObjectsOfType<T>();
                var result = new T[found.Length];
                for (int i = 0; i < result.Length; i++) result[i] = found[i];
                return result;
            }
            catch (Exception error)
            {
                Warn("scan." + typeof(T).Name, error, _scanErrors);
                return Array.Empty<T>();
            }
        }

        [HideFromIl2Cpp]
        private ProbeSnapshot Capture()
        {
            var snapshot = new ProbeSnapshot
            {
                Utc = DateTime.UtcNow.ToString("o"),
                Sequence = ++_sampleCount,
                Errors = new List<string>(_scanErrors)
            };
            snapshot.ActiveScene = Read("scene.active", () => Describe(SceneManager.GetActiveScene()), snapshot.Errors);
            var managerPlayers = new HashSet<int>();
            foreach (InGameManager manager in _managers)
            {
                if (manager == null) continue;
                ManagerObservation observation = Read("manager.identity", () => new ManagerObservation
                {
                    Object = Describe(manager)
                }, snapshot.Errors);
                if (observation == null) continue;
                observation.PlayerId = Read<int?>("manager.player", () => Id(manager.playerCharacter), snapshot.Errors);
                snapshot.Managers.Add(observation);
                if (observation.PlayerId.HasValue) managerPlayers.Add(observation.PlayerId.Value);
            }
            foreach (PlayerCharacter player in _players)
            {
                if (player == null) continue;
                ObjectObservation identity = Read("player.identity", () => Describe(player), snapshot.Errors);
                if (identity == null) continue;
                var observation = new PlayerObservation
                {
                    Object = identity,
                    IsManagerPlayer = managerPlayers.Contains(identity.Id)
                };
                observation.Position = Read("player.position", () => Vec(player.transform.position), snapshot.Errors);
                observation.Rotation = Read("player.rotation", () => Quat(player.transform.rotation), snapshot.Errors);
                observation.Scale = Read("player.scale", () => Vec(player.transform.localScale), snapshot.Errors);
                observation.Look = Read("player.look", () => Vec(player.LookDirection), snapshot.Errors);
                observation.MoveInput = Read("player.input", () => Vec(player.moveInput), snapshot.Errors);
                observation.MoveLocked = Read<bool?>("player.moveLocked", () => player.IsMoveLocked, snapshot.Errors);
                observation.CustomMoveInput = Read<bool?>("player.customInput", () => player.customMoveInput, snapshot.Errors);
                observation.Animator = Read("player.animator", () => DescribeAnimator(player.characterAnimator), snapshot.Errors);
                snapshot.Players.Add(observation);
            }
            snapshot.MainCamera = Read("camera.main", () => Describe(Camera.main), snapshot.Errors);
            foreach (CameraManager camera in _cameras)
            {
                if (camera == null) continue;
                CameraObservation observation = Read("camera.manager", () => new CameraObservation
                {
                    Kind = "CameraManager",
                    Object = Describe(camera),
                    Target = Describe(camera.PrimaryTargetTransform),
                    SecondTarget = Describe(camera.SecondTargetTransform),
                    Camera = Describe(camera.mainCamera)
                }, snapshot.Errors);
                AddCamera(observation, snapshot);
            }
            foreach (PerspectiveCameraManager camera in _perspectiveCameras)
            {
                if (camera == null) continue;
                CameraObservation observation = Read("camera.perspective", () => new CameraObservation
                {
                    Kind = "PerspectiveCameraManager",
                    Object = Describe(camera),
                    Target = Describe(camera.m_Target),
                    Camera = Describe(camera.m_MainCamera)
                }, snapshot.Errors);
                AddCamera(observation, snapshot);
            }
            foreach (OrthographicCameraManager camera in _orthographicCameras)
            {
                if (camera == null) continue;
                CameraObservation observation = Read("camera.orthographic", () => new CameraObservation
                {
                    Kind = "OrthographicCameraManager",
                    Object = Describe(camera),
                    Target = Describe(camera.Target),
                    Camera = Describe(camera.m_Camera)
                }, snapshot.Errors);
                AddCamera(observation, snapshot);
            }
            return snapshot;
        }

        private static void AddCamera(CameraObservation camera, ProbeSnapshot snapshot)
        {
            if (camera == null) return;
            // Follow targets may be a child of the player's transform. Preserve the path
            // and owning GameObject as evidence rather than assuming transform equality.
            if (camera.Target != null)
            {
                foreach (PlayerObservation player in snapshot.Players)
                    if (camera.Target.GameObjectId == player.Object.GameObjectId ||
                        Array.IndexOf(camera.Target.AncestorGameObjectIds, player.Object.GameObjectId) >= 0)
                        camera.TargetPlayerId = player.Object.Id;
            }
            snapshot.Cameras.Add(camera);
        }

        [HideFromIl2Cpp]
        private T Read<T>(string key, Func<T> reader, List<string> errors)
        {
            try { return reader(); }
            catch (Exception error)
            {
                Warn(key, error, errors);
                return default(T);
            }
        }

        [HideFromIl2Cpp]
        private void Warn(string key, Exception error, List<string> errors)
        {
            errors.Add(key);
            if (_warned.Add(key))
                Logger.LogWarning($"DAVECOOP_PROBE_WARNING: {key}: {error.GetType().Name}: {error.Message}");
        }

        private static SceneObservation Describe(Scene scene) => new SceneObservation
        {
            Name = scene.name,
            Handle = scene.handle
        };

        private static ObjectObservation Describe(Component component)
        {
            if (component == null) return null;
            GameObject obj = component.gameObject;
            var behaviour = component.TryCast<Behaviour>();
            return new ObjectObservation
            {
                Id = component.GetInstanceID(),
                GameObjectId = obj.GetInstanceID(),
                Name = obj.name,
                Path = HierarchyPath(component.transform),
                AncestorGameObjectIds = AncestorIds(component.transform),
                Scene = Describe(obj.scene),
                Active = obj.activeInHierarchy,
                Enabled = behaviour == null ? (bool?)null : behaviour.enabled
            };
        }

        private static string HierarchyPath(Transform transform)
        {
            var names = new List<string>();
            for (int depth = 0; transform != null && depth < 16; depth++, transform = transform.parent)
                names.Add(transform.name);
            names.Reverse();
            return string.Join("/", names);
        }

        private static AnimatorObservation DescribeAnimator(Animator animator)
        {
            if (animator == null) return null;
            var result = new AnimatorObservation { Id = animator.GetInstanceID(), LayerCount = animator.layerCount };
            for (int layer = 0; layer < result.LayerCount; layer++)
            {
                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(layer);
                if (!float.IsFinite(state.normalizedTime))
                    throw new InvalidDataException("Non-finite animation time.");
                result.Layers.Add(new AnimatorLayerObservation
                {
                    Layer = layer,
                    StateHash = state.fullPathHash,
                    NormalizedTime = state.normalizedTime
                });
            }
            return result;
        }

        private static int? Id(UnityEngine.Object obj) => obj == null ? (int?)null : obj.GetInstanceID();
        private static int[] AncestorIds(Transform transform)
        {
            var ids = new List<int>();
            transform = transform.parent;
            for (int depth = 0; transform != null && depth < 16; depth++, transform = transform.parent)
                ids.Add(transform.gameObject.GetInstanceID());
            return ids.ToArray();
        }

        private static float[] Finite(params float[] values)
        {
            foreach (float value in values)
                if (!float.IsFinite(value)) throw new InvalidDataException("Non-finite transform or input value.");
            return values;
        }

        private static float[] Vec(Vector2 value) => Finite(value.x, value.y);
        private static float[] Vec(Vector3 value) => Finite(value.x, value.y, value.z);
        private static float[] Quat(Quaternion value) => Finite(value.x, value.y, value.z, value.w);

        private static string Topology(ProbeSnapshot snapshot)
        {
            var parts = new List<string> { "scene=" + snapshot.ActiveScene?.Name + "#" + snapshot.ActiveScene?.Handle };
            foreach (ManagerObservation manager in snapshot.Managers)
                parts.Add($"manager={manager.Object.Id}->player={manager.PlayerId}");
            foreach (PlayerObservation player in snapshot.Players)
                parts.Add($"player={player.Object.Id}@{player.Object.Scene.Name},bound={player.IsManagerPlayer}");
            foreach (CameraObservation camera in snapshot.Cameras)
                parts.Add($"{camera.Kind}={camera.Object.Id}->target={camera.Target?.Id}");
            if (snapshot.Errors.Count > 0) parts.Add("errors=" + string.Join(",", snapshot.Errors));
            return string.Join("; ", parts);
        }

        [HideFromIl2Cpp]
        private void StartLog()
        {
            _fileLoggingStopped = false;
            _writtenSnapshots = 0;
            try
            {
                string directory = Path.Combine(Paths.PluginPath, "DaveCoop", "logs");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "discovery-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".jsonl");
                _writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
                _writer.AutoFlush = true;
                _writer.WriteLine(JsonSerializer.Serialize(new
                {
                    Kind = "session", SchemaVersion = 1, PluginVersion = Plugin.Version,
                    UnityVersion = Application.unityVersion, StartedUtc = DateTime.UtcNow.ToString("o"),
                    ReadOnly = true, SampleIntervalSeconds = 0.5, ScanIntervalSeconds = 2,
                    SnapshotLimit = Math.Clamp(MaxSnapshots.Value, 1, 18000)
                }));
                Logger.LogInfo("DAVECOOP_DISCOVERY_LOG: " + path);
            }
            catch (Exception error)
            {
                CloseLog();
                Logger.LogWarning("DAVECOOP_DISCOVERY_LOG_DISABLED: " + error.Message);
            }
        }

        [HideFromIl2Cpp]
        private void WriteSnapshot(ProbeSnapshot snapshot)
        {
            if (_fileLoggingStopped) return;
            if (_writer == null) return;
            try
            {
                _writer.WriteLine(JsonSerializer.Serialize(snapshot));
                _writtenSnapshots++;
                if (_writtenSnapshots >= Math.Clamp(MaxSnapshots.Value, 1, 18000))
                {
                    CloseLog();
                    Logger.LogInfo("DAVECOOP_DISCOVERY_LOG_COMPLETE: snapshot limit reached; live observation continues.");
                }
            }
            catch (Exception error)
            {
                CloseLog();
                Logger.LogWarning("DAVECOOP_DISCOVERY_LOG_DISABLED: " + error.Message);
            }
        }

        [HideFromIl2Cpp]
        private void CloseLog()
        {
            _fileLoggingStopped = true;
            StreamWriter writer = _writer;
            _writer = null;
            if (writer == null) return;
            try { writer.Dispose(); }
            catch (Exception error) { Logger.LogWarning("Discovery log close failed: " + error.Message); }
        }

        public void OnDestroy() { CloseLog(); }
    }
}
