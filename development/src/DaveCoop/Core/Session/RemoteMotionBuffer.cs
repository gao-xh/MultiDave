using System;
using DaveCoop.Core.Protocol;

namespace DaveCoop.Core.Session
{
    public sealed class RemoteMotionBuffer
    {
        private readonly SnapshotTimeline<PlayerFrame> _history;
        private long _epoch;
        private string _scene;
        private double _remoteOrigin;
        private double _localOrigin;
        private double _lastSample = -1;
        private double _lastReceipt;
        public int Count => _history.Count;
        public double StaleSeconds { get; }

        public RemoteMotionBuffer(int capacity = 120, double staleSeconds = 1)
        {
            if (!double.IsFinite(staleSeconds) || staleSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(staleSeconds));
            _history = new SnapshotTimeline<PlayerFrame>(capacity); StaleSeconds = staleSeconds;
        }

        public bool TryPush(ReceivedFrame received)
        {
            if (received == null || !double.IsFinite(received.ReceivedAt) || received.ReceivedAt < 0 ||
                !double.IsFinite(received.LocalSampleTime)) throw new ArgumentException("Invalid receipt clock.");
            PlayerFrame frame = received.Frame;
            PacketCodec.ValidateFrame(frame);
            if (frame.SceneEpoch < _epoch) return false;
            if (frame.SceneEpoch > _epoch) Clear();
            if (_history.Count == 0)
            {
                _epoch = frame.SceneEpoch; _scene = frame.SceneKey;
                _remoteOrigin = frame.SampleTime; _localOrigin = received.LocalSampleTime;
            }
            else if (frame.SceneKey != _scene) throw new ProtocolException("Remote motion scene identity changed without epoch.");
            if (frame.SampleTime <= _lastSample || (_history.Count > 0 && received.ReceivedAt < _lastReceipt)) return false;
            // Anchor once per scene. Later clock estimates must not reorder buffered
            // samples when the measured network delay changes.
            double localTime = _localOrigin + frame.SampleTime - _remoteOrigin;
            if (!_history.TryPush(localTime, frame)) return false;
            _lastSample = frame.SampleTime; _lastReceipt = received.ReceivedAt;
            return true;
        }

        public bool TrySample(double now, double delay, out PlayerFrame from, out PlayerFrame to, out float alpha)
        {
            from = null; to = null; alpha = 0;
            if (!double.IsFinite(now) || !double.IsFinite(delay) || delay < 0) throw new ArgumentException("Invalid motion sample clock.");
            if (_history.Count == 0 || now - _lastReceipt > StaleSeconds) return false;
            return _history.TrySample(now - delay, out from, out to, out alpha);
        }

        public void Clear()
        {
            _history.Clear(); _epoch = 0; _scene = null; _lastSample = -1; _lastReceipt = 0;
        }
    }
}
