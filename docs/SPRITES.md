# Sprites: how they work and how to switch them

## The idea
Every picture in the game is requested through `Assets/Scripts/Game/Art/Sprites.cs` by a *key*
(for example `tower_gun_turret`, `bed3`, `monster_moldy_matron`). For each key the game does:

1. If art is switched on, look for a PNG in `Assets/Resources/Art/` and build a sprite from it.
2. If art is off, or that PNG does not exist, draw the old placeholder pixel sprite in code.

So art and placeholders live side by side, per sprite.

## Switching between art and placeholders
- **In the editor:** menu **Bad Apple Hotel > Use Autosprite Art** (a check mark means art is on).
- **From code:** `Sprites.UseArt = false;` (or `true`). Saved in PlayerPrefs under `BadAppleHotel.UseArt`, default on.
- **Per sprite:** delete or rename one PNG in `Assets/Resources/Art/`; only that sprite falls back to the placeholder.
- Changes show from the **next match** (sprites already on screen keep their look until recreated).

## File names (all in `Assets/Resources/Art/`)
| Thing | File(s) | Size in world units (1 = one tile) |
|---|---|---|
| Floors, wall | `floor_room`, `floor_corridor`, `wall` | exactly 1 tile, tileable |
| Build plate | `build_plate` | 0.96 |
| Beds | `bed_1` ... `bed_6` | 1.85 tall; per-level pillow anchors in `beds.json` |
| Doors | `door_wood` (levels 1-3), `door_reinforced` (4-6), `door_iron` (7+), `door_open`, `door_broken` | 1 |
| Towers | `tower_<towerId>` e.g. `tower_gun_turret` | 1.1 to 1.4 (table in `Sprites.cs`) |
| Residents | `resident_0` ... `resident_5` (one per pajama color) | 1.5 tall, pivot at feet |
| Monsters | `monster_<monsterId>` e.g. `monster_moldy_matron` | 2.0 tall, pivot at feet |
| Ghost, body parts | `ghost`, `part_arm`, `part_leg`, `part_torso`, `part_eye` | 1.2 / 0.75 |

Still placeholders (no PNG yet): projectiles, the apple icon, build-slot marks, range rings.

## Replacing or adding a sprite
1. Make a transparent PNG with the file name above and drop it in `Assets/Resources/Art/` (replace the old one).
2. `Assets/Editor/ArtImporter.cs` sets the import options (uncompressed, mipmaps, smooth) automatically.
3. Size comes from the table above, not the image, so any resolution works (about 128 px per world unit is plenty).
4. New tower or monster: just name the file `tower_<id>` / `monster_<id>` using the id from the JSON config.
   A new kind of sprite needs a line in `ArtFor()` in `Sprites.cs`.

## How the current art was made
Autosprite, style "comic", prompts asking for a dark Tim Burton look (gothic, wonky, spindly, muted purples),
white background. `tools/art/process_sprites.py` removes the white background (keeping whites inside objects),
crops, resizes, recolors the resident's pajamas into 6 colors and makes the floor textures tile.
Raw 1024 px generations are kept outside `Assets` in `ArtSource/` (gitignored).

## October 4 static tiers and hotel kit

`Tiers/<towerId>_1` through `_4` provide four static upgrade designs per upgradeable family.
`towers.json` chooses the sprite and stats for each tier; the crystal ball remains single-level.
The designs use one fixed world size per family. Tower animations are deferred.

`Hotel/` holds the hallway floor, wall top/face/corner/baseboard, doorway frame, and four decorations.
`HotelArt` selects these separately from the room textures; layout neighbors determine wall edges.

`tools/art/prepare_static_tiers.py` rebuilds the imported images from the locally retained static sheets.
See [the implementation and credit ledger](PLAN-OCT04.md) for controls, configuration, and validation.
