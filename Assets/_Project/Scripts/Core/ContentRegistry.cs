using System;
using System.Collections.Generic;

namespace WorldBeyond.Core
{
    // Explizite Werte, nur anhängen.
    public enum ContentKind : byte
    {
        Unit = 0,
        Building = 1,
        Resource = 2,
        Tech = 3,
        Biome = 4,
        Terrain = 5,
    }

    /// <summary>
    /// String-Schlüssel ↔ Laufzeit-int (ARCHITECTURE §11.1 Regel 3). Laufzeit-ints beginnen je Art bei 1,
    /// sind nur innerhalb einer Sitzung gültig und dürfen nie gespeichert werden.
    /// Registrierung beim Start aus den Inhaltstabellen.
    /// </summary>
    public sealed class ContentRegistry
    {
        private sealed class Table
        {
            public readonly Dictionary<string, int> ByKey = new Dictionary<string, int>(StringComparer.Ordinal);
            public readonly List<string> Keys = new List<string> { null }; // Index 0 = ungültig
        }

        private readonly Dictionary<ContentKind, Table> _tables = new Dictionary<ContentKind, Table>();

        /// <summary>Doppelter oder leerer Schlüssel → Exception beim Start (Konfigurationsfehler).</summary>
        public int Register(ContentKind kind, string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException($"ContentRegistry: leerer Schlüssel für {kind}.", nameof(key));
            var t = GetTable(kind);
            if (t.ByKey.ContainsKey(key))
                throw new InvalidOperationException($"ContentRegistry: Schlüssel '{key}' für {kind} ist doppelt registriert.");
            int id = t.Keys.Count;
            t.Keys.Add(key);
            t.ByKey.Add(key, id);
            return id;
        }

        public bool TryGetId(ContentKind kind, string key, out int runtimeId)
        {
            runtimeId = 0;
            return key != null && _tables.TryGetValue(kind, out var t) && t.ByKey.TryGetValue(key, out runtimeId);
        }

        /// <summary>null bei unbekannter ID (keine Exception, Regel 6).</summary>
        public string GetKey(ContentKind kind, int runtimeId)
        {
            if (!_tables.TryGetValue(kind, out var t) || runtimeId <= 0 || runtimeId >= t.Keys.Count) return null;
            return t.Keys[runtimeId];
        }

        public bool Contains(ContentKind kind, string key) => TryGetId(kind, key, out _);

        public int Count(ContentKind kind) => _tables.TryGetValue(kind, out var t) ? t.Keys.Count - 1 : 0;

        private Table GetTable(ContentKind kind)
        {
            if (!_tables.TryGetValue(kind, out var t))
            {
                t = new Table();
                _tables.Add(kind, t);
            }
            return t;
        }
    }
}
