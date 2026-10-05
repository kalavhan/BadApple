# Wall sight and player control playtest — October 5, 2026

Historical snapshot: the [central-room and lamp-coverage iteration](../central-lamps-2026-10-05/README.md) supersedes this layout, APK and current validation results. Route measurements below apply to this earlier generator.

This update starts from merged PR #3. It keeps ten rooms and the build-space distribution of 30, 26×3, 23×3 and 20×3.

## Behavior

- Outer pilasters rotate 45 degrees left while retaining the same thin footprint and atlas UVs. Structural separator backing sits behind the decorative faces and below the crowns, with flush joins between solid neighbour cells.
- In Cutaway, an occupied room has lowered camera-near walls and full camera-far walls. Doorway jambs follow the entrance orientation. Concave entrance returns also lower when their projected area hides part of the room floor. Occupancy takes priority over selection; explicit Up/Down remains available.
- A monster in the adjacent corridor cell can see through at most three consecutive wall squares: its own and the two neighbours. The window stops at corners and doors and closes when the monster moves away. The resident cannot track a remote outside monster; the monster cannot reveal a remote room interior. Sight windows do not change wall collision or allow shots through solid walls.
- Wall sconces appear periodically in corridors and rooms, including at least one full-height lamp in each tested room. A static occluded illumination field adds warm light to wall textures, floors and characters, with fog gating. Doors conservatively block the static light field; opening a door does not rebake illumination.
- The human remains asleep during door attacks and bites until the Wake action is chosen. Death and monster reveal still end the resident state. Bots retain their existing danger response.
- Personal shots require a fresh Shoot button/F press, an awake living resident, a visible uncloaked monster within four tiles, a clear physical firing line and a 0.6-second cooldown. Each shot deals eight base bullet damage. Towers retain automatic fire without triggering the human's attack animation.
- The map envelope changes from 104×64 to 88×54. Camera framing and movement speed are unchanged. Across 20 identical seed inputs and 900 room pairs per map size, average corridor travel falls from 65.9178 to 56.1422 tiles: **14.8299% shorter**. This is an average, not a guarantee for every generated map. See the route CSV and summary.

## Reproduce and evidence

Use Unity 6000.3.24f1 and run EditMode tests with a graphics device. The baked wall kit is sufficient; no raw Tripo import or additional asset credits are required.

`BadAppleHotel.EditorTools.WallCapture.Run` renders nine geometry scenarios at each of 1560×720 and 960×720. Set `BADAPPLE_QA_WIDTH` and `BADAPPLE_QA_OUT`. Geometry inspection fixtures expose the room; corridor/walking fixtures retain ordinary fog. JSON reports include those visibility modes and rendering measurements.

`BadAppleHotel.EditorTools.WallBehaviorCapture.Run` captures six Night gameplay fixtures with ordinary fog: resident/monster adjacent and away, sleeping during a real bite, and awake with Shoot available. The JSON asserts exact local clipping, unaffected run height/fade, actor visibility and control availability. A known pre-fixture Unity Editor SearchDatabase startup exception is preserved under `editorStartupDiagnostics`, separately from gameplay errors; it is not an Android runtime error.

The corner, lighting field, GPU lighting probe and core backing comparisons document the corrected surface placement and warm illumination. Route data documents the smaller layout without changing room budgets.

`BadAppleHotel.EditorTools.AndroidBuild.BuildTestApk` builds the release-player, debug-signed test APK. Set `BADAPPLE_APK_PATH`. Version **1.4-wall-sight**, code **5**, IL2CPP ARM64/x86_64, OpenGLES3; native engine stripping remains disabled to preserve the earlier GUI startup fix. Exact APK size and SHA-256 are in `apk.json`.

## Validation results

- Complete integrated EditMode suite: **102/102 passed**, zero skipped or failed, including Android shader compilation/build safeguards, view-only peeking, six personal-control tests, all room wall modes, real core backing meshes and resource cleanup. See `editmode-tests.xml`.
- Eighteen geometry captures and six gameplay behavior captures passed. Gameplay assertions recorded no errors; the known editor-only startup diagnostic is retained as described above.
- Across 20 complete hotels, active wall submissions including core batches were **19–22 Up**, **19–23 Cutaway**, and **18–22 Down**, before camera culling. Editor values do not measure Android phone FPS.
- Final APK built successfully and passed ZIP integrity verification. Android 11/API 30 x86_64 software-emulator smoke reached the menu, resident setup and Night gameplay; joystick and wall-mode inputs were exercised. Wall textures, lamp lighting and HUD rendered without Unity/AndroidRuntime exceptions or shader errors in the captured log. The emulator briefly showed Android system-busy dialogs during boot; choosing Wait let it recover. This is a functionality check, not a timing benchmark.

Editor and software-emulator timing cannot establish phone performance. A physical Android playtest is still needed for frame-rate measurements.
