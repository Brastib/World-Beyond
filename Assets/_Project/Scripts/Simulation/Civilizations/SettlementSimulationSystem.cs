using System;
using System.Collections.Generic;
using WorldBeyond.Core;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Simulation.Civilizations
{
    /// <summary>
    /// Kern der Zivilisationswirtschaft (Order 200). Jede Siedlung wird mit der tatsächlich vergangenen Zeit fortgeschrieben:
    /// Arbeit verteilen → Bau → Produktion (Kacheln abbauen, verarbeiten) → Verbrauch → Bevölkerung → Migration →
    /// Rekrutierung → Stufe. Jeder Schritt hängt am vorherigen: kein Ertrag ohne Arbeiter, keine Arbeiter ohne Einwohner,
    /// keine Einwohner ohne Nahrung und Wohnraum.
    /// </summary>
    public sealed class SettlementSimulationSystem : ISimulationSystem
    {
        // ---- Raten pro Spieljahr (abgestimmt im Langzeittest, Docs/drafts/CIVILIZATION_DESIGN_DRAFT.md §15) ----
        public const double FoodPerAdult = 1.0, FoodPerChild = 0.6, FoodPerElder = 0.8;
        public const double BirthRate = 0.16;          // je Erwachsenem bei voller Versorgung und freiem Wohnraum
        public const double MaturationRate = 1.0 / 6;  // Kinder → Erwachsene
        public const double AgingRate = 1.0 / 40;      // Erwachsene → Alte
        public const double ChildDeathRate = 0.01, AdultDeathRate = 0.008, ElderDeathRate = 0.15;
        public const double StarvationRate = 0.4;      // Anteil der Hungernden, die pro Jahr sterben
        public const double ToolWearPerWorker = 0.03;
        public const double BaseResearchPerPerson = 0.05;
        public const int BuildersPerSite = 3;

        private readonly CivState _s;
        private readonly SettlementRegistry _settlements;
        private readonly TechTree _tech;
        private readonly CivLod _lod;
        private readonly Recruitment _recruitment;
        private readonly TerritoryService _territory;
        private readonly List<int> _due = new List<int>();
        private readonly List<EntityId> _tmpBuildings = new List<EntityId>();

        public SettlementSimulationSystem(CivState state, SettlementRegistry settlements, TechTree tech, CivLod lod, Recruitment recruitment, TerritoryService territory)
        {
            _s = state; _settlements = settlements; _tech = tech; _lod = lod; _recruitment = recruitment; _territory = territory;
        }

        public string Name => "Settlements";
        public int Order => SystemOrder.Economy;
        public int TickInterval => 1;

        public void Tick(SimContext ctx)
        {
            long now = ctx.Clock.Tick;
            _due.Clear();
            foreach (int id in _s.SettlementOrder)
            {
                var st = _s.Settlements[id];
                bool active = _lod.IsActive(st);
                if (active != st.ActiveLod)
                {
                    st.ActiveLod = active;
                    // Wechsel auf aktiv: spätestens in einer Spielsekunde aktualisieren. Kein Zustandswechsel.
                    long next = st.LastUpdateTick + CivLod.IntervalOf(st);
                    if (next < st.NextUpdateTick || !active) st.NextUpdateTick = Math.Max(now, next);
                }
                if (now >= st.NextUpdateTick) _due.Add(id);
            }
            foreach (int id in _due)
            {
                var st = _s.Settlement(id);
                if (st == null) continue;   // in diesem Tick aufgegeben
                double dt = CivTime.TicksToYears(now - st.LastUpdateTick);
                st.LastUpdateTick = now;
                st.NextUpdateTick = now + CivLod.IntervalOf(st);
                if (dt > 0) Update(st, dt, now);
            }
        }

        /// <summary>Schreibt eine Siedlung um dt Spieljahre fort. Öffentlich für Tests der Detailstufen.</summary>
        public void Update(Settlement st, double dt, long now)
        {
            var civ = _s.Civ(st.CivId);
            if (civ == null || !civ.Alive) return;
            st.Modifiers.RemoveAll(m => m.UntilTick <= now);
            _tech.Bonuses(civ, out float foodBonus, out float prodBonus, out float deathReduction);
            Array.Clear(st.ProductionPerYear, 0, st.ProductionPerYear.Length);
            Array.Clear(st.ConsumptionPerYear, 0, st.ConsumptionPerYear.Length);

            AssignLabor(st);
            Construct(st, dt, civ);
            Produce(st, dt, civ, foodBonus, prodBonus, now);
            double hungerShare = Consume(st, dt);
            Population(st, dt, civ, hungerShare, deathReduction, now);
            Migrate(st, hungerShare);
            _recruitment.Process(st, dt);
            UpdateTier(st);

            if (st.Population < 0.5) _settlements.Abandon(st);
        }

        // ---------------- Arbeit ----------------

        private static readonly JobKind[] ProductionOrder =
        {
            JobKind.Woodcutter, JobKind.Quarrier, JobKind.Miner, JobKind.Smelter, JobKind.Toolmaker, JobKind.OilWorker, JobKind.Scholar, JobKind.Guard,
        };

        private readonly int[] _slots = new int[Settlement.JobKindCount];

        private void AssignLabor(Settlement st)
        {
            Array.Clear(_slots, 0, _slots.Length);
            Array.Clear(st.Workers, 0, st.Workers.Length);
            int sites = 0;
            foreach (var b in st.Buildings)
            {
                var r = _s.Buildings.GetRecord(b);
                if (r == null) continue;
                r.Workers = 0;
                if (!r.Completed) { sites++; continue; }
                var d = _s.Content.Buildings.Get(r.Type);
                if (d.Job == JobKind.None || r.Idle) continue;
                // Rohstoffbetriebe ruhen, solange der Zielvorrat erreicht ist (kein Horten bis zur Lagergrenze).
                if (d.Produces != ResourceType.Food && Resources.IsStockable(d.Produces)
                    && st.Stock[(int)d.Produces] >= Math.Min(StockTarget(st, d.Produces), st.StorageCapacity * 0.95)) continue;
                _slots[(int)d.Job] += d.JobSlots;
            }
            _slots[(int)JobKind.Builder] = sites * BuildersPerSite;

            int free = (int)Math.Floor(st.Adults);
            // 1. Nahrung bis zum Bedarf plus 15 % Reserve (bei kleinem Vorrat 40 %).
            double need = FoodNeedPerYear(st);
            double reserve = st.Stock[(int)ResourceType.Food] < need ? 1.4 : 1.15;
            double perFarmer = Math.Max(0.5, ExpectedFoodPerWorker(st));
            int wantFood = (int)Math.Ceiling(need * reserve / perFarmer);
            free = Fill(st, JobKind.Farmer, Math.Min(wantFood, _slots[(int)JobKind.Farmer]), free);
            free = Fill(st, JobKind.Fisher, Math.Min(Math.Max(0, wantFood - st.Workers[(int)JobKind.Farmer]), _slots[(int)JobKind.Fisher]), free);
            // 2. Bauen, 3. Rohstoffe und Verarbeitung, 4. restliche Nahrungsplätze.
            free = Fill(st, JobKind.Builder, _slots[(int)JobKind.Builder], free);
            foreach (var j in ProductionOrder) free = Fill(st, j, _slots[(int)j], free);
            free = Fill(st, JobKind.Farmer, _slots[(int)JobKind.Farmer] - st.Workers[(int)JobKind.Farmer], free);
            free = Fill(st, JobKind.Fisher, _slots[(int)JobKind.Fisher] - st.Workers[(int)JobKind.Fisher], free);
            st.Unemployed = free;

            // Arbeiter auf die Gebäude verteilen (Reihenfolge der Gebäudeliste).
            var left = (int[])st.Workers.Clone();
            foreach (var b in st.Buildings)
            {
                var r = _s.Buildings.GetRecord(b);
                if (r == null || !r.Completed || r.Idle) continue;
                var d = _s.Content.Buildings.Get(r.Type);
                if (d.Job == JobKind.None) continue;
                int n = Math.Min(d.JobSlots, left[(int)d.Job]);
                r.Workers = n;
                left[(int)d.Job] -= n;
            }
        }

        private static int Fill(Settlement st, JobKind job, int wanted, int free)
        {
            int n = Math.Max(0, Math.Min(wanted, free));
            st.Workers[(int)job] += n;
            return free - n;
        }

        /// <summary>
        /// Vorrat, den eine Siedlung von einem Gut anstrebt. Darüber arbeiten die erzeugenden Betriebe nicht weiter;
        /// die KI baut Lager nur, wenn dieses Ziel die Lagergrenze übersteigt.
        /// </summary>
        internal static double StockTarget(Settlement st, ResourceType r)
        {
            double pop = st.Population;
            switch (r)
            {
                case ResourceType.Food: return FoodNeedPerYear(st) * 2.0 + 50;
                case ResourceType.Wood: return 80 + pop * 3;
                case ResourceType.Stone: return 60 + pop * 2;
                case ResourceType.Ore: return 40 + pop;
                case ResourceType.Metal: return 30 + pop;
                case ResourceType.Tools: return 20 + pop * 0.5;
                default: return 50 + pop;
            }
        }

        internal static double FoodNeedPerYear(Settlement st) =>
            st.Children * FoodPerChild + st.Adults * FoodPerAdult + st.Elders * FoodPerElder;

        private double ExpectedFoodPerWorker(Settlement st)
        {
            double sum = 0; int n = 0;
            foreach (var b in st.Buildings)
            {
                var r = _s.Buildings.GetRecord(b);
                if (r == null || !r.Completed) continue;
                var d = _s.Content.Buildings.Get(r.Type);
                if (d.Produces != ResourceType.Food) continue;
                sum += d.OutputPerWorkerYear * SiteFactor(r, d);
                n++;
            }
            return n == 0 ? 1.0 : sum / n;
        }

        // ---------------- Bau ----------------

        private void Construct(Settlement st, double dt, Civilization civ)
        {
            int builders = st.Workers[(int)JobKind.Builder];
            if (builders == 0) return;
            _tmpBuildings.Clear();
            foreach (var b in st.Buildings)
            {
                var r = _s.Buildings.GetRecord(b);
                if (r != null && !r.Completed) _tmpBuildings.Add(b);
            }
            if (_tmpBuildings.Count == 0) return;
            int i = 0;
            foreach (var b in _tmpBuildings)
            {
                var r = _s.Buildings.GetRecord(b);
                int share = builders / _tmpBuildings.Count + (i++ < builders % _tmpBuildings.Count ? 1 : 0);
                r.Workers = share;
                r.Progress += share * dt * civ.Traits.Industry;
                var d = _s.Content.Buildings.Get(r.Type);
                if (r.Progress >= d.BuildWorkYears)
                {
                    r.Completed = true;
                    r.Progress = d.BuildWorkYears;
                    _settlements.RecomputeCapacities(st);
                    _s.Ctx.Events.Publish(new BuildingCompletedEvent { Building = b, SettlementId = st.Id, X = r.X, Y = r.Y });
                }
            }
        }

        // ---------------- Produktion ----------------

        private void Produce(Settlement st, double dt, Civilization civ, float foodBonus, float prodBonus, long now)
        {
            int totalWorkers = 0;
            for (int j = 0; j < st.Workers.Length; j++) if (j != (int)JobKind.Builder) totalWorkers += st.Workers[j];
            // Werkzeuge: +25 % auf alle Erträge, solange genug im Lager; sie verschleißen mit der Arbeit.
            double toolsNeeded = totalWorkers * ToolWearPerWorker * dt;
            bool hasTools = toolsNeeded > 0 && st.Stock[(int)ResourceType.Tools] >= toolsNeeded;
            if (hasTools)
            {
                st.Stock[(int)ResourceType.Tools] -= toolsNeeded;
                st.ConsumptionPerYear[(int)ResourceType.Tools] += (float)(toolsNeeded / dt);
            }
            double toolFactor = hasTools ? 1.25 : 1.0;
            bool famine = st.HasModifier(CivEventKind.Famine, now);
            double research = st.Population * BaseResearchPerPerson * dt;

            foreach (var b in st.Buildings)
            {
                var r = _s.Buildings.GetRecord(b);
                if (r == null || !r.Completed) continue;
                var d = _s.Content.Buildings.Get(r.Type);

                if (d.ResearchPerWorkerYear > 0f)
                {
                    research += r.Workers * d.ResearchPerWorkerYear * dt;
                    continue;
                }
                if (d.Produces == ResourceType.None) continue;

                if (d.HarvestFrom != ResourceType.None && r.Workers == 0)
                {
                    // Ohne Arbeiter prüfen, ob es überhaupt noch Vorkommen gibt (sonst bliebe der Betrieb ewig "idle").
                    if (r.Idle && HasDeposit(r, d)) r.Idle = false;
                    continue;
                }
                if (r.Workers == 0) continue;

                double mult = civ.Traits.Industry * toolFactor;
                if (d.Produces == ResourceType.Food) mult *= (1.0 + foodBonus) * (famine ? 0.3 : 1.0);
                else mult *= 1.0 + prodBonus;
                double amount = r.Workers * d.OutputPerWorkerYear * SiteFactor(r, d) * mult * dt;

                // Verarbeitung: durch Eingangsgüter begrenzt.
                if (d.Input1 != ResourceType.None) amount = Math.Min(amount, st.Stock[(int)d.Input1] / d.Input1PerOutput);
                if (d.Input2 != ResourceType.None) amount = Math.Min(amount, st.Stock[(int)d.Input2] / d.Input2PerOutput);
                // Nicht mehr erzeugen, als ins Lager passt (sonst würde die Welt umsonst abgebaut).
                amount = Math.Min(amount, Math.Max(0, st.StorageCapacity - st.Stock[(int)d.Produces]));
                if (amount <= 0) continue;

                if (d.HarvestFrom != ResourceType.None)
                {
                    r.OutputCarry += amount;
                    int whole = (int)Math.Floor(r.OutputCarry);
                    if (whole <= 0) continue;
                    int taken = Harvest(r, d, whole);
                    r.OutputCarry -= taken;
                    if (taken < whole)
                    {
                        r.Idle = true;          // Vorkommen im Einzugsbereich erschöpft
                        r.OutputCarry = 0;
                    }
                    amount = taken;
                }
                if (d.Input1 != ResourceType.None) Spend(st, d.Input1, amount * d.Input1PerOutput, dt);
                if (d.Input2 != ResourceType.None) Spend(st, d.Input2, amount * d.Input2PerOutput, dt);
                double put = Economy.Deposit(st, d.Produces, amount);
                st.ProductionPerYear[(int)d.Produces] += (float)(put / dt);
            }

            research *= civ.Traits.Research;
            st.ResearchPerYear = (float)(research / dt);
            _tech.AddResearch(civ, research);
        }

        private static void Spend(Settlement st, ResourceType r, double amount, double dt)
        {
            st.Stock[(int)r] = Math.Max(0, st.Stock[(int)r] - amount);
            st.ConsumptionPerYear[(int)r] += (float)(amount / dt);
        }

        /// <summary>Standortfaktor: Fruchtbarkeit für Farmen, Wasseranteil für Fischerei, sonst 1.</summary>
        private double SiteFactor(BuildingStore.Record r, BuildingTypeData d)
        {
            if (!d.UsesFertility && !d.NeedsWater) return 1.0;
            var world = _s.World;
            int rad = d.CatchmentRadius;
            double fert = 0; int n = 0, water = 0;
            for (int y = r.Y - rad; y < r.Y + r.SizeY + rad; y++)
                for (int x = r.X - rad; x < r.X + r.SizeX + rad; x++)
                {
                    if (!world.InBounds(x, y)) continue;
                    if (world.IsWater(x, y)) { water++; continue; }
                    fert += world.Get(x, y).Fertility;
                    n++;
                }
            if (d.NeedsWater) return Math.Min(1.0, water / 8.0);
            return n == 0 ? 0.3 : 0.3 + 1.2 * (fert / n / 255.0);
        }

        private bool HasDeposit(BuildingStore.Record r, BuildingTypeData d)
        {
            var world = _s.World;
            int rad = d.CatchmentRadius;
            for (int y = r.Y - rad; y < r.Y + r.SizeY + rad; y++)
                for (int x = r.X - rad; x < r.X + r.SizeX + rad; x++)
                {
                    if (!world.InBounds(x, y)) continue;
                    ref readonly var t = ref world.Get(x, y);
                    if (t.Resource == d.HarvestFrom && t.ResourceAmount > 0 && Claimable(t, r)) return true;
                }
            return false;
        }

        // Abbau nur auf neutralem oder eigenem Gebiet und nicht unter fremden Gebäuden.
        private bool Claimable(in TileData t, BuildingStore.Record r)
        {
            if (t.Building.IsValid) return false;
            var st = _s.Settlement(r.SettlementId);
            return t.OwnerCivId == 0 || (st != null && t.OwnerCivId == st.CivId);
        }

        /// <summary>Baut bis zu amount Einheiten aus den nächstgelegenen Vorkommen im Einzugsbereich ab (IWorldGrid.TryHarvest).</summary>
        private int Harvest(BuildingStore.Record r, BuildingTypeData d, int amount)
        {
            var world = _s.World;
            int rad = d.CatchmentRadius;
            int cx = r.X, cy = r.Y, total = 0;
            // Ringweise von innen nach außen: deterministisch und bevorzugt nahe Kacheln.
            for (int ring = 0; ring <= rad + Math.Max(r.SizeX, r.SizeY) && total < amount; ring++)
            {
                for (int y = cy - ring; y <= cy + ring && total < amount; y++)
                {
                    for (int x = cx - ring; x <= cx + ring && total < amount; x++)
                    {
                        if (Math.Max(Math.Abs(x - cx), Math.Abs(y - cy)) != ring || !world.InBounds(x, y)) continue;
                        ref readonly var t = ref world.Get(x, y);
                        if (t.Resource != d.HarvestFrom || t.ResourceAmount == 0 || !Claimable(t, r)) continue;
                        if (world.TryHarvest(x, y, amount - total, out int taken))
                        {
                            total += taken;
                            r.LastWorkX = x; r.LastWorkY = y;
                        }
                    }
                }
            }
            return total;
        }

        // ---------------- Verbrauch ----------------

        /// <summary>Nahrung essen. Liefert den Anteil der Bevölkerung, der in diesem Zeitraum hungerte (0..1).</summary>
        private static double Consume(Settlement st, double dt)
        {
            double need = FoodNeedPerYear(st) * dt;
            double have = st.Stock[(int)ResourceType.Food];
            double eaten = Math.Min(have, need);
            st.Stock[(int)ResourceType.Food] = have - eaten;
            st.ConsumptionPerYear[(int)ResourceType.Food] += (float)(eaten / dt);
            st.FoodBalancePerYear = st.ProductionPerYear[(int)ResourceType.Food] - (float)(need / dt);
            return need <= 1e-9 ? 0 : (need - eaten) / need;
        }

        // ---------------- Bevölkerung ----------------

        private void Population(Settlement st, double dt, Civilization civ, double hungerShare, float deathReduction, long now)
        {
            double pop = st.Population;
            double stockYears = st.Stock[(int)ResourceType.Food] / Math.Max(1e-6, FoodNeedPerYear(st));
            double foodFactor = hungerShare > 0 ? 0 : Math.Min(1.0, 0.5 + stockYears);
            double freeHousing = st.HousingCapacity - pop;
            double housingFactor = Math.Max(0, Math.Min(1.0, freeHousing / Math.Max(2.0, 0.15 * st.HousingCapacity)));
            double boom = st.HasModifier(CivEventKind.BabyBoom, now) ? 2.0 : 1.0;
            double births = st.Adults * BirthRate * civ.Traits.Fertility * foodFactor * housingFactor * boom * dt;
            // Geburten nie über den freien Wohnraum hinaus.
            births = Math.Min(births, Math.Max(0, freeHousing));

            double deathMult = (1.0 - deathReduction) * (st.HasModifier(CivEventKind.Plague, now) ? 4.0 : 1.0);
            double dChildren = st.Children * Math.Min(1, ChildDeathRate * deathMult * dt);
            double dAdults = st.Adults * Math.Min(1, AdultDeathRate * deathMult * dt);
            double dElders = st.Elders * Math.Min(1, ElderDeathRate * deathMult * dt);
            // Hunger: ein Teil der Hungernden stirbt, Alte und Kinder zuerst stärker.
            double starve = Math.Min(1.0, hungerShare * StarvationRate * dt * 2.0);
            dChildren += (st.Children - dChildren) * starve * 1.3;
            dAdults += (st.Adults - dAdults) * starve * 0.8;
            dElders += (st.Elders - dElders) * starve * 1.5;

            double mature = st.Children * Math.Min(1, MaturationRate * dt);
            double age = st.Adults * Math.Min(1, AgingRate * dt);

            st.Children = Math.Max(0, st.Children + births - mature - dChildren);
            st.Adults = Math.Max(0, st.Adults + mature - age - dAdults);
            st.Elders = Math.Max(0, st.Elders + age - dElders);
            st.BirthsPerYear = (float)(births / dt);
            st.DeathsPerYear = (float)((dChildren + dAdults + dElders) / dt);
        }

        // ---------------- Migration ----------------

        /// <summary>
        /// Abstrahierte Binnenwanderung: Bei Hunger oder Überbelegung zieht ein Teil der Einwohner in die nächstgelegene
        /// Siedlung derselben Zivilisation mit freiem Wohnraum und Nahrungsvorrat. Kein Transportweg (bewusste Abstraktion).
        /// </summary>
        private void Migrate(Settlement st, double hungerShare)
        {
            double over = st.Population - st.HousingCapacity;
            double want = Math.Max(over, hungerShare > 0.05 ? st.Population * hungerShare * 0.5 : 0);
            if (want < 1) return;
            Settlement target = null;
            long bestD = long.MaxValue;
            foreach (int sid in _s.SettlementListOf(st.CivId))
            {
                var o = _s.Settlements[sid];
                if (o == st) continue;
                double free = o.HousingCapacity - o.Population;
                if (free < 3 || o.Stock[(int)ResourceType.Food] < FoodNeedPerYear(o) * 0.5) continue;
                long dx = o.X - st.X, dy = o.Y - st.Y, d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; target = o; }
            }
            if (target == null) return;
            double move = Math.Floor(Math.Min(want, target.HousingCapacity - target.Population - 1));
            if (move < 1) return;
            double share = move / st.Population;
            double c = st.Children * share, a = st.Adults * share, e = st.Elders * share;
            st.Children -= c; st.Adults -= a; st.Elders -= e;
            target.Children += c; target.Adults += a; target.Elders += e;
            _s.Ctx.Events.Publish(new MigrationEvent { FromSettlementId = st.Id, ToSettlementId = target.Id, People = (int)move });
        }

        // ---------------- Stufe ----------------

        public static int TierThreshold(SettlementTier t)
        {
            switch (t)
            {
                case SettlementTier.Village: return 20;
                case SettlementTier.Town: return 60;
                case SettlementTier.City: return 180;
                case SettlementTier.Metropolis: return 500;
                default: return 0;
            }
        }

        private void UpdateTier(Settlement st)
        {
            var old = st.Tier;
            double pop = st.Population;
            while (st.Tier < SettlementTier.Metropolis && pop >= TierThreshold(st.Tier + 1)) st.Tier++;
            while (st.Tier > SettlementTier.Camp && pop < TierThreshold(st.Tier) * 0.7) st.Tier--;
            if (st.Tier != old)
            {
                _territory.MarkDirty(st.Id);
                _s.Ctx.Events.Publish(new SettlementTierChangedEvent { SettlementId = st.Id, OldTier = old, NewTier = st.Tier });
            }
        }
    }
}
