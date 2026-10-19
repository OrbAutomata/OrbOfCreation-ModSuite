# Deferrals

What the runtime deliberately does not have, and what each item waits on. Design intent, not shipped
behaviour: nothing here describes code. Each item needs its own decision and is parked with enough
context to resume.

[Back to dossier](README.md)

## The full trace's world store

The full-trace mandate names four streams; raw capture data has no store, so a trace answers "what did
the service decide" and not "what did it see". This needs a ruling on volume and on how a world payload
is written before anyone writes a line of it.

**Why it is not one more store.** The world republishes up to four times a second and its payload is the
entire raw reading of the game rather than a settings tree — roughly a megabyte serialized, so an armed
session writes on the order of 240 MB a minute against a ~100 MB on-disk mandate. The derived
`GameWorldState` is the same magnitude, so recording the derived shape buys nothing. Nothing can be
borrowed from the generation-keyed publication stores either: their reflected sorted-text form suits a
settings tree, and the world is neither small nor stable. A hand-written codec is roughly 1,200 lines
that silently stops recording whatever was added to the world last; a reflection-driven one is roughly
400 lines that puts reflection on the capture path and the schema in the artifact. Choosing between
them is the actual decision.

Options, none taken: accept the volume and take the reflection codec, which is honest and cheapest to
write and makes an armed minute a large artifact; delta-encode against the previous generation, trading
capture-path CPU for size and needing a base-generation policy; store only the generations a decision
referenced, since a trace is read backwards from a decision; record a named category subset and say in
the artifact which were dropped rather than claiming a complete world; or leave it open, since the
other three streams answer most bug reports. Resume from
`src/Common/Runtime/ServiceCycle/Observation/FullTrace/Stores/` — the shape a world store would follow.

## The suite's on-disk cap

The always-on journal has a 64 MiB envelope, routine per-action success/no-op logging is absent, and
invariant refusal bundles have a 1 MiB envelope. The cap as the north star states it also covers
BepInEx's own retention and explicitly armed diagnostic sessions. BepInEx owns `LogOutput.log`, while
full traces intentionally have no live byte cutoff, so the combined mandate remains open even though
ordinary release steady state is bounded by design.

## Worker-side profile stages

Measuring the frame projection and the ported formulas — the way the four action-boundary stages
already measure the native edge — is blocked rather than unscheduled: the profile probe is owner-thread
affine and a worker definition may hold no runtime-owned storage, so worker stages need a different
probe than the suite has.

## On-demand collection

Collection could wait until a service asks for a snapshot instead of running on a fixed interval. The
interval is already faster than any consumer evaluates, so the work this would save is cheap, and the
decision that justifies it is a measured one nobody has taken. It reopens on a measured full-pass
main-thread cost high enough that four passes a second is a visible frame cost, or on a consumer whose
interval is much slower than the collection one.

Skipping a cycle because the world has not changed is rejected outright rather than deferred: there is
no such thing as an unchanged world in this game, so the check would never fire, and it would throttle
every consumer to the collection rate — a behaviour change disguised as an infrastructure setting.

## An elapsed-time action budget

Auto Buy's per-turn limit of 16 attempts is a count cap, not a wall-clock one. Measured full slices stay
under a 16.7 ms frame in the large majority of cases but not all, and a synchronous native call can
overrun after it starts. A budget should decline to *begin* another action once its slice is exhausted.

## Eliminating reflection at the native boundary

The current binding caches dispatch but keeps runtime discovery. A later pass should decompile the game
for the exact concrete types and member layout, then bind fully statically against publicized references
— removing the one-time audit cost and the delegate indirection, and showing whether the game exposes
cheaper bulk-read entry points the suite reconstructs candidate by candidate. See
[native action surfaces](../reverse-engineering/native-action-surfaces.md).

The graceful-degradation guards (runtime audit, "contract not audited" fallbacks, per-invoke try/catch)
exist so a game update fails soft. Since the suite quarantines mutation against an unaudited build
unless the player explicitly accepts that exact pair, removing them is coherent only if the
acknowledged-build path stays fail-closed at each adapter. Quantify the per-candidate cost first.

## Auto Buy's parked items

- **Structured "X of Y levels" in the trace and journal.** Routine per-action text narration is not
  emitted, and the compact journal deliberately carries only one outcome sentinel. The shared
  `NativeMutationCallOutcome` is a *call*-count model whose coherence invariant
  (`committed == attempts` for a verified action) cannot express "3 of 5 levels", so carrying it needs a
  new feature-scoped units field on the shared `ServiceActionResult` plus recorder and journal plumbing.
  It cuts across the Auto Harvest action path too, where "harvested X of Y" would use the same field, so
  it is a real contract decision. The gather → attempt → outcome decision chart is blocked on it.
- **Deliberate worker overcommit.** Emitting roughly twice the usable queue room would let multi-frame
  draining overlap native queue consumption — action-list overcommit, not multiple native mutations
  inside one action. Its value is doubtful now that each turn drains a bounded slice, because an
  ordinary native queue cannot free slots within one tick.
- **Operator-defined grouping modes.** `Single` and `Fixed` still request one level; they are
  operator-set counts rather than a native mechanism. `BulkDevelopment` raises a Structure's preferred
  level count to the game's own, capped at 100, and Upgrades intentionally remain one level.
- **Dynamic grouping beyond the native preferred count.** The worker descends from the live Bulk
  Development preference and emits the largest positive count whose exact published rising-cost sum
  clears the remaining batch ledger; an intermediate count with no exact published sum is refused, and
  the one-level row is the safe floor. Expanding beyond the player's preference would be new policy.
- **Replacing the last game call on the action path.** `CanPurchase()` folds availability, level caps,
  price and per-level prerequisites, each of which can move between the world the worker planned from
  and the mutation. The cost chain is ported and `GetTrueQuantity` collected, so the open half is only
  whether that boundary call should become a formula — blocked on serialized prerequisite and topology
  evidence portable tooling cannot extract. Asking the game at the moment of mutation carries no parity
  risk.
- **Worker-side `LeaveQueueSlots` subtraction.** The worker plans against raw captured room and the
  action adapter is the sole authority that reserves slots. Purchases are identical either way; the
  worker over-plans by up to the reserve, costing one extra rejected journal entry per full-queue cycle.
- **Reserve semantics.** An empty or malformed `AbsoluteReserve` resolves to the config default of 0.
  The whole reserve model is flagged for a later redesign.

## Open questions

- **What native `Purchase()` reports on failure.** `StructureSO.Purchase(bool)` and
  `UpgradeSO.Purchase()` both return void, so the adapter diffs the queued level before and after. In
  live play `CanPurchase()` returns true, `Purchase()` is called, no exception is thrown, and yet the
  queued-level delta is zero for roughly a fifth of attempts — either the game's `CanPurchase` and
  `Purchase` disagree, or the queued-level counter is the wrong success signal for some purchase shapes.
  The decision that follows is whether to keep post-verification, cheapen it, or drop it.
- **Where cost decoding spends its time.** Cost decoding and resource-state reads are the clearest
  remaining hot path and belong to world collection. The suspicion is reflection-based BigDouble field
  extraction plus per-resource-row reads; investigate cached delegates, batched or flattened row reads,
  or structure-of-arrays buffers. SIMD does not apply — `BigDouble` is a mantissa-and-exponent object
  graph, not a flat numeric array — but flattening the grab loop may.

## How this work is done

Every performance question here must leave the trace dashboard able to answer it directly on future
captures; extend the dashboard alongside the investigation rather than leaving the answer in a one-off
analysis. Land one behaviour or performance change at a time, and after each run the portable and
real-reference gates, install a profiler-enabled build, and capture one comparable session before
starting the next.
