# Goals and invariants

The rules stated nowhere else: the product goals, the conditions on owned economy math, the evidence
grades a mutation needs, the strategy rules, and the non-goals. Every other invariant of the runtime
is specified by the document that owns the mechanism.

[Back to dossier](README.md)

## Product goals

The runtime should make automation:

- responsive enough to keep up with fast native queues;
- capable of simple local decisions and future expensive strategy work;
- safe across save/load, reset, NG+, scenes, unlocks, native refusal, and faults;
- modular enough that feature code expresses product policy rather than orchestration mechanics;
- observable live and analyzable offline;
- efficient in Unity time, background CPU, copying, allocation, and logging;
- testable without the game through typed deterministic inputs and exact outcomes;
- extensible to cross-domain strategy without one god planner or one god runtime class.

The long-term product should support Auto Buy, harvesting, Agrimancy, spells, crafting, loadouts, and
other game domains, plus an optional high-level strategist. Enabling every supported service should
eventually be capable of playing the complete game safely under user policy.

## Owned economy math

The game owns availability, cost, quantity, rates, queue room, completion, and final mutation
validity. Planning may use captured native results but must not silently reproduce unknown economy
formulas.

Planning may evaluate an *owned* economy formula, on any thread, when all four of the following hold.
Fewer than four is the silent reproduction the rule above forbids.

- It was transcribed from the decompiled game assembly rather than inferred from observed behaviour,
  and the transcription records which assembly it came from.
- That assembly is covered by a hash baseline in `data/native-contracts.json`. On a complete pair
  matching no baseline, gameplay stays quarantined until the player accepts that exact pair; an
  incomplete installation refuses completely ([native contracts](../testing/native-contracts.md)).
  There is no "ask the game" fallback, deliberately: a mismatch invalidates the reflection contracts
  exactly as much as the ported arithmetic, so falling back would read members by name from an
  equally unverified assembly.
- It is differentially tested against the game's own result for real entities in a live session, and
  disagreement is a failure rather than a tolerance. Offline tests cannot establish this on their
  own, because they assert against values derived by hand from the same decompiled source — a
  misreading would be reproduced identically in the port and in its expected value, and pass.
- Native revalidation at the action boundary stays authoritative regardless. Owning the arithmetic
  changes what the suite may compute off-thread, never what it may trust when mutating.

The accepted cost is that any game patch quarantines gameplay until re-audited or accepted;
`script/re-audit` makes the re-audit cheap, which is the mitigation rather than softening the gate.
Stale suite values may remain useful; stale native references may not. Every action is advisory until
the main thread re-resolves stable identity and performs current validation, and temporary native
global-state changes stay adapter-owned with `try/finally` restoration on every path.

## Evidence grades

`OrbModding.Common.EvidenceAssessment` is the one vocabulary for what is known, how it was
established, and whether facts conflict. Strength never replaces required sources: an active mutation
must meet both its minimum level and every source its feature requires.

| Level | Meaning | May authorize active mutation alone? |
| --- | --- | --- |
| `Unresolved` | Required facts are missing, unknown, or contradictory. | No. |
| `Inferred` | Follows from other evidence but was never observed or audited directly. | No. |
| `RuntimeObserved` | Exact native type, identity, registry, or relationship evidence was read. | No. |
| `SerializedAssetVerified` | Verified from the canonical serialized-asset mapping *and* observed through the required runtime sources. | Only with a complete required-source mask. |
| `StaticallyVerified` | Exact managed signature or implementation verified from audited assembly metadata or IL. | Only as the right evidence kind, with every required runtime/identity source present. |

`IsContradictory` is independent of level: a contradiction degrades the effective level to
`Unresolved` and fails `Meets(...)` however many strong individual facts were observed. The bounded
source mask names facts rather than free-form confidence — `StaticContract`, `SerializedAsset`,
`RuntimeNativeType`, `StableIdentity`, `RuntimeRegistry`, `NativeRelationship`. Display names are
diagnostics and can never upgrade evidence.

Unknown or contradictory mutation evidence fails closed. A consumer that gates on a classification
requires its mutation-grade predicate rather than trusting the label, and tests assert the exact
level and source mask so a later game or mapping update produces a reviewable contract diff instead
of silently changing mutation authority.

## Strategy rules

- The strategist publishes immutable versioned goals and constraints; it does not call native
  mutations. Domain services own their native contracts, local planning, and action construction.
- Strategy may express resource goals, targets, reserves, spend limits, embargoes, pauses,
  priorities, and time horizons. Every constraint has scope, provenance, precedence, and
  replacement/expiry semantics.
- **Strategy may only tighten what user configuration already permits, never loosen it.**
  Configuration is evaluated first and independently; the stance is consulted only on spends the
  operator would have allowed, and can then only refuse. A wrong, stale, or hostile bulletin
  therefore costs throughput and nothing else.
- A missing or failed strategist does not prevent safe local fallback automation. The first published
  bulletin is neutral and reproduces unstrategised behaviour exactly, so a strategist that never
  runs, faults, or is disabled changes nothing.
- The cycle-pinned strategy snapshot is advisory beneath cycle-pinned user policy and current native
  validation.
- A constraint that cannot be evaluated against the captured facts is reported as inapplicable rather
  than silently skipped or invented, so an authoring error stays visible in diagnostics.
- If future strategy search needs a specialized execution contract, it does not complicate ordinary
  service runners.

## Non-goals

- Perfectly fresh mirrors of all game state.
- A world snapshot a consumer may treat as current. The shared snapshot exists; what stays a non-goal
  is the freshness *guarantee*. Every published reading is bounded stale by construction, and
  anything that must be current is revalidated natively at the action boundary.
- One physical shared worker scheduler for ordinary services.
- General async/actor/process authoring machinery.
- Reimplementing the game's economy wholesale, or porting any part of it outside the four conditions
  above.
- Eliminating final native validation.
- One global planner containing every domain algorithm.
- Automatic retry of rejected actions.
- Immediate application of ordinary configuration or strategy changes to current work.
- Retaining obsolete runtime paths as rollback mechanisms.
- Installed-game or release claims from portable evidence.
