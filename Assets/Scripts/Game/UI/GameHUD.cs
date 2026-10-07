using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using BadAppleHotel.Rules;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// Mobile-first HUD drawn with IMGUI on a virtual 720 px tall canvas (scales to any landscape screen).
    /// Touch handling runs in Update so several fingers work at once: a fixed translucent joystick at bottom left,
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

        bool choosingEndless;
        string monsterPick = "stitchwork_chef";

        // ---- touch / pointer state
        class Pointer
        {
            public Vector2 Start;
            public Vector2 Current;
            public float StartTime;
            public bool Moved;
            public bool Ignore;
            /// <summary>Began in the middle of the screen, away from the joystick and buttons: may pinch-zoom.</summary>
            public bool Middle;
            /// <summary>Took part in a pinch; never drags or taps until lifted.</summary>
            public bool Pinched;
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
        List<Rect> hudRects = new List<Rect>();
        List<Rect> popupRects = new List<Rect>();
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
            styled = true;
        }

        // ------------------------------------------------------------ helpers

        Vector2 WorldToGui(Vector2 w)
        {
            var sp = gm.Cam.WorldToScreenPoint(new Vector3(w.x, w.y, 0f));
            return new Vector2(sp.x / scale, (Screen.height - sp.y) / scale);
        }

        Vector2 GuiToWorld(Vector2 g)
        {
            return HotelView3D.GroundPoint(gm.Cam, new Vector2(g.x * scale, Screen.height - g.y * scale));
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
            Ui(r);
        }

        void Ui(Rect r)
        {
            if (repaint) nextUiRects.Add(r);
        }

        void AddZone(Rect r, System.Action press)
        {
            if (repaint) nextZones.Add(new Zone { R = r, Press = press });
            Ui(r);
        }

        void DrawRing(Vector2 world, float radiusTiles, Color c)
        {
            var g = WorldToGui(world);
            float d = radiusTiles * 2f * GuiPerTile;
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(g.x - d / 2f, g.y - d * 0.383f, d, d * 0.766f), ringTex);
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
            bool down = pointers.Values.Any(p => (p.Current - c).sqrMagnitude < d * d * 0.25f);
            float opacity = enabled ? (down ? 0.82f : 0.35f) : 0.2f;
            Circle(c, d, new Color(InkC.r, InkC.g, InkC.b, opacity * 0.32f));
            var saved = GUI.color;
            GUI.color = new Color(col.r, col.g, col.b, opacity);
            GUI.DrawTexture(new Rect(c.x - d / 2f, c.y - d / 2f, d, d), ringTex);
            // Four iron rivets keep the thin frame legible against the floor.
            foreach (var v in new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right })
                Circle(c + v * (d * 0.47f), 5f, new Color(Bone.r, Bone.g, Bone.b, opacity));
            GUI.color = saved;
            var lab = style ?? roundLabel;
            var oldC = lab.normal.textColor;
            lab.normal.textColor = new Color(Bone.r, Bone.g, Bone.b, enabled ? (down ? 1f : 0.75f) : 0.3f);
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
                case ActionResult.Blocked: gm.Toast("Your weakest weapon limits this door upgrade. Upgrade the blinking weapon first."); break;
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
            gm.SelectedBuildSlot = sel == Sel.Slot ? selSlot : -1;

            if (!gm.InMatch)
            {
                ReleaseJoystick();
                pointers.Clear();
                if (sel != Sel.None) ClearSelection();
                return;
            }

            gm.WallFocusRoom = sel != Sel.None ? gm.Human?.Room?.Def : null;
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
                    // Editor and desktop stand-in for the pinch: the scroll wheel over the middle of the screen.
                    if (Input.mouseScrollDelta.y != 0f && InPinchArea(ScreenToGui(mp))) gm.SetZoom(gm.Zoom * (1f + Input.mouseScrollDelta.y * .05f));
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
            if (gm.Human != null)
            {
                if (GameInput.ConsumePressed(KeyCode.E) || GameInput.ConsumePressed(KeyCode.Space)) DoResidentAction();
                if (GameInput.ConsumePressed(KeyCode.F)) ShootResident();
            }
            if (GameInput.ConsumePressed(KeyCode.M) && gm.HotelViewAvailable) gm.HotelView = !gm.HotelView;
            if (GameInput.ConsumePressed(KeyCode.R)) gm.RecenterCamera();
            if (GameInput.ConsumePressed(KeyCode.Escape)) ClearSelection();
        }

        void ShootResident()
        {
            var me = gm.Human;
            var result = gm.TryResidentShoot(me);
            if (result == ActionResult.TooFar) gm.Toast("No monster in sight within " + gm.Cfg.residents.personalShotRangeTiles + " tiles.");
            else if (result == ActionResult.Blocked && me != null)
                gm.Toast(me.Asleep ? "Wake up before shooting." : "Your shot is recharging.");
        }

        bool JoystickAllowed =>
            (gm.HumanRole == Role.Monster && gm.Monster != null) ||
            (gm.Human != null && gm.Human.Alive && !gm.Human.Asleep);

        Vector2 JoystickCenter => new Vector2(100f + Screen.safeArea.xMin / scale,
            VH - 100f - Screen.safeArea.yMin / scale);
        bool InJoystickArea(Vector2 g) => (g - JoystickCenter).sqrMagnitude <= (JoyRadius + 20f) * (JoyRadius + 20f);

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
                    var p = new Pointer { Start = g, Current = g, StartTime = Time.unscaledTime };
                    pointers[id] = p;
                    if (popupRects.Any(r => r.Contains(g))) { p.Ignore = true; return; }
                    foreach (var z in zones)
                        if (z.R.Contains(g)) { p.Ignore = true; z.Press?.Invoke(); return; }
                    if (OverUi(g)) { p.Ignore = true; return; }
                    p.Middle = InPinchArea(g);
                    if (joyId == NoPointer && JoystickAllowed && InJoystickArea(g))
                    {
                        joyId = id;
                        p.Moved = true; // joystick touches never fall through to world selection
                        joyOrigin = JoystickCenter;
                        joyKnob = joyOrigin + Vector2.ClampMagnitude(g - joyOrigin, JoyRadius);
                        var v = (joyKnob - joyOrigin) / JoyRadius;
                        GameInput.Joystick = new Vector2(v.x, -v.y);
                    }
                    break;
                }
                case TouchPhase.Moved:
                case TouchPhase.Stationary:
                {
                    if (!pointers.TryGetValue(id, out var p)) return;
                    var previous = p.Current;
                    p.Current = g;
                    if ((g - p.Start).magnitude * scale > 12f) p.Moved = true;
                    if (id != joyId && Pinch(id, p, previous, g)) break;
                    if (id != joyId && !p.Ignore && !p.Pinched && p.Moved && phase == TouchPhase.Moved)
                    {
                        var d = (g - previous) * scale;
                        gm.DragCamera(new Vector2(d.x, -d.y));
                    }
                    if (id == joyId)
                    {
                        var d = g - joyOrigin;
                        joyKnob = joyOrigin + Vector2.ClampMagnitude(d, JoyRadius);
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

        /// <summary>The central half of the screen: pinches there zoom, so they never fight the
        /// joystick, the action buttons or the HUD panels along the edges.</summary>
        bool InPinchArea(Vector2 g) => g.x > vw * .25f && g.x < vw * .75f && g.y > VH * .2f && g.y < VH * .8f;

        /// <summary>Two fingers that both began in the middle of the screen zoom like a photo:
        /// spreading them zooms in, pinching zooms out.</summary>
        bool Pinch(int id, Pointer p, Vector2 previous, Vector2 g)
        {
            if (!p.Middle || p.Ignore) return false;
            Pointer other = null;
            foreach (var kv in pointers)
                if (kv.Key != id && kv.Key != joyId && kv.Value.Middle && !kv.Value.Ignore) { other = kv.Value; break; }
            if (other == null) return false;
            p.Pinched = other.Pinched = p.Moved = other.Moved = true;
            float before = (previous - other.Current).magnitude, after = (g - other.Current).magnitude;
            if (before > 1f && after > 1f) gm.SetZoom(gm.Zoom * after / before);
            return true;
        }

        void ReleaseJoystick()
        {
            joyId = NoPointer;
            GameInput.Joystick = Vector2.zero;
        }

        void WorldTap(Vector2 g)
        {
            if (gm.Cam == null || gm.Human == null) return;
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
            if (room.Def.IsBedTile(tile)) { sel = Sel.Bed; return; }
            if (tile == room.Def.DoorTile) { sel = Sel.Door; return; }
            if (tile == room.Def.DoorInside) gm.Toast("The doorway square stays clear.");
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
            if (GUI.skin == null) return;
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

            if (repaint && (gm.Phase == Phase.RoleSelect || gm.Phase == Phase.Results)) popupRects.Clear();
            if (gm.Phase == Phase.ConfigError) DrawError();
            else if (gm.Cfg == null) GUI.Label(new Rect(0, VH / 2 - 25, vw, 50), "Opening the hotel…", subtitle);
            else if (gm.Phase == Phase.RoleSelect) DrawMenu();
            else if (gm.Cam != null && gm.Map != null)
            {
                DrawWorldLabels();
                DrawTopBar();
                DrawBanner();
                if (gm.Phase == Phase.Results) DrawResults();
                else
                {
                    DrawLog();
                    DrawToast();
                    if (gm.HumanRole == Role.Resident || gm.Phase == Phase.Setup) DrawResidentUI();
                    else DrawMonsterUI();
                    int popupStart = nextUiRects.Count;
                    if (repaint) hudRects = new List<Rect>(nextUiRects);
                    if (gm.Human != null && gm.Human.Alive && gm.Human.Room != null) DrawSelection(gm.Human);
                    if (gm.PendingHelpFrom != null) DrawHelpPopup();
                    DrawProgressChoice();
                    if (repaint) popupRects = nextUiRects.Skip(popupStart).ToList();
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

            float y = 244;
            GUI.Label(new Rect(0, y, vw, 24), "Your monster pick (played if you end up as the Monster)", center);
            y += 26;
            var defs = cfg.monsters.monsters;
            float w = 250, gap = 16, total = defs.Length * w + (defs.Length - 1) * gap;
            var cardStyle = new GUIStyle(bigButton) { fontSize = 14, fontStyle = FontStyle.Normal, alignment = TextAnchor.MiddleCenter };
            for (int i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                var r = new Rect(vw / 2 - total / 2 + i * (w + gap), y, w, 84);
                bool picked = d.id == monsterPick;
                var old = GUI.backgroundColor;
                GUI.backgroundColor = picked ? Candle : Color.white;
                if (GUI.Button(r, "<b>" + d.name + "</b>\nHP " + d.baseHealth + "  Speed " + d.moveSpeed + "\nWeak to " + WeakestTo(d), cardStyle))
                    monsterPick = d.id;
                GUI.backgroundColor = old;
                DrawSpriteFit(MenuSprite(d.id, 2.1f), new Rect(r.x + 6, r.y + 6, 50, r.height - 12));
            }

            y += 96;
            GUI.Label(new Rect(0, y, vw, 24), "Your resident (played if you end up as a Resident)", center);
            y += 26;
            var roster = cfg.residents.roster;
            int n = roster.Length + 1;
            float cw = Mathf.Min(112,(vw-80)/n-8), cg = 8, ctotal = n * cw + (n - 1) * cg;
            var rcard = new GUIStyle(cardStyle) { fontSize = 12, alignment = TextAnchor.LowerCenter };
            for (int i = 0; i < n; i++)
            {
                var r = new Rect(vw / 2 - ctotal / 2 + i * (cw + cg), y, cw, 112);
                bool random = i == 0;
                var c = random ? null : roster[i - 1];
                bool picked = random ? string.IsNullOrEmpty(gm.PickedCharacter) : gm.PickedCharacter == c.id;
                var old = GUI.backgroundColor;
                GUI.backgroundColor = picked ? Candle : Color.white;
                if (GUI.Button(r, random ? "<b>Random</b>" : "<b>" + c.name + "</b>", rcard)) gm.PickedCharacter = random ? null : c.id;
                GUI.backgroundColor = old;
                if (random) Shadowed(new Rect(r.x, r.y + 20, r.width, 50), "?", Candle, title);
                else DrawSpriteFit(MenuSprite(c.art, 1.55f, true), new Rect(r.x + 6, r.y + 4, r.width - 12, r.height - 30));
            }
            y += 116;
            var shown = roster.FirstOrDefault(c => c.id == gm.PickedCharacter);
            GUI.Label(new Rect(vw / 2 - 440, y, 880, 40), shown == null ? "Random seat: you could be any of the seven guests." :
                "<b>" + shown.fullName + "</b>, " + shown.title + ". " + shown.bio, center);

            y += 46;
            float bw = Mathf.Min(220, (vw-100)/4);
            if (GUI.Button(new Rect(vw / 2 - bw * 2f - 15, y, bw, 56), "Play as Resident", bigButton))
                gm.StartMatch(Role.Resident, monsterPick);
            if (GUI.Button(new Rect(vw / 2 - bw - 5, y, bw, 56), "Play as Monster", bigButton))
                gm.StartMatch(Role.Monster, monsterPick);
            if (GUI.Button(new Rect(vw / 2 + 5, y, bw, 56), "Random role", bigButton))
                gm.StartMatch(Random.value < 1f / cfg.match.playersPerMatch ? Role.Monster : Role.Resident, monsterPick);

            if (GUI.Button(new Rect(vw/2 + bw + 15, y, bw, 56), "Endless", bigButton)) choosingEndless = !choosingEndless;
            if (choosingEndless)
            {
                var choose = new Rect(vw/2-240, 480, 480, 120); Panel(choose);
                GUI.Label(new Rect(choose.x,choose.y+8,480,30), "Endless · choose your role", center);
                if (GUI.Button(new Rect(choose.x+15,choose.y+50,215,48), "Resident", bigButton)) { choosingEndless=false; gm.StartEndless(Role.Resident,monsterPick); }
                if (GUI.Button(new Rect(choose.x+250,choose.y+50,215,48), "Monster", bigButton)) { choosingEndless=false; gm.StartEndless(Role.Monster,monsterPick); }
            }
            y += 70;
            AccountProgress.Progress(cfg, Role.Resident, out int rl, out double ri, out double rn);
            AccountProgress.Progress(cfg, Role.Monster, out int ml, out double mi, out double mn);
            GUI.Label(new Rect(0, y, vw, 24), "Resident level " + rl + " (" + ri.ToString("0") + "/" + rn.ToString("0") + " XP)     Monster level " + ml + " (" + mi.ToString("0") + "/" + mn.ToString("0") + " XP)", center);
            y += 28;
            GUI.Label(new Rect(vw / 2 - 440, y, 880, 60),
                "Residents: walk into a free room in the first " + cfg.match.setupSeconds + " s, shut the door, sleep for Dream Power and tap the floor to build. " +
                "Stay awake when the monster is near: towers hit x" + cfg.residents.awakeWeaponDamageMultiplier + ".  " +
                "Monster: joystick / WASD to move, smash doors, eat body parts, 1 / 2 / 3 for abilities.", small);
        }

        /// <summary>Portrait for menus: the first idle frame facing the camera, else the legacy static sprite.</summary>
        Sprite MenuSprite(string artId, float height, bool resident = false)
        {
            var set = Sprites.UseArt ? CharacterSet.Load(artId, height) : null;
            if (set != null) return set.Frames[0][6][0];
            return resident ? Sprites.Resident(0) : Sprites.Monster(artId);
        }

        void DrawSpriteFit(Sprite sp, Rect box)
        {
            if (sp == null) return;
            var tr = sp.textureRect;
            var tex = sp.texture;
            float aspect = tr.width / tr.height;
            float w = box.width, h = w / aspect;
            if (h > box.height) { h = box.height; w = h * aspect; }
            var rect = new Rect(box.x + (box.width - w) / 2f, box.yMax - h, w, h);
            var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
            GUI.DrawTextureWithTexCoords(rect, tex, uv);
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
            bool housed = (gm.Phase == Phase.Night && gm.HumanRole == Role.Monster) || (gm.Human != null && gm.Human.Room != null);
            string phase = gm.Phase == Phase.Setup ? (housed ? "SETUP: GET READY" : "SETUP: FIND A ROOM")
                : gm.Phase == Phase.Night ? "NIGHT " + gm.Night + (gm.Endless ? " · ENDLESS" : " / " + gm.Cfg.match.nightCount)
                : "DAWN";
            var r = new Rect(vw / 2 - 130, 8, 260, 42);
            Panel(r);
            Shadowed(new Rect(r.x, r.y + 2, r.width, 20), phase, Candle);
            if (gm.Phase != Phase.Results) Shadowed(new Rect(r.x, r.y + 20, r.width, 20), Clock(gm.PhaseTimer), Bone);

            var menu = new Rect(10, 10, 64, 30);
            Ui(menu);
            if (GUI.Button(menu, "Menu", centerButton)) { gm.ReturnToMenu(); return; }
            var walls = new Rect(206, 10, 140, 30);
            Ui(walls);
            if (GUI.Button(walls, "Walls: " + gm.WallMode, centerButton))
                gm.SetWallMode((WallDisplayMode)(((int)gm.WallMode + 1) % 3));
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
            float width = Mathf.Min(660,vw-600);
            var r = new Rect(vw / 2 - width/2, 56, width, width<500?76:46);
            GUI.Box(r, GUIContent.none, box);
            Ui(r);
            GUI.Label(new Rect(r.x + 10, r.y + 4, r.width - 20, r.height - 8), gm.Banner, center);
        }

        void DrawLog()
        {
            if (gm.Log.Count == 0) return;
            float h = 16 * gm.Log.Count + 10;
            var r = new Rect(10, 48, 300, h);
            GUI.Box(r, GUIContent.none, box);
            Ui(r);
            for (int i = 0; i < gm.Log.Count; i++)
                GUI.Label(new Rect(r.x + 8, r.y + 4 + i * 16, 290, 18), gm.Log[i], small);
        }

        void DrawToast()
        {
            if (string.IsNullOrEmpty(gm.ToastText) || Time.unscaledTime > gm.ToastUntil) return;
            var r = new Rect(vw / 2 - 300, VH - 64, 600, 36);
            GUI.Box(r, GUIContent.none, box);
            Ui(r);
            GUI.Label(r, gm.ToastText, center);
        }

        void DrawWorldLabels()
        {
            float now = gm.Now;
            var me = gm.Human;
            float tile = GuiPerTile;

            foreach (var def in gm.Map.Rooms)
            {
                gm.RoomsByDef.TryGetValue(def, out var room);
                if (room == null)
                {
                    if (gm.Phase == Phase.Setup && (gm.IsVisible(def.Center) || gm.IsTileVisible(def.DoorTile)))
                    {
                        var c = WorldToGui(def.Center);
                        string text = "Room " + (def.Index + 1) + "\n" + def.BuildBudget + " build spots" +
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

                // Legal empty squares show a spectral plus on the floor (GameManager.FloorEnergy);
                // the HUD only flashes empty squares when the room still has no weapon.
                if (mine && me.Alive && Time.unscaledTime < room.NoWeaponBlinkUntil && Mathf.FloorToInt(Time.unscaledTime * 8f) % 2 == 0)
                    for (int i = 0; i < room.Slots.Length; i++)
                    {
                        if (room.Slots[i] != null) continue;
                        var pg = WorldToGui(HotelMap.Center(def.BuildTiles[i]));
                        var old = GUI.color;
                        GUI.color = new Color(Red.r, Red.g, Red.b, 0.3f);
                        GUI.DrawTexture(new Rect(pg.x - tile / 2, pg.y - tile / 2, tile, tile), Sprites.White);
                        GUI.color = old;
                    }
            }

            // residents
            bool zoomedOut = gm.HotelView || !gm.InMatch;
            foreach (var r in gm.Residents)
            {
                if (!gm.IsVisible(r.Pos) && r != me) continue;
                if (zoomedOut && r != me && r.Alive) continue; // names would pile up in the overview
                var g = WorldToGui(r.Pos) + Vector2.up * -(r.Asleep ? 0.5f : 1.6f) * GuiPerTile;
                string who = r.IsHuman ? "YOU" : r.Name;
                if (!r.Alive) who += " (eaten)";
                Shadowed(new Rect(g.x - 60, g.y - 10, 120, 20), who + (r.Asleep ? " <color=#9FE3C8>z</color>" : ""), r.IsHuman ? Candle : Bone);
                if (r.Alive && r.Health < gm.Cfg.match.residentHealth)
                    Bar(new Rect(g.x - 18, g.y + 8, 36, 5), r.Health / gm.Cfg.match.residentHealth, Red);
            }

            var m = gm.Monster;
            if (m != null && !m.Dead && gm.IsVisible(m.Pos))
            {
                var g = WorldToGui(m.Pos) + Vector2.up * -2.2f * GuiPerTile;
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
            var origin = JoystickCenter;
            float opacity = active ? 0.8f : 0.35f;
            var old = GUI.color;
            GUI.color = new Color(Bone.r, Bone.g, Bone.b, opacity);
            GUI.DrawTexture(new Rect(origin.x - JoyRadius, origin.y - JoyRadius, JoyRadius * 2, JoyRadius * 2), ringTex);
            GUI.color = old;
            Circle(active ? joyKnob : origin, 28f, new Color(Candle.r, Candle.g, Candle.b, opacity));
            Shadowed(new Rect(origin.x - 85, origin.y + 66, 170, 20), gm.SleepingCamera ? "look around" : "move", new Color(Bone.r, Bone.g, Bone.b, 0.5f));
        }

        // ------------------------------------------------------------ resident

        void DrawResidentUI()
        {
            var me = gm.Human;
            if (me == null) return;
            float now = gm.Now;

            DrawResources(me, now);
            DrawMonsterIntel(new Rect(vw - 270, 92, 260, 70));

            if (!me.Alive)
            {
                var r = new Rect(vw / 2 - 220, VH - 120, 440, 50);
                GUI.Box(r, GUIContent.none, box);
            Ui(r);
                GUI.Label(r, "You haunt the hallway now. Drag to watch the others.", center);
                RoundButton(new Vector2(vw-90,VH-225),76,"Recenter",Bone,true,gm.RecenterCamera,roundSmall);
                return;
            }

            if (!me.Asleep) DrawJoystick();

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
            var ac = new Vector2(vw - 90f - (Screen.width - Screen.safeArea.xMax) / scale, VH - 95f - Screen.safeArea.yMin / scale);
            RoundButton(ac, 116f, text, col, action != ResidentAction.None, DoResidentAction);

            if (gm.Phase == Phase.Night)
            {
                float cooldown = gm.PersonalShotCooldown(me);
                string shootText = me.Asleep ? "Wake to\nshoot" : cooldown > 0f ? "Shoot\n" + cooldown.ToString("0.0") + "s" : "Shoot\n[F]";
                RoundButton(ac + new Vector2(0, -250), 84f, shootText, Candle,
                    gm.ResidentShotAvailability(me) == ActionResult.Ok, ShootResident, roundSmall);
            }

            if (gm.NearDoor(me) && !me.Asleep)
                RoundButton(new Vector2(vw - 190f, VH - 280f), 64f, me.Room.DoorOpen ? "Close\ndoor" : "Open\ndoor", Bone, !me.Room.DoorBroken,
                    () => gm.TryToggleDoor(me), roundSmall);
            if (me.Room != null)
                RoundButton(new Vector2(vw - 90f, VH - 225f), 76f, "Recenter", Bone, true, gm.RecenterCamera, roundSmall);
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
            if (m == null) { Panel(r); GUI.Label(r, "Monster: ???", center); return; }
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
            var safe = Screen.safeArea;
            var bounds = new Rect(safe.xMin / scale + 8, (Screen.height - safe.yMax) / scale + 8,
                safe.width / scale - 16, safe.height / scale - 16);
            var obstacles = new List<Rect>(repaint ? nextUiRects : hudRects);
            if (JoystickAllowed) obstacles.Add(new Rect(JoystickCenter.x-82,JoystickCenter.y-82,164,164));
            return HudLayout.Popup(WorldToGui(world), new Vector2(w, h), bounds, obstacles);
        }

        bool CloseButton(Rect panel)
        {
            var r = new Rect(panel.xMax - 34, panel.y + 6, 28, 26);
            return GUI.Button(r, "x", centerButton);
        }

        void DrawSelection(Resident me)
        {
            var room = me.Room;
            if (me.IsMonster && sel != Sel.None)
            {
                var r = PopupRect(me.Pos, 320, 90); Panel(r);
                GUI.Label(new Rect(r.x + 12, r.y + 32, r.width - 24, 50), "You are hiding. Wait for lights out.", center);
                if (CloseButton(r)) ClearSelection();
                return;
            }
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

        // ---- build dock: tabs by kind of building, tap a card to preview its range, BUILD to place it
        static readonly string[] TabIds = { "resources", "fire", "bullets", "electric", "effects" };
        static readonly string[] TabNames = { "Resources", "Fire", "Bullets", "Electric", "Effects" };
        static readonly Color[] TabColors =
        {
            new Color(0.95f, 0.76f, 0.31f), new Color(1f, 0.52f, 0.18f), new Color(0.78f, 0.80f, 0.86f),
            new Color(0.38f, 0.86f, 1f), new Color(0.72f, 0.48f, 0.98f)
        };
        int buildTab = 2;
        string previewId;

        static string CategoryOf(Config.TowerDef t)
        {
            if (!string.IsNullOrEmpty(t.category)) return t.category;
            switch (t.damageType) { case "bullet": return "bullets"; case "fire": return "fire"; case "electric": return "electric"; case "slow": return "effects"; }
            return t.effect == "clairvoyance" ? "effects" : "resources";
        }

        /// <summary>Draws a sprite with its pivot on a world point (used for the placement ghost).</summary>
        void DrawSpriteAt(Sprite sp, Vector2 world, Color tint)
        {
            if (sp == null) return;
            var g = WorldToGui(world);
            float s = GuiPerTile;
            var b = sp.bounds;
            var rect = new Rect(g.x + b.min.x * s, g.y - b.max.y * s, b.size.x * s, b.size.y * s);
            var tr = sp.textureRect;
            var tex = sp.texture;
            var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
            var old = GUI.color;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(rect, tex, uv);
            GUI.color = old;
        }

        void DrawBuildMenu(Resident me, Room room, int slot)
        {
            var tile = room.Def.BuildTiles[slot];
            var wpos = HotelMap.Center(tile);
            var towers = gm.Cfg.towers.towers;
            if (buildTab < 0 || buildTab >= TabIds.Length) buildTab = 0;
            string tabId = TabIds[buildTab];
            var inTab = new List<Config.TowerDef>();
            foreach (var t in towers) if (CategoryOf(t) == tabId) inTab.Add(t);
            Config.TowerDef sel2 = null;
            foreach (var t in inTab) if (t.id == previewId) sel2 = t;
            if (sel2 == null && inTab.Count > 0) { sel2 = inTab[0]; previewId = sel2.id; }

            bool placeable = gm.CanBuildAt(room, slot);
            var tabCol = TabColors[buildTab];

            // ---- preview in the world: tile marker, translucent range disc + ring, ghost building
            var tileCol = placeable ? new Color(0.62f, 0.89f, 0.78f) : Red;
            var oldc = GUI.color;
            float ts = GuiPerTile;
            var tg = WorldToGui(wpos);
            GUI.color = new Color(tileCol.r, tileCol.g, tileCol.b, 0.28f + 0.12f * Mathf.Sin(Time.unscaledTime * 6f));
            HighlightTile(tile);
            GUI.color = oldc;
            if (sel2 != null)
            {
                bool weapon = DamageTypes.Index(sel2.damageType) >= 0;
                if (weapon)
                {
                    float rng = gm.RangeOf(sel2, 1);
                    GUI.color = new Color(tabCol.r, tabCol.g, tabCol.b, 0.10f);
                    GUI.DrawTexture(new Rect(tg.x-rng*ts,tg.y-rng*ts*0.766f,rng*2*ts,rng*2*ts*0.766f),circleTex);
                    GUI.color=oldc;
                    DrawRing(wpos, rng, new Color(tabCol.r, tabCol.g, tabCol.b, 0.85f));
                    if(sel2.minimumRange>0)DrawRing(wpos,sel2.minimumRange,Red);
                }
                if (sel2.areaRadius > 0f) DrawRing(wpos, sel2.areaRadius, new Color(1f, 1f, 1f, 0.4f));
                DrawSpriteAt(Sprites.Tower(sel2, 1), wpos, new Color(1f, 1f, 1f, placeable ? 0.78f : 0.4f));
            }

            // ---- dock (moves to the top when the tile is down there)
            float w = Mathf.Min(780f, vw - 300f), h = 244f;
            var r = PopupRect(wpos, w, h);
            Panel(r);

            float tx = r.x + 10f, tabW = (w - 60f) / TabIds.Length;
            for (int i = 0; i < TabIds.Length; i++)
            {
                var tr = new Rect(tx + i * tabW, r.y + 8f, tabW - 4f, 30f);
                bool on = i == buildTab;
                var oc = GUI.color;
                GUI.color = on ? TabColors[i] : new Color(TabColors[i].r, TabColors[i].g, TabColors[i].b, 0.45f);
                if (GUI.Button(tr, "<b>" + TabNames[i] + "</b>", centerButton)) { buildTab = i; previewId = null; }
                GUI.color = oc;
                if (on) { GUI.color = TabColors[i]; GUI.DrawTexture(new Rect(tr.x, tr.yMax, tr.width, 3f), Sprites.White); GUI.color = oc; }
            }
            if (CloseButton(r)) { ClearSelection(); return; }

            // cards
            float cx = r.x + 10f, cy = r.y + 48f;
            float cardW = Mathf.Min(142f, (w - 320f - 8f * Mathf.Max(0, inTab.Count - 1)) / Mathf.Max(1, inTab.Count)), cardH = 152f;
            if (inTab.Count == 0) GUI.Label(new Rect(cx, cy + 40f, 400f, 40f), "Nothing here yet.", small);
            for (int i = 0; i < inTab.Count; i++)
            {
                var t = inTab[i];
                var cr = new Rect(cx + i * (cardW + 8f), cy, cardW, cardH);
                bool chosen = t == sel2;
                bool afford = gm.Wallet(me, t.costResource) >= t.buildCost;
                var oc = GUI.color;
                GUI.color = chosen ? new Color(tabCol.r, tabCol.g, tabCol.b, 1f) : (afford ? Color.white : new Color(1f, 1f, 1f, 0.55f));
                if (GUI.Button(cr, GUIContent.none, centerButton)) previewId = t.id;
                GUI.color = oc;
                var ico = Sprites.Tower(t, 1);
                GUI.DrawTexture(new Rect(cr.x + 10f, cr.y + 6f, cr.width - 20f, 78f), ico.texture, ScaleMode.ScaleToFit);
                GUI.Label(new Rect(cr.x + 4f, cr.y + 86f, cr.width - 8f, 40f), "<b>" + t.name + "</b>", center);
                GUI.Label(new Rect(cr.x + 4f, cr.y + 126f, cr.width - 8f, 22f), (afford ? "" : "<color=#D7263D>") + t.buildCost + "</color> " + ResShort(t.costResource), center);
            }

            // detail pane
            var dr = new Rect(r.xMax - 300f, r.y + 46f, 290f, h - 56f);
            if (sel2 != null)
            {
                GUI.Label(new Rect(dr.x, dr.y, dr.width, 22f), "<b>" + sel2.name + "</b>", label);
                GUI.Label(new Rect(dr.x, dr.y + 22f, dr.width, 56f), sel2.description ?? "", small);
                string stats;
                int type = DamageTypes.Index(sel2.damageType);
                if (type >= 0)
                {
                    float rng = gm.RangeOf(sel2, 1);
                    stats = RangeLabel(sel2.rangeClass) + " (" + (sel2.minimumRange>0?sel2.minimumRange.ToString("0.#")+"–":"") + rng.ToString("0.#") + " tiles)";
                    if (sel2.damageType == "slow") stats += "  ·  slows " + Mathf.RoundToInt(sel2.slowPct * 100f) + "%";
                    else stats += "  ·  " + Mathf.RoundToInt(UpgradeRules.Damage(gm.Cfg.towers,sel2,1) * UpgradeRules.FireRate(gm.Cfg.towers,sel2,1) + UpgradeRules.BurnDamage(gm.Cfg.towers,sel2,1)) + " dmg/s";
                    var m = gm.Monster;
                    if (m != null)
                    {
                        float mult = gm.DamageTaken(m, type);
                        if (mult > 1.05f) stats += "\n<color=#9FE3C8>The monster is weak to this</color>";
                        else if (mult < 0.95f) stats += "\n<color=#D7263D>The monster resists this</color>";
                    }
                }
                else if (sel2.dreamPerSecond > 0f) stats = "+" + sel2.dreamPerSecond.ToString("0.#") + " Dream Power/s, awake or asleep";
                else if (sel2.effect == "clairvoyance") stats = "See the whole hotel";
                else stats = "+" + sel2.faithPerSecond.ToString("0.#") + " Faith/s";
                GUI.Label(new Rect(dr.x, dr.y + 84f, dr.width, 40f), stats, small);

                bool afford = gm.Wallet(me, sel2.costResource) >= sel2.buildCost;
                string btn;
                if (!placeable) btn = "<color=#D7263D>Keep a path to your bed</color>";
                else if (!afford) btn = "<color=#D7263D>Need " + Mathf.CeilToInt(sel2.buildCost - gm.Wallet(me, sel2.costResource)) + " more " + ResName(sel2.costResource) + "</color>";
                else btn = "<b>BUILD</b>  " + sel2.buildCost + " " + ResShort(sel2.costResource);
                var br = new Rect(dr.x, dr.yMax - 44f, dr.width, 44f);
                var oc2 = GUI.color;
                GUI.color = (placeable && afford) ? tabCol : new Color(1f, 1f, 1f, 0.5f);
                if (GUI.Button(br, btn, centerButton))
                {
                    var res = gm.TryBuildTower(me, slot, sel2.id);
                    if (res == ActionResult.Blocked) gm.Toast("That would wall off your bed. Keep a path from the door.");
                    else Report(res, ResName(sel2.costResource));
                    if (res == ActionResult.Ok) ClearSelection();
                }
                GUI.color = oc2;
            }
        }

        void DrawTowerMenu(Resident me, Room room, TowerInstance t)
        {
            var wpos = HotelMap.Center(t.Tile);
            if (t.IsWeapon) DrawRing(wpos, gm.TowerRange(t), new Color(Candle.r, Candle.g, Candle.b, 0.55f));
            HighlightTile(t.Tile);

            var r = PopupRect(wpos, 330, 222);
            Panel(r);
            GUI.DrawTexture(new Rect(r.x + 10, r.y + 10, 32, 32), Sprites.Tower(t.Def, t.Level).texture, ScaleMode.ScaleToFit);
            int max = gm.TowerMaxLevel(t);
            GUI.Label(new Rect(r.x + 50, r.y + 8, r.width - 90, 22), "<b>" + UpgradeRules.TowerName(t.Def, t.Level) + "</b>  Lv " + t.Level + "/" + max, label);
            if (CloseButton(r)) { ClearSelection(); return; }

            string info;
            if (t.IsWeapon)
            {
                var sc = gm.Cfg.towers.levelScaling;
                int lv = t.Level - 1;
                float dmg = UpgradeRules.Damage(gm.Cfg.towers, t.Def, t.Level) * gm.OwnerDamageMult(room);
                float rate = UpgradeRules.FireRate(gm.Cfg.towers, t.Def, t.Level);
                info = RangeLabel(t.Def.rangeClass) + " (" + gm.TowerRange(t).ToString("0.#") + " tiles)  ·  " +
                       (t.Def.damageType == "slow" ? "slows " + Mathf.RoundToInt((UpgradeRules.Tier(t.Def, t.Level)?.slowPct ?? t.Def.slowPct) * 100) + "%" : Mathf.RoundToInt(dmg * rate) + " dmg/s");
            }
            else if (t.IsClairvoyance) info = "You can see the whole hotel. Use Hotel view to look around.";
            else info = t.IsDreamGen ? "+" + UpgradeRules.DreamRate(t.Def, t.Level).ToString("0.#") + " DP/s" :
                "+" + UpgradeRules.FaithRate(t.Def, t.Level).ToString("0.#") + " Faith/s";
            if (t.IsWeapon) info += "  ·  door support " + UpgradeRules.DoorSupportLevel(t.Def, t.Level);
            GUI.Label(new Rect(r.x + 50, r.y + 30, r.width - 60, 40), info, small);

            float cost = gm.TowerUpgradeCost(t);
            if (cost >= 0f)
            {
                GUI.DrawTexture(new Rect(r.x + 10, r.y + 74, 44, 44), Sprites.Tower(t.Def, t.Level + 1).texture, ScaleMode.ScaleToFit);
                GUI.Label(new Rect(r.x + 62, r.y + 74, r.width - 72, 44), "Next: " + UpgradeRules.TowerName(t.Def, t.Level + 1), small);
            }
            var ub = new Rect(r.x + 10, r.y + 124, r.width - 20, 38);
            if (cost < 0f) GUI.Label(ub, "Max level", center);
            else if (GUI.Button(ub, "<b>Upgrade</b>  " + Mathf.CeilToInt(cost) + " " + ResShort(t.Def.costResource), button))
                Report(gm.TryUpgradeTower(me, t.SlotIndex), ResName(t.Def.costResource));

            float refund = gm.TowerSellValue(t);
            if (GUI.Button(new Rect(r.x + 10, r.y + 172, r.width - 20, 38), "<b>Sell</b>  +" + Mathf.FloorToInt(refund) + " " + ResShort(t.Def.costResource) +
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
            HighlightTile(room.Def.BedHeadTile);
            var r = PopupRect(room.Def.BedCenter, 300, 124);
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
            Bar(new Rect(r.x + 10, r.y + 36, r.width - 20, 10), room.DoorBroken ? 0f : room.DoorHp / gm.MaxDoorHp(room), room.UnderAttack(gm.Now) ? Candle : TealC);
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
            Vector2[] corners={new Vector2(t.x,t.y),new Vector2(t.x+1,t.y),new Vector2(t.x+1,t.y+1),new Vector2(t.x,t.y+1)};
            var matrix=GUI.matrix;var color=GUI.color;
            GUI.color=new Color(Candle.r,Candle.g,Candle.b,0.7f);
            for(int i=0;i<4;i++)
            {
                var a=WorldToGui(corners[i]);var b=WorldToGui(corners[(i+1)%4]);
                GUIUtility.RotateAroundPivot(Mathf.Atan2(b.y-a.y,b.x-a.x)*Mathf.Rad2Deg,a);
                GUI.DrawTexture(new Rect(a.x,a.y,Vector2.Distance(a,b),2),Sprites.White);GUI.matrix=matrix;
            }
            GUI.color=color;
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
            var panel = new Rect(10, 150, w, 264);
            Panel(panel);
            float x = panel.x, y = panel.y;
            GUI.Label(new Rect(x + 10, y + 6, w - 20, 22), "<b>Lv " + m.Level + " " + (gm.Evolution(m)?.name ?? m.Def.name) + "</b>", label);
            Bar(new Rect(x + 10, y + 30, w - 20, 10), m.Dead ? 0f : m.Hp / gm.MaxHp(m), Red);
            GUI.Label(new Rect(x + 10, y + 44, w - 20, 22), "<color=#F2C14E>Dream Power</color> " + Mathf.FloorToInt(m.DreamPower) +
                "    <color=#D7263D>Faith</color> " + Mathf.FloorToInt(m.Faith), small);
            GUI.Label(new Rect(x + 10, y + 62, w - 20, 22), "Arm " + gm.PartCount(m, "arm") + "  Leg " + gm.PartCount(m, "leg") +
                "  Torso " + gm.PartCount(m, "torso") + "  Eye " + gm.PartCount(m, "eye") + "   (max " + gm.Cfg.bodyParts.maxPartsPerType + ")", small);

            GUI.Label(new Rect(x + 10, y + 80, w - 20, 20), m.LevelXp.ToString("0") + "/" + gm.NextMonsterLevelXp(m).ToString("0") + " XP" + (m.Frenzy ? " · FRENZY" : "") + " · kills " + m.Kills, small);
            float yy = y + 104;
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
            Ui(r);
                GUI.Label(r, "Banished! Back in " + Mathf.CeilToInt(Mathf.Max(0f, m.RespawnAt - gm.Now)) + " s", center);
            }

            DrawJoystick();

            // abilities as round buttons, right side
            bool night = gm.Phase == Phase.Night;
            for (int i = 0; i < m.Loadout.Length; i++)
            {
                var a = m.Loadout[i];
                float cd = m.Cooldowns[i];
                int idx = i;
                var c = new Vector2(vw - 75f - (i % 3) * 110f, VH - 75f - (i / 3) * 108f);
                string text = a.name + "\n<size=11>" + AbilityHint(a) + "</size>" + (cd > 0 ? "\n" + Mathf.CeilToInt(cd) + "s" : "\n[" + (i + 1) + "]");
                RoundButton(c, 100f, text, Mint, cd <= 0f && night && !m.Dead, () => gm.UseAbility(idx), roundSmall);
            }
            if (!string.IsNullOrEmpty(m.Branch))
                RoundButton(new Vector2(vw - 410f, VH - 80f), 94f, gm.Evolution(m).ability, Candle, gm.Now >= m.EvolutionUntil, gm.UseEvolution, roundSmall);
            if (gm.HotelViewAvailable)
                RoundButton(new Vector2(vw - 80f, VH - 310f), 72f, gm.HotelView ? "Back" : "Hotel\nview", Bone, true, () => gm.HotelView = !gm.HotelView, roundSmall);
        }

        void DrawProgressChoice()
        {
            var m = gm.Monster;
            if (m == null || !m.IsHuman || m.Choices.Count == 0) return;
            var choice = m.Choices.Peek();
            float width = Mathf.Min(660,vw-40);
            var r = new Rect((vw-width)/2, 220, width, 165); Panel(r);
            GUI.Label(new Rect(r.x+10,r.y+10,r.width-20,35), choice.Kind + " · level " + choice.Level, subtitle);
            float w = (width-30)/choice.Options.Length;
            for (int i=0;i<choice.Options.Length;i++)
                if (GUI.Button(new Rect(r.x+15+i*w,r.y+60,w-8,85),gm.ChoiceLabel(choice,choice.Options[i]),centerButton))
                { gm.ChooseProgression(i); break; }
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
            Ui(r);
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

            GUI.Label(new Rect(r.x+30,y,r.width-60,24), (gm.Endless ? "Endless" : "Standard") + " · night " + gm.Night + " · monster level " + gm.Monster.Level +
                (gm.Endless ? " · personal best " + GameManager.PersonalBest(res.Role) : ""), small);
            y += 26;
            foreach (var rr in gm.Residents)
            {
                string status = rr.Alive ? "<color=#9FE3C8>alive</color>" : "<color=#D7263D>eaten</color>";
                GUI.Label(new Rect(r.x + 30, y, r.width - 60, 22), rr.Name + "  -  " + status + ",  nights " + rr.NightsSurvived, small);
                y += 22;
            }

            if (GUI.Button(new Rect(r.x + 60, r.yMax - 60, 220, 44), "Play again", bigButton))
                { if (gm.Endless) gm.StartEndless(res.Role, monsterPick); else gm.StartMatch(res.Role, monsterPick); }
            if (GUI.Button(new Rect(r.xMax - 280, r.yMax - 60, 220, 44), "Main menu", bigButton))
                gm.ReturnToMenu();
        }
    }
}
