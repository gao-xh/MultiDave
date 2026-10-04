using System;
using System.Threading;
using System.Threading.Tasks;
using DaveCoop.Core.World;

internal static class MapOriginLoadCallTests
{
    internal static void PreReturnBirthsBindOnlyAfterTheirOriginalHandleCompletes()
    {
        var f = new Fixture();
        Check(f.Registry.BeginLoadCall(f.Scope, "bootstrap", out long call));
        Check(f.Registry.RegisterManagerBirth(200, 90, out long manager), MapOriginStatus.Pending);
        Check(f.Registry.RegisterManagerIterator(manager, 200, 300, out long managerIterator), MapOriginStatus.Pending);
        Check(f.Registry.EnterMoveNext(300, managerIterator, out long managerScope), MapOriginStatus.Unbound);
        MapRouteSelection route = Route(); string fingerprint = MapSelections.FingerprintRoute(route);
        Check(f.Registry.BindRoute(managerScope, 600, route), MapOriginStatus.Pending);
        route.Scenes[0].SceneName = "caller-mutated";
        Check(f.Registry.ExitScope(managerScope));
        Check(f.Registry.RegisterControllerBirth(400, 90, "scene-1", out long controller), MapOriginStatus.Pending);
        Check(f.Registry.RegisterControllerIterator(controller, 400, 500, out long controllerIterator), MapOriginStatus.Pending);
        Check(f.Registry.ObserveChoice(controller, 1, Choice()), MapOriginStatus.Pending);
        Assert(f.Registry.OperationCount == 0 && f.Registry.PendingLoadCallCount == 1 &&
            !f.Registry.TryGetControllerSource(controller, out _) && !f.Registry.TryGetManagerOwner(manager, out _) &&
            !f.Registry.TryTakeBoundChoice(out _), "prefix evidence was mistaken for a completed original operation");
        Check(f.Registry.RegisterOperationFromLoadCall(call, 100, 7, "bootstrap", out long operation));
        Check(f.Registry.EndLoadCall(call, true));
        Check(f.Registry.EnterMoveNext(500, controllerIterator, out long pendingScope), MapOriginStatus.Unbound);
        Check(f.Registry.CompleteOperation(operation, 100, 7, 90, out long scene));
        Assert(f.Registry.CurrentOwnerLife == 0, "completion upgraded an already-entered unknown scope");
        Assert(f.Registry.TryGetControllerSource(controller, out MapOriginControllerSource source) &&
            source.OwnerLife == f.Owner && source.OperationLife == operation && source.SceneLife == scene &&
            source.OperationPointer == 100 && source.OperationVersion == 7 && source.LoadKey == "bootstrap" &&
            !source.NativeGenerationBound && !source.HostSelectionApplied && !source.NativePermission,
            "paired original result did not produce the exact observation-only controller chain");
        Assert(f.Registry.TryGetManagerOwner(manager, out long owner) && owner == f.Owner &&
            f.Registry.TryCaptureSource(out MapOriginSourceSnapshot snapshot) && snapshot.RouteFingerprint == fingerprint &&
            snapshot.Route.Scenes[0].SceneName == "scene-1" && snapshot.Choices.Length == 1 &&
            f.Registry.TryTakeBoundChoice(out MapOriginChoiceEvidence choice) && choice.SceneLife == scene,
            "the pending manager route or choice lost its owned pre-return binding");
        Check(f.Registry.ExitScope(pendingScope));
        Check(f.Registry.EnterMoveNext(500, controllerIterator, out long next));
        Assert(f.Registry.CurrentOwnerLife == f.Owner, "fixed controller iterator did not bind on its next resume");
        Check(f.Registry.ExitScope(next)); Check(f.Registry.ExitScope(f.Scope));
    }

    internal static void UnknownScopesAndBirthsOutsideTheCallCannotBorrowItsReturn()
    {
        for (int masked = 0; masked < 2; masked++)
        {
            var f = new Fixture();
            long controller, manager;
            if (masked == 0)
            {
                Check(f.Registry.RegisterControllerBirth(400, 90, "scene-1", out controller), MapOriginStatus.Unbound);
                Check(f.Registry.RegisterManagerBirth(200, 90, out manager), MapOriginStatus.Unbound);
            }
            Check(f.Registry.BeginLoadCall(f.Scope, "same-key", out long call));
            if (masked == 1)
            {
                Check(f.Registry.EnterMoveNext(999, 0, out long unknown), MapOriginStatus.Unbound);
                Check(f.Registry.RegisterControllerBirth(400, 90, "scene-1", out controller), MapOriginStatus.Unbound);
                Check(f.Registry.RegisterManagerBirth(200, 90, out manager), MapOriginStatus.Unbound);
                Check(f.Registry.ExitScope(unknown));
            }
            // A repeated actual birth must not expand its frozen call set.
            Check(f.Registry.RegisterControllerBirth(400, 90, "scene-1", out controller), MapOriginStatus.Unbound);
            Check(f.Registry.RegisterManagerBirth(200, 90, out manager), MapOriginStatus.Unbound);
            Check(f.Registry.RegisterOperationFromLoadCall(call, 100, 7, "same-key", out long operation));
            Check(f.Registry.EndLoadCall(call, true));
            Check(f.Registry.CompleteOperation(operation, 100, 7, 90, out _));
            Assert(f.Registry.IsControllerRetired(controller) && f.Registry.IsManagerRetired(manager) &&
                !f.Registry.TryGetControllerSource(controller, out _) && !f.Registry.TryGetManagerOwner(manager, out _),
                "an outside or masked birth borrowed a subsequently returned same-owner operation");
            Check(f.Registry.RegisterControllerBirth(401, 90, "scene-1", out _));
            Check(f.Registry.RegisterManagerBirth(201, 90, out _));
        }

        // Already registered operations remain valid for a genuinely later
        // asynchronous birth, even when it occurs outside a known Move scope.
        var registered = new Fixture();
        Check(registered.Registry.BeginLoadCall(registered.Scope, "preexisting", out long existingCall));
        Check(registered.Registry.RegisterOperationFromLoadCall(existingCall, 100, 7, "preexisting", out long existingOp));
        Check(registered.Registry.EndLoadCall(existingCall, true));
        Check(registered.Registry.EnterMoveNext(999, 0, out long opaque), MapOriginStatus.Unbound);
        Check(registered.Registry.RegisterControllerBirth(400, 90, "scene-1", out long known), MapOriginStatus.Pending);
        Check(registered.Registry.RegisterManagerBirth(200, 90, out long knownManager), MapOriginStatus.Pending);
        Check(registered.Registry.ExitScope(opaque));
        Check(registered.Registry.CompleteOperation(existingOp, 100, 7, 90, out _));
        Assert(registered.Registry.TryGetControllerSource(known, out _) && registered.Registry.TryGetManagerOwner(knownManager, out _),
            "unknown scope discarded already registered exact operation evidence");
    }

    internal static void NestedCallsKeepTheFrozenBirthBoundaryAndExactScene()
    {
        var f = new Fixture();
        Check(f.Registry.BeginLoadCall(f.Scope, "same-key", out long outer));
        Check(f.Registry.RegisterControllerBirth(400, 90, "scene-1", out long parent), MapOriginStatus.Pending);
        Check(f.Registry.RegisterManagerBirth(200, 90, out long manager), MapOriginStatus.Pending);
        Check(f.Registry.BeginLoadCall(f.Scope, "same-key", out long child));
        Check(f.Registry.RegisterControllerBirth(401, 91, "scene-2", out long nested), MapOriginStatus.Pending);
        Check(f.Registry.RegisterControllerBirth(400, 90, "scene-1", out long duplicate), MapOriginStatus.Pending);
        Check(f.Registry.RegisterManagerBirth(200, 90, out long repeated), MapOriginStatus.Pending);
        Assert(duplicate == parent && repeated == manager, "nested call changed an earlier birth identity");
        Check(f.Registry.RegisterOperationFromLoadCall(child, 101, 8, "same-key", out long childOp));
        Check(f.Registry.EndLoadCall(child, true));
        Check(f.Registry.RegisterOperationFromLoadCall(outer, 100, 7, "same-key", out long parentOp));
        Check(f.Registry.EndLoadCall(outer, true));
        Check(f.Registry.CompleteOperation(childOp, 101, 8, 91, out _));
        Assert(f.Registry.TryGetControllerSource(nested, out MapOriginControllerSource childSource) &&
            childSource.OperationLife == childOp && !f.Registry.TryGetControllerSource(parent, out _),
            "nested original return substituted the parent scene or became a name-based owner");
        Check(f.Registry.CompleteOperation(parentOp, 100, 7, 90, out _));
        Assert(f.Registry.TryGetControllerSource(parent, out MapOriginControllerSource parentSource) &&
            parentSource.OperationLife == parentOp && f.Registry.TryGetManagerOwner(manager, out _),
            "the outer call lost its still-eligible exact original handle");

        var mismatch = new Fixture();
        Check(mismatch.Registry.BeginLoadCall(mismatch.Scope, "same-key", out long first));
        Check(mismatch.Registry.RegisterControllerBirth(400, 90, "scene-1", out long firstBirth), MapOriginStatus.Pending);
        Check(mismatch.Registry.RegisterManagerBirth(200, 90, out long firstManager), MapOriginStatus.Pending);
        Check(mismatch.Registry.BeginLoadCall(mismatch.Scope, "same-key", out long later));
        Check(mismatch.Registry.RegisterControllerBirth(400, 90, "scene-1", out _), MapOriginStatus.Pending);
        Check(mismatch.Registry.RegisterManagerBirth(200, 90, out _), MapOriginStatus.Pending);
        Check(mismatch.Registry.RegisterOperationFromLoadCall(later, 102, 9, "same-key", out long laterOp));
        Check(mismatch.Registry.EndLoadCall(later, true));
        Check(mismatch.Registry.CompleteOperation(laterOp, 102, 9, 90, out _));
        Assert(mismatch.Registry.IsControllerRetired(firstBirth) && mismatch.Registry.IsManagerRetired(firstManager),
            "duplicate birth expanded its set to the later nested call with the same key and scene handle");
        Check(mismatch.Registry.RegisterOperationFromLoadCall(first, 100, 7, "same-key", out _));
        Check(mismatch.Registry.EndLoadCall(first, true));
    }

    internal static void OrdinaryLaterOperationsCannotStandInForAFrozenCall()
    {
        var f = new Fixture();
        Check(f.Registry.BeginLoadCall(f.Scope, "same-key", out long call));
        Check(f.Registry.RegisterControllerBirth(400, 90, "scene-1", out long controller), MapOriginStatus.Pending);
        Check(f.Registry.RegisterManagerBirth(200, 90, out long manager), MapOriginStatus.Pending);
        Check(f.Registry.RegisterOperation(f.Scope, 101, 7, "same-key", out long unrelated));
        Check(f.Registry.CompleteOperation(unrelated, 101, 7, 90, out _));
        Assert(f.Registry.IsControllerRetired(controller) && f.Registry.IsManagerRetired(manager) && f.Registry.Healthy,
            "an ordinary later registration acted as the pending call's actual return");
        Check(f.Registry.RegisterOperationFromLoadCall(call, 100, 7, "same-key", out _));
        Check(f.Registry.EndLoadCall(call, true));

        var generic = new Fixture();
        Check(generic.Registry.BeginLoadCall(generic.Scope, "bootstrap", out long pending));
        Check(generic.Registry.RegisterIterator(500, 0, out long opaque), MapOriginStatus.Unbound);
        Check(generic.Registry.RegisterControllerBirth(400, 90, "scene-1", out long actual), MapOriginStatus.Pending);
        Check(generic.Registry.RegisterOperationFromLoadCall(pending, 100, 7, "bootstrap", out long operation));
        Check(generic.Registry.EndLoadCall(pending, true));
        Check(generic.Registry.CompleteOperation(operation, 100, 7, 90, out _));
        Check(generic.Registry.EnterMoveNext(500, opaque, out long unknown), MapOriginStatus.Unbound);
        Assert(generic.Registry.CurrentOwnerLife == 0 && generic.Registry.TryGetControllerSource(actual, out _),
            "proving a controller upgraded an unrelated generic owner-zero iterator");
        Check(generic.Registry.ExitScope(unknown));
    }

    internal static void ReturnedHandlesAndFinalizersAreIdempotentButCannotChange()
    {
        var f = new Fixture();
        Check(f.Registry.RegisterOperation(f.Scope, 100, 7, "cached", out long cached));
        Check(f.Registry.BeginLoadCall(f.Scope, "cached", out long call));
        Check(f.Registry.RegisterControllerBirth(400, 90, "scene-1", out long controller), MapOriginStatus.Pending);
        Check(f.Registry.RegisterOperationFromLoadCall(call, 100, 7, "cached", out long returned), MapOriginStatus.Duplicate);
        Check(f.Registry.RegisterOperationFromLoadCall(call, 100, 7, "cached", out long same), MapOriginStatus.Duplicate);
        Assert(cached == returned && returned == same && f.Registry.OperationCount == 1, "cached exact typed handle minted a second operation");
        Check(f.Registry.EndLoadCall(call, true)); Check(f.Registry.EndLoadCall(call, true), MapOriginStatus.Duplicate);
        Check(f.Registry.CompleteOperation(cached, 100, 7, 90, out _));
        Assert(f.Registry.TryGetControllerSource(controller, out _), "normal duplicate finalizer erased valid evidence");
        Check(f.Registry.RegisterOperationFromLoadCall(call, 100, 7, "cached", out _), MapOriginStatus.Retired);
        Check(f.Registry.EndLoadCall(call, false), MapOriginStatus.Retired);
        Assert(f.Registry.CurrentOwnerLife == 0 && f.Registry.IsControllerRetired(controller),
            "a late original exception preserved previously completed source evidence");

        for (int changed = 0; changed < 3; changed++)
        {
            var bad = new Fixture();
            Check(bad.Registry.BeginLoadCall(bad.Scope, "fixed", out long original));
            Check(bad.Registry.RegisterOperationFromLoadCall(original, 100, 7, "fixed", out _));
            Check(bad.Registry.RegisterOperationFromLoadCall(original, changed == 0 ? 101 : 100,
                changed == 1 ? 8 : 7, changed == 2 ? "changed" : "fixed", out _), MapOriginStatus.Conflict);
            Assert(!bad.Registry.Healthy && bad.Registry.CurrentOwnerLife == 0, "a changed original handle/key retained ownership");
        }
    }

    internal static void FailedCallsAndLifecycleLossWithdrawPendingBirthsImmediately()
    {
        for (int loss = 0; loss < 5; loss++)
        {
            var f = new Fixture();
            Check(f.Registry.BeginLoadCall(f.Scope, "pending", out long call));
            Check(f.Registry.RegisterControllerBirth(400, 90, "scene-1", out long controller), MapOriginStatus.Pending);
            Check(f.Registry.RegisterControllerIterator(controller, 400, 500, out long iterator), MapOriginStatus.Pending);
            Check(f.Registry.RegisterManagerBirth(200, 90, out long manager), MapOriginStatus.Pending);
            if (loss == 0) Check(f.Registry.EndLoadCall(call, false), MapOriginStatus.Retired); // exception or skipped original
            else if (loss == 1) Check(f.Registry.RetireScene(90));
            else if (loss == 2) Check(f.Registry.BeginOwner(2, out _));
            else if (loss == 3) Check(f.Registry.Invalidate("missing original finalizer"), MapOriginStatus.Faulted);
            else Check(Task.Run(() => f.Registry.EndLoadCall(call, false)).GetAwaiter().GetResult(), MapOriginStatus.WrongThread);
            Assert(f.Registry.IsControllerRetired(controller) && f.Registry.IsManagerRetired(manager) &&
                !f.Registry.TryGetControllerSource(controller, out _) && !f.Registry.TryGetManagerOwner(manager, out _),
                "lifecycle loss left pre-return births claimable (case " + loss + ")");
            if (loss == 1)
            {
                Check(f.Registry.RegisterOperationFromLoadCall(call, 100, 7, "pending", out long operation));
                Check(f.Registry.EndLoadCall(call, true));
                Check(f.Registry.CompleteOperation(operation, 100, 7, 90, out _), MapOriginStatus.Retired);
                Check(f.Registry.EnterMoveNext(500, iterator, out long unknown), MapOriginStatus.Unbound);
                Check(f.Registry.ExitScope(unknown));
            }
            else Check(f.Registry.RegisterOperationFromLoadCall(call, 100, 7, "pending", out _),
                loss < 3 ? MapOriginStatus.Retired : MapOriginStatus.Faulted);
        }
    }

    internal static void FixedMoveAndStrictCallPairingAreRequired()
    {
        var ownerOnly = new MapOriginRegistry(Thread.CurrentThread.ManagedThreadId);
        Check(ownerOnly.BeginOwner(1, out long owner)); Check(ownerOnly.EnterOwner(owner, out long intent));
        Check(ownerOnly.BeginLoadCall(intent, "not-a-fixed-move", out long none), MapOriginStatus.Unbound);
        Assert(none == 0 && ownerOnly.LoadCallCount == 0, "owner intent impersonated a fixed original MoveNext scope");
        Check(ownerOnly.ExitScope(intent));
        for (int broken = 0; broken < 4; broken++)
        {
            var f = new Fixture(); Check(f.Registry.BeginLoadCall(f.Scope, "outer", out long call));
            MapOriginStatus status;
            if (broken == 0) status = f.Registry.EndLoadCall(call, true); // no original returned handle
            else if (broken == 1)
            { Check(f.Registry.BeginLoadCall(f.Scope, "inner", out _)); status = f.Registry.EndLoadCall(call, false); }
            else if (broken == 2) status = f.Registry.ExitScope(f.Scope); // missing finalizer
            else
            {
                Check(f.Registry.EnterMoveNext(999, 0, out long unknown), MapOriginStatus.Unbound);
                Check(f.Registry.BeginLoadCall(unknown, "unbound", out long masked), MapOriginStatus.Unbound);
                Assert(masked == 0, "unknown child minted a parent-owned load call");
                status = f.Registry.RegisterOperationFromLoadCall(call, 100, 7, "outer", out _);
            }
            Check(status, MapOriginStatus.Conflict);
            Assert(!f.Registry.Healthy && f.Registry.CurrentOwnerLife == 0 && f.Registry.PendingLoadCallCount == 0,
                "a lost or non-LIFO original callback left the synchronous call window open");
        }
    }

    internal static void LoadCallQuotaKeepsEndedTokensAndNeverEvictsReplayFences()
    {
        var f = new Fixture(); long first = 0;
        for (int index = 0; index < MapOriginRegistry.MaxLoadCalls; index++)
        {
            Check(f.Registry.BeginLoadCall(f.Scope, "fixed", out long call)); if (index == 0) first = call;
            Check(f.Registry.RegisterOperationFromLoadCall(call, 1000 + index, 1, "fixed", out _));
            Check(f.Registry.EndLoadCall(call, true));
        }
        Check(f.Registry.EndLoadCall(first, true), MapOriginStatus.Duplicate);
        Check(f.Registry.RegisterOperationFromLoadCall(first, 1000, 1, "fixed", out _), MapOriginStatus.Retired);
        Assert(f.Registry.LoadCallCount == MapOriginRegistry.MaxLoadCalls && f.Registry.PendingLoadCallCount == 0,
            "ended call tokens were dropped or remained open");
        Check(f.Registry.BeginLoadCall(f.Scope, "overflow", out long denied), MapOriginStatus.LimitExceeded);
        Assert(denied == 0 && !f.Registry.Healthy && f.Registry.LoadCallCount == MapOriginRegistry.MaxLoadCalls &&
            f.Registry.OperationCount == MapOriginRegistry.MaxLoadCalls && f.Registry.CurrentOwnerLife == 0,
            "quota evicted an old token, dispatched an extra operation, or retained current ownership");
    }

    internal static void PendingManagerScopeSuppliesOnlySameSceneBirthEvidence()
    {
        for (int boundary = 0; boundary < 4; boundary++)
        {
            var f = new Fixture();
            Check(f.Registry.BeginLoadCall(f.Scope, "bootstrap", out long call));
            Check(f.Registry.RegisterManagerBirth(200, 90, out long manager), MapOriginStatus.Pending);
            Check(f.Registry.RegisterManagerIterator(manager, 200, 300, out long iterator), MapOriginStatus.Pending);
            Check(f.Registry.EnterMoveNext(300, iterator, out long pendingScope), MapOriginStatus.Unbound);
            Assert(f.Registry.CurrentOwnerLife == 0, "pending manager gained an owner from its parent's synchronous call");
            Check(f.Registry.BeginLoadCall(pendingScope, "child-load", out long denied), MapOriginStatus.Unbound);
            Check(f.Registry.RegisterOperation(pendingScope, 199, 0, "child-load", out _), MapOriginStatus.Unbound);
            Assert(denied == 0 && f.Registry.OperationCount == 0, "birth-only ancestry enabled native load dispatch");
            long unknown = 0;
            if (boundary == 1) Check(f.Registry.EnterMoveNext(999, 0, out unknown), MapOriginStatus.Unbound);
            else if (boundary == 3) Check(f.Registry.RetireManager(manager));
            Check(f.Registry.RegisterControllerBirth(400, boundary == 2 ? 91 : 90, "scene-1", out long child),
                boundary == 0 ? MapOriginStatus.Pending : MapOriginStatus.Unbound);
            if (unknown != 0) Check(f.Registry.ExitScope(unknown));
            Check(f.Registry.ExitScope(pendingScope));
            if (boundary == 3)
            {
                Check(f.Registry.RegisterOperationFromLoadCall(call, 100, 7, "bootstrap", out _), MapOriginStatus.Retired);
                Check(f.Registry.EndLoadCall(call, false), MapOriginStatus.Retired);
                Assert(!f.Registry.TryGetControllerSource(child, out _) && f.Registry.CurrentOwnerLife == 0,
                    "retired pending manager supplied a child source or restored the parent");
                continue;
            }
            Check(f.Registry.RegisterOperationFromLoadCall(call, 100, 7, "bootstrap", out long operation));
            Check(f.Registry.EndLoadCall(call, true));
            Check(f.Registry.CompleteOperation(operation, 100, 7, 90, out _));
            Assert(f.Registry.TryGetManagerOwner(manager, out long owner) && owner == f.Owner,
                "child masking also removed the independently frozen manager birth");
            bool bound = f.Registry.TryGetControllerSource(child, out MapOriginControllerSource source);
            Assert(bound == (boundary == 0) && (!bound || source.OperationLife == operation),
                "unknown grandchild, foreign scene or live same-scene manager did not respect the birth-only boundary");
        }
    }

    private sealed class Fixture
    {
        internal readonly MapOriginRegistry Registry = new MapOriginRegistry(Thread.CurrentThread.ManagedThreadId);
        internal readonly long Owner, Scope;
        internal Fixture()
        {
            Check(Registry.BeginOwner(1, out long owner)); Owner = owner;
            Check(Registry.RegisterIterator(3000, owner, out long iterator));
            Check(Registry.EnterMoveNext(3000, iterator, out long scope)); Scope = scope;
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
