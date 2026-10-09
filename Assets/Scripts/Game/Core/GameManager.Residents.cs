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
        public const float ResidentRadius = 0.3f;
        public const float MonsterRadius = 0.38f;

        [Range(.1f, .45f)] public float WallThickness = .3f;
        WallGraph movementWalls;
        float movementWallThickness;
        public WallGraph Walls
        {
            get
            {
                if (Map == null) return null;
                float thickness = Mathf.Clamp(WallThickness, .1f, .45f);
                if (movementWalls == null || movementWalls.Map != Map || !Mathf.Approximately(thickness, movementWallThickness))
                {
                    movementWalls = WallGraph.Build(Map, thickness);
                    movementWallThickness = thickness;
                }
                return movementWalls;
            }
        }

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

        bool CanStand(Vector2 p, System.Func<int, int, bool> walkable, float r)
        {
            return TileMovement.CanStand(p, walkable, r, Walls);
        }

        Vector2 Slide(Vector2 p, Vector2 delta, System.Func<int, int, bool> walkable, float r)
        {
            return TileMovement.Slide(p, delta, walkable, r, Walls);
        }

        Vector2 Unstick(Vector2 p, System.Func<int, int, bool> walkable, float r, Vector2 fallback)
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
            foreach (var room in RoomsByDef.Values)
                if (room.CloseWhenClear && !DoorwayOccupied(room))
                {
                    room.CloseWhenClear = false;
                    if (!room.DoorBroken) room.DoorOpen = false;
                    RefreshDoor(room);
                }
            float speed = Cfg.residents.moveSpeed;
            foreach (var r in Residents)
            {
                if (!r.Alive) continue;
                System.Func<int, int, bool> walk = (x, y) => WalkableFor(r, x, y);

                Vector2 move = Vector2.zero;
                if (r.IsHuman) move = HotelView3D.Move(GameInput.Move);
                else if (r.Ai != null) move = r.Ai.Tick(dt, now);

                if (r.SleepRequested && !r.Asleep)
                {
                    if (r.IsHuman && move.sqrMagnitude > 0.01f) r.SleepRequested = false;
                    else if (OnBed(r)) TrySleep(r);
                    else move = r.Navigator.Steer(ref r.Pos, r.Room.Def.BedTile, walk, ResidentRadius, dt, now, Walls);
                }
                // Sleeping guests stay anchored to their bed.
                if (r.Asleep) move = Vector2.zero;
                if (!r.Asleep && move.sqrMagnitude > 0.0001f)
                {
                    move = Vector2.ClampMagnitude(move, 1f);
                    r.Facing = move.normalized;
                    r.Pos = Slide(r.Pos, move * speed * (now < r.StunUntil ? 0f : now < r.SlowUntil ? 0.5f : 1f) * dt, walk, ResidentRadius);
                }
                if (!r.Asleep)
                    foreach (var other in Residents)
                    {
                        if (other == r || !other.Alive || other.Asleep) continue;
                        var away = r.Pos-other.Pos;
                        if (away.sqrMagnitude > 0.0001f && away.sqrMagnitude < 0.16f)
                            r.Pos = Slide(r.Pos, away.normalized * (0.4f-away.magnitude) * dt * 3f, walk, ResidentRadius);
                    }
                if (!CanStand(r.Pos, walk, ResidentRadius))
                    r.Pos = Unstick(r.Pos, walk, ResidentRadius, r.Room != null ? HotelMap.Center(r.Room.Def.BedTile) : HotelMap.Center(Map.Lobby));

                if (r.Room == null)
                {
                    var def = Map.RoomContaining(HotelMap.ToTile(r.Pos));
                    if (def != null && IsRoomFree(def)) Claim(r, def, false);
                }


            }
        }

        void PlaceResidentSprite(Resident r)
        {
            if (r.Sr == null) return;
            var t = r.Sr.transform;
            if (r.Asleep && r.Room != null)
            {
                r.Anim?.Rest();
                r.Sr.flipX = false;
                r.SleepBlend = Mathf.MoveTowards(r.SleepBlend, 1f, Time.deltaTime * 3.5f);
                float k = Mathf.SmoothStep(0f, 1f, r.SleepBlend);
                var bed = Cfg.beds.levels[r.Room.BedLevel - 1];
                float angle = r.Room.Def.BedRotation + bed.sleepRotation;
                var head = r.Anim != null ? r.Anim.RestHead : new Vector2(0f, 1.25f);
                var target = SleepPose.Position(r.Room.Def.BedCenter, r.Room.Def.BedRotation, bed, head, r.Room.BedSr.transform.localScale);
                var p = Vector2.Lerp(r.SleepFrom + Vector2.down * 0.3f, target, k);
                t.position = new Vector3(p.x, p.y, Mathf.Lerp(-0.06f, SleepZ(r.Room), k));
                t.rotation = Quaternion.Slerp(HotelView3D.SpriteRotation, Quaternion.Euler(0f, 0f, angle), k);
                t.localScale = Vector3.Lerp(HotelView3D.SpriteScale,Vector3.one,k);
                r.Sr.color = Color.Lerp(Color.white, new Color(0.8f, 0.8f, 0.95f, 1f), k);
                r.Sr.sortingOrder = r.Room.BedSr.sortingOrder + 2;
            }
            else
            {
                HotelView3D.Billboard(r.Sr, r.Pos);
                r.Sr.color = Color.white;
                r.Sr.sortingOrder = 5000 - Mathf.RoundToInt((r.Pos.x+r.Pos.y)*10);
                if (r.Anim == null) r.Sr.flipX = r.Facing.x < 0f;
            }
            if (r.Anim != null && !r.Asleep)
            {
                float dt = Mathf.Max(Time.deltaTime, 1e-4f);
                bool moving = !r.Asleep && (r.Pos - r.LastPos).magnitude / dt > 0.4f;
                bool attacking = !r.Asleep && Now < r.AttackUntil;
                r.Anim.Drive(HotelView3D.Facing(r.Facing), moving, attacking);
            }
            if (r.Model != null) PlaceResidentModel(r);
            ContactShadow.Place(r.Sr, r.Pos, !r.Asleep && r.Alive);
            r.LastPos = r.Pos;
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
            if (r.Room.Def.ContainsInterior(HotelMap.ToTile(r.Pos))) return ResidentAction.Sleep;
            if (NearDoor(r)) return r.Room.DoorOpen ? ResidentAction.CloseDoor : ResidentAction.OpenDoor;
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
            if (!r.Room.Def.ContainsInterior(HotelMap.ToTile(r.Pos))) return ActionResult.TooFar;
            r.Room.CloseWhenClear = !r.Room.DoorBroken;
            r.SleepRequested = true;
            if (!OnBed(r)) return ActionResult.Ok;
            r.SleepRequested = false;
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
            r.SleepRequested = false;
            if (r.IsHuman) AddFloater(r.Pos + Vector2.up, "Awake! Towers x" + Cfg.residents.awakeWeaponDamageMultiplier, (Color)Palette.Candle);
        }

        public ActionResult TryToggleDoor(Resident r)
        {
            if (r == null || !r.Alive || r.Room == null) return ActionResult.Invalid;
            var room = r.Room;
            if (!NearDoor(r)) return ActionResult.TooFar;
            if (room.DoorBroken) return ActionResult.Blocked;
            if (room.DoorOpen && DoorwayOccupied(room)) return ActionResult.Blocked;
            room.CloseWhenClear = false;
            room.DoorOpen = !room.DoorOpen;
            RefreshDoor(room);
            return ActionResult.Ok;
        }

        bool DoorwayOccupied(Room room)
        {
            var tile = room.Def.DoorTile;
            bool Clear(int x, int y) => x != tile.x || y != tile.y;
            if (Monster != null && !Monster.Dead && !TileMovement.CanStand(Monster.Pos, Clear, MonsterRadius)) return true;
            foreach (var r in Residents)
                if (r.Alive && !TileMovement.CanStand(r.Pos, Clear, ResidentRadius)) return true;
            return false;
        }

        void RefreshDoor(Room room)
        {
            ApplyDoorLook(room.Def, room);
        }

        // ------------------------------------------------------------------ economy

        void UpdateEconomy(float dt, float now)
        {
            foreach (var r in Residents)
            {
                if (!r.Alive || r.IsMonster || r.Room == null) continue;
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
                    f += UpgradeRules.DreamRate(Cfg.towers, t.Def, t.Level);
            return f;
        }

        public float FaithPerSecond(Room room)
        {
            float f = 0f;
            foreach (var t in room.Slots)
                if (t != null && t.IsFaith)
                    f += UpgradeRules.FaithRate(Cfg.towers, t.Def, t.Level);
            return room.Owner.IsMonster ? 0f : Income(f);
        }

        /// <summary>Bed income while asleep; awakeDreamPowerMultiplier (0 by default) while awake.</summary>
        public float DreamPerSecond(Resident r, float now)
        {
            if (r.Room == null || r.IsMonster) return 0f;
            float bed = Cfg.beds.levels[r.Room.BedLevel - 1].dreamPowerPerSecond;
            if (now < r.BedSlowUntil) bed *= r.BedSlowValue;
            return Income(bed) * (r.Asleep ? 1f : Cfg.residents.awakeDreamPowerMultiplier);
        }

        public float BedRate(Room room) => Cfg.beds.levels[room.BedLevel - 1].dreamPowerPerSecond;

        // ------------------------------------------------------------------ building

        bool CanAct(Resident r)
        {
            if (r != null && r.IsMonster) { if (r.IsHuman) Toast("You are hiding. Wait for lights out."); return false; }
            return r != null && r.Alive && r.Room != null && InMatch;
        }

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
            var levels = Weapons(room).Select(w => UpgradeRules.DoorSupportLevel(Cfg.towers, w.Def, w.Level)).ToArray();
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
            ApplyBedLook(room);
            AddFloater(room.Def.BedCenter + Vector2.up, Cfg.beds.levels[room.BedLevel - 1].name, (Color)Palette.Candle);
            return ActionResult.Ok;
        }

        /// <summary>Upgrades the door (and rebuilds it if broken). Enforces the 4-level gap rule and blinks the lowest weapon when blocked.</summary>
        public ActionResult TryUpgradeDoor(Resident r)
        {
            if (!CanAct(r)) return ActionResult.Invalid;
            var room = r.Room;
            if (Now < room.RotUntil) return ActionResult.Blocked;   // rotting doors cannot be worked on
            var weapons = Weapons(room);
            var check = UpgradeRules.CanUpgradeDoor(Cfg.doors, room.DoorLevel, weapons.Select(w => UpgradeRules.DoorSupportLevel(Cfg.towers, w.Def, w.Level)).ToArray());

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
            t.Sr = MakeSprite(def.name, TowerSprite(def, 1), pos, OrderFor(pos.y), matchRoot);
            if (!Simulation)
            {
                HotelView3D.Billboard(t.Sr, pos);
                ContactShadow.Attach(t.Sr, pos, new Vector2(.85f, .62f));
            }
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
        /// <summary>The tier art and name a tower shows (forms span several levels).</summary>
        public int TowerForm(TowerInstance t) => UpgradeRules.Form(Cfg.towers, t.Def, t.Level);
        public UpgradeRules.Step TowerNextStep(TowerInstance t) => UpgradeRules.NextStep(Cfg.towers, t.Def, t.Level);
        public string TowerName(TowerInstance t) => UpgradeRules.TowerName(Cfg.towers, t.Def, t.Level);
        public static Sprite TowerSprite(TowerDef def, int form) => TowerDirections.Get(def.id, form, Vector2.down) ?? Sprites.Tower(def, form);

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
            int form = TowerForm(t);
            t.Level++;
            if (TowerForm(t) != form)
            {
                t.Sr.sprite = TowerSprite(t.Def, TowerForm(t));
                if (!Simulation) HotelView3D.Billboard(t.Sr, HotelMap.Center(t.Tile));
            }
            // The player's own windows show their own level-up mark.
            if (!r.IsHuman) AddFloater(HotelMap.Center(t.Tile) + Vector2.up * 0.8f, "Lv " + t.Level, (Color)Palette.Bone);
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
            if (t.Sr != null) RemoveObject(t.Sr.gameObject);
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
                PendingHelpUntil = Now + 8f;
                AddLog(from.Name + " begs you for Dream Power.");
                return "";
            }
            bool canSpare = to.DreamPower > 120f && !to.Room.UnderAttack(Now);
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
