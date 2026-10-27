using System.Collections.Generic;

namespace WorldBeyond.Simulation.Diplomacy
{
    // Lesemodell für die Diplomatie-Übersicht der UI (Phase 6): aktive Kriege, Bündnisse, Feindschaften.
    // Enthält nur Daten, keine Unity-Typen. Die UI baut es bei Bedarf neu (z. B. bei Diplomatie-Events).
    public sealed class DiplomacyOverview
    {
        public sealed class WarEntry
        {
            public int WarId;
            public List<int> Attackers = new List<int>();
            public List<int> Defenders = new List<int>();
            public float WarScore;                 // aus Sicht der Angreifer
            public float AttackerExhaustion;
            public float DefenderExhaustion;
            public int AttackerUnitsLost;
            public int DefenderUnitsLost;
            public int AttackerSettlementsCaptured;
            public int DefenderSettlementsCaptured;
            public float DurationSeconds;
            public bool Suspended;
            public CasusBelli CasusBelli;
            public List<WarGoal> Goals = new List<WarGoal>();
        }

        public struct PairEntry
        {
            public int CivA;
            public int CivB;
            public DiplomaticStatus Status;
            public float OpinionAtoB;
            public float OpinionBtoA;
        }

        public readonly List<WarEntry> Wars = new List<WarEntry>();
        public readonly List<PairEntry> Alliances = new List<PairEntry>();
        public readonly List<PairEntry> Hostilities = new List<PairEntry>();   // Spannung oder Krieg
        public readonly List<PairEntry> AllPairs = new List<PairEntry>();

        public static DiplomacyOverview Build(DiplomacyService d, long nowTick)
        {
            var o = new DiplomacyOverview();
            foreach (var w in d.ActiveWars)
            {
                var e = new WarEntry
                {
                    WarId = w.Id, WarScore = w.WarScore, Suspended = w.Suspended, CasusBelli = w.CasusBelli,
                    AttackerExhaustion = w.AttackerStats.Exhaustion, DefenderExhaustion = w.DefenderStats.Exhaustion,
                    AttackerUnitsLost = w.AttackerStats.UnitsLost, DefenderUnitsLost = w.DefenderStats.UnitsLost,
                    AttackerSettlementsCaptured = w.AttackerStats.SettlementsCaptured, DefenderSettlementsCaptured = w.DefenderStats.SettlementsCaptured,
                    DurationSeconds = (nowTick - w.StartTick) / (float)SimClock.TicksPerSecond,
                };
                e.Attackers.AddRange(w.Attackers);
                e.Defenders.AddRange(w.Defenders);
                e.Goals.AddRange(w.Goals);
                o.Wars.Add(e);
            }

            var civs = d.World.CivIds;
            for (int i = 0; i < civs.Count; i++)
            {
                for (int j = i + 1; j < civs.Count; j++)
                {
                    int a = civs[i], b = civs[j];
                    var p = new PairEntry { CivA = a, CivB = b, Status = d.GetStatus(a, b), OpinionAtoB = d.GetOpinion(a, b), OpinionBtoA = d.GetOpinion(b, a) };
                    o.AllPairs.Add(p);
                    if (p.Status == DiplomaticStatus.Alliance) o.Alliances.Add(p);
                    else if (p.Status == DiplomaticStatus.Tension || p.Status == DiplomaticStatus.War) o.Hostilities.Add(p);
                }
            }
            return o;
        }
    }
}
