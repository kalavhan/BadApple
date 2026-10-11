# Haunted hotel UI — round two review

The supplied fixes are implemented on `codex/haunted-hotel-ui`: permanent top lift indicator, uninterrupted lintel, safe drip paths, shared goo colors, pulse-driven Summon halos and bezel, core-connected lips, and corrected wallet/register baselines. [Implementation notes](../../HAUNTED_TRAY.md) describe the geometry and behavior.

## Tray states

| Canvas | Default | Cannot afford | Blocked | Faith | Wallet 45 | Wallet 1,240 |
| --- | --- | --- | --- | --- | --- | --- |
| 1280×720 | [View](haunted-1280-default.png) | [View](haunted-1280-unaffordable.png) | [View](haunted-1280-blocked.png) | [View](haunted-1280-faith.png) | [View](haunted-1280-wallet-45.png) | [View](haunted-1280-wallet-1240.png) |
| 1600×720 | [View](haunted-1600-default.png) | [View](haunted-1600-unaffordable.png) | [View](haunted-1600-blocked.png) | [View](haunted-1600-faith.png) | [View](haunted-1600-wallet-45.png) | [View](haunted-1600-wallet-1240.png) |
| 960×720 | [View](haunted-960-default.png) | [View](haunted-960-unaffordable.png) | [View](haunted-960-blocked.png) | [View](haunted-960-faith.png) | [View](haunted-960-wallet-45.png) | [View](haunted-960-wallet-1240.png) |

Compact layout: [1160×720 default](haunted-1160-default.png).

## Permanent lift indicator

| Canvas | Setup | Find a room | Night | Endless | Results | Monster |
| --- | --- | --- | --- | --- | --- | --- |
| 1280×720 | [View](haunted-1280-dial-setup.png) | [View](haunted-1280-dial-no-room.png) | [View](haunted-1280-dial-night.png) | [View](haunted-1280-dial-endless.png) | [View](haunted-1280-dial-results.png) | [View](haunted-1280-dial-monster.png) |
| 1600×720 | [View](haunted-1600-dial-setup.png) | [View](haunted-1600-dial-no-room.png) | [View](haunted-1600-dial-night.png) | [View](haunted-1600-dial-endless.png) | [View](haunted-1600-dial-results.png) | [View](haunted-1600-dial-monster.png) |
| 960×720 | [View](haunted-960-dial-setup.png) | [View](haunted-960-dial-no-room.png) | [View](haunted-960-dial-night.png) | [View](haunted-960-dial-endless.png) | [View](haunted-960-dial-results.png) | [View](haunted-960-dial-monster.png) |

## Summon beat

[10-second recording](haunted-beat.mp4) · [Rest frame](haunted-1600-glow-rest.png) · [Peak frame](haunted-1600-glow-peak.png).

The [verified recording](recording-verification.json) contains 300 actual 1600×720 frames at 30 fps, rendered offline with the editor-only presentation clock. Peak/rest images are selected from those frames using their measured glow levels. Gameplay is held still; the actual HUD, placement rules, assets and drip simulation are used.

The [capture report](haunted-report.json) contains 39 screenshots, 10 successful input assertions and 4 successful animation assertions. Recorded glow ranges from 0.407 to 1.138; the smallest normal Summon release gap is 1.300s and the average primary beat is 2.708s. Every complete primary beat is within 2.5–3 seconds, and each release has a synchronized glow peak.

## Validation and APK

The [full Unity graphics suite](editmode-results.xml) passed **231/231** tests with no skips. Coverage includes safe geometry, baseline flavor wrapping with the real font, pulse math, particle limits, release timing, phase indicator layout and the existing game suite. The capture checks delayed Summon, repeat presses, affordability changes, cancellation, all five type callbacks and card drag/release.

The [Android APK](../../../Builds/BadAppleHotel-haunted-fixes.apk) is `1.32-haunted-fixes` / code `33`, a Development IL2CPP build for ARM64 and x86_64 (194.74 MiB). [Build verification](android-build-verification.json) checks the package, debug signature and source/asset hashes. The installable APK is an ignored local artifact.

SHA-256: `8ccd31920083898461fc86b7dad24ebe9f9b339999390414047c18d633a0a421`.

No Android device was connected. APK installation, on-device frame time and the phone profiler check remain unverified; software-rendered editor captures do not establish real-time 60 fps. The report preserves any Unity editor startup diagnostics separately from game errors.
