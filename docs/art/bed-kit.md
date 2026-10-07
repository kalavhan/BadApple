# Imported 3D beds — October 6, 2026

The game has seven bed levels, matching the concepts in `docs/art/beds` 01 to 07: paper, cardboard, mattress, ordinary bed, floating bed, coffin and skull bed (the spider bed, 08, is not a level). All seven levels now render Josue's Tripo models instead of sprites.

| Level | Concept (docs/art/beds) | Tripo export folder | Source triangles |
| ---: | --- | --- | ---: |
| 1 | 01 The Liability Waiver (paper) | `95ff6c85-e1f1-459b-8f97-655703808af1` | 1,850 |
| 2 | 02 Cardboard Deluxe | `cardboard_sheet_3d_model` | 1,776 |
| 3 | 03 The Biohazard (mattress) | `1766abd6-4018-434b-8053-e1f1e9866f4b` | 2,414 |
| 4 | 04 An Actual Bed | `wooden_bed_3d_model` | 3,317 |
| 5 | 05 No Strings Attached (floating) | `c12a3930-1316-49f5-8b37-006107da2a02` | 3,343 |
| 6 | 06 Rest in Heat (coffin) | `coffin_3d_model` | 3,798 |
| 7 | 07 The Headache (skull bed) | `skull_bed_3d_model` | 4,850 |

The generic export names were identified by rendering each model. The importer also accepts the import folders named in each manifest (`bed_01_paper`, `bed_02_cardboard`, `bed_03_mattress`, `bed_04_normal`, `bed_05_floating`, `bed_06_coffin`, `bed_07_skull`), so later exports placed there are picked up by the same bake.

**Bake.** Bad Apple → Art → Bake imported bed models (batch: `BadAppleHotel.EditorTools.BedKitImporter.BakeBatch`). For each level with an export, the importer applies the FBX node transform, maps source axes (X width, Y up, Z length, head toward +Z) to game axes (XY floor, head toward +Y, negative Z height), turns the long side along the bed and the head end (taller headboard, skull or crescent; the wider coffin end) toward the pillow, uniformly fits the 0.94 × 1.9 tile bed footprint, and places the pivot at the footprint center on the floor. It writes `Assets/Resources/Art3D/Beds/bed_N.asset`, a 512² point-filtered albedo `bed_N_albedo.png`, and `HotelBedKit.asset`. It also records the sleeping-surface height measured over the torso-to-knee band, clear of headboards and skulls. The skull bed's pillow anchor in `beds.json` sits at 0.3 so the head clears the skull. The originals in `Assets/TripoModels/` are not edited or referenced at runtime.

**Runtime.** `GameManager.ApplyBedLook` shows the baked model for the room's bed level, or falls back to the sprite. Models use the same lighting as `HotelSurface`, so room lamps, ambient light and fog apply as on other hotel surfaces. The bed sprite renderer remains as the sorting and visibility anchor. A sleeping resident lies just above the measured surface. Headless simulations do not load models.

**Idle motion.** Beds use the `HotelBed` shader, which is `HotelSurface` lighting plus a vertex-only idle motion chosen per level (`BedKit.Piece.Motion`). Level 1 flutters its four free corners in short gusts; level 2 lifts its foot-end flap briefly every few seconds; level 5 hovers .14 tiles with a slow bob (its measured surface includes the hover). The middle sleeping surface does not move. Each room gets its own phase, so beds never move in unison. The flames, embers, eye glow and jaw chatter in the coffin and skull manifests are not implemented yet.

`BedKitTests` checks the footprint, pivot, floor contact, surface height, triangle budget and texture size for all seven levels.
