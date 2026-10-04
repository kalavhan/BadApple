using System.Collections.Generic;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// Bot monster. Every 1.5 s it scores every reachable target: a door to break (time to break it, scaled by how
    /// much the room's towers hurt), an exposed resident, or a body part (cheap early power). It walks a BFS path,
    /// fires abilities when they pay off, and buys resistance against whatever damage type the hotel builds most.
    /// </summary>
    public class MonsterAI
    {
        readonly GameManager gm;
        readonly Monster me;

        List<Vector2Int> path;
        int pathIdx;
        float nextPlan;
        float nextUpgrade = 3f;
        Room targetRoom;
        BodyPart targetPart;
        Vector2Int wanderGoal;
        bool wandering;
        float currentCost = float.MaxValue;
        Vector2 lastPos;
        float stuckTime;

        public MonsterAI(GameManager gm, Monster me)
        {
            this.gm = gm;
            this.me = me;
        }

        public Vector2 Tick(float dt, float now)
        {
            ThinkAbilities();
            nextUpgrade -= dt;
            if (nextUpgrade <= 0f) { nextUpgrade = 2f; ThinkUpgrades(); }

            bool busy = (me.AttackingRoom != null && me.AttackingRoom == targetRoom) || me.EatingPart != null;
            if (!busy && (me.Pos - lastPos).sqrMagnitude < 0.0004f) stuckTime += dt; else stuckTime = 0f;
            lastPos = me.Pos;

            nextPlan -= dt;
            if (nextPlan <= 0f || stuckTime > 1.2f || path == null || TargetGone())
            {
                Plan();
                nextPlan = 1.5f;
                stuckTime = 0f;
            }

            if (busy) return Vector2.zero;
            return FollowPath();
        }

        bool TargetGone()
        {
            if (targetPart != null && !gm.Parts.Contains(targetPart)) return true;
            if (targetRoom != null && (targetRoom.Owner == null || !targetRoom.Owner.Alive)) return true;
            return false;
        }

        // ------------------------------------------------------------ planning

        void Plan()
        {
            var start = HotelMap.ToTile(me.Pos);
            float speed = Mathf.Max(0.5f, gm.MonsterSpeed(me, Time.time));
            float bestCost = float.MaxValue;
            Room bestRoom = null;
            BodyPart bestPart = null;
            Vector2Int bestGoal = start;
            float keepCost = float.MaxValue;

            if (gm.Phase == Phase.Night)
            {
                foreach (var room in gm.RoomsByDef.Values)
                {
                    if (room.Owner == null || !room.Owner.Alive) continue;
                    bool inside = room.Def.ContainsInterior(start);
                    Vector2Int goal;
                    float work;
                    if (!room.DoorBroken)
                    {
                        goal = inside ? room.Def.DoorInside : room.Def.DoorOutside;
                        float resist = gm.Cfg.doors.levels[room.DoorLevel - 1].damageResistancePct;
                        float dps = Mathf.Max(1f, me.Def.doorDamagePerSecond * gm.AttackMult(me) * (1f - resist));
                        work = room.DoorHp / dps + room.Owner.Health / Mathf.Max(1f, me.Def.residentDamagePerSecond * gm.AttackMult(me));
                    }
                    else
                    {
                        goal = room.Def.BedTile;
                        work = room.Owner.Health / Mathf.Max(1f, me.Def.residentDamagePerSecond * gm.AttackMult(me));
                    }
                    var p = Pathfinding.FindPath(start, goal, gm.Walkable);
                    if (p == null) continue;
                    float travel = p.Count / speed;
                    float threat = RoomDps(room);
                    float cost = travel + work * (1f + threat / 40f);
                    if (work > me.Hp / Mathf.Max(1f, threat) * 0.9f) cost += 30f; // would probably die first
                    if (room == targetRoom) keepCost = cost;
                    if (cost < bestCost) { bestCost = cost; bestRoom = room; bestPart = null; bestGoal = goal; }
                }
            }

            int maxParts = gm.Cfg.bodyParts.maxPartsPerType;
            foreach (var part in gm.Parts)
            {
                if (me.Parts[part.TypeIndex] >= maxParts) continue;
                var p = Pathfinding.FindPath(start, part.Tile, gm.Walkable);
                if (p == null) continue;
                float bonus = gm.Night <= 2 ? 8f : 4f;
                if (part.Def.id == "torso" && me.Hp < gm.MaxHp(me) * 0.5f) bonus += 4f;
                float cost = p.Count / speed + gm.Cfg.bodyParts.eatSeconds - bonus;
                if (part == targetPart) keepCost = cost;
                if (cost < bestCost) { bestCost = cost; bestPart = part; bestRoom = null; bestGoal = part.Tile; }
            }

            // keep the current target unless the new one is clearly better
            if (keepCost < float.MaxValue && keepCost <= bestCost * 1.25f && (targetRoom != null || targetPart != null))
            {
                bestRoom = targetRoom;
                bestPart = targetPart;
                bestGoal = targetRoom != null ? GoalFor(targetRoom, start) : targetPart.Tile;
                bestCost = keepCost;
            }

            targetRoom = bestRoom;
            targetPart = bestPart;
            currentCost = bestCost;
            wandering = bestRoom == null && bestPart == null;

            if (wandering)
            {
                if (path == null || pathIdx >= (path?.Count ?? 0) || start == wanderGoal)
                {
                    var tiles = gm.Map.CorridorTiles();
                    wanderGoal = tiles[Random.Range(0, tiles.Count)];
                }
                bestGoal = wanderGoal;
            }

            path = Pathfinding.FindPath(start, bestGoal, gm.Walkable) ?? new List<Vector2Int>();
            pathIdx = 0;
        }

        Vector2Int GoalFor(Room room, Vector2Int start)
        {
            if (room.DoorBroken) return room.Def.BedTile;
            return room.Def.ContainsInterior(start) ? room.Def.DoorInside : room.Def.DoorOutside;
        }

        float RoomDps(Room room)
        {
            var sc = gm.Cfg.towers.levelScaling;
            float dps = 0f;
            foreach (var t in room.Slots)
            {
                if (t == null || !t.IsWeapon || t.Def.damageType == "slow") continue;
                int lv = t.Level - 1;
                float mult = gm.DamageTaken(me, DamageTypes.Index(t.Def.damageType));
                dps += t.Def.damage * Mathf.Pow(sc.damage, lv) * t.Def.shotsPerSecond * Mathf.Pow(sc.fireRate, lv) * mult;
                dps += t.Def.burnDamagePerSecond * mult * 0.5f;
            }
            return dps;
        }

        // ------------------------------------------------------------ movement

        Vector2 FollowPath()
        {
            if (path != null)
            {
                while (pathIdx < path.Count && Vector2.Distance(me.Pos, HotelMap.Center(path[pathIdx])) < 0.2f) pathIdx++;
                if (pathIdx < path.Count)
                {
                    var next = path[pathIdx];
                    if (!gm.Walkable(next.x, next.y)) return TowardFinal();
                    return (HotelMap.Center(next) - me.Pos).normalized;
                }
            }
            return TowardFinal();
        }

        Vector2 TowardFinal()
        {
            Vector2 aim;
            if (targetRoom != null) aim = HotelMap.Center(targetRoom.DoorBroken ? targetRoom.Def.BedTile : targetRoom.Def.DoorTile);
            else if (targetPart != null) aim = HotelMap.Center(targetPart.Tile);
            else return Vector2.zero;
            var d = aim - me.Pos;
            return d.magnitude > 0.5f ? d.normalized : Vector2.zero;
        }

        // ------------------------------------------------------------ abilities & upgrades

        void ThinkAbilities()
        {
            if (gm.Phase != Phase.Night || me.Loadout == null) return;
            for (int i = 0; i < me.Loadout.Length; i++)
            {
                if (me.Cooldowns[i] > 0f) continue;
                var a = me.Loadout[i];
                var room = me.AttackingRoom;
                bool use = false;
                switch (a.effect)
                {
                    case "doorDamageMultiplier":
                        use = room != null && !room.DoorBroken && room.DoorHp > 150f;
                        break;
                    case "towerDamageMultiplier":
                        use = room != null && CountType(room, "bullet") >= 1;
                        break;
                    case "faithIncomeMultiplier":
                        int near = 0;
                        foreach (var r in gm.RoomsByDef.Values)
                            if (r.Owner != null && r.Owner.Alive && r.CountTowers("faith_tower") > 0 &&
                                Vector2.Distance(me.Pos, HotelMap.Center(r.Def.DoorTile)) <= a.radius) near++;
                        use = near >= 2 || (near >= 1 && room != null);
                        break;
                    case "bedIncomeMultiplier":
                        int beds = 0;
                        foreach (var r in gm.RoomsByDef.Values)
                            if (r.Owner != null && r.Owner.Alive && Vector2.Distance(me.Pos, HotelMap.Center(r.Def.DoorTile)) <= a.radius) beds++;
                        use = beds >= 2;
                        break;
                    case "towerUntargetable":
                        use = room != null && RoomDps(room) > 30f;
                        break;
                    case "dash":
                        use = path != null && path.Count - pathIdx > 8;
                        break;
                }
                if (use) gm.UseAbility(i);
            }
        }

        int CountType(Room room, string type)
        {
            int n = 0;
            foreach (var t in room.Slots) if (t != null && t.Def.damageType == type) n++;
            return n;
        }

        void ThinkUpgrades()
        {
            var share = new float[4];
            foreach (var room in gm.RoomsByDef.Values)
            {
                if (room.Owner == null || !room.Owner.Alive) continue;
                foreach (var t in room.Slots)
                {
                    if (t == null || !t.IsWeapon) continue;
                    int type = DamageTypes.Index(t.Def.damageType);
                    if (type >= 0) share[type] += t.Level;
                }
            }
            int best = -1;
            float bestShare = 0f;
            for (int i = 0; i < 4; i++)
                if (share[i] > bestShare && gm.ResistCost(me, i) >= 0f) { bestShare = share[i]; best = i; }
            if (best >= 0) gm.TryUpgradeResist(me, best);
        }
    }
}
