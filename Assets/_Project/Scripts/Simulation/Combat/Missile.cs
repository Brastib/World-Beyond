using System;
using System.Numerics;

namespace WorldBeyond.Simulation.Combat
{
    // Abstrakte Spielwerte, keine realen Einsatzdaten.
    public enum GuidanceMode : byte { None = 0, Target = 1, Position = 2 }
    public enum MissilePhase : byte { Launch = 0, Climb = 1, Cruise = 2, Terminal = 3, Unpowered = 4 }

    public sealed class MissileData
    {
        public string Key = "";
        public float Speed = 120f;           // m/s
        public float TurnRateDeg = 90f;      // Grad/s
        public float Fuel = 8f;              // s Antrieb
        public float LaunchTime = 0.25f;     // s geradeaus nach dem Start
        public float CruiseAltitude = 30f;   // m über Startpunkt; 0 = direkter Flug
        public float TerminalDistance = 60f; // m horizontal, ab dann direkt aufs Ziel
        public GuidanceMode Guidance = GuidanceMode.Target;
        public float WarheadDamage = 300f;
        public float BlastRadius = 4f;
        public DamageType DamageType = DamageType.Explosive;
    }

    /// <summary>Laufzeitzustand einer Rakete (liegt im ProjectileStore, gespeichert).</summary>
    public struct MissileState
    {
        public Vector3 Position;
        public Vector3 Direction;   // Einheitsvektor
        public float Speed;
        public float FuelLeft;
        public float Age;
        public float LaunchAltitude;
        public MissilePhase Phase;

        public static MissileState Launch(MissileData d, Vector3 pos, Vector3 dir) => new MissileState
        {
            Position = pos,
            Direction = Vector3.Normalize(dir),
            Speed = d.Speed,
            FuelLeft = d.Fuel,
            LaunchAltitude = pos.Y,
            Phase = MissilePhase.Launch,
        };

        public Vector3 Velocity => Direction * Speed;
    }

    public static class MissileGuidance
    {
        /// <summary>
        /// Ein Tick. aim = aktuelle Zielposition (bei Target-Lenkung jeden Tick neu, bei None ignoriert).
        /// Gibt die Position vor dem Schritt zurück (für den Segment-Treffertest).
        /// </summary>
        public static Vector3 Step(ref MissileState s, MissileData d, Vector3 aim, float dt)
        {
            var prev = s.Position;
            s.Age += dt;

            if (s.Phase != MissilePhase.Unpowered)
            {
                s.FuelLeft -= dt;
                if (s.FuelLeft <= 0f) { s.FuelLeft = 0f; s.Phase = MissilePhase.Unpowered; }
            }

            if (s.Phase == MissilePhase.Unpowered)
            {
                // ohne Antrieb: ballistisch weiter
                var v = s.Velocity;
                var p = s.Position;
                Ballistics.Step(ref p, ref v, dt);
                s.Position = p;
                s.Speed = v.Length();
                if (s.Speed > 1e-4f) s.Direction = v / s.Speed;
                return prev;
            }

            UpdatePhase(ref s, d, aim);
            if (d.Guidance != GuidanceMode.None && s.Phase != MissilePhase.Launch)
            {
                var desired = DesiredDirection(in s, d, aim);
                s.Direction = RotateTowards(s.Direction, desired, d.TurnRateDeg * (MathF.PI / 180f) * dt);
            }
            s.Position += s.Direction * (s.Speed * dt);
            return prev;
        }

        private static void UpdatePhase(ref MissileState s, MissileData d, Vector3 aim)
        {
            if (s.Phase == MissilePhase.Launch && s.Age >= d.LaunchTime)
                s.Phase = d.CruiseAltitude > 0f ? MissilePhase.Climb : MissilePhase.Terminal;

            float horiz = HorizontalDistance(s.Position, aim);
            if ((s.Phase == MissilePhase.Climb || s.Phase == MissilePhase.Cruise) && horiz <= d.TerminalDistance)
                s.Phase = MissilePhase.Terminal;
            else if (s.Phase == MissilePhase.Climb && s.Position.Y >= s.LaunchAltitude + d.CruiseAltitude)
                s.Phase = MissilePhase.Cruise;
        }

        private static Vector3 DesiredDirection(in MissileState s, MissileData d, Vector3 aim)
        {
            switch (s.Phase)
            {
                case MissilePhase.Climb:
                {
                    var flat = Flat(aim - s.Position);
                    return Vector3.Normalize(flat + new Vector3(0f, 1f, 0f)); // 45° Steigflug Richtung Ziel
                }
                case MissilePhase.Cruise:
                {
                    var flat = Flat(aim - s.Position);
                    float dy = (s.LaunchAltitude + d.CruiseAltitude) - s.Position.Y;
                    return Vector3.Normalize(flat * 10f + new Vector3(0f, dy, 0f));
                }
                default:
                    var to = aim - s.Position;
                    return to.LengthSquared() > 1e-6f ? Vector3.Normalize(to) : s.Direction;
            }
        }

        /// <summary>Dreht Einheitsvektor from höchstens maxRadians Richtung to.</summary>
        public static Vector3 RotateTowards(Vector3 from, Vector3 to, float maxRadians)
        {
            float dot = Math.Clamp(Vector3.Dot(from, to), -1f, 1f);
            float angle = MathF.Acos(dot);
            if (angle <= maxRadians || angle < 1e-5f) return to;
            Vector3 b = to;
            if (angle > MathF.PI - 1e-3f)
            {
                // genau entgegengesetzt: über eine senkrechte Achse drehen
                var axis = MathF.Abs(from.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
                b = Vector3.Normalize(Vector3.Cross(Vector3.Cross(from, axis), from));
                float t2 = maxRadians / (MathF.PI / 2f);
                return Vector3.Normalize(Slerp(from, b, MathF.PI / 2f, MathF.Min(1f, t2)));
            }
            return Vector3.Normalize(Slerp(from, b, angle, maxRadians / angle));
        }

        private static Vector3 Slerp(Vector3 a, Vector3 b, float omega, float t)
        {
            float so = MathF.Sin(omega);
            return (MathF.Sin((1f - t) * omega) / so) * a + (MathF.Sin(t * omega) / so) * b;
        }

        private static Vector3 Flat(Vector3 v)
        {
            var f = new Vector3(v.X, 0f, v.Z);
            float l = f.Length();
            return l > 1e-5f ? f / l : Vector3.Zero;
        }

        public static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }
    }
}
