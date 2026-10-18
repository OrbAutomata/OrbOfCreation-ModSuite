# Game systems

Player-facing facts and math about Orb of Creation, one mechanic per file, with constants from the [audited build](../reverse-engineering/audited-build.md). Only what a player could learn from the game: no suite internals, no code identifiers, no play advice.

- [README.md](README.md) is the index; read it first, then only the files the task needs.
- Describe a mechanic as the player meets it, in the UI's vocabulary. Where the game's internal model differs from the UI story (a spendable point that is really a capacity allocation), that difference is signal: state it in one labeled **Code shape:** note, never blended into the player-facing text.
- Play advice belongs in `../strategy/`, code-level material in `../reverse-engineering/`.
- Numbers from a single save are observations and say so; only authored constants are stated as rules.
- An unknown is one entry in [open-questions.md](open-questions.md). When it is answered, the answer lands on the owning page and the entry is deleted.
