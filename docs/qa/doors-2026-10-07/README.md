# 3D doors validation — October 7, 2026

Validation baseline: `main` at `2575ffe`, with the door implementation on `codex/3d-doors`.

## Current APK and welcome mats

Version **1.19-door-markers**, Android code **20**, is `Builds/BadAppleHotel-door-markers.apk`. All **27/27 current door and welcome-mat tests passed**, with no failures or skips ([results](door-tests-final.xml)).

The floor marker is green for an [available room](door-game-1-mat-available.png) and red for an [occupied room](door-game-1-mat-occupied.png) during room selection. Afterwards it is bright yellow for a [sleeping resident](door-game-1-mat-sleeping.png), dim yellow while [awake](door-game-1-mat-awake.png), red after [death](door-game-1-mat-dead.png), and blue for a [still-empty room](door-game-1-mat-empty.png). The controlled captures exercise marker states; character poses are frozen for visual comparison.

Tests verify actual rendered colors, all four corridor-side placements, fog suppression, live state transitions, and shared-resource cleanup. The marker stays flat, clear of the wall, and nonblocking.

## Wall-cutaway correction

The door leaf is independent of wall height and fade. The wall/frame can lower while the complete door remains visible as a standalone object. Opening, breakage, and fog still work normally. The rendered regression compares all seven doors pixel-for-pixel in Walls Up, Cutaway, and Down.

See the [mirror with cutaway walls](door-game-6-cutaway.png) and [walls fully down](door-game-6-walls-down.png).

## Door placement and angle correction

The cardboard, walnut hotel, and mirror exports contained yaw inside the source meshes. Axis-aligned bounding-box fitting left their closed panels approximately 6–9 degrees off the wall plane. The importer now straightens the dominant rear panel before fitting the leaf and baking its hinge/debris. A geometry regression verifies that every closed panel is within 2.5 degrees of its frame (allowing the authored uneven surfaces). Hinge position and inward opening are checked in all four directions.

The capture pack includes all seven doors at four map orientations. Hotel-door examples: [0 degrees](door-3-alignment-0.png), [90](door-3-alignment-90.png), [180](door-3-alignment-180.png), [270](door-3-alignment-270.png).

## Imported models

All seven approved designs are implemented: cardboard, salvaged planks, hotel wood, protective runes, floating stone, haunted mirror, and skull. Each has a fixed hinge, a 512px texture, at most 4,000 triangles, and six baked textured broken fragments. Gameplay remains at ten levels; levels 8–10 reuse the skull model. Source aliases and mesh budgets are recorded in [the door-kit notes](../../art/door-kit.md).

## Visual evidence

| Level | Front inspection | Hotel placement |
| ---: | --- | --- |
| 1 | [Cardboard](door-1-closed-front.png) | [Closed](door-game-1-closed.png) |
| 2 | [Salvaged planks](door-2-closed-front.png) | [Closed](door-game-2-closed.png) |
| 3 | [Hotel wood](door-3-closed-front.png) | [Closed](door-game-3-closed.png) |
| 4 | [Protective runes](door-4-closed-front.png) | [Closed](door-game-4-closed.png) |
| 5 | [Floating stone](door-5-closed-front.png) | [Closed](door-game-5-closed.png) |
| 6 | [Haunted mirror](door-6-closed-front.png) | [Closed](door-game-6-closed.png) |
| 7 | [Skull](door-7-closed-front.png) | [Closed](door-game-7-closed.png) |

The capture pack contains 104 isolated views and 48 hotel views. Every design has closed, open, broken, rebuilt, cutaway, and walls-down evidence. Magical designs also have matching unpowered and later-idle views. The rune glow follows the seal, floating-stone light follows seams and inlays, mirror shimmer stays on opaque silver, and skull sockets/nose stay dark.

For the last imported model, compare [mirror open](door-6-open-iso.png), [broken](door-6-broken-iso.png), [unpowered](door-6-unpowered-front.png), and [cutaway](door-6-cutaway-iso.png), plus the hotel [open](door-game-6-open.png), [broken](door-game-6-broken.png), [rebuilt](door-game-6-rebuilt.png), and [cutaway](door-game-6-cutaway.png) states. The opened leaf shows its wooden back and clears the passage. Original surface texture remains on low debris; rebuilding restores the leaf.

The isolated inspection uses neutral lighting and a fixed clock. The hotel fixture uses clairvoyance to keep the entire capture readable; it is not evidence of ordinary gameplay fog. Visibility suppression is checked separately in regression tests. The isolated front view shows a small clearance between leaf hardware and frame; there is no collision obstruction. Captures use Unity 6000.3.24f1 and desktop llvmpipe rendering, not a phone performance benchmark.

## Reproduction

The earlier complete Unity EditMode suite passed **140/140 tests**, with no failures or skips ([results](editmode-tests.xml)). The current 27-case focused result above additionally covers door independence from wall modes, panel alignment, welcome mats, Android shader compilation, and rendered-emission checks. The test and capture logs contain no C# errors, shader errors, or runtime exceptions.

The door cases cover all seven meshes and rubble, all four hinge orientations, real path blocking and rebuild transitions, visibility/power, missing-art fallback, material lifetime, logical upgrades sharing tier-7 art, Android GLES3 vertex/fragment compilation, and actual animated emission rendering. The shader render test verifies that hidden, broken, fogged, and unpowered doors emit no supernatural light. The edit-mode hotel fixture emits an existing character-material instantiation warning; it does not come from the shared door materials.

Use a disposable project copy with a graphics device. Batch entry point `BadAppleHotel.EditorTools.DoorCapture.BakeAndCapture` bakes available imports and runs both capture fixtures; pass `-quit`. `DoorCapture.Run` runs only isolated inspection, and `DoorCapture.RunGame` only the hotel fixture. `BADAPPLE_DOOR_QA_OUT` chooses the output directory. Run Unity EditMode tests without `-quit`; `DoorKitTests` includes Android GLES3 vertex and fragment compilation.
