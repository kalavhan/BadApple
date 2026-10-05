using System;
using System.Collections.Generic;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>A local, straight opening for sight only. Physical walls remain solid.</summary>
    public sealed class WallPeek
    {
        public readonly RoomDef Room;
        public readonly Vector2Int CenterCell, Normal;
        public readonly IReadOnlyList<Vector2Int> Cells;
        public readonly Rect Bounds;

        internal WallPeek(RoomDef room, Vector2Int center, Vector2Int normal, Vector2Int[] cells)
        {
            Room = room; CenterCell = center; Normal = normal; Cells = cells;
            int xmin = center.x, xmax = center.x, ymin = center.y, ymax = center.y;
            foreach (var cell in cells)
            {
                xmin = Mathf.Min(xmin, cell.x); xmax = Mathf.Max(xmax, cell.x);
                ymin = Mathf.Min(ymin, cell.y); ymax = Mathf.Max(ymax, cell.y);
            }
            Bounds = Rect.MinMaxRect(xmin, ymin, xmax + 1, ymax + 1);
        }

        public bool ContainsCell(int x, int y)
        {
            foreach (var cell in Cells) if (cell.x == x && cell.y == y) return true;
            return false;
        }

        public bool IsAdjacent(Vector2 position)
        {
            if (HotelMap.ToTile(position) != CenterCell + Normal) return false;
            var outerEdge = HotelMap.Center(CenterCell) + (Vector2)Normal * .5f;
            float distance = Vector2.Dot(position - outerEdge, Normal);
            return distance >= 0 && distance <= WallSight.AdjacentDistance + .0001f;
        }
    }

    /// <summary>Monster peeking never changes movement, attacks, or the opacity of other walls.</summary>
    public static class WallSight
    {
        public const float AdjacentDistance = 1f;
        static readonly Vector2Int[] Directions = { Vector2Int.down, Vector2Int.left, Vector2Int.up, Vector2Int.right };

        public static RoomDef RoomAt(HotelMap map, Vector2Int cell)
        {
            if (map == null || map.Get(cell.x, cell.y) != Tile.RoomFloor) return null;
            foreach (var room in map.Rooms) if (room.ContainsInterior(cell)) return room;
            return null;
        }

        public static WallPeek FindPeek(HotelMap map, Vector2 monsterPosition)
        {
            if (map == null) return null;
            var outside = HotelMap.ToTile(monsterPosition);
            if (map.Get(outside.x, outside.y) != Tile.Corridor) return null;
            RoomDef selectedRoom = null;
            Vector2Int selectedCell = default, selectedNormal = default;
            float nearest = float.PositiveInfinity;
            foreach (var direction in Directions)
            {
                var wall = outside + direction;
                if (map.Get(wall.x, wall.y) != Tile.Wall) continue;
                var room = RoomAt(map, wall + direction);
                if (room == null) continue;
                var normal = -direction;
                var edge = HotelMap.Center(wall) + (Vector2)normal * .5f;
                float distance = Vector2.Dot(monsterPosition - edge, normal);
                if (distance < 0 || distance > AdjacentDistance + .0001f || distance >= nearest) continue;
                nearest = distance; selectedRoom = room; selectedCell = wall; selectedNormal = normal;
            }
            if (selectedRoom == null) return null;

            var cells = new List<Vector2Int>(3);
            var tangent = new Vector2Int(-selectedNormal.y, selectedNormal.x);
            for (int offset = -1; offset <= 1; offset++)
            {
                var wall = selectedCell + tangent * offset;
                var inside = wall - selectedNormal;
                var corridor = wall + selectedNormal;
                // A window stops at a doorway, an outline turn, or a different room.
                if (map.Get(wall.x, wall.y) == Tile.Wall && selectedRoom.ContainsInterior(inside) &&
                    map.Get(inside.x, inside.y) == Tile.RoomFloor && map.Get(corridor.x, corridor.y) == Tile.Corridor)
                    cells.Add(wall);
            }
            return new WallPeek(selectedRoom, selectedCell, selectedNormal, cells.ToArray());
        }

        static bool AdjacentToRoom(HotelMap map, Vector2 position, RoomDef room, WallPeek peek)
        {
            if (peek != null && peek.Room == room && peek.IsAdjacent(position)) return true;
            var cell = HotelMap.ToTile(position);
            // Doorways count as a local room boundary, but their closed-door opacity below
            // remains intact. Standing farther down the hall does not reveal the room.
            return (cell == room.DoorOutside && map.Get(cell.x, cell.y) == Tile.Corridor) ||
                (cell == room.DoorTile && map.Get(cell.x, cell.y) == Tile.Door);
        }

        public static bool CanSee(HotelMap map, Vector2 from, Vector2 to, float radius,
            Func<int, int, bool> opaque, WallPeek peek, bool monsterObserver, bool monsterTarget,
            bool seeTargetWall = false)
        {
            if (map == null || radius < 0 || (to - from).sqrMagnitude > radius * radius) return false;
            var fromCell = HotelMap.ToTile(from); var toCell = HotelMap.ToTile(to);
            var fromRoom = RoomAt(map, fromCell); var toRoom = RoomAt(map, toCell);
            if (monsterObserver && toRoom != null && fromRoom != toRoom && !AdjacentToRoom(map, from, toRoom, peek)) return false;
            if (monsterTarget && fromRoom != null && fromRoom != toRoom && !AdjacentToRoom(map, to, fromRoom, peek)) return false;
            if (map.Get(fromCell.x, fromCell.y) == Tile.Door && opaque(fromCell.x, fromCell.y)) return false;
            bool useOpening = peek != null &&
                ((monsterObserver && peek.IsAdjacent(from)) || fromRoom == peek.Room);
            return Sight.Clear(from, to, (x, y) => !(useOpening && peek.ContainsCell(x, y)) && opaque(x, y), seeTargetWall);
        }
    }
}
