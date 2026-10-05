using System.Linq;
using System.Reflection;
using BadAppleHotel.Config;
using BadAppleHotel.Game;
using NUnit.Framework;
using UnityEngine;

namespace BadAppleHotel.Tests
{
    public class ResidentControlTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        GameManager game;
        Resident player;
        Random.State randomState;
        float timeScale;

        [SetUp] public void SetUp()
        {
            randomState = Random.state; timeScale = Time.timeScale;
            GameInput.ClearAll();
            game = new GameObject("Resident controls test").AddComponent<GameManager>();
            var config = ConfigLoader.Load();
            // Keep damage measurements independent of level-up health growth.
            config.progression.aliveXpPerSecond = config.progression.biteDamageXp = config.progression.doorDamageXp = 0;
            game.StartSimulation(config, 241, false);
            player = game.Human; player.IsHuman = true; player.Ai = null;
            var room = game.Map.Rooms[0]; room.Isolated = false;
            Assert.IsTrue(game.Claim(player, room, true));
            Invoke("BeginNights");
            foreach (var resident in game.Residents)
            {
                resident.Ai = null;
                if (resident != player) resident.Alive = false;
            }
            game.Monster.Ai = null;
            game.Monster.Pos = HotelMap.Center(player.Room.Def.BedHeadTile);
        }

        [TearDown] public void TearDown()
        {
            GameInput.ClearAll();
            if (game != null) game.DisposeSimulation();
            Random.state = randomState; Time.timeScale = timeScale;
        }

        void Invoke(string name, params object[] arguments) => typeof(GameManager).GetMethod(name, Private).Invoke(game, arguments);

        [Test] public void Human_stays_asleep_through_door_damage_and_bites_until_the_wake_action()
        {
            Assert.AreEqual(ActionResult.Ok, game.TrySleep(player));
            var bed = player.Pos;
            game.Monster.Pos = HotelMap.Center(player.Room.Def.DoorOutside);
            player.Room.DoorOpen = false;
            float doorHp = player.Room.DoorHp;
            game.StepMatch(.1f);
            Assert.Less(player.Room.DoorHp, doorHp, "The closed door must actually be under attack.");
            Assert.IsTrue(player.Asleep);
            Assert.AreEqual(bed, player.Pos);

            game.Monster.Pos = HotelMap.Center(player.Room.Def.BedHeadTile);
            float health = player.Health;
            game.StepMatch(.1f);
            Assert.Less(player.Health, health, "A monster inside the room must still damage a sleeping player.");
            Assert.IsTrue(player.Alive);
            Assert.IsTrue(player.Asleep);
            Assert.AreEqual(bed, player.Pos);
            Assert.AreEqual(ActionResult.Blocked, game.TryResidentShoot(player));
            Assert.IsTrue(player.Asleep, "Shoot must not implicitly wake the player.");
            Assert.AreEqual(ActionResult.Ok, game.DoAction(player));
            Assert.IsFalse(player.Asleep);
        }

        [Test] public void Personal_damage_requires_a_new_explicit_shot_after_each_cooldown()
        {
            float hp = game.Monster.Hp;
            game.StepMatch(.1f);
            Assert.AreEqual(hp, game.Monster.Hp, "A nearby monster must not trigger an automatic player shot.");
            Assert.AreEqual(0f, player.AttackUntil);
            float damage = game.Cfg.residents.personalShotDamage * game.DamageTaken(game.Monster, DamageTypes.Bullet);
            Assert.AreEqual(ActionResult.Ok, game.TryResidentShoot(player));
            Assert.AreEqual(hp - damage, game.Monster.Hp, .0001f);
            Assert.Greater(player.AttackUntil, game.Now);
            Assert.AreSame(player, game.Monster.LastDamager);
            Assert.AreEqual(ActionResult.Blocked, game.TryResidentShoot(player));
            game.StepMatch(game.Cfg.residents.personalShotCooldownSeconds - .01f);
            Assert.AreEqual(ActionResult.Blocked, game.TryResidentShoot(player));
            game.StepMatch(.02f);
            Assert.AreEqual(hp - damage, game.Monster.Hp, .0001f, "A finished cooldown must not fire a queued or automatic shot.");
            Assert.AreEqual(ActionResult.Ok, game.TryResidentShoot(player));
            Assert.AreEqual(hp - damage * 2, game.Monster.Hp, .0001f);
        }

        [Test] public void Personal_shots_require_an_awake_player_and_an_uncloaked_target_in_clear_range()
        {
            float hp = game.Monster.Hp;
            game.TrySleep(player);
            Assert.AreEqual(ActionResult.Blocked, game.TryResidentShoot(player));
            game.DoAction(player);
            player.Pos = HotelMap.Center(player.Room.Def.DoorInside);
            game.Monster.Pos = HotelMap.Center(player.Room.Def.DoorOutside);
            player.Room.DoorOpen = false;
            Assert.AreEqual(ActionResult.TooFar, game.TryResidentShoot(player));
            player.Room.DoorOpen = true;
            Assert.AreEqual(ActionResult.Ok, game.ResidentShotAvailability(player));
            game.Monster.CloakUntil = game.Now + 10;
            Assert.AreEqual(ActionResult.TooFar, game.TryResidentShoot(player));
            game.Monster.CloakUntil = 0;
            game.Monster.Pos = player.Pos + Vector2.right * (game.Cfg.residents.personalShotRangeTiles + 1);
            Assert.AreEqual(ActionResult.TooFar, game.TryResidentShoot(player));
            Assert.AreEqual(hp, game.Monster.Hp);
            Assert.AreEqual(0, player.NextPersonalShotAt, "Rejected requests must not consume a shot.");
        }

        [Test] public void Towers_fire_automatically_without_making_the_human_attack()
        {
            player.DreamPower = 10000;
            int slot = Enumerable.Range(0, player.Room.Slots.Length).First(i => game.CanBuildAt(player.Room, i));
            Assert.AreEqual(ActionResult.Ok, game.TryBuildTower(player, slot, "gun_turret"));
            var tower = player.Room.Slots[slot];
            game.Monster.Pos = HotelMap.Center(tower.Tile) + Vector2.right;
            float hp = game.Monster.Hp;
            Vector2 facing = player.Facing;
            Invoke("UpdateTowers", .1f, game.Now);
            Assert.Less(game.Monster.Hp, hp);
            Assert.Greater(tower.Cooldown, 0);
            Assert.AreEqual(0, player.AttackUntil);
            Assert.AreEqual(0, player.NextPersonalShotAt);
            Assert.AreEqual(facing, player.Facing);
        }

        [Test] public void A_visible_monster_at_a_wall_peek_cannot_be_shot_through_the_solid_wall()
        {
            var outside = game.Map.CorridorTiles().First(tile => WallSight.FindPeek(game.Map, HotelMap.Center(tile)) != null);
            game.Monster.Pos = HotelMap.Center(outside);
            var peek = game.ActiveWallPeek;
            Assert.IsNotNull(peek);
            player.Pos = HotelMap.Center(peek.CenterCell - peek.Normal);
            Assert.IsTrue(game.CanSee(player.Pos, game.Monster.Pos, game.Cfg.residents.personalShotRangeTiles),
                "This must exercise an actually visible target through the local wall opening.");
            Assert.IsFalse(game.ClearLine(player.Pos, game.Monster.Pos));
            float hp = game.Monster.Hp;
            Assert.AreEqual(ActionResult.TooFar, game.TryResidentShoot(player));
            Assert.AreEqual(hp, game.Monster.Hp);
            Assert.AreEqual(0, player.AttackUntil);
            Assert.AreEqual(0, player.NextPersonalShotAt, "A view-only window must not spend the player's shot.");
        }

        [Test] public void F_key_fires_once_per_press_and_does_not_repeat_when_held()
        {
            var hud = game.gameObject.AddComponent<GameHUD>();
            typeof(GameHUD).GetField("gm", Private).SetValue(hud, game);
            var keys = typeof(GameHUD).GetMethod("HandleKeys", Private);
            float hp = game.Monster.Hp;
            GameInput.Handle(new Event { type = EventType.KeyDown, keyCode = KeyCode.F });
            keys.Invoke(hud, null);
            Assert.Less(game.Monster.Hp, hp);
            float afterShot = game.Monster.Hp;
            game.StepMatch(game.Cfg.residents.personalShotCooldownSeconds + .01f);
            GameInput.Handle(new Event { type = EventType.KeyDown, keyCode = KeyCode.F });
            keys.Invoke(hud, null);
            Assert.AreEqual(afterShot, game.Monster.Hp, "OS key repeat must not cause autofire.");
            GameInput.Handle(new Event { type = EventType.KeyUp, keyCode = KeyCode.F });
            GameInput.Handle(new Event { type = EventType.KeyDown, keyCode = KeyCode.F });
            keys.Invoke(hud, null);
            Assert.Less(game.Monster.Hp, afterShot);
        }
    }
}
