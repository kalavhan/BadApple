using UnityEngine;

namespace BadAppleHotel.Game
{
    public partial class GameManager
    {
        /// <summary>Standing height of a 3D resident in tiles; matches the sprite residents.</summary>
        const float ResidentModelHeight = 1.5f;
        CharacterKit characterKit;
        bool characterKitLoaded;

        CharacterKit monsterKit;
        bool monsterKitLoaded;

        /// <summary>Standing height in tiles of each monster's 3D model (residents are 1.5).</summary>
        static float MonsterModelHeight(string id) => id == "bellhop_wraith" ? 2.05f : id == "moldy_matron" ? 2.0f : 1.85f;

        CharacterModel MonsterModel(Monster m)
        {
            if (Simulation) return null;
            if (!monsterKitLoaded) { monsterKit = Resources.Load<CharacterKit>("Art3D/Monsters/HotelMonsterKit"); monsterKitLoaded = true; }
            var model = monsterKit != null ? monsterKit.Get(m.Def.id) : null;
            if (model == null || model.Rig == null) return null;
            var view = CharacterModel.Create(m.Sr, model, MonsterModelHeight(m.Def.id), matchRoot);
            // Dark goth costumes vanish in the night hallway; lift them a little so the silhouette reads.
            view.Brighten(1.3f);
            view.Glide = m.Def.id == "moldy_matron";
            if (m.Def.id == "bellhop_wraith") { view.Glide = true; view.Hover = .12f; }
            return view;
        }

        void PlaceMonsterModel(Monster m, bool moving)
        {
            var view = m.Model;
            view.SetSize(1f + GrowthStage(m) * 0.06f);
            if (m.ActionPending != null) { view.PlayAction(m.ActionPending); m.ActionPending = null; }
            if (m.EatingPart != null) view.PlayAction("Eat");
            else if (m.Biting == null && m.AttackingRoom == null) view.StopAction();
            float speed = moving ? MonsterSpeed(m, Now) : 0f;
            view.Drive(m.Pos, m.Facing, speed, false, m.HitPending);
            m.HitPending = false;
        }

        CharacterModel ResidentModel(Resident r)
        {
            if (Simulation || r.Char == null) return null;
            if (!characterKitLoaded) { characterKit = CharacterKit.Load(); characterKitLoaded = true; }
            var model = characterKit != null ? characterKit.Get(r.Char.art) : null;
            return model == null ? null : CharacterModel.Create(r.Sr, model, ResidentModelHeight, matchRoot);
        }

        void PlaceResidentModel(Resident r)
        {
            if (r.Asleep && r.Room != null)
            {
                var room = r.Room;
                var bed = Cfg.beds.levels[room.BedLevel - 1];
                var bedTurn = Quaternion.Euler(0f, 0f, room.Def.BedRotation);
                var pillow = room.Def.BedCenter + (Vector2)(bedTurn * Vector2.Scale(bed.sleepAnchor, room.BedSr.transform.localScale));
                var toward = (Vector2)(Quaternion.Euler(0f, 0f, room.Def.BedRotation + bed.sleepRotation) * Vector2.up);
                r.Model.Lie(pillow, toward, SleepZ(room), r.SleepBlend);
                return;
            }
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float speed = (r.Pos - r.LastPos).magnitude / dt;
            // Once the monster is out, residents flee with the scared run.
            bool scared = Monster != null && !Monster.Dead;
            r.Model.Drive(r.Pos, r.Facing, speed, scared, Now < r.AttackUntil);
        }
    }
}
