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
            Sprites.ArtOverride = null;
            DestroyImmediate(gameObject);
        }
    }
}
