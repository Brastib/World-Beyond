using System;
using System.Collections.Generic;
using NUnit.Framework;
using WorldBeyond.Application;
using WorldBeyond.Core;
using WorldBeyond.Simulation;
using WorldBeyond.Simulation.Units;
using WorldBeyond.Simulation.World;
using WorldBeyond.Simulation.World.Navigation;

namespace WorldBeyond.Tests.World
{
    // M1.6: N Ticks, Speichern, Laden in eine frische Komposition, M Ticks → identisch zu N + M Ticks ohne Speichern.
    public class MovementSaveTests
    {
        static GameComposition Make()
        {
            return GameComposition.Create(new GameStartSettings { Seed = 4242, WorldWidth = 128, WorldHeight = 128, CivilizationCount = 0 }, ctx =>
            {
                ctx.UnitTypes.Register(new UnitTypeData { Key = "unit.test_walker", Movement = MovementClass.Foot, MaxSpeed = 1.5f, TurnRateDeg = 360f, Acceleration = 8f });
                ctx.UnitTypes.Register(new UnitTypeData { Key = "unit.test_tracked", Movement = MovementClass.Tracked, MaxSpeed = 6f, TurnRateDeg = 45f, Acceleration = 2f, Radius = 3f });
            });
        }

        static void Populate(GameComposition comp)
        {
            var ctx = comp.Context;
            var world = ctx.Get<IWorldQuery>();
            ctx.UnitTypes.TryGetByKey("unit.test_walker", out UnitTypeId walker);
            ctx.UnitTypes.TryGetByKey("unit.test_tracked", out UnitTypeId tracked);
            var spots = new List<TileScore>();
            Assume.That(world.FindStartLocations(2, 20, spots), Is.EqualTo(2));
            var a = WorldCoords.TileToWorld(spots[0].X, spots[0].Y);
            var b = WorldCoords.TileToWorld(spots[1].X, spots[1].Y);
            for (int i = 0; i < 60; i++)
            {
                var from = i % 2 == 0 ? a : b;
                var to = i % 2 == 0 ? b : a;
                var id = ctx.Units.Spawn(i % 10 == 9 ? tracked : walker, 1 + i % 2, from.X + (i % 6) * 1.2f, from.Z + (i / 6 % 5) * 1.2f);
                // Ziel teils in einer anderen Kachel um den Gegenpunkt, damit viele verschiedene Pfade entstehen.
                ctx.Units.SetMoveTarget(id, to.X + (i % 7) * 3f, to.Z + (i % 5) * 3f);
            }
        }

        static List<string> Snapshot(GameComposition comp)
        {
            var ctx = comp.Context;
            var ids = new List<EntityId>();
            ctx.Units.CopyAliveIds(ids);
            var result = new List<string>();
            foreach (EntityId id in ids)
            {
                ctx.Units.TryGet(id, out UnitView v);
                result.Add($"{id} {BitConverter.SingleToInt32Bits(v.X)} {BitConverter.SingleToInt32Bits(v.Z)} {BitConverter.SingleToInt32Bits(v.Heading)} {BitConverter.SingleToInt32Bits(v.Speed)} {v.State} {v.HasMoveTarget}");
            }
            return result;
        }

        static void SaveLoad(GameComposition from, GameComposition into)
        {
            var captured = new Dictionary<string, object>();
            foreach (var s in from.Context.Saveables.InLoadOrder())
                captured[s.SaveKey] = s.CaptureState();
            foreach (var s in into.Context.Saveables.InLoadOrder())
                Assert.IsTrue(s.TryRestoreState(captured[s.SaveKey], out string err), err);
            var report = new LoadReport();
            foreach (var s in into.Context.Saveables.InLoadOrder())
                (s as ISaveReferenceCheck)?.CheckReferences(into.Context, report);
            Assert.IsFalse(report.HasErrors);
        }

        [TestCase(3, 600)]     // mitten in laufenden Pfadsuchen gespeichert
        [TestCase(60, 900)]    // Einheiten unterwegs, Pfade fertig
        public void SaveLoad_ContinuesIdentically(int before, int after)
        {
            var continuous = Make();
            Populate(continuous);
            for (int i = 0; i < before; i++)
                continuous.Runner.StepOnce();
            if (before < 10)
                Assume.That(((GridPathfinder)continuous.Context.Get<IPathfinder>()).PendingCount, Is.GreaterThan(1), "Test soll mitten in Suchen speichern");

            var loaded = Make();
            SaveLoad(continuous, loaded);
            CollectionAssert.AreEqual(Snapshot(continuous), Snapshot(loaded), "Direkt nach dem Laden");

            for (int i = 0; i < after; i++)
            {
                continuous.Runner.StepOnce();
                loaded.Runner.StepOnce();
                if (i % 50 == 0 || i == after - 1)
                    CollectionAssert.AreEqual(Snapshot(continuous), Snapshot(loaded), $"Abweichung {i + 1} Ticks nach dem Laden");
            }
        }

        [Test]
        public void CorruptMovementState_IsRejected()
        {
            var comp = Make();
            Populate(comp);
            comp.Runner.StepOnce();
            Assert.IsTrue(comp.Context.Saveables.TryGet(MovementSystem.Key, out ISaveable msSaveable));
            var ms = (MovementSystem)msSaveable;
            var st = (MovementState)ms.CaptureState();
            Assume.That(st.Agents.Length, Is.GreaterThan(0));
            st.Agents[0].Waypoints = new[] { 1, 2, 3 };
            Assert.IsTrue(Make().Context.Saveables.TryGet(MovementSystem.Key, out ISaveable fresh));
            Assert.IsFalse(fresh.TryRestoreState(st, out _));
            Assert.IsFalse(ms.TryRestoreState("falsch", out _));
        }

        [Test]
        public void BlockedGoal_IsMovedToNeighbour_AndUnitArrivesNextToIt()
        {
            var comp = Make();
            var ctx = comp.Context;
            var world = (WorldGrid)ctx.Get<IWorldGrid>();
            ctx.UnitTypes.TryGetByKey("unit.test_walker", out UnitTypeId walker);
            var spots = new List<TileScore>();
            Assume.That(world.FindStartLocations(2, 10, spots), Is.EqualTo(2));
            int gx = spots[1].X, gy = spots[1].Y;
            Assume.That(world.SetBuilding(gx, gy, new EntityId(77, 1)));
            Assume.That(world.IsWalkable(gx, gy, MovementClass.Foot), Is.False);
            var start = WorldCoords.TileToWorld(spots[0].X, spots[0].Y);
            var goal = WorldCoords.TileToWorld(gx, gy);
            var id = ctx.Units.Spawn(walker, 1, start.X, start.Z);
            ctx.Units.SetMoveTarget(id, goal.X, goal.Z);
            int arrived = 0, failed = 0;
            ctx.Events.Subscribe<UnitArrivedEvent>(_ => arrived++);
            ctx.Events.Subscribe<UnitMoveFailedEvent>(_ => failed++);
            for (int i = 0; i < 20000 && arrived + failed == 0; i++)
                comp.Runner.StepOnce();
            Assert.AreEqual(1, arrived);
            ctx.Units.TryGet(id, out UnitView v);
            var (tx, ty) = WorldCoords.WorldToTile(v.X, v.Z);
            Assert.LessOrEqual(Math.Max(Math.Abs(tx - gx), Math.Abs(ty - gy)), 2, "Einheit steht neben dem Gebäude");
            Assert.IsTrue(world.IsWalkable(tx, ty, MovementClass.Foot));
        }
    }
}
