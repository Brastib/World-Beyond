using System;
using System.Collections.Generic;
using WorldBeyond.Core;
using WorldBeyond.Simulation.Combat;
using WorldBeyond.Simulation.Units;
using WorldBeyond.Simulation.World;

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
    public sealed class ArmyAISystem : AI.IArmyAI, ISaveable
    {
        private const int DecisionIntervalTicks = 50;     // Entscheidung je Armee alle 5 s
        private const int RecruitIntervalTicks = 100;     // Rekrutierungsanfrage je Civ höchstens alle 10 s
        private const int MaxArmySize = 30;
        private const float RetreatRatio = 0.4f;          // eigene Stärke < 40 % der Feindstärke am Ziel → Rückzug
        private const float LocalEnemyRadius = 250f;
        private const float ArrivalRadius = 20f;
        private const float FormationSpacing = 4f;
        private const float SlotTolerance = 6f;
        private const int FailBackoffTicks = 50;          // nach gescheitertem Weg 5 s warten, je weiterem Fehlschlag doppelt
        private const int MaxFailBackoffTicks = 1200;     // höchstens 2 min
        private const int BlockTargetTicks = 600;         // unerreichbares Armeeziel 60 s meiden, bei Wiederholung doppelt so lange
        private const int MaxBlockTargetTicks = 9600;     // höchstens 16 min
        private const float BlockRadius = 30f;
        private const float HomeThreatRadius = 300f;      // Feind so nah an der Heimat gilt als Bedrohung

        private readonly ArmyRegistry _registry;
        private readonly IUnitStore _units;
        private readonly MilitaryCatalog _catalog;
        private readonly CombatSystem _combat;
        private readonly CombatEnvironment _combatEnv;
        private readonly ArmyEnvironment _env;
        private readonly SimClock _clock;

        private readonly List<EntityId> _alive = new List<EntityId>();
        private readonly SortedDictionary<int, List<EntityId>> _freeByCiv = new SortedDictionary<int, List<EntityId>>();
        private readonly SortedSet<int> _civs = new SortedSet<int>();
        private readonly Dictionary<int, long> _lastRecruit = new Dictionary<int, long>();
        private readonly List<Army> _scratchArmies = new List<Army>();
        // Zuletzt von der Armee-KI gesetztes Bewegungsziel je Einheit (gespeichert, sonst weicht ein geladener
        // Lauf vom ununterbrochenen ab: K7). Ziele, die nicht hier stehen, gelten als fremd und werden nicht überschrieben.
        private readonly Dictionary<EntityId, (float x, float z)> _issued = new Dictionary<EntityId, (float x, float z)>();

        // Gescheiterte Wege (UnitMoveFailedEvent) je Einheit: bis wann nicht neu befehlen, wie oft in Folge gescheitert.
        private readonly Dictionary<EntityId, (long until, int count)> _failed = new Dictionary<EntityId, (long until, int count)>();
        // Unerreichbares Ziel je Armee (Hälfte der Einheiten scheitert): Ort, Ablauf-Tick, wie oft dieser Ort in Folge gesperrt wurde.
        // Abgelaufene Einträge bleiben stehen, damit eine erneute Sperre desselben Orts länger dauert.
        private readonly SortedDictionary<int, (float x, float z, long until, int count)> _blocked = new SortedDictionary<int, (float x, float z, long until, int count)>();

        private static bool SameSpot(float ax, float az, float bx, float bz) => MathF.Abs(ax - bx) <= 1f && MathF.Abs(az - bz) <= 1f;

        public ArmyAISystem(ArmyRegistry registry, IUnitStore units, MilitaryCatalog catalog, CombatSystem combat,
            CombatEnvironment combatEnv, ArmyEnvironment env, ISimEventBus events = null, SimClock clock = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _combat = combat ?? throw new ArgumentNullException(nameof(combat));
            _combatEnv = combatEnv ?? throw new ArgumentNullException(nameof(combatEnv));
            _env = env ?? throw new ArgumentNullException(nameof(env));
            _clock = clock;
            events?.Subscribe<UnitMoveFailedEvent>(OnMoveFailed);
        }

        // Nur Ziele, die die Armee-KI selbst gesetzt hat; fremde Befehle (Spieler, Civ) gehen sie nichts an.
        private void OnMoveFailed(UnitMoveFailedEvent e)
        {
            if (!_issued.TryGetValue(e.Unit, out var last) || !SameSpot(last.x, last.z, e.TargetX, e.TargetZ)) return;
            _issued.Remove(e.Unit);
            long tick = _clock?.Tick ?? 0;
            int count = _failed.TryGetValue(e.Unit, out var f) ? f.count + 1 : 1;
            long wait = Math.Min(MaxFailBackoffTicks, (long)FailBackoffTicks << Math.Min(count - 1, 10));
            _failed[e.Unit] = (tick + wait, count);
        }

        private long Now => _clock?.Tick ?? 0;

        private bool InBackoff(EntityId id, long tick) => _failed.TryGetValue(id, out var f) && f.until > tick;

        private bool IsBlocked(int armyId, float x, float z, long tick)
        {
            return _blocked.TryGetValue(armyId, out var b) && b.until > tick && SameArea(b.x, b.z, x, z);
        }

        private static bool SameArea(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return dx * dx + dz * dz <= BlockRadius * BlockRadius;
        }

        // Scheitert mindestens die Hälfte der Einheiten am Weg, gilt das Ziel als unerreichbar: meiden und sofort neu entscheiden.
        private void CheckUnreachable(Army a, long tick)
        {
            int n = a.Units.Count, failing = 0;
            if (n == 0 || a.Order.Kind == ArmyOrderKind.None || a.Order.Kind == ArmyOrderKind.AttackUnit) return;
            foreach (var id in a.Units) if (InBackoff(id, tick)) failing++;
            if (failing * 2 < n || IsBlocked(a.Id, a.Order.X, a.Order.Z, tick)) return;
            int count = 1;
            if (_blocked.TryGetValue(a.Id, out var prev) && SameArea(prev.x, prev.z, a.Order.X, a.Order.Z)) count = prev.count + 1;
            long wait = Math.Min(MaxBlockTargetTicks, (long)BlockTargetTicks << Math.Min(count - 1, 10));
            _blocked[a.Id] = (a.Order.X, a.Order.Z, tick + wait, count);
            foreach (var id in a.Units)
                if (_failed.TryGetValue(id, out var f)) _failed[id] = (0, f.count);   // neues Ziel sofort versuchen, Zähler bleibt
            a.LastDecisionTick = -1_000_000;
        }

        public string Name => "ArmyAI";
        public int Order => SystemOrder.AI + 10;   // nach der Civ-KI (100), vor Wirtschaft (200)
        public int TickInterval => 10;

        public void Tick(SimContext ctx)
        {
            long tick = ctx.Clock.Tick;
            _registry.Prune();
            if (tick % 600 == 0) PruneIssued(tick);
            CollectFreeUnits();
            FormArmies();

            _scratchArmies.Clear();
            _scratchArmies.AddRange(_registry.All);
            foreach (var a in _scratchArmies)
            {
                if (!_registry.Exists(a.Id)) continue;
                bool manual = tick < a.ManualUntilTick;
                if (!manual) CheckUnreachable(a, tick);
                if (!manual && tick - a.LastDecisionTick >= DecisionIntervalTicks)
                {
                    a.LastDecisionTick = tick;
                    Decide(a);
                }
                Execute(a);
            }

            foreach (int civ in _civs) MaybeRecruit(civ, tick);
        }

        private void PruneIssued(long tick)
        {
            var dead = new List<EntityId>();
            foreach (var id in _issued.Keys) if (!_units.Exists(id)) dead.Add(id);
            foreach (var id in dead) _issued.Remove(id);
            dead.Clear();
            foreach (var id in _failed.Keys) if (!_units.Exists(id)) dead.Add(id);
            foreach (var id in dead) _failed.Remove(id);
            var expired = new List<int>();
            foreach (var kv in _blocked) if (!_registry.Exists(kv.Key)) expired.Add(kv.Key);
            foreach (int id in expired) _blocked.Remove(id);
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

            // Heimat bedroht? Diese Armee verteidigt, solange die übrigen eigenen Truppen dort nicht genügen (Abwägung je Armee,
            // in Id-Reihenfolge, damit nicht alle Armeen gleichzeitig umkehren).
            float homeThreat = EnemyStrengthNear(a.CivId, a.HomeX, a.HomeZ, HomeThreatRadius);
            if (homeThreat > 0f && OwnStrengthNearExcept(a, a.HomeX, a.HomeZ, HomeThreatRadius) < homeThreat * _env.Superiority)
            {
                if (a.Order.Kind != ArmyOrderKind.Defend || !SameSpot(a.Order.X, a.Order.Z, a.HomeX, a.HomeZ))
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
                    if (o.OwnerCiv != a.CivId && _combatEnv.AreHostile(a.CivId, o.OwnerCiv) && !IsBlocked(a.Id, o.X, o.Z, Now))
                    { best = o; return true; }

            // 2. Rückfall: nächste feindliche Militäreinheit, sonst nächste feindliche Einheit
            float bestD = float.MaxValue;
            bool found = false, bestIsMilitary = false;
            foreach (var id in _alive)
            {
                if (!_units.TryGet(id, out var u) || u.CivId == a.CivId || !_combatEnv.AreHostile(a.CivId, u.CivId)) continue;
                if (IsBlocked(a.Id, u.X, u.Z, Now)) continue;
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

        // Stärke der eigenen Truppen im Umkreis ohne Armee a: freie Einheiten und andere Armeen, die dort sind oder dorthin unterwegs sind.
        private float OwnStrengthNearExcept(Army a, float x, float z, float radius)
        {
            float s = 0f, r2 = radius * radius;
            foreach (var other in _registry.All)
            {
                if (other.CivId != a.CivId || other.Id == a.Id) continue;
                _registry.Center(other, out float ox, out float oz);
                float dx = ox - x, dz = oz - z;
                bool there = dx * dx + dz * dz <= r2;
                bool going = other.Order.Kind == ArmyOrderKind.Defend && SameSpot(other.Order.X, other.Order.Z, x, z);
                if (there || going) s += _registry.Strength(other);
            }
            return s;
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

        // Abwägung statt fester Zahl (VISION §2 Regel 6): Ziel ist eigene Stärke ≥ feindliche Stärke × Superiority.
        // Ohne feindliche Truppen (auch im Frieden) genügt eine kleine Grundarmee. Kosten, Unterhalt und Kriegsmüdigkeit wägt die Civ
        // über RecruitWillingness (0..1) ab; was sie sich nicht leisten kann, lehnt IRecruitment ohnehin ab.
        private void MaybeRecruit(int civ, long tick)
        {
            // Auch im Frieden: Grundarmee (MinAttackUnits), soweit die Civ bereit ist (War rekrutiert seit 0a07383 nicht mehr selbst).
            if (_env.Recruit == null) return;
            if (_lastRecruit.TryGetValue(civ, out long last) && tick - last < RecruitIntervalTicks) return;
            int missing = RecruitNeed(civ);
            if (missing <= 0) return;
            _lastRecruit[civ] = tick;
            _env.Recruit(civ, MilitaryRole.Rifleman, missing);
        }

        internal int RecruitNeed(int civ)
        {
            float own = 0f, threat = 0f;
            foreach (var id in _alive)
            {
                if (!_units.TryGet(id, out var u) || u.MaxHp <= 0f) continue;
                float s = _catalog.GetCombatStrength(u.Type) * (u.Hp / u.MaxHp);
                if (s <= 0f) continue;
                if (u.CivId == civ) own += s;
                else if (_combatEnv.AreHostile(civ, u.CivId)) threat += s;
            }
            float unit = RecruitUnitStrength();
            if (unit <= 0f) return 0;
            if (_env.PendingRecruits != null) own += Math.Max(0, _env.PendingRecruits(civ)) * unit;   // eingereihte Aufträge zählen mit
            float wanted = Math.Max(threat * _env.Superiority, _env.MinAttackUnits * unit);
            float willingness = _env.RecruitWillingness == null ? 1f : Math.Clamp(_env.RecruitWillingness(civ), 0f, 1f);
            float gap = (wanted - own) * willingness;
            return gap <= 0f ? 0 : (int)MathF.Ceiling(gap / unit - 1e-4f);
        }

        private float RecruitUnitStrength() => _catalog.GetRoleStrength(MilitaryRole.Rifleman);

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
                if (dx * dx + dz * dz <= SlotTolerance * SlotTolerance) { _failed.Remove(id); continue; }
                if (!u.HasMoveTarget && InBackoff(id, _clock?.Tick ?? 0)) continue;
                if (!u.HasMoveTarget || !SameSpot(u.MoveTargetX, u.MoveTargetZ, sx, sz))
                {
                    _units.SetMoveTarget(id, sx, sz);
                    _issued[id] = (sx, sz);
                }
            }
        }
        // ---------------- ISaveable (K7: Lauf nach Laden identisch zum ununterbrochenen) ----------------
        // _freeByCiv, _civs, _alive, _scratchArmies werden jeden Lauf neu aufgebaut und brauchen keinen Zustand.

        public const string Key = "armyai";
        public string SaveKey => Key;
        public int LoadOrder => 31;   // nach armies (30)
        public int SectionVersion => 1;
        public Type StateType => typeof(ArmyAISaveState);

        public object CaptureState()
        {
            var recruit = new List<ArmyAIRecruitRecord>();
            var civKeys = new List<int>(_lastRecruit.Keys);
            civKeys.Sort();
            foreach (int c in civKeys) recruit.Add(new ArmyAIRecruitRecord { CivId = c, LastTick = _lastRecruit[c] });
            var issued = new List<ArmyAIIssuedRecord>();
            foreach (var kv in _issued)
                if (_units.Exists(kv.Key))
                    issued.Add(new ArmyAIIssuedRecord { Index = kv.Key.Index, Generation = kv.Key.Generation, X = kv.Value.x, Z = kv.Value.z });
            issued.Sort((a, b) => a.Index != b.Index ? a.Index.CompareTo(b.Index) : a.Generation.CompareTo(b.Generation));
            var failed = new List<ArmyAIFailedRecord>();
            foreach (var kv in _failed)
                if (_units.Exists(kv.Key))
                    failed.Add(new ArmyAIFailedRecord { Index = kv.Key.Index, Generation = kv.Key.Generation, UntilTick = kv.Value.until, Count = kv.Value.count });
            failed.Sort((a, b) => a.Index != b.Index ? a.Index.CompareTo(b.Index) : a.Generation.CompareTo(b.Generation));
            var blocked = new List<ArmyAIBlockedRecord>();
            foreach (var kv in _blocked) blocked.Add(new ArmyAIBlockedRecord { ArmyId = kv.Key, X = kv.Value.x, Z = kv.Value.z, UntilTick = kv.Value.until, Count = kv.Value.count });
            return new ArmyAISaveState { Recruit = recruit.ToArray(), Issued = issued.ToArray(), Failed = failed.ToArray(), Blocked = blocked.ToArray() };
        }

        public bool TryRestoreState(object dto, out string error)
        {
            if (!(dto is ArmyAISaveState s) || s.Recruit == null || s.Issued == null) { error = "armyai: Zustand fehlt oder hat falschen Typ."; return false; }
            _lastRecruit.Clear();
            _issued.Clear();
            _failed.Clear();
            _blocked.Clear();
            foreach (var r in s.Recruit) if (r != null) _lastRecruit[r.CivId] = r.LastTick;
            foreach (var r in s.Issued)
            {
                if (r == null) continue;
                var id = new EntityId(r.Index, r.Generation);
                if (_units.Exists(id)) _issued[id] = (r.X, r.Z);   // tote Einheit: Eintrag ohne Wirkung, verwerfen
            }
            // Failed/Blocked sind additive Felder: ältere Stände ohne sie laden mit leerem Zustand.
            foreach (var r in s.Failed ?? Array.Empty<ArmyAIFailedRecord>())
            {
                if (r == null) continue;
                var id = new EntityId(r.Index, r.Generation);
                if (_units.Exists(id)) _failed[id] = (r.UntilTick, Math.Max(1, r.Count));
            }
            foreach (var r in s.Blocked ?? Array.Empty<ArmyAIBlockedRecord>())
                if (r != null) _blocked[r.ArmyId] = (r.X, r.Z, r.UntilTick, Math.Max(1, r.Count));
            error = null;
            return true;
        }
    }

    [Serializable]
    public sealed class ArmyAISaveState
    {
        public ArmyAIRecruitRecord[] Recruit;
        public ArmyAIIssuedRecord[] Issued;
        public ArmyAIFailedRecord[] Failed;     // additiv; fehlt in älteren Ständen (null)
        public ArmyAIBlockedRecord[] Blocked;   // additiv; fehlt in älteren Ständen (null)
    }

    [Serializable]
    public sealed class ArmyAIFailedRecord { public int Index, Generation; public long UntilTick; public int Count; }

    [Serializable]
    public sealed class ArmyAIBlockedRecord { public int ArmyId; public float X, Z; public long UntilTick; public int Count; }

    [Serializable]
    public sealed class ArmyAIRecruitRecord { public int CivId; public long LastTick; }

    [Serializable]
    public sealed class ArmyAIIssuedRecord { public int Index, Generation; public float X, Z; }
}
