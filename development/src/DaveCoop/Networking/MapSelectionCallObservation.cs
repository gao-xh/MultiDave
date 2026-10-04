using DaveCoop.Core.World;

namespace DaveCoop.Networking
{
    // Native wrappers live only during Capture. Queued observations contain
    // copied CLR values, and are never a map-adoption permission.
    internal sealed class MapSelectionCallObservation
    {
        public long ProcessSequence { get; set; }
        public string Stage { get; set; }
        public int CallbackThreadId { get; set; }
        public int? UnityFrame { get; set; }
        public bool MainThread { get; set; }
        public MapRouteSelection Route { get; set; }
        public string RouteFingerprint { get; set; }
        public string RouteUnavailableReason { get; set; }
        public string ControllerSceneName { get; set; }
        public string ControllerAddress { get; set; }
        public bool? SelectionPresent { get; set; }
        public bool? Addressable { get; set; }
        public string SelectedPrefabName { get; set; }
        public string PrefabObjectName { get; set; }
        public string SceneLoadKey { get; set; }
        public int? SceneLoadMode { get; set; }
        public bool? ActivateOnLoad { get; set; }
        public bool Truncated { get; set; }
        public string ReadError { get; set; }
        public bool ObservationOnly => true;
        public bool CrossMachineAddressVerified => false;
        public bool HostSelectionApplied => false;
        public bool ResourceLoadCompletionObserved => false;
    }
}
