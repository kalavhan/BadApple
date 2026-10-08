using System;
using System.Collections.Generic;
using System.Linq;
using BadAppleHotel.Config;
using UnityEngine;
using Random = UnityEngine.Random;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// The monster's horde: a rift outside every living resident's door releases a fixed nightly batch in pulses.
    /// Minions chew that door, then attack its resident. The monster buys and evolves them with Fear and picks one
    /// resistance for the whole horde, changeable every few nights.
    /// </summary>
    public partial class GameManager
    {
        public readonly List<Minion> Minions = new List<Minion>();
        public readonly List<Rift> Rifts = new List<Rift>();
        /// <summary>Which damage types the human resident has seen land on a minion (their multiplier shows in the HUD after that).</summary>
        public readonly bool[] KnownMinionTypes = new bool[4];
        int pulsesFired;
        float nextScout;

        // ------------------------------------------------------------------ upgrades

        public MinionLineDef MinionLine(Monster m) => Array.Find(Cfg.minions.lines, l => l.id == m.Def.minionLine);
        int UpgradeIndex(string id) => Array.FindIndex(Cfg.minions.upgrades, u => u.id == id);
        public MinionUpgradeDef MinionUpgrade(string id) => Array.Find(Cfg.minions.upgrades, u => u.id == id);

        public int MinionRank(Monster m, string id)
        {
            int i = UpgradeIndex(id);
            return m == null || i < 0 || m.MinionRanks == null ? 0 : m.MinionRanks[i];
        }

        float MinionBonus(Monster m, string id)
        {
            var u = MinionUpgrade(id);
            return u == null ? 0f : MinionRank(m, id) * u.perRank;
        }

        public bool HordeAwake(Monster m) => MinionRank(m, "awaken") > 0;
        public int HordeSize(Monster m) => Mathf.Min(Cfg.minions.maxHorde, Cfg.minions.baseHorde + Mathf.RoundToInt(MinionBonus(m, "horde")));
        public int MinionFormIndex(Monster m) => Mathf.Min(MinionLine(m).forms.Length - 1, MinionRank(m, "evolve"));
        public float MinionResistance(Monster m) => Cfg.minions.resistByRank[Mathf.Clamp(MinionRank(m, "hide"), 0, Cfg.minions.resistByRank.Length - 1)];

        /// <summary>Ranks bought in the stat upgrades (everything except Awaken and Evolve); evolving needs enough of them.</summary>
        public int CoreMinionRanks(Monster m)
        {
            int n = 0;
            for (int i = 0; i < Cfg.minions.upgrades.Length; i++)
                if (Cfg.minions.upgrades[i].id != "awaken" && Cfg.minions.upgrades[i].id != "evolve") n += m.MinionRanks[i];
            return n;
        }

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
            var m = Monster;
            if (type == n.Resist) return 1f - (m != null ? MinionResistance(m) : Cfg.minions.resistByRank[0]);
            if (type == Weakness(n.Resist)) return 1f + Cfg.minions.weaknessBonus;
            return 1f;
        }

        /// <summary>Fear price of the next rank, or -1 when the upgrade is maxed.</summary>
        public float MinionUpgradeCost(Monster m, string id)
        {
            var u = MinionUpgrade(id);
            if (u == null) return -1f;
            int rank = MinionRank(m, id);
            if (id == "horde" && HordeSize(m) >= Cfg.minions.maxHorde) return -1f;
            if (id == "evolve" && rank >= MinionLine(m).forms.Length - 1) return -1f;
            return rank < u.costs.Length ? u.costs[rank] : -1f;
        }

        /// <summary>Why an upgrade cannot be bought yet (ignoring Fear), or null.</summary>
        public string MinionUpgradeLock(Monster m, string id)
        {
            if (id != "awaken" && !HordeAwake(m)) return "Awaken the horde first";
            var u = MinionUpgrade(id);
            int rank = MinionRank(m, id);
            if (id == "evolve" && u.requiresRanks != null && rank < u.requiresRanks.Length && CoreMinionRanks(m) < u.requiresRanks[rank])
                return "Needs " + u.requiresRanks[rank] + " upgrade ranks";
            return null;
        }

        public ActionResult TryBuyMinionUpgrade(Monster m, string id)
        {
            if (m == null || m.MinionRanks == null) return ActionResult.Invalid;
            float cost = MinionUpgradeCost(m, id);
            if (cost < 0f) return ActionResult.MaxLevel;
            if (MinionUpgradeLock(m, id) != null) return ActionResult.Blocked;
            if (m.Fear + 0.001f < cost) return ActionResult.NoMoney;
            m.Fear -= cost;
            m.MinionRanks[UpgradeIndex(id)]++;
            if (id == "awaken")
            {
                if (m.MinionResist < 0) { m.MinionResist = BestResistGuess(m); m.ResistChosenNight = Mathf.Max(1, Night); }
                if (Phase == Phase.Night) StartNightRifts();
                Announce(m.IsHuman ? "Your horde awakens. Rifts open outside every door." : "Rifts tear open outside every door...", 3f);
            }
            if (id == "evolve") AddFloater(m.Pos + Vector2.up * 2.2f, MinionLine(m).forms[MinionFormIndex(m)].name + "!", ColorOf(m));
            return ActionResult.Ok;
        }

        // ------------------------------------------------------------------ resistance & scouting

        public bool CanSwapMinionResist(Monster m) => m.MinionResist < 0 || Night - m.ResistChosenNight >= Cfg.minions.swapEveryNights;
        public int NightsUntilSwap(Monster m) => Mathf.Max(0, m.ResistChosenNight + Cfg.minions.swapEveryNights - Night);

        public ActionResult TrySetMinionResist(Monster m, int type)
        {
            if (m == null || type < DamageTypes.Bullet || type > DamageTypes.Fire) return ActionResult.Invalid;
            if (type == m.MinionResist) return ActionResult.Ok;
            if (!CanSwapMinionResist(m)) return ActionResult.Blocked;
            m.MinionResist = type;
            m.ResistChosenNight = Mathf.Max(1, Night);
            if (m.IsHuman) Toast("New minions will resist " + DamageTypes.Label(type).ToLower() + ".");
            return ActionResult.Ok;
        }

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

        int BestResistGuess(Monster m)
        {
            var tally = ScoutTally(m, out _);
            int best = DamageTypes.Bullet;
            for (int i = 1; i < 3; i++) if (tally[i] > tally[best]) best = i;
            return best;
        }

        // ------------------------------------------------------------------ rifts

        /// <summary>Minions one rift releases in a night: horde * multiplier * (starting residents / alive) ^ exponent, rounded.</summary>
        public static int RiftBatch(int horde, float multiplier, int residents, int alive, float exponent)
        {
            if (alive <= 0) return 0;
            return Mathf.RoundToInt(horde * multiplier * Mathf.Pow(Mathf.Max(residents, alive) / (float)alive, exponent));
        }

        /// <summary>Opens a rift outside every living resident's door and loads tonight's batch (called at each night start and on awakening).</summary>
        void StartNightRifts()
        {
            var m = Monster;
            foreach (var rift in Rifts) if (rift.Sr != null) RemoveObject(rift.Sr.gameObject);
            Rifts.Clear();
            pulsesFired = 0;
            if (m == null || !HordeAwake(m)) return;
            var living = RoomsByDef.Values.Where(r => r.Owner != null && r.Owner.Alive && r != m.Lair).ToList();
            if (living.Count == 0) return;
            var line = MinionLine(m);
            int batch = RiftBatch(HordeSize(m), line.spawnMultiplier, Cfg.match.residentCount, living.Count, Cfg.minions.aliveExponent);
            if (batch * living.Count > Cfg.minions.nightCeiling) batch = Cfg.minions.nightCeiling / living.Count;
            // Awakened mid-night: skip the pulses already gone and scale the batch to what is left.
            var pulses = Cfg.minions.pulseSeconds;
            float elapsed = Phase == Phase.Night ? Cfg.match.nightSeconds - PhaseTimer : 0f;
            while (pulsesFired < pulses.Length - 1 && elapsed > pulses[pulsesFired] + 1f) pulsesFired++;
            int share = Mathf.CeilToInt(batch * (pulses.Length - pulsesFired) / (float)pulses.Length);
            foreach (var room in living)
            {
                var rift = new Rift { Room = room, Pos = RiftSpot(room), Pending = share };
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

        /// <summary>A dead resident's rift closes and its minions crumble.</summary>
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

        // ------------------------------------------------------------------ minions

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
                    int left = pulses.Length - pulsesFired;
                    foreach (var rift in Rifts.ToArray())
                    {
                        int count = Mathf.CeilToInt(rift.Pending / (float)left);
                        rift.Pending -= count;
                        if (rift.Boost) { count *= 2; rift.Boost = false; }
                        for (int k = 0; k < count && Minions.Count < Cfg.minions.nightCeiling; k++) SpawnMinion(rift, rift.Pos, MinionFormIndex(m));
                    }
                    pulsesFired++;
                }
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

        Minion SpawnMinion(Rift rift, Vector2 at, int formIndex)
        {
            var m = Monster;
            var form = MinionLine(m).forms[Mathf.Clamp(formIndex, 0, MinionLine(m).forms.Length - 1)];
            var pos = at + Random.insideUnitCircle * 0.3f;
            if (!CanStand(pos, MonsterWalkable, Cfg.minions.radius)) pos = at;
            float hp = form.health * (1f + MinionBonus(m, "toughness"));
            var n = new Minion { Form = form, FormIndex = formIndex, Rift = rift, Pos = pos, Hp = hp, MaxHp = hp, Resist = m.MinionResist, NextAttackAt = Now + 0.4f };
            if (!Simulation && matchRoot != null)
            {
                // Stand-in until the minion props arrive: a small spectre in the monster's colour.
                n.Sr = MakeSprite("Minion", Sprites.Ghost, pos, OrderFor(pos.y), matchRoot);
                n.Sr.color = Color.Lerp(Color.white, ColorOf(m), .7f);
            }
            Minions.Add(n);
            Metrics.MinionsSpawned++;
            return n;
        }

        void StepMinion(Minion n, float dt, float now)
        {
            var m = Monster;
            var room = n.Rift.Room;
            var owner = room.Owner;
            if (owner == null || !owner.Alive) { RemoveMinion(n); return; }
            if (now < n.BurnUntil) DamageMinion(n, n.BurnDps * dt, n.BurnSource);
            if (n.Dead) return;
            if (n.Rift.Shrine && Vector2.Distance(n.Pos, n.Rift.Pos) <= ShrineRadius(m))
                n.Hp = Mathf.Min(n.MaxHp, n.Hp + n.MaxHp * ShrineRegen(m) * dt);

            float interval = n.Form.interval / (1f + MinionBonus(m, "frenzy"));
            float damage = n.Form.damage * (1f + MinionBonus(m, "fangs"));
            bool stunned = now < n.StunUntil;
            if (!stunned && Vector2.Distance(n.Pos, owner.Pos) <= Cfg.minions.reachTiles && ClearLine(n.Pos, owner.Pos))
            {
                n.Facing = (owner.Pos - n.Pos).normalized;
                if (now >= n.NextAttackAt) { n.NextAttackAt = now + interval; DamageResident(owner, damage, false); }
                PlaceMinion(n, now);
                return;
            }
            Vector2Int goal;
            if (room.DoorBlocks)
            {
                if (!stunned && Vector2.Distance(n.Pos, HotelMap.Center(room.Def.DoorTile)) <= 1.3f)
                {
                    if (now >= n.NextAttackAt)
                    {
                        n.NextAttackAt = now + interval;
                        DamageDoor(room, damage * n.Form.doorMultiplier, false);
                        if (room.DoorBroken && n.Form.trait == "dropPart" && Parts.Count < Cfg.match.bodyPartsPerNight) AddPartAt(room.Def.DoorOutside);
                    }
                    PlaceMinion(n, now);
                    return;
                }
                goal = room.Def.DoorOutside;
            }
            else goal = HotelMap.ToTile(owner.Pos);

            float speed = stunned ? 0f : n.Form.speed * (1f + MinionBonus(m, "scurry")) * (now < n.SlowUntil ? 1f - n.SlowPct : 1f);
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
            n.Sr.transform.localScale = HotelView3D.SpriteScale * n.Form.scale * (1f + hop);
            n.Sr.flipX = n.Facing.x < 0f;
            n.Sr.enabled = IsVisible(n.Pos);
        }

        float ShrineRadius(Monster m) => Array.Find(Cfg.abilities.abilities, a => a.id == "graveroot")?.radius ?? 3f;
        float ShrineRegen(Monster m) => Array.Find(Cfg.abilities.abilities, a => a.id == "graveroot")?.value ?? 0f;

        /// <summary>Tower damage to a minion (already multiplied by its resistance); the Steamer Trunk swallows its first shot.</summary>
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
            float now = Now;
            switch (n.Form.trait)
            {
                case "deathSlow":
                    foreach (var r in Residents) if (r.Alive && Vector2.Distance(r.Pos, n.Pos) <= 2f) r.SlowUntil = now + 2f;
                    break;
                case "deathBlind":
                    foreach (var room in RoomsByDef.Values)
                        foreach (var t in room.Slots)
                            if (t != null && t.IsWeapon && Vector2.Distance(HotelMap.Center(t.Tile), n.Pos) <= 2.5f)
                                t.DisabledUntil = Mathf.Max(t.DisabledUntil, now + 1f);
                    break;
                case "split":
                case "releaseTwo":
                    if (n.Rift != null && Rifts.Contains(n.Rift))
                        for (int k = 0; k < 2; k++) SpawnMinion(n.Rift, n.Pos, 0);
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
