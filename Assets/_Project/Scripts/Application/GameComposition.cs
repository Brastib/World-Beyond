using System;
using WorldBeyond.Core;
using WorldBeyond.Simulation;
using WorldBeyond.Simulation.Civilizations;
using WorldBeyond.Simulation.Commands;
using WorldBeyond.Simulation.Save;
using WorldBeyond.Simulation.Units;
using WorldBeyond.Simulation.World;
using WorldBeyond.Simulation.World.Generation;
using WorldBeyond.Simulation.World.Navigation;

namespace WorldBeyond.Application
{
    [Serializable]
    public sealed class GameStartSettings
    {
        public ulong Seed = 12345;
        public int WorldWidth = 256;
        public int WorldHeight = 256;
        public int CivilizationCount = 2;
    }

    /// <summary>
    /// Composition Root in reinem C# (ARCHITECTURE §1 Prinzip 6). Einziger Ort, an dem Simulationsdienste und
    /// -systeme erzeugt, registriert und verbunden werden. GameBootstrap ruft nur Create auf.
    /// Fachphasen tragen ihre Dienste in <see cref="RegisterPhaseServices"/> ein (Absprache mit dem Integrations-Thread).
    /// Laden: neue leere Komposition erzeugen, Spielstand einspielen, dann im GameBootstrap austauschen.
    /// </summary>
    public sealed class GameComposition
    {
        public SimContext Context { get; }
        public SimulationRunner Runner { get; }
        public GameStartSettings Settings { get; }

        private GameComposition(SimContext ctx, SimulationRunner runner, GameStartSettings settings)
        {
            Context = ctx;
            Runner = runner;
            Settings = settings;
        }

        /// <param name="contentSetup">Registriert Inhaltstypen (Einheiten, Gebäude …) beim Start; darf null sein.</param>
        public static GameComposition Create(GameStartSettings settings, Action<SimContext> contentSetup = null)
        {
            settings ??= new GameStartSettings();

            var clock = new SimClock();
            var random = new SimRandom(settings.Seed);
            var events = new SimEventBus();
            var commands = new CommandQueue();
            var content = new ContentRegistry();
            var unitTypes = new UnitTypeRegistry(content);
            var units = new UnitStore(unitTypes, events);
            var saveables = new SaveableRegistry();

            var ctx = new SimContext(clock, random, events, commands, content, unitTypes, units, saveables);
            saveables.Add(new SimCoreSaveable(clock, random));
            saveables.Add(units);

            var runner = new SimulationRunner(ctx);
            runner.Register(new CommandProcessingSystem(commands));

            contentSetup?.Invoke(ctx);
            RegisterPhaseServices(ctx, runner, settings);

            ctx.Seal();
            return new GameComposition(ctx, runner, settings);
        }

        /// <summary>
        /// Anschlusspunkte der Fachphasen, in fester Reihenfolge. Jede Phase ergänzt hier ihre Zeilen per Pull Request:
        /// World (Phase 2): IWorldGenerator/IWorldGrid, MovementSystem, IPathfinder, ISpatialIndex.
        /// Civ (Phase 3): Civ-/Siedlungs-/Territoriums-Registry, IEconomy, ITechTree, IRecruitment, CivAISystem.
        /// War (Phase 4): IDiplomacyService, Kriegsbewerter für CivAISystem.
        /// Military (Phase 5): IArmyRegistry, IArmyAI, ICombatService, IProjectileSystem.
        /// Bis eine Phase gemergt ist, enthält die Simulation ihre Dienste nicht.
        /// </summary>
        private static void RegisterPhaseServices(SimContext ctx, SimulationRunner runner, GameStartSettings settings)
        {
            // World (Phase 2): eine Welt, aus Seed und Größe der Startparameter erzeugt.
            var world = new WorldGenerator().Generate(new WorldGenSettings
            {
                Seed = settings.Seed,
                Width = settings.WorldWidth,
                Height = settings.WorldHeight,
            });
            world.AttachEvents(ctx.Events);
            var pathfinder = new GridPathfinder(world);
            var spatial = new SpatialHash(world.Width * WorldConstants.TileSize, world.Height * WorldConstants.TileSize);
            ctx.Register<IWorldGrid>(world);
            ctx.Register<IWorldQuery>(world);
            ctx.Register<IPathfinder>(pathfinder);
            ctx.Register<ISpatialIndex>(spatial);
            ctx.Saveables.Add(world);
            var movement = new MovementSystem(world, pathfinder, spatial);
            ctx.Saveables.Add(movement);
            runner.Register(movement);
            runner.Register(new WorldEventsSystem(world));

            // Civ (Phase 3): Zivilisationen, Siedlungen, Territorium, Wirtschaft, Forschung, Rekrutierung, Civ-KI.
            // Neue Welt startet mit CivilizationCount Zivilisationen, die sich ohne Eingriff entwickeln (Sandbox).
            var civ = CivModule.Install(ctx, runner.Register);
            civ.SpawnInitialCivilizations(settings.CivilizationCount, settings.Seed);
        }
    }
}
