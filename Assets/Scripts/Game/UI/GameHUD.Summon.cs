using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using BadAppleHotel.Rules;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// The summon tray: tapping an empty plate raises a glass tray from the bottom with the tower types as an
    /// icon rail on its right edge. Picking a card puts a ghost of that tower on the plate with its range drawn
    /// on the floor; dragging a card onto another plate moves the summon there. Summon confirms, Cancel or the
    /// corner X closes it. Cards you can't afford count down from your income instead of turning red.
    /// </summary>
    public partial class GameHUD
    {
        static readonly string[] TypeIds = { "bullets", "fire", "electric", "effects", "resources" };
        static readonly string[] TypeNames = { "Bullets", "Fire", "Electric", "Effects", "Income" };
        static readonly string[] TypeIcons = { "type_bullets", "type_fire", "type_electric", "type_effects", "type_resources" };
        static readonly string[] TypeBlurbs =
        {
            "Steady single-target damage.", "Burns that keep hurting after the hit.", "Jolts and stuns that interrupt attacks.",
            "Slow the monster down or watch the whole hotel.", "Earn Dream Power or Faith while you hold out.",
        };

        int buildTab;
        string previewId;
        bool cardDragging;
        string cardDragId;
        Vector2 cardDragPos;

        const float TrayHeight = 316f;

        static string CategoryOf(Config.TowerDef t)
        {
            if (!string.IsNullOrEmpty(t.category)) return t.category;
            switch (t.damageType) { case "bullet": return "bullets"; case "fire": return "fire"; case "electric": return "electric"; case "slow": return "effects"; }
            return t.effect == "clairvoyance" ? "effects" : "resources";
        }

        Rect SafeRect()
        {
            var safe = Screen.safeArea;
            return new Rect(safe.xMin / scale, (Screen.height - safe.yMax) / scale, safe.width / scale, safe.height / scale);
        }

        /// <summary>Bottom-centred between the joystick and the action button, without its open/close slide.</summary>
        Rect TrayRect()
        {
            var safe = SafeRect();
            float left = safe.xMin + 190f, right = safe.xMax - 168f;
            if (right - left < 640f) { left = safe.xMin + 12f; right = safe.xMax - 12f; }
            float w = Mathf.Min(1000f, right - left);
            return new Rect((left + right) / 2f - w / 2f, safe.yMax - TrayHeight - 12f, w, TrayHeight);
        }

        float Income(Resident me, string res)
        {
            if (me.Room == null) return 0f;
            if (res == "faith") return gm.Now < me.FaithBlockedUntil ? 0f : gm.FaithPerSecond(me.Room);
            return gm.DreamPerSecond(me, gm.Now) + gm.DreamGenPerSecond(me.Room);
        }

        /// <summary>"need 33 · 14s", or just the amount when nothing is coming in.</summary>
        string Shortfall(Resident me, string res, float cost)
        {
            float need = Mathf.Ceil(cost - gm.Wallet(me, res)), rate = Income(me, res);
            return "need " + need + (rate > 0.01f ? " · " + DreamSkin.Clock(need / rate) : "");
        }

        string RoleLine(Config.TowerDef t)
        {
            var cfg = gm.Cfg.towers;
            if (t.effect == "clairvoyance") return "see the hotel";
            if (t.damageType == "slow") return "slows " + Mathf.RoundToInt(UpgradeRules.SlowPct(cfg, t, 1) * 100f) + "%";
            if (DamageTypes.Index(t.damageType) >= 0) return t.rangeClass + " · " + Mathf.RoundToInt(Dps(t, 1)) + " dmg/s";
            if (t.dreamPerSecond > 0f || (t.tiers != null && t.tiers.Length > 0 && t.tiers[0].dreamPerSecond > 0f))
                return "+" + UpgradeRules.DreamRate(cfg, t, 1).ToString("0.#") + " DP/s";
            return "+" + UpgradeRules.FaithRate(cfg, t, 1).ToString("0.#") + " Faith/s";
        }

        float Dps(Config.TowerDef t, int lv) =>
            UpgradeRules.Damage(gm.Cfg.towers, t, lv) * UpgradeRules.FireRate(gm.Cfg.towers, t, lv) + UpgradeRules.BurnDamage(gm.Cfg.towers, t, lv);

        string RangeText(Config.TowerDef t, int lv)
        {
            float range = UpgradeRules.TowerRange(gm.Cfg.towers, t, lv);
            return (t.minimumRange > 0 ? t.minimumRange.ToString("0.#") + "–" : "") + range.ToString("0.#") + " tiles";
        }

        List<string> Chips(Config.TowerDef t)
        {
            var cfg = gm.Cfg.towers;
            var chips = new List<string>();
            if (t.effect == "clairvoyance") { chips.Add("reveals the whole hotel"); return chips; }
            if (t.damageType == "slow") chips.Add("slows " + Mathf.RoundToInt(UpgradeRules.SlowPct(cfg, t, 1) * 100f) + "%");
            else if (DamageTypes.Index(t.damageType) >= 0) chips.Add(Mathf.RoundToInt(Dps(t, 1)) + " dmg/s");
            if (DamageTypes.Index(t.damageType) >= 0) { chips.Add(RangeText(t, 1)); chips.Add("door support " + UpgradeRules.DoorSupportLevel(cfg, t, 1)); }
            else chips.Add(RoleLine(t) + (t.dreamPerSecond > 0f || UpgradeRules.DreamRate(cfg, t, 1) > 0f ? ", awake or asleep" : ""));
            return chips;
        }

        // ------------------------------------------------------------ world preview

        void DrawPlatePreview(Room room, int slot, Config.TowerDef pick, bool placeable, float k)
        {
            var tile = room.Def.BuildTiles[slot];
            var wpos = HotelMap.Center(tile);
            if (!placeable) HighlightTile(tile, DreamSkin.Warn);
            if (pick == null) return;
            var g = WorldToGui(wpos);
            float ppt = GuiPerTile;
            if (DamageTypes.Index(pick.damageType) >= 0)
            {
                float range = UpgradeRules.TowerRange(gm.Cfg.towers, pick, 1) * Mathf.SmoothStep(.4f, 1f, k);
                UiFx.Ellipse(g, range * ppt, range * ppt * .766f, new Color(DreamSkin.Mint.r, DreamSkin.Mint.g, DreamSkin.Mint.b, .07f * k), 0, 0, scale, fill: true);
                UiFx.Ellipse(g, range * ppt, range * ppt * .766f, new Color(DreamSkin.Mint.r, DreamSkin.Mint.g, DreamSkin.Mint.b, .8f * k), 2.2f, Time.unscaledTime * .12f, scale);
                if (pick.minimumRange > 0)
                    UiFx.Ellipse(g, pick.minimumRange * ppt, pick.minimumRange * ppt * .766f, new Color(DreamSkin.Violet.r, DreamSkin.Violet.g, DreamSkin.Violet.b, .75f * k), 1.8f, -Time.unscaledTime * .2f, scale);
            }
            float bob = Mathf.Sin(Time.unscaledTime * 2.6f) * 4f;
            DreamSkin.GlowAt(g, new Vector2(ppt * 1.6f, ppt * 1.0f), new Color(DreamSkin.Mint.r, DreamSkin.Mint.g, DreamSkin.Mint.b, .35f * k));
            var ghostCol = placeable ? new Color(.75f, 1f, .92f, .62f * k) : new Color(1f, .6f, .65f, .4f * k);
            DrawSpriteAt(GameManager.TowerSprite(pick, 1), wpos, ghostCol, new Vector2(0, -bob));
        }

        // ------------------------------------------------------------ tray

        void DrawSummonTray(Resident me, Room room, int slot)
        {
            var towers = gm.Cfg.towers.towers;
            if (buildTab < 0 || buildTab >= TypeIds.Length) buildTab = 0;
            var inTab = towers.Where(t => CategoryOf(t) == TypeIds[buildTab]).ToList();
            var pick = inTab.FirstOrDefault(t => t.id == previewId) ?? inTab.FirstOrDefault();
            previewId = pick?.id;
            bool placeable = gm.CanBuildAt(room, slot);
            float k = WindowEase();

            DrawPlatePreview(room, slot, pick, placeable, k);

            var r = TrayRect();
            r.y += (1f - k) * (r.height + 40f);
            DreamSkin.Panel(r, k);
            Ui(r);
            var oldColor = GUI.color;
            GUI.color = new Color(1, 1, 1, k);

            // corner X, then the type rail beneath it
            CloseButton(new Rect(r.xMax - 58f, r.y + 10f, 46f, 46f), CloseWindow);
            for (int i = 0; i < TypeIds.Length; i++)
            {
                int index = i;
                var b = new Rect(r.xMax - 70f, r.y + 62f + i * 50f, 58f, 46f);
                bool on = i == buildTab;
                if (on)
                {
                    DreamSkin.GlowAt(b.center, b.size * 1.6f, new Color(DreamSkin.Mint.r, DreamSkin.Mint.g, DreamSkin.Mint.b, .35f));
                    DreamSkin.Fill(b, DreamSkin.Mint, 12);
                }
                else DreamSkin.Fill(b, new Color(1, 1, 1, .045f), 12);
                DreamSkin.Icon(new Rect(b.center.x - 13f, b.center.y - 13f, 26f, 26f), TypeIcons[i], on ? DreamSkin.Ink : DreamSkin.BoneDim);
                TapZone(b, () => { buildTab = index; previewId = null; });
            }

            // header
            float x0 = r.x + 20f, contentRight = r.xMax - 86f;
            var titleSize = DreamSkin.Title.CalcSize(new GUIContent(TypeNames[buildTab]));
            DreamSkin.Label(new Rect(x0, r.y + 14f, titleSize.x + 4f, 36f), TypeNames[buildTab], DreamSkin.Title, DreamSkin.Mint, TextAnchor.MiddleLeft);
            DreamSkin.Label(new Rect(x0 + titleSize.x + 14f, r.y + 14f, contentRight - x0 - titleSize.x - 14f, 36f), TypeBlurbs[buildTab], DreamSkin.Small, DreamSkin.BoneDim, TextAnchor.MiddleLeft);

            // cards
            int n = Mathf.Max(1, inTab.Count);
            const float gap = 12f, cardH = 196f, detailMin = 320f;
            float y0 = r.y + 60f;
            float cardW = Mathf.Clamp((contentRight - x0 - detailMin - 20f - gap * (n - 1)) / n, 104f, 150f);
            for (int i = 0; i < inTab.Count; i++)
            {
                var t = inTab[i];
                var cr = new Rect(x0 + i * (cardW + gap), y0, cardW, cardH);
                // cards fade in one after another as the tray rises
                float stagger = Mathf.Clamp01((Time.unscaledTime - windowOpenedAt - .08f - i * .05f) / .2f);
                var cardColor = GUI.color;
                GUI.color = new Color(1, 1, 1, k * stagger);
                DrawTowerCard(cr, t, me, t == pick);
                GUI.color = cardColor;
                string id = t.id;
                AddZone(new Zone
                {
                    R = cr,
                    Tap = () => previewId = id,
                    Drag = g => DragCard(room, id, g),
                    Release = (g, moved) => { cardDragging = false; },
                });
            }

            // detail
            float dx = x0 + inTab.Count * (cardW + gap) - gap + 22f;
            float dw = contentRight - dx;
            if (pick != null)
            {
                DreamSkin.Label(new Rect(dx, y0 + 2f, dw, 32f), pick.name, DreamSkin.Heading, DreamSkin.Bone, TextAnchor.MiddleLeft);
                DreamSkin.Label(new Rect(dx, y0 + 38f, dw, 62f), pick.description ?? "", DreamSkin.Small, DreamSkin.BoneDim, TextAnchor.UpperLeft);
                float cx = dx, cy = y0 + 104f;
                foreach (var chip in Chips(pick))
                {
                    var size = DreamSkin.Tiny.CalcSize(new GUIContent(chip));
                    if (cx + size.x + 18f > contentRight) { cx = dx; cy += 32f; }
                    var chipRect = new Rect(cx, cy, size.x + 18f, 26f);
                    DreamSkin.Fill(chipRect, new Color(DreamSkin.Bone.r, DreamSkin.Bone.g, DreamSkin.Bone.b, .08f), 7);
                    DreamSkin.Label(chipRect, chip, DreamSkin.Tiny, DreamSkin.Bone);
                    cx += chipRect.width + 8f;
                }

                // Cancel and Summon
                float summonW = Mathf.Min(220f, dw * .6f), cancelW = Mathf.Min(130f, dw - summonW - 10f);
                var summonRect = new Rect(contentRight - summonW, r.yMax - 16f - 56f, summonW, 56f);
                var cancelRect = new Rect(summonRect.x - 10f - cancelW, summonRect.y, cancelW, 56f);
                bool afford = gm.Wallet(me, pick.costResource) >= pick.buildCost;
                DreamSkin.Border(cancelRect, new Color(DreamSkin.Bone.r, DreamSkin.Bone.g, DreamSkin.Bone.b, .35f), 1.5f, 28f);
                DreamSkin.Label(cancelRect, "Cancel", DreamSkin.Body, DreamSkin.Bone, TextAnchor.MiddleCenter);
                TapZone(cancelRect, CloseWindow);
                if (placeable && afford)
                {
                    DreamSkin.GlowAt(summonRect.center, summonRect.size * 1.5f, new Color(DreamSkin.Mint.r, DreamSkin.Mint.g, DreamSkin.Mint.b, .3f + .1f * Mathf.Sin(Time.unscaledTime * 3f)));
                    DreamSkin.Fill(summonRect, Color.white, 28f, DreamSkin.MintFill);
                    DreamSkin.Label(summonRect, "Summon  <size=15>" + pick.buildCost + " " + (pick.costResource == "faith" ? "Faith" : "DP") + "</size>", DreamSkin.Dark, DreamSkin.Ink);
                    var chosen = pick;
                    TapZone(summonRect, () => Summon(me, room, slot, chosen));
                }
                else
                {
                    DreamSkin.Fill(summonRect, new Color(1, 1, 1, .07f), 28f);
                    string text = !placeable ? "Blocked" : Shortfall(me, pick.costResource, pick.buildCost);
                    DreamSkin.Label(summonRect, text, DreamSkin.Body, DreamSkin.BoneDim, TextAnchor.MiddleCenter);
                    if (!placeable)
                        DreamSkin.Label(new Rect(dx, summonRect.y - 26f, dw, 22f), "This square would wall off your bed.", DreamSkin.Small, DreamSkin.Warn, TextAnchor.MiddleLeft);
                }
            }
            GUI.color = oldColor;

            if (cardDragging && cardDragId != null)
            {
                var def = towers.FirstOrDefault(t => t.id == cardDragId);
                if (def != null)
                {
                    DreamSkin.GlowAt(cardDragPos, new Vector2(110, 110), new Color(DreamSkin.Mint.r, DreamSkin.Mint.g, DreamSkin.Mint.b, .45f));
                    DrawSpriteFit(GameManager.TowerSprite(def, 1), new Rect(cardDragPos.x - 36f, cardDragPos.y - 70f, 72f, 80f));
                }
            }
        }

        void DrawTowerCard(Rect cr, Config.TowerDef t, Resident me, bool chosen)
        {
            float have = gm.Wallet(me, t.costResource);
            bool poor = have < t.buildCost;
            if (chosen)
                DreamSkin.GlowAt(cr.center, cr.size * 1.4f, new Color(DreamSkin.Mint.r, DreamSkin.Mint.g, DreamSkin.Mint.b, .22f));
            DreamSkin.Fill(cr, chosen ? new Color(.8f, 1f, .92f, 1f) : Color.white, 14f, DreamSkin.CardFill);
            if (chosen) DreamSkin.Fill(cr, new Color(DreamSkin.Mint.r, DreamSkin.Mint.g, DreamSkin.Mint.b, .1f), 14f);
            DreamSkin.Border(cr, chosen ? DreamSkin.Mint : new Color(DreamSkin.Bone.r, DreamSkin.Bone.g, DreamSkin.Bone.b, .12f), chosen ? 2f : 1f, 14f);

            var old = GUI.color;
            if (poor) GUI.color = old * new Color(.5f, .5f, .56f, 1f);
            DrawSpriteFit(GameManager.TowerSprite(t, 1), new Rect(cr.x + 12f, cr.y + 10f, cr.width - 24f, 94f));
            GUI.color = old;

            DreamSkin.Label(new Rect(cr.x + 6f, cr.y + 106f, cr.width - 12f, 40f), "<b>" + t.name + "</b>", DreamSkin.Body, poor ? DreamSkin.BoneDim : DreamSkin.Bone, TextAnchor.MiddleCenter);
            DreamSkin.Label(new Rect(cr.x + 4f, cr.y + 144f, cr.width - 8f, 18f), RoleLine(t), DreamSkin.Small, DreamSkin.BoneDim, TextAnchor.MiddleCenter);

            // cost pill
            string cost = Mathf.RoundToInt(t.buildCost).ToString();
            float w = DreamSkin.Value.CalcSize(new GUIContent(cost)).x + 40f;
            var pill = new Rect(cr.center.x - w / 2f, cr.yMax - 34f, w, 24f);
            DreamSkin.Fill(pill, new Color(0, 0, 0, .4f), 12f);
            DreamSkin.Icon(new Rect(pill.x + 7f, pill.y + 4f, 16f, 16f), DreamSkin.ResIcon(t.costResource), Color.white);
            DreamSkin.Label(new Rect(pill.x + 26f, pill.y, w - 32f, 24f), cost, DreamSkin.Value, DreamSkin.ResColor(t.costResource), TextAnchor.MiddleLeft);

            if (poor)
            {
                var me2 = me;
                float rate = Income(me2, t.costResource);
                if (rate > 0.01f)
                    DreamSkin.Label(new Rect(cr.xMax - 54f, cr.y + 6f, 48f, 16f), DreamSkin.Clock((t.buildCost - have) / rate), DreamSkin.Tiny, DreamSkin.BoneDim, TextAnchor.MiddleRight);
                var bar = new Rect(cr.x + 12f, cr.yMax - 6f, cr.width - 24f, 3f);
                DreamSkin.Fill(bar, new Color(1, 1, 1, .1f), 1.5f);
                DreamSkin.Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(have / t.buildCost), bar.height), DreamSkin.Mint, 1.5f);
            }
        }

        /// <summary>Dragging a card: the summon follows the finger to the nearest free plate in the room.</summary>
        void DragCard(Room room, string id, Vector2 g)
        {
            cardDragging = true;
            cardDragId = id;
            cardDragPos = g;
            previewId = id;
            var world = GuiToWorld(g);
            int best = -1;
            float bestD = 1.3f;
            for (int i = 0; i < room.Slots.Length; i++)
            {
                if (room.Slots[i] != null) continue;
                float d = Vector2.Distance(HotelMap.Center(room.Def.BuildTiles[i]), world);
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best >= 0 && best != selSlot) selSlot = best;
        }

        void Summon(Resident me, Room room, int slot, Config.TowerDef def)
        {
            var res = gm.TryBuildTower(me, slot, def.id);
            if (res == ActionResult.Ok)
            {
                var g = WorldToGui(HotelMap.Center(room.Def.BuildTiles[slot]));
                fxFront.Ring(g, GuiPerTile * 1.2f, DreamSkin.Mint);
                fxFront.Burst(g, 28, DreamSkin.Mint, 220f, 10f, .8f);
                fxFront.Say(g - new Vector2(0, GuiPerTile * 1.6f), "SUMMONED", def.name.ToUpperInvariant());
                CloseWindow();
            }
            else if (res == ActionResult.Blocked) gm.Toast("That would wall off your bed. Keep a path from the door.");
            else Report(res, ResName(def.costResource));
        }
    }
}
