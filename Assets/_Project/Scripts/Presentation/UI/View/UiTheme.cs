using UnityEngine;
using UnityEngine.UIElements;
using WorldBeyond.Simulation.Civilizations;

namespace WorldBeyond.Presentation.UI
{
    // Design-Tokens aus Docs/UI_STYLE_GUIDE.md Teil B (§3 Farben, §4 Abstände, §2 Schriftgrößen).
    // Im Code statt in Tokens.uss, damit die Oberfläche ohne importierte UI-Assets und ohne Theme Style Sheet läuft
    // (das Projekt hat noch keine geprüften Unity-Assets). Schriften: Unity-Standardschrift, bis Barlow/IBM Plex importiert sind.
    public static class UiTheme
    {
        public static readonly Color Bg0 = Hex(0x12161B, 0.92f);
        public static readonly Color Bg1 = Hex(0x1B2128);
        public static readonly Color Bg1Hover = Hex(0x252D36);
        public static readonly Color Line = Hex(0x2E3742);
        public static readonly Color TextHi = Hex(0xE8E4D8);
        public static readonly Color TextLo = Hex(0x8E959C);
        public static readonly Color Accent = Hex(0xC9A24A);
        public static readonly Color Ok = Hex(0x6FA86A);
        public static readonly Color Warn = Hex(0xD99A3D);
        public static readonly Color Danger = Hex(0xC8553D);
        public static readonly Color Info = Hex(0x5C8FB8);

        public const float S1 = 4f, S2 = 8f, S3 = 12f, S4 = 16f, S6 = 24f;
        public const float Radius = 2f;
        public const float ButtonHeight = 32f;
        public const float RowHeight = 28f;
        public const int FontTitle = 20, FontHeading = 16, FontBody = 14, FontSmall = 12, FontNumber = 13;

        public static Color Hex(int rgb, float alpha = 1f) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);

        public static Color Civ(CivColor c) => new Color32(c.R, c.G, c.B, 255);

        public static Color ForFeed(FeedKind kind)
        {
            switch (kind)
            {
                case FeedKind.War: return Danger;
                case FeedKind.Conquest: return Warn;
                case FeedKind.Collapse: return Danger;
                case FeedKind.Peace: return Ok;
                case FeedKind.Founding: return Info;
                case FeedKind.Progress: return Accent;
                default: return TextLo;
            }
        }

        // ---------- Bausteine ----------

        public static VisualElement Panel(string name)
        {
            var p = new VisualElement { name = name };
            var s = p.style;
            s.backgroundColor = Bg0;
            SetBorder(s, 1f, Line);
            SetRadius(s, Radius);
            SetPadding(s, S3);
            return p;
        }

        public static Label Heading(string text)
        {
            var l = Text(text, FontHeading, TextHi);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.letterSpacing = 1f;
            l.style.marginBottom = S2;
            return l;
        }

        public static Label Text(string text, int size, Color color)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.style.fontSize = size;
            l.style.color = color;
            l.style.whiteSpace = WhiteSpace.Normal;
            SetMargin(l.style, 0f);
            SetPadding(l.style, 0f);
            return l;
        }

        /// <summary>
        /// Eigener Knopf aus Label + Clickable statt UnityEngine.UIElements.Button, damit Aussehen und Zustände
        /// nicht vom (fehlenden) Theme abhängen. Hover hellt auf, aktiv zeigt eine Messinglinie unten.
        /// </summary>
        public sealed class UiButton : Label
        {
            bool _active, _hover, _enabled = true;
            readonly Color _baseBg, _border;

            public UiButton(string text, System.Action onClick, string tooltipText = null, bool danger = false)
            {
                this.text = text;
                tooltip = tooltipText;
                _baseBg = Bg1;
                _border = danger ? Danger : Line;
                var s = style;
                s.fontSize = FontBody;
                s.color = TextHi;
                s.height = ButtonHeight;
                s.minWidth = 40f;
                s.unityTextAlign = TextAnchor.MiddleCenter;
                SetPadding(s, 0f);
                s.paddingLeft = S3;
                s.paddingRight = S3;
                SetMargin(s, 0f);
                SetRadius(s, Radius);
                SetBorder(s, 1f, _border);
                this.AddManipulator(new Clickable(() => { if (_enabled) onClick?.Invoke(); }));
                RegisterCallback<PointerEnterEvent>(_ => { _hover = true; Apply(); });
                RegisterCallback<PointerLeaveEvent>(_ => { _hover = false; Apply(); });
                Apply();
            }

            public bool Active { get => _active; set { _active = value; Apply(); } }
            public bool Enabled { get => _enabled; set { _enabled = value; Apply(); } }

            void Apply()
            {
                var s = style;
                s.backgroundColor = _enabled && _hover ? Bg1Hover : _baseBg;
                s.color = _enabled ? TextHi : TextLo;
                s.borderBottomColor = _active ? Accent : _border;
                s.borderBottomWidth = _active ? 2f : 1f;
            }
        }

        public static void SetPadding(IStyle s, float v) { s.paddingLeft = v; s.paddingRight = v; s.paddingTop = v; s.paddingBottom = v; }
        public static void SetMargin(IStyle s, float v) { s.marginLeft = v; s.marginRight = v; s.marginTop = v; s.marginBottom = v; }
        public static void SetRadius(IStyle s, float v) { s.borderTopLeftRadius = v; s.borderTopRightRadius = v; s.borderBottomLeftRadius = v; s.borderBottomRightRadius = v; }

        public static void SetBorder(IStyle s, float width, Color color)
        {
            s.borderLeftWidth = width; s.borderRightWidth = width; s.borderTopWidth = width; s.borderBottomWidth = width;
            s.borderLeftColor = color; s.borderRightColor = color; s.borderTopColor = color; s.borderBottomColor = color;
        }
    }
}
