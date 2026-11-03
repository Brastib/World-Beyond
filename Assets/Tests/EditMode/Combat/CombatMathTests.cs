using System;
using System.Numerics;
using NUnit.Framework;
using WorldBeyond.Simulation.Combat;

namespace WorldBeyond.Tests.EditMode.Combat
{
    public class DamageModelTests
    {
        static readonly ArmorProfile Tank = new ArmorProfile(0.95f, 0.85f, 0.5f, 0.3f, 0.6f);
        static readonly ArmorProfile Infantry = new ArmorProfile(0f, 0f, 0f, 0f, 0f);

        [Test] public void SmallArmsBarelyHurtTank() =>
            Assert.That(DamageModel.Effective(100f, DamageType.SmallArms, Tank), Is.EqualTo(5f).Within(1e-3));

        [Test] public void ArmorPiercingHurtsTankMoreThanHeavyMG() =>
            Assert.That(DamageModel.Effective(100f, DamageType.ArmorPiercing, Tank),
                Is.GreaterThan(DamageModel.Effective(100f, DamageType.HeavyMG, Tank) * 4f));

        [Test] public void NoArmorTakesFullDamage() =>
            Assert.That(DamageModel.Effective(40f, DamageType.HeavyMG, Infantry), Is.EqualTo(40f));

        [Test] public void ArmorIsClampedToMax() =>
            Assert.That(new ArmorProfile(2f, -1f, 0, 0, 0).Reduction(DamageType.SmallArms), Is.EqualTo(ArmorProfile.MaxReduction));

        [Test] public void NegativeDamageIsZero() =>
            Assert.That(DamageModel.Effective(-5f, DamageType.Fire, Infantry), Is.EqualTo(0f));

        [TestCase(0f, 1f)] [TestCase(5f, 0.5f)] [TestCase(10f, 0f)] [TestCase(20f, 0f)]
        public void AreaFalloffIsLinear(float dist, float expected) =>
            Assert.That(DamageModel.AreaFalloff(dist, 10f), Is.EqualTo(expected).Within(1e-4));

        [Test] public void AreaFalloffInnerRadiusIsFull() =>
            Assert.That(DamageModel.AreaFalloff(2f, 10f, 3f), Is.EqualTo(1f));

        [Test] public void ApplyReportsKillExactlyOnce()
        {
            float hp = 50f;
            Assert.That(DamageModel.Apply(ref hp, 30f), Is.False);
            Assert.That(DamageModel.Apply(ref hp, 30f), Is.True);
            Assert.That(hp, Is.EqualTo(0f));
            Assert.That(DamageModel.Apply(ref hp, 30f), Is.False, "toter Gegner darf kein zweites Todes-Event erzeugen");
        }
    }

    public class WeaponStateTests
    {
        static WeaponData Rifle() => new WeaponData { Range = 300, FireInterval = 0.2f, MagazineSize = 3, ReloadTime = 2f, MaxAmmoReserve = 6 };
        static WeaponData Mortar() => new WeaponData { Range = 1500, MinRange = 150, FireInterval = 3f, MagazineSize = 1, ReloadTime = 4f, MaxAmmoReserve = -1 };

        [Test] public void CooldownBlocksRapidFire()
        {
            var w = Rifle(); var s = WeaponState.Full(w);
            Assert.That(s.TryFire(w, 100), Is.True);
            Assert.That(s.CanFire(w, 100), Is.EqualTo(FireBlock.Cooldown));
            s.Tick(w, 0.2f);
            Assert.That(s.TryFire(w, 100), Is.True);
        }

        [Test] public void EmptyMagazineReloadsFromReserve()
        {
            var w = Rifle(); var s = WeaponState.Full(w);
            for (int i = 0; i < 3; i++) { Assert.That(s.TryFire(w, 100), Is.True); s.Tick(w, 0.2f); }
            Assert.That(s.CanFire(w, 100), Is.EqualTo(FireBlock.Reloading));
            s.Tick(w, 2f);
            Assert.That(s.InMagazine, Is.EqualTo(3));
            Assert.That(s.Reserve, Is.EqualTo(3));
        }

        [Test] public void RunsDryAndNeedsResupply()
        {
            var w = Rifle(); var s = WeaponState.Full(w);
            int shots = 0;
            for (int t = 0; t < 200; t++) { if (s.TryFire(w, 100)) shots++; s.Tick(w, 0.1f); }
            Assert.That(shots, Is.EqualTo(9), "3 im Magazin + 6 Reserve");
            Assert.That(s.CanFire(w, 100), Is.EqualTo(FireBlock.OutOfAmmo));
            Assert.That(s.Resupply(w, 100), Is.EqualTo(6), "Reserve ist auf 6 begrenzt");
            s.Tick(w, 2f);
            Assert.That(s.CanFire(w, 100), Is.EqualTo(FireBlock.None));
        }

        [Test] public void RangeAndMinRange()
        {
            var w = Mortar(); var s = WeaponState.Full(w);
            Assert.That(s.CanFire(w, 100), Is.EqualTo(FireBlock.TooClose));
            Assert.That(s.CanFire(w, 2000), Is.EqualTo(FireBlock.OutOfRange));
            Assert.That(s.CanFire(w, 800), Is.EqualTo(FireBlock.None));
        }

        [Test] public void UnlimitedReserveNeverRunsDry()
        {
            var w = Mortar(); var s = WeaponState.Full(w);
            int shots = 0;
            for (int t = 0; t < 1000; t++) { if (s.TryFire(w, 800)) shots++; s.Tick(w, 0.1f); }
            Assert.That(shots, Is.GreaterThan(20));
        }
    }

    public class BallisticsTests
    {
        [Test] public void SolvedShotLandsOnTarget([Values(false, true)] bool highArc)
        {
            var from = new Vector3(0, 10, 0);
            var to = new Vector3(600, 30, 250);
            Assert.That(Ballistics.TryLaunchVelocity(from, to, 120f, highArc, out var v), Is.True);
            var p = from;
            float dt = 0.1f; // Sim-Tick
            float bestDist = float.MaxValue;
            for (int i = 0; i < 2000 && !(v.Y < 0 && p.Y < to.Y - 5); i++)
            {
                var prev = p;
                Ballistics.Step(ref p, ref v, dt);
                // nächster Abstand des Segments zum Ziel
                if (Ballistics.SegmentHitsSphere(prev, p, to, 8f, out _)) { bestDist = 0; break; }
                bestDist = MathF.Min(bestDist, Vector3.Distance(p, to));
            }
            Assert.That(bestDist, Is.LessThan(8f), "Geschoss trifft im 8-m-Radius trotz festem 0,1-s-Tick");
        }

        [Test] public void HighArcIsSteeper()
        {
            Ballistics.TrySolveAngle(500, 0, 100, false, out float low);
            Ballistics.TrySolveAngle(500, 0, 100, true, out float high);
            Assert.That(high, Is.GreaterThan(low));
            Assert.That(high, Is.GreaterThan(MathF.PI / 4));
        }

        [Test] public void OutOfReachIsRejected() =>
            Assert.That(Ballistics.TrySolveAngle(5000, 0, 100, false, out _), Is.False); // max ≈ v²/g ≈ 1019 m

        [Test] public void FastProjectileDoesNotTunnel()
        {
            // 900 m/s, 0,1 s Tick = 90 m pro Schritt, Ziel 1 m Radius mitten drin
            Assert.That(Ballistics.SegmentHitsSphere(new Vector3(0, 1, 0), new Vector3(90, 1, 0), new Vector3(45, 1.5f, 0), 1f, out float t), Is.True);
            Assert.That(t, Is.InRange(0.4f, 0.5f));
        }

        [Test] public void MissesWhenPassingBeside() =>
            Assert.That(Ballistics.SegmentHitsSphere(new Vector3(0, 1, 0), new Vector3(90, 1, 0), new Vector3(45, 1, 3), 1f, out _), Is.False);

        [Test] public void NoHitBehindStart() =>
            Assert.That(Ballistics.SegmentHitsSphere(new Vector3(0, 0, 0), new Vector3(10, 0, 0), new Vector3(-5, 0, 0), 1f, out _), Is.False);
    }

    public class MissileTests
    {
        static (bool hit, float time, float maxAlt) Fly(MissileData d, Vector3 start, Vector3 dir, Func<float, Vector3> target, float radius = 2f)
        {
            var s = MissileState.Launch(d, start, dir);
            float dt = 0.1f, maxAlt = start.Y;
            for (int i = 0; i < 600; i++)
            {
                float time = i * dt;
                var aim = target(time);
                var prev = MissileGuidance.Step(ref s, d, aim, dt);
                maxAlt = MathF.Max(maxAlt, s.Position.Y);
                if (Ballistics.SegmentHitsSphere(prev, s.Position, target(time + dt), radius, out _)) return (true, time, maxAlt);
                if (s.Position.Y < -50) break;
            }
            return (false, -1, maxAlt);
        }

        [Test] public void GuidedMissileHitsStaticTarget()
        {
            var d = new MissileData { Speed = 150, TurnRateDeg = 120, Fuel = 10, CruiseAltitude = 40 };
            var r = Fly(d, new Vector3(0, 20, 0), new Vector3(0, 1, 1), _ => new Vector3(400, 22, 900));
            Assert.That(r.hit, Is.True);
            Assert.That(r.maxAlt, Is.GreaterThan(50f), "Steig- und Marschflug über Startpunkt");
        }

        [Test] public void GuidedMissileHitsMovingVehicle()
        {
            var d = new MissileData { Speed = 160, TurnRateDeg = 150, Fuel = 10, CruiseAltitude = 0 };
            // Fahrzeug fährt mit 15 m/s quer
            var r = Fly(d, new Vector3(0, 21, 0), new Vector3(0, 0.2f, 1), t => new Vector3(-100 + 15 * t, 21, 500), 2.5f);
            Assert.That(r.hit, Is.True);
        }

        [Test] public void UnguidedRocketFliesStraight()
        {
            var d = new MissileData { Speed = 100, Fuel = 2, Guidance = GuidanceMode.None, CruiseAltitude = 0 };
            var s = MissileState.Launch(d, new Vector3(0, 0, 0), new Vector3(1, 0, 0));
            for (int i = 0; i < 10; i++) MissileGuidance.Step(ref s, d, new Vector3(0, 0, 500), 0.1f);
            Assert.That(s.Position.X, Is.EqualTo(100f).Within(0.5f));
            Assert.That(MathF.Abs(s.Position.Z), Is.LessThan(1e-3f), "ungelenkt: ignoriert Ziel");
        }

        [Test] public void OutOfFuelFallsBallistically()
        {
            var d = new MissileData { Speed = 100, Fuel = 0.5f, Guidance = GuidanceMode.None };
            var s = MissileState.Launch(d, new Vector3(0, 100, 0), new Vector3(1, 0, 0));
            for (int i = 0; i < 30; i++) MissileGuidance.Step(ref s, d, default, 0.1f);
            Assert.That(s.Phase, Is.EqualTo(MissilePhase.Unpowered));
            Assert.That(s.Position.Y, Is.LessThan(100f - 20f));
        }

        [Test] public void TurnRateIsLimited()
        {
            var from = new Vector3(1, 0, 0);
            var r = MissileGuidance.RotateTowards(from, new Vector3(0, 0, 1), 0.1f);
            Assert.That(MathF.Acos(Vector3.Dot(from, r)), Is.EqualTo(0.1f).Within(1e-4));
            var opp = MissileGuidance.RotateTowards(from, new Vector3(-1, 0, 0), 0.1f);
            Assert.That(MathF.Acos(Math.Clamp(Vector3.Dot(from, opp), -1, 1)), Is.EqualTo(0.1f).Within(1e-3), "auch bei entgegengesetzter Richtung");
            Assert.That(opp.Length(), Is.EqualTo(1f).Within(1e-4));
        }
    }

    public class TargetingTests
    {
        static float Flat(float x, float z) => 0f;
        static float Hill(float x, float z) => (x > 40 && x < 60) ? 15f : 0f;

        [Test] public void LosClearOnFlatGround() =>
            Assert.That(LineOfSight.Clear(new Vector3(0, 2, 0), new Vector3(100, 2, 0), Flat), Is.True);

        [Test] public void LosBlockedByHill() =>
            Assert.That(LineOfSight.Clear(new Vector3(0, 2, 0), new Vector3(100, 2, 0), Hill), Is.False);

        [Test] public void LosOverHillFromHigh() =>
            Assert.That(LineOfSight.Clear(new Vector3(0, 30, 0), new Vector3(100, 30, 0), Hill), Is.True);

        [Test] public void MgPrefersInfantryOverTank()
        {
            var c = new[]
            {
                new TargetCandidate { Distance = 100, Effectiveness = 0.15f, Threat = 1f, HpFraction = 1, Visible = true }, // Panzer
                new TargetCandidate { Distance = 200, Effectiveness = 1f, Threat = 0.2f, HpFraction = 1, Visible = true },  // Infanterie
            };
            Assert.That(TargetScoring.PickBest(c, 400, 0), Is.EqualTo(1));
        }

        [Test] public void OrderTargetWins()
        {
            var c = new[]
            {
                new TargetCandidate { Distance = 50, Effectiveness = 1f, Threat = 1f, HpFraction = 0.1f, Visible = true },
                new TargetCandidate { Distance = 350, Effectiveness = 0.5f, Threat = 0f, HpFraction = 1, Visible = true, IsOrderTarget = true },
            };
            Assert.That(TargetScoring.PickBest(c, 400, 0), Is.EqualTo(1));
        }

        [Test] public void InvisibleOrOutOfRangeIgnored()
        {
            var c = new[]
            {
                new TargetCandidate { Distance = 50, Effectiveness = 1f, Visible = false },
                new TargetCandidate { Distance = 900, Effectiveness = 1f, Visible = true },
                new TargetCandidate { Distance = 50, Effectiveness = 0.01f, Visible = true },
            };
            Assert.That(TargetScoring.PickBest(c, 400, 0), Is.EqualTo(-1));
        }
    }
}
