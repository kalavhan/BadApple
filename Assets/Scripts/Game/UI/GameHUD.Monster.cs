using System.Linq;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// The monster's windows. Tapping the monster opens a ring of stat orbs around it, like a tower's ring: one tap on
    /// an orb spends the Fear for the next level and puts the point into that stat, with what it adds written under
    /// the orb. Ability ranks and utility tricks owed at some levels appear in the same ring as gold orbs.
    /// The Horde button opens a compact tray above the combat controls: three creature cards (Swarm, Breachers,
    /// Escort) with their art, role and resistance, and a strip with the selected creature's level and its one action.
    /// The monster's own buttons are icon orbs, each with its own colour and border.
    /// </summary>
    public partial class GameHUD
    {
        const string FearHex = "#FF6FAE";
        static readonly Color FearPink = new Color32(0xFF, 0x6F, 0xAE, 255);
        static readonly Color Brass = new Color32(0xE8, 0xB8, 0x4A, 255);
        static readonly string[] Romans = { "I", "II", "III" };
        static readonly string[] KitSlots = { "attack", "area", "special" };
        static readonly string[] KitIcons = { "kit_attack", "kit_area", "kit_special" };

        bool monsterRing, hordeOpen;
        float monsterRingAt = -9f, hordeOpenedAt = -9f, monsterShakeAt = -9f;
        int hordeCard = -1;
        float cardSelectedAt = -9f;
        readonly float[] cardFlashAt = { -9f, -9f, -9f };

        static Color StatColor(string id)
        {
            switch (id)
            {
                case "vitality": return new Color32(0xFF, 0x7A, 0x8E, 255);
                case "hide": return new Color32(0x9C, 0xC2, 0xFF, 255);
                case "stride": return DreamSkin.Mint;
                case "frenzy": return new Color32(0xF2, 0xC1, 0x4E, 255);
                case "maw": return new Color32(0xFF, 0x8A, 0x4C, 255);
                default: return DreamSkin.Violet;
            }
        }

        static string StatIcon(string id) => id == "vitality" ? "stat_vitality" : id == "hide" ? "stat_hide" : id == "stride" ? "stat_stride" :
            id == "frenzy" ? "stat_frenzy" : id == "maw" ? "stat_maw" : "stat_horde";

        /// <summary>What a stat rank does, said as the benefit ("Attacks 7% faster", not "+7% attack speed").</summary>
        static string StatGain(string id, float perRank)
        {
            int p = Mathf.RoundToInt(perRank * 100f);
            switch (id)
            {
                case "vitality": return "+" + p + "% health";
                case "hide": return "Takes " + p + "% less damage";
                case "stride": return "Moves " + p + "% faster";
                case "frenzy": return "Attacks " + p + "% faster";
                case "maw": return "Hits " + p + "% harder";
                default: return "+" + p + "%";
            }
        }

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
        static string RoleIcon(string role) => "role_" + role;

        /// <summary>Icon, rim colour and label colour for each ability button, so each reads as a different action.</summary>
        static string AbilityIcon(Config.AbilityDef a)
        {
            switch (a.effect)
            {
                case "flambe": return "ab_flambe";
                case "sporeBloom": return "ab_spores";
                case "lastCall": return "ab_bell";
                case "meatHook": return "ab_hook";
                case "graveroot": return "ab_shrine";
                case "doNotDisturb": return "ab_door";
                case "towerDamageMultiplier": return "ab_jam";
                case "doorDamageMultiplier": return "ab_rampage";
                case "faithIncomeMultiplier": return "ab_blackout";
                case "dash": return "ab_lunge";
                case "towerUntargetable": return "ab_cloak";
                case "residentSlowZone": return "ab_slime";
                case "bedIncomeMultiplier": return "ab_gaze";
                default: return "trick";
            }
        }

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
            DrawMonsterButtons(m);

            if (!m.Dead)
            {
                if (monsterRing) DrawMonsterRing(m);
                else DrawGrowHint(m);
            }
            if (hordeOpen) DrawHordeTray(m);
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

        /// <summary>An icon orb: dark lacquer face, a coloured rim (double for the signature special), an icon, a short
        /// label and a cooldown sweep. Fires on finger down like the other round buttons, so it works while moving.</summary>
        void IconButton(Vector2 c, float d, string icon, string label, Color col, bool enabled, float cooldown, float cooldownTotal, string key, System.Action press, Texture art = null)
        {
            bool down = pointers.Values.Any(p => (p.Current - c).sqrMagnitude < d * d * 0.25f);
            float a = enabled ? 1f : .45f;
            DreamSkin.Orb(c, d * (down ? .94f : 1f), col, a, enabled ? (down ? .45f : .16f) : 0f);
            DreamSkin.Ring(c, d - 7f, 2.4f, A(col, .85f * a));
            // Small orbs and art orbs keep their face clear: the label sits under the orb instead.
            bool below = art != null || d < 70f;
            if (art != null) DreamSkin.Tex(new Rect(c.x - d * .36f, c.y - d * .38f, d * .72f, d * .72f), art, new Color(1, 1, 1, a));
            else if (below) DreamSkin.Icon(new Rect(c.x - d * .25f, c.y - d * .25f, d * .5f, d * .5f), icon, A(col, a));
            else DreamSkin.Icon(new Rect(c.x - d * .2f, c.y - d * .3f, d * .4f, d * .4f), icon, A(col, a));
            if (below) DreamSkin.Shadowed(new Rect(c.x - 50f, c.y + d * .5f + 2f, 100f, 18f), "<b>" + label + "</b>", DreamSkin.Tiny, A(DreamSkin.Bone, a), TextAnchor.MiddleCenter);
            else DreamSkin.Label(new Rect(c.x - d * .5f, c.y + d * .1f, d, 18f), "<b>" + label + "</b>", DreamSkin.Tiny, A(DreamSkin.Bone, a));
            if (cooldown > 0f)
            {
                UiFx.Arc(c, d / 2f - 3f, 4f, 1f - cooldown / Mathf.Max(.01f, cooldownTotal), A(col, .9f), scale);
                DreamSkin.Shadowed(new Rect(c.x - 30f, c.y - 14f, 60f, 26f), "<b>" + Mathf.CeilToInt(cooldown) + "</b>", DreamSkin.Body, DreamSkin.Bone, TextAnchor.MiddleCenter);
            }
            else if (!string.IsNullOrEmpty(key) && !below) DreamSkin.Label(new Rect(c.x - d * .5f, c.y + d * .26f, d, 14f), key, DreamSkin.Tiny, A(DreamSkin.BoneDim, a));
            var r = new Rect(c.x - d / 2f, c.y - d / 2f, d, d);
            Ui(r);
            if (enabled) AddZone(r, press);
        }

        /// <summary>Bottom right: the area attack (the monster's colour, largest), the special (gold, double rim),
        /// utilities (mint and violet), the Horde button (the active creature's art, brass) and the hotel view (bone).</summary>
        void DrawMonsterButtons(Monster m)
        {
            bool night = gm.Phase == Phase.Night;
            float right = vw - (Screen.width - Screen.safeArea.xMax) / scale, bottom = VH - Screen.safeArea.yMin / scale;
            var spots = new[]
            {
                new Vector2(right - 82f, bottom - 86f), new Vector2(right - 206f, bottom - 66f),
                new Vector2(right - 70f, bottom - 214f), new Vector2(right - 176f, bottom - 184f), new Vector2(right - 296f, bottom - 160f),
            };
            // A restrained palette: the area attack in the monster's colour, the special and the horde in brass,
            // utilities and the hotel view in bone. Every rim is the same weight.
            for (int i = 0; i < m.Loadout.Length && i < spots.Length; i++)
            {
                var a = m.Loadout[i];
                int idx = i;
                bool area = a.id == m.Def.area, special = a.id == m.Def.special;
                var col = area ? gm.ColorOf(m) : special ? Brass : DreamSkin.Bone;
                float d = area ? 112f : special ? 94f : 78f;
                IconButton(spots[i], d, AbilityIcon(a), a.name, col, night && !m.Dead, m.Cooldowns[i], a.cooldownSeconds, "[" + (i + 1) + "]",
                    () => gm.UseAbility(idx));
            }
            // Horde: the active creature's art in a brass rim; it glows when something there is affordable.
            var hc = new Vector2(right - 318f, bottom - 62f);
            bool awake = gm.HordeAwake(m);
            var art = awake ? Sprites.MinionArt(gm.MinionLine(m).id, gm.Creature(m, m.ActiveMinion).role) : null;
            IconButton(hc, 90f, "stat_horde", awake ? "Horde · " + gm.Minions.Count : "Horde", hordeOpen ? Color.white : Brass, true, 0f, 1f, "[H]", () => ToggleHorde(!hordeOpen), art: art);
            if (!awake && m.Fear >= gm.UnlockCost(m, 0)) DreamSkin.GlowAt(hc, Vector2.one * 150f, A(Brass, .25f + .15f * Mathf.Sin(Time.unscaledTime * 3f)));
            if (gm.HotelViewAvailable)
                IconButton(new Vector2(right - 52f, 248f), 64f, "eye", gm.HotelView ? "Back" : "Hotel", DreamSkin.Bone, true, 0f, 1f, "[M]", () => gm.HotelView = !gm.HotelView);
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
            if (open)
            {
                hordeOpenedAt = Time.unscaledTime; monsterRing = false;
                var m = gm.Monster;
                hordeCard = m != null && gm.HordeAwake(m) ? m.ActiveMinion : 0;
                cardSelectedAt = Time.unscaledTime;
            }
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
                string gain = maxed ? "maxed" : StatGain(s.id, s.perRank);
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
            fxFront.Say(new Vector2(box.center.x, box.yMin - 24f), "LEVEL " + m.Level, StatGain(s.id, s.perRank).ToUpper());
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

        // ------------------------------------------------------------ the horde tray

        /// <summary>Right-aligned above the combat controls, so the joystick, the ability buttons and most of the hallway stay in view.</summary>
        Rect HordeTrayRect()
        {
            var safe = SafeRect();
            float w = Mathf.Min(560f, safe.width - 24f), h = 416f;
            return new Rect(safe.xMax - w - 12f, Mathf.Max(safe.yMin + 56f, safe.yMax - 262f - h), w, h);
        }

        void SelectCard(int i)
        {
            if (hordeCard != i) cardSelectedAt = Time.unscaledTime;
            hordeCard = i;
        }

        void DrawHordeTray(Monster m)
        {
            float k = Ease(hordeOpenedAt);
            var line = gm.MinionLine(m);
            bool awake = gm.HordeAwake(m);
            if (hordeCard < 0 || hordeCard > 2) hordeCard = 0;
            var r = HordeTrayRect();
            r.y += (1f - k) * 30f;
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, k);
            // Dark lacquer with a worn brass edge and a restrained purple glow.
            DreamSkin.GlowAt(new Vector2(r.center.x, r.y + 20f), new Vector2(r.width * 1.1f, 140f), A(DreamSkin.Violet, .14f));
            DreamSkin.Fill(r, new Color(.07f, .05f, .09f, .96f), 16f);
            DreamSkin.Border(r, A(Brass, .55f), 1.6f, 16f);
            DreamSkin.Border(new Rect(r.x + 5f, r.y + 5f, r.width - 10f, r.height - 10f), A(Brass, .18f), 1f, 12f);
            Ui(r);

            // ---- header: Horde, Fear, living minions and the next wave
            DreamSkin.Label(new Rect(r.x + 18f, r.y + 10f, 120f, 34f), "Horde", DreamSkin.Heading, Brass, TextAnchor.MiddleLeft);
            DreamSkin.Icon(new Rect(r.x + 120f, r.y + 15f, 22f, 22f), "fear", Color.white);
            DreamSkin.Label(new Rect(r.x + 146f, r.y + 10f, 70f, 32f), "<b><color=" + FearHex + ">" + Mathf.FloorToInt(m.Fear) + "</color></b>", DreamSkin.Body, DreamSkin.Bone, TextAnchor.MiddleLeft);
            DreamSkin.Icon(new Rect(r.x + 206f, r.y + 15f, 22f, 22f), "role_swarm", DreamSkin.BoneDim);
            DreamSkin.Label(new Rect(r.x + 232f, r.y + 10f, 80f, 32f), gm.Minions.Count + " out", DreamSkin.Small, DreamSkin.BoneDim, TextAnchor.MiddleLeft);
            float next = gm.NextPulseIn();
            string wave = !awake ? "asleep" : next < 0 ? "next night" : next < 1f ? "now" : Mathf.CeilToInt(next) + "s";
            var pill = new Rect(r.xMax - 210f, r.y + 10f, 150f, 32f);
            DreamSkin.Fill(pill, A(Brass, awake && next >= 0 && next < 5f ? .35f + .2f * Mathf.Sin(Time.unscaledTime * 6f) : .16f), 16f);
            DreamSkin.Label(pill, "Next wave · <b>" + wave + "</b>", DreamSkin.Small, DreamSkin.Bone, TextAnchor.MiddleCenter);
            CloseButton(new Rect(r.xMax - 50f, r.y + 8f, 38f, 38f), () => ToggleHorde(false));

            // ---- three creature cards
            float gap = 12f, cw = (r.width - 36f - gap * 2f) / 3f, ch = 176f;
            for (int i = 0; i < 3; i++)
                DrawCreatureCard(m, line, i, new Rect(r.x + 18f + i * (cw + gap), r.y + 52f, cw, ch));

            // ---- the selected creature's actions, then Horde Strength for all of them
            DrawSelectedRow(m, line, hordeCard, new Rect(r.x + 18f, r.y + 240f, r.width - 36f, 74f));
            DrawStrengthStrip(m, new Rect(r.x + 18f, r.y + 322f, r.width - 36f, 80f));
            GUI.color = old;
        }

        static void Pill(Rect r, string text, Color fill, Color textCol, bool outlined = false)
        {
            if (outlined) { DreamSkin.Fill(r, new Color(.07f, .05f, .09f, .95f), r.height / 2f); DreamSkin.Border(r, fill, 1.4f, r.height / 2f); }
            else DreamSkin.Fill(r, fill, r.height / 2f);
            DreamSkin.Label(r, "<b>" + text + "</b>", DreamSkin.Tiny, textCol, TextAnchor.MiddleCenter);
        }

        void DrawCreatureCard(Monster m, Config.MinionLineDef line, int i, Rect c)
        {
            var creature = line.creatures[i];
            var role = gm.MinionRole(creature.role);
            bool owned = gm.MinionOwned(m, i), active = owned && m.ActiveMinion == i, queued = m.QueuedMinion == i, selected = hordeCard == i;
            float now = Time.unscaledTime;

            DreamSkin.Fill(c, owned ? new Color(.13f, .09f, .15f, .98f) : new Color(.08f, .06f, .1f, .98f), 12f);
            DreamSkin.GlowAt(new Vector2(c.center.x, c.y + 60f), new Vector2(c.width, 120f), A(ResistColor(gm.ResistOf(creature)), owned ? .16f : .05f));
            // Active: a steady gold frame. Queued: a thinner brass frame. Selected: a thin white outline and a tab under
            // the card, with a beam that runs round the border once when it is picked.
            if (active) { DreamSkin.GlowAt(c.center, c.size * 1.25f, A(Brass, .2f)); DreamSkin.Border(c, Brass, 2.4f, 12f); }
            else if (queued) DreamSkin.Border(c, A(Brass, .75f), 1.6f, 12f);
            else DreamSkin.Border(c, A(DreamSkin.Bone, owned ? .3f : .14f), 1.2f, 12f);
            if (selected)
            {
                DreamSkin.Border(new Rect(c.x + 3f, c.y + 3f, c.width - 6f, c.height - 6f), A(Color.white, .45f), 1f, 10f);
                DreamSkin.Fill(new Rect(c.center.x - 22f, c.yMax + 3f, 44f, 4f), A(Color.white, .85f), 2f);
                float beam = 1f - Mathf.Clamp01((now - cardSelectedAt) / 1.1f);
                if (beam > 0f) BorderBeam(c, A(Color.white, beam), Mathf.Clamp01((now - cardSelectedAt) / 1.1f));
            }
            float flash = Mathf.Clamp01(1f - (now - cardFlashAt[i]) / .45f);
            if (flash > 0f) DreamSkin.Fill(c, A(Brass, .35f * flash), 12f);

            // Art: the creature itself is the biggest thing on the card. The selected one breathes a little.
            var art = Sprites.MinionArt(line.id, creature.role);
            float bob = selected ? Mathf.Sin(now * 3f) * 2.5f : 0f;
            var artRect = new Rect(c.x + 10f, c.y + 10f - bob, c.width - 20f, 102f);
            if (art != null) DrawTextureFit(art, artRect, owned ? Color.white : new Color(.38f, .34f, .42f, 1f));
            else DreamSkin.Icon(new Rect(artRect.center.x - 36f, artRect.center.y - 36f, 72f, 72f), "minion_" + line.id, owned ? DreamSkin.Bone : DreamSkin.BoneDim);

            // Corner markers, each with its own word as well as colour.
            if (active) Pill(new Rect(c.xMax - 68f, c.y - 9f, 74f, 22f), "ACTIVE", Brass, DreamSkin.Ink);
            else if (queued) Pill(new Rect(c.xMax - 74f, c.y - 9f, 80f, 22f), "QUEUED", Brass, Brass, outlined: true);
            if (owned && gm.MinionEvolved(m, i))
            {
                var ev = new Rect(c.x + 6f, c.y + 6f, 84f, 20f);
                DreamSkin.Fill(ev, A(DreamSkin.Violet, .9f), 10f);
                DreamSkin.Icon(new Rect(ev.x + 5f, ev.y + 3f, 14f, 14f), "kit_special", DreamSkin.Ink);
                DreamSkin.Label(new Rect(ev.x + 18f, ev.y, ev.width - 20f, ev.height), "<b>EVOLVED</b>", DreamSkin.Tiny, DreamSkin.Ink, TextAnchor.MiddleCenter);
            }
            if (!owned)
            {
                var lockC = new Vector2(c.center.x, c.y + 58f);
                DreamSkin.Orb(lockC, 46f, DreamSkin.Bone, .5f, 0f);
                DreamSkin.Icon(new Rect(lockC.x - 12f, lockC.y - 13f, 24f, 24f), "lock", DreamSkin.Bone);
                DreamSkin.Label(new Rect(c.x + 10f, c.y + 84f, c.width - 20f, 22f), "<b><color=" + FearHex + ">" + Mathf.CeilToInt(gm.UnlockCost(m, i)) + " Fear</color></b>", DreamSkin.Small, DreamSkin.Bone, TextAnchor.MiddleCenter);
            }

            // Role and numbers under the art: role icon and name, quantity, resistance and weakness.
            float y = c.y + 114f;
            DreamSkin.Icon(new Rect(c.x + 10f, y + 2f, 20f, 20f), RoleIcon(role.id), Brass);
            DreamSkin.Label(new Rect(c.x + 34f, y, c.width - 40f, 24f), "<b>" + role.name + "</b>", DreamSkin.Small, DreamSkin.Bone, TextAnchor.MiddleLeft);
            string qty = role.id == "escort" ? "guard ×" + gm.EscortCap(m, i) : "×" + gm.PerDoor(m, i) + " per door";
            DreamSkin.Label(new Rect(c.x + 10f, y + 22f, c.width - 20f, 18f), qty, DreamSkin.Tiny, DreamSkin.BoneDim, TextAnchor.MiddleLeft);
            int resist = gm.ResistOf(creature), weak = GameManager.Weakness(resist);
            DreamSkin.Icon(new Rect(c.x + 10f, y + 42f, 16f, 16f), ResistIcon(resist), ResistColor(resist));
            DreamSkin.Label(new Rect(c.x + 28f, y + 40f, 60f, 20f), "resists", DreamSkin.Tiny, ResistColor(resist), TextAnchor.MiddleLeft);
            DreamSkin.Icon(new Rect(c.center.x + 6f, y + 42f, 16f, 16f), ResistIcon(weak), A(ResistColor(weak), .8f));
            DreamSkin.Label(new Rect(c.center.x + 24f, y + 40f, 50f, 20f), "weak", DreamSkin.Tiny, A(ResistColor(weak), .8f), TextAnchor.MiddleLeft);
            int index = i;
            TapZone(c, () => SelectCard(index));
        }

        /// <summary>A short bright beam that runs once round a card's border as it is selected.</summary>
        void BorderBeam(Rect r, Color col, float progress)
        {
            float perimeter = 2f * (r.width + r.height);
            float head = progress * perimeter;
            for (int s = 0; s < 12; s++)
            {
                float d = head - s * 7f;
                if (d < 0f) break;
                DreamSkin.GlowAt(PerimeterPoint(r, d), Vector2.one * (14f - s * .6f), A(col, .8f * (1f - s / 12f)));
            }
        }

        static Vector2 PerimeterPoint(Rect r, float d)
        {
            if (d < r.width) return new Vector2(r.x + d, r.y);
            d -= r.width;
            if (d < r.height) return new Vector2(r.xMax, r.y + d);
            d -= r.height;
            if (d < r.width) return new Vector2(r.xMax - d, r.yMax);
            d -= r.width;
            return new Vector2(r.x, r.yMax - d);
        }

        void DrawTextureFit(Texture tex, Rect box, Color tint)
        {
            float aspect = tex.width / (float)tex.height;
            float w = box.width, h = w / aspect;
            if (h > box.height) { h = box.height; w = h * aspect; }
            DreamSkin.Tex(new Rect(box.center.x - w / 2f, box.yMax - h, w, h), tex, tint);
        }

        /// <summary>One clearly labelled action button; the price is spent on the tap, with no extra confirm.</summary>
        void ActionButton(Rect b, string label, string sub, Color col, bool enabled, bool afford, System.Action act)
        {
            DreamSkin.GlowAt(b.center, b.size * 1.3f, A(col, afford ? .2f + .08f * Mathf.Sin(Time.unscaledTime * 3f) : .03f));
            DreamSkin.Fill(b, afford ? A(col, .9f) : new Color(1, 1, 1, .06f), b.height / 2f);
            DreamSkin.Border(b, A(col, enabled ? 1f : .3f), 1.8f, b.height / 2f);
            float labelY = string.IsNullOrEmpty(sub) ? b.y : b.y + 3f;
            DreamSkin.Label(new Rect(b.x, labelY, b.width, string.IsNullOrEmpty(sub) ? b.height : b.height * .55f), "<b>" + label + "</b>", DreamSkin.Small, afford ? DreamSkin.Ink : A(DreamSkin.Bone, enabled ? 1f : .45f), TextAnchor.MiddleCenter);
            if (!string.IsNullOrEmpty(sub)) DreamSkin.Label(new Rect(b.x, b.y + b.height * .52f, b.width, b.height * .42f), sub, DreamSkin.Tiny, afford ? DreamSkin.Ink : FearPink);
            if (enabled) TapZone(b, act);
        }

        GUIStyle wrapTiny;

        /// <summary>A state, not a button: filled, no glow, two lines.</summary>
        void StatusBox(Rect b, string title, string sub, Color col)
        {
            DreamSkin.Fill(b, A(col, .22f), b.height / 2f);
            DreamSkin.Border(b, A(col, .8f), 1.4f, b.height / 2f);
            DreamSkin.Label(new Rect(b.x, b.y + 3f, b.width, b.height * .55f), "<b>" + title + "</b>", DreamSkin.Small, col, TextAnchor.MiddleCenter);
            DreamSkin.Label(new Rect(b.x, b.y + b.height * .52f, b.width, b.height * .42f), sub, DreamSkin.Tiny, DreamSkin.BoneDim);
        }

        /// <summary>The selected creature: name, role and signature evolution, with its contextual actions (Unlock,
        /// Deploy next pulse / Queued, Evolve).</summary>
        void DrawSelectedRow(Monster m, Config.MinionLineDef line, int i, Rect s)
        {
            var creature = line.creatures[i];
            bool owned = gm.MinionOwned(m, i), awake = gm.HordeAwake(m), evolved = gm.MinionEvolved(m, i);
            DreamSkin.Fill(s, new Color(1, 1, 1, .035f), 12f);
            DreamSkin.Icon(new Rect(s.x + 12f, s.y + 9f, 20f, 20f), RoleIcon(creature.role), Brass);
            DreamSkin.Label(new Rect(s.x + 38f, s.y + 6f, s.width - 330f, 24f), "<b>" + creature.name + "</b>", DreamSkin.Body, DreamSkin.Bone, TextAnchor.MiddleLeft);
            // The evolution, named and explained, wrapping under the name.
            if (wrapTiny == null) wrapTiny = new GUIStyle(DreamSkin.Tiny) { wordWrap = true, fontStyle = FontStyle.Normal };
            string evo = "<color=#B99CFF><b>" + creature.evolveName + "</b></color>" + (evolved ? "" : " (evolution)") + ": " + creature.evolveText;
            DreamSkin.Label(new Rect(s.x + 14f, s.y + 32f, s.width - 300f, 38f), evo, wrapTiny, DreamSkin.BoneDim, TextAnchor.UpperLeft);

            float bw = 132f, bh = 50f, by = s.y + 12f;
            var right = new Rect(s.xMax - bw - 8f, by, bw, bh);
            var left = new Rect(right.x - bw - 8f, by, bw, bh);
            if (!owned)
            {
                float cost = gm.UnlockCost(m, i);
                bool can = i == 0 || awake;
                ActionButton(right, i == 0 ? "Awaken" : "Unlock", can ? Mathf.CeilToInt(cost) + " Fear" : "awaken first", FearPink, can, can && m.Fear + .001f >= cost,
                    () => { var res = gm.TryUnlockMinion(m, i); if (res == ActionResult.Ok) Bought(i); else ReportFear(res, cost - m.Fear); });
                return;
            }
            // Evolve, or a quiet note once it is evolved.
            if (!evolved)
            {
                float cost = gm.EvolveCost(m, i);
                ActionButton(right, "Evolve", Mathf.CeilToInt(cost) + " Fear", DreamSkin.Violet, true, m.Fear + .001f >= cost,
                    () => { var res = gm.TryEvolveMinion(m, i); if (res == ActionResult.Ok) Bought(i); else ReportFear(res, cost - m.Fear); });
            }
            else StatusBox(right, "Evolved", "kept for the match", DreamSkin.Violet);
            // Deploy next pulse / Queued / spawning now.
            if (m.ActiveMinion == i) StatusBox(left, "Active", m.QueuedMinion >= 0 ? "until next pulse" : "spawning now", Brass);
            else if (m.QueuedMinion == i)
                ActionButton(left, "Queued", "tap to cancel", Brass, true, false, () => gm.TryQueueMinion(m, m.ActiveMinion));
            else if (!gm.CanSwitchMinion(m))
                ActionButton(left, "Deploy", "next night", Brass, false, false, null);
            else
                ActionButton(left, "Deploy", "free · next pulse", Brass, true, true, () => { if (gm.TryQueueMinion(m, i) == ActionResult.Ok) cardFlashAt[i] = Time.unscaledTime; });
        }

        /// <summary>Horde Strength, always shown: one shared level that improves every creature, now and unlocked later.</summary>
        void DrawStrengthStrip(Monster m, Rect s)
        {
            int strength = m.HordeStrength, max = gm.Cfg.minions.maxStrength;
            DreamSkin.Fill(s, new Color(1, 1, 1, .05f), 12f);
            DreamSkin.Border(s, A(Brass, .25f), 1f, 12f);
            DreamSkin.Label(new Rect(s.x + 14f, s.y + 6f, 300f, 26f), "<b>Horde Strength " + (strength > 0 ? Roman(strength) : "—") + "</b>", DreamSkin.Body, DreamSkin.Bone, TextAnchor.MiddleLeft);
            float tx = s.x + 18f, tw = Mathf.Clamp(s.width - 220f, 120f, 240f), step = tw / (max - 1);
            for (int l = 1; l <= max; l++)
            {
                var p = new Vector2(tx + (l - 1) * step, s.y + 44f);
                bool done = l <= strength;
                if (l > 1) DreamSkin.Fill(new Rect(p.x - step, p.y - 1.5f, step, 3f), done ? A(Brass, .9f) : new Color(1, 1, 1, .12f), 1.5f);
                DreamSkin.Fill(new Rect(p.x - 6f, p.y - 6f, 12f, 12f), done ? Brass : new Color(1, 1, 1, .18f), 6f);
            }
            string note = strength <= 0 ? "Awaken the horde to start." :
                "All creatures: +" + Mathf.RoundToInt(gm.Cfg.minions.healthPerStrength * 100f) + "% health, hit " + Mathf.RoundToInt(gm.Cfg.minions.damagePerStrength * 100f) + "% harder, more per wave";
            DreamSkin.Label(new Rect(s.x + 14f, s.y + 56f, s.width - 190f, 20f), note, DreamSkin.Tiny, DreamSkin.BoneDim, TextAnchor.MiddleLeft);
            float cost = gm.StrengthCost(m);
            var b = new Rect(s.xMax - 150f, s.y + 14f, 138f, 52f);
            if (strength <= 0) ActionButton(b, "Upgrade", "awaken first", Brass, false, false, null);
            else if (cost < 0) Pill(new Rect(b.x + 8f, b.y + 13f, b.width - 16f, 26f), "MAX STRENGTH", Brass, DreamSkin.Ink);
            else ActionButton(b, "Upgrade", Mathf.CeilToInt(cost) + " Fear", Brass, true, m.Fear + .001f >= cost,
                () => { var res = gm.TryUpgradeStrength(m); if (res == ActionResult.Ok) { cardFlashAt[0] = cardFlashAt[1] = cardFlashAt[2] = Time.unscaledTime; fxFront.Burst(b.center, 18, Brass, 160f, 7f, .6f); } else ReportFear(res, cost - m.Fear); });
        }

        static string Roman(int n) => n <= 0 ? "" : n < 4 ? new string('I', n) : n == 4 ? "IV" : n == 5 ? "V" : n == 6 ? "VI" : n.ToString();

        /// <summary>Purchase feedback: a quick brass flash on the card and a burst of sparks.</summary>
        void Bought(int i)
        {
            cardFlashAt[i] = Time.unscaledTime;
            var r = HordeTrayRect();
            float gap = 12f, cw = (r.width - 36f - gap * 2f) / 3f;
            var c = new Vector2(r.x + 18f + i * (cw + gap) + cw / 2f, r.y + 120f);
            fxFront.Burst(c, 22, Brass, 170f, 8f, .6f);
        }
    }
}
