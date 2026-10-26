using System;
using System.Collections.Generic;
using WorldBeyond.Core;
using WorldBeyond.Simulation.Combat;
using WorldBeyond.Simulation.Units;

namespace WorldBeyond.Simulation.AI
{
    public interface IArmyAI : ISimulationSystem { }   // SYSTEM_INTERFACES §10, TickInterval ≈ 10
}

namespace WorldBeyond.Simulation.Military
{
    /// <summary>
    /// Operative Armee-KI (Sandbox-Regel, INTEGRATION.md §0 Regel 4): bildet Armeen aus freien Militäreinheiten,
    /// wählt im Krieg Ziele, marschiert, greift an, zieht sich bei Unterlegenheit zurück und verteidigt im Frieden.
    /// Läuft ohne Spielerbefehle. Spielerbefehle über IArmyRegistry haben 60 s Vorrang.
    /// Order 100 (AI), jede Spielsekunde. Taktik pro Einheit bleibt im CombatSystem.
    /// </summary>
    public sealed class ArmyAISystem : AI.IArmyAI
    {
        private const int DecisionIntervalTicks = 50;     // Entscheidung je Armee alle 5 s
        private const int RecruitIntervalTicks = 100;     // Rekrutierungsanfrage je Civ höchstens alle 10 s
        private const int MaxArmySize = 30;
        private const float RetreatRatio = 0.4f;          // eigene Stärke < 40 % der Feindstärke am Ziel → Rückzug
        private const float LocalEnemyRadius = 250f;
        private const float ArrivalRadius = 20f;
        private const float FormationSpacing = 4f;
        private const float SlotTolerance = 6f;

        private readonly ArmyRegistry _registry;
        private readonly IUnitStore _units;
        private readonly MilitaryCatalog _catalog;
        private readonly CombatSystem _combat;
        private readonly CombatEnvironment _combatEnv;
        private readonly ArmyEnvironment _env;

        private readonly List<EntityId> _alive = new List<EntityId>();
        private readonly SortedDictionary<int, List<EntityId>> _freeByCiv = new SortedDictionary<int, List<EntityId>>();
        private readonly SortedSet<int> _civs = new SortedSet<int>();
        private readonly Dictionary<int, long> _lastRecruit = new Dictionary<int, long>();
        private readonly List<Army> _scratchArmies = new List<Army>();
        // Zuletzt von der Armee-KI gesetztes Bewegungsziel je Einheit. Liegt im ArmyRegistry und wird mit "armies" gespeichert,
        // sonst gälte ein KI-Ziel nach dem Laden als fremd und die Simulation liefe anders weiter als ohne Laden.
        private Dictionary<EntityId, (float x, float z)> _issued => _registry.IssuedMoveTargets;

        private static bool SameSpot(float ax, float az, float bx, float bz) => MathF.Abs(ax - bx) <= 1f && MathF.Abs(az - bz) <= 1f;

        public ArmyAISystem(ArmyRegistry registry, IUnitStore units, MilitaryCatalog catalog, CombatSystem combat,
            CombatEnvironment combatEnv, ArmyEnvironment env)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _combat = combat ?? throw new ArgumentNullException(nameof(combat));
            _combatEnv = combatEnv ?? throw new ArgumentNullException(nameof(combatEnv));
            _env = env ?? throw new ArgumentNullException(nameof(env));
        }

        public string Name => "ArmyAI";
        public int Order => SystemOrder.AI + 10;   // nach der Civ-KI (100), vor Wirtschaft (200)
        public int TickInterval => 10;

        public void Tick(SimContext ctx)
        {
            long tick = ctx.Clock.Tick;
            _registry.Prune();
            if (_issued.Count > 0 && tick % 600 == 0) PruneIssued();
            CollectFreeUnits();
            FormArmies();

            _scratchArmies.Clear();
            _scratchArmies.AddRange(_registry.All);
            foreach (var a in _scratchArmies)
            {
                if (!_registry.Exists(a.Id)) continue;
                bool manual = tick < a.ManualUntilTick;
                if (!manual && tick - a.LastDecisionTick >= DecisionIntervalTicks)
                {
                    a.LastDecisionTick = tick;
                    Decide(a);
                }
                Execute(a);
            }

            foreach (int civ in _civs) MaybeRecruit(civ, tick);
        }

        private void PruneIssued()
        {
            var dead = new List<EntityId>();
            foreach (var id in _issued.Keys) if (!_units.Exists(id)) dead.Add(id);
            foreach (var id in dead) _issued.Remove(id);
        }

        // ---------------- Armeebildung ----------------

        private void CollectFreeUnits()
        {
            foreach (var l in _freeByCiv.Values) l.Clear();
            _civs.Clear();
            _units.CopyAliveIds(_alive);
            foreach (var id in _alive)
            {
                if (!_units.TryGet(id, out var u) || !_catalog.TryGet(u.Type, out _)) continue;
                _civs.Add(u.CivId);
                if (_registry.TryGetArmyOf(id, out _)) continue;
                if (!_freeByCiv.TryGetValue(u.CivId, out var list)) _freeByCiv[u.CivId] = list = new List<EntityId>();
                list.Add(id);
            }
        }

        private void FormArmies()
        {
            foreach (var kv in _freeByCiv)
            {
                if (kv.Value.Count == 0) continue;
                // Neue Einheiten verstärken die kleinste autonome Armee der Civ, sonst neue Armee
                Army target = null;
                foreach (var a in _registry.All)
                    if (a.CivId == kv.Key && a.Autonomous && a.Units.Count < MaxArmySize && (target == null || a.Units.Count < target.Units.Count))
                        target = a;
                int i = 0;
                if (target != null)
                    for (; i < kv.Value.Count && target.Units.Count < MaxArmySize; i++) _registry.AddUnit(target.Id, kv.Value[i]);
                while (i < kv.Value.Count)
                {
                    int take = Math.Min(MaxArmySize, kv.Value.Count - i);
                    _registry.Create(kv.Key, kv.Value.GetRange(i, take), autonomous: true);
                    i += take;
                }
            }
        }

        // ---------------- Entscheidung ----------------

        private void Decide(Army a)
        {
            if (!HasEnemy(a.CivId))
            {
                if (a.Order.Kind != ArmyOrderKind.Defend)
                    _registry.SetOrder(a.Id, new ArmyOrder { Kind = ArmyOrderKind.Defend, X = a.HomeX, Z = a.HomeZ }, byAI: true);
                return;
            }

            _registry.Center(a, out float cx, out float cz);
            if (!TryPickObjective(a, cx, cz, out var obj))
            {
                if (a.Order.Kind != ArmyOrderKind.Defend)
                    _registry.SetOrder(a.Id, new ArmyOrder { Kind = ArmyOrderKind.Defend, X = a.HomeX, Z = a.HomeZ }, byAI: true);
                return;
            }

            float own = _registry.Strength(a);
            float enemy = EnemyStrengthNear(a.CivId, obj.X, obj.Z, LocalEnemyRadius);
            if (enemy > 0f && own < RetreatRatio * enemy)
            {
                _registry.SetOrder(a.Id, new ArmyOrder { Kind = ArmyOrderKind.Retreat, X = a.HomeX, Z = a.HomeZ }, byAI: true);
                return;
            }

            var order = obj.SettlementId > 0
                ? new ArmyOrder { Kind = ArmyOrderKind.AttackSettlement, TargetRef = obj.SettlementId, X = obj.X, Z = obj.Z }
                : new ArmyOrder { Kind = ArmyOrderKind.AttackPosition, X = obj.X, Z = obj.Z };
            _registry.SetOrder(a.Id, order, byAI: true);
        }

        private bool HasEnemy(int civ)
        {
            foreach (int other in _civs)
                if (other != civ && _combatEnv.AreHostile(civ, other)) return true;
            if (_env.Objectives != null)
                foreach (var o in _env.Objectives(civ))
                    if (o.OwnerCiv != civ && _combatEnv.AreHostile(civ, o.OwnerCiv)) return true;
            return false;
        }

        private bool TryPickObjective(Army a, float cx, float cz, out ArmyObjective best)
        {
            best = default;
            // 1. Kriegsziele von War (absteigend nach Wert geliefert): erstes feindliches Ziel
            if (_env.Objectives != null)
                foreach (var o in _env.Objectives(a.CivId))
                    if (o.OwnerCiv != a.CivId && _combatEnv.AreHostile(a.CivId, o.OwnerCiv)) { best = o; return true; }

            // 2. Rückfall: nächste feindliche Militäreinheit, sonst nächste feindliche Einheit
            float bestD = float.MaxValue;
            bool found = false, bestIsMilitary = false;
            foreach (var id in _alive)
            {
                if (!_units.TryGet(id, out var u) || u.CivId == a.CivId || !_combatEnv.AreHostile(a.CivId, u.CivId)) continue;
                bool mil = _catalog.TryGet(u.Type, out _);
                if (bestIsMilitary && !mil) continue;
                float dx = u.X - cx, dz = u.Z - cz;
                float d = dx * dx + dz * dz;
                if ((mil && !bestIsMilitary) || d < bestD)
                {
                    bestD = d; found = true; bestIsMilitary = mil;
                    best = new ArmyObjective(u.X, u.Z, 1f, u.CivId);
                }
            }
            return found;
        }

        private float EnemyStrengthNear(int civ, float x, float z, float radius)
        {
            float r2 = radius * radius, s = 0f;
            foreach (var id in _alive)
            {
                if (!_units.TryGet(id, out var u) || u.CivId == civ || !_combatEnv.AreHostile(civ, u.CivId)) continue;
                float dx = u.X - x, dz = u.Z - z;
                if (dx * dx + dz * dz > r2 || u.MaxHp <= 0f) continue;
                s += _catalog.GetCombatStrength(u.Type) * (u.Hp / u.MaxHp);
            }
            return s;
        }

        private void MaybeRecruit(int civ, long tick)
        {
            if (_env.Recruit == null || !HasEnemy(civ)) return;
            if (_lastRecruit.TryGetValue(civ, out long last) && tick - last < RecruitIntervalTicks) return;
            int have = 0;
            foreach (var a in _registry.All) if (a.CivId == civ) have += a.Units.Count;
            int missing = _env.DesiredArmySize - have;
            if (missing <= 0) return;
            _lastRecruit[civ] = tick;
            _env.Recruit(civ, MilitaryRole.Rifleman, missing);
        }

        // ---------------- Ausführung ----------------

        private void Execute(Army a)
        {
            switch (a.Order.Kind)
            {
                case ArmyOrderKind.None:
                    return;
                case ArmyOrderKind.AttackUnit:
                    if (!_units.TryGet(a.Order.TargetUnit, out _))
                    {
                        _registry.SetOrder(a.Id, default, byAI: true);
                        return;
                    }
                    var task = new UnitTask { Kind = UnitTaskKind.Attack, TargetEntity = a.Order.TargetUnit };
                    foreach (var u in a.Units)
                    {
                        _combat.SetStance(u, CombatStance.Aggressive);
                        if (_units.TryGet(u, out var v) && v.Task.TargetEntity != a.Order.TargetUnit) _units.SetTask(u, task);
                    }
                    return;
            }

            bool retreat = a.Order.Kind == ArmyOrderKind.Retreat;
            _registry.Center(a, out float cx, out float cz);
            float ddx = a.Order.X - cx, ddz = a.Order.Z - cz;
            bool arrived = ddx * ddx + ddz * ddz <= ArrivalRadius * ArrivalRadius;

            if (arrived)
            {
                if (retreat)
                {
                    _registry.SetOrder(a.Id, new ArmyOrder { Kind = ArmyOrderKind.Defend, X = a.HomeX, Z = a.HomeZ }, byAI: true);
                    retreat = false;
                }
                else if (a.Posture == ArmyPosture.Marching) a.Posture = ArmyPosture.Engaged;
            }

            int n = a.Units.Count;
            int cols = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(n)));
            for (int k = 0; k < n; k++)
            {
                var id = a.Units[k];
                if (!_units.TryGet(id, out var u)) continue;

                var stance = _combat.GetStance(id);
                if (retreat && stance != CombatStance.HoldFire) _combat.SetStance(id, stance = CombatStance.HoldFire);
                else if (!retreat && stance == CombatStance.HoldFire) _combat.SetStance(id, stance = CombatStance.Aggressive);
                if (!retreat)
                {
                    // Spieler-Haltung "Stellung halten" und einzeln befohlene Angriffe respektieren
                    if (stance == CombatStance.HoldPosition || u.Task.Kind == UnitTaskKind.Attack) continue;
                    // Im Gefecht: stehen bleiben, CombatSystem übernimmt Feuer und Verfolgung
                    if (u.State == UnitState.Fighting || _combat.GetTarget(id).IsValid)
                    {
                        if (u.HasMoveTarget && u.State == UnitState.Fighting) _units.ClearMoveTarget(id);
                        continue;
                    }
                }
                // Ein Bewegungsziel, das nicht von der Armee-KI stammt (Spieler, Verfolgung), nicht überschreiben
                if (u.HasMoveTarget && !(_issued.TryGetValue(id, out var last) && SameSpot(last.x, last.z, u.MoveTargetX, u.MoveTargetZ)))
                    continue;

                float sx = a.Order.X + ((k % cols) - (cols - 1) * 0.5f) * FormationSpacing;
                float sz = a.Order.Z + ((k / cols) - (cols - 1) * 0.5f) * FormationSpacing;
                float dx = sx - u.X, dz = sz - u.Z;
                if (dx * dx + dz * dz <= SlotTolerance * SlotTolerance) continue;
                if (!u.HasMoveTarget || !SameSpot(u.MoveTargetX, u.MoveTargetZ, sx, sz))
                {
                    _units.SetMoveTarget(id, sx, sz);
                    _issued[id] = (sx, sz);
                }
            }
        }
    }
}
