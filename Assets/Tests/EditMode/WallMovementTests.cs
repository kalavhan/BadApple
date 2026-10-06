using System;
using BadAppleHotel.Config;
using BadAppleHotel.Game;
using NUnit.Framework;
using UnityEngine;

namespace BadAppleHotel.Tests
{
    public class WallMovementTests
    {
        [Test] public void One_tile_door_openings_fit_both_radii_but_closed_doors_block_them()
        {
            var config = ConfigLoader.Load();
            for (int seed = 1; seed <= 20; seed++)
            {
                var map = new HotelMap(config.map, 10, seed);
                var graph = WallGraph.Build(map);
                bool Floor(int x, int y) => map.Get(x, y) == Tile.RoomFloor || map.Get(x, y) == Tile.Corridor || map.Get(x, y) == Tile.Door;
                foreach (var room in map.Rooms)
                    foreach (float radius in new[] { GameManager.ResidentRadius, GameManager.MonsterRadius })
                    {
                        var inside = HotelMap.Center(room.DoorInside);
                        var outside = HotelMap.Center(room.DoorOutside);
                        Assert.IsTrue(TileMovement.Clear(outside, inside, Floor, radius, graph), $"door {room.Index} seed {seed}");
                        var across = new Vector2(-(inside - outside).y, (inside - outside).x).normalized;
                        // The door retains its complete one-tile opening between the visible posts.
                        foreach (float sign in new[] { -1f, 1f })
                        {
                            var offset = across * sign * (.5f - radius - .001f);
                            Assert.IsTrue(TileMovement.Clear(outside + offset, inside + offset, Floor, radius, graph));
                        }
                        bool Shut(int x, int y) => Floor(x, y) && new Vector2Int(x, y) != room.DoorTile;
                        Assert.IsFalse(TileMovement.Clear(outside, inside, Shut, radius, graph));
                        var stopped = TileMovement.Slide(outside, inside - outside, Shut, radius, graph);
                        Assert.IsTrue(TileMovement.CanStand(stopped, Shut, radius, graph));
                        Assert.GreaterOrEqual(Vector2.Distance(stopped, HotelMap.Center(room.DoorTile)), .5f + radius - .001f);
                    }
            }
        }

        [Test] public void A_large_dash_cannot_cross_the_visible_wall_or_a_tower()
        {
            var config = ConfigLoader.Load();
            var map = new HotelMap(config.map, 10, 914);
            var graph = WallGraph.Build(map);
            bool Floor(int x, int y) => map.Get(x, y) == Tile.RoomFloor || map.Get(x, y) == Tile.Corridor || map.Get(x, y) == Tile.Door;
            foreach (var edge in graph.Edges)
            {
                var midpoint = ((Vector2)edge.A + (Vector2)edge.B) * .5f;
                var start = midpoint + (Vector2)edge.Normal * .5f;
                var stopped = TileMovement.Slide(start, -(Vector2)edge.Normal * 12f, Floor, GameManager.MonsterRadius, graph);
                Assert.IsTrue(TileMovement.CanStand(stopped, Floor, GameManager.MonsterRadius, graph));
                Assert.GreaterOrEqual(Vector2.Dot(stopped - midpoint, edge.Normal), -.001f,"Dash crossed the logical separator.");
                foreach(var rect in graph.CollisionFootprints)
                {
                    var nearest=new Vector2(Mathf.Clamp(stopped.x,rect.xMin,rect.xMax),Mathf.Clamp(stopped.y,rect.yMin,rect.yMax));
                    Assert.GreaterOrEqual(Vector2.Distance(stopped,nearest),GameManager.MonsterRadius-.001f,"Dash entered the visible wall.");
                }
            }
            var target = map.Rooms[0].DoorInside;
            var from = HotelMap.Center(map.Rooms[0].DoorTile);
            bool Tower(int x, int y) => Floor(x, y) && new Vector2Int(x, y) != target;
            var result = TileMovement.Slide(from, (HotelMap.Center(target) - from) * 8, Tower, GameManager.MonsterRadius, graph);
            Assert.IsTrue(TileMovement.CanStand(result, Tower, GameManager.MonsterRadius, graph));
            Assert.IsFalse(TileMovement.Clear(from, HotelMap.Center(target), Tower, GameManager.MonsterRadius, graph));
        }
    }
}
