using BadAppleHotel.Config;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>Place the head, not the feet pivot, on the bed's pillow in every orientation.</summary>
    public static class SleepPose
    {
        public static Vector3 BedScale(Sprite sprite) => new Vector3(Mathf.Min(1f, 0.94f / sprite.bounds.size.x), Mathf.Min(1f, 1.9f / sprite.bounds.size.y), 1f);
        public static Vector2 Position(Vector2 bedCenter, float bedRotation, BedLevel bed, Vector2 headLocal, Vector2? bedScale = null)
        {
            var bedTurn = Quaternion.Euler(0, 0, bedRotation);
            var bodyTurn = Quaternion.Euler(0, 0, bedRotation + bed.sleepRotation);
            return bedCenter + (Vector2)(bedTurn * Vector2.Scale(bed.sleepAnchor,bedScale ?? Vector2.one)) - (Vector2)(bodyTurn * headLocal);
        }
    }
}
