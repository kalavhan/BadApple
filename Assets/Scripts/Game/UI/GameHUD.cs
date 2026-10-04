using System.Collections.Generic;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// Mobile-first HUD drawn with IMGUI on a virtual 720 px tall canvas (scales to any landscape screen).
    /// Touch handling runs in Update so several fingers work at once: a floating joystick on the left half,
    /// round action buttons on the right, and short taps on the world to select build plates, the bed or the door.
    /// Popups for build / upgrade / sell are regular IMGUI panels. Mouse and keyboard work the same way in the editor.
    /// </summary>
    public class GameHUD : MonoBehaviour
    {
        GameManager gm;
        float scale = 1f;
        float vw = 1280f;
        const float VH = 720f;

        bool styled;
        GUIStyle box, label, small, title, subtitle, center, button, centerButton, bigButton, shadow, roundLabel, roundSmall;
        Texture2D panelTex, circleTex, ringTex;

        string monsterPick = "stitchwork_chef";

        // ---- touch / pointer state
        class Pointer
        {
            public Vector2 Start;
            public float StartTime;
            public bool Moved;
            public bool Ignore;
        }

        struct Zone
        {
            public Rect R;
            public System.Action Press;
        }

        readonly Dictionary<int, Pointer> pointers = new Dictionary<int, Pointer>();
        List<Zone> zones = new List<Zone>();
        List<Zone> nextZones = new List<Zone>();
        List<Rect> uiRects = new List<Rect>();
        List<Rect> nextUiRects = new List<Rect>();
        bool repaint;
        bool pointerInputBroken;
        const int NoPointer = int.MinValue;
        int joyId = NoPointer;
        Vector2 joyOrigin, joyKnob;
        const float JoyRadius = 62f;

        // ---- selection
        enum Sel { None, Slot, Bed, Door }
        Sel sel = Sel.None;
        int selSlot = -1;

        static readonly Color Bone = (Color)Palette.Bone;
        static readonly Color Candle = (Color)Palette.Candle;
        static readonly Color Red = (Color)Palette.AppleRed;
        static readonly Color Mint = (Color)Palette.Mint;
        static readonly Color TealC = (Color)Palette.Teal;
        static readonly Color InkC = (Color)Palette.Ink;

        void Awake()
        {
            gm = GetComponent<GameManager>();
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus)
            {
                GameInput.ClearAll();
                pointers.Clear();
                joyId = NoPointer;
            }
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
            panelTex = MakeTex(new Color(0.106f, 0.086f, 0.141f, 0.92f));
            circleTex = MakeCircle(128);
            ringTex = Sprites.Ring.texture;

            box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(10, 10, 8, 8) };
            box.normal.background = panelTex;

            label = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, richText = true };
            label.normal.textColor = Bone;
            small = new GUIStyle(label) { fontSize = 12 };
            center = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
            title = new GUIStyle(center) { fontSize = 54, fontStyle = FontStyle.Bold };
            title.normal.textColor = Candle;
            subtitle = new GUIStyle(center) { fontSize = 20, fontStyle = FontStyle.Italic };
            shadow = new GUIStyle(center) { fontSize = 14, fontStyle = FontStyle.Bold, wordWrap = false, richText = true };
            roundLabel = new GUIStyle(center) { fontSize = 19, fontStyle = FontStyle.Bold };
            roundLabel.normal.textColor = InkC;
            roundSmall = new GUIStyle(center) { fontSize = 13, fontStyle = FontStyle.Bold };
            roundSmall.normal.textColor = InkC;

            button = new GUIStyle(GUI.skin.button) { fontSize = 14, richText = true, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(8, 8, 4, 4) };
            centerButton = new GUIStyle(button) { alignment = TextAnchor.MiddleCenter };
            bigButton = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold, richText = true };
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

        Vector2 ScreenToGui(Vector2 s) => new Vector2(s.x / scale, (Screen.height - s.y) / scale);

        float GuiPerTile => gm.Cam == null ? 1f : Screen.height / (2f * gm.Cam.orthographicSize) / scale;

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

        void Panel(Rect r)
        {
            GUI.Box(r, GUIContent.none, box);
            Ui(r);
        }

        void Ui(Rect r)
        {
            if (repaint) nextUiRects.Add(r);
        }

        void AddZone(Rect r, System.Action press)
        {
            if (repaint) nextZones.Add(new Zone { R = r, Press = press });
        }

        void DrawRing(Vector2 world, float radiusTiles, Color c)
        {
            var g = WorldToGui(world);
            float d = radiusTiles * 2f * GuiPerTile;
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(g.x - d / 2f, g.y - d / 2f, d, d), ringTex);
            GUI.color = old;
        }

        void Circle(Vector2 c, float d, Color col)
        {
            var old = GUI.color;
            GUI.color = col;
            GUI.DrawTexture(new Rect(c.x - d / 2f, c.y - d / 2f, d, d), circleTex);
            GUI.color = old;
        }

        /// <summary>A round touch button (fires on finger down, so it works while the joystick is held).</summary>
        void RoundButton(Vector2 c, float d, string text, Color col, bool enabled, System.Action press, GUIStyle style = null)
        {
            Circle(c + new Vector2(0f, 3f), d, new Color(0f, 0f, 0f, 0.45f));
            Circle(c, d, enabled ? col : new Color(col.r * 0.45f, col.g * 0.45f, col.b * 0.45f, 0.75f));
            var lab = style ?? roundLabel;
            var oldC = lab.normal.textColor;
            if (!enabled) lab.normal.textColor = new Color(0.1f, 0.08f, 0.12f, 0.7f);
            GUI.Label(new Rect(c.x - d / 2f + 6f, c.y - d / 2f, d - 12f, d), text, lab);
            lab.normal.textColor = oldC;
            var r = new Rect(c.x - d / 2f, c.y - d / 2f, d, d);
            Ui(r);
            if (enabled) AddZone(r, press);
        }

        void Report(ActionResult r, string resource = "Dream Power")
        {
            switch (r)
            {
                case ActionResult.NoMoney: gm.Toast("Not enough " + resource + "."); break;
                case ActionResult.Blocked: gm.Toast("Blocked: your door can't be more than " + gm.Cfg.doors.maxLevelGapOverLowestWeapon + " levels above your weakest weapon."); break;
                case ActionResult.MaxLevel: gm.Toast("Already at max level."); break;
                case ActionResult.Invalid: gm.Toast("Can't do that right now."); break;
                case ActionResult.TooFar: gm.Toast("Get closer."); break;
            }
        }

        static string ResName(string res) => res == "faith" ? "Faith" : "Dream Power";
        static string ResShort(string res) => res == "faith" ? "<color=#D7263D>F</color>" : "<color=#F2C14E>DP</color>";

        static string Clock(float t)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(t));
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        static string RangeLabel(string rangeClass)
        {
            switch (rangeClass)
            {
                case "short": return "short range";
                case "mid": return "mid range";
                case "long": return "long range";
                default: return "";
            }
        }

        // ------------------------------------------------------------ touch input (Update)

        void Update()
        {
            if (gm == null) return;
            scale = Mathf.Max(0.01f, Screen.height / VH);
            vw = Screen.width / scale;

            if (!gm.InMatch)
            {
                ReleaseJoystick();
                pointers.Clear();
                if (sel != Sel.None) ClearSelection();
                return;
            }

            HandleKeys();
            if (pointerInputBroken) return;
            try
            {
                if (Input.touchCount > 0)
                {
                    for (int i = 0; i < Input.touchCount; i++)
                    {
                        var t = Input.GetTouch(i);
                        PointerEvent(t.fingerId, t.position, t.phase);
                    }
                }
                else
                {
                    Vector2 mp = Input.mousePosition;
                    if (Input.GetMouseButtonDown(0)) PointerEvent(-1, mp, TouchPhase.Began);
                    else if (Input.GetMouseButton(0)) PointerEvent(-1, mp, TouchPhase.Moved);
                    else if (Input.GetMouseButtonUp(0)) PointerEvent(-1, mp, TouchPhase.Ended);
                }
            }
            catch (System.InvalidOperationException)
            {
                pointerInputBroken = true;
                Debug.LogWarning("[Bad Apple Hotel] Legacy Input is disabled; touch joystick needs Active Input Handling = Both or Input Manager.");
            }
        }

        void HandleKeys()
        {
            if (gm.HumanRole == Role.Resident && gm.Human != null)
            {
                if (GameInput.ConsumePressed(KeyCode.E) || GameInput.ConsumePressed(KeyCode.Space)) DoResidentAction();
            }
            if (GameInput.ConsumePressed(KeyCode.M) && gm.HotelViewAvailable) gm.HotelView = !gm.HotelView;
            if (GameInput.ConsumePressed(KeyCode.Escape)) ClearSelection();
        }

        bool JoystickAllowed =>
            (gm.HumanRole == Role.Monster && gm.Monster != null) ||
            (gm.HumanRole == Role.Resident && gm.Human != null && gm.Human.Alive);

        bool InJoystickArea(Vector2 g) => g.x < vw * 0.45f && g.y > VH * 0.3f;

        bool OverUi(Vector2 g)
        {
            foreach (var r in uiRects) if (r.Contains(g)) return true;
            return false;
        }

        void PointerEvent(int id, Vector2 screen, TouchPhase phase)
        {
            var g = ScreenToGui(screen);
            switch (phase)
            {
                case TouchPhase.Began:
                {
                    var p = new Pointer { Start = g, StartTime = Time.unscaledTime };
                    pointers[id] = p;
                    foreach (var z in zones)
                        if (z.R.Contains(g)) { p.Ignore = true; z.Press?.Invoke(); return; }
                    if (OverUi(g)) { p.Ignore = true; return; }
                    if (joyId == NoPointer && JoystickAllowed && InJoystickArea(g))
                    {
                        joyId = id;
                        joyOrigin = g;
                        joyKnob = g;
                    }
                    break;
                }
                case TouchPhase.Moved:
                case TouchPhase.Stationary:
                {
                    if (!pointers.TryGetValue(id, out var p)) return;
                    if ((g - p.Start).magnitude > 14f) p.Moved = true;
                    if (id == joyId)
                    {
                        var d = g - joyOrigin;
                        if (d.magnitude > JoyRadius) joyOrigin = g - d.normalized * JoyRadius; // base follows the thumb
                        joyKnob = g;
                        var v = (joyKnob - joyOrigin) / JoyRadius;
                        GameInput.Joystick = p.Moved ? Vector2.ClampMagnitude(new Vector2(v.x, -v.y), 1f) : Vector2.zero;
                    }
                    break;
                }
                case TouchPhase.Ended:
                case TouchPhase.Canceled:
                {
                    if (id == joyId) ReleaseJoystick();
                    if (pointers.TryGetValue(id, out var p))
                    {
                        pointers.Remove(id);
                        if (!p.Ignore && !p.Moved && phase == TouchPhase.Ended && Time.unscaledTime - p.StartTime < 0.4f)
                            WorldTap(g);
                    }
                    break;
                }
            }
        }

        void ReleaseJoystick()
        {
            joyId = NoPointer;
            GameInput.Joystick = Vector2.zero;
        }

        void WorldTap(Vector2 g)
        {
            if (gm.Cam == null || gm.HumanRole != Role.Resident) return;
            var me = gm.Human;
            if (me == null || !me.Alive) return;
            var tile = HotelMap.ToTile(GuiToWorld(g));
            var room = me.Room;
            if (room == null)
            {
                var def = gm.Map.RoomContaining(tile);
                if (def != null) gm.Toast(gm.IsRoomFree(def) ? "Walk inside Room " + (def.Index + 1) + " to claim it." : "Room " + (def.Index + 1) + " is taken.");
                ClearSelection();
                return;
            }
            int slot = room.Def.BuildIndex(tile);
            if (slot >= 0) { sel = Sel.Slot; selSlot = slot; return; }
            if (tile == room.Def.BedTile) { sel = Sel.Bed; return; }
            if (tile == room.Def.DoorTile) { sel = Sel.Door; return; }
            ClearSelection();
        }

        void ClearSelection()
        {
            sel = Sel.None;
            selSlot = -1;
        }

        void DoResidentAction()
        {
            var me = gm.Human;
            if (me == null) return;
            var action = gm.ActionFor(me);
            var r = gm.DoAction(me);
            if (r == ActionResult.Ok) return;
            if (action == ResidentAction.DoorBroken) gm.Toast("Your door is broken. Tap it to rebuild.");
            else if (action == ResidentAction.CloseDoor && r == ActionResult.Blocked) gm.Toast("Something is standing in the doorway!");
            else if (r == ActionResult.TooFar) gm.Toast("Walk to your bed to sleep, or to your door to open or close it.");
        }

        // ------------------------------------------------------------ frame

        void OnGUI()
        {
            if (gm == null) return;
            GameInput.Handle(Event.current);
            EnsureStyles();
            scale = Mathf.Max(0.01f, Screen.height / VH);
            vw = Screen.width / scale;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            repaint = Event.current.type == EventType.Repaint;
            if (repaint)
            {
                nextZones.Clear();
                nextUiRects.Clear();
            }

            if (gm.Phase == Phase.ConfigError) DrawError();
            else if (gm.Phase == Phase.RoleSelect) DrawMenu();
            else if (gm.Cam != null && gm.Map != null)
            {
                DrawWorldLabels();
                DrawTopBar();
                DrawBanner();
                if (gm.Phase == Phase.Results) DrawResults();
                else
                {
                    if (gm.HumanRole == Role.Resident) DrawResidentUI();
                    else DrawMonsterUI();
                    DrawLog();
                    DrawToast();
                }
            }

            if (repaint)
            {
                (zones, nextZones) = (nextZones, zones);
                (uiRects, nextUiRects) = (nextUiRects, uiRects);
            }
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

            GUI.DrawTexture(new Rect(vw / 2 - 32, 180, 64, 64), Sprites.Apple.texture);

            float y = 265;
            GUI.Label(new Rect(0, y, vw, 24), "Your monster pick (played if you end up as the Monster)", center);
            y += 30;
            var defs = cfg.monsters.monsters;
            float w = 250, gap = 16, total = defs.Length * w + (defs.Length - 1) * gap;
            var cardStyle = new GUIStyle(bigButton) { fontSize = 14, fontStyle = FontStyle.Normal, alignment = TextAnchor.MiddleCenter };
            for (int i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                var r = new Rect(vw / 2 - total / 2 + i * (w + gap), y, w, 96);
                bool picked = d.id == monsterPick;
                var old = GUI.backgroundColor;
                GUI.backgroundColor = picked ? Candle : Color.white;
                if (GUI.Button(r, "<b>" + d.name + "</b>\nHP " + d.baseHealth + "  Speed " + d.moveSpeed + "\nWeak to " + WeakestTo(d), cardStyle))
                    monsterPick = d.id;
                GUI.backgroundColor = old;
                GUI.DrawTexture(new Rect(r.x + 8, r.y + 20, 36, 48), Sprites.Monster(d.id).texture, ScaleMode.ScaleToFit);
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
            GUI.Label(new Rect(vw / 2 - 440, y, 880, 80),
                "Residents: walk into a free room in the first " + cfg.match.setupSeconds + " s, shut the door, sleep for Dream Power and tap the bolted plates to build. " +
                "Stay awake when the monster is near: towers hit x" + cfg.residents.awakeWeaponDamageMultiplier + ".  " +
                "Monster: joystick / WASD to move, smash doors, eat body parts, 1 / 2 / 3 for abilities.", small);
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
            bool housed = gm.HumanRole == Role.Monster || (gm.Human != null && gm.Human.Room != null);
            string phase = gm.Phase == Phase.Setup ? (housed ? "SETUP: GET READY" : "SETUP: FIND A ROOM")
                : gm.Phase == Phase.Night ? "NIGHT " + gm.Night + " / " + gm.Cfg.match.nightCount
                : "DAWN";
            var r = new Rect(vw / 2 - 130, 8, 260, 42);
            Panel(r);
            Shadowed(new Rect(r.x, r.y + 2, r.width, 20), phase, Candle);
            if (gm.Phase != Phase.Results) Shadowed(new Rect(r.x, r.y + 20, r.width, 20), Clock(gm.PhaseTimer), Bone);

            var menu = new Rect(10, 10, 64, 30);
            Ui(menu);
            if (GUI.Button(menu, "Menu", centerButton)) { gm.ReturnToMenu(); return; }
            float[] speeds = { 1f, 2f, 4f };
            for (int i = 0; i < speeds.Length; i++)
            {
                var b = new Rect(80 + i * 40, 10, 36, 30);
                Ui(b);
                var old = GUI.backgroundColor;
                GUI.backgroundColor = Mathf.Approximately(gm.Speed, speeds[i]) ? Candle : Color.white;
                if (GUI.Button(b, speeds[i] + "x", centerButton)) gm.SetSpeed(speeds[i]);
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
            float h = 16 * gm.Log.Count + 10;
            var r = new Rect(10, 48, 300, h);
            GUI.Box(r, GUIContent.none, box);
            for (int i = 0; i < gm.Log.Count; i++)
                GUI.Label(new Rect(r.x + 8, r.y + 4 + i * 16, 290, 18), gm.Log[i], small);
        }

        void DrawToast()
        {
            if (string.IsNullOrEmpty(gm.ToastText) || Time.unscaledTime > gm.ToastUntil) return;
            var r = new Rect(vw / 2 - 300, VH - 64, 600, 36);
            GUI.Box(r, GUIContent.none, box);
            GUI.Label(r, gm.ToastText, center);
        }

        void DrawWorldLabels()
        {
            float now = Time.time;
            var me = gm.Human;
            float tile = GuiPerTile;

            foreach (var def in gm.Map.Rooms)
            {
                gm.RoomsByDef.TryGetValue(def, out var room);
                if (room == null)
                {
                    if (gm.Phase == Phase.Setup)
                    {
                        var c = WorldToGui(def.Center);
                        string text = "Room " + (def.Index + 1) + "\n" + def.BuildTiles.Count + " plates" +
                                      (def.Isolated ? "\n<color=#9FE3C8>lonely: free building</color>" : "");
                        Shadowed(new Rect(c.x - 80, c.y - 26, 160, 52), text, Bone);
                    }
                    continue;
                }

                bool mine = me != null && room.Owner == me;
                if (!gm.IsTileVisible(def.DoorTile) && !mine) continue;

                // door hp (only when the door is shut or damaged)
                if (room.DoorBroken || !room.DoorOpen || room.DoorHp < gm.MaxDoorHp(room))
                {
                    var doorG = WorldToGui(HotelMap.Center(def.DoorTile) + Vector2.up * 0.75f);
                    float frac = room.DoorBroken ? 0f : room.DoorHp / gm.MaxDoorHp(room);
                    Bar(new Rect(doorG.x - 22, doorG.y - 4, 44, 7), frac, room.DoorBroken ? Red : (room.UnderAttack(now) ? Candle : TealC));
                }
                if (mine && room.DoorOpen && !room.DoorBroken && gm.Phase == Phase.Night && Mathf.FloorToInt(Time.unscaledTime * 3f) % 2 == 0)
                {
                    var dg = WorldToGui(HotelMap.Center(def.DoorTile) + Vector2.up * 1.2f);
                    Shadowed(new Rect(dg.x - 60, dg.y - 10, 120, 20), "OPEN!", Red);
                }

                // empty plates in your own room show a "+" so it is clear where things attach
                if (mine && me.Alive)
                {
                    bool blinkEmpty = Time.unscaledTime < room.NoWeaponBlinkUntil && Mathf.FloorToInt(Time.unscaledTime * 8f) % 2 == 0;
                    for (int i = 0; i < room.Slots.Length; i++)
                    {
                        if (room.Slots[i] != null) continue;
                        var pg = WorldToGui(HotelMap.Center(def.BuildTiles[i]));
                        if (blinkEmpty)
                        {
                            var old = GUI.color;
                            GUI.color = new Color(Red.r, Red.g, Red.b, 0.55f);
                            GUI.DrawTexture(new Rect(pg.x - tile / 2, pg.y - tile / 2, tile, tile), Sprites.White);
                            GUI.color = old;
                        }
                        Shadowed(new Rect(pg.x - 10, pg.y - 10, 20, 20), "+", new Color(Candle.r, Candle.g, Candle.b, 0.8f));
                    }
                }
            }

            // residents
            bool zoomedOut = gm.HotelView || !gm.InMatch;
            foreach (var r in gm.Residents)
            {
                if (!gm.IsVisible(r.Pos) && r != me) continue;
                if (zoomedOut && r != me && r.Alive) continue; // names would pile up in the overview
                var g = WorldToGui(r.Pos + new Vector2(0f, r.Asleep ? 0.7f : 1.35f));
                string who = r.IsHuman ? "YOU" : r.Name;
                if (!r.Alive) who += " (eaten)";
                Shadowed(new Rect(g.x - 60, g.y - 10, 120, 20), who + (r.Asleep ? " <color=#9FE3C8>z</color>" : ""), r.IsHuman ? Candle : Bone);
                if (r.Alive && r.Health < gm.Cfg.match.residentHealth)
                    Bar(new Rect(g.x - 18, g.y + 8, 36, 5), r.Health / gm.Cfg.match.residentHealth, Red);
            }

            var m = gm.Monster;
            if (m != null && !m.Dead && gm.IsVisible(m.Pos))
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
                if (!gm.IsVisible(f.Pos)) continue;
                float age = Time.unscaledTime - f.Born;
                var g = WorldToGui(f.Pos) + Vector2.down * age * 30f;
                var c = f.Color;
                c.a = Mathf.Clamp01(1.4f - age);
                Shadowed(new Rect(g.x - 90, g.y - 10, 180, 20), f.Text, c);
            }
        }

        void DrawJoystick()
        {
            bool active = joyId != NoPointer;
            var origin = active ? joyOrigin : new Vector2(130f, VH - 130f);
            Circle(origin, JoyRadius * 2.3f, new Color(1f, 1f, 1f, active ? 0.16f : 0.07f));
            var knob = active ? joyKnob : origin;
            Circle(knob, 56f, new Color(Candle.r, Candle.g, Candle.b, active ? 0.65f : 0.25f));
            if (!active) Shadowed(new Rect(origin.x - 80, origin.y + 70, 160, 20), "drag here to walk", new Color(Bone.r, Bone.g, Bone.b, 0.5f));
        }

        // ------------------------------------------------------------ resident

        void DrawResidentUI()
        {
            var me = gm.Human;
            if (me == null) return;
            float now = Time.time;

            DrawResources(me, now);
            DrawMonsterIntel(new Rect(vw - 270, 92, 260, 70));

            if (!me.Alive)
            {
                var r = new Rect(vw / 2 - 220, VH - 120, 440, 50);
                GUI.Box(r, GUIContent.none, box);
                GUI.Label(r, "You haunt the hallway now. Watch the others try to make it to dawn.", center);
                return;
            }

            DrawJoystick();

            // action button + helpers (bottom right)
            var action = gm.ActionFor(me);
            string text;
            Color col = Candle;
            switch (action)
            {
                case ResidentAction.Wake: text = "Wake up"; col = Mint; break;
                case ResidentAction.Sleep: text = "Sleep"; col = Mint; break;
                case ResidentAction.CloseDoor: text = "Close\ndoor"; col = Candle; break;
                case ResidentAction.OpenDoor: text = "Open\ndoor"; col = Candle; break;
                case ResidentAction.DoorBroken: text = "Door\nbroken"; col = Red; break;
                default: text = me.Room == null ? "Find a\nroom" : "Go to bed\nor door"; break;
            }
            var ac = new Vector2(vw - 105f, VH - 110f);
            RoundButton(ac, 150f, text, col, action != ResidentAction.None, DoResidentAction);

            if (me.Room != null)
                RoundButton(new Vector2(vw - 245f, VH - 62f), 78f, "Ask\nhelp", Bone, true, () =>
                {
                    string msg = gm.AskForHelp(me);
                    if (!string.IsNullOrEmpty(msg)) gm.Toast(msg);
                }, roundSmall);
            if (gm.HotelViewAvailable)
                RoundButton(new Vector2(vw - 245f, VH - 158f), 78f, gm.HotelView ? "Back" : "Hotel\nview", (Color)Palette.Shade(Palette.Mint, 0.9f), true,
                    () => gm.HotelView = !gm.HotelView, roundSmall);

            // hints
            string hint = null;
            if (me.Room == null) hint = gm.Phase == Phase.Setup ? "Walk into a free room to claim it (its door is open)." : "";
            else if (gm.Phase == Phase.Night && !me.Asleep && gm.OnBed(me)) hint = "Awake: no Dream Power, but your towers hit x" + gm.Cfg.residents.awakeWeaponDamageMultiplier + ".";
            else if (gm.Phase == Phase.Setup && me.Room.DoorOpen) hint = "Shut your door before the lights go out. Tap a bolted plate to build.";
            if (!string.IsNullOrEmpty(hint))
                Shadowed(new Rect(vw / 2 - 300, VH - 96, 600, 24), hint, Bone);

            if (me.Room != null) DrawSelection(me);
            if (gm.PendingHelpFrom != null) DrawHelpPopup();
        }

        void DrawResources(Resident me, float now)
        {
            var r = new Rect(vw - 270, 8, 260, 80);
            Panel(r);
            float dps = gm.DreamPerSecond(me, now);
            string sleepTag = me.Room == null ? "" : me.Asleep ? "  <color=#9FE3C8>asleep</color>" : "  <color=#E8DCC0>awake</color>";
            GUI.Label(new Rect(r.x + 10, r.y + 6, r.width - 20, 22),
                "<color=#F2C14E><b>Dream Power</b></color>  " + Mathf.FloorToInt(me.DreamPower) + "  <size=12>+" + dps.ToString("0.#") + "/s</size>" + sleepTag, label);
            GUI.DrawTexture(new Rect(r.x + 10, r.y + 33, 18, 18), Sprites.Apple.texture);
            bool blackout = now < me.FaithBlockedUntil;
            float fps = me.Room != null ? gm.FaithPerSecond(me.Room) : 0f;
            GUI.Label(new Rect(r.x + 32, r.y + 31, r.width - 40, 22), "<color=#D7263D><b>Faith</b></color>  " + Mathf.FloorToInt(me.Faith) +
                "  <size=12>+" + fps.ToString("0.#") + "/s</size>" + (blackout ? "  <color=#D7263D><b>BLACKOUT</b></color>" : ""), label);
            string roomLine = me.Room == null ? "No room yet" : "Room " + (me.Room.Def.Index + 1) + "  ·  door " + me.Room.DoorLevel + (me.Room.DoorBroken ? " <color=#D7263D>broken</color>" : me.Room.DoorOpen ? " <color=#F2C14E>open</color>" : " shut") +
                "  ·  bed " + me.Room.BedLevel;
            GUI.Label(new Rect(r.x + 10, r.y + 56, r.width - 20, 20), roomLine, small);
        }

        void DrawMonsterIntel(Rect r)
        {
            var m = gm.Monster;
            if (m == null) return;
            Panel(r);
            GUI.Label(new Rect(r.x + 8, r.y + 4, r.width - 16, 20), "<b>" + m.Def.name + "</b>" + (m.Dead ? "  <size=11>(banished, back soon)</size>" : ""), small);
            string line = "";
            for (int i = 0; i < 4; i++)
            {
                float mult = gm.DamageTaken(m, i);
                string col = mult > 1.05f ? "#9FE3C8" : mult < 0.95f ? "#D7263D" : "#E8DCC0";
                line += "<color=" + col + ">" + DamageTypes.Label(i) + " x" + mult.ToString("0.0#") + "</color>  ";
            }
            GUI.Label(new Rect(r.x + 8, r.y + 22, r.width - 16, 20), line, small);
            GUI.Label(new Rect(r.x + 8, r.y + 42, r.width - 16, 20), "Ate: arm " + gm.PartCount(m, "arm") + "  leg " + gm.PartCount(m, "leg") +
                "  torso " + gm.PartCount(m, "torso") + "  eye " + gm.PartCount(m, "eye"), small);
        }

        // ------------------------------------------------------------ selection popups

        Rect PopupRect(Vector2 world, float w, float h)
        {
            var g = WorldToGui(world);
            float x = g.x + GuiPerTile * 0.8f;
            if (x + w > vw - 280) x = g.x - GuiPerTile * 0.8f - w;   // keep clear of the right-hand controls
            x = Mathf.Clamp(x, 10f, vw - w - 10f);
            float y = Mathf.Clamp(g.y - h / 2f, 56f, VH - h - 10f);
            return new Rect(x, y, w, h);
        }

        bool CloseButton(Rect panel)
        {
            var r = new Rect(panel.xMax - 34, panel.y + 6, 28, 26);
            return GUI.Button(r, "x", centerButton);
        }

        void DrawSelection(Resident me)
        {
            var room = me.Room;
            switch (sel)
            {
                case Sel.Slot:
                    if (selSlot < 0 || selSlot >= room.Slots.Length) { ClearSelection(); return; }
                    if (room.Slots[selSlot] == null) DrawBuildMenu(me, room, selSlot);
                    else DrawTowerMenu(me, room, room.Slots[selSlot]);
                    break;
                case Sel.Bed: DrawBedMenu(me, room); break;
                case Sel.Door: DrawDoorMenu(me, room); break;
            }
        }

        void DrawBuildMenu(Resident me, Room room, int slot)
        {
            var tile = room.Def.BuildTiles[slot];
            var wpos = HotelMap.Center(tile);
            var rt = gm.Cfg.towers.rangeTiles;
            DrawRing(wpos, rt.@short, new Color(1f, 1f, 1f, 0.35f));
            DrawRing(wpos, rt.mid, new Color(1f, 1f, 1f, 0.25f));
            DrawRing(wpos, rt.@long, new Color(1f, 1f, 1f, 0.18f));
            HighlightTile(tile);

            var towers = gm.Cfg.towers.towers;
            float h = 46 + towers.Length * 44;
            var r = PopupRect(wpos, 330, h);
            Panel(r);
            GUI.Label(new Rect(r.x + 10, r.y + 8, r.width - 50, 22), "<b>Build here</b>  <size=11>(rings: short / mid / long)</size>", label);
            if (CloseButton(r)) { ClearSelection(); return; }
            float yy = r.y + 40;
            var m = gm.Monster;
            foreach (var t in towers)
            {
                string tag;
                int type = DamageTypes.Index(t.damageType);
                if (type >= 0)
                {
                    tag = RangeLabel(t.rangeClass);
                    if (m != null)
                    {
                        float mult = gm.DamageTaken(m, type);
                        if (mult > 1.05f) tag += " <color=#9FE3C8>strong</color>";
                        else if (mult < 0.95f) tag += " <color=#D7263D>weak</color>";
                    }
                }
                else tag = t.effect == "clairvoyance" ? "<color=#9FE3C8>see the whole hotel</color>" : "<color=#D7263D>+Faith</color>";

                bool afford = gm.Wallet(me, t.costResource) >= t.buildCost;
                GUI.DrawTexture(new Rect(r.x + 10, yy + 6, 28, 28), Sprites.Tower(t.id).texture, ScaleMode.ScaleToFit);
                var old = GUI.color;
                if (!afford) GUI.color = new Color(1f, 1f, 1f, 0.55f);
                if (GUI.Button(new Rect(r.x + 44, yy, r.width - 54, 40),
                        "<b>" + t.name + "</b>  " + t.buildCost + " " + ResShort(t.costResource) + "\n<size=11>" + tag + "</size>", button))
                {
                    var res = gm.TryBuildTower(me, slot, t.id);
                    Report(res, ResName(t.costResource));
                }
                GUI.color = old;
                yy += 44;
            }
        }

        void DrawTowerMenu(Resident me, Room room, TowerInstance t)
        {
            var wpos = HotelMap.Center(t.Tile);
            if (t.IsWeapon) DrawRing(wpos, gm.TowerRange(t), new Color(Candle.r, Candle.g, Candle.b, 0.55f));
            HighlightTile(t.Tile);

            var r = PopupRect(wpos, 300, 168);
            Panel(r);
            GUI.DrawTexture(new Rect(r.x + 10, r.y + 10, 32, 32), Sprites.Tower(t.Def.id).texture, ScaleMode.ScaleToFit);
            int max = gm.TowerMaxLevel(t);
            GUI.Label(new Rect(r.x + 50, r.y + 8, r.width - 90, 22), "<b>" + t.Def.name + "</b>  Lv " + t.Level + "/" + max, label);
            if (CloseButton(r)) { ClearSelection(); return; }

            string info;
            if (t.IsWeapon)
            {
                var sc = gm.Cfg.towers.levelScaling;
                int lv = t.Level - 1;
                float dmg = t.Def.damage * Mathf.Pow(sc.damage, lv) * gm.OwnerDamageMult(room);
                float rate = t.Def.shotsPerSecond * Mathf.Pow(sc.fireRate, lv);
                info = RangeLabel(t.Def.rangeClass) + " (" + gm.TowerRange(t).ToString("0.#") + " tiles)  ·  " +
                       (t.Def.damageType == "slow" ? "slows " + Mathf.RoundToInt(t.Def.slowPct * 100) + "%" : Mathf.RoundToInt(dmg * rate) + " dmg/s");
            }
            else if (t.IsClairvoyance) info = "You can see the whole hotel. Use Hotel view to look around.";
            else info = "+" + (t.Def.faithPerSecond * Mathf.Pow(t.Def.faithLevelScaling, t.Level - 1)).ToString("0.#") + " Faith/s";
            GUI.Label(new Rect(r.x + 50, r.y + 30, r.width - 60, 40), info, small);

            float cost = gm.TowerUpgradeCost(t);
            var ub = new Rect(r.x + 10, r.y + 74, r.width - 20, 38);
            if (cost < 0f) GUI.Label(ub, "Max level", center);
            else if (GUI.Button(ub, "<b>Upgrade</b>  " + Mathf.CeilToInt(cost) + " " + ResShort(t.Def.costResource), button))
                Report(gm.TryUpgradeTower(me, t.SlotIndex), ResName(t.Def.costResource));

            float refund = gm.TowerSellValue(t);
            if (GUI.Button(new Rect(r.x + 10, r.y + 118, r.width - 20, 38), "<b>Sell</b>  +" + Mathf.FloorToInt(refund) + " " + ResShort(t.Def.costResource) +
                                                                      "  <size=11>(" + Mathf.RoundToInt(gm.Cfg.towers.sellRefundPct * 100) + "% back)</size>", button))
            {
                Report(gm.TrySellTower(me, t.SlotIndex));
                ClearSelection();
            }
        }

        void DrawBedMenu(Resident me, Room room)
        {
            var tile = room.Def.BedTile;
            HighlightTile(tile);
            var r = PopupRect(HotelMap.Center(tile), 300, 124);
            Panel(r);
            var bedCfg = gm.Cfg.beds.levels[room.BedLevel - 1];
            GUI.Label(new Rect(r.x + 10, r.y + 8, r.width - 50, 22), "<b>" + bedCfg.name + "</b>  Lv " + room.BedLevel + "/" + gm.Cfg.beds.levels.Length, label);
            if (CloseButton(r)) { ClearSelection(); return; }
            GUI.Label(new Rect(r.x + 10, r.y + 32, r.width - 20, 36), "+" + gm.BedRate(room).ToString("0.#") + " Dream Power/s while you sleep in it.", small);
            float cost = gm.BedUpgradeCost(room);
            var ub = new Rect(r.x + 10, r.y + 74, r.width - 20, 38);
            if (cost < 0f) GUI.Label(ub, "Max level", center);
            else if (GUI.Button(ub, "<b>Upgrade</b>  " + cost + " " + ResShort("dreamPower") + "  <size=11>→ " + gm.Cfg.beds.levels[room.BedLevel].name + "</size>", button))
                Report(gm.TryUpgradeBed(me));
        }

        void DrawDoorMenu(Resident me, Room room)
        {
            var tile = room.Def.DoorTile;
            HighlightTile(tile);
            var check = gm.CheckDoor(room);
            float h = !check.AtMaxLevel && !check.Allowed ? 150 : 124;
            var r = PopupRect(HotelMap.Center(tile), 300, h);
            Panel(r);
            GUI.Label(new Rect(r.x + 10, r.y + 8, r.width - 50, 22), "<b>Door</b>  Lv " + room.DoorLevel + "/" + gm.Cfg.doors.levels.Length +
                (room.DoorBroken ? "  <color=#D7263D>BROKEN</color>" : room.DoorOpen ? "  <color=#F2C14E>open</color>" : "  shut"), label);
            if (CloseButton(r)) { ClearSelection(); return; }
            Bar(new Rect(r.x + 10, r.y + 36, r.width - 20, 10), room.DoorBroken ? 0f : room.DoorHp / gm.MaxDoorHp(room), room.UnderAttack(Time.time) ? Candle : TealC);
            GUI.Label(new Rect(r.x + 10, r.y + 48, r.width - 20, 20), Mathf.RoundToInt(room.DoorHp) + " / " + gm.MaxDoorHp(room) + " HP  ·  " +
                Mathf.RoundToInt(gm.Cfg.doors.levels[room.DoorLevel - 1].damageResistancePct * 100) + "% resist", small);

            string doorLabel;
            if (check.AtMaxLevel) doorLabel = room.DoorBroken ? "<b>Rebuild</b>  " + gm.DoorRepairCostAtMax(room) + " " + ResShort("dreamPower") : null;
            else doorLabel = (room.DoorBroken ? "<b>Rebuild + upgrade</b>  " : "<b>Upgrade</b>  ") + check.Cost + " " + ResShort("dreamPower");
            var ub = new Rect(r.x + 10, r.y + 74, r.width - 20, 38);
            if (doorLabel == null) GUI.Label(ub, "Max level", center);
            else if (GUI.Button(ub, doorLabel, button)) Report(gm.TryUpgradeDoor(me));
            if (!check.AtMaxLevel && !check.Allowed)
                GUI.Label(new Rect(r.x + 10, r.y + 116, r.width - 20, 30), "<color=#D7263D>Gap rule: upgrade your weakest weapon first (it's blinking).</color>", small);
        }

        void HighlightTile(Vector2Int t)
        {
            var g = WorldToGui(HotelMap.Center(t));
            float s = GuiPerTile;
            var old = GUI.color;
            GUI.color = new Color(Candle.r, Candle.g, Candle.b, 0.35f + 0.2f * Mathf.Sin(Time.unscaledTime * 6f));
            GUI.DrawTexture(new Rect(g.x - s / 2, g.y - s / 2, s, s), Sprites.White);
            GUI.color = old;
        }

        void DrawHelpPopup()
        {
            var r = new Rect(vw / 2 - 220, VH / 2 - 60, 440, 120);
            Panel(r);
            GUI.Label(new Rect(r.x + 10, r.y + 10, r.width - 20, 40), "<b>" + gm.PendingHelpFrom.Name + "</b> begs you for Dream Power.\nGifts are final, and lost if they get eaten.", center);
            var cs = centerButton;
            if (GUI.Button(new Rect(r.x + 30, r.y + 70, 180, 36), "Give 25%", cs)) gm.AnswerHelp(true);
            if (GUI.Button(new Rect(r.x + 230, r.y + 70, 180, 36), "Ignore", cs)) gm.AnswerHelp(false);
        }

        // ------------------------------------------------------------ monster

        void DrawMonsterUI()
        {
            var m = gm.Monster;
            if (m == null) return;

            float w = 300;
            var panel = new Rect(10, 150, w, 238);
            Panel(panel);
            float x = panel.x, y = panel.y;
            GUI.Label(new Rect(x + 10, y + 6, w - 20, 22), "<b>" + m.Def.name + "</b>   kills " + m.Kills, label);
            Bar(new Rect(x + 10, y + 30, w - 20, 10), m.Dead ? 0f : m.Hp / gm.MaxHp(m), Red);
            GUI.Label(new Rect(x + 10, y + 44, w - 20, 22), "<color=#F2C14E>Dream Power</color> " + Mathf.FloorToInt(m.DreamPower) +
                "    <color=#D7263D>Faith</color> " + Mathf.FloorToInt(m.Faith), small);
            GUI.Label(new Rect(x + 10, y + 62, w - 20, 22), "Arm " + gm.PartCount(m, "arm") + "  Leg " + gm.PartCount(m, "leg") +
                "  Torso " + gm.PartCount(m, "torso") + "  Eye " + gm.PartCount(m, "eye") + "   (max " + gm.Cfg.bodyParts.maxPartsPerType + ")", small);

            float yy = y + 86;
            GUI.Label(new Rect(x + 10, yy, w - 20, 20), "<b>Thicken hide</b> (damage taken)", small);
            yy += 22;
            for (int i = 0; i < 4; i++)
            {
                float mult = gm.DamageTaken(m, i);
                float cost = gm.ResistCost(m, i);
                GUI.Label(new Rect(x + 10, yy + 5, 170, 22), DamageTypes.Label(i) + "  x" + mult.ToString("0.00") + "  (lv " + m.ResistLevels[i] + ")", small);
                if (GUI.Button(new Rect(x + w - 120, yy, 110, 28), cost < 0 ? "MAX" : "Buy " + cost + " DP", centerButton) && cost >= 0)
                    Report(gm.TryUpgradeResist(m, i));
                yy += 30;
            }

            if (m.Dead)
            {
                var r = new Rect(vw / 2 - 200, VH / 2 - 30, 400, 60);
                GUI.Box(r, GUIContent.none, box);
                GUI.Label(r, "Banished! Back in " + Mathf.CeilToInt(Mathf.Max(0f, m.RespawnAt - Time.time)) + " s", center);
            }

            DrawJoystick();

            // abilities as round buttons, right side
            bool night = gm.Phase == Phase.Night;
            for (int i = 0; i < m.Loadout.Length; i++)
            {
                var a = m.Loadout[i];
                float cd = m.Cooldowns[i];
                int idx = i;
                var c = new Vector2(vw - 80f - (m.Loadout.Length - 1 - i) * 125f, VH - 85f - (i == m.Loadout.Length - 1 ? 0f : 40f));
                string text = a.name + "\n<size=11>" + AbilityHint(a) + "</size>" + (cd > 0 ? "\n" + Mathf.CeilToInt(cd) + "s" : "\n[" + (i + 1) + "]");
                RoundButton(c, 112f, text, Mint, cd <= 0f && night && !m.Dead, () => gm.UseAbility(idx), roundSmall);
            }
            if (gm.HotelViewAvailable)
                RoundButton(new Vector2(vw - 80f, VH - 250f), 72f, gm.HotelView ? "Back" : "Hotel\nview", Bone, true, () => gm.HotelView = !gm.HotelView, roundSmall);
        }

        static string AbilityHint(Config.AbilityDef a)
        {
            switch (a.effect)
            {
                case "towerDamageMultiplier": return "Guns deal less";
                case "doorDamageMultiplier": return "Door dmg x" + a.value;
                case "faithIncomeMultiplier": return "Cut Faith";
                case "bedIncomeMultiplier": return "Beds pay less";
                case "dash": return "Dash";
                case "towerUntargetable": return "Unseen";
                default: return a.effect;
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
