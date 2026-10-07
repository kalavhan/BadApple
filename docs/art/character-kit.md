# Imported 3D residents — October 6, 2026

All seven residents now render Josue's Tripo models instead of the Autosprite sprite sheets. Monsters still use sprites.

| Roster art id | Character | Tripo export folder | Source → baked triangles |
| --- | --- | --- | ---: |
| stahl | General Viktor Stahl | `military_officer_figure_3d_model` | 18,886 → 6,000 |
| fenwick | Prof. Fenwick Quill | `mad_scientist_3d_model` | 19,536 → 5,999 |
| hana | Hana Seo | `press_journalist_3d_model` | 20,650 → 5,999 |
| dexter | Dexter Pruitt | `stylized_character_3d_model` | 24,900 → 6,000 |
| mando | Armando Ruiz | `office_worker_3d_model` | 22,057 → 6,000 |
| scarlett | Scarlett Voss | `goth_girl_3d_model` | 26,439 → 5,999 |
| cornelius | Cornelius Ashgrove | `victorian_gentleman_3d_model` | 29,087 → 6,000 |

The generic export names were matched to the roster by rendering each model against the references in `docs/art/characters`. The importer also accepts each manifest's import folder (`character_stahl` and so on), which takes priority.

**Bake.** Bad Apple → Art → Bake imported character models (batch: `BadAppleHotel.EditorTools.CharacterKitImporter.BakeBatch`). The importer reduces each mesh to the 6,000-triangle budget from the character manifests with the bundled UnityMeshSimplifier (UV foldovers preserved), maps source axes (X width, Y up, face toward +Z) to game axes (XY floor, face toward +Y, negative Z up) with the feet at the origin, and writes `Assets/Resources/Art3D/Characters/<id>.asset`, a 1024² albedo and `HotelCharacterKit.asset`. The originals are not referenced at runtime.

**Runtime.** `CharacterModel` replaces the sprite animator when the roster art has a model. Residents stand 1.5 tiles tall and use the `HotelSurface` lighting. The resident's SpriteRenderer remains the logical anchor for visibility, the contact shadow and sorting. The model hides when the anchor is hidden or shows the ghost sprite, and it is removed with the anchor (for example when the hidden monster transforms).

**Animation.** Josue's Mixamo downloads (FBX without skin, `mixamorig` bones) live loose in `Assets/TripoModels/`: `Idle`, `Idle-2`, `Walking`, `Running`, `Running-scared` and `Lying Down`. `CharacterAnimationImporter` (run by the same bake) sets each to Humanoid, keeps forward travel as root motion that the Animator ignores (baking it into the pose made bodies drift ahead and snap back each loop), copies the clips to `Assets/Resources/Art3D/Characters/Animations/*.anim`, and rebuilds `ResidentAnimator.controller` in place. `Attack.fbx` and `Hit Reaction.fbx` are picked up automatically when added. Humanoid retargeting lets every rigged resident share these clips.

A resident is rigged when one of its candidate folders, or Tripo's `_1`, `_2` re-export of one, contains a skinned mesh; the newest wins. All seven residents are rigged (65 mixamorig bones each): Stahl `military_officer_figure_3d_model_1`, Fenwick `mad_scientist_3d_model_1`, Hana `press_journalist_3d_model_1`, Dexter `stylized_character_3d_model_1`, Mando `office_worker_3d_model_1` (`_2` is an identical duplicate), Scarlett `goth_girl_3d_model_1` and Cornelius `victorian_gentleman_3d_model_1`. The bake sets each export to Humanoid, reduces the skinned mesh to 6,000 triangles with bone weights kept, uses the export's own texture (a re-export has a new UV layout), measures height from the bind-pose mesh (some exports are saved crouched or mid-stride), and saves `<id>_rig.prefab` with the avatar and controller.

Each rig is validated by sampling the Run clip: a sound skin stays about 90% of its bind height mid-run, otherwise the bake keeps the static model and removes the rig's assets. The earlier `steampunk_gentleman_3d_model` was exported as 35 separate Tripo parts whose shoes, cuffs and coat panels detached under animation; it is no longer a Cornelius candidate. Exports must be a single mesh (part segmentation off).

`CharacterModel` turns the rig into game axes and plays Idle when standing, Walk below about 2 tiles/s, Run above, and Running-scared once the monster is revealed, matching clip speed to ground speed. In bed the slowed Idle is laid flat on the mattress.

Residents without a rig keep procedural motion: turning to face the walking direction, a walking bob, sway and forward lean, a brief lean for attacks, and lying on the back on the bed with the head at the level's pillow anchor and the back on the measured bed surface.

Not yet implemented: per-seat shirt colours (the models keep their magenta), the head-socket psychic beam, and 3D monsters.

`CharacterKitTests` checks that every roster resident has a model with floor contact, centered pivot, triangle budget and UVs.
