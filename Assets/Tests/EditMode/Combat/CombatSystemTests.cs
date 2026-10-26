using System.Collections.Generic;
using NUnit.Framework;
using WorldBeyond.Application;
using WorldBeyond.Core;
using WorldBeyond.Simulation;
using WorldBeyond.Simulation.Combat;
using WorldBeyond.Simulation.Commands;
using WorldBeyond.Simulation.Military;
using WorldBeyond.Simulation.Save;
using WorldBeyond.Simulation.Units;

namespace WorldBeyond.Tests.EditMode.Combat
{
    /// <summary>Simulation wie in GameComposition, plus Military. Ohne World/War: Feindschaft und Gelände über CombatEnvironment.</summary>
    internal sealed class CombatWorld
    {
        public readonly SimContext Ctx;
        public readonly SimulationRunner Runner;
        public readonly CombatSystem Combat;
        public readonly CombatEnvironment Env = new CombatEnvironment();
        public readonly CommandQueue Commands = new CommandQueue();
        public readonly List<UnitDiedEvent> Deaths = new List<UnitDiedEvent>();
        public readonly List<WeaponFiredEvent> Shots = new List<WeaponFiredEvent>();
        public UnitTypeId Rifleman;
        public UnitTypeId Civilian;

        public CombatWorld(ulong seed = 7, System.Action<SimulationRunner> extraSystems = null)
        {
            var events = new SimEventBus();
            var content = new ContentRegistry();
            var types = new UnitTypeRegistry(content);
            var units = new UnitStore(types, events);
            var saveables = new SaveableRegistry();
            Ctx = new SimContext(new SimClock(), new SimRandom(seed), events, Commands, content, types, units, saveables);
            Runner = new SimulationRunner(Ctx);
            Runner.Register(new CommandProcessingSystem(Commands));
            Civilian = types.Register(new UnitTypeData { Key = "unit.settler", Role = UnitRole.Settler, MaxHp = 50f });
            Combat = MilitaryModule.Register(Ctx, Runner.Register, Env);
            extraSystems?.Invoke(Runner);
            Ctx.Seal();
            types.TryGetByKey(MilitaryContent.RiflemanKey, out Rifleman);
            events.Subscribe<UnitDiedEvent>(e => Deaths.Add(e));
            events.Subscribe<WeaponFiredEvent>(e => Shots.Add(e));
            Env.AreHostile = (a, b) => a != b;   // Krieg zwischen allen; War liefert das später
        }

        public void Ticks(int n) { for (int i = 0; i < n; i++) Runner.StepOnce(); }

        public int Alive(int civ)
        {
            int c = 0;
            Ctx.Units.ForEachAlive(id => { if (Ctx.Units.Get(id).CivId == civ) c++; });
            return c;
        }
    }

    public class CombatSystemTests
    {
        [Test]
        public void TwoSquadsFightUntilOneSideIsDead()
        {
            var w = new CombatWorld();
            for (int i = 0; i < 4; i++)
            {
                w.Ctx.Units.Spawn(w.Rifleman, 1, 0f, i * 3f);
                w.Ctx.Units.Spawn(w.Rifleman, 2, 100f, i * 3f);
            }
            w.Ticks(10 * 120); // 2 Spielminuten
            Assert.That(w.Alive(1) == 0 || w.Alive(2) == 0, Is.True, $"Civ1 {w.Alive(1)}, Civ2 {w.Alive(2)}");
            Assert.That(w.Deaths.Count, Is.GreaterThanOrEqualTo(4));
            foreach (var d in w.Deaths)
            {
                Assert.That(d.CivId, Is.AnyOf(1, 2));
                Assert.That(d.KillerCivId, Is.EqualTo(3 - d.CivId), "UnitDiedEvent nennt die Zivilisation des Schützen");
                Assert.That(d.UnitType, Is.EqualTo(w.Rifleman));
            }
            Assert.That(w.Shots.Count, Is.GreaterThan(w.Deaths.Count), "es wird auch vorbeigeschossen");
        }

        [Test]
        public void NoFightWithoutHostility()
        {
            var w = new CombatWorld();
            w.Env.AreHostile = (a, b) => false;
            w.Ctx.Units.Spawn(w.Rifleman, 1, 0f, 0f);
            w.Ctx.Units.Spawn(w.Rifleman, 2, 50f, 0f);
            w.Ticks(200);
            Assert.That(w.Shots, Is.Empty);
            Assert.That(w.Deaths, Is.Empty);
        }

        [Test]
        public void SameCivNeverShootsItself()
        {
            var w = new CombatWorld();
            w.Env.AreHostile = (a, b) => true;
            w.Ctx.Units.Spawn(w.Rifleman, 1, 0f, 0f);
            w.Ctx.Units.Spawn(w.Rifleman, 1, 20f, 0f);
            w.Ticks(100);
            Assert.That(w.Shots, Is.Empty);
        }

        [Test]
        public void HillBlocksLineOfSight()
        {
            var w = new CombatWorld();
            w.Env.HeightAt = (x, z) => (x > 40f && x < 60f) ? 20f : 0f;
            var a = w.Ctx.Units.Spawn(w.Rifleman, 1, 0f, 0f);
            w.Ctx.Combat_SetHold(a);
            var b = w.Ctx.Units.Spawn(w.Rifleman, 2, 100f, 0f);
            w.Ctx.Combat_SetHold(b);
            w.Ticks(100);
            Assert.That(w.Shots, Is.Empty, "Hügel zwischen den Schützen");
            w.Env.HeightAt = (x, z) => 0f;
            w.Ticks(30);
            Assert.That(w.Shots, Is.Not.Empty, "nach Entfernen des Hügels wird gefeuert");
        }

        [Test]
        public void OutOfRangeEnemyIsChasedAndHoldPositionIsNot()
        {
            var w = new CombatWorld();
            var hunter = w.Ctx.Units.Spawn(w.Rifleman, 1, 0f, 0f);
            var holder = w.Ctx.Units.Spawn(w.Rifleman, 1, 0f, 10f);
            w.Ctx.Combat_SetHold(holder);
            w.Ctx.Units.Spawn(w.Rifleman, 2, 190f, 0f);   // in Sicht (200 m), außer Reichweite (150 m)
            w.Ticks(10);
            var h = w.Ctx.Units.Get(hunter);
            Assert.That(h.HasMoveTarget, Is.True);
            Assert.That(h.MoveTargetX, Is.EqualTo(190f).Within(5f), "Verfolgung (Kampf) oder Formation am Feind (Armee-KI)");
            Assert.That(w.Ctx.Units.Get(holder).HasMoveTarget, Is.False);
            Assert.That(w.Shots, Is.Empty);
        }

        [Test]
        public void PlayerMoveOrderIsNotOverriddenByAutoChase()
        {
            var w = new CombatWorld();
            var u = w.Ctx.Units.Spawn(w.Rifleman, 1, 0f, 0f);
            w.Ctx.Units.SetMoveTarget(u, -500f, 0f);
            w.Ctx.Units.Spawn(w.Rifleman, 2, 190f, 0f);
            w.Ticks(30);
            Assert.That(w.Ctx.Units.Get(u).MoveTargetX, Is.EqualTo(-500f));
        }

        [Test]
        public void AttackOrderPrefersOrderedTargetAndClearsWhenDead()
        {
            var w = new CombatWorld();
            var a = w.Ctx.Units.Spawn(w.Rifleman, 1, 0f, 0f);
            w.Ctx.Units.Spawn(w.Rifleman, 2, 30f, 0f);                // nah, aber nicht befohlen
            var ordered = w.Ctx.Units.Spawn(w.Civilian, 2, 0f, 100f); // weiter weg
            var cmd = new AttackUnitCommand { Target = ordered };
            cmd.Units.Add(a);
            w.Commands.Enqueue(cmd);
            w.Ticks(2);
            Assert.That(w.Combat.GetTarget(a), Is.EqualTo(ordered));
            w.Ticks(600);
            Assert.That(w.Ctx.Units.Exists(ordered), Is.False, "befohlenes Ziel wurde getötet");
            if (w.Ctx.Units.Exists(a))
                Assert.That(w.Ctx.Units.Get(a).Task.Kind, Is.EqualTo(UnitTaskKind.None), "Auftrag nach Tod des Ziels beendet");
        }

        [Test]
        public void CivilianIsKilledAndCannotShootBack()
        {
            var w = new CombatWorld();
            w.Ctx.Units.Spawn(w.Rifleman, 1, 0f, 0f);
            var civ = w.Ctx.Units.Spawn(w.Civilian, 2, 50f, 0f);
            w.Ticks(300);
            Assert.That(w.Ctx.Units.Exists(civ), Is.False);
            Assert.That(w.Shots.TrueForAll(s => s.Shooter != civ), Is.True);
        }

        [Test]
        public void DamageGoesThroughServiceWithArmorAndKillsOnce()
        {
            var w = new CombatWorld();
            var svc = w.Ctx.Get<ICombatService>();
            var a = w.Ctx.Units.Spawn(w.Rifleman, 1, 0f, 0f);
            var t = w.Ctx.Units.Spawn(w.Rifleman, 2, 5f, 0f);
            svc.ApplyDamage(t, 60f, DamageType.SmallArms, a);
            Assert.That(w.Ctx.Units.Get(t).Hp, Is.EqualTo(40f));
            svc.ApplyDamage(t, 60f, DamageType.SmallArms, a);
            svc.ApplyDamage(t, 60f, DamageType.SmallArms, a);   // schon tot: ignoriert
            w.Ctx.Events.Flush();
            Assert.That(w.Deaths.Count, Is.EqualTo(1));
            Assert.That(w.Deaths[0].Killer, Is.EqualTo(a));
            Assert.That(w.Deaths[0].KillerCivId, Is.EqualTo(1));
        }

        [Test]
        public void AreaDamageFallsOff()
        {
            var w = new CombatWorld();
            var svc = w.Ctx.Get<ICombatService>();
            var near = w.Ctx.Units.Spawn(w.Rifleman, 2, 0f, 0f);
            var mid = w.Ctx.Units.Spawn(w.Rifleman, 2, 5f, 0f);
            var far = w.Ctx.Units.Spawn(w.Rifleman, 2, 20f, 0f);
            svc.ApplyAreaDamage(0f, 0f, 10f, 80f, DamageType.Explosive, EntityId.None);
            Assert.That(w.Ctx.Units.Get(near).Hp, Is.EqualTo(20f).Within(1e-3));
            Assert.That(w.Ctx.Units.Get(mid).Hp, Is.EqualTo(60f).Within(1e-3));
            Assert.That(w.Ctx.Units.Get(far).Hp, Is.EqualTo(100f));
        }

        [Test]
        public void SameSeedSameBattle()
        {
            int Run()
            {
                var w = new CombatWorld(seed: 99);
                for (int i = 0; i < 3; i++) { w.Ctx.Units.Spawn(w.Rifleman, 1, 0f, i * 4f); w.Ctx.Units.Spawn(w.Rifleman, 2, 120f, i * 4f); }
                w.Ticks(600);
                return w.Shots.Count * 1000 + w.Alive(1) * 10 + w.Alive(2);
            }
            Assert.That(Run(), Is.EqualTo(Run()), "deterministisch über SimRandom");
        }

        [Test]
        public void SaveAndRestoreCombatState()
        {
            var w = new CombatWorld();
            var a = w.Ctx.Units.Spawn(w.Rifleman, 1, 0f, 0f);
            var b = w.Ctx.Units.Spawn(w.Rifleman, 2, 100f, 0f);
            w.Combat.SetStance(b, CombatStance.HoldPosition);
            w.Ticks(25);
            Assert.That(w.Combat.TryGetWeaponState(a, out var before), Is.True);
            Assert.That(before.InMagazine, Is.LessThan(30));

            var unitsState = ((ISaveable)w.Ctx.Units).CaptureState();
            var combatState = w.Combat.CaptureState();

            var w2 = new CombatWorld();
            Assert.That(((ISaveable)w2.Ctx.Units).TryRestoreState(unitsState, out var e1), Is.True, e1);
            Assert.That(w2.Combat.TryRestoreState(combatState, out var e2), Is.True, e2);
            Assert.That(w2.Combat.LastRestoreWarnings, Is.Empty);
            Assert.That(w2.Combat.TryGetWeaponState(a, out var after), Is.True);
            Assert.That(after.InMagazine, Is.EqualTo(before.InMagazine));
            Assert.That(w2.Combat.GetStance(b), Is.EqualTo(CombatStance.HoldPosition));
            Assert.That(w2.Combat.GetTarget(a), Is.EqualTo(w.Combat.GetTarget(a)));
        }

        [Test]
        public void RestoreRejectsUnknownWeaponAndDropsDeadTarget()
        {
            var w = new CombatWorld();
            var a = w.Ctx.Units.Spawn(w.Rifleman, 1, 0f, 0f);
            var bad = new CombatSaveState { Records = new[] { new CombatRecord { Index = a.Index, Generation = a.Generation, WeaponKey = "weapon.unknown" } } };
            Assert.That(w.Combat.TryRestoreState(bad, out var err), Is.False);
            Assert.That(err, Does.Contain("weapon.unknown"));

            var dangling = new CombatSaveState { Records = new[] { new CombatRecord {
                Index = a.Index, Generation = a.Generation, WeaponKey = MilitaryContent.RifleKey, InMagazine = 5,
                TargetIndex = 50, TargetGeneration = 3 } } };
            Assert.That(w.Combat.TryRestoreState(dangling, out _), Is.True);
            Assert.That(w.Combat.GetTarget(a), Is.EqualTo(EntityId.None));
            Assert.That(w.Combat.LastRestoreWarnings.Count, Is.EqualTo(1));
        }

        [Test]
        public void CombatStrengthForWarEvaluation()
        {
            var w = new CombatWorld();
            var q = w.Ctx.Get<IMilitaryQuery>();
            Assert.That(q.GetCombatStrength(w.Rifleman), Is.GreaterThan(0f));
            Assert.That(q.GetCombatStrength(w.Civilian), Is.EqualTo(0f));
            Assert.That(w.Ctx.UnitTypes.Get(w.Rifleman).MilitaryRole, Is.EqualTo((byte)MilitaryRole.Rifleman));
        }
    }

    internal static class CombatTestExt
    {
        public static void Combat_SetHold(this SimContext ctx, EntityId id) => ctx.Get<CombatSystem>().SetStance(id, CombatStance.HoldPosition);
    }
}
