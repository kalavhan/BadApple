using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BadAppleHotel.Config;
using BadAppleHotel.Game;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace BadAppleHotel.Tests
{
    public class DoorKitTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        readonly List<Object> owned = new List<Object>();
        GameManager game;
        Random.State random;
        bool? art;
        float timeScale;

        [SetUp] public void SetUp()
        {
            random = Random.state; art = Sprites.ArtOverride; timeScale = Time.timeScale;
        }

        [TearDown] public void TearDown()
        {
            if (game != null) game.DisposeSimulation();
            foreach (var item in owned) if (item != null) Object.DestroyImmediate(item);
            owned.Clear();
            Random.state = random; Sprites.ArtOverride = art; Time.timeScale = timeScale;
        }

        [Test] public void Available_baked_doors_fit_the_frame_and_ship_complete_mobile_meshes()
        {
            var kit = Kit();
            foreach (int level in Enumerable.Range(1, DoorKit.LastArtLevel))
                Assert.NotNull(kit.Get(level), "The imported door design " + level + " must ship.");
            Assert.AreEqual(kit.Pieces.Length, kit.Pieces.Select(piece => piece.Level).Distinct().Count(), "One baked piece per design.");
            foreach (var piece in kit.Pieces)
            {
                string context = "Door " + piece.Level;
                Assert.NotNull(piece.Mesh, context);
                Assert.NotNull(piece.Albedo, context);
                Assert.IsNotEmpty(piece.Source, context + " source provenance");
                var mesh = piece.Mesh;
                var bounds = mesh.bounds;
                Assert.Greater(mesh.vertexCount, 0, context);
                Assert.AreEqual(mesh.vertexCount, mesh.uv.Length, context + " UVs");
                Assert.AreEqual(mesh.vertexCount, mesh.normals.Length, context + " normals");
                Assert.AreEqual(0, bounds.min.x, .001f, context + " left hinge");
                Assert.AreEqual(0, bounds.max.z, .001f, context + " rests on the floor");
                Assert.Less(Mathf.Abs(bounds.center.y), .001f, context + " centered door depth");
                Assert.LessOrEqual(bounds.size.x, DoorKit.Width + .001f, context + " doorway width");
                Assert.LessOrEqual(bounds.size.z, DoorKit.Height + .001f, context + " lintel clearance");
                Assert.LessOrEqual(bounds.size.y, DoorKit.MaxDepth + .001f, context + " leaf depth");
                Assert.Greater(bounds.size.x, .75f, context + " spans the opening");
                Assert.Greater(bounds.size.z, 1.1f, context + " useful leaf height");
                Assert.AreEqual(bounds.size, piece.Size, context + " recorded dimensions");
                Assert.AreEqual(mesh.triangles.Length / 3, piece.Triangles, context + " recorded geometry budget");
                Assert.LessOrEqual(piece.Triangles, 4400, context + " Android geometry budget");
                Assert.LessOrEqual(piece.Albedo.width, 512, context + " mobile texture width");
                Assert.LessOrEqual(piece.Albedo.height, 512, context + " mobile texture height");
                Assert.IsTrue(mesh.triangles.All(index => index >= 0 && index < mesh.vertexCount), context + " valid triangle indices");
            }
        }

        [Test] public void Closed_panels_are_parallel_to_the_wall_instead_of_preserving_export_yaw()
        {
            foreach (var piece in Kit().Pieces)
            {
                var vertices = piece.Mesh.vertices; var triangles = piece.Mesh.triangles;
                var rear = Vector3.zero;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    var face = Vector3.Cross(vertices[triangles[i + 1]] - vertices[triangles[i]],
                        vertices[triangles[i + 2]] - vertices[triangles[i]]);
                    if (face.magnitude > .000001f && face.y / face.magnitude > .8f) rear += face;
                }
                Assert.Greater(rear.y, .1f, "Door " + piece.Level + " needs a measurable rear panel.");
                float skew = Mathf.Abs(Mathf.Atan2(rear.x, rear.y) * Mathf.Rad2Deg);
                Assert.Less(skew, 2.5f, "Door " + piece.Level + " closed panel must line up with the frame.");
            }
        }

        [Test] public void Missing_designs_keep_their_sprite_and_only_gameplay_levels_eight_to_ten_reuse_design_seven()
        {
            var kit = Own(ScriptableObject.CreateInstance<DoorKit>());
            var cardboard = Kit().Get(1);
            var skull = new DoorKit.Piece { Level = 7, Mesh = cardboard.Mesh, Albedo = cardboard.Albedo };
            kit.Pieces = new[] { cardboard, skull };
            Assert.AreSame(cardboard, kit.Get(1));
            for (int level = 2; level <= 6; level++) Assert.IsNull(kit.Get(level), "Missing design " + level);
            for (int level = 7; level <= 10; level++) Assert.AreSame(skull, kit.Get(level), "Gameplay level " + level);
            Assert.IsNull(kit.Get(0));
            kit.Pieces = new[] { cardboard };
            for (int level = 7; level <= 10; level++) Assert.IsNull(kit.Get(level), "Do not substitute a different door for an absent skull.");
        }

        [TestCase(ShaderType.Vertex)] [TestCase(ShaderType.Fragment)]
        public void Door_shader_compiles_for_android_GLES3(ShaderType stage)
        {
            var shader = Resources.Load<Shader>("Shaders/HotelDoor");
            Assert.NotNull(shader);
            var result = ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(0).CompileVariant(stage,
                new string[0], ShaderCompilerPlatform.GLES3x, BuildTarget.Android, GraphicsTier.Tier2, true);
            Assert.IsTrue(result.Success, string.Join("\n", result.Messages.Select(message => message.message)));
        }

        [Test] public void Every_baked_door_breaks_into_textured_rubble_and_rebuilds_without_changing_materials()
        {
            foreach (var piece in Kit().Pieces)
            {
                var rubble = piece.BrokenMesh;
                Assert.NotNull(rubble, "Door " + piece.Level + " must have textured 3D rubble.");
                Assert.AreEqual(piece.Triangles, rubble.triangles.Length / 3, "Breaking repositions existing triangles without duplicating them.");
                Assert.AreEqual(rubble.vertexCount, rubble.uv.Length);
                Assert.AreEqual(rubble.vertexCount, rubble.normals.Length);
                var bounds = rubble.bounds;
                Assert.GreaterOrEqual(bounds.min.x, -.001f);
                Assert.LessOrEqual(bounds.max.x, DoorKit.Width + .001f);
                Assert.AreEqual(0, bounds.center.y, .001f);
                Assert.LessOrEqual(bounds.size.y, .601f, "Debris stays near the doorway.");
                Assert.GreaterOrEqual(bounds.min.z, -.121f, "Fragments stay below ankle height.");
                Assert.Less(bounds.max.z, 0, "Fragments sit above the floor rather than z-fighting it.");

                var model = Model(new RoomDef { DoorInside = Vector2Int.up });
                var material = MaterialFor(piece);
                model.SetState(piece, material, false, false, true, 1, 0);
                model.SetState(piece, material, false, true, true, 0, 1);
                model.Present(1, 1, true);
                Assert.IsTrue(model.Renderer.enabled);
                Assert.AreSame(rubble, model.GetComponent<MeshFilter>().sharedMesh);
                Assert.AreSame(material, model.Renderer.sharedMaterial);
                Assert.AreEqual(0, Properties(model).GetFloat("_Power"));
                Assert.AreEqual(0, Properties(model).GetFloat("_Hit"));
                Assert.AreEqual(0, Properties(model).GetFloat("_Pulse"));
                model.Present(0, 2, false);
                Assert.IsFalse(model.Renderer.enabled, "Rubble obeys fog too.");
                model.SetState(piece, material, false, false, true, 1, 3);
                model.Present(1, 3, true);
                Assert.AreSame(piece.Mesh, model.GetComponent<MeshFilter>().sharedMesh);
                Assert.IsTrue(model.Renderer.enabled);
                Assert.AreSame(material, model.Renderer.sharedMaterial, "Rebuild shares the original material for tier " + piece.Level);
                Assert.AreEqual(1, Properties(model).GetFloat("_Pulse"), "One rebuild pulse for tier " + piece.Level);
            }
        }

        [Test] public void Gameplay_upgrades_above_seven_still_pulse_when_they_share_the_same_art()
        {
            var piece = MagicalPiece(); piece.Level = 7;
            var model = Model(new RoomDef { DoorInside = Vector2Int.up });
            var material = MaterialFor(piece);
            model.SetState(piece, material, false, false, true, 1, 0, 7);
            for (int level = 8; level <= 10; level++)
            {
                float clock = level - 7;
                model.SetState(piece, material, false, false, true, 1, clock, level);
                model.Present(0, clock, true);
                Assert.AreEqual(1, Properties(model).GetFloat("_Pulse"), "Upgrade to " + level);
                model.SetState(piece, material, false, false, true, 1, clock + .66f, level);
                model.Present(0, clock + .66f, true);
                Assert.AreEqual(0, Properties(model).GetFloat("_Pulse"), "Ordinary refresh must not restart the upgrade pulse.");
                Assert.AreEqual(7, model.Tier);
                Assert.AreSame(material, model.Renderer.sharedMaterial);
            }
        }

        [TestCase(0, 1)] [TestCase(0, -1)] [TestCase(1, 0)] [TestCase(-1, 0)]
        public void All_four_doorway_directions_swing_ninety_degrees_into_the_room_about_a_fixed_hinge(int x, int y)
        {
            foreach (var piece in Kit().Pieces)
            {
                var room = new RoomDef { Index = 3, DoorTile = new Vector2Int(12, 8) };
                var inward = new Vector2Int(x, y);
                room.DoorInside = room.DoorTile + inward;
                room.DoorOutside = room.DoorTile - inward;
                var model = Model(room);
                var material = MaterialFor(piece);
                model.SetState(piece, material, false, false, true, 1, 0);
                model.Present(0, 0, true);
                Vector3 hinge = model.transform.position;
                Vector3 closedCenter = model.transform.TransformPoint(new Vector3(DoorKit.Width / 2, 0, 0));
                Assert.Less(Vector3.Distance(closedCenter, HotelMap.Center(room.DoorTile)), .0001f, "Closed leaf covers the logical doorway.");

                model.SetState(piece, material, true, false, true, 1, 1);
                model.Present(.1f, 1.1f, true);
                Assert.Greater(model.OpenAngle, 0, "Opening is animated.");
                Assert.Less(model.OpenAngle, DoorModel.OpenDegrees);
                model.Present(1, 2, true);
                Assert.AreEqual(90, model.OpenAngle, .0001f);
                Assert.Less(Vector3.Distance(hinge, model.transform.position), .0001f, "Hinge must not slide.");
                var openTip = model.transform.TransformPoint(new Vector3(DoorKit.Width, 0, 0));
                Assert.Less(Vector3.Distance(openTip - hinge, (Vector3)(Vector2)inward * DoorKit.Width), .0001f, "The free edge swings inward, never into the corridor.");
                Assert.IsEmpty(model.GetComponentsInChildren<Collider>(), "The visual must not add a second collision system.");
                Assert.IsEmpty(model.GetComponentsInChildren<Collider2D>());

                model.SetState(piece, material, false, false, true, 1, 3);
                model.Present(1, 4, true);
                Assert.AreEqual(0, model.OpenAngle, .0001f);
                Assert.Less(Vector3.Distance(closedCenter, model.transform.TransformPoint(new Vector3(DoorKit.Width / 2, 0, 0))), .0001f);
            }
        }

        [Test] public void Hidden_broken_and_unpowered_doors_cannot_emit_supernatural_light()
        {
            var model = Model(new RoomDef { DoorInside = Vector2Int.up });
            var piece = MagicalPiece();
            var material = MaterialFor(piece);
            model.SetState(piece, material, false, false, true, 1, 0);
            model.Present(0, 0, true);
            Assert.IsTrue(model.Renderer.enabled);
            Assert.AreEqual(1, Properties(model).GetFloat("_Power"));

            model.Present(0, 1, false);
            Assert.IsFalse(model.Renderer.enabled);
            Assert.AreEqual(0, Properties(model).GetFloat("_Visibility"));
            Assert.AreEqual(0, Properties(model).GetFloat("_Power"));

            model.SetState(piece, material, false, true, true, 0, 4);
            model.Present(0, 4, true);
            Assert.IsFalse(model.Renderer.enabled);
            Assert.IsFalse(model.Powered);
            Assert.AreEqual(0, Properties(model).GetFloat("_Power"));
            Assert.AreEqual(0, Properties(model).GetFloat("_Hit"));
            Assert.AreEqual(0, Properties(model).GetFloat("_Pulse"));

            model.SetState(piece, material, false, false, false, 1, 5);
            model.Present(0, 5, true);
            Assert.IsTrue(model.Renderer.enabled);
            Assert.AreEqual(0, Properties(model).GetFloat("_Power"));
        }

        [Test] public void Damage_hits_decay_and_rebuilding_restores_the_leaf_with_one_local_pulse()
        {
            var room = new RoomDef { Index = 2, DoorTile = new Vector2Int(7, 9), DoorInside = new Vector2Int(7, 10) };
            var model = Model(room);
            var piece = MagicalPiece();
            var material = MaterialFor(piece);
            model.SetState(piece, material, false, false, true, 1, 0);
            model.SetState(piece, material, false, false, true, .65f, 1);
            model.Present(0, 1, true);
            Assert.AreEqual(.35f, Properties(model).GetFloat("_Damage"), .0001f);
            Assert.AreEqual(1, Properties(model).GetFloat("_Hit"));
            Assert.AreEqual(0, Properties(model).GetFloat("_Pulse"));
            model.Present(0, 1.31f, true);
            Assert.AreEqual(0, Properties(model).GetFloat("_Hit"));

            model.SetState(piece, material, false, true, true, 0, 2);
            model.SetState(piece, material, false, false, true, 1, 3);
            model.Present(0, 3, true);
            Assert.IsTrue(model.Renderer.enabled);
            Assert.AreEqual(1, Properties(model).GetFloat("_Pulse"));
            Assert.AreEqual(0, Properties(model).GetFloat("_Damage"));
            var ground = HotelMap.Center(room.DoorTile);
            Assert.AreEqual(new Vector4(ground.x, ground.y, 0, 0), Properties(model).GetVector("_DoorGround"), "Fog samples the doorway even when the leaf moves.");
            model.Present(0, 3.66f, true);
            Assert.AreEqual(0, Properties(model).GetFloat("_Pulse"));
            Assert.AreSame(material, model.Renderer.sharedMaterial, "Effects stay per-renderer and do not clone materials.");
        }

        [Test] public void Real_room_closed_open_broken_and_rebuilt_states_keep_path_blocking_and_visuals_in_agreement()
        {
            BuildGame();
            var player = game.Human;
            var def = game.Map.Rooms[0]; def.Isolated = false;
            Assert.IsTrue(game.Claim(player, def, true));
            UseDoorRendering();
            var room = player.Room;
            foreach (var piece in Kit().Pieces)
            {
                room.DoorLevel = piece.Level;
                room.DoorBroken = false; room.DoorOpen = false; room.DoorHp = game.MaxDoorHp(room);
                Refresh(room);
                var model = Models()[def];
                Present(model);
                Assert.IsTrue(model.Renderer.enabled);
                Assert.IsTrue(model.Powered);
                Assert.IsNull(room.DoorSr.sprite);
                AssertPassage(room, false);

                room.DoorOpen = true; Refresh(room); Present(model);
                Assert.AreEqual(90, model.OpenAngle);
                AssertPassage(room, true);
                room.DoorOpen = false; room.DoorBroken = true; room.DoorHp = 0;
                Refresh(room); Present(model);
                Assert.IsFalse(model.Powered);
                var brokenMesh = Kit().Get(room.DoorLevel).BrokenMesh;
                if (brokenMesh != null)
                {
                    Assert.IsTrue(model.Renderer.enabled, "Low debris preserves the broken silhouette without blocking the passage.");
                    Assert.AreSame(brokenMesh, model.GetComponent<MeshFilter>().sharedMesh);
                    Assert.IsNull(room.DoorSr.sprite);
                }
                else
                {
                    Assert.IsFalse(model.Renderer.enabled);
                    Assert.IsTrue(room.DoorSr.enabled);
                    Assert.AreSame(Sprites.DoorBroken, room.DoorSr.sprite);
                }
                AssertPassage(room, true);

                Invoke("RebuildDoor", room); Present(model);
                Assert.IsFalse(room.DoorBroken);
                Assert.AreEqual(game.MaxDoorHp(room), room.DoorHp);
                Assert.IsFalse(room.DoorOpen, "A clear doorway rebuilds shut.");
                Assert.IsTrue(model.Renderer.enabled);
                Assert.IsTrue(model.Powered);
                Assert.AreSame(Kit().Get(room.DoorLevel).Mesh, model.GetComponent<MeshFilter>().sharedMesh);
                Assert.IsNull(room.DoorSr.sprite);
                AssertPassage(room, false);
            }
        }

        [Test] public void Unclaimed_doors_are_open_and_missing_tiers_return_to_the_existing_sprite()
        {
            BuildGame();
            var def = game.Map.Rooms[0]; def.Isolated = false;
            UseDoorRendering();
            Invoke("ApplyDoorLook", def, null);
            var model = Models()[def];
            Present(model);
            Assert.AreEqual(90, model.OpenAngle);
            Assert.IsFalse(model.Powered);
            Assert.IsTrue(game.MonsterWalkable(def.DoorTile.x, def.DoorTile.y));

            // Inject a deliberately partial kit to cover fallback even though all seven designs ship.
            var kit = Own(ScriptableObject.CreateInstance<DoorKit>());
            kit.Pieces = new[] { Kit().Get(1) };
            typeof(GameManager).GetField("doorKit", Private).SetValue(game, kit);
            Assert.IsTrue(game.Claim(game.Human, def, true));
            var room = game.Human.Room;
            room.DoorLevel = 2; room.DoorHp = game.MaxDoorHp(room); Refresh(room);
            Assert.IsFalse(model.gameObject.activeSelf);
            Assert.IsTrue(room.DoorSr.enabled);
            Assert.AreSame(Sprites.Door(2), room.DoorSr.sprite);
            AssertPassage(room, false);
            room.DoorLevel = 1; room.DoorHp = game.MaxDoorHp(room); Refresh(room); Present(model);
            Assert.AreSame(model, Models()[def]);
            Assert.IsTrue(model.gameObject.activeSelf);
            Assert.IsNull(room.DoorSr.sprite);
        }

        [Test] public void Rooms_share_one_material_per_design_and_disposal_releases_owned_materials()
        {
            BuildGame();
            for (int i = 0; i < 2; i++)
            {
                game.Map.Rooms[i].Isolated = false;
                Assert.IsTrue(game.Claim(game.Residents[i], game.Map.Rooms[i], true));
            }
            UseDoorRendering();
            var pieces = Kit().Pieces;
            for (int pass = 0; pass < 3; pass++) foreach (var piece in pieces)
            {
                foreach (var resident in game.Residents.Take(2))
                {
                    resident.Room.DoorLevel = piece.Level;
                    resident.Room.DoorHp = game.MaxDoorHp(resident.Room);
                    Refresh(resident.Room); Present(Models()[resident.Room.Def]);
                }
                Assert.AreSame(Models()[game.Residents[0].Room.Def].Renderer.sharedMaterial,
                    Models()[game.Residents[1].Room.Def].Renderer.sharedMaterial, "Tier " + piece.Level);
            }
            var materials = (Dictionary<int, Material>)typeof(GameManager).GetField("doorMaterials", Private).GetValue(game);
            Assert.AreEqual(pieces.Length, materials.Count, "No new material per room, frame, or upgrade cycle.");
            var released = materials.Values.ToArray();
            var views = Models().Values.ToArray();
            foreach (var piece in pieces)
            {
                Assert.AreSame(piece.Albedo, materials[piece.Level].mainTexture);
                Assert.AreEqual(piece.Level, materials[piece.Level].GetFloat("_Tier"));
            }
            game.DisposeSimulation(); game = null;
            Assert.IsTrue(released.All(material => material == null), "Scene-owned door materials must be destroyed.");
            Assert.IsTrue(views.All(model => model == null), "Door leaves must not survive their world.");
            Assert.IsTrue(pieces.All(piece => piece.Mesh != null && piece.Albedo != null), "Shared baked assets remain usable.");
        }

        [Test] public void Welcome_mats_follow_live_room_state_and_share_resources_until_world_disposal()
        {
            BuildGame(); UseDoorRendering(); Invoke("BuildWorld", 241);
            var mats = (Dictionary<RoomDef, DoorWelcomeMat>)typeof(GameManager).GetField("doorMats", Private).GetValue(game);
            Assert.AreEqual(game.Map.Rooms.Count, mats.Count);
            Assert.AreEqual(1, mats.Values.Select(mat => mat.Renderer.sharedMaterial).Distinct().Count());
            Assert.AreEqual(1, mats.Values.Select(mat => mat.GetComponent<MeshFilter>().sharedMesh).Distinct().Count());
            var def = game.Map.Rooms[0]; def.Isolated = false;
            var mat = mats[def];
            Invoke("UpdateDoorViews"); Assert.AreEqual(DoorMatState.Available, mat.State);
            Assert.IsTrue(game.Claim(game.Human, def, true));
            Invoke("UpdateDoorViews"); Assert.AreEqual(DoorMatState.Occupied, mat.State);
            typeof(GameManager).GetProperty(nameof(GameManager.Phase)).SetValue(game, Phase.Night);
            game.Human.Asleep = true;
            Invoke("UpdateDoorViews"); Assert.AreEqual(DoorMatState.Sleeping, mat.State);
            game.Human.Asleep = false;
            Invoke("UpdateDoorViews"); Assert.AreEqual(DoorMatState.Awake, mat.State);
            game.Human.Alive = false;
            Invoke("UpdateDoorViews"); Assert.AreEqual(DoorMatState.Dead, mat.State);
            game.RoomsByDef.Remove(def);
            Invoke("UpdateDoorViews"); Assert.AreEqual(DoorMatState.Empty, mat.State);
            typeof(GameManager).GetProperty(nameof(GameManager.Phase)).SetValue(game, Phase.RoleSelect);
            Invoke("UpdateDoorViews"); Assert.IsTrue(mats.Values.All(item => !item.Renderer.enabled));
            var material = mat.Renderer.sharedMaterial; var mesh = mat.GetComponent<MeshFilter>().sharedMesh;
            var views = mats.Values.ToArray();
            game.DisposeSimulation(); game = null;
            Assert.IsTrue(material == null && mesh == null && views.All(item => item == null), "The world owns and releases every marker resource.");
        }

        [Test] public void Every_door_renders_identically_with_walls_up_cutaway_or_down()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Requires a graphics device.");
            BuildGame(); UseDoorRendering();
            var camera = Own(new GameObject("Door wall-mode regression camera")).AddComponent<Camera>();
            camera.enabled = false; camera.orthographic = true; camera.orthographicSize = .78f;
            camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black; camera.allowHDR = false; camera.allowMSAA = false;
            typeof(GameManager).GetProperty(nameof(GameManager.Cam)).SetValue(game, camera);
            // Build the real hotel around the simulation roster without instantiating unrelated character rigs.
            Invoke("BuildWorld", 241);
            var def = game.Map.Rooms.First(room => WallVisibility.RoomHeight(room.DoorInside - room.DoorTile) == WallGraph.DownHeight);
            def.Isolated = false;
            Assert.IsTrue(game.Claim(game.Human, def, true));
            game.Human.Pos = HotelMap.Center(def.DoorInside);
            Invoke("UpdateVision");
            var room = game.Human.Room;
            var model = Models()[def]; model.gameObject.layer = 31;
            var rotation = Quaternion.Euler(0, 0, def.DoorRotation) * Quaternion.LookRotation(Vector3.up, Vector3.back);
            var center = (Vector3)HotelMap.Center(def.DoorTile) + Vector3.back * .7f;
            camera.transform.SetPositionAndRotation(center - rotation * Vector3.forward * 5, rotation);
            var target = Own(new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32));
            target.Create(); camera.targetTexture = target;
            var read = Own(new Texture2D(256, 256, TextureFormat.RGB24, false));
            var previous = RenderTexture.active;
            try
            {
                foreach (var piece in Kit().Pieces)
                {
                    room.DoorLevel = piece.Level; room.DoorOpen = false; room.DoorBroken = false;
                    room.DoorHp = game.MaxDoorHp(room); Refresh(room);
                    model.Present(1, Time.time, true);
                    Color32[] baseline = null;
                    foreach (var mode in new[] { WallDisplayMode.Up, WallDisplayMode.Cutaway, WallDisplayMode.Down })
                    {
                        game.SetWallMode(mode);
                        Invoke("UpdateWallStateTexture", true);
                        Invoke("UpdateDoorViews");
                        Assert.IsTrue(model.Renderer.enabled, "Door " + piece.Level + " / " + mode);
                        camera.Render(); RenderTexture.active = target;
                        read.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); read.Apply();
                        var pixels = read.GetPixels32();
                        if (baseline == null)
                        {
                            baseline = pixels;
                            Assert.Greater(LitPixels(pixels), 1000, "The complete door must actually render.");
                        }
                        else Assert.AreEqual(0, ChangedPixels(baseline, pixels),
                            "Door " + piece.Level + " must stay full height and opaque with walls " + mode);
                    }
                }
            }
            finally { RenderTexture.active = previous; camera.targetTexture = null; target.Release(); }
        }

        [Test] public void Imported_magical_doors_render_animated_energy_only_while_powered_and_visible()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Requires a graphics device.");
            const int pixels = 384, layer = 31;
            var camera = Own(new GameObject("Door emission camera")).AddComponent<Camera>();
            camera.enabled = false; camera.orthographic = true; camera.orthographicSize = .78f;
            camera.cullingMask = 1 << layer; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black; camera.allowHDR = false; camera.allowMSAA = false;
            var rotation = Quaternion.LookRotation(Vector3.up, Vector3.back);
            camera.transform.SetPositionAndRotation(new Vector3(.5f, .5f, -.7f) - rotation * Vector3.forward * 5, rotation);
            var target = Own(new RenderTexture(pixels, pixels, 24, RenderTextureFormat.ARGB32));
            target.Create(); camera.targetTexture = target;
            var read = Own(new Texture2D(pixels, pixels, TextureFormat.RGB24, false));
            var previous = RenderTexture.active;
            try
            {
                foreach (var piece in Kit().Pieces.Where(item => item.Level >= 4))
                {
                    string context = "Door " + piece.Level;
                    var model = Model(new RoomDef { DoorInside = Vector2Int.up });
                    model.gameObject.layer = layer;
                    var material = MaterialFor(piece);
                    // Keep the authored albedo for energy masks, but suppress its diffuse term.
                    // This isolates actual fragment-shader emission from ordinary texture brightness.
                    material.SetColor("_Color", Color.black);
                    material.SetFloat("_HotelFog", 0); material.SetFloat("_HotelLightingEnabled", 0);
                    material.SetTexture("_HotelVision", Texture2D.whiteTexture);
                    Color32[] Render(float clock, bool powered = true, bool visible = true,
                        bool broken = false)
                    {
                        model.SetState(piece, material, false, broken, powered, broken ? 0 : 1, 0);
                        model.Present(1, clock, visible);
                        camera.Render(); RenderTexture.active = target;
                        read.ReadPixels(new Rect(0, 0, pixels, pixels), 0, 0); read.Apply();
                        return read.GetPixels32();
                    }
                    var poweredFrame = Render(12);
                    Assert.Greater(LitPixels(poweredFrame), 8, context + " needs visible authored-face emission.");
                    Assert.Greater(ChangedPixels(poweredFrame, Render(14.5f)), 8, context + " idle energy must animate.");
                    Assert.AreEqual(0, LitPixels(Render(12, powered: false)), context + " unpowered energy");
                    Assert.AreEqual(0, LitPixels(Render(12, visible: false)), context + " hidden energy");
                    Assert.AreEqual(0, LitPixels(Render(12, broken: true)), context + " broken energy");
                    material.SetFloat("_HotelFog", 1); material.SetTexture("_HotelVision", Texture2D.blackTexture);
                    Assert.AreEqual(0, LitPixels(Render(12)), context + " shader fog must suppress energy even if the renderer remains enabled.");
                    Object.DestroyImmediate(model.gameObject);
                }
            }
            finally
            {
                RenderTexture.active = previous; camera.targetTexture = null; target.Release();
            }
        }

        static int LitPixels(Color32[] pixels) => pixels.Count(pixel => pixel.r > 1 || pixel.g > 1 || pixel.b > 1);
        static int ChangedPixels(Color32[] a, Color32[] b)
        {
            int count = 0;
            for (int i = 0; i < a.Length; i++)
                if (Mathf.Abs(a[i].r - b[i].r) > 1 || Mathf.Abs(a[i].g - b[i].g) > 1 || Mathf.Abs(a[i].b - b[i].b) > 1) count++;
            return count;
        }

        static DoorKit Kit()
        {
            var kit = DoorKit.Load();
            Assert.NotNull(kit, "Bake the imported door exports before running these tests.");
            return kit;
        }
        static DoorKit.Piece MagicalPiece()
        {
            // Omit rubble on this copy to retain broken-sprite fallback coverage without mutating shipped art.
            var available = Kit().Get(6);
            Assert.NotNull(available, "The imported mirror door must ship.");
            return new DoorKit.Piece { Level = 6, Mesh = available.Mesh, Albedo = available.Albedo };
        }
        T Own<T>(T item) where T : Object { owned.Add(item); return item; }
        DoorModel Model(RoomDef room)
        {
            var model = Own(new GameObject("Door test leaf")).AddComponent<DoorModel>();
            model.Initialize(room); return model;
        }
        Material MaterialFor(DoorKit.Piece piece)
        {
            var shader = Resources.Load<Shader>("Shaders/HotelDoor");
            Assert.NotNull(shader);
            var material = Own(new Material(shader) { mainTexture = piece.Albedo });
            material.SetFloat("_Tier", piece.Level); return material;
        }
        static MaterialPropertyBlock Properties(DoorModel model)
        {
            var block = new MaterialPropertyBlock(); model.Renderer.GetPropertyBlock(block); return block;
        }
        void BuildGame()
        {
            game = new GameObject("Door gameplay fixture").AddComponent<GameManager>();
            game.StartSimulation(ConfigLoader.Load(), 241, false);
        }
        void UseDoorRendering() => typeof(GameManager).GetProperty(nameof(GameManager.Simulation)).SetValue(game, false);
        Dictionary<RoomDef, DoorModel> Models() => (Dictionary<RoomDef, DoorModel>)typeof(GameManager).GetField("doorModels", Private).GetValue(game);
        void Invoke(string method, params object[] args) => typeof(GameManager).GetMethod(method, Private).Invoke(game, args);
        void Refresh(Room room) => Invoke("RefreshDoor", room);
        static void Present(DoorModel model) => model.Present(1, Time.time + 1, true);
        void AssertPassage(Room room, bool open)
        {
            var tile = room.Def.DoorTile;
            Assert.AreEqual(!open, room.DoorBlocks);
            Assert.AreEqual(open, game.MonsterWalkable(tile.x, tile.y), "Monster pathing");
            Assert.AreEqual(open, game.WalkableFor(room.Owner, tile.x, tile.y), "Owner pathing");
        }
    }
}
