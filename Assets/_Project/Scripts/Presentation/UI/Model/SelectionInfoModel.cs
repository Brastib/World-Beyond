using System.Collections.Generic;
using WorldBeyond.Core;
using WorldBeyond.Simulation;
using WorldBeyond.Simulation.Civilizations;
using WorldBeyond.Simulation.Units;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Presentation.UI
{
    public struct InfoLine
    {
        public string Label;
        public string Value;
        public InfoLine(string label, string value) { Label = label; Value = value; }
    }

    /// <summary>
    /// Infopanel zur Auswahl (Einheit oder Kachel/Siedlung). Hält nur IDs, keine Spielobjekte (ARCHITECTURE §11):
    /// Jede Aktualisierung prüft die ID erneut; ist sie ungültig geworden, meldet <see cref="Refresh"/> false.
    /// </summary>
    public sealed class SelectionInfoModel
    {
        private readonly List<InfoLine> _lines = new List<InfoLine>();

        public string Title { get; private set; }
        public string Subtitle { get; private set; }
        public IReadOnlyList<InfoLine> Lines => _lines;
        public bool HasContent { get; private set; }
        /// <summary>Siedlung der Auswahl (0 = keine). Die UI meldet sie an ICivSimulationLod.SetSelectedSettlement.</summary>
        public int SettlementId { get; private set; }

        public void Clear()
        {
            HasContent = false;
            Title = Subtitle = null;
            SettlementId = 0;
            _lines.Clear();
        }

        public bool RefreshUnit(SimContext ctx, EntityId unit)
        {
            Clear();
            if (ctx == null || !ctx.Units.TryGet(unit, out var v) || v.State == UnitState.Dead) return false;
            ctx.UnitTypes.TryGet(v.Type, out var type);
            ctx.TryGet(out ICivilizationQuery civs);
            Title = type != null && !string.IsNullOrEmpty(type.DisplayName) ? type.DisplayName : "Einheit";
            Subtitle = type != null ? UiText.UnitRole(type.Role) : null;
            _lines.Add(new InfoLine("Zivilisation", CivName(civs, v.CivId)));
            _lines.Add(new InfoLine("Zustand", UiText.UnitState(v.State)));
            _lines.Add(new InfoLine("Lebenspunkte", $"{Round(v.Hp)} / {Round(v.MaxHp)}"));
            _lines.Add(new InfoLine("Tempo", $"{v.Speed * 3.6f:0} km/h"));
            HasContent = true;
            return true;
        }

        /// <summary>Kachel: steht dort eine Siedlung, zeigt das Panel sie, sonst Gelände und Besitzer.</summary>
        public bool RefreshTile(SimContext ctx, int x, int y)
        {
            Clear();
            if (ctx == null) return false;
            ctx.TryGet(out ICivilizationQuery civs);
            if (ctx.TryGet(out ISettlementQuery settlements))
            {
                int sid = settlements.FindAt(x, y);
                if (sid > 0 && settlements.TryGet(sid, out var s)) return FillSettlement(s, civs);
            }
            if (!ctx.TryGet(out IWorldQuery world) || !world.InBounds(x, y)) return false;
            var tile = world.Get(x, y);
            Title = "Gelände";
            Subtitle = $"Kachel {x}, {y}";
            int owner = ctx.TryGet(out ITerritoryService territory) ? territory.OwnerAt(x, y) : 0;
            _lines.Add(new InfoLine("Gebiet von", owner > 0 ? CivName(civs, owner) : "niemand"));
            _lines.Add(new InfoLine("Höhe", $"{tile.Height:0} m"));
            HasContent = true;
            return true;
        }

        private bool FillSettlement(in SettlementView s, ICivilizationQuery civs)
        {
            SettlementId = s.Id;
            Title = string.IsNullOrEmpty(s.Name) ? "Siedlung" : s.Name;
            Subtitle = UiText.Tier(s.Tier) + (s.IsCapital ? " · Hauptstadt" : "");
            _lines.Add(new InfoLine("Zivilisation", CivName(civs, s.CivId)));
            _lines.Add(new InfoLine("Einwohner", $"{s.Population} (Kinder {s.Children}, Erwachsene {s.Adults}, Alte {s.Elders})"));
            _lines.Add(new InfoLine("Wohnraum", $"{s.Population} / {s.HousingCapacity}"));
            _lines.Add(new InfoLine("Arbeit", $"{s.Workers} beschäftigt, {s.Unemployed} ohne Arbeit"));
            _lines.Add(new InfoLine("Nahrung", $"{Signed(s.FoodBalancePerYear)} pro Jahr"));
            _lines.Add(new InfoLine("Gebäude", s.ConstructionSites > 0 ? $"{s.BuildingCount} (+{s.ConstructionSites} im Bau)" : $"{s.BuildingCount}"));
            _lines.Add(new InfoLine("Lager", $"Nahrung {s.Stock.Food} · Holz {s.Stock.Wood} · Stein {s.Stock.Stone} · Erz {s.Stock.Ore}"));
            HasContent = true;
            return true;
        }

        private static string CivName(ICivilizationQuery civs, int civId)
        {
            if (civId <= 0) return "keine";
            return civs != null && civs.TryGet(civId, out var v) && !string.IsNullOrEmpty(v.Name) ? v.Name : $"Zivilisation {civId}";
        }

        private static int Round(float f) => (int)(f + 0.5f);
        private static string Signed(float f) => f >= 0 ? $"+{f:0}" : $"{f:0}";
    }
}
