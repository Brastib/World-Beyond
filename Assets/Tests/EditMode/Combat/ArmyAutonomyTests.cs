using System;
using System.Collections.Generic;
using NUnit.Framework;
using WorldBeyond.Core;
using WorldBeyond.Simulation;
using WorldBeyond.Simulation.Combat;
using WorldBeyond.Simulation.Military;
using WorldBeyond.Simulation.Units;

namespace WorldBeyond.Tests.EditMode.Combat
{
    /// <summary>
    /// Nur für Tests: gerade Linie zum Bewegungsziel mit MaxSpeed. Ersetzt NICHT das MovementSystem von World;
    /// es gibt die Bewegung nur, damit die Armee-KI ohne World-Branch prüfbar ist.
    /// </summary>
    internal sealed class StraightLineTestMover : ISimulationSystem
    {
        public string Name => "TestMover";
        public int Order => SystemOrder.Movement;
        public int TickInterval => 1;
        private readonly List<EntityId> _ids = new List<EntityId>();

        public void Tick(SimContext ctx)
        {
            float dt = ctx.Clock.DeltaSeconds;
            ctx.Units.CopyAliveIds(_ids);
            foreach (var id in _ids)
            {
                var u = ctx.Units.Get(id);
                if (!u.HasMoveTarget) continue;
                float dx = u.MoveTargetX - u.X, dz = u.MoveTargetZ - u.Z;
                float d = MathF.Sqrt(dx * dx + dz * dz);
                float step = ctx.UnitTypes.Get(u.Type).MaxSpeed * dt;
                if (d <= step) { ctx.Units.SetPosition(id, u.MoveTargetX, u.MoveTargetZ, u.Heading, 0f); ctx.Units.ClearMoveTarget(id); continue; }
                ctx.Units.SetPosition(id, u.X + dx / d * step, u.Z + dz / d * step, u.Heading, step / dt);
            }
        }
    }

    public class ArmyAutonomyTests
    {
        private static CombatWorld MakeWorld(out bool[] atWar, ulong seed = 11)
        {
            var war = new bool[1];
            var w = new CombatWorld(seed, extraSystems: r => r.Register(new StraightLineTestMover()));
            w.Env.AreHostile = (a, b) => war[0] && a != b;
            atWar = war;
            return w;
        }

        [Test]
        public void SandboxWarWithoutAnyPlayerCommand()
        {
            var w = MakeWorld(out var atWar);
            var armyEvents = new List<ArmyCreatedEvent>();
            w.Ctx.Events.Subscribe<ArmyCreatedEvent>(e => armyEvents.Add(e));
            for (int i = 0; i < 6; i++)
            {
                w.Ctx.Units.Spawn(w.Rifleman, 1, 0f, i * 3f);
                w.Ctx.Units.Spawn(w.Rifleman, 2, 600f, i * 3f);   // weit außerhalb Sicht (200 m)
            }

            w.Ticks(300);   // 30 s Frieden
            Assert.That(armyEvents.Count, Is.EqualTo(2), "jede Civ bildet selbst eine Armee");
            Assert.That(w.Shots, Is.Empty, "kein Kampf im Frieden");

            atWar[0] = true;   // entspricht WarDeclaredEvent / IWarQuery.AreAtWar (Phase 4)
            w.Ticks(10 * 600);   // 10 Spielminuten, ohne einen einzigen Befehl

            Assert.That(w.Commands.PendingCount, Is.EqualTo(0));
            Assert.That(w.Shots, Is.Not.Empty, "Armeen sind marschiert und haben gekämpft");
            Assert.That(w.Deaths.Count, Is.GreaterThanOrEqualTo(6));
            Assert.That(w.Alive(1) == 0 || w.Alive(2) == 0, Is.True, $"eine Seite ist vernichtet: Civ1 {w.Alive(1)}, Civ2 {w.Alive(2)}");
        }

        [Test]
        public void ArmiesMarchTowardEnemyAfterWarStarts()
        {
            var w = MakeWorld(out var atWar);
            var a = w.Ctx.Units.Spawn(w.Rifleman, 1, 0f, 0f);
            w.Ctx.Units.Spawn(w.Rifleman, 2, 800f, 0f);
            w.Ticks(20);
            Assert.That(w.Ctx.Units.Get(a).X, Is.LessThan(5f), "im Frieden bleibt die Armee daheim");
            atWar[0] = true;
            w.Ticks(10 * 60);
            Assert.That(w.Ctx.Units.Get(a).X, Is.GreaterThan(50f), "nach Kriegsbeginn marschiert sie los");
        }

        [Test]
        public void OutnumberedArmyRetreats()
        {
            var w = MakeWorld(out var atWar);
            var weak = w.Ctx.Units.Spawn(w.Rifleman, 1, 0f, 0f);
            for (int i = 0; i < 6; i++) w.Ctx.Units.Spawn(w.Rifleman, 2, 500f, i * 3f);
            atWar[0] = true;
            w.Ticks(120);
            var q = w.Ctx.Get<IArmyQuery>();
            Assert.That(q.TryGetArmyOf(weak, out int armyId), Is.True);
            var view = q.GetArmies(1)[0];
            Assert.That(view.Order.Kind, Is.EqualTo(ArmyOrderKind.Retreat).Or.EqualTo(ArmyOrderKind.Defend), "1 gegen 6 greift nicht an");
        }

        [Test]
        public void PlayerOrderOverridesAIForAWhile()
        {
            var w = MakeWorld(out var atWar);
            var u = w.Ctx.Units.Spawn(w.Rifleman, 1, 0f, 0f);
            w.Ctx.Units.Spawn(w.Rifleman, 2, 800f, 0f);
            w.Ticks(20);
            var reg = w.Ctx.Get<IArmyRegistry>();
            w.Ctx.Get<IArmyQuery>().TryGetArmyOf(u, out int army);
            atWar[0] = true;
            reg.OrderMove(army, -300f, 0f);
            w.Ticks(10 * 30);
            Assert.That(w.Ctx.Units.Get(u).X, Is.LessThan(-20f), "Spielerbefehl wird befolgt");
            w.Ticks(10 * 120);
            Assert.That(w.Ctx.Get<IArmyQuery>().GetArmies(1)[0].UnderManualControl, Is.False, "danach übernimmt wieder die KI");
        }

        [Test]
        public void UsesWarObjectivesAndRecruitsWhenProvided()
        {
            var w = MakeWorld(out var atWar);
            var env = w.Ctx.Get<ArmyEnvironment>();
            int requested = 0;
            env.Objectives = civ => civ == 1 ? new[] { new ArmyObjective(0f, 400f, 10f, ownerCiv: 2, settlementId: 7) } : Array.Empty<ArmyObjective>();
            env.Recruit = (civ, role, n) => { requested += n; return n; };
            w.Ctx.Units.Spawn(w.Rifleman, 1, 0f, 0f);
            w.Ctx.Units.Spawn(w.Civilian, 2, 0f, 420f);
            atWar[0] = true;
            w.Ticks(120);
            var view = w.Ctx.Get<IArmyQuery>().GetArmies(1)[0];
            Assert.That(view.Order.Kind, Is.EqualTo(ArmyOrderKind.AttackSettlement));
            Assert.That(view.Order.TargetRef, Is.EqualTo(7));
            Assert.That(requested, Is.GreaterThan(0), "Armee unter Sollstärke fordert Rekruten an (IRecruitment)");
        }

        [Test]
        public void DeadUnitsLeaveArmyAndEmptyArmyIsDisbanded()
        {
            var w = MakeWorld(out _);
            var disbanded = new List<ArmyDisbandedEvent>();
            w.Ctx.Events.Subscribe<ArmyDisbandedEvent>(e => disbanded.Add(e));
            var u = w.Ctx.Units.Spawn(w.Rifleman, 1, 0f, 0f);
            w.Ticks(20);
            Assert.That(w.Ctx.Get<IArmyQuery>().GetArmies(1).Count, Is.EqualTo(1));
            w.Ctx.Get<ICombatService>().ApplyDamage(u, 1000f, DamageType.Explosive, EntityId.None);
            w.Ticks(20);
            Assert.That(w.Ctx.Get<IArmyQuery>().GetArmies(1), Is.Empty);
            Assert.That(disbanded.Count, Is.EqualTo(1));
        }

        [Test]
        public void ArmiesSaveRestoreAndReferenceCheck()
        {
            var w = MakeWorld(out var atWar);
            for (int i = 0; i < 3; i++) w.Ctx.Units.Spawn(w.Rifleman, 1, 0f, i * 3f);
            var victim = w.Ctx.Units.Spawn(w.Rifleman, 2, 700f, 0f);
            atWar[0] = true;
            w.Ticks(100);
            var reg = w.Ctx.Get<ArmyRegistry>();
            var unitsState = ((ISaveable)w.Ctx.Units).CaptureState();
            var armyState = (ArmySaveState)reg.CaptureState();
            Assert.That(armyState.Armies.Length, Is.EqualTo(2));

            var w2 = MakeWorld(out _);
            Assert.That(((ISaveable)w2.Ctx.Units).TryRestoreState(unitsState, out var e1), Is.True, e1);
            var reg2 = w2.Ctx.Get<ArmyRegistry>();
            Assert.That(reg2.TryRestoreState(armyState, out var e2), Is.True, e2);
            Assert.That(reg2.GetArmies(1)[0].Order.Kind, Is.EqualTo(reg.GetArmies(1)[0].Order.Kind));

            // Referenzprüfung: Einheit von Civ 2 fehlt → Armee leer → aufgelöst, Warnung
            w2.Ctx.Units.Despawn(victim);
            var report = new LoadReport();
            reg2.CheckReferences(w2.Ctx, report);
            Assert.That(report.HasErrors, Is.False);
            Assert.That(report.Warnings.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(reg2.GetArmies(2), Is.Empty);

            // neue Armee bekommt eine neue ID, nie eine alte
            int maxOld = 0;
            foreach (var r in armyState.Armies) maxOld = Math.Max(maxOld, r.Id);
            int fresh = reg2.CreateArmy(1, new List<EntityId>());
            Assert.That(fresh, Is.EqualTo(0), "leere Liste → keine Armee");
            var newUnit = w2.Ctx.Units.Spawn(w2.Rifleman, 1, 50f, 50f);
            Assert.That(reg2.CreateArmy(1, new[] { newUnit }), Is.GreaterThan(maxOld));
        }

        [Test]
        public void RestoreRejectsBrokenArmyIds()
        {
            var w = MakeWorld(out _);
            var reg = w.Ctx.Get<ArmyRegistry>();
            var bad = new ArmySaveState { LastIssuedId = 1, Armies = new[] { new ArmyRecord { Id = 5, UnitIndices = new int[0], UnitGenerations = new int[0] } } };
            Assert.That(reg.TryRestoreState(bad, out var err), Is.False);
            Assert.That(err, Does.Contain("ungültig"));
        }
        [Test]
        public void SaveLoadMidWarContinuesIdentically()
        {
            // K7: Lauf nach Speichern/Laden mitten im Krieg muss dem ununterbrochenen Lauf exakt gleichen.
            string Fingerprint(CombatWorld w)
            {
                var ids = new List<EntityId>();
                w.Ctx.Units.CopyAliveIds(ids);
                var sb = new System.Text.StringBuilder($"rnd {w.Ctx.Random.State}\n");
                foreach (var id in ids) { var u = w.Ctx.Units.Get(id); sb.Append($"{id} {u.X:R},{u.Z:R} {u.Hp:R} {u.State}\n"); }
                return sb.ToString();
            }
            CombatWorld Setup(out bool[] war)
            {
                var w = MakeWorld(out war);
                w.Ctx.Get<ArmyEnvironment>().Recruit = (civ, role, n) => 0;
                for (int i = 0; i < 5; i++) { w.Ctx.Units.Spawn(w.Rifleman, 1, 0f, i * 3f); w.Ctx.Units.Spawn(w.Rifleman, 2, 500f, i * 3f); }
                return w;
            }
            const int before = 900, after = 1500;
            var reference = Setup(out var warRef);
            for (int t = 0; t < before + after; t++) { if (t == 300) warRef[0] = true; reference.Runner.StepOnce(); }

            var a = Setup(out var warA);
            for (int t = 0; t < before; t++) { if (t == 300) warA[0] = true; a.Runner.StepOnce(); }
            var b = Setup(out var warB);
            warB[0] = true;
            Assert.That(((ISaveable)a.Ctx.Units).CaptureState() is var us && ((ISaveable)b.Ctx.Units).TryRestoreState(us, out _), Is.True);
            foreach (var s in a.Ctx.Saveables.InLoadOrder())
            {
                if (s.SaveKey == "units") continue;
                Assert.That(b.Ctx.Saveables.TryGet(s.SaveKey, out var target), Is.True, s.SaveKey);
                Assert.That(target.TryRestoreState(s.CaptureState(), out var err), Is.True, err);
            }
            b.Ctx.Random.State = a.Ctx.Random.State;
            b.Ctx.Clock.Restore(a.Ctx.Clock.Tick);
            for (int t = 0; t < after; t++) b.Runner.StepOnce();
            Assert.That(Fingerprint(b), Is.EqualTo(Fingerprint(reference)));
        }
    }
}
