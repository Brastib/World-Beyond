using System;
using System.Collections.Generic;
using WorldBeyond.Core;
using WorldBeyond.Simulation.Units;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Simulation.Civilizations
{
    /// <summary>
    /// Transport zwischen Siedlungen derselben Zivilisation durch echte Karawanen-Einheiten.
    /// Planung alle 10 Spielsekunden: Überschuss → Mangel bei Nahrung, Holz und Stein. Die Ladung verlässt das Lager
    /// beim Aufbruch und kommt erst bei Ankunft an; stirbt die Karawane, ist die Ladung verloren.
    /// </summary>
    public sealed class TransportSystem : ISimulationSystem
    {
        public const int MaxCargo = 60;
        public const int MaxCaravansPerCiv = 4;

        internal sealed class Caravan
        {
            public EntityId Unit;
            public int OriginSettlementId;
            public int TargetSettlementId;
            public ResourceType Resource;
            public int Amount;
        }

        private readonly CivState _s;
        private readonly Recruitment _recruitment;
        private readonly Dictionary<EntityId, Caravan> _caravans = new Dictionary<EntityId, Caravan>();
        private readonly List<EntityId> _order = new List<EntityId>();   // feste Reihenfolge für Speichern

        public TransportSystem(CivState state, Recruitment recruitment)
        {
            _s = state; _recruitment = recruitment;
            _s.Ctx.Events.Subscribe<UnitArrivedEvent>(OnArrived);
            _s.Ctx.Events.Subscribe<UnitMoveFailedEvent>(OnMoveFailed);
            _s.Ctx.Events.Subscribe<UnitDiedEvent>(OnDied);
        }

        public string Name => "Transport";
        public int Order => SystemOrder.Economy + 20;
        public int TickInterval => 100;
        public int ActiveCaravans => _caravans.Count;

        private static readonly ResourceType[] Goods = { ResourceType.Food, ResourceType.Wood, ResourceType.Stone };

        public void Tick(SimContext ctx)
        {
            foreach (int civId in _s.CivOrder)
            {
                var civ = _s.Civs[civId];
                if (!civ.Alive) continue;
                var list = _s.SettlementListOf(civId);
                if (list.Count < 2 || CountFor(civId) >= MaxCaravansPerCiv) continue;
                foreach (var good in Goods)
                {
                    if (TryPlan(civId, list, good)) break;   // höchstens eine neue Karawane pro Civ und Runde
                }
            }
        }

        private bool TryPlan(int civId, List<int> list, ResourceType good)
        {
            Settlement needy = null, rich = null;
            double worstLack = 0, bestSurplus = 0;
            foreach (int sid in list)
            {
                var st = _s.Settlements[sid];
                double need = Need(st, good);
                double have = st.Stock[(int)good];
                double lack = need * 0.5 - have;              // weniger als ein halbes Jahr Bedarf
                double surplus = have - need * 1.5 - 10;      // mehr als anderthalb Jahre Bedarf
                if (lack > worstLack && !HasIncoming(st.Id, good)) { worstLack = lack; needy = st; }
                if (surplus > bestSurplus) { bestSurplus = surplus; rich = st; }
            }
            if (needy == null || rich == null || needy == rich) return false;
            int amount = (int)Math.Min(MaxCargo, Math.Min(bestSurplus, worstLack + 10));
            if (amount < 5) return false;
            var target = needy;
            var res = _recruitment.Enqueue(civId, CivContent.UnitCaravan, 1, rich.Id, o =>
            {
                o.CargoTargetSettlementId = target.Id;
                o.CargoResource = good;
                o.CargoAmount = amount;
            });
            if (res != RecruitResult.Queued) return false;
            _s.Ctx.Events.Publish(new CivDecisionEvent { CivId = civId, SettlementId = rich.Id, Decision = $"Karawane mit {amount} {good} nach {needy.Name}", Reason = $"{needy.Name} hat zu wenig {good}." });
            return true;
        }

        private static double Need(Settlement st, ResourceType good)
        {
            if (good == ResourceType.Food) return SettlementSimulationSystem.FoodNeedPerYear(st);
            return st.Population * 1.0;   // grober Baubedarf
        }

        private bool HasIncoming(int settlementId, ResourceType good)
        {
            foreach (var c in _caravans.Values) if (c.TargetSettlementId == settlementId && c.Resource == good) return true;
            foreach (int sid in _s.SettlementOrder)
                foreach (var o in _s.Settlements[sid].RecruitQueue)
                    if (o.CargoTargetSettlementId == settlementId && o.CargoResource == good) return true;
            return false;
        }

        private int CountFor(int civId)
        {
            int n = 0;
            foreach (var c in _caravans.Values)
            {
                var o = _s.Settlement(c.OriginSettlementId);
                if (o != null && o.CivId == civId) n++;
            }
            return n;
        }

        /// <summary>Von Recruitment beim Erscheinen der Einheit: Ladung aus dem Lager nehmen und losschicken.</summary>
        internal void OnCaravanSpawned(Settlement origin, EntityId unit, RecruitOrder o)
        {
            var target = _s.Settlement(o.CargoTargetSettlementId);
            int amount = (int)Math.Min(o.CargoAmount, Math.Floor(origin.Stock[(int)o.CargoResource]));
            if (target == null || target.CivId != origin.CivId || amount <= 0 || !Resources.IsStockable(o.CargoResource))
            {
                origin.Adults += 1;   // Besatzung bleibt zu Hause
                _s.Ctx.Units.Despawn(unit);
                return;
            }
            origin.Stock[(int)o.CargoResource] -= amount;
            var c = new Caravan { Unit = unit, OriginSettlementId = origin.Id, TargetSettlementId = target.Id, Resource = o.CargoResource, Amount = amount };
            _caravans.Add(unit, c);
            _order.Add(unit);
            var (x, z) = CivState.SettlementCenter(target);
            _s.Ctx.Units.SetTask(unit, new UnitTask { Kind = UnitTaskKind.MoveTo, TargetRef = target.Id, TargetX = x, TargetZ = z });
            _s.Ctx.Units.SetMoveTarget(unit, x, z);
        }

        private void OnArrived(UnitArrivedEvent e)
        {
            if (!_caravans.TryGetValue(e.Unit, out var c)) return;
            var target = _s.Settlement(c.TargetSettlementId);
            if (target == null || !_s.Ctx.Units.TryGet(e.Unit, out var u) || target.CivId != u.CivId)
            {
                Redirect(c);
                return;
            }
            Economy.Deposit(target, c.Resource, c.Amount);
            target.Adults += 1;   // Besatzung bleibt am Ziel
            Remove(c.Unit);
            _s.Ctx.Units.Despawn(e.Unit);
        }

        private void OnMoveFailed(UnitMoveFailedEvent e)
        {
            if (_caravans.TryGetValue(e.Unit, out var c)) Redirect(c);
        }

        private void OnDied(UnitDiedEvent e)
        {
            if (_caravans.ContainsKey(e.Unit)) Remove(e.Unit);   // Ladung verloren
        }

        /// <summary>Ziel verloren oder unerreichbar: zur Herkunft zurück; existiert sie nicht mehr, verfällt die Karawane.</summary>
        private void Redirect(Caravan c)
        {
            if (!_s.Ctx.Units.TryGet(c.Unit, out var u)) { Remove(c.Unit); return; }
            var home = _s.Settlement(c.OriginSettlementId);
            if (home == null || home.CivId != u.CivId || c.TargetSettlementId == c.OriginSettlementId)
            {
                Remove(c.Unit);
                _s.Ctx.Units.Despawn(c.Unit);
                return;
            }
            c.TargetSettlementId = home.Id;
            var (x, z) = CivState.SettlementCenter(home);
            _s.Ctx.Units.SetTask(c.Unit, new UnitTask { Kind = UnitTaskKind.MoveTo, TargetRef = home.Id, TargetX = x, TargetZ = z });
            _s.Ctx.Units.SetMoveTarget(c.Unit, x, z);
        }

        private void Remove(EntityId id)
        {
            _caravans.Remove(id);
            _order.Remove(id);
        }

        // ---------------- Speichern ----------------

        internal CaravanDto[] Capture()
        {
            var list = new CaravanDto[_order.Count];
            for (int i = 0; i < _order.Count; i++)
            {
                var c = _caravans[_order[i]];
                list[i] = new CaravanDto
                {
                    UnitIndex = c.Unit.Index, UnitGeneration = c.Unit.Generation, Origin = c.OriginSettlementId,
                    Target = c.TargetSettlementId, Resource = c.Resource.ToString(), Amount = c.Amount,
                };
            }
            return list;
        }

        internal bool TryRestore(CaravanDto[] dtos, out string error)
        {
            _caravans.Clear();
            _order.Clear();
            foreach (var d in dtos ?? Array.Empty<CaravanDto>())
            {
                if (d == null || !Enum.TryParse(d.Resource, out ResourceType r) || !Resources.IsStockable(r) || d.Amount < 0)
                {
                    error = $"civs: ungültige Karawane ({d?.Resource}).";
                    return false;
                }
                var id = new EntityId(d.UnitIndex, d.UnitGeneration);
                if (_caravans.ContainsKey(id)) { error = $"civs: Karawane {id} doppelt."; return false; }
                _caravans.Add(id, new Caravan { Unit = id, OriginSettlementId = d.Origin, TargetSettlementId = d.Target, Resource = r, Amount = d.Amount });
                _order.Add(id);
            }
            error = null;
            return true;
        }

        /// <summary>Nach dem Laden: Karawanen ohne lebende Einheit verwerfen (Rückfall Regel 6). Liefert die Zahl verworfener.</summary>
        internal int DropOrphans()
        {
            int n = 0;
            for (int i = _order.Count - 1; i >= 0; i--)
            {
                if (_s.Ctx.Units.Exists(_order[i])) continue;
                _caravans.Remove(_order[i]);
                _order.RemoveAt(i);
                n++;
            }
            return n;
        }
    }

    [Serializable]
    public sealed class CaravanDto
    {
        public int UnitIndex, UnitGeneration;
        public int Origin, Target;
        public string Resource;
        public int Amount;
    }
}
