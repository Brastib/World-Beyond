using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using WorldBeyond.Core;
using WorldBeyond.Simulation.Civilizations;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Tests.Civilizations
{
    // Nutzervorgabe 2026-11-03: Die Spiel-KI baut wie in einer echten Stadt. Keine Häuser ineinander, auf Feldern oder
    // auf Wegen; Wege bilden sich, wo die Leute laufen; Häuser um den Ortskern, Felder außen.
    public class CivCityLogicTests
    {
        [Test]
        public void Buildings_NeverOverlapEachOtherFieldsOrRoads([Values(1UL, 99UL)] ulong seed)
        {
            var r = CivRun.Create(seed);
            r.Years(120);
            TestContext.WriteLine(r.Describe());
            var world = r.Ctx.Get<IWorldQuery>();
            var buildings = r.Ctx.Get<IBuildingQuery>();
            var owner = new Dictionary<(int, int), EntityId>();
            var all = new List<BuildingView>();
            var ids = new List<EntityId>();
            foreach (int sid in r.Settlements.AllSettlementIds)
            {
                r.Settlements.CopyBuildings(sid, ids);
                foreach (var b in ids)
                {
                    Assert.IsTrue(buildings.TryGet(b, out var v));
                    all.Add(v);
                    for (int y = v.Y; y < v.Y + v.SizeY; y++)
                        for (int x = v.X; x < v.X + v.SizeX; x++)
                        {
                            Assert.IsFalse(owner.TryGetValue((x, y), out var other), $"{v.TypeKey} und {other} teilen Kachel ({x},{y})");
                            owner.Add((x, y), b);
                            Assert.AreEqual(b, world.Get(x, y).Building, $"Kachel ({x},{y}) gehört nicht zu {v.TypeKey}");
                            Assert.AreEqual(TileFlags.None, world.Get(x, y).Flags & TileFlags.Road, $"{v.TypeKey} steht auf einem Weg ({x},{y})");
                        }
                }
            }
            // Türen bleiben frei: keine Tür liegt unter einem Gebäude (auch nicht unter einem Feld).
            int doors = 0;
            foreach (var v in all)
            {
                if (v.TypeKey == CivContent.TownCenter || !v.HasEntrance) continue;
                Assert.IsFalse(owner.ContainsKey((v.EntranceX, v.EntranceY)), $"Tür von {v.TypeKey} ({v.EntranceX},{v.EntranceY}) ist zugebaut");
                doors++;
            }
            TestContext.WriteLine($"Gebäude: {all.Count}, Türen geprüft: {doors}");
            Assert.Greater(all.Count, 20);
            Assert.Greater(doors, 15);
        }

        [Test]
        public void Roads_FormWhereResidentsWalk()
        {
            var r = CivRun.Create(1);
            var world = r.Ctx.Get<IWorldQuery>();
            Assert.AreEqual(0, CountRoads(world), "Zu Beginn gibt es keine Wege");
            r.Years(10);
            int after10 = CountRoads(world);
            r.Years(30);
            int after40 = CountRoads(world);
            TestContext.WriteLine($"Wegkacheln nach 10 Jahren: {after10}, nach 40 Jahren: {after40}");
            Assert.Greater(after10, 3, "Nach 10 Jahren sind Trampelpfade zu Wegen geworden");
            Assert.GreaterOrEqual(after40, after10);

            // Wege liegen in den Orten (eigenes Gebiet) und Gebäude haben ihre Tür am Weg.
            int atRoad = 0;
            var buildings = r.Ctx.Get<IBuildingQuery>();
            var ids = new List<EntityId>();
            foreach (int sid in r.Settlements.AllSettlementIds)
            {
                r.Settlements.CopyBuildings(sid, ids);
                foreach (var b in ids)
                    if (buildings.TryGet(b, out var v) && v.HasEntrance && (world.Get(v.EntranceX, v.EntranceY).Flags & TileFlags.Road) != 0) atRoad++;
            }
            TestContext.WriteLine($"Gebäude mit Tür am Weg: {atRoad}");
            Assert.Greater(atRoad, 5);
        }

        [Test]
        public void Houses_ClusterAroundCore_FieldsLieOutside([Values(1UL, 99UL)] ulong seed)
        {
            var r = CivRun.Create(seed);
            r.Years(120);
            var buildings = r.Ctx.Get<IBuildingQuery>();
            var ids = new List<EntityId>();
            double houseSum = 0, farmSum = 0;
            int houses = 0, farms = 0, farmsInside = 0, farmsAtCore = 0;
            foreach (int sid in r.Settlements.AllSettlementIds)
            {
                var st = r.State.FindSettlement(sid);
                r.Settlements.CopyBuildings(sid, ids);
                foreach (var b in ids)
                {
                    buildings.TryGet(b, out var v);
                    int d = CivAISystem.RingDistance(st, v.X, v.Y, v.SizeX, v.SizeY);
                    if (v.TypeKey == "building.hut" || v.TypeKey == "building.house") { houseSum += d; houses++; }
                    else if (v.TypeKey == "building.farm") { farmSum += d; farms++; if (d < CivAISystem.FieldRingOf(st.Tier)) farmsInside++; if (d == 0) farmsAtCore++; }
                }
            }
            double hAvg = houseSum / houses, fAvg = farmSum / farms;
            TestContext.WriteLine($"Häuser {houses}, mittlerer Abstand zum Kern {hAvg:F1}; Felder {farms}, Abstand {fAvg:F1}, davon im Wohnring {farmsInside}, am Kern {farmsAtCore}");
            Assert.Greater(houses, 5);
            Assert.Greater(farms, 3);
            Assert.Less(hAvg, fAvg, "Häuser stehen näher am Kern als Felder");
            // Im Lager (kleines Gebiet) reicht ein Ring Hütten; diese alten Felder liegen später im Wohnring des Dorfes.
            Assert.AreEqual(0, farmsAtCore, "Kein Feld grenzt an den Ortskern");
            Assert.Less(farmsInside, farms / 2, "Die meisten Felder liegen außerhalb des Wohnrings");
        }

        [Test]
        public void Roads_AndTraffic_SurviveSaveLoad()
        {
            var a = CivRun.Create(21);
            a.Years(6);
            var b = CivRun.Create(999);
            CivSaveLoadTests.Transfer(a, b);
            a.Years(8);
            b.Years(8);
            Assert.AreEqual(RoadMap(a), RoadMap(b), "Wege entstehen nach dem Laden genauso weiter");
            Assert.AreEqual(a.Fingerprint(), b.Fingerprint());
        }

        static int CountRoads(IWorldQuery world)
        {
            int n = 0;
            for (int y = 0; y < world.Height; y++)
                for (int x = 0; x < world.Width; x++)
                    if ((world.Get(x, y).Flags & TileFlags.Road) != 0) n++;
            return n;
        }

        static string RoadMap(CivRun r)
        {
            var world = r.Ctx.Get<IWorldQuery>();
            var sb = new StringBuilder();
            for (int y = 0; y < world.Height; y++)
                for (int x = 0; x < world.Width; x++)
                    if ((world.Get(x, y).Flags & TileFlags.Road) != 0) sb.Append(x).Append(',').Append(y).Append(';');
            return sb.ToString();
        }
    }
}
