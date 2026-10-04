using BadAppleHotel.Config;
using BadAppleHotel.Rules;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>Separate Monster and Resident account XP, saved locally with PlayerPrefs for the demo.</summary>
    public static class AccountProgress
    {
        static string Key(Role r) => r == Role.Monster ? "bah_xp_monster" : "bah_xp_resident";

        public static int TotalXp(Role r) => PlayerPrefs.GetInt(Key(r), 0);

        public static void AddXp(Role r, int xp)
        {
            PlayerPrefs.SetInt(Key(r), TotalXp(r) + Mathf.Max(0, xp));
            PlayerPrefs.Save();
        }

        static GrowthCurve Curve(GameConfig cfg, Role r) =>
            r == Role.Monster ? cfg.economy.levelCurve.monster : cfg.economy.levelCurve.resident;

        public static int Level(GameConfig cfg, Role r)
        {
            Progress(cfg, r, out int level, out _, out _);
            return level;
        }

        /// <summary>Level, XP into the current level, and XP needed for the next one.</summary>
        public static void Progress(GameConfig cfg, Role r, out int level, out int into, out int need)
        {
            var curve = Curve(cfg, r);
            int xp = TotalXp(r);
            level = 1;
            need = EconomyRules.XpForLevel(curve, 1);
            while (level < curve.maxLevel)
            {
                need = EconomyRules.XpForLevel(curve, level);
                if (xp < need) break;
                xp -= need;
                level++;
            }
            into = xp;
        }
    }
}
