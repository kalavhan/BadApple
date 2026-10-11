using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>Unscaled presentation time. Editor-only capture can advance one exact frame at a time;
    /// player builds always read Unity's real unscaled clock.</summary>
    public static class HauntedClock
    {
        public static float Now
        {
            get
            {
#if UNITY_EDITOR
                if (capturing) return captureTime;
#endif
                return Time.unscaledTime;
            }
        }
        public static float Delta
        {
            get
            {
#if UNITY_EDITOR
                if (capturing) return captureDelta;
#endif
                return Time.unscaledDeltaTime;
            }
        }
#if UNITY_EDITOR
        static bool capturing;
        static float captureTime, captureDelta;
        public static void BeginCapture(float start) { captureTime = start; captureDelta = 0; capturing = true; }
        public static void AdvanceCapture(float seconds) { captureDelta = seconds; captureTime += seconds; }
        public static void EndCapture() { capturing = false; captureDelta = 0; }
#endif
    }
}
