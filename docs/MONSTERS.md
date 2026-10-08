# Monsters, minions and the monster UI

State as of Oct 8 2026 (horde roles update), branch `claude/project-thread-sb8ifv` (PR #11). Every number below is read from
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
| Idle (no attack for 6 s) | 0.5 per second |
| Start of every night | +30 |
| Minion damage or minion kills | none (stops the horde snowballing) |

Fear buys two things from the same pool: **levels** and **minion upgrades**. A bot match earns about 1,600 Fear.

## 4. Levels and stats

- Level n+1 costs `20 + 6 × (n − 1)` Fear. Level 2 is 20, level 5 is 38, level 20 is 128, and the whole climb from 1 to 20 costs 1,406.
- Every level gives +4% HP and +3% damage on its own, plus **one point** in a stat track:

| Track | Icon | Per rank | Ranks |
|---|---|---|---|
| Vitality | heart | +8% health | 5 |
| Hide | shield | Takes 5% less damage | 5 |
| Stride | boot | Moves 3% faster | 5 |
| Frenzy | claw slashes | Attacks 7% faster | 5 |
| Maw | fanged jaw | Hits 7% harder | 5 |

- Some levels give an extra pick on top of the stat point:
  - The special unlocks at level 3, or at the start of night 2 at the latest, so a horde build still gets it.
  - Levels 4, 7, 10, 13, 16 and 19 each give a rank for one ability.
  - Levels 6 and 12 each give a utility ability.
- Levels 5, 10, 15 and 20 make the model 6% bigger each.
- Body parts still add their bonuses: arm +10% damage, leg +5% speed, torso +150 HP, eye +4 tiles reveal (which also helps scouting). Up to 4 of each.

## 5. Minions

The horde is automatic: rifts spawn minions in pulses and they fight on their own while the monster keeps playing.
The choices are which creature to send and how far to grow it.

- **Awaken the horde** (30 Fear) opens a rift in the hallway beside every living resident's door (not the lair) and unlocks the monster's first creature, the Swarm.
- Each monster has **three creatures, one per role**. The other two cost 60 and 90 Fear to unlock.
- **One creature is active at a time.** Every rift spawns it. Creatures already out finish their lives unchanged.
  - Buying a creature unlocks it and **queues** it: the current creature stays Active until the next pulse, then the new one takes over.
  - Deploying another owned creature is **free** and queues it the same way. Once a queued creature takes over, switching locks for the rest of that night.
  - Horde Strength and bought evolutions are kept through every switch.
- Every night each rift releases its batch in three pulses at 2 s, 30 s and 60 s. The batch per rift is `perDoor × (6 ÷ residents alive)^0.4`, rounded, with a cap of 40 a night.
- When a resident dies, their rift closes and the minions it sent crumble.
- Towers shoot whichever is nearest, the monster or a minion. The monster counts as 1.5 tiles closer. Area towers such as the Skull Mortar also hit every minion around the point they strike.

### Roles

| Role | Job | Quantity | Trade-off |
|---|---|---|---|
| **Swarm** | many small attackers overwhelm slow single-target towers | 3 per door, 5 at max level | fragile, dies to area damage |
| **Breachers** | wreck doors fast (×3.2–3.5 door damage) | 1 per door, 2 at max level | weak against residents (×0.5) |
| **Escort** | walks to the monster, guards it, fights beside it | 2 alive, 4 at max level | low damage; 50% of shots aimed at the monster nearby hit the escort instead |

### Creatures, resistances and evolutions

- Every creature has its own resistance, taking 50% less of that type, and a weakness that takes +25%.
  - Bullet-proof is weak to fire, fire-proof to electric, and electric-proof to bullets.
  - Each monster's three creatures cover all three types, so choosing a creature is also choosing a defence.
- Each creature also has one **signature evolution**. It is bought once (price per creature, by what it is worth) and kept for the match.

| Monster | Swarm | Breachers | Escort |
|---|---|---|---|
| Gorge | Cloche Biters, bullet-proof. *Snapping Lids* (80): split into two biters when killed | Carving Cloches, fire-proof. *Butcher's Ram* (100): leave a body part when they break a door | Banquet Trolley, electric-proof. *Silver Platter* (130): towers within 4 tiles shoot it first |
| Mildred | Puffcaps, electric-proof. *Spore Pop* (70): pop into a slowing puff | Mourning Morels, bullet-proof. *Rot Fist* (90): doors they hit rot (no repairs for 4 s), +40% door damage | Funeral Shroom, fire-proof. *Mourning Moss* (110): heals Mildred 2% a second while she is near |
| Porter | Luggage Mimics, fire-proof. *Sticky Tongue* (70): bites slow residents for 1 s | Steamer Trunks, electric-proof. *Slam* (95): breaking a door stuns everyone inside for 1.5 s | Wardrobe Mimic, bullet-proof. *Closet Guard* (140): catches every shot aimed at the Porter nearby |

### Horde Strength

- One shared level, I to VI, for all creatures, including any unlocked later.
- Each rank adds +15% health and +12% damage, and moves every role toward its max-level numbers: Swarm 3 → 5 per door, Breachers 1 → 2, Escort 2 → 4.
- A rank costs `40 × 1.4^(rank − 1)` Fear: 40, 56, 78, 110, 154. That is 438 in total.
- Example:
  - Buy Strength III, then unlock Carving Cloches: they come out at Strength III.
  - Evolve them, switch to Biters, then come back: still Strength III, and still evolved.

## 6. Monster UI layout (`Assets/Scripts/Game/UI/GameHUD.Monster.cs`)

Everything opens with one tap and every purchase is one more tap. The battlefield, joystick and buttons stay visible.

```
┌ Menu 1x 2x 4x Walls ┐        ┌ NIGHT 2 / 6 ┐
│ log (last 5 lines)  │        └─── 1:12 ────┘
├─────────────────────┤                                    ( eye: hotel view )
│ Lv 4  Maître Gorge  │  ← level, HP bar, Fear (tap = stat ring)
│ ●152  tap yourself  │
└─────────────────────┘      ┌───────────── Horde tray ─────────────┐
                             │ Horde ●152  12 out   [Next wave · 18s] ✕│
     ( stat ring opens       │ ┌──────┐ ┌──────┐ ┌──────┐            │
       around the monster )  │ │ art  │ │ art  │ │ art 🔒│            │
                             │ │Swarm │ │Breach│ │Escort│            │
                             │ └ACTIVE┘ └──────┘ └──────┘            │
                             │ Carving Cloches · Breachers  [Deploy] [Evolve]│
                             │ Horde Strength III ●─●─●─○─○─○ [Upgrade]│
                             └───────────────────────────────────────┘
 ( joystick )                       ( Horde ) ( utility ) ( utility )
                                              ( Special ) ( AREA )
```

- **Grow hint:** when a level is affordable, a pink glow circles the monster's feet and a badge bobs over its head. A gold badge means an ability rank or trick is owed.
- **Stat ring:** tap the monster, its chip or the badge.
  - Five orbs circle the monster. Each has an icon, its name, the benefit written under it ("Attacks 7% faster") and rank pips.
  - One tap on an orb buys the level and puts the point there. Owed ranks and tricks appear in the same ring as gold orbs.
- **Horde tray:** tap the Horde button (bottom right, showing the active creature's art). It sits right-aligned above the controls.
  - **Header:** Horde, Fear, minions out, and a **Next wave** countdown that pulses in the last seconds.
  - **Three cards:** each shows the creature's art, its role icon and name, quantity, and resistance and weakness icons.
  - **Card markers** (each has a word as well as a colour):
    - **ACTIVE** is a solid gold badge with a steady gold frame.
    - **QUEUED** is an outlined brass badge with a thinner brass frame.
    - **EVOLVED** is a violet badge on the art.
    - Locked cards keep their art dimmed, with a lock and the price.
    - **Selected** is a thin white outline and a white tab under the card. A beam runs once round the border when you pick it.
  - **Selected creature row:** name, role and its evolution (name and what it does), with its actions:
    - **Awaken / Unlock** with the price.
    - **Deploy · free · next pulse**, **Queued · tap to cancel**, **Deploy · next night** when switching is locked, or **ACTIVE · spawning now**.
    - **Evolve** with its own price, or **EVOLVED**.
  - **Horde Strength strip:** always shown. It has the rank, a six-notch track, "All creatures: +15% health, hit 12% harder, more per wave" and **Upgrade** with the price.
  - Every purchase is one tap and flashes the cards brass.
- **Monster buttons:** icon orbs with the same rim weight and a restrained palette.
  - The area attack is the largest, in the monster's colour.
  - The special and the Horde button (which shows the active creature's art) are brass.
  - Utilities and the hotel view (an eye) are bone.
  - Cooldowns sweep round the rim with the seconds left.
- **Desktop keys:** 1–5 abilities, L stat ring, H horde tray, M hotel view, Esc closes.

### Resident-side UI (PR #10, for reference)

- **Summon tray:** tap an empty plate to raise a tray between the joystick and the action button. Tower types are an icon rail on its right; the selected card shows a ghost and range ring on the plate.
- **Tower ring:** tap a tower to open Level up, Evolve and Banish orbs around it, with a card showing its 15-level track. Level up and Evolve open a slide switch to confirm; Banish is a 1 s hold.
- **Monster card:** the top-right card shows the monster's weaknesses only after your towers have hit it with that type, and the active creature's multipliers the same way.

## 7. Art pipeline

- **Monsters:** rigged Tripo models baked by `Assets/Editor/MonsterKitImporter.cs` (menu *Bad Apple → Art → Bake imported monster models*).
  - Sources: `Assets/TripoModels/<model>/`, kept out of git like the residents'.
  - Output: `Assets/Resources/Art3D/Monsters/`, with Humanoid rigs, 8k-triangle meshes, 1024 px albedos and one Animator per monster.
- **Clips:** Mixamo, in `Assets/TripoModels/MonsterClips/`. Monster-Idle / Walk / Run / Cast / Eat / Death, Gorge-Cleave / Hook, Porter-Lash / Bell.
  - Hits play Attack, the area attack plays Cast, the special plays Special, and eating loops Eat.
  - A missing clip falls back to the residents' idle, walk and run.
- **Icons:** `tools/art/ui_icons.py` renders the window icons into `Assets/Resources/Art/UI/` (stat_*, kit_*, minion_*, fear, trick, plus the tower ones).
- **Minion art:** nine AutoSprite renders in `Assets/Resources/Art/Minions/<line>_<role>.png`, used on the cards and, for now, as the minions' world sprites.
- **Still stand-in art:**
  - Rifts and area attacks are glowing rings on the floor.
  - Body parts are the old sprites.

## 8. Where things live

| What | File |
|---|---|
| Monster stats and kits | `Config/monsters.json`, `Config/abilities.json` |
| Fear, level prices, stat tracks, ranks | `Config/monster_progression.json` |
| Minions: roles, creatures, unlocks, Horde Strength, evolutions | `Config/minions.json` |
| Monster gameplay (hits, kits, hazards) | `Scripts/Game/Core/GameManager.Monster.cs` |
| Fear, levels, picks | `Scripts/Game/Core/GameManager.Progression.cs` |
| Rifts, minions, resistance, scouting | `Scripts/Game/Core/GameManager.Minions.cs` |
| Tower targeting (monster or minion) | `Scripts/Game/Core/GameManager.Towers.cs` |
| Bot monster (spending, kit use, resistance) | `Scripts/Game/AI/MonsterAI.cs` |
| Monster UI | `Scripts/Game/UI/GameHUD.Monster.cs` |
| 3D monster display | `Scripts/Game/Art/CharacterModel.cs`, `GameManager.Characters.cs` |
| Tests | `Tests/EditMode/MonsterKitTests.cs` |
| Bot balance sim | `Editor/BotSimulation.cs`. The latest 60 matches: residents win 17% (15% before the redesign); by monster Gorge 2/21, Mildred 5/20, Porter 3/19. |
