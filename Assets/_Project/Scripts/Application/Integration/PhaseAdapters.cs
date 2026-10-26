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
        private readonly List<EntityId> _scratchUnits = new List<EntityId>();
        private readonly Dictionary<int, int> _scratchOwners = new Dictionary<int, int>();

        public DiplomacyWorldPortAdapter(ICivilizationQuery civs, ISettlementRegistry settlements, ITerritoryService territory,
            IEconomy economy, IRecruitment recruitment, IUnitQuery units, IMilitaryQuery military, IWorldQuery world)
        {
            _civs = civs ?? throw new ArgumentNullException(nameof(civs));
            _settlements = settlements ?? throw new ArgumentNullException(nameof(settlements));
            _territory = territory ?? throw new ArgumentNullException(nameof(territory));
            _economy = economy ?? throw new ArgumentNullException(nameof(economy));
            _recruitment = recruitment ?? throw new ArgumentNullException(nameof(recruitment));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _military = military ?? throw new ArgumentNullException(nameof(military));
            _world = world ?? throw new ArgumentNullException(nameof(world));
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
            var result = MilitaryRecruitment.Request(_civs, _recruitment, civId, unitCount);
            reason = result == RecruitResult.Queued ? null : result.ToString();
            return result == RecruitResult.Queued;
        }

        // Besitzer mit mehr als der Hälfte der besetzten Kacheln im Quadrat; sonst 0.
        public int GetDominantCivNear(int x, int y, int radius)
        {
            _scratchOwners.Clear();
            int owned = 0;
            int x0 = Math.Max(0, x - radius), x1 = Math.Min(_world.Width - 1, x + radius);
            int y0 = Math.Max(0, y - radius), y1 = Math.Min(_world.Height - 1, y + radius);
            for (int ty = y0; ty <= y1; ty++)
            for (int tx = x0; tx <= x1; tx++)
            {
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

        public static RecruitResult Request(ICivilizationQuery civs, IRecruitment recruitment, int civId, int count)
        {
            if (count <= 0 || !civs.TryGet(civId, out var c) || c.CapitalSettlementId <= 0) return RecruitResult.InvalidRequest;
            return recruitment.Request(civId, MilitaryContent.RiflemanKey, count, c.CapitalSettlementId);
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
            ISpatialIndex spatial, ICivilizationQuery civs, ISettlementQuery settlements, IRecruitment recruitment)
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
            army.Recruit = (civId, role, count) =>
                role == MilitaryRole.Rifleman && MilitaryRecruitment.Request(civs, recruitment, civId, count) == RecruitResult.Queued ? count : 0;
        }
    }
}
