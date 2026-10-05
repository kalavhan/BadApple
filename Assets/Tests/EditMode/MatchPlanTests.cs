using System.Collections;
using System.Linq;
using System.Reflection;
using BadAppleHotel.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BadAppleHotel.Tests
{
    public class MatchPlanTests
    {
        [UnitySetUp]
        public IEnumerator EnterMatchMode()
        {
            yield return new EnterPlayMode();
        }

        [UnityTest]
        public IEnumerator Sleeping_input_pans_without_waking_and_spare_rooms_remain_open()
        {
            GameManager gm = null;
            for (int frame = 0; frame < 60; frame++)
            {
                yield return null;
                gm = Object.FindAnyObjectByType<GameManager>();
                if (gm != null && gm.Cfg != null && gm.Map != null) break;
            }
            Assert.IsNotNull(gm);
            Assert.IsNotNull(gm.Cfg, gm.ConfigError);
            Assert.IsNotNull(gm.Map, gm.ConfigError);
            gm.StartMatch(Role.Resident, "stitchwork_chef");
            gm.SetSpeed(0);
            var me = gm.Human;
            // Use a central room so camera movement has room in both directions.
            var room = gm.Map.Rooms.OrderBy(r => Vector2.Distance(r.Center, new Vector2(gm.Map.W / 2, gm.Map.H / 2))).First();
            Assert.IsTrue(gm.Claim(me, room, true));
            Assert.AreEqual(ActionResult.Ok, gm.TrySleep(me));
            var resting = me.Pos;
            var expectedCenter = HotelView3D.Clamp(me.Pos, HotelView3D.FollowSize, gm.Cam.aspect, gm.Map.W, gm.Map.H);
            float settle = Time.realtimeSinceStartup + 1f;
            while (Vector2.Distance(HotelView3D.GroundPoint(gm.Cam,new Vector2(Screen.width/2f,Screen.height/2f)), expectedCenter) > 0.01f && Time.realtimeSinceStartup < settle) yield return null;
            var camera = gm.Cam.transform.position;
            gm.DragCamera(new Vector2(-150, 0));
            float panUntil = Time.realtimeSinceStartup + 2f;
            while (gm.Cam.transform.position.x <= camera.x + .01f && Time.realtimeSinceStartup < panUntil) yield return null;
            Assert.IsTrue(me.Asleep);
            Assert.AreEqual(resting, me.Pos);
            Assert.Greater(gm.Cam.transform.position.x, camera.x);
            GameInput.ClearAll();
            gm.RecenterCamera();
            float until = Time.realtimeSinceStartup + 1f;
            while (Time.realtimeSinceStartup < until) yield return null;
            Assert.Less(Vector2.Distance(HotelView3D.GroundPoint(gm.Cam,new Vector2(Screen.width/2f,Screen.height/2f)), expectedCenter), 0.5f);
            Assert.Greater(me.Sr.sortingOrder, me.Room.BedSr.sortingOrder);

            // Existing build path protection must still work on the new layouts.
            me.DreamPower = me.Faith = 100000;
            int slot = Enumerable.Range(0, me.Room.Slots.Length).First(i => gm.CanBuildAt(me.Room, i));
            Assert.AreEqual(ActionResult.Ok, gm.TryBuildTower(me, slot, "gun_turret"));
            var tower = me.Room.Slots[slot];
            var firstSprite = tower.Sr.sprite;
            for (int i = 0; i < 3; i++) Assert.AreEqual(ActionResult.Ok, gm.TryUpgradeTower(me, slot));
            Assert.AreEqual(4, tower.Level);
            Assert.AreNotSame(firstSprite, tower.Sr.sprite);
            Assert.AreEqual(HotelView3D.SpriteScale, tower.Sr.transform.localScale);
            Assert.AreEqual(ActionResult.MaxLevel, gm.TryUpgradeTower(me, slot));
            Assert.IsTrue(gm.CheckDoor(me.Room).Allowed);

            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                ScreenCapture.CaptureScreenshot("/tmp/badapple-sleep-controls.png");
                until = Time.realtimeSinceStartup + 0.5f;
                while (Time.realtimeSinceStartup < until) yield return null;
            }
            gm.Wake(me);
            Assert.IsFalse(me.Asleep);
            typeof(GameManager).GetMethod("BeginNights", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(gm, null);
            var empty = gm.Map.Rooms.Where(gm.IsRoomFree).ToArray();
            Assert.AreEqual(3, empty.Length);
            foreach (var spare in empty)
            {
                Assert.IsTrue(gm.MonsterWalkable(spare.DoorTile.x, spare.DoorTile.y));
                Assert.IsTrue(spare.Floor.All(t => gm.MonsterWalkable(t.x, t.y)));
            }
            Assert.AreEqual(gm.Cfg.match.bodyPartsPerNight, gm.Parts.Count);
            for (int i = 0; i < gm.Parts.Count; i++)
                for (int j = i + 1; j < gm.Parts.Count; j++)
                    Assert.GreaterOrEqual(Vector2Int.Distance(gm.Parts[i].Tile, gm.Parts[j].Tile), 12f);
            gm.ReturnToMenu();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            GameInput.ClearAll();
            if (Application.isPlaying) yield return new ExitPlayMode();
        }
    }
}
