using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BadAppleHotel.Config;
using BadAppleHotel.Game;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BadAppleHotel.EditorTools
{
    /// <summary>Plays a scripted dream-summon sequence in a real hotel room and saves every frame:
    /// summons from the sleeper's bed, idle presence, one attack of every style, an evolution and a
    /// banishment. Run in a disposable batch editor with a graphics device (a separate checkout while the editor is open):
    /// -executeMethod BadAppleHotel.EditorTools.TowerFxCapture.Run. BADAPPLE_TOWER_FX_OUT selects the
    /// output folder. Gameplay is frozen; only the presentation clock advances.</summary>
    public static class TowerFxCapture
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        const int Width = 1280, Height = 720, Fps = 30;
        static readonly string[] Roster = { "dragon_statue", "electric_tower", "gun_turret", "slow_totem", "sniper_nest",
            "flame_brazier", "tesla_coil", "missile_launcher", "faith_tower", "dream_lamp" };

        public static void Run()
        {
            if (!Application.isBatchMode || EditorApplication.isPlaying)
                throw new InvalidOperationException("TowerFxCapture requires a disposable batch editor outside Play Mode.");
            string output = Environment.GetEnvironmentVariable("BADAPPLE_TOWER_FX_OUT");
            if (string.IsNullOrWhiteSpace(output)) output = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Builds", "TowerFxQA");
            string frames = Path.Combine(output, "frames");
            Directory.CreateDirectory(frames);
            foreach (var old in Directory.GetFiles(frames, "*.png")) File.Delete(old);
            GameManager game = null;
            Camera camera = null;
            RenderTexture target = null;
            Texture2D image = null;
            var oldActive = RenderTexture.active;
            var oldRandom = UnityEngine.Random.state;
            bool? oldArt = Sprites.ArtOverride;
            float oldScale = Time.timeScale;
            try
            {
                game = new GameObject("Tower FX capture").AddComponent<GameManager>();
                game.StartSimulation(ConfigLoader.Load(), 241, false);
                camera = new GameObject("Tower FX camera").AddComponent<Camera>();
                camera.enabled = false; camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.035f, .03f, .045f);
                camera.nearClipPlane = .1f; camera.farClipPlane = 200; camera.allowHDR = false; camera.allowMSAA = false;
                typeof(GameManager).GetProperty(nameof(GameManager.Cam)).SetValue(game, camera);
                typeof(GameManager).GetProperty(nameof(GameManager.Simulation)).SetValue(game, false);
                Sprites.ArtOverride = true;
                UnityEngine.Random.InitState(241);
                game.StartMatch(Role.Resident, "stitchwork_chef");
                game.SetSpeed(0);
                var def = game.Map.Rooms.OrderByDescending(room => room.BuildTiles.Count).First();
                def.Isolated = false;
                if (!game.Claim(game.Human, def, true)) throw new InvalidOperationException("Cannot claim the capture room.");
                var room = game.Human.Room;
                Invoke(game, "BeginNights");
                game.Human.Pos = game.Human.LastPos = def.BedCenter;
                game.Human.Asleep = true; game.Human.SleepBlend = 1;

                // A spread of creatures across the room, nearest the camera last so none hides another.
                var slots = Enumerable.Range(0, room.Slots.Length).Where(i => game.CanBuildAt(room, i))
                    .OrderBy(i => (def.BuildTiles[i] - def.BedTile).sqrMagnitude).ToList();
                var placed = new List<TowerInstance>();
                for (int k = 0; k < Roster.Length && slots.Count > 0; k++)
                {
                    var tower = game.Cfg.towers.towers.FirstOrDefault(item => item.id == Roster[k]);
                    if (tower == null) continue;
                    int slot = slots[Mathf.Min(slots.Count - 1, k * 2)];
                    slots.Remove(slot);
                    if (!game.CanBuildAt(room, slot)) continue;
                    Invoke(game, "PlaceTower", room, slot, tower);
                    var t = room.Slots[slot];
                    SetLevel(t, 1 + k % 3);
                    placed.Add(t);
                }

                var m = game.Monster;
                if (m == null) throw new InvalidOperationException("The monster was not revealed.");
                // The monster stands on the empty square farthest from the creatures, so every attack crosses the room.
                var center = def.BuildTiles.Aggregate(Vector2.zero, (sum, cell) => sum + HotelMap.Center(cell)) / def.BuildTiles.Count;
                var spot = Enumerable.Range(0, room.Slots.Length).Where(i => room.Slots[i] == null).Select(i => def.BuildTiles[i])
                    .OrderByDescending(cell => placed.Sum(t => Vector2.Distance(HotelMap.Center(cell), HotelMap.Center(t.Tile)))).First();
                m.Pos = HotelMap.Center(spot);
                HotelView3D.Billboard(m.Sr, m.Pos);

                var towers = placed.Aggregate(Vector2.zero, (sum, t) => sum + HotelMap.Center(t.Tile)) / Mathf.Max(1, placed.Count);
                var focus = (Vector3)((towers + m.Pos) * .5f) + new Vector3(0, 0, -.6f);
                camera.orthographicSize = 4.6f;
                camera.transform.SetPositionAndRotation(focus - HotelView3D.Forward * 100, HotelView3D.Rotation);
                target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
                target.Create(); camera.targetTexture = target;
                image = new Texture2D(Width, Height, TextureFormat.RGB24, false);

                float start = 20f, dt = 1f / Fps;
                int total = Fps * 11;
                var weapons = placed.Where(t => t.IsWeapon).ToList();
                var stills = new Dictionary<int, string>
                {
                    { 10, "summon-wisps" }, { 19, "summon-landing" }, { 28, "summon-materializing" }, { 100, "idle" },
                };
                for (int frame = 0; frame < total; frame++)
                {
                    float now = start + frame * dt;
                    // Attacks: every weapon fires in turn, then all of them together.
                    int attack = frame - 110;
                    if (attack >= 0 && attack % 9 == 0 && attack / 9 < weapons.Count) Fire(game, weapons[attack / 9], m, frame, stills);
                    if (frame == 210) foreach (var t in weapons) Fire(game, t, m, -1, null);
                    if (frame == 222) stills[frame] = "volley";
                    if (frame == 250 && placed.Count > 0) { SetLevel(placed[0], placed[0].Level + 1); stills[frame + 4] = "evolve"; stills[frame + 12] = "evolve-late"; }
                    if (frame == 290 && placed.Count > 1)
                    {
                        var gone = placed[placed.Count - 1];
                        room.Slots[gone.SlotIndex] = null;
                        if (gone.Sr != null) Object.DestroyImmediate(gone.Sr.gameObject);
                        stills[frame + 5] = "banish";
                    }
                    typeof(GameManager).GetProperty(nameof(GameManager.Now)).SetValue(game, now);
                    Invoke(game, "PlaceResidentSprite", game.Human);
                    Invoke(game, "UpdateVision");
                    Invoke(game, "UpdateFloorEnergy");
                    Invoke(game, "UpdateTowerFx", now, dt);
                    camera.Render(); RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); image.Apply();
                    var png = image.EncodeToPNG();
                    File.WriteAllBytes(Path.Combine(frames, "frame-" + frame.ToString("000") + ".png"), png);
                    if (stills.TryGetValue(frame, out var name)) File.WriteAllBytes(Path.Combine(output, "tower-fx-" + name + ".png"), png);
                }
                Debug.Log("[TowerFxCapture] Wrote " + total + " frames and " + stills.Count + " stills to " + output +
                    " (" + game.Effects.QuadCount + " quads, " + game.Effects.ParticleCount + " particles at the end)");
            }
            finally
            {
                RenderTexture.active = oldActive;
                if (game != null) game.DisposeSimulation();
                if (camera != null) Object.DestroyImmediate(camera.gameObject);
                if (target != null) Object.DestroyImmediate(target);
                if (image != null) Object.DestroyImmediate(image);
                UnityEngine.Random.state = oldRandom; Sprites.ArtOverride = oldArt; Time.timeScale = oldScale;
            }
        }

        static void Fire(GameManager game, TowerInstance t, Monster m, int frame, Dictionary<int, string> stills)
        {
            Invoke(game, "SpawnProjectile", HotelMap.Center(t.Tile), m.Pos, t.Def.damageType);
            if (stills == null) return;
            stills[frame + 3] = "attack-" + t.Def.id;
            stills[frame + 7] = "impact-" + t.Def.id;
        }

        static void SetLevel(TowerInstance t, int level)
        {
            t.Level = Mathf.Clamp(level, 1, 4);
            t.Sr.sprite = TowerDirections.Get(t.Def.id, t.Level, Vector2.down) ?? Sprites.Tower(t.Def, t.Level);
            HotelView3D.Billboard(t.Sr, HotelMap.Center(t.Tile));
        }

        static object Invoke(GameManager game, string method, params object[] args)
        {
            var types = args.Select(arg => arg.GetType()).ToArray();
            var info = typeof(GameManager).GetMethod(method, Private, null, types, null) ?? typeof(GameManager).GetMethod(method, Private);
            return info.Invoke(game, args);
        }
    }
}
