using System.Collections.Generic;
using System.IO;
using BadAppleHotel.Config;
using BadAppleHotel.Game;
using BadAppleHotel.Rules;
using NUnit.Framework;
using UnityEngine;

namespace BadAppleHotel.Tests
{
    public class MapTests
    {
        GameConfig cfg;

        [SetUp]
        public void Load()
        {
            string dir = Path.Combine(Application.dataPath, "StreamingAssets", "Config");
            cfg = ConfigLoader.LoadFromJson(name => File.ReadAllText(Path.Combine(dir, name + ".json")));
        }

        static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        [Test]
        public void Every_letter_shape_is_connected()
        {
            foreach (var letter in cfg.map.letters)
            {
                var b = LetterShapes.Get(letter);
                Assert.IsNotNull(b, "unknown letter " + letter);
                var cells = new HashSet<Vector2Int>();
                for (int x = 0; x < b.GetLength(0); x++)
                    for (int y = 0; y < b.GetLength(1); y++)
                        if (b[x, y]) cells.Add(new Vector2Int(x, y));
                Assert.AreEqual(cells.Count, Flood(cells, First(cells)).Count, "letter " + letter + " is not 4-connected");
            }
        }

        [Test]
        public void Generated_hotels_are_playable_across_many_seeds()
        {
            for (int seed = 1; seed <= 60; seed++)
            {
                var map = new HotelMap(cfg.map, cfg.match.roomCount, seed);
                Assert.AreEqual(cfg.match.roomCount, map.Rooms.Count, "seed " + seed);
                Assert.AreEqual(Tile.Corridor, map.Get(map.MonsterSpawn.x, map.MonsterSpawn.y), "spawn seed " + seed);
                Assert.AreEqual(Tile.Corridor, map.Get(map.Lobby.x, map.Lobby.y), "lobby seed " + seed);

                var owner = new Dictionary<Vector2Int, int>();
                foreach (var room in map.Rooms)
                {
                    string where = "seed " + seed + " room " + (room.Index + 1) + " (" + room.Letter + ")";
                    Assert.AreEqual(Tile.Door, map.Get(room.DoorTile.x, room.DoorTile.y), where + " door");
                    Assert.AreEqual(Tile.Corridor, map.Get(room.DoorOutside.x, room.DoorOutside.y), where + " door outside");
                    Assert.IsTrue(room.FloorSet.Contains(room.DoorInside), where + " door inside");
                    Assert.IsTrue(room.FloorSet.Contains(room.BedTile), where + " bed");
                    Assert.GreaterOrEqual(room.BuildTiles.Count, 2, where + " build tiles");
                    Assert.LessOrEqual(room.BuildTiles.Count, cfg.map.buildTilesMax, where + " build tiles");

                    foreach (var f in room.Floor)
                    {
                        Assert.AreEqual(Tile.RoomFloor, map.Get(f.x, f.y), where + " floor tile");
                        Assert.IsFalse(owner.ContainsKey(f), where + " overlaps room " + (owner.ContainsKey(f) ? owner[f] + 1 : 0));
                        owner[f] = room.Index;
                    }

                    // bed reachable from the door even with every build tile occupied
                    var open = new HashSet<Vector2Int>(room.Floor);
                    foreach (var b in room.BuildTiles)
                    {
                        Assert.IsFalse(room.Walkway.Contains(b), where + " build tile on walkway");
                        Assert.AreNotEqual(room.BedTile, b, where + " build tile on bed");
                        open.Remove(b);
                    }
                    Assert.IsTrue(Flood(open, room.DoorInside).Contains(room.BedTile), where + " bed blocked by buildings");
                }
            }
        }

        [Test]
        public void Some_rooms_are_lonely_and_some_have_neighbours()
        {
            int lonely = 0, social = 0;
            for (int seed = 1; seed <= 30; seed++)
            {
                var map = new HotelMap(cfg.map, cfg.match.roomCount, seed);
                foreach (var r in map.Rooms) if (r.Isolated) lonely++; else social++;
            }
            Assert.Greater(lonely, 0);
            Assert.Greater(social, 0);
        }

        [Test]
        public void Same_seed_gives_same_hotel()
        {
            var a = new HotelMap(cfg.map, cfg.match.roomCount, 1234);
            var b = new HotelMap(cfg.map, cfg.match.roomCount, 1234);
            for (int i = 0; i < a.Rooms.Count; i++)
            {
                Assert.AreEqual(a.Rooms[i].DoorTile, b.Rooms[i].DoorTile);
                Assert.AreEqual(a.Rooms[i].Floor.Count, b.Rooms[i].Floor.Count);
            }
        }

        [Test]
        public void Tower_ranges_come_from_range_class()
        {
            var gun = System.Array.Find(cfg.towers.towers, t => t.id == "gun_turret");
            var dragon = System.Array.Find(cfg.towers.towers, t => t.id == "dragon_statue");
            var missile = System.Array.Find(cfg.towers.towers, t => t.id == "missile_launcher");
            Assert.AreEqual(cfg.towers.rangeTiles.mid, UpgradeRules.TowerRange(cfg.towers, gun, 1), 0.001f);
            Assert.AreEqual(cfg.towers.rangeTiles.@short, UpgradeRules.TowerRange(cfg.towers, dragon, 1), 0.001f);
            Assert.AreEqual(cfg.towers.rangeTiles.@long, UpgradeRules.TowerRange(cfg.towers, missile, 1), 0.001f);
            Assert.Greater(UpgradeRules.TowerRange(cfg.towers, gun, 5), UpgradeRules.TowerRange(cfg.towers, gun, 1));
        }

        [Test]
        public void Selling_refunds_half_of_everything_spent()
        {
            var gun = System.Array.Find(cfg.towers.towers, t => t.id == "gun_turret");
            float spent = gun.buildCost + UpgradeRules.TowerUpgradeCost(cfg.towers, gun, 1) + UpgradeRules.TowerUpgradeCost(cfg.towers, gun, 2);
            Assert.AreEqual(Mathf.Floor(spent * cfg.towers.sellRefundPct), UpgradeRules.TowerSellValue(cfg.towers, gun, 3), 0.001f);
        }

        [Test]
        public void Crystal_ball_cannot_be_upgraded()
        {
            var ball = System.Array.Find(cfg.towers.towers, t => t.id == "crystal_ball");
            Assert.IsNotNull(ball);
            Assert.AreEqual("clairvoyance", ball.effect);
            Assert.AreEqual(1, UpgradeRules.TowerMaxLevel(cfg.towers, ball));
        }

        static Vector2Int First(HashSet<Vector2Int> set)
        {
            foreach (var v in set) return v;
            return default;
        }

        static HashSet<Vector2Int> Flood(HashSet<Vector2Int> cells, Vector2Int start)
        {
            var seen = new HashSet<Vector2Int>();
            if (!cells.Contains(start)) return seen;
            var q = new Queue<Vector2Int>();
            q.Enqueue(start);
            seen.Add(start);
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                foreach (var d in Dirs)
                {
                    var n = c + d;
                    if (cells.Contains(n) && seen.Add(n)) q.Enqueue(n);
                }
            }
            return seen;
        }
    }
}
