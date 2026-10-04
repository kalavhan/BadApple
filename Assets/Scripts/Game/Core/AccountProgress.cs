using System;
using System.Globalization;
using BadAppleHotel.Config;
using UnityEngine;
namespace BadAppleHotel.Game
{
    public static class AccountProgress
    {
        static string Key(Role r) => r == Role.Monster ? "bah_xp_monster" : "bah_xp_resident";
        public static double TotalXp(Role r)
        {
            string saved = PlayerPrefs.GetString(Key(r)+"_v2", "");
            if (double.TryParse(saved, NumberStyles.Float, CultureInfo.InvariantCulture, out double xp) && !double.IsNaN(xp) && !double.IsInfinity(xp)) return Math.Max(0,xp);
            return Math.Max(0,PlayerPrefs.GetInt(Key(r),0));
        }
        public static void AddXp(Role r, int xp)
        {
            PlayerPrefs.SetString(Key(r)+"_v2", Math.Min(double.MaxValue,TotalXp(r)+Math.Max(0,xp)).ToString("R",CultureInfo.InvariantCulture));
            PlayerPrefs.Save();
        }
        public static int Level(GameConfig cfg, Role r) { Progress(cfg,r,out int level,out _,out _); return level; }
        public static void Progress(GameConfig cfg, Role r, out int level, out double into, out double need)
            => Calculate(r==Role.Monster ? cfg.economy.levelCurve.monster : cfg.economy.levelCurve.resident,TotalXp(r),out level,out into,out need);
        public static void Calculate(GrowthCurve curve, double xp, out int level, out double into, out double need)
        {
            double total = Math.Max(0,xp), basis=Math.Max(1,curve.baseXp), growth=Math.Max(1.001,curve.growth);
            double estimate=(Math.Log(Math.Max(1,total))-Math.Log(basis)+Math.Log(growth-1))/Math.Log(growth);
            level=Math.Max(1,(int)Math.Floor(estimate)+1);
            double spent=basis*(Math.Pow(growth,level-1)-1)/(growth-1);
            while (spent>total && level>1) { level--; spent=basis*(Math.Pow(growth,level-1)-1)/(growth-1); }
            need=basis*Math.Pow(growth,level-1); into=Math.Max(0,total-spent);
            while (into>=need) { into-=need; level++; need*=growth; }
        }
    }
}
