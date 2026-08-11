# Shared world collection

One registered service reads the whole game once per interval and publishes an immutable snapshot every
other service consumes. The game is read once, by one reader, at one instant, and the cost is paid once.

[Back to dossier](README.md) · [How a service gets its data](service-data-flow.md) ·
[Collection quirks](world-collection-decisions.md)

This document describes the world publication specifically. The rule it serves — a service receives
configuration, world state and strategy and nothing else — is stated in
[service-data-flow.md](service-data-flow.md), which is the authority if the two disagree.

## The pipeline

```
63 category readers over the game's registries
        │  Unity thread, once per 250 ms
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
  250-millisecond captures neither enumerate it again nor copy its rows into category tables.
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

Six inputs belong to no single entity and are read once per pass into a `WorldFrameGlobals` struct
stamped onto the frame: five player globals — resource overflow, overflow loss, reset time passed,
structure cost percent, attribute quality bonus — plus Unity's fixed delta time.

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
itself was replaced ([W55](world-collection-decisions.md)).

**A lifecycle boundary trashes the publication.** Every accepted lifecycle replacement — scene change,
save load, reset, new game plus — flushes the shared world publication back to the state it was
constructed in: generation 1, the empty world, no collection timestamp. A snapshot reads objects the
boundary destroys, so keeping it answers for a run that no longer exists. Loading a save starts from
nothing, and "before the first run" and "after the run ended" are the same reading. The flush lives
in the registry's own lifecycle request, so no caller can forget it.

**A missing member degrades one category, not the pass.** Binding failures are reported per category in
`WorldCollectionReport` with the member that could not be found, and a build that renamed one field
still publishes the other fifty-nine. Degradation is per-term and never neutral-by-default
([W28](world-collection-decisions.md)).

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
the set into the answer: the verdict word first, then only the checks that did not agree, then one
`window:` line carrying everything that moves between two calls over an unchanged world. The
vocabulary and the rendering rules are in
[MCP tools](../development/mcp-tools.md#checking-the-suites-math-against-the-game).

Two of those comparisons exist because a reading left capture and the rule is that the replacing math
is proved before the call is deleted. The composed `canFire` verdict is compared against native
`ConsumableSO.CanFire()` — a call that writes while it answers, which is why the oracle lives here and
not in the pass. And every cost reader that used to hand a scalar to `ResourceCostList.Multiply` now
multiplies each entry as it walks it, so the check scales a live authored cost list both ways and
compares entry for entry.

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
- **Plot and harvest action element costs** — `WorldPlotAction.TryComputeElementCost`.
- **Derived level facts** — `committedLevel`, `effectiveLevel`, `developmentProgress`, `isBounded`,
  `isExhausted`, `remainingLevels`, `isDeveloping` — and the derived capacity facts other than the two
  the collection check now pins.
- **A prerequisite container's `adjustValue`.** The parameterised overload the requirement oracle
  uses builds its `ConditionInfo` from the level alone, so both sides answer the unadjusted question
  and the comparison stays honest — but a container authored with a nonzero adjustment is a
  shortfall neither side models and the pass therefore cannot see
  (`AutomataRequirementVerifier.cs:56-61`).
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

**Only the session collector owns the live owning-view admission snapshot.** That snapshot is a
process-wide singleton the purchase action boundary reads its admissions from, and taking it is an
explicit, named opt-in — `GameWorldCollector.ForSession(...)`, used by the plugin's world-collection
feature and by nothing else. Every other collector — a diagnostic's, a verifier's, a test's — binds
its own resolver and cannot write the live snapshot however it is constructed. The inverse default
cost a live save twenty-seven minutes of refused purchases: throwaway collectors inside a
verification pass took the shared resolver and stamped it at the epoch their own frames carried,
which is zero, and the once-per-lifecycle structural gate meant nothing restamped it until the next
lifecycle. The resolver also refuses to publish under a non-positive lifecycle epoch, keeping
whatever the last real lifecycle stamped rather than replacing it with evidence no epoch can match.
Publication is announced once per run in the always-on log, naming the run and how many candidates it
admits, because that outage was invisible in the log and only readable from source.

**Do not sort members into runtime state and definition constants.** The tempting fourth rule is to skip
fields the game never writes. It was measured and rejected: classifying the 270 members remaining after
the first three rules by whether the declaring type assigns them put 186 in "runtime" and 84 in
"definition", and the definition bucket contained `HarvestElementSO.harvestRate` and
`AlchemyRecipeSO.cachedRequiredXp`. A per-member IL audit may supersede an orphan only when an exact
game-owned accessor replaces it, as `GetRequiredExperience()` does for required experience — a rule
whose failure mode is silently dropping an externally maintained value is the rule this decision exists
to replace. The reads are compiled delegates; the price is a wider row, and a wider row is the cheap
side of this trade.

**A record that distributes is not a record that holds.** `OrderedMultiplierRecord` and
`MergingModifierRecord` derive from `ModifierRecord`, not `ValueModifierRecord`, and have no cached
value — they push modifiers, transformed, into the member records handed to them by `AddRecord`, so the
distributed effect reaches the snapshot through those members under the memo rule. What the distributor
alone knows is its own total, the `Adjust(100)` its tooltip prints as a percentage; `Adjust` is pure, so
computing it would not breach D16, but it needs the two variable-size modifier dictionaries. Until a
named service wants that number, the row carries the active-modifier count.

**A reading the chain cannot price honestly publishes no price.** A zero `attributeCostMod` is
authored at parity, so whatever produced the zero, multiplying by it makes the entity free — the one
error direction that commits a consumer to a purchase it cannot pay for. A zero quality is the same
refusal from the other side, being the base of the power the modifier is divided by, and would
price at infinity. Either way the entity publishes no price, and a consumer that finds none falls
back rather than reading a zero as cheap.

## Entity keywords

The word line under a tooltip title is `ITooltipable.GetDisplayType()`, and sixteen classes author it
by joining the display names of a list of type assets — the words a player means by "all Cantrips" or
"every Druidry structure". A type asset is not a tag: it is a modifier-bearing sibling entity, so the
keyword and the bonus that rides on it are the same object.

`WorldEntityKeyword.cs` publishes that membership for the thirteen classes no other category binds,
as one table keyed by the entity rather than as a column on thirteen row structs, because the word
line is one player concept spanning all of them. Three of the sixteen are absent by design: research,
consumable, and spell-recipe types are already bound whole by their own categories and carry more
than the keyword — investment levels, carry loads, graph edges — so re-reading them here would
publish one native member twice under two owners.

Two of the thirteen were previously published *lossily* rather than merely narrowly, which is the
reason this table exists at all:

| Entity | Was published | What the tooltip shows |
| --- | --- | --- |
| Structure | `structureType` alone | `GetAllTypes()` — the subtypes prepended with the primary type |
| Alchemy recipe | `GetCoreType()`, which is `alchemyTypes.Last()` | every authored type; each recipe has exactly two |

Both older bindings stay: a structure's primary type and a recipe's core type are real game concepts
with their own consumers. The full lists are published beside them.

Each row names the authored member it came from and that member's own ordinal, so the table's order
is **not** display order. The two composite classes compose in opposite directions —
`StructureSO.GetAllTypes()` prepends the primary type, `EquipmentSO.GetAllEquipmentTypes()` appends
it — so flattening them during capture would bake one class's order into the other's. Assembling the
line is a derivation.

The category is whole-or-withheld: one unbindable member withholds every keyword, with the failing
members named. A partial keyword table is indistinguishable from a table whose entities genuinely
have no keywords, and telling those two apart is the only question it exists to answer. Upgrades are
the large surface that genuinely has none — 229 of them author a constant string and read no type
field — so an empty keyword list is a real answer and must stay trustworthy.

### Keyword ids resolve to words, and only through the display name

Capture publishes identities and never words, so a keyword id becomes a word through the live entity
identity catalog like any other id. That works because every one of the sixteen taxonomies derives
from `IdScriptableObject`, which is what puts its assets in `RuntimeLookup` with a stable uuid;
`KeywordVocabularyContractTests` pins exactly that against the audited build, together with each
taxonomy's `All` registry — the enumerable vocabulary, including the words no entity carries and the
global catch-all type every member is also registered against.

This is the one place the catalog's names are **not** diagnostics. Elsewhere a display name is
presentation metadata and an empty one costs nothing; a type asset's display name *is* the
player-visible keyword, so an empty one is a lost word rather than a cosmetic gap. The consequence is
narrow and specific: keyword resolution must read the display name and must not fall back to the
asset name. `ChallengeTypeSO` is why — its seven assets are effect-targetable with empty display
names, deliberately wordless, and an asset-name fallback would invent seven keywords the game never
shows.

## Type-level modifiers, and the trap in reading them

Every taxonomy that derives from `UpgradeableObject` carries modifier records of its own — 145 of
them across fourteen classes, 1,812 records once each class's assets are counted. `WorldTypeModifier.cs`
publishes them as two categories rather than as members on fourteen row structs, because the shape is
identical everywhere and a record is a record:

| Category | Key | Carries |
| --- | --- | --- |
| `type modifiers` | type asset + record member | the record class, and the active and passive modifier counts |
| `type modifier contributions` | type asset + record member + modifier | one row per modifier: its kind, amount, order, and the tooltipable source that added it |

The counts exist so a consumer can size the contribution table before walking it. The contribution
table is variable-size with no upper bound the audited build can state: both dictionaries are runtime
state written by whatever effect fired, so the population in a mid-game save is not knowable from IL
or from the serialized assets.

Capture stops at the entries. A distributor holds no value of its own — its total is `Adjust(100)`,
which is arithmetic, and arithmetic on the Unity thread is exactly what this boundary refuses. The
fold runs on the worker instead, and publishes three derived tables:

| Table | Key | Carries |
| --- | --- | --- |
| `TypeModifierTotals` | type asset + record member | `DistributedTotalPercent` (`Adjust(100)`), the same total as a multiplier, and how many entries it folded |
| `KeywordModifiers` | type asset + member kind + record member | that total again, with how many members of that kind the keyword reaches |
| `SpellTypeResonance` | loadout position | the power, cost and cooldown-speed factors the spell's live type set multiplies in |

The seed is 100 because that is what the game passes: `OrderedMultiplierRecord.GetTotalPercent()` is
`Adjust((BigDouble)100)` beautified with a percent sign, and `GetTotalMultiplier()` is `AsPercent` of
that same number. `MergingModifierRecord` has no `GetTotal…` pair of its own — it inherits the same
`ModifierRecord.Adjust` and holds no `baseValue` to seed with instead — so that seed is the only
honest one for it too. A `ValueModifierRecord` gets no derived total: it is folded where it is
published, and folding it again from these entries would be a second answer to a settled question.

`KeywordModifiers` counts members from the authored membership `entity keywords` publishes, closed
transitively over the subtype edge, and never from a type's runtime registration list — reading that
would publish one fact twice under two owners and would still miss the edge. A member named by
several rungs of a chain counts once. A type nothing wears gets no row; its total is still on
`TypeModifierTotals`. Spell types have no rows here at all, because none of their records
distributes and a spell's types are published as spell-graph relations rather than keyword edges.

`SpellTypeResonance` reproduces `Spell.GetResonantPercent`: a product over `GetNotSpellTypes()`
**concatenated with** `augmentedSpellTypes` — a concatenation and not a set union, so a type named by
both halves multiplies twice — with each type's value taken `AsPercent`, and, when the product of the
types' elemental resonances is not approximately one, every `IsElemental()` type's percent raised to
that resonance first. A slot naming a type the world did not publish gets no row: a product short one
factor is a smaller number that still reads like an answer.

Its power half is the one derived table with a native oracle behind it. The **Spell type layer** pass
compares `TypePowerPercent` against `Spell.GetSpellTypePowerPercent()` for each occupied position,
which is the number `Spell.GetPower()` multiplies in — so a faithful reproduction of the aggregate is
told from a plausible one by the game rather than by the transcription's own reading of it. The pass
takes its positions from the list the identity registry answers for `ActiveSpells`, the same list the
collector read, so a position it compares and a row the world published are the same position rather
than two lists assumed to agree. An empty position is an expected skip and a loadout of them reports
that nothing could be verified; an occupied position the deriver failed closed on is reported as
unreadable, because a slot the game will answer for and the suite will not is the finding.

> **A published type total and a published member value are not two factors.** Eleven of the fourteen
> taxonomies reach their members by *distribution*: when a modifier lands on a
> `MergingModifierRecord` or an `OrderedMultiplierRecord`, that record pushes a transformed copy into
> every member record registered with `AddRecord`, and the member's own `ValueModifierRecord` — which
> this collection already publishes, folded — carries the result. Multiplying a total derived from
> these rows into a member value the snapshot already carries counts one bonus twice.
> `SpellTypeSO` is the single exception: all twenty-two of its records are values, nothing
> distributes, and `Spell.GetPower()` multiplies the type layer in as its own factor.

`ValueModifierRecord` is how the two are told apart, which is why the record class is published on
every row and pinned per member by `TypeModifierContractTests` against the audited build.

The rule is enforced by the names rather than left to a reader's memory: a derived type total is only
ever reachable as `DistributedTotal…`, while the member value keeps the plain property name. The
record member's own name cannot carry the distinction — nine of the thirteen structure pairs name the
type record and the member record identically — so a consumer that wrote `total.Power` does not
compile.
`NoDerivationMultipliesATypeTotalIntoTheMemberValueItAlreadySitsIn` sweeps every magnitude on every
derived modifier row reflectively and asserts the product appears nowhere, so a magnitude added later
inherits the rule instead of escaping it. The naming convention is normative in the
[game boundary doctrine](game-boundary-doctrine.md#a-type-total-and-a-member-value-are-one-bonus-and-the-names-say-so).

One record holds no value *and* distributes to nothing: `ResearchTypeSO.levelRequirementAdjust` is a
plain `ModifierRecord`, and `RegisterResearch` wires only `power` and `maxLevelCap` into its members.
Its total is the single one on `TypeModifierTotals` that is not also inside a member value, and
`RecordNativeType` is what says so.

### The reads that bound a type-level bonus

Three more reads exist because "a type-wide bonus reaches that type's own members" is not true as
written:

- **`structure-type.sub-types`**, published in the same category. `StructureTypeSO.Initialize()`
  calls `RegisterSubType` per entry, and that wires the parent's thirteen records into the child's
  thirteen — so a bonus on a parent reaches the children's members too, and the covered set is the
  transitive closure over this edge rather than one membership list.
- **The effective spell type set.** `Spell.GetAllSpellTypes()` is
  `SpellRecipeSO.GetNotSpellTypes()` concatenated with `Spell.augmentedSpellTypes`, and the latter is
  seeded from the recipe's authored list and then rewritten from the equipped glyphs. So the authored
  list answers the wrong question the moment a glyph changes. The recipe half rides the spell
  authored graph as a fourth relation kind; the live half is published per slot as `SpellSlotTypes`,
  because a `Spell` is the occupant of a position rather than an entity of its own. `notSpellTypes`
  is empty on all sixty-five recipes on the audited build — a reading, not a rule, so it is bound
  like anything else.
- **The challenge draft's buckets.** `ChallengeTypeSO` is the one type asset family with no modifier
  surface at all: it derives straight from `IdScriptableObject`, and its four authored fields are a
  weight, two draft flags, and a display effect. The draft picks a type from a weighted table and
  then a challenge from that type's bucket, so those fields decide whether a challenge can be offered
  at all. They are published with the challenge decision state, which is the decision they belong to,
  as `ChallengeTypes` and `ChallengeTypeMemberships`.

Neither type-modifier category is structural: modifier dictionaries are runtime state, so both are
read every pass. That is also why the three reads above are declared `per-pass` rather than
`per-epoch` even though what they carry is authored — the cadence on a contract says how often the
suite touches the member, and these are touched by per-pass readers.

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
  collection does not write ([W35](world-collection-decisions.md)).
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

## Layout

One file per category under `src/Common/Runtime/World/Categories/`, each holding that category's row
struct and its binder. The machinery lives one directory up: `WorldCategoryMachinery.cs` (buffers,
readers, derivers), `NativeAccessorBinder.cs` (member binding), `GameWorldCollector.cs` (the pass, and
owner of the 63-reader array), `GameWorldStateDeriver.cs` (the four derived row kinds — resource,
structure, upgrade, plot node). Derivation that reads more than one published table is assembled in
`GameWorldCycleFrame.cs`, where such tables are built as locals before the snapshot is composed:
`WorldTypeModifierTotal.cs`, `WorldKeywordModifier.cs`, and `WorldSpellTypeResonance.cs` are the
modifier half of that, and they add no reader and no category — a derived table has no
`WorldCategoryReport` to publish.

Ten readers are **structural**: plot authoring, effect blocks, spell authoring, entity requirement
graphs, purchase view relations, crafting recipe types, crafting recipe authored edges, structure
costs, upgrade costs, and entity keywords describe what the game's authors wrote rather than what the player has done, so they re-read only when the frame
arrives under a lifecycle epoch this collector has not already read for. Immutable output tables are
still derived on the worker for every publication — only the repeated Unity/native traversal is
skipped. Their paired live facts remain ordinary 250-millisecond collection: prerequisite-link and
native requirement verdicts, crafting visibility/purchase/capacity/drain verdicts, resources, active
modifier inputs, and affordability. The field-by-field ownership table is in
[Game MCP frame operations](game-mcp-frame-operations.md#data-lifetime-and-owner-inventory).

The epoch is recorded only when the structural readers actually delivered. A bound reader that threw
left its buffer reset, and recording that pass as read would claim the frame holds this run's
authored rows when it holds none — with no further epoch until the next lifecycle, recovery took a
prestige. A reader that never bound is the opposite fact and does not hold the epoch open: nothing on
this build will make it deliver, and waiting for it would re-walk every other structural category
four times a second.

`WorldScribeRelations.cs` is the reader that belongs in that structural set and is not in it yet. Its
enchantment-role half is authored data and it re-reads it every 250 ms, while its target sweep
filters the whole structure registry once per role and latches every candidate's availability on the
way past. It reads its members through the compiled accessors every other reader binds, so the
overhead `NativeAccessorBinder` exists to have removed is gone; the manifest still records the sweep
as owed capture debt, and the epoch scope is owed with it.

The live entity-name catalog follows an even narrower lifecycle contract: it binds at the first
stable Playing capture after `RuntimeReady`, then reuses one UUID-sorted snapshot until lifecycle
replacement. It is attached metadata, not a fourth ServiceCycle publication and not a 250-ms reader;
its registry and fallback rules are normative in the
[game boundary doctrine](game-boundary-doctrine.md#live-entity-identity-catalog).

Most tables are one row per entity and are walked by the identity check. Which tables the walk skips is
stated in exactly one place — `NotIdentityTables` in
`tests/OrbModding.Tests/Runtime/Verification/WorldIdentityWalkTests.cs`, currently 51 names — because
every second reading of an entity another table already claims lands there. Five exclusions have reasons
worth knowing:

- **Purchase costs.** `WorldPurchaseCost.cs` is a second walk of the structure registry with a buffer
  admitting several rows per entity, and `WorldUpgradeCost.cs` the matching second walk of the upgrade
  registry appending into the same buffer — which is why the collector, not either reader, resets it.
  Read through `WorldPurchaseCostLookup`, which returns an entity's whole range, because a partial price
  is worse than none.
- **Plot actions.** A row is a *pair*, so it has no identity of its own. `WorldPlotAction.cs` walks the
  plot registry a second time and prices each pair against the plot's remaining quantity on the worker;
  read it through `WorldPlotActionLookup`, which searches the composite key.
- **Action queue slots.** `WorldActionQueue.cs` publishes two tables: a queue *is* an entity, so
  `ActionQueues` is walked like any other identity table, while `ActionQueueSlots` is keyed by queue and
  index and is exempt. Neither is reached by a registry walk — both queues resolve by uuid through the
  identity registry, which keeps the action-manager singleton out of the collector.
- **Entity requirements.** `WorldEntityRequirement.cs` reads every upgrade's, structure's, and
  research entry's per-level prerequisite container, so a row is one condition keyed by an entity its
  own category already claimed.
  Its list is `[SerializeReference]`, so accessors compile per concrete condition class on first sight,
  and a class that does not bind yields a row of kind `Unknown` rather than none — an unmodelled
  condition must be visible as a requirement nobody can evaluate rather than as an entity with no
  requirements ([W58](world-collection-decisions.md)). The native parameterized `Check(ConditionInfo)`
  verdict is not published: it is asked at request time by `WorldRequirementNativeVerdictProbe` for the
  one entity being explained, and by the requirement passes for the registry they walk. It is a
  differential oracle, never a replacement for the graph and never an admission result; the explainer
  fails loud if its graph verdict disagrees. How that overload differs from the parameterless latch is
  recorded in [requirements](../reverse-engineering/requirements.md).
- **Spell slots and costs.** `WorldSpellSlot.cs` publishes the equipped loadout and `WorldSpellCost.cs`
  what casting out of it costs, both from one reader, because a slot's price is only answerable from the
  same equipped instance the slot was read from. Neither is identity-keyed: a position may be unfilled
  and two may hold the same spell, so `SlotIndex` is the key and counts unfilled positions exactly as
  the game does, because it is the number a cast is addressed by
  ([W60](world-collection-decisions.md)).
