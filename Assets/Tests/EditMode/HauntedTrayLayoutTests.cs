using NUnit.Framework;
using UnityEngine;
using BadAppleHotel.Game;

namespace BadAppleHotel.Tests
{
    public class HauntedTrayLayoutTests
    {
        [TestCase(1280, 922)]
        [TestCase(1600, 1040)]
        [TestCase(960, 936)]
        [TestCase(1000, 976)]
        [TestCase(1160, 802)]
        public void TrayUsesSafeBottomAndKeepsColumnsSeparate(float width, float expectedWidth)
        {
            var safe = new Rect(0, 0, width, 720);
            var r = HauntedTrayLayout.FrameIn(safe);
            Assert.That(r.width, Is.EqualTo(expectedWidth));
            Assert.That(r.height, Is.EqualTo(360));
            Assert.That(r.yMax, Is.EqualTo(708));
            var layout = new HauntedTrayLayout(r, 3);
            Assert.That(layout.Cabinet.xMax + 14, Is.EqualTo(layout.Detail.x).Within(.01f));
            Assert.That(layout.Detail.xMax + 14, Is.EqualTo(layout.Rail.x).Within(.01f));
            Assert.That(layout.Cubby(0).width, Is.GreaterThanOrEqualTo(90));
            Assert.That(layout.Cubby(layout.Slots - 1).xMax, Is.LessThan(layout.Detail.x));
            Assert.That(layout.Cancel.xMax, Is.LessThan(layout.Summon.x));
            Assert.That(layout.Summon.xMax, Is.LessThan(layout.Rail.x));
            Assert.That(HudLayout.Overlap(layout.Stamp, layout.Cancel), Is.Zero);
            Assert.That(HudLayout.Overlap(layout.Stamp, layout.Summon), Is.Zero);
            Assert.That(HudLayout.Overlap(layout.Stamp, layout.Detail), Is.Zero);
        }

        [Test]
        public void SafeAreaInsetsShiftWholeTrayWithoutCoveringControls()
        {
            var safe = new Rect(36, 20, 1520, 670);
            var r = HauntedTrayLayout.FrameIn(safe);
            Assert.That(r.xMin, Is.GreaterThanOrEqualTo(safe.xMin + 190));
            Assert.That(r.xMax, Is.LessThanOrEqualTo(safe.xMax - 168));
            Assert.That(r.yMax, Is.EqualTo(safe.yMax - 12));
        }

        [TestCase(0, 101)] [TestCase(11, 112)] [TestCase(12, 114)]
        public void CabinetSkipsRoom113(int index, int expected) => Assert.That(HauntedTrayLayout.PlateNumber(index), Is.EqualTo(expected));

        [TestCase(0, false, false, 1)] [TestCase(2, false, false, 3)]
        [TestCase(4, true, false, 4)] [TestCase(9, true, true, 6)]
        public void DialUsesUpcomingNightAndCapsEndless(int current, bool night, bool endless, int expected) =>
            Assert.That(HauntedTrayLayout.DialNight(current, 6, night, endless), Is.EqualTo(expected));
    }
}
