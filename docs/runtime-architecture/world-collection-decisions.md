# Collection quirks

The numbered constraints of world collection that source comments cite and no other document states.
Entries are `W<n>`; numbers are never reused and never renumbered. Each states the live constraint, not
how it was arrived at. [`world-collection.md`](world-collection.md) and
[`service-data-flow.md`](service-data-flow.md) hold the rules and win wherever an entry disagrees.

[Back to dossier](README.md)

## W1 — The published snapshot is a fresh allocation per cycle

One row array per category, copied into the published table. Immutability must be structural because
several workers read on their own threads: double buffering would make it depend on a reader finishing
before the writer wraps, an invariant nobody can check locally that fails silently and rarely. The
derive scratch is reused, so that copy is the only allocation surviving the cycle.

## W4 — Publication is an action, and a result declares its effect

A publishing action carries the channel and generation it published and contributes truthful **zeroes**
to `ServiceNativeCallTotals`. The published count defaults to zero, so a publishing batch that forgets
to declare itself is refused. The pipeline's property is *no action may claim a native mutation it
cannot prove*, not *every committed action is a native mutation*. Routing publication through the pump
puts the world change at one point, on one thread, before any consumer decides that frame. Accepted
consequence: `DispatchActions` returns early under emergency stop, so stopping freezes the world
generation and every world-bound service with it.

## W5 — A modifier record is read the way the game reads it

A modifier record is read through the same dirty/memo decision as the native accessor, without
calling the accessor that mutates its memo. Neither unconditional recomputation nor unconditional
memo reads satisfy [D16](world-collection.md#d16-the-suite-owns-transcribed-economy-math-gated-by-an-assembly-hash).
The active flag is checked before arithmetic; a clean record's stored value remains authoritative.

## W26 — Every source directory is an audit root from the start

A root the reflection audit does not walk reports nothing, which is indistinguishable from having
nothing to report. `sourceAudit.roots` lists every source directory; the cost of a missing one is
silence, not failure.

## W36 — Plot-and-action pairs are their own table

A pair is not an entity: neither side owns it and every term is a function of both, so `PlotActions` is
keyed by the composite. **Both sides are counted, not flagged** — a plot can name the same action twice
and can hold an instance of one it does not offer.

**`Prerequisites.Container.Check()` is a write.** It stamps `gameId` and latches `available`, so the
field carries the same answer without the write — third accessor that looks like a question and is not,
alongside the [no-write collection rule](world-collection.md#reading-rules). Reading the latch errs one way only: a
prerequisite satisfiable since the game last asked reads as unmet, the right direction for a gate.

**One branch of `GetElementCost` is not ported and fails closed.** `GetGivenCostIncrease()` is a
product over uncollected `sizeModNodes`; with a non-empty list the row publishes
`ElementCostKnown = false` rather than the unscaled cost, which would be too cheap in the one direction
that starts a run the plot cannot pay for. An `IdObjectRef` is read and parsed without writing the memo
back, because off a save load a reader trusting the memoised `_guid` sees a plot with no running
actions. `GetMaximumRemInstances()` branches on the *authored* `elementCost` and is clamped at zero
here, deliberately differing from the game: "how many more runs fit" has no reading below none.

## W39 — An audited member nobody reads is an audit of nothing

`CanPurchase()` and the queue's capacity and free room are not decision inputs: the boundary
re-resolves, re-checks admission and re-reads live room before mutating, so a captured copy is a
pre-filter over an answer taken again anyway. `FillAvailableQueue` means "keep going until only
`LeaveQueueSlots` remain", which only the boundary can know. Contracts losing their only reader are
deleted, not orphaned.

## W40 — Safety precedes queue admission

Safety is checked before queue room: an unsafe action remains unsafe even when the queue has
space. A recording's codec version describes the policy its evidence can support; changing a wire
contract requires the version owned by the codec.

## W43 — Cost ordering uses stable identity

Auto Buy ranks by cost ratio and stable UUID. [The evaluator](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/AutoBuy/ServiceCycle/Application/AutoBuyCycleEvaluator.cs)
owns ordering; no parallel effect-priority roster is maintained here.

## W45 — A failure's scope decides how far it stops the feature

The contract circuit stays at the action boundary; what it means reaches the worker as a result code.
`PairContractUnavailable` and `FeatureContractUnavailable` name a mismatch the game proved, and how far it
reaches decides whether one pair or the whole feature is held for the rest of the lifecycle. A scope is
claimed only for a proven mismatch: a read that only threw stays an unattributed adapter fault and is
retried. Every other fault, `PairFaulted` included, backs its pair off on the shared retry gate and is not
remembered by the worker.

## W46 — Action-family ownership is checked where the action happens

No frame carries whether this instance owns the family it would act on; both action adapters re-read the
lease before mutating, because a lease another plugin can take mid-cycle is a property of the live
runtime. The check runs per kind — holding the structure lease says nothing about upgrades — and
declines with `ActionFamilyUnavailable`, a rejection rather than a fault, since standing down for the
owning plugin is the arbitration working.

## W49 — The runtime stamps the strategy generation, before capture runs

The generation belongs to whoever owns the publisher, so a capture result carries none. It is pinned
before `Capture` and stamped into `ServiceCaptureContext`, closing the hole where a service publishing
a bulletin inside its own capture hands back the generation it just created and the runtime records a
cycle as consulting a bulletin that did not exist when it opened. An unbound service is stamped
`StrategyGeneration.Initial`, which is one, because zero throws on the trace identity; `Reported()`
answers `default`, so diagnostics read *no* strategy rather than a neutral one.

## W50 — The registry owns the one world, and hands it to both halves of a cycle

`ServiceWorldPublisher` is a field of `ServiceCycleRegistry`: one game, one world, and a feature owning
the instance made the game's world one feature's property. **A service is given the write half or
nothing** — `WorldPublication` exposes `Publish` and no way to read back, because a collector that
could also read would be a second, unpinned path to the world.
`ServiceCycleStartCoordinator` reads it once at the top of the attempt and hands the same snapshot to
both halves. A consumer's frame projection is consequently a static, field-free class taking the world
as an argument: a worker may hold no collection, and holding the world across cycles must not compile.

**One branch is open and named.** Re-reading the publication on the coordinator's *deferred* path —
taken when the non-blocking probe cannot publish because the previous response has not drained — is
uncovered. If it ever re-reads, a cycle would evaluate against a snapshot its own capture never saw and
nothing downstream would notice.

## W51 — A worker definition may hold no runtime-owned storage

The profile probe is runtime-owned storage and cannot follow feature projection onto the worker.
[The span enumeration](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/Runtime/ServiceCycle/Observation/Profile/ServiceCycleProfileSpan.cs)
and [record codec](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/Runtime/ServiceCycle/Observation/Profile/Format/ServiceCycleProfileRecordCodec.cs)
own stage identities and reserved wire space; incompatible evidence cannot be reinterpreted by
reassigning an unused number.

## W53 — Both action queues are published, and a queue reading still admits nothing

Services compete for the same slots and consume them with their own actions, so a collected reading is
wrong *within* one world generation, and the freshness gate cannot help because it is per-consumer — it
answers "has the world been re-read since *I* acted". That is decisive about what may admit an action
into a slot and says nothing about what a plan may be *shaped* by; a plan that cannot see the queue is
not safer, only blinder. So the reading is published and the authority is not: `GetRemainingRoom()` is
read live before every Auto Buy submission and the active-action list before every harvest submission.

**There are two queues** with different shapes, so a single category would have had to average them:
the plot-action queue publishes a row per slot, the attribute queue is occupancy plus an edge to the
`IntVariable` holding its maximum. Neither is reached by a registry walk — a list variable's `All` is
declared on its generic base and the member binder does not walk base types, so both resolve by uuid
through the identity registry, keeping the action-manager singleton out of the collector. A queue is an
entity and a slot is not.

## W58 — Per-level requirements need authored rows

A parameterized prerequisite depends on the proposed level, so a stored no-argument latch cannot
answer the same question. [Capture](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/Runtime/World/Categories/WorldEntityRequirementReader.cs)
publishes the authored program for [the shared evaluator](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/Runtime/World/WorldRequirementEvaluator.cs)
to fold against one immutable world. The evaluator owns supported comparisons and explicit
unevaluable cases; the [refusal tests](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/tests/OrbModding.Tests/Runtime/World/WorldRequirementRefusalTests.cs)
check that boundary without a copied discriminant or enum-member census in this document.

The native parameterized call remains a differential oracle, not a published admission verdict.
[World collection](world-collection.md#layout) owns graph capture, missing-container semantics and
reference guards; [requirements](../reverse-engineering/requirements.md) explains the native latch.

## W59 — Mastery readiness is the game's own answer, because the threshold is not published

`WorldSpellRecipe.MasteryLevelReady` is `SpellRecipeSO.IsReadyToLevelMastery()`. Every other mastery
track publishes both halves and lets the worker subtract, but a spell's threshold lives in a
`masteryXpContainer` the snapshot does not publish and whose members are not in the manifest, so the
composed boolean is the only readable form of the fact rather than a shortcut around a number that
exists. **It is a read:** a parameterless predicate that composes state and returns is the same shape
as `UpgradeSO.IsAvailable()`, which separates it from the `Check()` family W36 refused.

**The prerequisite half deliberately does not follow.** `levelingPrerequisites` is reachable only
through the latching `Check()`, so it is re-read at the action boundary — not a shortfall, since the
boundary is the authority and a planner that cannot see the gate plans a level the boundary refuses
penalty-free. What the snapshot buys is that the planner does not propose a spell with no banked
experience. **Rejected:** a spell-*level* cost table, which would have to call `GetLevelCost()` during
collection or port unaudited arithmetic, when affordability is re-read at the boundary anyway.

## W60 — The equipped loadout is a category, reached by uuid and keyed by position

`SpellSlots` publishes one row per readable loadout position and `SpellCosts` what casting out of each
costs, both from one reader, because a slot's price is only answerable from the same equipped instance
the slot was read from. **Reached by uuid, not through the singleton:** `SpellManager.activeSpells` is
an ordinary list variable with its own uuid, so collection touches no spell manager and the one
singleton read stays at the action boundary. **Not a `WorldPlainBinder`, deliberately** — a plain
binder declares `TypeName => "Spell"`, which would oblige declaring every scalar and modifier-record
field on a runtime instance nobody has audited; a bespoke reader asks for exactly the sixteen answers
wanted and declares each.

**The position is the key, and the holes are real.** `SlotIndex` is the game's own index — the number
`FireSpellIndex` takes — counting unfilled positions exactly as the game does. A hole publishes no row;
an empty-but-present slot publishes `Occupied` false. Both read as "nothing to cast here", the
direction a missed reading should fail in. `CastReady` is `Spell.CanCast()` on W59's licence, and its
three terms are the game's own classification of a refusal, so a planner can both rank and explain.
**Priced per position, not per recipe:** a spell's cost is its recipe's authored cost after the equipped
instance's modifier chain, so a recipe-keyed table would be wrong about one of the two answers for a
spell equipped twice. **Rejected:** publishing the loadout as identity-keyed equipped recipes, a
strictly weaker fact that cannot say which position a spell sits in when a cast is addressed by
position.
