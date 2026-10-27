using System;
using NUnit.Framework;
using WorldBeyond.Simulation.World;
using WorldBeyond.Simulation.World.Generation;

namespace WorldBeyond.Tests.World
{
    public class BiomeTests
    {
        [Test]
        public void BiomeValues_AreStable()
        {
            // Nur anhängen, nie umnummerieren (ARCHITECTURE §11.1 Regel 4).
            Assert.AreEqual(8, (int)BiomeType.Alpine);
            Assert.AreEqual(9, (int)BiomeType.TropicalForest);
            Assert.AreEqual(10, (int)BiomeType.Mediterranean);
        }

        [Test]
        public void TropicalAndMediterranean_AppearWithFittingClimate()
        {
            int tropical = 0, tropicalForest = 0, mediterranean = 0;
            for (ulong seed = 1; seed <= 6; seed++)
            {
                var g = new WorldGenerator().Generate(new WorldGenSettings { Seed = seed, Width = 256, Height = 256 });
                for (int y = 0; y < g.Height; y++)
                {
                    for (int x = 0; x < g.Width; x++)
                    {
                        TileData t = g.Get(x, y);
                        if (t.Biome == BiomeType.TropicalForest)
                        {
                            tropical++;
                            if (t.Terrain == TerrainType.Forest) tropicalForest++;
                            Assert.AreNotEqual(TerrainType.Desert, t.Terrain);
                            Assert.AreNotEqual(TerrainType.Snow, t.Terrain);
                        }
                        else if (t.Biome == BiomeType.Mediterranean)
                        {
                            mediterranean++;
                            Assert.AreNotEqual(TerrainType.Snow, t.Terrain);
                        }
                    }
                }
            }
            TestContext.WriteLine($"6 Welten 256²: Tropenwald {tropical} Kacheln (davon Wald {tropicalForest}), Mittelmeer {mediterranean}");
            Assert.Greater(tropical, 200, "Tropenwald kommt praktisch nicht vor");
            Assert.Greater(mediterranean, 200, "Mittelmeer kommt praktisch nicht vor");
            Assert.Greater(tropicalForest, tropical / 3, "Tropenwald soll überwiegend bewaldet sein");
        }
    }
}
