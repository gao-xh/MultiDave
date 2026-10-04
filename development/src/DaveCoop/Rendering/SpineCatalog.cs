using System;
using System.Collections.Generic;
using DaveCoop.Core.Assets;
using DaveCoop.Core.World;
using Spine.Unity;
using UnityEngine;

namespace DaveCoop.Rendering
{
    internal sealed class SpineCatalog
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private readonly AssetRegistry<SkeletonDataAsset> _assets = new AssetRegistry<SkeletonDataAsset>(2048);
        private readonly Dictionary<int, string> _keys = new Dictionary<int, string>();
        private float _nextScan;

        public string Register(SkeletonDataAsset asset)
        {
            CheckThread(); if (asset == null) return null;
            int id = asset.GetInstanceID();
            if (_keys.TryGetValue(id, out string cached)) return _assets.TryResolve(cached, out _) ? cached : null;
            if (_keys.Count >= 2048 || string.IsNullOrWhiteSpace(asset.name) || asset.atlasAssets == null || asset.atlasAssets.Length > 32) return null;
            var hash = new CanonicalHash("spine-asset-v1").Add(asset.name).Add(asset.scale).Add(asset.atlasAssets.Length);
            foreach (AtlasAssetBase atlas in asset.atlasAssets)
            {
                if (atlas == null || string.IsNullOrWhiteSpace(atlas.name)) return null;
                hash.Add(atlas.name);
            }
            string key = "spine-v1:" + hash.Finish(); _keys.Add(id, key);
            AssetRegistration result = _assets.Register(key, id, asset);
            return result == AssetRegistration.Ambiguous || result == AssetRegistration.Full ? null : key;
        }

        public bool TryResolve(string key, out SkeletonDataAsset asset)
        {
            CheckThread(); return _assets.TryResolve(key, out asset) && asset != null;
        }

        public void Scan(float now)
        {
            CheckThread(); if (now < _nextScan) return; _nextScan = now + 2f;
            foreach (SkeletonDataAsset asset in Resources.FindObjectsOfTypeAll<SkeletonDataAsset>())
            {
                if (_keys.Count >= 2048) break;
                try { Register(asset); } catch (ArgumentException) { }
            }
        }

        public void Clear() { CheckThread(); _assets.Clear(); _keys.Clear(); _nextScan = 0; }
        private void CheckThread()
        {
            if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Spine catalog must run on its Unity thread.");
        }
    }
}
