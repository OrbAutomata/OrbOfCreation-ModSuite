# Spell Leveling

Spell Leveling buys mastery levels as an Auto Buy sub-feature. The worker chooses a single spell or
requests the native Level All batch from the shared world. `SpellLevelCycleActionAdapter` applies
automation configuration policy; explicit player/MCP requests use the same service path without that
policy. Both still require the action-family lease and current lifecycle.

`SpellLevelGameAction` uses the Common native action shell for lifecycle bindings, main-thread and
epoch admission, and the mutation permit. Service composition registers it once with
`LifecycleGameActions`; that registry rebinds and disposes it. The capability probe shares the same
action. Native manager, recipe-list and upgrade instances remain local to each attempt, so a failed
read or registry mismatch can recover on the next attempt without a feature-owned latch.

Single leveling keeps the game's gate order and pays immediately before purchasing the named spell's
level. Level All uses the game's own batch after finding an affordable, ready live spell; an
unaffordable earlier spell does not veto the batch. Stable UUID and exact native type resolve a
single target. The shared mutation verifier checks one sentinel: a mastery-level rise. A visible
rise commits even when the game throws afterwards; a press without a rise keeps the existing
paid-without-level target backoff and log reference. No feature-owned retry timer or native cache
survives beside the shared lifecycle owner.

See the [native boundary doctrine](../../docs/runtime-architecture/game-boundary-doctrine.md) and
[testing guide](../../docs/testing/README.md) for validation and its evidence limits.
