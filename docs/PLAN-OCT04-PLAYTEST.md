# Bad Apple Hotel: playtest fixes plan (Oct 4, afternoon)

Repo on your PC: `/home/josue/Documents/BadApple`
GitHub: https://github.com/kalavhan/BadApple (branch `main`, latest `0a52e2f`, merge of PR #1 "crooked 10-room hotels with static tower tiers")

This plan answers the 12 points from the playtest. It is based on the code at `0a52e2f`.

## Why it feels the way it does (root causes found in the code)

| Symptom | Cause in the code |
|---|---|
| Monster too slow | `monsters.json`: Chef 3.2, Matron 2.6, Wraith 4.0 tiles/s. Residents walk at 3.6 (`residents.json`), so 2 of 3 monsters are slower than their prey. Legs add only +5% each. |
| Getting stuck | `Slide()` in `GameManager.Residents.cs` tests the 4 corners of a box and moves one axis at a time. Moving diagonally into a 1-tile doorway or a wall corner blocks both axes, so you stop dead. The monster's BFS path goes from tile centre to tile centre, and its goal may be a closed door tile it can never enter. Stuck recovery only replans the same path after 1.2 s. |
| No attacks in 6 nights | `MonsterAI.Plan()` adds +30 cost when "work > HP / threat" ("would probably die first") and scales cost by tower threat. Once rooms have a few towers, every resident looks too costly, so the bot eats body parts or wanders. It also does nothing during setup. |
| Not enough pressure | The monster has no in-match growth except body parts (max 4 per type: +600 HP, +40% attack, +20% speed) and 3 resistance levels. Residents grow every second (beds up to 11 DP/s, tiers up to about 400 damage per shot, doors up to 3,400 HP with 45% resistance). Nights don't escalate. |
| Door handling is fiddly | Sleeping and the door are separate actions (`ActionFor`: door wins when near it, bed only within 0.7 tiles). Sleep never closes the door. |
| Camera look-around | While asleep the joystick pans the camera (`LateUpdate` in `GameManager.cs`, 14 tiles/s). There is no drag-to-scroll. |
| Menu hidden behind boxes | `DrawResidentUI()` draws the popups, then `DrawLog()` and `DrawToast()` draw on top of them. `PopupRect()` only avoids the right 280 px when the popup opens to the right, and its clamp can still push it under the resources and monster-intel panels. |
| Rooms look like letters | `HotelMap.cs` grows every room from a `LetterShapes` bitmap (`map.json` → `letters`). |
| Monster is known from the start | `StartMatch()` creates the monster as a separate entity at `MonsterSpawn` and announces its name. The HUD's monster-intel panel shows it from second 1. |
| Match always waits 60 s | `Update()` only calls `BeginNights()` when `PhaseTimer <= 0`. |
| Levels are capped | `economy.json` `levelCurve` stops at 50. Abilities unlock by account level only (`abilities.json`), and there is no in-match monster level. |

## Phase 1: quick wins (about 1 day)

### 1.1 Monster speed
- `monsters.json`: Chef 4.4, Matron 3.9, Wraith 5.0. The rule is that every monster is at least 1.1× resident speed (3.6).
- Add a "hunt sprint": +25% speed for up to 2 s when the monster sees a resident in the hallway. Cooldown 8 s. Config in `monsters.json` (`sprintMultiplier`, `sprintSeconds`, `sprintCooldown`).
- Speed also grows with monster level (Phase 5).
- Done when: a monster in the hallway always catches a walking resident, and residents can still escape by reaching their room and closing the door.

### 1.2 Door closes automatically on sleep
- `TrySleep()`: if the doorway is clear, shut the door (`room.DoorOpen = false; RefreshDoor`). If someone is standing in it, set `room.CloseWhenClear = true` and close it on the first frame the doorway is free.
- Simplify the action button. Inside your room, the button is always **Sleep**, wherever you stand. Pressing it auto-walks you to the bed (reuse the bot path code), closes the door behind you and lies down. Near the door it still offers Open/Close as a second, smaller button.
- Resident bots use the same call, so their door is never left open by accident.
- Done when: from anywhere in your room, one tap gets you into bed with the door shut.

### 1.3 Start when everyone has a room
- In `Update()`: during setup, if every living guest (including the hidden monster, see Phase 4) has a room, set `PhaseTimer = min(PhaseTimer, 3)` and show "Lights out in 3…".
- Done when: if all 7 guests claim a room at second 20, the night starts at second 23.

### 1.4 Menus always on top and fully visible
- Draw order in `GameHUD.OnGUI`: world labels → top bar → banner → log → toast → resident/monster HUD → **popups last** (build, tower, bed, door, help). Move `DrawSelection` out of `DrawResidentUI` into a final pass.
- `PopupRect()`: test the popup against every HUD rect registered this frame (resources, monster intel, log, banner, buttons, joystick) and try right, left, above and below the tile. Take the first spot with no overlap, otherwise the one with least overlap. Then clamp into `Screen.safeArea`.
- Popups that are taller than the screen (build menu on a small phone) become a bottom sheet.
- Done when: at 16:9, 19.5:9 and 4:3, every popup is fully readable next to tiles in all four corners of the screen.

### 1.5 Drag the screen to look around
- New `CameraDrag` in `GameInput`: one finger (or left mouse) dragging more than 12 px pans the camera like scrolling a page. The content follows the finger, with inertia when released. Below 12 px it is still a tap, so building and selecting still work.
- Works while asleep, in hotel view and while dead (spectating). While awake and walking, a drag also pans; the camera eases back to you after 2 s without input or when you move.
- Remove joystick panning while asleep. The joystick is hidden while asleep. Keep the **Recenter** button.
- Optional: pinch to zoom between 5 and 11 tiles of vertical view.
- Done when: asleep, a player can swipe across the whole hotel and tap Recenter to return.

## Phase 2: movement that never sticks (about 1–2 days)

- **Circle collider instead of a box:** replace `CanStand` and `Slide` with circle-versus-tile collision that slides along walls (project the move onto the wall tangent). Moving diagonally into a wall keeps the sliding component.
- **Corner and doorway assist:** if the move is blocked and an open tile is within 0.45 tiles sideways, nudge toward its centre line. This makes 1-tile doorways easy to enter with a joystick.
- **A\* with path smoothing:** swap the BFS in `Pathfinding.cs` for A\* and add string-pulling (skip waypoints when there is a clear line). Bots walk straight lines instead of zig-zagging between tile centres.
- **Valid goals only:** when the goal is a closed door, path to `DoorOutside` and stop there; never path into a blocked tile.
- **Real stuck recovery:** if the bot moves less than 0.1 tiles in 0.6 s, mark the next waypoint blocked for 3 s, replan, and if still stuck, `Unstick` to the nearest standable tile.
- **Soft separation:** residents and the monster push apart slightly (no hard blocking), so a crowd in a corridor never jams.
- Tests: a new EditMode test runs 300 random bot routes on 20 seeds at 8× speed. It fails if any agent stays still for more than 1 s while it has a path.

## Phase 3: rooms with at least 24 spaces, not letters (about 1–2 days)

- Remove `LetterShapes` from room generation (keep the class only for old tests or delete it with its tests). Remove `letters` from `map.json`.
- New "floor plan" generator in `HotelMap.cs`. Start from a rectangle (interior 6×5 to 9×7), then apply 1–2 random features:
  - an alcove or bay window on one wall (2–3 tiles deep)
  - a closet notch cut into a corner
  - a short L extension (at least 3 tiles wide)
  - one pillar or a chamfered (cut) corner
- Rules checked after generation: at least **24 build spaces** (floor tiles minus bed, door-inside tile and the reserved bed-to-door path); no arm narrower than 2 tiles; area / bounding box ≥ 0.6, so the room reads as a room and not a shape; the bed fits along a wall.
- Config in `map.json`: `roomMinBuildTiles: 24`, `roomInteriorMin`, `roomInteriorMax`, `roomFeatureChance`. Replace `buildTileDensity`/`buildTilesMax`, because all floor tiles stay buildable.
- The map may need to grow from 88×56 to about 104×64 to fit 10 bigger rooms with the 7–32 tile door spacing. Check the camera clamp and fog texture size.
- Tests: on 50 seeds, every room has ≥ 24 build spaces, no 1-wide arms, and every room is reachable.

## Phase 4: the monster hides among the guests (about 2 days)

- The match has **7 guests**, all using roster characters (exactly the 7 we have). One of them is secretly the monster. A human who picks Play as Monster is that guest; otherwise a random bot is.
- Setup phase: the monster guest walks, claims a room and shuts its door like everyone else. Its UI looks like a resident's, but the build menu shows "You are hiding. Wait for lights out." Bots and the HUD treat it as a normal guest.
- Lights out (start of night 1): a transformation. The guest's sprite flashes, shakes and swaps to the monster art, with a short banner such as "Mando was the Stitchwork Chef!" The monster bursts out of its own room, which becomes its **lair**: it respawns there instead of at `MonsterSpawn`.
- Which monster type it becomes is still the monster pick (human) or random (bot), so residents also can't plan resistances in advance.
- Hide information until the reveal: the monster-intel panel shows "???", the opening announcement no longer names the monster, and the log hides it.
- Rooms: 10 rooms for 7 guests leaves 3 empty (open, no owner, can hold body parts).
- Online note: the server keeps the monster's identity secret and never sends it to resident clients before the reveal.
- Code: `StartMatch()` (create 7 residents, pick one as `HiddenMonster`); `BeginNights()` (transform, create the `Monster` from that guest's position and room); `Models.cs` (`Resident.IsMonster`, `Monster.Lair`); `GameHUD` (hide intel, monster disguise UI); `ResidentAI` (the hidden-monster bot behaves like a guest during setup).
- Done when: in 10 bot matches, nothing in the HUD or log hints at the monster before night 1, and the transformation plays at the right guest's position.

## Phase 5: monster that grows forever, and real pressure (about 3–4 days)

### 5.1 In-match monster level (unlimited)
- The monster earns match XP from: time alive at night (1/s), door damage (0.2 per point), bites (0.5 per point), body parts (25), kills (150).
- XP to the next level: `60 × 1.15^(L−1)`. There is no cap.
- Each level: +8% max HP, +7% attack and door damage, +1% speed (speed bonus capped at +30%). All values are data in a new `monster_progression.json`.
- A level-up shows a floater and a short roar. The monster bot spends upgrades automatically.

### 5.2 Skills unlock by level, evolutions by milestones
- Ability slots open at levels 1, 3 and 6, and up to 5 slots at level 12. Each slot offers a choice of 2–3 abilities from that monster's pool (data, not code).
- **Evolutions** at levels 5, 10, 15 and 20: choose 1 of 2 branches per monster, each with a passive and a stronger ability. Examples:
  - Stitchwork Chef: **Butcher** (door smasher: +60% door damage, Cleaver Throw) or **Glutton** (eats parts 2× faster, heals on eat).
  - Moldy Matron: **Spore Mother** (slowing area spores) or **Rot Queen** (damage over time to towers).
  - Bellhop Wraith: **Phantom** (walks through one closed door per night) or **Poltergeist** (throws furniture, stuns towers).
- **Infinite tail:** after level 20, every 5 levels gives an **Ascension**, a random choice of 3 perks from a generic pool (+HP, +speed, shorter cooldowns, tower-jam aura, bigger reveal radius…). Perks stack, so there is always another level to reach.
- Art: each evolution swaps to a new Autosprite form (idle, run, attack in 8 directions). 3 monsters × 2 forms × 3 packs × 15 credits = 270 credits. Until the art exists, use a tint plus 10% scale-up.
- Account level (meta): remove the 50 cap from `levelCurve`, use the same open-ended formula, and use the account level to unlock which abilities and branches appear in the in-match pool.

### 5.3 Smarter monster AI
- Replace the "would die first" +30 penalty with a **risk tolerance** that rises over the night and with every minute without a kill.
- **Night goals:** the bot must commit to at least one door assault per night. It picks the target by weakest door, fewest weapons in range and isolation (empty rooms nearby), not only path cost.
- **Hunt cycle:** eat parts and level up early in the night, then commit. It retreats to the lair to heal when below 30% HP and comes back.
- It uses abilities at the door (Rampage, Jam) and Lunge for chasing in the hallway. It ignores towers that can't reach its standing spot (it attacks from the side of the door that is out of range).
- It wakes the residents it attacks (already done) and switches targets if a door breaks elsewhere.

### 5.4 Pressure that grows with the night
- **Night escalation:** each night the monster gains +1 free level and the time between auto-spawned body parts drops.
- **Hunger meter:** if the monster hasn't damaged a resident or a door for 45 s at night, it enters Frenzy (+30% speed and door damage until it hits something).
- **Diminishing resident income:** bed and faith income above about 8/s per room is reduced by 25%, so turtling 6 rooms can't outscale the monster indefinitely.
- **Endless mode:** `match.json` `nightCount: 0` means unlimited nights; the score is nights survived. The standard mode stays at 6 nights.

### 5.5 Balance by simulation, not feel
- Add a headless bot-match runner (EditMode test or editor menu: Bad Apple > Simulate 50 matches) at 16× speed. It reports: attacks per night, first attack time, door breaks, kills, monster level per night and resident win rate.
- Targets: the first door assault in night 1 before 45 s; at least 1 assault every night; residents win 40–55% of 6-night matches; in endless mode the median run ends between nights 8 and 12.
- Tune JSON only, until the targets hold on 3 runs of 50 matches.

## Order of work

1. Phase 1 (speed, auto-close, start when ready, menu layering, drag camera)
2. Phase 2 (movement)
3. Phase 3 (rooms)
4. Phase 4 (hidden monster)
5. Phase 5 (progression, AI, pressure, simulation)

Each phase ends with: EditMode tests passing, a play-test on Android at 19.5:9, and a commit to `main` with a short note in `docs/`.

## Open questions

1. During setup, should the hidden monster be able to fake-build (decoys that do nothing) so other players can't spot it by its empty room? This only matters online.
2. Should endless mode be its own menu button, or the default after night 6 if residents are still alive?
3. Is spending about 270 Autosprite credits on evolution forms OK now, or should we use tint and scale until the branches are balanced?
