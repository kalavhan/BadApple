using System.Collections.Generic;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// Screen-space effects for the HUD windows: mint flames, sparks and spreading seam rings, plus the eerie
    /// rising marks (LEVEL UP, EVOLVED...). Particles live in the HUD's virtual canvas and are drawn additively
    /// with GL in one batch per texture, so a burning orb costs a couple of draw calls.
    /// </summary>
    public sealed class UiFx
    {
        public enum Kind { Spark, Flame, Glow }

        struct Particle
        {
            public Kind Kind;
            public Vector2 Pos, Vel;
            public float Age, Life, Size, Rise, Drag, Seed;
            public Color Color, Late;
        }

        struct RingFx { public Vector2 Pos; public float Age, Life, Radius, Squash; public Color Color; }

        struct Mark { public Vector2 Pos; public string Title, Sub; public float Born; public bool Warn; }

        readonly List<Particle> parts = new List<Particle>(512);
        readonly List<RingFx> rings = new List<RingFx>();
        readonly List<Mark> marks = new List<Mark>();
        Material material;
        float lastStep = -1f;
        const int MaxParticles = 700;

        public int Count => parts.Count;

        public void Clear() { parts.Clear(); rings.Clear(); marks.Clear(); }

        public void Add(Kind kind, Vector2 pos, Vector2 vel, float size, float life, Color color, Color? late = null, float rise = 0, float drag = 0)
        {
            if (parts.Count >= MaxParticles) return;
            parts.Add(new Particle { Kind = kind, Pos = pos, Vel = vel, Size = size, Life = life, Color = color, Late = late ?? color,
                Rise = rise, Drag = drag, Seed = Random.value * 10f });
        }

        /// <summary>One tongue of fire licking upward and slightly outward from a point.</summary>
        public void Flame(Vector2 pos, Vector2 outward, float scale, Color color, Color late)
        {
            var vel = new Vector2(outward.x * 22f + Random.Range(-10f, 10f), -Random.Range(45f, 85f) + outward.y * 18f) * scale;
            Add(Kind.Flame, pos, vel, Random.Range(10f, 17f) * scale, Random.Range(.4f, .7f), color, late, rise: -40f * scale, drag: 1.2f);
        }

        public void Burst(Vector2 pos, int count, Color color, float speed, float size, float life, float upward = .6f)
        {
            for (int i = 0; i < count; i++)
            {
                var dir = Random.insideUnitCircle.normalized;
                dir.y = -Mathf.Abs(dir.y) * upward + dir.y * (1f - upward);
                Add(Kind.Spark, pos, dir * speed * Random.Range(.4f, 1f), size * Random.Range(.6f, 1.2f), life * Random.Range(.6f, 1.1f),
                    color, Color.Lerp(color, DreamSkin.Violet, .6f), rise: -30f, drag: 2.2f);
            }
        }

        public void Ring(Vector2 pos, float radius, Color color, float life = .55f, float squash = .55f) =>
            rings.Add(new RingFx { Pos = pos, Radius = radius, Color = color, Life = life, Squash = squash });

        /// <summary>A rising, flickering mark over a point: LEVEL UP, EVOLVED, BANISHED, NOT YET.</summary>
        public void Say(Vector2 pos, string title, string sub, bool warn = false) =>
            marks.Add(new Mark { Pos = new Vector2(pos.x, Mathf.Max(pos.y, 150f)), Title = title, Sub = sub, Born = Time.unscaledTime, Warn = warn });

        void Step()
        {
            float now = Time.unscaledTime, dt = lastStep < 0 ? 0 : Mathf.Min(.05f, now - lastStep);
            lastStep = now;
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                var p = parts[i];
                p.Age += dt;
                if (p.Age >= p.Life) { parts.RemoveAt(i); continue; }
                p.Vel *= Mathf.Exp(-p.Drag * dt);
                p.Vel.y += p.Rise * dt;
                p.Pos += p.Vel * dt;
                if (p.Kind == Kind.Flame) p.Pos.x += Mathf.Sin(now * 9f + p.Seed) * 18f * dt;
                parts[i] = p;
            }
            for (int i = rings.Count - 1; i >= 0; i--)
            {
                var r = rings[i]; r.Age += dt;
                if (r.Age >= r.Life) rings.RemoveAt(i); else rings[i] = r;
            }
            marks.RemoveAll(m => now - m.Born > 1.9f);
        }

        Material Mat() => material != null ? material : (material = Shared());

        static Material shared;
        static Material Shared()
        {
            if (shared == null)
            {
                var shader = Shader.Find("Hidden/BadAppleHotel/UiAdditive");
                if (shader == null) return null;
                shared = new Material(shader) { hideFlags = HideFlags.DontSave };
            }
            return shared;
        }

        /// <summary>
        /// An ellipse in the HUD canvas, drawn right away (so it layers with the IMGUI calls around it): a soft
        /// fill, a solid line, or a dashed line whose dashes turn with phase (in turns).
        /// </summary>
        public static void Ellipse(Vector2 center, float rx, float ry, Color color, float width, float phase, float scale, bool fill = false, int dashes = 36)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint || rx <= 0) return;
            var mat = Shared();
            if (mat == null) return;
            mat.mainTexture = Texture2D.whiteTexture;
            GL.PushMatrix();
            GL.LoadPixelMatrix();
            mat.SetPass(0);
            float h = Screen.height;
            var c = new Vector2(center.x * scale, h - center.y * scale);
            rx *= scale; ry *= scale;
            const int n = 96;
            if (fill)
            {
                GL.Begin(GL.TRIANGLES);
                for (int i = 0; i < n; i++)
                {
                    float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
                    GL.Color(color); GL.Vertex(c);
                    var edge = new Color(color.r, color.g, color.b, color.a * 1.6f);
                    GL.Color(edge); GL.Vertex(c + new Vector2(Mathf.Cos(a0) * rx, Mathf.Sin(a0) * ry));
                    GL.Vertex(c + new Vector2(Mathf.Cos(a1) * rx, Mathf.Sin(a1) * ry));
                }
            }
            else
            {
                GL.Begin(GL.QUADS);
                GL.Color(color);
                float w = width * scale;
                for (int i = 0; i < n; i++)
                {
                    // Every other dash pair is left out when dashed.
                    float u = (i + .5f) / n + phase;
                    if (dashes > 0 && Mathf.Repeat(u * dashes, 1f) > .55f) continue;
                    float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
                    var d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)); var d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                    GL.Vertex(c + new Vector2(d0.x * rx, d0.y * ry)); GL.Vertex(c + new Vector2(d0.x * (rx + w), d0.y * (ry + w)));
                    GL.Vertex(c + new Vector2(d1.x * (rx + w), d1.y * (ry + w))); GL.Vertex(c + new Vector2(d1.x * rx, d1.y * ry));
                }
            }
            GL.End();
            GL.PopMatrix();
        }

        /// <summary>An arc on a circle (a hold-to-confirm progress ring), from 12 o'clock clockwise.</summary>
        public static void Arc(Vector2 center, float radius, float width, float fraction, Color color, float scale)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint || fraction <= 0) return;
            var mat = Shared();
            if (mat == null) return;
            mat.mainTexture = Texture2D.whiteTexture;
            GL.PushMatrix();
            GL.LoadPixelMatrix();
            mat.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(color);
            var c = new Vector2(center.x * scale, Screen.height - center.y * scale);
            float r0 = radius * scale, r1 = (radius + width) * scale;
            int n = Mathf.Max(2, Mathf.CeilToInt(64 * fraction));
            for (int i = 0; i < n; i++)
            {
                // Screen y points up here, so clockwise runs toward negative angles from the top.
                float a0 = Mathf.PI / 2f - fraction * Mathf.PI * 2f * i / n, a1 = Mathf.PI / 2f - fraction * Mathf.PI * 2f * (i + 1) / n;
                var d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)); var d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                GL.Vertex(c + d0 * r0); GL.Vertex(c + d0 * r1); GL.Vertex(c + d1 * r1); GL.Vertex(c + d1 * r0);
            }
            GL.End();
            GL.PopMatrix();
        }

        /// <summary>Advances and draws everything (call once per Repaint, after the windows).</summary>
        public void Draw(float scale)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            Step();
            var mat = Mat();
            if (mat != null && (parts.Count > 0 || rings.Count > 0))
            {
                GL.PushMatrix();
                GL.LoadPixelMatrix();
                DrawBatch(mat, DreamSkin.Glow, scale, Kind.Spark, Kind.Glow);
                DrawBatch(mat, DreamSkin.Flame, scale, Kind.Flame, Kind.Flame);
                DrawRings(mat, scale);
                GL.PopMatrix();
            }
            DrawMarks();
        }

        void DrawBatch(Material mat, Texture tex, float scale, Kind a, Kind b)
        {
            mat.mainTexture = tex;
            mat.SetPass(0);
            GL.Begin(GL.QUADS);
            float h = Screen.height;
            foreach (var p in parts)
            {
                if (p.Kind != a && p.Kind != b) continue;
                float k = p.Age / p.Life;
                var c = Color.Lerp(p.Color, p.Late, k);
                c.a *= p.Kind == Kind.Flame ? Mathf.Clamp01((1f - k) * 1.6f) * Mathf.Clamp01(k * 8f) : 1f - k;
                float size = p.Size * (p.Kind == Kind.Flame ? 1f - k * .6f : 1f - k * .4f) * scale;
                float w = size, ht = p.Kind == Kind.Flame ? size * 1.8f : size;
                if (p.Kind == Kind.Spark) { float stretch = Mathf.Min(3f, 1f + p.Vel.magnitude * .012f); ht *= stretch; }
                float x = p.Pos.x * scale, y = h - p.Pos.y * scale;
                // Flames sit on their base; sparks are centred.
                float y0 = p.Kind == Kind.Flame ? y - size * .35f : y - ht / 2f, y1 = y0 + ht;
                GL.Color(c);
                GL.TexCoord2(0, 0); GL.Vertex3(x - w / 2f, y0, 0);
                GL.TexCoord2(0, 1); GL.Vertex3(x - w / 2f, y1, 0);
                GL.TexCoord2(1, 1); GL.Vertex3(x + w / 2f, y1, 0);
                GL.TexCoord2(1, 0); GL.Vertex3(x + w / 2f, y0, 0);
            }
            GL.End();
        }

        void DrawRings(Material mat, float scale)
        {
            if (rings.Count == 0) return;
            mat.mainTexture = Texture2D.whiteTexture;
            mat.SetPass(0);
            GL.Begin(GL.QUADS);
            float h = Screen.height;
            foreach (var r in rings)
            {
                float k = r.Age / r.Life, radius = r.Radius * (.35f + k * .85f) * scale, width = (3f * (1f - k) + .8f) * scale;
                var c = r.Color; c.a *= (1f - k) * .9f;
                GL.Color(c);
                const int n = 48;
                for (int i = 0; i < n; i++)
                {
                    float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
                    Vector2 o0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0) * r.Squash), o1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1) * r.Squash);
                    var center = new Vector2(r.Pos.x * scale, h - r.Pos.y * scale);
                    GL.Vertex(center + o0 * radius); GL.Vertex(center + o0 * (radius + width));
                    GL.Vertex(center + o1 * (radius + width)); GL.Vertex(center + o1 * radius);
                }
            }
            GL.End();
        }

        void DrawMarks()
        {
            float now = Time.unscaledTime;
            foreach (var m in marks)
            {
                float t = (now - m.Born) / 1.9f;
                float alpha = t < .1f ? t / .1f : t > .7f ? 1f - (t - .7f) / .3f : 1f;
                // A brief flicker right after it appears.
                if (t > .12f && t < .2f) alpha *= .5f;
                float rise = Mathf.Lerp(0, 70, 1f - Mathf.Pow(1f - t, 2f));
                var c = new Vector2(m.Pos.x, m.Pos.y - rise);
                var r = new Rect(c.x - 300, c.y - 30, 600, 56);
                DreamSkin.GlowAt(c, new Vector2(340, 90), new Color(DreamSkin.Mint.r, DreamSkin.Mint.g, DreamSkin.Mint.b, .16f * alpha));
                var split = m.Warn ? DreamSkin.Warn : DreamSkin.Violet;
                DreamSkin.Label(new Rect(r.x - 2.5f, r.y, r.width, r.height), m.Title, DreamSkin.Eerie, new Color(split.r, split.g, split.b, .7f * alpha));
                DreamSkin.Label(new Rect(r.x + 2.5f, r.y, r.width, r.height), m.Title, DreamSkin.Eerie, new Color(DreamSkin.Mint.r, DreamSkin.Mint.g, DreamSkin.Mint.b, .55f * alpha));
                DreamSkin.Label(r, m.Title, DreamSkin.Eerie, new Color(.88f, 1f, .95f, alpha));
                if (!string.IsNullOrEmpty(m.Sub))
                    DreamSkin.Shadowed(new Rect(c.x - 200, c.y + 24, 400, 22), m.Sub, DreamSkin.EerieSub, new Color(DreamSkin.Violet.r, DreamSkin.Violet.g, DreamSkin.Violet.b, alpha));
            }
        }
    }
}
