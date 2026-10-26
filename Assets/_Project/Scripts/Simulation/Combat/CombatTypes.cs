// Reines C# (System.Numerics.Vector3 läuft in Unity und in dotnet test). Keine UnityEngine-Abhängigkeit.
using System;

namespace WorldBeyond.Simulation.Combat
{
    // Explizite Werte: werden gespeichert (ARCHITECTURE §11). Reihenfolge = SYSTEM_INTERFACES §9.
    public enum DamageType : byte { SmallArms = 0, HeavyMG = 1, Explosive = 2, ArmorPiercing = 3, Fire = 4 }
    public enum ProjectileKind : byte { Hitscan = 0, Ballistic = 1, Rocket = 2, ArtilleryShell = 3 }

    public static class DamageTypeInfo
    {
        public const int Count = 5;
    }

    /// <summary>Panzerung als Schadensreduktion 0…0.95 pro Schadensart.</summary>
    public readonly struct ArmorProfile
    {
        public const float MaxReduction = 0.95f;
        private readonly float _smallArms, _heavyMG, _explosive, _armorPiercing, _fire;

        public ArmorProfile(float smallArms, float heavyMG, float explosive, float armorPiercing, float fire)
        {
            _smallArms = Clamp(smallArms);
            _heavyMG = Clamp(heavyMG);
            _explosive = Clamp(explosive);
            _armorPiercing = Clamp(armorPiercing);
            _fire = Clamp(fire);
        }

        public float Reduction(DamageType type) => type switch
        {
            DamageType.SmallArms => _smallArms,
            DamageType.HeavyMG => _heavyMG,
            DamageType.Explosive => _explosive,
            DamageType.ArmorPiercing => _armorPiercing,
            DamageType.Fire => _fire,
            _ => 0f
        };

        private static float Clamp(float v) => v < 0f ? 0f : (v > MaxReduction ? MaxReduction : v);
    }

    public static class DamageModel
    {
        /// <summary>Schaden nach Panzerung. Nie negativ.</summary>
        public static float Effective(float amount, DamageType type, in ArmorProfile armor)
        {
            if (amount <= 0f) return 0f;
            return amount * (1f - armor.Reduction(type));
        }

        /// <summary>Linearer Abfall: voll im Zentrum, 0 am Rand. Kerndurchmesser innerRadius bekommt vollen Schaden.</summary>
        public static float AreaFalloff(float distance, float radius, float innerRadius = 0f)
        {
            if (radius <= 0f || distance >= radius) return 0f;
            if (distance <= innerRadius) return 1f;
            return 1f - (distance - innerRadius) / (radius - innerRadius);
        }

        /// <summary>Neue HP nach Schaden; true wenn die Einheit dadurch zerstört wurde.</summary>
        public static bool Apply(ref float hp, float effectiveDamage)
        {
            if (hp <= 0f) return false;            // schon tot: kein zweites Todes-Event
            hp -= effectiveDamage;
            if (hp > 0f) return false;
            hp = 0f;
            return true;
        }
    }
}
