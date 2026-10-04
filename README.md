# Bad Apple Hotel

Working title. A 7-player online 2.5D pixel-art game: **1 Monster vs 6 Residents**, a mix of tower defense and asymmetric horror (think Dead by Daylight chases with Haunted Dorm-style room defense). Built in Unity with PixelLab art.

Seven players spend six nights in a hotel hosting a reunion of occultists. One is secretly a monster a guest conjured; the other six fortify their rooms and survive.

## Match in one minute
- 60 s room pick, then 6 nights of 90 s each. Residents win by surviving all six nights; the Monster wins by killing everyone first.
- **Dream Power** comes from the bed (5 levels: paper, cardboard box, pile of clothes, basic bed, comfy bed). **Faith** (shown as the smiling Bad Apple) comes from upgradeable faith towers.
- Door upgrades are blocked if the new door level would be more than **4 levels above your lowest weapon**; the lowest weapon blinks so you know what to upgrade.
- Towers: gun, missile, electric, dragon statue, slow totem, faith tower. They only fire while the Monster attacks that room's door or its owner.
- The Monster hunts body parts (arm, leg, torso, eye) or players, upgrades resistances to bullet/electric/fire/slow, and equips 3 abilities with cooldowns (Jam, Rampage, Blackout to start).
- Monster kills a Resident: takes 90% of their resources and XP value. Residents kill the Monster: the killer gets 120% of its resources; the Monster respawns.
- Account XP: Monster per Resident killed, Resident per night survived. Separate Monster and Resident levels.

## Balance lives in JSON
Every number is in `Assets/StreamingAssets/Config/` (`match`, `economy`, `beds`, `doors`, `towers`, `monsters`, `bodyparts`, `abilities`). All values are placeholders to tune by playtesting. `ConfigLoader` loads and validates them at startup.

## Play the local demo (milestone 1)
1. Clone this repo, then in Unity Hub choose **Add > Add project from disk** and pick the folder. It targets **Unity 6000.3.24f1**.
2. On first open, Unity creates `Assets/Scenes/Main.unity` and adds it to the build (menu: *Bad Apple Hotel > Run Project Setup*). Open it and press **Play**. The game also boots in any empty scene.
3. Pick a monster, then **Play as Resident**, **Play as Monster**, or **Random role**. Every other seat is a bot.

**Resident:** tap a free room during the 60 s setup. Upgrade the bed (Dream Power), door and towers from the right panel. A door upgrade is blocked if it would be more than 4 levels above your weakest weapon, and that weapon blinks. Faith towers make Faith (the Bad Apple) for magic towers. Ask a neighbour for Dream Power when you are short.

**Monster:** WASD / arrows or the on-screen joystick to move. Stand next to a door to smash it, then eat whoever is inside. Stand on body parts to eat them (arm = damage, leg = speed, torso = health, eye = spot parts). Abilities on 1 / 2 / 3 (Jam, Rampage, Blackout). Buy resistances in the left panel.

Use the 1x / 2x / 4x buttons to fast-forward. Results award Monster or Resident XP (saved locally).

Placeholder art is drawn in code from the bible palette (`Assets/Scripts/Game/Art/Sprites.cs`), so nothing needs importing yet.

## Code
- `Assets/StreamingAssets/Config/*.json`: every balance number.
- `Assets/Scripts/Config/`: models and loader (Unity `JsonUtility`).
- `Assets/Scripts/Rules/`: pure rules (door gap, 90% / 120% rewards, XP curve).
- `Assets/Scripts/Game/`: the demo. `Core/GameManager.cs` runs the match, `AI/` holds the resident and monster bots, `UI/GameHUD.cs` is the IMGUI HUD, `World/HotelMap.cs` is the 10-room layout.
- `Assets/Tests/EditMode/`: NUnit tests for the rules (Window > General > Test Runner).
- `Packages/manifest.json` includes the MCP for Unity bridge so Claude can read the console and run tests in your editor.

## Art direction
See [`docs/art-style.md`](docs/art-style.md). Dark and funny: Tim Burton crookedness, Don't Starve charm, a 9-color palette where red is reserved for Faith.

The full game bible (match flow, systems, research, assumptions, roadmap) is kept as a doc and can be exported into `docs/`.
