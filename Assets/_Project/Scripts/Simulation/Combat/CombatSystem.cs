using System;
using System.Collections.Generic;
using System.Numerics;
using WorldBeyond.Core;
using WorldBeyond.Simulation.Military;
using WorldBeyond.Simulation.Units;

namespace WorldBeyond.Simulation.Combat
{
    // Haltung einer Einheit. Explizite Werte, gespeichert.
    public enum CombatStance : byte
    {
        Aggressive = 0,   // greift Feinde in Sichtweite an und verfolgt sie
        HoldPosition = 1, // feuert nur auf Ziele in Waffenreichweite, bewegt sich nicht
        HoldFire = 2,     // feuert nicht (Rückzug, Transport)
    }

    /// <summary>
    /// Taktischer Kampf pro Einheit (Order 400, jeden Tick): Ziel wählen, Sichtlinie prüfen, feuern, verfolgen.
    /// M1: Hitscan-Waffen. Schaden nur über ICombatService. Speichert Waffen- und Zielzustand als Sektion "combat".
    /// </summary>
    public sealed class CombatSystem : ISimulationSystem, ISaveable
    {
        public const string Key = "combat";
        private const int AcquireStagger = 5;    // Zielsuche je Einheit alle 5 Ticks (0,5 s)
        private const int ChaseRefresh = 10;     // Verfolgungsziel alle 1 s erneuern

        private readonly IUnitStore _units;
        private readonly MilitaryCatalog _catalog;
        private readonly CombatService _combat;
        private readonly CombatEnvironment _env;
        private readonly SimRandom _random;
        private readonly ISimEventBus _events;

        private EntityId[] _owner = new EntityId[64];   // für welche Einheit der Slot gilt (Generation!)
        private WeaponState[] _weapon = new WeaponState[64];
        private EntityId[] _target = new EntityId[64];
        private CombatStance[] _stance = new CombatStance[64];
        private bool[] _chasing = new bool[64];

        private readonly List<EntityId> _alive = new List<EntityId>();
        private readonly List<EntityId> _nearby = new List<EntityId>();
        private readonly List<string> _restoreWarnings = new List<string>();

        public CombatSystem(IUnitStore units, MilitaryCatalog catalog, CombatService combat, CombatEnvironment env,
            SimRandom random, ISimEventBus events)
        {
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _combat = combat ?? throw new ArgumentNullException(nameof(combat));
            _env = env ?? throw new ArgumentNullException(nameof(env));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _events = events ?? throw new ArgumentNullException(nameof(events));
        }

        public string Name => "Combat";
        public int Order => SystemOrder.Combat;
        public int TickInterval => 1;

        public IReadOnlyList<string> LastRestoreWarnings => _restoreWarnings;

        // ---------------- Befehls-API (für Commands, Armee-KI) ----------------

        public bool SetStance(EntityId unit, CombatStance stance)
        {
            if (!EnsureSlot(unit, out int i)) return false;
            _stance[i] = stance;
            if (stance == CombatStance.HoldFire) _target[i] = EntityId.None;
            return true;
        }

        public CombatStance GetStance(EntityId unit) => EnsureSlot(unit, out int i) ? _stance[i] : CombatStance.Aggressive;

        public EntityId GetTarget(EntityId unit) => EnsureSlot(unit, out int i) ? _target[i] : EntityId.None;

        public bool TryGetWeaponState(EntityId unit, out WeaponState state)
        {
            state = default;
            if (!EnsureSlot(unit, out int i)) return false;
            state = _weapon[i];
            return true;
        }

        // ---------------- Tick ----------------

        public void Tick(SimContext ctx)
        {
            float dt = ctx.Clock.DeltaSeconds;
            long tick = ctx.Clock.Tick;
            _units.CopyAliveIds(_alive);
            foreach (var id in _alive)
            {
                if (!_units.TryGet(id, out var me)) continue;            // in diesem Tick gestorben
                if (!_catalog.TryGet(me.Type, out var md)) continue;     // kein Militär
                EnsureSlot(id, out int i);
                _weapon[i].Tick(md.Weapon, dt);
                if (_stance[i] == CombatStance.HoldFire) continue;

                bool ordered = me.Task.Kind == UnitTaskKind.Attack;
                if (ordered)
                {
                    if (!_units.TryGet(me.Task.TargetEntity, out _))
                    {
                        // befohlenes Ziel tot oder ungültig → Auftrag erledigt (ARCHITECTURE §11 Regel 6)
                        _units.SetTask(id, UnitTask.None);
                        StopChase(id, i);
                        ordered = false;
                    }
                    else _target[i] = me.Task.TargetEntity;
                }

                if (!ordered && (!IsValidTarget(in me, _target[i], md, out _) || (id.Index + tick) % AcquireStagger == 0))
                    _target[i] = Acquire(in me, md);

                if (!_units.TryGet(_target[i], out var tgt))
                {
                    _target[i] = EntityId.None;
                    StopChase(id, i);
                    if (me.State == UnitState.Fighting) _units.SetState(id, UnitState.Idle);
                    continue;
                }

                float dist = Distance(me.X, me.Z, tgt.X, tgt.Z);
                bool visible = HasLineOfSight(in me, md, in tgt);
                bool inRange = dist <= md.Weapon.Range && dist >= md.Weapon.MinRange;

                if (inRange && visible)
                {
                    StopChase(id, i);
                    if (me.State != UnitState.Fighting) _units.SetState(id, UnitState.Fighting);
                    if (_weapon[i].TryFire(md.Weapon, dist)) FireHitscan(id, md, in me, in tgt, dist);
                }
                else if (_stance[i] == CombatStance.Aggressive && (ordered || CanChaseOnOwn(in me, i)))
                {
                    if (!_chasing[i] || (id.Index + tick) % ChaseRefresh == 0)
                    {
                        _units.SetMoveTarget(id, tgt.X, tgt.Z);
                        _chasing[i] = true;
                    }
                }
            }
        }

        private bool CanChaseOnOwn(in UnitView me, int i)
        {
            // Eigenständige Verfolgung nur ohne fremden Auftrag; Spieler-Bewegungsbefehle haben Vorrang.
            if (_chasing[i]) return true;
            return me.Task.Kind == UnitTaskKind.None && !me.HasMoveTarget;
        }

        private void StopChase(EntityId id, int i)
        {
            if (!_chasing[i]) return;
            _chasing[i] = false;
            _units.ClearMoveTarget(id);
        }

        private void FireHitscan(EntityId shooter, MilitaryUnitData md, in UnitView me, in UnitView tgt, float dist)
        {
            float chance = Math.Clamp(0.9f - 0.5f * dist / MathF.Max(1f, md.Weapon.Range), 0.2f, 0.95f);
            bool hit = _random.Value01() < chance;
            _events.Publish(new WeaponFiredEvent
            {
                Shooter = shooter, Target = tgt.Id,
                FromX = me.X, FromZ = me.Z, ToX = tgt.X, ToZ = tgt.Z,
                Projectile = md.Weapon.Projectile, Hit = hit,
            });
            if (hit) _combat.ApplyDamage(tgt.Id, md.Weapon.Damage, md.Weapon.DamageType, shooter);
        }

        private EntityId Acquire(in UnitView me, MilitaryUnitData md)
        {
            _combat.QueryRadius(me.X, me.Z, md.SightRange, _nearby);
            Span<TargetCandidate> cand = _nearby.Count <= 128 ? stackalloc TargetCandidate[_nearby.Count] : new TargetCandidate[_nearby.Count];
            int n = 0;
            for (int k = 0; k < _nearby.Count; k++)
            {
                var other = _nearby[k];
                if (!IsValidTarget(in me, other, md, out var t)) { _nearby[k] = EntityId.None; continue; }
                float otherStrength = _catalog.GetCombatStrength(t.Type);
                var armor = _catalog.TryGet(t.Type, out var od) ? od.Armor : default;
                cand[n++] = new TargetCandidate
                {
                    Distance = Distance(me.X, me.Z, t.X, t.Z),
                    Effectiveness = 1f - armor.Reduction(md.Weapon.DamageType),
                    Threat = otherStrength / (otherStrength + md.CombatStrength),
                    HpFraction = t.MaxHp > 0f ? t.Hp / t.MaxHp : 1f,
                    Visible = HasLineOfSight(in me, md, in t),
                };
                _nearby[n - 1] = other;   // Index n−1 ↔ Kandidat n−1 (kompaktiert; k ≥ n−1)
            }
            // Bewertung mit Sichtweite als "Reichweite": verfolgen ist erlaubt, Waffenreichweite prüft der Tick.
            int best = TargetScoring.PickBest(cand.Slice(0, n), md.SightRange, 0f);
            return best >= 0 ? _nearby[best] : EntityId.None;
        }

        private bool IsValidTarget(in UnitView me, EntityId other, MilitaryUnitData md, out UnitView t)
        {
            if (!other.IsValid || other == me.Id || !_units.TryGet(other, out t)) { t = default; return false; }
            if (t.CivId == me.CivId || !_env.AreHostile(me.CivId, t.CivId)) return false;
            return Distance(me.X, me.Z, t.X, t.Z) <= md.SightRange;
        }

        private bool HasLineOfSight(in UnitView me, MilitaryUnitData md, in UnitView t)
        {
            if (md.Weapon.Indirect) return true;
            var from = new Vector3(me.X, _env.HeightAt(me.X, me.Z) + md.EyeHeight, me.Z);
            float targetEye = _catalog.TryGet(t.Type, out var td) ? td.EyeHeight : 1.6f;
            var to = new Vector3(t.X, _env.HeightAt(t.X, t.Z) + targetEye, t.Z);
            return LineOfSight.Clear(from, to, _env.HeightAt);
        }

        private static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Slot für die Einheit; legt ihn bei neuer Generation frisch an (volle Waffe).</summary>
        private bool EnsureSlot(EntityId id, out int i)
        {
            i = id.Index;
            if (!_units.TryGet(id, out var u) || !_catalog.TryGet(u.Type, out var md)) return false;
            Grow(i + 1);
            if (_owner[i] != id)
            {
                _owner[i] = id;
                _weapon[i] = WeaponState.Full(md.Weapon);
                _target[i] = EntityId.None;
                _stance[i] = CombatStance.Aggressive;
                _chasing[i] = false;
            }
            return true;
        }

        private void Grow(int needed)
        {
            if (needed <= _owner.Length) return;
            int cap = Math.Max(needed, _owner.Length * 2);
            Array.Resize(ref _owner, cap);
            Array.Resize(ref _weapon, cap);
            Array.Resize(ref _target, cap);
            Array.Resize(ref _stance, cap);
            Array.Resize(ref _chasing, cap);
        }

        // ---------------- ISaveable ----------------

        public string SaveKey => Key;
        public int LoadOrder => 20;           // nach "units" (10)
        public int SectionVersion => 1;
        public Type StateType => typeof(CombatSaveState);

        public object CaptureState()
        {
            var list = new List<CombatRecord>();
            _units.CopyAliveIds(_alive);
            foreach (var id in _alive)
            {
                int i = id.Index;
                if (i >= _owner.Length || _owner[i] != id) continue;   // nie im Kampf gewesen → beim Laden frisch
                if (!_units.TryGet(id, out var u) || !_catalog.TryGet(u.Type, out var md)) continue;
                list.Add(new CombatRecord
                {
                    Index = id.Index, Generation = id.Generation,
                    WeaponKey = md.Weapon.Key,
                    InMagazine = _weapon[i].InMagazine, Reserve = _weapon[i].Reserve,
                    Cooldown = _weapon[i].Cooldown, ReloadRemaining = _weapon[i].ReloadRemaining,
                    TargetIndex = _target[i].Index, TargetGeneration = _target[i].Generation,
                    Stance = _stance[i], Chasing = _chasing[i],
                });
            }
            return new CombatSaveState { Records = list.ToArray() };
        }

        public bool TryRestoreState(object dto, out string error)
        {
            _restoreWarnings.Clear();
            if (!(dto is CombatSaveState s) || s.Records == null) { error = "combat: Zustand fehlt oder hat falschen Typ."; return false; }
            foreach (var r in s.Records)
            {
                if (r == null) { error = "combat: leerer Datensatz."; return false; }
                if (!_catalog.TryGetWeapon(r.WeaponKey, out _)) { error = $"combat: unbekannter Waffen-Key '{r.WeaponKey}'."; return false; }
            }

            Array.Clear(_owner, 0, _owner.Length);
            foreach (var r in s.Records)
            {
                var id = new EntityId(r.Index, r.Generation);
                if (!_units.TryGet(id, out var u) || !_catalog.TryGet(u.Type, out var md))
                {
                    _restoreWarnings.Add($"combat: Einheit {id} existiert nicht oder ist kein Militär, Datensatz verworfen.");
                    continue;
                }
                if (md.Weapon.Key != r.WeaponKey)
                {
                    _restoreWarnings.Add($"combat: Einheit {id} hat jetzt Waffe '{md.Weapon.Key}' statt '{r.WeaponKey}', Waffe voll aufgefüllt.");
                    continue;
                }
                int i = id.Index;
                Grow(i + 1);
                _owner[i] = id;
                _weapon[i] = new WeaponState
                {
                    InMagazine = Math.Clamp(r.InMagazine, 0, md.Weapon.MagazineSize),
                    Reserve = md.Weapon.MaxAmmoReserve < 0 ? -1 : Math.Clamp(r.Reserve, 0, md.Weapon.MaxAmmoReserve),
                    Cooldown = MathF.Max(0f, r.Cooldown),
                    ReloadRemaining = MathF.Max(0f, r.ReloadRemaining),
                };
                var target = new EntityId(r.TargetIndex, r.TargetGeneration);
                if (target.IsValid && !_units.Exists(target))
                {
                    _restoreWarnings.Add($"combat: Ziel {target} von {id} existiert nicht mehr, Ziel gelöscht.");
                    target = EntityId.None;
                }
                _target[i] = target;
                _stance[i] = Enum.IsDefined(typeof(CombatStance), r.Stance) ? r.Stance : CombatStance.Aggressive;
                _chasing[i] = r.Chasing;
            }
            error = null;
            return true;
        }
    }

    [Serializable]
    public sealed class CombatSaveState
    {
        public CombatRecord[] Records;
    }

    [Serializable]
    public sealed class CombatRecord
    {
        public int Index, Generation;
        public string WeaponKey;
        public int InMagazine, Reserve;
        public float Cooldown, ReloadRemaining;
        public int TargetIndex, TargetGeneration;
        public CombatStance Stance;
        public bool Chasing;
    }
}
