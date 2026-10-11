using System;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>Canvas-pixel, unscaled-time settings for the haunted tray's viscous drips.</summary>
    [Serializable]
    public struct EctoplasmTuning
    {
        [Header("Timing (seconds)")]
        public Vector2 RefillDuration, SwellDuration, NeckDuration;
        public float RecoilDuration, ReleaseGap, RetractDuration, PressNeckDuration;
        [Header("Shape (canvas pixels)")]
        public float RestNeck, SwellNeck, ReleaseNeck, RestBulb, MaximumBulb;
        public float RefillLength, SwellLength, NeckStretch, BulbWidth, Sway, SwaySpeed;
        [Header("Spring")]
        public float SpringFrequency, SpringDamping;
        [Header("Droplets")]
        public float Gravity, TerminalSpeed, InitialSpeed, SatelliteChance, SatelliteDelay, SatelliteRadius, FadeDistance;
        [Header("Landing")]
        public float SquashDuration, PuddleDuration, PuddleWidth, PuddleHeight, PuddleSpread, FlashDuration;
        public float SpeckDuration;
        public Vector2 SpeckSpeed, SpeckRadius;
        public float FeedLength;

        public static EctoplasmTuning Default => new EctoplasmTuning
        {
            RefillDuration = new Vector2(1.6f, 4f), SwellDuration = new Vector2(1.1f, 2f), NeckDuration = new Vector2(.16f, .24f),
            RecoilDuration = .35f, ReleaseGap = .4f, RetractDuration = .5f, PressNeckDuration = .05f,
            RestNeck = 1.6f, SwellNeck = .9f, ReleaseNeck = .4f, RestBulb = 1.5f, MaximumBulb = 6f,
            RefillLength = .45f, SwellLength = 2.1f, NeckStretch = 1.08f, BulbWidth = .22f, Sway = .6f, SwaySpeed = 1.7f,
            SpringFrequency = 18f, SpringDamping = .35f,
            Gravity = 900f, TerminalSpeed = 650f, InitialSpeed = 20f, SatelliteChance = .2f, SatelliteDelay = .06f,
            SatelliteRadius = .35f, FadeDistance = 30f,
            SquashDuration = .07f, PuddleDuration = 1f, PuddleWidth = 2.2f, PuddleHeight = .35f, PuddleSpread = 1.3f,
            FlashDuration = .1f, SpeckDuration = .35f, SpeckSpeed = new Vector2(60f, 140f), SpeckRadius = new Vector2(1f, 1.8f),
            FeedLength = .25f
        };
    }
}
