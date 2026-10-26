using System;
using System.Diagnostics;
using NUnit.Framework;
using WorldBeyond.Application;
using WorldBeyond.Simulation.Civilizations;
using WorldBeyond.Simulation.Diplomacy;
using WorldBeyond.Simulation.Units;

namespace WorldBeyond.Tests.Integration
{
    public class M1SandboxProbe
    {
        [Explicit, TestCase(1UL), TestCase(7UL), TestCase(2026UL)]
        public void Probe(ulong seed)
        {
            var comp = GameComposition.Create(new GameStartSettings { Seed = seed, WorldWidth = 128, WorldHeight = 128 });
            var ctx = comp.Context;
            long war = -1, combatDeath = -1; int deaths = 0, recruited = 0;
            ctx.Events.Subscribe<WarDeclaredEvent>(e => { if (war < 0) war = ctx.Clock.Tick; });
            ctx.Events.Subscribe<UnitRecruitedEvent>(e => recruited++);
            ctx.Events.Subscribe<UnitDiedEvent>(e => { if (e.KillerCivId > 0 && e.KillerCivId != e.CivId) { deaths++; if (combatDeath < 0) combatDeath = ctx.Clock.Tick; } });
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 18000 && combatDeath < 0; i++) comp.Runner.StepOnce();
            var civs = ctx.Get<ICivilizationQuery>();
            var ev = new WarStrategyEvaluator((DiplomacyService)ctx.Get<IDiplomacyService>());
            foreach (var (a, b) in new[] { (civs.AllCivIds[0], civs.AllCivIds[1]), (civs.AllCivIds[1], civs.AllCivIds[0]) })
            {
                var o = ev.ScoreWar(a, b);
                var d = (DiplomacyService)ctx.Get<IDiplomacyService>();
                d.CanDeclareWar(a, b, out var why);
                TestContext.WriteLine($"  {a}->{b}: utility {o.Utility:0.000} thr {d.Settings.WarThreshold} can={why} " + string.Join(", ", Array.ConvertAll(o.Factors, f => f.Name + "=" + f.Value.ToString("0.00"))));
                TestContext.WriteLine($"  pop {civs.Get(a).Population} mil {civs.Get(a).MilitaryStrength} terr {civs.Get(a).TerritoryTiles} border {d.World.GetSharedBorderLength(a,b)} str {d.World.GetMilitaryStrength(a)} opinion {d.GetOpinion(a,b):0.0}");
            }
            var port = ((DiplomacyService)ctx.Get<IDiplomacyService>()).World;
            bool ok = port.RequestMilitaryBuildup(civs.AllCivIds[0], 2, out var reason);
            TestContext.WriteLine($"  buildup ok={ok} reason={reason}");
            TestContext.WriteLine($"seed {seed}: ticks {ctx.Clock.Tick} in {sw.ElapsedMilliseconds} ms, civs {civs.AllCivIds.Count}, war@{war}, firstCombatDeath@{combatDeath}, deaths {deaths}, recruited {recruited}, faults {comp.Runner.FaultCount}");
        }
    }
}
