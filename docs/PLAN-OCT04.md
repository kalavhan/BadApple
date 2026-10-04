# October 4 hotel update

Implemented plan items 1–8. Tower animations (item 9) are deliberately deferred.
The hotel remains **10 rooms for 6 residents**, leaving **4 unclaimed rooms** after setup, per Josue's correction.
Future effects, audio, online play, and broader mobile optimization remain separate work.

## Gameplay and layout

- A seeded, connected corridor network replaces the cross layout. Offset spine nodes, room branches,
  extra loop routes, and dead ends route around reserved room plots. Existing deformed letter rooms remain.
- `map.json` now uses an 88 × 56 map. Nearest doorway distances must be 7–32 Manhattan tiles;
  invalid placements retry deterministically. `loopCount` and `deadEndCount` control side routes.
- Unclaimed rooms have open doors, no owner, no bed, and no towers; the monster can enter them.
- Body parts alternate between dead ends and unclaimed room floors when spacing permits.
  `bodyparts.json.minSpacingTiles` enforces **12 tiles of Euclidean separation** for every pair.
- Lonely-room bonus buildings now pass the same door-to-bed path check as player buildings.

## Beds and camera

- Each bed level declares `sleepAnchor` (pillow offset in world units) and `sleepRotation` (relative to bed art).
- Bed orientation follows its room entrance. A frozen front-facing idle frame lies along the bed's axis;
  the character's head, rather than its feet pivot, is positioned on the pillow. No sleep animation pack was generated.
- Bed art is 1.85 tiles long so tall residents fit, while character scale remains unchanged.
- The sleeper glides into place and renders above the bed. Animation and the glide update once per rendered frame,
  independently of simulation substeps.
- Sleeping joystick/WASD input pans the camera at 14 tiles/second in unscaled time. It does not wake or move the resident.
  **Wake** (E/Space) ends sleep. **Recenter** (R) eases back to the room. The viewport stays inside map bounds.
- Sleeping makes unexplored terrain dimly readable; hidden residents, monsters, and pickups remain hidden.
  The crystal ball still reveals the hotel and unlocks the full hotel overview at night.

## Controls and static art

- Fixed bottom-left joystick: thin ring, small knob, 35% resting opacity, brighter while held.
  Action buttons use matching translucent iron rings and rivets. Touches on the joystick cannot select world tiles.
- Hallway art uses a separate floor, wall cap, wall face, corner, baseboard, doorway frame, portraits,
  candles, cracks, and cobwebs. Position hashes choose variants without affecting simulation randomness.
- All ten upgradeable tower families have **four named static designs**, with equal per-family rendering size.
  The crystal ball remains a single-level utility item.
- Tier data owns sprite, name, damage, fire rate, range, effects, income, and upgrade cost.
  The upgrade menu previews the next design. Combat, income, range previews, and bot threat estimates read the same data.
- Four tiers represent the former power levels 1, 4, 7, 10. Each upgrade costs the combined price of the three old steps.
  `doorSupportLevel` preserves the existing door progression and gap rule. Sell refunds use actual tier costs.
  This preserves end-tier strength and total spend; the larger individual purchases still need balance playtesting.

## Asset pipeline and credit ledger

Static source sheets: `ArtSource/PlanOct04/` (locally retained, gitignored).
Shipped PNGs: `Assets/Resources/Art/Tiers/` and `Assets/Resources/Art/Hotel/`.
Prompts and asset IDs: `tools/art/oct04-static-manifest.json`.

Run `python3 tools/art/prepare_static_tiers.py` to slice local 2×2 design sheets, remove edge-connected white backgrounds,
fit each static design to a 256 px canvas, and generate a review sheet. It makes no API calls and uses no credits.
Unity's existing art importer supplies texture settings; runtime sprites retain the placeholder fallback.

Starting balance read once: **694**. Fourteen static generations: **14 credits**.
One gun-sheet background removal: **1 credit**. Total used: **15**. Estimated remaining: **679**.
No tower idle, attack, animation, or spritesheet-animation jobs were requested.

## Android test build

`Assets/Editor/AndroidBuild.cs` provides the `BadAppleHotel.EditorTools.AndroidBuild.BuildTestApk` batch entry point.
It creates an ARM64 IL2CPP development APK, debug-signed, package `com.kalavhan.badapplehotel`, minimum Android 7.1/API 25.
Set `BADAPPLE_APK_PATH` to choose the output (default `Builds/BadAppleHotel-test.apk`).
The player asynchronously reads the JSON configs through UnityWebRequest on Android because StreamingAssets lives inside the APK.
A loading message is shown until config is ready. Builds and APK files remain gitignored.

## Verification

**24 Unity tests passed**, including the live match test. The ARM64 APK built successfully and its v2 signature verifies.
Its packaged balance data contains 10 rooms and 40 static tower tiers.

The tests cover 60 seeded hotel layouts and hallway reachability; three distinct sample layouts;
210 nightly pickup sets with minimum separation and both room/dead-end hiding places;
all seven character head anchors on six bed levels in four orientations; camera bounds;
tier assets and stats; and a live sleep/pan/recenter/wake/build/upgrade/night-start sequence.

Layout and bed visual tests write `/tmp/badapple-three-seeds.png` and `/tmp/badapple-bed-review.png`.
The interactive editor capture checks the controls at 1280 × 720. Reviewed images are retained in `docs/qa/oct04/`. Run visual checks with graphics enabled (for example `xvfb-run -a Unity -batchmode`),
and use Unity's EditMode test runner for the full suite. A physical-phone touch/performance check remains outstanding.
