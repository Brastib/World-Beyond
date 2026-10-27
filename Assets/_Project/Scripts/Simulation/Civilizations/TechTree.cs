using System;

namespace WorldBeyond.Simulation.Civilizations
{
    /// <summary>
    /// Forschung je Zivilisation. Forschungspunkte entstehen im Siedlungs-Update (Bevölkerung, Gelehrte in Bibliotheken)
    /// und werden hier auf die laufende Technologie gebucht. Epoche = höchste Epoche einer erforschten Technologie.
    /// </summary>
    public sealed class TechTree : ITechTree
    {
        private readonly CivState _s;

        public TechTree(CivState state) => _s = state ?? throw new ArgumentNullException(nameof(state));

        public bool TryGetTech(string techKey, out TechId tech) => _s.Content.Techs.TryGetByKey(techKey, out tech);

        public bool IsUnlocked(int civId, TechId tech)
        {
            var c = _s.Civ(civId);
            return c != null && tech.Value > 0 && tech.Value < c.Researched.Length && c.Researched[tech.Value];
        }

        public bool IsUnlocked(int civId, string techKey)
        {
            if (string.IsNullOrEmpty(techKey)) return _s.Civ(civId) != null;
            return TryGetTech(techKey, out var t) && IsUnlocked(civId, t);
        }

        public Epoch CurrentEpoch(int civId) => _s.Civ(civId)?.Epoch ?? Epoch.Primitive;

        public bool CanResearch(Civilization c, TechId tech)
        {
            if (!_s.Content.Techs.TryGet(tech, out _) || c.Researched[tech.Value]) return false;
            foreach (var p in _s.Content.Techs.PrerequisitesOf(tech))
                if (!c.Researched[p.Value]) return false;
            return true;
        }

        public void SetResearch(int civId, TechId tech)
        {
            var c = _s.Civ(civId);
            if (c == null || !CanResearch(c, tech) || c.CurrentResearch.Equals(tech)) return;
            c.CurrentResearch = tech;
            c.ResearchPoints = 0;   // Wechsel verwirft den Fortschritt (bewusste Regel, im UI anzeigen)
        }

        public void ForceEpoch(int civId, Epoch epoch)
        {
            var c = _s.Civ(civId);
            if (c == null) return;
            foreach (var t in _s.Content.Techs.All())
            {
                var d = _s.Content.Techs.Get(t);
                if (d.Epoch <= epoch && !c.Researched[t.Value]) Complete(c, t);
            }
            if (c.CurrentResearch.IsValid && c.Researched[c.CurrentResearch.Value]) ChooseNextResearch(c);
        }

        /// <summary>Bucht Forschungspunkte; schließt Technologien ab und wählt automatisch die nächste.</summary>
        internal void AddResearch(Civilization c, double points)
        {
            if (!c.Alive || points <= 0) return;
            if (!c.CurrentResearch.IsValid) ChooseNextResearch(c);
            int guard = 8;
            while (points > 0 && c.CurrentResearch.IsValid && guard-- > 0)
            {
                var d = _s.Content.Techs.Get(c.CurrentResearch);
                double need = d.Cost - c.ResearchPoints;
                if (points < need) { c.ResearchPoints += points; return; }
                points -= need;
                Complete(c, c.CurrentResearch);
                ChooseNextResearch(c);
            }
        }

        private void Complete(Civilization c, TechId tech)
        {
            c.Researched[tech.Value] = true;
            var old = c.Epoch;
            var e = _s.Content.Techs.Get(tech).Epoch;
            if (e > c.Epoch) c.Epoch = e;
            if (c.CurrentResearch.Equals(tech)) { c.CurrentResearch = default; c.ResearchPoints = 0; }
            _s.Ctx.Events.Publish(new TechResearchedEvent { CivId = c.Id, Tech = tech, EpochAfter = c.Epoch });
            if (c.Epoch != old) _s.Ctx.Events.Publish(new EpochChangedEvent { CivId = c.Id, OldEpoch = old, NewEpoch = c.Epoch });
        }

        /// <summary>
        /// Wahl der nächsten Forschung nach Nutzen: niedrigste Epoche zuerst, dann günstigste; Fruchtwechsel und Medizin
        /// werden bei Hunger bzw. hoher Sterblichkeit vorgezogen (Bewertung im CivAISystem setzt CurrentResearch gezielt).
        /// </summary>
        internal void ChooseNextResearch(Civilization c)
        {
            TechId best = default;
            float bestScore = float.MaxValue;
            foreach (var t in _s.Content.Techs.All())
            {
                if (!CanResearch(c, t)) continue;
                var d = _s.Content.Techs.Get(t);
                float score = (int)d.Epoch * 10000f + d.Cost;
                // Fischfang nützt nur mit Wasser im eigenen Gebiet: dann zuletzt in seiner Epoche.
                if (d.Key == "tech.fishing" && !CivHasWater(c)) score += 5000f;
                if (score < bestScore) { bestScore = score; best = t; }
            }
            c.CurrentResearch = best;
            c.ResearchPoints = 0;
        }

        private bool CivHasWater(Civilization c)
        {
            foreach (int sid in _s.SettlementListOf(c.Id))
            {
                var st = _s.Settlements[sid];
                for (int y = st.Y - 6; y <= st.Y + 7; y++)
                    for (int x = st.X - 6; x <= st.X + 7; x++)
                        if (_s.World.InBounds(x, y) && _s.World.IsWater(x, y)) return true;
            }
            return false;
        }

        /// <summary>Summierter Bonus aller erforschten Technologien.</summary>
        internal void Bonuses(Civilization c, out float food, out float production, out float deathReduction)
        {
            food = 0f; production = 0f; deathReduction = 0f;
            for (int i = 1; i < c.Researched.Length; i++)
            {
                if (!c.Researched[i]) continue;
                var d = _s.Content.Techs.Get(new TechId(i));
                food += d.FoodYieldBonus;
                production += d.ProductionBonus;
                deathReduction += d.DeathRateReduction;
            }
            if (deathReduction > 0.8f) deathReduction = 0.8f;
        }
    }
}
