# Engineering doctrine

[Documentation](../README.md) · [Development setup](setup.md) ·
[Game-boundary doctrine](../runtime-architecture/game-boundary-doctrine.md)

These rules govern implementation and review.

- **Tests model reality.** Drive the shipping path with faithful portable stubs. A test that hides
  native behavior behind a self-consistent fake cannot establish correctness. Observe a meaningful
  assertion failure before changing behavior, then verify the implemented path.
- **No silent fallbacks.** Failed capture or binding is a named failure. Missing evidence does not
  become empty data, a successful no-op, or an alternate implementation.
- **Honest outcomes.** After a press, verify the requested transition. A landed change commits; an
  unverified transition faults. Admission refusal and an attempted fault have different retry meanings.
- **Freshness belongs at the action boundary.** Use the doctrine's
  [freshness classes](../runtime-architecture/game-boundary-doctrine.md#boundary-validators-and-freshness-classes).
  A UI-cached validator is not current authorization without its declared revalidation.
- **Prefer simple ownership.** A cycle consumes its pinned immutable inputs; narrowing an
  imperceptible timing window does not justify another reader or another source of truth.
- **One capability, one GameAction.** Features, MCP and tests share the
  [canonical mutation path](../runtime-architecture/game-boundary-doctrine.md#gameactions-the-only-way-to-mutate).
- **Counts reconcile.** Explain every test, contract, exemption, entity and warning delta. An
  unexplained change fails review rather than establishing a new baseline.
