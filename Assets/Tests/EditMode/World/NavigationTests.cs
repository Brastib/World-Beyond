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
    public class PathfinderTests
    {
        static readonly List<(int x, int y)> Path = new List<(int x, int y)>();

        [Test]
        public void StraightLine_OnOpenGround_IsSmoothedToTwoPoints()
        {
            var g = TestWorlds.Flat();
            var pf = new GridPathfinder(g);
            Assert.AreEqual(PathStatus.Found, pf.FindPathImmediate(4, 4, 20, 12, MovementClass.Foot, Path));
            Assert.AreEqual((4, 4), Path[0]);
            Assert.AreEqual((20, 12), Path[Path.Count - 1]);
            Assert.AreEqual(2, Path.Count);
        }

        [Test]
        public void GoesAroundWall_AndNeverEntersBlockedTiles()
        {
            var g = TestWorlds.Flat();
            for (int y = 4; y < 26; y++)
                TestWorlds.Water(g, 15, y);
            var pf = new GridPathfinder(g);
            Assert.AreEqual(PathStatus.Found, pf.FindPathImmediate(5, 15, 25, 15, MovementClass.Foot, Path));
            AssertSegmentsWalkable(g, Path, MovementClass.Foot);
            bool aroundTop = false;
            foreach (var p in Path)
                if (p.y >= 26 || p.y < 4) aroundTop = true;
            Assert.IsTrue(aroundTop, "Pfad muss um die Wand herum führen");
        }

        [Test]
        public void Island_IsNotConnected_AndFailsWithoutSearch()
        {
            var g = TestWorlds.Flat();
            for (int y = 2; y < 30; y++)
                TestWorlds.Water(g, 16, y);
            var pf = new GridPathfinder(g);
            Assert.IsFalse(pf.AreConnected(5, 5, 25, 5, MovementClass.Foot));
            Assert.IsTrue(float.IsInfinity(pf.EstimateCost(5, 5, 25, 5, MovementClass.Foot)));
            long before = pf.TotalExpansions;
            Assert.AreEqual(PathStatus.NotFound, pf.FindPathImmediate(5, 5, 25, 5, MovementClass.Foot, Path));
            Assert.AreEqual(before, pf.TotalExpansions, "Unerreichbares Ziel darf keine Suche auslösen");
            Assert.AreEqual(PathStatus.Invalid, pf.FindPathImmediate(5, 5, 99, 5, MovementClass.Foot, Path));
        }

        [Test]
        public void Regions_UpdateAfterBridgeIsBuilt()
        {
            var g = TestWorlds.Flat();
            for (int y = 2; y < 30; y++)
                TestWorlds.Water(g, 16, y);
            var pf = new GridPathfinder(g);
            Assert.IsFalse(pf.AreConnected(5, 5, 25, 5, MovementClass.Wheeled));
            g.SetFlags(16, 10, TileFlags.Road, TileFlags.None);
            Assert.IsTrue(pf.AreConnected(5, 5, 25, 5, MovementClass.Wheeled));
            Assert.AreEqual(PathStatus.Found, pf.FindPathImmediate(5, 5, 25, 5, MovementClass.Wheeled, Path));
            CollectionAssert.Contains(Path, (16, 10));
        }

        [Test]
        public void Vehicles_AvoidForest_InfantryCrossesIt()
        {
            var g = TestWorlds.Flat();
            for (int y = 2; y < 30; y++)
                if (y != 28) g.SetTerrain(16, y, TerrainType.Forest);
            var pf = new GridPathfinder(g);
            Assert.AreEqual(PathStatus.Found, pf.FindPathImmediate(5, 10, 25, 10, MovementClass.Wheeled, Path));
            AssertSegmentsWalkable(g, Path, MovementClass.Wheeled);
            Assert.Greater(Path.Count, 2, "Fahrzeug muss den Wald über die Lücke bei y = 28 umfahren");
            Assert.AreEqual(PathStatus.Found, pf.FindPathImmediate(5, 10, 25, 10, MovementClass.Foot, Path));
            Assert.Less(Path.Count, 4, "Infanterie geht direkt durch den Wald");
        }

        [Test]
        public void Path_IsOptimal_OnSmallMap()
        {
            // Dijkstra-Vergleich: A*-Kosten (ungeglättet) = Dijkstra-Kosten
            var g = TestWorlds.Flat(32);
            var rnd = new Random(5);
            for (int i = 0; i < 80; i++)
                g.SetTerrain(rnd.Next(2, 30), rnd.Next(2, 30), i % 3 == 0 ? TerrainType.Forest : TerrainType.Rock);
            var pf = new GridPathfinder(g);
            Assert.AreEqual(PathStatus.Found, pf.FindPathImmediate(3, 3, 20, 19, MovementClass.Foot, Path));
            float smoothedCost = PathCost(g, Path, MovementClass.Foot);
            float best = Dijkstra(g, 3, 3, 20, 19, MovementClass.Foot);
            // Geglättete Linien dürfen nicht teurer sein als der optimale Rasterweg (+5 % Toleranz für Linienführung).
            Assert.LessOrEqual(smoothedCost, best * 1.05f + 0.01f);
        }

        [Test]
        public void Budget_SplitsSearchOverTicks()
        {
            var g = new Simulation.World.Generation.WorldGenerator().Generate(new WorldGenSettings { Seed = 11, Width = 128, Height = 128 });
            var starts = new List<TileScore>();
            Assert.GreaterOrEqual(g.FindStartLocations(2, 40, starts), 2);
            var pf = new GridPathfinder(g);
            if (!pf.AreConnected(starts[0].X, starts[0].Y, starts[1].X, starts[1].Y, MovementClass.Foot))
                Assert.Ignore("Startplätze auf verschiedenen Inseln");
            int id = pf.RequestPath(starts[0].X, starts[0].Y, starts[1].X, starts[1].Y, MovementClass.Foot);
            int ticks = 0;
            PathStatus status;
            do
            {
                pf.ProcessRequests(50);
                ticks++;
                status = pf.TryGetPath(id, Path);
            } while (status == PathStatus.Pending && ticks < 10000);
            Assert.AreEqual(PathStatus.Found, status);
            Assert.Greater(ticks, 1, "Suche muss über mehrere Ticks verteilt sein");
        }

        [Test]
        public void WorldChangeDuringSearch_RestartsAndAvoidsNewObstacle()
        {
            var g = TestWorlds.Flat();
            var pf = new GridPathfinder(g);
            int id = pf.RequestPath(4, 15, 27, 15, MovementClass.Foot);
            pf.ProcessRequests(5);
            for (int y = 2; y < 29; y++)
                TestWorlds.Water(g, 16, y);
            PathStatus s;
            do { pf.ProcessRequests(1000); s = pf.TryGetPath(id, Path); } while (s == PathStatus.Pending);
            Assert.AreEqual(PathStatus.Found, s);
            AssertSegmentsWalkable(g, Path, MovementClass.Foot);
        }

        [Test]
        public void Performance_ManyRequestsOn256()
        {
            var g = new Simulation.World.Generation.WorldGenerator().Generate(new WorldGenSettings { Seed = 21, Width = 256, Height = 256 });
            var pf = new GridPathfinder(g);
            var tiles = new List<TileScore>();
            g.FindStartLocations(40, 10, tiles);
            Assume.That(tiles.Count, Is.GreaterThanOrEqualTo(10));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int found = 0, total = 0;
            for (int i = 0; i < 200; i++)
            {
                var a = tiles[i % tiles.Count];
                var b = tiles[(i * 7 + 3) % tiles.Count];
                total++;
                if (pf.FindPathImmediate(a.X, a.Y, b.X, b.Y, MovementClass.Foot, Path) == PathStatus.Found) found++;
            }
            sw.Stop();
            TestContext.WriteLine($"200 Pfade auf 256²: {sw.ElapsedMilliseconds} ms, gefunden {found}, Expansionen {pf.TotalExpansions}");
            Assert.Less(sw.ElapsedMilliseconds, 5000);
        }

        internal static void AssertSegmentsWalkable(WorldGrid g, List<(int x, int y)> path, MovementClass mc)
        {
            for (int i = 1; i < path.Count; i++)
            {
                var a = path[i - 1];
                var b = path[i];
                int steps = Math.Max(Math.Abs(b.x - a.x), Math.Abs(b.y - a.y)) * 4;
                for (int s = 0; s <= steps; s++)
                {
                    float t = steps == 0 ? 0f : s / (float)steps;
                    float px = WorldCoords.TileCenterX(a.x) + (WorldCoords.TileCenterX(b.x) - WorldCoords.TileCenterX(a.x)) * t;
                    float pz = WorldCoords.TileCenterZ(a.y) + (WorldCoords.TileCenterZ(b.y) - WorldCoords.TileCenterZ(a.y)) * t;
                    var (tx, ty) = WorldCoords.WorldToTile(px, pz);
                    Assert.IsTrue(g.IsWalkable(tx, ty, mc), $"Segment {a}->{b} schneidet blockierte Kachel ({tx},{ty})");
                }
            }
        }

        static float PathCost(WorldGrid g, List<(int x, int y)> path, MovementClass mc)
        {
            float cost = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                var a = path[i - 1];
                var b = path[i];
                int n = Math.Max(Math.Abs(b.x - a.x), Math.Abs(b.y - a.y)) * 8;
                for (int s = 0; s < n; s++)
                {
                    float t0 = s / (float)n, t1 = (s + 1) / (float)n;
                    float x0 = a.x + (b.x - a.x) * t0, y0 = a.y + (b.y - a.y) * t0;
                    float x1 = a.x + (b.x - a.x) * t1, y1 = a.y + (b.y - a.y) * t1;
                    float len = MathF.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
                    int tx = (int)MathF.Round((x0 + x1) * 0.5f), ty = (int)MathF.Round((y0 + y1) * 0.5f);
                    cost += len * g.MoveCost(tx, ty, mc);
                }
            }
            return cost;
        }

        static float Dijkstra(WorldGrid g, int sx, int sy, int tx, int ty, MovementClass mc)
        {
            int w = g.Width, n = w * g.Height;
            var dist = new float[n];
            for (int i = 0; i < n; i++) dist[i] = float.PositiveInfinity;
            var done = new bool[n];
            dist[sy * w + sx] = 0f;
            int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 }, dy = { 0, 0, 1, -1, 1, -1, 1, -1 };
            while (true)
            {
                int cur = -1;
                float best = float.PositiveInfinity;
                for (int i = 0; i < n; i++)
                    if (!done[i] && dist[i] < best) { best = dist[i]; cur = i; }
                if (cur < 0) return float.PositiveInfinity;
                if (cur == ty * w + tx) return best;
                done[cur] = true;
                int cx = cur % w, cy = cur / w;
                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + dx[d], ny = cy + dy[d];
                    if (!g.InBounds(nx, ny) || !g.IsWalkable(nx, ny, mc)) continue;
                    bool diag = dx[d] != 0 && dy[d] != 0;
                    if (diag && (!g.IsWalkable(cx + dx[d], cy, mc) || !g.IsWalkable(cx, cy + dy[d], mc))) continue;
                    float step = (g.MoveCost(cx, cy, mc) + g.MoveCost(nx, ny, mc)) * 0.5f * (diag ? 1.41421356f : 1f);
                    int ni = ny * w + nx;
                    if (best + step < dist[ni]) dist[ni] = best + step;
                }
            }
        }
    }

    public class SpatialHashTests
    {
        [Test]
        public void InsertMoveRemove_Queries()
        {
            var h = new SpatialHash(256f, 256f, 32f);
            var a = new EntityId(1, 1);
            var b = new EntityId(2, 1);
            var results = new List<EntityId>();
            h.Insert(a, 10f, 10f);
            h.Insert(b, 100f, 100f);
            h.QueryRadius(12f, 12f, 5f, results);
            CollectionAssert.AreEquivalent(new[] { a }, results);
            h.Move(a, 98f, 99f);
            h.QueryRadius(100f, 100f, 3f, results);
            CollectionAssert.AreEquivalent(new[] { a, b }, results);
            h.QueryRect(110f, 110f, 90f, 90f, results);
            CollectionAssert.AreEquivalent(new[] { a, b }, results);
            h.Remove(a);
            Assert.IsFalse(h.Contains(a));
            h.QueryRadius(100f, 100f, 3f, results);
            CollectionAssert.AreEquivalent(new[] { b }, results);
            Assert.AreEqual(1, h.Count);
        }

        [Test]
        public void StaleGeneration_IsNotContained_AndOutOfWorldIsClamped()
        {
            var h = new SpatialHash(64f, 64f, 16f);
            h.Insert(new EntityId(3, 1), -50f, 500f);
            Assert.IsFalse(h.Contains(new EntityId(3, 2)));
            h.Remove(new EntityId(3, 2));
            Assert.AreEqual(1, h.Count);
            var results = new List<EntityId>();
            h.QueryRect(-100f, 0f, 100f, 1000f, results);
            Assert.AreEqual(1, results.Count);
        }
    }

    public class MotionTests
    {
        [Test]
        public void Vehicle_TurnsWithLimitedRate_AndAccelerates()
        {
            var g = TestWorlds.Flat();
            var s = new MotionState { X = 60f, Z = 60f, Heading = 0f, Speed = 0f };
            var p = new MotionParams { MovementClass = MovementClass.Wheeled, Speed = 10f, TurnRateDeg = 45f, Acceleration = 2f };
            MotionIntegrator.Step(ref s, in p, g, 60f, 30f, true, 0.1f);   // Ziel genau hinter dem Fahrzeug
            Assert.AreEqual(4.5f * MathF.PI / 180f, MathF.Abs(s.Heading), 1e-4f);
            Assert.LessOrEqual(s.Speed, 0.2f + 1e-4f);
        }

        [Test]
        public void Infantry_TurnsInstantly_AndReachesTarget()
        {
            var g = TestWorlds.Flat();
            var s = new MotionState { X = 30f, Z = 30f };
            var p = new MotionParams { MovementClass = MovementClass.Foot, Speed = 1.5f, TurnRateDeg = 0f, Acceleration = 0f };
            int steps = 0;
            while (!MotionIntegrator.Step(ref s, in p, g, 40f, 30f, true, 0.1f) && steps < 200) steps++;
            Assert.Less(steps, 80);
            Assert.AreEqual(40f, s.X, 0.6f);
        }

        [Test]
        public void ForestSlowsInfantry()
        {
            var g = TestWorlds.Flat();
            for (int x = 2; x < 30; x++) g.SetTerrain(x, 10, TerrainType.Forest);
            var p = new MotionParams { MovementClass = MovementClass.Foot, Speed = 1.5f };
            var grass = new MotionState { X = 20f, Z = 70f };
            var forest = new MotionState { X = 20f, Z = WorldCoords.TileCenterZ(10) };
            MotionIntegrator.Step(ref grass, in p, g, 100f, grass.Z, false, 1f);
            MotionIntegrator.Step(ref forest, in p, g, 100f, forest.Z, false, 1f);
            Assert.Greater(grass.X - 20f, forest.X - 20f);
        }
    }

    public class MovementSystemTests
    {
        static GameComposition Make(out UnitTypeId rifleman, out UnitTypeId tank, out WorldGrid world)
        {
            UnitTypeId r = default, t = default;
            var comp = GameComposition.Create(new GameStartSettings { Seed = 4, WorldWidth = 96, WorldHeight = 96 }, ctx =>
            {
                r = ctx.UnitTypes.Register(new UnitTypeData { Key = "unit.test_rifleman", Movement = MovementClass.Foot, MaxSpeed = 1.5f, TurnRateDeg = 720f, Acceleration = 10f });
                t = ctx.UnitTypes.Register(new UnitTypeData { Key = "unit.test_tank", Movement = MovementClass.Tracked, MaxSpeed = 8f, TurnRateDeg = 40f, Acceleration = 2f, Radius = 3f });
            });
            rifleman = r;
            tank = t;
            world = (WorldGrid)comp.Context.Get<IWorldGrid>();
            return comp;
        }

        [Test]
        public void Composition_RegistersWorldServices()
        {
            var comp = Make(out _, out _, out var world);
            Assert.AreSame(world, comp.Context.Get<IWorldQuery>());
            Assert.IsNotNull(comp.Context.Get<IPathfinder>());
            Assert.IsNotNull(comp.Context.Get<ISpatialIndex>());
            Assert.IsTrue(comp.Context.Saveables.TryGet(WorldGrid.Key, out _));
            Assert.AreEqual(96, world.Width);
        }

        [Test]
        public void Unit_WalksToTarget_OverTerrain_AndArrives()
        {
            var comp = Make(out var rifleman, out _, out var world);
            var ctx = comp.Context;
            var spots = new List<TileScore>();
            Assume.That(world.FindStartLocations(2, 15, spots), Is.EqualTo(2));
            var pf = ctx.Get<IPathfinder>();
            Assume.That(pf.AreConnected(spots[0].X, spots[0].Y, spots[1].X, spots[1].Y, MovementClass.Foot));

            var start = WorldCoords.TileToWorld(spots[0].X, spots[0].Y);
            var goal = WorldCoords.TileToWorld(spots[1].X, spots[1].Y);
            var id = ctx.Units.Spawn(rifleman, 1, start.X, start.Z);
            Assert.IsTrue(ctx.Units.SetMoveTarget(id, goal.X, goal.Z));
            var arrived = new List<UnitArrivedEvent>();
            ctx.Events.Subscribe<UnitArrivedEvent>(e => arrived.Add(e));

            float maxDist = 0f;
            UnitView v = default;
            for (int i = 0; i < 20000 && arrived.Count == 0; i++)
            {
                comp.Runner.StepOnce();
                ctx.Units.TryGet(id, out v);
                var (tx, ty) = WorldCoords.WorldToTile(v.X, v.Z);
                Assert.IsTrue(world.IsWalkable(tx, ty, MovementClass.Foot) || i < 2, $"Einheit auf nicht begehbarer Kachel ({tx},{ty})");
                maxDist = Math.Max(maxDist, v.Speed);
            }
            Assert.AreEqual(1, arrived.Count, "Einheit ist nicht angekommen");
            Assert.IsFalse(v.HasMoveTarget);
            Assert.AreEqual(UnitState.Idle, v.State);
            Assert.AreEqual(goal.X, v.X, 1f);
            Assert.AreEqual(goal.Z, v.Z, 1f);
            Assert.LessOrEqual(maxDist, 1.5f + 1e-3f);
        }

        [Test]
        public void UnreachableTarget_FailsAndClearsTarget()
        {
            var comp = Make(out var rifleman, out _, out var world);
            var ctx = comp.Context;
            var spots = new List<TileScore>();
            Assume.That(world.FindStartLocations(1, 10, spots), Is.EqualTo(1));
            var start = WorldCoords.TileToWorld(spots[0].X, spots[0].Y);
            var id = ctx.Units.Spawn(rifleman, 1, start.X, start.Z);
            ctx.Units.SetMoveTarget(id, 2f, 2f);   // Tiefwasser am Rand
            var failed = new List<UnitMoveFailedEvent>();
            ctx.Events.Subscribe<UnitMoveFailedEvent>(e => failed.Add(e));
            for (int i = 0; i < 5; i++)
                comp.Runner.StepOnce();
            Assert.AreEqual(1, failed.Count);
            ctx.Units.TryGet(id, out var v);
            Assert.IsFalse(v.HasMoveTarget);
        }

        [Test]
        public void DeadUnit_IsRemovedFromSpatialIndex()
        {
            var comp = Make(out var rifleman, out _, out var world);
            var ctx = comp.Context;
            var id = ctx.Units.Spawn(rifleman, 1, 150f, 150f);
            comp.Runner.StepOnce();
            var spatial = ctx.Get<ISpatialIndex>();
            Assert.IsTrue(spatial.Contains(id));
            ctx.Units.Despawn(id);
            comp.Runner.StepOnce();
            Assert.IsFalse(spatial.Contains(id));
        }

        [Test]
        public void Performance_500UnitsMoving_TickUnder8ms()
        {
            var comp = Make(out var rifleman, out var tank, out var world);
            var ctx = comp.Context;
            var spots = new List<TileScore>();
            world.FindStartLocations(30, 4, spots);
            Assume.That(spots.Count, Is.GreaterThanOrEqualTo(6));
            var rnd = new Random(1);
            for (int i = 0; i < 500; i++)
            {
                var a = spots[i % spots.Count];
                var b = spots[rnd.Next(spots.Count)];
                var pa = WorldCoords.TileToWorld(a.X, a.Y);
                var pb = WorldCoords.TileToWorld(b.X, b.Y);
                var id = ctx.Units.Spawn(i % 10 == 0 ? tank : rifleman, 1, pa.X + (i % 5), pa.Z + (i % 7) * 0.5f);
                ctx.Units.SetMoveTarget(id, pb.X, pb.Z);
            }
            for (int i = 0; i < 20; i++) comp.Runner.StepOnce();   // Pfade anfordern, JIT aufwärmen
            var sw = System.Diagnostics.Stopwatch.StartNew();
            const int ticks = 100;
            for (int i = 0; i < ticks; i++) comp.Runner.StepOnce();
            sw.Stop();
            double perTick = sw.Elapsed.TotalMilliseconds / ticks;
            TestContext.WriteLine($"500 Einheiten: {perTick:F2} ms pro Tick (inkl. Pfadbudget)");
            Assert.Less(perTick, 8.0);
        }
    }
}
