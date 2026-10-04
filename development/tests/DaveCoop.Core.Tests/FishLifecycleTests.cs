using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DaveCoop.Core.World;

internal static class FishLifecycleTests
{
    internal static void PoolCycleBetweenSnapshots()
    {
        var tracker = new FishLifecycleTracker(); var registry = new HostEntityRegistry(); registry.BeginEpoch(1);
        long firstGeneration = tracker.ObserveActive(400);
        long firstId = registry.Bind(-9, EntityKind.Fish, 2010007, firstGeneration);
        Assert(!tracker.Signal(400, FishLifecycleSignal.Enable), "duplicate enable changed a live generation");
        Assert(registry.Bind(-9, EntityKind.Fish, 2010007, tracker.ObserveActive(400)) == firstId, "ordinary polling changed fish identity");
        tracker.Signal(400, FishLifecycleSignal.Disable); tracker.Signal(400, FishLifecycleSignal.Enable);
        long generation = tracker.ObserveActive(400);
        Assert(generation > firstGeneration && registry.Bind(-9, EntityKind.Fish, 2010007, generation) > firstId,
            "same-species pool cycle between observations reused the old network identity");
        tracker.Signal(400, FishLifecycleSignal.Disable); tracker.Signal(400, FishLifecycleSignal.Disable);
        tracker.Signal(400, FishLifecycleSignal.Enable); tracker.Signal(400, FishLifecycleSignal.Enable);
        Assert(tracker.Transitions == 4, "nested base/derived callbacks created duplicate transitions");
    }

    internal static void DestroyAndPointerReuse()
    {
        var tracker = new FishLifecycleTracker(); long initial = tracker.ObserveActive(-400);
        Assert(tracker.Signal(-400, FishLifecycleSignal.Destroy) && !tracker.Signal(-400, FishLifecycleSignal.Destroy), "duplicate destroy was not idempotent");
        Assert(!tracker.Signal(-400, FishLifecycleSignal.Disable), "disable after destroy changed state");
        long reused = tracker.ObserveActive(-400);
        Assert(reused > initial, "observed live object at destroyed pointer reused a generation");
        tracker.Signal(-400, FishLifecycleSignal.Disable);
        Assert(tracker.ObserveActive(-400) > reused, "missed enable callback retained a disabled generation");
    }

    internal static void BoundsAndUntrackedCallbacks()
    {
        var tracker = new FishLifecycleTracker();
        for (int i = 1; i <= 20000; i++) Assert(!tracker.Signal(i, FishLifecycleSignal.Enable), "untracked callback allocated a fish");
        Assert(tracker.Count == 0 && tracker.Transitions == 0, "unobserved native callbacks grew the ledger");
        Throws<ArgumentException>(() => tracker.ObserveActive(0));
        Throws<ArgumentException>(() => tracker.Signal(1, (FishLifecycleSignal)99));
        for (int i = 1; i <= WorldFrames.MaxEntities; i++) tracker.ObserveActive(i);
        Throws<InvalidOperationException>(() => tracker.ObserveActive(5000));
        tracker.Retain(new HashSet<long> { 1 });
        Assert(tracker.Count == 1 && tracker.ObserveActive(5000) > WorldFrames.MaxEntities, "pruning did not free capacity without identity reuse");
    }

    internal static void ClearPreservesGeneration()
    {
        var tracker = new FishLifecycleTracker(); long before = tracker.ObserveActive(7);
        tracker.Signal(7, FishLifecycleSignal.Disable); tracker.Clear();
        Assert(tracker.Count == 0 && tracker.Transitions == 0 && tracker.ObserveActive(7) > before, "diagnostic restart reused old generations");
        var registry = new HostEntityRegistry(); registry.BeginEpoch(1);
        Throws<ArgumentException>(() => registry.Bind(7, EntityKind.Fish, 1, -1));
    }

    internal static void ConcurrentCallbacks()
    {
        var tracker = new FishLifecycleTracker(); var generations = new long[256];
        Parallel.For(0, generations.Length, i =>
        {
            long first = tracker.ObserveActive(i + 1);
            tracker.Signal(i + 1, FishLifecycleSignal.Disable); tracker.Signal(i + 1, FishLifecycleSignal.Enable);
            generations[i] = tracker.ObserveActive(i + 1);
            Assert(generations[i] > first, "concurrent callbacks lost a pool transition");
        });
        Assert(tracker.Count == 256 && tracker.Transitions == 512 && new HashSet<long>(generations).Count == 256,
            "callback ledger was not thread safe or generations collided");
    }

    internal static void ActiveGenerationLookup()
    {
        var tracker = new FishLifecycleTracker();
        Assert(!tracker.TryGetActiveGeneration(0, out long generation) && generation == 0 && tracker.Count == 0,
            "target lookup allocated an unknown fish or leaked a generation");
        long original = tracker.ObserveActive(17);
        Assert(tracker.TryGetActiveGeneration(17, out generation) && generation == original, "active target lost its observed generation");
        tracker.Signal(17, FishLifecycleSignal.Disable);
        Assert(!tracker.TryGetActiveGeneration(17, out generation) && generation == 0, "disabled fish remained an active command target");
        tracker.Signal(17, FishLifecycleSignal.Enable);
        Assert(tracker.TryGetActiveGeneration(17, out generation) && generation > original, "pool reenable did not fence the old target");
        tracker.Signal(17, FishLifecycleSignal.Destroy);
        Assert(!tracker.TryGetActiveGeneration(17, out generation) && generation == 0, "destroyed fish remained an active command target");
        tracker.ObserveActive(17); tracker.Retain(new HashSet<long>());
        Assert(!tracker.TryGetActiveGeneration(17, out generation) && generation == 0 && tracker.Count == 0, "pruned fish remained resolvable");
        tracker.ObserveActive(17); tracker.Clear();
        Assert(!tracker.TryGetActiveGeneration(17, out generation) && generation == 0, "cleared lifecycle leaked an active target");
    }

    private static void Throws<T>(Action run) where T : Exception
    {
        try { run(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name);
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
