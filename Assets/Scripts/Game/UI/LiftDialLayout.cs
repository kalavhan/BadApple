using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>Geometry of the permanent top-mounted lift indicator, in the 720-high HUD canvas.</summary>
    public readonly struct LiftDialLayout
    {
        public readonly Vector2 Pivot;
        public readonly Rect Housing, HousingArt, Face, Sill, Hub, Needle, LeftWing, RightWing, Interaction;
        public readonly bool Wings;

        public LiftDialLayout(float canvasWidth, float top, bool wings)
        {
            Pivot = new Vector2(canvasWidth * .5f, top + 84);
            Housing = new Rect(Pivot.x - 96, top, 192, 92);
            // The 400x192 art includes the wider 200px sill and four transparent pixels below it.
            HousingArt = new Rect(Pivot.x - 100, top, 200, 96);
            Face = new Rect(Pivot.x - 72, Pivot.y - 72, 144, 72);
            Sill = new Rect(Pivot.x - 100, Pivot.y, 200, 8);
            Hub = new Rect(Pivot.x - 11, Pivot.y - 12, 22, 24);
            Needle = new Rect(Pivot.x - 6, Pivot.y - 50, 12, 50);
            LeftWing = new Rect(Pivot.x - 184, Pivot.y - 30, 96, 40);
            RightWing = new Rect(Pivot.x + 88, Pivot.y - 30, 96, 40);
            Interaction = new Rect(Pivot.x - 100, top, 200, 120);
            Wings = wings;
        }

        public static bool ShowWings(float canvasWidth) => canvasWidth * .5f - 196 >= 356;
        public Rect Plate(float width) => new Rect(Pivot.x - Mathf.Max(72, width) * .5f, Pivot.y + 14, Mathf.Max(72, width), 22);
        public static float NumeralAngle(int oneBasedIndex, int count) => count <= 1 ? 0 : -62 + (oneBasedIndex - 1) * 124f / (count - 1);
        public Vector2 NumeralPosition(int oneBasedIndex, int count)
        {
            float angle = NumeralAngle(oneBasedIndex, count) * Mathf.Deg2Rad;
            return Pivot + new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * 55;
        }
    }
}
