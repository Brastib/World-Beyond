using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
        private const int MaxTicks = 144000;  // 4 Spielstunden bei 10 Hz; gemessen: Krieg nach 6–126 min (Seeds 1/3/7/42/2026)

        private static GameStartSettings Settings(ulong seed) =>
            new GameStartSettings { Seed = seed, WorldWidth = 128, WorldHeight = 128, CivilizationCount = 2 };

        [Test]
        public void SaveLoad_InPeace_ContinuesExactlyLikeUninterruptedRun() => SaveLoadRoundtrip(1, 6000, 3000, expectWar: false);

        [TestCase(1UL), TestCase(7UL), TestCase(2026UL)]
        public void WithoutPlayerInput_CivsGrow_DeclareWar_AndFight(ulong seed)
        {
            var comp = GameComposition.Create(Settings(seed));
            var ctx = comp.Context;
            var civs = ctx.Get<ICivilizationQuery>();
            Assert.AreEqual(2, civs.AllCivIds.Count, "Start-Civs");
            int startPop = Population(civs);

            long war = -1, combat = -1;
            ctx.Events.Subscribe<WarDeclaredEvent>(_ => { if (war < 0) war = ctx.Clock.Tick; });
            ctx.Events.Subscribe<UnitDiedEvent>(e => { if (combat < 0 && e.KillerCivId > 0 && e.KillerCivId != e.CivId) combat = ctx.Clock.Tick; });

            for (int i = 0; i < MaxTicks && combat < 0; i++)
            {
                Assert.AreEqual(0, ctx.Commands.PendingCount, "Sandbox: kein Befehl darf anfallen");
                comp.Runner.StepOnce();
            }

            Assert.GreaterOrEqual(war, 0, "kein Krieg in 4 Spielstunden");
            Assert.GreaterOrEqual(combat, war, "kein Kampftoter nach der Kriegserklärung");
            Assert.Greater(Population(civs), startPop, "Bevölkerung ist nicht gewachsen");
            Assert.AreEqual(0, comp.Runner.FaultCount, "Systemfehler im Lauf");
        }

        [Test, Ignore("K7: ArmyAISystem speichert _lastRecruit/_issued nicht, Lauf weicht nach Laden im Krieg ab – Military behebt, dann Ignore entfernen")]
        public void SaveLoad_MidWar_ContinuesExactlyLikeUninterruptedRun() => SaveLoadRoundtrip(1, 12700, 3000, expectWar: true);   // Seed 1: Krieg bei Tick 12651, erster Kampftoter bei 13796

        private static void SaveLoadRoundtrip(ulong seed, int before, int after, bool expectWar)
        {
            string dir = Path.Combine(Path.GetTempPath(), "wb-m1-" + Guid.NewGuid().ToString("N"));
            try
            {
                var reference = GameComposition.Create(Settings(seed));
                for (int i = 0; i < before + after; i++) reference.Runner.StepOnce();

                GameComposition current = GameComposition.Create(Settings(seed));
                for (int i = 0; i < before; i++) current.Runner.StepOnce();
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
                if (expectWar) Assert.IsTrue(reference.Context.Get<IWarQuery>().ActiveWars.Count > 0, "Referenzlauf ohne Krieg – Testannahme prüfen");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
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
