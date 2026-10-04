using System.Collections.Generic;
using System.Linq;
using BadAppleHotel.Config;
using BadAppleHotel.Rules;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>What the resident's single action button does right now.</summary>
    public enum ResidentAction { None, Wake, Sleep, OpenDoor, CloseDoor, DoorBroken }

    public partial class GameManager
    {
        const float ResidentRadius = 0.25f;
        const float MonsterRadius = 0.3f;

        // ------------------------------------------------------------------ walking

        /// <summary>Residents walk corridors, free rooms, and their own room (through their own door only when it is open).</summary>
        public bool WalkableFor(Resident r, int x, int y)
        {
            var v = new Vector2Int(x, y);
            switch (Map.Get(x, y))
            {
                case Tile.Corridor:
                    return true;
                case Tile.RoomFloor:
                {
                    if (TowerAt(v) != null) return false;
                    var def = Map.RoomContaining(v);
                    if (def == null || !RoomsByDef.TryGetValue(def, out var room)) return true;
                    return room.Owner == r;
                }
                case Tile.Door:
                {
                    var def = Map.RoomAtDoor(v);
                    if (def == null || !RoomsByDef.TryGetValue(def, out var room)) return true;
                    return room.Owner == r && !room.DoorBlocks;
                }
                default:
                    return false;
            }
        }

        /// <summary>The monster walks corridors and any room floor, but only through doors that are open, broken or unclaimed.</summary>
        public bool MonsterWalkable(int x, int y)
        {
            var v = new Vector2Int(x, y);
            switch (Map.Get(x, y))
            {
                case Tile.Corridor:
                    return true;
                case Tile.RoomFloor:
                    return TowerAt(v) == null;
                case Tile.Door:
                {
                    var def = Map.RoomAtDoor(v);
                    return def == null || !RoomsByDef.TryGetValue(def, out var room) || !room.DoorBlocks;
                }
                default:
                    return false;
            }
        }

        static bool CanStand(Vector2 p, System.Func<int, int, bool> walkable, float r)
        {
            return walkable(Mathf.FloorToInt(p.x - r), Mathf.FloorToInt(p.y - r))
                && walkable(Mathf.FloorToInt(p.x + r), Mathf.FloorToInt(p.y - r))
                && walkable(Mathf.FloorToInt(p.x - r), Mathf.FloorToInt(p.y + r))
                && walkable(Mathf.FloorToInt(p.x + r), Mathf.FloorToInt(p.y + r));
        }

        static Vector2 Slide(Vector2 p, Vector2 delta, System.Func<int, int, bool> walkable, float r)
        {
            var nx = new Vector2(p.x + delta.x, p.y);
            if (CanStand(nx, walkable, r)) p = nx;
            var ny = new Vector2(p.x, p.y + delta.y);
            if (CanStand(ny, walkable, r)) p = ny;
            return p;
        }

        static Vector2 Unstick(Vector2 p, System.Func<int, int, bool> walkable, float r, Vector2 fallback)
        {
            var c = HotelMap.ToTile(p);
            for (int radius = 1; radius <= 5; radius++)
                for (int dx = -radius; dx <= radius; dx++)
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        var q = HotelMap.Center(new Vector2Int(c.x + dx, c.y + dy));
                        if (CanStand(q, walkable, r)) return q;
                    }
            return fallback;
        }

        void UpdateResidents(float dt, float now)
        {
            float speed = Cfg.residents.moveSpeed;
            foreach (var r in Residents)
            {
                if (!r.Alive) continue;
                System.Func<int, int, bool> walk = (x, y) => WalkableFor(r, x, y);

                Vector2 move = Vector2.zero;
                if (r.IsHuman) move = GameInput.Move;
                else if (r.Ai != null) move = r.Ai.Tick(dt, now);

                if (r.Asleep && move.sqrMagnitude > 0.04f)
                {
                    if (r.IsHuman) Wake(r); // walking away wakes you up
                    else move = Vector2.zero;
                }
                if (!r.Asleep && move.sqrMagnitude > 0.0001f)
                {
                    move = Vector2.ClampMagnitude(move, 1f);
                    r.Facing = move.normalized;
                    r.Pos = Slide(r.Pos, move * speed * dt, walk, ResidentRadius);
                }
                if (!CanStand(r.Pos, walk, ResidentRadius))
                    r.Pos = Unstick(r.Pos, walk, ResidentRadius, r.Room != null ? HotelMap.Center(r.Room.Def.BedTile) : HotelMap.Center(Map.Lobby));

                if (r.Room == null)
                {
                    var def = Map.RoomContaining(HotelMap.ToTile(r.Pos));
                    if (def != null && IsRoomFree(def)) Claim(r, def, false);
                }

                PlaceResidentSprite(r);
            }
        }

        void PlaceResidentSprite(Resident r)
        {
            if (r.Sr == null) return;
            var t = r.Sr.transform;
            if (r.Asleep && r.Room != null)
            {
                // glide onto the bed at normal size (no scaling), then stay put slightly dimmed
                r.SleepBlend = Mathf.MoveTowards(r.SleepBlend, 1f, Time.deltaTime * 3.5f);
                float k = r.SleepBlend * r.SleepBlend * (3f - 2f * r.SleepBlend);
                var p = Vector2.Lerp(r.SleepFrom, r.Pos, k);
                t.position = new Vector3(p.x, p.y - 0.3f, 0f);
                t.rotation = Quaternion.identity;
                t.localScale = Vector3.one;
                r.Sr.color = Color.Lerp(Color.white, new Color(0.8f, 0.8f, 0.95f, 1f), k);
                r.Sr.sortingOrder = OrderFor(p.y - 0.3f) + (r.SleepBlend >= 1f ? 2 : 0);
            }
            else
            {
                t.position = new Vector3(r.Pos.x, r.Pos.y - 0.3f, 0f);
                t.rotation = Quaternion.identity;
                t.localScale = Vector3.one;
                r.Sr.color = Color.white;
                r.Sr.sortingOrder = OrderFor(r.Pos.y - 0.3f);
                r.Sr.flipX = r.Facing.x < 0f;
            }
        }

        // ------------------------------------------------------------------ sleep & doors

        public bool OnBed(Resident r) =>
            r != null && r.Room != null && Vector2.Distance(r.Pos, HotelMap.Center(r.Room.Def.BedTile)) <= Cfg.residents.bedReachTiles;

        public bool NearDoor(Resident r) =>
            r != null && r.Room != null && Vector2.Distance(r.Pos, HotelMap.Center(r.Room.Def.DoorTile)) <= Cfg.residents.doorReachTiles;

        public ResidentAction ActionFor(Resident r)
        {
            if (r == null || !r.Alive || r.Room == null || !InMatch) return ResidentAction.None;
            if (r.Asleep) return ResidentAction.Wake;
            if (NearDoor(r)) return r.Room.DoorBroken ? ResidentAction.DoorBroken : (r.Room.DoorOpen ? ResidentAction.CloseDoor : ResidentAction.OpenDoor);
            if (OnBed(r)) return ResidentAction.Sleep;
            return ResidentAction.None;
        }

        public ActionResult DoAction(Resident r)
        {
            switch (ActionFor(r))
            {
                case ResidentAction.Wake: Wake(r); return ActionResult.Ok;
                case ResidentAction.Sleep: return TrySleep(r);
                case ResidentAction.OpenDoor:
                case ResidentAction.CloseDoor: return TryToggleDoor(r);
                case ResidentAction.DoorBroken: return ActionResult.Blocked;
                default: return ActionResult.TooFar;
            }
        }

        public ActionResult TrySleep(Resident r)
        {
            if (r == null || !r.Alive || r.Room == null) return ActionResult.Invalid;
            if (!OnBed(r)) return ActionResult.TooFar;
            r.Asleep = true;
            r.SleepFrom = r.Pos;
            r.SleepBlend = 0f;
            r.Pos = HotelMap.Center(r.Room.Def.BedTile);
            AddFloater(r.Pos + Vector2.up, "Zzz", (Color)Palette.Mint);
            return ActionResult.Ok;
        }

        public void Wake(Resident r)
        {
            if (r == null || !r.Asleep) return;
            r.Asleep = false;
            if (r.IsHuman) AddFloater(r.Pos + Vector2.up, "Awake! Towers x" + Cfg.residents.awakeWeaponDamageMultiplier, (Color)Palette.Candle);
        }

        public ActionResult TryToggleDoor(Resident r)
        {
            if (r == null || !r.Alive || r.Room == null) return ActionResult.Invalid;
            var room = r.Room;
            if (!NearDoor(r)) return ActionResult.TooFar;
            if (room.DoorBroken) return ActionResult.Blocked;
            if (room.DoorOpen && DoorwayOccupied(room)) return ActionResult.Blocked;
            room.DoorOpen = !room.DoorOpen;
            RefreshDoor(room);
            return ActionResult.Ok;
        }

        bool DoorwayOccupied(Room room)
        {
            var c = HotelMap.Center(room.Def.DoorTile);
            if (Monster != null && !Monster.Dead && Vector2.Distance(Monster.Pos, c) < 0.8f) return true;
            foreach (var r in Residents)
                if (r.Alive && Vector2.Distance(r.Pos, c) < 0.6f) return true;
            return false;
        }

        void RefreshDoor(Room room)
        {
            if (room.DoorSr == null) return;
            room.DoorSr.sprite = room.DoorBroken ? Sprites.DoorBroken : room.DoorOpen ? Sprites.DoorOpen : Sprites.Door(room.DoorLevel);
        }

        // ------------------------------------------------------------------ economy

        void UpdateEconomy(float dt, float now)
        {
            foreach (var r in Residents)
            {
                if (!r.Alive || r.Room == null) continue;
                r.DreamPower += (DreamPerSecond(r, now) + DreamGenPerSecond(r.Room)) * dt;
                if (now < r.FaithBlockedUntil) continue;
                r.Faith += FaithPerSecond(r.Room) * dt;
            }
        }

        public float DreamGenPerSecond(Room room)
        {
            float f = 0f;
            foreach (var t in room.Slots)
                if (t != null && t.IsDreamGen)
                    f += t.Def.dreamPerSecond * Mathf.Pow(Mathf.Max(1f, t.Def.dreamLevelScaling), t.Level - 1);
            return f;
        }

        public float FaithPerSecond(Room room)
        {
            float f = 0f;
            foreach (var t in room.Slots)
                if (t != null && t.IsFaith)
                    f += t.Def.faithPerSecond * Mathf.Pow(t.Def.faithLevelScaling, t.Level - 1);
            return f;
        }

        /// <summary>Bed income while asleep; awakeDreamPowerMultiplier (0 by default) while awake.</summary>
        public float DreamPerSecond(Resident r, float now)
        {
            if (r.Room == null) return 0f;
            float bed = Cfg.beds.levels[r.Room.BedLevel - 1].dreamPowerPerSecond;
            if (now < r.BedSlowUntil) bed *= r.BedSlowValue;
            return bed * (r.Asleep ? 1f : Cfg.residents.awakeDreamPowerMultiplier);
        }

        public float BedRate(Room room) => Cfg.beds.levels[room.BedLevel - 1].dreamPowerPerSecond;

        // ------------------------------------------------------------------ building

        bool CanAct(Resident r) => r != null && r.Alive && r.Room != null && InMatch;

        public float Wallet(Resident r, string res) => res == "faith" ? r.Faith : r.DreamPower;

        bool Spend(Resident r, string res, float cost)
        {
            if (Wallet(r, res) + 0.001f < cost) return false;
            if (res == "faith") r.Faith -= cost; else r.DreamPower -= cost;
            return true;
        }

        void Earn(Resident r, string res, float amount)
        {
            if (res == "faith") r.Faith += amount; else r.DreamPower += amount;
        }

        public TowerDef TowerById(string id) => Cfg.towers.towers.FirstOrDefault(t => t.id == id);

        public TowerInstance TowerAt(Vector2Int tile)
        {
            var def = Map.RoomContaining(tile);
            if (def == null || !RoomsByDef.TryGetValue(def, out var room)) return null;
            int i = def.BuildIndex(tile);
            return i >= 0 && i < room.Slots.Length ? room.Slots[i] : null;
        }

        public float MaxDoorHp(Room room) => Cfg.doors.levels[room.DoorLevel - 1].health;

        public float BedUpgradeCost(Room room) =>
            room.BedLevel >= Cfg.beds.levels.Length ? -1f : Cfg.beds.levels[room.BedLevel - 1].upgradeCost;

        public List<TowerInstance> Weapons(Room room)
        {
            var list = new List<TowerInstance>();
            foreach (var t in room.Slots) if (t != null && t.IsWeapon) list.Add(t);
            return list;
        }

        public DoorUpgradeResult CheckDoor(Room room)
        {
            var levels = Weapons(room).Select(w => w.Level).ToArray();
            return UpgradeRules.CanUpgradeDoor(Cfg.doors, room.DoorLevel, levels);
        }

        public float DoorRepairCostAtMax(Room room) =>
            Cfg.doors.levels[Mathf.Max(0, room.DoorLevel - 2)].upgradeCost;

        public ActionResult TryUpgradeBed(Resident r)
        {
            if (!CanAct(r)) return ActionResult.Invalid;
            var room = r.Room;
            float cost = BedUpgradeCost(room);
            if (cost < 0f) return ActionResult.MaxLevel;
            if (!Spend(r, "dreamPower", cost)) return ActionResult.NoMoney;
            room.BedLevel++;
            room.BedSr.sprite = Sprites.Bed(room.BedLevel);
            AddFloater(HotelMap.Center(room.Def.BedTile) + Vector2.up, Cfg.beds.levels[room.BedLevel - 1].name, (Color)Palette.Candle);
            return ActionResult.Ok;
        }

        /// <summary>Upgrades the door (and rebuilds it if broken). Enforces the 4-level gap rule and blinks the lowest weapon when blocked.</summary>
        public ActionResult TryUpgradeDoor(Resident r)
        {
            if (!CanAct(r)) return ActionResult.Invalid;
            var room = r.Room;
            var weapons = Weapons(room);
            var check = UpgradeRules.CanUpgradeDoor(Cfg.doors, room.DoorLevel, weapons.Select(w => w.Level).ToArray());

            if (check.AtMaxLevel)
            {
                if (!room.DoorBroken) return ActionResult.MaxLevel;
                if (!Spend(r, "dreamPower", DoorRepairCostAtMax(room))) return ActionResult.NoMoney;
                RebuildDoor(room);
                return ActionResult.Ok;
            }

            if (!check.Allowed)
            {
                float until = Time.unscaledTime + Cfg.doors.blockedBlinkSeconds;
                if (check.BlinkWeaponIndex >= 0 && check.BlinkWeaponIndex < weapons.Count)
                    weapons[check.BlinkWeaponIndex].BlinkUntil = until;
                else
                    room.NoWeaponBlinkUntil = until;
                return ActionResult.Blocked;
            }

            if (!Spend(r, "dreamPower", check.Cost)) return ActionResult.NoMoney;
            room.DoorLevel++;
            RebuildDoor(room);
            return ActionResult.Ok;
        }

        void RebuildDoor(Room room)
        {
            bool wasBroken = room.DoorBroken;
            room.DoorBroken = false;
            room.DoorHp = MaxDoorHp(room);
            if (wasBroken && !DoorwayOccupied(room)) room.DoorOpen = false; // rebuilt doors come back shut
            RefreshDoor(room);
            if (Monster != null && !Monster.Dead && !CanStand(Monster.Pos, MonsterWalkable, MonsterRadius))
                Monster.Pos = Unstick(Monster.Pos, MonsterWalkable, MonsterRadius, HotelMap.Center(Map.MonsterSpawn));
            AddFloater(HotelMap.Center(room.Def.DoorTile) + Vector2.up, "Door " + room.DoorLevel, (Color)Palette.Teal);
        }

        public ActionResult TryBuildTower(Resident r, int slot, string towerId)
        {
            if (!CanAct(r)) return ActionResult.Invalid;
            var room = r.Room;
            var def = TowerById(towerId);
            if (def == null || slot < 0 || slot >= room.Slots.Length || room.Slots[slot] != null)
                return ActionResult.Invalid;
            if (!CanBuildAt(room, slot)) return ActionResult.Blocked;
            if (!Spend(r, def.costResource, def.buildCost)) return ActionResult.NoMoney;
            PlaceTower(room, slot, def);
            return ActionResult.Ok;
        }

        /// <summary>A building may go on any free room tile as long as the bed stays reachable from the door.</summary>
        public bool CanBuildAt(Room room, int slot)
        {
            if (slot < 0 || slot >= room.Slots.Length || room.Slots[slot] != null) return false;
            var def = room.Def;
            var blocked = new HashSet<Vector2Int> { def.BuildTiles[slot] };
            for (int i = 0; i < room.Slots.Length; i++) if (room.Slots[i] != null) blocked.Add(def.BuildTiles[i]);
            var seen = new HashSet<Vector2Int> { def.DoorInside };
            var q = new Queue<Vector2Int>();
            q.Enqueue(def.DoorInside);
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                if (c == def.BedTile) return true;
                foreach (var d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
                {
                    var n = c + d;
                    if (!def.FloorSet.Contains(n) || blocked.Contains(n) || !seen.Add(n)) continue;
                    q.Enqueue(n);
                }
            }
            return false;
        }

        void PlaceTower(Room room, int slot, TowerDef def)
        {
            var tile = room.Def.BuildTiles[slot];
            var pos = HotelMap.Center(tile);
            var t = new TowerInstance { Def = def, Level = 1, SlotIndex = slot, Tile = tile };
            t.Sr = MakeSprite(def.name, Sprites.Tower(def.id), pos, OrderFor(pos.y), matchRoot);
            room.Slots[slot] = t;

            // anyone standing on the plate gets nudged off it
            foreach (var r in Residents)
            {
                if (!r.Alive) continue;
                var res = r;
                System.Func<int, int, bool> walk = (x, y) => WalkableFor(res, x, y);
                if (!CanStand(r.Pos, walk, ResidentRadius)) r.Pos = Unstick(r.Pos, walk, ResidentRadius, HotelMap.Center(room.Def.BedTile));
            }
            if (Monster != null && !Monster.Dead && !CanStand(Monster.Pos, MonsterWalkable, MonsterRadius))
                Monster.Pos = Unstick(Monster.Pos, MonsterWalkable, MonsterRadius, HotelMap.Center(Map.MonsterSpawn));
        }

        public int TowerMaxLevel(TowerInstance t) => UpgradeRules.TowerMaxLevel(Cfg.towers, t.Def);

        public float TowerUpgradeCost(TowerInstance t) =>
            t.Level >= TowerMaxLevel(t) ? -1f : UpgradeRules.TowerUpgradeCost(Cfg.towers, t.Def, t.Level);

        public float TowerSellValue(TowerInstance t) => UpgradeRules.TowerSellValue(Cfg.towers, t.Def, t.Level);

        public ActionResult TryUpgradeTower(Resident r, int slot)
        {
            if (!CanAct(r)) return ActionResult.Invalid;
            if (slot < 0 || slot >= r.Room.Slots.Length) return ActionResult.Invalid;
            var t = r.Room.Slots[slot];
            if (t == null) return ActionResult.Invalid;
            float cost = TowerUpgradeCost(t);
            if (cost < 0f) return ActionResult.MaxLevel;
            if (!Spend(r, t.Def.costResource, cost)) return ActionResult.NoMoney;
            t.Level++;
            AddFloater(HotelMap.Center(t.Tile) + Vector2.up * 0.8f, "Lv " + t.Level, (Color)Palette.Bone);
            return ActionResult.Ok;
        }

        public ActionResult TrySellTower(Resident r, int slot)
        {
            if (!CanAct(r)) return ActionResult.Invalid;
            if (slot < 0 || slot >= r.Room.Slots.Length) return ActionResult.Invalid;
            var t = r.Room.Slots[slot];
            if (t == null) return ActionResult.Invalid;
            float refund = TowerSellValue(t);
            Earn(r, t.Def.costResource, refund);
            if (t.Sr != null) Destroy(t.Sr.gameObject);
            r.Room.Slots[slot] = null;
            AddFloater(HotelMap.Center(t.Tile) + Vector2.up * 0.8f, "+" + Mathf.RoundToInt(refund) + (t.Def.costResource == "faith" ? " Faith" : " DP"), (Color)Palette.Candle);
            return ActionResult.Ok;
        }

        // ------------------------------------------------------------------ sharing

        public Resident NearestNeighbour(Resident from)
        {
            if (from == null || from.Room == null) return null;
            Resident best = null;
            float bestD = float.MaxValue;
            foreach (var r in Residents)
            {
                if (r == from || !r.Alive || r.Room == null) continue;
                float d = Vector2.Distance(HotelMap.Center(r.Room.Def.DoorTile), HotelMap.Center(from.Room.Def.DoorTile));
                if (d < bestD) { bestD = d; best = r; }
            }
            return best;
        }

        public string AskForHelp(Resident from)
        {
            if (!CanAct(from) || !Cfg.economy.sharing.enabled) return "Sharing is off.";
            var to = NearestNeighbour(from);
            if (to == null) return "Nobody can hear you.";
            if (to.IsHuman)
            {
                PendingHelpFrom = from;
                PendingHelpUntil = Time.time + 8f;
                AddLog(from.Name + " begs you for Dream Power.");
                return "";
            }
            bool canSpare = to.DreamPower > 120f && !to.Room.UnderAttack(Time.time);
            if (!canSpare)
            {
                AddLog(to.Name + " refuses to share with " + (from.IsHuman ? "you" : from.Name) + ".");
                return to.Name + " refused.";
            }
            float amount = Mathf.Max(Cfg.economy.sharing.minTransfer, to.DreamPower * 0.25f);
            Transfer(to, from, amount);
            AddLog(to.Name + " sent " + (from.IsHuman ? "you" : from.Name) + " " + Mathf.RoundToInt(amount) + " Dream Power.");
            return to.Name + " sent " + Mathf.RoundToInt(amount) + ".";
        }

        public void AnswerHelp(bool accept)
        {
            var from = PendingHelpFrom;
            PendingHelpFrom = null;
            if (from == null || Human == null || !Human.Alive) return;
            if (!accept) { AddLog("You ignored " + from.Name + "."); return; }
            float amount = Mathf.Min(Human.DreamPower, Mathf.Max(Cfg.economy.sharing.minTransfer, Human.DreamPower * 0.25f));
            Transfer(Human, from, amount);
            AddLog("You sent " + from.Name + " " + Mathf.RoundToInt(amount) + " Dream Power.");
        }

        void Transfer(Resident a, Resident b, float amount)
        {
            amount = Mathf.Min(amount, a.DreamPower);
            if (amount <= 0f) return;
            a.DreamPower -= amount;
            b.DreamPower += amount;
        }
    }
}
