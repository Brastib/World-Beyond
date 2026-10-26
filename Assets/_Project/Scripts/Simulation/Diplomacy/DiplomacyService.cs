using System;
using System.Collections.Generic;
using WorldBeyond.Core;
using WorldBeyond.Simulation.Units;

namespace WorldBeyond.Simulation.Diplomacy
{
    // Einzige Beziehungsmatrix und Kriegsverwaltung (Phase 4).
    // Zustand: Meinungsmodifikatoren, Verträge, Kriege, strategische Punkte, KI-Gedächtnis.
    // Speichern: DiplomacySaveable. Takt: DiplomacySystem. KI: WarStrategyEvaluator.
    public sealed partial class DiplomacyService : IDiplomacyService, IDiplomacyQuery, IWarQuery
    {
        readonly IDiplomacyWorldPort _world;
        readonly ISimEventBus _events;
        readonly Func<long> _now;
        public readonly DiplomacySettings Settings;

        internal SequentialIdAllocator WarIds = new SequentialIdAllocator();
        internal SequentialIdAllocator TreatyIds = new SequentialIdAllocator();
        internal SequentialIdAllocator ModifierIds = new SequentialIdAllocator();
        internal SequentialIdAllocator PointIds = new SequentialIdAllocator();

        internal readonly List<OpinionModifier> Modifiers = new List<OpinionModifier>();   // nach Id sortiert (nur angehängt)
        internal readonly List<Treaty> Treaties = new List<Treaty>();                     // nur aktive
        internal readonly List<War> Wars = new List<War>();                               // aktive + ruhende
        internal readonly List<War> WarHistory = new List<War>();                         // beendete, begrenzt
        internal readonly List<StrategicPoint> Points = new List<StrategicPoint>();
        internal readonly List<DiplomaticDecision> Decisions = new List<DiplomaticDecision>();
        internal readonly List<CivAiMemory> Memory = new List<CivAiMemory>();              // nach CivId sortiert
        internal long LastFactorRefreshTick = long.MinValue / 4;
        internal long LastUpdateTick = -1;

        public const int MaxWarHistory = 50;

        public DiplomacyService(IDiplomacyWorldPort world, ISimEventBus events, Func<long> currentTick, DiplomacySettings settings = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _now = currentTick ?? throw new ArgumentNullException(nameof(currentTick));
            Settings = settings ?? new DiplomacySettings();
            _events.Subscribe<UnitDiedEvent>(OnUnitDied);
        }

        // Verluste kommen direkt vom UnitStore (UnitDiedEvent enthält CivId, KillerCivId und UnitType).
        void OnUnitDied(UnitDiedEvent e)
        {
            if (e.CivId <= 0 || e.KillerCivId <= 0) return;
            ReportUnitLost(e.CivId, e.KillerCivId, _world.GetUnitCombatStrength(e.UnitType.Value));
        }

        public IDiplomacyWorldPort World => _world;
        internal long Now => _now();

        // ================= Meinung =================

        public float GetOpinion(int civA, int civB)
        {
            if (civA == civB) return 100f;
            float sum = 0f;
            for (int i = 0; i < Modifiers.Count; i++)
            {
                var m = Modifiers[i];
                if (m.FromCiv == civA && m.ToCiv == civB) sum += m.Value;
            }
            return Clamp(sum, -100f, 100f);
        }

        public int GetRelation(int civA, int civB) => (int)Math.Round(GetOpinion(civA, civB), MidpointRounding.AwayFromZero);

        public IReadOnlyList<OpinionModifier> GetModifiers(int civA, int civB)
        {
            var list = new List<OpinionModifier>();
            for (int i = 0; i < Modifiers.Count; i++)
                if (Modifiers[i].FromCiv == civA && Modifiers[i].ToCiv == civB) list.Add(Modifiers[i]);
            return list;
        }

        // Meinung von A über B ändert sich um delta (Ereignis, klingt ab).
        public void ModifyRelation(int civA, int civB, int delta, RelationReason reason)
        {
            AddModifier(civA, civB, reason, delta, reason == RelationReason.PlayerIntervention ? 0f : Settings.DefaultEventDecay);
        }

        internal void AddModifier(int from, int to, RelationReason reason, float value, float decayPerSecond)
        {
            if (from == to || from <= 0 || to <= 0 || value == 0f) return;
            Modifiers.Add(new OpinionModifier
            {
                Id = ModifierIds.Next(), FromCiv = from, ToCiv = to, Reason = reason, Value = value,
                DecayPerSecond = Math.Abs(decayPerSecond), CreatedTick = Now,
            });
        }

        // Dauerhafte Faktoren (Grenze, Ressourcen): genau ein Modifikator pro Richtung und Grund, wird ersetzt.
        internal void SetPersistentModifier(int from, int to, RelationReason reason, float value)
        {
            for (int i = 0; i < Modifiers.Count; i++)
            {
                var m = Modifiers[i];
                if (m.FromCiv == from && m.ToCiv == to && m.Reason == reason && m.DecayPerSecond == 0f)
                {
                    if (value == 0f) Modifiers.RemoveAt(i);
                    else m.Value = value;
                    return;
                }
            }
            if (value != 0f) AddModifier(from, to, reason, value, 0f);
        }

        // Nähe zweier Civs 0 … 1: mit Grenze 0,5 + Grenzlänge/80; sonst bis 0,7 nach Abstand der Gebietsränder
        // (Hauptstadtabstand minus geschätzte Gebietsradien), 0 ab ContactReachTiles.
        public float ContactProximity(int a, int b, int border)
        {
            if (border > 0) return Clamp01(0.5f + border / 80f);
            if (!_world.TryGetCapitalPosition(a, out int ax, out int ay) || !_world.TryGetCapitalPosition(b, out int bx, out int by)) return 0f;
            float gap = Distance(ax, ay, bx, by) - TerritoryRadius(a) - TerritoryRadius(b);
            return 0.7f * Clamp01(1f - Math.Max(0f, gap) / Settings.ContactReachTiles);
        }

        float TerritoryRadius(int civ) => (float)Math.Sqrt(Math.Max(0, _world.GetTerritoryTiles(civ)) / Math.PI);

        internal float GetPersistentValue(int from, int to, RelationReason reason)
        {
            foreach (var m in Modifiers)
                if (m.FromCiv == from && m.ToCiv == to && m.Reason == reason && m.DecayPerSecond == 0f) return m.Value;
            return 0f;
        }

        // Nach einem Frieden ist der angestaute Grenzgroll zwischen den Parteien abgebaut.
        void ClearGrievance(int a, int b)
        {
            SetPersistentModifier(a, b, RelationReason.BorderGrievance, 0f);
            SetPersistentModifier(b, a, RelationReason.BorderGrievance, 0f);
        }

        // ================= Status =================

        public DiplomaticStatus GetStatus(int civA, int civB)
        {
            if (civA == civB) return DiplomaticStatus.Neutral;
            var war = FindWar(civA, civB);
            if (war != null) return war.Suspended ? DiplomaticStatus.Truce : DiplomaticStatus.War;
            if (FindTreaty(civA, civB, TreatyType.Alliance) != null) return DiplomaticStatus.Alliance;
            if (FindTreaty(civA, civB, TreatyType.Truce) != null) return DiplomaticStatus.Truce;
            float worst = Math.Min(GetOpinion(civA, civB), GetOpinion(civB, civA));
            if (worst <= Settings.TensionThreshold) return DiplomaticStatus.Tension;
            // Trade: kommt mit dem Handelssystem (noch nicht vorhanden).
            return DiplomaticStatus.Neutral;
        }

        public IReadOnlyList<Treaty> GetTreaties(int civId)
        {
            var list = new List<Treaty>();
            foreach (var t in Treaties) if (t.Active && (t.CivA == civId || t.CivB == civId)) list.Add(t);
            return list;
        }

        public IReadOnlyList<int> GetAllies(int civId)
        {
            var list = new List<int>();
            foreach (var t in Treaties)
            {
                if (!t.Active || t.Type != TreatyType.Alliance) continue;
                if (t.CivA == civId) list.Add(t.CivB);
                else if (t.CivB == civId) list.Add(t.CivA);
            }
            list.Sort();
            return list;
        }

        public bool AreAllied(int a, int b) => FindTreaty(a, b, TreatyType.Alliance) != null;

        internal Treaty FindTreaty(int a, int b, TreatyType type)
        {
            foreach (var t in Treaties)
                if (t.Active && t.Type == type && ((t.CivA == a && t.CivB == b) || (t.CivA == b && t.CivB == a))) return t;
            return null;
        }

        public IReadOnlyList<DiplomaticDecision> RecentDecisions => Decisions;
        public IReadOnlyList<StrategicPoint> StrategicPoints => Points;
        public IReadOnlyList<War> EndedWars => WarHistory;

        // ================= Kriege: Abfragen =================

        public IReadOnlyList<War> ActiveWars => Wars;

        public War GetWar(int warId)
        {
            foreach (var w in Wars) if (w.Id == warId) return w;
            foreach (var w in WarHistory) if (w.Id == warId) return w;
            return null;
        }

        public War FindWar(int civA, int civB)
        {
            foreach (var w in Wars)
            {
                var sa = w.SideOf(civA);
                var sb = w.SideOf(civB);
                if (sa != WarSide.None && sb != WarSide.None && sa != sb) return w;
            }
            return null;
        }

        public bool AreAtWar(int civA, int civB)
        {
            var w = FindWar(civA, civB);
            return w != null && !w.Suspended;
        }

        public bool IsAtWar(int civId)
        {
            foreach (var w in Wars) if (!w.Suspended && w.SideOf(civId) != WarSide.None) return true;
            return false;
        }

        public List<int> GetEnemies(int civId)
        {
            var list = new List<int>();
            foreach (var w in Wars)
            {
                if (w.Suspended) continue;
                var side = w.SideOf(civId);
                if (side == WarSide.None) continue;
                foreach (var e in w.MembersOf(side == WarSide.Attacker ? WarSide.Defender : WarSide.Attacker))
                    if (!list.Contains(e)) list.Add(e);
            }
            list.Sort();
            return list;
        }

        // ================= Kriegserklärung =================

        public bool DeclareWar(int attacker, int defender) => DeclareWar(attacker, defender, CasusBelli.PlayerForced, null, out _);

        public bool CanDeclareWar(int attacker, int defender, out string reason)
        {
            reason = null;
            if (attacker == defender) { reason = "Eine Zivilisation kann sich nicht selbst den Krieg erklären."; return false; }
            if (!_world.IsCivAlive(attacker) || !_world.IsCivAlive(defender)) { reason = "Zivilisation existiert nicht."; return false; }
            if (FindWar(attacker, defender) != null) { reason = "Die beiden befinden sich bereits im Krieg oder Waffenstillstand."; return false; }
            if (FindTreaty(attacker, defender, TreatyType.Alliance) != null) { reason = "Bündnis besteht. Zuerst Bündnis brechen."; return false; }
            if (FindTreaty(attacker, defender, TreatyType.Truce) != null) { reason = "Waffenstillstand besteht. Zuerst Vertrag brechen."; return false; }
            if (FindTreaty(attacker, defender, TreatyType.PeaceCooldown) != null) { reason = "Friedensvertrag ist noch gültig. Zuerst Vertrag brechen."; return false; }
            return true;
        }

        public bool DeclareWar(int attacker, int defender, CasusBelli casusBelli, List<WarGoal> goals, out string reason)
        {
            if (!CanDeclareWar(attacker, defender, out reason)) return false;

            var war = new War
            {
                Id = WarIds.Next(), AttackerLeader = attacker, DefenderLeader = defender,
                StartTick = Now, CasusBelli = casusBelli,
            };
            war.Attackers.Add(attacker);
            war.Defenders.Add(defender);
            war.AttackerStats.InitialStrength = _world.GetMilitaryStrength(attacker);
            war.AttackerStats.InitialPopulation = _world.GetPopulation(attacker);
            war.DefenderStats.InitialStrength = _world.GetMilitaryStrength(defender);
            war.DefenderStats.InitialPopulation = _world.GetPopulation(defender);
            war.Goals.AddRange(goals ?? ChooseWarGoals(attacker, defender));
            Wars.Add(war);

            AddModifier(defender, attacker, RelationReason.WarDeclaredOnUs, Settings.WarDeclaredOnUsValue, Settings.WarDeclaredOnUsDecay);
            _events.Publish(new WarDeclaredEvent { WarId = war.Id, Attacker = attacker, Defender = defender, CasusBelli = casusBelli });

            CallAllies(war);
            return true;
        }

        // Bündnispflichten: Verbündete des Verteidigers treten bei, Verbündete des Angreifers nur, wenn sie den Verteidiger nicht mögen.
        void CallAllies(War war)
        {
            int att = war.AttackerLeader, def = war.DefenderLeader;
            foreach (var ally in GetAllies(def))
            {
                if (ally == att || AreAllied(ally, att) || !_world.IsCivAlive(ally)) continue;
                if (FindWar(ally, att) != null || war.SideOf(ally) != WarSide.None) continue;
                JoinWar(war, ally, WarSide.Defender);
                AddModifier(ally, att, RelationReason.AttackedAlly, Settings.AttackedAllyValue, Settings.DefaultEventDecay);
            }
            foreach (var ally in GetAllies(att))
            {
                if (ally == def || AreAllied(ally, def) || !_world.IsCivAlive(ally)) continue;
                if (FindWar(ally, def) != null || war.SideOf(ally) != WarSide.None) continue;
                if (FindTreaty(ally, def, TreatyType.Truce) != null || FindTreaty(ally, def, TreatyType.PeaceCooldown) != null) continue;
                float opinionOfDefender = GetOpinion(ally, def);
                if (opinionOfDefender < 0f)
                {
                    JoinWar(war, ally, WarSide.Attacker);
                    LogDecision(ally, def, DiplomaticActionType.JoinWar, 1f, new DecisionFactor("Meinung über Gegner", opinionOfDefender));
                }
                else
                {
                    AddModifier(att, ally, RelationReason.RefusedCallToArms, Settings.RefusedCallToArmsValue, Settings.DefaultEventDecay);
                    LogDecision(ally, def, DiplomaticActionType.DeclineCallToArms, 0f, new DecisionFactor("Meinung über Gegner", opinionOfDefender));
                }
            }
        }

        void JoinWar(War war, int civ, WarSide side)
        {
            war.MembersOf(side).Add(civ);
            var stats = war.StatsOf(side);
            stats.InitialStrength += _world.GetMilitaryStrength(civ);
            stats.InitialPopulation += _world.GetPopulation(civ);
            foreach (var m in war.MembersOf(side))
                if (m != civ) AddModifier(m, civ, RelationReason.CommonEnemy, Settings.CommonEnemyValue, Settings.DefaultEventDecay);
            _events.Publish(new WarJoinedEvent { WarId = war.Id, CivId = civ, Side = side });
        }

        // Kriegsziele: wertvollste gegnerische Siedlung in Reichweite + umkämpfter strategischer Punkt, sonst Demütigung.
        public List<WarGoal> ChooseWarGoals(int attacker, int defender)
        {
            var goals = new List<WarGoal>();
            bool hasCapital = _world.TryGetCapitalPosition(attacker, out int cx, out int cy);
            int bestId = 0; float bestValue = float.MinValue;
            foreach (var sid in _world.GetSettlements(defender))
            {
                if (!_world.TryGetSettlement(sid, out var s)) continue;
                float value = SettlementValue(s, false);
                if (hasCapital) value /= 1f + Distance(cx, cy, s.X, s.Y) / Settings.TargetDistanceScale;
                if (value > bestValue || (value == bestValue && sid < bestId)) { bestValue = value; bestId = sid; }
            }
            if (bestId != 0) goals.Add(new WarGoal { Type = WarGoalType.ConquerSettlement, OwnerCiv = attacker, TargetId = bestId });

            if (hasCapital)
            {
                StrategicPoint bestPoint = null; float bestPv = float.MinValue;
                foreach (var p in Points)
                {
                    if (p.ControllerCiv == attacker) continue;
                    float d = Distance(cx, cy, p.X, p.Y);
                    if (d > Settings.MaxReachTiles) continue;
                    float v = p.Value / (1f + d / Settings.TargetDistanceScale);
                    if (v > bestPv) { bestPv = v; bestPoint = p; }
                }
                if (bestPoint != null) goals.Add(new WarGoal { Type = WarGoalType.HoldPoint, OwnerCiv = attacker, TargetId = bestPoint.Id });
            }
            if (goals.Count == 0) goals.Add(new WarGoal { Type = WarGoalType.Humiliate, OwnerCiv = attacker });
            return goals;
        }

        internal static float SettlementValue(in SettlementInfo s, bool isGoal)
        {
            return 10f + s.Population * 0.05f + (s.IsCapital ? 25f : 0f) + (isGoal ? 40f : 0f);
        }

        // ================= Verträge =================

        public bool FormAlliance(int civA, int civB)
        {
            if (civA == civB || !_world.IsCivAlive(civA) || !_world.IsCivAlive(civB)) return false;
            if (FindWar(civA, civB) != null || AreAllied(civA, civB)) return false;
            var t = AddTreaty(TreatyType.Alliance, civA, civB, -1, 0);
            AddModifier(civA, civB, RelationReason.AllianceFormed, Settings.AllianceFormedValue, Settings.DefaultEventDecay);
            AddModifier(civB, civA, RelationReason.AllianceFormed, Settings.AllianceFormedValue, Settings.DefaultEventDecay);
            _events.Publish(new AllianceFormedEvent { TreatyId = t.Id, CivA = civA, CivB = civB });
            return true;
        }

        // Waffenstillstand. Im Krieg nur zwischen den beiden Kriegsführern; der Krieg ruht bis zum Ablauf oder Frieden.
        public bool SignTruce(int civA, int civB, int durationTicks)
        {
            if (civA == civB || durationTicks <= 0) return false;
            if (!_world.IsCivAlive(civA) || !_world.IsCivAlive(civB)) return false;
            if (FindTreaty(civA, civB, TreatyType.Truce) != null) return false;
            var war = FindWar(civA, civB);
            int suspended = 0;
            if (war != null)
            {
                if (war.Suspended) return false;
                bool leaders = (war.AttackerLeader == civA && war.DefenderLeader == civB) || (war.AttackerLeader == civB && war.DefenderLeader == civA);
                if (!leaders) return false;
                war.Suspended = true;
                suspended = war.Id;
            }
            var t = AddTreaty(TreatyType.Truce, civA, civB, Now + durationTicks, suspended);
            _events.Publish(new TruceSignedEvent { TreatyId = t.Id, CivA = civA, CivB = civB, EndTick = t.EndTick, SuspendedWarId = suspended });
            return true;
        }

        // Vertragsbruch: kostet Meinung beim Partner und bei allen anderen. Ein ruhender Krieg läuft sofort weiter.
        public bool BreakTreaty(int treatyId, int breakerCiv)
        {
            Treaty t = null;
            foreach (var x in Treaties) if (x.Id == treatyId && x.Active) t = x;
            if (t == null || (t.CivA != breakerCiv && t.CivB != breakerCiv)) return false;
            int other = t.CivA == breakerCiv ? t.CivB : t.CivA;
            EndTreaty(t, TreatyEndReason.Broken);
            AddModifier(other, breakerCiv, RelationReason.TreatyBroken, Settings.TreatyBrokenValue, Settings.TreatyBrokenDecay);
            foreach (var c in _world.CivIds)
                if (c != other && c != breakerCiv) AddModifier(c, breakerCiv, RelationReason.TreatyBroken, Settings.TreatyBrokenObserverValue, Settings.TreatyBrokenDecay);
            _events.Publish(new TreatyBrokenEvent { TreatyId = t.Id, BreakerCiv = breakerCiv, OtherCiv = other, Type = t.Type });
            if (t.SuspendedWarId != 0) ResumeWar(t);
            return true;
        }

        // God-Modus: Krieg erzwingen, blockierende Verträge werden vom Angreifer gebrochen.
        public bool ForceWar(int attacker, int defender, out string reason)
        {
            reason = null;
            var war = FindWar(attacker, defender);
            if (war != null && !war.Suspended) { reason = "Die beiden befinden sich bereits im Krieg."; return false; }
            foreach (var type in new[] { TreatyType.Alliance, TreatyType.Truce, TreatyType.PeaceCooldown })
            {
                var t = FindTreaty(attacker, defender, type);
                if (t != null) BreakTreaty(t.Id, attacker);
            }
            if (FindWar(attacker, defender) is War resumed && !resumed.Suspended) return true;  // ruhender Krieg lief wieder an
            return DeclareWar(attacker, defender, CasusBelli.PlayerForced, null, out reason);
        }

        Treaty AddTreaty(TreatyType type, int a, int b, long endTick, int suspendedWarId)
        {
            var t = new Treaty
            {
                Id = TreatyIds.Next(), Type = type, CivA = Math.Min(a, b), CivB = Math.Max(a, b),
                StartTick = Now, EndTick = endTick, SuspendedWarId = suspendedWarId, Active = true,
            };
            Treaties.Add(t);
            return t;
        }

        void EndTreaty(Treaty t, TreatyEndReason reason)
        {
            t.Active = false;
            t.EndReason = reason;
            Treaties.Remove(t);
        }

        void ResumeWar(Treaty truce)
        {
            var war = GetWar(truce.SuspendedWarId);
            if (war == null || !war.Active) return;
            war.Suspended = false;
            _events.Publish(new WarResumedEvent { WarId = war.Id, TreatyId = truce.Id });
        }

        // ================= Frieden =================

        // Erzwungener weißer Frieden (God-Modus / IDiplomacyService).
        public bool MakePeace(int civA, int civB)
        {
            var war = FindWar(civA, civB);
            if (war == null) return false;
            ApplyPeace(war, civA, civB, PeaceTerm.White());
            return true;
        }

        // Annahmewert des Empfängers (> 0 = annehmen). Berücksichtigt Kriegsverlauf, Erschöpfung, Forderungen, weitere Kriege.
        public float EvaluatePeaceAcceptance(War war, int proposer, int receiver, PeaceTerm term, out string reason)
        {
            reason = null;
            float scoreR = war.ScoreFor(receiver);
            float exhaustionR = war.StatsOf(war.SideOf(receiver)).Exhaustion;
            float cost = 0f;
            if (term.Type != PeaceTermType.WhitePeace)
            {
                float termValue = TermValue(term);
                if (term.GiverCiv == receiver)
                {
                    // Forderungen sind nur so hoch erlaubt, wie der Kriegsverlauf es trägt.
                    if (war.ScoreFor(proposer) < termValue) { reason = "Forderung übersteigt die Kriegslage."; return -100f; }
                    cost = termValue;
                }
                else cost = -termValue;
            }
            int otherWars = 0;
            foreach (var w in Wars) if (w != war && !w.Suspended && w.SideOf(receiver) != WarSide.None) otherWars++;

            float accept = Settings.PeaceBaseline - scoreR * Settings.PeaceScoreWeight + exhaustionR * Settings.PeaceExhaustionWeight
                           - cost + otherWars * Settings.PeaceOtherWarBonus;
            if (accept <= 0f) reason = "Der Kriegsverlauf spricht aus Sicht des Empfängers gegen diesen Frieden.";
            return accept;
        }

        // Wert einer Bedingung in warScore-Punkten.
        public float TermValue(PeaceTerm term)
        {
            switch (term.Type)
            {
                case PeaceTermType.CedeSettlement:
                    if (!_world.TryGetSettlement(term.SettlementId, out var s)) return 1000f;
                    int pop = Math.Max(1, _world.GetPopulation(term.GiverCiv));
                    return 20f + 40f * s.Population / pop + (s.IsCapital ? 30f : 0f);
                case PeaceTermType.Tribute:
                    return 60f * Clamp(term.TributeFraction, 0f, 0.5f);
                default:
                    return 0f;
            }
        }

        public PeaceProposalResult ProposePeace(int proposer, int receiver, PeaceTerm term)
        {
            var war = FindWar(proposer, receiver);
            if (war == null) return new PeaceProposalResult(false, 0f, "Kein Krieg zwischen diesen Zivilisationen.");
            if (!ValidateTerm(war, proposer, receiver, term, out string invalid)) return new PeaceProposalResult(false, 0f, invalid);
            float accept = EvaluatePeaceAcceptance(war, proposer, receiver, term, out string reason);
            if (accept <= 0f)
            {
                _events.Publish(new PeaceRejectedEvent { WarId = war.Id, Proposer = proposer, Receiver = receiver, AcceptScore = accept });
                return new PeaceProposalResult(false, accept, reason);
            }
            ApplyPeace(war, proposer, receiver, term);
            return new PeaceProposalResult(true, accept, null);
        }

        bool ValidateTerm(War war, int proposer, int receiver, PeaceTerm term, out string reason)
        {
            reason = null;
            if (term.Type == PeaceTermType.WhitePeace) return true;
            if (term.GiverCiv != proposer && term.GiverCiv != receiver) { reason = "Nur Kriegsparteien können abtreten oder zahlen."; return false; }
            if (term.Type == PeaceTermType.CedeSettlement)
            {
                if (!_world.TryGetSettlement(term.SettlementId, out var s)) { reason = "Siedlung existiert nicht."; return false; }
                if (s.OwnerCiv != term.GiverCiv) { reason = "Die Siedlung gehört nicht der abtretenden Zivilisation."; return false; }
                if (_world.GetSettlements(term.GiverCiv).Count <= 1) { reason = "Die letzte Siedlung kann nicht abgetreten werden."; return false; }
            }
            if (term.Type == PeaceTermType.Tribute && (term.TributeFraction <= 0f || term.TributeFraction > 0.5f)) { reason = "Tribut muss zwischen 0 und 50 % liegen."; return false; }
            return true;
        }

        void ApplyPeace(War war, int civA, int civB, PeaceTerm term)
        {
            int receiverOfTerm = term.GiverCiv == civA ? civB : civA;
            if (term.Type == PeaceTermType.CedeSettlement)
            {
                if (_world.TransferSettlement(term.SettlementId, receiverOfTerm))
                {
                    AddModifier(term.GiverCiv, receiverOfTerm, RelationReason.TerritoryCeded, Settings.TerritoryCededValue, Settings.DefaultEventDecay * 0.5f);
                }
            }
            else if (term.Type == PeaceTermType.Tribute)
            {
                _world.TransferTribute(term.GiverCiv, receiverOfTerm, term.TributeFraction);
            }

            bool leaders = (war.AttackerLeader == civA && war.DefenderLeader == civB) || (war.AttackerLeader == civB && war.DefenderLeader == civA);
            if (leaders)
            {
                foreach (var a in war.Attackers)
                    foreach (var d in war.Defenders)
                    {
                        AddTreaty(TreatyType.PeaceCooldown, a, d, Now + Settings.PeaceCooldownTicks, 0);
                        ClearGrievance(a, d);
                    }
                EndWar(war, WarEndReason.Peace);
            }
            else
            {
                // Separatfrieden: wer nicht Kriegsführer ist, verlässt den Krieg.
                if (civA != war.AttackerLeader && civA != war.DefenderLeader) LeaveWar(war, civA);
                if (civB != war.AttackerLeader && civB != war.DefenderLeader) LeaveWar(war, civB);
                AddTreaty(TreatyType.PeaceCooldown, civA, civB, Now + Settings.PeaceCooldownTicks, 0);
                ClearGrievance(civA, civB);
            }
            AddModifier(civA, civB, RelationReason.PeaceSigned, Settings.PeaceSignedValue, Settings.DefaultEventDecay);
            AddModifier(civB, civA, RelationReason.PeaceSigned, Settings.PeaceSignedValue, Settings.DefaultEventDecay);
            _events.Publish(new PeaceSignedEvent { WarId = war.Id, CivA = civA, CivB = civB, Term = term.Type, SettlementId = term.SettlementId });
        }

        void LeaveWar(War war, int civ)
        {
            if (war.Attackers.Remove(civ) || war.Defenders.Remove(civ))
                _events.Publish(new WarLeftEvent { WarId = war.Id, CivId = civ });
        }

        void EndWar(War war, WarEndReason reason)
        {
            if (!war.Active) return;
            war.Active = false;
            war.Suspended = false;
            war.EndTick = Now;
            war.EndReason = reason;
            Wars.Remove(war);
            WarHistory.Add(war);
            if (WarHistory.Count > MaxWarHistory) WarHistory.RemoveAt(0);
            // Ein Waffenstillstand zu diesem Krieg ist damit gegenstandslos.
            for (int i = Treaties.Count - 1; i >= 0; i--)
                if (Treaties[i].SuspendedWarId == war.Id) EndTreaty(Treaties[i], TreatyEndReason.PeaceSigned);
            _events.Publish(new WarEndedEvent { WarId = war.Id, Reason = reason, FinalWarScore = war.WarScore });
        }

        // ================= Meldungen aus anderen Systemen (über den Adapter im GameBootstrap) =================

        // Aus UnitDiedEvent (abonniert im Konstruktor) oder direkt gemeldet. strength = Kampfstärke des Einheitentyps.
        // Nur Tode durch einen Kriegsgegner zählen. Ziviler Despawn (Siedler gründet, Karawane kommt an,
        // KillerCivId 0) und Eigenverluste sind keine Kriegsverluste.
        public void ReportUnitLost(int civId, int killerCivId, float strength)
        {
            if (civId <= 0 || killerCivId <= 0 || killerCivId == civId) return;
            foreach (var war in Wars)
            {
                var side = war.SideOf(civId);
                if (side == WarSide.None) continue;
                var killerSide = war.SideOf(killerCivId);
                if (killerSide == WarSide.None || killerSide == side) continue;
                var stats = war.StatsOf(side);
                stats.UnitsLost++;
                stats.StrengthLost += Math.Max(0f, strength);
                RecomputeWar(war);
                return;
            }
        }

        // Aus SettlementCapturedEvent (Civ).
        public void ReportSettlementCaptured(int settlementId, int fromCiv, int toCiv, int population)
        {
            foreach (var war in Wars)
            {
                var winner = war.SideOf(toCiv);
                var loser = war.SideOf(fromCiv);
                if (winner == WarSide.None || loser == WarSide.None || winner == loser) continue;
                war.StatsOf(winner).SettlementsCaptured++;
                war.StatsOf(winner).PopulationCaptured += population;
                war.StatsOf(loser).SettlementsLost++;
                war.StatsOf(loser).PopulationLost += population;
                RecomputeWar(war);
                return;
            }
        }

        // Ressourcen, die eine Civ im Krieg für Militär ausgibt (für Kriegsstatistik).
        public void ReportWarSpending(int civId, float amount)
        {
            foreach (var war in Wars)
            {
                var side = war.SideOf(civId);
                if (side != WarSide.None) war.StatsOf(side).ResourcesSpent += Math.Max(0f, amount);
            }
        }

        // Aus CivilizationCollapsedEvent (Civ) und aus der Referenzprüfung nach dem Laden.
        public void ReportCivilizationCollapsed(int civId)
        {
            for (int i = Wars.Count - 1; i >= 0; i--)
            {
                var war = Wars[i];
                var side = war.SideOf(civId);
                if (side == WarSide.None) continue;
                var members = war.MembersOf(side);
                members.Remove(civId);
                if (members.Count == 0) { EndWar(war, WarEndReason.CivCollapsed); continue; }
                if (war.AttackerLeader == civId) war.AttackerLeader = war.Attackers[0];
                if (war.DefenderLeader == civId) war.DefenderLeader = war.Defenders[0];
                _events.Publish(new WarLeftEvent { WarId = war.Id, CivId = civId });
            }
            for (int i = Treaties.Count - 1; i >= 0; i--)
            {
                var t = Treaties[i];
                if (t.CivA != civId && t.CivB != civId) continue;
                if (t.SuspendedWarId != 0) ResumeWar(t);
                EndTreaty(t, TreatyEndReason.CivCollapsed);
            }
            Modifiers.RemoveAll(m => m.FromCiv == civId || m.ToCiv == civId);
            foreach (var p in Points) if (p.ControllerCiv == civId) p.ControllerCiv = 0;
            Memory.RemoveAll(m => m.CivId == civId);
        }

        // ================= Strategische Punkte =================

        public int AddStrategicPoint(StrategicPointType type, int x, int y, float value)
        {
            var p = new StrategicPoint { Id = PointIds.Next(), Type = type, X = x, Y = y, Value = Math.Max(0f, value) };
            Points.Add(p);
            return p.Id;
        }

        public StrategicPoint GetStrategicPoint(int pointId)
        {
            foreach (var p in Points) if (p.Id == pointId) return p;
            return null;
        }

        // ================= Zielprioritäten für Military IArmyAI =================

        public IReadOnlyList<TargetPriority> GetTargetPriorities(int civId)
        {
            var result = new List<TargetPriority>();
            bool hasCapital = _world.TryGetCapitalPosition(civId, out int cx, out int cy);
            foreach (var war in Wars)
            {
                if (war.Suspended) continue;
                var side = war.SideOf(civId);
                if (side == WarSide.None) continue;
                var mySide = war.MembersOf(side);
                var enemies = war.MembersOf(side == WarSide.Attacker ? WarSide.Defender : WarSide.Attacker);

                foreach (var enemy in enemies)
                {
                    foreach (var sid in _world.GetSettlements(enemy))
                    {
                        if (!_world.TryGetSettlement(sid, out var s)) continue;
                        bool goal = IsGoalTarget(war, mySide, WarGoalType.ConquerSettlement, sid);
                        float v = SettlementValue(s, goal);
                        if (hasCapital) v /= 1f + Distance(cx, cy, s.X, s.Y) / Settings.TargetDistanceScale;
                        result.Add(new TargetPriority(TargetKind.Settlement, sid, enemy, war.Id, s.X, s.Y, v, goal));
                    }
                }
                foreach (var p in Points)
                {
                    if (mySide.Contains(p.ControllerCiv)) continue;
                    bool goal = IsGoalTarget(war, mySide, WarGoalType.HoldPoint, p.Id);
                    bool enemyHeld = enemies.Contains(p.ControllerCiv);
                    float d = hasCapital ? Distance(cx, cy, p.X, p.Y) : 0f;
                    if (!goal && !enemyHeld && d > Settings.MaxReachTiles) continue;
                    float v = p.Value * (goal ? 2f : 1f) / (1f + d / Settings.TargetDistanceScale);
                    result.Add(new TargetPriority(TargetKind.StrategicPoint, p.Id, p.ControllerCiv, war.Id, p.X, p.Y, v, goal));
                }
            }
            result.Sort((a, b) =>
            {
                int c = b.Value.CompareTo(a.Value);
                if (c != 0) return c;
                c = a.Kind.CompareTo(b.Kind);
                return c != 0 ? c : a.TargetId.CompareTo(b.TargetId);
            });
            return result;
        }

        static bool IsGoalTarget(War war, List<int> side, WarGoalType type, int targetId)
        {
            foreach (var g in war.Goals) if (!g.Fulfilled && g.Type == type && g.TargetId == targetId && side.Contains(g.OwnerCiv)) return true;
            return false;
        }

        // ================= Entscheidungsprotokoll / KI-Gedächtnis =================

        internal void LogDecision(int civ, int target, DiplomaticActionType action, float utility, params DecisionFactor[] factors)
        {
            var d = new DiplomaticDecision { Tick = Now, CivId = civ, TargetCiv = target, Action = action, Utility = utility };
            d.Factors.AddRange(factors);
            Decisions.Add(d);
            while (Decisions.Count > Settings.DecisionLogSize) Decisions.RemoveAt(0);
            _events.Publish(new DiplomaticDecisionEvent { CivId = civ, TargetCiv = target, Action = action, Utility = utility });
        }

        internal CivAiMemory GetMemory(int civId)
        {
            int i = Memory.FindIndex(m => m.CivId == civId);
            if (i >= 0) return Memory[i];
            var mem = new CivAiMemory { CivId = civId };
            int insert = 0;
            while (insert < Memory.Count && Memory[insert].CivId < civId) insert++;
            Memory.Insert(insert, mem);
            return mem;
        }

        // ================= Hilfen =================

        internal static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        internal static float Clamp01(float v) => Clamp(v, 0f, 1f);
        internal static float Distance(int x0, int y0, int x1, int y1)
        {
            float dx = x1 - x0, dy = y1 - y0;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
