using System;
using System.Linq;
using BadAppleHotel.Config;
using UnityEngine;
using Random = UnityEngine.Random;
namespace BadAppleHotel.Game
{
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
        public double NextMonsterLevelXp(Monster m) => Cfg.progression.baseXp * Math.Pow(Cfg.progression.growth, m.Level - 1);
        public EvolutionDef Evolution(Monster m) => Cfg.progression.evolutions.LastOrDefault(e => e.monster == m.Def.id && e.branch == m.Branch && e.level <= m.EvolutionStage*5);
        public float Income(float raw) => Mathf.Min(raw, Cfg.progression.incomeThreshold) + Mathf.Max(0, raw-Cfg.progression.incomeThreshold)*Cfg.progression.incomeExcessMultiplier;
        public void StartEndless(Role role, string pick) { StartMatch(role, pick); Endless = true; }
        public static int PersonalBest(Role role) => PlayerPrefs.GetInt("bah_endless_" + role, 0);

        void InitializeProgression(Monster m)
        {
            m.AccountLevel = m.IsHuman ? AccountProgress.Level(Cfg, Role.Monster) : 20;
            m.LastDamageAt = m.LastKillAt = Now;
            OfferSkill(m, 1);
            if (!m.IsHuman) AutoChoose(m);
            nextPartSpawn = Now + Cfg.progression.partSpawnSeconds;
        }
        void OfferSkill(Monster m, int level)
        {
            var pool = Cfg.progression.pools.First(p => p.monster == m.Def.id);
            var used = m.Loadout.Select(a => a.id).ToArray();
            var options = pool.abilities.Where(id => !used.Contains(id) && Cfg.abilities.abilities.Any(a => a.id == id && a.unlockMonsterLevel <= m.AccountLevel))
                .OrderBy(_ => Random.value).Take(3).ToArray();
            if (options.Length > 0) m.Choices.Enqueue(new ProgressChoice { Kind = "Skill", Level = level, Options = options });
        }
        public void GainMonsterXp(Monster m, double amount)
        {
            if (amount <= 0 || double.IsNaN(amount) || double.IsInfinity(amount)) return;
            m.MatchXp += amount; m.LevelXp += amount;
            while (m.LevelXp >= NextMonsterLevelXp(m))
            {
                m.LevelXp -= NextMonsterLevelXp(m); LevelUp(m);
            }
        }
        void LevelUp(Monster m)
        {
            float before = MaxHp(m); m.Level++; m.Hp += MaxHp(m)-before;
            AddFloater(m.Pos + Vector2.up * 2, "LEVEL " + m.Level, (Color)Palette.Candle);
            Roar();
            if (Cfg.progression.slotLevels.Contains(m.Level)) OfferSkill(m, m.Level);
            if (Cfg.progression.evolutionLevels.Contains(m.Level))
            {
                var options = Cfg.progression.evolutions.Where(e => e.monster == m.Def.id && e.level == m.Level && e.accountLevel <= m.AccountLevel).Select(e => e.branch).ToArray();
                if (options.Length > 0) m.Choices.Enqueue(new ProgressChoice { Kind = "Evolution", Level = m.Level, Options = options });
            }
            else if (m.Level > 20 && (m.Level-20) % Cfg.progression.ascensionEvery == 0)
                m.Choices.Enqueue(new ProgressChoice { Kind = "Ascension", Level = m.Level, Options = Cfg.progression.ascensionPerks.OrderBy(_ => Random.value).Take(3).ToArray() });
            if (!m.IsHuman) AutoChoose(m);
        }
        void AutoChoose(Monster m) { while (m.Choices.Count > 0) ChooseProgression(Random.Range(0, m.Choices.Peek().Options.Length)); }
        public string ChoiceLabel(ProgressChoice choice, string option)
        {
            if (choice.Kind == "Skill") return Cfg.abilities.abilities.First(a => a.id == option).name;
            if (choice.Kind == "Evolution")
            {
                var e = Cfg.progression.evolutions.First(v => v.monster == Monster.Def.id && v.level == choice.Level && v.branch == option);
                return e.name + " · " + e.passive;
            }
            return option;
        }
        public void ChooseProgression(int index)
        {
            var m = Monster;
            if (m == null || m.Choices.Count == 0) return;
            var choice = m.Choices.Peek();
            if (index < 0 || index >= choice.Options.Length) return;
            m.Choices.Dequeue(); string option = choice.Options[index];
            if (choice.Kind == "Skill")
            {
                var ability = Cfg.abilities.abilities.First(a => a.id == option);
                if (m.Loadout.Any(a => a.id == option))
                {
                    OfferSkill(m, choice.Level);
                    return;
                }
                if (m.Loadout.Length < 5)
                {
                    m.Loadout = m.Loadout.Concat(new[] { ability }).ToArray();
                    Array.Resize(ref m.Cooldowns, m.Loadout.Length);
                }
            }
            else if (choice.Kind == "Evolution")
            {
                m.Branch = option; m.EvolutionStage = choice.Level / 5;
                var evo = Evolution(m);
                if (!string.IsNullOrEmpty(evo.art) && m.Sr != null)
                    m.Anim = CharacterAnimator.Attach(m.Sr, CharacterSet.Load(evo.art, 2.1f));
                Announce(evo.name + " awakens!", 3f); Roar();
            }
            else
            {
                float before = MaxHp(m); m.Ascensions++;
                switch (option)
                {
                    case "Vitality": m.BonusHp += 0.15f; break;
                    case "Haste": m.BonusSpeed += 0.03f; break;
                    case "Quickening": m.CooldownReduction = 1f-(1f-m.CooldownReduction)*0.95f; break;
                    case "Disruption": m.JamAura += 0.05f; break;
                    case "Sight": m.BonusReveal += 2f; break;
                }
                m.Hp += MaxHp(m)-before;
            }
        }
        void UpdateProgression(float dt, float now)
        {
            var m = Monster;
            if (m == null || Phase != Phase.Night) return;
            if (!m.Dead)
            {
                GainMonsterXp(m, Cfg.progression.aliveXpPerSecond * dt);
                m.Frenzy = now - m.LastDamageAt >= Cfg.progression.hungerSeconds;
                if (now >= m.NextSprint && Residents.Any(r => r.Alive && Map.Get(HotelMap.ToTile(r.Pos).x, HotelMap.ToTile(r.Pos).y) == Tile.Corridor && Vector2.Distance(m.Pos,r.Pos)<12 && ClearLine(m.Pos,r.Pos)))
                { m.SprintUntil = now + Cfg.monsters.sprintSeconds; m.NextSprint = now + Cfg.monsters.sprintCooldown; }
                if (m.Lair != null && m.Lair.Def.ContainsInterior(HotelMap.ToTile(m.Pos)))
                    m.Hp = Mathf.Min(MaxHp(m), m.Hp + MaxHp(m)*Cfg.progression.lairHealPerSecond*dt);
                EvolutionPassive(m, dt, now);
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
                int type = Random.Range(0,Cfg.bodyParts.parts.Length); var def = Cfg.bodyParts.parts[type];
                var p = new BodyPart { Def=def, TypeIndex=type, Tile=tile };
                p.Sr = MakeSprite("part", Sprites.Part(def.id), HotelMap.Center(tile), OrderFor(tile.y), matchRoot);
                Parts.Add(p); break;
            }
        }
        void EvolutionPassive(Monster m, float dt, float now)
        {
            if (m.Branch == "spore" || now < m.SlowZoneUntil)
                foreach (var r in Residents) if (Vector2.Distance(r.Pos,m.Pos)<4) r.SlowUntil = now+0.2f;
            if (m.Branch == "rot")
                foreach (var room in RoomsByDef.Values) for (int i=0; i<room.Slots.Length; i++)
                {
                    var t=room.Slots[i];
                    if (t==null || Vector2.Distance(HotelMap.Center(t.Tile),m.Pos)>4) continue;
                    t.Decay += dt;
                    if (t.Decay < 20f / Mathf.Max(1,m.EvolutionStage)) continue;
                    if (t.Sr!=null) RemoveObject(t.Sr.gameObject); room.Slots[i]=null;
                }
            if (m.IsHuman || now < m.EvolutionUntil || string.IsNullOrEmpty(m.Branch)) return;
            if (m.AttackingRoom != null || m.Biting != null) UseEvolution();
        }
        public void UseEvolution()
        {
            var m=Monster;
            if (m==null || m.Dead || Phase!=Phase.Night || string.IsNullOrEmpty(m.Branch) || Now<m.EvolutionUntil) return;
            var room=m.AttackingRoom;
            switch (m.Branch)
            {
                case "butcher":
                    if (room==null) return;
                    room.DoorHp = Mathf.Max(1,room.DoorHp-m.Def.doorDamagePerSecond*AttackMult(m)*3); break;
                case "glutton": m.Hp=Mathf.Min(MaxHp(m),m.Hp+MaxHp(m)*0.2f); break;
                case "spore": m.SlowZoneUntil=Now+10; break;
                case "rot":
                case "poltergeist":
                    foreach (var r in RoomsByDef.Values) foreach(var t in r.Slots)
                        if (t!=null && Vector2.Distance(HotelMap.Center(t.Tile),m.Pos)<8) t.DisabledUntil=Now+6;
                    break;
                case "phantom":
                    if (room==null || m.PhasedNight==Night || !TileMovement.CanStand(HotelMap.Center(room.Def.DoorInside),MonsterWalkable,MonsterRadius,Walls)) return;
                    m.Pos=HotelMap.Center(room.Def.DoorInside); m.PhasedNight=Night; break;
            }
            m.EvolutionUntil=Now+25*(1-m.CooldownReduction);
            AddFloater(m.Pos+Vector2.up*2,Evolution(m).ability,(Color)Palette.Mint);
        }
        SpriteRenderer ascensionAura;
        void DrawAscensionAura(Monster m)
        {
            if (m.Ascensions == 0) return;
            if (ascensionAura == null) ascensionAura = MakeSprite("Ascension aura", Sprites.Ring, m.Pos, OrderFor(m.Pos.y)-1, matchRoot);
            ascensionAura.enabled = m.Sr.enabled;
            ascensionAura.transform.position = m.Sr.transform.position + Vector3.up*0.5f;
            ascensionAura.transform.localScale = Vector3.one*(1.5f+0.2f*Mathf.Sin(Now*3));
            ascensionAura.color = new Color(0.5f,0.9f,1f,0.15f+Mathf.Min(0.35f,m.Ascensions*0.04f));
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
