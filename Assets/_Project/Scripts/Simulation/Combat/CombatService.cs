using System;
using System.Collections.Generic;
using WorldBeyond.Core;
using WorldBeyond.Simulation.Military;
using WorldBeyond.Simulation.Units;

namespace WorldBeyond.Simulation.Combat
{
    public interface ICombatService
    {
        // Schaden immer über diese Funktion, damit Panzerung/Events konsistent sind (SYSTEM_INTERFACES §9).
        void ApplyDamage(EntityId target, float amount, DamageType type, EntityId source);
        void ApplyAreaDamage(float x, float z, float radius, float amount, DamageType type, EntityId source);
    }

    public struct UnitDamagedEvent : ISimEvent
    {
        public EntityId Target;
        public EntityId Source;
        public float Amount;       // nach Panzerung
        public DamageType Type;
        public float HpLeft;
    }

    /// <summary>Schuss einer Waffe (für VFX/Audio: Mündungsfeuer, Leuchtspur). Hit = Hitscan hat getroffen.</summary>
    public struct WeaponFiredEvent : ISimEvent
    {
        public EntityId Shooter;
        public EntityId Target;
        public float FromX, FromZ, ToX, ToZ;
        public ProjectileKind Projectile;
        public bool Hit;
    }

    public struct ExplosionEvent : ISimEvent { public float X, Z, Radius; public DamageType Type; }

    /// <summary>
    /// Anschluss an Dienste anderer Phasen, die es im Code noch nicht gibt. GameComposition setzt die Delegates,
    /// sobald World (ISpatialIndex, SampleHeight) und War (IDiplomacyQuery.AreAtWar) gemergt sind.
    /// Die Standardwerte sind nur Rückfälle: keine Feindschaft, flaches Gelände, lineare Suche über alle Einheiten.
    /// </summary>
    public sealed class CombatEnvironment
    {
        public Func<int, int, bool> AreHostile = (a, b) => false;
        public Func<float, float, float> HeightAt = (x, z) => 0f;
        /// <summary>Alle Einheiten im Kreis (x, z, Radius) in results schreiben. Null = lineare Suche.</summary>
        public Action<float, float, float, List<EntityId>> QueryRadius;
    }

    public sealed class CombatService : ICombatService
    {
        private readonly IUnitStore _units;
        private readonly IMilitaryQuery _military;
        private readonly ISimEventBus _events;
        private readonly CombatEnvironment _env;
        private readonly List<EntityId> _scratch = new List<EntityId>();

        public CombatService(IUnitStore units, IMilitaryQuery military, ISimEventBus events, CombatEnvironment env)
        {
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _military = military ?? throw new ArgumentNullException(nameof(military));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _env = env ?? throw new ArgumentNullException(nameof(env));
        }

        public void ApplyDamage(EntityId target, float amount, DamageType type, EntityId source)
        {
            if (!_units.TryGet(target, out var t) || t.Hp <= 0f) return;   // ungültig oder schon tot
            var armor = _military.TryGet(t.Type, out var md) ? md.Armor : default;
            float eff = DamageModel.Effective(amount, type, armor);
            if (eff <= 0f) return;
            float hp = t.Hp;
            bool killed = DamageModel.Apply(ref hp, eff);
            _units.SetHp(target, hp);
            _events.Publish(new UnitDamagedEvent { Target = target, Source = source, Amount = eff, Type = type, HpLeft = hp });
            if (killed)
            {
                int killerCiv = _units.TryGet(source, out var s) ? s.CivId : 0;
                _units.Despawn(target, source, killerCiv);   // einziges UnitDiedEvent
            }
        }

        public void ApplyAreaDamage(float x, float z, float radius, float amount, DamageType type, EntityId source)
        {
            if (radius <= 0f || amount <= 0f) return;
            _events.Publish(new ExplosionEvent { X = x, Z = z, Radius = radius, Type = type });
            QueryRadius(x, z, radius, _scratch);
            // Kopie, weil ApplyDamage Einheiten entfernen kann
            var hits = _scratch.ToArray();
            foreach (var id in hits)
            {
                if (!_units.TryGet(id, out var u)) continue;
                float dx = u.X - x, dz = u.Z - z;
                float f = DamageModel.AreaFalloff(MathF.Sqrt(dx * dx + dz * dz), radius);
                if (f > 0f) ApplyDamage(id, amount * f, type, source);
            }
        }

        internal void QueryRadius(float x, float z, float radius, List<EntityId> results)
        {
            results.Clear();
            if (_env.QueryRadius != null) { _env.QueryRadius(x, z, radius, results); return; }
            _units.CopyAliveIds(results);
            float r2 = radius * radius;
            for (int i = results.Count - 1; i >= 0; i--)
            {
                _units.TryGet(results[i], out var u);
                float dx = u.X - x, dz = u.Z - z;
                if (dx * dx + dz * dz > r2) results.RemoveAt(i);
            }
        }
    }
}
