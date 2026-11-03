using NUnit.Framework;
using WorldBeyond.Simulation.Civilizations;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Tests.Civilizations
{
    // Stadtbild (Nutzer 2026-11-03): Wege entstehen mit der Bebauung, nichts wird auf Wege gebaut.
    public class CivTownLayoutTests
    {
        [Test]
        public void Roads_FormNextToBuildings_AndStayFree()
        {
            var r = CivRun.Create(1);
            r.Years(40);
            var world = r.Ctx.Get<IWorldQuery>();
            int roads = 0, builtOnRoad = 0, roadOffStreet = 0, roadNextToBuilding = 0;
            for (int y = 0; y < world.Height; y++)
                for (int x = 0; x < world.Width; x++)
                {
                    ref readonly var t = ref world.Get(x, y);
                    if ((t.Flags & TileFlags.Road) == 0) continue;
                    roads++;
                    if (t.Building.IsValid) builtOnRoad++;
                    if (!SettlementRegistry.IsStreet(x, y)) roadOffStreet++;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                        if (world.InBounds(nx, ny) && world.Get(nx, ny).Building.IsValid) { roadNextToBuilding++; break; }
                    }
                }
            TestContext.WriteLine($"Wege {roads}, davon an Gebäuden {roadNextToBuilding}, bebaut {builtOnRoad}, neben Raster {roadOffStreet}");
            Assert.Greater(roads, 10, "Wege entstehen von selbst");
            Assert.AreEqual(0, builtOnRoad, "Kein Gebäude auf einem Weg");
            Assert.AreEqual(0, roadOffStreet, "Wege liegen auf dem Straßenraster");
            Assert.Greater(roadNextToBuilding, roads / 2, "Wege führen an Gebäuden entlang");
        }
    }
}
