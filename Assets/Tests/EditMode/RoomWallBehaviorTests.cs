using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BadAppleHotel.Config;
using BadAppleHotel.Game;
using NUnit.Framework;
using UnityEngine;

namespace BadAppleHotel.Tests
{
    public class RoomWallBehaviorTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        GameManager game;
        Texture2D targets;
        WallInstances instances;
        Random.State random;
        bool? art;
        float timeScale;

        void Build(int seed)
        {
            random = Random.state; art = Sprites.ArtOverride; timeScale = Time.timeScale;
            game = new GameObject("Room wall behavior " + seed).AddComponent<GameManager>();
            game.StartSimulation(ConfigLoader.Load(), seed, false);
            typeof(GameManager).GetMethod("BuildScene3D", Private).Invoke(game, null);
            targets = Field<Texture2D>("wallStateTexture");
            instances = Field<WallInstances>("wallInstances");
            Assert.AreEqual(10, game.Map.Rooms.Count);
            game.Human.IsHuman = true;
            game.Human.Ai = null;
        }

        [TearDown] public void Cleanup()
        {
            if (game != null) game.DisposeSimulation();
            Random.state = random; Sprites.ArtOverride = art; Time.timeScale = timeScale;
        }

        [TestCase(11)] [TestCase(41)] [TestCase(83)]
        public void Every_occupied_room_uploads_near_baseboards_far_walls_and_matching_joins_cores_and_door_frames(int seed)
        {
            Build(seed);
            game.SetWallMode(WallDisplayMode.Cutaway);
            int joins = 0, cores = 0;
            foreach (var room in game.Map.Rooms)
            {
                game.Human.Pos = HotelMap.Center(room.BedTile);
                UpdateTargets();
                var expected = ExpectedRoomRuns(room);
                Assert.IsTrue(expected.Values.Contains(WallGraph.DownHeight), "Missing near wall in room " + room.Index);
                Assert.IsTrue(expected.Values.Contains(WallGraph.FullHeight), "Missing far wall in room " + room.Index);
                foreach (var item in expected) AssertTarget(item.Key, item.Value, "room " + room.Index + " run");

                int index = game.Walls.Runs.Count;
                foreach (var join in game.Walls.Joins)
                {
                    var roomSides = join.IncidentRunIds.Where(expected.ContainsKey).ToArray();
                    if (roomSides.Length > 0)
                    {
                        AssertTarget(index, roomSides.Min(id => expected[id]), "room " + room.Index + " join");
                        joins++;
                    }
                    index++;
                }
                foreach (var core in game.Walls.Cores)
                {
                    var roomSides = core.IncidentRunIds.Where(expected.ContainsKey).ToArray();
                    if (roomSides.Length > 0)
                    {
                        AssertTarget(index, roomSides.Min(id => expected[id]), "room " + room.Index + " core");
                        cores++;
                    }
                    index++;
                }
                var frame = instances.Records.Single(record => record.Piece.Id == "door_frame" &&
                    Vector2.Distance(record.Footprint.center, HotelMap.Center(room.DoorTile)) < .001f);
                Assert.AreEqual(index + game.Map.Rooms.IndexOf(room), frame.StateId);
                Assert.AreEqual(3, frame.Mode, "The actual frame must consume its room-specific target.");
                AssertTarget(frame.StateId, HeightForRoomFace(room.DoorInside - room.DoorTile), "room " + room.Index + " frame");
            }
            Assert.Greater(joins, 0, "This seeded hotel must exercise room corner targets.");
            Assert.Greater(cores, 0, "This seeded hotel must exercise filled separator targets.");
        }

        [TestCase(11)] [TestCase(41)] [TestCase(83)]
        public void Occupied_room_overrides_selection_and_manual_modes_override_both(int seed)
        {
            Build(seed);
            foreach (var room in game.Map.Rooms)
            {
                game.Human.Pos = HotelMap.Center(room.BedTile);
                game.WallFocusRoom = null;
                game.SetWallMode(WallDisplayMode.Cutaway);
                UpdateTargets();
                var occupiedTargets = targets.GetPixels(0, 0, targets.width, 1);
                game.WallFocusRoom = game.Map.Rooms[(room.Index + 1) % game.Map.Rooms.Count];
                UpdateTargets();
                for (int i = 0; i < StateCount; i++)
                {
                    Assert.AreEqual(occupiedTargets[i].g, targets.GetPixel(i, 0).g, .00001f,
                        "Selection changed occupied room " + room.Index + " state " + i);
                    Assert.AreEqual(occupiedTargets[i].a, targets.GetPixel(i, 0).a, .00001f);
                }
                foreach (var mode in new[] { WallDisplayMode.Up, WallDisplayMode.Down })
                {
                    game.SetWallMode(mode); UpdateTargets();
                    for (int i = 0; i < StateCount; i++)
                        AssertTarget(i, mode == WallDisplayMode.Up ? WallGraph.FullHeight : WallGraph.DownHeight,
                            "Manual " + mode + " in room " + room.Index);
                }
            }

            // Selection also has observable effect when there is no occupied room.
            // Put the observer outside the map to isolate it from hallway fading.
            game.Human.Pos = new Vector2(-10, -10);
            game.SetWallMode(WallDisplayMode.Cutaway);
            foreach (var room in game.Map.Rooms)
            {
                game.WallFocusRoom = room; UpdateTargets();
                foreach (var item in ExpectedRoomRuns(room)) AssertTarget(item.Key, item.Value, "Selected room " + room.Index);
            }
        }

        [TestCase(11)] [TestCase(41)] [TestCase(83)]
        public void Every_room_has_a_placed_far_wall_lamp_that_remains_full_height_and_lights_its_floor(int seed)
        {
            Build(seed);
            var lamps = Field<List<HotelLighting.Lamp>>("wallLamps");
            var lighting = Field<HotelLighting>("hotelLighting");
            Assert.AreEqual(instances.Records.Count(record => record.Piece.Id == "wall_lamp"), lamps.Count);
            Assert.AreEqual(lamps.Count, lighting.LampCount, "Every placed lamp must reach a usable side of its wall.");
            game.SetWallMode(WallDisplayMode.Cutaway);
            foreach (var room in game.Map.Rooms)
            {
                game.Human.Pos = HotelMap.Center(room.BedTile); UpdateTargets();
                var farLamps = instances.Records.Where(record => record.Piece.Id == "wall_lamp" &&
                    HeightForRoomFace(record.Normal) == WallGraph.FullHeight &&
                    targets.GetPixel(record.StateId, 0).g > WallGraph.FullHeight - .001f &&
                    room.ContainsInterior(HotelMap.ToTile(record.Footprint.center + record.Normal * (game.Walls.Thickness / 2 + .1f)))).ToArray();
                Assert.Greater(farLamps.Length, 0, "No visible far-wall lamp in room " + room.Index);
                foreach (var record in farLamps)
                {
                    Assert.AreEqual(1, record.Mode);
                    AssertTarget(record.StateId, WallGraph.FullHeight, "Far lamp in room " + room.Index);
                    Vector2 position = record.Footprint.center + record.Normal * game.Walls.Thickness / 2;
                    Assert.IsTrue(lamps.Any(lamp => Vector2.Distance(lamp.Position, position) < .001f && lamp.Normal == record.Normal));
                    Assert.Greater(lighting.Sample(position + record.Normal * .5f), .5f,
                        "The far-wall lamp in room " + room.Index + " should illuminate its adjacent floor.");
                }
            }
        }

        [TestCase(11)] [TestCase(41)] [TestCase(83)] [TestCase(40102026)]
        public void Short_doorway_jambs_follow_the_near_or_far_door_face_including_the_captured_room(int seed)
        {
            Build(seed);
            if (seed == 40102026) Assert.AreEqual(580023594, game.Map.Seed, "Reproduce the visual QA hotel.");
            game.SetWallMode(WallDisplayMode.Cutaway);
            int near = 0, far = 0;
            foreach (var room in game.Map.Rooms)
            {
                game.Human.Pos = HotelMap.Center(room.BedTile); UpdateTargets();
                float expected = HeightForRoomFace(room.DoorInside - room.DoorTile);
                foreach (var run in game.Walls.Runs)
                {
                    if (!run.EdgeIds.Any(id => game.Walls.Edges[id].WalkableCell == room.DoorTile) ||
                        run.EdgeIds.Any(id => room.ContainsInterior(game.Walls.Edges[id].WalkableCell))) continue;
                    Assert.IsTrue(WallVisibility.BordersRoom(game.Walls, run, room), "A jamb is part of its doorway's room boundary.");
                    AssertTarget(run.Id, expected, "Door-only jamb in room " + room.Index);
                    // Some jamb strips are owned by a filled core instead of the run.
                    // Include those linked states to exercise the meshes actually drawn.
                    var ownedStates = new HashSet<int> { run.Id };
                    for (int i = 0; i < game.Walls.Joins.Count; i++)
                        if (game.Walls.Joins[i].IncidentRunIds.Contains(run.Id)) ownedStates.Add(game.Walls.Runs.Count + i);
                    for (int i = 0; i < game.Walls.Cores.Count; i++)
                        if (game.Walls.Cores[i].IncidentRunIds.Contains(run.Id))
                            ownedStates.Add(game.Walls.Runs.Count + game.Walls.Joins.Count + i);
                    Assert.IsTrue(instances.Records.Any(record => ownedStates.Contains(record.StateId) && record.Mode == 1),
                        "The regression must include the actual full jamb mesh or its structural core.");
                    if (expected == WallGraph.DownHeight) near++; else far++;
                }
            }
            Assert.Greater(near, 0, "Exercise a near doorway, whose two perpendicular jamb normals must both lower.");
            Assert.Greater(far, 0, "Exercise a far doorway, whose short jambs must both remain full height.");
        }

        [Test]
        public void Concave_returns_lower_over_room_floor_but_preserve_the_far_backdrop()
        {
            Build(40102026);
            var room = game.Map.Rooms.First(r => game.Walls.Runs.Any(run =>
                run.EdgeIds.Any(id => r.ContainsInterior(game.Walls.Edges[id].WalkableCell)) &&
                HeightForRoomFace(run.Normal) == WallGraph.FullHeight && ProjectsOverRoomFloor(run, r)));
            game.Human.Pos = HotelMap.Center(room.BedTile);
            game.SetWallMode(WallDisplayMode.Cutaway); UpdateTargets();
            var notch = game.Walls.Runs.First(run =>
                run.EdgeIds.Any(id => room.ContainsInterior(game.Walls.Edges[id].WalkableCell)) &&
                HeightForRoomFace(run.Normal) == WallGraph.FullHeight && ProjectsOverRoomFloor(run, room));
            AssertTarget(notch.Id, WallGraph.DownHeight, "Far-facing foreground return");
            int backdrops = 0;
            foreach (var run in game.Walls.Runs)
                if (run.EdgeIds.Any(id => room.FloorSet.Contains(game.Walls.Edges[id].WalkableCell)) &&
                    HeightForRoomFace(run.Normal) == WallGraph.FullHeight && !ProjectsOverRoomFloor(run, room))
                {
                    AssertTarget(run.Id, WallGraph.FullHeight, "Unobstructing far backdrop"); backdrops++;
                }
            Assert.Greater(backdrops, 0);
            var corridor = game.Walls.Runs.First(run => run.EdgeIds.All(id =>
                game.Map.Get(game.Walls.Edges[id].WalkableCell.x,game.Walls.Edges[id].WalkableCell.y)==Tile.Corridor));
            Assert.IsFalse(WallVisibility.BordersRoom(game.Walls, corridor, room));
            AssertTarget(corridor.Id, corridor.DefaultHeight, "Separate corridor boundary");
        }

        int StateCount => game.Walls.Runs.Count + game.Walls.Joins.Count + game.Walls.Cores.Count + game.Map.Rooms.Count;
        T Field<T>(string name) => (T)typeof(GameManager).GetField(name, Private).GetValue(game);
        void UpdateTargets() => typeof(GameManager).GetMethod("UpdateWallStateTexture", Private).Invoke(game, new object[] { false });

        Dictionary<int, float> ExpectedRoomRuns(RoomDef room)
        {
            var result = new Dictionary<int, float>();
            // Start with floor-boundary edges instead of calling the production BordersRoom helper.
            foreach (var edge in game.Walls.Edges)
                if (room.FloorSet.Contains(edge.WalkableCell))
                {
                    float height = HeightForRoomFace(edge.Normal);
                    if (height == WallGraph.FullHeight && ProjectsOverRoomFloor(game.Walls.Runs[edge.RunId], room)) height = WallGraph.DownHeight;
                    result[edge.RunId] = height;
                }
            // Door-only short sides follow the door plane. Existing perimeter entries
            // take precedence when a longer run also borders actual room floor.
            foreach (var edge in game.Walls.Edges)
                if (edge.WalkableCell == room.DoorTile && !result.ContainsKey(edge.RunId))
                    result[edge.RunId] = HeightForRoomFace(room.DoorInside - room.DoorTile);
            return result;
        }

        static bool ProjectsOverRoomFloor(WallRun run, RoomDef room)
        {
            // Independently project the eight wall-box vertices onto the floor and use
            // separating axes against each floor square. This checks positive overlap
            // area, rejecting a mere corner graze, without using production CoversAny.
            var projected = new List<Vector2>();
            foreach (float x in new[] { run.Footprint.xMin, run.Footprint.xMax })
                foreach (float y in new[] { run.Footprint.yMin, run.Footprint.yMax })
                    foreach (float z in new[] { 0, -WallGraph.FullHeight })
                    {
                        var corner = new Vector3(x, y, z);
                        projected.Add(corner - HotelView3D.Forward * (z / HotelView3D.Forward.z));
                    }
            var sweep = (Vector2)HotelView3D.Forward;
            var axes = new[] { Vector2.right, Vector2.up, new Vector2(-sweep.y, sweep.x).normalized };
            foreach (var tile in room.Floor)
            {
                var floor = new[] { new Vector2(tile.x + .0001f, tile.y + .0001f), new Vector2(tile.x + .9999f, tile.y + .0001f),
                    new Vector2(tile.x + .9999f, tile.y + .9999f), new Vector2(tile.x + .0001f, tile.y + .9999f) };
                bool intersects = true;
                foreach (var axis in axes)
                {
                    float wallMin = projected.Min(point => Vector2.Dot(point, axis));
                    float wallMax = projected.Max(point => Vector2.Dot(point, axis));
                    float floorMin = floor.Min(point => Vector2.Dot(point, axis));
                    float floorMax = floor.Max(point => Vector2.Dot(point, axis));
                    if (Mathf.Min(wallMax, floorMax) - Mathf.Max(wallMin, floorMin) <= .00001f)
                    { intersects = false; break; }
                }
                if (intersects) return true;
            }
            return false;
        }

        static float HeightForRoomFace(Vector2 inward)
        {
            // The fixed camera sees the lower-X/lower-Y faces first. Those room faces
            // point right/up; the opposite two sides form the visible far-wall backdrop.
            Assert.Greater(HotelView3D.Forward.x, 0); Assert.Greater(HotelView3D.Forward.y, 0);
            return inward == Vector2.right || inward == Vector2.up ? WallGraph.DownHeight : WallGraph.FullHeight;
        }

        void AssertTarget(int state, float height, string context)
        {
            // G/A are the destination height/opacity sampled by both core meshes and kit
            // instances. R/B can still hold the previous values during the 150ms tween.
            var pixel = targets.GetPixel(state, 0);
            Assert.AreEqual(height, pixel.g, .00001f, context + " state " + state);
            Assert.AreEqual(1, pixel.a, .00001f, context + " should remain opaque");
        }
    }
}
