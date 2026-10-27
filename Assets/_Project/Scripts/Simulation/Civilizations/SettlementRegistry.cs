using System;
using System.Collections.Generic;
using WorldBeyond.Core;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Simulation.Civilizations
{
    /// <summary>
    /// Siedlungen und ihre Gebäude: Gründen, Bauen, Abreißen, Aufgeben, Besitzwechsel.
    /// Gebäude belegen Kacheln nur über IWorldGrid.SetBuilding; Besitzprüfung macht diese Klasse (SYSTEM_INTERFACES §4.3).
    /// </summary>
    public sealed class SettlementRegistry : ISettlementRegistry
    {
        public const int MinSettlementDistance = 10;      // Kacheln zwischen Ortskernen
        public const int CoreSize = 2;                    // Ortskern 2 × 2 Kacheln

        private readonly CivState _s;
        internal CivilizationRegistry Civs;
        internal TerritoryService Territory;
        internal Economy Economy;

        public SettlementRegistry(CivState state) => _s = state ?? throw new ArgumentNullException(nameof(state));

        // ---------------- Abfragen ----------------

        public bool Exists(int settlementId) => _s.Settlements.ContainsKey(settlementId);
        public IReadOnlyList<int> AllSettlementIds => _s.SettlementOrder;

        public IReadOnlyList<int> GetByCiv(int civId) =>
            _s.SettlementsByCiv.TryGetValue(civId, out var list) ? list : (IReadOnlyList<int>)Array.Empty<int>();

        public SettlementView Get(int settlementId) => TryGet(settlementId, out var v) ? v : default;

        public bool TryGet(int settlementId, out SettlementView view)
        {
            var st = _s.Settlement(settlementId);
            if (st == null) { view = default; return false; }
            int workers = 0;
            for (int j = 0; j < st.Workers.Length; j++) workers += st.Workers[j];
            int sites = 0;
            foreach (var b in st.Buildings)
            {
                var r = _s.Buildings.GetRecord(b);
                if (r != null && !r.Completed) sites++;
            }
            var civ = _s.Civ(st.CivId);
            view = new SettlementView(st.Id, st.CivId, st.Name, st.X, st.Y, st.Tier,
                (int)Math.Round(st.Population), (int)Math.Round(st.Children), (int)Math.Round(st.Adults), (int)Math.Round(st.Elders),
                st.HousingCapacity, st.StorageCapacity, StockOf(st), st.FoodBalancePerYear, st.BirthsPerYear, st.DeathsPerYear,
                workers, st.Unemployed, st.Buildings.Count, sites, st.ActiveLod, (int)CivTime.TicksToYears(st.FoundedTick),
                civ != null && civ.CapitalSettlementId == st.Id);
            return true;
        }

        public int FindAt(int x, int y)
        {
            if (!_s.World.InBounds(x, y)) return 0;
            var b = _s.World.Get(x, y).Building;
            var r = _s.Buildings.GetRecord(b);
            return r != null ? r.SettlementId : 0;
        }

        public void CopyBuildings(int settlementId, List<EntityId> results)
        {
            results.Clear();
            var st = _s.Settlement(settlementId);
            if (st != null) results.AddRange(st.Buildings);
        }

        internal static ResourceCost StockOf(Settlement st)
        {
            var c = new ResourceCost();
            foreach (var r in Resources.Stockable) c[r] = (int)Math.Floor(st.Stock[(int)r]);
            return c;
        }

        // ---------------- Gründen ----------------

        public int Found(int civId, int x, int y) => TryFound(civId, x, y, 6, out int id, out _) ? id : 0;

        public bool CanFoundAt(int civId, int x, int y, out string reason)
        {
            if (_s.Civ(civId) == null || !_s.Civ(civId).Alive) { reason = $"Zivilisation {civId} existiert nicht."; return false; }
            if (!_s.World.CanPlaceFootprint(x, y, CoreSize, CoreSize, out var failure)) { reason = $"Ort ungeeignet ({failure})."; return false; }
            for (int yy = y; yy < y + CoreSize; yy++)
                for (int xx = x; xx < x + CoreSize; xx++)
                {
                    int owner = _s.World.Get(xx, yy).OwnerCivId;
                    if (owner != 0 && owner != civId) { reason = "Gebiet gehört einer anderen Zivilisation."; return false; }
                }
            int min2 = MinSettlementDistance * MinSettlementDistance;
            foreach (int id in _s.SettlementOrder)
            {
                var o = _s.Settlements[id];
                int dx = o.X - x, dy = o.Y - y;
                if (dx * dx + dy * dy < min2) { reason = $"Zu nah an {o.Name} (mindestens {MinSettlementDistance} Kacheln)."; return false; }
            }
            reason = null;
            return true;
        }

        public bool TryFound(int civId, int x, int y, int initialPopulation, out int settlementId, out string reason)
            => TryFound(civId, x, y, initialPopulation, default, false, out settlementId, out reason);

        internal bool TryFound(int civId, int x, int y, int initialPopulation, ResourceCost startStock, bool byPlayer, out int settlementId, out string reason)
        {
            settlementId = 0;
            if (initialPopulation < 1) { reason = "Eine Siedlung braucht mindestens einen Einwohner."; return false; }
            if (!CanFoundAt(civId, x, y, out reason)) return false;

            var civ = _s.Civ(civId);
            var st = new Settlement
            {
                Id = _s.SettlementIds.Next(),
                CivId = civId,
                X = x, Y = y,
                Tier = SettlementTier.Camp,
                FoundedTick = _s.Ctx.Clock.Tick,
                LastUpdateTick = _s.Ctx.Clock.Tick,
                Name = NameGenerator.SettlementName(civ.CultureSeed, civ.NameCounter++),
            };
            st.Adults = Math.Max(1, Math.Round(initialPopulation * 0.7));
            st.Children = Math.Round(initialPopulation * 0.2);
            st.Elders = Math.Max(0, initialPopulation - st.Adults - st.Children);
            foreach (var r in Resources.Stockable) st.Stock[(int)r] = Math.Max(0, startStock[r]);

            // Ortskern sofort fertig: Wald im Footprint wird gerodet, das Holz gehört der Siedlung.
            var center = _s.Content.TownCenterType;
            // Bei Fehlschlag bleibt die ID verbraucht (nie wiederverwenden, ARCHITECTURE §11.1 Regel 2).
            if (!TryOccupyFootprint(st, center, x, y, out var centerId, out reason)) return false;
            var rec = _s.Buildings.GetRecord(centerId);
            rec.Completed = true;
            rec.Progress = _s.Content.Buildings.Get(center).BuildWorkYears;
            st.Buildings.Add(centerId);

            // Versetzte Aktualisierung, damit nicht alle Siedlungen im selben Tick rechnen.
            st.NextUpdateTick = st.FoundedTick + 1 + (st.Id * 7) % CivTime.ActiveUpdateTicks;
            _s.AddSettlement(st);
            if (civ.CapitalSettlementId == 0 || _s.Settlement(civ.CapitalSettlementId) == null) civ.CapitalSettlementId = st.Id;
            RecomputeCapacities(st);
            Territory?.MarkDirty(st.Id);
            _s.Ctx.Events.Publish(new SettlementFoundedEvent { SettlementId = st.Id, CivId = civId, X = x, Y = y, ByPlayer = byPlayer });
            settlementId = st.Id;
            reason = null;
            return true;
        }

        // ---------------- Gebäude ----------------

        /// <summary>Prüft Typ, Technologie, Stufe, Höchstzahl, Gelände und Besitz. Kosten werden hier nicht geprüft.</summary>
        internal bool CanBuild(Settlement st, BuildingTypeId type, int x, int y, out string reason)
        {
            if (!_s.Content.Buildings.TryGet(type, out var d)) { reason = "Unbekannter Gebäudetyp."; return false; }
            if (d.Category == BuildingCategory.Center) { reason = "Ortskerne entstehen nur durch Gründung."; return false; }
            if (d.RequiredTechKey != null && !Civs.Tech.IsUnlocked(st.CivId, d.RequiredTechKey)) { reason = $"Technologie {d.RequiredTechKey} fehlt."; return false; }
            if (st.Tier < d.MinTier) { reason = $"Siedlung muss mindestens {d.MinTier} sein."; return false; }
            if (CountOfType(st, type) >= d.MaxPerSettlement) { reason = $"Höchstens {d.MaxPerSettlement} × {d.DisplayName}."; return false; }
            if (!_s.World.CanPlaceFootprint(x, y, d.SizeX, d.SizeY, out var failure)) { reason = $"Ort ungeeignet ({failure})."; return false; }
            for (int yy = y; yy < y + d.SizeY; yy++)
                for (int xx = x; xx < x + d.SizeX; xx++)
                {
                    if (_s.World.Get(xx, yy).OwnerCivId != st.CivId) { reason = "Nur im eigenen Gebiet."; return false; }
                    if (IsStreet(xx, yy)) { reason = "Straße bleibt frei."; return false; }
                }
            if (UnitsInside(x, y, d.SizeX, d.SizeY)) { reason = "Einheiten stehen im Weg."; return false; }
            reason = null;
            return true;
        }

        private readonly List<EntityId> _unitsInRect = new List<EntityId>();

        // Gebäudekacheln sind gesperrt: Eine Einheit, die beim Bau darauf stünde, fände nie wieder einen Weg (Prüf-Befund).
        private bool UnitsInside(int x, int y, int sizeX, int sizeY)
        {
            if (!_s.Ctx.TryGet<ISpatialIndex>(out var spatial)) return false;
            const float inset = 0.01f;
            float t = WorldConstants.TileSize;
            spatial.QueryRect(x * t + inset, y * t + inset, (x + sizeX) * t - inset, (y + sizeY) * t - inset, _unitsInRect);
            return _unitsInRect.Count > 0;
        }

        /// <summary>Abstand des Straßenrasters: zwischen zwei Straßen liegen 5 Kacheln (Platz für zwei 2×2-Gebäude).</summary>
        public const int StreetSpacing = 6;

        /// <summary>
        /// Weltweites Straßenraster: Jede sechste Spalte und Zeile der Karte bleibt unbebaut. Weil alle Siedlungen dasselbe
        /// Raster nutzen, bilden die Straßen ein zusammenhängendes Netz, auch wo Nachbarorte aneinanderwachsen. So kann sich
        /// keine Siedlung einmauern oder von Nachbarn eingemauert werden; nur Gelände (Wasser, Steilhang) unterbricht Straßen
        /// (Prüf-Befund Seed 12345).
        /// </summary>
        public static bool IsStreet(int x, int y)
        {
            int dx = (x % StreetSpacing + StreetSpacing) % StreetSpacing;
            int dy = (y % StreetSpacing + StreetSpacing) % StreetSpacing;
            return dx == StreetSpacing - 1 || dy == StreetSpacing - 1;
        }

        /// <summary>Legt eine Baustelle an und bezahlt sie aus dem Lager der Siedlung. false ohne Zustandsänderung.</summary>
        internal bool TryPlaceBuilding(Settlement st, BuildingTypeId type, int x, int y, out EntityId id, out string reason)
        {
            id = EntityId.None;
            if (!CanBuild(st, type, x, y, out reason)) return false;
            var d = _s.Content.Buildings.Get(type);
            if (!Economy.TrySpendAt(st.Id, d.Cost)) { reason = $"Nicht genug Material ({d.Cost})."; return false; }
            if (!TryOccupyFootprint(st, type, x, y, out id, out reason))
            {
                Economy.Refund(st, d.Cost);
                return false;
            }
            st.Buildings.Add(id);
            _s.Ctx.Events.Publish(new BuildingPlacedEvent { Building = id, SettlementId = st.Id, X = x, Y = y });
            return true;
        }

        private bool TryOccupyFootprint(Settlement st, BuildingTypeId type, int x, int y, out EntityId id, out string reason)
        {
            var d = _s.Content.Buildings.Get(type);
            id = _s.Buildings.Create(type, st.Id, x, y);
            for (int yy = y; yy < y + d.SizeY; yy++)
            {
                for (int xx = x; xx < x + d.SizeX; xx++)
                {
                    // Wald roden: Holz an die Siedlung (SYSTEM_INTERFACES §4.3).
                    ref readonly var t = ref _s.World.Get(xx, yy);
                    if (t.Resource == ResourceType.Wood && t.ResourceAmount > 0 && _s.World.TryHarvest(xx, yy, t.ResourceAmount, out int wood))
                        Economy.Deposit(st, ResourceType.Wood, wood);   // Rodungsholz bis zur Lagergrenze, Rest verrottet
                    if (!_s.World.SetBuilding(xx, yy, id))
                    {
                        // Rückbau der bereits belegten Kacheln
                        for (int ry = y; ry < y + d.SizeY; ry++)
                            for (int rx = x; rx < x + d.SizeX; rx++)
                                if (_s.World.Get(rx, ry).Building == id) _s.World.ClearBuilding(rx, ry);
                        _s.Buildings.Destroy(id);
                        id = EntityId.None;
                        reason = $"Kachel ({xx}, {yy}) ist nicht bebaubar.";
                        return false;
                    }
                }
            }
            reason = null;
            return true;
        }

        internal bool DestroyBuilding(EntityId id)
        {
            var r = _s.Buildings.GetRecord(id);
            if (r == null) return false;
            for (int yy = r.Y; yy < r.Y + r.SizeY; yy++)
                for (int xx = r.X; xx < r.X + r.SizeX; xx++)
                    if (_s.World.InBounds(xx, yy) && _s.World.Get(xx, yy).Building == id) _s.World.ClearBuilding(xx, yy);
            var st = _s.Settlement(r.SettlementId);
            st?.Buildings.Remove(id);
            int x = r.X, y = r.Y, sid = r.SettlementId;
            _s.Buildings.Destroy(id);
            if (st != null) RecomputeCapacities(st);
            _s.Ctx.Events.Publish(new BuildingDestroyedEvent { Building = id, SettlementId = sid, X = x, Y = y });
            return true;
        }

        internal int CountOfType(Settlement st, BuildingTypeId type)
        {
            int n = 0;
            foreach (var b in st.Buildings)
            {
                var r = _s.Buildings.GetRecord(b);
                if (r != null && r.Type.Equals(type)) n++;
            }
            return n;
        }

        /// <summary>Wohnraum, Lager und Verteidigung aus fertigen Gebäuden.</summary>
        internal void RecomputeCapacities(Settlement st)
        {
            int housing = 0, storage = 0;
            float defense = 0f;
            foreach (var b in st.Buildings)
            {
                var r = _s.Buildings.GetRecord(b);
                if (r == null || !r.Completed) continue;
                var d = _s.Content.Buildings.Get(r.Type);
                housing += d.Housing;
                storage += d.Storage;
                defense += d.Defense;
            }
            st.HousingCapacity = housing;
            st.StorageCapacity = storage;
            st.Defense = defense;
        }

        // ---------------- Besitzwechsel und Aufgabe ----------------

        public void TransferSettlement(int settlementId, int newCivId)
        {
            var st = _s.Settlement(settlementId);
            var newCiv = _s.Civ(newCivId);
            if (st == null || newCiv == null || !newCiv.Alive || st.CivId == newCivId) return;
            int oldCivId = st.CivId;
            _s.MoveSettlement(st, newCivId);
            st.RecruitQueue.Clear();
            st.ExpansionTargetX = st.ExpansionTargetY = -1;
            if (newCiv.CapitalSettlementId == 0) newCiv.CapitalSettlementId = st.Id;
            Territory?.MarkDirty(st.Id);
            _s.Ctx.Events.Publish(new SettlementCapturedEvent { SettlementId = st.Id, OldCivId = oldCivId, NewCivId = newCivId });
            Civs.OnSettlementLost(oldCivId, st.Id);
        }

        /// <summary>Siedlung ohne Einwohner: Gebäude werden abgerissen, Gebiet frei.</summary>
        internal void Abandon(Settlement st)
        {
            if (!_s.Settlements.ContainsKey(st.Id)) return;
            var ids = st.Buildings.ToArray();
            foreach (var b in ids) DestroyBuilding(b);
            Territory?.MarkReleased(st);
            _s.RemoveSettlement(st);
            _s.Ctx.Events.Publish(new SettlementAbandonedEvent { SettlementId = st.Id, CivId = st.CivId, X = st.X, Y = st.Y });
            Civs.OnSettlementLost(st.CivId, st.Id);
        }
    }

    /// <summary>Deterministische Namen aus Silben, abhängig vom cultureSeed der Zivilisation.</summary>
    public static class NameGenerator
    {
        private static readonly string[] Start = { "Al", "Bel", "Cor", "Dar", "El", "Fen", "Gal", "Hal", "Ist", "Kar", "Lor", "Mar", "Nor", "Os", "Pel", "Ros", "Sal", "Tor", "Ul", "Var", "Wen", "Yr", "Zan", "Bram" };
        private static readonly string[] Mid = { "", "a", "e", "i", "o", "en", "ar", "ul", "is", "an" };
        private static readonly string[] End = { "heim", "burg", "dorf", "stadt", "ia", "on", "eth", "mar", "wald", "furt", "holm", "rath", "ven", "gard" };

        public static string SettlementName(ulong cultureSeed, int counter)
        {
            var r = new SimRandom(cultureSeed * 31UL + (ulong)counter * 0x9E3779B97F4A7C15UL + 17UL);
            return Start[r.Range(0, Start.Length)] + Mid[r.Range(0, Mid.Length)] + End[r.Range(0, End.Length)];
        }

        public static string CivName(ulong cultureSeed)
        {
            var r = new SimRandom(cultureSeed + 0xABCDEFUL);
            return Start[r.Range(0, Start.Length)] + Mid[r.Range(0, Mid.Length)] + "er";
        }
    }
}
