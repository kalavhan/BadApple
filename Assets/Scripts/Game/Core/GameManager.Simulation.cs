using System.Collections.Generic;
using System.Linq;
using BadAppleHotel.Config;
using UnityEngine;
namespace BadAppleHotel.Game
{
    public class MatchMetrics
    {
        public float FirstAttackSeconds = -1;
        public int DoorBreaks;
        public int MinionsSpawned, MinionsKilled, KillsByMinions;
        public readonly Dictionary<int,int> AttacksPerNight = new Dictionary<int,int>();
        public readonly Dictionary<int,int> DoorAssaultsPerNight = new Dictionary<int,int>();
        public readonly List<int> LevelPerNight = new List<int>();
    }
    public partial class GameManager
    {
        static void RemoveObject(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
        }
        public bool Simulation { get; private set; }

        /// <summary>Forces every bot's skill (0..1) when set, e.g. the bot sim measuring expert play.</summary>
        public float? BotSkillOverride { get; set; }

        /// <summary>
        /// How well the bots play right now, 0 (novice) to 1 (expert). A standard match climbs from botSkill.start on
        /// night 1 to botSkill.end on the last night; endless climbs over botSkill.endlessRampNights nights and stays there.
        /// Bots of both sides use it: a human monster faces novice residents early, a human resident a novice monster.
        /// </summary>
        public float BotSkill
        {
            get
            {
                if (BotSkillOverride.HasValue) return Mathf.Clamp01(BotSkillOverride.Value);
                var s = Cfg.match.botSkill;
                int nights = Endless ? Mathf.Max(2, s.endlessRampNights) : Mathf.Max(2, Cfg.match.nightCount);
                float k = Mathf.Clamp01((Mathf.Max(1, Night) - 1) / (float)(nights - 1));
                return Mathf.Lerp(s.start, s.end, k);
            }
        }
        public MatchMetrics Metrics { get; private set; } = new MatchMetrics();
        Room lastAssault;
        float lastAssaultTime;
        Room lastDoorAssault;
        float lastDoorAssaultTime;
        public void StartSimulation(GameConfig config, int seed, bool endless)
        {
            Simulation = true; Cfg = config;
            Random.InitState(seed); Sprites.ArtOverride = false;
            StartMatch(Role.Resident, "stitchwork_chef");
            Endless = endless;
            foreach (var r in Residents)
            {
                r.IsHuman = false;
                if (r.Ai == null) r.Ai = new ResidentAI(this,r);
            }
            enabled = false;
        }
        public void StepMatch(float dt)
        {
            if (!InMatch) return;
            Now += dt; PhaseTimer -= dt;
            UpdateResidents(dt, Now);
            UpdateEconomy(dt, Now);
            UpdateDisguise(dt);
            UpdateProgression(dt, Now);
            UpdateMonster(dt, Now);
            UpdateMinions(dt, Now);
            UpdateTowers(dt, Now);
            if (PendingHelpFrom != null && Now > PendingHelpUntil) PendingHelpFrom = null;
            if (Phase == Phase.Setup && PhaseTimer > 3f && Residents.All(r => !r.Alive || r.Room != null))
            {
                PhaseTimer = 3f;
                Announce("Lights out in 3…", 3f);
            }
            if (Phase == Phase.Setup && PhaseTimer <= 0f) BeginNights();
            else if (Phase == Phase.Night)
            {
                if (Residents.All(r => !r.Alive)) EndMatch();
                else if (PhaseTimer <= 0f) EndNight();
            }
        }

        void RecordAssault(Room room, bool door = true)
        {
            if (door)
            {
                if (Metrics.FirstAttackSeconds < 0) Metrics.FirstAttackSeconds = Now - Monster.RevealedAt;
                if (lastDoorAssault != room || Now-lastDoorAssaultTime > 2f || !Metrics.DoorAssaultsPerNight.ContainsKey(Night))
                {
                    if (!Metrics.DoorAssaultsPerNight.ContainsKey(Night)) Metrics.DoorAssaultsPerNight[Night] = 0;
                    Metrics.DoorAssaultsPerNight[Night]++;
                }
                lastDoorAssault = room; lastDoorAssaultTime = Now;
            }
            if (lastAssault != room || Now-lastAssaultTime > 2f || !Metrics.AttacksPerNight.ContainsKey(Night))
            {
                if (!Metrics.AttacksPerNight.ContainsKey(Night)) Metrics.AttacksPerNight[Night] = 0;
                Metrics.AttacksPerNight[Night]++;
            }
            lastAssault = room; lastAssaultTime = Now;
        }
        public void DisposeSimulation()
        {
            if (matchRoot != null) DestroyImmediate(matchRoot.gameObject);
            if (worldRoot != null) DestroyImmediate(worldRoot.gameObject);
            // A simulation can be created and disabled in EditMode without ever receiving
            // Awake. Unity does not guarantee OnDestroy for that lifecycle, so explicitly
            // release the native meshes/materials/textures as well as the scene objects.
            // The cleanup is idempotent if Unity also sends OnDestroy during destruction.
            OnDestroy();
            Sprites.ArtOverride = null;
            DestroyImmediate(gameObject);
        }
    }
}
