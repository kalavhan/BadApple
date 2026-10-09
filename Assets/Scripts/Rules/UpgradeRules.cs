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
        /// above the lowest-level weapon (0 or less: no limit). An empty weapon list counts as level emptyWeaponSlotCountsAsLevel.
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
            bool allowed = doors.maxLevelGapOverLowestWeapon <= 0 || newLevel - lowest <= doors.maxLevelGapOverLowestWeapon;
            float cost = doors.levels[currentDoorLevel - 1].upgradeCost; // cost to go from current to next

            return new DoorUpgradeResult
            {
                Allowed = allowed,
                BlinkWeaponIndex = allowed ? -1 : lowestIndex,
                Cost = cost,
                AtMaxLevel = false,
            };
        }

        // ---- towers: levels inside forms
        // A tiered tower has one form per tier. With towers.levels set, each form spans several levels
        // (for example 1-3, 4-7, 8-11, 12-15): level-ups grow its stats part of the way toward the next
        // form, and the last level of a form unlocks Evolve, the step into the next tier.

        static bool Leveled(TowersConfig towers, TowerDef tower) =>
            tower.tiers != null && tower.tiers.Length > 0 && towers?.levels?.formStartLevels != null && towers.levels.formStartLevels.Length > 0;

        static int FormCount(TowersConfig towers, TowerDef tower) =>
            Leveled(towers, tower) ? UnityEngine.Mathf.Min(tower.tiers.Length, towers.levels.formStartLevels.Length) : tower.tiers?.Length ?? 0;

        /// <summary>Which form (1-based tier) a tower shows at a level. Towers without tiers stay form 1.</summary>
        public static int Form(TowersConfig towers, TowerDef tower, int level)
        {
            if (tower.tiers == null || tower.tiers.Length == 0) return 1;
            if (!Leveled(towers, tower)) return UnityEngine.Mathf.Clamp(level, 1, tower.tiers.Length);
            int form = 1, count = FormCount(towers, tower);
            for (int i = 1; i < count; i++) if (level >= towers.levels.formStartLevels[i]) form = i + 1;
            return form;
        }

        /// <summary>First level of a form.</summary>
        public static int FormStart(TowersConfig towers, TowerDef tower, int form) =>
            Leveled(towers, tower) ? towers.levels.formStartLevels[UnityEngine.Mathf.Clamp(form - 1, 0, FormCount(towers, tower) - 1)] : form;

        /// <summary>Last level of a form: the level where Evolve unlocks, or the max level for the final form.</summary>
        public static int FormEnd(TowersConfig towers, TowerDef tower, int form) =>
            form < FormCount(towers, tower) ? FormStart(towers, tower, form + 1) - 1 : TowerMaxLevel(towers, tower);

        /// <summary>The tier data of a form (1-based), or null for towers without tiers.</summary>
        public static TowerTier FormTier(TowerDef tower, int form) =>
            tower.tiers != null && tower.tiers.Length > 0 ? tower.tiers[UnityEngine.Mathf.Clamp(form - 1, 0, tower.tiers.Length - 1)] : null;

        public static TowerTier Tier(TowersConfig towers, TowerDef tower, int level) => FormTier(tower, Form(towers, tower, level));

        public enum Step { LevelUp, Evolve, Max }

        /// <summary>What the next upgrade does: a level-up inside the form, Evolve into the next form, or nothing.</summary>
        public static Step NextStep(TowersConfig towers, TowerDef tower, int level)
        {
            if (level >= TowerMaxLevel(towers, tower)) return Step.Max;
            if (tower.tiers == null || tower.tiers.Length == 0) return Step.LevelUp;
            if (!Leveled(towers, tower)) return Step.Evolve;
            int form = Form(towers, tower, level);
            return form < FormCount(towers, tower) && level == FormEnd(towers, tower, form) ? Step.Evolve : Step.LevelUp;
        }

        /// <summary>
        /// A tier stat at a level: the form's value at its first level, growing (in log space) toward the next
        /// form's value. The final form keeps growing at the ratio between the last two forms.
        /// </summary>
        static float Grow(TowersConfig towers, TowerDef tower, int level, System.Func<TowerTier, float> stat)
        {
            int form = Form(towers, tower, level);
            float value = stat(FormTier(tower, form));
            if (!Leveled(towers, tower)) return value;
            int start = FormStart(towers, tower, form), end = FormEnd(towers, tower, form);
            if (end <= start || level <= start) return value;
            float target;
            if (form < FormCount(towers, tower)) target = stat(tower.tiers[form]);
            else if (form > 1)
            {
                float previous = stat(tower.tiers[form - 2]);
                target = previous > 0f ? value * value / previous : value;
            }
            else return value;
            float frac = towers.levels.inFormGrowth * (level - start) / (end - start);
            if (value > 0f && target > 0f) return value * UnityEngine.Mathf.Pow(target / value, frac);
            return UnityEngine.Mathf.Lerp(value, target, frac);
        }

        public static string TowerName(TowersConfig towers, TowerDef tower, int level) => Tier(towers, tower, level)?.name ?? tower.name;
        public static int DoorSupportLevel(TowersConfig towers, TowerDef tower, int level) => Tier(towers, tower, level)?.doorSupportLevel ?? level;
        public static float Damage(TowersConfig cfg, TowerDef t, int lv) => t.tiers != null && t.tiers.Length > 0 ? Grow(cfg, t, lv, x => x.damage) : t.damage * UnityEngine.Mathf.Pow(cfg.levelScaling.damage, lv - 1);
        public static float FireRate(TowersConfig cfg, TowerDef t, int lv) => t.tiers != null && t.tiers.Length > 0 ? Grow(cfg, t, lv, x => x.shotsPerSecond) : t.shotsPerSecond * UnityEngine.Mathf.Pow(cfg.levelScaling.fireRate, lv - 1);
        public static float BurnDamage(TowersConfig cfg, TowerDef t, int lv) => t.tiers != null && t.tiers.Length > 0 ? Grow(cfg, t, lv, x => x.burnDamagePerSecond) : t.burnDamagePerSecond * UnityEngine.Mathf.Pow(cfg.levelScaling.damage, lv - 1);
        public static float SlowPct(TowersConfig cfg, TowerDef t, int lv) => t.tiers != null && t.tiers.Length > 0 ? Grow(cfg, t, lv, x => x.slowPct) : t.slowPct * (1f + 0.05f * (lv - 1));
        public static float FaithRate(TowersConfig cfg, TowerDef t, int lv) => t.tiers != null && t.tiers.Length > 0 ? Grow(cfg, t, lv, x => x.faithPerSecond) : t.faithPerSecond * UnityEngine.Mathf.Pow(t.faithLevelScaling, lv - 1);
        public static float DreamRate(TowersConfig cfg, TowerDef t, int lv) => t.tiers != null && t.tiers.Length > 0 ? Grow(cfg, t, lv, x => x.dreamPerSecond) : t.dreamPerSecond * UnityEngine.Mathf.Pow(UnityEngine.Mathf.Max(1f, t.dreamLevelScaling), lv - 1);

        /// <summary>
        /// Cost of the next step from currentLevel. Going from one form to the next costs the tier's upgradeCost
        /// in total, split between the level-ups (levelUpShare, each levelUpCostGrowth times the one before) and
        /// Evolve (the rest). The final form's level-ups share the last evolve price times finalFormCostMultiplier.
        /// Untiered towers use baseUpgradeCost * upgradeCostGrowth^(currentLevel - 1).
        /// </summary>
        public static float TowerUpgradeCost(TowersConfig towers, TowerDef tower, int currentLevel)
        {
            if (tower.tiers == null || tower.tiers.Length == 0)
                return tower.baseUpgradeCost * UnityEngine.Mathf.Pow(towers.levelScaling.upgradeCostGrowth, currentLevel - 1);
            int form = Form(towers, tower, currentLevel);
            var tier = FormTier(tower, form);
            if (!Leveled(towers, tower)) return tier.upgradeCost;
            if (NextStep(towers, tower, currentLevel) == Step.Evolve)
            {
                // Evolve takes what the level-ups left of the tier price (their rounding included).
                float spent = 0f;
                for (int l = FormStart(towers, tower, form); l < currentLevel; l++) spent += TowerUpgradeCost(towers, tower, l);
                return UnityEngine.Mathf.Max(1f, UnityEngine.Mathf.Round(tier.upgradeCost - spent));
            }
            var lv = towers.levels;
            bool final = form >= FormCount(towers, tower);
            float total = !final ? tier.upgradeCost : form > 1 ? tower.tiers[form - 2].upgradeCost * lv.finalFormCostMultiplier : tier.upgradeCost;
            float share = final ? 1f : lv.levelUpShare;
            int start = FormStart(towers, tower, form), ups = FormEnd(towers, tower, form) - start;
            float weights = 0f;
            for (int i = 0; i < ups; i++) weights += UnityEngine.Mathf.Pow(lv.levelUpCostGrowth, i);
            float weight = UnityEngine.Mathf.Pow(lv.levelUpCostGrowth, currentLevel - start);
            return UnityEngine.Mathf.Round(total * share * weight / UnityEngine.Mathf.Max(1f, weights));
        }

        /// <summary>
        /// Highest level: with forms, levels.maxLevel (or the end of the last form a short tier list reaches);
        /// otherwise one level per tier, or the tower's own maxLevel (the crystal ball is 1), or towers.maxLevel.
        /// </summary>
        public static int TowerMaxLevel(TowersConfig towers, TowerDef tower)
        {
            if (tower.tiers != null && tower.tiers.Length > 0)
            {
                if (!Leveled(towers, tower)) return tower.tiers.Length;
                var starts = towers.levels.formStartLevels;
                return tower.tiers.Length >= starts.Length ? towers.levels.maxLevel : starts[tower.tiers.Length] - 1;
            }
            return tower.maxLevel > 0 ? tower.maxLevel : towers.maxLevel;
        }

        /// <summary>Range in tiles: rangeTiles[rangeClass] * levelScaling.range^(level - 1). Non-weapons have range 0.</summary>
        public static float TowerRange(TowersConfig towers, TowerDef tower, int level)
        {
            if (tower.tiers != null && tower.tiers.Length > 0) return Grow(towers, tower, level, x => x.range);
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
