using System;
using System.Collections.Generic;
using UnityEngine;
namespace BadAppleHotel.Game
{
    public static class Pathfinding
    {
        static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        static int H(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
        public static List<Vector2Int> FindPath(Vector2Int start, Vector2Int goal, Func<int,int,bool> walkable, int maxNodes = 12000)
        {
            if (!walkable(goal.x, goal.y)) return null;
            var result = new List<Vector2Int>();
            if (start == goal) return result;
            var open = new SortedSet<(int score, int serial, Vector2Int tile)>(Comparer<(int score, int serial, Vector2Int tile)>.Create((a,b) => a.score != b.score ? a.score.CompareTo(b.score) : a.serial.CompareTo(b.serial)));
            var prev = new Dictionary<Vector2Int, Vector2Int>();
            var costs = new Dictionary<Vector2Int, int> { [start] = 0 };
            int serial = 0;
            open.Add((H(start, goal), serial++, start));
            while (open.Count > 0 && costs.Count <= maxNodes)
            {
                var item = open.Min; open.Remove(item); var cur = item.tile;
                if (cur == goal)
                {
                    while (cur != start) { result.Add(cur); cur = prev[cur]; }
                    result.Reverse(); return result;
                }
                foreach (var d in Dirs)
                {
                    var n = cur + d;
                    if (!walkable(n.x, n.y)) continue;
                    int cost = costs[cur] + 1;
                    if (costs.TryGetValue(n, out int old) && old <= cost) continue;
                    costs[n] = cost; prev[n] = cur;
                    open.Add((cost + H(n, goal), serial++, n));
                }
            }
            return null;
        }
        public static int Distance(Vector2Int start, Vector2Int goal, Func<int,int,bool> walkable) => FindPath(start, goal, walkable)?.Count ?? 9999;
    }
}
