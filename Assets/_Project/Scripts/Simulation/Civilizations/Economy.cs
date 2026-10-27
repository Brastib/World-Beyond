using System;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Simulation.Civilizations
{
    /// <summary>
    /// Einziges Ressourcenlager (INTEGRATION.md §3). Nach außen pro Zivilisation, intern Lager pro Siedlung,
    /// damit Transport, Lagergrenzen und lokaler Hunger echte Folgen haben.
    /// </summary>
    public sealed class Economy : IEconomy
    {
        private readonly CivState _s;

        public Economy(CivState state) => _s = state ?? throw new ArgumentNullException(nameof(state));

        public int GetStock(int civId, ResourceType r)
        {
            if (!Resources.IsStockable(r)) return 0;
            double sum = 0;
            foreach (int sid in _s.SettlementListOf(civId)) sum += _s.Settlements[sid].Stock[(int)r];
            return (int)Math.Floor(sum);
        }

        public int GetSettlementStock(int settlementId, ResourceType r)
        {
            var st = _s.Settlement(settlementId);
            return st != null && Resources.IsStockable(r) ? (int)Math.Floor(st.Stock[(int)r]) : 0;
        }

        public bool TryConsume(int civId, ResourceType r, int amount)
        {
            if (!Resources.IsStockable(r) || amount < 0) return false;
            var cost = new ResourceCost();
            cost[r] = amount;
            return TrySpend(civId, cost);
        }

        public bool TrySpend(int civId, in ResourceCost cost)
        {
            var civ = _s.Civ(civId);
            if (civ == null || !civ.Alive) return false;
            if (cost.IsZero) return true;
            foreach (var r in Resources.Stockable)
            {
                if (cost[r] < 0) return false;
                if (GetStock(civId, r) < cost[r]) return false;
            }
            // Abbuchen: Hauptstadt zuerst, dann Siedlungen nach Vorrat (absteigend), bei Gleichstand nach ID.
            var list = _s.SettlementListOf(civId);
            foreach (var r in Resources.Stockable)
            {
                double need = cost[r];
                if (need <= 0) continue;
                var cap = _s.Settlement(civ.CapitalSettlementId);
                if (cap != null) need -= Take(cap, r, need);
                while (need > 1e-9)
                {
                    Settlement richest = null;
                    foreach (int sid in list)
                    {
                        var st = _s.Settlements[sid];
                        if (st == cap || st.Stock[(int)r] <= 1e-9) continue;
                        if (richest == null || st.Stock[(int)r] > richest.Stock[(int)r]) richest = st;
                    }
                    if (richest == null) break;   // durch GetStock-Prüfung ausgeschlossen
                    need -= Take(richest, r, need);
                }
            }
            return true;
        }

        public bool TrySpendAt(int settlementId, in ResourceCost cost)
        {
            var st = _s.Settlement(settlementId);
            if (st == null) return false;
            foreach (var r in Resources.Stockable)
                if (cost[r] < 0 || st.Stock[(int)r] + 1e-9 < cost[r]) return false;
            foreach (var r in Resources.Stockable) st.Stock[(int)r] = Math.Max(0, st.Stock[(int)r] - cost[r]);
            return true;
        }

        public void Add(int civId, ResourceType r, int amount)
        {
            if (!Resources.IsStockable(r) || amount <= 0) return;
            var civ = _s.Civ(civId);
            var cap = civ != null ? _s.Settlement(civ.CapitalSettlementId) : null;
            if (cap != null) Deposit(cap, r, amount);
        }

        internal void Refund(Settlement st, in ResourceCost cost)
        {
            foreach (var r in Resources.Stockable) st.Stock[(int)r] += cost[r];
        }

        /// <summary>Einlagern bis zur Lagergrenze; der Rest verfällt. Liefert die eingelagerte Menge.</summary>
        internal static double Deposit(Settlement st, ResourceType r, double amount)
        {
            int i = (int)r;
            double space = Math.Max(0, st.StorageCapacity - st.Stock[i]);
            double put = Math.Min(space, amount);
            st.Stock[i] += put;
            return put;
        }

        private static double Take(Settlement st, ResourceType r, double amount)
        {
            int i = (int)r;
            double t = Math.Min(st.Stock[i], amount);
            st.Stock[i] -= t;
            return t;
        }
    }
}
