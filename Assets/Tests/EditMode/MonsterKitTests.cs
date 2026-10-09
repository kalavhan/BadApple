using System.Linq;
using System.Reflection;
using BadAppleHotel.Config;
using BadAppleHotel.Game;
using NUnit.Framework;
using UnityEngine;

namespace BadAppleHotel.Tests
{
    public class MonsterKitTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        GameManager gm;
        Random.State randomState;

        [SetUp] public void SetUp()
        {
            randomState = Random.state;
            gm = new GameObject("Monster kit test").AddComponent<GameManager>();
            gm.StartSimulation(ConfigLoader.Load(), 512, false);
            while (gm.Phase == Phase.Setup) gm.StepMatch(1f / 30);
            foreach (var r in gm.Residents) r.Ai = null;
            gm.Monster.Ai = null;
        }

        [TearDown] public void TearDown()
        {
            if (gm != null) gm.DisposeSimulation();
            Random.state = randomState;
        }

        void Call(string name, params object[] args) => typeof(GameManager).GetMethod(name, Private).Invoke(gm, args);

        /// <summary>Turns the revealed monster into a specific one, with a fresh kit.</summary>
        Monster Become(string id)
        {
            var m = gm.Monster;
            m.Def = gm.Cfg.monsters.monsters.First(d => d.id == id);
            Call("InitializeProgression", m);
            m.Hp = gm.MaxHp(m);
            return m;
        }

        Resident Victim() => gm.Residents.First(r => r.Alive && r.Room != null && r.Room != gm.Monster.Lair);

        [Test] public void Bots_climb_from_novice_on_night_one_to_expert_on_the_last_night_and_over_longer_in_endless()
        {
            Assert.AreEqual("hard", gm.DifficultyId, "Simulations play hard.");
            var s = gm.Difficulty;
            var night = typeof(GameManager).GetProperty(nameof(GameManager.Night));
            var endless = typeof(GameManager).GetProperty(nameof(GameManager.Endless));
            night.SetValue(gm, 1);
            Assert.AreEqual(s.start, gm.BotSkill, .001f);
            night.SetValue(gm, gm.Cfg.match.nightCount);
            Assert.AreEqual(s.end, gm.BotSkill, .001f);
            float last = -1f;
            endless.SetValue(gm, true);
            for (int n = 1; n <= s.endlessRampNights + 3; n++)
            {
                night.SetValue(gm, n);
                Assert.GreaterOrEqual(gm.BotSkill, last);
                last = gm.BotSkill;
            }
            night.SetValue(gm, gm.Cfg.match.nightCount);
            Assert.Less(gm.BotSkill, s.end, "Endless ramps more slowly than a standard match.");
            night.SetValue(gm, s.endlessRampNights);
            Assert.AreEqual(s.end, gm.BotSkill, .001f);
            gm.BotSkillOverride = .5f;
            Assert.AreEqual(.5f, gm.BotSkill);
        }

        [Test] public void Difficulties_get_harder_from_easy_to_extra_hard_and_hard_is_the_old_ramp()
        {
            var d = gm.Cfg.match.difficulties;
            CollectionAssert.AreEqual(new[] { "easy", "normal", "hard", "extraHard" }, d.Select(x => x.id).ToArray());
            for (int i = 1; i < d.Length; i++)
            {
                Assert.GreaterOrEqual(d[i].start, d[i - 1].start);
                Assert.GreaterOrEqual(d[i].end, d[i - 1].end);
            }
            var hard = d.First(x => x.id == "hard");
            Assert.AreEqual(.15f, hard.start, .001f); Assert.AreEqual(1f, hard.end, .001f);
            var night = typeof(GameManager).GetProperty(nameof(GameManager.Night));
            night.SetValue(gm, 3);
            gm.DifficultyId = "easy"; float easy = gm.BotSkill;
            gm.DifficultyId = "extraHard"; float extra = gm.BotSkill;
            Assert.Less(easy, extra);
        }

        [Test] public void Level_prices_climb_by_a_fixed_step_and_the_whole_climb_costs_about_1400_fear()
        {
            var p = gm.Cfg.progression;
            Assert.AreEqual(20f, GameManager.LevelPrice(p, 1));
            Assert.AreEqual(38f, GameManager.LevelPrice(p, 4), "Level 5 costs 38 Fear.");
            Assert.AreEqual(-1f, GameManager.LevelPrice(p, p.maxLevel));
            float total = 0;
            for (int level = 1; level < p.maxLevel; level++) total += GameManager.LevelPrice(p, level);
            Assert.AreEqual(1406f, total);
        }

        [Test] public void Levels_cost_fear_and_each_one_gives_a_stat_rank()
        {
            var m = gm.Monster;
            m.Fear = 10;
            Assert.AreEqual(ActionResult.NoMoney, gm.TryBuyLevel(m));
            m.Fear = 20;
            float hp = gm.MaxHp(m);
            Assert.AreEqual(ActionResult.Ok, gm.TryBuyLevel(m));
            Assert.AreEqual(2, m.Level);
            Assert.AreEqual(0f, m.Fear, .001f);
            Assert.AreEqual(1, m.StatRanks.Sum());
            Assert.Greater(gm.MaxHp(m), hp - .01f);
        }

        [Test] public void One_tap_on_a_stat_buys_the_level_and_puts_the_point_there()
        {
            var m = gm.Monster;
            m.IsHuman = true;   // a player's picks wait for taps instead of being chosen by the bot
            m.Fear = 1000;
            Assert.AreEqual(ActionResult.Ok, gm.TryBuyLevelInto(m, "maw"));
            Assert.AreEqual(2, m.Level);
            Assert.AreEqual(1, gm.StatRank(m, "maw"));
            Assert.AreEqual(0, m.Choices.Count, "Nothing else is owed at level 2.");
            while (m.Level < 4) gm.TryBuyLevelInto(m, "vitality");
            Assert.AreEqual(2, gm.StatRank(m, "vitality"));
            Assert.IsTrue(gm.HasPendingPick(m), "Level 4 owes an ability rank, picked in the same ring.");
            gm.ChooseProgression(0);
            Assert.IsFalse(gm.HasPendingPick(m));
            m.Fear = 0;
            Assert.AreEqual(ActionResult.NoMoney, gm.TryBuyLevelInto(m, "hide"));
            Assert.AreEqual(4, m.Level);
        }

        [Test] public void Hitting_residents_and_eating_parts_earn_fear_and_idling_earns_a_little()
        {
            var m = gm.Monster;
            var victim = Victim();
            victim.Room.DoorOpen = true;
            m.Pos = victim.Pos + Vector2.right * .5f;
            m.NextAttackAt = 0;
            float before = m.Fear;
            gm.StepMatch(1f / 30);
            Assert.Greater(m.Fear, before, "A hit on a resident earns Fear.");

            // Somewhere quiet: the corridor tile farthest from every resident and door.
            m.Pos = HotelMap.Center(gm.Map.CorridorTiles().OrderByDescending(tile =>
                gm.Residents.Select(r => Vector2.Distance(r.Pos, HotelMap.Center(tile)))
                    .Concat(gm.Map.Rooms.Select(def => Vector2.Distance(HotelMap.Center(def.DoorTile), HotelMap.Center(tile)))).Min()).First());
            m.LastDamageAt = gm.Now - 60;
            m.Frenzy = false;
            before = m.Fear;
            for (int i = 0; i < 30; i++) gm.StepMatch(1f / 30);
            Assert.AreEqual(before + gm.Cfg.progression.fear.idlePerSecond, m.Fear, .2f, "One idle second.");
        }

        [Test] public void Rift_batches_shrink_in_total_but_grow_per_survivor_as_residents_die()
        {
            float exponent = gm.Cfg.minions.aliveExponent;
            Assert.AreEqual(3, GameManager.RiftBatch(3, 6, 6, exponent));
            Assert.AreEqual(6, GameManager.RiftBatch(3, 6, 1, exponent));
            Assert.AreEqual(10, GameManager.RiftBatch(5, 6, 1, exponent), "The last resident faces about 10 a night, not 30.");
            int lastPer = 0, lastTotal = int.MaxValue;
            for (int alive = 6; alive >= 1; alive--)
            {
                int per = GameManager.RiftBatch(4, 6, alive, exponent);
                Assert.GreaterOrEqual(per, lastPer);
                Assert.LessOrEqual(per * alive, lastTotal);
                lastPer = per; lastTotal = per * alive;
            }
        }

        [Test] public void Resistances_cycle_through_one_weakness_each()
        {
            Assert.AreEqual(DamageTypes.Fire, GameManager.Weakness(DamageTypes.Bullet));
            Assert.AreEqual(DamageTypes.Electric, GameManager.Weakness(DamageTypes.Fire));
            Assert.AreEqual(DamageTypes.Bullet, GameManager.Weakness(DamageTypes.Electric));
        }

        [Test] public void Every_monster_has_a_swarm_breachers_and_an_escort_covering_all_three_resistances()
        {
            foreach (var line in gm.Cfg.minions.lines)
            {
                CollectionAssert.AreEqual(new[] { "swarm", "breacher", "escort" }, line.creatures.Select(c => c.role).ToArray(), line.id);
                CollectionAssert.AreEquivalent(new[] { "bullet", "electric", "fire" }, line.creatures.Select(c => c.resist).ToArray(), line.id);
            }
        }

        [Test] public void Horde_strength_climbs_in_price_and_each_evolution_has_its_own_price()
        {
            var c = gm.Cfg.minions;
            Assert.AreEqual(c.strengthCostBase, GameManager.StrengthCost(c, 1));
            Assert.AreEqual(Mathf.Round(c.strengthCostBase * c.strengthCostGrowth), GameManager.StrengthCost(c, 2));
            Assert.AreEqual(-1f, GameManager.StrengthCost(c, c.maxStrength));
            Assert.AreEqual(-1f, GameManager.StrengthCost(c, 0), "Nothing to upgrade before the horde awakens.");
            foreach (var line in c.lines)
                Assert.Greater(line.creatures.Select(x => x.evolveCost).Distinct().Count(), 1, line.id + " prices its evolutions by value, not one rule");
        }

        [Test] public void Horde_strength_carries_to_creatures_unlocked_later_and_evolutions_survive_switching()
        {
            var m = gm.Monster;
            m.Fear = 5000;
            gm.TryUnlockMinion(m, 0);
            gm.TryUpgradeStrength(m); gm.TryUpgradeStrength(m);
            Assert.AreEqual(3, m.HordeStrength);
            gm.TryUnlockMinion(m, 1);
            float expected = gm.Creature(m, 1).health * (1f + 2f * gm.Cfg.minions.healthPerStrength);
            Assert.AreEqual(expected, gm.MinionHealth(m, 1), .01f, "Strength III applies to a creature unlocked afterwards.");
            Assert.AreEqual(ActionResult.Ok, gm.TryEvolveMinion(m, 1));
            Assert.AreEqual(ActionResult.MaxLevel, gm.TryEvolveMinion(m, 1), "An evolution is bought once.");
            m.ActiveMinion = 1; m.QueuedMinion = -1; m.SwitchedNight = -1;
            gm.TryQueueMinion(m, 0);
            Assert.IsTrue(gm.MinionEvolved(m, 1));
            Assert.AreEqual(3, m.HordeStrength);
        }

        [Test] public void Awakening_unlocks_the_swarm_and_opens_a_rift_per_living_resident()
        {
            var m = gm.Monster;
            m.Fear = 1000;
            Assert.AreEqual(ActionResult.Blocked, gm.TryUnlockMinion(m, 1), "The other creatures wait for the awakening.");
            Assert.AreEqual(ActionResult.Ok, gm.TryUnlockMinion(m, 0));
            int living = gm.Residents.Count(r => r.Alive && r.Room != null && r.Room != m.Lair);
            Assert.AreEqual(living, gm.Rifts.Count);
            for (int i = 0; i < 90; i++) gm.StepMatch(1f / 30);
            Assert.Greater(gm.Minions.Count, 0);
            int resist = gm.ResistOf(gm.Creature(m, 0));
            Assert.IsTrue(gm.Minions.All(n => n.Index == 0 && n.Resist == resist));
        }

        [Test] public void Killing_a_resident_closes_their_rift_and_their_minions_crumble()
        {
            var m = gm.Monster;
            m.Fear = 1000;
            gm.TryUnlockMinion(m, 0);
            // No towers, so nothing kills the minions before the resident dies.
            foreach (var room in gm.RoomsByDef.Values) for (int i = 0; i < room.Slots.Length; i++) room.Slots[i] = null;
            for (int i = 0; i < 90; i++) gm.StepMatch(1f / 30);
            var victim = gm.Rifts[0].Room.Owner;
            Assert.IsTrue(gm.Minions.Any(n => n.Rift.Room == victim.Room));
            Call("KillResident", victim, true);
            gm.StepMatch(1f / 30);
            Assert.IsFalse(gm.Rifts.Any(r => r.Room == victim.Room));
            Assert.IsFalse(gm.Minions.Any(n => n.Rift != null && n.Rift.Room == victim.Room));
        }

        [Test] public void A_bought_creature_is_queued_takes_over_at_the_next_pulse_and_then_switching_locks()
        {
            var m = gm.Monster;
            m.Fear = 1000;
            gm.TryUnlockMinion(m, 0);
            for (int i = 0; i < 90; i++) gm.StepMatch(1f / 30);   // past the first pulse
            Assert.AreEqual(ActionResult.Ok, gm.TryUnlockMinion(m, 1));
            Assert.AreEqual(0, m.ActiveMinion, "The current creature stays active until the next pulse.");
            Assert.AreEqual(1, m.QueuedMinion);
            int before = gm.Minions.Count(n => n.Index == 0);
            while (gm.NextPulseIn() > 0f && gm.Phase == Phase.Night) gm.StepMatch(1f / 30);
            gm.StepMatch(1f / 30);
            Assert.AreEqual(1, m.ActiveMinion);
            Assert.AreEqual(-1, m.QueuedMinion);
            Assert.IsTrue(gm.Minions.Any(n => n.Index == 1), "The new creature spawns from that pulse.");
            Assert.AreEqual(ActionResult.Blocked, gm.TryQueueMinion(m, 0), "One switch per night.");
            m.SwitchedNight = gm.Night - 1;   // as if the next night had started
            float fear = m.Fear;
            Assert.AreEqual(ActionResult.Ok, gm.TryQueueMinion(m, 0));
            Assert.AreEqual(0, m.QueuedMinion);
            Assert.AreEqual(fear, m.Fear, "Switching is free.");
        }

        [Test] public void Towers_shoot_minions_and_their_resistance_cuts_the_damage()
        {
            var m = gm.Monster;
            m.Fear = 1000;
            gm.TryUnlockMinion(m, 0);
            for (int i = 0; i < 90; i++) gm.StepMatch(1f / 30);
            var minion = gm.Minions[0];
            var owner = minion.Rift.Room.Owner;
            owner.DreamPower = 10000;
            int slot = Enumerable.Range(0, owner.Room.Slots.Length).First(i => gm.CanBuildAt(owner.Room, i));
            Assert.AreEqual(ActionResult.Ok, gm.TryBuildTower(owner, slot, "gun_turret"));
            var tower = owner.Room.Slots[slot];
            // Only this tower may fire, and only at this minion.
            foreach (var room in gm.RoomsByDef.Values)
                for (int i = 0; i < room.Slots.Length; i++) if (room.Slots[i] != tower) room.Slots[i] = null;
            m.CloakUntil = gm.Now + 100;
            foreach (var n in gm.Minions.ToArray()) if (n != minion) n.Dead = true;
            gm.Minions.RemoveAll(n => n.Dead);
            minion.Pos = HotelMap.Center(tower.Tile) + Vector2.right;

            minion.Resist = DamageTypes.Fire;   // neither resists nor fears bullets
            minion.Hp = minion.MaxHp = 10000;
            float hp = minion.Hp;
            Call("UpdateTowers", .1f, gm.Now);
            float neutral = hp - minion.Hp;
            Assert.Greater(neutral, 0f);

            minion.Resist = DamageTypes.Bullet;
            hp = minion.Hp; tower.Cooldown = 0;
            Call("UpdateTowers", .1f, gm.Now);
            Assert.AreEqual(neutral * (1f - gm.Cfg.minions.resistPct), hp - minion.Hp, .01f);

            minion.Resist = DamageTypes.Electric;   // electric-proof minions are weak to bullets
            hp = minion.Hp; tower.Cooldown = 0;
            Call("UpdateTowers", .1f, gm.Now);
            Assert.AreEqual(neutral * (1f + gm.Cfg.minions.weaknessBonus), hp - minion.Hp, .01f);
        }

        [Test] public void An_escort_beside_the_monster_steps_in_front_of_tower_shots()
        {
            var m = gm.Monster;
            m.Fear = 2000;
            gm.TryUnlockMinion(m, 0);
            gm.TryUnlockMinion(m, 2);
            var victim = Victim();
            victim.DreamPower = 10000;
            int slot = Enumerable.Range(0, victim.Room.Slots.Length).First(i => gm.CanBuildAt(victim.Room, i));
            Assert.AreEqual(ActionResult.Ok, gm.TryBuildTower(victim, slot, "gun_turret"));
            var tower = victim.Room.Slots[slot];
            foreach (var room in gm.RoomsByDef.Values)
                for (int i = 0; i < room.Slots.Length; i++) if (room.Slots[i] != tower) room.Slots[i] = null;
            m.Pos = HotelMap.Center(tower.Tile) + Vector2.right * 2f;
            var escort = (Minion)typeof(GameManager).GetMethod("SpawnMinion", Private).Invoke(gm, new object[] { m, 2, null, m.Pos, false });
            escort.Creature.interceptChance = 1f;
            try
            {
                float monsterHp = m.Hp, escortHp = escort.Hp;
                Call("UpdateTowers", .1f, gm.Now);
                Assert.AreEqual(monsterHp, m.Hp, "The shot never reached the monster.");
                Assert.Less(escort.Hp, escortHp);
            }
            finally { escort.Creature.interceptChance = .5f; }
        }

        [Test] public void Flambe_burns_residents_in_the_ring()
        {
            var m = Become("stitchwork_chef");
            var victim = Victim();
            victim.Room.DoorOpen = true;
            m.Pos = Vector2.MoveTowards(victim.Pos, HotelMap.Center(victim.Room.Def.DoorInside), 1.2f);
            Assume.That(gm.ClearLine(m.Pos, victim.Pos));
            float hp = victim.Health;
            Assert.IsTrue(gm.UseAbility(0));
            Assert.Less(victim.Health, hp);
            Assert.AreEqual(1, gm.Hazards.Count);
            Assert.IsFalse(gm.UseAbility(0), "On cooldown.");
        }

        [Test] public void Do_not_disturb_slips_through_a_shut_door_and_out_again()
        {
            var m = Become("bellhop_wraith");
            m.Fear = 1000;
            while (m.Level < gm.Cfg.progression.specialLevel) gm.TryBuyLevel(m);
            while (m.Choices.Count > 0) gm.ChooseProgression(0);
            var room = Victim().Room;
            room.DoorOpen = false;
            m.Pos = HotelMap.Center(room.Def.DoorOutside);
            Assert.IsTrue(gm.UseAbility(1));
            Assert.IsTrue(room.Def.ContainsInterior(HotelMap.ToTile(m.Pos)));
            Assert.IsTrue(room.DoorBlocks, "The door stays intact.");
            for (int i = 0; i < 5 * 30 && m.PhasedRoom != null; i++) gm.StepMatch(1f / 30);
            if (room.DoorBlocks && room.Owner.Alive) Assert.IsFalse(room.Def.ContainsInterior(HotelMap.ToTile(m.Pos)), "Back out when the phase ends.");
        }

        [Test] public void Meat_hook_eats_a_body_part_from_across_the_hall()
        {
            var m = Become("stitchwork_chef");
            m.Fear = 1000;
            while (m.Level < gm.Cfg.progression.specialLevel) gm.TryBuyLevel(m);
            while (m.Choices.Count > 0) gm.ChooseProgression(0);
            var part = gm.Parts.FirstOrDefault();
            Assume.That(part, Is.Not.Null);
            var spot = gm.Map.CorridorTiles().Where(t => Vector2Int.Distance(t, part.Tile) is var d && d > 3 && d < 6)
                .FirstOrDefault(t => gm.ClearLine(HotelMap.Center(t), HotelMap.Center(part.Tile)));
            Assume.That(spot, Is.Not.EqualTo(default(Vector2Int)));
            m.Pos = HotelMap.Center(spot);
            int eaten = m.Parts.Sum();
            Assert.IsTrue(gm.UseAbility(1));
            Assert.AreEqual(eaten + 1, m.Parts.Sum());
            Assert.IsFalse(gm.Parts.Contains(part));
        }
    }
}
