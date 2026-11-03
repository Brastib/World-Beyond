using System;
using System.Numerics;

namespace WorldBeyond.Simulation.Combat
{
    public static class LineOfSight
    {
        /// <summary>
        /// Sichtlinie zwischen zwei Augpunkten über ein Höhenfeld (Terrain + Hindernishöhe, z. B. Wald).
        /// heightAt(worldX, worldZ) kommt später aus IWorldGrid. Abtastung im Abstand step (Standard halbe Kachel).
        /// </summary>
        public static bool Clear(Vector3 from, Vector3 to, Func<float, float, float> heightAt, float step = 2f)
        {
            var d = to - from;
            float horiz = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
            int samples = (int)(horiz / step);
            for (int i = 1; i < samples; i++)
            {
                float t = (float)i / samples;
                var p = from + d * t;
                if (heightAt(p.X, p.Z) > p.Y) return false;
            }
            return true;
        }
    }

    /// <summary>Eingangswerte für die taktische Zielbewertung (CombatSystem).</summary>
    public struct TargetCandidate
    {
        public float Distance;
        public float Effectiveness;   // 0…1: 1 − Panzerreduktion des Ziels gegen meine Schadensart
        public float Threat;          // 0…1: Kampfstärke des Ziels relativ zu meiner
        public float HpFraction;      // 0…1
        public bool IsOrderTarget;    // ausdrücklich befohlenes Ziel
        public bool Visible;          // eigene Sicht oder (bei indirektem Feuer) Sicht eines Verbündeten
    }

    public static class TargetScoring
    {
        public const float Invalid = float.NegativeInfinity;

        public static float Score(in TargetCandidate c, float weaponRange, float minRange)
        {
            if (!c.Visible || c.Distance > weaponRange || c.Distance < minRange) return Invalid;
            if (c.Effectiveness <= 0.05f && !c.IsOrderTarget) return Invalid; // MG gegen Panzer: sinnlos
            float s = 0f;
            if (c.IsOrderTarget) s += 1000f;
            s += c.Effectiveness * 50f;
            s += c.Threat * 20f;
            s += (1f - c.HpFraction) * 15f;              // angeschlagene Ziele zuerst erledigen
            s -= (c.Distance / MathF.Max(1f, weaponRange)) * 25f;
            return s;
        }

        /// <summary>Index des besten Ziels oder −1.</summary>
        public static int PickBest(ReadOnlySpan<TargetCandidate> candidates, float weaponRange, float minRange)
        {
            int best = -1;
            float bestScore = Invalid;
            for (int i = 0; i < candidates.Length; i++)
            {
                float s = Score(candidates[i], weaponRange, minRange);
                if (s > bestScore) { bestScore = s; best = i; }
            }
            return best;
        }
    }
}
