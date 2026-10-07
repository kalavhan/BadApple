using System.Linq;
using BadAppleHotel.Config;
using BadAppleHotel.Game;
using BadAppleHotel.Rules;
using NUnit.Framework;
using UnityEngine;

namespace BadAppleHotel.Tests
{
    public class World3DTests
    {
        [Test] public void Exact_room_budgets_two_square_beds_and_wide_irregular_shapes_across_100_seeds()
        {
            var cfg=ConfigLoader.Load();var assignments=new System.Collections.Generic.HashSet<string>();
            for(int seed=1;seed<=100;seed++)
            {
                var map=new HotelMap(cfg.map,10,seed);
                CollectionAssert.AreEqual(new[]{20,20,20,23,23,23,26,26,26,30},map.Rooms.Select(r=>r.BuildBudget).OrderBy(n=>n));
                assignments.Add(string.Join(",",map.Rooms.Select(r=>r.BuildBudget)));
                foreach(var room in map.Rooms)
                {
                    Assert.AreEqual(1,Vector2Int.Distance(room.BedTile,room.BedHeadTile));
                    Assert.IsTrue(room.FloorSet.Contains(room.BedHeadTile));
                    Assert.IsFalse(room.Walkway.Contains(room.BedHeadTile));
                    Assert.IsTrue(room.BuildTiles.All(t=>!room.IsBedTile(t)&&t!=room.DoorInside));
                    int area=(room.Floor.Max(t=>t.x)-room.Floor.Min(t=>t.x)+1)*(room.Floor.Max(t=>t.y)-room.Floor.Min(t=>t.y)+1);
                    Assert.Less(room.Floor.Count,area,"no rectangular rooms");
                    foreach(var f in room.Floor)
                        Assert.IsTrue(new[]{-1,1}.Any(dx=>new[]{-1,1}.Any(dy=>room.FloorSet.Contains(f+new Vector2Int(dx,0))&&room.FloorSet.Contains(f+new Vector2Int(0,dy))&&room.FloorSet.Contains(f+new Vector2Int(dx,dy)))),"no narrow arms");
                    foreach(var bed in cfg.beds.levels)
                    {
                        var sprite=Sprites.Bed(bed.level);var scale=SleepPose.BedScale(sprite);var turn=Quaternion.Euler(0,0,room.BedRotation);
                        var bounds=sprite.bounds;var ext=Vector3.Scale(bounds.extents,scale);
                        foreach(float x in new[]{-1f,1f})foreach(float y in new[]{-1f,1f})
                        {
                            var corner=room.BedCenter+(Vector2)(turn*new Vector3(x*ext.x,y*ext.y,0));
                            Assert.IsTrue(room.IsBedTile(HotelMap.ToTile(corner)),"rendered bed must fit its two squares");
                        }
                        var head=room.BedCenter+(Vector2)(turn*Vector2.Scale(bed.sleepAnchor,scale));
                        Assert.IsTrue(room.IsBedTile(HotelMap.ToTile(head)),"pillow inside footprint");
                    }
                }
            }
            Assert.Greater(assignments.Count,90,"size order must shuffle independently of room number");
        }
        [Test] public void Sight_stops_at_walls_closed_doors_corners_and_distance()
        {
            System.Func<int,int,bool> wall=(x,y)=>x==2;
            Assert.IsFalse(Sight.Clear(new Vector2(.5f,.5f),new Vector2(4.5f,.5f),wall));
            Assert.IsTrue(Sight.Clear(new Vector2(.5f,.5f),new Vector2(2.5f,.5f),wall,true));
            Assert.IsFalse(Sight.Clear(new Vector2(.5f,.5f),new Vector2(1.5f,1.5f),(x,y)=>x==1&&y==0));
            Assert.IsTrue(Sight.Clear(new Vector2(.5f,.5f),new Vector2(.5f,6.5f),wall));
            Assert.IsFalse(Sight.Clear(new Vector2(.5f,.5f),new Vector2(4.5f,.5f),(x,y)=>x==2&&y==0));
        }
        [Test] public void Fixed_camera_ground_picking_and_screen_movement_agree()
        {
            var go=new GameObject("camera test");var camera=go.AddComponent<Camera>();
            try
            {
                camera.orthographic=true;camera.orthographicSize=7.5f;
                camera.transform.SetPositionAndRotation(new Vector3(20,20,0)-HotelView3D.Forward*100,HotelView3D.Rotation);
                foreach(var point in new[]{new Vector2(19,21),new Vector2(20,20),new Vector2(23,18)})
                    Assert.Less(Vector2.Distance(point,HotelView3D.GroundPoint(camera,camera.WorldToScreenPoint(point))),.001f);
                Assert.Greater(HotelView3D.Facing(HotelView3D.Move(Vector2.right)).x,.9f);
                Assert.Less(Mathf.Abs(HotelView3D.Facing(HotelView3D.Move(Vector2.up)).x),.001f);
            }
            finally { Object.DestroyImmediate(go); }
        }
        [Test] public void Directional_towers_have_32_distinct_views_per_family()
        {
            foreach(string id in new[]{"gun_turret","sniper_nest"})
            {
                var frames=new System.Collections.Generic.HashSet<Sprite>();
                for(int level=1;level<=4;level++)for(int dir=0;dir<8;dir++)
                {
                    float angle=dir*Mathf.PI/4;var face=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
                    var sprite=TowerDirections.Get(id,level,face);Assert.IsNotNull(sprite);frames.Add(sprite);
                    Assert.Greater(sprite.rect.width,200);Assert.Greater(sprite.rect.height,200);
                }
                Assert.AreEqual(32,frames.Count);
            }
        }
        [Test] public void Range_tradeoffs_have_real_bonuses_blind_spots_and_mixed_prices()
        {
            var cfg=ConfigLoader.Load();var towers=cfg.towers.towers;
            var shortTower=towers.First(t=>t.id=="flame_brazier");var mid=towers.First(t=>t.id=="gun_turret");var longTower=towers.First(t=>t.id=="sniper_nest");
            Assert.AreEqual(1.35f,UpgradeRules.DistanceBonus(shortTower,1.9f),.001f);
            Assert.AreEqual(1,UpgradeRules.DistanceBonus(shortTower,2.1f));
            Assert.IsFalse(UpgradeRules.InRange(cfg.towers,longTower,1,1.9f));
            Assert.IsTrue(UpgradeRules.InRange(cfg.towers,longTower,1,2));
            Assert.AreEqual(1.3f,UpgradeRules.DistanceBonus(longTower,6.1f),.001f);
            Assert.IsTrue(UpgradeRules.InRange(cfg.towers,mid,1,.2f));
            Assert.Less(towers.First(t=>t.id=="missile_launcher").buildCost,towers.First(t=>t.id=="electric_tower").buildCost);
        }
    }
}
