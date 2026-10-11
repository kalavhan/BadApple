using BadAppleHotel.Config;
using NUnit.Framework;

namespace BadAppleHotel.Tests
{
    public class HauntedConfigTests
    {
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
