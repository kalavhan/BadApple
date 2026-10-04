# Bad Apple Hotel

Working title. A 7-player online 2.5D pixel-art game: **1 Monster vs 6 Residents**, a mix of tower defense and asymmetric horror (think Dead by Daylight chases with Haunted Dorm-style room defense). Built in Unity with PixelLab art.

Seven players spend six nights in a hotel hosting a reunion of occultists. One is secretly a monster a guest conjured; the other six fortify their rooms and survive.

## Match in one minute
- 60 s room pick, then 6 nights of 90 s each. Residents win by surviving all six nights; the Monster wins by killing everyone first.
- **Dream Power** comes from the bed (5 levels: paper, cardboard box, pile of clothes, basic bed, comfy bed). **Faith** (shown as the smiling Bad Apple) comes from upgradeable faith towers.
- Door upgrades are blocked if the new door level would be more than **4 levels above your lowest weapon**; the lowest weapon blinks so you know what to upgrade.
- Every match generates a new hotel: rooms grow from deformed letters (L, T, U, E, O...), some sit in clusters and some alone. Lonely rooms get a free random building. Inside each room, bolted floor plates mark where buildings attach; bigger rooms get more plates. Such is luck.
- Residents walk (joystick) to a free room, shut the door, and sleep in bed for Dream Power. Awake, you earn nothing but your weapons hit x1.1.
- Towers: gun, missile, electric, dragon statue, slow totem (short / mid / long range, auto-fire when the Monster is in range), faith tower, and the crystal ball (see the whole hotel). Sell any building for 50% back.
- Fog of war at night: you only see your room and a little outside it, unless you own a crystal ball.
- The Monster hunts body parts (arm, leg, torso, eye) or players, upgrades resistances to bullet/electric/fire/slow, and equips 3 abilities with cooldowns (Jam, Rampage, Blackout to start).
- Monster kills a Resident: takes 90% of their resources and XP value. Residents kill the Monster: the killer gets 120% of its resources; the Monster respawns.
- Account XP: Monster per Resident killed, Resident per night survived. Separate Monster and Resident levels.

## Balance lives in JSON
Every number is in `Assets/StreamingAssets/Config/` (`match`, `economy`, `beds`, `doors`, `towers`, `monsters`, `bodyparts`, `abilities`, `map`, `residents`). All values are placeholders to tune by playtesting. `ConfigLoader` loads and validates them at startup.

## Play the local demo (milestone 1)
1. Clone this repo, then in Unity Hub choose **Add > Add project from disk** and pick the folder. It targets **Unity 6000.3.24f1**.
2. On first open, Unity creates `Assets/Scenes/Main.unity` and adds it to the build (menu: *Bad Apple Hotel > Run Project Setup*). Open it and press **Play**. The game also boots in any empty scene.
3. Pick a monster, then **Play as Resident**, **Play as Monster**, or **Random role**. Every other seat is a bot.

**Resident:** drag on the left half of the screen (or WASD) to walk. During the 60 s setup, walk into any free room (its door is open) to claim it. The big round **action button** (or E / Space) does what fits where you stand: close or open the door, sleep in bed, wake up. Tap a bolted plate to build (rings show short / mid / long range), tap a building to upgrade or sell it, tap the bed or door to upgrade them. A door upgrade is blocked if it would be more than 4 levels above your weakest weapon, and that weapon blinks. Leave your door open at night and the monster just walks in; step into the hallway and it can bite you there. With a crystal ball, **Hotel view** (or M) zooms out over the whole hotel.

**Monster:** WASD / arrows or the on-screen joystick to move. Stand next to a shut door to smash it (open doors you just walk through), then eat whoever is inside. Stand on body parts to eat them (arm = damage, leg = speed, torso = health, eye = spot parts). Abilities on 1 / 2 / 3 (Jam, Rampage, Blackout). Buy resistances in the left panel.

Use the 1x / 2x / 4x buttons to fast-forward. Results award Monster or Resident XP (saved locally).

Placeholder art is drawn in code from the bible palette (`Assets/Scripts/Game/Art/Sprites.cs`), so nothing needs importing yet.

## Code
- `Assets/StreamingAssets/Config/*.json`: every balance number.
- `Assets/Scripts/Config/`: models and loader (Unity `JsonUtility`).
- `Assets/Scripts/Rules/`: pure rules (door gap, 90% / 120% rewards, XP curve).
- `Assets/Scripts/Game/`: the demo. `Core/GameManager.cs` runs the match, `AI/` holds the resident and monster bots, `UI/GameHUD.cs` is the touch HUD (multi-touch joystick, action button, tap popups), `World/HotelMap.cs` generates the letter-shaped rooms.
- `Assets/Tests/EditMode/`: NUnit tests for the rules and the map generator (60 seeds), via Window > General > Test Runner.
- `Packages/manifest.json` includes the MCP for Unity bridge so Claude can read the console and run tests in your editor.

## Art direction
See [`docs/art-style.md`](docs/art-style.md). Dark and funny: Tim Burton crookedness, Don't Starve charm, a 9-color palette where red is reserved for Faith.

The full game bible (match flow, systems, research, assumptions, roadmap) is kept as a doc and can be exported into `docs/`.

## Art

All sprites are generated in Autosprite (comic style, dark Tim Burton look) and live in `Assets/Resources/Art/`
(beds 1-6, 7 towers, doors by tier, floor/wall tiles, build plate, residents in 6 pajama colors, 3 monsters, ghost, body parts).
`Sprites.cs` loads a file by name and sizes it in world units; if a file is missing it falls back to the placeholder
pixel art drawn in code, so any sprite can be swapped by replacing its PNG. `Assets/Editor/ArtImporter.cs` sets the
import options. `tools/art/process_sprites.py` is the script that cut the sprites out of the raw generations.
Raw 1024px images stay in `ArtSource/` (not committed).
Switch art on/off with the menu **Bad Apple Hotel > Use Autosprite Art**. Full details in [docs/SPRITES.md](docs/SPRITES.md).
