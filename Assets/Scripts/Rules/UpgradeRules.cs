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

        /// <summary>Tower upgrade cost: baseUpgradeCost * upgradeCostGrowth^(currentLevel - 1).</summary>
        public static float TowerUpgradeCost(TowersConfig towers, TowerDef tower, int currentLevel)
        {
            return tower.baseUpgradeCost * UnityEngine.Mathf.Pow(towers.levelScaling.upgradeCostGrowth, currentLevel - 1);
        }
    }
}
