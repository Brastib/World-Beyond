using System.Collections.Generic;
using System.Text;
using WorldBeyond.Simulation.Civilizations;
using WorldBeyond.Simulation.Diplomacy;

namespace WorldBeyond.Presentation.UI
{
    public sealed class CivOverviewRow
    {
        public int CivId;
        public string Name;
        public CivColor Color;
        public int Population;
        public int Settlements;
        public int MilitaryStrength;
        public string Epoch;
        public bool AtWar;
        public string WarStatus;   // "Frieden" oder "Krieg mit …"; ruhender Krieg: "Waffenstillstand mit …"
    }

    /// <summary>
    /// Zivilisationsübersicht, nur lesend über ICivilizationQuery und IWarQuery. Fehlt ein Dienst, bleibt die Liste leer.
    /// Refresh füllt eine wiederverwendete Liste, damit die UI ohne Allokation je Bild aktualisieren kann.
    /// </summary>
    public sealed class CivOverviewModel
    {
        private readonly List<CivOverviewRow> _rows = new List<CivOverviewRow>();
        private readonly StringBuilder _sb = new StringBuilder();
        private int _count;

        public IReadOnlyList<CivOverviewRow> Rows => _rows.GetRange(0, _count);
        public int Count => _count;
        public CivOverviewRow this[int i] => _rows[i];

        public void Refresh(ICivilizationQuery civs, IWarQuery wars)
        {
            _count = 0;
            if (civs == null) return;
            var ids = civs.AllCivIds;
            for (int i = 0; i < ids.Count; i++)
            {
                if (!civs.TryGet(ids[i], out var v) || !v.Alive) continue;
                if (_count == _rows.Count) _rows.Add(new CivOverviewRow());
                var row = _rows[_count++];
                row.CivId = v.Id;
                row.Name = string.IsNullOrEmpty(v.Name) ? $"Zivilisation {v.Id}" : v.Name;
                row.Color = v.Color;
                row.Population = v.Population;
                row.Settlements = v.SettlementCount;
                row.MilitaryStrength = v.MilitaryStrength;
                row.Epoch = UiText.Epoch(v.Epoch);
                row.WarStatus = WarStatus(v.Id, civs, wars, out row.AtWar);
            }
        }

        private string WarStatus(int civId, ICivilizationQuery civs, IWarQuery wars, out bool atWar)
        {
            atWar = false;
            if (wars == null) return "Frieden";
            _sb.Clear();
            bool suspendedOnly = true;
            var active = wars.ActiveWars;
            for (int w = 0; w < active.Count; w++)
            {
                var war = active[w];
                if (war == null || !war.Active) continue;
                var side = war.SideOf(civId);
                if (side == WarSide.None) continue;
                var enemies = war.MembersOf(side == WarSide.Attacker ? WarSide.Defender : WarSide.Attacker);
                for (int e = 0; e < enemies.Count; e++)
                {
                    if (_sb.Length > 0) _sb.Append(", ");
                    _sb.Append(civs.TryGet(enemies[e], out var ev) && !string.IsNullOrEmpty(ev.Name) ? ev.Name : $"Zivilisation {enemies[e]}");
                }
                if (!war.Suspended) suspendedOnly = false;
            }
            if (_sb.Length == 0) return "Frieden";
            atWar = !suspendedOnly;
            return (suspendedOnly ? "Waffenstillstand mit " : "Krieg mit ") + _sb;
        }
    }
}
