using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using WorldBeyond.Core;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Tests.World
{
    // Sammelt Events wie der echte Bus, ohne Zustellung am Tick-Ende.
    internal sealed class RecordingBus : ISimEventBus
    {
        public readonly List<object> Events = new List<object>();
        public void Publish<T>(in T evt) where T : struct, ISimEvent => Events.Add(evt);
        public void Subscribe<T>(Action<T> handler) where T : struct, ISimEvent { }
        public void Unsubscribe<T>(Action<T> handler) where T : struct, ISimEvent { }
        public void Flush() { }
        public int Count<T>() { int n = 0; foreach (var e in Events) if (e is T) n++; return n; }
    }

    internal static class TestWorlds
    {
        // Flaches Grasland auf 25 m Höhe, Rand Tiefwasser. Für Tests mit exakt bekannter Karte.
        public static WorldGrid Flat(int size = 32, float height = 25f)
        {
            var g = new WorldGrid(new WorldGenSettings { Seed = 1, Width = size, Height = size });
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool border = x < 2 || y < 2 || x >= size - 2 || y >= size - 2;
                    g.Set(x, y, new TileData
                    {
                        Height = border ? 5f : height,
                        WaterLevel = border ? 20f : 0f,
                        Terrain = border ? TerrainType.DeepWater : TerrainType.Grass,
                        Biome = border ? BiomeType.Ocean : BiomeType.Temperate,
                    });
                }
            }
            return g;
        }

        public static void Water(WorldGrid g, int x, int y)
        {
            var t = g.Get(x, y);
            t.Terrain = TerrainType.ShallowWater;
            t.WaterLevel = t.Height + 1.5f;
            g.Set(x, y, t);
        }
    }

    public class WorldGridTests
    {
        [Test]
        public void Coords_RoundTrip()
        {
            for (int x = 0; x < 10; x++)
            {
                Vector3 c = WorldCoords.TileToWorld(x, 7);
                var (tx, ty) = WorldCoords.WorldToTile(c.X, c.Z);
                Assert.AreEqual((x, 7), (tx, ty));
            }
            Assert.AreEqual((-1, -1), WorldCoords.WorldToTile(-0.1f, -0.1f));
            Assert.AreEqual(4f * 1.5f, WorldCoords.TileCenterX(1));
        }

        [Test]
        public void ClampToBounds_KeepsInsideWorld()
        {
            var p = WorldCoords.ClampToBounds(new Vector3(-5f, 3f, 9999f), 32, 32);
            Assert.AreEqual(0f, p.X);
            Assert.Less(p.Z, 32 * WorldConstants.TileSize);
            Assert.AreEqual(3f, p.Y);
        }

        [Test]
        public void Get_OutOfBounds_Throws_InBoundsFalse()
        {
            var g = TestWorlds.Flat();
            Assert.IsFalse(g.InBounds(-1, 0));
            Assert.IsFalse(g.InBounds(32, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => g.Get(32, 0));
        }

        [Test]
        public void SampleHeight_IsContinuous_AndMatchesTileCenters()
        {
            var g = new Simulation.World.Generation.WorldGenerator().Generate(new WorldGenSettings { Seed = 9, Width = 64, Height = 64 });
            for (int x = 1; x < 63; x++)
            {
                float cx = WorldCoords.TileCenterX(x), cz = WorldCoords.TileCenterZ(20);
                Assert.AreEqual(g.SurfaceHeightAt(x, 20), g.SampleHeight(cx, cz), 1e-3f);
            }
            float max = 0f;
            float prev = g.SampleHeight(0f, 80f);
            for (float wx = 0.05f; wx < 64 * 4f; wx += 0.05f)
            {
                float h = g.SampleHeight(wx, 80f);
                max = Math.Max(max, Math.Abs(h - prev));
                prev = h;
            }
            Assert.Less(max, 2f, "Sprung in SampleHeight");
        }

        [Test]
        public void SampleNormal_FlatGround_PointsUp()
        {
            var g = TestWorlds.Flat();
            Vector3 n = g.SampleNormal(60f, 60f);
            Assert.AreEqual(1f, n.Y, 1e-5f);
        }

        [Test]
        public void Walkability_ByMovementClass()
        {
            var g = TestWorlds.Flat();
            g.SetTerrain(10, 10, TerrainType.Forest);
            g.SetTerrain(11, 10, TerrainType.Mountain);
            g.SetTerrain(12, 10, TerrainType.Swamp);
            g.SetTerrain(13, 10, TerrainType.River);
            TestWorlds.Water(g, 14, 10);

            Assert.IsTrue(g.IsWalkable(10, 10, MovementClass.Foot));
            Assert.IsFalse(g.IsWalkable(10, 10, MovementClass.Wheeled));
            Assert.IsTrue(g.IsWalkable(10, 10, MovementClass.Tracked));
            Assert.IsTrue(g.IsWalkable(11, 10, MovementClass.Foot));
            Assert.IsFalse(g.IsWalkable(11, 10, MovementClass.Tracked));
            Assert.IsFalse(g.IsWalkable(12, 10, MovementClass.Wheeled));
            Assert.IsTrue(g.IsWalkable(13, 10, MovementClass.Foot), "Furt");
            Assert.IsFalse(g.IsWalkable(13, 10, MovementClass.Wheeled));
            Assert.IsFalse(g.IsWalkable(14, 10, MovementClass.Foot));

            g.SetFlags(14, 10, TileFlags.Road, TileFlags.None);
            Assert.IsTrue(g.IsWalkable(14, 10, MovementClass.Wheeled), "Brücke");
            Assert.Less(g.MoveCost(15, 10, MovementClass.Foot), g.MoveCost(10, 10, MovementClass.Foot));
        }

        [Test]
        public void SteepSlope_BlocksVehiclesBeforeInfantry()
        {
            var g = TestWorlds.Flat();
            // Rampe entlang x: 2 m pro Kachel (≈ 26.6°): Rad gesperrt (20°), Kette und Fuß erlaubt.
            for (int x = 5; x < 20; x++)
                for (int y = 5; y < 20; y++)
                    g.SetHeight(x, y, 25f + (x - 5) * 2f);
            Assert.IsFalse(g.IsWalkable(10, 10, MovementClass.Wheeled));
            Assert.IsTrue(g.IsWalkable(10, 10, MovementClass.Tracked));
            Assert.IsTrue(g.IsWalkable(10, 10, MovementClass.Foot));
            Assert.AreEqual(26.6f, g.SlopeDegrees(10, 10), 0.5f);
        }

        [Test]
        public void Events_AreRaised_ForChanges()
        {
            var g = TestWorlds.Flat();
            var bus = new RecordingBus();
            g.AttachEvents(bus);
            int nav = g.NavigationVersion;
            g.SetHeight(5, 5, 30f);
            g.SetHeight(5, 5, 30f);   // keine Änderung → kein Event
            Assert.AreEqual(1, bus.Count<TileChangedEvent>());
            Assert.Greater(g.NavigationVersion, nav);
        }

        [Test]
        public void TryHarvest_DepletesForest_ToGrass()
        {
            var g = TestWorlds.Flat();
            var bus = new RecordingBus();
            g.AttachEvents(bus);
            g.SetTerrain(8, 8, TerrainType.Forest);
            g.SetResource(8, 8, ResourceType.Wood, 30);
            int nav = g.NavigationVersion;

            Assert.IsTrue(g.TryHarvest(8, 8, 20, out int a));
            Assert.AreEqual(20, a);
            Assert.AreEqual(nav, g.NavigationVersion, "Teilabbau ändert die Begehbarkeit nicht");
            Assert.IsTrue(g.TryHarvest(8, 8, 20, out int b));
            Assert.AreEqual(10, b);
            Assert.AreEqual(ResourceType.None, g.Get(8, 8).Resource);
            Assert.AreEqual(TerrainType.Grass, g.Get(8, 8).Terrain);
            Assert.AreEqual(1, bus.Count<ResourceDepletedEvent>());
            Assert.Greater(g.NavigationVersion, nav);
            Assert.IsFalse(g.TryHarvest(8, 8, 5, out int c));
            Assert.AreEqual(0, c);
            Assert.IsFalse(g.TryHarvest(-1, 8, 5, out _));
        }

        [Test]
        public void SetOwner_IsBundled_OneEventPerFlush()
        {
            var g = TestWorlds.Flat();
            var bus = new RecordingBus();
            g.AttachEvents(bus);
            for (int x = 4; x <= 9; x++)
                g.SetOwner(x, 6, 3);
            g.SetOwner(12, 15, 3);
            Assert.AreEqual(0, bus.Count<OwnershipChangedEvent>());
            Assert.AreEqual(0, bus.Count<TileChangedEvent>());
            g.FlushPendingEvents();
            g.FlushPendingEvents();
            Assert.AreEqual(1, bus.Count<OwnershipChangedEvent>());
            var e = (OwnershipChangedEvent)bus.Events.Find(o => o is OwnershipChangedEvent);
            Assert.AreEqual((4, 6, 12, 15), (e.MinX, e.MinY, e.MaxX, e.MaxY));
            Assert.AreEqual(3, g.Get(7, 6).OwnerCivId);
            Assert.Throws<ArgumentOutOfRangeException>(() => g.SetOwner(4, 6, -1));
        }

        [Test]
        public void Buildings_OccupyTiles_AndBlockMovement()
        {
            var g = TestWorlds.Flat();
            var b = new EntityId(5, 1);
            Assert.IsTrue(g.SetBuilding(10, 10, b));
            Assert.IsTrue(g.SetBuilding(10, 10, b), "gleiche ID erneut: unverändert ok");
            Assert.IsFalse(g.SetBuilding(10, 10, new EntityId(6, 1)), "belegt");
            Assert.IsFalse(g.IsWalkable(10, 10, MovementClass.Foot));
            Assert.IsFalse(g.CanPlaceFootprint(9, 9, 3, 3, out var reason));
            Assert.AreEqual(PlacementFailure.Occupied, reason);
            g.ClearBuilding(10, 10);
            Assert.IsTrue(g.CanPlaceFootprint(9, 9, 3, 3, out reason));
            Assert.IsFalse(g.SetBuilding(10, 10, EntityId.None));
        }

        [Test]
        public void Footprint_RejectsWaterSteepAndBounds()
        {
            var g = TestWorlds.Flat();
            Assert.IsFalse(g.CanPlaceFootprint(0, 0, 2, 2, out var r1));
            Assert.AreEqual(PlacementFailure.Water, r1);
            Assert.IsFalse(g.CanPlaceFootprint(30, 30, 4, 4, out var r2));
            Assert.AreEqual(PlacementFailure.Water, r2);
            Assert.IsFalse(g.CanPlaceFootprint(31, 31, 3, 3, out var r3));
            Assert.AreNotEqual(PlacementFailure.None, r3);
            g.SetHeight(15, 15, 40f);
            Assert.IsFalse(g.CanPlaceFootprint(14, 14, 3, 3, out var r4));
            Assert.AreEqual(PlacementFailure.TooSteep, r4);
        }

        [Test]
        public void LineOfSight_BlockedByHillAndForest()
        {
            var g = TestWorlds.Flat();
            var a = new Vector3(WorldCoords.TileCenterX(5), 26.8f, WorldCoords.TileCenterZ(15));
            var b = new Vector3(WorldCoords.TileCenterX(25), 26.8f, WorldCoords.TileCenterZ(15));
            Assert.IsTrue(g.HasLineOfSight(a, b));
            g.SetTerrain(15, 15, TerrainType.Forest);
            Assert.IsFalse(g.HasLineOfSight(a, b), "Wald verdeckt");
            g.SetTerrain(15, 15, TerrainType.Grass);
            g.SetHeight(15, 15, 35f);
            Assert.IsFalse(g.HasLineOfSight(a, b), "Hügel verdeckt");
            var high = new Vector3(a.X, 60f, a.Z);
            Assert.IsTrue(g.HasLineOfSight(high, new Vector3(b.X, 60f, b.Z)));
        }

        [Test]
        public void Raycast_FromAbove_HitsCorrectTile()
        {
            var g = TestWorlds.Flat();
            var target = WorldCoords.TileToWorld(g, 12, 17);
            var origin = new Vector3(target.X - 30f, 120f, target.Z - 40f);
            var dir = Vector3.Normalize(target - origin);
            Assert.IsTrue(g.Raycast(new WorldRay(origin, dir), 1000f, out int x, out int y, out Vector3 hit));
            Assert.AreEqual((12, 17), (x, y));
            Assert.AreEqual(25f, hit.Y, 0.05f);
            Assert.IsFalse(g.Raycast(new WorldRay(origin, new Vector3(0f, 1f, 0f)), 1000f, out _, out _, out _), "nach oben");
        }

        [Test]
        public void Raycast_HitsWaterSurface()
        {
            var g = TestWorlds.Flat();
            var target = new Vector3(WorldCoords.TileCenterX(0), 20f, WorldCoords.TileCenterZ(16));
            var origin = new Vector3(target.X + 10f, 80f, target.Z);
            Assert.IsTrue(g.Raycast(new WorldRay(origin, Vector3.Normalize(target - origin)), 500f, out int x, out _, out Vector3 hit));
            Assert.AreEqual(20f, hit.Y, 0.1f);
            Assert.AreEqual(0, x);
        }

        [Test]
        public void FindBestTiles_SortedAndBounded()
        {
            var g = TestWorlds.Flat();
            var results = new List<TileScore>();
            int n = g.FindBestTiles(16, 16, 5, (int x, int y, in TileData t) => x + y * 0.01f, 3, results);
            Assert.AreEqual(3, n);
            Assert.AreEqual(21, results[0].X);
            Assert.GreaterOrEqual(results[0].Score, results[1].Score);
            Assert.GreaterOrEqual(results[1].Score, results[2].Score);
            foreach (var r in results)
                Assert.LessOrEqual((r.X - 16) * (r.X - 16) + (r.Y - 16) * (r.Y - 16), 25);
            Assert.AreEqual(0, g.FindBestTiles(16, 16, 5, (int x, int y, in TileData t) => 0f, 3, results));
        }

        [Test]
        public void ForEachInRect_ClipsToBounds()
        {
            var g = TestWorlds.Flat();
            int count = 0;
            g.ForEachInRect(-5, -5, 1, 1, (int x, int y, in TileData t) => count++);
            Assert.AreEqual(4, count);
        }
    }
}
