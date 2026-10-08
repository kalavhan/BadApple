using BadAppleHotel.Config;
using UnityEngine;

namespace BadAppleHotel.Game
{
    public enum Phase { RoleSelect, Setup, Night, Results, ConfigError }
    public enum Role { Resident, Monster }
    public enum ActionResult { Ok, NoMoney, Blocked, MaxLevel, Invalid, TooFar }

    public static class DamageTypes
    {
        public const int Bullet = 0, Electric = 1, Fire = 2, Slow = 3;
        public static readonly string[] Names = { "bullet", "electric", "fire", "slow" };

        public static int Index(string type)
        {
            switch (type)
            {
                case "bullet": return Bullet;
                case "electric": return Electric;
                case "fire": return Fire;
                case "slow": return Slow;
                default: return -1;
            }
        }

        public static string Label(int i)
        {
            switch (i)
            {
                case Bullet: return "Bullet";
                case Electric: return "Electric";
                case Fire: return "Fire";
                default: return "Slow";
            }
        }
    }

    public class TowerInstance
    {
        public TowerDef Def;
        public int Level = 1;
        public int SlotIndex;
        public Vector2Int Tile;
        public float Cooldown;
        public float BlinkUntil;
        public bool Decoy;
        public float DisabledUntil, Decay;
        public SpriteRenderer Sr;
        public bool IsWeapon => Def.damageType != "none" && Def.damageType != "" && Def.damageType != null;
        public bool IsFaith => Def.effect == "faith" || Def.faithPerSecond > 0f;
        public bool IsDreamGen => Def.dreamPerSecond > 0f;
        public bool IsClairvoyance => Def.effect == "clairvoyance";
    }

    public class Room
    {
        public readonly RoomDef Def;
        public Resident Owner;
        public int DoorLevel = 1;
        public float DoorHp;
        public bool DoorBroken;
        public bool DoorOpen = true;
        public bool CloseWhenClear;
        public int BedLevel = 1;
        public readonly TowerInstance[] Slots;
        public float LastAttackedTime = -99f;
        public float RotUntil;             // Widow Mildred's rot: the door cannot be repaired or upgraded
        public float NoWeaponBlinkUntil;
        public SpriteRenderer DoorSr;
        public SpriteRenderer BedSr;
        public MeshRenderer BedModel;

        public Room(RoomDef def)
        {
            Def = def;
            Slots = new TowerInstance[def.BuildTiles.Count];
        }

        /// <summary>True when the monster cannot walk through the doorway.</summary>
        public bool DoorBlocks => !DoorOpen && !DoorBroken;

        public int WeaponCount()
        {
            int n = 0;
            foreach (var t in Slots) if (t != null && t.IsWeapon) n++;
            return n;
        }

        public int CountTowers(string id)
        {
            int n = 0;
            foreach (var t in Slots) if (t != null && t.Def.id == id) n++;
            return n;
        }

        public bool HasClairvoyance()
        {
            foreach (var t in Slots) if (t != null && t.IsClairvoyance) return true;
            return false;
        }

        public int EmptySlot()
        {
            for (int i = 0; i < Slots.Length; i++) if (Slots[i] == null) return i;
            return -1;
        }

        public bool UnderAttack(float now) => now - LastAttackedTime < 2f;
    }

    public class Resident
    {
        public int Id;
        public string Name;
        public bool IsHuman;
        public bool IsMonster;
        public int ColorIndex;
        public Room Room;
        public float DreamPower;
        public float Faith;
        public bool Alive = true;
        public float Health;
        public int NightsSurvived;
        public float XpValue;
        public float FaithBlockedUntil;
        public float BedSlowUntil;
        public float BedSlowValue = 1f;
        public Vector2 Pos;
        public Vector2 Facing = Vector2.right;
        public bool Asleep;
        public bool SleepRequested;
        public readonly Navigator Navigator = new Navigator();
        public float SlowUntil;
        public float StunUntil;            // the Night Porter's every-third-hit stun, the Meat Hook yank
        public float SleepBlend = 1f;      // 0..1: how far the sprite has glided onto the bed
        public Vector2 SleepFrom;          // where the glide started
        public SpriteRenderer Sr;
        public CharacterAnimator Anim;     // 8-direction art; null when only the placeholder sprite exists
        public CharacterModel Model;       // baked 3D resident; replaces Anim when the roster art has a model
        public RosterDef Char;             // which of the roster characters this seat plays
        public Vector2 LastPos;
        public float AttackUntil;          // personal attack animation; bots may join their towers
        public float NextPersonalShotAt;
        public ResidentAI Ai;
        public float ClaimAt;   // bots: when they start walking to a room during setup
    }

    public class Monster
    {
        public MonsterDef Def;
        public Room Lair;
        public float RevealedAt;
        public bool IsHuman;
        public bool IsMonster;
        public Vector2 Pos;
        public Vector2 Facing = Vector2.right;
        public float Hp;
        public float DreamPower;
        public float Faith;
        public readonly int[] Parts = new int[4]; // arm, leg, torso, eye
        public AbilityDef[] Loadout;               // [area, special (from specialLevel), utility picks...]
        public float[] Cooldowns;
        public bool Dead;
        public float RespawnAt;
        public int Kills;
        public int Level = 1;
        public float LastDamageAt, LastKillAt, SprintUntil, NextSprint, SlowZoneUntil;
        public bool Frenzy, Retreating;
        public readonly System.Collections.Generic.Queue<ProgressChoice> Choices = new System.Collections.Generic.Queue<ProgressChoice>();
        public Room AttackingRoom;
        public Resident Biting;
        public Resident LastDamager;
        public BodyPart EatingPart;
        public float EatProgress;

        // Fear economy and growth
        public float Fear, FearEarned;
        public int[] StatRanks;                    // one per progression.statTracks entry
        public readonly int[] KitRanks = { 1, 1, 1 }; // single target, area, special
        public float NextAttackAt;
        public int HitCount;

        // minions
        public int[] MinionRanks;                  // one per minions.upgrades entry
        public int MinionResist = -1;              // damage type index the horde resists; -1 before the horde awakens
        public int ResistChosenNight;
        public readonly System.Collections.Generic.HashSet<Room> Scouted = new System.Collections.Generic.HashSet<Room>();

        // signature specials
        public Room PhasedRoom;
        public float PhaseUntil;

        // timed effects
        public float SlowPct, SlowUntil, StunUntil, StunImmuneUntil;
        public float BurnDps, BurnUntil;
        public Resident BurnSource;
        public float JamUntil, JamValue = 1f, JamRadius;
        public float RampageUntil, RampageValue = 1f;
        public float CloakUntil;
        public float DashUntil;
        public Vector2 DashVelocity;

        public SpriteRenderer Sr;
        public CharacterAnimator Anim;
        public MonsterAI Ai;
    }

    /// <summary>A minion: walks from its rift to its resident's door, chews through it, then attacks the resident.</summary>
    public class Minion
    {
        public MinionFormDef Form;
        public int FormIndex;
        public Rift Rift;
        public Vector2 Pos;
        public Vector2 Facing = Vector2.down;
        public float Hp, MaxHp;
        public int Resist;
        public float NextAttackAt;
        public bool ShotSwallowed;
        public float SlowPct, SlowUntil, StunUntil, BurnDps, BurnUntil;
        public Resident BurnSource;
        public bool Dead;
        public readonly Navigator Navigator = new Navigator();
        public SpriteRenderer Sr;
    }

    /// <summary>A spectral tear in the hallway outside a living resident's door; minions crawl out of it each night.</summary>
    public class Rift
    {
        public Room Room;
        public Vector2 Pos;
        public int Pending;                  // minions still to release tonight
        public int ReleasedTonight;
        public bool Shrine;                  // Widow Mildred's Graveroot: heals minions around the rift
        public bool Boost;                   // ...and doubles the rift's next pulse
        public SpriteRenderer Sr;
    }

    /// <summary>A lingering hazard left by an area attack (ember ground, spore cloud).</summary>
    public class HazardZone
    {
        public Vector2 Pos;
        public float Radius, Until, Dps, SlowPct;
        public bool NoDreams;
        public Color Color;
        public SpriteRenderer Sr;
    }

    public class BodyPart
    {
        public BodyPartDef Def;
        public int TypeIndex;
        public Vector2Int Tile;
        public SpriteRenderer Sr;
    }
}
