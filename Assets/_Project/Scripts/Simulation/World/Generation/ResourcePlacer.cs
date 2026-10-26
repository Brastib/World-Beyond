using System;
using System.Collections.Generic;
using WorldBeyond.Core;

namespace WorldBeyond.Simulation.World.Generation
{
    // Fruchtbarkeit und Rohstoffvorkommen. Verteilung folgt dem Gelände:
    // Holz in Wäldern, Stein an Fels und Hängen, Erz und Gold geclustert im Bergland, Öl in Wüste, Sumpf und an Küsten,
    // Wildnahrung verstreut im Grasland. Danach Mindestgarantie je größerer Landmasse.
    internal static class ResourcePlacer
    {
        public const int GuaranteeMinLandmassTiles = 2000;

        public static void Run(WorldGrid grid, Climate climate)
        {
            var s = grid.GenSettings;
            int w = grid.Width, h = grid.Height;
            float rich = s.ResourceRichness;
            ulong seedR = HashNoise.DeriveSeed(s.Seed, WorldGenerator.StepResources);
            ulong seedOre = HashNoise.DeriveSeed(s.Seed, WorldGenerator.StepOre);
            ulong seedOil = HashNoise.DeriveSeed(s.Seed, WorldGenerator.StepOil);
            ulong seedGold = HashNoise.DeriveSeed(s.Seed, WorldGenerator.StepGold);
            ulong seedFood = HashNoise.DeriveSeed(s.Seed, WorldGenerator.StepWildFood);
            ulong seedAmt = HashNoise.DeriveSeed(s.Seed, WorldGenerator.StepAmounts);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    ref TileData t = ref grid.RawRef(i);
                    t.Fertility = ComputeFertility(in t, climate.Temperature[i]);
                    t.Resource = ResourceType.None;
                    t.ResourceAmount = 0;
                    if (rich <= 0f)
                        continue;

                    float amt = HashNoise.Value01(seedAmt, x, y);
                    switch (t.Terrain)
                    {
                        case TerrainType.Forest:
                            Put(ref t, ResourceType.Wood, (200f + amt * 300f) * rich);
                            break;

                        case TerrainType.Rock:
                        case TerrainType.Grass:
                        case TerrainType.Snow:
                        case TerrainType.Desert:
                        {
                            float alt = climate.Altitude[i];
                            float slope = climate.SlopeTan[i];
                            float ore = HashNoise.Fbm(seedOre, x / 11f, y / 11f, 3) * 0.5f + 0.5f;
                            float gold = HashNoise.Fbm(seedGold, x / 9f, y / 9f, 3) * 0.5f + 0.5f;
                            bool upland = alt > 0.2f || slope > 0.35f;
                            if (upland && gold > 0.83f && alt > 0.3f)
                                Put(ref t, ResourceType.Gold, (120f + amt * 180f) * rich);
                            else if (upland && ore > 0.74f)
                                Put(ref t, ResourceType.Ore, (400f + amt * 600f) * rich);
                            else if (t.Terrain == TerrainType.Rock && HashNoise.Value01(seedR, x, y) < 0.45f)
                                Put(ref t, ResourceType.Stone, (500f + amt * 700f) * rich);
                            else if (t.Terrain == TerrainType.Grass && slope > 0.3f && HashNoise.Value01(seedR, x, y) < 0.08f)
                                Put(ref t, ResourceType.Stone, (300f + amt * 400f) * rich);
                            else if (t.Terrain == TerrainType.Desert && OilAt(seedOil, x, y))
                                Put(ref t, ResourceType.Oil, (600f + amt * 900f) * rich);
                            else if (t.Terrain == TerrainType.Grass && t.Fertility > 150 && HashNoise.Value01(seedFood, x, y) < 0.035f)
                                Put(ref t, ResourceType.Food, (150f + amt * 150f) * rich);
                            break;
                        }

                        case TerrainType.Swamp:
                        case TerrainType.Beach:
                            if (OilAt(seedOil, x, y))
                                Put(ref t, ResourceType.Oil, (500f + amt * 700f) * rich);
                            break;
                    }
                }
            }

            EnsureMinimumResources(grid);
        }

        static bool OilAt(ulong seedOil, int x, int y) => HashNoise.Fbm(seedOil, x / 14f, y / 14f, 3) * 0.5f + 0.5f > 0.76f;

        static void Put(ref TileData t, ResourceType type, float amount)
        {
            int a = (int)(amount + 0.5f);
            if (a <= 0)
                return;
            t.Resource = type;
            t.ResourceAmount = (ushort)Math.Min(a, ushort.MaxValue);
        }

        static byte ComputeFertility(in TileData t, float temperature)
        {
            float f;
            switch (t.Terrain)
            {
                case TerrainType.Grass: f = 110f + t.Moisture * 0.45f; break;
                case TerrainType.Forest: f = 70f; break;
                case TerrainType.Swamp: f = 60f; break;
                case TerrainType.Beach: f = 30f; break;
                case TerrainType.Desert: f = 12f; break;
                case TerrainType.Rock: f = 8f; break;
                default: f = 0f; break;
            }
            if (t.Biome == BiomeType.Savanna) f *= 0.8f;
            if (t.Biome == BiomeType.Boreal) f *= 0.75f;
            if (t.Biome == BiomeType.Tundra) f *= 0.35f;
            if ((t.Flags & TileFlags.RiverBank) != 0 && f > 0f) f += 45f;
            if (temperature < 0.25f) f *= 0.7f;
            return (byte)Math.Max(0f, Math.Min(255f, f));
        }

        // Jede Landmasse ab GuaranteeMinLandmassTiles Kacheln bekommt erreichbares Holz, Stein und Erz,
        // damit eine dort startende Zivilisation nicht dauerhaft blockiert ist.
        static void EnsureMinimumResources(WorldGrid grid)
        {
            int w = grid.Width, h = grid.Height, n = w * h;
            int[] label = new int[n];
            var tiles = new List<int>();
            var stack = new Stack<int>();
            int next = 0;
            for (int start = 0; start < n; start++)
            {
                if (label[start] != 0 || !grid.IsWalkable(start % w, start / w, MovementClass.Foot))
                    continue;
                next++;
                tiles.Clear();
                stack.Push(start);
                label[start] = next;
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    tiles.Add(i);
                    int x = i % w, y = i / w;
                    Visit(x - 1, y);
                    Visit(x + 1, y);
                    Visit(x, y - 1);
                    Visit(x, y + 1);
                }

                void Visit(int x, int y)
                {
                    if ((uint)x >= (uint)w || (uint)y >= (uint)h)
                        return;
                    int j = y * w + x;
                    if (label[j] != 0 || !grid.IsWalkable(x, y, MovementClass.Foot))
                        return;
                    label[j] = next;
                    stack.Push(j);
                }

                if (tiles.Count < GuaranteeMinLandmassTiles)
                    continue;

                tiles.Sort();
                EnsureResource(grid, tiles, ResourceType.Wood);
                EnsureResource(grid, tiles, ResourceType.Stone);
                EnsureResource(grid, tiles, ResourceType.Ore);
            }
        }

        static void EnsureResource(WorldGrid grid, List<int> tiles, ResourceType type)
        {
            foreach (int i in tiles)
            {
                if (grid.RawTiles[i].Resource == type)
                    return;
            }

            // Höchste geeignete Kachel (Erz/Stein) bzw. feuchteste Graskachel (Holz) als Zentrum eines 3×3-Vorkommens.
            int best = -1;
            float bestScore = float.MinValue;
            foreach (int i in tiles)
            {
                ref readonly TileData t = ref grid.RawTiles[i];
                if (t.Resource != ResourceType.None || t.Terrain == TerrainType.River || t.Building.IsValid)
                    continue;
                float score = type == ResourceType.Wood ? t.Moisture : t.Height;
                if (type == ResourceType.Wood && t.Terrain != TerrainType.Grass)
                    continue;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            if (best < 0)
                return;

            int w = grid.Width, h = grid.Height;
            int cx = best % w, cy = best / w;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if ((uint)x >= (uint)w || (uint)y >= (uint)h)
                        continue;
                    ref TileData t = ref grid.RawRef(y * w + x);
                    if (Navigation.MovementRules.IsWaterTerrain(t.Terrain) || t.Resource != ResourceType.None || t.Terrain == TerrainType.Mountain)
                        continue;
                    if (type == ResourceType.Wood)
                    {
                        if (t.Terrain != TerrainType.Grass)
                            continue;
                        t.Terrain = TerrainType.Forest;
                        Put(ref t, type, 350f);
                    }
                    else
                    {
                        Put(ref t, type, type == ResourceType.Ore ? 600f : 700f);
                    }
                }
            }
        }

    }
}
