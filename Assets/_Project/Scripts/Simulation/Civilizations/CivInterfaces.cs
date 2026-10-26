using System.Collections.Generic;
using WorldBeyond.Core;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Simulation.Civilizations
{
    // Verträge der Civ-Phase (SYSTEM_INTERFACES §7). Besitzer: Phase 3 (Civ).
    // Abweichungen zur Doku-Fassung sind in Docs/drafts/CIVILIZATION_DESIGN_DRAFT.md §14 begründet.

    public interface ICivilizationQuery
    {
        bool Exists(int civId);                       // existiert und lebt
        CivilizationView Get(int civId);              // ungültige ID → default
        bool TryGet(int civId, out CivilizationView view);
        IReadOnlyList<int> AllCivIds { get; }         // lebende Zivilisationen, aufsteigend
    }

    public interface ICivilizationRegistry : ICivilizationQuery
    {
        // Gründet die Zivilisation mit Hauptstadt an (startX, startY). 0 = abgelehnt (Grund über TryCreate).
        int Create(string name, CivColor color, ulong cultureSeed, int startX, int startY);
        bool TryCreate(string name, CivColor color, ulong cultureSeed, int startX, int startY, out int civId, out string reason);
        void Destroy(int civId);                      // CivilizationCollapsedEvent; Siedlungen werden aufgegeben
    }

    public interface ISettlementQuery
    {
        bool Exists(int settlementId);
        SettlementView Get(int settlementId);
        bool TryGet(int settlementId, out SettlementView view);
        IReadOnlyList<int> GetByCiv(int civId);       // aufsteigend; unbekannte Civ → leere Liste
        IReadOnlyList<int> AllSettlementIds { get; }
        int FindAt(int x, int y);                     // Siedlung, deren Ortskern oder Gebäude auf der Kachel steht; 0 = keine
        void CopyBuildings(int settlementId, List<EntityId> results);
    }

    public interface ISettlementRegistry : ISettlementQuery
    {
        int Found(int civId, int x, int y);           // SettlementFoundedEvent; 0 = abgelehnt
        bool TryFound(int civId, int x, int y, int initialPopulation, out int settlementId, out string reason);
        bool CanFoundAt(int civId, int x, int y, out string reason);
        void TransferSettlement(int settlementId, int newCivId);   // SettlementCapturedEvent (Eroberung, Friedensvertrag)
    }

    public interface ITerritoryService
    {
        // Einzige Wahrheit: TileData.OwnerCivId. Geschrieben nur hier, über IWorldGrid.SetOwner.
        int OwnerAt(int x, int y);
        int SharedBorderLength(int civA, int civB);   // Kachelkanten zwischen beiden Gebieten
        int TerritoryTiles(int civId);
        void MarkDirty(int settlementId);             // gebündelte Neuberechnung (TerritorySystem)
    }

    public interface IEconomy
    {
        int GetStock(int civId, ResourceType r);                 // Summe aller Siedlungslager, abgerundet
        bool TrySpend(int civId, in ResourceCost cost);          // atomar: Hauptstadt zuerst, dann nach Vorrat; false ohne Abbuchung
        bool TryConsume(int civId, ResourceType r, int amount);  // Ein-Gut-Variante von TrySpend (Truppenunterhalt)
        void Add(int civId, ResourceType r, int amount);         // bucht in die Hauptstadt (über Lagergrenze hinaus verfällt)
        int GetSettlementStock(int settlementId, ResourceType r);
        bool TrySpendAt(int settlementId, in ResourceCost cost); // nur aus diesem Lager
    }

    public interface ITechTree
    {
        bool IsUnlocked(int civId, TechId tech);
        bool IsUnlocked(int civId, string techKey);              // null/leer = immer frei
        Epoch CurrentEpoch(int civId);
        void SetResearch(int civId, TechId tech);                // nur erforschbare Techs; sonst ignoriert
        void ForceEpoch(int civId, Epoch epoch);                 // God-Modus: schaltet alle Techs bis einschließlich epoch frei
        bool TryGetTech(string techKey, out TechId tech);
    }

    // Rekrutierung: einziger Produktionsweg für alle Einheiten (INTEGRATION.md §5).
    // Military trägt Militärtypen über RegisterRecruitable ein und ruft Request aus der Armee-KI auf.
    public enum RecruitResult { Queued = 0, NotEnoughResources = 1, NotEnoughPopulation = 2, TechMissing = 3, NoProductionBuilding = 4, InvalidRequest = 5 }

    public sealed class RecruitableUnitData
    {
        public string UnitTypeKey;            // muss in UnitTypeRegistry existieren
        public ResourceCost Cost;             // je Einheit
        public int PopulationCost = 1;        // Erwachsene, die die Siedlung verlassen
        public string RequiredTechKey;        // null = frei
        public string RequiredBuildingKey;    // null = Ortskern genügt
        public float BuildYears = 0.25f;      // Produktionszeit je Einheit
    }

    public interface IRecruitment
    {
        RecruitResult Request(int civId, string unitTypeKey, int count, int settlementId);
        void RegisterRecruitable(RecruitableUnitData data);      // nur beim Start (contentSetup)
        bool TryGetRecruitable(string unitTypeKey, out RecruitableUnitData data);
    }

    /// <summary>Detailstufe der Simulation: Siedlungen im Fokus (Kamera, Auswahl) laufen im Normaltakt, alle anderen abstrahiert.</summary>
    public interface ICivSimulationLod
    {
        void SetFocus(float worldX, float worldZ, float radiusMeters);   // von der Darstellung, z. B. Kameramitte
        void ClearFocus();
        void SetSelectedSettlement(int settlementId);                     // 0 = keine; ausgewählte Siedlung ist immer aktiv
    }

    /// <summary>Letzte begründete KI-Entscheidungen je Zivilisation (UI: "Warum tut die Civ das?").</summary>
    public interface ICivDecisionLog
    {
        void CopyRecent(int civId, List<CivDecisionEvent> results);
    }

    /// <summary>Kriegsbewerter aus Phase 4. Wird vom CivAISystem pro Entscheidungsrunde aufgerufen.</summary>
    public interface ICivWarEvaluator
    {
        void Evaluate(SimContext ctx, int civId);
    }
}
