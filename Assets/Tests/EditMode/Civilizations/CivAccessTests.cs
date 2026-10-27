using System.Collections.Generic;
using NUnit.Framework;
using WorldBeyond.Core;
using WorldBeyond.Simulation.Civilizations;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Tests.Civilizations
{
    // Prüf-Befund (Seed 12345, 256²): eine Siedlung mauerte sich mit eigenen Gebäuden ein, Karawanen und Truppen
    // fanden keinen Weg mehr. Alle Siedlungen halten jetzt ein gemeinsames, weltweites Straßenraster frei.
    public class CivAccessTests
    {
        [Test]
        public void Settlements_NeverWallThemselvesIn([Values(1UL, 12345UL)] ulong seed)
        {
            var r = CivRun.Create(seed);
            r.Years(400);
            TestContext.WriteLine(r.Describe());
            var buildings = r.Ctx.Get<IBuildingQuery>();
            var pathfinder = r.Ctx.Get<IPathfinder>();
            var ids = new List<EntityId>();
            int checkedBuildings = 0;
            foreach (int sid in r.Settlements.AllSettlementIds)
            {
                var st = r.State.FindSettlement(sid);
                r.Settlements.CopyBuildings(sid, ids);
                foreach (var b in ids)
                {
                    Assert.IsTrue(buildings.TryGet(b, out var v));
                    if (v.TypeKey == CivContent.TownCenter) continue;
                    for (int y = v.Y; y < v.Y + v.SizeY; y++)
                        for (int x = v.X; x < v.X + v.SizeX; x++)
                            Assert.IsFalse(SettlementRegistry.IsStreet(x, y), $"{st.Name}: {v.TypeKey} steht auf der Straße ({x},{y})");
                    checkedBuildings++;
                }
                // Jede Siedlung ist von der Hauptstadt ihres Volkes zu Fuß erreichbar (Siedler sind ja hingelaufen).
                var capital = r.Settlements.Get(r.Civs.Get(st.CivId).CapitalSettlementId);
                var world = r.Ctx.Get<IWorldQuery>();
                var a = CivState.ArrivalTile(world, st.X, st.Y, MovementClass.Foot);
                var c = CivState.ArrivalTile(world, capital.X, capital.Y, MovementClass.Foot);
                Assert.IsTrue(a.x >= 0 && c.x >= 0, $"{st.Name}: kein Ankunftspunkt");
                Assert.IsTrue(pathfinder.AreConnected(a.x, a.y, c.x, c.y, MovementClass.Foot), $"{st.Name} ({st.X},{st.Y}) ist von {capital.Name} abgeschnitten");
            }
            TestContext.WriteLine($"Geprüfte Gebäude: {checkedBuildings}");
            Assert.Greater(checkedBuildings, 30);

            // Keine Einheit steht in einem Gebäude (Prüf-Befund: Bau auf Kacheln, auf denen Einheiten warteten).
            var blocked = new HashSet<(int, int)>();
            foreach (int sid in r.Settlements.AllSettlementIds)
            {
                r.Settlements.CopyBuildings(sid, ids);
                foreach (var b in ids)
                {
                    buildings.TryGet(b, out var v);
                    for (int y = v.Y; y < v.Y + v.SizeY; y++)
                        for (int x = v.X; x < v.X + v.SizeX; x++) blocked.Add((x, y));
                }
            }
            var units = new List<EntityId>();
            r.Ctx.Units.CopyAliveIds(units);
            int inside = 0;
            foreach (var u in units)
            {
                var uv = r.Ctx.Units.Get(u);
                if (blocked.Contains(WorldCoords.WorldToTile(uv.X, uv.Z))) inside++;
            }
            TestContext.WriteLine($"Einheiten: {units.Count}, davon in Gebäuden: {inside}");
            Assert.AreEqual(0, inside, "Einheiten stehen in Gebäuden");
        }
    }
}
