using System;
using System.Collections.Generic;
using WorldBeyond.Core;
using WorldBeyond.Simulation.Units;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Simulation.Civilizations
{
    /// <summary>
    /// Unterhalt der Kampfeinheiten: Jeder Soldat isst wie ein Erwachsener aus den Lagern seines Volkes (Hauptort zuerst).
    /// Reicht die Nahrung nicht, desertiert der unversorgte Teil und kehrt als Erwachsener in den Hauptort zurück.
    /// Es gibt keine feste Armeegrenze (Nutzerentscheidung); große Heere begrenzt so die eigene Versorgung.
    /// Ohne Unterhalt hielten Völker 145 Soldaten bei 85 Einwohnern über Jahrhunderte (Probe-Merge 2026-10-27).
    /// </summary>
    public sealed class ArmyUpkeepSystem : ISimulationSystem
    {
        public const int IntervalTicks = 60;
        /// <summary>Anteil der unversorgten Soldaten, der je Jahr desertiert.</summary>
        public const double DesertionPerYear = 0.5;

        private readonly CivState _s;
        private readonly List<EntityId> _units = new List<EntityId>();
        private readonly List<EntityId> _ofCiv = new List<EntityId>();

        public ArmyUpkeepSystem(CivState state) => _s = state;

        public string Name => "ArmyUpkeep";
        public int Order => SystemOrder.Settlements + 5;
        public int TickInterval => IntervalTicks;

        public void Tick(SimContext ctx)
        {
            double dt = (double)IntervalTicks / CivTime.TicksPerYear;
            ctx.Units.CopyAliveIds(_units);
            foreach (int civId in _s.CivOrder)
            {
                var civ = _s.Civs[civId];
                if (!civ.Alive) continue;
                _ofCiv.Clear();
                foreach (var id in _units)
                {
                    var u = ctx.Units.Get(id);
                    if (u.CivId == civId && ctx.UnitTypes.TryGet(u.Type, out var t) && t.Role >= UnitRole.Infantry) _ofCiv.Add(id);
                }
                if (_ofCiv.Count == 0) continue;
                double need = _ofCiv.Count * SettlementSimulationSystem.FoodPerAdult * dt;
                double paid = Pay(civ, need);
                if (paid >= need - 1e-9) continue;

                // Unversorgt: erwarteter Anteil desertiert; Bruchteile sammeln, damit auch kleine Heere schrumpfen.
                double unfed = _ofCiv.Count * (1.0 - paid / need);
                double owed = civ.DesertionOwed + unfed * DesertionPerYear * dt;   // im Spielstand (Determinismus nach Laden)
                int leave = Math.Min(_ofCiv.Count, (int)Math.Floor(owed));
                civ.DesertionOwed = owed - leave;
                var home = _s.Settlement(civ.CapitalSettlementId);
                for (int i = 0; i < leave; i++)
                {
                    var id = _ofCiv[_ofCiv.Count - 1 - i];   // zuletzt ausgehobene zuerst (feste Reihenfolge)
                    if (!ctx.Units.Despawn(id)) continue;
                    if (home != null) home.Adults += 1;
                }
            }
        }

        private double Pay(Civilization civ, double need)
        {
            double left = need;
            var cap = _s.Settlement(civ.CapitalSettlementId);
            if (cap != null) left -= Take(cap, left);
            foreach (int sid in _s.SettlementListOf(civ.Id))
            {
                if (left <= 1e-9) break;
                if (sid == civ.CapitalSettlementId) continue;
                left -= Take(_s.Settlements[sid], left);
            }
            return need - Math.Max(0, left);
        }

        private static double Take(Settlement st, double amount)
        {
            double have = st.Stock[(int)ResourceType.Food];
            double take = Math.Min(have, amount);
            st.Stock[(int)ResourceType.Food] = have - take;
            return take;
        }
    }
}
