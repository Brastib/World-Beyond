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
    // Sandbox-Nachweis (INTEGRATION.md §0 Regel 4) für die Welt: Ohne einen einzigen Spieler-Command laufen
    // Weltgenerierung, Startplatzwahl, Wegfindung und Bewegung zuverlässig. Die Bewegungsziele setzt hier ein
    // Platzhalter für die spätere Civ-KI (WanderSystem), nie der Spieler.
    public class WorldAutonomyTests
    {
        // Platzhalter für Civ-/Militär-KI: gibt jeder Einheit ohne Ziel periodisch ein neues Ziel in der Nähe.
        sealed class WanderSystem : ISimulationSystem
        {
            readonly IWorldQuery _world;
            readonly List<EntityId> _ids = new List<EntityId>();
            public int Orders;

            public WanderSystem(IWorldQuery world) => _world = world;
            public string Name => "TestWander";
            public int Order => SystemOrder.AI;
            public int TickInterval => 20;

            public void Tick(SimContext ctx)
            {
                ctx.Units.CopyAliveIds(_ids);
                foreach (EntityId id in _ids)
                {
                    if (!ctx.Units.TryGet(id, out UnitView v) || v.HasMoveTarget)
                        continue;
                    float tx = v.X + ctx.Random.Range(-80f, 80f);
                    float tz = v.Z + ctx.Random.Range(-80f, 80f);
                    ctx.Units.SetMoveTarget(id, tx, tz);
                    Orders++;
                }
            }
        }

        sealed class Run
        {
            public int Orders, Arrived, Failed, CommandsSeen, OffTerrainSamples;
            public ulong Hash;
        }

        static Run Simulate(ulong seed, int ticks)
        {
            UnitTypeId foot = default, tank = default;
            var comp = GameComposition.Create(new GameStartSettings { Seed = seed, WorldWidth = 128, WorldHeight = 128 }, ctx =>
            {
                foot = ctx.UnitTypes.Register(new UnitTypeData { Key = "unit.test_walker", Movement = MovementClass.Foot, MaxSpeed = 1.5f, TurnRateDeg = 360f, Acceleration = 8f });
                tank = ctx.UnitTypes.Register(new UnitTypeData { Key = "unit.test_tracked", Movement = MovementClass.Tracked, MaxSpeed = 6f, TurnRateDeg = 45f, Acceleration = 2f, Radius = 3f });
            });
            var ctx = comp.Context;
            var world = ctx.Get<IWorldQuery>();
            var run = new Run();

            // Startplätze kommen automatisch aus der Welt; zwei Zivilisationen müssen sich zu Fuß erreichen können.
            var spots = new List<TileScore>();
            Assert.AreEqual(2, world.FindStartLocations(2, 20, spots), "Seed {0}: keine zwei Startplätze", seed);
            Assert.IsTrue(ctx.Get<IPathfinder>().AreConnected(spots[0].X, spots[0].Y, spots[1].X, spots[1].Y, MovementClass.Foot),
                "Seed {0}: Startplätze liegen auf getrennten Landstücken", seed);

            for (int civ = 0; civ < 2; civ++)
            {
                var p = WorldCoords.TileToWorld(spots[civ].X, spots[civ].Y);
                for (int i = 0; i < 10; i++)
                    ctx.Units.Spawn(i < 8 ? foot : tank, civ + 1, p.X + (i % 4) * 2f, p.Z + (i / 4) * 2f);
            }

            var wander = new WanderSystem(world);
            comp.Runner.Register(wander);
            ctx.Events.Subscribe<UnitArrivedEvent>(_ => run.Arrived++);
            ctx.Events.Subscribe<UnitMoveFailedEvent>(_ => run.Failed++);

            var ids = new List<EntityId>();
            for (int t = 0; t < ticks; t++)
            {
                run.CommandsSeen += ctx.Commands.PendingCount;
                comp.Runner.StepOnce();
                if (t % 50 != 0)
                    continue;
                ctx.Units.CopyAliveIds(ids);
                foreach (EntityId id in ids)
                {
                    ctx.Units.TryGet(id, out UnitView v);
                    ctx.UnitTypes.TryGet(v.Type, out UnitTypeData type);
                    var (tx, ty) = WorldCoords.WorldToTile(v.X, v.Z);
                    if (!world.IsWalkable(tx, ty, type.Movement))
                        run.OffTerrainSamples++;
                }
            }

            run.Orders = wander.Orders;
            ctx.Units.CopyAliveIds(ids);
            ulong h = 1469598103934665603UL;
            foreach (EntityId id in ids)
            {
                ctx.Units.TryGet(id, out UnitView v);
                h = (h ^ (ulong)System.BitConverter.SingleToInt32Bits(v.X)) * 1099511628211UL;
                h = (h ^ (ulong)System.BitConverter.SingleToInt32Bits(v.Z)) * 1099511628211UL;
            }
            run.Hash = h;
            return run;
        }

        [Test]
        public void TenMinutes_WithoutPlayerCommands_UnitsKeepMoving([Values(1UL, 42UL, 2026UL)] ulong seed)
        {
            const int ticks = 10 * 60 * SimClock.TicksPerSecond;
            Run run = Simulate(seed, ticks);
            TestContext.WriteLine($"Seed {seed}: Ziele {run.Orders}, angekommen {run.Arrived}, fehlgeschlagen {run.Failed}");

            Assert.AreEqual(0, run.CommandsSeen, "Es darf kein Command beteiligt sein");
            Assert.Greater(run.Orders, 40, "KI-Platzhalter hat zu wenige Ziele vergeben");
            // Jedes Ziel endet in Ankunft oder sauberem Fehlschlag; höchstens die zuletzt vergebenen sind noch unterwegs.
            Assert.GreaterOrEqual(run.Arrived + run.Failed, run.Orders - 20, "Einheiten bleiben hängen");
            Assert.Greater(run.Arrived, run.Failed, "Mehr Fehlschläge als Ankünfte");
            Assert.AreEqual(0, run.OffTerrainSamples, "Einheit stand auf nicht begehbarem Gelände");
        }

        [Test]
        public void AutonomousRun_IsDeterministic()
        {
            Run a = Simulate(7, 1200);
            Run b = Simulate(7, 1200);
            Assert.AreEqual(a.Hash, b.Hash);
            Assert.AreEqual(a.Arrived, b.Arrived);
        }

        [Test]
        public void StartLocations_ShareOneLandmass_AcrossSeeds()
        {
            int connected = 0;
            for (ulong seed = 100; seed < 120; seed++)
            {
                var world = new WorldBeyond.Simulation.World.Generation.WorldGenerator()
                    .Generate(new WorldGenSettings { Seed = seed, Width = 128, Height = 128 });
                var spots = new List<TileScore>();
                Assert.AreEqual(2, world.FindStartLocations(2, 20, spots), "Seed {0}", seed);
                var regions = new NavRegions(world);
                if (regions.RegionOf(spots[0].X, spots[0].Y, MovementClass.Foot) == regions.RegionOf(spots[1].X, spots[1].Y, MovementClass.Foot))
                    connected++;
            }
            Assert.AreEqual(20, connected, "Alle 20 Seeds sollen zwei zu Fuß verbundene Startplätze liefern");
        }
    }
}
