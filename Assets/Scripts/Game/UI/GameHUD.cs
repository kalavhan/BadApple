using System.Linq;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// All UI for the demo, drawn with IMGUI on a virtual 720 px tall canvas (scales to any landscape screen).
    /// Placeholder look; the real UI will follow the "in-world objects" direction from the bible.
    /// </summary>
    public class GameHUD : MonoBehaviour
    {
        GameManager gm;
        float scale = 1f;
        float vw = 1280f;
        const float VH = 720f;

        bool styled;
        GUIStyle box, label, small, title, subtitle, center, button, bigButton, shadow;
        Texture2D panelTex, circleTex;

        int buildMenuSlot = -1;
        string toast;
        float toastUntil;
        string monsterPick = "stitchwork_chef";
        bool joyActive;

        static readonly Color Bone = (Color)Palette.Bone;
        static readonly Color Candle = (Color)Palette.Candle;
        static readonly Color Red = (Color)Palette.AppleRed;
        static readonly Color Mint = (Color)Palette.Mint;
        static readonly Color TealC = (Color)Palette.Teal;

        void Awake()
        {
            gm = GetComponent<GameManager>();
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus) GameInput.ClearAll();
        }

        // ------------------------------------------------------------ setup

        static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        static Texture2D MakeCircle(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            float r = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - d)));
                }
            t.Apply();
            return t;
        }

        void EnsureStyles()
        {
            if (styled) return;
            styled = true;
            panelTex = MakeTex(new Color(0.106f, 0.086f, 0.141f, 0.9f));
            circleTex = MakeCircle(64);

            box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(10, 10, 8, 8) };
            box.normal.background = panelTex;

            label = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, richText = true };
            label.normal.textColor = Bone;
            small = new GUIStyle(label) { fontSize = 12 };
            center = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
            title = new GUIStyle(center) { fontSize = 54, fontStyle = FontStyle.Bold };
            title.normal.textColor = Candle;
            subtitle = new GUIStyle(center) { fontSize = 20, fontStyle = FontStyle.Italic };
            shadow = new GUIStyle(center) { fontSize = 14, fontStyle = FontStyle.Bold, wordWrap = false };

            button = new GUIStyle(GUI.skin.button) { fontSize = 14, richText = true };
            bigButton = new GUIStyle(button) { fontSize = 20, fontStyle = FontStyle.Bold };
        }

        // ------------------------------------------------------------ helpers

        Vector2 WorldToGui(Vector2 w)
        {
            var sp = gm.Cam.WorldToScreenPoint(new Vector3(w.x, w.y, 0f));
            return new Vector2(sp.x / scale, (Screen.height - sp.y) / scale);
        }

        Vector2 GuiToWorld(Vector2 g)
        {
            var wp = gm.Cam.ScreenToWorldPoint(new Vector3(g.x * scale, Screen.height - g.y * scale, 10f));
            return new Vector2(wp.x, wp.y);
        }

        void Bar(Rect r, float frac, Color fg)
        {
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(r, Sprites.White);
            GUI.color = fg;
            GUI.DrawTexture(new Rect(r.x + 1, r.y + 1, Mathf.Max(0f, (r.width - 2) * Mathf.Clamp01(frac)), r.height - 2), Sprites.White);
            GUI.color = old;
        }

        void Shadowed(Rect r, string text, Color c, GUIStyle style = null)
        {
            style = style ?? shadow;
            var old = style.normal.textColor;
            style.normal.textColor = Color.black;
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, style);
            style.normal.textColor = c;
            GUI.Label(r, text, style);
            style.normal.textColor = old;
        }

        void Toast(string text)
        {
            toast = text;
            toastUntil = Time.unscaledTime + 2.5f;
        }

        void Report(ActionResult r, string resource = "Dream Power")
        {
            switch (r)
            {
                case ActionResult.NoMoney: Toast("Not enough " + resource + "."); break;
                case ActionResult.Blocked: Toast("Blocked: your door can't be more than 4 levels above your weakest weapon."); break;
                case ActionResult.MaxLevel: Toast("Already at max level."); break;
                case ActionResult.Invalid: Toast("Can't do that right now."); break;
            }
        }

        static string ResName(string res) => res == "faith" ? "Faith" : "Dream Power";
        static string ResShort(string res) => res == "faith" ? "F" : "DP";

        static string Clock(float t)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(t));
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        // ------------------------------------------------------------ frame

        void OnGUI()
        {
            if (gm == null) return;
            GameInput.Handle(Event.current);
            EnsureStyles();
            scale = Screen.height / VH;
            vw = Screen.width / scale;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            if (gm.Phase == Phase.ConfigError) { DrawError(); return; }
            if (gm.Phase == Phase.RoleSelect) { DrawMenu(); return; }
            if (gm.Cam == null) return;

            DrawWorldLabels();
            DrawTopBar();
            DrawBanner();

            if (gm.Phase == Phase.Results) { DrawResults(); return; }

            if (gm.HumanRole == Role.Resident) DrawResidentUI();
            else DrawMonsterUI();

            DrawLog();
            DrawToast();
            HandleWorldClick();
        }

        void DrawError()
        {
            GUI.Box(new Rect(vw / 2 - 320, 200, 640, 220), GUIContent.none, box);
            GUI.Label(new Rect(vw / 2 - 300, 215, 600, 40), "Config error", subtitle);
            GUI.Label(new Rect(vw / 2 - 300, 260, 600, 150),
                gm.ConfigError + "\n\nCheck Assets/StreamingAssets/Config/*.json and press Play again.", label);
        }

        // ------------------------------------------------------------ menu

        void DrawMenu()
        {
            var cfg = gm.Cfg;
            var dim = GUI.color;
            GUI.color = new Color(0.106f, 0.086f, 0.141f, 0.82f);
            GUI.DrawTexture(new Rect(0, 0, vw, VH), Sprites.White);
            GUI.color = dim;
            GUI.Label(new Rect(0, 70, vw, 70), "BAD APPLE HOTEL", title);
            GUI.Label(new Rect(0, 140, vw, 30), "Six nights. Seven guests. One of them is a monster.", subtitle);

            var apple = Sprites.Apple.texture;
            GUI.DrawTexture(new Rect(vw / 2 - 32, 180, 64, 64), apple);

            float y = 265;
            GUI.Label(new Rect(0, y, vw, 24), "Your monster pick (played if you end up as the Monster)", center);
            y += 30;
            var defs = cfg.monsters.monsters;
            float w = 250, gap = 16, total = defs.Length * w + (defs.Length - 1) * gap;
            for (int i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                var r = new Rect(vw / 2 - total / 2 + i * (w + gap), y, w, 96);
                bool sel = d.id == monsterPick;
                var old = GUI.backgroundColor;
                GUI.backgroundColor = sel ? Candle : Color.white;
                string weak = WeakestTo(d);
                if (GUI.Button(r, "<b>" + d.name + "</b>\nHP " + d.baseHealth + "  Speed " + d.moveSpeed + "\nWeak to " + weak, button))
                    monsterPick = d.id;
                GUI.backgroundColor = old;
                GUI.DrawTexture(new Rect(r.x + 8, r.y + 20, 36, 48), Sprites.Monster(d.id).texture);
            }

            y += 120;
            float bw = 260;
            if (GUI.Button(new Rect(vw / 2 - bw * 1.5f - 20, y, bw, 64), "Play as Resident", bigButton))
                gm.StartMatch(Role.Resident, monsterPick);
            if (GUI.Button(new Rect(vw / 2 - bw / 2, y, bw, 64), "Play as Monster", bigButton))
                gm.StartMatch(Role.Monster, monsterPick);
            if (GUI.Button(new Rect(vw / 2 + bw / 2 + 20, y, bw, 64), "Random role (like online)", bigButton))
                gm.StartMatch(Random.value < 1f / cfg.match.playersPerMatch ? Role.Monster : Role.Resident, monsterPick);

            y += 90;
            AccountProgress.Progress(cfg, Role.Resident, out int rl, out int ri, out int rn);
            AccountProgress.Progress(cfg, Role.Monster, out int ml, out int mi, out int mn);
            GUI.Label(new Rect(0, y, vw, 24), "Resident level " + rl + " (" + ri + "/" + rn + " XP)     Monster level " + ml + " (" + mi + "/" + mn + " XP)", center);
            y += 40;
            GUI.Label(new Rect(vw / 2 - 420, y, 840, 80),
                "Residents: tap a room in the first " + cfg.match.setupSeconds + " s, then upgrade your bed, door and towers to survive " +
                cfg.match.nightCount + " nights.  Monster: WASD or the joystick to move, walk into doors to smash them, walk over body parts to eat them, 1 / 2 / 3 for abilities.", small);
        }

        string WeakestTo(Config.MonsterDef d)
        {
            var m = d.damageTakenMultiplier;
            float[] v = { m.bullet, m.electric, m.fire };
            int best = 0;
            for (int i = 1; i < v.Length; i++) if (v[i] > v[best]) best = i;
            return DamageTypes.Label(best).ToLower();
        }

        // ------------------------------------------------------------ shared HUD

        void DrawTopBar()
        {
            string phase = gm.Phase == Phase.Setup ? "SETUP: PICK A ROOM"
                : gm.Phase == Phase.Night ? "NIGHT " + gm.Night + " / " + gm.Cfg.match.nightCount
                : "DAWN";
            var r = new Rect(vw / 2 - 170, 8, 340, 40);
            GUI.Box(r, GUIContent.none, box);
            Shadowed(new Rect(r.x, r.y + 2, r.width, 20), phase, Candle);
            if (gm.Phase != Phase.Results) Shadowed(new Rect(r.x, r.y + 19, r.width, 20), Clock(gm.PhaseTimer), Bone);

            if (GUI.Button(new Rect(10, 10, 70, 30), "Menu", button)) { gm.ReturnToMenu(); return; }
            float[] speeds = { 1f, 2f, 4f };
            for (int i = 0; i < speeds.Length; i++)
            {
                var old = GUI.backgroundColor;
                GUI.backgroundColor = Mathf.Approximately(gm.Speed, speeds[i]) ? Candle : Color.white;
                if (GUI.Button(new Rect(90 + i * 46, 10, 42, 30), speeds[i] + "x", button)) gm.SetSpeed(speeds[i]);
                GUI.backgroundColor = old;
            }
        }

        void DrawBanner()
        {
            if (string.IsNullOrEmpty(gm.Banner) || Time.unscaledTime > gm.BannerUntil) return;
            var r = new Rect(vw / 2 - 330, 56, 660, 46);
            GUI.Box(r, GUIContent.none, box);
            GUI.Label(new Rect(r.x + 10, r.y + 4, r.width - 20, r.height - 8), gm.Banner, center);
        }

        void DrawLog()
        {
            if (gm.Log.Count == 0) return;
            float h = 18 * gm.Log.Count + 12;
            float x = gm.HumanRole == Role.Monster ? 10 : 10;
            float y = gm.HumanRole == Role.Monster ? 330 : VH - h - 10;
            var r = new Rect(x, y, 330, h);
            GUI.Box(r, GUIContent.none, box);
            for (int i = 0; i < gm.Log.Count; i++)
                GUI.Label(new Rect(x + 8, y + 5 + i * 18, 320, 20), gm.Log[i], small);
        }

        void DrawToast()
        {
            if (string.IsNullOrEmpty(toast) || Time.unscaledTime > toastUntil) return;
            var r = new Rect(vw / 2 - 300, VH - 70, 600, 36);
            GUI.Box(r, GUIContent.none, box);
            GUI.Label(r, toast, center);
        }

        void DrawWorldLabels()
        {
            float now = Time.time;
            foreach (var def in gm.Map.Rooms)
            {
                gm.RoomsByDef.TryGetValue(def, out var room);
                var doorG = WorldToGui(HotelMap.Center(def.DoorTile) + new Vector2(0f, def.DoorInside.y > def.DoorTile.y ? -1.1f : 1.1f));
                if (room == null)
                {
                    if (gm.Phase == Phase.Setup)
                    {
                        var c = WorldToGui(new Vector2(def.Rect.center.x, def.Rect.center.y));
                        Shadowed(new Rect(c.x - 60, c.y - 20, 120, 40), "Room " + (def.Index + 1) + "\nfree", Bone);
                    }
                    continue;
                }
                // door hp
                float frac = room.DoorBroken ? 0f : room.DoorHp / gm.MaxDoorHp(room);
                Bar(new Rect(doorG.x - 22, doorG.y - 4, 44, 7), frac, room.DoorBroken ? Red : (room.UnderAttack(now) ? Candle : TealC));
                // owner name over bed
                var bedG = WorldToGui(HotelMap.Center(def.BedTile) + new Vector2(0f, 1.4f));
                string who = room.Owner.IsHuman ? "YOU" : room.Owner.Name;
                Shadowed(new Rect(bedG.x - 60, bedG.y - 10, 120, 20), room.Owner.Alive ? who : who + " (eaten)", room.Owner.IsHuman ? Candle : Bone);
                if (room.Owner.Alive && room.Owner.Health < gm.Cfg.match.residentHealth)
                    Bar(new Rect(bedG.x - 18, bedG.y + 8, 36, 5), room.Owner.Health / gm.Cfg.match.residentHealth, Red);
            }

            var m = gm.Monster;
            if (m != null && !m.Dead)
            {
                var g = WorldToGui(m.Pos + new Vector2(0f, 1.9f));
                Bar(new Rect(g.x - 30, g.y, 60, 7), m.Hp / gm.MaxHp(m), Red);
                Shadowed(new Rect(g.x - 80, g.y - 18, 160, 18), m.Def.name, Bone);
                if (m.EatingPart != null)
                    Bar(new Rect(g.x - 20, g.y + 10, 40, 5), m.EatProgress / gm.Cfg.bodyParts.eatSeconds, (Color)Palette.Moss);
            }

            // the monster's eyes reveal body parts nearby
            if (m != null && m.IsHuman && gm.RevealRadius(m) > 0f)
            {
                float rr = gm.RevealRadius(m);
                foreach (var p in gm.Parts)
                    if (Vector2.Distance(p.Tile, m.Pos) <= rr * 2f)
                    {
                        var g = WorldToGui(HotelMap.Center(p.Tile) + Vector2.up * 0.8f);
                        Shadowed(new Rect(g.x - 20, g.y - 10, 40, 20), "v", (Color)Palette.Moss);
                    }
            }

            foreach (var f in gm.Floaters)
            {
                float age = Time.unscaledTime - f.Born;
                var g = WorldToGui(f.Pos) + Vector2.down * age * 30f;
                var c = f.Color;
                c.a = Mathf.Clamp01(1.4f - age);
                Shadowed(new Rect(g.x - 80, g.y - 10, 160, 20), f.Text, c);
            }
        }

        void HandleWorldClick()
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0) return;
            if (gm.Phase != Phase.Setup && gm.Phase != Phase.Night) return;
            if (gm.Human == null || gm.Human.Room != null) return;
            var world = GuiToWorld(e.mousePosition);
            var tile = HotelMap.ToTile(world);
            var def = gm.Map.RoomContaining(tile) ?? gm.Map.RoomAtDoor(tile);
            if (def == null) return;
            if (!gm.IsRoomFree(def)) { Toast("Room " + (def.Index + 1) + " is taken."); e.Use(); return; }
            if (gm.Claim(gm.Human, def)) Toast("You moved into Room " + (def.Index + 1) + ". Lock that door!");
            e.Use();
        }

        // ------------------------------------------------------------ resident

        void DrawResidentUI()
        {
            var me = gm.Human;
            if (me == null) return;
            float now = Time.time;
            float px = vw - 320, py = 56, pw = 310;
            GUI.Box(new Rect(px, py, pw, VH - py - 8), GUIContent.none, box);
            float x = px + 10, y = py + 8, w = pw - 20;

            if (me.Room == null)
            {
                GUI.Label(new Rect(x, y, w, 60), "<b>Tap a free room on the map to move in.</b>\nRooms with doors close together are easier to help; isolated ones are lonelier.", label);
                return;
            }
            var room = me.Room;

            GUI.Label(new Rect(x, y, w, 22), "<b>Room " + (room.Def.Index + 1) + "</b>" + (me.Alive ? "" : "  (you were eaten)"), label);
            y += 26;
            GUI.Label(new Rect(x, y, w, 22), "<color=#F2C14E>Dream Power</color>  " + Mathf.FloorToInt(me.DreamPower) + "   +" + gm.DreamPerSecond(room).ToString("0.#") + "/s", label);
            y += 22;
            GUI.DrawTexture(new Rect(x, y + 1, 18, 18), Sprites.Apple.texture);
            bool blackout = now < me.FaithBlockedUntil;
            GUI.Label(new Rect(x + 22, y, w - 22, 22), "<color=#D7263D>Faith</color>  " + Mathf.FloorToInt(me.Faith) + "   +" +
                gm.FaithPerSecond(room).ToString("0.#") + "/s" + (blackout ? "  <color=#D7263D><b>BLACKOUT</b></color>" : ""), label);
            y += 30;

            if (!me.Alive)
            {
                GUI.Label(new Rect(x, y, w, 60), "You haunt the hallway now. Watch the others try to make it to dawn.", label);
                DrawMonsterIntel(x, VH - 120, w);
                return;
            }

            // bed
            var bedCfg = gm.Cfg.beds.levels[room.BedLevel - 1];
            GUI.Label(new Rect(x, y, w - 110, 34), "Bed: " + bedCfg.name + "\n<size=11>Level " + room.BedLevel + "/" + gm.Cfg.beds.levels.Length + "</size>", small);
            float bedCost = gm.BedUpgradeCost(room);
            if (GUI.Button(new Rect(x + w - 105, y + 2, 105, 28), bedCost < 0 ? "MAX" : "Upgrade " + bedCost + " DP", button) && bedCost >= 0)
                Report(gm.TryUpgradeBed(me));
            y += 38;

            // door
            float maxHp = gm.MaxDoorHp(room);
            GUI.Label(new Rect(x, y, w - 110, 18), "Door level " + room.DoorLevel + (room.DoorBroken ? "  <color=#D7263D>BROKEN</color>" : ""), small);
            Bar(new Rect(x, y + 20, w - 115, 8), room.DoorBroken ? 0f : room.DoorHp / maxHp, room.UnderAttack(now) ? Candle : TealC);
            var check = gm.CheckDoor(room);
            string doorLabel;
            if (check.AtMaxLevel) doorLabel = room.DoorBroken ? "Rebuild " + gm.DoorRepairCostAtMax(room) + " DP" : "MAX";
            else doorLabel = (room.DoorBroken ? "Rebuild " : "Upgrade ") + check.Cost + " DP";
            if (GUI.Button(new Rect(x + w - 105, y + 2, 105, 28), doorLabel, button)) Report(gm.TryUpgradeDoor(me));
            y += 32;
            if (!check.AtMaxLevel && !check.Allowed)
            {
                GUI.Label(new Rect(x, y, w, 30), "<color=#D7263D>Gap rule: upgrade your weakest weapon first.</color>", small);
                y += 18;
            }
            y += 6;

            // tower slots
            GUI.Label(new Rect(x, y, w, 20), "<b>Towers</b>  <size=11>(fire only while the monster attacks your door)</size>", small);
            y += 22;
            for (int i = 0; i < room.Slots.Length && i < room.Def.Slots.Count; i++)
            {
                var t = room.Slots[i];
                var row = new Rect(x, y, w, 30);
                bool blink = t != null ? Time.unscaledTime < t.BlinkUntil : (Time.unscaledTime < room.NoWeaponBlinkUntil && i == room.EmptySlot());
                if (blink && Mathf.FloorToInt(Time.unscaledTime * 8f) % 2 == 0)
                {
                    var old = GUI.color;
                    GUI.color = new Color(Red.r, Red.g, Red.b, 0.6f);
                    GUI.DrawTexture(row, Sprites.White);
                    GUI.color = old;
                }
                if (t == null)
                {
                    GUI.Label(new Rect(x + 4, y + 6, w - 110, 22), "Slot " + (i + 1) + ": empty", small);
                    if (GUI.Button(new Rect(x + w - 105, y + 1, 105, 28), buildMenuSlot == i ? "Close" : "Build...", button))
                        buildMenuSlot = buildMenuSlot == i ? -1 : i;
                }
                else
                {
                    GUI.DrawTexture(new Rect(x + 2, y + 3, 24, 24), Sprites.Tower(t.Def.id).texture);
                    GUI.Label(new Rect(x + 30, y + 6, w - 140, 22), t.Def.name + "  Lv " + t.Level, small);
                    float cost = gm.TowerUpgradeCost(t);
                    if (GUI.Button(new Rect(x + w - 105, y + 1, 105, 28), cost < 0 ? "MAX" : "Up " + Mathf.CeilToInt(cost) + " " + ResShort(t.Def.costResource), button) && cost >= 0)
                        Report(gm.TryUpgradeTower(me, i), ResName(t.Def.costResource));
                }
                y += 32;
            }

            y += 4;
            if (GUI.Button(new Rect(x, y, w, 30), "Ask a neighbour for Dream Power", button))
            {
                string msg = gm.AskForHelp(me);
                if (!string.IsNullOrEmpty(msg)) Toast(msg);
            }

            DrawMonsterIntel(x, VH - 112, w);

            if (buildMenuSlot >= 0) DrawBuildMenu(me, px - 300, 120);
            if (gm.PendingHelpFrom != null) DrawHelpPopup();
        }

        void DrawBuildMenu(Resident me, float x, float y)
        {
            var towers = gm.Cfg.towers.towers;
            float h = 44 + towers.Length * 40;
            GUI.Box(new Rect(x, y, 290, h), GUIContent.none, box);
            GUI.Label(new Rect(x + 10, y + 8, 270, 22), "<b>Build in slot " + (buildMenuSlot + 1) + "</b>", label);
            float yy = y + 36;
            var m = gm.Monster;
            foreach (var t in towers)
            {
                string tag = "";
                int type = DamageTypes.Index(t.damageType);
                if (m != null && type >= 0)
                {
                    float mult = gm.DamageTaken(m, type);
                    if (mult > 1.05f) tag = "  <color=#9FE3C8>strong</color>";
                    else if (mult < 0.95f) tag = "  <color=#D7263D>weak</color>";
                }
                if (t.id == "faith_tower") tag = "  <color=#D7263D>+Faith</color>";
                GUI.DrawTexture(new Rect(x + 10, yy + 4, 28, 28), Sprites.Tower(t.id).texture);
                if (GUI.Button(new Rect(x + 44, yy, 236, 36), t.name + "  " + t.buildCost + " " + ResShort(t.costResource) + tag, button))
                {
                    var r = gm.TryBuildTower(me, buildMenuSlot, t.id);
                    Report(r, ResName(t.costResource));
                    if (r == ActionResult.Ok) buildMenuSlot = -1;
                }
                yy += 40;
            }
        }

        void DrawHelpPopup()
        {
            var r = new Rect(vw / 2 - 220, VH / 2 - 60, 440, 120);
            GUI.Box(r, GUIContent.none, box);
            GUI.Label(new Rect(r.x + 10, r.y + 10, r.width - 20, 40), "<b>" + gm.PendingHelpFrom.Name + "</b> begs you for Dream Power.\nGifts are final, and lost if they get eaten.", center);
            if (GUI.Button(new Rect(r.x + 30, r.y + 70, 180, 36), "Give 25%", button)) gm.AnswerHelp(true);
            if (GUI.Button(new Rect(r.x + 230, r.y + 70, 180, 36), "Ignore", button)) gm.AnswerHelp(false);
        }

        void DrawMonsterIntel(float x, float y, float w)
        {
            var m = gm.Monster;
            if (m == null) return;
            GUI.Label(new Rect(x, y, w, 20), "<b>" + m.Def.name + "</b>" + (m.Dead ? "  (banished, back soon)" : ""), small);
            string line = "Takes: ";
            for (int i = 0; i < 4; i++)
            {
                float mult = gm.DamageTaken(m, i);
                string col = mult > 1.05f ? "#9FE3C8" : mult < 0.95f ? "#D7263D" : "#E8DCC0";
                line += "<color=" + col + ">" + DamageTypes.Label(i) + " x" + mult.ToString("0.00") + "</color>  ";
            }
            GUI.Label(new Rect(x, y + 20, w, 40), line, small);
            GUI.Label(new Rect(x, y + 58, w, 40), "Parts eaten: arm " + gm.PartCount(m, "arm") + ", leg " + gm.PartCount(m, "leg") +
                ", torso " + gm.PartCount(m, "torso") + ", eye " + gm.PartCount(m, "eye"), small);
        }

        // ------------------------------------------------------------ monster

        void DrawMonsterUI()
        {
            var m = gm.Monster;
            if (m == null) return;
            HandleJoystick();

            float x = 10, y = 50, w = 320;
            GUI.Box(new Rect(x, y, w, 272), GUIContent.none, box);
            GUI.Label(new Rect(x + 10, y + 6, w - 20, 22), "<b>" + m.Def.name + "</b>   kills " + m.Kills, label);
            Bar(new Rect(x + 10, y + 30, w - 20, 10), m.Dead ? 0f : m.Hp / gm.MaxHp(m), Red);
            GUI.Label(new Rect(x + 10, y + 44, w - 20, 22), "<color=#F2C14E>Dream Power</color> " + Mathf.FloorToInt(m.DreamPower) +
                "    <color=#D7263D>Faith</color> " + Mathf.FloorToInt(m.Faith), small);
            GUI.Label(new Rect(x + 10, y + 62, w - 20, 22), "Arm " + gm.PartCount(m, "arm") + "  Leg " + gm.PartCount(m, "leg") +
                "  Torso " + gm.PartCount(m, "torso") + "  Eye " + gm.PartCount(m, "eye") + "   (max " + gm.Cfg.bodyParts.maxPartsPerType + " each)", small);

            float yy = y + 88;
            GUI.Label(new Rect(x + 10, yy, w - 20, 20), "<b>Thicken hide</b> (damage taken)", small);
            yy += 22;
            for (int i = 0; i < 4; i++)
            {
                float mult = gm.DamageTaken(m, i);
                float cost = gm.ResistCost(m, i);
                GUI.Label(new Rect(x + 10, yy + 5, 170, 22), DamageTypes.Label(i) + "  x" + mult.ToString("0.00") + "  (lv " + m.ResistLevels[i] + ")", small);
                if (GUI.Button(new Rect(x + w - 130, yy, 120, 28), cost < 0 ? "MAX" : "Buy " + cost + " DP", button) && cost >= 0)
                    Report(gm.TryUpgradeResist(m, i));
                yy += 32;
            }
            GUI.Label(new Rect(x + 10, yy + 2, w - 20, 40), "WASD / joystick: move. Touch a door to smash it. Stand on body parts to eat.", small);

            if (m.Dead)
            {
                var r = new Rect(vw / 2 - 200, VH / 2 - 30, 400, 60);
                GUI.Box(r, GUIContent.none, box);
                GUI.Label(r, "Banished! Back in " + Mathf.CeilToInt(Mathf.Max(0f, m.RespawnAt - Time.time)) + " s", center);
            }

            // joystick
            var jc = JoyCenter;
            var oldC = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            GUI.DrawTexture(new Rect(jc.x - 75, jc.y - 75, 150, 150), circleTex);
            GUI.color = new Color(Candle.r, Candle.g, Candle.b, 0.6f);
            var knob = jc + new Vector2(GameInput.Joystick.x, -GameInput.Joystick.y) * 60f;
            GUI.DrawTexture(new Rect(knob.x - 30, knob.y - 30, 60, 60), circleTex);
            GUI.color = oldC;

            // abilities
            for (int i = 0; i < m.Loadout.Length; i++)
            {
                var a = m.Loadout[i];
                var r = new Rect(vw - 130 * (m.Loadout.Length - i) - 10, VH - 130, 120, 120);
                float cd = m.Cooldowns[i];
                bool night = gm.Phase == Phase.Night;
                string text = "<b>" + a.name + "</b>\n<size=11>" + AbilityHint(a) + "</size>\n[" + (i + 1) + "]" + (cd > 0 ? "  " + Mathf.CeilToInt(cd) + "s" : "");
                GUI.enabled = cd <= 0f && night && !m.Dead;
                if (GUI.Button(r, text, button)) gm.UseAbility(i);
                GUI.enabled = true;
            }
        }

        static string AbilityHint(Config.AbilityDef a)
        {
            switch (a.effect)
            {
                case "towerDamageMultiplier": return "Guns nearby deal less";
                case "doorDamageMultiplier": return "Door damage x" + a.value;
                case "faithIncomeMultiplier": return "Cut Faith nearby";
                case "bedIncomeMultiplier": return "Beds pay less";
                case "dash": return "Dash forward";
                case "towerUntargetable": return "Towers can't see you";
                default: return a.effect;
            }
        }

        Vector2 JoyCenter => new Vector2(110f, VH - 110f);

        void HandleJoystick()
        {
            var e = Event.current;
            var mp = e.mousePosition;
            var jc = JoyCenter;
            if (e.type == EventType.MouseDown && Vector2.Distance(mp, jc) <= 95f)
            {
                joyActive = true;
                e.Use();
            }
            if (joyActive && (e.type == EventType.MouseDrag || e.type == EventType.MouseDown || e.type == EventType.Used))
            {
                var d = (mp - jc) / 60f;
                GameInput.Joystick = Vector2.ClampMagnitude(new Vector2(d.x, -d.y), 1f);
            }
            if (e.rawType == EventType.MouseUp && joyActive)
            {
                joyActive = false;
                GameInput.Joystick = Vector2.zero;
            }
        }

        // ------------------------------------------------------------ results

        void DrawResults()
        {
            var res = gm.Result;
            if (res == null) return;
            var r = new Rect(vw / 2 - 300, 120, 600, 460);
            GUI.Box(r, GUIContent.none, box);
            float y = r.y + 16;
            GUI.Label(new Rect(r.x, y, r.width, 50), res.ResidentsWin ? "THE RESIDENTS SURVIVED" : "THE MONSTER FEASTED", subtitle);
            y += 50;
            bool won = (res.Role == Role.Resident) == res.ResidentsWin;
            if (res.Role == Role.Resident && gm.Human != null)
                won = gm.Human.Alive;
            GUI.Label(new Rect(r.x, y, r.width, 30), won ? "<color=#F2C14E><b>You win.</b></color>" : "<color=#D7263D><b>You lose.</b></color>", center);
            y += 36;

            if (res.Role == Role.Resident && gm.Human != null)
                GUI.Label(new Rect(r.x + 30, y, r.width - 60, 24), "Nights survived: " + gm.Human.NightsSurvived + " / " + gm.Cfg.match.nightCount, label);
            else
                GUI.Label(new Rect(r.x + 30, y, r.width - 60, 24), "Residents eaten: " + res.Kills + " / " + gm.Cfg.match.residentCount, label);
            y += 28;
            string role = res.Role == Role.Monster ? "Monster" : "Resident";
            GUI.Label(new Rect(r.x + 30, y, r.width - 60, 24), "+" + res.XpGained + " " + role + " XP   (level " + res.LevelBefore +
                (res.LevelAfter > res.LevelBefore ? " -> <color=#F2C14E>" + res.LevelAfter + "</color>" : "") + ")", label);
            y += 36;

            foreach (var rr in gm.Residents)
            {
                string status = rr.Alive ? "<color=#9FE3C8>alive</color>" : "<color=#D7263D>eaten</color>";
                GUI.Label(new Rect(r.x + 30, y, r.width - 60, 22), rr.Name + "  -  " + status + ",  nights " + rr.NightsSurvived, small);
                y += 22;
            }

            if (GUI.Button(new Rect(r.x + 60, r.yMax - 60, 220, 44), "Play again", bigButton))
                gm.StartMatch(res.Role, monsterPick);
            if (GUI.Button(new Rect(r.xMax - 280, r.yMax - 60, 220, 44), "Main menu", bigButton))
                gm.ReturnToMenu();
        }
    }
}
