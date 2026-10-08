# Bad Apple Hotel

Working title. A local prototype of a 7-player 2.5D game: **1 Monster vs 6 Residents**, a mix of tower defense and asymmetric horror (think Dead by Daylight chases with Haunted Dorm-style room defense). Built in Unity with 3D hotel geometry, a fixed isometric camera, and eight-direction 2D characters. Online play is still planned.

Seven players spend six nights in a hotel hosting a reunion of occultists. One is secretly a monster a guest conjured; the other six fortify their rooms and survive.

## Match in one minute

- Up to 60 s to pick a room; once all seven guests have rooms, lights out follows in 3 s. Standard has 6 nights of 90 s each. Residents win by surviving all six nights; the Monster wins by killing everyone first.
- **Dream Power** comes from the bed (6 upgrade levels). **Faith** (shown as the smiling Bad Apple) comes from upgradeable faith towers.
- Door upgrades are blocked if the new door level would be more than **4 levels above your lowest weapon**; the lowest weapon blinks so you know what to upgrade.
- Every match scatters 10 irregular rooms along crooked corridors: one with 30 build spaces, three with 26, three with 23, and three with 20. Two bed squares and a permanently clear access path are additional to those counts. Lonely rooms get a free random building. Seven guests claim rooms, leaving three empty.
- Residents walk (joystick) to a free room. Sleep walks to the bed and shuts the door once the doorway clears. Awake, you earn nothing but your weapons hit x1.1.
- Towers: gun, missile, electric, dragon statue, slow totem (short / mid / long range, auto-fire when the Monster is in range), faith tower, and the crystal ball (see the whole hotel). Sell any building for 50% back.
- Seven-tile sight while walking and sleeping, including setup. Walls and shut doors block sight; explored floor stays dimly remembered, and a crystal ball reveals the hotel. Nearby walls fade when they obstruct your view in a hallway.
- The Monster hunts body parts (arm, leg, torso, eye) or players, upgrades resistances to bullet/electric/fire/slow, and chooses up to 5 abilities as match levels increase. Evolutions unlock at levels 5/10/15/20; ascensions continue every 5 levels afterward.
- Monster kills a Resident: takes 90% of their resources and earns match XP. Residents kill the Monster: the killer gets 120% of its resources; the Monster respawns.
- Account XP: Monster per Resident killed, Resident per night survived. Separate Monster and Resident levels.

## Balance lives in JSON
Balance data lives in `Assets/StreamingAssets/Config/` (`match`, `economy`, `beds`, `doors`, `towers`, `monsters`, `bodyparts`, `abilities`, `map`, `residents`, `monster_progression`). The bot simulation reports and phone playtests guide tuning. `ConfigLoader` loads and validates them at startup.

## Play the local demo (milestone 1)

1. Clone this repo, then in Unity Hub choose **Add > Add project from disk** and pick the folder. It targets **Unity 6000.3.24f1**.
2. On first open, Unity creates `Assets/Scenes/Main.unity` and adds it to the build (menu: *Bad Apple Hotel > Run Project Setup*). Open it and press **Play**. The game also boots in any empty scene.
3. Pick a monster, then **Play as Resident**, **Play as Monster**, or **Random role**. **Endless** opens its own role choice and continues beyond six nights. Every other seat is a bot.

**Resident:** use the fixed bottom-left joystick or WASD to walk into a free room. Press **Sleep** (E / Space) anywhere inside your room to walk to bed and close the door safely. The same button wakes you. A separate small button opens or closes the door when you are near it. Sleeping hides the joystick; drag the world to pan, and use **Recenter** / R to return. Dragging also works while awake or spectating. Tap the floor to build, a tower to upgrade or sell, or the bed/door to upgrade. Hotel view keeps your current sight limits; a crystal ball reveals the whole hotel.

**Monster:** during setup, you look and move like an ordinary guest. Claim a room and sleep; harmless decoy furniture appears automatically. At lights out you transform in that room, which becomes your respawn lair. Use WASD / arrows or the joystick to hunt. Stand beside a shut door to smash it, or near a resident to bite. Stand on body parts to eat them. Choose skills when slots open, then use their touch buttons or keys 1–5. Evolution choices add a passive and an ability; the left panel buys resistances.

Use the 1x / 2x / 4x buttons to fast-forward. Results award Monster or Resident XP (saved locally).

Art loads from `Assets/Resources/Art/`, with code-drawn placeholders as a fallback.

## Code

- `Assets/StreamingAssets/Config/*.json`: every balance number.
- `Assets/Scripts/Config/`: models and loader (Unity `JsonUtility`).
- `Assets/Scripts/Rules/`: pure rules (door gap, 90% / 120% rewards, XP curve).
- `Assets/Scripts/Game/`: the demo. `Core/GameManager.cs` runs the match, `AI/` holds the resident and monster bots, `UI/GameHUD.cs` is the touch HUD (multi-touch joystick, action button, tap popups), `World/HotelMap.cs` generates the natural guest rooms.
- `Assets/Tests/EditMode/`: 38 checks, including 300 movement routes, exact room/bed footprints over 100 seeds, sight blocking, and hidden-monster matches. Run via Window > General > Test Runner.
- `Assets/Editor/BotSimulation.cs`: **Bad Apple > Simulate 50 matches**, using the same match step as the player.
- `Packages/manifest.json` includes the MCP for Unity bridge so Claude can read the console and run tests in your editor.

## Art direction
See [`docs/art-style.md`](docs/art-style.md). Dark and funny: Tim Burton crookedness, Don't Starve charm, a 9-color palette where red is reserved for Faith.

The full game bible (match flow, systems, research, assumptions, roadmap) is kept as a doc and can be exported into `docs/`.

## Art

The original sprites were generated in Autosprite (comic style, dark Tim Burton look) and live in `Assets/Resources/Art/`
(beds 1-6, 7 towers, doors by tier, floor/wall tiles, build plate, residents in 6 pajama colors, 3 monsters, ghost, body parts).
`Sprites.cs` loads a file by name and sizes it in world units; if a file is missing it falls back to the placeholder
pixel art drawn in code, so any sprite can be swapped by replacing its PNG. `Assets/Editor/ArtImporter.cs` sets the
import options. `tools/art/process_sprites.py` is the script that cut the sprites out of the raw generations.
Raw 1024px images stay in `ArtSource/` (not committed).
Switch art on/off with the menu **Bad Apple Hotel > Use Autosprite Art**. Full details in [docs/SPRITES.md](docs/SPRITES.md).


## October 4 playtest update

The hotel keeps **10 rooms**, with three empty rooms after all seven guests claim theirs. One guest becomes the monster at lights out. Tower upgrades stay static. The zombie and Ghost Marksman use eight aiming views per tier, made with imagegen alongside the wall texture; no AutoSprite credits were spent.

The earlier Android test build was `Builds/BadAppleHotel-playtest-fixes.apk` (version `1.1-playtest`, code 2). See [the playtest report](docs/PLAYTEST-RESULTS-OCT04.md) for validation, balance batches, screenshots, and remaining device checks. The earlier layout/art work is recorded in [the original implementation notes](docs/PLAN-OCT04.md).

### 2.5D playtest update

The 3D world uses the original XY tile coordinates; negative Z is height. `HotelView3D` owns the fixed 45-degree azimuth, camera projection, screen-relative controls and upright sprite cards. `GameManager.Scene3D` batches floors into meshes and builds textured partitions only 0.14 tiles thick. Far-side walls are tall; near-side walls stay low so furniture is usable. Hallway walls fade where they obscure the player. Future Tripo models can attach at tile centers with their vertical axis mapped to negative Z.

Short-range weapons deal +35% damage within two tiles. Mid-range weapons fire at any distance within reach, with affordable sustained damage and premium control options. Long-range weapons have a two-tile blind spot and +30% damage beyond six tiles. Build descriptions and range previews show these tradeoffs.

Art prompts and sources: [2.5D textures and directional sheets](docs/art/2.5D-textures.md).

The latest APK is `Builds/BadAppleHotel-1.2-isometric.apk` (version code 3), with ARM64 and x86_64 in one package. See [the 2.5D validation report](docs/ISOMETRIC-PLAYTEST-OCT04.md) for Android startup diagnostics and current test/balance results.

### Dream summons (October 7)

Towers now read as creatures a sleeping guest dreams into being, in the same mint-and-violet language as the claimed-room floor seams. A dream wisp leaves the owner's bed and the creature materializes from the floor up over a summoning sigil whose pips show its level. Idle creatures breathe, shed rising motes and stay tied to the bed by a faint dream thread that brightens while the owner sleeps. Every damage type has its own attack and impact (spectral bolts, a sniper beam, arcing missiles, forked lightning, fireballs, a slowing hex), and muzzle and impact flashes briefly light nearby floors, walls and characters. Upgrades evolve the creature in a burst; selling it lets it dissolve back into the dream. Dream Power and Faith creatures send their harvest to the owner as drifting motes.

All of it lives in one additive mesh (`DreamFx`, one draw call) driven by `GameManager.TowerFx`, with at most six shader lights and no `Light` components. It is presentation only: the bot simulation skips it, and it uses its own random source so gameplay rolls are unchanged. `-executeMethod BadAppleHotel.EditorTools.TowerFxCapture.Run` in a batch editor records the whole sequence frame by frame (`BADAPPLE_TOWER_FX_OUT` picks the folder). Test build: `Builds/BadAppleHotel-animated-towers.apk` (version `1.21-animated-towers`, code 22).

Animated tower art lives in `Assets/Resources/Art/TowerAnim/{id}_{level}/` and replaces the static tier sprite whenever a folder exists. Props get a single-view idle loop (AutoSprite `animate_asset`). Characters get an eight-direction idle and attack (AutoSprite `generate_isometric_pack`), turn to face the monster, and release their shot on the attack clip's `release` time. `tools/art/tower_anim.py` turns AutoSprite exports into these folders, cropping every frame to one shared cell with the feet on the pivot. The pilot covers the level 1 zombie soldier (`gun_turret_1`) and hourglass (`slow_totem_1`).
