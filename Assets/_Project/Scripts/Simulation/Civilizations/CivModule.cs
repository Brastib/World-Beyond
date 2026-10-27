using System;
using System.Collections.Generic;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Simulation.Civilizations
{
    /// <summary>
    /// Anschlusspunkt der Civ-Phase für die Composition Root (GameComposition.RegisterPhaseServices).
    /// Erzeugt alle Civ-Dienste und -Systeme genau einmal, registriert sie im SimContext und im Runner.
    /// Voraussetzung: IWorldGrid ist bereits registriert (World vor Civ).
    /// </summary>
    public sealed class CivModule
    {
        public CivState State { get; private set; }
        public CivContent Content { get; private set; }
        public CivilizationRegistry Civilizations { get; private set; }
        public SettlementRegistry Settlements { get; private set; }
        public TerritoryService Territory { get; private set; }
        public Economy Economy { get; private set; }
        public TechTree Tech { get; private set; }
        public Recruitment Recruitment { get; private set; }
        public CivLod Lod { get; private set; }
        public TransportSystem Transport { get; private set; }
        public SettlerSystem Settlers { get; private set; }
        public ExpansionPlanner Planner { get; private set; }
        public CivAISystem AI { get; private set; }
        public SettlementSimulationSystem SettlementSystem { get; private set; }
        public TerritorySystem TerritorySystem { get; private set; }
        public CivSaveable Saveable { get; private set; }

        /// <param name="register">Registriert ein System im Runner (runner.Register).</param>
        public static CivModule Install(SimContext ctx, Action<ISimulationSystem> register)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (register == null) throw new ArgumentNullException(nameof(register));
            var world = ctx.Get<IWorldGrid>();
            var m = new CivModule();

            m.Content = new CivContent(ctx.Content);
            m.Content.RegisterDefaults(ctx.UnitTypes);
            m.State = new CivState(ctx, world, m.Content);
            var s = m.State;

            m.Civilizations = new CivilizationRegistry(s);
            m.Settlements = new SettlementRegistry(s);
            m.Territory = new TerritoryService(s);
            m.Economy = new Economy(s);
            m.Tech = new TechTree(s);
            m.Recruitment = new Recruitment(s);
            m.Lod = new CivLod();

            m.Civilizations.Settlements = m.Settlements;
            m.Civilizations.Tech = m.Tech;
            m.Civilizations.Territory = m.Territory;
            m.Settlements.Civs = m.Civilizations;
            m.Settlements.Territory = m.Territory;
            m.Settlements.Economy = m.Economy;
            m.Recruitment.Settlements = m.Settlements;
            m.Recruitment.Tech = m.Tech;
            m.Recruitment.Economy = m.Economy;

            m.Transport = new TransportSystem(s, m.Recruitment);
            m.Recruitment.Transport = m.Transport;
            m.Recruitment.RegisterDefaults();
            m.Planner = new ExpansionPlanner(s, m.Settlements);
            m.Settlers = new SettlerSystem(s, m.Settlements, m.Planner);
            m.SettlementSystem = new SettlementSimulationSystem(s, m.Settlements, m.Tech, m.Lod, m.Recruitment, m.Territory);
            m.TerritorySystem = new TerritorySystem(m.Territory);
            m.AI = new CivAISystem(s, m.Settlements, m.Tech, m.Recruitment, m.Planner, m.Settlers, m.Territory);
            m.Saveable = new CivSaveable(s, m.Civilizations, m.Settlements, m.Territory, m.Transport);

            ctx.Register(s);
            ctx.Register<ICivilizationRegistry>(m.Civilizations);
            ctx.Register<ICivilizationQuery>(m.Civilizations);
            ctx.Register<ISettlementRegistry>(m.Settlements);
            ctx.Register<ISettlementQuery>(m.Settlements);
            ctx.Register<ITerritoryService>(m.Territory);
            ctx.Register<IEconomy>(m.Economy);
            ctx.Register<ITechTree>(m.Tech);
            ctx.Register<IRecruitment>(m.Recruitment);
            ctx.Register<ICivSimulationLod>(m.Lod);
            ctx.Register<ICivDecisionLog>(m.AI);
            ctx.Register<IBuildingQuery>(s.Buildings);
            ctx.Register(m.Transport);
            ctx.Saveables.Add(m.Saveable);

            register(m.AI);
            register(m.SettlementSystem);
            register(m.Transport);
            register(m.TerritorySystem);
            register(new ArmyUpkeepSystem(s));

            // Welt prüft beim Laden ihre Verweise auf Gebäude und Zivilisationen (SYSTEM_INTERFACES §4.5).
            if (world is WorldGrid grid)
            {
                grid.BuildingExists = s.Buildings.Exists;
                grid.CivilizationExists = id => s.Civ(id) != null;
            }
            return m;
        }

        /// <summary>
        /// Neue Welt: count Zivilisationen an den besten, weit auseinanderliegenden Startplätzen. Danach entwickeln sie sich
        /// ohne Spielereingriff (Sandbox). Liefert die Zahl tatsächlich gegründeter Zivilisationen.
        /// </summary>
        public int SpawnInitialCivilizations(int count, ulong seed)
        {
            if (count <= 0) return 0;
            var world = State.World;
            var starts = new List<TileScore>();
            int minDist = Math.Max(SettlementRegistry.MinSettlementDistance * 2, Math.Min(world.Width, world.Height) / (count + 1));
            world.FindStartLocations(count, minDist, starts);
            int created = 0;
            for (int i = 0; i < starts.Count; i++)
            {
                ulong culture = seed * 0x9E3779B97F4A7C15UL + (ulong)(i + 1) * 0xBF58476D1CE4E5B9UL;
                var color = CivBootstrap.Palette[i % CivBootstrap.Palette.Length];
                // FindStartLocations liefert die Mitte eines 3×3-Bereichs; der Ortskern (2×2) beginnt eine Kachel davor.
                // In der Nähe den Platz mit dem meisten bebaubaren, fruchtbaren Land wählen (Hänge tragen keine Felder).
                var (sx, sy) = RefineStart(starts[i].X - 1, starts[i].Y - 1);
                if (Civilizations.TryCreate(null, color, culture, sx, sy, out _, out _)) created++;
            }
            Territory.Recompute();
            return created;
        }

        private readonly List<TileScore> _refine = new List<TileScore>();

        private (int x, int y) RefineStart(int x, int y)
        {
            var world = State.World;
            world.FindBestTiles(x, y, 8, (int tx, int ty, in TileData t) =>
            {
                if (t.OwnerCivId != 0 || SettlementRegistry.CoreOnStreet(tx, ty) || !world.CanPlaceFootprint(tx, ty, SettlementRegistry.CoreSize, SettlementRegistry.CoreSize, out _)) return 0f;
                foreach (int id in State.SettlementIdsInOrder)
                {
                    var o = State.FindSettlement(id);
                    int dx = o.X - tx, dy = o.Y - ty;
                    if (dx * dx + dy * dy < SettlementRegistry.MinSettlementDistance * SettlementRegistry.MinSettlementDistance * 4) return 0f;
                }
                float v = ExpansionPlanner.SiteValue(world, tx, ty);
                int ddx = tx - x, ddy = ty - y;
                return v / (1f + (float)Math.Sqrt(ddx * ddx + ddy * ddy) * 0.05f);
            }, 1, _refine);
            if (_refine.Count > 0) return (_refine[0].X, _refine[0].Y);
            return SettlementRegistry.OffStreet(x, y);   // Rückfall: vorgeschlagener Platz, vom Straßenraster weggeschoben
        }
    }

    public static class CivBootstrap
    {
        /// <summary>Gut unterscheidbare Reichsfarben (auch auf Gelände und Minimap).</summary>
        public static readonly CivColor[] Palette =
        {
            new CivColor(214, 69, 65), new CivColor(52, 120, 198), new CivColor(236, 190, 52), new CivColor(76, 160, 92),
            new CivColor(150, 86, 186), new CivColor(232, 126, 44), new CivColor(40, 170, 170), new CivColor(200, 90, 150),
        };

        /// <summary>Freier Startplatz für eine nachträglich erschaffene Zivilisation: bester Platz, an dem gegründet werden darf.</summary>
        public static bool TryFindFreeStart(SimContext ctx, out int x, out int y)
        {
            var world = ctx.Get<IWorldQuery>();
            var starts = new List<TileScore>();
            world.FindStartLocations(32, SettlementRegistry.MinSettlementDistance, starts);
            foreach (var s in starts)
            {
                var (sx, sy) = SettlementRegistry.OffStreet(s.X - 1, s.Y - 1);
                bool free = world.InBounds(sx, sy) && world.Get(sx, sy).OwnerCivId == 0;
                if (!free) continue;
                // Probe mit einer existierenden Civ ist nicht möglich; Gründbarkeit ohne Besitzprüfung genügt hier.
                if (!world.CanPlaceFootprint(sx, sy, SettlementRegistry.CoreSize, SettlementRegistry.CoreSize, out _)) continue;
                bool farEnough = true;
                var q = ctx.Get<ISettlementQuery>();
                foreach (int id in q.AllSettlementIds)
                {
                    var v = q.Get(id);
                    int dx = v.X - sx, dy = v.Y - sy;
                    if (dx * dx + dy * dy < 400) { farEnough = false; break; }
                }
                if (farEnough) { x = sx; y = sy; return true; }
            }
            x = y = -1;
            return false;
        }
    }
}
