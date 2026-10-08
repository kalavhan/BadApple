using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BadAppleHotel.Config;
using BadAppleHotel.Game;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BadAppleHotel.EditorTools
{
    /// <summary>Deterministic door/frame inspection; run with a graphics device and -quit
    /// -executeMethod BadAppleHotel.EditorTools.DoorCapture.Run. BADAPPLE_DOOR_QA_OUT
    /// selects the output folder. Only actually imported designs are captured.</summary>
    public static class DoorCapture
    {
        public static void BakeAndCapture()
        {
            DoorKitImporter.Bake();
            Run();
            RunGame();
        }
        const int Layer = 31, Pixels = 720;
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        [Serializable] sealed class Shot
        {
            public int level, triangles;
            public string state, view, file;
            public bool powered, broken;
            public float angle, clock, frameHeight;
        }
        [Serializable] sealed class Report
        {
            public string unityVersion, graphicsDevice, scope;
            public int[] missingArtLevels;
            public List<Shot> shots = new List<Shot>();
        }
        [Serializable] sealed class GameShot
        {
            public string state, file, wallMode, matState;
            public int mapSeed, roomIndex, doorLevel, visibleWallTriangles, wallDrawCalls;
            public bool fog, doorBlocks, meshVisible;
            public Vector2 actorPosition, doorway;
        }
        [Serializable] sealed class GameReport
        {
            public string unityVersion, graphicsDevice;
            public List<GameShot> shots = new List<GameShot>();
        }

        [MenuItem("Bad Apple/Capture imported doors")]
        public static void Run()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("DoorCapture starts outside Play Mode.");
            var kit = DoorKit.Load();
            if (kit == null || kit.Pieces.Length == 0) throw new InvalidOperationException("Bake at least one door before capture.");
            var shader = Resources.Load<Shader>("Shaders/HotelDoor");
            if (shader == null) throw new InvalidOperationException("HotelDoor shader is missing.");
            string output = Environment.GetEnvironmentVariable("BADAPPLE_DOOR_QA_OUT");
            if (string.IsNullOrWhiteSpace(output)) output = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Builds", "DoorQA");
            Directory.CreateDirectory(output);
            var report = new Report
            {
                unityVersion = Application.unityVersion, graphicsDevice = SystemInfo.graphicsDeviceName,
                scope = "Isolated inspection using the actual baked door mesh, runtime DoorModel, shared hotel door-frame mesh, and production shaders. Fixed animation clock; neutral lighting. These are not gameplay or Android performance captures.",
                missingArtLevels = Enumerable.Range(1, DoorKit.LastArtLevel).Where(level => kit.Get(level) == null).ToArray()
            };
            var objects = new List<Object>();
            GameObject root = null;
            float oldFog = Shader.GetGlobalFloat("_HotelFog"), oldLighting = Shader.GetGlobalFloat("_HotelLightingEnabled");
            var oldSize = Shader.GetGlobalVector("_HotelSize");
            var oldVision = Shader.GetGlobalTexture("_HotelVision");
            var oldActive = RenderTexture.active;
            bool? oldArt = Sprites.ArtOverride;
            try
            {
                Shader.SetGlobalFloat("_HotelFog", 0); Shader.SetGlobalFloat("_HotelLightingEnabled", 0);
                Shader.SetGlobalVector("_HotelSize", new Vector4(4, 4, 0, 0));
                Shader.SetGlobalTexture("_HotelVision", Texture2D.whiteTexture);
                Sprites.ArtOverride = false;
                root = new GameObject("Door capture fixture") { hideFlags = HideFlags.HideAndDontSave };
                var camera = Child(root.transform, "Capture camera").AddComponent<Camera>();
                camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 1.05f;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f, .08f, .10f);
                camera.cullingMask = 1 << Layer; camera.nearClipPlane = .05f; camera.farClipPlane = 20;
                camera.allowHDR = false; camera.allowMSAA = false;
                var target = new RenderTexture(Pixels, Pixels, 24, RenderTextureFormat.ARGB32);
                objects.Add(target); target.Create(); camera.targetTexture = target;
                var image = new Texture2D(Pixels, Pixels, TextureFormat.RGB24, false);
                objects.Add(image);
                AddFloor(root.transform, objects);
                var wallState = AddFrame(root.transform, objects);
                var rubble = Child(root.transform, "Broken door rubble").AddComponent<SpriteRenderer>();
                rubble.sprite = Sprites.DoorBroken;
                var rubbleMaterial = new Material(Resources.Load<Shader>("Shaders/CharacterSprite"));
                objects.Add(rubbleMaterial); rubble.sharedMaterial = rubbleMaterial;
                rubble.transform.SetPositionAndRotation(new Vector3(.5f, .5f, -.12f), Quaternion.LookRotation(Vector3.up, Vector3.back));
                rubble.transform.localScale = new Vector3(.94f / rubble.sprite.bounds.size.x, .22f / rubble.sprite.bounds.size.y, 1);
                rubble.enabled = false;
                foreach (var piece in kit.Pieces.OrderBy(item => item.Level))
                {
                    var leaf = Child(root.transform, "Door " + piece.Level).AddComponent<DoorModel>();
                    leaf.Initialize(new RoomDef { DoorTile = Vector2Int.zero, DoorInside = Vector2Int.up, DoorOutside = Vector2Int.down });
                    var material = new Material(shader) { mainTexture = piece.Albedo };
                    objects.Add(material); material.SetFloat("_Tier", piece.Level);
                    leaf.SetState(piece, material, false, false, true, 1, 10);
                    var scenarios = new List<string> { "closed", "open", "impact", "broken", "rebuilt", "unclaimed", "cutaway" };
                    if (piece.Level >= 4) scenarios.AddRange(new[] { "unpowered", "idle-later" });
                    foreach (string state in scenarios)
                    {
                        bool broken = state == "broken", powered = state != "unclaimed" && state != "unpowered";
                        bool open = state == "open" || state == "unclaimed";
                        float clock = state == "idle-later" ? 14.5f : 12;
                        float height = state == "cutaway" ? WallGraph.DownHeight : WallGraph.FullHeight;
                        // Give each scenario a fresh healthy baseline so impact/rebuild timing is reproducible.
                        leaf.SetState(piece, material, false, false, powered, 1, 10);
                        if (state == "rebuilt") leaf.SetState(piece, material, false, true, powered, 0, 11);
                        leaf.SetState(piece, material, open, broken, powered, broken ? 0 : state == "impact" ? .65f : 1, 12);
                        if (state == "impact") clock += .06f;
                        if (state == "rebuilt") clock += .12f;
                        leaf.Present(1, clock, true);
                        rubble.enabled = broken && piece.BrokenMesh == null;
                        SetFrameHeight(wallState, state == "cutaway" ? WallGraph.DownHeight : WallGraph.FullHeight);
                        bool compareFront = state == "closed" || (piece.Level >= 4 &&
                            (state == "unpowered" || state == "idle-later" || state == "broken"));
                        foreach (string view in compareFront ? new[] { "front", "iso" } : new[] { "iso" })
                        {
                            var center = new Vector3(.5f, .65f, -.65f);
                            Quaternion rotation = view == "front" ? Quaternion.LookRotation(Vector3.up, Vector3.back) : HotelView3D.Rotation;
                            camera.transform.SetPositionAndRotation(center - rotation * Vector3.forward * 8, rotation);
                            camera.Render();
                            RenderTexture.active = target;
                            image.ReadPixels(new Rect(0, 0, Pixels, Pixels), 0, 0); image.Apply();
                            string file = Path.Combine(output, "door-" + piece.Level + "-" + state + "-" + view + ".png");
                            File.WriteAllBytes(file, image.EncodeToPNG());
                            report.shots.Add(new Shot { level = piece.Level, triangles = piece.Triangles, state = state,
                                view = view, file = file, powered = powered && !broken, broken = broken,
                                angle = leaf.OpenAngle, clock = clock, frameHeight = height });
                        }
                    }
                    // Inspect the closed panel against the same frame from every map orientation.
                    // Rotate the assembly, then restore the fixed isometric camera in world space.
                    var pivot = new Vector3(.5f, .5f, 0);
                    foreach (int facing in new[] { 0, 90, 180, 270 })
                    {
                        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                        leaf.SetState(piece, material, false, false, true, 1, 20);
                        leaf.Present(1, 22, true); rubble.enabled = false;
                        SetFrameHeight(wallState, WallGraph.FullHeight);
                        var turn = Quaternion.Euler(0, 0, facing);
                        root.transform.SetPositionAndRotation(pivot - turn * pivot, turn);
                        camera.transform.SetPositionAndRotation(pivot + Vector3.back * .65f - HotelView3D.Forward * 8, HotelView3D.Rotation);
                        camera.Render(); RenderTexture.active = target;
                        image.ReadPixels(new Rect(0, 0, Pixels, Pixels), 0, 0); image.Apply();
                        string file = Path.Combine(output, "door-" + piece.Level + "-alignment-" + facing + ".png");
                        File.WriteAllBytes(file, image.EncodeToPNG());
                        report.shots.Add(new Shot { level = piece.Level, triangles = piece.Triangles, state = "alignment",
                            view = "iso-" + facing, file = file, powered = true, angle = 0, clock = 22, frameHeight = WallGraph.FullHeight });
                    }
                    root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    Object.DestroyImmediate(leaf.gameObject);
                }
                File.WriteAllText(Path.Combine(output, "doors-report.json"), JsonUtility.ToJson(report, true));
                Debug.Log("[DoorCapture] Wrote " + report.shots.Count + " images to " + output);
            }
            finally
            {
                RenderTexture.active = oldActive;
                if (root != null) Object.DestroyImmediate(root);
                foreach (var item in objects) if (item != null) Object.DestroyImmediate(item);
                Shader.SetGlobalFloat("_HotelFog", oldFog); Shader.SetGlobalFloat("_HotelLightingEnabled", oldLighting);
                Shader.SetGlobalVector("_HotelSize", oldSize); Shader.SetGlobalTexture("_HotelVision", oldVision);
                Sprites.ArtOverride = oldArt;
            }
        }

        /// <summary>Actual hotel fixture for a disposable batch editor. Includes the production
        /// resident, bed, floor, lighting, door lifecycle, and wall cutaway integration.</summary>
        public static void RunGame()
        {
            if (!Application.isBatchMode || EditorApplication.isPlaying)
                throw new InvalidOperationException("RunGame requires a disposable batch editor outside Play Mode.");
            string output = Environment.GetEnvironmentVariable("BADAPPLE_DOOR_QA_OUT");
            if (string.IsNullOrWhiteSpace(output)) output = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Builds", "DoorQA");
            Directory.CreateDirectory(output);
            var report = new GameReport { unityVersion = Application.unityVersion, graphicsDevice = SystemInfo.graphicsDeviceName };
            GameManager game = null;
            Camera camera = null;
            RenderTexture target = null;
            Texture2D image = null;
            var oldActive = RenderTexture.active;
            var oldRandom = UnityEngine.Random.state;
            bool? oldArt = Sprites.ArtOverride;
            float oldScale = Time.timeScale;
            try
            {
                game = new GameObject("Door gameplay capture").AddComponent<GameManager>();
                game.StartSimulation(ConfigLoader.Load(), 241, false);
                camera = new GameObject("Door gameplay camera").AddComponent<Camera>();
                camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 2.6f;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.035f, .03f, .045f);
                camera.nearClipPlane = .1f; camera.farClipPlane = 200; camera.allowHDR = false; camera.allowMSAA = false;
                typeof(GameManager).GetProperty(nameof(GameManager.Cam)).SetValue(game, camera);
                typeof(GameManager).GetProperty(nameof(GameManager.Simulation)).SetValue(game, false);
                Sprites.ArtOverride = true;
                UnityEngine.Random.InitState(241);
                game.StartMatch(Role.Resident, "stitchwork_chef");
                game.SetSpeed(0);
                var def = game.Map.Rooms.FirstOrDefault(room => room.DoorInside.y > room.DoorTile.y) ?? game.Map.Rooms[0];
                def.Isolated = false;
                if (!game.Claim(game.Human, def, true)) throw new InvalidOperationException("Cannot claim the door capture room.");
                var room = game.Human.Room;
                // Clairvoyance exposes the surrounding geometry for inspection using its ordinary gameplay rule.
                int slot = room.EmptySlot();
                if (slot >= 0) room.Slots[slot] = new TowerInstance { Def = game.Cfg.towers.towers.First(item => item.effect == "clairvoyance"),
                    Tile = def.BuildTiles[slot], SlotIndex = slot };
                game.Human.Pos = game.Human.LastPos = HotelMap.Center(def.DoorInside);
                game.Human.Facing = Vector2.down;
                Invoke(game, "PlaceResidentSprite", game.Human);
                Invoke(game, "UpdateVision");
                Invoke(game, "UpdateFloorEnergy");
                var models = (Dictionary<RoomDef, DoorModel>)typeof(GameManager).GetField("doorModels", Private).GetValue(game);
                var model = models[def];
                var focus = (Vector3)HotelMap.Center(def.DoorTile) + new Vector3(0, .4f, -.45f);
                camera.transform.SetPositionAndRotation(focus - HotelView3D.Forward * 100, HotelView3D.Rotation);
                target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
                target.Create(); camera.targetTexture = target;
                image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                var kit = DoorKit.Load();
                if (kit == null || kit.Pieces.Length == 0) throw new InvalidOperationException("Bake at least one door before capture.");
                foreach (var piece in kit.Pieces.OrderBy(item => item.Level))
                foreach (string state in piece.Level == 1
                    ? new[] { "closed", "open", "broken", "rebuilt", "cutaway", "walls-down",
                        "mat-available", "mat-occupied", "mat-sleeping", "mat-awake", "mat-dead", "mat-empty" }
                    : new[] { "closed", "open", "broken", "rebuilt", "cutaway", "walls-down" })
                {
                    bool empty = state == "mat-available" || state == "mat-empty";
                    bool night = state == "mat-sleeping" || state == "mat-awake" || state == "mat-dead" || state == "mat-empty";
                    typeof(GameManager).GetProperty(nameof(GameManager.Phase)).SetValue(game, night ? Phase.Night : Phase.Setup);
                    room.Owner.Alive = state != "mat-dead"; room.Owner.Asleep = state == "mat-sleeping";
                    if (empty) game.RoomsByDef.Remove(def); else game.RoomsByDef[def] = room;
                    room.DoorLevel = piece.Level;
                    game.SetWallMode(state == "cutaway" ? WallDisplayMode.Cutaway : state == "walls-down" || state.StartsWith("mat-") ? WallDisplayMode.Down : WallDisplayMode.Up);
                    if (state == "rebuilt") Invoke(game, "RebuildDoor", room);
                    else
                    {
                        room.DoorOpen = state == "open" || empty || state == "mat-dead";
                        room.DoorBroken = state == "broken";
                        room.DoorHp = room.DoorBroken ? 0 : game.MaxDoorHp(room);
                        Invoke(game, "RefreshDoor", room);
                    }
                    Invoke(game, "UpdateWallStateTexture", true);
                    Invoke(game, "UpdateDoorViews");
                    model.Present(1, Time.time + 1, true);
                    camera.Render(); RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                    string file = Path.Combine(output, "door-game-" + piece.Level + "-" + state + ".png");
                    File.WriteAllBytes(file, image.EncodeToPNG());
                    report.shots.Add(new GameShot { state = state, file = file, wallMode = game.WallMode.ToString(),
                        matState = DoorWelcomeMat.StateFor(game.Phase, empty ? null : room).ToString(),
                        mapSeed = game.Map.Seed, roomIndex = def.Index, doorLevel = room.DoorLevel,
                        visibleWallTriangles = game.VisibleWallTriangles, wallDrawCalls = game.ActualWallDrawCalls,
                        fog = game.FogActive, doorBlocks = room.DoorBlocks, meshVisible = model.Renderer.enabled,
                        actorPosition = game.Human.Pos, doorway = HotelMap.Center(def.DoorTile) });
                }
                File.WriteAllText(Path.Combine(output, "door-game-report.json"), JsonUtility.ToJson(report, true));
                Debug.Log("[DoorCapture] Wrote " + report.shots.Count + " actual hotel captures to " + output);
            }
            finally
            {
                RenderTexture.active = oldActive;
                if (game != null) game.DisposeSimulation();
                if (camera != null) Object.DestroyImmediate(camera.gameObject);
                if (target != null) Object.DestroyImmediate(target);
                if (image != null) Object.DestroyImmediate(image);
                UnityEngine.Random.state = oldRandom; Sprites.ArtOverride = oldArt; Time.timeScale = oldScale;
            }
        }

        static object Invoke(GameManager game, string method, params object[] args) =>
            typeof(GameManager).GetMethod(method, Private).Invoke(game, args);

        static GameObject Child(Transform parent, string name)
        {
            var child = new GameObject(name) { layer = Layer, hideFlags = HideFlags.HideAndDontSave };
            child.transform.SetParent(parent, false); return child;
        }

        static void AddFloor(Transform parent, List<Object> objects)
        {
            var mesh = new Mesh { name = "Door QA floor", vertices = new[] { new Vector3(-1.5f, -1, .045f),
                new Vector3(2.5f, -1, .045f), new Vector3(2.5f, 2.5f, .045f), new Vector3(-1.5f, 2.5f, .045f) },
                triangles = new[] { 0, 2, 1, 0, 3, 2 } };
            mesh.RecalculateNormals(); objects.Add(mesh);
            var material = new Material(Shader.Find("Unlit/Color")) { color = new Color(.16f, .135f, .12f) };
            objects.Add(material);
            var floor = Child(parent, "Inspection floor");
            floor.AddComponent<MeshFilter>().sharedMesh = mesh; floor.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        static Texture2D AddFrame(Transform parent, List<Object> objects)
        {
            var kit = WallKit.Load();
            var piece = kit?.Get("door_frame");
            if (piece == null) throw new InvalidOperationException("The shared hotel door frame must be baked.");
            var instances = new WallInstances();
            instances.Add(piece, new Rect(-.1f, .35f, 1.2f, .3f), Vector2.down, 0, 1, 3);
            var record = instances.Records[0];
            var frame = Child(parent, "Shared hotel door frame");
            frame.transform.SetPositionAndRotation(record.Matrix.GetColumn(3), record.Matrix.rotation);
            frame.transform.localScale = record.Matrix.lossyScale;
            frame.AddComponent<MeshFilter>().sharedMesh = piece.Mesh;
            var material = new Material(Resources.Load<Shader>("Shaders/HotelWall")) { mainTexture = kit.Atlas };
            material.SetVector("_WallInstance", record.Data); material.SetFloat("_WallClock", 20);
            var state = new Texture2D(1, 2, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point };
            objects.Add(material); objects.Add(state); SetFrameHeight(state, WallGraph.FullHeight);
            material.SetTexture("_WallStates", state); frame.AddComponent<MeshRenderer>().sharedMaterial = material;
            return state;
        }

        static void SetFrameHeight(Texture2D state, float height)
        {
            state.SetPixel(0, 0, new Color(height, height, 1, 1));
            state.SetPixel(0, 1, Color.clear); state.Apply();
        }
    }
}
