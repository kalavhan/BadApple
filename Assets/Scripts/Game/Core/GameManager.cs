using System.Collections.Generic;
using System.Linq;
using BadAppleHotel.Config;
using BadAppleHotel.Rules;
using UnityEngine;

namespace BadAppleHotel.Game
{
    public class MatchResult
    {
        public bool ResidentsWin;
        public int Kills;
        public int XpGained;
        public int LevelBefore;
        public int LevelAfter;
        public Role Role;
    }

    public class Floater
    {
        public Vector2 Pos;
        public string Text;
        public Color Color;
        public float Born;
    }

    class Projectile
    {
        public Transform T;
        public Vector2 From, To;
        public float Born, Duration;
    }

    /// <summary>
    /// Runs one local match: 1 Monster vs 6 Residents, any of them bots. All numbers come from the JSON configs.
    /// Split into partial files: this one (lifecycle, world, match flow), Residents, Monster, Towers and Vision.
    /// </summary>
    public partial class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public GameConfig Cfg { get; private set; }
        public string ConfigError { get; private set; }
        public HotelMap Map { get; private set; }
        public Phase Phase { get; private set; } = Phase.RoleSelect;
        public float PhaseTimer { get; private set; }
        public int Night { get; private set; }
        public Role HumanRole { get; private set; }
        public Resident Human { get; private set; }
        public Monster Monster { get; private set; }
        public Camera Cam { get; private set; }
        public MatchResult Result { get; private set; }
        public string Banner { get; private set; }
        public float BannerUntil { get; private set; }
        public float Speed { get; private set; } = 1f;
        public bool InMatch => Phase == Phase.Setup || Phase == Phase.Night;

        /// <summary>Zoomed-out whole-hotel camera (always for the monster and in setup; at night only with a crystal ball).</summary>
        public bool HotelView { get; set; }

        public readonly List<Resident> Residents = new List<Resident>();
        public readonly Dictionary<RoomDef, Room> RoomsByDef = new Dictionary<RoomDef, Room>();
        public readonly List<BodyPart> Parts = new List<BodyPart>();
        public readonly List<string> Log = new List<string>();
        public readonly List<Floater> Floaters = new List<Floater>();

        public Resident PendingHelpFrom { get; private set; }
        public float PendingHelpUntil { get; private set; }

        readonly List<Projectile> projectiles = new List<Projectile>();
        readonly Dictionary<RoomDef, SpriteRenderer> doorSprites = new Dictionary<RoomDef, SpriteRenderer>();
        Transform worldRoot;
        Transform matchRoot;
        float damageAccum;
        float nextDamageFloater;

        const float MaxStep = 1f / 30f;

        static readonly string[] BotNames ={ "Mortimer", "Edna", "Prudence", "Silas", "Agatha", "Ignatius", "Ophelia" };

        // ------------------------------------------------------------------ lifecycle

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            Application.targetFrameRate = 60;
            Initialize();
        }

        /// <summary>Loads configs and builds a hotel for the menu backdrop. Also used to recover after a script reload during Play mode.</summary>
        void Initialize()
        {
            ConfigError = null;
            Cfg = null;
            try
            {
                Cfg = ConfigLoader.Load();
            }
            catch (System.Exception e)
            {
                ConfigError = e.Message;
                Phase = Phase.ConfigError;
                Debug.LogError("[Bad Apple Hotel] Config failed: " + e.Message);
                return;
            }

            SetupCamera();
            if (!BuildWorld(Random.Range(1, 1 << 30))) return;
            Phase = Phase.RoleSelect;
        }

        /// <summary>Generates a fresh hotel and its tile visuals.</summary>
        bool BuildWorld(int seed)
        {
            if (worldRoot != null) Destroy(worldRoot.gameObject);
            doorSprites.Clear();
            try
            {
                Map = new HotelMap(Cfg.map, Cfg.match.roomCount, seed);
            }
            catch (System.Exception e)
            {
                ConfigError = e.Message;
                Phase = Phase.ConfigError;
                Debug.LogError("[Bad Apple Hotel] Map failed: " + e.Message);
                return false;
            }
            worldRoot = new GameObject("World").transform;
            BuildMapVisuals();
            CreateFog();
            return true;
        }

        void SetupCamera()
        {
            Cam = Camera.main;
            if (Cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                Cam = go.AddComponent<Camera>();
            }
            Cam.orthographic = true;
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = (Color)Palette.Ink;
            Cam.transform.rotation = Quaternion.identity;
        }

        void LateUpdate()
        {
            if (Cam == null || Map == null) return;
            float aspect = Mathf.Max(0.5f, Cam.aspect);

            Vector2? follow = null;
            if (InMatch && !HotelView)
            {
                if (HumanRole == Role.Monster && Monster != null) follow = Monster.Pos;
                else if (HumanRole == Role.Resident && Human != null && Human.Alive) follow = Human.Pos;
            }
            if (HotelView && !HotelViewAvailable) HotelView = false;

            if (follow.HasValue)
            {
                float size = 7.5f;
                Cam.orthographicSize = size;
                float hw = size * aspect;
                float x = Map.W <= hw * 2f - 2f ? Map.W / 2f : Mathf.Clamp(follow.Value.x, hw - 1f, Map.W - hw + 1f);
                float y = Map.H <= size * 2f - 2f ? Map.H / 2f : Mathf.Clamp(follow.Value.y, size - 1f, Map.H - size + 1f);
                Cam.transform.position = new Vector3(x, y, -10f);
            }
            else
            {
                float size = Mathf.Max(Map.H / 2f + 1.5f, (Map.W / 2f + 1f) / aspect);
                Cam.orthographicSize = size;
                Cam.transform.position = new Vector3(Map.W / 2f, Map.H / 2f - 0.5f, -10f);
            }
        }

        public bool HotelViewAvailable =>
            Phase == Phase.Setup || (Phase == Phase.Night && (HumanRole == Role.Monster || !FogActive));

        // ------------------------------------------------------------------ visuals helpers

        public static int OrderFor(float y) => 5000 - Mathf.RoundToInt(y * 10f);

        SpriteRenderer MakeSprite(string name, Sprite sprite, Vector2 pos, int order, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(pos.x, pos.y, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return sr;
        }

        void BuildMapVisuals()
        {
            var floorRoot = new GameObject("Tiles").transform;
            floorRoot.SetParent(worldRoot, false);
            var buildTiles = new HashSet<Vector2Int>();
            foreach (var def in Map.Rooms) foreach (var b in def.BuildTiles) buildTiles.Add(b);

            for (int x = 0; x < Map.W; x++)
                for (int y = 0; y < Map.H; y++)
                {
                    var v = new Vector2Int(x, y);
                    var pos = HotelMap.Center(v);
                    switch (Map.Tiles[x, y])
                    {
                        case Tile.Corridor: MakeSprite("c", Sprites.CorridorFloor, pos, -3000, floorRoot); break;
                        case Tile.RoomFloor:
                            MakeSprite(buildTiles.Contains(v) ? "b" : "f", buildTiles.Contains(v) ? Sprites.BuildTile : Sprites.RoomFloor, pos, -3000, floorRoot);
                            break;
                        case Tile.Wall: MakeSprite("w", Sprites.Wall, pos, -2900, floorRoot); break;
                        case Tile.Door: MakeSprite("f", Sprites.RoomFloor, pos, -3000, floorRoot); break;
                    }
                }
            foreach (var def in Map.Rooms)
                doorSprites[def] = MakeSprite("Door " + (def.Index + 1), Sprites.DoorOpen, HotelMap.Center(def.DoorTile), -2800, worldRoot);
        }

        // ------------------------------------------------------------------ match setup

        public void StartMatch(Role role, string monsterPick)
        {
            if (Cfg == null) return;
            ClearMatch();
            if (!BuildWorld(Random.Range(1, 1 << 30))) return;
            matchRoot = new GameObject("Match").transform;
            HumanRole = role;
            Result = null;
            HotelView = false;

            var defs = Cfg.monsters.monsters;
            var mdef = role == Role.Monster
                ? (defs.FirstOrDefault(d => d.id == monsterPick) ?? defs[0])
                : defs[Random.Range(0, defs.Length)];

            Monster = new Monster { Def = mdef, IsHuman = role == Role.Monster };
            Monster.Loadout = Cfg.abilities.starterLoadout
                .Select(id => Cfg.abilities.abilities.FirstOrDefault(a => a.id == id))
                .Where(a => a != null).ToArray();
            Monster.Cooldowns = new float[Monster.Loadout.Length];
            Monster.Pos = HotelMap.Center(Map.MonsterSpawn);
            Monster.Hp = MaxHp(Monster);
            Monster.Sr = MakeSprite("Monster", Sprites.Monster(mdef.id), Monster.Pos, OrderFor(Monster.Pos.y), matchRoot);
            if (!Monster.IsHuman) Monster.Ai = new MonsterAI(this, Monster);

            int residentLevelForHuman = AccountProgress.Level(Cfg, Role.Resident);
            for (int i = 0; i < Cfg.match.residentCount; i++)
            {
                bool human = role == Role.Resident && i == 0;
                var r = new Resident
                {
                    Id = i,
                    Name = human ? "You" : BotNames[i % BotNames.Length],
                    IsHuman = human,
                    ColorIndex = i,
                    Health = Cfg.match.residentHealth,
                    DreamPower = Cfg.economy.startingResources.dreamPower,
                    Faith = Cfg.economy.startingResources.faith,
                    XpValue = ResidentXpValue(human ? residentLevelForHuman : Random.Range(1, 6)),
                };
                r.Pos = LobbySpot(i);
                r.Sr = MakeSprite(r.Name, Sprites.Resident(i), r.Pos, OrderFor(r.Pos.y), matchRoot);
                if (!human)
                {
                    r.Ai = new ResidentAI(this, r);
                    r.ClaimAt = Random.Range(1.5f, Mathf.Max(3f, Cfg.match.setupSeconds * 0.45f));
                }
                Residents.Add(r);
            }
            Human = role == Role.Resident ? Residents[0] : null;

            Phase = Phase.Setup;
            PhaseTimer = Cfg.match.setupSeconds;
            Night = 0;
            Time.timeScale = Speed;
            Announce("Manager: a guest upstairs conjured a " + mdef.name + ", and it escaped! Walk into a free room and shut the door.", 7f);
            AddLog("The " + mdef.name + " is loose in the hotel.");
        }

        Vector2 LobbySpot(int i)
        {
            var lobby = HotelMap.Center(Map.Lobby);
            for (int k = 0; k < 12; k++)
            {
                float dx = (i - 2.5f) * 1.1f + (k % 2 == 0 ? k * 0.25f : -k * 0.25f);
                var p = lobby + new Vector2(dx, (i % 2 == 0 ? 0.4f : -0.4f));
                if (Map.Get(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y)) == Tile.Corridor) return p;
            }
            return lobby;
        }

        float ResidentXpValue(int level)
        {
            var v = Cfg.economy.residentXpValueByLevel;
            return v.@base * Mathf.Pow(v.growth, Mathf.Max(0, level - 1));
        }

        public void ReturnToMenu()
        {
            ClearMatch();
            Phase = Phase.RoleSelect;
            Time.timeScale = 1f;
        }

        void ClearMatch()
        {
            if (matchRoot != null) Destroy(matchRoot.gameObject);
            matchRoot = null;
            foreach (var p in projectiles) if (p.T != null) Destroy(p.T.gameObject);
            projectiles.Clear();
            Residents.Clear();
            RoomsByDef.Clear();
            Parts.Clear();
            Log.Clear();
            Floaters.Clear();
            Monster = null;
            Human = null;
            PendingHelpFrom = null;
            HotelView = false;
            foreach (var kv in doorSprites) if (kv.Value != null) kv.Value.sprite = Sprites.DoorOpen;
        }

        public bool IsRoomFree(RoomDef def) => !RoomsByDef.ContainsKey(def);

        /// <summary>Moves a resident into a free room. Walking in leaves the door open; a late auto-assign puts them in bed behind a closed door.</summary>
        public bool Claim(Resident r, RoomDef def, bool teleport)
        {
            if (!InMatch) return false;
            if (r == null || !r.Alive || r.Room != null || def == null || !IsRoomFree(def)) return false;

            var room = new Room(def)
            {
                Owner = r,
                DoorLevel = 1,
                DoorHp = Cfg.doors.levels[0].health,
                BedLevel = 1,
                DoorOpen = !teleport,
                DoorSr = doorSprites[def],
            };
            var bedPos = HotelMap.Center(def.BedTile);
            room.BedSr = MakeSprite("Bed", Sprites.Bed(1), bedPos, OrderFor(bedPos.y + 0.4f), matchRoot);
            RoomsByDef[def] = room;
            r.Room = room;
            RefreshDoor(room);
            if (teleport)
            {
                r.Pos = bedPos;
                r.Asleep = false;
            }
            AddLog(r.Name + " moved into Room " + (def.Index + 1) + ".");
            if (r.IsHuman)
                Toast(teleport ? "The manager dragged you into Room " + (def.Index + 1) + "." : "Room " + (def.Index + 1) + " is yours. Shut the door!");

            if (def.Isolated) GiveLonelyBonus(room);
            return true;
        }

        void GiveLonelyBonus(Room room)
        {
            int slot = room.EmptySlot();
            if (slot < 0) return;
            var options = Cfg.towers.towers;
            var def = options[Random.Range(0, options.Length)];
            PlaceTower(room, slot, def);
            string who = room.Owner.IsHuman ? "You get" : room.Owner.Name + " gets";
            AddLog("Lonely room bonus: " + who.ToLower() + " a free " + def.name.ToLower() + ".");
            if (room.Owner.IsHuman) Announce("Lonely room bonus: no neighbours, so the manager left you a free " + def.name + ".", 4f);
        }

        void ClaimRandom(Resident r, bool teleport)
        {
            var free = Map.Rooms.Where(IsRoomFree).ToList();
            if (free.Count == 0) return;
            Claim(r, free[Random.Range(0, free.Count)], teleport);
        }

        // ------------------------------------------------------------------ main loop

        void Update()
        {
            // A script reload while playing wipes non-serialized state (configs, residents) but keeps the phase.
            if (Instance == null) Instance = this;
            if (Cfg == null && Phase != Phase.ConfigError) { Recover(); return; }
            UpdateProjectiles();
            Floaters.RemoveAll(f => Time.unscaledTime - f.Born > 1.4f);
            UpdateVision();
            if (!InMatch) return;

            // fixed sub-steps keep movement and collisions stable at high game speed or low frame rates
            float total = Time.deltaTime;
            int steps = Mathf.Clamp(Mathf.CeilToInt(total / MaxStep), 1, 16);
            float dt = total / steps;
            HandleHumanMonsterKeys();
            for (int i = 0; i < steps && InMatch; i++)
            {
                float now = Time.time - total + dt * (i + 1);
                PhaseTimer -= dt;
                UpdateResidents(dt, now);
                UpdateEconomy(dt, now);
                UpdateMonster(dt, now);
                UpdateTowers(dt, now);
            }
            UpdateTowerBlink(Time.time);

            if (PendingHelpFrom != null && Time.time > PendingHelpUntil) PendingHelpFrom = null;

            if (Phase == Phase.Setup && PhaseTimer <= 0f) BeginNights();
            else if (Phase == Phase.Night)
            {
                if (Residents.All(r => !r.Alive)) EndMatch();
                else if (PhaseTimer <= 0f) EndNight();
            }
        }

        void Recover()
        {
            Debug.LogWarning("[Bad Apple Hotel] Scripts reloaded mid-match; the match state was lost. Back to the menu.");
            foreach (var n in new[] { "World", "Match", "Fog" })
            {
                var go = GameObject.Find(n);
                if (go != null) Destroy(go);
            }
            doorSprites.Clear();
            projectiles.Clear();
            Residents.Clear();
            RoomsByDef.Clear();
            Parts.Clear();
            Floaters.Clear();
            Monster = null;
            Human = null;
            PendingHelpFrom = null;
            Time.timeScale = 1f;
            Phase = Phase.RoleSelect;
            Initialize();
        }

        void BeginNights()
        {
            foreach (var r in Residents) if (r.Room == null) ClaimRandom(r, true);
            Night = 1;
            Phase = Phase.Night;
            PhaseTimer = Cfg.match.nightSeconds;
            HotelView = false;
            SpawnParts();
            if (Human != null && Human.Room != null && Human.Room.DoorOpen)
                Announce("Night 1 of " + Cfg.match.nightCount + ". Lights out... and YOUR DOOR IS OPEN!", 4f);
            else
                Announce("Night 1 of " + Cfg.match.nightCount + ". Lights out.", 3f);
        }

        void EndNight()
        {
            foreach (var r in Residents) if (r.Alive) r.NightsSurvived++;
            if (Night >= Cfg.match.nightCount) { EndMatch(); return; }
            Night++;
            PhaseTimer = Cfg.match.nightSeconds + Cfg.match.nightBreakSeconds;
            SpawnParts();
            Announce("Night " + Night + " of " + Cfg.match.nightCount + ".", 3f);
        }

        void EndMatch()
        {
            Phase = Phase.Results;
            HotelView = false;
            var res = new MatchResult
            {
                ResidentsWin = Residents.Any(r => r.Alive),
                Kills = Monster != null ? Monster.Kills : 0,
                Role = HumanRole,
            };
            res.LevelBefore = AccountProgress.Level(Cfg, HumanRole);
            if (HumanRole == Role.Resident && Human != null)
                res.XpGained = EconomyRules.ResidentAccountXp(Cfg.economy, Human.NightsSurvived, Cfg.match.nightCount);
            else
                res.XpGained = EconomyRules.MonsterAccountXp(Cfg.economy, res.Kills);
            AccountProgress.AddXp(HumanRole, res.XpGained);
            res.LevelAfter = AccountProgress.Level(Cfg, HumanRole);
            Result = res;
            Announce(res.ResidentsWin ? "Dawn breaks. The residents survived!" : "Silence. The monster ate everyone.", 6f);
        }

        void SpawnParts()
        {
            foreach (var p in Parts) if (p.Sr != null) Destroy(p.Sr.gameObject);
            Parts.Clear();
            var tiles = Map.CorridorTiles();
            var doorFronts = new HashSet<Vector2Int>(Map.Rooms.Select(r => r.DoorOutside));
            int want = Cfg.match.bodyPartsPerNight;
            int guard = 0;
            while (Parts.Count < want && guard++ < 500)
            {
                var t = tiles[Random.Range(0, tiles.Count)];
                if (doorFronts.Contains(t)) continue;
                if ((t - Map.MonsterSpawn).sqrMagnitude < 16) continue;
                if (Parts.Any(p => p.Tile == t)) continue;
                int typeIndex = Random.Range(0, Cfg.bodyParts.parts.Length);
                var def = Cfg.bodyParts.parts[typeIndex];
                var pos = HotelMap.Center(t);
                var part = new BodyPart
                {
                    Def = def,
                    TypeIndex = typeIndex,
                    Tile = t,
                    Sr = MakeSprite("Part " + def.id, Sprites.Part(def.id), pos, OrderFor(pos.y) - 5, matchRoot),
                };
                Parts.Add(part);
            }
        }

        // ------------------------------------------------------------------ messages

        /// <summary>Short messages for the human player, shown by the HUD.</summary>
        public string ToastText { get; private set; }
        public float ToastUntil { get; private set; }

        public void Toast(string text)
        {
            ToastText = text;
            ToastUntil = Time.unscaledTime + 2.5f;
        }

        public void SetSpeed(float s)
        {
            Speed = s;
            if (InMatch) Time.timeScale = s;
        }

        public void Announce(string text, float seconds)
        {
            Banner = text;
            BannerUntil = Time.unscaledTime + seconds;
        }

        public void AddLog(string text)
        {
            Log.Add(text);
            if (Log.Count > 5) Log.RemoveAt(0);
        }

        public void AddFloater(Vector2 pos, string text, Color color)
        {
            Floaters.Add(new Floater { Pos = pos, Text = text, Color = color, Born = Time.unscaledTime });
        }
    }
}
