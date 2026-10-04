using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// Bot resident. Every ~1 s it picks one thing to spend on: bed, door, a new tower or an upgrade.
    /// It respects the 4-level gap rule (upgrading the lowest weapon when the door is blocked), saves up
    /// when it cannot afford its pick, prefers towers the monster is weak to, and begs a neighbour for help
    /// when its door is failing.
    /// </summary>
    public class ResidentAI
    {
        readonly GameManager gm;
        readonly Resident me;
        float nextThink;
        float nextHelp;
        readonly float eco;      // personality: how much it likes economy upgrades
        readonly float turtle;   // personality: how much it likes the door

        public ResidentAI(GameManager gm, Resident me)
        {
            this.gm = gm;
            this.me = me;
            eco = Random.Range(0.6f, 1.6f);
            turtle = Random.Range(0.6f, 1.6f);
            nextThink = Random.Range(0.5f, 1.5f);
        }

        public void Tick(float dt, float now)
        {
            nextThink -= dt;
            if (nextThink > 0f) return;
            nextThink = Random.Range(0.7f, 1.3f);
            Think(now);
        }

        void Think(float now)
        {
            var room = me.Room;
            if (room == null || !me.Alive) return;
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
                // while attacked, more firepower helps as well
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
            int emptySlot = room.EmptySlot();

            if (room.BedLevel < gm.Cfg.beds.levels.Length)
                options.Add((eco * (6 - room.BedLevel) * 0.8f, () => gm.TryUpgradeBed(me)));

            var door = gm.CheckDoor(room);
            if (!door.AtMaxLevel)
                options.Add((door.Allowed ? turtle * (attacked ? 6f : 2.5f) : 0.5f, () =>
                {
                    var res = gm.TryUpgradeDoor(me);
                    return res == ActionResult.Blocked ? UpgradeLowestWeapon() : res;
                }));

            if (emptySlot >= 0)
            {
                int faithTowers = room.CountTowers("faith_tower");
                if (faithTowers < 2)
                    options.Add((faithTowers == 0 ? 4f * eco : 1.5f * eco, () => gm.TryBuildTower(me, emptySlot, "faith_tower")));
                options.Add((4f, () => BuildBestWeapon(room)));
            }

            if (room.WeaponCount() > 0)
                options.Add((3f, () => UpgradeLowestWeapon()));

            var faithTower = room.Slots.FirstOrDefault(t => t != null && t.Def.id == "faith_tower");
            if (faithTower != null)
                options.Add((1.2f * eco, () => gm.TryUpgradeTower(me, faithTower.SlotIndex)));

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
            return gm.TryUpgradeTower(me, lowest.SlotIndex);
        }

        /// <summary>Builds the weapon the monster is weakest to, among the ones we can pay for now.</summary>
        ActionResult BuildBestWeapon(Room room)
        {
            int slot = room.EmptySlot();
            if (slot < 0) return UpgradeLowestWeaponIfAny(room);
            var m = gm.Monster;
            var weapons = gm.Cfg.towers.towers.Where(t => t.damageType != "none").ToList();
            var affordable = weapons.Where(t => gm.Wallet(me, t.costResource) >= t.buildCost).ToList();
            if (affordable.Count == 0) return ActionResult.NoMoney;

            float total = 0f;
            var weights = new List<float>();
            foreach (var t in affordable)
            {
                float mult = m != null ? gm.DamageTaken(m, DamageTypes.Index(t.damageType)) : 1f;
                float w = Mathf.Pow(mult, 3f);
                if (t.damageType == "slow" && room.CountTowers(t.id) > 0) w *= 0.3f; // one slow totem is plenty
                if (t.id == "gun_turret") w *= 1.3f; // cheap, reliable
                weights.Add(w);
                total += w;
            }
            float pick = Random.Range(0f, total);
            for (int i = 0; i < affordable.Count; i++)
            {
                pick -= weights[i];
                if (pick <= 0f) return gm.TryBuildTower(me, slot, affordable[i].id);
            }
            return gm.TryBuildTower(me, slot, affordable[affordable.Count - 1].id);
        }

        ActionResult UpgradeLowestWeaponIfAny(Room room)
        {
            var weapons = gm.Weapons(room);
            if (weapons.Count == 0) return ActionResult.Invalid;
            return gm.TryUpgradeTower(me, weapons.OrderBy(w => w.Level).First().SlotIndex);
        }
    }
}
