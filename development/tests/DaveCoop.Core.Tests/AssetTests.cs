using System;
using System.Globalization;
using System.Numerics;
using DaveCoop.Core.Assets;

internal static class AssetTests
{
    internal static void StableKeys()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            string expected = SpriteKey.Create(Descriptor());
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert(SpriteKey.Create(Descriptor()) == expected, "asset key changed with machine culture");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            Assert(SpriteKey.Create(Descriptor()) == expected && expected.Length == 74, "asset key changed with numeric formatting");
            SpriteDescriptor negativeZero = Descriptor(); negativeZero.Pivot = new Vector2(-0f, 0);
            SpriteDescriptor positiveZero = Descriptor(); positiveZero.Pivot = Vector2.Zero;
            Assert(SpriteKey.Create(negativeZero) == SpriteKey.Create(positiveZero), "equivalent signed zero changed key");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    internal static void DistinctDescriptors()
    {
        SpriteDescriptor first = Descriptor(); first.TextureName = "a:b"; first.SpriteName = "c";
        SpriteDescriptor second = Descriptor(); second.TextureName = "a"; second.SpriteName = "b:c";
        Assert(SpriteKey.Create(first) != SpriteKey.Create(second), "names with delimiters collided");
        string original = SpriteKey.Create(first);
        first.Rect += new Vector4(1, 0, 0, 0);
        Assert(SpriteKey.Create(first) != original, "atlas rectangle was omitted from asset identity");
        first = Descriptor(); original = SpriteKey.Create(first); first.PixelsPerUnit += 1;
        Assert(SpriteKey.Create(first) != original, "pixels per unit was omitted from asset identity");
    }

    internal static void InvalidDescriptors()
    {
        SpriteDescriptor invalid = Descriptor(); invalid.Rect = new Vector4(0, 0, float.NaN, 1);
        Throws<ArgumentException>(() => SpriteKey.Create(invalid));
        invalid = Descriptor(); invalid.SpriteName = "bad\nname";
        Throws<ArgumentException>(() => SpriteKey.Create(invalid));
        invalid = Descriptor(); invalid.PixelsPerUnit = 0;
        Throws<ArgumentException>(() => SpriteKey.Create(invalid));
        invalid = Descriptor(); invalid.Border = new Vector4(float.PositiveInfinity, 0, 0, 0);
        Throws<ArgumentException>(() => SpriteKey.Create(invalid));
    }

    internal static void RegistryCollisions()
    {
        var assets = new AssetRegistry<object>(2);
        string key = SpriteKey.Create(Descriptor());
        var first = new object(); var second = new object();
        Assert(assets.Register(key, 10, first) == AssetRegistration.Added, "new asset rejected");
        Assert(assets.Register(key, 10, first) == AssetRegistration.Existing && assets.Count == 1, "same native resource duplicated");
        Assert(assets.TryResolve(key, out object found) && ReferenceEquals(first, found), "registered asset missing");
        Assert(assets.Register(key, 11, second) == AssetRegistration.Ambiguous && !assets.TryResolve(key, out _),
            "metadata collision silently selected a sprite");
        Assert(assets.Register(key, 10, first) == AssetRegistration.Ambiguous, "re-registration cleared ambiguity");
    }

    internal static void RegistryBoundsAndClear()
    {
        var assets = new AssetRegistry<object>(1);
        var value = new object(); assets.Register("one", 1, value);
        Assert(assets.Register("two", 2, value) == AssetRegistration.Full && !assets.TryResolve("two", out _), "resource table grew beyond capacity");
        Assert(!assets.TryResolve(null, out _) && !assets.TryResolve("missing", out _), "unknown resource resolved");
        assets.Clear();
        Assert(assets.Count == 0 && !assets.TryResolve("one", out _) && assets.Register("two", 2, value) == AssetRegistration.Added,
            "previous scene resource survived reset");
    }

    private static SpriteDescriptor Descriptor() => new SpriteDescriptor
    {
        TextureName = "fixture/atlas", SpriteName = "戴夫-swim", TextureWidth = 1024, TextureHeight = 1024,
        Rect = new Vector4(32, 64, 48, 80), Pivot = new Vector2(24, 40), PixelsPerUnit = 16
    };

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<T>(Action run) where T : Exception
    {
        try { run(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
}
