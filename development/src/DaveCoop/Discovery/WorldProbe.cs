using System;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace DaveCoop.Discovery
{
    // Only Unity callbacks are exposed to IL2CPP. The controller reads existing
    // objects on this thread and never invokes spawn, damage, pickup or save APIs.
    public sealed class WorldProbe : MonoBehaviour
    {
        internal static ManualLogSource Logger;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> MaxSnapshots;
        private readonly WorldProbeController _controller = new WorldProbeController();

        public WorldProbe(IntPtr pointer) : base(pointer) { }
        public void Update() { _controller.Update(); }
        public void OnDestroy() { _controller.Dispose(); }
    }
}
