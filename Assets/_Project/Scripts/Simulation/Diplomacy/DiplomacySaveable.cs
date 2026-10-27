using System;
using System.Collections.Generic;

namespace WorldBeyond.Simulation.Diplomacy
{
    // Spielstand-Sektion "diplomacy" (ARCHITECTURE §11). Reine Daten, nur IDs, Enums als feste Zahlen.
    [Serializable]
    public sealed class DiplomacyStateDto
    {
        public int LastWarId;
        public int LastTreatyId;
        public int LastModifierId;
        public int LastPointId;
        public long LastFactorRefreshTick;
        public long LastUpdateTick;
        public List<OpinionModifier> Modifiers = new List<OpinionModifier>();
        public List<Treaty> Treaties = new List<Treaty>();
        public List<War> Wars = new List<War>();
        public List<War> WarHistory = new List<War>();
        public List<StrategicPoint> Points = new List<StrategicPoint>();
        public List<DiplomaticDecision> Decisions = new List<DiplomaticDecision>();
        public List<CivAiMemory> Memory = new List<CivAiMemory>();
    }

    public sealed class DiplomacySaveable : ISaveable, ISaveReferenceCheck
    {
        public const int CurrentSectionVersion = 1;
        readonly DiplomacyService _d;

        public DiplomacySaveable(DiplomacyService diplomacy) { _d = diplomacy; }

        public string SaveKey => "diplomacy";
        public int LoadOrder => 800;             // nach Welt, Einheiten, Zivilisationen und Siedlungen
        public int SectionVersion => CurrentSectionVersion;
        public Type StateType => typeof(DiplomacyStateDto);

        public object CaptureState()
        {
            var dto = new DiplomacyStateDto
            {
                LastWarId = _d.WarIds.LastIssued,
                LastTreatyId = _d.TreatyIds.LastIssued,
                LastModifierId = _d.ModifierIds.LastIssued,
                LastPointId = _d.PointIds.LastIssued,
                LastFactorRefreshTick = _d.LastFactorRefreshTick,
                LastUpdateTick = _d.LastUpdateTick,
            };
            foreach (var m in _d.Modifiers) dto.Modifiers.Add(Copy(m));
            foreach (var t in _d.Treaties) dto.Treaties.Add(Copy(t));
            foreach (var w in _d.Wars) dto.Wars.Add(Copy(w));
            foreach (var w in _d.WarHistory) dto.WarHistory.Add(Copy(w));
            foreach (var p in _d.Points) dto.Points.Add(Copy(p));
            foreach (var x in _d.Decisions) dto.Decisions.Add(Copy(x));
            foreach (var m in _d.Memory) dto.Memory.Add(Copy(m));
            return dto;
        }

        // Prüft zuerst alles, übernimmt erst danach (bei Fehler bleibt der bisherige Zustand).
        public bool TryRestoreState(object state, out string error)
        {
            error = null;
            var dto = state as DiplomacyStateDto;
            if (dto == null) { error = "diplomacy: falscher Datentyp."; return false; }
            if (!Validate(dto, out error)) return false;

            var warIds = new Core.SequentialIdAllocator();
            var treatyIds = new Core.SequentialIdAllocator();
            var modIds = new Core.SequentialIdAllocator();
            var pointIds = new Core.SequentialIdAllocator();
            if (!warIds.TryRestore(dto.LastWarId, out error) || !treatyIds.TryRestore(dto.LastTreatyId, out error)
                || !modIds.TryRestore(dto.LastModifierId, out error) || !pointIds.TryRestore(dto.LastPointId, out error))
            { error = "diplomacy: " + error; return false; }

            _d.WarIds = warIds; _d.TreatyIds = treatyIds; _d.ModifierIds = modIds; _d.PointIds = pointIds;
            _d.LastFactorRefreshTick = dto.LastFactorRefreshTick;
            _d.LastUpdateTick = dto.LastUpdateTick;
            Replace(_d.Modifiers, dto.Modifiers, Copy);
            Replace(_d.Treaties, dto.Treaties, Copy);
            Replace(_d.Wars, dto.Wars, Copy);
            Replace(_d.WarHistory, dto.WarHistory, Copy);
            Replace(_d.Points, dto.Points, Copy);
            Replace(_d.Decisions, dto.Decisions, Copy);
            Replace(_d.Memory, dto.Memory, Copy);
            _d.Memory.Sort((a, b) => a.CivId.CompareTo(b.CivId));
            return true;
        }

        static bool Validate(DiplomacyStateDto dto, out string error)
        {
            error = null;
            if (dto.Modifiers == null || dto.Treaties == null || dto.Wars == null || dto.WarHistory == null
                || dto.Points == null || dto.Decisions == null || dto.Memory == null)
            { error = "diplomacy: Liste fehlt."; return false; }

            if (!UniqueIds(dto.Modifiers, m => m.Id, dto.LastModifierId, "Meinungsmodifikator", out error)) return false;
            if (!UniqueIds(dto.Treaties, t => t.Id, dto.LastTreatyId, "Vertrag", out error)) return false;
            var allWars = new List<War>(dto.Wars); allWars.AddRange(dto.WarHistory);
            if (!UniqueIds(allWars, w => w.Id, dto.LastWarId, "Krieg", out error)) return false;
            if (!UniqueIds(dto.Points, p => p.Id, dto.LastPointId, "Strategischer Punkt", out error)) return false;

            foreach (var t in dto.Treaties)
            {
                if (t.CivA <= 0 || t.CivB <= 0 || t.CivA == t.CivB) { error = $"diplomacy: Vertrag {t.Id} hat ungültige Parteien."; return false; }
                if (!Enum.IsDefined(typeof(TreatyType), t.Type)) { error = $"diplomacy: Vertrag {t.Id} hat unbekannten Typ {(int)t.Type}."; return false; }
                if (!t.Active) { error = $"diplomacy: Vertrag {t.Id} ist inaktiv, aber in der Aktiv-Liste."; return false; }
            }
            foreach (var w in dto.Wars)
            {
                if (w.Attackers == null || w.Defenders == null || w.Goals == null || w.AttackerStats == null || w.DefenderStats == null)
                { error = $"diplomacy: Krieg {w.Id} unvollständig."; return false; }
                if (w.Attackers.Count == 0 || w.Defenders.Count == 0) { error = $"diplomacy: Krieg {w.Id} hat eine leere Seite."; return false; }
                if (!w.Attackers.Contains(w.AttackerLeader) || !w.Defenders.Contains(w.DefenderLeader)) { error = $"diplomacy: Krieg {w.Id}: Kriegsführer nicht in seiner Seite."; return false; }
                foreach (var c in w.Attackers) if (w.Defenders.Contains(c)) { error = $"diplomacy: Krieg {w.Id}: Civ {c} auf beiden Seiten."; return false; }
                if (!w.Active) { error = $"diplomacy: Krieg {w.Id} ist beendet, aber in der Aktiv-Liste."; return false; }
            }
            var memoryCivs = new HashSet<int>();
            foreach (var m in dto.Memory)
            {
                if (m == null || m.CivId <= 0) { error = "diplomacy: ungültiger KI-Gedächtnis-Eintrag."; return false; }
                if (!memoryCivs.Add(m.CivId)) { error = $"diplomacy: KI-Gedächtnis für Civ {m.CivId} doppelt."; return false; }
            }
            foreach (var m in dto.Modifiers)
                if (!Enum.IsDefined(typeof(RelationReason), m.Reason)) { error = $"diplomacy: Modifikator {m.Id} hat unbekannten Grund {(int)m.Reason}."; return false; }
            return true;
        }

        static bool UniqueIds<T>(List<T> items, Func<T, int> id, int lastIssued, string what, out string error)
        {
            error = null;
            var seen = new HashSet<int>();
            foreach (var item in items)
            {
                if (item == null) { error = $"diplomacy: leerer Eintrag ({what})."; return false; }
                int i = id(item);
                if (i <= 0 || i > lastIssued) { error = $"diplomacy: {what}-ID {i} außerhalb 1…{lastIssued}."; return false; }
                if (!seen.Add(i)) { error = $"diplomacy: {what}-ID {i} doppelt."; return false; }
            }
            return true;
        }

        // Weiche Fehler nach dem Laden aller Sektionen: Rückfall anwenden und melden (ARCHITECTURE §11.1 Regel 6).
        public void CheckReferences(SimContext ctx, LoadReport report) => CheckReferences(report.Warn);

        // Liefert die Anzahl der Warnungen; jede Warnung bedeutet einen bereits angewendeten Rückfall.
        public int CheckReferences(Action<string> warn)
        {
            int warnings = 0;
            Action<string> report = m => { warnings++; warn?.Invoke(m); };
            var world = _d.World;
            var involved = new SortedSet<int>();
            foreach (var w in _d.Wars) { foreach (var c in w.Attackers) involved.Add(c); foreach (var c in w.Defenders) involved.Add(c); }
            foreach (var t in _d.Treaties) { involved.Add(t.CivA); involved.Add(t.CivB); }
            foreach (var m in _d.Modifiers) { involved.Add(m.FromCiv); involved.Add(m.ToCiv); }
            foreach (var p in _d.Points) if (p.ControllerCiv != 0) involved.Add(p.ControllerCiv);
            foreach (var m in _d.Memory) involved.Add(m.CivId);

            foreach (var civ in involved)
            {
                if (world.IsCivAlive(civ)) continue;
                report($"diplomacy: Zivilisation {civ} existiert nicht mehr. Kriege beendet, Verträge aufgelöst, Meinungen entfernt.");
                _d.ReportCivilizationCollapsed(civ);
            }

            foreach (var w in _d.Wars)
            {
                for (int i = w.Goals.Count - 1; i >= 0; i--)
                {
                    var g = w.Goals[i];
                    bool missing = (g.Type == WarGoalType.ConquerSettlement && !world.TryGetSettlement(g.TargetId, out _))
                                || (g.Type == WarGoalType.HoldPoint && _d.GetStrategicPoint(g.TargetId) == null)
                                || w.SideOf(g.OwnerCiv) == WarSide.None;
                    if (!missing) continue;
                    report($"diplomacy: Kriegsziel in Krieg {w.Id} verweist auf fehlendes Objekt {g.TargetId}. Ziel gestrichen.");
                    w.Goals.RemoveAt(i);
                }
            }
            foreach (var t in _d.Treaties)
            {
                if (t.SuspendedWarId == 0 || _d.GetWar(t.SuspendedWarId) != null) continue;
                report($"diplomacy: Waffenstillstand {t.Id} verweist auf unbekannten Krieg {t.SuspendedWarId}. Verweis entfernt.");
                t.SuspendedWarId = 0;
            }
            return warnings;
        }

        // ---------- Tiefe Kopien (Zustand und DTO teilen nie Objekte) ----------

        static void Replace<T>(List<T> target, List<T> source, Func<T, T> copy)
        {
            target.Clear();
            foreach (var x in source) target.Add(copy(x));
        }

        static OpinionModifier Copy(OpinionModifier m) => new OpinionModifier
        { Id = m.Id, FromCiv = m.FromCiv, ToCiv = m.ToCiv, Reason = m.Reason, Value = m.Value, DecayPerSecond = m.DecayPerSecond, CreatedTick = m.CreatedTick };

        static Treaty Copy(Treaty t) => new Treaty
        { Id = t.Id, Type = t.Type, CivA = t.CivA, CivB = t.CivB, StartTick = t.StartTick, EndTick = t.EndTick, SuspendedWarId = t.SuspendedWarId, Active = t.Active, EndReason = t.EndReason };

        static StrategicPoint Copy(StrategicPoint p) => new StrategicPoint
        { Id = p.Id, Type = p.Type, X = p.X, Y = p.Y, Value = p.Value, ControllerCiv = p.ControllerCiv };

        static CivAiMemory Copy(CivAiMemory m) => new CivAiMemory
        { CivId = m.CivId, LastWarTick = m.LastWarTick, LastPeaceProposalTick = m.LastPeaceProposalTick, LastAllianceTick = m.LastAllianceTick, LastRecruitTick = m.LastRecruitTick,
          PendingRecruits = m.PendingRecruits, PendingRecruitsTick = m.PendingRecruitsTick, RecruitWillingness = m.RecruitWillingness };

        static DiplomaticDecision Copy(DiplomaticDecision d)
        {
            var c = new DiplomaticDecision { Tick = d.Tick, CivId = d.CivId, TargetCiv = d.TargetCiv, Action = d.Action, Utility = d.Utility };
            if (d.Factors != null) c.Factors.AddRange(d.Factors);
            return c;
        }

        static WarSideStats Copy(WarSideStats s) => new WarSideStats
        {
            StrengthLost = s.StrengthLost, UnitsLost = s.UnitsLost, SettlementsCaptured = s.SettlementsCaptured, PopulationCaptured = s.PopulationCaptured,
            SettlementsLost = s.SettlementsLost, PopulationLost = s.PopulationLost, ResourcesSpent = s.ResourcesSpent,
            InitialStrength = s.InitialStrength, InitialPopulation = s.InitialPopulation, Exhaustion = s.Exhaustion,
        };

        static War Copy(War w)
        {
            var c = new War
            {
                Id = w.Id, AttackerLeader = w.AttackerLeader, DefenderLeader = w.DefenderLeader, StartTick = w.StartTick, EndTick = w.EndTick, FirstContactTick = w.FirstContactTick,
                CasusBelli = w.CasusBelli, WarScore = w.WarScore, Active = w.Active, Suspended = w.Suspended, EndReason = w.EndReason,
                AttackerStats = Copy(w.AttackerStats ?? new WarSideStats()), DefenderStats = Copy(w.DefenderStats ?? new WarSideStats()),
            };
            if (w.Attackers != null) c.Attackers.AddRange(w.Attackers);
            if (w.Defenders != null) c.Defenders.AddRange(w.Defenders);
            if (w.Goals != null)
                foreach (var g in w.Goals) c.Goals.Add(new WarGoal { Type = g.Type, OwnerCiv = g.OwnerCiv, TargetId = g.TargetId, Fulfilled = g.Fulfilled });
            return c;
        }
    }
}
