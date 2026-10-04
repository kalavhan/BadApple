using System.Collections.Generic;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// Bot monster. Every 1.5 s it scores every living resident (walk in through an open or broken door, smash a shut
    /// one, or grab someone in the hallway) and every body part, weighting travel time, work and the tower fire it
    /// would stand in. It walks a BFS path, fires abilities when they pay off, and buys resistance against whatever
    /// damage type the hotel builds most.
    /// </summary>
    public class MonsterAI
    {
        readonly GameManager gm;
        readonly Monster me;

        List<Vector2Int> path;
        int pathIdx;
        float nextPlan;
        float nextUpgrade = 3f;
        Resident targetRes;
        BodyPart targetPart;
        Vector2Int goal;
        Vector2Int wanderGoal;
        Vector2 lastPos;
        float stuckTime;

        const float KillDesire = 12f; // how much more the bot wants a resident than a body part

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

            bool busy = me.Biting != null || me.EatingPart != null ||
                        (me.AttackingRoom != null && targetRes != null && me.AttackingRoom == targetRes.Room);
            if (!busy && (me.Pos - lastPos).sqrMagnitude < 0.0004f) stuckTime += dt; else stuckTime = 0f;
            lastPos = me.Pos;

            nextPlan -= dt;
            bool targetMoved = targetRes != null && (HotelMap.ToTile(targetRes.Pos) - goal).sqrMagnitude > 4 && !GoalIsDoor();
            if (nextPlan <= 0f || stuckTime > 1.2f || path == null || TargetGone() || targetMoved)
            {
                Plan();
                nextPlan = 1.5f;
                stuckTime = 0f;
            }

            if (busy) return Vector2.zero;
            return FollowPath();
        }

        bool GoalIsDoor() => targetRes != null && targetRes.Room != null && goal == targetRes.Room.Def.DoorOutside;

        bool TargetGone()
        {
            if (targetPart != null && !gm.Parts.Contains(targetPart)) return true;
            if (targetRes != null && !targetRes.Alive) return true;
            return false;
        }

        // ------------------------------------------------------------ planning

        void Plan()
        {
            var start = HotelMap.ToTile(me.Pos);
            float speed = Mathf.Max(0.5f, gm.MonsterSpeed(me, Time.time));
            float bite = Mathf.Max(1f, me.Def.residentDamagePerSecond * gm.AttackMult(me));
            float bestCost = float.MaxValue;
            Resident bestRes = null;
            BodyPart bestPart = null;
            Vector2Int bestGoal = start;
            float keepCost = float.MaxValue;
            Vector2Int keepGoal = start;

            if (gm.Phase == Phase.Night)
            {
                foreach (var r in gm.Residents)
                {
                    if (!r.Alive) continue;
                    var room = r.Room;
                    var rTile = HotelMap.ToTile(r.Pos);
                    bool rInside = room != null && room.Def.ContainsInterior(rTile);
                    Vector2Int g;
                    float work = r.Health / bite;
                    if (rInside && room.DoorBlocks && !room.Def.ContainsInterior(start))
                    {
                        g = room.Def.DoorOutside;
                        float resist = gm.Cfg.doors.levels[room.DoorLevel - 1].damageResistancePct;
                        float dps = Mathf.Max(1f, me.Def.doorDamagePerSecond * gm.AttackMult(me) * (1f - resist));
                        work += room.DoorHp / dps;
                    }
                    else g = rTile;

                    var p = Pathfinding.FindPath(start, g, gm.MonsterWalkable);
                    if (p == null) continue;
                    float travel = p.Count / speed;
                    float threat = gm.ThreatAt(HotelMap.Center(g), me);
                    float cost = travel + work * (1f + threat / 60f) - KillDesire;
                    if (work > me.Hp / Mathf.Max(1f, threat) * 0.9f) cost += 30f; // would probably die first
                    if (!rInside) cost -= 6f;                                     // a resident in the hallway is a gift
                    if (r == targetRes) { keepCost = cost; keepGoal = g; }
                    if (cost < bestCost) { bestCost = cost; bestRes = r; bestPart = null; bestGoal = g; }
                }
            }

            int maxParts = gm.Cfg.bodyParts.maxPartsPerType;
            foreach (var part in gm.Parts)
            {
                if (me.Parts[part.TypeIndex] >= maxParts) continue;
                var p = Pathfinding.FindPath(start, part.Tile, gm.MonsterWalkable);
                if (p == null) continue;
                float eat = gm.Cfg.bodyParts.eatSeconds;
                float partThreat = gm.ThreatAt(HotelMap.Center(part.Tile), me);
                if (partThreat * eat > me.Hp * 0.3f) continue;                 // not worth dying for a snack
                float bonus = gm.Night <= 2 ? 3f : 1f;
                if (part.Def.id == "torso" && me.Hp < gm.MaxHp(me) * 0.5f) bonus += 4f;
                float cost = p.Count / speed + eat * (1f + partThreat / 20f) - bonus;
                if (part == targetPart) { keepCost = cost; keepGoal = part.Tile; }
                if (cost < bestCost) { bestCost = cost; bestPart = part; bestRes = null; bestGoal = part.Tile; }
            }

            // keep the current target unless the new one is clearly better
            if (keepCost < float.MaxValue && keepCost <= bestCost * 1.25f + 2f && (targetRes != null || targetPart != null))
            {
                bestRes = targetRes;
                bestPart = targetPart;
                bestGoal = keepGoal;
            }

            targetRes = bestRes;
            targetPart = bestPart;
            if (bestRes == null && bestPart == null)
            {
                if (path == null || pathIdx >= path.Count || start == wanderGoal)
                {
                    var tiles = gm.Map.CorridorTiles();
                    wanderGoal = tiles[Random.Range(0, tiles.Count)];
                }
                bestGoal = wanderGoal;
            }

            goal = bestGoal;
            path = Pathfinding.FindPath(start, goal, gm.MonsterWalkable) ?? new List<Vector2Int>();
            pathIdx = 0;
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
                    if (!gm.MonsterWalkable(next.x, next.y)) return TowardGoal();
                    return (HotelMap.Center(next) - me.Pos).normalized;
                }
            }
            return TowardGoal();
        }

        Vector2 TowardGoal()
        {
            Vector2 aim;
            if (targetRes != null) aim = GoalIsDoor() ? HotelMap.Center(goal) : targetRes.Pos;
            else if (targetPart != null) aim = HotelMap.Center(targetPart.Tile);
            else return Vector2.zero;
            var d = aim - me.Pos;
            return d.magnitude > 0.4f ? d.normalized : Vector2.zero;
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
                        use = room != null && room.DoorBlocks && room.DoorHp > 150f;
                        break;
                    case "towerDamageMultiplier":
                        use = room != null && CountType(room, "bullet") >= 1;
                        break;
                    case "faithIncomeMultiplier":
                        int near = 0;
                        foreach (var r in gm.RoomsByDef.Values)
                            if (r.Owner != null && r.Owner.Alive && HasFaith(r) &&
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
                        use = gm.ThreatAt(me.Pos, me) > 30f;
                        break;
                    case "dash":
                        use = path != null && path.Count - pathIdx > 8;
                        break;
                }
                if (use) gm.UseAbility(i);
            }
        }

        static bool HasFaith(Room room)
        {
            foreach (var t in room.Slots) if (t != null && t.IsFaith) return true;
            return false;
        }

        static int CountType(Room room, string type)
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
