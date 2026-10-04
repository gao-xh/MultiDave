using System;
using System.Collections.Generic;
using System.Threading;
using DaveCoop.Core.Assets;
using UnityEngine;

namespace DaveCoop.Rendering
{
    // Call only from the Unity thread, and clear on scene changes. It owns no sprites.
    // This adapter is compiled but not yet attached to the network renderer.
    internal sealed class SpriteCatalog
    {
        private const int Capacity = 16384;
        private readonly int _ownerThread = Environment.CurrentManagedThreadId;
        private readonly AssetRegistry<Sprite> _registry = new AssetRegistry<Sprite>(Capacity);
        private readonly Dictionary<int, string> _byInstance = new Dictionary<int, string>();
        private float _nextScan;

        public string Register(Sprite sprite)
        {
            CheckThread();
            if (sprite == null) return null;
            int id = sprite.GetInstanceID();
            if (_byInstance.TryGetValue(id, out string cached)) return _registry.TryResolve(cached, out _) ? cached : null;
            if (_byInstance.Count >= Capacity) return null;
            Texture2D texture = sprite.texture;
            if (texture == null) return null;
            Rect rect = sprite.rect; Vector2 pivot = sprite.pivot; Vector4 border = sprite.border;
            string key = SpriteKey.Create(new SpriteDescriptor
            {
                TextureName = texture.name, SpriteName = sprite.name, TextureWidth = texture.width, TextureHeight = texture.height,
                Rect = new System.Numerics.Vector4(rect.x, rect.y, rect.width, rect.height),
                Pivot = new System.Numerics.Vector2(pivot.x, pivot.y),
                Border = new System.Numerics.Vector4(border.x, border.y, border.z, border.w), PixelsPerUnit = sprite.pixelsPerUnit
            });
            _byInstance.Add(id, key);
            AssetRegistration registration = _registry.Register(key, id, sprite);
            return registration == AssetRegistration.Ambiguous || registration == AssetRegistration.Full ? null : key;
        }

        public bool TryResolve(string key, out Sprite sprite)
        {
            CheckThread();
            return _registry.TryResolve(key, out sprite) && sprite != null;
        }

        public void ScanLoadedSprites(float now)
        {
            CheckThread();
            if (now < _nextScan) return;
            _nextScan = now + 2;
            var sprites = Resources.FindObjectsOfTypeAll<Sprite>();
            foreach (Sprite sprite in sprites)
            {
                if (_byInstance.Count >= Capacity) break;
                // Unsupported runtime-generated descriptors should not prevent
                // cataloguing other sprites; unresolved keys remain hidden.
                try { Register(sprite); } catch (ArgumentException) { }
            }
        }

        public void Clear()
        {
            CheckThread(); _registry.Clear(); _byInstance.Clear(); _nextScan = 0;
        }

        private void CheckThread()
        {
            if (Environment.CurrentManagedThreadId != _ownerThread)
                throw new InvalidOperationException("Sprite catalog must run on its owning Unity thread.");
        }
    }
}
