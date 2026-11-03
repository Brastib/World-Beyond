using System;
using System.Collections.Generic;
using NUnit.Framework;
using WorldBeyond.Core;
using WorldBeyond.Simulation;
using WorldBeyond.Simulation.Combat;
using WorldBeyond.Simulation.Military;

namespace WorldBeyond.Tests.EditMode.Combat
{
    public class SettlementCaptureTests
    {
        private const int SettlementId = 7;
        private const float Sx = 0f, Sz = 400f;

        /// <summary>Testwelt mit einer Siedlung von Civ 2. Der Besitzer liegt in owner[0] (steht für Civs SettlementRegistry).</summary>
        private sealed class CaptureWorld
        {
            public CombatWorld W;
            public bool[] War = new bool[1];
            public int[] Owner = { 2 };
            public readonly List<(long tick, int sid, int civ)> Captures = new List<(long, int, int)>();
            public SettlementCaptureSystem Capture => W.Ctx.Get<SettlementCaptureSystem>();
        }

        private static CaptureWorld Make(int attackers, int defenders, ulong seed = 5)
        {
            var c = new CaptureWorld();
            c.W = new CombatWorld(seed, extraSystems: r => r.Register(new StraightLineTestMover()));
            c.W.Env.AreHostile = (a, b) => c.War[0] && a != b;
            var env = c.W.Ctx.Get<ArmyEnvironment>();
            env.Recruit = (civ, role, n) => 0;
            env.Settlements = () => new[] { new ArmyObjective(Sx, Sz, 10f, c.Owner[0], SettlementId) };
            env.Objectives = civ => civ != c.Owner[0]
                ? new[] { new ArmyObjective(Sx, Sz, 10f, c.Owner[0], SettlementId) }
                : Array.Empty<ArmyObjective>();
            env.HomeOf = civ => civ == c.Owner[0] ? (true, Sx, Sz) : (true, 0f, 0f);
            env.CaptureSettlement = (sid, civ) => { c.Captures.Add((c.W.Ctx.Clock.Tick, sid, civ)); c.Owner[0] = civ; return true; };
            for (int i = 0; i < attackers; i++) c.W.Ctx.Units.Spawn(c.W.Rifleman, 1, i * 3f, 0f);
            for (int i = 0; i < defenders; i++) c.W.Ctx.Units.Spawn(c.W.Rifleman, 2, Sx + i * 3f, Sz);
            return c;
        }

        [Test]
        public void AttackersHoldUndefendedSettlementAndTakeItOver()
        {
            var c = Make(attackers: 4, defenders: 0);
            c.War[0] = true;
            long firstProgress = -1;
            for (int t = 0; t < 4000 && c.Captures.Count == 0; t++)
            {
                c.W.Runner.StepOnce();
                if (firstProgress < 0 && c.Capture.TryGetProgress(SettlementId, out int who, out _)) { firstProgress = c.W.Ctx.Clock.Tick; Assert.That(who, Is.EqualTo(1)); }
            }
            Assert.That(c.Captures.Count, Is.EqualTo(1), "Siedlung wurde übertragen");
            Assert.That(c.Captures[0].sid, Is.EqualTo(SettlementId));
            Assert.That(c.Captures[0].civ, Is.EqualTo(1));
            Assert.That(c.Captures[0].tick - firstProgress, Is.GreaterThanOrEqualTo(280), "erst nach ~30 s Halten (erste Sichtung zählt schon 1 s)");
            c.W.Ticks(300);
            Assert.That(c.Captures.Count, Is.EqualTo(1), "eigene Siedlung wird nicht erneut übertragen");
        }

        [Test]
        public void NoCaptureInPeace()
        {
            var c = Make(attackers: 4, defenders: 0);
            // Einheiten direkt in den Ortskern, ohne Krieg
            var env = c.W.Ctx.Get<ArmyEnvironment>();
            env.Objectives = civ => Array.Empty<ArmyObjective>();
            env.HomeOf = civ => (true, Sx, Sz);   // Verteidigungsposten im Ortskern → Einheiten bleiben dort
            var ids = new List<EntityId>();
            c.W.Ctx.Units.CopyAliveIds(ids);
            foreach (var id in ids) c.W.Ctx.Units.SetPosition(id, Sx, Sz, 0f, 0f);
            c.W.Ticks(600);
            Assert.That(c.Captures, Is.Empty);
            Assert.That(c.Capture.TryGetProgress(SettlementId, out _, out _), Is.False);
        }

        [Test]
        public void LivingDefenderBlocksCaptureUntilDead()
        {
            // Angreifer und ein Verteidiger stehen im Ortskern. HP werden jeden Tick aufgefüllt, damit der Verteidiger
            // so lange lebt, wie der Test es will (sonst endet das Gefecht schon vor dem Ortskern).
            var c = Make(attackers: 6, defenders: 1);
            var env = c.W.Ctx.Get<ArmyEnvironment>();
            env.HomeOf = civ => (true, Sx, Sz);
            var ids = new List<EntityId>();
            c.W.Ctx.Units.CopyAliveIds(ids);
            EntityId defender = default;
            foreach (var id in ids)
            {
                c.W.Ctx.Units.SetPosition(id, Sx, Sz, 0f, 0f);
                if (c.W.Ctx.Units.Get(id).CivId == 2) defender = id;
            }
            Assert.That(defender.IsValid, Is.True);
            c.War[0] = true;

            void StepHealed()
            {
                c.W.Ctx.Units.CopyAliveIds(ids);
                foreach (var id in ids) c.W.Ctx.Units.SetHp(id, 100f);
                c.W.Runner.StepOnce();
            }
            for (int t = 0; t < 900; t++)
            {
                StepHealed();
                Assert.That(c.Capture.TryGetProgress(SettlementId, out _, out _), Is.False, "Verteidiger lebt → kein Fortschritt");
            }
            Assert.That(c.Captures, Is.Empty);
            Assert.That(c.W.Ctx.Units.Exists(defender), Is.True);

            c.W.Ctx.Get<ICombatService>().ApplyDamage(defender, 1e6f, DamageType.Explosive, EntityId.None);
            Assert.That(c.W.Ctx.Units.Exists(defender), Is.False);
            long deathTick = c.W.Ctx.Clock.Tick;
            for (int t = 0; t < 600 && c.Captures.Count == 0; t++) StepHealed();
            Assert.That(c.Captures.Count, Is.EqualTo(1), "nach dem Tod des Verteidigers wird übertragen");
            Assert.That(c.Captures[0].civ, Is.EqualTo(1));
            Assert.That(c.Captures[0].tick - deathTick, Is.GreaterThanOrEqualTo(280));
        }

        [Test]
        public void SaveLoadMidCaptureContinuesIdentically()
        {
            string Fingerprint(CaptureWorld c)
            {
                var ids = new List<EntityId>();
                c.W.Ctx.Units.CopyAliveIds(ids);
                var sb = new System.Text.StringBuilder($"rnd {c.W.Ctx.Random.State} owner {c.Owner[0]}\n");
                foreach (var cap in c.Captures) sb.Append($"cap {cap.tick} {cap.sid} {cap.civ}\n");
                foreach (var id in ids) { var u = c.W.Ctx.Units.Get(id); sb.Append($"{id} {u.X:R},{u.Z:R} {u.Hp:R} {u.State}\n"); }
                return sb.ToString();
            }

            // Zeitpunkt mitten in der Besetzung suchen
            var probe = Make(4, 0); probe.War[0] = true;
            long mid = -1;
            for (int t = 0; t < 4000 && mid < 0; t++)
            {
                probe.W.Runner.StepOnce();
                if (probe.Capture.TryGetProgress(SettlementId, out _, out float f) && f >= 0.4f) mid = t + 1;
            }
            Assert.That(mid, Is.GreaterThan(0), "Besetzung beginnt");
            const int after = 1000;

            var reference = Make(4, 0); reference.War[0] = true;
            for (long t = 0; t < mid + after; t++) reference.W.Runner.StepOnce();
            Assert.That(reference.Captures.Count, Is.EqualTo(1), "Referenzlauf überträgt die Siedlung");

            var a = Make(4, 0); a.War[0] = true;
            for (long t = 0; t < mid; t++) a.W.Runner.StepOnce();
            Assert.That(a.Capture.TryGetProgress(SettlementId, out _, out _), Is.True, "Speichern mitten in der Besetzung");

            var b = Make(4, 0); b.War[0] = true;
            var unitState = ((ISaveable)a.W.Ctx.Units).CaptureState();
            Assert.That(((ISaveable)b.W.Ctx.Units).TryRestoreState(unitState, out _), Is.True);
            foreach (var s in a.W.Ctx.Saveables.InLoadOrder())
            {
                if (s.SaveKey == "units") continue;
                Assert.That(b.W.Ctx.Saveables.TryGet(s.SaveKey, out var target), Is.True, s.SaveKey);
                Assert.That(target.TryRestoreState(s.CaptureState(), out var err), Is.True, err);
            }
            b.Owner[0] = a.Owner[0];
            b.W.Ctx.Random.State = a.W.Ctx.Random.State;
            b.W.Ctx.Clock.Restore(a.W.Ctx.Clock.Tick);
            for (int t = 0; t < after; t++) b.W.Runner.StepOnce();
            Assert.That(Fingerprint(b), Is.EqualTo(Fingerprint(reference)));
        }

        [Test]
        public void RestoreRejectsBrokenRecords()
        {
            var c = Make(0, 0);
            var bad = new CaptureSaveState { Records = new[] { new CaptureRecord { SettlementId = 0, AttackerCiv = 1, HeldTicks = 10 } } };
            Assert.That(c.Capture.TryRestoreState(bad, out var err), Is.False);
            Assert.That(err, Does.Contain("ungültig"));
            Assert.That(c.Capture.TryRestoreState(new CaptureSaveState(), out _), Is.False);
        }
    }
}
