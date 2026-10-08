using System.Collections.Generic;
using System.Linq;
using BadAppleHotel.Game;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace BadAppleHotel.Tests
{
    public class DoorWelcomeMatTests
    {
        readonly List<Object> owned = new List<Object>();
        T Own<T>(T item) where T : Object { owned.Add(item); return item; }
        [TearDown] public void TearDown()
        {
            foreach (var item in owned) if (item != null) Object.DestroyImmediate(item);
            owned.Clear();
        }

        [Test] public void Color_states_follow_selection_occupancy_sleep_death_and_empty_rooms()
        {
            var room = new Room(new RoomDef()) { Owner = new Resident() };
            Assert.AreEqual(DoorMatState.Available, DoorWelcomeMat.StateFor(Phase.Setup, null));
            Assert.AreEqual(DoorMatState.Occupied, DoorWelcomeMat.StateFor(Phase.Setup, room));
            room.Owner.Asleep = true;
            Assert.AreEqual(DoorMatState.Occupied, DoorWelcomeMat.StateFor(Phase.Setup, room), "Selection stays red even when the guest sleeps early.");
            Assert.AreEqual(DoorMatState.Sleeping, DoorWelcomeMat.StateFor(Phase.Night, room));
            room.Owner.Asleep = false;
            Assert.AreEqual(DoorMatState.Awake, DoorWelcomeMat.StateFor(Phase.Night, room));
            room.Owner.Alive = false; room.Owner.Asleep = true;
            Assert.AreEqual(DoorMatState.Dead, DoorWelcomeMat.StateFor(Phase.Night, room), "Death takes precedence over stale sleep state.");
            Assert.AreEqual(DoorMatState.Empty, DoorWelcomeMat.StateFor(Phase.Night, null));
            room.Owner = null;
            Assert.AreEqual(DoorMatState.Empty, DoorWelcomeMat.StateFor(Phase.Night, room));
        }

        [TestCase(0, 1)] [TestCase(0, -1)] [TestCase(1, 0)] [TestCase(-1, 0)]
        public void Mat_is_flat_in_front_of_the_door_and_clear_of_the_wall_in_every_orientation(int x, int y)
        {
            var inward = new Vector2Int(x, y);
            var def = new RoomDef { DoorTile = new Vector2Int(7, 11) };
            def.DoorInside = def.DoorTile + inward; def.DoorOutside = def.DoorTile - inward;
            var mat = Mat(def);
            var center = (Vector2)mat.transform.position;
            Assert.Less(Vector2.Distance(HotelMap.Center(def.DoorTile) - (Vector2)inward * DoorWelcomeMat.OutsideOffset, center), .0001f);
            Assert.Greater(Vector2.Dot(-inward, center - HotelMap.Center(def.DoorTile)), .5f);
            foreach (var vertex in mat.GetComponent<MeshFilter>().sharedMesh.vertices)
            {
                var point = mat.transform.TransformPoint(vertex);
                Assert.AreEqual(DoorWelcomeMat.FloorZ, point.z, .0001f, "Mat stays flat above the floor.");
                Assert.Greater(Vector2.Dot(-inward, (Vector2)point - HotelMap.Center(def.DoorTile)), .3f, "No glow crosses under the wall or closed leaf.");
            }
            Assert.IsNull(mat.GetComponent<Collider>(), "The marker cannot block passage.");
        }

        [TestCase(ShaderType.Vertex)] [TestCase(ShaderType.Fragment)]
        public void Welcome_mat_shader_compiles_for_android_GLES3(ShaderType stage)
        {
            var result = ShaderUtil.GetShaderData(Shader()).GetSubshader(0).GetPass(0).CompileVariant(stage,
                new string[0], ShaderCompilerPlatform.GLES3x, BuildTarget.Android, GraphicsTier.Tier2, true);
            Assert.IsTrue(result.Success, string.Join("\n", result.Messages.Select(message => message.message)));
        }

        [Test] public void Rendered_marker_changes_color_and_disappears_when_hidden_or_in_fog()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Requires a graphics device.");
            var def = new RoomDef { DoorInside = Vector2Int.up, DoorOutside = Vector2Int.down };
            var mat = Mat(def); mat.gameObject.layer = 31;
            var material = mat.Renderer.sharedMaterial;
            material.SetFloat("_HotelFog", 0); material.SetTexture("_HotelVision", Texture2D.whiteTexture);
            var camera = Own(new GameObject("Welcome mat camera")).AddComponent<Camera>();
            camera.enabled = false; camera.orthographic = true; camera.orthographicSize = .7f;
            camera.cullingMask = 1 << 31; camera.backgroundColor = Color.black; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.allowHDR = false; camera.allowMSAA = false;
            camera.transform.SetPositionAndRotation(mat.transform.position + Vector3.back * 5, Quaternion.identity);
            var target = Own(new RenderTexture(128, 128, 24, RenderTextureFormat.ARGB32));
            target.Create(); camera.targetTexture = target;
            var read = Own(new Texture2D(128, 128, TextureFormat.RGB24, false));
            var previous = RenderTexture.active;
            try
            {
                Vector3 Render(Phase phase, Room room, bool visible = true)
                {
                    mat.Present(phase, room, visible, 12);
                    camera.Render(); RenderTexture.active = target;
                    read.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); read.Apply();
                    var sum = Vector3.zero;
                    foreach (var p in read.GetPixels32()) sum += new Vector3(p.r, p.g, p.b);
                    return sum;
                }
                var green = Render(Phase.Setup, null);
                Assert.Greater(green.y, green.x * 1.5f);
                var room = new Room(def) { Owner = new Resident { Asleep = true } };
                var red = Render(Phase.Setup, room);
                Assert.Greater(red.x, red.y * 2);
                var yellow = Render(Phase.Night, room);
                Assert.Greater(yellow.x, yellow.z * 2); Assert.Greater(yellow.y, yellow.z * 2);
                room.Owner.Asleep = false;
                Assert.Less(Render(Phase.Night, room).magnitude, yellow.magnitude * .8f);
                room.Owner.Alive = false;
                var dead = Render(Phase.Night, room);
                Assert.Greater(dead.x, dead.y * 2);
                var blue = Render(Phase.Night, null);
                Assert.Greater(blue.z, blue.x * 2);
                Assert.AreEqual(Vector3.zero, Render(Phase.Night, null, false));
                material.SetFloat("_HotelFog", 1); material.SetTexture("_HotelVision", Texture2D.blackTexture);
                Assert.AreEqual(Vector3.zero, Render(Phase.Night, null), "Fog also gates glow in the shader.");
            }
            finally { RenderTexture.active = previous; camera.targetTexture = null; target.Release(); }
        }

        Shader Shader()
        {
            var shader = Resources.Load<Shader>("Shaders/HotelDoorMat"); Assert.NotNull(shader); return shader;
        }
        DoorWelcomeMat Mat(RoomDef room)
        {
            var mat = Own(new GameObject("Welcome mat test")).AddComponent<DoorWelcomeMat>();
            mat.Initialize(room, Own(DoorWelcomeMat.CreateMesh()), Own(new Material(Shader())));
            return mat;
        }
    }
}
