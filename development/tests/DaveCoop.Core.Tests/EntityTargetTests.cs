using System;
using DaveCoop.Core.World;

internal static class EntityTargetTests
{
    internal static void ResolveAndUnbind()
    {
        var registry = new HostEntityRegistry();
        Assert(!registry.TryResolve(0, 1, out HostEntityTarget absent) && absent.EntityId == 0,
            "an uninitialized registry resolved a target");
        registry.BeginEpoch(31);
        long fish = registry.Bind(-19, EntityKind.Fish, 2010007, 6);
        long item = registry.Bind(20, EntityKind.Item, 901, 8);
        Assert(registry.TryResolve(31, fish, out HostEntityTarget target) &&
            target.SceneEpoch == 31 && target.EntityId == fish && target.LocalToken == -19 &&
            target.Kind == EntityKind.Fish && target.DataTid == 2010007 && target.Generation == 6,
            "fish lookup lost the local binding or its generation");
        Assert(registry.TryResolve(31, item, out target) && target.LocalToken == 20 &&
            target.Kind == EntityKind.Item && target.DataTid == 901 && target.Generation == 8,
            "item lookup reused a neighboring fish binding");
        Assert(!registry.TryResolve(32, fish, out _) && !registry.TryResolve(0, fish, out _) &&
            !registry.TryResolve(31, 0, out _) && !registry.TryResolve(31, -1, out _) &&
            !registry.TryResolve(31, long.MaxValue, out _),
            "invalid epoch or unknown entity identity resolved");
        Assert(registry.Unbind(-19) && !registry.TryResolve(31, fish, out _) &&
            registry.TryResolve(31, item, out target) && target.LocalToken == 20 && registry.Count == 1,
            "unbind left a target alive or removed another entity's binding");
        Assert(!registry.Unbind(-19) && registry.Count == 1, "repeated unbind changed registry ownership");
    }

    internal static void PoolGenerationAndKindReplacement()
    {
        var registry = new HostEntityRegistry(); registry.BeginEpoch(1);
        long original = registry.Bind(99, EntityKind.Fish, 2010007, 10);
        Assert(registry.Bind(99, EntityKind.Fish, 2010007, 10) == original, "unchanged binding consumed a new identity");
        long reused = registry.Bind(99, EntityKind.Fish, 2010007, 11);
        Assert(reused > original && !registry.TryResolve(1, original, out _) &&
            registry.TryResolve(1, reused, out HostEntityTarget target) && target.Generation == 11 && registry.Count == 1,
            "same-species pool reuse still resolved the retired generation");
        long speciesChanged = registry.Bind(99, EntityKind.Fish, 2010008, 11);
        Assert(speciesChanged > reused && !registry.TryResolve(1, reused, out _) &&
            registry.TryResolve(1, speciesChanged, out target) && target.DataTid == 2010008,
            "species replacement retained an old target");
        long kindChanged = registry.Bind(99, EntityKind.Item, 901, 11);
        Assert(kindChanged > speciesChanged && !registry.TryResolve(1, speciesChanged, out _) &&
            registry.TryResolve(1, kindChanged, out target) && target.Kind == EntityKind.Item && registry.Count == 1,
            "kind replacement retained a fish target or accumulated inverse entries");
        Throws<ArgumentException>(() => registry.Bind(99, EntityKind.Item, 901, -1));
        Assert(registry.TryResolve(1, kindChanged, out target) && target.Generation == 11 &&
            registry.Bind(99, EntityKind.Item, 901, 11) == kindChanged,
            "invalid replacement retired the current valid target");
    }

    internal static void ClearAndEpochFence()
    {
        var registry = new HostEntityRegistry(); registry.BeginEpoch(2);
        long original = registry.Bind(1, EntityKind.Fish, 3, 4);
        registry.Clear();
        Assert(registry.Count == 0 && registry.Epoch == 2 && !registry.TryResolve(2, original, out _),
            "same-epoch clear retained an inverse target or reset epoch ownership");
        long afterClear = registry.Bind(1, EntityKind.Fish, 3, 4);
        Assert(afterClear > original && !registry.TryResolve(2, original, out _), "same-epoch clear resurrected a retired identity");
        registry.BeginEpoch(2);
        Assert(registry.TryResolve(2, afterClear, out _) && registry.Count == 1, "same-epoch begin cleared a live binding");
        registry.BeginEpoch(3);
        Assert(registry.Count == 0 && !registry.TryResolve(2, afterClear, out _), "new epoch retained an old target");
        long newEpoch = registry.Bind(2, EntityKind.Fish, 5, 6);
        Assert(newEpoch == original && !registry.TryResolve(2, original, out _) &&
            registry.TryResolve(3, newEpoch, out HostEntityTarget target) && target.LocalToken == 2 && target.Generation == 6,
            "an identical numeric ID crossed its epoch fence");
        Throws<ArgumentException>(() => registry.BeginEpoch(2));
        Assert(registry.TryResolve(3, newEpoch, out _), "rejected epoch regression erased the active target");
    }

    internal static void CapacityAndReplacementConsistency()
    {
        var registry = new HostEntityRegistry(); registry.BeginEpoch(1);
        for (int i = 1; i <= WorldFrames.MaxEntities; i++) registry.Bind(i, EntityKind.Fish, 3, 1);
        Throws<InvalidOperationException>(() => registry.Bind(WorldFrames.MaxEntities + 1, EntityKind.Fish, 3, 1));
        Assert(registry.Count == WorldFrames.MaxEntities && !registry.TryResolve(1, WorldFrames.MaxEntities + 1, out _),
            "failed capacity admission created an inverse target");
        for (int i = 1; i <= WorldFrames.MaxEntities; i++)
        {
            Assert(registry.TryResolve(1, i, out HostEntityTarget target) && target.LocalToken == i && target.Generation == 1,
                "capacity rejection corrupted a previously active inverse target");
        }
        long replacement = registry.Bind(1, EntityKind.Fish, 3, 2);
        Assert(replacement == WorldFrames.MaxEntities + 1 && registry.Count == WorldFrames.MaxEntities &&
            !registry.TryResolve(1, 1, out _) && registry.TryResolve(1, replacement, out HostEntityTarget replaced) &&
            replaced.LocalToken == 1 && replaced.Generation == 2,
            "full-registry replacement consumed failed-admission identity or retained the old inverse target");
        Assert(registry.Unbind(2) && !registry.TryResolve(1, 2, out _), "capacity release retained the retired inverse target");
        long admitted = registry.Bind(WorldFrames.MaxEntities + 1, EntityKind.Item, 7, 3);
        Assert(admitted == replacement + 1 && registry.Count == WorldFrames.MaxEntities &&
            registry.TryResolve(1, admitted, out HostEntityTarget added) && added.Kind == EntityKind.Item &&
            added.LocalToken == WorldFrames.MaxEntities + 1 && added.Generation == 3,
            "freed capacity did not admit exactly one new inverse target");
        for (int i = 3; i <= WorldFrames.MaxEntities; i++)
        {
            Assert(registry.TryResolve(1, i, out HostEntityTarget target) && target.LocalToken == i,
                "replacement or capacity release removed an unrelated inverse binding");
        }
    }

    internal static void SnapshotOwnershipAndFailureOutput()
    {
        var registry = new HostEntityRegistry(); registry.BeginEpoch(7);
        long original = registry.Bind(-88, EntityKind.Fish, 101, 5);
        Assert(registry.TryResolve(7, original, out HostEntityTarget observed), "initial ownership lookup failed");
        HostEntityTarget saved = observed;
        long replacement = registry.Bind(-88, EntityKind.Fish, 102, 6);
        Assert(registry.TryResolve(7, replacement, out observed) && observed.DataTid == 102 && observed.Generation == 6,
            "current target did not describe the replacement");
        Assert(saved.SceneEpoch == 7 && saved.EntityId == original && saved.LocalToken == -88 &&
            saved.Kind == EntityKind.Fish && saved.DataTid == 101 && saved.Generation == 5,
            "a captured target snapshot changed after registry replacement");
        Assert(!registry.TryResolve(7, original, out observed) && observed.EntityId == 0 &&
            observed.LocalToken == 0 && observed.SceneEpoch == 0 && observed.Generation == 0,
            "failed lookup leaked a previously resolved local target");
        registry.Clear();
        Assert(saved.EntityId == original && saved.DataTid == 101 && !registry.TryResolve(7, saved.EntityId, out _),
            "clearing the registry changed a saved value or treated it as an active authorization");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
