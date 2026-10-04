using System;
using System.Collections.Generic;
using System.Numerics;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.Session;

namespace DaveCoop.Core.Crew
{
    // Host-approved Mod values. Damage is an intent amount, with no implicit
    // game weapon/item TID or native damage/capture implementation.
    public sealed class HostHarpoonProfile
    {
        public float Speed { get; }
        public float MaxDistance { get; }
        public double CooldownSeconds { get; }
        public float Radius { get; }
        public float Damage { get; }

        public HostHarpoonProfile(float speed = 25, float maxDistance = 20,
            double cooldownSeconds = .25, float radius = .1f, float damage = 0)
        {
            if (!Positive(speed, 200) || !Positive(maxDistance, 1000) || !Positive(radius, 2) ||
                !double.IsFinite(cooldownSeconds) || cooldownSeconds < 0 || cooldownSeconds > 60 ||
                !float.IsFinite(damage) || damage < 0 || damage > 100000)
                throw new ArgumentException("Invalid host harpoon profile.");
            Speed = speed; MaxDistance = maxDistance; CooldownSeconds = cooldownSeconds;
            Radius = radius; Damage = damage;
        }
        private static bool Positive(float value, float max) => float.IsFinite(value) && value > 0 && value <= max;
    }

    public enum CrewHarpoonCommandKind { Fire = 1, Recall = 2 }
    public enum CrewHarpoonFlightPhase { Idle = 1, Flying = 2, Recalling = 3 }

    // A single consumed input edge, minted before native dispatch. Its origin
    // is the supplied host body readback, never a client pose.
    public sealed class CrewHarpoonCommand
    {
        public CrewHarpoonCommandKind Kind { get; }
        public long ShotId { get; }
        public long InputSequence { get; }
        public long PacketSequence { get; }
        public double ReceivedAt { get; }
        public string RoomId { get; }
        public string MemberId { get; }
        public long SceneEpoch { get; }
        public string SceneKey { get; }
        public long ActorRevision { get; }
        public Vector3 Origin { get; }
        public Vector2 Direction { get; }
        public HostHarpoonProfile Profile { get; }
        internal CrewHarpoonCommand(CrewHarpoonCommandKind kind, long shotId, IntentSource input,
            HostCrewControl crew, Vector3 origin, Vector2 direction, HostHarpoonProfile profile)
        {
            Kind = kind; ShotId = shotId; InputSequence = input.InputSequence; PacketSequence = input.PacketSequence;
            ReceivedAt = input.ReceivedAt; RoomId = crew.RoomId; MemberId = crew.MemberId;
            SceneEpoch = crew.SceneEpoch; SceneKey = crew.SceneKey; ActorRevision = crew.ActorRevision;
            Origin = origin; Direction = direction; Profile = profile;
        }
    }

    // Supplied only by the production collision reader with the exact original
    // Fire command. Scalar identity does not itself prove a native collision.
    public sealed class CrewHarpoonCollision
    {
        public CrewHarpoonCommand Shot { get; }
        public long EntityId { get; }
        public long Generation { get; }
        public int DataTid { get; }
        public Vector3 Position { get; }
        public CrewHarpoonCollision(CrewHarpoonCommand shot, long entityId, long generation, int dataTid, Vector3 position)
        {
            if (shot == null || shot.Kind != CrewHarpoonCommandKind.Fire || entityId < 1 || generation < 1 ||
                dataTid < 1 || !CrewFrames.PositionValid(position))
                throw new ArgumentException("Invalid harpoon collision identity.");
            Shot = shot; EntityId = entityId; Generation = generation; DataTid = dataTid; Position = position;
        }
    }

    // Once-only damage intent. The actual native adapter still must validate
    // the target and apply damage; this result is neither capture nor reward.
    public sealed class CrewHarpoonHit
    {
        public CrewHarpoonCollision Collision { get; }
        public long ShotId => Collision.Shot.ShotId;
        public float Damage => Collision.Shot.Profile.Damage;
        public bool CaptureConfirmed => false;
        public bool RewardsGranted => false;
        internal CrewHarpoonHit(CrewHarpoonCollision collision) { Collision = collision; }
    }

    internal sealed class IntentSource
    {
        internal readonly long InputSequence, PacketSequence;
        internal readonly double ReceivedAt, AcceptedAt;
        internal readonly CrewHarpoonCommandKind Kind;
        internal readonly Vector2 Aim;
        internal IntentSource(ReceivedCrewInput receipt, double now, CrewHarpoonCommandKind kind, Vector2 aim)
        {
            InputSequence = receipt.Frame.InputSequence; PacketSequence = receipt.PacketSequence;
            ReceivedAt = receipt.ReceivedAt; AcceptedAt = now; Kind = kind; Aim = aim;
        }
    }

    // Independent ordered weapon consumer of the same approved input receipts.
    // It neither uses HostCrewControl.PressedButtons nor integrates a projectile
    // pose. Native source checks surround each command/read/hit in its caller.
    public sealed class HostHarpoonControl
    {
        public const int MaxPendingIntents = 32;
        public const double InputStaleSeconds = HostCrewControl.InputStaleSeconds;
        private readonly int _threadId;
        private readonly Queue<IntentSource> _pending = new Queue<IntentSource>();
        private readonly HostCrewControl _crew;
        private CrewButtons _held;
        private Vector2 _aim = Vector2.UnitX;
        private CrewHarpoonCommand _activeShot;
        private Vector3 _actualProjectilePosition;
        private double _lastClock = -1, _nextFireAt;
        private bool _stopped, _needsRelease;
        public HostCrewControl Crew => _crew;
        public HostHarpoonProfile Profile { get; }
        public long HighestInputSequence { get; private set; }
        public long HighestPacketSequence { get; private set; }
        public long HighestShotId { get; private set; }
        public long ActiveShotId => _activeShot?.ShotId ?? 0;
        public CrewHarpoonCommand ActiveShot => _activeShot;
        public CrewHarpoonFlightPhase Phase { get; private set; } = CrewHarpoonFlightPhase.Idle;
        public Vector3 ActualProjectilePosition => _actualProjectilePosition;
        public double TravelledDistance { get; private set; }
        public int PendingCount => _pending.Count;
        public bool Stopped => _stopped;
        public long ShotsCreated { get; private set; }
        public long HitsConsumed { get; private set; }
        public long RejectedFireEdges { get; private set; }
        public long DiscardedIntents { get; private set; }
        public string Status { get; private set; } = "WaitingForInput";

        // Floors are retained by the room controller across an actor/scene
        // replacement. Neutralize/Stop never lower any of these fences.
        public HostHarpoonControl(HostCrewControl crew, HostHarpoonProfile profile,
            long inputSequenceFloor = 0, long packetSequenceFloor = 0, long shotIdFloor = 0)
        {
            if (crew == null || profile == null || inputSequenceFloor < 0 || packetSequenceFloor < 0 || shotIdFloor < 0)
                throw new ArgumentException("Invalid harpoon control binding.");
            _crew = crew; Profile = profile; HighestInputSequence = inputSequenceFloor;
            HighestPacketSequence = packetSequenceFloor; HighestShotId = shotIdFloor;
            _threadId = Environment.CurrentManagedThreadId;
        }

        // Called immediately after Crew.TryAccept succeeds for this receipt.
        // A true result means the input was consumed, not that a shot was fired.
        public bool TryAccept(ReceivedCrewInput receipt, SessionSnapshot session, double hostNow)
        {
            if (!CreatorThread() || _stopped) return false;
            if (!CheckCurrent(session, hostNow)) return false;
            ReceivedCrewInput owned = CrewFrames.Copy(receipt);
            try { CrewFrames.Validate(owned); }
            catch (ProtocolException) { Neutralize("InvalidInput"); return false; }
            if (owned.RoomId != _crew.RoomId || owned.Frame.SceneEpoch != _crew.SceneEpoch ||
                owned.Frame.SceneKey != _crew.SceneKey || owned.Frame.ActorRevision != _crew.ActorRevision ||
                owned.Frame.InputSequence != _crew.HighestInputSequence || owned.PacketSequence != _crew.HighestPacketSequence)
            { Neutralize("InputNotAcceptedByCurrentActor"); return false; }
            if (owned.Frame.InputSequence <= HighestInputSequence || owned.PacketSequence <= HighestPacketSequence)
            { Status = "InputReplay"; return false; }
            HighestInputSequence = owned.Frame.InputSequence; HighestPacketSequence = owned.PacketSequence;
            if (!Fresh(owned.ReceivedAt, hostNow)) { Neutralize("InputStale"); return false; }
            CrewButtons buttons = owned.Frame.Buttons & (CrewButtons.Fire | CrewButtons.Recall);
            CrewButtons pressed = buttons & ~_held;
            _held = buttons;
            if (owned.Frame.AimX != 0 || owned.Frame.AimY != 0)
                _aim = Normalize(new Vector2(owned.Frame.AimX, owned.Frame.AimY));
            if (_needsRelease)
            {
                if (buttons == CrewButtons.None) _needsRelease = false;
                Status = _needsRelease ? "WaitingForRelease" : "InputRearmed";
                return true;
            }
            // If both rise in one frame, Recall consumes that frame's Fire.
            // Distinct input frames retain their own ordering and aim.
            if ((pressed & CrewButtons.Recall) != 0)
            {
                if ((pressed & CrewButtons.Fire) != 0) RejectedFireEdges = Increment(RejectedFireEdges);
                return Enqueue(new IntentSource(owned, hostNow, CrewHarpoonCommandKind.Recall, _aim));
            }
            if ((pressed & CrewButtons.Fire) != 0)
            {
                if (_activeShot != null || hostNow < _nextFireAt)
                {
                    RejectedFireEdges = Increment(RejectedFireEdges);
                    Status = _activeShot != null ? "FireWhileActiveConsumed" : "FireDuringCooldownConsumed";
                    return true;
                }
                return Enqueue(new IntentSource(owned, hostNow, CrewHarpoonCommandKind.Fire, _aim));
            }
            Status = "HeldOrReleased"; return true;
        }

        public bool TryTakeCommand(SessionSnapshot session, double hostNow, Vector3 actualBodyPosition,
            out CrewHarpoonCommand command)
        {
            command = null;
            if (!CreatorThread() || !CheckCurrent(session, hostNow)) return false;
            if (!CrewFrames.PositionValid(actualBodyPosition)) { Neutralize("InvalidBodyReadback"); return false; }
            while (_pending.Count > 0)
            {
                IntentSource intent = _pending.Dequeue();
                if (!Fresh(intent.ReceivedAt, hostNow))
                { DiscardedIntents = Increment(DiscardedIntents); Status = "ExpiredIntentConsumed"; continue; }
                if (intent.Kind == CrewHarpoonCommandKind.Recall)
                {
                    if (_activeShot == null || Phase == CrewHarpoonFlightPhase.Recalling)
                    { DiscardedIntents = Increment(DiscardedIntents); Status = "RecallWithoutFlyingShot"; continue; }
                    Phase = CrewHarpoonFlightPhase.Recalling;
                    command = new CrewHarpoonCommand(intent.Kind, _activeShot.ShotId, intent, _crew,
                        actualBodyPosition, _activeShot.Direction, Profile);
                    Status = "RecallConsumed"; return true;
                }
                // Check the original edge time as well as this dispatch time:
                // a press during cooldown must never wait for permission later.
                if (_activeShot != null || intent.AcceptedAt < _nextFireAt || hostNow < _nextFireAt)
                { RejectedFireEdges = Increment(RejectedFireEdges); Status = "FireIntentRejectedConsumed"; continue; }
                if (HighestShotId == long.MaxValue) { Stop("ShotIdExhausted"); return false; }
                command = new CrewHarpoonCommand(intent.Kind, ++HighestShotId, intent, _crew,
                    actualBodyPosition, intent.Aim, Profile);
                _activeShot = command; _actualProjectilePosition = actualBodyPosition;
                TravelledDistance = 0; Phase = CrewHarpoonFlightPhase.Flying;
                _nextFireAt = hostNow + Profile.CooldownSeconds;
                ShotsCreated = Increment(ShotsCreated); Status = "FireConsumed"; return true;
            }
            return false;
        }

        // Returns false when the actual projectile has ended/exceeded its
        // profile. The native owner handles retirement of its retained body.
        public bool ObserveProjectile(long shotId, SessionSnapshot session, double hostNow,
            Vector3 actualPosition, bool active)
        {
            if (!CreatorThread() || !CheckCurrent(session, hostNow) || _activeShot == null || shotId != _activeShot.ShotId)
                return false;
            if (!CrewFrames.PositionValid(actualPosition)) { Neutralize("InvalidProjectileReadback"); return false; }
            TravelledDistance += Distance(_actualProjectilePosition, actualPosition);
            _actualProjectilePosition = actualPosition;
            if (!active || TravelledDistance > Profile.MaxDistance)
            { EndShot(!active ? "ProjectileEnded" : "ProjectileRangeExceeded"); return false; }
            Status = "ProjectileReadback"; return true;
        }

        public bool TryHit(CrewHarpoonCollision collision, SessionSnapshot session, double hostNow,
            out CrewHarpoonHit hit)
        {
            hit = null;
            if (!CreatorThread() || !CheckCurrent(session, hostNow) || collision == null ||
                _activeShot == null || Phase != CrewHarpoonFlightPhase.Flying ||
                !ReferenceEquals(collision.Shot, _activeShot)) return false;
            if (Distance(collision.Shot.Origin, collision.Position) > (double)Profile.MaxDistance + Profile.Radius)
            { Status = "CollisionOutsideShotRange"; return false; }
            // Claim once before the caller can enter any native damage method.
            hit = new CrewHarpoonHit(collision); HitsConsumed = Increment(HitsConsumed);
            EndShot("HitIntentConsumed"); return true;
        }

        public bool CheckCurrent(SessionSnapshot session, double hostNow)
        {
            if (!CreatorThread() || _stopped) return false;
            if (session == null || session.Role != SessionRole.Host || session.Phase != SessionPhase.Ready ||
                session.RoomId != _crew.RoomId || session.LocalPlayerId != 1 || session.RemotePlayerId != 2 ||
                !session.LocalUsesCrewActor || !session.RemoteUsesCrewActor ||
                session.SceneEpoch != _crew.SceneEpoch || session.SceneKey != _crew.SceneKey ||
                session.CrewActorRevision != _crew.ActorRevision || !_crew.Active || !_crew.Alive)
            { Neutralize("ActorSourceUnavailable"); return false; }
            if (!double.IsFinite(hostNow) || hostNow < 0 || hostNow < _lastClock)
            { Neutralize("InvalidHostClock"); return false; }
            _lastClock = hostNow; return true;
        }

        public void Neutralize(string reason = "Neutralized")
        {
            RequireThread();
            DiscardedIntents = Add(DiscardedIntents, _pending.Count); _pending.Clear();
            _held = CrewButtons.None; _aim = Vector2.UnitX; _needsRelease = true;
            EndShot(reason ?? "Neutralized");
        }
        public void Stop(string reason = "Stopped")
        { RequireThread(); _stopped = true; Neutralize(reason); }

        private bool Enqueue(IntentSource intent)
        {
            if (_pending.Count >= MaxPendingIntents) { Stop("IntentQueueFull"); return false; }
            _pending.Enqueue(intent); Status = "IntentQueued"; return true;
        }
        private void EndShot(string reason)
        { _activeShot = null; Phase = CrewHarpoonFlightPhase.Idle; Status = reason; }
        private bool CreatorThread() => Environment.CurrentManagedThreadId == _threadId;
        private void RequireThread()
        { if (!CreatorThread()) throw new InvalidOperationException("Harpoon control requires its creator thread."); }
        private static bool Fresh(double at, double now) => now >= at && now - at <= InputStaleSeconds;
        private static Vector2 Normalize(Vector2 value)
        {
            double length = Math.Sqrt((double)value.X * value.X + (double)value.Y * value.Y);
            return new Vector2((float)(value.X / length), (float)(value.Y / length));
        }
        private static double Distance(Vector3 a, Vector3 b)
        {
            double x = (double)a.X - b.X, y = (double)a.Y - b.Y, z = (double)a.Z - b.Z;
            return Math.Sqrt(x * x + y * y + z * z);
        }
        private static long Increment(long value) => value == long.MaxValue ? value : value + 1;
        private static long Add(long value, int amount) => value > long.MaxValue - amount ? long.MaxValue : value + amount;
    }
}
