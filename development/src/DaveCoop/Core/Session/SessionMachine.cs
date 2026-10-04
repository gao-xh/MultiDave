using System;
using System.Collections.Generic;
using DaveCoop.Core.Protocol;

namespace DaveCoop.Core.Session
{
    // Pure CLR state machine. SessionPeer serializes access; it never calls Unity.
    public sealed class SessionMachine
    {
        private const int MaxControls = 32;
        private const int MaxEvents = 64;
        private readonly SessionRole _role;
        private readonly HandshakeResult _identity;
        private readonly SessionOptions _options;
        private readonly Queue<WirePacket> _controls = new Queue<WirePacket>();
        private readonly Queue<SessionEvent> _events = new Queue<SessionEvent>();
        private SessionPhase _phase;
        private string _reason;
        private SceneDescriptor _localScene;
        private SceneNotice _proposal;
        private long _epoch;
        private bool _suspended;
        private bool _ackSent;
        private double _sceneStarted;
        private double _now;
        private double _lastReceive;
        private double _nextPing;
        private long _pingId;
        private long _pendingPing;
        private double _pingSent;
        private bool _hasClock;
        private double _offset;
        private double _rtt;
        private double _lastFrameTime = -1;
        private double _lastLocalFrameTime = -1;
        private WirePacket _outgoingFrame;
        private ReceivedFrame _incomingFrame;

        public SessionMachine(SessionRole role, HandshakeResult identity, double now, SessionOptions options = null)
        {
            if (identity == null || !Enum.IsDefined(typeof(SessionRole), role) ||
                identity.LocalPlayerId != (role == SessionRole.Host ? 1 : 2) ||
                identity.RemotePlayerId != (role == SessionRole.Host ? 2 : 1))
                throw new ArgumentException("Handshake identities do not match the session role.");
            PacketCodec.RequireRoom(new WirePacket { RoomId = identity.RoomId }, identity.RoomId);
            _identity = new HandshakeResult { RoomId = identity.RoomId, LocalPlayerId = identity.LocalPlayerId, RemotePlayerId = identity.RemotePlayerId };
            _role = role; _options = (options ?? new SessionOptions()).CopyValidated();
            CheckTime(now); _lastReceive = now; _nextPing = now;
            ChangePhase(SessionPhase.WaitingForScene, "Connected; waiting for local scene.");
        }

        public SessionSnapshot Snapshot => new SessionSnapshot
        {
            Role = _role, Phase = _phase, RoomId = _identity.RoomId, Reason = _reason,
            LocalPlayerId = _identity.LocalPlayerId, RemotePlayerId = _identity.RemotePlayerId,
            SceneEpoch = _epoch, SceneKey = _proposal?.SceneKey, HasClockEstimate = _hasClock,
            RemoteClockOffsetSeconds = _offset, RoundTripSeconds = _rtt
        };

        public void SetLocalScene(SceneDescriptor scene, double now)
        {
            CheckTime(now);
            if (_phase == SessionPhase.Closed) return;
            if ((scene == null && _localScene == null) || (scene?.SameAs(_localScene) ?? false)) return;
            _localScene = scene;
            ClearFrames();
            if (_role == SessionRole.Host) OfferScene(now);
            else
            {
                if (_ackSent && _proposal != null)
                    Queue(new WirePacket { Kind = PacketKind.ScenePause, Scene = CopyScene(_proposal) });
                _ackSent = false;
                ChangePhase(SessionPhase.WaitingForScene, "Local scene changed; waiting for matching host scene.");
                TryAck();
            }
        }

        public bool PublishFrame(PlayerFrame frame, double now)
        {
            CheckTime(now);
            if (_phase != SessionPhase.Ready) return false;
            if (frame == null || frame.SceneKey != _proposal.SceneKey || frame.SampleTime > now + 1)
                throw new ProtocolException("Local frame does not match the active scene or clock.");
            PlayerFrame copy = CopyFrame(frame);
            copy.PlayerId = _identity.LocalPlayerId; copy.SceneEpoch = _epoch;
            PacketCodec.ValidateFrame(copy);
            if (copy.SampleTime <= _lastLocalFrameTime) return false;
            _lastLocalFrameTime = copy.SampleTime;
            // Coalesce an unsent frame instead of retaining an unbounded send backlog.
            _outgoingFrame = new WirePacket { Kind = PacketKind.PlayerFrame, RoomId = _identity.RoomId, Frame = copy };
            return true;
        }

        public void Receive(WirePacket packet, double now)
        {
            CheckTime(now);
            if (_phase == SessionPhase.Closed) return;
            PacketCodec.Validate(packet);
            PacketCodec.RequireRoom(packet, _identity.RoomId);
            _lastReceive = now;
            switch (packet.Kind)
            {
                case PacketKind.Ping:
                    Queue(new WirePacket { Kind = PacketKind.Pong, Clock = new ClockMessage { Id = packet.Clock.Id, Time = now } });
                    break;
                case PacketKind.Pong:
                    if (packet.Clock.Id != _pendingPing) throw new ProtocolException("Unsolicited or duplicate clock reply.");
                    double rtt = now - _pingSent;
                    double offset = packet.Clock.Time - (_pingSent + now) * 0.5;
                    if (!_hasClock || rtt <= _rtt * 1.5)
                    {
                        _offset = _hasClock ? _offset * 0.8 + offset * 0.2 : offset;
                        _rtt = _hasClock ? _rtt * 0.8 + rtt * 0.2 : rtt;
                        _hasClock = true;
                    }
                    _pendingPing = 0;
                    break;
                case PacketKind.SceneChange:
                case PacketKind.SceneSuspend:
                    RequireRole(SessionRole.Guest);
                    if (packet.Scene.Epoch <= _epoch) throw new ProtocolException("Scene epoch replay.");
                    _epoch = packet.Scene.Epoch; _proposal = CopyScene(packet.Scene);
                    _suspended = packet.Kind == PacketKind.SceneSuspend;
                    _ackSent = false; _sceneStarted = now; ClearFrames();
                    ChangePhase(SessionPhase.WaitingForScene, _suspended ? "Host is changing scenes." : "Waiting for matching local world.");
                    TryAck();
                    break;
                case PacketKind.SceneAck:
                    RequireRole(SessionRole.Host);
                    if (packet.Scene.Epoch < _epoch) break;
                    RequireCurrentScene(packet.Scene);
                    if (_suspended || _localScene == null) throw new ProtocolException("Cannot acknowledge a suspended scene.");
                    if (_phase == SessionPhase.Ready) break;
                    Queue(new WirePacket { Kind = PacketKind.SceneCommit, Scene = CopyScene(_proposal) });
                    ChangePhase(SessionPhase.Ready, "Both peers acknowledged the same scene and world fingerprint.");
                    break;
                case PacketKind.SceneCommit:
                    RequireRole(SessionRole.Guest);
                    if (packet.Scene.Epoch < _epoch) break;
                    RequireCurrentScene(packet.Scene);
                    if (!_ackSent || _suspended || !(_localScene?.Matches(_proposal) ?? false))
                        throw new ProtocolException("Scene committed before local acknowledgement.");
                    ChangePhase(SessionPhase.Ready, "Host committed the acknowledged scene.");
                    break;
                case PacketKind.ScenePause:
                    RequireRole(SessionRole.Host);
                    if (packet.Scene.Epoch < _epoch) break;
                    RequireCurrentScene(packet.Scene);
                    OfferScene(now); // New epoch prevents frames from the guest's old player leaking into a reload.
                    break;
                case PacketKind.PlayerFrame:
                    if (packet.Frame.PlayerId != _identity.RemotePlayerId) throw new ProtocolException("Player identity spoofing.");
                    if (packet.Frame.SceneEpoch < _epoch || _phase != SessionPhase.Ready) break;
                    if (packet.Frame.SceneEpoch != _epoch || packet.Frame.SceneKey != _proposal.SceneKey)
                        throw new ProtocolException("Frame does not match committed scene.");
                    if (packet.Frame.SampleTime <= _lastFrameTime) break;
                    _lastFrameTime = packet.Frame.SampleTime;
                    _incomingFrame = new ReceivedFrame
                    {
                        Frame = CopyFrame(packet.Frame), ReceivedAt = now,
                        LocalSampleTime = _hasClock ? packet.Frame.SampleTime - _offset : now
                    };
                    break;
                case PacketKind.Leave:
                    Close("Peer left: " + packet.Reason);
                    break;
                default: throw new ProtocolException("Unexpected packet after handshake.");
            }
        }

        public void Tick(double now)
        {
            CheckTime(now);
            if (_phase == SessionPhase.Closed) return;
            if (now - _lastReceive >= _options.PeerTimeoutSeconds) throw new ProtocolException("Peer heartbeat timed out.");
            if (_proposal != null && !_suspended && _phase != SessionPhase.Ready && now - _sceneStarted >= _options.SceneTimeoutSeconds)
                throw new ProtocolException("Scene acknowledgement timed out; worlds may differ.");
            if (now >= _nextPing && _pendingPing == 0)
            {
                _pendingPing = ++_pingId; _pingSent = now; _nextPing = now + _options.HeartbeatIntervalSeconds;
                Queue(new WirePacket { Kind = PacketKind.Ping, Clock = new ClockMessage { Id = _pendingPing, Time = now } });
            }
            if (_pendingPing != 0 && now - _pingSent >= _options.PeerTimeoutSeconds)
                throw new ProtocolException("Peer clock reply timed out.");
        }

        public bool TryTakePacket(out WirePacket packet)
        {
            if (_controls.Count > 0) { packet = _controls.Dequeue(); return true; }
            packet = _outgoingFrame; _outgoingFrame = null;
            return packet != null;
        }

        public bool TryTakeRemoteFrame(out ReceivedFrame frame)
        {
            frame = _incomingFrame; _incomingFrame = null;
            return frame != null;
        }

        public bool TryTakeEvent(out SessionEvent item)
        {
            item = _events.Count == 0 ? null : _events.Dequeue();
            return item != null;
        }

        public void Close(string reason)
        {
            if (_phase == SessionPhase.Closed) return;
            _controls.Clear(); ClearFrames(); _proposal = null; _localScene = null;
            ChangePhase(SessionPhase.Closed, reason ?? "Connection closed.");
        }

        private void OfferScene(double now)
        {
            _epoch++; _sceneStarted = now; _ackSent = false; _suspended = _localScene == null;
            _proposal = new SceneNotice
            {
                Epoch = _epoch, SceneKey = _localScene?.Key ?? "@unavailable",
                WorldFingerprint = _localScene?.WorldFingerprint ?? "@unavailable"
            };
            ClearFrames();
            Queue(new WirePacket { Kind = _suspended ? PacketKind.SceneSuspend : PacketKind.SceneChange, Scene = CopyScene(_proposal) });
            ChangePhase(_suspended ? SessionPhase.WaitingForScene : SessionPhase.WaitingForPeer,
                _suspended ? "Local scene is unavailable." : "Waiting for guest scene acknowledgement.");
        }

        private void TryAck()
        {
            if (_proposal == null || _suspended || _ackSent || !(_localScene?.Matches(_proposal) ?? false)) return;
            _ackSent = true;
            Queue(new WirePacket { Kind = PacketKind.SceneAck, Scene = CopyScene(_proposal) });
            ChangePhase(SessionPhase.WaitingForPeer, "Local world matches; waiting for host commit.");
        }

        private void ClearFrames()
        {
            _outgoingFrame = null; _incomingFrame = null; _lastFrameTime = -1; _lastLocalFrameTime = -1;
        }

        private void RequireCurrentScene(SceneNotice notice)
        {
            if (_proposal == null || notice.Epoch != _epoch || notice.SceneKey != _proposal.SceneKey ||
                notice.WorldFingerprint != _proposal.WorldFingerprint)
                throw new ProtocolException("Scene acknowledgement identity mismatch.");
        }

        private void RequireRole(SessionRole expected)
        {
            if (_role != expected) throw new ProtocolException("Unexpected scene authority.");
        }

        private void Queue(WirePacket packet)
        {
            if (_controls.Count >= MaxControls) throw new ProtocolException("Control queue overflow.");
            packet.RoomId = _identity.RoomId; _controls.Enqueue(packet);
        }

        private void ChangePhase(SessionPhase phase, string reason)
        {
            _phase = phase; _reason = reason;
            if (_events.Count == MaxEvents) _events.Dequeue();
            _events.Enqueue(new SessionEvent { Code = phase.ToString(), Message = reason, Phase = phase });
        }

        private void CheckTime(double now)
        {
            if (!double.IsFinite(now) || now < _now) throw new ArgumentException("Expected a monotonic finite clock.");
            _now = now;
        }

        private static SceneNotice CopyScene(SceneNotice scene) => new SceneNotice
        {
            Epoch = scene.Epoch, SceneKey = scene.SceneKey, WorldFingerprint = scene.WorldFingerprint
        };

        private static PlayerFrame CopyFrame(PlayerFrame source)
        {
            if (source.Parts == null) throw new ProtocolException("Missing sprite parts.");
            if (source.Parts.Length > PacketCodec.MaxParts) throw new ProtocolException("Too many sprite parts.");
            var parts = new SpritePartFrame[source.Parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                SpritePartFrame part = source.Parts[i];
                if (part == null) throw new ProtocolException("Missing sprite part.");
                parts[i] = new SpritePartFrame
                {
                    Slot = part.Slot, SpriteKey = part.SpriteKey, Pose = part.Pose, Color = part.Color,
                    Visible = part.Visible, FlipX = part.FlipX, FlipY = part.FlipY,
                    Layer = part.Layer, SortingLayer = part.SortingLayer, SortingOrder = part.SortingOrder
                };
            }
            return new PlayerFrame
            {
                PlayerId = source.PlayerId, SceneEpoch = source.SceneEpoch, SceneKey = source.SceneKey,
                SampleTime = source.SampleTime, Root = source.Root, Parts = parts
            };
        }
    }
}
