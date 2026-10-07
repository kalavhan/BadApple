# Imported 3D doors — October 7, 2026

The door renderer uses the existing hotel frame and a separately hinged 3D leaf. All seven agreed designs are imported. Gameplay still has ten upgrade levels with the existing health, resistance, costs, movement, and line-of-sight rules. The art progression ends at level 7; levels 8–10 reuse Deadbolt.

## Available art

| Level | Design | Import folder under `Assets/TripoModels` | Status |
| ---: | --- | --- | --- |
| 1 | Privacy, Allegedly | `door_01_cardboard` or `cardboard_crate_3d_model` | Baked: 1,862 triangles, 512px albedo |
| 2 | Splinter Security | `door_02_planks` or `rustic_wooden_door_3d_model` | Baked: 1,890 triangles, 512px albedo |
| 3 | An Actual Door | `door_03_hotel` or `antique_wooden_door_3d_model` | Baked: 1,850 triangles, 512px albedo |
| 4 | Do Not Summon | `door_04_runes` or `wooden_door_3d_model` | Baked: 4,000 triangles (from 19,762), 512px albedo |
| 5 | The Spirit Level | `door_05_floating` or `stone_door_3d_model` | Baked: 4,000 triangles (from 13,510), 512px albedo |
| 6 | Bad Reflection | `door_06_mirror` or `medieval_wooden_door_3d_model` | Baked: 3,999 triangles (from 12,386), 512px albedo |
| 7 | Deadbolt | `door_07_skull` or `skull_door_3d_model` | Baked: 4,000 triangles (from 13,157), 512px albedo |

All seven designs have matching textured broken-fragment meshes. Levels 8–10 reuse the imported Deadbolt mesh and its effects. The earlier eye door is superseded by the mirror, and the coffin door by the skull. Local authoring references and per-design notes remain in the unversioned `docs/art/doors` directory; they are not needed to load the baked kit.

## Baking later exports

Place one textured static FBX and its `_BaseColor` texture in the matching folder, then use **Bad Apple → Art → Bake imported door models**. Batch entry point: `BadAppleHotel.EditorTools.DoorKitImporter.BakeBatch`.

The bake applies FBX transforms, faces the front outward, straightens the dominant rear panel to remove authored yaw, fits a 0.94 × 1.4 tile leaf with at most 0.2 tile depth, and establishes a lower-left ground-level hinge. Local X runs from hinge to latch, local Y into the room, and negative Z is height. Normals use the inverse-transpose fit; UVs remain authored. Dense exports are reduced toward 4,000 triangles. Inspect each newly imported model's front, hinge, fit, and UVs before accepting it.

Runtime meshes and textures live in `Assets/Resources/Art3D/Doors`. Original Tripo files stay untouched and are not runtime dependencies. A partial rebake retains previously baked levels whose source files are absent. Missing intermediate tiers use their existing sprite rather than substituting another design.

## Presentation

`DoorModel` swings inward by 90 degrees in about 0.21 seconds. Unclaimed doors start open. Damage briefly rattles the leaf; supernatural tiers render a local pulse on upgrades and rebuilds, including upgrades sharing level-7 art. Ordinary tiers remain nonmagical. Broken doors immediately replace the leaf with six low 3D fragments using its original texture and stop powered effects. Debris is baked, fits the threshold, and stays below 0.12 tile height; no runtime mesh generation or new collision is involved. Missing rubble art retains the legacy marker. Rebuilding follows the existing doorway-occupancy rules.

`HotelDoor` keeps opaque, depth-tested surfaces under hotel lighting. It uses the logical doorway for fog. Door leaves stay full height and opaque in every wall display mode, independently of the frame’s cutaway height/fade. Hidden doors, dead/unowned rooms, and broken doors do not emit powered light. Materials are shared per design; animation state uses property blocks. Simulation-only matches do not load door meshes.

The shader includes cardboard edge flutter, an ordinary-door latch hit glint, breathing mint/violet runes, shallow floating-plate motion, opaque mirror shimmer and impact ripples, and skull-carving traces with dark sockets/nose. Effects remain on the leaf and never span an open passage. The magical masks use local coordinates and albedo tones tuned against their imported geometry. There are no new combat powers, live reflections, independent rigged braces, or persistent particle clouds.

## Room-status welcome mats

Every doorway has a soft rectangular spectral outline on the corridor floor, aligned with the leaf and placed 0.78 tile outside its center. It stays clear of the wall and never swings with the door. The treatment uses a breathing border, inset line, and soft fill related to the tower-placement glow.

| Match period | Room state | Mat |
| --- | --- | --- |
| Choosing a room | Available | Green |
| Choosing a room | Occupied, including an early sleeper | Red |
| After selection | Living resident awake | Dim yellow |
| After selection | Living resident sleeping | Bright yellow |
| After selection | Resident dead | Red |
| After selection | Still empty | Blue |

`DoorWelcomeMat` reads the live phase and owner state each frame. All mats share one two-triangle mesh and material, with per-room property blocks. The additive shader is depth tested, obeys doorway fog visibility, and is independent of wall cutaways. Markers are hidden outside a match, add no colliders, and are released with the world.

`DoorKitTests` and `DoorWelcomeMatTests` cover import bounds/budgets, closed-panel alignment, all four hinge/mat directions, state transitions, path blocking, fog power suppression, full-door rendering in every wall mode, missing-art fallback, rendered status colors, and shared-resource disposal. `DoorCapture` renders the available baked doors and all six mat states for visual inspection.
