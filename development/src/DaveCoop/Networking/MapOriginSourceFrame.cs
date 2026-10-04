using System;
using DaveCoop.Core.World;

namespace DaveCoop.Networking
{
    // Owned CLR values. The same synthetic values never grant native authority.
    internal sealed class MapOriginSourceFrame
    {
        public Guid RunId { get; set; }
        public bool Healthy { get; set; }
        public MapOriginSourceSnapshot Source { get; set; }
        public bool ObservationOnly => true;
        public bool NativeGenerationBound => false;
        public bool HostSelectionApplied => false;
        public bool WorldAuthority => false;
        public bool CargoAuthority => false;
    }
}
