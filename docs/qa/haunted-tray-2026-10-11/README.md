# Haunted tray review

Phase one of the attached haunted-hotel UI specification. Implementation and intentional layout adjustments: [Haunted tray](../../HAUNTED_TRAY.md).

The images show the production Unity IMGUI HUD over a seeded hotel room, with the gameplay simulation held still for reproducible comparisons. Affordability, path blocking and resource currencies use the actual game rules.

| Canvas | Default | Cannot afford | Blocked | Faith | Endless |
| --- | --- | --- | --- | --- | --- |
| 1280×720 (16:9) | [View](haunted-1280-default.png) | [View](haunted-1280-unaffordable.png) | [View](haunted-1280-blocked.png) | [View](haunted-1280-faith.png) | [View](haunted-1280-endless.png) |
| 1600×720 (20:9) | [View](haunted-1600-default.png) | [View](haunted-1600-unaffordable.png) | [View](haunted-1600-blocked.png) | [View](haunted-1600-faith.png) | [View](haunted-1600-endless.png) |
| 960×720 (full-width fallback) | [View](haunted-960-default.png) | [View](haunted-960-unaffordable.png) | [View](haunted-960-blocked.png) | [View](haunted-960-faith.png) | [View](haunted-960-endless.png) |
| 1160×720 (compact columns) | [View](haunted-1160-default.png) | [View](haunted-1160-unaffordable.png) | [View](haunted-1160-blocked.png) | [View](haunted-1160-faith.png) | [View](haunted-1160-endless.png) |

The [full graphics test run](editmode-results.xml) passed all 207 tests with no skips. After the final clock and layout changes, the [focused haunted suite](haunted-focused-results.xml) passed all 25 cases, also with no skips. A headless trial passed 200, skipped six graphics tests, and could not produce valid pixels for the existing wall-depth test; all seven rendering cases passed with the OpenGL renderer enabled.

The [capture report](haunted-report.json) passed all ten interaction assertions: delayed placement, exactly one charge on repeated presses, changed affordability during the delay, cancellation, all five actual type-rail callbacks, and cubby drag/release. It reports no gameplay errors; an unrelated Unity Search database startup exception is recorded separately.

The [15-second drip recording](haunted-drips.mp4) contains 450 actual rendered frames at 1600×720, played at 30 fps. An editor-only presentation clock advances one fixed step per captured frame; the footage is rendered offline and does not measure real-time performance.

Phone performance is not established by these captures. No phone was connected; the editor used Mesa llvmpipe software rendering. The development APK is intended for the remaining on-device 60 fps/Frame Debugger/Profiler check.

The [development APK](../../../Builds/BadAppleHotel-haunted-tray.apk) is version `1.31-haunted-tray` / code `32`, IL2CPP for ARM64 and x86_64 (200.25 MiB). The [build verification](android-build-verification.json) confirms its debug signature and that all 72 runtime source files match this implementation. The APK is an ignored local build artifact.

SHA-256: `be091342f93c3ac2824dffd30b632dc9c8369245d13b74d39e96dffeac534444`.
