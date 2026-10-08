using System.Collections.Generic;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// The dream look of the HUD windows, drawn with IMGUI: ink glass panels with a mint-to-violet seam along
    /// the top edge (the floor seams are the benchmark), rounded fills, glows, orbs, icons from Resources/Art/UI
    /// and the text styles. Every helper works in the HUD's virtual 720-pixel-high canvas.
    /// </summary>
    public static class DreamSkin
    {
        public static readonly Color Mint = new Color32(0x9F, 0xE3, 0xC8, 255);
        public static readonly Color Violet = new Color32(0xB9, 0x9C, 0xFF, 255);
        public static readonly Color Bone = new Color32(0xE8, 0xDC, 0xC0, 255);
        public static readonly Color BoneDim = new Color32(0xB6, 0xAB, 0x95, 255);
        public static readonly Color Ink = new Color32(0x15, 0x11, 0x1D, 255);
        public static readonly Color Plum = new Color32(0x3A, 0x2A, 0x4D, 255);
        public static readonly Color Warn = new Color32(0xE0, 0x47, 0x5B, 255);
        public const string MintHex = "#9FE3C8", VioletHex = "#B99CFF", DimHex = "#B6AB95", BoneHex = "#E8DCC0";

        public static Texture2D Glow, Disc, Fade, PanelFill, CardFill, MintFill, Flame;
        public static Font Display;
        public static GUIStyle Title, Heading, Body, Small, Tiny, Value, Dark, Eerie, EerieSub;
        static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
        static bool ready;

        public static Texture2D Icon(string name)
        {
            if (!icons.TryGetValue(name, out var tex))
            {
                tex = Resources.Load<Texture2D>("Art/UI/" + name);
                icons[name] = tex;
            }
            return tex;
        }

        public static void Ensure()
        {
            if (ready || GUI.skin == null) return;
            Glow = Radial(64, r => Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f));
            Disc = Radial(128, r => Mathf.Clamp01((1f - r) * 64f));
            Fade = Gradient(4, 64, y => new Color(1, 1, 1, y));
            PanelFill = Gradient(4, 64, y => Color.Lerp(new Color32(21, 17, 30, 246), new Color32(38, 29, 54, 242), y));
            CardFill = Gradient(4, 64, y => Color.Lerp(new Color32(24, 19, 34, 225), new Color32(64, 46, 84, 200), y));
            MintFill = Gradient(64, 4, x => Color.Lerp(Mint, new Color32(0xCF, 0xF5, 0xE5, 255), x), horizontal: true);
            Flame = MakeFlame(48, 80);
            Display = Resources.Load<Font>("Fonts/IMFellEnglishSC-Regular");

            var label = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0) };
            label.normal.textColor = Bone;
            Body = new GUIStyle(label) { fontSize = 16 };
            Small = new GUIStyle(label) { fontSize = 14 };
            Small.normal.textColor = BoneDim;
            Tiny = new GUIStyle(label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            Value = new GUIStyle(label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, wordWrap = false };
            Dark = new GUIStyle(label) { fontSize = 19, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            Dark.normal.textColor = Ink;
            Heading = new GUIStyle(label) { fontSize = Display != null ? 26 : 22, font = Display, fontStyle = Display != null ? FontStyle.Normal : FontStyle.Bold, wordWrap = false };
            Title = new GUIStyle(Heading) { fontSize = Display != null ? 30 : 24 };
            Title.normal.textColor = Mint;
            Eerie = new GUIStyle(label) { fontSize = Display != null ? 46 : 38, font = Display, fontStyle = Display != null ? FontStyle.Normal : FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter, wordWrap = false };
            EerieSub = new GUIStyle(label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            EerieSub.normal.textColor = Violet;
            ready = true;
        }

        // ---------------------------------------------------------------- textures

        static Texture2D Radial(int size, System.Func<float, float> alpha)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            float h = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float r = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(h, h)) / h;
                    t.SetPixel(x, y, new Color(1, 1, 1, alpha(r)));
                }
            t.Apply();
            return t;
        }

        static Texture2D Gradient(int w, int h, System.Func<float, Color> at, bool horizontal = false)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    t.SetPixel(x, y, at(horizontal ? x / (w - 1f) : y / (h - 1f)));
            t.Apply();
            return t;
        }

        /// <summary>A soft tongue of fire: widest and brightest near the bottom, tapering to a point.</summary>
        static Texture2D MakeFlame(int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float v = (y + .5f) / h, u = (x + .5f) / w * 2f - 1f;
                    float width = Mathf.Sin(Mathf.Clamp01(v * 1.15f + .08f) * Mathf.PI) * (1f - v * .55f);
                    float a = Mathf.Clamp01(1f - Mathf.Abs(u) / Mathf.Max(.001f, width));
                    a = Mathf.Pow(a, 1.6f) * Mathf.Clamp01((1f - v) * 1.4f);
                    t.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            t.Apply();
            return t;
        }

        // ---------------------------------------------------------------- drawing

        static bool Painting => Event.current != null && Event.current.type == EventType.Repaint;

        public static void Fill(Rect r, Color c, float radius, Texture tex = null)
        {
            if (!Painting) return;
            GUI.DrawTexture(r, tex ?? Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, c, Vector4.zero, Vector4.one * radius);
        }

        public static void Border(Rect r, Color c, float width, float radius)
        {
            if (!Painting) return;
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, c, Vector4.one * width, Vector4.one * radius);
        }

        public static void Tex(Rect r, Texture tex, Color c)
        {
            if (!Painting || tex == null) return;
            var old = GUI.color;
            GUI.color = c * old;
            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, true);
            GUI.color = old;
        }

        public static void GlowAt(Vector2 center, Vector2 size, Color c) => Tex(new Rect(center - size / 2f, size), Glow, c);

        public static void Icon(Rect r, string name, Color c) => Tex(r, Icon(name), c);

        /// <summary>A window: ink glass, a faint mint rim and the breathing seam along its top edge.</summary>
        public static void Panel(Rect r, float alpha = 1f)
        {
            var a = new Color(1, 1, 1, alpha);
            GlowAt(new Vector2(r.center.x, r.yMax - 8), new Vector2(r.width * 1.1f, 60), new Color(0, 0, 0, .55f * alpha));
            Fill(r, a, 18, PanelFill);
            Border(r, new Color(Mint.r, Mint.g, Mint.b, .2f * alpha), 1.2f, 18);
            Seam(new Rect(r.x + r.width * .06f, r.y - 1, r.width * .88f, 3), alpha);
        }

        /// <summary>The mint-to-violet thread with a slow breath, like the spectral floor seams.</summary>
        public static void Seam(Rect r, float alpha = 1f)
        {
            float breath = .7f + .3f * Mathf.Sin(Time.unscaledTime * 2f);
            GlowAt(new Vector2(r.center.x, r.center.y), new Vector2(r.width * .9f, 26), new Color(Mint.r, Mint.g, Mint.b, .18f * breath * alpha));
            int n = 24;
            for (int i = 0; i < n; i++)
            {
                float u0 = i / (float)n, u1 = (i + 1) / (float)n, mid = (u0 + u1) / 2f;
                float edge = Mathf.Sin(mid * Mathf.PI);
                var c = Color.Lerp(Mint, Violet, mid);
                c.a = edge * edge * breath * alpha;
                Fill(new Rect(r.x + r.width * u0, r.y, r.width * (u1 - u0) + .5f, r.height), c, 0);
            }
        }

        /// <summary>A round button face: dark glass with a lit top and a rim.</summary>
        public static void Orb(Vector2 c, float d, Color rim, float rimAlpha, float glow)
        {
            if (glow > 0) GlowAt(c, Vector2.one * d * 1.9f, new Color(rim.r, rim.g, rim.b, glow));
            GlowAt(c + Vector2.up * d * .12f, Vector2.one * d * 1.25f, new Color(0, 0, 0, .55f));
            Tex(new Rect(c.x - d / 2, c.y - d / 2, d, d), Disc, new Color(.075f, .058f, .105f, .97f));
            Tex(new Rect(c.x - d * .42f, c.y - d * .48f, d * .84f, d * .6f), Glow, new Color(Plum.r, Plum.g, Plum.b, .9f));
            Ring(c, d, Mathf.Max(1.5f, d * .03f), new Color(rim.r, rim.g, rim.b, rimAlpha));
        }

        public static void Ring(Vector2 c, float d, float width, Color col)
        {
            Border(new Rect(c.x - d / 2, c.y - d / 2, d, d), col, width, d / 2);
        }

        public static void Label(Rect r, string text, GUIStyle style, Color color, TextAnchor? anchor = null)
        {
            var oldC = style.normal.textColor;
            var oldA = style.alignment;
            style.normal.textColor = color;
            if (anchor.HasValue) style.alignment = anchor.Value;
            GUI.Label(r, text, style);
            style.normal.textColor = oldC;
            style.alignment = oldA;
        }

        /// <summary>Text with a dark drop so it reads over the floor.</summary>
        public static void Shadowed(Rect r, string text, GUIStyle style, Color color, TextAnchor? anchor = null)
        {
            Label(new Rect(r.x + 1, r.y + 1.5f, r.width, r.height), text, style, new Color(0, 0, 0, .85f * color.a), anchor);
            Label(r, text, style, color, anchor);
        }

        /// <summary>The HUD's currency tag for a cost: mint Dream Power, violet Faith.</summary>
        public static string Res(string resource) => resource == "faith" ? "<color=" + VioletHex + ">Faith</color>" : "<color=" + MintHex + ">DP</color>";
        public static string ResIcon(string resource) => resource == "faith" ? "faith" : "dream";
        public static Color ResColor(string resource) => resource == "faith" ? Violet : Mint;

        public static string Clock(float seconds)
        {
            int s = Mathf.Max(1, Mathf.CeilToInt(seconds));
            return s >= 60 ? s / 60 + ":" + (s % 60).ToString("00") : s + "s";
        }
    }
}
