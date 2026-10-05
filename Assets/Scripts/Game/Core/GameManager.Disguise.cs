using System.Linq;
using BadAppleHotel.Config;
using UnityEngine;
namespace BadAppleHotel.Game
{
    public partial class GameManager
    {
        public Resident HiddenMonster { get; private set; }
        MonsterDef hiddenDefinition;
        float nextDisguiseBuild;
        void UpdateDisguise(float dt)
        {
            var guest = HiddenMonster;
            if (Phase != Phase.Setup || guest?.Room == null) return;
            nextDisguiseBuild -= dt;
            if (nextDisguiseBuild > 0) return;
            var range = Cfg.match.disguiseBuildEverySeconds;
            nextDisguiseBuild = Random.Range(range[0], range[1]);
            var room = guest.Room;
            if (room.Slots.Count(t => t != null) >= Cfg.match.disguiseMaxBuildings) return;
            if (room.BedLevel == 1)
            {
                room.BedLevel = 2; room.BedSr.sprite = Sprites.Bed(2); room.BedSr.transform.localScale = SleepPose.BedScale(room.BedSr.sprite);
                AddFloater(guest.Pos, Cfg.beds.levels[1].name, (Color)Palette.Candle);
            }
            else if (room.DoorLevel == 1 && Random.value < 0.25f)
            {
                room.DoorLevel = 2; room.DoorHp = MaxDoorHp(room); RefreshDoor(room);
            }
            else
            {
                int slot = Enumerable.Range(0, room.Slots.Length).Where(i => CanBuildAt(room, i)).DefaultIfEmpty(-1).First();
                if (slot < 0) return;
                var options = Cfg.towers.towers.Where(t => t.buildCost <= 100).ToArray();
                PlaceTower(room, slot, options[Random.Range(0, options.Length)]);
                room.Slots[slot].Decoy = true;
                AddFloater(HotelMap.Center(room.Slots[slot].Tile), room.Slots[slot].Def.name, (Color)Palette.Candle);
            }
        }
        void RevealMonster()
        {
            var guest = HiddenMonster;
            var m = new Monster { Def = hiddenDefinition, IsHuman = HumanRole == Role.Monster, Pos = guest.Pos, Lair = guest.Room, RevealedAt = Now };
            Monster = m;
            m.Loadout = new AbilityDef[0]; m.Cooldowns = new float[0];
            InitializeProgression(m);
            m.Hp = MaxHp(m);
            m.Sr = MakeSprite("Monster", Sprites.Monster(m.Def.id), m.Pos, OrderFor(m.Pos.y), matchRoot);
            if (!Simulation) ContactShadow.Attach(m.Sr, m.Pos, new Vector2(.8f, .56f));
            if (Sprites.UseArt) m.Anim = CharacterAnimator.Attach(m.Sr, CharacterSet.Load(m.Def.id, 2.1f));
            if (!m.IsHuman) m.Ai = new MonsterAI(this, m);
            guest.Alive = false;
            guest.Asleep = false;
            if (guest.Sr != null) RemoveObject(guest.Sr.gameObject);
            Residents.Remove(guest);
            if (m.IsHuman) Human = null;
            var lair = m.Lair;
            for (int i = 0; i < lair.Slots.Length; i++)
            {
                var t = lair.Slots[i];
                if (t == null) continue;
                AddFloater(HotelMap.Center(t.Tile), "puff", (Color)Palette.Mint);
                if (t.Sr != null) RemoveObject(t.Sr.gameObject);
                lair.Slots[i] = null;
            }
            lair.DoorOpen = true; lair.CloseWhenClear = false; RefreshDoor(lair);
            Announce(guest.Char.name + " was the " + m.Def.name + "!", 4f);
            AddLog(guest.Char.name + " transformed. Room " + (lair.Def.Index + 1) + " is its lair.");
            AddFloater(m.Pos + Vector2.up, "REVEALED!", (Color)Palette.Candle);
        }
    }
}
