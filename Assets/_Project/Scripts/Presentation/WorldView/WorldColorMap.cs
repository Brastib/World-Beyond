using System;
using UnityEngine;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Presentation.WorldView
{
    // Farben der Natur laut UI_STYLE_GUIDE A4 (Gras, Wald, Sand, Fels, Schnee, Flach-/Tiefwasser) plus abgeleitete Töne.
    public static class WorldPalette
    {
        public static readonly Color32 Grass = new Color32(0x6F, 0xA8, 0x3C, 255);
        public static readonly Color32 Forest = new Color32(0x3F, 0x7A, 0x2E, 255);
        public static readonly Color32 Sand = new Color32(0xE3, 0xC9, 0x87, 255);
        public static readonly Color32 Rock = new Color32(0x8A, 0x83, 0x78, 255);
        public static readonly Color32 Snow = new Color32(0xEE, 0xF1, 0xF2, 255);
        public static readonly Color32 ShallowWater = new Color32(0x3F, 0xB6, 0xC9, 255);
        public static readonly Color32 DeepWater = new Color32(0x24, 0x62, 0xB5, 255);

        // Abgeleitet, nicht im Style Guide festgelegt (im Style Lock zu prüfen).
        public static readonly Color32 Desert = new Color32(0xD9, 0xB0, 0x6A, 255);
        public static readonly Color32 Savanna = new Color32(0xB5, 0xB0, 0x52, 255);
        public static readonly Color32 Swamp = new Color32(0x55, 0x6E, 0x3A, 255);
        public static readonly Color32 Tundra = new Color32(0x9C, 0xA4, 0x7E, 255);
        public static readonly Color32 Boreal = new Color32(0x2F, 0x5F, 0x34, 255);
        public static readonly Color32 Mountain = new Color32(0x76, 0x70, 0x68, 255);
        public static readonly Color32 Road = new Color32(0xA8, 0x8B, 0x63, 255);
        public static readonly Color32 Scorched = new Color32(0x3A, 0x33, 0x2C, 255);
    }

    // Farbkarte der Welt: ein Pixel pro Kachel, bilinear gefiltert. Dient als Textur für Terrain und Wasser
    // und als Quelle der Minikarte (gleiche Textur, UI zeigt sie an). Einzelne Kacheln werden bei Änderungen
    // nachgeführt; Apply erfolgt gesammelt einmal pro Frame.
    public sealed class WorldColorMap : IDisposable
    {
        readonly IWorldQuery _world;
        readonly Color32[] _pixels;
        bool _dirty;

        public Texture2D Texture { get; }

        public WorldColorMap(IWorldQuery world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _pixels = new Color32[world.Width * world.Height];
            Texture = new Texture2D(world.Width, world.Height, TextureFormat.RGBA32, false)
            {
                name = "WorldColorMap",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 4,
            };
            for (int y = 0; y < world.Height; y++)
            {
                for (int x = 0; x < world.Width; x++)
                    _pixels[y * world.Width + x] = ColorOf(x, y);
            }
            Texture.SetPixels32(_pixels);
            Texture.Apply(false, false);
        }

        public void UpdateTile(int x, int y)
        {
            if (!_world.InBounds(x, y))
                return;
            _pixels[y * _world.Width + x] = ColorOf(x, y);
            _dirty = true;
        }

        // Einmal pro Frame aufrufen; lädt nur hoch, wenn sich etwas geändert hat.
        public void ApplyIfDirty()
        {
            if (!_dirty)
                return;
            Texture.SetPixels32(_pixels);
            Texture.Apply(false, false);
            _dirty = false;
        }

        public Color32 ColorOf(int x, int y)
        {
            TileData t = _world.Get(x, y);
            Color32 c;
            if (t.WaterLevel > t.Height + 0.05f)
            {
                // Wasserfarbe nach Tiefe: flach türkis, tief kräftig blau (WorldBox-Lesbarkeit).
                float depth = t.WaterLevel - t.Height;
                float k = Mathf.Clamp01((depth - 0.5f) / 6f);
                c = Color32.Lerp(WorldPalette.ShallowWater, WorldPalette.DeepWater, k);
                if ((t.Flags & TileFlags.Road) != 0)
                    c = WorldPalette.Road;
                return c;
            }

            switch (t.Terrain)
            {
                case TerrainType.Beach: c = WorldPalette.Sand; break;
                case TerrainType.Forest: c = t.Biome == BiomeType.Boreal || t.Biome == BiomeType.Alpine ? WorldPalette.Boreal : WorldPalette.Forest; break;
                case TerrainType.Rock: c = WorldPalette.Rock; break;
                case TerrainType.Mountain: c = WorldPalette.Mountain; break;
                case TerrainType.Snow: c = WorldPalette.Snow; break;
                case TerrainType.Desert: c = WorldPalette.Desert; break;
                case TerrainType.Swamp: c = WorldPalette.Swamp; break;
                case TerrainType.River: c = WorldPalette.ShallowWater; break;
                case TerrainType.DeepWater: c = WorldPalette.DeepWater; break;
                case TerrainType.ShallowWater: c = WorldPalette.ShallowWater; break;
                default:
                    c = t.Biome switch
                    {
                        BiomeType.Savanna => WorldPalette.Savanna,
                        BiomeType.Tundra => WorldPalette.Tundra,
                        BiomeType.Desert => WorldPalette.Desert,
                        BiomeType.Wetland => WorldPalette.Swamp,
                        _ => WorldPalette.Grass,
                    };
                    // Fruchtbarer Boden etwas satter, karger etwas heller.
                    c = Color32.Lerp(c, new Color32(0xA5, 0xB8, 0x5A, 255), (1f - t.Fertility / 255f) * 0.25f);
                    break;
            }

            if ((t.Flags & TileFlags.Road) != 0)
                c = WorldPalette.Road;
            if ((t.Flags & (TileFlags.Scorched | TileFlags.Crater)) != 0)
                c = Color32.Lerp(c, WorldPalette.Scorched, 0.65f);

            // Leichte, deterministische Farbvariation je Kachel für eine „gemalte“ Wirkung.
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)_world.Seed;
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            float jitter = 0.94f + (h & 0xFF) / 255f * 0.12f;
            return new Color32(Scale(c.r, jitter), Scale(c.g, jitter), Scale(c.b, jitter), 255);
        }

        static byte Scale(byte v, float f) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * f), 0, 255);

        public void Dispose()
        {
            if (Texture != null)
                UnityObjects.Destroy(Texture);
        }
    }
}
