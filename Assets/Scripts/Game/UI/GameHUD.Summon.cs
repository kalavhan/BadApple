using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using BadAppleHotel.Rules;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// The summon tray: tapping an empty plate raises a haunted hotel tray from the bottom with the tower types as an
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

        const float TrayHeight = HauntedTrayLayout.Height;

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
            return HauntedTrayLayout.FrameIn(SafeRect());
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
                UiFx.Ellipse(g, range * ppt, range * ppt * .766f, new Color(DreamSkin.Mint.r, DreamSkin.Mint.g, DreamSkin.Mint.b, .8f * k), 2.2f, HauntedClock.Now * .12f, scale);
                if (pick.minimumRange > 0)
                    UiFx.Ellipse(g, pick.minimumRange * ppt, pick.minimumRange * ppt * .766f, new Color(DreamSkin.Violet.r, DreamSkin.Violet.g, DreamSkin.Violet.b, .75f * k), 1.8f, -HauntedClock.Now * .2f, scale);
            }
            float bob = Mathf.Sin(HauntedClock.Now * 2.6f) * 4f;
            DreamSkin.GlowAt(g, new Vector2(ppt * 1.6f, ppt * 1.0f), new Color(DreamSkin.Mint.r, DreamSkin.Mint.g, DreamSkin.Mint.b, .35f * k));
            var ghostCol = placeable ? new Color(.75f, 1f, .92f, .62f * k) : new Color(1f, .6f, .65f, .4f * k);
            DrawSpriteAt(GameManager.TowerSprite(pick, 1), wpos, ghostCol, new Vector2(0, -bob));
        }

        // ------------------------------------------------------------ haunted tray

        [SerializeField] EctoplasmTuning ectoplasmTuning = EctoplasmTuning.Default;
        [SerializeField] SummonGlowTuning summonGlowTuning = SummonGlowTuning.Default;
        float summonGlow, lastSummon2Release = float.NegativeInfinity, summonPressedAtForGlow = float.NegativeInfinity;
        int summonGlowFrame = -1;
        bool summonGlowSubscribed;
        readonly Ectoplasm ectoplasm = new Ectoplasm();
        readonly HauntedGags hauntedGags = new HauntedGags();
        readonly List<Config.TowerDef> trayTowers = new List<Config.TowerDef>(4);
        readonly Dictionary<string, List<string>> trayChips = new Dictionary<string, List<string>>();
        Config.TowerDef[] trayConfig;
        int cachedTrayTab = -1;
        string lastTrayPick;
        Config.TowerDef oldRegisterPick;
        float pickChangedAt = -9f, blockedAt = -9f;
        bool wasBlocked;
        float summonPressedAt = -1f, closePressedAt = -9f, cancelPressedAt = -9f;
        int summonSlot = -1, closingSummonSlot = -1;
        Config.TowerDef summonPick;
        Room summonRoom;
        Resident summonResident;

        void ResetHauntedTray()
        {
            ectoplasm.Tuning = ectoplasmTuning;
            ectoplasm.Open(Time.frameCount);
            if (!summonGlowSubscribed) { ectoplasm.Released += OnTrayDripReleased; summonGlowSubscribed = true; }
            summonGlow = 0; summonGlowFrame = -1;
            lastSummon2Release = summonPressedAtForGlow = float.NegativeInfinity;
            hauntedGags.Open(Time.frameCount);
            lastTrayPick = null; oldRegisterPick = null; wasBlocked = false;
            summonPressedAt = -1f; closingSummonSlot = -1;
        }

        void OnTrayDripReleased(string id, float simulationTime, bool forced)
        {
            if (id == Ectoplasm.Summon2) lastSummon2Release = HauntedClock.Now;
        }

        void StepTrayGoo(HauntedTrayLayout layout, bool lit, float pressAge)
        {
            ectoplasm.Tuning = ectoplasmTuning;
            ectoplasm.SetAnchor(Ectoplasm.LintelA, layout.LintelA.Root, 22, 16, layout.LintelA.LandingY, true);
            ectoplasm.SetAnchor(Ectoplasm.LintelB, layout.LintelB.Root, 18, 22, layout.LintelB.LandingY, true);
            ectoplasm.SetAnchor(Ectoplasm.LintelC, layout.LintelC.Root, 14, 12, layout.LintelC.LandingY, true);
            float rootY = layout.Summon.yMax - 6 + (pressAge < .08f ? 2 : 0);
            ectoplasm.SetAnchor(Ectoplasm.Summon1, new Vector2(layout.Summon.x + layout.Summon.width * .62f, rootY), 16, 16, layout.Frame.yMax + 30, lit);
            ectoplasm.SetAnchor(Ectoplasm.Summon2, new Vector2(layout.Summon.x + layout.Summon.width * .82f, rootY), 11, 13, layout.Frame.yMax + 30, lit);
            ectoplasm.Step();
            if (summonGlowFrame == Time.frameCount) return;
            summonGlowFrame = Time.frameCount;
            ectoplasm.TryGetDrip(Ectoplasm.Summon1, out var primary);
            float target = SummonGlow.Target(lit, primary, HauntedClock.Now - lastSummon2Release, HauntedClock.Now - summonPressedAtForGlow, summonGlowTuning);
            summonGlow = lit ? SummonGlow.Smooth(summonGlow, target, HauntedClock.Delta, summonGlowTuning) : 0;
        }

        void UpdateSummonPress()
        {
            if (summonPressedAt < 0f || HauntedClock.Now - summonPressedAt < .22f) return;
            var who = summonResident; var room = summonRoom; var chosen = summonPick; int slot = summonSlot;
            summonPressedAt = -1f;
            if (windowClosingAt >= 0f || sel != Sel.Slot || selSlot != slot || gm.Human != who || who == null || !who.Alive || who.Room != room) return;
            closingSummonSlot = slot;
            Summon(who, room, slot, chosen);
            if (windowClosingAt < 0f) closingSummonSlot = -1;
        }

        void BeginSummon(Resident me, Room room, int slot, Config.TowerDef pick)
        {
            if (summonPressedAt >= 0f || windowClosingAt >= 0f || selSlot != slot || !gm.CanBuildAt(room, slot) || gm.Wallet(me, pick.costResource) < pick.buildCost) return;
            summonPressedAt = HauntedClock.Now;
            summonPressedAtForGlow = HauntedClock.Now;
            summonResident = me; summonRoom = room; summonSlot = slot; summonPick = pick;
            ectoplasm.PressSummon();
        }

        void SelectTrayTower(string id)
        {
            if (summonPressedAt < 0f && windowClosingAt < 0f) previewId = id;
        }

        void DrawSummonTray(Resident me, Room room, int slot)
        {
            if (!repaint) return;
            HauntedSkin.Ensure();
            var towers = gm.Cfg.towers.towers;
            if (buildTab < 0 || buildTab >= TypeIds.Length) buildTab = 0;
            if (trayConfig != towers || cachedTrayTab != buildTab)
            {
                if (trayConfig != towers) trayChips.Clear();
                trayConfig = towers; cachedTrayTab = buildTab; trayTowers.Clear();
                foreach (var tower in towers) if (CategoryOf(tower) == TypeIds[buildTab]) trayTowers.Add(tower);
            }
            var pick = trayTowers.FirstOrDefault(t => t.id == previewId) ?? trayTowers.FirstOrDefault();
            if (pick == null) return;
            previewId = pick.id;
            float now = HauntedClock.Now;
            if (lastTrayPick != previewId)
            {
                oldRegisterPick = towers.FirstOrDefault(t => t.id == lastTrayPick);
                pickChangedAt = now; lastTrayPick = previewId;
            }
            bool placeable = closingSummonSlot == slot || gm.CanBuildAt(room, slot);
            if (!placeable && !wasBlocked) blockedAt = now;
            wasBlocked = !placeable;
            bool lit = placeable && gm.Wallet(me, pick.costResource) >= pick.buildCost;
            float k = WindowEase();
            if (closingSummonSlot != slot) DrawPlatePreview(room, slot, pick, placeable, k);
            var r = TrayRect();
            r.y += (1f - k) * (r.height + 80f);
            float pressAge = summonPressedAt < 0 ? 9f : now - summonPressedAt;
            if (pressAge >= .03f && pressAge < .11f) r.x += Mathf.Sin(pressAge * 190f) * 2f;
            var layout = new HauntedTrayLayout(r, trayTowers.Count);
            if (!placeable)
            {
                layout.Cancel.y += 12; layout.Cancel.height = 44;
                layout.Summon.y += 12; layout.Summon.height = 44;
            }
            StepTrayGoo(layout, lit, pressAge);
            Ui(r);
            var oldColor = GUI.color;
            GUI.color = new Color(1, 1, 1, k);
            HauntedSkin.WoodFrame(r);
            HauntedSkin.Tiled(new Rect(r.x + 10, r.y + 10, r.width - 20, r.height - 20), HauntedSkin.Texture("damask"), new Vector2(72, 96), Vector2.zero);
            HauntedSkin.Tex(new Rect(r.x + 10, r.y + 10, r.width - 20, r.height - 20), HauntedSkin.Texture("body_shade"), Color.white);
            HauntedSkin.Tiled(new Rect(r.x + 10, r.yMax - 40, r.width - 20, 30), HauntedSkin.Texture("wood_h"), new Vector2(128, 128), Vector2.zero);
            HauntedSkin.Fill(new Rect(r.x + 10, r.yMax - 43, r.width - 20, 3), HauntedSkin.Brass, 0);
            HauntedSkin.Gate(layout.LeftGate);
            HauntedSkin.Gate(layout.RightGate);
            HauntedSkin.Lintel(layout.Lintel);
            for (int joint = 0; joint < 4; joint++)
            {
                float x = joint < 2 ? r.x + 18 + joint * 24 : r.xMax - 18 - (joint - 2) * 24;
                HauntedSkin.Tex(new Rect(x - 3, r.y + 67, 6, 6), DreamSkin.Disc, HauntedSkin.BrassHi);
            }
            hauntedGags.DrawWebs(r);
            float plaqueAngle = Mathf.Clamp(-4f + 10f * Mathf.Exp(-6f * (now - windowOpenedAt)) * Mathf.Cos(14f * (now - windowOpenedAt)), -8f, 8f);
            HauntedSkin.Plaque(new Rect(r.x + 68, r.y + 18, 160, 42), "Summon", "Room " + (room.Def.Index + 1) + " · Do Not Disturb", plaqueAngle);
            DrawTrayWallet(r, me, pick);
            DrawHauntedClose(new Rect(r.xMax - 40, r.y - 8, 48, 48));

            HauntedSkin.WoodFrame(layout.Cabinet);
            var strip = new Rect(layout.Cabinet.x + 12, layout.Cabinet.y + 12, layout.Cabinet.width - 24, 20);
            HauntedSkin.RegisterCard(strip);
            HauntedSkin.Label(new Rect(strip.x + 5, strip.y + 1, strip.width - 10, 18), TypeNames[buildTab].ToUpperInvariant() + " · " + TypeBlurbs[buildTab], HauntedSkin.Typed(11), HauntedSkin.Ink, TextAnchor.MiddleLeft);
            for (int i = 0; i < layout.Slots; i++)
            {
                var cr = layout.Cubby(i);
                float stagger = Mathf.Clamp01((now - windowOpenedAt - .08f - i * .05f) / .2f);
                GUI.color = new Color(1, 1, 1, k * stagger);
                if (i >= trayTowers.Count)
                {
                    HauntedSkin.NineSlice(cr, HauntedSkin.Texture("cubby_9"), CubbyBorder, Color.white);
                    if (i == layout.Slots - 1) hauntedGags.DrawEyes(cr.center, 1);
                }
                else
                {
                    var t = trayTowers[i];
                    DrawTowerCard(cr, t, me, t == pick, System.Array.IndexOf(towers, t), i);
                    string id = t.id;
                    if (windowClosingAt < 0f && summonPressedAt < 0f)
                        AddZone(new Zone { R = cr, Tap = () => SelectTrayTower(id), Drag = g => DragCard(room, id, g), Release = (g, moved) => { cardDragging = false; } });
                }
            }
            GUI.color = new Color(1, 1, 1, k);
            DrawTrayDetail(layout, pick, placeable, lit, me, room, slot, pressAge);
            DrawTypeRail(layout.Rail);
            hauntedGags.Draw(r, layout.SpiderX);
            ectoplasm.Draw(scale);
            GUI.color = oldColor;
            if (cardDragging && cardDragId != null)
            {
                var def = towers.FirstOrDefault(t => t.id == cardDragId);
                if (def != null)
                {
                    HauntedSkin.GlowAt(cardDragPos, new Vector2(110, 110), Alpha(HauntedSkin.Mint, .45f));
                    DrawSpriteFit(GameManager.TowerSprite(def, 1), new Rect(cardDragPos.x - 36, cardDragPos.y - 70, 72, 80));
                }
            }
        }

        static RectOffset cubbyBorder;
        static RectOffset CubbyBorder => cubbyBorder ?? (cubbyBorder = new RectOffset(20, 20, 20, 20));
        static RectOffset plateBorder;
        static RectOffset PlateBorder => plateBorder ?? (plateBorder = new RectOffset(8, 8, 8, 8));
        static Color Alpha(Color color, float alpha) { color.a = alpha; return color; }

        static int StableTowerHash(string id)
        {
            unchecked { int hash = 17; foreach (char c in id) hash = hash * 31 + c; return hash & 0x7fffffff; }
        }

        void DrawTowerCard(Rect cr, Config.TowerDef t, Resident me, bool chosen, int index, int cubbyIndex)
        {
            float have = gm.Wallet(me, t.costResource);
            bool poor = have < t.buildCost;
            float breath = 1f + .15f * Mathf.Sin(HauntedClock.Now * Mathf.PI * 2 / 2.4f);
            if (chosen)
            {
                HauntedSkin.GlowAt(cr.center, cr.size * 1.6f, Alpha(HauntedSkin.Violet, .3f * breath));
                HauntedSkin.GlowAt(cr.center, cr.size * 1.3f, Alpha(HauntedSkin.Mint, .5f * breath));
            }
            HauntedSkin.NineSlice(cr, HauntedSkin.Texture("cubby_9"), CubbyBorder, Color.white);
            var saved = GUI.color;
            GUI.BeginGroup(cr);
            GUI.color = saved * (poor ? new Color(.55f, .53f, .56f, .85f) : new Color(.95f, .92f, .88f, 1));
            DrawSpriteFit(GameManager.TowerSprite(t, 1), new Rect(8, 24, cr.width - 16, 100));
            GUI.color = saved;
            HauntedSkin.Tex(new Rect(0, 0, cr.width, cr.height), HauntedSkin.Texture("cubby_vignette"), Color.white);
            if (chosen) HauntedSkin.GlowAt(new Vector2(cr.width * .5f, cr.height - 50), new Vector2(cr.width, 120), Alpha(HauntedSkin.Mint, .16f * breath));
            GUI.EndGroup();
            if (chosen) HauntedSkin.Border(new Rect(cr.x + 1, cr.y + 1, cr.width - 2, cr.height - 2), Alpha(HauntedSkin.Mint, .8f), 2f, 2f);
            int hash = StableTowerHash(t.id);
            var plate = new Rect(cr.center.x - 17, cr.y + 5, 34, 16);
            HauntedSkin.Rotated(hash % 12 - 3, plate.center, () =>
            {
                HauntedSkin.NineSlice(plate, HauntedSkin.Texture("plate_9"), PlateBorder, Color.white);
                HauntedSkin.Label(plate, HauntedTrayLayout.PlateNumber(index).ToString(), HauntedSkin.Gothic(12), HauntedSkin.Ink);
            });
            float width = Mathf.Min(110, cr.width - 2), tagHeight = poor ? 64 : 54;
            var tag = new Rect(cr.center.x - width * .5f, cr.yMax - tagHeight - 8 - (chosen ? 2 : 0), width, tagHeight);
            float age = Mathf.Max(0, HauntedClock.Now - windowOpenedAt - .08f - cubbyIndex * .05f);
            float angle = chosen ? -2f : hash % 15 - 7;
            angle += 12 * Mathf.Exp(-7 * age) * Mathf.Cos(16 * age);
            if (chosen) angle += 6 * Mathf.Exp(-7 * (HauntedClock.Now - pickChangedAt)) * Mathf.Cos(16 * (HauntedClock.Now - pickChangedAt));
            angle = Mathf.Clamp(angle, -8, 8);
            HauntedSkin.Rotated(angle, new Vector2(tag.x + 14, tag.y + 25), () =>
            {
                HauntedSkin.Fill(new Rect(tag.x + 13.4f, tag.y - 10, 1.2f, 34), new Color32(217, 207, 184, 255));
                HauntedSkin.Tex(tag, HauntedSkin.Texture("tag"), Color.white);
                HauntedSkin.Label(new Rect(tag.x + 24, tag.y + 3, tag.width - 27, 27), t.name, HauntedSkin.Typed(11), HauntedSkin.Ink, TextAnchor.UpperLeft);
                if (poor)
                {
                    HauntedSkin.Fill(new Rect(tag.x + 24, tag.y + 31, tag.width - 29, 1), Alpha(HauntedSkin.Ink, .2f), 0);
                    HauntedSkin.Fill(new Rect(tag.x + 24, tag.y + 31, (tag.width - 29) * Mathf.Clamp01(have / t.buildCost), 1), HauntedSkin.CostInk, 0);
                }
                string cost = poor ? Shortfall(me, t.costResource, t.buildCost) : Mathf.RoundToInt(t.buildCost).ToString();
                if (!poor) DrawCostDiamond(new Vector2(tag.x + 26, tag.y + 41), t.costResource == "faith");
                HauntedSkin.Label(new Rect(tag.x + (poor ? 9 : 33), tag.y + 34, tag.width - (poor ? 12 : 36), poor ? 28 : 17), cost, HauntedSkin.Typed(13), poor ? Alpha(HauntedSkin.Ink, .7f) : t.costResource == "faith" ? new Color32(91, 63, 168, 255) : HauntedSkin.CostInk, TextAnchor.UpperLeft);
            });
        }

        void DrawTrayWallet(Rect frame, Resident me, Config.TowerDef pick)
        {
            bool faith = pick.costResource == "faith";
            var jar = new Rect(frame.xMax - 257, frame.y + 23, 22, 28);
            HauntedSkin.GlowAt(jar.center, jar.size * 1.8f, Alpha(faith ? HauntedSkin.Violet : HauntedSkin.Mint, .3f));
            HauntedSkin.Tex(jar, HauntedSkin.Texture(faith ? "jar_violet" : "jar_mint"), Color.white);
            for (int i = 0; i < 2; i++)
            {
                float phase = Mathf.Repeat(HauntedClock.Now / (i == 0 ? 2.2f : 3.1f) + .4f * i, 1f);
                HauntedSkin.Tex(new Rect(jar.x + 7 + i * 6, jar.yMax - 6 - phase * 12, 2.6f, 2.6f), DreamSkin.Disc, Alpha(Color.white, 1f - phase));
            }
            HauntedSkin.Label(new Rect(jar.xMax + 5, frame.y + 19, 102, 34), faith ? "Faith" : "Dream Power", HauntedSkin.Serif(15), HauntedSkin.Ivory, TextAnchor.MiddleLeft);
            HauntedSkin.Label(new Rect(frame.xMax - 132, frame.y + 19, 60, 34), Mathf.FloorToInt(gm.Wallet(me, pick.costResource)).ToString(), HauntedSkin.Gothic(22), HauntedSkin.MintHi, TextAnchor.MiddleRight);
        }

        void DrawTypeRail(Rect rail)
        {
            HauntedSkin.NineSlice(rail, HauntedSkin.Texture("rail_9"), RailBorder, Color.white);
            HauntedSkin.Label(new Rect(rail.x, rail.y + 5, rail.width, 20), "Type", HauntedSkin.Gothic(13), HauntedSkin.BrassHi);
            for (int i = 0; i < TypeIds.Length; i++)
            {
                int index = i;
                var c = new Vector2(rail.center.x, rail.y + 43 + i * 42);
                bool on = i == buildTab;
                var saved = GUI.color;
                if (on) GUI.color *= new Color(1, 1, 1, hauntedGags.BulbAlpha());
                HauntedSkin.CallButton(c, 38, DreamSkin.Icon(TypeIcons[i]), on);
                GUI.color = saved;
                if (windowClosingAt < 0 && summonPressedAt < 0)
                    TapZone(new Rect(c.x - 23, c.y - 23, 46, 46), () => { buildTab = index; previewId = null; }, round: true);
            }
        }
        static RectOffset railBorder;
        static RectOffset RailBorder => railBorder ?? (railBorder = new RectOffset(16, 16, 16, 16));

        void DrawTrayDetail(HauntedTrayLayout layout, Config.TowerDef pick, bool placeable, bool lit, Resident me, Room room, int slot, float pressAge)
        {
            var r = layout.Frame;
            var portrait = layout.Portrait;
            var nail = new Vector2(portrait.center.x, r.y + 57);
            float age = Mathf.Max(0, HauntedClock.Now - pickChangedAt);
            float angle = 3 + 5 * Mathf.Exp(-5 * age) * Mathf.Cos(11 * age);
            HauntedSkin.Rotated(angle, nail, () =>
            {
                HauntedSkin.Tex(new Rect(portrait.center.x - 55, r.y + 56, 110, 32), HauntedSkin.Texture("wire"), Color.white);
                HauntedSkin.Tex(portrait, HauntedSkin.Texture("portrait_back"), Color.white);
                DrawSpriteFit(GameManager.TowerSprite(pick, 1), new Rect(portrait.x + 35, portrait.y + 11, 80, 73));
                HauntedSkin.Tex(portrait, HauntedSkin.Texture("portrait_frame"), Color.white);
            });
            var card = layout.Detail;
            HauntedSkin.Rotated(-1.2f, card.center, () =>
            {
                HauntedSkin.RegisterCard(card);
                HauntedSkin.Tex(new Rect(card.x + card.width * .8f - 23, card.y + 70, 46, 46), HauntedSkin.Texture("coffee_ring"), Color.white);
                var saved = GUI.color;
                float textAlpha = age < .08f && oldRegisterPick != null ? 1 - age / .08f : Mathf.Clamp01((age - .08f) / .14f);
                var displayed = age < .08f && oldRegisterPick != null ? oldRegisterPick : pick;
                GUI.color *= new Color(1, 1, 1, textAlpha);
                DrawRegisterText(card, displayed, age < .08f ? 0 : -6 * (1 - textAlpha));
                GUI.color = saved;
                HauntedSkin.Tex(new Rect(card.center.x - 6, card.y - 6, 12, 12), HauntedSkin.Texture("tack"), Color.white);
            });
            if (!placeable) DrawRegisterStamp(layout.Stamp, false, pressAge);
            if (!placeable)
            {
                var slip = new Rect(card.x + 2, card.yMax - 3, card.width - 6, 18);
                var saved = GUI.color;
                GUI.color *= new Color(1, 1, 1, Mathf.Clamp01((HauntedClock.Now - blockedAt) / .15f));
                HauntedSkin.Fill(slip, HauntedSkin.Paper, 0);
                HauntedSkin.Label(slip, "This square would wall off your bed.", HauntedSkin.Typed(11), HauntedSkin.BloodRed);
                GUI.color = saved;
            }
            var summon = layout.Summon;
            bool down = pressAge < .08f;
            if (down) summon.y += 2;
            if (lit)
            {
                HauntedSkin.NineSlice(Grown(summon, summonGlowTuning.WideSpread), HauntedSkin.Texture("halo_wide_9"), WideHaloBorder,
                    Alpha(HauntedSkin.Violet, Mathf.Clamp01(summonGlowTuning.WideAlpha * summonGlow)));
                HauntedSkin.NineSlice(Grown(summon, summonGlowTuning.TightSpread), HauntedSkin.Texture("halo_tight_9"), TightHaloBorder,
                    Alpha(HauntedSkin.Mint, Mathf.Clamp01(summonGlowTuning.TightAlpha * summonGlow)));
            }
            var cancel = layout.Cancel;
            HauntedSkin.NineSlice(cancel, HauntedSkin.Texture("brass_9"), BrassBorder, Color.white);
            HauntedSkin.Tiled(new Rect(cancel.x + 3, cancel.y + 3, cancel.width - 6, cancel.height - 6), HauntedSkin.Texture("wood_v"), new Vector2(128, 128), Vector2.zero);
            if (HauntedClock.Now - cancelPressedAt < .08f) HauntedSkin.Fill(cancel, new Color(0, 0, 0, .15f), 2);
            HauntedSkin.Label(cancel, "Cancel", HauntedSkin.Gothic(19), HauntedSkin.Bone);
            if (windowClosingAt < 0 && summonPressedAt < 0)
                TapZone(cancel, () => { cancelPressedAt = HauntedClock.Now; CloseWindow(); });
            HauntedSkin.NineSlice(summon, HauntedSkin.Texture("brass_9"), BrassBorder, Color.white);
            var core = new Rect(summon.x + 4, summon.y + 4, summon.width - 8, summon.height - 8);
            HauntedSkin.Tex(core, HauntedSkin.Texture(lit ? "summon_core" : "summon_unlit"), down ? new Color(.9f, .9f, .9f, 1) : Color.white);
            if (lit)
            {
                HauntedSkin.NineSlice(Grown(summon, summonGlowTuning.BezelSpread), HauntedSkin.Texture("summon_bezel_lit_9"), LitBezelBorder,
                    Alpha(Color.white, SummonGlow.BezelAlpha(summonGlow, summonGlowTuning)));
                GUI.BeginGroup(core);
                HauntedSkin.GlowAt(core.size * .5f, new Vector2(core.width * summonGlowTuning.FaceWidth, core.height * summonGlowTuning.FaceHeight),
                    Alpha(HauntedSkin.MintHi, summonGlowTuning.FaceAlpha * SummonGlow.Emphasis(summonGlow, summonGlowTuning)));
                GUI.EndGroup();
                HauntedSkin.Fill(new Rect(core.x + 1, core.y, core.width - 2, 2), new Color(1, 1, 1, .7f), 0);
                if (summon.width < 160)
                {
                    HauntedSkin.Tex(new Rect(core.x + 7, core.y + 5, 17, 17), HauntedSkin.Texture("bell"), Color.white);
                    HauntedSkin.Label(new Rect(core.x + 27, core.y, core.width - 33, 26), "Summon", HauntedSkin.Gothic(22, true), HauntedSkin.ButtonInk);
                    DrawCostDiamond(new Vector2(core.center.x - 21, core.yMax - 10), pick.costResource == "faith");
                    HauntedSkin.Label(new Rect(core.center.x - 14, core.yMax - 19, 45, 18), Mathf.RoundToInt(pick.buildCost).ToString(), HauntedSkin.Typed(14), HauntedSkin.ButtonInk, TextAnchor.MiddleLeft);
                }
                else
                {
                    HauntedSkin.Tex(new Rect(core.x + 7, core.center.y - 10, 20, 20), HauntedSkin.Texture("bell"), Color.white);
                    HauntedSkin.Label(new Rect(core.x + 29, core.y, 82, core.height), "Summon", HauntedSkin.Gothic(22, true), HauntedSkin.ButtonInk);
                    DrawCostDiamond(new Vector2(core.xMax - 42, core.center.y), pick.costResource == "faith");
                    HauntedSkin.Label(new Rect(core.xMax - 36, core.y, 32, core.height), Mathf.RoundToInt(pick.buildCost).ToString(), HauntedSkin.Typed(14), HauntedSkin.ButtonInk, TextAnchor.MiddleRight);
                }
                if (windowClosingAt < 0 && summonPressedAt < 0) TapZone(layout.Summon, () => BeginSummon(me, room, slot, pick));
            }
            else HauntedSkin.Label(core, placeable ? Shortfall(me, pick.costResource, pick.buildCost) : "Blocked", HauntedSkin.Typed(13), placeable ? DreamSkin.BoneDim : HauntedSkin.StampRed);
            if (placeable) DrawRegisterStamp(layout.Stamp, true, pressAge);
        }
        static Rect Grown(Rect r, float amount) => new Rect(r.x - amount, r.y - amount, r.width + amount * 2, r.height + amount * 2);
        static RectOffset tightHaloBorder, wideHaloBorder, litBezelBorder;
        static RectOffset TightHaloBorder => tightHaloBorder ?? (tightHaloBorder = new RectOffset(24, 24, 24, 24));
        static RectOffset WideHaloBorder => wideHaloBorder ?? (wideHaloBorder = new RectOffset(48, 48, 48, 48));
        RectOffset LitBezelBorder
        {
            get
            {
                int border = Mathf.Max(1, summonGlowTuning.BezelBorder);
                if (litBezelBorder == null || litBezelBorder.left != border) litBezelBorder = new RectOffset(border, border, border, border);
                return litBezelBorder;
            }
        }
        static RectOffset brassBorder;
        static RectOffset BrassBorder => brassBorder ?? (brassBorder = new RectOffset(12, 12, 12, 12));

        void DrawRegisterStamp(Rect stamp, bool placeable, float pressAge)
        {
            float stampAge = !placeable ? HauntedClock.Now - blockedAt : pressAge - .03f;
            float slam = stampAge >= 0 && stampAge < .12f ? 1 - Mathf.Clamp01(stampAge / .12f) : 0;
            float stampScale = 1 + .5f * slam * slam;
            stamp = new Rect(stamp.center - stamp.size * stampScale * .5f, stamp.size * stampScale);
            HauntedSkin.Rotated(-14 - 6 * slam, stamp.center, () => HauntedSkin.Tex(stamp, HauntedSkin.Texture(placeable ? "stamp_checked_in" : "stamp_no_vacancy"), new Color(1, 1, 1, .85f)));
        }

        static void DrawCostDiamond(Vector2 centre, bool faith)
        {
            HauntedSkin.Rotated(45, centre, () =>
            {
                HauntedSkin.Fill(new Rect(centre.x - 2.5f, centre.y - 2.5f, 5, 5), faith ? HauntedSkin.FaithInk : HauntedSkin.CostInk);
                HauntedSkin.Fill(new Rect(centre.x - 2.5f, centre.y - 2.5f, 5, 2.5f), faith ? HauntedSkin.Violet : HauntedSkin.MintDeep);
            });
        }

        void DrawRegisterText(Rect card, Config.TowerDef pick, float offset)
        {
            card.y += offset;
            float x = card.x + 12, w = card.width - 24;
            HauntedSkin.Label(new Rect(x, card.y + 7, w - 48, 15), "Nightmare Register", HauntedSkin.SmallCaps(11), HauntedSkin.BloodRed, TextAnchor.MiddleLeft);
            HauntedSkin.Label(new Rect(card.xMax - 60, card.y + 7, 48, 15), "No. " + HauntedTrayLayout.PlateNumber(System.Array.IndexOf(trayConfig, pick)), HauntedSkin.SmallCaps(11), HauntedSkin.BloodRed, TextAnchor.MiddleRight);
            HauntedSkin.Fill(new Rect(x, card.y + 24, w, 1), Alpha(HauntedSkin.BloodRed, .5f), 0);
            HauntedSkin.Label(new Rect(x, card.y + 24, w, 29), pick.name, HauntedSkin.Gothic(24), HauntedSkin.Ink, TextAnchor.MiddleLeft);
            string damage = string.IsNullOrEmpty(pick.damageType) || pick.damageType == "none" ? TypeNames[System.Array.IndexOf(TypeIds, CategoryOf(pick))] : char.ToUpperInvariant(pick.damageType[0]) + pick.damageType.Substring(1);
            HauntedSkin.Label(new Rect(x, card.y + 52, w, 16), damage + " · " + RoleLine(pick), HauntedSkin.Typed(12), HauntedSkin.BloodRed, TextAnchor.MiddleLeft);
            HauntedSkin.Label(new Rect(x, card.y + 69, w, 34), pick.flavor ?? "", HauntedSkin.Serif(14, true), new Color32(67, 48, 31, 255), TextAnchor.UpperLeft);
            if (!trayChips.TryGetValue(pick.id, out var chips)) { chips = Chips(pick); trayChips.Add(pick.id, chips); }
            float cellW = w / chips.Count;
            for (int i = 0; i < chips.Count; i++)
            {
                float cx = x + i * cellW;
                if (i > 0) HauntedSkin.Fill(new Rect(cx - 4, card.y + 104, 1, 25), Alpha(HauntedSkin.Ink, .22f), 0);
                string value = chips[i], caption = "EFFECT";
                if (value.EndsWith("dmg/s")) caption = "DAMAGE";
                else if (value.EndsWith(" tiles")) caption = "RANGE";
                else if (value.StartsWith("door support ")) { caption = "SUPPORT"; value = value.Substring(13); }
                else if (value.StartsWith("slows ")) { caption = "SLOW"; value = value.Substring(6); }
                else if (value.Contains("/s")) caption = "INCOME";
                HauntedSkin.Label(new Rect(cx, card.y + 100, cellW - 5, 12), caption, HauntedSkin.SmallCaps(11), new Color32(107, 82, 64, 255), TextAnchor.MiddleLeft);
                HauntedSkin.Label(new Rect(cx, card.y + 112, cellW - 5, 19), value, HauntedSkin.Typed(13), HauntedSkin.Ink, TextAnchor.MiddleLeft);
            }
        }

        void DrawHauntedClose(Rect r)
        {
            float age = HauntedClock.Now - closePressedAt;
            float flap = age < .2f ? 1 - .3f * Mathf.Sin(age / .2f * Mathf.PI) : 1;
            HauntedSkin.Tex(new Rect(r.center.x - 60, r.center.y - 23 * flap, 120, 46 * flap), HauntedSkin.Texture("bat_wings_close"), Color.white);
            var knob = age < .08f ? new Rect(r.center - r.size * .46f, r.size * .92f) : r;
            HauntedSkin.Tex(knob, HauntedSkin.Texture("knob"), Color.white);
            HauntedSkin.Tex(new Rect(knob.center.x - 10, knob.center.y - 10, 20, 20), DreamSkin.Icon("close"), HauntedSkin.Bone);
            if (windowClosingAt < 0 && summonPressedAt < 0) TapZone(new Rect(r.center.x - 30, r.center.y - 30, 60, 60), () => { closePressedAt = HauntedClock.Now; CloseWindow(); });
        }

        /// <summary>Dragging a card: the summon follows the finger to the nearest free plate in the room.</summary>
        void DragCard(Room room, string id, Vector2 g)
        {
            if (summonPressedAt >= 0 || windowClosingAt >= 0) return;
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
