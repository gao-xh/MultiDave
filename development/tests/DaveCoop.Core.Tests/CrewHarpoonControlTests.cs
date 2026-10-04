using System;
using System.Numerics;
using System.Threading.Tasks;
using DaveCoop.Core.Crew;
using DaveCoop.Core.Session;

internal static class CrewHarpoonControlTests
{
    internal static void OrderedFireEdgesUseFrozenAimAndHostBodyOrigin()
    {
        var f = new Fixture();
        ReceivedCrewInput first = f.Input(1, 1, 1, CrewButtons.Fire, 0, -1);
        Assert(f.Feed(first, 1), "first bound edge rejected");
        first.Frame.AimY = 1; first.Frame.Buttons = CrewButtons.None;
        first.RoomId = Guid.NewGuid().ToString("N");
        Assert(f.Feed(f.Input(2, 2, 1.01, CrewButtons.Fire), 1.01), "held input rejected");
        Assert(f.Feed(f.Input(3, 3, 1.02), 1.02), "release input rejected");
        Assert(f.Feed(f.Input(4, 4, 1.03, CrewButtons.Fire, -1, 0), 1.03), "second distinct edge rejected");
        Assert(f.Weapon.PendingCount == 2, "two press/release cycles were reduced to one OR bit");
        var actual = new Vector3(12, -30, 0);
        Assert(f.Take(1.03, actual, out CrewHarpoonCommand shot) && shot.InputSequence == 1 &&
            shot.PacketSequence == 1 && shot.Origin == actual && shot.Direction == new Vector2(0, -1),
            "shot used caller mutation, latest aim, or a model/client pose instead of the edge and actual body");
        Assert(ReferenceEquals(shot.Profile, f.Weapon.Profile) && shot.ActorRevision == 7 &&
            shot.RoomId == f.Session.RoomId && shot.MemberId == f.Crew.MemberId,
            "shot lost its fixed actor/member/profile binding");
        Assert(!f.Take(1.04, actual, out _) && f.Weapon.PendingCount == 0 &&
            f.Weapon.RejectedFireEdges == 1 && f.Weapon.ShotsCreated == 1,
            "second edge was merged, retained for a later automatic shot, or spawned another active head");
        Assert(!f.Weapon.ObserveProjectile(shot.ShotId, f.Session, 1.04, actual, false), "ended readback remained flying");
        CrewHarpoonCommand next = null;
        Assert(f.Feed(f.Input(5, 5, 1.3), 1.3) &&
            f.Feed(f.Input(6, 6, 1.31, CrewButtons.Fire, 1, 1), 1.31) && f.Take(1.31, actual, out next),
            "a genuinely new post-cooldown edge could not shoot");
        Assert(next.ShotId == shot.ShotId + 1 && next.InputSequence == 6 &&
            Close(next.Direction.X, .70710677f) && Close(next.Direction.Y, .70710677f),
            "new edge did not retain its normalized own aim or monotonic shot identity");
    }

    internal static void HoldingAndRecallEdgesConsumeAtMostOneFlyingShot()
    {
        var f = new Fixture();
        CrewHarpoonCommand shot = null, recall = null;
        Assert(f.Feed(f.Input(1, 1, 1, CrewButtons.Fire), 1) && f.Take(1, Vector3.Zero, out shot), "first shot missing");
        Assert(f.Feed(f.Input(2, 2, 1.01, CrewButtons.Fire), 1.01) && f.Weapon.PendingCount == 0,
            "held fire generated another edge");
        Assert(f.Feed(f.Input(3, 3, 1.02, CrewButtons.Fire | CrewButtons.Recall), 1.02) &&
            f.Take(1.02, new Vector3(1, 0, 0), out recall) && recall.Kind == CrewHarpoonCommandKind.Recall &&
            recall.ShotId == shot.ShotId && f.Weapon.Phase == CrewHarpoonFlightPhase.Recalling,
            "recall did not consume the fixed active shot once");
        Assert(f.Feed(f.Input(4, 4, 1.03, CrewButtons.Fire | CrewButtons.Recall), 1.03) &&
            !f.Take(1.03, Vector3.Zero, out _), "held recall replayed its command");
        Assert(!f.Weapon.TryHit(new CrewHarpoonCollision(shot, 1, 1, 22, Vector3.Zero), f.Session, 1.03, out _),
            "returning head dispatched damage");
        Assert(f.Feed(f.Input(5, 5, 1.04), 1.04) && f.Feed(f.Input(6, 6, 1.05, CrewButtons.Fire), 1.05),
            "new controls rejected during recall");
        Assert(f.Weapon.PendingCount == 0 && f.Weapon.RejectedFireEdges == 1, "new fire waited for an active head to return");
        f.Weapon.ObserveProjectile(shot.ShotId, f.Session, 1.05, Vector3.Zero, false);
        Assert(f.Feed(f.Input(7, 7, 1.4, CrewButtons.Fire), 1.4) && !f.Take(1.4, Vector3.Zero, out _),
            "held rejected fire fired automatically after the projectile ended");
        Assert(f.Feed(f.Input(8, 8, 1.41), 1.41) &&
            f.Feed(f.Input(9, 9, 1.42, CrewButtons.Fire | CrewButtons.Recall), 1.42) &&
            !f.Take(1.42, Vector3.Zero, out _) && f.Weapon.ShotsCreated == 1,
            "simultaneous recall/fire was allowed to create a replacement head");
    }

    internal static void CooldownAndActiveRejectionsCannotBecomeDelayedShots()
    {
        var f = new Fixture(new HostHarpoonProfile(cooldownSeconds: .1));
        Assert(f.Feed(f.Input(1, 1, 1, CrewButtons.Fire), 1) && f.Feed(f.Input(2, 2, 1.01), 1.01) &&
            f.Feed(f.Input(3, 3, 1.02, CrewButtons.Fire), 1.02), "initial burst rejected");
        Assert(f.Take(1.02, Vector3.Zero, out var shot), "first burst edge missing");
        f.Weapon.ObserveProjectile(shot.ShotId, f.Session, 1.03, Vector3.Zero, false);
        Assert(!f.Take(1.13, Vector3.Zero, out _) && f.Weapon.PendingCount == 0 && f.Weapon.RejectedFireEdges == 1,
            "earlier queued edge waited for the first shot's cooldown to finish");
        CrewHarpoonCommand second = null;
        Assert(f.Feed(f.Input(4, 4, 1.14), 1.14) && f.Feed(f.Input(5, 5, 1.15, CrewButtons.Fire), 1.15) &&
            f.Take(1.15, Vector3.Zero, out second), "fresh new edge after cooldown did not shoot");
        f.Weapon.ObserveProjectile(second.ShotId, f.Session, 1.16, Vector3.Zero, false);
        Assert(f.Feed(f.Input(6, 6, 1.17), 1.17) && f.Feed(f.Input(7, 7, 1.18, CrewButtons.Fire), 1.18),
            "source-valid cooldown rejection was confused with invalid input");
        Assert(f.Weapon.PendingCount == 0 && !f.Take(1.3, Vector3.Zero, out _) && f.Weapon.ShotsCreated == 2,
            "rejected fresh input was deferred instead of consumed");
    }

    internal static void PendingReceiptDeadlineAndInvalidClockDiscardWork()
    {
        var atBoundary = new Fixture();
        Assert(atBoundary.Feed(atBoundary.Input(1, 1, 1, CrewButtons.Fire), 1) &&
            atBoundary.Take(1.25, Vector3.Zero, out _), "exact quarter-second receipt deadline was incorrectly stale");
        var late = new Fixture();
        Assert(late.Feed(late.Input(1, 1, 1, CrewButtons.Fire), 1) && !late.Take(1.250001, Vector3.Zero, out _) &&
            late.Weapon.PendingCount == 0 && late.Weapon.HighestInputSequence == 1 && late.Weapon.DiscardedIntents == 1,
            "stale input remained queued or lost its high-water mark");
        Assert(!late.Crew.TryAccept(late.Input(1, 2, 1.26, CrewButtons.Fire), late.Session, 1.26) &&
            !late.Take(1.3, Vector3.Zero, out _), "expired edge was rebound to a new receive timestamp");
        var clock = new Fixture();
        Assert(clock.Feed(clock.Input(1, 1, 2, CrewButtons.Fire), 2), "current edge rejected");
        Assert(!clock.Take(1.9, Vector3.Zero, out _) && clock.Weapon.PendingCount == 0 &&
            clock.Weapon.HighestInputSequence == 1, "clock rewind dispatched or forgot an intent");
        Assert(!clock.Take(double.NaN, Vector3.Zero, out _) && !clock.Take(double.PositiveInfinity, Vector3.Zero, out _),
            "nonfinite clock dispatched a shot");
    }

    internal static void PausedDeadAndReplacedSourcesCannotResurrectIntents()
    {
        var f = new Fixture();
        Assert(f.Feed(f.Input(1, 1, 1, CrewButtons.Fire), 1), "initial edge missing");
        f.Session.Phase = SessionPhase.WaitingForScene;
        Assert(!f.Take(1.01, Vector3.Zero, out _) && f.Weapon.PendingCount == 0, "pause dispatched pending input");
        f.Session.Phase = SessionPhase.Ready;
        Assert(f.Feed(f.Input(2, 2, 1.02, CrewButtons.Fire), 1.02) && !f.Take(1.02, Vector3.Zero, out _),
            "held fire from before pause was treated as a new press");
        Assert(f.Feed(f.Input(3, 3, 1.03), 1.03) && f.Feed(f.Input(4, 4, 1.04, CrewButtons.Fire), 1.04) &&
            f.Take(1.04, Vector3.Zero, out _), "fresh release/press could not rearm");
        f.Session.CrewActorRevision++;
        Assert(!f.Weapon.CheckCurrent(f.Session, 1.05) && f.Weapon.ActiveShotId == 0,
            "same-scene replacement actor borrowed the old projectile");
        var dead = new Fixture();
        CrewHarpoonCommand shot = null;
        Assert(dead.Feed(dead.Input(1, 1, 1, CrewButtons.Fire), 1) && dead.Take(1, Vector3.Zero, out shot), "dead fixture shot missing");
        dead.Crew.ApplyDamage(100);
        Assert(!dead.Weapon.ObserveProjectile(shot.ShotId, dead.Session, 1.01, Vector3.One, true) &&
            dead.Weapon.ActiveShotId == 0 && !dead.Take(1.02, Vector3.Zero, out _), "dead employee kept an active weapon");
        var source = new Fixture();
        Assert(source.Feed(source.Input(1, 1, 1, CrewButtons.Fire), 1), "source fixture edge missing");
        source.Session.RemoteUsesCrewActor = false;
        Assert(!source.Take(1.01, Vector3.Zero, out _) && source.Weapon.PendingCount == 0, "one-sided opt-out preserved weapon intent");
        source.Session.RemoteUsesCrewActor = true; source.Session.Phase = SessionPhase.Closed;
        Assert(!source.Take(1.02, Vector3.Zero, out _) && source.Weapon.HighestInputSequence == 1,
            "disconnect reopened an old edge or reset its sequence");
    }

    internal static void CollisionConsumesExactIssuedShotOnceWithoutReward()
    {
        var f = new Fixture(new HostHarpoonProfile(damage: 10));
        var other = new Fixture(new HostHarpoonProfile(damage: 99));
        CrewHarpoonCommand shot = null, foreign = null;
        Assert(f.Feed(f.Input(1, 1, 1, CrewButtons.Fire), 1) && f.Take(1, Vector3.Zero, out shot) &&
            other.Feed(other.Input(1, 1, 1, CrewButtons.Fire), 1) && other.Take(1, Vector3.Zero, out foreign),
            "initial damage fixtures failed");
        Assert(shot.ShotId == foreign.ShotId, "fixture did not exercise colliding numeric identities");
        Assert(!f.Weapon.TryHit(new CrewHarpoonCollision(foreign, 7, 2, 100, Vector3.One), f.Session, 1.01, out _),
            "another control's equal numeric ShotId acquired this shot");
        Assert(!f.Weapon.TryHit(new CrewHarpoonCollision(shot, 7, 2, 100, new Vector3(50, 0, 0)), f.Session, 1.02, out _),
            "distant collision exceeded the host-approved shot profile");
        var collision = new CrewHarpoonCollision(shot, 7, 2, 100, new Vector3(2, 0, 0));
        Assert(f.Weapon.TryHit(collision, f.Session, 1.03, out CrewHarpoonHit hit) && hit.ShotId == shot.ShotId &&
            hit.Damage == 10 && ReferenceEquals(hit.Collision, collision) && f.Weapon.ActiveShotId == 0 &&
            !hit.CaptureConfirmed && !hit.RewardsGranted, "once-consumed damage intent became capture/reward or borrowed damage");
        Assert(!f.Weapon.TryHit(collision, f.Session, 1.04, out _) &&
            !f.Weapon.TryHit(new CrewHarpoonCollision(shot, 8, 3, 101, Vector3.One), f.Session, 1.04, out _) &&
            f.Weapon.HitsConsumed == 1, "same shot retried native damage after a first intent, including a different target");
        Assert(f.Crew.HP == 100 && f.Crew.CaptureState().BagWeightKg == null,
            "collision intent changed crew HP or invented capture inventory");
    }

    internal static void NativeProjectileReadbackBoundsActualTravelWithoutPoseIntegration()
    {
        var f = new Fixture(new HostHarpoonProfile(maxDistance: 5));
        CrewHarpoonCommand shot = null;
        Assert(f.Feed(f.Input(1, 1, 1, CrewButtons.Fire), 1) && f.Take(1, new Vector3(10, 0, 0), out shot), "shot missing");
        Assert(f.Weapon.ActualProjectilePosition == new Vector3(10, 0, 0) && f.Weapon.TravelledDistance == 0,
            "model integrated a projectile before actual readback");
        Assert(f.Weapon.CheckCurrent(f.Session, 1.1) && f.Weapon.ActualProjectilePosition == shot.Origin,
            "advancing the clock fabricated a moved pose");
        Assert(f.Weapon.ObserveProjectile(shot.ShotId, f.Session, 1.11, new Vector3(13, 0, 0), true) &&
            f.Weapon.TravelledDistance == 3 && f.Weapon.ActualProjectilePosition == new Vector3(13, 0, 0), "actual path readback was not retained");
        Assert(f.Weapon.ObserveProjectile(shot.ShotId, f.Session, 1.12, new Vector3(11, 0, 0), true) &&
            f.Weapon.TravelledDistance == 5, "out-and-back path was reduced to origin distance or exact bound prematurely retired");
        Assert(!f.Weapon.ObserveProjectile(shot.ShotId, f.Session, 1.13, new Vector3(12, 0, 0), true) &&
            f.Weapon.ActiveShotId == 0 && f.Weapon.TravelledDistance == 6, "actual cumulative range did not retire the head");
        Assert(!f.Weapon.ObserveProjectile(shot.ShotId, f.Session, 1.14, shot.Origin, true), "late old head revived its shot");
        var invalid = new Fixture();
        CrewHarpoonCommand current = null;
        Assert(invalid.Feed(invalid.Input(1, 1, 1, CrewButtons.Fire), 1) && invalid.Take(1, Vector3.Zero, out current), "invalid fixture missing");
        Assert(!invalid.Weapon.ObserveProjectile(current.ShotId, invalid.Session, 1.01, new Vector3(float.NaN, 0, 0), true) &&
            invalid.Weapon.ActiveShotId == 0, "invalid native readback left the weapon usable");
    }

    internal static void SequenceFloorsSurviveNeutralizationAndSceneReplacement()
    {
        var f = new Fixture();
        CrewHarpoonCommand old = null, fresh = null;
        Assert(f.Feed(f.Input(1, 3, 1, CrewButtons.Fire), 1) && f.Take(1, Vector3.Zero, out old), "first shot missing");
        f.Weapon.Neutralize("LocalPause");
        Assert(f.Weapon.HighestInputSequence == 1 && f.Weapon.HighestPacketSequence == 3 && f.Weapon.HighestShotId == 1 &&
            !f.Weapon.TryAccept(f.Input(1, 3, 1, CrewButtons.Fire), f.Session, 1.01), "neutralization cleared an input/packet/shot fence");
        f.Weapon.Stop("SceneReplaced");
        f.Session.SceneEpoch = 4; f.Session.CrewActorRevision = 8;
        var crew = new HostCrewControl(f.Session.RoomId, f.Crew.MemberId, 4, "dive", 8, f.Crew.Profile, Vector3.One);
        crew.ObserveBody(Vector3.One, Vector2.Zero, true);
        var replacement = new HostHarpoonControl(crew, f.Weapon.Profile,
            f.Weapon.HighestInputSequence, f.Weapon.HighestPacketSequence, f.Weapon.HighestShotId);
        ReceivedCrewInput replay = f.Input(1, 3, 2, CrewButtons.Fire); replay.Frame.SceneEpoch = 4; replay.Frame.ActorRevision = 8;
        Assert(crew.TryAccept(replay, f.Session, 2) && !replacement.TryAccept(replay, f.Session, 2), "new actor recycled a room sequence floor");
        var next = f.Input(2, 4, 2.01, CrewButtons.Fire); next.Frame.SceneEpoch = 4; next.Frame.ActorRevision = 8;
        Assert(crew.TryAccept(next, f.Session, 2.01) && replacement.TryAccept(next, f.Session, 2.01) &&
            replacement.TryTakeCommand(f.Session, 2.01, Vector3.One, out fresh) && fresh.ShotId == 2,
            "room handoff reused a shot ID or blocked genuinely new actor input");
        Assert(!replacement.TryHit(new CrewHarpoonCollision(old, 7, 2, 100, Vector3.One), f.Session, 2.02, out _),
            "old scene's minted command acquired a replacement actor shot");
    }

    internal static void BoundedIntentQueueAndCreatorThreadCannotDispatchMoreWork()
    {
        var f = new Fixture();
        for (int index = 0; index < HostHarpoonControl.MaxPendingIntents; index++)
        {
            long sequence = index * 2 + 1;
            Assert(f.Feed(f.Input(sequence, sequence, 1, CrewButtons.Fire), 1) &&
                f.Feed(f.Input(sequence + 1, sequence + 1, 1), 1), "bounded queue rejected before its stated limit");
        }
        Assert(f.Weapon.PendingCount == HostHarpoonControl.MaxPendingIntents, "queue limit fixture did not preserve distinct edges");
        long overflow = HostHarpoonControl.MaxPendingIntents * 2 + 1;
        Assert(!f.Feed(f.Input(overflow, overflow, 1, CrewButtons.Fire), 1) && f.Weapon.Stopped &&
            f.Weapon.PendingCount == 0 && f.Weapon.HighestInputSequence == overflow &&
            !f.Take(1.1, Vector3.Zero, out _), "overflow dropped one edge and later resumed old work");
        var thread = new Fixture();
        ReceivedCrewInput input = thread.Input(1, 1, 1, CrewButtons.Fire);
        Assert(thread.Crew.TryAccept(input, thread.Session, 1), "creator receipt rejected");
        bool accepted = true, taken = true;
        Task.Run(() =>
        {
            accepted = thread.Weapon.TryAccept(input, thread.Session, 1);
            taken = thread.Weapon.TryTakeCommand(thread.Session, 1, Vector3.Zero, out _);
        }).GetAwaiter().GetResult();
        Assert(!accepted && !taken && thread.Weapon.HighestInputSequence == 0 && thread.Weapon.PendingCount == 0,
            "worker thread consumed an input or minted a shot");
        Assert(thread.Weapon.TryAccept(input, thread.Session, 1) && thread.Take(1, Vector3.Zero, out _),
            "off-thread rejection destroyed creator-thread source work");
        thread.Weapon.Stop("NativeDispatchUnknown");
        Assert(thread.Weapon.ActiveShotId == 0 && !thread.Take(1.1, Vector3.Zero, out _) &&
            thread.Weapon.HighestShotId == 1, "unknown native dispatch was retried or forgot its consumed shot");
    }

    internal static void ProfilesAndSourceIdentitiesCannotAuthorizeMalformedShots()
    {
        Throws<ArgumentException>(() => new HostHarpoonProfile(speed: 0), "zero projectile speed accepted");
        Throws<ArgumentException>(() => new HostHarpoonProfile(maxDistance: float.NaN), "nonfinite range accepted");
        Throws<ArgumentException>(() => new HostHarpoonProfile(cooldownSeconds: -1), "negative cooldown accepted");
        Throws<ArgumentException>(() => new HostHarpoonProfile(radius: 3), "unbounded hit radius accepted");
        Throws<ArgumentException>(() => new HostHarpoonProfile(damage: float.PositiveInfinity), "infinite native damage intent accepted");
        var f = new Fixture();
        Assert(!f.Weapon.TryAccept(f.Input(1, 1, 1, CrewButtons.Fire), f.Session, 1) && f.Weapon.PendingCount == 0,
            "unaccepted wire input bypassed the actual actor consumer");
        Assert(f.Feed(f.Input(1, 1, 1.01), 1.01), "neutral rearm input rejected");
        Assert(f.Feed(f.Input(2, 2, 1.02, CrewButtons.Fire), 1.02) &&
            !f.Take(1.02, new Vector3(float.NaN, 0, 0), out _) && f.Weapon.PendingCount == 0,
            "invalid actual body readback became a shot origin");
        var exhausted = new Fixture(shotIdFloor: long.MaxValue);
        Assert(exhausted.Feed(exhausted.Input(1, 1, 1, CrewButtons.Fire), 1) &&
            !exhausted.Take(1, Vector3.Zero, out _) && exhausted.Weapon.Stopped && exhausted.Weapon.HighestShotId == long.MaxValue,
            "shot identity wrapped and reused a previous native identity");
        var hit = new Fixture();
        CrewHarpoonCommand shot = null;
        Assert(hit.Feed(hit.Input(1, 1, 1, CrewButtons.Fire), 1) && hit.Take(1, Vector3.Zero, out shot), "collision fixture shot missing");
        Throws<ArgumentException>(() => new CrewHarpoonCollision(shot, 0, 1, 100, Vector3.Zero), "unbound target accepted");
        Throws<ArgumentException>(() => new CrewHarpoonCollision(shot, 1, 0, 100, Vector3.Zero), "unknown source generation accepted");
        Throws<ArgumentException>(() => new CrewHarpoonCollision(shot, 1, 1, 0, Vector3.Zero), "unknown fish TID accepted");
        hit.Session.Role = SessionRole.Guest;
        Assert(!hit.Weapon.TryHit(new CrewHarpoonCollision(shot, 1, 1, 100, Vector3.Zero), hit.Session, 1.01, out _) &&
            hit.Weapon.ActiveShotId == 0, "guest role dispatched a host damage intent");
    }

    private sealed class Fixture
    {
        internal readonly SessionSnapshot Session;
        internal readonly HostCrewControl Crew;
        internal readonly HostHarpoonControl Weapon;
        internal Fixture(HostHarpoonProfile profile = null, long shotIdFloor = 0)
        {
            Session = new SessionSnapshot
            {
                RoomId = Guid.NewGuid().ToString("N"), Role = SessionRole.Host, Phase = SessionPhase.Ready,
                LocalPlayerId = 1, RemotePlayerId = 2, LocalUsesCrewActor = true, RemoteUsesCrewActor = true,
                SceneEpoch = 3, SceneKey = "dive", CrewActorRevision = 7
            };
            Crew = new HostCrewControl(Session.RoomId, Guid.NewGuid().ToString("N"), 3, "dive", 7,
                new HostCrewProfile(), new Vector3(2, -8, 0));
            Crew.ObserveBody(new Vector3(2, -8, 0), Vector2.Zero, true);
            Weapon = new HostHarpoonControl(Crew, profile ?? new HostHarpoonProfile(), shotIdFloor: shotIdFloor);
        }
        internal ReceivedCrewInput Input(long sequence, long packet, double at,
            CrewButtons buttons = CrewButtons.None, float aimX = 1, float aimY = 0) => new ReceivedCrewInput
        {
            RoomId = Session.RoomId, BoundPlayerId = 2, PacketSequence = packet, ReceivedAt = at,
            Frame = new CrewInputFrame
            { PlayerId = 2, SceneEpoch = 3, SceneKey = "dive", ActorRevision = 7, InputSequence = sequence,
                AimX = aimX, AimY = aimY, Buttons = buttons }
        };
        internal bool Feed(ReceivedCrewInput input, double now) =>
            Crew.TryAccept(input, Session, now) && Weapon.TryAccept(input, Session, now);
        internal bool Take(double now, Vector3 actual, out CrewHarpoonCommand command) =>
            Weapon.TryTakeCommand(Session, now, actual, out command);
    }

    private static bool Close(float a, float b) => Math.Abs(a - b) < .000001f;
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action, string message) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException(message); }
}
