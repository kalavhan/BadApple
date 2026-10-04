# Bad Apple Hotel

Working title. A local prototype of a 7-player 2.5D game: **1 Monster vs 6 Residents**, a mix of tower defense and asymmetric horror (think Dead by Daylight chases with Haunted Dorm-style room defense). Built in Unity with AutoSprite comic art. Online play is still planned.

Seven players spend six nights in a hotel hosting a reunion of occultists. One is secretly a monster a guest conjured; the other six fortify their rooms and survive.

## Match in one minute

- Up to 60 s to pick a room; once all seven guests have rooms, lights out follows in 3 s. Standard has 6 nights of 90 s each. Residents win by surviving all six nights; the Monster wins by killing everyone first.
- **Dream Power** comes from the bed (6 upgrade levels). **Faith** (shown as the smiling Bad Apple) comes from upgradeable faith towers.
- Door upgrades are blocked if the new door level would be more than **4 levels above your lowest weapon**; the lowest weapon blinks so you know what to upgrade.
- Every match generates 10 rooms with rectangular interiors and small recesses, linked by crooked corridors. Each room has at least 24 build spaces outside its bed path. Lonely rooms get a free random building. Seven guests claim rooms, leaving three empty.
- Residents walk (joystick) to a free room. Sleep walks to the bed and shuts the door once the doorway clears. Awake, you earn nothing but your weapons hit x1.1.
- Towers: gun, missile, electric, dragon statue, slow totem (short / mid / long range, auto-fire when the Monster is in range), faith tower, and the crystal ball (see the whole hotel). Sell any building for 50% back.
- Fog of war at night: you only see your room and a little outside it, unless you own a crystal ball.
- The Monster hunts body parts (arm, leg, torso, eye) or players, upgrades resistances to bullet/electric/fire/slow, and chooses up to 5 abilities as match levels increase. Evolutions unlock at levels 5/10/15/20; ascensions continue every 5 levels afterward.
- Monster kills a Resident: takes 90% of their resources and earns match XP. Residents kill the Monster: the killer gets 120% of its resources; the Monster respawns.
- Account XP: Monster per Resident killed, Resident per night survived. Separate Monster and Resident levels.

## Balance lives in JSON
Balance data lives in `Assets/StreamingAssets/Config/` (`match`, `economy`, `beds`, `doors`, `towers`, `monsters`, `bodyparts`, `abilities`, `map`, `residents`, `monster_progression`). The bot simulation reports and phone playtests guide tuning. `ConfigLoader` loads and validates them at startup.

## Play the local demo (milestone 1)

1. Clone this repo, then in Unity Hub choose **Add > Add project from disk** and pick the folder. It targets **Unity 6000.3.24f1**.
2. On first open, Unity creates `Assets/Scenes/Main.unity` and adds it to the build (menu: *Bad Apple Hotel > Run Project Setup*). Open it and press **Play**. The game also boots in any empty scene.
3. Pick a monster, then **Play as Resident**, **Play as Monster**, or **Random role**. **Endless** opens its own role choice and continues beyond six nights. Every other seat is a bot.

**Resident:** use the fixed bottom-left joystick or WASD to walk into a free room. Press **Sleep** (E / Space) anywhere inside your room to walk to bed and close the door safely. The same button wakes you. A separate small button opens or closes the door when you are near it. Sleeping hides the joystick; drag the world to pan, and use **Recenter** / R to return. Dragging also works while awake or spectating. Tap the floor to build, a tower to upgrade or sell, or the bed/door to upgrade. Crystal balls enable the whole-hotel view at night.

**Monster:** during setup, you look and move like an ordinary guest. Claim a room and sleep; harmless decoy furniture appears automatically. At lights out you transform in that room, which becomes your respawn lair. Use WASD / arrows or the joystick to hunt. Stand beside a shut door to smash it, or near a resident to bite. Stand on body parts to eat them. Choose skills when slots open, then use their touch buttons or keys 1–5. Evolution choices add a passive and an ability; the left panel buys resistances.

Use the 1x / 2x / 4x buttons to fast-forward. Results award Monster or Resident XP (saved locally).

Art loads from `Assets/Resources/Art/`, with code-drawn placeholders as a fallback.

## Code

- `Assets/StreamingAssets/Config/*.json`: every balance number.
- `Assets/Scripts/Config/`: models and loader (Unity `JsonUtility`).
- `Assets/Scripts/Rules/`: pure rules (door gap, 90% / 120% rewards, XP curve).
- `Assets/Scripts/Game/`: the demo. `Core/GameManager.cs` runs the match, `AI/` holds the resident and monster bots, `UI/GameHUD.cs` is the touch HUD (multi-touch joystick, action button, tap popups), `World/HotelMap.cs` generates the natural guest rooms.
- `Assets/Tests/EditMode/`: 32 NUnit checks, including 300 movement routes over 20 seeds and hidden-monster matches. Run via Window > General > Test Runner.
- `Assets/Editor/BotSimulation.cs`: **Bad Apple > Simulate 50 matches**, using the same match step as the player.
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


## October 4 playtest update

The hotel keeps **10 rooms**, with three empty rooms after all seven guests claim theirs. One guest becomes the monster at lights out. Tower upgrades stay static; no new AutoSprite credits were spent on these fixes.

The updated Android test build is `Builds/BadAppleHotel-playtest-fixes.apk` (version `1.1-playtest`, code 2). See [the playtest report](docs/PLAYTEST-RESULTS-OCT04.md) for validation, balance batches, screenshots, and remaining device checks. The earlier layout/art work is recorded in [the original implementation notes](docs/PLAN-OCT04.md).
