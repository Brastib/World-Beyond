using System;
using WorldBeyond.Simulation.Combat;

namespace WorldBeyond.Simulation.Military
{
    /// <summary>
    /// Erzeugt und registriert alle Phase-5-Dienste. Einziger Aufruf aus GameComposition.RegisterPhaseServices
    /// (Zeile trägt der Integrations-Thread ein). Die Delegates in CombatEnvironment werden dort mit World/War verbunden.
    /// </summary>
    public static class MilitaryModule
    {
        public static CombatSystem Register(SimContext ctx, Action<ISimulationSystem> registerSystem, CombatEnvironment env = null)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (registerSystem == null) throw new ArgumentNullException(nameof(registerSystem));
            env ??= new CombatEnvironment();

            var catalog = new MilitaryCatalog();
            MilitaryContent.RegisterM1(catalog, ctx.UnitTypes);

            var service = new CombatService(ctx.Units, catalog, ctx.Events, env);
            var system = new CombatSystem(ctx.Units, catalog, service, env, ctx.Random, ctx.Events);

            ctx.Register(catalog);
            ctx.Register<IMilitaryQuery>(catalog);
            ctx.Register(env);
            ctx.Register<ICombatService>(service);
            ctx.Register(system);
            ctx.Saveables.Add(system);
            registerSystem(system);
            return system;
        }
    }
}
