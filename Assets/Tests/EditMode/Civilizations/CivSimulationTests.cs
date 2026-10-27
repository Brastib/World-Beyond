using System.Collections.Generic;
using NUnit.Framework;
using WorldBeyond.Core;
using WorldBeyond.Simulation;
using WorldBeyond.Simulation.Civilizations;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Tests.Civilizations
{
    // Abnahme Phase 3 (headless): Zivilisationen entwickeln sich ohne Spielereingriff (Sandbox, INTEGRATION.md §0 Regel 4).
    // Alle Läufe nutzen die echte GameComposition (Welt, Einheiten, Bewegung, Civ) – keine Attrappen.
    public class CivAutonomyTests
    {
        const double LongRunYears = 200;  // 200 Spieljahre (120 000 Ticks); vorindustriell wächst ein Volk höchstens ~1 %/Jahr

        [Test]
        public void TwoCivs_GrowAndExpand_WithoutAnyCommand([Values(1UL, 42UL, 2026UL)] ulong seed)
        {
            var r = CivRun.Create(seed);
            Assert.AreEqual(2, r.Civs.AllCivIds.Count, "Zwei Start-Zivilisationen aus GameStartSettings");
            long woodBefore = r.WorldResource(ResourceType.Wood);
            var startPop = new Dictionary<int, int>();
            foreach (int id in r.Civs.AllCivIds) startPop[id] = r.Civs.Get(id).Population;

            for (int i = 0; i < 4; i++) { r.Years(LongRunYears / 4); TestContext.WriteLine(r.Describe()); }

            Assert.AreEqual(0, r.CommandsSeen, "Sandbox: kein einziger Command beteiligt");
            Assert.AreEqual(0, r.Collapsed, "Keine Zivilisation darf ohne Krieg zusammenbrechen");
            Assert.AreEqual(2, r.Civs.AllCivIds.Count);
            int settlements = r.Settlements.AllSettlementIds.Count;
            foreach (int id in r.Civs.AllCivIds)
            {
                var c = r.Civs.Get(id);
                Assert.GreaterOrEqual(c.Population, startPop[id] * 2, $"{c.Name}: Bevölkerung wächst deutlich");
                Assert.Greater(c.TerritoryTiles, 80, $"{c.Name}: Gebiet wächst");
            }
            Assert.GreaterOrEqual(settlements, 2 + 3, "Mindestens drei neue Siedlungen durch Siedler gegründet");
            Assert.Greater(r.Recruited, 0, "Siedler/Karawanen wurden als echte Einheiten rekrutiert");
            Assert.Greater(r.BuildingsCompleted, 20, "Gebäude wurden gebaut");
            // Realistisches Tempo (VISION V7): Forschung läuft, Zeitalterwechsel kommen erst nach Jahrhunderten (CivTechPaceTests).
            foreach (int id in r.Civs.AllCivIds)
                Assert.IsTrue(r.TechsResearched > 0 || r.State.FindCiv(id).ResearchPoints > 0, "Forschung schreitet voran");
            Assert.Less(r.WorldResource(ResourceType.Wood), woodBefore, "Holz wird auf den Weltkacheln abgebaut");
        }

        [Test]
        public void FoundedSettlements_RespectPlacementRules()
        {
            var r = CivRun.Create(1);
            r.Years(200);
            var world = r.Ctx.Get<IWorldQuery>();
            var ids = r.Settlements.AllSettlementIds;
            Assert.Greater(ids.Count, 2);
            for (int i = 0; i < ids.Count; i++)
            {
                var a = r.Settlements.Get(ids[i]);
                Assert.IsTrue(r.Civs.Exists(a.CivId), $"{a.Name}: Besitzer lebt");
                Assert.IsFalse(world.IsWater(a.X, a.Y), $"{a.Name}: nicht im Wasser");
                Assert.AreEqual(a.CivId, world.Get(a.X, a.Y).OwnerCivId, $"{a.Name}: Ortskern liegt im eigenen Gebiet");
                Assert.IsTrue(world.Get(a.X, a.Y).Building.IsValid, $"{a.Name}: Ortskern steht als Gebäude auf der Karte");
                for (int j = i + 1; j < ids.Count; j++)
                {
                    var b = r.Settlements.Get(ids[j]);
                    int dx = a.X - b.X, dy = a.Y - b.Y;
                    Assert.GreaterOrEqual(dx * dx + dy * dy, SettlementRegistry.MinSettlementDistance * SettlementRegistry.MinSettlementDistance,
                        $"{a.Name} und {b.Name} zu nah");
                }
            }
        }

        [Test]
        public void AutonomousRun_IsDeterministic()
        {
            var a = CivRun.Create(7);
            var b = CivRun.Create(7);
            a.Years(15);
            b.Years(15);
            Assert.AreEqual(a.Fingerprint(), b.Fingerprint());
        }
    }

    public class CivEconomyTests
    {
        [Test]
        public void Hunger_ReducesPopulation_AndConsumesFood()
        {
            var r = CivRun.Create(3);
            r.Years(0.1);
            var st = r.State.FindSettlement(r.Settlements.AllSettlementIds[0]);
            st.Adults = 300;                                  // weit über Wohnraum und Nahrung
            st.Stock[(int)ResourceType.Food] = 20;
            double before = st.Population;
            r.Years(2);
            Assert.Less(st.Population, before * 0.8, "Verhungern senkt die Bevölkerung");
            Assert.Less(st.Stock[(int)ResourceType.Food], 50, "Vorrat wird aufgezehrt statt zu wachsen");
        }

        [Test]
        public void Territory_IsConsistent_WithSettlementsAndCounts()
        {
            var r = CivRun.Create(42);
            r.Years(20);
            var world = r.Ctx.Get<IWorldQuery>();
            var territory = r.Ctx.Get<ITerritoryService>();
            var counted = new Dictionary<int, int>();
            for (int y = 0; y < world.Height; y++)
                for (int x = 0; x < world.Width; x++)
                {
                    int owner = world.Get(x, y).OwnerCivId;
                    if (owner == 0) continue;
                    Assert.IsTrue(r.Civs.Exists(owner), $"Kachel ({x},{y}) gehört toter Civ {owner}");
                    Assert.AreEqual(owner, territory.OwnerAt(x, y));
                    counted[owner] = counted.TryGetValue(owner, out int n) ? n + 1 : 1;
                }
            foreach (int id in r.Civs.AllCivIds)
                Assert.AreEqual(counted.TryGetValue(id, out int n) ? n : 0, territory.TerritoryTiles(id), "Gebietszähler stimmt mit Karte überein");
        }

        [Test]
        public void Lod_ActiveAndDistant_DevelopSimilarly()
        {
            // Eine Civ: ohne Nachbarn kein Krieg, der Test misst nur die Detailstufe (Kämpfe würden die Läufe chaotisch trennen).
            var distant = CivRun.Create(11, civs: 1);
            var active = CivRun.Create(11, civs: 1);
            var w = active.Ctx.Get<IWorldQuery>();
            active.Ctx.Get<ICivSimulationLod>().SetFocus(w.Width * WorldConstants.TileSize / 2f, w.Height * WorldConstants.TileSize / 2f, w.Width * WorldConstants.TileSize);
            distant.Years(150);
            active.Years(150);
            int pd = distant.TotalPopulation(), pa = active.TotalPopulation();
            TestContext.WriteLine($"Entfernt: {pd}, Aktiv: {pa}");
            Assert.Greater(pd, 20);
            Assert.Greater(pa, 20);
            Assert.Less(System.Math.Abs(pd - pa), System.Math.Max(pd, pa) * 0.2, "Detailstufe ändert die Entwicklung nur geringfügig");
        }
    }

    public class CivCommandTests
    {
        [Test]
        public void CreateCivilization_FoundSettlement_AndEvents_Work()
        {
            var r = CivRun.Create(5);
            var rejected = new List<string>();
            r.Ctx.Events.Subscribe<WorldBeyond.Simulation.Commands.CommandRejectedEvent>(e => rejected.Add(e.CommandName + ": " + e.Reason));

            r.Ctx.Commands.Enqueue(new CreateCivilizationCommand { Name = "Testreich", Color = new CivColor(10, 20, 30), CultureSeed = 9 });
            r.Years(0.05);
            Assert.IsEmpty(rejected);
            Assert.AreEqual(3, r.Civs.AllCivIds.Count);
            int newCiv = r.Civs.AllCivIds[2];
            Assert.AreEqual("Testreich", r.Civs.Get(newCiv).Name);
            Assert.AreEqual(1, r.Civs.Get(newCiv).SettlementCount);

            Assert.IsTrue(CivBootstrap.TryFindFreeStart(r.Ctx, out int fx, out int fy));
            r.Ctx.Commands.Enqueue(new FoundSettlementCommand { CivId = newCiv, X = fx, Y = fy });
            r.Years(0.05);
            Assert.IsEmpty(rejected);
            Assert.AreEqual(2, r.Civs.Get(newCiv).SettlementCount);

            int target = r.Civs.Get(1).CapitalSettlementId;
            var plagued = r.State.FindSettlement(target);
            r.Ctx.Commands.Enqueue(new TriggerCivEventCommand { SettlementId = target, Kind = CivEventKind.Plague });
            r.Years(0.05);
            Assert.IsEmpty(rejected);
            Assert.IsTrue(plagued.HasModifier(CivEventKind.Plague, r.Ctx.Clock.Tick), "Seuche wirkt als Modifikator");

            r.Ctx.Commands.Enqueue(new FoundSettlementCommand { CivId = newCiv, X = fx, Y = fy });   // belegt
            r.Years(0.05);
            Assert.AreEqual(1, rejected.Count, "Ungültige Gründung wird abgelehnt, nicht ausgeführt");
        }

        [Test]
        public void Recruitment_SpawnsRealUnit_AndPaysPopulation()
        {
            var r = CivRun.Create(5);
            r.Years(0.05);
            int sid = r.Civs.Get(1).CapitalSettlementId;
            var st = r.State.FindSettlement(sid);
            st.Adults = 30;
            st.Stock[(int)ResourceType.Food] = 200;
            st.Stock[(int)ResourceType.Wood] = 140;
            int unitsBefore = r.Ctx.Units.Count;
            double adultsBefore = st.Adults;
            Assert.AreEqual(RecruitResult.Queued, r.Ctx.Get<IRecruitment>().Request(1, CivContent.UnitSettler, 1, sid));
            r.Years(1);
            Assert.Greater(r.Recruited, 0, "UnitRecruitedEvent");
            Assert.Less(st.Adults, adultsBefore + 3, "Siedler verlassen die Siedlung");
            Assert.AreEqual(RecruitResult.InvalidRequest, r.Ctx.Get<IRecruitment>().Request(1, "unit.unbekannt", 1, sid));
        }

        [Test]
        public void MilitaryCap_IsTenPercent_AtLeastTwo_AtMostEighty()
        {
            var r = CivRun.Create(5);
            r.Years(0.05);
            var rec = (Recruitment)r.Ctx.Get<IRecruitment>();
            var st = r.State.FindSettlement(r.Civs.Get(1).CapitalSettlementId);
            int others = 0;
            foreach (int sid in r.Settlements.GetByCiv(1)) if (sid != st.Id) others += r.Settlements.Get(sid).Population;
            Assert.AreEqual(0, others, "Am Anfang nur der Hauptort");
            st.Children = 0; st.Elders = 0;
            st.Adults = 10;
            Assert.AreEqual(Recruitment.MinMilitaryCap, rec.MaxMilitary(1), "kleines Volk: Mindestwert");
            st.Adults = 300;
            Assert.AreEqual(30, rec.MaxMilitary(1), "10 % der Einwohner");
            st.Adults = 5000;
            Assert.AreEqual(Recruitment.MaxMilitaryAbsolute, rec.MaxMilitary(1), "absolute Obergrenze");
            Assert.AreEqual(80, Recruitment.MaxMilitaryAbsolute);
        }
    }
}
