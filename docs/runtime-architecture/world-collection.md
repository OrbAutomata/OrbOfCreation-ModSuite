# Shared world collection

One registered service reads the whole game once per [pace](#pace) interval and publishes an immutable snapshot every
other service consumes. The game is read once, by one reader, at one instant, and the cost is paid once.

[Back to dossier](README.md) · [How a service gets its data](service-data-flow.md) ·
[Collection quirks](world-collection-decisions.md)

This document describes the world publication specifically. The rule it serves — a service receives
configuration, world state and strategy and nothing else — is stated in
[service-data-flow.md](service-data-flow.md), which is the authority if the two disagree.

## The pipeline

```
Category readers over the game's registries
        │  Unity thread, once per pace interval (250 ms at Fastest)
        ▼
GameWorldCollector ──fills──► GameWorldCycleFrame
                                (samples, frame globals, collected frame, collected epoch)
        │  worker thread
        ▼
GameWorldStateDeriver ──► GameWorldState ──► one publish action
        │  back on the Unity thread, dispatched before any service starts
        ▼
ServiceCycleRegistry's ServiceWorldPublisher
        │  pinned once per cycle by the runtime, at cycle start
        ▼
ServiceCaptureContext.World  and  IServiceCycleWorkerDefinition.Evaluate(…, world, …)
```

- **Collector** compiles one accessor per member per category against the loaded assembly and answers
  each modifier record the way the game's own accessor would without calling it. It is the only stage
  that touches a native object.
- **Frame** carries raw samples across the thread boundary. It cannot be the collector, because a
  service frame is structurally forbidden from storing delegates and the collector is almost entirely
  delegates.
- **Identity catalog** validates and copies the live runtime registry once per lifecycle on the
  Unity thread. The frame and derived world carry that exact immutable snapshot reference; later
  captures neither enumerate it again nor copy its rows into category tables.
- **Deriver** turns samples into rows — the ported half of the game's own math, including the full
  resource rate chain — on the worker, holding no native surface to reach for. It is pure and total:
  unreadable arithmetic fails neutral, so a NaN operand yields uncapped, zero-headroom output rather
  than a fabricated bound.
- **Publish action** carries the derived snapshot back to Unity. Publishing is an action rather than
  something the worker does directly, so the live world changes at exactly one point in the pump, on
  one thread, before any service decides anything that frame ([W4](world-collection-decisions.md)).
- **Publisher** swaps the reference under the generation the snapshot was *collected* at. Latest wins;
  nothing published is ever written again.
- **The pin** is the runtime's, not the consumer's. `ServiceCycleStartCoordinator` reads the
  publication once when it opens a cycle and hands that snapshot to both halves, so a service cannot
  evaluate against one snapshot and act against another ([W50](world-collection-decisions.md)).

Frame-wide inputs belong to [WorldFrameGlobals](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/Runtime/World/WorldFrameGlobals.cs) and are stamped onto the captured frame. The model owns the input inventory; resource derivation receives the values captured for that frame.

## Prospective level economy

The [level economy publication](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/Runtime/World/Categories/WorldLevelEconomy.cs)
carries current and next-level quotes, availability of each quote and exact incompleteness reasons.
Capture reads native-free operands without changing a live dial; shared worker derivation applies the
owned spell-cost, alchemy-drain and plot-scaling math to them. A guessed level multiplier never fills
a missing quote. Resource publication retains the unclamped net-regeneration coordinate needed to
compare immediate spell costs independently of a full resource bar.

[Auto Levels](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/AutoLevels/README.md) consumes this publication; its policy and current-level
mutation guard stay outside collection. [The native boundary](game-boundary-doctrine.md) continues to
own lifecycle, current control availability and final mutation authority.

## Reading rules

**Reproduce the accessor, never call it.** `ValueModifierRecord.GetValue()` calls `Calculate()` when
dirty, which writes `calculatedValue`, clears `calculationDirty`, and re-stamps observables — mutating
game state on the suite's schedule. Every modifier is read as `GetValue()` would answer it and never
through it. This is the memo rule, **D16** below.

**Enumerate the runtime type, not the save record.** A save record describes what was written to disk;
the runtime `ScriptableObject` describes what the game is currently playing. **D17** below.

**A generation is a frame number, and it is the frame the game was read on** — not the frame the
snapshot finished deriving on. Only the first lets a consumer ask "has the world been re-read since I
last changed it" and get the right answer.

**An epoch says which run of the game a snapshot describes.** `CollectedAtEpoch` is stamped at capture
from the lifecycle monitor's generation. A generation says *when*; only the epoch can say the run
itself was replaced ([lifecycle ownership](#frame-and-lifecycle-ownership)).

**A lifecycle boundary trashes the publication.** Every accepted lifecycle replacement — scene change,
save load, reset, new game plus — flushes the shared world publication back to the state it was
constructed in: generation 1, the empty world, no collection timestamp. A snapshot reads objects the
boundary destroys, so keeping it answers for a run that has ended. Loading a save starts from
nothing, and "before the first run" and "after the run ended" are the same reading. The flush lives
in the registry's own lifecycle request, so no caller can forget it.

**An expected native failure degrades one row or one category; anything else faults the pass.**
Binding failures are reported per category in `WorldCollectionReport` with the member that could not
be found, and a build that renamed one field still publishes the other categories. At read time, an
exception `NativeFailureClassifier.IsExpected` accepts, which includes a reader's own
`InvalidOperationException` shape checks, costs the row or the category it hit. Any other exception is
a suite defect: it leaves `GameWorldCollector.Collect`, the world service records a capture fault and
retries with backoff, and no world publishes until a pass succeeds. Degradation is per-term and never
neutral-by-default ([frame-wide inputs](#frame-and-lifecycle-ownership)). A category that fails withdraws the rows it
had already read, and a frame-global accessor that did not bind is named in the report's
modifier-folding row.

## D16 — The suite owns transcribed economy math, gated by an assembly hash

Asking the game to recompute a derived value is not cheap, and the cost is not the reflection.
`StructureSO.GetNextCost()` chains four `ResourceCostList` transforms, each a LINQ projection into a
fresh list, and caches nothing. Measured against the shipped macOS build, evaluating that chain from
the same inputs in suite-owned code is roughly 11.5× faster per structure, bit-identical for every
entity in a real save, and allocates nothing where the game allocates about six lists per call.

Base values are a different matter. `GetQuantity()` returns a field. `ValueModifierRecord.GetValue()` is
reproduced rather than ported, because it is a branch before it is a computation:
`calculationDirty ? Calculate() : calculatedValue`. `Calculate()` runs an allocating LINQ pass over both
modifier dictionaries and then writes four fields of game state — the game recomputing and re-stamping
its own observable at whatever point in the frame the suite's pump happens to run. An accessor that
writes on read is a mutation however innocuous the write looks, and the suite does not mutate game
state outside the action boundary.

**So the reading is the memo rule, and the dirty flag decides it:** a clean record reads as its
`calculatedValue`, a dirty one as `Adjust(baseValue)` over both modifier sets, computed and not stored.
Neither half is the rule alone — see [W5](world-collection-decisions.md), where each half shipped alone
and cost a live failure in the opposite direction. **The number to publish is the number the game will
act on**, which makes this an exact reading rather than a tolerated stale one.

What makes owning the derived math tolerable is the gate, not the speed: the four conditions in
[goals and invariants](goals-and-invariants.md) are load-bearing together. Porting proceeds one layer at
a time, each proven before the next begins. `GetTrueRate()` has been through that door: the rate
chain is `GameResourceRateMath.GetTrueRate` over published inputs, and the rate parity pass is what
made removing the call legitimate. `IsAvailable()` has not, and remains a genuine game call for
exactly that reason, since stacking a second unverified transcription on an unverified first would
leave no way to attribute a differential failure to either.

### What the differential passes prove, and what they do not

`AutomataDifferentialVerificationControl.RunEverything()` is the list, and it is the whole list: the
world collection check, then one pass per ported chain — Concept drain, spell level cost, spell level
affordability, the spell type layer, structure cost, the upgrade cost curve, resource rate,
requirement verdicts for upgrades, structures, research and prerequisite-link tiers, Concept usage
prerequisites, and the two plot-node quantities. The world collection check additionally compares every published purchase price
and eligibility verdict against `GetPurchaseCost()` and `HasEnough()`, every resource's display
coordinate and capacity verdict, the ritual/consumable/resource predicates, reference edges,
identities, and cache staleness.

Every one of them reports a `VerificationFinding` rather than writing a line, and one renderer turns
the set into the answer: the verdict word first, then one line per check — the agreeing ones
included, so that a check which ran and a check which is missing cannot read alike — then one
`window:` line carrying everything that moves between two calls over an unchanged world. The
vocabulary and the rendering rules are in
[MCP tools](../development/mcp-suite-reference.md#checking-the-suites-math-against-the-game).

The differential verifier checks the composed consumable readiness verdict against its native oracle
outside collection, because that oracle can write while answering. Cost-scaling checks compare the
owned per-entry multiplication with the native cost-list result. [Verification](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/tree/main/src/Automata/Runtime/Verification)
owns the oracle set; collection performs no speculative oracle calls.

A passing suite is not a proof that everything published is right, and the gaps are named here so
that nobody has to infer them from a comparison count:

- **`WorldExactCostMath.TryCombinedExactCost`** has nothing to compare against. It sums the game's own
  captured amounts, and the native answer *is* the per-row amount it adds.
- **The clamped end of the upgrade curve.** The upgrade pass samples only levels the game would price
  without clamping, because reproducing `maxLevel - 1` to decide what to expect would put one
  transcription on both sides of the comparison.
- **The grouped multi-level structure projection.** Grouped amounts price several successive levels by
  advancing the committed quantity, and the game answers only for the level the structure stands on.
- **The cost and cooldown-speed halves of the spell type layer.** The power layer answers to
  `Spell.GetSpellTypePowerPercent()`; the other two run the same aggregate over the same set through
  a different fetch closure, and each would need its own declared oracle to be checked rather than
  assumed to follow.
- **Plot and harvest action element costs** — [the plot-action deriver](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/Runtime/World/Categories/WorldPlotAction.cs).
- **Derived level facts** — `committedLevel`, `effectiveLevel`, `developmentProgress`, `isBounded`,
  `isExhausted`, `remainingLevels`, `isDeveloping` — and the derived capacity facts other than the two
  the collection check now pins.
- **Crafting worker enrichment, purchase-view route admission, and each decision reader's derived
  half.** These compose published facts under suite policy rather than transcribing a native chain, so
  there is no single member to disagree with; portable tests and the reason codes each verdict carries
  are what cover them.

## D17 — World collection is derived from the runtime type, never from the save record

Every entity category has two shapes: the `ScriptableObject` it is at runtime, and the
`SaveDataBase<T>` record it serialises into. They are not the same set of fields, and the difference is
exactly the set of values this suite exists to read. A save record carries only what must survive a
restart, so everything the game recomputes on load is absent by construction, and a field list built
from `SaveData`/`ApplyTo` reads as a complete inventory while omitting the entire cached layer. It did:
the first pass over twenty-five categories was derived that way and missed 165 scalars and 125 modifier
records, including all six of `ResourceSO`'s rate terms and the cached `ConsumableSO.quantity` that
answers "how many do I have". The save record is a shortlist of what the game considers *state*, not
the list.

**Enumerate the runtime type.** Walk the declared instance members of the `ScriptableObject` itself,
collect the value-typed ones and the `ValueModifierRecord`s, and justify each omission rather than each
inclusion — the default is that a cached number the game keeps is a number some service will want,
because the game keeps it for the same reason.

**Private is not a signal.** `ConsumableSO.quantity`, `ResourceSO.inLossMode`,
`StructureSO.currentBuildTime` and `DiscoveryTreeSO.totalDiscoveredCount` are all private, all cached,
and all the exact number a consumer needs. Visibility describes the game's encapsulation, not the
value's usefulness, and compiled field accessors do not care.

**A count is a fixed-size fact about a variable-size thing.** An immutable publication cannot carry
`List<RitualEffectInstance>`, and deferring the list is often right; it is never a reason to defer
`ritualInstances.Count`, which is what "is this ritual currently running" actually means. Defer the
elements; collect the cardinality and any scalar the game itself derives from the collection. A
single-valued reference is likewise collectable — a `Guid` is a value type, so `AlchemyTypeSO.selectedLevel`
and every edge like it travels as an identity. Collecting entities but no edges leaves the snapshot
holding numbers it cannot attribute.

**Reachability is an edge the planner needs, not a boundary-only fact.** The snapshot publishes each
candidate's exact category/list/owning-view relation from both `ViewSO.relevantLists` and
`availableLists`, with a named fail-closed row when the relation is missing, unreadable, ambiguous or
contradictory — so the planner excludes content behind an unavailable owning view instead of
repeatedly proposing work only the action boundary can refuse. The boundary rebuilds the same
relation from live native objects immediately before payment. All views carrying one exact list form
that list's complete route and must be available, because a child view does not supersede its parent
screen's progression gate. Separate list identities remain alternate routes, and one completely
available route is sufficient. This distinction preserves authored global access without treating two
views of the same gated list as independent entrances.

**Only the session collector owns the live owning-view admission snapshot.**
[Session collection](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/Runtime/World/GameWorldCollector.cs) explicitly opts into that
publication. Diagnostic, verifier and test collectors cannot overwrite it. Publication requires a
positive lifecycle epoch, and each run's publication is announced in the always-on log. A stale or
zero epoch is never current admission evidence.

**A member's declaration site does not establish its lifetime.** A value maintained through another
object still belongs in collection even if its declaring type never assigns it. A member can be
superseded only by an exact audited accessor carrying the required fact. The
[category binders](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/tree/main/src/Common/Runtime/World/Categories) own those choices.

**A record that distributes is not a record that holds.** `OrderedMultiplierRecord` and
`MergingModifierRecord` derive from `ModifierRecord`, not `ValueModifierRecord`, and have no cached
value — they push modifiers, transformed, into the member records handed to them by `AddRecord`, so the
distributed effect reaches the snapshot through those members under the memo rule. What the distributor
alone knows is its own total, the `Adjust(100)` its tooltip prints as a percentage; `Adjust` is pure, so
computing it would not breach D16, but it needs the two variable-size modifier dictionaries. How loaded
each record is has exactly one home — the `type modifiers` category below, which carries it for all
fourteen taxonomies alike — so a type's own row carries no count of it.

**A reading the chain cannot price honestly publishes no price.** A zero `attributeCostMod` is
authored at parity, so whatever produced the zero, multiplying by it makes the entity free — the one
error direction that commits a consumer to a purchase it cannot pay for. A zero quality is the same
refusal from the other side, being the base of the power the modifier is divided by, and would
price at infinity. Either way the entity publishes no price, and a consumer that finds none falls
back rather than reading a zero as cheap.

### Frame and lifecycle ownership

Frame-wide terms are immutable inputs to that frame's derivation. Missing-term neutral values follow
the operation's identity rather than assuming every neutral is zero. Participation is distinct from
a numeric contribution that cancels to zero. [Frame globals](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/Runtime/World/WorldFrameGlobals.cs)
and the resource/cost derivers own defaults and active-term semantics.

Plot quantities sum authored phases without calling lazy accessors that could create instances.
Remaining quantity keeps the game's asymmetric usage semantics, including a negative remainder where
the native calculation allows one. [Plot derivation](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/Runtime/World/Categories/WorldPlotNode.cs)
owns that math. A cost can reference an element-owned resource outside the ordinary resource registry;
lookup must use its published resource table or fail the candidate, never treat a missing row as free.

Authored action safety is published as terms; a feature derives policy from those terms on the worker
and carries the decision back to the boundary. Stable UUIDs do not prove an object belongs to the
same run: the collected epoch must still match. Lifecycle observation installs independently of
Mentor's mastery-hook availability. [Plugin](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Plugin.cs) owns installation, and the
[lifecycle monitor](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/GameLifecycleMonitor.cs) owns accepted observations; no hook roster is
copied here.

Mastery hooks are exact-XP inputs rather than redundant world-change signals: native rollover and
a suite grant can both change saved XP, so a snapshot delta cannot establish what was earned.
[The mastery bridge](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Mentor/ServiceCycle/Native/MentorMasteryPatchBridge.cs) owns the input
hooks, with installation in Plugin. Tick-fault observation is separately governed by the
[diagnostic exception](game-boundary-doctrine.md#diagnostic-observe-and-rethrow-exception).

## Entity keywords

[Keyword capture](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/Runtime/World/Categories/WorldEntityKeyword.cs) owns membership
bindings. A keyword is an identity-bearing type asset, not a second tag table maintained by consumers.
Full type membership coexists with meaningful primary/core type relations; their display ordering is
a derivation, not an assumption that every native list composes in the same direction.

### Keyword ids resolve to words, and only through the display name

Keyword display names are player content. An empty display name must not fall back to a diagnostic
asset name, which would invent a word the game does not draw. Capture retains identity; the shared
identity catalog supplies the display word. The [identity rule](game-boundary-doctrine.md#live-entity-identity-catalog)
owns this distinction. Unreadable membership is withheld loudly rather than published as genuinely empty.

## Type-level modifiers, and the trap in reading them

[Type-modifier capture](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/Runtime/World/Categories/WorldTypeModifier.cs) owns record and
contribution inventories. Records that distribute modifiers are different from records holding a
member value. Capture publishes native-free contributions; the worker derives totals. Never
multiply a distributed total into a member that already contains it. The
[one-bonus rule](game-boundary-doctrine.md#a-type-total-and-a-member-value-are-one-bonus-and-the-names-say-so)
is specified in the boundary doctrine, including the spell-type exception.

### The reads that bound a type-level bonus

[The collector](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/Runtime/World/GameWorldCollector.cs) and
[type-modifier derivation](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/Runtime/World/Categories/WorldTypeModifier.cs) own the bound
inputs. Unreadable contributions remain unavailable rather than a plausible zero. Exact native
contracts and the differential verifier establish the owned calculation; the document maintains no
parallel taxonomy, native-member or record-count list.

## What is deliberately not collected

An immutable publication may not carry a list, and wrapping each list in an audited table is a
per-category design decision rather than a mechanical one. So the scalar half of these categories is
collected and the list half is not:

| Category | Not carried |
| --- | --- |
| Ritual | `ritualInstances`, `currentSpoils` |
| Plot node | `sizeMods` |
| Discovery tree | the discovered-identity list |

This is stated rather than silently omitted, because a consumer that assumed a row described its entity
completely would be wrong about exactly the part that says how many are in stock. Four cases that look
like they belong on that list do not:

- **A consumable's stock count.** The save record stores a `consumableCounts` list and derives the total
  on load; the runtime keeps that total in a plain cached int, and `Quantity` carries it.
- **A plot node's phase quantities.** `IdleQuantity` and `TotalQuantity` are summed out of
  `phaseInstances` during collection and `RemainingQuantity` derived on the worker, so the numbers
  travel while the instances do not. The game's own accessors are *not* how they are read: both reach
  the list through `GetPhaseInstance()`, which lazily caches and creates a missing instance, and
  collection does not write ([plot derivation](#frame-and-lifecycle-ownership)).
- **A plot's action instances.** They travel, one row each in `PlotActionInstances`, keyed by the pair
  and by the instance's position in the plot's own list — that position is the plot's, not a queue's.
- **A crafting recipe's authored edges and evaluated blockers.** `CraftingRecipes` is one entity row
  per concrete `CraftingRecipeSO`, with immutable nested tables for its crafting types, authored
  resource inputs, generated resource outputs, consumable completion outputs, and engagement-drain
  blocks. The Unity-thread reader invokes the native visibility, starting-quantity purchase,
  generated-output capacity, and necessary-drain evaluators through lifecycle-compiled bindings and
  copies only their values; the worker then enriches each resource edge from the already-derived
  `Resources` table in the same frame, never following a retained native reference.

**The live action queue** is collected — a queue is a list variable carrying its own uuid — and what is
deliberately absent is not the reading but the *authority*. A collected reading may shape a plan and
may never admit an action into a slot, because services compete for the slots they read
([W53](world-collection-decisions.md)).

## Acting twice on one world

A service that attempts a game-facing action must not decide again until the world has been re-read
since. A commit is absent from the pinned reading; a skip, rejection, or fault is just as important,
because the live action boundary has shown the facts which produced the plan were insufficient or
divergent. Planning another action from those facts means trusting a snapshot just proven unreliable.

The first world is the same problem in different clothes. At activation the published snapshot is the
seed one — an empty world, or a real collection whose prices the game has not finished cooking — and a
service deciding against it decides that nothing costs anything. So the gate is armed at birth too:
`ServiceCycleSlot.ArmWorldGate` raises the floor to the generation live when the runner became current,
at activation and again on lifecycle replacement.

This is enforced by the runtime, not by the services, and there is nothing to declare. The gate is
unconditional: every ordinary service is subject to it and none opts in.
`ServiceCycleSlot.RecordWorldInvalidation` raises the floor after every attempted game-facing dispatch,
whatever its terminal disposition, and `TryStartCycle` refuses to open the next cycle until the
published world generation is strictly newer than the floor. Both numbers are pump frames, so the
comparison is a plain `>`, and the floor only rises, so re-arming never forgives an earlier attempt.

A `Source` is exempt by shape, so collection never waits on itself. That is the only exemption:
committed, skipped, rejected, and faulted ordinary attempts all close the gate. Strictly-after is
deliberate — within a frame, actions dispatch before captures, so a snapshot stamped with our own
action's frame can contain it, but waiting one more collection is cheap next to acting again on
unreliable facts. A world source that cannot answer holds the service closed, because "unknown" is not
"fresh".

Nothing about this lives in a feature. `AutoBuyService.ShouldStart` answers only "is Auto Buy configured
to run". Only the generation crosses the seam (`IServiceWorldGenerationSource`), so this stays a
scheduling rule rather than a second way to read the world, and every held frame is recorded as a
`ServiceWorldGateDeferralFact`, because a held service is otherwise indistinguishable from a service
with nothing to do.

This deliberately couples every consumer to collection's health: if collection stalls, consumers that
have already acted stop acting rather than working from a world that is falling behind. Emergency stop
freezes the generation for the same reason and with the same effect.

## Pace

The player's **Automation pace** (`General.Pace`) sets how long collection waits after its last
publication before reading again: Fastest 250 ms, Fast 500 ms, Balanced 1 s, Relaxed 5 s, CpuSaver
30 s. Reading the game costs tens of milliseconds of Unity-thread time a pass, and every ordinary
service runs on publication and is held by the gate above until the next reading, so this one interval
paces the whole suite. `AutomataWorldCollectionService.IsDue` is the rule:

- A lifecycle that has read nothing yet reads at once, whatever the pace.
- A caller waiting on a world newer than its own press opens `AutomataWorldDemand`; while one is open,
  collection reads at the Fastest interval. The MCP post-state wait is that caller, so its one-second
  settlement holds at every pace.
- An automation's own action is not a waiter. Its postcondition is proved at the action boundary, and
  the gate holds it until the paced reading; pulling that reading forward would run every busy save at
  Fastest.
- "Not yet" is answered every 250 ms, so a pace change or a new waiter is seen within that, and a
  slower pace counts from the last reading.

MCP reads use the latest publication, so at a slow pace they can be up to one interval old.

## Layout

Category rows and binding live under [World/Categories](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/tree/main/src/Common/Runtime/World/Categories).
[The collector](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/Runtime/World/GameWorldCollector.cs) owns reader order and structural
classification. Its lifecycle rule applies to authored facts, including resource-panel membership;
paired mutable facts are captured on each ordinary pass. The classification is not repeated as a
hand-kept reader list.

[Relation buffers](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/Runtime/World/WorldCategoryMachinery.cs) cache each authored
publication until their contents change. [WorldAuthoredValue](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/Runtime/World/WorldAuthoredValue.cs)
retains cross-table authored derivation for its structural generation. A derived table joining live
facts is rebuilt for the current publication. Lifecycle replacement invalidates the captured epoch
and its native references.

The collector advances structural freshness only when the required authored readings delivered.
Expected native faults keep the cause and retry the affected capture; durable unavailable authored
data remains explicitly Unknown. An unexpected reader throw faults the entire pass. An unbound
reader reports its binding failure rather than silently pretending to have read an empty category.

The live entity-name catalog follows an even narrower lifecycle contract: it binds at the first
stable Playing capture after `RuntimeReady`, then reuses one UUID-sorted snapshot until lifecycle
replacement. It is attached metadata, not a fourth ServiceCycle publication and not a 250-ms reader;
its registry and fallback rules are normative in the
[game boundary doctrine](game-boundary-doctrine.md#live-entity-identity-catalog).

Most tables are one row per entity and are walked by the identity check, which asserts that no table
holds two rows under one identity and that no row went out unidentified. Sharing an identity between
tables is not what it checks: the per-owner detail tables key their rows by the entity they describe
on purpose, so that reaches the answer as a named, non-scoring line. Which tables the walk skips is
stated in exactly one place — `NotIdentityTables` in
[the identity walk tests](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/tests/OrbModding.Tests/Runtime/Verification/WorldIdentityWalkTests.cs) — because
every second reading of an entity another table already claims lands there. The following examples are partial; the test-owned exclusion set is authoritative:

- **Purchase costs.** One parameterized `WorldPurchaseCostReader` walks the structure and upgrade
  registries into the same buffer, admitting several rows per entity; the upgrade instance also reads
  its optional per-level `ModifierListRef` source. The collector, not either instance, resets that
  shared cost buffer.
  Read through `WorldPurchaseCostLookup`, which returns an entity's whole range, because a partial price
  is worse than none.
- **Plot actions.** A row is a *pair*, so it has no identity of its own. `WorldPlotAction.cs` walks the
  plot registry a second time and prices each pair against the plot's remaining quantity on the worker;
  read it through `WorldPlotActionLookup`, which searches the composite key.
- **Action queue slots.** `WorldActionQueue.cs` publishes two tables: a queue *is* an entity, so
  `ActionQueues` is walked like any other identity table, while `ActionQueueSlots` is keyed by queue and
  index and is exempt. Neither is reached by a registry walk — both queues resolve by uuid through the
  identity registry, which keeps the action-manager singleton out of the collector.
- **Entity requirements.** [The reader](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/Runtime/World/Categories/WorldEntityRequirementReader.cs)
  owns declared programs, containers and member bindings. Recipe Book admission uses the book's own
  prerequisite rather than the internal glyph's different program. The shared requirement row and
  [iterative evaluator](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/Runtime/World/WorldRequirementEvaluator.cs) retain the graph.
  Every program uses one graph shape: container, program, node ordinal, parent ordinal, depth and
  explicit AND/OR operator. Capture and the shared fold walk finite acyclic group trees iteratively,
  without an authored-depth limit or recursive group calls, including empty AND (Met) and empty OR
  (Unmet). Reference cycles and null conditions at any position publish durable Unknown leaves;
  they report incomplete authored data without requesting a retry. A null top-level condition list
  likewise publishes a durable Unknown for its owner/program/tier; a valid empty list keeps its
  normal truth. A declared required container that returns null uses that same scoped Unknown path;
  an undeclared program, such as a resource's NextLevel, remains legitimately absent. Both Research
  unlock operands retain separate ordinals even if either is unreadable. Prerequisite-link container
  references retain their separate cycle guard and 32-container expansion bound. Container threshold
  adjustments reach every descendant leaf. Research's two unlock containers retain distinct node ordinals under the implicit
  AND, and program filtering keeps unlock, next-level and usage gates separate. Auto Buy uses this
  same fold, so a satisfied deeply nested gate admits its next-level candidate. A failed native tier
  read discards that entire tier's prefix and publishes a scoped capture gap; later tiers are still
  read independently. An unreadable tier list gaps every tier of that owner. Expected native faults
  retain their full logged cause and retry in the same epoch; durable missing tier data remains
  visible and unevaluable while reusing its authored publication. List membership publication is
  atomic too: a failed member read cannot leave a partial list header or suppress a later attempt.
  Null static membership is a durable skipped shortfall and publishes no successful list header,
  so every owner referencing it remains Unevaluable. A real empty membership list still publishes
  its header and folds Unmet; null members inside a real list retain the native Any predicate's false
  contribution.
  The authored condition list is `[SerializeReference]`, so accessors compile per concrete condition class on first sight,
  and a class that does not bind yields a row of kind `Unknown` rather than none — an unmodelled
  condition must be visible as a requirement nobody can evaluate rather than as an entity with no
  requirements ([W58](world-collection-decisions.md)). The native parameterized `Check(ConditionInfo)`
  verdict is not published: the differential checker's requirement passes ask it for the registry
  they walk. It is a differential oracle, never a replacement for the graph and never an admission
  result. How that overload differs from the parameterless latch is
  recorded in [requirements](../reverse-engineering/requirements.md).
- **Recipe book glyphs and discovery pools.** [Glyph relations](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/Runtime/World/Categories/WorldRecipeBookGlyph.cs)
  and [pool relations](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/Runtime/World/Categories/WorldDiscoveryTreeBook.cs)
  own authored membership and its direction. A relation table is whole or withheld: a missing list
  cannot masquerade as a book that widens no pool.
- **Resource panel.** [Panel membership](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/Runtime/World/Categories/WorldResourceBar.cs)
  is captured once per lifecycle in native display order. Its changing resource values remain live
  world facts. A partial panel would resemble a smaller valid panel, so failed membership capture
  reports the category unavailable.
- **Spell slots and costs.** [Spell slots](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/Runtime/World/Categories/WorldSpellSlot.cs) publishes the equipped loadout and [spell costs](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/Runtime/World/Categories/WorldSpellCost.cs)
  what casting out of it costs, both from one reader, because a slot's price is only answerable from the
  same equipped instance the slot was read from. Neither is identity-keyed: a position may be unfilled
  and two may hold the same spell, so `SlotIndex` is the key and counts unfilled positions exactly as
  the game does, because it is the number a cast is addressed by
  ([W60](world-collection-decisions.md)).

World relation buffers share one append/reset/truncate primitive, with a transform-and-sort overload
for derived rows. Their lookups share one lower-bound search for identity, integer and composite keys;
category wrappers retain their signatures and miss conventions. Consumable count rows at level zero,
or with negative quantities, are omitted at capture and counted as Skipped with a named reason; valid
counts and the consumable scalar remain published. Native read exceptions are logged whole under a
fresh reference for each failed attempt. Collection reports carry the authored reason and that
reference, never exception text; an unexpected reader exception still faults the whole world pass.

A caught native read fault is retryable even when the category produced other rows: its structured
collection failure keeps the structural epoch gate open. Clearing the native failure recaptures and
invalidates the failed authored publication in the same epoch. Unsupported authored condition classes
are durable facts rather than read faults; their Unknown rows and skip counts remain cached until the
next structural read. A category's first read fault takes precedence over an earlier durable shortfall,
so its log reference stays visible. Repeated warnings compare category/outcome and the reason's target,
code and authored sentence; every failed attempt still logs the full exception under a fresh reference.
Purchase-view category, membership and live-availability failures follow the same rule and carry their
structured cause through the captured resolution and Auto Buy admission. Availability failures and
diagnostic reads retry on each call; they introduce no fault cache.

Challenge context retains its established first-failure precedence: an unavailable challenge-type
binding is the report's sole reason even when prestige capture also fails. The prestige exception
still receives its own full fault-log entry; retaining the first report reason never suppresses it.

Structural capture records prerequisite capture completeness separately from retryability. An
incomplete Alchemy identity mapping publishes a CaptureGap node for the affected Usage program and
container, retaining the upstream reason/reference in the report. Its empty owner UUID is scope
metadata for the identities that could not be mapped, never an entity identity. Evaluation checks
explicit gaps for the requested owner/program/container; a successfully read empty NextLevel or
Unlock scope stays Met even while Usage is incomplete. Other programs' rows cannot change that
answer. A missing affected Usage graph remains Unevaluable, so owned concept math refuses it.

An actual upstream native read fault keeps structural capture open and recovers in the same epoch;
subsequent healthy worlds share the recovered authored tables. Durable null, duplicate, empty-ID or
binding defects remain visibly incomplete and unevaluable, but their dependency does not invent a
native retry flag: structural generation and unrelated authored tables remain cached. Spell authoring
uses the same dependency primitive and withholds an incomplete positional identity mapping. Readers
see only this pass's completed upstream reports; a previous pass's cause is never reused.

Per-owner capture faults retain the stable UUID already obtained by that read, including effect,
plot, relation, member, cost and dynamic reader paths. Helpers return that same identity to their
catch scope; no extra native identity read is made for diagnostics. Before identity is established,
or for a global read with no entity identity, the failure target remains empty.

The source service requests level-economy capture only while Auto Levels is operational. Disabling
that feature clears its previous inputs and skips the additional forecast capture and derivation;
the shared world remains available to every other consumer.
