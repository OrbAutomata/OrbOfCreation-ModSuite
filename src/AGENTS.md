# Source

Runtime code for the suite. Read the affected feature's README and [the testing doctrine](../docs/testing/README.md) before changing behavior, and follow [the runtime validation protocol](../docs/testing/runtime-validation.md) when a claim needs live evidence.

## Ownership

- `Common` owns gameplay-neutral contracts and infrastructure: scheduling, configuration, lifecycle, native binding, mutation verification, freshness and diagnostics. A feature uses those and never builds its own.
- Neutral infrastructure goes to `Common` even when only one feature needs it today. A feature never reaches into another feature's folder for a shared piece.
- Feature policy stays in its feature. A host or the composition root wires features through their seams and never names a feature by concrete type or display name.

## The native boundary in code

- Bind native members once, at lifecycle scope, with exact owner, overload, parameter, field and return types. A mutation path never resolves a member by name, arity or assignability at call time.
- Every new reflected member, Harmony target, native type or UI asset contract gets an installed-game metadata contract.
- `Update`, `LateUpdate` and Harmony hooks stay bounded: capture native-free facts or enqueue work. Policy runs against immutable publications.
- A read that failed is reported as failed. It never becomes empty, absent, "collected" or a different refusal.
- A throw after the native call ran means the change may have landed. Read the after-state before classifying, as [the boundary doctrine](../docs/runtime-architecture/game-boundary-doctrine.md) says: committed if the outcome is visible, otherwise a fault, never a refusal a caller could retry.
- A refusal carries an exact reason and a stable result code, and that reason reaches a surface someone reads. An exception, no-op, partial result or unverified postcondition never becomes success.

## Engineering principles

- Verify an action with the single simplest sentinel: one observable check that it landed, such as the new spell instance existing or the level rising. Before/after deltas, payment reconciliation and receipt bookkeeping gate nothing; don't add them.
- Never compute a value nothing acts on. If no branch, gate or displayed surface consumes it, delete the code that produces it; don't keep it as evidence or log it just in case.
- Design for the common case. Most actions simply succeed, and guards around outcomes that cannot fail closed are findings, not safety margins.
- Success is quiet and failure is exact. A success says what changed; a failure names the one reason a caller can act on.
- When two designs are otherwise equal, the one with fewer moving parts, fields and states wins, over marginal latency, completeness or flexibility.
