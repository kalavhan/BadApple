using System.Linq;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// The monster's windows. Tapping the monster opens a ring of stat orbs around it, like a tower's ring: one tap on
    /// an orb spends the Fear for the next level and puts the point into that stat, with what it adds written under
    /// the orb. Ability ranks and utility tricks owed at some levels appear in the same ring as gold orbs.
    /// The Horde button opens one big window: the resistance types across the top tint the whole window, three tabs
    /// down the side are the minion's three forms, and the current form's upgrades sit in a ring around it.
    /// </summary>
    public partial class GameHUD
    {
        const string FearHex = "#FF6FAE";
        static readonly Color FearPink = new Color32(0xFF, 0x6F, 0xAE, 255);
        static readonly string[] Romans = { "I", "II", "III" };
        static readonly string[] KitSlots = { "attack", "area", "special" };
        static readonly string[] KitIcons = { "kit_attack", "kit_area", "kit_special" };
        static readonly string[] HordeStats = { "horde", "toughness", "fangs", "scurry", "frenzy", "hide" };

        bool monsterRing, hordeOpen;
        float monsterRingAt = -9f, hordeOpenedAt = -9f, monsterShakeAt = -9f;
        int hordeTab = -1;

        static Color StatColor(string id)
        {
            switch (id)
            {
                case "vitality": case "toughness": return new Color32(0xFF, 0x7A, 0x8E, 255);
                case "hide": return new Color32(0x9C, 0xC2, 0xFF, 255);
                case "stride": case "scurry": return DreamSkin.Mint;
                case "frenzy": return new Color32(0xF2, 0xC1, 0x4E, 255);
                case "maw": case "fangs": return new Color32(0xFF, 0x8A, 0x4C, 255);
                default: return DreamSkin.Violet;
            }
        }

        static string StatIcon(string id)
        {
            switch (id)
            {
                case "vitality": case "toughness": return "stat_vitality";
                case "hide": return "stat_hide";
                case "stride": case "scurry": return "stat_stride";
                case "frenzy": return "stat_frenzy";
                case "maw": case "fangs": return "stat_maw";
                default: return "stat_horde";
            }
        }

        /// <summary>Thick hide shows in the colour of the resistance it thickens.</summary>
        static Color ResistColor(int type)
        {
            switch (type)
            {
                case DamageTypes.Bullet: return new Color32(0xF2, 0xC1, 0x4E, 255);
                case DamageTypes.Electric: return new Color32(0x6E, 0xC8, 0xFF, 255);
                case DamageTypes.Fire: return new Color32(0xFF, 0x6A, 0x3D, 255);
                default: return DreamSkin.Violet;
            }
        }

        static string ResistIcon(int type) => type == DamageTypes.Bullet ? "type_bullets" : type == DamageTypes.Electric ? "type_electric" : "type_fire";

        static float Ease(float since, float seconds = .24f)
        {
            float k = Mathf.Clamp01((Time.unscaledTime - since) / seconds);
            return 1f - Mathf.Pow(1f - k, 3f);
        }

        static Color A(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);

        void ReportFear(ActionResult r, float need = 0f)
        {
            switch (r)
            {
                case ActionResult.NoMoney: gm.Toast(need > 0 ? "Need " + Mathf.CeilToInt(need) + " more Fear. Hit residents, eat body parts, or lurk." : "Not enough Fear."); break;
                case ActionResult.MaxLevel: gm.Toast("Already maxed."); break;
                case ActionResult.Blocked: gm.Toast("Not available yet."); break;
                case ActionResult.Invalid: gm.Toast("Can't do that right now."); break;
            }
        }

        // ------------------------------------------------------------ frame

        void DrawMonsterUI()
        {
            var m = gm.Monster;
            if (m == null) return;
            DrawMonsterChip(m);

            if (m.Dead)
            {
                var r = new Rect(vw / 2 - 200, VH / 2 - 30, 400, 60);
                GUI.Box(r, GUIContent.none, box);
                Ui(r);
                GUI.Label(r, "Banished! Back in " + Mathf.CeilToInt(Mathf.Max(0f, m.RespawnAt - gm.Now)) + " s", center);
                monsterRing = false;
            }

            DrawJoystick();

            bool night = gm.Phase == Phase.Night;
            for (int i = 0; i < m.Loadout.Length; i++)
            {
                var a = m.Loadout[i];
                float cd = m.Cooldowns[i];
                int idx = i;
                var c = new Vector2(vw - 75f - (i % 3) * 110f, VH - 75f - (i / 3) * 108f);
                string text = a.name + "\n<size=11>" + AbilityHint(a) + "</size>" + (cd > 0 ? "\n" + Mathf.CeilToInt(cd) + "s" : "\n[" + (i + 1) + "]");
                RoundButton(c, 100f, text, i < 2 ? gm.ColorOf(m) : Mint, cd <= 0f && night && !m.Dead, () => gm.UseAbility(idx), roundSmall);
            }
            if (gm.HotelViewAvailable)
                RoundButton(new Vector2(vw - 80f, VH - 310f), 72f, gm.HotelView ? "Back" : "Hotel\nview", Bone, true, () => gm.HotelView = !gm.HotelView, roundSmall);
            DrawHordeButton(m);

            if (!m.Dead && !hordeOpen)
            {
                if (monsterRing) DrawMonsterRing(m);
                else DrawGrowHint(m);
            }
            if (hordeOpen) DrawHordeWindow(m);
        }

        /// <summary>Top left: level, health and Fear. Tapping it opens the ring too.</summary>
        void DrawMonsterChip(Monster m)
        {
            var r = new Rect(10, 146, 290, 82);
            DreamSkin.Panel(r);
            Ui(r);
            DreamSkin.Label(new Rect(r.x + 14, r.y + 8, r.width - 28, 26), "<b>Lv " + m.Level + "</b>  " + m.Def.name, DreamSkin.Body, DreamSkin.Bone, TextAnchor.MiddleLeft);
            Bar(new Rect(r.x + 14, r.y + 36, r.width - 28, 9), m.Dead ? 0f : m.Hp / gm.MaxHp(m), Red);
            DreamSkin.Icon(new Rect(r.x + 12, r.y + 50, 26, 26), "fear", Color.white);
            float price = gm.LevelPrice(m);
            string next = price < 0 ? "max level" : m.Fear >= price ? "<color=" + FearHex + ">tap yourself to grow</color>" : "next level " + Mathf.CeilToInt(price);
            DreamSkin.Label(new Rect(r.x + 42, r.y + 50, r.width - 56, 26), "<b><color=" + FearHex + ">" + Mathf.FloorToInt(m.Fear) + "</color></b>   <size=12>" + next + "</size>", DreamSkin.Body, DreamSkin.BoneDim, TextAnchor.MiddleLeft);
            TapZone(r, () => ToggleMonsterRing(true));
        }

        void DrawHordeButton(Monster m)
        {
            var c = new Vector2(78f + Screen.safeArea.xMin / scale, VH - 262f);
            float d = 92f;
            bool awake = gm.HordeAwake(m);
            var tint = awake ? ResistColor(m.MinionResist) : DreamSkin.Violet;
            bool afford = !awake && m.Fear >= gm.MinionUpgradeCost(m, "awaken");
            DreamSkin.Orb(c, d, tint, .9f, hordeOpen || afford ? .3f + .1f * Mathf.Sin(Time.unscaledTime * 3f) : .08f);
            DreamSkin.Icon(new Rect(c.x - 20f, c.y - 30f, 40f, 40f), awake ? "minion_" + gm.MinionLine(m).id : "stat_horde", tint);
            DreamSkin.Label(new Rect(c.x - 46f, c.y + 10f, 92f, 20f), "<b>Horde</b>", DreamSkin.Tiny, DreamSkin.Bone);
            if (awake) DreamSkin.Label(new Rect(c.x - 46f, c.y + 26f, 92f, 16f), gm.Minions.Count + " out", DreamSkin.Tiny, DreamSkin.BoneDim);
            var r = new Rect(c.x - d / 2f, c.y - d / 2f, d, d);
            Ui(r);
            TapZone(r, () => ToggleHorde(!hordeOpen), round: true);
        }

        void ToggleMonsterRing(bool open)
        {
            if (open == monsterRing) return;
            monsterRing = open;
            if (open) { monsterRingAt = Time.unscaledTime; hordeOpen = false; }
        }

        void ToggleHorde(bool open)
        {
            if (open == hordeOpen) return;
            hordeOpen = open;
            if (open) { hordeOpenedAt = Time.unscaledTime; monsterRing = false; hordeTab = -1; }
        }

        /// <summary>World taps while playing the monster: the monster's own body opens or closes its ring; anywhere else closes the windows.</summary>
        void MonsterWorldTap(Vector2 g)
        {
            var m = gm.Monster;
            if (m == null || m.Dead) return;
            if (hordeOpen) { ToggleHorde(false); return; }
            if (MonsterBox(m).Contains(g)) ToggleMonsterRing(!monsterRing);
            else ToggleMonsterRing(false);
        }

        /// <summary>The monster's on-screen box (model or sprite bounds), a little padded for fingers.</summary>
        Rect MonsterBox(Monster m)
        {
            Bounds b;
            var model = m.Model != null ? m.Model.GetComponentInChildren<Renderer>() : null;
            if (model != null) b = model.bounds;
            else if (m.Sr != null && m.Sr.sprite != null) b = m.Sr.bounds;
            else
            {
                var feet = WorldToGui(m.Pos);
                return new Rect(feet.x - GuiPerTile * .7f, feet.y - GuiPerTile * 2.2f, GuiPerTile * 1.4f, GuiPerTile * 2.4f);
            }
            float xMin = float.MaxValue, xMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                var sp = gm.Cam.WorldToScreenPoint(corner);
                var gp = new Vector2(sp.x / scale, (Screen.height - sp.y) / scale);
                xMin = Mathf.Min(xMin, gp.x); xMax = Mathf.Max(xMax, gp.x); yMin = Mathf.Min(yMin, gp.y); yMax = Mathf.Max(yMax, gp.y);
            }
            const float pad = 18f;
            return Rect.MinMaxRect(xMin - pad, yMin - pad, xMax + pad, yMax + pad);
        }

        /// <summary>When a level is affordable or a pick is owed, a glow at the monster's feet and a bobbing badge over its head.</summary>
        void DrawGrowHint(Monster m)
        {
            float price = gm.LevelPrice(m);
            bool owed = m.Choices.Count > 0;
            bool ready = owed || (price >= 0 && m.Fear >= price);
            if (!ready || gm.Cam == null) return;
            var box = MonsterBox(m);
            var feet = WorldToGui(m.Pos);
            float pulse = .5f + .5f * Mathf.Sin(Time.unscaledTime * 3.2f);
            var col = owed ? (Color)Candle : FearPink;
            UiFx.Ellipse(feet, GuiPerTile * .9f, GuiPerTile * .9f * .766f, A(col, .35f + .35f * pulse), 2.2f, Time.unscaledTime * .2f, scale);
            var badge = new Vector2(box.center.x, box.yMin + 6f - 6f * pulse);
            DreamSkin.Orb(badge, 40f, col, .95f, .25f + .2f * pulse);
            DreamSkin.Icon(new Rect(badge.x - 11f, badge.y - 11f, 22f, 22f), owed ? "kit_special" : "levelup", col);
            var r = new Rect(badge.x - 22f, badge.y - 22f, 44f, 44f);
            Ui(r);
            TapZone(r, () => ToggleMonsterRing(true), round: true);
        }

        // ------------------------------------------------------------ the monster ring

        void DrawMonsterRing(Monster m)
        {
            if (gm.Cam == null) return;
            float k = Ease(monsterRingAt);
            var box = MonsterBox(m);
            var center = box.center;
            float R = Mathf.Clamp(box.height * .5f + 92f, 150f, 205f) * (.85f + .15f * k);
            float shake = Time.unscaledTime - monsterShakeAt < .35f ? Mathf.Sin((Time.unscaledTime - monsterShakeAt) * 60f) * 7f * (1f - (Time.unscaledTime - monsterShakeAt) / .35f) : 0f;
            center.x += shake;
            var safe = SafeRect();
            center.y = Mathf.Clamp(center.y, safe.yMin + R + 60f, safe.yMax - R - 90f);

            var choice = m.Choices.Count > 0 && m.Choices.Peek().Kind != "Stat" ? m.Choices.Peek() : null;
            DreamSkin.GlowAt(center, Vector2.one * R * 2.6f, new Color(0, 0, 0, .35f * k));
            DreamSkin.Ring(center, R * 2f, 1.6f, A(choice != null ? (Color)Candle : FearPink, .3f * k));

            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, k);
            if (choice != null) DrawPickRing(m, choice, center, R, k);
            else DrawStatRing(m, center, R, k);
            GUI.color = old;
        }

        Vector2 OrbAt(Vector2 center, float R, int i, int n)
        {
            float a = (-90f + i * 360f / n) * Mathf.Deg2Rad;
            return center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R;
        }

        void DrawStatRing(Monster m, Vector2 center, float R, float k)
        {
            var tracks = gm.Cfg.progression.statTracks;
            float price = gm.LevelPrice(m);
            bool owed = m.Choices.Count > 0 && m.Choices.Peek().Kind == "Stat";
            bool afford = owed || (price >= 0 && m.Fear + .001f >= price);
            // Heading under the ring: the price of the level, or how much is missing.
            string head = owed ? "<b>Free stat point</b>  ·  tap a stat" : price < 0 ? "<b>Max level</b>" :
                afford ? "<b>Level " + (m.Level + 1) + "</b>  ·  <color=" + FearHex + ">" + Mathf.CeilToInt(price) + " Fear</color>  ·  tap a stat"
                       : "<b>Level " + (m.Level + 1) + "</b>  ·  need <color=" + FearHex + ">" + Mathf.CeilToInt(price - m.Fear) + "</color> more Fear";
            var hr = new Rect(center.x - 190f, center.y + R + 52f, 380f, 34f);
            DreamSkin.Fill(hr, new Color(.06f, .045f, .09f, .9f), 17f);
            DreamSkin.Icon(new Rect(hr.x + 10f, hr.y + 5f, 24f, 24f), "fear", Color.white);
            DreamSkin.Label(new Rect(hr.x + 38f, hr.y, hr.width - 46f, hr.height), head, DreamSkin.Small, DreamSkin.Bone, TextAnchor.MiddleCenter);
            Ui(hr);

            for (int i = 0; i < tracks.Length; i++)
            {
                var s = tracks[i];
                int rank = m.StatRanks[i];
                bool maxed = rank >= s.maxRank;
                bool ok = afford && !maxed;
                var c = OrbAt(center, R, i, tracks.Length);
                var col = StatColor(s.id);
                float d = 94f * (.8f + .2f * k);
                DreamSkin.Orb(c, d, col, ok ? .95f : .3f, ok ? .2f + .08f * Mathf.Sin(Time.unscaledTime * 3f + i) : 0f);
                DreamSkin.Icon(new Rect(c.x - 19f, c.y - 30f, 38f, 38f), StatIcon(s.id), ok ? col : A(col, .45f));
                DreamSkin.Label(new Rect(c.x - 48f, c.y + 10f, 96f, 18f), "<b>" + s.name + "</b>", DreamSkin.Tiny, ok ? DreamSkin.Bone : DreamSkin.BoneDim);
                // Under the orb: what it adds, and its rank as pips.
                string gain = maxed ? "maxed" : (s.id == "hide" ? "−" : "+") + Mathf.RoundToInt(s.perRank * 100f) + "% " + s.stat.ToLower().Replace("hp", "HP");
                DreamSkin.Shadowed(new Rect(c.x - 70f, c.y + d / 2f + 2f, 140f, 18f), gain, DreamSkin.Tiny, maxed ? DreamSkin.BoneDim : col, TextAnchor.MiddleCenter);
                Pips(new Vector2(c.x, c.y + d / 2f + 26f), rank, s.maxRank, col);
                var r = new Rect(c.x - d / 2f, c.y - d / 2f, d, d);
                Ui(r);
                string id = s.id;
                Vector2 at = c;
                TapZone(r, () => BuyStat(m, id, at), round: true);
            }
        }

        void Pips(Vector2 c, int on, int total, Color col)
        {
            float w = 9f, gap = 4f, x = c.x - (total * w + (total - 1) * gap) / 2f;
            for (int i = 0; i < total; i++)
            {
                var r = new Rect(x + i * (w + gap), c.y - 3f, w, 6f);
                DreamSkin.Fill(r, i < on ? col : new Color(1, 1, 1, .14f), 3f);
            }
        }

        void BuyStat(Monster m, string id, Vector2 at)
        {
            float price = gm.LevelPrice(m);
            var s = gm.Cfg.progression.statTracks.First(x => x.id == id);
            var res = gm.TryBuyLevelInto(m, id);
            if (res != ActionResult.Ok)
            {
                monsterShakeAt = Time.unscaledTime;
                ReportFear(res, price - m.Fear);
                return;
            }
            var col = StatColor(id);
            fxFront.Burst(at, 18, col, 160f, 8f, .7f);
            fxFront.Ring(at, 60f, col);
            var box = MonsterBox(m);
            fxFront.Say(new Vector2(box.center.x, box.yMin - 24f), "LEVEL " + m.Level, s.name.ToUpper() + " " + (s.id == "hide" ? "−" : "+") + Mathf.RoundToInt(s.perRank * 100f) + "%");
            // A pick owed at this level keeps the ring open; otherwise one tap was the whole action.
            if (!gm.HasPendingPick(m) && (gm.LevelPrice(m) < 0 || m.Fear < gm.LevelPrice(m))) ToggleMonsterRing(false);
        }

        /// <summary>Ability ranks and utility tricks owed at some levels, as gold orbs in the same ring.</summary>
        void DrawPickRing(Monster m, ProgressChoice choice, Vector2 center, float R, float k)
        {
            string head = choice.Kind == "Rank" ? "<b>Sharpen an ability</b>  ·  free with level " + choice.Level : "<b>Learn a trick</b>  ·  free with level " + choice.Level;
            var hr = new Rect(center.x - 170f, center.y + R + 52f, 340f, 34f);
            DreamSkin.Fill(hr, new Color(.06f, .045f, .09f, .9f), 17f);
            DreamSkin.Label(hr, head, DreamSkin.Small, Candle, TextAnchor.MiddleCenter);
            Ui(hr);
            int n = choice.Options.Length;
            for (int i = 0; i < n; i++)
            {
                string option = choice.Options[i];
                var c = OrbAt(center, R, i, Mathf.Max(3, n));
                float d = 104f * (.8f + .2f * k);
                DreamSkin.Orb(c, d, Candle, .95f, .22f + .08f * Mathf.Sin(Time.unscaledTime * 3f + i));
                string icon, name, gain;
                if (choice.Kind == "Rank")
                {
                    int slot = System.Array.IndexOf(KitSlots, option);
                    icon = KitIcons[slot];
                    name = gm.KitName(m, slot) + " " + Romans[Mathf.Min(2, m.KitRanks[slot])];
                    gain = "+" + Mathf.RoundToInt((gm.RankDamage(m.KitRanks[slot] + 1) / gm.RankDamage(m.KitRanks[slot]) - 1) * 100f) + "% damage" + (slot > 0 ? ", faster" : "");
                }
                else
                {
                    var a = gm.Cfg.abilities.abilities.First(x => x.id == option);
                    icon = "trick"; name = a.name; gain = AbilityHint(a).ToLower();
                }
                DreamSkin.Icon(new Rect(c.x - 19f, c.y - 32f, 38f, 38f), icon, Candle);
                DreamSkin.Label(new Rect(c.x - 52f, c.y + 8f, 104f, 34f), "<b>" + name + "</b>", DreamSkin.Tiny, DreamSkin.Bone);
                DreamSkin.Shadowed(new Rect(c.x - 80f, c.y + d / 2f + 2f, 160f, 18f), gain, DreamSkin.Tiny, Candle, TextAnchor.MiddleCenter);
                var r = new Rect(c.x - d / 2f, c.y - d / 2f, d, d);
                Ui(r);
                int index = i;
                Vector2 at = c;
                TapZone(r, () =>
                {
                    gm.ChooseProgression(index);
                    fxFront.Burst(at, 18, Candle, 160f, 8f, .7f);
                    if (!gm.HasPendingPick(m) && (gm.LevelPrice(m) < 0 || m.Fear < gm.LevelPrice(m))) ToggleMonsterRing(false);
                }, round: true);
            }
        }

        // ------------------------------------------------------------ the horde window

        Rect HordeRect()
        {
            var safe = SafeRect();
            float w = Mathf.Min(860f, safe.width - 40f), h = Mathf.Min(560f, safe.height - 40f);
            return new Rect(safe.center.x - w / 2f, safe.center.y - h / 2f + 10f, w, h);
        }

        void DrawHordeWindow(Monster m)
        {
            float k = Ease(hordeOpenedAt);
            var line = gm.MinionLine(m);
            bool awake = gm.HordeAwake(m);
            int form = gm.MinionFormIndex(m);
            if (hordeTab < 0 || hordeTab >= line.forms.Length) hordeTab = form;
            var tint = ResistColor(m.MinionResist);

            var r = HordeRect();
            r.y += (1f - k) * 40f;
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, k);
            DreamSkin.Panel(r, k);
            // The window takes the colour of the horde's resistance.
            DreamSkin.GlowAt(new Vector2(r.center.x, r.y + 40f), new Vector2(r.width * 1.1f, 220f), A(tint, .22f));
            DreamSkin.GlowAt(r.center, new Vector2(r.width * .9f, r.height * .9f), A(tint, .08f));
            DreamSkin.Border(r, A(tint, .7f), 2f, 18f);
            Ui(r);
            CloseButton(new Rect(r.xMax - 58f, r.y + 12f, 46f, 46f), () => ToggleHorde(false));

            // ---- the types across the top
            DreamSkin.Label(new Rect(r.x + 22f, r.y + 14f, 200f, 40f), "Horde", DreamSkin.Title, tint, TextAnchor.MiddleLeft);
            bool canSwap = gm.CanSwapMinionResist(m);
            float tx = r.x + 190f;
            DreamSkin.Label(new Rect(tx, r.y + 14f, 80f, 40f), "Resists", DreamSkin.Small, DreamSkin.BoneDim, TextAnchor.MiddleLeft);
            tx += 70f;
            for (int i = 0; i < 3; i++)
            {
                int type = i;
                bool on = m.MinionResist == type;
                var col = ResistColor(type);
                var b = new Rect(tx + i * 132f, r.y + 12f, 124f, 44f);
                bool usable = canSwap || on;
                if (on) { DreamSkin.GlowAt(b.center, b.size * 1.5f, A(col, .3f)); DreamSkin.Fill(b, col, 22f); }
                else DreamSkin.Fill(b, new Color(1, 1, 1, usable ? .06f : .03f), 22f);
                DreamSkin.Border(b, A(col, usable ? .7f : .25f), 1.4f, 22f);
                DreamSkin.Icon(new Rect(b.x + 14f, b.y + 10f, 24f, 24f), ResistIcon(type), on ? DreamSkin.Ink : A(col, usable ? 1f : .4f));
                DreamSkin.Label(new Rect(b.x + 42f, b.y, b.width - 46f, b.height), "<b>" + DamageTypes.Label(type) + "</b>", DreamSkin.Small, on ? DreamSkin.Ink : A(DreamSkin.Bone, usable ? 1f : .4f), TextAnchor.MiddleLeft);
                TapZone(b, () =>
                {
                    var res = gm.TrySetMinionResist(m, type);
                    if (res == ActionResult.Blocked) gm.Toast("You can change the resistance in " + gm.NightsUntilSwap(m) + " night(s).");
                });
            }
            string typeNote = m.MinionResist < 0 ? "pick what your minions shrug off"
                : "take " + Mathf.RoundToInt(gm.MinionResistance(m) * 100f) + "% less " + DamageTypes.Label(m.MinionResist).ToLower() + ", weak to " + DamageTypes.Label(GameManager.Weakness(m.MinionResist)).ToLower() +
                  (canSwap ? "" : "  ·  change in " + gm.NightsUntilSwap(m) + " night(s)");
            DreamSkin.Label(new Rect(tx, r.y + 58f, 400f, 20f), typeNote, DreamSkin.Tiny, DreamSkin.BoneDim, TextAnchor.MiddleLeft);

            // ---- the three forms as tabs down the left
            for (int i = 0; i < line.forms.Length; i++)
            {
                int tab = i;
                var b = new Rect(r.x + 18f, r.y + 96f + i * 132f, 112f, 122f);
                bool on = hordeTab == i;
                bool owned = awake && i <= form;
                var col = owned ? tint : DreamSkin.BoneDim;
                if (on) { DreamSkin.GlowAt(b.center, b.size * 1.4f, A(col, .25f)); DreamSkin.Fill(b, A(col, .22f), 16f); }
                else DreamSkin.Fill(b, new Color(1, 1, 1, .04f), 16f);
                DreamSkin.Border(b, A(col, on ? .9f : .3f), on ? 2f : 1.2f, 16f);
                float iconSize = 44f + i * 8f;
                DreamSkin.Icon(new Rect(b.center.x - iconSize / 2f, b.y + 14f + (60f - iconSize) / 2f, iconSize, iconSize), "minion_" + line.id, owned ? col : A(col, .35f));
                DreamSkin.Label(new Rect(b.x + 4f, b.y + 76f, b.width - 8f, 20f), "<b>" + Romans[i] + "</b>", DreamSkin.Small, owned ? DreamSkin.Bone : DreamSkin.BoneDim, TextAnchor.MiddleCenter);
                DreamSkin.Label(new Rect(b.x + 4f, b.y + 96f, b.width - 8f, 20f), line.forms[i].name, DreamSkin.Tiny, owned ? DreamSkin.Bone : DreamSkin.BoneDim);
                TapZone(b, () => hordeTab = tab);
            }

            // ---- the middle: awaken, the current form's ring, or the next evolution
            var area = new Rect(r.x + 150f, r.y + 90f, r.width - 170f, r.height - 140f);
            var mid = new Vector2(area.center.x, area.center.y - 6f);
            if (!awake) DrawAwaken(m, line, mid, k);
            else if (hordeTab == form) DrawFormRing(m, line, form, mid, Mathf.Min(area.height * .5f - 54f, 176f), tint, k);
            else if (hordeTab == form + 1) DrawEvolve(m, line, hordeTab, mid, tint);
            else DrawFormInfo(line, hordeTab, mid, hordeTab < form ? "an earlier form" : "Evolve to form " + Romans[Mathf.Min(2, form + 1)] + " first");

            // ---- footer: tonight and what the monster has seen
            var tally = gm.ScoutTally(m, out int unknown);
            string foot = (awake ? "<b>" + gm.HordeSize(m) + "</b> per door each night  ·  <b>" + gm.Minions.Count + "</b> out now      " : "") +
                "Towers seen: bullet " + tally[0] + "  electric " + tally[1] + "  fire " + tally[2] + (unknown > 0 ? "   <color=#8A8070>? " + unknown + " room" + (unknown > 1 ? "s" : "") + " unseen</color>" : "");
            DreamSkin.Label(new Rect(r.x + 150f, r.yMax - 40f, r.width - 170f, 26f), foot, DreamSkin.Small, DreamSkin.BoneDim, TextAnchor.MiddleCenter);
            GUI.color = old;
        }

        void DrawAwaken(Monster m, Config.MinionLineDef line, Vector2 c, float k)
        {
            float cost = gm.MinionUpgradeCost(m, "awaken");
            bool afford = m.Fear + .001f >= cost;
            float d = 168f;
            DreamSkin.Orb(c, d, DreamSkin.Violet, .95f, afford ? .3f + .12f * Mathf.Sin(Time.unscaledTime * 3f) : .05f);
            DreamSkin.Icon(new Rect(c.x - 34f, c.y - 56f, 68f, 68f), "minion_" + line.id, afford ? DreamSkin.Violet : A(DreamSkin.Violet, .5f));
            DreamSkin.Label(new Rect(c.x - 80f, c.y + 16f, 160f, 26f), "<b>Awaken</b>", DreamSkin.Body, DreamSkin.Bone, TextAnchor.MiddleCenter);
            DreamSkin.Label(new Rect(c.x - 80f, c.y + 42f, 160f, 20f), "<color=" + FearHex + ">" + Mathf.CeilToInt(cost) + " Fear</color>", DreamSkin.Tiny, DreamSkin.Bone);
            DreamSkin.Label(new Rect(c.x - 230f, c.y + d / 2f + 14f, 460f, 44f),
                "Opens a rift outside every resident's door. Each night it releases " + gm.Cfg.minions.baseHorde + " " + line.forms[0].name + "s per door.",
                DreamSkin.Small, DreamSkin.BoneDim, TextAnchor.MiddleCenter);
            var r = new Rect(c.x - d / 2f, c.y - d / 2f, d, d);
            Ui(r);
            TapZone(r, () =>
            {
                var res = gm.TryBuyMinionUpgrade(m, "awaken");
                if (res == ActionResult.Ok) { fxFront.Burst(c, 30, DreamSkin.Violet, 200f, 9f, .8f); hordeTab = 0; }
                else ReportFear(res, cost - m.Fear);
            }, round: true);
        }

        static string Trait(Config.MinionFormDef f)
        {
            switch (f.trait)
            {
                case "dropPart": return "drops a body part when it breaks a door";
                case "deathSlow": return "pops into a slowing puff";
                case "deathBlind": return "its puff also blinds towers for 1 s";
                case "split": return "splits into two Puffcaps when it dies";
                case "swallowShot": return "swallows the first tower shot";
                case "releaseTwo": return "releases two Mimics when it breaks";
                default: return "";
            }
        }

        string FormStats(Monster m, Config.MinionFormDef f) =>
            Mathf.RoundToInt(f.health * (1f + gm.MinionRank(m, "toughness") * gm.MinionUpgrade("toughness").perRank)) + " HP  ·  " +
            Mathf.RoundToInt(f.damage * (1f + gm.MinionRank(m, "fangs") * gm.MinionUpgrade("fangs").perRank)) + " dmg  ·  speed " + f.speed.ToString("0.#");

        void DrawFormRing(Monster m, Config.MinionLineDef line, int form, Vector2 c, float R, Color tint, float k)
        {
            var f = line.forms[form];
            DreamSkin.GlowAt(c, Vector2.one * 190f, A(tint, .18f));
            DreamSkin.Icon(new Rect(c.x - 44f, c.y - 58f, 88f, 88f), "minion_" + line.id, tint);
            DreamSkin.Label(new Rect(c.x - 110f, c.y + 30f, 220f, 26f), "<b>" + f.name + "</b>", DreamSkin.Body, DreamSkin.Bone, TextAnchor.MiddleCenter);
            DreamSkin.Label(new Rect(c.x - 110f, c.y + 54f, 220f, 18f), FormStats(m, f), DreamSkin.Tiny, DreamSkin.BoneDim);
            string trait = Trait(f);
            if (trait != "") DreamSkin.Label(new Rect(c.x - 110f, c.y + 72f, 220f, 18f), trait, DreamSkin.Tiny, tint);
            DreamSkin.Ring(c, R * 2f, 1.4f, A(tint, .25f));

            for (int i = 0; i < HordeStats.Length; i++)
            {
                string id = HordeStats[i];
                var u = gm.MinionUpgrade(id);
                int rank = gm.MinionRank(m, id);
                float cost = gm.MinionUpgradeCost(m, id);
                bool maxed = cost < 0;
                bool ok = !maxed && m.Fear + .001f >= cost;
                var p = OrbAt(c, R, i, HordeStats.Length);
                var col = id == "hide" ? tint : StatColor(id);
                float d = 84f * (.8f + .2f * k);
                DreamSkin.Orb(p, d, col, ok ? .95f : .3f, ok ? .2f + .08f * Mathf.Sin(Time.unscaledTime * 3f + i) : 0f);
                DreamSkin.Icon(new Rect(p.x - 17f, p.y - 27f, 34f, 34f), StatIcon(id), ok ? col : A(col, .45f));
                DreamSkin.Label(new Rect(p.x - 44f, p.y + 8f, 88f, 18f), "<b>" + (id == "horde" ? "Horde" : u.name) + "</b>", DreamSkin.Tiny, ok ? DreamSkin.Bone : DreamSkin.BoneDim);
                string gain = maxed ? "maxed" : id == "horde" ? "+1 per door" : id == "hide" ? Mathf.RoundToInt(gm.Cfg.minions.resistByRank[Mathf.Min(rank + 1, gm.Cfg.minions.resistByRank.Length - 1)] * 100f) + "% resist" :
                    "+" + Mathf.RoundToInt(u.perRank * 100f) + "% " + (id == "toughness" ? "HP" : id == "fangs" ? "damage" : id == "scurry" ? "speed" : "attack speed");
                DreamSkin.Shadowed(new Rect(p.x - 95f, p.y + d / 2f, 190f, 16f), gain + (maxed ? "" : "  ·  <color=" + FearHex + ">" + Mathf.CeilToInt(cost) + "</color>"), DreamSkin.Tiny, maxed ? DreamSkin.BoneDim : col, TextAnchor.MiddleCenter);
                Pips(new Vector2(p.x, p.y + d / 2f + 22f), rank, id == "horde" ? gm.Cfg.minions.maxHorde - gm.Cfg.minions.baseHorde : u.costs.Length, col);
                var r = new Rect(p.x - d / 2f, p.y - d / 2f, d, d);
                Ui(r);
                Vector2 at = p;
                TapZone(r, () =>
                {
                    var res = gm.TryBuyMinionUpgrade(m, id);
                    if (res == ActionResult.Ok) { fxFront.Burst(at, 16, col, 150f, 7f, .6f); fxFront.Ring(at, 50f, col); }
                    else ReportFear(res, cost - m.Fear);
                }, round: true);
            }
        }

        void DrawEvolve(Monster m, Config.MinionLineDef line, int next, Vector2 c, Color tint)
        {
            float cost = gm.MinionUpgradeCost(m, "evolve");
            string lockReason = gm.MinionUpgradeLock(m, "evolve");
            bool ok = cost >= 0 && lockReason == null && m.Fear + .001f >= cost;
            float d = 168f;
            DreamSkin.Orb(c, d, DreamSkin.Violet, ok ? .95f : .35f, ok ? .3f + .12f * Mathf.Sin(Time.unscaledTime * 2.8f) : .04f);
            DreamSkin.Icon(new Rect(c.x - 40f, c.y - 56f, 80f, 52f), ok ? "evolve_body" : "evolve_dim", Color.white);
            DreamSkin.Label(new Rect(c.x - 80f, c.y + 8f, 160f, 26f), "<b>Evolve</b>", DreamSkin.Body, ok ? DreamSkin.Mint : DreamSkin.BoneDim, TextAnchor.MiddleCenter);
            int needRanks = gm.MinionUpgrade("evolve").requiresRanks[Mathf.Min(next - 1, gm.MinionUpgrade("evolve").requiresRanks.Length - 1)];
            string sub = lockReason != null ? gm.CoreMinionRanks(m) + " / " + needRanks + " upgrades" : "<color=" + FearHex + ">" + Mathf.CeilToInt(cost) + " Fear</color>";
            DreamSkin.Label(new Rect(c.x - 80f, c.y + 34f, 160f, 20f), sub, DreamSkin.Tiny, DreamSkin.Bone);
            var f = line.forms[next];
            DreamSkin.Label(new Rect(c.x - 230f, c.y + d / 2f + 12f, 460f, 24f), "<b>" + f.name + "</b>  ·  " + FormStats(m, f), DreamSkin.Small, DreamSkin.Bone, TextAnchor.MiddleCenter);
            string trait = Trait(f);
            if (trait != "") DreamSkin.Label(new Rect(c.x - 230f, c.y + d / 2f + 36f, 460f, 20f), trait, DreamSkin.Small, tint, TextAnchor.MiddleCenter);
            var r = new Rect(c.x - d / 2f, c.y - d / 2f, d, d);
            Ui(r);
            TapZone(r, () =>
            {
                var res = gm.TryBuyMinionUpgrade(m, "evolve");
                if (res == ActionResult.Ok) fxFront.Burst(c, 30, DreamSkin.Mint, 200f, 9f, .8f);
                else if (lockReason != null) gm.Toast("Buy " + (needRanks - gm.CoreMinionRanks(m)) + " more upgrades on the current form first.");
                else ReportFear(res, cost - m.Fear);
            }, round: true);
        }

        void DrawFormInfo(Config.MinionLineDef line, int index, Vector2 c, string note)
        {
            var f = line.forms[index];
            DreamSkin.Orb(c, 150f, DreamSkin.BoneDim, .25f, 0f);
            DreamSkin.Icon(new Rect(c.x - 36f, c.y - 48f, 72f, 72f), "minion_" + line.id, A(DreamSkin.BoneDim, .6f));
            DreamSkin.Label(new Rect(c.x - 230f, c.y + 90f, 460f, 24f), "<b>" + f.name + "</b>", DreamSkin.Body, DreamSkin.Bone, TextAnchor.MiddleCenter);
            DreamSkin.Label(new Rect(c.x - 230f, c.y + 114f, 460f, 20f), note + (Trait(f) != "" ? "  ·  " + Trait(f) : ""), DreamSkin.Small, DreamSkin.BoneDim, TextAnchor.MiddleCenter);
        }
    }
}
