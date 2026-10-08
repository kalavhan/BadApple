using System;
using System.Linq;
using BadAppleHotel.Config;
using UnityEngine;
using Random = UnityEngine.Random;
namespace BadAppleHotel.Game
{
    /// <summary>A pick the monster owes after a level: a stat track ("Stat"), a kit ability rank ("Rank") or a utility ability ("Utility").</summary>
    public class ProgressChoice
    {
        public string Kind;
        public int Level;
        public string[] Options;
    }
    public partial class GameManager
    {
        public bool Endless { get; private set; }
        public float Now { get; private set; }
        float nextPartSpawn;
        public float Income(float raw) => Mathf.Min(raw, Cfg.progression.incomeThreshold) + Mathf.Max(0, raw-Cfg.progression.incomeThreshold)*Cfg.progression.incomeExcessMultiplier;
        public void StartEndless(Role role, string pick) { StartMatch(role, pick); Endless = true; }
        public static int PersonalBest(Role role) => PlayerPrefs.GetInt("bah_endless_" + role, 0);

        static readonly string[] KitSlots = { "attack", "area", "special" };

        void InitializeProgression(Monster m)
        {
            m.LastDamageAt = m.LastKillAt = Now;
            m.StatRanks = new int[Cfg.progression.statTracks.Length];
            var area = Cfg.abilities.abilities.First(a => a.id == m.Def.area);
            m.Loadout = new[] { area };
            m.Cooldowns = new float[1];
            nextPartSpawn = Now + Cfg.progression.partSpawnSeconds;
        }

        // ------------------------------------------------------------------ Fear

        /// <summary>Fear is the monster's only currency: it buys levels and minion upgrades.</summary>
        public void AddFear(Monster m, float amount)
        {
            if (m == null || amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return;
            m.Fear += amount; m.FearEarned += amount;
        }

        /// <summary>Fear price of the monster's next level, or -1 at the cap.</summary>
        public float LevelPrice(Monster m) => LevelPrice(Cfg.progression, m.Level);
        public static float LevelPrice(MonsterProgressionConfig p, int level) =>
            level >= p.maxLevel ? -1f : p.levelPriceBase + p.levelPriceStep * (level - 1);

        public ActionResult TryBuyLevel(Monster m)
        {
            if (m == null) return ActionResult.Invalid;
            float price = LevelPrice(m);
            if (price < 0f) return ActionResult.MaxLevel;
            if (m.Fear + 0.001f < price) return ActionResult.NoMoney;
            m.Fear -= price;
            LevelUp(m);
            return ActionResult.Ok;
        }

        /// <summary>One tap on a stat in the monster's ring: buys the next level and puts its point there.</summary>
        public ActionResult TryBuyLevelInto(Monster m, string statId)
        {
            if (m == null || m.StatRanks == null) return ActionResult.Invalid;
            int i = Array.FindIndex(Cfg.progression.statTracks, s => s.id == statId);
            if (i < 0) return ActionResult.Invalid;
            if (m.StatRanks[i] >= Cfg.progression.statTracks[i].maxRank) return ActionResult.MaxLevel;
            // Settle any stat point already owed first, so this tap's point lands where it was aimed.
            if (m.Choices.Count > 0 && m.Choices.Peek().Kind == "Stat") return ChooseStat(m, statId) ? ActionResult.Ok : ActionResult.Invalid;
            var result = TryBuyLevel(m);
            if (result != ActionResult.Ok) return result;
            // LevelUp queues the stat pick first; ranks or utilities for this level wait behind it.
            ChooseStat(m, statId);
            return ActionResult.Ok;
        }

        bool ChooseStat(Monster m, string statId)
        {
            if (m.Choices.Count == 0 || m.Choices.Peek().Kind != "Stat") return false;
            int option = Array.IndexOf(m.Choices.Peek().Options, statId);
            if (option < 0) return false;
            ChooseProgression(option);
            return true;
        }

        public int StatRank(Monster m, string id)
        {
            int i = Array.FindIndex(Cfg.progression.statTracks, s => s.id == id);
            return i < 0 || m.StatRanks == null ? 0 : m.StatRanks[i];
        }

        /// <summary>True when a rank or utility pick is waiting for the player.</summary>
        public bool HasPendingPick(Monster m) => m != null && m.Choices.Count > 0 && m.Choices.Peek().Kind != "Stat";

        /// <summary>Each night start: a guaranteed Fear payout, so a horde build is not starved, and the special arrives
        /// on schedule (specialUnlockNight) even when Fear went into minions instead of levels.</summary>
        void NightStartForMonster(Monster m)
        {
            if (m == null) return;
            AddFear(m, Cfg.progression.fear.perNight);
            if (Night >= Cfg.progression.specialUnlockNight) AddToLoadout(m, Cfg.abilities.abilities.First(a => a.id == m.Def.special));
        }

        void LevelUp(Monster m)
        {
            var p = Cfg.progression;
            if (m.Level >= p.maxLevel) return;
            float before = MaxHp(m); m.Level++; m.Hp += MaxHp(m) - before;
            AddFloater(m.Pos + Vector2.up * 2, "LEVEL " + m.Level, ColorOf(m));
            Roar();
            var stats = Enumerable.Range(0, p.statTracks.Length).Where(i => m.StatRanks[i] < p.statTracks[i].maxRank).Select(i => p.statTracks[i].id).ToArray();
            if (stats.Length > 0) m.Choices.Enqueue(new ProgressChoice { Kind = "Stat", Level = m.Level, Options = stats });
            if (m.Level == p.specialLevel) AddToLoadout(m, Cfg.abilities.abilities.First(a => a.id == m.Def.special));
            if (p.abilityRankLevels.Contains(m.Level)) OfferRank(m);
            if (p.utilityLevels.Contains(m.Level)) OfferUtility(m);
            if (!m.IsHuman) AutoChoose(m);
        }

        void AddToLoadout(Monster m, AbilityDef a)
        {
            if (m.Loadout.Any(x => x.id == a.id) || m.Loadout.Length >= 5) return;
            m.Loadout = m.Loadout.Concat(new[] { a }).ToArray();
            var cds = m.Cooldowns; Array.Resize(ref cds, m.Loadout.Length); m.Cooldowns = cds;
        }

        void OfferRank(Monster m)
        {
            bool special = m.Loadout.Any(a => a.id == m.Def.special);
            var options = Enumerable.Range(0, 3).Where(k => m.KitRanks[k] < 3 && (k < 2 || special)).Select(k => KitSlots[k]).ToArray();
            if (options.Length > 0) m.Choices.Enqueue(new ProgressChoice { Kind = "Rank", Level = m.Level, Options = options });
        }

        void OfferUtility(Monster m)
        {
            var used = m.Loadout.Select(a => a.id).ToArray();
            var options = Cfg.progression.utilityAbilities.Where(id => !used.Contains(id)).OrderBy(_ => Random.value).Take(3).ToArray();
            if (options.Length > 0) m.Choices.Enqueue(new ProgressChoice { Kind = "Utility", Level = m.Level, Options = options });
        }

        /// <summary>Bots pick by their monster's role: a bruiser leans on health and damage, an assassin on speed.</summary>
        void AutoChoose(Monster m)
        {
            while (m.Choices.Count > 0)
            {
                var choice = m.Choices.Peek();
                int pick = Random.Range(0, choice.Options.Length);
                if (choice.Kind == "Stat")
                {
                    string[] order = m.Def.id == "bellhop_wraith" ? new[] { "frenzy", "stride", "maw", "vitality", "hide" }
                        : m.Def.id == "moldy_matron" ? new[] { "vitality", "hide", "frenzy", "maw", "stride" }
                        : new[] { "maw", "vitality", "hide", "frenzy", "stride" };
                    // Mostly the favourite still open, sometimes anything, so bots differ match to match.
                    var favourite = order.FirstOrDefault(id => choice.Options.Contains(id) && Random.value < 0.6f);
                    if (favourite != null) pick = Array.IndexOf(choice.Options, favourite);
                }
                ChooseProgression(pick);
            }
        }

        public string ChoiceTitle(ProgressChoice choice)
        {
            switch (choice.Kind)
            {
                case "Stat": return "Level " + choice.Level + " · grow a stat";
                case "Rank": return "Level " + choice.Level + " · sharpen an ability";
                default: return "Level " + choice.Level + " · learn a trick";
            }
        }

        public string ChoiceLabel(ProgressChoice choice, string option)
        {
            var m = Monster;
            switch (choice.Kind)
            {
                case "Stat":
                {
                    int i = Array.FindIndex(Cfg.progression.statTracks, s => s.id == option);
                    var s2 = Cfg.progression.statTracks[i];
                    string sign = s2.id == "hide" ? "−" : "+";
                    return s2.name + "\n" + sign + Mathf.RoundToInt(s2.perRank * 100) + "% " + s2.stat.ToLower() + "\nrank " + (m.StatRanks[i] + 1) + "/" + s2.maxRank;
                }
                case "Rank":
                {
                    int k = Array.IndexOf(KitSlots, option);
                    return KitName(m, k) + "\nrank " + (m.KitRanks[k] + 1) + "\n+" + Mathf.RoundToInt((RankDamage(m.KitRanks[k] + 1) / RankDamage(m.KitRanks[k]) - 1) * 100) + "% damage";
                }
                default:
                    return Cfg.abilities.abilities.First(a => a.id == option).name;
            }
        }

        /// <summary>Kit slot name: 0 single target, 1 area, 2 special.</summary>
        public string KitName(Monster m, int k)
        {
            if (k == 0) return m.Def.attackName;
            string id = k == 1 ? m.Def.area : m.Def.special;
            return Cfg.abilities.abilities.First(a => a.id == id).name;
        }

        public void ChooseProgression(int index)
        {
            var m = Monster;
            if (m == null || m.Choices.Count == 0) return;
            var choice = m.Choices.Peek();
            if (index < 0 || index >= choice.Options.Length) return;
            m.Choices.Dequeue(); string option = choice.Options[index];
            switch (choice.Kind)
            {
                case "Stat":
                {
                    int i = Array.FindIndex(Cfg.progression.statTracks, s => s.id == option);
                    float before = MaxHp(m);
                    m.StatRanks[i] = Mathf.Min(Cfg.progression.statTracks[i].maxRank, m.StatRanks[i] + 1);
                    m.Hp += MaxHp(m) - before;
                    break;
                }
                case "Rank":
                {
                    int k = Array.IndexOf(KitSlots, option);
                    if (k >= 0) m.KitRanks[k] = Mathf.Min(3, m.KitRanks[k] + 1);
                    break;
                }
                default:
                    AddToLoadout(m, Cfg.abilities.abilities.First(a => a.id == option));
                    break;
            }
        }

        void UpdateProgression(float dt, float now)
        {
            var m = Monster;
            if (m == null || Phase != Phase.Night) return;
            if (!m.Dead)
            {
                var fear = Cfg.progression.fear;
                if (now - m.LastDamageAt >= fear.idleAfterSeconds) AddFear(m, fear.idlePerSecond * dt);
                m.Frenzy = now - m.LastDamageAt >= Cfg.progression.hungerSeconds;
                if (now >= m.NextSprint && Residents.Any(r => r.Alive && Map.Get(HotelMap.ToTile(r.Pos).x, HotelMap.ToTile(r.Pos).y) == Tile.Corridor && Vector2.Distance(m.Pos,r.Pos)<12 && ClearLine(m.Pos,r.Pos)))
                { m.SprintUntil = now + Cfg.monsters.sprintSeconds; m.NextSprint = now + Cfg.monsters.sprintCooldown; }
                if (m.Lair != null && m.Lair.Def.ContainsInterior(HotelMap.ToTile(m.Pos)))
                    m.Hp = Mathf.Min(MaxHp(m), m.Hp + MaxHp(m)*Cfg.progression.lairHealPerSecond*dt);
                if (now < m.SlowZoneUntil)
                    foreach (var r in Residents) if (Vector2.Distance(r.Pos,m.Pos)<4) r.SlowUntil = now+0.2f;
            }
            if (now >= nextPartSpawn)
            {
                nextPartSpawn = now + Mathf.Max(Cfg.progression.partSpawnMinSeconds, Cfg.progression.partSpawnSeconds-Night*Cfg.progression.partSpawnReductionPerNight);
                if (Parts.Count < Cfg.match.bodyPartsPerNight) AddOnePart();
            }
        }
        void AddOnePart()
        {
            var options = Map.BodyPartSpawns(Cfg.match.bodyPartsPerNight, Cfg.bodyParts.minSpacingTiles, Random.Range(1,int.MaxValue), Map.Rooms.Where(IsRoomFree).ToArray());
            foreach (var tile in options)
            {
                if (Parts.Any(p => Vector2Int.Distance(p.Tile,tile)<Cfg.bodyParts.minSpacingTiles)) continue;
                AddPartAt(tile);
                break;
            }
        }
        void AddPartAt(Vector2Int tile)
        {
            int type = Random.Range(0,Cfg.bodyParts.parts.Length); var def = Cfg.bodyParts.parts[type];
            var p = new BodyPart { Def=def, TypeIndex=type, Tile=tile };
            p.Sr = MakeSprite("part", Sprites.Part(def.id), HotelMap.Center(tile), OrderFor(tile.y), matchRoot);
            Parts.Add(p);
        }
        AudioClip roar;
        void Roar()
        {
            if (Simulation) return;
            if (roar==null)
            {
                int rate=22050; var data=new float[rate/4];
                for(int i=0;i<data.Length;i++) { float t=(float)i/rate; data[i]=Mathf.Sin(t*2*Mathf.PI*(100-100*t))*0.12f*(1-(float)i/data.Length); }
                roar=AudioClip.Create("monster rise",data.Length,1,rate,false); roar.SetData(data,0);
            }
            AudioSource.PlayClipAtPoint(roar,Cam.transform.position,0.5f);
        }
    }
}
