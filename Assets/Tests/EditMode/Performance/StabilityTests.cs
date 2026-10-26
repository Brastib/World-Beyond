using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using WorldBeyond.Application;
using WorldBeyond.Application.Save;
using WorldBeyond.Core;
using WorldBeyond.Simulation;
using WorldBeyond.Simulation.Civilizations;
using WorldBeyond.Simulation.Diplomacy;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Tests.Performance
{
    // Phase 7 Stabilität (TEST_PLAN T-7-08, T-7-19 mit allen Systemen): laufender Krieg, Einheiten verschwinden mitten im Kampf,
    // Siedlungen wechseln den Besitzer, Pause, Speichern und Laden mitten im Gefecht, langer Lauf.
    public class StabilityTests
    {
        private string _dir;

        [SetUp]
        public void SetUp() => _dir = Path.Combine(Path.GetTempPath(), "wb-stab-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch (IOException) { }
        }

        // Zwei Zivilisationen im Krieg mit je 25 Schützen, die sich auf halbem Weg zwischen den Hauptstädten treffen.
        private static GameComposition WarScenario(ulong seed)
        {
            var comp = GameComposition.Create(new GameStartSettings { Seed = seed, WorldWidth = 128, WorldHeight = 128, CivilizationCount = 2 });
            var ctx = comp.Context;
            var civs = ctx.Get<ICivilizationQuery>();
            var settlements = ctx.Get<ISettlementQuery>();
            var world = ctx.Get<IWorldQuery>();
            Assert.AreEqual(2, civs.AllCivIds.Count, "Szenario braucht zwei Zivilisationen");
            int a = civs.AllCivIds[0], b = civs.AllCivIds[1];
            var sa = settlements.Get(civs.Get(a).CapitalSettlementId);
            var sb = settlements.Get(civs.Get(b).CapitalSettlementId);
            Assert.IsTrue(ctx.UnitTypes.TryGetByKey("unit.rifleman", out var rifleman));

            float mx = (sa.X + sb.X) * 0.5f * WorldConstants.TileSize, mz = (sa.Y + sb.Y) * 0.5f * WorldConstants.TileSize;
            foreach (var (civ, s) in new[] { (a, sa), (b, sb) })
            {
                for (int i = 0; i < 25; i++)
                {
                    int tx = s.X + (i % 5) - 2, ty = s.Y + (i / 5) - 2;
                    if (!world.IsWalkable(tx, ty, MovementClass.Foot)) { tx = s.X; ty = s.Y; }
                    var id = ctx.Units.Spawn(rifleman, civ, (tx + 0.5f) * WorldConstants.TileSize, (ty + 0.5f) * WorldConstants.TileSize);
                    ctx.Units.SetMoveTarget(id, mx, mz);
                }
            }
            Assert.IsTrue(ctx.Get<IDiplomacyService>().DeclareWar(a, b), "Krieg konnte nicht erklärt werden");
            return comp;
        }

        // Störungen, wie sie im Spiel vorkommen: Einheit stirbt außerhalb des Kampfs, Siedlung wird übergeben.
        private static void Chaos(GameComposition comp, Random rng, List<EntityId> buffer)
        {
            var ctx = comp.Context;
            long tick = ctx.Clock.Tick;
            if (tick % 37 == 0)
            {
                ctx.Units.CopyAliveIds(buffer);
                if (buffer.Count > 0) ctx.Units.Despawn(buffer[rng.Next(buffer.Count)]);
            }
            if (tick % 400 == 0)
            {
                var civs = ctx.Get<ICivilizationQuery>().AllCivIds;
                var all = ctx.Get<ISettlementQuery>().AllSettlementIds;
                if (civs.Count >= 2 && all.Count > 1)
                {
                    int sid = all[rng.Next(all.Count)];
                    int owner = ctx.Get<ISettlementQuery>().Get(sid).CivId;
                    int to = civs[0] == owner ? civs[1] : civs[0];
                    ctx.Get<ISettlementRegistry>().TransferSettlement(sid, to);
                }
            }
        }

        private static Dictionary<string, string> SectionHashes(GameComposition comp)
        {
            var serializer = SaveGameSerializer.CreateJsonSerializer();
            var result = new Dictionary<string, string>();
            foreach (var s in comp.Context.Saveables.InLoadOrder())
                result[s.SaveKey] = SaveGameSerializer.ComputeChecksum(JToken.FromObject(s.CaptureState(), serializer));
            return result;
        }

        private SaveLoadService Service(Func<GameComposition> current, Action<GameComposition> replace)
            => new SaveLoadService(new SaveFileStore(_dir), s => GameComposition.Create(s), current, replace, null, "stability-test");

        [TestCase(1UL)]
        [TestCase(7UL)]
        [TestCase(42UL)]
        [TestCase(2026UL)]
        public void SaveLoad_MidCombatWithChaos_ContinuesIdentically(ulong seed)
        {
            var original = WarScenario(seed);
            int faults = 0;
            original.Runner.SystemFailed += (t, src, ex) => faults++;
            var rngWarmup = new Random(99);
            var buffer = new List<EntityId>();
            for (int i = 0; i < 400; i++) { Chaos(original, rngWarmup, buffer); original.Runner.StepOnce(); }

            GameComposition current = original;
            var service = Service(() => current, c => current = c);
            var save = service.Save("mid");
            Assert.IsTrue(save.Success, save.Detail);
            var load = service.Load("mid");
            Assert.IsTrue(load.Success, load.Error + ": " + load.Detail);
            var loaded = current;
            Assert.AreNotSame(original, loaded);
            loaded.Runner.SystemFailed += (t, src, ex) => faults++;

            var before = SectionHashes(original);
            var after = SectionHashes(loaded);
            foreach (var key in before.Keys) Assert.AreEqual(before[key], after[key], $"Sektion '{key}' direkt nach dem Laden verschieden");

            var rngA = new Random(7);
            var rngB = new Random(7);
            var bufA = new List<EntityId>();
            var bufB = new List<EntityId>();
            for (int i = 0; i < 800; i++)
            {
                Chaos(original, rngA, bufA); original.Runner.StepOnce();
                Chaos(loaded, rngB, bufB); loaded.Runner.StepOnce();
            }

            before = SectionHashes(original);
            after = SectionHashes(loaded);
            var diverged = new List<string>();
            foreach (var key in before.Keys) if (before[key] != after[key]) diverged.Add(key);
            Assert.IsEmpty(diverged, "Nach 800 Ticks auseinandergelaufen: " + string.Join(", ", diverged));
            Assert.AreEqual(0, faults, "Systemfehler");
        }

        [Test]
        public void LongRun_WarAndChaos_NoFaults_AndSaveStaysLoadable()
        {
            var comp = WarScenario(7UL);
            var errors = new List<string>();
            comp.Runner.SystemFailed += (t, src, ex) => { if (errors.Count < 5) errors.Add($"{t} {src}: {ex}"); };
            var rng = new Random(3);
            var buffer = new List<EntityId>();
            GameComposition current = comp;
            var service = Service(() => current, c => current = c);

            for (int round = 0; round < 6; round++)
            {
                for (int i = 0; i < 1000; i++) { Chaos(current, rng, buffer); current.Runner.StepOnce(); }
                var save = service.Save("long");
                Assert.IsTrue(save.Success, save.Detail);
                var load = service.Load("long");
                Assert.IsTrue(load.Success, $"Runde {round}: {load.Error}: {load.Detail}");
                if (!ReferenceEquals(current, comp))
                {
                    comp = current;
                    comp.Runner.SystemFailed += (t, src, ex) => { if (errors.Count < 5) errors.Add($"{t} {src}: {ex}"); };
                }
                foreach (var w in load.Report.Warnings) TestContext.WriteLine($"Runde {round} Warnung: {w}");
            }
            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.AreEqual(6000, current.Context.Clock.Tick);
        }

        [Test]
        public void Pause_StopsSimulation_AndIsRestoredAfterLoad()
        {
            var comp = WarScenario(1UL);
            for (int i = 0; i < 50; i++) comp.Runner.StepOnce();
            var hashes = SectionHashes(comp);
            comp.Runner.Speed = GameSpeed.Paused;
            Assert.AreEqual(0, comp.Runner.Advance(5f));
            Assert.AreEqual(50, comp.Context.Clock.Tick);
            CollectionAssert.AreEqual(hashes, SectionHashes(comp), "Pause hat den Zustand verändert");

            GameComposition current = comp;
            var service = Service(() => current, c => current = c);
            Assert.IsTrue(service.Save("paused").Success);
            var load = service.Load("paused");
            Assert.IsTrue(load.Success, load.Detail);
            Assert.AreEqual(GameSpeed.Paused, current.Runner.Speed);
            Assert.AreEqual(0, current.Runner.Advance(5f));
            CollectionAssert.AreEqual(hashes, SectionHashes(current));
        }
    }
}
