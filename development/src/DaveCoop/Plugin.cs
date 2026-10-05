using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using UnityEngine.SceneManagement;
using DaveCoop.Rendering;
using DaveCoop.Networking;
using DaveCoop.Discovery;

namespace DaveCoop
{
    [BepInPlugin(Id, Name, Version)]
    [BepInProcess("DaveTheDiver.exe")]
    public sealed class Plugin : BasePlugin
    {
        public const string Id = "local.davecoop.prototype";
        public const string Name = "DaveCoop Prototype";
        public const string Version = "0.1.48-dev";

        public override void Load()
        {
            // This is a CLR Plugin.Load marker, not proof that a Unity/save
            // callback had not already run before BepInEx loaded this plugin.
            int startupLoadThread = Environment.CurrentManagedThreadId;
            double startupLoadAt = (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
            ConfigEntry<bool> temporaryGuest = Config.Bind("Startup", "ExperimentalGuestInitialization", false,
                "Experimental existing-save guest startup. Automatically joins the configured host, holds initialization until the handshake, and retains temporary progress until process exit. Restart to return to personal progress. Not verified for gameplay or complete save isolation.");
            if (temporaryGuest.Value)
                NativeGuestInitializationController.Start(Log, startupLoadThread);
            ConfigEntry<bool> observeSaveStartup = Config.Bind("Startup", "ObserveSaveStartup", false,
                "Observe natural save startup calls with bounded private hash-only diagnostics. Requires process restart; no save/path/cloud changes or guest isolation.");
            if (observeSaveStartup.Value && !temporaryGuest.Value)
                Diagnostics.SaveStartup = SaveStartupCapture.Start(Log, startupLoadThread, startupLoadAt);

            Diagnostics.Logger = Log;
            Diagnostics.ShowOverlay = Config.Bind("Diagnostics", "ShowOverlay", true,
                "Show the development status panel. Press F8 to hide/show it.");
            PlayerProbe.Logger = Log;
            PlayerProbe.Enabled = Config.Bind("Discovery", "EnablePlayerProbe", true,
                "Read player/camera state on the Unity thread; write bounded discovery logs. F9 captures a snapshot.");
            PlayerProbe.MaxSnapshots = Config.Bind("Discovery", "MaxSnapshots", 9000,
                "Maximum snapshots per game launch, clamped to 1..18000 (two samples per second).");
            WorldProbe.Logger = Log;
            WorldProbe.Enabled = Config.Bind("Discovery", "EnableWorldProbe", false,
                "F7 toggles read-only map selection, fish and item observations; no game-state writes.");
            WorldProbe.MaxSnapshots = Config.Bind("Discovery", "MaxWorldSnapshots", 900,
                "Maximum world snapshots per launch, clamped to 1..1800; also bounded to 32 MiB.");
            RemotePreview.Logger = Log;
            RemotePreview.Enabled = Config.Bind("Preview", "Enabled", true,
                "Display a sprite-only delayed local replay actor for M2 testing. F10 toggles it.");
            RemotePreview.Delay = Config.Bind("Preview", "DelaySeconds", 1f,
                "Visual replay delay in seconds, clamped to 0.1..2.5.");
            RemotePreview.OffsetX = Config.Bind("Preview", "OffsetX", 3f,
                "Horizontal replay offset in world units, clamped to -10..10.");
            NetworkDriver.Logger = Log;
            NetworkDriver.ShowPanel = Config.Bind("Network", "ShowPanel", false, "F11 opens the LAN movement prototype panel.");
            NetworkDriver.HostAddress = Config.Bind("Network", "HostAddress", "127.0.0.1", "LAN IPv4 of the host.");
            NetworkDriver.Port = Config.Bind("Network", "Port", 27182, "TCP listen/connect port.");
            NetworkDriver.PlayerName = Config.Bind("Network", "PlayerName", "Dave", "Room display name, up to 32 characters.");
            NetworkDriver.RenderDelay = Config.Bind("Network", "RenderDelaySeconds", 0.12f, "Remote interpolation delay, clamped to 0.05..0.5 seconds.");
            NetworkDriver.TransmitFishObservations = Config.Bind("Network", "TransmitFishObservations", false,
                "Send bounded read-only initialized fish state from the host; diagnostic only, does not control fish.");
            NetworkDriver.ShowFishPreview = Config.Bind("Network", "ShowFishPreview", false,
                "Preview one received fish with sprite/Spine display only; no AI, collision, interaction or original-fish changes.");
            NetworkDriver.ShowFishWorld = Config.Bind("Network", "ShowFishWorld", false,
                "Display the received active fish observation roster; display only, no original AI or capture changes.");
            NetworkDriver.ObserveFishInteractions = Config.Bind("Network", "ObserveFishInteractions", false,
                "Observe native host harpoon, damage and pickup calls while transmitting fish observations; bounded read-only diagnostics.");
            NetworkDriver.ObserveMapSelectionCalls = Config.Bind("Network", "ObserveMapSelectionCalls", false,
                "Freeze route and original IGP choices at native call boundaries on the Unity thread; bounded read-only diagnostics, no map/save writes.");
            NetworkDriver.ObserveLootCalls = Config.Bind("Network", "ObserveLootCalls", false,
                "Observe natural loot, bag, caught-fish and return-storage calls on the Unity thread; bounded read-only diagnostics, no rewards or bag/save writes.");
            NetworkDriver.ObserveMapOrigins = Config.Bind("Network", "ObserveMapOrigins", false,
                "Track natural loading coroutine ownership and exact resource-operation scene results on the Unity thread; bounded read-only diagnostics, no map adoption or game/save writes.");
            NetworkDriver.ExperimentalHostFishAreas = Config.Bind("Network", "ExperimentalHostFishAreas", false,
                "Experimental same-scene fish spawning and activity around both members. With CrewActor, uses the host-controlled employee body's actual position and stops that region when the body is unavailable; otherwise uses read-only guest observations. Fixed at first network Update. Native behavior and distant gameplay remain unverified.");
            NetworkDriver.ExperimentalCrewActor = Config.Bind("Network", "ExperimentalCrewActor", false,
                "Experimental host-owned employee physics/input/state. Fixed at first network Update; both peers must opt in. Guest requires ExperimentalGuestInitialization. Native collision, camera correction and independent survival remain unverified. Harpoon and personal cargo each have a separate default-off switch.");
            NetworkDriver.ExperimentalCrewHarpoon = Config.Bind("Network", "ExperimentalCrewHarpoon", false,
                "Experimental host-owned employee Mod harpoon; fixed at first network Update. Requires CrewActor on both peers and actual host fish observation/lifecycle sources. Native projectile collision and damage ABI remain unverified; no capture or cargo receipt.");
            NetworkDriver.ExperimentalCrewCargo = Config.Bind("Network", "ExperimentalCrewCargo", false,
                "Experimental personal employee bag and ordinary downed-fish Harvest. Host observes a natural dive and original bag first; Space is consumed as a pickup edge. Native capture, progression, return and save remain unverified. Fixed at first network Update.");
            NetworkDriver.CrewAllowOverweight = Config.Bind("Crew", "AllowOverweight", true,
                "Host-approved personal capacity policy. False rejects the entire batch beyond employee capacity; true permits personal overload with a Mod speed factor down to 0.25. Host bag capacity is never borrowed.");
            NetworkDriver.HarpoonSpeed = Config.Bind("CrewHarpoon", "Speed", 12f, "Host-approved Mod projectile speed, fixed on startup.");
            NetworkDriver.HarpoonRange = Config.Bind("CrewHarpoon", "Range", 8f, "Host-approved Mod maximum projectile travel distance.");
            NetworkDriver.HarpoonCooldown = Config.Bind("CrewHarpoon", "CooldownSeconds", 0.5f, "Host-approved Mod delay between fire edges; blocked fire is consumed.");
            NetworkDriver.HarpoonRadius = Config.Bind("CrewHarpoon", "Radius", 0.08f, "Host-approved Mod swept circle radius.");
            NetworkDriver.HarpoonDamage = Config.Bind("CrewHarpoon", "Damage", 10, "Host-approved fixed Mod base damage for the narrow ordinary-fish pipeline; client never supplies damage.");
            NetworkDriver.CrewSpeed = Config.Bind("Crew", "Speed", 3f, "Host-approved Mod swim speed, greater than 0 and at most 20; fixed on startup.");
            NetworkDriver.CrewBoostMultiplier = Config.Bind("Crew", "BoostMultiplier", 1.5f, "Host-approved Mod boost multiplier, 1..3.");
            NetworkDriver.CrewMaxHP = Config.Bind("Crew", "MaxHP", 100f, "Host-approved employee Mod maximum HP; native damage is not connected.");
            NetworkDriver.CrewMaxOxygen = Config.Bind("Crew", "MaxOxygen", 100f, "Host-approved employee Mod maximum oxygen.");
            NetworkDriver.CrewOxygenPerSecond = Config.Bind("Crew", "OxygenPerSecond", 1f, "Employee Mod oxygen drain per simulated second, 0..1000.");
            NetworkDriver.CrewBoostOxygenPerSecond = Config.Bind("Crew", "BoostOxygenPerSecond", 2f, "Additional employee Mod oxygen drain while boosting, 0..1000.");
            NetworkDriver.CrewCapacityKg = Config.Bind("Crew", "CapacityKg", 20f, "Employee approved Mod capacity, fixed on startup. ExperimentalCrewCargo binds this personal ledger capacity independently of the host's native bag.");
            AddComponent<Diagnostics>();
            AddComponent<PlayerProbe>();
            AddComponent<RemotePreview>();
            AddComponent<NetworkDriver>();
            AddComponent<WorldProbe>();
            Log.LogInfo($"DAVECOOP_BOOTSTRAP_OK: {Name} {Version}; Unity {Application.unityVersion}");
        }
    }

    // BepInEx registers this component with the IL2CPP runtime and keeps it alive across scenes.
    public sealed class Diagnostics : MonoBehaviour
    {
        internal static ManualLogSource Logger;
        internal static ConfigEntry<bool> ShowOverlay;
        internal static SaveStartupCapture SaveStartup;
        private string _scene = "starting";
        private float _nextSceneCheck;
        private bool _sceneProbeFailed;
        private bool _firstFrameLogged;

        public Diagnostics(IntPtr pointer) : base(pointer) { }

        public void Update()
        {
            // The read-only startup observer confirms its own actual Update.
            // Experimental guest startup is confirmed by NetworkDriver.Update
            // before it consumes an asynchronous join result.
            SaveStartup?.Update();
            if (!_firstFrameLogged)
            {
                _firstFrameLogged = true;
                Logger.LogInfo("DAVECOOP_UPDATE_OK: Unity is calling the diagnostic component.");
            }

            if (Input.GetKeyDown(KeyCode.F8))
                ShowOverlay.Value = !ShowOverlay.Value;

            if (_sceneProbeFailed || Time.unscaledTime < _nextSceneCheck)
                return;

            _nextSceneCheck = Time.unscaledTime + 1f;
            try
            {
                string nextScene = SceneManager.GetActiveScene().name;
                if (nextScene != _scene)
                {
                    _scene = nextScene;
                    Logger.LogInfo($"DAVECOOP_SCENE: {_scene}");
                }
            }
            catch (Exception error)
            {
                _sceneProbeFailed = true;
                _scene = "unavailable";
                Logger.LogWarning($"Scene diagnostics disabled: {error.Message}");
            }
        }

        public void OnApplicationQuit()
        {
            // Dispose only this observer's Harmony owner. Keep its reference
            // when cleanup is unverified; never claim all hooks were removed.
            SaveStartup?.Dispose();
        }

        public void OnGUI()
        {
            if (ShowOverlay == null || !ShowOverlay.Value)
                return;

            var startupGuest = NativeGuestInitializationController.Current;
            GUI.Box(new Rect(12, 12, 640, startupGuest == null ? 145 : 190),
                $"DaveCoop Prototype {Plugin.Version}\nPlugin loaded | Scene: {_scene}\n{PlayerProbe.Status}\n{RemotePreview.Status}\n{NetworkDriver.Status}\nF7: world probe | F8: panel | F9: snapshot | F10: replay | F11: room" +
                (startupGuest == null ? "" : "\nGuest startup: " + startupGuest.Status));
        }
    }
}
