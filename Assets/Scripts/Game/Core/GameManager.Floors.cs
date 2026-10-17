using System.Collections.Generic;
using System.Linq;
using BadAppleHotel.Config;
using BadAppleHotel.Rules;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// The hallway tower-defense match. Guests check in at the front desk one at a time (random order) and pick a
    /// floor; each floor is a winding carpeted hallway from the lift to their room. After a building break, every night
    /// one living guest is chosen at random to be the monster. Minion waves leave the lift on every other floor, the
    /// monster rides the lift to the floor it wants to hunt (that floor's minions get its buff), and Fear, shared by
    /// whoever is the monster, permanently grows each floor's waves. The last guest alive wins.
    /// </summary>
    public partial class GameManager
    {
        /// <summary>Tests and capture tools can ask for the older single connected hotel instead of the floors.</summary>
        public bool LegacyHotel { get; set; }
        public bool Floors => Map != null && Map.IsFloors;

        // ------------------------------------------------------------------ front desk

        public readonly List<Resident> CheckInOrder = new List<Resident>();
        public int CheckInIndex { get; private set; }
        public Resident CheckingIn => Phase == Phase.CheckIn && CheckInIndex < CheckInOrder.Count ? CheckInOrder[CheckInIndex] : null;
        float checkInBotAt;

        /// <summary>True while the human is the guest the manager is talking to.</summary>
        public bool HumanAtDesk => CheckingIn != null && CheckingIn.IsHuman;

        void StartCheckIn()
        {
            CheckInOrder.Clear();
            CheckInOrder.AddRange(Residents.OrderBy(_ => Random.value));
            CheckInIndex = 0;
            Phase = Phase.CheckIn;
            PhaseTimer = Cfg.match.checkInSeconds;
            checkInBotAt = Random.Range(0.8f, 1.6f);
            foreach (var r in Residents) if (r.Sr != null) r.Sr.enabled = false;
            Announce("The clerk rings the bell. Guests come up to the desk one at a time.", 4f);
        }

        void UpdateCheckIn(float dt)
        {
            var guest = CheckingIn;
            if (guest == null) { FinishCheckIn(); return; }
            checkInBotAt -= dt;
            if (!guest.IsHuman && checkInBotAt <= 0f) PickFloor(guest, BotFloorPick());
            else if (PhaseTimer <= 0f) PickFloor(guest, BotFloorPick());
        }

        /// <summary>The human (or a bot) takes a floor at the desk; the lift takes them straight up to their room.</summary>
        public bool PickFloor(Resident r, RoomDef def)
        {
            if (Phase != Phase.CheckIn || r == null || r != CheckingIn || def == null || !IsRoomFree(def)) return false;
            if (!Claim(r, def, true)) return false;
            if (r.Sr != null) r.Sr.enabled = true;
            LastKeyTaken = def; LastKeyTakenBy = r; LastKeyTakenAt = Time.unscaledTime;
            r.LastPos = r.Pos;
            AddLog(r.Name + " took Room " + def.RoomNumber + " on floor " + def.FloorNumber + ".");
            CheckInIndex++;
            PhaseTimer = Cfg.match.checkInSeconds;
            checkInBotAt = Random.Range(0.9f, 1.8f);
            if (CheckingIn == null) FinishCheckIn();
            return true;
        }

        /// <summary>Nobody sees the floor plans at the desk, so a bot just takes one of the keys still hanging.</summary>
        RoomDef BotFloorPick()
        {
            var free = Map.Rooms.Where(IsRoomFree).ToList();
            return free.Count == 0 ? null : free[Random.Range(0, free.Count)];
        }

        /// <summary>The last key taken at the desk and when (the HUD flies it from its hook to the counter).</summary>
        public RoomDef LastKeyTaken { get; private set; }
        public Resident LastKeyTakenBy { get; private set; }
        public float LastKeyTakenAt { get; private set; } = -9f;

        void FinishCheckIn()
        {
            Phase = Phase.Setup;
            PhaseTimer = Cfg.match.buildSeconds;
            Night = 0;
            Announce("Everyone has a room. Build your hallway before lights out!", 4f);
        }

        // ------------------------------------------------------------------ nights

        /// <summary>The guest who is the monster tonight (null between nights).</summary>
        public Resident MonsterGuest { get; private set; }
        Resident lastMonsterGuest;
        /// <summary>Fear is shared: whatever one monster leaves unspent, the next one inherits.</summary>
        public float Fear { get; private set; }
        public float FearEarned { get; private set; }
        /// <summary>Each monster kind's floor buff for this match: 0 damage, 1 speed, 2 health (rolled at check-in).</summary>
        readonly Dictionary<string, int> monsterBuffs = new Dictionary<string, int>();
        /// <summary>Set by the menu's Play as Monster: the human takes the first night.</summary>
        bool humanFirstMonster;

        public static readonly string[] BuffNames = { "Damage", "Speed", "Health" };
        public int BuffOf(MonsterDef def) => def != null && monsterBuffs.TryGetValue(def.id, out int b) ? b : 0;
        public string BuffText(MonsterDef def)
        {
            var w = Cfg.waves;
            switch (BuffOf(def))
            {
                case 0: return "+" + Mathf.RoundToInt(w.buffDamage * 100) + "% minion damage";
                case 1: return "+" + Mathf.RoundToInt(w.buffSpeed * 100) + "% minion speed";
                default: return "+" + Mathf.RoundToInt(w.buffHealth * 100) + "% minion health";
            }
        }

        void RollMonsterBuffs()
        {
            monsterBuffs.Clear();
            foreach (var def in Cfg.monsters.monsters) monsterBuffs[def.id] = Random.Range(0, 3);
        }

        /// <summary>Lights out: a random living guest (never the same one twice in a row) turns into a random monster.</summary>
        void StartFloorNight()
        {
            Night++;
            Phase = Phase.Night;
            PhaseTimer = Cfg.match.nightSeconds;
            HotelView = false;
            var alive = Residents.Where(r => r.Alive).ToList();
            var pool = alive.Count > 1 ? alive.Where(r => r != lastMonsterGuest).ToList() : alive;
            var guest = humanFirstMonster && Human != null && Human.Alive ? Human : pool[Random.Range(0, pool.Count)];
            humanFirstMonster = false;
            MonsterGuest = lastMonsterGuest = guest;
            guest.IsMonster = true;
            if (guest.IsHuman) { HumanRole = Role.Monster; sleepCameraActive = false; }
            // The guest sleeps through the night; the monster is what they dream up.
            guest.SleepRequested = false;
            guest.Asleep = true;
            guest.SleepFrom = guest.Pos;
            guest.SleepBlend = 0f;
            guest.Pos = HotelMap.Center(guest.Room.Def.BedTile);
            if (guest.Room.DoorOpen && !guest.Room.DoorBroken) { guest.Room.DoorOpen = false; RefreshDoor(guest.Room); }

            var defs = Cfg.monsters.monsters;
            var def = defs[Random.Range(0, defs.Length)];
            var m = CreateMonster(def, guest.IsHuman, HotelMap.Center(Map.Rooms[0].LiftLanding));
            m.Guest = guest;
            m.Level = Night;
            // A fresh monster every night, so it gets its whole kit at once.
            AddToLoadout(m, Cfg.abilities.abilities.First(a => a.id == m.Def.special));
            m.Hp = MaxHp(m);
            EnterLift(m);
            AddFear(m, Cfg.progression.fear.perNight);
            StartNightRifts();
            string who = guest.IsHuman ? "You are" : guest.Name + " is";
            Announce("Night " + Night + ". " + who + " " + def.name + " tonight. Its floor buff: " + BuffText(def) + ".", 5f);
            AddLog(guest.Name + " became " + def.name + ".");
        }

        Monster CreateMonster(MonsterDef def, bool human, Vector2 pos)
        {
            var m = new Monster { Def = def, IsHuman = human, Pos = pos, RevealedAt = Now };
            Monster = m;
            InitializeProgression(m);
            m.Hp = MaxHp(m);
            m.Sr = MakeSprite("Monster", Sprites.Monster(m.Def.id), m.Pos, OrderFor(m.Pos.y), matchRoot);
            if (!Simulation) ContactShadow.Attach(m.Sr, m.Pos, new Vector2(.8f, .56f));
            m.Model = MonsterModel(m);
            if (m.Model == null && Sprites.UseArt) m.Anim = CharacterAnimator.Attach(m.Sr, CharacterSet.Load(m.Def.id, 2.1f));
            if (!human)
            {
                if (Floors) m.FloorAi = new FloorMonsterAI(this, m);
                else m.Ai = new MonsterAI(this, m);
            }
            return m;
        }

        /// <summary>Dawn: the monster turns back into its guest, the hallways clear and everyone gets a building break.</summary>
        void EndFloorNight()
        {
            foreach (var r in Residents) if (r.Alive) r.NightsSurvived++;
            var m = Monster;
            if (m != null)
            {
                if (m.Sr != null) RemoveObject(m.Sr.gameObject);
                if (m.Model != null) RemoveObject(m.Model.gameObject);
            }
            Monster = null;
            ClearMinions();
            ClearHazards();
            if (MonsterGuest != null) MonsterGuest.IsMonster = false;
            MonsterGuest = null;
            if (HumanRole == Role.Monster) { sleepCameraActive = false; floorCameraSet = false; }
            HumanRole = Role.Resident;
            Phase = Phase.Setup;
            PhaseTimer = Cfg.match.duskSeconds;
            Announce("Dawn. " + Residents.Count(r => r.Alive) + " guests left. The next monster wakes soon...", 4f);
        }

        /// <summary>Last guest standing (or the human eliminated): the match ends.</summary>
        void CheckFloorsWinner()
        {
            int alive = Residents.Count(r => r.Alive);
            if (alive <= 1 || (Human != null && !Human.Alive && !Simulation)) EndMatch();
        }

        public Resident Winner => Residents.Count(r => r.Alive) == 1 ? Residents.First(r => r.Alive) : null;

        void StepFloors(float dt)
        {
            if (Phase == Phase.CheckIn) { UpdateCheckIn(dt); return; }
            UpdateResidents(dt, Now);
            UpdateEconomy(dt, Now);
            UpdateProgression(dt, Now);
            UpdateMonster(dt, Now);
            UpdateMinions(dt, Now);
            UpdateTowers(dt, Now);
            if (PendingHelpFrom != null && Now > PendingHelpUntil) PendingHelpFrom = null;
            if (!InMatch) return;
            if (Phase == Phase.Setup && PhaseTimer <= 0f) BeginNights();
            else if (Phase == Phase.Night)
            {
                CheckFloorsWinner();
                if (Phase == Phase.Night && PhaseTimer <= 0f) EndNight();
            }
        }

        void EndFloorsMatch()
        {
            Phase = Phase.Results;
            HotelView = false;
            var winner = Winner;
            int alive = Residents.Count(r => r.Alive);
            var res = new MatchResult
            {
                ResidentsWin = Human != null && Human.Alive,
                Kills = Residents.Count(r => !r.Alive),
                Role = Role.Resident,
                Placement = Human == null ? 0 : Human.Alive ? 1 : alive + 1 + Residents.Count(r => !r.Alive && r.DiedAt > Human.DiedAt),
                WinnerName = winner?.Name,
            };
            res.LevelBefore = AccountProgress.Level(Cfg, Role.Resident);
            if (Human != null) res.XpGained = EconomyRules.ResidentAccountXp(Cfg.economy, Human.NightsSurvived, Mathf.Max(1, Night));
            if (!Simulation) AccountProgress.AddXp(Role.Resident, res.XpGained);
            res.LevelAfter = AccountProgress.Level(Cfg, Role.Resident);
            Result = res;
            Announce(winner != null ? (winner.IsHuman ? "You are the last guest standing!" : winner.Name + " is the last guest standing.")
                : "Nobody made it to check-out.", 6f);
        }

        // ------------------------------------------------------------------ the lift

        public const float LiftRideSeconds = 1.4f;

        /// <summary>The floors a monster can ride to: every floor with a living guest except its own.</summary>
        public IEnumerable<RoomDef> HuntableFloors(Monster m) =>
            Map.Rooms.Where(d => RoomsByDef.TryGetValue(d, out var room) && room.Owner != null && room.Owner.Alive && room.Owner != m.Guest);

        public bool NearLift(Monster m) =>
            m != null && !m.InLift && m.Floor != null && Vector2.Distance(m.Pos, HotelMap.Center(m.Floor.LiftLanding)) <= 1.6f;

        /// <summary>Steps into the lift: hidden and safe while the floor buttons are up.</summary>
        public void EnterLift(Monster m)
        {
            if (m == null) return;
            m.InLift = true;
            m.LiftTarget = null;
            m.AttackingRoom = null;
            m.Biting = null;
            if (m.Sr != null) m.Sr.enabled = false;
        }

        /// <summary>Presses a floor button; the car arrives after a short ride.</summary>
        public bool RideLift(Monster m, RoomDef floor)
        {
            if (m == null || !m.InLift || m.Dead || floor == null || !HuntableFloors(m).Contains(floor)) return false;
            m.LiftTarget = floor;
            m.LiftArriveAt = Now + LiftRideSeconds;
            return true;
        }

        void UpdateLift(Monster m)
        {
            if (m.LiftTarget == null || Now < m.LiftArriveAt) return;
            var floor = m.LiftTarget;
            m.LiftTarget = null;
            m.InLift = false;
            m.Floor = floor;
            m.Pos = HotelMap.Center(floor.LiftLanding);
            m.Facing = ((Vector2)(floor.LiftLanding - floor.LiftTile)).normalized;
            var owner = RoomsByDef[floor].Owner;
            // The player's camera drops any old pan and follows the monster onto its new floor.
            if (m.IsHuman) { sleepCameraActive = false; recenterCamera = false; floorCameraSet = false; }
            if (owner != null && owner.IsHuman) Announce(m.Def.name + " stepped out of the lift on your floor!", 3f);
            else if (m.IsHuman) Toast("Floor " + floor.FloorNumber + ": " + (owner != null ? owner.Name : "empty") + "'s hallway.");
        }

        Sprite liftGateSprite, lanternSprite;
        Material lanternMaterial;
        readonly Dictionary<RoomDef, SpriteRenderer> liftGates = new Dictionary<RoomDef, SpriteRenderer>();
        /// <summary>Per-storey props (lanterns) shown only while their storey is the one being watched.</summary>
        readonly List<KeyValuePair<RoomDef, SpriteRenderer>> floorProps = new List<KeyValuePair<RoomDef, SpriteRenderer>>();
        public int LanternCount { get; private set; }
        const float LanternSpacing = 8f, LanternRadius = 7f, LanternHeight = 1.75f;
        /// <summary>Extra lamp brightness on the hallway floors, so pools read clearly against the dusk between them.</summary>
        [Range(0f, 1.5f)] public float LampBoost = .6f;

        /// <summary>
        /// Spirit lanterns hung over the carpet every few steps: the light comes in warm pools from its sources, with
        /// dusk between them, and the middle of a wide hallway is never pitch dark.
        /// </summary>
        void AddHallwayLanterns(RoomDef def)
        {
            var picked = new List<Vector2>();
            var torches = new List<Vector2>();
            foreach (var w in wallLamps) if (Map.FloorOfWorld(w.Position) == def) torches.Add(w.Position);
            if (lanternSprite == null)
            {
                var tex = Resources.Load<Texture2D>("Art/UI/Haunted/drop_glow");
                if (tex == null) return;
                lanternSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(.5f, .5f), 100f);
                lanternSprite.name = "Lantern glow";
                sceneAssets.Add(lanternSprite);
            }
            if (lanternMaterial == null)
            {
                // Additive, so the halo adds light instead of being clipped like the cut-out world sprites.
                lanternMaterial = new Material(Resources.Load<Shader>("Shaders/UiAdditive")) { name = "Lantern glow (additive)" };
                sceneAssets.Add(lanternMaterial);
            }
            foreach (var tile in def.Lane)
            {
                var p = HotelMap.Center(tile);
                bool clear = true;
                foreach (var q in picked) if ((q - p).sqrMagnitude < LanternSpacing * LanternSpacing) { clear = false; break; }
                // Leave the carpet right under a wall torch to that torch.
                foreach (var q in torches) if ((q - p).sqrMagnitude < 3.5f * 3.5f) { clear = false; break; }
                if (!clear) continue;
                picked.Add(p);
                wallLamps.Add(new HotelLighting.Lamp(p, Vector2.up, LanternRadius, .9f, torch: true, omni: true));
                LanternCount++;
                // The lantern itself: a warm halo and a hot core, hanging above the carpet.
                foreach (var (size, color) in new[] { (2.2f, new Color(1f, .6f, .25f, .28f)), (.55f, new Color(1f, .9f, .66f, .9f)) })
                {
                    var glow = MakeSprite("Lantern", lanternSprite, p, -2700, worldRoot);
                    var pos = (Vector3)p - HotelView3D.Forward * 0f + Vector3.back * LanternHeight;
                    glow.transform.SetPositionAndRotation(pos, HotelView3D.SpriteRotation);
                    glow.transform.localScale = Vector3.one * (size / Mathf.Max(.01f, lanternSprite.bounds.size.x));
                    glow.color = color;
                    glow.sharedMaterial = lanternMaterial;
                    glow.sortingOrder = 6000;
                    floorProps.Add(new KeyValuePair<RoomDef, SpriteRenderer>(def, glow));
                }
            }
        }

        /// <summary>The lift on each floor: a brass door frame in the wall behind the landing, holding a scissor gate.</summary>
        void CreateLiftDoors(RoomDef def)
        {
            var inward = (Vector2)(def.LiftLanding - def.LiftTile);
            var center = HotelMap.Center(def.LiftTile);
            var graph = Walls;
            bool horizontal = Mathf.Abs(inward.y) > .5f;
            var rect = horizontal ? new Rect(center.x - .6f, center.y - graph.Thickness / 2, 1.2f, graph.Thickness)
                : new Rect(center.x - graph.Thickness / 2, center.y - .6f, graph.Thickness, 1.2f);
            int state = AddWallState(rect, WallGraph.FullHeight);
            AddWallPiece("door_frame", rect, -inward, state, 3);
            if (liftGateSprite == null)
            {
                var tex = Resources.Load<Texture2D>("Art/UI/Haunted/gate_tile");
                if (tex == null) return;
                liftGateSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(.5f, .5f), 100f);
                liftGateSprite.name = "Lift gate";
                sceneAssets.Add(liftGateSprite);
            }
            var gate = MakeSprite("Lift " + def.FloorNumber, liftGateSprite, center, -2800, worldRoot);
            var face = center + inward * (graph.Thickness * .5f + .03f);
            gate.transform.SetPositionAndRotation(new Vector3(face.x, face.y, -.62f), Quaternion.LookRotation(new Vector3(inward.x, inward.y, 0f), Vector3.back));
            var bounds = liftGateSprite.bounds.size;
            gate.transform.localScale = new Vector3(.9f / bounds.x, 1.24f / bounds.y, 1f);
            gate.color = new Color(1f, .9f, .7f, 1f);
            gate.sortingOrder = 5000 - Mathf.RoundToInt((face.x + face.y) * 10);
            liftGates[def] = gate;
        }

        // ------------------------------------------------------------------ waves and Fear

        public float WaveSizeCost(Room room) =>
            room.WaveSize >= Cfg.waves.maxSize ? -1f : Mathf.Round(Cfg.waves.sizeCostBase * Mathf.Pow(Cfg.waves.sizeCostGrowth, room.WaveSize - Cfg.waves.startSize));

        public float WaveTypeCost(Room room, int type) =>
            type < 0 || type >= room.WaveTypes.Length || room.WaveTypes[type] ? -1f : Cfg.waves.typeCosts[Mathf.Min(type, Cfg.waves.typeCosts.Length - 1)];

        /// <summary>One more minion per pulse on this floor, for the rest of the match.</summary>
        public ActionResult TryGrowWave(Room room)
        {
            if (room == null || Monster == null) return ActionResult.Invalid;
            float cost = WaveSizeCost(room);
            if (cost < 0f) return ActionResult.MaxLevel;
            if (Fear + .001f < cost) return ActionResult.NoMoney;
            Fear -= cost;
            room.WaveSize++;
            AddLog("Floor " + room.Def.FloorNumber + " waves grow to " + room.WaveSize + ".");
            return ActionResult.Ok;
        }

        /// <summary>Adds a creature to this floor's waves, for the rest of the match.</summary>
        public ActionResult TryAddWaveType(Room room, int type)
        {
            if (room == null || Monster == null) return ActionResult.Invalid;
            float cost = WaveTypeCost(room, type);
            if (cost < 0f) return ActionResult.MaxLevel;
            if (Fear + .001f < cost) return ActionResult.NoMoney;
            Fear -= cost;
            room.WaveTypes[type] = true;
            AddLog("Floor " + room.Def.FloorNumber + " waves now bring " + Creature(Monster, type).name + ".");
            return ActionResult.Ok;
        }

        /// <summary>Minion multiplier from the monster's buff, while it stands on that minion's floor.</summary>
        public float FloorBuff(Minion n, int kind)
        {
            var m = Monster;
            if (!Floors || m == null || m.Dead || m.InLift || n.Rift == null || m.Floor != n.Rift.Room.Def || BuffOf(m.Def) != kind) return 1f;
            var w = Cfg.waves;
            return 1f + (kind == 0 ? w.buffDamage : kind == 1 ? w.buffSpeed : w.buffHealth);
        }

        /// <summary>Minion growth for tonight: compounding per night after the first.</summary>
        float NightGrowth(float perNight) => Mathf.Pow(1f + perNight, Mathf.Max(0, Night - 1));

        /// <summary>Pulse k of P on every hunted floor: each floor sends its share of its wave, cycling through its creatures.</summary>
        void ReleaseFloorPulse(Monster m, int k, int pulses)
        {
            foreach (var rift in Rifts.ToArray())
            {
                var room = rift.Room;
                int size = room.WaveSize;
                int count = size * (k + 1) / pulses - size * k / pulses;
                if (k == 0) count = Mathf.Max(count, 1);
                var types = Enumerable.Range(0, room.WaveTypes.Length).Where(i => room.WaveTypes[i]).ToArray();
                if (types.Length == 0) continue;
                for (int s = 0; s < count && Minions.Count < FloorMinionCeiling; s++)
                {
                    int type = types[(rift.ReleasedTonight + s) % types.Length];
                    var n = SpawnMinion(m, type, rift, rift.Pos);
                    var w = Cfg.waves;
                    n.Hp = n.MaxHp = n.MaxHp * NightGrowth(w.perNightHealth);
                    n.Damage *= NightGrowth(w.perNightDamage);
                    n.SpeedMult = Mathf.Min(1f + w.maxSpeedGrowth, NightGrowth(w.perNightSpeed));
                }
                rift.ReleasedTonight += count;
            }
        }

        int FloorMinionCeiling => Mathf.Max(Cfg.minions.nightCeiling, 30 * Mathf.Max(1, Rifts.Count));

        // ------------------------------------------------------------------ build pads

        /// <summary>How many carpet squares a tower of this kind would cover from a pad (bots and the build preview).</summary>
        public int LaneCoverage(RoomDef floor, Vector2Int pad, TowerDef def, int level = 1)
        {
            int n = 0;
            var p = HotelMap.Center(pad);
            foreach (var c in floor.Lane)
                if (Rules.UpgradeRules.InRange(Cfg.towers, def, level, Vector2.Distance(p, HotelMap.Center(c)))) n++;
            return n;
        }

        // ------------------------------------------------------------------ camera

        /// <summary>The floor the player is watching: their own, or the monster's while hunting.</summary>
        public RoomDef ViewFloor
        {
            get
            {
                if (!Floors || !InMatch) return null;
                if (Phase == Phase.CheckIn && (Human == null || Human.Room == null)) return null;
                if (HumanRole == Role.Monster && Monster != null && !Monster.InLift && Monster.Floor != null) return Monster.Floor;
                if (Human != null && Human.Room != null) return Human.Room.Def;
                return null;
            }
        }

        Vector2 floorCamera;
        bool floorCameraSet;

        /// <summary>
        /// Frames the floor being watched: the whole hallway for a guest (dragging pans, Recenter returns), the
        /// previewed floor at the front desk. A monster player hunting a floor uses the normal follow camera.
        /// </summary>
        bool FloorCamera(float aspect, float dt)
        {
            if (!Floors || !InMatch || HotelView) { floorCameraSet = false; return false; }
            if (HumanRole == Role.Monster && Monster != null && !Monster.InLift && !Monster.Dead) { floorCameraSet = false; return false; }
            var floor = ViewFloor;
            RectInt rect = floor != null ? floor.ViewBounds : new RectInt(0, 0, Map.W, Map.H);
            // A guest's view starts FloorViewZoom closer than the whole floor; pinching out shows all of it.
            float size = FloorViewSize(rect, aspect) * DefaultZoom / (Zoom * FloorViewZoom);
            var center = rect.center;
            if (floor != null && Human != null && Human.Room != null && floor == Human.Room.Def && !sleepCameraActive)
                center = Vector2.Lerp(rect.center, floor.Center, .3f);   // lean a little toward the guest's room
            if (sleepCameraActive)
            {
                if (recenterCamera)
                {
                    cameraPosition = Vector2.Lerp(cameraPosition, center, 1f - Mathf.Exp(-8f * dt));
                    if ((cameraPosition - center).sqrMagnitude < .01f) { sleepCameraActive = false; recenterCamera = false; }
                }
                else if (Time.unscaledTime > lastCameraInput + .04f)
                {
                    cameraPosition += cameraVelocity * dt;
                    cameraVelocity *= Mathf.Exp(-6f * dt);
                }
                cameraPosition = new Vector2(Mathf.Clamp(cameraPosition.x, rect.xMin, rect.xMax), Mathf.Clamp(cameraPosition.y, rect.yMin, rect.yMax));
                center = cameraPosition;
            }
            if (!floorCameraSet) { floorCamera = center; floorCameraSet = true; }
            floorCamera = Vector2.Lerp(floorCamera, center, 1f - Mathf.Exp(-7f * dt));
            Cam.orthographicSize = Mathf.Lerp(Cam.orthographicSize, size, 1f - Mathf.Exp(-7f * dt));
            var target = floorCamera + UiFocusShift(floorCamera, Cam.orthographicSize, dt);
            Cam.transform.position = new Vector3(target.x, target.y, 0) - HotelView3D.Forward * 100f;
            return true;
        }

        /// <summary>How much closer than "whole floor" a guest's view starts (pinch out to see all of it).</summary>
        public const float FloorViewZoom = 1.3f;

        RoomDef litFloor;
        bool litFloorSet;

        /// <summary>Lights the watched storey's squares (and its walls); every other storey stays dark.</summary>
        void FloorVision()
        {
            var floor = ViewFloor;
            if (litFloorSet && floor == litFloor) return;
            litFloor = floor; litFloorSet = true;
            for (int y = 0; y < Map.H; y++)
                for (int x = 0; x < Map.W; x++)
                {
                    int i = y * Map.W + x;
                    visible[i] = floor != null && Map.FloorOf(new Vector2Int(x, y)) == floor;
                    byte value = visible[i] ? (byte)255 : (byte)0;
                    fogPx[i] = new Color32(value, value, value, 255);
                }
            fogTex.SetPixels32(fogPx);
            fogTex.Apply(false);
            foreach (var gate in liftGates) if (gate.Value != null) gate.Value.enabled = gate.Key == floor;
            foreach (var prop in floorProps) if (prop.Value != null) prop.Value.enabled = prop.Key == floor;
        }

        /// <summary>Orthographic size that fits a whole floor on screen in the isometric view.</summary>
        public static float FloorViewSize(RectInt bounds, float aspect)
        {
            float span = (bounds.width + bounds.height) * 0.7071f;
            return Mathf.Max(span * 0.36f + 1f, span / (2f * aspect) + 1f);
        }
    }
}
