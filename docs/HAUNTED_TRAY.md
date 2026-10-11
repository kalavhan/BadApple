# Haunted hotel summon tray

The first haunted UI phase replaces the resident summon tray with the supplied wood, brass, damask, cubbies, toe tags, register and lift dial design. Other windows retain DreamSkin until subsequent review phases. This branch starts from main at `bdc1033`.

## Behavior

The tray remains on the 720-high HUD canvas, with its existing plate preview, range rings, type selection and card dragging. Cards use income countdowns when unaffordable. Blocked plates show a NO VACANCY stamp and a paper warning. The wallet and costs follow the selected tower's currency. Tower flavor is separate from the factual description in configuration.

Summon queues a 0.22-second press animation. The existing build operation rechecks funds and placement when that delay ends. Duplicate presses cannot queue duplicate purchases, and closing or changing selection cancels a pending purchase. After a successful purchase the tray completes its close slide before the selection clears.

The crest displays the current night, the upcoming night outside night phase, and VI/ENDLESS from night six in endless mode. Its needle uses an unscaled damped spring.

## Motion and performance

`GameHUD` exposes `Ectoplasm Tuning` in its Inspector. Defaults follow the supplied specification. Each anchor owns an independently seeded state machine; falling droplets use fixed 1/120-second gravity integration. Releases are at least 0.4 seconds apart except the two deliberate Summon press drops. Lintel C feeds the button drips. Closing stops releases, and clearing selection clears the simulation.

The renderer caps itself at five drips, eight droplets, five puddles and twenty specks, with two GL batches. It samples the fourteen-row silhouette more sparsely during dense splashes to stay below 600 submitted vertices. The included `UiAlpha` resource shader prevents the body material from being stripped from Android builds. Texture/font/nine-slice styles are cached and the tray draws only on Repaint.

## Responsive adjustments

The desktop references in the spec have conflicting dimensions in a few tight areas. The implementation keeps the normal 922-pixel tray at 16:9 and 1040-pixel tray at 20:9, with these readability adjustments:

- Use the full-width fallback below an 800-pixel control gap. The stated 640-pixel threshold leaves only ~50 pixels per cubby while retaining the register and rail.
- At narrower non-fallback widths, omit excess empty cubbies and use a 240-pixel register. Stack the Summon cost beneath its label when its button is narrow.
- Toe tags are 54 pixels tall normally and 64 when a wrapped shortfall/countdown needs two lines. Font sizes remain at least 11 pixels.
- Place the register stamp on the cabinet’s lower rail. The tightly packed register otherwise hides NO VACANCY behind the warning or overlaps the Summon label.
- For Blocked, move the buttons down 12 pixels and use 44-pixel heights to make room for the warning slip. All targets remain at least 44 pixels.
- Compose rotations in canvas coordinates before the existing HUD scale, so geometry and axis-aligned input stay aligned at non-unit screen scales.

## Assets

`tools/art/export_haunted_ui.py` reproducibly exports the kit from SVG geometry transcribed from the supplied mockup. Run it with Python/Pillow and Chromium; source SVGs and the audit atlas are written to ignored `ArtSource/UI/Haunted/`. Only the resulting PNGs and their metadata are included. Static font files, official source URLs and licenses are under `Assets/Resources/Fonts/`.

## Reproduce validation

Use Unity 6000.3.24f1. The editor fixture captures the production HUD in a seeded hotel room; it does not substitute an HTML mockup. It exercises all five actual type-rail callbacks, card drag/release, delayed Summon, duplicate presses, losing affordability during the delay, and cancellation.

```sh
xvfb-run -a -s '-screen 0 1920x1080x24' Unity -batchmode -force-glcore -projectPath "$PWD" -runTests -testPlatform EditMode -testResults /tmp/haunted-editmode.xml -logFile /tmp/haunted-editmode.log
BADAPPLE_HAUNTED_OUT="$PWD/docs/qa/haunted-tray-2026-10-11" BADAPPLE_HAUNTED_RECORD=1 xvfb-run -a -s '-screen 0 1920x1080x24' Unity -projectPath "$PWD" -executeMethod BadAppleHotel.EditorTools.HauntedTrayCapture.Run -logFile /tmp/haunted-capture.log
BADAPPLE_APK_PATH="$PWD/Builds/BadAppleHotel-haunted-tray.apk" Unity -batchmode -nographics -quit -projectPath "$PWD" -executeMethod BadAppleHotel.EditorTools.AndroidBuild.BuildTestApk -logFile /tmp/haunted-build.log
```

The optional recording renders 450 actual frames at 30 presentation steps per second. `HauntedClock` overrides presentation time only in the Editor capture fixture; player builds always read Unity’s unscaled time. The exported video is motion evidence, not a real-time performance measurement.

The APK is a development IL2CPP build for ARM64 and x86_64. Phone profiling remains a separate device check: editor rendering and simulation tests do not establish 60 fps on the test phone.
