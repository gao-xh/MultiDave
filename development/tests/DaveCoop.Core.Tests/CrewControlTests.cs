using System;
using System.Numerics;
using System.Threading.Tasks;
using DaveCoop.Core.Crew;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.Session;

internal static class CrewControlTests
{
    internal static void BoundInputNormalizesMovementAndUsesOnlyHostBodyReadback()
    {
        var f = new Fixture();
        ReceivedCrewInput input = f.Input(1, 1, 10, 1, 1);
        input.Frame.AimX = 0; input.Frame.AimY = -1;
        Assert(f.Control.TryAccept(input, f.Session, 10), "bound input was rejected");
        input.RoomId = Guid.NewGuid().ToString("N"); input.Frame.MoveX = -1; input.Frame.MoveY = 0;
        input.Frame.AimY = 1;
        var actual = new Vector3(12, -7, 0);
        CrewMovementPlan plan = f.Control.Step(f.Session, 10.05, .05f, actual, new Vector2(.25f, -.5f), true);
        Assert(plan.CanMove && Close(plan.RequestedVelocity.X, 2.1213202f) &&
            Close(plan.RequestedVelocity.Y, 2.1213202f) && plan.RequestedVelocity.Length() <= 3.000001f,
            "diagonal axes exceeded host speed or a caller mutation changed the owned input");
        Assert(plan.Aim == new Vector2(0, -1), "aim was not frozen with its accepted input");
        CrewActorState state = f.Control.CaptureState();
        Assert(state.Position == actual && state.Velocity == new Vector2(.25f, -.5f) &&
            state.Position != actual + new Vector3(plan.RequestedVelocity * .05f, 0),
            "control integrated a guessed pose instead of reporting the actual body readback");
        Assert(state.LoadoutRevision == 1 && state.CapacityKg == 20 && state.BagWeightKg == null &&
            !state.HasConfirmedCargoWeight, "movement pretended to confirm a personal bag");
    }

    internal static void QueuedButtonEdgesSurviveReleaseAndAreConsumedExactlyOnce()
    {
        var f = new Fixture();
        Assert(f.Control.TryAccept(f.Input(1, 1, 1, 0, 0, CrewButtons.Fire), f.Session, 1), "first edge rejected");
        Assert(f.Control.TryAccept(f.Input(2, 2, 1.01, 0, 0, CrewButtons.Fire), f.Session, 1.01), "held frame rejected");
        Assert(f.Control.TryAccept(f.Input(3, 3, 1.02), f.Session, 1.02), "release rejected");
        CrewMovementPlan first = f.Step(1.02);
        Assert(first.PressedButtons == CrewButtons.Fire && first.RequestedVelocity == Vector2.Zero,
            "a queued press/release disappeared before the host fixed step");
        Assert(f.Step(1.03).PressedButtons == CrewButtons.None, "the same weapon edge replayed on a later step");
        Assert(!f.Control.TryAccept(f.Input(2, 4, 1.04, 0, 0, CrewButtons.Fire), f.Session, 1.04),
            "an old input was reclassified as another press");
        Assert(f.Step(1.04).PressedButtons == CrewButtons.None, "replay disturbed the latest release");
        Assert(f.Control.TryAccept(f.Input(4, 5, 1.05, 0, 0, CrewButtons.Fire | CrewButtons.Recall | CrewButtons.Interact),
            f.Session, 1.05), "a distinct new edge was rejected");
        Assert(f.Step(1.05).PressedButtons == (CrewButtons.Fire | CrewButtons.Recall | CrewButtons.Interact),
            "independent diagnostic action edges were lost");
        // There is no action/backend callback in this actual movement consumer.
        Assert(f.Control.CaptureState().HP == 100 && f.Control.CaptureState().BagWeightKg == null,
            "button diagnostics changed capture, inventory or damage state");
    }

    internal static void StalePauseAndOptOutNeutralizeWithoutReopeningReplayFences()
    {
        var f = new Fixture();
        ReceivedCrewInput first = f.Input(1, 1, 1, 1, 0);
        Assert(f.Control.TryAccept(first, f.Session, 1), "first input rejected");
        Assert(f.Step(1.26).RequestedVelocity == Vector2.Zero, "expired control kept moving the employee");
        Assert(!f.Control.TryAccept(f.Input(2, 2, 1, 1, 0), f.Session, 1.27) &&
            f.Control.HighestInputSequence == 2, "a late drained input escaped the replay fence");
        Assert(!f.Control.TryAccept(f.Input(2, 3, 1.28, 1, 0), f.Session, 1.28), "late input revived with a new timestamp");
        Assert(f.Control.TryAccept(f.Input(3, 4, 1.29, 1, 0), f.Session, 1.29), "fresh input could not resume");
        f.Session.Phase = SessionPhase.WaitingForScene;
        float oxygen = f.Control.Oxygen;
        CrewMovementPlan paused = f.Step(1.3);
        Assert(!paused.CanMove && paused.RequestedVelocity == Vector2.Zero && f.Control.Oxygen == oxygen,
            "paused scene performed movement or charged a simulation step");
        f.Session.Phase = SessionPhase.Ready;
        Assert(!f.Control.TryAccept(f.Input(3, 5, 1.31, 1, 0), f.Session, 1.31), "pause reset input high water");
        Assert(f.Step(1.31).RequestedVelocity == Vector2.Zero, "old held input survived pause");
        Assert(f.Control.TryAccept(f.Input(4, 6, 1.32, 1, 0), f.Session, 1.32), "new post-pause input rejected");
        f.Session.RemoteUsesCrewActor = false;
        Assert(!f.Step(1.33).CanMove, "one-sided opt-in allowed employee control");
        f.Session.RemoteUsesCrewActor = true; f.Session.LocalUsesCrewActor = false;
        Assert(!f.Control.TryAccept(f.Input(5, 7, 1.34), f.Session, 1.34), "local opt-out ignored");
        f.Session.LocalUsesCrewActor = true; f.Session.Phase = SessionPhase.Closed;
        Assert(!f.Step(1.35).CanMove && f.Control.HighestInputSequence == 4,
            "closed session moved an actor or forgot its accepted sequence");
    }

    internal static void ActorRoomSceneAndCreatorThreadCannotBorrowEmployeeControls()
    {
        var f = new Fixture();
        ReceivedCrewInput input = f.Input(1, 1, 1, 1, 0);
        input.RoomId = Guid.NewGuid().ToString("N");
        Assert(!f.Control.TryAccept(input, f.Session, 1), "another room borrowed this body");
        input.RoomId = f.Session.RoomId; input.BoundPlayerId = 1;
        Assert(!f.Control.TryAccept(input, f.Session, 1), "host-source receipt was treated as employee input");
        input.BoundPlayerId = 2; input.Frame.ActorRevision++;
        Assert(!f.Control.TryAccept(input, f.Session, 1), "old/future body identity was interchangeable");
        input.Frame.ActorRevision--; input.Frame.SceneEpoch++;
        Assert(!f.Control.TryAccept(input, f.Session, 1), "another epoch borrowed this actor");
        input.Frame.SceneEpoch--; input.Frame.SceneKey = "other";
        Assert(!f.Control.TryAccept(input, f.Session, 1), "same epoch allowed another scene key");
        input.Frame.SceneKey = f.Session.SceneKey;
        bool workerAccept = Task.Run(() => f.Control.TryAccept(input, f.Session, 1)).GetAwaiter().GetResult();
        Assert(!workerAccept && f.Control.HighestInputSequence == 0, "worker mutated creator-thread controls");
        Assert(Task.Run(() => !f.Control.Step(f.Session, 1, .02f, Vector3.Zero, Vector2.Zero, true).CanMove)
            .GetAwaiter().GetResult(), "worker produced a new movement command");
        Assert(f.Control.TryAccept(input, f.Session, 1), "rejected foreign sources consumed a valid identity");
        f.Session.Role = SessionRole.Guest;
        Assert(!f.Step(1.01).CanMove, "a guest session could drive the host's employee body");
    }

    internal static void IndependentProfilesOwnOxygenDamageAndPermanentStop()
    {
        var first = new Fixture(new HostCrewProfile(maxSpeed: 2, maxHp: 20, maxOxygen: .25f));
        var second = new Fixture(new HostCrewProfile(maxSpeed: 5, maxHp: 80, maxOxygen: 50, capacityKg: 30));
        Assert(first.Control.TryAccept(first.Input(1, 1, 1, 1, 0, CrewButtons.Boost), first.Session, 1), "boost input rejected");
        CrewMovementPlan boost = first.Step(1, .1f);
        Assert(boost.Boosting && boost.RequestedVelocity.X == 3 && first.Control.Oxygen == 0,
            "boost did not use the host's independent speed and oxygen profile");
        CrewMovementPlan empty = first.Step(1.1, .1f);
        Assert(!empty.Boosting && empty.RequestedVelocity.X == 2 && first.Control.HP == 20,
            "zero oxygen kept boosting or invented an unimplemented native suffocation rule");
        first.Control.ApplyDamage(5);
        Assert(first.Control.HP == 15 && second.Control.HP == 80 && second.Control.Oxygen == 50,
            "one member's survival model modified the other profile");
        first.Control.ApplyDamage(100);
        Assert(!first.Step(1.2).CanMove && !first.Control.CaptureState().Alive && !first.Control.CaptureState().Active,
            "a dead employee kept moving");
        second.Control.Stop("Disconnected");
        Assert(!second.Control.TryAccept(second.Input(1, 1, 1, 1, 0), second.Session, 1) &&
            !second.Control.ObserveBody(Vector3.One, Vector2.One, true) && !second.Control.CaptureState().Active,
            "a stopped actor was revived by new controls or a later body readback");
        Assert(second.Control.CaptureState().CapacityKg == 30 && second.Control.CaptureState().BagWeightKg == null,
            "independent capacity pretended to be a confirmed cargo weight");
    }

    internal static void StateCopiesCannotMutateBodyProfileOrInventConfirmedCargo()
    {
        string room = Guid.NewGuid().ToString("N");
        var model = new HostCrewControl(room, Guid.NewGuid().ToString("N"), 3, "dive", 7,
            new HostCrewProfile(), new Vector3(2, -8, 0));
        CrewActorState born = model.CaptureState();
        Assert(!born.Active && born.Position == new Vector3(2, -8, 0), "constructor values claimed a live native body");
        Assert(model.ObserveBody(new Vector3(3, -9, 0), new Vector2(.5f, -.25f), true), "readback rejected");
        CrewActorState source = model.CaptureState();
        var receipt = new ReceivedCrewActorState { RoomId = room, BoundPlayerId = 1, PacketSequence = 4, ReceivedAt = 1, Frame = source };
        CrewFrames.Validate(receipt);
        ReceivedCrewActorState owned = CrewFrames.Copy(receipt);
        source.Position = new Vector3(999, 999, 0); source.HP = 1; receipt.RoomId = "mutated";
        Assert(owned.Frame.Position == new Vector3(3, -9, 0) && owned.Frame.HP == 100 && owned.RoomId == room,
            "actor envelope copy retained caller-owned mutable payload");
        CrewActorState next = model.CaptureState();
        Assert(next.StateRevision > owned.Frame.StateRevision && next.Position == owned.Frame.Position && next.HP == 100,
            "consumer mutation changed the next actual state");
        next.HasConfirmedCargoWeight = true;
        Throws<ProtocolException>(() => CrewFrames.Validate(next), "wire accepted a guessed confirmed cargo weight");
        next.HasConfirmedCargoWeight = false; next.BagWeightKg = 0;
        Throws<ProtocolException>(() => CrewFrames.Validate(next), "unknown personal bag was silently replaced by zero");
        Assert(!model.ObserveBody(new Vector3(float.NaN, 0, 0), Vector2.Zero, true) && !model.CaptureState().Active,
            "invalid native readback remained active");
    }

    internal static void InvalidAxesProfilesAndFixedStepsCannotProduceMovement()
    {
        var f = new Fixture();
        CrewInputFrame input = f.Input(1, 1, 1).Frame;
        input.MoveX = float.NaN;
        Throws<ProtocolException>(() => CrewFrames.Validate(input), "nonfinite controls accepted");
        input.MoveX = 1.01f;
        Throws<ProtocolException>(() => CrewFrames.Validate(input), "out-of-range control accepted");
        input.MoveX = 0; input.Buttons = (CrewButtons)16;
        Throws<ProtocolException>(() => CrewFrames.Validate(input), "unknown button behavior accepted");
        Throws<ArgumentException>(() => new HostCrewProfile(maxSpeed: 0), "zero host speed accepted");
        Throws<ArgumentException>(() => new HostCrewProfile(boostMultiplier: 4), "unbounded boost accepted");
        Throws<ArgumentException>(() => new HostCrewProfile(maxHp: float.PositiveInfinity), "infinite HP accepted");
        Throws<ArgumentException>(() => new HostCrewProfile(oxygenPerSecond: -1), "negative oxygen charge accepted");
        float oxygen = f.Control.Oxygen;
        Throws<ArgumentOutOfRangeException>(() => f.Step(1, 0), "zero fixed step accepted");
        Throws<ArgumentOutOfRangeException>(() => f.Step(1, .101f), "oversized fixed step accepted");
        Throws<ArgumentOutOfRangeException>(() => f.Step(1, float.NaN), "nonfinite fixed step accepted");
        Assert(f.Control.Oxygen == oxygen && f.Control.HighestInputSequence == 0, "invalid step changed simulation state");
        Assert(f.Control.TryAccept(f.Input(1, 1, 2, 1, 0), f.Session, 2), "fresh bound control rejected");
        Assert(!f.Step(1).CanMove && f.Control.HighestInputSequence == 1, "host clock rewind revived movement");
    }

    internal static void ReceiptDeadlineAndBothSequenceFencesBoundControlLifetime()
    {
        var f = new Fixture();
        Assert(!f.Control.TryAccept(f.Input(1, 1, 2, 1, 0), f.Session, 1), "future host receipt accepted");
        Assert(f.Control.HighestInputSequence == 1, "invalid delivery was not fenced after valid identity");
        Assert(f.Control.TryAccept(f.Input(2, 2, 1, 1, 0), f.Session, 1), "new current receipt rejected");
        Assert(f.Step(1.25).RequestedVelocity.X == 3, "exact host receipt deadline was prematurely stale");
        Assert(f.Step(1.250001).RequestedVelocity == Vector2.Zero, "receipt remained fresh after the fixed deadline");
        Assert(!f.Control.TryAccept(f.Input(3, 2, 1.3, 1, 0), f.Session, 1.3) &&
            f.Control.HighestInputSequence == 2, "new control sequence borrowed an old packet envelope");
        Assert(!f.Control.TryAccept(f.Input(2, 3, 1.3, 1, 0), f.Session, 1.3), "new packet replayed an old control sequence");
        Assert(f.Control.TryAccept(f.Input(3, 3, 1.3, -1, 0), f.Session, 1.3), "next distinct packet/control rejected");
        f.Control.Neutralize("LocalPause");
        Assert(!f.Control.TryAccept(f.Input(3, 4, 1.31, 1, 0), f.Session, 1.31) &&
            f.Step(1.31).RequestedVelocity == Vector2.Zero, "manual neutralization erased replay history");
    }

    internal static void SceneReplacementInheritsSurvivalWithoutRefillingOrReviving()
    {
        var f = new Fixture(new HostCrewProfile(maxHp: 20, maxOxygen: 20));
        Assert(f.Control.TryAccept(f.Input(1, 1, 1, 1, 0, CrewButtons.Boost), f.Session, 1), "initial boost rejected");
        f.Step(1, .1f); f.Control.ApplyDamage(7);
        float hp = f.Control.HP, oxygen = f.Control.Oxygen;
        f.Control.Neutralize("ScenePause");
        Assert(hp == 13 && oxygen < 20 && f.Control.HP == hp && f.Control.Oxygen == oxygen,
            "neutralization refilled independent survival values");
        f.Control.Stop("SceneReplaced");
        f.Session.SceneEpoch = 4;
        var next = new HostCrewControl(f.Session.RoomId, f.Control.MemberId, 4, "dive", 8, f.Control.Profile,
            new Vector3(40, -20, 0), initialHp: hp, initialOxygen: oxygen);
        next.ObserveBody(new Vector3(40, -20, 0), Vector2.Zero, true);
        CrewActorState state = next.CaptureState();
        Assert(state.HP == hp && state.Oxygen == oxygen && state.ActorRevision == 8 && state.SceneEpoch == 4,
            "a same-room scene replacement created full HP/oxygen");
        Assert(!next.TryAccept(f.Input(2, 2, 2, 1, 0), f.Session, 2), "old scene/actor input borrowed the new body");
        var input = f.Input(2, 2, 2, 1, 0); input.Frame.SceneEpoch = 4; input.Frame.ActorRevision = 8;
        Assert(next.TryAccept(input, f.Session, 2) && next.Step(f.Session, 2, .02f,
            new Vector3(40, -20, 0), Vector2.Zero, true).CanMove, "new body's bound input could not continue");
        var dead = new HostCrewControl(f.Session.RoomId, next.MemberId, 4, "dive", 9, next.Profile,
            Vector3.Zero, initialHp: 0, initialOxygen: 0);
        dead.ObserveBody(Vector3.Zero, Vector2.Zero, true); input.Frame.ActorRevision = 9; input.Frame.InputSequence++;
        Assert(!dead.TryAccept(input, f.Session, 2) && !dead.CaptureState().Alive && !dead.CaptureState().Active,
            "scene replacement resurrected a previously dead employee");
        Throws<ArgumentException>(() => new HostCrewControl(f.Session.RoomId, next.MemberId, 4, "dive", 10,
            next.Profile, Vector3.Zero, initialHp: float.NaN), "invalid inherited HP accepted");
        Throws<ArgumentException>(() => new HostCrewControl(f.Session.RoomId, next.MemberId, 4, "dive", 10,
            next.Profile, Vector3.Zero, initialOxygen: 21), "inherited oxygen exceeded host profile");
    }

    private sealed class Fixture
    {
        internal readonly SessionSnapshot Session;
        internal readonly HostCrewControl Control;
        internal Fixture(HostCrewProfile profile = null)
        {
            Session = new SessionSnapshot
            {
                RoomId = Guid.NewGuid().ToString("N"), Role = SessionRole.Host, Phase = SessionPhase.Ready,
                LocalPlayerId = 1, RemotePlayerId = 2, LocalUsesCrewActor = true, RemoteUsesCrewActor = true,
                SceneEpoch = 3, SceneKey = "dive"
            };
            Control = new HostCrewControl(Session.RoomId, Guid.NewGuid().ToString("N"), 3, "dive", 7,
                profile ?? new HostCrewProfile(), new Vector3(2, -8, 0));
            Control.ObserveBody(new Vector3(2, -8, 0), Vector2.Zero, true);
        }
        internal ReceivedCrewInput Input(long sequence, long packet, double at, float x = 0, float y = 0,
            CrewButtons buttons = CrewButtons.None) => new ReceivedCrewInput
        {
            RoomId = Session.RoomId, BoundPlayerId = 2, PacketSequence = packet, ReceivedAt = at,
            Frame = new CrewInputFrame
            { PlayerId = 2, SceneEpoch = 3, SceneKey = "dive", ActorRevision = 7, InputSequence = sequence,
                MoveX = x, MoveY = y, AimX = 1, AimY = 0, Buttons = buttons }
        };
        internal CrewMovementPlan Step(double now, float dt = .02f) =>
            Control.Step(Session, now, dt, new Vector3(2, -8, 0), Vector2.Zero, true);
    }

    private static bool Close(float a, float b) => Math.Abs(a - b) < .00001f;
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action, string message) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException(message); }
}
