using System;
using System.Collections.Generic;
using WorldBeyond.Core;
using WorldBeyond.Simulation.Units;

namespace WorldBeyond.Simulation.Military
{
    internal sealed class Army
    {
        public int Id;
        public int CivId;
        public readonly List<EntityId> Units = new List<EntityId>();
        public ArmyOrder Order;
        public ArmyPosture Posture;
        public float HomeX, HomeZ;
        public bool Autonomous;            // von der Armee-KI gegründet
        public long ManualUntilTick;       // Spielerbefehl hat bis hierhin Vorrang vor der KI
        public long LastDecisionTick = -1_000_000;   // "nie": kein long.MinValue, sonst läuft tick − Wert über
    }

    /// <summary>
    /// Einzige Armee-Verwaltung (INTEGRATION.md §3). Hält nur IDs; Positionen und HP liest sie aus dem UnitStore.
    /// Ausführung der Aufträge und KI-Entscheidungen: ArmyAISystem.
    /// </summary>
    public sealed class ArmyRegistry : IArmyRegistry, IArmyQuery, ISaveable, ISaveReferenceCheck
    {
        public const string Key = "armies";
        public const long ManualOverrideTicks = 600;   // 60 s Vorrang für Spielerbefehle

        private readonly IUnitStore _units;
        private readonly IMilitaryQuery _military;
        private readonly ISimEventBus _events;
        private readonly SimClock _clock;
        private readonly ArmyEnvironment _env;
        private readonly SequentialIdAllocator _ids = new SequentialIdAllocator();
        private readonly SortedDictionary<int, Army> _armies = new SortedDictionary<int, Army>();  // feste Reihenfolge → deterministisch
        private readonly Dictionary<EntityId, int> _armyOf = new Dictionary<EntityId, int>();

        public ArmyRegistry(IUnitStore units, IMilitaryQuery military, ISimEventBus events, SimClock clock, ArmyEnvironment env)
        {
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _military = military ?? throw new ArgumentNullException(nameof(military));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _env = env ?? throw new ArgumentNullException(nameof(env));
        }

        internal IEnumerable<Army> All => _armies.Values;

        // Von ArmyAISystem gesetzte Bewegungsziele (Einheit → Ziel); gespeichert, damit die KI nach dem Laden gleich entscheidet.
        internal readonly Dictionary<EntityId, (float x, float z)> IssuedMoveTargets = new Dictionary<EntityId, (float x, float z)>();

        // ---------------- IArmyRegistry (Spieler/UI/Commands; setzt manuellen Vorrang) ----------------

        public int CreateArmy(int civId, IReadOnlyList<EntityId> units) => Create(civId, units, autonomous: false);

        public void OrderMove(int armyId, float x, float z) =>
            SetOrder(armyId, new ArmyOrder { Kind = ArmyOrderKind.Move, X = x, Z = z }, byAI: false);

        public void OrderAttack(int armyId, EntityId targetUnit)
        {
            if (!_units.TryGet(targetUnit, out var t)) return;
            SetOrder(armyId, new ArmyOrder { Kind = ArmyOrderKind.AttackUnit, TargetUnit = targetUnit, X = t.X, Z = t.Z }, byAI: false);
        }

        public void OrderAttackSettlement(int armyId, int settlementId)
        {
            if (!TrySettlement(settlementId, out float x, out float z)) return;
            SetOrder(armyId, new ArmyOrder { Kind = ArmyOrderKind.AttackSettlement, TargetRef = settlementId, X = x, Z = z }, byAI: false);
        }

        public void OrderDefend(int armyId, int settlementId)
        {
            if (!TrySettlement(settlementId, out float x, out float z)) return;
            SetOrder(armyId, new ArmyOrder { Kind = ArmyOrderKind.Defend, TargetRef = settlementId, X = x, Z = z }, byAI: false);
        }

        public void OrderRetreat(int armyId)
        {
            if (!_armies.TryGetValue(armyId, out var a)) return;
            SetOrder(armyId, new ArmyOrder { Kind = ArmyOrderKind.Retreat, X = a.HomeX, Z = a.HomeZ }, byAI: false);
        }

        public void Disband(int armyId)
        {
            if (!_armies.TryGetValue(armyId, out var a)) return;
            foreach (var u in a.Units) _armyOf.Remove(u);
            _armies.Remove(armyId);
            _events.Publish(new ArmyDisbandedEvent { ArmyId = armyId, CivId = a.CivId });
        }

        // ---------------- IArmyQuery ----------------

        public bool Exists(int armyId) => _armies.ContainsKey(armyId);

        public bool TryGetArmyOf(EntityId unit, out int armyId) => _armyOf.TryGetValue(unit, out armyId) && _units.Exists(unit);

        public IReadOnlyList<ArmyView> GetArmies(int civId)
        {
            var list = new List<ArmyView>();
            foreach (var a in _armies.Values)
                if (a.CivId == civId) list.Add(View(a));
            return list;
        }

        public bool TryGetView(int armyId, out ArmyView view)
        {
            view = default;
            if (!_armies.TryGetValue(armyId, out var a)) return false;
            view = View(a);
            return true;
        }

        // ---------------- intern (ArmyAISystem) ----------------

        internal int Create(int civId, IReadOnlyList<EntityId> units, bool autonomous)
        {
            if (units == null) return 0;
            var army = new Army { CivId = civId, Autonomous = autonomous };
            foreach (var u in units)
            {
                if (!_units.TryGet(u, out var v) || v.CivId != civId || !_military.TryGet(v.Type, out _)) continue;
                if (_armyOf.TryGetValue(u, out int old) && _armies.TryGetValue(old, out var oldArmy)) oldArmy.Units.Remove(u);
                army.Units.Add(u);
            }
            if (army.Units.Count == 0) return 0;
            army.Id = _ids.Next();
            Center(army, out army.HomeX, out army.HomeZ);
            if (_env.HomeOf != null)
            {
                var home = _env.HomeOf(civId);
                if (home.ok) { army.HomeX = home.x; army.HomeZ = home.z; }
            }
            if (!autonomous) army.ManualUntilTick = _clock.Tick + ManualOverrideTicks;
            _armies.Add(army.Id, army);
            foreach (var u in army.Units) _armyOf[u] = army.Id;
            _events.Publish(new ArmyCreatedEvent { ArmyId = army.Id, CivId = civId, UnitCount = army.Units.Count, Autonomous = autonomous });
            return army.Id;
        }

        internal bool AddUnit(int armyId, EntityId unit)
        {
            if (!_armies.TryGetValue(armyId, out var a) || _armyOf.ContainsKey(unit)) return false;
            a.Units.Add(unit);
            _armyOf[unit] = armyId;
            return true;
        }

        internal void SetOrder(int armyId, ArmyOrder order, bool byAI)
        {
            if (!_armies.TryGetValue(armyId, out var a)) return;
            if (!byAI) a.ManualUntilTick = _clock.Tick + ManualOverrideTicks;
            bool changed = a.Order.Kind != order.Kind || a.Order.TargetUnit != order.TargetUnit || a.Order.TargetRef != order.TargetRef
                           || MathF.Abs(a.Order.X - order.X) > 1f || MathF.Abs(a.Order.Z - order.Z) > 1f;
            a.Order = order;
            a.Posture = order.Kind switch
            {
                ArmyOrderKind.Retreat => ArmyPosture.Retreating,
                ArmyOrderKind.Defend => ArmyPosture.Defending,
                ArmyOrderKind.None => ArmyPosture.Idle,
                _ => ArmyPosture.Marching,
            };
            if (changed) _events.Publish(new ArmyOrderChangedEvent { ArmyId = armyId, CivId = a.CivId, Order = order, ByAI = byAI });
        }

        /// <summary>Tote Einheiten entfernen, leere Armeen auflösen.</summary>
        internal void Prune()
        {
            List<int> empty = null;
            foreach (var a in _armies.Values)
            {
                for (int i = a.Units.Count - 1; i >= 0; i--)
                {
                    if (_units.Exists(a.Units[i])) continue;
                    _armyOf.Remove(a.Units[i]);
                    a.Units.RemoveAt(i);
                }
                if (a.Units.Count == 0) (empty ??= new List<int>()).Add(a.Id);
            }
            if (empty != null) foreach (var id in empty) Disband(id);
        }

        internal void Center(Army a, out float x, out float z)
        {
            x = 0f; z = 0f;
            int n = 0;
            foreach (var u in a.Units)
            {
                if (!_units.TryGet(u, out var v)) continue;
                x += v.X; z += v.Z; n++;
            }
            if (n > 0) { x /= n; z /= n; }
        }

        internal float Strength(Army a)
        {
            float s = 0f;
            foreach (var u in a.Units)
                if (_units.TryGet(u, out var v) && v.MaxHp > 0f) s += _military.GetCombatStrength(v.Type) * (v.Hp / v.MaxHp);
            return s;
        }

        private ArmyView View(Army a)
        {
            Center(a, out float x, out float z);
            return new ArmyView(a.Id, a.CivId, a.Units.Count, x, z, a.Order, a.Posture, Strength(a), _clock.Tick < a.ManualUntilTick);
        }

        private bool TrySettlement(int id, out float x, out float z)
        {
            x = z = 0f;
            if (_env.SettlementPosition == null) return false;
            var p = _env.SettlementPosition(id);
            x = p.x; z = p.z;
            return p.ok;
        }

        // ---------------- ISaveable / ISaveReferenceCheck ----------------

        public string SaveKey => Key;
        public int LoadOrder => 30;   // nach units (10) und combat (20)
        public int SectionVersion => 1;
        public Type StateType => typeof(ArmySaveState);

        public object CaptureState()
        {
            var list = new List<ArmyRecord>();
            foreach (var a in _armies.Values)
            {
                var idx = new int[a.Units.Count];
                var gen = new int[a.Units.Count];
                for (int i = 0; i < a.Units.Count; i++) { idx[i] = a.Units[i].Index; gen[i] = a.Units[i].Generation; }
                list.Add(new ArmyRecord
                {
                    Id = a.Id, CivId = a.CivId, UnitIndices = idx, UnitGenerations = gen, Order = a.Order, Posture = a.Posture,
                    HomeX = a.HomeX, HomeZ = a.HomeZ, Autonomous = a.Autonomous, ManualUntilTick = a.ManualUntilTick,
                    LastDecisionTick = a.LastDecisionTick,
                });
            }
            var issued = new List<IssuedMoveRecord>(IssuedMoveTargets.Count);
            foreach (var kv in IssuedMoveTargets)
            {
                if (!_units.Exists(kv.Key)) continue;
                issued.Add(new IssuedMoveRecord { Index = kv.Key.Index, Generation = kv.Key.Generation, X = kv.Value.x, Z = kv.Value.z });
            }
            issued.Sort((a, b) => a.Index.CompareTo(b.Index));   // Index ist unter lebenden Einheiten eindeutig
            return new ArmySaveState { LastIssuedId = _ids.LastIssued, Armies = list.ToArray(), Issued = issued.ToArray() };
        }

        public bool TryRestoreState(object dto, out string error)
        {
            if (!(dto is ArmySaveState s) || s.Armies == null) { error = "armies: Zustand fehlt oder hat falschen Typ."; return false; }
            var seen = new HashSet<int>();
            foreach (var r in s.Armies)
            {
                if (r == null || r.Id <= 0 || r.Id > s.LastIssuedId) { error = $"armies: Armee-ID {r?.Id} ungültig (LastIssued {s.LastIssuedId})."; return false; }
                if (!seen.Add(r.Id)) { error = $"armies: Armee-ID {r.Id} doppelt."; return false; }
                if (r.UnitIndices == null || r.UnitGenerations == null || r.UnitIndices.Length != r.UnitGenerations.Length)
                { error = $"armies: Einheitenliste von Armee {r.Id} beschädigt."; return false; }
                if (!Enum.IsDefined(typeof(ArmyOrderKind), r.Order.Kind)) { error = $"armies: unbekannter Auftrag {(byte)r.Order.Kind} in Armee {r.Id}."; return false; }
            }
            if (!_ids.TryRestore(s.LastIssuedId, out error)) { error = "armies: " + error; return false; }

            _armies.Clear();
            _armyOf.Clear();
            foreach (var r in s.Armies)
            {
                var a = new Army
                {
                    Id = r.Id, CivId = r.CivId, Order = r.Order,
                    Posture = Enum.IsDefined(typeof(ArmyPosture), r.Posture) ? r.Posture : ArmyPosture.Idle,
                    HomeX = r.HomeX, HomeZ = r.HomeZ, Autonomous = r.Autonomous, ManualUntilTick = r.ManualUntilTick,
                    LastDecisionTick = r.LastDecisionTick,
                };
                for (int i = 0; i < r.UnitIndices.Length; i++)
                {
                    var id = new EntityId(r.UnitIndices[i], r.UnitGenerations[i]);
                    if (_armyOf.ContainsKey(id)) continue;   // Einheit in zwei Armeen: erste gewinnt, Prüfung meldet nichts Härteres
                    a.Units.Add(id);
                    _armyOf[id] = a.Id;
                }
                _armies.Add(a.Id, a);
            }
            // Ältere Spielstände ohne "Issued": alle vorhandenen Ziele gelten als fremd (früheres Verhalten).
            IssuedMoveTargets.Clear();
            if (s.Issued != null)
                foreach (var r in s.Issued)
                {
                    if (r == null) continue;
                    var id = new EntityId(r.Index, r.Generation);
                    if (_units.Exists(id)) IssuedMoveTargets[id] = (r.X, r.Z);
                }
            error = null;
            return true;
        }

        public void CheckReferences(SimContext ctx, LoadReport report)
        {
            List<int> empty = null;
            foreach (var a in _armies.Values)
            {
                int before = a.Units.Count;
                for (int i = a.Units.Count - 1; i >= 0; i--)
                {
                    if (_units.Exists(a.Units[i])) continue;
                    _armyOf.Remove(a.Units[i]);
                    a.Units.RemoveAt(i);
                }
                if (a.Units.Count != before) report.Warn($"armies: Armee {a.Id} verlor {before - a.Units.Count} nicht existierende Einheiten.");
                if (a.Order.Kind == ArmyOrderKind.AttackUnit && !_units.Exists(a.Order.TargetUnit))
                {
                    report.Warn($"armies: Angriffsziel von Armee {a.Id} existiert nicht mehr, Auftrag beendet.");
                    a.Order = default;
                    a.Posture = ArmyPosture.Idle;
                }
                if (a.Units.Count == 0) (empty ??= new List<int>()).Add(a.Id);
            }
            if (empty != null)
                foreach (var id in empty)
                {
                    report.Warn($"armies: Armee {id} ist leer und wurde aufgelöst.");
                    Disband(id);
                }
        }
    }

    [Serializable]
    public sealed class ArmySaveState
    {
        public int LastIssuedId;
        public ArmyRecord[] Armies;
        public IssuedMoveRecord[] Issued;   // seit Phase 7; fehlt in älteren Spielständen (null)
    }

    [Serializable]
    public sealed class IssuedMoveRecord
    {
        public int Index, Generation;
        public float X, Z;
    }

    [Serializable]
    public sealed class ArmyRecord
    {
        public int Id, CivId;
        public int[] UnitIndices, UnitGenerations;
        public ArmyOrder Order;
        public ArmyPosture Posture;
        public float HomeX, HomeZ;
        public bool Autonomous;
        public long ManualUntilTick;
        public long LastDecisionTick;
    }
}
