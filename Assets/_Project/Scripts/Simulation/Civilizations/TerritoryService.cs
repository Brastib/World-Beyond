using System;
using System.Collections.Generic;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Simulation.Civilizations
{
    /// <summary>
    /// Territorium aus Einflusskreisen der Siedlungen. Einzige Wahrheit ist TileData.OwnerCivId, geschrieben nur hier
    /// über IWorldGrid.SetOwner. Neuberechnung gebündelt im TerritorySystem (nicht jeden Tick), nur im geänderten Rechteck.
    /// Konflikt: Die Kachel gehört der Siedlung mit dem größten Einfluss (Radius − Abstand); Gleichstand → kleinere Siedlungs-ID.
    /// </summary>
    public sealed class TerritoryService : ITerritoryService
    {
        public static int RadiusOf(SettlementTier tier)
        {
            switch (tier)
            {
                case SettlementTier.Camp: return 5;
                case SettlementTier.Village: return 7;
                case SettlementTier.Town: return 9;
                case SettlementTier.City: return 12;
                default: return 15;
            }
        }

        public const int MaxRadius = 15;

        private readonly CivState _s;
        private bool _dirty;
        private int _minX, _minY, _maxX, _maxY;
        private readonly Dictionary<int, int> _tilesByCiv = new Dictionary<int, int>();
        private readonly Dictionary<long, int> _borders = new Dictionary<long, int>();
        private readonly List<Settlement> _candidates = new List<Settlement>();

        public TerritoryService(CivState state) => _s = state ?? throw new ArgumentNullException(nameof(state));

        public bool IsDirty => _dirty;

        public int OwnerAt(int x, int y) => _s.World.InBounds(x, y) ? _s.World.Get(x, y).OwnerCivId : 0;

        public int TerritoryTiles(int civId) => _tilesByCiv.TryGetValue(civId, out int n) ? n : 0;

        public int SharedBorderLength(int civA, int civB)
        {
            if (civA == civB || civA <= 0 || civB <= 0) return 0;
            return _borders.TryGetValue(PairKey(civA, civB), out int n) ? n : 0;
        }

        public void MarkDirty(int settlementId)
        {
            var st = _s.Settlement(settlementId);
            if (st != null) MarkArea(st.X, st.Y, MaxRadius + 1);
        }

        internal void MarkReleased(Settlement st) => MarkArea(st.X, st.Y, MaxRadius + 1);

        // Ohne offene Neuberechnung sind die Grenzen bedeutungslos: Nullen speichern, damit gleicher Zustand gleiche Bytes ergibt.
        internal TerritoryDirtyDto CaptureDirty() => _dirty
            ? new TerritoryDirtyDto { Dirty = true, MinX = _minX, MinY = _minY, MaxX = _maxX, MaxY = _maxY }
            : new TerritoryDirtyDto();

        /// <summary>Nach dem Laden: Besitz steht bereits in der Welt (Sektion world); nur offene Neuberechnung und Statistik herstellen.</summary>
        internal void RestoreDirty(TerritoryDirtyDto d, int width, int height)
        {
            _dirty = d != null && d.Dirty;
            if (_dirty)
            {
                _minX = Math.Max(0, d.MinX); _minY = Math.Max(0, d.MinY);
                _maxX = Math.Min(width - 1, d.MaxX); _maxY = Math.Min(height - 1, d.MaxY);
            }
            else _minX = _minY = _maxX = _maxY = 0;
            RecountStatistics();
        }

        internal void MarkAll()
        {
            _dirty = true;
            _minX = 0; _minY = 0; _maxX = _s.World.Width - 1; _maxY = _s.World.Height - 1;
        }

        private void MarkArea(int cx, int cy, int r)
        {
            int minX = Math.Max(0, cx - r), minY = Math.Max(0, cy - r);
            int maxX = Math.Min(_s.World.Width - 1, cx + r + 1), maxY = Math.Min(_s.World.Height - 1, cy + r + 1);
            if (!_dirty) { _minX = minX; _minY = minY; _maxX = maxX; _maxY = maxY; _dirty = true; return; }
            _minX = Math.Min(_minX, minX); _minY = Math.Min(_minY, minY);
            _maxX = Math.Max(_maxX, maxX); _maxY = Math.Max(_maxY, maxY);
        }

        /// <summary>Berechnet das geänderte Rechteck neu und zählt Gebiete und Grenzen. Liefert die Zahl geänderter Kacheln.</summary>
        internal int Recompute()
        {
            if (!_dirty) return 0;
            _dirty = false;
            var world = _s.World;

            _candidates.Clear();
            foreach (int id in _s.SettlementOrder)
            {
                var st = _s.Settlements[id];
                var c = _s.Civ(st.CivId);
                if (c == null || !c.Alive) continue;
                int r = RadiusOf(st.Tier);
                if (st.X + 1 + r < _minX || st.X - r > _maxX || st.Y + 1 + r < _minY || st.Y - r > _maxY) continue;
                _candidates.Add(st);
            }

            int changed = 0;
            for (int y = _minY; y <= _maxY; y++)
            {
                for (int x = _minX; x <= _maxX; x++)
                {
                    int owner = 0;
                    float best = 0f;
                    for (int i = 0; i < _candidates.Count; i++)
                    {
                        var st = _candidates[i];
                        float r = RadiusOf(st.Tier);
                        float dx = x + 0.5f - (st.X + 1f), dy = y + 0.5f - (st.Y + 1f);
                        float infl = r - (float)Math.Sqrt(dx * dx + dy * dy);
                        if (infl > best) { best = infl; owner = st.CivId; }   // gleicher Einfluss: erste (kleinste ID) bleibt
                    }
                    if (world.Get(x, y).OwnerCivId != owner)
                    {
                        world.SetOwner(x, y, owner);
                        changed++;
                    }
                }
            }
            RecountStatistics();
            return changed;
        }

        private void RecountStatistics()
        {
            _tilesByCiv.Clear();
            _borders.Clear();
            var world = _s.World;
            int w = world.Width, h = world.Height;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int o = world.Get(x, y).OwnerCivId;
                    if (o == 0) continue;
                    _tilesByCiv[o] = (_tilesByCiv.TryGetValue(o, out int n) ? n : 0) + 1;
                    if (x + 1 < w) CountEdge(o, world.Get(x + 1, y).OwnerCivId);
                    if (y + 1 < h) CountEdge(o, world.Get(x, y + 1).OwnerCivId);
                }
            }
        }

        private void CountEdge(int a, int b)
        {
            if (b == 0 || b == a) return;
            long k = PairKey(a, b);
            _borders[k] = (_borders.TryGetValue(k, out int n) ? n : 0) + 1;
        }

        private static long PairKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
    }

    /// <summary>Gebündelte Territoriums-Neuberechnung (Order 700, alle 5 Spielsekunden).</summary>
    public sealed class TerritorySystem : ISimulationSystem
    {
        private readonly TerritoryService _territory;
        public TerritorySystem(TerritoryService territory) => _territory = territory ?? throw new ArgumentNullException(nameof(territory));
        public string Name => "Territory";
        public int Order => SystemOrder.Settlements + 10;
        public int TickInterval => 50;
        public void Tick(SimContext ctx) => _territory.Recompute();
    }
}
