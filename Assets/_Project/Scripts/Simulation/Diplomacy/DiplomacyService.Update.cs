using System;

namespace WorldBeyond.Simulation.Diplomacy
{
    // Zeitabhängiger Teil: Abklingen, dauerhafte Faktoren, Vertragsablauf, Kriegsstand.
    public sealed partial class DiplomacyService
    {
        // Wird vom DiplomacySystem aufgerufen. Robust gegen unregelmäßige Aufrufe (rechnet mit der vergangenen Zeit).
        public void Update()
        {
            long now = Now;
            float dt = LastUpdateTick < 0 ? 0f : (now - LastUpdateTick) / (float)SimClock.TicksPerSecond;
            LastUpdateTick = now;

            DecayModifiers(dt);
            if (now - LastFactorRefreshTick >= Settings.FactorRefreshTicks)
            {
                // Erste Auffrischung zählt als ein Intervall; lange Pausen werden begrenzt.
                long elapsed = LastFactorRefreshTick < 0 ? Settings.FactorRefreshTicks
                             : Math.Min(now - LastFactorRefreshTick, Settings.FactorRefreshTicks * 4L);
                LastFactorRefreshTick = now;
                RefreshStandingFactors(elapsed / (float)SimClock.TicksPerSecond);
            }
            ExpireTreaties(now);
            UpdateStrategicPoints();
            for (int i = Wars.Count - 1; i >= 0; i--) RecomputeWar(Wars[i]);
        }

        void DecayModifiers(float dt)
        {
            if (dt <= 0f) return;
            for (int i = Modifiers.Count - 1; i >= 0; i--)
            {
                var m = Modifiers[i];
                if (m.DecayPerSecond <= 0f) continue;
                // Wert läuft linear Richtung 0 und wird entfernt, sobald er dort ankommt.
                if (m.Value > 0f) m.Value = Math.Max(0f, m.Value - m.DecayPerSecond * dt);
                else m.Value = Math.Min(0f, m.Value + m.DecayPerSecond * dt);
                if (Math.Abs(m.Value) < 0.5f) Modifiers.RemoveAt(i);
            }
        }

        // Grenzreibung und Ressourcenkonkurrenz sind Zustände, keine Ereignisse: Wert wird ersetzt, nicht aufaddiert.
        void RefreshStandingFactors(float seconds)
        {
            var civs = _world.CivIds;
            for (int i = 0; i < civs.Count; i++)
            {
                for (int j = 0; j < civs.Count; j++)
                {
                    if (i == j) continue;
                    int a = civs[i], b = civs[j];
                    int border = _world.GetSharedBorderLength(a, b);
                    float friction = Math.Max(Settings.BorderFrictionMax, border * Settings.BorderFrictionPerEdge);
                    if (AreAllied(a, b)) friction *= 0.25f;   // Verbündete tolerieren gemeinsame Grenzen eher
                    SetPersistentModifier(a, b, RelationReason.BorderFriction, border > 0 ? friction : 0f);

                    float contestion = Clamp01(_world.GetResourceContestion(a, b));
                    float need = Clamp01(_world.GetResourceNeed(a));
                    float competition = Settings.ResourceCompetitionMax * contestion * (0.5f + 0.5f * need);
                    SetPersistentModifier(a, b, RelationReason.ResourceCompetition, competition);

                    UpdateGrievance(a, b, border, ContactProximity(a, b, border), seconds);
                }
            }
        }

        // Groll wächst an einer Grenze ohne Bündnis, ruht im Krieg und in der Friedensfrist, baut sich ohne Grenze ab.
        // Ohne gemeinsame Grenze wächst Groll als Rivalität, solange die Gebiete nah beieinander liegen.
        void UpdateGrievance(int a, int b, int border, float proximity, float seconds)
        {
            float current = GetPersistentValue(a, b, RelationReason.BorderGrievance);
            float next = current;
            float edges = border > 0 ? border : Settings.RivalryEquivalentEdges * proximity;
            if (edges <= 0f) next = Math.Min(0f, current + Settings.GrievanceDecayPerSecond * seconds);
            else if (!AreAllied(a, b) && FindWar(a, b) == null && FindTreaty(a, b, TreatyType.PeaceCooldown) == null)
                next = Math.Max(Settings.GrievanceMax, current + Settings.GrievancePerEdgePerSecond * edges * seconds);
            if (next > -0.01f) next = 0f;
            if (next != current) SetPersistentModifier(a, b, RelationReason.BorderGrievance, next);
        }

        void ExpireTreaties(long now)
        {
            for (int i = Treaties.Count - 1; i >= 0; i--)
            {
                var t = Treaties[i];
                if (t.EndTick < 0 || now < t.EndTick) continue;
                EndTreaty(t, TreatyEndReason.Expired);
                if (t.SuspendedWarId != 0) ResumeWar(t);
            }
        }

        // Ein Punkt wechselt den Besitzer, wenn eine Civ ihn allein mit Einheiten hält (Military meldet über den Port).
        void UpdateStrategicPoints()
        {
            foreach (var p in Points)
            {
                int dominant = _world.GetDominantCivNear(p.X, p.Y, Settings.StrategicPointRadius);
                if (dominant == 0 || dominant == p.ControllerCiv) continue;
                int old = p.ControllerCiv;
                p.ControllerCiv = dominant;
                _events.Publish(new StrategicPointCapturedEvent { PointId = p.Id, OldController = old, NewController = dominant });
            }
        }

        // warScore und Erschöpfung werden immer aus den Statistiken berechnet, nie frei gesetzt.
        internal void RecomputeWar(War war)
        {
            if (!war.Active) return;
            var att = war.AttackerStats;
            var def = war.DefenderStats;

            float conquest = att.PopulationCaptured / (float)Math.Max(1, def.InitialPopulation)
                           - def.PopulationCaptured / (float)Math.Max(1, att.InitialPopulation);
            conquest = Clamp(conquest, -1f, 1f) * Settings.ConquestWeight;

            float lostTotal = att.StrengthLost + def.StrengthLost;
            float casualty = 0f;
            if (lostTotal > 0f)
            {
                float balance = (def.StrengthLost - att.StrengthLost) / lostTotal;
                float intensity = Clamp01(lostTotal / Math.Max(1f, 0.2f * (att.InitialStrength + def.InitialStrength)));
                casualty = balance * intensity * Settings.CasualtyWeight;
            }

            int heldA = 0, heldD = 0;
            foreach (var p in Points)
            {
                if (war.Attackers.Contains(p.ControllerCiv)) heldA++;
                else if (war.Defenders.Contains(p.ControllerCiv)) heldD++;
            }
            float points = (heldA + heldD) > 0 ? (heldA - heldD) / (float)(heldA + heldD) * Settings.PointsWeight : 0f;

            float goals = 0f;
            foreach (var g in war.Goals)
            {
                g.Fulfilled = IsGoalFulfilled(war, g);
                if (!g.Fulfilled) continue;
                goals += war.Attackers.Contains(g.OwnerCiv) ? Settings.GoalWeightEach : -Settings.GoalWeightEach;
            }
            goals = Clamp(goals, -Settings.GoalWeightMax, Settings.GoalWeightMax);

            war.WarScore = Clamp(conquest + casualty + points + goals, -100f, 100f);

            float minutes = Math.Max(0L, Now - war.StartTick) / (float)(SimClock.TicksPerSecond * 60);
            att.Exhaustion = Exhaustion(att, minutes);
            def.Exhaustion = Exhaustion(def, minutes);
        }

        float Exhaustion(WarSideStats s, float minutes)
        {
            float e = 60f * s.StrengthLost / Math.Max(1f, s.InitialStrength)
                    + 80f * s.PopulationLost / Math.Max(1f, s.InitialPopulation)
                    + Settings.ExhaustionPerMinute * minutes;
            return Clamp(e, 0f, 100f);
        }

        bool IsGoalFulfilled(War war, WarGoal g)
        {
            var side = war.SideOf(g.OwnerCiv);
            if (side == WarSide.None) return false;
            var members = war.MembersOf(side);
            switch (g.Type)
            {
                case WarGoalType.ConquerSettlement:
                    return _world.TryGetSettlement(g.TargetId, out var s) && members.Contains(s.OwnerCiv);
                case WarGoalType.HoldPoint:
                    var p = GetStrategicPoint(g.TargetId);
                    return p != null && members.Contains(p.ControllerCiv);
                default:
                    return false;  // Tribute/Humiliate erfüllen sich erst im Friedensvertrag
            }
        }
    }
}
