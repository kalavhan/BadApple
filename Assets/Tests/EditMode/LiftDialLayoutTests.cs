using NUnit.Framework;
using UnityEngine;
using BadAppleHotel.Game;

namespace BadAppleHotel.Tests
{
    public class LiftDialLayoutTests
    {
        [TestCase(960, false)] [TestCase(1160, true)] [TestCase(1280, true)] [TestCase(1600, true)]
        public void IndicatorClearsTopControlsAndResources(float width, bool wings)
        {
            var d = new LiftDialLayout(width, 0, LiftDialLayout.ShowWings(width));
            Assert.That(d.Wings, Is.EqualTo(wings));
            var controls = new Rect(10, 10, 336, 30);
            var log = new Rect(10, 48, 300, 100);
            var resources = new Rect(width - 270, 8, 260, 80);
            foreach (Rect part in new[] { d.HousingArt, d.Interaction, d.Plate(240) })
            {
                Assert.That(HudLayout.Overlap(part, controls), Is.Zero);
                Assert.That(HudLayout.Overlap(part, log), Is.Zero);
                Assert.That(HudLayout.Overlap(part, resources), Is.Zero);
            }
            if (d.Wings)
                foreach (Rect wing in new[] { d.LeftWing, d.RightWing })
                {
                    Assert.That(HudLayout.Overlap(wing, controls), Is.Zero);
                    Assert.That(HudLayout.Overlap(wing, log), Is.Zero);
                    Assert.That(HudLayout.Overlap(wing, resources), Is.Zero);
                }
            Assert.That(d.HousingArt.y, Is.Zero);
            Assert.That(d.Face.yMax, Is.EqualTo(d.Pivot.y));
            Assert.That(d.Sill.y, Is.EqualTo(d.Pivot.y));
            Assert.That(d.Plate(20).width, Is.EqualTo(72));
            Assert.That(d.Plate(240).yMax, Is.EqualTo(120));
        }

        [Test]
        public void SafeTopMovesEveryPartAsOneAssembly()
        {
            var d = new LiftDialLayout(1280, 24, true);
            Assert.That(d.Pivot, Is.EqualTo(new Vector2(640, 108)));
            Assert.That(d.Housing.y, Is.EqualTo(24));
            Assert.That(d.Interaction.y, Is.EqualTo(24));
            Assert.That(d.Plate(100).yMax, Is.EqualTo(144));
            Assert.That(d.Hub.center, Is.EqualTo(d.Pivot));
        }

        [TestCase(1, 6, -62)] [TestCase(2, 6, -37.2f)] [TestCase(6, 6, 62)] [TestCase(1, 1, 0)]
        public void NumeralsFollowTheSpecifiedArc(int index, int count, float angle)
        {
            Assert.That(LiftDialLayout.NumeralAngle(index, count), Is.EqualTo(angle).Within(.001f));
            var d = new LiftDialLayout(1280, 0, true);
            Assert.That(Vector2.Distance(d.Pivot, d.NumeralPosition(index, count)), Is.EqualTo(55).Within(.001f));
        }

        [TestCase(1103, false)] [TestCase(1104, true)]
        public void WingsUseTheWallsButtonClearanceThreshold(float width, bool expected) =>
            Assert.That(LiftDialLayout.ShowWings(width), Is.EqualTo(expected));
    }
}
