using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BadAppleHotel.Config;
using BadAppleHotel.Game;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BadAppleHotel.Tests
{
    public class WallRenderingTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly string[] PieceIds = { "wall_straight", "wall_lamp", "wall_corner", "wall_inner_corner", "wall_end_cap", "door_frame", "wall_cutaway_cap" };

        [TestCase(0, -1, 1, 0)]
        [TestCase(0, 1, -1, 0)]
        [TestCase(-1, 0, 0, -1)]
        [TestCase(1, 0, 0, 1)]
        public void Every_wall_face_rotates_without_mirroring_and_meets_the_next_piece_exactly(int nx, int ny, int tx, int ty)
        {
            var piece = WallKit.Load().Get("wall_straight");
            Assert.IsNotNull(piece?.Mesh);
            var normal = new Vector2(nx, ny);
            bool horizontal = ny != 0;
            const float firstLength = 1.75f, nextLength = .65f, depth = .3f;
            var first = new Rect(10, 20, horizontal ? firstLength : depth, horizontal ? depth : firstLength);
            var next = horizontal
                ? new Rect(tx > 0 ? first.xMax : first.xMin - nextLength, first.yMin, nextLength, depth)
                : new Rect(first.xMin, ty > 0 ? first.yMax : first.yMin - nextLength, depth, nextLength);
            var instances = new WallInstances();
            instances.Add(piece, first, normal, 0, 2, 1);
            instances.Add(piece, next, normal, 1, 2, 1);
            Assert.AreEqual(2, instances.Count);

            foreach (var record in instances.Records)
            {
                // A reflected basis reverses authored asymmetric details and triangle winding.
                Assert.Greater(record.Matrix.determinant, 0, "Every orientation must be a proper rotation with positive scale.");
                AssertVector(new Vector3(tx, ty, 0), record.Matrix.MultiplyVector(Vector3.right).normalized);
                AssertVector(Vector3.back, record.Matrix.MultiplyVector(Vector3.up).normalized);
                AssertVector(new Vector3(nx, ny, 0), record.Matrix.MultiplyVector(Vector3.back).normalized);
                var bounds = new Bounds(record.Matrix.MultiplyPoint3x4(Vector3.zero), Vector3.zero);
                foreach (float x in new[] { 0, piece.Size.x })
                    foreach (float y in new[] { 0, piece.Size.y })
                        foreach (float z in new[] { 0, piece.Size.z })
                            bounds.Encapsulate(record.Matrix.MultiplyPoint3x4(new Vector3(x, y, z)));
                Assert.AreEqual(record.Footprint.xMin, bounds.min.x, .00001f);
                Assert.AreEqual(record.Footprint.xMax, bounds.max.x, .00001f);
                Assert.AreEqual(record.Footprint.yMin, bounds.min.y, .00001f);
                Assert.AreEqual(record.Footprint.yMax, bounds.max.y, .00001f);
                Assert.AreEqual(.04f - piece.Size.y, bounds.min.z, .00001f);
                Assert.AreEqual(.04f, bounds.max.z, .00001f);
            }

            // Different lengths still share the complete vertical end plane, from the
            // floor/front edge to the back of the crown, for all four wall normals.
            foreach (float y in new[] { 0, piece.Size.y })
                foreach (float z in new[] { 0, piece.Size.z })
                    AssertVector(instances.Records[0].Matrix.MultiplyPoint3x4(new Vector3(piece.Size.x, y, z)),
                        instances.Records[1].Matrix.MultiplyPoint3x4(new Vector3(0, y, z)));
        }

        [Test]
        public void Twenty_hotels_use_the_complete_baked_kit_in_bounded_batches_and_release_owned_resources()
        {
            var config = ConfigLoader.Load();
            var kit = WallKit.Load();
            Assert.IsNotNull(kit, "The imported, baked kit must be available to the real renderer.");
            foreach (string id in PieceIds)
            {
                Assert.IsNotNull(kit.Get(id), id);
                Assert.IsNotNull(kit.Get(id).Mesh, id);
            }
            Assert.IsNotNull(kit.Atlas);
            var build = typeof(GameManager).GetMethod("BuildScene3D", Private);
            var rootField = typeof(GameManager).GetField("worldRoot", Private);
            var assetsField = typeof(GameManager).GetField("sceneAssets", Private);
            var instancesField = typeof(GameManager).GetField("wallInstances", Private);
            var updateStates = typeof(GameManager).GetMethod("UpdateWallStateTexture", Private);
            var sourceVertices = kit.Pieces.ToDictionary(piece => piece.Mesh, piece => piece.Mesh.vertices);
            int baselineBatches = RuntimeWallMeshCount();
            var vertices = new List<Vector3>();
            var modes = new List<Vector2>();
            var states = new List<Vector2>();
            bool? artOverride = Sprites.ArtOverride;
            var randomState = Random.state;
            float timeScale = Time.timeScale;
            try
            {
                for (int seed = 1; seed <= 20; seed++)
                {
                    var go = new GameObject("Wall renderer integration " + seed);
                    var game = go.AddComponent<GameManager>();
                    Object[] owned = null;
                    try
                    {
                        // Simulation supplies a complete seeded map and match without starting the
                        // editor frame loop. Invoke the production renderer explicitly, including
                        // real imported meshes, atlas, material and animated-state texture.
                        game.StartSimulation(config, seed, false);
                        build.Invoke(game, null);
                        foreach (string id in PieceIds)
                            Assert.IsTrue(game.WallPieceCounts.TryGetValue(id, out int count) && count > 0,
                                "Missing placed kit piece " + id + " at seed " + seed);
                        Assert.AreEqual(game.Map.Rooms.Count, game.WallPieceCounts["door_frame"]);
                        Assert.Greater(game.WallBatchCount, 0);
                        Assert.Greater(game.WallTriangleCount, 0);

                        var world = (Transform)rootField.GetValue(game);
                        Assert.IsNotNull(world);
                        var filters = world.GetComponentsInChildren<MeshFilter>();
                        var wallFilters = filters.Where(filter => filter.sharedMesh != null && filter.sharedMesh.name.StartsWith("Hotel walls ")).ToArray();
                        var instances = (WallInstances)instancesField.GetValue(game);
                        Assert.IsNotNull(instances);
                        Assert.AreEqual(game.WallPieceCounts.Values.Sum(), instances.InstanceCount);
                        Assert.AreEqual(PieceIds.Length, instances.GroupCount, "The seven reusable source meshes must remain shared.");
                        Assert.AreEqual(game.WallBatchCount, wallFilters.Length + instances.MaxDrawCalls + 1);
                        int triangles = 0;
                        int generatedVertices = 0;
                        Material shared = (Material)typeof(GameManager).GetField("hotelWallMaterial",Private).GetValue(game);
                        foreach (var filter in filters)
                        {
                            Assert.IsNotNull(filter.sharedMesh, "Missing generated mesh");
                            var renderer = filter.GetComponent<MeshRenderer>();
                            Assert.IsNotNull(renderer);
                            Assert.IsNotNull(renderer.sharedMaterial, filter.name);
                            Assert.IsNotNull(renderer.sharedMaterial.shader, filter.name);
                        }
                        foreach (var filter in wallFilters)
                        {
                            var mesh = filter.sharedMesh;
                            var material = filter.GetComponent<MeshRenderer>().sharedMaterial;
                            Assert.AreEqual("BadApple/HotelWall", material.shader.name);
                            Assert.AreSame(kit.Atlas, material.mainTexture);
                            Assert.IsTrue(material.enableInstancing, "The shared wall material must permit hardware instancing.");
                            if (shared == null) shared = material; else Assert.AreSame(shared, material, "wall batches must share one atlas material");
                            Assert.AreEqual(1, mesh.subMeshCount);
                            triangles += (int)mesh.GetIndexCount(0) / 3;
                            generatedVertices += mesh.vertexCount;
                            Assert.Greater(mesh.vertexCount, 0);
                            Assert.IsTrue(mesh.isReadable);
                            Assert.LessOrEqual(mesh.bounds.max.x, game.Map.W + .01f);
                            Assert.LessOrEqual(mesh.bounds.max.y, game.Map.H + .01f);
                            Assert.GreaterOrEqual(mesh.bounds.min.x, -.01f);
                            Assert.GreaterOrEqual(mesh.bounds.min.y, -.01f);
                        }
                        Assert.Less(generatedVertices, 10000, "Only small separator core meshes may be duplicated per hotel; decorative kit meshes must remain shared.");
                        var glow = filters.Single(filter=>filter.sharedMesh.name=="Wall sconce glow halos");
                        Assert.AreEqual(game.WallTriangleCount, triangles + instances.TotalTriangles + glow.sharedMesh.GetIndexCount(0)/3);
                        LampDistributionTests.AssertCoverage(game);
                        for(int i=0;i<game.Walls.Perimeter.Corners.Count;i++)
                        {
                            var corner=game.Walls.Perimeter.Corners[i];
                            int state=game.Walls.Runs.Count+game.Walls.Perimeter.Spans.Count+i;
                            Assert.AreEqual(1,instances.Records.Count(record=>record.Mode==0&&record.StateId==state&&record.Footprint==corner.Bounds),
                                "One corner asset per turn, seed "+seed);
                        }
                        foreach(var record in instances.Records.Where(record=>record.Mode==1))
                            Assert.IsTrue(record.Normal==Vector2.down||record.Normal==Vector2.left,"Wall face points away from the fixed camera.");
                        Assert.AreEqual(0,wallFilters.Length,"Thin shared walls must not rebuild the old full-cell separator blocks.");
                        Assert.AreEqual(instances.TotalTriangles, instances.Records.Sum(record => record.TriangleCount));
                        var stateTexture = shared.GetTexture("_WallStates") as Texture2D;
                        Assert.IsNotNull(stateTexture);
                        Assert.AreEqual(2, stateTexture.height);
                        Assert.AreEqual(FilterMode.Point, stateTexture.filterMode);
                        Assert.AreEqual(TextureWrapMode.Clamp, stateTexture.wrapMode);
                        int stateCount = game.Walls.Runs.Count + game.Walls.Perimeter.Spans.Count + game.Walls.Perimeter.Corners.Count + game.Map.Rooms.Count;
                        Assert.GreaterOrEqual(stateTexture.width, stateCount);
                        foreach (var record in instances.Records)
                        {
                            Assert.AreSame(kit.Get(record.Piece.Id).Mesh, record.Mesh, "An instance must reference its original imported source mesh.");
                            Assert.Greater(record.Matrix.determinant, 0, "Placed kit details must never be mirrored.");
                            Assert.AreEqual((int)record.Mesh.GetIndexCount(0) / 3, record.TriangleCount);
                            Assert.That(record.StateId, Is.InRange(0, stateCount - 1));
                            Assert.AreEqual(record.StateId, Mathf.FloorToInt(record.Data.x * stateTexture.width));
                            Assert.AreEqual(record.Piece.Size.y, record.Data.y, .0001f);
                            Assert.AreEqual(record.Mode, record.Data.z);
                            Assert.LessOrEqual(record.Bounds.max.x, game.Map.W + .01f);
                            Assert.LessOrEqual(record.Bounds.max.y, game.Map.H + .01f);
                            Assert.GreaterOrEqual(record.Bounds.min.x, -.01f);
                            Assert.GreaterOrEqual(record.Bounds.min.y, -.01f);
                        }

                        // The registered full-height models and low caps are mutually exclusive.
                        // Count actual active groups for a whole hotel, with no camera/frustum
                        // discount, at the portable instance budget used by the production renderer.
                        // Read production state heights so corners, separator cores and doors
                        // follow the same Up/Cutaway/Down decisions as the running game.
                        foreach (var mode in new[] { WallDisplayMode.Up, WallDisplayMode.Cutaway, WallDisplayMode.Down })
                        {
                            game.SetWallMode(mode);
                            updateStates.Invoke(game, new object[] { true });
                            int submissions = FullHotelSubmissions(instances, stateTexture, WallInstances.MaxBatchCapacity, wallFilters.Length + 1);
                            TestContext.WriteLine("128-instance full hotel: seed=" + seed + " mode=" + mode + " submissions=" + submissions);
                            Assert.Less(submissions, 30, "Active full-map wall submissions at capacity128, seed " + seed + ", mode " + mode);
                        }

                        // Every seed exercises placement, batching and disposal. Three separated
                        // layouts additionally inspect every transformed core and instance
                        // vertex. Imported vertex data is read just once per reusable mesh.
                        if (seed == 1 || seed == 10 || seed == 20)
                        {
                            foreach (var filter in wallFilters)
                            {
                                var mesh = filter.sharedMesh;
                                mesh.GetVertices(vertices); mesh.GetUVs(2, modes); mesh.GetUVs(1, states);
                                Assert.AreEqual(vertices.Count, modes.Count);
                                Assert.AreEqual(vertices.Count, states.Count);
                                for (int i = 0; i < vertices.Count; i++)
                                {
                                    var point = filter.transform.TransformPoint(vertices[i]);
                                    int state = Mathf.FloorToInt(states[i].x * stateTexture.width);
                                    AssertVertex(game.Map, point, modes[i].x, state, stateCount, seed);
                                }
                            }
                            foreach (var record in instances.Records)
                                foreach (var point in sourceVertices[record.Mesh])
                                    try { AssertVertex(game.Map, record.Matrix.MultiplyPoint3x4(point), record.Mode, record.StateId, stateCount, seed); }
                                    catch(AssertionException){TestContext.WriteLine(record.Piece.Id+" "+record.Footprint+" state "+record.StateId+" map "+game.Map.Seed);throw;}
                        }
                        owned = ((IEnumerable<Object>)assetsField.GetValue(game)).ToArray();
                        Assert.IsTrue(owned.Any(asset => asset is Mesh));
                        Assert.IsTrue(owned.Any(asset => asset is Material));
                        Assert.IsTrue(owned.Contains(stateTexture));
                    }
                    finally
                    {
                        if (game != null) game.DisposeSimulation();
                        else if (go != null) Object.DestroyImmediate(go);
                    }
                    Assert.IsTrue(game == null);
                    Assert.IsTrue(owned.All(asset => asset == null), "Generated native resources leaked after seed " + seed + ": " +
                        string.Join(", ", owned.Where(asset => asset != null).Select(asset => asset.GetType().Name + " " + asset.name)));
                    Assert.AreEqual(baselineBatches, RuntimeWallMeshCount(), "Wall batch meshes accumulated after seed " + seed);
                    Assert.IsNotNull(kit.Atlas, "Disposal must preserve the shared imported atlas");
                    foreach (var piece in kit.Pieces) Assert.IsNotNull(piece.Mesh, "Disposal must preserve the imported mesh " + piece.Id);
                }
            }
            finally
            {
                Sprites.ArtOverride = artOverride;
                Random.state = randomState;
                Time.timeScale = timeScale;
            }
        }

        static int RuntimeWallMeshCount() => Resources.FindObjectsOfTypeAll<Mesh>().Count(mesh => mesh.name.StartsWith("Hotel walls "));

        static void AssertVector(Vector3 expected, Vector3 actual) =>
            Assert.Less(Vector3.Distance(expected, actual), .00001f, "Expected " + expected + ", actual " + actual);

        static int FullHotelSubmissions(WallInstances instances, Texture2D stateTexture, int capacity, int coreBatches)
        {
            var states = stateTexture.GetPixels(0, 0, stateTexture.width, 1);
            return coreBatches + instances.Records.Where(record =>
                record.Mode >= 5 ? false :
                record.Mode == 1 ? states[record.StateId].g > .46f :
                record.Mode == 2 ? states[record.StateId].g <= .46f : true)
                .GroupBy(record => record.Mesh)
                .Sum(group => (group.Count() + capacity - 1) / capacity);
        }

        static void AssertVertex(HotelMap map, Vector3 point, float mode, int state, int stateCount, int seed)
        {
            if (state < 0 || state >= stateCount)
                Assert.Fail("Mesh vertex has an invalid wall-state index at seed " + seed);
            if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z) ||
                float.IsInfinity(point.x) || float.IsInfinity(point.y) || float.IsInfinity(point.z))
                Assert.Fail("Non-finite wall vertex at seed " + seed);
            var cell = HotelMap.ToTile(point);
            // Vertices exactly on a floor edge are legal. Only strict intrusion is rejected;
            // the closed edge belongs to the wall.
            if (point.x - cell.x < .0001f || cell.x + 1 - point.x < .0001f ||
                point.y - cell.y < .0001f || cell.y + 1 - point.y < .0001f) return;
            var tile = map.Get(cell.x, cell.y);
            if (!WallGraph.IsWalkable(tile)) return;
            bool doorLintel = Mathf.Abs(mode - 3) < .01f && tile == Tile.Door && .04f - point.z >= 1.149f;
            if (!doorLintel) Assert.Fail("Rendered wall enters a walkable tile at " + point + ", seed " + seed + ", mode " + mode);
        }
    }
}
