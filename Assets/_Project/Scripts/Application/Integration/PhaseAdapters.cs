using System;
using System.Collections.Generic;
using WorldBeyond.Core;
using WorldBeyond.Simulation;
using WorldBeyond.Simulation.Civilizations;
using WorldBeyond.Simulation.Combat;
using WorldBeyond.Simulation.Diplomacy;
using WorldBeyond.Simulation.Military;
using WorldBeyond.Simulation.Units;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Application.Integration
{
    /// <summary>
    /// Verbindet War (Phase 4) mit Civ (Phase 3) und Military (Phase 5). Nur Übersetzung, keine eigene Spiellogik
    /// (INTEGRATION.md §3: Adapter gehören der Integration). Heuristiken, die eine Phase noch nicht liefert, sind als
    /// PLATZHALTER markiert und werden ersetzt, sobald die zuständige Phase eine echte Abfrage anbietet.
    /// </summary>
    public sealed class DiplomacyWorldPortAdapter : IDiplomacyWorldPort
    {
        private static readonly IReadOnlyList<int> Empty = Array.Empty<int>();
        private static readonly ResourceType[] TributeResources =
        {
            ResourceType.Food, ResourceType.Wood, ResourceType.Stone, ResourceType.Ore,
            ResourceType.Oil, ResourceType.Gold, ResourceType.Metal, ResourceType.Tools,
        };

        private readonly ICivilizationQuery _civs;
        private readonly ISettlementRegistry _settlements;
        private readonly ITerritoryService _territory;
        private readonly IEconomy _economy;
        private readonly IRecruitment _recruitment;
        private readonly IUnitQuery _units;
        private readonly IMilitaryQuery _military;
        private readonly IWorldQuery _world;
        private readonly ISpatialIndex _spatial;
        private readonly List<EntityId> _scratchUnits = new List<EntityId>();
        private readonly Dictionary<int, int> _scratchOwners = new Dictionary<int, int>();
        private readonly Dictionary<int, float> _scratchStrength = new Dictionary<int, float>();

        public DiplomacyWorldPortAdapter(ICivilizationQuery civs, ISettlementRegistry settlements, ITerritoryService territory,
            IEconomy economy, IRecruitment recruitment, IUnitQuery units, IMilitaryQuery military, IWorldQuery world, ISpatialIndex spatial)
        {
            _civs = civs ?? throw new ArgumentNullException(nameof(civs));
            _settlements = settlements ?? throw new ArgumentNullException(nameof(settlements));
            _territory = territory ?? throw new ArgumentNullException(nameof(territory));
            _economy = economy ?? throw new ArgumentNullException(nameof(economy));
            _recruitment = recruitment ?? throw new ArgumentNullException(nameof(recruitment));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _military = military ?? throw new ArgumentNullException(nameof(military));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
        }

        public IReadOnlyList<int> CivIds => _civs.AllCivIds;

        public bool IsCivAlive(int civId) => _civs.Exists(civId);

        public float GetMilitaryStrength(int civId)
        {
            float sum = 0f;
            _units.CopyAliveIds(_scratchUnits);
            for (int i = 0; i < _scratchUnits.Count; i++)
                if (_units.TryGet(_scratchUnits[i], out var u) && u.CivId == civId)
                    sum += _military.GetCombatStrength(u.Type);
            return sum;
        }

        // Lebende plus eingereihte Militäreinheiten (Military zählt). Die Warteschlange kommt über ArmyEnvironment.PendingRecruits,
        // das erst verdrahtet ist, wenn Civ eine öffentliche Abfrage liefert; bis dahin zählt War angenommene Anfragen selbst
        // (DiplomacySettings.RecruitPendingTicks).
        public int GetArmyUnitCount(int civId) => _military.GetMilitaryCount(civId);

        public float GetUnitCombatStrength(int unitTypeId) => _military.GetCombatStrength(new UnitTypeId(unitTypeId));

        public int GetPopulation(int civId) => _civs.TryGet(civId, out var c) ? c.Population : 0;

        // CivTraits.Militarism liegt in 0,7 … 1,3 (CivTraits.FromSeed) und wird auf 0 … 1 abgebildet.
        public float GetAggression(int civId) => _civs.TryGet(civId, out var c) ? Clamp01((c.Traits.Militarism - 0.7f) / 0.6f) : 0f;

        // PLATZHALTER bis Civ einen Mangelwert liefert: Nahrung für weniger als zwei Einheiten je Einwohner gilt als Mangel.
        public float GetResourceNeed(int civId)
        {
            if (!_civs.TryGet(civId, out var c) || c.Population <= 0) return 0f;
            return Clamp01(1f - _economy.GetStock(civId, ResourceType.Food) / (2f * c.Population));
        }

        public int GetSharedBorderLength(int civA, int civB) => _territory.SharedBorderLength(civA, civB);

        public int GetTerritoryTiles(int civId) => _civs.TryGet(civId, out var c) ? c.TerritoryTiles : 0;

        // PLATZHALTER bis Civ/World umkämpfte Ressourcenkacheln zählen: Grenzlänge relativ zum kleineren Gebiet.
        public float GetResourceContestion(int civA, int civB)
        {
            int border = _territory.SharedBorderLength(civA, civB);
            if (border <= 0) return 0f;
            int smaller = Math.Min(_territory.TerritoryTiles(civA), _territory.TerritoryTiles(civB));
            return smaller <= 0 ? 0f : Clamp01(4f * border / smaller);
        }

        public bool TryGetCapitalPosition(int civId, out int x, out int y)
        {
            x = y = 0;
            if (!_civs.TryGet(civId, out var c) || !_settlements.TryGet(c.CapitalSettlementId, out var s)) return false;
            x = s.X; y = s.Y;
            return true;
        }

        public IReadOnlyList<int> GetSettlements(int civId) => _civs.Exists(civId) ? _settlements.GetByCiv(civId) : Empty;

        public bool TryGetSettlement(int settlementId, out SettlementInfo info)
        {
            info = default;
            if (!_settlements.TryGet(settlementId, out var s)) return false;
            bool capital = _civs.TryGet(s.CivId, out var c) && c.CapitalSettlementId == settlementId;
            info = new SettlementInfo(s.Id, s.CivId, s.Population, s.X, s.Y, capital);
            return true;
        }

        public bool TransferSettlement(int settlementId, int newCivId)
        {
            if (!_settlements.TryGet(settlementId, out var s) || s.CivId == newCivId || !_civs.Exists(newCivId)) return false;
            _settlements.TransferSettlement(settlementId, newCivId);
            return _settlements.TryGet(settlementId, out var after) && after.CivId == newCivId;
        }

        // Überträgt je Ressource den Anteil fraction; Rückgabe = Summe der Mengen über alle Ressourcen (gemischte Einheiten, nur als Größenordnung nutzen).
        public float TransferTribute(int fromCiv, int toCiv, float fraction)
        {
            if (fraction <= 0f || !_civs.Exists(fromCiv) || !_civs.Exists(toCiv)) return 0f;
            if (fraction > 1f) fraction = 1f;
            int total = 0;
            foreach (var r in TributeResources)
            {
                int amount = (int)(_economy.GetStock(fromCiv, r) * fraction);
                if (amount <= 0 || !_economy.TryConsume(fromCiv, r, amount)) continue;
                _economy.Add(toCiv, r, amount);
                total += amount;
            }
            return total;
        }

        public bool RequestMilitaryBuildup(int civId, int unitCount, out string reason)
        {
            int queued = MilitaryRecruitment.Request(_civs, _settlements, _recruitment, civId, unitCount, out var last);
            reason = queued > 0 ? null : last.ToString();
            return queued > 0;
        }

        // Wer hält den Punkt? Zuerst lebende Militäreinheiten im Kreis (Mehrheit der Kampfstärke, mehr als die Hälfte);
        // ohne Einheiten im Kreis entscheidet der Gebietsbesitz (Mehrheit der besetzten Kacheln im Kreis). 0 = niemand oder umkämpft.
        public int GetDominantCivNear(int x, int y, int radius)
        {
            if (radius < 0) radius = 0;
            _scratchOwners.Clear();
            float cx = WorldCoords.TileCenterX(x), cz = WorldCoords.TileCenterZ(y);
            _spatial.QueryRadius(cx, cz, (radius + 0.5f) * WorldConstants.TileSize, _scratchUnits);
            float total = 0f;
            var strength = _scratchStrength;
            strength.Clear();
            for (int i = 0; i < _scratchUnits.Count; i++)
            {
                if (!_units.TryGet(_scratchUnits[i], out var u) || u.CivId <= 0) continue;
                float s = _military.GetCombatStrength(u.Type);
                if (s <= 0f) continue;
                strength.TryGetValue(u.CivId, out float cur);
                strength[u.CivId] = cur + s;
                total += s;
            }
            if (total > 0f)
            {
                foreach (var kv in strength)
                    if (kv.Value * 2f > total) return kv.Key;
                return 0;
            }

            int owned = 0, r2 = radius * radius;
            int x0 = Math.Max(0, x - radius), x1 = Math.Min(_world.Width - 1, x + radius);
            int y0 = Math.Max(0, y - radius), y1 = Math.Min(_world.Height - 1, y + radius);
            for (int ty = y0; ty <= y1; ty++)
            for (int tx = x0; tx <= x1; tx++)
            {
                int dx = tx - x, dy = ty - y;
                if (dx * dx + dy * dy > r2) continue;
                int owner = _territory.OwnerAt(tx, ty);
                if (owner <= 0) continue;
                owned++;
                _scratchOwners.TryGetValue(owner, out int n);
                _scratchOwners[owner] = n + 1;
            }
            foreach (var kv in _scratchOwners)
                if (kv.Value * 2 > owned) return kv.Key;
            return 0;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }

    /// <summary>Einziger Rekrutierungsweg für Militär: IRecruitment der Civ, Produktion in der Hauptstadt.</summary>
    public static class MilitaryRecruitment
    {
        // PLATZHALTER-Balancing für M1 (keine Tech-Voraussetzung), bis Military/Civ echte Kosten festlegen.
        public static RecruitableUnitData Rifleman => new RecruitableUnitData
        {
            UnitTypeKey = MilitaryContent.RiflemanKey,
            Cost = new ResourceCost { Food = 10, Wood = 5 },
            PopulationCost = 1,
            BuildYears = 0.1f,
        };

        /// <summary>
        /// Reiht bis zu count Schützen ein: Hauptstadt zuerst, dann Siedlungen nach Bevölkerung (absteigend, bei Gleichstand kleinere ID).
        /// Je Siedlung die größte machbare Anzahl (count … 1), da Bevölkerung und Lager je Siedlung geprüft werden.
        /// Liefert die Zahl eingereihter Einheiten; last = letztes Ergebnis (Grund bei 0).
        /// </summary>
        public static int Request(ICivilizationQuery civs, ISettlementQuery settlements, IRecruitment recruitment, int civId, int count, out RecruitResult last)
        {
            last = RecruitResult.InvalidRequest;
            if (count <= 0 || !civs.TryGet(civId, out var c)) return 0;
            var ids = new List<int>(settlements.GetByCiv(civId));
            ids.Sort((a, b) =>
            {
                if (a == b) return 0;
                if (a == c.CapitalSettlementId) return -1;
                if (b == c.CapitalSettlementId) return 1;
                int pa = settlements.TryGet(a, out var sa) ? sa.Population : 0;
                int pb = settlements.TryGet(b, out var sb) ? sb.Population : 0;
                return pa != pb ? pb.CompareTo(pa) : a.CompareTo(b);
            });
            int remaining = count;
            foreach (int sid in ids)
            {
                for (int n = remaining; n >= 1; n--)
                {
                    last = recruitment.Request(civId, MilitaryContent.RiflemanKey, n, sid);
                    if (last == RecruitResult.Queued) { remaining -= n; break; }
                    if (last == RecruitResult.TechMissing || last == RecruitResult.InvalidRequest) return count - remaining;
                    if (last == RecruitResult.NoProductionBuilding) break;   // andere Siedlung versuchen
                }
                if (remaining == 0) break;
            }
            if (remaining < count) last = RecruitResult.Queued;
            return count - remaining;
        }
    }

    /// <summary>CivAISystem ruft den Kriegsbewerter über ICivWarEvaluator (Civ kennt War nicht).</summary>
    public sealed class CivWarEvaluatorAdapter : ICivWarEvaluator
    {
        private readonly WarStrategyEvaluator _evaluator;
        public CivWarEvaluatorAdapter(WarStrategyEvaluator evaluator) => _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        public void EvaluateAndAct(int civId) => _evaluator.EvaluateAndAct(civId);
    }

    /// <summary>Setzt die Delegates von CombatEnvironment und ArmyEnvironment auf World, War und Civ.</summary>
    public static class MilitaryWiring
    {
        public static void Connect(CombatEnvironment combat, ArmyEnvironment army, IWarQuery wars, IWorldQuery world,
            ISpatialIndex spatial, ICivilizationQuery civs, ISettlementRegistry settlements, IRecruitment recruitment)
        {
            combat.AreHostile = wars.AreAtWar;
            combat.HeightAt = world.SampleHeight;
            combat.QueryRadius = spatial.QueryRadius;

            army.Objectives = civId =>
            {
                var targets = wars.GetTargetPriorities(civId);
                var list = new List<ArmyObjective>(targets.Count);
                for (int i = 0; i < targets.Count; i++)
                {
                    var t = targets[i];
                    int settlementId = t.Kind == TargetKind.Settlement ? t.TargetId : 0;
                    list.Add(new ArmyObjective(WorldCoords.TileCenterX(t.X), WorldCoords.TileCenterZ(t.Y), t.Value, t.OwnerCiv, settlementId));
                }
                return list;
            };
            army.SettlementPosition = id => settlements.TryGet(id, out var s)
                ? (true, WorldCoords.TileCenterX(s.X), WorldCoords.TileCenterZ(s.Y))
                : (false, 0f, 0f);
            army.HomeOf = civId => civs.TryGet(civId, out var c) ? army.SettlementPosition(c.CapitalSettlementId) : (false, 0f, 0f);
            // Besetzung (Military SettlementCaptureSystem) → einziger Übernahmeweg der Civ; SettlementCapturedEvent geht an War.
            army.CaptureSettlement = (settlementId, newCivId) =>
            {
                if (!settlements.TryGet(settlementId, out var s) || s.CivId == newCivId || !civs.Exists(newCivId)) return false;
                settlements.TransferSettlement(settlementId, newCivId);
                return settlements.TryGet(settlementId, out var after) && after.CivId == newCivId;
            };
            army.Settlements = () =>
            {
                var ids = settlements.AllSettlementIds;
                var list = new List<ArmyObjective>(ids.Count);
                for (int i = 0; i < ids.Count; i++)
                    if (settlements.TryGet(ids[i], out var s))
                        list.Add(new ArmyObjective(WorldCoords.TileCenterX(s.X), WorldCoords.TileCenterZ(s.Y), s.Population, s.CivId, s.Id));
                return list;
            };
            army.Recruit = (civId, role, count) =>
                role == MilitaryRole.Rifleman ? MilitaryRecruitment.Request(civs, settlements, recruitment, civId, count, out _) : 0;
        }
    }
}
