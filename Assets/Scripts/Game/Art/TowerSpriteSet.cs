using System;
using System.Collections.Generic;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// Animated art for one tower form, from Resources/Art/TowerAnim/{id}_{level}/ (written by
    /// tools/art/tower_anim.py): an idle loop and an optional attack clip, either as a single view
    /// (props such as the hourglass) or in eight directions (characters that turn to aim).
    /// Every frame shares one cell size with the pivot at the feet, so swapping frames never moves
    /// the sprite on its square. Forms without a folder keep their static tier art.
    /// </summary>
    public sealed class TowerSpriteSet
    {
        [Serializable] sealed class ClipDef { public string name; public int dirs = 1, count, cols; public float fps = 12; public bool loop = true; }
        [Serializable] sealed class SetDef { public int cellW, cellH; public float height = 1.2f, release = .25f; public ClipDef[] clips; }

        /// <summary>File suffixes in CharacterSet.DirIndex order: east, then counter-clockwise on screen.</summary>
        public static readonly string[] DirNames = { "e", "ne", "n", "nw", "w", "sw", "s", "se" };
        static readonly Dictionary<string, TowerSpriteSet> cache = new Dictionary<string, TowerSpriteSet>();

        Sprite[][] idle, fire;
        float idleFps = 12, fireFps = 16;

        /// <summary>True when the clips turn to face a target (eight views).</summary>
        public bool Directional { get; private set; }
        /// <summary>Seconds into the attack clip at which the shot leaves the creature.</summary>
        public float Release { get; private set; }
        public bool HasFire => fire != null;
        public float FireDuration => fire == null ? 0 : fire[0].Length / fireFps;

        public static TowerSpriteSet Load(string id, int level)
        {
            if (!Sprites.UseArt) return null;
            string key = id + "_" + level;
            if (cache.TryGetValue(key, out var hit)) return hit;
            TowerSpriteSet set = null;
            var meta = Resources.Load<TextAsset>("Art/TowerAnim/" + key + "/anim");
            if (meta != null)
            {
                try { set = Build(key, JsonUtility.FromJson<SetDef>(meta.text)); }
                catch (Exception e) { Debug.LogWarning("[Bad Apple Hotel] Tower animation " + key + " is unusable: " + e.Message); }
            }
            cache[key] = set;
            return set;
        }

        static TowerSpriteSet Build(string key, SetDef def)
        {
            if (def?.clips == null || def.cellW <= 0 || def.cellH <= 0) return null;
            var set = new TowerSpriteSet { Release = def.release };
            float ppu = def.cellH / Mathf.Max(.1f, def.height);
            foreach (var clip in def.clips)
            {
                if (clip.count <= 0 || clip.cols <= 0) continue;
                var views = new Sprite[clip.dirs == 8 ? 8 : 1][];
                for (int d = 0; d < views.Length; d++)
                {
                    string file = "Art/TowerAnim/" + key + "/" + clip.name + (views.Length == 8 ? "_" + DirNames[d] : "");
                    var texture = Resources.Load<Texture2D>(file);
                    if (texture == null) return null;
                    int rows = (clip.count + clip.cols - 1) / clip.cols;
                    views[d] = new Sprite[clip.count];
                    for (int f = 0; f < clip.count; f++)
                    {
                        int x = f % clip.cols, y = rows - 1 - f / clip.cols;
                        views[d][f] = Sprite.Create(texture, new Rect(x * def.cellW, y * def.cellH, def.cellW, def.cellH),
                            new Vector2(.5f, 0), ppu, 0, SpriteMeshType.FullRect);
                    }
                }
                if (clip.name == "idle") { set.idle = views; set.idleFps = clip.fps; set.Directional = views.Length == 8; }
                else if (clip.name == "fire") { set.fire = views; set.fireFps = clip.fps; }
            }
            return set.idle != null ? set : null;
        }

        static Sprite[] View(Sprite[][] views, int dir) => views[views.Length == 8 ? ((dir % 8) + 8) % 8 : 0];

        public Sprite Idle(int dir, float time)
        {
            var frames = View(idle, dir);
            return frames[(int)Mathf.Repeat(time * idleFps, frames.Length)];
        }

        /// <summary>The attack frame at this point of the clip, or null once it has finished.</summary>
        public Sprite Fire(int dir, float time)
        {
            if (fire == null || time < 0) return null;
            var frames = View(fire, dir);
            int f = (int)(time * fireFps);
            return f < frames.Length ? frames[f] : null;
        }
    }
}
