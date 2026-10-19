# Engineering doctrine

[Back to documentation](../README.md) · [Development setup](setup.md) ·
[Game-boundary doctrine](../runtime-architecture/game-boundary-doctrine.md)

These are review rules, not aspirations. Each was paid for by a concrete defect
or design ruling in the 0.5.0 cycle.

| Rule | Earned precedent |
|---|---|
| **Tests model reality.** A portable stub that differs from the real runtime is defective; reproduce the live failure portably, observe red, correct the stub or product, then observe green. | `GameObject`/`RectTransform`, `Graphic.raycastTarget`, and prerequisite `Check()` defaults each let a self-consistent fake hide or invent production behavior. |
| **No silent fallbacks.** Capture failures are loud defects; degraded lanes, pending states, and best-effort paths are design smells, and every refusal carries its exact reason and stable result code. | Native UI capture once hid missing shapes behind alternate rendering, while the corrected surfaces publish and log the failing member and reason. |
| **Honest refusal beats fake success.** If a postcondition cannot be verified, refuse the capability and say why. | Spell-level batches retain a live waiting reason, backup failure blocks startup automation, and Auto Harvest preserves prerequisite result codes `1028` and `1029`. |
| **Freshness belongs to the game, not the suite.** Follow the boundary doctrine's [freshness classes](../runtime-architecture/game-boundary-doctrine.md#boundary-validators-and-freshness-classes); collection never turns a UI-refreshed cache into current authorization. | The Agromancy prerequisite latch proved only that the game had once checked it, so the exact native validator moved to the action boundary. |
| **Simplicity beats frame-scale latency.** Prefer the clean ownership model over machinery that narrows an imperceptible window. | The cycle-pinned configuration ruling kept one immutable cycle input instead of adding a second current-configuration reader for a one-or-two-frame batch. |
| **One capability, one GameAction.** Follow the boundary doctrine's [single mutation path](../runtime-architecture/game-boundary-doctrine.md#gameactions-the-only-way-to-mutate); features, MCP surfaces, and tests share that definition. | The Auto Items and Auto Scribe ports made binding, preflight, mutation, and evidence one reusable boundary instead of parallel feature-specific calls. |
| **Counts reconcile in both directions.** Every gate names test, contract, exemption, entity, and warning deltas together with their cause; an unexplained addition or removal fails review. | Contract and UI changes repeatedly moved one lock while leaving another stale, so schema, contract, exemption, entity, test, and warning totals are one review unit. |
