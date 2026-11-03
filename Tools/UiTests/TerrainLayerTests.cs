using NUnit.Framework;
using WorldBeyond.Application;
using WorldBeyond.Presentation.WorldView;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.UiTests
{
    // Zuordnung Kachel → Bodentextur (TerrainSplat). Der Shader selbst ist nur im Unity-Lauf prüfbar.
    public class TerrainLayerTests
    {
        [Test]
        public void EveryTileGetsExactlyOneLayer_AndUsageOverridesGround()
        {
            var c = GameComposition.Create(new GameStartSettings { Seed = 7, WorldWidth = 128, WorldHeight = 128, CivilizationCount = 2 });
            for (int i = 0; i < 200; i++) c.Runner.StepOnce();
            var world = c.Context.Get<IWorldQuery>();
            int w = world.Width;
            var ctrl = new[] { new byte[w * world.Height * 4], new byte[w * world.Height * 4], new byte[w * world.Height * 4] };
            var seen = new bool[TerrainLayers.Count];
            int buildings = 0;
            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < w; x++)
            {
                var t = world.Get(x, y);
                var layer = TerrainLayers.LayerFor(t);
                seen[(int)layer] = true;
                if (t.Building.IsValid && (t.Flags & TileFlags.Road) == 0) { buildings++; Assert.AreEqual(GroundLayer.Earth, layer); }
                if ((t.Flags & TileFlags.Road) != 0) Assert.AreEqual(GroundLayer.Path, layer);
                TerrainLayers.Write(ctrl, w, x, y, layer);
                int sum = 0, p = (y * w + x) * 4;
                for (int m = 0; m < 3; m++) for (int k = 0; k < 4; k++) sum += ctrl[m][p + k];
                Assert.AreEqual(255, sum, "genau eine Schicht je Kachel");
                Assert.AreEqual(255, ctrl[(int)layer / 4][p + (int)layer % 4]);
            }
            Assert.Greater(buildings, 0);
            Assert.IsTrue(seen[(int)GroundLayer.Grass] && seen[(int)GroundLayer.Sand], "Gras und Sand müssen in einer Startwelt vorkommen");
            Assert.AreEqual(TerrainLayers.Count, TerrainLayers.Names.Length);
            Assert.AreEqual(TerrainLayers.Count, TerrainLayers.RepeatMeters.Length);
        }
    }
}
