using System.Collections.Generic;
using BadAppleHotel.Game;
using NUnit.Framework;
using UnityEngine;

namespace BadAppleHotel.Tests
{
    public class EctoplasmTests
    {
        static Ectoplasm Tray(int seed = 73)
        {
            var goo = new Ectoplasm();
            goo.Open(seed);
            goo.SetAnchor(Ectoplasm.LintelA, new Vector2(230, 66), 26, 16, 317, true);
            goo.SetAnchor(Ectoplasm.LintelB, new Vector2(600, 66), 18, 22, 317, true);
            goo.SetAnchor(Ectoplasm.LintelC, new Vector2(746, 66), 14, 12, 294, true);
            goo.SetAnchor(Ectoplasm.Summon1, new Vector2(766, 344), 16, 10, 390, true);
            goo.SetAnchor(Ectoplasm.Summon2, new Vector2(800, 344), 11, 7, 390, true);
            return goo;
        }

        static void Run(Ectoplasm goo, float seconds, int fps = 120)
        {
            for (int frame = 0; frame < Mathf.RoundToInt(seconds * fps); frame++) goo.Advance(1f / fps);
        }

        [Test]
        public void Goo_body_shader_is_packaged_as_a_resource_for_player_builds()
        {
            var shader = Resources.Load<Shader>("Shaders/UiAlpha");
            Assert.IsNotNull(shader);
            Assert.AreEqual("Hidden/BadAppleHotel/UiAlpha", shader.name);
            Assert.IsFalse(UnityEditor.ShaderUtil.ShaderHasError(shader));
        }

        [Test]
        public void Fifteen_seconds_show_independent_cycles_with_at_least_three_staggered_releases()
        {
            using (var goo = Tray())
            {
                var releases = new List<float>();
                var anchors = new HashSet<string>();
                bool sawNeck = false, sawSwell = false, sawRecoil = false;
                goo.Released += (id, time, forced) => { releases.Add(time); anchors.Add(id); Assert.IsFalse(forced); };
                for (int i = 0; i < 15 * 120; i++)
                {
                    goo.Advance(Ectoplasm.FixedStep);
                    goo.TryGetDrip(Ectoplasm.LintelA, out var drip);
                    sawSwell |= drip.State == Ectoplasm.DripState.Swell && drip.Length > 16;
                    sawNeck |= drip.State == Ectoplasm.DripState.Neck && drip.Neck < .6f;
                    sawRecoil |= drip.State == Ectoplasm.DripState.Recoil && drip.Length < 16 * .45f;
                    Assert.Less(goo.GeometryVertexCount, 600);
                }
                Assert.GreaterOrEqual(releases.Count, 3);
                Assert.GreaterOrEqual(anchors.Count, 3);
                for (int i = 1; i < releases.Count; i++) Assert.GreaterOrEqual(releases[i] - releases[i - 1], .4f - .0001f);
                Assert.IsTrue(sawSwell, "The stem grows visibly before detaching.");
                Assert.IsTrue(sawNeck, "The neck visibly thins to a thread.");
                Assert.IsTrue(sawRecoil, "The stem overshoots its refill length.");
            }
        }

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(144)]
        public void Frame_rate_does_not_change_physics_or_release_order(int fps)
        {
            using (var reference = Tray(81))
            using (var candidate = Tray(81))
            {
                var expected = new List<string>();
                var actual = new List<string>();
                reference.Released += (id, time, forced) => expected.Add(id + ":" + time.ToString("R"));
                candidate.Released += (id, time, forced) => actual.Add(id + ":" + time.ToString("R"));
                Run(reference, 12, 120); Run(candidate, 12, fps);
                CollectionAssert.AreEqual(expected, actual);
                Assert.AreEqual(reference.ImpactCount, candidate.ImpactCount);
                Assert.AreEqual(reference.DropletCount, candidate.DropletCount);
                reference.TryGetDrip(Ectoplasm.LintelB, out var expectedDrip);
                candidate.TryGetDrip(Ectoplasm.LintelB, out var actualDrip);
                Assert.AreEqual(expectedDrip.Length, actualDrip.Length, .0001f);
                Assert.AreEqual(expectedDrip.State, actualDrip.State);
            }
        }

        [Test]
        public void Press_releases_both_summon_drips_together_and_drops_accelerate_then_fade_without_splats()
        {
            using (var goo = new Ectoplasm())
            {
                goo.Tuning.SatelliteChance = 0;
                goo.Open(19);
                goo.SetAnchor(Ectoplasm.Summon1, new Vector2(100, 100), 16, 10, 220, true);
                goo.SetAnchor(Ectoplasm.Summon2, new Vector2(134, 100), 11, 7, 220, true);
                var times = new List<float>();
                goo.Released += (id, time, forced) => { Assert.IsTrue(forced); times.Add(time); };
                goo.PressSummon(); Run(goo, .1f);
                Assert.AreEqual(2, times.Count);
                Assert.AreEqual(times[0], times[1]);
                Assert.AreEqual(2, goo.DropletCount);
                var before = goo.GetDroplet(0);
                Run(goo, .1f);
                var after = goo.GetDroplet(0);
                Assert.Greater(after.Position.y, before.Position.y);
                Assert.Greater(after.Velocity.y, before.Velocity.y);
                Assert.LessOrEqual(after.Velocity.y, goo.Tuning.TerminalSpeed);
                goo.BeginClose(); Run(goo, 2);
                Assert.AreEqual(0, goo.DropletCount);
                Assert.AreEqual(0, goo.ImpactCount, "Summon drops fade past the tray instead of landing.");
            }
        }

        [Test]
        public void Dark_summon_retracts_smoothly_pauses_and_refills_when_lit_again()
        {
            using (var goo = new Ectoplasm())
            {
                goo.Open(91);
                Vector2 root = new Vector2(30, 100);
                goo.SetAnchor(Ectoplasm.Summon1, root, 16, 10, 220, true);
                goo.TryGetDrip(Ectoplasm.Summon1, out var lit);
                goo.SetAnchor(Ectoplasm.Summon1, root, 16, 10, 220, false);
                goo.TryGetDrip(Ectoplasm.Summon1, out var justDark);
                Assert.AreEqual(lit.Length, justDark.Length, "Changing affordability must not snap the goo away.");
                Run(goo, .25f);
                goo.TryGetDrip(Ectoplasm.Summon1, out var halfway);
                Assert.Greater(halfway.Length, 0);
                Assert.Less(halfway.Length, lit.Length);
                Run(goo, .35f);
                goo.TryGetDrip(Ectoplasm.Summon1, out var dark);
                Assert.AreEqual(0, dark.Length);
                Assert.AreEqual(Ectoplasm.DripState.Paused, dark.State);
                int count = goo.ReleaseCount;
                Run(goo, 3);
                Assert.AreEqual(count, goo.ReleaseCount);
                goo.SetAnchor(Ectoplasm.Summon1, root, 16, 10, 220, true);
                goo.TryGetDrip(Ectoplasm.Summon1, out var relit);
                Assert.AreEqual(0, relit.Length);
                Assert.AreEqual(Ectoplasm.DripState.Refill, relit.State);
                Run(goo, .1f);
                goo.TryGetDrip(Ectoplasm.Summon1, out relit);
                Assert.Greater(relit.Length, 0);
            }
        }

        [Test]
        public void Closing_stops_releases_but_existing_drops_squash_splash_and_finish_their_puddles()
        {
            using (var goo = new Ectoplasm())
            {
                goo.Tuning.SatelliteChance = 0;
                goo.Open(5);
                goo.SetAnchor(Ectoplasm.LintelA, new Vector2(30, 40), 26, 16, 190, true);
                for (int i = 0; i < 10 * 120 && goo.DropletCount == 0; i++) goo.Advance(Ectoplasm.FixedStep);
                Assert.Greater(goo.DropletCount, 0);
                int releases = goo.ReleaseCount;
                goo.BeginClose();
                bool squashed = false, puddle = false, specks = false;
                for (int i = 0; i < 3 * 120; i++)
                {
                    goo.Advance(Ectoplasm.FixedStep);
                    for (int d = 0; d < goo.DropletCount; d++) squashed |= goo.GetDroplet(d).Squashing;
                    puddle |= goo.PuddleCount > 0; specks |= goo.SpeckCount > 0;
                }
                Assert.AreEqual(releases, goo.ReleaseCount);
                Assert.AreEqual(1, goo.ImpactCount);
                Assert.IsTrue(squashed); Assert.IsTrue(puddle); Assert.IsTrue(specks);
                Assert.AreEqual(0, goo.DropletCount); Assert.AreEqual(0, goo.PuddleCount); Assert.AreEqual(0, goo.SpeckCount);
                goo.Clear();
                Assert.AreEqual(0, goo.DripCount); Assert.IsFalse(goo.IsOpen);
            }
        }

        [Test]
        public void Anchor_updates_follow_the_slide_without_advancing_or_duplicating_particles()
        {
            using (var goo = new Ectoplasm())
            {
                goo.Tuning.SatelliteChance = 0;
                goo.Open(8);
                goo.SetAnchor(Ectoplasm.Summon1, new Vector2(30, 100), 16, 10, 300, true);
                goo.PressSummon(); Run(goo, .1f);
                var drop = goo.GetDroplet(0);
                float time = goo.SimulationTime;
                goo.SetAnchor(Ectoplasm.Summon1, new Vector2(70, 150), 16, 10, 350, true);
                goo.SetAnchor(Ectoplasm.Summon1, new Vector2(70, 150), 16, 10, 350, true);
                Assert.AreEqual(time, goo.SimulationTime);
                Assert.AreEqual(1, goo.DropletCount);
                Assert.AreEqual(drop.Position + new Vector2(40, 50), goo.GetDroplet(0).Position);
                Assert.AreEqual(drop.Velocity, goo.GetDroplet(0).Velocity);
            }
        }

        [Test]
        public void A_lintel_C_impact_feeds_the_lit_button_and_swells_the_nearer_drip()
        {
            using (var goo = new Ectoplasm())
            {
                goo.Tuning.SatelliteChance = 0;
                goo.Open(28);
                goo.SetAnchor(Ectoplasm.LintelC, new Vector2(80, 40), 14, 12, 180, true);
                for (int i = 0; i < 10 * 120 && goo.DropletCount == 0; i++) goo.Advance(Ectoplasm.FixedStep);
                Assert.AreEqual(1, goo.DropletCount);
                goo.SetAnchor(Ectoplasm.Summon1, new Vector2(84, 230), 16, 10, 300, false);
                goo.SetAnchor(Ectoplasm.Summon2, new Vector2(120, 230), 11, 7, 300, false);
                goo.SetAnchor(Ectoplasm.Summon1, new Vector2(84, 230), 16, 10, 300, true);
                goo.SetAnchor(Ectoplasm.Summon2, new Vector2(120, 230), 11, 7, 300, true);
                Ectoplasm.DripSnapshot before1 = default, before2 = default;
                for (int i = 0; i < 120 && goo.ImpactCount == 0; i++)
                {
                    goo.TryGetDrip(Ectoplasm.Summon1, out before1);
                    goo.TryGetDrip(Ectoplasm.Summon2, out before2);
                    goo.Advance(Ectoplasm.FixedStep);
                }
                Assert.AreEqual(1, goo.ImpactCount);
                goo.TryGetDrip(Ectoplasm.Summon1, out var after1); goo.TryGetDrip(Ectoplasm.Summon2, out var after2);
                Assert.AreEqual(Ectoplasm.DripState.Swell, after1.State);
                Assert.GreaterOrEqual(after1.Length, before1.Length * 1.25f);
                Assert.GreaterOrEqual(after2.Length, before2.Length * 1.25f);
            }
        }

        [Test]
        public void Repeated_presses_stay_within_particle_and_geometry_budgets()
        {
            using (var goo = Tray(100))
            {
                goo.Tuning.SatelliteChance = 1;
                for (int i = 0; i < 30 * 120; i++)
                {
                    if (i % 8 == 0) goo.PressSummon();
                    goo.Advance(Ectoplasm.FixedStep);
                    Assert.LessOrEqual(goo.DripCount, Ectoplasm.MaxDrips);
                    Assert.LessOrEqual(goo.DropletCount, Ectoplasm.MaxDroplets);
                    Assert.LessOrEqual(goo.PuddleCount, Ectoplasm.MaxPuddles);
                    Assert.LessOrEqual(goo.SpeckCount, Ectoplasm.MaxSpecks);
                    Assert.Less(goo.GeometryVertexCount, 600);
                }
            }
        }
    }
}
