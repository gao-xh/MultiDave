using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using BepInEx;
using DR.AI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityObject = UnityEngine.Object;

namespace DaveCoop.Discovery
{
    internal sealed class WorldProbeController : IDisposable
    {
        private const long MaxLogBytes = 32 * 1024 * 1024;
        private const int MaxErrors = 64;
        private StreamWriter _writer;
        private int _written;
        private long _bytes;
        private float _nextCapture;
        private bool _ready;
        private bool _loggingStopped;
        private bool _failureLogged;

        public void Update()
        {
            try
            {
                if (!_ready)
                {
                    _ready = true;
                    WorldProbe.Logger.LogInfo("DAVECOOP_WORLD_PROBE_READY: F7 toggles bounded read-only world observations; disabled by default.");
                }
                if (Input.GetKeyDown(KeyCode.F7)) WorldProbe.Enabled.Value = !WorldProbe.Enabled.Value;
                if (!WorldProbe.Enabled.Value) { Close(); return; }
                if (_loggingStopped || Time.unscaledTime < _nextCapture) return;
                _nextCapture = Time.unscaledTime + 2f;
                if (_written >= Math.Clamp(WorldProbe.MaxSnapshots.Value, 1, 1800)) { Stop(); return; }
                if (_writer == null) Open();
                WorldObservation snapshot = Capture();
                string json = JsonSerializer.Serialize(snapshot);
                long bytes = Encoding.UTF8.GetByteCount(json) + 1;
                if (bytes > MaxLogBytes - _bytes) { Stop(); return; }
                _writer.WriteLine(json); _writer.Flush(); _written++; _bytes += bytes;
                WorldProbe.Logger.LogInfo("DAVECOOP_WORLD_STATE: " + JsonSerializer.Serialize(new
                {
                    snapshot.Sequence, snapshot.Scene, snapshot.LoadedAll, snapshot.FoundCounts,
                    ObservedFish = snapshot.Fish.Count, ObservedItems = snapshot.Items.Count,
                    snapshot.TruncatedCollections, Errors = snapshot.Errors.Count
                }));
            }
            catch (Exception error)
            {
                Close(); _loggingStopped = true;
                if (!_failureLogged)
                {
                    _failureLogged = true;
                    WorldProbe.Logger.LogWarning("DAVECOOP_WORLD_WARNING: probe stopped: " + Limit(error.Message, 256));
                }
            }
        }

        private WorldObservation Capture()
        {
            var result = new WorldObservation
            {
                Utc = DateTime.UtcNow.ToString("o"), Sequence = _written + 1,
                Scene = Limit(SceneManager.GetActiveScene().name, 256)
            };
            Observe<InGameManager>("managers", 16, result, manager =>
            {
                if (result.ManagerId.HasValue || manager.playerCharacter == null) return;
                result.ManagerId = manager.GetInstanceID(); result.LoadedAll = manager.IsLoadedAll;
                var allocators = manager.FishAllocators;
                result.ManagerAllocatorCount = allocators == null ? (int?)null : allocators.Count;
            });
            Observe<DynamicIngameNodeLoader>("nodes", 128, result, node => result.Nodes.Add(new NodeObservation
            {
                Object = Describe(node), UniqueId = Limit(node.UniqueID, 256),
                DefaultAddress = Limit(node.defaultPrefabAddressName, 512), SelectedAddress = Limit(node.addressablePrefabName, 512)
            }));
            Observe<IGPSetController>("group-sets", 32, result, group =>
            {
                var selected = group.CurrIGPSetInfo; var current = group.CurrIGPSet;
                result.GroupSets.Add(new GroupSetObservation
                {
                    Object = Describe(group), SelectedPrefab = selected == null ? null : Limit(selected.prefabName, 512),
                    CurrentObject = current == null ? null : Limit(current.name, 256)
                });
            });
            Observe<FishAllocator>("allocators", 256, result, allocator =>
            {
                var fish = allocator.GetInstancedFishs;
                result.Allocators.Add(new AllocatorObservation
                {
                    Object = Describe(allocator), InstanceCount = fish == null ? (int?)null : fish.Count,
                    OverrideSpeciesTid = allocator._overwriteFishDataTID, ManualSpawn = allocator._spawnManual
                });
            });
            Observe<FishAISystem>("fish", 256, result, fish =>
            {
                var body = fish.FishRigidbody; var damageable = fish.FishDamageable;
                result.Fish.Add(new FishObservation
                {
                    Object = Describe(fish), SpeciesTid = fish.FishDataTID, Position = Position(fish.transform),
                    Hp = Finite(fish.HP), MaxHp = Finite(fish.MaxHP), Captured = fish.IsFishCaptured,
                    Dead = damageable == null ? (bool?)null : damageable.m_IsDead,
                    Stopped = fish.IsFishStopped, Hooked = fish.IsFishHooked, Flock = fish.IsFlockFish,
                    RigidbodyType = body == null ? null : body.bodyType.ToString(),
                    SpriteParts = fish.GetComponentsInChildren<SpriteRenderer>(true).Length,
                    MeshParts = fish.GetComponentsInChildren<MeshRenderer>(true).Length
                });
            });
            Observe<PickupInstanceItem>("items", 128, result, item => result.Items.Add(new ItemObservation
            {
                Object = Describe(item), PresetItemId = item.presetItemID,
                Position = Position(item.transform), Interactable = item.GetIsEnableInteraction
            }));
            return result;
        }

        private static void Observe<T>(string label, int limit, WorldObservation result, Action<T> capture) where T : Component
        {
            try
            {
                var found = UnityObject.FindObjectsOfType<T>(); result.FoundCounts[label] = found.Length;
                if (found.Length > limit) result.TruncatedCollections.Add(label);
                for (int i = 0; i < Math.Min(found.Length, limit); i++)
                {
                    T item = found[i];
                    if (item == null || !item.gameObject.activeInHierarchy) continue;
                    try { capture(item); }
                    catch (Exception error) { Error(result, label, error); }
                }
            }
            catch (Exception error) { Error(result, label, error); }
        }

        private static ObjectObservation Describe(Component item)
        {
            GameObject gameObject = item.gameObject;
            var path = new List<string>(); Transform node = item.transform;
            for (int depth = 0; node != null && depth < 32; depth++, node = node.parent) path.Add(Limit(node.name, 128));
            path.Reverse();
            var behavior = item.TryCast<Behaviour>();
            return new ObjectObservation
            {
                Id = item.GetInstanceID(), GameObjectId = gameObject.GetInstanceID(), Name = Limit(gameObject.name, 256),
                Path = Limit(string.Join("/", path), 2048), Active = gameObject.activeInHierarchy,
                Enabled = behavior == null ? (bool?)null : behavior.enabled,
                Scene = new SceneObservation { Name = Limit(gameObject.scene.name, 256), Handle = gameObject.scene.handle }
            };
        }

        private static float[] Position(Transform transform)
        {
            Vector3 position = transform.position;
            if (!float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z))
                throw new InvalidOperationException("Non-finite world position.");
            return new[] { position.x, position.y, position.z };
        }

        private static float? Finite(float value) { return float.IsFinite(value) ? value : (float?)null; }
        private static string Limit(string value, int limit) { return value == null || value.Length <= limit ? value : value.Substring(0, limit); }
        private static void Error(WorldObservation result, string label, Exception error)
        {
            if (result.Errors.Count < MaxErrors) result.Errors.Add(label + ": " + Limit(error.Message, 256));
        }

        private void Open()
        {
            string folder = Path.Combine(Paths.PluginPath, "DaveCoop", "logs"); Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "world-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".jsonl");
            _writer = new StreamWriter(path, false, new UTF8Encoding(false)) { NewLine = "\n" };
            WorldProbe.Logger.LogInfo("DAVECOOP_WORLD_LOG: " + path);
        }

        private void Stop() { Close(); _loggingStopped = true; WorldProbe.Logger.LogInfo("DAVECOOP_WORLD_LOG_LIMIT: process logging limit reached."); }
        private void Close() { StreamWriter writer = _writer; _writer = null; writer?.Dispose(); }
        public void Dispose() { Close(); }
    }
}
