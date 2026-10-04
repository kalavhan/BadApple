# Characters and animation

Bad Apple Hotel has **7 resident characters** and **3 monsters**. All of them are drawn in the same dark, comic,
Tim Burton style (Autosprite) and animate in **8 directions** with **idle, run and attack** cycles.

## The roster

Defined in `Assets/StreamingAssets/Config/residents.json` (`roster`). Edit the JSON to rename someone or change a bio;
no code changes needed.

| id | name | who they are |
|----|------|--------------|
| `hana` | Hana Seo | Investigative reporter with a hunch that this hotel hides a secret |
| `dexter` | Dexter Pruitt | Armchair occultist who is sure he understands it better than anyone |
| `fenwick` | Prof. Fenwick Quill | Old scientist whose inventions all aim to prove the paranormal is real |
| `mando` | Armando "Mando" Ruiz | Office worker who won a ticket, scared, wants to go home and finish his work |
| `scarlett` | Scarlett Voss | Goth slasher-movie addict, eager to meet the paranormal |
| `stahl` | General Viktor Stahl | Wants to weaponize the next paranormal thing for his fictional country |
| `cornelius` | Cornelius Ashgrove | A real occultist, dressed Victorian, thrilled about the meeting |

The player picks one on the main menu (or "Random"); the other seats are filled from the rest of the roster.

Monsters keep their ids from `monsters.json`: `stitchwork_chef`, `moldy_matron`, `bellhop_wraith`.

## Shirt colour

Every resident wears a **magenta shirt** in the art. A small shader (`Assets/Resources/Shaders/CharacterSprite.shader`)
hue-shifts anything in the magenta band to the seat's colour, so one set of art gives six different shirt colours.
Seat colours are `shirtHues` in `residents.json` (0 to 1 hue values). Monsters pass `-1`, which turns the shift off.
The same shader has a `_Flash` value used for hit flashes.

## Files

```
Assets/Resources/Art/Chars/<id>_idle.png    one atlas per animation
Assets/Resources/Art/Chars/<id>_run.png
Assets/Resources/Art/Chars/<id>_attack.png
Assets/Resources/Art/Chars/<id>.json        frame size, pivot, standing height, frame counts
```

Atlas layout: **8 rows = directions** (top to bottom: east, north-east, north, north-west, west, south-west, south,
south-east), **8 columns = frames**. Atlases are palette PNGs to keep the repo small; Unity compresses them on import
(`Assets/Editor/ArtImporter.cs`, `Chars` rules: no mipmaps, 2048 max).

## Code

* `Assets/Scripts/Game/Art/CharacterArt.cs`
  * `CharacterSet.Load(id, worldHeight)` slices the atlases into sprites (cached).
  * `CharacterAnimator` plays them: direction comes from the facing vector, loops idle/run, one-shots attack.
    The game calls `Drive(facing, moving, attack)` every frame.
* Residents: `GameManager.PlaceResidentSprite`. They swing (attack) when one of their towers fires while they are awake.
* Monster: `GameManager.UpdateMonster`. Attacks while biting a resident or smashing a door.
* If an id has no art, the game falls back to the old single-image sprite, so a missing character never breaks a match.
* The art switch (`Sprites.UseArt`, see `SPRITES.md`) also turns the animated characters off.

## Making or replacing a character

1. Make the character in Autosprite (`create_character`, comic style). Give them a magenta shirt if the shirt should be recolourable.
2. Run `generate_isometric_pack` three times: `idle`, `run`, `attack` (8 frames, 256 px). Each call renders all 8 directions.
3. Download the three zips into `ArtSource/Anim/zips/<id>_<anim>.zip`.
4. Run `python3 tools/art/chars.py <id>` (set `SCALE=0.6` for the default atlas size). It crops all frames to one shared box,
   scales, packs the atlases and writes the json.
5. Add the character to `roster` in `residents.json` (or a monster to `monsters.json`, using the monster id as the art id).
