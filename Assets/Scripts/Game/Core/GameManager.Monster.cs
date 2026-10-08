using System.Collections.Generic;
using System.Linq;
using BadAppleHotel.Config;
using BadAppleHotel.Rules;
using UnityEngine;

namespace BadAppleHotel.Game
{
    public partial class GameManager
    {
        // ------------------------------------------------------------------ monster stats

        int PartIndex(string id)
        {
            var parts = Cfg.bodyParts.parts;
            for (int i = 0; i < parts.Length; i++) if (parts[i].id == id) return i;
            return -1;
        }

        public int PartCount(Monster m, string id)
        {
            int i = PartIndex(id);
            return i >= 0 && i < m.Parts.Length ? m.Parts[i] : 0;
        }

        float PartValue(string id)
        {
            int i = PartIndex(id);
            return i >= 0 ? Cfg.bodyParts.parts[i].perPart : 0f;
        }

        /// <summary>The bonus a stat track gives at the monster's current rank (0.16 = +16%).</summary>
        public float StatBonus(Monster m, string id)
        {
            var tracks = Cfg.progression.statTracks;
            if (m.StatRanks == null) return 0f;
            for (int i = 0; i < tracks.Length && i < m.StatRanks.Length; i++)
                if (tracks[i].id == id) return m.StatRanks[i] * tracks[i].perRank;
            return 0f;
        }

        float EndlessHealth => Endless ? Cfg.match.endless.healthMultiplier + (Night - 1) * Cfg.match.endless.healthPerNight : 1f;
        float EndlessDamage => Endless ? Cfg.match.endless.damageMultiplier + (Night - 1) * Cfg.match.endless.damagePerNight : 1f;

        public float MaxHp(Monster m) => (m.Def.baseHealth + PartCount(m, "torso") * PartValue("torso")) *
            (1 + (m.Level - 1) * Cfg.progression.autoHealthPerLevel + StatBonus(m, "vitality")) * EndlessHealth;
        public float AttackMult(Monster m) => (1f + PartCount(m, "arm") * PartValue("arm")) *
            (1 + (m.Level - 1) * Cfg.progression.autoAttackPerLevel) * (1 + StatBonus(m, "maw")) * EndlessDamage;
        public float RevealRadius(Monster m) => PartCount(m, "eye") * PartValue("eye");

        /// <summary>Kit rank multiplier for damage (rank 1..3).</summary>
        public float RankDamage(int rank) => Cfg.progression.rankDamage[Mathf.Clamp(rank, 1, 3) - 1];
        float RankCooldown(int rank) => Cfg.progression.rankCooldown[Mathf.Clamp(rank, 1, 3) - 1];

        /// <summary>Seconds between single-target hits, after the Frenzy stat track.</summary>
        public float AttackInterval(Monster m) => m.Def.attackInterval / (1f + StatBonus(m, "frenzy"));
        /// <summary>Damage of one single-target hit on a resident.</summary>
        public float HitDamage(Monster m) => m.Def.attackDamage * RankDamage(m.KitRanks[0]) * AttackMult(m);
        /// <summary>Average damage per second against residents and doors (used by the monster bot).</summary>
        public float ResidentDps(Monster m) => HitDamage(m) / AttackInterval(m);
        public float DoorDps(Monster m) => m.Def.doorDamagePerSecond * m.Def.attackDoorMultiplier * RankDamage(m.KitRanks[0]) * AttackMult(m) * m.Def.attackInterval / AttackInterval(m);

        public float MonsterSpeed(Monster m, float now)
        {
            if (now < m.StunUntil) return 0f;
            float s = m.Def.moveSpeed * (1f + PartCount(m, "leg") * PartValue("leg")) * (1f + StatBonus(m, "stride"));
            if (now < m.SprintUntil) s *= Cfg.monsters.sprintMultiplier;
            if (m.Frenzy) s *= Cfg.progression.frenzyMultiplier;
            if (now < m.SlowUntil) s *= 1f - m.SlowPct;
            return s;
        }

        /// <summary>How much of a tower's damage type the monster takes: its own weakness, then the Hide stat track.</summary>
        public float DamageTaken(Monster m, int type)
        {
            if (type < 0) return 1f;
            var b = m.Def.damageTakenMultiplier;
            switch (type)
            {
                case DamageTypes.Bullet: return b.bullet * (1f - StatBonus(m, "hide"));
                case DamageTypes.Electric: return b.electric * (1f - StatBonus(m, "hide"));
                case DamageTypes.Fire: return b.fire * (1f - StatBonus(m, "hide"));
                default: return b.slow;
            }
        }

        public int GrowthStage(Monster m)
        {
            int n = 0;
            foreach (int level in Cfg.progression.growthLevels) if (m.Level >= level) n++;
            return n;
        }

        // ------------------------------------------------------------------ movement & combat

        void HandleHumanMonsterKeys()
        {
            if (HumanRole != Role.Monster || Monster == null) return;
            if (GameInput.ConsumePressed(KeyCode.Alpha1)) UseAbility(0);
            if (GameInput.ConsumePressed(KeyCode.Alpha2)) UseAbility(1);
            if (GameInput.ConsumePressed(KeyCode.Alpha3)) UseAbility(2);
            if (GameInput.ConsumePressed(KeyCode.Alpha4)) UseAbility(3);
            if (GameInput.ConsumePressed(KeyCode.Alpha5)) UseAbility(4);
        }

        void UpdateMonster(float dt, float now)
        {
            var m = Monster;
            if (m == null) return;
            for (int i = 0; i < m.Cooldowns.Length; i++) m.Cooldowns[i] = Mathf.Max(0f, m.Cooldowns[i] - dt);
            UpdateHazards(dt, now);

            if (m.Dead)
            {
                m.Sr.enabled = false;
                if (now >= m.RespawnAt) Respawn(m);
                return;
            }

            if (now < m.BurnUntil) DamageMonster(m.BurnDps * dt, m.BurnSource);
            if (m.Dead) return;
            EndPhase(m, now);

            Vector2 move = m.IsHuman ? HotelView3D.Move(GameInput.Move) : (m.Ai != null ? m.Ai.Tick(dt, now) : Vector2.zero);
            if (now < m.DashUntil)
            {
                m.Pos = Slide(m.Pos, m.DashVelocity * dt, MonsterWalkable, MonsterRadius);
            }
            else if (move.sqrMagnitude > 0.0001f)
            {
                move = Vector2.ClampMagnitude(move, 1f);
                m.Facing = move.normalized;
                m.Pos = Slide(m.Pos, move * MonsterSpeed(m, now) * dt, MonsterWalkable, MonsterRadius);
            }
            if (!CanStand(m.Pos, MonsterWalkable, MonsterRadius))
                m.Pos = Unstick(m.Pos, MonsterWalkable, MonsterRadius, HotelMap.Center(Map.MonsterSpawn));

            m.AttackingRoom = null;
            m.Biting = null;
            if (Phase == Phase.Night) MonsterAttack(m, now);
            UpdateEating(m, dt);
            UpdateScouting(m, now);

            if (Simulation) return;
            // visuals
            HotelView3D.Billboard(m.Sr, m.Pos);
            if (m.Anim != null)
            {
                bool moving = move.sqrMagnitude > 0.0001f || now < m.DashUntil;
                m.Anim.Drive(HotelView3D.Facing(m.Facing), moving, m.Biting != null || m.AttackingRoom != null);
            }
            else m.Sr.flipX = m.Facing.x < 0f;
            m.Sr.enabled = IsVisible(m.Pos);
            Color tint = Color.white;
            m.Sr.transform.localScale = HotelView3D.SpriteScale * (1 + GrowthStage(m) * 0.06f);
            if (now - m.RevealedAt < 0.8f)
            {
                tint = Color.Lerp(tint, Color.white, 0.5f + 0.5f * Mathf.Sin(now * 40));
                m.Sr.transform.position += new Vector3(Mathf.Sin(now * 60) * 0.08f, 0, 0);
            }
            if (now < m.StunUntil) tint = (Color)Palette.Mint;
            else if (now < m.SlowUntil) tint = new Color(0.75f, 0.9f, 1f);
            else if (now < m.BurnUntil) tint = new Color(1f, 0.8f, 0.55f);
            if (now < m.CloakUntil || now < m.PhaseUntil) tint.a = 0.35f;
            m.Sr.color = tint;
            ContactShadow.Place(m.Sr, m.Pos, true);
        }

        /// <summary>True when nothing solid (wall, closed door, void) sits between two points.</summary>
        public bool ClearLine(Vector2 a, Vector2 b)
        {
            float d = Vector2.Distance(a, b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(d / 0.2f));
            for (int i = 1; i < steps; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)steps);
                int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y);
                var t = Map.Get(x, y);
                if (t == Tile.Wall || t == Tile.Void) return false;
                if (t == Tile.Door)
                {
                    var def = Map.RoomAtDoor(new Vector2Int(x, y));
                    if (def != null && RoomsByDef.TryGetValue(def, out var room) && room.DoorBlocks) return false;
                }
            }
            return true;
        }

        /// <summary>The single-target attack: one hit every AttackInterval on a resident in reach, otherwise on the nearest shut door.</summary>
        void MonsterAttack(Monster m, float now)
        {
            Resident prey = null;
            float best = Cfg.monsters.attackReachTiles;
            foreach (var r in Residents)
            {
                if (!r.Alive) continue;
                float d = Vector2.Distance(m.Pos, r.Pos);
                if (d <= best && ClearLine(m.Pos, r.Pos)) { best = d; prey = r; }
            }
            if (prey != null)
            {
                m.Biting = prey;
                if (prey.Room != null) m.AttackingRoom = prey.Room;
                if (now < m.NextAttackAt) return;
                m.NextAttackAt = now + AttackInterval(m);
                m.HitCount++;
                if (m.Def.stunEveryHits > 0 && m.HitCount % m.Def.stunEveryHits == 0) prey.StunUntil = now + m.Def.stunSeconds;
                if (m.Def.rotSeconds > 0 && prey.Room != null) prey.Room.RotUntil = now + m.Def.rotSeconds;
                DamageResident(prey, HitDamage(m), true);
                return;
            }

            Room door = null;
            float bestD = 1.35f;
            foreach (var r in RoomsByDef.Values)
            {
                if (!r.DoorBlocks || r.Owner == null || !r.Owner.Alive) continue;
                float d = Vector2.Distance(m.Pos, HotelMap.Center(r.Def.DoorTile));
                if (d < bestD) { bestD = d; door = r; }
            }
            if (door == null) return;
            m.AttackingRoom = door;
            if (now < m.NextAttackAt) return;
            m.NextAttackAt = now + AttackInterval(m);
            float rampage = now < m.RampageUntil ? m.RampageValue : 1f;
            float hit = m.Def.doorDamagePerSecond * m.Def.attackInterval * m.Def.attackDoorMultiplier * RankDamage(m.KitRanks[0]) *
                AttackMult(m) * rampage * (m.Frenzy ? Cfg.progression.frenzyMultiplier : 1f);
            if (m.Def.rotSeconds > 0) door.RotUntil = now + m.Def.rotSeconds;
            DamageDoor(door, hit, true);
        }

        /// <summary>Damages a shut door (after its resistance); the monster earns Fear and Dream Power only for its own hits.</summary>
        void DamageDoor(Room door, float raw, bool byMonster)
        {
            if (!door.DoorBlocks) return;
            var m = Monster;
            float dmg = Mathf.Min(door.DoorHp, raw * (1f - Cfg.doors.levels[door.DoorLevel - 1].damageResistancePct));
            door.DoorHp -= dmg;
            if (!door.Owner.IsHuman) Wake(door.Owner);
            RecordAssault(door);
            door.LastAttackedTime = Now;
            if (byMonster && m != null)
            {
                AddFear(m, dmg * Cfg.progression.fear.perDoorDamage);
                m.LastDamageAt = Now; m.Frenzy = false;
                m.DreamPower += dmg * Cfg.economy.monsterIncome.dreamPowerPerDoorDamage;
            }
            if (door.DoorHp > 0f) return;
            door.DoorHp = 0f;
            door.DoorBroken = true;
            Metrics.DoorBreaks++;
            RefreshDoor(door);
            Announce(door.Owner.IsHuman ? "Your door is broken! Rebuild it or fight back!" : door.Owner.Name + "'s door is broken!", 3f);
        }

        /// <summary>Hurts a resident; the monster earns Fear only for damage it deals itself (not its minions).</summary>
        void DamageResident(Resident r, float amount, bool byMonster)
        {
            if (r == null || !r.Alive || amount <= 0f) return;
            var m = Monster;
            float dealt = Mathf.Min(r.Health, amount);
            r.Health -= dealt;
            RecordAssault(r.Room, false);
            if (r.Room != null) r.Room.LastAttackedTime = Now;
            if (r.Asleep && !r.IsHuman) Wake(r);
            if (byMonster && m != null)
            {
                AddFear(m, dealt * Cfg.progression.fear.perResidentDamage);
                m.LastDamageAt = Now; m.Frenzy = false;
            }
            if (r.Health <= 0f) KillResident(r, byMonster);
        }

        void UpdateEating(Monster m, float dt)
        {
            BodyPart near = null;
            foreach (var p in Parts)
                if (Vector2.Distance(m.Pos, HotelMap.Center(p.Tile)) < 0.8f) { near = p; break; }

            if (near == null || m.AttackingRoom != null || m.Biting != null || m.Parts[near.TypeIndex] >= Cfg.bodyParts.maxPartsPerType)
            {
                m.EatingPart = null;
                m.EatProgress = 0f;
                return;
            }
            if (m.EatingPart != near) { m.EatingPart = near; m.EatProgress = 0f; }
            m.EatProgress += dt;
            if (m.EatProgress < Cfg.bodyParts.eatSeconds) return;
            EatPart(m, near);
        }

        void EatPart(Monster m, BodyPart part)
        {
            float oldMax = MaxHp(m);
            m.Parts[part.TypeIndex] = Mathf.Min(Cfg.bodyParts.maxPartsPerType, m.Parts[part.TypeIndex] + 1);
            m.Hp += MaxHp(m) - oldMax;
            m.Faith += Cfg.economy.monsterIncome.faithPerBodyPart;
            AddFear(m, Cfg.progression.fear.perPart);
            Parts.Remove(part);
            if (part.Sr != null) RemoveObject(part.Sr.gameObject);
            m.EatingPart = null;
            m.EatProgress = 0f;
            AddFloater(m.Pos + Vector2.up * 1.5f, "+" + part.Def.name, (Color)Palette.Moss);
            AddLog(m.Def.name + " ate a " + part.Def.name.ToLower() + ".");
        }

        void Respawn(Monster m)
        {
            m.Dead = false;
            m.Pos = m.Lair != null ? HotelMap.Center(m.Lair.Def.BedTile) : HotelMap.Center(Map.MonsterSpawn);
            m.Hp = MaxHp(m);
            Announce(m.Def.name + " crawls back out of the dark...", 3f);
        }

        public void DamageMonster(float amount, Resident source)
        {
            var m = Monster;
            if (m == null || m.Dead || amount <= 0f) return;
            m.Hp -= amount;
            if (source != null) m.LastDamager = source;
            damageAccum += amount;
            if (Time.unscaledTime >= nextDamageFloater)
            {
                if (IsVisible(m.Pos)) AddFloater(m.Pos + Vector2.up * 1.8f, "-" + Mathf.RoundToInt(damageAccum), (Color)Palette.Candle);
                damageAccum = 0f;
                nextDamageFloater = Time.unscaledTime + 0.35f;
            }
            if (m.Hp <= 0f) MonsterDies(source ?? m.LastDamager);
        }

        void MonsterDies(Resident killer)
        {
            var m = Monster;
            m.Dead = true;
            m.Hp = 0f;
            m.RespawnAt = Now + Cfg.match.monsterRespawnSeconds;
            m.Sr.enabled = false;
            if (killer != null && killer.Alive)
            {
                var reward = EconomyRules.MonsterDeathReward(Cfg.economy, m.DreamPower, m.Faith);
                killer.DreamPower += reward.DreamPower;
                killer.Faith += reward.Faith;
                Announce((killer.IsHuman ? "You" : killer.Name) + " banished " + m.Def.name + "! +" +
                         Mathf.RoundToInt(reward.DreamPower) + " Dream Power", 4f);
            }
            else
            {
                Announce(m.Def.name + " was banished!", 3f);
            }
            m.DreamPower = 0f;
            m.Faith = 0f;
            for (int i = 0; i < m.Parts.Length; i++)
                m.Parts[i] = Mathf.FloorToInt(m.Parts[i] * Cfg.bodyParts.keepOnRespawnPct);
            m.SlowUntil = m.StunUntil = m.BurnUntil = m.JamUntil = m.RampageUntil = m.CloakUntil = m.DashUntil = m.PhaseUntil = 0f;
            m.PhasedRoom = null;
            m.EatingPart = null;
            m.EatProgress = 0f;
        }

        void KillResident(Resident r, bool byMonster = true)
        {
            var m = Monster;
            r.Alive = false;
            r.Asleep = false;
            r.Health = 0f;
            // A phased-in porter must not remain sealed inside a dead guest's room.
            if (r.Room != null)
            {
                r.Room.DoorOpen = true; r.Room.CloseWhenClear = false; RefreshDoor(r.Room);
            }
            var reward = EconomyRules.MonsterKillReward(Cfg.economy, r.DreamPower, r.Faith, r.XpValue);
            m.DreamPower += reward.DreamPower + Cfg.economy.monsterIncome.dreamPowerPerResidentKill;
            m.Faith += reward.Faith;
            if (byMonster) AddFear(m, Cfg.progression.fear.perKill);
            else Metrics.KillsByMinions++;
            m.LastKillAt = Now;
            m.Kills++;
            r.DreamPower = 0f;
            r.Faith = 0f;
            CloseRift(r);
            ContactShadow.Place(r.Sr, r.Pos, false);
            r.Sr.sprite = Sprites.Ghost;
            r.Sr.transform.rotation = Quaternion.identity;
            r.Sr.transform.localScale = Vector3.one;
            r.Sr.color = Color.white;
            HotelView3D.Billboard(r.Sr, r.Pos);
            string by = byMonster ? m.Def.name : m.Def.name + "'s minions";
            Announce(r.IsHuman ? "You were eaten. Now you haunt the hallway (spectating)." : r.Name + " was eaten by " + by + "!", 4f);
            AddLog(r.Name + " was eaten.");
        }

        // ------------------------------------------------------------------ monster actions

        /// <summary>Uses loadout slot i: 0 is the area attack, 1 the signature special (from specialLevel), then utility picks.</summary>
        public bool UseAbility(int i)
        {
            var m = Monster;
            if (m == null || m.Dead || Phase != Phase.Night) return false;
            if (i < 0 || i >= m.Loadout.Length || m.Cooldowns[i] > 0f) return false;
            var a = m.Loadout[i];
            float now = Now;
            int rank = a.id == m.Def.area ? m.KitRanks[1] : a.id == m.Def.special ? m.KitRanks[2] : 0;
            float power = rank > 0 ? RankDamage(rank) : 1f;
            switch (a.effect)
            {
                case "towerDamageMultiplier":
                    m.JamUntil = now + a.durationSeconds; m.JamValue = a.value; m.JamRadius = a.radius;
                    break;
                case "doorDamageMultiplier":
                    m.RampageUntil = now + a.durationSeconds; m.RampageValue = a.value;
                    break;
                case "faithIncomeMultiplier":
                    foreach (var r in Residents)
                        if (r.Alive && r.Room != null && Vector2.Distance(m.Pos, HotelMap.Center(r.Room.Def.DoorTile)) <= a.radius)
                            r.FaithBlockedUntil = now + a.durationSeconds;
                    break;
                case "bedIncomeMultiplier":
                    foreach (var r in Residents)
                        if (r.Alive && r.Room != null && Vector2.Distance(m.Pos, HotelMap.Center(r.Room.Def.DoorTile)) <= a.radius)
                        { r.BedSlowUntil = now + a.durationSeconds; r.BedSlowValue = a.value; }
                    break;
                case "dash":
                    m.DashVelocity = m.Facing.normalized * (a.value / 0.3f);
                    m.DashUntil = now + 0.3f;
                    break;
                case "towerUntargetable":
                    m.CloakUntil = now + a.durationSeconds;
                    break;
                case "residentSlowZone":
                    m.SlowZoneUntil = now + a.durationSeconds; break;
                case "flambe":
                    HitResidentsAround(m, a.radius, a.damage * power * AttackMult(m));
                    foreach (var room in RoomsByDef.Values)
                        if (room.Owner != null && room.Owner.Alive && room.DoorBlocks &&
                            Vector2.Distance(m.Pos, HotelMap.Center(room.Def.DoorTile)) <= a.radius)
                            DamageDoor(room, a.doorDamage * power * AttackMult(m), true);
                    AddHazard(m.Pos, a.radius * 0.8f, a.durationSeconds, a.dps * power, 0f, false, ColorOf(m));
                    break;
                case "sporeBloom":
                    AddHazard(m.Pos, a.radius, a.durationSeconds, a.dps * power, a.value, true, ColorOf(m));
                    break;
                case "lastCall":
                    HitResidentsAround(m, a.radius, a.damage * power * AttackMult(m));
                    foreach (var room in RoomsByDef.Values)
                        foreach (var t in room.Slots)
                            if (t != null && t.IsWeapon && Vector2.Distance(HotelMap.Center(t.Tile), m.Pos) <= a.radius)
                                t.DisabledUntil = Mathf.Max(t.DisabledUntil, now + a.durationSeconds);
                    break;
                case "meatHook":
                    if (!MeatHook(m, a)) return false;
                    break;
                case "graveroot":
                    if (!PlantGraveroot(m, a)) return false;
                    break;
                case "doNotDisturb":
                    if (!PhaseThroughDoor(m, a)) return false;
                    break;
                default:
                    AddLog(a.name + " is not in the demo yet.");
                    return false;
            }
            m.Cooldowns[i] = a.cooldownSeconds * (rank > 0 ? RankCooldown(rank) : 1f);
            if (IsVisible(m.Pos)) AddFloater(m.Pos + Vector2.up * 2.2f, a.name + "!", ColorOf(m));
            if (!m.IsHuman) AddLog(m.Def.name + " used " + a.name + ".");
            return true;
        }

        public Color ColorOf(Monster m) => ColorUtility.TryParseHtmlString(m.Def.color, out var c) ? c : (Color)Palette.Mint;

        void HitResidentsAround(Monster m, float radius, float damage)
        {
            // Copy: a kill removes nobody from the list, but stays safe if that ever changes.
            foreach (var r in Residents.ToArray())
                if (r.Alive && Vector2.Distance(r.Pos, m.Pos) <= radius && ClearLine(m.Pos, r.Pos))
                    DamageResident(r, damage, true);
        }

        /// <summary>Maître Gorge: reel in a body part and eat it on the spot, or yank a resident out of an open doorway.</summary>
        bool MeatHook(Monster m, AbilityDef a)
        {
            BodyPart part = null;
            float best = a.radius;
            foreach (var p in Parts)
            {
                float d = Vector2.Distance(m.Pos, HotelMap.Center(p.Tile));
                if (d <= best && m.Parts[p.TypeIndex] < Cfg.bodyParts.maxPartsPerType && ClearLine(m.Pos, HotelMap.Center(p.Tile))) { best = d; part = p; }
            }
            if (part != null) { EatPart(m, part); return true; }

            Resident prey = null;
            best = a.radius;
            foreach (var r in Residents)
            {
                if (!r.Alive) continue;
                float d = Vector2.Distance(m.Pos, r.Pos);
                if (d > Cfg.monsters.attackReachTiles && d <= best && ClearLine(m.Pos, r.Pos)) { best = d; prey = r; }
            }
            if (prey == null) return false;
            var pull = (m.Pos - prey.Pos).normalized * Mathf.Min(a.value, best - Cfg.monsters.attackReachTiles * 0.8f);
            var victim = prey;
            prey.Pos = Slide(prey.Pos, pull, (x, y) => WalkableFor(victim, x, y) || MonsterWalkable(x, y), ResidentRadius);
            prey.Asleep = false; prey.SleepRequested = false;
            prey.StunUntil = Now + 0.6f;
            return true;
        }

        /// <summary>The Night Porter: slip through a shut door without breaking it, for a few seconds.</summary>
        bool PhaseThroughDoor(Monster m, AbilityDef a)
        {
            Room target = null;
            float best = a.radius;
            foreach (var room in RoomsByDef.Values)
            {
                if (!room.DoorBlocks || room.Owner == null || !room.Owner.Alive) continue;
                float d = Vector2.Distance(m.Pos, HotelMap.Center(room.Def.DoorTile));
                if (d <= best && CanStand(HotelMap.Center(room.Def.DoorInside), MonsterWalkable, MonsterRadius)) { best = d; target = room; }
            }
            if (target == null) return false;
            m.Pos = HotelMap.Center(target.Def.DoorInside);
            m.PhasedRoom = target;
            m.PhaseUntil = Now + a.durationSeconds;
            return true;
        }

        /// <summary>Towers inside the room the porter phased into hit him harder while he is there.</summary>
        public float PhaseBonus(Monster m, Room towerRoom)
        {
            if (m.PhasedRoom == null || towerRoom != m.PhasedRoom || Now >= m.PhaseUntil) return 1f;
            var a = System.Array.Find(Cfg.abilities.abilities, x => x.id == m.Def.special);
            return a != null ? a.value : 1f;
        }

        void EndPhase(Monster m, float now)
        {
            if (m.PhasedRoom == null || now < m.PhaseUntil) return;
            var room = m.PhasedRoom;
            m.PhasedRoom = null;
            if (room.DoorBlocks && room.Def.ContainsInterior(HotelMap.ToTile(m.Pos)))
                m.Pos = HotelMap.Center(room.Def.DoorOutside);
        }

        // ------------------------------------------------------------------ hazards

        public readonly List<HazardZone> Hazards = new List<HazardZone>();

        void AddHazard(Vector2 pos, float radius, float seconds, float dps, float slow, bool noDreams, Color color)
        {
            if (seconds <= 0f) return;
            var z = new HazardZone { Pos = pos, Radius = radius, Until = Now + seconds, Dps = dps, SlowPct = slow, NoDreams = noDreams, Color = color };
            if (!Simulation && matchRoot != null)
            {
                z.Sr = MakeSprite("Hazard", Sprites.Ring, pos, OrderFor(pos.y) - 20, matchRoot);
                float size = Mathf.Max(0.01f, z.Sr.sprite.bounds.size.x);
                z.Sr.transform.localScale = Vector3.one * (radius * 2f / size);
                z.Sr.color = new Color(color.r, color.g, color.b, 0.55f);
            }
            Hazards.Add(z);
        }

        void UpdateHazards(float dt, float now)
        {
            for (int i = Hazards.Count - 1; i >= 0; i--)
            {
                var z = Hazards[i];
                if (now >= z.Until || Phase != Phase.Night)
                {
                    if (z.Sr != null) RemoveObject(z.Sr.gameObject);
                    Hazards.RemoveAt(i);
                    continue;
                }
                foreach (var r in Residents.ToArray())
                {
                    if (!r.Alive || Vector2.Distance(r.Pos, z.Pos) > z.Radius) continue;
                    if (z.SlowPct > 0f) r.SlowUntil = now + 0.2f;
                    if (z.NoDreams) { r.BedSlowUntil = now + 0.2f; r.BedSlowValue = 0f; }
                    DamageResident(r, z.Dps * dt, true);
                }
                if (z.Sr != null)
                {
                    float left = Mathf.Clamp01((z.Until - now) / 0.6f);
                    z.Sr.color = new Color(z.Color.r, z.Color.g, z.Color.b, 0.55f * left * (0.8f + 0.2f * Mathf.Sin(now * 9f)));
                    z.Sr.enabled = IsVisible(z.Pos);
                }
            }
        }

        void ClearHazards()
        {
            foreach (var z in Hazards) if (z.Sr != null) RemoveObject(z.Sr.gameObject);
            Hazards.Clear();
        }
    }
}
