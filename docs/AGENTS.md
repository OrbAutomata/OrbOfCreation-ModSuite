# Docs

Every line here is read by future sessions, so each one has a recurring cost. The tree states what is true on `main` now and where the known gaps are.

## Where a fact goes

- `game-systems/`: player-facing game facts and math.
- `strategy/`: how to play well, as opinions and policies.
- `reverse-engineering/`: how an engineer reads the game's code.
- `runtime-architecture/`: how the suite is designed.
- `testing/`: gates and per-feature risk contracts.
- `user-guide/`: player-facing suite documentation.
- `development/`: contributor setup, releases and engineering references.

## Rules for every change

- State the end state. No changelogs, no "no longer" or "previously", no discovery narration, no release labels, no hedging.
- A fact appears once, in the file that owns it; everything else links to it. Never copy a count, member list, type name or mapping from code or from another doc: name its owner instead, because every copy drifts.
- General rules are the content; observations are examples and read as examples. A partial catalogue says it is partial.
- Each folder's README is a one-line-per-file index, and each file stays short and on one topic. A new file gets its index line in the same change.
- New lines remove the lines they make obsolete, here and in every file that duplicated them.
- When a gap closes, the answer moves to the owning page and the gap entry is deleted.
