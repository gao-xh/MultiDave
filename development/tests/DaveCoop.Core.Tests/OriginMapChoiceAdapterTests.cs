using System;
using System.Linq;
using System.Threading.Tasks;
using DaveCoop.Core.Session;
using DaveCoop.Core.World;
using DaveCoop.Networking;
using static MapChoiceControllerTests;

internal static class OriginMapChoiceAdapterTests
{
    public static async Task TcpInventoryRemovalAndRetiredControllerReplay()
    {
        using var pair = await TcpPair.Open();
        var source = new MapChoiceController(); var received = new MapChoiceController(); Guid run = Guid.NewGuid();
        source.BindRoom(pair.Host, 0); received.BindRoom(pair.Guest, 0);
        source.ObserveOrigin(Frame(run, 1, 3, 2), pair.Host);
        await Remote(received, pair, () => received.RemoteGeneration == 1 && received.RemoteChoiceCount == 2);
        source.ObserveOrigin(Frame(run, 1), pair.Host);
        await Remote(received, pair, () => received.RemoteGeneration == 2 && received.RemoteChoiceCount == 1);
        Assert(pair.Guest.Snapshot.MapChoiceRevision == 1, "inventory deletion was treated as an append and retained the missing group");

        var replacement = Frame(run, 1);
        replacement.Source.Choices[0].ControllerLife = 1100;
        replacement.Source.Choices[0].Choice.SelectedPrefabName = "replacement";
        source.ObserveOrigin(replacement, pair.Host);
        await Remote(received, pair, () => received.RemoteGeneration == 3 && received.RemoteChoiceCount == 1);
        long choices = source.PublishedChoices;
        source.ObserveOrigin(Frame(run, 1), pair.Host);
        Assert(source.SourceGeneration == 0 && source.PublishedChoices == choices, "old controller life revived through its reused address");
        await Remote(received, pair, () => received.RemoteGeneration == 3 && received.RemoteChoiceCount == 0);
        source.ObserveOrigin(replacement, pair.Host);
        Assert(pair.Host.Snapshot.MapChoiceFingerprint == null, "same native owner silently recovered after a conflicting source replay");
        source.ObserveOrigin(Frame(run, 2), pair.Host);
        await Remote(received, pair, () => received.RemoteGeneration == 4 && received.RemoteChoiceCount == 1);
        Assert(source.SourceOriginOwnerLife == 2 && source.SourceOriginRunId == run && source.PublicationErrors == 0,
            "new natural entry could not recover after inventory fencing");
    }

    public static async Task TcpRunSwitchFailureAndOldRunTombstones()
    {
        using var pair = await TcpPair.Open();
        var source = new MapChoiceController(); var received = new MapChoiceController();
        Guid first = Guid.NewGuid(), second = Guid.NewGuid(), third = Guid.NewGuid();
        source.BindRoom(pair.Host, 0); received.BindRoom(pair.Guest, 0);
        for (int i = 0; i < 3; i++) source.ObserveOrigin(new MapOriginSourceFrame { Healthy = false }, pair.Host);
        Assert(source.InvalidObservations == 0 && source.PublishedRoutes == 0, "normal origin-off frames accumulated invalid samples");
        source.ObserveOrigin(new MapOriginSourceFrame { Healthy = true }, pair.Host);
        Assert(source.InvalidObservations == 1 && source.PublishedRoutes == 0, "a healthy source with an empty RunId was admitted");
        source.ObserveOrigin(Frame(first, 1), pair.Host);
        await Remote(received, pair, () => received.RemoteGeneration == 1 && received.RemoteChoiceCount == 1);
        source.ObserveOrigin(Frame(second, 1), pair.Host);
        await Remote(received, pair, () => received.RemoteGeneration == 2 && received.RemoteChoiceCount == 1);
        source.ObserveOrigin(Frame(first, 99), pair.Host);
        source.ObserveOrigin(new MapOriginSourceFrame { RunId = first, Healthy = false }, pair.Host);
        Assert(source.SourceOriginRunId == second && source.SourceGeneration == 2, "a tombstoned run replaced or retired the newer run");

        source.ObserveOrigin(new MapOriginSourceFrame { RunId = second, Healthy = false }, pair.Host);
        await Remote(received, pair, () => received.RemoteGeneration == 2 && received.RemoteRouteSceneCount == 0);
        source.ObserveOrigin(Frame(second, 2), pair.Host);
        Assert(source.SourceGeneration == 0 && pair.Host.Snapshot.MapChoiceFingerprint == null, "a failed observer run was reused with a larger owner ID");
        source.ObserveOrigin(new MapOriginSourceFrame { RunId = third, Healthy = true, Source = new MapOriginSourceSnapshot { OwnerLife = 0, Choices = Array.Empty<MapOriginChoiceEvidence>() } }, pair.Host);
        Assert(source.PublishedRoutes == 2, "a fresh observer run without an entry supplied a route");
        source.ObserveOrigin(Frame(third, 1), pair.Host);
        await Remote(received, pair, () => received.RemoteGeneration == 3 && received.RemoteChoiceCount == 1);
        source.ObserveOrigin(new MapOriginSourceFrame { RunId = third, Healthy = true, Source = null }, pair.Host);
        await Remote(received, pair, () => received.RemoteGeneration == 3 && received.RemoteChoiceCount == 0);
        source.ObserveOrigin(Frame(third, 2), pair.Host);
        Assert(pair.Host.Snapshot.MapChoiceFingerprint == null, "a source-copy failure did not latch the run retirement");
        source.ObserveOrigin(Frame(Guid.NewGuid(), 1), pair.Host);
        await Remote(received, pair, () => received.RemoteGeneration == 4 && received.RemoteChoiceCount == 1);
        Assert(pair.Host.Snapshot.Phase != SessionPhase.Closed, "candidate failures disconnected the TCP room");
    }

    public static async Task TcpScalarForgeryRejectedAtomically()
    {
        using var pair = await TcpPair.Open();
        var source = new MapChoiceController(); var received = new MapChoiceController(); Guid run = Guid.NewGuid();
        source.BindRoom(pair.Host, 0); received.BindRoom(pair.Guest, 0);
        for (int fault = 0; fault < 12; fault++)
        {
            long owner = fault + 1;
            source.ObserveOrigin(Frame(run, owner), pair.Host);
            await Remote(received, pair, () => received.RemoteGeneration == owner && received.RemoteChoiceCount == 1);
            var bad = Frame(run, owner, 3, fault >= 9 ? 2 : 1);
            var item = bad.Source.Choices[0];
            switch (fault)
            {
                case 0: item.OwnerLife++; break;
                case 1: item.RouteFingerprint = "map-route-v1/" + new string('0', 64); break;
                case 2: item.ContextPointer = 0; break;
                case 3: item.OperationLife = 0; break;
                case 4: item.SceneLife = 0; break;
                case 5: item.SceneHandle = 0; break;
                case 6: item.ControllerLife = 0; break;
                case 7: item.ControllerSceneName = "dive-1"; break;
                case 8: item.CallbackSequence = 0; break;
                case 9: bad.Source.Choices[1].Choice.ControllerAddress = item.Choice.ControllerAddress; break;
                case 10: bad.Source.Choices[1].ContextPointer++; break;
                case 11: bad.Source.Choices[1].OperationLife++; break;
            }
            long published = source.PublishedChoices;
            source.ObserveOrigin(bad, pair.Host);
            Assert(source.PublishedChoices == published && pair.Host.Snapshot.MapChoiceFingerprint == null,
                "forged/inconsistent chain partially published before atomic rejection (fault " + fault + ")");
            await Remote(received, pair, () => received.RemoteGeneration == owner && received.RemoteChoiceCount == 0);
            source.ObserveOrigin(Frame(run, owner), pair.Host);
            Assert(source.SourceGeneration == 0 && pair.Host.Snapshot.Phase != SessionPhase.Closed, "rejected owner was replayed or closed the room");
        }
        source.ObserveOrigin(Frame(run, 13), pair.Host);
        await Remote(received, pair, () => received.RemoteGeneration == 13 && received.RemoteChoiceCount == 1);
        var changed = Frame(run, 13); changed.Source.Choices[0].Choice.SelectedPrefabName = "changed-with-same-callback";
        source.ObserveOrigin(changed, pair.Host);
        Assert(source.SourceGeneration == 0, "same callback identity accepted a changed result");
    }

    public static async Task TcpEightChoiceBudgetAndCopiedInventory()
    {
        using var pair = await TcpPair.Open();
        var source = new MapChoiceController(); var received = new MapChoiceController(); Guid run = Guid.NewGuid();
        source.BindRoom(pair.Host, 0); received.BindRoom(pair.Guest, 0);
        var caller = Frame(run, 1, 17, 17);
        source.ObserveOrigin(caller, pair.Host);
        Assert(source.PublishedChoices == 8 && source.PendingOriginChoices == 9, "one frame exceeded the eight-choice publication budget");
        caller.Source.Route.Scenes[0].SceneName = "caller-mutated";
        caller.Source.Choices[0].Choice.SelectedPrefabName = "caller-mutated";
        caller.Source.Choices[8].ControllerLife = 0;
        await Remote(received, pair, () => received.RemoteRouteSceneCount == 17 && received.RemoteChoiceCount == 8);
        source.ObserveOrigin(Frame(run, 1, 17, 17), pair.Host);
        Assert(source.PublishedChoices == 16 && source.PendingOriginChoices == 1 && source.InvalidObservations == 0,
            "caller mutation contaminated the owned route, inventory or controller history");
        await Remote(received, pair, () => received.RemoteChoiceCount == 16);
        source.ObserveOrigin(Frame(run, 1, 17, 17), pair.Host);
        Assert(source.PublishedChoices == 17 && source.PendingOriginChoices == 0, "remaining choices were dropped or republished");
        await Remote(received, pair, () => received.RemoteChoiceCount == 17 && pair.Guest.Snapshot.MapChoiceRevision == 17);
        source.ObserveOrigin(Frame(run, 1, 17, 17), pair.Host);
        Assert(source.PublishedRoutes == 1 && source.PublishedChoices == 17, "identical complete inventory consumed new wire revisions");

        var update = Frame(run, 1, 17, 17);
        update.Source.Choices[16].CallbackSequence = 2; update.Source.Choices[16].Choice.SelectedPrefabName = "updated-one";
        source.ObserveOrigin(update, pair.Host);
        await Remote(received, pair, () => pair.Guest.Snapshot.MapChoiceRevision == 18);
        Assert(source.PublishedChoices == 18 && source.SourceGeneration == 1, "one live-controller update rebuilt or republished the whole inventory");
        var excessive = Frame(run, 2, 3, MapChoiceFrames.MaxChoices + 1);
        source.ObserveOrigin(excessive, pair.Host);
        Assert(source.SourceGeneration == 0 && source.InvalidObservations == 1 && pair.Host.Snapshot.MapChoiceFingerprint == null,
            "oversized eligible inventory bypassed the existing schema limit");
    }

    public static async Task TcpPendingRouteFloorAndNewRoomEntry()
    {
        using var first = await TcpPair.Open();
        var source = new MapChoiceController(); var received = new MapChoiceController(); Guid run = Guid.NewGuid();
        source.BindRoom(first.Host, 100, run, 7); received.BindRoom(first.Guest, 0);
        var pending = new MapOriginSourceFrame { RunId = run, Healthy = true, Source = new MapOriginSourceSnapshot { OwnerLife = 7, Choices = Array.Empty<MapOriginChoiceEvidence>() } };
        source.ObserveOrigin(pending, first.Host); source.ObserveOrigin(Frame(run, 7), first.Host);
        Assert(source.PublishedRoutes == 0, "pre-room owner became eligible after its route completed");
        pending.Source.OwnerLife = 8; source.ObserveOrigin(pending, first.Host);
        Assert(source.PublishedRoutes == 0, "a pending route was inferred from the old source");
        source.ObserveOrigin(Frame(run, 8), first.Host);
        await Remote(received, first, () => received.RemoteGeneration == 1 && received.RemoteChoiceCount == 1);
        pending.Source.OwnerLife = 9; source.ObserveOrigin(pending, first.Host);
        await Remote(received, first, () => received.RemoteRouteSceneCount == 0);
        source.ObserveOrigin(Frame(run, 9), first.Host);
        await Remote(received, first, () => received.RemoteGeneration == 2 && received.RemoteChoiceCount == 1);
        source.ObserveOrigin(Frame(run, 8), first.Host);
        Assert(source.SourceOriginOwnerLife == 9 && source.SourceGeneration == 2, "owner downgrade rolled back the current candidate");

        source.Clear(200); received.Clear(200);
        using var second = await TcpPair.Open();
        source.BindRoom(second.Host, 200, run, 9); received.BindRoom(second.Guest, 200);
        source.ObserveOrigin(Frame(run, 9), second.Host);
        source.ObserveOrigin(Frame(run, 10), first.Host);
        Assert(second.Host.Snapshot.MapChoiceGeneration == 0, "new room adopted its prior owner or the wrong peer");
        Guid nextRun = Guid.NewGuid();
        var noEntry = new MapOriginSourceFrame { RunId = nextRun, Healthy = true, Source = new MapOriginSourceSnapshot { OwnerLife = 0, Choices = Array.Empty<MapOriginChoiceEvidence>() } };
        source.ObserveOrigin(noEntry, second.Host);
        Assert(second.Host.Snapshot.MapChoiceGeneration == 0, "new RunId alone supplied a natural entry");
        source.ObserveOrigin(Frame(nextRun, 1), second.Host);
        await Remote(received, second, () => received.RemoteGeneration == 1 && received.RemoteChoiceCount == 1);
        source.ObserveOrigin(Frame(run, 11), second.Host);
        Assert(source.SourceOriginRunId == nextRun && source.SourceGeneration == 1 && source.CallbackFloor == 200,
            "old run or independent legacy floor displaced the new room source");
    }

    public static async Task TcpRunQuotaKeepsTombstones()
    {
        using var pair = await TcpPair.Open();
        var source = new MapChoiceController(); var received = new MapChoiceController(); Guid original = Guid.NewGuid();
        source.BindRoom(pair.Host, 0); received.BindRoom(pair.Guest, 0);
        source.ObserveOrigin(Frame(original, 1), pair.Host);
        await Remote(received, pair, () => received.RemoteChoiceCount == 1);
        for (int i = 1; i <= MapChoiceController.MaxOriginRuns; i++)
            source.ObserveOrigin(new MapOriginSourceFrame { RunId = Guid.NewGuid(), Healthy = true, Source = new MapOriginSourceSnapshot { OwnerLife = 0, Choices = Array.Empty<MapOriginChoiceEvidence>() } }, pair.Host);
        source.ObserveOrigin(Frame(original, 99), pair.Host);
        source.ObserveOrigin(Frame(Guid.NewGuid(), 1), pair.Host);
        await Remote(received, pair, () => received.RemoteRouteSceneCount == 0);
        Assert(source.PublishedRoutes == 1 && source.InvalidObservations >= 1 && source.SourceGeneration == 0 &&
            pair.Host.Snapshot.MapChoiceFingerprint == null && pair.Host.Snapshot.Phase != SessionPhase.Closed,
            "run capacity silently evicted a replay fence or retained the old candidate");
    }
}
