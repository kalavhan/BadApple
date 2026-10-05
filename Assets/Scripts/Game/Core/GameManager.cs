using System.Collections.Generic;
using System.Collections;
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


        // ------------------------------------------------------------------ lifecycle

        void Awake()
        {
            Instance = this;
            if (Application.isPlaying) SetupCamera();
        }

        bool loadingConfig;

        IEnumerator Start()
        {
            Application.targetFrameRate = 60;
            yield return Initialize();
        }

        /// <summary>Loads config asynchronously on Android before constructing the menu backdrop.</summary>
        IEnumerator Initialize()
        {
            loadingConfig = true;
            ConfigError = null;
            Cfg = null;
            SetupCamera();
            yield return ConfigLoader.LoadForPlayer(config => Cfg = config, error =>
            {
                ConfigError = error.Message;
                Phase = Phase.ConfigError;
                Debug.LogError("[Bad Apple Hotel] Config failed: " + error.Message);
            });
            loadingConfig = false;
            if (Cfg == null) yield break;
            if (!BuildWorld(Random.Range(1, 1 << 30))) yield break;
            Phase = Phase.RoleSelect;
        }

        /// <summary>Generates a fresh hotel and its tile visuals.</summary>
        bool BuildWorld(int seed)
        {
            if (worldRoot != null) RemoveObject(worldRoot.gameObject);
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
            if (!Simulation) { BuildScene3D(); CreateFog(); }
            else foreach (var def in Map.Rooms)
                doorSprites[def] = MakeSprite("door", null, HotelMap.Center(def.DoorTile), 0, worldRoot);
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
            Cam.transform.rotation = HotelView3D.Rotation;
            Cam.nearClipPlane = 0.1f; Cam.farClipPlane = 250f;
        }

        bool sleepCameraActive;
        bool recenterCamera;
        Vector2 cameraPosition, cameraVelocity;
        float lastCameraInput = -100f;
        public bool SleepingCamera => InMatch && Human != null && Human.Alive && Human.Asleep;
        public void DragCamera(Vector2 screenDelta)
        {
            if (!InMatch || Cam == null) return;
            if (!sleepCameraActive) cameraPosition = HotelView3D.GroundPoint(Cam, new Vector2(Screen.width/2f,Screen.height/2f));
            sleepCameraActive = true; recenterCamera = false;
            var middle = new Vector2(Screen.width/2f, Screen.height/2f);
            var delta = HotelView3D.GroundPoint(Cam, middle) - HotelView3D.GroundPoint(Cam, middle + screenDelta);
            cameraPosition += delta;
            cameraVelocity = Vector2.ClampMagnitude(delta / Mathf.Max(Time.unscaledDeltaTime, 0.008f), 40f);
            lastCameraInput = Time.unscaledTime;
        }
        public void RecenterCamera()
        {
            HotelView = false; recenterCamera = true; cameraVelocity = Vector2.zero;
        }
        public static Vector2 ClampCamera(Vector2 target, float size, float aspect, int width, int height)
        {
            float hw = size * aspect;
            return new Vector2(width <= hw * 2 ? width / 2f : Mathf.Clamp(target.x, hw, width - hw),
                height <= size * 2 ? height / 2f : Mathf.Clamp(target.y, size, height - size));
        }
        void LateUpdate()
        {
            if (Cam == null || Map == null) return;
            float aspect = Mathf.Max(0.5f, Cam.aspect), dt = Time.unscaledDeltaTime;
            if (HotelView && !HotelViewAvailable) HotelView = false;
            bool actor = InMatch && !HotelView && ((Monster != null && HumanRole == Role.Monster) || (Human != null && Human.Alive));
            float size = !HotelView && (actor || (InMatch && sleepCameraActive)) ? HotelView3D.FollowSize : Mathf.Max((Map.W+Map.H)*0.7071f*0.383f+2, (Map.W+Map.H)*0.7071f/(2*aspect)+2);
            Vector2 follow = actor ? (HumanRole == Role.Monster && Monster != null ? Monster.Pos : Human.Pos) : new Vector2(Map.W / 2f, Map.H / 2f);
            var target = HotelView3D.Clamp(follow, size, aspect, Map.W, Map.H);
            if (sleepCameraActive)
            {
                bool awake = actor && !SleepingCamera;
                if (recenterCamera || (awake && (Time.unscaledTime - lastCameraInput > 2f || GameInput.Move.sqrMagnitude > 0.01f)))
                {
                    cameraPosition = Vector2.Lerp(cameraPosition, target, 1f - Mathf.Exp(-8f * dt));
                    cameraVelocity = Vector2.zero;
                    if ((cameraPosition - target).sqrMagnitude < 0.01f) { sleepCameraActive = false; recenterCamera = false; }
                }
                else if (Time.unscaledTime > lastCameraInput + 0.04f)
                {
                    cameraPosition += cameraVelocity * dt;
                    cameraVelocity *= Mathf.Exp(-6f * dt);
                }
                target = cameraPosition = HotelView
                    ? new Vector2(Mathf.Clamp(cameraPosition.x,0,Map.W),Mathf.Clamp(cameraPosition.y,0,Map.H))
                    : HotelView3D.Clamp(cameraPosition, size, aspect, Map.W, Map.H);
            }
            Cam.orthographicSize = size;
            Cam.transform.position = new Vector3(target.x, target.y, 0) - HotelView3D.Forward * 100f;
            if (!Simulation) UpdateWallOcclusion();
        }

        public bool HotelViewAvailable =>
            InMatch; // Overview keeps the same sight mask; it does not reveal hidden actors.

        // ------------------------------------------------------------------ visuals helpers

        public static int OrderFor(float y) => 5000 - Mathf.RoundToInt(y * 10f);

        Material worldSpriteMaterial;

        SpriteRenderer MakeSprite(string name, Sprite sprite, Vector2 pos, int order, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(pos.x, pos.y, -0.025f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            if (!Simulation)
            {
                if (worldSpriteMaterial == null)
                {
                    var shader = Resources.Load<Shader>("Shaders/CharacterSprite");
                    if (shader != null) worldSpriteMaterial = new Material(shader) { name = "World sprites (depth)" };
                }
                if (worldSpriteMaterial != null) sr.sharedMaterial = worldSpriteMaterial;
            }
            return sr;
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
            Endless = false;
            Now = 0;
            Metrics = new MatchMetrics();
            HotelView = false;

            var defs = Cfg.monsters.monsters;
            var mdef = role == Role.Monster
                ? (defs.FirstOrDefault(d => d.id == monsterPick) ?? defs[0])
                : defs[Random.Range(0, defs.Length)];

            hiddenDefinition = mdef;
            int hiddenSeat = role == Role.Monster ? 0 : Random.Range(1, Cfg.match.playersPerMatch);

            int residentLevelForHuman = AccountProgress.Level(Cfg, Role.Resident);
            var roster = PickRoster(Cfg.match.playersPerMatch);
            for (int i = 0; i < Cfg.match.playersPerMatch; i++)
            {
                bool human = i == 0;
                var r = new Resident
                {
                    Id = i,
                    Name = human ? "You" : roster[i].name,
                    Char = roster[i],
                    IsHuman = human,
                    IsMonster = i == hiddenSeat,
                    ColorIndex = i,
                    Health = Cfg.match.residentHealth,
                    DreamPower = Cfg.economy.startingResources.dreamPower,
                    Faith = Cfg.economy.startingResources.faith,
                    XpValue = ResidentXpValue(human ? residentLevelForHuman : Random.Range(1, 6)),
                };
                r.Pos = LobbySpot(i);
                r.Sr = MakeSprite(r.Name, Sprites.Resident(i), r.Pos, OrderFor(r.Pos.y), matchRoot);
                if (!Simulation) ContactShadow.Attach(r.Sr, r.Pos, new Vector2(.65f, .45f));
                r.LastPos = r.Pos;
                if (Sprites.UseArt)
                {
                    var hues = Cfg.residents.shirtHues;
                    float hue = hues != null && hues.Length > 0 ? hues[i % hues.Length] : 0.86f;
                    r.Anim = CharacterAnimator.Attach(r.Sr, CharacterSet.Load(r.Char.art, 1.55f), hue);
                }
                if (!human)
                {
                    r.Ai = new ResidentAI(this, r);
                    r.ClaimAt = Random.Range(1.5f, Mathf.Max(3f, Cfg.match.setupSeconds * 0.45f));
                }
                Residents.Add(r);
                if (r.IsMonster) HiddenMonster = r;
            }
            Human = Residents[0];
            nextDisguiseBuild = Random.Range(6f, 14f);

            Phase = Phase.Setup;
            PhaseTimer = Cfg.match.setupSeconds;
            Night = 0;
            Time.timeScale = Speed;
            Announce("Seven guests checked in. Find a room and sleep before lights out.", 7f);
            AddLog("One guest has a terrible secret.");
        }

        /// <summary>The character the human wants to play (roster id); null = random. Everyone else is shuffled from the rest.</summary>
        public string PickedCharacter;

        /// <summary>Roster characters for the seats; seat 0 (the human) gets the picked one.</summary>
        RosterDef[] PickRoster(int seats)
        {
            var all = Cfg.residents.roster.ToList();
            var order = new List<RosterDef>();
            var pick = all.FirstOrDefault(c => c.id == PickedCharacter);
            if (pick != null) { order.Add(pick); all.Remove(pick); }
            while (all.Count > 0)
            {
                int k = Random.Range(0, all.Count);
                order.Add(all[k]);
                all.RemoveAt(k);
            }
            var result = new RosterDef[seats];
            for (int i = 0; i < seats; i++) result[i] = order[i % order.Count];
            return result;
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
            if (matchRoot != null) RemoveObject(matchRoot.gameObject);
            matchRoot = null;
            foreach (var p in projectiles) if (p.T != null) RemoveObject(p.T.gameObject);
            projectiles.Clear();
            Residents.Clear();
            RoomsByDef.Clear();
            Parts.Clear();
            Log.Clear();
            Floaters.Clear();
            Monster = null;
            HiddenMonster = null;
            Human = null;
            PendingHelpFrom = null;
            HotelView = false;
            sleepCameraActive = false;
            GameInput.ClearAll();
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
            var bedPos = def.BedCenter;
            room.BedSr = MakeSprite("Bed", Sprites.Bed(1), bedPos, OrderFor(bedPos.y + 0.4f), matchRoot);
            room.BedSr.transform.rotation = Quaternion.Euler(0f, 0f, def.BedRotation);
            room.BedSr.transform.localScale = SleepPose.BedScale(room.BedSr.sprite);
            RoomsByDef[def] = room;
            r.Room = room;
            RefreshDoor(room);
            if (teleport)
            {
                r.Pos = HotelMap.Center(def.BedTile);
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
            int slot = -1;
            for (int i = 0; i < room.Slots.Length; i++) if (CanBuildAt(room, i)) { slot = i; break; }
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
            if (loadingConfig || Simulation) return;
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
                StepMatch(dt);
            }

            foreach (var r in Residents) if (r.Alive) PlaceResidentSprite(r);
            UpdateTowerBlink(Time.time);



        }

        void Recover()
        {
            Debug.LogWarning("[Bad Apple Hotel] Scripts reloaded mid-match; the match state was lost. Back to the menu.");
            foreach (var n in new[] { "World", "Match", "Fog" })
            {
                var go = GameObject.Find(n);
                if (go != null) RemoveObject(go);
            }
            doorSprites.Clear();
            projectiles.Clear();
            Residents.Clear();
            RoomsByDef.Clear();
            Parts.Clear();
            Floaters.Clear();
            Monster = null;
            HiddenMonster = null;
            Human = null;
            PendingHelpFrom = null;
            Time.timeScale = 1f;
            Phase = Phase.RoleSelect;
            StartCoroutine(Initialize());
        }

        void BeginNights()
        {
            foreach (var r in Residents) if (r.Room == null) ClaimRandom(r, true);
            Night = 1;
            Phase = Phase.Night;
            PhaseTimer = Cfg.match.nightSeconds;
            HotelView = false;
            RevealMonster();
            SpawnParts();

        }

        void EndNight()
        {
            foreach (var r in Residents) if (r.Alive) r.NightsSurvived++;
            if (!Endless && Night >= Cfg.match.nightCount) { EndMatch(); return; }
            Metrics.LevelPerNight.Add(Monster.Level);
            Night++;
            for (int i = 0; i < (Endless ? Cfg.match.endless.freeLevelsPerNight : 1); i++) LevelUp(Monster);
            PhaseTimer = Cfg.match.nightSeconds + Cfg.match.nightBreakSeconds;
            SpawnParts();
            Announce("Night " + Night + " of " + Cfg.match.nightCount + ".", 3f);
        }

        void EndMatch()
        {
            Metrics.LevelPerNight.Add(Monster.Level);
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
            if (!Simulation) AccountProgress.AddXp(HumanRole, res.XpGained);
            if (Endless && !Simulation) { PlayerPrefs.SetInt("bah_endless_" + HumanRole, Mathf.Max(PersonalBest(HumanRole), HumanRole == Role.Resident && Human != null ? Human.NightsSurvived : Night-1)); PlayerPrefs.Save(); }
            res.LevelAfter = AccountProgress.Level(Cfg, HumanRole);
            Result = res;
            Announce(res.ResidentsWin ? "Dawn breaks. The residents survived!" : "Silence. The monster ate everyone.", 6f);
        }

        void SpawnParts()
        {
            foreach (var p in Parts) if (p.Sr != null) RemoveObject(p.Sr.gameObject);
            Parts.Clear();
            var tiles = Map.BodyPartSpawns(Cfg.match.bodyPartsPerNight, Cfg.bodyParts.minSpacingTiles,
                unchecked(Map.Seed + Night * 7919), Map.Rooms.Where(IsRoomFree));
            foreach (var t in tiles)
            {
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
            if (Simulation) return;
            Floaters.Add(new Floater { Pos = pos, Text = text, Color = color, Born = Time.unscaledTime });
        }
    }
}
