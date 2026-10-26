using System;
using System.Collections.Generic;
using WorldBeyond.Core;
using WorldBeyond.Simulation.Units;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Simulation.Civilizations
{
    /// <summary>
    /// Standortwahl für neue Siedlungen. Bewertet Kacheln im Umkreis nach Gründbarkeit, Erreichbarkeit zu Fuß,
    /// Fruchtbarkeit, Wald, Stein, Erz und Wasser in der Nähe. Sampling über IWorldQuery.FindBestTiles statt Vollsuche.
    /// </summary>
    public sealed class ExpansionPlanner
    {
        public const int SearchRadius = 30;
        public const int IdealDistance = 16;

        private readonly CivState _s;
        private readonly SettlementRegistry _settlements;
        private readonly List<TileScore> _results = new List<TileScore>();

        public ExpansionPlanner(CivState state, SettlementRegistry settlements)
        {
            _s = state; _settlements = settlements;
        }

        public bool TryFindSite(int civId, int fromX, int fromY, int radius, out int x, out int y, out float score)
        {
            x = y = -1; score = 0f;
            var pathfinder = _s.Ctx.TryGet<IPathfinder>(out var pf) ? pf : null;
            var world = _s.World;
            _s.World.FindBestTiles(fromX, fromY, radius, (int tx, int ty, in TileData t) =>
            {
                if (t.OwnerCivId != 0 && t.OwnerCivId != civId) return 0f;
                if (world.IsWater(tx, ty) || t.Building.IsValid) return 0f;
                if (IsReserved(civId, tx, ty)) return 0f;
                if (!_settlements.CanFoundAt(civId, tx, ty, out _)) return 0f;
                float v = SiteValue(tx, ty);
                if (v <= 0f) return 0f;
                int dx = tx - fromX, dy = ty - fromY;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                v *= 1f / (1f + Math.Abs(dist - IdealDistance) / IdealDistance);
                return v;
            }, 8, _results);

            foreach (var c in _results)
            {
                if (pathfinder != null && !pathfinder.AreConnected(fromX, fromY, c.X, c.Y, MovementClass.Foot)) continue;
                x = c.X; y = c.Y; score = c.Score;
                return true;
            }
            return false;
        }

        /// <summary>Wert eines Standorts aus der Umgebung (Radius 5): Nahrung, Holz, Stein, Erz, Wasser.</summary>
        public float SiteValue(int cx, int cy)
        {
            var world = _s.World;
            float fert = 0f; int land = 0, forest = 0, stone = 0, ore = 0, water = 0;
            for (int y = cy - 5; y <= cy + 6; y++)
                for (int x = cx - 5; x <= cx + 6; x++)
                {
                    if (!world.InBounds(x, y)) continue;
                    if (world.IsWater(x, y)) { water++; continue; }
                    ref readonly var t = ref world.Get(x, y);
                    land++;
                    fert += t.Fertility;
                    if (t.Resource == ResourceType.Wood) forest++;
                    else if (t.Resource == ResourceType.Stone) stone++;
                    else if (t.Resource == ResourceType.Ore || t.Resource == ResourceType.Gold) ore++;
                }
            if (land < 40) return 0f;
            float v = fert / land / 255f * 4f;          // Fruchtbarkeit wichtigster Faktor
            v += Math.Min(forest, 20) * 0.08f;
            v += Math.Min(stone, 8) * 0.06f + Math.Min(ore, 6) * 0.08f;
            v += Math.Min(water, 10) * 0.04f;
            return v;
        }

        private bool IsReserved(int civId, int x, int y)
        {
            int min2 = SettlementRegistry.MinSettlementDistance * SettlementRegistry.MinSettlementDistance;
            foreach (int sid in _s.SettlementOrder)
            {
                var st = _s.Settlements[sid];
                if (st.ExpansionTargetX < 0) continue;
                int dx = st.ExpansionTargetX - x, dy = st.ExpansionTargetY - y;
                if (dx * dx + dy * dy < min2) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Siedler sind echte Einheiten im UnitStore. Sie laufen über das MovementSystem (World) zum Ziel und gründen dort
    /// eine Siedlung. Ist der Ort bei Ankunft nicht mehr gültig, suchen sie in der Nähe neu oder kehren heim.
    /// Ihr Zustand steht vollständig im UnitTask (Kind Settle/MoveTo, TargetRef = Heimatsiedlung) und wird mit den
    /// Einheiten gespeichert.
    /// </summary>
    public sealed class SettlerSystem
    {
        public static readonly ResourceCost SettlerCost = new ResourceCost { Food = 40, Wood = 30 };
        public static readonly ResourceCost SettlerStartStock = new ResourceCost { Food = 30, Wood = 20 };   // mitgeführt
        public const int SettlerPopulation = 5;
        public const int ReplanRadius = 10;

        private readonly CivState _s;
        private readonly SettlementRegistry _settlements;
        private readonly ExpansionPlanner _planner;

        public SettlerSystem(CivState state, SettlementRegistry settlements, ExpansionPlanner planner)
        {
            _s = state; _settlements = settlements; _planner = planner;
            _s.Ctx.Events.Subscribe<UnitArrivedEvent>(OnArrived);
            _s.Ctx.Events.Subscribe<UnitMoveFailedEvent>(OnMoveFailed);
        }

        private bool IsSettler(EntityId id, out UnitView u) =>
            _s.Ctx.Units.TryGet(id, out u) && u.Type.Equals(_s.Content.SettlerType);

        private void OnArrived(UnitArrivedEvent e)
        {
            if (!IsSettler(e.Unit, out var u)) return;
            var (tx, ty) = WorldCoords.WorldToTile(u.Task.TargetX, u.Task.TargetZ);
            if (u.Task.Kind == UnitTaskKind.Settle)
            {
                var origin = _s.Settlement(u.Task.TargetRef);
                ClearReservation(origin, tx, ty);
                if (_settlements.TryFound(u.CivId, tx, ty, SettlerPopulation, SettlerStartStock, false, out _, out _))
                {
                    _s.Ctx.Units.Despawn(e.Unit);
                    return;
                }
                Replan(e.Unit, u, tx, ty);
            }
            else if (u.Task.Kind == UnitTaskKind.MoveTo)
            {
                // Heimkehr: Siedler werden wieder Einwohner (Vorräte gingen unterwegs verloren).
                var home = _s.Settlement(u.Task.TargetRef);
                if (home != null && home.CivId == u.CivId) home.Adults += SettlerPopulation;
                _s.Ctx.Units.Despawn(e.Unit);
            }
        }

        private void OnMoveFailed(UnitMoveFailedEvent e)
        {
            if (!IsSettler(e.Unit, out var u)) return;
            var (cx, cy) = WorldCoords.WorldToTile(u.X, u.Z);
            if (u.Task.Kind == UnitTaskKind.Settle)
            {
                var (tx, ty) = WorldCoords.WorldToTile(u.Task.TargetX, u.Task.TargetZ);
                ClearReservation(_s.Settlement(u.Task.TargetRef), tx, ty);
                Replan(e.Unit, u, cx, cy);
            }
            else
            {
                // Auch der Heimweg ist unmöglich: hier gründen oder auflösen.
                if (!_settlements.TryFound(u.CivId, cx, cy, SettlerPopulation, SettlerStartStock, false, out _, out _))
                    _s.Ctx.Units.Despawn(e.Unit);
                else _s.Ctx.Units.Despawn(e.Unit);
            }
        }

        private void Replan(EntityId id, UnitView u, int nearX, int nearY)
        {
            if (_planner.TryFindSite(u.CivId, nearX, nearY, ReplanRadius, out int x, out int y, out _))
            {
                var origin = _s.Settlement(u.Task.TargetRef);
                if (origin != null) { origin.ExpansionTargetX = x; origin.ExpansionTargetY = y; }
                var task = u.Task;
                task.TargetX = WorldCoords.TileCenterX(x);
                task.TargetZ = WorldCoords.TileCenterZ(y);
                _s.Ctx.Units.SetTask(id, task);
                _s.Ctx.Units.SetMoveTarget(id, task.TargetX, task.TargetZ);
                return;
            }
            var home = _s.Settlement(u.Task.TargetRef);
            if (home == null || home.CivId != u.CivId)
            {
                _s.Ctx.Units.Despawn(id);
                return;
            }
            var (hx, hz) = CivState.SettlementCenter(home);
            _s.Ctx.Units.SetTask(id, new UnitTask { Kind = UnitTaskKind.MoveTo, TargetRef = home.Id, TargetX = hx, TargetZ = hz });
            _s.Ctx.Units.SetMoveTarget(id, hx, hz);
        }

        private static void ClearReservation(Settlement origin, int x, int y)
        {
            if (origin != null && origin.ExpansionTargetX == x && origin.ExpansionTargetY == y)
                origin.ExpansionTargetX = origin.ExpansionTargetY = -1;
        }

        /// <summary>Zählt Siedler einer Zivilisation, die unterwegs sind.</summary>
        internal int CountTraveling(int civId)
        {
            int n = 0;
            var units = _s.Ctx.Units;
            units.ForEachAlive(id =>
            {
                if (units.TryGet(id, out var u) && u.CivId == civId && u.Type.Equals(_s.Content.SettlerType)) n++;
            });
            return n;
        }
    }
}
