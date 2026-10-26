using System.Collections.Generic;
using NUnit.Framework;
using WorldBeyond.Application;
using WorldBeyond.Core;
using WorldBeyond.Simulation;
using WorldBeyond.Simulation.Commands;
using WorldBeyond.Simulation.Save;
using WorldBeyond.Simulation.Units;

namespace WorldBeyond.Tests
{
    internal sealed class RecordingSystem : ISimulationSystem
    {
        private readonly List<string> _log;
        public RecordingSystem(string name, int order, int interval, List<string> log) { Name = name; Order = order; TickInterval = interval; _log = log; }
        public string Name { get; }
        public int Order { get; }
        public int TickInterval { get; }
        public void Tick(SimContext ctx) => _log.Add($"{Name}@{ctx.Clock.Tick}");
    }

    internal sealed class SpawnCommand : IWorldCommand
    {
        public string TypeKey; public int Civ; public float X, Z;
        public EntityId Spawned;
        public bool Validate(SimContext ctx, out string reason)
        {
            if (!ctx.UnitTypes.TryGetByKey(TypeKey, out _)) { reason = $"Typ {TypeKey} unbekannt"; return false; }
            reason = null; return true;
        }
        public void Execute(SimContext ctx)
        {
            ctx.UnitTypes.TryGetByKey(TypeKey, out var t);
            Spawned = ctx.Units.Spawn(t, Civ, X, Z);
        }
    }

    internal static class TestContent
    {
        public static void Setup(SimContext ctx)
        {
            ctx.UnitTypes.Register(new UnitTypeData { Key = "unit.settler", DisplayName = "Siedler", Role = UnitRole.Settler, MaxHp = 50f });
            ctx.UnitTypes.Register(new UnitTypeData { Key = "unit.rifleman", DisplayName = "Schütze", Role = UnitRole.Infantry, MaxHp = 100f });
        }
    }

    public class SimulationRunnerTests
    {
        private static (GameComposition comp, List<string> log) Make()
        {
            var comp = GameComposition.Create(new GameStartSettings { Seed = 1 });
            return (comp, new List<string>());
        }

        [Test]
        public void Systems_RunInOrder_WithIntervals()
        {
            var ctx = GameComposition.Create(null).Context;
            var runner = new SimulationRunner(ctx);
            var log = new List<string>();
            runner.Register(new RecordingSystem("B", 200, 1, log));
            runner.Register(new RecordingSystem("A", 100, 1, log));
            runner.Register(new RecordingSystem("A2", 100, 1, log));
            runner.Register(new RecordingSystem("Slow", 50, 3, log));
            for (int i = 0; i < 4; i++) runner.StepOnce();
            CollectionAssert.AreEqual(new[]
            {
                "Slow@0", "A@0", "A2@0", "B@0",
                "A@1", "A2@1", "B@1",
                "A@2", "A2@2", "B@2",
                "Slow@3", "A@3", "A2@3", "B@3",
            }, log);
            Assert.AreEqual(4, ctx.Clock.Tick);
        }

        [Test]
        public void Advance_RespectsSpeedAndPause()
        {
            var comp = GameComposition.Create(null);
            var r = comp.Runner;
            Assert.AreEqual(10, r.Advance(1.0f), "1x: 10 Ticks pro Sekunde");
            r.Speed = GameSpeed.x5;
            Assert.AreEqual(5, r.Advance(0.1f));
            r.Speed = GameSpeed.Paused;
            Assert.AreEqual(0, r.Advance(1.0f));
            r.StepOnce();
            Assert.AreEqual(16, comp.Context.Clock.Tick);
        }

        [Test]
        public void Advance_AccumulatesFractions_AndExposesAlpha()
        {
            var r = GameComposition.Create(null).Runner;
            Assert.AreEqual(0, r.Advance(0.05f));
            Assert.That(r.InterpolationAlpha, Is.EqualTo(0.5f).Within(1e-4));
            Assert.AreEqual(1, r.Advance(0.05f));
        }

        [Test]
        public void Advance_CapsTicksPerCall_AndDropsBacklog()
        {
            var comp = GameComposition.Create(null);
            var r = comp.Runner;
            r.Speed = GameSpeed.x10;
            Assert.AreEqual(r.MaxTicksPerAdvance, r.Advance(10f));
            Assert.AreEqual(1, r.DroppedBacklogCount);
            Assert.That(r.InterpolationAlpha, Is.LessThan(1f));
        }

        [Test]
        public void Register_RejectsInvalidAndDuplicate()
        {
            var r = GameComposition.Create(null).Runner;
            var log = new List<string>();
            var s = new RecordingSystem("X", 1, 1, log);
            r.Register(s);
            Assert.Throws<System.InvalidOperationException>(() => r.Register(s));
            Assert.Throws<System.ArgumentException>(() => r.Register(new RecordingSystem("Bad", 1, 0, log)));
        }

        [Test]
        public void EventsPublishedInTick_AreFlushedAtEndOfThatTick()
        {
            var comp = GameComposition.Create(null, TestContent.Setup);
            var ctx = comp.Context;
            var spawned = new List<EntityId>();
            ctx.Events.Subscribe<UnitSpawnedEvent>(e => spawned.Add(e.Unit));
            var cmd = new SpawnCommand { TypeKey = "unit.settler", Civ = 1, X = 2, Z = 3 };
            ctx.Commands.Enqueue(cmd);
            Assert.AreEqual(0, ctx.Units.Count, "Commands wirken erst an der Tick-Grenze");
            comp.Runner.StepOnce();
            Assert.AreEqual(1, ctx.Units.Count);
            CollectionAssert.AreEqual(new[] { cmd.Spawned }, spawned);
        }
    }

    public class CommandQueueTests
    {
        [Test]
        public void RejectedCommand_PublishesReason_AndDoesNotExecute()
        {
            var comp = GameComposition.Create(null, TestContent.Setup);
            var ctx = comp.Context;
            var rejected = new List<CommandRejectedEvent>();
            ctx.Events.Subscribe<CommandRejectedEvent>(rejected.Add);
            ctx.Commands.Enqueue(new SpawnCommand { TypeKey = "unit.dragon" });
            comp.Runner.StepOnce();
            Assert.AreEqual(0, ctx.Units.Count);
            Assert.AreEqual(1, rejected.Count);
            Assert.AreEqual(nameof(SpawnCommand), rejected[0].CommandName);
            StringAssert.Contains("unit.dragon", rejected[0].Reason);
        }

        [Test]
        public void CommandEnqueuedDuringExecution_RunsNextTick()
        {
            var comp = GameComposition.Create(null, TestContent.Setup);
            var ctx = comp.Context;
            var chained = new ChainCommand();
            ctx.Commands.Enqueue(chained);
            comp.Runner.StepOnce();
            Assert.AreEqual(0, ctx.Units.Count);
            Assert.AreEqual(1, ctx.Commands.PendingCount);
            comp.Runner.StepOnce();
            Assert.AreEqual(1, ctx.Units.Count);
        }

        private sealed class ChainCommand : IWorldCommand
        {
            public bool Validate(SimContext ctx, out string reason) { reason = null; return true; }
            public void Execute(SimContext ctx) => ctx.Commands.Enqueue(new SpawnCommand { TypeKey = "unit.settler" });
        }
    }

    public class SimContextTests
    {
        private interface IFoo { }
        private sealed class Foo : IFoo { }

        [Test]
        public void Services_AreSingleInstance_AndSealed()
        {
            var ctx = GameComposition.Create(null).Context;
            Assert.IsTrue(ctx.IsSealed, "nach Create versiegelt");
            Assert.Throws<System.InvalidOperationException>(() => ctx.Register<IFoo>(new Foo()));
            Assert.IsFalse(ctx.TryGet<IFoo>(out _));
            var ex = Assert.Throws<System.InvalidOperationException>(() => ctx.Get<IFoo>());
            StringAssert.Contains("IFoo", ex.Message);
        }

        [Test]
        public void DuplicateRegistration_Throws()
        {
            Assert.Throws<System.InvalidOperationException>(() =>
                GameComposition.Create(null, ctx => { ctx.Register<IFoo>(new Foo()); ctx.Register<IFoo>(new Foo()); }));
        }
    }

    public class UnitStoreTests
    {
        private static SimContext Ctx() => GameComposition.Create(null, TestContent.Setup).Context;

        [Test]
        public void Spawn_UnknownType_ReturnsNone()
        {
            var ctx = Ctx();
            Assert.AreEqual(EntityId.None, ctx.Units.Spawn(new UnitTypeId(999), 1, 0, 0));
            Assert.AreEqual(0, ctx.Units.Count);
        }

        [Test]
        public void Despawn_PublishesSingleDiedEvent_WithCivAndKiller()
        {
            var ctx = Ctx();
            ctx.UnitTypes.TryGetByKey("unit.rifleman", out var rifle);
            var victim = ctx.Units.Spawn(rifle, 1, 5, 6);
            var killer = ctx.Units.Spawn(rifle, 2, 0, 0);
            var died = new List<UnitDiedEvent>();
            ctx.Events.Subscribe<UnitDiedEvent>(died.Add);
            Assert.IsTrue(ctx.Units.Despawn(victim, killer, 2));
            Assert.IsFalse(ctx.Units.Despawn(victim, killer, 2));
            ctx.Events.Flush();
            Assert.AreEqual(1, died.Count);
            Assert.AreEqual(victim, died[0].Unit);
            Assert.AreEqual(killer, died[0].Killer);
            Assert.AreEqual(1, died[0].CivId);
            Assert.AreEqual(2, died[0].KillerCivId);
            Assert.AreEqual(rifle, died[0].UnitType);
            Assert.AreEqual(5f, died[0].X);
        }

        [Test]
        public void StaleId_IsIgnoredEverywhere()
        {
            var ctx = Ctx();
            ctx.UnitTypes.TryGetByKey("unit.settler", out var t);
            var a = ctx.Units.Spawn(t, 1, 0, 0);
            ctx.Units.Despawn(a);
            var b = ctx.Units.Spawn(t, 1, 0, 0);
            Assert.AreEqual(a.Index, b.Index);
            Assert.IsFalse(ctx.Units.SetMoveTarget(a, 1, 1));
            Assert.IsFalse(ctx.Units.SetHp(a, 1));
            Assert.IsFalse(ctx.Units.SetPosition(a, 1, 1, 0, 0));
            Assert.IsFalse(ctx.Units.TryGet(a, out _));
            Assert.AreEqual(default(UnitView).Id, ctx.Units.Get(a).Id);
            Assert.IsTrue(ctx.Units.TryGet(b, out var vb));
            Assert.AreEqual(0f, vb.X);
        }

        [Test]
        public void HpIsClamped_StateDeadOnlyViaDespawn()
        {
            var ctx = Ctx();
            ctx.UnitTypes.TryGetByKey("unit.settler", out var t);
            var a = ctx.Units.Spawn(t, 1, 0, 0);
            ctx.Units.SetHp(a, 999);
            Assert.AreEqual(50f, ctx.Units.Get(a).Hp);
            ctx.Units.SetHp(a, -5);
            Assert.AreEqual(0f, ctx.Units.Get(a).Hp);
            Assert.IsFalse(ctx.Units.SetState(a, UnitState.Dead));
        }

        [Test]
        public void MoveTarget_SetsAndClearsMovingState()
        {
            var ctx = Ctx();
            ctx.UnitTypes.TryGetByKey("unit.settler", out var t);
            var a = ctx.Units.Spawn(t, 1, 0, 0);
            ctx.Units.SetMoveTarget(a, 10, 20);
            var v = ctx.Units.Get(a);
            Assert.IsTrue(v.HasMoveTarget);
            Assert.AreEqual(UnitState.Moving, v.State);
            ctx.Units.ClearMoveTarget(a);
            Assert.AreEqual(UnitState.Idle, ctx.Units.Get(a).State);
        }

        [Test]
        public void CopyAliveIds_ListsOnlyLiving()
        {
            var ctx = Ctx();
            ctx.UnitTypes.TryGetByKey("unit.settler", out var t);
            var a = ctx.Units.Spawn(t, 1, 0, 0);
            var b = ctx.Units.Spawn(t, 1, 0, 0);
            var c = ctx.Units.Spawn(t, 1, 0, 0);
            ctx.Units.Despawn(b);
            var ids = new List<EntityId>();
            ctx.Units.CopyAliveIds(ids);
            CollectionAssert.AreEqual(new[] { a, c }, ids);
            int n = 0;
            ctx.Units.ForEachAlive(_ => n++);
            Assert.AreEqual(2, n);
        }

        [Test]
        public void Grows_PastInitialCapacity()
        {
            var ctx = Ctx();
            ctx.UnitTypes.TryGetByKey("unit.settler", out var t);
            for (int i = 0; i < 3000; i++) Assert.IsTrue(ctx.Units.Spawn(t, 1, i, 0).IsValid);
            Assert.AreEqual(3000, ctx.Units.Count);
        }
    }

    public class SaveRoundTripTests
    {
        [Test]
        public void CoreAndUnits_RoundTrip_IntoFreshComposition()
        {
            var a = GameComposition.Create(new GameStartSettings { Seed = 77 }, TestContent.Setup);
            var ctx = a.Context;
            ctx.UnitTypes.TryGetByKey("unit.rifleman", out var rifle);
            ctx.UnitTypes.TryGetByKey("unit.settler", out var settler);
            var u1 = ctx.Units.Spawn(rifle, 1, 1, 2);
            var u2 = ctx.Units.Spawn(settler, 2, 3, 4);
            var u3 = ctx.Units.Spawn(rifle, 2, 5, 6);
            ctx.Units.Despawn(u2);
            ctx.Units.SetMoveTarget(u1, 9, 9);
            ctx.Units.SetTask(u3, new UnitTask { Kind = UnitTaskKind.Attack, TargetEntity = u1 });
            ctx.Units.SetHp(u3, 42);
            for (int i = 0; i < 7; i++) a.Runner.StepOnce();
            ctx.Random.NextULong();

            var captured = new Dictionary<string, object>();
            foreach (var s in ctx.Saveables.InLoadOrder()) captured[s.SaveKey] = s.CaptureState();

            // Inhalte in anderer Reihenfolge registriert: Laufzeit-ints unterscheiden sich, Keys nicht.
            var b = GameComposition.Create(new GameStartSettings { Seed = 1 }, c2 =>
            {
                c2.UnitTypes.Register(new UnitTypeData { Key = "unit.rifleman", MaxHp = 100f });
                c2.UnitTypes.Register(new UnitTypeData { Key = "unit.settler", MaxHp = 50f });
            });
            var loadOrder = new List<string>();
            foreach (var s in b.Context.Saveables.InLoadOrder())
            {
                loadOrder.Add(s.SaveKey);
                Assert.IsTrue(s.TryRestoreState(captured[s.SaveKey], out var err), err);
            }
            CollectionAssert.AreEqual(new[] { "core", "units" }, loadOrder);

            var rc = b.Context;
            Assert.AreEqual(7, rc.Clock.Tick);
            Assert.AreEqual(ctx.Random.State, rc.Random.State);
            Assert.AreEqual(2, rc.Units.Count);
            Assert.IsFalse(rc.Units.Exists(u2));
            Assert.IsTrue(rc.Units.TryGet(u1, out var v1));
            Assert.AreEqual("unit.rifleman", rc.UnitTypes.KeyOf(v1.Type));
            Assert.IsTrue(v1.HasMoveTarget);
            Assert.AreEqual(9f, v1.MoveTargetX);
            var v3 = rc.Units.Get(u3);
            Assert.AreEqual(42f, v3.Hp);
            Assert.AreEqual(UnitTaskKind.Attack, v3.Task.Kind);
            Assert.AreEqual(u1, v3.Task.TargetEntity);
            var next = rc.Units.Spawn(v1.Type, 1, 0, 0);
            Assert.AreEqual(u2.Index, next.Index);
            Assert.AreEqual(u2.Generation + 1, next.Generation, "Generation nach dem Laden korrekt fortgesetzt");
        }

        [Test]
        public void UnitRestore_WithUnknownTypeKey_FailsAndKeepsState()
        {
            var a = GameComposition.Create(null, TestContent.Setup);
            a.Context.UnitTypes.TryGetByKey("unit.settler", out var t);
            a.Context.Units.Spawn(t, 1, 0, 0);
            var state = (UnitStoreState)((ISaveable)a.Context.Units).CaptureState();
            state.Units[0].TypeKey = "unit.removed";

            var b = GameComposition.Create(null, TestContent.Setup);
            b.Context.UnitTypes.TryGetByKey("unit.settler", out var tb);
            var keep = b.Context.Units.Spawn(tb, 3, 0, 0);
            Assert.IsFalse(((ISaveable)b.Context.Units).TryRestoreState(state, out var err));
            StringAssert.Contains("unit.removed", err);
            Assert.IsTrue(b.Context.Units.Exists(keep), "bisheriger Zustand bleibt bei Fehler erhalten");
        }

        [Test]
        public void UnitRestore_RejectsAliveIndexWithoutRecord()
        {
            var a = GameComposition.Create(null, TestContent.Setup);
            a.Context.UnitTypes.TryGetByKey("unit.settler", out var t);
            a.Context.Units.Spawn(t, 1, 0, 0);
            var state = (UnitStoreState)((ISaveable)a.Context.Units).CaptureState();
            state.Units = new UnitRecord[0];
            Assert.IsFalse(((ISaveable)GameComposition.Create(null, TestContent.Setup).Context.Units).TryRestoreState(state, out _));
        }

        [Test]
        public void CoreRestore_RejectsWrongType()
        {
            var ctx = GameComposition.Create(null).Context;
            ctx.Saveables.TryGet(SimCoreSaveable.Key, out var core);
            Assert.IsFalse(core.TryRestoreState("falsch", out _));
            Assert.IsFalse(core.TryRestoreState(new SimCoreState { Tick = 1, RandomState = 0 }, out _));
        }

        [Test]
        public void SaveableRegistry_RejectsDuplicateKeys()
        {
            var ctx = GameComposition.Create(null).Context;
            ctx.Saveables.TryGet("units", out var units);
            Assert.Throws<System.InvalidOperationException>(() => ctx.Saveables.Add(units));
        }
    }
}

namespace WorldBeyond.Tests
{
    public class LoadReportTests
    {
        [Test]
        public void WarnAndFail_AreCollectedSeparately()
        {
            var r = new WorldBeyond.Simulation.LoadReport();
            Assert.IsFalse(r.HasErrors);
            r.Warn("Auftrag zurückgesetzt");
            Assert.IsFalse(r.HasErrors);
            r.Fail("doppelte ID");
            Assert.IsTrue(r.HasErrors);
            CollectionAssert.AreEqual(new[] { "Auftrag zurückgesetzt" }, r.Warnings);
            CollectionAssert.AreEqual(new[] { "doppelte ID" }, r.Errors);
        }
    }
}

namespace WorldBeyond.Tests
{
    public class UnitContentCheckTests
    {
        [Test]
        public void CollectContentKeys_ListsEveryUnitTypeKey()
        {
            var comp = WorldBeyond.Application.GameComposition.Create(null, TestContent.Setup);
            var ctx = comp.Context;
            ctx.UnitTypes.TryGetByKey("unit.settler", out var s);
            ctx.UnitTypes.TryGetByKey("unit.rifleman", out var r);
            ctx.Units.Spawn(s, 1, 0, 0);
            ctx.Units.Spawn(r, 1, 0, 0);
            var store = (WorldBeyond.Simulation.ISaveable)ctx.Units;
            var keys = new List<(WorldBeyond.Core.ContentKind, string)>();
            ((WorldBeyond.Simulation.ISaveContentCheck)ctx.Units).CollectContentKeys(store.CaptureState(), keys);
            CollectionAssert.AreEquivalent(new[] { (WorldBeyond.Core.ContentKind.Unit, "unit.settler"), (WorldBeyond.Core.ContentKind.Unit, "unit.rifleman") }, keys);
        }
    }
}
