using System;
using System.Globalization;
using DaveCoop.Core.World;

internal static class MapSelectionTests
{
    internal static void CanonicalOrderingAndCulture()
    {
        MapSelectionManifest source = Example(); string expected = MapSelections.Fingerprint(source);
        Array.Reverse(source.Scenes); Array.Reverse(source.Groups);
        Assert(MapSelections.Fingerprint(source) == expected, "native/list discovery ordering changed selection identity");
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert(MapSelections.Fingerprint(source) == expected, "current culture changed map fingerprint");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            source.Scenes[2].Offset = -0f;
            Assert(MapSelections.Fingerprint(source) == expected, "culture or signed zero changed map fingerprint");
        }
        finally { CultureInfo.CurrentCulture = before; }
        Assert(expected.StartsWith("map-selection-v1/", StringComparison.Ordinal) && expected.Length == 81,
            "unexpected selection fingerprint schema/size");
    }

    internal static void SelectionIdentityAndFieldBoundaries()
    {
        string baseline = MapSelections.Fingerprint(Example());
        MapSelectionManifest changed = Example(); changed.Scenes[0].SceneName += "x"; Different(changed, baseline);
        changed = Example(); changed.Scenes[0].Layer = 'D'; Different(changed, baseline);
        changed = Example(); changed.Scenes[0].TopY += 0.25f; Different(changed, baseline);
        changed = Example(); changed.Scenes[0].BottomY -= 0.25f; Different(changed, baseline);
        changed = Example(); changed.Scenes[0].MapHeight += 0.25f; Different(changed, baseline);
        changed = Example(); changed.Scenes[0].Offset = 0.25f; Different(changed, baseline);
        changed = Example(); changed.Groups[0].SelectedPrefabName += "x"; Different(changed, baseline);
        changed = Example(); changed.Groups[0].ControllerAddress += "x"; Different(changed, baseline);
        changed = Example(); changed.Groups[2].PrefabObjectName += "x"; Different(changed, baseline);
        changed = Example(); changed.Groups[0].Addressable = false; changed.Groups[0].PrefabObjectName = "prefab"; Different(changed, baseline);
        MapSelectionManifest left = Example(), right = Example();
        left.Scenes[0].TopConnection = "ab"; left.Scenes[0].BottomConnection = "c";
        right.Scenes[0].TopConnection = "a"; right.Scenes[0].BottomConnection = "bc";
        Assert(MapSelections.Fingerprint(left) != MapSelections.Fingerprint(right), "connection string field boundaries collided");
    }

    internal static void CopyOwnership()
    {
        MapSelectionManifest source = Example(); MapSelectionManifest copy = MapSelections.Copy(source);
        string original = MapSelections.Fingerprint(copy);
        Assert(copy.Scenes[0].SceneId == 10 && copy.Groups[0].SceneId == 10, "copy did not use canonical scene order");
        source.Scenes[0].SceneName = "changed"; source.Groups[0].SelectedPrefabName = "changed";
        source.Scenes[1] = null; source.Groups[1] = null;
        Assert(MapSelections.Fingerprint(copy) == original, "owned copy retained source array/element references");
        MapSelectionManifest second = MapSelections.Copy(copy);
        second.Scenes[0].TopConnection = "changed"; second.Groups[0].ControllerAddress = "changed";
        Assert(MapSelections.Fingerprint(copy) == original, "copies share mutable route/group elements");
        MapSelectionManifest nullText = Example(); nullText.Scenes[0].TopConnection = null;
        nullText.Scenes[0].BottomConnection = "";
        MapSelectionManifest emptyText = Example(); emptyText.Scenes[0].TopConnection = ""; emptyText.Scenes[0].BottomConnection = null;
        Assert(MapSelections.Fingerprint(nullText) == MapSelections.Fingerprint(emptyText), "optional null/empty connection representation differed");
    }

    internal static void IncompleteAndBoundedInput()
    {
        Throws<ArgumentNullException>(() => MapSelections.Validate(null));
        MapSelectionManifest value = Example(); value.Scenes = null; Reject(value);
        value = Example(); value.Scenes = new[] { value.Scenes[0], value.Scenes[1] }; Reject(value);
        value = Example(); value.Scenes = new MapRouteScene[MapSelections.MaxScenes + 1]; Reject(value);
        value = Example(); value.Groups = null; Reject(value);
        value = Example(); value.Groups = Array.Empty<MapGroupSelection>(); Reject(value);
        value = Example(); value.Groups = new MapGroupSelection[MapSelections.MaxGroups + 1]; Reject(value);
        value = Example(); value.EntrySceneId = 0; Reject(value);
        value = Example(); value.Scenes[0] = null; Reject(value);
        value = Example(); value.Groups[0] = null; Reject(value);
    }

    internal static void DuplicateIdentityRejection()
    {
        MapSelectionManifest value = Example(); value.Scenes[1].SceneId = value.Scenes[0].SceneId; Reject(value);
        value = Example(); value.Scenes[1].SceneName = value.Scenes[0].SceneName; Reject(value);
        value = Example(); value.Groups[1].SceneId = value.Groups[0].SceneId;
        value.Groups[1].ControllerAddress = value.Groups[0].ControllerAddress; Reject(value);
        value = Example(); value.Groups[0].SceneId = 999; Reject(value);
        // Identical hierarchy addresses in different additive scenes are legal.
        value = Example(); value.Groups[1].ControllerAddress = value.Groups[0].ControllerAddress;
        MapSelections.Validate(value);
    }

    internal static void InvalidStringsAndNumbers()
    {
        MapSelectionManifest value = Example(); value.Scenes[0].SceneName = " "; Reject(value);
        value = Example(); value.Scenes[0].SceneName = new string('a', MapSelections.MaxSceneName + 1); Reject(value);
        value = Example(); value.Scenes[0].TopConnection = "a\nb"; Reject(value);
        value = Example(); value.Scenes[0].BottomConnection = new string('a', MapSelections.MaxConnection + 1); Reject(value);
        value = Example(); value.Scenes[0].Layer = '\0'; Reject(value);
        value = Example(); value.Scenes[0].TopY = float.NaN; Reject(value);
        value = Example(); value.Scenes[0].BottomY = float.NegativeInfinity; Reject(value);
        value = Example(); value.Scenes[0].Offset = 1000001; Reject(value);
        value = Example(); value.Scenes[0].MapHeight = 0; Reject(value);
        value = Example(); value.Scenes[0].MapHeight = float.PositiveInfinity; Reject(value);
        value = Example(); value.Groups[0].ControllerAddress = new string('a', MapSelections.MaxControllerAddress + 1); Reject(value);
        value = Example(); value.Groups[0].SelectedPrefabName = "a\ud800"; Reject(value);
        value = Example(); value.Groups[0].SelectedPrefabName = new string('a', MapSelections.MaxPrefabName + 1); Reject(value);
        value = Example(); value.Groups[0].SelectedPrefabName = "fish\U0001F41F"; MapSelections.Validate(value);
    }

    internal static void RouteConnectivityAndModes()
    {
        MapSelectionManifest value = Example(); value.EntrySceneId = 10; Reject(value);
        value = Example(); value.Scenes[1].PreviousSceneId = 0; Reject(value);
        value = Example(); value.Scenes[0].NextSceneId = 999; Reject(value);
        value = Example(); value.Scenes[0].NextSceneId = 0; Reject(value);
        value = Example(); value.Scenes[2].NextSceneId = 30; Reject(value);
        value = Example(); value.Groups[0].SelectedPrefabName = null; Reject(value);
        value = Example(); value.Groups[2].PrefabObjectName = null; Reject(value);
        value = Example(); value.Groups[2].SelectedPrefabName = null; MapSelections.Validate(value);
        value = Example(); value.Groups[0].PrefabObjectName = null; MapSelections.Validate(value);
    }

    internal static void MaximumBoundedManifest()
    {
        var value = new MapSelectionManifest
        {
            EntrySceneId = 1, Scenes = new MapRouteScene[MapSelections.MaxScenes], Groups = new MapGroupSelection[MapSelections.MaxGroups]
        };
        for (int i = 0; i < value.Scenes.Length; i++) value.Scenes[i] = new MapRouteScene
        {
            SceneId = i + 1, SceneName = new string('a', MapSelections.MaxSceneName - 2) + i.ToString("D2", CultureInfo.InvariantCulture),
            Layer = 'A', TopConnection = new string('t', MapSelections.MaxConnection), BottomConnection = new string('b', MapSelections.MaxConnection),
            TopY = 1000000, BottomY = -1000000, Offset = -1000000, MapHeight = 1000000,
            PreviousSceneId = i, NextSceneId = i + 1 == value.Scenes.Length ? 0 : i + 2
        };
        for (int i = 0; i < value.Groups.Length; i++) value.Groups[i] = new MapGroupSelection
        {
            SceneId = i % MapSelections.MaxScenes + 1,
            ControllerAddress = new string('a', MapSelections.MaxControllerAddress - 3) + i.ToString("D3", CultureInfo.InvariantCulture),
            Addressable = true, SelectedPrefabName = new string('a', MapSelections.MaxPrefabName)
        };
        string expected = MapSelections.Fingerprint(value); Array.Reverse(value.Groups); Array.Reverse(value.Scenes);
        Assert(MapSelections.Fingerprint(value) == expected, "maximum bounded manifest depended on discovery order");
    }

    internal static void RouteCandidateIsNotCompleteManifest()
    {
        MapSelectionManifest complete = Example();
        const string legacy = "map-selection-v1/0570621489ca994e1209b0a8c793973f8efa6b385e2735db6ae362740cf23e20";
        Assert(MapSelections.Fingerprint(complete) == legacy, "shared route extraction changed the existing full-manifest fingerprint");
        var route = new MapRouteSelection { EntrySceneId = complete.EntrySceneId, Scenes = complete.Scenes };
        MapSelections.ValidateRoute(route);
        string candidate = MapSelections.FingerprintRoute(route);
        Assert(candidate.StartsWith("map-route-v1/", StringComparison.Ordinal) && candidate.Length == 77 && candidate != legacy,
            "route-only candidate used the complete route-and-IGP fingerprint domain");
        var incomplete = new MapSelectionManifest { EntrySceneId = route.EntrySceneId, Scenes = route.Scenes };
        Reject(incomplete); incomplete.Groups = Array.Empty<MapGroupSelection>(); Reject(incomplete);
        complete.Groups[0].SelectedPrefabName += "changed";
        Assert(MapSelections.Fingerprint(complete) != legacy && MapSelections.FingerprintRoute(route) == candidate,
            "later IGP selection changed route identity or disappeared from the complete identity");
        incomplete.Groups = new MapGroupSelection[MapSelections.MaxGroups + 1]; Reject(incomplete);
        Assert(MapSelections.FingerprintRoute(route) == candidate, "rejected complete manifest damaged its independently valid route");
    }

    internal static void RouteChainAndBoundaryRejection()
    {
        Throws<ArgumentNullException>(() => MapSelections.ValidateRoute(null));
        InvalidRoute(r => r.EntrySceneId = 0); InvalidRoute(r => r.EntrySceneId = 10);
        InvalidRoute(r => r.Scenes = null);
        InvalidRoute(r => r.Scenes = new[] { r.Scenes[0], r.Scenes[1] });
        InvalidRoute(r => r.Scenes = new MapRouteScene[MapSelections.MaxScenes + 1]);
        InvalidRoute(r => r.Scenes[1].SceneId = r.Scenes[0].SceneId);
        InvalidRoute(r => r.Scenes[1].SceneName = r.Scenes[0].SceneName);
        InvalidRoute(r => r.Scenes[0].NextSceneId = 0);
        InvalidRoute(r => r.Scenes[0].NextSceneId = 999);
        InvalidRoute(r => r.Scenes[1].PreviousSceneId = 0);
        InvalidRoute(r => r.Scenes[2].NextSceneId = 30);
        InvalidRoute(r => r.Scenes[1].NextSceneId = r.Scenes[1].SceneId);
        InvalidRoute(r => r.Scenes[0].SceneName = "invalid\ud800");
        InvalidRoute(r => r.Scenes[0].Layer = '\uD800');
        InvalidRoute(r => r.Scenes[0].TopConnection = "top\n");
        InvalidRoute(r => r.Scenes[0].BottomConnection = new string('b', MapSelections.MaxConnection + 1));
        InvalidRoute(r => r.Scenes[0].TopY = float.NaN);
        InvalidRoute(r => r.Scenes[0].BottomY = float.NegativeInfinity);
        InvalidRoute(r => r.Scenes[0].Offset = 1000001);
        InvalidRoute(r => r.Scenes[0].MapHeight = 0);
        InvalidRoute(r => r.Scenes[0].MapHeight = float.PositiveInfinity);
    }

    internal static void RouteCopyOwnershipAndCanonicalCompatibility()
    {
        MapRouteSelection source = RouteExample();
        const string expected = "map-route-v1/7568e19089be0c484bd4473a98ba2d8768bb5146b72586caf3bd725ac6c9dba3";
        Assert(MapSelections.FingerprintRoute(source) == expected, "route fingerprint lost its canonical field schema");
        MapRouteSelection copy = MapSelections.CopyRoute(source);
        Assert(copy.EntrySceneId == 30 && copy.Scenes[0].SceneId == 10 && copy.Scenes[1].SceneId == 20,
            "route copy used native discovery order instead of canonical scene order");
        source.EntrySceneId = 999; source.Scenes[0].SceneName = "changed"; source.Scenes[1] = null;
        Assert(MapSelections.FingerprintRoute(copy) == expected, "frozen route retained mutable source arrays or elements");
        MapRouteSelection next = MapSelections.CopyRoute(copy); next.Scenes[0].Offset += 0.25f;
        Assert(MapSelections.FingerprintRoute(next) != expected && MapSelections.FingerprintRoute(copy) == expected,
            "route copies shared geometry or omitted it from identity");
        Array.Reverse(copy.Scenes);
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR"); copy.Scenes[0].Offset = -0f;
            Assert(MapSelections.FingerprintRoute(copy) == expected, "route ordering, culture or signed zero changed identity");
        }
        finally { CultureInfo.CurrentCulture = before; }
        MapRouteSelection optional = RouteExample(); optional.Scenes[0].TopConnection = null;
        MapRouteSelection normalized = MapSelections.CopyRoute(optional);
        Assert(normalized.Scenes[2].TopConnection == "", "route copy failed to normalize optional connection text");
        optional.Scenes[0].TopConnection = "";
        Assert(MapSelections.FingerprintRoute(optional) == MapSelections.FingerprintRoute(normalized),
            "route-only and complete canonical connection representation diverged");
    }

    private static MapRouteSelection RouteExample()
    {
        MapSelectionManifest manifest = Example();
        return new MapRouteSelection { EntrySceneId = manifest.EntrySceneId, Scenes = manifest.Scenes };
    }

    private static void InvalidRoute(Action<MapRouteSelection> mutation)
    {
        MapRouteSelection route = RouteExample(); mutation(route);
        Throws<ArgumentException>(() => MapSelections.ValidateRoute(route));
        Throws<ArgumentException>(() => MapSelections.CopyRoute(route));
        Throws<ArgumentException>(() => MapSelections.FingerprintRoute(route));
    }

    private static MapSelectionManifest Example() => new MapSelectionManifest
    {
        EntrySceneId = 30,
        Scenes = new[]
        {
            new MapRouteScene { SceneId = 30, SceneName = "A03_01_02", Layer = 'A', TopConnection = "top", BottomConnection = "AB", TopY = 0, BottomY = -100, MapHeight = 100, Offset = 0, NextSceneId = 10 },
            new MapRouteScene { SceneId = 10, SceneName = "B06_02_02", Layer = 'B', TopConnection = "AB", BottomConnection = "BC", TopY = -100, BottomY = -200, MapHeight = 100, Offset = -100, PreviousSceneId = 30, NextSceneId = 20 },
            new MapRouteScene { SceneId = 20, SceneName = "C04_02_01", Layer = 'C', TopConnection = "BC", BottomConnection = "bottom", TopY = -200, BottomY = -300, MapHeight = 100, Offset = -200, PreviousSceneId = 10 }
        },
        Groups = new[]
        {
            new MapGroupSelection { SceneId = 30, ControllerAddress = "RuntimeObjects[0]/A03_IGP[0]#IGP[0]", Addressable = true, SelectedPrefabName = "IGPSet_A03_1" },
            new MapGroupSelection { SceneId = 10, ControllerAddress = "RuntimeObjects[0]/B06_IGP[0]#IGP[0]", Addressable = true, SelectedPrefabName = "IGPSet_B06_1" },
            new MapGroupSelection { SceneId = 20, ControllerAddress = "RuntimeObjects[0]/C04_IGP[0]#IGP[0]", Addressable = false, PrefabObjectName = "IGPSet_C04_1" }
        }
    };

    private static void Different(MapSelectionManifest value, string expected)
        => Assert(MapSelections.Fingerprint(value) != expected, "changed selected route/resource shared fingerprint");
    private static void Reject(MapSelectionManifest value) => Throws<ArgumentException>(() => MapSelections.Validate(value));
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
