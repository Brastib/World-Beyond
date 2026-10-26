using System;

namespace WorldBeyond.Core
{
    /// <summary>
    /// Einziger Zufallsgenerator der Simulation (ARCHITECTURE §1 Prinzip 8). xorshift64* mit
    /// SplitMix64-Initialisierung. Der komplette Zustand ist <see cref="State"/> und wird gespeichert.
    /// Kein UnityEngine.Random in der Simulation.
    /// </summary>
    public sealed class SimRandom
    {
        private ulong _state;

        public SimRandom(ulong seed)
        {
            _state = SplitMix64(seed);
            if (_state == 0) _state = 0x9E3779B97F4A7C15UL;
        }

        /// <summary>Vollständiger Generatorzustand; nie 0. Setzen auf 0 ist ein Fehler.</summary>
        public ulong State
        {
            get => _state;
            set
            {
                if (value == 0) throw new ArgumentException("SimRandom.State darf nicht 0 sein.", nameof(value));
                _state = value;
            }
        }

        public ulong NextULong()
        {
            ulong x = _state;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            _state = x;
            return x * 0x2545F4914F6CDD1DUL;
        }

        /// <summary>Gleichverteilt in [minInclusive, maxExclusive). maxExclusive &lt;= minInclusive ist ein Fehler.</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), $"Range({minInclusive}, {maxExclusive}) ist leer.");
            ulong span = (ulong)((long)maxExclusive - minInclusive);
            ulong r = (NextULong() >> 32) * span >> 32;
            return (int)(minInclusive + (long)r);
        }

        /// <summary>Gleichverteilt in [0, 1).</summary>
        public float Value01() => (NextULong() >> 40) * (1f / (1 << 24));

        /// <summary>Gleichverteilt in [min, max).</summary>
        public float Range(float min, float max) => min + (max - min) * Value01();

        private static ulong SplitMix64(ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }
    }
}
