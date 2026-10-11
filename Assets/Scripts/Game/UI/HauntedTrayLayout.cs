using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>Canvas-space geometry shared by rendering, input and layout regression checks.</summary>
    public struct HauntedTrayLayout
    {
        public const float Height = 360f;
        public Rect Frame, Cabinet, Detail, Portrait, Rail, Cancel, Summon, Stamp, Lintel, LeftGate, RightGate;
        public DripAnchor LintelA, LintelB, LintelC;
        public float SpiderX;

        public readonly struct DripAnchor
        {
            public readonly Vector2 Root;
            public readonly float LandingY;
            public DripAnchor(Vector2 root, float landingY) { Root = root; LandingY = landingY; }
        }
        public int Slots;

        public static Rect FrameIn(Rect safe)
        {
            float left = safe.xMin + 190f, right = safe.xMax - 168f;
            // Three readable 97px cubbies plus the register and rail need at least 800px.
            if (right - left < 800f) { left = safe.xMin + 12f; right = safe.xMax - 12f; }
            float width = Mathf.Min(1040f, right - left);
            return new Rect((left + right - width) * .5f, safe.yMax - Height - 12f, width, Height);
        }

        public HauntedTrayLayout(Rect frame, int towerCount)
        {
            this = default;
            Frame = frame;
            float detailWidth = frame.width < 820f ? 240f : 270f;
            Rail = new Rect(frame.xMax - 118f, frame.y + 72f, 56f, 244f);
            Detail = new Rect(Rail.x - 14f - detailWidth, frame.y + 152f, detailWidth, 136f);
            Cabinet = new Rect(frame.x + 62f, frame.y + 72f, Detail.x - 14f - frame.x - 62f, 244f);
            // At narrow full-width fallbacks omit surplus empty cubbies before squeezing information.
            Slots = Cabinet.width < 420f ? Mathf.Max(1, towerCount) : 4;
            Stamp = new Rect(Cabinet.xMax - 110f, frame.yMax - 52f, 92f, 38f);
            Cancel = new Rect(Detail.x, frame.y + 294f, 92f, 50f);
            Summon = new Rect(Cancel.xMax + 10f, Cancel.y, detailWidth - 102f, 50f);
            Portrait = new Rect(Detail.center.x - 75f, frame.y + 58f, 150f, 96f);
            Lintel = new Rect(frame.x + 10f, frame.y + 10f, frame.width - 20f, 56f);
            LeftGate = new Rect(frame.x + 10f, Lintel.yMax, 40f, frame.height - 76f);
            RightGate = new Rect(frame.xMax - 50f, Lintel.yMax, 40f, frame.height - 76f);
            LintelA = new DripAnchor(new Vector2(Slots > 1 ? Cubby(0).xMax + 5f : Cabinet.x + 30f, Lintel.yMax), Cabinet.yMax - 8f);
            LintelB = new DripAnchor(new Vector2(Cabinet.xMax + 7f, Lintel.yMax), frame.yMax - 43f);
            LintelC = new DripAnchor(new Vector2(Detail.xMax + 7f, Lintel.yMax), frame.yMax - 43f);
            // The second cubby gap stays free of goo. With fewer than three slots, use a cabinet edge.
            SpiderX = Slots > 2 ? Cubby(1).xMax + 5f : Slots == 2 ? Cabinet.x + 7f : Cabinet.xMax - 30f;
        }

        public Rect Cubby(int index)
        {
            float width = (Cabinet.width - 24f - (Slots - 1) * 10f) / Slots;
            return new Rect(Cabinet.x + 12f + index * (width + 10f), Cabinet.y + 40f, width, 192f);
        }

        public static int PlateNumber(int zeroBasedIndex)
        {
            int plate = 101 + zeroBasedIndex;
            return plate >= 113 ? plate + 1 : plate;
        }

        public static int DialNight(int night, int nightCount, bool isNight, bool endless)
        {
            return Mathf.Clamp(isNight ? night : night + 1, 1, endless ? 6 : Mathf.Max(1, nightCount));
        }
    }
}
