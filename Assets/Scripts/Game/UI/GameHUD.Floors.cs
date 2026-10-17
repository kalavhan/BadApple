using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>
    /// Screens for the hallway floors. The front desk: the manager calls guests up one at a time, and the room list
    /// shows every floor's hallway as a register card with its plan; the live floor behind it follows the card you
    /// look at. The lift: the monster player's first-person car, with the floor dial above the gate, brass floor
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

        /// <summary>Fraction of the screen width the register covers; the camera frames the previewed floor beside it.</summary>
        const float DeskPanelFraction = GameManager.DeskPanelFraction;

        void DrawFrontDesk()
        {
            HauntedSkin.Ensure();
            var safe = SafeRect();
            float panelW = Mathf.Min(vw * DeskPanelFraction, 820f);
            var panel = new Rect(safe.xMin + 14f, safe.yMin + 112f, panelW - 20f, safe.yMax - safe.yMin - 128f);
            Ui(panel);
            HauntedSkin.WoodFrame(panel);
            var guest = gm.CheckingIn;
            float now = HauntedClock.Now;

            // The manager behind the counter, and who is at the desk.
            var head = new Rect(panel.x + 24f, panel.y + 18f, panel.width - 48f, 34f);
            HauntedSkin.Label(head, "Front Desk", HauntedSkin.Gothic(30, true), HauntedSkin.BrassHi, TextAnchor.MiddleLeft);
            string line = guest == null ? "\"That's everyone. Sleep tight.\""
                : guest.IsHuman ? "\"Good evening. Which room shall it be? " + Mathf.CeilToInt(Mathf.Max(0f, gm.PhaseTimer)) + "\""
                : "\"" + guest.Name + ", your key, and do lock the door...\"";
            HauntedSkin.Label(new Rect(head.x, head.yMax, head.width, 24f), line, HauntedSkin.Serif(18, true), HauntedSkin.Bone, TextAnchor.MiddleLeft);

            // The queue: everyone in calling order, the current guest lit, checked-in guests with their room.
            float qx = head.x, qy = head.yMax + 30f;
            for (int i = 0; i < gm.CheckInOrder.Count; i++)
            {
                var r = gm.CheckInOrder[i];
                bool current = r == guest, done = r.Room != null;
                string label = (r.IsHuman ? "You" : r.Name) + (done ? " · " + r.Room.Def.RoomNumber : "");
                var style = HauntedSkin.Typed(12);
                float w = HauntedSkin.Measure(label, style).x + 18f;
                if (qx + w > panel.xMax - 24f) { qx = head.x; qy += 26f; }
                var chip = new Rect(qx, qy, w, 22f);
                HauntedSkin.Fill(chip, current ? HauntedSkin.Brass : done ? HauntedSkin.WoodTop : HauntedSkin.Plum, 4f);
                if (current) HauntedSkin.GlowAt(chip.center, chip.size * 1.6f, new Color(1f, .82f, .48f, .25f + .15f * Mathf.Sin(now * 4f)));
                HauntedSkin.Label(chip, label, style, current ? HauntedSkin.Ink : done ? new Color(.75f, .7f, .62f, 1f) : HauntedSkin.Bone);
                qx += w + 6f;
            }

            // The room list: one register card per floor, with its hallway plan.
            int columns = 3;
            int rows = Mathf.CeilToInt(gm.Map.Rooms.Count / (float)columns);
            float gridTop = qy + 34f, gridBottom = panel.yMax - 74f;
            float gap = 10f;
            float cardW = (panel.width - 48f - gap * (columns - 1)) / columns;
            float cardH = Mathf.Min((gridBottom - gridTop - gap * (rows - 1)) / rows, cardW * .62f);
            for (int i = 0; i < gm.Map.Rooms.Count; i++)
            {
                var def = gm.Map.Rooms[i];
                var card = new Rect(panel.x + 24f + (i % columns) * (cardW + gap), gridTop + (i / columns) * (cardH + gap), cardW, cardH);
                DrawFloorCard(def, card, guest);
            }

            // Take the room (the human at the desk), or wait for the others.
            var foot = new Rect(panel.x + 24f, panel.yMax - 62f, panel.width - 48f, 46f);
            var pick = gm.PreviewFloor;
            if (gm.HumanAtDesk && pick != null && gm.IsRoomFree(pick))
            {
                var button = new Rect(foot.xMax - 300f, foot.y, 300f, foot.height);
                HauntedSkin.Fill(button, HauntedSkin.MintDeep, 6f);
                HauntedSkin.GlowAt(button.center, button.size * 1.3f, new Color(.5f, 1f, .85f, .2f + .1f * Mathf.Sin(now * 3f)));
                HauntedSkin.BrassRim(button, 2f);
                HauntedSkin.Label(button, "Take Room " + pick.RoomNumber, HauntedSkin.Gothic(24, true), HauntedSkin.MintHi);
                TapZone(button, () => { if (gm.PickFloor(gm.Human, gm.PreviewFloor)) fxFront.Say(button.center - new Vector2(0, 40), "CHECKED IN", "ROOM " + pick.RoomNumber); });
                HauntedSkin.Label(new Rect(foot.x, foot.y, foot.width - 310f, foot.height),
                    "Longer hallways give your towers more time. The lift is at the start, your room at the end.", HauntedSkin.Serif(15), HauntedSkin.Bone, TextAnchor.MiddleLeft);
            }
            else
            {
                string wait = gm.HumanAtDesk ? "Pick a free room from the register." :
                    gm.Human != null && gm.Human.Room != null ? "You are in Room " + gm.Human.Room.Def.RoomNumber + ". The others are still checking in..." :
                    "Waiting for your turn at the desk...";
                HauntedSkin.Label(foot, wait, HauntedSkin.Serif(17, true), HauntedSkin.Bone, TextAnchor.MiddleLeft);
            }
        }

        void DrawFloorCard(RoomDef def, Rect card, Resident guest)
        {
            gm.RoomsByDef.TryGetValue(def, out var room);
            bool free = room == null, previewed = gm.PreviewFloor == def;
            HauntedSkin.RegisterCard(card);
            if (previewed) HauntedSkin.Border(new Rect(card.x - 2f, card.y - 2f, card.width + 4f, card.height + 4f), HauntedSkin.Mint, 2.5f, 6f);
            float thumb = card.height - 16f;
            var plan = new Rect(card.x + 8f, card.y + 8f, thumb, thumb);
            HauntedSkin.Fill(plan, new Color(.07f, .03f, .05f, 1f), 3f);
            HauntedSkin.Tex(plan, FloorThumb(def), free ? Color.white : new Color(1f, 1f, 1f, .45f));
            var text = new Rect(plan.xMax + 8f, card.y + 6f, card.xMax - plan.xMax - 14f, card.height - 12f);
            HauntedSkin.Label(new Rect(text.x, text.y, text.width, 24f), "Room " + def.RoomNumber, HauntedSkin.Gothic(20, true), HauntedSkin.Ink, TextAnchor.UpperLeft);
            HauntedSkin.Label(new Rect(text.x, text.y + 24f, text.width, 16f), "Floor " + def.FloorNumber, HauntedSkin.SmallCaps(12), HauntedSkin.BrassLow, TextAnchor.UpperLeft);
            HauntedSkin.Label(new Rect(text.x, text.y + 42f, text.width, 16f), def.Lane.Count + " steps of hallway", HauntedSkin.Typed(11), HauntedSkin.Ink, TextAnchor.UpperLeft);
            HauntedSkin.Label(new Rect(text.x, text.y + 58f, text.width, 16f), def.BuildTiles.Count + " tower pads", HauntedSkin.Typed(11), HauntedSkin.CostInk, TextAnchor.UpperLeft);
            if (!free)
            {
                var stamp = new Rect(card.center.x - card.width * .3f, card.center.y - card.height * .28f, card.width * .6f, card.height * .56f);
                HauntedSkin.Rotated(-12f, stamp.center, () => HauntedSkin.Tex(stamp, HauntedSkin.Texture("stamp_no_vacancy"), new Color(1f, 1f, 1f, .85f)));
                HauntedSkin.Label(new Rect(text.x, card.yMax - 22f, text.width, 16f), room.Owner != null ? (room.Owner.IsHuman ? "You" : room.Owner.Name) : "", HauntedSkin.Typed(12), HauntedSkin.BloodRed, TextAnchor.UpperLeft);
            }
            TapZone(card, () => gm.PreviewFloor = def);
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
