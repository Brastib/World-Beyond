using System.Linq;
using NUnit.Framework;
using WorldBeyond.Simulation;
using WorldBeyond.Simulation.Diplomacy;

namespace WorldBeyond.Tests.EditMode.Diplomacy
{
    // Phase 4, TEST_PLAN T-4-01 … T-4-07 plus Zusatzfälle.
    public class DiplomacyTests
    {
        DiplomacyTestRig _rig;
        DiplomacyService D => _rig.Service;
        FakeDiplomacyWorld W => _rig.World;

        [SetUp]
        public void SetUp() { _rig = new DiplomacyTestRig(); }

        // ---------- T-4-01 Kriegserklärung ----------

        [Test]
        public void T401_DeclareWar_SetsWarForBothSides_AndPublishesEvent()
        {
            W.AddCiv(1, 0, 0); W.AddCiv(2, 30, 0);
            Assert.IsTrue(D.DeclareWar(1, 2));
            Assert.AreEqual(DiplomaticStatus.War, D.GetStatus(1, 2));
            Assert.AreEqual(DiplomaticStatus.War, D.GetStatus(2, 1));
            Assert.IsTrue(D.AreAtWar(2, 1));
            Assert.AreEqual(1, _rig.Bus.Of<WarDeclaredEvent>().Count);
            Assert.Less(D.GetOpinion(2, 1), -40f, "Opfer der Kriegserklärung muss den Angreifer schlechter sehen");
            Assert.IsFalse(D.DeclareWar(1, 2), "Doppelte Kriegserklärung muss abgelehnt werden");
        }

        [Test]
        public void T401_DeclareWar_CallsAllies_DefenderAllyJoins_AttackerAllyDeclinesIfFriendlyWithTarget()
        {
            W.AddCiv(1, 0, 0); W.AddCiv(2, 30, 0); W.AddCiv(3, 60, 0); W.AddCiv(4, 0, 30);
            Assert.IsTrue(D.FormAlliance(2, 3));   // Verbündeter des Verteidigers
            Assert.IsTrue(D.FormAlliance(1, 4));   // Verbündeter des Angreifers
            D.ModifyRelation(4, 2, 30, RelationReason.PlayerIntervention);  // 4 mag 2

            Assert.IsTrue(D.DeclareWar(1, 2));
            var war = D.FindWar(1, 2);
            CollectionAssert.AreEquivalent(new[] { 2, 3 }, war.Defenders);
            CollectionAssert.AreEquivalent(new[] { 1 }, war.Attackers);
            Assert.IsTrue(D.AreAtWar(3, 1));
            Assert.IsFalse(D.AreAtWar(4, 2));
            Assert.IsTrue(D.GetModifiers(1, 4).Any(m => m.Reason == RelationReason.RefusedCallToArms));
        }

        [Test]
        public void T401_AttackerAlly_Joins_WhenHostileToTarget()
        {
            W.AddCiv(1, 0, 0); W.AddCiv(2, 30, 0); W.AddCiv(4, 0, 30);
            D.FormAlliance(1, 4);
            D.ModifyRelation(4, 2, -20, RelationReason.PlayerIntervention);
            D.DeclareWar(1, 2);
            CollectionAssert.Contains(D.FindWar(1, 2).Attackers, 4);
        }

        // ---------- T-4-02 Frieden ----------

        [Test]
        public void T402_PeaceDemand_RejectedWhenWarScoreTooLow_AcceptedAfterConquest()
        {
            W.AddCiv(1, 0, 0, strength: 100, population: 1000);
            W.AddCiv(2, 40, 0, strength: 100, population: 1000);
            int extra = W.AddSettlement(2, 50, 10, 200);
            W.AddSettlement(2, 70, 10, 200);   // damit 2 nach den Verlusten nicht nur noch eine Siedlung hat
            D.DeclareWar(1, 2);
            var war = D.FindWar(1, 2);

            var demand = PeaceTerm.Cede(2, extra);
            var early = D.ProposePeace(1, 2, demand);
            Assert.IsFalse(early.Accepted, "Ohne Kriegserfolg darf keine Siedlung gefordert werden");

            // Kriegsverlauf: Angreifer erobert die Hauptstadt (Meldung aus Civ) und gewinnt Gefechte.
            int capital = W.GetSettlements(2).First(id => W.Settlements[id].IsCapital);
            W.TransferSettlement(capital, 1);
            D.ReportSettlementCaptured(capital, 2, 1, 500);
            for (int i = 0; i < 10; i++) D.ReportUnitLost(2, 1, 5f);
            _rig.RunSeconds(1);
            Assert.Greater(war.WarScore, 30f);

            var late = D.ProposePeace(1, 2, demand);
            Assert.IsTrue(late.Accepted, late.Reason);
            Assert.AreEqual(1, W.Settlements[extra].OwnerCiv, "Abgetretene Siedlung wechselt den Besitzer");
            Assert.IsFalse(D.AreAtWar(1, 2));
            Assert.AreEqual(1, _rig.Bus.Of<PeaceSignedEvent>().Count);
            Assert.IsFalse(D.CanDeclareWar(1, 2, out _), "Friedensvertrag verhindert sofortigen neuen Krieg");
            _rig.RunSeconds(D.Settings.PeaceCooldownTicks / 10 + 1);
            Assert.IsTrue(D.CanDeclareWar(1, 2, out _), "Nach Ablauf ist ein neuer Krieg wieder möglich");
        }

        [Test]
        public void T402_WhitePeace_RejectedByWinner_AcceptedByLoser()
        {
            W.AddCiv(1, 0, 0); W.AddCiv(2, 40, 0);
            D.DeclareWar(1, 2);
            for (int i = 0; i < 15; i++) D.ReportUnitLost(2, 1, 5f);  // Verteidiger verliert
            _rig.RunSeconds(1);
            Assert.IsFalse(D.ProposePeace(2, 1, PeaceTerm.White()).Accepted, "Gewinner lehnt weißen Frieden ab");
            Assert.IsTrue(D.AreAtWar(1, 2));
            Assert.AreEqual(1, _rig.Bus.Of<PeaceRejectedEvent>().Count);
        }

        [Test]
        public void T402_Tribute_TransfersResources()
        {
            W.AddCiv(1, 0, 0); W.AddCiv(2, 40, 0);
            D.DeclareWar(1, 2);
            for (int i = 0; i < 20; i++) D.ReportUnitLost(2, 1, 5f);
            _rig.RunSeconds(1);
            var r = D.ProposePeace(1, 2, PeaceTerm.Tribute(2, 0.2f));
            Assert.IsTrue(r.Accepted, r.Reason);
            Assert.AreEqual(1200f, W.Civs[1].Stock, 0.01f);
            Assert.AreEqual(800f, W.Civs[2].Stock, 0.01f);
        }

        // ---------- T-4-03 Bündnisse ----------

        [Test]
        public void T403_Alliance_StatusAndBlocksWarBetweenMembers()
        {
            W.AddCiv(1, 0, 0); W.AddCiv(2, 30, 0);
            Assert.IsTrue(D.FormAlliance(1, 2));
            Assert.AreEqual(DiplomaticStatus.Alliance, D.GetStatus(1, 2));
            Assert.IsFalse(D.DeclareWar(1, 2));
            Assert.IsFalse(D.FormAlliance(1, 2), "Doppeltes Bündnis");
        }

        [Test]
        public void T403_AttackOnMember_DrawsPartnerIntoWar_AndBreakingCostsOpinion()
        {
            W.AddCiv(1, 0, 0); W.AddCiv(2, 30, 0); W.AddCiv(3, 60, 0);
            D.FormAlliance(2, 3);
            D.DeclareWar(1, 2);
            Assert.IsTrue(D.AreAtWar(1, 3));
            Assert.AreEqual(1, _rig.Bus.Of<WarJoinedEvent>().Count);

            var treaty = D.GetTreaties(2).Single(t => t.Type == TreatyType.Alliance);
            float before = D.GetOpinion(3, 2);
            Assert.IsTrue(D.BreakTreaty(treaty.Id, 2));
            Assert.Less(D.GetOpinion(3, 2), before);
            Assert.AreNotEqual(DiplomaticStatus.Alliance, D.GetStatus(2, 3));
        }

        [Test]
        public void T403_Ai_FormsAlliance_AgainstCommonThreat()
        {
            W.AddCiv(1, 0, 0); W.AddCiv(2, 20, 0); W.AddCiv(3, 200, 200);
            D.ModifyRelation(1, 2, 40, RelationReason.PlayerIntervention);
            D.ModifyRelation(2, 1, 40, RelationReason.PlayerIntervention);
            D.ModifyRelation(1, 3, -50, RelationReason.PlayerIntervention);
            D.ModifyRelation(2, 3, -50, RelationReason.PlayerIntervention);
            W.Civs[1].Aggression = 0f; W.Civs[2].Aggression = 0f;
            _rig.RunSeconds(5);
            Assert.AreEqual(DiplomaticStatus.Alliance, D.GetStatus(1, 2));
            Assert.IsTrue(D.RecentDecisions.Any(x => x.Action == DiplomaticActionType.ProposeAlliance));
        }

        // ---------- T-4-04 Zielauswahl ----------

        [Test]
        public void T404_TargetPriorities_PreferValuableTargetAtEqualDistance_AndNearerAtEqualValue()
        {
            W.AddCiv(1, 0, 0); W.AddCiv(2, 100, 100);
            int small = W.AddSettlement(2, 40, 0, 100);
            int big = W.AddSettlement(2, 0, 40, 2000);       // gleiche Entfernung, mehr Bevölkerung
            int far = W.AddSettlement(2, 0, 90, 100);         // gleiche Bevölkerung wie small, weiter weg
            D.DeclareWar(1, 2, CasusBelli.Hostility, new System.Collections.Generic.List<WarGoal>(), out _);

            var targets = D.GetTargetPriorities(1).Where(t => t.Kind == TargetKind.Settlement).ToList();
            int iBig = targets.FindIndex(t => t.TargetId == big);
            int iSmall = targets.FindIndex(t => t.TargetId == small);
            int iFar = targets.FindIndex(t => t.TargetId == far);
            Assert.Less(iBig, iSmall, "Wertvolleres Ziel bei gleicher Entfernung zuerst");
            Assert.Less(iSmall, iFar, "Näheres Ziel bei gleichem Wert zuerst");
            Assert.IsEmpty(D.GetTargetPriorities(2).Where(t => t.OwnerCiv == 2), "Keine eigenen Siedlungen als Angriffsziel");
        }

        [Test]
        public void T404_WarGoal_RaisesPriority_AndStrategicPointsAreTargets()
        {
            W.AddCiv(1, 0, 0); W.AddCiv(2, 100, 0);
            int a = W.AddSettlement(2, 30, 0, 300);
            int b = W.AddSettlement(2, 0, 30, 300);
            int point = D.AddStrategicPoint(StrategicPointType.Bridge, 20, 20, 30f);
            var goals = new System.Collections.Generic.List<WarGoal> { new WarGoal { Type = WarGoalType.ConquerSettlement, OwnerCiv = 1, TargetId = b } };
            D.DeclareWar(1, 2, CasusBelli.Hostility, goals, out _);

            var targets = D.GetTargetPriorities(1);
            Assert.AreEqual(b, targets[0].TargetId);
            Assert.IsTrue(targets[0].IsWarGoal);
            Assert.IsTrue(targets.Any(t => t.Kind == TargetKind.StrategicPoint && t.TargetId == point));
        }

        [Test]
        public void T404_Ai_ChoosesWarGoals_FromEnemySettlements()
        {
            W.AddCiv(1, 0, 0); W.AddCiv(2, 40, 0);
            D.DeclareWar(1, 2);
            var war = D.FindWar(1, 2);
            Assert.IsTrue(war.Goals.Any(g => g.Type == WarGoalType.ConquerSettlement && W.Settlements[g.TargetId].OwnerCiv == 2));
        }

        // ---------- T-4-05 Kein Krieg ohne Grund ----------

        [Test]
        public void T405_NoWar_WithoutBorderAndNeutralOpinion_EvenWhenMuchStronger()
        {
            W.AddCiv(1, 0, 0, strength: 1000); W.AddCiv(2, 300, 300, strength: 10);
            _rig.RunSeconds(30 * 60);   // 30 Spielminuten
            Assert.IsEmpty(_rig.Bus.Of<WarDeclaredEvent>());
            Assert.AreEqual(0, D.ActiveWars.Count);
        }

        [Test]
        public void T405_NoWar_WhenWeaker_EvenWithHostileNeighbour()
        {
            W.AddCiv(1, 0, 0, strength: 50); W.AddCiv(2, 20, 0, strength: 200);
            W.SetBorder(1, 2, 60);
            W.Civs[1].Aggression = 1f;
            D.ModifyRelation(1, 2, -80, RelationReason.PlayerIntervention);
            _rig.RunSeconds(10 * 60);
            Assert.IsFalse(D.AreAtWar(1, 2) && D.FindWar(1, 2).AttackerLeader == 1, "Schwächere Civ greift nicht an");
        }

        [Test]
        public void T405_War_WithReason_IsDeclared_AndExplained()
        {
            W.AddCiv(1, 0, 0, strength: 300); W.AddCiv(2, 20, 0, strength: 100);
            W.SetBorder(1, 2, 80);
            W.Contestion[(1, 2)] = 1f;
            W.Civs[1].ResourceNeed = 1f;
            W.Civs[1].Aggression = 0.6f;
            _rig.RunSeconds(10 * 60);
            var war = D.FindWar(1, 2);
            Assert.IsNotNull(war, "Starker, bedürftiger Nachbar mit Grenzkonflikt erklärt Krieg");
            Assert.AreEqual(1, war.AttackerLeader);
            Assert.AreNotEqual(CasusBelli.None, war.CasusBelli);
            var decision = D.RecentDecisions.First(x => x.Action == DiplomaticActionType.DeclareWar);
            Assert.GreaterOrEqual(decision.Utility, D.Settings.WarThreshold);
            Assert.IsNotEmpty(decision.Factors);
        }

        // Sandbox-Regel (INTEGRATION.md §0 Regel 4): Krieg und Frieden entstehen ohne Spielereingabe.
        // Kein Command, keine erzwungene Meinung: nur Weltzustand (Grenze, Ressourcen, Stärke) und gemeldete Kampfverluste.
        [Test]
        public void Autonomy_WarAndPeace_EmergeWithoutPlayerInput()
        {
            W.AddCiv(1, 0, 0, strength: 300); W.AddCiv(2, 20, 0, strength: 100);
            W.AddSettlement(2, 30, 5, 200);
            W.SetBorder(1, 2, 80);
            W.Contestion[(1, 2)] = 1f;
            W.Civs[1].ResourceNeed = 1f;

            int seconds = 0;
            while (D.ActiveWars.Count == 0 && seconds < 30 * 60) { _rig.RunSeconds(1); seconds++; }
            var war = D.FindWar(1, 2);
            Assert.IsNotNull(war, "Krieg muss ohne Eingriff innerhalb von 30 Spielminuten entstehen");
            Assert.AreEqual(1, war.AttackerLeader);
            Assert.IsTrue(D.RecentDecisions.Any(x => x.Action == DiplomaticActionType.DeclareWar && x.CivId == 1));

            // Kampf läuft (in Phase 5 meldet das der UnitStore): der Verteidiger verliert Einheiten.
            seconds = 0;
            while (D.FindWar(1, 2) != null && seconds < 30 * 60)
            {
                if (seconds % 5 == 0) D.ReportUnitLost(2, 1, 4f);
                _rig.RunSeconds(1);
                seconds++;
            }
            Assert.IsNull(D.FindWar(1, 2), "Frieden muss ohne Eingriff aus dem Kriegsverlauf entstehen");
            Assert.AreEqual(1, _rig.Bus.Of<PeaceSignedEvent>().Count);
            Assert.IsTrue(D.RecentDecisions.Any(x => x.Action == DiplomaticActionType.AcceptPeace));
        }

        // Review-Befund: auch gleich starke Nachbarn geraten ohne Eingriff in Krieg (Grenzgroll wächst über die Zeit).
        [TestCase(100f, 100f, 0f, 0f, 0.5f)]
        [TestCase(110f, 100f, 0.5f, 0.5f, 0.5f)]
        [TestCase(130f, 100f, 0.5f, 0.5f, 0.5f)]
        [TestCase(100f, 100f, 1f, 1f, 1f)]
        public void Autonomy_SymmetricNeighbours_EventuallyGoToWar(float s1, float s2, float need, float contestion, float aggression)
        {
            W.AddCiv(1, 0, 0, strength: s1); W.AddCiv(2, 20, 0, strength: s2);
            W.SetBorder(1, 2, 80);
            W.Contestion[(1, 2)] = contestion; W.Contestion[(2, 1)] = contestion;
            W.Civs[1].ResourceNeed = need; W.Civs[2].ResourceNeed = need;
            W.Civs[1].Aggression = aggression; W.Civs[2].Aggression = aggression;

            int seconds = 0;
            while (D.ActiveWars.Count == 0 && seconds < 60 * 60) { _rig.RunSeconds(1); seconds++; }
            Assert.AreEqual(1, D.ActiveWars.Count, "Krieg innerhalb von 60 Spielminuten ohne Spielereingabe");
            var decision = D.RecentDecisions.First(x => x.Action == DiplomaticActionType.DeclareWar);
            Assert.GreaterOrEqual(decision.Utility, D.Settings.WarThreshold);
        }

        [Test]
        public void Grievance_IsCleared_ByPeace()
        {
            W.AddCiv(1, 0, 0); W.AddCiv(2, 20, 0);
            W.Civs[1].Aggression = 0f; W.Civs[2].Aggression = 0f;
            W.SetBorder(1, 2, 80);
            D.Settings.WarCooldownTicks = int.MaxValue / 2;   // nur Groll beobachten, keine KI-Kriege
            _rig.Service.GetMemory(1).LastWarTick = 0; _rig.Service.GetMemory(2).LastWarTick = 0;
            _rig.RunSeconds(600);
            Assert.Less(D.GetModifiers(1, 2).Where(m => m.Reason == RelationReason.BorderGrievance).Sum(m => m.Value), -10f);
            D.DeclareWar(1, 2);
            D.MakePeace(1, 2);
            Assert.IsFalse(D.GetModifiers(1, 2).Any(m => m.Reason == RelationReason.BorderGrievance));
            Assert.IsFalse(D.GetModifiers(2, 1).Any(m => m.Reason == RelationReason.BorderGrievance));
        }

        // Review-Befund: Verluste kommen über den echten Event-Bus vom UnitStore an.
        [Test]
        public void UnitDiedEvent_FromRealUnitStore_CountsAsWarLoss()
        {
            var bus = new WorldBeyond.Core.SimEventBus();
            var types = new WorldBeyond.Simulation.Units.UnitTypeRegistry(new WorldBeyond.Core.ContentRegistry());
            var type = types.Register(new WorldBeyond.Simulation.Units.UnitTypeData { Key = "unit.test_soldier", DisplayName = "Test" });
            var units = new WorldBeyond.Simulation.Units.UnitStore(types, bus);
            var world = new FakeDiplomacyWorld { UnitStrength = 7f };
            world.AddCiv(1, 0, 0); world.AddCiv(2, 40, 0);
            var d = new DiplomacyService(world, bus, () => 0);
            d.DeclareWar(1, 2);
            bus.Flush();

            var soldier = units.Spawn(type, 2, 5f, 5f);
            Assert.IsTrue(units.Despawn(soldier, default, 1));
            bus.Flush();

            var war = d.FindWar(1, 2);
            Assert.AreEqual(1, war.DefenderStats.UnitsLost);
            Assert.AreEqual(7f, war.DefenderStats.StrengthLost, 0.001f);
            Assert.AreEqual(0, war.AttackerStats.UnitsLost);
        }

        // ---------- T-4-06 Verluste ----------

        [Test]
        public void T406_Losses_RaiseExhaustion_AndLowerWarScore()
        {
            W.AddCiv(1, 0, 0); W.AddCiv(2, 40, 0);
            D.DeclareWar(1, 2);
            var war = D.FindWar(1, 2);
            _rig.RunSeconds(1);
            float score0 = war.WarScore, exh0 = war.AttackerStats.Exhaustion;
            for (int i = 0; i < 10; i++) D.ReportUnitLost(1, 2, 5f);
            _rig.RunSeconds(1);
            Assert.AreEqual(10, war.AttackerStats.UnitsLost);
            Assert.Greater(war.AttackerStats.Exhaustion, exh0);
            Assert.Less(war.WarScore, score0);
        }

        [Test]
        public void T406_Losses_OfUninvolvedCivs_AreIgnored()
        {
            W.AddCiv(1, 0, 0); W.AddCiv(2, 40, 0); W.AddCiv(3, 80, 0);
            D.DeclareWar(1, 2);
            D.ReportUnitLost(3, 1, 5f);
            var war = D.FindWar(1, 2);
            Assert.AreEqual(0, war.AttackerStats.UnitsLost + war.DefenderStats.UnitsLost);
        }

        [Test]
        public void T406_LosingAi_ProposesPeace_WhichWinnerOnlyAcceptsWithConcession()
        {
            W.AddCiv(1, 0, 0); W.AddCiv(2, 40, 0);
            W.AddSettlement(2, 50, 0, 100);
            W.Civs[1].Aggression = 0f; W.Civs[2].Aggression = 0f;
            D.DeclareWar(1, 2);
            for (int i = 0; i < 40; i++) D.ReportUnitLost(2, 1, 5f);
            _rig.RunSeconds(10);
            Assert.IsTrue(D.RecentDecisions.Any(x => x.Action == DiplomaticActionType.ProposePeace));
            Assert.IsNull(D.FindWar(1, 2), "Frieden wurde auf Grundlage des Kriegsverlaufs geschlossen");
            var peace = _rig.Bus.Of<PeaceSignedEvent>().Single();
            Assert.AreNotEqual(PeaceTermType.WhitePeace, peace.Term, "Verlierer musste etwas abgeben");
        }

        // ---------- T-4-07 Speichern/Laden während des Kriegs ----------

        [Test]
        public void T407_SaveLoad_DuringWar_ContinuesIdentically()
        {
            var a = BuildScenario();
            a.RunSeconds(120);
            Assert.IsTrue(a.Service.ActiveWars.Count > 0, "Szenario muss während des Kriegs gespeichert werden");
            var saveA = new DiplomacySaveable(a.Service);
            // Hinweis: JSON-Umwandlung gehört dem SaveLoadService (Phase 7). Hier: DTO-Rundlauf in eine neue Instanz.
            var dto = (DiplomacyStateDto)saveA.CaptureState();
            string atSave = StateDump.Of(dto);

            var b = BuildScenario();
            b.Tick = a.Tick;
            CopyWorld(a.World, b.World);
            var saveB = new DiplomacySaveable(b.Service);
            Assert.IsTrue(saveB.TryRestoreState(dto, out string error), error);
            var report = new LoadReport();
            saveB.CheckReferences(null, report);
            Assert.IsEmpty(report.Warnings);
            Assert.AreEqual(atSave, StateDump.Of(saveB.CaptureState()), "Wiederhergestellter Zustand gleicht dem gespeicherten");

            for (int i = 0; i < 10; i++) { a.Service.ReportUnitLost(2, 1, 3f); b.Service.ReportUnitLost(2, 1, 3f); }
            a.RunSeconds(300);
            b.RunSeconds(300);
            Assert.AreEqual(StateDump.Of(saveA.CaptureState()), StateDump.Of(saveB.CaptureState()));
            Assert.AreNotEqual(atSave, StateDump.Of(saveB.CaptureState()), "Simulation lief nach dem Laden weiter");
        }

        [Test]
        public void T407_CapturedState_IsIndependentCopy()
        {
            W.AddCiv(1, 0, 0); W.AddCiv(2, 40, 0);
            D.DeclareWar(1, 2);
            var save = new DiplomacySaveable(D);
            var dto = (DiplomacyStateDto)save.CaptureState();
            string before = StateDump.Of(dto);
            D.ReportUnitLost(1, 2, 5f);
            _rig.RunSeconds(3);
            Assert.AreEqual(before, StateDump.Of(dto));
        }

        [Test]
        public void T407_Restore_RejectsDuplicateIds_AndKeepsPreviousState()
        {
            W.AddCiv(1, 0, 0); W.AddCiv(2, 40, 0);
            D.DeclareWar(1, 2);
            var save = new DiplomacySaveable(D);
            var dto = (DiplomacyStateDto)save.CaptureState();
            dto.Wars.Add(dto.Wars[0]);
            Assert.IsFalse(save.TryRestoreState(dto, out string error));
            StringAssert.Contains("doppelt", error);
            Assert.AreEqual(1, D.ActiveWars.Count);
        }

        [Test]
        public void T407_Restore_RejectsIdAboveAllocator()
        {
            W.AddCiv(1, 0, 0); W.AddCiv(2, 40, 0);
            D.DeclareWar(1, 2);
            var save = new DiplomacySaveable(D);
            var dto = (DiplomacyStateDto)save.CaptureState();
            dto.LastWarId = 0;
            Assert.IsFalse(save.TryRestoreState(dto, out _));
        }

        [Test]
        public void T407_ReferenceCheck_CollapsedCiv_EndsWar_WithWarning()
        {
            W.AddCiv(1, 0, 0); W.AddCiv(2, 40, 0);
            D.DeclareWar(1, 2);
            W.Kill(2);
            var report = new LoadReport();
            new DiplomacySaveable(D).CheckReferences(null, report);
            Assert.IsNotEmpty(report.Warnings);
            Assert.IsFalse(report.HasErrors);
            Assert.AreEqual(0, D.ActiveWars.Count);
            Assert.AreEqual(WarEndReason.CivCollapsed, D.EndedWars.Last().EndReason);
        }

        [Test]
        public void Ids_AreNeverReused_AfterWarEnds()
        {
            W.AddCiv(1, 0, 0); W.AddCiv(2, 40, 0);
            D.DeclareWar(1, 2);
            int first = D.FindWar(1, 2).Id;
            D.MakePeace(1, 2);
            _rig.RunSeconds(D.Settings.PeaceCooldownTicks / 10 + 1);
            D.DeclareWar(1, 2);
            Assert.Greater(D.FindWar(1, 2).Id, first);
        }

        // ---------- Waffenstillstand, Zusammenbruch, strategische Punkte, Rekrutierung, Befehle ----------

        [Test]
        public void Truce_SuspendsWar_AndWarResumesAfterExpiry()
        {
            W.AddCiv(1, 0, 0); W.AddCiv(2, 40, 0);
            D.DeclareWar(1, 2);
            Assert.IsTrue(D.SignTruce(1, 2, 100));
            Assert.AreEqual(DiplomaticStatus.Truce, D.GetStatus(1, 2));
            Assert.IsFalse(D.AreAtWar(1, 2));
            Assert.IsEmpty(D.GetTargetPriorities(1), "Im Waffenstillstand keine Angriffsziele");
            _rig.RunSeconds(11);
            Assert.IsTrue(D.AreAtWar(1, 2));
            Assert.AreEqual(1, _rig.Bus.Of<WarResumedEvent>().Count);
        }

        [Test]
        public void Collapse_OfLeader_PassesLeadership_OrEndsWar()
        {
            W.AddCiv(1, 0, 0); W.AddCiv(2, 40, 0); W.AddCiv(3, 80, 0);
            D.FormAlliance(2, 3);
            D.DeclareWar(1, 2);
            W.Kill(2);
            D.ReportCivilizationCollapsed(2);
            var war = D.FindWar(1, 3);
            Assert.IsNotNull(war);
            Assert.AreEqual(3, war.DefenderLeader);
            W.Kill(3);
            D.ReportCivilizationCollapsed(3);
            Assert.AreEqual(0, D.ActiveWars.Count);
            Assert.IsEmpty(D.GetTreaties(3));
        }

        [Test]
        public void StrategicPoint_Capture_ChangesController_AndWarScore()
        {
            W.AddCiv(1, 0, 0); W.AddCiv(2, 40, 0);
            int p = D.AddStrategicPoint(StrategicPointType.Pass, 20, 0, 20f);
            D.DeclareWar(1, 2);
            W.DominantAt[(20, 0)] = 1;
            _rig.RunSeconds(1);
            Assert.AreEqual(1, D.GetStrategicPoint(p).ControllerCiv);
            Assert.AreEqual(1, _rig.Bus.Of<StrategicPointCapturedEvent>().Count);
            Assert.Greater(D.FindWar(1, 2).WarScore, 0f);
        }

        [Test]
        public void Ai_RequestsRecruitment_WhenThreatened()
        {
            W.AddCiv(1, 0, 0, strength: 50); W.AddCiv(2, 20, 0, strength: 300);
            D.DeclareWar(2, 1);
            _rig.RunSeconds(5);
            Assert.IsTrue(W.RecruitRequests.Any(r => r.civ == 1));
        }

        [Test]
        public void BorderFriction_LowersOpinion_AndCausesTension()
        {
            W.AddCiv(1, 0, 0); W.AddCiv(2, 20, 0);
            W.SetBorder(1, 2, 100);
            W.Contestion[(1, 2)] = 1f; W.Contestion[(2, 1)] = 1f;
            _rig.RunSeconds(6);
            Assert.Less(D.GetOpinion(1, 2), -30f);
            Assert.AreEqual(DiplomaticStatus.Tension, D.GetStatus(1, 2));
            W.SetBorder(1, 2, 0); W.Contestion.Clear();
            _rig.RunSeconds(6);
            Assert.Greater(D.GetOpinion(1, 2), -1f, "Dauerhafte Faktoren werden ersetzt, nicht aufaddiert; Groll baut sich ab");
        }

        [Test]
        public void Commands_ValidateAndExecute()
        {
            W.AddCiv(1, 0, 0); W.AddCiv(2, 40, 0);
            D.FormAlliance(1, 2);
            var plain = new DeclareWarCommand(D, 1, 2, false);
            Assert.IsFalse(plain.Validate(null, out string reason));
            Assert.IsNotNull(reason);
            var force = new DeclareWarCommand(D, 1, 2, true);
            Assert.IsTrue(force.Validate(null, out _));
            force.Execute(null);
            Assert.IsTrue(D.AreAtWar(1, 2));
            Assert.IsTrue(D.GetModifiers(2, 1).Any(m => m.Reason == RelationReason.TreatyBroken));

            var peace = new ProposePeaceCommand(D, 1, 2, PeaceTerm.White(), true);
            Assert.IsTrue(peace.Validate(null, out _));
            peace.Execute(null);
            Assert.IsFalse(D.AreAtWar(1, 2));
        }

        [Test]
        public void Overview_ListsWarsAlliancesAndHostilities()
        {
            W.AddCiv(1, 0, 0); W.AddCiv(2, 40, 0); W.AddCiv(3, 80, 0);
            D.FormAlliance(2, 3);
            D.DeclareWar(1, 2);
            var o = DiplomacyOverview.Build(D, _rig.Tick);
            Assert.AreEqual(1, o.Wars.Count);
            Assert.AreEqual(1, o.Alliances.Count);
            Assert.AreEqual(2, o.Hostilities.Count(h => h.Status == DiplomaticStatus.War));
        }

        // ---------- Hilfen ----------

        static DiplomacyTestRig BuildScenario()
        {
            var r = new DiplomacyTestRig();
            r.World.AddCiv(1, 0, 0, strength: 500);   // stark genug trotz Verbündetem von 2
            r.World.AddCiv(2, 20, 0, strength: 100);
            r.World.AddCiv(3, 60, 0, strength: 120);
            r.World.AddSettlement(2, 30, 5, 300);
            r.World.SetBorder(1, 2, 80);
            r.World.SetBorder(2, 3, 20);
            r.World.Contestion[(1, 2)] = 1f;
            r.World.Civs[1].ResourceNeed = 1f;
            r.World.Civs[1].Aggression = 0.6f;
            r.Service.AddStrategicPoint(StrategicPointType.Bridge, 10, 0, 20f);
            r.Service.FormAlliance(2, 3);
            return r;
        }

        static void CopyWorld(FakeDiplomacyWorld from, FakeDiplomacyWorld to)
        {
            to.Settlements.Clear();
            foreach (var kv in from.Settlements) to.Settlements[kv.Key] = kv.Value;
            foreach (var kv in from.Civs) { to.Civs[kv.Key].Stock = kv.Value.Stock; to.Civs[kv.Key].Alive = kv.Value.Alive; }
        }
    }
}
