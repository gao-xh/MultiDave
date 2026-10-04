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
        internal static ConfigEntry<bool> ObserveLootCalls;
        internal static ConfigEntry<bool> ObserveMapOrigins;
        internal static ConfigEntry<bool> ExperimentalHostFishAreas;
        internal static ConfigEntry<bool> ExperimentalCrewActor;
        internal static ConfigEntry<bool> ExperimentalCrewHarpoon;
        internal static ConfigEntry<bool> ExperimentalCrewCargo, CrewAllowOverweight;
        internal static ConfigEntry<float> CrewSpeed, CrewBoostMultiplier, CrewMaxHP, CrewMaxOxygen;
        internal static ConfigEntry<float> CrewOxygenPerSecond, CrewBoostOxygenPerSecond, CrewCapacityKg;
        internal static ConfigEntry<float> HarpoonSpeed, HarpoonRange, HarpoonCooldown, HarpoonRadius;
        internal static ConfigEntry<int> HarpoonDamage;
        internal static string Status = "Network: offline (F11)";
        private readonly NetworkController _controller;

        public NetworkDriver(IntPtr pointer) : base(pointer) { _controller = new NetworkController(); }
        public void Update() { _controller.Update(); }
        public void FixedUpdate() { _controller.FixedUpdate(); }
        public void LateUpdate() { _controller.LateUpdate(); }
        public void OnGUI() { _controller.Draw(); }
        public void OnDestroy() { _controller.Dispose(); }
    }
}
