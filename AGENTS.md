# Orb Of Creation ModSuite

BepInEx 5 mods that automate Orb Of Creation without taking the game away from the player. The game owns identity, structure, costs and every transaction; the suite owns only audited math and policy, and when it cannot prove what the game will do, it refuses and says why. When goals conflict, the player's save and control win, then fail-closed correctness at the native boundary, then the simpler design, then speed or coverage. [The north star](docs/north-star.md) is the long form.

## The native boundary

[The game-boundary doctrine](docs/runtime-architecture/game-boundary-doctrine.md) owns the mechanism; every change keeps these:

- Unity objects and game APIs are touched only on the Unity main thread.
- Identity is a stable UUID plus the expected native type; names are diagnostics only.
- Each capability has one GameAction, and features, MCP tools and tests all mutate through it. It revalidates mutable native facts at the action boundary and verifies the postcondition.
- Scene changes, save-load, reset and NG+ invalidate every native reference.
- An unknown game contract fails closed.

## How code changes here

Keep each change focused and leave unrelated code alone. Where the code you touch breaks these rules, follow the rules, not the surrounding pattern.

- Extend the shared primitive; never copy it. If it lacks a parameter you need, add the parameter and delete the copies it makes redundant. If no shared home exists yet, raise that instead of copying the nearest capability: copies drift on policy.
- A new capability lives in its own files. Don't grow a file that every capability edits; plug into the seam beside it, or build that seam first.
- Finish a migration in the change that starts it, or don't start it: the callers move and the old model is deleted.
- Register a capability in one place, never in another hand-kept list the next capability must remember.
- A test must be able to fail when the behavior it names regresses: it drives the shipping path, not a copy of it, a test-only entry point, or a fake doing the work. Don't write tests that only prove something was removed.
- Update the docs that describe a behavior in the same change, and delete the lines it obsoletes.

## Workflow

- Never install DLLs into the game, edit an active save, create tags, publish a release, or push unless the user asks for that action.
- Commit subjects use a lowercase area prefix (`world:`, `ui:`, `docs:`, `tests:`, `build:`, `autobuy:`, …). PRs stay small and land on `main`.
- `artifacts/` is gitignored so working reports stay local; never force-add it.
- Agent guidance lives in `AGENTS.md`; a new one gets the same sibling symlink alias the existing ones have.
- Before every PR, run [the gate](docs/testing/README.md#the-gate) (`./script/test`), plus the installed-game contracts for native-boundary changes, one heavy process at a time.
- Reconcile test, contract, exemption, entity and compiler-warning counts: an unexplained delta is a failure, not a new baseline. A gate passed only if it ran against the current working tree.

## Game vocabulary traps

- Glyph = Recipe Book = pool unlocker; the socketable Spell Augment is a different system ([vocabulary](docs/game-systems/vocabulary.md)).
- A pool unlocker is learned when `GlyphSO.IsAvailable()` says so; non-discoverable glyphs use the "Learn X" prerequisite edge ([pool unlockers](docs/game-systems/pool-unlockers.md)).
- `Divination` displays as "Divining" ([spell types](docs/game-systems/spell-types.md)).
- A screen label is not an entity label; resolve screens through the translation table ([naming traps](docs/reverse-engineering/naming-traps.md)).
- Discovery is one mechanism on seven trees, Concepts included ([discovery](docs/game-systems/discovery.md)).
- Prices round to two significant digits under two different rules ([numbers and rounding](docs/game-systems/numbers-and-rounding.md)).

## Where to look

- The game: [game-systems](docs/game-systems/README.md) for facts, [strategy](docs/strategy/README.md) for play, [reverse-engineering](docs/reverse-engineering/README.md) for its code.
- The suite: [runtime-architecture](docs/runtime-architecture/README.md) for design, [testing](docs/testing/README.md) for gates and live validation.
- Contributing: [CONTRIBUTING.md](CONTRIBUTING.md) and [the engineering doctrine](docs/development/engineering-doctrine.md).
