using System.Collections.Generic;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// Keyboard state read from IMGUI events (works with either input backend), plus the on-screen joystick vector the
    /// HUD writes. Keyboard: WASD / arrows to move, E or Space for the action button, M for the hotel view,
    /// 1-2-3 for monster abilities.
    /// </summary>
    public static class GameInput
    {
        static readonly HashSet<KeyCode> held = new HashSet<KeyCode>();
        static readonly HashSet<KeyCode> pressed = new HashSet<KeyCode>();
        public static Vector2 Joystick;

        public static void Handle(Event e)
        {
            if (e == null) return;
            if (e.type == EventType.KeyDown && e.keyCode != KeyCode.None)
            {
                if (held.Add(e.keyCode)) pressed.Add(e.keyCode);
            }
            else if (e.type == EventType.KeyUp && e.keyCode != KeyCode.None)
            {
                held.Remove(e.keyCode);
            }
        }

        public static bool Held(KeyCode k) => held.Contains(k);

        public static bool ConsumePressed(KeyCode k) => pressed.Remove(k);

        public static void ClearAll()
        {
            held.Clear();
            pressed.Clear();
            Joystick = Vector2.zero;
        }

        public static Vector2 Move
        {
            get
            {
                float x = 0f, y = 0f;
                if (Held(KeyCode.A) || Held(KeyCode.LeftArrow)) x -= 1f;
                if (Held(KeyCode.D) || Held(KeyCode.RightArrow)) x += 1f;
                if (Held(KeyCode.S) || Held(KeyCode.DownArrow)) y -= 1f;
                if (Held(KeyCode.W) || Held(KeyCode.UpArrow)) y += 1f;
                var v = new Vector2(x, y);
                if (Joystick.sqrMagnitude > 0.01f) v = Joystick;
                return Vector2.ClampMagnitude(v, 1f);
            }
        }
    }
}
