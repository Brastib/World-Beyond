using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using WorldBeyond.Core;
using WorldBeyond.Simulation.World.Generation;

namespace WorldBeyond.Simulation.World
{
    // Spielstand der Welt (ARCHITECTURE §11, SYSTEM_INTERFACES §4.5): Generator-Einstellungen + geänderte Kacheln.
    // Beim Laden wird die Welt aus dem Seed neu erzeugt und die Änderungen werden eingespielt.
    // Ab FullGridThreshold geänderten Kacheln wird das komplette Raster gzip-komprimiert gespeichert.
    // Enums stehen als Namen im Spielstand (Paletten), nie als Zahl (Regel 3/4).
    public sealed partial class WorldGrid : ISaveable, ISaveReferenceCheck
    {
        public const string Key = "world";
        public const float FullGridThreshold = 0.3f;

        public string SaveKey => Key;
        public int LoadOrder => 0;
        public int SectionVersion => 1;
        public Type StateType => typeof(WorldGridState);

        // Diagnose: ob das letzte erfolgreiche oder versuchte Laden die Welt neu generieren musste.
        public bool LastRestoreRegenerated { get; private set; }

        static bool SameSettings(WorldGenSettings a, WorldGenSettings b) =>
            a != null && b != null && a.Seed == b.Seed && a.Width == b.Width && a.Height == b.Height
            && a.SeaLevel == b.SeaLevel && a.MaxHeight == b.MaxHeight && a.LandRatio == b.LandRatio
            && a.RiverCount == b.RiverCount && a.ForestDensity == b.ForestDensity && a.ResourceRichness == b.ResourceRichness;

        public object CaptureState()
        {
            var s = GenSettings;
            var state = new WorldGridState
            {
                Seed = s.Seed,
                Width = s.Width,
                Height = s.Height,
                SeaLevel = s.SeaLevel,
                MaxHeight = s.MaxHeight,
                LandRatio = s.LandRatio,
                RiverCount = s.RiverCount,
                ForestDensity = s.ForestDensity,
                ResourceRichness = s.ResourceRichness,
                TerrainNames = Enum.GetNames(typeof(TerrainType)),
                BiomeNames = Enum.GetNames(typeof(BiomeType)),
                ResourceNames = Enum.GetNames(typeof(ResourceType)),
            };
            var palettes = new Palettes(state);

            int n = _tiles.Length;
            if (_changedCount > n * FullGridThreshold)
            {
                state.FullGrid = true;
                state.FullGridData = EncodeFullGrid(palettes);
                state.Tiles = new WorldTileRecord[0];
                return state;
            }

            var list = new List<WorldTileRecord>(_changedCount);
            for (int i = 0; i < n; i++)
            {
                if (_changedSinceGeneration[i])
                    list.Add(ToRecord(i, in _tiles[i], palettes));
            }
            state.Tiles = list.ToArray();
            return state;
        }

        public bool TryRestoreState(object dto, out string error)
        {
            if (!(dto is WorldGridState st))
            {
                error = $"world: erwartet {nameof(WorldGridState)}, erhalten {dto?.GetType().Name ?? "null"}.";
                return false;
            }
            var settings = new WorldGenSettings
            {
                Seed = st.Seed,
                Width = st.Width,
                Height = st.Height,
                SeaLevel = st.SeaLevel,
                MaxHeight = st.MaxHeight,
                LandRatio = st.LandRatio,
                RiverCount = st.RiverCount,
                ForestDensity = st.ForestDensity,
                ResourceRichness = st.ResourceRichness,
            };
            if (!settings.Validate(out string settingsError))
            {
                error = "world: ungültige Generator-Einstellungen: " + settingsError;
                return false;
            }
            if (!Palettes.TryCreate(st, out Palettes palettes, out error))
                return false;

            // In eine neue Instanz laden; erst bei Erfolg übernehmen (bisheriger Zustand bleibt bei Fehlern erhalten).
            // Stammt die aktuelle Welt aus denselben Einstellungen (typisch: frische Komposition mit Seed und Größe des
            // Spielstands, auch nach ersten Änderungen wie Start-Siedlungen), wird der generierte Zustand aus den
            // gesicherten Ursprungswerten wiederhergestellt statt neu generiert.
            WorldGrid fresh;
            if (SameSettings(GenSettings, settings))
            {
                fresh = CloneGenerated();
                LastRestoreRegenerated = false;
            }
            else
            {
                fresh = new WorldGenerator().Generate(settings);
                LastRestoreRegenerated = true;
            }
            int n = fresh._tiles.Length;
            if (st.FullGrid)
            {
                if (!fresh.DecodeFullGrid(st.FullGridData, palettes, out error))
                    return false;
            }
            else
            {
                if (st.Tiles == null)
                {
                    error = "world: Kachelliste fehlt.";
                    return false;
                }
                foreach (WorldTileRecord r in st.Tiles)
                {
                    if (r == null || r.Index < 0 || r.Index >= n)
                    {
                        error = $"world: Kachelindex {(r == null ? "null" : r.Index.ToString())} liegt außerhalb von 0..{n - 1}.";
                        return false;
                    }
                    if (!FromRecord(r, palettes, out TileData tile, out error))
                        return false;
                    fresh.ApplyLoadedDelta(r.Index, in tile);
                }
            }

            CopyFrom(fresh);
            error = null;
            return true;
        }

        // Werden von der Composition Root gesetzt, sobald Gebäude- und Zivilisationsregister existieren (Phase 3).
        // Ohne sie werden die jeweiligen Verweise beim Laden nicht geprüft.
        public Func<EntityId, bool> BuildingExists { get; set; }
        public Func<int, bool> CivilizationExists { get; set; }

        public void CheckReferences(SimContext ctx, LoadReport report)
        {
            var warnings = new List<string>();
            CleanupInvalidReferences(BuildingExists, CivilizationExists, warnings);
            foreach (string w in warnings)
                report.Warn(w);
        }

        // Bereinigt Verweise auf nicht mehr existierende Gebäude/Zivilisationen nach dem Laden (ARCHITECTURE §11.1 Regel 6).
        // Wird von der Composition Root nach dem Einspielen aller Sektionen aufgerufen; Rückgabe = Anzahl bereinigter Kacheln.
        public int CleanupInvalidReferences(Func<EntityId, bool> buildingExists, Func<int, bool> civExists, List<string> warnings)
        {
            int fixedCount = 0;
            for (int i = 0; i < _tiles.Length; i++)
            {
                ref TileData t = ref _tiles[i];
                bool changed = false;
                if (buildingExists != null && t.Building.IsValid && !buildingExists(t.Building))
                {
                    warnings?.Add($"world: Kachel {i % Width},{i / Width} verweist auf nicht existierendes Gebäude {t.Building}; freigegeben.");
                    t.Building = EntityId.None;
                    changed = true;
                }
                if (civExists != null && t.OwnerCivId != 0 && !civExists(t.OwnerCivId))
                {
                    warnings?.Add($"world: Kachel {i % Width},{i / Width} gehört nicht existierender Zivilisation {t.OwnerCivId}; Besitz entfernt.");
                    t.OwnerCivId = 0;
                    changed = true;
                }
                if (changed)
                {
                    fixedCount++;
                    MarkChangedSilently(i);
                }
            }
            if (fixedCount > 0)
                NavigationVersion++;
            return fixedCount;
        }

        WorldTileRecord ToRecord(int index, in TileData t, Palettes p)
        {
            return new WorldTileRecord
            {
                Index = index,
                Height = t.Height,
                WaterLevel = t.WaterLevel,
                Terrain = t.Terrain.ToString(),
                Biome = t.Biome.ToString(),
                Resource = t.Resource.ToString(),
                ResourceAmount = t.ResourceAmount,
                Fertility = t.Fertility,
                Moisture = t.Moisture,
                OwnerCivId = t.OwnerCivId,
                BuildingIndex = t.Building.Index,
                BuildingGeneration = t.Building.Generation,
                Flags = (ushort)t.Flags,
            };
        }

        static bool FromRecord(WorldTileRecord r, Palettes p, out TileData tile, out string error)
        {
            tile = default;
            if (!Enum.TryParse(r.Terrain, false, out TerrainType terrain) || !Enum.IsDefined(typeof(TerrainType), terrain))
            {
                error = $"world: unbekannter Geländetyp '{r.Terrain}'.";
                return false;
            }
            if (!Enum.TryParse(r.Biome, false, out BiomeType biome) || !Enum.IsDefined(typeof(BiomeType), biome))
            {
                error = $"world: unbekanntes Biom '{r.Biome}'.";
                return false;
            }
            if (!Enum.TryParse(r.Resource, false, out ResourceType resource) || !Enum.IsDefined(typeof(ResourceType), resource))
            {
                error = $"world: unbekannte Ressource '{r.Resource}'.";
                return false;
            }
            if (!IsFinite(r.Height) || !IsFinite(r.WaterLevel) || r.OwnerCivId < 0)
            {
                error = $"world: Kachel {r.Index} hat ungültige Werte.";
                return false;
            }
            tile = new TileData
            {
                Height = r.Height,
                WaterLevel = r.WaterLevel,
                Terrain = terrain,
                Biome = biome,
                Resource = resource,
                ResourceAmount = r.ResourceAmount,
                Fertility = r.Fertility,
                Moisture = r.Moisture,
                OwnerCivId = r.OwnerCivId,
                Building = r.BuildingIndex > 0 ? new EntityId(r.BuildingIndex, r.BuildingGeneration) : EntityId.None,
                Flags = (TileFlags)r.Flags,
            };
            error = null;
            return true;
        }

        static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        // Binärformat je Kachel: Height, WaterLevel (float), Terrain/Biome/Resource (Palettenindex, byte),
        // ResourceAmount (ushort), Fertility, Moisture (byte), OwnerCivId, BuildingIndex, BuildingGeneration (int),
        // Flags (ushort), Changed (byte). Little Endian (BinaryWriter), gzip, Base64.
        string EncodeFullGrid(Palettes p)
        {
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionLevel.Optimal, true))
            using (var w = new BinaryWriter(gz))
            {
                for (int i = 0; i < _tiles.Length; i++)
                {
                    ref readonly TileData t = ref _tiles[i];
                    w.Write(t.Height);
                    w.Write(t.WaterLevel);
                    w.Write((byte)t.Terrain);
                    w.Write((byte)t.Biome);
                    w.Write((byte)t.Resource);
                    w.Write(t.ResourceAmount);
                    w.Write(t.Fertility);
                    w.Write(t.Moisture);
                    w.Write(t.OwnerCivId);
                    w.Write(t.Building.Index);
                    w.Write(t.Building.Generation);
                    w.Write((ushort)t.Flags);
                    w.Write(_changedSinceGeneration[i] ? (byte)1 : (byte)0);
                }
            }
            return Convert.ToBase64String(ms.ToArray());
        }

        bool DecodeFullGrid(string data, Palettes p, out string error)
        {
            if (string.IsNullOrEmpty(data))
            {
                error = "world: Rasterdaten fehlen.";
                return false;
            }
            try
            {
                byte[] bytes = Convert.FromBase64String(data);
                using var ms = new MemoryStream(bytes);
                using var gz = new GZipStream(ms, CompressionMode.Decompress);
                using var r = new BinaryReader(gz);
                ResetChangeTracking();
                for (int i = 0; i < _tiles.Length; i++)
                {
                    var t = new TileData
                    {
                        Height = r.ReadSingle(),
                        WaterLevel = r.ReadSingle(),
                    };
                    if (!p.TryTerrain(r.ReadByte(), out t.Terrain) || !p.TryBiome(r.ReadByte(), out t.Biome) || !p.TryResource(r.ReadByte(), out t.Resource))
                    {
                        error = $"world: unbekannter Inhaltsschlüssel in Kachel {i}.";
                        return false;
                    }
                    t.ResourceAmount = r.ReadUInt16();
                    t.Fertility = r.ReadByte();
                    t.Moisture = r.ReadByte();
                    t.OwnerCivId = r.ReadInt32();
                    int bi = r.ReadInt32(), bg = r.ReadInt32();
                    t.Building = bi > 0 ? new EntityId(bi, bg) : EntityId.None;
                    t.Flags = (TileFlags)r.ReadUInt16();
                    bool changed = r.ReadByte() != 0;
                    if (!IsFinite(t.Height) || !IsFinite(t.WaterLevel) || t.OwnerCivId < 0)
                    {
                        error = $"world: Kachel {i} hat ungültige Werte.";
                        return false;
                    }
                    // Ungeänderte Kacheln entsprechen der Generierung; geänderte sichern den generierten Wert als Ursprung.
                    if (changed)
                        ApplyLoadedDelta(i, in t);
                    else
                        _tiles[i] = t;
                }
            }
            catch (Exception e) when (e is FormatException || e is InvalidDataException || e is EndOfStreamException || e is IOException)
            {
                error = "world: Rasterdaten beschädigt (" + e.GetType().Name + ").";
                return false;
            }
            error = null;
            return true;
        }

        // Übersetzt Palettenindizes des Spielstands (Enum-Namen zum Speicherzeitpunkt) in aktuelle Enum-Werte.
        sealed class Palettes
        {
            TerrainType[] _terrain;
            BiomeType[] _biome;
            ResourceType[] _resource;

            public Palettes(WorldGridState st)
            {
                TryBuild(st.TerrainNames, out _terrain, out _);
                TryBuild(st.BiomeNames, out _biome, out _);
                TryBuild(st.ResourceNames, out _resource, out _);
            }

            Palettes() { }

            public static bool TryCreate(WorldGridState st, out Palettes p, out string error)
            {
                p = new Palettes();
                if (!TryBuild(st.TerrainNames, out p._terrain, out error) ||
                    !TryBuild(st.BiomeNames, out p._biome, out error) ||
                    !TryBuild(st.ResourceNames, out p._resource, out error))
                {
                    error = "world: " + error;
                    return false;
                }
                return true;
            }

            static bool TryBuild<T>(string[] names, out T[] values, out string error) where T : struct, Enum
            {
                values = null;
                if (names == null)
                {
                    error = $"Palette {typeof(T).Name} fehlt.";
                    return false;
                }
                values = new T[names.Length];
                for (int i = 0; i < names.Length; i++)
                {
                    if (!Enum.TryParse(names[i], false, out T v) || !Enum.IsDefined(typeof(T), v))
                    {
                        error = $"unbekannter {typeof(T).Name} '{names[i]}'.";
                        return false;
                    }
                    values[i] = v;
                }
                error = null;
                return true;
            }

            public bool TryTerrain(byte i, out TerrainType v) => TryGet(_terrain, i, out v);
            public bool TryBiome(byte i, out BiomeType v) => TryGet(_biome, i, out v);
            public bool TryResource(byte i, out ResourceType v) => TryGet(_resource, i, out v);

            static bool TryGet<T>(T[] arr, byte i, out T v)
            {
                if (arr != null && i < arr.Length)
                {
                    v = arr[i];
                    return true;
                }
                v = default;
                return false;
            }
        }
    }

    [Serializable]
    public sealed class WorldGridState
    {
        public ulong Seed;
        public int Width;
        public int Height;
        public float SeaLevel;
        public float MaxHeight;
        public float LandRatio;
        public int RiverCount;
        public float ForestDensity;
        public float ResourceRichness;

        // Enum-Namen in Wertreihenfolge zum Speicherzeitpunkt; FullGridData referenziert Indizes darin.
        public string[] TerrainNames;
        public string[] BiomeNames;
        public string[] ResourceNames;

        public bool FullGrid;
        public string FullGridData;
        public WorldTileRecord[] Tiles;
    }

    [Serializable]
    public sealed class WorldTileRecord
    {
        public int Index;               // y * Width + x
        public float Height;
        public float WaterLevel;
        public string Terrain;          // Enum-Name
        public string Biome;            // Enum-Name
        public string Resource;         // Enum-Name
        public ushort ResourceAmount;
        public byte Fertility;
        public byte Moisture;
        public int OwnerCivId;
        public int BuildingIndex;
        public int BuildingGeneration;
        public ushort Flags;
    }
}
