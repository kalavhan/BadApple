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

        public float MaxHp(Monster m) => m.Def.baseHealth + PartCount(m, "torso") * PartValue("torso");
        public float AttackMult(Monster m) => 1f + PartCount(m, "arm") * PartValue("arm");
        public float RevealRadius(Monster m) => PartCount(m, "eye") * PartValue("eye");

        public float MonsterSpeed(Monster m, float now)
        {
            if (now < m.StunUntil) return 0f;
            float s = m.Def.moveSpeed * (1f + PartCount(m, "leg") * PartValue("leg"));
            if (now < m.SlowUntil) s *= 1f - m.SlowPct;
            return s;
        }

        public float DamageTaken(Monster m, int type)
        {
            if (type < 0) return 1f;
            var b = m.Def.damageTakenMultiplier;
            float baseMult;
            switch (type)
            {
                case DamageTypes.Bullet: baseMult = b.bullet; break;
                case DamageTypes.Electric: baseMult = b.electric; break;
                case DamageTypes.Fire: baseMult = b.fire; break;
                default: baseMult = b.slow; break;
            }
            var track = Cfg.monsters.resistanceTracks.damageTakenMultiplierByLevel;
            int lv = Mathf.Clamp(m.ResistLevels[type], 0, track.Length - 1);
            return baseMult * track[lv];
        }

        // ------------------------------------------------------------------ movement & combat

        void HandleHumanMonsterKeys()
        {
            if (HumanRole != Role.Monster || Monster == null) return;
            if (GameInput.ConsumePressed(KeyCode.Alpha1)) UseAbility(0);
            if (GameInput.ConsumePressed(KeyCode.Alpha2)) UseAbility(1);
            if (GameInput.ConsumePressed(KeyCode.Alpha3)) UseAbility(2);
        }

        void UpdateMonster(float dt, float now)
        {
            var m = Monster;
            if (m == null) return;
            for (int i = 0; i < m.Cooldowns.Length; i++) m.Cooldowns[i] = Mathf.Max(0f, m.Cooldowns[i] - dt);

            if (m.Dead)
            {
                m.Sr.enabled = false;
                if (now >= m.RespawnAt) Respawn(m);
                return;
            }

            if (now < m.BurnUntil) DamageMonster(m.BurnDps * dt, m.BurnSource);
            if (m.Dead) return;

            Vector2 move = m.IsHuman ? GameInput.Move : (m.Ai != null ? m.Ai.Tick(dt, now) : Vector2.zero);
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
            if (Phase == Phase.Night) MonsterAttack(m, dt, now);
            UpdateEating(m, dt);

            // visuals
            m.Sr.transform.position = new Vector3(m.Pos.x, m.Pos.y - 0.3f, 0f);
            m.Sr.sortingOrder = OrderFor(m.Pos.y - 0.3f);
            if (m.Anim != null)
            {
                bool moving = move.sqrMagnitude > 0.0001f || now < m.DashUntil;
                m.Anim.Drive(m.Facing, moving, m.Biting != null || m.AttackingRoom != null);
            }
            else m.Sr.flipX = m.Facing.x < 0f;
            m.Sr.enabled = IsVisible(m.Pos);
            Color tint = Color.white;
            if (now < m.StunUntil) tint = (Color)Palette.Mint;
            else if (now < m.SlowUntil) tint = new Color(0.75f, 0.9f, 1f);
            else if (now < m.BurnUntil) tint = new Color(1f, 0.8f, 0.55f);
            if (now < m.CloakUntil) tint.a = 0.35f;
            m.Sr.color = tint;
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

        void MonsterAttack(Monster m, float dt, float now)
        {
            // bite: any resident in reach with nothing solid in between (bed, hallway, doorway...)
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
                if (prey.Room != null)
                {
                    prey.Room.LastAttackedTime = now;
                    m.AttackingRoom = prey.Room;
                }
                if (prey.Asleep) Wake(prey);
                prey.Health -= m.Def.residentDamagePerSecond * AttackMult(m) * dt;
                if (prey.Health <= 0f) KillResident(prey);
                return;
            }

            // otherwise smash the nearest shut door
            Room door = null;
            float bestD = 1.35f;
            foreach (var r in RoomsByDef.Values)
            {
                if (!r.DoorBlocks || r.Owner == null || !r.Owner.Alive) continue;
                float d = Vector2.Distance(m.Pos, HotelMap.Center(r.Def.DoorTile));
                if (d < bestD) { bestD = d; door = r; }
            }
            if (door == null) return;

            float resist = Cfg.doors.levels[door.DoorLevel - 1].damageResistancePct;
            float rampage = now < m.RampageUntil ? m.RampageValue : 1f;
            float dmg = m.Def.doorDamagePerSecond * AttackMult(m) * rampage * (1f - resist) * dt;
            door.DoorHp -= dmg;
            door.LastAttackedTime = now;
            m.AttackingRoom = door;
            m.DreamPower += dmg * Cfg.economy.monsterIncome.dreamPowerPerDoorDamage;
            if (door.DoorHp <= 0f)
            {
                door.DoorHp = 0f;
                door.DoorBroken = true;
                RefreshDoor(door);
                Announce(door.Owner.IsHuman ? "Your door is broken! Rebuild it or fight back!" : door.Owner.Name + "'s door is broken!", 3f);
            }
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

            float oldMax = MaxHp(m);
            m.Parts[near.TypeIndex]++;
            m.Hp += MaxHp(m) - oldMax;
            m.Faith += Cfg.economy.monsterIncome.faithPerBodyPart;
            Parts.Remove(near);
            Destroy(near.Sr.gameObject);
            m.EatingPart = null;
            m.EatProgress = 0f;
            AddFloater(m.Pos + Vector2.up * 1.5f, "+" + near.Def.name, (Color)Palette.Moss);
            AddLog("The " + m.Def.name + " ate a " + near.Def.name.ToLower() + ". Gross.");
        }

        void Respawn(Monster m)
        {
            m.Dead = false;
            m.Pos = HotelMap.Center(Map.MonsterSpawn);
            m.Hp = MaxHp(m);
            Announce("The " + m.Def.name + " crawls back out of the dark...", 3f);
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
            m.RespawnAt = Time.time + Cfg.match.monsterRespawnSeconds;
            m.Sr.enabled = false;
            if (killer != null && killer.Alive)
            {
                var reward = EconomyRules.MonsterDeathReward(Cfg.economy, m.DreamPower, m.Faith);
                killer.DreamPower += reward.DreamPower;
                killer.Faith += reward.Faith;
                Announce((killer.IsHuman ? "You" : killer.Name) + " banished the " + m.Def.name + "! +" +
                         Mathf.RoundToInt(reward.DreamPower) + " Dream Power", 4f);
            }
            else
            {
                Announce("The " + m.Def.name + " was banished!", 3f);
            }
            m.DreamPower = 0f;
            m.Faith = 0f;
            for (int i = 0; i < m.Parts.Length; i++)
                m.Parts[i] = Mathf.FloorToInt(m.Parts[i] * Cfg.bodyParts.keepOnRespawnPct);
            m.SlowUntil = m.StunUntil = m.BurnUntil = m.JamUntil = m.RampageUntil = m.CloakUntil = m.DashUntil = 0f;
            m.EatingPart = null;
            m.EatProgress = 0f;
        }

        void KillResident(Resident r)
        {
            var m = Monster;
            r.Alive = false;
            r.Asleep = false;
            r.Health = 0f;
            var reward = EconomyRules.MonsterKillReward(Cfg.economy, r.DreamPower, r.Faith, r.XpValue);
            m.DreamPower += reward.DreamPower + Cfg.economy.monsterIncome.dreamPowerPerResidentKill;
            m.Faith += reward.Faith;
            m.MatchXp += reward.Xp;
            m.Kills++;
            r.DreamPower = 0f;
            r.Faith = 0f;
            r.Sr.sprite = Sprites.Ghost;
            r.Sr.transform.rotation = Quaternion.identity;
            r.Sr.transform.localScale = Vector3.one;
            r.Sr.color = Color.white;
            r.Sr.transform.position = new Vector3(r.Pos.x, r.Pos.y - 0.3f, 0f);
            Announce(r.IsHuman ? "You were eaten. Now you haunt the hallway (spectating)." : r.Name + " was eaten by the " + m.Def.name + "!", 4f);
            AddLog(r.Name + " was eaten.");
        }

        // ------------------------------------------------------------------ monster actions

        public bool UseAbility(int i)
        {
            var m = Monster;
            if (m == null || m.Dead || Phase != Phase.Night) return false;
            if (i < 0 || i >= m.Loadout.Length || m.Cooldowns[i] > 0f) return false;
            var a = m.Loadout[i];
            float now = Time.time;
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
                default:
                    AddLog(a.name + " is not in the demo yet.");
                    return false;
            }
            m.Cooldowns[i] = a.cooldownSeconds;
            if (IsVisible(m.Pos)) AddFloater(m.Pos + Vector2.up * 2.2f, a.name + "!", (Color)Palette.Mint);
            if (!m.IsHuman) AddLog("The monster used " + a.name + ".");
            return true;
        }

        public float ResistCost(Monster m, int type)
        {
            var tr = Cfg.monsters.resistanceTracks;
            int lv = m.ResistLevels[type];
            if (lv >= tr.maxLevel || lv + 1 >= tr.upgradeCostByLevel.Length) return -1f;
            return tr.upgradeCostByLevel[lv + 1];
        }

        public ActionResult TryUpgradeResist(Monster m, int type)
        {
            if (m == null || m.Dead) return ActionResult.Invalid;
            float cost = ResistCost(m, type);
            if (cost < 0f) return ActionResult.MaxLevel;
            bool faith = Cfg.monsters.resistanceTracks.costResource == "faith";
            float have = faith ? m.Faith : m.DreamPower;
            if (have + 0.001f < cost) return ActionResult.NoMoney;
            if (faith) m.Faith -= cost; else m.DreamPower -= cost;
            m.ResistLevels[type]++;
            AddFloater(m.Pos + Vector2.up * 2.2f, DamageTypes.Label(type) + " resist " + m.ResistLevels[type], (Color)Palette.Bone);
            if (!m.IsHuman) AddLog("The monster grew a thicker hide against " + DamageTypes.Label(type).ToLower() + ".");
            return ActionResult.Ok;
        }
    }
}
