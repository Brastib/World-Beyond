using System;
using System.Collections.Generic;
using NUnit.Framework;
using WorldBeyond.Core;
using WorldBeyond.Simulation;
using WorldBeyond.Simulation.Military;
using WorldBeyond.Simulation.Units;
using WorldBeyond.Simulation.World;
using WorldBeyond.Simulation.World.Navigation;

namespace WorldBeyond.Tests.EditMode.Combat
{
    /// <summary>Nur für Tests: wie StraightLineTestMover, aber Ziele mit X jenseits von Limit sind unerreichbar (wie PathStatus.NotFound).</summary>
    internal sealed class BlockedZoneTestMover : ISimulationSystem
    {
        public string Name => "TestMoverBlocked";
        public int Order => SystemOrder.Movement;
        public int TickInterval => 1;
        public float Limit = 300f;
        public int Failures;
        private readonly StraightLineTestMover _inner = new StraightLineTestMover();
        private readonly List<EntityId> _ids = new List<EntityId>();

        public void Tick(SimContext ctx)
        {
            ctx.Units.CopyAliveIds(_ids);
            foreach (var id in _ids)
            {
                var u = ctx.Units.Get(id);
                if (!u.HasMoveTarget || u.MoveTargetX <= Limit) continue;
                ctx.Units.ClearMoveTarget(id);
                ctx.Events.Publish(new UnitMoveFailedEvent { Unit = id, TargetX = u.MoveTargetX, TargetZ = u.MoveTargetZ, Reason = PathStatus.NotFound });
                Failures++;
            }
            _inner.Tick(ctx);
        }
    }

    public class ArmyRobustnessTests
    {
        private static CombatWorld Make(out bool[] war, out BlockedZoneTestMover mover, ulong seed = 3)
        {
            var w0 = new bool[1];
            var m = new BlockedZoneTestMover();
            var w = new CombatWorld(seed, extraSystems: r => r.Register(m));
            w.Env.AreHostile = (a, b) => w0[0] && a != b;
            war = w0; mover = m;
            return w;
        }

        // Civ 2 hat ein unerreichbares Ziel (X 400) mit höherem Wert und ein erreichbares (X 0, Z 250).
        private static void UnreachableAndReachableObjectives(CombatWorld w)
        {
            var env = w.Ctx.Get<ArmyEnvironment>();
            env.Recruit = (civ, role, n) => 0;
            env.Objectives = civ => civ == 1
                ? new[] { new ArmyObjective(400f, 0f, 20f, ownerCiv: 2, settlementId: 7), new ArmyObjective(0f, 250f, 10f, ownerCiv: 2, settlementId: 8) }
                : Array.Empty<ArmyObjective>();
            for (int i = 0; i < 4; i++) w.Ctx.Units.Spawn(w.Rifleman, 1, 0f, i * 3f);
            w.Ctx.Units.Spawn(w.Civilian, 2, 0f, 900f);   // Feind vorhanden, außer Sicht
        }

        private static void OnlyUnreachableObjective(CombatWorld w)
        {
            var env = w.Ctx.Get<ArmyEnvironment>();
            env.Recruit = (civ, role, n) => 0;
            env.Objectives = civ => civ == 1 ? new[] { new ArmyObjective(400f, 0f, 20f, 2, 7) } : Array.Empty<ArmyObjective>();
            for (int i = 0; i < 4; i++) w.Ctx.Units.Spawn(w.Rifleman, 1, 0f, i * 3f);
        }

        [Test]
        public void FailedPathsBackOffAndArmySwitchesToReachableObjective()
        {
            var w = Make(out var war, out var mover);
            UnreachableAndReachableObjectives(w);
            war[0] = true;
            w.Ticks(3000);   // 5 Spielminuten
            Assert.That(mover.Failures, Is.LessThan(30), "kein ständiges Neu-Befehlen nach gescheitertem Weg");
            var view = w.Ctx.Get<IArmyQuery>().GetArmies(1)[0];
            Assert.That(view.Order.Kind, Is.EqualTo(ArmyOrderKind.AttackSettlement));
            Assert.That(view.Order.TargetRef, Is.EqualTo(8), "unerreichbares Ziel gemieden, erreichbares gewählt");
        }

        [Test]
        public void BlockedTargetIsRetriedAfterCooldown()
        {
            // Nur das unerreichbare Ziel: die KI versucht es nach der Sperre erneut, aber mit wachsenden Pausen.
            var w = Make(out var war, out var mover);
            OnlyUnreachableObjective(w);
            war[0] = true;
            w.Ticks(6000);   // 10 Spielminuten
            Assert.That(mover.Failures, Is.GreaterThan(4), "Ziel wird erneut versucht");
            Assert.That(mover.Failures, Is.LessThan(40), "aber mit wachsenden Pausen");
        }

        [Test]
        public void RecruitingCountsQueuedOrders()
        {
            var w = Make(out var war, out _);
            var env = w.Ctx.Get<ArmyEnvironment>();
            int queued = 0;
            env.Recruit = (civ, role, n) => { if (civ == 1) queued += n; return n; };   // Aufträge bleiben in der Warteschlange
            env.PendingRecruits = civ => civ == 1 ? queued : 0;
            w.Ctx.Units.Spawn(w.Rifleman, 1, 0f, 0f);
            w.Ctx.Units.Spawn(w.Rifleman, 2, 1000f, 0f);
            war[0] = true;
            w.Ticks(3000);
            // Feind hat 1 Schützen → Wunsch max(1,5 × 1, MinAttackUnits 4) = 4 Schützen, 1 vorhanden → 3, nur einmal angefordert
            Assert.That(queued, Is.EqualTo(3), "nur das Fehlende einmal anfordern");
        }

        [TestCase(1f, 3)]
        [TestCase(0f, 0)]
        public void PeaceRecruitsBaseArmyOnlyIfCivIsWilling(float willingness, int expected)
        {
            var w = Make(out _, out _);
            var env = w.Ctx.Get<ArmyEnvironment>();
            int queued = 0;
            env.Recruit = (civ, role, n) => { if (civ == 1) queued += n; return n; };
            env.PendingRecruits = civ => civ == 1 ? queued : 0;
            env.RecruitWillingness = civ => willingness;
            w.Ctx.Units.Spawn(w.Rifleman, 1, 0f, 0f);
            w.Ticks(1200);
            Assert.That(queued, Is.EqualTo(expected), "Grundarmee MinAttackUnits (4) − 1 vorhanden, ohne Krieg");
        }

        [Test]
        public void CivWithoutAnySoldierStillRecruits()
        {
            // Henne-Ei: eine Civ ohne Militär kennt die Armee-KI nur über ihre Siedlungen (ArmyEnvironment.Settlements).
            var w = Make(out _, out _);
            var env = w.Ctx.Get<ArmyEnvironment>();
            int queued = 0;
            env.Recruit = (civ, role, n) => { if (civ == 3) queued += n; return n; };
            env.PendingRecruits = civ => civ == 3 ? queued : 0;
            env.Settlements = () => new[] { new ArmyObjective(0f, 0f, 1f, ownerCiv: 3, settlementId: 1) };
            w.Ticks(300);
            Assert.That(queued, Is.EqualTo(env.MinAttackUnits));
        }

        [Test]
        public void RecruitNeedFollowsThreatAndWillingness()
        {
            var w = Make(out var war, out _);
            var env = w.Ctx.Get<ArmyEnvironment>();
            w.Ctx.Units.Spawn(w.Rifleman, 1, 0f, 0f);
            for (int i = 0; i < 10; i++) w.Ctx.Units.Spawn(w.Rifleman, 2, 1000f, i * 3f);
            war[0] = true;
            w.Ticks(10);
            var ai = w.Ctx.Get<ArmyAISystem>();
            Assert.That(ai.RecruitNeed(1), Is.EqualTo(14), "1,5 × 10 feindliche − 1 eigene");
            Assert.That(ai.RecruitNeed(2), Is.EqualTo(0), "überlegene Seite rüstet nicht weiter auf");
            env.PendingRecruits = civ => civ == 1 ? 4 : 0;
            Assert.That(ai.RecruitNeed(1), Is.EqualTo(10), "eingereihte Aufträge zählen mit");
            env.RecruitWillingness = civ => 0.5f;
            Assert.That(ai.RecruitNeed(1), Is.EqualTo(5), "Civ wägt Kosten ab: halbe Bereitschaft");
            env.RecruitWillingness = civ => 0f;
            Assert.That(ai.RecruitNeed(1), Is.EqualTo(0));
        }

        [Test]
        public void EnemyGarrisonAtItsOwnHomeIsNoThreatEvenIfClose()
        {
            // Kleine Karte: Heimatorte 250 m auseinander, beide Garnisonen stehen zu Hause → trotzdem wird angegriffen.
            var w = Make(out var war, out _);
            var env = w.Ctx.Get<ArmyEnvironment>();
            env.Recruit = (civ, role, n) => 0;
            env.HomeOf = civ => civ == 1 ? (true, 0f, 0f) : (true, 0f, 250f);
            env.Objectives = civ => civ == 1 ? new[] { new ArmyObjective(0f, 250f, 10f, 2, 8) } : Array.Empty<ArmyObjective>();
            for (int i = 0; i < 6; i++) w.Ctx.Units.Spawn(w.Rifleman, 1, i * 3f, 0f);
            for (int i = 0; i < 2; i++) { var d = w.Ctx.Units.Spawn(w.Rifleman, 2, i * 3f, 250f); w.Ctx.Combat_SetHold(d); }
            war[0] = true;
            w.Ticks(60);
            var view = w.Ctx.Get<IArmyQuery>().GetArmies(1)[0];
            Assert.That(view.Order.Kind, Is.EqualTo(ArmyOrderKind.AttackSettlement), "fremde Garnison daheim bedroht nicht");
        }

        [Test]
        public void DefenderAimsForParityAttackerForSuperiority()
        {
            var w = Make(out var war, out _);
            var env = w.Ctx.Get<ArmyEnvironment>();
            env.IsAttackerAgainst = (civ, enemy) => civ == 2;   // Civ 2 hat den Krieg erklärt
            w.Ctx.Units.Spawn(w.Rifleman, 1, 0f, 0f);
            for (int i = 0; i < 10; i++) w.Ctx.Units.Spawn(w.Rifleman, 2, 1000f, i * 3f);
            war[0] = true;
            w.Ticks(10);
            var ai = w.Ctx.Get<ArmyAISystem>();
            Assert.That(ai.RecruitNeed(1), Is.EqualTo(9), "Verteidiger: 1,0 × 10 − 1");
            Assert.That(ai.RecruitNeed(2), Is.EqualTo(0), "Angreifer ist schon überlegen (10 ≥ 1,5 × 1)");
        }

        [Test]
        public void ThreatenedHomeIsDefendedInsteadOfAttacking()
        {
            var w = Make(out var war, out _);
            var env = w.Ctx.Get<ArmyEnvironment>();
            env.Recruit = (civ, role, n) => 0;
            env.HomeOf = civ => civ == 1 ? (true, 0f, 0f) : (true, 2000f, 0f);
            env.Objectives = civ => civ == 1 ? new[] { new ArmyObjective(0f, 290f, 10f, 2, 8) } : Array.Empty<ArmyObjective>();
            for (int i = 0; i < 4; i++) w.Ctx.Units.Spawn(w.Rifleman, 1, 0f, -250f + i * 3f);   // Armee unterwegs, 250 m von der Heimat
            var raider = w.Ctx.Units.Spawn(w.Rifleman, 2, 200f, 150f);                       // Feind 250 m vor der Heimat
            w.Ctx.Combat_SetHold(raider);
            war[0] = true;
            w.Ticks(60);
            var view = w.Ctx.Get<IArmyQuery>().GetArmies(1)[0];
            Assert.That(view.Order.Kind, Is.EqualTo(ArmyOrderKind.Defend), "Heimat bedroht → verteidigen");
            Assert.That(view.Order.X, Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void MilitaryQueryCountsAliveAndQueuedPerCiv()
        {
            var w = Make(out _, out _);
            var env = w.Ctx.Get<ArmyEnvironment>();
            for (int i = 0; i < 3; i++) w.Ctx.Units.Spawn(w.Rifleman, 1, 0f, i * 3f);
            w.Ctx.Units.Spawn(w.Civilian, 1, 10f, 0f);
            w.Ctx.Units.Spawn(w.Rifleman, 2, 500f, 0f);
            var q = w.Ctx.Get<IMilitaryQuery>();
            Assert.That(q.GetQueuedMilitaryCount(1), Is.EqualTo(0), "ohne Anschluss 0");
            env.PendingRecruits = civ => civ == 1 ? 5 : 0;
            Assert.That(q.GetAliveMilitaryCount(1), Is.EqualTo(3), "Zivilist zählt nicht");
            Assert.That(q.GetQueuedMilitaryCount(1), Is.EqualTo(5));
            Assert.That(q.GetMilitaryCount(1), Is.EqualTo(8));
            Assert.That(q.GetMilitaryCount(2), Is.EqualTo(1));
            Assert.That(q.GetMilitaryCount(9), Is.EqualTo(0));
        }

        // full: nur unerreichbares Ziel, ganze Armee scheitert → Sperre des Ziels läuft beim Speichern.
        // partial: 9 Einheiten, Ziel an der Grenze → nur die rechte Spalte scheitert → Einzel-Abklingzeit läuft beim Speichern.
        [TestCase(false)]
        [TestCase(true)]
        public void SaveLoadDuringBackoffContinuesIdentically(bool partial)
        {
            string Fingerprint(CombatWorld w, BlockedZoneTestMover m)
            {
                var ids = new List<EntityId>();
                w.Ctx.Units.CopyAliveIds(ids);
                var sb = new System.Text.StringBuilder($"rnd {w.Ctx.Random.State} fails {m.Failures}\n");
                foreach (var v in w.Ctx.Get<IArmyQuery>().GetArmies(1)) sb.Append($"army {v.Id} {v.Order.Kind} {v.Order.TargetRef}\n");
                foreach (var id in ids) { var u = w.Ctx.Units.Get(id); sb.Append($"{id} {u.X:R},{u.Z:R} {u.Hp:R} {u.State}\n"); }
                return sb.ToString();
            }
            CombatWorld Setup(out BlockedZoneTestMover m)
            {
                var w = Make(out var war, out m);
                if (partial)
                {
                    var env = w.Ctx.Get<ArmyEnvironment>();
                    env.Recruit = (civ, role, n) => 0;
                    env.Objectives = civ => civ == 1 ? new[] { new ArmyObjective(299f, 0f, 20f, 2, 7) } : Array.Empty<ArmyObjective>();
                    for (int i = 0; i < 9; i++) w.Ctx.Units.Spawn(w.Rifleman, 1, 0f, i * 3f);
                }
                else OnlyUnreachableObjective(w);
                war[0] = true;
                return w;
            }
            bool Pending(ArmyAISaveState st, long tick)
            {
                if (partial) { foreach (var f in st.Failed) if (f.UntilTick > tick + 20) return st.Blocked.Length == 0; return false; }
                foreach (var b in st.Blocked) if (b.Count >= 2 && b.UntilTick > tick + 20) return true;
                return false;
            }
            var probe = Setup(out _);
            int before = -1;
            for (int t = 1; t <= 6000 && before < 0; t++)
            {
                probe.Runner.StepOnce();
                if (Pending((ArmyAISaveState)probe.Ctx.Get<ArmyAISystem>().CaptureState(), probe.Ctx.Clock.Tick)) before = t;
            }
            Assert.That(before, Is.GreaterThan(0), "gesuchter Zustand (Sperre bzw. Einzel-Abklingzeit) tritt auf");
            const int after = 3000;
            var reference = Setup(out var mr);
            for (int t = 0; t < before + after; t++) reference.Runner.StepOnce();

            var a = Setup(out var ma);
            for (int t = 0; t < before; t++) a.Runner.StepOnce();

            var b = Setup(out var mb);
            Assert.That(((ISaveable)b.Ctx.Units).TryRestoreState(((ISaveable)a.Ctx.Units).CaptureState(), out _), Is.True);
            foreach (var s in a.Ctx.Saveables.InLoadOrder())
            {
                if (s.SaveKey == "units") continue;
                Assert.That(b.Ctx.Saveables.TryGet(s.SaveKey, out var target), Is.True, s.SaveKey);
                Assert.That(target.TryRestoreState(s.CaptureState(), out var err), Is.True, err);
            }
            b.Ctx.Random.State = a.Ctx.Random.State;
            b.Ctx.Clock.Restore(a.Ctx.Clock.Tick);
            mb.Failures = ma.Failures;
            for (int t = 0; t < after; t++) b.Runner.StepOnce();
            Assert.That(Fingerprint(b, mb), Is.EqualTo(Fingerprint(reference, mr)));
        }

        [Test]
        public void OldSaveWithoutFailedAndBlockedLoads()
        {
            var w = Make(out _, out _);
            var ai = w.Ctx.Get<ArmyAISystem>();
            var old = new ArmyAISaveState { Recruit = Array.Empty<ArmyAIRecruitRecord>(), Issued = Array.Empty<ArmyAIIssuedRecord>() };
            Assert.That(ai.TryRestoreState(old, out var err), Is.True, err);
        }
    }
}
