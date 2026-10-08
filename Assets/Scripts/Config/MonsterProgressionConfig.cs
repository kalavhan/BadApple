using System;
namespace BadAppleHotel.Config
{
    [Serializable] public class EndlessConfig
    {
        public float healthMultiplier = 1, damageMultiplier = 1;
        public int freeLevelsPerNight = 1;
        public float healthPerNight = 0.04f, damagePerNight = 0.04f;
    }
    /// <summary>Where the monster's Fear comes from.</summary>
    [Serializable] public class FearConfig
    {
        public float perResidentDamage, perDoorDamage, perKill, perPart, idlePerSecond, idleAfterSeconds, perNight;
    }
    /// <summary>One of the five stat tracks a level point can go into.</summary>
    [Serializable] public class StatTrackDef
    {
        public string id, name, stat;
        public float perRank;
        public int maxRank;
    }
    [Serializable] public class MonsterProgressionConfig
    {
        public FearConfig fear;
        /// <summary>Level n costs levelPriceBase + levelPriceStep * (n - 2) Fear.</summary>
        public int maxLevel = 20;
        public float levelPriceBase, levelPriceStep;
        public float autoHealthPerLevel, autoAttackPerLevel;
        public StatTrackDef[] statTracks;
        public int specialLevel = 3;
        /// <summary>The special unlocks at specialLevel or at the start of this night, whichever comes first.</summary>
        public int specialUnlockNight = 2;
        public int[] abilityRankLevels, utilityLevels, growthLevels;
        /// <summary>Kit ability rank I/II/III multipliers.</summary>
        public float[] rankDamage, rankCooldown;
        public string[] utilityAbilities;
        public float hungerSeconds, frenzyMultiplier, incomeThreshold, incomeExcessMultiplier;
        public float partSpawnSeconds, partSpawnReductionPerNight, partSpawnMinSeconds, lairHealPerSecond;
        public float maxStunSeconds = 0.3f, stunRecoverySeconds = 1.2f;
        public float commitAfterSeconds, riskPerNight, riskPerMinuteWithoutKill, retreatHealth;
    }
}
