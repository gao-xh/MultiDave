using System;
using System.Threading;
using System.Threading.Tasks;
using DaveCoop.Core.World;

internal static class MapOriginRegistryTests
{
    internal static void IteratorScopesStayFixedAndUnknownShadowsParent()
    {
        var registry = New(); Accepted(registry.BeginOwner(1, out long first));
        Accepted(registry.EnterOwner(first, out long entry));
        Accepted(registry.RegisterIterator(10, registry.CurrentOwnerLife, out long iterator));
        Accepted(registry.ExitScope(entry));
        Assert(registry.CurrentOwnerLife == 0, "factory owner leaked across yield");
        Accepted(registry.EnterMoveNext(10, iterator, out long resume));
        Assert(registry.CurrentOwnerLife == first, "MoveNext did not restore the factory owner");
        Accepted(registry.RegisterIterator(11, registry.CurrentOwnerLife, out long child));
        Assert(registry.EnterMoveNext(999, 0, out long unknown) == MapOriginStatus.Unbound && unknown != 0 &&
            registry.CurrentOwnerLife == 0, "unknown nested iterator borrowed its parent owner");
        Assert(registry.RegisterOperation(unknown, 70, 0, "unowned", out _) == MapOriginStatus.Unbound &&
            registry.OperationCount == 0, "unowned call created an operation binding");
        Accepted(registry.RegisterIterator(12, 0, out long unbound), MapOriginStatus.Unbound);
        Accepted(registry.ExitScope(unknown)); Assert(registry.CurrentOwnerLife == first, "scope pop lost its parent");
        Accepted(registry.ExitScope(resume));
        Accepted(registry.EnterMoveNext(11, child, out long later));
        Assert(registry.CurrentOwnerLife == first, "child factory lost its fixed owner after yield");
        Accepted(registry.ExitScope(later));

        Accepted(registry.BeginOwner(1, out long second)); Assert(second > first, "entry reused an owner life");
        Accepted(registry.EnterOwner(second, out long next));
        Assert(registry.EnterMoveNext(10, iterator, out long stale) == MapOriginStatus.Unbound && stale != 0 &&
            registry.CurrentOwnerLife == 0, "old iterator rebound the replacement entry");
        Assert(registry.RegisterOperation(stale, 70, 0, "late", out _) == MapOriginStatus.Unbound,
            "late old iterator acquired a new operation owner");
        Accepted(registry.ExitScope(stale)); Assert(registry.CurrentOwnerLife == second, "old scope damaged the new entry");
        Assert(registry.RegisterIterator(10, second, out _) == MapOriginStatus.Retired,
            "retired iterator pointer was relabelled as a new life");
        Assert(registry.EnterMoveNext(12, unbound, out long stillUnknown) == MapOriginStatus.Unbound &&
            registry.CurrentOwnerLife == 0, "unbound factory was later upgraded from a current scope");
        Accepted(registry.ExitScope(stillUnknown)); Accepted(registry.ExitScope(next));
    }

    internal static void BootstrapAndLateSceneCompletionProduceOwnedChoicesOnly()
    {
        var registry = New(); Accepted(registry.BeginOwner(1, out long owner));
        Accepted(registry.EnterOwner(owner, out long entry));
        Accepted(registry.RegisterOperation(entry, 100, 0, "bootstrap-not-in-route", out long bootstrap));
        Accepted(registry.CompleteOperation(bootstrap, 100, 0, 90, out long bootstrapScene));
        Assert(registry.TryGetSceneOwner(90, out long managerOwner) && managerOwner == owner && bootstrapScene > 0,
            "bootstrap scene required a dive route or borrowed a singleton owner");
        Accepted(registry.RegisterIterator(101, managerOwner, out long managerIterator));
        Accepted(registry.ExitScope(entry));
        Accepted(registry.EnterMoveNext(101, managerIterator, out long manager));
        MapRouteSelection route = Route(); string fingerprint = MapSelections.FingerprintRoute(route);
        Accepted(registry.BindRoute(manager, 500, route));
        route.Scenes[0].SceneName = "mutated-route"; route.Scenes[1] = null;
        Accepted(registry.RegisterOperation(manager, 200, 7, "scene-1-address", out long operation));
        Accepted(registry.ExitScope(manager));
        Accepted(registry.RegisterControllerBirth(300, 91, "scene-1", out long controller), MapOriginStatus.Pending);
        MapGroupSelection first = Choice("selected-1");
        Accepted(registry.ObserveChoice(controller, 1, first), MapOriginStatus.Pending);
        first.SelectedPrefabName = "caller-mutated";
        Assert(registry.PendingChoiceCount == 1 && !registry.TryTakeBoundChoice(out _),
            "birth scene/name alone proved a resource request");
        Accepted(registry.CompleteOperation(operation, 200, 7, 91, out long scene));
        Assert(registry.TryTakeBoundChoice(out MapOriginChoiceEvidence frozen) && frozen.OwnerLife == owner &&
            frozen.ContextPointer == 500 && frozen.RouteFingerprint == fingerprint && frozen.OperationLife == operation &&
            frozen.SceneLife == scene && frozen.SceneHandle == 91 && frozen.ControllerLife == controller &&
            frozen.Choice.SceneId == 1 && frozen.Choice.SelectedPrefabName == "selected-1" && frozen.CallbackSequence == 1,
            "late exact completion lost the owned choice or bound it to a different origin");
        Assert(frozen.ObservationOnly && !frozen.NativeGenerationBound && !frozen.HostSelectionApplied &&
            !frozen.NativePermission && !frozen.CrossMachineAddressVerified, "synthetic registry facts granted native authority");
        MapGroupSelection second = Choice("selected-2"); Accepted(registry.ObserveChoice(controller, 2, second));
        Accepted(registry.ObserveChoice(controller, 2, Choice("selected-2")), MapOriginStatus.Duplicate);
        frozen.Choice.SelectedPrefabName = "output-mutated"; second.SelectedPrefabName = "input-mutated";
        Assert(registry.TryTakeBoundChoice(out MapOriginChoiceEvidence next) && next.Choice.SelectedPrefabName == "selected-2" &&
            next.RouteFingerprint == fingerprint && !registry.TryTakeBoundChoice(out _), "output/input copies changed stored evidence");

        // The controller may also have an exact scene before the route exists.
        var early = New(); Accepted(early.BeginOwner(1, out long earlyOwner));
        Accepted(early.EnterOwner(earlyOwner, out long earlyScope));
        Accepted(early.RegisterOperation(earlyScope, 2, 0, "first-scene", out long earlyOp));
        Accepted(early.CompleteOperation(earlyOp, 2, 0, 3, out _));
        Accepted(early.RegisterControllerBirth(4, 3, "scene-1", out long earlyController));
        Accepted(early.ObserveChoice(earlyController, 1, Choice()), MapOriginStatus.Pending);
        Assert(!early.TryTakeBoundChoice(out _), "missing route was filled from current global data");
        Accepted(early.BindRoute(earlyScope, 5, Route()));
        Assert(early.TryTakeBoundChoice(out MapOriginChoiceEvidence complete) && complete.Choice.SceneId == 1,
            "route arriving after exact result could not complete the same birth chain");
    }

    internal static void TombstonesRejectPointerVersionHandleAndEntryReplay()
    {
        var registry = New(); Bound(registry, out long first, out long scope, out long operation, out long controller, out long scene);
        Accepted(registry.RegisterIterator(40, first, out long iterator));
        Accepted(registry.ObserveChoice(controller, 1, Choice()));
        Accepted(registry.ExitScope(scope)); Accepted(registry.BeginOwner(1, out long second));
        Assert(!registry.TryTakeBoundChoice(out _) && !registry.TryGetSceneOwner(10, out _) &&
            registry.CompleteOperation(operation, 200, 1, 10, out _) == MapOriginStatus.Retired,
            "new entry retained an old ready choice/result owner");
        Assert(registry.ObserveChoice(controller, 2, Choice()) == MapOriginStatus.Retired &&
            registry.RegisterControllerBirth(300, 10, "scene-1", out _) == MapOriginStatus.Retired &&
            registry.RegisterIterator(40, second, out _) == MapOriginStatus.Retired,
            "retired controller/iterator pointer revived under the new entry");
        Accepted(registry.EnterOwner(second, out long newScope));
        Assert(registry.RegisterOperation(newScope, 200, 1, "scene-1", out _) == MapOriginStatus.Retired,
            "same pointer/version operation tombstone rebound a new owner");
        Accepted(registry.RegisterOperation(newScope, 200, 2, "scene-1", out long replacement));
        Assert(registry.CompleteOperation(replacement, 200, 2, 10, out _) == MapOriginStatus.Retired,
            "retired scene handle acquired a new scene life");
        Accepted(registry.CompleteOperation(replacement, 200, 2, 11, out long newScene));
        Assert(newScene > scene && registry.TryGetSceneOwner(11, out long actual) && actual == second,
            "fresh operation version did not bind its new actual scene handle");
        Assert(registry.EnterMoveNext(40, iterator, out long oldResume) == MapOriginStatus.Unbound,
            "late old iterator executed in the new entry");
        Accepted(registry.ExitScope(oldResume)); Accepted(registry.ExitScope(newScope));

        var changedVersion = New(); Bound(changedVersion, out _, out long cvScope, out _, out long cvController, out _);
        Accepted(changedVersion.ObserveChoice(cvController, 1, Choice()));
        Accepted(changedVersion.RegisterOperation(cvScope, 600, 3, "scene-2", out long cvOp));
        Assert(changedVersion.CompleteOperation(cvOp, 600, 4, 12, out _) == MapOriginStatus.Conflict &&
            !changedVersion.Healthy && !changedVersion.TryTakeBoundChoice(out _),
            "changed operation version preserved previously queued evidence");
    }

    internal static void PendingBirthCannotReviveAfterUnloadDestroyOrNewEntry()
    {
        // Bootstrap/empty scenes may unload before either completion or birth.
        var empty = New(); Accepted(empty.BeginOwner(1, out long emptyOwner));
        Accepted(empty.EnterOwner(emptyOwner, out long emptyScope));
        Accepted(empty.RegisterOperation(emptyScope, 200, 1, "bootstrap", out long emptyOperation));
        Accepted(empty.RetireScene(10));
        Assert(empty.CompleteOperation(emptyOperation, 200, 1, 10, out _) == MapOriginStatus.Retired &&
            !empty.TryGetSceneOwner(10, out _) && empty.RegisterControllerBirth(300, 10, "scene-1", out _) == MapOriginStatus.Retired,
            "unload before any birth/completion permitted a late bootstrap ownership claim");

        for (int mode = 0; mode < 3; mode++)
        {
            var registry = New(); Accepted(registry.BeginOwner(1, out long owner));
            Accepted(registry.EnterOwner(owner, out long scope)); Accepted(registry.BindRoute(scope, 5, Route()));
            Accepted(registry.RegisterOperation(scope, 200, 1, "scene-1", out long operation));
            Accepted(registry.RegisterControllerBirth(300, 10, "scene-1", out long controller), MapOriginStatus.Pending);
            Accepted(registry.ObserveChoice(controller, 1, Choice()), MapOriginStatus.Pending);
            Accepted(registry.ExitScope(scope));
            if (mode == 0) Accepted(registry.RetireScene(10));
            if (mode == 1) Accepted(registry.RetireController(controller));
            if (mode == 2) Accepted(registry.BeginOwner(1, out _));
            MapOriginStatus completion = registry.CompleteOperation(operation, 200, 1, 10, out _);
            Assert(completion == (mode == 1 ? MapOriginStatus.Accepted : MapOriginStatus.Retired) &&
                registry.PendingChoiceCount == 0 && !registry.TryTakeBoundChoice(out _) &&
                registry.ObserveChoice(controller, 2, Choice()) == MapOriginStatus.Retired,
                "late exact completion revived a retired pending birth (case " + mode + ")");
        }

        var outside = New();
        Accepted(outside.RegisterControllerBirth(100, 10, "scene-1", out long noEntry), MapOriginStatus.Unbound);
        Accepted(outside.BeginOwner(1, out long next)); Accepted(outside.EnterOwner(next, out long nextScope));
        Accepted(outside.BindRoute(nextScope, 5, Route()));
        Accepted(outside.RegisterOperation(nextScope, 200, 1, "scene-1", out long later));
        Accepted(outside.CompleteOperation(later, 200, 1, 10, out _));
        Assert(outside.ObserveChoice(noEntry, 1, Choice()) == MapOriginStatus.Retired && !outside.TryTakeBoundChoice(out _),
            "observer enabled late attached an old birth to a subsequent entry");
    }

    internal static void RepeatedCacheAndFailedOperationRetireWithoutRebinding()
    {
        var registry = New(); Bound(registry, out long owner, out long scope, out long operation, out long controller, out _);
        Accepted(registry.ObserveChoice(controller, 1, Choice()));
        Assert(registry.BindRoute(scope, 500, Route()) == MapOriginStatus.Retired && registry.Healthy &&
            registry.ActiveOwnerLife == 0 && registry.CurrentOwnerLife == 0 && registry.ReadyChoiceCount == 0 &&
            registry.CompleteOperation(operation, 200, 1, 10, out _) == MapOriginStatus.Retired,
            "same-fingerprint cache was deduplicated instead of retiring the fixed entry");
        Accepted(registry.ExitScope(scope));
        Accepted(registry.BeginOwner(1, out long next)); Accepted(registry.EnterOwner(next, out long nextScope));
        Accepted(registry.BindRoute(nextScope, 501, Route()));
        Accepted(registry.RegisterOperation(nextScope, 201, 1, "scene-2", out long failed));
        Accepted(registry.RetireOperation(failed));
        Assert(registry.Healthy && registry.ActiveOwnerLife == 0 && registry.RetireOwner(next) == MapOriginStatus.Duplicate,
            "native load failure retained its origin or prevented a new natural entry");
        Accepted(registry.ExitScope(nextScope)); Accepted(registry.BeginOwner(1, out long recovered));
        Assert(recovered > next && next > owner && registry.ObserveChoice(999, 2, Choice()) == MapOriginStatus.Unbound,
            "recovery reused old life or selection fabricated a controller birth");
    }

    internal static void ThreadReadLossScopeAndQuotaFaultsRevokeAllEvidence()
    {
        var wrongThread = New(); Bound(wrongThread, out _, out _, out _, out long controller, out _);
        Accepted(wrongThread.ObserveChoice(controller, 1, Choice()));
        MapOriginStatus result = Task.Run(() => wrongThread.RegisterControllerBirth(999, 10, "scene-1", out _)).GetAwaiter().GetResult();
        Assert(result == MapOriginStatus.WrongThread && !wrongThread.Healthy && wrongThread.ReadyChoiceCount == 0 &&
            wrongThread.BeginOwner(1, out _) == MapOriginStatus.Faulted, "off-thread callback retained/reopened evidence");

        var lost = New(); Bound(lost, out _, out _, out _, out long lostController, out _);
        Accepted(lost.ObserveChoice(lostController, 1, Choice()));
        Assert(lost.Invalidate("native read failed or callback lost") == MapOriginStatus.Faulted &&
            !lost.Healthy && lost.ReadyChoiceCount == 0 && !lost.TryTakeBoundChoice(out _), "read/loss fault kept queued choices");

        var nesting = New(); Accepted(nesting.BeginOwner(1, out long nestedOwner));
        Accepted(nesting.EnterOwner(nestedOwner, out long outer)); Accepted(nesting.EnterOwner(nestedOwner, out _));
        Assert(nesting.ExitScope(outer) == MapOriginStatus.Conflict && !nesting.Healthy && nesting.CurrentOwnerLife == 0,
            "mismatched finalizer left a usable parent scope");

        var quota = New(); Bound(quota, out _, out _, out _, out long quotaController, out _);
        for (int index = 1; index <= MapOriginRegistry.MaxChoices; index++)
            Accepted(quota.ObserveChoice(quotaController, index, Choice("choice-" + index)));
        Assert(quota.ObserveChoice(quotaController, MapOriginRegistry.MaxChoices + 1, Choice("overflow")) == MapOriginStatus.LimitExceeded &&
            quota.ReadyChoiceCount == 0 && !quota.Healthy, "choice overflow silently evicted older evidence");

        var iteratorQuota = New(); Accepted(iteratorQuota.BeginOwner(1, out long quotaOwner));
        for (int index = 1; index <= MapOriginRegistry.MaxIterators; index++) Accepted(iteratorQuota.RegisterIterator(index, quotaOwner, out _));
        Assert(iteratorQuota.RegisterIterator(9999, quotaOwner, out _) == MapOriginStatus.LimitExceeded &&
            iteratorQuota.IteratorCount == MapOriginRegistry.MaxIterators && !iteratorQuota.Healthy,
            "iterator quota evicted a replay tombstone or grew without bound");

        var sceneQuota = New();
        for (int index = 1; index <= MapOriginRegistry.MaxScenes; index++) Accepted(sceneQuota.RetireScene(index));
        Assert(sceneQuota.RetireScene(MapOriginRegistry.MaxScenes) == MapOriginStatus.Duplicate &&
            sceneQuota.RetireScene(MapOriginRegistry.MaxScenes + 1) == MapOriginStatus.LimitExceeded &&
            sceneQuota.SceneHandleCount == MapOriginRegistry.MaxScenes && !sceneQuota.Healthy,
            "unknown unload fences were unbounded, evicted, or double-counted on replay");

        var conflict = New(); Bound(conflict, out _, out _, out _, out long conflictController, out _);
        Accepted(conflict.ObserveChoice(conflictController, 1, Choice()));
        Assert(conflict.ObserveChoice(conflictController, 1, Choice("changed-same-callback")) == MapOriginStatus.Conflict &&
            conflict.ReadyChoiceCount == 0 && !conflict.Healthy, "conflicting callback replay retained the prior choice");
    }

    private static MapOriginRegistry New() => new MapOriginRegistry(Thread.CurrentThread.ManagedThreadId);

    private static void Bound(MapOriginRegistry registry, out long owner, out long scope, out long operation,
        out long controller, out long scene)
    {
        Accepted(registry.BeginOwner(1, out owner)); Accepted(registry.EnterOwner(owner, out scope));
        Accepted(registry.BindRoute(scope, 500, Route()));
        Accepted(registry.RegisterOperation(scope, 200, 1, "scene-1", out operation));
        Accepted(registry.CompleteOperation(operation, 200, 1, 10, out scene));
        Accepted(registry.RegisterControllerBirth(300, 10, "scene-1", out controller));
    }

    private static MapGroupSelection Choice(string name = "selected") => new MapGroupSelection
    { SceneId = 0, ControllerAddress = "root/controller[0]", Addressable = true, SelectedPrefabName = name };

    private static MapRouteSelection Route()
    {
        var scenes = new MapRouteScene[3];
        for (int index = 0; index < scenes.Length; index++) scenes[index] = new MapRouteScene
        {
            SceneId = index + 1, SceneName = "scene-" + (index + 1), Layer = 'A',
            MapHeight = 10, PreviousSceneId = index == 0 ? 0 : index,
            NextSceneId = index == scenes.Length - 1 ? 0 : index + 2
        };
        return new MapRouteSelection { EntrySceneId = 1, Scenes = scenes };
    }

    private static void Accepted(MapOriginStatus actual, MapOriginStatus expected = MapOriginStatus.Accepted)
    { Assert(actual == expected, "Expected " + expected + " but received " + actual); }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
