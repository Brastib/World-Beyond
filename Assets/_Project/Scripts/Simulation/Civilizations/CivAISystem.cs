using System;
using System.Collections.Generic;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Simulation.Civilizations
{
    /// <summary>
    /// Strategische KI jeder Zivilisation (ICivAI, ~alle 5 Spielsekunden, je Civ versetzt). Die Welt entwickelt sich ohne
    /// Spielereingriff (Sandbox, INTEGRATION.md §0 Regel 4). Utility-Bewertung pro Siedlung: Engpässe zuerst (Nahrung,
    /// Wohnraum, Lager), dann Rohstoffe, Verarbeitung, Forschung, Verteidigung; danach Expansion durch Siedler.
    /// Jede Entscheidung wird mit Grund protokolliert (CivDecisionEvent, ICivDecisionLog).
    /// Der Kriegsbewerter aus Phase 4 wird über ICivWarEvaluator eingehängt, falls registriert.
    /// </summary>
    public sealed class CivAISystem : ISimulationSystem, ICivDecisionLog
    {
        public const int DecisionIntervalTicks = 50;
        public const int MaxLogPerCiv = 20;

        private readonly CivState _s;
        private readonly SettlementRegistry _settlements;
        private readonly TechTree _tech;
        private readonly Recruitment _recruitment;
        private readonly ExpansionPlanner _planner;
        private readonly SettlerSystem _settlers;
        private readonly TerritoryService _territory;
        private readonly Dictionary<int, List<CivDecisionEvent>> _log = new Dictionary<int, List<CivDecisionEvent>>();
        private readonly List<TileScore> _tiles = new List<TileScore>();

        public CivAISystem(CivState state, SettlementRegistry settlements, TechTree tech, Recruitment recruitment, ExpansionPlanner planner, SettlerSystem settlers, TerritoryService territory)
        {
            _s = state; _settlements = settlements; _tech = tech; _recruitment = recruitment; _planner = planner; _settlers = settlers; _territory = territory;
            _s.Ctx.Events.Subscribe<CivDecisionEvent>(Record);
        }

        public string Name => "CivAI";
        public int Order => SystemOrder.AI;
        public int TickInterval => 1;

        public void Tick(SimContext ctx)
        {
            long now = ctx.Clock.Tick;
            foreach (int civId in _s.CivOrder)
            {
                var civ = _s.Civs[civId];
                if (!civ.Alive || now < civ.NextAiTick) continue;
                civ.NextAiTick = now + DecisionIntervalTicks;
                Decide(ctx, civ);
            }
        }

        private void Decide(SimContext ctx, Civilization civ)
        {
            ChooseResearch(civ);
            var ids = _s.SettlementListOf(civ.Id).ToArray();
            foreach (int sid in ids)
            {
                var st = _s.Settlement(sid);
                if (st == null) continue;
                DecideBuilding(civ, st);
            }
            DecideExpansion(civ);
            if (ctx.TryGet<ICivWarEvaluator>(out var war)) war.EvaluateAndAct(civ.Id);
        }

        // ---------------- Forschung ----------------

        private void ChooseResearch(Civilization civ)
        {
            bool hungry = false, dying = false;
            foreach (int sid in _s.SettlementListOf(civ.Id))
            {
                var st = _s.Settlements[sid];
                if (st.FoodBalancePerYear < 0) hungry = true;
                if (st.DeathsPerYear > st.BirthsPerYear && st.Population > 20) dying = true;
            }
            string want = hungry ? "tech.crop_rotation" : dying ? "tech.medicine" : null;
            if (want != null && _tech.TryGetTech(want, out var t) && _tech.CanResearch(civ, t) && !civ.CurrentResearch.Equals(t) && civ.ResearchPoints < 1)
            {
                _tech.SetResearch(civ.Id, t);
                Log(civ.Id, 0, $"Forschung: {_s.Content.Techs.Get(t).DisplayName}", hungry ? "Nahrungsmangel" : "Mehr Todesfälle als Geburten");
            }
        }

        // ---------------- Bauen ----------------

        private void DecideBuilding(Civilization civ, Settlement st)
        {
            int sites = 0;
            foreach (var b in st.Buildings)
            {
                var r = _s.Buildings.GetRecord(b);
                if (r != null && !r.Completed) sites++;
            }
            if (sites >= 1 + (int)st.Tier / 2) return;

            double need = SettlementSimulationSystem.FoodNeedPerYear(st);
            double food = st.Stock[(int)ResourceType.Food];
            double pop = st.Population;

            // 1. Nahrung
            if (st.FoodBalancePerYear < need * 0.1 || food < need * 0.5)
            {
                bool fish = _tech.IsUnlocked(civ.Id, "tech.fishing") && WaterNear(st) && Count(st, "building.fishery") < 2;
                if (TryBuild(civ, st, FreeFoodSlots(st) > 0 && st.Unemployed == 0 ? null : (fish && Count(st, "building.farm") >= 2 ? "building.fishery" : "building.farm"),
                    "Nahrungsbilanz zu niedrig")) return;
            }
            // 2. Wohnraum
            if (pop > st.HousingCapacity * 0.85)
                if (TryBuild(civ, st, BestAvailable(civ, st, "building.apartment", "building.house", "building.hut"), "Wohnraum fast voll")) return;
            // 3. Lager
            foreach (var r in Resources.Stockable)
                if (st.Stock[(int)r] >= st.StorageCapacity * 0.9 && SettlementSimulationSystem.StockTarget(st, r) > st.StorageCapacity * 0.9)
                {
                    if (TryBuild(civ, st, BestAvailable(civ, st, "building.warehouse", "building.storehouse"), $"Lager für {r} voll")) return;
                    break;
                }
            // 4. Rohstoffe
            if (st.Stock[(int)ResourceType.Wood] < 40 + pop && ActiveCount(st, "building.woodcutter") < 1 + (int)st.Tier)
                if (TryBuild(civ, st, "building.woodcutter", "Holz knapp")) return;
            if (st.Stock[(int)ResourceType.Stone] < 40 + pop * 0.5 && ActiveCount(st, "building.quarry") < 1 + (int)st.Tier / 2)
                if (TryBuild(civ, st, "building.quarry", "Stein knapp")) return;
            // 5. Forschung
            if (Count(st, "building.library") < 1 + (st.Tier >= SettlementTier.City ? 1 : 0))
                if (TryBuild(civ, st, "building.library", "Forschung beschleunigen")) return;
            // 6. Metall und Werkzeug
            if (st.Stock[(int)ResourceType.Ore] < 40 && ActiveCount(st, "building.mine") < 1 + (int)st.Tier / 2)
                if (TryBuild(civ, st, "building.mine", "Erz für Metall")) return;
            if (st.Stock[(int)ResourceType.Ore] > 20 && Count(st, "building.smelter") < 1)
                if (TryBuild(civ, st, "building.smelter", "Erz zu Metall verarbeiten")) return;
            if (st.Stock[(int)ResourceType.Metal] > 10 && Count(st, "building.workshop") < 1)
                if (TryBuild(civ, st, "building.workshop", "Werkzeuge steigern Erträge")) return;
            if (Count(st, "building.gold_mine") < 1)
                if (TryBuild(civ, st, "building.gold_mine", "Goldvorkommen nutzen")) return;
            if (Count(st, "building.oil_well") < 1)
                if (TryBuild(civ, st, "building.oil_well", "Öl für die Moderne")) return;
            // 7. Verteidigung: bei Grenzkontakt oder ab Stadtgröße, verstärkt durch Militarismus
            if (BorderContact(civ.Id) || (st.Tier >= SettlementTier.Town && civ.Traits.Militarism > 1.1f))
            {
                string def = BestAvailable(civ, st, "building.stone_wall", "building.barracks", "building.palisade");
                if (def != null && TryBuild(civ, st, def, "Grenze sichern")) return;
            }
            // 8. Arbeitslose beschäftigen: Nahrung als sicherer Überschuss
            if (st.Unemployed > 4)
                TryBuild(civ, st, pop > st.HousingCapacity * 0.7 ? BestAvailable(civ, st, "building.house", "building.hut") : "building.farm", $"{st.Unemployed} Arbeitslose");
        }

        private int FreeFoodSlots(Settlement st)
        {
            int slots = 0;
            foreach (var b in st.Buildings)
            {
                var r = _s.Buildings.GetRecord(b);
                if (r == null || !r.Completed) continue;
                var d = _s.Content.Buildings.Get(r.Type);
                if (d.Produces == ResourceType.Food) slots += d.JobSlots - r.Workers;
            }
            return slots;
        }

        private string BestAvailable(Civilization civ, Settlement st, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (!_s.Content.Buildings.TryGetByKey(k, out var t)) continue;
                var d = _s.Content.Buildings.Get(t);
                if (st.Tier >= d.MinTier && _tech.IsUnlocked(civ.Id, d.RequiredTechKey) && _settlements.CountOfType(st, t) < d.MaxPerSettlement) return k;
            }
            return null;
        }

        private int Count(Settlement st, string key) =>
            _s.Content.Buildings.TryGetByKey(key, out var t) ? _settlements.CountOfType(st, t) : 0;

        private int ActiveCount(Settlement st, string key)
        {
            if (!_s.Content.Buildings.TryGetByKey(key, out var t)) return 0;
            int n = 0;
            foreach (var b in st.Buildings)
            {
                var r = _s.Buildings.GetRecord(b);
                if (r != null && r.Type.Equals(t) && !r.Idle) n++;
            }
            return n;
        }

        private bool WaterNear(Settlement st)
        {
            int r = TerritoryService.RadiusOf(st.Tier);
            for (int y = st.Y - r; y <= st.Y + r; y++)
                for (int x = st.X - r; x <= st.X + r; x++)
                    if (_s.World.InBounds(x, y) && _s.World.IsWater(x, y) && _s.World.Get(x, y).OwnerCivId == st.CivId) return true;
            return false;
        }

        private bool BorderContact(int civId)
        {
            foreach (int other in _s.CivOrder)
                if (other != civId && _s.Civs[other].Alive && _territory.SharedBorderLength(civId, other) > 0) return true;
            return false;
        }

        /// <summary>Wählt den besten Bauplatz im eigenen Gebiet und legt die Baustelle an. Kosten fehlen → false.</summary>
        private bool TryBuild(Civilization civ, Settlement st, string key, string reason)
        {
            if (key == null || !_s.Content.Buildings.TryGetByKey(key, out var type)) return false;
            var d = _s.Content.Buildings.Get(type);
            if (d.RequiredTechKey != null && !_tech.IsUnlocked(civ.Id, d.RequiredTechKey)) return false;
            if (st.Tier < d.MinTier || _settlements.CountOfType(st, type) >= d.MaxPerSettlement) return false;
            foreach (var r in Resources.Stockable) if (st.Stock[(int)r] < d.Cost[r]) return false;

            int radius = TerritoryService.RadiusOf(st.Tier) - 1;
            var world = _s.World;
            world.FindBestTiles(st.X + 1, st.Y + 1, radius, (int x, int y, in TileData t) =>
            {
                if (t.OwnerCivId != civ.Id || t.Building.IsValid) return 0f;
                if (!_settlements.CanBuild(st, type, x, y, out _)) return 0f;
                float v = PlacementValue(d, x, y);
                if (v <= 0f) return 0f;
                int dx = x - st.X, dy = y - st.Y;
                return v / (1f + (float)Math.Sqrt(dx * dx + dy * dy) * 0.05f);
            }, 1, _tiles);
            if (_tiles.Count == 0) return false;
            var best = _tiles[0];
            if (!_settlements.TryPlaceBuilding(st, type, best.X, best.Y, out _, out _)) return false;
            Log(civ.Id, st.Id, $"{st.Name}: baut {d.DisplayName}", reason);
            return true;
        }

        /// <summary>Standortwert eines Gebäudes: Rohstoffe im Einzugsbereich, Fruchtbarkeit, Wasser; sonst Nähe zum Kern.</summary>
        private float PlacementValue(BuildingTypeData d, int x, int y)
        {
            var world = _s.World;
            int rad = d.HarvestFrom != ResourceType.None || d.NeedsWater || d.UsesFertility ? d.CatchmentRadius : 0;
            if (rad == 0) return 1f;
            float v = 0f;
            for (int yy = y - rad; yy < y + d.SizeY + rad; yy++)
                for (int xx = x - rad; xx < x + d.SizeX + rad; xx++)
                {
                    if (!world.InBounds(xx, yy)) continue;
                    ref readonly var t = ref world.Get(xx, yy);
                    if (d.HarvestFrom != ResourceType.None && t.Resource == d.HarvestFrom && !t.Building.IsValid && (t.OwnerCivId == 0 || t.OwnerCivId == world.Get(x, y).OwnerCivId))
                        v += t.ResourceAmount;
                    else if (d.NeedsWater && world.IsWater(xx, yy)) v += 1f;
                    else if (d.UsesFertility && !world.IsWater(xx, yy)) v += t.Fertility / 255f;
                }
            if (d.NeedsWater && v < 4f) return 0f;
            return v;
        }

        // ---------------- Expansion ----------------

        /// <summary>Tochtersiedlungen erst aus gewachsenen Orten (sonst zerfällt eine Civ in Weiler zu 5 Personen).</summary>
        public const int MinAdultsForSettlers = 20;

        private void DecideExpansion(Civilization civ)
        {
            var list = _s.SettlementListOf(civ.Id);
            int traveling = _settlers.CountTraveling(civ.Id);
            int pending = 0;
            foreach (int sid in list) pending += _recruitment.PendingCount(_s.Settlements[sid], CivContent.UnitSettler);
            int maxParallel = 1 + list.Count / 3;
            if (traveling + pending >= maxParallel) return;

            Settlement best = null;
            double bestScore = 0;
            foreach (int sid in list)
            {
                var st = _s.Settlements[sid];
                if (st.ExpansionTargetX >= 0 && !st.PlayerChoseTarget) continue;
                double need = SettlementSimulationSystem.FoodNeedPerYear(st);
                bool canPay = st.Stock[(int)ResourceType.Food] >= SettlerSystem.SettlerCost.Food + need * 0.5
                    && st.Stock[(int)ResourceType.Wood] >= SettlerSystem.SettlerCost.Wood && st.Adults >= MinAdultsForSettlers;
                if (!canPay) continue;
                double crowd = st.Population / Math.Max(1.0, st.HousingCapacity);
                double score = (st.Population / 20.0) * (0.5 + crowd) * civ.Traits.Expansion;
                if (st.PlayerChoseTarget) score += 100;   // Spielervorgabe zuerst
                if (score > bestScore) { bestScore = score; best = st; }
            }
            if (best == null || bestScore < 1.0) return;

            int tx = best.ExpansionTargetX, ty = best.ExpansionTargetY;
            if (!best.PlayerChoseTarget || tx < 0)
            {
                if (!_planner.TryFindSite(civ.Id, best.X, best.Y, ExpansionPlanner.SearchRadius, out tx, out ty, out _)) return;
            }
            var res = _recruitment.Enqueue(civ.Id, CivContent.UnitSettler, 1, best.Id, o => { o.TargetX = tx; o.TargetY = ty; });
            if (res != RecruitResult.Queued) return;
            best.ExpansionTargetX = tx; best.ExpansionTargetY = ty;
            best.PlayerChoseTarget = false;
            Log(civ.Id, best.Id, $"{best.Name}: schickt Siedler nach ({tx}, {ty})", $"{(int)best.Population} Einwohner, Vorräte reichen für eine neue Siedlung");
        }

        // ---------------- Protokoll ----------------

        private void Log(int civId, int settlementId, string decision, string reason) =>
            _s.Ctx.Events.Publish(new CivDecisionEvent { CivId = civId, SettlementId = settlementId, Decision = decision, Reason = reason });

        private void Record(CivDecisionEvent e)
        {
            if (!_log.TryGetValue(e.CivId, out var list)) { list = new List<CivDecisionEvent>(); _log.Add(e.CivId, list); }
            list.Add(e);
            if (list.Count > MaxLogPerCiv) list.RemoveAt(0);
        }

        public void CopyRecent(int civId, List<CivDecisionEvent> results)
        {
            results.Clear();
            if (_log.TryGetValue(civId, out var list)) results.AddRange(list);
        }
    }
}
