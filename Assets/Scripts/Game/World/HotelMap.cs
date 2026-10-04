using System.Collections.Generic;
using BadAppleHotel.Config;
using UnityEngine;

namespace BadAppleHotel.Game
{
    public enum Tile { Void, Corridor, Wall, Door, RoomFloor }

    /// <summary>Static layout data for one guest room (generated per match).</summary>
    public class RoomDef
    {
        public int Index;
        public char Letter;                 // which deformed letter the room grew from
        public RectInt Lot;                 // the plot it was built on (walls included)
        public readonly List<Vector2Int> Floor = new List<Vector2Int>();
        public readonly HashSet<Vector2Int> FloorSet = new HashSet<Vector2Int>();
        public Vector2Int DoorTile;         // in the wall
        public Vector2Int DoorOutside;      // corridor tile in front of the door
        public Vector2Int DoorInside;       // first floor tile inside
        public Vector2Int BedTile;          // farthest walkable tile from the door
        public readonly HashSet<Vector2Int> Walkway = new HashSet<Vector2Int>(); // default door -> bed path (used by bots)
        public readonly List<Vector2Int> BuildTiles = new List<Vector2Int>();     // every floor tile except bed and door-inside
        public bool Isolated;               // no other door nearby: gets a free building when claimed
        public int NearestDoorDistance;
        public Vector2 Center;

        public bool ContainsInterior(Vector2Int t) => FloorSet.Contains(t);
        public int BuildIndex(Vector2Int t) => BuildTiles.IndexOf(t);
    }

    /// <summary>
    /// Procedural hotel floor. A horizontal corridor (plus vertical ones) splits the map into strips; each strip is cut
    /// into lots of random width and depth, a random subset becomes rooms (so some rooms have neighbours and some sit
    /// alone). Every room grows from a deformed letter (L, T, U, E, O...): cells get random sizes, the letter may be
    /// rotated or mirrored, corners get nibbled and bulged, then a short neck connects it to its door.
    /// Inside, the path from the door to the bed is kept clear and a random share of the remaining floor becomes build
    /// tiles. Bigger, rounder rooms get more tiles; long skinny ones get fewer. Such is luck.
    /// One tile = one world unit; tile (x, y) covers [x, x+1] x [y, y+1].
    /// </summary>
    public class HotelMap
    {
        public readonly int W;
        public readonly int H;
        public readonly int Seed;
        public Tile[,] Tiles { get; private set; }
        public readonly List<RoomDef> Rooms = new List<RoomDef>();
        public Vector2Int MonsterSpawn { get; private set; }
        public Vector2Int Lobby { get; private set; }
        public int CorridorY { get; private set; }
        public readonly List<int> VerticalX = new List<int>();

        readonly MapConfig cfg;
        readonly Dictionary<Vector2Int, RoomDef> roomByDoor = new Dictionary<Vector2Int, RoomDef>();
        int[,] roomId;
        System.Random rng;

        static readonly Vector2Int[] Dirs4 = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        class Lot
        {
            public RectInt Rect;
            public bool Top;            // above the main corridor (door on the bottom wall)
            public bool LeftCorridor;   // a vertical corridor runs along its left side
            public bool RightCorridor;
        }

        public HotelMap(MapConfig cfg, int roomCount, int seed)
        {
            this.cfg = cfg;
            W = cfg.width;
            H = cfg.height;
            Seed = seed;
            for (int attempt = 0; attempt < 120; attempt++)
            {
                rng = new System.Random(seed * 31 + attempt * 7919);
                if (TryGenerate(roomCount)) return;
            }
            throw new System.InvalidOperationException("Could not fit " + roomCount + " rooms in the hotel; check map.json sizes.");
        }

        int R(int min, int maxInclusive) => maxInclusive <= min ? min : rng.Next(min, maxInclusive + 1);

        // ------------------------------------------------------------------ generation

        bool TryGenerate(int roomCount)
        {
            Tiles = new Tile[W, H];
            roomId = new int[W, H];
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++) roomId[x, y] = -1;
            Rooms.Clear();
            roomByDoor.Clear();
            VerticalX.Clear();

            int cw = Mathf.Max(2, cfg.corridorWidth);
            CorridorY = H / 2 - cw / 2 + R(-2, 2);
            for (int x = 0; x < W; x++)
                for (int y = CorridorY; y < CorridorY + cw; y++) Tiles[x, y] = Tile.Corridor;

            int vc = Mathf.Clamp(cfg.verticalCorridors, 0, 3);
            for (int i = 0; i < vc; i++)
            {
                int center = W * (i + 1) / (vc + 1) + R(-W / 12, W / 12);
                int x0 = Mathf.Clamp(center - cw / 2, cfg.lotWidthMin + 1, W - cw - cfg.lotWidthMin - 1);
                VerticalX.Add(x0);
            }
            VerticalX.Sort();
            foreach (int x0 in VerticalX)
                for (int x = x0; x < x0 + cw; x++)
                    for (int y = 0; y < H; y++) Tiles[x, y] = Tile.Corridor;

            // strips between vertical corridors, above and below the main corridor
            var segments = new List<Vector2Int>();
            int start = 0;
            foreach (int x0 in VerticalX)
            {
                if (x0 - 1 >= start) segments.Add(new Vector2Int(start, x0 - 1));
                start = x0 + cw;
            }
            if (W - 1 >= start) segments.Add(new Vector2Int(start, W - 1));

            var lots = new List<Lot>();
            foreach (var seg in segments)
                for (int side = 0; side < 2; side++)
                {
                    bool top = side == 0;
                    int stripH = top ? H - (CorridorY + cw) : CorridorY;
                    int x = seg.x + R(0, 1);
                    while (true)
                    {
                        int lw = R(cfg.lotWidthMin, cfg.lotWidthMax);
                        if (x + lw - 1 > seg.y) lw = seg.y - x + 1;
                        if (lw < cfg.lotWidthMin) break;
                        int lh = R(cfg.lotHeightMin, Mathf.Min(cfg.lotHeightMax, stripH));
                        if (lh < cfg.lotHeightMin || lh > stripH) break;
                        int ly = top ? CorridorY + cw : CorridorY - lh;
                        lots.Add(new Lot
                        {
                            Rect = new RectInt(x, ly, lw, lh),
                            Top = top,
                            LeftCorridor = x == seg.x && seg.x > 0,
                            RightCorridor = x + lw - 1 == seg.y && seg.y < W - 1,
                        });
                        x += lw + R(0, cfg.lotGapMax);
                    }
                }

            if (lots.Count < roomCount) return false;
            for (int i = lots.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (lots[i], lots[j]) = (lots[j], lots[i]);
            }
            lots.RemoveRange(roomCount, lots.Count - roomCount);
            lots.Sort((a, b) => a.Top != b.Top ? (a.Top ? -1 : 1) : a.Rect.x.CompareTo(b.Rect.x));

            foreach (var lot in lots)
                if (!BuildRoom(lot)) return false;

            // walls hug every room's floor; doors are cut into them
            foreach (var room in Rooms)
                foreach (var f in room.Floor)
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            int nx = f.x + dx, ny = f.y + dy;
                            if (InBounds(nx, ny) && Tiles[nx, ny] == Tile.Void) Tiles[nx, ny] = Tile.Wall;
                        }
            foreach (var room in Rooms)
            {
                Tiles[room.DoorTile.x, room.DoorTile.y] = Tile.Door;
                if (Tiles[room.DoorOutside.x, room.DoorOutside.y] != Tile.Corridor) return false;
                roomByDoor[room.DoorTile] = room;
            }

            // lonely rooms: no other door within isolatedDoorDistance
            foreach (var a in Rooms)
            {
                int best = int.MaxValue;
                foreach (var b in Rooms)
                {
                    if (a == b) continue;
                    int d = Mathf.Abs(a.DoorOutside.x - b.DoorOutside.x) + Mathf.Abs(a.DoorOutside.y - b.DoorOutside.y);
                    if (d < best) best = d;
                }
                a.NearestDoorDistance = best;
                a.Isolated = best > cfg.isolatedDoorDistance;
            }

            int mid = cw / 2;
            if (VerticalX.Count > 0)
            {
                int vx = VerticalX[0] + mid;
                MonsterSpawn = new Vector2Int(vx, H - 5);
                Lobby = new Vector2Int(vx, CorridorY + mid);
            }
            else
            {
                MonsterSpawn = new Vector2Int(W - 2, CorridorY + mid);
                Lobby = new Vector2Int(2, CorridorY + mid);
            }
            return true;
        }

        bool BuildRoom(Lot lot)
        {
            var rect = lot.Rect;
            var interior = new RectInt(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2);
            for (int tries = 0; tries < 16; tries++)
            {
                string letter = cfg.letters[rng.Next(cfg.letters.Length)];
                var bmp = LetterShapes.Get(letter);
                if (bmp == null) continue;
                if (rng.Next(2) == 0) bmp = LetterShapes.Transpose(bmp);
                if (rng.Next(2) == 0) bmp = LetterShapes.FlipX(bmp);
                if (rng.Next(2) == 0) bmp = LetterShapes.FlipY(bmp);
                int cols = bmp.GetLength(0), rows = bmp.GetLength(1);

                var colW = Sizes(cols, interior.width);
                var rowH = Sizes(rows, interior.height);
                if (colW == null || rowH == null) continue;
                int sumW = 0, sumH = 0;
                foreach (int v in colW) sumW += v;
                foreach (int v in rowH) sumH += v;

                int ox = interior.x + R(0, interior.width - sumW);
                int slackY = interior.height - sumH;
                int oy = lot.Top ? interior.y + R(0, Mathf.Min(1, slackY)) : interior.yMax - sumH - R(0, Mathf.Min(1, slackY));

                var floor = new HashSet<Vector2Int>();
                int cx = ox;
                for (int c = 0; c < cols; c++)
                {
                    int cy = oy;
                    for (int r = 0; r < rows; r++)
                    {
                        if (bmp[c, r])
                            for (int x = cx; x < cx + colW[c]; x++)
                                for (int y = cy; y < cy + rowH[r]; y++) floor.Add(new Vector2Int(x, y));
                        cy += rowH[r];
                    }
                    cx += colW[c];
                }
                if (floor.Count < 10) continue;

                Deform(floor, interior);
                if (!Connected(floor)) continue;
                if (!CarveDoor(lot, floor, out var door, out var inside, out var outside)) continue;

                var def = new RoomDef
                {
                    Index = Rooms.Count,
                    Letter = letter[0],
                    Lot = rect,
                    DoorTile = door,
                    DoorInside = inside,
                    DoorOutside = outside,
                };
                foreach (var f in floor) { def.Floor.Add(f); def.FloorSet.Add(f); }
                def.Floor.Sort((a, b) => a.y != b.y ? b.y.CompareTo(a.y) : a.x.CompareTo(b.x));
                if (!FurnishRoom(def)) continue;

                Vector2 sum = Vector2.zero;
                foreach (var f in def.Floor)
                {
                    Tiles[f.x, f.y] = Tile.RoomFloor;
                    roomId[f.x, f.y] = def.Index;
                    sum += Center(f);
                }
                def.Center = sum / def.Floor.Count;
                Rooms.Add(def);
                return true;
            }
            return false;
        }

        /// <summary>Random cell sizes for a letter, shrunk until they fit the space (cells may end up 1 tile wide).</summary>
        int[] Sizes(int n, int avail)
        {
            var a = new int[n];
            int sum = 0;
            for (int i = 0; i < n; i++) { a[i] = R(cfg.cellSizeMin, cfg.cellSizeMax); sum += a[i]; }
            while (sum > avail)
            {
                int bi = -1;
                for (int i = 0; i < n; i++) if (a[i] > 1 && (bi < 0 || a[i] > a[bi])) bi = i;
                if (bi < 0) return null;
                a[bi]--;
                sum--;
            }
            return a;
        }

        /// <summary>Nibbles a few corners and pushes out a few bulges so no two rooms look machine-made.</summary>
        void Deform(HashSet<Vector2Int> floor, RectInt interior)
        {
            int nibbles = R(0, cfg.nibbleMax);
            for (int n = 0; n < nibbles; n++)
            {
                var list = new List<Vector2Int>(floor);
                var t = list[rng.Next(list.Count)];
                bool openX = !floor.Contains(t + Vector2Int.left) || !floor.Contains(t + Vector2Int.right);
                bool openY = !floor.Contains(t + Vector2Int.up) || !floor.Contains(t + Vector2Int.down);
                if (!openX || !openY || floor.Count <= 12) continue;
                floor.Remove(t);
                if (!Connected(floor)) floor.Add(t);
            }

            int bulges = R(0, cfg.bulgeMax);
            for (int n = 0; n < bulges; n++)
            {
                var list = new List<Vector2Int>(floor);
                var t = list[rng.Next(list.Count)];
                var d = Dirs4[rng.Next(4)];
                var a = t + d;
                if (floor.Contains(a) || !interior.Contains(a)) continue;
                floor.Add(a);
                var perp = new Vector2Int(d.y, d.x);
                var b = a + perp;
                if (interior.Contains(b) && floor.Contains(t + perp)) floor.Add(b);
            }
        }

        static bool Connected(HashSet<Vector2Int> floor)
        {
            if (floor.Count == 0) return false;
            var seen = new HashSet<Vector2Int>();
            var q = new Queue<Vector2Int>();
            var e = floor.GetEnumerator();
            e.MoveNext();
            q.Enqueue(e.Current);
            seen.Add(e.Current);
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                foreach (var d in Dirs4)
                {
                    var n = c + d;
                    if (floor.Contains(n) && seen.Add(n)) q.Enqueue(n);
                }
            }
            return seen.Count == floor.Count;
        }

        /// <summary>Picks a door on a wall that faces a corridor and digs a straight neck to the nearest floor.</summary>
        bool CarveDoor(Lot lot, HashSet<Vector2Int> floor, out Vector2Int door, out Vector2Int inside, out Vector2Int outside)
        {
            door = inside = outside = default;
            var rect = lot.Rect;
            var sides = new List<int>(); // 0 main, 1 left, 2 right
            if (lot.LeftCorridor && rng.NextDouble() < cfg.sideDoorChance) sides.Add(1);
            if (lot.RightCorridor && rng.NextDouble() < cfg.sideDoorChance) sides.Add(2);
            sides.Add(0);
            int side = sides[rng.Next(sides.Count)];

            Vector2Int inward;
            var candidates = new List<Vector2Int>();
            if (side == 0)
            {
                int doorY = lot.Top ? rect.yMin : rect.yMax - 1;
                inward = lot.Top ? Vector2Int.up : Vector2Int.down;
                var xs = new HashSet<int>();
                foreach (var f in floor) xs.Add(f.x);
                foreach (int x in xs)
                    if (x > rect.xMin && x < rect.xMax - 1) candidates.Add(new Vector2Int(x, doorY));
            }
            else
            {
                int doorX = side == 1 ? rect.xMin : rect.xMax - 1;
                inward = side == 1 ? Vector2Int.right : Vector2Int.left;
                var ys = new HashSet<int>();
                foreach (var f in floor) ys.Add(f.y);
                foreach (int y in ys)
                    if (y > rect.yMin && y < rect.yMax - 1) candidates.Add(new Vector2Int(doorX, y));
            }
            if (candidates.Count == 0) return false;
            candidates.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

            door = candidates[rng.Next(candidates.Count)];
            outside = door - inward;
            inside = door + inward;
            if (!InBounds(outside.x, outside.y) || Tiles[outside.x, outside.y] != Tile.Corridor) return false;

            var p = inside;
            int guard = 0;
            while (!floor.Contains(p))
            {
                if (!rect.Contains(p) || guard++ > 40) return false;
                floor.Add(p);
                p += inward;
            }
            return true;
        }

        /// <summary>Bed at the far end, a reserved walkway to it, and the build tiles around.</summary>
        bool FurnishRoom(RoomDef def)
        {
            var prev = new Dictionary<Vector2Int, Vector2Int>();
            var dist = new Dictionary<Vector2Int, int>();
            var q = new Queue<Vector2Int>();
            q.Enqueue(def.DoorInside);
            dist[def.DoorInside] = 0;
            prev[def.DoorInside] = def.DoorInside;
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                foreach (var d in Dirs4)
                {
                    var n = c + d;
                    if (!def.FloorSet.Contains(n) || dist.ContainsKey(n)) continue;
                    dist[n] = dist[c] + 1;
                    prev[n] = c;
                    q.Enqueue(n);
                }
            }
            if (dist.Count != def.Floor.Count) return false;

            int far = -1;
            foreach (var f in def.Floor)
                if (dist[f] > far || (dist[f] == far && rng.Next(3) == 0)) { far = dist[f]; def.BedTile = f; }

            var step = def.BedTile;
            while (step != def.DoorInside)
            {
                step = prev[step];
                def.Walkway.Add(step);
            }
            def.Walkway.Add(def.DoorInside);

            // every floor tile can hold a building, except the bed and the tile right inside the door.
            // (GameManager.CanBuildAt refuses placements that would wall the bed off.)
            foreach (var f in def.Floor)
                if (f != def.BedTile && f != def.DoorInside) def.BuildTiles.Add(f);
            if (def.BuildTiles.Count < 2) return false;
            def.BuildTiles.Sort((a, b) => a.y != b.y ? b.y.CompareTo(a.y) : a.x.CompareTo(b.x));
            return true;
        }

        // ------------------------------------------------------------------ queries

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;

        public Tile Get(int x, int y) => InBounds(x, y) ? Tiles[x, y] : Tile.Void;

        public RoomDef RoomAtDoor(Vector2Int t) => roomByDoor.TryGetValue(t, out var r) ? r : null;

        public RoomDef RoomContaining(Vector2Int t)
        {
            if (!InBounds(t.x, t.y)) return null;
            int id = roomId[t.x, t.y];
            return id >= 0 ? Rooms[id] : null;
        }

        public RoomDef RoomContainingWorld(Vector2 world) => RoomContaining(ToTile(world));

        public static Vector2Int ToTile(Vector2 world) => new Vector2Int(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y));

        public static Vector2 Center(Vector2Int t) => new Vector2(t.x + 0.5f, t.y + 0.5f);

        /// <summary>All corridor tiles (where body parts can spawn and the monster roams).</summary>
        public List<Vector2Int> CorridorTiles()
        {
            var list = new List<Vector2Int>();
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    if (Tiles[x, y] == Tile.Corridor) list.Add(new Vector2Int(x, y));
            return list;
        }
    }

    /// <summary>Letter bitmaps the rooms grow from. Rows are written top to bottom; every letter is 4-connected.</summary>
    public static class LetterShapes
    {
        static readonly Dictionary<string, string[]> Shapes = new Dictionary<string, string[]>
        {
            { "L", new[] { "#.", "#.", "##" } },
            { "T", new[] { "###", ".#.", ".#." } },
            { "U", new[] { "#.#", "#.#", "###" } },
            { "C", new[] { "##", "#.", "##" } },
            { "E", new[] { "##", "#.", "##", "#.", "##" } },
            { "F", new[] { "##", "#.", "##", "#." } },
            { "H", new[] { "#.#", "###", "#.#" } },
            { "J", new[] { ".#", ".#", "##" } },
            { "O", new[] { "###", "#.#", "###" } },
            { "P", new[] { "##", "##", "#." } },
            { "S", new[] { "###", "#..", "###", "..#", "###" } },
            { "Z", new[] { "##.", ".#.", ".##" } },
            { "I", new[] { "#", "#", "#" } },
            { "Y", new[] { "#.#", "###", ".#." } },
            { "A", new[] { "###", "#.#", "###", "#.#" } },
            { "G", new[] { "###", "#..", "#.#", "###" } },
        };

        /// <summary>[column, row] with row 0 at the bottom, or null for an unknown letter.</summary>
        public static bool[,] Get(string letter)
        {
            if (string.IsNullOrEmpty(letter) || !Shapes.TryGetValue(letter.ToUpperInvariant(), out var rows)) return null;
            int h = rows.Length, w = rows[0].Length;
            var b = new bool[w, h];
            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                    b[c, h - 1 - r] = rows[r][c] == '#';
            return b;
        }

        public static bool[,] Transpose(bool[,] b)
        {
            int w = b.GetLength(0), h = b.GetLength(1);
            var t = new bool[h, w];
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++) t[y, x] = b[x, y];
            return t;
        }

        public static bool[,] FlipX(bool[,] b)
        {
            int w = b.GetLength(0), h = b.GetLength(1);
            var t = new bool[w, h];
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++) t[w - 1 - x, y] = b[x, y];
            return t;
        }

        public static bool[,] FlipY(bool[,] b)
        {
            int w = b.GetLength(0), h = b.GetLength(1);
            var t = new bool[w, h];
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++) t[x, h - 1 - y] = b[x, y];
            return t;
        }
    }
}
