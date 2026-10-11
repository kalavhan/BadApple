using System.Collections.Generic;
using BadAppleHotel.Config;
using BadAppleHotel.Game;
using UnityEngine;
using NUnit.Framework;

namespace BadAppleHotel.Tests
{
    public class HauntedConfigTests
    {
        [TestCase(246f)] // 270px register, 12px padding on each side.
        [TestCase(216f)] // 240px compact register, same padding.
        public void Every_flavor_fits_two_lines_in_the_actual_register_font(float textWidth)
        {
            var style = HauntedSkin.Serif(13, true);
            Assert.AreEqual(Resources.Load<Font>("Fonts/CormorantGaramond-SemiBoldItalic"), style.font);
            Assert.IsNotNull(style.font, "The actual register font must be imported.");
            Assert.AreEqual(13, style.fontSize);
            var lines = new List<string>();
            foreach (var tower in ConfigLoader.Load().towers.towers)
            {
                HauntedSkin.WrapLines(tower.flavor, style, textWidth, lines);
                Assert.That(lines.Count, Is.InRange(1, 2), tower.id + " exceeds the two flavor baselines at " + textWidth + "px");
                Assert.AreEqual(tower.flavor, string.Join(" ", lines), tower.id + " must preserve every word");
                foreach (string line in lines)
                    Assert.LessOrEqual(HauntedSkin.Measure(line, style).x, textWidth + .01f,
                        tower.id + " has a clipped line at " + textWidth + "px: " + line);
            }
        }

        [Test]
        public void Every_tower_has_short_flavor_and_keeps_its_factual_description()
        {
            var towers = ConfigLoader.Load().towers.towers;
            Assert.IsNotEmpty(towers);
            foreach (var tower in towers)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(tower.flavor), tower.id + " needs register flavor");
                Assert.LessOrEqual(tower.flavor.Length, 60, tower.id + " must fit two register lines");
                Assert.IsFalse(string.IsNullOrWhiteSpace(tower.description), tower.id + " needs factual details");
                Assert.AreNotEqual(tower.description, tower.flavor, tower.id + " must keep separate flavor and facts");
            }
        }
    }
}
