using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>Quiet, independently timed hotel inhabitants. All motion uses unscaled time.</summary>
    public sealed class HauntedGags
    {
        System.Random random = new System.Random(31);
        readonly float[] nextBlink = new float[2], blinkAt = { -10f, -10f };
        readonly bool[] doubleBlink = new bool[2];
        readonly float[] lookAt = new float[2], nextLook = new float[2], lookDirection = new float[2];
        float spiderAt, spiderWait, flickerAt, nextFlicker;
        int lastFrame = -1;

        float Range(float a, float b) => a + (float)random.NextDouble() * (b - a);
        public void Open(int seed)
        {
            random = new System.Random(seed ^ 0x51A7);
            float now = HauntedClock.Now;
            for (int i = 0; i < 2; i++)
            {
                nextBlink[i] = now + Range(2.5f, 6f); blinkAt[i] = -10;
                nextLook[i] = now + Range(8, 15); lookAt[i] = -10;
                doubleBlink[i] = false;
            }
            spiderAt = now + Range(7, 14); spiderWait = Range(.6f, 1.2f);
            flickerAt = -10; nextFlicker = now + Range(6, 12); lastFrame = -1;
        }

        void Step()
        {
            if (lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            float now = HauntedClock.Now;
            bool spiderBusy = now >= spiderAt && now < spiderAt + .5f + spiderWait + 1.4f;
            if (now > spiderAt + .5f + spiderWait + 1.4f) { spiderAt = now + Range(7, 14); spiderWait = Range(.6f, 1.2f); }
            for (int i = 0; i < 2; i++)
            {
                if (!spiderBusy && now >= nextBlink[i] && now - blinkAt[1 - i] > .4f)
                {
                    blinkAt[i] = now;
                    if (doubleBlink[i]) { doubleBlink[i] = false; nextBlink[i] = now + Range(2.5f, 6); }
                    else
                    {
                        doubleBlink[i] = random.NextDouble() < .15;
                        nextBlink[i] = now + (doubleBlink[i] ? .37f : Range(2.5f, 6));
                    }
                }
                if (!spiderBusy && now >= nextLook[i])
                {
                    lookAt[i] = now; lookDirection[i] = random.Next(2) == 0 ? -2 : 2;
                    nextLook[i] = now + Range(8, 15);
                }
            }
            if (!spiderBusy && now >= nextFlicker) { flickerAt = now; nextFlicker = now + Range(6, 12); }
        }

        public float BulbAlpha()
        {
            Step();
            return HauntedClock.Now - flickerAt < .06f ? .7f : .92f + .08f * Mathf.PerlinNoise(HauntedClock.Now * 3, .43f);
        }

        public void DrawEyes(Vector2 centre, int pair)
        {
            Step();
            float now = HauntedClock.Now, blinkAge = now - blinkAt[pair];
            float height = blinkAge < .12f ? Mathf.Abs(blinkAge / .06f - 1) : 1;
            float lookAge = now - lookAt[pair];
            if (lookAge < 1) centre.x += Mathf.Sin(lookAge * Mathf.PI) * lookDirection[pair];
            float glow = .8f + .2f * Mathf.PerlinNoise(now * 2 + pair * 5, .19f);
            for (int i = 0; i < 2; i++)
            {
                var p = centre + new Vector2(i == 0 ? -5 : 5, 0);
                HauntedSkin.GlowAt(p, new Vector2(18, 12 * height), new Color(1, .71f, .17f, .35f * glow));
                HauntedSkin.Tex(new Rect(p.x - 2.8f, p.y - 1.6f * height, 5.6f, 3.2f * height), DreamSkin.Disc, new Color(1, .89f, .42f, glow));
            }
        }

        public void DrawWebs(Rect tray)
        {
            float sway = .6f * Mathf.Sin(HauntedClock.Now * Mathf.PI * .5f);
            HauntedSkin.Tex(new Rect(tray.x + 50, tray.y + 62, 90 + sway, 90), HauntedSkin.Texture("cobweb"), Color.white);
            HauntedSkin.Rotated(180, new Vector2(tray.xMax - 50 - 35, tray.yMax - 40 - 35), () =>
                HauntedSkin.Tex(new Rect(tray.xMax - 120, tray.yMax - 110, 70 + sway, 70), HauntedSkin.Texture("cobweb"), Color.white));
        }

        public void Draw(Rect tray, float spiderX)
        {
            Step();
            DrawEyes(new Vector2(tray.x + 30, tray.y + tray.height * .7f), 0);
            float now = HauntedClock.Now, age = now - spiderAt, drop = 0;
            if (age >= 0 && age < .5f) drop = 18 * Mathf.Pow(age / .5f, 2);
            else if (age >= .5f && age < .5f + spiderWait) drop = 18;
            else if (age >= .5f + spiderWait && age < .5f + spiderWait + 1.4f)
                drop = 18 * (1 - Mathf.SmoothStep(0, 1, (age - .5f - spiderWait) / 1.4f));
            var root = new Vector2(spiderX, tray.y + 66);
            float thread = 70 + 3 * Mathf.Sin(now * 1.1f) + drop;
            HauntedSkin.Rotated(2 * Mathf.Sin(now * .8f), root, () =>
            {
                HauntedSkin.Fill(new Rect(root.x - .6f, root.y, 1.2f, thread), new Color(.6f, .55f, .6f, .7f), 0);
                HauntedSkin.Tex(new Rect(root.x - 15, root.y + thread - 5, 30, 30), HauntedSkin.Texture("spider"), Color.white);
            });
        }
    }
}
