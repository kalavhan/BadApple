using BadAppleHotel.Config;

namespace BadAppleHotel.Rules
{
    public struct DoorUpgradeResult
    {
        public bool Allowed;
        /// <summary>Index into the weapon list of the lowest-level weapon to blink when blocked, or -1.</summary>
        public int BlinkWeaponIndex;
        /// <summary>Cost of the upgrade when allowed or blocked-by-gap; 0 when already at max level.</summary>
        public float Cost;
        public bool AtMaxLevel;
    }

    public static class UpgradeRules
    {
        /// <summary>
        /// Door upgrade rule: the NEW door level may be at most maxLevelGapOverLowestWeapon levels
        /// above the lowest-level weapon. An empty weapon list counts as level emptyWeaponSlotCountsAsLevel.
        /// </summary>
        public static DoorUpgradeResult CanUpgradeDoor(DoorsConfig doors, int currentDoorLevel, int[] weaponLevels)
        {
            int maxLevel = doors.levels[doors.levels.Length - 1].level;
            if (currentDoorLevel >= maxLevel)
                return new DoorUpgradeResult { Allowed = false, BlinkWeaponIndex = -1, Cost = 0f, AtMaxLevel = true };

            int lowest = doors.emptyWeaponSlotCountsAsLevel;
            int lowestIndex = -1;
            if (weaponLevels != null && weaponLevels.Length > 0)
            {
                lowest = weaponLevels[0];
                lowestIndex = 0;
                for (int i = 1; i < weaponLevels.Length; i++)
                {
                    if (weaponLevels[i] < lowest) { lowest = weaponLevels[i]; lowestIndex = i; }
                }
            }

            int newLevel = currentDoorLevel + 1;
            bool allowed = newLevel - lowest <= doors.maxLevelGapOverLowestWeapon;
            float cost = doors.levels[currentDoorLevel - 1].upgradeCost; // cost to go from current to next

            return new DoorUpgradeResult
            {
                Allowed = allowed,
                BlinkWeaponIndex = allowed ? -1 : lowestIndex,
                Cost = cost,
                AtMaxLevel = false,
            };
        }

        public static TowerTier Tier(TowerDef tower, int level) =>
            tower.tiers != null && tower.tiers.Length > 0 ? tower.tiers[UnityEngine.Mathf.Clamp(level - 1, 0, tower.tiers.Length - 1)] : null;

        public static string TowerName(TowerDef tower, int level) => Tier(tower, level)?.name ?? tower.name;
        public static int DoorSupportLevel(TowerDef tower, int level) => Tier(tower, level)?.doorSupportLevel ?? level;
        public static float Damage(TowersConfig cfg, TowerDef t, int lv) => Tier(t, lv)?.damage ?? t.damage * UnityEngine.Mathf.Pow(cfg.levelScaling.damage, lv - 1);
        public static float FireRate(TowersConfig cfg, TowerDef t, int lv) => Tier(t, lv)?.shotsPerSecond ?? t.shotsPerSecond * UnityEngine.Mathf.Pow(cfg.levelScaling.fireRate, lv - 1);
        public static float BurnDamage(TowersConfig cfg, TowerDef t, int lv) => Tier(t, lv)?.burnDamagePerSecond ?? t.burnDamagePerSecond * UnityEngine.Mathf.Pow(cfg.levelScaling.damage, lv - 1);
        public static float FaithRate(TowerDef t, int lv) => Tier(t, lv)?.faithPerSecond ?? t.faithPerSecond * UnityEngine.Mathf.Pow(t.faithLevelScaling, lv - 1);
        public static float DreamRate(TowerDef t, int lv) => Tier(t, lv)?.dreamPerSecond ?? t.dreamPerSecond * UnityEngine.Mathf.Pow(UnityEngine.Mathf.Max(1f, t.dreamLevelScaling), lv - 1);

        /// <summary>Tower upgrade cost: baseUpgradeCost * upgradeCostGrowth^(currentLevel - 1).</summary>
        public static float TowerUpgradeCost(TowersConfig towers, TowerDef tower, int currentLevel)
        {
            var tier = Tier(tower, currentLevel);
            if (tier != null) return tier.upgradeCost;
            return tower.baseUpgradeCost * UnityEngine.Mathf.Pow(towers.levelScaling.upgradeCostGrowth, currentLevel - 1);
        }

        /// <summary>A tower's own maxLevel when set (e.g. the crystal ball is 1), otherwise the global towers.maxLevel.</summary>
        public static int TowerMaxLevel(TowersConfig towers, TowerDef tower)
        {
            if (tower.tiers != null && tower.tiers.Length > 0) return tower.tiers.Length;
            return tower.maxLevel > 0 ? tower.maxLevel : towers.maxLevel;
        }

        /// <summary>Range in tiles: rangeTiles[rangeClass] * levelScaling.range^(level - 1). Non-weapons have range 0.</summary>
        public static float TowerRange(TowersConfig towers, TowerDef tower, int level)
        {
            var tier = Tier(tower, level);
            if (tier != null) return tier.range;
            float baseRange;
            switch (tower.rangeClass)
            {
                case "short": baseRange = towers.rangeTiles.@short; break;
                case "mid": baseRange = towers.rangeTiles.mid; break;
                case "long": baseRange = towers.rangeTiles.@long; break;
                default: return 0f;
            }
            return baseRange * UnityEngine.Mathf.Pow(towers.levelScaling.range, level - 1);
        }

        public static bool InRange(TowersConfig config, TowerDef tower, int level, float distance) =>
            distance >= tower.minimumRange && distance <= TowerRange(config,tower,level);
        public static float DistanceBonus(TowerDef tower,float distance) =>
            (tower.bonusBelowRange > 0 && distance <= tower.bonusBelowRange) ||
            (tower.bonusAboveRange > 0 && distance >= tower.bonusAboveRange)
                ? UnityEngine.Mathf.Max(1,tower.distanceBonusMultiplier) : 1f;

        /// <summary>Total spent on a tower at a level: build cost plus every upgrade so far.</summary>
        public static float TowerTotalSpent(TowersConfig towers, TowerDef tower, int level)
        {
            float total = tower.buildCost;
            for (int l = 1; l < level; l++) total += TowerUpgradeCost(towers, tower, l);
            return total;
        }

        /// <summary>What selling gives back: sellRefundPct of everything spent on the tower.</summary>
        public static float TowerSellValue(TowersConfig towers, TowerDef tower, int level)
        {
            return UnityEngine.Mathf.Floor(TowerTotalSpent(towers, tower, level) * towers.sellRefundPct);
        }
    }
}
