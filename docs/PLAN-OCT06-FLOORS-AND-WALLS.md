# Bad Apple Hotel floor materials and wall integration plan

Create a believable, continuous hotel interior with readable building spaces. Wooden boards and carpet patterns cross gameplay-cell boundaries; spectral markings communicate the grid. Warm wall lamps establish the room lighting, while restrained cool energy identifies building and upgrading interactions.

This handoff includes five generated source textures and the implementation sequence. The current task delivers artwork and planning. The textures are not installed in the game yet. The earlier proposal to repeat a framed parquet panel once per gameplay square is superseded.

Repository: `/home/josue/Documents/BadApple`. Baseline inspected: `20d9f5f`, “Light rooms from their lamps and keep claimed rooms visible”, following the merged single-wall work. Recheck the current branch before implementation and retain newer unrelated changes.

## Generated artwork and paths

All five PNG sources are currently 1254 × 1254 pixels. They were created with the built-in imagegen tool; no AutoSprite or Tripo generation was used. The files are base-color artwork, not complete physically calibrated material sets. Repeat-edge cleanup, runtime import sizing and in-game lighting checks are still required.

Source folder: `/home/josue/Documents/BadApple/docs/art/floor-materials-2026-10-06`.

| File | Intended use | Sampling rule |
| --- | --- | --- |
| [floor-walnut-continuous-a.png](/home/josue/Documents/BadApple/docs/art/floor-materials-2026-10-06/floor-walnut-continuous-a.png) | Warm walnut floor with long boards and staggered joints. Default room floor and exposed floor beside runners. | Continuous mapping over several gameplay cells. |
| [floor-walnut-continuous-b.png](/home/josue/Documents/BadApple/docs/art/floor-materials-2026-10-06/floor-walnut-continuous-b.png) | Cooler, quieter weathered walnut finish for selected rooms or wings. | Select by room or zone. Do not blend whole images until plank seams have been checked for compatibility. |
| [carpet-burgundy-field.png](/home/josue/Documents/BadApple/docs/art/floor-materials-2026-10-06/carpet-burgundy-field.png) | Burgundy botanical fabric for corridor runners and some rugs. | Repeat fabric across the runner; do not stretch one image down an entire corridor. |
| [carpet-charcoal-field.png](/home/josue/Documents/BadApple/docs/art/floor-materials-2026-10-06/carpet-charcoal-field.png) | Charcoal-green fabric for a coordinated second corridor or lobby treatment. | Assign coherent zones, avoiding a checkerboard of carpet designs. |
| [carpet-antique-binding.png](/home/josue/Documents/BadApple/docs/art/floor-materials-2026-10-06/carpet-antique-binding.png) | Woven burgundy and antique-brass edge binding. | Repeat horizontally along the strip; clamp across its width. Make corners and ends with appropriate mesh UVs. |

Exact prompts, references, source paths, dimensions and checksums are in [generation-manifest.json](/home/josue/Documents/BadApple/docs/art/floor-materials-2026-10-06/generation-manifest.json). Keep these sources separate from processed Unity assets. Proposed runtime destination: `Assets/Resources/Art/Floors/` after material preparation.

## Floor appearance and repetition control

The finish should read as wood and woven fabric, with the restrained, slightly crooked character of an original gothic comic hotel. Preserve the imported walls' walnut, oxblood and antique-brass palette. Strong directional highlights and magical signs belong in the engine, not in the base texture.

1. Keep the existing combined floor meshes. Replace the current one-texture-repeat-per-cell mapping with continuous room or world coordinates. Floorboards should have a believable width relative to a resident and run in one direction within a room. A starting scale is roughly 0.2–0.3 tile per board width, adjusted in the actual camera view.
2. Choose finish, texture offset and orientation deterministically by room or corridor zone. Maintain consistent scale across the hotel. Do not rotate individual gameplay squares.
3. Add subtle wear over larger areas: paths near thresholds, modest edge dirt and occasional local scuffs. These masks should cross several cells and avoid a conspicuous stain repeating on every texture cycle.
4. Preserve board joints when varying grain. A later plank atlas may offer more variation, but arbitrary blending of two complete floor images can produce doubled seams. First validate the two supplied finishes as whole-room alternatives.
5. Carpet patterns may repeat like manufactured fabric. Vary the runner route, fabric zone, rug placement and wear so the surrounding composition does not repeat identically.
6. Test complete rooms and long corridors at gameplay zoom, with the spectral grid off as well as on. A single texture swatch cannot establish that repetition is acceptable.

Retain the nonoverlapping floor subdivisions beside thin walls. They fill the recovered 0.35-tile margins. These fragments need the same continuous mapping and finish ownership as the adjacent floor; expanding overlapping quads would reintroduce flicker.

## Carpets and rugs

Generate thin carpet meshes from valid floor regions. Use a separate fabric field and edge binding, with clipped corners, clean junctions and end pieces. Leave an exposed floor margin where appropriate, end runners cleanly at door thresholds, and avoid fabric intersecting wall bases or door frames.

Ordinary rugs need a simple thin mesh, not a Tripo model. Add enough vertical separation to prevent depth flicker while keeping the carpet visibly seated on the floor. Carpets do not add movement collision or consume build slots. Room rugs should remain within existing reserved areas unless the design deliberately permits building over them.

A curled or rolled rug can be a later Tripo prop if its silhouette is useful. It is not required for the initial floor pass.

## Spectral energy and building indicators

Use a shared material with pale cyan or mint light and restrained violet fringes. Low drifting ribbons should suggest a small aurora rising through the floor. Most motion stays near ankle height, with a little more energy around the selected cell. Keep wood, carpet, feet and tower silhouettes readable.

Build the plus and arrow as exact procedural shapes. Animate their material and transforms in Unity; no tower animation sheets or AutoSprite credits are needed. Use a small capped amount of ribbon geometry and soft local halos. Avoid one real-time light or a large particle emitter per cell, and ensure the effect remains readable without fullscreen bloom.

| State | Presentation |
| --- | --- |
| Empty and legally buildable | Quiet square corners or boundary plus a small centered spectral plus. |
| Selected and buildable | Brighter complete outline, stable plus and sparse rising wisps. |
| Occupied | Remove the plus; retain subtle separation where needed. |
| Blocked | No inviting plus. Selection explains the obstruction. |
| Bed and reserved access path | Normal flooring without build signs. |
| Owned tower upgrade affordable now | Camera-facing arrow with gentle bob and tilt. |
| Upgrade available but unaffordable | Show cost and a muted indication in the selected tower menu. |
| Maximum tower level | No upgrade arrow. |

Reuse `CanBuildAt` and the authoritative upgrade conditions. Cache placement eligibility after build, sell or layout changes rather than flood-filling all cells every frame. Share a read-only upgrade eligibility query between the arrow and the action. An arrow tap opens the menu; it does not spend currency. Show the word UP only for the selected tower if it remains readable.

Respect the current visibility policy: a living resident's claimed room remains visible from elsewhere, while unknown rooms and hidden corridors remain sight-limited. This ownership exception does not grant action permissions. Effects must still depth-test against wall geometry and cannot disclose other residents' hidden tower states.

## Wall appearance and construction

The walls should look like one continuous hotel structure, with aligned wood panels, chair rail, wallpaper and crown moulding. Their craftsmanship may be stylized; the architectural joins must be precise.

- Keep one physical perimeter per room, including all four central rooms. The corridor must not generate another shell around the same room boundary.
- Keep the thin 0.3-tile wall footprint and use that same footprint for rendering and collision. Do not restore full-cell backing blocks or opposing decorative wall layers.
- Decorative faces point toward the fixed camera: south on horizontal runs, west on vertical runs. This is the working interpretation of the single south-facing wall request in the current camera orientation.
- Place one fitted corner cover at every turn, including south-facing, convex and concave corners. Use end caps at genuine terminations. Straight runs and their trim should meet the corner without gaps, protrusions or doubled posts.
- The outer corner's requested left turn is already baked by `WallKitImporter`. Preserve it; do not apply the rotation again during placement.
- Keep baseboard, chair-rail and crown heights consistent across adjoining pieces. Floors reach the wall footing without a dark unfilled strip, and carpet edges do not climb the baseboard.
- Keep wallpaper and wood detail at a coherent scale. Repeated architecture is natural; identical conspicuous stains, damage or pictures on every panel are not. Add later wall wear sparingly and use separate decorative sockets for varied frames.
- Do not bake the same painting into every wall section. Later artwork should use interchangeable frames and canvases with multiple images. Chairs, shelves and cabinets belong in suitable corridor or lobby spaces, without reducing resident build capacity.

## Wall cutaways and sight

Full walls and lowered walls must both look finished. Lowering a wall is a view aid; collision does not disappear.

| Situation | Required behavior |
| --- | --- |
| Hotel exterior boundary | Full-height wall by default, rather than a permanent external cutaway. |
| Resident inside a room | Camera-near bottom-left and bottom-right sides lower to the capped base. Camera-far top sides stay up. |
| Concave room return obscuring its interior | Apply the established local occlusion rule, without lowering unrelated exterior walls. |
| Resident walking in a hallway | Preserve the local wall lowering or fading that keeps the resident readable, then restore the wall after passage. Keep its cap, solid collision and existing fog rules. This is separate from a monster peek. |
| Monster adjacent to a room wall | A local peek may expose at most three neighboring straight cells: the adjacent cell and one on either side. Stop at turns, doors and other rooms. |
| Peek or cutaway active | Retain the low capped wall/baseboard instead of an empty hole. Keep the wall collision intact. |
| Lamp hidden by the cutaway or peek | Hide its visible halo with the fixture; no floating light blob. |

Current normal room heights are 1.7 tiles when full and 0.1 tile at the lowered base. Hallway occlusion can use the intermediate 0.45-tile cutaway height. Preserve the available wall modes and their transitions. Keep resident-to-monster and monster-to-room sight restrictions centralized in the existing sight rules, including the current claimed-room visibility exception. A visual wall opening must never reveal an entire unrelated room or remote corridor by itself.

## Lamps and dark readability

Sconces remain the visible source of warm light. Their shades or bulbs should glow, and the wallpaper, wood and floor should show a readable pool of illumination. Maintain dimmer regions for atmosphere. Spectral floor effects provide a cool accent and must not wash the room into uniform brightness.

There is a numerical difference between the earlier request and the latest committed tuning:

| Setting | Earlier requested target | Current baseline in HotelLampPlan |
| --- | --- | --- |
| Minimum lamp spacing | 3 tiles | 2 tiles |
| Useful room light coverage | 90 percent, with the remainder dim but visible | Target all reachable room samples |
| Useful corridor light coverage | 85 percent, with the remainder dim | 80 percent target |

These percentages describe useful floor-area coverage, not a brightness multiplier. The latest code uses radii of 10 tiles for room lamps and 9 for corridor lamps, and a planning threshold of 0.30. Its room target is capped by samples reachable from available mounts.

The floor pass should first preserve the committed lamp positions and lighting behavior to make before-and-after material comparisons meaningful. Record the difference from the earlier targets explicitly. Any later spacing or coverage retune needs a deliberate decision and new measurements; old QA numbers must not be presented as evidence for the current tuning. The visual objective remains dark, readable rooms and somewhat darker corridors, with clear warm pools from actual lamps.

## Implementation sequence

1. **Prepare materials.** Inspect repeated previews, clean seams and unwanted baked lighting, and create runtime copies. Add a floor-specific import profile with appropriate repeat modes, filtered sampling, mipmaps and verified Android compression. Existing generic art importer settings are unsuitable as a silent default. Start with albedo; normal mapping and roughness need explicit shader work.
2. **Build one room and corridor sample.** Introduce continuous UVs and room/zone material selection while preserving the thin-wall floor margins. Add carpet fields, border strips and thresholds. Compare the two wood finishes under actual lamp lighting.
3. **Add interaction effects.** Implement the shared spectral material, eligibility-driven cell markers and upgrade indicators. Verify selection, affordability, maximum level, ownership and visibility.
4. **Validate wall integration.** Exercise central and peripheral rooms, both wall axes, corners, doorways, wall modes and monster peeks. Correct any floor-to-wall or carpet junction problems without rebuilding a second perimeter.
5. **Expand across generated maps.** Apply deterministic finish selection and restrained wear. Validate the exact room budgets, navigation, material submissions and Android rendering. Produce a release-style test APK, screenshots and a PR for this implemented phase.
6. **Add props and hiding later.** Use corridor/lobby placement sockets with footprint and clearance checks. Hiding requires explicit entry/exit, occupancy, safe exit positioning and monster search behavior; it is a separate gameplay feature.

Primary code areas are `GameManager.Scene3D.cs`, `ArtImporter.cs`, the floor and lighting shaders, `GameHUD.cs`, `GameManager.Residents.cs` and `GameManager.Vision.cs`. Wall behavior remains owned by `WallPerimeter`, `WallVisibility`, `WallSight` and the wall state renderer. Keep shared-material batching rather than creating one renderer or material instance per floor square.

## Acceptance checks

- A complete room and a long corridor read as installed flooring with the grid hidden. No framed texture stamps appear once per build cell; plank size and direction remain coherent.
- Carpets have clean borders, correct scale, seated edges and usable thresholds. Floors remain continuous at thin walls, stepped corners and door frames, without depth flicker.
- Every central room has one wall perimeter. South and concave corners close correctly, and no corner piece enters a route or leaves a gap.
- Room cutaways keep their low cap; far walls stay up; exterior walls remain full by default. Walking past a hallway occluder lowers or fades it locally, then restores it. The local monster window stays within three cells and never removes collision.
- Own-room visibility, other-room fog, lamp halos, spectral markers and tower visibility agree with the current gameplay policy.
- Exactly ten rooms remain, with four central rooms, rare conjoined rooms, existing distance constraints and build budgets of 30, three 26s, three 23s and three 20s. Beds occupy two reserved cells; access paths and clear corridor routes remain intact.
- Upgrade markers agree with actual actions before and after spending, earning, building, upgrading and selling. Effects remain distinguishable with reduced motion.
- Check useful lamp coverage separately from perceived material brightness. Capture both with and without spectral effects so glow cannot hide poor room lighting.
- Run the relevant current Unity tests and Android shader checks after implementation. Capture normal-fog play at phone aspect ratios and compare device frame time against the same baseline scene. Do not claim a phone performance target from source images or an emulator screenshot alone.

## Later Tripo asset delivery

No floor model is needed for this material approach. For furniture, keep textured FBX sources with base-color maps; retain optional normal and roughness maps. A GLB source may also be retained but should not be assumed to import without a suitable importer. Keep full-quality originals separate from optimized game meshes.

Request complete chair and bookshelf models, a decorative small cabinet, a tall linen cupboard and a large lidded hamper or waste bin for hiding. Cabinet doors and bin lids must remain separate meshes with hinge pivots. Use a consistent upright authoring convention and a ground-level body pivot; normalize scale on import. Place props outside resident rooms and preserve clear walking and interaction space. Do not use the wall mesh baker to merge away movable parts.
