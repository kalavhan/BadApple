using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>The 9-color art palette from the game bible. Red is reserved for Faith.</summary>
    public static class Palette
    {
        public static readonly Color32 Ink = new Color32(0x1B, 0x16, 0x24, 255);
        public static readonly Color32 Plum = new Color32(0x3A, 0x2A, 0x4D, 255);
        public static readonly Color32 Teal = new Color32(0x3E, 0x6B, 0x6B, 255);
        public static readonly Color32 Moss = new Color32(0x6B, 0x7B, 0x3A, 255);
        public static readonly Color32 Rust = new Color32(0x8A, 0x4B, 0x2A, 255);
        public static readonly Color32 Bone = new Color32(0xE8, 0xDC, 0xC0, 255);
        public static readonly Color32 Candle = new Color32(0xF2, 0xC1, 0x4E, 255);
        public static readonly Color32 Mint = new Color32(0x9F, 0xE3, 0xC8, 255);
        public static readonly Color32 AppleRed = new Color32(0xD7, 0x26, 0x3D, 255);
        public static readonly Color32 Clear = new Color32(0, 0, 0, 0);

        public static Color32 Shade(Color32 c, float f)
        {
            return new Color32(
                (byte)Mathf.Clamp(c.r * f, 0, 255),
                (byte)Mathf.Clamp(c.g * f, 0, 255),
                (byte)Mathf.Clamp(c.b * f, 0, 255),
                c.a);
        }

        public static Color32 WithAlpha(Color32 c, byte a)
        {
            return new Color32(c.r, c.g, c.b, a);
        }
    }
}
