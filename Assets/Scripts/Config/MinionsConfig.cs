using System;
namespace BadAppleHotel.Config
{
    /// <summary>One upgrade in the monster's minion tree (minions.json); costs has one entry per rank.</summary>
    [Serializable] public class MinionUpgradeDef
    {
        public string id, name, description;
        public float[] costs;
        public float perRank;
        /// <summary>Evolve only: total ranks of the other upgrades needed before each rank.</summary>
        public int[] requiresRanks;
    }
    [Serializable] public class MinionFormDef
    {
        public string name, trait;
        public float health, damage, interval = 1f, speed, doorMultiplier = 1f, scale = .5f;
    }
    /// <summary>A monster's minion species and its three evolution forms.</summary>
    [Serializable] public class MinionLineDef
    {
        public string id, monster;
        public float spawnMultiplier = 1f;
        public MinionFormDef[] forms;
    }
    [Serializable] public class MinionsConfig
    {
        /// <summary>Seconds into the night when each rift releases a share of its nightly batch.</summary>
        public float[] pulseSeconds;
        public int baseHorde = 2, maxHorde = 5;
        /// <summary>Each rift's batch is horde * (starting residents / alive) ^ aliveExponent.</summary>
        public float aliveExponent = .4f;
        public int nightCeiling = 40;
        public int swapEveryNights = 4;
        public float[] resistByRank;
        public float weaknessBonus = .25f;
        public float reachTiles = .85f, radius = .24f;
        public MinionUpgradeDef[] upgrades;
        public MinionLineDef[] lines;
    }
}
