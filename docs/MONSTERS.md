# Monsters, minions and the monster UI

State as of Oct 8 2026, branch `claude/project-thread-sb8ifv` (PR #11). Every number below is read from
`Assets/StreamingAssets/Config/*.json` and can be changed there without touching code.

Plan page with the concepts and T-poses: https://claude.ai/artifact/VyKv9AMxLUjAPqxB6W3i2M

## 1. The match, from the monster's side

- One of the seven guests is secretly the monster. During setup (60 s) it plays as a normal resident.
  At the start of night 1 it transforms in its room, and that room becomes its lair: the lair heals 6% HP per second, and banished monsters come back there.
- A match is 6 nights of 90 s. The monster wins if every resident is eaten. The residents win by surviving.
- When the residents banish the monster, it comes back after 10 s and keeps half its eaten body parts.

## 2. The three monsters

Ids stay the old ones, so saves and configs keep working.

| | Maître Gorge (`stitchwork_chef`) | Widow Mildred (`moldy_matron`) | The Night Porter (`bellhop_wraith`) |
|---|---|---|---|
| Role | Bruiser, door breaker | Controller, minion queen | Assassin, infiltrator |
| HP / speed | 980 / 4.0 | 1050 / 4.2 | 900 / 5.2 |
| Weak to | fire ×1.2 (bullets ×0.9) | fire ×1.3 (slow ×0.8) | electric ×1.3, bullets ×1.1 |
| Model | `stylized_pig_humanoid_3d_model` | `mushroom_lady_3d_model` (glides on her idle) | `stylized_ghost_officer_3d_model` |

### Kits

Each monster has three moves. The single-target attack is automatic; the other two are buttons.

| | Single target (auto) | Area (button 1) | Special (button 2, from level 3) |
|---|---|---|---|
| Gorge | **Cleave**: 32 dmg every 1.3 s, doors take ×1.25 | **Flambé** (14 s): 2.5-tile ring, 24 dmg to residents, 30 to shut doors, burning ground 8/s for 3 s | **Meat Hook** (18 s, 7 tiles): eats a body part in clear sight on the spot, or yanks a resident in clear sight 1.5 tiles toward him |
| Mildred | **Parasol Jab**: 22 dmg every 1.0 s; rot for 3 s (door can't be repaired or upgraded) | **Spore Bloom** (16 s): 3-tile cloud for 5 s, 6 dmg/s, residents slowed, sleepers inside earn no Dream Power | **Graveroot** (40 s): a shrine on the nearest rift (within 8 tiles) doubles its next pulse and heals minions within 3 tiles by 4%/s |
| Porter | **Key Ring Lash**: 17 dmg every 0.5 s, every 3rd hit stuns 0.4 s | **Last Call** (16 s): 3.5-tile bell, 25 dmg, towers inside stop firing for 2.5 s | **Do Not Disturb** (45 s): slips through a shut door without breaking it, 4 s inside; towers in that room do +25% to him meanwhile |

- The single-target attack hits residents within 1.1 tiles with nothing solid in between. Otherwise it hits the nearest shut door.
- Hits wake sleeping bot residents. A human player stays asleep until they wake up themselves.
- Ability ranks II and III give +25% and +50% damage. The area attack and special also get cooldown ×0.88 and ×0.76.
- Utility abilities from the old pool are learned at levels 6 and 12: Jam, Rampage, Blackout, Lunge, Cloak, Sticky Slime, Nightmare Gaze.

## 3. Fear: the monster's only currency

| Source | Fear |
|---|---|
| Damage to a resident (by the monster itself) | 0.4 per point |
| Damage to a resident's door | 0.1 per point |
| Killing a resident | +35 |
| Eating a body part | +40 |
| Idle (no attack for 6 s) | 0.8 per second |
| Minion damage or minion kills | none (stops the horde snowballing) |

Fear buys two things from the same pool: **levels** and **minion upgrades**. A bot match earns about 1,600 Fear.

## 4. Levels and stats

- Level n+1 costs `20 + 6 × (n − 1)` Fear. Level 2 is 20, level 5 is 38, level 20 is 128, and the whole climb from 1 to 20 costs 1,406.
- Every level gives +4% HP and +3% damage on its own, plus **one point** in a stat track:

| Track | Icon | Per rank | Ranks |
|---|---|---|---|
| Vitality | heart | +8% max HP | 5 |
| Hide | shield | −5% damage taken | 5 |
| Stride | boot | +3% move speed | 5 |
| Frenzy | claw slashes | +7% attack speed | 5 |
| Maw | fanged jaw | +7% damage | 5 |

- Some levels give an extra pick on top of the stat point:
  - Level 3 unlocks the special.
  - Levels 4, 7, 10, 13, 16 and 19 each give a rank for one ability.
  - Levels 6 and 12 each give a utility ability.
- Levels 5, 10, 15 and 20 make the model 6% bigger each.
- Body parts still add their bonuses: arm +10% damage, leg +5% speed, torso +150 HP, eye +4 tiles reveal (which also helps scouting). Up to 4 of each.

## 5. Minions

- **Awaken the horde** (30 Fear) opens a rift in the hallway beside every living resident's door. The monster's own lair gets none.
- Every night each rift releases a fixed batch, split into three pulses at 2 s, 30 s and 60 s into the night.
- Batch per rift is `horde × spawnMultiplier × (6 ÷ residents alive)^0.4`, rounded. Horde size starts at 2 and goes up to 5. The whole night is capped at 40 minions.
  - With horde 3 and all 6 residents alive, each door gets 3 a night (18 in total).
  - With 1 resident left, that door gets 6 a night.
  - With horde 5 and 1 resident left, that door gets about 10, not 30.
- Minions chew their resident's door, then go for that resident. When the resident dies, their rift closes and its minions crumble.
- Towers shoot whichever is nearest, the monster or a minion. The monster counts as 1.5 tiles closer, so towers favour it over a minion right beside it.

### Resistance

- The monster picks one resistance for the whole horde: bullet, electric or fire. Resisted damage is cut by 40%, or 55% and 70% with Thick hide.
- Each pick has a weakness that takes +25%: bullet-proof is weak to fire, fire-proof to electric, electric-proof to bullets.
- The first pick happens when the horde awakens; after that it can change every **4 nights**. In a 6-night match that means one swap.
- The monster only learns a room's towers after looking inside: an open or broken door in view, a room within its Eye radius, or walking in.
- Residents only see a minion multiplier after one of their towers hits a minion with that type, the same rule as the monster's weaknesses.

### Minion upgrades (Fear per rank)

| Upgrade | Effect | Costs |
|---|---|---|
| Horde size | +1 per door each night (max 5) | 50 · 90 · 150 |
| Toughness | +20% HP | 25 · 50 · 80 |
| Fangs | +20% damage | 25 · 50 · 80 |
| Scurry | +10% speed | 20 · 40 · 60 |
| Frenzy | +15% attack speed | 25 · 50 · 80 |
| Thick hide | resistance 40 → 55 → 70% | 30 · 60 · 100 |
| Evolve | next form (needs 4, then 8 ranks bought above) | 120 · 220 |

### Minion lines and forms

| Monster | Form I | Form II | Form III |
|---|---|---|---|
| Gorge (cloche) | Cloche Biter: 60 HP, 7 dmg/1.0 s, speed 3, doors ×1.5 | Carving Cloche: 85 HP, 9 dmg | Banquet Trolley: 120 HP, 11 dmg, doors ×1.7, drops a body part when it breaks a door |
| Mildred (puffcap, ×1.5 per batch) | Puffcap: 35 HP, 5 dmg/0.9 s, pops into a slowing puff | Mourning Morel: 50 HP, puff also blinds towers 1 s | Funeral Shroom: 70 HP, splits into two Puffcaps |
| Porter (mimic) | Luggage Mimic: 40 HP, 8 dmg/0.7 s, speed 4.2 | Steamer Trunk: 55 HP, swallows the first tower shot | Wardrobe Mimic: 80 HP, releases two Mimics when it breaks |

## 6. Monster UI layout (`Assets/Scripts/Game/UI/GameHUD.Monster.cs`)

Everything opens with one tap and every purchase is one more tap.

```
┌ Menu 1x 2x 4x Walls ┐        ┌ NIGHT 2 / 6 ┐
│ log (last 5 lines)  │        └─── 1:12 ────┘
├─────────────────────┤
│ Lv 4  Maître Gorge  │  ← corner chip: level, HP bar, Fear
│ ████████████░░░     │    (tap = open the stat ring)
│ ● 152  tap yourself │
└─────────────────────┘
                              ( ring opens around the monster )
 ( Horde )  ← round button, tinted by the horde's resistance       ( Hotel view )
  3 out
 ( joystick )                                         ( Special ) ( Area )
```

- **Grow hint:** when a level is affordable, a pink glow circles the monster's feet and a badge bobs over its head. A gold badge means an ability rank or trick is owed.
- **Stat ring:** tap the monster, its chip or the badge.
  - Five orbs circle the monster, one per stat track. Each has an icon, its name, the gain written under it ("+7% damage") and rank pips.
  - Below the ring: "Level 5 · 38 Fear · tap a stat", or "need 12 more Fear".
  - **One tap on an orb buys the level and puts the point there.** If that level owes a rank or trick, the same ring turns into gold orbs for it. Otherwise the ring closes.
  - Tapping anywhere else closes it. When you can't afford a level, the ring shakes.
- **Horde window:** tap the Horde button. One big window:
  - **Top:** "Horde" and the three resistance types (Bullet / Electric / Fire). The selected type tints the whole window gold, blue or orange. Below it: "take 40% less fire, weak to electric · change in 3 nights".
  - **Left:** three tabs, one for each minion form, with the minion icon and I / II / III. Forms you own are lit.
  - **Middle, current form:** the minion icon, its name and stats, its trait, and six upgrade orbs in a ring (Horde, Toughness, Fangs, Scurry, Frenzy, Thick hide). Under each orb: gain, cost and rank pips. **One tap buys.**
  - **Middle, next form's tab:** the Evolve orb with its cost, or "2 / 4 upgrades" until it unlocks, and the next form's stats and trait.
  - **Before awakening:** one big Awaken orb (30 Fear) with what it does.
  - **Footer:** "3 per door each night · 12 out now · Towers seen: bullet 3 electric 0 fire 2 · ? 1 room unseen".
  - The X or a tap outside closes it.
- **Ability buttons:** round buttons at the bottom right, keys 1–5. Area attack, special and utilities, with their cooldowns.
- **Desktop keys:** L opens the stat ring, H the Horde window, Esc closes both.

### Resident-side UI (PR #10, for reference)

- **Summon tray:** tap an empty plate to raise a tray between the joystick and the action button. Tower types are an icon rail on its right; the selected card shows a ghost and range ring on the plate.
- **Tower ring:** tap a tower to open Level up, Evolve and Banish orbs around it, with a card showing its 15-level track. Level up and Evolve open a slide switch to confirm; Banish is a 1 s hold.
- **Monster card:** the top-right card shows the monster's weaknesses only after your towers have hit it with that type, and now minion multipliers the same way.

## 7. Art pipeline

- **Monsters:** rigged Tripo models baked by `Assets/Editor/MonsterKitImporter.cs` (menu *Bad Apple → Art → Bake imported monster models*).
  - Sources: `Assets/TripoModels/<model>/`, kept out of git like the residents'.
  - Output: `Assets/Resources/Art3D/Monsters/`, with Humanoid rigs, 8k-triangle meshes, 1024 px albedos and one Animator per monster.
- **Clips:** Mixamo, in `Assets/TripoModels/MonsterClips/`. Monster-Idle / Walk / Run / Cast / Eat / Death, Gorge-Cleave / Hook, Porter-Lash / Bell.
  - Hits play Attack, the area attack plays Cast, the special plays Special, and eating loops Eat.
  - A missing clip falls back to the residents' idle, walk and run.
- **Icons:** `tools/art/ui_icons.py` renders the window icons into `Assets/Resources/Art/UI/` (stat_*, kit_*, minion_*, fear, trick, plus the tower ones).
- **Still stand-in art:**
  - Minions are a small spectre tinted in the monster's colour.
  - Rifts and area attacks are glowing rings on the floor.
  - Body parts are the old sprites.

## 8. Where things live

| What | File |
|---|---|
| Monster stats and kits | `Config/monsters.json`, `Config/abilities.json` |
| Fear, level prices, stat tracks, ranks | `Config/monster_progression.json` |
| Minions, rifts, upgrades, forms | `Config/minions.json` |
| Monster gameplay (hits, kits, hazards) | `Scripts/Game/Core/GameManager.Monster.cs` |
| Fear, levels, picks | `Scripts/Game/Core/GameManager.Progression.cs` |
| Rifts, minions, resistance, scouting | `Scripts/Game/Core/GameManager.Minions.cs` |
| Tower targeting (monster or minion) | `Scripts/Game/Core/GameManager.Towers.cs` |
| Bot monster (spending, kit use, resistance) | `Scripts/Game/AI/MonsterAI.cs` |
| Monster UI | `Scripts/Game/UI/GameHUD.Monster.cs` |
| 3D monster display | `Scripts/Game/Art/CharacterModel.cs`, `GameManager.Characters.cs` |
| Tests | `Tests/EditMode/MonsterKitTests.cs` |
| Bot balance sim | `Editor/BotSimulation.cs`. The latest 60 matches: residents win 13% (15% before the redesign); by monster Gorge 2/21, Mildred 4/20, Porter 2/19. |
