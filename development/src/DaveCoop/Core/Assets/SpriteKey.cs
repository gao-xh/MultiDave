using System;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace DaveCoop.Core.Assets
{
    public sealed class SpriteDescriptor
    {
        public string TextureName { get; set; }
        public string SpriteName { get; set; }
        public int TextureWidth { get; set; }
        public int TextureHeight { get; set; }
        public Vector4 Rect { get; set; }
        public Vector2 Pivot { get; set; }
        public Vector4 Border { get; set; }
        public float PixelsPerUnit { get; set; }
    }

    public static class SpriteKey
    {
        // Metadata key, not a pixel hash or game asset GUID. Ambiguity is handled
        // by AssetRegistry; a matching key must never override that check.
        public static string Create(SpriteDescriptor sprite)
        {
            if (sprite == null) throw new ArgumentNullException(nameof(sprite));
            RequireName(sprite.TextureName); RequireName(sprite.SpriteName);
            if (sprite.TextureWidth < 1 || sprite.TextureHeight < 1 || sprite.TextureWidth > 65536 || sprite.TextureHeight > 65536 ||
                sprite.Rect.X < 0 || sprite.Rect.Y < 0 || sprite.Rect.Z <= 0 || sprite.Rect.W <= 0 || sprite.PixelsPerUnit <= 0)
                throw new ArgumentException("Invalid sprite geometry.");
            var text = new StringBuilder("sprite-v1:");
            AppendName(text, sprite.TextureName); AppendName(text, sprite.SpriteName);
            text.Append(sprite.TextureWidth.ToString(CultureInfo.InvariantCulture)).Append(':')
                .Append(sprite.TextureHeight.ToString(CultureInfo.InvariantCulture)).Append(':');
            Append(text, sprite.Rect.X); Append(text, sprite.Rect.Y); Append(text, sprite.Rect.Z); Append(text, sprite.Rect.W);
            Append(text, sprite.Pivot.X); Append(text, sprite.Pivot.Y);
            Append(text, sprite.Border.X); Append(text, sprite.Border.Y); Append(text, sprite.Border.Z); Append(text, sprite.Border.W);
            Append(text, sprite.PixelsPerUnit);
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(text.ToString());
            using SHA256 hash = SHA256.Create();
            return "sprite-v1/" + Convert.ToHexString(hash.ComputeHash(bytes)).ToLowerInvariant();
        }

        private static void Append(StringBuilder text, float value)
        {
            if (!float.IsFinite(value)) throw new ArgumentException("Non-finite sprite metadata.");
            // Canonicalize +/- zero; use exact bits instead of culture/format rounding.
            text.Append(BitConverter.SingleToInt32Bits(value == 0 ? 0 : value).ToString("X8", CultureInfo.InvariantCulture)).Append(':');
        }

        private static void AppendName(StringBuilder text, string value)
            => text.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append(':');

        private static void RequireName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 1024) throw new ArgumentException("Invalid sprite name.");
            foreach (char character in name) if (char.IsControl(character)) throw new ArgumentException("Control character in sprite name.");
        }
    }
}
