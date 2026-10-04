using System;
using System.Threading;
using System.Threading.Tasks;
using DaveCoop.Core.World;

internal static class MapOriginControllerTests
{
    internal static void PendingControllerAndIteratorRequireExactFrozenCompletion()
    {
        var fixture = new Fixture(bindRoute: true);
        long other = fixture.AddOperation(101, 8, "another-scene");
        Check(fixture.Registry.RegisterControllerBirth(300, 90, "scene-a", out long controller), MapOriginStatus.Pending);
        Check(fixture.Registry.RegisterControllerIterator(controller, 300, 400, out long iterator), MapOriginStatus.Pending);
        MapGroupSelection choice = Choice();
        Check(fixture.Registry.ObserveChoice(controller, 11, choice), MapOriginStatus.Pending);
        choice.SelectedPrefabName = "caller-mutated";
        Assert(!fixture.Registry.TryGetControllerSource(controller, out _) && !fixture.Registry.TryTakeBoundChoice(out _),
            "a pending birth supplied an operation or adopted choice before actual completion");
        Check(fixture.Registry.EnterMoveNext(400, iterator, out long pendingScope), MapOriginStatus.Unbound);
        Check(fixture.Registry.CompleteOperation(fixture.Operation, 100, 7, 91, out _));
        Assert(!fixture.Registry.TryGetControllerSource(controller, out _), "a different frozen scene handle supplied this controller");
        Check(fixture.Registry.CompleteOperation(other, 101, 8, 90, out long scene));
        Assert(fixture.Registry.CurrentOwnerLife == 0, "completion changed an already-entered pending scope's owner");
        Assert(fixture.Registry.TryGetControllerSource(controller, out MapOriginControllerSource source) &&
            source.ControllerLife == controller && source.ControllerPointer == 300 && source.OwnerLife == fixture.Owner &&
            source.OperationLife == other && source.OperationPointer == 101 && source.OperationVersion == 8 &&
            source.SceneLife == scene && source.SceneHandle == 90 && source.SceneName == "scene-a" && source.LoadKey == "another-scene",
            "exact completion did not produce the fixed immutable local chain");
        Assert(fixture.Registry.TryTakeBoundChoice(out MapOriginChoiceEvidence evidence) && evidence.ControllerLife == controller &&
            evidence.OperationLife == source.OperationLife && evidence.SceneLife == source.SceneLife &&
            evidence.Choice.SelectedPrefabName == "chosen", "pending choice ownership or its exact source chain changed");
        Check(fixture.Registry.ExitScope(pendingScope));
        Check(fixture.Registry.EnterMoveNext(400, iterator, out long liveScope));
        Assert(fixture.Registry.CurrentOwnerLife == fixture.Owner, "the original controller iterator did not acquire its proven owner on the next resume");
        Check(fixture.Registry.ExitScope(liveScope));
        Check(fixture.Registry.RegisterControllerIterator(controller, 300, 400, out long duplicate), MapOriginStatus.Duplicate);
        Assert(duplicate == iterator, "a repeated original factory return minted another iterator life");
    }

    internal static void LaterOperationsAndRepeatedBirthCannotClaimOlderController()
    {
        var fixture = new Fixture();
        Check(fixture.Registry.RegisterControllerBirth(300, 90, "scene-a", out long controller), MapOriginStatus.Pending);
        Check(fixture.Registry.RegisterControllerIterator(controller, 300, 400, out long iterator), MapOriginStatus.Pending);
        // The same operation pointer with a newer version is still a later load.
        long later = fixture.AddOperation(100, 8, "later-scene");
        Check(fixture.Registry.RegisterControllerBirth(300, 90, "scene-a", out long repeated), MapOriginStatus.Pending);
        Assert(repeated == controller, "a repeated Init replaced its original birth identity");
        Check(fixture.Registry.CompleteOperation(fixture.Operation, 100, 7, 91, out _));
        Assert(!fixture.Registry.TryGetControllerSource(controller, out _), "the birth ignored its actual scene handle");
        Check(fixture.Registry.CompleteOperation(later, 100, 8, 90, out _));
        Assert(fixture.Registry.IsControllerRetired(controller) && !fixture.Registry.TryGetControllerSource(controller, out _) && fixture.Registry.Healthy,
            "a later same-owner load expanded the first birth's frozen operation set");
        Check(fixture.Registry.EnterMoveNext(400, iterator, out long stale), MapOriginStatus.Unbound);
        Check(fixture.Registry.ExitScope(stale));
        Check(fixture.Registry.RegisterControllerBirth(301, 90, "scene-a", out long fresh));
        Assert(fixture.Registry.TryGetControllerSource(fresh, out MapOriginControllerSource newSource) && newSource.OperationLife == later,
            "rejecting the older birth also rejected a distinct birth after exact completion");

        var noLoad = new Fixture(registerOperation: false);
        Check(noLoad.Registry.RegisterControllerBirth(300, 90, "scene-a", out long unbound), MapOriginStatus.Unbound);
        Check(noLoad.Registry.RegisterControllerIterator(unbound, 300, 400, out _), MapOriginStatus.Unbound);
        long afterBirth = noLoad.AddOperation(100, 7, "too-late");
        Check(noLoad.Registry.RegisterControllerBirth(300, 90, "scene-a", out _), MapOriginStatus.Unbound);
        Check(noLoad.Registry.CompleteOperation(afterBirth, 100, 7, 90, out _));
        Assert(noLoad.Registry.IsControllerRetired(unbound) && !noLoad.Registry.TryGetControllerSource(unbound, out _),
            "an entry pointer without an observed operation became ownership through a later callback");
    }

    internal static void CompletedControllerSourceCopiesRemainObservationOnly()
    {
        var fixture = new Fixture();
        Check(fixture.Registry.CompleteOperation(fixture.Operation, 100, 7, 90, out long scene));
        Check(fixture.Registry.RegisterControllerBirth(300, 90, "scene-a", out long controller));
        Check(fixture.Registry.RegisterControllerIterator(controller, 300, 400, out _));
        Assert(fixture.Registry.TryGetControllerSource(controller, out MapOriginControllerSource first) &&
            fixture.Registry.TryGetControllerSource(controller, out MapOriginControllerSource second) && !ReferenceEquals(first, second),
            "source capture returned mutable shared storage or omitted an already-completed birth");
        fixture.AddOperation(101, 9, "future");
        Check(fixture.Registry.RegisterControllerBirth(300, 90, "scene-a", out long duplicate), MapOriginStatus.Duplicate);
        Assert(duplicate == controller && first.SceneLife == scene && first.OperationVersion == 7 && first.ObservationOnly &&
            !first.NativeGenerationBound && !first.NativePermission && !first.HostSelectionApplied && !first.CrossMachineAddressVerified,
            "a copied local scalar chain granted native permission or changed when the operation table grew");
        Check(fixture.Registry.RetireController(controller));
        Assert(!fixture.Registry.TryGetControllerSource(controller, out MapOriginControllerSource retired) && retired == null &&
            first.SceneLife == scene && first.ControllerPointer == 300 && !first.NativePermission,
            "retirement returned stale current evidence or mutated a previously copied historical snapshot");
    }

    internal static void ControllerFactoryConflictsCannotUpgradeGenericIterators()
    {
        for (int boundary = 0; boundary < 5; boundary++)
        {
            var fixture = new Fixture();
            Check(fixture.Registry.RegisterControllerBirth(300, 90, "scene-a", out long controller), MapOriginStatus.Pending);
            MapOriginStatus conflict;
            if (boundary == 0) conflict = fixture.Registry.RegisterControllerIterator(controller, 301, 400, out _);
            else if (boundary == 1)
            {
                Check(fixture.Registry.RegisterControllerIterator(controller, 300, 400, out _), MapOriginStatus.Pending);
                conflict = fixture.Registry.RegisterControllerIterator(controller, 300, 401, out _);
            }
            else if (boundary == 2)
            {
                Check(fixture.Registry.RegisterIterator(400, 0, out _), MapOriginStatus.Unbound);
                conflict = fixture.Registry.RegisterControllerIterator(controller, 300, 400, out _);
            }
            else if (boundary == 3)
            {
                Check(fixture.Registry.RegisterControllerIterator(controller, 300, 400, out _), MapOriginStatus.Pending);
                Check(fixture.Registry.RegisterControllerBirth(301, 90, "scene-a", out long other), MapOriginStatus.Pending);
                conflict = fixture.Registry.RegisterControllerIterator(other, 301, 400, out _);
            }
            else conflict = fixture.Registry.RegisterControllerBirth(300, 90, "scene-b", out _);
            Check(conflict, MapOriginStatus.Conflict);
            Assert(!fixture.Registry.Healthy && !fixture.Registry.TryGetControllerSource(controller, out _),
                "an incompatible actor/factory/scene reused a controller source (case " + boundary + ")");
        }
    }

    internal static void UnknownChildrenAndRetirementMaskControllerScopesImmediately()
    {
        var fixture = new Fixture();
        Check(fixture.Registry.CompleteOperation(fixture.Operation, 100, 7, 90, out _));
        Check(fixture.Registry.RegisterControllerBirth(300, 90, "scene-a", out long controller));
        Check(fixture.Registry.RegisterControllerIterator(controller, 300, 400, out long iterator));
        Check(fixture.Registry.EnterMoveNext(400, iterator, out long outer));
        Check(fixture.Registry.EnterMoveNext(999, 0, out long unknown), MapOriginStatus.Unbound);
        Assert(fixture.Registry.CurrentOwnerLife == 0, "an unknown nested MoveNext borrowed the controller's parent owner");
        Check(fixture.Registry.RegisterOperation(unknown, 9999, 0, "unknown-child", out _), MapOriginStatus.Unbound);
        Check(fixture.Registry.ExitScope(unknown));
        Assert(fixture.Registry.CurrentOwnerLife == fixture.Owner, "the still-live parent did not recover after a paired unknown child");
        Check(fixture.Registry.RetireController(controller));
        Assert(fixture.Registry.CurrentOwnerLife == 0 && fixture.Registry.ActiveOwnerLife == fixture.Owner,
            "controller retirement left its entered scope usable merely because the entry itself remains live");
        Check(fixture.Registry.RegisterOperation(outer, 9998, 0, "retired-parent", out _), MapOriginStatus.Unbound);
        Check(fixture.Registry.ExitScope(outer));
        Check(fixture.Registry.EnterMoveNext(400, iterator, out long retired), MapOriginStatus.Unbound);
        Check(fixture.Registry.ExitScope(retired));
        Check(fixture.Registry.RegisterControllerBirth(300, 90, "scene-a", out _), MapOriginStatus.Retired);

        var order = new Fixture();
        Check(order.Registry.EnterOwner(order.Owner, out long parent));
        Check(order.Registry.EnterMoveNext(999, 0, out _), MapOriginStatus.Unbound);
        Check(order.Registry.ExitScope(parent), MapOriginStatus.Conflict);
        Assert(!order.Registry.Healthy && order.Registry.CurrentOwnerLife == 0, "a non-LIFO callback restored the parent source");
    }

    internal static void PendingControllerCannotReviveAfterLifecycleLoss()
    {
        for (int boundary = 0; boundary < 4; boundary++)
        {
            var fixture = new Fixture(bindRoute: true);
            Check(fixture.Registry.RegisterControllerBirth(300, 90, "scene-a", out long controller), MapOriginStatus.Pending);
            Check(fixture.Registry.RegisterControllerIterator(controller, 300, 400, out long iterator), MapOriginStatus.Pending);
            Check(fixture.Registry.ObserveChoice(controller, 1, Choice()), MapOriginStatus.Pending);
            if (boundary == 0) Check(fixture.Registry.RetireController(controller));
            else if (boundary == 1) Check(fixture.Registry.RetireScene(90));
            else if (boundary == 2) Check(fixture.Registry.RetireOperation(fixture.Operation));
            else Check(fixture.Registry.BeginOwner(2, out _));
            Check(fixture.Registry.CompleteOperation(fixture.Operation, 100, 7, 90, out _),
                boundary == 0 ? MapOriginStatus.Accepted : MapOriginStatus.Retired);
            Assert(fixture.Registry.IsControllerRetired(controller) && !fixture.Registry.TryGetControllerSource(controller, out _) &&
                fixture.Registry.PendingChoiceCount == 0 && !fixture.Registry.TryTakeBoundChoice(out _),
                "late completion revived pending controller evidence after lifecycle loss " + boundary);
            Check(fixture.Registry.EnterMoveNext(400, iterator, out long scope), MapOriginStatus.Unbound);
            Check(fixture.Registry.RegisterOperation(scope, 999, 0, "stale", out _), MapOriginStatus.Unbound);
            Check(fixture.Registry.ExitScope(scope));
            Check(fixture.Registry.RegisterControllerBirth(300, 90, "scene-a", out _), MapOriginStatus.Retired);
        }
    }

    internal static void ThreadAndQuotasInvalidateWithoutDroppingControllerFences()
    {
        var wrong = new Fixture();
        Check(wrong.Registry.CompleteOperation(wrong.Operation, 100, 7, 90, out _));
        Check(wrong.Registry.RegisterControllerBirth(300, 90, "scene-a", out long controller));
        bool captured = Task.Run(() => wrong.Registry.TryGetControllerSource(controller, out _)).GetAwaiter().GetResult();
        Assert(!captured && !wrong.Registry.Healthy && !wrong.Registry.TryGetControllerSource(controller, out _),
            "a foreign callback thread returned evidence usable again on the creator thread");

        var quota = new Fixture();
        for (int index = 0; index < MapOriginRegistry.MaxControllers; index++)
        {
            Check(quota.Registry.RegisterControllerBirth(1000 + index, 90, "scene-a", out long life), MapOriginStatus.Pending);
            Check(quota.Registry.RetireController(life));
        }
        Check(quota.Registry.RegisterControllerBirth(1000, 90, "scene-a", out _), MapOriginStatus.Retired);
        Check(quota.Registry.RegisterControllerBirth(9999, 90, "scene-a", out _), MapOriginStatus.LimitExceeded);
        Assert(!quota.Registry.Healthy && quota.Registry.ControllerCount == MapOriginRegistry.MaxControllers,
            "a quota evicted an old controller tombstone or allowed fresh source after trace loss");

        var iterators = new Fixture();
        Check(iterators.Registry.RegisterControllerBirth(300, 90, "scene-a", out long pending), MapOriginStatus.Pending);
        for (int index = 0; index < MapOriginRegistry.MaxIterators; index++)
            Check(iterators.Registry.RegisterIterator(1000 + index, 0, out _), MapOriginStatus.Unbound);
        Check(iterators.Registry.RegisterControllerIterator(pending, 300, 9999, out _), MapOriginStatus.LimitExceeded);
        Assert(!iterators.Registry.Healthy && iterators.Registry.IteratorCount == MapOriginRegistry.MaxIterators &&
            !iterators.Registry.TryGetControllerSource(pending, out _), "typed registration silently evicted a generic iterator fence");
    }

    private sealed class Fixture
    {
        internal readonly MapOriginRegistry Registry = new MapOriginRegistry(Thread.CurrentThread.ManagedThreadId);
        internal readonly long Owner, Operation;
        internal Fixture(bool registerOperation = true, bool bindRoute = false)
        {
            Check(Registry.BeginOwner(1, out long owner)); Owner = owner;
            Check(Registry.EnterOwner(owner, out long scope));
            if (bindRoute) Check(Registry.BindRoute(scope, 500, Route()));
            if (registerOperation) { Check(Registry.RegisterOperation(scope, 100, 7, "scene-a-key", out long operation)); Operation = operation; }
            Check(Registry.ExitScope(scope));
        }
        internal long AddOperation(long pointer, int version, string key)
        {
            Check(Registry.EnterOwner(Owner, out long scope));
            Check(Registry.RegisterOperation(scope, pointer, version, key, out long operation));
            Check(Registry.ExitScope(scope)); return operation;
        }
    }

    private static MapRouteSelection Route()
    {
        var scenes = new MapRouteScene[3];
        for (int index = 0; index < scenes.Length; index++) scenes[index] = new MapRouteScene
        {
            SceneId = index + 1, SceneName = "scene-" + (char)('a' + index), Layer = 'A', MapHeight = 10,
            Priority = index, PreferenceWeight = index + 1, PreloadAndNotUnloadable = index == 0,
            PreviousSceneId = index == 0 ? 0 : index, NextSceneId = index == 2 ? 0 : index + 2
        };
        return new MapRouteSelection { EntrySceneId = 1, TotalSceneHeight = 30, Scenes = scenes };
    }
    private static MapGroupSelection Choice() => new MapGroupSelection
    { SceneId = 0, ControllerAddress = "root/controller[0]", Addressable = true, SelectedPrefabName = "chosen" };
    private static void Check(MapOriginStatus actual, MapOriginStatus expected = MapOriginStatus.Accepted)
    { Assert(actual == expected, "Expected " + expected + " but received " + actual); }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
