using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using WorldBeyond.Application;
using WorldBeyond.Application.Save;
using WorldBeyond.Core;
using WorldBeyond.Simulation;
using WorldBeyond.Simulation.Civilizations;
using WorldBeyond.Simulation.World;
using WorldBeyond.Simulation.World.Navigation;

namespace WorldBeyond.Tests.Performance
{
    // Reproduzierbarer Messlauf für Phase 7 (TEST_PLAN T-7-06/T-7-07). Explicit: läuft nur bei gezieltem Aufruf, z. B.
    // dotnet test Tools/SimTests -c Release --filter "FullyQualifiedName~PerformanceProbe" --logger "console;verbosity=detailed"
    // Misst Weltaufbau, Zeit und Allokationen je System und Tick, Speichern und Laden. Prüft nur Konsistenz, keine Zeitgrenzen,
    // weil die Werte vom Rechner abhängen.
    public class PerformanceProbe
    {
        [Explicit]
        [TestCase(256, 2, 0, 3000, 1UL)]
        [TestCase(512, 4, 0, 3000, 2026UL)]
        [TestCase(512, 4, 2000, 1500, 2026UL)]
        [TestCase(1024, 8, 2000, 600, 7UL)]
        [TestCase(512, 4, 1000, 1500, 2026UL, true)]
        public void Measure(int size, int civs, int extraUnits, int ticks, ulong seed, bool allAtWar = false)
        {
            var settings = new GameStartSettings { Seed = seed, WorldWidth = size, WorldHeight = size, CivilizationCount = civs };

            long alloc0 = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            var comp = GameComposition.Create(settings);
            double createMs = sw.Elapsed.TotalMilliseconds;
            long createAlloc = GC.GetAllocatedBytesForCurrentThread() - alloc0;
            var ctx = comp.Context;
            var faults = new List<string>();
            comp.Runner.SystemFailed += (t, src, ex) => { if (faults.Count < 10) faults.Add($"{t} {src}: {ex.GetType().Name} {ex.Message}"); };

            var rng = new Random((int)seed);
            if (extraUnits > 0) SpawnWanderers(ctx, extraUnits, rng);
            if (allAtWar)
            {
                // Alle gegen alle: Kampf, Verfolgung und Armee-KI unter Last (QA-Fund Seed 99: bis 956 Einheiten).
                var ids = ctx.Get<ICivilizationQuery>().AllCivIds;
                var dip = ctx.Get<WorldBeyond.Simulation.Diplomacy.IDiplomacyService>();
                for (int a = 0; a < ids.Count; a++)
                    for (int b = a + 1; b < ids.Count; b++)
                        Assert.IsTrue(dip.DeclareWar(ids[a], ids[b]), $"Krieg {ids[a]}-{ids[b]} nicht möglich");
            }

            var systems = comp.Runner.Systems;
            var total = new double[systems.Count];
            var max = new double[systems.Count];
            var allocs = new long[systems.Count];
            double flushMs = 0, worstTick = 0;
            var tickTimes = new List<double>(ticks);
            int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
            long runAlloc0 = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < ticks; i++)
            {
                // Gleiche Reihenfolge wie SimulationRunner.RunTick, aber je System gemessen.
                long tick = ctx.Clock.Tick;
                double tickMs = 0;
                for (int s = 0; s < systems.Count; s++)
                {
                    var sys = systems[s];
                    if (tick % sys.TickInterval != 0) continue;
                    long a = GC.GetAllocatedBytesForCurrentThread();
                    long t0 = Stopwatch.GetTimestamp();
                    try { sys.Tick(ctx); }
                    catch (Exception ex) { comp.Runner.ReportFault(tick, sys.Name, ex); }
                    double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                    allocs[s] += GC.GetAllocatedBytesForCurrentThread() - a;
                    total[s] += ms;
                    if (ms > max[s]) max[s] = ms;
                    tickMs += ms;
                }
                long f0 = Stopwatch.GetTimestamp();
                try { ctx.Events.Flush(); }
                catch (Exception ex) { comp.Runner.ReportFault(tick, "EventBus", ex); }
                double fms = (Stopwatch.GetTimestamp() - f0) * 1000.0 / Stopwatch.Frequency;
                flushMs += fms;
                tickMs += fms;
                ctx.Clock.Advance();
                tickTimes.Add(tickMs);
                if (tickMs > worstTick) worstTick = tickMs;
            }
            long runAlloc = GC.GetAllocatedBytesForCurrentThread() - runAlloc0;

            tickTimes.Sort();
            double sum = 0; foreach (var t in tickTimes) sum += t;
            var civQuery = ctx.Get<ICivilizationQuery>();

            TestContext.WriteLine($"=== Welt {size}x{size}, Civs {civs}, Zusatzeinheiten {extraUnits}, Seed {seed}, Ticks {ticks}{(allAtWar ? ", alle im Krieg" : "")} ===");
            TestContext.WriteLine($"Aufbau (GameComposition.Create): {createMs:0} ms, {createAlloc / 1048576.0:0.0} MB alloziert");
            TestContext.WriteLine($"Tick: Mittel {sum / ticks:0.000} ms, Median {tickTimes[ticks / 2]:0.000} ms, P99 {tickTimes[(int)(ticks * 0.99)]:0.000} ms, Max {worstTick:0.00} ms (Budget bei x10: 10 ms/Tick)");
            TestContext.WriteLine($"Allokation im Lauf: {runAlloc / 1048576.0:0.0} MB = {runAlloc / ticks / 1024.0:0.0} KB/Tick; GC gen0/1/2: {GC.CollectionCount(0) - gen0}/{GC.CollectionCount(1) - gen1}/{GC.CollectionCount(2) - gen2}");
            for (int s = 0; s < systems.Count; s++)
                TestContext.WriteLine($"  {systems[s].Name,-22} Summe {total[s],9:0.0} ms  Max {max[s],8:0.00} ms  Alloc {allocs[s] / 1024.0,10:0} KB  (Intervall {systems[s].TickInterval})");
            TestContext.WriteLine($"  {"EventFlush",-22} Summe {flushMs,9:0.0} ms");
            if (ctx.Get<IPathfinder>() is GridPathfinder gp) TestContext.WriteLine($"Pfadsuche: {gp.TotalExpansions} Knotenexpansionen gesamt");
            TestContext.WriteLine($"Ende: Einheiten {ctx.Units.Count}, Civs {civQuery.AllCivIds.Count}, Fehler {comp.Runner.FaultCount}");
            foreach (var f in faults) TestContext.WriteLine("  Fehler: " + f);

            MeasureSaveLoad(comp);
            Assert.AreEqual(0, comp.Runner.FaultCount, "Systemfehler während des Laufs: " + string.Join(" | ", faults));
        }

        // Einheiten, die ständig zu neuen Zielen laufen: belastet Pfadsuche, Bewegung und Raumindex.
        private static void SpawnWanderers(SimContext ctx, int count, Random rng)
        {
            Assert.IsTrue(ctx.UnitTypes.TryGetByKey("unit.rifleman", out var type));
            var world = ctx.Get<IWorldQuery>();
            var civIds = ctx.Get<ICivilizationQuery>().AllCivIds;
            var land = new List<(int x, int y)>();
            for (int y = 0; y < world.Height; y += 2)
                for (int x = 0; x < world.Width; x += 2)
                    if (world.IsWalkable(x, y, MovementClass.Foot)) land.Add((x, y));
            Assert.Greater(land.Count, 0);

            (float, float) Center((int x, int y) t) => ((t.x + 0.5f) * WorldConstants.TileSize, (t.y + 0.5f) * WorldConstants.TileSize);
            (float, float) Near((int x, int y) from)
            {
                for (int tries = 0; tries < 20; tries++)
                {
                    var c = land[rng.Next(land.Count)];
                    if (Math.Abs(c.x - from.x) <= 60 && Math.Abs(c.y - from.y) <= 60) return Center(c);
                }
                return Center(land[rng.Next(land.Count)]);
            }

            for (int i = 0; i < count; i++)
            {
                var start = land[rng.Next(land.Count)];
                var (x, z) = Center(start);
                var id = ctx.Units.Spawn(type, civIds[i % civIds.Count], x, z);
                var (tx, tz) = Near(start);
                ctx.Units.SetMoveTarget(id, tx, tz);
            }

            void Retarget(EntityId unit)
            {
                if (!ctx.Units.TryGet(unit, out var v)) return;
                int ux = (int)(v.X / WorldConstants.TileSize), uy = (int)(v.Z / WorldConstants.TileSize);
                var (tx, tz) = Near((ux, uy));
                ctx.Units.SetMoveTarget(unit, tx, tz);
            }
            ctx.Events.Subscribe<UnitArrivedEvent>(e => Retarget(e.Unit));
            ctx.Events.Subscribe<UnitMoveFailedEvent>(e => Retarget(e.Unit));
        }

        private static void MeasureSaveLoad(GameComposition comp)
        {
            string dir = Path.Combine(Path.GetTempPath(), "wb-perf-" + Guid.NewGuid().ToString("N"));
            try
            {
                GameComposition current = comp;
                var service = new SaveLoadService(new SaveFileStore(dir), s => GameComposition.Create(s), () => current, c => current = c, null, "perf");
                var sw = Stopwatch.StartNew();
                var save = service.Save("perf");
                double saveMs = sw.Elapsed.TotalMilliseconds;
                Assert.IsTrue(save.Success, save.Detail);
                long bytes = new FileInfo(new SaveFileStore(dir).PathFor("perf")).Length;
                var root = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(new SaveFileStore(dir).PathFor("perf")));
                var parts = new List<string>();
                foreach (var p in ((Newtonsoft.Json.Linq.JObject)root["sections"]).Properties())
                    parts.Add($"{p.Name} {p.Value.ToString(Newtonsoft.Json.Formatting.None).Length / 1024}KB");
                TestContext.WriteLine("Sektionen: " + string.Join(", ", parts));

                sw.Restart();
                var fresh = GameComposition.Create(comp.Settings);
                double regenMs = sw.Elapsed.TotalMilliseconds;

                sw.Restart();
                var load = service.Load("perf");
                double loadMs = sw.Elapsed.TotalMilliseconds;
                Assert.IsTrue(load.Success, load.Error + ": " + load.Detail);
                Assert.AreEqual(comp.Context.Clock.Tick, current.Context.Clock.Tick);
                Assert.AreEqual(comp.Context.Units.Count, current.Context.Units.Count);
                var stages = new List<string>();
                foreach (var (stage, ms) in service.LastLoadTimings) stages.Add($"{stage} {ms:0}");
                TestContext.WriteLine("Ladeschritte (ms): " + string.Join(", ", stages));
                if (current.Context.Get<IWorldQuery>() is WorldGrid grid) TestContext.WriteLine($"Welt beim Laden neu erzeugt: {grid.LastRestoreRegenerated}");
                TestContext.WriteLine($"Speichern {saveMs:0} ms, Datei {bytes / 1024.0:0} KB; Laden {loadMs:0} ms (davon Weltneuaufbau ca. {regenMs:0} ms)");
                GC.KeepAlive(fresh);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }
    }
}
