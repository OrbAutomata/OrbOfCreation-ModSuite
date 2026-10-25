# Game boundary doctrine

> **Lifecycle: Accepted production rule set.** Every surface that touches the game — reads,
> owned math, mutations, suite UI, and the Game MCP — follows these rules. A lane that needs
> an exception records the justification in its report, and the accepted exception becomes a
> rule here rather than a per-feature variant.

[Back to dossier](README.md) · [Goals and invariants](goals-and-invariants.md)

## The stance

The suite owns its math and its behavior. The game is the authority on **identity** (what
exists, UUIDs, types), **structure** (relationships, unlock state, serialized graphs), and
**transaction execution** (mutations and their results). Every touch of game code, data, or
assets is a declared schema-3 contract. Nothing the suite does may depend on ambient game-UI
state — which screen is open, what is loaded, what the player happens to be looking at.

The two failure modes this doctrine exists to prevent are not symmetric. A broken declared
contract fails loudly and enumerably at audit time, before anything acts. An undeclared
dependency on UI state fails per-screen, per-session, invisibly. When a choice exists, take
the risk the manifest can see.

### Perf-debug inspection exception

The Game MCP is a debugger, not an ordinary service or a production gameplay dependency. A
parameterized tooltip, inspected panel, active-screen catalog, fixed probe, or framebuffer cannot be
truthfully published as general world state. A perf-debug MCP operation may read that exact fact
directly provided the HTTP worker submits one immutable operation, Unity performs the read on its
main thread, the result is native-free and terminal, and no gameplay mutation occurs. UI state
changes such as navigation or opening an inspected tooltip are classified and audited as UI
mutations, not called read-only.

This exception does not permit speculative capture. There is no periodic MCP snapshot, cache warm,
hidden navigation, or “freshness” loop. Broadly reusable changing facts go into `GameWorldState`;
authored graphs are build-time or lifecycle-structural; genuinely parameterized UI facts are read
only for the request that names them. See
[Game MCP frame operations](game-mcp-frame-operations.md).

### Diagnostic observe-and-rethrow exception

The game's tick has no exception handling: an element that throws from
`GameElementIterator.Increment` ends that tick for every element after it, every tick, and the game
stops advancing without a word. Naming that element is the one reason the suite patches the game's
tick (`GameTickFaultObserver`, on that method and on `ViewManager.CheckViews`). Such a patch:

- observes only: an exception filter that answers false records the element and the exception, so
  the same exception unwinds with the thrower's stack — never caught, wrapped, or swallowed;
- records inside the filter, which runs during the first unwinding pass, before any of the game's
  finally blocks have run: the record calls no Unity or game API and never throws;
- requires that no game-side catch sits between the patched call site and `FixedUpdate` or
  `ViewManager.Update`, so every recorded exception is one the game does not handle; the audited
  build pin guards this, because a game update that adds a handler is an unaudited build;
- costs one static call and no allocation on the no-throw path, proved by the allocation probe;
- names, logs, and reacts on the suite's own update, never inside the tick, logging each element and
  exception type once per lifecycle and counting repeats;
- rewrites only the call sites a contract pins by count and opcode in the audited IL, and leaves a
  body that differs unpatched;
- has a kill switch read at composition (`Diagnostics.ObserveGameTickFaults`).

## Reads and owned math

- Compute what we can; collect what we cannot. Collection cost is a real budget: every native
  read is main-thread FFI work, and time spent collecting answers we can derive ourselves is
  time the suite does nothing.
- Owned math is verifier-covered. Divergence between suite math and the game's own answers
  must be detectable, loud, and named — never assumed away.
- Never re-derive what only the game can know. `WorldAlchemyRecipe` capturing the natively
  resolved `AlchemyRecipeSO.GetMaxUsageSlots()` instead of interpreting the raw modifier and
  its `-1` sentinel is the paid-for lesson: re-implementing *facts* (as opposed to *formulas*)
  silently diverges when the game changes.
- A published value that requires a screen visit to be fresh is a **collection defect**.
  Either the collector performs the declared revalidation the UI would perform, or the value
  is published as stale-capable and no action may be authorized by it. "Works when the screen
  is open" is never an accepted state.

### The modifier fold runs on the Unity thread, and that is the accepted trade

Owned math belongs off the Unity thread. `NativeModifierRecordAccess.Fold` is the one named
exception, and it is settled rather than open: it runs inside the compiled accessor, so its
arithmetic happens on the main thread during capture, roughly five thousand records per pass.

Two facts make that the cheaper side of the trade, and both are load-bearing together:

- **The dirty flag is read first.** A clean record costs one boolean read and one field read and
  skips the arithmetic entirely, and most records in a live save are clean. The fold is paid for
  only where the game itself would also have recomputed.
- **Moving it off-thread means allocating.** Each dirty record's modifier set is variable-length and
  is copied into one reused scratch array. Carrying it across the thread boundary means per-record
  storage on the frame — an allocation per dirty record, which is exactly the cost the scratch
  buffer exists to avoid. Trading bounded main-thread arithmetic for unbounded per-pass allocation
  is a worse frame, not a better one.

This is recorded so it is not re-litigated. A future reviewer proposing to move it is proposing the
allocation, and has to answer for that instead of for the arithmetic.

The type-level fold is **not** that exception and must not be confused with it. A type's own record
holds no value, has no dirty flag and no memo, and the game recomputes its total every time it is
asked for — so there is nothing to short-circuit and nothing to copy across a boundary that capture
has not already published. `WorldTypeModifierTotalDeriver` folds the published contribution rows on
the worker, which is where owned math belongs by default. Capture takes entries; derivation takes
totals.

### A type total and a member value are one bonus, and the names say so

Eleven of the fourteen modifier-bearing taxonomies reach their members by **distribution**: a
modifier landing on a `MergingModifierRecord` or an `OrderedMultiplierRecord` is pushed, transformed,
into every member record registered with `AddRecord`, so the member's own `ValueModifierRecord` —
which this suite already collects and folds — carries the result. Multiplying a derived type total
into a member value the snapshot already publishes counts one bonus twice, and every number along the
way stays plausible.

Publishing the total anyway is the answer world collection gives everywhere else — publish the facts,
not a goal-shaped subset of them. Withholding the totals whose members happen to be well covered
would drop `structurePower`, which is the single most useful number
for "is this attribute worth investing in", precisely because it is well covered. The guard is a
naming convention plus a test rather than a withheld fact:

- **A distributor's total is only ever reachable as `DistributedTotal…`.** The member value keeps the
  plain property name. `Property` cannot carry the distinction on its own — nine of the thirteen
  structure pairs name the type record and the member record identically — so the magnitude's own
  name has to, and a consumer that wrote `total.Power` does not compile.
- **`RecordNativeType` says which kind of record it was.** A `ValueModifierRecord` gets no derived
  total at all, because it is folded where it is published. `ResearchTypeSO.levelRequirementAdjust`
  is the one plain `ModifierRecord`: it holds no value *and* distributes to nothing, so its total is
  the only one that is not also inside a member value.
- **The rule is a test, not a comment.** `NoDerivationMultipliesATypeTotalIntoTheMemberValueItAlreadySitsIn`
  sweeps every magnitude on every derived modifier row reflectively, so a magnitude added later
  inherits the rule instead of escaping it.

`SpellTypeSO` is the single exception in the other direction: all twenty-two of its records are
values, nothing distributes, and `Spell.GetPower()` multiplies the type layer in as its own factor.
That product is derived as `SpellTypeResonance`, and it is the one place combining a type's number
into a member's is correct.

### Live entity identity catalog

`RuntimeIdentityRegistryBinding` is the one Common-owned binding for
`IdScriptableObject.RuntimeLookup` and `IdScriptableObject.GetGuid()`. It compiles them when it
binds and publishes them as one immutable snapshot, so a thread that arrives mid-bind never reads
half a binding. Its `StableUuidMethod` lists the identity shapes a reader accepts, and
`StableUuid` returns the compiled read for a type, the one bound `GetGuid()` delegate for every
`IdScriptableObject`. Typed registry resolution, the identity catalog, reference edges and verifier
oracles read identity through it; no feature, MCP surface, diagnostic, or log owns another registry
or UUID-to-name reader.

The first shared world capture after `RuntimeReady` in a Playing generation enumerates that registry
once on Unity's main thread. The pass records lifecycle generation and registry count before and
after enumeration, requires every value's `GetGuid()` to equal its dictionary key, and copies only
UUID, exact runtime type, `TooltipableObject.GetName()` when applicable, and the Unity asset name.
An expected native failure reading one entity's name costs that name and keeps the other
identity-correct rows; any other throw from a name read faults the world pass. Any lifecycle or
count instability discards the entire candidate and retries on a later ordinary capture; an identity
contradiction or total binding failure publishes an empty unavailable catalog and reports one error
for that lifecycle. An empty name never blocks world publication, a feature, or a mutation.

The successful table is UUID-sorted and immutable. One snapshot reference is attached to every
`GameWorldState` in that lifecycle and placed in a Common latest-wins holder for non-world
presentation consumers; it is not rebuilt per publication and its rows are not copied into entity
categories. A lifecycle transition first replaces both views with an unbound empty snapshot, so no
Unity asset or name survives save/load, reset, NG+, or scene-generation replacement.

`EntityIdentityFormatter` is the sole rendering facade and never reflects or touches Unity. Its
total fallback ladder is live display name, live asset name, generated `KnownEntities` diagnostic
name before the live bind only, then bare canonical UUID. Rendered labels always retain the UUID.
After bind, a missing label reports one UUID-only warning per `(generation, UUID)` and does not fall
back to authored bootstrap metadata. MCP entity references, refusal receipts, feature diagnostics,
logs, and traces all use this same substrate; names remain diagnostics and never identity authority.

One class of entity is the exception, and it is an exception about *meaning*, not about authority. A
`*TypeSO` asset's display name is the keyword word the game prints in a tooltip's word line, so for
the sixteen keyword taxonomies the display name is player-facing content that a consumer is entitled
to read as the word itself. Identity authority is unchanged — the uuid is still the key and the name
still proves nothing — but two things follow that do not hold for ordinary entities: an empty name is
a lost keyword rather than a cosmetic gap, and the fallback ladder must not be walked, because an
asset name is a diagnostic and would be shown as a word the game never prints. See
[world collection](world-collection.md#keyword-ids-resolve-to-words-and-only-through-the-display-name).

Portable and installed-contract gates prove this shape and its exact native members. Stability of
the live registry at `RuntimeReady` remains a supervised promotion check; this lane does not claim
live UAT and does not launch the game.

## Boundary validators and freshness classes

The main thread re-checks live native facts before mutating. Some of the game's own validator
methods are backed by caches that only recompute when a screen views them — the game can be
the stale party while the suite's snapshot is honest. Every native validator used in an
action protocol is therefore classified in its contract dossier:

1. **Pure.** Computes from live state on every call. Trust it.
2. **UI-cached, revalidatable.** Backed by a visibility-driven cache with a known recompute
   path. The action protocol invokes that exact recompute as its own declared, scoped
   contract call immediately before asking — never ambient or blanket cache-touching.
   Warming an unrelated cache has changed game math before; every revalidation is named,
   scoped to its action, and audited.
3. **Unrefreshable or side-effectful.** No safe recompute path exists. Do not pre-validate.
   Attempt the mutation, verify postconditions exactly, and treat a rejection as the answer.
   The one-attempt-per-world rule makes this self-correcting: a stale-validator rejection
   costs one publication, and the next publication is a fresh collection.

Every rejection names the check that refused, in the named-cache vocabulary
(`UpgradeSO.cachedCostLevel` is the naming precedent). "The game said no" is not a reason.

Action dispatch deliberately continues under the cycle-pinned configuration even when a newer
committed generation appears while its batch drains. Dispatch is bounded per frame, so a batch of
`N` actions under bound `B` spans `ceil(N / B)` frames; world staleness across those frames is
expected and is handled by live boundary revalidation plus feature-local spend reconciliation.
Configuration has one
player-driven writer operating at human rate; adding a second configuration read or invalidation
path would buy only an imperceptible narrowing window. A future design with long-running batches
changes this invariant and must revisit the decision. Master-disable also releases feature
ownership leases on committed-configuration refresh as a fast backstop, but that is not the
policy-correctness mechanism. This bounded policy staleness does not relax live native
revalidation: game facts can change every frame without bound.

**Staleness probes.** Perf-debug capture QA may, for class-2 contracts, collect the cached
value, force the declared revalidation, and compare. Divergence is a build-behavior finding.
This is the detector for game updates that change caching behavior; a new game baseline
re-runs the probes as part of its audit.

## GameActions: the only way to mutate

Each game capability the suite can perform is defined **once** as a GameAction, and every
consumer — feature services, the Game MCP, tests — uses that one definition. A GameAction
declares:

- its **complete binding set**, resolved and validated at lifecycle scope before any use, so
  a resolution failure is impossible mid-transaction. `NativeBindingSet` is the one strict
  binder: each member is looked up by its declared contract id with exact types, and any
  miss makes the whole set unavailable with one reason;
- its **preflights**, each with its freshness class;
- the **mutation** itself;
- its **postcondition** — the single simplest observable check that the requested transition
  landed on the requested target (see below);
- its **failure story** — what blocks, what the health surface says when the postcondition
  fails, and, for automation consumers only, what quarantines.

`NativeGameAction` in Common is the shell a GameAction derives from; the action supplies only its
binding factory, screen, admission, native call and sentinel. The shell answers in a fixed order,
for presses and for the reads another GameAction makes through it alike: the wrong thread, then a
missing binding set, then the run. An epoch the game cannot answer is a fault, never a replaced
run. An expected throw during admission is a refusal, because nothing was sent. The native call
goes through `NativeMutationVerifier`: a visible outcome commits, a missing one is a verification
fault, and an expected throw or an unreadable after-state is a post-commit fault that names the
game's error by its log reference. An outcome keeps its authored sentence and that reference apart:
every surface someone reads gets the sentence followed by the reference, and anything that asks
whether a failure repeated — a warning written once, a health revision — compares the code and the
sentence, and the target too where one warning serves many targets, because each attempt's cause is
logged under a fresh reference. An action of several presses — pay then act, open then press, a
swap's give-up then take — makes them all inside that one call, under one sentinel for the whole
effect. A press counts as an attempt even when the suite presses it back: a call that stops after
pressing the game refuses only when it pressed its change back and the game rereads undone, and
that refusal carries the one attempt; any other stop after a press is an attempted fault. A call
that throws partway is put back by the shell when it supplies a put-back: the call's exception
stays the cause its log reference names, a throw from the put-back is logged in the same entry, and
the answer says so when the put-back does not read back. Zero attempts means no press was made. A
press is a press of the player's controls: pinning the game's multi-buy multiplier around one is
scaffolding, guarded by the scope's verified restore, and counts no attempt. A restore that does
not read back stays outstanding: every entry is refused, the first entry after each doubling wait
puts the player's remembered value back, and only a verified read-back ends it. The scope never
takes its own leftover for the player's value, and a setting that reads otherwise than the suite
left it stands. A GameAction not yet on the shell is migration debt, except the Modal pair
(`ModalOpenGameAction`, `ModalDismissGameAction`): perf-debug UI chrome that stays outside the shell
because putting a panel up or taking it down is no run-scoped transaction, the suite owns no permit
family for it, and it has no lifecycle epoch to admit. The pair still touches the game only on the
main thread, verifies its one outcome through `NativeMutationVerifier`, reads the after-state when
the game throws after the press, and answers every unverified press as a fault. Its MCP command
lives in `GameMcpGadgetCommands` and its lifecycle in `GameMcpGadgets`.

Composition registers each GameAction on the shell once, into the one `LifecycleGameActions` the
runtime owns, whether the composition root or a feature builds it. That registration is its whole
lifecycle wiring: the runtime rebinds every registered action in creation order after the features
observe an accepted lifecycle, disposes them in reverse, and reaches each by its type. Its mutation
permit and its ownership refusal come from the one family its MCP kind names in the
`AutomataActionFamilyOwnership` table, the same entry that says what the MCP operation claims;
naming the family once yields both, and the refusal sentence is derived from the family's members.

Mutation entry points, in strict order of preference:

1. **A data-layer composite** the game already owns: `UpgradeSO.Purchase`,
   `StructureSO.Purchase`, `SpellManager.FireSpellIndex`,
   `AlchemyInstanceListVariable.AddAlchemyInstances`/`RemoveAlchemyInstances`,
   `PlotNodeActionInstanceListVariable.AddInstance`.
2. **A disciplined re-drive** when the composite exists only inside a UI handler body.
   `SpellLevelGameAction` is the reference implementation: every member prebound and
   validated before the first mutation is possible, the outcome verified by the simplest
   observable check, and irreversible steps (payments) preflighted hardest and taken last.
3. Invoking UI handler methods: **never.**

One evaluation round per world reading; any attempted round raises the world-gate floor.
This is engine-enforced (`ServiceCycleSlot`), not per-feature policy — a GameAction never
carries its own cooldown, retry timer, or candidate memory.

## Suite UI

- The suite owns every control and every pixel it presents. Live native UI objects are never
  cloned, repurposed, or listener-stripped into suite controls.
- Static assets — sprites, fonts, sliced frames — are borrowed through declared capture, like
  any other read. If capture fails, the control does not appear and health names the exact
  reason. There is no fallback rendering.
- Suite surfaces **speak the game's idiom**, not just its palette. If the native siblings of
  a surface are tabs, the suite's addition is a tab; if they are toggle buttons, it is a
  toggle. Interaction grammar is part of looking native.
- The Game MCP navigates suite surfaces exactly as it navigates native ones. A suite surface
  that requires MCP special-casing is wrongly built; fix the surface, not the MCP.

## Enforcement

- Every implementation lane cites this document; review findings reference the specific rule
  violated.
- A new capability is a new GameAction, or an explicitly justified exception recorded in the
  lane report — which then becomes doctrine, so the next feature reuses it instead of
  reinventing it.
- Bespoke per-feature variants of already-solved problems (binding, verification, freshness
  handling, asset capture) are findings, not style choices.
- The Game MCP owns transport only. All stateful tools cross its single post-pump frame-operation
  inbox; an empty inbox performs no projection, health read, native read, or authority refresh.

## Postconditions: one simplest sentinel

A GameAction verifies its outcome with the single simplest observable check that the requested
transition happened to the requested target: stable UUID plus expected native type, plus one
outcome fact — a loadout add checks for the new spell instance, a purchase checks the new level.
Nothing else is computed. Payment deltas, resource balances, counters, timers, and flags are not
gathered as receipt evidence; code that computes values nothing acts on is deleted, not tolerated.
Most actions succeed, the boundary is designed for that, and a layer of paranoia bookkeeping is a
finding, not a safety margin. In particular, an accounting fact can never veto, downgrade, or
quarantine an outcome the game observably performed — a sub-ULP native `BigDouble` subtraction
cannot disprove a transition that happened.

After a native exception, capture the simplest available after-state before classifying: if the
requested identity and outcome are observable, the action committed and the exception is logged
with a reference that `suite_health` names as that feature's latest; if the outcome is absent or
the target ambiguous after a mutation began, fail closed. Admission checks still prevent
unaffordable or otherwise ineligible attempts — this rule defines what a postcondition is, not
preflight authority.

`NativeFailureClassifier.IsExpected` is the one list of exceptions that count as a failed native
read or call, for every GameAction, adapter and reader. Any other exception is a suite defect and
escapes to the boundary that reports it as a fault, unless a native call ran and its change landed.
After a native call, `NativeMutationVerifier` classifies every throw first by reading the
after-state. A landed change followed by an unexpected throw commits, and the throw is logged with a
reference that `suite_health` names as the feature's latest. A change that did not land faults
the action, and only then does an unexpected exception escape, so the defect stays loud and the
retry gate backs the target off before the fault tracker retries the batch. Auto Scribe and
Spell Leveling reread the after-state the same way, and a visible craft or a risen level commits with
the throw logged. Their one exception is a paid press whose outcome is not visible: the unexpected
exception is written whole to the suite log under the reference the post-payment fault names, so the
recipe or spell backs off for a minute instead of the service retrying a paid press within seconds.

## MCP responses: worked, or why not

A success says the action worked and, when the settlement window delivers it, what changed — the
settled post-state a caller would otherwise need a follow-up read for. Nothing else: no request
echoes, no zero counters, no empty collections, no receipts, no expected/observed generation
pairs, no verbosity options. Absence means not applicable, and each tool has one response shape.

A refusal or fault answers with the exact reason a caller can act on, plus only the facts that
explain that reason. Explaining a failure is not dumping a dossier: an audit stanza that ships
with every failure regardless of relevance is ceremony, and ceremony is deleted.

## The wire speaks the screen's language

What the MCP returns matches what the player sees: a screenshot and the MCP answer for the same
screen agree on names and numbers. Reads answer `available`/`unavailable`; mutations answer
`committed`/`refused`/`faulted`. Every entity reference carries the stable UUID plus the
player-facing name, category or type when relevant, and the internal name only when it differs.
Magnitudes render the way the player recognizes them from the screen; native numeric sentinels
become field absence, never plausible values. Resource and cost rows use one canonical spendable
`amount`; collector and factor internals stay off the player surface.

The format is the simplest one that carries the signal. Compact text is the default whenever an
agent reads it faster than structured data; JSON is for responses whose handles feed the next
call. One vocabulary, one spelling, one numeric rendering per concept, whatever the media.

Every read operation pins one immutable publication for its whole answer, but the wire does not
expose its internal generation counter. Action schemas and results likewise carry no generation:
the GameAction revalidates live, and settlement accepts only a world captured after the action.

## The UI defines the verb surface

The native player UI defines the MCP action vocabulary. A compiled manager or UI handler is not by
itself permission to expose a verb: the player must actually be offered that choice. Discovery is
therefore component composition followed by native resolution and confirmation; transient discovery
offers are modes of that same discovery namespace. A loaded spell has no in-place glyph editor, so
glyph layout is chosen before loadout add and baked into the new spell. Casting Output and Reserve
are global dials, not per-spell state. Whenever the MCP cannot bind the exact visible sibling, it
returns `contract_unavailable` rather than substituting a nearby native path.

Spell-loadout add is the concrete complete-candidate case. On the Unity main thread the shared
GameAction builds a temporary spell at the recipe's selected level, bakes the requested glyph
multiset into it, and follows the visible recipe button's gate order: non-level duration/toggle
requirements; authored usage requirements (with the UI's selected-augment override); computed
usage-budget affordability; an empty loadout slot; and unique-spell compatibility. Those gates are
checked once, before anything is pressed. After acquiring its mutation permit, the boundary stages
the requested augments over the live selection, verifies the staging by reading it back, and
invokes native creation. The player's staged Spellcraft selection is put back on every outcome,
success included, and it is part of the postcondition: the load commits only when a new spell with
the requested recipe identity and exact baked glyph multiset is observable and that selection reads
back as it was; when it does not, the answer says the selection changed. Duplicate request rows
count cumulatively against each glyph's native usable maximum. Spell discovery is not part of this
verb: it is the Magic > Spellbook > Unlock tree's roll.

Discovery is one boundary because the game discovers through one mechanism: a Discovery Tree's
roll. `DiscoveryTreeSO.DiscoverItem` is the only `IDiscoverable.Discover()` caller on a screen the
player reaches; the other, `UIDiscoverablePage.HandleClick`, belongs to a page no reachable screen
draws. So the suite has no per-row discover action, and a row says which tree's roll gives it rather
than offering a press.

Every MCP gameplay action still uses its capability's canonical GameAction and live Unity-main-thread
revalidation. A failed player-driven attempt returns its exact failure and leaves no MCP-owned
lifecycle quarantine; the next request revalidates live again. This does not relax automation safety
policy, and MCP capability registration does not depend on whether the owning automation is enabled.

All committed gameplay actions pass through one post-state settlement path. It waits up to one
second for a shared world captured after the action completed, then delegates to the
command-to-world delta projector the kind's command descriptor registers. The successful response
returns only what changed. If no such
publication arrives, the response stays `committed` and emits one exceptional
`postStateUnavailable` fact with `reasonCode=post_state_timeout`; it never labels an older world as
committed state and never adds a ceremonial lag explanation. A `game_cast` fire is the exception:
its boundary sentinel already proves the cast started, so its timeout answers with the press's own
facts instead. Domain-specific readiness may strengthen the shared predicate only when the
requested outcome itself requires it, as the casting dials, cast fires and discovery events do.
