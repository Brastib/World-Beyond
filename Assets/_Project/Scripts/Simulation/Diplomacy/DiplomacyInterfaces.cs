using System.Collections.Generic;

namespace WorldBeyond.Simulation.Diplomacy
{
    // Einzige Beziehungsmatrix des Spiels (INTEGRATION.md §3, Besitzer: War / Phase 4).
    public interface IDiplomacyService
    {
        int GetRelation(int civA, int civB);                        // −100 … +100, Meinung von A über B
        DiplomaticStatus GetStatus(int civA, int civB);
        void ModifyRelation(int civA, int civB, int delta, RelationReason reason);
        bool DeclareWar(int attacker, int defender);                // WarDeclaredEvent
        bool MakePeace(int civA, int civB);                         // PeaceSignedEvent (weißer Frieden, erzwungen)
        bool FormAlliance(int civA, int civB);                      // AllianceFormedEvent
        bool SignTruce(int civA, int civB, int durationTicks);      // TruceSignedEvent
    }

    public interface IDiplomacyQuery
    {
        float GetOpinion(int civA, int civB);
        DiplomaticStatus GetStatus(int civA, int civB);
        IReadOnlyList<OpinionModifier> GetModifiers(int civA, int civB);
        IReadOnlyList<Treaty> GetTreaties(int civId);               // nur aktive
        IReadOnlyList<int> GetAllies(int civId);
        IReadOnlyList<DiplomaticDecision> RecentDecisions { get; }  // neueste zuletzt
        IReadOnlyList<StrategicPoint> StrategicPoints { get; }
    }

    public interface IWarQuery
    {
        IReadOnlyList<War> ActiveWars { get; }                      // inkl. ruhender (Suspended) Kriege
        War GetWar(int warId);                                      // null, wenn unbekannt
        bool AreAtWar(int civA, int civB);                          // false bei ruhendem Krieg
        War FindWar(int civA, int civB);                            // aktiver oder ruhender Krieg zwischen beiden, sonst null
        // Für Military IArmyAI: Ziele der Civ, absteigend nach Wert.
        IReadOnlyList<TargetPriority> GetTargetPriorities(int civId);
    }

    // Was Phase 4 von Welt, Zivilisationen, Wirtschaft und Militär braucht.
    // Implementiert wird es im GameBootstrap durch einen Adapter auf ICivilizationQuery, ISettlementRegistry,
    // ITerritoryService, IEconomy, IRecruitment, IArmyQuery (SYSTEM_INTERFACES §7, §9).
    // So bleibt Phase 4 ohne fremde Implementierungsdetails testbar und legt keine zweite Version dieser Systeme an.
    public interface IDiplomacyWorldPort
    {
        IReadOnlyList<int> CivIds { get; }                          // lebende Civs, aufsteigend sortiert
        bool IsCivAlive(int civId);
        float GetMilitaryStrength(int civId);                       // Military: GetCivCombatStrength
        float GetUnitCombatStrength(int unitTypeId);                // Military: GetCombatStrength(UnitTypeId); Zivilisten 0 … klein
        int GetPopulation(int civId);
        int GetTerritoryTiles(int civId);                           // ICivilizationQuery.TerritoryTiles (für Abstand der Gebietsränder)
        float GetAggression(int civId);                             // Kulturhaltung 0 (friedlich) … 1 (aggressiv)
        float GetResourceNeed(int civId);                           // 0 … 1, Mangel an Grundressourcen
        int GetSharedBorderLength(int civA, int civB);              // ITerritoryService.SharedBorderLength
        float GetResourceContestion(int civA, int civB);            // 0 … 1, Anteil umkämpfter Ressourcenkacheln an der Grenze
        bool TryGetCapitalPosition(int civId, out int x, out int y);
        IReadOnlyList<int> GetSettlements(int civId);
        bool TryGetSettlement(int settlementId, out SettlementInfo info);
        bool TransferSettlement(int settlementId, int newCivId);     // ISettlementRegistry.TransferSettlement
        float TransferTribute(int fromCiv, int toCiv, float fraction); // über IEconomy; liefert übertragene Menge
        bool RequestMilitaryBuildup(int civId, int unitCount, out string reason); // IRecruitment.Request
        int GetDominantCivNear(int x, int y, int radius);           // 0 = niemand oder umkämpft
    }
}
