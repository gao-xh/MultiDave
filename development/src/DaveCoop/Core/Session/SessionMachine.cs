using System;
using System.Collections.Generic;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.World;
using DaveCoop.Core.Actions;
using DaveCoop.Core.Cargo;

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
        public const int MaxOutgoingMapPackets = 32;
        private sealed class LocalAction
        {
            public FishActionRequest Request;
            public string Fingerprint;
            public FishActionResult LastResult;
        }
        private sealed class CargoHeader
        {
            public CargoExpeditionPhase Phase;
            public string ReturnId;
            public CargoMemberSnapshot[] Members;
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
        private readonly Queue<WirePacket> _outgoingMap = new Queue<WirePacket>();
        private readonly Dictionary<(int Scene, string Address), MapIgpChoice> _publishedMapChoices = new Dictionary<(int, string), MapIgpChoice>();
        private MapRouteSelection _publishedMapRoute;
        private long _mapGeneration;
        private long _mapRevision;
        private string _mapFingerprint;
        private MapChoiceAssembler _mapAssembler = new MapChoiceAssembler();
        private MapChoiceSnapshot _incomingMapChoices;
        private long _receivedMapGeneration;
        private long _receivedMapRevision;
        private string _receivedMapFingerprint;
        private bool _receivedMapRetired;
        private bool _receivedMapRouteComplete;
        private CargoInventorySlice[] _outgoingCargo, _pendingCargo;
        private int _nextCargoSlice;
        private long _cargoGeneration, _cargoRevision;
        private string _cargoExpedition, _cargoFingerprint;
        private CargoHeader _publishedCargoHeader;
        private readonly HashSet<string> _cargoExpeditions = new HashSet<string>(StringComparer.Ordinal);
        private CargoInventoryAssembler _cargoAssembler;
        private CargoInventorySnapshot _incomingCargo;
        private long _receivedCargoGeneration, _receivedCargoRevision;
        private string _receivedCargoExpedition;
        private bool _receivedCargoCurrent;

        public SessionMachine(SessionRole role, HandshakeResult identity, double now, SessionOptions options = null)
        {
            if (identity == null || !Enum.IsDefined(typeof(SessionRole), role) ||
                identity.LocalPlayerId != (role == SessionRole.Host ? 1 : 2) ||
                identity.RemotePlayerId != (role == SessionRole.Host ? 2 : 1))
                throw new ArgumentException("Handshake identities do not match the session role.");
            PacketCodec.RequireRoom(new WirePacket { RoomId = identity.RoomId }, identity.RoomId);
            _identity = new HandshakeResult
            {
                RoomId = identity.RoomId, LocalPlayerId = identity.LocalPlayerId, RemotePlayerId = identity.RemotePlayerId,
                Peer = identity.Peer == null ? null : PacketCodec.CopyIdentity(identity.Peer)
            };
            _role = role; _options = (options ?? new SessionOptions()).CopyValidated();
            _cargoAssembler = new CargoInventoryAssembler(_identity.RoomId);
            CheckTime(now); _lastReceive = now; _nextPing = now;
            ChangePhase(SessionPhase.WaitingForScene, "Connected; waiting for local scene.");
        }

        public SessionSnapshot Snapshot => new SessionSnapshot
        {
            Role = _role, Phase = _phase, RoomId = _identity.RoomId, Reason = _reason,
            LocalPlayerId = _identity.LocalPlayerId, RemotePlayerId = _identity.RemotePlayerId,
            RemoteRequestsHostFishDisplay = _identity.Peer?.RequestsHostFishDisplay ?? false,
            SceneEpoch = _epoch, SceneKey = _proposal?.SceneKey, HasClockEstimate = _hasClock,
            RemoteClockOffsetSeconds = _offset, RoundTripSeconds = _rtt,
            MapChoiceGeneration = _role == SessionRole.Host ? _mapGeneration : _receivedMapGeneration,
            MapChoiceRevision = _role == SessionRole.Host ? _mapRevision : _receivedMapRevision,
            MapChoiceFingerprint = _role == SessionRole.Host ? (_publishedMapRoute == null ? null : _mapFingerprint) : _receivedMapFingerprint,
            CargoGeneration = _role == SessionRole.Host ? _cargoGeneration : _receivedCargoGeneration,
            CargoRevision = _role == SessionRole.Host ? _cargoRevision : _receivedCargoRevision,
            CargoExpeditionId = _role == SessionRole.Host ? _cargoExpedition : _receivedCargoExpedition,
            CargoPending = _role == SessionRole.Host ? _outgoingCargo != null || _pendingCargo != null : _cargoAssembler.Pending,
            CargoInventoryCurrent = _role == SessionRole.Host ? _cargoExpedition != null : _receivedCargoCurrent
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
                        RoomId = _identity.RoomId, BoundPlayerId = _identity.RemotePlayerId,
                        PacketSequence = packet.Sequence,
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
                case PacketKind.MapRouteSlice:
                    RequireRole(SessionRole.Guest);
                    PacketCodec.ValidateMapPayload(() => _mapAssembler.AcceptRoute(packet.MapRoute));
                    RefreshReceivedMapChoices();
                    break;
                case PacketKind.MapIgpChoice:
                    RequireRole(SessionRole.Guest);
                    PacketCodec.ValidateMapPayload(() => _mapAssembler.AcceptChoice(packet.MapChoice));
                    RefreshReceivedMapChoices();
                    break;
                case PacketKind.MapChoiceRetire:
                    RequireRole(SessionRole.Guest);
                    PacketCodec.ValidateMapPayload(() => _mapAssembler.Retire(packet.MapRetire));
                    RefreshReceivedMapChoices();
                    break;
                case PacketKind.CargoInventorySlice:
                    RequireRole(SessionRole.Guest);
                    ReceiveCargoInventory(packet.CargoInventory);
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
            for (int offset = 0; offset < 5; offset++)
            {
                int lane = (_nextGameplayLane + offset) % 5;
                if (lane == 0 && _outgoingActions.Count > 0)
                    packet = _outgoingActions.Dequeue();
                else if (lane == 1 && _outgoingFrame != null)
                { packet = _outgoingFrame; _outgoingFrame = null; }
                else if (lane == 2 && _outgoingWorld != null)
                {
                    packet = new WirePacket { Kind = PacketKind.WorldSlice, RoomId = _identity.RoomId, World = _outgoingWorld[_nextWorldSlice++] };
                    if (_nextWorldSlice == _outgoingWorld.Length) { _outgoingWorld = _pendingWorld; _pendingWorld = null; _nextWorldSlice = 0; }
                }
                else if (lane == 3 && _outgoingMap.Count > 0)
                    packet = _outgoingMap.Dequeue();
                else if (lane == 4 && _outgoingCargo != null)
                {
                    packet = new WirePacket { Kind = PacketKind.CargoInventorySlice, RoomId = _identity.RoomId, CargoInventory = _outgoingCargo[_nextCargoSlice++] };
                    if (_nextCargoSlice == _outgoingCargo.Length)
                    { _outgoingCargo = _pendingCargo; _pendingCargo = null; _nextCargoSlice = 0; }
                }
                else continue;
                _nextGameplayLane = (lane + 1) % 5; return true;
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

        public bool PublishMapRoute(MapRouteSelection route, double now)
        {
            CheckTime(now); RequireRole(SessionRole.Host);
            if (_phase == SessionPhase.Closed) return false;
            if (_mapGeneration == long.MaxValue) throw new ProtocolException("Map choice generation exhausted.");
            MapRouteSelection owned = MapCopy(() => MapSelections.CopyRoute(route));
            MapRouteSlice[] slices = MapCopy(() => MapChoiceFrames.SplitRoute(owned, _mapGeneration + 1));
            // Prepare every owned packet before canceling the old batch or
            // publishing a new generation. No partially queued route is possible.
            if (slices.Length > MaxOutgoingMapPackets) return false;
            var packets = new WirePacket[slices.Length];
            for (int i = 0; i < slices.Length; i++)
            {
                packets[i] = new WirePacket { Kind = PacketKind.MapRouteSlice, RoomId = _identity.RoomId, MapRoute = slices[i] };
                ValidateOutgoingMapPacket(packets[i]);
            }
            _outgoingMap.Clear();
            foreach (WirePacket packet in packets) _outgoingMap.Enqueue(packet);
            _mapGeneration++; _mapRevision = 0; _mapFingerprint = slices[0].RouteFingerprint;
            _publishedMapRoute = owned; _publishedMapChoices.Clear();
            // A packet already taken by the single writer precedes this new
            // generation on TCP. Its first slice invalidates the old candidate.
            return true;
        }

        public bool PublishMapIgpChoice(MapIgpChoice choice, double now)
        {
            CheckTime(now); RequireRole(SessionRole.Host);
            if (_phase == SessionPhase.Closed) return false;
            MapIgpChoice owned = MapCopy(() => MapChoiceFrames.Copy(choice));
            if (owned.Generation < _mapGeneration) return false;
            if (owned.Generation > _mapGeneration || owned.RouteFingerprint != _mapFingerprint)
                throw new ProtocolException("Local IGP choice does not match the published map generation/fingerprint.");
            if (_publishedMapRoute == null) return false;
            if (_mapRevision == long.MaxValue || owned.Revision != _mapRevision + 1)
                throw new ProtocolException("Local IGP choice revision must advance continuously.");
            bool knownScene = false;
            foreach (MapRouteScene scene in _publishedMapRoute.Scenes)
                if (scene.SceneId == owned.SceneId) { knownScene = true; break; }
            if (!knownScene) throw new ProtocolException("Local IGP choice targets a scene outside the published route.");
            var key = (owned.SceneId, owned.ControllerAddress);
            if (!_publishedMapChoices.ContainsKey(key) && _publishedMapChoices.Count >= MapChoiceFrames.MaxChoices)
                return RetireOverflowedMap("Map choice group capacity exceeded.", now);
            if (_outgoingMap.Count >= MaxOutgoingMapPackets)
                return RetireOverflowedMap("Map choice send queue full.", now);
            var packet = new WirePacket { Kind = PacketKind.MapIgpChoice, RoomId = _identity.RoomId, MapChoice = owned };
            ValidateOutgoingMapPacket(packet);
            _outgoingMap.Enqueue(packet); _publishedMapChoices[key] = owned; _mapRevision = owned.Revision;
            return true;
        }

        // Retirement is a control packet: cancel pending slices/choices, retain
        // the room's generation high water, and send before any later generation.
        public bool RetireMapChoices(string reason, double now)
        {
            CheckTime(now); RequireRole(SessionRole.Host);
            if (_phase == SessionPhase.Closed || _publishedMapRoute == null) return false;
            MapChoiceRetire notice = MapCopy(() => MapChoiceFrames.Copy(new MapChoiceRetire { Generation = _mapGeneration, Reason = reason }));
            var packet = new WirePacket { Kind = PacketKind.MapChoiceRetire, RoomId = _identity.RoomId, MapRetire = notice };
            ValidateOutgoingMapPacket(packet);
            // If the control bound is full, the peer terminates on this explicit
            // protocol failure rather than hiding an untransmitted retirement.
            Queue(packet);
            _outgoingMap.Clear(); _publishedMapRoute = null; _publishedMapChoices.Clear();
            // Keep the retired fingerprint internally so a legitimate delayed
            // same-generation callback cancels; a forged current fingerprint
            // still fails validation. Snapshot exposes null for retired sources.
            return true;
        }

        private bool RetireOverflowedMap(string reason, double now)
        {
            RetireMapChoices(reason, now);
            return false;
        }

        public bool TryTakeRemoteMapChoices(out MapChoiceSnapshot choices)
        {
            RequireRole(SessionRole.Guest);
            choices = _incomingMapChoices; _incomingMapChoices = null;
            return choices != null;
        }

        private void RefreshReceivedMapChoices()
        {
            MapChoiceSnapshot snapshot = MapCopy(() => _mapAssembler.Snapshot);
            bool complete = snapshot.Route != null;
            if (snapshot.Generation == _receivedMapGeneration && snapshot.LastChoiceRevision == _receivedMapRevision &&
                snapshot.Retired == _receivedMapRetired && complete == _receivedMapRouteComplete) return;
            _receivedMapGeneration = snapshot.Generation; _receivedMapRevision = snapshot.LastChoiceRevision;
            _receivedMapFingerprint = snapshot.Retired ? null : snapshot.RouteFingerprint;
            _receivedMapRetired = snapshot.Retired; _receivedMapRouteComplete = complete;
            // Assembly consumes every FIFO revision. Only the latest owned state
            // waits for the adapter; partial new routes revoke old usable state.
            _incomingMapChoices = snapshot;
        }

        private static T MapCopy<T>(Func<T> copy)
        {
            try { return copy(); }
            catch (ArgumentException error) { throw new ProtocolException("Invalid map choice data: " + error.Message); }
        }

        private static void ValidateOutgoingMapPacket(WirePacket packet)
        {
            packet.Sequence = 1;
            try { PacketCodec.Encode(packet); }
            finally { packet.Sequence = 0; }
        }

        private void ClearMapChoices()
        {
            _outgoingMap.Clear(); _publishedMapRoute = null; _publishedMapChoices.Clear();
            _mapGeneration = 0; _mapRevision = 0; _mapFingerprint = null;
            _mapAssembler = new MapChoiceAssembler(); _incomingMapChoices = null;
            _receivedMapGeneration = 0; _receivedMapRevision = 0; _receivedMapFingerprint = null;
            _receivedMapRetired = false; _receivedMapRouteComplete = false;
        }

        // Independent of scene/Ready: this is an owned ledger observation,
        // not a capture receipt, source fact, native bag or saving authority.
        public bool PublishCargoInventory(CargoInventorySnapshot snapshot, double now)
        {
            CheckTime(now); RequireRole(SessionRole.Host);
            if (_phase == SessionPhase.Closed) return false;
            CargoInventorySnapshot owned = CargoCopy(() => CargoInventoryFrames.Copy(snapshot));
            if (owned.SourceRoomId != Guid.Parse(_identity.RoomId).ToString("N"))
                throw new ProtocolException("Local cargo inventory belongs to a different source room.");
            string fingerprint = CargoCopy(() => CargoInventoryFrames.Fingerprint(owned));
            if (owned.Generation < _cargoGeneration || owned.Generation == _cargoGeneration && owned.Revision < _cargoRevision) return false;
            if (owned.Generation == _cargoGeneration && owned.Revision == _cargoRevision)
            {
                if (fingerprint != _cargoFingerprint) throw new ProtocolException("Local cargo revision conflicts with its prior snapshot.");
                return false;
            }
            if (owned.Revision <= _cargoRevision) throw new ProtocolException("Cargo room revision must advance across generations.");
            if (owned.Generation == _cargoGeneration)
            {
                if (owned.ExpeditionId != _cargoExpedition) throw new ProtocolException("Cargo generation cannot change expedition identity.");
                ValidateCargoContinuity(owned);
            }
            else
            {
                if (_cargoExpeditions.Contains(owned.ExpeditionId)) throw new ProtocolException("A retired cargo expedition cannot be replayed as a new generation.");
                if (_cargoExpeditions.Count >= CargoInventoryFrames.MaxExpeditions)
                    throw new ProtocolException("Cargo expedition identity quota exhausted.");
            }
            CargoInventorySlice[] slices = CargoCopy(() => CargoInventoryFrames.Split(owned));
            var nextHeader = new CargoHeader { Phase = owned.Phase, ReturnId = owned.ReturnId,
                Members = new[] { CargoInventoryFrames.CopyMember(owned.Members[0], true), CargoInventoryFrames.CopyMember(owned.Members[1], true) } };
            // Encode all pages before publishing high water or replacing a
            // batch. Caller mutation and invalid later pages cannot leak out.
            foreach (CargoInventorySlice slice in slices)
            {
                var packet = new WirePacket { Kind = PacketKind.CargoInventorySlice, RoomId = _identity.RoomId, CargoInventory = slice, Sequence = 1 };
                PacketCodec.Encode(packet);
            }
            if (_outgoingCargo != null && _nextCargoSlice > 0) _pendingCargo = slices;
            else { _outgoingCargo = slices; _nextCargoSlice = 0; }
            _cargoExpeditions.Add(owned.ExpeditionId);
            _cargoGeneration = owned.Generation; _cargoRevision = owned.Revision;
            _cargoExpedition = owned.ExpeditionId; _cargoFingerprint = fingerprint;
            _publishedCargoHeader = nextHeader;
            return true;
        }

        private void ValidateCargoContinuity(CargoInventorySnapshot next)
        {
            CargoHeader previous = _publishedCargoHeader;
            if (previous == null || previous.ReturnId != null && previous.ReturnId != next.ReturnId ||
                previous.Phase == CargoExpeditionPhase.Returned && next.Phase != CargoExpeditionPhase.Returned ||
                previous.Phase == CargoExpeditionPhase.Aborted && next.Phase != CargoExpeditionPhase.Aborted ||
                previous.Phase == CargoExpeditionPhase.Returning && next.Phase == CargoExpeditionPhase.Active)
                throw new ProtocolException("Cargo expedition phase or frozen return identity regressed.");
            for (int i = 0; i < previous.Members.Length; i++)
            {
                CargoMemberSnapshot prior = previous.Members[i], current = next.Members[i];
                if (prior.MemberId != current.MemberId || prior.BagMode != current.BagMode ||
                    current.BagRevision < prior.BagRevision || current.HighestRequestId < prior.HighestRequestId)
                    throw new ProtocolException("Cargo member identity or request/bag revision regressed.");
            }
        }

        public bool TryTakeRemoteCargoInventory(out CargoInventorySnapshot snapshot)
        {
            RequireRole(SessionRole.Guest);
            snapshot = _incomingCargo; _incomingCargo = null;
            return snapshot != null;
        }

        private void ReceiveCargoInventory(CargoInventorySlice slice)
        {
            CargoInventorySnapshot completed;
            bool accepted;
            try { accepted = _cargoAssembler.Add(slice, out completed); }
            catch (ArgumentException)
            {
                _incomingCargo = null; _receivedCargoCurrent = false;
                throw new ProtocolException("Cargo inventory assembly failed; its view is unavailable.");
            }
            if (_receivedCargoGeneration != _cargoAssembler.HighestGeneration || _receivedCargoRevision != _cargoAssembler.HighestRevision)
            {
                _incomingCargo = null; _receivedCargoCurrent = false;
                _receivedCargoGeneration = _cargoAssembler.HighestGeneration;
                _receivedCargoRevision = _cargoAssembler.HighestRevision;
                _receivedCargoExpedition = slice.ExpeditionId;
            }
            if (accepted)
            {
                _incomingCargo = CargoCopy(() => CargoInventoryFrames.Copy(completed));
                _receivedCargoCurrent = true;
            }
        }

        private static T CargoCopy<T>(Func<T> copy)
        {
            try { return copy(); }
            catch (ArgumentException) { throw new ProtocolException("Invalid cargo inventory data."); }
        }

        private void ClearCargoInventory()
        {
            _outgoingCargo = null; _pendingCargo = null; _nextCargoSlice = 0;
            _cargoGeneration = 0; _cargoRevision = 0; _cargoExpedition = null; _cargoFingerprint = null;
            _publishedCargoHeader = null;
            _cargoExpeditions.Clear(); _cargoAssembler = new CargoInventoryAssembler(_identity.RoomId);
            _incomingCargo = null; _receivedCargoGeneration = 0; _receivedCargoRevision = 0;
            _receivedCargoExpedition = null; _receivedCargoCurrent = false;
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
            _controls.Clear(); ClearFrames(); ClearMapChoices(); ClearCargoInventory(); _proposal = null; _localScene = null;
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
