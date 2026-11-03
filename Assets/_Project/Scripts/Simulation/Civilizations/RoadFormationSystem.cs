using System;
using System.Collections.Generic;
using WorldBeyond.Core;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Simulation.Civilizations
{
    /// <summary>
    /// Trampelpfade werden zu Wegen (Nutzervorgabe 2026-11-03: „Wege sollen sich bilden können“).
    /// Bewohner und Arbeiter gehen täglich von der Tür ihres Gebäudes zum Ortskern; der Weg folgt dem kürzesten Pfad und
    /// bevorzugt vorhandene Wege, so bündeln sich die Pfade. Dazu zählen Einheiten, die gerade unterwegs sind.
    /// Ist der Verkehr auf einer Kachel groß genug, wird sie zum Weg (TileFlags.Road, im Weltspielstand).
    /// Der Verkehr verblasst langsam; er liegt in CivState.Traffic und wird mit den Civ-Daten gespeichert.
    /// Arbeiter sind Zahlen, keine Einheiten (Phase 3); die Gänge werden daher aus Belegung und Wohnraum geschätzt.
    /// </summary>
    public sealed class RoadFormationSystem : ISimulationSystem
    {
        public const int IntervalTicks = 60;
        /// <summary>Verkehr (Personenjahre), ab dem eine Kachel zum Weg wird.</summary>
        public const double RoadThreshold = 12.0;
        /// <summary>Anteil des Verkehrs, der nach einem Jahr ohne neue Gänge übrig ist.</summary>
        public const double KeepPerYear = 0.8;
        /// <summary>Gewicht einer Einheit, die bei einer Stichprobe auf der Kachel unterwegs ist.</summary>
        public const double UnitWeight = 0.5;
        public const double MinTraffic = 0.05;

        private const int CostRoad = 2, CostOpen = 3;

        private readonly CivState _s;
        private readonly SettlementRegistry _settlements;
        private readonly List<EntityId> _units = new List<EntityId>();
        private readonly List<int> _keys = new List<int>();

        // Wiederverwendete Puffer für die Wegsuche im Umkreis einer Siedlung.
        private int[] _dist = Array.Empty<int>();
        private int[] _parent = Array.Empty<int>();
        private readonly List<(int cost, int idx)> _heap = new List<(int, int)>();
        private int _ox, _oy, _w, _h;

        public RoadFormationSystem(CivState state, SettlementRegistry settlements)
        {
            _s = state;
            _settlements = settlements;
        }

        public string Name => "RoadFormation";
        public int Order => SystemOrder.Settlements + 6;
        public int TickInterval => IntervalTicks;

        public void Tick(SimContext ctx)
        {
            double dt = (double)IntervalTicks / CivTime.TicksPerYear;
            foreach (int sid in _s.SettlementOrder) Commute(_s.Settlements[sid], dt);
            SampleUnits(ctx);
            Decay(dt);
        }

        private void Commute(Settlement st, double dt)
        {
            if (st.Buildings.Count <= 1) return;
            var home = CivState.ArrivalTile(_s.World, st.X, st.Y, MovementClass.Foot);
            if (home.x < 0) return;
            int r = TerritoryService.RadiusOf(st.Tier) + 2;
            if (!Search(home.x, home.y, st.X - r, st.Y - r, SettlementRegistry.CoreSize + 2 * r)) return;

            double fill = st.HousingCapacity > 0 ? Math.Min(1.0, st.Population / st.HousingCapacity) : 0.0;
            foreach (var b in st.Buildings)
            {
                var rec = _s.Buildings.GetRecord(b);
                if (rec == null || !rec.Completed) continue;
                var d = _s.Content.Buildings.Get(rec.Type);
                if (d.Category == BuildingCategory.Center) continue;
                double people = rec.Workers + d.Housing * fill;
                if (people <= 0) continue;
                if (rec.EntranceX < 0 || _s.World.Get(rec.EntranceX, rec.EntranceY).Building.IsValid)
                    _settlements.AssignEntrance(st, rec);
                if (rec.EntranceX < 0) continue;
                int i = Local(rec.EntranceX, rec.EntranceY);
                if (i < 0 || _dist[i] == int.MaxValue) continue;
                for (; i >= 0; i = _parent[i]) AddTraffic(_ox + i % _w, _oy + i / _w, people * dt);
            }
        }

        private void SampleUnits(SimContext ctx)
        {
            ctx.Units.CopyAliveIds(_units);
            foreach (var id in _units)
            {
                if (!ctx.Units.TryGet(id, out var u) || !u.HasMoveTarget || u.Speed <= 0f) continue;
                var (x, y) = WorldCoords.WorldToTile(u.X, u.Z);
                if (_s.World.InBounds(x, y) && _s.World.Get(x, y).OwnerCivId != 0) AddTraffic(x, y, UnitWeight);
            }
        }

        private void AddTraffic(int x, int y, double amount)
        {
            ref readonly var t = ref _s.World.Get(x, y);
            if ((t.Flags & TileFlags.Road) != 0 || t.Building.IsValid || _s.World.IsWater(x, y)) return;
            int key = y * _s.World.Width + x;
            _s.Traffic.TryGetValue(key, out double v);
            v += amount;
            if (v >= RoadThreshold)
            {
                _s.Traffic.Remove(key);
                _s.World.SetFlags(x, y, TileFlags.Road, TileFlags.None);
                return;
            }
            _s.Traffic[key] = v;
        }

        private void Decay(double dt)
        {
            if (_s.Traffic.Count == 0) return;
            double keep = Math.Pow(KeepPerYear, dt);
            _keys.Clear();
            _keys.AddRange(_s.Traffic.Keys);
            foreach (int k in _keys)
            {
                double v = _s.Traffic[k] * keep;
                if (v < MinTraffic) _s.Traffic.Remove(k);
                else _s.Traffic[k] = v;
            }
        }

        // ---------------- Wegsuche (Dijkstra im Quadrat um den Ort) ----------------

        private int Local(int x, int y)
        {
            int lx = x - _ox, ly = y - _oy;
            return lx < 0 || ly < 0 || lx >= _w || ly >= _h ? -1 : ly * _w + lx;
        }

        private bool Search(int sx, int sy, int ox, int oy, int size)
        {
            _ox = ox; _oy = oy; _w = size; _h = size;
            int n = size * size;
            if (_dist.Length < n) { _dist = new int[n]; _parent = new int[n]; }
            for (int i = 0; i < n; i++) { _dist[i] = int.MaxValue; _parent[i] = -1; }
            int start = Local(sx, sy);
            if (start < 0) return false;
            _dist[start] = 0;
            _heap.Clear();
            Push(0, start);
            while (_heap.Count > 0)
            {
                var (c, i) = Pop();
                if (c > _dist[i]) continue;
                int x = _ox + i % _w, y = _oy + i / _w;
                Relax(c, i, x + 1, y); Relax(c, i, x - 1, y); Relax(c, i, x, y + 1); Relax(c, i, x, y - 1);
            }
            return true;
        }

        private void Relax(int c, int from, int x, int y)
        {
            int j = Local(x, y);
            if (j < 0 || !_s.World.InBounds(x, y)) return;
            ref readonly var t = ref _s.World.Get(x, y);
            if (t.Building.IsValid || !_s.World.IsWalkable(x, y, MovementClass.Foot)) return;
            int nc = c + ((t.Flags & TileFlags.Road) != 0 ? CostRoad : CostOpen);
            if (nc >= _dist[j]) return;
            _dist[j] = nc;
            _parent[j] = from;
            Push(nc, j);
        }

        // Binärer Heap; bei gleichen Kosten entscheidet der Index (feste Reihenfolge, deterministisch).
        private static bool Less((int cost, int idx) a, (int cost, int idx) b) => a.cost < b.cost || (a.cost == b.cost && a.idx < b.idx);

        private void Push(int cost, int idx)
        {
            _heap.Add((cost, idx));
            int i = _heap.Count - 1;
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (!Less(_heap[i], _heap[p])) break;
                (_heap[i], _heap[p]) = (_heap[p], _heap[i]);
                i = p;
            }
        }

        private (int cost, int idx) Pop()
        {
            var top = _heap[0];
            int last = _heap.Count - 1;
            _heap[0] = _heap[last];
            _heap.RemoveAt(last);
            int i = 0;
            while (true)
            {
                int l = 2 * i + 1, r = l + 1, m = i;
                if (l < _heap.Count && Less(_heap[l], _heap[m])) m = l;
                if (r < _heap.Count && Less(_heap[r], _heap[m])) m = r;
                if (m == i) break;
                (_heap[i], _heap[m]) = (_heap[m], _heap[i]);
                i = m;
            }
            return top;
        }
    }
}
