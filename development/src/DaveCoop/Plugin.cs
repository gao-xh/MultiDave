using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using UnityEngine.SceneManagement;
using DaveCoop.Rendering;

namespace DaveCoop
{
    [BepInPlugin(Id, Name, Version)]
    [BepInProcess("DaveTheDiver.exe")]
    public sealed class Plugin : BasePlugin
    {
        public const string Id = "local.davecoop.prototype";
        public const string Name = "DaveCoop Prototype";
        public const string Version = "0.1.2-dev";

        public override void Load()
        {
            Diagnostics.Logger = Log;
            Diagnostics.ShowOverlay = Config.Bind("Diagnostics", "ShowOverlay", true,
                "Show the development status panel. Press F8 to hide/show it.");
            PlayerProbe.Logger = Log;
            PlayerProbe.Enabled = Config.Bind("Discovery", "EnablePlayerProbe", true,
                "Read player/camera state on the Unity thread; write bounded discovery logs. F9 captures a snapshot.");
            PlayerProbe.MaxSnapshots = Config.Bind("Discovery", "MaxSnapshots", 9000,
                "Maximum snapshots per game launch, clamped to 1..18000 (two samples per second).");
            RemotePreview.Logger = Log;
            RemotePreview.Enabled = Config.Bind("Preview", "Enabled", true,
                "Display a sprite-only delayed local replay actor for M2 testing. F10 toggles it.");
            RemotePreview.Delay = Config.Bind("Preview", "DelaySeconds", 1f,
                "Visual replay delay in seconds, clamped to 0.1..2.5.");
            RemotePreview.OffsetX = Config.Bind("Preview", "OffsetX", 3f,
                "Horizontal replay offset in world units, clamped to -10..10.");
            AddComponent<Diagnostics>();
            AddComponent<PlayerProbe>();
            AddComponent<RemotePreview>();
            Log.LogInfo($"DAVECOOP_BOOTSTRAP_OK: {Name} {Version}; Unity {Application.unityVersion}");
        }
    }

    // BepInEx registers this component with the IL2CPP runtime and keeps it alive across scenes.
    public sealed class Diagnostics : MonoBehaviour
    {
        internal static ManualLogSource Logger;
        internal static ConfigEntry<bool> ShowOverlay;
        private string _scene = "starting";
        private float _nextSceneCheck;
        private bool _sceneProbeFailed;
        private bool _firstFrameLogged;

        public Diagnostics(IntPtr pointer) : base(pointer) { }

        public void Update()
        {
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

        public void OnGUI()
        {
            if (ShowOverlay == null || !ShowOverlay.Value)
                return;

            GUI.Box(new Rect(12, 12, 520, 130),
                $"DaveCoop Prototype {Plugin.Version}\nPlugin loaded | Scene: {_scene}\n{PlayerProbe.Status}\n{RemotePreview.Status}\nF8: panel | F9: snapshot | F10: replay | Network: not implemented");
        }
    }
}
