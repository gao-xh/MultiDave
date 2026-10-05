using DaveCoop.Core.Crew;
using DaveCoop.Core.Session;
using DaveCoop.Core.World;

namespace DaveCoop.Networking
{
    // Only the owner's currently retained exact token can be validated. Native
    // references remain local; the copied sample grants no action permissions.
    internal sealed class HostEmployeeFishInterestWindow
    {
        internal CrewActorController Owner { get; }
        internal SessionPeer Peer { get; }
        internal HostEmployeeActorBody Body { get; }
        internal HostCrewControl Control { get; }
        internal long Fence { get; }
        public HostFishBodySample Sample { get; }
        public int SceneHandle { get; }

        internal HostEmployeeFishInterestWindow(CrewActorController owner, SessionPeer peer,
            HostEmployeeActorBody body, HostCrewControl control, long fence,
            HostFishBodySample sample, int sceneHandle)
        {
            Owner = owner; Peer = peer; Body = body; Control = control;
            Fence = fence; Sample = sample; SceneHandle = sceneHandle;
        }
    }
}
