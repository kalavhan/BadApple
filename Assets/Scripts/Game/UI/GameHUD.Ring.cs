using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using BadAppleHotel.Rules;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// The tower ring: tapping a tower opens three orbs around it (Level up, Evolve, Banish) and a card with its
    /// level, its track to the next evolution and what the next step gives. Level up and Evolve unfold into a
    /// switch that you slide (or tap at either end) while the ghost of the result floats over the tower.
    /// Evolve stays dark until the last level of a form. Banish is held while mint fire climbs its rim.
    /// Double-tapping a tower takes the next step straight away when you can afford it.
    /// The bed and door use the same card with a single Upgrade button.
    /// </summary>
    public partial class GameHUD
    {
        struct RingLayout
        {
            public Vector2 Base, Center, Level, Evolve, Banish;
            public float R, Height;
            public Rect Card;
        }

        const float LevelOrb = 112f, SideOrb = 100f, BanishHoldSeconds = 1f;

        UpgradeRules.Step? pending;          // the step a switch is waiting to confirm
        float knobOffset, knobGrab = float.NaN;
        float banishHoldStart = -1f;
        float evolveIgniteAt = -99f, ringShakeAt = -99f;
        TowerInstance ringTower;
        int lastTapSlot = -1;
        float lastTapTime = -9f;

        /// <summary>Where a tower sprite sits on screen: its feet and its visible height (from the renderer's bounds).</summary>
        float TowerHeight(TowerInstance t)
        {
            float fallback = GuiPerTile * 1.3f;
            if (t.Sr == null || gm.Cam == null) return fallback;
            var b = t.Sr.bounds;
            float top = float.MaxValue, bottom = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                var sp = gm.Cam.WorldToScreenPoint(corner);
                float y = (Screen.height - sp.y) / scale;
                top = Mathf.Min(top, y); bottom = Mathf.Max(bottom, y);
            }
            float h = bottom - top;
            return h > 4f && h < 600f ? h : fallback;
        }

        RingLayout Layout(TowerInstance t)
        {
            var L = new RingLayout { Base = WorldToGui(HotelMap.Center(t.Tile)), Height = TowerHeight(t) };
            L.Center = L.Base - new Vector2(0, L.Height * .45f);
            L.R = Mathf.Clamp(L.Height * .5f + 92f, 125f, 175f);
            if (Time.unscaledTime - ringShakeAt < .35f) L.Center.x += Mathf.Sin((Time.unscaledTime - ringShakeAt) * 60f) * 6f * (1f - (Time.unscaledTime - ringShakeAt) / .35f);
            L.Level = L.Center + new Vector2(0, L.R);
            L.Evolve = L.Center + new Vector2(-L.R * .87f, L.R * .5f);
            L.Banish = L.Center + new Vector2(L.R * .87f, L.R * .5f);
            var safe = SafeRect();
            const float w = 430f, h = 318f;
            float x = L.Center.x + L.R + 72f;
            if (x + w > safe.xMax - 12f) x = L.Center.x - L.R - 72f - w;
            x = Mathf.Clamp(x, safe.xMin + 12f, safe.xMax - 12f - w);
            float y = Mathf.Clamp(L.Center.y - h * .55f, safe.yMin + 60f, safe.yMax - h - 12f);
            // Stay below the resource and monster panels in the top-right corner.
            if (x + w > vw - 280f) y = Mathf.Max(y, 172f);
            y = Mathf.Min(y, safe.yMax - h - 12f);
            L.Card = new Rect(x, y, w, h);
            return L;
        }

        // ------------------------------------------------------------ stats

        struct StatRow { public string Label, Format, Unit; public float Now, Next, Max; }

        List<StatRow> Rows(TowerInstance t, int lv, int next)
        {
            var cfg = gm.Cfg.towers;
            var def = t.Def;
            int max = gm.TowerMaxLevel(t);
            var rows = new List<StatRow>();
            if (t.IsClairvoyance) return rows;
            if (def.damageType == "slow")
                rows.Add(new StatRow { Label = "Slow", Format = "0", Unit = "%", Now = UpgradeRules.SlowPct(cfg, def, lv) * 100f, Next = UpgradeRules.SlowPct(cfg, def, next) * 100f, Max = UpgradeRules.SlowPct(cfg, def, max) * 100f });
            else if (t.IsWeapon)
                rows.Add(new StatRow { Label = "Damage / s", Format = "0", Now = Dps(def, lv), Next = Dps(def, next), Max = Dps(def, max) });
            else if (t.IsDreamGen)
                rows.Add(new StatRow { Label = "Dream Power / s", Format = "0.0", Now = UpgradeRules.DreamRate(cfg, def, lv), Next = UpgradeRules.DreamRate(cfg, def, next), Max = UpgradeRules.DreamRate(cfg, def, max) });
            else
                rows.Add(new StatRow { Label = "Faith / s", Format = "0.0", Now = UpgradeRules.FaithRate(cfg, def, lv), Next = UpgradeRules.FaithRate(cfg, def, next), Max = UpgradeRules.FaithRate(cfg, def, max) });
            if (t.IsWeapon)
            {
                rows.Add(new StatRow { Label = "Range", Format = "0.0", Now = UpgradeRules.TowerRange(cfg, def, lv), Next = UpgradeRules.TowerRange(cfg, def, next), Max = UpgradeRules.TowerRange(cfg, def, max) * 1.1f });
                rows.Add(new StatRow { Label = "Door support", Format = "0", Now = UpgradeRules.DoorSupportLevel(cfg, def, lv), Next = UpgradeRules.DoorSupportLevel(cfg, def, next), Max = 10 });
            }
            return rows;
        }

        // ------------------------------------------------------------ ring

        void DrawTowerRing(Resident me, Room room, TowerInstance t)
        {
            if (ringTower != t) { ringTower = t; pending = null; banishHoldStart = -1f; knobOffset = 0f; }
            var L = Layout(t);
            float k = WindowEase();
            var cfg = gm.Cfg.towers;
            var step = gm.TowerNextStep(t);
            float cost = gm.TowerUpgradeCost(t);
            float have = gm.Wallet(me, t.Def.costResource);
            bool afford = step != UpgradeRules.Step.Max && have >= cost;
            if (pending.HasValue && pending.Value != step) pending = null;

            // floor: range ring and the halo the orbs sit on
            float ppt = GuiPerTile;
            if (t.IsWeapon)
            {
                float range = gm.TowerRange(t);
                UiFx.Ellipse(L.Base, range * ppt, range * ppt * .766f, new Color(DreamSkin.Mint.r, DreamSkin.Mint.g, DreamSkin.Mint.b, .55f * k), 1.8f, Time.unscaledTime * .1f, scale);
            }
            float halo = L.R * (.8f + .2f * k);
            DreamSkin.Ring(L.Center, halo * 2f, 1.6f, new Color(DreamSkin.Violet.r, DreamSkin.Violet.g, DreamSkin.Violet.b, .32f * k));
            DreamSkin.GlowAt(L.Center, Vector2.one * halo * 2.3f, new Color(DreamSkin.Violet.r, DreamSkin.Violet.g, DreamSkin.Violet.b, .06f * k));

            // the ghost of what the switch would make, floating over the tower
            if (pending.HasValue)
            {
                int form = gm.TowerForm(t);
                var ghost = GameManager.TowerSprite(t.Def, pending == UpgradeRules.Step.Evolve ? form + 1 : form);
                float bob = Mathf.Sin(Time.unscaledTime * 2.4f) * 5f;
                var feet = new Vector2(L.Base.x, L.Base.y - L.Height - 18f + bob);
                DreamSkin.GlowAt(new Vector2(feet.x, feet.y - L.Height * .4f), new Vector2(L.Height, L.Height * 1.2f), new Color(DreamSkin.Violet.r, DreamSkin.Violet.g, DreamSkin.Violet.b, .3f));
                DrawSpriteGui(ghost, feet, L.Height * (pending == UpgradeRules.Step.Evolve ? 1.1f : 1f), new Color(.82f, .78f, 1f, .62f));
                if (pending == UpgradeRules.Step.LevelUp)
                    DreamSkin.Shadowed(new Rect(feet.x - 40f, feet.y - L.Height * .7f - 16f, 80f, 32f), "+1", DreamSkin.Title, DreamSkin.Mint, TextAnchor.MiddleCenter);
            }

            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, k);
            if (!pending.HasValue)
            {
                DrawLevelOrb(me, t, L.Level, step, cost, afford, k);
                DrawEvolveOrb(me, t, L.Evolve, step, cost, afford, k);
                DrawBanishOrb(me, t, L.Banish, k);
            }
            else DrawSwitch(me, t, L.Level, pending.Value, cost);
            DrawInfoCard(t, L.Card, step, k);
            GUI.color = old;
        }

        void DrawLevelOrb(Resident me, TowerInstance t, Vector2 c, UpgradeRules.Step step, float cost, bool afford, float k)
        {
            float d = LevelOrb * (.7f + .3f * k);
            bool ok = step == UpgradeRules.Step.LevelUp && afford;
            DreamSkin.Orb(c, d, ok ? DreamSkin.Mint : DreamSkin.Bone, ok ? .95f : .22f, ok ? .28f + .08f * Mathf.Sin(Time.unscaledTime * 3f) : 0f);
            float lift = ok ? Mathf.Repeat(Time.unscaledTime * .8f, 1f) * 6f : 0f;
            DreamSkin.Icon(new Rect(c.x - 17f, c.y - 36f - lift, 34f, 34f), "levelup", ok ? DreamSkin.Mint : new Color(1, 1, 1, .25f));
            DreamSkin.Label(new Rect(c.x - 55f, c.y + 1f, 110f, 22f), "<b>Level up</b>", DreamSkin.Body, ok ? DreamSkin.Mint : new Color(DreamSkin.Bone.r, DreamSkin.Bone.g, DreamSkin.Bone.b, .45f), TextAnchor.MiddleCenter);
            string sub = step == UpgradeRules.Step.Max ? "max level" : step == UpgradeRules.Step.Evolve ? "form max" : afford ? Mathf.RoundToInt(cost) + " " + (t.Def.costResource == "faith" ? "Faith" : "DP") : Shortfall(me, t.Def.costResource, cost);
            DreamSkin.Label(new Rect(c.x - 60f, c.y + 22f, 120f, 18f), sub, DreamSkin.Tiny, ok ? DreamSkin.Mint : new Color(DreamSkin.Bone.r, DreamSkin.Bone.g, DreamSkin.Bone.b, .45f));
            var r = new Rect(c.x - d / 2f, c.y - d / 2f, d, d);
            Ui(r);
            TapZone(r, () => OpenSwitch(me, t, UpgradeRules.Step.LevelUp), round: true);
        }

        void DrawEvolveOrb(Resident me, TowerInstance t, Vector2 c, UpgradeRules.Step step, float cost, bool afford, float k)
        {
            float now = Time.unscaledTime, ignite = Mathf.Clamp01((now - evolveIgniteAt) / .9f);
            bool due = step == UpgradeRules.Step.Evolve;
            float pop = due && ignite < 1f ? Mathf.Sin(ignite * Mathf.PI) * .18f : 0f;
            float d = SideOrb * (.7f + .3f * k) * (1f + pop);
            if (due)
            {
                float breathe = .45f + .2f * Mathf.Sin(now * 2.8f) + pop * 2f;
                DreamSkin.GlowAt(c, Vector2.one * d * 2.6f, new Color(DreamSkin.Mint.r, DreamSkin.Mint.g, DreamSkin.Mint.b, .08f + pop));
                DreamSkin.Orb(c, d, DreamSkin.Violet, .95f, afford ? breathe : breathe * .5f);
                var body = new Rect(c.x - 32f, c.y - 38f, 64f, 42f);
                DreamSkin.Icon(body, "evolve_body", Color.white);
                // The shadows blink now and then.
                float blink = Mathf.Repeat(now + t.SlotIndex * .7f, 3.4f) > 3.25f ? .15f : 1f;
                var eyes = new Rect(body.x, body.center.y - body.height * blink / 2f, body.width, body.height * blink);
                DreamSkin.Icon(eyes, "evolve_eyes", Color.white);
                DreamSkin.Label(new Rect(c.x - 50f, c.y + 2f, 100f, 22f), "<b>Evolve</b>", DreamSkin.Body, afford ? DreamSkin.Mint : DreamSkin.Violet, TextAnchor.MiddleCenter);
                string sub = afford ? Mathf.RoundToInt(cost) + " " + (t.Def.costResource == "faith" ? "Faith" : "DP") : Shortfall(me, t.Def.costResource, cost);
                DreamSkin.Label(new Rect(c.x - 60f, c.y + 21f, 120f, 18f), sub, DreamSkin.Tiny, afford ? DreamSkin.Mint : DreamSkin.Violet);
            }
            else
            {
                DreamSkin.Orb(c, d, DreamSkin.Bone, .16f, 0f);
                DreamSkin.Icon(new Rect(c.x - 32f, c.y - 38f, 64f, 42f), "evolve_dim", new Color(1, 1, 1, .8f));
                DreamSkin.Label(new Rect(c.x - 50f, c.y + 2f, 100f, 22f), "<b>Evolve</b>", DreamSkin.Body, new Color(DreamSkin.Bone.r, DreamSkin.Bone.g, DreamSkin.Bone.b, .4f), TextAnchor.MiddleCenter);
                int form = gm.TowerForm(t);
                string sub = step == UpgradeRules.Step.Max || form >= (t.Def.tiers?.Length ?? 1) ? "final form" : "at Lv " + UpgradeRules.FormEnd(gm.Cfg.towers, t.Def, form);
                DreamSkin.Label(new Rect(c.x - 60f, c.y + 21f, 120f, 18f), sub, DreamSkin.Tiny, new Color(DreamSkin.Bone.r, DreamSkin.Bone.g, DreamSkin.Bone.b, .4f));
            }
            var r = new Rect(c.x - d / 2f, c.y - d / 2f, d, d);
            Ui(r);
            TapZone(r, () => OpenSwitch(me, t, UpgradeRules.Step.Evolve), round: true);
        }

        void DrawBanishOrb(Resident me, TowerInstance t, Vector2 c, float k)
        {
            float d = SideOrb * (.7f + .3f * k);
            float p = banishHoldStart >= 0 ? Mathf.Clamp01((Time.unscaledTime - banishHoldStart) / BanishHoldSeconds) : 0f;
            banishCenter = c; banishRadius = d / 2f;
            // fxBack holds the flames: they are drawn before this face, so they burn around the rim and the
            // orb itself stays readable.
            DreamSkin.Orb(c, d, DreamSkin.Mint, .45f + .5f * p, .12f + .3f * p);
            DreamSkin.Label(new Rect(c.x - 50f, c.y - 26f, 100f, 22f), "<b>Banish</b>", DreamSkin.Body, new Color(.8f, .98f, .9f, 1f), TextAnchor.MiddleCenter);
            DreamSkin.Label(new Rect(c.x - 50f, c.y - 4f, 100f, 16f), p > 0 ? "keep holding" : "hold", DreamSkin.Tiny, DreamSkin.BoneDim);
            DreamSkin.Label(new Rect(c.x - 60f, c.y + 14f, 120f, 18f), "+" + Mathf.FloorToInt(gm.TowerSellValue(t)) + " " + (t.Def.costResource == "faith" ? "Faith" : "DP"), DreamSkin.Tiny, DreamSkin.Mint);
            if (p > 0) UiFx.Arc(c, d / 2f - 2f, 4f, p, new Color(DreamSkin.Mint.r, DreamSkin.Mint.g, DreamSkin.Mint.b, .95f), scale);
            var r = new Rect(c.x - d / 2f, c.y - d / 2f, d, d);
            Ui(r);
            AddZone(new Zone
            {
                R = r, Round = true,
                Press = () => banishHoldStart = Time.unscaledTime,
                Drag = g => { if ((g - c).magnitude > d * .75f) banishHoldStart = -1f; },
                Release = (g, moved) => banishHoldStart = -1f,
            });
        }

        Vector2 banishCenter;
        float banishRadius;

        /// <summary>Feeds the mint fire around the Banish orb and finishes a completed hold (from Update).</summary>
        void UpdateBanish(float dt)
        {
            bool ringOpen = sel == Sel.Slot && ringTower != null && !pending.HasValue && windowClosingAt < 0f && gm.Human?.Room != null
                            && selSlot >= 0 && selSlot < gm.Human.Room.Slots.Length && gm.Human.Room.Slots[selSlot] == ringTower;
            if (!ringOpen) { banishHoldStart = -1f; return; }
            float p = banishHoldStart >= 0 ? Mathf.Clamp01((Time.unscaledTime - banishHoldStart) / BanishHoldSeconds) : 0f;
            float rate = 60f + 200f * p;
            flameDebt += rate * dt;
            while (flameDebt >= 1f)
            {
                flameDebt -= 1f;
                float a = Random.value * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                // Born on the outside of the rim and pushed outward, so the fire wraps the orb without covering it.
                var at = banishCenter + dir * banishRadius * Random.Range(1.02f, 1.12f);
                var late = Color.Lerp(DreamSkin.Mint, DreamSkin.Violet, .8f);
                var col = Random.value < .2f ? DreamSkin.Violet : DreamSkin.Mint;
                // Kept translucent so overlapping tongues stay mint instead of blowing out to white.
                col.a = .5f;
                fxBack.Flame(at, dir * 1.6f, 1.1f + p * .7f, col, late);
            }
            if (p >= 1f)
            {
                banishHoldStart = -1f;
                Banish(gm.Human, ringTower);
            }
        }

        float flameDebt;

        void Banish(Resident me, TowerInstance t)
        {
            float value = gm.TowerSellValue(t);
            var L = Layout(t);
            var res = gm.TrySellTower(me, t.SlotIndex);
            if (res != ActionResult.Ok) { Report(res); return; }
            for (int i = 0; i < 70; i++)
            {
                var at = L.Base + new Vector2(Random.Range(-.45f, .45f) * L.Height, -Random.Range(0f, .8f) * L.Height);
                fxFront.Flame(at, new Vector2(Random.Range(-.4f, .4f), 0), 1.6f, Random.value < .25f ? DreamSkin.Violet : DreamSkin.Mint, DreamSkin.Violet);
            }
            fxFront.Ring(L.Base, GuiPerTile * 1.3f, DreamSkin.Mint);
            fxFront.Say(L.Base - new Vector2(0, L.Height + 40f), "BANISHED", "+" + Mathf.FloorToInt(value) + " " + (t.Def.costResource == "faith" ? "FAITH" : "DP"));
            ClearSelection();
        }

        // ------------------------------------------------------------ the confirm switch

        void OpenSwitch(Resident me, TowerInstance t, UpgradeRules.Step want)
        {
            var step = gm.TowerNextStep(t);
            if (step != want) return;
            float cost = gm.TowerUpgradeCost(t);
            if (gm.Wallet(me, t.Def.costResource) < cost) { Denied(me, t); return; }
            pending = want;
            knobOffset = 0f;
            knobGrab = float.NaN;
            switchOpenedAt = Time.unscaledTime;
        }

        float switchOpenedAt;

        void DrawSwitch(Resident me, TowerInstance t, Vector2 c, UpgradeRules.Step step, float cost)
        {
            var safe = SafeRect();
            float unfold = Mathf.Clamp01((Time.unscaledTime - switchOpenedAt) / .22f);
            unfold = 1f - Mathf.Pow(1f - unfold, 3f);
            float w = Mathf.Lerp(LevelOrb, 440f, unfold), h = 86f;
            c.x = Mathf.Clamp(c.x, safe.xMin + w / 2f + 8f, safe.xMax - w / 2f - 8f);
            c.y = Mathf.Min(c.y, safe.yMax - h / 2f - 8f);
            var r = new Rect(c.x - w / 2f, c.y - h / 2f, w, h);
            DreamSkin.GlowAt(c, new Vector2(w * 1.2f, h * 2.2f), new Color(0, 0, 0, .5f));
            DreamSkin.Fill(r, new Color(.08f, .06f, .11f, .97f), h / 2f);
            DreamSkin.GlowAt(new Vector2(r.x + 60f, c.y), new Vector2(170f, 110f), new Color(DreamSkin.Warn.r, DreamSkin.Warn.g, DreamSkin.Warn.b, .14f * unfold));
            DreamSkin.GlowAt(new Vector2(r.xMax - 70f, c.y), new Vector2(200f, 120f), new Color(DreamSkin.Mint.r, DreamSkin.Mint.g, DreamSkin.Mint.b, .2f * unfold));
            DreamSkin.Border(r, new Color(DreamSkin.Bone.r, DreamSkin.Bone.g, DreamSkin.Bone.b, .22f), 1.4f, h / 2f);
            Ui(r);

            bool evolve = step == UpgradeRules.Step.Evolve;
            float half = w / 2f - 46f;
            if (float.IsNaN(knobGrab)) knobOffset = Mathf.Lerp(knobOffset, 0f, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
            float pull = Mathf.Clamp(knobOffset / Mathf.Max(1f, half), -1f, 1f);

            if (unfold > .6f)
            {
                var left = new Rect(r.x, r.y, 150f, h);
                var right = new Rect(r.xMax - 170f, r.y, 170f, h);
                var noCol = Color.Lerp(DreamSkin.BoneDim, DreamSkin.Warn, Mathf.Clamp01(-pull));
                DreamSkin.Icon(new Rect(left.x + 28f, c.y - 11f, 22f, 22f), "close", noCol);
                DreamSkin.Label(new Rect(left.x + 56f, c.y - 11f, 90f, 22f), "Cancel", DreamSkin.Body, noCol, TextAnchor.MiddleLeft);
                var yesCol = Color.Lerp(DreamSkin.Mint, Color.white, Mathf.Clamp01(pull));
                DreamSkin.Label(new Rect(right.x, c.y - 22f, right.width - 34f, 24f), "<b>" + (evolve ? "Evolve" : "Level up") + "</b>", DreamSkin.Body, yesCol, TextAnchor.MiddleRight);
                DreamSkin.Label(new Rect(right.x, c.y + 2f, right.width - 34f, 18f), Mathf.RoundToInt(cost) + " " + (t.Def.costResource == "faith" ? "Faith" : "DP"), DreamSkin.Tiny, yesCol, TextAnchor.MiddleRight);
                // faint guides toward each end
                for (int i = 1; i <= 3; i++)
                {
                    float a = .12f + .1f * Mathf.Repeat(Time.unscaledTime * 1.6f - i * .25f, 1f);
                    DreamSkin.GlowAt(new Vector2(c.x + 46f + i * 20f, c.y), new Vector2(10f, 10f), new Color(DreamSkin.Mint.r, DreamSkin.Mint.g, DreamSkin.Mint.b, a * 2f));
                    DreamSkin.GlowAt(new Vector2(c.x - 46f - i * 20f, c.y), new Vector2(10f, 10f), new Color(DreamSkin.Bone.r, DreamSkin.Bone.g, DreamSkin.Bone.b, a));
                }
                TapZone(left, () => pending = null);
                TapZone(right, () => Confirm(me, t));
            }

            var kc = new Vector2(c.x + knobOffset, c.y);
            DreamSkin.Orb(kc, 76f, evolve ? DreamSkin.Violet : DreamSkin.Mint, .95f, .35f + .25f * Mathf.Abs(pull));
            if (evolve)
            {
                DreamSkin.Icon(new Rect(kc.x - 26f, kc.y - 17f, 52f, 34f), "evolve_body", Color.white);
                DreamSkin.Icon(new Rect(kc.x - 26f, kc.y - 17f, 52f, 34f), "evolve_eyes", Color.white);
            }
            else DreamSkin.Icon(new Rect(kc.x - 16f, kc.y - 18f, 32f, 32f), "levelup", DreamSkin.Mint);
            var knob = new Rect(kc.x - 40f, kc.y - 40f, 80f, 80f);
            AddZone(new Zone
            {
                R = knob, Round = true,
                Drag = g =>
                {
                    if (float.IsNaN(knobGrab)) knobGrab = g.x - knobOffset;
                    knobOffset = Mathf.Clamp(g.x - knobGrab, -half, half);
                },
                Release = (g, moved) =>
                {
                    knobGrab = float.NaN;
                    float f = knobOffset / Mathf.Max(1f, half);
                    if (f > .7f) Confirm(me, t);
                    else if (f < -.7f) pending = null;
                },
            });
        }

        void Confirm(Resident me, TowerInstance t)
        {
            var want = pending;
            pending = null;
            knobGrab = float.NaN;
            if (!want.HasValue || gm.TowerNextStep(t) != want.Value) return;
            TakeStep(me, t, false);
        }

        /// <summary>Levels up or evolves, with the matching mark; returns false (after saying why) when it can't.</summary>
        bool TakeStep(Resident me, TowerInstance t, bool quick)
        {
            var step = gm.TowerNextStep(t);
            if (step == UpgradeRules.Step.Max) { Denied(me, t); return false; }
            var res = gm.TryUpgradeTower(me, t.SlotIndex);
            if (res != ActionResult.Ok)
            {
                if (res == ActionResult.NoMoney) Denied(me, t); else Report(res, ResName(t.Def.costResource));
                return false;
            }
            float h = TowerHeight(t);
            var b = WorldToGui(HotelMap.Center(t.Tile));
            var top = b - new Vector2(0, h);
            fxFront.Ring(b, GuiPerTile * (step == UpgradeRules.Step.Evolve ? 1.6f : 1.1f), step == UpgradeRules.Step.Evolve ? DreamSkin.Violet : DreamSkin.Mint);
            fxFront.Burst(b - new Vector2(0, h * .4f), step == UpgradeRules.Step.Evolve ? 48 : 30, DreamSkin.Mint, step == UpgradeRules.Step.Evolve ? 300f : 200f, 9f, .9f);
            if (step == UpgradeRules.Step.Evolve)
                fxFront.Say(top - new Vector2(0, 40f), "EVOLVED", gm.TowerName(t).ToUpperInvariant());
            else
                fxFront.Say(top - new Vector2(0, 40f), "LEVEL UP", "LV " + t.Level + (quick ? " · DOUBLE-TAP" : ""));
            if (gm.TowerNextStep(t) == UpgradeRules.Step.Evolve) evolveIgniteAt = Time.unscaledTime + .4f;
            return true;
        }

        void Denied(Resident me, TowerInstance t)
        {
            ringShakeAt = Time.unscaledTime;
            var step = gm.TowerNextStep(t);
            var b = WorldToGui(HotelMap.Center(t.Tile));
            var top = b - new Vector2(0, TowerHeight(t) + 40f);
            if (step == UpgradeRules.Step.Max) fxFront.Say(top, "FULLY GROWN", "", true);
            else
            {
                float need = Mathf.Ceil(gm.TowerUpgradeCost(t) - gm.Wallet(me, t.Def.costResource));
                fxFront.Say(top, "NOT YET", "NEED " + need + " " + (t.Def.costResource == "faith" ? "FAITH" : "DP"), true);
            }
        }

        // ------------------------------------------------------------ info card

        void DrawInfoCard(TowerInstance t, Rect r, UpgradeRules.Step step, float k)
        {
            var cfg = gm.Cfg.towers;
            int lv = t.Level, form = gm.TowerForm(t), forms = Mathf.Max(1, t.Def.tiers?.Length ?? 1);
            r.x += (1f - k) * 30f;
            DreamSkin.Panel(r, k);
            Ui(r);
            CloseButton(new Rect(r.xMax - 56f, r.y + 10f, 44f, 44f), CloseWindow);

            DrawSpriteFit(GameManager.TowerSprite(t.Def, form), new Rect(r.x + 16f, r.y + 14f, 62f, 66f));
            DreamSkin.Label(new Rect(r.x + 88f, r.y + 16f, r.width - 150f, 32f), gm.TowerName(t), DreamSkin.Heading, DreamSkin.Bone, TextAnchor.MiddleLeft);
            int type = Mathf.Max(0, System.Array.IndexOf(TypeIds, CategoryOf(t.Def)));
            string kind = t.IsWeapon ? TypeNames[type] + " · " + t.Def.rangeClass + " range" : TypeNames[type];
            DreamSkin.Label(new Rect(r.x + 88f, r.y + 48f, r.width - 150f, 20f), kind, DreamSkin.Small, DreamSkin.BoneDim, TextAnchor.MiddleLeft);

            // level line
            float y = r.y + 88f;
            var lvText = "Lv " + lv;
            var lvSize = DreamSkin.Title.CalcSize(new GUIContent(lvText));
            DreamSkin.GlowAt(new Vector2(r.x + 20f + lvSize.x / 2f, y + 18f), new Vector2(lvSize.x * 1.8f, 50f), new Color(DreamSkin.Mint.r, DreamSkin.Mint.g, DreamSkin.Mint.b, .14f));
            DreamSkin.Label(new Rect(r.x + 20f, y, lvSize.x + 6f, 36f), lvText, DreamSkin.Title, DreamSkin.Mint, TextAnchor.MiddleLeft);
            string formLine = step == UpgradeRules.Step.Max ? "Form " + form + " of " + forms + " · max level"
                : form >= forms ? "Form " + form + " of " + forms + " · final form"
                : step == UpgradeRules.Step.Evolve ? "Form " + form + " of " + forms + " · <color=" + DreamSkin.VioletHex + ">ready to evolve</color>"
                : "Form " + form + " of " + forms + " · evolves at Lv " + UpgradeRules.FormEnd(cfg, t.Def, form);
            DreamSkin.Label(new Rect(r.x + 32f + lvSize.x, y + 8f, r.width - lvSize.x - 50f, 22f), formLine, DreamSkin.Small, DreamSkin.BoneDim, TextAnchor.MiddleLeft);

            // track: one pip per level, grouped by form, with the evolve points between groups
            y += 46f;
            int max = gm.TowerMaxLevel(t);
            float x = r.x + 20f;
            const float pipW = 15f, pipH = 7f, pipGap = 3f;
            for (int f = 1; f <= forms; f++)
            {
                int from = UpgradeRules.FormStart(cfg, t.Def, f), to = UpgradeRules.FormEnd(cfg, t.Def, f);
                float gw = (to - from + 1) * (pipW + pipGap) - pipGap + 8f;
                if (f == form) DreamSkin.Fill(new Rect(x - 4f, y - 5f, gw, pipH + 10f), new Color(DreamSkin.Mint.r, DreamSkin.Mint.g, DreamSkin.Mint.b, .08f), 5f);
                for (int l = from; l <= to; l++)
                {
                    var pip = new Rect(x, y, pipW, pipH);
                    if (l <= lv) DreamSkin.Fill(pip, DreamSkin.Mint, 3.5f);
                    else if (l == lv + 1 && step == UpgradeRules.Step.LevelUp)
                        DreamSkin.Border(pip, new Color(DreamSkin.Mint.r, DreamSkin.Mint.g, DreamSkin.Mint.b, .5f + .5f * Mathf.Sin(Time.unscaledTime * 6f)), 1.2f, 3.5f);
                    else DreamSkin.Fill(pip, new Color(1, 1, 1, .13f), 3.5f);
                    x += pipW + pipGap;
                }
                x += 4f;
                if (f < forms)
                {
                    bool done = lv > to, due = lv == to;
                    float a = done ? 1f : due ? .55f + .45f * Mathf.Sin(Time.unscaledTime * 5f) : .3f;
                    DreamSkin.Icon(new Rect(x + 1f, y - 6f, 28f, 19f), done || due ? "evolve_body" : "evolve_dim", new Color(1, 1, 1, a));
                    x += 34f;
                }
            }

            // rows: now and next, the part the next step adds glowing
            y += 26f;
            int next = step == UpgradeRules.Step.Max ? lv : lv + 1;
            bool preview = pending.HasValue;
            foreach (var row in Rows(t, lv, next))
            {
                DreamSkin.Label(new Rect(r.x + 20f, y, 130f, 20f), row.Label, DreamSkin.Small, DreamSkin.BoneDim, TextAnchor.MiddleLeft);
                var bar = new Rect(r.x + 150f, y + 7f, r.width - 300f, 7f);
                float m = Mathf.Max(.001f, row.Max);
                DreamSkin.Fill(bar, new Color(1, 1, 1, .08f), 3.5f);
                DreamSkin.Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(row.Now / m), bar.height), DreamSkin.BoneDim, 3.5f);
                if (row.Next > row.Now)
                {
                    float a = preview ? .7f + .3f * Mathf.Sin(Time.unscaledTime * 6f) : .45f;
                    DreamSkin.Fill(new Rect(bar.x + bar.width * Mathf.Clamp01(row.Now / m), bar.y, bar.width * Mathf.Clamp01((row.Next - row.Now) / m), bar.height),
                        new Color(DreamSkin.Mint.r, DreamSkin.Mint.g, DreamSkin.Mint.b, a), 3.5f);
                }
                string value = row.Now.ToString(row.Format) + row.Unit;
                if (row.Next.ToString(row.Format) != row.Now.ToString(row.Format)) value += " <color=" + DreamSkin.MintHex + (preview ? "" : "AA") + ">→ " + row.Next.ToString(row.Format) + row.Unit + "</color>";
                DreamSkin.Label(new Rect(r.xMax - 146f, y, 128f, 20f), value, DreamSkin.Value, DreamSkin.Bone, TextAnchor.MiddleRight);
                y += 26f;
            }
            if (t.IsClairvoyance)
            {
                DreamSkin.Label(new Rect(r.x + 20f, y, r.width - 40f, 40f), "You see the whole hotel. Use Hotel view to look around.", DreamSkin.Small, DreamSkin.BoneDim, TextAnchor.UpperLeft);
                y += 26f;
            }

            // what comes next
            float lineY = r.yMax - 52f;
            DreamSkin.Fill(new Rect(r.x + 20f, lineY - 6f, r.width - 40f, 1f), new Color(1, 1, 1, .08f), 0);
            string nextLine = step == UpgradeRules.Step.Max ? (form >= forms && forms > 1 ? "Final form, fully grown." : "This can't be upgraded.")
                : step == UpgradeRules.Step.Evolve ? "Next: <color=" + DreamSkin.VioletHex + ">evolve into " + UpgradeRules.TowerName(cfg, t.Def, lv + 1) + "</color>"
                : "Next: <color=" + DreamSkin.MintHex + ">Lv " + (lv + 1) + "</color>" + (form >= forms
                    ? (UpgradeRules.FormEnd(cfg, t.Def, form) - lv - 1 > 0 ? ", then " + (UpgradeRules.FormEnd(cfg, t.Def, form) - lv - 1) + " more to max" : ", the last level")
                    : UpgradeRules.FormEnd(cfg, t.Def, form) - lv - 1 > 0 ? ", then " + (UpgradeRules.FormEnd(cfg, t.Def, form) - lv - 1) + " more before Evolve" : ", then Evolve unlocks");
            DreamSkin.Label(new Rect(r.x + 20f, lineY, r.width - 40f, 22f), nextLine, DreamSkin.Small, DreamSkin.BoneDim, TextAnchor.MiddleLeft);
            if (step != UpgradeRules.Step.Max)
                DreamSkin.Label(new Rect(r.x + 20f, lineY + 22f, r.width - 40f, 18f), "Double-tap the tower to skip the switch.", DreamSkin.Tiny, new Color(DreamSkin.BoneDim.r, DreamSkin.BoneDim.g, DreamSkin.BoneDim.b, .7f), TextAnchor.MiddleLeft);
        }

        // ------------------------------------------------------------ world marks

        /// <summary>Over each of your towers you can upgrade right now, a small mark floats: chevrons or the evolve shadows.</summary>
        void DrawReadyMarks(Resident me)
        {
            if (me == null || !me.Alive || me.Room == null || gm.HotelView) return;
            float now = Time.unscaledTime;
            foreach (var t in me.Room.Slots)
            {
                if (t == null || (sel == Sel.Slot && ringTower == t && windowClosingAt < 0f)) continue;
                var step = gm.TowerNextStep(t);
                if (step == UpgradeRules.Step.Max || gm.Wallet(me, t.Def.costResource) < gm.TowerUpgradeCost(t)) continue;
                var b = WorldToGui(HotelMap.Center(t.Tile));
                float bob = Mathf.Sin(now * 2.2f + t.SlotIndex) * 4f;
                var c = b - new Vector2(0, TowerHeight(t) + 18f + bob);
                if (step == UpgradeRules.Step.Evolve)
                {
                    DreamSkin.GlowAt(c, new Vector2(70, 50), new Color(DreamSkin.Violet.r, DreamSkin.Violet.g, DreamSkin.Violet.b, .45f));
                    DreamSkin.Icon(new Rect(c.x - 24f, c.y - 16f, 48f, 32f), "evolve_body", Color.white);
                    DreamSkin.Icon(new Rect(c.x - 24f, c.y - 16f, 48f, 32f), "evolve_eyes", Color.white);
                }
                else
                {
                    DreamSkin.GlowAt(c, new Vector2(50, 50), new Color(DreamSkin.Mint.r, DreamSkin.Mint.g, DreamSkin.Mint.b, .4f));
                    DreamSkin.Icon(new Rect(c.x - 13f, c.y - 13f, 26f, 26f), "levelup", DreamSkin.Mint);
                }
            }
        }

        /// <summary>The slot of your tower whose sprite covers a GUI point (front-most first), or -1.</summary>
        int TowerAt(Room room, Vector2 g)
        {
            int best = -1;
            float bestY = float.MinValue;
            for (int i = 0; i < room.Slots.Length; i++)
            {
                var t = room.Slots[i];
                if (t == null) continue;
                var b = WorldToGui(HotelMap.Center(t.Tile));
                float h = TowerHeight(t), w = Mathf.Max(GuiPerTile * .7f, h * .55f);
                if (g.x < b.x - w / 2f || g.x > b.x + w / 2f || g.y < b.y - h || g.y > b.y + GuiPerTile * .2f) continue;
                if (b.y > bestY) { bestY = b.y; best = i; }
            }
            return best;
        }

        /// <summary>A tap on one of your towers: opens or closes its ring; two quick taps take the next step.</summary>
        void TowerTapped(Resident me, Room room, int slot)
        {
            float now = Time.unscaledTime;
            var t = room.Slots[slot];
            bool twice = lastTapSlot == slot && now - lastTapTime < .35f;
            lastTapSlot = slot; lastTapTime = now;
            if (twice)
            {
                lastTapSlot = -1;
                pending = null;
                if (sel != Sel.Slot || selSlot != slot) OpenWindow(Sel.Slot, slot);
                TakeStep(me, t, true);
                return;
            }
            if (sel == Sel.Slot && selSlot == slot && windowClosingAt < 0f) CloseWindow();
            else OpenWindow(Sel.Slot, slot);
        }

        // ------------------------------------------------------------ bed and door

        void DrawUpgradeCard(Vector2 anchorWorld, string title, string level, List<StatRow> rows, string note, string button, string sub, bool enabled, System.Action press)
        {
            float k = WindowEase();
            float h = 150f + rows.Count * 26f + (string.IsNullOrEmpty(note) ? 0f : 26f);
            var r = PopupRect(anchorWorld, 380f, h);
            r.y += (1f - k) * 20f;
            DreamSkin.Panel(r, k);
            Ui(r);
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, k);
            CloseButton(new Rect(r.xMax - 56f, r.y + 10f, 44f, 44f), CloseWindow);
            DreamSkin.Label(new Rect(r.x + 20f, r.y + 16f, r.width - 90f, 30f), title, DreamSkin.Heading, DreamSkin.Bone, TextAnchor.MiddleLeft);
            DreamSkin.Label(new Rect(r.x + 20f, r.y + 46f, r.width - 90f, 20f), level, DreamSkin.Small, DreamSkin.Mint, TextAnchor.MiddleLeft);
            float y = r.y + 76f;
            foreach (var row in rows)
            {
                DreamSkin.Label(new Rect(r.x + 20f, y, 120f, 20f), row.Label, DreamSkin.Small, DreamSkin.BoneDim, TextAnchor.MiddleLeft);
                var bar = new Rect(r.x + 140f, y + 7f, r.width - 290f, 7f);
                float m = Mathf.Max(.001f, row.Max);
                DreamSkin.Fill(bar, new Color(1, 1, 1, .08f), 3.5f);
                DreamSkin.Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(row.Now / m), bar.height), DreamSkin.BoneDim, 3.5f);
                if (row.Next > row.Now)
                    DreamSkin.Fill(new Rect(bar.x + bar.width * Mathf.Clamp01(row.Now / m), bar.y, bar.width * Mathf.Clamp01((row.Next - row.Now) / m), bar.height), new Color(DreamSkin.Mint.r, DreamSkin.Mint.g, DreamSkin.Mint.b, .6f), 3.5f);
                string value = row.Now.ToString(row.Format) + row.Unit + (row.Next.ToString(row.Format) != row.Now.ToString(row.Format) ? " <color=" + DreamSkin.MintHex + ">→ " + row.Next.ToString(row.Format) + row.Unit + "</color>" : "");
                DreamSkin.Label(new Rect(r.xMax - 146f, y, 128f, 20f), value, DreamSkin.Value, DreamSkin.Bone, TextAnchor.MiddleRight);
                y += 26f;
            }
            if (!string.IsNullOrEmpty(note))
            {
                DreamSkin.Label(new Rect(r.x + 20f, y, r.width - 40f, 22f), note, DreamSkin.Small, DreamSkin.Warn, TextAnchor.MiddleLeft);
                y += 26f;
            }
            var br = new Rect(r.x + 20f, r.yMax - 70f, r.width - 40f, 52f);
            if (button == null) DreamSkin.Label(br, "Max level", DreamSkin.Body, DreamSkin.BoneDim, TextAnchor.MiddleCenter);
            else if (enabled)
            {
                DreamSkin.GlowAt(br.center, br.size * 1.3f, new Color(DreamSkin.Mint.r, DreamSkin.Mint.g, DreamSkin.Mint.b, .25f));
                DreamSkin.Fill(br, Color.white, 26f, DreamSkin.MintFill);
                DreamSkin.Label(br, button + (string.IsNullOrEmpty(sub) ? "" : "  <size=15>" + sub + "</size>"), DreamSkin.Dark, DreamSkin.Ink);
                TapZone(br, press);
            }
            else
            {
                DreamSkin.Fill(br, new Color(1, 1, 1, .07f), 26f);
                DreamSkin.Label(br, button + (string.IsNullOrEmpty(sub) ? "" : "  <size=14>" + sub + "</size>"), DreamSkin.Body, DreamSkin.BoneDim, TextAnchor.MiddleCenter);
            }
            GUI.color = old;
        }

        void DrawBedCard(Resident me, Room room)
        {
            HighlightTile(room.Def.BedTile, DreamSkin.Mint);
            HighlightTile(room.Def.BedHeadTile, DreamSkin.Mint);
            var levels = gm.Cfg.beds.levels;
            int lv = room.BedLevel;
            float cost = gm.BedUpgradeCost(room);
            bool max = cost < 0f;
            var rows = new List<StatRow>
            {
                new StatRow { Label = "Dream Power / s", Format = "0.0", Now = levels[lv - 1].dreamPowerPerSecond,
                    Next = max ? levels[lv - 1].dreamPowerPerSecond : levels[lv].dreamPowerPerSecond, Max = levels[levels.Length - 1].dreamPowerPerSecond },
            };
            bool afford = !max && me.DreamPower >= cost;
            DrawUpgradeCard(room.Def.BedCenter, levels[lv - 1].name, "Bed · Lv " + lv + " of " + levels.Length + (max ? "" : " · next: " + levels[lv].name), rows, null,
                max ? null : afford ? "Upgrade" : Shortfall(me, "dreamPower", cost), afford ? Mathf.RoundToInt(cost) + " DP" : "", afford,
                () =>
                {
                    var res = gm.TryUpgradeBed(me);
                    if (res == ActionResult.Ok) fxFront.Say(WorldToGui(room.Def.BedCenter) - new Vector2(0, 80f), "UPGRADED", gm.Cfg.beds.levels[room.BedLevel - 1].name.ToUpperInvariant());
                    else Report(res);
                });
        }

        void DrawDoorCard(Resident me, Room room)
        {
            HighlightTile(room.Def.DoorTile, DreamSkin.Mint);
            var levels = gm.Cfg.doors.levels;
            var check = gm.CheckDoor(room);
            int lv = room.DoorLevel;
            bool atMax = check.AtMaxLevel;
            var rows = new List<StatRow>
            {
                new StatRow { Label = "Health", Format = "0", Now = room.DoorBroken ? 0 : room.DoorHp, Next = atMax ? (room.DoorBroken ? gm.MaxDoorHp(room) : room.DoorHp) : levels[lv].health, Max = levels[levels.Length - 1].health },
                new StatRow { Label = "Resist", Format = "0", Unit = "%", Now = levels[lv - 1].damageResistancePct * 100f, Next = (atMax ? levels[lv - 1] : levels[lv]).damageResistancePct * 100f, Max = 100f },
            };
            string note = !atMax && !check.Allowed ? "Upgrade your weakest weapon first (it's blinking)." : null;
            string state = room.DoorBroken ? "<color=" + "#E0475B" + ">broken</color>" : room.DoorOpen ? "open" : "shut";
            float cost = atMax ? (room.DoorBroken ? gm.DoorRepairCostAtMax(room) : -1f) : check.Cost;
            string button = cost < 0f ? null : room.DoorBroken ? (atMax ? "Rebuild" : "Rebuild + upgrade") : "Upgrade";
            bool afford = cost >= 0f && me.DreamPower >= cost;
            bool enabled = button != null && afford && (atMax || check.Allowed);
            if (button != null && !afford) button = Shortfall(me, "dreamPower", cost);
            DrawUpgradeCard(HotelMap.Center(room.Def.DoorTile), "Door", "Lv " + lv + " of " + levels.Length + " · " + state, rows, note, button, afford ? Mathf.RoundToInt(cost) + " DP" : "", enabled,
                () =>
                {
                    var res = gm.TryUpgradeDoor(me);
                    if (res == ActionResult.Ok) fxFront.Say(WorldToGui(HotelMap.Center(room.Def.DoorTile)) - new Vector2(0, 90f), room.DoorLevel > lv ? "REINFORCED" : "REBUILT", "DOOR LV " + room.DoorLevel);
                    else Report(res);
                });
        }
    }
}
