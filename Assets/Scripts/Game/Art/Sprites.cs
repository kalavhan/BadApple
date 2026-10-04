using System.Collections.Generic;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// Sprites for everything on screen. Real art (Autosprite, comic + dark Tim Burton style) is loaded from
    /// Assets/Resources/Art when present; otherwise the placeholder pixel art drawn in code is used. Every getter is cached.
    /// </summary>
    public static class Sprites
    {
        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        static Texture2D white;

        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        public static Texture2D White
        {
            get
            {
                if (white == null)
                {
                    white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    white.SetPixel(0, 0, Color.white);
                    white.Apply();
                }
                return white;
            }
        }

        static Sprite Cached(string key, System.Func<Sprite> make)
        {
            if (!cache.TryGetValue(key, out var s) || s == null)
            {
                // Real art first (Assets/Resources/Art/*.png, made in Autosprite); the code-drawn sprite is the fallback.
                s = (UseArt ? LoadArt(key) : null) ?? make();
                cache[key] = s;
            }
            return s;
        }

        // ---------- Imported art ----------

        const string UseArtKey = "BadAppleHotel.UseArt";

        /// <summary>
        /// True (default): load PNGs from Assets/Resources/Art, falling back per sprite to the code-drawn placeholder.
        /// False: always use the placeholders. Saved in PlayerPrefs; changing it clears the cache, so the new look
        /// shows up from the next match (or when the sprite is next requested).
        /// </summary>
        public static bool UseArt
        {
            get => PlayerPrefs.GetInt(UseArtKey, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(UseArtKey, value ? 1 : 0);
                PlayerPrefs.Save();
                cache.Clear();
            }
        }

        struct ArtSpec
        {
            public string File;
            public float Size;     // world units for the longest side (tiles: ignored, always exactly 1 tile)
            public Vector2 Pivot;
            public bool Tile;
        }

        static readonly Vector2 FeetPivot = new Vector2(0.5f, 0.04f);

        static readonly Dictionary<string, float> TowerSize = new Dictionary<string, float>
        {
            { "gun_turret", 1.1f }, { "missile_launcher", 1.1f }, { "electric_tower", 1.35f }, { "dragon_statue", 1.1f },
            { "slow_totem", 1.4f }, { "faith_tower", 1.2f }, { "crystal_ball", 1.1f },
            { "dream_lamp", 1.2f }, { "flame_brazier", 1.0f }, { "sniper_nest", 1.45f }, { "tesla_coil", 1.4f }
        };

        static bool ArtFor(string key, out ArtSpec a)
        {
            a = new ArtSpec { Size = 1f, Pivot = Center };
            if (key == "corridor") { a.File = "floor_corridor"; a.Tile = true; return true; }
            if (key == "roomfloor") { a.File = "floor_room"; a.Tile = true; return true; }
            if (key == "wall") { a.File = "wall"; a.Tile = true; return true; }
            if (key == "buildtile") { a.File = "build_plate"; a.Size = 0.96f; return true; }
            if (key == "dooropen") { a.File = "door_open"; return true; }
            if (key == "doorbroken") { a.File = "door_broken"; return true; }
            if (key == "ghost") { a.File = "ghost"; a.Size = 1.2f; return true; }
            if (key.StartsWith("door") && int.TryParse(key.Substring(4), out int dl))
            {
                a.File = dl <= 3 ? "door_wood" : dl <= 6 ? "door_reinforced" : "door_iron";
                return true;
            }
            if (key.StartsWith("bed") && int.TryParse(key.Substring(3), out int bl)) { a.File = "bed_" + bl; a.Size = 1.45f; return true; }
            if (key.StartsWith("resident") && int.TryParse(key.Substring(8), out int ri))
            {
                a.File = "resident_" + (Mathf.Abs(ri) % 6); a.Size = 1.5f; a.Pivot = FeetPivot; return true;
            }
            if (key.StartsWith("monster_")) { a.File = key; a.Size = 2f; a.Pivot = FeetPivot; return true; }
            if (key.StartsWith("tower_"))
            {
                a.File = key;
                a.Size = TowerSize.TryGetValue(key.Substring(6), out var ts) ? ts : 1.15f;
                return true;
            }
            if (key.StartsWith("part_")) { a.File = key; a.Size = 0.75f; return true; }
            return false;
        }

        static Sprite LoadArt(string key)
        {
            if (!ArtFor(key, out var a)) return null;
            var tex = Resources.Load<Texture2D>("Art/" + a.File);
            if (tex == null) return null;
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            float ppu = a.Tile ? tex.width : Mathf.Max(tex.width, tex.height) / a.Size;
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), a.Pivot, ppu, 0, SpriteMeshType.FullRect);
        }

        // ---------- Tiles ----------

        public static Sprite CorridorFloor => Cached("corridor", () =>
        {
            var c = new PixelCanvas(16, 16);
            var a = Palette.Shade(Palette.Teal, 0.55f);
            var b = Palette.Shade(Palette.Teal, 0.62f);
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    c.Set(x, y, ((x / 8) + (y / 8)) % 2 == 0 ? a : b);
            c.Set(3, 11, Palette.Shade(Palette.Teal, 0.75f));
            c.Set(12, 4, Palette.Shade(Palette.Teal, 0.75f));
            return c.ToSprite(Center);
        });

        public static Sprite RoomFloor => Cached("roomfloor", () =>
        {
            var c = new PixelCanvas(16, 16);
            c.Fill(Palette.Shade(Palette.Rust, 0.55f));
            for (int y = 0; y < 16; y += 4) c.Rect(0, y, 16, 1, Palette.Shade(Palette.Rust, 0.4f));
            c.Set(5, 2, Palette.Shade(Palette.Rust, 0.4f));
            c.Set(11, 6, Palette.Shade(Palette.Rust, 0.4f));
            c.Set(2, 10, Palette.Shade(Palette.Rust, 0.4f));
            c.Set(13, 14, Palette.Shade(Palette.Rust, 0.4f));
            return c.ToSprite(Center);
        });

        public static Sprite Wall => Cached("wall", () =>
        {
            var c = new PixelCanvas(16, 16);
            c.Fill(Palette.Plum);
            c.Rect(0, 12, 16, 4, Palette.Shade(Palette.Plum, 1.35f));
            c.Rect(0, 12, 16, 1, Palette.Shade(Palette.Plum, 0.7f));
            c.Rect(0, 6, 16, 1, Palette.Shade(Palette.Plum, 0.75f));
            c.Rect(5, 0, 1, 6, Palette.Shade(Palette.Plum, 0.75f));
            c.Rect(11, 6, 1, 6, Palette.Shade(Palette.Plum, 0.75f));
            c.Rect(0, 0, 16, 1, Palette.Ink);
            return c.ToSprite(Center);
        });

        public static Sprite Door(int level) => Cached("door" + level, () =>
        {
            var c = new PixelCanvas(16, 16);
            c.Fill(Palette.Rust);
            c.Rect(2, 0, 12, 15, Palette.Teal);
            c.Rect(3, 1, 10, 13, Palette.Shade(Palette.Teal, 1.15f));
            int bands = Mathf.Clamp((level + 1) / 2, 1, 5);
            for (int i = 0; i < bands; i++)
                c.Rect(2, 2 + i * 2 + (i * 1), 12, 1, Palette.Shade(Palette.Bone, 0.7f));
            c.Rect(11, 7, 2, 2, Palette.Candle);
            c.Rect(0, 15, 16, 1, Palette.Ink);
            return c.ToSprite(Center);
        });

        public static Sprite DoorBroken => Cached("doorbroken", () =>
        {
            var c = new PixelCanvas(16, 16);
            c.Rect(0, 0, 2, 16, Palette.Rust);
            c.Rect(14, 0, 2, 16, Palette.Rust);
            c.Rect(2, 0, 3, 5, Palette.Teal);
            c.Rect(11, 0, 3, 3, Palette.Teal);
            c.Line(2, 5, 4, 8, Palette.Teal);
            c.Line(13, 3, 12, 7, Palette.Teal);
            c.Set(7, 1, Palette.Shade(Palette.Teal, 1.2f));
            c.Set(9, 0, Palette.Shade(Palette.Teal, 1.2f));
            return c.ToSprite(Center);
        });

        public static Sprite Slot => Cached("slot", () =>
        {
            var c = new PixelCanvas(16, 16);
            var col = Palette.WithAlpha(Palette.Bone, 55);
            for (int i = 2; i < 14; i += 2)
            {
                c.Set(i, 2, col); c.Set(i, 13, col);
                c.Set(2, i, col); c.Set(13, i, col);
            }
            return c.ToSprite(Center);
        });

        /// <summary>A bolted floor plate: the only tiles where buildings can be attached.</summary>
        public static Sprite BuildTile => Cached("buildtile", () =>
        {
            var c = new PixelCanvas(16, 16);
            c.Fill(Palette.Shade(Palette.Rust, 0.55f));
            c.Rect(1, 1, 14, 14, Palette.Shade(Palette.Plum, 0.9f));
            c.Rect(2, 2, 12, 12, Palette.Shade(Palette.Teal, 0.7f));
            c.Rect(3, 3, 10, 10, Palette.Shade(Palette.Teal, 0.85f));
            for (int i = 4; i < 12; i += 3) { c.Set(i, 8, Palette.Shade(Palette.Teal, 0.65f)); c.Set(8, i, Palette.Shade(Palette.Teal, 0.65f)); }
            var bolt = Palette.Shade(Palette.Bone, 0.85f);
            c.Set(2, 2, bolt); c.Set(13, 2, bolt); c.Set(2, 13, bolt); c.Set(13, 13, bolt);
            return c.ToSprite(Center);
        });

        public static Sprite DoorOpen => Cached("dooropen", () =>
        {
            var c = new PixelCanvas(16, 16);
            c.Fill(Palette.Rust);
            c.Rect(2, 0, 12, 15, Palette.Shade(Palette.Ink, 1.2f));
            c.Rect(2, 0, 3, 15, Palette.Teal);           // door leaf swung against the frame
            c.Rect(3, 1, 1, 13, Palette.Shade(Palette.Teal, 1.2f));
            c.Set(4, 7, Palette.Candle);
            c.Rect(0, 15, 16, 1, Palette.Ink);
            return c.ToSprite(Center);
        });

        /// <summary>Unit circle outline (diameter = 1 world unit), scaled to show tower ranges.</summary>
        public static Sprite Ring => Cached("ring", () =>
        {
            const int size = 256;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float r = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    float edge = Mathf.Clamp01(1.5f - Mathf.Abs(d - (r - 3f)));
                    float fill = d < r - 3f ? 0.10f : 0f;
                    px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Max(edge, fill));
                }
            t.SetPixels32(px);
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, size, size), Center, size);
        });

        // ---------- Beds (6 levels; the code-drawn fallback only has 5 looks) ----------

        public static Sprite Bed(int level) => Cached("bed" + level, () =>
        {
            var c = new PixelCanvas(16, 16);
            switch (level)
            {
                case 1: // a piece of paper on the floor, crooked
                    c.Rect(3, 5, 10, 6, Palette.Bone);
                    c.Rect(3, 10, 3, 1, Palette.Clear);
                    c.Rect(12, 5, 1, 2, Palette.Clear);
                    c.Line(5, 7, 10, 7, Palette.Shade(Palette.Bone, 0.75f));
                    c.Line(5, 9, 9, 9, Palette.Shade(Palette.Bone, 0.75f));
                    break;
                case 2: // a cardboard box
                    c.Rect(2, 3, 12, 9, Palette.Shade(Palette.Rust, 1.25f));
                    c.Rect(2, 11, 12, 1, Palette.Shade(Palette.Rust, 0.8f));
                    c.Line(2, 12, 0, 14, Palette.Shade(Palette.Rust, 1.1f));
                    c.Line(13, 12, 15, 13, Palette.Shade(Palette.Rust, 1.1f));
                    c.Rect(7, 3, 1, 8, Palette.Shade(Palette.Rust, 0.85f));
                    break;
                case 3: // a pile of random clothes
                    c.Ellipse(6, 6, 4, 3, Palette.Shade(Palette.Plum, 1.5f));
                    c.Ellipse(10, 7, 4, 3, Palette.Teal);
                    c.Ellipse(8, 9, 3, 2, Palette.Moss);
                    c.Rect(4, 4, 3, 2, Palette.Candle);
                    c.Line(11, 4, 14, 3, Palette.Shade(Palette.Bone, 0.8f));
                    break;
                case 4: // a very basic bed
                    c.Rect(1, 2, 14, 9, Palette.Rust);
                    c.Rect(2, 3, 12, 7, Palette.Bone);
                    c.Rect(2, 7, 4, 3, Palette.Shade(Palette.Bone, 1.08f));
                    c.Rect(1, 1, 1, 2, Palette.Shade(Palette.Rust, 0.7f));
                    c.Rect(14, 1, 1, 2, Palette.Shade(Palette.Rust, 0.7f));
                    break;
                default: // a comfy, detailed bed
                    c.Rect(1, 2, 14, 10, Palette.Rust);
                    c.Rect(0, 1, 2, 12, Palette.Shade(Palette.Rust, 0.8f));
                    c.Rect(14, 1, 2, 12, Palette.Shade(Palette.Rust, 0.8f));
                    c.Rect(2, 3, 12, 6, Palette.Candle);
                    for (int x = 3; x < 14; x += 3) c.Rect(x, 3, 1, 6, Palette.Shade(Palette.Candle, 0.8f));
                    c.Ellipse(5, 10, 2, 1, Palette.Bone);
                    c.Ellipse(10, 10, 2, 1, Palette.Bone);
                    c.Set(0, 13, Palette.Candle);
                    c.Set(15, 13, Palette.Candle);
                    break;
            }
            c.Outline(Palette.Ink);
            return c.ToSprite(Center);
        });

        // ---------- Characters ----------

        static readonly Color32[] Pajamas =
        {
            Palette.Shade(Palette.Plum, 1.6f), Palette.Shade(Palette.Teal, 1.3f), Palette.Shade(Palette.Moss, 1.2f),
            Palette.Shade(Palette.Rust, 1.3f), Palette.Shade(Palette.Mint, 0.8f), Palette.Shade(Palette.Candle, 0.9f)
        };

        public static Sprite Resident(int colorIndex) => Cached("resident" + colorIndex, () =>
        {
            var c = new PixelCanvas(16, 24);
            var pj = Pajamas[Mathf.Abs(colorIndex) % Pajamas.Length];
            // stick legs, slightly crooked
            c.Line(7, 1, 7, 6, Palette.Ink);
            c.Line(9, 1, 8, 6, Palette.Ink);
            c.Set(6, 1, Palette.Ink);
            c.Set(10, 1, Palette.Ink);
            // thin body
            c.Rect(6, 6, 4, 6, pj);
            c.Rect(6, 9, 4, 1, Palette.Shade(pj, 0.8f));
            // stick arms
            c.Line(5, 11, 3, 8, Palette.Ink);
            c.Line(10, 11, 12, 7, Palette.Ink);
            // oversized head
            c.Ellipse(8, 17, 5, 5, Palette.Bone);
            // big round eyes
            c.Ellipse(6, 17, 1, 1, Palette.Ink);
            c.Ellipse(10, 17, 1, 1, Palette.Ink);
            c.Set(6, 18, Palette.Bone);
            c.Set(10, 18, Palette.Bone);
            // small mouth
            c.Set(8, 14, Palette.Ink);
            // hair tuft
            c.Line(7, 22, 9, 23, Palette.Ink);
            c.Set(10, 22, Palette.Ink);
            c.Outline(Palette.Ink);
            return c.ToSprite(new Vector2(0.5f, 0.06f));
        });

        public static Sprite Ghost => Cached("ghost", () =>
        {
            var c = new PixelCanvas(16, 24);
            c.Ellipse(8, 14, 5, 7, Palette.WithAlpha(Palette.Mint, 150));
            for (int x = 3; x < 14; x += 2) c.Set(x, 7, Palette.Clear);
            c.Ellipse(6, 16, 1, 1, Palette.Ink);
            c.Ellipse(10, 16, 1, 1, Palette.Ink);
            c.Rect(7, 12, 2, 1, Palette.Ink);
            return c.ToSprite(new Vector2(0.5f, 0.06f));
        });

        public static Sprite Monster(string id) => Cached("monster_" + id, () =>
        {
            var c = new PixelCanvas(24, 32);
            switch (id)
            {
                case "moldy_matron":
                    c.Ellipse(12, 11, 10, 10, Palette.Moss);
                    c.Ellipse(7, 9, 2, 2, Palette.Shade(Palette.Moss, 0.7f));
                    c.Ellipse(16, 13, 2, 1, Palette.Shade(Palette.Moss, 0.7f));
                    c.Ellipse(13, 5, 1, 1, Palette.Shade(Palette.Moss, 0.7f));
                    for (int x = 4; x < 21; x += 4) c.Line(x, 2, x, 0, Palette.Moss);
                    c.Ellipse(12, 25, 6, 5, Palette.Plum);
                    c.Ellipse(12, 22, 4, 3, Palette.Shade(Palette.Bone, 0.85f));
                    c.Set(10, 23, Palette.Ink);
                    c.Set(14, 23, Palette.Ink);
                    c.Line(10, 20, 14, 21, Palette.Ink);
                    break;
                case "bellhop_wraith":
                    for (int y = 2; y <= 21; y++)
                    {
                        int half = 2 + (21 - y) / 3;
                        c.Rect(12 - half, y, half * 2, 1, Palette.Shade(Palette.Plum, 1.4f));
                    }
                    for (int x = 4; x < 21; x += 2) c.Set(x, 2, Palette.Clear);
                    c.Line(7, 16, 3, 10, Palette.Shade(Palette.Plum, 1.4f));
                    c.Line(17, 16, 21, 11, Palette.Shade(Palette.Plum, 1.4f));
                    c.Ellipse(12, 24, 5, 5, Palette.Shade(Palette.Bone, 0.95f));
                    c.Rect(9, 24, 2, 2, Palette.Mint);
                    c.Rect(14, 24, 2, 2, Palette.Mint);
                    c.Rect(11, 21, 3, 1, Palette.Ink);
                    c.Rect(8, 28, 8, 3, Palette.Rust);
                    c.Rect(8, 28, 8, 1, Palette.Candle);
                    break;
                default: // stitchwork_chef
                    c.Ellipse(12, 11, 8, 9, Palette.Shade(Palette.Bone, 0.9f));
                    for (int y = 5; y < 18; y += 3) { c.Set(11, y, Palette.Ink); c.Set(13, y, Palette.Ink); c.Set(12, y + 1, Palette.Ink); }
                    c.Line(4, 13, 1, 8, Palette.Ink);
                    c.Line(20, 13, 22, 9, Palette.Ink);
                    c.Rect(20, 9, 3, 4, Palette.Shade(Palette.Bone, 0.65f));
                    c.Ellipse(12, 22, 6, 5, Palette.Bone);
                    c.Ellipse(10, 22, 2, 2, Palette.Ink);
                    c.Set(10, 23, Palette.Bone);
                    c.Line(14, 21, 16, 23, Palette.Ink);
                    c.Line(14, 23, 16, 21, Palette.Ink);
                    c.Line(9, 19, 15, 19, Palette.Ink);
                    for (int x = 10; x < 15; x += 2) c.Set(x, 18, Palette.Ink);
                    c.Rect(8, 26, 8, 3, Palette.Shade(Palette.Bone, 1.05f));
                    c.Ellipse(12, 29, 5, 2, Palette.Shade(Palette.Bone, 1.05f));
                    break;
            }
            c.Outline(Palette.Ink);
            return c.ToSprite(new Vector2(0.5f, 0.06f));
        });

        // ---------- Towers ----------

        public static Sprite Tower(string id) => Cached("tower_" + id, () =>
        {
            var c = new PixelCanvas(16, 16);
            switch (id)
            {
                case "missile_launcher":
                    c.Rect(2, 1, 12, 6, Palette.Rust);
                    c.Rect(3, 7, 3, 6, Palette.Teal);
                    c.Rect(7, 7, 3, 7, Palette.Teal);
                    c.Rect(11, 7, 3, 6, Palette.Teal);
                    c.Rect(3, 12, 3, 1, Palette.Candle);
                    c.Rect(7, 13, 3, 1, Palette.Candle);
                    c.Rect(11, 12, 3, 1, Palette.Candle);
                    break;
                case "electric_tower":
                    c.Rect(7, 1, 2, 10, Palette.Ink);
                    c.Rect(4, 1, 8, 2, Palette.Shade(Palette.Plum, 1.3f));
                    c.Ellipse(8, 8, 4, 1, Palette.Mint);
                    c.Ellipse(8, 11, 3, 1, Palette.Mint);
                    c.Ellipse(8, 14, 2, 1, Palette.Shade(Palette.Mint, 1.1f));
                    break;
                case "dragon_statue":
                    c.Rect(3, 1, 10, 4, Palette.Shade(Palette.Plum, 1.5f));
                    c.Ellipse(8, 9, 5, 4, Palette.Shade(Palette.Moss, 0.9f));
                    c.Rect(11, 7, 4, 3, Palette.Shade(Palette.Moss, 0.9f));
                    c.Set(9, 10, Palette.Candle);
                    c.Rect(14, 7, 1, 1, Palette.Candle);
                    c.Line(5, 12, 4, 15, Palette.Shade(Palette.Moss, 0.7f));
                    c.Line(8, 13, 8, 15, Palette.Shade(Palette.Moss, 0.7f));
                    break;
                case "slow_totem":
                    c.Rect(5, 1, 6, 12, Palette.Shade(Palette.Plum, 1.4f));
                    c.Set(6, 10, Palette.Mint); c.Set(9, 10, Palette.Mint);
                    c.Rect(6, 8, 4, 1, Palette.Ink);
                    c.Set(6, 5, Palette.Shade(Palette.Mint, 0.8f)); c.Set(9, 5, Palette.Shade(Palette.Mint, 0.8f));
                    c.Rect(7, 3, 2, 1, Palette.Ink);
                    c.Rect(4, 13, 8, 2, Palette.Teal);
                    break;
                case "crystal_ball":
                    c.Rect(4, 1, 8, 3, Palette.Shade(Palette.Plum, 1.4f));
                    c.Rect(5, 4, 6, 1, Palette.Rust);
                    c.Ellipse(8, 9, 4, 4, Palette.Shade(Palette.Mint, 0.85f));
                    c.Ellipse(8, 9, 2, 2, Palette.Mint);
                    c.Set(6, 11, Palette.Bone); c.Set(7, 12, Palette.Bone);
                    c.Set(9, 8, Palette.Ink);
                    break;
                case "faith_tower":
                    c.Rect(4, 1, 8, 7, Palette.Shade(Palette.Bone, 0.8f));
                    c.Rect(6, 1, 4, 4, Palette.Shade(Palette.Bone, 0.55f));
                    for (int i = 0; i < 5; i++) c.Rect(3 + i, 8 + i, 10 - i * 2, 1, Palette.Rust);
                    c.Ellipse(8, 4, 2, 2, Palette.AppleRed);
                    c.Set(8, 7, Palette.Moss);
                    break;
                default: // gun_turret
                    c.Rect(3, 1, 10, 5, Palette.Shade(Palette.Rust, 0.9f));
                    c.Ellipse(8, 8, 4, 3, Palette.Shade(Palette.Teal, 1.1f));
                    c.Rect(9, 8, 6, 2, Palette.Ink);
                    c.Set(6, 9, Palette.Candle);
                    break;
            }
            c.Outline(Palette.Ink);
            return c.ToSprite(Center);
        });

        // ---------- Items ----------

        /// <summary>The Bad Apple: grinning, half-lidded, leaf curled like an eyebrow.</summary>
        public static Sprite Apple => Cached("apple", () =>
        {
            var c = new PixelCanvas(16, 16);
            c.Ellipse(8, 7, 6, 6, Palette.AppleRed);
            c.Set(4, 10, Palette.Bone); c.Set(4, 9, Palette.Bone); c.Set(5, 11, Palette.Bone);
            // half-lidded eyes
            c.Rect(5, 8, 2, 1, Palette.Ink); c.Rect(10, 8, 2, 1, Palette.Ink);
            c.Rect(5, 9, 2, 1, Palette.Shade(Palette.AppleRed, 0.7f)); c.Rect(10, 9, 2, 1, Palette.Shade(Palette.AppleRed, 0.7f));
            // lopsided grin with tiny teeth
            c.Line(3, 6, 5, 4, Palette.Ink);
            c.Line(5, 4, 11, 4, Palette.Ink);
            c.Line(11, 4, 13, 7, Palette.Ink);
            c.Set(6, 5, Palette.Bone); c.Set(8, 5, Palette.Bone); c.Set(10, 5, Palette.Bone);
            // stem + leaf
            c.Rect(8, 13, 1, 2, Palette.Rust);
            c.Ellipse(11, 14, 2, 1, Palette.Moss);
            c.Set(13, 15, Palette.Moss);
            c.Outline(Palette.Ink);
            return c.ToSprite(Center);
        });

        public static Sprite Part(string id) => Cached("part_" + id, () =>
        {
            var c = new PixelCanvas(12, 12);
            var flesh = Palette.Shade(Palette.Bone, 0.9f);
            switch (id)
            {
                case "leg":
                    c.Rect(5, 3, 2, 7, flesh);
                    c.Rect(5, 2, 4, 2, flesh);
                    c.Set(6, 7, Palette.Ink);
                    break;
                case "torso":
                    c.Ellipse(6, 6, 4, 5, flesh);
                    c.Line(6, 2, 6, 10, Palette.Ink);
                    c.Set(5, 4, Palette.Ink); c.Set(7, 6, Palette.Ink); c.Set(5, 8, Palette.Ink);
                    break;
                case "eye":
                    c.Ellipse(6, 6, 4, 4, Palette.Bone);
                    c.Ellipse(6, 6, 2, 2, Palette.Teal);
                    c.Set(6, 6, Palette.Ink);
                    c.Set(3, 7, Palette.Rust); c.Set(9, 5, Palette.Rust);
                    break;
                default: // arm
                    c.Line(2, 3, 8, 8, flesh);
                    c.Line(2, 4, 8, 9, flesh);
                    c.Ellipse(9, 9, 1, 1, flesh);
                    break;
            }
            c.Outline(Palette.Ink);
            return c.ToSprite(Center);
        });

        public static Sprite Projectile(string damageType) => Cached("proj_" + damageType, () =>
        {
            var c = new PixelCanvas(4, 4);
            Color32 col;
            switch (damageType)
            {
                case "electric": col = Palette.Mint; break;
                case "fire": col = Palette.Candle; break;
                case "slow": col = Palette.Shade(Palette.Mint, 0.7f); break;
                default: col = Palette.Bone; break;
            }
            c.Rect(1, 0, 2, 4, col);
            c.Rect(0, 1, 4, 2, col);
            return c.ToSprite(Center);
        });
    }
}
