using System;
using System.Collections.Generic;
using NUnit.Framework;
using WorldBeyond.Core;
using WorldBeyond.Simulation;
using WorldBeyond.Simulation.World;
using WorldBeyond.Simulation.World.Generation;

namespace WorldBeyond.Tests.World
{
    public class WorldSaveTests
    {
        static WorldGrid Gen(ulong seed = 5, int size = 64) =>
            new WorldGenerator().Generate(new WorldGenSettings { Seed = seed, Width = size, Height = size });

        static void AssertSameTiles(WorldGrid a, WorldGrid b)
        {
            Assert.AreEqual(a.Width, b.Width);
            for (int y = 0; y < a.Height; y++)
            {
                for (int x = 0; x < a.Width; x++)
                {
                    TileData p = a.Get(x, y), q = b.Get(x, y);
                    Assert.AreEqual(p.Height, q.Height, $"Höhe ({x},{y})");
                    Assert.AreEqual(p.WaterLevel, q.WaterLevel);
                    Assert.AreEqual(p.Terrain, q.Terrain, $"Gelände ({x},{y})");
                    Assert.AreEqual(p.Biome, q.Biome);
                    Assert.AreEqual(p.Resource, q.Resource);
                    Assert.AreEqual(p.ResourceAmount, q.ResourceAmount);
                    Assert.AreEqual(p.OwnerCivId, q.OwnerCivId);
                    Assert.AreEqual(p.Building, q.Building);
                    Assert.AreEqual(p.Flags, q.Flags);
                }
            }
        }

        [Test]
        public void FreshWorld_SavesOnlySettings()
        {
            var g = Gen();
            var st = (WorldGridState)g.CaptureState();
            Assert.AreEqual(0, st.Tiles.Length);
            Assert.IsFalse(st.FullGrid);
            Assert.AreEqual(g.Seed, st.Seed);
        }

        [Test]
        public void Deltas_RoundTrip_IntoOtherWorld()
        {
            var g = Gen();
            g.SetHeight(20, 20, 77f);
            g.SetOwner(21, 20, 4);
            g.SetBuilding(30, 30, new EntityId(9, 2));
            g.SetResource(10, 40, ResourceType.Gold, 123);
            g.SetFlags(11, 11, TileFlags.Crater | TileFlags.Scorched, TileFlags.None);
            var st = (WorldGridState)g.CaptureState();
            Assert.LessOrEqual(st.Tiles.Length, 5);

            var other = Gen(seed: 999, size: 32);       // andere Größe und Seed: muss vollständig ersetzt werden
            int navBefore = other.NavigationVersion;
            Assert.IsTrue(other.TryRestoreState(st, out string error), error);
            AssertSameTiles(g, other);
            Assert.AreEqual(g.Seed, other.Seed);
            Assert.AreNotEqual(navBefore, other.NavigationVersion);

            // Erneut speichern liefert dieselben Deltas.
            var st2 = (WorldGridState)other.CaptureState();
            Assert.AreEqual(st.Tiles.Length, st2.Tiles.Length);
        }

        [Test]
        public void Restore_IntoUnchangedSameSeedWorld_CopiesInsteadOfRegenerating()
        {
            var g = Gen();
            g.SetHeight(20, 20, 77f);
            g.SetOwner(21, 20, 4);
            var st = (WorldGridState)g.CaptureState();

            var same = Gen();
            Assert.IsTrue(same.TryRestoreState(st, out string error), error);
            Assert.IsFalse(same.LastRestoreRegenerated);
            AssertSameTiles(g, same);
            Assert.AreEqual(g.ChangedTileCount, same.ChangedTileCount);

            // Bereits veränderte Welt mit gleichem Seed (z. B. Start-Siedlungen): Ursprungswerte stellen die Generierung
            // wieder her, fremde Änderungen verschwinden, ohne neu zu generieren.
            var changed = Gen();
            changed.SetHeight(30, 30, 90f);
            changed.SetHeight(20, 20, 5f);
            changed.SetOwner(10, 10, 3);
            changed.SetFlags(12, 12, TileFlags.Road, TileFlags.None);
            changed.SetResource(13, 13, ResourceType.Gold, 9);
            changed.TryHarvest(13, 13, 2, out _);
            changed.SetTerrain(14, 14, TerrainType.Desert);
            Assert.IsTrue(changed.TryRestoreState(st, out error), error);
            Assert.IsFalse(changed.LastRestoreRegenerated);
            AssertSameTiles(g, changed);
            Assert.AreEqual(g.ChangedTileCount, changed.ChangedTileCount);

            // Zweimal hintereinander laden: Ursprungswerte der geladenen Deltas müssen stimmen.
            Assert.IsTrue(changed.TryRestoreState(st, out error), error);
            Assert.IsFalse(changed.LastRestoreRegenerated);
            AssertSameTiles(g, changed);
            AssertSameTiles(Gen(), changed.CloneGenerated());

            var other = Gen(seed: 6);
            Assert.IsTrue(other.TryRestoreState(st, out error), error);
            Assert.IsTrue(other.LastRestoreRegenerated);
            AssertSameTiles(g, other);
        }

        [Test]
        public void FullGrid_Restore_KeepsGeneratedOriginals()
        {
            var g = Gen(size: 48);
            for (int y = 0; y < 48; y++)
                for (int x = 0; x < 30; x++)
                    g.SetOwner(x, y, 1 + (x % 3));
            g.SetHeight(40, 40, 66f);
            var st = (WorldGridState)g.CaptureState();
            Assume.That(st.FullGrid);

            var target = Gen(size: 48);
            target.SetOwner(1, 1, 9);
            Assert.IsTrue(target.TryRestoreState(st, out string error), error);
            Assert.IsFalse(target.LastRestoreRegenerated);
            AssertSameTiles(g, target);
            AssertSameTiles(Gen(size: 48), target.CloneGenerated());
        }

        [Test]
        public void ManyChanges_UseCompressedFullGrid()
        {
            var g = Gen(size: 48);
            for (int y = 0; y < 48; y++)
                for (int x = 0; x < 30; x++)
                    g.SetOwner(x, y, 1 + (x % 3));
            var st = (WorldGridState)g.CaptureState();
            Assert.IsTrue(st.FullGrid);
            Assert.IsNotEmpty(st.FullGridData);
            var other = Gen(seed: 1, size: 48);
            Assert.IsTrue(other.TryRestoreState(st, out string error), error);
            AssertSameTiles(g, other);
            Assert.AreEqual(g.ChangedTileCount, other.ChangedTileCount);
        }

        [Test]
        public void CorruptOrUnknownData_FailsAndKeepsCurrentWorld()
        {
            var g = Gen();
            g.SetHeight(5, 5, 50f);
            var st = (WorldGridState)g.CaptureState();
            var target = Gen(seed: 77, size: 32);

            st.Tiles[0].Terrain = "Lava";
            Assert.IsFalse(target.TryRestoreState(st, out string e1));
            StringAssert.Contains("Lava", e1);
            Assert.AreEqual(32, target.Width, "Bei Fehler bleibt die alte Welt erhalten");

            st.Tiles[0].Terrain = "Grass";
            st.Tiles[0].Index = 999999;
            Assert.IsFalse(target.TryRestoreState(st, out _));

            var full = (WorldGridState)g.CaptureState();
            full.FullGrid = true;
            full.FullGridData = "kein-base64!";
            Assert.IsFalse(target.TryRestoreState(full, out string e3));
            StringAssert.Contains("beschädigt", e3);

            var bad = (WorldGridState)g.CaptureState();
            bad.Width = 5;
            Assert.IsFalse(target.TryRestoreState(bad, out _));
            Assert.IsFalse(target.TryRestoreState("falscher Typ", out _));
        }

        [Test]
        public void EnumOrderChange_IsHandledByNames()
        {
            var g = Gen();
            g.SetTerrain(15, 15, TerrainType.Desert);
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 40; x++)
                    g.SetOwner(x, y, 2);
            var st = (WorldGridState)g.CaptureState();
            Assert.IsTrue(st.FullGrid);
            // Simuliert einen alten Spielstand mit anderer Enum-Reihenfolge: Namen tauschen und Daten entsprechend umdeuten
            // ist nicht möglich ohne Neukodierung; daher prüfen wir, dass Indizes über die Namenspalette aufgelöst werden.
            Array.Reverse(st.TerrainNames);
            var other = Gen(seed: 3);
            Assert.IsTrue(other.TryRestoreState(st, out string error), error);
            // Mit umgekehrter Palette werden Indizes anders aufgelöst: Beweis, dass die Palette (Namen) verwendet wird.
            Assert.AreNotEqual(g.Get(15, 15).Terrain, other.Get(15, 15).Terrain);
            st.TerrainNames[0] = "Unbekannt";
            Assert.IsFalse(Gen().TryRestoreState(st, out _));
        }

        [Test]
        public void ReferenceCheck_ClearsDeadBuildingsAndCivs()
        {
            var g = Gen();
            var alive = new EntityId(1, 1);
            var dead = new EntityId(2, 1);
            Assume.That(g.SetBuilding(30, 30, alive) || g.SetBuilding(31, 31, alive));
            var spots = new List<TileScore>();
            g.FindStartLocations(1, 1, spots);
            Assume.That(spots.Count, Is.EqualTo(1));
            Assume.That(g.SetBuilding(spots[0].X, spots[0].Y, dead));
            g.SetOwner(3, 3, 7);
            g.BuildingExists = id => id.Equals(alive);
            g.CivilizationExists = civ => civ != 7;
            var report = new LoadReport();
            g.CheckReferences(null, report);
            Assert.AreEqual(2, report.Warnings.Count);
            Assert.IsFalse(g.Get(spots[0].X, spots[0].Y).Building.IsValid);
            Assert.AreEqual(0, g.Get(3, 3).OwnerCivId);
            Assert.IsFalse(report.HasErrors);
        }
    }
}
