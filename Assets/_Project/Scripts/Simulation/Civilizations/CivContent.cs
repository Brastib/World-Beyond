using System;
using System.Collections.Generic;
using WorldBeyond.Core;
using WorldBeyond.Simulation.Units;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Simulation.Civilizations
{
    /// <summary>Laufzeit-ID einer Technologie (ContentRegistry, ContentKind.Tech). Nie speichern, im Spielstand steht der Key.</summary>
    public readonly struct TechId : IEquatable<TechId>
    {
        public readonly int Value;
        public TechId(int value) => Value = value;
        public bool IsValid => Value > 0;
        public bool Equals(TechId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is TechId o && Equals(o);
        public override int GetHashCode() => Value;
        public override string ToString() => $"Tech({Value})";
    }

    /// <summary>Laufzeit-ID eines Gebäudetyps (ContentKind.Building). Nie speichern.</summary>
    public readonly struct BuildingTypeId : IEquatable<BuildingTypeId>
    {
        public readonly int Value;
        public BuildingTypeId(int value) => Value = value;
        public bool IsValid => Value > 0;
        public bool Equals(BuildingTypeId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is BuildingTypeId o && Equals(o);
        public override int GetHashCode() => Value;
        public override string ToString() => $"BuildingType({Value})";
    }

    /// <summary>
    /// Reine Daten eines Gebäudetyps. Erträge pro Arbeiter und Spieljahr. Ein Betrieb mit Rohstoff (Holz, Stein, Erz, Öl)
    /// baut seinen Ertrag über IWorldGrid.TryHarvest im Einzugsbereich ab; ohne Vorkommen steht er still.
    /// </summary>
    public sealed class BuildingTypeData
    {
        public string Key;                    // z. B. "building.farm"
        public string DisplayName;
        public BuildingCategory Category;
        public ResourceCost Cost;
        public float BuildWorkYears = 1f;     // Arbeiterjahre bis Fertigstellung
        public int SizeX = 1, SizeY = 1;      // Footprint in Kacheln
        public int Housing;                   // Wohnplätze
        public int Storage;                   // zusätzliche Lagerkapazität je Gut
        public JobKind Job;
        public int JobSlots;
        public ResourceType Produces;         // ResourceType.None = nichts
        public float OutputPerWorkerYear;
        public ResourceType HarvestFrom;      // Kachelvorkommen, das abgebaut wird (None = kein Abbau)
        public int CatchmentRadius = 5;       // Einzugsbereich in Kacheln um das Gebäude
        public ResourceType Input1; public float Input1PerOutput;   // Verarbeitung, z. B. Erz → Metall
        public ResourceType Input2; public float Input2PerOutput;
        public float ResearchPerWorkerYear;
        public float Defense;
        public bool NeedsWater;               // Fischerei: Wasser im Einzugsbereich
        public bool UsesFertility;            // Farm: Ertrag × Fruchtbarkeit im Einzugsbereich
        public string RequiredTechKey;        // null = von Anfang an
        public SettlementTier MinTier = SettlementTier.Camp;
        public int MaxPerSettlement = 99;
    }

    public sealed class BuildingTypeRegistry
    {
        private readonly ContentRegistry _content;
        private readonly List<BuildingTypeData> _byId = new List<BuildingTypeData> { null };

        public BuildingTypeRegistry(ContentRegistry content) => _content = content ?? throw new ArgumentNullException(nameof(content));

        public BuildingTypeId Register(BuildingTypeData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.SizeX < 1 || data.SizeY < 1) throw new ArgumentException($"Gebäude '{data.Key}': Footprint muss ≥ 1 sein.");
            if (data.BuildWorkYears <= 0f) throw new ArgumentException($"Gebäude '{data.Key}': BuildWorkYears muss > 0 sein.");
            int id = _content.Register(ContentKind.Building, data.Key);
            while (_byId.Count <= id) _byId.Add(null);
            _byId[id] = data;
            return new BuildingTypeId(id);
        }

        public bool TryGet(BuildingTypeId id, out BuildingTypeData data)
        {
            data = id.Value > 0 && id.Value < _byId.Count ? _byId[id.Value] : null;
            return data != null;
        }

        public BuildingTypeData Get(BuildingTypeId id) => TryGet(id, out var d) ? d : throw new ArgumentException($"Unbekannter Gebäudetyp {id}.");

        public bool TryGetByKey(string key, out BuildingTypeId id)
        {
            if (key != null && _content.TryGetId(ContentKind.Building, key, out int v) && v < _byId.Count && _byId[v] != null)
            {
                id = new BuildingTypeId(v);
                return true;
            }
            id = default;
            return false;
        }

        public string KeyOf(BuildingTypeId id) => TryGet(id, out var d) ? d.Key : null;

        public IEnumerable<BuildingTypeId> All()
        {
            for (int i = 1; i < _byId.Count; i++) if (_byId[i] != null) yield return new BuildingTypeId(i);
        }
    }

    public sealed class TechData
    {
        public string Key;                   // z. B. "tech.masonry"
        public string DisplayName;
        public Epoch Epoch;
        public float Cost;                   // Forschungspunkte
        public string[] Prerequisites = Array.Empty<string>();
        public float FoodYieldBonus;         // +Anteil auf Nahrungserträge
        public float ProductionBonus;        // +Anteil auf Rohstoff-/Verarbeitungserträge
        public float DeathRateReduction;     // −Anteil auf Sterberaten
    }

    public sealed class TechRegistry
    {
        private readonly ContentRegistry _content;
        private readonly List<TechData> _byId = new List<TechData> { null };
        private TechId[][] _prereqs = Array.Empty<TechId[]>();
        private bool _resolved;

        public TechRegistry(ContentRegistry content) => _content = content ?? throw new ArgumentNullException(nameof(content));

        public int Count => _byId.Count - 1;

        public TechId Register(TechData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Cost <= 0f) throw new ArgumentException($"Technologie '{data.Key}': Cost muss > 0 sein.");
            int id = _content.Register(ContentKind.Tech, data.Key);
            while (_byId.Count <= id) _byId.Add(null);
            _byId[id] = data;
            _resolved = false;
            return new TechId(id);
        }

        public bool TryGet(TechId id, out TechData data)
        {
            data = id.Value > 0 && id.Value < _byId.Count ? _byId[id.Value] : null;
            return data != null;
        }

        public TechData Get(TechId id) => TryGet(id, out var d) ? d : throw new ArgumentException($"Unbekannte Technologie {id}.");

        public bool TryGetByKey(string key, out TechId id)
        {
            if (key != null && _content.TryGetId(ContentKind.Tech, key, out int v) && v < _byId.Count && _byId[v] != null)
            {
                id = new TechId(v);
                return true;
            }
            id = default;
            return false;
        }

        public string KeyOf(TechId id) => TryGet(id, out var d) ? d.Key : null;

        public IReadOnlyList<TechId> PrerequisitesOf(TechId id)
        {
            Resolve();
            return id.Value > 0 && id.Value < _prereqs.Length && _prereqs[id.Value] != null ? _prereqs[id.Value] : Array.Empty<TechId>();
        }

        public IEnumerable<TechId> All()
        {
            for (int i = 1; i < _byId.Count; i++) if (_byId[i] != null) yield return new TechId(i);
        }

        /// <summary>Löst Voraussetzungs-Keys einmal auf. Unbekannter Key → Konfigurationsfehler beim Start.</summary>
        public void Resolve()
        {
            if (_resolved) return;
            _prereqs = new TechId[_byId.Count][];
            for (int i = 1; i < _byId.Count; i++)
            {
                var d = _byId[i];
                if (d == null) continue;
                var list = new TechId[d.Prerequisites.Length];
                for (int p = 0; p < list.Length; p++)
                {
                    if (!TryGetByKey(d.Prerequisites[p], out list[p]))
                        throw new InvalidOperationException($"Technologie '{d.Key}': unbekannte Voraussetzung '{d.Prerequisites[p]}'.");
                }
                _prereqs[i] = list;
            }
            _resolved = true;
        }
    }

    /// <summary>
    /// Inhaltstabellen der Zivilisationsphase: Zivil-Einheitentypen, Gebäude, Technologien.
    /// Registrierung einmal beim Start (GameComposition contentSetup bzw. CivModule.Install).
    /// </summary>
    public sealed class CivContent
    {
        public const string UnitWorker = "unit.worker";
        public const string UnitSettler = "unit.settler";
        public const string UnitCaravan = "unit.caravan";

        public const string TownCenter = "building.town_center";

        public BuildingTypeRegistry Buildings { get; }
        public TechRegistry Techs { get; }
        public UnitTypeId WorkerType { get; private set; }
        public UnitTypeId SettlerType { get; private set; }
        public UnitTypeId CaravanType { get; private set; }
        public BuildingTypeId TownCenterType { get; private set; }

        public CivContent(ContentRegistry content)
        {
            Buildings = new BuildingTypeRegistry(content);
            Techs = new TechRegistry(content);
        }

        public void RegisterDefaults(UnitTypeRegistry unitTypes)
        {
            // Bereits registrierte Typen (z. B. aus contentSetup oder Tests) werden übernommen, nicht doppelt angelegt.
            WorkerType = Unit(unitTypes, new UnitTypeData { Key = UnitWorker, DisplayName = "Arbeiter", Role = UnitRole.Worker, Movement = MovementClass.Foot, MaxHp = 60f, MaxSpeed = 1.4f, Radius = 0.35f });
            SettlerType = Unit(unitTypes, new UnitTypeData { Key = UnitSettler, DisplayName = "Siedler", Role = UnitRole.Settler, Movement = MovementClass.Foot, MaxHp = 80f, MaxSpeed = 1.3f, Radius = 0.45f });
            CaravanType = Unit(unitTypes, new UnitTypeData { Key = UnitCaravan, DisplayName = "Karawane", Role = UnitRole.Trader, Movement = MovementClass.Foot, MaxHp = 120f, MaxSpeed = 1.6f, Radius = 0.8f });

            // ---- Technologien (Frühzeit → Moderne) ----
            T("tech.fishing", "Fischfang", Epoch.Primitive, 25);
            T("tech.masonry", "Steinbearbeitung", Epoch.Primitive, 30);
            T("tech.writing", "Schrift", Epoch.Ancient, 60, "tech.masonry");
            T("tech.mining", "Bergbau", Epoch.Ancient, 70, "tech.masonry");
            T("tech.metallurgy", "Metallurgie", Epoch.Ancient, 110, "tech.mining");
            T("tech.construction", "Bauwesen", Epoch.Medieval, 180, "tech.writing", "tech.masonry");
            T("tech.toolmaking", "Werkzeugbau", Epoch.Medieval, 170, "tech.metallurgy");
            T("tech.crop_rotation", "Fruchtwechsel", Epoch.Medieval, 200, "tech.writing").FoodYieldBonus = 0.3f;
            var ind = T("tech.industrialization", "Industrialisierung", Epoch.Industrial, 450, "tech.construction", "tech.toolmaking");
            ind.ProductionBonus = 0.4f;
            T("tech.medicine", "Medizin", Epoch.Industrial, 400, "tech.writing", "tech.construction").DeathRateReduction = 0.35f;
            T("tech.combustion", "Verbrennungsmotor", Epoch.Modern, 900, "tech.industrialization");
            T("tech.modern_warfare", "Moderne Kriegsführung", Epoch.Modern, 1100, "tech.combustion");
            Techs.Resolve();

            // ---- Gebäude ----
            TownCenterType = B(new BuildingTypeData
            {
                Key = TownCenter, DisplayName = "Ortskern", Category = BuildingCategory.Center, SizeX = 2, SizeY = 2,
                Housing = 8, Storage = 150, BuildWorkYears = 0.01f, MaxPerSettlement = 1,
            });
            B(new BuildingTypeData { Key = "building.hut", DisplayName = "Hütte", Category = BuildingCategory.Housing, Housing = 6, BuildWorkYears = 1.5f, Cost = new ResourceCost { Wood = 15 } });
            B(new BuildingTypeData { Key = "building.house", DisplayName = "Steinhaus", Category = BuildingCategory.Housing, Housing = 12, BuildWorkYears = 3f, Cost = new ResourceCost { Wood = 15, Stone = 20 }, RequiredTechKey = "tech.construction", MinTier = SettlementTier.Village });
            B(new BuildingTypeData { Key = "building.apartment", DisplayName = "Mietshaus", Category = BuildingCategory.Housing, Housing = 40, SizeX = 2, SizeY = 2, BuildWorkYears = 8f, Cost = new ResourceCost { Stone = 60, Metal = 20, Tools = 5 }, RequiredTechKey = "tech.industrialization", MinTier = SettlementTier.City });
            B(new BuildingTypeData { Key = "building.farm", DisplayName = "Farm", Category = BuildingCategory.Supply, SizeX = 2, SizeY = 2, BuildWorkYears = 1.5f, Cost = new ResourceCost { Wood = 20 }, Job = JobKind.Farmer, JobSlots = 4, Produces = ResourceType.Food, OutputPerWorkerYear = 3.6f, UsesFertility = true, CatchmentRadius = 2 });
            B(new BuildingTypeData { Key = "building.fishery", DisplayName = "Fischerhütte", Category = BuildingCategory.Supply, BuildWorkYears = 1.5f, Cost = new ResourceCost { Wood = 20 }, Job = JobKind.Fisher, JobSlots = 3, Produces = ResourceType.Food, OutputPerWorkerYear = 3.2f, NeedsWater = true, CatchmentRadius = 4, RequiredTechKey = "tech.fishing" });
            B(new BuildingTypeData { Key = "building.woodcutter", DisplayName = "Holzfäller", Category = BuildingCategory.Production, BuildWorkYears = 1f, Cost = new ResourceCost { Wood = 5 }, Job = JobKind.Woodcutter, JobSlots = 3, Produces = ResourceType.Wood, OutputPerWorkerYear = 6f, HarvestFrom = ResourceType.Wood, CatchmentRadius = 7 });
            B(new BuildingTypeData { Key = "building.quarry", DisplayName = "Steinbruch", Category = BuildingCategory.Production, BuildWorkYears = 2f, Cost = new ResourceCost { Wood = 25 }, Job = JobKind.Quarrier, JobSlots = 3, Produces = ResourceType.Stone, OutputPerWorkerYear = 4f, HarvestFrom = ResourceType.Stone, CatchmentRadius = 7, RequiredTechKey = "tech.masonry" });
            B(new BuildingTypeData { Key = "building.mine", DisplayName = "Mine", Category = BuildingCategory.Production, BuildWorkYears = 3f, Cost = new ResourceCost { Wood = 30, Stone = 15 }, Job = JobKind.Miner, JobSlots = 4, Produces = ResourceType.Ore, OutputPerWorkerYear = 3f, HarvestFrom = ResourceType.Ore, CatchmentRadius = 7, RequiredTechKey = "tech.mining" });
            B(new BuildingTypeData { Key = "building.gold_mine", DisplayName = "Goldmine", Category = BuildingCategory.Production, BuildWorkYears = 3f, Cost = new ResourceCost { Wood = 30, Stone = 20 }, Job = JobKind.Miner, JobSlots = 3, Produces = ResourceType.Gold, OutputPerWorkerYear = 1f, HarvestFrom = ResourceType.Gold, CatchmentRadius = 7, RequiredTechKey = "tech.mining" });
            B(new BuildingTypeData { Key = "building.smelter", DisplayName = "Schmelze", Category = BuildingCategory.Production, BuildWorkYears = 3f, Cost = new ResourceCost { Stone = 30, Wood = 10 }, Job = JobKind.Smelter, JobSlots = 3, Produces = ResourceType.Metal, OutputPerWorkerYear = 2f, Input1 = ResourceType.Ore, Input1PerOutput = 2f, Input2 = ResourceType.Wood, Input2PerOutput = 1f, RequiredTechKey = "tech.metallurgy", MinTier = SettlementTier.Village });
            B(new BuildingTypeData { Key = "building.workshop", DisplayName = "Werkstatt", Category = BuildingCategory.Production, BuildWorkYears = 3f, Cost = new ResourceCost { Wood = 25, Stone = 20 }, Job = JobKind.Toolmaker, JobSlots = 3, Produces = ResourceType.Tools, OutputPerWorkerYear = 2f, Input1 = ResourceType.Metal, Input1PerOutput = 0.5f, Input2 = ResourceType.Wood, Input2PerOutput = 0.5f, RequiredTechKey = "tech.toolmaking", MinTier = SettlementTier.Village });
            B(new BuildingTypeData { Key = "building.oil_well", DisplayName = "Ölförderung", Category = BuildingCategory.Production, BuildWorkYears = 4f, Cost = new ResourceCost { Metal = 30, Tools = 10 }, Job = JobKind.OilWorker, JobSlots = 3, Produces = ResourceType.Oil, OutputPerWorkerYear = 3f, HarvestFrom = ResourceType.Oil, CatchmentRadius = 6, RequiredTechKey = "tech.combustion" });
            B(new BuildingTypeData { Key = "building.storehouse", DisplayName = "Lagerhaus", Category = BuildingCategory.Storage, BuildWorkYears = 1.5f, Cost = new ResourceCost { Wood = 25 }, Storage = 250 });
            B(new BuildingTypeData { Key = "building.warehouse", DisplayName = "Großlager", Category = BuildingCategory.Storage, SizeX = 2, SizeY = 2, BuildWorkYears = 4f, Cost = new ResourceCost { Wood = 30, Stone = 40 }, Storage = 800, RequiredTechKey = "tech.construction", MinTier = SettlementTier.Town });
            B(new BuildingTypeData { Key = "building.library", DisplayName = "Bibliothek", Category = BuildingCategory.Research, BuildWorkYears = 3f, Cost = new ResourceCost { Wood = 20, Stone = 30 }, Job = JobKind.Scholar, JobSlots = 3, ResearchPerWorkerYear = 1.5f, RequiredTechKey = "tech.writing", MinTier = SettlementTier.Village, MaxPerSettlement = 2 });
            B(new BuildingTypeData { Key = "building.palisade", DisplayName = "Palisade", Category = BuildingCategory.Defense, BuildWorkYears = 2f, Cost = new ResourceCost { Wood = 40 }, Job = JobKind.Guard, JobSlots = 2, Defense = 10f, MinTier = SettlementTier.Village, MaxPerSettlement = 1 });
            B(new BuildingTypeData { Key = "building.stone_wall", DisplayName = "Steinmauer", Category = BuildingCategory.Defense, SizeX = 2, SizeY = 1, BuildWorkYears = 5f, Cost = new ResourceCost { Stone = 80 }, Job = JobKind.Guard, JobSlots = 3, Defense = 30f, RequiredTechKey = "tech.construction", MinTier = SettlementTier.Town, MaxPerSettlement = 2 });
            B(new BuildingTypeData { Key = "building.barracks", DisplayName = "Kaserne", Category = BuildingCategory.Defense, SizeX = 2, SizeY = 2, BuildWorkYears = 4f, Cost = new ResourceCost { Wood = 40, Stone = 40 }, Job = JobKind.Guard, JobSlots = 4, Defense = 15f, RequiredTechKey = "tech.metallurgy", MinTier = SettlementTier.Village, MaxPerSettlement = 1 });

            // Prüfen, dass jede Tech-Abhängigkeit der Gebäude existiert.
            foreach (var b in Buildings.All())
            {
                var d = Buildings.Get(b);
                if (d.RequiredTechKey != null && !Techs.TryGetByKey(d.RequiredTechKey, out _))
                    throw new InvalidOperationException($"Gebäude '{d.Key}': unbekannte Technologie '{d.RequiredTechKey}'.");
            }
        }

        private static UnitTypeId Unit(UnitTypeRegistry types, UnitTypeData data) =>
            types.TryGetByKey(data.Key, out var existing) ? existing : types.Register(data);

        private TechData T(string key, string name, Epoch epoch, float cost, params string[] prereqs)
        {
            var d = new TechData { Key = key, DisplayName = name, Epoch = epoch, Cost = cost, Prerequisites = prereqs ?? Array.Empty<string>() };
            Techs.Register(d);
            return d;
        }

        private BuildingTypeId B(BuildingTypeData d) => Buildings.Register(d);
    }
}
