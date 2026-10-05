# Hotel walls validation

The wall update uses the seven Tripo models imported by Josue. Hallway furniture and paintings are deferred.

## Reproduce

Use Unity 6000.3.24f1. Run `EditMode` tests with a graphics device (the sprite-depth tests render into a target). The wall importer is an explicit editor command: **Bad Apple → Art → Bake imported wall kit**. A normal checkout uses the committed baked assets and does not require the large source FBXs or the Tripo bridge.

`BadAppleHotel.EditorTools.WallCapture.Run` produces gameplay captures and measurements. Set `BADAPPLE_QA_WIDTH=1560` or `960` for 19.5:9 or 4:3, both at 720 pixels high; `BADAPPLE_QA_OUT` selects the output directory. Some geometry captures deliberately enable the existing clairvoyance mechanic so the whole room is inspectable. Corridor and walking captures retain ordinary fog; each capture records its visibility fixture.

`BadAppleHotel.EditorTools.AndroidBuild.BuildTestApk` creates the release-player, debug-signed APK. Set `BADAPPLE_APK_PATH` to select the destination. ARM64 and x86_64 are included, using OpenGL ES 3. Native engine stripping stays disabled because the earlier Android startup failure involved Unity's built-in GUI skin.

## Implementation

- A boundary graph owns straight runs, unique corner footprints and filled one-cell separators. Thickness defaults to 0.3 tiles, entirely on the solid side of the floor edge.
- The same footprints drive radius-aware navigation and continuous collision: 0.3 tiles for residents, 0.38 for monsters. Closed doors and buildings remain dynamic obstacles.
- Up, Cutaway and Down are available from the HUD. Cutaway is the default. Camera-ray tests keep floor centers visible; selected and occupied rooms and nearby hallway occluders participate in dynamic lowering/fading.
- A small state texture carries independent run heights/fades into the vertex shader. Targets update every 0.1 seconds and interpolate over 0.15 seconds. Related corner/core states follow the lowest incident run.
- Full and lowered kit variants share a texture atlas. The low variant retains finished wainscot/caps; door lintels clip away instead of being compressed across the opening.
- Wall placement uses proper rotations in all four directions. The importer removes the straight source model's unintended yaw, squares modular ends, and uses the same panel body beneath the imported lamp so crown, rail and base profiles continue through each join.
- All repeated kit pieces, including corners and sconces, use GPU instances of seven shared meshes and one atlas material. CPU frustum culling and active-height filtering skip off-camera and inactive variants. Only small separator cores are combined into spatial mesh batches; their CPU mesh copies are released in players. No real-time lamp lights are added. Emission obeys fog.
- The player-build hook preserves instancing shader variants for runtime-created materials. The wall shader explicitly targets shader model 3.5; Android uses OpenGL ES 3.
- Sprites write clipped depth against the wall meshes. Ground-plane oval shadows follow characters and towers.
- Hallway carpet uses a seamless pixel-stepped hexagon pattern; rooms retain wood flooring and a rug within the reserved two-square bed footprint.

## Results

- Complete EditMode suite: **67/67 passed**, including 100-seed boundary/visibility checks, 300 movement routes over 20 seeds, room budgets, setup continuity, sprite depth, all four placement rotations, real panel cross-section continuity, Android shader compilation/build safeguards, and 20 complete hotels with the imported kit. See `editmode-tests.xml`.
- Kit continuity also passed its focused **7/7** checks. The four `wall-kit-*-run/top.png` images show five repeated modules from the game angle and directly above, including the lamp and lowered caps.
- Eighteen captures passed at 1560 × 720 and 960 × 720: L-shaped room, corridor corner, doorway, solid separator, walking along a wall, Walls Up, Walls Down, and close views along both wall axes. Geometry fixtures expose the full scene; corridor/walking fixtures keep gameplay fog. No capture errors.
- Captured wall draw submissions: **2–7**, with **21,596–101,909** submitted triangles. The capture hotel registers 30 batches when both mutually exclusive full/low variants are counted. The integration test checks the actual active variants across 20 complete hotels: **20–24** submissions in Up, **21–25** in Cutaway, and **19–24** in Down, including every core batch and with no camera culling. The production shader and C# buffers use batches of 128, within the GLES3 16KB minimum uniform-block budget. Unity may split instanced submissions internally; full-frame engine counts, including UI and sprites, are recorded separately in each JSON.
- Each baked piece stays within the 1,500-triangle target. The straight panel reserves part of that budget for the lamp variant's imported sconce. The 20-map integration test requires shared source meshes, fewer than 5,000 generated core vertices, and disposal of every owned mesh, texture and material. This prevents repeated models from producing hundreds of megabytes of duplicated geometry.
- Final APK smoke test on the Android 11/API 30 x86_64 emulator: menu, resident match, joystick input and all three wall modes exercised; corrected wall meshes visible and no Unity/Android runtime exceptions or shader errors in the captured log. The `android-*.png` captures use normal gameplay visibility.
- APK built successfully with IL2CPP for ARM64 and x86_64. Version `1.3-hotel-walls`, code 4; release player, debug signed. Exact size and SHA-256 are recorded in `apk.json`.

The renderer also has a narrow compatibility path for the tested Android emulator’s legacy SwiftShader 4.0 GLES3 renderer. That driver reports instancing support but fails even a two-instance, matrix-only, solid-color control. Ordinary shared-mesh draws render correctly. The compatibility path retains shared source geometry but uses more draw calls; it is not covered by the instanced draw-count target.

## Scope of measurements

Editor timing and draw counts are diagnostic only. They do not establish 60 FPS on an Android phone. Physical-device performance still needs a phone playtest.
