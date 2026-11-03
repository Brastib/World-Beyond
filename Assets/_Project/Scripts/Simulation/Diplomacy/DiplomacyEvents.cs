using WorldBeyond.Core;

namespace WorldBeyond.Simulation.Diplomacy
{
    // Sender: DiplomacyService. Empfänger: Military (IArmyAI), UI (Ereignis-Feed, Übersicht), Audio.

    public struct WarDeclaredEvent : ISimEvent { public int WarId; public int Attacker; public int Defender; public CasusBelli CasusBelli; }

    // Eine Civ tritt einem bestehenden Krieg bei (Bündnispflicht).
    public struct WarJoinedEvent : ISimEvent { public int WarId; public int CivId; public WarSide Side; }

    // Eine Civ verlässt einen Krieg, der ohne sie weiterläuft (Separatfrieden).
    public struct WarLeftEvent : ISimEvent { public int WarId; public int CivId; }

    public struct WarEndedEvent : ISimEvent { public int WarId; public WarEndReason Reason; public float FinalWarScore; }

    public struct PeaceSignedEvent : ISimEvent { public int WarId; public int CivA; public int CivB; public PeaceTermType Term; public int SettlementId; }

    public struct PeaceRejectedEvent : ISimEvent { public int WarId; public int Proposer; public int Receiver; public float AcceptScore; }

    public struct TruceSignedEvent : ISimEvent { public int TreatyId; public int CivA; public int CivB; public long EndTick; public int SuspendedWarId; }

    // Waffenstillstand abgelaufen, der ruhende Krieg läuft weiter.
    public struct WarResumedEvent : ISimEvent { public int WarId; public int TreatyId; }

    public struct AllianceFormedEvent : ISimEvent { public int TreatyId; public int CivA; public int CivB; }

    public struct TreatyBrokenEvent : ISimEvent { public int TreatyId; public int BreakerCiv; public int OtherCiv; public TreatyType Type; }

    public struct StrategicPointCapturedEvent : ISimEvent { public int PointId; public int OldController; public int NewController; }

    // Jede KI-Entscheidung mit Wirkung (für Ereignis-Feed). Details: IDiplomacyQuery.RecentDecisions.
    public struct DiplomaticDecisionEvent : ISimEvent { public int CivId; public int TargetCiv; public DiplomaticActionType Action; public float Utility; }
}
