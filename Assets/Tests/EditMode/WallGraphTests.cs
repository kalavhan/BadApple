using System.Collections.Generic;
using System.Linq;
using BadAppleHotel.Config;
using BadAppleHotel.Game;
using NUnit.Framework;
using UnityEngine;

namespace BadAppleHotel.Tests
{
    public class WallGraphTests
    {
        static readonly Vector2Int[] Directions = { Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.up };

        [Test]
        public void Logical_boundary_coverage_and_solid_physical_footprints_hold_for_100_hotels()
        {
            var config = ConfigLoader.Load();
            int cores = 0, innerCorners = 0, outerCorners = 0;
            for (int seed = 1; seed <= 100; seed++)
            {
                var map = new HotelMap(config.map, 10, seed);
                var graph = WallGraph.Build(map);
                var expected = new HashSet<string>();
                for (int x = 0; x < map.W; x++) for (int y = 0; y < map.H; y++)
                {
                    if (!Walkable(map.Get(x, y))) continue;
                    foreach (var direction in Directions)
                        if (!Walkable(map.Get(x + direction.x, y + direction.y))) expected.Add(Key(new Vector2Int(x, y), direction));
                }
                var actual = new HashSet<string>();
                var edgeCounts = new int[graph.Edges.Count];
                var meetingVertices = new Dictionary<Vector2, int>();
                foreach (var run in graph.Runs)
                {
                    Assert.AreEqual(run.EdgeIds.Count, run.Length, .0001f, "a run must have no gaps or overshoot");
                    foreach (var id in run.EdgeIds)
                    {
                        edgeCounts[id]++;
                        var edge = graph.Edges[id];
                        Assert.AreEqual(run.Id, edge.RunId);
                        Assert.AreEqual(run.Normal, (Vector2)edge.Normal);
                        Assert.IsTrue(actual.Add(Key(edge.WalkableCell, -edge.Normal)), "duplicate boundary face");
                    }
                    foreach (var vertex in new[] { run.A, run.B })
                        meetingVertices[vertex] = meetingVertices.TryGetValue(vertex, out int count) ? count + 1 : 1;
                }
                CollectionAssert.AreEquivalent(expected, actual, "boundary coverage, seed " + seed);
                Assert.IsTrue(edgeCounts.All(count => count == 1));
                Assert.AreEqual(graph.Joins.Count, graph.Joins.Select(join => join.Vertex).Distinct().Count(), "one owner per vertex");
                foreach (var vertex in meetingVertices)
                    if (vertex.Value >= 2) Assert.AreEqual(1, graph.Joins.Count(join => (Vector2)join.Vertex == vertex.Key));

                var pieces = RenderPieces(graph);
                foreach (var rectangle in graph.CollisionFootprints) AssertSolid(map, rectangle, seed);
                var perCell = new Dictionary<Vector2Int, List<Rect>>();
                foreach (var rectangle in pieces)
                {
                    AssertSolid(map, rectangle, seed);
                    for (int x = Mathf.FloorToInt(rectangle.xMin + .0001f); x <= Mathf.FloorToInt(rectangle.xMax - .0001f); x++)
                        for (int y = Mathf.FloorToInt(rectangle.yMin + .0001f); y <= Mathf.FloorToInt(rectangle.yMax - .0001f); y++)
                        {
                            var cell = new Vector2Int(x, y);
                            if (!perCell.TryGetValue(cell, out var others)) perCell.Add(cell, others = new List<Rect>());
                            foreach (var other in others)
                                Assert.IsFalse(Overlaps(rectangle, other), "render pieces overlap in " + cell + ", seed " + seed);
                            others.Add(rectangle);
                        }
                }
                foreach (var edge in graph.Edges)
                {
                    Vector2 justInside = ((Vector2)edge.A + edge.B) * .5f - (Vector2)edge.Normal * .001f;
                    Assert.AreEqual(1, pieces.Count(piece => piece.Contains(justInside)), "face must have one physical owner");
                }
                foreach (var core in graph.Cores)
                {
                    Assert.AreEqual(new Rect(core.Cell.x, core.Cell.y, 1, 1), core.Footprint);
                    Assert.IsTrue(core.IncidentRunIds.Count >= 2);
                }
                cores += graph.Cores.Count;
                innerCorners += graph.Joins.Count(join => join.Kind == WallJoinKind.InnerCorner);
                outerCorners += graph.Joins.Count(join => join.Kind == WallJoinKind.OuterCorner);
            }
            Assert.Greater(cores, 0, "one-cell separators must have a solid core");
            Assert.Greater(innerCorners, 0);
            Assert.Greater(outerCorners, 0);
        }

        [Test]
        public void All_exterior_runs_corners_and_structural_cores_start_at_full_height()
        {
            var config=ConfigLoader.Load();
            for(int seed=1;seed<=100;seed++)
            {
                var graph=WallGraph.Build(new HotelMap(config.map,10,seed));
                foreach(var run in graph.Runs)Assert.AreEqual(WallGraph.FullHeight,run.DefaultHeight);
                foreach(var join in graph.Joins)
                {
                    Assert.AreEqual(WallGraph.FullHeight,join.DefaultHeight);
                }
                foreach(var core in graph.Cores)Assert.AreEqual(WallGraph.FullHeight,core.DefaultHeight);
            }
        }

        [Test]
        public void Cutaway_detects_occlusion_beyond_a_wall_normal_and_is_deterministic()
        {
            var config = ConfigLoader.Load();
            var map = new HotelMap(config.map, 10, 129);
            var first = WallGraph.Build(map);
            var second = WallGraph.Build(map);
            Assert.AreEqual(first.Runs.Count, second.Runs.Count);
            Assert.AreEqual(first.Joins.Count, second.Joins.Count);
            for (int i = 0; i < first.Runs.Count; i++)
            {
                Assert.AreEqual(first.Runs[i].A, second.Runs[i].A);
                Assert.AreEqual(first.Runs[i].B, second.Runs[i].B);
                Assert.AreEqual(first.Runs[i].DefaultHeight, second.Runs[i].DefaultHeight);
            }
            // This floor is behind a wall even though it is not the wall's adjacent floor.
            var wall = new Rect(0, 0, 1, .3f);
            var behind = new Vector2(.7f, .7f);
            Assert.IsTrue(WallVisibility.CoversGround(wall, WallGraph.FullHeight, behind));
            Assert.IsFalse(WallVisibility.CoversGround(wall, WallGraph.CutawayHeight, behind));
            Assert.IsFalse(WallVisibility.CoversGround(wall, WallGraph.FullHeight, new Vector2(-.5f, -.5f)));
        }

        static void AssertCenterRaysClear(HotelMap map, Rect rectangle, float height, int seed)
        {
            // Use Unity's independent 3D AABB ray intersection rather than the implementation's
            // 2D slab helper. Only centers within the projected box bounds need testing.
            var box = new Bounds(new Vector3(rectangle.center.x, rectangle.center.y, -height * .5f),
                new Vector3(rectangle.width, rectangle.height, height));
            Vector2 projected = (Vector2)HotelView3D.Forward * (height / HotelView3D.Forward.z);
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(rectangle.xMin, rectangle.xMin + projected.x)));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(rectangle.yMin, rectangle.yMin + projected.y)));
            int x1 = Mathf.Min(map.W - 1, Mathf.CeilToInt(Mathf.Max(rectangle.xMax, rectangle.xMax + projected.x)));
            int y1 = Mathf.Min(map.H - 1, Mathf.CeilToInt(Mathf.Max(rectangle.yMax, rectangle.yMax + projected.y)));
            for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++)
            {
                if (!Walkable(map.Get(x, y))) continue;
                var floor = new Vector3(x + .5f, y + .5f, 0);
                var ray = new Ray(floor - HotelView3D.Forward * 100, HotelView3D.Forward);
                Assert.IsFalse(box.IntersectRay(ray, out float distance) && distance < 99.999f,
                    "wall obscures floor " + new Vector2Int(x, y) + ", seed " + seed + ", height " + height);
            }
        }

        static List<Rect> RenderPieces(WallGraph graph)
        {
            var pieces = new List<Rect>();
            foreach (var run in graph.Runs) pieces.AddRange(run.RenderFootprints);
            foreach (var join in graph.Joins) pieces.AddRange(join.Footprints);
            foreach (var core in graph.Cores) pieces.Add(core.Footprint);
            return pieces;
        }

        static void AssertSolid(HotelMap map, Rect rectangle, int seed)
        {
            Assert.Greater(rectangle.width, 0);
            Assert.Greater(rectangle.height, 0);
            for (int x = Mathf.FloorToInt(rectangle.xMin + .0001f); x <= Mathf.FloorToInt(rectangle.xMax - .0001f); x++)
                for (int y = Mathf.FloorToInt(rectangle.yMin + .0001f); y <= Mathf.FloorToInt(rectangle.yMax - .0001f); y++)
                    Assert.IsFalse(Walkable(map.Get(x, y)), "wall enters floor " + new Vector2Int(x, y) + ", seed " + seed);
        }

        static bool Walkable(Tile tile) => tile == Tile.RoomFloor || tile == Tile.Door || tile == Tile.Corridor;
        static string Key(Vector2Int cell, Vector2Int outward) => cell.x + "," + cell.y + ":" + outward.x + "," + outward.y;
        static bool Overlaps(Rect a, Rect b) => Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin) > .0001f &&
            Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin) > .0001f;
    }
}
