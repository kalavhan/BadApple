using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// Screens for the hallway floors. The front desk: the clerk calls guests up one at a time and each takes a key
    /// from the rack (room numbers only, no plans). The lift: the monster player's first-person car, with the floor dial above the gate, brass floor
    /// buttons on the right and the selected floor's register on the left, where Fear grows that floor's waves.
    /// Both are built from the haunted-hotel kit (wood, brass, paper, the lift gate) like the summon windows.
    /// </summary>
    public partial class GameHUD
    {
        readonly Dictionary<RoomDef, Texture2D> floorThumbs = new Dictionary<RoomDef, Texture2D>();
        HotelMap thumbsFor;
        RoomDef liftPick;
        float liftOpenedAt = -9f, liftRideStartAt = -9f;
        bool liftWasOpen;
        int liftFromFloor = 1;

        static readonly Color ThumbWall = new Color32(0x1A, 0x0C, 0x16, 255);
        static readonly Color ThumbPad = new Color32(0x5A, 0x3A, 0x26, 255);
        static readonly Color ThumbCarpet = new Color32(0x9A, 0x22, 0x32, 255);
        static readonly Color ThumbRoom = new Color32(0xD9, 0xC8, 0xA4, 255);
        static readonly Color ThumbLift = new Color32(0xF0, 0xDC, 0x98, 255);
        static readonly Color ThumbBoards = new Color32(0x3A, 0x26, 0x1C, 255);

        /// <summary>A pixel-per-square plan of one floor: carpet in burgundy, pads in wood, the room in ivory, the lift in brass.</summary>
        Texture2D FloorThumb(RoomDef def)
        {
            if (thumbsFor != gm.Map)
            {
                foreach (var t in floorThumbs.Values) if (t != null) Destroy(t);
                floorThumbs.Clear();
                thumbsFor = gm.Map;
            }
            if (floorThumbs.TryGetValue(def, out var tex) && tex != null) return tex;
            var map = gm.Map;
            var b = def.ViewBounds;
            int size = Mathf.Max(b.width, b.height);
            tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Floor plan " + def.FloorNumber };
            var px = new Color[size * size];
            int ox = b.xMin - (size - b.width) / 2, oy = b.yMin - (size - b.height) / 2;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var t = new Vector2Int(ox + x, oy + y);
                    var tile = map.Get(t.x, t.y);
                    Color c = Color.clear;
                    if (map.FloorOf(t) != def) { px[y * size + x] = c; continue; }
                    if (t == def.LiftTile || t == def.LiftLanding) c = ThumbLift;
                    else if (t == def.DoorTile) c = ThumbLift;
                    else if (map.IsCarpet(t)) c = ThumbCarpet;
                    else if (map.PadOwner(t) == def) c = ThumbPad;
                    else if (tile == Tile.RoomFloor) c = ThumbRoom;
                    else if (tile == Tile.Corridor) c = ThumbBoards;
                    else if (tile == Tile.Wall) c = ThumbWall;
                    px[y * size + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply(false);
            floorThumbs[def] = tex;
            return tex;
        }

        // ------------------------------------------------------------ front desk

        static readonly Color ClerkInk = new Color32(0x0C, 0x05, 0x0A, 255);
        static readonly Color ClerkCoat = new Color32(0x2E, 0x16, 0x28, 255);

        /// <summary>
        /// The lobby: a key rack on the wall, the clerk behind the counter asking in a speech bubble, the bell and the
        /// register on the counter. Guests come up one at a time (15 s each); the player taps a key on the rack to
        /// take that room. The keys show only room numbers: nobody sees a floor's layout before going up.
        /// </summary>
        void DrawFrontDesk()
        {
            HauntedSkin.Ensure();
            float now = HauntedClock.Now;
            var all = new Rect(0, 0, vw, VH);
            Ui(all);
            var guest = gm.CheckingIn;

            // Lobby wall and lamplight.
            HauntedSkin.Fill(all, HauntedSkin.Plum);
            HauntedSkin.Tiled(all, HauntedSkin.Texture("damask"), new Vector2(180f, 180f), Vector2.zero);
            HauntedSkin.Fill(all, new Color(.04f, .015f, .04f, .5f));
            foreach (float x in new[] { vw * .1f, vw * .9f })
            {
                HauntedSkin.GlowAt(new Vector2(x, 150f), new Vector2(420f, 420f), new Color(1f, .78f, .42f, .22f + .03f * Mathf.Sin(now * 7f + x)));
                HauntedSkin.GlowAt(new Vector2(x, 150f), new Vector2(60f, 60f), new Color(1f, .9f, .7f, .8f));
            }

            // The key rack.
            float rackW = Mathf.Min(vw * .5f, 620f), rackH = 330f;
            var rack = new Rect(Mathf.Max(vw * .62f - rackW * .5f, 400f), 70f, rackW, rackH);
            HauntedSkin.Plaque(new Rect(rack.center.x - 130f, rack.y - 52f, 260f, 44f), "Reception", "keys · one per guest", -1.5f);
            HauntedSkin.WoodFrame(rack);
            var floors = gm.Map.Rooms;
            int columns = 3, rows = Mathf.CeilToInt(floors.Count / (float)columns);
            float cw = (rack.width - 36f) / columns, ch = (rack.height - 36f) / rows;
            var keyHooks = new Dictionary<RoomDef, Vector2>();
            for (int i = 0; i < floors.Count; i++)
            {
                var def = floors[i];
                int row = rows - 1 - i / columns, col = i % columns;   // top floors on the top row, like the building
                var cubby = new Rect(rack.x + 18f + col * cw + 3f, rack.y + 18f + row * ch + 3f, cw - 6f, ch - 6f);
                HauntedSkin.NineSlice(cubby, HauntedSkin.Texture("cubby_9"), new RectOffset(14, 14, 14, 14), Color.white);
                var plate = new Rect(cubby.center.x - 26f, cubby.y + 6f, 52f, 20f);
                HauntedSkin.Fill(plate, HauntedSkin.Brass, 3f);
                HauntedSkin.BrassRim(plate, 1f);
                HauntedSkin.Label(plate, def.RoomNumber, HauntedSkin.Typed(13), HauntedSkin.Ink);
                var hook = new Vector2(cubby.center.x, plate.yMax + 10f);
                HauntedSkin.Fill(new Rect(hook.x - 3f, hook.y - 4f, 6f, 6f), HauntedSkin.BrassMid, 3f);
                keyHooks[def] = hook;
                gm.RoomsByDef.TryGetValue(def, out var room);
                bool flying = def == gm.LastKeyTaken && Time.unscaledTime - gm.LastKeyTakenAt < .8f;
                if (room == null)
                {
                    bool mine = gm.HumanAtDesk;
                    float swing = 6f * Mathf.Sin(now * 1.7f + i * 1.3f) * (mine ? 1f : .4f);
                    DrawKey(hook, def.RoomNumber, swing, mine);
                    if (mine) TapZone(cubby, () => TakeKey(def));
                }
                else if (!flying)
                {
                    string who = room.Owner == null ? "" : room.Owner.IsHuman ? "yours" : room.Owner.Char != null ? room.Owner.Char.name : room.Owner.Name;
                    HauntedSkin.Label(new Rect(cubby.x + 4f, hook.y + 14f, cubby.width - 8f, 18f), who, HauntedSkin.Serif(15, true),
                        room.Owner != null && room.Owner.IsHuman ? HauntedSkin.Mint : new Color(.75f, .7f, .62f, 1f));
                }
            }

            // The clerk, behind the counter in front of the rack.
            var counterTop = VH - 196f;
            var clerk = new Vector2(rack.x - 130f, counterTop);
            DrawClerk(clerk, now);

            // The counter: the register, the bell, and the queue of guests.
            var counter = new Rect(0, counterTop, vw, VH - counterTop);
            HauntedSkin.Tiled(counter, HauntedSkin.Texture("wood_h"), new Vector2(128f, 128f), Vector2.zero);
            HauntedSkin.Fill(new Rect(0, counterTop, vw, 2f), HauntedSkin.BrassHi);
            HauntedSkin.Fill(new Rect(0, counterTop + 2f, vw, 3f), HauntedSkin.Brass);
            HauntedSkin.Fill(new Rect(0, counterTop + 5f, vw, 1f), HauntedSkin.BrassLow);
            HauntedSkin.Fill(new Rect(0, counterTop + 46f, vw, VH - counterTop - 46f), new Color(0, 0, 0, .35f));
            var register = new Rect(vw * .5f - 170f, counterTop - 26f, 340f, 64f);
            HauntedSkin.Rotated(-2f, register.center, () =>
            {
                HauntedSkin.RegisterCard(register);
                HauntedSkin.Label(new Rect(register.x + 16f, register.y + 6f, register.width - 32f, 22f), "Guest register", HauntedSkin.SmallCaps(13), HauntedSkin.BrassLow, TextAnchor.UpperLeft);
                if (guest != null)
                    HauntedSkin.Label(new Rect(register.x + 16f, register.y + 26f, register.width - 32f, 30f),
                        (guest.Char != null ? guest.Char.name : guest.Name) + "  ·  " + Mathf.CeilToInt(Mathf.Max(0f, gm.PhaseTimer)) + " s",
                        HauntedSkin.Typed(18), HauntedSkin.Ink, TextAnchor.UpperLeft);
            });
            var bell = new Rect(register.xMax + 30f, counterTop - 48f, 60f, 56f);
            HauntedSkin.Tex(bell, HauntedSkin.Texture("bell"), Color.white);
            float queueX = 30f;
            HauntedSkin.Label(new Rect(queueX, counterTop + 54f, 200f, 20f), "In line", HauntedSkin.SmallCaps(13), HauntedSkin.Brass, TextAnchor.UpperLeft);
            float qx = queueX, qy = counterTop + 78f;
            foreach (var r in gm.CheckInOrder)
            {
                bool current = r == guest, done = r.Room != null;
                string label = (r.IsHuman ? "You" : r.Char != null ? r.Char.name : r.Name) + (done ? " · " + r.Room.Def.RoomNumber : "");
                var style = HauntedSkin.Typed(13);
                float w = HauntedSkin.Measure(label, style).x + 20f;
                if (qx + w > vw - 30f) { qx = queueX; qy += 30f; }
                var chip = new Rect(qx, qy, w, 24f);
                HauntedSkin.Fill(chip, current ? HauntedSkin.Brass : done ? HauntedSkin.WoodTop : HauntedSkin.Plum, 4f);
                if (current) HauntedSkin.GlowAt(chip.center, chip.size * 1.7f, new Color(1f, .82f, .48f, .25f + .15f * Mathf.Sin(now * 4f)));
                HauntedSkin.Label(chip, label, style, current ? HauntedSkin.Ink : done ? new Color(.75f, .7f, .62f, 1f) : HauntedSkin.Bone);
                qx += w + 8f;
            }

            // A key that was just taken flies from its hook down to the counter.
            if (gm.LastKeyTaken != null && keyHooks.TryGetValue(gm.LastKeyTaken, out var from))
            {
                float k = (Time.unscaledTime - gm.LastKeyTakenAt) / .8f;
                if (k >= 0f && k < 1f)
                {
                    var to = new Vector2(register.center.x, counterTop - 30f);
                    var p = Vector2.Lerp(from, to, k * k * (3f - 2f * k)) + Vector2.up * -60f * Mathf.Sin(k * Mathf.PI);
                    DrawKey(p, gm.LastKeyTaken.RoomNumber, 40f * k, true);
                }
            }

            // What the clerk says.
            string say;
            bool justTaken = gm.LastKeyTakenBy != null && !gm.LastKeyTakenBy.IsHuman && Time.unscaledTime - gm.LastKeyTakenAt < 1.6f;
            if (justTaken)
                say = "Room " + gm.LastKeyTaken.RoomNumber + ". Sleep tight, " + (gm.LastKeyTakenBy.Char != null ? gm.LastKeyTakenBy.Char.name : gm.LastKeyTakenBy.Name) + ".";
            else if (guest == null) say = "That's everyone. Mind the lift.";
            else if (guest.IsHuman)
                say = "Good evening, " + (guest.Char != null ? guest.Char.name : "dear guest") + ". Which key will it be? Take one, quickly now.";
            else say = "Ah, " + (guest.Char != null ? guest.Char.name : guest.Name) + ". Take your time... not too much.";
            DrawSpeechBubble(new Vector2(clerk.x - 40f, clerk.y - 286f), say, now);
        }

        void TakeKey(RoomDef def)
        {
            if (!gm.PickFloor(gm.Human, def)) return;
            fxFront.Say(new Vector2(vw * .5f, VH * .45f), "CHECKED IN", "ROOM " + def.RoomNumber);
        }

        /// <summary>A brass room key with a paper tag, hanging from its hook (pivot) and swinging by degrees.</summary>
        void DrawKey(Vector2 hook, string number, float degrees, bool lit)
        {
            if (!repaint) return;
            HauntedSkin.Rotated(degrees, hook, () =>
            {
                var brass = lit ? HauntedSkin.BrassHi : HauntedSkin.Brass;
                if (lit) HauntedSkin.GlowAt(hook + Vector2.up * 30f, new Vector2(70f, 90f), new Color(1f, .85f, .5f, .22f));
                DreamSkin.Ring(hook + Vector2.up * 10f, 18f, 4f, brass);
                HauntedSkin.Fill(new Rect(hook.x - 2f, hook.y + 18f, 4f, 30f), brass, 1f);
                HauntedSkin.Fill(new Rect(hook.x + 2f, hook.y + 38f, 7f, 3f), brass, 1f);
                HauntedSkin.Fill(new Rect(hook.x + 2f, hook.y + 44f, 5f, 3f), brass, 1f);
                var tag = new Rect(hook.x - 34f, hook.y + 6f, 26f, 40f);
                HauntedSkin.Fill(tag, HauntedSkin.TagPaper, 3f);
                HauntedSkin.Border(tag, HauntedSkin.TagPaperDark, 1f, 3f);
                HauntedSkin.Rotated(-90f, tag.center, () => HauntedSkin.Label(new Rect(tag.center.x - 20f, tag.center.y - 10f, 40f, 20f), number, HauntedSkin.Typed(12), HauntedSkin.Ink));
            });
        }

        /// <summary>The clerk: a tall silhouette in a tailcoat, bow tie and two lamp-yellow eyes that blink.</summary>
        void DrawClerk(Vector2 feet, float now)
        {
            if (!repaint) return;
            float bob = Mathf.Sin(now * 1.3f) * 2f;
            var shoulders = new Rect(feet.x - 92f, feet.y - 200f + bob, 184f, 220f);
            HauntedSkin.GlowAt(new Vector2(feet.x, feet.y - 110f), new Vector2(320f, 360f), new Color(0, 0, 0, .7f));
            DreamSkin.Fill(shoulders, ClerkCoat, 70f);
            DreamSkin.Border(shoulders, new Color(.66f, .55f, 1f, .35f), 2f, 70f);                                   // violet rim light
            DreamSkin.Fill(new Rect(feet.x - 14f, feet.y - 196f + bob, 28f, 90f), HauntedSkin.Ivory, 4f);              // shirt front
            DreamSkin.Fill(new Rect(feet.x - 30f, feet.y - 196f + bob, 16f, 70f), ClerkInk, 6f);                       // lapels
            DreamSkin.Fill(new Rect(feet.x + 14f, feet.y - 196f + bob, 16f, 70f), ClerkInk, 6f);
            DreamSkin.Fill(new Rect(feet.x - 16f, feet.y - 194f + bob, 14f, 10f), HauntedSkin.BloodRed, 4f);         // bow tie
            DreamSkin.Fill(new Rect(feet.x + 2f, feet.y - 194f + bob, 14f, 10f), HauntedSkin.BloodRed, 4f);
            DreamSkin.Fill(new Rect(feet.x - 4f, feet.y - 193f + bob, 8f, 8f), HauntedSkin.StampRed, 3f);
            var head = new Rect(feet.x - 36f, feet.y - 284f + bob, 72f, 86f);
            DreamSkin.Fill(new Rect(feet.x - 14f, feet.y - 206f + bob, 28f, 16f), ClerkInk, 4f);                      // neck
            DreamSkin.Fill(head, ClerkInk, 34f);
            DreamSkin.Border(head, new Color(.66f, .55f, 1f, .3f), 1.5f, 34f);
            DreamSkin.Fill(new Rect(head.x - 2f, head.y - 4f, head.width + 4f, 30f), new Color32(0x05, 0x02, 0x04, 255), 14f); // slicked hair
            bool blink = Mathf.Repeat(now, 4.3f) < .12f;
            foreach (float side in new[] { -1f, 1f })
            {
                var eye = new Vector2(feet.x + side * 14f, head.y + 44f);
                HauntedSkin.GlowAt(eye, new Vector2(34f, 26f), new Color(1f, .75f, .2f, .55f));
                DreamSkin.Fill(new Rect(eye.x - 7f, eye.y - (blink ? 1f : 3.5f), 14f, blink ? 2f : 7f), HauntedSkin.EyeYellow, 3f);
            }
            DreamSkin.Fill(new Rect(feet.x - 12f, head.y + 64f, 24f, 2f), new Color(.5f, .2f, .2f, .6f), 1f);          // thin smile
        }

        void DrawSpeechBubble(Vector2 at, string text, float now)
        {
            if (!repaint) return;
            var style = HauntedSkin.Serif(19, true);
            float w = 300f;
            var lines = new List<string>();
            HauntedSkin.WrapLines(text, style, w - 36f, lines);
            float h = 26f + lines.Count * 24f;
            var bubble = new Rect(Mathf.Max(12f, at.x - w), at.y - h + Mathf.Sin(now * 2f) * 1.5f, w, h);
            HauntedSkin.GlowAt(bubble.center + Vector2.up * 6f, bubble.size * 1.15f, new Color(0, 0, 0, .6f));
            DreamSkin.Fill(bubble, HauntedSkin.Paper, 18f);
            DreamSkin.Border(bubble, HauntedSkin.PaperDark, 2f, 18f);
            // the tail, stepping down toward the clerk
            DreamSkin.Fill(new Rect(bubble.xMax - 40f, bubble.yMax - 4f, 18f, 16f), HauntedSkin.Paper, 8f);
            DreamSkin.Fill(new Rect(bubble.xMax - 22f, bubble.yMax + 12f, 10f, 10f), HauntedSkin.Paper, 5f);
            for (int i = 0; i < lines.Count; i++)
                HauntedSkin.Label(new Rect(bubble.x + 18f, bubble.y + 13f + i * 24f, w - 36f, 24f), lines[i], style, HauntedSkin.Ink, TextAnchor.UpperLeft);
        }

        // ------------------------------------------------------------ the lift (monster player)

        void DrawLiftScreen(Monster m)
        {
            HauntedSkin.Ensure();
            float now = HauntedClock.Now;
            if (!liftWasOpen) { liftOpenedAt = now; liftWasOpen = true; liftPick = gm.HuntableFloors(m).FirstOrDefault(); }
            bool riding = m.LiftTarget != null;
            if (riding && liftRideStartAt < 0f) liftRideStartAt = now;
            if (!riding) liftRideStartAt = -1f;
            var floors = gm.Map.Rooms;
            var huntable = new HashSet<RoomDef>(gm.HuntableFloors(m));
            if (liftPick != null && !huntable.Contains(liftPick)) liftPick = huntable.FirstOrDefault();

            var all = new Rect(0, 0, vw, VH);
            Ui(all);
            // The car: damask walls, a brass gate across the middle, lamplight from above.
            float shake = riding ? Mathf.Sin(now * 38f) * 2.2f * Mathf.Clamp01((now - liftRideStartAt) * 4f) : 0f;
            HauntedSkin.Fill(all, HauntedSkin.Plum);
            HauntedSkin.Tiled(new Rect(0, shake, vw, VH), HauntedSkin.Texture("damask"), new Vector2(180f, 180f), Vector2.zero);
            HauntedSkin.Fill(all, new Color(.05f, .02f, .05f, .55f));
            HauntedSkin.GlowAt(new Vector2(vw * .5f, 40f), new Vector2(vw * .9f, 340f), new Color(1f, .82f, .48f, .16f));

            var gate = new Rect(vw * .5f - 230f, 190f + shake, 460f, VH - 250f);
            HauntedSkin.Fill(new Rect(gate.x - 10f, gate.y - 10f, gate.width + 20f, gate.height + 20f), HauntedSkin.WoodBottom, 4f);
            // Beyond the gate: the hallway the car is facing (dark while moving, the floor's lamplight when stopped).
            HauntedSkin.Fill(gate, riding ? new Color(.02f, .01f, .02f, 1f) : new Color(.16f, .07f, .09f, 1f));
            if (!riding && liftPick != null)
                HauntedSkin.Tex(new Rect(gate.center.x - 150f, gate.center.y - 150f, 300f, 300f), FloorThumb(liftPick), new Color(1f, 1f, 1f, .5f));
            // The scissor gate: two leaves that close while the car moves.
            float close = riding ? Mathf.Clamp01((now - liftRideStartAt) / .35f) : 1f - Mathf.Clamp01((now - liftOpenedAt) / .5f) * .7f;
            float leaf = Mathf.Lerp(gate.width * .15f, gate.width * .5f, close);
            HauntedSkin.Gate(new Rect(gate.x, gate.y, leaf, gate.height));
            HauntedSkin.Gate(new Rect(gate.xMax - leaf, gate.y, leaf, gate.height));
            HauntedSkin.BrassRim(gate, 3f);

            // The floor dial above the gate.
            DrawLiftCarDial(m, new Vector2(vw * .5f, 168f + shake), floors.Count, now);

            // Fear, shared by every night's monster.
            var fear = new Rect(24f, 24f, 300f, 54f);
            HauntedSkin.WoodFrame(fear);
            DreamSkin.Icon(new Rect(fear.x + 16f, fear.y + 13f, 28f, 28f), "fear", Color.white);
            HauntedSkin.Label(new Rect(fear.x + 52f, fear.y + 4f, fear.width - 60f, 28f), Mathf.FloorToInt(gm.Fear) + " Fear", HauntedSkin.Gothic(24, true), FearPink, TextAnchor.MiddleLeft);
            HauntedSkin.Label(new Rect(fear.x + 52f, fear.y + 30f, fear.width - 60f, 18f), "shared · what you leave, the next monster keeps", HauntedSkin.Typed(10), HauntedSkin.Bone, TextAnchor.MiddleLeft);
            var who = new Rect(vw * .5f - 230f, 20f, 460f, 40f);
            HauntedSkin.Label(who, m.Def.name + " · " + gm.BuffText(m.Def) + " on the floor you visit", HauntedSkin.Serif(18, true), HauntedSkin.Bone);

            DrawLiftButtons(m, huntable, riding, now);
            if (liftPick != null) DrawFloorRegister(m, liftPick, riding);
        }

        /// <summary>A large version of the top bar's lift dial: one numeral per floor, the needle on the floor the car is at or heading to.</summary>
        void DrawLiftCarDial(Monster m, Vector2 pivot, int count, float now)
        {
            if (!repaint) return;
            const float s = 1.6f;
            var housing = new Rect(pivot.x - 100f * s, pivot.y - 84f * s, 200f * s, 96f * s);
            HauntedSkin.GlowAt(pivot + Vector2.up * 6f, new Vector2(380f, 140f), new Color(0, 0, 0, .55f));
            HauntedSkin.Tex(housing, HauntedSkin.Texture("lift_housing"), Color.white);
            HauntedSkin.Tex(new Rect(pivot.x - 72f * s, pivot.y - 72f * s, 144f * s, 72f * s), HauntedSkin.Texture("dial_face"), Color.white);
            int at = m.LiftTarget != null ? m.LiftTarget.FloorNumber : m.Floor != null ? m.Floor.FloorNumber : liftFromFloor;
            if (m.LiftTarget == null && m.Floor != null) liftFromFloor = m.Floor.FloorNumber;
            float from = LiftDialLayout.NumeralAngle(liftFromFloor, count), to = LiftDialLayout.NumeralAngle(at, count);
            float t = m.LiftTarget != null ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((gm.Now - (m.LiftArriveAt - GameManager.LiftRideSeconds)) / GameManager.LiftRideSeconds)) : 1f;
            float angle = Mathf.Lerp(from, to, t) + .8f * Mathf.Sin(1.3f * now);
            for (int i = 1; i <= count; i++)
            {
                float a = LiftDialLayout.NumeralAngle(i, count) * Mathf.Deg2Rad;
                var p = pivot + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * 55f * s;
                if (i == at) HauntedSkin.GlowAt(p, Vector2.one * 34f, new Color(FearPink.r, FearPink.g, FearPink.b, .55f));
                HauntedSkin.Label(new Rect(p.x - 16f, p.y - 13f, 32f, 26f), i.ToString(), HauntedSkin.Gothic(20), i == at ? HauntedSkin.BloodRed : HauntedSkin.Ink);
            }
            HauntedSkin.Rotated(angle, pivot, () => HauntedSkin.Tex(new Rect(pivot.x - 6f * s, pivot.y - 50f * s, 12f * s, 50f * s), HauntedSkin.Texture("needle_finger"), Color.white));
            HauntedSkin.Tex(new Rect(pivot.x - 11f * s, pivot.y - 12f * s, 22f * s, 24f * s), HauntedSkin.Texture("skull_hub"), Color.white);
        }

        /// <summary>The brass panel on the right: one call button per floor, top floor first; dark where there is nobody to hunt.</summary>
        void DrawLiftButtons(Monster m, HashSet<RoomDef> huntable, bool riding, float now)
        {
            var floors = gm.Map.Rooms;
            int columns = 2, rows = Mathf.CeilToInt(floors.Count / (float)columns);
            float d = Mathf.Min(78f, (VH - 300f) / rows - 12f);
            var plate = new Rect(vw - 70f - columns * (d + 18f), 120f, columns * (d + 18f) + 40f, rows * (d + 14f) + 110f);
            Ui(plate);
            HauntedSkin.Fill(plate, HauntedSkin.BrassLow, 10f);
            HauntedSkin.NineSlice(plate, HauntedSkin.Texture("brass_9"), new RectOffset(18, 18, 18, 18), Color.white);
            HauntedSkin.Label(new Rect(plate.x, plate.y + 10f, plate.width, 26f), "FLOORS", HauntedSkin.SmallCaps(16), HauntedSkin.Ink);
            for (int i = 0; i < floors.Count; i++)
            {
                var def = floors[floors.Count - 1 - i];
                int row = i / columns, col = i % columns;
                var c = new Vector2(plate.x + 20f + d * .5f + col * (d + 18f), plate.y + 52f + d * .5f + row * (d + 14f));
                bool can = huntable.Contains(def) && !riding;
                bool lit = def == liftPick || def == m.LiftTarget;
                HauntedSkin.CallButton(c, d, null, lit);
                HauntedSkin.Label(new Rect(c.x - d * .5f, c.y - d * .5f, d, d), def.FloorNumber.ToString(), HauntedSkin.Gothic(Mathf.RoundToInt(d * .42f), true),
                    can || lit ? (lit ? HauntedSkin.BrassLow : HauntedSkin.Ivory) : new Color(.4f, .3f, .3f, .7f));
                if (!huntable.Contains(def))
                {
                    string why = gm.RoomsByDef.TryGetValue(def, out var room) ? room.Owner == m.Guest ? "yours" : room.Owner != null && !room.Owner.Alive ? "dead" : "" : "empty";
                    HauntedSkin.Label(new Rect(c.x - d * .5f, c.y + d * .22f, d, 16f), why, HauntedSkin.Typed(10), new Color(.95f, .6f, .6f, .85f));
                }
                var r = new Rect(c.x - d * .5f, c.y - d * .5f, d, d);
                if (can) TapZone(r, () => liftPick = def, round: true);
            }
            // Ride: the big lever under the buttons.
            var ride = new Rect(plate.x + 20f, plate.yMax - 50f, plate.width - 40f, 38f);
            bool canRide = liftPick != null && !riding;
            HauntedSkin.Fill(ride, canRide ? HauntedSkin.BloodRed : HauntedSkin.WoodTop, 6f);
            HauntedSkin.BrassRim(ride, 2f);
            HauntedSkin.Label(ride, riding ? "Going up..." : liftPick != null ? "Ride to " + liftPick.FloorNumber : "Nobody to hunt", HauntedSkin.Gothic(20, true), HauntedSkin.Ivory);
            if (canRide) TapZone(ride, () => gm.RideLift(m, liftPick));
        }

        /// <summary>The selected floor's register: who lives there, how well it is defended, and its waves, which Fear grows for good.</summary>
        void DrawFloorRegister(Monster m, RoomDef def, bool riding)
        {
            if (!gm.RoomsByDef.TryGetValue(def, out var room)) return;
            var card = new Rect(30f, 104f, Mathf.Min(360f, vw * .5f - 270f), VH - 140f);
            if (card.width < 250f) card.width = 250f;
            Ui(card);
            HauntedSkin.RegisterCard(card);
            float x = card.x + 22f, w = card.width - 44f, y = card.y + 18f;
            HauntedSkin.Label(new Rect(x, y, w, 30f), "Floor " + def.FloorNumber + " · Room " + def.RoomNumber, HauntedSkin.Gothic(24, true), HauntedSkin.Ink, TextAnchor.UpperLeft);
            y += 32f;
            var owner = room.Owner;
            HauntedSkin.Label(new Rect(x, y, w, 20f), (owner != null ? owner.Name : "?") + " · " + Mathf.CeilToInt(owner != null ? owner.Health : 0) + " health", HauntedSkin.Typed(13), HauntedSkin.Ink, TextAnchor.UpperLeft);
            y += 20f;
            int weapons = room.WeaponCount(), levels = room.Slots.Where(t => t != null && t.IsWeapon).Sum(t => t.Level);
            HauntedSkin.Label(new Rect(x, y, w, 20f), weapons + " towers (" + levels + " levels) · door " + room.DoorLevel + (room.DoorBroken ? " broken" : ""),
                HauntedSkin.Typed(13), HauntedSkin.Ink, TextAnchor.UpperLeft);
            y += 20f;
            HauntedSkin.Label(new Rect(x, y, w, 20f), def.Lane.Count + " steps from the lift to the door", HauntedSkin.Typed(13), HauntedSkin.Ink, TextAnchor.UpperLeft);
            y += 30f;
            HauntedSkin.Fill(new Rect(x, y, w, 1f), HauntedSkin.BrassMid);
            y += 10f;

            HauntedSkin.Label(new Rect(x, y, w, 26f), "Waves", HauntedSkin.Gothic(22, true), HauntedSkin.BloodRed, TextAnchor.UpperLeft);
            y += 28f;
            HauntedSkin.Label(new Rect(x, y, w, 20f), room.WaveSize + " minions per wave, every wave tonight and after", HauntedSkin.Typed(12), HauntedSkin.Ink, TextAnchor.UpperLeft);
            y += 24f;
            float grow = gm.WaveSizeCost(room);
            var growButton = new Rect(x, y, w, 36f);
            LiftShopButton(growButton, grow < 0 ? "Waves are as big as they get" : "+1 minion per wave", grow, () => gm.TryGrowWave(room));
            y += 46f;

            var line = gm.MinionLine(m);
            for (int i = 0; i < room.WaveTypes.Length && i < line.creatures.Length; i++)
            {
                var creature = line.creatures[i];
                var row = new Rect(x, y, w, 58f);
                HauntedSkin.Fill(row, new Color(0f, 0f, 0f, .08f), 4f);
                var art = Sprites.MinionArt(line.id, creature.role);
                if (art != null) HauntedSkin.Tex(new Rect(row.x + 4f, row.y + 4f, 50f, 50f), art, Color.white);
                HauntedSkin.Label(new Rect(row.x + 60f, row.y + 4f, row.width - 64f, 20f), creature.name, HauntedSkin.Gothic(17, true), HauntedSkin.Ink, TextAnchor.UpperLeft);
                HauntedSkin.Label(new Rect(row.x + 60f, row.y + 24f, row.width - 64f, 16f), "resists " + creature.resist + " · " + Mathf.RoundToInt(creature.health) + " hp", HauntedSkin.Typed(11), HauntedSkin.CostInk, TextAnchor.UpperLeft);
                if (room.WaveTypes[i])
                    HauntedSkin.Label(new Rect(row.x + 60f, row.y + 38f, row.width - 64f, 16f), "in this floor's waves", HauntedSkin.Typed(11), HauntedSkin.MintDeep, TextAnchor.UpperLeft);
                else
                {
                    int type = i;
                    float cost = gm.WaveTypeCost(room, i);
                    LiftShopButton(new Rect(row.xMax - 118f, row.y + 30f, 114f, 24f), "Add", cost, () => gm.TryAddWaveType(room, type), small: true);
                }
                y += 64f;
            }
        }

        void LiftShopButton(Rect r, string label, float cost, System.Func<ActionResult> buy, bool small = false)
        {
            bool afford = cost >= 0f && gm.Fear + .001f >= cost;
            HauntedSkin.Fill(r, cost < 0f ? new Color(0, 0, 0, .1f) : afford ? HauntedSkin.BloodRed : new Color(.35f, .2f, .22f, 1f), 5f);
            HauntedSkin.BrassRim(r, 1.5f);
            string text = cost < 0f ? label : label + " · " + Mathf.CeilToInt(cost) + " Fear";
            HauntedSkin.Label(r, text, small ? HauntedSkin.Typed(11) : HauntedSkin.Gothic(17, true), cost < 0f ? HauntedSkin.Ink : HauntedSkin.Ivory);
            if (cost >= 0f) TapZone(r, () =>
            {
                var res = buy();
                if (res == ActionResult.Ok) fxFront.Burst(r.center, 18, FearPink, 160f, 8f, .6f);
                else ReportFear(res, cost - gm.Fear);
            });
        }

        // ------------------------------------------------------------ monster player on a floor

        void DrawFloorMonsterUI(Monster m)
        {
            if (m.InLift && !m.Dead) { DrawLiftScreen(m); return; }
            liftWasOpen = false;
            // Corner chip: health, the shared Fear, tonight's floor buff.
            var r = new Rect(10, 146, 310, 86);
            DreamSkin.Panel(r);
            Ui(r);
            DreamSkin.Label(new Rect(r.x + 14, r.y + 6, r.width - 28, 24), "<b>" + m.Def.name + "</b>  ·  floor " + (m.Floor != null ? m.Floor.FloorNumber.ToString() : "?"), DreamSkin.Body, DreamSkin.Bone, TextAnchor.MiddleLeft);
            Bar(new Rect(r.x + 14, r.y + 34, r.width - 28, 9), m.Dead ? 0f : m.Hp / gm.MaxHp(m), Red);
            DreamSkin.Icon(new Rect(r.x + 12, r.y + 50, 26, 26), "fear", Color.white);
            DreamSkin.Label(new Rect(r.x + 42, r.y + 50, r.width - 56, 26), "<b><color=" + FearHex + ">" + Mathf.FloorToInt(gm.Fear) + "</color></b>   <size=12>" + gm.BuffText(m.Def) + " here</size>", DreamSkin.Body, DreamSkin.BoneDim, TextAnchor.MiddleLeft);
            if (m.Dead)
            {
                var b = new Rect(vw / 2 - 200, VH / 2 - 30, 400, 60);
                GUI.Box(b, GUIContent.none, box);
                Ui(b);
                GUI.Label(b, "Banished! The lift takes you back in " + Mathf.CeilToInt(Mathf.Max(0f, m.RespawnAt - gm.Now)) + " s", center);
                return;
            }
            DrawJoystick();
            bool night = gm.Phase == Phase.Night;
            float right = vw - (Screen.width - Screen.safeArea.xMax) / scale, bottom = VH - Screen.safeArea.yMin / scale;
            var spots = new[] { new Vector2(right - 82f, bottom - 86f), new Vector2(right - 206f, bottom - 66f), new Vector2(right - 70f, bottom - 214f), new Vector2(right - 176f, bottom - 184f) };
            for (int i = 0; i < m.Loadout.Length && i < spots.Length; i++)
            {
                var a = m.Loadout[i];
                int idx = i;
                bool area = a.id == m.Def.area, special = a.id == m.Def.special;
                IconButton(spots[i], area ? 112f : special ? 94f : 78f, AbilityIcon(a), a.name, area ? gm.ColorOf(m) : special ? Brass : DreamSkin.Bone,
                    night, m.Cooldowns[i], a.cooldownSeconds, "[" + (i + 1) + "]", () => gm.UseAbility(idx));
            }
            // The lift: back at the landing, call the car to change floors (and spend Fear).
            bool near = gm.NearLift(m);
            IconButton(new Vector2(right - 318f, bottom - 62f), 90f, "stat_horde", near ? "Lift" : "Lift (walk back)", Brass, near, 0f, 1f, "[L]", () => gm.EnterLift(m));
            if (near) DreamSkin.GlowAt(new Vector2(right - 318f, bottom - 62f), Vector2.one * 150f, A(Brass, .25f + .15f * Mathf.Sin(Time.unscaledTime * 3f)));
        }

        /// <summary>Resident view at night on the hallway floors: who is the monster tonight and where it is.</summary>
        void DrawFloorsNightChip()
        {
            if (gm.Phase == Phase.CheckIn && gm.CheckingIn != null)
            {
                var c = new Rect(vw - 270, 92, 260, 48);
                Panel(c);
                var at = gm.CheckingIn;
                GUI.Label(new Rect(c.x + 8, c.y + 4, c.width - 16, 20), "<b>Front desk</b>  ·  " + (at.Char != null ? at.Char.name : at.Name) + " is taking a key", small);
                GUI.Label(new Rect(c.x + 8, c.y + 24, c.width - 16, 20), (gm.CheckInOrder.Count - gm.CheckInIndex) + " guests still in line", small);
                return;
            }
            if (gm.Phase != Phase.Night || gm.Monster == null) return;
            var m = gm.Monster;
            var r = new Rect(vw - 270, 92, 260, 70);
            Panel(r);
            string who = m.Guest != null ? m.Guest.Name : "?";
            GUI.Label(new Rect(r.x + 8, r.y + 4, r.width - 16, 20), "<b>" + who + "</b> is " + m.Def.name, small);
            string where = m.Dead ? "banished, back soon" : m.InLift ? (m.LiftTarget != null ? "riding to floor " + m.LiftTarget.FloorNumber : "in the lift") : "on floor " + (m.Floor != null ? m.Floor.FloorNumber.ToString() : "?");
            bool mine = gm.Human != null && gm.Human.Room != null && m.Floor == gm.Human.Room.Def && !m.InLift;
            GUI.Label(new Rect(r.x + 8, r.y + 24, r.width - 16, 20), (mine ? "<color=#D7263D><b>ON YOUR FLOOR</b></color>" : where), small);
            GUI.Label(new Rect(r.x + 8, r.y + 44, r.width - 16, 20), "Its floor gets " + gm.BuffText(m.Def), small);
        }

        void DrawFloorsResults(MatchResult res)
        {
            var r = new Rect(vw / 2 - 300, 120, 600, 460);
            GUI.Box(r, GUIContent.none, box);
            Ui(r);
            float y = r.y + 16;
            bool won = res.Placement == 1;
            GUI.Label(new Rect(r.x, y, r.width, 50), won ? "LAST GUEST STANDING" : "CHECKED OUT EARLY", subtitle);
            y += 50;
            string placed = won ? "<color=#F2C14E><b>You win.</b></color>"
                : "<color=#D7263D><b>You placed " + res.Placement + " of " + gm.Residents.Count + ".</b></color>" + (res.WinnerName != null ? "  " + res.WinnerName + " won." : "");
            GUI.Label(new Rect(r.x, y, r.width, 30), placed, center);
            y += 36;
            if (gm.Human != null)
                GUI.Label(new Rect(r.x + 30, y, r.width - 60, 24), "Nights survived: " + gm.Human.NightsSurvived + "   ·   night " + gm.Night + "   ·   " + gm.Difficulty.name, label);
            y += 28;
            GUI.Label(new Rect(r.x + 30, y, r.width - 60, 24), "+" + res.XpGained + " Resident XP   (level " + res.LevelBefore +
                (res.LevelAfter > res.LevelBefore ? " -> <color=#F2C14E>" + res.LevelAfter + "</color>" : "") + ")", label);
            y += 36;
            foreach (var rr in gm.Residents.OrderByDescending(x => x.Alive).ThenByDescending(x => x.DiedAt))
            {
                string status = rr.Alive ? "<color=#9FE3C8>alive</color>" : "<color=#D7263D>eaten on night " + Mathf.Max(1, rr.NightsSurvived + 1) + "</color>";
                string room = rr.Room != null ? "Room " + rr.Room.Def.RoomNumber : "";
                GUI.Label(new Rect(r.x + 30, y, r.width - 60, 22), (rr.IsHuman ? "You" : rr.Name) + "  ·  " + room + "  ·  " + status, small);
                y += 22;
            }
            if (GUI.Button(new Rect(r.x + 60, r.yMax - 60, 220, 44), "Play again", bigButton)) gm.StartMatch(Role.Resident, monsterPick);
            if (GUI.Button(new Rect(r.xMax - 280, r.yMax - 60, 220, 44), "Main menu", bigButton)) gm.ReturnToMenu();
        }
    }
}
