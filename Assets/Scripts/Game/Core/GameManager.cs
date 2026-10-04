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
    /// </summary>
    public class GameManager : MonoBehaviour
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

        static readonly string[] BotNames = { "Mortimer", "Edna", "Prudence", "Silas", "Agatha", "Ignatius", "Ophelia" };

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

        /// <summary>Loads configs and builds the hotel. Also used to recover after a script reload during Play mode.</summary>
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
            }

            SetupCamera();
            Map = new HotelMap();
            worldRoot = new GameObject("World").transform;
            BuildMapVisuals();
            if (Cfg != null) Phase = Phase.RoleSelect;
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
            Cam.transform.position = new Vector3(HotelMap.W / 2f, HotelMap.H / 2f, -10f);
            Cam.transform.rotation = Quaternion.identity;
        }

        void LateUpdate()
        {
            if (Cam == null) return;
            float aspect = Mathf.Max(0.5f, Cam.aspect);
            bool follow = Monster != null && HumanRole == Role.Monster && (Phase == Phase.Setup || Phase == Phase.Night);
            if (follow)
            {
                float size = 8f;
                Cam.orthographicSize = size;
                float hw = size * aspect;
                float x = Mathf.Clamp(Monster.Pos.x, hw - 1f, HotelMap.W - hw + 1f);
                float y = Mathf.Clamp(Monster.Pos.y, size - 1f, HotelMap.H - size + 1f);
                Cam.transform.position = new Vector3(x, y, -10f);
            }
            else
            {
                // fit the whole hotel in the left ~76% of the screen (the build panel sits on the right)
                bool panel = HumanRole == Role.Resident && (Phase == Phase.Setup || Phase == Phase.Night);
                float usable = panel ? 0.76f : 1f;
                float size = Mathf.Max(HotelMap.H / 2f + 1.5f, (HotelMap.W / 2f + 1f) / (aspect * usable));
                Cam.orthographicSize = size;
                float visibleW = 2f * size * aspect;
                float x = HotelMap.W / 2f + (panel ? visibleW * (1f - usable) / 2f : 0f);
                Cam.transform.position = new Vector3(x, HotelMap.H / 2f - 0.5f, -10f);
            }
        }

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
            for (int x = 0; x < HotelMap.W; x++)
                for (int y = 0; y < HotelMap.H; y++)
                {
                    var t = Map.Tiles[x, y];
                    var pos = HotelMap.Center(new Vector2Int(x, y));
                    switch (t)
                    {
                        case Tile.Corridor: MakeSprite("c", Sprites.CorridorFloor, pos, -3000, floorRoot); break;
                        case Tile.RoomFloor: MakeSprite("f", Sprites.RoomFloor, pos, -3000, floorRoot); break;
                        case Tile.Wall: MakeSprite("w", Sprites.Wall, pos, -2900, floorRoot); break;
                        case Tile.Door: MakeSprite("f", Sprites.RoomFloor, pos, -3000, floorRoot); break;
                    }
                }
            foreach (var def in Map.Rooms)
                doorSprites[def] = MakeSprite("Door " + (def.Index + 1), Sprites.Door(1), HotelMap.Center(def.DoorTile), -2800, worldRoot);
        }

        // ------------------------------------------------------------------ match setup

        public void StartMatch(Role role, string monsterPick)
        {
            if (Cfg == null) return;
            ClearMatch();
            matchRoot = new GameObject("Match").transform;
            HumanRole = role;
            Result = null;

            var defs = Cfg.monsters.monsters;
            var mdef = role == Role.Monster
                ? (defs.FirstOrDefault(d => d.id == monsterPick) ?? defs[0])
                : defs[UnityEngine.Random.Range(0, defs.Length)];

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
                    XpValue = ResidentXpValue(human ? residentLevelForHuman : UnityEngine.Random.Range(1, 6)),
                };
                var lobby = HotelMap.Center(new Vector2Int(22 + i * 2, 15));
                r.Sr = MakeSprite(r.Name, Sprites.Resident(i), lobby, OrderFor(lobby.y), matchRoot);
                if (!human)
                {
                    r.Ai = new ResidentAI(this, r);
                    r.ClaimAt = UnityEngine.Random.Range(3f, Mathf.Max(4f, Cfg.match.setupSeconds * 0.6f));
                }
                Residents.Add(r);
            }
            Human = role == Role.Resident ? Residents[0] : null;

            Phase = Phase.Setup;
            PhaseTimer = Cfg.match.setupSeconds;
            Night = 0;
            Time.timeScale = Speed;
            Announce("Manager: a guest upstairs conjured a " + mdef.name + ", and it escaped! Pick a room and lock the door.", 7f);
            AddLog("The " + mdef.name + " is loose in the hotel.");
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
            foreach (var kv in doorSprites) kv.Value.sprite = Sprites.Door(1);
        }

        public bool IsRoomFree(RoomDef def) => !RoomsByDef.ContainsKey(def);

        public bool Claim(Resident r, RoomDef def)
        {
            if (Phase != Phase.Setup && Phase != Phase.Night) return false;
            if (r == null || r.Room != null || def == null || !IsRoomFree(def)) return false;

            var room = new Room
            {
                Def = def,
                Owner = r,
                DoorLevel = 1,
                DoorHp = Cfg.doors.levels[0].health,
                BedLevel = 1,
                DoorSr = doorSprites[def],
            };
            room.DoorSr.sprite = Sprites.Door(1);
            var bedPos = HotelMap.Center(def.BedTile);
            room.BedSr = MakeSprite("Bed", Sprites.Bed(1), bedPos, OrderFor(bedPos.y), matchRoot);
            foreach (var s in def.Slots) MakeSprite("Slot", Sprites.Slot, HotelMap.Center(s), -2700, matchRoot);
            RoomsByDef[def] = room;
            r.Room = room;
            var restPos = bedPos + new Vector2(0f, -0.25f);
            r.Sr.transform.position = new Vector3(restPos.x, restPos.y, 0f);
            r.Sr.sortingOrder = OrderFor(restPos.y);
            AddLog(r.Name + " locked into Room " + (def.Index + 1) + ".");
            return true;
        }

        void ClaimRandom(Resident r)
        {
            var free = Map.Rooms.Where(IsRoomFree).ToList();
            if (free.Count == 0) return;
            Claim(r, free[UnityEngine.Random.Range(0, free.Count)]);
        }

        // ------------------------------------------------------------------ main loop

        void Update()
        {
            // A script reload while playing wipes non-serialized state (configs, residents) but keeps the phase.
            if (Cfg == null && Phase != Phase.ConfigError) { Recover(); return; }
            UpdateProjectiles();
            Floaters.RemoveAll(f => Time.unscaledTime - f.Born > 1.4f);
            if (Phase != Phase.Setup && Phase != Phase.Night) return;

            float dt = Time.deltaTime;
            float now = Time.time;
            PhaseTimer -= dt;

            if (Phase == Phase.Setup)
            {
                float elapsed = Cfg.match.setupSeconds - PhaseTimer;
                foreach (var r in Residents)
                    if (!r.IsHuman && r.Room == null && elapsed >= r.ClaimAt) ClaimRandom(r);
            }

            UpdateEconomy(dt, now);
            HandleHumanMonsterKeys();
            UpdateMonster(dt, now);
            UpdateTowers(dt, now);
            foreach (var r in Residents)
                if (r.Ai != null && r.Alive && r.Room != null) r.Ai.Tick(dt, now);
            UpdateTowerBlink(now);

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
            foreach (var n in new[] { "World", "Match" })
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
            Initialize();
        }

        void BeginNights()
        {
            foreach (var r in Residents) if (r.Room == null) ClaimRandom(r);
            Night = 1;
            Phase = Phase.Night;
            PhaseTimer = Cfg.match.nightSeconds;
            SpawnParts();
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
                var t = tiles[UnityEngine.Random.Range(0, tiles.Count)];
                if (doorFronts.Contains(t)) continue;
                if ((t - Map.MonsterSpawn).sqrMagnitude < 16) continue;
                if (Parts.Any(p => p.Tile == t)) continue;
                int typeIndex = UnityEngine.Random.Range(0, Cfg.bodyParts.parts.Length);
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

        // ------------------------------------------------------------------ economy

        void UpdateEconomy(float dt, float now)
        {
            foreach (var r in Residents)
            {
                if (!r.Alive || r.Room == null) continue;
                float bed = Cfg.beds.levels[r.Room.BedLevel - 1].dreamPowerPerSecond;
                if (now < r.BedSlowUntil) bed *= r.BedSlowValue;
                r.DreamPower += bed * dt;
                if (now < r.FaithBlockedUntil) continue;
                r.Faith += FaithPerSecond(r.Room) * dt;
            }
        }

        public float FaithPerSecond(Room room)
        {
            float f = 0f;
            foreach (var t in room.Slots)
                if (t != null && t.Def.id == "faith_tower")
                    f += t.Def.faithPerSecond * Mathf.Pow(t.Def.faithLevelScaling, t.Level - 1);
            return f;
        }

        public float DreamPerSecond(Room room) => Cfg.beds.levels[room.BedLevel - 1].dreamPowerPerSecond;

        // ------------------------------------------------------------------ monster stats

        int PartIndex(string id)
        {
            var parts = Cfg.bodyParts.parts;
            for (int i = 0; i < parts.Length; i++) if (parts[i].id == id) return i;
            return -1;
        }

        public int PartCount(Monster m, string id)
        {
            int i = PartIndex(id);
            return i >= 0 && i < m.Parts.Length ? m.Parts[i] : 0;
        }

        float PartValue(string id)
        {
            int i = PartIndex(id);
            return i >= 0 ? Cfg.bodyParts.parts[i].perPart : 0f;
        }

        public float MaxHp(Monster m) => m.Def.baseHealth + PartCount(m, "torso") * PartValue("torso");
        public float AttackMult(Monster m) => 1f + PartCount(m, "arm") * PartValue("arm");
        public float RevealRadius(Monster m) => PartCount(m, "eye") * PartValue("eye");

        public float MonsterSpeed(Monster m, float now)
        {
            if (now < m.StunUntil) return 0f;
            float s = m.Def.moveSpeed * (1f + PartCount(m, "leg") * PartValue("leg"));
            if (now < m.SlowUntil) s *= 1f - m.SlowPct;
            return s;
        }

        public float DamageTaken(Monster m, int type)
        {
            if (type < 0) return 1f;
            var b = m.Def.damageTakenMultiplier;
            float baseMult;
            switch (type)
            {
                case DamageTypes.Bullet: baseMult = b.bullet; break;
                case DamageTypes.Electric: baseMult = b.electric; break;
                case DamageTypes.Fire: baseMult = b.fire; break;
                default: baseMult = b.slow; break;
            }
            var track = Cfg.monsters.resistanceTracks.damageTakenMultiplierByLevel;
            int lv = Mathf.Clamp(m.ResistLevels[type], 0, track.Length - 1);
            return baseMult * track[lv];
        }

        // ------------------------------------------------------------------ monster movement & combat

        public bool Walkable(int x, int y)
        {
            var t = Map.Get(x, y);
            if (t == Tile.Corridor || t == Tile.RoomFloor) return true;
            if (t == Tile.Door)
            {
                var def = Map.RoomAtDoor(new Vector2Int(x, y));
                return def != null && RoomsByDef.TryGetValue(def, out var room) && room.DoorBroken;
            }
            return false;
        }

        bool CanStand(Vector2 p)
        {
            const float r = 0.3f;
            return Walkable(Mathf.FloorToInt(p.x - r), Mathf.FloorToInt(p.y - r))
                && Walkable(Mathf.FloorToInt(p.x + r), Mathf.FloorToInt(p.y - r))
                && Walkable(Mathf.FloorToInt(p.x - r), Mathf.FloorToInt(p.y + r))
                && Walkable(Mathf.FloorToInt(p.x + r), Mathf.FloorToInt(p.y + r));
        }

        void MoveMonster(Monster m, Vector2 delta)
        {
            var p = m.Pos;
            var nx = new Vector2(p.x + delta.x, p.y);
            if (CanStand(nx)) p = nx;
            var ny = new Vector2(p.x, p.y + delta.y);
            if (CanStand(ny)) p = ny;
            m.Pos = p;
        }

        void Unstick(Monster m)
        {
            var c = HotelMap.ToTile(m.Pos);
            for (int radius = 1; radius <= 4; radius++)
                for (int dx = -radius; dx <= radius; dx++)
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        var p = HotelMap.Center(new Vector2Int(c.x + dx, c.y + dy));
                        if (CanStand(p)) { m.Pos = p; return; }
                    }
            m.Pos = HotelMap.Center(Map.MonsterSpawn);
        }

        void HandleHumanMonsterKeys()
        {
            if (HumanRole != Role.Monster || Monster == null) return;
            if (GameInput.ConsumePressed(KeyCode.Alpha1)) UseAbility(0);
            if (GameInput.ConsumePressed(KeyCode.Alpha2)) UseAbility(1);
            if (GameInput.ConsumePressed(KeyCode.Alpha3)) UseAbility(2);
        }

        void UpdateMonster(float dt, float now)
        {
            var m = Monster;
            if (m == null) return;
            for (int i = 0; i < m.Cooldowns.Length; i++) m.Cooldowns[i] = Mathf.Max(0f, m.Cooldowns[i] - dt);

            if (m.Dead)
            {
                if (now >= m.RespawnAt) Respawn(m);
                return;
            }

            if (now < m.BurnUntil) DamageMonster(m.BurnDps * dt, m.BurnSource);
            if (m.Dead) return;

            Vector2 move = m.IsHuman ? GameInput.Move : (m.Ai != null ? m.Ai.Tick(dt, now) : Vector2.zero);
            if (now < m.DashUntil)
            {
                MoveMonster(m, m.DashVelocity * dt);
            }
            else if (move.sqrMagnitude > 0.0001f)
            {
                move = Vector2.ClampMagnitude(move, 1f);
                m.Facing = move.normalized;
                MoveMonster(m, move * MonsterSpeed(m, now) * dt);
            }
            if (!CanStand(m.Pos)) Unstick(m);

            m.AttackingRoom = null;
            if (Phase == Phase.Night) MonsterAttack(m, dt, now);
            UpdateEating(m, dt);

            // visuals
            m.Sr.transform.position = new Vector3(m.Pos.x, m.Pos.y - 0.3f, 0f);
            m.Sr.sortingOrder = OrderFor(m.Pos.y - 0.3f);
            m.Sr.flipX = m.Facing.x < 0f;
            Color tint = Color.white;
            if (now < m.StunUntil) tint = (Color)Palette.Mint;
            else if (now < m.SlowUntil) tint = new Color(0.75f, 0.9f, 1f);
            else if (now < m.BurnUntil) tint = new Color(1f, 0.8f, 0.55f);
            if (now < m.CloakUntil) tint.a = 0.35f;
            m.Sr.color = tint;
        }

        void MonsterAttack(Monster m, float dt, float now)
        {
            var tile = HotelMap.ToTile(m.Pos);
            var inside = Map.RoomContaining(tile);
            if (inside != null && RoomsByDef.TryGetValue(inside, out var invaded) && invaded.Owner != null && invaded.Owner.Alive)
            {
                if (Vector2.Distance(m.Pos, HotelMap.Center(inside.BedTile)) <= 1.4f)
                {
                    m.AttackingRoom = invaded;
                    invaded.LastAttackedTime = now;
                    invaded.Owner.Health -= m.Def.residentDamagePerSecond * AttackMult(m) * dt;
                    if (invaded.Owner.Health <= 0f) KillResident(invaded.Owner);
                    return;
                }
            }

            Room best = null;
            float bestD = 1.35f;
            foreach (var r in RoomsByDef.Values)
            {
                if (r.DoorBroken || r.Owner == null || !r.Owner.Alive) continue;
                float d = Vector2.Distance(m.Pos, HotelMap.Center(r.Def.DoorTile));
                if (d < bestD) { bestD = d; best = r; }
            }
            if (best == null) return;

            float resist = Cfg.doors.levels[best.DoorLevel - 1].damageResistancePct;
            float rampage = now < m.RampageUntil ? m.RampageValue : 1f;
            float dmg = m.Def.doorDamagePerSecond * AttackMult(m) * rampage * (1f - resist) * dt;
            best.DoorHp -= dmg;
            best.LastAttackedTime = now;
            m.AttackingRoom = best;
            m.DreamPower += dmg * Cfg.economy.monsterIncome.dreamPowerPerDoorDamage;
            if (best.DoorHp <= 0f)
            {
                best.DoorHp = 0f;
                best.DoorBroken = true;
                best.DoorSr.sprite = Sprites.DoorBroken;
                Announce(best.Owner.IsHuman ? "Your door is broken! Rebuild it or hold on!" : best.Owner.Name + "'s door is broken!", 3f);
            }
        }

        void UpdateEating(Monster m, float dt)
        {
            BodyPart near = null;
            foreach (var p in Parts)
                if (Vector2.Distance(m.Pos, HotelMap.Center(p.Tile)) < 0.8f) { near = p; break; }

            if (near == null || m.AttackingRoom != null || m.Parts[near.TypeIndex] >= Cfg.bodyParts.maxPartsPerType)
            {
                m.EatingPart = null;
                m.EatProgress = 0f;
                return;
            }
            if (m.EatingPart != near) { m.EatingPart = near; m.EatProgress = 0f; }
            m.EatProgress += dt;
            if (m.EatProgress < Cfg.bodyParts.eatSeconds) return;

            float oldMax = MaxHp(m);
            m.Parts[near.TypeIndex]++;
            m.Hp += MaxHp(m) - oldMax;
            m.Faith += Cfg.economy.monsterIncome.faithPerBodyPart;
            Parts.Remove(near);
            Destroy(near.Sr.gameObject);
            m.EatingPart = null;
            m.EatProgress = 0f;
            AddFloater(m.Pos + Vector2.up * 1.5f, "+" + near.Def.name, (Color)Palette.Moss);
            AddLog("The " + m.Def.name + " ate a " + near.Def.name.ToLower() + ". Gross.");
        }

        void Respawn(Monster m)
        {
            m.Dead = false;
            m.Pos = HotelMap.Center(Map.MonsterSpawn);
            m.Hp = MaxHp(m);
            m.Sr.enabled = true;
            Announce("The " + m.Def.name + " crawls back out of the dark...", 3f);
        }

        public void DamageMonster(float amount, Resident source)
        {
            var m = Monster;
            if (m == null || m.Dead || amount <= 0f) return;
            m.Hp -= amount;
            if (source != null) m.LastDamager = source;
            damageAccum += amount;
            if (Time.unscaledTime >= nextDamageFloater)
            {
                AddFloater(m.Pos + Vector2.up * 1.8f, "-" + Mathf.RoundToInt(damageAccum), (Color)Palette.Candle);
                damageAccum = 0f;
                nextDamageFloater = Time.unscaledTime + 0.35f;
            }
            if (m.Hp <= 0f) MonsterDies(source ?? m.LastDamager);
        }

        void MonsterDies(Resident killer)
        {
            var m = Monster;
            m.Dead = true;
            m.Hp = 0f;
            m.RespawnAt = Time.time + Cfg.match.monsterRespawnSeconds;
            m.Sr.enabled = false;
            if (killer != null && killer.Alive)
            {
                var reward = EconomyRules.MonsterDeathReward(Cfg.economy, m.DreamPower, m.Faith);
                killer.DreamPower += reward.DreamPower;
                killer.Faith += reward.Faith;
                Announce((killer.IsHuman ? "You" : killer.Name) + " banished the " + m.Def.name + "! +" +
                         Mathf.RoundToInt(reward.DreamPower) + " Dream Power", 4f);
            }
            else
            {
                Announce("The " + m.Def.name + " was banished!", 3f);
            }
            m.DreamPower = 0f;
            m.Faith = 0f;
            for (int i = 0; i < m.Parts.Length; i++)
                m.Parts[i] = Mathf.FloorToInt(m.Parts[i] * Cfg.bodyParts.keepOnRespawnPct);
            m.SlowUntil = m.StunUntil = m.BurnUntil = m.JamUntil = m.RampageUntil = m.CloakUntil = m.DashUntil = 0f;
            m.EatingPart = null;
            m.EatProgress = 0f;
        }

        void KillResident(Resident r)
        {
            var m = Monster;
            r.Alive = false;
            r.Health = 0f;
            var reward = EconomyRules.MonsterKillReward(Cfg.economy, r.DreamPower, r.Faith, r.XpValue);
            m.DreamPower += reward.DreamPower + Cfg.economy.monsterIncome.dreamPowerPerResidentKill;
            m.Faith += reward.Faith;
            m.MatchXp += reward.Xp;
            m.Kills++;
            r.DreamPower = 0f;
            r.Faith = 0f;
            r.Sr.sprite = Sprites.Ghost;
            Announce(r.IsHuman ? "You were eaten. Now you haunt the hallway (spectating)." : r.Name + " was eaten by the " + m.Def.name + "!", 4f);
            AddLog(r.Name + " was eaten.");
        }

        // ------------------------------------------------------------------ towers

        void UpdateTowers(float dt, float now)
        {
            var m = Monster;
            if (m == null || m.Dead || Phase != Phase.Night) return;
            bool cloaked = now < m.CloakUntil;
            var insideDef = Map.RoomContaining(HotelMap.ToTile(m.Pos));
            var sc = Cfg.towers.levelScaling;

            foreach (var room in RoomsByDef.Values)
            {
                if (room.Owner == null || !room.Owner.Alive) continue;
                bool engaged = m.AttackingRoom == room || insideDef == room.Def;
                foreach (var t in room.Slots)
                {
                    if (t == null || !t.IsWeapon) continue;
                    t.Cooldown = Mathf.Max(0f, t.Cooldown - dt);
                    if (!engaged || cloaked || t.Cooldown > 0f) continue;
                    var tpos = HotelMap.Center(room.Def.Slots[t.SlotIndex]);
                    float range = t.Def.range * Mathf.Pow(sc.range, t.Level - 1);
                    if (Vector2.Distance(tpos, m.Pos) > range) continue;
                    Fire(t, room, tpos, m, now);
                    if (m.Dead) return;
                }
            }
        }

        void Fire(TowerInstance t, Room room, Vector2 tpos, Monster m, float now)
        {
            var sc = Cfg.towers.levelScaling;
            int lv = t.Level - 1;
            float rate = t.Def.shotsPerSecond * Mathf.Pow(sc.fireRate, lv);
            t.Cooldown = rate > 0f ? 1f / rate : 1f;
            int type = DamageTypes.Index(t.Def.damageType);
            float mult = DamageTaken(m, type);
            SpawnProjectile(tpos, m.Pos + Vector2.up * 0.5f, t.Def.damageType);

            if (type == DamageTypes.Slow)
            {
                m.SlowPct = Mathf.Clamp(t.Def.slowPct * (1f + 0.05f * lv) * mult, 0f, 0.75f);
                m.SlowUntil = now + t.Def.slowSeconds;
                return;
            }

            float dmg = t.Def.damage * Mathf.Pow(sc.damage, lv) * mult;
            if (type == DamageTypes.Bullet && now < m.JamUntil && Vector2.Distance(tpos, m.Pos) <= m.JamRadius)
                dmg *= m.JamValue;
            if (type == DamageTypes.Electric && t.Def.stunSeconds > 0f)
                m.StunUntil = Mathf.Max(m.StunUntil, now + t.Def.stunSeconds);
            if (type == DamageTypes.Fire && t.Def.burnSeconds > 0f)
            {
                m.BurnDps = t.Def.burnDamagePerSecond * Mathf.Pow(sc.damage, lv) * mult;
                m.BurnUntil = now + t.Def.burnSeconds;
                m.BurnSource = room.Owner;
            }
            DamageMonster(dmg, room.Owner);
        }

        void UpdateTowerBlink(float now)
        {
            float wall = Time.unscaledTime;
            foreach (var room in RoomsByDef.Values)
                foreach (var t in room.Slots)
                {
                    if (t == null || t.Sr == null) continue;
                    bool blink = wall < t.BlinkUntil && Mathf.FloorToInt(wall * 8f) % 2 == 0;
                    t.Sr.color = blink ? (Color)Palette.AppleRed : Color.white;
                }
        }

        void SpawnProjectile(Vector2 from, Vector2 to, string type)
        {
            if (matchRoot == null) return;
            var sr = MakeSprite("shot", Sprites.Projectile(type), from, 6000, matchRoot);
            projectiles.Add(new Projectile { T = sr.transform, From = from, To = to, Born = Time.time, Duration = 0.15f });
        }

        void UpdateProjectiles()
        {
            for (int i = projectiles.Count - 1; i >= 0; i--)
            {
                var p = projectiles[i];
                if (p.T == null) { projectiles.RemoveAt(i); continue; }
                float k = (Time.time - p.Born) / p.Duration;
                if (k >= 1f) { Destroy(p.T.gameObject); projectiles.RemoveAt(i); continue; }
                var pos = Vector2.Lerp(p.From, p.To, k);
                p.T.position = new Vector3(pos.x, pos.y, 0f);
            }
        }

        // ------------------------------------------------------------------ monster actions

        public bool UseAbility(int i)
        {
            var m = Monster;
            if (m == null || m.Dead || Phase != Phase.Night) return false;
            if (i < 0 || i >= m.Loadout.Length || m.Cooldowns[i] > 0f) return false;
            var a = m.Loadout[i];
            float now = Time.time;
            switch (a.effect)
            {
                case "towerDamageMultiplier":
                    m.JamUntil = now + a.durationSeconds; m.JamValue = a.value; m.JamRadius = a.radius;
                    break;
                case "doorDamageMultiplier":
                    m.RampageUntil = now + a.durationSeconds; m.RampageValue = a.value;
                    break;
                case "faithIncomeMultiplier":
                    foreach (var r in Residents)
                        if (r.Alive && r.Room != null && Vector2.Distance(m.Pos, HotelMap.Center(r.Room.Def.DoorTile)) <= a.radius)
                            r.FaithBlockedUntil = now + a.durationSeconds;
                    break;
                case "bedIncomeMultiplier":
                    foreach (var r in Residents)
                        if (r.Alive && r.Room != null && Vector2.Distance(m.Pos, HotelMap.Center(r.Room.Def.DoorTile)) <= a.radius)
                        { r.BedSlowUntil = now + a.durationSeconds; r.BedSlowValue = a.value; }
                    break;
                case "dash":
                    m.DashVelocity = m.Facing.normalized * (a.value / 0.3f);
                    m.DashUntil = now + 0.3f;
                    break;
                case "towerUntargetable":
                    m.CloakUntil = now + a.durationSeconds;
                    break;
                default:
                    AddLog(a.name + " is not in the demo yet.");
                    return false;
            }
            m.Cooldowns[i] = a.cooldownSeconds;
            AddFloater(m.Pos + Vector2.up * 2.2f, a.name + "!", (Color)Palette.Mint);
            if (!m.IsHuman) AddLog("The monster used " + a.name + ".");
            return true;
        }

        public float ResistCost(Monster m, int type)
        {
            var tr = Cfg.monsters.resistanceTracks;
            int lv = m.ResistLevels[type];
            if (lv >= tr.maxLevel || lv + 1 >= tr.upgradeCostByLevel.Length) return -1f;
            return tr.upgradeCostByLevel[lv + 1];
        }

        public ActionResult TryUpgradeResist(Monster m, int type)
        {
            if (m == null || m.Dead) return ActionResult.Invalid;
            float cost = ResistCost(m, type);
            if (cost < 0f) return ActionResult.MaxLevel;
            bool faith = Cfg.monsters.resistanceTracks.costResource == "faith";
            float have = faith ? m.Faith : m.DreamPower;
            if (have + 0.001f < cost) return ActionResult.NoMoney;
            if (faith) m.Faith -= cost; else m.DreamPower -= cost;
            m.ResistLevels[type]++;
            AddFloater(m.Pos + Vector2.up * 2.2f, DamageTypes.Label(type) + " resist " + m.ResistLevels[type], (Color)Palette.Bone);
            if (!m.IsHuman) AddLog("The monster grew a thicker hide against " + DamageTypes.Label(type).ToLower() + ".");
            return ActionResult.Ok;
        }

        // ------------------------------------------------------------------ resident actions

        bool CanAct(Resident r) => r != null && r.Alive && r.Room != null && (Phase == Phase.Setup || Phase == Phase.Night);

        public float Wallet(Resident r, string res) => res == "faith" ? r.Faith : r.DreamPower;

        bool Spend(Resident r, string res, float cost)
        {
            if (Wallet(r, res) + 0.001f < cost) return false;
            if (res == "faith") r.Faith -= cost; else r.DreamPower -= cost;
            return true;
        }

        public TowerDef TowerById(string id) => Cfg.towers.towers.FirstOrDefault(t => t.id == id);

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
            room.DoorBroken = false;
            room.DoorHp = MaxDoorHp(room);
            room.DoorSr.sprite = Sprites.Door(room.DoorLevel);
            if (Monster != null && !Monster.Dead && !CanStand(Monster.Pos)) Unstick(Monster);
            AddFloater(HotelMap.Center(room.Def.DoorTile) + Vector2.up, "Door " + room.DoorLevel, (Color)Palette.Teal);
        }

        public ActionResult TryBuildTower(Resident r, int slot, string towerId)
        {
            if (!CanAct(r)) return ActionResult.Invalid;
            var room = r.Room;
            var def = TowerById(towerId);
            if (def == null || slot < 0 || slot >= room.Slots.Length || slot >= room.Def.Slots.Count || room.Slots[slot] != null)
                return ActionResult.Invalid;
            if (!Spend(r, def.costResource, def.buildCost)) return ActionResult.NoMoney;
            var pos = HotelMap.Center(room.Def.Slots[slot]);
            var t = new TowerInstance { Def = def, Level = 1, SlotIndex = slot };
            t.Sr = MakeSprite(def.name, Sprites.Tower(def.id), pos, OrderFor(pos.y), matchRoot);
            room.Slots[slot] = t;
            return ActionResult.Ok;
        }

        public float TowerUpgradeCost(TowerInstance t) =>
            t.Level >= Cfg.towers.maxLevel ? -1f : UpgradeRules.TowerUpgradeCost(Cfg.towers, t.Def, t.Level);

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
            AddFloater(HotelMap.Center(r.Room.Def.Slots[slot]) + Vector2.up * 0.8f, "Lv " + t.Level, (Color)Palette.Bone);
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

        // ------------------------------------------------------------------ messages

        public void SetSpeed(float s)
        {
            Speed = s;
            if (Phase == Phase.Setup || Phase == Phase.Night) Time.timeScale = s;
        }

        public void Announce(string text, float seconds)
        {
            Banner = text;
            BannerUntil = Time.unscaledTime + seconds;
        }

        public void AddLog(string text)
        {
            Log.Add(text);
            if (Log.Count > 6) Log.RemoveAt(0);
        }

        public void AddFloater(Vector2 pos, string text, Color color)
        {
            Floaters.Add(new Floater { Pos = pos, Text = text, Color = color, Born = Time.unscaledTime });
        }
    }
}
