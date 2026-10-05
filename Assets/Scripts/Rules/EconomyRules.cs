using BadAppleHotel.Config;

namespace BadAppleHotel.Rules
{
    public struct Reward
    {
        public float DreamPower;
        public float Faith;
        public float Xp;
    }

    public static class EconomyRules
    {
        /// <summary>The Monster kills a Resident: it takes a percentage of that Resident's resources and XP value.</summary>
        public static Reward MonsterKillReward(EconomyConfig e, float victimDreamPower, float victimFaith, float victimXpValue)
        {
            return new Reward
            {
                DreamPower = victimDreamPower * e.monsterKillsResident.monsterGainsResourcesPct,
                Faith = victimFaith * e.monsterKillsResident.monsterGainsResourcesPct,
                Xp = victimXpValue * e.monsterKillsResident.monsterGainsXpPct,
            };
        }

        /// <summary>A Resident's defenses kill the Monster: that Resident gains a percentage (120% by default) of its resources.</summary>
        public static Reward MonsterDeathReward(EconomyConfig e, float monsterDreamPower, float monsterFaith)
        {
            return new Reward
            {
                DreamPower = monsterDreamPower * e.monsterDies.killerResidentGainsResourcesPct,
                Faith = monsterFaith * e.monsterDies.killerResidentGainsResourcesPct,
                Xp = 0f,
            };
        }

        /// <summary>XP needed to go from level-1 to level: baseXp * growth^(level-1). Level 1 needs baseXp.</summary>
        public static double XpForLevel(GrowthCurve curve, int level)
        {
            return System.Math.Round(curve.baseXp * System.Math.Pow(curve.growth, level - 1));
        }

        public static int MonsterAccountXp(EconomyConfig e, int residentsKilled)
        {
            return residentsKilled * e.accountXp.monsterPerResidentKilled;
        }

        public static int ResidentAccountXp(EconomyConfig e, int nightsSurvived, int totalNights)
        {
            int xp = nightsSurvived * e.accountXp.residentPerNightSurvived;
            if (nightsSurvived >= totalNights) xp += e.accountXp.residentSurvivedAllNightsBonus;
            return xp;
        }
    }
}
