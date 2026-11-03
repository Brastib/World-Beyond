using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WorldBeyond.Application;
using WorldBeyond.Core;
using WorldBeyond.Presentation.WorldObjects;
using WorldBeyond.Simulation.Civilizations;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.UiTests
{
    // Unity-freie Teile der Welt-Objekt-Darstellung gegen die echte GameComposition. Die Zeichnung selbst
    // (WorldObjectsRenderer) ist nur im Unity-Lauf prüfbar.
    public class WorldObjectsTests
    {
        private static GameComposition NewGame(ulong seed = 7) =>
            GameComposition.Create(new GameStartSettings { Seed = seed, WorldWidth = 128, WorldHeight = 128, CivilizationCount = 2 });

        private static void Run(GameComposition c, int ticks)
        {
            for (int i = 0; i < ticks; i++) c.Runner.StepOnce();
        }

        [Test]
        public void BuildingVisuals_ListsEveryAliveBuilding_WithCivAndShape()
        {
            var c = NewGame();
            var vis = new BuildingVisuals();
            vis.Attach(c.Context);
            Run(c, CivTime.TicksPerYear);
            Assert.IsTrue(vis.Refresh());

            var query = c.Context.Get<IBuildingQuery>();
            var settlements = c.Context.Get<ISettlementQuery>();
            var ids = new List<EntityId>();
            query.CopyAliveIds(ids);
            Assert.Greater(ids.Count, 0, "nach einem Spieljahr muss es Gebäude geben");
            Assert.AreEqual(ids.Count, vis.Items.Count);
            foreach (var item in vis.Items)
            {
                Assert.IsTrue(query.TryGet(item.Id, out var v));
                Assert.AreEqual(settlements.Get(v.SettlementId).CivId, item.CivId);
                Assert.AreEqual(BuildingVisuals.ShapeFor(v.TypeKey), item.Shape);
                Assert.AreEqual(v.Completed ? BuildingState.Complete : BuildingState.UnderConstruction, item.State);
                Assert.That(item.Height01, Is.InRange(BuildingVisuals.MinConstructionHeight, 1f));
            }
            Assert.IsTrue(vis.Items.Any(i => i.Shape == BuildingShape.Center), "Ortszentrum fehlt");
            int version = vis.Version;
            Assert.IsFalse(vis.Refresh() && !vis.HasConstruction, "ohne Änderung keine neue Version");
            Assert.GreaterOrEqual(vis.Version, version);
        }

        [Test]
        public void BuildingVisuals_ShapeMapping_CoversKnownKeys()
        {
            Assert.AreEqual(BuildingShape.Center, BuildingVisuals.ShapeFor("building.town_center"));
            Assert.AreEqual(BuildingShape.Field, BuildingVisuals.ShapeFor("building.farm"));
            Assert.AreEqual(BuildingShape.Wall, BuildingVisuals.ShapeFor("building.stone_wall"));
            Assert.AreEqual(BuildingShape.Barracks, BuildingVisuals.ShapeFor("building.barracks"));
            Assert.AreEqual(BuildingShape.Storage, BuildingVisuals.ShapeFor("building.warehouse"));
            Assert.AreEqual(BuildingShape.Workshop, BuildingVisuals.ShapeFor("building.smelter"));
            Assert.AreEqual(BuildingShape.Workshop, BuildingVisuals.ShapeFor("building.unbekannt"));
        }

        [Test]
        public void BuildingVisuals_Capture_ChangesCivOfBuildings()
        {
            var c = NewGame();
            var vis = new BuildingVisuals();
            vis.Attach(c.Context);
            Run(c, 200);
            var civs = c.Context.Get<ICivilizationQuery>();
            int a = civs.AllCivIds[0], b = civs.AllCivIds[1];
            int capitalB = civs.Get(b).CapitalSettlementId;
            c.Context.Get<ISettlementRegistry>().TransferSettlement(capitalB, a);
            Run(c, 1);
            Assert.IsTrue(vis.Refresh());
            var query = c.Context.Get<IBuildingQuery>();
            var inCapital = vis.Items.Where(i => query.TryGet(i.Id, out var v) && v.SettlementId == capitalB).ToList();
            Assert.Greater(inCapital.Count, 0);
            Assert.IsTrue(inCapital.All(i => i.CivId == a), "eroberte Gebäude zeigen die neue Civ");
        }

        [Test]
        public void UnitInterpolator_KeepsPreviousTick_AndLerpsBetween()
        {
            var c = NewGame();
            var ids = new List<EntityId>();
            for (int i = 0; i < 3 * CivTime.TicksPerYear && ids.Count == 0; i += 50)
            {
                Run(c, 50);
                c.Context.Units.CopyAliveIds(ids);
            }
            Assert.Greater(ids.Count, 0, "keine Einheit nach 3 Spieljahren");

            var interp = new UnitInterpolator();
            interp.Capture(c.Context.Units, c.Context.UnitTypes, c.Context.Clock.Tick);
            Assert.AreEqual(ids.Count, interp.Samples.Count);
            var first = interp.Samples[0];
            Assert.AreEqual(first.X, first.PrevX, "neue Einheit: kein Sprung");

            // Mehrere Ticks laufen lassen, bis sich eine Einheit bewegt hat.
            UnitSample moved = default;
            bool found = false;
            for (int t = 0; t < 600 && !found; t++)
            {
                Run(c, 1);
                interp.Capture(c.Context.Units, c.Context.UnitTypes, c.Context.Clock.Tick);
                foreach (var s in interp.Samples)
                {
                    if (System.Math.Abs(s.X - s.PrevX) + System.Math.Abs(s.Z - s.PrevZ) > 0.01f) { moved = s; found = true; break; }
                }
            }
            Assert.IsTrue(found, "keine Einheit hat sich in 600 Ticks bewegt");
            UnitInterpolator.Lerp(moved, 0f, out float x0, out float z0, out _);
            UnitInterpolator.Lerp(moved, 1f, out float x1, out float z1, out _);
            UnitInterpolator.Lerp(moved, 0.5f, out float xm, out float zm, out _);
            Assert.AreEqual(moved.PrevX, x0, 1e-4); Assert.AreEqual(moved.PrevZ, z0, 1e-4);
            Assert.AreEqual(moved.X, x1, 1e-4); Assert.AreEqual(moved.Z, z1, 1e-4);
            Assert.AreEqual((moved.PrevX + moved.X) * 0.5f, xm, 1e-4);

            // Kopfrichtung über die 0/360-Grenze auf kürzestem Weg.
            var turn = new UnitSample { PrevHeading = 350f, Heading = 10f };
            UnitInterpolator.Lerp(turn, 0.5f, out _, out _, out float h);
            Assert.AreEqual(360f, h, 1e-3);

            // Entfernte Einheit verschwindet beim nächsten Capture.
            var victim = interp.Samples[0].Id;
            c.Context.Units.Despawn(victim);
            Run(c, 1);
            interp.Capture(c.Context.Units, c.Context.UnitTypes, c.Context.Clock.Tick);
            Assert.IsFalse(interp.Samples.Any(s => s.Id.Equals(victim)));
        }

        [Test]
        public void TerritoryGrid_MatchesWorldOwners_AndReportsBordersAndDirtyChunks()
        {
            var c = NewGame();
            Run(c, 100);
            var world = c.Context.Get<IWorldQuery>();
            var grid = new TerritoryGrid(world.Width, world.Height);
            var dirty = new List<int>();
            grid.Refresh(world, dirty);
            Assert.Greater(dirty.Count, 0, "erstes Lesen markiert besessene Chunks");

            int owned = 0, borders = 0;
            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                int o = world.Get(x, y).OwnerCivId;
                Assert.AreEqual(o, grid.OwnerAt(x, y));
                if (o == 0) continue;
                owned++;
                foreach (TerritoryGrid.Side side in System.Enum.GetValues(typeof(TerritoryGrid.Side)))
                {
                    if (!grid.IsBorder(x, y, side)) continue;
                    borders++;
                    int nx = x + (side == TerritoryGrid.Side.East ? 1 : side == TerritoryGrid.Side.West ? -1 : 0);
                    int ny = y + (side == TerritoryGrid.Side.North ? 1 : side == TerritoryGrid.Side.South ? -1 : 0);
                    Assert.AreNotEqual(o, grid.OwnerAt(nx, ny));
                }
            }
            Assert.Greater(owned, 0);
            Assert.Greater(borders, 0);

            grid.Refresh(world, dirty);
            Assert.AreEqual(0, dirty.Count, "ohne Besitzwechsel keine geänderten Chunks");
        }
    }
}
