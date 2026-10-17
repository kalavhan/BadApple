using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BadAppleHotel.Game;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace BadAppleHotel.EditorTools
{
    /// <summary>
    /// Reproducible captures of the real IMGUI tray over a seeded hotel room. Run a disposable
    /// graphics-enabled editor with -executeMethod BadAppleHotel.EditorTools.HauntedTrayCapture.Run,
    /// without -quit. BADAPPLE_HAUNTED_RECORD_ONLY=1 reuses preceding screenshots and reruns the recording and input checks. BADAPPLE_HAUNTED_OUT sets the output folder; BADAPPLE_HAUNTED_RECORD=1 also
    /// records 10 seconds at 30 fps using an editor-only fixed presentation clock, as PNG frames and an ffmpeg concat file.
    /// This validates desktop rendering and gameplay states; it does not measure phone performance.
    /// </summary>
    [InitializeOnLoad]
    public static class HauntedTrayCapture
    {
        const string Session = "BadApple.HauntedTrayCapture";
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        const int Height = 720, RecordingFps = 30, RecordingFrameCount = 10 * RecordingFps;
        static readonly int[] Widths = { 1280, 1600, 960 };
        static readonly string[] Names = { "default", "unaffordable", "blocked", "wallet-45", "wallet-1240", "faith", "dial-setup", "dial-no-room", "dial-night", "dial-endless", "dial-results", "dial-monster" };

        [Serializable] sealed class Shot
        {
            public string scenario, file, selectedTower, resource, phase, role;
            public int width, height, seed, room, slot, night, renderedFrames, editorDrawCalls;
            public int recordingFrame = -1;
            public bool placeable, affordable, endless, sleeping, trayVisible;
            public float wallet, cost, glow;
            public Rect tray;
        }
        [Serializable] sealed class BeatFrame
        {
            public int frame;
            public float seconds, glow, simulationTime, primaryAge, primaryDuration, primaryLength;
            public string primaryState, secondaryState;
        }
        [Serializable] sealed class Release
        {
            public string source;
            public int frame;
            public float seconds, simulationTime;
            public bool forced;
        }
        [Serializable] sealed class Report
        {
            public string generatedUtc, unityVersion, scope, recordingConcat;
            public bool success;
            public float recordingSeconds;
            public int recordingFrames;
            public float minimumSummonReleaseGap, averagePrimaryBeat, glowMinimum, glowMaximum;
            public List<BeatFrame> beatFrames = new List<BeatFrame>();
            public List<Release> releases = new List<Release>();
            public List<string> beatAssertions = new List<string>();
            public List<Shot> shots = new List<Shot>();
            public List<string> interactionAssertions = new List<string>();
            public List<string> errors = new List<string>();
            public List<string> editorStartupDiagnostics = new List<string>();
        }

        static GameManager game;
        static GameHUD hud;
        static Room room;
        static Resident resident;
        static Ectoplasm goo;
        static Report report;
        static string output, pending;
        static int scenario, phase, slot, rendered, recordingIndex, lastRecordedFrame = -1;
        static double started, deadline;
        static readonly List<string> recordingFiles = new List<string>();
        static readonly List<double> recordingTimes = new List<double>();
        static bool record, recordOnly;
        static int clockFrame = -1;
        static Vector3 focus;
        static float walletBefore;
        static int testTab;
        static readonly string[] TestTypes = { "bullets", "fire", "electric", "effects", "resources" };
        static int Width => scenario == Widths.Length * Names.Length ? 1160 : Widths[Mathf.Min(scenario / Names.Length, Widths.Length - 1)];
        static string Scenario => scenario == Widths.Length * Names.Length ? "default" : Names[scenario % Names.Length];
        static bool HasTray => !Scenario.StartsWith("dial-", StringComparison.Ordinal);

        static HauntedTrayCapture() { if (SessionState.GetBool(Session, false)) Hook(); }

        public static void Run()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Begin outside Play Mode.");
            output = Environment.GetEnvironmentVariable("BADAPPLE_HAUNTED_OUT") ?? "/tmp/badapple-haunted-qa";
            Directory.CreateDirectory(output);
            SessionState.SetBool(Session, true);
            SessionState.SetString(Session + ".Output", output);
            recordOnly = Environment.GetEnvironmentVariable("BADAPPLE_HAUNTED_RECORD_ONLY") == "1";
            SessionState.SetBool(Session + ".Record", recordOnly || Environment.GetEnvironmentVariable("BADAPPLE_HAUNTED_RECORD") == "1");
            SessionState.SetBool(Session + ".RecordOnly", recordOnly);
            if (recordOnly && File.Exists(Path.Combine(output, "haunted-report.json")))
                File.Copy(Path.Combine(output, "haunted-report.json"), Path.Combine(output, "haunted-before-recording-report.json"), true);
            ConfigureView(recordOnly ? Widths[1] : Widths[0]);
            Sprites.UseArt = true;
            Hook();
            EditorApplication.isPlaying = true;
        }

        static void Hook()
        {
            started = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Camera.onPreCull -= CameraFrame; Camera.onPreCull += CameraFrame;
            Camera.onPostRender -= CountFrame; Camera.onPostRender += CountFrame;
            Application.logMessageReceived -= Log; Application.logMessageReceived += Log;
        }

        static void ConfigureView(int width) => typeof(WallCapture).GetMethod("ConfigureGameView", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { width, Height });

        static void Tick()
        {
            if (!SessionState.GetBool(Session, false)) return;
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if (report == null)
                {
                    output = SessionState.GetString(Session + ".Output", "/tmp/badapple-haunted-qa");
                    record = SessionState.GetBool(Session + ".Record", false);
                    recordOnly = SessionState.GetBool(Session + ".RecordOnly", false);
                    if (recordOnly) scenario = Names.Length;
                    report = new Report { generatedUtc = DateTime.UtcNow.ToString("o"), unityVersion = Application.unityVersion,
                        scope = "Real Unity Editor Game View at 1280x720, 1600x720 and 960x720, plus the compact default at 1160x720. Seeded room; AI/economy frozen, real unscaled presentation time, real affordability and path-blocking rules. Optional recording advances an editor-only presentation clock by 1/30 second per rendered frame; it is offline presentation evidence. Desktop visual/functional evidence, not Android phone performance." };
                    if (recordOnly && File.Exists(Path.Combine(output, "haunted-before-recording-report.json")))
                    {
                        var previous = JsonUtility.FromJson<Report>(File.ReadAllText(Path.Combine(output, "haunted-before-recording-report.json")));
                        report.shots = previous.shots;
                        report.scope += " Screenshot metadata retained from the preceding same-source fixture run; recording and interaction assertions rerun independently. Prior attempt report retained beside this report.";
                    }
                }
                if (!EditorApplication.isPlaying || GameManager.Instance?.Cfg == null || GameManager.Instance.Map == null)
                {
                    if (now - started > 240) throw new InvalidOperationException("Game initialization timed out.");
                    return;
                }
                if (game == null)
                {
                    Initialize();
                    if (recordOnly) { Prepare(); phase = 3; Directory.CreateDirectory(Path.Combine(output, "recording")); }
                    else phase = 0;
                    deadline = now + .75; return;
                }
                Refresh();
                if (phase >= 5) { InteractionTick(now); return; }
                if (phase == 0 && now >= deadline)
                {
                    if (Screen.width != Width || Screen.height != Height)
                    {
                        if (now - deadline > 30) throw new InvalidOperationException("Game View resolution did not update.");
                        return;
                    }
                    Prepare(); phase = 1; deadline = now + 1.5;
                }
                else if (phase == 1 && now >= deadline && rendered >= 5)
                {
                    var shot = Inspect(); pending = shot.file;
                    Capture(pending); report.shots.Add(shot);
                    Debug.Log("[HauntedTrayCapture] Screenshot " + shot.width + " " + shot.scenario + ": " + pending);
                    phase = 2; deadline = now + 30;
                }
                else if (phase == 2)
                {
                    if (Ready(pending))
                    {
                        ValidatePng(pending, Width);
                        scenario++;
                        if (scenario == Widths.Length * Names.Length + 1)
                        {
                            if (!record) { StartInteractions(now); return; }
                            scenario = Names.Length; ConfigureView(Width); Prepare();
                            phase = 3; deadline = now + 1;
                            Directory.CreateDirectory(Path.Combine(output, "recording"));
                        }
                        else
                        {
                            if (scenario % Names.Length == 0) ConfigureView(Width);
                            phase = 0; deadline = now + .5;
                        }
                    }
                    else if (now > deadline) throw new InvalidOperationException("Screenshot timed out: " + pending);
                }
                else if (phase == 3 && now >= deadline && Screen.width == Width)
                {
                    HauntedClock.BeginCapture(Time.unscaledTime);
                    clockFrame = -1;
                    report.beatFrames.Clear(); report.releases.Clear(); report.beatAssertions.Clear();
                    recordingIndex = 0; lastRecordedFrame = -1; recordingFiles.Clear(); recordingTimes.Clear();
                    phase = 4;
                    Debug.Log("[HauntedTrayCapture] Recording started: 300 frames at fixed 30 fps.");
                }
                else if (phase == 4 && recordingIndex >= RecordingFrameCount && Ready(recordingFiles[recordingFiles.Count - 1]))
                {
                    foreach (string file in recordingFiles) Require(Ready(file), "A recording frame was not saved: " + file);
                    ValidatePng(recordingFiles[0], Width);
                    SamplePreviousFrame();
                    ValidateBeat();
                    HauntedClock.EndCapture();
                    WriteRecording((double)RecordingFrameCount / RecordingFps); StartInteractions(now); return;
                }
            }
            catch (Exception error) { Fail(error.ToString()); }
        }

        static void Initialize()
        {
            game = GameManager.Instance;
            hud = game.GetComponent<GameHUD>();
            Require(hud != null, "The production GameHUD is missing.");
            Random.InitState(40112026);
            game.LegacyHotel = true; game.StartMatch(Role.Resident, "stitchwork_chef");
            game.SetSpeed(0); game.enabled = false;
            var def = game.Map.Rooms.OrderByDescending(candidate => (candidate.Isolated ? 0 : 1000) + candidate.BuildTiles.Count).First();
            def.Isolated = false;
            Require(game.Claim(game.Human, def, true), "Cannot claim capture room.");
            resident = game.Human;
            room = resident.Room;
            goo = (Ectoplasm)typeof(GameHUD).GetField("ectoplasm", Private).GetValue(hud);
            goo.Released += OnRelease;
            Invoke(game, "BeginNights"); game.SetSpeed(0);
            game.Human.Asleep = true; game.Human.SleepBlend = 1;
            game.Human.Pos = game.Human.LastPos = def.BedCenter;
            room.DoorOpen = false; Invoke(game, "RefreshDoor", room);
            game.SetWallMode(WallDisplayMode.Cutaway);
            game.HotelView = false; game.WallFocusRoom = null;
            game.Announce("", 0); game.Toast(""); game.Floaters.Clear();
        }

        static void ClearTowers()
        {
            for (int i = 0; i < room.Slots.Length; i++)
            {
                var tower = room.Slots[i];
                if (tower?.Sr != null) Object.Destroy(tower.Sr.gameObject);
                room.Slots[i] = null;
            }
        }

        static void Prepare()
        {
            ClearTowers();
            SetGame(nameof(GameManager.Human), resident); SetGame(nameof(GameManager.HumanRole), Role.Resident);
            resident.IsHuman = true; resident.Room = room; resident.Asleep = true; resident.SleepBlend = 1;
            resident.Pos = resident.LastPos = room.Def.BedCenter;
            if (game.Monster != null) game.Monster.IsHuman = false;
            resident.DreamPower = 218; resident.Faith = 120;
            bool endless = Scenario == "dial-endless";
            SetGame(nameof(GameManager.Endless), endless);
            SetGame(nameof(GameManager.Night), endless ? 9 : 3);
            SetGame(nameof(GameManager.Phase), Phase.Night);
            SetGame(nameof(GameManager.PhaseTimer), 50f);
            if (Scenario == "unaffordable") resident.DreamPower = 12;
            if (Scenario == "wallet-45") resident.DreamPower = 45;
            if (Scenario == "wallet-1240") resident.DreamPower = 1240;
            slot = Enumerable.Range(0, room.Slots.Length).Where(i => game.CanBuildAt(room, i))
                .OrderBy(i => Vector2.Distance(HotelMap.Center(room.Def.BuildTiles[i]), room.Def.Center)).First();
            if (Scenario == "blocked")
            {
                // Build only legal towers until one remaining empty plate would break the bed route.
                for (int placed = 0; placed < room.Slots.Length; placed++)
                {
                    int blocked = Enumerable.Range(0, room.Slots.Length).Where(i => room.Slots[i] == null && !game.CanBuildAt(room, i)).DefaultIfEmpty(-1).First();
                    if (blocked >= 0) { slot = blocked; break; }
                    int legal = Enumerable.Range(0, room.Slots.Length).Where(i => game.CanBuildAt(room, i))
                        .OrderBy(i => Vector2.Distance(HotelMap.Center(room.Def.BuildTiles[i]), HotelMap.Center(room.Def.BedTile))).DefaultIfEmpty(-1).First();
                    Require(legal >= 0, "Could not create a blocked empty plate.");
                    Invoke(game, "PlaceTower", room, legal, game.Cfg.towers.towers.First(t => t.id == "gun_turret"));
                }
                Require(room.Slots[slot] == null && !game.CanBuildAt(room, slot), "Blocked fixture is not an empty, path-blocking plate.");
            }
            SetHud("buildTab", Scenario == "faith" ? 3 : 0);
            SetHud("previewId", Scenario == "faith" ? "slow_totem" : "gun_turret");
            var selection = typeof(GameHUD).GetNestedType("Sel", BindingFlags.NonPublic);
            Invoke(hud, "ClearSelection");
            focus = (Vector3)HotelMap.Center(room.Def.BuildTiles[slot]) - HotelView3D.Rotation * Vector3.up * 1.5f;
            if (HasTray)
            {
                Invoke(hud, "OpenWindow", Enum.Parse(selection, "Slot"), slot);
                // Fix the visual seed as well as the hotel seed; OpenWindow ordinarily seeds from frame count.
                goo.Open(40112026);
            }
            else if (Scenario == "dial-setup" || Scenario == "dial-no-room")
            {
                SetGame(nameof(GameManager.Phase), Phase.Setup);
                SetGame(nameof(GameManager.PhaseTimer), 42f);
                if (Scenario == "dial-no-room")
                {
                    resident.Room = null; resident.Asleep = false; resident.SleepBlend = 0;
                    resident.Pos = resident.LastPos = HotelMap.Center(game.Map.Lobby);
                    focus = (Vector3)resident.Pos - HotelView3D.Rotation * Vector3.up * 1.5f;
                }
            }
            else if (Scenario == "dial-results")
            {
                SetGame(nameof(GameManager.Phase), Phase.Results);
                SetGame(nameof(GameManager.Result), new MatchResult { ResidentsWin = true, Role = Role.Resident,
                    XpGained = 120, LevelBefore = 1, LevelAfter = 2 });
            }
            else if (Scenario == "dial-monster")
            {
                SetGame(nameof(GameManager.HumanRole), Role.Monster); SetGame(nameof(GameManager.Human), null);
                resident.IsHuman = false; game.Monster.IsHuman = true;
            }
            game.Announce("", 0); game.Toast(""); game.Floaters.Clear();
            rendered = 0;
        }

        static void Refresh()
        {
            Invoke(game, "PlaceResidentSprite", resident);
            typeof(GameManager).GetField("nextVisionUpdate", Private).SetValue(game, 0f);
            Invoke(game, "UpdateVision"); Invoke(game, "UpdateFloorEnergy");
            Invoke(game, "UpdateTowerFx", game.Now, Time.unscaledDeltaTime);
        }

        static Shot Inspect()
        {
            string id = (string)typeof(GameHUD).GetField("previewId", Private).GetValue(hud);
            var tower = game.Cfg.towers.towers.First(t => t.id == id);
            bool placeable = game.CanBuildAt(room, slot), affordable = game.Wallet(resident, tower.costResource) >= tower.buildCost;
            var tray = HasTray ? (Rect)Invoke(hud, "TrayRect") : Rect.zero;
            float glow = (float)typeof(GameHUD).GetField("summonGlow", Private).GetValue(hud);
            if (HasTray)
            {
                Require(placeable == (Scenario != "blocked"), "Placeability mismatch: " + Scenario);
                Require(affordable == (Scenario != "unaffordable"), "Affordability mismatch: " + Scenario);
                Require(Scenario != "faith" || tower.costResource == "faith", "Faith fixture must spend Faith.");
                Require(Mathf.Abs(tray.height - 360) < .01f, "Tray must be 360 canvas px tall.");
                if (!placeable || !affordable)
                {
                    Require(glow < .001f, "Disabled Summon still glows: " + Scenario);
                    foreach (string anchor in new[] { Ectoplasm.Summon1, Ectoplasm.Summon2 })
                        Require(goo.TryGetDrip(anchor, out var drip) && !drip.Enabled, "Disabled Summon still drips: " + anchor);
                }
            }
            return new Shot { scenario = Scenario, file = Path.Combine(output, "haunted-" + Width + "-" + Scenario + ".png"),
                width = Screen.width, height = Screen.height, seed = game.Map.Seed, room = resident.Room?.Def.Index ?? -1, slot = slot,
                selectedTower = HasTray ? id : "", resource = tower.costResource, wallet = game.Wallet(resident, tower.costResource), cost = tower.buildCost,
                placeable = placeable, affordable = affordable, endless = game.Endless, night = game.Night, sleeping = resident.Asleep,
                phase = game.Phase.ToString(), role = game.HumanRole.ToString(), trayVisible = HasTray, glow = glow,
                renderedFrames = rendered, editorDrawCalls = UnityStats.drawCalls, tray = tray };
        }

        static void StartInteractions(double now)
        {
            scenario = Names.Length;
            Prepare(); phase = 5; deadline = now + .5;
        }

        static void InteractionTick(double now)
        {
            if (now < deadline) return;
            var tower = game.Cfg.towers.towers.First(t => t.id == "gun_turret");
            if (phase == 5)
            {
                walletBefore = game.Human.DreamPower;
                Invoke(hud, "BeginSummon", game.Human, room, slot, tower);
                Invoke(hud, "BeginSummon", game.Human, room, slot, tower);
                Invoke(hud, "UpdateSummonPress");
                Require(room.Slots[slot] == null && game.Human.DreamPower == walletBefore,
                    "Summon placed/spent before its press animation completed.");
                report.interactionAssertions.Add("Press does not place or charge immediately; repeated press queues only one summon.");
                phase = 6; deadline = now + .5;
            }
            else if (phase == 6)
            {
                Require(room.Slots[slot]?.Def == tower, "Delayed Summon did not place the selected tower.");
                Require(Mathf.Abs(game.Human.DreamPower - (walletBefore - tower.buildCost)) < .001f,
                    "Delayed Summon did not charge exactly once.");
                report.interactionAssertions.Add("After the 0.22-second press, one selected tower is placed and charged once.");
                Prepare(); phase = 7; deadline = now + .5;
            }
            else if (phase == 7)
            {
                Invoke(hud, "BeginSummon", game.Human, room, slot, tower);
                game.Human.DreamPower = 0;
                phase = 8; deadline = now + .5;
            }
            else if (phase == 8)
            {
                Require(room.Slots[slot] == null && game.Human.DreamPower == 0,
                    "A delayed Summon must revalidate funds before placing.");
                report.interactionAssertions.Add("Losing affordability during the press cancels placement without charging.");
                Prepare(); phase = 9; deadline = now + .5;
            }
            else if (phase == 9)
            {
                walletBefore = game.Human.DreamPower;
                Invoke(hud, "BeginSummon", game.Human, room, slot, tower);
                Invoke(hud, "CloseWindow");
                phase = 10; deadline = now + .5;
            }
            else if (phase == 10)
            {
                Require(room.Slots[slot] == null && game.Human.DreamPower == walletBefore,
                    "Closing during a queued summon must prevent placement and spending.");
                report.interactionAssertions.Add("Closing during the press cancels the pending summon and retains the wallet.");
                Prepare(); testTab = 0; phase = 11; deadline = now + .5;
            }
            else if (phase == 11)
            {
                var layout = new HauntedTrayLayout((Rect)Invoke(hud, "TrayRect"), 3);
                var button = new Vector2(layout.Rail.center.x, layout.Rail.y + 43 + testTab * 42);
                ((Action)ZoneAction(button, "Tap")).Invoke();
                phase = 12; deadline = now + .3;
            }
            else if (phase == 12)
            {
                string id = (string)typeof(GameHUD).GetField("previewId", Private).GetValue(hud);
                Require(game.Cfg.towers.towers.Any(t => t.id == id && t.category == TestTypes[testTab]),
                    "Type tab did not select a valid tower: " + TestTypes[testTab]);
                report.interactionAssertions.Add("Actual type-rail Tap callback selects a valid " + TestTypes[testTab] + " tower.");
                testTab++;
                if (testTab < TestTypes.Length) { phase = 11; deadline = now + .1; }
                else { phase = 13; deadline = now + .3; }
            }
            else
            {
                int count = game.Cfg.towers.towers.Count(t => t.category == "resources");
                var layout = new HauntedTrayLayout((Rect)Invoke(hud, "TrayRect"), count);
                var center = layout.Cubby(0).center;
                int destination = Enumerable.Range(0, room.Slots.Length).First(i => i != slot && game.CanBuildAt(room, i));
                var point = (Vector2)Invoke(hud, "WorldToGui", HotelMap.Center(room.Def.BuildTiles[destination]));
                ((Action<Vector2>)ZoneAction(center, "Drag")).Invoke(point);
                Require((int)typeof(GameHUD).GetField("selSlot", Private).GetValue(hud) == destination,
                    "Dragging a card did not move the selection to the destination plate.");
                ((Action<Vector2, bool>)ZoneAction(center, "Release")).Invoke(point, true);
                Require(!(bool)typeof(GameHUD).GetField("cardDragging", Private).GetValue(hud), "Drag release left a floating card active.");
                report.interactionAssertions.Add("Actual cubby Drag/Release callbacks move the preview onto another free plate and release the floating card.");
                Finish();
            }
        }

        static Delegate ZoneAction(Vector2 point, string callback)
        {
            var zones = (IEnumerable)typeof(GameHUD).GetField("zones", Private).GetValue(hud);
            foreach (var zone in zones)
            {
                var type = zone.GetType();
                var bounds = (Rect)type.GetField("R").GetValue(zone);
                var action = type.GetField(callback).GetValue(zone) as Delegate;
                if (bounds.Contains(point) && action != null) return action;
            }
            throw new InvalidOperationException("No active " + callback + " zone at " + point);
        }

        static void CameraFrame(Camera camera)
        {
            if (game == null || camera != game.Cam) return;
            if (phase == 4 && clockFrame != Time.frameCount)
            {
                // OnGUI finishes after the preceding camera render; sample that completed image
                // before advancing the next frame. Freeze once the last PNG is queued.
                SamplePreviousFrame();
                HauntedClock.AdvanceCapture(recordingIndex < RecordingFrameCount ? 1f / RecordingFps : 0f);
                clockFrame = Time.frameCount;
            }
            camera.orthographicSize = 4.7f;
            camera.transform.SetPositionAndRotation(focus - HotelView3D.Forward * 100, HotelView3D.Rotation);
        }
        static void CountFrame(Camera camera)
        {
            if (game == null || camera != game.Cam) return;
            rendered++;
            if (phase != 4 || recordingIndex >= RecordingFrameCount || lastRecordedFrame == Time.frameCount) return;
            lastRecordedFrame = Time.frameCount;
            try
            {
                if (recordingIndex == 5)
                    Require(Mathf.Abs(HauntedClock.Delta - 1f / RecordingFps) < .001f,
                        "Haunted presentation clock did not provide the expected fixed timestep.");
                string file = Path.Combine(output, "recording", "frame-" + recordingIndex.ToString("0000") + ".png");
                Capture(file); recordingFiles.Add(file); recordingTimes.Add((double)recordingIndex / RecordingFps);
                recordingIndex++;
                if (recordingIndex % RecordingFps == 0) Debug.Log("[HauntedTrayCapture] Recorded " + recordingIndex + "/" + RecordingFrameCount + " frames.");
            }
            catch (Exception error) { Fail(error.ToString()); }
        }
        static void OnRelease(string source, float simulationTime, bool forced)
        {
            if (phase != 4) return;
            int frame = Mathf.Max(0, recordingIndex - 1);
            report.releases.Add(new Release { source = source, frame = frame, seconds = (float)frame / RecordingFps,
                simulationTime = simulationTime, forced = forced });
        }
        static void SamplePreviousFrame()
        {
            if (recordingIndex == 0 || report.beatFrames.Count >= recordingIndex) return;
            Require(goo.TryGetDrip(Ectoplasm.Summon1, out var primary), "Primary Summon drip missing in beat recording.");
            Require(goo.TryGetDrip(Ectoplasm.Summon2, out var secondary), "Secondary Summon drip missing in beat recording.");
            int frame = recordingIndex - 1;
            report.beatFrames.Add(new BeatFrame { frame = frame, seconds = (float)frame / RecordingFps,
                glow = (float)typeof(GameHUD).GetField("summonGlow", Private).GetValue(hud), simulationTime = goo.SimulationTime,
                primaryState = primary.State.ToString(), secondaryState = secondary.State.ToString(),
                primaryAge = primary.Age, primaryDuration = primary.Duration, primaryLength = primary.Length });
        }
        static void ValidateBeat()
        {
            Require(report.beatFrames.Count == RecordingFrameCount, "Each recorded frame needs matching post-render telemetry.");
            var settled = report.beatFrames.Where(f => f.frame >= RecordingFps).ToArray();
            var rest = settled.OrderBy(f => f.glow).First();
            var peak = settled.OrderByDescending(f => f.glow).First();
            report.glowMinimum = rest.glow; report.glowMaximum = peak.glow;
            Require(peak.glow - rest.glow > .35f, "The live Summon glow did not visibly pulse.");
            report.beatAssertions.Add("Rendered Summon glow rises by more than 0.35 between rest and peak.");
            var releases = report.releases.Where(r => r.source == Ectoplasm.Summon1 || r.source == Ectoplasm.Summon2).ToArray();
            Require(releases.Length >= 4, "Ten seconds must contain at least four alternating Summon releases.");
            report.minimumSummonReleaseGap = float.MaxValue;
            for (int i = 1; i < releases.Length; i++)
                report.minimumSummonReleaseGap = Mathf.Min(report.minimumSummonReleaseGap, releases[i].simulationTime - releases[i-1].simulationTime);
            Require(report.minimumSummonReleaseGap >= .399f, "Actual rendered Summon releases violate the 0.4-second gap.");
            report.beatAssertions.Add("Live Summon releases retain at least a 0.4-second gap.");
            var primary = releases.Where(r => r.source == Ectoplasm.Summon1).ToArray();
            Require(primary.Length >= 2, "The primary drip must complete multiple beats.");
            report.averagePrimaryBeat = (primary[primary.Length - 1].simulationTime - primary[0].simulationTime) / (primary.Length - 1);
            for (int i = 1; i < primary.Length; i++)
            {
                float beat = primary[i].simulationTime - primary[i-1].simulationTime;
                Require(beat >= 2.499f && beat <= 3.001f, "Actual rendered Summon beat is outside 2.5–3.0 seconds: " + beat);
            }
            report.beatAssertions.Add("Every complete primary Summon beat in the recording lasts 2.5–3.0 seconds.");
            foreach (var release in primary.Where(r => r.frame < RecordingFrameCount - 8))
                Require(report.beatFrames.Where(f => f.frame >= release.frame && f.frame <= release.frame + 8).Max(f => f.glow) > .9f,
                    "Primary release has no synchronized glow peak near frame " + release.frame);
            report.beatAssertions.Add("Each primary release has a synchronized glow peak above 0.9 within eight recorded frames.");
            foreach (var pair in new[] { new { name = "glow-rest", frame = rest }, new { name = "glow-peak", frame = peak } })
            {
                var shot = Inspect(); shot.scenario = pair.name; shot.glow = pair.frame.glow; shot.recordingFrame = pair.frame.frame;
                shot.file = Path.Combine(output, "haunted-" + Width + "-" + pair.name + ".png");
                File.Copy(recordingFiles[pair.frame.frame], shot.file, true); report.shots.Add(shot);
            }
        }
        static void SetGame(string name, object value) => typeof(GameManager).GetProperty(name).SetValue(game, value);
        static void SetHud(string name, object value) => typeof(GameHUD).GetField(name, Private).SetValue(hud, value);
        static object Invoke(object target, string name, params object[] args)
        {
            var types = args.Select(arg => arg.GetType()).ToArray();
            var method = target.GetType().GetMethod(name, Private, null, types, null) ?? target.GetType().GetMethod(name, Private);
            return method.Invoke(target, args);
        }
        static void Capture(string path) { if (File.Exists(path)) File.Delete(path); ScreenCapture.CaptureScreenshot(path); }
        static bool Ready(string path) => File.Exists(path) && new FileInfo(path).Length > 1000;
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void ValidatePng(string path, int width)
        {
            var bytes = File.ReadAllBytes(path);
            int Number(int i) => (bytes[i] << 24) | (bytes[i + 1] << 16) | (bytes[i + 2] << 8) | bytes[i + 3];
            Require(Number(16) == width && Number(20) == Height, "Incorrect screenshot dimensions: " + path);
        }
        static void WriteRecording(double duration)
        {
            var text = new StringBuilder("ffconcat version 1.0\n");
            for (int i = 0; i < recordingFiles.Count; i++)
            {
                text.Append("file '").Append(recordingFiles[i].Replace("'", "'\\''")).Append("'\n");
                double end = i + 1 < recordingTimes.Count ? recordingTimes[i + 1] : duration;
                text.Append("duration ").Append((end - recordingTimes[i]).ToString("0.000000", CultureInfo.InvariantCulture)).Append('\n');
            }
            text.Append("file '").Append(recordingFiles[recordingFiles.Count - 1].Replace("'", "'\\''")).Append("'\n");
            report.recordingConcat = Path.Combine(output, "haunted-recording.ffconcat");
            report.recordingSeconds = (float)duration; report.recordingFrames = recordingFiles.Count;
            File.WriteAllText(report.recordingConcat, text.ToString());
        }
        static void Log(string message, string stack, LogType type)
        {
            if (report == null || (type != LogType.Error && type != LogType.Exception)) return;
            if (game == null && stack.Contains("UnityEditor.Search.SearchDatabase") && stack.Contains("IndexationOnStartup"))
                report.editorStartupDiagnostics.Add(message + "\n" + stack);
            else if (report.errors.Count < 30) report.errors.Add(message + "\n" + stack);
        }
        static void Finish()
        {
            report.success = report.errors.Count == 0; Write(); Stop();
            Debug.Log("[HauntedTrayCapture] Captured " + report.shots.Count + " scenarios and " + report.recordingFrames + " recording frames in " + output);
            EditorApplication.Exit(report.success ? 0 : 1);
        }
        static void Fail(string message)
        {
            if (report != null) { report.errors.Add(message); Write(); }
            Stop(); Debug.LogError("[HauntedTrayCapture] " + message); EditorApplication.Exit(1);
        }
        static void Write() => File.WriteAllText(Path.Combine(output, "haunted-report.json"), JsonUtility.ToJson(report, true));
        static void Stop()
        {
            HauntedClock.EndCapture();
            if (goo != null) goo.Released -= OnRelease;
            SessionState.SetBool(Session, false); EditorApplication.update -= Tick;
            Camera.onPreCull -= CameraFrame; Camera.onPostRender -= CountFrame; Application.logMessageReceived -= Log;
        }
    }
}
