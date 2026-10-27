using System.Collections.Generic;

namespace WorldBeyond.Simulation.Diplomacy
{
    // Alle Enums: explizite Werte, nur anhängen (ARCHITECTURE §11). Sie werden als Zahl gespeichert.

    public enum DiplomaticStatus : byte { Neutral = 0, Tension = 1, Trade = 2, Alliance = 3, War = 4, Truce = 5 }

    // Herkunft einer Meinungsänderung. Die UI zeigt sie als "Warum?".
    public enum RelationReason : byte
    {
        Unknown = 0,
        BorderFriction = 1,        // dauerhaft, aus gemeinsamer Grenzlänge neu berechnet
        ResourceCompetition = 2,   // dauerhaft, aus umkämpften Ressourcen neu berechnet
        WarDeclaredOnUs = 3,
        AttackedAlly = 4,
        TreatyBroken = 5,
        CommonEnemy = 6,
        PeaceSigned = 7,
        TerritoryCeded = 8,
        RefusedCallToArms = 9,
        AllianceFormed = 10,
        PlayerIntervention = 11,
        WarDamage = 12,            // Verluste durch den Gegner
        BorderGrievance = 13,      // dauerhaft, wächst mit der Zeit, solange eine Grenze ohne Bündnis besteht
        PeaceSettlement = 14,      // Friedensvertrag: gleicht bestehende Grenz-/Ressourcenreibung aus, klingt langsam ab
    }

    public enum TreatyType : byte { Alliance = 0, Truce = 1, PeaceCooldown = 2 }

    public enum TreatyEndReason : byte { None = 0, Expired = 1, Broken = 2, PeaceSigned = 3, CivCollapsed = 4, WarDeclared = 5 }

    public enum WarGoalType : byte { ConquerSettlement = 0, HoldPoint = 1, Tribute = 2, Humiliate = 3 }

    public enum CasusBelli : byte { None = 0, BorderDispute = 1, ResourceCompetition = 2, Hostility = 3, Expansion = 4, DefendAlly = 5, PlayerForced = 6 }

    public enum WarSide : byte { None = 0, Attacker = 1, Defender = 2 }

    public enum WarEndReason : byte { None = 0, Peace = 1, CivCollapsed = 2, NoParticipantsLeft = 3 }

    public enum PeaceTermType : byte { WhitePeace = 0, CedeSettlement = 1, Tribute = 2 }

    public enum StrategicPointType : byte { Pass = 0, Bridge = 1, ResourceSite = 2, Fort = 3 }

    public enum TargetKind : byte { Settlement = 0, StrategicPoint = 1 }

    public enum DiplomaticActionType : byte
    {
        None = 0, DeclareWar = 1, ProposePeace = 2, ProposeAlliance = 3, Recruit = 4, AcceptPeace = 5, RejectPeace = 6, JoinWar = 7, DeclineCallToArms = 8,
    }

    // ---------- Zustandsdaten (reine Daten, IDs statt Referenzen) ----------

    public sealed class OpinionModifier
    {
        public int Id;                 // nie wiederverwendet
        public int FromCiv;            // wer die Meinung hat
        public int ToCiv;              // über wen
        public RelationReason Reason;
        public float Value;            // aktueller Wert, klingt Richtung 0 ab
        public float DecayPerSecond;   // 0 = kein Abklingen (dauerhafte Modifikatoren)
        public long CreatedTick;
    }

    public sealed class Treaty
    {
        public int Id;                 // nie wiederverwendet
        public TreatyType Type;
        public int CivA;
        public int CivB;
        public long StartTick;
        public long EndTick;           // -1 = unbefristet
        public int SuspendedWarId;     // nur Truce: Krieg, der ruht (0 = keiner)
        public bool Active;
        public TreatyEndReason EndReason;
    }

    public sealed class WarGoal
    {
        public WarGoalType Type;
        public int OwnerCiv;           // wer das Ziel verfolgt
        public int TargetId;           // Siedlung oder strategischer Punkt (0 bei Tribute/Humiliate)
        public bool Fulfilled;
    }

    public sealed class WarSideStats
    {
        public float StrengthLost;          // Summe Kampfstärke verlorener Einheiten
        public int UnitsLost;
        public int SettlementsCaptured;
        public int PopulationCaptured;
        public int SettlementsLost;
        public int PopulationLost;
        public float ResourcesSpent;        // für Rekrutierung im Krieg (gemeldet)
        public float InitialStrength;       // Stärke beim Kriegseintritt
        public int InitialPopulation;
        public float Exhaustion;            // 0 … 100
    }

    public sealed class War
    {
        public int Id;                 // nie wiederverwendet
        public int AttackerLeader;
        public int DefenderLeader;
        public List<int> Attackers = new List<int>();
        public List<int> Defenders = new List<int>();
        public long StartTick;
        public long EndTick = -1;
        public long FirstContactTick = -1;  // erste Feindberührung (Kampftreffer oder Verlust durch den Gegner), -1 = noch keine
        public CasusBelli CasusBelli;
        public List<WarGoal> Goals = new List<WarGoal>();
        public WarSideStats AttackerStats = new WarSideStats();
        public WarSideStats DefenderStats = new WarSideStats();
        public float WarScore;         // −100 … +100 aus Sicht der Angreifer, nur berechnet
        public bool Active = true;
        public bool Suspended;         // Waffenstillstand
        public WarEndReason EndReason;

        public WarSide SideOf(int civId)
        {
            if (Attackers.Contains(civId)) return WarSide.Attacker;
            if (Defenders.Contains(civId)) return WarSide.Defender;
            return WarSide.None;
        }

        public WarSideStats StatsOf(WarSide side) => side == WarSide.Attacker ? AttackerStats : DefenderStats;
        public List<int> MembersOf(WarSide side) => side == WarSide.Attacker ? Attackers : Defenders;

        // warScore aus Sicht der angegebenen Civ (+ = sie gewinnt).
        public float ScoreFor(int civId)
        {
            var side = SideOf(civId);
            if (side == WarSide.Attacker) return WarScore;
            if (side == WarSide.Defender) return -WarScore;
            return 0f;
        }
    }

    public sealed class StrategicPoint
    {
        public int Id;                 // nie wiederverwendet
        public StrategicPointType Type;
        public int X;
        public int Y;
        public float Value;            // Gewicht als Kriegsziel
        public int ControllerCiv;      // 0 = niemand
    }

    public struct PeaceTerm
    {
        public PeaceTermType Type;
        public int GiverCiv;           // wer abtritt oder zahlt (0 bei WhitePeace)
        public int SettlementId;       // nur CedeSettlement
        public float TributeFraction;  // nur Tribute, 0 … 0.5 des Vorrats

        public static PeaceTerm White() => new PeaceTerm { Type = PeaceTermType.WhitePeace };
        public static PeaceTerm Cede(int giver, int settlementId) => new PeaceTerm { Type = PeaceTermType.CedeSettlement, GiverCiv = giver, SettlementId = settlementId };
        public static PeaceTerm Tribute(int giver, float fraction) => new PeaceTerm { Type = PeaceTermType.Tribute, GiverCiv = giver, TributeFraction = fraction };
    }

    public readonly struct PeaceProposalResult
    {
        public readonly bool Accepted;
        public readonly float AcceptScore;
        public readonly string Reason;
        public PeaceProposalResult(bool accepted, float acceptScore, string reason) { Accepted = accepted; AcceptScore = acceptScore; Reason = reason; }
    }

    // KI-Gedächtnis pro Civ (Abklingzeiten), Teil des Spielstands, damit die KI nach dem Laden identisch weiterläuft.
    public sealed class CivAiMemory
    {
        public int CivId;
        public long LastWarTick = long.MinValue / 4;
        public long LastPeaceProposalTick = long.MinValue / 4;
        public long LastAllianceTick = long.MinValue / 4;
        public long LastRecruitTick = long.MinValue / 4;
        // Angenommene, vermutlich noch nicht ausgebildete Rekruten (zählen zur Armee-Obergrenze, bis RecruitPendingTicks vorbei sind).
        public int PendingRecruits;
        public float RecruitWillingness;    // 0 … 1, zuletzt berechnete Bereitschaft zum Aufrüsten (Nutzen − Kosten)
        public long PendingRecruitsTick = long.MinValue / 4;
    }

    public readonly struct DecisionFactor
    {
        public readonly string Name;
        public readonly float Value;
        public DecisionFactor(string name, float value) { Name = name; Value = value; }
    }

    // Protokoll einer KI-Entscheidung, damit Kriege nachvollziehbar sind (UI, Ereignis-Feed, Debug).
    public sealed class DiplomaticDecision
    {
        public long Tick;
        public int CivId;
        public int TargetCiv;
        public DiplomaticActionType Action;
        public float Utility;
        public List<DecisionFactor> Factors = new List<DecisionFactor>();
    }

    public readonly struct TargetPriority
    {
        public readonly TargetKind Kind;
        public readonly int TargetId;
        public readonly int OwnerCiv;
        public readonly int WarId;
        public readonly int X;
        public readonly int Y;
        public readonly float Value;
        public readonly bool IsWarGoal;

        public TargetPriority(TargetKind kind, int targetId, int ownerCiv, int warId, int x, int y, float value, bool isWarGoal)
        {
            Kind = kind; TargetId = targetId; OwnerCiv = ownerCiv; WarId = warId; X = x; Y = y; Value = value; IsWarGoal = isWarGoal;
        }
    }

    // Sicht einer Siedlung, wie Phase 4 sie braucht (über IDiplomacyWorldPort geliefert).
    public readonly struct SettlementInfo
    {
        public readonly int Id;
        public readonly int OwnerCiv;
        public readonly int Population;
        public readonly int X;
        public readonly int Y;
        public readonly bool IsCapital;

        public SettlementInfo(int id, int ownerCiv, int population, int x, int y, bool isCapital)
        {
            Id = id; OwnerCiv = ownerCiv; Population = population; X = x; Y = y; IsCapital = isCapital;
        }
    }
}
