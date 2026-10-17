using System.Collections.Generic;
using BadAppleHotel.Config;
using UnityEngine;

namespace BadAppleHotel.Game
{
    public partial class RoomDef
    {
        // ------------------------------------------------------------------ hallway floors (tower defense)
        /// <summary>The floor's square of the shared grid, margins included.</summary>
        public RectInt FloorBounds;
        /// <summary>The carpet the monster and its minions walk, in order from the lift to the door.</summary>
        public readonly List<Vector2Int> Lane = new List<Vector2Int>();
        /// <summary>Carpet square in front of the lift doors, where minions spawn and the monster arrives.</summary>
        public Vector2Int LiftLanding;
        /// <summary>Wall square that holds the lift doors (just behind the landing).</summary>
        public Vector2Int LiftTile;
        /// <summary>Tight box around the floor's walls, for framing the camera.</summary>
        public RectInt ViewBounds;
        public int FloorNumber => Index + 1;
        public string RoomNumber => FloorNumber + "01";
    }

    /// <summary>
    /// The tower-defense hotel: every guest room sits on a floor of its own. A floor is a carpeted lane that winds back
    /// and forth from the lift to the room's door, with wooden build pads either side of the carpet and walls around
    /// the hallway. Lanes, turns, orientation and mirroring are rolled per floor, so every floor plays differently.
    /// The floors share one tile grid (side by side, never connected), which keeps walls, lighting, movement and the
    /// floor shader unchanged.
    /// </summary>
    public partial class HotelMap
    {
        /// <summary>True for the hallway tower-defense layout; false for the older single connected hotel.</summary>
        public bool IsFloors { get; private set; }
        readonly HashSet<Vector2Int> carpet = new HashSet<Vector2Int>();
        readonly Dictionary<Vector2Int, RoomDef> padOwner = new Dictionary<Vector2Int, RoomDef>();
        int[,] floorId;

        /// <summary>Carpet squares (the walking lane and lift landing). In the old layout every corridor square counts.</summary>
        public bool IsCarpet(int x, int y) => IsFloors ? carpet.Contains(new Vector2Int(x, y)) : Get(x, y) == Tile.Corridor;
        public bool IsCarpet(Vector2Int t) => IsCarpet(t.x, t.y);

        /// <summary>The floor whose build pad this square is, or null.</summary>
        public RoomDef PadOwner(Vector2Int t) => padOwner.TryGetValue(t, out var def) ? def : null;

        /// <summary>The floor this square belongs to (hallway, pads, room or walls), or null.</summary>
        public RoomDef FloorOf(Vector2Int t)
        {
            if (!IsFloors || !InBounds(t.x, t.y)) return null;
            int id = floorId[t.x, t.y];
            return id >= 0 ? Rooms[id] : null;
        }
        public RoomDef FloorOfWorld(Vector2 world) => FloorOf(ToTile(world));

        public static HotelMap Floors(FloorsConfig cfg, int floorCount, int seed) => new HotelMap(cfg, floorCount, seed);

        HotelMap(FloorsConfig fc, int floorCount, int seed)
        {
            IsFloors = true;
            Seed = seed;
            int columns = Mathf.Max(1, fc.columns), rows = (floorCount + columns - 1) / columns;
            // The storeys share one tile grid but sit apart, never touching; only the watched one is lit.
            int pitch = fc.cellSize + Mathf.Max(0, fc.gap);
            W = columns * pitch;
            H = rows * pitch;
            Tiles = new Tile[W, H];
            roomId = new int[W, H];
            floorId = new int[W, H];
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++) { roomId[x, y] = -1; floorId[x, y] = -1; }
            for (int i = 0; i < floorCount; i++)
            {
                // Floor 1 at the bottom left, reading like a building directory.
                var origin = new Vector2Int(i % columns * pitch, i / columns * pitch);
                bool built = false;
                for (int attempt = 0; attempt < 40 && !built; attempt++)
                {
                    rng = new System.Random(unchecked(seed * 31 + i * 7919 + attempt * 104729));
                    built = TryBuildFloor(fc, i, origin);
                }
                if (!built) throw new System.InvalidOperationException("Could not lay out floor " + (i + 1) + "; check map.json floors.");
            }
            foreach (var def in Rooms)
            {
                roomByDoor[def.DoorTile] = def;
                Tiles[def.DoorTile.x, def.DoorTile.y] = Tile.Door;
            }
            Lobby = Rooms[0].LiftLanding;
            MonsterSpawn = Rooms[0].LiftLanding;
        }

        /// <summary>Lays one floor out in local coordinates, then mirrors/transposes it into its grid square.</summary>
        bool TryBuildFloor(FloorsConfig fc, int index, Vector2Int origin)
        {
            int s = fc.cellSize, lw = Mathf.Max(1, fc.laneWidth), pad = Mathf.Max(1, fc.padDepth);
            const int margin = 2;
            int roomW = fc.roomInterior.x, roomH = fc.roomInterior.y;
            int block = roomW + 2;                       // room interior plus its wall ring, beside the lanes
            int laneMin = margin + block, laneMax = s - margin - block - 1;
            if (laneMax - laneMin < 8) return false;

            int lanes = R(Mathf.Max(2, fc.lanesMin), Mathf.Max(fc.lanesMin, fc.lanesMax));
            int usable = s - 2 * margin - 2 * pad - lw;
            int pitch = Mathf.Min(fc.lanePitchMax, usable / Mathf.Max(1, lanes - 1));
            if (pitch < lw + 3) return false;
            int stack = (lanes - 1) * pitch + lw;
            int y0 = margin + pad + (s - 2 * margin - 2 * pad - stack) / 2;

            // Local plan: lane i runs along rows y0 + i*pitch .. +lw-1; even lanes left to right.
            var local = new HashSet<Vector2Int>();
            var order = new List<Vector2Int>();
            void Lay(Vector2Int p) { if (local.Add(p)) order.Add(p); }
            void Strip(int xa, int xb, int ya, int yb, bool reverseX, bool reverseY)
            {
                // A lw-wide strip, appended in walking order.
                int xs = reverseX ? xb : xa, xe = reverseX ? xa : xb, dx = reverseX ? -1 : 1;
                int ys = reverseY ? yb : ya, ye = reverseY ? ya : yb, dy = reverseY ? -1 : 1;
                for (int x = xs; x != xe + dx; x += dx)
                    for (int y = ys; y != ye + dy; y += dy) Lay(new Vector2Int(x, y));
            }

            int jitter = Mathf.Max(0, fc.turnJitter);
            int startX = laneMin - 1, prevTurn = startX;
            int liftRow = y0;
            for (int i = 0; i < lanes; i++)
            {
                int y = y0 + i * pitch;
                bool rightward = i % 2 == 0;
                bool last = i == lanes - 1;
                int end;
                if (last) end = rightward ? laneMax + 1 : laneMin - 1;
                else end = rightward ? laneMax - R(0, jitter) : laneMin + R(0, jitter);
                int from = i == 0 ? startX : prevTurn;
                Strip(Mathf.Min(from, end), Mathf.Max(from, end), y, y + lw - 1, !rightward, false);
                if (!last)
                {
                    // Turn: a lw-wide strip up to the next lane, at this lane's end.
                    int tx = rightward ? end - lw + 1 : end;
                    Strip(tx, tx + lw - 1, y + lw, y + pitch - 1, false, false);
                    prevTurn = rightward ? end : end;
                }
                else prevTurn = end;
            }
            bool endsRight = (lanes - 1) % 2 == 0;
            int lastRow = y0 + (lanes - 1) * pitch;

            // Room beside the last lane's end; the lift beside the first lane's start (the left side).
            int roomX = endsRight ? laneMax + 3 : laneMin - 2 - roomW;
            int roomY = Mathf.Clamp(lastRow - (roomH - lw) / 2, margin + 1, s - margin - 1 - roomH);
            var interior = new RectInt(roomX, roomY, roomW, roomH);
            var doorLocal = new Vector2Int(endsRight ? roomX - 1 : roomX + roomW, lastRow);
            var doorOutsideLocal = new Vector2Int(endsRight ? roomX - 2 : roomX + roomW + 1, lastRow);
            var doorInsideLocal = new Vector2Int(endsRight ? roomX : roomX + roomW - 1, lastRow);
            // Join the lane end to the doorway.
            var tail = order[order.Count - 1];
            for (int x = Mathf.Min(tail.x, doorOutsideLocal.x); x <= Mathf.Max(tail.x, doorOutsideLocal.x); x++)
                for (int y = lastRow; y < lastRow + lw; y++) Lay(new Vector2Int(x, y));
            var liftLanding = new Vector2Int(startX, liftRow);
            var liftWall = new Vector2Int(startX - 1, liftRow);
            if (endsRight == false && interior.Contains(liftWall)) return false;

            // Ring of the room (local) for overlap checks.
            var ring = new RectInt(interior.x - 1, interior.y - 1, interior.width + 2, interior.height + 2);
            foreach (var c in local)
                if (ring.Contains(c) && c != doorLocal) return false;

            // Random orientation per floor: transpose, then mirror.
            bool transpose = rng.NextDouble() < fc.verticalChance, flipX = rng.Next(2) == 0, flipY = rng.Next(2) == 0;
            Vector2Int Map(Vector2Int p)
            {
                if (transpose) p = new Vector2Int(p.y, p.x);
                if (flipX) p.x = s - 1 - p.x;
                if (flipY) p.y = s - 1 - p.y;
                return origin + p;
            }

            var def = new RoomDef { Index = Rooms.Count, Letter = ' ', FloorBounds = new RectInt(origin.x, origin.y, s, s) };
            for (int x = interior.xMin; x < interior.xMax; x++)
                for (int y = interior.yMin; y < interior.yMax; y++)
                {
                    var f = Map(new Vector2Int(x, y));
                    def.Floor.Add(f); def.FloorSet.Add(f);
                }
            def.Floor.Sort((a, b) => a.y != b.y ? b.y.CompareTo(a.y) : a.x.CompareTo(b.x));
            def.DoorTile = Map(doorLocal);
            def.DoorInside = Map(doorInsideLocal);
            def.DoorOutside = Map(doorOutsideLocal);
            def.LiftLanding = Map(liftLanding);
            def.LiftTile = Map(liftWall);
            var c0 = Map(ring.min); var c1 = Map(ring.max - Vector2Int.one);
            def.Lot = new RectInt(Mathf.Min(c0.x, c1.x), Mathf.Min(c0.y, c1.y), ring.width, ring.height);
            if (!FurnishRoom(def)) return false;
            // Nothing is built inside the room: its build squares are the hallway pads.
            def.BuildTiles.Clear();
            def.Walkway.Clear();

            foreach (var c in order) { var t = Map(c); def.Lane.Add(t); carpet.Add(t); Tiles[t.x, t.y] = Tile.Corridor; }
            foreach (var f in def.Floor) { Tiles[f.x, f.y] = Tile.RoomFloor; roomId[f.x, f.y] = def.Index; }

            // Pads: hallway squares near the carpet, kept off the room and the lift landing.
            var ringWorld = new HashSet<Vector2Int>();
            for (int x = ring.xMin; x < ring.xMax; x++) for (int y = ring.yMin; y < ring.yMax; y++) ringWorld.Add(Map(new Vector2Int(x, y)));
            var pads = new HashSet<Vector2Int>();
            foreach (var c in local)
                for (int dx = -pad; dx <= pad; dx++)
                    for (int dy = -pad; dy <= pad; dy++)
                    {
                        var q = new Vector2Int(c.x + dx, c.y + dy);
                        if (q.x < margin || q.y < margin || q.x >= s - margin || q.y >= s - margin) continue;
                        if (local.Contains(q) || q.x < startX) continue;
                        var w = Map(q);
                        if (ringWorld.Contains(w)) continue;
                        // The lift's doorway stays clear: plain hallway boards, never a pad.
                        if (Mathf.Abs(q.x - liftLanding.x) <= 1 && q.y >= liftRow - 1 && q.y <= liftRow + lw) { Tiles[w.x, w.y] = Tile.Corridor; continue; }
                        pads.Add(w);
                    }
            foreach (var p in pads)
            {
                Tiles[p.x, p.y] = Tile.Corridor;
                padOwner[p] = def;
                def.BuildTiles.Add(p);
            }
            def.BuildTiles.Sort((a, b) => a.y != b.y ? b.y.CompareTo(a.y) : a.x.CompareTo(b.x));
            def.BuildBudget = def.BuildTiles.Count;

            // Walls around everything walkable on this floor (including the room), and the lift's wall square.
            for (int x = origin.x; x < origin.x + s; x++)
                for (int y = origin.y; y < origin.y + s; y++)
                {
                    if (!WallGraph.IsWalkable(Tiles[x, y]) && !def.DoorTile.Equals(new Vector2Int(x, y))) continue;
                    floorId[x, y] = def.Index;
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (InBounds(nx, ny) && Tiles[nx, ny] == Tile.Void) { Tiles[nx, ny] = Tile.Wall; floorId[nx, ny] = def.Index; }
                        }
                }
            Tiles[def.DoorTile.x, def.DoorTile.y] = Tile.Wall; // becomes a Door once every floor is laid out
            floorId[def.DoorTile.x, def.DoorTile.y] = def.Index;
            int vx0 = int.MaxValue, vy0 = int.MaxValue, vx1 = int.MinValue, vy1 = int.MinValue;
            for (int x = origin.x; x < origin.x + s; x++)
                for (int y = origin.y; y < origin.y + s; y++)
                    if (floorId[x, y] == def.Index) { vx0 = Mathf.Min(vx0, x); vy0 = Mathf.Min(vy0, y); vx1 = Mathf.Max(vx1, x); vy1 = Mathf.Max(vy1, y); }
            def.ViewBounds = new RectInt(vx0, vy0, vx1 - vx0 + 1, vy1 - vy0 + 1);
            Vector2 sum = Vector2.zero;
            foreach (var f in def.Floor) sum += Center(f);
            def.Center = sum / def.Floor.Count;
            Rooms.Add(def);
            return true;
        }
    }
}
