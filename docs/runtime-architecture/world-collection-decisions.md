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

`GetValue()` is `calculationDirty ? Calculate() : calculatedValue`; both halves are reproduced without
calling it, because calling it recalculates and re-stamps observables. Specified as
[D16](world-collection.md). Neither half is the rule alone, and each shipped alone once: the raw memo
published the `[NonSerialized]` zero of an untouched record, pricing all 180 structures at nothing;
unconditional folding "corrected" `passiveCostMod` from its memo of 0 to its base of 100, over-pricing
every structure by 1.25^levels, because a record with no modifiers is never dirtied and its memo *is*
the price. "Dirty" and "never calculated" being one flag is therefore safe. A live run measured 40–45%
dirty at read time (consumables 100%, alchemy types 0%), so most reads are a flag test and a field
load — which is what makes exactness affordable at ~5,000 records a collection.

## W8 — A cost can name a resource that is not in `ResourceSO.All`

Element-owned resources are created at runtime and never registered, so `TryFindResource` searches
`Resources` then `HarvestResources`. A resource in neither still fails the candidate, deliberately: a
missing row means the pass could not read it, and treating unreadable as free buys the unaffordable.

## W26 — Every source directory is an audit root from the start

A root the reflection audit does not walk reports nothing, which is indistinguishable from having
nothing to report. `sourceAudit.roots` lists every source directory; the cost of a missing one is
silence, not failure.

## W28 — Frame-wide terms live on the frame, and degrade to their own identities

The six inputs belonging to no single entity ride on `WorldFrameGlobals`, so resource derivers are
per-cycle instances rather than shared singletons — a shared instance holding mutable globals would let
the next collection rewrite terms a worker was still deriving against. Binding failure degrades per
term and never throws, but **neutral is not zero everywhere**: `structureCost` multiplies, so a zeroed
one prices every structure at nothing and it degrades to one; `attributeQualityBonus` is an exponent
whose identity is zero, since `Pow(quality, 1)` would divide the price by the quality. The `*HasActive`
flags are modifier counts, not zero-checks — the chain branches on whether a term *participates*, which
differs from contributing zero for a stack that cancels out.

## W35 — A plot node's quantities are summed during collection

`GetQuantity()` and `GetTotalQuantity()` reach the list through `GetPhaseInstance(phase)`, which lazily
caches *and creates a missing instance* — a write from a pass that does not write. The total sums
**authored phases, not instances**: the game iterates `phaseInfos`, so summing the instance list would
count a phase the node does not author and put the suite out of step with the game's own calculations.
`GetRemainingQuantity()` is asymmetric — main usage comes off the idle count, any usage is absorbed
first by whatever is busy — and is left free to go negative, because the game's callers compare against
zero rather than clamping.

## W36 — Plot-and-action pairs are their own table

A pair is not an entity: neither side owns it and every term is a function of both, so `PlotActions` is
keyed by the composite. **Both sides are counted, not flagged** — a plot can name the same action twice
and can hold an instance of one it no longer offers.

**`Prerequisites.Container.Check()` is a write.** It stamps `gameId` and latches `available`, so the
field carries the same answer without the write — third accessor that looks like a question and is not,
after D16's `GetValue()` and W35's `GetPhaseInstance()`. Reading the latch errs one way only: a
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

## W40 — Retired

Superseded by W54 on where an action's audited safety is checked. Two sub-decisions still hold: safety
is checked *before* the queue, because an unsafe action is unsafe whether or not there is room and a
rejection naming the full queue reads as "try again later" for something that must never be submitted;
and a codec version moves with the record rather than the layout being widened, because an older
recording decodes to evidence the current policy would not have decided from.

## W43 — Retired

Auto Buy ranks by cost ratio and stable UUID. The configuration key, policy, world category, native
reads, and tests for the structure-effect priority tier were deleted together.

## W45 — A failure's scope decides how far it stops the feature

The quarantine and contract circuit stay at the action boundary; what they mean reaches the worker as a
result code. `PairContractUnavailable`, `FeatureContractUnavailable` and `PairFaulted` replace one
undifferentiated `AdapterFault`, because how far a failure reaches decides whether one pair or the whole
feature stops being tried. A scope is claimed only when the circuit was actually tripped — a resolution
that failed without tripping it stays an unattributed adapter fault rather than being reported as a
contract the build does not have.

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

The profile probe is runtime-owned storage, so the three capture-side stages could not follow the frame
projection onto the worker. **A retired profile stage code is burned rather than reused:** codes
1003–1005 are gone, the record keeps its fixed size with the retired slots as zero padding the reader
checks, and the trace tool's decoder arms retire in the same commit, since the gate builds that tool
under the profiler symbol and a split would fail it rather than rot quietly.

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

## W54 — Facts in the collector, classification in the service

An action's audited structural safety is published as its *terms* — plot authoring, phase descriptors,
audited scalars, authored completion blocks — never as a verdict, because a summary of a whole-graph
check is a conclusion the deriver cannot justify. The verdict is a pure function of the snapshot
computed on the worker from the same world the plan was made against, and carried to the boundary on
the action rather than looked up there. Two comparisons are weaker than the reference-equality checks
they replace, since a row carries identities rather than objects; what catches an object replaced by
one with the same uuid is the collected epoch (W55), so the trade holds only while the five lifecycle
hooks live.

## W55 — The snapshot says which run of the game it describes

`CollectedAtEpoch` is stamped at capture from the lifecycle monitor's generation. A generation says
*when* — a frame number, monotonic within one run — and cannot say the run itself was replaced, which
is what W54's identity comparisons need told. **It makes structural readings skippable:** plot
authoring, effect blocks and entity requirements are skipped when the same frame object is filled at
the same epoch it was last filled at. Skipping means skipping the *native reads*, not the derivation —
buffers are left alone rather than reset, tables rebuild every cycle from unchanged samples, and a
skipped category returns the report of the pass that actually read it.

**What it cannot do:** detect a save-load that changes no identity, because nothing has measured
whether one occurs. The five `PatchOptional` lifecycle hooks are the signal, and they are the
*intended* signal for "we are moving to a new game, trash everything". `AutoHarvestBindingCoherence`
stays anchored on resolver lifecycle generations, because what it guards is whether a registry
resolution is still current — a fact about the resolver, not a snapshot. Auto Buy's boundary compares
against the snapshot's epoch ferried in on the action, so a world collected under a new epoch is not
refused while runner replacement lags; a plan made against a run the game has left still cannot submit,
and neither can one carrying a zero epoch.

## W56 — The patches that survive, and what each waits on

A service woken by a generation gate needs no second way to be told, so a service consuming the
published world carries no signal patch: keeping a patch-fed generation beside the collected world
would create two answers to one question, and structure and upgrade completion announcements, queue
postfixes, `AfterNativeCompletion` and Auto Concept's signal patches are each an instance of that rule.

Three groups survive it. `SpellFirePatch` is the before/after probe of `NativeMutationVerifier.Execute`
for Auto Cast's fire, which is the declared verifier exception, since a verifier that may not observe
the game cannot verify anything. The five lifecycle hooks wait on the experiment W55 defers. The four
mastery hooks registered from `ComposeMentor` are exact-XP **inputs**, not a signal the world already
carries: a native rollover consumes saved XP and Mentor's own grant writes the same value, so no
snapshot delta recovers what was earned. They publish value-only rows with the world and retire only
with Mentor itself.

## W57 — Lifecycle observation installs with Automata, not with Mentor

The five optional lifecycle hooks — `SaveStateManager:ImplementLoadedJson` prefix and postfix,
`GameManager:InitGame`, `GameManager:ResetGameState`, `PersistentResetManager:PersistentResetLogic` —
install from `ComposeAutomata`, enumerated in `Plugin.LifecycleObservationHooks`. Behind Mentor's early
returns an unavailable mastery hook silently blocks lifecycle observation, and what that costs is not
Mentor's to spend: the monitor's generation stops moving, the collected epoch freezes with it, the
structural-fact skip never re-reads after a save-load, and Auto Buy's boundary compares one stale epoch
against another and admits. A degraded mastery feature is still a feature; a frozen epoch is every
consumer deciding against a world the game has left. The three `SpellManager` loadout postfixes stay
Mentor's own signal. The list is a `(Target, Handler, Postfix)` tuple table because one target carries
both a prefix and a postfix.

## W58 — The per-level prerequisite is rows, because the game's own answer takes an argument

`WorldEntityRequirement` is one row per authored condition on an entity's *next* level, read once per
lifecycle. An entity's two containers answer different questions: `prerequisites` gates the entity at
all and latches into the published `available` field, while `prerequisitesPerLevel` gates the level
being bought — `Check(level + queuedLevels + 1)` for an upgrade, `Check(quantity)` for a structure — so
there is no field to read and no parameterless call to make.

**The gating container is rows too, under its own program.** A published `available: false` says a
row is locked and nothing more, and "what would unlock it" is the question a player actually asks, so
`UpgradeSO.prerequisites`, `StructureSO.prerequisites` and both of `ResearchSO`'s visibility
containers are read as a third program kind, `Unlock`. Reading them is fields only — the no-argument
`Check()` that answers them is the write W36 refused, and it stays refused: the program is evaluated
at level nought because that is the level that overload's own `ConditionInfo` carries, and the
container's `adjustValue`, which only that overload reads, rides on each row and is added to the
leveled threshold the way `ConditionValueInstance` adds it. A Research's two containers are ANDed by
the game, so the second one's group ordinals continue after the first's and the existing all-of fold
across group positions *is* that AND — no new node kind, no operator row.

**What is published is the conditions, not the verdict.** Every value a condition compares against is
already a row in the same snapshot, so the verdict is arithmetic a worker can do, and doing it there
keeps the snapshot free of an answer only true at one level. It also leaves the rows for consumers that
want the fact: "this upgrade waits on that research reaching six" is what chain planning needs and a
boolean throws away. The row carries its owner's registry, because the level a container is checked at
is a property of the owner.

**Eight of the twenty-six comparisons are refused.** Six reach the latching no-argument `Check()` W36
logged as a write — none occurs in a per-level container on this baseline, but "none today" is what
needs a guard rather than a habit, and the guard is what carries the refusal into the gating
containers, where such a comparison publishes its `Unknown` row and leaves that entity's verdict
unevaluable rather than wrong. `SpellRequirement.MasteryLevelReady` asks for state the snapshot did
not publish (W59 adds it). `GenericRequirement.Discovered` targets an arbitrary `UpgradeableObject`
whose `IsDiscovered()` is virtual across six implementers reading different fields, and a row carries
an identity rather than a type, so there is no way to pick the right override — the same ground
`GenericRequirement.Level` is refused on. The remaining eighteen are modelled. **Unknown is a row, not
an absence:** an unaudited condition class publishes a row of kind `Unknown` and the pass reports
itself incomplete, because an entity with no rows reads as unconditional — the wrong answer for one
gated by something nobody modelled.

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
