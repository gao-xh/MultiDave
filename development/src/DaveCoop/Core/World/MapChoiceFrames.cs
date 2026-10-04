using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DaveCoop.Core.World
{
    // Host selection evidence. No scene epoch, native reference or adopted-world
    // permission is inferred from a complete route or any number of IGP choices.
    public sealed class MapRouteSlice
    {
        public long Generation { get; set; }
        public string RouteFingerprint { get; set; }
        public int EntrySceneId { get; set; }
        public float TotalSceneHeight { get; set; }
        public int Index { get; set; }
        public int Count { get; set; }
        public MapRouteScene[] Scenes { get; set; }
    }

    public sealed class MapIgpChoice
    {
        public long Generation { get; set; }
        public string RouteFingerprint { get; set; }
        public long Revision { get; set; }
        public int SceneId { get; set; }
        public string ControllerAddress { get; set; }
        public bool Addressable { get; set; }
        public string SelectedPrefabName { get; set; }
        public string PrefabObjectName { get; set; }
    }

    public sealed class MapChoiceRetire
    {
        public long Generation { get; set; }
        public string Reason { get; set; }
    }

    public sealed class MapChoiceSnapshot
    {
        public long Generation { get; set; }
        public string RouteFingerprint { get; set; }
        public MapRouteSelection Route { get; set; }
        public MapIgpChoice[] Choices { get; set; } = Array.Empty<MapIgpChoice>();
        public long LastChoiceRevision { get; set; }
        public bool Retired { get; set; }
        public bool ObservationOnly => true;
        public bool HostSelectionApplied => false;
    }

    public static class MapChoiceFrames
    {
        public const int ScenesPerSlice = 8;
        public const int MaxRouteSlices = 4;
        public const int MaxChoices = MapSelections.MaxGroups;
        public const int MaxChoiceHistory = 128;
        public const int MaxRetireReason = 256;
        private const string RoutePrefix = "map-route-v2/";

        public static void Validate(MapRouteSlice slice)
        {
            if (slice == null) throw new ArgumentNullException(nameof(slice));
            Header(slice.Generation, slice.RouteFingerprint);
            MapSelections.ValidateTotalSceneHeight(slice.TotalSceneHeight);
            if (slice.EntrySceneId < 1 || slice.Count < 1 || slice.Count > MaxRouteSlices ||
                slice.Index < 0 || slice.Index >= slice.Count || slice.Scenes == null ||
                slice.Scenes.Length < 1 || slice.Scenes.Length > ScenesPerSlice ||
                (slice.Index + 1 < slice.Count && slice.Scenes.Length != ScenesPerSlice) ||
                (slice.Count == 1 && slice.Scenes.Length < MapSelections.MinScenes))
                throw new ArgumentException("Invalid map route slice boundaries.");
            var ids = new HashSet<int>(); var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (MapRouteScene scene in slice.Scenes)
            {
                Scene(scene);
                if (!ids.Add(scene.SceneId) || !names.Add(scene.SceneName))
                    throw new ArgumentException("Duplicate scene within map route slice.");
            }
        }

        public static void Validate(MapIgpChoice choice)
        {
            if (choice == null) throw new ArgumentNullException(nameof(choice));
            Header(choice.Generation, choice.RouteFingerprint);
            if (choice.Revision < 1 || choice.SceneId < 1) throw new ArgumentException("Invalid IGP choice identity.");
            Text(choice.ControllerAddress, MapSelections.MaxControllerAddress, false);
            Text(choice.SelectedPrefabName, MapSelections.MaxPrefabName, !choice.Addressable);
            Text(choice.PrefabObjectName, MapSelections.MaxPrefabName, choice.Addressable);
        }

        public static void Validate(MapChoiceRetire notice)
        {
            if (notice == null) throw new ArgumentNullException(nameof(notice));
            if (notice.Generation < 1) throw new ArgumentException("Invalid map choice retirement generation.");
            Text(notice.Reason, MaxRetireReason, false);
        }

        public static MapRouteSlice Copy(MapRouteSlice slice)
        {
            Validate(slice);
            return new MapRouteSlice
            {
                Generation = slice.Generation, RouteFingerprint = slice.RouteFingerprint, EntrySceneId = slice.EntrySceneId,
                TotalSceneHeight = slice.TotalSceneHeight,
                Index = slice.Index, Count = slice.Count, Scenes = slice.Scenes.Select(CopyScene).ToArray()
            };
        }

        public static MapIgpChoice Copy(MapIgpChoice choice)
        {
            Validate(choice);
            return new MapIgpChoice
            {
                Generation = choice.Generation, RouteFingerprint = choice.RouteFingerprint, Revision = choice.Revision,
                SceneId = choice.SceneId, ControllerAddress = choice.ControllerAddress, Addressable = choice.Addressable,
                SelectedPrefabName = choice.SelectedPrefabName ?? "", PrefabObjectName = choice.PrefabObjectName ?? ""
            };
        }

        public static MapChoiceRetire Copy(MapChoiceRetire notice)
        {
            Validate(notice); return new MapChoiceRetire { Generation = notice.Generation, Reason = notice.Reason };
        }

        public static MapRouteSlice[] SplitRoute(MapRouteSelection route, long generation)
        {
            if (generation < 1) throw new ArgumentException("Invalid map choice generation.");
            MapRouteSelection owned = MapSelections.CopyRoute(route);
            string fingerprint = MapSelections.FingerprintRoute(owned);
            int count = (owned.Scenes.Length + ScenesPerSlice - 1) / ScenesPerSlice;
            var result = new MapRouteSlice[count];
            for (int index = 0; index < count; index++)
            {
                int length = Math.Min(ScenesPerSlice, owned.Scenes.Length - index * ScenesPerSlice);
                var scenes = new MapRouteScene[length];
                for (int i = 0; i < length; i++) scenes[i] = CopyScene(owned.Scenes[index * ScenesPerSlice + i]);
                result[index] = new MapRouteSlice
                {
                    Generation = generation, RouteFingerprint = fingerprint, EntrySceneId = owned.EntrySceneId,
                    TotalSceneHeight = owned.TotalSceneHeight,
                    Index = index, Count = count, Scenes = scenes
                };
            }
            return result;
        }

        internal static string SliceIdentity(MapRouteSlice slice)
        {
            var hash = new CanonicalHash("map-route-slice-v2").Add(slice.Generation.ToString(CultureInfo.InvariantCulture))
                .Add(slice.RouteFingerprint).Add(slice.EntrySceneId).Add(slice.TotalSceneHeight).Add(slice.Index).Add(slice.Count).Add(slice.Scenes.Length);
            foreach (MapRouteScene scene in slice.Scenes)
                hash.Add(scene.SceneId).Add(scene.SceneName).Add((int)scene.Layer).Add(scene.TopConnection ?? "").Add(scene.BottomConnection ?? "")
                    .Add(scene.TopY).Add(scene.BottomY).Add(scene.MapHeight).Add(scene.Offset)
                    .Add(scene.Priority).Add(scene.PreferenceWeight).Add(scene.PreloadAndNotUnloadable ? 1 : 0)
                    .Add(scene.PreviousSceneId).Add(scene.NextSceneId);
            return hash.Finish();
        }

        internal static bool SameChoice(MapIgpChoice left, MapIgpChoice right) =>
            left.Generation == right.Generation && left.RouteFingerprint == right.RouteFingerprint && left.Revision == right.Revision &&
            left.SceneId == right.SceneId && left.ControllerAddress == right.ControllerAddress && left.Addressable == right.Addressable &&
            (left.SelectedPrefabName ?? "") == (right.SelectedPrefabName ?? "") && (left.PrefabObjectName ?? "") == (right.PrefabObjectName ?? "");

        private static MapRouteScene CopyScene(MapRouteScene scene) => new MapRouteScene
        {
            SceneId = scene.SceneId, SceneName = scene.SceneName, Layer = scene.Layer,
            TopConnection = scene.TopConnection ?? "", BottomConnection = scene.BottomConnection ?? "",
            TopY = scene.TopY, BottomY = scene.BottomY, MapHeight = scene.MapHeight, Offset = scene.Offset,
            Priority = scene.Priority, PreferenceWeight = scene.PreferenceWeight, PreloadAndNotUnloadable = scene.PreloadAndNotUnloadable,
            PreviousSceneId = scene.PreviousSceneId, NextSceneId = scene.NextSceneId
        };

        private static void Header(long generation, string fingerprint)
        {
            if (generation < 1 || fingerprint == null || fingerprint.Length != RoutePrefix.Length + 64 ||
                !fingerprint.StartsWith(RoutePrefix, StringComparison.Ordinal)) throw new ArgumentException("Invalid map route generation or fingerprint.");
            for (int i = RoutePrefix.Length; i < fingerprint.Length; i++)
                if (!((fingerprint[i] >= '0' && fingerprint[i] <= '9') || (fingerprint[i] >= 'a' && fingerprint[i] <= 'f')))
                    throw new ArgumentException("Expected a lowercase map route fingerprint.");
        }

        // A slice validates each bounded scene locally. Only final assembly can
        // validate connections across slices and the complete route fingerprint.
        private static void Scene(MapRouteScene scene)
        {
            if (scene == null || scene.SceneId < 1 || scene.PreviousSceneId < 0 || scene.NextSceneId < 0 ||
                scene.SceneId == scene.PreviousSceneId || scene.SceneId == scene.NextSceneId ||
                char.IsControl(scene.Layer) || char.IsWhiteSpace(scene.Layer) || char.IsSurrogate(scene.Layer))
                throw new ArgumentException("Invalid map route scene identity.");
            Text(scene.SceneName, MapSelections.MaxSceneName, false);
            Text(scene.TopConnection, MapSelections.MaxConnection, true); Text(scene.BottomConnection, MapSelections.MaxConnection, true);
            Coordinate(scene.TopY); Coordinate(scene.BottomY); Coordinate(scene.Offset);
            if (!float.IsFinite(scene.MapHeight) || scene.MapHeight <= 0 || scene.MapHeight > 1000000)
                throw new ArgumentException("Invalid map route height.");
        }

        private static void Coordinate(float value)
        {
            if (!float.IsFinite(value) || Math.Abs(value) > 1000000) throw new ArgumentException("Invalid map route coordinate.");
        }

        private static void Text(string value, int maximum, bool optional)
        {
            if (value == null) { if (optional) return; throw new ArgumentException("Missing map choice text."); }
            if (value.Length > maximum || (!optional && string.IsNullOrWhiteSpace(value))) throw new ArgumentException("Invalid map choice text length.");
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsControl(c)) throw new ArgumentException("Control character in map choice text.");
                if (!char.IsSurrogate(c)) continue;
                if (!char.IsHighSurrogate(c) || i + 1 >= value.Length || !char.IsLowSurrogate(value[++i]))
                    throw new ArgumentException("Invalid Unicode in map choice text.");
            }
        }
    }

    // Consumer-thread state. Construct a new assembler for a new room; there is
    // intentionally no Clear that could discard retirement/replay high water.
    public sealed class MapChoiceAssembler
    {
        private long _generation;
        private string _fingerprint;
        private int _entrySceneId;
        private float _totalSceneHeight;
        private MapRouteSlice[] _slices;
        private string[] _sliceIdentities;
        private int _nextSlice;
        private MapRouteSelection _route;
        private bool _retired;
        private string _retireReason;
        private long _revision;
        private readonly Dictionary<(int Scene, string Address), MapIgpChoice> _choices = new Dictionary<(int, string), MapIgpChoice>();
        private readonly Dictionary<long, MapIgpChoice> _history = new Dictionary<long, MapIgpChoice>();
        private readonly Queue<long> _historyOrder = new Queue<long>();

        public MapChoiceSnapshot Snapshot => new MapChoiceSnapshot
        {
            Generation = _generation, RouteFingerprint = _fingerprint, Route = _route == null ? null : MapSelections.CopyRoute(_route),
            Choices = _choices.Values.OrderBy(choice => choice.SceneId).ThenBy(choice => choice.ControllerAddress, StringComparer.Ordinal)
                .Select(MapChoiceFrames.Copy).ToArray(),
            LastChoiceRevision = _revision, Retired = _retired
        };

        public bool AcceptRoute(MapRouteSlice slice)
        {
            MapRouteSlice owned = MapChoiceFrames.Copy(slice);
            if (owned.Generation < _generation || (owned.Generation == _generation && _retired)) return false;
            if (owned.Generation > _generation)
            {
                if (owned.Index != 0) throw new ArgumentException("New map route must start at slice zero.");
                Begin(owned);
            }
            if (_slices == null || owned.RouteFingerprint != _fingerprint || owned.EntrySceneId != _entrySceneId ||
                owned.TotalSceneHeight != _totalSceneHeight || owned.Count != _slices.Length)
                throw new ArgumentException("Conflicting map route assembly header.");
            string identity = MapChoiceFrames.SliceIdentity(owned);
            if (owned.Index < _nextSlice)
            {
                if (_sliceIdentities[owned.Index] != identity) throw new ArgumentException("Map route slice replay changed its payload.");
                return _route != null;
            }
            if (owned.Index != _nextSlice) throw new ArgumentException("Map route slices arrived out of order.");

            // Build the prospective full prefix without changing stored slices.
            // Invalid final data cannot expose a partial or advance its cursor.
            var scenes = new List<MapRouteScene>(MapSelections.MaxScenes);
            for (int i = 0; i < _nextSlice; i++) scenes.AddRange(_slices[i].Scenes);
            scenes.AddRange(owned.Scenes);
            var ids = new HashSet<int>(); var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (MapRouteScene scene in scenes)
                if (!ids.Add(scene.SceneId) || !names.Add(scene.SceneName)) throw new ArgumentException("Duplicate scene across map route slices.");
            MapRouteSelection complete = null;
            if (owned.Index + 1 == owned.Count)
            {
                complete = MapSelections.CopyRoute(new MapRouteSelection { EntrySceneId = _entrySceneId, TotalSceneHeight = _totalSceneHeight, Scenes = scenes.ToArray() });
                if (MapSelections.FingerprintRoute(complete) != _fingerprint) throw new ArgumentException("Map route fingerprint does not match the assembled selection.");
            }
            _slices[owned.Index] = owned; _sliceIdentities[owned.Index] = identity; _nextSlice++;
            if (complete != null) _route = complete;
            return complete != null;
        }

        public bool AcceptChoice(MapIgpChoice choice)
        {
            MapIgpChoice owned = MapChoiceFrames.Copy(choice);
            if (owned.Generation < _generation || (owned.Generation == _generation && _retired)) return false;
            if (owned.Generation != _generation || _route == null || owned.RouteFingerprint != _fingerprint)
                throw new ArgumentException("IGP choice requires its committed map route.");
            if (!_route.Scenes.Any(scene => scene.SceneId == owned.SceneId)) throw new ArgumentException("IGP choice references an unknown route scene.");
            if (owned.Revision <= _revision)
            {
                if (!_history.TryGetValue(owned.Revision, out MapIgpChoice previous)) return false;
                if (!MapChoiceFrames.SameChoice(previous, owned)) throw new ArgumentException("IGP choice revision replay changed its payload.");
                return true;
            }
            if (_revision == long.MaxValue || owned.Revision != _revision + 1) throw new ArgumentException("IGP choice revisions must be continuous.");
            var key = (owned.SceneId, owned.ControllerAddress);
            if (!_choices.ContainsKey(key) && _choices.Count >= MapChoiceFrames.MaxChoices)
                throw new ArgumentException("IGP selection capacity exceeded.");
            _choices[key] = owned; _revision = owned.Revision;
            _history.Add(owned.Revision, owned); _historyOrder.Enqueue(owned.Revision);
            if (_historyOrder.Count > MapChoiceFrames.MaxChoiceHistory) _history.Remove(_historyOrder.Dequeue());
            return true;
        }

        public void Retire(MapChoiceRetire notice)
        {
            MapChoiceRetire owned = MapChoiceFrames.Copy(notice);
            if (owned.Generation < _generation) return;
            if (owned.Generation == _generation && _retired)
            {
                if (owned.Reason != _retireReason) throw new ArgumentException("Map choice retirement replay changed its reason.");
                return;
            }
            if (owned.Generation > _generation) { _generation = owned.Generation; _fingerprint = null; _revision = 0; }
            _route = null; _slices = null; _sliceIdentities = null; _nextSlice = 0;
            _choices.Clear(); _history.Clear(); _historyOrder.Clear(); _retired = true; _retireReason = owned.Reason;
        }

        private void Begin(MapRouteSlice first)
        {
            _generation = first.Generation; _fingerprint = first.RouteFingerprint; _entrySceneId = first.EntrySceneId;
            _totalSceneHeight = first.TotalSceneHeight;
            _slices = new MapRouteSlice[first.Count]; _sliceIdentities = new string[first.Count]; _nextSlice = 0;
            _route = null; _choices.Clear(); _history.Clear(); _historyOrder.Clear(); _revision = 0;
            _retired = false; _retireReason = null;
        }
    }
}
