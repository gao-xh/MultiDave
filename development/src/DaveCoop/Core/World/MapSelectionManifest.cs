using System;
using System.Collections.Generic;
using System.Linq;

namespace DaveCoop.Core.World
{
    // A route/IGP selection description, not a player save or a complete world
    // snapshot. Hierarchy addresses are cross-machine identity candidates only.
    public sealed class MapSelectionManifest
    {
        public int EntrySceneId { get; set; }
        public MapRouteScene[] Scenes { get; set; }
        public MapGroupSelection[] Groups { get; set; }
    }

    public sealed class MapRouteScene
    {
        public int SceneId { get; set; }
        public string SceneName { get; set; }
        public char Layer { get; set; }
        public string TopConnection { get; set; }
        public string BottomConnection { get; set; }
        public float TopY { get; set; }
        public float BottomY { get; set; }
        public float MapHeight { get; set; }
        public float Offset { get; set; }
        public int PreviousSceneId { get; set; }
        public int NextSceneId { get; set; }
    }

    public sealed class MapGroupSelection
    {
        public int SceneId { get; set; }
        public string ControllerAddress { get; set; }
        public bool Addressable { get; set; }
        public string SelectedPrefabName { get; set; }
        public string PrefabObjectName { get; set; }
    }

    public static class MapSelections
    {
        public const int MinScenes = 3;
        public const int MaxScenes = 32;
        public const int MaxGroups = 128;
        public const int MaxSceneName = 160;
        public const int MaxConnection = 256;
        public const int MaxControllerAddress = 4096;
        public const int MaxPrefabName = 512;

        public static void Validate(MapSelectionManifest manifest)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (manifest.EntrySceneId < 1 || manifest.Scenes == null || manifest.Scenes.Length < MinScenes ||
                manifest.Scenes.Length > MaxScenes || manifest.Groups == null || manifest.Groups.Length == 0 ||
                manifest.Groups.Length > MaxGroups) throw new ArgumentException("Incomplete or excessive map selection.");
            var byId = new Dictionary<int, MapRouteScene>(); var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (MapRouteScene scene in manifest.Scenes)
            {
                if (scene == null || scene.SceneId < 1 || scene.PreviousSceneId < 0 || scene.NextSceneId < 0 ||
                    scene.SceneId == scene.PreviousSceneId || scene.SceneId == scene.NextSceneId ||
                    char.IsControl(scene.Layer) || char.IsWhiteSpace(scene.Layer) || char.IsSurrogate(scene.Layer))
                    throw new ArgumentException("Invalid route scene identity.");
                Text(scene.SceneName, MaxSceneName, false); Text(scene.TopConnection, MaxConnection, true);
                Text(scene.BottomConnection, MaxConnection, true);
                Coordinate(scene.TopY); Coordinate(scene.BottomY); Coordinate(scene.Offset);
                if (!float.IsFinite(scene.MapHeight) || scene.MapHeight <= 0 || scene.MapHeight > 1000000)
                    throw new ArgumentException("Invalid route height.");
                if (!byId.TryAdd(scene.SceneId, scene) || !names.Add(scene.SceneName))
                    throw new ArgumentException("Ambiguous route scene identity.");
            }
            if (!byId.TryGetValue(manifest.EntrySceneId, out MapRouteScene first) || first.PreviousSceneId != 0)
                throw new ArgumentException("Missing route entry.");
            var visited = new HashSet<int>(); MapRouteScene current = first;
            while (current != null)
            {
                if (!visited.Add(current.SceneId)) throw new ArgumentException("Cyclic route selection.");
                if (current.NextSceneId == 0) break;
                if (!byId.TryGetValue(current.NextSceneId, out MapRouteScene next) || next.PreviousSceneId != current.SceneId)
                    throw new ArgumentException("Inconsistent route connection.");
                current = next;
            }
            if (visited.Count != manifest.Scenes.Length) throw new ArgumentException("Disconnected route selection.");
            var groupAddresses = new Dictionary<int, HashSet<string>>();
            foreach (MapGroupSelection group in manifest.Groups)
            {
                if (group == null || !byId.ContainsKey(group.SceneId)) throw new ArgumentException("Unknown group scene.");
                Text(group.ControllerAddress, MaxControllerAddress, false);
                Text(group.SelectedPrefabName, MaxPrefabName, !group.Addressable);
                Text(group.PrefabObjectName, MaxPrefabName, group.Addressable);
                if (!groupAddresses.TryGetValue(group.SceneId, out HashSet<string> addresses))
                { addresses = new HashSet<string>(StringComparer.Ordinal); groupAddresses.Add(group.SceneId, addresses); }
                if (!addresses.Add(group.ControllerAddress)) throw new ArgumentException("Ambiguous group address.");
            }
        }

        // Arrays and every mutable element belong to the returned DTO. Canonical
        // order depends on scene IDs/addresses, not native discovery/list order.
        public static MapSelectionManifest Copy(MapSelectionManifest manifest)
        {
            Validate(manifest);
            return new MapSelectionManifest
            {
                EntrySceneId = manifest.EntrySceneId,
                Scenes = manifest.Scenes.OrderBy(scene => scene.SceneId).Select(scene => new MapRouteScene
                {
                    SceneId = scene.SceneId, SceneName = scene.SceneName, Layer = scene.Layer,
                    TopConnection = scene.TopConnection ?? "", BottomConnection = scene.BottomConnection ?? "",
                    TopY = scene.TopY, BottomY = scene.BottomY, MapHeight = scene.MapHeight, Offset = scene.Offset,
                    PreviousSceneId = scene.PreviousSceneId, NextSceneId = scene.NextSceneId
                }).ToArray(),
                Groups = manifest.Groups.OrderBy(group => group.SceneId).ThenBy(group => group.ControllerAddress, StringComparer.Ordinal)
                    .Select(group => new MapGroupSelection
                    {
                        SceneId = group.SceneId, ControllerAddress = group.ControllerAddress, Addressable = group.Addressable,
                        SelectedPrefabName = group.SelectedPrefabName ?? "", PrefabObjectName = group.PrefabObjectName ?? ""
                    }).ToArray()
            };
        }

        public static string Fingerprint(MapSelectionManifest manifest)
        {
            MapSelectionManifest canonical = Copy(manifest);
            var hash = new CanonicalHash("map-selection-v1").Add(canonical.EntrySceneId).Add(canonical.Scenes.Length);
            foreach (MapRouteScene scene in canonical.Scenes)
            {
                hash.Add(scene.SceneId).Add(scene.SceneName).Add((int)scene.Layer).Add(scene.TopConnection).Add(scene.BottomConnection)
                    .Add(scene.TopY).Add(scene.BottomY).Add(scene.MapHeight).Add(scene.Offset)
                    .Add(scene.PreviousSceneId).Add(scene.NextSceneId);
            }
            hash.Add(canonical.Groups.Length);
            foreach (MapGroupSelection group in canonical.Groups)
                hash.Add(group.SceneId).Add(group.ControllerAddress).Add(group.Addressable ? 1 : 0)
                    .Add(group.SelectedPrefabName).Add(group.PrefabObjectName);
            return "map-selection-v1/" + hash.Finish();
        }

        private static void Coordinate(float value)
        {
            if (!float.IsFinite(value) || Math.Abs(value) > 1000000) throw new ArgumentException("Invalid route coordinate.");
        }

        private static void Text(string value, int maximum, bool optional)
        {
            if (value == null) { if (optional) return; throw new ArgumentException("Missing map selection text."); }
            if (value.Length > maximum || (!optional && string.IsNullOrWhiteSpace(value)))
                throw new ArgumentException("Invalid map selection text length.");
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (char.IsControl(character)) throw new ArgumentException("Control character in map selection text.");
                if (!char.IsSurrogate(character)) continue;
                if (!char.IsHighSurrogate(character) || i + 1 >= value.Length || !char.IsLowSurrogate(value[++i]))
                    throw new ArgumentException("Invalid Unicode in map selection text.");
            }
        }
    }
}
