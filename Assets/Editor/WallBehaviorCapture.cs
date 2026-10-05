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
    /// <summary>Editor-only, seeded gameplay fixtures; run without -quit in a disposable project.</summary>
    [InitializeOnLoad]
    public static class WallBehaviorCapture
    {
        const string Session = "BadApple.WallBehaviorCapture";
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly string[] Names = { "resident-near-monster", "resident-monster-away", "monster-local-window", "monster-away-room-hidden", "sleeping-monster-inside", "awake-shoot-ready" };
        [Serializable] sealed class Shot
        {
            public string scenario, file, role, primaryAction, shotAvailability;
            public int width, height, seed, room, apertureCells, visibleWallTriangles, wallSubmissions;
            public bool fog, monsterVisible, monsterSprite, residentSprite, probeRoomVisible, asleep, shaderWindow;
            public Vector2 residentPosition, monsterPosition;
            public Rect apertureBounds;
            public Vector4 shaderBounds;
            public float minimumRelatedWallHeight, minimumRelatedWallFade;
        }
        [Serializable] sealed class Report
        {
            public string generatedUtc, unityVersion, scope;
            public bool success;
            public List<Shot> shots = new List<Shot>();
            public List<string> errors = new List<string>();
            public List<string> editorStartupDiagnostics = new List<string>();
        }
        static GameManager game;
        static Resident resident;
        static RoomDef room;
        static WallPeek fixturePeek;
        static Vector2 near, away, inside, focus;
        static string output, pending;
        static Report report;
        static int scenario, phase, frames;
        static double started, deadline;
        static float cameraSize;

        static WallBehaviorCapture() { if (SessionState.GetBool(Session, false)) Hook(); }
        public static void Run()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Begin outside Play Mode.");
            output = Environment.GetEnvironmentVariable("BADAPPLE_BEHAVIOR_OUT") ?? "/tmp/badapple-wall-behavior-qa";
            Directory.CreateDirectory(output);
            SessionState.SetBool(Session, true); SessionState.SetString(Session + ".Output", output);
            typeof(WallCapture).GetMethod("ConfigureGameView", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { 1560, 720 });
            Sprites.UseArt = true; Hook(); EditorApplication.isPlaying = true;
        }
        static void Hook()
        {
            started = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Camera.onPreCull -= Frame; Camera.onPreCull += Frame;
            Camera.onPostRender -= CountFrame; Camera.onPostRender += CountFrame;
            Application.logMessageReceived -= Log; Application.logMessageReceived += Log;
        }
        static void Tick()
        {
            if (!SessionState.GetBool(Session, false)) return;
            try
            {
                if (report == null)
                {
                    output = SessionState.GetString(Session + ".Output", "/tmp/badapple-wall-behavior-qa");
                    report = new Report { generatedUtc = DateTime.UtcNow.ToString("o"), unityVersion = Application.unityVersion,
                        scope = "Real Unity Editor Game View, 1560x720, normal gameplay fog, fixed seeded match. AI and time are frozen by the editor fixture; production sight, wall shaders, HUD, sleep and shooting availability are exercised. Success requires all gameplay assertions and no gameplay errors. A known Unity SearchDatabase startup exception, if raised before the fixture exists, is retained separately in editorStartupDiagnostics. This is functional visual QA, not Android performance evidence." };
                }
                if (!EditorApplication.isPlaying || GameManager.Instance?.Cfg == null || GameManager.Instance.Map == null)
                {
                    if (EditorApplication.timeSinceStartup - started > 240) throw new InvalidOperationException("Initialization timeout.");
                    return;
                }
                if (game == null) { Initialize(); deadline = EditorApplication.timeSinceStartup + .5; return; }
                double now = EditorApplication.timeSinceStartup;
                if (phase == 0 && now >= deadline) { Prepare(); phase = 1; deadline = now + 1.5; }
                Refresh();
                if (phase == 1 && now >= deadline && frames >= 5)
                {
                    var shot = Inspect(); pending = shot.file;
                    if (File.Exists(pending)) File.Delete(pending);
                    ScreenCapture.CaptureScreenshot(pending); report.shots.Add(shot);
                    phase = 2; deadline = now + 30;
                }
                else if (phase == 2)
                {
                    if (File.Exists(pending) && new FileInfo(pending).Length > 1000)
                    {
                        var bytes = File.ReadAllBytes(pending);
                        Require(PngInt(bytes, 16) == 1560 && PngInt(bytes, 20) == 720, "Incorrect screenshot dimensions.");
                        scenario++;
                        if (scenario == Names.Length) Finish();
                        else { phase = 0; deadline = now + .25; }
                    }
                    else if (now > deadline) throw new InvalidOperationException("Screenshot timeout: " + pending);
                }
            }
            catch (Exception error) { Fail(error.ToString()); }
        }
        static void Initialize()
        {
            game = GameManager.Instance; Random.InitState(40102027);
            game.StartMatch(Role.Resident, "stitchwork_chef"); game.SetSpeed(0); game.enabled = false;
            foreach (var def in game.Map.Rooms) def.Isolated = false;
            var choices = new List<WallPeek>();
            for (int x = 0; x < game.Map.W; x++) for (int y = 0; y < game.Map.H; y++)
            {
                var point = new Vector2(x + .5f, y + .5f);
                var peek = WallSight.FindPeek(game.Map, point);
                if (peek == null || peek.Cells.Count != 3 || Vector2.Dot(peek.Normal, HotelView3D.Forward) <= 0) continue;
                var farther = point + (Vector2)peek.Normal;
                var cell = HotelMap.ToTile(farther);
                if (game.Map.Get(cell.x, cell.y) != Tile.Corridor || WallSight.FindPeek(game.Map, farther) != null) continue;
                choices.Add(peek);
            }
            fixturePeek = choices.OrderByDescending(p => p.Room.Floor.Count).ThenBy(p => p.Room.Index).FirstOrDefault();
            Require(fixturePeek != null, "No three-cell far-wall fixture with a clear step-away corridor was available.");
            room = fixturePeek.Room;
            near = HotelMap.Center(fixturePeek.CenterCell + fixturePeek.Normal);
            away = near + (Vector2)fixturePeek.Normal;
            inside = HotelMap.Center(fixturePeek.CenterCell - fixturePeek.Normal);
            Require(game.Claim(game.Human, room, true), "Cannot claim fixture room.");
            Invoke("BeginNights"); game.SetSpeed(0);
            resident = game.Human;
            game.Human.Room.DoorOpen = false; Invoke("RefreshDoor", game.Human.Room);
            focus = Vector2.Lerp(room.Center, HotelMap.Center(fixturePeek.CenterCell), .35f);
            var screen = room.Floor.Select(p => HotelView3D.Facing(HotelMap.Center(p))).ToList();
            cameraSize = Mathf.Max(3.8f, (screen.Max(p => p.y) - screen.Min(p => p.y)) * .5f + 1.9f);
            game.SetWallMode(WallDisplayMode.Cutaway); game.WallFocusRoom = null; game.HotelView = false;
            game.Announce("", 0); game.Toast("");
        }
        static void Prepare()
        {
            bool monsterRole = scenario == 2 || scenario == 3;
            typeof(GameManager).GetProperty("HumanRole").SetValue(game, monsterRole ? Role.Monster : Role.Resident);
            // The real monster-role reveal clears Human. Preserve the original guest as
            // a fixture target, but do not let the resident's self-visibility exemption
            // leak that target through fog when capturing from the monster's viewpoint.
            typeof(GameManager).GetProperty("Human").SetValue(game, monsterRole ? null : resident);
            resident.IsHuman = !monsterRole; resident.Name = monsterRole ? "Guest" : "You";
            game.Monster.IsHuman = monsterRole;
            resident.Asleep = resident.SleepRequested = false;
            resident.Pos = resident.LastPos = inside;
            game.Monster.Pos = scenario == 1 || scenario == 3 ? away : near;
            if (scenario >= 4)
            {
                resident.Pos = HotelMap.Center(room.BedTile);
                game.Monster.Pos = room.Floor.Where(cell => !room.IsBedTile(cell))
                    .Select(HotelMap.Center).Where(point => game.ClearLine(point, resident.Pos))
                    .OrderBy(point => Vector2.Distance(point, resident.Pos)).First();
                if (scenario == 4)
                {
                    Require(game.TrySleep(resident) == ActionResult.Ok && resident.Asleep, "Fixture resident did not fall asleep.");
                    resident.SleepBlend = 1;
                    Invoke("MonsterAttack", game.Monster, .01f, game.Now);
                    Require(game.Monster.Biting == resident && resident.Asleep, "A nearby monster attack woke the sleeping human.");
                }
                else game.Wake(resident);
            }
            resident.Facing = (game.Monster.Pos - resident.Pos).normalized;
            game.Monster.Facing = (resident.Pos - game.Monster.Pos).normalized;
            game.Announce("", 0); game.Toast(""); game.Floaters.Clear();
            frames = 0;
        }
        static void Refresh()
        {
            Invoke("PlaceResidentSprite", resident);
            HotelView3D.Billboard(game.Monster.Sr, game.Monster.Pos);
            game.Monster.Anim?.Drive(HotelView3D.Facing(game.Monster.Facing), false, false);
            ContactShadow.Place(game.Monster.Sr, game.Monster.Pos, true);
            typeof(GameManager).GetField("nextVisionUpdate", Private).SetValue(game, 0f);
            Invoke("UpdateVision"); Invoke("UpdateWallStateTexture", true);
        }
        static Shot Inspect()
        {
            var peek = game.ActiveWallPeek;
            var material = (Material)typeof(GameManager).GetField("hotelWallMaterial", Private).GetValue(game);
            bool enabled = material.GetFloat("_WallPeekEnabled") > .5f;
            var bounds = material.GetVector("_WallPeekBounds");
            var pixels = ((Texture2D)material.GetTexture("_WallStates")).GetPixels();
            float minHeight = WallGraph.FullHeight, minFade = 1;
            var instances = (WallInstances)typeof(GameManager).GetField("wallInstances", Private).GetValue(game);
            foreach (int state in instances.Records.Where(record => record.Footprint.Overlaps(fixturePeek.Bounds)).Select(record => record.StateId).Distinct())
            { minHeight = Mathf.Min(minHeight, pixels[state].g); minFade = Mathf.Min(minFade, pixels[state].a); }
            bool local = scenario == 0 || scenario == 2;
            Require(game.FogActive, "Behavior fixture must use normal fog, never clairvoyance.");
            Require((peek != null) == local && enabled == local, "Aperture/shader activation mismatch for " + Names[scenario]);
            if (local)
            {
                Require(peek.Cells.Count == 3 && peek.Room == room, "Aperture escaped its three-cell room wall.");
                Require(Vector4.Distance(bounds, new Vector4(peek.Bounds.xMin, peek.Bounds.yMin, peek.Bounds.xMax, peek.Bounds.yMax)) < .0001f, "Shader mask does not match sight aperture.");
                Require(minFade > .999f, "Peeking faded an entire wall run.");
                if (scenario == 2) Require(minHeight > WallGraph.FullHeight - .001f, "Monster peeking lowered the entire room wall.");
            }
            if (scenario == 0) Require(game.IsVisible(game.Monster.Pos) && game.Monster.Sr.enabled, "Nearby monster is hidden from the resident.");
            if (scenario == 1) Require(!game.IsVisible(game.Monster.Pos) && !game.Monster.Sr.enabled, "Remote monster is visible from the resident room.");
            bool probe = game.IsTileVisible(fixturePeek.CenterCell - fixturePeek.Normal);
            if (scenario == 2) Require(probe, "Monster cannot see the floor through its local window.");
            if (scenario == 3) Require(!probe && !resident.Sr.enabled, "Monster still sees room floor or the guest after leaving the wall.");
            if (scenario == 4) Require(resident.Asleep && game.ActionFor(resident) == ResidentAction.Wake && game.ResidentShotAvailability(resident) == ActionResult.Blocked, "Sleeping controls do not show Wake and block Shoot.");
            if (scenario == 5) Require(!resident.Asleep && game.ResidentShotAvailability(resident) == ActionResult.Ok, "Awake human cannot use Shoot against the nearby monster inside the room.");
            return new Shot { scenario = Names[scenario], file = Path.Combine(output, "behavior-1560-" + Names[scenario] + ".png"),
                width = Screen.width, height = Screen.height, seed = game.Map.Seed, room = room.Index,
                role = game.HumanRole.ToString(), fog = game.FogActive, monsterVisible = game.IsVisible(game.Monster.Pos), monsterSprite = game.Monster.Sr.enabled,
                residentSprite = resident.Sr.enabled, probeRoomVisible = probe, asleep = resident.Asleep, primaryAction = game.ActionFor(resident).ToString(),
                shotAvailability = game.ResidentShotAvailability(resident).ToString(), apertureCells = peek?.Cells.Count ?? 0,
                apertureBounds = peek?.Bounds ?? new Rect(), shaderWindow = enabled, shaderBounds = bounds,
                minimumRelatedWallHeight = minHeight, minimumRelatedWallFade = minFade,
                residentPosition = resident.Pos, monsterPosition = game.Monster.Pos,
                visibleWallTriangles = game.VisibleWallTriangles, wallSubmissions = game.ActualWallDrawCalls };
        }
        static void Frame(Camera camera)
        {
            if (game == null || camera != game.Cam) return;
            camera.orthographicSize = cameraSize;
            camera.transform.SetPositionAndRotation(new Vector3(focus.x, focus.y, 0) - HotelView3D.Forward * 100, HotelView3D.Rotation);
        }
        static void CountFrame(Camera camera) { if (game != null && camera == game.Cam && phase == 1) frames++; }
        static object Invoke(string method, params object[] args) => typeof(GameManager).GetMethod(method, Private).Invoke(game, args);
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static int PngInt(byte[] b, int i) => (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];
        static void Log(string message, string stack, LogType type)
        {
            if (report == null || (type != LogType.Error && type != LogType.Exception)) return;
            // Unity 6000.3's editor search index can throw while discovering its databases
            // before Play Mode. Preserve that external diagnostic without treating it as a
            // wall/gameplay failure; all errors after fixture creation remain fatal.
            if (game == null && stack.Contains("UnityEditor.Search.SearchDatabase") && stack.Contains("IndexationOnStartup"))
                report.editorStartupDiagnostics.Add(message + "\n" + stack);
            else if (report.errors.Count < 30) report.errors.Add(message + "\n" + stack);
        }
        static void Finish()
        {
            report.success = report.errors.Count == 0; Write(); Stop();
            Debug.Log("[WallBehaviorCapture] " + report.shots.Count + " gameplay scenarios captured in " + output);
            EditorApplication.Exit(report.success ? 0 : 1);
        }
        static void Fail(string message)
        { if (report != null) { report.errors.Add(message); Write(); } Stop(); Debug.LogError("[WallBehaviorCapture] " + message); EditorApplication.Exit(1); }
        static void Write() { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "behavior-1560-report.json"), JsonUtility.ToJson(report, true)); }
        static void Stop()
        {
            SessionState.SetBool(Session, false); EditorApplication.update -= Tick;
            Camera.onPreCull -= Frame; Camera.onPostRender -= CountFrame; Application.logMessageReceived -= Log;
        }
    }
}
