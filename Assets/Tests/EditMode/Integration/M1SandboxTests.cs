using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using NUnit.Framework;
using WorldBeyond.Application;
using WorldBeyond.Application.Save;
using WorldBeyond.Core;
using WorldBeyond.Simulation.Civilizations;
using WorldBeyond.Simulation.Diplomacy;
using WorldBeyond.Simulation.Units;

namespace WorldBeyond.Tests.Integration
{
    // M1-Abnahme (INTEGRATION §4) mit der echten GameComposition und allen Phasen, ohne jeden Spielerbefehl (Sandbox).
    public class M1SandboxTests
    {
        private const int MaxTicks = 324000;  // 9 Spielstunden (= 540 Civ-Jahre bei Civ e8f38dc); gemessen: erster Krieg nach 56 min (Seed 7), 144 min (1), 498 min (2026)

        private static GameStartSettings Settings(ulong seed) =>
            new GameStartSettings { Seed = seed, WorldWidth = 128, WorldHeight = 128, CivilizationCount = 2 };

        [Test]
        public void SaveLoad_InPeace_ContinuesExactlyLikeUninterruptedRun() => SaveLoadRoundtrip(1, 20000, 3000, expectWar: false);

        // Nutzerregel 2026-10-26: Krieg ist kein Zwang, nur wenn die KI ihn für nötig hält. Deshalb muss nicht jede Welt Krieg haben,
        // aber Wachstum ohne Fehler gilt für jede Welt.
        [TestCase(1UL), TestCase(7UL), TestCase(2026UL)]
        public void WithoutPlayerInput_CivsGrow_WithoutFaults(ulong seed)
        {
            var comp = GameComposition.Create(Settings(seed));
            var ctx = comp.Context;
            var civs = ctx.Get<ICivilizationQuery>();
            Assert.AreEqual(2, civs.AllCivIds.Count, "Start-Civs");
            int startPop = Population(civs);
            for (int i = 0; i < 144000; i++)  // 4 Spielstunden = 240 Civ-Jahre; Frühzeit kann anfangs schrumpfen (Seed 7: 20 → 16 nach 1 h)
            {
                Assert.AreEqual(0, ctx.Commands.PendingCount, "Sandbox: kein Befehl darf anfallen");
                comp.Runner.StepOnce();
            }
            Assert.Greater(Population(civs), startPop, "Bevölkerung ist nicht gewachsen");
            Assert.AreEqual(0, comp.Runner.FaultCount, "Systemfehler im Lauf");
        }

        // Ohne Befehl können Kriege entstehen, werden gekämpft und enden; Siedlungen wechseln durch Besetzung den Besitzer.
        // Geprüft über mehrere Welten zusammen (gemessen bei Civ e8f38dc / Military 83faa42: Seed 7 Krieg ab 56 min,
        // Besetzung bei 327 min; Seed 1 drei Kriege, zwei Besetzungen; Seed 2026 ein Krieg ohne Besetzung).
        [Test]
        public void WithoutPlayerInput_WarsEmerge_AreFought_End_AndConquer()
        {
            bool war = false, combat = false, ended = false, captured = false;
            foreach (ulong seed in new[] { 7UL, 1UL, 2026UL })
            {
                var comp = GameComposition.Create(Settings(seed));
                var ctx = comp.Context;
                ctx.Events.Subscribe<WarDeclaredEvent>(_ => war = true);
                ctx.Events.Subscribe<WarEndedEvent>(_ => ended = true);
                ctx.Events.Subscribe<SettlementCapturedEvent>(_ => captured = true);
                ctx.Events.Subscribe<UnitDiedEvent>(e => { if (e.KillerCivId > 0 && e.KillerCivId != e.CivId) combat = true; });
                for (int i = 0; i < MaxTicks && !(war && combat && ended && captured); i++)
                {
                    Assert.AreEqual(0, ctx.Commands.PendingCount, "Sandbox: kein Befehl darf anfallen");
                    comp.Runner.StepOnce();
                }
                Assert.AreEqual(0, comp.Runner.FaultCount, $"Systemfehler im Lauf (Seed {seed})");
                if (war && combat && ended && captured) break;
            }
            Assert.IsTrue(war, "kein Krieg in 3 Welten × 9 Spielstunden");
            Assert.IsTrue(combat, "kein Kampftoter");
            Assert.IsTrue(ended, "kein Krieg endet (festhängender Krieg)");
            Assert.IsTrue(captured, "keine Siedlung durch Besetzung übernommen");
        }

        [Test]
        public void SaveLoad_MidWar_ContinuesExactlyLikeUninterruptedRun()
        {
            // Speicherpunkt aus dem Lauf selbst bestimmen (300 Ticks nach der ersten Kriegserklärung), damit Balancing-Änderungen den Test nicht verschieben.
            const ulong seed = 7;
            var probe = GameComposition.Create(Settings(seed));
            long warTick = -1;
            probe.Context.Events.Subscribe<WarDeclaredEvent>(_ => { if (warTick < 0) warTick = probe.Context.Clock.Tick; });
            for (int i = 0; i < MaxTicks && warTick < 0; i++) probe.Runner.StepOnce();
            Assert.GreaterOrEqual(warTick, 0, "Seed 7 ohne Krieg in 9 Spielstunden – Testwelt wechseln");
            SaveLoadRoundtrip(seed, (int)warTick + 300, 2000, expectWar: true);
        }

        private static void SaveLoadRoundtrip(ulong seed, int before, int after, bool expectWar)
        {
            string dir = Path.Combine(Path.GetTempPath(), "wb-m1-" + Guid.NewGuid().ToString("N"));
            try
            {
                var reference = GameComposition.Create(Settings(seed));
                for (int i = 0; i < before + after; i++) reference.Runner.StepOnce();

                GameComposition current = GameComposition.Create(Settings(seed));
                for (int i = 0; i < before; i++) current.Runner.StepOnce();
                if (expectWar) Assert.IsTrue(current.Context.Get<IWarQuery>().ActiveWars.Count > 0, "Kein Krieg zum Speicherzeitpunkt – Testannahme prüfen");
                var service = new SaveLoadService(new SaveFileStore(dir), s => GameComposition.Create(s), () => current, c => current = c,
                                                  null, "m1-test", () => new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc));
                var saved = service.Save("m1");
                Assert.IsTrue(saved.Success, "Speichern: " + saved.Error);
                var original = current;
                var loaded = service.Load("m1");
                Assert.IsTrue(loaded.Success, "Laden: " + loaded.Error);
                Assert.AreNotSame(original, current, "Komposition wurde nicht ersetzt");
                Assert.AreEqual(before, current.Context.Clock.Tick);
                for (int i = 0; i < after; i++) current.Runner.StepOnce();

                Assert.AreEqual(Fingerprint(reference), Fingerprint(current));
                Assert.AreEqual(SaveablesJson(reference), SaveablesJson(current), "gespeicherter Zustand weicht ab");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        // Alles, was gespeichert wird, als JSON je Sektion (deckt auch Systeme ohne Zufall ab, z. B. Armeen, Kriegsstand).
        private static string SaveablesJson(GameComposition comp)
        {
            var sb = new StringBuilder();
            foreach (var s in comp.Context.Saveables.InLoadOrder())
                sb.Append(s.SaveKey).Append(": ").Append(JsonConvert.SerializeObject(s.CaptureState(), SaveGameSerializer.CreateSettings())).Append('\n');
            return sb.ToString();
        }

        private static int Population(ICivilizationQuery civs)
        {
            int p = 0;
            foreach (int id in civs.AllCivIds) p += civs.Get(id).Population;
            return p;
        }

        // Vergleichbarer Zustand: Uhr, Einheiten (Typ, Civ, Position, HP, Zustand), Civs, Kriege.
        private static string Fingerprint(GameComposition comp)
        {
            var ctx = comp.Context;
            var sb = new StringBuilder();
            sb.Append("tick ").Append(ctx.Clock.Tick).Append(" rnd ").Append(ctx.Random.State).Append('\n');
            var ids = new List<EntityId>();
            ctx.Units.CopyAliveIds(ids);
            ids.Sort((a, b) => a.Index != b.Index ? a.Index.CompareTo(b.Index) : a.Generation.CompareTo(b.Generation));
            foreach (var id in ids)
            {
                var u = ctx.Units.Get(id);
                sb.Append($"u {id.Index}/{id.Generation} {ctx.UnitTypes.KeyOf(u.Type)} c{u.CivId} {u.X:R},{u.Z:R} hp{u.Hp:R} {u.State}\n");
            }
            var civs = ctx.Get<ICivilizationQuery>();
            foreach (int c in civs.AllCivIds)
            {
                var v = civs.Get(c);
                sb.Append($"civ {c} pop{v.Population} st{v.SettlementCount} terr{v.TerritoryTiles} mil{v.MilitaryStrength}\n");
            }
            foreach (var w in ctx.Get<IWarQuery>().ActiveWars) sb.Append($"war {w.Id}\n");
            return sb.ToString();
        }
    }
}
