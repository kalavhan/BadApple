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
        bool[,] protectedRoom;

        bool TryGenerate(int roomCount)
        {
            Tiles = new Tile[W, H];
            roomId = new int[W, H];
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++) roomId[x, y] = -1;
            Rooms.Clear(); roomByDoor.Clear(); VerticalX.Clear();
            DeadEnds.Clear(); Spine.Clear(); reservedLots.Clear();

            int centralCount = Mathf.Min(cfg.centralRoomCount, roomCount);
            bool joinedPair = roomCount >= centralCount + 2 && new System.Random(Seed ^ 0x52a19).NextDouble() < cfg.conjoinedPairChance;
            // Four islands occupy the central quadrants. Their clearance reserves a complete
            // corridor ring, rather than relying on a route happening to pass each side.
            for (int i = 0; i < centralCount; i++)
            {
                int w = R(cfg.roomInteriorMin.x + 2, cfg.roomInteriorMax.x + 2);
                int h = R(cfg.roomInteriorMin.y + 2, cfg.roomInteriorMax.y + 2);
                int cx = Mathf.RoundToInt(W * (i % 2 == 0 ? .39f : .61f)) + R(-2, 2);
                int cy = Mathf.RoundToInt(H * (i / 2 == 0 ? .35f : .65f)) + R(-1, 1);
                var rect = new RectInt(cx - w / 2, cy - h / 2, w, h);
                if (!LotFits(rect)) return false;
                reservedLots.Add(rect);
            }
            if (joinedPair)
            {
                int w = R(8, 10), h = R(7, 9);
                int x = R(0, 1) == 0 ? 5 : W - w - 5;
                int y = R(6, H - h * 2 - 5);
                var first = new RectInt(x, y, w, h);
                var second = new RectInt(x, y + h - 1, w, R(7, 9));
                if (!LotFits(first) || !LotFits(second)) return false;
                reservedLots.Add(first); reservedLots.Add(second);
            }
            for (int tries = 0; reservedLots.Count < roomCount && tries < 1600; tries++)
            {
                int w = R(cfg.roomInteriorMin.x + 2, cfg.roomInteriorMax.x + 2), h = R(cfg.roomInteriorMin.y + 2, cfg.roomInteriorMax.y + 2);
                if ((w-2)*(h-2)-(w+h-5) < cfg.roomMinBuildTiles) continue;
                var rect = new RectInt(R(4, W - w - 4), R(4, H - h - 4), w, h);
                // The remaining six rooms belong to the outer wings.
                if (rect.center.x > W * .26f && rect.center.x < W * .74f &&
                    rect.center.y > H * .20f && rect.center.y < H * .80f) continue;
                if (LotFits(rect)) reservedLots.Add(rect);
            }
            if (reservedLots.Count != roomCount) return false;
            var budgets = (int[])cfg.roomBuildTiles.Clone();
            for (int i = budgets.Length - 1; i > 0; i--)
            {
                int j = R(0, i); int temp = budgets[i]; budgets[i] = budgets[j]; budgets[j] = temp;
            }
            for (int i = 0; i < reservedLots.Count; i++)
            {
                var rect = reservedLots[i];
                int side = ChooseDoorSide(rect);
                var lot = new Lot { Rect = rect, Top = side == 0, DoorSide = side };
                if (joinedPair && (i == centralCount || i == centralCount + 1))
                {
                    lot.DoorSide = R(1, 2); // Never open a doorway into the shared wall.
                    int y = i == centralCount ? rect.yMax - 2 : rect.yMin + 1;
                    for (int x = rect.xMin + 1; x < rect.xMax - 1; x++) lot.KeepFloor.Add(new Vector2Int(x, y));
                }
                if (!BuildRoom(lot, budgets[Rooms.Count])) return false;
                Rooms[i].IsCentral = i < centralCount;
            }
            ProtectRoomOutlines();
            foreach (var room in Rooms) if (room.IsCentral) CarveRoomRing(room);

            foreach (var a in Rooms)
            {
                int nearest = int.MaxValue;
                foreach (var b in Rooms)
                    if (a != b) nearest = Mathf.Min(nearest, Manhattan(a.DoorOutside, b.DoorOutside));
                if (nearest < cfg.minDoorDistance || nearest > cfg.maxNearestDoorDistance) return false;
                a.NearestDoorDistance = nearest;
                a.Isolated = nearest > cfg.isolatedDoorDistance;
            }

            if (!Rooms.Exists(room=>!room.Isolated) || !Rooms.Exists(room=>room.Isolated)) return false;

            // The spine is a graph of offset waypoints. Routing reserves room outlines, so branches cannot
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

            // Merge accidentally adjacent hallway strips and bring hallway faces up to a
            // single reserved room-wall cell. Room floors and their wall shell never move.
            foreach(var room in Rooms) Tiles[room.DoorTile.x,room.DoorTile.y]=Tile.Door;
            RemoveDoubleSeparators();

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

        bool HasNearbyRoomPair()
        {
            for(int i=0;i<Rooms.Count;i++)for(int j=i+1;j<Rooms.Count;j++)
                if(Manhattan(Rooms[i].DoorOutside,Rooms[j].DoorOutside)<=cfg.isolatedDoorDistance)return true;
            return false;
        }

        int ChooseDoorSide(RectInt rect)
        {
            if(Rooms.Count==0||HasNearbyRoomPair())return R(0,3);
            var sides=new List<int>();
            for(int side=0;side<4;side++)
            {
                int start=side==0||side==3?rect.xMin+1:rect.yMin+1;
                int end=side==0||side==3?rect.xMax-1:rect.yMax-1;
                for(int at=start;at<end;at++)
                {
                    var outside=side==0?new Vector2Int(at,rect.yMin-1):side==3?new Vector2Int(at,rect.yMax):
                        side==1?new Vector2Int(rect.xMin-1,at):new Vector2Int(rect.xMax,at);
                    int nearest=int.MaxValue;foreach(var room in Rooms)nearest=Mathf.Min(nearest,Manhattan(outside,room.DoorOutside));
                    if(nearest>=cfg.minDoorDistance&&nearest<=cfg.isolatedDoorDistance){sides.Add(side);break;}
                }
            }
            return sides.Count>0?sides[R(0,sides.Count-1)]:R(0,3);
        }

        bool LotFits(RectInt rect)
        {
            if (rect.xMin < 3 || rect.yMin < 3 || rect.xMax > W - 3 || rect.yMax > H - 3) return false;
            int gap = Mathf.Max(3, cfg.corridorWidth);
            var clearance = new RectInt(rect.x - gap, rect.y - gap, rect.width + gap * 2, rect.height + gap * 2);
            foreach (var other in reservedLots) if (clearance.Overlaps(other)) return false;
            return true;
        }

        void ProtectRoomOutlines()
        {
            protectedRoom = new bool[W, H];
            foreach (var room in Rooms)
            {
                foreach (var f in room.Floor)
                {
                    protectedRoom[f.x, f.y] = true;
                    foreach (var direction in Dirs4) protectedRoom[f.x + direction.x, f.y + direction.y] = true;
                }
                protectedRoom[room.DoorTile.x, room.DoorTile.y] = true;
                var inward=room.DoorInside-room.DoorTile;var tangent=new Vector2Int(-inward.y,inward.x);
                foreach(int side in new[]{-1,1})
                {var jamb=room.DoorTile+tangent*side;protectedRoom[jamb.x,jamb.y]=true;}
            }
        }

        void CarveRoomRing(RoomDef room)
        {
            int reach = 1 + cfg.corridorWidth;
            foreach (var floor in room.Floor)
                for (int dx = -reach; dx <= reach; dx++) for (int dy = -reach; dy <= reach; dy++)
                {
                    var p = floor + new Vector2Int(dx, dy);
                    if (RouteOpen(p)) Tiles[p.x, p.y] = Tile.Corridor;
                }
        }

        void RemoveDoubleSeparators()
        {
            bool changed;
            do
            {
                changed = false;
                for (int x = 1; x < W - 1; x++) for (int y = 1; y < H - 1; y++)
                    foreach (var direction in new[] { Vector2Int.right, Vector2Int.up })
                    {
                        var a = new Vector2Int(x, y); var b = a + direction;
                        var left = a - direction; var right = b + direction;
                        if (!InBounds(right.x, right.y) || WallGraph.IsWalkable(Get(a.x, a.y)) || WallGraph.IsWalkable(Get(b.x, b.y)) ||
                            !WallGraph.IsWalkable(Get(left.x, left.y)) || !WallGraph.IsWalkable(Get(right.x, right.y))) continue;
                        foreach (var cell in new[] { a, b })
                            if (RouteOpen(cell)) { Tiles[cell.x, cell.y] = Tile.Corridor; changed = true; }
                    }
            } while (changed);
        }

        static int Manhattan(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

        bool RouteOpen(Vector2Int p)
        {
            if (p.x < 2 || p.y < 2 || p.x >= W - 2 || p.y >= H - 2) return false;
            if (protectedRoom[p.x, p.y]) return false;
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
