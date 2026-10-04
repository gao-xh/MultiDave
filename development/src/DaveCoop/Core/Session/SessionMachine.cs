using System;
using System.Collections.Generic;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.World;
using DaveCoop.Core.Actions;

namespace DaveCoop.Core.Session
{
    // Pure CLR state machine. SessionPeer serializes access; it never calls Unity.
    public sealed class SessionMachine
    {
        private const int MaxControls = 32;
        private const int MaxEvents = 64;
        public const int MaxRemoteFishActions = 16;
        public const int MaxOutgoingFishActions = 32;
        public const int MaxRemoteFishActionResults = 32;
        public const int MaxOutstandingFishActions = 32;
        private const int MaxCompletedFishActions = 32;
        private sealed class LocalAction
        {
            public FishActionRequest Request;
            public string Fingerprint;
            public FishActionResult LastResult;
        }
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
        private readonly WorldAssembler _worldAssembler = new WorldAssembler();
        private WorldSlice[] _outgoingWorld;
        private WorldSlice[] _pendingWorld;
        private int _nextWorldSlice;
        private long _worldRevision;
        private double _lastLocalWorldTime = -1;
        private WorldSnapshot _incomingWorld;
        private int _nextGameplayLane;
        private readonly Queue<WirePacket> _outgoingActions = new Queue<WirePacket>();
        private readonly Queue<ReceivedFishAction> _incomingActions = new Queue<ReceivedFishAction>();
        private readonly Queue<FishActionResult> _incomingActionResults = new Queue<FishActionResult>();
        private readonly Dictionary<long, LocalAction> _localActions = new Dictionary<long, LocalAction>();
        private readonly Dictionary<long, FishActionResult> _completedActions = new Dictionary<long, FishActionResult>();
        private readonly Queue<long> _completedActionOrder = new Queue<long>();
        private long _highestLocalActionId;

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
                case PacketKind.WorldSlice:
                    RequireRole(SessionRole.Guest);
                    if (packet.World.SceneEpoch < _epoch) break;
                    if (packet.World.SceneEpoch != _epoch || packet.World.SceneKey != _proposal?.SceneKey)
                        throw new ProtocolException("World snapshot does not match the current scene.");
                    if (_phase != SessionPhase.Ready) break;
                    if (_worldAssembler.Accept(packet.World, out WorldSnapshot completed)) _incomingWorld = completed;
                    break;
                case PacketKind.FishActionRequest:
                    RequireRole(SessionRole.Host);
                    FishActionRequest request = packet.ActionRequest;
                    if (request.PlayerId != _identity.RemotePlayerId) throw new ProtocolException("Fish action player identity spoofing.");
                    if (request.SceneEpoch > _epoch || (request.SceneEpoch == _epoch && request.SceneKey != _proposal?.SceneKey))
                        throw new ProtocolException("Fish action does not match the current or a retired scene.");
                    if (_incomingActions.Count >= MaxRemoteFishActions) throw new ProtocolException("Fish action receive queue overflow; no intent was overwritten.");
                    // Retired/non-Ready requests still reach the CLR gate for a
                    // terminal rejection and request-ID accounting, never Unity.
                    _incomingActions.Enqueue(new ReceivedFishAction
                    {
                        RoomId = _identity.RoomId, BoundPlayerId = _identity.RemotePlayerId,
                        PacketSequence = packet.Sequence, ReceivedAt = now, Request = FishActions.Copy(request)
                    });
                    break;
                case PacketKind.FishActionResult:
                    RequireRole(SessionRole.Guest);
                    ReceiveFishActionResult(packet.ActionResult);
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
            // Controls/heartbeats precede gameplay. Each populated gameplay
            // lane receives a turn; action FIFO cannot overwrite older intent.
            for (int offset = 0; offset < 3; offset++)
            {
                int lane = (_nextGameplayLane + offset) % 3;
                if (lane == 0 && _outgoingActions.Count > 0)
                    packet = _outgoingActions.Dequeue();
                else if (lane == 1 && _outgoingFrame != null)
                { packet = _outgoingFrame; _outgoingFrame = null; }
                else if (lane == 2 && _outgoingWorld != null)
                {
                    packet = new WirePacket { Kind = PacketKind.WorldSlice, RoomId = _identity.RoomId, World = _outgoingWorld[_nextWorldSlice++] };
                    if (_nextWorldSlice == _outgoingWorld.Length) { _outgoingWorld = _pendingWorld; _pendingWorld = null; _nextWorldSlice = 0; }
                }
                else continue;
                _nextGameplayLane = (lane + 1) % 3; return true;
            }
            packet = null; return false;
        }

        public bool PublishFishAction(FishActionRequest request, double now)
        {
            CheckTime(now); RequireRole(SessionRole.Guest);
            if (_phase == SessionPhase.Closed) return false;
            FishActions.ValidateRequest(request);
            if (request.PlayerId != _identity.LocalPlayerId || request.SceneEpoch > _epoch ||
                (request.SceneEpoch == _epoch && request.SceneKey != _proposal?.SceneKey))
                throw new ProtocolException("Local fish action does not match its player or committed scene.");
            // A main-thread snapshot can become retired while this call waits
            // for SessionPeer's lock. It is a canceled send, not peer spoofing.
            if (_phase != SessionPhase.Ready || request.SceneEpoch < _epoch) return false;
            if (_outgoingActions.Count >= MaxOutgoingFishActions) throw new ProtocolException("Fish action send queue overflow; no intent was overwritten.");
            FishActionRequest copy = FishActions.Copy(request);
            string fingerprint = FishActions.Fingerprint(copy);
            if (_localActions.TryGetValue(copy.RequestId, out LocalAction existing))
            {
                if (existing.Fingerprint != fingerprint) throw new ProtocolException("Local fish action request ID changed its payload.");
            }
            else
            {
                if (copy.RequestId <= _highestLocalActionId) throw new ProtocolException("Local fish action request IDs must advance after completion or cancellation.");
                if (_localActions.Count >= MaxOutstandingFishActions) throw new ProtocolException("Fish action outstanding request capacity exceeded.");
                _localActions.Add(copy.RequestId, new LocalAction { Request = FishActions.Copy(copy), Fingerprint = fingerprint });
                _highestLocalActionId = copy.RequestId;
            }
            _outgoingActions.Enqueue(new WirePacket { Kind = PacketKind.FishActionRequest, RoomId = _identity.RoomId, ActionRequest = copy });
            return true;
        }

        public bool PublishFishActionResult(FishActionResult result, double now)
        {
            CheckTime(now); RequireRole(SessionRole.Host);
            if (_phase == SessionPhase.Closed) return false;
            FishActions.ValidateResult(result);
            if (result.PlayerId != _identity.RemotePlayerId || result.SceneEpoch > _epoch ||
                (result.SceneEpoch == _epoch && result.SceneKey != _proposal?.SceneKey))
                throw new ProtocolException("Local fish action result does not match its guest or scene.");
            if ((_phase != SessionPhase.Ready || result.SceneEpoch < _epoch) &&
                result.Status != FishActionStatus.Rejected && result.Status != FishActionStatus.OutcomeUnknown)
                // Scene state and enqueue are protected by SessionPeer's lock.
                // An old admission/probe decision cannot disconnect the room
                // merely because the network task advanced the scene first.
                return false;
            if (_outgoingActions.Count >= MaxOutgoingFishActions) throw new ProtocolException("Fish action result send queue overflow; no result was overwritten.");
            _outgoingActions.Enqueue(new WirePacket { Kind = PacketKind.FishActionResult, RoomId = _identity.RoomId, ActionResult = FishActions.Copy(result) });
            return true;
        }

        public bool TryTakeRemoteFishAction(out ReceivedFishAction action)
        {
            action = _incomingActions.Count == 0 ? null : _incomingActions.Dequeue();
            return action != null;
        }

        public bool TryTakeRemoteFishActionResult(out FishActionResult result)
        {
            result = _incomingActionResults.Count == 0 ? null : _incomingActionResults.Dequeue();
            return result != null;
        }

        private void ReceiveFishActionResult(FishActionResult result)
        {
            if (result.PlayerId != _identity.LocalPlayerId) throw new ProtocolException("Fish action result player identity spoofing.");
            if (result.SceneEpoch > _epoch || (result.SceneEpoch == _epoch && result.SceneKey != _proposal?.SceneKey))
                throw new ProtocolException("Fish action result does not match the current scene.");
            if (result.SceneEpoch < _epoch || _phase != SessionPhase.Ready) return;
            if (!_localActions.TryGetValue(result.RequestId, out LocalAction local))
            {
                if (_completedActions.TryGetValue(result.RequestId, out FishActionResult completed) && SameResult(completed, result)) return;
                throw new ProtocolException("Unsolicited or conflicting completed fish action result.");
            }
            FishActionRequest request = local.Request;
            if (result.PlayerId != request.PlayerId || result.SceneEpoch != request.SceneEpoch || result.SceneKey != request.SceneKey ||
                result.Action != request.Action || result.TargetEntityId != request.TargetEntityId || result.RequestFingerprint != local.Fingerprint)
                throw new ProtocolException("Fish action result does not match the outstanding request metadata and fingerprint.");
            if (local.LastResult?.Status == FishActionStatus.NativeStarted &&
                (result.Status == FishActionStatus.Queued || result.OperationId != local.LastResult.OperationId))
                throw new ProtocolException("Fish action result changed or regressed its native operation.");
            if (_incomingActionResults.Count >= MaxRemoteFishActionResults) throw new ProtocolException("Fish action result receive queue overflow; no result was overwritten.");
            FishActionResult copy = FishActions.Copy(result);
            _incomingActionResults.Enqueue(copy); local.LastResult = FishActions.Copy(copy);
            if (copy.Status == FishActionStatus.Rejected || copy.Status == FishActionStatus.DryRunValidated || copy.Status == FishActionStatus.OutcomeUnknown)
            {
                _localActions.Remove(copy.RequestId);
                if (_completedActionOrder.Count == MaxCompletedFishActions) _completedActions.Remove(_completedActionOrder.Dequeue());
                _completedActionOrder.Enqueue(copy.RequestId); _completedActions.Add(copy.RequestId, FishActions.Copy(copy));
            }
        }

        private static bool SameResult(FishActionResult a, FishActionResult b) =>
            a.RequestId == b.RequestId && a.PlayerId == b.PlayerId && a.SceneEpoch == b.SceneEpoch && a.SceneKey == b.SceneKey &&
            a.Action == b.Action && a.TargetEntityId == b.TargetEntityId && a.Status == b.Status && a.Reason == b.Reason &&
            a.WorldRevision == b.WorldRevision && a.OperationId == b.OperationId && a.RequestFingerprint == b.RequestFingerprint;

        public bool PublishWorld(WorldSnapshot source, double now)
        {
            CheckTime(now); RequireRole(SessionRole.Host);
            if (_phase != SessionPhase.Ready) return false;
            if (source == null || source.SceneEpoch != _epoch || source.SceneKey != _proposal.SceneKey || source.SampleTime > now + 1)
                throw new ProtocolException("Local world snapshot does not match the active scene or clock.");
            if (_worldRevision == long.MaxValue) throw new ProtocolException("World revision exhausted.");
            var copy = new WorldSnapshot
            {
                SceneEpoch = _epoch, SceneKey = source.SceneKey, Revision = _worldRevision + 1,
                SampleTime = source.SampleTime, Entities = source.Entities
            };
            WorldSlice[] slices = WorldFrames.Split(copy);
            if (source.SampleTime <= _lastLocalWorldTime) return false;
            _worldRevision++; _lastLocalWorldTime = source.SampleTime;
            // Finish a started snapshot even on a slow link. Retain only the
            // latest subsequent snapshot, so continuous producers cannot starve
            // the receiver's atomic commits or grow a send backlog.
            if (_outgoingWorld != null && _nextWorldSlice > 0) _pendingWorld = slices;
            else { _outgoingWorld = slices; _nextWorldSlice = 0; }
            return true;
        }

        public bool TryTakeRemoteWorld(out WorldSnapshot snapshot)
        {
            snapshot = _incomingWorld; _incomingWorld = null; return snapshot != null;
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
            _outgoingWorld = null; _pendingWorld = null; _nextWorldSlice = 0; _incomingWorld = null;
            _worldRevision = 0; _lastLocalWorldTime = -1; _worldAssembler.Clear(); _nextGameplayLane = 0;
            _outgoingActions.Clear(); _incomingActions.Clear(); _incomingActionResults.Clear();
            _localActions.Clear(); _completedActions.Clear(); _completedActionOrder.Clear();
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
