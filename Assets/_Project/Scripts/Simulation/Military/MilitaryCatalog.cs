using System;
using System.Collections.Generic;
using WorldBeyond.Core;
using WorldBeyond.Simulation.Combat;
using WorldBeyond.Simulation.Units;

namespace WorldBeyond.Simulation.Military
{
    // Feine Militärrolle, steht als byte in UnitTypeData.MilitaryRole. Explizite Werte, nur anhängen.
    public enum MilitaryRole : byte
    {
        None = 0,
        Rifleman = 1,
        AntiTank = 2,
        Recon = 3,
        Transport = 4,
        LightArmed = 5,
        InfantryFightingVehicle = 6,
        MainBattleTank = 7,
        Artillery = 8,
        RocketArtillery = 9,
        Support = 10,
    }

    /// <summary>Kampfwerte eines Militär-Einheitentyps. Ergänzt UnitTypeData, ersetzt sie nicht.</summary>
    public sealed class MilitaryUnitData
    {
        public UnitTypeId Type;
        public ArmorProfile Armor;
        public WeaponData Weapon;          // M1: eine Waffe pro Typ; Waffenslots kommen nach M1
        public float SightRange = 250f;    // m
        public float EyeHeight = 1.6f;     // m über Boden, für Sichtlinie
        public float HitRadius = 0.5f;     // m, Trefferkugel
        public float CombatStrength = 1f;  // für War-Bewertung (IMilitaryQuery)
    }

    public interface IMilitaryQuery
    {
        bool TryGet(UnitTypeId type, out MilitaryUnitData data);
        float GetCombatStrength(UnitTypeId type);   // 0 für Nicht-Militär
        int GetAliveMilitaryCount(int civId);       // lebende Militäreinheiten der Civ
        int GetQueuedMilitaryCount(int civId);      // eingereihte, noch nicht fertige Aufträge (ArmyEnvironment.PendingRecruits; 0 ohne Anschluss)
        int GetMilitaryCount(int civId);            // lebend + eingereiht (für die Aufrüstungsgrenze von War)
    }

    /// <summary>
    /// Militär-Typen in der gemeinsamen UnitTypeRegistry plus Kampfwerte. Registrierung nur beim Start.
    /// Waffen haben stabile Keys ("weapon.*") für den Spielstand.
    /// </summary>
    public sealed class MilitaryCatalog : IMilitaryQuery
    {
        private readonly Dictionary<int, MilitaryUnitData> _byType = new Dictionary<int, MilitaryUnitData>();
        private readonly Dictionary<string, WeaponData> _weapons = new Dictionary<string, WeaponData>(StringComparer.Ordinal);

        public UnitTypeId Register(UnitTypeRegistry registry, UnitTypeData unit, MilitaryUnitData military)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            if (military == null) throw new ArgumentNullException(nameof(military));
            if (military.Weapon == null || string.IsNullOrEmpty(military.Weapon.Key))
                throw new ArgumentException($"Militärtyp '{unit.Key}': Waffe mit Key fehlt.");
            if (_weapons.TryGetValue(military.Weapon.Key, out var existing) && !ReferenceEquals(existing, military.Weapon))
                throw new ArgumentException($"Waffen-Key '{military.Weapon.Key}' ist schon mit anderen Werten registriert.");

            var id = registry.Register(unit);
            military.Type = id;
            _byType[id.Value] = military;
            _weapons[military.Weapon.Key] = military.Weapon;
            return id;
        }

        public bool TryGet(UnitTypeId type, out MilitaryUnitData data) => _byType.TryGetValue(type.Value, out data);

        public bool TryGetWeapon(string key, out WeaponData weapon) => _weapons.TryGetValue(key ?? "", out weapon);

        public float GetCombatStrength(UnitTypeId type) => TryGet(type, out var d) ? d.CombatStrength : 0f;

        // ---------------- Zählung je Civ (einmal je Tick gezählt, dann gepuffert) ----------------
        private IUnitQuery _census;
        private SimClock _censusClock;
        private Func<Func<int, int>> _pending;
        private readonly Dictionary<int, int> _aliveByCiv = new Dictionary<int, int>();
        private readonly List<EntityId> _censusIds = new List<EntityId>();
        private long _censusTick = long.MinValue;

        internal void BindCensus(IUnitQuery units, SimClock clock, Func<Func<int, int>> pending)
        {
            _census = units; _censusClock = clock; _pending = pending;
        }

        public int GetAliveMilitaryCount(int civId)
        {
            if (_census == null) return 0;
            long tick = _censusClock?.Tick ?? 0;
            if (tick != _censusTick || _censusClock == null)
            {
                _censusTick = tick;
                _aliveByCiv.Clear();
                _census.CopyAliveIds(_censusIds);
                foreach (var id in _censusIds)
                    if (_census.TryGet(id, out var u) && _byType.ContainsKey(u.Type.Value))
                        _aliveByCiv[u.CivId] = _aliveByCiv.TryGetValue(u.CivId, out int n) ? n + 1 : 1;
            }
            return _aliveByCiv.TryGetValue(civId, out int c) ? c : 0;
        }

        public int GetQueuedMilitaryCount(int civId)
        {
            var f = _pending?.Invoke();
            return f == null ? 0 : Math.Max(0, f(civId));
        }

        public int GetMilitaryCount(int civId) => GetAliveMilitaryCount(civId) + GetQueuedMilitaryCount(civId);
    }

    /// <summary>M1-Inhalt (INTEGRATION.md §4): ein Infanterietyp mit Hitscan-Gewehr. Weitere Typen nach M1.</summary>
    public static class MilitaryContent
    {
        public const string RiflemanKey = "unit.rifleman";
        public const string RifleKey = "weapon.rifle";

        public static WeaponData Rifle() => new WeaponData
        {
            Key = RifleKey,
            Range = 150f,
            FireInterval = 1.0f,
            MagazineSize = 30,
            ReloadTime = 3f,
            MaxAmmoReserve = -1,          // M1: keine Versorgung, unbegrenzte Reserve
            Damage = 12f,
            DamageType = DamageType.SmallArms,
            Projectile = ProjectileKind.Hitscan,
        };

        public static void RegisterM1(MilitaryCatalog catalog, UnitTypeRegistry registry)
        {
            catalog.Register(registry,
                new UnitTypeData
                {
                    Key = RiflemanKey,
                    DisplayName = "Schütze",
                    Role = UnitRole.Infantry,
                    MilitaryRole = (byte)MilitaryRole.Rifleman,
                    Movement = Core.MovementClass.Foot,
                    MaxHp = 100f,
                    MaxSpeed = 1.8f,
                    Radius = 0.4f,
                },
                new MilitaryUnitData
                {
                    Armor = new ArmorProfile(0f, 0f, 0f, 0f, 0f),
                    Weapon = Rifle(),
                    SightRange = 200f,
                    CombatStrength = 1f,
                });
        }
    }
}
