using System;
using WorldBeyond.Core;

namespace WorldBeyond.Simulation.World.Navigation
{
    // Bewegungswerte eines Einheitentyps (kommen aus UnitTypeData; Werte pflegen Civ und Military).
    public struct MotionParams
    {
        public MovementClass MovementClass;
        public float Speed;            // m/s auf Kosten-1-Gelände
        public float TurnRateDeg;      // °/s; <= 0 bedeutet sofortiges Drehen (Infanterie)
        public float Acceleration;     // m/s²; <= 0 bedeutet sofortige Zielgeschwindigkeit
    }

    // Bewegungszustand einer Einheit in der Ebene; Höhe kommt immer aus IWorldQuery.SampleHeight.
    public struct MotionState
    {
        public float X;
        public float Z;
        public float Heading;          // Bogenmaß, 0 = +Z (Norden), im Uhrzeigersinn
        public float Speed;            // aktuelle Geschwindigkeit in m/s
    }

    // Ein Simulationsschritt Richtung Wegpunkt: Drehen mit Wenderate, Beschleunigen/Bremsen, Gelände- und Hangfaktor.
    // Wird vom MovementSystem pro Einheit und Tick aufgerufen (MovementSystem entsteht, sobald der UnitStore aus dem Fundament existiert).
    public static class MotionIntegrator
    {
        const float ArriveDistance = 0.5f;
        const float TwoPi = MathF.PI * 2f;

        // Liefert true, wenn der Wegpunkt erreicht ist. nextIsFinal: am letzten Wegpunkt wird auf 0 abgebremst.
        public static bool Step(ref MotionState s, in MotionParams p, IWorldQuery world, float targetX, float targetZ, bool nextIsFinal, float dt)
        {
            float dx = targetX - s.X;
            float dz = targetZ - s.Z;
            float dist = MathF.Sqrt(dx * dx + dz * dz);
            if (dist <= ArriveDistance)
            {
                if (nextIsFinal)
                    s.Speed = 0f;
                return true;
            }

            // Drehen
            float desired = MathF.Atan2(dx, dz);
            float delta = WrapAngle(desired - s.Heading);
            if (p.TurnRateDeg <= 0f)
            {
                s.Heading = desired;
                delta = 0f;
            }
            else
            {
                float maxTurn = p.TurnRateDeg * (MathF.PI / 180f) * dt;
                if (MathF.Abs(delta) <= maxTurn)
                {
                    s.Heading = desired;
                    delta = 0f;
                }
                else
                {
                    s.Heading = WrapAngle(s.Heading + MathF.Sign(delta) * maxTurn);
                    delta -= MathF.Sign(delta) * maxTurn;
                }
            }

            // Zielgeschwindigkeit aus Gelände; Fahrzeuge fahren in engen Kurven langsamer statt auf der Stelle zu wenden.
            var (tx, ty) = WorldCoords.WorldToTile(s.X, s.Z);
            float cost = world.MoveCost(tx, ty, p.MovementClass);
            if (float.IsInfinity(cost))
                cost = 3f;  // auf blockierter Kachel (z. B. frisch bebaut) langsam weiterfahren, damit sie verlassen wird
            float target = p.Speed / Math.Max(0.5f, cost);
            if (p.TurnRateDeg > 0f)
            {
                // Ziel seitlich/hinter dem Fahrzeug: fast anhalten und drehen, sonst kreist es um nahe Wegpunkte.
                float absDelta = MathF.Abs(delta);
                float turnFactor = 1f - Math.Min(1f, absDelta / (MathF.PI * 0.5f));
                target *= absDelta >= MathF.PI * 0.5f ? 0.05f : 0.25f + 0.75f * turnFactor;
            }
            // Vor dem letzten Wegpunkt bremsen: v² = 2·a·s
            if (nextIsFinal && p.Acceleration > 0f)
                target = Math.Min(target, MathF.Sqrt(2f * p.Acceleration * dist));

            if (p.Acceleration <= 0f)
                s.Speed = target;
            else if (s.Speed < target)
                s.Speed = Math.Min(target, s.Speed + p.Acceleration * dt);
            else
                s.Speed = Math.Max(target, s.Speed - p.Acceleration * 2f * dt);   // Bremsen doppelt so stark

            float move = Math.Min(s.Speed * dt, dist);
            s.X += MathF.Sin(s.Heading) * move;
            s.Z += MathF.Cos(s.Heading) * move;
            return false;
        }

        static float WrapAngle(float a)
        {
            while (a > MathF.PI) a -= TwoPi;
            while (a < -MathF.PI) a += TwoPi;
            return a;
        }
    }
}
