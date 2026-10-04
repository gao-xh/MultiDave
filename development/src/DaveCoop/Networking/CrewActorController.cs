using System;
using BepInEx.Logging;
using DaveCoop.Core.Crew;
using DaveCoop.Core.Session;
using UnityEngine;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace DaveCoop.Networking
{
    // Production owner: main real peer -> host physics -> state -> temporary
    // guest correction/input. Local TCP display tests cannot create this body.
    internal sealed class CrewActorController : IDisposable
    {
        public const double StateStaleSeconds = 0.5;
        private readonly LocalAvatarCapture _local;
        private readonly Func<SessionPeer> _currentPeer;
        private readonly Func<bool> _localTest;
        private readonly ManualLogSource _logger;
        private readonly int _thread;
        private readonly HostCrewProfile _profile;
        private SessionPeer _bound;
        private string _room, _member;
        private long _nextActor, _nextInput, _guestActor, _guestState, _guestPacket, _guestInputActor;
        private HostEmployeeActorBody _body;
        private HostCrewControl _control;
        private GuestCrewActorCorrection _correction;
        private ReceivedCrewActorState _receivedState;
        private float? _previousHP, _previousOxygen;
        private double _nextSend, _nextInputSend;
        private bool _failed, _busy, _paused, _hostStateEstablished;
        public bool Enabled { get; }
        public bool Failed => _failed || (_correction?.Failed ?? false);
        public string Status { get; private set; }
        public long InputsAccepted { get; private set; }
        public long InputsSent { get; private set; }
        public long StatesSent { get; private set; }
        public long StatesReceived { get; private set; }
        public long FireEdgesObserved { get; private set; }
        public long Corrections => _correction?.Corrections ?? 0;
        public long Moves => _body?.Moves ?? 0;
        public long BlockedMoves => _body?.Blocked ?? 0;
        public string BodyStatus => _body?.Status;
        public string CorrectionStatus => _correction?.Status;
        public float? HP => _control?.HP ?? _receivedState?.Frame.HP;
        public float? Oxygen => _control?.Oxygen ?? _receivedState?.Frame.Oxygen;

        public CrewActorController(LocalAvatarCapture local, Func<SessionPeer> currentPeer, Func<bool> localTest,
            int thread, ManualLogSource logger, bool enabled, HostCrewProfile profile)
        {
            _local = local; _currentPeer = currentPeer; _localTest = localTest; _thread = thread;
            _logger = logger; Enabled = enabled; _profile = profile;
            Status = enabled ? "WaitingForCrewPeer" : "Disabled";
        }

        public void Update(bool panelOpen)
        {
            if (!Begin()) return;
            try
            {
                SessionPeer peer = _currentPeer();
                if (peer == null || _localTest()) { Disconnect(); return; }
                SessionSnapshot state = peer.Snapshot;
                if (!ReferenceEquals(_bound, peer) || _room != state.RoomId) Bind(peer, state);
                if (!state.LocalUsesCrewActor || !state.RemoteUsesCrewActor)
                { RetireBody("BothPeersMustOptIn"); _receivedState = null; Status = "BothPeersMustOptIn"; return; }
                if (state.Role == SessionRole.Host) UpdateHost(peer, state);
                else UpdateGuest(peer, state, panelOpen);
            }
            catch (Exception error) { Fault(error); }
            finally { _busy = false; }
        }

        private void Bind(SessionPeer peer, SessionSnapshot state)
        {
            Disconnect(); _bound = peer; _room = state.RoomId;
            // Host-minted actor member token bound to the real player-2 peer.
            // This is not a CargoLedger member registration or bag receipt.
            _member = Guid.NewGuid().ToString("N");
            _correction = new GuestCrewActorCorrection(_local, _currentPeer, _thread);
        }

        private void UpdateHost(SessionPeer peer, SessionSnapshot state)
        {
            if (state.Phase != SessionPhase.Ready)
            { RetireBody("SessionPaused"); _paused = true; Status = "SessionPaused"; return; }
            if (_control != null && (_control.SceneEpoch != state.SceneEpoch || _control.SceneKey != state.SceneKey))
                RetireBody("SceneChanged");
            if (!_local.IsAvailable || _local.SceneHandle == 0) { RetireBody("LocalSourceUnavailable"); return; }
            if (_hostStateEstablished && peer.Snapshot.CrewActorRevision != _control.ActorRevision)
                throw new InvalidOperationException("Employee transport actor binding changed.");
            if (_body != null && !_body.IsCurrent(_control.ActorRevision, state.SceneEpoch, state.SceneKey))
                throw new InvalidOperationException("Employee body source changed in the current scene.");
            if (_body == null)
            {
                if (_nextActor == long.MaxValue) throw new InvalidOperationException("Crew actor revision exhausted.");
                long revision = ++_nextActor;
                // Retain the instance before native creation. A failed/unknown
                // creation is stopped, never repeated by the following Update.
                HostEmployeeActorBody created = null;
                created = new HostEmployeeActorBody(_local, _thread, _logger,
                    () => Current(peer, state) && state.Role == SessionRole.Host && state.Phase == SessionPhase.Ready &&
                        _nextActor == revision && ReferenceEquals(_body, created) &&
                        (!_hostStateEstablished || peer.Snapshot.CrewActorRevision == revision));
                _body = created;
                if (!_body.TryCreate(revision, state.SceneEpoch, state.SceneKey) ||
                    !_body.TryReadMotion(out NVector3 position, out NVector2 velocity))
                    throw new InvalidOperationException("Employee body creation unsupported or unknown: " + _body.Status);
                _control = new HostCrewControl(_room, _member, state.SceneEpoch, state.SceneKey, revision, _profile,
                    position, _previousHP, _previousOxygen);
                _control.ObserveBody(position, velocity, true); _nextSend = 0;
                if (!peer.PublishCrewActorState(_control.CaptureState()))
                { _control.Neutralize("FirstStateNotPublished"); return; }
                _hostStateEstablished = true; StatesSent++; _nextSend = peer.Now + 1d / 30;
                _logger.LogInfo("DAVECOOP_CREW_ACTOR_CREATED: host-owned employee physics; native gameplay remains unverified.");
            }
            if (!_hostStateEstablished)
            {
                if (!Current(peer, state) || !peer.PublishCrewActorState(_control.CaptureState())) return;
                _hostStateEstablished = true; StatesSent++; _nextSend = peer.Now + 1d / 30;
            }
            bool running = float.IsFinite(Time.timeScale) && Time.timeScale > 0;
            if (!running)
            {
                _control.Neutralize("UnityTimePaused");
                if (!_paused) _body.TryMove(NVector2.Zero, 0.02f);
                _paused = true; Status = "UnityTimePaused";
            }
            else { _paused = false; Status = _control.Status; }
            for (int i = 0; i < 32 && peer.TryTakeRemoteCrewInput(out ReceivedCrewInput input); i++)
                if (running && HostCurrent(peer, state) && _body.IsCurrent(_control.ActorRevision, state.SceneEpoch, state.SceneKey) &&
                    _control.TryAccept(input, peer.Snapshot, peer.Now)) InputsAccepted++;
        }

        private void UpdateGuest(SessionPeer peer, SessionSnapshot state, bool panelOpen)
        {
            if (state.Phase != SessionPhase.Ready) { _receivedState = null; Status = "SessionPaused"; return; }
            if (peer.TryTakeRemoteCrewActorState(out ReceivedCrewActorState received))
            {
                CrewFrames.Validate(received);
                CrewActorState frame = received.Frame;
                if (Current(peer, state) && received.RoomId == _room && frame.SceneEpoch == state.SceneEpoch && frame.SceneKey == state.SceneKey &&
                    frame.ActorRevision == peer.Snapshot.CrewActorRevision && received.PacketSequence > _guestPacket &&
                    (frame.ActorRevision > _guestActor || (frame.ActorRevision == _guestActor && frame.StateRevision > _guestState)))
                {
                    _guestPacket = received.PacketSequence; _guestActor = frame.ActorRevision; _guestState = frame.StateRevision;
                    _receivedState = CrewFrames.Copy(received); StatesReceived++;
                }
            }
            if (!GuestStateCurrent(peer, state)) { _receivedState = null; Status = "WaitingForFreshHostBody"; return; }
            if (peer.Now < _nextInputSend) return;
            if (!_correction.TryReadInputPosition(peer, out NVector3 position))
            { Status = "WaitingForTemporaryGuestBody"; return; }
            if (_nextInput == long.MaxValue) throw new InvalidOperationException("Crew input sequence exhausted.");
            var input = new CrewInputFrame
            {
                PlayerId = 2, SceneEpoch = state.SceneEpoch, SceneKey = state.SceneKey,
                InputSequence = ++_nextInput, ActorRevision = _receivedState.Frame.ActorRevision
            };
            if (_guestInputActor == input.ActorRevision && !panelOpen && float.IsFinite(Time.timeScale) && Time.timeScale > 0)
            {
                input.MoveX = (Down(KeyCode.D, KeyCode.RightArrow) ? 1 : 0) - (Down(KeyCode.A, KeyCode.LeftArrow) ? 1 : 0);
                input.MoveY = (Down(KeyCode.W, KeyCode.UpArrow) ? 1 : 0) - (Down(KeyCode.S, KeyCode.DownArrow) ? 1 : 0);
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) input.Buttons |= CrewButtons.Boost;
                if (Input.GetMouseButton(0)) input.Buttons |= CrewButtons.Fire;
                if (Input.GetKey(KeyCode.R)) input.Buttons |= CrewButtons.Recall;
                if (Input.GetKey(KeyCode.Space)) input.Buttons |= CrewButtons.Interact;
                Camera camera = Camera.main;
                if (camera != null)
                {
                    Vector3 point = new Vector3 { x = position.X, y = position.Y, z = position.Z };
                    Vector3 mouse = Input.mousePosition; mouse.z = camera.WorldToScreenPoint(point).z;
                    Vector3 world = camera.ScreenToWorldPoint(mouse);
                    double dx = (double)world.x - position.X, dy = (double)world.y - position.Y;
                    double length = Math.Sqrt(dx * dx + dy * dy);
                    if (double.IsFinite(length) && length > 0)
                    { input.AimX = (float)(dx / length); input.AimY = (float)(dy / length); }
                }
            }
            // Recheck the native guest source after input/camera queries, and
            // the exact peer/actor before enqueue. No client pose is transmitted.
            if (!_correction.TryReadInputPosition(peer, out _) || !GuestStateCurrent(peer, state)) return;
            if (peer.PublishCrewInput(input))
            { _guestInputActor = input.ActorRevision; InputsSent++; _nextInputSend = peer.Now + 1d / 30; Status = "GuestInputPublished"; }
        }

        public void FixedUpdate()
        {
            if (!Begin()) return;
            try
            {
                SessionPeer peer = _currentPeer(); SessionSnapshot state = peer?.Snapshot;
                if (!Current(peer, state) || state.Phase != SessionPhase.Ready || _localTest()) return;
                if (state.Role == SessionRole.Guest)
                {
                    if (GuestStateCurrent(peer, state)) _correction.TryApply(peer, _receivedState);
                    return;
                }
                if (!HostCurrent(peer, state)) return;
                if (_paused || _body == null || _control == null) return;
                if (!_body.IsCurrent(_control.ActorRevision, state.SceneEpoch, state.SceneKey))
                    throw new InvalidOperationException("Employee source expired before physics step.");
                if (!_body.TryReadMotion(out NVector3 position, out NVector2 velocity))
                    throw new InvalidOperationException("Employee physical readback unavailable.");
                float delta = Time.fixedDeltaTime;
                CrewMovementPlan plan = _control.Step(state, peer.Now, delta, position, velocity, _body.Active);
                if (plan.CanMove && HostCurrent(peer, state))
                {
                    if (!_body.TryMove(plan.RequestedVelocity, delta))
                        throw new InvalidOperationException("Employee move unsupported or unknown: " + _body.Status);
                    if ((plan.PressedButtons & CrewButtons.Fire) != 0) FireEdgesObserved++;
                }
                if (!HostCurrent(peer, state) || !_body.IsCurrent(_control.ActorRevision, state.SceneEpoch, state.SceneKey) ||
                    !_body.TryReadMotion(out position, out velocity))
                    throw new InvalidOperationException("Employee source changed during physics step.");
                _control.ObserveBody(position, velocity, _body.Active);
                if (peer.Now >= _nextSend && peer.PublishCrewActorState(_control.CaptureState()))
                { StatesSent++; _nextSend = peer.Now + 1d / 30; }
                Status = _control.Status;
            }
            catch (Exception error) { Fault(error); }
            finally { _busy = false; }
        }

        public bool TryHostDisplayPosition(out NVector3 position)
        {
            position = default;
            SessionPeer peer = _currentPeer(); SessionSnapshot state = peer?.Snapshot;
            if (!Enabled || Failed || Environment.CurrentManagedThreadId != _thread || !HostCurrent(peer, state) ||
                state.Role != SessionRole.Host || state.Phase != SessionPhase.Ready || _body == null || _control == null ||
                !_control.Active || !_body.IsCurrent(_control.ActorRevision, state.SceneEpoch, state.SceneKey)) return false;
            return _body.TryReadPosition(out position) && HostCurrent(peer, state);
        }

        private bool GuestStateCurrent(SessionPeer peer, SessionSnapshot state)
        {
            if (!Current(peer, state) || state.Phase != SessionPhase.Ready || _receivedState == null) return false;
            CrewActorState frame = _receivedState.Frame;
            return frame.SceneEpoch == state.SceneEpoch && frame.SceneKey == state.SceneKey &&
                frame.ActorRevision == peer.Snapshot.CrewActorRevision && frame.StateRevision == peer.Snapshot.CrewStateRevision && frame.Active && frame.Alive &&
                peer.Now >= _receivedState.ReceivedAt && peer.Now - _receivedState.ReceivedAt <= StateStaleSeconds;
        }
        private bool Current(SessionPeer peer, SessionSnapshot state)
        {
            if (Failed || peer == null || state == null || !ReferenceEquals(peer, _bound) || !ReferenceEquals(peer, _currentPeer()) ||
                state.RoomId != _room || !state.LocalUsesCrewActor || !state.RemoteUsesCrewActor) return false;
            SessionSnapshot now = peer.Snapshot;
            return now.RoomId == _room && now.Role == state.Role && now.Phase == state.Phase &&
                now.SceneEpoch == state.SceneEpoch && now.SceneKey == state.SceneKey && now.LocalUsesCrewActor && now.RemoteUsesCrewActor;
        }
        private bool HostCurrent(SessionPeer peer, SessionSnapshot state) => Current(peer, state) &&
            state.Role == SessionRole.Host && state.Phase == SessionPhase.Ready && _hostStateEstablished && _control != null &&
            peer.Snapshot.CrewActorRevision == _control.ActorRevision;
        private bool Begin()
        {
            if (_correction?.Failed ?? false) { _failed = true; Status = "GuestCorrectionFailed"; }
            if (!Enabled || Failed || Environment.CurrentManagedThreadId != _thread) return false;
            if (_busy) { Fault(new InvalidOperationException("Reentrant crew update.")); return false; }
            _busy = true; return true;
        }
        private static bool Down(KeyCode a, KeyCode b) => Input.GetKey(a) || Input.GetKey(b);
        private void RetireBody(string reason)
        {
            if (_control != null) { _previousHP = _control.HP; _previousOxygen = _control.Oxygen; _control.Stop(reason); }
            _body?.Stop(reason); _body?.Dispose();
            if (_body != null && (_body.Failed || !_body.CleanupVerified)) _failed = true;
            _body = null; _control = null; _hostStateEstablished = false; Status = reason;
        }
        public void InvalidateLocalSource()
        { RetireBody("LocalSourceChanged"); _receivedState = null; }
        public void Disconnect()
        {
            RetireBody("Disconnected"); _bound = null; _room = null; _member = null;
            _previousHP = null; _previousOxygen = null; _receivedState = null; _correction = null;
            _nextInput = 0; _guestActor = 0; _guestState = 0; _guestPacket = 0; _guestInputActor = 0;
            _nextSend = 0; _nextInputSend = 0; _paused = false;
        }
        private void Fault(Exception error)
        {
            _failed = true; RetireBody("Failed"); _receivedState = null;
            try { _logger.LogWarning("DAVECOOP_CREW_ACTOR_UNAVAILABLE: " + error.GetType().Name + ": " + error.Message); } catch { }
        }
        public void Dispose() => Disconnect();
    }
}
