namespace WorldBeyond.Simulation.World.Generation
{
    // Zustandsloses, seed-basiertes Rauschen für die Weltgenerierung.
    // Nur Ganzzahl-Hashes und Grundrechenarten (+, −, ×, ÷), keine Winkelfunktionen und kein Mathf.PerlinNoise,
    // damit dieselbe Seed auf jeder Plattform dieselbe Welt ergibt.
    public static class HashNoise
    {
        // 8 Gradientenrichtungen (Achsen und Diagonalen, Diagonalen auf Länge 1 normiert).
        static readonly float[] GradX = { 1f, -1f, 0f, 0f, 0.70710678f, -0.70710678f, 0.70710678f, -0.70710678f };
        static readonly float[] GradY = { 0f, 0f, 1f, -1f, 0.70710678f, 0.70710678f, -0.70710678f, -0.70710678f };

        public static uint Hash(ulong seed, int x, int y)
        {
            unchecked
            {
                ulong h = seed ^ 0x9E3779B97F4A7C15UL;
                h ^= (uint)x * 0x85EBCA6BUL;
                h = (h ^ (h >> 31)) * 0xBF58476D1CE4E5B9UL;
                h ^= (uint)y * 0xC2B2AE35UL;
                h = (h ^ (h >> 27)) * 0x94D049BB133111EBUL;
                h ^= h >> 31;
                return (uint)(h ^ (h >> 32));
            }
        }

        public static uint Hash(ulong seed, int x, int y, int z) => Hash(seed ^ ((ulong)(uint)z * 0xD6E8FEB86659FD93UL), x, y);

        // Gleichverteilt in [0, 1).
        public static float Value01(ulong seed, int x, int y) => (Hash(seed, x, y) >> 8) * (1f / 16777216f);

        public static float Value01(ulong seed, int x, int y, int z) => (Hash(seed, x, y, z) >> 8) * (1f / 16777216f);

        // Leitet für jeden Generierungsschritt eine eigene Seed ab, damit Schritte unabhängig sind.
        public static ulong DeriveSeed(ulong seed, uint step)
        {
            unchecked
            {
                ulong h = seed + step * 0x9E3779B97F4A7C15UL;
                h = (h ^ (h >> 30)) * 0xBF58476D1CE4E5B9UL;
                h = (h ^ (h >> 27)) * 0x94D049BB133111EBUL;
                return h ^ (h >> 31);
            }
        }

        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        static int FastFloor(float v)
        {
            int i = (int)v;
            return v < i ? i - 1 : i;
        }

        // Gradientenrauschen, ungefähr in [-1, 1].
        public static float Gradient(ulong seed, float x, float y)
        {
            int x0 = FastFloor(x);
            int y0 = FastFloor(y);
            float fx = x - x0;
            float fy = y - y0;

            float n00 = Dot(seed, x0, y0, fx, fy);
            float n10 = Dot(seed, x0 + 1, y0, fx - 1f, fy);
            float n01 = Dot(seed, x0, y0 + 1, fx, fy - 1f);
            float n11 = Dot(seed, x0 + 1, y0 + 1, fx - 1f, fy - 1f);

            float u = Fade(fx);
            float v = Fade(fy);
            float nx0 = n00 + u * (n10 - n00);
            float nx1 = n01 + u * (n11 - n01);
            // Maximalwert für 2D-Gradientenrauschen mit Einheitsgradienten ist √0,5; auf ~[-1, 1] skalieren.
            return (nx0 + v * (nx1 - nx0)) * 1.41421356f;
        }

        static float Dot(ulong seed, int ix, int iy, float dx, float dy)
        {
            int g = (int)(Hash(seed, ix, iy) & 7u);
            return GradX[g] * dx + GradY[g] * dy;
        }

        // Fraktales Rauschen (fBm), normiert auf ungefähr [-1, 1].
        public static float Fbm(ulong seed, float x, float y, int octaves, float lacunarity = 2f, float gain = 0.5f)
        {
            float sum = 0f, amp = 1f, norm = 0f, freq = 1f;
            for (int o = 0; o < octaves; o++)
            {
                sum += Gradient(seed + (ulong)o * 0x632BE59BD9B4E019UL, x * freq, y * freq) * amp;
                norm += amp;
                amp *= gain;
                freq *= lacunarity;
            }
            return sum / norm;
        }

        // Grat-Rauschen für Gebirgsketten, in [0, 1]; hohe Werte entlang schmaler Linien.
        public static float Ridged(ulong seed, float x, float y, int octaves)
        {
            float sum = 0f, amp = 1f, norm = 0f, freq = 1f;
            for (int o = 0; o < octaves; o++)
            {
                float n = Gradient(seed + (ulong)o * 0x632BE59BD9B4E019UL, x * freq, y * freq);
                n = 1f - (n < 0f ? -n : n);
                n *= n;
                sum += n * amp;
                norm += amp;
                amp *= 0.5f;
                freq *= 2f;
            }
            return sum / norm;
        }

        public static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = (x - edge0) / (edge1 - edge0);
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            return t * t * (3f - 2f * t);
        }

        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
