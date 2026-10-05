# Bad Apple Hotel: 2.5D walls plan (Oct 4, evening)

Repo on your PC: `/home/josue/Documents/BadApple`
GitHub: https://github.com/kalavhan/BadApple (branch `main`, latest `bf10f89`, merge of PR #2 "playtest fixes" + "thin-walled 2.5D hotel")

This plan covers the four wall issues from the 2.5D playtest and the new wall look. It is based on the code at `bf10f89`: `GameManager.Scene3D.cs` (wall meshes), `HotelView3D.cs` (camera and billboards), `HotelMap.cs` (grid), `GameManager.Residents.cs` (collision) and `HotelSurface.shader`.

## Why the walls look and behave wrong (root causes)

| Problem | Cause in the code |
|---|---|
| Corner walls don't merge and overflow | `BuildScene3D()` makes one box **per wall tile per walkable side**, each `1 + 2×0.14` tiles long. Every box sticks 0.14 tiles past both ends of its tile, even when the wall continues (overlap) or turns. At convex corners (a wall tile with floor on two sides) the two boxes have different heights (far side 1.7 or 1.35, near side 0.22), so they never meet cleanly and their end caps poke out past the corner into the floor. |
| In L-shaped rooms, the short side is hidden by the long side's wall | A wall is tall whenever its floor is on the "far" side (`side == down` or `left`), wherever it is. An interior wall of an L (the inner corner) counts as far-side, so it is 1.7 tall. With the 40–50° camera, a 1.7-tall wall covers about 2 tiles of floor behind it, which is the short arm of the same room. Fading (`UpdateWallOcclusion`) only applies while walking in a hallway, within 3 tiles. |
| It feels like walking over walls | The collision grid and the picture disagree. Collision uses whole wall cells (1 tile thick) with a 0.25 radius character, but the visible wall is only 0.14 thick and hugs the floor edge. Near-side walls are only 0.22 tall, so the upright character card overlaps and stands "on top of" them. Between a room and a corridor the 1-tile wall cell shows two thin slabs with an empty dark gap between them, which looks like floor you should be able to step into. Characters also draw over walls when their sorting order wins. |
| Walls don't look like a hotel | One generated pixel texture (`haunted_wall.png`, purple and charcoal stripes) on plain boxes. No trim, no light, no corners or lamps. |

## Target look: classic 80s–90s grand hotel, warm and dark

Think of the corridors in *The Shining* (the Overlook), but warmer and cosier, still with our Tim Burton twist:

- **Walls:** deep oxblood or burgundy damask wallpaper on top, dark walnut wainscoting (wood panels) on the bottom third, a brass or gold chair rail between them, dark crown moulding at the top.
- **Lamps:** brass sconces with amber fabric shades that throw a warm pool of light on the wallpaper.
- **Corners:** walnut corner posts or pilasters with brass caps.
- **Floors:** hallways get a patterned 80s hotel carpet (orange, brown and red hexagons or diamonds). Rooms keep the wood floor, with a rug under the bed.
- **Palette:** oxblood `#5A1A1E`, walnut `#3B2418`, brass `#B08A3E`, amber light `#F2B35C`, shadow `#1A0E10`. A tiny touch of the Burton look: slightly crooked frames, a too-wide grin in the damask, sconces that lean.

## Phase A: wall geometry rebuilt from edges (fixes overflow and corners, about 1 day)

Replace the per-tile boxes with a wall graph built from **edges**:

1. **Find edges:** for every pair of neighbouring cells where one is walkable (room floor, corridor, door) and the other is not (wall, void), record the shared tile edge and its outward normal.
2. **Merge runs:** join collinear edges with the same normal and the same height class into one run (a straight wall from corner to corner).
3. **Corners as their own pieces:** at every grid vertex where runs meet, classify the vertex as an outer corner, inner corner, T-junction or end cap, and place one corner piece there. Runs stop exactly at the vertex, so there is no overlap and nothing sticks out past a corner.
4. **Walls sit inside the wall cell, never in the floor:** wall thickness `0.3` (config), placed from the floor edge outwards into the wall cell. A room-to-corridor wall cell (1 tile wide) is drawn as **one solid wall block** filling the whole cell, with a moulding cap on top, so there is no dark gap.
5. **One height per run:** height is decided per run (Phase B), never per tile, so the two faces of a corner always match.
6. Combine all straight pieces into a few meshes (`Mesh.CombineMeshes`) for mobile performance. Corner and lamp pieces use GPU instancing.

Code: new `Assets/Scripts/Game/World/WallGraph.cs` (pure data, testable) and a rewritten wall part of `GameManager.Scene3D.cs`.

Tests (`World3DTests.cs`):
- No wall piece's bounds overlap any walkable cell (100 seeds).
- Every edge between walkable and solid is covered exactly once.
- Every grid vertex with 2 or more runs has exactly one corner piece.

## Phase B: cutaway rules so every walkable tile stays visible (fixes the L problem, about 1 day)

- **Heights:** `full` 1.7, `cutaway` 0.45 (a waist-high wall with its moulding cap, so you still see the room shape), `down` 0.1 (baseboard only).
- **Static rule (computed once per map):** a run is `full` only if nothing walkable lies behind it from the camera. Sample the ground under the wall's shadow (height ÷ tan(camera elevation), about 2 tiles) along the camera direction. If any of it is floor or corridor, the run is `cutaway`. This makes the inner corner of an L low, while the room's real back walls stay tall.
- **Dynamic rule (every 0.1 s):** walls of the room you are in, and of a room you select or build in, drop to `cutaway` if they cover any of its floor. In hallways, keep the current fade, but fade whole runs (not single tiles) within 4 tiles of the player.
- **Smooth change:** heights animate over 0.15 s instead of popping, using a vertex-shader height scale per run.
- Add a settings toggle like The Sims: Walls **Up / Cutaway / Down**. Cutaway is the default.

Tests: on 100 seeds, from the default camera, every floor and corridor tile has its centre visible in the room's view (a raycast against the wall bounding boxes).

## Phase C: the wall boundary matches what you see (fixes walking over walls, about 1 day)

- **Make the collision match the wall:** collision uses the same edge graph. Characters collide with each wall run as a thick line segment (`thickness/2 + radius`), not with whole tiles. The visible face of the wall is exactly where your feet stop.
- **Radius:** resident 0.3, monster 0.38 (from 0.25 and 0.3). This keeps the upright card from overlapping a wall face.
- **Depth, not order:** characters, towers and doors render with depth testing against the wall meshes (a sprite shader that writes depth with alpha clipping). A wall in front of a character hides the character's lower body, and a character is never drawn on top of a wall it stands behind.
- **Contact shadow:** a soft dark oval under every character and tower, so it is obvious where the feet touch the floor.
- **Doors:** the door frame is part of the wall kit. Its sides are collision posts, so you can't clip the frame when walking through.
- Tests: walk 300 random routes over 20 seeds (the existing movement test). Assert that no character centre is ever closer than `radius` to a visible wall face, and none ends up inside a wall cell.

## Phase D: hotel wall kit made with Tripo (about 1–2 days, plus generation time)

### Pieces

Required (as you asked):

| Piece | Size (tiles W × D × H) | Description for Tripo |
|---|---|---|
| `wall_straight` | 1 × 0.3 × 1.7 | Straight hotel wall section: dark walnut wainscoting on the bottom third, brass chair rail, oxblood damask wallpaper above, dark crown moulding at the top. Flat back, front-facing detail only. |
| `wall_lamp` | 1 × 0.3 × 1.7 | Same wall section with a brass art-deco sconce at two-thirds height and an amber fabric shade. The bulb area is a separate material for emission. |
| `wall_corner` | 0.3 × 0.3 × 1.7 | Square walnut corner post with brass base and cap, the same mouldings as the walls so it joins them seamlessly. |

Recommended (same style, small):

| Piece | Why |
|---|---|
| `wall_inner_corner` | Concave corners look cleaner than two walls meeting. |
| `wall_end_cap` | Where a wall stops (doorways, dead ends). |
| `door_frame` | Walnut frame with a brass room-number plate, fits the 1-tile door. |
| `wall_cutaway_cap` | A low wall top for cutaway mode (wainscot plus rail only), 1 × 0.3 × 0.45. |
| `wall_portrait` | A wall variant with a crooked framed portrait (Burton touch), used at random. |

### How we make them

1. **Setup (one-time, you):** Tripo is available through the Unity MCP `generate_model` tool, but it is **not configured yet** (`list_providers` shows `tripo: configured false`). Add your Tripo API key in the Unity MCP settings (the key stays in the editor's secure store). Unity has no glTF importer in this project, so either request `format: fbx` or add the `com.unity.cloud.gltfast` package.
2. **Concept first:** make one image of the wall straight piece with an image generator, matching the palette above. Then use Tripo **image → 3D** for every piece with that image as the style reference, so all pieces match. Use text → 3D only for small variants.
3. **Generate:** `generate_model` with `provider: tripo`, `format: fbx`, `texture: true`, `target_size` set to the sizes above, `output_folder: Assets/Art3D/Walls`.
4. **Clean-up in Unity (scripted):** set the pivot at the bottom-left of the floor edge, scale to exact tile units, reduce to at most about 1,500 triangles per piece, and check that the ends of `wall_straight` line up with `wall_corner` (no gaps).
5. **Consistent materials:** Tripo textures can drift in colour between pieces. Keep Tripo's geometry, but put **our own shared materials** on it: one tileable damask wallpaper texture, one walnut texture, one brass material and one amber emissive shade. Every piece then has exactly the same colours, and the wallpaper repeats seamlessly along long walls.
6. **Lighting:** the sconce shade gets a warm emissive glow, plus a cheap decal of light (a soft amber gradient) on the wallpaper above and below it. No real-time lights per lamp, for mobile. Put a lamp about every 4–6 tiles along hallway runs and one per room wall.
7. **Sprites too:** render each piece from the game camera into a sprite (`Assets/Resources/Art/Walls/*.png`). Use them for the build-menu and settings previews, and as a low-cost fallback when `Sprites.UseArt` or a low-quality setting turns the 3D kit off.

### Placement rules
- Straight runs are filled with `wall_straight`. Every 4–6 tiles, one piece becomes `wall_lamp` (hallways) or `wall_portrait` (random 10%), chosen by a hash of the position so it is the same every time.
- `wall_corner` sits on every outer corner vertex, `wall_inner_corner` on inner ones and `wall_end_cap` on wall ends.
- Cutaway walls use `wall_cutaway_cap`, so lowered walls still look finished, not sliced.
- Hallway floor: the 80s hexagon carpet texture; rooms: wood plus a rug under the bed.

Tests: every piece fits its footprint (bounds within 0.01 tiles), and the kit builds a hotel on 20 seeds with no missing piece types.

## Phase E: polish and checks

- Fog of war still works on the new meshes (the `_HotelVision` sampling in `HotelSurface.shader`), including lamps.
- Performance on Android: wall draw calls under about 30 after batching, and 60 fps on the test phone in a full hotel.
- Screenshots at 19.5:9 and 4:3 of: an L-shaped room, a corridor corner, a door, a room-to-corridor wall, and a character walking along a wall.
- Update `docs/art/` with the Tripo prompts, job ids and settings, the same way `2.5D-textures.md` records the image prompts.

## Order of work

1. Phase A (wall graph) and Phase C (collision) together, because both use the same edges.
2. Phase B (cutaway).
3. Phase D (Tripo kit), once your Tripo key is set. Phases A–C work with simple box pieces in the meantime, so they don't wait on art.
4. Phase E.

Each phase ends with EditMode tests passing, an Android build, screenshots in `docs/qa/` and a commit to `main`.

## What I need from you

1. Add the Tripo API key in the Unity MCP settings, so `generate_model` can use Tripo.
2. Confirm the look: oxblood damask wallpaper over walnut wainscoting, brass sconces, 80s hexagon carpet in the hallways. Or tell me which of these to swap.
