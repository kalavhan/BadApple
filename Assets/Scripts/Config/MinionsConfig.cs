using System;
namespace BadAppleHotel.Config
{
    /// <summary>A battlefield job shared by every monster's horde: Swarm, Breachers or Escort.</summary>
    [Serializable] public class MinionRoleDef
    {
        public string id, name, blurb;
        /// <summary>Minions per rift per night at Strength I and at max Strength (Swarm and Breachers).</summary>
        public int perDoor, perDoorAtMax;
        /// <summary>Escorts alive at once around the monster at Strength I and at max Strength.</summary>
        public int escortCap, escortCapAtMax;
    }
    /// <summary>One of a monster's three creatures: its role, stats, built-in resistance and signature evolution
    /// (a one-time purchase, priced by what that behaviour is worth).</summary>
    [Serializable] public class MinionCreatureDef
    {
        public string role, name, resist, evolveName, evolve, evolveText;
        public float health, damage, interval = 1f, speed, doorMultiplier = 1f, residentMultiplier = 1f, interceptChance, evolveCost;
    }
    [Serializable] public class MinionLineDef
    {
        public string id, monster;
        public MinionCreatureDef[] creatures;
    }
    [Serializable] public class MinionsConfig
    {
        /// <summary>Seconds into the night when each rift releases a share of its nightly batch.</summary>
        public float[] pulseSeconds;
        /// <summary>Each rift's batch is perDoor * (starting residents / alive) ^ aliveExponent.</summary>
        public float aliveExponent = .4f;
        public int nightCeiling = 40;
        public float reachTiles = .85f, radius = .24f;
        /// <summary>Damage cut from the creature's own resistance, and the extra taken from its weakness.</summary>
        public float resistPct = .5f, weaknessBonus = .25f;
        /// <summary>Awakening unlocks the first creature; unlockCosts are per creature (index 0 is the awakening).</summary>
        public float awakenCost = 30f;
        public float[] unlockCosts;
        /// <summary>Horde Strength: one shared level for every creature, owned now or unlocked later. Each rank adds
        /// health and damage and moves each role's numbers toward their max-level count.</summary>
        public int maxStrength = 6;
        public float strengthCostBase = 40f, strengthCostGrowth = 1.4f;
        public float healthPerStrength = .15f, damagePerStrength = .12f;
        public float escortFollowTiles = 1.4f, interceptRangeTiles = 1.8f, tauntRangeTiles = 4f;
        public MinionRoleDef[] roles;
        public MinionLineDef[] lines;
    }
}
