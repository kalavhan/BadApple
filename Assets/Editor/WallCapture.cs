using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BadAppleHotel.Game;
using UnityEditor;
using UnityEngine;
using Random = UnityEngine.Random;

namespace BadAppleHotel.EditorTools
{
    /// <summary>
    /// Reproducible, real Game View captures. Run in a disposable editor project using
    /// -executeMethod BadAppleHotel.EditorTools.WallCapture.Run (without -quit).
    /// BADAPPLE_QA_WIDTH is 1560 or 960; BADAPPLE_QA_OUT selects the output directory.
    /// This command exits the editor after writing its images and JSON report.
    /// </summary>
    [InitializeOnLoad]
    public static class WallCapture
    {
        const string Session = "BadApple.WallCapture";
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly string[] Names = { "l-shaped-room", "corridor-corner", "door", "solid-separator", "walk-along-wall", "walls-up", "walls-down", "aligned-east-west-run", "aligned-north-south-run", "glowing-sconce", "south-room-corner", "outer-boundary-cutaway" };

        [Serializable]
        sealed class PieceCount
        {
            public string id;
            public int placed;
        }

        [Serializable]
        sealed class Shot
        {
            public string scenario, file, wallMode, visibility;
            public int width, height, mapSeed, roomIndex, wallBatches, wallTriangles, visibleWallTriangles, actualWallDrawCalls;
            public int editorDrawCalls, editorBatches, sampledFrames;
            public float editorAverageFrameMs, walkedDistance, cameraSize;
            public Vector2 cameraGroundFocus, actorPosition;
            public bool fogEnabled;
            public List<PieceCount> wallPieces = new List<PieceCount>();
        }

        [Serializable]
        sealed class Report
        {
            public string unityVersion, platform, measurementScope, generatedUtc;
            public int randomSeed;
            public bool success;
            public List<Shot> shots = new List<Shot>();
            public List<string> errors = new List<string>();
        }

        static GameManager game;
        static RoomDef room;
        static Report report;
        static string output, pendingFile;
        static int width, scenario, phase, fixtureSlot = -1, frameCount;
        static Vector2 focus, actor, walkStart, walkEnd;
        static float cameraSize, frameSeconds, walked;
        static double started, deadline, lastTick;
        static bool cameraOverride, walking;

        static WallCapture()
        {
            if (SessionState.GetBool(Session, false)) Hook();
        }

        public static void Run()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("WallCapture.Run must start outside Play Mode.");
            width = int.TryParse(Environment.GetEnvironmentVariable("BADAPPLE_QA_WIDTH"), out int parsed) ? parsed : 1560;
            if (width != 1560 && width != 960) throw new ArgumentException("BADAPPLE_QA_WIDTH must be 1560 (19.5:9) or 960 (4:3), both at 720 pixels high.");
            output = Environment.GetEnvironmentVariable("BADAPPLE_QA_OUT");
            if (string.IsNullOrWhiteSpace(output)) output = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Builds", "WallQA");
            Directory.CreateDirectory(output);
            SessionState.SetBool(Session, true);
            SessionState.SetInt(Session + ".Width", width);
            SessionState.SetString(Session + ".Output", output);
            SessionState.SetInt(Session + ".Seed", int.TryParse(Environment.GetEnvironmentVariable("BADAPPLE_QA_SEED"), out int seed) ? seed : 40102026);
            ConfigureGameView(width, 720);
            Hook();
            Sprites.UseArt = true;
            EditorApplication.isPlaying = true;
        }

        static void Hook()
        {
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Camera.onPreCull -= FrameCamera; Camera.onPreCull += FrameCamera;
            Camera.onPostRender -= CountFrame; Camera.onPostRender += CountFrame;
            Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
            started = EditorApplication.timeSinceStartup;
            lastTick = started;
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Session, false)) return;
            try
            {
                if (report == null)
                {
                    width = SessionState.GetInt(Session + ".Width", 1560);
                    output = SessionState.GetString(Session + ".Output", "/tmp/badapple-wall-qa");
                    report = new Report { randomSeed = SessionState.GetInt(Session + ".Seed", 40102026), unityVersion = Application.unityVersion,
                        platform = Application.platform.ToString(), generatedUtc = DateTime.UtcNow.ToString("o"),
                        measurementScope = "Unity Editor Game View under the host graphics driver; draw calls and frame times are diagnostic samples, not Android phone performance. Repeated kit pieces share GPU-instanced source meshes. wallBatches and wallTriangles include all registered full and low variants, including inactive ones; they are not per-frame submitted counts. visibleWallTriangles counts active triangles submitted after camera culling; actualWallDrawCalls counts wall draw API submissions including visible core batches (the driver may split instance batches). Geometry shots use a clairvoyance fixture; hallway shots use normal gameplay fog." };
                }
                if (!EditorApplication.isPlaying || GameManager.Instance?.Cfg == null || GameManager.Instance.Map == null)
                {
                    if (EditorApplication.timeSinceStartup - started > 240) Fail("Timed out waiting for playable game initialization.");
                    return;
                }
                double now = EditorApplication.timeSinceStartup;
                float dt = Mathf.Clamp((float)(now - lastTick), 0, .05f); lastTick = now;
                if (game == null) { Initialize(); phase = 0; deadline = now + 1; return; }
                if (phase == 0)
                {
                    if (now < deadline) return;
                    PrepareScenario();
                    phase = 1; deadline = now + (walking ? 2.2 : 1.5);
                }
                else if (phase == 1)
                {
                    if (walking)
                    {
                        Vector2 before = game.Human.Pos;
                        Vector2 step = Vector2.ClampMagnitude(walkEnd - before, 1.4f * dt);
                        game.Human.Pos = TileMovement.Slide(before, step, (x, y) => game.WalkableFor(game.Human, x, y), GameManager.ResidentRadius, game.Walls);
                        game.Human.Facing = (walkEnd - walkStart).normalized;
                        walked += Vector2.Distance(before, game.Human.Pos);
                    }
                    if (now < deadline || frameCount < 4) return;
                    if (walking && walked < .5f) throw new InvalidOperationException("Walking capture failed to advance at least half a tile along the wall.");
                    pendingFile = Path.Combine(output, "walls-" + width + "-" + Names[scenario] + ".png");
                    if (File.Exists(pendingFile)) File.Delete(pendingFile);
                    ScreenCapture.CaptureScreenshot(pendingFile);
                    report.shots.Add(new Shot { scenario = Names[scenario], file = pendingFile, width = Screen.width, height = Screen.height,
                        mapSeed = game.Map.Seed, roomIndex = room.Index, wallMode = game.WallMode.ToString(), fogEnabled = game.FogActive,
                        visibility = game.FogActive ? "Normal resident sight radius and line-of-sight fog" : "Clairvoyance fixture for unobstructed geometry inspection",
                        wallBatches = game.WallBatchCount, wallTriangles = game.WallTriangleCount, visibleWallTriangles = game.VisibleWallTriangles,
                        actualWallDrawCalls = game.ActualWallDrawCalls, editorDrawCalls = UnityStats.drawCalls,
                        editorBatches = UnityStats.batches, sampledFrames = frameCount, editorAverageFrameMs = frameCount > 0 ? frameSeconds / frameCount * 1000 : 0,
                        walkedDistance = walked, cameraSize = cameraSize, cameraGroundFocus = focus, actorPosition = game.Human.Pos,
                        wallPieces = game.WallPieceCounts.OrderBy(pair => pair.Key).Select(pair => new PieceCount { id = pair.Key, placed = pair.Value }).ToList() });
                    phase = 2; deadline = now + 30;
                }
                else if (phase == 2)
                {
                    if (File.Exists(pendingFile) && new FileInfo(pendingFile).Length > 1000)
                    {
                        using (var stream = File.OpenRead(pendingFile))
                        {
                            var header = new byte[24];
                            if (stream.Read(header, 0, header.Length) != header.Length || PngInt(header, 16) != width || PngInt(header, 20) != 720)
                                throw new InvalidOperationException("Unexpected screenshot dimensions: " + pendingFile);
                        }
                        scenario++;
                        if (scenario == Names.Length) Finish();
                        else { phase = 0; deadline = now + .25; }
                    }
                    else if (now > deadline) Fail("Screenshot was not written: " + pendingFile);
                }
            }
            catch (Exception error) { Fail(error.ToString()); }
        }

        static void Initialize()
        {
            game = GameManager.Instance;
            Random.InitState(report.randomSeed);
            game.StartMatch(Role.Resident, "stitchwork_chef");
            game.SetSpeed(0);
            game.Announce("", 0);
            room = game.Map.Rooms.OrderByDescending(r => (r.Isolated ? 0 : 1000) + MissingRectangleArea(r)).First();
            if (!game.Claim(game.Human, room, true)) throw new InvalidOperationException("Could not claim the QA room.");
            game.Human.Room.DoorOpen = true;
            typeof(GameManager).GetMethod("RefreshDoor", Private).Invoke(game, new object[] { game.Human.Room });
            fixtureSlot = game.Human.Room.EmptySlot();
            if (fixtureSlot < 0) throw new InvalidOperationException("The QA room has no space for the clairvoyance fixture.");
            var corridorRuns = game.Walls.Runs.Where(run => run.EdgeIds.All(id => game.Map.Get(game.Walls.Edges[id].WalkableCell.x,
                game.Walls.Edges[id].WalkableCell.y) == Tile.Corridor)).OrderByDescending(run => run.Length);
            bool foundWalk = false;
            foreach (var run in corridorRuns)
            {
                if (run.Length < 4) continue;
                Vector2 tangent = (run.B - run.A).normalized;
                Vector2 a = run.A + tangent * .65f + run.Normal * (GameManager.ResidentRadius + .035f);
                Vector2 b = run.B - tangent * .65f + run.Normal * (GameManager.ResidentRadius + .035f);
                if (!TileMovement.Clear(a, b, (x, y) => game.WalkableFor(game.Human, x, y), GameManager.ResidentRadius, game.Walls)) continue;
                walkStart = a; walkEnd = a + tangent * Mathf.Min(4, Vector2.Distance(a, b)); foundWalk = true; break;
            }
            if (!foundWalk) throw new InvalidOperationException("No radius-clear corridor wall was available for the walking capture.");
            focus = RoomBoxCenter(room);
            cameraSize = RoomCameraSize(room);
            cameraOverride = true;
        }

        static void PrepareScenario()
        {
            var human = game.Human;
            human.Asleep = human.SleepRequested = false;
            game.HotelView = false;
            game.WallFocusRoom = null;
            game.SetWallMode(scenario == 5 || scenario >= 7 ? WallDisplayMode.Up : scenario == 6 ? WallDisplayMode.Down : WallDisplayMode.Cutaway);
            bool fog = scenario == 1 || scenario == 4;
            human.Room.Slots[fixtureSlot] = fog ? null : new TowerInstance { Def = game.Cfg.towers.towers.First(def => def.effect == "clairvoyance"), Tile = room.BuildTiles[fixtureSlot], SlotIndex = fixtureSlot };
            focus = RoomBoxCenter(room);
            actor = room.Floor.Where(cell => !room.IsBedTile(cell)).OrderBy(cell => Vector2.Distance(HotelMap.Center(cell), room.Center)).Select(HotelMap.Center).First();
            cameraSize = RoomCameraSize(room);
            walking = false;
            if (scenario == 1)
            {
                var corner = game.Walls.Joins.Where(join => join.Footprints.Count > 0 && CorridorNeighbours(join.Vertex, game.Map).Count >= 1 && IsHallwayCorner(join.Vertex, game.Map))
                    .OrderBy(join => Vector2.Distance(join.Vertex, HotelMap.Center(room.DoorOutside))).First();
                actor = CorridorNeighbours(corner.Vertex, game.Map).Select(HotelMap.Center).OrderBy(point => Vector2.Distance(point, room.Center)).First();
                focus = (Vector2)corner.Vertex + (actor - (Vector2)corner.Vertex) * .35f;
                cameraSize = 3.8f;
            }
            else if (scenario == 2)
            {
                actor = HotelMap.Center(room.DoorOutside);
                focus = HotelMap.Center(room.DoorTile);
                cameraSize = 3.5f;
            }
            else if (scenario == 3)
            {
                var core = game.Walls.Cores.Where(candidate => game.Walls.Edges.Any(edge => edge.SolidCell == candidate.Cell &&
                        game.Map.Get(edge.WalkableCell.x, edge.WalkableCell.y) == Tile.RoomFloor) &&
                    game.Walls.Edges.Any(edge => edge.SolidCell == candidate.Cell && game.Map.Get(edge.WalkableCell.x, edge.WalkableCell.y) == Tile.Corridor))
                    .OrderBy(candidate => Vector2.Distance(candidate.Footprint.center, HotelMap.Center(room.DoorTile))).First();
                var corridor = game.Walls.Edges.First(edge => edge.SolidCell == core.Cell && game.Map.Get(edge.WalkableCell.x, edge.WalkableCell.y) == Tile.Corridor);
                actor = HotelMap.Center(corridor.WalkableCell); focus = core.Footprint.center; cameraSize = 3.6f;
            }
            else if (scenario == 4)
            {
                actor = walkStart; focus = (walkStart + walkEnd) * .5f; cameraSize = 3.8f; walking = true;
            }
            else if (scenario == 9)
            {
                var pieces=(WallInstances)typeof(GameManager).GetField("wallInstances",Private).GetValue(game);
                var lamp=pieces.Records.Where(piece=>piece.Piece.Id=="wall_lamp"&&
                    (piece.Normal==Vector2.down||piece.Normal==Vector2.left))
                    .OrderBy(piece=>Vector2.Distance(piece.Footprint.center,room.Center)).First();
                focus=lamp.Footprint.center+lamp.Normal*.8f;cameraSize=1.8f;
            }
            else if(scenario==10)
            {
                var corner=game.Walls.Joins.Where(j=>j.Kind==WallJoinKind.OuterCorner &&
                    !new[]{new Vector2Int(-1,-1),new Vector2Int(-1,0),new Vector2Int(0,-1),Vector2Int.zero}.Any(d=>game.Map.Get(j.Vertex.x+d.x,j.Vertex.y+d.y)==Tile.Door) &&
                    j.IncidentRunIds.Any(id=>WallVisibility.BordersRoom(game.Walls,game.Walls.Runs[id],room)))
                    .OrderBy(j=>HotelView3D.Facing(j.Vertex).y).First();
                focus=corner.Vertex;cameraSize=2.2f;
            }
            else if(scenario==11)
            {
                game.SetWallMode(WallDisplayMode.Cutaway);
                var run=game.Walls.Runs.Where(r=>r.RenderFootprints.Count>0&&r.EdgeIds.All(id=>game.Map.Get(game.Walls.Edges[id].WalkableCell.x,game.Walls.Edges[id].WalkableCell.y)==Tile.Corridor))
                    .OrderByDescending(r=>r.Length).First();
                actor=HotelMap.Center(game.Walls.Edges[run.EdgeIds[run.EdgeIds.Count/2]].WalkableCell);
                focus=run.Center;cameraSize=2.8f;
            }
            else if (scenario >= 7)
            {
                // Close views of both axes make panel angles, crown seams and lamp joins inspectable.
                Vector2 normal = scenario == 7 ? Vector2.down : Vector2.left;
                var run = game.Walls.Runs.Where(candidate => candidate.Normal == normal && candidate.Length >= 5 &&
                    candidate.EdgeIds.All(id => game.Map.Get(game.Walls.Edges[id].WalkableCell.x,
                        game.Walls.Edges[id].WalkableCell.y) == Tile.Corridor)).OrderByDescending(candidate => candidate.Length).First();
                focus = run.Center;
                cameraSize = 2.5f;
            }
            if (!TileMovement.CanStand(actor, (x, y) => game.WalkableFor(human, x, y), GameManager.ResidentRadius, game.Walls))
                throw new InvalidOperationException("QA actor placement is blocked for " + Names[scenario] + " at " + actor);
            human.Pos = human.LastPos = actor;
            human.Facing = walking ? (walkEnd - walkStart).normalized : Vector2.down;
            typeof(GameManager).GetMethod("PlaceResidentSprite", Private).Invoke(game, new object[] { human });
            if (fog != game.FogActive) throw new InvalidOperationException("QA fog fixture does not match the requested scenario.");
            frameCount = 0; frameSeconds = 0; walked = 0;
            game.Announce("", 0);
            game.Toast("");
        }

        static List<Vector2Int> CorridorNeighbours(Vector2Int vertex, HotelMap map)
        {
            var cells = new List<Vector2Int>();
            for (int x = -1; x <= 0; x++) for (int y = -1; y <= 0; y++)
            {
                var cell = vertex + new Vector2Int(x, y);
                if (map.Get(cell.x, cell.y) == Tile.Corridor) cells.Add(cell);
            }
            return cells;
        }

        static bool IsHallwayCorner(Vector2Int vertex, HotelMap map)
        {
            for (int x = -1; x <= 0; x++) for (int y = -1; y <= 0; y++)
            {
                var tile = map.Get(vertex.x + x, vertex.y + y);
                if (tile == Tile.Door || tile == Tile.RoomFloor) return false;
            }
            return true;
        }

        static Vector2 RoomBoxCenter(RoomDef room) => new Vector2((room.Floor.Min(p => p.x) + room.Floor.Max(p => p.x) + 1) * .5f,
            (room.Floor.Min(p => p.y) + room.Floor.Max(p => p.y) + 1) * .5f);
        static int MissingRectangleArea(RoomDef room) => (room.Floor.Max(p => p.x) - room.Floor.Min(p => p.x) + 1) *
            (room.Floor.Max(p => p.y) - room.Floor.Min(p => p.y) + 1) - room.Floor.Count;
        static float RoomCameraSize(RoomDef room)
        {
            var points = room.Floor.Select(tile => HotelView3D.Facing(HotelMap.Center(tile))).ToList();
            float vertical = (points.Max(p => p.y) - points.Min(p => p.y)) * .5f + 1.65f;
            float horizontal = ((points.Max(p => p.x) - points.Min(p => p.x)) * .5f + 1) / (width / 720f);
            return Mathf.Max(3.8f, vertical, horizontal);
        }

        static void FrameCamera(Camera camera)
        {
            if (!cameraOverride || game == null || camera != game.Cam) return;
            camera.orthographicSize = cameraSize;
            camera.transform.SetPositionAndRotation(new Vector3(focus.x, focus.y, 0) - HotelView3D.Forward * 100, HotelView3D.Rotation);
            if (walking) game.Human.Anim?.Drive(HotelView3D.Facing(game.Human.Facing), true, false);
        }

        static void CountFrame(Camera camera)
        {
            if (game == null || camera != game.Cam || phase != 1) return;
            frameCount++; frameSeconds += Time.unscaledDeltaTime;
        }

        static void OnLog(string condition, string stack, LogType type)
        {
            if (report == null || (type != LogType.Exception && type != LogType.Error)) return;
            if (report.errors.Count < 30) report.errors.Add(condition + "\n" + stack);
        }

        static int PngInt(byte[] bytes, int offset) => (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

        static void Finish()
        {
            report.success = report.errors.Count == 0;
            WriteReport();
            Stop();
            Debug.Log("[WallCapture] Captured " + report.shots.Count + " scenarios at " + width + "x720 in " + output);
            EditorApplication.Exit(report.success ? 0 : 1);
        }

        static void Fail(string error)
        {
            if (report != null) { report.errors.Add(error); report.success = false; WriteReport(); }
            Stop(); Debug.LogError("[WallCapture] " + error); EditorApplication.Exit(1);
        }

        static void WriteReport()
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "walls-" + width + "-report.json"), JsonUtility.ToJson(report, true));
        }

        static void Stop()
        {
            cameraOverride = false;
            SessionState.SetBool(Session, false);
            EditorApplication.update -= Tick; Camera.onPreCull -= FrameCamera; Camera.onPostRender -= CountFrame;
            Application.logMessageReceived -= OnLog;
        }

        static void ConfigureGameView(int width, int height)
        {
            var assembly = typeof(Editor).Assembly;
            var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
            var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            var sizes = singleton.GetProperty("instance").GetValue(null);
            var groupType = assembly.GetType("UnityEditor.GameViewSizeGroupType");
            var group = sizesType.GetMethod("GetGroup").Invoke(sizes, new[] { Enum.Parse(groupType, "Standalone") });
            var sizeType = assembly.GetType("UnityEditor.GameViewSize");
            var kindType = assembly.GetType("UnityEditor.GameViewSizeType");
            var size = Activator.CreateInstance(sizeType, Enum.Parse(kindType, "FixedResolution"), width, height, "Wall QA " + width);
            group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { size });
            int index = (int)group.GetType().GetMethod("GetTotalCount").Invoke(group, null) - 1;
            var view = EditorWindow.GetWindow(assembly.GetType("UnityEditor.GameView"));
            view.GetType().GetProperty("selectedSizeIndex", Private | BindingFlags.Public).SetValue(view, index);
            view.maximized = true; view.Show(); view.Focus();
        }
    }
}
