# October 4 playtest fixes

Implementation commit: `2a15ee9`, based on `main` at `50d5162` after PR #1 was merged. The implementation follows `PLAN-OCT04-PLAYTEST.md` and keeps **10 rooms**. No AutoSprite calls or credits, new tower animations, or evolution sprite generation were used.

## Changes

- **Controls:** Sleep walks to the bed from anywhere inside the owned room and schedules door closure once the doorway clears. Bots use the same action. The near-door control is separate. Setup ends three seconds after all seven guests have claimed rooms. Camera dragging has a physical 12-pixel threshold, inertia, recentering, and awake follow recovery; sleeping hides the joystick.
- **HUD:** Logs, banners, and toasts draw before controls and selection panels. Popup placement scores right, left, above, below, and bottom candidates against HUD rectangles, clamps to the safe area, and captures touches before underlying action zones.
- **Movement:** Circle/tile contact resolution, tangent sliding, doorway alignment, valid-goal A*, radius-safe path smoothing, progress-based replanning, temporary waypoint avoidance, and gentle guest separation. Closed doors are approached from a walkable tile. Abandoned doors open so a wraith cannot trap itself after phasing in and killing a guest.
- **Rooms:** Rectangle-based interiors with small recesses and chamfers replace letter generation. Every room has at least 24 build spaces outside its bed path, at least 60% bounding-box occupancy, a wall-adjacent bed, and a connected route from the door. All normal floor remains eligible for building subject to the existing path check. The map is 104 × 64.
- **Disguise:** All seven roster guests appear during setup. One is secretly the monster; no active monster or monster-specific intel exists before lights out. Its room gains harmless decoys every 6–14 seconds. Reveal removes decoys, announces the guest, and retains its position and room as its lair. Three rooms remain empty.
- **Progression:** Match XP, unlimited levels, five skill slots at levels 1/3/6/9/12, account-gated choices, four evolution milestones, and repeatable ascensions. Evolutions use existing art with tint/scale and optional future art IDs. The level-up roar is synthesized in code. Account XP uses a versioned double-precision save with migration from the old integer key and no level-50 cap.
- **Pressure:** Hunt sprint, hunger frenzy, nightly levels, additional body-part spawns, diminishing bed/faith income, door commitments, retreat/healing, and defensive ability use. Electric stuns have a recovery window to prevent permanent immobilization.
- **Endless:** A separate menu choice, role selection, night escalation, role-specific personal best, and results showing night and monster level. Standard remains six nights.

Matron speed is **4.0**, rather than the plan's suggested 3.9, to satisfy its stricter requirement that every monster be at least 10% faster than a 3.6-speed resident. Chef is 4.4 and Wraith is 5.0. Health and door damage were tuned in JSON against the simulations below.

## Verification

Unity 6000.3.24f1: **32 tests passed**, including 300 movement routes over 20 seeds, natural-room checks over 50 seeds, 10 hidden-identity/reveal matches, blocked-door sleep, a trapped-wraith regression, unlimited account XP, and the existing economy/art/build checks. Popup safe-area checks cover 16:9, 19.5:9, and 4:3; game screens were also rendered at 19.5:9 and 4:3. See the [sleep popup](qa/oct04-playtest/sleep-popup-1959.png), [hotel layout](qa/oct04-playtest/hotel-1959.png), [disguise](qa/oct04-playtest/disguise-1959.png), and [progression choice](qa/oct04-playtest/progression-1959.png).

The editor menu **Bad Apple → Simulate 50 matches** runs the same `StepMatch` used by the player, at a fixed 30 Hz in batches of 16 ticks. It does not substitute a statistical approximation for combat. Reports distinguish all attack episodes from door assault episodes and include each match's kills, door breaks, and level at each night.

| Batch seed | Standard matches | Resident wins | Latest first door attack | Endless matches | Median final night |
|---|---:|---:|---:|---:|---:|
| 20261004 | 50 | 50% | 29.37 s | 50 | 9.5 |
| 20261104 | 50 | 44% | 25.73 s | 50 | 10 |
| 20261204 | 50 | 50% | 31.20 s | 50 | 11 |

All 150 standard matches had a damaging attack episode in every played night. The three standard win rates, all first door attacks, and all three Endless medians meet the requested ranges. Endless still has defensive outliers: 12 played nights across 150 Endless runs had no damaging attack episode, and three runs were still alive at the runner's 30-night time budget. Those runs are explicitly marked `censored`, not counted as completed wins. The median values remain observable below that cutoff. Nightly commitment is an AI goal; successful damage is not guaranteed against a sufficiently defended approach.

Raw evidence: [Unity tests](qa/oct04-playtest/editmode-results.xml), [batch 1](qa/oct04-playtest/balance-1.json), [batch 2](qa/oct04-playtest/balance-2.json), [batch 3](qa/oct04-playtest/balance-3.json).

## APK

`Builds/BadAppleHotel-playtest-fixes.apk` — version **1.1-playtest**, Android version code **2**, package `com.kalavhan.badapplehotel`, ARM64 IL2CPP development build, minimum Android API 25. APK v2 signature verification passed with the development keystore.

SHA-256: `0a34e547d7dd7365e6402fdf9ed3644190a6aaf7307068e65cf741db1edb1978`

No Android device was connected (`adb devices` was empty). Physical touch behavior, audio, and device performance still need the requested phone playtest; the desktop renders do not replace that check.

For batch reproduction, set `BADAPPLE_SIM_COUNT=50`, `BADAPPLE_SIM_SEED` to a seed above, `BADAPPLE_SIM_MODE=both`, and `BADAPPLE_SIM_PATH` to the desired JSON path, then invoke `BadAppleHotel.EditorTools.BotSimulation.Run` using Unity's `-executeMethod`. Use a separate project copy if the editor is already open.
