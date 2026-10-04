using System;
using System.Numerics;
using DaveCoop.Core;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.World;

internal static class FishWorldTests
{
    internal static void AtomicRosterAndOwnership()
    {
        var buffer = new FishWorldBuffer();
        WorldSnapshot source = Snapshot(1, 1, Fish(1, 1), Fish(2, 2), Item(3));
        Assert(buffer.Push(source, 10) && buffer.ReceivedEntityCount == 3 && buffer.Count == 2 && buffer.AliveFishCount == 2,
            "complete numeric roster counts were confused with displayable fish");
        source.Entities[0].Hp = 1; source.Entities[0].Visual.Animation = "changed";
        Assert(buffer.TryGetLatest(1, out EntityState copied) && copied.Hp == 10 && copied.Visual.Animation == "swim",
            "ingress DTO mutation changed the retained fish");
        copied.Hp = 2; copied.Visual.Animation = "changed again";
        Assert(buffer.Sample(1, 10, 0, out EntityState from, out EntityState to, out _) && to.Hp == 10 && to.Visual.Animation == "swim",
            "latest-state query exposed mutable history ownership");
        from.Hp = 3; to.Visual.Animation = "changed through sample";
        Assert(buffer.Sample(1, 10, 0, out _, out to, out _) && to.Hp == 10 && to.Visual.Animation == "swim",
            "render samples exposed mutable history ownership");
        long[] ids = buffer.GetEntityIds(); ids[0] = 999;
        Assert(buffer.GetEntityIds()[0] == 1 && !buffer.Contains(3), "ID enumeration exposed mutable membership or tracked an item as fish");
        EntityState added = Fish(4, 4); added.Visual = null;
        Assert(buffer.Push(Snapshot(2, 2, Fish(2, 5), added), 11) && buffer.Count == 2 && buffer.ReceivedEntityCount == 2,
            "complete roster update did not add, update and remove atomically");
        Assert(!buffer.Contains(1) && !buffer.Sample(1, 11, 0, out _, out _, out _) && buffer.LastSampleStatus == "UnknownEntity",
            "complete-roster removal left an old fish sample available");
        Assert(buffer.HistoryCount(2) == 2 && buffer.HistoryCount(4) == 1 && buffer.Contains(4) &&
            buffer.TryGetLatest(4, out EntityState missing) && missing.Visual == null,
            "retained fish lost history or a missing visual discarded its numeric identity");
    }

    internal static void InvalidBatchPreservesCommittedWorld()
    {
        var buffer = new FishWorldBuffer(); buffer.Push(Snapshot(1, 1, Fish(1, 1), Fish(2, 2)), 10);
        var duplicate = Snapshot(2, 2, Fish(3, 3), Fish(3, 4));
        Throws<ProtocolException>(() => buffer.Push(duplicate, 11));
        var invalid = Snapshot(2, 2, Fish(3, 3), Fish(4, 4)); invalid.Entities[1].Hp = float.NaN;
        Throws<ProtocolException>(() => buffer.Push(invalid, 11));
        var identityChanged = Snapshot(2, 2, Fish(1, 5), Fish(2, 6)); identityChanged.Entities[1].DataTid++;
        Throws<ProtocolException>(() => buffer.Push(identityChanged, 11));
        var sceneChanged = Snapshot(2, 2, Fish(1, 5)); sceneChanged.SceneKey = "another-dive";
        Throws<ProtocolException>(() => buffer.Push(sceneChanged, 11));
        Throws<ProtocolException>(() => buffer.Push(Snapshot(2, 0.5, Fish(1, 5)), 11));
        Assert(buffer.SceneEpoch == 1 && buffer.Revision == 1 && buffer.Count == 2 && buffer.Contains(1) && buffer.Contains(2) &&
            !buffer.Contains(3) && !buffer.Contains(4) && buffer.HistoryCount(1) == 1 && buffer.HistoryCount(2) == 1,
            "invalid batch partially removed an old fish, advanced its history or inserted a new fish");
        Assert(buffer.Sample(1, 10, 0, out _, out EntityState state, out _) && state.Root.Position.X == 1,
            "failed batch changed the previously committed fish pose");
        Assert(buffer.Push(Snapshot(2, 2, Fish(1, 5)), 11) && buffer.Revision == 2 && buffer.Count == 1,
            "validation failure consumed the revision or broke a later valid commit");
    }

    internal static void IndependentInterpolationAndVisualRecovery()
    {
        var buffer = new FishWorldBuffer(); buffer.Push(Snapshot(1, 1, Fish(1, 0), Fish(2, 10)), 10);
        EntityState first = Fish(1, 2), second = Fish(2, 12); first.Visual = null;
        buffer.Push(Snapshot(2, 1.2, first, second), 10.2);
        Assert(buffer.Sample(1, 10.2, 0.1, out EntityState a0, out EntityState a1, out float aa) &&
            buffer.Sample(2, 10.2, 0.1, out EntityState b0, out EntityState b1, out float ba) &&
            Math.Abs(aa - 0.5f) < 0.001f && Math.Abs(ba - 0.5f) < 0.001f &&
            a0.Id == 1 && a1.Id == 1 && b0.Id == 2 && b1.Id == 2 &&
            a0.Root.Position.X == 0 && a1.Root.Position.X == 2 && b0.Root.Position.X == 10 && b1.Root.Position.X == 12,
            "fish histories shared another entity's state or lost the epoch clock interpolation anchor");
        Assert(buffer.Contains(1) && buffer.HistoryCount(1) == 2 && a1.Visual == null,
            "missing visual removed a numeric fish or replaced its history");
        first = Fish(1, 4); first.Visual.Visible = false;
        buffer.Push(Snapshot(3, 1.4, first, Fish(2, 14)), 10.4);
        Assert(buffer.Sample(1, 10.4, 0, out _, out EntityState invisible, out _) && !invisible.Visual.Visible && buffer.HistoryCount(1) == 3,
            "source invisibility changed numeric membership or history ownership");
        buffer.Push(Snapshot(4, 1.6, Fish(1, 6), Fish(2, 16)), 10.6);
        Assert(buffer.Contains(1) && buffer.HistoryCount(1) == 4 && buffer.Sample(1, 10.6, 0, out _, out EntityState visible, out _) && visible.Visual.Visible,
            "restored visual created a new identity or lost accumulated fish history");
    }

    internal static void EpochReplayAndFreshnessRecovery()
    {
        var buffer = new FishWorldBuffer(); buffer.Push(Snapshot(1, 1, Fish(1, 1)), 10);
        Assert(!buffer.Sample(1, 9.9, 0.1, out _, out _, out _) && buffer.LastSampleStatus == "BeforeArrival",
            "sampling before the first arrival lacked a diagnosis");
        Assert(buffer.Sample(1, 11, 0.1, out _, out _, out _) && !buffer.Sample(1, 11.001, 0.1, out _, out _, out _) &&
            buffer.LastSampleStatus == "Stale" && buffer.Contains(1), "staleness destroyed identity or violated the one-second boundary");
        Assert(!buffer.Push(Snapshot(1, 1.2, Fish(2, 2)), 11.2) && buffer.SampleAge(11.2) > 1,
            "replayed revision replaced a fish or refreshed its stale arrival clock");
        Assert(buffer.Push(Snapshot(2, 1.2, Fish(1, 3)), 11.4) && buffer.Sample(1, 11.5, 0.1, out _, out _, out _) &&
            buffer.LastSampleStatus == "Ready" && buffer.HistoryCount(1) == 2,
            "fresh update did not recover the retained fish after a stale display");
        var newer = Snapshot(1, 0.1, Fish(2, 20)); newer.SceneEpoch = 2;
        buffer.Push(newer, 12);
        Assert(buffer.SceneEpoch == 2 && buffer.Revision == 1 && !buffer.Contains(1) && buffer.Contains(2) && buffer.HistoryCount(2) == 1,
            "new epoch retained the previous fish roster or interpolation history");
        Assert(!buffer.Push(Snapshot(999, 2, Fish(1, 1)), 13) && buffer.SceneEpoch == 2 && buffer.Contains(2),
            "old epoch resurrected a retired numeric fish");
        Assert(!buffer.Sample(2, double.NaN, 0.1, out _, out _, out _) && buffer.LastSampleStatus == "InvalidClock",
            "invalid sampling clock lacked its own diagnosis");
        Throws<ArgumentException>(() => buffer.Push(newer, double.PositiveInfinity));
        var fresh = Snapshot(2, 0.2, Fish(2, 21)); fresh.SceneEpoch = 2;
        Throws<ArgumentException>(() => buffer.Push(fresh, 11));
        buffer.Clear();
        Assert(buffer.SceneEpoch == 0 && buffer.Revision == 0 && buffer.Count == 0 && buffer.ReceivedEntityCount == 0 &&
            !buffer.Sample(2, 13, 0.1, out _, out _, out _) && buffer.LastSampleStatus == "EmptyHistory",
            "disconnect clear retained a numeric fish or clock fence");
    }

    internal static void TerminalAndUndisplayableNumericRoster()
    {
        var buffer = new FishWorldBuffer(); EntityState dead = Fish(1, 1), captured = Fish(2, 2), missing = Fish(3, 3), hidden = Fish(4, 4);
        dead.Dead = true; captured.Captured = true; missing.Visual = null; hidden.Visual.Visible = false;
        buffer.Push(Snapshot(1, 1, dead, captured, missing, hidden, Item(5)), 10);
        Assert(buffer.ReceivedEntityCount == 5 && buffer.Count == 4 && buffer.AliveFishCount == 2 &&
            buffer.Contains(1) && buffer.Contains(2) && buffer.Contains(3) && buffer.Contains(4),
            "numeric fish counts were reduced to visible assets or included a non-fish entity");
        Assert(buffer.TryGetLatest(1, out EntityState terminal) && terminal.Dead &&
            buffer.TryGetLatest(2, out terminal) && terminal.Captured &&
            buffer.Sample(3, 10, 0.1, out _, out EntityState noVisual, out _) && noVisual.Visual == null,
            "terminal or undisplayable state was discarded rather than retained for reconciliation");
        Assert(buffer.Push(Snapshot(2, 2), 11) && buffer.Count == 0 && buffer.ReceivedEntityCount == 0 && buffer.AliveFishCount == 0 &&
            buffer.GetEntityIds().Length == 0 && !buffer.Contains(1), "empty complete roster did not remove every numeric fish");
    }

    internal static void CapacityAndIndependentHistoryBounds()
    {
        var buffer = new FishWorldBuffer(); var entities = new EntityState[WorldFrames.MaxEntities];
        for (int i = 0; i < entities.Length; i++) entities[i] = Fish(i + 1, i);
        buffer.Push(Snapshot(1, 1, entities), 10);
        Assert(buffer.Count == WorldFrames.MaxEntities && buffer.ReceivedEntityCount == WorldFrames.MaxEntities &&
            buffer.Contains(WorldFrames.MaxEntities), "maximum bounded fish roster was truncated");
        var excessive = new EntityState[WorldFrames.MaxEntities + 1];
        for (int i = 0; i < excessive.Length; i++) excessive[i] = Fish(i + 1, i);
        Throws<ProtocolException>(() => buffer.Push(Snapshot(2, 2, excessive), 11));
        Assert(buffer.Count == WorldFrames.MaxEntities && buffer.Revision == 1 && buffer.HistoryCount(1) == 1,
            "capacity rejection partially changed the committed world");
        for (int i = 2; i <= 50; i++) buffer.Push(Snapshot(i, i, Fish(1, i), Fish(2, i + 100)), i + 9);
        Assert(buffer.Count == 2 && buffer.HistoryCount(1) == 16 && buffer.HistoryCount(2) == 16 && !buffer.Contains(3),
            "per-entity histories exceeded their cap or obsolete roster entries accumulated");
        Assert(buffer.Sample(1, 59, 0, out _, out EntityState first, out _) && buffer.Sample(2, 59, 0, out _, out EntityState second, out _) &&
            first.Id == 1 && first.Root.Position.X == 50 && second.Id == 2 && second.Root.Position.X == 150,
            "bounded histories leaked another fish's latest pose");
    }

    private static Pose PoseAt(float x) => new Pose { Position = new Vector3(x, 0, 0), Rotation = Quaternion.Identity, Scale = Vector3.One };
    private static EntityState Fish(long id, float x) => new EntityState
    {
        Id = id, Kind = EntityKind.Fish, DataTid = 2010007, Hp = 10, MaxHp = 10, Root = PoseAt(x),
        Visual = new FishVisual
        {
            Kind = FishVisualKind.Spine, AssetKey = "spine-v1:" + new string('a', 64), LocalPose = PoseAt(0), Color = Vector4.One,
            Visible = true, Skin = "default", Animation = "swim", AnimationRate = 1
        }
    };
    private static EntityState Item(long id) => new EntityState { Id = id, Kind = EntityKind.Item, DataTid = 901, Root = PoseAt(0) };
    private static WorldSnapshot Snapshot(long revision, double sampleTime, params EntityState[] entities) => new WorldSnapshot
    { SceneEpoch = 1, SceneKey = "dive", Revision = revision, SampleTime = sampleTime, Entities = entities };
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
