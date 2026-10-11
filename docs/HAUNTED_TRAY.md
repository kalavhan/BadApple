# Haunted hotel summon tray

The haunted UI is implemented on `codex/haunted-hotel-ui`, based on main at `bdc1033`. Round two follows the supplied fixes package: a permanent top lift indicator, an uninterrupted lintel, clear drip paths, a drip-driven Summon glow and consistent text baselines. The tower and monster windows retain their existing skin pending later review phases.

## Top lift indicator

The night dial sits at the screen's top safe edge in Setup, Night, Endless and Results, for both roles. It points to the upcoming night during setup and the last played night in Results. The countdown plate shows CHECK-IN, NIGHT or ENDLESS with the relevant time; Results hides the plate. Monster numeral glows use Fear pink. A target change flashes the numeral and the damped needle overshoots once; phase changes swing the hanging plate.

`LiftDialLayout` shares geometry between drawing, hit blocking and tests. The housing includes the 200-pixel sill; its decorative wings are omitted on narrow screens. Banners begin 128 pixels below the safe top and window-open toasts begin at 180.

## Tray and input

The tray remains 360 canvas pixels tall, 922 wide at 16:9 and at most 1040 wide at 20:9. It retains plate previews, range rings, all five type tabs, tap/drag selection, affordability countdowns and blocked-path handling.

Summon queues a 0.22-second press animation. The existing build operation rechecks funds and placement when the delay ends. Duplicate presses cannot buy twice; closing or changing selection cancels a pending purchase. A successful purchase completes the tray's close slide before selection clears.

The lintel extends over both gates, which begin below its continuous four-pixel brass edge. All three lintel roots and landing heights come from `HauntedTrayLayout`: A falls through the first cubby gap, B between cabinet and register, C between register and type rail. The spider uses another gap, or a cabinet edge on layouts with fewer cubbies. C no longer feeds the button.

## Goo and Summon pulse

`GameHUD` exposes `Ectoplasm Tuning` and `Summon Glow Tuning` in the Inspector. Goo uses deterministic 1/120-second physics with independently seeded anchors. Normal releases stay at least 0.4 seconds apart; a Summon press deliberately releases both button drips together. Closing stops new releases and clearing selection clears the particles.

Every body color goes through `GooAt`. The old length-dependent stem glow is replaced by fixed root and bulb glows. Lintel pools shrink briefly after release, and curved shoulders join them to the stems. Summon roots begin inside the mint core, with rounded lips spilling over the brass.

Summon has dedicated refill/swell durations and staggered button drips. The lintel scheduler leaves release windows for the steady 2.5–3-second button beats. Halo and bezel intensity follow Summon 1's current state, with a secondary flash for Summon 2 and press feedback. The two alpha-blended nine-slice halos sit behind Cancel. Unaffordable and blocked buttons turn off their halo and lit rim while existing stems retract.

The renderer keeps two GL batches and caps at five drips, eight droplets, five puddles and twenty specks. Contour and tiny-droplet tessellation adapt to stay below 600 submitted vertices. `UiAlpha` is an explicit Resources shader for Android packaging. Assets, fonts, styles and nine-slices are cached; drawing runs only on Repaint.

## Type and responsive details

Wallet text is one right-aligned run: a jar, seven-pixel gap, label, seven-pixel gap, then value. All text shares the specified baseline, including Faith's violet value. The register uses explicit row baselines; all tower flavor lines fit at most two lines at Cormorant italic 13, including the compact register.

Existing readability adaptations are retained: use the full-width fallback below an 800-pixel control gap, use a 240-pixel register at compact widths, omit surplus empty cubbies, and stack the Summon cost on narrow buttons. Blocked warnings move the buttons down 12 pixels with 44-pixel targets. The stamp remains on the cabinet's lower rail. Rotations compose before the HUD scale.

## Assets and validation

`tools/art/export_haunted_ui.py` exports the supplied kit's SVG geometry and the new lift housing, wing, halos and lit bezel. Run it with Python/Pillow and Chromium. Intermediate SVGs and the audit atlas go to ignored `ArtSource/UI/Haunted/`. Font sources and licenses are under `Assets/Resources/Fonts/`.

Use Unity 6000.3.24f1. The capture fixture renders the production HUD over a seeded hotel room, retaining real affordability and path-blocking rules while freezing gameplay for comparisons. The 10-second beat clip renders 300 actual frames at 30 presentation steps per second; an Editor-only `HauntedClock` override makes offline recording independent of host rendering speed. Player builds always read Unity's unscaled time.

```sh
xvfb-run -a -s '-screen 0 1920x1080x24' Unity -batchmode -force-glcore -projectPath "$PWD" -runTests -testPlatform EditMode -testResults /tmp/haunted-editmode.xml -logFile /tmp/haunted-editmode.log
BADAPPLE_HAUNTED_OUT="/tmp/haunted-r2-qa/captures" BADAPPLE_HAUNTED_RECORD=1 xvfb-run -a -s '-screen 0 1920x1080x24' Unity -projectPath "$PWD" -executeMethod BadAppleHotel.EditorTools.HauntedTrayCapture.Run -logFile /tmp/haunted-capture.log
BADAPPLE_APK_PATH="$PWD/Builds/BadAppleHotel-haunted-fixes.apk" Unity -batchmode -nographics -quit -projectPath "$PWD" -executeMethod BadAppleHotel.EditorTools.AndroidBuild.BuildTestApk -logFile /tmp/haunted-build.log
```

Fresh screenshots, pulse telemetry, test reports and the video are in [the round-two review bundle](qa/haunted-tray-r2-2026-10-11/README.md). The APK is a Development IL2CPP build for ARM64 and x86_64. Phone installation and performance require a connected test device; offline editor captures do not establish real-time 60 fps.
