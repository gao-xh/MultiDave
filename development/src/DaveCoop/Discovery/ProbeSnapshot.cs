using System.Collections.Generic;

namespace DaveCoop
{
    // Plain CLR data only: serialization never calls an IL2CPP/Unity property.
    public sealed class ProbeSnapshot
    {
        public string Kind { get; set; } = "snapshot";
        public string Utc { get; set; }
        public int Sequence { get; set; }
        public SceneObservation ActiveScene { get; set; }
        public List<PlayerObservation> Players { get; set; } = new List<PlayerObservation>();
        public List<ManagerObservation> Managers { get; set; } = new List<ManagerObservation>();
        public ObjectObservation MainCamera { get; set; }
        public List<CameraObservation> Cameras { get; set; } = new List<CameraObservation>();
        public List<string> Errors { get; set; } = new List<string>();
    }

    public sealed class SceneObservation
    {
        public string Name { get; set; }
        public int Handle { get; set; }
    }

    public sealed class ObjectObservation
    {
        public int Id { get; set; }
        public int GameObjectId { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public int[] AncestorGameObjectIds { get; set; }
        public SceneObservation Scene { get; set; }
        public bool Active { get; set; }
        public bool? Enabled { get; set; }
    }

    public sealed class ManagerObservation
    {
        public ObjectObservation Object { get; set; }
        public int? PlayerId { get; set; }
    }

    public sealed class PlayerObservation
    {
        public ObjectObservation Object { get; set; }
        public bool IsManagerPlayer { get; set; }
        public float[] Position { get; set; }
        public float[] Rotation { get; set; }
        public float[] Scale { get; set; }
        public float[] Look { get; set; }
        public float[] MoveInput { get; set; }
        public bool? MoveLocked { get; set; }
        public bool? CustomMoveInput { get; set; }
        public AnimatorObservation Animator { get; set; }
    }

    public sealed class AnimatorObservation
    {
        public int Id { get; set; }
        public int LayerCount { get; set; }
        public List<AnimatorLayerObservation> Layers { get; set; } = new List<AnimatorLayerObservation>();
    }

    public sealed class AnimatorLayerObservation
    {
        public int Layer { get; set; }
        public int StateHash { get; set; }
        public float NormalizedTime { get; set; }
    }

    public sealed class CameraObservation
    {
        public string Kind { get; set; }
        public ObjectObservation Object { get; set; }
        public ObjectObservation Camera { get; set; }
        public ObjectObservation Target { get; set; }
        public ObjectObservation SecondTarget { get; set; }
        public int? TargetPlayerId { get; set; }
    }
}
