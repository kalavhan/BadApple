using BadAppleHotel.Config;
using UnityEngine;

namespace BadAppleHotel.Game
{
    public enum Phase { RoleSelect, Setup, Night, Results, ConfigError }
    public enum Role { Resident, Monster }
    public enum ActionResult { Ok, NoMoney, Blocked, MaxLevel, Invalid }

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
        public float Cooldown;
        public float BlinkUntil;
        public SpriteRenderer Sr;
        public bool IsWeapon => Def.damageType != "none";
    }

    public class Room
    {
        public RoomDef Def;
        public Resident Owner;
        public int DoorLevel = 1;
        public float DoorHp;
        public bool DoorBroken;
        public int BedLevel = 1;
        public readonly TowerInstance[] Slots = new TowerInstance[6];
        public float LastAttackedTime = -99f;
        public float NoWeaponBlinkUntil;
        public SpriteRenderer DoorSr;
        public SpriteRenderer BedSr;

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
        public SpriteRenderer Sr;
        public ResidentAI Ai;
        public float ClaimAt;   // bots: when they pick a room during setup
    }

    public class Monster
    {
        public MonsterDef Def;
        public bool IsHuman;
        public Vector2 Pos;
        public Vector2 Facing = Vector2.right;
        public float Hp;
        public float DreamPower;
        public float Faith;
        public readonly int[] ResistLevels = new int[4];
        public readonly int[] Parts = new int[4]; // arm, leg, torso, eye
        public AbilityDef[] Loadout;
        public float[] Cooldowns;
        public bool Dead;
        public float RespawnAt;
        public int Kills;
        public float MatchXp;
        public Room AttackingRoom;
        public Resident LastDamager;
        public BodyPart EatingPart;
        public float EatProgress;

        // timed effects
        public float SlowPct, SlowUntil, StunUntil;
        public float BurnDps, BurnUntil;
        public Resident BurnSource;
        public float JamUntil, JamValue = 1f, JamRadius;
        public float RampageUntil, RampageValue = 1f;
        public float CloakUntil;
        public float DashUntil;
        public Vector2 DashVelocity;

        public SpriteRenderer Sr;
        public MonsterAI Ai;
    }

    public class BodyPart
    {
        public BodyPartDef Def;
        public int TypeIndex;
        public Vector2Int Tile;
        public SpriteRenderer Sr;
    }
}
