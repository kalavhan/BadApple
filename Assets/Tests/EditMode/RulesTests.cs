using System.IO;
using BadAppleHotel.Config;
using BadAppleHotel.Rules;
using NUnit.Framework;
using UnityEngine;

namespace BadAppleHotel.Tests
{
    public class RulesTests
    {
        GameConfig cfg;

        [SetUp]
        public void Load()
        {
            string dir = Path.Combine(Application.dataPath, "StreamingAssets", "Config");
            cfg = ConfigLoader.LoadFromJson(name => File.ReadAllText(Path.Combine(dir, name + ".json")));
        }

        [Test]
        public void Config_loads_and_validates()
        {
            Assert.AreEqual(7, cfg.match.playersPerMatch);
            Assert.AreEqual(6, cfg.match.nightCount);
            Assert.AreEqual(90f, cfg.match.nightSeconds);
        }

        [Test]
        public void Doors_upgrade_regardless_of_weapon_levels()
        {
            Assert.IsTrue(UpgradeRules.CanUpgradeDoor(cfg.doors, 5, new[] { 3, 1 }).Allowed);
            Assert.IsTrue(UpgradeRules.CanUpgradeDoor(cfg.doors, 8, new int[0]).Allowed);
        }

        [Test]
        public void A_configured_gap_still_blocks_and_blinks_the_lowest_weapon()
        {
            int gap = cfg.doors.maxLevelGapOverLowestWeapon;
            cfg.doors.maxLevelGapOverLowestWeapon = 4;
            try
            {
                Assert.IsTrue(UpgradeRules.CanUpgradeDoor(cfg.doors, 4, new[] { 3, 1 }).Allowed);   // 4 -> 5, gap 4
                var r = UpgradeRules.CanUpgradeDoor(cfg.doors, 5, new[] { 3, 1 });                // 5 -> 6, gap 5
                Assert.IsFalse(r.Allowed);
                Assert.AreEqual(1, r.BlinkWeaponIndex);
            }
            finally { cfg.doors.maxLevelGapOverLowestWeapon = gap; }
        }

        [Test]
        public void Max_level_door_cannot_upgrade()
        {
            Assert.IsTrue(UpgradeRules.CanUpgradeDoor(cfg.doors, 10, new[] { 10 }).AtMaxLevel);
        }

        [Test]
        public void Monster_kill_takes_90_percent_and_monster_death_pays_120_percent()
        {
            var kill = EconomyRules.MonsterKillReward(cfg.economy, 100f, 50f, 200f);
            Assert.AreEqual(90f, kill.DreamPower, 0.001f);
            Assert.AreEqual(45f, kill.Faith, 0.001f);
            Assert.AreEqual(180f, kill.Xp, 0.001f);

            var death = EconomyRules.MonsterDeathReward(cfg.economy, 100f, 50f);
            Assert.AreEqual(120f, death.DreamPower, 0.001f);
            Assert.AreEqual(60f, death.Faith, 0.001f);
        }

        [Test]
        public void Resident_xp_includes_all_nights_bonus()
        {
            Assert.AreEqual(5 * 60, EconomyRules.ResidentAccountXp(cfg.economy, 5, 6));
            Assert.AreEqual(6 * 60 + 150, EconomyRules.ResidentAccountXp(cfg.economy, 6, 6));
        }

        [Test]
        public void Level_curve_starts_at_base_and_grows()
        {
            Assert.AreEqual(60, EconomyRules.XpForLevel(cfg.economy.levelCurve.monster, 1));
            Assert.AreEqual(69, EconomyRules.XpForLevel(cfg.economy.levelCurve.monster, 2));
        }
    }
}
