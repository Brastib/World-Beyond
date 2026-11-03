using System;
using System.Numerics;

namespace WorldBeyond.Simulation.Combat
{
    public static class Ballistics
    {
        public const float Gravity = 9.81f;

        /// <summary>
        /// Abschusswinkel (Radiant) für horizontale Entfernung range und Höhendifferenz dy (Ziel − Start).
        /// highArc = Steilfeuer (Artillerie). false, wenn das Ziel mit dieser Geschwindigkeit unerreichbar ist.
        /// </summary>
        public static bool TrySolveAngle(float range, float dy, float speed, bool highArc, out float angle, float g = Gravity)
        {
            angle = 0f;
            if (range <= 0f || speed <= 0f) return false;
            float v2 = speed * speed;
            float disc = v2 * v2 - g * (g * range * range + 2f * dy * v2);
            if (disc < 0f) return false;
            float root = MathF.Sqrt(disc);
            float tan = (v2 + (highArc ? root : -root)) / (g * range);
            angle = MathF.Atan(tan);
            return true;
        }

        /// <summary>Anfangsgeschwindigkeit für einen Schuss von from nach to (Richtung horizontal + Winkel).</summary>
        public static bool TryLaunchVelocity(Vector3 from, Vector3 to, float speed, bool highArc, out Vector3 velocity, float g = Gravity)
        {
            velocity = default;
            var flat = new Vector3(to.X - from.X, 0f, to.Z - from.Z);
            float range = flat.Length();
            if (range < 1e-4f) return false;
            if (!TrySolveAngle(range, to.Y - from.Y, speed, highArc, out float a, g)) return false;
            var dir = flat / range;
            velocity = dir * (speed * MathF.Cos(a)) + new Vector3(0f, speed * MathF.Sin(a), 0f);
            return true;
        }

        /// <summary>Ein Integrationsschritt (semi-implizites Euler, stabil bei festem Tick).</summary>
        public static void Step(ref Vector3 position, ref Vector3 velocity, float dt, float g = Gravity)
        {
            velocity.Y -= g * dt;
            position += velocity * dt;
        }

        /// <summary>
        /// Schneidet das Segment a→b die Kugel? t = Anteil 0…1 entlang des Segments am ersten Kontakt.
        /// Segment-Test statt Punkt-Test, damit schnelle Geschosse nicht durch Ziele "tunneln".
        /// </summary>
        public static bool SegmentHitsSphere(Vector3 a, Vector3 b, Vector3 center, float radius, out float t)
        {
            t = 0f;
            var d = b - a;
            var m = a - center;
            float c = Vector3.Dot(m, m) - radius * radius;
            if (c <= 0f) return true;                // Start schon innerhalb
            float dd = Vector3.Dot(d, d);
            if (dd < 1e-12f) return false;
            float bq = Vector3.Dot(m, d);
            if (bq > 0f) return false;               // bewegt sich weg
            float disc = bq * bq - dd * c;
            if (disc < 0f) return false;
            t = (-bq - MathF.Sqrt(disc)) / dd;
            return t <= 1f;
        }
    }
}
