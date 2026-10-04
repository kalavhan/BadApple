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
        public void Door_upgrade_allowed_when_gap_stays_within_four()
        {
            // door 4 -> 5, lowest weapon 1: gap 4, allowed
            var r = UpgradeRules.CanUpgradeDoor(cfg.doors, 4, new[] { 3, 1 });
            Assert.IsTrue(r.Allowed);
        }

        [Test]
        public void Door_upgrade_blocked_at_gap_five_and_lowest_weapon_blinks()
        {
            // door 5 -> 6, lowest weapon 1: gap 5, blocked, weapon index 1 blinks
            var r = UpgradeRules.CanUpgradeDoor(cfg.doors, 5, new[] { 3, 1 });
            Assert.IsFalse(r.Allowed);
            Assert.AreEqual(1, r.BlinkWeaponIndex);
        }

        [Test]
        public void Door_with_no_weapons_caps_at_level_four()
        {
            Assert.IsTrue(UpgradeRules.CanUpgradeDoor(cfg.doors, 3, new int[0]).Allowed);   // -> 4
            Assert.IsFalse(UpgradeRules.CanUpgradeDoor(cfg.doors, 4, new int[0]).Allowed);  // -> 5
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
            Assert.AreEqual(200, EconomyRules.XpForLevel(cfg.economy.levelCurve.monster, 1));
            Assert.AreEqual(250, EconomyRules.XpForLevel(cfg.economy.levelCurve.monster, 2));
        }
    }
}
