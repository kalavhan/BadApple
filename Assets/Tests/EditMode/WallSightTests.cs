using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BadAppleHotel.Config;
using BadAppleHotel.Game;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BadAppleHotel.Tests
{
    public class WallSightTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        HotelMap map;
        RoomDef room;

        [SetUp]
        public void Layout()
        {
            var cfg = ConfigLoader.Load();
            map = new HotelMap(cfg.map, 10, 17);
            room = SetLayout(map);
        }

        static RoomDef SetLayout(HotelMap target)
        {
            for (int x = 0; x < target.W; x++) for (int y = 0; y < target.H; y++) target.Tiles[x, y] = Tile.Void;
            target.Rooms.Clear();
            var result = new RoomDef { Index = 0, DoorTile = new Vector2Int(10, 9), DoorInside = new Vector2Int(10, 10), DoorOutside = new Vector2Int(10, 8) };
            for (int x = 9; x <= 17; x++) for (int y = 9; y <= 15; y++) target.Tiles[x, y] = Tile.Wall;
            for (int x = 10; x <= 16; x++) for (int y = 10; y <= 14; y++)
            {
                var cell = new Vector2Int(x, y);
                result.Floor.Add(cell); result.FloorSet.Add(cell); result.BuildTiles.Add(cell);
                target.Tiles[x, y] = Tile.RoomFloor;
            }
            for (int x = 8; x <= 18; x++) for (int y = 6; y <= 8; y++) target.Tiles[x, y] = Tile.Corridor;
            for (int y = 9; y <= 16; y++) target.Tiles[8, y] = target.Tiles[18, y] = Tile.Corridor;
            for (int x = 8; x <= 18; x++) target.Tiles[x, 16] = Tile.Corridor;
            target.Tiles[10, 9] = Tile.Door;
            target.Rooms.Add(result);
            // The fixture changes public static layout data; keep its door index consistent
            // so the real GameManager closed-door opacity is also exercised below.
            var doors = (Dictionary<Vector2Int, RoomDef>)typeof(HotelMap).GetField("roomByDoor", Private).GetValue(target);
            doors.Clear(); doors.Add(result.DoorTile, result);
            return result;
        }

        Func<int, int, bool> Opaque(bool closed = true) => (x, y) =>
            map.Get(x, y) == Tile.Wall || map.Get(x, y) == Tile.Void || (closed && map.Get(x, y) == Tile.Door);

        [Test]
        public void Window_is_three_local_cells_and_does_not_turn_corners_or_cross_doors()
        {
            var peek = WallSight.FindPeek(map, new Vector2(13.5f, 8.5f));
            Assert.AreSame(room, peek.Room);
            Assert.AreEqual(new Vector2Int(13, 9), peek.CenterCell);
            Assert.AreEqual(Vector2Int.down, peek.Normal);
            CollectionAssert.AreEquivalent(new[] { new Vector2Int(12, 9), new Vector2Int(13, 9), new Vector2Int(14, 9) }, peek.Cells);
            Assert.AreEqual(new Rect(12, 9, 3, 1), peek.Bounds);
            Assert.IsNull(WallSight.FindPeek(map, new Vector2(13.5f, 7.99f)), "The next corridor row is not adjacent.");
            Assert.IsNull(WallSight.FindPeek(map, new Vector2(9.5f, 8.5f)), "A diagonal room corner is not an opening.");
            Assert.IsNull(WallSight.FindPeek(map, new Vector2(13.5f, 10.5f)), "Entering the room ends its wall peek.");
            map.Tiles[12, 9] = Tile.Door;
            peek = WallSight.FindPeek(map, new Vector2(13.5f, 8.5f));
            Assert.AreEqual(2, peek.Cells.Count);
            Assert.IsFalse(peek.ContainsCell(12, 9));
            map.Tiles[14, 10] = Tile.Void;
            Assert.AreEqual(1, WallSight.FindPeek(map, new Vector2(13.5f, 8.5f)).Cells.Count);
        }

        [Test]
        public void Sight_passes_only_the_local_opening_and_preserves_range_other_walls_and_same_room_sight()
        {
            var monster = new Vector2(13.5f, 8.5f);
            var near = new Vector2(13.5f, 10.5f);
            var peek = WallSight.FindPeek(map, monster);
            Assert.IsTrue(WallSight.CanSee(map, monster, near, 10, Opaque(), peek, true, false));
            Assert.IsTrue(WallSight.CanSee(map, near, monster, 10, Opaque(), peek, false, true));
            Assert.IsFalse(Sight.Clear(monster, near, Opaque()), "The attack/movement wall remains opaque.");
            Assert.IsFalse(WallSight.CanSee(map, monster, near, 1.9f, Opaque(), peek, true, false));
            Assert.IsFalse(WallSight.CanSee(map, monster, new Vector2(16.5f, 10.5f), 10, Opaque(), peek, true, false), "The same room's remote wall remains closed.");
            Assert.IsFalse(WallSight.CanSee(map, new Vector2(16.5f, 10.5f), monster, 10, Opaque(), peek, false, true));
            map.Tiles[13, 11] = Tile.Wall;
            Assert.IsFalse(WallSight.CanSee(map, monster, new Vector2(13.5f, 12.5f), 10, Opaque(), peek, true, false));
            Assert.IsTrue(WallSight.CanSee(map, new Vector2(14.5f, 10.5f), new Vector2(15.5f, 12.5f), 10, Opaque(), null, true, false));
        }

        [Test]
        public void Monster_room_crossing_requires_adjacency_even_through_an_open_door()
        {
            var inside = HotelMap.Center(room.DoorInside);
            var local = HotelMap.Center(room.DoorOutside);
            var remote = local + Vector2.down * 2;
            Assert.IsTrue(Sight.Clear(remote, inside, Opaque(false)), "Control ray passes the open doorway.");
            Assert.IsFalse(WallSight.CanSee(map, remote, inside, 10, Opaque(false), null, true, false));
            Assert.IsFalse(WallSight.CanSee(map, inside, remote, 10, Opaque(false), null, false, true));
            Assert.IsTrue(WallSight.CanSee(map, local, inside, 10, Opaque(false), null, true, false));
            Assert.IsTrue(WallSight.CanSee(map, inside, local, 10, Opaque(false), null, false, true));
            Assert.IsFalse(WallSight.CanSee(map, local, inside, 10, Opaque(), null, true, false));
            Assert.IsTrue(WallSight.CanSee(map, remote, inside, 10, Opaque(false), null, false, false), "Ordinary guests keep normal open-door sight.");
            var other = new RoomDef { Index = 1 };
            other.FloorSet.Add(new Vector2Int(13, 4)); other.Floor.Add(new Vector2Int(13, 4));
            map.Rooms.Add(other); map.Tiles[13, 4] = Tile.RoomFloor;
            var monster = new Vector2(13.5f, 8.5f);
            Assert.IsFalse(WallSight.CanSee(map, monster, new Vector2(13.5f, 4.5f), 10, (x, y) => false, WallSight.FindPeek(map, monster), true, false),
                "An opening for one room never grants the monster sight into a different room.");
        }

        [Test]
        public void Generated_windows_remain_straight_and_attached_to_one_room_across_twenty_hotels()
        {
            var config = ConfigLoader.Load(); int windows = 0;
            for (int seed = 1; seed <= 20; seed++)
            {
                var hotel = new HotelMap(config.map, 10, seed);
                for (int x = 0; x < hotel.W; x++) for (int y = 0; y < hotel.H; y++)
                {
                    if (hotel.Get(x, y) != Tile.Corridor) continue;
                    var position = new Vector2(x + .5f, y + .5f);
                    var peek = WallSight.FindPeek(hotel, position);
                    if (peek == null) continue;
                    windows++;
                    Assert.That(peek.Cells.Count, Is.InRange(1, 3));
                    Assert.IsTrue(peek.IsAdjacent(position));
                    Assert.IsTrue(peek.ContainsCell(peek.CenterCell.x, peek.CenterCell.y));
                    foreach (var cell in peek.Cells)
                    {
                        Assert.AreEqual(Tile.Wall, hotel.Get(cell.x, cell.y));
                        Assert.IsTrue(peek.Room.ContainsInterior(cell - peek.Normal));
                        Assert.AreEqual(0, (cell.x - peek.CenterCell.x) * peek.Normal.x + (cell.y - peek.CenterCell.y) * peek.Normal.y);
                        Assert.LessOrEqual((cell - peek.CenterCell).sqrMagnitude, 1);
                    }
                }
            }
            Assert.Greater(windows, 100);
        }

        [Test]
        public void Fog_and_monster_sprite_follow_peek_policy_including_clairvoyance_and_room_entry()
        {
            var config = ConfigLoader.Load(); var random = UnityEngine.Random.state; var art = Sprites.ArtOverride;
            var go = new GameObject("Peek vision integration"); var game = go.AddComponent<GameManager>();
            try
            {
                game.StartSimulation(config, 17, false);
                var def = SetLayout(game.Map);
                game.RoomsByDef.Clear(); var occupied = new Room(def) { Owner = game.Human, DoorOpen = false };
                game.RoomsByDef.Add(def, occupied); game.Human.Room = occupied; game.Human.Pos = new Vector2(13.5f, 10.5f);
                var monsterGo = new GameObject("Peek test monster"); monsterGo.transform.SetParent(go.transform);
                var monster = new Monster { Pos = new Vector2(13.5f, 8.5f), Sr = monsterGo.AddComponent<SpriteRenderer>() };
                typeof(GameManager).GetProperty("Monster").SetValue(game, monster);
                typeof(GameManager).GetMethod("CreateFog", Private).Invoke(game, null);
                typeof(GameManager).GetProperty("Simulation").SetValue(game, false);
                Refresh(game);
                Assert.IsNotNull(game.ActiveWallPeek);
                Assert.IsTrue(game.CanSee(game.Human.Pos, monster.Pos, 10));
                Assert.IsFalse(game.ClearLine(game.Human.Pos, monster.Pos), "Peeking must not permit wall attacks.");
                Assert.IsTrue(game.IsVisible(monster.Pos)); Assert.IsTrue(monster.Sr.enabled);
                Assert.IsTrue(game.IsTileVisible(new Vector2Int(13, 8)));

                monster.Pos += Vector2.down;
                Refresh(game);
                Assert.IsNull(game.ActiveWallPeek);
                Assert.IsFalse(game.CanSee(game.Human.Pos, monster.Pos, 10));
                Assert.IsFalse(game.IsVisible(monster.Pos)); Assert.IsFalse(monster.Sr.enabled);
                occupied.Slots[0] = new TowerInstance { Def = new TowerDef { effect = "clairvoyance" } };
                Refresh(game);
                Assert.IsFalse(game.FogActive);
                Assert.IsFalse(game.IsVisible(monster.Pos), "Clairvoyance reveals layout, not a remote monster through a room wall.");
                Assert.IsFalse(monster.Sr.enabled);
                game.Human.Alive = false; Refresh(game);
                Assert.IsTrue(monster.Sr.enabled, "Dead spectators retain full visibility.");
                monster.Dead = true; Refresh(game);
                Assert.IsFalse(monster.Sr.enabled, "A spectator must not resurrect the monster sprite during respawn.");
                monster.Dead = false;

                game.Human.Alive = true; occupied.Slots[0] = null;
                monster.Pos = new Vector2(14.5f, 10.5f); Refresh(game);
                Assert.IsNull(game.ActiveWallPeek);
                Assert.IsTrue(game.CanSee(game.Human.Pos, monster.Pos, 10));
                Assert.IsTrue(monster.Sr.enabled, "A monster already in the room uses ordinary same-room sight.");

                typeof(GameManager).GetProperty("HumanRole").SetValue(game, Role.Monster);
                monster.Pos = new Vector2(13.5f, 8.5f); Refresh(game);
                Assert.IsTrue(game.IsTileVisible(new Vector2Int(13, 10)));
                Assert.IsFalse(game.IsTileVisible(new Vector2Int(16, 10)), "Fog opens only the three-cell window.");
                monster.Pos += Vector2.down; Refresh(game);
                Assert.IsFalse(game.IsTileVisible(new Vector2Int(13, 10)));
                monster.Pos = HotelMap.Center(def.DoorOutside); Refresh(game);
                Assert.IsTrue(game.IsTileVisible(def.DoorTile), "A visible closed door face must retain its HUD marker.");
                Assert.IsFalse(game.IsTileVisible(def.DoorInside), "Seeing the door face must not reveal its interior floor.");
                Assert.IsFalse(game.CanSee(monster.Pos, HotelMap.Center(def.DoorInside), 10));
            }
            finally
            {
                game.DisposeSimulation(); UnityEngine.Random.state = random; Sprites.ArtOverride = art;
            }
        }

        static void Refresh(GameManager game)
        {
            typeof(GameManager).GetField("nextVisionUpdate", Private).SetValue(game, 0f);
            typeof(GameManager).GetMethod("UpdateVision", Private).Invoke(game, null);
        }
    }
}
