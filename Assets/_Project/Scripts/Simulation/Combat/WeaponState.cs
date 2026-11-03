using System;

namespace WorldBeyond.Simulation.Combat
{
    /// <summary>Statische Waffenwerte (Daten, über ContentRegistry als "weapon.*" gespeichert).</summary>
    public sealed class WeaponData
    {
        public string Key = "";
        public float Range;            // m
        public float MinRange;         // m (Artillerie, Raketenwerfer)
        public float FireInterval;     // s zwischen Schüssen im Magazin
        public int MagazineSize;       // Schuss pro Magazin/Salve
        public float ReloadTime;       // s
        public int MaxAmmoReserve;     // Reserve außerhalb des Magazins; < 0 = unbegrenzt
        public float Damage;
        public DamageType DamageType;
        public ProjectileKind Projectile;
        public float ProjectileSpeed;  // m/s (Hitscan ignoriert)
        public float BlastRadius;      // m, 0 = Einzelziel
        public float SpreadRadius;     // m Streuung am Ziel bei max. Reichweite
        public bool Indirect;          // braucht keine eigene Sichtlinie (Ziel muss von Verbündeten gesehen werden)
    }

    public enum FireBlock : byte { None = 0, Cooldown = 1, Reloading = 2, OutOfAmmo = 3, OutOfRange = 4, TooClose = 5 }

    /// <summary>Laufzeitzustand einer Waffe (gespeichert). Reiner Wert, tickt mit dt.</summary>
    public struct WeaponState
    {
        public int InMagazine;
        public int Reserve;
        public float Cooldown;
        public float ReloadRemaining;

        public static WeaponState Full(WeaponData w) => new WeaponState
        {
            InMagazine = w.MagazineSize,
            Reserve = w.MaxAmmoReserve,
        };

        public bool IsReloading => ReloadRemaining > 0f;

        public void Tick(WeaponData w, float dt)
        {
            if (Cooldown > 0f) Cooldown = MathF.Max(0f, Cooldown - dt);
            if (ReloadRemaining > 0f)
            {
                ReloadRemaining -= dt;
                if (ReloadRemaining <= 0f)
                {
                    ReloadRemaining = 0f;
                    FinishReload(w);
                }
            }
        }

        public FireBlock CanFire(WeaponData w, float distance)
        {
            if (distance > w.Range) return FireBlock.OutOfRange;
            if (distance < w.MinRange) return FireBlock.TooClose;
            if (ReloadRemaining > 0f) return FireBlock.Reloading;
            if (InMagazine <= 0) return Reserve == 0 ? FireBlock.OutOfAmmo : FireBlock.Reloading;
            if (Cooldown > 0f) return FireBlock.Cooldown;
            return FireBlock.None;
        }

        /// <summary>Verbraucht einen Schuss. Startet automatisch das Nachladen, wenn das Magazin leer ist.</summary>
        public bool TryFire(WeaponData w, float distance)
        {
            if (CanFire(w, distance) != FireBlock.None) return false;
            InMagazine--;
            Cooldown = w.FireInterval;
            if (InMagazine == 0 && Reserve != 0) ReloadRemaining = w.ReloadTime;
            return true;
        }

        /// <summary>Versorgung durch Versorgungs-LKW. Gibt die tatsächlich aufgefüllte Menge zurück.</summary>
        public int Resupply(WeaponData w, int amount)
        {
            if (w.MaxAmmoReserve < 0 || amount <= 0) return 0;
            int space = w.MaxAmmoReserve - Reserve;
            int added = Math.Min(space, amount);
            Reserve += added;
            if (InMagazine == 0 && ReloadRemaining <= 0f && Reserve > 0) ReloadRemaining = w.ReloadTime;
            return added;
        }

        private void FinishReload(WeaponData w)
        {
            int need = w.MagazineSize - InMagazine;
            if (Reserve < 0) { InMagazine = w.MagazineSize; return; }
            int take = Math.Min(need, Reserve);
            InMagazine += take;
            Reserve -= take;
        }
    }
}
