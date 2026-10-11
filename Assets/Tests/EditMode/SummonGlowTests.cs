using BadAppleHotel.Game;
using NUnit.Framework;

namespace BadAppleHotel.Tests
{
    public class SummonGlowTests
    {
        static Ectoplasm.DripSnapshot State(Ectoplasm.DripState state, float progress) => new Ectoplasm.DripSnapshot
        { Enabled = true, State = state, Age = progress, Duration = 1 };

        [Test]
        public void The_glow_target_follows_refill_swell_neck_and_release()
        {
            var tuning = SummonGlowTuning.Default;
            float None = float.PositiveInfinity;
            Assert.AreEqual(.35f, SummonGlow.Target(true, State(Ectoplasm.DripState.Refill, 0), None, None, tuning), .0001f);
            Assert.AreEqual(.55f, SummonGlow.Target(true, State(Ectoplasm.DripState.Refill, 1), None, None, tuning), .0001f);
            Assert.AreEqual(.4375f, SummonGlow.Target(true, State(Ectoplasm.DripState.Refill, .25f), None, None, tuning), .0001f);
            Assert.AreEqual(.6625f, SummonGlow.Target(true, State(Ectoplasm.DripState.Swell, .5f), None, None, tuning), .0001f);
            Assert.AreEqual(1, SummonGlow.Target(true, State(Ectoplasm.DripState.Neck, .5f), None, None, tuning), .0001f);
            Assert.AreEqual(1.35f, SummonGlow.Target(true, State(Ectoplasm.DripState.Recoil, 0), None, None, tuning), .0001f);
            float settling = SummonGlow.Target(true, State(Ectoplasm.DripState.Recoil, .18f), None, None, tuning);
            Assert.That(settling, Is.InRange(.71f, .72f));
            Assert.AreEqual(0, SummonGlow.Target(false, State(Ectoplasm.DripState.Neck, .5f), 0, 0, tuning));
            Assert.AreEqual(tuning.Floor, SummonGlow.Target(true, default, None, None, tuning));
        }

        [Test]
        public void The_secondary_drop_and_press_add_decay_flashes_on_the_presentation_clock()
        {
            var t = SummonGlowTuning.Default;
            var neck = State(Ectoplasm.DripState.Neck, 0);
            Assert.AreEqual(1.15f, SummonGlow.Target(true, neck, 0, float.PositiveInfinity, t), .0001f);
            Assert.AreEqual(1.6f, SummonGlow.Target(true, neck, float.PositiveInfinity, 0, t), .0001f);
            Assert.AreEqual(1, SummonGlow.Target(true, neck, float.PositiveInfinity, float.PositiveInfinity, t), .0001f);
            Assert.Greater(SummonGlow.Target(true, neck, .01f, .01f, t), SummonGlow.Target(true, neck, .3f, .3f, t));
        }

        [Test]
        public void Glow_smoothing_is_frame_rate_independent_and_the_rim_stays_bounded()
        {
            var t = SummonGlowTuning.Default;
            float at30 = 0, at120 = 0;
            for (int i = 0; i < 30; i++) at30 = SummonGlow.Smooth(at30, 1.35f, 1f / 30, t);
            for (int i = 0; i < 120; i++) at120 = SummonGlow.Smooth(at120, 1.35f, 1f / 120, t);
            Assert.AreEqual(at30, at120, .00001f);
            Assert.Greater(SummonGlow.Smooth(.35f, 1, 1f / 60, t), .35f);
            Assert.Less(SummonGlow.Smooth(.35f, 1, 1f / 60, t), 1);
            Assert.AreEqual(.25f, SummonGlow.BezelAlpha(.35f, t));
            Assert.AreEqual(1, SummonGlow.BezelAlpha(1.6f, t));
            Assert.AreEqual(0, SummonGlow.Emphasis(t.Floor, t));
            Assert.AreEqual(1, SummonGlow.Emphasis(t.SwellPeak, t));
        }
    }
}
