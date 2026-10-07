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
        public Vector2Int BedTile;          // foot square, also the navigation target
        public Vector2Int BedHeadTile;      // adjacent square, always inside the room
        public Vector2 BedCenter => (HotelMap.Center(BedTile) + HotelMap.Center(BedHeadTile)) * 0.5f;
        public bool IsBedTile(Vector2Int tile) => tile == BedTile || tile == BedHeadTile;
        public readonly HashSet<Vector2Int> Walkway = new HashSet<Vector2Int>(); // default door -> bed path (used by bots)
        // Every floor square except the two-square bed and the doorway square. Guests arrange
        // freely; GameManager.CanBuildAt keeps some path from the doorway to the bed open.
        public readonly List<Vector2Int> BuildTiles = new List<Vector2Int>();
        /// <summary>How many towers fit while the default walkway stays clear: the room's build budget.</summary>
        public int BuildBudget;
        public bool IsCentral;              // one of the four corridor-surrounded central islands
        public bool Isolated;               // no other door nearby: gets a free building when claimed
        public int NearestDoorDistance;
        public Vector2 Center;
        public float BedRotation => Mathf.Atan2(BedHeadTile.y - BedTile.y, BedHeadTile.x - BedTile.x) * Mathf.Rad2Deg - 90f;
        public float DoorRotation => DoorInside.x > DoorTile.x ? -90f : DoorInside.x < DoorTile.x ? 90f : DoorInside.y < DoorTile.y ? 180f : 0f;

        public bool ContainsInterior(Vector2Int t) => FloorSet.Contains(t);
        public int BuildIndex(Vector2Int t) => BuildTiles.IndexOf(t);
    }

    /// <summary>Seeded corridor graph with crooked branches, loops, dead ends and natural guest rooms.</summary>
    public partial class HotelMap
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
            public int DoorSide;
            public bool LeftCorridor;   // a vertical corridor runs along its left side
            public bool RightCorridor;
            public readonly HashSet<Vector2Int> KeepFloor = new HashSet<Vector2Int>();
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

        bool BuildRoom(Lot lot, int buildBudget)
        {
            var rect = lot.Rect;
            var interior = new RectInt(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2);
            for (int tries = 0; tries < 64; tries++)
            {
                var floor = new HashSet<Vector2Int>();
                for (int x = interior.xMin; x < interior.xMax; x++)
                    for (int y = interior.yMin; y < interior.yMax; y++) floor.Add(new Vector2Int(x, y));
                if (!CarveDoor(lot, floor, out var door, out var inside, out var outside)) continue;

                // Peel corners into alcoves, stepped walls and L-shaped bays. Every remaining
                // square belongs to a 2x2 patch, so the outline never sprouts a one-wide arm.
                while (floor.Count >= buildBudget + 3)
                {
                    var def = new RoomDef { Index = Rooms.Count, Letter = ' ', Lot = rect,
                        DoorTile = door, DoorInside = inside, DoorOutside = outside };
                    foreach (var f in floor) { def.Floor.Add(f); def.FloorSet.Add(f); }
                    def.Floor.Sort((a, b) => a.y != b.y ? b.y.CompareTo(a.y) : a.x.CompareTo(b.x));
                    if (!FurnishRoom(def) || def.BuildBudget < buildBudget) break;
                    int xmin = int.MaxValue, ymin = int.MaxValue, xmax = 0, ymax = 0;
                    foreach (var f in floor) { xmin = Mathf.Min(xmin, f.x); ymin = Mathf.Min(ymin, f.y); xmax = Mathf.Max(xmax, f.x); ymax = Mathf.Max(ymax, f.y); }
                    int boxArea = (xmax - xmin + 1) * (ymax - ymin + 1);
                    if (def.BuildBudget == buildBudget && floor.Count < boxArea && floor.Count >= boxArea * 0.6f)
                    {
                        Vector2 sum = Vector2.zero;
                        foreach (var f in def.Floor)
                        {
                            Tiles[f.x, f.y] = Tile.RoomFloor; roomId[f.x, f.y] = def.Index; sum += Center(f);
                        }
                        def.Center = sum / def.Floor.Count;
                        Rooms.Add(def);
                        return true;
                    }
                    var corners = new List<Vector2Int>();
                    foreach (var f in def.Floor)
                    {
                        if (f == inside || lot.KeepFloor.Contains(f)) continue;
                        bool verticalEdge = !floor.Contains(f + Vector2Int.up) || !floor.Contains(f + Vector2Int.down);
                        bool horizontalEdge = !floor.Contains(f + Vector2Int.left) || !floor.Contains(f + Vector2Int.right);
                        if (verticalEdge && horizontalEdge) corners.Add(f);
                    }
                    bool trimmed = false;
                    while (corners.Count > 0)
                    {
                        int i = rng.Next(corners.Count); var cut = corners[i]; corners.RemoveAt(i);
                        floor.Remove(cut);
                        if (WideFloor(floor) && Connected(floor)) { trimmed = true; break; }
                        floor.Add(cut);
                    }
                    if (!trimmed) break;
                }
            }
            return false;
        }

        static bool WideFloor(HashSet<Vector2Int> floor)
        {
            foreach (var f in floor)
            {
                bool wide = false;
                for (int dx = -1; dx <= 1; dx += 2)
                    for (int dy = -1; dy <= 1; dy += 2)
                        if (floor.Contains(f + new Vector2Int(dx, 0)) && floor.Contains(f + new Vector2Int(0, dy)) &&
                            floor.Contains(f + new Vector2Int(dx, dy))) wide = true;
                if (!wide) return false;
            }
            return true;
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
            int side = lot.DoorSide; // bottom, left, right, top

            Vector2Int inward;
            var candidates = new List<Vector2Int>();
            if (side == 0 || side == 3)
            {
                int doorY = side == 0 ? rect.yMin : rect.yMax - 1;
                inward = side == 0 ? Vector2Int.up : Vector2Int.down;
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

            candidates.RemoveAll(c => !floor.Contains(c + inward));
            if (candidates.Count == 0) return false;
            var axis=inward;
            candidates.RemoveAll(c=>Rooms.Exists(room=>Manhattan(c-axis,room.DoorOutside)<cfg.minDoorDistance));
            if(candidates.Count==0)return false;
            if(!HasNearbyRoomPair())
            {
                var close=candidates.FindAll(c=>Rooms.Exists(room=>Manhattan(c-axis,room.DoorOutside)<=cfg.isolatedDoorDistance));
                if(close.Count>0)candidates=close;
            }
            door = candidates[rng.Next(candidates.Count)];
            outside = door - inward;
            inside = door + inward;
            if (!InBounds(outside.x, outside.y)) return false;

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
            foreach (var foot in def.Floor)
                foreach (var axis in Dirs4)
                {
                    var head = foot + axis;
                    if (foot == def.DoorInside || head == def.DoorInside || !def.FloorSet.Contains(head) ||
                        def.FloorSet.Contains(head + axis) || dist[head] <= dist[foot]) continue;
                    if (dist[foot] > far)
                    {
                        far = dist[foot]; def.BedTile = foot; def.BedHeadTile = head;
                    }
                }
            if (far < 0) return false;
            var step = def.BedTile;
            while (step != def.DoorInside)
            {
                step = prev[step]; def.Walkway.Add(step);
            }
            def.Walkway.Add(def.DoorInside);
            foreach (var f in def.Floor)
                if (!def.IsBedTile(f) && f != def.DoorInside) def.BuildTiles.Add(f);
            def.BuildBudget = def.Floor.Count - 2 - def.Walkway.Count;
            if (def.BuildBudget < 2) return false;
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
