using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BadAppleHotel.Game
{
    public static class TileMovement
    {
        // The same footprint rectangles feed visible wall meshes and collision. Index them by
        // tile so continuous movement only tests nearby faces, including solid separator cores.
        sealed class WallIndex
        {
            readonly List<Rect>[] cells;
            readonly int width, height;
            public WallIndex(WallGraph graph)
            {
                width = graph.Map.W; height = graph.Map.H;
                cells = new List<Rect>[width * height];
                foreach (var rect in graph.CollisionFootprints)
                    for (int x = Mathf.Max(0, Mathf.FloorToInt(rect.xMin)); x <= Mathf.Min(width - 1, Mathf.FloorToInt(rect.xMax)); x++)
                        for (int y = Mathf.Max(0, Mathf.FloorToInt(rect.yMin)); y <= Mathf.Min(height - 1, Mathf.FloorToInt(rect.yMax)); y++)
                        {
                            int cell = y * width + x;
                            if (cells[cell] == null) cells[cell] = new List<Rect>();
                            cells[cell].Add(rect);
                        }
            }
            public List<Rect> At(int x, int y) => (uint)x < (uint)width && (uint)y < (uint)height ? cells[y * width + x] : null;
        }
        static readonly ConditionalWeakTable<WallGraph, WallIndex> indexes = new ConditionalWeakTable<WallGraph, WallIndex>();
        static WallIndex Index(WallGraph graph) => indexes.GetValue(graph, g => new WallIndex(g));
        static bool IsFloor(Tile tile) => tile == Tile.Corridor || tile == Tile.RoomFloor || tile == Tile.Door;
        static Vector2 Closest(Vector2 p, Rect rect) => new Vector2(Mathf.Clamp(p.x, rect.xMin, rect.xMax), Mathf.Clamp(p.y, rect.yMin, rect.yMax));
        static bool Touches(Vector2 p, Rect rect, float radius) => (Closest(p, rect) - p).sqrMagnitude < radius * radius - 0.00001f;

        static void Resolve(ref Vector2 p, Rect rect, float radius)
        {
            var away = p - Closest(p, rect);
            float distance = away.magnitude;
            if (distance > 0.00001f && distance < radius)
                p += away / distance * (radius - distance + 0.0001f);
        }
        public static bool CanStand(Vector2 p, Func<int, int, bool> walk, float radius, WallGraph graph = null)
        {
            return CanStand(p, walk, radius, graph, graph == null ? null : Index(graph));
        }

        static bool CanStand(Vector2 p, Func<int, int, bool> walk, float radius, WallGraph graph, WallIndex walls)
        {
            var center = HotelMap.ToTile(p);
            if (!walk(center.x, center.y) || (graph != null && !IsFloor(graph.Map.Get(center.x, center.y)))) return false;
            for (int x = Mathf.FloorToInt(p.x - radius); x <= Mathf.FloorToInt(p.x + radius); x++)
                for (int y = Mathf.FloorToInt(p.y - radius); y <= Mathf.FloorToInt(p.y + radius); y++)
                {
                    var nearby = walls?.At(x, y);
                    if (nearby != null)
                        foreach (var rect in nearby) if (Touches(p, rect, radius)) return false;
                    // Only doors, towers and other temporarily blocked floor remain tile-sized.
                    // Solid wall/void cells use their rendered edge graph instead.
                    if ((x != center.x || y != center.y) && (graph == null || IsFloor(graph.Map.Get(x, y))) && !walk(x, y) &&
                        Touches(p, new Rect(x, y, 1, 1), radius)) return false;
                }
            return true;
        }

        public static bool Clear(Vector2 a, Vector2 b, Func<int, int, bool> walk, float radius, WallGraph graph = null)
        {
            var walls = graph == null ? null : Index(graph);
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / 0.1f));
            for (int i = 0; i <= steps; i++)
                if (!CanStand(Vector2.Lerp(a, b, (float)i / steps), walk, radius, graph, walls)) return false;
            return true;
        }

        public static Vector2 Slide(Vector2 p, Vector2 delta, Func<int, int, bool> walk, float radius, WallGraph graph = null)
        {
            if (delta.sqrMagnitude < 1e-12f) return p;
            var walls = graph == null ? null : Index(graph);
            int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / 0.1f));
            var step = delta / steps;
            for (int i = 0; i < steps; i++)
            {
                var q = p + step;
                // Resolve against the visible wall footprint; the remaining velocity slides
                // along the contact tangent. Small steps prevent dashes tunnelling through walls.
                for (int pass = 0; pass < 3; pass++)
                {
                    var beforePass = q;
                    for (int x = Mathf.FloorToInt(q.x - radius); x <= Mathf.FloorToInt(q.x + radius); x++)
                        for (int y = Mathf.FloorToInt(q.y - radius); y <= Mathf.FloorToInt(q.y + radius); y++)
                        {
                            var nearby = walls?.At(x, y);
                            if (nearby != null) foreach (var rect in nearby) Resolve(ref q, rect, radius);
                            if ((graph == null || IsFloor(graph.Map.Get(x, y))) && !walk(x, y))
                                Resolve(ref q, new Rect(x, y, 1, 1), radius);
                        }
                    if ((q - beforePass).sqrMagnitude < 1e-12f) break;
                }
                // Thin walls leave a small floor margin. Keep the logical center on
                // its permitted floor while preserving tangential motion along the wall.
                // Rejecting the entire diagonal step here would strand path followers.
                if(graph!=null&&!walk(HotelMap.ToTile(q).x,HotelMap.ToTile(q).y))
                {
                    var tile=HotelMap.ToTile(q);var closest=q;float distance=float.MaxValue;
                    for(int x=tile.x-1;x<=tile.x+1;x++)for(int y=tile.y-1;y<=tile.y+1;y++)
                    {
                        if(!IsFloor(graph.Map.Get(x,y))||!walk(x,y))continue;
                        var candidate=new Vector2(Mathf.Clamp(q.x,x+.0001f,x+1-.0001f),Mathf.Clamp(q.y,y+.0001f,y+1-.0001f));
                        float d=(candidate-q).sqrMagnitude;
                        if(d<distance){distance=d;closest=candidate;}
                    }
                    if(distance<.04f)q=closest;
                }
                bool contact = (q - (p + step)).sqrMagnitude > 0.00001f;
                if (CanStand(q, walk, radius, graph, walls)) p = q;
                // Align an off-center approach with the nearby doorway, never cross a blocked tile.
                if (contact)
                {
                    Vector2 assist = Mathf.Abs(step.x) > Mathf.Abs(step.y)
                        ? new Vector2(p.x, Mathf.Floor(p.y) + 0.5f)
                        : new Vector2(Mathf.Floor(p.x) + 0.5f, p.y);
                    if (Vector2.Distance(p, assist) <= 0.45f && Clear(p, assist, walk, radius, graph) &&
                        CanStand(assist + step.normalized * 0.55f, walk, radius, graph, walls))
                        p = Vector2.MoveTowards(p, assist, step.magnitude);
                }
            }
            return p;
        }
    }

    /// <summary>Shared path following, radius-safe string pulling and measured-progress recovery.</summary>
    public class Navigator
    {
        List<Vector2Int> path;
        int index, failures;
        Vector2Int goal, blocked;
        Vector2 sample;
        float sampleAt, blockedUntil, replanAt;
        public int Recoveries { get; private set; }

        public Vector2 Steer(ref Vector2 pos, Vector2Int target, Func<int, int, bool> walk, float radius, float dt, float now, WallGraph graph = null)
        {
            bool waiting = Vector2.Distance(pos, HotelMap.Center(target)) < 0.12f;
            if (waiting) { sample = pos; sampleAt = now; failures = 0; return Vector2.zero; }
            if (now - sampleAt >= 0.6f)
            {
                if (path != null && index < path.Count && Vector2.Distance(sample, pos) < 0.1f)
                {
                    blocked = path[index]; blockedUntil = now + 3f; path = null; Recoveries++;
                    if (++failures >= 2)
                    {
                        Vector2 best = pos; float dist = float.MaxValue;
                        var tile = HotelMap.ToTile(pos);
                        for (int x = -2; x <= 2; x++) for (int y = -2; y <= 2; y++)
                        {
                            var q = HotelMap.Center(tile + new Vector2Int(x, y));
                            float d = Vector2.Distance(pos, q);
                            if (d < dist && TileMovement.CanStand(q, walk, radius, graph) && TileMovement.Clear(pos, q, walk, radius, graph)) { best = q; dist = d; }
                        }
                        pos = best; failures = 0;
                    }
                }
                else failures = 0;
                sample = pos; sampleAt = now;
            }
            if (path == null || target != goal || now >= replanAt)
            {
                goal = target; replanAt = now + 1.2f; index = 0;
                path = Pathfinding.FindPath(HotelMap.ToTile(pos), goal,
                    (x, y) => walk(x, y) && (now >= blockedUntil || new Vector2Int(x, y) != blocked));
                if (path == null) path = Pathfinding.FindPath(HotelMap.ToTile(pos), goal, walk);
            }
            if (path == null) return Vector2.zero;
            while (index < path.Count && Vector2.Distance(pos, HotelMap.Center(path[index])) < 0.15f) index++;
            if (index >= path.Count) return Vector2.ClampMagnitude((HotelMap.Center(target) - pos) * 6f, 1f);
            for (int i = Mathf.Min(path.Count - 1, index + 8); i > index; i--)
                if (TileMovement.Clear(pos, HotelMap.Center(path[i]), walk, radius, graph)) { index = i; break; }
            return Vector2.ClampMagnitude((HotelMap.Center(path[index]) - pos) * 6f, 1f);
        }
    }
}
