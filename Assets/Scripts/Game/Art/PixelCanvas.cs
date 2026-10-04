using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>Tiny pixel painter used to make placeholder sprites at runtime (y = 0 is the bottom row).</summary>
    public class PixelCanvas
    {
        public readonly int W;
        public readonly int H;
        readonly Color32[] px;

        public PixelCanvas(int w, int h)
        {
            W = w;
            H = h;
            px = new Color32[w * h];
        }

        public void Set(int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            px[y * W + x] = c;
        }

        public Color32 Get(int x, int y)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return Palette.Clear;
            return px[y * W + x];
        }

        public void Fill(Color32 c)
        {
            for (int i = 0; i < px.Length; i++) px[i] = c;
        }

        public void Rect(int x, int y, int w, int h, Color32 c)
        {
            for (int j = y; j < y + h; j++)
                for (int i = x; i < x + w; i++)
                    Set(i, j, c);
        }

        public void Ellipse(int cx, int cy, int rx, int ry, Color32 c)
        {
            if (rx <= 0 || ry <= 0) { Set(cx, cy, c); return; }
            for (int j = -ry; j <= ry; j++)
                for (int i = -rx; i <= rx; i++)
                {
                    float fx = i / (rx + 0.5f);
                    float fy = j / (ry + 0.5f);
                    if (fx * fx + fy * fy <= 1f) Set(cx + i, cy + j, c);
                }
        }

        public void Line(int x0, int y0, int x1, int y1, Color32 c)
        {
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                Set(x0, y0, c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        /// <summary>Adds a 1 px outline around every opaque shape.</summary>
        public void Outline(Color32 c)
        {
            var copy = (Color32[])px.Clone();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (copy[y * W + x].a != 0) continue;
                    bool near =
                        (x > 0 && copy[y * W + x - 1].a != 0) ||
                        (x < W - 1 && copy[y * W + x + 1].a != 0) ||
                        (y > 0 && copy[(y - 1) * W + x].a != 0) ||
                        (y < H - 1 && copy[(y + 1) * W + x].a != 0);
                    if (near) px[y * W + x] = c;
                }
        }

        public Texture2D ToTexture()
        {
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        public Sprite ToSprite(Vector2 pivot, float pixelsPerUnit = 16f)
        {
            var t = ToTexture();
            return Sprite.Create(t, new Rect(0, 0, W, H), pivot, pixelsPerUnit);
        }
    }
}
