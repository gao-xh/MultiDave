using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace DaveCoop.Core.World
{
    public sealed class CanonicalHash
    {
        private readonly StringBuilder _text = new StringBuilder();
        private const int MaxCharacters = 8388608;

        public CanonicalHash(string schema) { Add(schema); }

        public CanonicalHash Add(string value)
        {
            if (value == null || value.Length > 4096) throw new ArgumentException("Invalid fingerprint field.");
            foreach (char character in value) if (char.IsControl(character)) throw new ArgumentException("Invalid fingerprint text.");
            _text.Append('s').Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
            CheckSize(); return this;
        }

        public CanonicalHash Add(int value)
        {
            _text.Append('i').Append(value.ToString(CultureInfo.InvariantCulture)).Append(':'); CheckSize(); return this;
        }

        public CanonicalHash Add(float value)
        {
            if (!float.IsFinite(value)) throw new ArgumentException("Non-finite fingerprint geometry.");
            _text.Append('f').Append(BitConverter.SingleToInt32Bits(value == 0 ? 0 : value).ToString("X8", CultureInfo.InvariantCulture));
            CheckSize(); return this;
        }

        public string Finish()
        {
            using SHA256 hash = SHA256.Create();
            return Convert.ToHexString(hash.ComputeHash(new UTF8Encoding(false, true).GetBytes(_text.ToString()))).ToLowerInvariant();
        }

        private void CheckSize()
        {
            if (_text.Length > MaxCharacters) throw new ArgumentException("Fingerprint input limit exceeded.");
        }
    }

    public static class LayoutFingerprint
    {
        public const int MaxEntries = 4096;

        public static string Create(string scene, IEnumerable<string> entries)
        {
            if (string.IsNullOrWhiteSpace(scene) || entries == null) throw new ArgumentException("Missing scene layout.");
            // Preserve multiplicity: duplicate geometry is part of a layout too.
            string[] sorted = entries.Take(MaxEntries + 1).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            if (sorted.Length == 0 || sorted.Length > MaxEntries) throw new ArgumentException("Invalid layout entry count.");
            var hash = new CanonicalHash("world-layout-v1").Add(scene).Add(sorted.Length);
            foreach (string entry in sorted)
            {
                if (entry == null || entry.Length != 64 || entry.Any(character => !Uri.IsHexDigit(character)))
                    throw new ArgumentException("Expected layout entry hashes.");
                hash.Add(entry.ToLowerInvariant());
            }
            return "layout-v1/" + hash.Finish();
        }
    }
}
