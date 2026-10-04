using System;
using System.Collections.Generic;
using UnityEngine;

namespace BadAppleHotel.Game
{
    public partial class HotelMap
    {
        public readonly List<Vector2Int> DeadEnds = new List<Vector2Int>();
        public readonly List<Vector2Int> Spine = new List<Vector2Int>();
        readonly List<RectInt> reservedLots = new List<RectInt>();

        bool TryGenerate(int roomCount)
        {
            Tiles = new Tile[W, H];
            roomId = new int[W, H];
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++) roomId[x, y] = -1;
            Rooms.Clear(); roomByDoor.Clear(); VerticalX.Clear();
            DeadEnds.Clear(); Spine.Clear(); reservedLots.Clear();

            // Irregular plots, separated by enough space for a full-width corridor and walls.
            for (int tries = 0; reservedLots.Count < roomCount && tries < 1600; tries++)
            {
                int w = R(cfg.roomInteriorMin.x + 2, cfg.roomInteriorMax.x + 2), h = R(cfg.roomInteriorMin.y + 2, cfg.roomInteriorMax.y + 2);
                if ((w-2)*(h-2)-(w+h-5) < cfg.roomMinBuildTiles) continue;
                var rect = new RectInt(R(4, W - w - 4), R(4, H - h - 4), w, h);
                var padded = new RectInt(rect.x - 3, rect.y - 3, w + 6, h + 6);
                bool overlap = false;
                foreach (var other in reservedLots) if (padded.Overlaps(other)) { overlap = true; break; }
                if (!overlap) reservedLots.Add(rect);
            }
            if (reservedLots.Count != roomCount) return false;
            foreach (var rect in reservedLots)
            {
                int side = R(0, 3);
                if (!BuildRoom(new Lot { Rect = rect, Top = side == 0, DoorSide = side })) return false;
            }

            foreach (var a in Rooms)
            {
                int nearest = int.MaxValue;
                foreach (var b in Rooms)
                    if (a != b) nearest = Mathf.Min(nearest, Manhattan(a.DoorOutside, b.DoorOutside));
                if (nearest < cfg.minDoorDistance || nearest > cfg.maxNearestDoorDistance) return false;
                a.NearestDoorDistance = nearest;
                a.Isolated = nearest > cfg.isolatedDoorDistance;
            }

            // The spine is a graph of offset waypoints. Routing reserves room plots, so branches cannot
            // punch accidental entrances through a room wall. Every subsequent edge joins this graph.
            for (int i = 0; i < 5; i++)
            {
                var target = new Vector2Int(3 + (W - 7) * i / 4, H / 2 + R(-H / 4, H / 4));
                var node = NearestOpen(target);
                if (i > 0 && !Connect(Spine[i - 1], node)) return false;
                Spine.Add(node);
            }
            Lobby = Spine[0]; MonsterSpawn = Spine[Spine.Count - 1]; CorridorY = Lobby.y;
            foreach (var room in Rooms)
            {
                var join = Spine[0]; int best = int.MaxValue;
                foreach (var node in Spine)
                {
                    int distance = Manhattan(node, room.DoorOutside);
                    if (distance < best) { best = distance; join = node; }
                }
                if (!Connect(room.DoorOutside, join)) return false;
            }

            // Additional edges take a detour through unused space, closing loops in the connected graph.
            for (int i = 0; i < cfg.loopCount; i++)
            {
                var a = Rooms[i % Rooms.Count].DoorOutside;
                var b = Rooms[(i + Rooms.Count / 2) % Rooms.Count].DoorOutside;
                var via = NearestOpen(new Vector2Int(R(3, W - 4), i % 2 == 0 ? 3 : H - 4));
                if (!Connect(a, via) || !Connect(via, b)) return false;
            }
            for (int i = 0; i < cfg.deadEndCount; i++)
            {
                var end = FarthestUnused();
                if (end.x < 0) break;
                var route = RouteToCorridor(end);
                if (route == null || route.Count < 6) break;
                Carve(route);
                DeadEnds.Add(end);
            }

            // Walls also surround hallways, including the ends of corridors.
            var walk = new List<Vector2Int>();
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    if (Tiles[x, y] == Tile.Corridor || Tiles[x, y] == Tile.RoomFloor) walk.Add(new Vector2Int(x, y));
            foreach (var f in walk)
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int x = f.x + dx, y = f.y + dy;
                        if (InBounds(x, y) && Tiles[x, y] == Tile.Void) Tiles[x, y] = Tile.Wall;
                    }
            foreach (var room in Rooms)
            {
                Tiles[room.DoorTile.x, room.DoorTile.y] = Tile.Door;
                roomByDoor[room.DoorTile] = room;
                if (Get(room.DoorOutside.x, room.DoorOutside.y) != Tile.Corridor) return false;
            }
            return true;
        }

        static int Manhattan(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

        bool RouteOpen(Vector2Int p)
        {
            if (p.x < 2 || p.y < 2 || p.x >= W - 2 || p.y >= H - 2) return false;
            foreach (var rect in reservedLots) if (rect.Contains(p)) return false;
            return true;
        }

        Vector2Int NearestOpen(Vector2Int target)
        {
            var best = new Vector2Int(2, 2); int distance = int.MaxValue;
            for (int x = 2; x < W - 2; x++)
                for (int y = 2; y < H - 2; y++)
                {
                    var p = new Vector2Int(x, y); int d = Manhattan(p, target);
                    if (d < distance && RouteOpen(p)) { best = p; distance = d; }
                }
            return best;
        }

        // Deterministic BFS with a per-edge direction order produces bent orthogonal paths.
        List<Vector2Int> Route(Vector2Int from, Func<Vector2Int, bool> goal)
        {
            var q = new Queue<Vector2Int>();
            var prev = new Dictionary<Vector2Int, Vector2Int> { [from] = from };
            q.Enqueue(from);
            int offset = R(0, 3);
            while (q.Count > 0)
            {
                var p = q.Dequeue();
                if (goal(p))
                {
                    var path = new List<Vector2Int> { p };
                    while (p != from) { p = prev[p]; path.Add(p); }
                    return path;
                }
                for (int i = 0; i < 4; i++)
                {
                    var n = p + Dirs4[(i + offset) % 4];
                    if (!RouteOpen(n) || prev.ContainsKey(n)) continue;
                    prev[n] = p; q.Enqueue(n);
                }
            }
            return null;
        }

        bool Connect(Vector2Int from, Vector2Int to)
        {
            var route = Route(from, p => p == to);
            if (route == null) return false;
            Carve(route); return true;
        }

        List<Vector2Int> RouteToCorridor(Vector2Int from) => Route(from, p => Get(p.x, p.y) == Tile.Corridor);

        void Carve(List<Vector2Int> route)
        {
            int radius = Mathf.Max(0, (cfg.corridorWidth - 1) / 2);
            foreach (var p in route)
                for (int dx = -radius; dx <= radius; dx++)
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        var n = p + new Vector2Int(dx, dy);
                        if (RouteOpen(n)) Tiles[n.x, n.y] = Tile.Corridor;
                    }
        }

        Vector2Int FarthestUnused()
        {
            // Multi-source distances from the existing network select meaningful branch lengths.
            var distance = new Dictionary<Vector2Int, int>();
            var q = new Queue<Vector2Int>();
            foreach (var p in CorridorTiles()) { distance[p] = 0; q.Enqueue(p); }
            var best = new Vector2Int(-1, -1); int far = 0;
            while (q.Count > 0)
            {
                var p = q.Dequeue();
                if (distance[p] > far) { far = distance[p]; best = p; }
                foreach (var d in Dirs4)
                {
                    var n = p + d;
                    if (!RouteOpen(n) || distance.ContainsKey(n)) continue;
                    distance[n] = distance[p] + 1; q.Enqueue(n);
                }
            }
            return best;
        }

        /// <summary>Spaced pickups, preferring dead ends and unclaimed rooms. Never weakens the spacing rule.</summary>
        public List<Vector2Int> BodyPartSpawns(int count, float spacing, int seed, IEnumerable<RoomDef> emptyRooms)
        {
            var preferred = new List<Vector2Int>(DeadEnds);
            foreach (var room in emptyRooms) preferred.AddRange(room.Floor);
            var candidates = new List<Vector2Int>(preferred);
            candidates.AddRange(CorridorTiles());
            candidates.RemoveAll(p => (p - MonsterSpawn).sqrMagnitude < 16 || Rooms.Exists(r => Manhattan(p, r.DoorTile) < 2));
            var random = new System.Random(seed);
            float minSq = spacing * spacing;
            for (int attempt = 0; attempt < 100; attempt++)
            {
                var placed = new List<Vector2Int>();
                for (int i = 0; i < count; i++)
                {
                    var options = new List<Vector2Int>();
                    foreach (var p in candidates)
                    {
                        bool allowed = true;
                        foreach (var other in placed) if ((p - other).sqrMagnitude < minSq) { allowed = false; break; }
                        if (allowed) options.Add(p);
                    }
                    if (options.Count == 0) break;
                    var ends = options.FindAll(p => DeadEnds.Contains(p));
                    var rooms = options.FindAll(p => Get(p.x, p.y) == Tile.RoomFloor);
                    // Alternate hiding places so one large empty room cannot monopolize the pool.
                    if (i % 2 == 0 && ends.Count > 0) options = ends;
                    else if (rooms.Count > 0) options = rooms;
                    placed.Add(options[random.Next(options.Count)]);
                }
                if (placed.Count == count) return placed;
            }
            throw new InvalidOperationException("Cannot place body parts at the configured minimum spacing.");
        }
    }
}
