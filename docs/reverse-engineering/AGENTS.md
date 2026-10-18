# Reverse engineering

How an engineer reads the game's code: the decompiling workflow, the identity and type model, naming traps, math internals, the save format, UI internals, hooks and native action surfaces.

- Techniques and code-level truths only; player-facing facts belong in `../game-systems/`.
- Cite against [the audited build](audited-build.md). IL and asset citations belong to one baseline and die with a game update, so never copy them into other folders.
- The game stays authoritative at runtime: findings here describe code, not guarantees.
