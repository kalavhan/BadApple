using BadAppleHotel.Config;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>Place the head, not the feet pivot, on the bed's pillow in every orientation.</summary>
    public static class SleepPose
    {
        public static Vector2 Position(Vector2 bedCenter, float bedRotation, BedLevel bed, Vector2 headLocal)
        {
            var bedTurn = Quaternion.Euler(0, 0, bedRotation);
            var bodyTurn = Quaternion.Euler(0, 0, bedRotation + bed.sleepRotation);
            return bedCenter + (Vector2)(bedTurn * bed.sleepAnchor) - (Vector2)(bodyTurn * headLocal);
        }
    }
}
