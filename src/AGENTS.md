# Source guidance

Supplements the root `AGENTS.md` for work under `src/`.

- Read the affected feature README and the testing doctrine before changing
  behavior; use the runtime protocol when the claim needs live evidence.
- `Common` owns gameplay-neutral contracts and infrastructure only; feature
  policy stays with the feature.
- Add no alternate scheduler, configuration authority, native mutation path, or
  feature-local copy of shared lifecycle, binding, verification, freshness, or
  diagnostics infrastructure.
- Resolve complete native binding sets at lifecycle scope. Exact overload,
  owner, parameter, field, and return types must be known before mutation.
- Keep `Update`, `LateUpdate`, and Harmony hooks bounded. Capture native-free
  facts or enqueue work; policy runs against immutable publications.
- Add installed-game metadata contracts for every new reflected member, Harmony
  target, native type, or UI asset contract.
- A refusal carries an exact reason and stable result code; an exception, no-op,
  partial result, or unverified postcondition never becomes success.

## Engineering principles

- Verify with the single simplest sentinel. One observable check proves an
  action landed (the new spell instance exists, the level rose). Parallel
  before/after deltas, payment reconciliation, and receipt bookkeeping are
  ceremony: they cost CPU and maintenance and gate nothing.
- Never compute a value nothing acts on. If no branch, gate, or displayed
  surface consumes it, the code that produces it is deleted — not kept as
  evidence, not logged just in case.
- Design for the common case. Most actions simply succeed; layers of paranoia
  around outcomes that cannot fail closed are findings, not safety margins.
- Success is quiet, failure is exact. A success says what changed and stops; a
  failure names the one reason a caller can act on. Neither ships a dossier.
- Less drama. When two designs are otherwise equal, the one with fewer moving
  parts, fewer fields, and fewer states wins — simplicity beats marginal
  latency, marginal completeness, and speculative flexibility.
