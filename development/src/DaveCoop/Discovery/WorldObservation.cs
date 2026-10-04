using System.Collections.Generic;

namespace DaveCoop.Discovery
{
    // Process-local IDs here are diagnostics, never shared-world entity IDs.
    internal sealed class WorldObservation
    {
        public string Kind { get; set; } = "world-snapshot";
        public string Utc { get; set; }
        public int Sequence { get; set; }
        public string Scene { get; set; }
        public int? ManagerId { get; set; }
        public bool? LoadedAll { get; set; }
        public int? ManagerAllocatorCount { get; set; }
        public Dictionary<string, int> FoundCounts { get; set; } = new Dictionary<string, int>();
        public List<string> TruncatedCollections { get; set; } = new List<string>();
        public List<NodeObservation> Nodes { get; set; } = new List<NodeObservation>();
        public List<GroupSetObservation> GroupSets { get; set; } = new List<GroupSetObservation>();
        public List<AllocatorObservation> Allocators { get; set; } = new List<AllocatorObservation>();
        public List<FishObservation> Fish { get; set; } = new List<FishObservation>();
        public List<ItemObservation> Items { get; set; } = new List<ItemObservation>();
        public List<string> Errors { get; set; } = new List<string>();
    }

    internal sealed class NodeObservation
    {
        public ObjectObservation Object { get; set; }
        public string UniqueId { get; set; }
        public string DefaultAddress { get; set; }
        public string SelectedAddress { get; set; }
    }

    internal sealed class GroupSetObservation
    {
        public ObjectObservation Object { get; set; }
        public string SelectedPrefab { get; set; }
        public string CurrentObject { get; set; }
    }

    internal sealed class AllocatorObservation
    {
        public ObjectObservation Object { get; set; }
        public int? InstanceCount { get; set; }
        public int OverrideSpeciesTid { get; set; }
        public bool ManualSpawn { get; set; }
    }

    internal sealed class FishObservation
    {
        public ObjectObservation Object { get; set; }
        public int SpeciesTid { get; set; }
        public float[] Position { get; set; }
        public float? Hp { get; set; }
        public float? MaxHp { get; set; }
        public bool Captured { get; set; }
        public bool? Dead { get; set; }
        public bool Stopped { get; set; }
        public bool Hooked { get; set; }
        public bool Flock { get; set; }
        public string RigidbodyType { get; set; }
        public int SpriteParts { get; set; }
        public int MeshParts { get; set; }
    }

    internal sealed class ItemObservation
    {
        public ObjectObservation Object { get; set; }
        public int PresetItemId { get; set; }
        public float[] Position { get; set; }
        public bool Interactable { get; set; }
    }
}
