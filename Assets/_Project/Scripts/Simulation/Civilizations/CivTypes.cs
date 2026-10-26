using System;
using WorldBeyond.Core;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Simulation.Civilizations
{
    /// <summary>
    /// Zeitskala der Zivilisationssimulation. 1 Spieljahr = 60 Spielsekunden = 600 Ticks.
    /// 30 Spielminuten (Abnahme T-3-01) entsprechen damit 30 Spieljahren.
    /// </summary>
    public static class CivTime
    {
        public const int TicksPerYear = 60 * SimClock.TicksPerSecond;
        public const int ActiveUpdateTicks = 10;     // Siedlung in Spielernähe: jede Spielsekunde
        public const int DistantUpdateTicks = 50;    // entfernte Siedlung: alle 5 s mit 5-fachem Zeitschritt
        public static double TicksToYears(long ticks) => (double)ticks / TicksPerYear;
    }

    /// <summary>Farbe einer Zivilisation. Eigener Werttyp, damit Simulation ohne UnityEngine testbar bleibt.</summary>
    [Serializable]
    public struct CivColor : IEquatable<CivColor>
    {
        public byte R, G, B, A;
        public CivColor(byte r, byte g, byte b, byte a = 255) { R = r; G = g; B = b; A = a; }
        public bool Equals(CivColor o) => R == o.R && G == o.G && B == o.B && A == o.A;
        public override bool Equals(object obj) => obj is CivColor o && Equals(o);
        public override int GetHashCode() => (R << 24) | (G << 16) | (B << 8) | A;
        public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
    }

    /// <summary>
    /// Eigenschaften einer Zivilisation als Multiplikatoren (0,7 … 1,3), deterministisch aus dem cultureSeed.
    /// Wirken auf echte Formeln: Fleiß auf Erträge, Fruchtbarkeit auf Geburten, Forschung auf Forschungspunkte,
    /// Expansion auf die Bereitschaft der KI zu siedeln, Militarismus auf Verteidigungsbauten.
    /// </summary>
    [Serializable]
    public struct CivTraits
    {
        public float Industry;
        public float Fertility;
        public float Research;
        public float Expansion;
        public float Militarism;

        public static CivTraits Neutral => new CivTraits { Industry = 1f, Fertility = 1f, Research = 1f, Expansion = 1f, Militarism = 1f };

        public static CivTraits FromSeed(ulong cultureSeed)
        {
            var r = new SimRandom(cultureSeed ^ 0xC1F7A115UL);
            float T() => 0.7f + 0.6f * r.Value01();
            return new CivTraits { Industry = T(), Fertility = T(), Research = T(), Expansion = T(), Militarism = T() };
        }
    }

    // Explizite Werte, nur anhängen.
    public enum Epoch : byte
    {
        Primitive = 0,     // Frühzeit
        Ancient = 1,       // Antike
        Medieval = 2,      // Mittelalter
        Industrial = 3,    // Industrie
        Modern = 4,        // Moderne
    }

    // Explizite Werte, nur anhängen.
    public enum SettlementTier : byte
    {
        Camp = 0,          // Lager
        Village = 1,       // Dorf
        Town = 2,          // Kleinstadt
        City = 3,          // Stadt
        Metropolis = 4,    // Großstadt
    }

    // Explizite Werte, nur anhängen.
    public enum BuildingCategory : byte
    {
        Center = 0,        // Ortskern (Lager + Wohnraum + Verwaltung)
        Housing = 1,
        Supply = 2,        // Nahrung
        Production = 3,    // Holz, Stein, Erz, Verarbeitung
        Storage = 4,
        Defense = 5,
        Research = 6,
    }

    // Explizite Werte, nur anhängen. Arbeitsplatzarten; Builder entsteht aus Baustellen, nicht aus Gebäuden.
    public enum JobKind : byte
    {
        None = 0,
        Farmer = 1,
        Fisher = 2,
        Woodcutter = 3,
        Quarrier = 4,
        Miner = 5,
        Smelter = 6,
        Toolmaker = 7,
        Scholar = 8,
        Guard = 9,
        Builder = 10,
        OilWorker = 11,
    }

    // Explizite Werte, nur anhängen.
    public enum CivEventKind : byte
    {
        Plague = 0,        // Seuche: Sterberate × 4 für 1 Jahr
        Famine = 1,        // Missernte: 60 % Nahrung verdorben, Nahrungsertrag × 0,3 für 1 Jahr
        BabyBoom = 2,      // Geburtenrate × 2 für 1 Jahr
        Fire = 3,          // Brand: ein Gebäude zerstört, 30 % Holz verloren
        GoldRush = 4,      // Goldfund: +Gold, Zuwanderung
    }

    /// <summary>Kosten in Lagergütern. Werttyp ohne Allokation (SYSTEM_INTERFACES §7).</summary>
    [Serializable]
    public struct ResourceCost
    {
        public int Food, Wood, Stone, Ore, Oil, Gold, Metal, Tools;

        public int this[ResourceType r]
        {
            get
            {
                switch (r)
                {
                    case ResourceType.Food: return Food;
                    case ResourceType.Wood: return Wood;
                    case ResourceType.Stone: return Stone;
                    case ResourceType.Ore: return Ore;
                    case ResourceType.Oil: return Oil;
                    case ResourceType.Gold: return Gold;
                    case ResourceType.Metal: return Metal;
                    case ResourceType.Tools: return Tools;
                    default: return 0;
                }
            }
            set
            {
                switch (r)
                {
                    case ResourceType.Food: Food = value; break;
                    case ResourceType.Wood: Wood = value; break;
                    case ResourceType.Stone: Stone = value; break;
                    case ResourceType.Ore: Ore = value; break;
                    case ResourceType.Oil: Oil = value; break;
                    case ResourceType.Gold: Gold = value; break;
                    case ResourceType.Metal: Metal = value; break;
                    case ResourceType.Tools: Tools = value; break;
                    default: throw new ArgumentOutOfRangeException(nameof(r), $"{r} ist kein Lagergut.");
                }
            }
        }

        public bool IsZero => Food == 0 && Wood == 0 && Stone == 0 && Ore == 0 && Oil == 0 && Gold == 0 && Metal == 0 && Tools == 0;

        public ResourceCost Times(int n)
        {
            return new ResourceCost
            {
                Food = Food * n, Wood = Wood * n, Stone = Stone * n, Ore = Ore * n,
                Oil = Oil * n, Gold = Gold * n, Metal = Metal * n, Tools = Tools * n,
            };
        }

        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var r in Resources.Stockable)
            {
                int v = this[r];
                if (v == 0) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(v).Append(' ').Append(r);
            }
            return sb.Length == 0 ? "0" : sb.ToString();
        }
    }

    /// <summary>Hilfen zu Lagergütern. Index in Lagern = (int)ResourceType.</summary>
    public static class Resources
    {
        public const int SlotCount = 9;  // ResourceType 0..8, Slot 0 (None) bleibt leer

        public static readonly ResourceType[] Stockable =
        {
            ResourceType.Food, ResourceType.Wood, ResourceType.Stone, ResourceType.Ore,
            ResourceType.Oil, ResourceType.Gold, ResourceType.Metal, ResourceType.Tools,
        };

        public static bool IsStockable(ResourceType r) => r != ResourceType.None && (int)r < SlotCount;
    }

    /// <summary>Lesesicht einer Zivilisation für UI und KI. Kopie, keine Referenz in den Zustand.</summary>
    public readonly struct CivilizationView
    {
        public readonly int Id;
        public readonly string Name;
        public readonly CivColor Color;
        public readonly CivTraits Traits;
        public readonly bool Alive;
        public readonly int CapitalSettlementId;
        public readonly int SettlementCount;
        public readonly int Population;
        public readonly int MilitaryStrength;   // Zahl lebender Militäreinheiten (UnitRole ≥ Infantry)
        public readonly Epoch Epoch;
        public readonly int TerritoryTiles;
        public readonly ResourceCost Stock;     // Summe aller Lager (abgerundet)
        public readonly string CurrentResearchKey;
        public readonly float ResearchProgress01;

        public CivilizationView(int id, string name, CivColor color, CivTraits traits, bool alive, int capital, int settlementCount,
            int population, int militaryStrength, Epoch epoch, int territoryTiles, ResourceCost stock, string researchKey, float researchProgress)
        {
            Id = id; Name = name; Color = color; Traits = traits; Alive = alive; CapitalSettlementId = capital;
            SettlementCount = settlementCount; Population = population; MilitaryStrength = militaryStrength; Epoch = epoch;
            TerritoryTiles = territoryTiles; Stock = stock; CurrentResearchKey = researchKey; ResearchProgress01 = researchProgress;
        }
    }

    /// <summary>Lesesicht einer Siedlung. Bilanzen sind Jahreswerte aus der letzten Aktualisierung.</summary>
    public readonly struct SettlementView
    {
        public readonly int Id;
        public readonly int CivId;
        public readonly string Name;
        public readonly int X, Y;
        public readonly SettlementTier Tier;
        public readonly int Population, Children, Adults, Elders;
        public readonly int HousingCapacity;
        public readonly int StorageCapacity;
        public readonly ResourceCost Stock;
        public readonly float FoodBalancePerYear;
        public readonly float BirthsPerYear, DeathsPerYear;
        public readonly int Workers, Unemployed;
        public readonly int BuildingCount, ConstructionSites;
        public readonly bool IsActiveLod;
        public readonly int FoundedTickYear;
        public readonly bool IsCapital;

        public SettlementView(int id, int civId, string name, int x, int y, SettlementTier tier, int pop, int children, int adults, int elders,
            int housing, int storage, ResourceCost stock, float foodBalance, float births, float deaths, int workers, int unemployed,
            int buildings, int sites, bool active, int foundedYear, bool isCapital)
        {
            Id = id; CivId = civId; Name = name; X = x; Y = y; Tier = tier; Population = pop; Children = children; Adults = adults; Elders = elders;
            HousingCapacity = housing; StorageCapacity = storage; Stock = stock; FoodBalancePerYear = foodBalance;
            BirthsPerYear = births; DeathsPerYear = deaths; Workers = workers; Unemployed = unemployed;
            BuildingCount = buildings; ConstructionSites = sites; IsActiveLod = active; FoundedTickYear = foundedYear; IsCapital = isCapital;
        }
    }

    // ---------------- Events ----------------

    public struct CivilizationCreatedEvent : ISimEvent { public int CivId; public int CapitalSettlementId; }
    public struct CivilizationCollapsedEvent : ISimEvent { public int CivId; }
    public struct SettlementFoundedEvent : ISimEvent { public int SettlementId; public int CivId; public int X, Y; public bool ByPlayer; }
    public struct SettlementCapturedEvent : ISimEvent { public int SettlementId; public int OldCivId; public int NewCivId; }
    public struct SettlementAbandonedEvent : ISimEvent { public int SettlementId; public int CivId; public int X, Y; }
    public struct SettlementTierChangedEvent : ISimEvent { public int SettlementId; public SettlementTier OldTier; public SettlementTier NewTier; }
    public struct BuildingPlacedEvent : ISimEvent { public EntityId Building; public int SettlementId; public int X, Y; }
    public struct BuildingCompletedEvent : ISimEvent { public EntityId Building; public int SettlementId; public int X, Y; }
    public struct BuildingDestroyedEvent : ISimEvent { public EntityId Building; public int SettlementId; public int X, Y; }
    public struct TechResearchedEvent : ISimEvent { public int CivId; public TechId Tech; public Epoch EpochAfter; }
    public struct EpochChangedEvent : ISimEvent { public int CivId; public Epoch OldEpoch; public Epoch NewEpoch; }
    public struct CivEventTriggeredEvent : ISimEvent { public int SettlementId; public CivEventKind Kind; }
    public struct UnitRecruitedEvent : ISimEvent { public EntityId Unit; public int CivId; public int SettlementId; public string UnitTypeKey; }
    public struct MigrationEvent : ISimEvent { public int FromSettlementId; public int ToSettlementId; public int People; }

    /// <summary>Eine begründete KI-Entscheidung, für den Ereignis-Feed und "Warum tut die Civ das?".</summary>
    public struct CivDecisionEvent : ISimEvent { public int CivId; public int SettlementId; public string Decision; public string Reason; }
}
