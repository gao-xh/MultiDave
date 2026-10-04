using System;
using System.Threading;
using System.Threading.Tasks;
using DaveCoop.Core.World;

internal static class MapOriginFishBirthTests
{
    internal static void FishBirthBeforeReturnedHandleBindsOnlyItsExactScene()
    {
        var f = new Fixture();
        Check(f.Registry.BeginLoadCall(f.Scope, "bootstrap", out long call));
        Check(f.Registry.RegisterFishBirth(400, 90, "scene-1", out long fish), MapOriginStatus.Pending);
        Check(f.Registry.RegisterControllerBirth(500, 90, "scene-1", out long igp), MapOriginStatus.Pending);
        Check(f.Registry.RegisterFishBirth(400, 90, "scene-1", out long duplicate), MapOriginStatus.Pending);
        Assert(duplicate == fish && !f.Registry.TryGetControllerSource(fish, out _) &&
            f.Registry.FishBirthCount == 1 && f.Registry.ControllerCount == 1,
            "pending birth supplied ownership or a duplicate consumed another slot");
        Check(f.Registry.RegisterOperationFromLoadCall(call, 100, 7, "bootstrap", out long operation));
        Check(f.Registry.EndLoadCall(call, true));
        Assert(!f.Registry.TryGetControllerSource(fish, out _), "original typed return was mistaken for scene completion");
        Check(f.Registry.CompleteOperation(operation, 100, 7, 90, out long scene));
        Assert(f.Registry.TryGetControllerSource(fish, out MapOriginControllerSource first) && first.IsFishBirth &&
            first.ControllerLife == fish && first.ControllerPointer == 400 && first.OwnerLife == f.Owner &&
            first.OperationLife == operation && first.OperationPointer == 100 && first.OperationVersion == 7 &&
            first.SceneLife == scene && first.SceneHandle == 90 && first.SceneName == "scene-1" && first.LoadKey == "bootstrap",
            "completed fish source omitted or substituted an immutable exact chain field");
        Assert(f.Registry.TryGetControllerSource(igp, out MapOriginControllerSource controller) && !controller.IsFishBirth &&
            f.Registry.TryGetControllerSource(fish, out MapOriginControllerSource copy) && !ReferenceEquals(first, copy) &&
            first.ObservationOnly && !first.NativePermission && !first.NativeGenerationBound &&
            !first.HostSelectionApplied && !first.CrossMachineAddressVerified,
            "source kind changed or copied CLR evidence granted native permission");
        Check(f.Registry.RetireController(fish));
        Assert(!f.Registry.TryGetControllerSource(fish, out _) && first.SceneLife == scene && first.IsFishBirth &&
            f.Registry.TryGetControllerSource(igp, out _),
            "fish retirement mutated historical evidence or retired an independent IGP life");
    }

    internal static void PendingManagerWitnessBindsFishWithoutGivingItsScopeAnOwner()
    {
        for (int boundary = 0; boundary < 3; boundary++)
        {
            var f = new Fixture();
            Check(f.Registry.BeginLoadCall(f.Scope, "bootstrap", out long call));
            Check(f.Registry.RegisterManagerBirth(200, 90, out long manager), MapOriginStatus.Pending);
            Check(f.Registry.RegisterManagerIterator(manager, 200, 300, out long iterator), MapOriginStatus.Pending);
            Check(f.Registry.EnterMoveNext(300, iterator, out long managerScope), MapOriginStatus.Unbound);
            Assert(f.Registry.CurrentOwnerLife == 0, "pending manager gained a usable owner before completion");
            Check(f.Registry.BeginLoadCall(managerScope, "illegal-child", out long denied), MapOriginStatus.Unbound);
            Assert(denied == 0, "pending manager witness granted load dispatch");
            long unknown = 0;
            if (boundary == 1) Check(f.Registry.EnterMoveNext(999, 0, out unknown), MapOriginStatus.Unbound);
            Check(f.Registry.RegisterFishBirth(400, boundary == 2 ? 91 : 90, "scene-1", out long fish),
                boundary == 0 ? MapOriginStatus.Pending : MapOriginStatus.Unbound);
            if (unknown != 0) Check(f.Registry.ExitScope(unknown));
            Check(f.Registry.ExitScope(managerScope));
            Check(f.Registry.RegisterOperationFromLoadCall(call, 100, 7, "bootstrap", out long operation));
            Check(f.Registry.EndLoadCall(call, true));
            Check(f.Registry.CompleteOperation(operation, 100, 7, 90, out _));
            Assert(f.Registry.TryGetManagerOwner(manager, out long owner) && owner == f.Owner,
                "a rejected child changed the independently frozen manager witness");
            bool bound = f.Registry.TryGetControllerSource(fish, out MapOriginControllerSource source);
            Assert(bound == (boundary == 0) && (!bound || source.IsFishBirth && source.OperationLife == operation),
                "an unknown grandchild or foreign scene borrowed the manager's original load call");
            Assert(f.Registry.FishBirthCount == 1 && f.Registry.ControllerCount == 0 && f.Registry.IteratorCount == 2,
                "fish registration consumed an IGP slot or created a native iterator association");
        }
    }

    internal static void FrozenFishBirthCannotBorrowUnknownCallsOrLaterOperations()
    {
        for (int boundary = 0; boundary < 3; boundary++)
        {
            var f = new Fixture(); long fish = 0;
            if (boundary == 0)
                Check(f.Registry.RegisterFishBirth(400, 90, "scene-1", out fish), MapOriginStatus.Unbound);
            Check(f.Registry.BeginLoadCall(f.Scope, "same-key", out long call));
            if (boundary == 1)
            {
                Check(f.Registry.EnterMoveNext(999, 0, out long unknown), MapOriginStatus.Unbound);
                Check(f.Registry.RegisterFishBirth(400, 90, "scene-1", out fish), MapOriginStatus.Unbound);
                Check(f.Registry.ExitScope(unknown));
            }
            else if (boundary == 2)
                Check(f.Registry.RegisterFishBirth(400, 90, "scene-1", out fish), MapOriginStatus.Pending);
            Check(f.Registry.RegisterFishBirth(400, 90, "scene-1", out long same),
                boundary == 2 ? MapOriginStatus.Pending : MapOriginStatus.Unbound);
            Assert(same == fish, "repeated natural birth replaced its frozen call set");
            long operation;
            if (boundary == 2) Check(f.Registry.RegisterOperation(f.Scope, 101, 7, "same-key", out operation));
            else
            {
                Check(f.Registry.RegisterOperationFromLoadCall(call, 101, 7, "same-key", out operation));
                Check(f.Registry.EndLoadCall(call, true));
            }
            Check(f.Registry.CompleteOperation(operation, 101, 7, 90, out _));
            Assert(f.Registry.IsControllerRetired(fish) && !f.Registry.TryGetControllerSource(fish, out _) && f.Registry.Healthy,
                "a later same-key operation or unknown call upgraded an earlier fish birth");
            Check(f.Registry.RegisterFishBirth(401, 90, "scene-1", out long fresh));
            Assert(f.Registry.TryGetControllerSource(fresh, out MapOriginControllerSource source) && source.IsFishBirth &&
                source.OperationLife == operation, "a genuinely later birth lost its already completed exact operation");
            if (boundary == 2)
            {
                Check(f.Registry.RegisterOperationFromLoadCall(call, 100, 8, "same-key", out _));
                Check(f.Registry.EndLoadCall(call, true));
            }
        }

        // A real asynchronous birth can use operations already registered at
        // birth. Unknown scopes mask pending ancestors, not existing exact ops.
        var existing = new Fixture();
        Check(existing.Registry.RegisterOperation(existing.Scope, 100, 7, "registered", out long oldOperation));
        Check(existing.Registry.EnterMoveNext(999, 0, out long opaque), MapOriginStatus.Unbound);
        Check(existing.Registry.RegisterFishBirth(400, 90, "scene-1", out long known), MapOriginStatus.Pending);
        Check(existing.Registry.ExitScope(opaque));
        Check(existing.Registry.CompleteOperation(oldOperation, 100, 7, 90, out _));
        Assert(existing.Registry.TryGetControllerSource(known, out MapOriginControllerSource knownSource) && knownSource.IsFishBirth,
            "unknown scope discarded a preexisting operation rather than masking its pending parent");
    }

    internal static void FishBirthCannotAcquireIgpChoiceOrIteratorRights()
    {
        var f = new Fixture(); f.Complete();
        Check(f.Registry.BindRoute(f.Scope, 600, Route()));
        Check(f.Registry.RegisterFishBirth(400, 90, "scene-1", out long fish));
        Check(f.Registry.RegisterControllerBirth(500, 90, "scene-1", out long igp));
        Assert(!f.Registry.TryGetControllerLife(400, out _) && f.Registry.TryGetFishBirthLife(400, out long fishQuery) &&
            fishQuery == fish && !f.Registry.TryGetFishBirthLife(500, out _) &&
            f.Registry.TryGetControllerLife(500, out long igpQuery) && igpQuery == igp,
            "pointer lookup crossed birth kinds");
        int iteratorCount = f.Registry.IteratorCount;
        Check(f.Registry.RegisterControllerIterator(fish, 400, 700, out long rejected), MapOriginStatus.Unbound);
        Check(f.Registry.ObserveChoice(fish, 1, Choice()), MapOriginStatus.Unbound);
        Assert(rejected == 0 && f.Registry.IteratorCount == iteratorCount && f.Registry.ReadyChoiceCount == 0 &&
            f.Registry.PendingChoiceCount == 0 && f.Registry.Healthy,
            "fish life became an IGP iterator or queued a selection");
        Check(f.Registry.RegisterControllerIterator(igp, 500, 700, out _));
        Check(f.Registry.ObserveChoice(igp, 1, Choice()));
        Assert(f.Registry.TryCaptureSource(out MapOriginSourceSnapshot snapshot) && snapshot.Choices.Length == 1 &&
            snapshot.Choices[0].ControllerLife == igp && f.Registry.TryTakeBoundChoice(out MapOriginChoiceEvidence choice) &&
            choice.ControllerLife == igp, "fish births altered the complete IGP inventory or consumed its ready queue");

        for (int reversed = 0; reversed < 3; reversed++)
        {
            var conflict = new Fixture(); conflict.Complete();
            if (reversed == 0)
            {
                Check(conflict.Registry.RegisterFishBirth(400, 90, "scene-1", out _));
                Check(conflict.Registry.RegisterControllerBirth(400, 90, "scene-1", out _), MapOriginStatus.Conflict);
            }
            else if (reversed == 1)
            {
                Check(conflict.Registry.RegisterControllerBirth(400, 90, "scene-1", out _));
                Check(conflict.Registry.RegisterFishBirth(400, 90, "scene-1", out _), MapOriginStatus.Conflict);
            }
            else
            {
                Check(conflict.Registry.RegisterFishBirth(400, 90, "scene-1", out long retired));
                Check(conflict.Registry.RetireController(retired));
                Check(conflict.Registry.RegisterControllerBirth(400, 90, "scene-1", out _), MapOriginStatus.Conflict);
            }
            Assert(!conflict.Registry.Healthy && !conflict.Registry.TryCaptureSource(out _),
                "kind reuse, including a retired native pointer, retained current source evidence");
        }
    }

    internal static void FishBirthLifecycleLossCannotReviveThroughLateCompletion()
    {
        for (int loss = 0; loss < 6; loss++)
        {
            var f = new Fixture();
            Check(f.Registry.BeginLoadCall(f.Scope, "pending", out long call));
            Check(f.Registry.RegisterFishBirth(400, 90, "scene-1", out long fish), MapOriginStatus.Pending);
            Check(f.Registry.RegisterControllerBirth(500, 90, "scene-1", out long igp), MapOriginStatus.Pending);
            Check(f.Registry.RegisterOperationFromLoadCall(call, 100, 7, "pending", out long operation));
            if (loss != 4) Check(f.Registry.EndLoadCall(call, true));
            if (loss == 0) Check(f.Registry.RetireController(fish));
            else if (loss == 1) Check(f.Registry.RetireScene(90));
            else if (loss == 2) Check(f.Registry.RetireOperation(operation));
            else if (loss == 3) Check(f.Registry.BeginOwner(2, out _));
            else if (loss == 4) Check(f.Registry.EndLoadCall(call, false), MapOriginStatus.Retired);
            else Check(f.Registry.Invalidate("lost original callback"), MapOriginStatus.Faulted);
            Check(f.Registry.CompleteOperation(operation, 100, 7, 90, out _), loss == 0 ? MapOriginStatus.Accepted :
                loss == 5 ? MapOriginStatus.Faulted : MapOriginStatus.Retired);
            Assert(f.Registry.IsControllerRetired(fish) && !f.Registry.TryGetControllerSource(fish, out _) &&
                (loss == 0 ? f.Registry.TryGetControllerSource(igp, out _) : f.Registry.IsControllerRetired(igp)),
                "late completion revived a retired birth or changed the wrong kind (case " + loss + ")");
            Check(f.Registry.RegisterFishBirth(400, 90, "scene-1", out _),
                loss == 5 ? MapOriginStatus.Faulted : MapOriginStatus.Retired);
            Assert(f.Registry.FishBirthCount == 1 && f.Registry.ControllerCount == 1,
                "lifecycle retirement evicted a process birth tombstone");
        }
    }

    internal static void FishBirthQuotaIsIndependentAndWrongThreadWithdrawsBothKinds()
    {
        var f = new Fixture(); f.Complete();
        Check(f.Registry.BindRoute(f.Scope, 600, Route()));
        for (int index = 0; index < MapOriginRegistry.MaxFishBirths; index++)
        {
            Check(f.Registry.RegisterFishBirth(10000 + index, 90, "scene-1", out long fish));
            Check(f.Registry.RetireController(fish));
        }
        long firstIgp = 0;
        for (int index = 0; index < MapOriginRegistry.MaxControllers; index++)
        {
            Check(f.Registry.RegisterControllerBirth(20000 + index, 90, "scene-1", out long igp));
            if (index == 0) firstIgp = igp;
        }
        Check(f.Registry.ObserveChoice(firstIgp, 1, Choice()));
        Assert(f.Registry.FishBirthCount == MapOriginRegistry.MaxFishBirths &&
            f.Registry.ControllerCount == MapOriginRegistry.MaxControllers && f.Registry.Healthy &&
            f.Registry.TryCaptureSource(out MapOriginSourceSnapshot snapshot) && snapshot.Choices.Length == 1 &&
            snapshot.Choices[0].ControllerLife == firstIgp,
            "fish tombstones exhausted IGP capacity or made its snapshot exceed the 256-controller bound");
        Check(f.Registry.RegisterFishBirth(10000, 90, "scene-1", out _), MapOriginStatus.Retired);
        Check(f.Registry.RegisterFishBirth(30000, 90, "scene-1", out _), MapOriginStatus.LimitExceeded);
        Assert(!f.Registry.Healthy && f.Registry.FishBirthCount == MapOriginRegistry.MaxFishBirths &&
            f.Registry.ControllerCount == MapOriginRegistry.MaxControllers && !f.Registry.TryCaptureSource(out _),
            "fish quota evicted a fence, added an over-limit record or preserved current IGP evidence");

        var igpFull = new Fixture(); igpFull.Complete();
        for (int index = 0; index < MapOriginRegistry.MaxControllers; index++)
            Check(igpFull.Registry.RegisterControllerBirth(20000 + index, 90, "scene-1", out _));
        Check(igpFull.Registry.RegisterFishBirth(400, 90, "scene-1", out long independent));
        Assert(igpFull.Registry.TryGetControllerSource(independent, out MapOriginControllerSource fishSource) && fishSource.IsFishBirth,
            "full IGP quota prevented an independent fish birth");
        Check(igpFull.Registry.RegisterControllerBirth(30000, 90, "scene-1", out _), MapOriginStatus.LimitExceeded);
        Assert(igpFull.Registry.FishBirthCount == 1 && igpFull.Registry.ControllerCount == MapOriginRegistry.MaxControllers,
            "IGP quota replaced a fish or controller tombstone");

        var thread = new Fixture(); thread.Complete();
        Check(thread.Registry.RegisterFishBirth(400, 90, "scene-1", out long existing));
        Check(thread.Registry.RegisterControllerBirth(500, 90, "scene-1", out long controller));
        Check(Task.Run(() => thread.Registry.RegisterFishBirth(401, 90, "scene-1", out _)).GetAwaiter().GetResult(),
            MapOriginStatus.WrongThread);
        Assert(!thread.Registry.Healthy && thread.Registry.IsControllerRetired(existing) &&
            thread.Registry.IsControllerRetired(controller) && !thread.Registry.TryGetControllerSource(existing, out _) &&
            thread.Registry.FishBirthCount == 1 && thread.Registry.ControllerCount == 1,
            "foreign thread created a fish source or left either kind usable");
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
        internal void Complete()
        {
            Check(Registry.RegisterOperation(Scope, 100, 7, "bootstrap", out long operation));
            Check(Registry.CompleteOperation(operation, 100, 7, 90, out _));
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
