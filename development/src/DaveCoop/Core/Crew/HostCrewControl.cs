using System;
using System.Numerics;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.Session;

namespace DaveCoop.Core.Crew
{
    public sealed class HostCrewProfile
    {
        public float MaxSpeed { get; }
        public float BoostMultiplier { get; }
        public float MaxHP { get; }
        public float MaxOxygen { get; }
        public float OxygenPerSecond { get; }
        public float BoostOxygenPerSecond { get; }
        public float CapacityKg { get; }
        public long LoadoutRevision { get; }

        public HostCrewProfile(float maxSpeed = 3, float boostMultiplier = 1.5f, float maxHp = 100,
            float maxOxygen = 100, float oxygenPerSecond = 1, float boostOxygenPerSecond = 2,
            float capacityKg = 20, long loadoutRevision = 1)
        {
            if (!float.IsFinite(maxSpeed) || maxSpeed <= 0 || maxSpeed > 20 ||
                !float.IsFinite(boostMultiplier) || boostMultiplier < 1 || boostMultiplier > 3 ||
                !Positive(maxHp) || !Positive(maxOxygen) || !Positive(capacityKg) ||
                !Rate(oxygenPerSecond) || !Rate(boostOxygenPerSecond) || loadoutRevision < 1)
                throw new ArgumentException("Invalid host crew profile.");
            MaxSpeed = maxSpeed; BoostMultiplier = boostMultiplier; MaxHP = maxHp; MaxOxygen = maxOxygen;
            OxygenPerSecond = oxygenPerSecond; BoostOxygenPerSecond = boostOxygenPerSecond;
            CapacityKg = capacityKg; LoadoutRevision = loadoutRevision;
        }
        private static bool Positive(float value) => float.IsFinite(value) && value > 0 && value <= 1000000;
        private static bool Rate(float value) => float.IsFinite(value) && value >= 0 && value <= 1000;
    }

    // A command for the host's physics body, not an integrated pose or a hit.
    public sealed class CrewMovementPlan
    {
        public Vector2 RequestedVelocity { get; }
        public Vector2 Aim { get; }
        public CrewButtons PressedButtons { get; }
        public bool Boosting { get; }
        public bool CanMove { get; }
        internal CrewMovementPlan(Vector2 velocity, Vector2 aim, CrewButtons pressed, bool boosting, bool canMove)
        { RequestedVelocity = velocity; Aim = aim; PressedButtons = pressed; Boosting = boosting; CanMove = canMove; }
    }

    // One host-minted actor, tied to one actual body and scene by its caller.
    // The caller must freshly validate the real peer/body before and after
    // applying RequestedVelocity, then report actual physics readback here.
    public sealed class HostCrewControl
    {
        public const double InputStaleSeconds = 0.25;
        public const float MaxFixedStepSeconds = 0.1f;
        private readonly int _threadId;
        private ReceivedCrewInput _input;
        private CrewButtons _heldButtons, _pressedButtons;
        private Vector2 _aim = Vector2.UnitX;
        private Vector3 _position;
        private Vector2 _velocity;
        private bool _bodyActive, _stopped;
        private double _lastClock = -1;
        private long _stateRevision;
        public string RoomId { get; }
        public string MemberId { get; }
        public long SceneEpoch { get; }
        public string SceneKey { get; }
        public long ActorRevision { get; }
        public HostCrewProfile Profile { get; }
        public long HighestInputSequence { get; private set; }
        public long HighestPacketSequence { get; private set; }
        public float HP { get; private set; }
        public float Oxygen { get; private set; }
        public bool Alive => HP > 0;
        public bool Active => !_stopped && _bodyActive && Alive;
        public string Status { get; private set; } = "WaitingForBody";

        public HostCrewControl(string roomId, string memberId, long sceneEpoch, string sceneKey,
            long actorRevision, HostCrewProfile profile, Vector3 actualPosition, float? initialHp = null, float? initialOxygen = null)
        {
            if (!Guid.TryParse(roomId, out Guid room) || room == Guid.Empty ||
                !Guid.TryParse(memberId, out Guid member) || member == Guid.Empty || profile == null ||
                !CrewFrames.PositionValid(actualPosition)) throw new ArgumentException("Invalid host crew binding.");
            CrewFrames.Validate(new CrewInputFrame
            { PlayerId = 2, SceneEpoch = sceneEpoch, SceneKey = sceneKey, ActorRevision = actorRevision, InputSequence = 1 });
            float hp = initialHp ?? profile.MaxHP, oxygen = initialOxygen ?? profile.MaxOxygen;
            if (!float.IsFinite(hp) || hp < 0 || hp > profile.MaxHP ||
                !float.IsFinite(oxygen) || oxygen < 0 || oxygen > profile.MaxOxygen)
                throw new ArgumentException("Invalid inherited crew survival state.");
            RoomId = roomId; MemberId = memberId; SceneEpoch = sceneEpoch; SceneKey = sceneKey;
            ActorRevision = actorRevision; Profile = profile; _position = actualPosition;
            HP = hp; Oxygen = oxygen; _threadId = Environment.CurrentManagedThreadId;
        }

        public bool TryAccept(ReceivedCrewInput receipt, SessionSnapshot session, double hostNow)
        {
            if (!CreatorThread()) return false;
            if (!SessionCurrent(session)) { Neutralize("SessionUnavailable"); return false; }
            if (!Clock(hostNow)) { Neutralize("InvalidHostClock"); return false; }
            ReceivedCrewInput owned = CrewFrames.Copy(receipt);
            try { CrewFrames.Validate(owned); }
            catch (ProtocolException) { Neutralize("InvalidInput"); return false; }
            if (owned.RoomId != RoomId || owned.Frame.SceneEpoch != SceneEpoch || owned.Frame.SceneKey != SceneKey ||
                owned.Frame.ActorRevision != ActorRevision)
            { Neutralize("InputIdentityMismatch"); return false; }
            if (owned.Frame.InputSequence <= HighestInputSequence || owned.PacketSequence <= HighestPacketSequence)
            { Status = "InputReplay"; return false; }
            // Consume a valid new identity even when delivery was too late;
            // clearing or pausing cannot make that drained input fresh again.
            HighestInputSequence = owned.Frame.InputSequence; HighestPacketSequence = owned.PacketSequence;
            if (!Fresh(owned.ReceivedAt, hostNow) || !Active)
            { Neutralize(!Active ? "BodyUnavailable" : "InputStale"); return false; }
            _pressedButtons |= owned.Frame.Buttons & ~_heldButtons;
            _heldButtons = owned.Frame.Buttons;
            if (owned.Frame.AimX != 0 || owned.Frame.AimY != 0)
                _aim = Normalize(new Vector2(owned.Frame.AimX, owned.Frame.AimY));
            _input = owned; Status = "InputCurrent"; return true;
        }

        public CrewMovementPlan Step(SessionSnapshot session, double hostNow, float fixedDelta,
            Vector3 actualPosition, Vector2 actualVelocity, bool bodyActive)
        {
            if (!CreatorThread()) return Idle();
            if (!float.IsFinite(fixedDelta) || fixedDelta <= 0 || fixedDelta > MaxFixedStepSeconds)
                throw new ArgumentOutOfRangeException(nameof(fixedDelta));
            if (!ObserveBody(actualPosition, actualVelocity, bodyActive)) return Idle();
            if (!SessionCurrent(session)) { Neutralize("SessionUnavailable"); return Idle(); }
            if (!Clock(hostNow)) { Neutralize("InvalidHostClock"); return Idle(); }
            if (!Active) { Neutralize("BodyUnavailable"); return Idle(); }
            bool fresh = _input != null && Fresh(_input.ReceivedAt, hostNow);
            if (!fresh) Neutralize("InputStale");
            Vector2 move = fresh ? new Vector2(_input.Frame.MoveX, _input.Frame.MoveY) : Vector2.Zero;
            if (move.LengthSquared() > 1) move = Normalize(move);
            bool boost = fresh && (_heldButtons & CrewButtons.Boost) != 0 && move != Vector2.Zero && Oxygen > 0;
            double drain = ((double)Profile.OxygenPerSecond + (boost ? Profile.BoostOxygenPerSecond : 0)) * fixedDelta;
            Oxygen = (float)Math.Max(0, Oxygen - drain);
            CrewButtons pressed = fresh ? _pressedButtons : CrewButtons.None;
            _pressedButtons = CrewButtons.None;
            // Fire/recall/interact edges are retained diagnostics only. This
            // movement path never invokes a weapon, pickup or cargo operation.
            return new CrewMovementPlan(move * Profile.MaxSpeed * (boost ? Profile.BoostMultiplier : 1),
                _aim, pressed, boost, true);
        }

        public bool ObserveBody(Vector3 actualPosition, Vector2 actualVelocity, bool bodyActive)
        {
            if (!CreatorThread() || _stopped) return false;
            if (!CrewFrames.PositionValid(actualPosition) || !CrewFrames.VelocityValid(actualVelocity))
            { _bodyActive = false; Neutralize("InvalidBodyReadback"); return false; }
            _position = actualPosition; _velocity = actualVelocity; _bodyActive = bodyActive;
            if (!Active) Neutralize("BodyUnavailable");
            return true;
        }

        public void ApplyDamage(float amount)
        {
            RequireThread();
            if (!float.IsFinite(amount) || amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (_stopped || !Alive) return;
            HP = (float)Math.Max(0, (double)HP - amount);
            if (!Alive) Neutralize("Dead");
        }

        public CrewActorState CaptureState()
        {
            RequireThread();
            if (_stateRevision == long.MaxValue) throw new InvalidOperationException("Crew state revision exhausted.");
            var frame = new CrewActorState
            {
                PlayerId = 2, SceneEpoch = SceneEpoch, SceneKey = SceneKey, ActorRevision = ActorRevision,
                StateRevision = ++_stateRevision, LastInputSequence = HighestInputSequence,
                Position = _position, Velocity = _velocity, HP = HP, MaxHP = Profile.MaxHP,
                Oxygen = Oxygen, MaxOxygen = Profile.MaxOxygen, Alive = Alive, Active = Active,
                LoadoutRevision = Profile.LoadoutRevision, CapacityKg = Profile.CapacityKg,
                BagWeightKg = null, HasConfirmedCargoWeight = false
            };
            CrewFrames.Validate(frame); return frame;
        }

        public void Neutralize(string reason = "Neutralized")
        {
            RequireThread(); _input = null; _heldButtons = CrewButtons.None; _pressedButtons = CrewButtons.None;
            Status = reason ?? "Neutralized";
        }

        public void Stop(string reason = "Stopped")
        { RequireThread(); _stopped = true; _bodyActive = false; Neutralize(reason); }

        private bool SessionCurrent(SessionSnapshot session) => !_stopped && session != null &&
            session.Role == SessionRole.Host && session.Phase == SessionPhase.Ready && session.RoomId == RoomId &&
            session.LocalPlayerId == 1 && session.RemotePlayerId == 2 && session.LocalUsesCrewActor && session.RemoteUsesCrewActor &&
            session.SceneEpoch == SceneEpoch && session.SceneKey == SceneKey;
        private bool Clock(double now)
        {
            if (!double.IsFinite(now) || now < 0 || now < _lastClock) return false;
            _lastClock = now; return true;
        }
        private static bool Fresh(double at, double now) => double.IsFinite(at) && at >= 0 && now >= at && now - at <= InputStaleSeconds;
        private static Vector2 Normalize(Vector2 value)
        {
            double length = Math.Sqrt((double)value.X * value.X + (double)value.Y * value.Y);
            return length > 0 ? new Vector2((float)(value.X / length), (float)(value.Y / length)) : Vector2.Zero;
        }
        private CrewMovementPlan Idle() => new CrewMovementPlan(Vector2.Zero, _aim, CrewButtons.None, false, false);
        private bool CreatorThread() => Environment.CurrentManagedThreadId == _threadId;
        private void RequireThread()
        { if (!CreatorThread()) throw new InvalidOperationException("Crew control requires its creator thread."); }
    }
}
