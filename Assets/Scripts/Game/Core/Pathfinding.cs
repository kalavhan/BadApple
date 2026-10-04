using System.Collections.Generic;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// 4-way breadth-first search on the tile grid. The goal tile itself may be blocked (e.g. a closed door).
    /// The walkable callback must return false outside the map.
    /// </summary>
    public static class Pathfinding
    {
        static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        public static List<Vector2Int> FindPath(Vector2Int start, Vector2Int goal, System.Func<int, int, bool> walkable, int maxNodes = 6000)
        {
            var result = new List<Vector2Int>();
            if (start == goal) return result;

            var prev = new Dictionary<Vector2Int, Vector2Int>();
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(start);
            prev[start] = start;
            bool found = false;

            while (queue.Count > 0 && prev.Count < maxNodes)
            {
                var cur = queue.Dequeue();
                foreach (var d in Dirs)
                {
                    var n = cur + d;
                    if (prev.ContainsKey(n)) continue;
                    if (n != goal && !walkable(n.x, n.y)) continue;
                    prev[n] = cur;
                    if (n == goal) { found = true; break; }
                    queue.Enqueue(n);
                }
                if (found) break;
            }

            if (!found) return null;
            var step = goal;
            while (step != start)
            {
                result.Add(step);
                step = prev[step];
            }
            result.Reverse();
            return result;
        }

        /// <summary>Path length in tiles, or a large number if unreachable.</summary>
        public static int Distance(Vector2Int start, Vector2Int goal, System.Func<int, int, bool> walkable)
        {
            var p = FindPath(start, goal, walkable);
            return p == null ? 9999 : p.Count;
        }
    }
}
