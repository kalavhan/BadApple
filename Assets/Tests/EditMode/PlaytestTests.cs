using System;
using System.Linq;
using BadAppleHotel.Config;
using BadAppleHotel.Game;
using NUnit.Framework;
using UnityEngine;
namespace BadAppleHotel.Tests
{
    public class PlaytestTests
    {
        [Test] public void Natural_rooms_have_space_walls_and_connected_paths()
        {
            var cfg=ConfigLoader.Load();
            for(int seed=1;seed<=50;seed++)
            {
                var map=new HotelMap(cfg.map,10,seed);
                foreach(var room in map.Rooms)
                {
                    Assert.GreaterOrEqual(room.BuildBudget,20,$"seed {seed}");
                    Assert.GreaterOrEqual((float)room.Floor.Count/((room.Floor.Max(t=>t.x)-room.Floor.Min(t=>t.x)+1)*(room.Floor.Max(t=>t.y)-room.Floor.Min(t=>t.y)+1)),.6f);
                    Assert.IsTrue(new[]{Vector2Int.up,Vector2Int.down,Vector2Int.left,Vector2Int.right}.Any(d=>!room.FloorSet.Contains(room.BedHeadTile+d)));
                    Assert.IsNotNull(Pathfinding.FindPath(room.DoorInside,room.BedTile,(x,y)=>room.FloorSet.Contains(new Vector2Int(x,y))));
                }
            }
        }
        [Test] public void Blocked_goals_are_rejected_and_circle_corners_are_walkable()
        {
            Func<int,int,bool> walk=(x,y)=>x>=0&&y>=0&&x<5&&y<5&&(x!=2||y!=2);
            Assert.IsNull(Pathfinding.FindPath(Vector2Int.zero,new Vector2Int(2,2),walk));
            Assert.IsTrue(TileMovement.CanStand(new Vector2(1.8f,1.8f),walk,.25f));
            Assert.IsFalse(TileMovement.CanStand(new Vector2(1.9f,1.9f),walk,.25f));
        }
        [Test] public void Three_hundred_routes_across_twenty_seeds_do_not_stall()
        {
            var cfg=ConfigLoader.Load();var rng=new System.Random(101);
            for(int seed=1;seed<=20;seed++)
            {
                var map=new HotelMap(cfg.map,10,seed);
                Func<int,int,bool> walk=(x,y)=>{var t=map.Get(x,y);return t==Tile.Corridor||t==Tile.RoomFloor||t==Tile.Door;};
                var walls=WallGraph.Build(map);
                // Independent oracle: cache every footprint within .5 of a tile. Both radii
                // are smaller than .5, so an omitted footprint cannot violate clearance.
                var candidates=new System.Collections.Generic.List<Rect>[map.W,map.H];
                foreach(var rect in walls.CollisionFootprints)
                    for(int x=Mathf.Max(0,Mathf.FloorToInt(rect.xMin-.5f));x<=Mathf.Min(map.W-1,Mathf.FloorToInt(rect.xMax+.5f));x++)
                        for(int y=Mathf.Max(0,Mathf.FloorToInt(rect.yMin-.5f));y<=Mathf.Min(map.H-1,Mathf.FloorToInt(rect.yMax+.5f));y++)
                        {
                            if(candidates[x,y]==null)candidates[x,y]=new System.Collections.Generic.List<Rect>();
                            candidates[x,y].Add(rect);
                        }
                for(int route=0;route<15;route++)
                {
                    float radius=route%2==0?GameManager.ResidentRadius:GameManager.MonsterRadius;
                    string context=$"visible wall clearance seed {seed} route {route}";
                    var a=map.Rooms[rng.Next(10)].BedTile; var b=map.Rooms[rng.Next(10)].BedTile;
                    var p=HotelMap.Center(a);var nav=new Navigator();var sample=p;float sampleAt=0,now=0;
                    while(Vector2.Distance(p,HotelMap.Center(b))>.15f && now<100)
                    {
                        for(int frame=0;frame<8;frame++)
                        {
                            const float dt=1f/30;now+=dt;
                            var move=nav.Steer(ref p,b,walk,radius,dt,now,walls);
                            p=TileMovement.Slide(p,move*5*dt,walk,radius,walls);
                            Assert.IsTrue(TileMovement.CanStand(p,walk,radius,walls));
                            var tile=HotelMap.ToTile(p);Assert.IsTrue(walk(tile.x,tile.y),"center entered a wall/void cell");
                            float nearest=float.MaxValue;
                            var nearby=candidates[tile.x,tile.y];
                            if(nearby!=null)foreach(var rect in nearby)
                            {
                                var face=new Vector2(Mathf.Clamp(p.x,rect.xMin,rect.xMax),Mathf.Clamp(p.y,rect.yMin,rect.yMax));
                                nearest=Mathf.Min(nearest,(face-p).sqrMagnitude);
                            }
                            Assert.GreaterOrEqual(nearest,radius*radius-.00002f,context);
                        }
                        if(now-sampleAt>=1 && Vector2.Distance(p,HotelMap.Center(b))>.15f)
                        { Assert.Greater(Vector2.Distance(p,sample),.1f,$"stalled seed {seed} route {route}");sample=p;sampleAt=now; }
                    }
                    Assert.Less(now,100,$"unreachable seed {seed} route {route}");
                }
            }
        }
        [Test] public void Popup_candidates_avoid_hud_and_stay_inside_all_aspects()
        {
            foreach(float aspect in new[]{16f/9,19.5f/9,4f/3})
            {
                var safe=new Rect(28,12,720*aspect-56,696);
                var blocks=new[]{new Rect(10,48,300,90),new Rect(720*aspect-270,8,260,154),new Rect(0,560,180,160)};
                foreach(var p in new[]{safe.min,safe.max,new Vector2(safe.xMin,safe.yMax),new Vector2(safe.xMax,safe.yMin)})
                {
                    var r=HudLayout.Popup(p,new Vector2(330,222),safe,blocks);
                    Assert.GreaterOrEqual(r.xMin,safe.xMin);Assert.LessOrEqual(r.xMax,safe.xMax+.01f);
                    Assert.GreaterOrEqual(r.yMin,safe.yMin);Assert.LessOrEqual(r.yMax,safe.yMax+.01f);
                }
            }
        }
        [Test] public void Sleep_walks_to_bed_and_closes_only_after_doorway_is_clear()
        {
            var gm=new GameObject().AddComponent<GameManager>();
            try
            {
                gm.StartSimulation(ConfigLoader.Load(),77,false);
                var guest=gm.Residents.First(r=>!r.IsMonster);guest.IsHuman=true;
                var def=gm.Map.Rooms[0];Assert.IsTrue(gm.Claim(guest,def,false));
                guest.Pos=HotelMap.Center(def.DoorInside)+(Vector2)(def.DoorTile-def.DoorInside)*0.45f;
                Assert.AreEqual(ActionResult.Ok,gm.TrySleep(guest));Assert.IsFalse(guest.Asleep);
                gm.StepMatch(1f/30);Assert.IsTrue(guest.Room.DoorOpen,"The door closed on its occupant");
                gm.StepMatch(1f/30);Assert.IsTrue(guest.Room.DoorOpen,"The guest circle still overlaps the doorway edge");
                for(int step=0;step<900&&!guest.Asleep;step++)gm.StepMatch(1f/30);
                Assert.IsTrue(guest.Asleep);Assert.IsFalse(guest.Room.DoorOpen);
                Assert.AreEqual(HotelMap.Center(def.BedTile),guest.Pos);
            }
            finally { gm.DisposeSimulation(); }
        }
        [Test] public void A_dead_guests_door_cannot_trap_a_phased_monster()
        {
            var gm=new GameObject().AddComponent<GameManager>();
            try
            {
                gm.StartSimulation(ConfigLoader.Load(),88,false);
                while(gm.Phase==Phase.Setup)gm.StepMatch(1f/30);
                var victim=gm.Residents.First();victim.Room.DoorOpen=false;
                gm.Monster.Pos=victim.Pos;gm.Monster.PhasedRoom=victim.Room;gm.Monster.PhaseUntil=gm.Now+4;
                typeof(GameManager).GetMethod("KillResident",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(gm,new object[]{victim,true});
                Assert.IsTrue(victim.Room.DoorOpen);
                Assert.IsNotNull(Pathfinding.FindPath(HotelMap.ToTile(gm.Monster.Pos),gm.Monster.Lair.Def.BedTile,gm.MonsterWalkable));
                gm.Monster.Fear=100000;
                while(gm.TryBuyLevel(gm.Monster)==ActionResult.Ok){}
                // Area attack, the signature special at level 3 and two utility picks (levels 6 and 12).
                Assert.AreEqual(4,gm.Monster.Loadout.Length);
            }
            finally { gm.DisposeSimulation(); }
        }
        [Test] public void Account_progress_exceeds_fifty_without_integer_overflow()
        {
            var cfg=ConfigLoader.Load();AccountProgress.Calculate(cfg.economy.levelCurve.monster,1e15,out int level,out double into,out double need);
            Assert.Greater(level,50);Assert.GreaterOrEqual(into,0);Assert.Less(into,need);Assert.Greater(need,int.MaxValue);
        }
        [Test] public void Ten_matches_keep_identity_secret_and_reveal_in_the_lair()
        {
            bool art=Sprites.UseArt;
            try
            {
                for(int seed=1;seed<=10;seed++)
                {
                    var gm=new GameObject().AddComponent<GameManager>();
                    try
                    {
                        gm.StartSimulation(ConfigLoader.Load(),seed,false);
                        Assert.AreEqual(7,gm.Residents.Count);Assert.AreEqual(7,gm.Residents.Select(r=>r.Char.id).Distinct().Count());
                        Assert.IsNull(gm.Monster);
                        Resident hidden=gm.HiddenMonster; Vector2 last=hidden.Pos;
                        while(gm.Phase==Phase.Setup)
                        {
                            Assert.IsNull(gm.Monster);
                            foreach(var def in gm.Cfg.monsters.monsters) Assert.IsFalse(gm.Log.Any(l=>l.Contains(def.name)));
                            last=hidden.Pos;gm.StepMatch(1f/30);
                        }
                        Assert.Less(Vector2.Distance(last,gm.Monster.Pos),.3f);
                        Assert.AreSame(hidden.Room,gm.Monster.Lair);Assert.AreEqual(6,gm.Residents.Count);
                        Assert.AreEqual(3,gm.Map.Rooms.Count(gm.IsRoomFree));
                        Assert.IsTrue(hidden.Room.Slots.All(t=>t==null));Assert.IsTrue(hidden.Room.DoorOpen);
                        Assert.AreEqual(0,gm.Monster.Kills);
                        gm.Monster.Fear=100000;
                        while(gm.TryBuyLevel(gm.Monster)==ActionResult.Ok){}
                        Assert.AreEqual(gm.Cfg.progression.maxLevel,gm.Monster.Level);
                        Assert.AreEqual(gm.Cfg.progression.maxLevel-1,gm.Monster.StatRanks.Sum(),"Every bought level gives one stat rank.");
                    }
                    finally { gm.DisposeSimulation(); }
                }
            }
            finally { Sprites.UseArt=art; }
        }
    }
}
