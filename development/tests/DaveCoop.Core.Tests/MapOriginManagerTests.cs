using System;
using System.Threading;
using System.Threading.Tasks;
using DaveCoop.Core.World;

internal static class MapOriginManagerTests
{
    internal static void PendingManagerCommitsOwnedRouteAndIteratorOnlyOnExactCompletion()
    {
        var fixture = new Fixture();
        long[] eligible = { fixture.Operation };
        Check(fixture.Registry.RegisterManagerBirth(200, 90, eligible, out long manager), MapOriginStatus.Pending);
        eligible[0] = 999; // The birth's eligible set is owned, not caller storage.
        Check(fixture.Registry.RegisterManagerIterator(manager, 200, 300, out long iterator), MapOriginStatus.Pending);
        Check(fixture.Registry.EnterMoveNext(300, iterator, out long resume), MapOriginStatus.Unbound);
        MapRouteSelection route = Route(); string fingerprint = MapSelections.FingerprintRoute(route);
        Check(fixture.Registry.BindRoute(resume, 500, route), MapOriginStatus.Pending);
        route.Scenes[0].SceneName = "caller-mutated";
        Check(fixture.Registry.RegisterControllerBirth(400, 90, "scene-1", out long controller), MapOriginStatus.Pending);
        Check(fixture.Registry.ObserveChoice(controller, 1, Choice()), MapOriginStatus.Pending);
        Assert(fixture.Registry.TryCaptureSource(out MapOriginSourceSnapshot pending) && pending.OwnerLife == fixture.Owner &&
            pending.Route == null && pending.Choices.Length == 0 && fixture.Registry.PendingManagerCount == 1 &&
            !fixture.Registry.TryGetManagerOwner(manager, out _), "pending manager became an adopted source before exact completion");
        Check(fixture.Registry.CompleteOperation(fixture.Operation, 100, 7, 90, out long scene));
        Assert(fixture.Registry.CurrentOwnerLife == 0, "completion changed the owner of an already-restored MoveNext scope");
        Assert(fixture.Registry.TryGetManagerOwner(manager, out long owner) && owner == fixture.Owner &&
            fixture.Registry.TryGetRouteOwner(500, out long contextOwner) && contextOwner == owner,
            "exact operation could not attach the frozen manager birth and context");
        Assert(fixture.Registry.TryCaptureSource(out MapOriginSourceSnapshot complete) && complete.Route != null &&
            complete.RouteFingerprint == fingerprint && complete.Route.Scenes[0].SceneName == "scene-1" &&
            complete.Choices.Length == 1 && complete.Choices[0].SceneLife == scene &&
            fixture.Registry.ReadyChoiceCount == 1, "owned pending route/choice was lost or source capture consumed its diagnostics");
        complete.Route.Scenes[0].SceneName = "output-mutated";
        Assert(fixture.Registry.TryCaptureSource(out MapOriginSourceSnapshot again) && again.Route.Scenes[0].SceneName == "scene-1" &&
            !again.NativeGenerationBound && !again.NativePermission && !again.HostSelectionApplied,
            "source ownership or synthetic scalar facts granted native permission");
        Check(fixture.Registry.ExitScope(resume));
        Check(fixture.Registry.EnterMoveNext(300, iterator, out long next));
        Assert(fixture.Registry.CurrentOwnerLife == owner && fixture.Registry.PendingManagerCount == 0,
            "next resume did not use the manager's once-proven owner");
        Check(fixture.Registry.ExitScope(next));
        Check(fixture.Registry.RegisterManagerIterator(manager, 200, 300, out long duplicate), MapOriginStatus.Duplicate);
        Assert(duplicate == iterator, "same returned iterator acquired a second life");
    }

    internal static void LaterOperationsAndUnknownNestedScopesCannotSupplyManagerOrigin()
    {
        var unbound = new Fixture(false);
        Check(unbound.Registry.RegisterManagerBirth(200, 90, Array.Empty<long>(), out long manager), MapOriginStatus.Unbound);
        Check(unbound.Registry.RegisterManagerIterator(manager, 200, 300, out long iterator), MapOriginStatus.Unbound);
        Check(unbound.Registry.EnterOwner(unbound.Owner, out long entry));
        Check(unbound.Registry.RegisterOperation(entry, 100, 7, "late-bootstrap", out long late));
        Check(unbound.Registry.CompleteOperation(late, 100, 7, 90, out _));
        Assert(!unbound.Registry.TryGetManagerOwner(manager, out _) && unbound.Registry.IsManagerRetired(manager),
            "an operation observed after manager birth retroactively assigned its owner");
        Check(unbound.Registry.EnterMoveNext(300, iterator, out long unknown), MapOriginStatus.Unbound);
        Check(unbound.Registry.BindRoute(unknown, 500, Route()), MapOriginStatus.Unbound);
        Check(unbound.Registry.ExitScope(unknown)); Check(unbound.Registry.ExitScope(entry));

        var pending = new Fixture();
        Check(pending.Registry.RegisterManagerBirth(200, 90, new[] { pending.Operation }, out long pendingManager), MapOriginStatus.Pending);
        Check(pending.Registry.RegisterManagerIterator(pendingManager, 200, 300, out long pendingIterator), MapOriginStatus.Pending);
        Check(pending.Registry.EnterMoveNext(300, pendingIterator, out long outer), MapOriginStatus.Unbound);
        Check(pending.Registry.EnterMoveNext(999, 0, out long child), MapOriginStatus.Unbound);
        Check(pending.Registry.BindRoute(child, 500, Route()), MapOriginStatus.Unbound);
        Check(pending.Registry.ExitScope(child)); Check(pending.Registry.ExitScope(outer));
        Check(pending.Registry.EnterOwner(pending.Owner, out long scope));
        Check(pending.Registry.RegisterOperation(scope, 101, 0, "future", out long future));
        Check(pending.Registry.ExitScope(scope));
        Check(pending.Registry.CompleteOperation(future, 101, 0, 90, out _));
        Assert(pending.Registry.IsManagerRetired(pendingManager) && pending.Registry.TryCaptureSource(out MapOriginSourceSnapshot source) &&
            source.Route == null, "wrong operation/unknown child borrowed the pending manager's birth lineage");
    }

    internal static void DestroyUnloadFailureAndReplacementEntryRetirePendingBirths()
    {
        for (int boundary = 0; boundary < 4; boundary++)
        {
            var fixture = new Fixture();
            Check(fixture.Registry.RegisterManagerBirth(200, 90, new[] { fixture.Operation }, out long manager), MapOriginStatus.Pending);
            Check(fixture.Registry.RegisterManagerIterator(manager, 200, 300, out long iterator), MapOriginStatus.Pending);
            Check(fixture.Registry.EnterMoveNext(300, iterator, out long scope), MapOriginStatus.Unbound);
            Check(fixture.Registry.BindRoute(scope, 500, Route()), MapOriginStatus.Pending);
            Check(fixture.Registry.ExitScope(scope));
            if (boundary == 0) Check(fixture.Registry.RetireManager(manager)); // Destroy or original factory/MoveNext exception.
            else if (boundary == 1) Check(fixture.Registry.RetireScene(90));
            else if (boundary == 2) Check(fixture.Registry.RetireOperation(fixture.Operation));
            else Check(fixture.Registry.BeginOwner(2, out _));
            Check(fixture.Registry.CompleteOperation(fixture.Operation, 100, 7, 90, out _), MapOriginStatus.Retired);
            Assert(fixture.Registry.IsManagerRetired(manager) && !fixture.Registry.TryGetManagerOwner(manager, out _) &&
                fixture.Registry.TryCaptureSource(out MapOriginSourceSnapshot source) && source.Route == null && source.Choices.Length == 0,
                "retired pending manager revived its route after boundary " + boundary);
            Check(fixture.Registry.EnterMoveNext(300, iterator, out long stale), MapOriginStatus.Unbound);
            Check(fixture.Registry.BindRoute(stale, 501, Route()), MapOriginStatus.Unbound);
            Check(fixture.Registry.ExitScope(stale));
            Check(fixture.Registry.RegisterManagerBirth(200, 90, Array.Empty<long>(), out _), MapOriginStatus.Retired);
        }
    }

    internal static void CompletedBirthAndRepeatedCacheRetainOneShotRouteBoundary()
    {
        var fixture = new Fixture();
        Check(fixture.Registry.CompleteOperation(fixture.Operation, 100, 7, 90, out _));
        Check(fixture.Registry.RegisterManagerBirth(200, 90, Array.Empty<long>(), out long manager));
        Check(fixture.Registry.RegisterManagerIterator(manager, 200, 300, out long iterator));
        Check(fixture.Registry.EnterMoveNext(300, iterator, out long scope));
        Check(fixture.Registry.BindRoute(scope, 500, Route()));
        Check(fixture.Registry.RegisterControllerBirth(400, 90, "scene-1", out long controller));
        Check(fixture.Registry.ObserveChoice(controller, 1, Choice()));
        Assert(fixture.Registry.TryCaptureSource(out MapOriginSourceSnapshot source) && source.Route != null && source.Choices.Length == 1,
            "already-proven bootstrap scene unnecessarily stayed unbound");
        Check(fixture.Registry.BindRoute(scope, 500, Route()), MapOriginStatus.Retired);
        Assert(fixture.Registry.Healthy && fixture.Registry.ActiveOwnerLife == 0 && fixture.Registry.IsManagerRetired(manager) &&
            fixture.Registry.ReadyChoiceCount == 0, "same-hash cache boundary retained the prior entry's manager/choice");
        Check(fixture.Registry.ExitScope(scope));

        var destroyed = new Fixture();
        Check(destroyed.Registry.CompleteOperation(destroyed.Operation, 100, 7, 90, out _));
        Check(destroyed.Registry.RegisterManagerBirth(200, 90, Array.Empty<long>(), out long live));
        Check(destroyed.Registry.RegisterManagerIterator(live, 200, 300, out long liveIterator));
        Check(destroyed.Registry.EnterMoveNext(300, liveIterator, out long liveScope));
        Check(destroyed.Registry.BindRoute(liveScope, 500, Route())); Check(destroyed.Registry.ExitScope(liveScope));
        Check(destroyed.Registry.RetireScene(90));
        Assert(destroyed.Registry.ActiveOwnerLife == 0 && destroyed.Registry.IsManagerRetired(live) &&
            destroyed.Registry.TryCaptureSource(out MapOriginSourceSnapshot empty) && empty.OwnerLife == 0 && empty.Route == null,
            "unloaded route-producing manager left a current source behind");

        var staged = new Fixture();
        Check(staged.Registry.RegisterManagerBirth(200, 90, new[] { staged.Operation }, out long stagedManager), MapOriginStatus.Pending);
        Check(staged.Registry.RegisterManagerIterator(stagedManager, 200, 300, out long stagedIterator), MapOriginStatus.Pending);
        Check(staged.Registry.EnterMoveNext(300, stagedIterator, out long stagedScope), MapOriginStatus.Unbound);
        Check(staged.Registry.BindRoute(stagedScope, 500, Route()), MapOriginStatus.Pending);
        Check(staged.Registry.BindRoute(stagedScope, 500, Route()), MapOriginStatus.Retired);
        Check(staged.Registry.ExitScope(stagedScope));
        Check(staged.Registry.CompleteOperation(staged.Operation, 100, 7, 90, out _), MapOriginStatus.Retired);
        Assert(staged.Registry.ActiveOwnerLife == 0 && staged.Registry.IsManagerRetired(stagedManager),
            "repeated pending cache was deduplicated by hash and revived at late completion");
    }

    internal static void ManagerAndIteratorIdentityConflictsCannotRebind()
    {
        var actor = new Fixture();
        Check(actor.Registry.RegisterManagerBirth(200, 90, new[] { actor.Operation }, out long manager), MapOriginStatus.Pending);
        Check(actor.Registry.RegisterManagerIterator(manager, 201, 300, out _), MapOriginStatus.Conflict);
        Assert(!actor.Registry.Healthy && !actor.Registry.TryCaptureSource(out _), "foreign manager actor retained usable source");

        var returned = new Fixture();
        Check(returned.Registry.RegisterManagerBirth(200, 90, new[] { returned.Operation }, out long managerLife), MapOriginStatus.Pending);
        Check(returned.Registry.RegisterManagerIterator(managerLife, 200, 300, out _), MapOriginStatus.Pending);
        Check(returned.Registry.RegisterManagerIterator(managerLife, 200, 301, out _), MapOriginStatus.Conflict);
        Assert(!returned.Registry.Healthy, "same manager accepted a second returned iterator");

        var ordinary = new Fixture();
        Check(ordinary.Registry.RegisterIterator(300, 0, out _), MapOriginStatus.Unbound);
        Check(ordinary.Registry.RegisterManagerBirth(200, 90, new[] { ordinary.Operation }, out long ordinaryManager), MapOriginStatus.Pending);
        Check(ordinary.Registry.RegisterManagerIterator(ordinaryManager, 200, 300, out _), MapOriginStatus.Conflict);
        Assert(!ordinary.Registry.Healthy, "generic unbound iterator was upgraded into manager lineage");

        var changed = new Fixture();
        Check(changed.Registry.RegisterManagerBirth(200, 90, new[] { changed.Operation }, out _), MapOriginStatus.Pending);
        Check(changed.Registry.RegisterManagerBirth(200, 91, new[] { changed.Operation }, out _), MapOriginStatus.Conflict);
        Assert(!changed.Registry.Healthy, "manager pointer was reused for a different birth scene");
    }

    internal static void ManagerQuotasAndWrongThreadRevokeWithoutEvictingTombstones()
    {
        var quota = new Fixture(false);
        for (int index = 0; index < MapOriginRegistry.MaxManagers; index++)
        {
            Check(quota.Registry.RegisterManagerBirth(1000 + index, 100 + index, Array.Empty<long>(), out long life), MapOriginStatus.Unbound);
            Check(quota.Registry.RetireManager(life));
        }
        Check(quota.Registry.RegisterManagerBirth(9999, 999, Array.Empty<long>(), out _), MapOriginStatus.LimitExceeded);
        Assert(!quota.Registry.Healthy && quota.Registry.ManagerCount == MapOriginRegistry.MaxManagers,
            "manager quota evicted a birth tombstone or grew indefinitely");
        var wrong = new Fixture();
        MapOriginStatus result = Task.Run(() => wrong.Registry.RegisterManagerBirth(200, 90, new[] { wrong.Operation }, out _)).GetAwaiter().GetResult();
        Assert(result == MapOriginStatus.WrongThread && !wrong.Registry.Healthy && !wrong.Registry.TryCaptureSource(out _),
            "wrong-thread birth was retained for later owner attachment");
    }

    private sealed class Fixture
    {
        internal readonly MapOriginRegistry Registry = new MapOriginRegistry(Thread.CurrentThread.ManagedThreadId);
        internal readonly long Owner, Operation;
        internal Fixture(bool registerOperation = true)
        {
            Check(Registry.BeginOwner(1, out long owner)); Owner = owner;
            if (!registerOperation) return;
            Check(Registry.EnterOwner(owner, out long scope));
            Check(Registry.RegisterOperation(scope, 100, 7, "bootstrap", out long operation)); Operation = operation;
            Check(Registry.ExitScope(scope));
        }
    }

    private static MapRouteSelection Route()
    {
        var scenes = new MapRouteScene[3];
        for (int index = 0; index < scenes.Length; index++) scenes[index] = new MapRouteScene
        {
            SceneId = index + 1, SceneName = "scene-" + (index + 1), Layer = 'A', MapHeight = 10,
            Priority = index, PreferenceWeight = index + 1, PreloadAndNotUnloadable = index == 0,
            PreviousSceneId = index == 0 ? 0 : index, NextSceneId = index == 2 ? 0 : index + 2
        };
        return new MapRouteSelection { EntrySceneId = 1, TotalSceneHeight = 30, Scenes = scenes };
    }
    private static MapGroupSelection Choice() => new MapGroupSelection
    { SceneId = 0, ControllerAddress = "root/controller[0]", Addressable = true, SelectedPrefabName = "selected" };
    private static void Check(MapOriginStatus actual, MapOriginStatus expected = MapOriginStatus.Accepted)
    { Assert(actual == expected, "Expected " + expected + " but received " + actual); }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
