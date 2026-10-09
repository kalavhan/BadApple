using System.Collections.Generic;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// Bot monster. Every 1.5 s it scores every living resident (walk in through an open or broken door, smash a shut
    /// one, or grab someone in the hallway) and every body part, weighting travel time, work and the tower fire it
    /// would stand in. It walks a BFS path, fires abilities when they pay off, spends Fear on levels and its horde,
    /// and sets the horde's resistance from the towers it has actually seen. GameManager.BotSkill scales all of it: a
    /// novice replans slowly, picks targets carelessly, sits on its abilities and its Fear, and never retreats.
    /// </summary>
    public class MonsterAI
    {
        readonly GameManager gm;
        readonly Monster me;

        List<Vector2Int> path;
        int pathIdx;
        float nextPlan;
        float nextUpgrade = 3f, nextAbilityThink;
        Resident targetRes;
        BodyPart targetPart;
        Vector2Int goal;
        Vector2Int wanderGoal;
        Vector2 lastPos;
        float stuckTime;

        readonly Navigator navigator = new Navigator();
        float tickDt, tickNow;
        float commitUntil;
        const float KillDesire = 12f; // how much more the bot wants a resident than a body part

        public MonsterAI(GameManager gm, Monster me)
        {
            this.gm = gm;
            this.me = me;
        }

        public Vector2 Tick(float dt, float now)
        {
            tickDt = dt; tickNow = now;
            bool assaulted = gm.Metrics.DoorAssaultsPerNight.ContainsKey(gm.Night);
            if (!assaulted) me.Retreating = false;
            else if (me.Hp < gm.MaxHp(me) * gm.Cfg.progression.retreatHealth * gm.BotSkill) me.Retreating = true;
            if (me.Retreating && me.Lair != null)
            {
                if (me.Hp >= gm.MaxHp(me) * 0.85f) { me.Retreating = false; nextPlan = 0; }
                else
                {
                    var current = gm.Map.RoomContainingWorld(me.Pos);
                    var escape = current != null && gm.RoomsByDef.TryGetValue(current, out var room) && room.DoorBlocks
                        ? room.Def.DoorInside : me.Lair.Def.BedTile;
                    return navigator.Steer(ref me.Pos, escape, gm.MonsterWalkable, GameManager.MonsterRadius, dt, now, gm.Walls);
                }
            }
            if (now >= nextAbilityThink) { nextAbilityThink = now + Mathf.Lerp(3.5f, 0f, gm.BotSkill); ThinkAbilities(); }
            // Phased into a room: go straight for its resident before the phase runs out.
            if (me.PhasedRoom != null && me.PhasedRoom.Owner != null && me.PhasedRoom.Owner.Alive)
            {
                var prey = me.PhasedRoom.Owner;
                if (Vector2.Distance(me.Pos, prey.Pos) <= gm.Cfg.monsters.attackReachTiles * 0.8f) return Vector2.zero;
                return navigator.Steer(ref me.Pos, HotelMap.ToTile(prey.Pos), gm.MonsterWalkable, GameManager.MonsterRadius, dt, now, gm.Walls);
            }
            nextUpgrade -= dt;
            if (nextUpgrade <= 0f) { nextUpgrade = Mathf.Lerp(8f, 2f, gm.BotSkill); ThinkUpgrades(); }

            bool busy = me.Biting != null || me.EatingPart != null ||
                        (me.AttackingRoom != null && targetRes != null && me.AttackingRoom == targetRes.Room);
            if (!busy && (me.Pos - lastPos).sqrMagnitude < 0.0004f) stuckTime += dt; else stuckTime = 0f;
            lastPos = me.Pos;

            nextPlan -= dt;
            if (targetRes?.Room != null && targetRes.Room.DoorBroken && GoalIsDoor()) nextPlan = 0;
            bool targetMoved = targetRes != null && gm.CanSee(me.Pos,targetRes.Pos,gm.Cfg.residents.visionRadiusTiles+gm.RevealRadius(me)) && (HotelMap.ToTile(targetRes.Pos) - goal).sqrMagnitude > 4 && !GoalIsDoor();
            if (nextPlan <= 0f || stuckTime > 1.2f || path == null || TargetGone() || targetMoved)
            {
                if (now >= commitUntil || TargetGone() || targetRes?.Room == null || targetRes.Room.DoorBroken) Plan();
                nextPlan = Mathf.Lerp(4f, 1.5f, gm.BotSkill);
                stuckTime = 0f;
            }

            if (busy)
            {
                if (me.AttackingRoom != null && me.AttackingRoom.DoorBlocks)
                {
                    var room = me.AttackingRoom;
                    var center = HotelMap.Center(room.Def.DoorOutside);
                    var normal = (Vector2)(room.Def.DoorInside-room.Def.DoorTile);
                    var tangent = new Vector2(normal.y,-normal.x);
                    var best = center; float threat=gm.ThreatAt(center,me);
                    foreach (float side in new[] {-0.45f,0.45f})
                    {
                        var candidate=center+tangent*side;
                        float score=gm.ThreatAt(candidate,me);
                        if (score<threat && TileMovement.Clear(me.Pos,candidate,gm.MonsterWalkable,GameManager.MonsterRadius,gm.Walls)) { best=candidate; threat=score; }
                    }
                    return Vector2.ClampMagnitude((best-me.Pos)*5,1);
                }
                return Vector2.zero;
            }
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
            float speed = Mathf.Max(0.5f, gm.MonsterSpeed(me, gm.Now));
            float bite = Mathf.Max(1f, gm.ResidentDps(me));
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
                        float dps = Mathf.Max(1f, gm.DoorDps(me) * (1f - resist));
                        work += room.DoorHp / dps;
                    }
                    else
                    {
                        if(gm.CanSee(me.Pos,r.Pos,gm.Cfg.residents.visionRadiusTiles+gm.RevealRadius(me))) g = rTile;
                        else if(rInside)
                            // Search a known room after breaking in; don't track an unseen guest's live position.
                            g = room.Def.ContainsInterior(start) ? room.Def.BedTile : room.Def.DoorInside;
                        else continue;
                    }

                    var p = Pathfinding.FindPath(start, g, gm.MonsterWalkable);
                    if (p == null) continue;
                    float travel = p.Count / speed;
                    float threat = gm.ThreatAt(HotelMap.Center(g), me);
                    float cost = travel + work * (1f + threat / 60f) - KillDesire;
                    float risk = 1 + gm.Night * gm.Cfg.progression.riskPerNight + (gm.Now-me.LastKillAt)/60 * gm.Cfg.progression.riskPerMinuteWithoutKill;
                    cost += Mathf.Max(0, work - me.Hp/Mathf.Max(1,threat)) / risk;
                    if (room != null) cost += room.WeaponCount() * 0.25f - (room.Def.Isolated ? 2f : 0f);
                    if (!gm.Metrics.DoorAssaultsPerNight.ContainsKey(gm.Night) && rInside && room.DoorBlocks)
                        cost = travel*3f + work*0.15f + threat*0.02f;
                    if (!rInside) cost -= 6f;                                     // a resident in the hallway is a gift
                    cost += Random.Range(0f, (1f - gm.BotSkill) * 20f);   // a novice picks its prey carelessly
                    if (r == targetRes) { keepCost = cost; keepGoal = g; }
                    if (cost < bestCost) { bestCost = cost; bestRes = r; bestPart = null; bestGoal = g; }
                }
            }

            int maxParts = gm.Cfg.bodyParts.maxPartsPerType;
            foreach (var part in gm.Parts)
            {
                bool mustAssault = !gm.Metrics.DoorAssaultsPerNight.ContainsKey(gm.Night) && gm.Cfg.match.nightSeconds-gm.PhaseTimer >= gm.Cfg.progression.commitAfterSeconds;
                if (mustAssault || me.Parts[part.TypeIndex] >= maxParts) continue;
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
            if (bestRes != null && bestRes.Room != null && bestRes.Room.DoorBlocks) commitUntil = gm.Now + 14f;
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
            if (targetRes != null && !GoalIsDoor() && gm.CanSee(me.Pos,targetRes.Pos,gm.Cfg.residents.visionRadiusTiles+gm.RevealRadius(me))) goal = HotelMap.ToTile(targetRes.Pos);
            return navigator.Steer(ref me.Pos, goal, gm.MonsterWalkable, GameManager.MonsterRadius, tickDt, tickNow, gm.Walls);
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
                    case "residentSlowZone": use = me.Biting != null; break;
                    case "dash":
                        use = targetRes != null && !GoalIsDoor() && gm.ClearLine(me.Pos, targetRes.Pos) && Vector2.Distance(me.Pos,targetRes.Pos)>4;
                        break;
                    case "flambe":
                        use = ResidentsNear(a.radius) > 0 || (room != null && room.DoorBlocks && Vector2.Distance(me.Pos, HotelMap.Center(room.Def.DoorTile)) <= a.radius);
                        break;
                    case "sporeBloom":
                        use = ResidentsNear(a.radius) > 0;
                        break;
                    case "lastCall":
                        use = TowersNear(a.radius) >= 2 || (ResidentsNear(a.radius) > 0 && gm.ThreatAt(me.Pos, me) > 20f);
                        break;
                    case "meatHook":
                        use = HookTarget(a.radius);
                        break;
                    case "graveroot":
                        use = gm.Rifts.Exists(r => Vector2.Distance(r.Pos, me.Pos) < 8f);
                        break;
                    case "doNotDisturb":
                        // Worth it when the resident is close enough to reach and hurt inside the phase.
                        use = room != null && room.DoorBlocks && room.DoorHp > gm.DoorDps(me) * 3f && room.Owner != null &&
                              Vector2.Distance(HotelMap.Center(room.Def.DoorInside), room.Owner.Pos) < gm.MonsterSpeed(me, gm.Now) * a.durationSeconds * 0.5f;
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

        int ResidentsNear(float radius)
        {
            int n = 0;
            foreach (var r in gm.Residents) if (r.Alive && Vector2.Distance(r.Pos, me.Pos) <= radius && gm.ClearLine(me.Pos, r.Pos)) n++;
            return n;
        }

        int TowersNear(float radius)
        {
            int n = 0;
            foreach (var room in gm.RoomsByDef.Values)
            {
                if (room.Owner == null || !room.Owner.Alive) continue;
                foreach (var t in room.Slots) if (t != null && t.IsWeapon && Vector2.Distance(HotelMap.Center(t.Tile), me.Pos) <= radius) n++;
            }
            return n;
        }

        bool HookTarget(float range)
        {
            foreach (var p in gm.Parts)
                if (me.Parts[p.TypeIndex] < gm.Cfg.bodyParts.maxPartsPerType && Vector2.Distance(me.Pos, HotelMap.Center(p.Tile)) <= range && gm.ClearLine(me.Pos, HotelMap.Center(p.Tile))) return true;
            foreach (var r in gm.Residents)
            {
                float d = Vector2.Distance(me.Pos, r.Pos);
                if (r.Alive && d > 2f && d <= range && gm.ClearLine(me.Pos, r.Pos)) return true;
            }
            return false;
        }

        /// <summary>Picks which creature the rifts spawn, then spends Fear: the horde wakes on night 1, after that levels and
        /// horde purchases alternate so neither side of the tree is ignored.</summary>
        void ThinkUpgrades()
        {
            ChooseActiveMinion();
            for (int k = 0; k < 3 && SpendOnce(); k++) { }
        }

        /// <summary>Favour the creature that resists what the scouted towers shoot, a breacher against strong doors and the
        /// escort when the monster itself is under heavy fire.</summary>
        void ChooseActiveMinion()
        {
            if (!gm.HordeAwake(me) || !gm.CanSwitchMinion(me) || Random.value > gm.BotSkill) return;
            var tally = gm.ScoutTally(me, out _);
            float doors = 0f; int rooms = 0;
            foreach (var room in gm.RoomsByDef.Values)
                if (room.Owner != null && room.Owner.Alive && room != me.Lair) { doors += room.DoorLevel; rooms++; }
            doors = rooms > 0 ? doors / rooms : 1f;
            int best = me.ActiveMinion; float bestScore = float.MinValue;
            for (int i = 0; i < 3; i++)
            {
                if (!gm.MinionOwned(me, i)) continue;
                var c = gm.Creature(me, i);
                int resist = gm.ResistOf(c);
                float score = tally[resist] - tally[GameManager.Weakness(resist)] * .7f + (gm.MinionEvolved(me, i) ? 2f : 0f);
                if (c.role == "breacher") score += (doors - 2f) * 1.5f;
                if (c.role == "escort") score += gm.ThreatAt(me.Pos, me) > 30f ? 4f : -2f;
                if (i == me.ActiveMinion) score += 1f;   // a little stickiness, since a switch locks for the night
                if (score > bestScore) { bestScore = score; best = i; }
            }
            if (best != me.ActiveMinion && best != me.QueuedMinion) gm.TryQueueMinion(me, best);
        }

        bool SpendOnce()
        {
            if (gm.Phase != Phase.Night) return false;
            if (!gm.HordeAwake(me)) return gm.TryUnlockMinion(me, 0) == ActionResult.Ok;
            int horde = me.HordeStrength;
            for (int i = 0; i < 3; i++) horde += (gm.MinionOwned(me, i) ? 1 : 0) + (gm.MinionEvolved(me, i) ? 1 : 0);
            if (gm.LevelPrice(me) >= 0f && me.Level < 2 + 2 * horde) return gm.TryBuyLevel(me) == ActionResult.Ok;
            for (int i = 1; i < 3; i++)
                if (!gm.MinionOwned(me, i) && me.Level >= 4 * i + 1) return gm.TryUnlockMinion(me, i) == ActionResult.Ok;
            if (!gm.MinionEvolved(me, me.ActiveMinion) && me.HordeStrength >= 3) return gm.TryEvolveMinion(me, me.ActiveMinion) == ActionResult.Ok;
            if (gm.StrengthCost(me) >= 0f) return gm.TryUpgradeStrength(me) == ActionResult.Ok;
            return gm.LevelPrice(me) >= 0f && gm.TryBuyLevel(me) == ActionResult.Ok;
        }
    }
}
