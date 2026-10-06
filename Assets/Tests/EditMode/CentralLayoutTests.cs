using System.Collections.Generic;
using System.Linq;
using BadAppleHotel.Config;
using BadAppleHotel.Game;
using NUnit.Framework;
using UnityEngine;

namespace BadAppleHotel.Tests
{
    public class CentralLayoutTests
    {
        [Test]
        public void Hundred_layouts_have_four_central_islands_single_partitions_and_rare_shared_rooms()
        {
            var cfg=ConfigLoader.Load(); int joinedMaps=0,closeMaps=0;
            for(int seed=1;seed<=100;seed++)
            {
                var map=new HotelMap(cfg.map,10,seed);
                Assert.AreEqual(4,map.Rooms.Count(r=>r.IsCentral),"central count seed "+seed);
                foreach(var room in map.Rooms.Where(r=>r.IsCentral))
                {
                    Assert.That(room.Center.x,Is.InRange(map.W*.25f,map.W*.75f));
                    Assert.That(room.Center.y,Is.InRange(map.H*.2f,map.H*.8f));
                    foreach(var direction in new[]{Vector2Int.up,Vector2Int.down,Vector2Int.left,Vector2Int.right})
                    {
                        // Every exposed extremal face has a wall then a corridor, with no second slab.
                        int extreme=room.Floor.Max(p=>(p.x*direction.x+p.y*direction.y));
                        foreach(var floor in room.Floor.Where(p=>(p.x*direction.x+p.y*direction.y)==extreme))
                        {
                            var outside=floor+direction*2;
                            Assert.AreEqual(Tile.Corridor,map.Get(outside.x,outside.y),"central ring seed "+seed+" room "+room.Index+" at "+outside);
                        }
                    }
                }
                foreach(var room in map.Rooms)foreach(var floor in room.Floor)
                    for(int dx=-1;dx<=1;dx++)for(int dy=-1;dy<=1;dy++)
                        Assert.AreNotEqual(Tile.Corridor,map.Get(floor.x+dx,floor.y+dy),"Corridor consumed a room corner, seed "+seed);
                var joined=new HashSet<string>();
                for(int y=1;y<map.H-1;y++)for(int x=1;x<map.W-1;x++)
                    foreach(var d in new[]{Vector2Int.right,Vector2Int.up})
                    {
                        var a=new Vector2Int(x,y);var b=a+d;
                        if(map.Get(x,y)==Tile.Wall&&map.Get(b.x,b.y)==Tile.Wall)
                        {
                            var before=a-d;var after=b+d;
                            bool across=WallGraph.IsWalkable(map.Get(before.x,before.y))&&WallGraph.IsWalkable(map.Get(after.x,after.y));
                            var tangent=new Vector2Int(-d.y,d.x);
                            foreach(int side in new[]{-1,1})
                            {
                                var offset=tangent*side;var c=a+offset;var e=b+offset;
                                var entry=before+offset;var exit=after+offset;
                                // A one-cell turn/end cap is a legitimate corner of one wall.
                                // Parallel runs touching over multiple cells are double walls.
                                bool parallel=map.Get(c.x,c.y)==Tile.Wall&&map.Get(e.x,e.y)==Tile.Wall&&
                                    WallGraph.IsWalkable(map.Get(entry.x,entry.y))&&WallGraph.IsWalkable(map.Get(exit.x,exit.y));
                                Assert.IsFalse(across&&parallel,"double wall runs seed "+seed+" at "+a+" axis "+d);
                            }
                        }
                        var r1=map.RoomContaining(a-d);var r2=map.RoomContaining(a+d);
                        if(r1!=null&&r2!=null&&r1!=r2)
                        {
                            Assert.AreEqual(Tile.Wall,map.Get(x,y));
                            Assert.IsFalse(r1.IsCentral||r2.IsCentral);
                            joined.Add(Mathf.Min(r1.Index,r2.Index)+":"+Mathf.Max(r1.Index,r2.Index));
                        }
                    }
                Assert.LessOrEqual(joined.Count,1,"Only one peripheral pair can share a wall.");
                if(joined.Count>0)joinedMaps++;
                if(map.Rooms.Any(r=>!r.Isolated))closeMaps++;
            }
            Assert.That(joinedMaps,Is.InRange(1,20),"Rare joined rooms across 100 seeds");
            Assert.Greater(closeMaps,50,"Most hotels should offer nearby rooms as well as isolated rooms.");
            Debug.Log("CENTRAL LAYOUT: joined maps="+joinedMaps+"/100, maps with nearby doors="+closeMaps+"/100");
        }
    }
}
