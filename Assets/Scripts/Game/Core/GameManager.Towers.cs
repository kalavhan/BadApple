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

        /// <summary>Every weapon fires on its own as soon as the monster is within range, wherever it is.</summary>
        void UpdateTowers(float dt, float now)
        {
            var m = Monster;
            bool active = m != null && !m.Dead && Phase == Phase.Night;
            bool cloaked = active && now < m.CloakUntil;

            foreach (var room in RoomsByDef.Values)
            {
                if (room.Owner == null || !room.Owner.Alive) continue;
                float mult = OwnerDamageMult(room);
                foreach (var t in room.Slots)
                {
                    if (t == null || t.Decoy || !t.IsWeapon) continue;
                    t.Cooldown = Mathf.Max(0f, t.Cooldown - dt);
                    if (!active || cloaked || now < t.DisabledUntil) continue;
                    var tpos = HotelMap.Center(t.Tile);
                    if (!UpgradeRules.InRange(Cfg.towers,t.Def,t.Level,Vector2.Distance(tpos,m.Pos))) continue;
                    if (!Simulation && t.Sr != null)
                    {
                        var facing = TowerDirections.Get(t.Def.id,t.Level,m.Pos-tpos);
                        if(facing!=null)t.Sr.sprite=facing;
                    }
                    if(t.Cooldown>0f)continue;
                    Fire(t, room, tpos, m, now, mult);
                    if (m.Dead) return;
                }
            }
        }

        void Fire(TowerInstance t, Room room, Vector2 tpos, Monster m, float now, float ownerMult)
        {
            var sc = Cfg.towers.levelScaling;
            int lv = t.Level - 1;
            float rate = UpgradeRules.FireRate(Cfg.towers, t.Def, t.Level);
            t.Cooldown = rate > 0f ? 1f / rate : 1f;
            int type = DamageTypes.Index(t.Def.damageType);
            float mult = DamageTaken(m, type) * UpgradeRules.DistanceBonus(t.Def,Vector2.Distance(tpos,m.Pos));
            SpawnProjectile(tpos, m.Pos + Vector2.up * 0.5f, t.Def.damageType);
            var owner = room.Owner;
            if (owner != null && owner.Alive && !owner.Asleep && !owner.IsHuman)
            {
                owner.AttackUntil = Now + 0.45f;   // bots join in; the player's attack is an explicit action
                var toMonster = m.Pos - owner.Pos;
                if (toMonster.sqrMagnitude > 0.01f) owner.Facing = toMonster.normalized;
            }

            if (type == DamageTypes.Slow)
            {
                m.SlowPct = Mathf.Clamp((UpgradeRules.Tier(t.Def, t.Level)?.slowPct ?? t.Def.slowPct * (1f + 0.05f * lv)) * mult, 0f, 0.75f);
                m.SlowUntil = now + (UpgradeRules.Tier(t.Def, t.Level)?.slowSeconds ?? t.Def.slowSeconds);
                return;
            }

            float dmg = UpgradeRules.Damage(Cfg.towers, t.Def, t.Level) * mult * ownerMult;
            if (m.JamAura > 0 && Vector2.Distance(tpos,m.Pos)<5) dmg *= 1f/(1+m.JamAura);
            if (type == DamageTypes.Bullet && now < m.JamUntil && Vector2.Distance(tpos, m.Pos) <= m.JamRadius)
                dmg *= m.JamValue;
            if (type == DamageTypes.Electric && now >= m.StunImmuneUntil && (UpgradeRules.Tier(t.Def, t.Level)?.stunSeconds ?? t.Def.stunSeconds) > 0f)
            {
                m.StunUntil = now + Mathf.Min(Cfg.progression.maxStunSeconds, UpgradeRules.Tier(t.Def, t.Level)?.stunSeconds ?? t.Def.stunSeconds);
                m.StunImmuneUntil = m.StunUntil + Cfg.progression.stunRecoverySeconds;
            }
            if (type == DamageTypes.Fire && (UpgradeRules.Tier(t.Def, t.Level)?.burnSeconds ?? t.Def.burnSeconds) > 0f)
            {
                m.BurnDps = UpgradeRules.BurnDamage(Cfg.towers, t.Def, t.Level) * mult * ownerMult;
                m.BurnUntil = now + (UpgradeRules.Tier(t.Def, t.Level)?.burnSeconds ?? t.Def.burnSeconds);
                m.BurnSource = room.Owner;
            }
            DamageMonster(dmg, room.Owner);
        }

        /// <summary>Damage per second the monster would take standing at a point (used by the monster bot).</summary>
        public float ThreatAt(Vector2 pos, Monster m)
        {
            var sc = Cfg.towers.levelScaling;
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
                    int lv = t.Level - 1;
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

        void SpawnProjectile(Vector2 from, Vector2 to, string type)
        {
            if (Simulation || matchRoot == null) return;
            if (TowerAttackFx(from, type)) return;
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
