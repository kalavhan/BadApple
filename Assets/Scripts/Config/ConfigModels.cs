using System;

namespace BadAppleHotel.Config
{
    // Plain serializable models that mirror Assets/StreamingAssets/Config/*.json.
    // Field names must match the JSON keys exactly (Unity JsonUtility).

    [Serializable]
    public class MatchConfig
    {
        public int playersPerMatch;
        public int monsterCount;
        public int residentCount;
        public float setupSeconds;
        public int nightCount;
        public float nightSeconds;
        public float nightBreakSeconds;
        public int roomCount;
        public float residentHealth;
        public float monsterRespawnSeconds;
        public int bodyPartsPerNight;
        public bool deadResidentsSpectate;
    }

    [Serializable]
    public class Wallet
    {
        public float dreamPower;
        public float faith;
    }

    [Serializable]
    public class KillRewardConfig
    {
        public float monsterGainsResourcesPct;
        public float monsterGainsXpPct;
    }

    [Serializable]
    public class MonsterDeathConfig
    {
        public float killerResidentGainsResourcesPct;
    }

    [Serializable]
    public class MonsterIncomeConfig
    {
        public float dreamPowerPerDoorDamage;
        public float dreamPowerPerResidentKill;
        public float faithPerBodyPart;
    }

    [Serializable]
    public class AccountXpConfig
    {
        public int monsterPerResidentKilled;
        public int residentPerNightSurvived;
        public int residentSurvivedAllNightsBonus;
    }

    [Serializable]
    public class GrowthCurve
    {
        public float baseXp;
        public float growth;
        public int maxLevel;
    }

    [Serializable]
    public class ResidentXpValue
    {
        public float @base;
        public float growth;
    }

    [Serializable]
    public class LevelCurves
    {
        public GrowthCurve monster;
        public GrowthCurve resident;
    }

    [Serializable]
    public class SharingConfig
    {
        public bool enabled;
        public bool refundable;
        public bool lostIfRecipientDies;
        public float minTransfer;
    }

    [Serializable]
    public class EconomyConfig
    {
        public Wallet startingResources;
        public KillRewardConfig monsterKillsResident;
        public MonsterDeathConfig monsterDies;
        public MonsterIncomeConfig monsterIncome;
        public AccountXpConfig accountXp;
        public ResidentXpValue residentXpValueByLevel;
        public LevelCurves levelCurve;
        public SharingConfig sharing;
    }

    [Serializable]
    public class BedLevel
    {
        public int level;
        public string name;
        public UnityEngine.Vector2 sleepAnchor;
        public float sleepRotation;
        public float dreamPowerPerSecond;
        public float upgradeCost;
    }

    [Serializable]
    public class BedsConfig
    {
        public BedLevel[] levels;
    }

    [Serializable]
    public class DoorLevel
    {
        public int level;
        public float health;
        public float damageResistancePct;
        public float upgradeCost;
    }

    [Serializable]
    public class DoorsConfig
    {
        public int maxLevelGapOverLowestWeapon;
        public float blockedBlinkSeconds;
        public int emptyWeaponSlotCountsAsLevel;
        public DoorLevel[] levels;
    }

    [Serializable]
    public class TowerLevelScaling
    {
        public float damage;
        public float fireRate;
        public float range;
        public float upgradeCostGrowth;
    }

    [Serializable]
    public class RangeTiles
    {
        public float @short;
        public float mid;
        public float @long;
    }

    [Serializable]
    public class TowerTier
    {
        public string name;
        public string sprite;
        public int doorSupportLevel;
        public float upgradeCost;
        public float damage, shotsPerSecond, range;
        public float burnDamagePerSecond, burnSeconds, stunSeconds, slowPct, slowSeconds;
        public float faithPerSecond, dreamPerSecond;
    }

    [Serializable]
    public class TowerDef
    {
        public string id;
        public string name;
        public TowerTier[] tiers;
        public string damageType;   // bullet | electric | fire | slow | none
        public string rangeClass;   // short | mid | long (weapons only)
        public string effect;       // "" | faith | clairvoyance (non-weapons)
        public string costResource; // dreamPower | faith
        public float buildCost;
        public float baseUpgradeCost;
        public int maxLevel;        // 0 = use towers.maxLevel
        public float damage;
        public float shotsPerSecond;
        public float areaRadius;
        public int chainTargets;
        public float stunSeconds;
        public float coneDegrees;
        public float burnDamagePerSecond;
        public float burnSeconds;
        public float slowPct;
        public float slowSeconds;
        public float faithPerSecond;
        public float faithLevelScaling;
        public float dreamPerSecond;      // generator towers: Dream Power per second, any time
        public float dreamLevelScaling;
        public string category;           // resources | fire | bullets | electric | effects (build menu tab)
        public string description;
    }

    [Serializable]
    public class TowersConfig
    {
        public int maxLevel;
        public float sellRefundPct;
        public RangeTiles rangeTiles;
        public TowerLevelScaling levelScaling;
        public TowerDef[] towers;
    }

    [Serializable]
    public class DamageTakenMultiplier
    {
        public float bullet = 1f;
        public float electric = 1f;
        public float fire = 1f;
        public float slow = 1f;
    }

    [Serializable]
    public class ResistanceTracks
    {
        public int maxLevel;
        public float[] damageTakenMultiplierByLevel;
        public float[] upgradeCostByLevel;
        public string costResource;
    }

    [Serializable]
    public class MonsterDef
    {
        public string id;
        public string name;
        public float baseHealth;
        public float moveSpeed;
        public float doorDamagePerSecond;
        public float residentDamagePerSecond;
        public DamageTakenMultiplier damageTakenMultiplier;
    }

    [Serializable]
    public class MonstersConfig
    {
        public float attackReachTiles;
        public ResistanceTracks resistanceTracks;
        public MonsterDef[] monsters;
    }

    [Serializable]
    public class BodyPartDef
    {
        public string id;
        public string name;
        public string effect;
        public float perPart;
    }

    [Serializable]
    public class BodyPartsConfig
    {
        public float minSpacingTiles = 12f;
        public float eatSeconds;
        public int maxPartsPerType;
        public float keepOnRespawnPct;
        public BodyPartDef[] parts;
    }

    [Serializable]
    public class AbilityDef
    {
        public string id;
        public string name;
        public int unlockMonsterLevel;
        public float cooldownSeconds;
        public float durationSeconds;
        public string effect;
        public string damageType;
        public float value;
        public float radius;
    }

    [Serializable]
    public class AbilitiesConfig
    {
        public int loadoutSize;
        public string[] starterLoadout;
        public AbilityDef[] abilities;
    }

    [Serializable]
    public class MapConfig
    {
        public int width;
        public int height;
        public int corridorWidth;
        public int verticalCorridors;
        public int lotWidthMin;
        public int lotWidthMax;
        public int lotHeightMin;
        public int lotHeightMax;
        public int lotGapMax;
        public float sideDoorChance;
        public string[] letters;
        public int cellSizeMin;
        public int cellSizeMax;
        public int nibbleMax;
        public int bulgeMax;
        public float buildTileDensity;
        public int buildTilesMin;
        public int buildTilesMax;
        public int isolatedDoorDistance;
        public int minDoorDistance = 7;
        public int maxNearestDoorDistance = 32;
        public int deadEndCount = 4;
        public int loopCount = 2;
    }

    [Serializable]
    public class RosterDef
    {
        public string id;
        public string art;       // animation set under Resources/Art/Chars
        public string name;      // short name shown in game
        public string fullName;
        public string title;
        public string bio;
    }

    [Serializable]
    public class ResidentsConfig
    {
        public RosterDef[] roster;
        public float[] shirtHues;
        public float moveSpeed;
        public float awakeWeaponDamageMultiplier;
        public float awakeDreamPowerMultiplier;
        public float doorReachTiles;
        public float bedReachTiles;
        public float visionRadiusTiles;
        public float botWakeRadiusTiles;
    }

    // Everything the game reads, loaded once at startup.
    public class GameConfig
    {
        public MatchConfig match;
        public EconomyConfig economy;
        public BedsConfig beds;
        public DoorsConfig doors;
        public TowersConfig towers;
        public MonstersConfig monsters;
        public BodyPartsConfig bodyParts;
        public AbilitiesConfig abilities;
        public MapConfig map;
        public ResidentsConfig residents;
    }
}
