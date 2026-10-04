using System;
using System.Collections.Generic;

namespace DaveCoop.Core.Assets
{
    public enum AssetRegistration { Added, Existing, Ambiguous, Full }

    // Local identities detect metadata collisions; they are never sent over the wire.
    public sealed class AssetRegistry<T> where T : class
    {
        private sealed class Entry
        {
            public long LocalIdentity;
            public T Value;
            public bool Ambiguous;
        }

        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly int _capacity;
        public int Count => _entries.Count;

        public AssetRegistry(int capacity = 16384)
        {
            if (capacity < 1 || capacity > 131072) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
        }

        public AssetRegistration Register(string key, long localIdentity, T value)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 512 || value == null)
                throw new ArgumentException("Invalid asset registration.");
            if (_entries.TryGetValue(key, out Entry existing))
            {
                if (existing.Ambiguous) return AssetRegistration.Ambiguous;
                if (existing.LocalIdentity == localIdentity) return AssetRegistration.Existing;
                existing.Ambiguous = true; existing.Value = null;
                return AssetRegistration.Ambiguous;
            }
            if (_entries.Count == _capacity) return AssetRegistration.Full;
            _entries.Add(key, new Entry { LocalIdentity = localIdentity, Value = value });
            return AssetRegistration.Added;
        }

        public bool TryResolve(string key, out T value)
        {
            value = null;
            if (key == null || !_entries.TryGetValue(key, out Entry entry) || entry.Ambiguous) return false;
            value = entry.Value;
            return true;
        }

        public void Clear() => _entries.Clear();
    }
}
