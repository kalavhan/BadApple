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
    /// The build, tower, bed and door windows live in GameHUD.Summon.cs and GameHUD.Ring.cs and use DreamSkin and UiFx.
    /// Mouse and keyboard work the same way in the editor.
    /// </summary>
    public partial class GameHUD : MonoBehaviour
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
            /// <summary>The touch zone this finger came down on: it gets the drags and the release.</summary>
            public Zone? Zone;
        }

        /// <summary>
        /// A touch target drawn this frame. Press fires on finger down (round HUD buttons, holds), Tap on a release
        /// that stays inside, Drag on every move once the finger has travelled, Release on lift either way.
        /// </summary>
        struct Zone
        {
            public Rect R;
            public bool Round;
            public System.Action Press, Tap;
            public System.Action<Vector2> Drag;
            public System.Action<Vector2, bool> Release;
            public bool Hit(Vector2 g) => Round ? (g - R.center).sqrMagnitude <= R.width * R.width * .25f : R.Contains(g);
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
        float windowOpenedAt = -9f, windowClosingAt = -1f;
        readonly UiFx fxFront = new UiFx();

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

        void AddZone(Zone z)
        {
            if (repaint) nextZones.Add(z);
            Ui(z.R);
        }

        /// <summary>A button that fires when a finger lifts inside it (window buttons, so a brushed thumb never buys anything).</summary>
        void TapZone(Rect r, System.Action tap, bool round = false) => AddZone(new Zone { R = r, Tap = tap, Round = round });

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
        static string ResShort(string res) => DreamSkin.Res(res);

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
                gm.UiFocus = null;
                return;
            }

            gm.WallFocusRoom = sel != Sel.None ? gm.Human?.Room?.Def : null;
            if (windowClosingAt >= 0f && Time.unscaledTime - windowClosingAt > .18f) ClearSelection();
            UpdateBanish();
            UpdateCameraFocus();
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
            if (GameInput.ConsumePressed(KeyCode.Escape)) { CloseWindow(); ToggleMonsterRing(false); ToggleHorde(false); }
            if (gm.HumanRole == Role.Monster && gm.Monster != null)
            {
                if (GameInput.ConsumePressed(KeyCode.L)) ToggleMonsterRing(!monsterRing);
                if (GameInput.ConsumePressed(KeyCode.H)) ToggleHorde(!hordeOpen);
            }
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
                    // The last zone drawn is on top.
                    for (int i = zones.Count - 1; i >= 0; i--)
                    {
                        var z = zones[i];
                        if (!z.Hit(g)) continue;
                        p.Ignore = true; p.Zone = z;
                        z.Press?.Invoke();
                        return;
                    }
                    if (popupRects.Any(r => r.Contains(g))) { p.Ignore = true; return; }
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
                    if (p.Zone.HasValue)
                    {
                        if (p.Moved && phase == TouchPhase.Moved) p.Zone.Value.Drag?.Invoke(g);
                        break;
                    }
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
                        if (p.Zone.HasValue)
                        {
                            var z = p.Zone.Value;
                            bool ended = phase == TouchPhase.Ended;
                            z.Release?.Invoke(g, p.Moved || !ended);
                            if (ended && z.Tap != null && z.Hit(g) && !(p.Moved && z.Drag != null)) z.Tap();
                        }
                        else if (!p.Ignore && !p.Moved && phase == TouchPhase.Ended && Time.unscaledTime - p.StartTime < 0.4f)
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

        /// <summary>
        /// Keeps what an open window is about in view: the plate above the summon tray, or the tower high enough
        /// for its ring of orbs. The camera eases there and back (GameManager.UiFocus).
        /// </summary>
        void UpdateCameraFocus()
        {
            var room = gm.Human?.Room;
            gm.UiFocus = null;
            if (room == null || sel != Sel.Slot || windowClosingAt >= 0f || selSlot < 0 || selSlot >= room.Slots.Length) return;
            gm.UiFocus = HotelMap.Center(room.Def.BuildTiles[selSlot]);
            var tower = room.Slots[selSlot];
            if (tower == null)
            {
                gm.UiFocusMinY = (VH - TrayRect().y + 70f) * scale;
                gm.UiFocusMaxY = (VH - 110f) * scale;
            }
            else
            {
                // The orbs need room below the tower; the ghost of its next form floats a tower's height above it,
                // under the top bar.
                gm.UiFocusMinY = 205f * scale;
                gm.UiFocusMaxY = Mathf.Max(gm.UiFocusMinY, (VH - 2f * TowerHeight(tower) - 90f) * scale);
            }
        }

        void ReleaseJoystick()
        {
            joyId = NoPointer;
            GameInput.Joystick = Vector2.zero;
        }

        void WorldTap(Vector2 g)
        {
            if (gm.Cam == null) return;
            if (gm.HumanRole == Role.Monster && gm.Monster != null) { MonsterWorldTap(g); return; }
            if (gm.Human == null) return;
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
            // A tap on a tower's body counts too, not only on the square it stands on.
            int body = TowerAt(room, g);
            if (body >= 0) { TowerTapped(me, room, body); return; }
            int slot = room.Def.BuildIndex(tile);
            if (slot >= 0)
            {
                if (room.Slots[slot] != null) TowerTapped(me, room, slot);
                else OpenWindow(Sel.Slot, slot);
                return;
            }
            if (room.Def.IsBedTile(tile)) { OpenWindow(Sel.Bed, -1); return; }
            if (tile == room.Def.DoorTile) { OpenWindow(Sel.Door, -1); return; }
            if (tile == room.Def.DoorInside) gm.Toast("The doorway square stays clear.");
            if (sel != Sel.None) CloseWindow();
        }

        /// <summary>Opens (or switches to) a window; the open animation only replays when the kind of window changes.</summary>
        void OpenWindow(Sel what, int slot)
        {
            var room = gm.Human?.Room;
            bool wasTray = sel == Sel.Slot && room != null && selSlot >= 0 && selSlot < room.Slots.Length && room.Slots[selSlot] == null;
            bool isTray = what == Sel.Slot && room != null && slot >= 0 && slot < room.Slots.Length && room.Slots[slot] == null;
            bool same = sel == what && windowClosingAt < 0f && (what != Sel.Slot || wasTray == isTray && (isTray || selSlot == slot));
            if (!same) windowOpenedAt = Time.unscaledTime;
            if (what != Sel.Slot || selSlot != slot) { pending = null; banishHoldStart = -1f; }
            sel = what; selSlot = slot; windowClosingAt = -1f;
            if (isTray && !same && gm.Cam != null)
            {
                var g = WorldToGui(HotelMap.Center(room.Def.BuildTiles[slot]));
                fxFront.Burst(g, 10, DreamSkin.Mint, 90f, 7f, .7f, .9f);
            }
        }

        /// <summary>Slides the open window away, then clears the selection.</summary>
        void CloseWindow()
        {
            if (sel == Sel.None || windowClosingAt >= 0f) return;
            windowClosingAt = Time.unscaledTime;
            pending = null;
            banishHoldStart = -1f;
            cardDragging = false;
        }

        /// <summary>0 to 1 as the window opens (ease-out), back to 0 while it closes.</summary>
        float WindowEase()
        {
            float now = Time.unscaledTime;
            float open = Mathf.Clamp01((now - windowOpenedAt) / .26f);
            open = 1f - Mathf.Pow(1f - open, 3f);
            if (windowClosingAt >= 0f) open *= 1f - Mathf.Clamp01((now - windowClosingAt) / .18f);
            return open;
        }

        void ClearSelection()
        {
            sel = Sel.None;
            selSlot = -1;
            windowClosingAt = -1f;
            pending = null;
            banishHoldStart = -1f;
            cardDragging = false;
            ringTower = null;
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
            DreamSkin.Ensure();
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
                if (gm.HumanRole == Role.Resident && gm.Phase != Phase.Results) DrawReadyMarks(gm.Human);
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
                    if (repaint) popupRects = nextUiRects.Skip(popupStart).ToList();
                }
                fxFront.Draw(scale);
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
            // Windows cover the bottom of the screen, so toasts move under the banner while one is open.
            var r = new Rect(vw / 2 - 300, sel != Sel.None ? 108 : VH - 64, 600, 36);
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
                // The monster player reads its own health in the corner chip, and its windows open over its head.
                if (!m.IsHuman)
                {
                    Bar(new Rect(g.x - 30, g.y, 60, 7), m.Hp / gm.MaxHp(m), Red);
                    Shadowed(new Rect(g.x - 80, g.y - 18, 160, 18), m.Def.name, Bone);
                }
                if (m.EatingPart != null)
                    Bar(new Rect(g.x - 20, g.y + 10, 40, 5), m.EatProgress / gm.Cfg.bodyParts.eatSeconds, (Color)Palette.Moss);
            }

            foreach (var n in gm.Minions)
            {
                if (n.Dead || n.Hp >= n.MaxHp || !gm.IsVisible(n.Pos)) continue;
                var g = WorldToGui(n.Pos) + Vector2.up * -1.0f * GuiPerTile;
                Bar(new Rect(g.x - 14, g.y, 28, 4), n.Hp / n.MaxHp, Red);
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
            DrawMonsterIntel(new Rect(vw - 270, 92, 260, gm.Monster != null && gm.HordeAwake(gm.Monster) ? 86 : 70));

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

            // An open window takes the space of the secondary buttons and the hints.
            if (sel != Sel.None) return;

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
            DreamSkin.Icon(new Rect(r.x + 10, r.y + 8, 18, 18), "dream", Color.white);
            GUI.Label(new Rect(r.x + 32, r.y + 6, r.width - 40, 22),
                "<color=" + DreamSkin.MintHex + "><b>Dream Power</b></color>  " + Mathf.FloorToInt(me.DreamPower) + "  <size=12>+" + (dps + (me.Room != null ? gm.DreamGenPerSecond(me.Room) : 0f)).ToString("0.#") + "/s</size>" + sleepTag, label);
            DreamSkin.Icon(new Rect(r.x + 10, r.y + 33, 18, 18), "faith", Color.white);
            bool blackout = now < me.FaithBlockedUntil;
            float fps = me.Room != null ? gm.FaithPerSecond(me.Room) : 0f;
            GUI.Label(new Rect(r.x + 32, r.y + 31, r.width - 40, 22), "<color=" + DreamSkin.VioletHex + "><b>Faith</b></color>  " + Mathf.FloorToInt(me.Faith) +
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
            // Each multiplier stays hidden until one of your hits of that type has landed.
            string line = "";
            for (int i = 0; i < 4; i++)
            {
                if (!gm.KnownDamageTypes[i]) { line += "<color=#8A8070>" + DamageTypes.Label(i) + " ?</color>  "; continue; }
                float mult = gm.DamageTaken(m, i);
                string col = mult > 1.05f ? "#9FE3C8" : mult < 0.95f ? "#D7263D" : "#E8DCC0";
                line += "<color=" + col + ">" + DamageTypes.Label(i) + " x" + mult.ToString("0.0#") + "</color>  ";
            }
            GUI.Label(new Rect(r.x + 8, r.y + 22, r.width - 16, 20), line, small);
            GUI.Label(new Rect(r.x + 8, r.y + 42, r.width - 16, 20), "Ate: arm " + gm.PartCount(m, "arm") + "  leg " + gm.PartCount(m, "leg") +
                "  torso " + gm.PartCount(m, "torso") + "  eye " + gm.PartCount(m, "eye"), small);
            if (!gm.HordeAwake(m)) return;
            // Minion multipliers stay hidden too, until one of your towers lands that type on that creature.
            var creature = gm.Creature(m, m.ActiveMinion);
            string minions = creature.name + ": ";
            for (int i = 0; i < 3; i++)
            {
                if (!gm.KnownMinion(m.ActiveMinion, i)) { minions += "<color=#8A8070>" + DamageTypes.Label(i) + " ?</color>  "; continue; }
                int resist = gm.ResistOf(creature);
                float mult = i == resist ? 1f - gm.Cfg.minions.resistPct : i == GameManager.Weakness(resist) ? 1f + gm.Cfg.minions.weaknessBonus : 1f;
                string col = mult > 1.05f ? "#9FE3C8" : mult < 0.95f ? "#D7263D" : "#E8DCC0";
                minions += "<color=" + col + ">" + DamageTypes.Label(i) + " x" + mult.ToString("0.0#") + "</color>  ";
            }
            GUI.Label(new Rect(r.x + 8, r.y + 60, r.width - 16, 20), minions, small);
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

        /// <summary>The corner X of a window: a round glass button with the cross icon.</summary>
        void CloseButton(Rect r, System.Action close)
        {
            var c = r.center;
            DreamSkin.Fill(r, new Color(1, 1, 1, .06f), r.width / 2f);
            DreamSkin.Ring(c, r.width, 1.2f, new Color(DreamSkin.Bone.r, DreamSkin.Bone.g, DreamSkin.Bone.b, .25f));
            DreamSkin.Icon(new Rect(c.x - 10f, c.y - 10f, 20f, 20f), "close", DreamSkin.Bone);
            TapZone(new Rect(r.x - 4f, r.y - 4f, r.width + 8f, r.height + 8f), close, round: true);
        }

        void DrawSelection(Resident me)
        {
            var room = me.Room;
            if (me.IsMonster && sel != Sel.None)
            {
                var r = PopupRect(me.Pos, 320, 90);
                DreamSkin.Panel(r);
                Ui(r);
                GUI.Label(new Rect(r.x + 12, r.y + 32, r.width - 24, 50), "You are hiding. Wait for lights out.", center);
                CloseButton(new Rect(r.xMax - 50, r.y + 8, 40, 40), ClearSelection);
                return;
            }
            switch (sel)
            {
                case Sel.Slot:
                    if (selSlot < 0 || selSlot >= room.Slots.Length) { ClearSelection(); return; }
                    if (room.Slots[selSlot] == null) DrawSummonTray(me, room, selSlot);
                    else DrawTowerRing(me, room, room.Slots[selSlot]);
                    break;
                case Sel.Bed: DrawBedCard(me, room); break;
                case Sel.Door: DrawDoorCard(me, room); break;
            }
        }

        /// <summary>Draws a sprite with its pivot on a world point (the summon ghost), nudged by a GUI offset.</summary>
        void DrawSpriteAt(Sprite sp, Vector2 world, Color tint, Vector2 offset = default)
        {
            if (sp == null) return;
            var g = WorldToGui(world) + offset;
            float s = GuiPerTile;
            var b = sp.bounds;
            var rect = new Rect(g.x + b.min.x * s, g.y - b.max.y * s, b.size.x * s, b.size.y * s);
            var tr = sp.textureRect;
            var tex = sp.texture;
            var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
            var old = GUI.color;
            GUI.color = tint * new Color(1, 1, 1, old.a);
            GUI.DrawTextureWithTexCoords(rect, tex, uv);
            GUI.color = old;
        }

        /// <summary>Draws a sprite standing on a GUI point, scaled to a height in GUI pixels.</summary>
        void DrawSpriteGui(Sprite sp, Vector2 feet, float height, Color tint)
        {
            if (sp == null) return;
            var tr = sp.textureRect;
            float w = height * tr.width / tr.height;
            var old = GUI.color;
            GUI.color = tint * new Color(1, 1, 1, old.a);
            DrawSpriteFit(sp, new Rect(feet.x - w / 2f, feet.y - height, w, height));
            GUI.color = old;
        }

        void HighlightTile(Vector2Int t) => HighlightTile(t, Candle);

        void HighlightTile(Vector2Int t, Color c)
        {
            Vector2[] corners={new Vector2(t.x,t.y),new Vector2(t.x+1,t.y),new Vector2(t.x+1,t.y+1),new Vector2(t.x,t.y+1)};
            var matrix=GUI.matrix;var color=GUI.color;
            GUI.color=new Color(c.r,c.g,c.b,0.7f*color.a);
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
                case "residentSlowZone": return "Slow zone";
                case "flambe": return "Fire ring";
                case "sporeBloom": return "Spore cloud";
                case "lastCall": return "Silence towers";
                case "meatHook": return "Hook & eat";
                case "graveroot": return "Rift shrine";
                case "doNotDisturb": return "Phase in";
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
