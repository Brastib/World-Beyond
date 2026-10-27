using System;
using System.Collections.Generic;
using WorldBeyond.Core;

namespace WorldBeyond.Simulation.Military
{
    // Explizite Werte, gespeichert. Nur anhängen.
    public enum ArmyOrderKind : byte
    {
        None = 0,
        Move = 1,
        AttackUnit = 2,
        AttackPosition = 3,
        AttackSettlement = 4,
        Defend = 5,
        Retreat = 6,
    }

    public enum ArmyPosture : byte
    {
        Idle = 0,
        Marching = 1,
        Engaged = 2,
        Retreating = 3,
        Defending = 4,
    }

    /// <summary>Auftrag einer Armee als Daten (ARCHITECTURE §11.1). TargetRef = Siedlungs-ID bei Settlement-Aufträgen.</summary>
    [Serializable]
    public struct ArmyOrder
    {
        public ArmyOrderKind Kind;
        public EntityId TargetUnit;
        public int TargetRef;
        public float X, Z;
    }

    public readonly struct ArmyView
    {
        public readonly int Id;
        public readonly int CivId;
        public readonly int UnitCount;
        public readonly float CenterX, CenterZ;
        public readonly ArmyOrder Order;
        public readonly ArmyPosture Posture;
        public readonly float Strength;
        public readonly bool UnderManualControl;

        public ArmyView(int id, int civId, int unitCount, float cx, float cz, ArmyOrder order, ArmyPosture posture, float strength, bool manual)
        {
            Id = id; CivId = civId; UnitCount = unitCount; CenterX = cx; CenterZ = cz;
            Order = order; Posture = posture; Strength = strength; UnderManualControl = manual;
        }
    }

    /// <summary>SYSTEM_INTERFACES §9. Armee-IDs: int, fortlaufend, nie wiederverwendet.</summary>
    public interface IArmyRegistry
    {
        int CreateArmy(int civId, IReadOnlyList<EntityId> units);   // 0 bei leerer/ungültiger Liste
        void OrderMove(int armyId, float x, float z);
        void OrderAttack(int armyId, EntityId targetUnit);
        void OrderAttackSettlement(int armyId, int settlementId);
        void OrderDefend(int armyId, int settlementId);
        void OrderRetreat(int armyId);
        void Disband(int armyId);
    }

    public interface IArmyQuery
    {
        bool Exists(int armyId);
        IReadOnlyList<ArmyView> GetArmies(int civId);
        bool TryGetArmyOf(EntityId unit, out int armyId);
    }

    public struct ArmyCreatedEvent : ISimEvent { public int ArmyId; public int CivId; public int UnitCount; public bool Autonomous; }
    public struct ArmyDisbandedEvent : ISimEvent { public int ArmyId; public int CivId; }
    public struct ArmyOrderChangedEvent : ISimEvent { public int ArmyId; public int CivId; public ArmyOrder Order; public bool ByAI; }

    /// <summary>
    /// Ziel aus Sicht der Kriegsführung. Wird aus IWarQuery.GetTargetPriorities (War) übersetzt; Kachel → Welt
    /// macht der Adapter in GameComposition. OwnerCiv = Besitzer des Ziels.
    /// </summary>
    public readonly struct ArmyObjective
    {
        public readonly float X, Z;
        public readonly float Value;
        public readonly int OwnerCiv;
        public readonly int SettlementId;   // 0 = kein Siedlungsziel

        public ArmyObjective(float x, float z, float value, int ownerCiv, int settlementId = 0)
        {
            X = x; Z = z; Value = value; OwnerCiv = ownerCiv; SettlementId = settlementId;
        }
    }

    /// <summary>
    /// Anschluss der Armee-KI an Civ und War. GameComposition setzt die Delegates; null = Rückfall:
    /// Ziele = nächste feindliche Einheiten, Heimat = Sammelpunkt bei Armeegründung, keine Rekrutierung.
    /// </summary>
    public sealed class ArmyEnvironment
    {
        public Func<int, IReadOnlyList<ArmyObjective>> Objectives;          // IWarQuery.GetTargetPriorities
        public Func<int, (bool ok, float x, float z)> HomeOf;                // Hauptstadt der Civ
        public Func<int, (bool ok, float x, float z)> SettlementPosition;   // Siedlungsmittelpunkt
        public Func<int, MilitaryRole, int, int> Recruit;                    // IRecruitment.Request → Anzahl eingereiht
        public Func<int, int> PendingRecruits;                               // eingereihte, noch nicht fertige Militäraufträge je Civ (Civ-Warteschlange)
        public Func<int, float> RecruitWillingness;                          // 0..1: Bereitschaft der Civ aufzurüsten (Kosten, Unterhalt, Kriegsmüdigkeit); null = 1
        public float Superiority = 1.5f;                                     // PLATZHALTER bis Persönlichkeit/War: gewünschte Überlegenheit gegenüber feindlicher Stärke
        public int MinAttackUnits = 4;                                       // PLATZHALTER: kleine Eroberungstruppe, wenn der Feind keine Truppen hat

        // Besetzung von Siedlungen (SettlementCaptureSystem)
        public Func<int, int, bool> CaptureSettlement;                       // (settlementId, newCivId) → ISettlementRegistry.TransferSettlement
        public Func<IReadOnlyList<ArmyObjective>> Settlements;              // alle Siedlungen (Id, Besitzer, Mitte); null = Siedlungsziele aus Objectives
        public float CaptureRadius = 30f;                                    // Ortskern-Radius in m
        public float CaptureHoldSeconds = 30f;                               // so lange halten, ohne lebenden Verteidiger
    }
}
