# Runtime architecture dossier

ServiceCycle is the production runtime for every automation feature in the suite.

[Back to documentation](../README.md)

## Purpose

ServiceCycle separates reading the game from deciding what to do. The game is read **once**, by one
service, off to the side; every other service decides against three immutable published snapshots —
configuration, world state, and strategy — and never reads the game to make a decision. Before
performing an action, the main thread re-checks live game state, so a stale decision is discarded
rather than overriding the game.

## Index

1. [How a service gets its data](service-data-flow.md) — the contract: three publications, two
   service shapes, and the reads a service may not make. Authority if anything else disagrees.
2. [Service-cycle runtime](service-cycle-runtime.md) — how a cycle executes, from wake to terminal
   receipt.
3. [Shared world collection](world-collection.md) — the one reader: what it collects, how it reads
   without writing, and the freshness gate every consumer is held by.
4. [Collection quirks](world-collection-decisions.md) — the numbered W-entries source comments cite.
5. [Observability](observability.md) — the observation products, their artifacts, and how to
   read a capture.
6. [Architecture](architecture.md) — where the components sit and what depends on what.
7. [Goals and invariants](goals-and-invariants.md) — product goals, the conditions on owned math,
   evidence grades, strategy rules, and the non-goals.
8. [Game boundary doctrine](game-boundary-doctrine.md) — the rules for every touch of the game.
9. [Deferrals](deferrals.md) — what is deliberately not built, and what each item waits on.
10. [Game MCP frame operations](game-mcp-frame-operations.md) — the perf-debug transport: one
    request-scoped inbox, frame ordering, data lifetimes, and the HTTP/Unity boundary.

## The four boundaries

- **Production.** One feature-neutral Automata host owns one registry and one frame pump. One source
  service reads the game; every other service consumes its publication and is admitted at most once
  per accepted frame. The game stays authoritative for availability, cost, quantity, queue room,
  completion, and mutation results. There is one production path, no runtime selector, and no
  fallback implementation.
- **Observability.** One release action packages past evidence from the rolling decision journal,
  recent-event ring, configuration, identifiable save files, and redacted log into a capped bug-report
  zip. Profiling builds separately own their correlated full trace and performance profile. Diagnostic
  failure disables or faults that path without changing gameplay behaviour.
- **Verification.** `./script/test` is the complete bounded portable gate; real-reference builds and
  installed-game contract tests cover reflected game boundaries; interactive runtime validation is
  required for gameplay, UI, save, and installation claims. Passing one boundary does not imply
  another. Current commands live in the [testing guide](../testing/README.md).
- **Not yet built.** No service publishes a `SuiteStrategy` bulletin. The publisher, the stances, and
  the neutral default exist, and every consumer already reads a neutral one.
