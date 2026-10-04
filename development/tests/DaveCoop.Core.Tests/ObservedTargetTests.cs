using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DaveCoop.Core.World;

internal static class ObservedTargetTests
{
    internal static void SnapshotOwnershipAndGeneration()
    {
        var index = new ObservedHostTargets();
        HostPointerTarget first = Entry(3, 1, 11, 101, 5);
        var input = new[] { first }; index.Publish(3, input); input[0] = Entry(3, 2, 12, 102, 6);
        Assert(index.TryResolve(101, 5, out HostEntityTarget target) && target.EntityId == 1 && index.Count == 1,
            "published callback map retained mutable caller storage");
        Assert(!index.TryResolve(101, 6, out target) && target.EntityId == 0, "new pool generation resolved the retired observation");
        index.Publish(3, new[] { Entry(3, 2, 11, 101, 6) });
        Assert(!index.TryResolve(101, 5, out _) && index.TryResolve(101, 6, out target) && target.EntityId == 2,
            "replacement failed to revoke an old callback mapping");
        index.Clear();
        Assert(index.Count == 0 && !index.TryResolve(101, 6, out _), "cleared scene retained callback targets");
    }

    internal static void InvalidPublishIsAtomic()
    {
        var index = new ObservedHostTargets(); HostPointerTarget first = Entry(1, 1, 1, 101, 5);
        index.Publish(1, new[] { first });
        Throws<ArgumentException>(() => index.Publish(1, new[] { first, Entry(1, 2, 2, 101, 5) }));
        Throws<ArgumentException>(() => index.Publish(1, new[] { first, Entry(1, 1, 2, 102, 5) }));
        Throws<ArgumentException>(() => index.Publish(2, new[] { first }));
        Throws<ArgumentException>(() => index.Publish(1, new[] { new HostPointerTarget(0, first.Target) }));
        Throws<ArgumentException>(() => index.Publish(1, new[] { Entry(1, 3, 3, 103, 0) }));
        Assert(index.Count == 1 && index.TryResolve(101, 5, out HostEntityTarget target) && target.EntityId == 1,
            "failed publication partially replaced the live callback map");
        index.Publish(1, Array.Empty<HostPointerTarget>());
        Assert(index.Count == 0 && !index.TryResolve(101, 5, out _), "empty complete observation retained an old target");
    }

    internal static void ConcurrentReadersAndCapacity()
    {
        var index = new ObservedHostTargets(); var entries = new List<HostPointerTarget>();
        for (int i = 1; i <= WorldFrames.MaxEntities; i++) entries.Add(Entry(1, i, i, i + 10000, 1));
        index.Publish(1, entries);
        entries.Add(Entry(1, WorldFrames.MaxEntities + 1, WorldFrames.MaxEntities + 1, 50000, 1));
        Throws<InvalidOperationException>(() => index.Publish(1, entries));
        Assert(index.Count == WorldFrames.MaxEntities && index.TryResolve(10001, 1, out _), "capacity rejection corrupted the published map");
        Parallel.Invoke(
            () => { for (int i = 0; i < 2000; i++) index.Publish(1, new[] { Entry(1, 1, 1, 101, 1) }); },
            () => { for (int i = 0; i < 2000; i++) index.Publish(2, new[] { Entry(2, 2, 2, 101, 2) }); },
            () => { for (int i = 0; i < 10000; i++) if (index.TryResolve(101, 1, out HostEntityTarget target)) Assert(target.SceneEpoch == 1 && target.EntityId == 1, "mixed publication reached a reader"); },
            () => { for (int i = 0; i < 10000; i++) if (index.TryResolve(101, 2, out HostEntityTarget target)) Assert(target.SceneEpoch == 2 && target.EntityId == 2, "mixed generation reached a reader"); });
    }

    private static HostPointerTarget Entry(long epoch, long id, long token, long pointer, long generation)
        => new HostPointerTarget(pointer, new HostEntityTarget(epoch, id, token, EntityKind.Fish, 2010007, generation));
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
