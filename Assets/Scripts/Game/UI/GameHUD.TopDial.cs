using UnityEngine;

namespace BadAppleHotel.Game
{
    public partial class GameHUD
    {
        float dialAngle = -62, dialVelocity, dialTarget = float.NaN;
        int dialFrame = -1;
        float dialFlashAt = -10, dialPhaseAt = -10;
        string dialPhaseWord;
        int plateSecond = -1, plateNight = -1;
        bool plateHasRoom, plateEndless;
        Phase platePhase = (Phase)(-1);
        string plateText = "";
        static readonly string[] RomanNights = { "I", "II", "III", "IV", "V", "VI" };
        static RectOffset liftPlateBorder;
        static RectOffset LiftPlateBorder => liftPlateBorder ?? (liftPlateBorder = new RectOffset(8, 8, 8, 8));

        void DrawLiftIndicator()
        {
            var d = new LiftDialLayout(vw, SafeRect().yMin, LiftDialLayout.ShowWings(vw));
            Ui(d.Interaction);
            if (d.Wings) { Ui(d.LeftWing); Ui(d.RightWing); }
            if (!repaint) return;
            HauntedSkin.Ensure();
            float now = HauntedClock.Now;
            bool results = gm.Phase == Phase.Results;
            bool endless = gm.Endless && gm.Night >= 6;
            int count = gm.Endless ? 6 : Mathf.Max(1, gm.Cfg.match.nightCount);
            int night = HauntedTrayLayout.DialNight(gm.Night, count, gm.Phase == Phase.Night || results, gm.Endless);
            StepLiftNeedle(count, night, now);
            string phaseWord = results ? "CHECK-OUT" : gm.Phase == Phase.Setup ? "CHECK-IN" : endless ? "ENDLESS" : "NIGHT";
            if (phaseWord != dialPhaseWord) { dialPhaseWord = phaseWord; dialPhaseAt = now; }

            if (d.Wings)
            {
                HauntedSkin.Tex(d.LeftWing, HauntedSkin.Texture("lift_wing"), Color.white);
                HauntedSkin.TexFlipped(d.RightWing, HauntedSkin.Texture("lift_wing"));
            }
            HauntedSkin.GlowAt(new Vector2(d.Pivot.x, d.Pivot.y + 4), new Vector2(236, 92), new Color(0, 0, 0, .55f));
            HauntedSkin.Tex(d.HousingArt, HauntedSkin.Texture("lift_housing"), Color.white);
            HauntedSkin.Tex(d.Face, HauntedSkin.Texture("dial_face"), Color.white);
            float flashAge = Mathf.Max(0, now - dialFlashAt);
            float glow = flashAge < .15f ? .6f * flashAge / .15f : Mathf.Lerp(.6f, .3f, Mathf.Clamp01((flashAge - .15f) / .45f));
            Color energy = gm.HumanRole == Role.Monster ? FearPink : HauntedSkin.Mint;
            for (int i = 1; i <= count; i++)
            {
                Vector2 p = d.NumeralPosition(i, count);
                if (i == night) HauntedSkin.GlowAt(p, Vector2.one * 24, new Color(energy.r, energy.g, energy.b, glow));
                HauntedSkin.Label(new Rect(p.x - 14, p.y - 12, 28, 24), i <= 6 ? RomanNights[i - 1] : i.ToString(),
                    HauntedSkin.Gothic(15), i == night ? HauntedSkin.CostInk : HauntedSkin.Ink);
            }
            float sway = .8f * Mathf.Sin(1.3f * now) + .4f * Mathf.Sin(3.1f * now);
            if (endless) sway += (Mathf.PerlinNoise(now * 9, .17f) - .5f) * 3;
            HauntedSkin.Rotated(dialAngle + sway, d.Pivot, () => HauntedSkin.Tex(d.Needle, HauntedSkin.Texture("needle_finger"),
                new Color(1, 1, 1, gm.Phase == Phase.Night ? 1 : .6f)));
            HauntedSkin.Tex(d.Hub, HauntedSkin.Texture("skull_hub"), Color.white);
            if (!results) DrawLiftCountdown(d, phaseWord, now);
        }

        void StepLiftNeedle(int count, int night, float now)
        {
            float target = LiftDialLayout.NumeralAngle(night, count);
            if (float.IsNaN(dialTarget) || !Mathf.Approximately(dialTarget, target))
            {
                dialTarget = target;
                dialFlashAt = now;
            }
            if (dialFrame == Time.frameCount) return;
            dialFrame = Time.frameCount;
            float remaining = Mathf.Min(.05f, HauntedClock.Delta);
            while (remaining > 0)
            {
                float dt = Mathf.Min(1f / 120, remaining); remaining -= dt;
                dialVelocity += (60 * (target - dialAngle) - 9 * dialVelocity) * dt;
                dialAngle += dialVelocity * dt;
            }
        }

        void DrawLiftCountdown(LiftDialLayout d, string phaseWord, float now)
        {
            bool housed = gm.Human != null && gm.Human.Room != null;
            bool endless = gm.Endless && gm.Night >= 6;
            int second = Mathf.Max(0, Mathf.CeilToInt(gm.PhaseTimer));
            if (second != plateSecond || gm.Night != plateNight || gm.Phase != platePhase || housed != plateHasRoom || endless != plateEndless)
            {
                plateSecond = second; plateNight = gm.Night; platePhase = gm.Phase; plateHasRoom = housed; plateEndless = endless;
                plateText = gm.Phase == Phase.Setup && !housed ? "Find a room · " + Clock(second)
                    : endless ? "Night " + gm.Night + " · " + Clock(second) : Clock(second);
            }
            var phaseStyle = HauntedSkin.SmallCaps(11);
            var timeStyle = HauntedSkin.Typed(gm.Phase == Phase.Setup && !housed ? 13 : 15);
            float phaseWidth = HauntedSkin.Measure(phaseWord, phaseStyle).x;
            float timeWidth = HauntedSkin.Measure(plateText, timeStyle).x;
            float textWidth = phaseWidth + 7 + timeWidth;
            Rect plate = d.Plate(textWidth + 20);
            Ui(plate);
            float angle = 6 * Mathf.Exp(-6 * Mathf.Max(0, now - dialPhaseAt)) * Mathf.Cos(14 * Mathf.Max(0, now - dialPhaseAt));
            HauntedSkin.Rotated(angle, d.Pivot, () =>
            {
                float inset = plate.width * .5f - 10;
                HauntedSkin.Fill(new Rect(d.Pivot.x - inset - .7f, d.Sill.yMax, 1.4f, 6), HauntedSkin.BrassMid);
                HauntedSkin.Fill(new Rect(d.Pivot.x + inset - .7f, d.Sill.yMax, 1.4f, 6), HauntedSkin.BrassMid);
                HauntedSkin.NineSlice(plate, HauntedSkin.Texture("plate_9"), LiftPlateBorder, Color.white);
                float x = plate.center.x - textWidth * .5f;
                float baseline = plate.y + 15;
                HauntedSkin.LabelOnBaseline(x, baseline, phaseWord, phaseStyle, HauntedSkin.PhaseInk);
                HauntedSkin.LabelOnBaseline(x + phaseWidth + 7, baseline, plateText, timeStyle, HauntedSkin.Ink);
            });
        }
    }
}
