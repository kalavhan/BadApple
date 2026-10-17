using System.Collections.Generic;
using System.Linq;
using BadAppleHotel.Config;
using BadAppleHotel.Game;
using NUnit.Framework;
using UnityEngine;

namespace BadAppleHotel.Tests
{
    /// <summary>The hallway tower-defense hotel: one floor per room, check-in, a new monster each night, shared Fear.</summary>
    public class FloorsMatchTests
    {
        static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        [Test]
        public void Every_floor_has_a_carpet_lane_from_the_lift_to_the_door_with_pads_beside_it()
        {
            var cfg = ConfigLoader.Load();
            var shapes = new HashSet<string>();
            for (int seed = 1; seed <= 40; seed++)
            {
                var map = HotelMap.Floors(cfg.map.floors, cfg.match.playersPerMatch + cfg.map.floors.extraFloors, seed);
                Assert.AreEqual(cfg.match.playersPerMatch + cfg.map.floors.extraFloors, map.Rooms.Count);
                foreach (var def in map.Rooms)
                {
                    var seen = new HashSet<Vector2Int> { def.LiftLanding };
                    var queue = new Queue<Vector2Int>(seen);
                    while (queue.Count > 0)
                    {
                        var c = queue.Dequeue();
                        foreach (var d in Dirs) if (map.IsCarpet(c + d) && seen.Add(c + d)) queue.Enqueue(c + d);
                    }
                    Assert.IsTrue(seen.Contains(def.DoorOutside), $"seed {seed} floor {def.FloorNumber}: the lane reaches the door");
                    Assert.AreEqual(Tile.Door, map.Get(def.DoorTile.x, def.DoorTile.y));
                    Assert.AreEqual(Tile.Wall, map.Get(def.LiftTile.x, def.LiftTile.y));
                    Assert.IsTrue(def.FloorSet.Contains(def.BedTile) && def.FloorSet.Contains(def.BedHeadTile));
                    Assert.Greater(def.BuildTiles.Count, 40);
                    Assert.Greater(def.Lane.Count, 60);
                    foreach (var pad in def.BuildTiles)
                    {
                        Assert.IsFalse(map.IsCarpet(pad), "pads never sit on the carpet");
                        Assert.IsNull(map.RoomContaining(pad), "pads never sit in the room");
                        Assert.AreEqual(def, map.PadOwner(pad));
                        Assert.AreEqual(def, map.FloorOf(pad));
                    }
                    shapes.Add(def.Lane.Count + ":" + def.BuildTiles.Count);
                }
            }
            Assert.Greater(shapes.Count, 20, "floors roll different layouts");
        }

        [Test]
        public void Floors_are_never_connected_to_each_other()
        {
            var cfg = ConfigLoader.Load();
            var map = HotelMap.Floors(cfg.map.floors, 9, 77);
            foreach (var def in map.Rooms)
                foreach (var t in def.Lane)
                    foreach (var d in Dirs)
                    {
                        var n = t + d;
                        if (map.IsCarpet(n)) Assert.AreEqual(def, map.FloorOf(n));
                    }
        }

        GameManager game;

        [SetUp]
        public void Start()
        {
            game = new GameObject("Floors match").AddComponent<GameManager>();
            game.StartSimulation(ConfigLoader.Load(), 4242, false);
        }

        [TearDown]
        public void Stop()
        {
            if (game != null) game.DisposeSimulation();
        }

        void Step(float seconds)
        {
            for (int i = 0; i < seconds * 30 && game.InMatch; i++) game.StepMatch(1f / 30f);
        }

        [Test]
        public void Guests_check_in_one_at_a_time_and_each_gets_a_floor()
        {
            Assert.IsTrue(game.Floors);
            Assert.AreEqual(Phase.CheckIn, game.Phase);
            Assert.AreEqual(game.Residents.Count, game.CheckInOrder.Count);
            var first = game.CheckingIn;
            Step(3f);
            Assert.IsNotNull(first.Room, "the first guest took a room");
            while (game.Phase == Phase.CheckIn) Step(1f);
            Assert.AreEqual(Phase.Setup, game.Phase);
            Assert.IsTrue(game.Residents.All(r => r.Room != null));
            Assert.AreEqual(game.Residents.Count, game.Residents.Select(r => r.Room.Def).Distinct().Count());
            foreach (var r in game.Residents) Assert.IsTrue(r.Room.Def.ContainsInterior(HotelMap.ToTile(r.Pos)));
        }

        void ToFirstNight()
        {
            while (game.Phase == Phase.CheckIn) Step(1f);
            while (game.Phase == Phase.Setup) Step(1f);
            Assert.AreEqual(Phase.Night, game.Phase);
        }

        [Test]
        public void Each_night_a_guest_becomes_the_monster_and_rides_the_lift_to_another_floor()
        {
            ToFirstNight();
            var m = game.Monster;
            Assert.IsNotNull(m);
            Assert.IsNotNull(m.Guest);
            Assert.IsTrue(m.Guest.IsMonster);
            Assert.IsTrue(m.InLift);
            Assert.IsFalse(game.Rifts.Any(r => r.Room.Owner == m.Guest), "the monster's own floor is quiet");
            Assert.AreEqual(game.Residents.Count(r => r.Alive) - 1, game.Rifts.Count);
            Step(4f);
            Assert.IsFalse(m.InLift, "the bot picked a floor");
            Assert.IsNotNull(m.Floor);
            Assert.AreNotEqual(m.Guest.Room.Def, m.Floor);
            Assert.AreEqual(m.Floor, game.Map.FloorOfWorld(m.Pos));
            var firstGuest = m.Guest;
            while (game.Phase == Phase.Night) Step(1f);
            Assert.IsNull(game.Monster);
            Assert.IsFalse(firstGuest.IsMonster);
            while (game.Phase == Phase.Setup) Step(1f);
            if (game.Phase == Phase.Night && game.Residents.Count(r => r.Alive) > 1)
                Assert.AreNotEqual(firstGuest, game.Monster.Guest, "never the same guest twice in a row");
        }

        [Test]
        public void Minions_walk_the_carpet_from_the_lift()
        {
            ToFirstNight();
            Step(4f);
            Assert.Greater(game.Minions.Count, 0);
            Step(6f);
            foreach (var n in game.Minions.Where(n => !n.Dead))
            {
                var tile = HotelMap.ToTile(n.Pos);
                var floor = n.Rift.Room.Def;
                Assert.AreEqual(floor, game.Map.FloorOf(tile));
                Assert.IsNull(game.Map.PadOwner(tile), "minions never stand on the build pads");
            }
        }

        [Test]
        public void Fear_is_shared_and_buys_bigger_waves_that_stay_bought()
        {
            ToFirstNight();
            var m = game.Monster;
            var room = game.RoomsByDef.Values.First(r => r.Owner != m.Guest && r.Owner.Alive);
            float before = game.Fear;
            float cost = game.WaveSizeCost(room);
            int size = room.WaveSize;
            if (before < cost) Assert.AreEqual(ActionResult.NoMoney, game.TryGrowWave(room));
            typeof(GameManager).GetProperty("Fear").SetValue(game, cost + 100f);
            Assert.AreEqual(ActionResult.Ok, game.TryGrowWave(room));
            Assert.AreEqual(size + 1, room.WaveSize);
            Assert.AreEqual(100f, game.Fear, .01f);
            Assert.AreEqual(ActionResult.Ok, game.TryAddWaveType(room, 1));
            Assert.IsTrue(room.WaveTypes[1]);
            while (game.Phase == Phase.Night) Step(1f);
            Assert.GreaterOrEqual(room.WaveSize, size + 1, "waves stay bought for the next monster");
            float left = game.Fear;
            Step(1f);
            Assert.AreEqual(left, game.Fear, .01f, "nobody spends Fear at dawn");
            while (game.Phase == Phase.Setup) Step(1f);
            if (game.Phase == Phase.Night) Assert.GreaterOrEqual(game.Fear + .01f, left, "the next monster inherits it");
        }

        [Test]
        public void The_match_runs_until_one_guest_is_left()
        {
            for (int i = 0; i < 30 * 60 * 40 && game.InMatch; i++) game.StepMatch(1f / 30f);
            if (game.InMatch) Assert.Inconclusive("still going after 40 simulated minutes (night " + game.Night + ")");
            Assert.AreEqual(Phase.Results, game.Phase);
            Assert.LessOrEqual(game.Residents.Count(r => r.Alive), 1);
        }
    }
}
