using System;
using System.Collections.Generic;
using WorldBeyond.Simulation.Units;

namespace WorldBeyond.Simulation.Civilizations
{
    /// <summary>Zivilisationen: Erschaffen, Lesesicht, Zusammenbruch. IDs fortlaufend, nie wiederverwendet.</summary>
    public sealed class CivilizationRegistry : ICivilizationRegistry
    {
        /// <summary>
        /// Ein Volk beginnt als Stamm aus mehreren Großfamilien (realistisch 25–150 Menschen), nicht als einzelne Familie.
        /// Mit 10 Startern und ~1 %/Jahr Wachstum entstand in den ersten 60 Jahren keine Tochtersiedlung (Unity-Lauf M1).
        /// </summary>
        public const int StartPopulation = 40;
        public static readonly ResourceCost StartStock = new ResourceCost { Food = 150, Wood = 120, Stone = 10 };

        private readonly CivState _s;
        private readonly List<int> _alive = new List<int>();
        internal SettlementRegistry Settlements;
        internal TechTree Tech;
        internal TerritoryService Territory;

        public CivilizationRegistry(CivState state) => _s = state ?? throw new ArgumentNullException(nameof(state));

        public IReadOnlyList<int> AllCivIds => _alive;

        public bool Exists(int civId)
        {
            var c = _s.Civ(civId);
            return c != null && c.Alive;
        }

        public CivilizationView Get(int civId) => TryGet(civId, out var v) ? v : default;

        public bool TryGet(int civId, out CivilizationView view)
        {
            var c = _s.Civ(civId);
            if (c == null) { view = default; return false; }
            double pop = 0;
            var stock = new ResourceCost();
            var list = _s.SettlementListOf(civId);
            var sums = new double[Resources.SlotCount];
            foreach (int sid in list)
            {
                var st = _s.Settlements[sid];
                pop += st.Population;
                for (int i = 0; i < sums.Length; i++) sums[i] += st.Stock[i];
            }
            foreach (var r in Resources.Stockable) stock[r] = (int)Math.Floor(sums[(int)r]);
            float progress = 0f;
            string researchKey = null;
            if (c.CurrentResearch.IsValid && _s.Content.Techs.TryGet(c.CurrentResearch, out var td))
            {
                researchKey = td.Key;
                progress = (float)Math.Min(1.0, c.ResearchPoints / td.Cost);
            }
            view = new CivilizationView(c.Id, c.Name, c.Color, c.Traits, c.Alive, c.CapitalSettlementId, list.Count,
                (int)Math.Round(pop), CountMilitary(civId), c.Epoch, Territory?.TerritoryTiles(civId) ?? 0, stock, researchKey, progress);
            return true;
        }

        private int CountMilitary(int civId)
        {
            int n = 0;
            var units = _s.Ctx.Units;
            var types = _s.Ctx.UnitTypes;
            units.ForEachAlive(id =>
            {
                if (units.TryGet(id, out var u) && u.CivId == civId && types.TryGet(u.Type, out var d) && d.Role >= UnitRole.Infantry && d.Role <= UnitRole.RocketArtillery)
                    n++;
            });
            return n;
        }

        public int Create(string name, CivColor color, ulong cultureSeed, int startX, int startY)
            => TryCreate(name, color, cultureSeed, startX, startY, out int id, out _) ? id : 0;

        public bool TryCreate(string name, CivColor color, ulong cultureSeed, int startX, int startY, out int civId, out string reason)
        {
            civId = 0;
            if (string.IsNullOrWhiteSpace(name)) name = NameGenerator.CivName(cultureSeed);
            var civ = new Civilization
            {
                Id = _s.CivIds.Next(),
                Name = name.Trim(),
                Color = color,
                CultureSeed = cultureSeed,
                Traits = CivTraits.FromSeed(cultureSeed),
                CreatedTick = _s.Ctx.Clock.Tick,
                Researched = new bool[_s.TechSlots],
                NextAiTick = _s.Ctx.Clock.Tick + 1,
            };
            // Vorläufig eintragen, damit CanFoundAt die Civ kennt; bei Fehlschlag wieder entfernen (ID bleibt verbraucht).
            _s.Civs.Add(civ.Id, civ);
            if (!Settlements.TryFound(civ.Id, startX, startY, StartPopulation, StartStock, false, out int capital, out reason))
            {
                _s.Civs.Remove(civ.Id);
                return false;
            }
            _s.CivOrder.Add(civ.Id);
            _alive.Add(civ.Id);
            civ.CapitalSettlementId = capital;
            Tech.ChooseNextResearch(civ);
            _s.Ctx.Events.Publish(new CivilizationCreatedEvent { CivId = civ.Id, CapitalSettlementId = capital });
            civId = civ.Id;
            return true;
        }

        public void Destroy(int civId)
        {
            var c = _s.Civ(civId);
            if (c == null || !c.Alive) return;
            var ids = _s.SettlementListOf(civId).ToArray();
            foreach (int sid in ids)
            {
                var st = _s.Settlement(sid);
                if (st != null) Settlements.Abandon(st);
            }
            Collapse(c);
        }

        /// <summary>Hauptstadt neu wählen (größte Siedlung) oder Zusammenbruch, wenn keine Siedlung mehr übrig ist.</summary>
        internal void OnSettlementLost(int civId, int settlementId)
        {
            var c = _s.Civ(civId);
            if (c == null || !c.Alive) return;
            var list = _s.SettlementListOf(civId);
            if (list.Count == 0) { Collapse(c); return; }
            if (c.CapitalSettlementId == settlementId || _s.Settlement(c.CapitalSettlementId) == null)
                c.CapitalSettlementId = LargestSettlement(list);
        }

        internal int LargestSettlement(List<int> list)
        {
            int best = 0;
            double bestPop = -1;
            foreach (int sid in list)
            {
                var st = _s.Settlements[sid];
                if (st.Population > bestPop) { bestPop = st.Population; best = sid; }
            }
            return best;
        }

        private void Collapse(Civilization c)
        {
            if (!c.Alive) return;
            c.Alive = false;
            c.CapitalSettlementId = 0;
            _alive.Remove(c.Id);
            _s.Ctx.Events.Publish(new CivilizationCollapsedEvent { CivId = c.Id });
        }

        internal void RebuildAliveList()
        {
            _alive.Clear();
            foreach (int id in _s.CivOrder)
                if (_s.Civs[id].Alive) _alive.Add(id);
        }
    }
}
