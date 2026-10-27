using System.Collections.Generic;
using WorldBeyond.Core;
using WorldBeyond.Simulation;
using WorldBeyond.Simulation.Civilizations;

namespace WorldBeyond.Presentation.WorldObjects
{
    // Grobe Form eines Gebäudes für die Ersatzmodelle (Platzhalter bis R2, UI_STYLE_GUIDE A7).
    // Explizite Werte, nur anhängen: der Renderer nutzt sie als Index in sein Mesh-Feld.
    public enum BuildingShape : byte
    {
        Center = 0,     // Ortszentrum: großes Haus mit Turm
        Hut = 1,        // Hütte/Zelt
        House = 2,      // Wohnhaus mit Satteldach
        Block = 3,      // Mehrfamilienhaus
        Field = 4,      // Feld (flach)
        Workshop = 5,   // Produktion: Halle mit Schornstein
        Storage = 6,    // Lager: breiter Schuppen
        Library = 7,    // Forschung: Säulenbau
        Wall = 8,       // Palisade/Mauer
        Barracks = 9,   // Kaserne: lange Halle mit Fahne
    }

    public enum BuildingState : byte { UnderConstruction = 0, Complete = 1, Ruin = 2 }

    public struct BuildingInstance
    {
        public EntityId Id;
        public BuildingShape Shape;
        public BuildingState State;
        public int CivId;                // 0 = unbekannt (Siedlung aufgegeben)
        public int X, Y, SizeX, SizeY;   // Kacheln
        public float Height01;           // Bauhöhe 0,2 … 1 (Baufortschritt), Ruine 0,35
    }

    /// <summary>
    /// Unity-freie Liste aller sichtbaren Gebäude inklusive kürzlich zerstörter (Ruine, ein Spieljahr lang).
    /// Liest nur IBuildingQuery/ISettlementQuery und Gebäude-/Siedlungs-Events. Version steigt nur bei Änderung,
    /// damit der Renderer seine Instanzlisten nicht jedes Bild neu baut.
    /// </summary>
    public sealed class BuildingVisuals
    {
        public const float MinConstructionHeight = 0.2f;
        public const float RuinHeight = 0.35f;
        public static long RuinTicks = CivTime.TicksPerYear;

        // Baufortschritt sichtbar, aber nicht jeden Tick neu bauen: alle 10 Ticks (= 1 s bei 1×).
        public const int ProgressRefreshTicks = 10;

        readonly List<BuildingInstance> _items = new List<BuildingInstance>();
        readonly Dictionary<EntityId, BuildingInstance> _lastKnown = new Dictionary<EntityId, BuildingInstance>();
        readonly List<(BuildingInstance item, long until)> _ruins = new List<(BuildingInstance, long)>();
        readonly List<EntityId> _ids = new List<EntityId>();
        SimContext _ctx;
        IBuildingQuery _buildings;
        ISettlementQuery _settlements;
        bool _dirty = true;
        long _lastRefreshTick;

        public IReadOnlyList<BuildingInstance> Items => _items;
        public int Version { get; private set; }

        public static BuildingShape ShapeFor(string typeKey)
        {
            switch (typeKey)
            {
                case "building.town_center": return BuildingShape.Center;
                case "building.hut": return BuildingShape.Hut;
                case "building.house": return BuildingShape.House;
                case "building.apartment": return BuildingShape.Block;
                case "building.farm": return BuildingShape.Field;
                case "building.storehouse":
                case "building.warehouse": return BuildingShape.Storage;
                case "building.library": return BuildingShape.Library;
                case "building.palisade":
                case "building.stone_wall": return BuildingShape.Wall;
                case "building.barracks": return BuildingShape.Barracks;
                // fishery, woodcutter, quarry, mine, gold_mine, smelter, workshop, oil_well und unbekannte Produktionsbauten
                default: return BuildingShape.Workshop;
            }
        }

        public void Attach(SimContext ctx)
        {
            Detach();
            _ctx = ctx;
            if (ctx == null || !ctx.TryGet(out _buildings) || !ctx.TryGet(out _settlements))
            {
                _ctx = null;
                return;
            }
            ctx.Events.Subscribe<BuildingPlacedEvent>(OnPlaced);
            ctx.Events.Subscribe<BuildingCompletedEvent>(OnCompleted);
            ctx.Events.Subscribe<BuildingDestroyedEvent>(OnDestroyed);
            ctx.Events.Subscribe<SettlementCapturedEvent>(OnCaptured);
            _dirty = true;
            _lastRefreshTick = ctx.Clock.Tick;
        }

        public void Detach()
        {
            if (_ctx != null)
            {
                _ctx.Events.Unsubscribe<BuildingPlacedEvent>(OnPlaced);
                _ctx.Events.Unsubscribe<BuildingCompletedEvent>(OnCompleted);
                _ctx.Events.Unsubscribe<BuildingDestroyedEvent>(OnDestroyed);
                _ctx.Events.Unsubscribe<SettlementCapturedEvent>(OnCaptured);
            }
            _ctx = null;
            _buildings = null;
            _settlements = null;
            _items.Clear();
            _lastKnown.Clear();
            _ruins.Clear();
            Version++;
        }

        void OnPlaced(BuildingPlacedEvent e) => _dirty = true;
        void OnCompleted(BuildingCompletedEvent e) => _dirty = true;
        void OnCaptured(SettlementCapturedEvent e) => _dirty = true;

        void OnDestroyed(BuildingDestroyedEvent e)
        {
            _dirty = true;
            if (_ctx == null || !_lastKnown.TryGetValue(e.Building, out var last))
                return;   // vor dem ersten Refresh entstanden und wieder zerstört: keine Maße bekannt, keine Ruine
            last.State = BuildingState.Ruin;
            last.Height01 = RuinHeight;
            _ruins.Add((last, _ctx.Clock.Tick + RuinTicks));
        }

        /// <summary>Aktualisiert die Liste, wenn sich etwas geändert hat. true = neue Version.</summary>
        public bool Refresh()
        {
            if (_ctx == null)
                return false;
            long tick = _ctx.Clock.Tick;
            bool expired = _ruins.RemoveAll(r => r.until <= tick) > 0;
            bool progress = HasConstruction && tick - _lastRefreshTick >= ProgressRefreshTicks;
            if (!_dirty && !expired && !progress)
                return false;

            _dirty = false;
            _lastRefreshTick = tick;
            _items.Clear();
            _lastKnown.Clear();
            HasConstruction = false;
            _ids.Clear();
            _buildings.CopyAliveIds(_ids);
            foreach (EntityId id in _ids)
            {
                if (!_buildings.TryGet(id, out BuildingView v))
                    continue;
                var item = new BuildingInstance
                {
                    Id = id,
                    Shape = ShapeFor(v.TypeKey),
                    State = v.Completed ? BuildingState.Complete : BuildingState.UnderConstruction,
                    CivId = _settlements.TryGet(v.SettlementId, out SettlementView s) ? s.CivId : 0,
                    X = v.X, Y = v.Y,
                    SizeX = v.SizeX < 1 ? 1 : v.SizeX,
                    SizeY = v.SizeY < 1 ? 1 : v.SizeY,
                    Height01 = v.Completed ? 1f : Clamp(v.Construction01, MinConstructionHeight, 1f),
                };
                if (!v.Completed)
                    HasConstruction = true;
                _items.Add(item);
                _lastKnown[id] = item;
            }
            foreach (var r in _ruins)
                _items.Add(r.item);
            Version++;
            return true;
        }

        public bool HasConstruction { get; private set; }

        static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
    }
}
