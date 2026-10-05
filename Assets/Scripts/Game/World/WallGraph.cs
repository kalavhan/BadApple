using System;
using System.Collections.Generic;
using UnityEngine;

namespace BadAppleHotel.Game
{
    public enum WallJoinKind { InnerCorner, OuterCorner, Junction, End }

    /// <summary>A boundary face. A and B are grid vertices, and Normal points into the walkable cell.</summary>
    public sealed class WallEdge
    {
        public int Id;
        public Vector2Int A, B, Normal, WalkableCell, SolidCell;
        public int RunId;
    }

    public sealed class WallRun
    {
        public int Id;
        public Vector2 A, B, Normal;
        public Rect Footprint;
        // The envelope is useful for selection; render the partitioned list to avoid double caps.
        public Rect RenderFootprint;
        public readonly List<Rect> RenderFootprints = new List<Rect>();
        public readonly List<int> EdgeIds = new List<int>();
        public float DefaultHeight;
        public float Length => Vector2.Distance(A, B);
        public Vector2 Center => Footprint.center;
    }

    public sealed class WallJoin
    {
        public Vector2Int Vertex;
        public WallJoinKind Kind;
        public readonly List<Rect> Footprints = new List<Rect>();
        public readonly List<int> IncidentRunIds = new List<int>();
        public float DefaultHeight;
    }

    public sealed class WallCore
    {
        public Vector2Int Cell;
        public Rect Footprint;
        public readonly List<int> IncidentRunIds = new List<int>();
        public float DefaultHeight;
    }

    /// <summary>
    /// Shared geometry for rendering, cutaway and collision. Floor edges are never moved into
    /// walkable space. Runs have exact square ends; joins own the small corner patches, and
    /// opposite faces of one-cell separators share a filled structural core.
    /// </summary>
    public sealed class WallGraph
    {
        public const float DefaultThickness = .3f;
        public const float FullHeight = 1.7f;
        public const float CutawayHeight = .45f;
        public const float DownHeight = .1f;

        public readonly HotelMap Map;
        public readonly float Thickness;
        public readonly List<WallEdge> Edges = new List<WallEdge>();
        public readonly List<WallRun> Runs = new List<WallRun>();
        public readonly List<WallJoin> Joins = new List<WallJoin>();
        public readonly List<WallCore> Cores = new List<WallCore>();
        public readonly List<Rect> CollisionFootprints = new List<Rect>();

        static readonly Vector2Int[] Neighbours = { Vector2Int.left, Vector2Int.down, Vector2Int.right, Vector2Int.up };

        WallGraph(HotelMap map, float thickness) { Map = map; Thickness = thickness; }

        public static bool IsWalkable(Tile tile) => tile == Tile.RoomFloor || tile == Tile.Corridor || tile == Tile.Door;

        public static WallGraph Build(HotelMap map, float thickness = DefaultThickness)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (!(thickness > 0 && thickness <= .5f)) throw new ArgumentOutOfRangeException(nameof(thickness), "Wall thickness must be in (0, 0.5].");
            var graph = new WallGraph(map, thickness);
            graph.FindEdges();
            graph.MergeRuns();
            graph.FindCores();
            graph.FindJoins();
            graph.PartitionGeometry();
            graph.AssignHeights();
            return graph;
        }

        void FindEdges()
        {
            for (int y = 0; y < Map.H; y++)
                for (int x = 0; x < Map.W; x++)
                {
                    if (!IsWalkable(Map.Get(x, y))) continue;
                    var floor = new Vector2Int(x, y);
                    foreach (var direction in Neighbours)
                    {
                        var solid = floor + direction;
                        if (IsWalkable(Map.Get(solid.x, solid.y))) continue;
                        Vector2Int a, b;
                        if (direction.x != 0)
                        {
                            int side = x + (direction.x > 0 ? 1 : 0);
                            a = new Vector2Int(side, y); b = new Vector2Int(side, y + 1);
                        }
                        else
                        {
                            int side = y + (direction.y > 0 ? 1 : 0);
                            a = new Vector2Int(x, side); b = new Vector2Int(x + 1, side);
                        }
                        Edges.Add(new WallEdge { Id = Edges.Count, A = a, B = b, Normal = -direction, WalkableCell = floor, SolidCell = solid });
                    }
                }
        }

        void MergeRuns()
        {
            // Sorting explicitly keeps run ids stable across runtimes and dictionary implementations.
            var sorted = new List<WallEdge>(Edges);
            sorted.Sort((a, b) =>
            {
                int compare = a.Normal.x.CompareTo(b.Normal.x);
                if (compare != 0) return compare;
                compare = a.Normal.y.CompareTo(b.Normal.y);
                if (compare != 0) return compare;
                bool horizontal = a.A.y == a.B.y;
                compare = (horizontal ? a.A.y : a.A.x).CompareTo(horizontal ? b.A.y : b.A.x);
                return compare != 0 ? compare : (horizontal ? a.A.x : a.A.y).CompareTo(horizontal ? b.A.x : b.A.y);
            });
            WallRun run = null;
            foreach (var edge in sorted)
            {
                if (run == null || run.Normal != (Vector2)edge.Normal || run.B != (Vector2)edge.A)
                {
                    run = new WallRun { Id = Runs.Count, A = edge.A, B = edge.B, Normal = edge.Normal };
                    Runs.Add(run);
                }
                else run.B = edge.B;
                edge.RunId = run.Id;
                run.EdgeIds.Add(edge.Id);
            }
            foreach (var item in Runs)
            {
                var outsideA = item.A - item.Normal * Thickness;
                var outsideB = item.B - item.Normal * Thickness;
                item.Footprint = Rect.MinMaxRect(Mathf.Min(item.A.x, outsideA.x), Mathf.Min(item.A.y, outsideA.y),
                    Mathf.Max(item.B.x, outsideB.x), Mathf.Max(item.B.y, outsideB.y));
                item.RenderFootprint = item.Footprint;
                CollisionFootprints.Add(item.Footprint);
            }
        }

        void FindCores()
        {
            var byCell = new Dictionary<Vector2Int, List<int>>();
            foreach (var edge in Edges)
            {
                if (!byCell.TryGetValue(edge.SolidCell, out var ids)) byCell.Add(edge.SolidCell, ids = new List<int>());
                if (!ids.Contains(edge.RunId)) ids.Add(edge.RunId);
            }
            var cells = new List<Vector2Int>(byCell.Keys);
            cells.Sort(CompareVertices);
            foreach (var cell in cells)
            {
                bool acrossX = IsWalkable(Map.Get(cell.x - 1, cell.y)) && IsWalkable(Map.Get(cell.x + 1, cell.y));
                bool acrossY = IsWalkable(Map.Get(cell.x, cell.y - 1)) && IsWalkable(Map.Get(cell.x, cell.y + 1));
                if (!acrossX && !acrossY) continue;
                var core = new WallCore { Cell = cell, Footprint = new Rect(cell.x, cell.y, 1, 1) };
                core.IncidentRunIds.AddRange(byCell[cell]);
                core.IncidentRunIds.Sort();
                Cores.Add(core);
                CollisionFootprints.Add(core.Footprint);
            }
        }

        void FindJoins()
        {
            var atVertex = new Dictionary<Vector2Int, List<int>>();
            foreach (var run in Runs)
                foreach (var point in new[] { Vector2Int.RoundToInt(run.A), Vector2Int.RoundToInt(run.B) })
                {
                    if (!atVertex.TryGetValue(point, out var ids)) atVertex.Add(point, ids = new List<int>());
                    ids.Add(run.Id);
                }
            var vertices = new List<Vector2Int>(atVertex.Keys);
            vertices.Sort(CompareVertices);
            foreach (var vertex in vertices)
            {
                int floors = 0;
                Vector2Int floorQuadrant = default, solidQuadrant = default;
                for (int dx = -1; dx <= 0; dx++) for (int dy = -1; dy <= 0; dy++)
                {
                    var cell = vertex + new Vector2Int(dx, dy);
                    if (IsWalkable(Map.Get(cell.x, cell.y))) { floors++; floorQuadrant = cell; }
                    else solidQuadrant = cell;
                }
                var join = new WallJoin { Vertex = vertex,
                    Kind = atVertex[vertex].Count <= 1 ? WallJoinKind.End : atVertex[vertex].Count > 2 ? WallJoinKind.Junction :
                        floors == 1 ? WallJoinKind.InnerCorner : WallJoinKind.OuterCorner };
                join.IncidentRunIds.AddRange(atVertex[vertex]);
                join.IncidentRunIds.Sort();
                if (floors == 1)
                {
                    // Fill the diagonal quadrant; both straight faces stop at the exact vertex.
                    var opposite = new Vector2Int(floorQuadrant.x < vertex.x ? vertex.x : vertex.x - 1,
                        floorQuadrant.y < vertex.y ? vertex.y : vertex.y - 1);
                    join.Footprints.Add(CornerPatch(vertex, opposite));
                }
                else if (floors == 3) join.Footprints.Add(CornerPatch(vertex, solidQuadrant));
                else
                    for (int dx = -1; dx <= 0; dx++) for (int dy = -1; dy <= 0; dy++)
                    {
                        var cell = vertex + new Vector2Int(dx, dy);
                        if (!IsWalkable(Map.Get(cell.x, cell.y))) join.Footprints.Add(CornerPatch(vertex, cell));
                    }
                foreach (var footprint in join.Footprints) CollisionFootprints.Add(footprint);
                Joins.Add(join);
            }
        }

        Rect CornerPatch(Vector2Int vertex, Vector2Int cell) => new Rect(
            cell.x < vertex.x ? vertex.x - Thickness : vertex.x,
            cell.y < vertex.y ? vertex.y - Thickness : vertex.y, Thickness, Thickness);

        void PartitionGeometry()
        {
            // Cores own their complete cell. Joins then own their patches, and runs own what
            // remains. Logical boundary/collision rectangles deliberately remain untrimmed.
            foreach (var join in Joins)
                foreach (var core in Cores) Subtract(join.Footprints, core.Footprint);
            foreach (var run in Runs)
            {
                run.RenderFootprints.Add(run.Footprint);
                foreach (var core in Cores) Subtract(run.RenderFootprints, core.Footprint);
                foreach (var join in Joins)
                    foreach (var patch in join.Footprints) Subtract(run.RenderFootprints, patch);
                if (run.RenderFootprints.Count == 0) run.RenderFootprint = new Rect(run.Footprint.position, Vector2.zero);
                else
                {
                    var bounds = run.RenderFootprints[0];
                    for (int i = 1; i < run.RenderFootprints.Count; i++)
                    {
                        var part = run.RenderFootprints[i];
                        bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, part.xMin), Mathf.Min(bounds.yMin, part.yMin),
                            Mathf.Max(bounds.xMax, part.xMax), Mathf.Max(bounds.yMax, part.yMax));
                    }
                    run.RenderFootprint = bounds;
                }
            }
        }

        static void Subtract(List<Rect> pieces, Rect cut)
        {
            for (int i = pieces.Count - 1; i >= 0; i--)
            {
                var source = pieces[i];
                float left = Mathf.Max(source.xMin, cut.xMin), right = Mathf.Min(source.xMax, cut.xMax);
                float bottom = Mathf.Max(source.yMin, cut.yMin), top = Mathf.Min(source.yMax, cut.yMax);
                if (right - left < .0001f || top - bottom < .0001f) continue;
                pieces.RemoveAt(i);
                AddRect(pieces, source.xMin, source.yMin, left, source.yMax);
                AddRect(pieces, right, source.yMin, source.xMax, source.yMax);
                AddRect(pieces, left, source.yMin, right, bottom);
                AddRect(pieces, left, top, right, source.yMax);
            }
        }

        static void AddRect(List<Rect> pieces, float xMin, float yMin, float xMax, float yMax)
        {
            if (xMax - xMin > .0001f && yMax - yMin > .0001f) pieces.Add(Rect.MinMaxRect(xMin, yMin, xMax, yMax));
        }

        void AssignHeights()
        {
            foreach (var run in Runs) run.DefaultHeight = WallVisibility.DefaultHeight(Map, run.Footprint);
            foreach (var join in Joins)
            {
                join.DefaultHeight = IncidentHeight(join.IncidentRunIds);
                foreach (var footprint in join.Footprints)
                    join.DefaultHeight = Mathf.Min(join.DefaultHeight, WallVisibility.DefaultHeight(Map, footprint));
            }
            foreach (var core in Cores)
                core.DefaultHeight = Mathf.Min(IncidentHeight(core.IncidentRunIds), WallVisibility.DefaultHeight(Map, core.Footprint));
        }

        float IncidentHeight(List<int> ids)
        {
            float height = FullHeight;
            foreach (int id in ids) height = Mathf.Min(height, Runs[id].DefaultHeight);
            return height;
        }

        static int CompareVertices(Vector2Int a, Vector2Int b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x);
    }
}
