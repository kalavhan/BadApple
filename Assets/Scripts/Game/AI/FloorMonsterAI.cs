using System.Linq;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// Bot monster for the hallway floors. In the lift it spends Fear on the waves and picks the floor that looks
    /// weakest (few towers, a cheap door, a hurt guest). On a floor it walks the carpet to the door, smashes it and
    /// goes for the guest, using its abilities when they would land. Badly hurt, it walks back to the lift, heals
    /// on the ride and tries another floor. GameManager.BotSkill scales how sharp all of that is.
    /// </summary>
    public class FloorMonsterAI
    {
        readonly GameManager gm;
        readonly Monster me;
        readonly Navigator navigator = new Navigator();
        float decideAt, nextSpend, nextAbility;
        bool retreating;

        public FloorMonsterAI(GameManager gm, Monster me)
        {
            this.gm = gm;
            this.me = me;
            decideAt = gm.Now + Random.Range(.8f, 1.6f);
        }

        public Vector2 Tick(float dt, float now)
        {
            float skill = gm.BotSkill;
            if (now >= nextSpend) { nextSpend = now + Mathf.Lerp(6f, 2f, skill); Spend(); }
            if (me.InLift)
            {
                if (me.LiftTarget == null && now >= decideAt)
                {
                    var floor = PickFloor(skill);
                    if (floor != null) gm.RideLift(me, floor);
                    decideAt = now + 1f;
                }
                return Vector2.zero;
            }
            var room = me.Floor != null && gm.RoomsByDef.TryGetValue(me.Floor, out var r) ? r : null;
            if (room == null || room.Owner == null || !room.Owner.Alive) { retreating = true; }
            else if (me.Hp < gm.MaxHp(me) * Mathf.Lerp(.1f, .35f, skill)) retreating = true;

            if (retreating)
            {
                if (gm.NearLift(me)) { retreating = false; gm.EnterLift(me); decideAt = now + Random.Range(.5f, 1.2f); return Vector2.zero; }
                return navigator.Steer(ref me.Pos, me.Floor.LiftLanding, gm.MonsterWalkable, GameManager.MonsterRadius, dt, now, gm.Walls);
            }

            if (now >= nextAbility) { nextAbility = now + Mathf.Lerp(3f, .4f, skill); UseAbilities(room); }
            if (me.Biting != null || me.AttackingRoom != null) return Vector2.zero;
            var goal = room.DoorBlocks ? room.Def.DoorOutside : HotelMap.ToTile(room.Owner.Pos);
            return navigator.Steer(ref me.Pos, goal, gm.MonsterWalkable, GameManager.MonsterRadius, dt, now, gm.Walls);
        }

        /// <summary>Weakest-looking floor: tower levels and door strength against the guest's health.</summary>
        RoomDef PickFloor(float skill)
        {
            RoomDef best = null;
            float bestScore = float.MaxValue;
            foreach (var def in gm.HuntableFloors(me))
            {
                var room = gm.RoomsByDef[def];
                float towers = room.Slots.Where(t => t != null && t.IsWeapon).Sum(t => (float)t.Level);
                float score = towers * 2f + room.DoorLevel * 6f + room.Owner.Health * .1f - def.Lane.Count * .05f;
                if (room.DoorBroken) score -= 20f;
                if (me.Floor == def && retreating) score += 25f;
                score += Random.Range(0f, 60f) * (1.1f - skill);
                if (score < bestScore) { bestScore = score; best = def; }
            }
            return best;
        }

        /// <summary>Fear goes first into the floor it is hunting, then into wherever it is cheapest.</summary>
        void Spend()
        {
            if (gm.Phase != Phase.Night || Random.value > Mathf.Lerp(.5f, 1f, gm.BotSkill)) return;
            var floors = gm.HuntableFloors(me).Select(d => gm.RoomsByDef[d]).ToList();
            if (floors.Count == 0) return;
            var target = me.Floor != null && gm.RoomsByDef.TryGetValue(me.Floor, out var r) && floors.Contains(r) ? r : floors[Random.Range(0, floors.Count)];
            for (int guard = 0; guard < 4; guard++)
            {
                int type = System.Array.IndexOf(target.WaveTypes, false);
                bool bought = type >= 0 && Random.value < .5f && gm.TryAddWaveType(target, type) == ActionResult.Ok;
                if (!bought) bought = gm.TryGrowWave(target) == ActionResult.Ok;
                if (!bought) break;
            }
        }

        void UseAbilities(Room room)
        {
            if (me.Loadout == null) return;
            bool busy = me.Biting != null || (me.AttackingRoom != null && me.AttackingRoom == room);
            for (int i = 0; i < me.Loadout.Length; i++)
            {
                if (me.Cooldowns[i] > 0f) continue;
                var a = me.Loadout[i];
                bool use;
                switch (a.effect)
                {
                    case "towerUntargetable": use = gm.ThreatAt(me.Pos, me) > 25f; break;
                    case "dash": use = !busy && Random.value < .3f; break;
                    default: use = busy; break;
                }
                if (use) gm.UseAbility(i);
            }
        }
    }
}
