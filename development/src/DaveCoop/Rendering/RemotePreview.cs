using System;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace DaveCoop.Rendering
{
    // Keep Unity callbacks thin; managed helpers are not exported to IL2CPP.
    public sealed class RemotePreview : MonoBehaviour
    {
        internal static ManualLogSource Logger;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Delay;
        internal static ConfigEntry<float> OffsetX;
        internal static bool NetworkActive;
        internal static string Status = "Second actor: starting";
        private readonly RemotePreviewController _controller;

        public RemotePreview(IntPtr pointer) : base(pointer)
        {
            _controller = new RemotePreviewController();
        }

        public void Update() { _controller.Update(); }
        public void LateUpdate() { _controller.LateUpdate(); }
        public void OnDestroy() { _controller.Dispose(); }
    }
}
