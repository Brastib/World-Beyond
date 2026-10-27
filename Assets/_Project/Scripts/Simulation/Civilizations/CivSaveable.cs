using System;
using System.Collections.Generic;
using WorldBeyond.Core;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Simulation.Civilizations
{
    /// <summary>
    /// Speichert den gesamten Zustand der Civ-Schicht als Sektion "civs" (LoadOrder 20: nach world und units).
    /// Inhaltstypen (Gebäude, Technologien) und Enums als String-Schlüssel, Querverweise nur über IDs (ARCHITECTURE §11).
    /// Einspielen erst in Prüfobjekte; der echte Zustand ändert sich nur bei vollständigem Erfolg.
    /// </summary>
    public sealed class CivSaveable : ISaveable, ISaveReferenceCheck
    {
        public const string Key = "civs";

        private readonly CivState _s;
        private readonly CivilizationRegistry _civs;
        private readonly TerritoryService _territory;
        private readonly TransportSystem _transport;
        private readonly SettlementRegistry _settlements;

        public CivSaveable(CivState s, CivilizationRegistry civs, SettlementRegistry settlements, TerritoryService territory, TransportSystem transport)
        {
            _s = s; _civs = civs; _settlements = settlements; _territory = territory; _transport = transport;
        }

        public string SaveKey => Key;
        public int LoadOrder => 20;
        public int SectionVersion => 1;
        public Type StateType => typeof(CivSaveState);

        public object CaptureState()
        {
            var st = new CivSaveState
            {
                LastCivId = _s.CivIds.LastIssued,
                LastSettlementId = _s.SettlementIds.LastIssued,
                Buildings = _s.Buildings.Capture(),
                Caravans = _transport.Capture(),
                BlockedRoutes = _transport.CaptureBlocked(),
                Territory = _territory.CaptureDirty(),
            };
            var civs = new List<CivDto>();
            foreach (int id in _s.CivOrder) civs.Add(ToDto(_s.Civs[id]));
            st.Civilizations = civs.ToArray();
            var sets = new List<SettlementDto>();
            foreach (int id in _s.SettlementOrder) sets.Add(ToDto(_s.Settlements[id]));
            st.Settlements = sets.ToArray();
            return st;
        }

        public bool TryRestoreState(object dto, out string error)
        {
            if (!(dto is CivSaveState s) || s.Civilizations == null || s.Settlements == null || s.Buildings == null)
            {
                error = "civs: Zustand fehlt oder hat falschen Typ.";
                return false;
            }
            if (s.LastCivId < 0 || s.LastSettlementId < 0) { error = "civs: negativer ID-Zähler."; return false; }

            // 1. Alles in neue Objekte übersetzen und prüfen.
            var civs = new List<Civilization>();
            var seenCiv = new HashSet<int>();
            foreach (var c in s.Civilizations)
            {
                if (c == null || c.Id <= 0 || c.Id > s.LastCivId || !seenCiv.Add(c.Id)) { error = $"civs: ungültige oder doppelte Zivilisations-ID {c?.Id}."; return false; }
                if (!FromDto(c, out var civ, out error)) return false;
                civs.Add(civ);
            }
            var sets = new List<Settlement>();
            var seenSet = new HashSet<int>();
            foreach (var d in s.Settlements)
            {
                if (d == null || d.Id <= 0 || d.Id > s.LastSettlementId || !seenSet.Add(d.Id)) { error = $"civs: ungültige oder doppelte Siedlungs-ID {d?.Id}."; return false; }
                if (!seenCiv.Contains(d.CivId)) { error = $"civs: Siedlung {d.Id} gehört unbekannter Zivilisation {d.CivId}."; return false; }
                if (!FromDto(d, out var st, out error)) return false;
                sets.Add(st);
            }

            // 2. Gebäude (eigener EntityStore) und Karawanen einspielen; bei Fehler alten Zustand behalten.
            var oldBuildings = _s.Buildings.Capture();
            if (!_s.Buildings.TryRestore(s.Buildings, out error)) return false;
            if (!_transport.TryRestore(s.Caravans, s.BlockedRoutes, out error)) { _s.Buildings.TryRestore(oldBuildings, out _); return false; }
            foreach (var st in sets)
                foreach (var b in st.Buildings)
                {
                    var r = _s.Buildings.GetRecord(b);
                    if (r == null || r.SettlementId != st.Id)
                    {
                        error = $"civs: Siedlung {st.Id} verweist auf Gebäude {b}, das nicht zu ihr gehört.";
                        _s.Buildings.TryRestore(oldBuildings, out _);
                        return false;
                    }
                }

            // 3. Übernehmen.
            _s.CivIds.TryRestore(s.LastCivId, out _);
            _s.SettlementIds.TryRestore(s.LastSettlementId, out _);
            _s.ClearAll();
            foreach (var c in civs) { _s.Civs.Add(c.Id, c); _s.CivOrder.Add(c.Id); }
            _s.CivOrder.Sort();
            foreach (var st in sets) { _s.AddSettlement(st); _settlements.RecomputeCapacities(st); }
            _civs.RebuildAliveList();
            _territory.RestoreDirty(s.Territory, _s.World.Width, _s.World.Height);
            error = null;
            return true;
        }

        /// <summary>Nach dem Laden aller Sektionen: verwaiste Gebäude und Karawanen bereinigen (Rückfall, Warnung).</summary>
        public void CheckReferences(SimContext ctx, LoadReport report)
        {
            var ids = new List<EntityId>();
            _s.Buildings.CopyAliveIds(ids);
            foreach (var b in ids)
            {
                var r = _s.Buildings.GetRecord(b);
                var st = _s.Settlement(r.SettlementId);
                if (st == null || !st.Buildings.Contains(b))
                {
                    report.Warn($"civs: Gebäude {b} ohne gültige Siedlung {r.SettlementId}; abgerissen.");
                    for (int y = r.Y; y < r.Y + r.SizeY; y++)
                        for (int x = r.X; x < r.X + r.SizeX; x++)
                            if (_s.World.InBounds(x, y) && _s.World.Get(x, y).Building == b) _s.World.ClearBuilding(x, y);
                    _s.Buildings.Destroy(b);
                }
            }
            foreach (int cid in _s.CivOrder)
            {
                var c = _s.Civs[cid];
                if (c.Alive && _s.Settlement(c.CapitalSettlementId) == null)
                {
                    var list = _s.SettlementListOf(cid);
                    c.CapitalSettlementId = list.Count > 0 ? _civs.LargestSettlement(list) : 0;
                    report.Warn($"civs: Hauptstadt von {c.Name} ungültig; neu gewählt ({c.CapitalSettlementId}).");
                }
            }
            int dropped = _transport.DropOrphans();
            if (dropped > 0) report.Warn($"civs: {dropped} Karawane(n) ohne Einheit verworfen.");
        }

        // ---------------- DTO-Übersetzung ----------------

        private CivDto ToDto(Civilization c)
        {
            var keys = new List<string>();
            for (int i = 1; i < c.Researched.Length; i++) if (c.Researched[i]) keys.Add(_s.Content.Techs.KeyOf(new TechId(i)));
            return new CivDto
            {
                Id = c.Id, Name = c.Name, Color = c.Color, CultureSeed = c.CultureSeed, Traits = c.Traits, Alive = c.Alive,
                Capital = c.CapitalSettlementId, CreatedTick = c.CreatedTick, NameCounter = c.NameCounter,
                Researched = keys.ToArray(), CurrentResearch = _s.Content.Techs.KeyOf(c.CurrentResearch), ResearchPoints = c.ResearchPoints,
                Epoch = c.Epoch.ToString(), NextAiTick = c.NextAiTick,
            };
        }

        private bool FromDto(CivDto d, out Civilization c, out string error)
        {
            c = null;
            if (!Enum.TryParse(d.Epoch, out Epoch epoch)) { error = $"civs: unbekannte Epoche '{d.Epoch}'."; return false; }
            c = new Civilization
            {
                Id = d.Id, Name = d.Name ?? "", Color = d.Color, CultureSeed = d.CultureSeed, Traits = d.Traits, Alive = d.Alive,
                CapitalSettlementId = d.Capital, CreatedTick = d.CreatedTick, NameCounter = d.NameCounter,
                Researched = new bool[_s.TechSlots], ResearchPoints = d.ResearchPoints, Epoch = epoch, NextAiTick = d.NextAiTick,
            };
            foreach (var k in d.Researched ?? Array.Empty<string>())
            {
                if (!_s.Content.Techs.TryGetByKey(k, out var t)) { error = $"civs: unbekannte Technologie '{k}'."; return false; }
                c.Researched[t.Value] = true;
            }
            if (!string.IsNullOrEmpty(d.CurrentResearch))
            {
                if (!_s.Content.Techs.TryGetByKey(d.CurrentResearch, out var cur)) { error = $"civs: unbekannte Technologie '{d.CurrentResearch}'."; return false; }
                c.CurrentResearch = cur;
            }
            error = null;
            return true;
        }

        private static SettlementDto ToDto(Settlement st)
        {
            var stock = new List<ResourceAmountDto>();
            foreach (var r in Resources.Stockable) if (st.Stock[(int)r] != 0) stock.Add(new ResourceAmountDto { Resource = r.ToString(), Amount = st.Stock[(int)r] });
            var mods = new List<ModifierDto>();
            foreach (var m in st.Modifiers) mods.Add(new ModifierDto { Kind = m.Kind.ToString(), UntilTick = m.UntilTick });
            var queue = new List<RecruitOrderDto>();
            foreach (var o in st.RecruitQueue)
                queue.Add(new RecruitOrderDto
                {
                    UnitTypeKey = o.UnitTypeKey, Remaining = o.Remaining, ProgressYears = o.ProgressYears, YearsPerUnit = o.YearsPerUnit,
                    TargetX = o.TargetX, TargetY = o.TargetY, CargoTarget = o.CargoTargetSettlementId, CargoResource = o.CargoResource.ToString(), CargoAmount = o.CargoAmount,
                });
            var buildings = new int[st.Buildings.Count * 2];
            for (int i = 0; i < st.Buildings.Count; i++) { buildings[2 * i] = st.Buildings[i].Index; buildings[2 * i + 1] = st.Buildings[i].Generation; }
            return new SettlementDto
            {
                Id = st.Id, CivId = st.CivId, Name = st.Name, X = st.X, Y = st.Y, Tier = st.Tier.ToString(), FoundedTick = st.FoundedTick,
                Children = st.Children, Adults = st.Adults, Elders = st.Elders, Stock = stock.ToArray(),
                Buildings = buildings, Workers = (int[])st.Workers.Clone(), Unemployed = st.Unemployed,
                FoodBalancePerYear = st.FoodBalancePerYear, BirthsPerYear = st.BirthsPerYear, DeathsPerYear = st.DeathsPerYear,
                LastUpdateTick = st.LastUpdateTick, NextUpdateTick = st.NextUpdateTick, ActiveLod = st.ActiveLod,
                Modifiers = mods.ToArray(), RecruitQueue = queue.ToArray(),
                ExpansionTargetX = st.ExpansionTargetX, ExpansionTargetY = st.ExpansionTargetY, PlayerChoseTarget = st.PlayerChoseTarget,
            };
        }

        private bool FromDto(SettlementDto d, out Settlement st, out string error)
        {
            st = null;
            if (!Enum.TryParse(d.Tier, out SettlementTier tier)) { error = $"civs: unbekannte Stufe '{d.Tier}'."; return false; }
            if (!_s.World.InBounds(d.X, d.Y)) { error = $"civs: Siedlung {d.Id} liegt außerhalb der Welt."; return false; }
            if (d.Children < 0 || d.Adults < 0 || d.Elders < 0 || double.IsNaN(d.Adults)) { error = $"civs: Siedlung {d.Id} hat ungültige Bevölkerung."; return false; }
            st = new Settlement
            {
                Id = d.Id, CivId = d.CivId, Name = d.Name ?? "", X = d.X, Y = d.Y, Tier = tier, FoundedTick = d.FoundedTick,
                Children = d.Children, Adults = d.Adults, Elders = d.Elders, Unemployed = d.Unemployed,
                FoodBalancePerYear = d.FoodBalancePerYear, BirthsPerYear = d.BirthsPerYear, DeathsPerYear = d.DeathsPerYear,
                LastUpdateTick = d.LastUpdateTick, NextUpdateTick = d.NextUpdateTick, ActiveLod = d.ActiveLod,
                ExpansionTargetX = d.ExpansionTargetX, ExpansionTargetY = d.ExpansionTargetY, PlayerChoseTarget = d.PlayerChoseTarget,
            };
            foreach (var a in d.Stock ?? Array.Empty<ResourceAmountDto>())
            {
                if (a == null || !Enum.TryParse(a.Resource, out ResourceType r) || !Resources.IsStockable(r) || a.Amount < 0 || double.IsNaN(a.Amount))
                { error = $"civs: Siedlung {d.Id} hat ungültigen Lagereintrag."; return false; }
                st.Stock[(int)r] = a.Amount;
            }
            if (d.Workers != null) Array.Copy(d.Workers, st.Workers, Math.Min(d.Workers.Length, st.Workers.Length));
            if (d.Buildings == null || d.Buildings.Length % 2 != 0) { error = $"civs: Siedlung {d.Id} hat ungültige Gebäudeliste."; return false; }
            for (int i = 0; i < d.Buildings.Length; i += 2) st.Buildings.Add(new EntityId(d.Buildings[i], d.Buildings[i + 1]));
            foreach (var m in d.Modifiers ?? Array.Empty<ModifierDto>())
            {
                if (m == null || !Enum.TryParse(m.Kind, out CivEventKind k)) { error = $"civs: unbekanntes Ereignis '{m?.Kind}'."; return false; }
                st.Modifiers.Add(new SettlementModifier { Kind = k, UntilTick = m.UntilTick });
            }
            foreach (var o in d.RecruitQueue ?? Array.Empty<RecruitOrderDto>())
            {
                if (o == null || !_s.Ctx.UnitTypes.TryGetByKey(o.UnitTypeKey, out _)) { error = $"civs: unbekannter Einheitentyp '{o?.UnitTypeKey}' in Produktion."; return false; }
                Enum.TryParse(o.CargoResource, out ResourceType cargo);
                st.RecruitQueue.Add(new RecruitOrder
                {
                    UnitTypeKey = o.UnitTypeKey, Remaining = o.Remaining, ProgressYears = o.ProgressYears, YearsPerUnit = o.YearsPerUnit,
                    TargetX = o.TargetX, TargetY = o.TargetY, CargoTargetSettlementId = o.CargoTarget, CargoResource = cargo, CargoAmount = o.CargoAmount,
                });
            }
            // Wohnraum, Lager und Verteidigung werden nach dem Einspielen der Gebäude neu berechnet (TryRestoreState Schritt 3).
            error = null;
            return true;
        }
    }

    [Serializable]
    public sealed class CivSaveState
    {
        public int LastCivId;
        public int LastSettlementId;
        public CivDto[] Civilizations;
        public SettlementDto[] Settlements;
        public BuildingStoreState Buildings;
        public CaravanDto[] Caravans;
        public RouteBlockDto[] BlockedRoutes;   // fehlt in älteren Spielständen: dann keine Sperren
        public TerritoryDirtyDto Territory;
    }

    [Serializable]
    public sealed class CivDto
    {
        public int Id;
        public string Name;
        public CivColor Color;
        public ulong CultureSeed;
        public CivTraits Traits;
        public bool Alive;
        public int Capital;
        public long CreatedTick;
        public int NameCounter;
        public string[] Researched;
        public string CurrentResearch;
        public double ResearchPoints;
        public string Epoch;
        public long NextAiTick;
    }

    [Serializable]
    public sealed class SettlementDto
    {
        public int Id, CivId;
        public string Name;
        public int X, Y;
        public string Tier;
        public long FoundedTick;
        public double Children, Adults, Elders;
        public ResourceAmountDto[] Stock;
        public int[] Buildings;            // Paare (Index, Generation)
        public int[] Workers;              // Index = JobKind
        public int Unemployed;
        public float FoodBalancePerYear, BirthsPerYear, DeathsPerYear;
        public long LastUpdateTick, NextUpdateTick;
        public bool ActiveLod;
        public ModifierDto[] Modifiers;
        public RecruitOrderDto[] RecruitQueue;
        public int ExpansionTargetX, ExpansionTargetY;
        public bool PlayerChoseTarget;
    }

    [Serializable] public sealed class ResourceAmountDto { public string Resource; public double Amount; }
    [Serializable] public sealed class ModifierDto { public string Kind; public long UntilTick; }

    [Serializable]
    public sealed class RecruitOrderDto
    {
        public string UnitTypeKey;
        public int Remaining;
        public double ProgressYears;
        public float YearsPerUnit;
        public int TargetX, TargetY;
        public int CargoTarget;
        public string CargoResource;
        public int CargoAmount;
    }

    [Serializable]
    public sealed class TerritoryDirtyDto
    {
        public bool Dirty;
        public int MinX, MinY, MaxX, MaxY;
    }
}
