using System;
using System.Text.Json;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.World;

internal static class MapChoiceTests
{
    internal static void RouteAtomicOrderAndObservationOnly()
    {
        var assembler = new MapChoiceAssembler(); MapRouteSlice[] slices = MapChoiceFrames.SplitRoute(Route(9), 1);
        Assert(slices.Length == 2 && slices[0].Scenes.Length == 8 && slices[1].Scenes.Length == 1,
            "route split exceeded the eight-scene slice contract");
        Throws<ArgumentException>(() => assembler.AcceptRoute(slices[1]));
        Assert(assembler.Snapshot.Generation == 0 && assembler.Snapshot.Route == null,
            "out-of-order initial slice changed route ownership");
        Assert(!assembler.AcceptRoute(slices[0]), "partial route was published as complete");
        MapChoiceSnapshot partial = assembler.Snapshot;
        Assert(partial.Generation == 1 && partial.Route == null && partial.Choices.Length == 0 &&
            partial.ObservationOnly && !partial.HostSelectionApplied, "partial route granted complete/adopted-world state");
        Throws<ArgumentException>(() => assembler.AcceptChoice(Choice(slices[0], 1)));
        Assert(!assembler.AcceptRoute(slices[0]) && assembler.AcceptRoute(slices[1]), "identical partial replay advanced or broke assembly");
        MapChoiceSnapshot complete = assembler.Snapshot;
        Assert(complete.Route.Scenes.Length == 9 && complete.RouteFingerprint == MapSelections.FingerprintRoute(complete.Route) &&
            complete.Choices.Length == 0 && complete.ObservationOnly && !complete.HostSelectionApplied,
            "complete route pretended to be an adopted or complete IGP world");
        var notCompleteManifest = new MapSelectionManifest { EntrySceneId = complete.Route.EntrySceneId, Scenes = complete.Route.Scenes };
        Throws<ArgumentException>(() => MapSelections.Validate(notCompleteManifest));
    }

    internal static void InvalidAssemblyAndFingerprintNeverCommit()
    {
        MapRouteSlice[] slices = MapChoiceFrames.SplitRoute(Route(9), 1); var assembler = new MapChoiceAssembler();
        assembler.AcceptRoute(slices[0]); MapRouteSlice broken = MapChoiceFrames.Copy(slices[1]); broken.Scenes[0].PreviousSceneId = 7;
        Throws<ArgumentException>(() => assembler.AcceptRoute(broken));
        Assert(assembler.Snapshot.Route == null && assembler.Snapshot.LastChoiceRevision == 0,
            "disconnected route exposed a complete candidate");
        Assert(assembler.AcceptRoute(slices[1]), "failed final slice poisoned the earlier valid prefix");
        MapRouteSlice conflict = MapChoiceFrames.Copy(slices[0]); conflict.Scenes[0].Offset += 0.25f;
        Throws<ArgumentException>(() => assembler.AcceptRoute(conflict));
        Assert(assembler.AcceptRoute(slices[0]) && assembler.Snapshot.RouteFingerprint == slices[0].RouteFingerprint,
            "changed duplicate corrupted the already committed route");
        var badHashAssembler = new MapChoiceAssembler(); MapRouteSlice[] badHash = MapChoiceFrames.SplitRoute(Route(9), 2);
        foreach (MapRouteSlice slice in badHash) slice.RouteFingerprint = "map-route-v1/" + new string('f', 64);
        badHashAssembler.AcceptRoute(badHash[0]); Throws<ArgumentException>(() => badHashAssembler.AcceptRoute(badHash[1]));
        Assert(badHashAssembler.Snapshot.Route == null, "declared fingerprint was trusted without computing the complete route");
        var duplicateAssembler = new MapChoiceAssembler(); duplicateAssembler.AcceptRoute(slices[0]);
        MapRouteSlice duplicate = MapChoiceFrames.Copy(slices[1]); duplicate.Scenes[0].SceneName = slices[0].Scenes[0].SceneName;
        Throws<ArgumentException>(() => duplicateAssembler.AcceptRoute(duplicate));
        Assert(duplicateAssembler.Snapshot.Route == null, "cross-slice scene alias was not rejected");
        MapRouteSlice[] three = MapChoiceFrames.SplitRoute(Route(17), 3); var ordered = new MapChoiceAssembler(); ordered.AcceptRoute(three[0]);
        Throws<ArgumentException>(() => ordered.AcceptRoute(three[2]));
        Assert(!ordered.AcceptRoute(three[1]) && ordered.AcceptRoute(three[2]), "skipped slice advanced the assembly cursor");
    }

    internal static void SnapshotAndIngressCopiesAreOwned()
    {
        MapRouteSelection source = Route(9); MapRouteSlice[] slices = MapChoiceFrames.SplitRoute(source, 1);
        string fingerprint = slices[0].RouteFingerprint; source.Scenes[0].SceneName = "source-mutated"; source.Scenes[1] = null;
        var assembler = new MapChoiceAssembler(); assembler.AcceptRoute(slices[0]);
        slices[0].Scenes[0].SceneName = "slice-mutated"; slices[0].Scenes[1] = null;
        Assert(assembler.AcceptRoute(slices[1]), "caller mutation reached retained route prefix");
        MapChoiceSnapshot snapshot = assembler.Snapshot;
        Assert(snapshot.RouteFingerprint == fingerprint && snapshot.Route.Scenes[0].SceneName == "scene-1",
            "split or assembler retained source array/element references");
        MapIgpChoice incoming = Choice(slices[1], 1); assembler.AcceptChoice(incoming); incoming.SelectedPrefabName = "caller-mutated";
        snapshot = assembler.Snapshot; snapshot.Generation = 99; snapshot.Route.Scenes[0].SceneName = "snapshot-mutated";
        snapshot.Choices[0].SelectedPrefabName = "snapshot-mutated"; snapshot.Choices = null;
        MapChoiceSnapshot fresh = assembler.Snapshot;
        Assert(fresh.Generation == 1 && fresh.Route.Scenes[0].SceneName == "scene-1" && fresh.Choices[0].SelectedPrefabName == "set-1",
            "egress snapshots shared route or choice objects with assembler ownership");
        MapRouteSlice sliceCopy = MapChoiceFrames.Copy(MapChoiceFrames.SplitRoute(Route(3), 4)[0]);
        MapRouteSlice anotherCopy = MapChoiceFrames.Copy(sliceCopy); anotherCopy.Scenes[0].Offset = 100;
        Assert(sliceCopy.Scenes[0].Offset == 0, "frame Copy shared scene objects");
        MapChoiceRetire notice = new MapChoiceRetire { Generation = 4, Reason = "context changed" };
        MapChoiceRetire copy = MapChoiceFrames.Copy(notice); notice.Reason = "mutated";
        Assert(copy.Reason == "context changed", "retirement Copy lost its owned value");
    }

    internal static void ChoiceSceneRevisionAndDuplicateGuards()
    {
        var assembler = new MapChoiceAssembler(); MapRouteSlice route = MapChoiceFrames.SplitRoute(Route(3), 1)[0]; assembler.AcceptRoute(route);
        MapIgpChoice choice = Choice(route, 2); Throws<ArgumentException>(() => assembler.AcceptChoice(choice));
        choice = Choice(route, 1); choice.SceneId = 999; Throws<ArgumentException>(() => assembler.AcceptChoice(choice));
        choice = Choice(route, 1); choice.Generation = 2; Throws<ArgumentException>(() => assembler.AcceptChoice(choice));
        choice = Choice(route, 1); choice.RouteFingerprint = "map-route-v1/" + new string('0', 64);
        Throws<ArgumentException>(() => assembler.AcceptChoice(choice));
        Assert(assembler.Snapshot.Choices.Length == 0 && assembler.Snapshot.LastChoiceRevision == 0,
            "invalid choice partially consumed revision or group capacity");
        choice = Choice(route, 1); Assert(assembler.AcceptChoice(choice), "first continuous choice was rejected");
        MapIgpChoice equivalent = MapChoiceFrames.Copy(choice); equivalent.PrefabObjectName = null;
        Assert(assembler.AcceptChoice(equivalent) && assembler.Snapshot.LastChoiceRevision == 1, "identical normalized replay advanced revision");
        MapIgpChoice conflict = MapChoiceFrames.Copy(choice); conflict.SelectedPrefabName = "changed";
        Throws<ArgumentException>(() => assembler.AcceptChoice(conflict));
        MapIgpChoice update = Choice(route, 2); update.SelectedPrefabName = "set-2"; assembler.AcceptChoice(update);
        Assert(assembler.AcceptChoice(choice) && assembler.Snapshot.Choices.Length == 1 &&
            assembler.Snapshot.Choices[0].SelectedPrefabName == "set-2" && assembler.Snapshot.LastChoiceRevision == 2,
            "old identical observation rolled a controller back to its earlier choice");
    }

    internal static void GenerationReplacementAndRetirementFence()
    {
        var assembler = new MapChoiceAssembler(); MapRouteSlice old = MapChoiceFrames.SplitRoute(Route(3), 1)[0];
        assembler.AcceptRoute(old); assembler.AcceptChoice(Choice(old, 1));
        MapRouteSlice[] next = MapChoiceFrames.SplitRoute(Route(9), 2); assembler.AcceptRoute(next[0]);
        MapChoiceSnapshot snapshot = assembler.Snapshot;
        Assert(snapshot.Generation == 2 && snapshot.Route == null && snapshot.Choices.Length == 0 && snapshot.LastChoiceRevision == 0,
            "new generation retained prior route or choices before its own complete commit");
        Assert(!assembler.AcceptRoute(old) && !assembler.AcceptChoice(Choice(old, 1)), "old generation crossed the replacement fence");
        assembler.AcceptRoute(next[1]);
        var retire = new MapChoiceRetire { Generation = 2, Reason = "scene origin unavailable" }; assembler.Retire(retire); assembler.Retire(retire);
        Assert(assembler.Snapshot.Retired && assembler.Snapshot.Route == null && assembler.Snapshot.Choices.Length == 0 &&
            !assembler.AcceptRoute(next[0]) && !assembler.AcceptChoice(Choice(next[0], 1)), "retired selection was resurrected");
        assembler.Retire(new MapChoiceRetire { Generation = 1, Reason = "old notice" });
        Assert(assembler.Snapshot.Generation == 2 && assembler.Snapshot.Retired, "old retirement reset generation high water");
        Throws<ArgumentException>(() => assembler.Retire(new MapChoiceRetire { Generation = 2, Reason = "changed notice" }));
        MapRouteSlice newer = MapChoiceFrames.SplitRoute(Route(3), 3)[0]; Assert(assembler.AcceptRoute(newer), "new generation could not follow retirement");
        assembler.Retire(new MapChoiceRetire { Generation = 5, Reason = "future revocation" });
        Assert(!assembler.AcceptRoute(MapChoiceFrames.SplitRoute(Route(3), 4)[0]) && !assembler.AcceptRoute(MapChoiceFrames.SplitRoute(Route(3), 5)[0]),
            "retirement before route arrival did not fence delayed selection evidence");
        Assert(assembler.AcceptRoute(MapChoiceFrames.SplitRoute(Route(3), 6)[0]), "retirement blocked every later generation");
        var freshRoom = new MapChoiceAssembler(); Assert(freshRoom.AcceptRoute(old), "a genuinely new room could not begin its own generations");
    }

    internal static void SameFingerprintNewIdentityAndBoundedChoiceHistory()
    {
        var assembler = new MapChoiceAssembler(); MapRouteSlice old = MapChoiceFrames.SplitRoute(Route(3), 1)[0];
        assembler.AcceptRoute(old); assembler.AcceptChoice(Choice(old, 1));
        MapRouteSlice next = MapChoiceFrames.SplitRoute(Route(3), 2)[0];
        Assert(old.RouteFingerprint == next.RouteFingerprint && assembler.AcceptRoute(next) &&
            assembler.Snapshot.Generation == 2 && assembler.Snapshot.LastChoiceRevision == 0 && assembler.Snapshot.Choices.Length == 0,
            "same route fingerprint reused the retired generation's choice identity");
        MapIgpChoice first = Choice(next, 1);
        for (int i = 1; i <= MapChoiceFrames.MaxChoiceHistory + 2; i++)
        {
            MapIgpChoice update = Choice(next, i); update.SelectedPrefabName = "set-" + i;
            Assert(assembler.AcceptChoice(update), "continuous controller update failed bounded journal admission");
        }
        Assert(!assembler.AcceptChoice(first) && assembler.Snapshot.LastChoiceRevision == MapChoiceFrames.MaxChoiceHistory + 2 &&
            assembler.Snapshot.Choices.Length == 1, "evicted old revision replay changed the latest choice or grew the group roster");
        MapIgpChoice recent = Choice(next, MapChoiceFrames.MaxChoiceHistory + 1); recent.SelectedPrefabName = "set-" + recent.Revision;
        Assert(assembler.AcceptChoice(recent), "bounded history lost a recent identical replay"); recent.SelectedPrefabName = "conflict";
        Throws<ArgumentException>(() => assembler.AcceptChoice(recent));
    }

    internal static void ChoiceCapacityAndMaximumUnicodeFrames()
    {
        MapRouteSelection route = Route(MapSelections.MaxScenes);
        foreach (MapRouteScene scene in route.Scenes)
        {
            scene.SceneName = new string('\u4e00', MapSelections.MaxSceneName - 2) + scene.SceneId.ToString("D2");
            scene.TopConnection = new string('\u4e01', MapSelections.MaxConnection);
            scene.BottomConnection = new string('\u4e02', MapSelections.MaxConnection);
        }
        MapRouteSlice[] slices = MapChoiceFrames.SplitRoute(route, long.MaxValue);
        Assert(slices.Length == MapChoiceFrames.MaxRouteSlices, "maximum route exceeded four fragments");
        var assembler = new MapChoiceAssembler();
        foreach (MapRouteSlice slice in slices)
        {
            MapChoiceFrames.Validate(slice);
            Assert(JsonSerializer.SerializeToUtf8Bytes(slice).Length < PacketCodec.MaxPacketBytes, "maximum Unicode route fragment exceeded packet body budget");
            assembler.AcceptRoute(slice);
        }
        for (int i = 1; i <= MapChoiceFrames.MaxChoices; i++)
        {
            MapIgpChoice choice = Choice(slices[0], i); choice.ControllerAddress = "controller-" + i;
            Assert(assembler.AcceptChoice(choice), "legal group capacity was rejected early");
        }
        MapIgpChoice overflow = Choice(slices[0], MapChoiceFrames.MaxChoices + 1); overflow.ControllerAddress = "extra-controller";
        Throws<ArgumentException>(() => assembler.AcceptChoice(overflow));
        Assert(assembler.Snapshot.Choices.Length == 128 && assembler.Snapshot.LastChoiceRevision == 128,
            "capacity rejection consumed revision or overwrote an existing group");
        MapIgpChoice replace = Choice(slices[0], 129); replace.ControllerAddress = "controller-1"; replace.SelectedPrefabName = "updated";
        Assert(assembler.AcceptChoice(replace) && assembler.Snapshot.Choices.Length == 128, "full roster could not update an existing controller");
        MapIgpChoice maximum = Choice(slices[0], 130);
        maximum.ControllerAddress = new string('\u4e03', MapSelections.MaxControllerAddress);
        maximum.SelectedPrefabName = new string('\u4e04', MapSelections.MaxPrefabName);
        maximum.PrefabObjectName = new string('\u4e05', MapSelections.MaxPrefabName);
        MapChoiceFrames.Validate(maximum);
        Assert(JsonSerializer.SerializeToUtf8Bytes(maximum).Length < PacketCodec.MaxPacketBytes, "maximum Unicode IGP choice exceeded packet body budget");
    }

    internal static void MalformedFramesAndResourceModes()
    {
        MapRouteSlice source = MapChoiceFrames.SplitRoute(Route(3), 1)[0];
        InvalidSlice(source, s => s.Generation = 0); InvalidSlice(source, s => s.Count = 5);
        InvalidSlice(source, s => s.Index = 1); InvalidSlice(source, s => s.EntrySceneId = 0);
        InvalidSlice(source, s => s.Scenes = null); InvalidSlice(source, s => s.Scenes = Array.Empty<MapRouteScene>());
        InvalidSlice(source, s => s.Scenes[0].TopY = float.NaN); InvalidSlice(source, s => s.Scenes[0].BottomY = float.PositiveInfinity);
        InvalidSlice(source, s => s.Scenes[0].MapHeight = 0); InvalidSlice(source, s => s.Scenes[0].Offset = 1000001);
        InvalidSlice(source, s => s.Scenes[0].SceneName = "bad\ud800"); InvalidSlice(source, s => s.Scenes[0].TopConnection = "bad\n");
        InvalidSlice(source, s => s.RouteFingerprint = "map-route-v1/" + s.RouteFingerprint.Substring(13).ToUpperInvariant());
        InvalidSlice(source, s => s.RouteFingerprint = "map-selection-v1/" + new string('0', 64));
        InvalidSlice(source, s => s.Scenes[1].SceneName = s.Scenes[0].SceneName);
        MapIgpChoice choice = Choice(source, 1); choice.SelectedPrefabName = null;
        Throws<ArgumentException>(() => MapChoiceFrames.Validate(choice));
        choice.Addressable = false; choice.PrefabObjectName = "native-prefab"; MapChoiceFrames.Validate(choice);
        choice.PrefabObjectName = null; Throws<ArgumentException>(() => MapChoiceFrames.Validate(choice));
        choice = Choice(source, 1); choice.ControllerAddress = new string('x', MapSelections.MaxControllerAddress + 1);
        Throws<ArgumentException>(() => MapChoiceFrames.Validate(choice));
        choice = Choice(source, 1); choice.Revision = 0; Throws<ArgumentException>(() => MapChoiceFrames.Validate(choice));
        Throws<ArgumentException>(() => MapChoiceFrames.Validate(new MapChoiceRetire { Generation = 0, Reason = "bad" }));
        Throws<ArgumentException>(() => MapChoiceFrames.Validate(new MapChoiceRetire { Generation = 1, Reason = " " }));
        Throws<ArgumentException>(() => MapChoiceFrames.Validate(new MapChoiceRetire { Generation = 1, Reason = new string('x', 257) }));
        MapChoiceFrames.Validate(new MapChoiceRetire { Generation = long.MaxValue, Reason = new string('x', 256) });
    }

    private static MapRouteSelection Route(int count)
    {
        var route = new MapRouteSelection { EntrySceneId = 1, Scenes = new MapRouteScene[count] };
        for (int i = 0; i < count; i++) route.Scenes[i] = new MapRouteScene
        {
            SceneId = i + 1, SceneName = "scene-" + (i + 1), Layer = 'A',
            TopConnection = "top-" + i, BottomConnection = "top-" + (i + 1),
            TopY = -100 * i, BottomY = -100 * (i + 1), MapHeight = 100, Offset = -100 * i,
            PreviousSceneId = i, NextSceneId = i + 1 == count ? 0 : i + 2
        };
        return route;
    }
    private static MapIgpChoice Choice(MapRouteSlice route, long revision) => new MapIgpChoice
    {
        Generation = route.Generation, RouteFingerprint = route.RouteFingerprint, Revision = revision, SceneId = 1,
        ControllerAddress = "Runtime/IGP[0]#Component[0]", Addressable = true, SelectedPrefabName = "set-1"
    };
    private static void InvalidSlice(MapRouteSlice source, Action<MapRouteSlice> mutation)
    {
        MapRouteSlice invalid = MapChoiceFrames.Copy(source); mutation(invalid);
        Throws<ArgumentException>(() => MapChoiceFrames.Validate(invalid));
        Throws<ArgumentException>(() => MapChoiceFrames.Copy(invalid));
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
