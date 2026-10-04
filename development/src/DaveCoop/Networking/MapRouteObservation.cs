using System;
using System.Text.Json;

namespace DaveCoop.Networking
{
    // Owned CLR diagnostics only. Candidate layers and loaded scene names do
    // not establish a complete selected route or grant map-loading authority.
    internal sealed class MapRouteObservation
    {
        public int Frame { get; set; }
        public bool ContextPresent { get; set; }
        public string ContextSceneName { get; set; }
        public int? CacheCount { get; set; }
        public int? RoadmapCount { get; set; }
        public int? FirstSceneId { get; set; }
        public int? LayerCount { get; set; }
        public int? SelectedLayerCount { get; set; }
        public MapCachedSceneObservation[] CachedScenes { get; set; } = Array.Empty<MapCachedSceneObservation>();
        public string[] SelectedLayerNames { get; set; } = Array.Empty<string>();
        public string[] LoadedSceneNames { get; set; } = Array.Empty<string>();
        public bool CacheTruncated { get; set; }
        public bool SelectedLayersTruncated { get; set; }
        public bool SelectedLayerScanIncomplete { get; set; }
        public bool LoadedScenesTruncated { get; set; }
        public bool NamesTruncated { get; set; }
        public bool Truncated { get; set; }
        public bool LimitsExceeded { get; set; }

        // Frames change every Update; the trace key changes only with the
        // copied input values. This is a diagnostic key, not a map fingerprint.
        public string TraceKey()
        {
            return JsonSerializer.Serialize(new
            {
                ContextPresent, ContextSceneName, CacheCount, RoadmapCount,
                FirstSceneId, LayerCount, SelectedLayerCount, CachedScenes,
                SelectedLayerNames, LoadedSceneNames, CacheTruncated,
                SelectedLayersTruncated, SelectedLayerScanIncomplete,
                LoadedScenesTruncated, NamesTruncated, Truncated, LimitsExceeded
            });
        }
    }

    internal sealed class MapCachedSceneObservation
    {
        public int SceneId { get; set; }
        public string SceneName { get; set; }
        public bool Selected { get; set; }
        public bool Loaded { get; set; }
    }
}
