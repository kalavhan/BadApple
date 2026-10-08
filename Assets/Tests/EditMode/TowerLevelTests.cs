using System.IO;
using System.Linq;
using BadAppleHotel.Config;
using BadAppleHotel.Rules;
using NUnit.Framework;
using UnityEngine;

namespace BadAppleHotel.Tests
{
    /// <summary>Fifteen levels in four forms: level-ups inside a form, Evolve at levels 3, 7 and 11.</summary>
    public class TowerLevelTests
    {
        GameConfig cfg;

        [SetUp]
        public void Load()
        {
            string dir = Path.Combine(Application.dataPath, "StreamingAssets", "Config");
            cfg = ConfigLoader.LoadFromJson(name => File.ReadAllText(Path.Combine(dir, name + ".json")));
        }

        TowerDef Tower(string id) => cfg.towers.towers.First(t => t.id == id);

        [Test]
        public void Levels_map_to_four_forms_and_evolve_unlocks_at_3_7_and_11()
        {
            var gun = Tower("gun_turret");
            int[] forms = { 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4 };
            for (int lv = 1; lv <= 15; lv++)
            {
                Assert.AreEqual(forms[lv - 1], UpgradeRules.Form(cfg.towers, gun, lv), "level " + lv);
                var expected = lv == 15 ? UpgradeRules.Step.Max : lv == 3 || lv == 7 || lv == 11 ? UpgradeRules.Step.Evolve : UpgradeRules.Step.LevelUp;
                Assert.AreEqual(expected, UpgradeRules.NextStep(cfg.towers, gun, lv), "level " + lv);
            }
            Assert.AreEqual("Rifle Sentry", UpgradeRules.TowerName(cfg.towers, gun, 4));
            Assert.AreEqual(11, UpgradeRules.FormEnd(cfg.towers, gun, 3));
            Assert.AreEqual(15, UpgradeRules.FormEnd(cfg.towers, gun, 4));
        }

        [Test]
        public void Stats_climb_every_level_and_evolving_is_the_biggest_jump()
        {
            foreach (var tower in cfg.towers.towers.Where(t => t.tiers != null && t.tiers.Length == 4))
            {
                float Power(int lv) => UpgradeRules.Damage(cfg.towers, tower, lv) * UpgradeRules.FireRate(cfg.towers, tower, lv)
                    + UpgradeRules.BurnDamage(cfg.towers, tower, lv) + UpgradeRules.SlowPct(cfg.towers, tower, lv)
                    + UpgradeRules.FaithRate(cfg.towers, tower, lv) + UpgradeRules.DreamRate(cfg.towers, tower, lv);
                for (int lv = 1; lv < 15; lv++)
                {
                    Assert.GreaterOrEqual(Power(lv + 1), Power(lv), tower.id + " level " + lv);
                    if (UpgradeRules.NextStep(cfg.towers, tower, lv) != UpgradeRules.Step.Evolve || lv < 2) continue;
                    // The evolve gain beats the level-up just before it.
                    Assert.Greater(Power(lv + 1) - Power(lv), Power(lv) - Power(lv - 1), tower.id + " evolve at " + lv);
                }
                Assert.Greater(UpgradeRules.TowerRange(cfg.towers, tower, 15), UpgradeRules.TowerRange(cfg.towers, tower, 12) - .001f);
            }
        }

        [Test]
        public void Reaching_each_form_costs_what_its_tier_upgrade_did_and_evolve_is_the_biggest_step()
        {
            foreach (var tower in cfg.towers.towers.Where(t => t.tiers != null && t.tiers.Length == 4))
                for (int form = 1; form <= 3; form++)
                {
                    int start = UpgradeRules.FormStart(cfg.towers, tower, form), end = UpgradeRules.FormEnd(cfg.towers, tower, form);
                    float total = 0f, biggestLevelUp = 0f;
                    for (int lv = start; lv <= end; lv++)
                    {
                        float cost = UpgradeRules.TowerUpgradeCost(cfg.towers, tower, lv);
                        Assert.Greater(cost, 0, tower.id + " level " + lv);
                        total += cost;
                        if (lv < end) biggestLevelUp = Mathf.Max(biggestLevelUp, cost);
                    }
                    Assert.AreEqual(Mathf.Round(tower.tiers[form - 1].upgradeCost), total, 1f, tower.id + " form " + form);
                    Assert.Greater(UpgradeRules.TowerUpgradeCost(cfg.towers, tower, end), biggestLevelUp, tower.id + " evolve from form " + form);
                }
        }

        [Test]
        public void Without_a_levels_block_each_upgrade_is_one_tier()
        {
            cfg.towers.levels = null;
            var gun = Tower("gun_turret");
            Assert.AreEqual(4, UpgradeRules.TowerMaxLevel(cfg.towers, gun));
            Assert.AreEqual(2, UpgradeRules.Form(cfg.towers, gun, 2));
            Assert.AreEqual(UpgradeRules.Step.Evolve, UpgradeRules.NextStep(cfg.towers, gun, 1));
            Assert.AreEqual(gun.tiers[1].damage, UpgradeRules.Damage(cfg.towers, gun, 2));
            Assert.AreEqual(gun.tiers[0].upgradeCost, UpgradeRules.TowerUpgradeCost(cfg.towers, gun, 1));
        }

        [Test]
        public void The_crystal_ball_still_has_a_single_level()
        {
            var ball = Tower("crystal_ball");
            Assert.AreEqual(1, UpgradeRules.TowerMaxLevel(cfg.towers, ball));
            Assert.AreEqual(UpgradeRules.Step.Max, UpgradeRules.NextStep(cfg.towers, ball, 1));
        }
    }
}
