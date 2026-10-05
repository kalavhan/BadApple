using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BadAppleHotel.Config;
using BadAppleHotel.Game;
using NUnit.Framework;
using UnityEngine;

namespace BadAppleHotel.Tests
{
    public class CoreBackingTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [TestCase(11)] [TestCase(41)] [TestCase(83)]
        public void Actual_separator_backings_stay_behind_decorative_skins_and_meet_solid_neighbours_without_top_gaps(int seed)
        {
            var random = Random.state; var art = Sprites.ArtOverride; float timeScale = Time.timeScale;
            var game = new GameObject("Separator backing geometry " + seed).AddComponent<GameManager>();
            try
            {
                game.StartSimulation(ConfigLoader.Load(), seed, false);
                typeof(GameManager).GetMethod("BuildScene3D", Private).Invoke(game, null);
                var root = (Transform)typeof(GameManager).GetField("worldRoot", Private).GetValue(game);
                var states = (Texture2D)typeof(GameManager).GetField("wallStateTexture", Private).GetValue(game);
                var instances = (WallInstances)typeof(GameManager).GetField("wallInstances", Private).GetValue(game);
                var boxes = new Dictionary<int, Bounds>();
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
                {
                    var mesh = filter.sharedMesh;
                    if (mesh == null || !mesh.name.StartsWith("Hotel walls ")) continue;
                    var vertices = mesh.vertices; var uv = mesh.uv2;
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        int state = Mathf.FloorToInt(uv[i].x * states.width);
                        var point = filter.transform.TransformPoint(vertices[i]);
                        if (!boxes.TryGetValue(state, out var bounds)) bounds = new Bounds(point, Vector3.zero);
                        else bounds.Encapsulate(point);
                        boxes[state] = bounds;
                    }
                }
                int firstCore = game.Walls.Runs.Count + game.Walls.Joins.Count;
                var byCell = game.Walls.Cores.Select((core, index) => new { core.Cell, Bounds = boxes[firstCore + index] }).ToDictionary(x => x.Cell, x => x.Bounds);
                var skinDepths = new Dictionary<Mesh, float>();
                int skins = 0, joins = 0;
                for (int index = 0; index < game.Walls.Cores.Count; index++)
                {
                    var core = game.Walls.Cores[index]; int state = firstCore + index; var box = boxes[state];
                    Assert.Less(.04f - box.min.z, WallGraph.FullHeight - .07f, "Core top must sit below decorative crown bevels.");
                    Assert.Greater(.04f - box.min.z, WallGraph.FullHeight - .15f, "The filled structural top must remain close to the crown.");
                    foreach (var record in instances.Records.Where(record => record.StateId == state))
                    {
                        if (!skinDepths.TryGetValue(record.Mesh, out float frontDepth))
                        { frontDepth = DeepestFrontSkin(record.Mesh, record.Piece.Size); skinDepths.Add(record.Mesh, frontDepth); }
                        var vector = record.Matrix.MultiplyVector(Vector3.forward);
                        var inward = vector.normalized; var origin = record.Matrix.MultiplyPoint3x4(Vector3.zero);
                        float backingDepth = float.PositiveInfinity;
                        foreach (float x in new[] { box.min.x, box.max.x }) foreach (float y in new[] { box.min.y, box.max.y })
                            backingDepth = Mathf.Min(backingDepth, Vector3.Dot(new Vector3(x,y,origin.z) - origin, inward));
                        Assert.Greater(backingDepth, frontDepth * vector.magnitude + .005f,
                            $"Core {core.Cell} would cover the recessed {record.Piece.Id} skin.");
                        skins++;
                    }
                    foreach (var direction in new[] { Vector2Int.right, Vector2Int.up })
                    {
                        if (!byCell.TryGetValue(core.Cell + direction, out var neighbour)) continue;
                        if (direction.x != 0)
                        {
                            Assert.AreEqual(box.max.x, neighbour.min.x, .00001f, "A solid-neighbour inset opens a vertical slot through the separator top.");
                            Assert.Greater(Mathf.Min(box.max.y,neighbour.max.y) - Mathf.Max(box.min.y,neighbour.min.y), .3f);
                        }
                        else
                        {
                            Assert.AreEqual(box.max.y, neighbour.min.y, .00001f, "A solid-neighbour inset opens a horizontal slot through the separator top.");
                            Assert.Greater(Mathf.Min(box.max.x,neighbour.max.x) - Mathf.Max(box.min.x,neighbour.min.x), .3f);
                        }
                        Assert.AreEqual(box.min.z, neighbour.min.z, .00001f); joins++;
                    }
                }
                Assert.Greater(skins, 0, "This hotel must exercise actual decorated separator faces.");
                Assert.Greater(joins, 0, "This hotel must exercise consecutive filled separator cells.");
            }
            finally
            {
                if (game != null) game.DisposeSimulation();
                Random.state = random; Sprites.ArtOverride = art; Time.timeScale = timeScale;
            }
        }

        static float DeepestFrontSkin(Mesh mesh, Vector3 size)
        {
            var vertices = mesh.vertices; var triangles = mesh.triangles;
            float deepest = 0; int hits = 0;
            // Rays use the real recessed shape; a mere bounds check accepts the original bug.
            for (int ix = 1; ix < 12; ix++) for (int iy = 1; iy < 12; iy++)
            {
                float x = size.x * ix / 12, y = size.y * iy / 12, nearest = float.PositiveInfinity;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    var a = vertices[triangles[i]]; var b = vertices[triangles[i+1]]; var c = vertices[triangles[i+2]];
                    float det = (b.y-c.y)*(a.x-c.x)+(c.x-b.x)*(a.y-c.y);
                    if (Mathf.Abs(det) < 1e-9f) continue;
                    float u = ((b.y-c.y)*(x-c.x)+(c.x-b.x)*(y-c.y))/det;
                    float v = ((c.y-a.y)*(x-c.x)+(a.x-c.x)*(y-c.y))/det;
                    if (u < 0 || v < 0 || u+v > 1) continue;
                    nearest = Mathf.Min(nearest, u*a.z+v*b.z+(1-u-v)*c.z);
                }
                // The rotated ornament has intentional empty shoulders; only its visible skin
                // participates. The structural fill itself is checked separately above.
                if (float.IsInfinity(nearest)) continue;
                deepest = Mathf.Max(deepest, nearest); hits++;
            }
            Assert.Greater(hits, 0, mesh.name + " needs measurable front geometry"); return deepest;
        }
    }
}
