using System;
namespace BadAppleHotel.Config
{
    [Serializable] public class EndlessConfig
    {
        public float healthMultiplier = 1, damageMultiplier = 1;
        public int freeLevelsPerNight = 1;
        public float healthPerNight = 0.04f, damagePerNight = 0.04f;
    }
    [Serializable] public class AbilityPool { public string monster; public string[] abilities; }
    [Serializable] public class EvolutionDef
    {
        public string monster, branch, name, tint, passive, ability, art;
        public int level, accountLevel;
    }
    [Serializable] public class MonsterProgressionConfig
    {
        public float baseXp, growth, aliveXpPerSecond, doorDamageXp, biteDamageXp, partXp, killXp;
        public float healthPerLevel, attackPerLevel, speedPerLevel, speedBonusCap;
        public int[] slotLevels, evolutionLevels;
        public int ascensionEvery;
        public float hungerSeconds, frenzyMultiplier, incomeThreshold, incomeExcessMultiplier;
        public float partSpawnSeconds, partSpawnReductionPerNight, partSpawnMinSeconds, lairHealPerSecond;
        public float maxStunSeconds = 0.3f, stunRecoverySeconds = 1.2f;
        public float commitAfterSeconds, riskPerNight, riskPerMinuteWithoutKill, retreatHealth;
        public AbilityPool[] pools;
        public EvolutionDef[] evolutions;
        public string[] ascensionPerks;
    }
}
