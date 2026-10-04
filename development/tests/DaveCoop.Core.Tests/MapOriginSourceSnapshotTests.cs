using System;
using System.Threading;
using System.Threading.Tasks;
using DaveCoop.Core.World;

internal static class MapOriginSourceSnapshotTests
{
    internal static void SourceSnapshotsOwnLatestChoicesAndPreserveReadyQueue()
    {
        var registry = New();
        MapOriginSourceSnapshot empty = Capture(registry);
        Assert(empty.OwnerLife == 0 && empty.Route == null && empty.RouteFingerprint == null && empty.Choices.Length == 0,
            "empty registry invented a route owner");
        Accepted(registry.BeginOwner(1, out long owner));
        MapOriginSourceSnapshot noRoute = Capture(registry);
        Assert(noRoute.OwnerLife == owner && noRoute.Route == null && noRoute.Choices.Length == 0,
            "entry without a route borrowed global route data");
        Accepted(registry.EnterOwner(owner, out long scope));
        MapRouteSelection route = Route(); string fingerprint = MapSelections.FingerprintRoute(route);
        Accepted(registry.BindRoute(scope, 500, route));
        Accepted(registry.RegisterOperation(scope, 200, 1, "scene-1", out long operation));
        Accepted(registry.CompleteOperation(operation, 200, 1, 10, out _));
        Accepted(registry.RegisterControllerBirth(300, 10, "scene-1", out long first));
        Accepted(registry.RegisterControllerBirth(301, 10, "scene-1", out long second));
        Accepted(registry.ObserveChoice(second, 2, Choice("second", 1)));
        MapGroupSelection incoming = Choice("first", 0); Accepted(registry.ObserveChoice(first, 1, incoming));
        Accepted(registry.ObserveChoice(first, 3, Choice("first-latest", 0)));
        incoming.SelectedPrefabName = "input-mutated"; route.Scenes[0].SceneName = "input-mutated"; route.Scenes[1] = null;
        MapOriginSourceSnapshot snapshot = Capture(registry);
        Assert(snapshot.OwnerLife == owner && snapshot.RouteFingerprint == fingerprint && snapshot.Route.Scenes[0].SceneName == "scene-1" &&
            snapshot.Choices.Length == 2 && snapshot.Choices[0].ControllerLife == first && snapshot.Choices[1].ControllerLife == second &&
            snapshot.Choices[0].CallbackSequence == 3 && snapshot.Choices[0].Choice.SelectedPrefabName == "first-latest" &&
            snapshot.Choices[1].Choice.SelectedPrefabName == "second" && registry.ReadyChoiceCount == 3,
            "snapshot kept old choices, changed controller order, shared input, or consumed ready events");
        Assert(snapshot.ObservationOnly && !snapshot.NativeGenerationBound && !snapshot.HostSelectionApplied &&
            !snapshot.NativePermission && !snapshot.CrossMachineAddressVerified, "snapshot granted native or world authority");
        snapshot.OwnerLife = 999; snapshot.Route.Scenes[0].SceneName = "output-mutated"; snapshot.Route.Scenes[1] = null;
        snapshot.Choices[0].Choice.SelectedPrefabName = "output-mutated"; snapshot.Choices[0].ControllerLife = 999;
        snapshot.Choices[1] = null;
        MapOriginSourceSnapshot fresh = Capture(registry);
        Assert(fresh.OwnerLife == owner && fresh.RouteFingerprint == fingerprint && fresh.Route.Scenes[0].SceneName == "scene-1" &&
            fresh.Route.Scenes[1] != null && fresh.Choices[0].ControllerLife == first && fresh.Choices[0].Choice.SelectedPrefabName == "first-latest" &&
            fresh.Choices[1].ControllerLife == second && registry.ReadyChoiceCount == 3,
            "snapshot output arrays/elements alias internal registry state");
        Assert(registry.TryTakeBoundChoice(out MapOriginChoiceEvidence oldest) && oldest.CallbackSequence == 2 && registry.ReadyChoiceCount == 2,
            "snapshot reordered or drained the independent diagnostic FIFO");
    }

    internal static void SameFingerprintNewEntryDoesNotMixSourceLives()
    {
        var registry = New(); Bound(registry, out long firstOwner, out long firstScope, out long oldController, out _);
        Accepted(registry.ObserveChoice(oldController, 1, Choice()));
        MapOriginSourceSnapshot first = Capture(registry); Accepted(registry.ExitScope(firstScope));
        Accepted(registry.BeginOwner(1, out long secondOwner));
        MapOriginSourceSnapshot changing = Capture(registry);
        Assert(changing.OwnerLife == secondOwner && changing.Route == null && changing.Choices.Length == 0 && registry.ReadyChoiceCount == 0,
            "new entry inherited a previous pending/ready source");
        Accepted(registry.EnterOwner(secondOwner, out long secondScope)); Accepted(registry.BindRoute(secondScope, 501, Route()));
        Accepted(registry.RegisterOperation(secondScope, 201, 1, "scene-1", out long secondOperation));
        Accepted(registry.CompleteOperation(secondOperation, 201, 1, 11, out _));
        Accepted(registry.RegisterControllerBirth(301, 11, "scene-1", out long newController));
        Assert(registry.ObserveChoice(oldController, 2, Choice("old-late")) == MapOriginStatus.Retired,
            "old controller could publish into the new entry");
        Accepted(registry.ObserveChoice(newController, 3, Choice("new")));
        MapOriginSourceSnapshot current = Capture(registry);
        Assert(current.OwnerLife > firstOwner && current.OwnerLife == secondOwner && current.RouteFingerprint == first.RouteFingerprint &&
            current.Choices.Length == 1 && current.Choices[0].ControllerLife == newController && current.Choices[0].ContextPointer == 501 &&
            current.Choices[0].Choice.SelectedPrefabName == "new" && first.Choices[0].Choice.SelectedPrefabName == "selected",
            "identical route hash mixed entry/controller lives or changed an earlier owned audit snapshot");
    }

    internal static void PendingChoicesWaitForExactSceneAndKeepLatestAfterDrain()
    {
        var registry = New(); Accepted(registry.BeginOwner(1, out long owner));
        Accepted(registry.EnterOwner(owner, out long scope)); Accepted(registry.BindRoute(scope, 500, Route()));
        Accepted(registry.RegisterOperation(scope, 200, 1, "scene-1", out long operation));
        Accepted(registry.RegisterControllerBirth(300, 10, "scene-1", out long controller), MapOriginStatus.Pending);
        Accepted(registry.ObserveChoice(controller, 1, Choice("pending-first")), MapOriginStatus.Pending);
        Accepted(registry.ObserveChoice(controller, 2, Choice("pending-latest")), MapOriginStatus.Pending);
        MapOriginSourceSnapshot pending = Capture(registry);
        Assert(pending.OwnerLife == owner && pending.Route != null && pending.Choices.Length == 0 &&
            registry.PendingChoiceCount == 2 && registry.ReadyChoiceCount == 0,
            "snapshot guessed a controller origin or flushed pending choices");
        Accepted(registry.CompleteOperation(operation, 200, 1, 10, out long scene));
        MapOriginSourceSnapshot complete = Capture(registry);
        Assert(complete.Choices.Length == 1 && complete.Choices[0].SceneLife == scene && complete.Choices[0].OperationLife == operation &&
            complete.Choices[0].CallbackSequence == 2 && complete.Choices[0].Choice.SelectedPrefabName == "pending-latest" && registry.ReadyChoiceCount == 2,
            "exact late completion did not expose only the latest choice");
        Assert(registry.TryTakeBoundChoice(out MapOriginChoiceEvidence first) && first.Choice.SelectedPrefabName == "pending-first" &&
            registry.TryTakeBoundChoice(out _) && !registry.TryTakeBoundChoice(out _), "snapshot changed pending-to-ready event history");
        Assert(Capture(registry).Choices[0].Choice.SelectedPrefabName == "pending-latest", "draining diagnostic events deleted current source state");
        Accepted(registry.RegisterControllerBirth(301, 10, "outside-route", out long outside));
        Assert(registry.ObserveChoice(outside, 3, Choice("unbound")) == MapOriginStatus.Unbound && Capture(registry).Choices.Length == 1,
            "scene-name-only unbound choice entered the selected route source");
    }

    internal static void SourceSnapshotsRemoveRetiredControllerSceneAndRouteEvidence()
    {
        var registry = New(); Bound(registry, out long owner, out long scope, out long first, out long firstScene);
        Accepted(registry.RegisterControllerBirth(301, 10, "scene-1", out long second));
        Accepted(registry.ObserveChoice(first, 1, Choice("first", 0)));
        Accepted(registry.ObserveChoice(second, 2, Choice("second", 1)));
        Assert(Capture(registry).Choices.Length == 2, "two live controller choices were not visible");
        // Native finalizers/destroy callbacks retire the fixed controller token;
        // this CLR fixture does not execute an original method or native hook.
        Accepted(registry.RetireController(first));
        MapOriginSourceSnapshot retired = Capture(registry);
        Assert(retired.Route != null && retired.Choices.Length == 1 && retired.Choices[0].ControllerLife == second,
            "controller exception/destroy left its previous choice in the source");
        Accepted(registry.RegisterOperation(scope, 201, 1, "scene-2", out long otherOperation));
        Accepted(registry.CompleteOperation(otherOperation, 201, 1, 11, out _));
        Accepted(registry.RegisterControllerBirth(302, 11, "scene-2", out long other));
        Accepted(registry.ObserveChoice(other, 3, Choice("other-scene", 0)));
        Accepted(registry.RetireScene(10, firstScene));
        MapOriginSourceSnapshot unloaded = Capture(registry);
        Assert(unloaded.OwnerLife == owner && unloaded.Route != null && unloaded.Choices.Length == 1 && unloaded.Choices[0].ControllerLife == other,
            "unloaded scene retained its choice or erased a different live scene");
        Accepted(registry.RetireOwner(owner));
        MapOriginSourceSnapshot closed = Capture(registry);
        Assert(closed.OwnerLife == 0 && closed.Route == null && closed.Choices.Length == 0,
            "retired owner left an authoritative-looking route source");

        var repeatedCache = New(); Bound(repeatedCache, out _, out long repeatedScope, out long repeatedController, out _);
        Accepted(repeatedCache.ObserveChoice(repeatedController, 1, Choice()));
        Assert(repeatedCache.BindRoute(repeatedScope, 500, Route()) == MapOriginStatus.Retired && Capture(repeatedCache).OwnerLife == 0,
            "same-hash repeated cache preserved the old source snapshot");
    }

    internal static void FaultAndPendingConflictNeverReturnStaleSource()
    {
        var registry = New(); Bound(registry, out _, out _, out long controller, out _);
        Accepted(registry.ObserveChoice(controller, 1, Choice()));
        MapOriginSourceSnapshot previous = Capture(registry);
        registry.Invalidate("read failure or lost callback");
        Assert(!registry.TryCaptureSource(out previous) && previous == null && registry.ReadyChoiceCount == 0,
            "fault returned a prior successful source snapshot");

        var conflict = New(); Accepted(conflict.BeginOwner(1, out long owner));
        Accepted(conflict.EnterOwner(owner, out long scope)); Accepted(conflict.BindRoute(scope, 500, Route()));
        Accepted(conflict.RegisterOperation(scope, 200, 1, "scene-1", out long operation));
        Accepted(conflict.RegisterControllerBirth(300, 10, "scene-1", out long pending), MapOriginStatus.Pending);
        MapGroupSelection wrongScene = Choice(); wrongScene.SceneId = 2;
        Accepted(conflict.ObserveChoice(pending, 1, wrongScene), MapOriginStatus.Pending);
        Assert(Capture(conflict).Choices.Length == 0 && conflict.PendingChoiceCount == 1,
            "snapshot promoted conflicting pending scene data before exact completion");
        Assert(conflict.CompleteOperation(operation, 200, 1, 10, out _) == MapOriginStatus.Faulted &&
            !conflict.TryCaptureSource(out MapOriginSourceSnapshot poisoned) && poisoned == null &&
            conflict.PendingChoiceCount == 0 && conflict.ReadyChoiceCount == 0,
            "Resolve failure/collection retirement exposed a partial source or threw during pending traversal");
    }

    internal static void SourceSnapshotControllerBoundAndWrongThreadFailClosed()
    {
        var registry = New(); Accepted(registry.BeginOwner(1, out long owner));
        Accepted(registry.EnterOwner(owner, out long scope)); Accepted(registry.BindRoute(scope, 500, Route()));
        Accepted(registry.RegisterOperation(scope, 200, 1, "scene-1", out long operation));
        Accepted(registry.CompleteOperation(operation, 200, 1, 10, out _));
        for (int index = 0; index < MapOriginRegistry.MaxControllers; index++)
        {
            Accepted(registry.RegisterControllerBirth(index + 300, 10, "scene-1", out long controller));
            Accepted(registry.ObserveChoice(controller, index + 1, Choice("selected-" + index, index)));
            Assert(registry.TryTakeBoundChoice(out _), "diagnostic queue could not be drained independently");
        }
        MapOriginSourceSnapshot bounded = Capture(registry);
        Assert(bounded.Choices.Length == MapOriginRegistry.MaxControllers && registry.ReadyChoiceCount == 0,
            "source was accidentally capped by the diagnostic queue or exceeded its controller limit");
        for (int index = 1; index < bounded.Choices.Length; index++)
            Assert(bounded.Choices[index - 1].ControllerLife < bounded.Choices[index].ControllerLife, "source controller order was unstable");
        Assert(registry.RegisterControllerBirth(9999, 10, "scene-1", out _) == MapOriginStatus.LimitExceeded &&
            !registry.TryCaptureSource(out bounded) && bounded == null && registry.ControllerCount == MapOriginRegistry.MaxControllers,
            "controller overflow evicted a replay fence or returned truncated source evidence");

        var wrongThread = New(); Bound(wrongThread, out _, out _, out long live, out _);
        Accepted(wrongThread.ObserveChoice(live, 1, Choice()));
        // A dedicated worker cannot be inlined onto an async test runner's
        // current thread while it synchronously waits for this task.
        bool offThread = Task.Factory.StartNew(() => wrongThread.TryCaptureSource(out _),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).GetAwaiter().GetResult();
        Assert(!offThread && !wrongThread.Healthy && !wrongThread.TryCaptureSource(out MapOriginSourceSnapshot invalid) && invalid == null &&
            wrongThread.ReadyChoiceCount == 0, "non-main source query retained or later returned live evidence");
    }

    private static MapOriginRegistry New() => new MapOriginRegistry(Thread.CurrentThread.ManagedThreadId);
    private static MapOriginSourceSnapshot Capture(MapOriginRegistry registry)
    { Assert(registry.TryCaptureSource(out MapOriginSourceSnapshot source), "Source snapshot unavailable: " + registry.FaultReason); return source; }
    private static void Bound(MapOriginRegistry registry, out long owner, out long scope, out long controller, out long scene)
    {
        Accepted(registry.BeginOwner(1, out owner)); Accepted(registry.EnterOwner(owner, out scope));
        Accepted(registry.BindRoute(scope, 500, Route()));
        Accepted(registry.RegisterOperation(scope, 200, 1, "scene-1", out long operation));
        Accepted(registry.CompleteOperation(operation, 200, 1, 10, out scene));
        Accepted(registry.RegisterControllerBirth(300, 10, "scene-1", out controller));
    }
    private static MapGroupSelection Choice(string name = "selected", int address = 0) => new MapGroupSelection
    { SceneId = 0, ControllerAddress = "root/controller[" + address + "]", Addressable = true, SelectedPrefabName = name };
    private static MapRouteSelection Route()
    {
        var scenes = new MapRouteScene[3];
        for (int index = 0; index < scenes.Length; index++) scenes[index] = new MapRouteScene
        {
            SceneId = index + 1, SceneName = "scene-" + (index + 1), Layer = 'A', MapHeight = 10,
            PreviousSceneId = index == 0 ? 0 : index, NextSceneId = index == scenes.Length - 1 ? 0 : index + 2
        };
        return new MapRouteSelection { EntrySceneId = 1, Scenes = scenes };
    }
    private static void Accepted(MapOriginStatus actual, MapOriginStatus expected = MapOriginStatus.Accepted)
    { Assert(actual == expected, "Expected " + expected + " but received " + actual); }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
