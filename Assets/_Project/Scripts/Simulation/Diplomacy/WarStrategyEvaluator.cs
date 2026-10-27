using System;
using System.Collections.Generic;

namespace WorldBeyond.Simulation.Diplomacy
{
    // Kriegsbewerter der Zivilisations-KI (Utility AI). Wird vom CivAISystem (Besitzer: Civ, Phase 3)
    // pro Civ aufgerufen, etwa alle 5 Spielsekunden. Hat keinen eigenen Zustand; Abklingzeiten liegen im
    // KI-Gedächtnis des DiplomacyService und werden mitgespeichert.
    // Entscheidungen hängen nur von Ressourcenlage, Stärke, Bedrohung, Entfernung und Beziehungen ab, nie von Zufall.
    public sealed class WarStrategyEvaluator
    {
        readonly DiplomacyService _d;
        DiplomacySettings S => _d.Settings;
        IDiplomacyWorldPort W => _d.World;

        public WarStrategyEvaluator(DiplomacyService diplomacy)
        {
            _d = diplomacy ?? throw new ArgumentNullException(nameof(diplomacy));
        }

        // Führt höchstens eine diplomatische Hauptaktion und ggf. eine Rekrutierungsanfrage aus.
        public void EvaluateAndAct(int civId)
        {
            if (!W.IsCivAlive(civId)) return;
            var mem = _d.GetMemory(civId);
            long now = _d.Now;

            if (now - mem.LastPeaceProposalTick >= S.PeaceProposalCooldownTicks && TryPeace(civId))
                mem.LastPeaceProposalTick = now;
            else if (now - mem.LastWarTick >= S.WarCooldownTicks && TryDeclareWar(civId))
                mem.LastWarTick = now;
            else if (now - mem.LastAllianceTick >= S.AllianceCooldownTicks && TryAlliance(civId))
                mem.LastAllianceTick = now;

            mem.RecruitWillingness = RecruitWillingness(civId);
            if (S.SelfRecruit && now - mem.LastRecruitTick >= S.RecruitCooldownTicks && TryRecruit(civId))
                mem.LastRecruitTick = now;
        }

        // ---------------- Kriegserklärung ----------------

        public struct WarOption
        {
            public int Target;
            public float Utility;
            public CasusBelli CasusBelli;
            public DecisionFactor[] Factors;
        }

        public WarOption ScoreWar(int civ, int target)
        {
            var option = new WarOption { Target = target, Utility = 0f, CasusBelli = CasusBelli.None, Factors = Array.Empty<DecisionFactor>() };
            if (!_d.CanDeclareWar(civ, target, out _)) return option;

            float myStrength = EffectiveStrength(civ, 0.5f);
            float theirStrength = EffectiveStrength(target, 0.7f);
            if (_d.IsAtWar(target)) theirStrength *= 0.7f;     // Gegner ist schon gebunden
            // Beide ohne Armee (Spielstart): gleich stark, nicht "unendlich schwach".
            float ratio = (myStrength < 1f && theirStrength < 1f) ? 1f : myStrength / Math.Max(1f, theirStrength);
            if (ratio < S.MinStrengthRatio) return option;            // deutlich schwächer: nie angreifen
            // Weicher Faktor: auch gleich starke Nachbarn können mit genug Motiv Krieg führen.
            float strength = DiplomacyService.Clamp01((ratio - S.MinStrengthRatio) / (S.FullStrengthRatio - S.MinStrengthRatio));

            float opinion = _d.GetOpinion(civ, target);
            float hostility = DiplomacyService.Clamp01(-opinion / 100f);

            int border = W.GetSharedBorderLength(civ, target);
            float proximity = _d.ContactProximity(civ, target, border);
            if (proximity <= 0f) return option;                       // außer Reichweite: kein Krieg
            float reach = 0.5f + 0.5f * proximity;

            float contestion = DiplomacyService.Clamp01(W.GetResourceContestion(civ, target));
            float need = DiplomacyService.Clamp01(W.GetResourceNeed(civ)) * Math.Max(contestion, border > 0 ? 0.3f : 0f);
            float aggression = DiplomacyService.Clamp01(W.GetAggression(civ));

            float hostilityPart = 0.45f * hostility, needPart = 0.30f * need, aggressionPart = 0.25f * aggression;
            float motive = hostilityPart + needPart + aggressionPart;
            if (hostilityPart + needPart < S.MinWarReason) return option;   // kein Kriegsgrund: Stärke/Haltung allein reichen nicht
            float busy = _d.IsAtWar(civ) ? 0.4f : 1f;
            float motiveScale = DiplomacyService.Clamp01(motive / Math.Max(0.01f, S.MotiveForFullStrength));
            float weariness = _d.WarWeariness(civ, target);

            option.Utility = reach * (S.StrengthWeight * strength * motiveScale + (1f - S.StrengthWeight) * motive) * busy * (1f - weariness);

            if (hostilityPart >= needPart && hostilityPart >= aggressionPart)
                option.CasusBelli = border > 0 ? CasusBelli.BorderDispute : CasusBelli.Hostility;
            else if (needPart >= aggressionPart)
                option.CasusBelli = contestion > 0f ? CasusBelli.ResourceCompetition : CasusBelli.Expansion;
            else option.CasusBelli = CasusBelli.Expansion;

            option.Factors = new[]
            {
                new DecisionFactor("Stärkeverhältnis", ratio),
                new DecisionFactor("Feindseligkeit", hostility),
                new DecisionFactor("Nähe", proximity),
                new DecisionFactor("Ressourcenbedarf", need),
                new DecisionFactor("Aggressivität", aggression),
                new DecisionFactor("Bereits im Krieg", busy < 1f ? 1f : 0f),
                new DecisionFactor("Kriegsmüdigkeit", weariness),
            };
            return option;
        }

        bool TryDeclareWar(int civ)
        {
            var best = new WarOption { Utility = 0f };
            foreach (var other in W.CivIds)
            {
                if (other == civ) continue;
                var o = ScoreWar(civ, other);
                if (o.Utility > best.Utility) best = o;   // bei Gleichstand gewinnt die kleinere ID (aufsteigende Liste)
            }
            if (best.Utility < S.WarThreshold) return false;
            if (!_d.DeclareWar(civ, best.Target, best.CasusBelli, null, out _)) return false;
            _d.LogDecision(civ, best.Target, DiplomaticActionType.DeclareWar, best.Utility, best.Factors);
            return true;
        }

        // Stärke inklusive Verbündeter (gewichtet).
        public float EffectiveStrength(int civ, float allyWeight)
        {
            float s = W.GetMilitaryStrength(civ);
            foreach (var ally in _d.GetAllies(civ)) s += allyWeight * W.GetMilitaryStrength(ally);
            return s;
        }

        // ---------------- Frieden ----------------

        bool TryPeace(int civ)
        {
            foreach (var war in _d.ActiveWars)
            {
                if (war.Suspended || !_d.PeaceTalksOpen(war)) continue;
                var side = war.SideOf(civ);
                if (side == WarSide.None) continue;
                bool leader = war.AttackerLeader == civ || war.DefenderLeader == civ;
                int opponent = side == WarSide.Attacker ? war.DefenderLeader : war.AttackerLeader;
                float score = war.ScoreFor(civ);
                float exhaustion = war.StatsOf(side).Exhaustion;

                PeaceTerm term;
                if (score >= 25f) term = BestDemand(war, civ, opponent, score);
                else if (score <= -45f || exhaustion >= 80f
                         || (Threat(civ) >= S.HopelessThreat && exhaustion >= S.HopelessPeaceExhaustion))
                    term = BestConcession(civ, opponent);   // aussichtslose Lage: lieber früh nachgeben
                else if (score <= -20f || exhaustion >= 60f || !leader) term = PeaceTerm.White();
                else continue;

                var result = _d.ProposePeace(civ, opponent, term);
                _d.LogDecision(civ, opponent, DiplomaticActionType.ProposePeace, result.AcceptScore,
                    new DecisionFactor("Kriegsstand", score), new DecisionFactor("Erschöpfung", exhaustion),
                    new DecisionFactor("Bedingung", (float)term.Type), new DecisionFactor("Angenommen", result.Accepted ? 1f : 0f));
                _d.LogDecision(opponent, civ, result.Accepted ? DiplomaticActionType.AcceptPeace : DiplomaticActionType.RejectPeace, result.AcceptScore,
                    new DecisionFactor("Annahmewert", result.AcceptScore));
                return true;
            }
            return false;
        }

        // Höchste Forderung, die der Kriegsstand trägt: wertvollste Siedlung, sonst Tribut.
        PeaceTerm BestDemand(War war, int civ, int opponent, float score)
        {
            PeaceTerm best = PeaceTerm.White();
            float bestValue = 0f;
            var settlements = W.GetSettlements(opponent);
            if (settlements.Count > 1)
            {
                foreach (var sid in settlements)
                {
                    var term = PeaceTerm.Cede(opponent, sid);
                    float v = _d.TermValue(term);
                    if (v <= score && v > bestValue) { bestValue = v; best = term; }
                }
            }
            if (best.Type == PeaceTermType.WhitePeace)
            {
                float fraction = Math.Min(0.5f, score / 120f);
                if (fraction > 0.05f) best = PeaceTerm.Tribute(opponent, fraction);
            }
            return best;
        }

        // Kleinstes Zugeständnis: kleinste Siedlung (nie Hauptstadt), sonst Tribut.
        PeaceTerm BestConcession(int civ, int opponent)
        {
            var settlements = W.GetSettlements(civ);
            int bestId = 0; int bestPop = int.MaxValue;
            if (settlements.Count > 1)
            {
                foreach (var sid in settlements)
                {
                    if (!W.TryGetSettlement(sid, out var s) || s.IsCapital) continue;
                    if (s.Population < bestPop) { bestPop = s.Population; bestId = sid; }
                }
            }
            return bestId != 0 ? PeaceTerm.Cede(civ, bestId) : PeaceTerm.Tribute(civ, 0.2f);
        }

        // ---------------- Bündnisse ----------------

        public float ScoreAlliance(int civ, int other)
        {
            if (civ == other || _d.AreAllied(civ, other) || _d.FindWar(civ, other) != null) return 0f;
            if (_d.GetAllies(civ).Count >= S.MaxAlliancesPerCiv) return 0f;
            float opinion = _d.GetOpinion(civ, other);
            if (opinion < 20f) return 0f;
            return 0.5f * DiplomacyService.Clamp01(opinion / 100f) + 0.5f * CommonThreat(civ, other);
        }

        // 1, wenn beide denselben Gegner fürchten oder einer von beiden mit ihm im Krieg ist.
        public float CommonThreat(int a, int b)
        {
            foreach (var c in W.CivIds)
            {
                if (c == a || c == b) continue;
                bool aThreat = _d.AreAtWar(a, c) || _d.GetOpinion(a, c) <= -20f;
                bool bThreat = _d.AreAtWar(b, c) || _d.GetOpinion(b, c) <= -20f;
                if (aThreat && bThreat) return 1f;
            }
            return 0f;
        }

        public bool WouldAcceptAlliance(int receiver, int proposer)
        {
            if (_d.GetAllies(receiver).Count >= S.MaxAlliancesPerCiv) return false;
            float opinion = _d.GetOpinion(receiver, proposer);
            return opinion >= 20f && (CommonThreat(receiver, proposer) > 0f || opinion >= 50f);
        }

        bool TryAlliance(int civ)
        {
            int best = 0; float bestU = 0f;
            foreach (var other in W.CivIds)
            {
                float u = ScoreAlliance(civ, other);
                if (u > bestU) { bestU = u; best = other; }
            }
            if (best == 0 || bestU < S.AllianceThreshold) return false;
            bool accepted = WouldAcceptAlliance(best, civ) && _d.FormAlliance(civ, best);
            _d.LogDecision(civ, best, DiplomaticActionType.ProposeAlliance, bestU,
                new DecisionFactor("Meinung", _d.GetOpinion(civ, best)), new DecisionFactor("Gemeinsame Bedrohung", CommonThreat(civ, best)),
                new DecisionFactor("Angenommen", accepted ? 1f : 0f));
            return true;
        }

        // ---------------- Rekrutierung ----------------

        // Bedrohung = stärkster Gegner oder feindseliger Nachbar im Verhältnis zur eigenen Stärke.
        public float Threat(int civ)
        {
            float mine = Math.Max(1f, EffectiveStrength(civ, 0.5f));
            float worst = 0f;
            foreach (var other in W.CivIds)
            {
                if (other == civ || _d.AreAllied(civ, other)) continue;
                bool enemy = _d.AreAtWar(civ, other);
                bool hostileNeighbour = W.GetSharedBorderLength(civ, other) > 0 && _d.GetOpinion(other, civ) <= S.TensionThreshold;
                if (!enemy && !hostileNeighbour) continue;
                worst = Math.Max(worst, W.GetMilitaryStrength(other) / mine);
            }
            return worst;
        }

        // Grundarmee im Frieden: Stärke, die eine Civ nach Bevölkerung und Haltung unterhalten will.
        public float PeacetimeArmyTarget(int civ)
        {
            return W.GetPopulation(civ) * S.PeaceArmyStrengthPerPop * (0.5f + DiplomacyService.Clamp01(W.GetAggression(civ)));
        }

        // Kosten einer Armee dieser Größe: Arbeitskraft (unbegrenzt steigend), Nahrungsmangel, Kriegsmüdigkeit.
        public float RecruitCost(int civ, int armyUnits, int population)
        {
            float share = Math.Min(0.99f, armyUnits / (float)Math.Max(1, population));
            float labor = S.LaborCostWeight * share / (1f - share);
            float food = S.FoodCostWeight * DiplomacyService.Clamp01(W.GetResourceNeed(civ));
            float exhaustion = 0f;
            foreach (var war in _d.ActiveWars)
            {
                var side = war.SideOf(civ);
                if (side != WarSide.None) exhaustion = Math.Max(exhaustion, war.StatsOf(side).Exhaustion);
            }
            return labor + food + S.ExhaustionCostWeight * exhaustion / 100f;
        }

        float RecruitBenefit(int civ, out float threat, out bool atWar, out float deficit)
        {
            threat = Threat(civ);
            atWar = _d.IsAtWar(civ);
            deficit = Math.Max(0f, PeacetimeArmyTarget(civ) - W.GetMilitaryStrength(civ));
            return DiplomacyService.Clamp01(threat - 0.8f) + (atWar ? 0.3f : 0f) + (deficit > 0f ? 0.45f : 0f);
        }

        int CountedArmy(int civ, CivAiMemory mem, long now)
        {
            if (now - mem.PendingRecruitsTick >= S.RecruitPendingTicks) mem.PendingRecruits = 0;
            return Math.Max(0, W.GetArmyUnitCount(civ)) + mem.PendingRecruits;
        }

        // Bereitschaft für einen weiteren Rekruten: Nutzen − Kosten, 0 … 1 (für Military über IWarQuery.GetRecruitWillingness).
        public float RecruitWillingness(int civ)
        {
            float benefit = RecruitBenefit(civ, out _, out _, out _);
            int army = CountedArmy(civ, _d.GetMemory(civ), _d.Now);
            return DiplomacyService.Clamp01(benefit - RecruitCost(civ, army + 1, Math.Max(1, W.GetPopulation(civ))));
        }

        bool TryRecruit(int civ)
        {
            float benefit = RecruitBenefit(civ, out float threat, out bool atWar, out float deficit);
            var mem = _d.GetMemory(civ);
            long now = _d.Now;
            int army = CountedArmy(civ, mem, now);
            int pop = Math.Max(1, W.GetPopulation(civ));
            bool hopeless = atWar && threat >= S.HopelessThreat;
            int byThreat = (int)Math.Ceiling(threat * 3f);
            int byDeficit = (int)Math.Ceiling(deficit / Math.Max(0.1f, S.AssumedUnitStrength));
            int wanted = Math.Max(1, Math.Min(S.MaxRecruitPerRequest, Math.Max(byThreat, byDeficit)));
            // Keine feste Grenze: größte Anzahl, deren Nutzen die Kosten (Arbeitskraft, Nahrung, Kriegsmüdigkeit) noch trägt.
            int count = 0; float utility = 0f, cost = 0f;
            for (int n = wanted; n >= 1; n--)
            {
                cost = RecruitCost(civ, army + n, pop);
                utility = benefit - cost;
                if (utility >= S.RecruitThreshold) { count = n; break; }
            }
            if (count == 0) return false;
            bool ok = W.RequestMilitaryBuildup(civ, count, out string reason);
            if (!ok && count > 1)
            {
                // Größere Anforderung abgelehnt (z. B. Ressourcen reichen nur für eine Einheit): einzeln versuchen,
                // damit eine schwache Civ im Krieg nicht dauerhaft ohne Nachschub bleibt.
                count = 1;
                ok = W.RequestMilitaryBuildup(civ, count, out reason);
            }
            if (ok && S.RecruitPendingTicks > 0) { mem.PendingRecruits += count; mem.PendingRecruitsTick = now; }
            _d.LogDecision(civ, 0, DiplomaticActionType.Recruit, utility,
                new DecisionFactor("Bedrohung", threat), new DecisionFactor("Im Krieg", atWar ? 1f : 0f), new DecisionFactor("Fehlende Grundarmee", deficit),
                new DecisionFactor("Kosten", cost), new DecisionFactor("Armeeanteil", army / (float)pop), new DecisionFactor("Aussichtslos", hopeless ? 1f : 0f),
                new DecisionFactor("Anzahl", count), new DecisionFactor("Angenommen", ok ? 1f : 0f));
            return true;   // auch bei Ablehnung (z. B. zu wenig Ressourcen) Abklingzeit, um nicht jeden Takt neu anzufragen
        }
    }
}
