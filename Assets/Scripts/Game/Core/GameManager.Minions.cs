using System;
using System.Collections.Generic;
using System.Linq;
using BadAppleHotel.Config;
using UnityEngine;
using Random = UnityEngine.Random;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// The monster's horde. Awakening it opens a rift outside every living resident's door and unlocks the first of
    /// the monster's three creatures (Swarm, Breachers, Escort); the other two are bought. One creature spawns at a
    /// time: a new choice is queued, takes over at the next pulse, and switching then locks for the rest of the night.
    /// Horde Strength is one shared level that improves every creature, including ones unlocked later; each creature
    /// also has one signature evolution, bought once and kept when switching.
    /// </summary>
    public partial class GameManager
    {
        public readonly List<Minion> Minions = new List<Minion>();
        public readonly List<Rift> Rifts = new List<Rift>();
        /// <summary>Which damage types the human resident has seen land on each creature (index * 4 + type); its multiplier shows in the HUD after that.</summary>
        public readonly bool[] KnownMinionTypes = new bool[12];
        public bool KnownMinion(int creature, int type) => type >= 0 && type < 4 && KnownMinionTypes[creature * 4 + type];
        int pulsesFired;
        float nextScout;

        // ------------------------------------------------------------------ creatures

        public MinionLineDef MinionLine(Monster m) => Array.Find(Cfg.minions.lines, l => l.id == m.Def.minionLine);
        public MinionCreatureDef Creature(Monster m, int i) => MinionLine(m).creatures[i];
        public MinionRoleDef MinionRole(string id) => Array.Find(Cfg.minions.roles, r => r.id == id);
        public bool HordeAwake(Monster m) => m != null && m.HordeStrength > 0;
        public bool MinionOwned(Monster m, int i) => m != null && m.MinionOwned[i];
        public bool MinionEvolved(Monster m, int i) => m != null && m.MinionEvolved[i];
        public int ResistOf(MinionCreatureDef c) => DamageTypes.Index(c.resist);

        /// <summary>Bullet-proof minions are weak to fire, fire-proof to electric, electric-proof to bullets.</summary>
        public static int Weakness(int resist)
        {
            switch (resist)
            {
                case DamageTypes.Bullet: return DamageTypes.Fire;
                case DamageTypes.Fire: return DamageTypes.Electric;
                case DamageTypes.Electric: return DamageTypes.Bullet;
                default: return -1;
            }
        }

        public float MinionDamageTaken(Minion n, int type)
        {
            if (type < 0 || type == DamageTypes.Slow) return 1f;
            if (type == n.Resist) return 1f - Cfg.minions.resistPct;
            if (type == Weakness(n.Resist)) return 1f + Cfg.minions.weaknessBonus;
            return 1f;
        }

        /// <summary>Fear to unlock creature i (index 0 is the awakening), or -1 once it is owned.</summary>
        public float UnlockCost(Monster m, int i) => MinionOwned(m, i) ? -1f : i == 0 ? Cfg.minions.awakenCost : Cfg.minions.unlockCosts[i];

        /// <summary>Unlocks creature i. The first one awakens the horde; later ones queue themselves to deploy at the next pulse when switching is open.</summary>
        public ActionResult TryUnlockMinion(Monster m, int i)
        {
            if (m == null || i < 0 || i > 2) return ActionResult.Invalid;
            if (MinionOwned(m, i)) return ActionResult.MaxLevel;
            if (i > 0 && !HordeAwake(m)) return ActionResult.Blocked;
            float cost = UnlockCost(m, i);
            if (m.Fear + .001f < cost) return ActionResult.NoMoney;
            m.Fear -= cost;
            m.MinionOwned[i] = true;
            if (i == 0)
            {
                m.HordeStrength = 1;
                m.ActiveMinion = 0;
                if (Phase == Phase.Night) StartNightRifts();
                Announce(m.IsHuman ? "Your horde awakens. Rifts open outside every door." : "Rifts tear open outside every door...", 3f);
            }
            else if (CanSwitchMinion(m)) m.QueuedMinion = i;
            AddFloater(m.Pos + Vector2.up * 2.2f, Creature(m, i).name + "!", ColorOf(m));
            return ActionResult.Ok;
        }

        /// <summary>A new creature can be queued unless a switch already happened tonight.</summary>
        public bool CanSwitchMinion(Monster m) => m.SwitchedNight < Night;

        /// <summary>Queues creature i to take over at the next pulse (free). Picking the active one cancels a queued switch.</summary>
        public ActionResult TryQueueMinion(Monster m, int i)
        {
            if (!MinionOwned(m, i)) return ActionResult.Invalid;
            if (m.ActiveMinion == i) { m.QueuedMinion = -1; return ActionResult.Ok; }
            if (!CanSwitchMinion(m)) return ActionResult.Blocked;
            m.QueuedMinion = i;
            if (m.IsHuman) Toast(Creature(m, i).name + " come out of the rifts from the next pulse.");
            return ActionResult.Ok;
        }

        public float EvolveCost(Monster m, int i) => MinionEvolved(m, i) ? -1f : Creature(m, i).evolveCost;

        /// <summary>Buys creature i's signature evolution once; it is kept when switching away and back.</summary>
        public ActionResult TryEvolveMinion(Monster m, int i)
        {
            if (!MinionOwned(m, i)) return ActionResult.Blocked;
            if (MinionEvolved(m, i)) return ActionResult.MaxLevel;
            float cost = EvolveCost(m, i);
            if (m.Fear + .001f < cost) return ActionResult.NoMoney;
            m.Fear -= cost;
            m.MinionEvolved[i] = true;
            AddFloater(m.Pos + Vector2.up * 2.2f, Creature(m, i).evolveName + "!", ColorOf(m));
            return ActionResult.Ok;
        }

        /// <summary>Fear for the next Horde Strength rank, or -1 before awakening or at the cap.</summary>
        public float StrengthCost(Monster m) => StrengthCost(Cfg.minions, m.HordeStrength);

        /// <summary>base × growth^(strength − 1).</summary>
        public static float StrengthCost(MinionsConfig c, int strength)
        {
            if (strength <= 0 || strength >= c.maxStrength) return -1f;
            return Mathf.Round(c.strengthCostBase * Mathf.Pow(c.strengthCostGrowth, strength - 1));
        }

        public ActionResult TryUpgradeStrength(Monster m)
        {
            if (!HordeAwake(m)) return ActionResult.Blocked;
            float cost = StrengthCost(m);
            if (cost < 0f) return ActionResult.MaxLevel;
            if (m.Fear + .001f < cost) return ActionResult.NoMoney;
            m.Fear -= cost;
            m.HordeStrength++;
            AddFloater(m.Pos + Vector2.up * 2.2f, "Horde Strength " + m.HordeStrength + "!", ColorOf(m));
            return ActionResult.Ok;
        }

        float StrengthLerp(Monster m) => Cfg.minions.maxStrength <= 1 ? 1f : (Mathf.Max(1, m.HordeStrength) - 1) / (float)(Cfg.minions.maxStrength - 1);
        public int PerDoor(Monster m, int i) { var r = MinionRole(Creature(m, i).role); return Mathf.RoundToInt(Mathf.Lerp(r.perDoor, r.perDoorAtMax, StrengthLerp(m))); }
        public int EscortCap(Monster m, int i) { var r = MinionRole(Creature(m, i).role); return Mathf.RoundToInt(Mathf.Lerp(r.escortCap, r.escortCapAtMax, StrengthLerp(m))); }
        public float MinionHealth(Monster m, int i) => Creature(m, i).health * (1f + (Mathf.Max(1, m.HordeStrength) - 1) * Cfg.minions.healthPerStrength);
        public float MinionHitDamage(Monster m, int i) => Creature(m, i).damage * (1f + (Mathf.Max(1, m.HordeStrength) - 1) * Cfg.minions.damagePerStrength);

        // ------------------------------------------------------------------ scouting

        /// <summary>The monster learns a room's towers only once it has looked inside (open door in view, or within its Eye reveal radius).</summary>
        void UpdateScouting(Monster m, float now)
        {
            if (now < nextScout) return;
            nextScout = now + 0.5f;
            float radius = Cfg.residents.visionRadiusTiles + RevealRadius(m);
            float reveal = RevealRadius(m);
            var here = HotelMap.ToTile(m.Pos);
            foreach (var room in RoomsByDef.Values)
            {
                if (room.Owner == null || !room.Owner.Alive || m.Scouted.Contains(room)) continue;
                var inside = HotelMap.Center(room.Def.DoorInside);
                var bed = HotelMap.Center(room.Def.BedTile);
                if (room.Def.ContainsInterior(here) || Vector2.Distance(m.Pos, bed) <= reveal ||
                    CanSee(m.Pos, inside, radius) || CanSee(m.Pos, bed, radius))
                    m.Scouted.Add(room);
            }
        }

        /// <summary>Weapon tower levels per damage type (bullet, electric, fire) in the rooms the monster has seen; unknown = living residents' rooms not yet seen.</summary>
        public float[] ScoutTally(Monster m, out int unknownRooms)
        {
            var tally = new float[3];
            unknownRooms = 0;
            foreach (var room in RoomsByDef.Values)
            {
                if (room.Owner == null || !room.Owner.Alive || room == m.Lair) continue;
                if (!m.Scouted.Contains(room)) { unknownRooms++; continue; }
                foreach (var t in room.Slots)
                {
                    if (t == null || !t.IsWeapon) continue;
                    int type = DamageTypes.Index(t.Def.damageType);
                    if (type >= 0 && type <= DamageTypes.Fire) tally[type] += t.Level;
                }
            }
            return tally;
        }

        // ------------------------------------------------------------------ rifts

        /// <summary>Minions one rift releases in a night: perDoor * (starting residents / alive) ^ exponent, rounded.</summary>
        public static int RiftBatch(int perDoor, int residents, int alive, float exponent)
        {
            if (alive <= 0 || perDoor <= 0) return 0;
            return Mathf.RoundToInt(perDoor * Mathf.Pow(Mathf.Max(residents, alive) / (float)alive, exponent));
        }

        /// <summary>Opens a rift outside every living resident's door (at each night start and on awakening).</summary>
        void StartNightRifts()
        {
            var m = Monster;
            foreach (var rift in Rifts) if (rift.Sr != null) RemoveObject(rift.Sr.gameObject);
            Rifts.Clear();
            pulsesFired = 0;
            if (m == null || !HordeAwake(m)) return;
            // Awakened mid-night: the pulses already gone are skipped.
            var pulses = Cfg.minions.pulseSeconds;
            float elapsed = Phase == Phase.Night ? Cfg.match.nightSeconds - PhaseTimer : 0f;
            while (pulsesFired < pulses.Length - 1 && elapsed > pulses[pulsesFired] + 1f) pulsesFired++;
            foreach (var room in RoomsByDef.Values.Where(r => r.Owner != null && r.Owner.Alive && r != m.Lair))
            {
                var rift = new Rift { Room = room, Pos = RiftSpot(room) };
                if (!Simulation && matchRoot != null)
                {
                    rift.Sr = MakeSprite("Rift", Sprites.Ring, rift.Pos, OrderFor(rift.Pos.y) - 30, matchRoot);
                    var c = ColorOf(m); rift.Sr.color = new Color(c.r, c.g, c.b, .7f);
                }
                Rifts.Add(rift);
            }
        }

        Vector2 RiftSpot(Room room)
        {
            var outside = HotelMap.Center(room.Def.DoorOutside);
            var normal = (Vector2)(room.Def.DoorInside - room.Def.DoorTile);
            var tangent = new Vector2(normal.y, -normal.x);
            foreach (float side in new[] { 1f, -1f })
            {
                var p = outside + tangent * side - normal * 0.2f;
                if (CanStand(p, MonsterWalkable, Cfg.minions.radius)) return p;
            }
            return outside;
        }

        /// <summary>A dead resident's rift closes and the minions it sent crumble (escorts stay with the monster).</summary>
        void CloseRift(Resident r)
        {
            for (int i = Rifts.Count - 1; i >= 0; i--)
            {
                if (Rifts[i].Room != r.Room) continue;
                if (Rifts[i].Sr != null) RemoveObject(Rifts[i].Sr.gameObject);
                Rifts.RemoveAt(i);
            }
            foreach (var n in Minions) if (!n.Dead && n.Rift != null && n.Rift.Room == r.Room) RemoveMinion(n);
        }

        /// <summary>Widow Mildred's Graveroot: the nearest rift doubles its next pulse and heals minions around it.</summary>
        bool PlantGraveroot(Monster m, AbilityDef a)
        {
            Rift best = null;
            float bestD = 8f;
            foreach (var rift in Rifts)
            {
                float d = Vector2.Distance(rift.Pos, m.Pos);
                if (d < bestD) { bestD = d; best = rift; }
            }
            if (best == null) return false;
            foreach (var rift in Rifts) rift.Shrine = false;
            best.Shrine = true;
            best.Boost = true;
            return true;
        }

        // ------------------------------------------------------------------ pulses

        /// <summary>Seconds until the next pulse tonight, or -1 when none is left.</summary>
        public float NextPulseIn()
        {
            var pulses = Cfg.minions.pulseSeconds;
            if (Phase != Phase.Night || pulsesFired >= pulses.Length) return -1f;
            return Mathf.Max(0f, pulses[pulsesFired] - (Cfg.match.nightSeconds - PhaseTimer));
        }

        void UpdateMinions(float dt, float now)
        {
            var m = Monster;
            if (m == null) return;
            if (Phase == Phase.Night && HordeAwake(m))
            {
                var pulses = Cfg.minions.pulseSeconds;
                float elapsed = Cfg.match.nightSeconds - PhaseTimer;
                while (pulsesFired < pulses.Length && elapsed >= pulses[pulsesFired])
                {
                    ReleasePulse(m, pulsesFired, pulses.Length);
                    pulsesFired++;
                }
                // Mildred's mourning moss: evolved escorts heal her while she is near.
                foreach (var n in Minions)
                    if (!n.Dead && n.IsEscort && n.Evolved && n.Creature.evolve == "regenAura" && !m.Dead && Vector2.Distance(n.Pos, m.Pos) <= 2f)
                    { m.Hp = Mathf.Min(MaxHp(m), m.Hp + MaxHp(m) * .02f * dt); break; }
            }
            foreach (var rift in Rifts)
                if (rift.Sr != null)
                {
                    // Lies flat on the hallway floor, like the spectral seams.
                    rift.Sr.transform.localScale = Vector3.one * ((rift.Shrine ? 1.8f : 1.2f) / Mathf.Max(.01f, rift.Sr.sprite.bounds.size.x)) * (1f + .08f * Mathf.Sin(now * 4f));
                    rift.Sr.enabled = IsVisible(rift.Pos);
                }
            for (int i = 0; i < Minions.Count; i++) if (!Minions[i].Dead) StepMinion(Minions[i], dt, now);
            Minions.RemoveAll(n => n.Dead);
        }

        /// <summary>Pulse k of P: each rift sends its share of the active creature's nightly batch (read now, so a switch shows
        /// from the next pulse). Escorts instead top up around the monster from the nearest rift.</summary>
        void ReleasePulse(Monster m, int k, int pulses)
        {
            // A queued creature takes over now; switching then waits for the next night.
            if (m.QueuedMinion >= 0 && m.QueuedMinion != m.ActiveMinion && MinionOwned(m, m.QueuedMinion))
            {
                m.ActiveMinion = m.QueuedMinion;
                m.SwitchedNight = Night;
            }
            m.QueuedMinion = -1;
            int active = m.ActiveMinion;
            var creature = Creature(m, active);
            if (creature.role == "escort")
            {
                int alive = Minions.Count(n => !n.Dead && n.IsEscort);
                var from = Rifts.OrderBy(r => Vector2.Distance(r.Pos, m.Pos)).FirstOrDefault();
                var at = from != null ? from.Pos : m.Lair != null ? HotelMap.Center(m.Lair.Def.DoorInside) : m.Pos;
                for (int e = alive; e < EscortCap(m, active) && Minions.Count < Cfg.minions.nightCeiling; e++) SpawnMinion(m, active, null, at);
                return;
            }
            int living = Rifts.Count;
            int batch = RiftBatch(PerDoor(m, active), Cfg.match.residentCount, living, Cfg.minions.aliveExponent);
            if (batch * living > Cfg.minions.nightCeiling && living > 0) batch = Cfg.minions.nightCeiling / living;
            int share = batch * (k + 1) / pulses - batch * k / pulses;
            foreach (var rift in Rifts.ToArray())
            {
                int count = share;
                if (rift.Boost && count > 0) { count *= 2; rift.Boost = false; }
                for (int s = 0; s < count && Minions.Count < Cfg.minions.nightCeiling; s++) SpawnMinion(m, active, rift, rift.Pos);
            }
        }

        Minion SpawnMinion(Monster m, int index, Rift rift, Vector2 at, bool child = false)
        {
            var creature = Creature(m, index);
            var pos = at + Random.insideUnitCircle * 0.3f;
            if (!CanStand(pos, MonsterWalkable, Cfg.minions.radius)) pos = at;
            float hp = MinionHealth(m, index) * (child ? .5f : 1f);
            var n = new Minion
            {
                Creature = creature, Index = index, Evolved = MinionEvolved(m, index), Child = child, Rift = creature.role == "escort" ? null : rift,
                Pos = pos, Hp = hp, MaxHp = hp, Damage = MinionHitDamage(m, index), Resist = ResistOf(creature), NextAttackAt = Now + 0.4f,
            };
            if (!Simulation && matchRoot != null)
            {
                var art = Sprites.Minion(MinionLine(m).id, creature.role);
                n.Sr = MakeSprite("Minion", art ?? Sprites.Ghost, pos, OrderFor(pos.y), matchRoot);
                if (art == null) n.Sr.color = Color.Lerp(Color.white, ColorOf(m), .7f);
            }
            Minions.Add(n);
            Metrics.MinionsSpawned++;
            return n;
        }

        // ------------------------------------------------------------------ behaviour

        void StepMinion(Minion n, float dt, float now)
        {
            var m = Monster;
            if (now < n.BurnUntil) DamageMinion(n, n.BurnDps * dt, n.BurnSource);
            if (n.Dead) return;
            if (n.Rift != null && n.Rift.Shrine && Vector2.Distance(n.Pos, n.Rift.Pos) <= ShrineRadius())
                n.Hp = Mathf.Min(n.MaxHp, n.Hp + n.MaxHp * ShrineRegen() * dt);
            if (n.IsEscort) { StepEscort(n, m, dt, now); return; }

            var room = n.Rift != null ? n.Rift.Room : null;
            var owner = room?.Owner;
            if (owner == null || !owner.Alive) { RemoveMinion(n); return; }
            bool stunned = now < n.StunUntil;
            if (!stunned && TryBite(n, owner, now)) { PlaceMinion(n, now); return; }
            Vector2Int goal;
            if (room.DoorBlocks)
            {
                if (!stunned && Vector2.Distance(n.Pos, HotelMap.Center(room.Def.DoorTile)) <= 1.3f)
                {
                    HitDoor(n, room, now);
                    PlaceMinion(n, now);
                    return;
                }
                goal = room.Def.DoorOutside;
            }
            else goal = HotelMap.ToTile(owner.Pos);
            Walk(n, goal, stunned, dt, now);
        }

        /// <summary>Escorts stay at the monster's side, bite residents beside it and help on the door it is breaking.</summary>
        void StepEscort(Minion n, Monster m, float dt, float now)
        {
            bool stunned = now < n.StunUntil;
            if (!stunned)
            {
                Resident prey = null;
                float best = Cfg.minions.reachTiles;
                foreach (var r in Residents)
                    if (r.Alive && Vector2.Distance(n.Pos, r.Pos) <= best && ClearLine(n.Pos, r.Pos)) { best = Vector2.Distance(n.Pos, r.Pos); prey = r; }
                if (prey != null && TryBite(n, prey, now)) { PlaceMinion(n, now); return; }
                var door = m != null && !m.Dead ? m.AttackingRoom : null;
                if (door != null && door.DoorBlocks && door.Owner != null && door.Owner.Alive && Vector2.Distance(n.Pos, HotelMap.Center(door.Def.DoorTile)) <= 1.3f)
                {
                    HitDoor(n, door, now);
                    PlaceMinion(n, now);
                    return;
                }
            }
            if (m == null || m.Dead || Vector2.Distance(n.Pos, m.Pos) <= Cfg.minions.escortFollowTiles) { PlaceMinion(n, now); return; }
            Walk(n, HotelMap.ToTile(m.Pos), stunned, dt, now);
        }

        bool TryBite(Minion n, Resident r, float now)
        {
            if (Vector2.Distance(n.Pos, r.Pos) > Cfg.minions.reachTiles || !ClearLine(n.Pos, r.Pos)) return false;
            n.Facing = (r.Pos - n.Pos).normalized;
            if (now < n.NextAttackAt) return true;
            n.NextAttackAt = now + n.Creature.interval;
            if (n.Evolved && n.Creature.evolve == "tongue") r.SlowUntil = Mathf.Max(r.SlowUntil, now + 1f);
            DamageResident(r, n.Damage * n.Creature.residentMultiplier, false);
            return true;
        }

        void HitDoor(Minion n, Room room, float now)
        {
            if (now < n.NextAttackAt) return;
            n.NextAttackAt = now + n.Creature.interval;
            float hit = n.Damage * n.Creature.doorMultiplier;
            if (n.Evolved && n.Creature.evolve == "rotDoor") { hit *= 1.4f; room.RotUntil = Mathf.Max(room.RotUntil, now + 4f); }
            bool wasShut = room.DoorBlocks;
            DamageDoor(room, hit, false);
            if (!wasShut || !room.DoorBroken || !n.Evolved) return;
            if (n.Creature.evolve == "dropPart" && Parts.Count < Cfg.match.bodyPartsPerNight) AddPartAt(room.Def.DoorOutside);
            if (n.Creature.evolve == "shatter" && room.Owner != null && room.Owner.Alive) room.Owner.StunUntil = now + 1.5f;
        }

        void Walk(Minion n, Vector2Int goal, bool stunned, float dt, float now)
        {
            float speed = stunned ? 0f : n.Creature.speed * (now < n.SlowUntil ? 1f - n.SlowPct : 1f);
            var move = n.Navigator.Steer(ref n.Pos, goal, MonsterWalkable, Cfg.minions.radius, dt, now, Walls);
            if (move.sqrMagnitude > 0.0001f) n.Facing = move.normalized;
            // Spread out a little so a batch does not stack into one sprite.
            foreach (var other in Minions)
            {
                if (other == n || other.Dead) continue;
                var away = n.Pos - other.Pos;
                float d = away.magnitude;
                if (d > 0.001f && d < 0.4f) move += away / d * (0.4f - d) * 2f;
            }
            n.Pos = Slide(n.Pos, Vector2.ClampMagnitude(move, 1f) * speed * dt, MonsterWalkable, Cfg.minions.radius);
            PlaceMinion(n, now);
        }

        void PlaceMinion(Minion n, float now)
        {
            if (n.Sr == null) return;
            HotelView3D.Billboard(n.Sr, n.Pos);
            float hop = Mathf.Abs(Mathf.Sin(now * 9f + n.Pos.x)) * .06f;
            float size = n.IsEscort ? .62f : n.Creature.role == "breacher" ? .5f : .38f;
            if (n.Child) size *= .7f;
            n.Sr.transform.localScale = HotelView3D.SpriteScale * size * (1f + hop);
            n.Sr.flipX = n.Facing.x < 0f;
            n.Sr.enabled = IsVisible(n.Pos);
        }

        float ShrineRadius() => Array.Find(Cfg.abilities.abilities, a => a.id == "graveroot")?.radius ?? 3f;
        float ShrineRegen() => Array.Find(Cfg.abilities.abilities, a => a.id == "graveroot")?.value ?? 0f;

        // ------------------------------------------------------------------ escorts guarding the monster

        /// <summary>An escort near the monster that steps in front of a tower shot aimed at the monster, or null.</summary>
        Minion InterceptingEscort(Monster m)
        {
            foreach (var n in Minions)
            {
                if (n.Dead || !n.IsEscort || Vector2.Distance(n.Pos, m.Pos) > Cfg.minions.interceptRangeTiles) continue;
                bool catchAll = n.Evolved && n.Creature.evolve == "catchAll";
                if (catchAll || Random.value < n.Creature.interceptChance) return n;
            }
            return null;
        }

        /// <summary>An evolved taunting escort a tower must shoot first, when it is in range and close to that tower.</summary>
        Minion TauntingEscort(Vector2 towerPos, TowerDef def, int level)
        {
            foreach (var n in Minions)
            {
                if (n.Dead || !n.IsEscort || !n.Evolved || n.Creature.evolve != "taunt") continue;
                float d = Vector2.Distance(towerPos, n.Pos);
                if (d <= Cfg.minions.tauntRangeTiles && Rules.UpgradeRules.InRange(Cfg.towers, def, level, d)) return n;
            }
            return null;
        }

        // ------------------------------------------------------------------ damage

        /// <summary>Tower damage to a minion (already multiplied by its resistance).</summary>
        public void DamageMinion(Minion n, float amount, Resident source)
        {
            if (n.Dead || amount <= 0f) return;
            n.Hp -= amount;
            if (n.Hp <= 0f) MinionDies(n);
        }

        void MinionDies(Minion n)
        {
            if (n.Dead) return;
            RemoveMinion(n);
            Metrics.MinionsKilled++;
            if (!n.Evolved) return;
            float now = Now;
            switch (n.Creature.evolve)
            {
                case "deathSlow":
                    foreach (var r in Residents) if (r.Alive && Vector2.Distance(r.Pos, n.Pos) <= 2f) r.SlowUntil = now + 2f;
                    break;
                case "split":
                    if (n.Child || Monster == null || (n.Rift != null && !Rifts.Contains(n.Rift))) break;
                    for (int k = 0; k < 2; k++) SpawnMinion(Monster, n.Index, n.Rift, n.Pos, child: true);
                    break;
            }
        }

        void RemoveMinion(Minion n)
        {
            n.Dead = true;
            if (n.Sr != null) RemoveObject(n.Sr.gameObject);
            n.Sr = null;
        }

        void ClearMinions()
        {
            foreach (var n in Minions) if (n.Sr != null) RemoveObject(n.Sr.gameObject);
            Minions.Clear();
            foreach (var rift in Rifts) if (rift.Sr != null) RemoveObject(rift.Sr.gameObject);
            Rifts.Clear();
            Array.Clear(KnownMinionTypes, 0, KnownMinionTypes.Length);
            pulsesFired = 0;
        }
    }
}
