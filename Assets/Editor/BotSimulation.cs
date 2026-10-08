using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BadAppleHotel.Config;
using BadAppleHotel.Game;
using UnityEditor;
using UnityEngine;
namespace BadAppleHotel.EditorTools
{
    public static class BotSimulation
    {
        [Serializable] public class Row
        {
            public int seed, night, kills, doorBreaks, monsterLevel;
            public string monster;
            public float fearEarned;
            public int minionRanks, minionsSpawned, minionsKilled, killsByMinions;
            public bool endless, residentsWin, censored;
            public float firstAttackSeconds;
            public int[] attacksPerNight, doorAssaultsPerNight, levelPerNight;
        }
        [Serializable] public class Report
        {
            public string generatedUtc;
            public int seed, count;
            public float residentWinRate, medianEndlessNight, maxFirstAttack;
            public int missedAssaultNights, censoredRuns;
            public List<Row> matches = new List<Row>();
        }
        [MenuItem("Bad Apple/Simulate 50 matches")]
        public static void Run()
        {
            int count = int.TryParse(Environment.GetEnvironmentVariable("BADAPPLE_SIM_COUNT"),out int n)?n:50;
            int seed = int.TryParse(Environment.GetEnvironmentVariable("BADAPPLE_SIM_SEED"),out int s)?s:20261004;
            string mode=Environment.GetEnvironmentVariable("BADAPPLE_SIM_MODE")??"standard";
            string path=Environment.GetEnvironmentVariable("BADAPPLE_SIM_PATH")??Path.GetFullPath("Builds/bot-simulation.json");
            var report=new Report { generatedUtc=DateTime.UtcNow.ToString("O"),seed=seed,count=count };
            var cfg=ConfigLoader.Load(); bool art=Sprites.UseArt; var rng=UnityEngine.Random.state;
            try
            {
                foreach(bool endless in mode=="both" ? new[]{false,true} : new[]{mode=="endless"})
                    for(int i=0;i<count;i++)
                    {
                        var gm=new GameObject("Bot simulation").AddComponent<GameManager>();
                        try
                        {
                            gm.StartSimulation(cfg,seed+i,endless);
                            // 16 simulation ticks per batch; identical 30 Hz physics/combat to the player.
                            int steps=0,maxSteps=(int)((cfg.match.setupSeconds+cfg.match.nightSeconds*30)*30);
                            while(gm.InMatch && steps<maxSteps)
                                for(int tick=0;tick<16 && gm.InMatch && steps<maxSteps;tick++,steps++) gm.StepMatch(1f/30f);
                            var row=new Row { seed=seed+i,endless=endless,night=gm.Night,kills=gm.Monster.Kills,doorBreaks=gm.Metrics.DoorBreaks,
                                monsterLevel=gm.Monster.Level,residentsWin=gm.Result?.ResidentsWin??false,censored=gm.InMatch,
                                firstAttackSeconds=gm.Metrics.FirstAttackSeconds,
                                attacksPerNight=Enumerable.Range(1,gm.Night).Select(night=>gm.Metrics.AttacksPerNight.TryGetValue(night,out int attacks)?attacks:0).ToArray(),
                                doorAssaultsPerNight=Enumerable.Range(1,gm.Night).Select(night=>gm.Metrics.DoorAssaultsPerNight.TryGetValue(night,out int attacks)?attacks:0).ToArray(),
                                levelPerNight=gm.Metrics.LevelPerNight.ToArray(),
                                monster=gm.Monster.Def.id, fearEarned=gm.Monster.FearEarned, minionRanks=gm.Monster.HordeStrength + gm.Monster.MinionOwned.Count(o=>o) + gm.Monster.MinionEvolved.Count(e=>e),
                                minionsSpawned=gm.Metrics.MinionsSpawned, minionsKilled=gm.Metrics.MinionsKilled, killsByMinions=gm.Metrics.KillsByMinions };
                            report.matches.Add(row);
                            Debug.Log("SIM "+(endless?"endless":"standard")+" "+(i+1)+"/"+count+" seed="+row.seed+" night="+row.night+" kills="+row.kills+" first="+row.firstAttackSeconds+" level="+row.monsterLevel);
                        }
                        finally { gm.DisposeSimulation(); }
                    }
                var standard=report.matches.Where(r=>!r.endless).ToArray();
                report.residentWinRate=standard.Length==0?0:(float)standard.Count(r=>r.residentsWin)/standard.Length;
                var nights=report.matches.Where(r=>r.endless).Select(r=>r.night).OrderBy(v=>v).ToArray();
                report.medianEndlessNight=nights.Length==0?0:(nights[(nights.Length-1)/2]+nights[nights.Length/2])/2f;
                report.maxFirstAttack=report.matches.Max(r=>r.firstAttackSeconds);
                report.missedAssaultNights=report.matches.Sum(r=>r.attacksPerNight.Count(a=>a==0));
                report.censoredRuns=report.matches.Count(r=>r.censored);
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path,JsonUtility.ToJson(report,true));
                Debug.Log("SIM REPORT "+path+" wins="+report.residentWinRate+" endless median="+report.medianEndlessNight);
            }
            finally { Sprites.UseArt=art; UnityEngine.Random.state=rng; }
        }
    }
}
