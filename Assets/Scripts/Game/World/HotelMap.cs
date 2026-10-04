using System.Collections.Generic;
using UnityEngine;

namespace BadAppleHotel.Game
{
    public enum Tile { Void, Corridor, Wall, Door, RoomFloor }

    /// <summary>Static layout data for one guest room.</summary>
    public class RoomDef
    {
        public int Index;
        public RectInt Rect;            // includes the walls
        public Vector2Int DoorTile;     // on the wall
        public Vector2Int DoorOutside;  // corridor tile in front of the door
        public Vector2Int DoorInside;   // first floor tile inside
        public Vector2Int BedTile;
        public List<Vector2Int> Slots = new List<Vector2Int>();

        public bool ContainsInterior(Vector2Int t)
        {
            return t.x > Rect.xMin && t.x < Rect.xMax - 1 && t.y > Rect.yMin && t.y < Rect.yMax - 1;
        }
    }

    /// <summary>
    /// The hotel floor: a horizontal and a vertical corridor with 10 rooms. Some doors sit close together
    /// (R1/R2, R5/R6, R7/R8), some are isolated (R0, R4, R9), matching the bible's "entrances close or far" rule.
    /// One tile = one world unit; tile (x, y) covers [x, x+1] x [y, y+1].
    /// </summary>
    public class HotelMap
    {
        public const int W = 58;
        public const int H = 30;

        public readonly Tile[,] Tiles = new Tile[W, H];
        public readonly List<RoomDef> Rooms = new List<RoomDef>();
        public readonly Vector2Int MonsterSpawn = new Vector2Int(28, 14);
        readonly Dictionary<Vector2Int, RoomDef> roomByDoor = new Dictionary<Vector2Int, RoomDef>();

        public HotelMap()
        {
            // corridors
            for (int x = 0; x < W; x++)
                for (int y = 13; y <= 16; y++) Tiles[x, y] = Tile.Corridor;
            for (int y = 0; y < H; y++)
                for (int x = 27; x <= 30; x++) Tiles[x, y] = Tile.Corridor;

            // top rooms: door on the bottom wall (y = 17), facing the corridor
            AddRoom(0, 17, 9, 13, 4, true);
            AddRoom(9, 17, 9, 13, 16, true);
            AddRoom(18, 17, 9, 13, 19, true);
            AddRoom(31, 17, 13, 13, 37, true);
            AddRoom(44, 17, 14, 13, 55, true);
            // bottom rooms: door on the top wall (y = 12)
            AddRoom(0, 0, 13, 13, 11, false);
            AddRoom(13, 0, 14, 13, 14, false);
            AddRoom(31, 0, 9, 13, 35, false);
            AddRoom(40, 0, 9, 13, 44, false);
            AddRoom(49, 0, 9, 13, 52, false);
        }

        void AddRoom(int x, int y, int w, int h, int doorX, bool doorOnBottom)
        {
            var def = new RoomDef { Index = Rooms.Count, Rect = new RectInt(x, y, w, h) };
            for (int i = x; i < x + w; i++)
                for (int j = y; j < y + h; j++)
                {
                    bool border = i == x || j == y || i == x + w - 1 || j == y + h - 1;
                    Tiles[i, j] = border ? Tile.Wall : Tile.RoomFloor;
                }

            int doorY = doorOnBottom ? y : y + h - 1;
            def.DoorTile = new Vector2Int(doorX, doorY);
            def.DoorOutside = new Vector2Int(doorX, doorOnBottom ? doorY - 1 : doorY + 1);
            def.DoorInside = new Vector2Int(doorX, doorOnBottom ? doorY + 1 : doorY - 1);
            Tiles[doorX, doorY] = Tile.Door;

            int bedX = Mathf.Clamp(doorX + (doorX - x < w / 2 ? 3 : -3), x + 2, x + w - 3);
            def.BedTile = new Vector2Int(bedX, doorOnBottom ? y + h - 3 : y + 2);

            // tower slots: the 6 floor tiles closest to the door, keeping the door column clear as a walkway
            var candidates = new List<Vector2Int>();
            for (int i = x + 1; i < x + w - 1; i++)
                for (int j = y + 1; j < y + h - 1; j++)
                {
                    var t = new Vector2Int(i, j);
                    if (i == doorX) continue;
                    if (t == def.DoorInside) continue;
                    if (Mathf.Abs(i - def.BedTile.x) <= 1 && Mathf.Abs(j - def.BedTile.y) <= 1) continue;
                    candidates.Add(t);
                }
            candidates.Sort((a, b) =>
            {
                float da = (a - def.DoorTile).sqrMagnitude + Mathf.Abs(a.x - doorX) * 0.01f;
                float db = (b - def.DoorTile).sqrMagnitude + Mathf.Abs(b.x - doorX) * 0.01f;
                return da.CompareTo(db);
            });
            for (int k = 0; k < 6 && k < candidates.Count; k++) def.Slots.Add(candidates[k]);

            Rooms.Add(def);
            roomByDoor[def.DoorTile] = def;
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;

        public Tile Get(int x, int y) => InBounds(x, y) ? Tiles[x, y] : Tile.Void;

        public RoomDef RoomAtDoor(Vector2Int t) => roomByDoor.TryGetValue(t, out var r) ? r : null;

        public RoomDef RoomContaining(Vector2Int t)
        {
            foreach (var r in Rooms) if (r.ContainsInterior(t)) return r;
            return null;
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
}
