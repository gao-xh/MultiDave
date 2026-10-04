using System;

namespace DaveCoop.Core
{
    // Fixed capacity, monotonic timestamps, no extrapolation. Clear on owner/scene changes.
    public sealed class SnapshotTimeline<T>
    {
        private readonly T[] _items;
        private readonly double[] _times;
        private int _start;
        public int Count { get; private set; }

        public SnapshotTimeline(int capacity)
        {
            if (capacity < 2 || capacity > 1024) throw new ArgumentOutOfRangeException(nameof(capacity));
            _items = new T[capacity];
            _times = new double[capacity];
        }

        public bool TryPush(double time, T item)
        {
            if (!double.IsFinite(time) || (Count > 0 && time <= _times[Index(Count - 1)])) return false;
            int index;
            if (Count == _items.Length)
            {
                index = _start;
                _start = (_start + 1) % _items.Length;
            }
            else { index = Index(Count); Count++; }
            _times[index] = time;
            _items[index] = item;
            return true;
        }

        public bool TrySample(double time, out T from, out T to, out float amount)
        {
            from = default(T); to = default(T); amount = 0f;
            if (Count == 0 || !double.IsFinite(time)) return false;
            if (time <= _times[_start])
            {
                from = to = _items[_start];
                return true;
            }
            for (int i = 1; i < Count; i++)
            {
                int upper = Index(i);
                if (time > _times[upper]) continue;
                int lower = Index(i - 1);
                from = _items[lower]; to = _items[upper];
                amount = (float)((time - _times[lower]) / (_times[upper] - _times[lower]));
                return true;
            }
            from = to = _items[Index(Count - 1)];
            return true;
        }

        public void Clear()
        {
            Array.Clear(_items, 0, _items.Length);
            Array.Clear(_times, 0, _times.Length);
            _start = 0; Count = 0;
        }

        private int Index(int index) => (_start + index) % _items.Length;
    }
}
