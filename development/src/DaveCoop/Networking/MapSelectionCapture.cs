using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using DaveCoop.Core.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityObject = UnityEngine.Object;

namespace DaveCoop.Networking
{
    // Unity-thread input diagnostics and post-load selection observation.
    // It does not select resources,
    // invoke conditions/save APIs, install hooks, or adopt the host's map. A
    // pre-load authority barrier is a separate, still-unimplemented operation.
    internal sealed class MapSelectionCapture
    {
        private const int MaxHierarchyDepth = 32;
        private const int MaxObjectName = 128;
        private const int MaxCandidateLayers = 512;
        private readonly int _unityThreadId;
        private string _previousFingerprint;
        private long _previousManagerPointer;
        private int _previousFrame = -1;
        public string UnavailableReason { get; private set; }
        public MapRouteObservation LatestRouteObservation { get; private set; }

        // Construct from an already-confirmed Unity Update thread identity.
        public MapSelectionCapture(int unityThreadId)
        {
            if (unityThreadId <= 0) throw new ArgumentOutOfRangeException(nameof(unityThreadId));
            _unityThreadId = unityThreadId;
        }

        public MapSelectionManifest Capture(InGameManager manager)
        {
            if (Thread.CurrentThread.ManagedThreadId != _unityThreadId)
                throw new InvalidOperationException("Map selection reads require the confirmed Unity thread.");
            try { return CaptureLoaded(manager); }
            catch
            {
                Unavailable("Map selection observation rejected.");
                throw;
            }
        }

        // Caller resets observation on session/scene ownership boundaries.
        // This only clears CLR correlation data; no game object is changed.
        public void Clear()
        {
            LatestRouteObservation = null;
            Unavailable("Map selection observation reset.");
        }

        // Available before a player/manager or network peer exists. Only the
        // generated native field proxies are read for route data; original
        // selection getters such as GetSelectedMapLayerCached are not called.
        public MapRouteObservation ReadRouteInputs()
        {
            if (Thread.CurrentThread.ManagedThreadId != _unityThreadId)
                throw new InvalidOperationException("Map route reads require the confirmed Unity thread.");
            LatestRouteObservation = null;
            var observation = new MapRouteObservation { Frame = Time.frameCount };
            var loadedNames = new List<string>();
            int sceneCount = SceneManager.sceneCount;
            if (sceneCount > MapSelections.MaxScenes)
            {
                observation.LoadedScenesTruncated = true;
                observation.LimitsExceeded = true;
            }
            for (int i = 0; i < Math.Min(sceneCount, MapSelections.MaxScenes); i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) loadedNames.Add(ObservationName(scene.name, observation));
            }
            observation.LoadedSceneNames = loadedNames.ToArray();

            SceneContext context = SceneContext._s_Instance_k__BackingField;
            observation.ContextPresent = context != null;
            if (context != null)
            {
                observation.ContextSceneName = ObservationName(context._CurrentSceneName_k__BackingField, observation);
                var cache = context.selectedMapLayerCacheList;
                var roadmap = context.m_SceneRoadmap;
                var first = context._firstData_k__BackingField;
                var layers = context.sceneLayerDataList;
                observation.CacheCount = cache == null ? (int?)null : cache.Count;
                observation.RoadmapCount = roadmap == null ? (int?)null : roadmap.Count;
                observation.FirstSceneId = first == null ? (int?)null : first.sceneID;
                observation.LayerCount = layers == null ? (int?)null : layers.Count;

                if (cache != null)
                {
                    int count = observation.CacheCount.Value;
                    observation.CacheTruncated = count > MapSelections.MaxScenes;
                    if (observation.CacheTruncated) observation.LimitsExceeded = true;
                    var cachedScenes = new List<MapCachedSceneObservation>();
                    for (int i = 0; i < Math.Min(count, MapSelections.MaxScenes); i++)
                    {
                        SceneMapLayerDataCache selected = cache[i];
                        cachedScenes.Add(selected == null ? null : new MapCachedSceneObservation
                        {
                            SceneId = selected.SceneID,
                            SceneName = ObservationName(selected.SceneName, observation),
                            Selected = selected.bSelected, Loaded = selected.IsSceneLoaded
                        });
                    }
                    observation.CachedScenes = cachedScenes.ToArray();
                }
                if (observation.RoadmapCount > MapSelections.MaxScenes) observation.LimitsExceeded = true;
                if (layers != null)
                {
                    int count = observation.LayerCount.Value;
                    observation.SelectedLayerScanIncomplete = count > MaxCandidateLayers;
                    if (observation.SelectedLayerScanIncomplete) observation.LimitsExceeded = true;
                    int selectedCount = 0;
                    var selectedNames = new List<string>();
                    for (int i = 0; i < Math.Min(count, MaxCandidateLayers); i++)
                    {
                        SceneMapLayerData layer = layers[i];
                        if (layer == null || !layer.bSelected) continue;
                        selectedCount++;
                        if (selectedNames.Count < MapSelections.MaxScenes)
                            selectedNames.Add(ObservationName(layer.SceneName, observation));
                    }
                    // A bounded prefix scan is not an exact selected total.
                    observation.SelectedLayerCount = observation.SelectedLayerScanIncomplete ? (int?)null : selectedCount;
                    observation.SelectedLayersTruncated = observation.SelectedLayerScanIncomplete || selectedCount > selectedNames.Count;
                    observation.SelectedLayerNames = selectedNames.ToArray();
                }
            }
            observation.Truncated = observation.CacheTruncated || observation.SelectedLayersTruncated ||
                observation.LoadedScenesTruncated || observation.NamesTruncated;
            LatestRouteObservation = observation;
            return observation;
        }

        private static string ObservationName(string value, MapRouteObservation observation)
        {
            if (value == null || value.Length <= MapSelections.MaxSceneName) return value;
            observation.NamesTruncated = true;
            int length = MapSelections.MaxSceneName;
            if (char.IsHighSurrogate(value[length - 1]) && char.IsLowSurrogate(value[length])) length--;
            return value.Substring(0, length);
        }

        private MapSelectionManifest CaptureLoaded(InGameManager manager)
        {
            ReadRouteInputs();
            if (manager == null || !manager.IsLoadedAll || manager.playerCharacter == null)
                return Unavailable("Manager/player loading.");
            // Read the generated native field wrapper instead of Instance,
            // whose original getter may lazily create/reset the singleton.
            SceneContext context = SceneContext._s_Instance_k__BackingField;
            if (context == null) return Unavailable("Scene route context missing.");
            var cache = context.selectedMapLayerCacheList;
            var roadmap = context.m_SceneRoadmap;
            var first = context._firstData_k__BackingField;
            if (cache == null) return Unavailable("Selected route cache missing.");
            if (roadmap == null) return Unavailable("Selected route roadmap missing.");
            if (first == null) return Unavailable("Selected route first scene missing.");
            if (cache.Count < MapSelections.MinScenes)
                return Unavailable("Selected route cache incomplete: " + cache.Count.ToString(CultureInfo.InvariantCulture) +
                    "/" + MapSelections.MinScenes.ToString(CultureInfo.InvariantCulture) + " scenes.");
            int count = cache.Count;
            if (count > MapSelections.MaxScenes || roadmap.Count > MapSelections.MaxScenes)
                throw new InvalidOperationException("Map route exceeds bounded observation.");
            var scenes = new List<MapRouteScene>(count);
            var routeNames = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                SceneMapLayerDataCache selected = cache[i];
                if (selected == null || !selected.bSelected || !selected.IsSceneLoaded)
                    return Unavailable("Selected route is not fully loaded.");
                if (!roadmap.TryGetValue(selected.SceneID, out SceneContext.SceneRoadmapData road) || road == null)
                    return Unavailable("Selected route roadmap incomplete.");
                if (road.sceneID != selected.SceneID) throw new InvalidOperationException("Route cache/roadmap scene disagreement.");
                string name = selected.SceneName;
                if (name == null || !routeNames.TryAdd(name, selected.SceneID))
                    throw new InvalidOperationException("Ambiguous selected scene name.");
                scenes.Add(new MapRouteScene
                {
                    SceneId = selected.SceneID, SceneName = name, Layer = selected.LayerChar,
                    TopConnection = selected.TopConnIdStr, BottomConnection = selected.BottomConnIdStr,
                    TopY = selected.TopYCoord, BottomY = selected.BottomYCoord, MapHeight = selected.MapHeight,
                    Offset = road.offset, PreviousSceneId = road.previous == null ? 0 : road.previous.sceneID,
                    NextSceneId = road.next == null ? 0 : road.next.sceneID
                });
            }
            if (cache.Count != count || !routeNames.ContainsKey(manager.playerCharacter.gameObject.scene.name))
                return Unavailable("Route changed during observation.");
            var loadedNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || !routeNames.ContainsKey(scene.name)) continue;
                if (!loadedNames.Add(scene.name)) throw new InvalidOperationException("Duplicate loaded selected scene.");
            }
            if (loadedNames.Count != routeNames.Count) return Unavailable("Selected route scenes still loading.");

            var groups = new List<MapGroupSelection>();
            var groupScenes = new HashSet<int>();
            var foundPointers = new HashSet<long>();
            var controllers = UnityObject.FindObjectsOfType<IGPSetController>(true);
            if (controllers.Length > MapSelections.MaxGroups)
                throw new InvalidOperationException("IGP controllers exceed bounded observation.");
            foreach (IGPSetController controller in controllers)
            {
                if (controller == null) return Unavailable("IGP controller destroyed during observation.");
                if (!routeNames.TryGetValue(controller.gameObject.scene.name, out int sceneId)) continue;
                IGPSetInfo selected = controller.CurrIGPSetInfo;
                if (!controller.IsInitDone || selected == null || controller.CurrIGPSet == null)
                    return Unavailable("IGP selection/loading incomplete.");
                if (!foundPointers.Add(controller.Pointer.ToInt64()))
                    throw new InvalidOperationException("Duplicate local IGP controller observation.");
                groupScenes.Add(sceneId);
                // Do not touch GetSaveableInterface, saveDatatype's save object,
                // used-instance sets, random-selection or condition methods.
                groups.Add(new MapGroupSelection
                {
                    SceneId = sceneId, ControllerAddress = Address(controller), Addressable = selected.isAddressableMode,
                    SelectedPrefabName = selected.prefabName,
                    PrefabObjectName = selected.Prefab == null ? null : selected.Prefab.name
                });
            }
            if (groups.Count == 0) return Unavailable("No loaded IGP selections.");
            // Standard observed A/B/C routes have a selected group in every
            // layer. Other route modes remain unavailable instead of guessing
            // that a stable partial set is a complete selection description.
            foreach (int sceneId in routeNames.Values)
                if (!groupScenes.Contains(sceneId)) return Unavailable("Selected scene IGP selection not observed: " + sceneId.ToString(CultureInfo.InvariantCulture));
            // CurrentIGPControllers is an existing generated native field
            // wrapper. Reading it neither calls Init nor a completion method.
            var registered = IGPSetController.CurrentIGPControllers;
            if (registered == null || registered.Count == 0) return Unavailable("IGP controller registration incomplete.");
            int registeredCount = registered.Count;
            if (registeredCount > MapSelections.MaxGroups)
                throw new InvalidOperationException("Registered IGP controllers exceed bounded observation.");
            var registeredPointers = new HashSet<long>();
            for (int i = 0; i < registeredCount; i++)
            {
                IGPSetController controller = registered[i];
                if (controller == null) return Unavailable("Registered IGP controller removed during observation.");
                if (!routeNames.ContainsKey(controller.gameObject.scene.name)) continue;
                if (!controller.IsInitDone || !registeredPointers.Add(controller.Pointer.ToInt64()))
                    return Unavailable("IGP controller registrations are incomplete or repeated.");
            }
            if (registered.Count != registeredCount || !registeredPointers.SetEquals(foundPointers))
                return Unavailable("Loaded/registered IGP controllers disagree.");
            var manifest = new MapSelectionManifest { EntrySceneId = first.sceneID, Scenes = scenes.ToArray(), Groups = groups.ToArray() };
            MapSelectionManifest owned = MapSelections.Copy(manifest);
            string fingerprint = MapSelections.Fingerprint(owned);
            if (!manager.IsLoadedAll || manager.playerCharacter == null || context.Pointer != SceneContext._s_Instance_k__BackingField?.Pointer)
                return Unavailable("Map ownership changed during observation.");
            // Native pointer is local correlation only; it never enters DTO/hash.
            long managerPointer = manager.Pointer.ToInt64(); int frame = Time.frameCount;
            bool repeated = _previousManagerPointer == managerPointer && _previousFingerprint == fingerprint && _previousFrame != frame;
            _previousManagerPointer = managerPointer; _previousFingerprint = fingerprint; _previousFrame = frame;
            if (!repeated) { UnavailableReason = "Awaiting identical selection in a later Unity frame."; return null; }
            UnavailableReason = null;
            return owned;
        }

        private MapSelectionManifest Unavailable(string reason)
        {
            _previousFingerprint = null; _previousManagerPointer = 0; _previousFrame = -1;
            UnavailableReason = reason; return null;
        }

        private static string Address(IGPSetController controller)
        {
            var path = new List<string>(); Transform node = controller.transform;
            while (node != null)
            {
                if (path.Count >= MaxHierarchyDepth || string.IsNullOrWhiteSpace(node.name) || node.name.Length > MaxObjectName)
                    throw new InvalidOperationException("IGP hierarchy address exceeds bounded observation.");
                string name = node.name;
                for (int i = 0; i < name.Length; i++)
                {
                    if (char.IsControl(name[i])) throw new InvalidOperationException("Control character in IGP hierarchy address.");
                    if (!char.IsSurrogate(name[i])) continue;
                    if (!char.IsHighSurrogate(name[i]) || i + 1 >= name.Length || !char.IsLowSurrogate(name[++i]))
                        throw new InvalidOperationException("Invalid Unicode in IGP hierarchy address.");
                }
                int sibling = node.GetSiblingIndex();
                if (sibling < 0) throw new InvalidOperationException("Invalid IGP sibling index.");
                path.Add(Uri.EscapeDataString(name) + "[" + sibling.ToString(CultureInfo.InvariantCulture) + "]");
                node = node.parent;
            }
            path.Reverse();
            var components = controller.gameObject.GetComponents<IGPSetController>();
            if (components.Length == 0 || components.Length > MapSelections.MaxGroups)
                throw new InvalidOperationException("IGP component list exceeds bounded observation.");
            int componentIndex = -1;
            for (int i = 0; i < components.Length; i++)
            {
                IGPSetController component = components[i];
                if (component == null) throw new InvalidOperationException("IGP component list changed during observation.");
                if (component.Pointer != controller.Pointer) continue;
                if (componentIndex >= 0) throw new InvalidOperationException("Ambiguous local IGP component.");
                componentIndex = i;
            }
            if (componentIndex < 0) throw new InvalidOperationException("IGP component missing from owner.");
            string address = string.Join("/", path) + "#IGP[" + componentIndex.ToString(CultureInfo.InvariantCulture) + "]";
            if (address.Length > MapSelections.MaxControllerAddress)
                throw new InvalidOperationException("IGP address would require truncation.");
            return address;
        }
    }
}
