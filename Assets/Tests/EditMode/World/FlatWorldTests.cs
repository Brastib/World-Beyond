using NUnit.Framework;
using WorldBeyond.Simulation.World;
using WorldBeyond.Simulation.World.Generation;

namespace WorldBeyond.Tests.World
{
    // Flache Welt (Relief klein): keine Berge, Land nur wenige Meter über dem Meer, trotzdem Land, Wasser und Wald.
    public class FlatWorldTests
    {
        [Test]
        public void FlatRelief_HasNoMountainsAndLowLand()
        {
            var s = new WorldGenSettings { Seed = 99, Width = 256, Height = 256, Relief = 0.08f };
            WorldGrid g = new WorldGenerator().Generate(s);
            int land = 0, forest = 0, high = 0;
            float maxLand = 0f;
            for (int y = 0; y < g.Height; y++)
                for (int x = 0; x < g.Width; x++)
                {
                    ref readonly TileData t = ref g.Get(x, y);
                    if (t.Terrain == TerrainType.DeepWater || t.Terrain == TerrainType.ShallowWater) continue;
                    land++;
                    if (t.Terrain == TerrainType.Forest) forest++;
                    if (t.Terrain == TerrainType.Mountain || t.Terrain == TerrainType.Snow) high++;
                    if (t.Height > maxLand) maxLand = t.Height;
                }
            TestContext.WriteLine($"Land {land}, Wald {forest}, Berg/Schnee {high}, max. Landhöhe {maxLand:F1} m");
            Assert.That(high, Is.EqualTo(0));
            Assert.That(maxLand, Is.LessThanOrEqualTo(s.SeaLevel + 10f));
            Assert.That(land, Is.GreaterThan(256 * 256 / 4));
            Assert.That(forest, Is.GreaterThan(0));
        }

        [Test]
        public void Relief_OutOfRange_IsRejected()
        {
            Assert.That(new WorldGenSettings { Relief = 0f }.Validate(out _), Is.False);
            Assert.That(new WorldGenSettings { Relief = 1f }.Validate(out _), Is.True);
        }
    }
}
