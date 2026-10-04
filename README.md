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

## Code
- `Assets/Scripts/Config/` models and loader (Unity `JsonUtility`).
- `Assets/Scripts/Rules/` pure rules: the door/weapon gap rule and the 90%/120% rewards and XP curve.
- `Assets/Tests/EditMode/` NUnit tests for those rules (run in Unity's Test Runner).

## Setup
This repo holds Assets only. In Unity Hub create a **2D (URP)** project in this folder (or copy `Assets/` into one), add the Pixel Perfect Camera, set 16 pixels per unit, and open Window > General > Test Runner to run the EditMode tests.

## Art direction
See [`docs/art-style.md`](docs/art-style.md). Dark and funny: Tim Burton crookedness, Don't Starve charm, a 9-color palette where red is reserved for Faith.

The full game bible (match flow, systems, research, assumptions, roadmap) is kept as a doc and can be exported into `docs/`.
