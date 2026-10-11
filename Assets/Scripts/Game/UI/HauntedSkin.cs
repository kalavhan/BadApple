using System;
using System.Collections.Generic;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// Baked wood, brass and paper kit for the hotel's IMGUI windows. All rectangles and
    /// pivots are in the HUD's 720-high canvas. Draw helpers are inert outside Repaint;
    /// textures, font styles and nine-slice styles are cached after first use.
    /// </summary>
    public static class HauntedSkin
    {
        public static readonly Color BrassHi = new Color32(0xF0, 0xDC, 0x98, 255);
        public static readonly Color Brass = new Color32(0xB8, 0x92, 0x3F, 255);
        public static readonly Color BrassMid = new Color32(0x8E, 0x6A, 0x26, 255);
        public static readonly Color BrassLow = new Color32(0x4A, 0x34, 0x10, 255);
        public static readonly Color Verdigris = new Color32(0x50, 0x82, 0x6E, 77);
        public static readonly Color WoodTop = new Color32(0x2E, 0x14, 0x20, 255);
        public static readonly Color WoodMid = new Color32(0x1C, 0x0A, 0x12, 255);
        public static readonly Color WoodBottom = new Color32(0x12, 0x06, 0x0B, 255);
        public static readonly Color Plum = new Color32(0x1A, 0x0C, 0x1C, 255);
        public static readonly Color DamaskInk = new Color32(0x3A, 0x18, 0x30, 179);
        public static readonly Color CubbyIn = new Color32(0x24, 0x12, 0x1F, 255);
        public static readonly Color CubbyOut = new Color32(0x0B, 0x04, 0x09, 255);
        public static readonly Color Paper = new Color32(0xE6, 0xD9, 0xBB, 255);
        public static readonly Color PaperDark = new Color32(0xCF, 0xBC, 0x95, 255);
        public static readonly Color TagPaper = new Color32(0xEA, 0xDF, 0xC4, 255);
        public static readonly Color TagPaperDark = new Color32(0xCB, 0xB9, 0x91, 255);
        public static readonly Color Ink = new Color32(0x23, 0x15, 0x10, 255);
        public static readonly Color BloodRed = new Color32(0x8E, 0x1B, 0x22, 255);
        public static readonly Color StampRed = new Color32(0xE0, 0x50, 0x5A, 255);
        public static readonly Color Ivory = new Color32(0xEF, 0xE6, 0xD2, 255);
        public static readonly Color Bone = new Color32(0xE8, 0xDC, 0xC0, 255);
        public static readonly Color Mint = new Color32(0x7E, 0xF5, 0xD2, 255);
        public static readonly Color MintHi = new Color32(0xE6, 0xFF, 0xF7, 255);
        public static readonly Color MintDeep = new Color32(0x33, 0xA0, 0x88, 255);
        public static readonly Color GooTop = new Color32(0xB9, 0xFF, 0xE9, 255);
        public static readonly Color GooBottom = new Color32(0x5F, 0xD6, 0xB6, 255);
        public static readonly Color Violet = new Color32(0xA9, 0x8B, 0xFF, 255);
        public static readonly Color CostInk = new Color32(0x1B, 0x5E, 0x4E, 255);
        public static readonly Color FaithInk = new Color32(0x5B, 0x3F, 0xA8, 255);
        public static readonly Color LampAmber = new Color32(0xFF, 0xD2, 0x7A, 255);
        public static readonly Color LampHi = new Color32(0xFF, 0xF6, 0xD6, 255);
        public static readonly Color LampEdge = new Color32(0xC2, 0x7A, 0x1F, 255);
        public static readonly Color EyeYellow = new Color32(0xFF, 0xE3, 0x6B, 255);
        public static readonly Color EyeGlow = new Color32(0xFF, 0xB5, 0x2B, 255);
        public static readonly Color ButtonInk = new Color32(0x05, 0x23, 0x1C, 255);
        public static readonly Color PhaseInk = new Color32(0x5A, 0x10, 0x16, 255);
        public static readonly Color PlaqueInk = new Color32(0x22, 0x14, 0x0A, 255);

        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        static readonly Dictionary<int, GUIStyle> textStyles = new Dictionary<int, GUIStyle>();
        static readonly Dictionary<SliceKey, GUIStyle> slices = new Dictionary<SliceKey, GUIStyle>();
        static readonly RectOffset PlaqueBorder = new RectOffset(18, 18, 18, 18);
        static readonly RectOffset PaperBorder = new RectOffset(24, 24, 24, 24);
        static Font gothic, gothicHeavy, typed, serif, serifItalic, smallCaps;
        static bool ready;
        public static bool Painting => Event.current != null && Event.current.type == EventType.Repaint;

        public static Texture2D Texture(string name)
        {
            if (!textures.TryGetValue(name, out var tex))
            {
                tex = Resources.Load<Texture2D>("Art/UI/Haunted/" + name);
                textures.Add(name, tex);
            }
            return tex;
        }

        public static Texture2D Icon(string name) => DreamSkin.Icon(name);

        public static void Ensure()
        {
            if (ready) return;
            gothic = Resources.Load<Font>("Fonts/GrenzeGotisch-Bold");
            gothicHeavy = Resources.Load<Font>("Fonts/GrenzeGotisch-ExtraBold");
            typed = Resources.Load<Font>("Fonts/SpecialElite-Regular");
            serif = Resources.Load<Font>("Fonts/CormorantGaramond-SemiBold");
            serifItalic = Resources.Load<Font>("Fonts/CormorantGaramond-SemiBoldItalic");
            smallCaps = Resources.Load<Font>("Fonts/CormorantSC-Bold");
            ready = true;
        }

        static GUIStyle Style(int family, int size)
        {
            Ensure();
            // Nine-pixel NIGHT lettering is the sole art-label exception, requested by the crest.
            size = Mathf.Max(9, size);
            int key = family * 1024 + size;
            if (textStyles.TryGetValue(key, out var style)) return style;
            Font font = family == 0 ? gothic : family == 1 ? gothicHeavy : family == 2 ? typed :
                family == 3 ? serif : family == 4 ? serifItalic : smallCaps;
            style = new GUIStyle
            {
                font = font,
                fontSize = size,
                fontStyle = FontStyle.Normal,
                richText = false,
                wordWrap = true,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(), margin = new RectOffset(),
                clipping = TextClipping.Clip
            };
            style.normal.textColor = Ivory;
            textStyles.Add(key, style);
            return style;
        }

        public static GUIStyle Gothic(int size, bool extraBold = false) => Style(extraBold ? 1 : 0, size);
        public static GUIStyle Typed(int size) => Style(2, size);
        public static GUIStyle Serif(int size, bool italic = false) => Style(italic ? 4 : 3, size);
        public static GUIStyle SmallCaps(int size) => Style(5, size);

        static readonly GUIContent measuredText = new GUIContent();
        static readonly Dictionary<WrapKey, string[]> wrappedText = new Dictionary<WrapKey, string[]>();
        static readonly List<string> wrapBuffer = new List<string>(4);

        public static Vector2 Measure(string text, GUIStyle style)
        {
            measuredText.text = text ?? "";
            return style.CalcSize(measuredText);
        }

        /// <summary>Font ascent as a fraction of em, measured from the bundled static TTFs.</summary>
        public static float Ascender(GUIStyle style)
        {
            Ensure();
            return style.font == gothic || style.font == gothicHeavy ? 1.100f : style.font == typed ? .703f : .924f;
        }

        /// <summary>Draw a single line on a shared baseline and return its advance width.</summary>
        public static float LabelOnBaseline(float x, float baseline, string text, GUIStyle style, Color color)
        {
            Vector2 size = Measure(text, style);
            bool wrap = style.wordWrap;
            try
            {
                style.wordWrap = false;
                Label(new Rect(x, baseline - Ascender(style) * style.fontSize, size.x + 2, size.y), text, style, color, TextAnchor.UpperLeft);
            }
            finally { style.wordWrap = wrap; }
            return size.x;
        }

        readonly struct WrapKey : IEquatable<WrapKey>
        {
            readonly string text;
            readonly int font, size;
            readonly FontStyle style;
            readonly float width;
            public WrapKey(string value, GUIStyle s, float w) { text = value; font = s.font == null ? 0 : s.font.GetInstanceID(); size = s.fontSize; style = s.fontStyle; width = w; }
            public bool Equals(WrapKey other) => text == other.text && font == other.font && size == other.size && style == other.style && width.Equals(other.width);
            public override bool Equals(object value) => value is WrapKey other && Equals(other);
            public override int GetHashCode() { unchecked { return (((text.GetHashCode() * 397 ^ font) * 397 ^ size) * 397 ^ (int)style) * 397 ^ width.GetHashCode(); } }
        }

        /// <summary>Word wrapping cached by content, font and width. Reuses the caller's line buffer.</summary>
        public static void WrapLines(string text, GUIStyle style, float maxWidth, List<string> lines)
        {
            lines.Clear();
            if (string.IsNullOrEmpty(text)) return;
            var key = new WrapKey(text, style, maxWidth);
            if (!wrappedText.TryGetValue(key, out var cached))
            {
                wrapBuffer.Clear();
                foreach (string paragraph in text.Split('\n'))
                {
                    string line = "";
                    foreach (string word in paragraph.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string candidate = line.Length == 0 ? word : line + " " + word;
                        if (line.Length > 0 && Measure(candidate, style).x > maxWidth) { wrapBuffer.Add(line); line = word; }
                        else line = candidate;
                    }
                    wrapBuffer.Add(line);
                }
                cached = wrapBuffer.ToArray();
                if (wrappedText.Count >= 256) wrappedText.Clear();
                wrappedText.Add(key, cached);
            }
            lines.AddRange(cached);
        }

        readonly struct SliceKey : IEquatable<SliceKey>
        {
            readonly int texture, left, right, top, bottom;
            public SliceKey(Texture2D tex, RectOffset border)
            {
                texture = tex.GetInstanceID(); left = border.left; right = border.right;
                top = border.top; bottom = border.bottom;
            }
            public bool Equals(SliceKey other) => texture == other.texture && left == other.left && right == other.right && top == other.top && bottom == other.bottom;
            public override bool Equals(object obj) => obj is SliceKey other && Equals(other);
            public override int GetHashCode()
            {
                unchecked { return ((((texture * 397 ^ left) * 397 ^ right) * 397 ^ top) * 397) ^ bottom; }
            }
        }

        public static void NineSlice(Rect r, Texture2D tex, RectOffset border, Color tint)
        {
            if (!Painting || tex == null || r.width <= 0 || r.height <= 0) return;
            var key = new SliceKey(tex, border);
            if (!slices.TryGetValue(key, out var style))
            {
                style = new GUIStyle { border = new RectOffset(border.left, border.right, border.top, border.bottom) };
                style.normal.background = tex;
                slices.Add(key, style);
            }
            Color old = GUI.color;
            try { GUI.color = old * tint; style.Draw(r, GUIContent.none, false, false, false, false); }
            finally { GUI.color = old; }
        }

        public static void Tiled(Rect r, Texture2D tex, Vector2 tileSize, Vector2 offset)
        {
            if (!Painting || tex == null || tileSize.x <= 0 || tileSize.y <= 0 || r.width <= 0 || r.height <= 0) return;
            GUI.DrawTextureWithTexCoords(r, tex, new Rect(offset.x / tileSize.x, offset.y / tileSize.y, r.width / tileSize.x, r.height / tileSize.y), true);
        }

        /// <summary>Compose in canvas coordinates; the existing matrix applies screen scale exactly once.</summary>
        public static void Rotated(float degrees, Vector2 pivot, Action draw)
        {
            if (!Painting || draw == null) return;
            Matrix4x4 old = GUI.matrix;
            try
            {
                GUI.matrix = old * Matrix4x4.Translate(new Vector3(pivot.x, pivot.y, 0)) *
                    Matrix4x4.Rotate(Quaternion.Euler(0, 0, degrees)) * Matrix4x4.Translate(new Vector3(-pivot.x, -pivot.y, 0));
                draw();
            }
            finally { GUI.matrix = old; }
        }

        public static void Fill(Rect r, Color color, float radius = 0)
        {
            if (!Painting) return;
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color, Vector4.zero, Vector4.one * radius);
        }

        public static void Border(Rect r, Color color, float width, float radius = 0)
        {
            if (!Painting) return;
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color, Vector4.one * width, Vector4.one * radius);
        }

        public static void Tex(Rect r, UnityEngine.Texture tex, Color tint)
        {
            if (!Painting || tex == null) return;
            Color old = GUI.color;
            try { GUI.color = old * tint; GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, true); }
            finally { GUI.color = old; }
        }

        public static void TexFlipped(Rect r, UnityEngine.Texture tex) => TexFlipped(r, tex, Color.white);

        public static void TexFlipped(Rect r, UnityEngine.Texture tex, Color tint)
        {
            if (!Painting || tex == null) return;
            Color old = GUI.color;
            try { GUI.color = old * tint; GUI.DrawTextureWithTexCoords(r, tex, new Rect(1, 0, -1, 1), true); }
            finally { GUI.color = old; }
        }

        public static void GlowAt(Vector2 center, Vector2 size, Color color) => Tex(new Rect(center - size * .5f, size), Texture("drop_glow"), color);

        public static void Label(Rect r, string text, GUIStyle style, Color color, TextAnchor? anchor = null)
        {
            if (!Painting || string.IsNullOrEmpty(text)) return;
            Color oldColor = style.normal.textColor;
            TextAnchor oldAnchor = style.alignment;
            try
            {
                style.normal.textColor = color;
                if (anchor.HasValue) style.alignment = anchor.Value;
                GUI.Label(r, text, style);
            }
            finally { style.normal.textColor = oldColor; style.alignment = oldAnchor; }
        }

        public static void WoodFrame(Rect r)
        {
            if (!Painting) return;
            GlowAt(r.center + Vector2.up * 7, new Vector2(r.width * 1.1f, r.height * 1.15f), new Color(0, 0, 0, .8f));
            Fill(r, WoodBottom, 6);
            Tiled(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), Texture("wood_v"), new Vector2(128, 128), Vector2.zero);
            BrassRim(new Rect(r.x + 7, r.y + 7, r.width - 14, r.height - 14));
        }

        public static void BrassRim(Rect r, float width = 1.5f) => Border(r, Brass, width, 2);
        public static void Gate(Rect r) => Tiled(r, Texture("gate_tile"), new Vector2(r.width, 44), Vector2.zero);

        public static void Lintel(Rect r)
        {
            if (!Painting) return;
            GlowAt(new Vector2(r.center.x, r.yMax + 5), new Vector2(r.width, 30), new Color(0, 0, 0, .8f));
            Tiled(r, Texture("wood_h"), new Vector2(128, 128), Vector2.zero);
            Fill(new Rect(r.x, r.yMax - 4, r.width, 1), BrassHi);
            Fill(new Rect(r.x, r.yMax - 3, r.width, 2), Brass);
            Fill(new Rect(r.x, r.yMax - 1, r.width, 1), BrassLow);
        }

        public static void Plaque(Rect r, string title, string sub, float degrees)
        {
            if (!Painting) return;
            Tex(new Rect(r.center.x - 50, r.y - 8, 100, 16), Texture("wire"), Color.white);
            Tex(new Rect(r.center.x - 4, r.y - 9, 8, 8), Texture("nail"), Color.white);
            Rotated(degrees, new Vector2(r.center.x, r.y), () =>
            {
                GlowAt(r.center + Vector2.up * 5, r.size * 1.15f, new Color(0, 0, 0, .8f));
                NineSlice(r, Texture("plaque_9"), PlaqueBorder, Color.white);
                Label(new Rect(r.x + 10, r.y - 1, r.width - 20, 30), title, Gothic(28, true), PlaqueInk);
                Label(new Rect(r.x + 10, r.y + 27, r.width - 20, 13), sub, SmallCaps(11), BrassLow);
            });
        }

        public static void ToeTag(Rect r, string name, string cost, Color costColor, float progress = -1)
        {
            if (!Painting) return;
            GlowAt(r.center + Vector2.up * 3, r.size * 1.1f, new Color(0, 0, 0, .6f));
            Tex(r, Texture("tag"), Color.white);
            float indent = r.width * .23f;
            Label(new Rect(r.x + indent, r.y + 4, r.width - indent - 5, 24), name, Typed(11), Ink, TextAnchor.UpperLeft);
            Label(new Rect(r.x + indent, r.yMax - 22, r.width - indent - 5, 20), cost, Typed(13), costColor, TextAnchor.MiddleLeft);
            if (progress >= 0) Fill(new Rect(r.x + indent, r.yMax - 3, (r.width - indent - 5) * Mathf.Clamp01(progress), 1), CostInk);
        }

        public static void RegisterCard(Rect r)
        {
            if (!Painting) return;
            GlowAt(r.center + Vector2.up * 3, r.size * 1.1f, new Color(0, 0, 0, .6f));
            NineSlice(r, Texture("paper_9"), PaperBorder, Color.white);
        }

        public static void CallButton(Vector2 center, float diameter, UnityEngine.Texture icon, bool lit)
        {
            if (!Painting) return;
            if (lit) GlowAt(center, Vector2.one * diameter * 1.6f, new Color(LampAmber.r, LampAmber.g, LampAmber.b, .45f));
            Tex(new Rect(center - Vector2.one * diameter * .5f, Vector2.one * diameter), Texture(lit ? "call_button_lit" : "call_button"), Color.white);
            Tex(new Rect(center - Vector2.one * diameter * .23f, Vector2.one * diameter * .46f), icon, lit ? BrassLow : Ivory);
        }

        public static void BrassKnobClose(Rect r)
        {
            if (!Painting) return;
            Tex(new Rect(r.center.x - 60, r.center.y - 23, 120, 46), Texture("bat_wings_close"), Color.white);
            GlowAt(r.center + Vector2.up * 4, r.size * 1.3f, new Color(0, 0, 0, .75f));
            Tex(r, Texture("knob"), Color.white);
            Tex(new Rect(r.center.x - 9, r.center.y - 9, 18, 18), Icon("close"), Bone);
        }
    }
}
