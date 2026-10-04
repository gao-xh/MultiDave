using System;
using System.Collections.Generic;

namespace DaveCoop.Core.World
{
    public enum FishLifecycleSignal { Enable, Disable, Destroy }

    // Tracks only fish already observed by the host. Native callbacks never
    // allocate entries. Local pointers and generations do not enter the wire.
    public sealed class FishLifecycleTracker
    {
        private sealed class Entry { public long Generation; public bool Active; public bool Destroyed; }
        private readonly object _gate = new object();
        private readonly Dictionary<long, Entry> _entries = new Dictionary<long, Entry>();
        private long _nextGeneration;
        private long _transitions;
        public int Count { get { lock (_gate) return _entries.Count; } }
        public long Transitions { get { lock (_gate) return _transitions; } }

        public bool TryGetActiveGeneration(long pointer, out long generation)
        {
            generation = 0;
            lock (_gate)
            {
                if (!_entries.TryGetValue(pointer, out Entry entry) || !entry.Active || entry.Destroyed) return false;
                generation = entry.Generation; return true;
            }
        }

        public long ObserveActive(long pointer)
        {
            if (pointer == 0) throw new ArgumentException("Missing local fish pointer.");
            lock (_gate)
            {
                if (!_entries.TryGetValue(pointer, out Entry entry))
                {
                    if (_entries.Count == WorldFrames.MaxEntities) throw new InvalidOperationException("Fish lifecycle capacity exceeded.");
                    entry = new Entry { Generation = Next(), Active = true }; _entries.Add(pointer, entry);
                }
                else if (!entry.Active || entry.Destroyed)
                {
                    entry.Generation = Next(); entry.Active = true; entry.Destroyed = false;
                }
                return entry.Generation;
            }
        }

        public bool Signal(long pointer, FishLifecycleSignal signal)
        {
            if (!Enum.IsDefined(typeof(FishLifecycleSignal), signal)) throw new ArgumentException("Unknown fish lifecycle signal.");
            lock (_gate)
            {
                if (!_entries.TryGetValue(pointer, out Entry entry)) return false;
                switch (signal)
                {
                    case FishLifecycleSignal.Enable:
                        if (entry.Active && !entry.Destroyed) return false;
                        entry.Generation = Next(); entry.Active = true; entry.Destroyed = false; break;
                    case FishLifecycleSignal.Disable:
                        if (!entry.Active) return false;
                        entry.Active = false; break;
                    case FishLifecycleSignal.Destroy:
                        if (entry.Destroyed) return false;
                        entry.Active = false; entry.Destroyed = true; break;
                }
                if (_transitions < long.MaxValue) _transitions++;
                return true;
            }
        }

        public void Retain(ISet<long> livePointers)
        {
            if (livePointers == null) throw new ArgumentNullException(nameof(livePointers));
            lock (_gate)
            {
                var gone = new List<long>();
                foreach (long pointer in _entries.Keys) if (!livePointers.Contains(pointer)) gone.Add(pointer);
                foreach (long pointer in gone) _entries.Remove(pointer);
            }
        }

        // Preserve generation monotonicity across diagnostic toggles/scenes.
        public void Clear() { lock (_gate) { _entries.Clear(); _transitions = 0; } }
        private long Next()
        {
            if (_nextGeneration == long.MaxValue) throw new InvalidOperationException("Fish generation exhausted.");
            return ++_nextGeneration;
        }
    }
}
