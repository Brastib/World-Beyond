using System;
using System.Collections.Generic;
using NUnit.Framework;
using WorldBeyond.Core;
using WorldBeyond.Simulation.World;
using WorldBeyond.Simulation.World.Generation;

namespace WorldBeyond.Tests.World
{
    public class WorldGenerationTests
    {
        internal static WorldGrid Generate(ulong seed, int size = 128)
        {
            return new WorldGenerator().Generate(new WorldGenSettings { Seed = seed, Width = size, Height = size });
        }

        static ulong Fingerprint(WorldGrid g)
        {
            // FNV-1a über alle Kachelfelder; Höhen als Bitmuster, damit jede Abweichung auffällt.
            ulong h = 1469598103934665603UL;
            void Mix(ulong v)
            {
                unchecked
                {
                    h ^= v;
                    h *= 1099511628211UL;
                }
            }
            for (int y = 0; y < g.Height; y++)
            {
                for (int x = 0; x < g.Width; x++)
                {
                    ref readonly TileData t = ref g.Get(x, y);
                    Mix((ulong)BitConverter.SingleToInt32Bits(t.Height));
                    Mix((ulong)BitConverter.SingleToInt32Bits(t.WaterLevel));
                    Mix((ulong)t.Terrain << 8 | (ulong)t.Biome);
                    Mix((ulong)t.Resource << 16 | t.ResourceAmount);
                    Mix((ulong)t.Fertility << 8 | t.Moisture);
                    Mix((ulong)t.Flags);
                }
            }
            return h;
        }

        [Test]
        public void SameSeed_ProducesIdenticalWorld()
        {
            Assert.AreEqual(Fingerprint(Generate(42)), Fingerprint(Generate(42)));
        }

        [Test]
        public void DifferentSeed_ProducesDifferentWorld()
        {
            Assert.AreNotEqual(Fingerprint(Generate(42)), Fingerprint(Generate(43)));
        }

        [Test]
        public void Border_IsAlwaysDeepWater()
        {
            var g = Generate(7);
            for (int i = 0; i < g.Width; i++)
            {
                for (int b = 0; b < WorldConstants.OceanBorder; b++)
                {
                    Assert.AreEqual(TerrainType.DeepWater, g.Get(i, b).Terrain);
                    Assert.AreEqual(TerrainType.DeepWater, g.Get(i, g.Height - 1 - b).Terrain);
                    Assert.AreEqual(TerrainType.DeepWater, g.Get(b, i).Terrain);
                    Assert.AreEqual(TerrainType.DeepWater, g.Get(g.Width - 1 - b, i).Terrain);
                }
            }
        }

        [TestCase(1UL)]
        [TestCase(99UL)]
        [TestCase(123456789UL)]
        public void LandRatio_IsCloseToSetting(ulong seed)
        {
            var g = Generate(seed, 192);
            int land = 0, ocean = 0;
            for (int y = 0; y < g.Height; y++)
            {
                for (int x = 0; x < g.Width; x++)
                {
                    var t = g.Get(x, y).Terrain;
                    if (t == TerrainType.DeepWater || t == TerrainType.ShallowWater)
                    {
                        if ((g.Get(x, y).Flags & TileFlags.Lake) == 0)
                            ocean++;
                    }
                    else
                    {
                        land++;
                    }
                }
            }
            float ratio = land / (float)(land + ocean);
            Assert.That(ratio, Is.InRange(0.45f, 0.62f), $"Landanteil {ratio:P1}");
        }

        [Test]
        public void TerrainVariety_PlainsHillsMountainsWaterForest()
        {
            var counts = new Dictionary<TerrainType, int>();
            var g = Generate(2024, 256);
            int river = 0, lake = 0;
            for (int y = 0; y < g.Height; y++)
            {
                for (int x = 0; x < g.Width; x++)
                {
                    ref readonly TileData t = ref g.Get(x, y);
                    counts.TryGetValue(t.Terrain, out int c);
                    counts[t.Terrain] = c + 1;
                    if (t.Terrain == TerrainType.River) river++;
                    if ((t.Flags & TileFlags.Lake) != 0) lake++;
                }
            }
            foreach (var t in new[] { TerrainType.DeepWater, TerrainType.ShallowWater, TerrainType.Beach, TerrainType.Grass, TerrainType.Forest, TerrainType.Rock })
                Assert.That(counts.ContainsKey(t) && counts[t] > 20, $"{t} kommt zu selten vor: {(counts.ContainsKey(t) ? counts[t] : 0)}");
            Assert.Greater(river, 50, "Flüsse fehlen");
            TestContext.WriteLine($"Flusskacheln {river}, Seekacheln {lake}");
            foreach (var kv in counts)
                TestContext.WriteLine($"{kv.Key}: {kv.Value}");
        }

        [Test]
        public void Rivers_FlowDownhillIntoWater()
        {
            var g = Generate(555, 192);
            // Jede Flusskachel hat einen Nachbarn (Fluss, See oder Meer) mit niedrigerer oder gleicher Wasseroberfläche.
            for (int y = 1; y < g.Height - 1; y++)
            {
                for (int x = 1; x < g.Width - 1; x++)
                {
                    ref readonly TileData t = ref g.Get(x, y);
                    if (t.Terrain != TerrainType.River)
                        continue;
                    Assert.Greater(t.WaterLevel, t.Height, $"Fluss ({x},{y}) ohne Wasser");
                    bool drains = false;
                    for (int d = 0; d < 4 && !drains; d++)
                    {
                        int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                        ref readonly TileData n = ref g.Get(nx, ny);
                        bool water = n.Terrain == TerrainType.River || n.Terrain == TerrainType.DeepWater || n.Terrain == TerrainType.ShallowWater;
                        drains = water && n.WaterLevel <= t.WaterLevel + 0.001f;
                    }
                    Assert.IsTrue(drains, $"Fluss ({x},{y}) hat keinen Abfluss");
                }
            }
        }

        [Test]
        public void LargeLandmasses_HaveWoodStoneAndOre()
        {
            var g = Generate(31337, 256);
            var found = new HashSet<ResourceType>();
            for (int y = 0; y < g.Height; y++)
                for (int x = 0; x < g.Width; x++)
                    found.Add(g.Get(x, y).Resource);
            Assert.IsTrue(found.Contains(ResourceType.Wood));
            Assert.IsTrue(found.Contains(ResourceType.Stone));
            Assert.IsTrue(found.Contains(ResourceType.Ore));
        }

        [Test]
        public void StartLocations_AreBuildableAndSpread()
        {
            var g = Generate(8, 256);
            var results = new List<TileScore>();
            int n = g.FindStartLocations(4, 40, results);
            Assert.GreaterOrEqual(n, 2, "Mindestens zwei Startplätze (M1: zwei Zivilisationen)");
            for (int i = 0; i < results.Count; i++)
            {
                Assert.IsTrue(g.IsBuildable(results[i].X, results[i].Y));
                Assert.IsTrue(g.IsWalkable(results[i].X, results[i].Y, MovementClass.Foot));
                for (int j = i + 1; j < results.Count; j++)
                {
                    int dx = results[i].X - results[j].X, dy = results[i].Y - results[j].Y;
                    Assert.GreaterOrEqual(dx * dx + dy * dy, 40 * 40);
                }
            }
        }

        [Test]
        public void SmallWorld_64_Generates()
        {
            var g = Generate(3, 64);
            Assert.AreEqual(64, g.Width);
            Assert.AreEqual(0, g.ChangedTileCount);
        }

        [Test]
        public void InvalidSettings_Throw()
        {
            Assert.Throws<ArgumentException>(() => new WorldGenerator().Generate(new WorldGenSettings { Width = 8, Height = 8 }));
            Assert.Throws<ArgumentException>(() => new WorldGenerator().Generate(new WorldGenSettings { LandRatio = 0.95f }));
        }

        [Test]
        public void Performance_256_Under3Seconds()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Generate(77, 256);
            sw.Stop();
            TestContext.WriteLine($"256×256: {sw.ElapsedMilliseconds} ms");
            Assert.Less(sw.ElapsedMilliseconds, 3000);
        }

        [Test]
        public void Performance_512_Under10Seconds()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Generate(78, 512);
            sw.Stop();
            TestContext.WriteLine($"512×512: {sw.ElapsedMilliseconds} ms");
            Assert.Less(sw.ElapsedMilliseconds, 10000);
        }
    }
}
