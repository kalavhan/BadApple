# 2.5D playtest — October 4, 2026

This update extends PR #2 with the subsequent Android startup report, exact room budgets, two-square beds, directional aiming, and the requested 2.5D world. The earlier playtest implementation and its historical results are in [PLAYTEST-RESULTS-OCT04.md](PLAYTEST-RESULTS-OCT04.md).

## World and placement

- Ten randomly scattered irregular rooms: one with 30, three with 26, three with 23, and three with 20 **available build squares**. The two bed squares and the reserved route from the doorway to the bed are additional. Every floor cell belongs to a 2×2 patch; rooms remain connected and occupy at least 60% of their bounding rectangle.
- Beds reserve adjacent head and foot squares. All six bed tiers render inside that footprint, including rotated beds. The pillow anchor scales with the artwork; tapping either square opens the bed controls.
- Real floor and wall meshes, a fixed 45° azimuth / 50° elevation orthographic camera, upright 2D character cards, camera-relative walking, and ground-plane picking/panning. The simulation retains XY coordinates; height points toward negative Z.
- Walls are **0.14 tiles thick** and follow floor edges, with joined corners. Far-side room walls are 1.7 tiles tall; near-side walls are low cutaways. Nearby tall walls dissolve when they obscure a walking player in a hallway. The navigation grid still reserves wall cells; visual partitions no longer fill those cells.
- Seven-tile sight operates during setup, walking, and sleep. Walls and closed doors block sight, explored floor stays dim, and hidden actors stay hidden even in hotel view. A crystal ball retains its explicit full-hotel benefit. Monsters search known rooms without following an unseen guest's live position.
- Generated pixel wall texture and static eight-view sheets for all four tiers of the zombie/Pistol Revenant and Ghost Marksman. No tower animation, AutoSprite call, or Tripo model generation. [Saved assets and exact imagegen prompts](art/2.5D-textures.md).

## Range choices

Short-range weapons gain 35% damage within two tiles. Mid-range weapons have no minimum distance and cover sustained fire/control. Long-range weapons cannot fire inside two tiles and gain 30% damage at six tiles or more. Both combat and the monster's threat calculation apply these rules. Build descriptions, range ellipses, and the red inner boundary explain the tradeoffs.

Prices cross range classes: Pistol Revenant costs 30 Dream Power, Skull Mortar 45 Dream Power, Static Doll 65 Faith, and Ghost Marksman 95 Dream Power. Ghost damage was increased with its price. Monster health was retuned; Endless applies an inverse multiplier to retain its longer-run health curve.

## Android startup diagnosis

An Android 11 x86_64 emulator reproduced a missing menu over the complete map. The log showed `Could not produce class with ID 115`, a built-in skin serialization mismatch, and repeated `NullReferenceException` in `UnityEngine.GUI.DoSetSkin` / `GUIUtility.BeginGUI`, before the game's own HUD drawing code. [Failure log](qa/oct04-isometric/android-before-fix.log).

Native engine stripping is disabled in the project settings and the reproducible Android build method. Unity documents that this setting removes engine modules/classes and can be disabled when it causes loading problems: [PlayerSettings.stripEngineCode](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/PlayerSettings-stripEngineCode.html). The camera now initializes before asynchronous configuration loading, and HUD style initialization is only marked complete after it succeeds.

The exact rebuilt release APK was installed over the earlier build and launched normally, without command-line graphics overrides. Its menu renders and starts a resident match with visible HUD/joystick and 3D geometry. On-screen joystick and speed-button input was exercised, and the match reached night 3 and spectator mode after the resident was eaten. The startup and match logs contain no Unity errors or exceptions. [Android menu](qa/oct04-isometric/android-menu.png), [resident match](qa/oct04-isometric/android-match.png), [night 3](qa/oct04-isometric/android-night3.png), [startup log](qa/oct04-isometric/android-launch.log), [match log](qa/oct04-isometric/android-match.log). This reproduces and fixes the missing-menu exception in the emulator; confirmation on the reported phone remains pending.

A separate Vulkan graphics-initialization crash occurred in the software emulator. The test APK therefore uses OpenGL ES 3. The build is a release player with no Development Build overlay, signed with the development keystore for local testing.

## Verification

Unity 6000.3.24f1: **38/38 tests passed**. Coverage includes exact room budgets and all six bed footprints over 100 seeds, 300 movement routes over 20 seeds, closed-door sight during walking and hotel view, camera projection/picking, 32 distinct aiming sprites per tower family, and actual range-rule boundaries. [Test results](qa/oct04-isometric/editmode-results.xml).

Visual checks: [19.5:9 bed popup and thin walls](qa/oct04-isometric/sleep-popup-1959.png), [4:3 bed popup](qa/oct04-isometric/sleep-popup-43.png), [limited-sight hotel overview](qa/oct04-isometric/hotel-1959.png), [monster disguise](qa/oct04-isometric/disguise-1959.png), and [progression](qa/oct04-isometric/progression-1959.png).

| Batch seed | Standard matches | Resident wins | Latest first attack | Endless matches | Median last night |
|---|---:|---:|---:|---:|---:|
| 20261004 | 50 | 58% | 34.30 s | 50 | 10.5 |
| 20261104 | 50 | 56% | 25.80 s | 50 | 11 |
| 20261204 | 50 | 46% | 25.27 s | 50 | 9.5 |

Overall standard resident win rate is **53.3%**. Two individual batches slightly exceed the earlier 55% target; no standard run reached the simulation cutoff. Five played standard nights across two matches had no damaging attack. Endless had 20 such nights, and six runs were still alive at the 30-night cutoff. These are reported as censored, not completed wins. First-attack timing and all three Endless medians meet the plan's ranges. The new sight and range mechanics still need human balance feedback; the earlier report's stronger per-night/batch claims do not describe this version.

Raw simulations: [batch 1](qa/oct04-isometric/balance-1.json), [batch 2](qa/oct04-isometric/balance-2.json), [batch 3](qa/oct04-isometric/balance-3.json). Reproduce with the same `BotSimulation.Run` environment variables documented in the earlier report.

## APK

`Builds/BadAppleHotel-1.2-isometric.apk`, package `com.kalavhan.badapplehotel`, version **1.2-isometric**, code **3**, minimum API **25**, ARM64 + x86_64 IL2CPP. APK v2 signature verification passed. Size: 70,468,817 bytes.

SHA-256: `b985e7bb98f15313e01c99d7c6c905e880cc632f37ea1836c00b8b844351c6ed`.

Physical ARM64 phone performance, audio, and touch feel remain device-playtest checks; software-emulator results cannot establish those properties.
