using System;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace DaveCoop.Networking
{
    public sealed class NetworkDriver : MonoBehaviour
    {
        internal static ManualLogSource Logger;
        internal static ConfigEntry<bool> ShowPanel;
        internal static ConfigEntry<string> HostAddress;
        internal static ConfigEntry<int> Port;
        internal static ConfigEntry<string> PlayerName;
        internal static ConfigEntry<float> RenderDelay;
        internal static ConfigEntry<bool> TransmitFishObservations;
        internal static ConfigEntry<bool> ShowFishPreview;
        internal static ConfigEntry<bool> ShowFishWorld;
        internal static ConfigEntry<bool> ObserveFishInteractions;
        internal static ConfigEntry<bool> ObserveMapSelectionCalls;
        internal static string Status = "Network: offline (F11)";
        private readonly NetworkController _controller;

        public NetworkDriver(IntPtr pointer) : base(pointer) { _controller = new NetworkController(); }
        public void Update() { _controller.Update(); }
        public void LateUpdate() { _controller.LateUpdate(); }
        public void OnGUI() { _controller.Draw(); }
        public void OnDestroy() { _controller.Dispose(); }
    }
}
