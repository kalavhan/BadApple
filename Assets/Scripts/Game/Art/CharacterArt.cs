using System.Collections.Generic;
using UnityEngine;

namespace BadAppleHotel.Game
{
    public enum CharAnim { Idle = 0, Run = 1, Attack = 2 }

    [System.Serializable]
    class CharInfo
    {
        public string id;
        public int frameW, frameH, dirs;
        public float pivotX, pivotY, contentH, heightPx;
        public int idleFrames, runFrames, attackFrames;
    }

    /// <summary>
    /// 8-direction animation set for one character (idle / run / attack), sliced from the atlases in
    /// Assets/Resources/Art/Chars (made by tools/art/chars.py from Autosprite isometric packs).
    /// Atlas layout: one row per direction (E, NE, N, NW, W, SW, S, SE, top to bottom), one column per frame.
    /// </summary>
    public class CharacterSet
    {
        public Vector2 RestHead;
        public string Id;
        public Sprite[][][] Frames;   // [anim][dir][frame]
        public static readonly string[] AnimNames = { "idle", "run", "attack" };
        public static readonly float[] Fps = { 6f, 11f, 14f };

        static readonly Dictionary<string, CharacterSet> cache = new Dictionary<string, CharacterSet>();

        public static bool Exists(string id) => Resources.Load<TextAsset>("Art/Chars/" + id) != null;

        /// <summary>worldHeight: how tall (in tiles) a standing character is drawn.</summary>
        public static CharacterSet Load(string id, float worldHeight)
        {
            string key = id + "@" + worldHeight.ToString("0.##");
            if (cache.TryGetValue(key, out var hit) && hit != null && hit.Frames != null && hit.Frames[0][0][0] != null) return hit;
            var ta = Resources.Load<TextAsset>("Art/Chars/" + id);
            if (ta == null) return null;
            var info = JsonUtility.FromJson<CharInfo>(ta.text);
            float ppu = info.heightPx / worldHeight;
            var set = new CharacterSet { Id = id, RestHead = new Vector2(0f, worldHeight * 0.83f), Frames = new Sprite[3][][] };
            int[] counts = { info.idleFrames, info.runFrames, info.attackFrames };
            for (int a = 0; a < 3; a++)
            {
                var tex = Resources.Load<Texture2D>("Art/Chars/" + id + "_" + AnimNames[a]);
                if (tex == null) return null;
                set.Frames[a] = new Sprite[8][];
                for (int d = 0; d < 8; d++)
                {
                    set.Frames[a][d] = new Sprite[counts[a]];
                    int rowFromBottom = 7 - d; // atlas rows are written top-down
                    for (int f = 0; f < counts[a]; f++)
                    {
                        var rect = new Rect(f * info.frameW, rowFromBottom * info.frameH, info.frameW, info.frameH);
                        set.Frames[a][d][f] = Sprite.Create(tex, rect, new Vector2(info.pivotX, info.pivotY), ppu, 0, SpriteMeshType.FullRect);
                    }
                }
            }
            cache[key] = set;
            return set;
        }

        public static void ClearCache() => cache.Clear();

        /// <summary>Direction row (0 = east, counter-clockwise in 45 degree steps) for a world-space vector.</summary>
        public static int DirIndex(Vector2 v)
        {
            if (v.sqrMagnitude < 1e-6f) return 6;
            float ang = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
            return ((Mathf.RoundToInt(ang / 45f) % 8) + 8) % 8;
        }
    }

    /// <summary>Plays a CharacterSet on a SpriteRenderer: picks direction from facing, loops idle/run, one-shots attack.</summary>
    public class CharacterAnimator : MonoBehaviour
    {
        static Shader shader;
        CharacterSet set;
        SpriteRenderer sr;
        Material mat;
        int dir = 6;
        CharAnim anim = CharAnim.Idle;
        float t;
        float attackLeft;
        float flash;
        Color flashColor = Color.white;

        public bool Ready => set != null;
        public Vector2 RestHead => set.RestHead;

        // A static front-facing resting frame prevents attack/idle bobbing from moving the head off the pillow.
        public void Rest()
        {
            if (set == null) return;
            anim = CharAnim.Idle; dir = 6; t = 0f; attackLeft = 0f;
            Apply();
        }

        public static CharacterAnimator Attach(SpriteRenderer target, CharacterSet characters, float shirtHue = -1f)
        {
            if (characters == null || target == null) return null;
            var a = target.gameObject.AddComponent<CharacterAnimator>();
            a.set = characters;
            a.sr = target;
            if (shader == null) shader = Resources.Load<Shader>("Shaders/CharacterSprite");
            if (shader != null)
            {
                a.mat = new Material(shader);
                a.mat.SetFloat("_ShirtHue", shirtHue);
                target.sharedMaterial = a.mat;
            }
            a.Apply();
            return a;
        }

        void OnDestroy()
        {
            if (mat != null)
            {
                if (Application.isPlaying) Destroy(mat);
                else DestroyImmediate(mat);
            }
        }

        public void SetShirt(float hue, float sat = 1f, float val = 1f)
        {
            if (mat == null) return;
            mat.SetFloat("_ShirtHue", hue);
            mat.SetFloat("_ShirtSat", sat);
            mat.SetFloat("_ShirtVal", val);
        }

        public void Flash(Color c, float strength = 0.85f)
        {
            flashColor = c;
            flash = strength;
        }

        /// <summary>Called every frame by the game: facing, whether the body is moving, and whether to start an attack swing.</summary>
        public void Drive(Vector2 facing, bool moving, bool attack, bool frozen = false)
        {
            if (set == null) return;
            if (facing.sqrMagnitude > 0.0001f) dir = CharacterSet.DirIndex(facing);
            if (attack && attackLeft <= 0f)
            {
                anim = CharAnim.Attack;
                t = 0f;
                attackLeft = set.Frames[2][0].Length / CharacterSet.Fps[2];
            }
            if (attackLeft > 0f) attackLeft -= Time.deltaTime;
            else
            {
                var want = moving ? CharAnim.Run : CharAnim.Idle;
                if (want != anim) { anim = want; t = 0f; }
            }
            if (!frozen) t += Time.deltaTime;
            Apply();
        }

        void Apply()
        {
            var frames = set.Frames[(int)anim][dir];
            int n = frames.Length;
            int f = (int)(t * CharacterSet.Fps[(int)anim]);
            f = anim == CharAnim.Attack ? Mathf.Min(f, n - 1) : f % n;
            sr.sprite = frames[f];
            if (mat != null)
            {
                flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime * 4f);
                mat.SetFloat("_Flash", flash);
                mat.SetColor("_FlashColor", flashColor);
            }
        }
    }
}
