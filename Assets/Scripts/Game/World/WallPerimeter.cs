using System.Collections.Generic;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// Physical walls on the middle of the reserved wall strip. The two logical floor
    /// boundaries collapse onto this same line; they never create two rendered shells.
    /// All positions are keyed on a half-tile lattice, including doorway terminations.
    /// </summary>
    public sealed class WallPerimeter
    {
        public sealed class Part
        {
            public Rect Bounds;
            public Vector2 Normal;
            public string Piece;
            public readonly List<int> Runs = new List<int>();
        }
        sealed class Segment
        {
            public Vector2Int A, B;
            public readonly HashSet<int> Runs = new HashSet<int>();
        }
        public readonly List<Part> Spans = new List<Part>();
        public readonly List<Part> Corners = new List<Part>();

        public static WallPerimeter Build(WallGraph graph)
        {
            var result = new WallPerimeter();
            var ends = new Dictionary<Vector2, List<WallRun>>();
            foreach (var run in graph.Runs) foreach (var point in new[] { run.A, run.B })
            {
                if (!ends.TryGetValue(point, out var list)) ends.Add(point, list = new List<WallRun>());
                list.Add(run);
            }
            var roomStrip = new HashSet<Vector2Int>();
            foreach (var room in graph.Map.Rooms)
                foreach (var floor in room.Floor)
                    for(int dx=-1;dx<=1;dx++) for(int dy=-1;dy<=1;dy++) roomStrip.Add(floor+new Vector2Int(dx,dy));
            var segments = new Dictionary<(Vector2Int, Vector2Int), Segment>();
            foreach (var run in graph.Runs)
            {
                // Door jambs belong to the single frame on the wall centerline.
                bool jamb = true;
                foreach (int id in run.EdgeIds)
                    if (graph.Map.Get(graph.Edges[id].WalkableCell.x, graph.Edges[id].WalkableCell.y) != Tile.Door) jamb = false;
                if (jamb) continue;
                Vector2 Offset(Vector2 point, bool start)
                {
                    var other = Vector2.zero;
                    var tangent = (run.B - run.A).normalized * (start ? 1 : -1);
                    foreach (var next in ends[point])
                        if (Mathf.Abs(Vector2.Dot(next.Normal, run.Normal)) < .5f)
                        {
                            other = next.Normal;
                            // At a diagonal contact, join around each solid quadrant.
                            // Pairing the same floor quadrant would place the offset corner
                            // in the diagonally opposite (also walkable) floor cell.
                            if (other == (ends[point].Count>2 ? -tangent : tangent)) break;
                        }
                    var shifted = point - (run.Normal + other) * .5f;
                    foreach (var room in graph.Map.Rooms)
                    {
                        var door = room.DoorTile;
                        var inward = (Vector2)(room.DoorInside - door);
                        if (Mathf.Abs(Vector2.Dot(inward, run.Normal)) < .5f) continue;
                        if (point.x < door.x || point.x > door.x + 1 || point.y < door.y || point.y > door.y + 1) continue;
                        // Preserve the one-tile opening; only its depth moves to the centerline.
                        if (run.Normal.y != 0) shifted.x = point.x; else shifted.y = point.y;
                    }
                    return shifted;
                }
                var a = Vector2Int.RoundToInt(Offset(run.A, true) * 2);
                var b = Vector2Int.RoundToInt(Offset(run.B, false) * 2);
                var direction = Vector2Int.RoundToInt((run.B - run.A).normalized);
                if (Vector2.Dot(b - a, direction) <= 0) continue; // collapsed concave notch
                for (var at = a; at != b; at += direction)
                {
                    var next = at + direction;
                    var midpoint = (Vector2)(at+next)*.25f;
                    WallEdge closest = null; float distance = float.MaxValue;
                    foreach(int id in run.EdgeIds)
                    {
                        var edge = graph.Edges[id];
                        float d = (((Vector2)edge.A+edge.B)*.5f-midpoint).sqrMagnitude;
                        if(d<distance){distance=d;closest=edge;}
                    }
                    // A room owns its whole separator, including stepped corners. The
                    // corridor's dilated outline can shortcut those turns, so it must not
                    // build a second enclosing polygon around that same room.
                    if(graph.Map.Get(closest.WalkableCell.x,closest.WalkableCell.y)==Tile.Corridor && roomStrip.Contains(closest.SolidCell)) continue;
                    var key = (at, next);
                    if (!segments.TryGetValue(key, out var segment))
                        segments.Add(key, segment = new Segment { A = at, B = next });
                    segment.Runs.Add(run.Id);
                }
            }
            var nodes = new Dictionary<Vector2Int, List<Segment>>();
            foreach (var segment in segments.Values) foreach (var point in new[] { segment.A, segment.B })
            {
                if (!nodes.TryGetValue(point, out var list)) nodes.Add(point, list = new List<Segment>());
                list.Add(segment);
            }
            var posts = new Dictionary<Vector2Int, Part>();
            float half = graph.Thickness * .5f;
            var sortedNodes = new List<Vector2Int>(nodes.Keys);
            sortedNodes.Sort((a,b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            foreach (var point in sortedNodes)
            {
                var touching = nodes[point];
                bool straight = touching.Count == 2 && touching[0].B - touching[0].A == touching[1].B - touching[1].A;
                if (straight) continue;
                var center = (Vector2)point * .5f;
                float endLength=graph.Thickness;
                Vector2 endDirection=Vector2.zero;
                if(touching.Count==1)
                {
                    var segment=touching[0];var other=segment.A==point?segment.B:segment.A;
                    endDirection=other-point;
                    if(nodes[other].Count==1)endLength=Mathf.Min(endLength,.25f);
                    center+=endDirection*(endLength*.5f);
                }
                var post = new Part { Bounds = new Rect(center.x-half, center.y-half, graph.Thickness, graph.Thickness),
                    Normal = Vector2.down, Piece = touching.Count == 1 ? "wall_end_cap" : "wall_corner" };
                if(touching.Count==1)
                {
                    bool horizontal=endDirection.x!=0;
                    post.Normal=horizontal?Vector2.down:Vector2.left;
                    post.Bounds=horizontal?new Rect(center.x-endLength*.5f,center.y-half,endLength,graph.Thickness):
                        new Rect(center.x-half,center.y-endLength*.5f,graph.Thickness,endLength);
                }
                // The inside-corner asset belongs at a turn open toward the camera.
                bool east = false, north = false;
                foreach (var segment in touching)
                {
                    var direction = segment.A == point ? segment.B-point : segment.A-point;
                    east |= direction.x > 0; north |= direction.y > 0;
                    foreach (int run in segment.Runs) if (!post.Runs.Contains(run)) post.Runs.Add(run);
                }
                if (touching.Count == 2 && east && north) post.Piece = "wall_inner_corner";
                post.Runs.Sort(); posts.Add(point, post); result.Corners.Add(post);
            }
            // Merge collinear half-steps with identical logical owners. Their shared state
            // follows the occupied room, regardless of which side supplied the boundary.
            var ordered = new List<Segment>(segments.Values);
            ordered.Sort((a,b) => a.A.y != b.A.y ? a.A.y.CompareTo(b.A.y) : a.A.x != b.A.x ? a.A.x.CompareTo(b.A.x) : a.B.y.CompareTo(b.B.y));
            var used = new HashSet<Segment>();
            foreach (var segment in ordered)
            {
                if (!used.Add(segment)) continue;
                var a = segment.A; var b = segment.B; var direction = b-a;
                while (!posts.ContainsKey(b) && segments.TryGetValue((b,b+direction),out var next) && segment.Runs.SetEquals(next.Runs))
                { used.Add(next); b = next.B; }
                Vector2 start = (Vector2)a*.5f, end = (Vector2)b*.5f;
                if (posts.TryGetValue(a,out var firstPost)) start += (Vector2)direction*(firstPost.Piece=="wall_end_cap"?(direction.x!=0?firstPost.Bounds.width:firstPost.Bounds.height):half);
                if (posts.TryGetValue(b,out var lastPost)) end -= (Vector2)direction*(lastPost.Piece=="wall_end_cap"?(direction.x!=0?lastPost.Bounds.width:lastPost.Bounds.height):half);
                if (Vector2.Dot(end-start,direction) < .001f) continue;
                bool horizontal = direction.x != 0;
                var part = new Part { Bounds = horizontal ? new Rect(start.x,start.y-half,end.x-start.x,graph.Thickness) :
                    new Rect(start.x-half,start.y,graph.Thickness,end.y-start.y), Normal = horizontal ? Vector2.down : Vector2.left };
                part.Runs.AddRange(segment.Runs); part.Runs.Sort(); result.Spans.Add(part);
            }
            return result;
        }
    }
}
