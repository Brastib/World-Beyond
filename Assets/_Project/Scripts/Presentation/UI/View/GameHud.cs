using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBeyond.Application;
using WorldBeyond.Presentation.CameraControl;
using WorldBeyond.Simulation;
using WorldBeyond.Simulation.Civilizations;
using WorldBeyond.Simulation.Diplomacy;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Presentation.UI
{
    // M1-Oberfläche (INTEGRATION §4, UI_STYLE_GUIDE B0 „Beobachten zuerst“): Zeitleiste, Zivilisationsübersicht,
    // Ereignis-Feed, Auswahl-Info, Speichern/Laden. Liest nur über Queries; ändert den Zustand nur über
    // ISimulationRunner.Speed und ISaveLoadService. Nichts davon hält die Simulation an, außer der Pause-Knopf.
    // Baut ein UIDocument zur Laufzeit (eigene PanelSettings), damit keine UI-Assets im Projekt nötig sind.
    // Szene: auf ein GameObject legen; Verweise werden sonst per FindFirstObjectByType gesucht.
    public sealed class GameHud : MonoBehaviour
    {
        [SerializeField] GameBootstrap bootstrap;
        [SerializeField] SaveLoadController saveLoad;
        [SerializeField] WorldSelection selection;
        [SerializeField] StrategyCameraController cameraController;
        [Tooltip("Optional: Unity-Runtime-Theme. Ohne Theme funktioniert die HUD trotzdem, weil sie nur eigene Stile nutzt.")]
        [SerializeField] ThemeStyleSheet theme;
        [SerializeField] Vector2Int referenceResolution = new Vector2Int(1920, 1080);
        [SerializeField, Range(0.75f, 1.5f)] float uiScale = 1f;
        [SerializeField] int visibleFeedEntries = 12;
        [SerializeField] float overviewRefreshSeconds = 0.5f;
        [SerializeField] float selectionRefreshSeconds = 0.25f;

        readonly CivOverviewModel _overview = new CivOverviewModel();
        readonly EventFeed _feed = new EventFeed();
        readonly SelectionInfoModel _info = new SelectionInfoModel();
        readonly SaveMenuModel _saveMenu = new SaveMenuModel();
        readonly List<UiTheme.UiButton> _speedButtons = new List<UiTheme.UiButton>();

        GameComposition _comp;
        UIDocument _document;
        PanelSettings _panelSettings;
        VisualElement _root, _civList, _feedList, _infoPanel, _infoLines, _savePanel, _saveRows, _toastBox;
        Label _dateLabel, _infoTitle, _infoSubtitle, _toastText, _saveMessage;
        UiTheme.UiButton _civToggle;
        bool _civCollapsed;
        GameSpeed _lastRunning = GameSpeed.x1;
        long _shownTick = -1;
        GameSpeed _shownSpeed = (GameSpeed)(-1);
        int _shownFeedVersion = -1;
        float _overviewTimer, _selectionTimer, _toastTimer;
        WorldSelectionInfo _selected;
        int _lodSettlement;
        string _confirmSlot;      // zweiter Klick bestätigt Überschreiben/Laden
        bool _confirmIsLoad;

        // ---------------- Lebenszyklus ----------------

        void OnEnable()
        {
            if (bootstrap == null) bootstrap = FindFirstObjectByType<GameBootstrap>();
            if (saveLoad == null && bootstrap != null) saveLoad = bootstrap.GetComponent<SaveLoadController>();
            if (saveLoad == null) saveLoad = FindFirstObjectByType<SaveLoadController>();
            if (selection == null) selection = FindFirstObjectByType<WorldSelection>();
            if (cameraController == null) cameraController = FindFirstObjectByType<StrategyCameraController>();

            BuildDocument();
            if (bootstrap != null)
            {
                bootstrap.CompositionChanged += OnCompositionChanged;
                OnCompositionChanged(bootstrap.Composition);
            }
            else
                Debug.LogWarning("GameHud: Kein GameBootstrap in der Szene. Die Oberfläche bleibt leer.");
            if (selection != null)
            {
                selection.SelectionChanged += OnSelectionChanged;
                selection.IsPointerOverUi = IsPointerOverUi;
            }
        }

        void OnDisable()
        {
            if (bootstrap != null) bootstrap.CompositionChanged -= OnCompositionChanged;
            if (selection != null)
            {
                selection.SelectionChanged -= OnSelectionChanged;
                if (selection.IsPointerOverUi == IsPointerOverUi) selection.IsPointerOverUi = null;
            }
            _feed.Detach();
            SetLodSettlement(0);
            _comp = null;
            if (_document != null) Destroy(_document.gameObject);
            if (_panelSettings != null) Destroy(_panelSettings);
            _document = null;
            _panelSettings = null;
            _speedButtons.Clear();
        }

        void OnCompositionChanged(GameComposition comp)
        {
            SetLodSettlement(0);
            _comp = comp;
            _feed.Attach(comp?.Context);
            _info.Clear();
            _selected = default;
            _shownTick = -1;
            _shownFeedVersion = -1;
            _overviewTimer = 0f;
            RefreshAll();
        }

        void Update()
        {
            if (_root == null) return;
            HandleKeys();

            float dt = Time.unscaledDeltaTime;
            RefreshTimeBar();
            if (_shownFeedVersion != _feed.Version) RebuildFeed();
            if ((_overviewTimer -= dt) <= 0f) { _overviewTimer = overviewRefreshSeconds; RebuildOverview(); }
            if ((_selectionTimer -= dt) <= 0f) { _selectionTimer = selectionRefreshSeconds; RefreshSelection(); }
            if (_toastTimer > 0f && (_toastTimer -= dt) <= 0f) _toastBox.style.display = DisplayStyle.None;
        }

        void HandleKeys()
        {
            var runner = _comp?.Runner;
            if (runner == null) return;
            if (HudKeys.EscapePressed && _savePanel.style.display == DisplayStyle.Flex) { CloseSaveMenu(); return; }
            if (HudKeys.PausePressed) SetSpeed(TimeControl.Toggle(runner.Speed, _lastRunning));
            int k = HudKeys.SpeedKey;
            if (k > 0 && k < TimeControl.Speeds.Count) SetSpeed(TimeControl.Speeds[k]);
        }

        // ---------------- Aufbau ----------------

        void BuildDocument()
        {
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _panelSettings.name = "GameHudPanel";
            _panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            _panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            _panelSettings.match = 0.5f;
            _panelSettings.referenceResolution = new Vector2Int(
                Mathf.RoundToInt(referenceResolution.x / uiScale), Mathf.RoundToInt(referenceResolution.y / uiScale));
            if (theme != null) _panelSettings.themeStyleSheet = theme;

            // Inaktiv anlegen, damit UIDocument erst mit gesetzten PanelSettings aktiviert wird.
            var go = new GameObject("GameHud Document");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            _document = go.AddComponent<UIDocument>();
            _document.panelSettings = _panelSettings;
            go.SetActive(true);

            _root = _document.rootVisualElement;
            _root.pickingMode = PickingMode.Ignore;
            _root.style.flexGrow = 1f;
            _root.Add(BuildTimeBar());
            _root.Add(BuildCivPanel());
            _root.Add(BuildFeedPanel());
            _root.Add(BuildInfoPanel());
            _root.Add(BuildSaveMenu());
            _root.Add(BuildToast());
        }

        VisualElement BuildTimeBar()
        {
            var bar = UiTheme.Panel("TimeBar");
            var s = bar.style;
            s.position = Position.Absolute;
            s.top = UiTheme.S3;
            s.alignSelf = Align.Center;
            s.left = new Length(50f, LengthUnit.Percent);
            s.translate = new Translate(new Length(-50f, LengthUnit.Percent), 0f);
            s.flexDirection = FlexDirection.Row;
            s.alignItems = Align.Center;
            UiTheme.SetPadding(s, UiTheme.S2);

            _dateLabel = UiTheme.Text("", UiTheme.FontHeading, UiTheme.TextHi);
            _dateLabel.style.minWidth = 170f;
            _dateLabel.style.marginRight = UiTheme.S3;
            _dateLabel.style.marginLeft = UiTheme.S2;
            bar.Add(_dateLabel);

            foreach (var speed in TimeControl.Speeds)
            {
                var sp = speed;
                var b = new UiTheme.UiButton(TimeControl.Label(sp), () => SetSpeed(sp), TimeControl.Tooltip(sp));
                b.style.marginRight = UiTheme.S1;
                _speedButtons.Add(b);
                bar.Add(b);
            }

            var menu = new UiTheme.UiButton("Speichern / Laden", OpenSaveMenu, "Spielstände verwalten. Das Spiel läuft dabei weiter.");
            menu.style.marginLeft = UiTheme.S3;
            bar.Add(menu);
            return bar;
        }

        VisualElement BuildCivPanel()
        {
            var p = UiTheme.Panel("CivOverview");
            var s = p.style;
            s.position = Position.Absolute;
            s.left = UiTheme.S3;
            s.top = 72f;
            s.width = 320f;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            var h = UiTheme.Heading("ZIVILISATIONEN");
            header.Add(h);
            _civToggle = new UiTheme.UiButton("–", ToggleCivPanel, "Übersicht ein- oder ausklappen");
            _civToggle.style.height = 24f;
            _civToggle.style.minWidth = 28f;
            header.Add(_civToggle);
            p.Add(header);

            _civList = new VisualElement { name = "CivList" };
            p.Add(_civList);
            return p;
        }

        VisualElement BuildFeedPanel()
        {
            var p = UiTheme.Panel("EventFeed");
            var s = p.style;
            s.position = Position.Absolute;
            s.right = UiTheme.S3;
            s.top = 72f;
            s.width = 360f;
            p.Add(UiTheme.Heading("EREIGNISSE"));
            _feedList = new VisualElement { name = "FeedList" };
            p.Add(_feedList);
            return p;
        }

        VisualElement BuildInfoPanel()
        {
            _infoPanel = UiTheme.Panel("SelectionInfo");
            var s = _infoPanel.style;
            s.position = Position.Absolute;
            s.left = UiTheme.S3;
            s.bottom = UiTheme.S3;
            s.width = 360f;
            s.display = DisplayStyle.None;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            var titles = new VisualElement();
            _infoTitle = UiTheme.Text("", UiTheme.FontTitle, UiTheme.TextHi);
            _infoTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _infoSubtitle = UiTheme.Text("", UiTheme.FontSmall, UiTheme.TextLo);
            titles.Add(_infoTitle);
            titles.Add(_infoSubtitle);
            header.Add(titles);
            var close = new UiTheme.UiButton("×", () => { if (selection != null) selection.Clear(); else ClearSelectionInfo(); }, "Auswahl aufheben");
            close.style.height = 24f;
            close.style.minWidth = 28f;
            header.Add(close);
            _infoPanel.Add(header);

            _infoLines = new VisualElement();
            _infoLines.style.marginTop = UiTheme.S2;
            _infoPanel.Add(_infoLines);
            return _infoPanel;
        }

        VisualElement BuildSaveMenu()
        {
            _savePanel = UiTheme.Panel("SaveMenu");
            var s = _savePanel.style;
            s.position = Position.Absolute;
            s.left = new Length(50f, LengthUnit.Percent);
            s.top = new Length(50f, LengthUnit.Percent);
            s.translate = new Translate(new Length(-50f, LengthUnit.Percent), new Length(-50f, LengthUnit.Percent));
            s.width = 560f;
            s.display = DisplayStyle.None;
            UiTheme.SetPadding(s, UiTheme.S4);

            _savePanel.Add(UiTheme.Heading("SPEICHERN / LADEN"));
            var hint = UiTheme.Text("Die Welt läuft weiter, während dieses Fenster offen ist.", UiTheme.FontSmall, UiTheme.TextLo);
            hint.style.marginBottom = UiTheme.S3;
            _savePanel.Add(hint);
            _saveRows = new VisualElement();
            _savePanel.Add(_saveRows);
            _saveMessage = UiTheme.Text("", UiTheme.FontBody, UiTheme.TextHi);
            _saveMessage.style.marginTop = UiTheme.S3;
            _savePanel.Add(_saveMessage);
            var close = new UiTheme.UiButton("Schließen", CloseSaveMenu, "Esc");
            close.style.marginTop = UiTheme.S3;
            close.style.alignSelf = Align.FlexEnd;
            _savePanel.Add(close);
            return _savePanel;
        }

        VisualElement BuildToast()
        {
            _toastBox = UiTheme.Panel("Toast");
            var s = _toastBox.style;
            s.position = Position.Absolute;
            s.bottom = UiTheme.S6;
            s.left = new Length(50f, LengthUnit.Percent);
            s.translate = new Translate(new Length(-50f, LengthUnit.Percent), 0f);
            s.display = DisplayStyle.None;
            _toastBox.pickingMode = PickingMode.Ignore;
            _toastText = UiTheme.Text("", UiTheme.FontBody, UiTheme.TextHi);
            _toastBox.Add(_toastText);
            return _toastBox;
        }

        // ---------------- Aktualisierung ----------------

        void RefreshAll()
        {
            if (_root == null) return;
            RefreshTimeBar();
            RebuildOverview();
            RebuildFeed();
            ClearSelectionInfo();
        }

        void RefreshTimeBar()
        {
            var runner = _comp?.Runner;
            long tick = _comp != null ? _comp.Context.Clock.Tick : 0;
            if (tick != _shownTick)
            {
                _shownTick = tick;
                _dateLabel.text = _comp != null ? GameDate.Format(tick) : "Keine Welt geladen";
            }
            var speed = runner != null ? runner.Speed : GameSpeed.Paused;
            if (speed == _shownSpeed) return;
            _shownSpeed = speed;
            for (int i = 0; i < _speedButtons.Count; i++)
            {
                _speedButtons[i].Active = TimeControl.Speeds[i] == speed;
                _speedButtons[i].Enabled = runner != null;
            }
        }

        void SetSpeed(GameSpeed speed)
        {
            var runner = _comp?.Runner;
            if (runner == null) return;
            if (speed != GameSpeed.Paused) _lastRunning = speed;
            else if (runner.Speed != GameSpeed.Paused) _lastRunning = runner.Speed;
            runner.Speed = speed;
            RefreshTimeBar();
        }

        void ToggleCivPanel()
        {
            _civCollapsed = !_civCollapsed;
            _civList.style.display = _civCollapsed ? DisplayStyle.None : DisplayStyle.Flex;
            _civToggle.text = _civCollapsed ? "+" : "–";
        }

        void RebuildOverview()
        {
            if (_civList == null || _civCollapsed) return;
            var ctx = _comp?.Context;
            ICivilizationQuery civs = null;
            IWarQuery wars = null;
            ctx?.TryGet(out civs);
            ctx?.TryGet(out wars);
            _overview.Refresh(civs, wars);

            _civList.Clear();
            if (_overview.Count == 0)
            {
                _civList.Add(UiTheme.Text(ctx == null ? "Keine Welt geladen." : "Keine lebende Zivilisation.", UiTheme.FontBody, UiTheme.TextLo));
                return;
            }
            for (int i = 0; i < _overview.Count; i++)
            {
                var r = _overview[i];
                var row = new VisualElement();
                row.style.marginBottom = UiTheme.S2;
                row.style.paddingLeft = UiTheme.S2;
                row.style.borderLeftWidth = 3f;
                row.style.borderLeftColor = UiTheme.Civ(r.Color);

                var name = UiTheme.Text(r.Name, UiTheme.FontBody, UiTheme.TextHi);
                name.style.unityFontStyleAndWeight = FontStyle.Bold;
                row.Add(name);
                row.Add(UiTheme.Text($"Einwohner {r.Population} · Siedlungen {r.Settlements} · Militär {r.MilitaryStrength}",
                    UiTheme.FontNumber, UiTheme.TextHi));
                var status = UiTheme.Text($"{r.Epoch} · {r.WarStatus}", UiTheme.FontSmall, r.AtWar ? UiTheme.Danger : UiTheme.TextLo);
                row.Add(status);

                int civId = r.CivId;
                row.tooltip = "Klicken: Kamera zur Hauptstadt";
                row.AddManipulator(new Clickable(() => FocusCapital(civId)));
                _civList.Add(row);
            }
        }

        void RebuildFeed()
        {
            if (_feedList == null) return;
            _shownFeedVersion = _feed.Version;
            _feedList.Clear();
            var entries = _feed.Entries;
            if (entries.Count == 0)
            {
                _feedList.Add(UiTheme.Text("Noch keine Ereignisse. Die Zivilisationen entwickeln sich von selbst.", UiTheme.FontSmall, UiTheme.TextLo));
                return;
            }
            int shown = 0;
            for (int i = entries.Count - 1; i >= 0 && shown < visibleFeedEntries; i--, shown++)
            {
                var e = entries[i];
                var row = new VisualElement();
                row.style.marginBottom = UiTheme.S2;
                row.style.paddingLeft = UiTheme.S2;
                row.style.borderLeftWidth = 2f;
                row.style.borderLeftColor = UiTheme.ForFeed(e.Kind);
                row.Add(UiTheme.Text(GameDate.Format(e.Tick), UiTheme.FontSmall, UiTheme.TextLo));
                row.Add(UiTheme.Text(e.Text, UiTheme.FontBody, UiTheme.TextHi));
                if (e.HasLocation)
                {
                    int x = e.TileX, y = e.TileY;
                    row.tooltip = "Klicken: Kamera zum Ort";
                    row.AddManipulator(new Clickable(() => FocusTile(x, y, select: true)));
                }
                _feedList.Add(row);
            }
        }

        // ---------------- Auswahl ----------------

        void OnSelectionChanged(WorldSelectionInfo info)
        {
            _selected = info;
            _selectionTimer = 0f;
            RefreshSelection();
        }

        void RefreshSelection()
        {
            var ctx = _comp?.Context;
            bool ok = false;
            if (ctx != null && _selected.Kind == WorldSelectionKind.Unit) ok = _info.RefreshUnit(ctx, _selected.Unit);
            else if (ctx != null && _selected.Kind == WorldSelectionKind.Tile) ok = _info.RefreshTile(ctx, _selected.TileX, _selected.TileY);
            if (!ok) { ClearSelectionInfo(); return; }

            SetLodSettlement(_info.SettlementId);
            _infoTitle.text = _info.Title;
            _infoSubtitle.text = _info.Subtitle ?? "";
            _infoLines.Clear();
            foreach (var line in _info.Lines)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.minHeight = 20f;
                var label = UiTheme.Text(line.Label, UiTheme.FontSmall, UiTheme.TextLo);
                label.style.width = 100f;
                label.style.flexShrink = 0f;
                var value = UiTheme.Text(line.Value, UiTheme.FontNumber, UiTheme.TextHi);
                value.style.flexShrink = 1f;
                row.Add(label);
                row.Add(value);
                _infoLines.Add(row);
            }
            _infoPanel.style.display = DisplayStyle.Flex;
        }

        void ClearSelectionInfo()
        {
            _info.Clear();
            SetLodSettlement(0);
            if (_infoPanel != null) _infoPanel.style.display = DisplayStyle.None;
        }

        // Ausgewählte Siedlung läuft im vollen Takt (ICivSimulationLod, Besitzer Civ). 0 = keine.
        void SetLodSettlement(int settlementId)
        {
            if (settlementId == _lodSettlement) return;
            _lodSettlement = settlementId;
            if (_comp != null && _comp.Context.TryGet(out ICivSimulationLod lod)) lod.SetSelectedSettlement(settlementId);
        }

        void FocusCapital(int civId)
        {
            var ctx = _comp?.Context;
            if (ctx == null || !ctx.TryGet(out ICivilizationQuery civs) || !ctx.TryGet(out ISettlementQuery settlements)) return;
            if (!civs.TryGet(civId, out var civ) || !settlements.TryGet(civ.CapitalSettlementId, out var capital)) return;
            FocusTile(capital.X, capital.Y, select: true);
        }

        void FocusTile(int x, int y, bool select)
        {
            var ctx = _comp?.Context;
            if (ctx == null || !ctx.TryGet(out IWorldQuery world) || !world.InBounds(x, y)) return;
            var p = WorldCoords.TileToWorld(world, x, y);
            if (cameraController != null) cameraController.FocusOn(new Vector3(p.X, p.Y, p.Z));
            if (select && selection != null) selection.SelectTile(x, y);
        }

        bool IsPointerOverUi()
        {
            var panel = _root?.panel;
            if (panel == null) return false;
            Vector2 mouse = PointerInput.MousePosition;
            Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(mouse.x, Screen.height - mouse.y));
            // Nur eigene Panels zählen; Wurzel und Panel-Baum darüber sind nicht „UI unter dem Zeiger“.
            for (var e = panel.Pick(panelPos); e != null; e = e.parent)
                if (e.parent == _root)
                    return true;
            return false;
        }

        // ---------------- Speichern / Laden ----------------

        void OpenSaveMenu()
        {
            _confirmSlot = null;
            _saveMessage.text = "";
            _saveMenu.Refresh(saveLoad != null ? saveLoad.Service : null);
            if (saveLoad == null || saveLoad.Service == null)
                SetSaveMessage("Speichern und Laden sind nicht verfügbar: In der Szene fehlt der SaveLoadController.", true);
            RebuildSaveRows();
            _savePanel.style.display = DisplayStyle.Flex;
        }

        void CloseSaveMenu()
        {
            _confirmSlot = null;
            _savePanel.style.display = DisplayStyle.None;
        }

        void RebuildSaveRows()
        {
            _saveRows.Clear();
            bool available = saveLoad != null && saveLoad.Service != null;
            foreach (var r in _saveMenu.Rows)
            {
                var row = new VisualElement();
                var s = row.style;
                s.flexDirection = FlexDirection.Row;
                s.alignItems = Align.Center;
                s.minHeight = UiTheme.ButtonHeight + UiTheme.S2;
                s.borderBottomWidth = 1f;
                s.borderBottomColor = UiTheme.Line;

                var texts = new VisualElement();
                texts.style.flexGrow = 1f;
                texts.Add(UiTheme.Text(r.Title, UiTheme.FontBody, UiTheme.TextHi));
                texts.Add(UiTheme.Text(r.Detail, UiTheme.FontSmall, r.Exists && !r.CanLoad ? UiTheme.Warn : UiTheme.TextLo));
                row.Add(texts);

                string slot = r.Slot;
                bool confirmSave = _confirmSlot == slot && !_confirmIsLoad;
                bool confirmLoad = _confirmSlot == slot && _confirmIsLoad;
                var save = new UiTheme.UiButton(confirmSave ? "Überschreiben?" : "Speichern", () => OnSaveClicked(slot, r.Exists),
                    r.Exists ? "Vorhandenen Spielstand überschreiben (zweiter Klick bestätigt)" : "Aktuelle Welt hier speichern", danger: confirmSave);
                save.Enabled = available;
                save.style.marginLeft = UiTheme.S2;
                var load = new UiTheme.UiButton(confirmLoad ? "Wirklich laden?" : "Laden", () => OnLoadClicked(slot),
                    "Diesen Spielstand laden. Nicht gespeicherter Fortschritt geht verloren (zweiter Klick bestätigt).", danger: confirmLoad);
                load.Enabled = available && r.CanLoad;
                load.style.marginLeft = UiTheme.S2;
                row.Add(save);
                row.Add(load);
                _saveRows.Add(row);
            }
        }

        void OnSaveClicked(string slot, bool exists)
        {
            if (exists && !(_confirmSlot == slot && !_confirmIsLoad))
            {
                _confirmSlot = slot;
                _confirmIsLoad = false;
                SetSaveMessage("Dieser Platz ist belegt. Zum Überschreiben noch einmal klicken.", false);
                RebuildSaveRows();
                return;
            }
            _confirmSlot = null;
            bool ok = _saveMenu.Save(saveLoad.Service, slot);
            ReportSaveResult(ok);
        }

        void OnLoadClicked(string slot)
        {
            if (!(_confirmSlot == slot && _confirmIsLoad))
            {
                _confirmSlot = slot;
                _confirmIsLoad = true;
                SetSaveMessage("Laden ersetzt die laufende Welt. Zum Bestätigen noch einmal klicken.", false);
                RebuildSaveRows();
                return;
            }
            _confirmSlot = null;
            bool ok = _saveMenu.Load(saveLoad.Service, slot);
            ReportSaveResult(ok);
            if (ok) CloseSaveMenu();
        }

        void ReportSaveResult(bool ok)
        {
            SetSaveMessage(_saveMenu.LastMessage, !ok);
            if (!ok && !string.IsNullOrEmpty(_saveMenu.LastDetail))
                Debug.LogWarning($"GameHud Speichern/Laden: {_saveMenu.LastMessage} ({_saveMenu.LastDetail})");
            ShowToast(_saveMenu.LastMessage, !ok);
            RebuildSaveRows();
        }

        void SetSaveMessage(string text, bool error)
        {
            _saveMessage.text = text ?? "";
            _saveMessage.style.color = error ? UiTheme.Danger : UiTheme.TextHi;
        }

        void ShowToast(string text, bool error)
        {
            _toastText.text = text ?? "";
            _toastText.style.color = error ? UiTheme.Danger : UiTheme.TextHi;
            _toastBox.style.borderLeftWidth = 3f;
            _toastBox.style.borderLeftColor = error ? UiTheme.Danger : UiTheme.Ok;
            _toastBox.style.display = DisplayStyle.Flex;
            _toastTimer = 4f;
        }
    }
}
