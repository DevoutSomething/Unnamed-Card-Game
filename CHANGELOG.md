# What's New — orientation for anyone coming from the old repo

*If the last thing you saw was the live GitHub repo (commit `4e03253`, "tested opus out…"),
this file catches you up. Four commits land at once; together they take the project from a
compiling skeleton to a playable game. Verified before push: headless compile with zero
errors, 57/57 EditMode tests, and the card validator reporting 22 cards / 0 problems.*

## Where the repo left off

The old tip had the core scaffolding: a `GameState` with lanes/players/energy, a
`CommandResolver` handling basic commands (untested), simple combat, and a hand-authored
`Card` ScriptableObject loaded from `Assets/Resources`. No way to play, no real card
content, no tests to speak of.

## `8d636b7` — "claudes plan v1": the card pipeline skeleton

Introduces the architecture that everything since builds on: **cards are authored as JSON,
not clicked together in Unity.**

- `Assets/GameData/cards/*.json` — card data as plain JSON (4 sample cards at this point),
  plus `abilities.json` (the ability vocabulary) and `costing.json` (balance weights).
- `tools/cards.py` — a Python CLI to create/edit/validate cards from the terminal.
- `Assets/Scripts/Editor/CardPipeline.cs` — Unity menu (`Cards ▸ Pipeline ▸ Import All`)
  that bakes the JSON into `Assets/Resources` assets the game loads at runtime.
- New core pieces: ability definitions (trigger/effect/target + magnitude X),
  `CardZones` (deck/hand/lane placement rules), card skin/art/layout ScriptableObjects,
  first view classes (`CardView`, `CardViewFactory`, `CardGallery`), and a first test file.

## `a73f394` — "mvp": the game is playable

The big one (~295 files — most are Unity `.meta` files and baked card assets).

- **You can play.** Open `Assets/Scenes/Game.unity`, press Play: local hot-seat for two
  players sharing a mouse. Click a hand card, click a lane to play it, End Phase (or Space)
  to pass. Turn rotation is P0 P1 P1 P0 → combat → repeat, with an energy refill and +1 max
  energy after each combat. First hero to 0 HP loses; Play Again restarts. The whole board
  UI (`GameController` + `BoardView`) is built from code — the scene file is nearly empty.
- **Real content:** 22 cards across 5 archetypes (tank/mage/healer/assassin/bruiser), each
  with generated placeholder art and per-archetype borders (`tools/gen_placeholder_art.py`).
  Decks are 10 random Commons, seeded (set the seed on the `Game` object to reproduce).
- **Abilities are data-driven:** 18 keywords defined in `abilities.json` and resolved at
  runtime (defend, thorns, bounty, pierce, precision, doubletap, overkill, growth, heal,
  mending, regen, heroregen, goldgen, goldsteal, rob, herodamage, guydamage, draw).
  14 are live in combat; the rest await their systems.
- **Combat got its real shape:** per lane — start-of-combat wave (growth → heals →
  guy/hero damage), then front cards swing simultaneously, then back cards; overkill
  spills to the hero; deaths pay kill-reward gold to the opponent and bounty to the killer.
- **Tests:** NUnit suites under `Assets/Scripts/Editor/Tests/` (they must live in an editor
  assembly): command resolver, card system, and one test per ability keyword.
- Deleted along the way: the old hand-made `Knight.asset`, `TestCardCreator.cs`, and the
  original 4 sample cards (replaced by the 22).

## `15a19dc` — combat bookkeeping fixes

Post-MVP review pass; four gameplay-logic fixes, each with regression coverage:

- **Thorns can't erase kill credit anymore.** A card killed in the simultaneous swing still
  counterattacks; the thorns it takes back used to overwrite the record of who killed it,
  costing the real killer its gold. A corpse's death attribution is now frozen.
- **Mutual kills pay both sides fairly.** Death rewards are awarded for both sides of a lane
  *before* any corpse is removed, so a killer that died in the same trade still collects
  its bounty (previously player 0's side was swept first and lost out). *(The fix itself
  landed in the mvp commit; this commit finished the group.)*
- **One armor system.** A legacy hardcoded `"Armored"` status stacked with the data-driven
  `defend` keyword; the legacy branch is gone and the old test harness now uses `defend`.
- **One placement path.** `HandlePlayCard` re-implemented lane placement; it now goes
  through `CardZones.TryPlaceInLane` like everything else.

## Latest commit — prefab builder restored, this file

`CardViewPrefabBuilder.cs` (accidentally deleted during the MVP pass) is back: the
`Cards ▸ Setup ▸ Build Default Card Prefab` menu rebuilds the card's visual template
(art window, name, description, cost/attack/health, border frame) entirely from code —
the only way to iterate on the card's look without hand-editing the prefab.

## Getting oriented

| Where | What |
|---|---|
| `Assets/GameData/` | Source of truth: card JSON, abilities, costing, art PNGs |
| `tools/cards.py` | `py tools/cards.py validate` / `cost-check` / `set <id> field=value` |
| `Assets/Scripts/Core/` | Pure C# simulation — commands in, events out, no Unity in the logic |
| `Assets/Scripts/Client/` | `LocalGameServer` (in-process stand-in for a future real server) + UI |
| `Assets/Scripts/Editor/` | The import pipeline, scene/prefab builders, and the NUnit tests |

Day-to-day loop: edit JSON (or use `cards.py`) → Unity menu `Cards ▸ Pipeline ▸ Import All`
→ press Play. Tests: `Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All` (57 tests).

Known gaps, all deliberate for the MVP: no shop/augments/events (rotation slots exist but
pass through), no spells (all 22 cards are guys), an empty deck silently stops drawing
(no reshuffle/fatigue yet), and no networking (both players share one machine).
