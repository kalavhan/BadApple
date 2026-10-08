using System.Collections.Generic;
using System.IO;
using System.Linq;
using BadAppleHotel.Config;
using BadAppleHotel.Game;
using BadAppleHotel.Rules;
using NUnit.Framework;
using UnityEngine;

namespace BadAppleHotel.Tests
{
    public class PlanTests
    {
        GameConfig cfg;
        static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        [SetUp]
        public void Load() => cfg = ConfigLoader.LoadFromJson(n => File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Config", n + ".json")));

        [Test]
        public void Ten_rooms_all_reachable_without_going_through_another_room()
        {
            Assert.AreEqual(10, cfg.match.roomCount);
            Assert.AreEqual(4, cfg.match.roomCount - cfg.match.residentCount);
            for (int seed = 1; seed <= 60; seed++)
            {
                var map = new HotelMap(cfg.map, cfg.match.roomCount, seed);
                var reached = new HashSet<Vector2Int> { map.Lobby };
                var q = new Queue<Vector2Int>(); q.Enqueue(map.Lobby);
                while (q.Count > 0)
                {
                    var p = q.Dequeue();
                    foreach (var dir in Dirs)
                    {
                        var n = p + dir;
                        if (map.Get(n.x, n.y) == Tile.Corridor && reached.Add(n)) q.Enqueue(n);
                    }
                }
                Assert.IsTrue(reached.Contains(map.MonsterSpawn), "monster seed " + seed);
                foreach (var room in map.Rooms)
                {
                    Assert.IsTrue(reached.Contains(room.DoorOutside), "door seed " + seed);
                    Assert.That(room.NearestDoorDistance, Is.InRange(cfg.map.minDoorDistance, cfg.map.maxNearestDoorDistance));
                    // Unclaimed rooms begin with an open, nonblocking door and no owner or equipment.
                    var empty = new Room(room);
                    Assert.IsNull(empty.Owner); Assert.IsTrue(empty.DoorOpen); Assert.IsFalse(empty.DoorBlocks);
                    Assert.IsTrue(empty.Slots.All(t => t == null));
                }
                Assert.Greater(map.DeadEnds.Count, 0, "dead ends seed " + seed);
                Assert.AreEqual(map.CorridorTiles().Count, reached.Count, "disconnected corridor seed " + seed);
            }
        }

        [Test]
        public void Three_seeds_have_distinct_bent_spines_and_room_spacing()
        {
            var signatures = new HashSet<string>();
            foreach (int seed in new[] { 19, 2026, 7717 })
            {
                var map = new HotelMap(cfg.map, 10, seed);
                signatures.Add(string.Join(";", map.Rooms.Select(r => r.DoorTile.ToString())));
                Assert.Greater(map.Spine.Select(p => p.y).Distinct().Count(), 2);
                Assert.Greater(map.Rooms.Max(r => r.NearestDoorDistance) - map.Rooms.Min(r => r.NearestDoorDistance), 4);
            }
            Assert.AreEqual(3, signatures.Count);
        }

        [Test]
        public void Body_parts_keep_twelve_tiles_between_every_pair_for_every_night()
        {
            for (int seed = 1; seed <= 35; seed++)
            {
                var map = new HotelMap(cfg.map, 10, seed);
                for (int night = 1; night <= cfg.match.nightCount; night++)
                {
                    var empty = map.Rooms.Skip(6).ToArray();
                    var parts = map.BodyPartSpawns(cfg.match.bodyPartsPerNight, cfg.bodyParts.minSpacingTiles, seed + night * 7919, empty);
                    Assert.AreEqual(cfg.match.bodyPartsPerNight, parts.Count);
                    Assert.IsTrue(parts.Any(p => map.DeadEnds.Contains(p)), "at least one dead-end hiding place");
                    Assert.IsTrue(parts.Any(p => map.RoomContaining(p) != null), "at least one empty-room hiding place");
                    for (int i = 0; i < parts.Count; i++)
                    {
                        var room = map.RoomContaining(parts[i]);
                        Assert.IsTrue(room == null || empty.Contains(room));
                        for (int j = i + 1; j < parts.Count; j++)
                            Assert.GreaterOrEqual(Vector2Int.Distance(parts[i], parts[j]), cfg.bodyParts.minSpacingTiles);
                    }
                    CollectionAssert.AreEqual(parts, map.BodyPartSpawns(parts.Count, cfg.bodyParts.minSpacingTiles, seed + night * 7919, empty));
                }
            }
        }

        [Test]
        public void All_character_heads_meet_all_bed_anchors_in_four_orientations()
        {
            Assert.AreEqual(7, cfg.residents.roster.Length);
            foreach (var person in cfg.residents.roster)
            {
                var character = CharacterSet.Load(person.art, 1.55f);
                Assert.IsNotNull(character, person.id);
                foreach (var bed in cfg.beds.levels)
                    foreach (float angle in new[] { 0f, 90f, 180f, 270f })
                    {
                        var center = new Vector2(8.5f, 11.5f);
                        var position = SleepPose.Position(center, angle, bed, character.RestHead);
                        var head = position + (Vector2)(Quaternion.Euler(0, 0, angle + bed.sleepRotation) * character.RestHead);
                        var pillow = center + (Vector2)(Quaternion.Euler(0, 0, angle) * bed.sleepAnchor);
                        Assert.Less(Vector2.Distance(head, pillow), 0.0001f, person.id + " bed " + bed.level);
                    }
            }
        }

        [Test]
        public void Camera_clamps_at_all_edges_and_centers_maps_smaller_than_view()
        {
            Assert.AreEqual(new Vector2(15, 7.5f), GameManager.ClampCamera(new Vector2(-999, -999), 7.5f, 2, 88, 56));
            Assert.AreEqual(new Vector2(73, 48.5f), GameManager.ClampCamera(new Vector2(999, 999), 7.5f, 2, 88, 56));
            Assert.AreEqual(new Vector2(5, 4), GameManager.ClampCamera(Vector2.zero, 7.5f, 2, 10, 8));
        }

        [Test]
        public void Upgradeable_families_have_four_distinct_static_designs_and_complete_stats()
        {
            foreach (var tower in cfg.towers.towers.Where(t => t.effect != "clairvoyance"))
            {
                Assert.AreEqual(4, tower.tiers.Length, tower.id);
                Assert.AreEqual(4, tower.tiers.Select(t => t.sprite).Distinct().Count());
                Assert.AreEqual(15, UpgradeRules.TowerMaxLevel(cfg.towers, tower));
                Assert.AreEqual(10, UpgradeRules.DoorSupportLevel(cfg.towers, tower, 15));
                for (int form = 1; form <= 4; form++)
                {
                    // Each form shows its tier's stats exactly at the form's first level.
                    int lv = UpgradeRules.FormStart(cfg.towers, tower, form);
                    var tier = tower.tiers[form - 1];
                    Assert.IsNotNull(Resources.Load<Texture2D>("Art/" + tier.sprite), tier.sprite);
                    Assert.AreEqual(tier.damage, UpgradeRules.Damage(cfg.towers, tower, lv));
                    Assert.AreEqual(tier.range, UpgradeRules.TowerRange(cfg.towers, tower, lv));
                    Assert.AreEqual(tier.faithPerSecond, UpgradeRules.FaithRate(cfg.towers, tower, lv));
                    Assert.AreEqual(tier.dreamPerSecond, UpgradeRules.DreamRate(cfg.towers, tower, lv));
                    if (form < 4) Assert.Greater(tier.upgradeCost, 0);
                }
            }
        }
    }
}
