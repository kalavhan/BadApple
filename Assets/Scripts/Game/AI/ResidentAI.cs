using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// Bot resident. During setup it walks to a free room, shuts the door and goes to bed. It sleeps for Dream Power
    /// and wakes up (towers hit harder) when the monster comes close. About once a second it picks one thing to spend
    /// on: bed, door, a new tower placed where its range covers the door, or an upgrade. It respects the 4-level gap
    /// rule, saves up when it cannot afford its pick, prefers towers the monster is weak to, and begs a neighbour for
    /// help when its door is failing.
    /// </summary>
    public class ResidentAI
    {
        readonly GameManager gm;
        readonly Resident me;
        float nextThink;
        float tickDt, tickNow;
        float nextHelp;
        readonly float eco;      // personality: how much it likes economy upgrades
        readonly float turtle;   // personality: how much it likes the door

        RoomDef targetRoom;

        public ResidentAI(GameManager gm, Resident me)
        {
            this.gm = gm;
            this.me = me;
            eco = Random.Range(0.6f, 1.6f);
            turtle = Random.Range(0.6f, 1.6f);
            nextThink = Random.Range(0.5f, 1.5f);
        }

        /// <summary>Returns the direction to walk this frame (zero to stand still).</summary>
        public Vector2 Tick(float dt, float now)
        {
            tickDt = dt; tickNow = now;
            nextThink -= dt;
            if (nextThink <= 0f)
            {
                nextThink = Random.Range(0.7f, 1.3f);
                Think(now);
            }
            var move = Steer(dt, now);
            return move;
        }

        // ------------------------------------------------------------ walking & routine

        Vector2 Steer(float dt, float now)
        {
            var room = me.Room;

            if (room == null)
            {
                if (gm.Phase == Phase.Setup && gm.Cfg.match.setupSeconds - gm.PhaseTimer < me.ClaimAt) return Vector2.zero;
                if (targetRoom == null || !gm.IsRoomFree(targetRoom)) targetRoom = PickRoom();
                return targetRoom == null ? Vector2.zero : WalkTo(targetRoom.DoorInside);
            }

            var tile = HotelMap.ToTile(me.Pos);
            bool inside = room.Def.ContainsInterior(tile);
            if (!inside)
            {
                // somehow outside: go back in through our own door
                if (room.DoorBlocks)
                {
                    if (gm.NearDoor(me)) gm.TryToggleDoor(me);
                    return WalkTo(room.Def.DoorOutside);
                }
                return WalkTo(room.Def.DoorInside);
            }

            if (!gm.OnBed(me))
            {
                gm.TrySleep(me);
                return Vector2.zero;
            }

            bool danger = room.UnderAttack(now) || room.DoorBroken;
            var m = gm.Monster;
            if (gm.Phase == Phase.Night && m != null && !m.Dead &&
                Vector2.Distance(m.Pos, HotelMap.Center(room.Def.DoorOutside)) <= gm.Cfg.residents.botWakeRadiusTiles)
                danger = true;
            if (danger && me.Asleep) gm.Wake(me);
            else if (!danger && !me.Asleep) gm.TrySleep(me);
            return Vector2.zero;
        }

        RoomDef PickRoom()
        {
            var routes = new List<(RoomDef room, int distance)>();
            var start = HotelMap.ToTile(me.Pos);
            foreach (var room in gm.Map.Rooms)
            {
                if (!gm.IsRoomFree(room)) continue;
                var path = Pathfinding.FindPath(start, room.DoorInside, (x, y) => gm.WalkableFor(me, x, y));
                if (path != null) routes.Add((room, path.Count));
            }
            if (routes.Count == 0) return null;
            // A room across a wall can be close on screen but a long corridor walk away.
            // Keep personality in the choice without sending guests on avoidable detours
            // that leave them unclaimed when lights go out.
            routes.Sort((a, b) => a.distance != b.distance ? a.distance.CompareTo(b.distance) : a.room.Index.CompareTo(b.room.Index));
            float limit = routes[0].distance + Mathf.Max(8, routes[0].distance * .35f);
            int choices = Mathf.Min(4, routes.Count);
            while (choices > 1 && routes[choices - 1].distance > limit) choices--;
            return routes[Random.Range(0, choices)].room;
        }

        Vector2 WalkTo(Vector2Int goal)
        {
            return me.Navigator.Steer(ref me.Pos, goal, (x, y) => gm.WalkableFor(me, x, y), GameManager.ResidentRadius, tickDt, tickNow, gm.Walls);
        }

        // ------------------------------------------------------------ spending

        void Think(float now)
        {
            var room = me.Room;
            if (room == null || !me.Alive || me.IsMonster) return;
            bool attacked = room.UnderAttack(now);

            // 1) emergency: door failing
            if (attacked && (room.DoorBroken || room.DoorHp < gm.MaxDoorHp(room) * 0.5f))
            {
                var r = gm.TryUpgradeDoor(me);
                if (r == ActionResult.Ok) return;
                if (r == ActionResult.Blocked && UpgradeLowestWeapon() == ActionResult.Ok) return;
                if (now >= nextHelp && me.DreamPower < 60f)
                {
                    nextHelp = now + 20f;
                    gm.AskForHelp(me);
                }
                if (UpgradeLowestWeapon() == ActionResult.Ok) return;
            }

            // 2) early economy: get off the floor
            if (room.BedLevel < 2)
            {
                if (gm.TryUpgradeBed(me) == ActionResult.Ok) return;
                if (room.WeaponCount() > 0) return; // save for the bed
            }

            // 3) always have at least one weapon
            if (room.WeaponCount() == 0)
            {
                BuildBestWeapon(room);
                return;
            }

            // 4) weighted choice; if the pick is unaffordable we simply wait (that is how bots save up)
            var options = new List<(float w, System.Func<ActionResult> act)>();
            bool hasEmpty = room.EmptySlot() >= 0;

            if (room.BedLevel < gm.Cfg.beds.levels.Length)
                options.Add((eco * (gm.Cfg.beds.levels.Length - room.BedLevel) * 0.8f, () => gm.TryUpgradeBed(me)));

            var door = gm.CheckDoor(room);
            if (!door.AtMaxLevel)
                options.Add((door.Allowed ? turtle * (attacked ? 6f : 2.5f) : 0.5f, () =>
                {
                    var res = gm.TryUpgradeDoor(me);
                    return res == ActionResult.Blocked ? UpgradeLowestWeapon() : res;
                }));

            if (hasEmpty)
            {
                int faithTowers = room.Slots.Count(t => t != null && t.IsFaith);
                if (faithTowers < 2)
                    options.Add((faithTowers == 0 ? 4f * eco : 1.5f * eco, () => BuildFaith(room)));
                options.Add((4f, () => BuildBestWeapon(room)));
            }

            if (room.WeaponCount() > 0)
                options.Add((3f, () => UpgradeLowestWeapon()));

            var faithTower = room.Slots.FirstOrDefault(t => t != null && t.IsFaith);
            if (faithTower != null)
                options.Add((1.2f * eco, () => Upgrade(faithTower)));

            if (options.Count == 0) return;
            float total = options.Sum(o => o.w);
            float pick = Random.Range(0f, total);
            foreach (var o in options)
            {
                pick -= o.w;
                if (pick <= 0f) { o.act(); return; }
            }
        }

        ActionResult UpgradeLowestWeapon()
        {
            var weapons = gm.Weapons(me.Room);
            if (weapons.Count == 0) return BuildBestWeapon(me.Room);
            var lowest = weapons.OrderBy(w => w.Level).First();
            return Upgrade(lowest);
        }

        /// <summary>
        /// One upgrade decision climbs a whole form at once (every level-up and the Evolve), as a player
        /// double-tapping through the levels would. Bots save until they can pay for the whole climb, so their
        /// money still reaches doors and beds the way it did when each upgrade was one tier.
        /// </summary>
        ActionResult Upgrade(TowerInstance t)
        {
            int form = gm.TowerForm(t), end = Rules.UpgradeRules.FormEnd(gm.Cfg.towers, t.Def, form), max = gm.TowerMaxLevel(t);
            if (t.Level >= max) return ActionResult.MaxLevel;
            float climb = 0f;
            for (int l = t.Level; l <= end && l < max; l++) climb += Rules.UpgradeRules.TowerUpgradeCost(gm.Cfg.towers, t.Def, l);
            if (gm.Wallet(me, t.Def.costResource) + .001f < climb) return ActionResult.NoMoney;
            var result = ActionResult.Invalid;
            while (gm.TowerForm(t) == form && t.Level < max)
            {
                var step = gm.TryUpgradeTower(me, t.SlotIndex);
                if (step != ActionResult.Ok) break;
                result = ActionResult.Ok;
            }
            return result;
        }

        ActionResult BuildFaith(Room room)
        {
            var def = gm.Cfg.towers.towers.FirstOrDefault(t => t.effect == "faith" || t.faithPerSecond > 0f);
            if (def == null) return ActionResult.Invalid;
            int slot = -1;
            float far = -1f;
            var door = HotelMap.Center(room.Def.DoorOutside);
            for (int i = 0; i < room.Slots.Length; i++)
            {
                if (room.Slots[i] != null || !gm.CanBuildAt(room, i)) continue;
                float d = Vector2.Distance(HotelMap.Center(room.Def.BuildTiles[i]), door);
                if (d > far) { far = d; slot = i; }
            }
            return slot < 0 ? ActionResult.Invalid : gm.TryBuildTower(me, slot, def.id);
        }

        /// <summary>Builds the weapon the monster is weakest to (among the affordable ones) on the plate where its range best covers the door.</summary>
        ActionResult BuildBestWeapon(Room room)
        {
            if (room.EmptySlot() < 0) return UpgradeLowestWeaponIfAny(room);
            var m = gm.Monster;
            var door = HotelMap.Center(room.Def.DoorOutside);
            var weapons = gm.Cfg.towers.towers.Where(t => t.damageType != "none").ToList();
            var affordable = weapons.Where(t => gm.Wallet(me, t.costResource) >= t.buildCost).ToList();
            if (affordable.Count == 0) return ActionResult.NoMoney;

            var choices = new List<(Config.TowerDef def, int slot, float w)>();
            foreach (var t in affordable)
            {
                int slot = BestSlotFor(room, t, door, out float coverage);
                if (slot < 0) continue;
                float mult = m != null ? gm.DamageTaken(m, DamageTypes.Index(t.damageType)) : 1f;
                float w = Mathf.Pow(mult, 3f) * coverage;
                if (t.damageType == "slow" && room.CountTowers(t.id) > 0) w *= 0.3f; // one slow totem is plenty
                if (t.id == "gun_turret") w *= 1.3f; // cheap, reliable
                choices.Add((t, slot, w));
            }
            if (choices.Count == 0) return ActionResult.Invalid;
            float total = choices.Sum(c => c.w);
            float pick = Random.Range(0f, total);
            foreach (var c in choices)
            {
                pick -= c.w;
                if (pick <= 0f) return gm.TryBuildTower(me, c.slot, c.def.id);
            }
            var last = choices[choices.Count - 1];
            return gm.TryBuildTower(me, last.slot, last.def.id);
        }

        int BestSlotFor(Room room, Config.TowerDef def, Vector2 door, out float coverage)
        {
            float range = gm.RangeOf(def, 1);
            int best = -1;
            coverage = 0f;
            for (int i = 0; i < room.Slots.Length; i++)
            {
                if (room.Slots[i] != null || !gm.CanBuildAt(room, i)) continue;
                float d = Vector2.Distance(HotelMap.Center(room.Def.BuildTiles[i]), door);
                float score = d <= range ? 1f + (range - d) / range : 0.15f / (1f + d - range);
                if (score > coverage) { coverage = score; best = i; }
            }
            return best;
        }

        ActionResult UpgradeLowestWeaponIfAny(Room room)
        {
            var weapons = gm.Weapons(room);
            if (weapons.Count == 0) return ActionResult.Invalid;
            return Upgrade(weapons.OrderBy(w => w.Level).First());
        }
    }
}
