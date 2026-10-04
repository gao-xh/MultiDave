namespace DaveCoop.Core.World
{
    // Local-only lookup result. This value never belongs in a network message.
    // It is a snapshot, not permission to use a cached native object: adapters
    // must resolve the active epoch/ID again immediately before an operation.
    public readonly struct HostEntityTarget
    {
        public long SceneEpoch { get; }
        public long EntityId { get; }
        public long LocalToken { get; }
        public EntityKind Kind { get; }
        public int DataTid { get; }
        public long Generation { get; }

        internal HostEntityTarget(long sceneEpoch, long entityId, long localToken, EntityKind kind, int dataTid, long generation)
        {
            SceneEpoch = sceneEpoch; EntityId = entityId; LocalToken = localToken;
            Kind = kind; DataTid = dataTid; Generation = generation;
        }
    }
}
