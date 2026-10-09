using BadAppleHotel.Rules;
using UnityEngine;

namespace BadAppleHotel.Game
{
    public partial class GameManager
    {
        /// <summary>Range in tiles for this tower at its level (short / mid / long from towers.json).</summary>
        public float TowerRange(TowerInstance t) => UpgradeRules.TowerRange(Cfg.towers, t.Def, t.Level);

        public float RangeOf(Config.TowerDef def, int level) => UpgradeRules.TowerRange(Cfg.towers, def, level);

        /// <summary>Weapon damage multiplier for a room: awake owners hit harder.</summary>
        public float OwnerDamageMult(Room room) =>
            room.Owner != null && room.Owner.Alive && !room.Owner.Asleep ? Cfg.residents.awakeWeaponDamageMultiplier : 1f;

        /// <summary>Monster targets count this much closer than they are, so towers favour the monster over a minion beside it.</summary>
        const float MonsterPriorityTiles = 1.5f;

        /// <summary>Every weapon fires on its own at the nearest target in range: the monster, or one of its minions.</summary>
        void UpdateTowers(float dt, float now)
        {
            var m = Monster;
            bool night = Phase == Phase.Night;
            bool monsterActive = night && m != null && !m.Dead && now >= m.CloakUntil;

            foreach (var room in RoomsByDef.Values)
            {
                if (room.Owner == null || !room.Owner.Alive) continue;
                float mult = OwnerDamageMult(room);
                foreach (var t in room.Slots)
                {
                    if (t == null || t.Decoy || !t.IsWeapon) continue;
                    t.Cooldown = Mathf.Max(0f, t.Cooldown - dt);
                    if (!night || now < t.DisabledUntil) continue;
                    var tpos = HotelMap.Center(t.Tile);
                    float best = float.MaxValue;
                    bool monsterInRange = false;
                    if (monsterActive)
                    {
                        float d = Vector2.Distance(tpos, m.Pos);
                        if (UpgradeRules.InRange(Cfg.towers, t.Def, t.Level, d)) { monsterInRange = true; best = d - MonsterPriorityTiles; }
                    }
                    // An evolved taunting escort close by draws the fire first.
                    Minion minion = TauntingEscort(tpos, t.Def, t.Level);
                    if (minion != null) monsterInRange = false;
                    else foreach (var n in Minions)
                    {
                        if (n.Dead) continue;
                        float d = Vector2.Distance(tpos, n.Pos);
                        if (d < best && UpgradeRules.InRange(Cfg.towers, t.Def, t.Level, d)) { best = d; minion = n; }
                    }
                    if (minion == null && !monsterInRange) continue;
                    var aim = minion != null ? minion.Pos : m.Pos;
                    if (!Simulation && t.Sr != null)
                    {
                        var facing = TowerDirections.Get(t.Def.id,TowerForm(t),aim-tpos);
                        if(facing!=null)t.Sr.sprite=facing;
                    }
                    if(t.Cooldown>0f)continue;
                    if (minion != null) FireAtMinion(t, room, tpos, minion, now, mult);
                    else if (InterceptingEscort(m) is Minion guard)
                        FireAtMinion(t, room, tpos, guard, now, mult);   // an escort steps into the shot
                    else
                    {
                        Fire(t, room, tpos, m, now, mult);
                        if (m.Dead) monsterActive = false;
                    }
                }
            }
        }

        void JoinTowerAttack(Room room, Vector2 target)
        {
            var owner = room.Owner;
            if (owner == null || !owner.Alive || owner.Asleep || owner.IsHuman) return;
            owner.AttackUntil = Now + 0.45f;   // bots join in; the player's attack is an explicit action
            var to = target - owner.Pos;
            if (to.sqrMagnitude > 0.01f) owner.Facing = to.normalized;
        }

        void Fire(TowerInstance t, Room room, Vector2 tpos, Monster m, float now, float ownerMult)
        {
            float rate = UpgradeRules.FireRate(Cfg.towers, t.Def, t.Level);
            t.Cooldown = rate > 0f ? 1f / rate : 1f;
            int type = DamageTypes.Index(t.Def.damageType);
            if (room.Owner != null && room.Owner.IsHuman) LearnDamageType(type);
            float mult = DamageTaken(m, type) * UpgradeRules.DistanceBonus(t.Def,Vector2.Distance(tpos,m.Pos)) * PhaseBonus(m, room);
            SpawnProjectile(tpos, m.Pos + Vector2.up * 0.5f, t.Def.damageType, m);
            JoinTowerAttack(room, m.Pos);

            if (type == DamageTypes.Slow)
            {
                m.SlowPct = Mathf.Clamp(UpgradeRules.SlowPct(Cfg.towers, t.Def, t.Level) * mult, 0f, 0.75f);
                m.SlowUntil = now + (UpgradeRules.Tier(Cfg.towers, t.Def, t.Level)?.slowSeconds ?? t.Def.slowSeconds);
                return;
            }

            float dmg = UpgradeRules.Damage(Cfg.towers, t.Def, t.Level) * mult * ownerMult;
            if (type == DamageTypes.Bullet && now < m.JamUntil && Vector2.Distance(tpos, m.Pos) <= m.JamRadius)
                dmg *= m.JamValue;
            if (type == DamageTypes.Electric && now >= m.StunImmuneUntil && (UpgradeRules.Tier(Cfg.towers, t.Def, t.Level)?.stunSeconds ?? t.Def.stunSeconds) > 0f)
            {
                m.StunUntil = now + Mathf.Min(Cfg.progression.maxStunSeconds, UpgradeRules.Tier(Cfg.towers, t.Def, t.Level)?.stunSeconds ?? t.Def.stunSeconds);
                m.StunImmuneUntil = m.StunUntil + Cfg.progression.stunRecoverySeconds;
            }
            if (type == DamageTypes.Fire && (UpgradeRules.Tier(Cfg.towers, t.Def, t.Level)?.burnSeconds ?? t.Def.burnSeconds) > 0f)
            {
                m.BurnDps = UpgradeRules.BurnDamage(Cfg.towers, t.Def, t.Level) * mult * ownerMult;
                m.BurnUntil = now + (UpgradeRules.Tier(Cfg.towers, t.Def, t.Level)?.burnSeconds ?? t.Def.burnSeconds);
                m.BurnSource = room.Owner;
            }
            DamageMonster(dmg, room.Owner);
            Splash(t, room, m.Pos, null, now, ownerMult);
        }

        /// <summary>Area towers (areaRadius in towers.json) also hit every minion around the point they strike.</summary>
        void Splash(TowerInstance t, Room room, Vector2 at, Minion primary, float now, float ownerMult)
        {
            float radius = t.Def.areaRadius;
            if (radius <= 0f || Minions.Count == 0) return;
            int type = DamageTypes.Index(t.Def.damageType);
            float baseDmg = UpgradeRules.Damage(Cfg.towers, t.Def, t.Level) * ownerMult;
            foreach (var n in Minions.ToArray())
                if (n != primary && !n.Dead && Vector2.Distance(n.Pos, at) <= radius)
                    DamageMinion(n, baseDmg * MinionDamageTaken(n, type), room.Owner);
        }

        /// <summary>A tower shot at a minion, after the horde's chosen resistance (and its weakness).</summary>
        void FireAtMinion(TowerInstance t, Room room, Vector2 tpos, Minion n, float now, float ownerMult)
        {
            float rate = UpgradeRules.FireRate(Cfg.towers, t.Def, t.Level);
            t.Cooldown = rate > 0f ? 1f / rate : 1f;
            int type = DamageTypes.Index(t.Def.damageType);
            if (room.Owner != null && room.Owner.IsHuman && type >= 0) KnownMinionTypes[n.Index * 4 + type] = true;
            SpawnProjectile(tpos, n.Pos + Vector2.up * 0.3f, t.Def.damageType, null, n);
            JoinTowerAttack(room, n.Pos);
            float mult = MinionDamageTaken(n, type) * UpgradeRules.DistanceBonus(t.Def, Vector2.Distance(tpos, n.Pos));
            var tier = UpgradeRules.Tier(Cfg.towers, t.Def, t.Level);
            if (type == DamageTypes.Slow)
            {
                n.SlowPct = Mathf.Clamp(UpgradeRules.SlowPct(Cfg.towers, t.Def, t.Level), 0f, 0.75f);
                n.SlowUntil = now + (tier?.slowSeconds ?? t.Def.slowSeconds);
                return;
            }
            float dmg = UpgradeRules.Damage(Cfg.towers, t.Def, t.Level) * mult * ownerMult;
            float stun = tier?.stunSeconds ?? t.Def.stunSeconds;
            if (type == DamageTypes.Electric && stun > 0f) n.StunUntil = now + Mathf.Min(Cfg.progression.maxStunSeconds, stun);
            float burn = tier?.burnSeconds ?? t.Def.burnSeconds;
            if (type == DamageTypes.Fire && burn > 0f)
            {
                n.BurnDps = UpgradeRules.BurnDamage(Cfg.towers, t.Def, t.Level) * mult * ownerMult;
                n.BurnUntil = now + burn;
                n.BurnSource = room.Owner;
            }
            var at = n.Pos;
            DamageMinion(n, dmg, room.Owner);
            Splash(t, room, at, n, now, ownerMult);
        }

        /// <summary>
        /// Which damage types the player has seen land on the monster this match. A resident starts out not
        /// knowing the monster's weaknesses; each multiplier shows up in the HUD after their first hit of that type.
        /// </summary>
        public readonly bool[] KnownDamageTypes = new bool[4];
        public void LearnDamageType(int type) { if (type >= 0 && type < KnownDamageTypes.Length) KnownDamageTypes[type] = true; }

        /// <summary>Damage per second the monster would take standing at a point (used by the monster bot).</summary>
        public float ThreatAt(Vector2 pos, Monster m)
        {
            float dps = 0f;
            foreach (var room in RoomsByDef.Values)
            {
                if (room.Owner == null || !room.Owner.Alive) continue;
                float own = OwnerDamageMult(room);
                foreach (var t in room.Slots)
                {
                    if (t == null || t.Decoy || !t.IsWeapon || t.Def.damageType == "slow") continue;
                    float distance=Vector2.Distance(HotelMap.Center(t.Tile),pos);
                    if (!UpgradeRules.InRange(Cfg.towers,t.Def,t.Level,distance)) continue;
                    float mult = DamageTaken(m, DamageTypes.Index(t.Def.damageType)) * own * UpgradeRules.DistanceBonus(t.Def,distance);
                    dps += UpgradeRules.Damage(Cfg.towers, t.Def, t.Level) * UpgradeRules.FireRate(Cfg.towers, t.Def, t.Level) * mult;
                    dps += UpgradeRules.BurnDamage(Cfg.towers, t.Def, t.Level) * mult * 0.5f;
                }
            }
            return dps;
        }

        void UpdateTowerBlink(float now)
        {
            float wall = Time.unscaledTime;
            foreach (var room in RoomsByDef.Values)
                foreach (var t in room.Slots)
                {
                    if (t == null || t.Sr == null) continue;
                    bool blink = wall < t.BlinkUntil && Mathf.FloorToInt(wall * 8f) % 2 == 0;
                    t.Sr.color = blink ? (Color)Palette.AppleRed : Color.white;
                }
        }

        void SpawnProjectile(Vector2 from, Vector2 to, string type, Monster target, Minion minion = null)
        {
            if (Simulation || matchRoot == null) return;
            if (TowerAttackFx(from, type, target, minion)) return;
            var sr = MakeSprite("shot", Sprites.Projectile(type), from, 6000, matchRoot);
            projectiles.Add(new Projectile { T = sr.transform, From = from, To = to, Born = Now, Duration = 0.15f });
        }

        void UpdateProjectiles()
        {
            for (int i = projectiles.Count - 1; i >= 0; i--)
            {
                var p = projectiles[i];
                if (p.T == null) { projectiles.RemoveAt(i); continue; }
                float k = (Now - p.Born) / p.Duration;
                if (k >= 1f) { RemoveObject(p.T.gameObject); projectiles.RemoveAt(i); continue; }
                var pos = Vector2.Lerp(p.From, p.To, k);
                p.T.position = new Vector3(pos.x, pos.y, 0f);
            }
        }
    }
}
