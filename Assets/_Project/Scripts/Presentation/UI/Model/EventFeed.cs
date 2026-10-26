using System;
using System.Collections.Generic;
using WorldBeyond.Core;
using WorldBeyond.Simulation;
using WorldBeyond.Simulation.Civilizations;
using WorldBeyond.Simulation.Diplomacy;

namespace WorldBeyond.Presentation.UI
{
    // Explizite Werte, nur anhängen.
    public enum FeedKind : byte { Info = 0, War = 1, Peace = 2, Conquest = 3, Collapse = 4, Founding = 5, Progress = 6 }

    public struct FeedEntry
    {
        public long Tick;
        public FeedKind Kind;
        public string Text;
        public bool HasLocation;   // Klick → Kamera springt zur Kachel
        public int TileX, TileY;
    }

    /// <summary>
    /// Ereignis-Feed (Besitzer: UI, INTEGRATION §3 IGameEventFeed). Hört auf die Events der laufenden Komposition und
    /// formuliert sie auf Deutsch. Kein Spielzustand: nur Anzeigetexte; nach dem Laden beginnt der Feed leer.
    /// Namen werden beim Eintreffen aufgelöst und zwischengespeichert, weil eine zerfallene Zivilisation danach nicht mehr abfragbar ist.
    /// </summary>
    public sealed class EventFeed
    {
        public const int DefaultCapacity = 60;

        private readonly int _capacity;
        private readonly List<FeedEntry> _entries = new List<FeedEntry>();   // älteste zuerst
        private readonly Dictionary<int, string> _civNames = new Dictionary<int, string>();
        private SimContext _ctx;
        private ICivilizationQuery _civs;
        private ISettlementQuery _settlements;

        public EventFeed(int capacity = DefaultCapacity) => _capacity = Math.Max(1, capacity);

        public IReadOnlyList<FeedEntry> Entries => _entries;
        /// <summary>Zählt bei jedem neuen Eintrag hoch; die UI baut nur bei Änderung neu auf.</summary>
        public int Version { get; private set; }

        public void Attach(SimContext ctx)
        {
            Detach();
            _entries.Clear();
            _civNames.Clear();
            Version++;
            if (ctx == null) return;
            _ctx = ctx;
            ctx.TryGet(out _civs);
            ctx.TryGet(out _settlements);
            RememberAllCivNames();
            var ev = ctx.Events;
            ev.Subscribe<CivilizationCreatedEvent>(OnCivCreated);
            ev.Subscribe<WarDeclaredEvent>(OnWarDeclared);
            ev.Subscribe<WarJoinedEvent>(OnWarJoined);
            ev.Subscribe<PeaceSignedEvent>(OnPeace);
            ev.Subscribe<TruceSignedEvent>(OnTruce);
            ev.Subscribe<AllianceFormedEvent>(OnAlliance);
            ev.Subscribe<SettlementCapturedEvent>(OnCaptured);
            ev.Subscribe<SettlementFoundedEvent>(OnFounded);
            ev.Subscribe<CivilizationCollapsedEvent>(OnCollapsed);
            ev.Subscribe<EpochChangedEvent>(OnEpoch);
        }

        public void Detach()
        {
            if (_ctx == null) return;
            var ev = _ctx.Events;
            ev.Unsubscribe<CivilizationCreatedEvent>(OnCivCreated);
            ev.Unsubscribe<WarDeclaredEvent>(OnWarDeclared);
            ev.Unsubscribe<WarJoinedEvent>(OnWarJoined);
            ev.Unsubscribe<PeaceSignedEvent>(OnPeace);
            ev.Unsubscribe<TruceSignedEvent>(OnTruce);
            ev.Unsubscribe<AllianceFormedEvent>(OnAlliance);
            ev.Unsubscribe<SettlementCapturedEvent>(OnCaptured);
            ev.Unsubscribe<SettlementFoundedEvent>(OnFounded);
            ev.Unsubscribe<CivilizationCollapsedEvent>(OnCollapsed);
            ev.Unsubscribe<EpochChangedEvent>(OnEpoch);
            _ctx = null;
            _civs = null;
            _settlements = null;
        }

        // ---------- Handler (laufen beim Event-Flush am Tickende) ----------

        private void OnCivCreated(CivilizationCreatedEvent e)
        {
            string name = CivName(e.CivId);
            AddAtSettlement(FeedKind.Founding, $"{name} wurde gegründet.", e.CapitalSettlementId);
        }

        private void OnWarDeclared(WarDeclaredEvent e)
        {
            string reason = UiText.CasusBelli(e.CasusBelli);
            string text = $"{CivName(e.Attacker)} erklärt {CivName(e.Defender)} den Krieg" + (reason != null ? $" ({reason})." : ".");
            AddAtCapital(FeedKind.War, text, e.Defender);
        }

        private void OnWarJoined(WarJoinedEvent e)
        {
            string side = e.Side == WarSide.Attacker ? "auf Seite der Angreifer" : "auf Seite der Verteidiger";
            AddAtCapital(FeedKind.War, $"{CivName(e.CivId)} tritt dem Krieg bei ({side}).", e.CivId);
        }

        private void OnPeace(PeaceSignedEvent e)
        {
            string term = UiText.PeaceTerm(e.Term);
            string text = $"Frieden zwischen {CivName(e.CivA)} und {CivName(e.CivB)}" + (term != null ? $" {term}." : ".");
            if (e.Term == PeaceTermType.CedeSettlement && e.SettlementId > 0) AddAtSettlement(FeedKind.Peace, text, e.SettlementId);
            else AddAtCapital(FeedKind.Peace, text, e.CivA);
        }

        private void OnTruce(TruceSignedEvent e) =>
            AddAtCapital(FeedKind.Peace, $"Waffenstillstand zwischen {CivName(e.CivA)} und {CivName(e.CivB)}.", e.CivA);

        private void OnAlliance(AllianceFormedEvent e) =>
            AddAtCapital(FeedKind.Info, $"{CivName(e.CivA)} und {CivName(e.CivB)} schließen ein Bündnis.", e.CivA);

        private void OnCaptured(SettlementCapturedEvent e)
        {
            string place = SettlementName(e.SettlementId);
            AddAtSettlement(FeedKind.Conquest, $"{CivName(e.NewCivId)} erobert {place} von {CivName(e.OldCivId)}.", e.SettlementId);
        }

        private void OnFounded(SettlementFoundedEvent e)
        {
            // Hauptstädte meldet bereits CivilizationCreatedEvent.
            if (_civs != null && _civs.TryGet(e.CivId, out var v) && v.CapitalSettlementId == e.SettlementId) return;
            Add(FeedKind.Founding, $"{CivName(e.CivId)} gründet {SettlementName(e.SettlementId)}.", true, e.X, e.Y);
        }

        private void OnCollapsed(CivilizationCollapsedEvent e) =>
            Add(FeedKind.Collapse, $"{CivName(e.CivId)} ist zerfallen.", false, 0, 0);

        private void OnEpoch(EpochChangedEvent e) =>
            AddAtCapital(FeedKind.Progress, $"{CivName(e.CivId)} erreicht die {UiText.Epoch(e.NewEpoch)}.", e.CivId);

        // ---------- Hilfen ----------

        private void RememberAllCivNames()
        {
            if (_civs == null) return;
            var ids = _civs.AllCivIds;
            for (int i = 0; i < ids.Count; i++) CivName(ids[i]);
        }

        /// <summary>Name aus der Abfrage, sonst aus dem Zwischenspeicher, sonst "Zivilisation N".</summary>
        public string CivName(int civId)
        {
            if (_civs != null && _civs.TryGet(civId, out var v) && !string.IsNullOrEmpty(v.Name))
            {
                _civNames[civId] = v.Name;
                return v.Name;
            }
            return _civNames.TryGetValue(civId, out var cached) ? cached : $"Zivilisation {civId}";
        }

        private string SettlementName(int settlementId) =>
            _settlements != null && _settlements.TryGet(settlementId, out var s) && !string.IsNullOrEmpty(s.Name) ? s.Name : "eine Siedlung";

        private void AddAtSettlement(FeedKind kind, string text, int settlementId)
        {
            if (_settlements != null && _settlements.TryGet(settlementId, out var s)) Add(kind, text, true, s.X, s.Y);
            else Add(kind, text, false, 0, 0);
        }

        private void AddAtCapital(FeedKind kind, string text, int civId)
        {
            int capital = _civs != null && _civs.TryGet(civId, out var v) ? v.CapitalSettlementId : 0;
            AddAtSettlement(kind, text, capital);
        }

        private void Add(FeedKind kind, string text, bool hasLocation, int x, int y)
        {
            if (_entries.Count >= _capacity) _entries.RemoveAt(0);
            _entries.Add(new FeedEntry
            {
                Tick = _ctx != null ? _ctx.Clock.Tick : 0,
                Kind = kind,
                Text = text,
                HasLocation = hasLocation,
                TileX = x,
                TileY = y,
            });
            Version++;
        }
    }
}
