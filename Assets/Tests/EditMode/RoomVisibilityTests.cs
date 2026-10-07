using System.Reflection;
using BadAppleHotel.Config;
using BadAppleHotel.Game;
using NUnit.Framework;
using UnityEngine;

namespace BadAppleHotel.Tests
{
    public class RoomVisibilityTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        GameManager game;
        Resident player;
        Random.State randomState;
        float timeScale;

        [SetUp] public void SetUp()
        {
            randomState = Random.state; timeScale = Time.timeScale;
            game = new GameObject("Room visibility test").AddComponent<GameManager>();
            game.StartSimulation(ConfigLoader.Load(), 241, false);
            player = game.Human; player.IsHuman = true; player.Ai = null;
        }

        [TearDown] public void TearDown()
        {
            if (game != null) game.DisposeSimulation();
            Random.state = randomState; Time.timeScale = timeScale;
        }

        [Test] public void Lonely_room_bonus_never_hands_out_a_crystal_ball()
        {
            var def = game.Map.Rooms[0]; def.Isolated = false;
            Assert.IsTrue(game.Claim(player, def, true));
            var bonus = typeof(GameManager).GetMethod("GiveLonelyBonus", Private);
            for (int seed = 0; seed < 200; seed++)
            {
                var room = player.Room;
                for (int i = 0; i < room.Slots.Length; i++) room.Slots[i] = null;
                Random.InitState(seed);
                bonus.Invoke(game, new object[] { room });
                Assert.IsFalse(room.HasClairvoyance(), "seed " + seed);
            }
            Assert.IsTrue(game.FogActive);
        }

        [Test] public void A_claimed_room_stays_in_view_after_its_guest_walks_away()
        {
            var own = game.Map.Rooms[0]; own.Isolated = false;
            Assert.IsTrue(game.Claim(player, own, true));
            typeof(GameManager).GetMethod("CreateFog", Private).Invoke(game, null);
            typeof(GameManager).GetProperty("Simulation").SetValue(game, false);
            Vector2Int far = own.DoorOutside; float distance = 0;
            foreach (var tile in game.Map.CorridorTiles())
            {
                float d = Vector2.Distance(tile, own.DoorOutside);
                if (d > distance) { distance = d; far = tile; }
            }
            Assert.Greater(distance, game.SightRadius + 2);
            player.Pos = HotelMap.Center(far);
            typeof(GameManager).GetField("nextVisionUpdate", Private).SetValue(game, 0f);
            typeof(GameManager).GetMethod("UpdateVision", Private).Invoke(game, null);
            Assert.IsTrue(game.FogActive);
            foreach (var tile in own.Floor) Assert.IsTrue(game.IsTileVisible(tile), "Own room tile " + tile);
            Assert.IsTrue(game.IsTileVisible(own.DoorTile));
            Assert.IsFalse(game.IsTileVisible(own.DoorOutside), "Only the room itself is revealed, not its hallway.");
        }

        [Test] public void A_guest_always_sees_the_monster_inside_their_own_room()
        {
            var own = game.Map.Rooms[0]; own.Isolated = false;
            Assert.IsTrue(game.Claim(player, own, true));
            typeof(GameManager).GetMethod("BeginNights", Private).Invoke(game, null);
            game.Monster.Ai = null;
            // Standing in a different room, the guest has no line of sight to their own.
            var other = game.Map.Rooms[1];
            player.Pos = HotelMap.Center(other.Floor[0]);
            game.Monster.Pos = HotelMap.Center(own.BedTile);
            Assert.IsTrue(game.IsVisible(game.Monster.Pos));
            game.Monster.Pos = HotelMap.Center(other.DoorOutside) + Vector2.one * 30f;
            Assert.IsFalse(game.IsVisible(game.Monster.Pos));
        }
    }
}
