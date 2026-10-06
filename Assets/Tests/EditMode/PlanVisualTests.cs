using System.Collections.Generic;
using System.IO;
using System.Linq;
using BadAppleHotel.Config;
using BadAppleHotel.Game;
using NUnit.Framework;
using UnityEngine;

namespace BadAppleHotel.Tests
{
    public class PlanVisualTests
    {
        [Test]
        public void Render_three_seeded_hotels_for_layout_review()
        {
            var cfg = ConfigLoader.LoadFromJson(n => File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Config", n + ".json")));
            const int scale = 8;
            int w = cfg.map.width * scale, h = cfg.map.height * scale;
            var texture = new Texture2D(w * 3, h, TextureFormat.RGB24, false);
            int column = 0;
            foreach (int seed in new[] { 19, 2026, 7717 })
            {
                var map = new HotelMap(cfg.map, 10, seed);
                var parts = map.BodyPartSpawns(6, 12, seed, map.Rooms.Skip(6));
                for (int x = 0; x < map.W; x++)
                    for (int y = 0; y < map.H; y++)
                    {
                        var tile = new Vector2Int(x, y);
                        Color color = new Color32(17, 14, 23, 255);
                        switch (map.Get(x, y))
                        {
                            case Tile.Wall: color = new Color32(64, 49, 71, 255); break;
                            case Tile.Corridor: color = new Color32(128, 111, 138, 255); break;
                            case Tile.RoomFloor: color = map.RoomContaining(tile).IsCentral ? new Color32(75, 125, 175, 255) : new Color32(76, 113, 109, 255); break;
                            case Tile.Door: color = new Color32(249, 202, 106, 255); break;
                        }
                        if (parts.Contains(tile)) color = Color.red;
                        if (map.DeadEnds.Contains(tile)) color = Color.cyan;
                        if (tile == map.Lobby) color = Color.green;
                        for (int dx = 0; dx < scale; dx++)
                            for (int dy = 0; dy < scale; dy++) texture.SetPixel(column * w + x * scale + dx, y * scale + dy, color);
                    }
                column++;
            }
            texture.Apply(); File.WriteAllBytes("/tmp/badapple-three-seeds.png", texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        [Test]
        public void Render_every_character_on_every_bed_for_alignment_review()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("Visual rendering needs graphics; use xvfb-run without -nographics.");
            var cfg = ConfigLoader.LoadFromJson(n => File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Config", n + ".json")));
            var root = new GameObject("Bed alignment review");
            var camera = root.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 8.2f;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.backgroundColor = new Color32(49, 39, 59, 255);
            camera.clearFlags = CameraClearFlags.SolidColor;
            var target = new RenderTexture(1400, 1400, 16); camera.targetTexture = target;
            var made = new List<GameObject>();
            for (int person = 0; person < cfg.residents.roster.Length; person++)
                for (int bedIndex = 0; bedIndex < cfg.beds.levels.Length; bedIndex++)
                {
                    var bed = cfg.beds.levels[bedIndex];
                    Vector2 center = new Vector2((person - 3) * 2.2f, (2.5f - bedIndex) * 2.45f);
                    float angle = (person + bedIndex) % 4 * 90;
                    var bedGo = new GameObject("bed"); made.Add(bedGo);
                    bedGo.transform.position = center; bedGo.transform.rotation = Quaternion.Euler(0, 0, angle);
                    var bedSr = bedGo.AddComponent<SpriteRenderer>(); bedSr.sprite = Sprites.Bed(bed.level);
                    var body = new GameObject("sleeper"); made.Add(body);
                    var sr = body.AddComponent<SpriteRenderer>(); sr.sortingOrder = 2;
                    var set = CharacterSet.Load(cfg.residents.roster[person].art, 1.55f);
                    var anim = CharacterAnimator.Attach(sr, set, cfg.residents.shirtHues[person % cfg.residents.shirtHues.Length]);
                    anim.Rest();
                    body.transform.position = SleepPose.Position(center, angle, bed, set.RestHead);
                    body.transform.rotation = Quaternion.Euler(0, 0, angle + bed.sleepRotation);
                }
            camera.Render();
            var previous = RenderTexture.active; RenderTexture.active = target;
            var pixels = new Texture2D(1400, 1400, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, 1400, 1400), 0, 0); pixels.Apply();
            File.WriteAllBytes("/tmp/badapple-bed-review.png", pixels.EncodeToPNG());
            RenderTexture.active = previous; camera.targetTexture = null;
            foreach (var go in made) Object.DestroyImmediate(go);
            Object.DestroyImmediate(root); Object.DestroyImmediate(pixels); Object.DestroyImmediate(target);
        }
    }
}
