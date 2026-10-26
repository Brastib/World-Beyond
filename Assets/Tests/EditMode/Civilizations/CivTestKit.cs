using System.Collections.Generic;
using System.Text;
using WorldBeyond.Application;
using WorldBeyond.Core;
using WorldBeyond.Simulation;
using WorldBeyond.Simulation.Civilizations;

namespace WorldBeyond.Tests.Civilizations
{
    // Gemeinsame Hilfen der Civ-Tests (Phase 3): echte GameComposition, keine Attrappen.
    internal sealed class CivRun
    {
        public GameComposition Comp;
        public SimContext Ctx => Comp.Context;
        public ICivilizationQuery Civs => Ctx.Get<ICivilizationQuery>();
        public ISettlementQuery Settlements => Ctx.Get<ISettlementQuery>();
        public CivState State => Ctx.Get<CivState>();

        public int Founded, Abandoned, BuildingsCompleted, TechsResearched, Harvested, CommandsSeen, Collapsed, Recruited;
        public readonly List<string> Log = new List<string>();

        public static CivRun Create(ulong seed, int civs = 2, int size = 128)
        {
            var r = new CivRun
            {
                Comp = GameComposition.Create(new GameStartSettings { Seed = seed, WorldWidth = size, WorldHeight = size, CivilizationCount = civs }, null),
            };
            var ev = r.Ctx.Events;
            ev.Subscribe<SettlementFoundedEvent>(_ => r.Founded++);
            ev.Subscribe<SettlementAbandonedEvent>(_ => r.Abandoned++);
            ev.Subscribe<BuildingCompletedEvent>(_ => r.BuildingsCompleted++);
            ev.Subscribe<TechResearchedEvent>(_ => r.TechsResearched++);
            ev.Subscribe<CivilizationCollapsedEvent>(_ => r.Collapsed++);
            ev.Subscribe<UnitRecruitedEvent>(_ => r.Recruited++);
            ev.Subscribe<WorldBeyond.Simulation.World.ResourceDepletedEvent>(_ => r.Harvested++);
            return r;
        }

        public void Years(double years)
        {
            int ticks = (int)(years * CivTime.TicksPerYear);
            for (int i = 0; i < ticks; i++)
            {
                CommandsSeen += Ctx.Commands.PendingCount;
                Comp.Runner.StepOnce();
            }
        }

        public int TotalPopulation()
        {
            int p = 0;
            foreach (int id in Civs.AllCivIds) p += Civs.Get(id).Population;
            return p;
        }

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append($"Jahr {Ctx.Clock.Tick / CivTime.TicksPerYear}: ");
            foreach (int id in Civs.AllCivIds)
            {
                var c = Civs.Get(id);
                sb.Append($"[{c.Name}: Pop {c.Population}, Siedl. {c.SettlementCount}, Gebiet {c.TerritoryTiles}, Epoche {c.Epoch}, " +
                          $"Nahrung {c.Stock.Food}, Holz {c.Stock.Wood}, Stein {c.Stock.Stone}, Forschung {c.CurrentResearchKey}] ");
            }
            sb.Append($"Gründungen {Founded}, Gebäude {BuildingsCompleted}, Techs {TechsResearched}, Rekrutiert {Recruited}, Einheiten {Ctx.Units.Count}");
            return sb.ToString();
        }
    }
}
