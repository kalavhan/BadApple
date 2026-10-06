using System.Collections.Generic;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>Analytical orthographic camera rays against wall boxes, expressed in ground XY and height.</summary>
    public static class WallVisibility
    {
        static readonly Vector2 TowardCameraPerHeight = -(Vector2)HotelView3D.Forward / HotelView3D.Forward.z;

        /// <summary>In an occupied room, camera-near sides become baseboards; far sides stay full height.</summary>
        public static float RoomHeight(Vector2 inwardNormal) =>
            Vector2.Dot(inwardNormal, (Vector2)HotelView3D.Forward) > 0 ? WallGraph.DownHeight : WallGraph.FullHeight;

        /// <summary>Short doorway jambs follow the doorway face, not their perpendicular side faces.</summary>
        public static float RoomHeight(WallGraph graph, WallRun run, RoomDef room)
        {
            if (room == null) return RoomHeight(run.Normal);
            bool doorway = false;
            foreach (int edge in run.EdgeIds)
            {
                var floor = graph.Edges[edge].WalkableCell;
                // A merged run may continue into the room's actual perimeter. Preserve
                // that wall's orientation rather than rotating the entire side to the door.
                if (room.ContainsInterior(floor))
                {
                    float height = RoomHeight(run.Normal);
                    // An irregular room can have a far-facing return at its entrance.
                    // Lower that foreground notch when it hides another floor square
                    // in the same room; the true far backdrop casts outside the room.
                    if (height == WallGraph.FullHeight && CoversAny(run.Footprint, height, room.Floor, true))
                        height = WallGraph.DownHeight;
                    return height;
                }
                doorway |= floor == room.DoorTile;
            }
            return RoomHeight(doorway ? (Vector2)(room.DoorInside - room.DoorTile) : run.Normal);
        }

        public static bool BordersRoom(WallGraph graph, WallRun run, RoomDef room)
        {
            if (room == null) return false;
            foreach (int edge in run.EdgeIds)
            {
                var floor = graph.Edges[edge].WalkableCell;
                if (room.ContainsInterior(floor) || floor == room.DoorTile) return true;
            }
            return false;
        }

        /// <summary>True when a floor point's ray to the camera passes through the wall's box.</summary>
        public static bool CoversGround(Rect footprint, float height, Vector2 ground)
        {
            float enter = 0, leave = height;
            return height > 0 && Slab(ground.x, TowardCameraPerHeight.x, footprint.xMin, footprint.xMax, ref enter, ref leave) &&
                Slab(ground.y, TowardCameraPerHeight.y, footprint.yMin, footprint.yMax, ref enter, ref leave) &&
                leave > .0001f && enter < height - .0001f;
        }

        public static bool CoversAny(Rect footprint, float height, IEnumerable<Vector2Int> floor, bool wholeTiles = true)
        {
            Rect test = wholeTiles ? Expanded(footprint, .4999f) : footprint;
            foreach (var tile in floor)
                if (CoversGround(test, height, HotelMap.Center(tile))) return true;
            return false;
        }

        public static float DefaultHeight(HotelMap map, Rect footprint)
        {
            if (!CoversWalkable(map, footprint, WallGraph.FullHeight, true)) return WallGraph.FullHeight;
            return CoversWalkable(map, footprint, WallGraph.CutawayHeight, false) ? WallGraph.DownHeight : WallGraph.CutawayHeight;
        }

        public static bool CoversWalkable(HotelMap map, Rect footprint, float height, bool wholeTiles = false)
        {
            var shadow = -TowardCameraPerHeight * height;
            var bounds = Rect.MinMaxRect(Mathf.Min(footprint.xMin, footprint.xMin + shadow.x),
                Mathf.Min(footprint.yMin, footprint.yMin + shadow.y), Mathf.Max(footprint.xMax, footprint.xMax + shadow.x),
                Mathf.Max(footprint.yMax, footprint.yMax + shadow.y));
            Rect test = wholeTiles ? Expanded(footprint, .4999f) : footprint;
            for (int y = Mathf.Max(0, Mathf.FloorToInt(bounds.yMin)); y <= Mathf.Min(map.H - 1, Mathf.FloorToInt(bounds.yMax)); y++)
                for (int x = Mathf.Max(0, Mathf.FloorToInt(bounds.xMin)); x <= Mathf.Min(map.W - 1, Mathf.FloorToInt(bounds.xMax)); x++)
                    if (WallGraph.IsWalkable(map.Get(x, y)) && CoversGround(test, height, new Vector2(x + .5f, y + .5f))) return true;
            return false;
        }

        static Rect Expanded(Rect bounds, float distance) => Rect.MinMaxRect(bounds.xMin - distance, bounds.yMin - distance,
            bounds.xMax + distance, bounds.yMax + distance);

        static bool Slab(float origin, float direction, float min, float max, ref float enter, ref float leave)
        {
            if (Mathf.Abs(direction) < .00001f) return origin > min && origin < max;
            float a = (min - origin) / direction, b = (max - origin) / direction;
            enter = Mathf.Max(enter, Mathf.Min(a, b)); leave = Mathf.Min(leave, Mathf.Max(a, b));
            return leave > enter + .00001f;
        }
    }
}
