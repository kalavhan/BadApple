using System;
using UnityEngine;

namespace BadAppleHotel.Game
{
    [Serializable]
    public struct SummonGlowTuning
    {
        [Header("Drip-driven pulse")]
        public float Floor, RefillPeak, SwellPeak, RecoilFlash, RecoilTau, Summon2Flash;
        public float PressFlash, PressTau, SmoothTau;
        [Header("Halos")]
        public float TightAlpha, WideAlpha, TightSpread, WideSpread;
        [Header("Lit bezel and face")]
        public float BezelFloor, BezelPeak, BezelSpread;
        public int BezelBorder;
        public float FaceAlpha, FaceWidth, FaceHeight;

        public static SummonGlowTuning Default => new SummonGlowTuning
        {
            Floor = .35f, RefillPeak = .55f, SwellPeak = 1f, RecoilFlash = 1.35f, RecoilTau = .18f, Summon2Flash = .15f,
            PressFlash = 1.6f, PressTau = .2f, SmoothTau = .08f,
            TightAlpha = .55f, WideAlpha = .35f, TightSpread = 24f, WideSpread = 48f,
            BezelFloor = .25f, BezelPeak = 1f, BezelSpread = 6f, BezelBorder = 18,
            FaceAlpha = .25f, FaceWidth = .9f, FaceHeight = 1.6f
        };
    }

    /// <summary>Pure pulse maths. Flash ages use the presentation clock; positive infinity disables a flash.</summary>
    public static class SummonGlow
    {
        public static float Target(bool lit, Ectoplasm.DripSnapshot primary, float secondaryReleaseAge, float pressAge, SummonGlowTuning tuning)
        {
            if (!lit) return 0;
            float glow = tuning.Floor;
            if (primary.Enabled)
            {
                float u = Mathf.Clamp01(primary.Age / Mathf.Max(.0001f, primary.Duration));
                switch (primary.State)
                {
                    case Ectoplasm.DripState.Refill: glow = Mathf.Lerp(tuning.Floor, tuning.RefillPeak, 1 - (1 - u) * (1 - u)); break;
                    case Ectoplasm.DripState.Swell: glow = Mathf.Lerp(tuning.RefillPeak, tuning.SwellPeak, u * u); break;
                    case Ectoplasm.DripState.Neck: glow = tuning.SwellPeak; break;
                    case Ectoplasm.DripState.Recoil: glow = tuning.Floor + (tuning.RecoilFlash - tuning.Floor) * Decay(primary.Age, tuning.RecoilTau); break;
                }
            }
            glow += tuning.Summon2Flash * Decay(secondaryReleaseAge, tuning.RecoilTau);
            glow += (tuning.PressFlash - tuning.SwellPeak) * Decay(pressAge, tuning.PressTau);
            return Mathf.Max(0, glow);
        }

        public static float Smooth(float current, float target, float dt, SummonGlowTuning tuning)
        {
            if (dt <= 0) return current;
            return Mathf.Lerp(current, target, 1 - Mathf.Exp(-dt / Mathf.Max(.0001f, tuning.SmoothTau)));
        }
        public static float Emphasis(float level, SummonGlowTuning tuning) => Mathf.Clamp01((level - tuning.Floor) / Mathf.Max(.0001f, tuning.SwellPeak - tuning.Floor));
        public static float BezelAlpha(float level, SummonGlowTuning tuning) => Mathf.Clamp01(Mathf.Lerp(tuning.BezelFloor, tuning.BezelPeak, Emphasis(level, tuning)));
        static float Decay(float age, float tau) => age < 0 || float.IsInfinity(age) ? 0 : Mathf.Exp(-age / Mathf.Max(.0001f, tau));
    }
}
