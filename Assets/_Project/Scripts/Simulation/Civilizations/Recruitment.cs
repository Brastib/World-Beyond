using System;
using System.Collections.Generic;
using WorldBeyond.Core;
using WorldBeyond.Simulation.Units;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Simulation.Civilizations
{
    /// <summary>
    /// Einziger Produktionsweg für alle Einheiten (INTEGRATION.md §5). Request prüft Technologie, Produktionsgebäude,
    /// Bevölkerung und Kosten, bucht die Kosten sofort ab und reiht die Produktion in der Siedlung ein.
    /// Die Einheit entsteht im Siedlungs-Update nach BuildYears; dann verlassen PopulationCost Erwachsene die Siedlung.
    /// </summary>
    public sealed class Recruitment : IRecruitment
    {
        private readonly CivState _s;
        private readonly Dictionary<string, RecruitableUnitData> _table = new Dictionary<string, RecruitableUnitData>(StringComparer.Ordinal);
        internal SettlementRegistry Settlements;
        internal TechTree Tech;
        internal Economy Economy;
        internal TransportSystem Transport;

        public Recruitment(CivState state) => _s = state ?? throw new ArgumentNullException(nameof(state));

        public void RegisterRecruitable(RecruitableUnitData data)
        {
            if (data == null || string.IsNullOrEmpty(data.UnitTypeKey)) throw new ArgumentException("RecruitableUnitData ohne UnitTypeKey.");
            if (!_s.Ctx.UnitTypes.TryGetByKey(data.UnitTypeKey, out _)) throw new ArgumentException($"Einheitentyp '{data.UnitTypeKey}' ist nicht registriert.");
            if (data.BuildYears <= 0f || data.PopulationCost < 0) throw new ArgumentException($"Rekrutierung '{data.UnitTypeKey}': ungültige Zeit oder Bevölkerungskosten.");
            if (_table.ContainsKey(data.UnitTypeKey)) throw new InvalidOperationException($"Rekrutierung '{data.UnitTypeKey}' ist doppelt registriert.");
            _table.Add(data.UnitTypeKey, data);
        }

        public bool TryGetRecruitable(string unitTypeKey, out RecruitableUnitData data)
        {
            data = null;
            return unitTypeKey != null && _table.TryGetValue(unitTypeKey, out data);
        }

        public RecruitResult Request(int civId, string unitTypeKey, int count, int settlementId)
            => Enqueue(civId, unitTypeKey, count, settlementId, null);

        internal RecruitResult Enqueue(int civId, string unitTypeKey, int count, int settlementId, Action<RecruitOrder> configure)
        {
            var st = _s.Settlement(settlementId);
            if (count < 1 || st == null || st.CivId != civId || !TryGetRecruitable(unitTypeKey, out var data)) return RecruitResult.InvalidRequest;
            if (!Tech.IsUnlocked(civId, data.RequiredTechKey)) return RecruitResult.TechMissing;
            if (data.RequiredBuildingKey != null && !HasCompleted(st, data.RequiredBuildingKey)) return RecruitResult.NoProductionBuilding;
            int pendingPop = 0;
            foreach (var o in st.RecruitQueue)
                if (_table.TryGetValue(o.UnitTypeKey, out var od)) pendingPop += od.PopulationCost * o.Remaining;
            // Mindestens 2 Erwachsene bleiben zurück, damit die Siedlung nicht durch Rekrutierung ausstirbt.
            if (st.Adults - pendingPop - data.PopulationCost * count < 2) return RecruitResult.NotEnoughPopulation;
            if (!Economy.TrySpendAt(settlementId, data.Cost.Times(count))) return RecruitResult.NotEnoughResources;
            var order = new RecruitOrder { UnitTypeKey = unitTypeKey, Remaining = count, YearsPerUnit = data.BuildYears };
            configure?.Invoke(order);
            st.RecruitQueue.Add(order);
            return RecruitResult.Queued;
        }

        internal bool HasCompleted(Settlement st, string buildingKey)
        {
            if (!_s.Content.Buildings.TryGetByKey(buildingKey, out var type)) return false;
            foreach (var b in st.Buildings)
            {
                var r = _s.Buildings.GetRecord(b);
                if (r != null && r.Completed && r.Type.Equals(type)) return true;
            }
            return false;
        }

        internal int PendingCount(Settlement st, string unitTypeKey)
        {
            int n = 0;
            foreach (var o in st.RecruitQueue) if (o.UnitTypeKey == unitTypeKey) n += o.Remaining;
            return n;
        }

        /// <summary>Produktionsfortschritt der Siedlung (aus dem Siedlungs-Update). Nur die erste Bestellung läuft.</summary>
        internal void Process(Settlement st, double dt)
        {
            double budget = dt;
            while (budget > 0 && st.RecruitQueue.Count > 0)
            {
                var o = st.RecruitQueue[0];
                if (!_table.TryGetValue(o.UnitTypeKey, out var data)) { st.RecruitQueue.RemoveAt(0); continue; }
                double need = o.YearsPerUnit - o.ProgressYears;
                if (budget < need) { o.ProgressYears += budget; return; }
                // Fertig, aber nicht genug Erwachsene: warten (Fortschritt bleibt voll).
                if (st.Adults - data.PopulationCost < 1) { o.ProgressYears = o.YearsPerUnit; return; }
                budget -= need;
                o.ProgressYears = 0;
                o.Remaining--;
                Spawn(st, data, o);
                if (o.Remaining <= 0) st.RecruitQueue.RemoveAt(0);
            }
        }

        private void Spawn(Settlement st, RecruitableUnitData data, RecruitOrder o)
        {
            if (!_s.Ctx.UnitTypes.TryGetByKey(data.UnitTypeKey, out var type)) return;
            var (x, z) = _s.ArrivalPoint(st, type);
            var id = _s.Ctx.Units.Spawn(type, st.CivId, x, z);
            if (!id.IsValid) return;
            st.Adults = Math.Max(0, st.Adults - data.PopulationCost);

            if (type.Equals(_s.Content.SettlerType) && o.TargetX >= 0)
            {
                _s.Ctx.Units.SetTask(id, new UnitTask
                {
                    Kind = UnitTaskKind.Settle, TargetRef = st.Id,
                    TargetX = WorldCoords.TileCenterX(o.TargetX), TargetZ = WorldCoords.TileCenterZ(o.TargetY),
                });
                _s.Ctx.Units.SetMoveTarget(id, WorldCoords.TileCenterX(o.TargetX), WorldCoords.TileCenterZ(o.TargetY));
            }
            else if (type.Equals(_s.Content.CaravanType))
            {
                Transport.OnCaravanSpawned(st, id, o);
            }
            _s.Ctx.Events.Publish(new UnitRecruitedEvent { Unit = id, CivId = st.CivId, SettlementId = st.Id, UnitTypeKey = data.UnitTypeKey });
        }

        internal void RegisterDefaults()
        {
            RegisterRecruitable(new RecruitableUnitData { UnitTypeKey = CivContent.UnitSettler, Cost = SettlerSystem.SettlerCost, PopulationCost = SettlerSystem.SettlerPopulation, BuildYears = 0.5f });
            RegisterRecruitable(new RecruitableUnitData { UnitTypeKey = CivContent.UnitCaravan, Cost = new ResourceCost { Wood = 5 }, PopulationCost = 1, BuildYears = 0.1f });
        }
    }
}
