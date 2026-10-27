using System.Collections.Generic;
using WorldBeyond.Application.Save;

namespace WorldBeyond.Presentation.UI
{
    public sealed class SaveSlotRow
    {
        public string Slot;
        public string Title;       // "Spielstand 1"
        public string Detail;      // "Jahr 12, Tag 40 · gespeichert 2026-10-26 21:30" oder "leer" / Fehlertext
        public bool Exists;
        public bool CanLoad;
    }

    /// <summary>
    /// Speichern/Laden über den einzigen ISaveLoadService (Besitzer: Save, Phase 7). Feste Plätze statt freier Namen,
    /// damit kein Texteingabefeld nötig ist. Rückmeldungen sind immer SaveResult/LoadResult.UserMessage (ARCHITECTURE §11.7).
    /// </summary>
    public sealed class SaveMenuModel
    {
        public const int SlotCount = 5;
        private readonly List<SaveSlotRow> _rows = new List<SaveSlotRow>();

        public SaveMenuModel()
        {
            for (int i = 1; i <= SlotCount; i++) _rows.Add(new SaveSlotRow { Slot = SlotName(i), Title = $"Spielstand {i}" });
        }

        public IReadOnlyList<SaveSlotRow> Rows => _rows;
        public string LastMessage { get; private set; }
        public bool LastWasError { get; private set; }

        public static string SlotName(int index) => $"slot{index}";

        public void Refresh(ISaveLoadService service)
        {
            foreach (var r in _rows) { r.Exists = false; r.CanLoad = false; r.Detail = "leer"; }
            if (service == null) return;
            IReadOnlyList<SaveSlotInfo> infos;
            try { infos = service.ListSlots(); }
            catch (System.Exception ex)
            {
                SetMessage("Die Spielstände konnten nicht gelesen werden.", true, ex.Message);
                return;
            }
            foreach (var info in infos)
            {
                var row = _rows.Find(r => r.Slot == info.Slot);
                if (row == null) continue;
                row.Exists = true;
                row.CanLoad = info.Readable;
                row.Detail = info.Readable
                    ? $"{GameDate.Format(info.SimTick)} · gespeichert {ShortDate(info.CreatedUtc)}"
                    : SaveMessages.ForLoad(info.Error);
            }
        }

        public bool Save(ISaveLoadService service, string slot)
        {
            if (service == null) { SetMessage("Speichern ist gerade nicht verfügbar.", true, null); return false; }
            var r = service.Save(slot);
            SetMessage(r.UserMessage, !r.Success, r.Detail);
            Refresh(service);
            return r.Success;
        }

        public bool Load(ISaveLoadService service, string slot)
        {
            if (service == null) { SetMessage("Laden ist gerade nicht verfügbar.", true, null); return false; }
            var r = service.Load(slot);
            SetMessage(r.UserMessage, !r.Success, r.Detail);
            Refresh(service);
            return r.Success;
        }

        /// <summary>Technisches Detail der letzten Meldung, für das Log (nicht für den Spieler).</summary>
        public string LastDetail { get; private set; }

        private void SetMessage(string message, bool error, string detail)
        {
            LastMessage = message;
            LastWasError = error;
            LastDetail = detail;
        }

        // "2026-10-26T21:30:12Z" → "2026-10-26 21:30" (UTC, wie gespeichert).
        private static string ShortDate(string utc)
        {
            if (string.IsNullOrEmpty(utc)) return "unbekannt";
            string s = utc.Replace('T', ' ');
            return s.Length >= 16 ? s.Substring(0, 16) + " UTC" : s;
        }
    }
}
