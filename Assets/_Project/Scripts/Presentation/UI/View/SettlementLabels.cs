using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using WorldBeyond.Simulation;
using WorldBeyond.Simulation.Civilizations;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Presentation.UI
{
    // Siedlungsnamen als Schild über dem Ortskern (UI_STYLE_GUIDE B§7: Namen ab mittlerer Zoomstufe).
    // Sichtbar, solange die Kamera nicht weiter als maxDistance vom Ort entfernt ist; Hauptstädte und große Orte
    // bleiben weiter sichtbar als Lager. Nur Anzeige, nicht anklickbar (PickingMode.Ignore), damit die Weltauswahl
    // darunter weiter funktioniert.
    public sealed class SettlementLabels
    {
        sealed class Tag
        {
            public VisualElement Box;
            public Label Text;
            public VisualElement Swatch;
            public int Version;
        }

        readonly VisualElement _layer;
        readonly Dictionary<int, Tag> _tags = new Dictionary<int, Tag>();
        readonly List<int> _remove = new List<int>();
        int _version;

        public float MaxDistance = 1400f;   // Meter Kamera → Ort, für Großstadt/Hauptstadt
        public float MinTierFactor = 0.45f; // Lager sichtbar bis MaxDistance × Faktor
        public bool Visible = true;

        public SettlementLabels(VisualElement parent)
        {
            _layer = new VisualElement { name = "SettlementLabels", pickingMode = PickingMode.Ignore };
            _layer.style.position = Position.Absolute;
            _layer.style.left = 0f; _layer.style.top = 0f; _layer.style.right = 0f; _layer.style.bottom = 0f;
            parent.Insert(0, _layer);   // hinter allen Panels
        }

        public void Clear()
        {
            _layer.Clear();
            _tags.Clear();
        }

        public void Update(SimContext ctx, Camera cam)
        {
            var panel = _layer.panel;
            if (!Visible || ctx == null || cam == null || panel == null ||
                !ctx.TryGet(out ISettlementQuery settlements) || !ctx.TryGet(out IWorldQuery world))
            {
                _layer.style.display = DisplayStyle.None;
                return;
            }
            _layer.style.display = DisplayStyle.Flex;
            ctx.TryGet(out ICivilizationQuery civs);
            _version++;
            Vector3 camPos = cam.transform.position;
            var ids = settlements.AllSettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                if (!settlements.TryGet(ids[i], out SettlementView s) || !world.InBounds(s.X, s.Y))
                    continue;
                var p = WorldCoords.TileToWorld(world, s.X, s.Y);
                var pos = new Vector3(p.X, p.Y + 14f, p.Z);
                float tierFactor = s.IsCapital ? 1f : Mathf.Lerp(MinTierFactor, 1f, (int)s.Tier / 4f);
                float dist = Vector3.Distance(camPos, pos);
                Vector3 vp = cam.WorldToViewportPoint(pos);
                if (dist > MaxDistance * tierFactor || vp.z <= 0f || vp.x < -0.05f || vp.x > 1.05f || vp.y < -0.05f || vp.y > 1.05f)
                    continue;

                if (!_tags.TryGetValue(s.Id, out Tag tag))
                    _tags[s.Id] = tag = CreateTag();
                tag.Version = _version;
                string name = s.IsCapital ? "★ " + s.Name : s.Name;
                if (tag.Text.text != name) tag.Text.text = name;
                Color civColor = civs != null && civs.TryGet(s.CivId, out CivilizationView cv) ? UiTheme.Civ(cv.Color) : UiTheme.TextLo;
                tag.Swatch.style.backgroundColor = civColor;
                tag.Text.style.fontSize = s.IsCapital || s.Tier >= SettlementTier.City ? UiTheme.FontHeading : UiTheme.FontSmall;

                Vector2 panelPos = RuntimePanelUtils.CameraTransformWorldToPanel(panel, pos, cam);
                tag.Box.style.left = panelPos.x;
                tag.Box.style.top = panelPos.y;
                // Nähere Orte vorn: einfache Sortierung über die Reihenfolge ist zu teuer, Deckkraft reicht als Tiefenhinweis.
                tag.Box.style.opacity = Mathf.Lerp(1f, 0.55f, dist / (MaxDistance * tierFactor));
                tag.Box.style.display = DisplayStyle.Flex;
            }

            _remove.Clear();
            foreach (var kv in _tags)
            {
                if (kv.Value.Version == _version)
                    continue;
                if (!settlements.Exists(kv.Key))
                    _remove.Add(kv.Key);
                else
                    kv.Value.Box.style.display = DisplayStyle.None;
            }
            foreach (int id in _remove)
            {
                _tags[id].Box.RemoveFromHierarchy();
                _tags.Remove(id);
            }
        }

        Tag CreateTag()
        {
            var box = new VisualElement { pickingMode = PickingMode.Ignore };
            var s = box.style;
            s.position = Position.Absolute;
            s.flexDirection = FlexDirection.Row;
            s.alignItems = Align.Center;
            s.translate = new Translate(new Length(-50f, LengthUnit.Percent), new Length(-100f, LengthUnit.Percent));
            s.backgroundColor = UiTheme.Bg0;
            UiTheme.SetRadius(s, UiTheme.Radius);
            UiTheme.SetBorder(s, 1f, UiTheme.Line);
            s.paddingLeft = UiTheme.S1; s.paddingRight = UiTheme.S2; s.paddingTop = 2f; s.paddingBottom = 2f;

            var swatch = new VisualElement { pickingMode = PickingMode.Ignore };
            swatch.style.width = 4f;
            swatch.style.height = 14f;
            swatch.style.marginRight = UiTheme.S1 + 2f;
            box.Add(swatch);

            var text = UiTheme.Text("", UiTheme.FontSmall, UiTheme.TextHi);
            text.style.whiteSpace = WhiteSpace.NoWrap;
            box.Add(text);
            _layer.Add(box);
            return new Tag { Box = box, Text = text, Swatch = swatch };
        }
    }
}
