using System;
using BepInEx.Logging;
using DaveCoop.Core.Crew;
using DaveCoop.Core.Session;
using DaveCoop.Core.World;
using UnityEngine;
using UnityEngine.SceneManagement;
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
        private readonly HostHarpoonProfile _harpoonProfile;
        private readonly FishStateCapture _fish;
        private readonly FishLifecycleHooks _lifecycle;
        private readonly bool _harpoonEnabled;
        private SessionPeer _bound;
        private string _room, _member;
        private long _nextActor, _nextInput, _guestActor, _guestState, _guestPacket, _guestInputActor;
        private HostEmployeeActorBody _body;
        private HostCrewControl _control;
        private HostHarpoonControl _harpoon;
        private HostEmployeeHarpoonProjectile _projectile;
        private NativeEmployeeHarpoonDamageBridge _damage;
        private RemoteEmployeeHarpoonVisual _harpoonDisplay;
        private long _harpoonInputFloor, _harpoonPacketFloor, _harpoonShotFloor;
        private NVector3 _harpoonPosition;
        private NVector2 _harpoonDirection;
        private GuestCrewActorCorrection _correction;
        private ReceivedCrewActorState _receivedState;
        private float? _previousHP, _previousOxygen;
        private double _nextSend, _nextInputSend;
        private bool _failed, _busy, _paused, _hostStateEstablished;
        private CrewButtons _lastPublishedButtons;
        public bool Enabled { get; }
        public bool Failed => _failed || (_correction?.Failed ?? false);
        public string Status { get; private set; }
        public long InputsAccepted { get; private set; }
        public long InputsSent { get; private set; }
        public long StatesSent { get; private set; }
        public long StatesReceived { get; private set; }
        public long FireEdgesObserved { get; private set; }
        public long HarpoonsFired { get; private set; }
        public long HarpoonCollisions { get; private set; }
        public long HarpoonDamageDispatches { get; private set; }
        public string HarpoonStatus { get; private set; } = "Disabled";
        public long Corrections => _correction?.Corrections ?? 0;
        public long Moves => _body?.Moves ?? 0;
        public long BlockedMoves => _body?.Blocked ?? 0;
        public string BodyStatus => _body?.Status;
        public string CorrectionStatus => _correction?.Status;
        public float? HP => _control?.HP ?? _receivedState?.Frame.HP;
        public float? Oxygen => _control?.Oxygen ?? _receivedState?.Frame.Oxygen;

        public CrewActorController(LocalAvatarCapture local, Func<SessionPeer> currentPeer, Func<bool> localTest,
            int thread, ManualLogSource logger, bool enabled, HostCrewProfile profile,
            bool harpoonEnabled, HostHarpoonProfile harpoonProfile, FishStateCapture fish, FishLifecycleHooks lifecycle)
        {
            _local = local; _currentPeer = currentPeer; _localTest = localTest; _thread = thread;
            _logger = logger; Enabled = enabled; _profile = profile;
            _harpoonEnabled = enabled && harpoonEnabled; _harpoonProfile = harpoonProfile; _fish = fish; _lifecycle = lifecycle;
            HarpoonStatus = _harpoonEnabled ? "WaitingForHostActor" : "Disabled";
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
                if (_harpoonEnabled) _harpoon = new HostHarpoonControl(_control, _harpoonProfile,
                    _harpoonInputFloor, _harpoonPacketFloor, _harpoonShotFloor);
                _control.ObserveBody(position, velocity, true); _nextSend = 0;
                if (!peer.PublishCrewActorState(CaptureHostState()))
                { _control.Neutralize("FirstStateNotPublished"); return; }
                _hostStateEstablished = true; StatesSent++; _nextSend = peer.Now + 1d / 30;
                _logger.LogInfo("DAVECOOP_CREW_ACTOR_CREATED: host-owned employee physics; native gameplay remains unverified.");
            }
            if (!_hostStateEstablished)
            {
                if (!Current(peer, state) || !peer.PublishCrewActorState(CaptureHostState())) return;
                _hostStateEstablished = true; StatesSent++; _nextSend = peer.Now + 1d / 30;
            }
            bool running = float.IsFinite(Time.timeScale) && Time.timeScale > 0;
            if (!running)
            {
                _control.Neutralize("UnityTimePaused");
                _harpoon?.Neutralize("UnityTimePaused"); RetireProjectile("UnityTimePaused");
                if (!_paused) _body.TryMove(NVector2.Zero, 0.02f);
                _paused = true; Status = "UnityTimePaused";
            }
            else { _paused = false; Status = _control.Status; }
            for (int i = 0; i < 32 && peer.TryTakeRemoteCrewInput(out ReceivedCrewInput input); i++)
                if (running && HostCurrent(peer, state) && _body.IsCurrent(_control.ActorRevision, state.SceneEpoch, state.SceneKey) &&
                    _control.TryAccept(input, peer.Snapshot, peer.Now))
                {
                    InputsAccepted++;
                    // Consume every accepted input separately; never dispatch
                    // the movement model's OR-merged diagnostic button bits.
                    _harpoon?.TryAccept(input, peer.Snapshot, peer.Now);
                }
        }

        private void UpdateGuest(SessionPeer peer, SessionSnapshot state, bool panelOpen)
        {
            if (state.Phase != SessionPhase.Ready) { HideGuestHarpoon(); _receivedState = null; Status = "SessionPaused"; return; }
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
            if (!GuestStateCurrent(peer, state)) { HideGuestHarpoon(); _receivedState = null; Status = "WaitingForFreshHostBody"; return; }
            DisplayGuestHarpoon(peer, state);
            CrewButtons observedButtons = CrewButtons.None;
            bool inputActive = _guestInputActor == _receivedState.Frame.ActorRevision && !panelOpen &&
                float.IsFinite(Time.timeScale) && Time.timeScale > 0;
            if (inputActive)
            {
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) observedButtons |= CrewButtons.Boost;
                if (Input.GetMouseButton(0)) observedButtons |= CrewButtons.Fire;
                if (Input.GetKey(KeyCode.R)) observedButtons |= CrewButtons.Recall;
                if (Input.GetKey(KeyCode.Space)) observedButtons |= CrewButtons.Interact;
            }
            // Poll on every Unity Update. Publish button changes immediately;
            // the 30 Hz movement clock must not swallow a complete click.
            if (peer.Now < _nextInputSend && observedButtons == _lastPublishedButtons) return;
            if (!_correction.TryReadInputPosition(peer, out NVector3 position))
            { Status = "WaitingForTemporaryGuestBody"; return; }
            if (_nextInput == long.MaxValue) throw new InvalidOperationException("Crew input sequence exhausted.");
            var input = new CrewInputFrame
            {
                PlayerId = 2, SceneEpoch = state.SceneEpoch, SceneKey = state.SceneKey,
                InputSequence = ++_nextInput, ActorRevision = _receivedState.Frame.ActorRevision, Buttons = observedButtons
            };
            if (inputActive)
            {
                input.MoveX = (Down(KeyCode.D, KeyCode.RightArrow) ? 1 : 0) - (Down(KeyCode.A, KeyCode.LeftArrow) ? 1 : 0);
                input.MoveY = (Down(KeyCode.W, KeyCode.UpArrow) ? 1 : 0) - (Down(KeyCode.S, KeyCode.DownArrow) ? 1 : 0);
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
            { _guestInputActor = input.ActorRevision; _lastPublishedButtons = observedButtons; InputsSent++; _nextInputSend = peer.Now + 1d / 30; Status = "GuestInputPublished"; }
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
                StepHarpoon(peer, state, position, delta);
                if (peer.Now >= _nextSend && peer.PublishCrewActorState(CaptureHostState()))
                { StatesSent++; _nextSend = peer.Now + 1d / 30; }
                Status = _control.Status;
            }
            catch (Exception error) { Fault(error); }
            finally { _busy = false; }
        }

        private CrewActorState CaptureHostState()
        {
            CrewActorState frame = _control.CaptureState();
            frame.HarpoonShotId = _harpoon?.HighestShotId ?? _harpoonShotFloor;
            frame.HarpoonActive = frame.Active && frame.Alive && (_projectile?.Active ?? false);
            frame.HarpoonPosition = frame.HarpoonShotId > 0 ? _harpoonPosition : NVector3.Zero;
            frame.HarpoonDirection = frame.HarpoonShotId > 0 ? _harpoonDirection : NVector2.Zero;
            return frame;
        }

        private bool HarpoonCurrent(SessionPeer peer, SessionSnapshot state, HostEmployeeHarpoonProjectile projectile) =>
            HostCurrent(peer, state) && _control.Alive && _control.Active && _body != null && _body.Active &&
            ReferenceEquals(projectile, _projectile) && projectile != null && projectile.ActorRevision == _control.ActorRevision &&
            projectile.SceneEpoch == state.SceneEpoch && projectile.SceneKey == state.SceneKey;

        private void StepHarpoon(SessionPeer peer, SessionSnapshot state, NVector3 position, float delta)
        {
            if (!_harpoonEnabled || _harpoon == null) return;
            if (!_harpoon.CheckCurrent(peer.Snapshot, peer.Now)) { RetireProjectile("ActorUnavailable"); return; }
            if (_harpoon.TryTakeCommand(peer.Snapshot, peer.Now, position, out CrewHarpoonCommand command))
            {
                if (command.Kind == CrewHarpoonCommandKind.Recall)
                {
                    if (_projectile != null && _projectile.ShotId == command.ShotId)
                    {
                        _harpoon.ObserveProjectile(command.ShotId, peer.Snapshot, peer.Now, _harpoonPosition, false);
                        RetireProjectile("RecallConsumed");
                    }
                }
                else
                {
                    RetireProjectile("PreviousShotEnded");
                    if (!_body.TryReadWeaponScene(out Scene scene, out int mask, out _) || !HostCurrent(peer, state))
                        throw new InvalidOperationException("Harpoon actor source unavailable.");
                    HostEmployeeHarpoonProjectile created = null;
                    created = new HostEmployeeHarpoonProjectile(_body, scene, command, _thread,
                        () => HarpoonCurrent(peer, state, created), _logger);
                    _projectile = created;
                    // Slot and consumed command retained before native creation.
                    _harpoonShotFloor = command.ShotId; _harpoonDirection = command.Direction; _harpoonPosition = position;
                    _damage = new NativeEmployeeHarpoonDamageBridge(_fish, _lifecycle, _thread,
                        () => HarpoonCurrent(peer, state, created), _logger);
                    if (!created.TryFire(mask)) throw new InvalidOperationException("Harpoon fire unknown: " + created.Status);
                    HarpoonsFired++;
                }
            }
            HostEmployeeHarpoonProjectile shot = _projectile;
            if (shot == null) { HarpoonStatus = _harpoon.Status; return; }
            if (!HarpoonCurrent(peer, state, shot)) throw new InvalidOperationException("Harpoon source expired.");
            if (!shot.FixedStep(delta, out HostEmployeeHarpoonHit collision) || !shot.TryReadPosition(out _harpoonPosition))
                throw new InvalidOperationException("Harpoon sweep/readback unknown: " + shot.Status);
            if (collision != null)
            {
                HarpoonCollisions++;
                if (_damage.TryResolveTarget(collision, out HostEntityTarget target))
                {
                    var candidate = new CrewHarpoonCollision(collision.Command, target.EntityId, target.Generation, target.DataTid, collision.Point);
                    if (_harpoon.TryHit(candidate, peer.Snapshot, peer.Now, out CrewHarpoonHit once))
                    {
                        _damage.TryDispatch(collision, once); HarpoonDamageDispatches += _damage.DispatchesEntered;
                    }
                }
                if (_damage.Failed) throw new InvalidOperationException("Harpoon native damage source unknown: " + _damage.Status);
                _harpoon.ObserveProjectile(shot.ShotId, peer.Snapshot, peer.Now, _harpoonPosition, false);
                HarpoonStatus = _damage.Status; RetireProjectile("CollisionConsumed");
            }
            else if (!_harpoon.ObserveProjectile(shot.ShotId, peer.Snapshot, peer.Now, _harpoonPosition, shot.Active))
            { HarpoonStatus = shot.Status; RetireProjectile("ProjectileEnded"); }
            else HarpoonStatus = shot.Status;
        }

        private void DisplayGuestHarpoon(SessionPeer peer, SessionSnapshot state)
        {
            CrewActorState frame = _receivedState.Frame;
            if (!frame.HarpoonActive) { HideGuestHarpoon(); return; }
            if (!_correction.TryReadInputPosition(peer, out _) || !GuestStateCurrent(peer, state)) { HideGuestHarpoon(); return; }
            if (_harpoonDisplay != null && (_harpoonDisplay.SceneEpoch != state.SceneEpoch || _harpoonDisplay.ActorRevision != frame.ActorRevision))
            { RetireGuestHarpoon(); if (Failed) return; }
            if (_harpoonDisplay == null) _harpoonDisplay = new RemoteEmployeeHarpoonVisual(_thread, _logger);
            PlayerCharacter displayedPlayer = _local.Player;
            if (ReferenceEquals(displayedPlayer, null) || !GuestStateCurrent(peer, state)) { HideGuestHarpoon(); return; }
            GameObject displayedRoot = displayedPlayer.gameObject;
            if (ReferenceEquals(displayedRoot, null) || !GuestStateCurrent(peer, state)) { HideGuestHarpoon(); return; }
            Scene scene = displayedRoot.scene;
            if (scene.handle != _local.SceneHandle) { HideGuestHarpoon(); return; }
            if (!GuestStateCurrent(peer, state) || !_correction.TryReadInputPosition(peer, out _)) { HideGuestHarpoon(); return; }
            RemoteEmployeeHarpoonVisual visual = _harpoonDisplay;
            visual.Show(frame, scene, () => GuestStateCurrent(peer, state) && ReferenceEquals(_receivedState.Frame, frame));
            HarpoonStatus = visual.Status;
            if (visual.Failed || Failed) RetireGuestHarpoon();
        }

        private void HideGuestHarpoon()
        {
            _harpoonDisplay?.Hide();
            if (_harpoonDisplay?.Failed ?? false) { _failed = true; RetireGuestHarpoon(); }
        }
        private void RetireGuestHarpoon()
        {
            RemoteEmployeeHarpoonVisual visual = _harpoonDisplay;
            if (visual == null) return;
            visual.Dispose();
            if (visual.Failed) _failed = true;
            else if (visual.RetirementEntered && ReferenceEquals(_harpoonDisplay, visual)) _harpoonDisplay = null;
        }

        private void RetireProjectile(string reason)
        {
            _damage?.Stop(reason);
            if (_projectile != null)
            {
                _projectile.Stop(reason); _projectile.Dispose();
                if (_projectile.Failed || !_projectile.CleanupVerified) _failed = true;
            }
            _projectile = null; _damage = null;
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
            if (_correction?.Failed ?? false) { _failed = true; RetireGuestHarpoon(); Status = "GuestCorrectionFailed"; }
            if (!Enabled || Failed || Environment.CurrentManagedThreadId != _thread) return false;
            if (_busy) { Fault(new InvalidOperationException("Reentrant crew update.")); return false; }
            _busy = true; return true;
        }
        private static bool Down(KeyCode a, KeyCode b) => Input.GetKey(a) || Input.GetKey(b);
        private void RetireBody(string reason)
        {
            RetireGuestHarpoon();
            if (_harpoon != null)
            {
                _harpoonInputFloor = _harpoon.HighestInputSequence; _harpoonPacketFloor = _harpoon.HighestPacketSequence;
                _harpoonShotFloor = _harpoon.HighestShotId; _harpoon.Stop(reason);
            }
            RetireProjectile(reason); _harpoon = null;
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
            RetireGuestHarpoon();
            _harpoonInputFloor = _harpoonPacketFloor = _harpoonShotFloor = 0;
            _harpoonPosition = NVector3.Zero; _harpoonDirection = NVector2.Zero;
            _previousHP = null; _previousOxygen = null; _receivedState = null; _correction = null;
            _nextInput = 0; _guestActor = 0; _guestState = 0; _guestPacket = 0; _guestInputActor = 0;
            _lastPublishedButtons = CrewButtons.None;
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
