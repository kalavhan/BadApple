using System.Collections;
using System.Linq;
using System.Reflection;
using BadAppleHotel.Config;
using BadAppleHotel.Game;
using NUnit.Framework;
using UnityEngine;

namespace BadAppleHotel.Tests
{
    public class TowerFxTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test] public void Each_primitive_is_one_quad_and_a_path_is_one_quad_per_segment()
        {
            var fx = new DreamFx(null);
            fx.Billboard(Vector3.zero, .1f, .1f, DreamFx.Shape.Glow, Color.white);
            fx.Decal(Vector2.one, .4f, DreamFx.Shape.Sigil, Color.white);
            fx.Column(Vector2.one, .2f, 1.5f, DreamFx.Shape.Pillar, Color.white);
            Assert.AreEqual(3, fx.QuadCount);
            fx.Path(new[] { Vector3.zero, Vector3.right, new Vector3(2, 1, -.5f), new Vector3(3, 1, -.5f) }, .05f, DreamFx.Shape.Thread, Color.white);
            Assert.AreEqual(6, fx.QuadCount);
            fx.Band(Vector3.one, Vector3.one, .05f, DreamFx.Shape.Bolt, Color.white);
            Assert.AreEqual(6, fx.QuadCount, "A zero-length band draws nothing.");
            fx.Flush(0);
            Assert.AreEqual(0, fx.QuadCount, "Immediate primitives last one frame.");
            fx.Dispose();
        }

        [Test] public void Particles_age_out_wait_for_delayed_starts_and_are_capped()
        {
            var fx = new DreamFx(null);
            fx.Emit(DreamFx.Shape.Glow, Vector3.zero, Vector3.right, .1f, .5f, Color.white);
            fx.Emit(new DreamFx.Particle { Shape = DreamFx.Shape.Ring, Size = 1, Life = .5f, Age = -.4f, Color = Color.white, Ground = true });
            fx.Step(.3f);
            Assert.AreEqual(2, fx.ParticleCount);
            fx.Step(.3f);
            Assert.AreEqual(1, fx.ParticleCount, "The glow expired; the delayed ring has only just started.");
            fx.Step(.3f);
            Assert.AreEqual(0, fx.ParticleCount);
            for (int i = 0; i < DreamFx.MaxParticles + 50; i++) fx.Emit(DreamFx.Shape.Glow, Vector3.zero, Vector3.zero, .1f, 1, Color.white);
            Assert.AreEqual(DreamFx.MaxParticles, fx.ParticleCount);
            fx.Dispose();
        }

        [Test] public void Only_the_strongest_lights_reach_the_shaders_and_flashes_fade_out()
        {
            var fx = new DreamFx(null);
            for (int i = 0; i < DreamFx.MaxLights + 4; i++) fx.Light(new Vector2(i, 0), 1 + i, Color.white);
            fx.Flush(0);
            Assert.AreEqual(DreamFx.MaxLights, fx.LightCount);
            fx.FlashLight(Vector2.zero, 2, Color.white, .5f);
            fx.Step(.1f); fx.Flush(.1f);
            Assert.AreEqual(1, fx.LightCount);
            fx.Step(.5f); fx.Flush(.6f);
            Assert.AreEqual(0, fx.LightCount, "An expired flash no longer lights anything.");
            fx.Dispose();
        }

        [Test] public void Animated_tower_art_loads_in_one_or_eight_views_and_falls_back_when_missing()
        {
            var art = Sprites.ArtOverride;
            Sprites.ArtOverride = true;
            try
            {
                var soldier = TowerSpriteSet.Load("gun_turret", 1);
                Assert.IsNotNull(soldier);
                Assert.IsTrue(soldier.Directional);
                Assert.IsTrue(soldier.HasFire);
                Assert.AreNotSame(soldier.Idle(0, 0), soldier.Idle(4, 0), "East and west are different views.");
                Assert.IsNotNull(soldier.Fire(6, soldier.Release));
                Assert.IsNull(soldier.Fire(6, soldier.FireDuration + .01f), "The attack clip ends.");
                var bounds = soldier.Idle(2, 0).bounds;
                Assert.AreEqual(bounds.size, soldier.Fire(5, .3f).bounds.size, "Every frame shares one cell.");
                Assert.AreEqual(0f, bounds.min.y, 1e-4f, "The feet sit on the pivot.");

                var hourglass = TowerSpriteSet.Load("slow_totem", 1);
                Assert.IsNotNull(hourglass);
                Assert.IsFalse(hourglass.Directional);
                Assert.IsFalse(hourglass.HasFire);
                Assert.AreSame(hourglass.Idle(0, .3f), hourglass.Idle(5, .3f), "A single view ignores facing.");

                Assert.IsNull(TowerSpriteSet.Load("tesla_coil", 4), "Forms without animation keep their static art.");
            }
            finally { Sprites.ArtOverride = art; }
        }

        [Test] public void Every_element_has_its_own_accent()
        {
            var towers = ConfigLoader.Load().towers.towers;
            var accents = towers.GroupBy(t => t.damageType == "none" ? t.effect : t.damageType)
                .Select(g => GameManager.TowerAccent(g.First())).ToList();
            Assert.AreEqual(accents.Count, accents.Distinct().Count());
        }

        [Test] public void The_bot_simulation_never_builds_effects()
        {
            var game = new GameObject("Simulation without effects").AddComponent<GameManager>();
            try
            {
                game.StartSimulation(ConfigLoader.Load(), 7, false);
                for (int i = 0; i < 600 && game.InMatch; i++) game.StepMatch(.1f);
                typeof(GameManager).GetMethod("UpdateTowerFx", Private, null, new[] { typeof(float), typeof(float) }, null).Invoke(game, new object[] { 1f, .1f });
                Assert.IsNull(game.Effects);
                Assert.AreEqual(0, game.TowerShotCount);
            }
            finally { game.DisposeSimulation(); }
        }

        [Test] public void Creatures_are_summoned_from_the_bed_evolve_attack_and_fade_back()
        {
            var game = new GameObject("Dream summon test").AddComponent<GameManager>();
            var art = Sprites.ArtOverride;
            // Building the presented hotel in EditMode logs Unity's renderer.material warning; it is unrelated.
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            try
            {
                game.StartSimulation(ConfigLoader.Load(), 241, false);
                typeof(GameManager).GetProperty(nameof(GameManager.Simulation)).SetValue(game, false);
                game.StartMatch(Role.Resident, "stitchwork_chef");
                game.SetSpeed(0);
                var def = game.Map.Rooms.OrderByDescending(r => r.BuildTiles.Count).First();
                def.Isolated = false;
                Assert.IsTrue(game.Claim(game.Human, def, true));
                var room = game.Human.Room;
                int slot = Enumerable.Range(0, room.Slots.Length).First(i => game.CanBuildAt(room, i));
                var gun = game.Cfg.towers.towers.First(t => t.id == "gun_turret");
                Invoke(game, "PlaceTower", room, slot, gun);
                var tower = room.Slots[slot];
                float now = 10;
                void Frame(float dt) { now += dt; Invoke(game, "UpdateTowerFx", now, dt); }

                Frame(.02f);
                Assert.IsNotNull(game.Effects);
                Assert.AreEqual(1, game.TowerShotCount, "A dream wisp leaves the sleeper's bed.");
                Assert.AreEqual(0f, Materialize(tower), "The creature is not there until the wisp lands.");
                for (int i = 0; i < 60; i++) Frame(1 / 30f);
                Assert.AreEqual(1f, Materialize(tower), .001f);
                Assert.AreEqual(0, game.TowerShotCount);

                tower.Level = 2;
                Frame(1 / 30f);
                Assert.Less(Materialize(tower), 1f, "Evolving re-forms the creature.");
                Assert.Greater(game.Effects.ParticleCount, 0);

                Invoke(game, "BeginNights");
                var projectiles = (IList)typeof(GameManager).GetField("projectiles", Private).GetValue(game);
                Invoke(game, "SpawnProjectile", HotelMap.Center(tower.Tile), game.Monster.Pos, gun.damageType);
                Assert.AreEqual(0, projectiles.Count, "Dream creatures no longer fire the legacy sprite shot.");
                Assert.AreEqual(1, game.TowerShotCount);

                for (int i = 0; i < 40; i++) Frame(1 / 30f);
                int before = game.Effects.ParticleCount;
                room.Slots[slot] = null;
                Object.DestroyImmediate(tower.Sr.gameObject);
                Frame(1 / 30f);
                Assert.Greater(game.Effects.ParticleCount, before, "A removed creature dissolves back into the dream.");
            }
            finally { game.DisposeSimulation(); Sprites.ArtOverride = art; UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false; }
        }

        static float Materialize(TowerInstance t)
        {
            var block = new MaterialPropertyBlock();
            t.Sr.GetPropertyBlock(block);
            return block.GetFloat("_Materialize");
        }

        static object Invoke(GameManager game, string method, params object[] args)
        {
            var info = typeof(GameManager).GetMethod(method, Private, null, args.Select(a => a.GetType()).ToArray(), null)
                ?? typeof(GameManager).GetMethod(method, Private);
            return info.Invoke(game, args);
        }
    }
}
