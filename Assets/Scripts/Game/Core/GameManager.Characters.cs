using UnityEngine;

namespace BadAppleHotel.Game
{
    public partial class GameManager
    {
        /// <summary>Standing height of a 3D resident in tiles; matches the sprite residents.</summary>
        const float ResidentModelHeight = 1.5f;
        CharacterKit characterKit;
        bool characterKitLoaded;

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
