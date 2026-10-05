# Imported hotel wall kit — October 4, 2026

Josue generated and imported seven Tripo models from our original dark gothic comic references. The palette is oxblood botanical damask, walnut wainscoting, worn brass and amber shades. No movie design, character or specific set is reproduced. The fixed portrait wall is retired; separate paintings and hallway furniture remain for the later prop pass.

The generic Tripo names were identified by rendering the actual geometry:

| Runtime piece | Tripo export stem | Source triangles | Baked triangles | Width × depth × height |
| --- | --- | ---: | ---: | --- |
| wall_straight | ornate_wooden_cabinet_3d_model | 10,404 | 1,200 | 1 × .3 × 1.7 |
| wall_lamp | decorative_wall_panel_3d_model | 1,932,011 | 1,499 | 1 × .3 × 1.7 |
| wall_corner | ornate_wooden_pedestal_3d_model | 1,916,892 | 1,500 | .3 × .3 × 1.7 |
| wall_inner_corner | ornate_column_3d_model | 1,970,974 | 1,500 | .3 × .3 × 1.7 |
| wall_end_cap | ornate_wooden_pedestal_3d_model_1 | 1,894,644 | 1,500 | .3 × .3 × 1.7 |
| door_frame | wooden_console_table_3d_model | 1,974,345 | 1,500 | 1.2 × .3 × 1.7 |
| wall_cutaway_cap | wooden_cabinet_3d_model | 1,971,466 | 1,500 | 1 × .3 × .45 |

`Assets/Resources/Art3D/Walls/HotelWallKit.asset` references the seven readable, single-submesh baked meshes and one 2048² sRGB atlas. Model coordinates are X width, Y up and Z depth, with minimum-bounds pivot at the front floor edge. The renderer converts them to the game's XY floor and negative-Z height. Door posts leave one complete tile of passage, including at the base moulding.

The original geometry contains many tiny UV islands. Direct UV-preserving simplification retained roughly 200,000 triangles per high-resolution piece. The importer instead welds positions within 1e-5 tile, reduces geometric topology with the MIT-licensed [UnityMeshSimplifier](https://github.com/Whinarn/UnityMeshSimplifier), and reprojects the original albedo from six orthographic directions onto a shared atlas. This retains the damask, grain and carving while making the kit suitable for runtime batching. Fine ornament is represented in the texture; silhouettes retain the imported 3D shape. Four pixels of edge extrusion surround each atlas view. Point filtering and mipmaps give the requested pixel texture treatment without distant shimmer.

The straight source was reconstructed at a slight horizontal angle. The bake aligns its broad faces before fitting the tile footprint, then squares its inset end shoulders before projecting the albedo. This prevents alternating crown steps and empty vertical seams when panels repeat. The lamp variant shares that exact panel, crown, base and atlas region; only the imported brass sconce is added. Its relief depth fits inside the shared panel’s front recess. This keeps the wallpaper and moulding continuous instead of alternating between independently generated wall profiles. The cutaway cap already aligns closely: ray measurements show less than .005 tile difference between its two end profiles.

Lamp shade vertices carry emission strength in color alpha; the rest of the kit has zero emission. Lighting is handled by the shared hotel shader, with no realtime lamp objects. Preview/fallback sprites in `Assets/Resources/Art/Walls/` use the fixed game-camera angle and transparent backgrounds.

To regenerate, keep Josue's original folders under `Assets/TripoModels/`, then use **Bad Apple → Art → Bake imported wall kit**. The importer preserves originals and updates the baked assets in place. Batch entry point: `BadAppleHotel.EditorTools.WallKitImporter.BakeBatch`. The high-resolution originals are not referenced by runtime assets and need not be present to build or play a checkout containing the baked kit.

Source hashes, normalization settings and triangle counts are in [tripo-wall-kit-manifest.json](tripo-wall-kit-manifest.json). The original image-generation reference prompts are in [tripo-wall-reference-prompts.json](tripo-wall-reference-prompts.json). Tripo job IDs and generation presets were not included in the exports, so they are recorded as unavailable. This integration used no Tripo or AutoSprite generation credits.

`WallKitTests` checks all seven assets, bounds within .01 tile, the geometry budget, atlas UVs, a clear doorway using rays through the actual triangles, and lamp-only emission. Additional checks reject broad-face yaw, incomplete or mismatched end profiles, and a lamp body that differs from the straight panel. Five-module normal and cutaway runs are rendered from the fixed game angle and directly above in `docs/qa/walls-2026-10-04/wall-kit-*-{run,top}.png`. Game-level collision, cutaway, placement and screen captures are validated separately.
