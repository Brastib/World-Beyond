using System;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Simulation.Civilizations
{
    /// <summary>
    /// Detailstufe der Siedlungssimulation. Aktiv (im Fokus der Kamera oder ausgewählt): Aktualisierung jede Spielsekunde.
    /// Entfernt: alle 5 Spielsekunden mit 5-fachem Zeitschritt. Beide Stufen rechnen mit denselben Formeln über die
    /// tatsächlich vergangene Zeit; ein Wechsel ändert nur die Häufigkeit, nie den Zustand.
    /// Der Fokus ist Darstellungszustand und wird nicht gespeichert; die Stufe jeder Siedlung schon.
    /// </summary>
    public sealed class CivLod : ICivSimulationLod
    {
        private bool _hasFocus;
        private float _fx, _fz, _r2;
        private int _selected;

        public void SetFocus(float worldX, float worldZ, float radiusMeters)
        {
            _hasFocus = radiusMeters > 0f;
            _fx = worldX; _fz = worldZ; _r2 = radiusMeters * radiusMeters;
        }

        public void ClearFocus() => _hasFocus = false;

        public void SetSelectedSettlement(int settlementId) => _selected = settlementId;

        internal bool IsActive(Settlement st)
        {
            if (st.Id == _selected) return true;
            if (!_hasFocus) return false;
            var (x, z) = CivState.SettlementCenter(st);
            float dx = x - _fx, dz = z - _fz;
            return dx * dx + dz * dz <= _r2;
        }

        internal static int IntervalOf(Settlement st) => st.ActiveLod ? CivTime.ActiveUpdateTicks : CivTime.DistantUpdateTicks;
    }
}
