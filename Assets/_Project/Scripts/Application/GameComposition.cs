using System;
using WorldBeyond.Application.Integration;
using WorldBeyond.Core;
using WorldBeyond.Simulation;
using WorldBeyond.Simulation.Civilizations;
using WorldBeyond.Simulation.Combat;
using WorldBeyond.Simulation.Diplomacy;
using WorldBeyond.Simulation.Military;
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
            // Handler-Fehler melden statt mitten im Tick zu werfen (sonst liefe derselbe Tick erneut).
            events.HandlerFailed += ex => runner.ReportFault(clock.Tick, "EventHandler", ex);
            runner.Register(new CommandProcessingSystem(commands));

            // Phasen zuerst: Phaseninhalte (unit.rifleman, unit.settler …) registriert nur ihr Modul.
            // contentSetup ergänzt danach Zusatzinhalte; Tests verwenden dafür Schlüssel mit Präfix "test." (INTEGRATION §5).
            RegisterPhaseServices(ctx, runner, settings);
            contentSetup?.Invoke(ctx);

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

            // Military (Phase 5): Kampf, Armeen, Armee-KI. Delegates werden unten mit World/War/Civ verbunden.
            var combatEnv = new CombatEnvironment();
            var armyEnv = new ArmyEnvironment();
            MilitaryModule.Register(ctx, runner.Register, combatEnv, armyEnv);
            var military = ctx.Get<IMilitaryQuery>();
            civ.Recruitment.RegisterRecruitable(MilitaryRecruitment.Rifleman);

            // War (Phase 4): Diplomatie und Kriegsentscheidung. Verluste abonniert DiplomacyService selbst (UnitDiedEvent).
            var port = new DiplomacyWorldPortAdapter(civ.Civilizations, civ.Settlements, civ.Territory, civ.Economy,
                civ.Recruitment, ctx.Units, military, world, spatial);
            // GetArmyUnitCount enthält die Ausbildungsschlangen (Military + Civ), War muss Anfragen nicht selbst mitzählen.
            var diplomacy = new DiplomacyService(port, ctx.Events, () => ctx.Clock.Tick, new DiplomacySettings { RecruitPendingTicks = 0 });
            ctx.Register<IDiplomacyService>(diplomacy);
            ctx.Register<IDiplomacyQuery>(diplomacy);
            ctx.Register<IWarQuery>(diplomacy);
            ctx.Register<ICivWarEvaluator>(new CivWarEvaluatorAdapter(new WarStrategyEvaluator(diplomacy)));
            ctx.Saveables.Add(new DiplomacySaveable(diplomacy));
            runner.Register(new DiplomacySystem(diplomacy));
            // Abos nach SpawnInitialCivilizations sind unkritisch, weil Events erst beim Flush am Tickende ausgeliefert werden.
            // Wer hier künftig synchron publiziert, muss diese Abos vor Civ setzen.
            ctx.Events.Subscribe<SettlementCapturedEvent>(e =>
                diplomacy.ReportSettlementCaptured(e.SettlementId, e.OldCivId, e.NewCivId,
                    civ.Settlements.TryGet(e.SettlementId, out var s) ? s.Population : 0));
            ctx.Events.Subscribe<CivilizationCollapsedEvent>(e => diplomacy.ReportCivilizationCollapsed(e.CivId));
            // Erste Feindberührung für War (Frieden erst nach Kontakt): jeder Treffer zwischen Einheiten zweier Civs.
            // Ist der Schütze bis zum Flush schon entfernt, zählt der Treffer nicht; Verluste meldet UnitDiedEvent ohnehin.
            ctx.Events.Subscribe<UnitDamagedEvent>(e =>
            {
                if (ctx.Units.TryGet(e.Source, out var src) && ctx.Units.TryGet(e.Target, out var dst) && src.CivId != dst.CivId)
                    diplomacy.ReportCombatContact(src.CivId, dst.CivId);
            });

            MilitaryWiring.Connect(combatEnv, armyEnv, diplomacy, world, spatial, civ.Civilizations, civ.Settlements, civ.Recruitment);
            // Eingereihte Militäraufträge (nur Ausbildungsschlangen) für die Aufrüstungsgrenze der Armee-KI und von War.
            armyEnv.PendingRecruits = civId => civ.Recruitment.QueuedMilitaryCount(civId);
        }
    }
}
