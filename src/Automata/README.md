# Automation

This folder is the automation feature area of the suite: Auto Buy, Auto Cast, Auto Concept, Spell
Leveling, Auto Harvest, Auto Items, and Auto Scribe. It is not a separate plugin and carries no
version of its own; everything here compiles into `OrbModSuite.dll` and loads under the suite's
single plugin GUID.

Auto Buy, Auto Harvest, Auto Items, Auto Scribe, Spell Leveling, Auto Cast, and Auto Concept are
registered ServiceCycle services and share the frame pump with world collection. Auto Buy receives
a fixed 16-action turn per Unity frame; the other feature services retain a bounded one-action
turn. Fruit and treasure capabilities cannot mask an eligible sibling. A feature-neutral host owns
the ServiceCycle registry, frame pump, lifecycle, emergency control, timing publication, and
pump-shutdown lease. That host also owns the observation products — the always-held recent-event ring,
the normally-on compact decision journal, and the profiling build's correlated full trace and
performance profile — so no feature owns them and gameplay is independent of all four. Every native operation passes
through fail-closed family adapters, normalized admission facts, lifecycle isolation, and
capture-execute-capture postconditions.

Automation claims Structure, Upgrade plus native multi-buy override, Spell Cast, Concept Assignment,
Spell Level Purchase, Harvest Action, Consumable Use, and Scribe queue submission independently.
The exact AutobuyOrb GUID blocks only Structure and Upgrade automation. Claims are released on
configuration/lifecycle teardown, prepared work is cancelled on loss, and ownership is rechecked
after live native validation immediately before mutation. Unknown unregistered automation is not
disabled and cannot be proven absent.

## Build

The suite builds as one project. Set `OOC_GAME_DIR` to the Orb of Creation installation root:

```powershell
$env:OOC_GAME_DIR='C:\Program Files (x86)\Steam\steamapps\common\Orb of Creation'
dotnet build .\src\OrbModSuite.csproj -c Release
```

Do not commit the referenced BepInEx, Unity, Harmony, or game DLLs.

## Release defaults

- Auto Buy starts `Active` with separate `Excess100` thresholds for structures and upgrades; progression-aware spell leveling is enabled within Auto Buy and can be disabled separately.
- Auto Cast starts `Disabled` and can be toggled with `F8` or its registered quick control.
- Gameplay controls close to exactly two suite-owned buttons below the native top-left gear and character buttons: immediate STOP/resume and a disclosure. The disclosure opens the shared registry's Auto Buy, Auto Cast, Auto Concept, Auto Harvest, Mentor, Auto Items, and Auto Scribe controls in one transient row to the right; its closed state shows a structural marker plus red color for contained faults or blocks.
- Auto Concept starts `Disabled`; `Active` fills compatible acquired Active Concept slots breadth-first, then batches safe quantity depth up to native mastery limits.
- Auto Harvest starts `Disabled`; its fruit-tree and treasure-tree selectors default on behind that master switch, and its native harvest-speed quick icon toggles the mode.
- Auto Items starts `Disabled`; `UseScrolls` and `UseRelics` default on behind it, exact temporary-item approval remains in `TemporaryItemAllowlist`, and one feature-wide quick control toggles the master mode.
- Auto Scribe starts `Disabled`; an empty `Roles` value selects every audited producible semantic role, and one feature-wide quick control toggles its mode.
- Auto Buy, Auto Cast, Auto Concept, Auto Harvest, Auto Items, Auto Scribe, and Mentor expose only `Disabled` and `Active`.
- One native queue slot is reserved for manual actions.
- Each ranked Structure prefers one live Bulk Development-sized group, reduced to the largest exactly priced positive count the remaining batch ledger can fund; each Upgrade receives one level. Every submitted level is independently revalidated.
- Queue admission uses the shared fail-closed `QueueCapacitySnapshot`: authoritative native total capacity and remaining room determine occupancy, then the Auto Buy usage limit and manual reservation are applied once to derive usable room.
- Candidates rank by cost ratio and stable UUID; there is no UUID-list filter or structure-effect priority tier.
- Absolute and relative reserves default to zero; affordability modes provide the default spending margin.
- Startup, warning, and error records remain enabled; explicit Runtime actions own deeper trace and journal evidence.
- Routine per-action successes and ordinary no-ops do not enter the BepInEx log; the compact action
  journal and Runtime outcome projection own them. Lifecycle, startup, shutdown, refusal, warning,
  and error records remain. Suite automation logging emits the first retained state immediately. Further
  byte-identical occurrences collapse independently per severity: a changed message first emits the
  held count and span, then the new state, while an unchanged state emits a count-and-span heartbeat
  on the first occurrence at or beyond 60 seconds. Summaries repeat the exact original content for
  searching. This is fixed suite behavior rather than a configuration option.
- `EmergencyDisable` immediately stops new automated purchases, casts, concept mutations, spell levels, harvest submissions, and consumable uses.
- An unknown complete game assembly pair starts with emergency stop engaged and only the Mods control plane available. Explicitly clearing STOP through the quick button or General's immediate command accepts the exact observed pair and permits runtime composition in the same action. `Compatibility.AllowUnverifiedGameBuild` remains a separate Advanced acknowledgement path that leaves STOP engaged. Changing either assembly returns the suite to quarantine. Removing acknowledgement during a running session re-engages STOP immediately, and a restart is required to unload patches already installed for that session.
- Each Mods feature header and its matching gameplay drawer button issue the same immediate mode command through the committed store. Mode is not repeated in the staged settings list, and General's emergency command is likewise immediate rather than staged. Toggle shortcuts, quick-control visibility, emergency control, and diagnostics remain editable while inactive feature tuning stays locked.

At runtime, those persisted settings are mapped once per change into one composed immutable
configuration record with General, Auto Buy, Auto Cast, Auto Concept, Auto Harvest, Auto Items, Auto
Scribe, Mentor, Safety, and Reserves sections. Engines and controls consume that record rather than
BepInEx entries or feature-specific configuration mirrors. A ServiceCycle cycle pins the complete
record it began with; later changes apply to a later cycle.

The former runtime-probe, per-session purchase-limit, DryRun, expert-override, Auto Research, and Auto Harvest runtime-selector settings are not part of the release configuration or the configuration UI. Automation has no configuration schema of its own: it binds into the suite's single configuration file, which the shared pre-bind transaction marks at `ConfigurationSchemaVersion` 6. Migrations remove retired controls, preserve reviewed current values, and move the former default Auto Concept training period from 300 to 30 seconds. Malformed, negative, or future schema data fails closed without starting the suite.

## Auto Harvest

Auto Harvest is independent from Auto Buy. When `AutoHarvest.Mode=Active`, it considers only the exact audited fruit-tree and treasure-tree collect pairs selected by `CollectFruitTrees` and `CollectTreasureTrees`. It alternates eligible pairs after each successful submission and never queues more than one supported collect action at a time.

Each pair-set capture admits both pair circuits once, resolves the shared active-list/scaling contract once,
then resolves the fruit and treasure bindings independently and caches only their immutable serialized safety
graphs. Visibility, readiness, duplicates, slot room, identity currency, and mutation postconditions remain
live. The published prerequisite latch is evidence rather than a refusal: true means the native validator has
latched success, while false means the exact current action needs one validation at the action boundary. A
missing or unsafe pair therefore cannot starve a healthy sibling; transient
live-state gaps use bounded retry, static pair-contract failures remain process-bound, and partial availability
is reported as degraded.

Every evaluation resolves the exact plot, action, active-list, scaling-weight, and reward-pool identities through the lifecycle-stamped typed registry. It then verifies native plot visibility, exact action membership, empty prerequisites, positive remaining quantity, native one-instance readiness, the reusable `Idle` to `Resting` phase contract, empty resource drain and persistent effects, and the exact `EarnTreasure` completion graph. Unknown or changed evidence rejects the action.

Pair fact capture validates the plot's available-action list type closure and exact action membership in one
native list pass. Shared active-action traversal remains a separate snapshot because it supplies duplicate,
slot, and mutation-transition evidence for both pairs.

The pair-set resolver performs the lifecycle-generation coherence check once. The following fact read verifies
the plot and action stable UUIDs directly; it does not reread the same registry generation. Scheduler admission
may inspect configuration, quarantine, and family ownership to avoid reserving unnecessary work, while the
action callback checks them again to produce the authoritative terminal result.

After configuration, UUID/type, and lifecycle checks, submission calls the exact current action's
parameterless `Prerequisites.Container.Check()` once through its lifecycle-bound compiled contract. A fresh
false result is the penalty-free refusal “native prerequisites currently unmet”; unreadable evidence refuses
under its own code. Both paths perform no quantity mutation. A true result continues the unchanged queue,
capacity, cost, and mutation path. Submission always requests quantity one through
`ActivePlotNodeActions.AddInstance`. Auto Harvest requires both the action list's native `HasEmptySpot()`
predicate and at least one enumerated empty action entry, and may consume the final free entry. The visible
plot-space meter (for example `30/33`) is unrelated, and this action list is also separate from Auto Buy's
global action queue. Auto Harvest uses the admitted current list to submit quantity one, then observes only
that exactly one matching native action is engaged. If an attempted mutation
cannot be verified, that tree pair remains blocked until a scene, save-load, reset, or NG+ lifecycle transition.

Auto Harvest evaluates after each world or configuration publication. Disabled mode and `EmergencyDisable` perform no Auto Harvest scans or new submissions. Auto Harvest does not plant, replant, replace, enrich, force growth, destroy plots, modify saves, or coexist with another plot-action automation mod. Interactive behavior is covered by the [runtime validation guide](../../docs/testing/runtime-validation.md).

### ServiceCycle runtime

The Common ServiceCycle engine is the only Auto Harvest driver. The suite registers a feature-owned activation service during plugin startup, but does not construct the lifecycle-bound runtime until the first playable `Main` frame. Generation zero remains invalid rather than being converted into a synthetic native lifecycle. Construction is attempted once; an ordinary failure disables Auto Harvest alone without selecting an alternate path or affecting sibling services. Fatal process failures are never relabeled as an isolated feature fault.

The neutral host seals an explicitly populated typed registry and admits each registered service at most once per Unity frame. World collection publishes at its hardcoded 250-millisecond cadence; every ordinary automation service evaluates after each world publication and configuration publication. Training-period deadlines, Auto Cast's manual pause, and fault backoffs remain explicit semantic waits. A mixed-type composition test registers unrelated frame/config/state/action graphs in the same host; no host type references Auto Harvest or Auto Buy. Production explicitly registers world collection, Auto Harvest, and Auto Buy rather than creating another pump. Main-thread capture copies only native-free facts; sleeping workers evaluate feature policy; and every advisory action returns to the Unity thread for fresh native validation.

Auto Buy audits each exact candidate type and native convenience signature once, then binds typed read delegates for live availability, admission, levels, and cost. Stable UUID and exact type name are cached only by native object reference within the current lifecycle and are discarded on lifecycle replacement. Resource quantities, costs, queue room, and every other mutable gameplay fact remain fresh per capture.

The opt-in profiling build records the action path in separate stages: current-fact revalidation, the
admission snapshot, the native `AddInstance` invocation, and the single engaged-pair observation.
The admission snapshot is captured once and supplies current policy and queue admission; the outcome read
traverses only far enough to identify one matching engaged action. Ordinary assemblies compile out the
profiler types and calls.

Saved configuration publishes only after a successful Apply and affects the next cycle. Lifecycle replacement clears stale native bindings, emergency disable rejects unattempted work immediately, and action-family ownership is checked both in diagnostics and immediately before mutation. Capture preserves native failure evidence: an exception thrown inside a correctly bound native call is retryable, while reflected shape, access, owner, or return-contract drift latches until restart without invoking the broken path again. A failed shared active-action snapshot is feature-scoped; a failed pair-fact read remains pair-scoped. Process-lifetime contract failures remain closed until restart rather than being converted into ordinary lifecycle retries.

The Runtime page projects pair health from immutable ServiceCycle state without inventing a legacy cycle identity. A worker response requests one full service projection; a zero-wait contended read remains pending until a later frame succeeds. Emergency-stop and action-family conditions remain immediate.

Profiler-enabled debug builds start a full semantic trace and the ServiceCycle performance profile
when the shared runtime is created. They write correlated sessions under that launch's own
`BepInEx/config/OrbOfCreation-ModSuite/trace/run-<timestamp>/` folder, and each launch prunes all but
the newest eight run folders. `./script/trace --full <session-directory> [report.md]` strictly validates
and reports a trace. Closing the game stops both products with the runtime-shutdown reason and flushes
their accepted prefixes. Release composition has no record-forward full-trace control.

The compact decision journal starts once with the lifecycle-bound ServiceCycle runtime under the stable
`BepInEx/config/OrbOfCreation-ModSuite/trace/journal/` directory, which is deliberately not per-launch so
one 64 MiB envelope governs the retained store. Schema 3 writes one fixed 80-byte sentinel per attempted
action with candidate UUID, exact native type, list/view route, and one outcome; zero-action decisions
coalesce. It owns ten reusable blocks, checkpoints once per minute, and derives a 6,476-segment retained
limit that leaves room for one maximum atomic-commit temporary. An older schema is discarded with a loud
count and recording starts fresh; there is no migration or dual reader. Journal initialization or writer
failure detaches only observation; it does not stop automation, start a replacement writer, or switch
formats. Shutdown seals the accepted prefix and returns without waiting for disk I/O on Unity's main thread.

Portable tests measure the warmed journal control tick, pump, and always-on diagnostics across 64 successful
cycles and keep every owner and worker sample within its reviewed 64-byte ceiling. Installed-game frame cost
remains an interactive profiling gate.

## Auto Buy

Auto Buy asks the game nothing while it decides. Its candidates *are* the shared world snapshot's structures and upgrades, and identity, availability, current and queued levels, prices, resource quantities and the economic priority a candidate's authored effects earn it all arrive as published rows. Each cycle projects that snapshot into a flat frame on the worker — no candidate index, no incremental reconcile, no dirty tracking — ranks the eligible ones by priority, then by cost ratio, then by uuid, and plans one purchase per eligible candidate in that order. The game is touched only where the purchase is made: `CanPurchase()` and the queue's remaining room are read there, and the boundary re-validates every level immediately before it mutates. Because the native Structure `CanPurchase()` omits affordability, that path also asks its exact live `GetPurchaseCost().HasEnough()` term before `Purchase(true)`; an unread term fails closed.

Owning-view reachability follows the game's authored list topology. Every view that carries the same
list is part of that list route and must be available; this preserves parent-screen gates when a child
view has already unlocked. Distinct lists are alternate routes, so a proven available global list can
still admit a candidate carried separately by a locked feature screen. The worker and live purchase
boundary apply the same rule, and an incomplete or unreadable route is never admission evidence.

`AutoLevelSpells=true` runs while Auto Buy is active and is configured in the Auto Buy section. Its capability follows native progression automatically: `Locked` while no discovered spell passes its own leveling prerequisites, `Single` after that contract unlocks, and `All` after the exact `UnlockLevelAllSpells` Upgrade has completed. Single mode pays the spell's live native cost and confirms one native `PurchaseLevel()` per mutation. All mode calls the game's native `SpellManager.TryLevelAllSpells()` action. Queued upgrades do not count. Readiness and the native cost's current affordability are published with each spell, so an already-known wait plans no action; the action boundary repeats both checks immediately before mutation. Any ambiguous failure after a cost attempt blocks further spell leveling for that lifecycle.

A candidate's price is the published `WorldPurchaseCost` row for its next level, computed by the suite's own port of the game's cost chain and verified against the game entity by entity in a live session. Reserves and affordability are applied to that price against the published resource quantities, so what a purchase would leave behind is decided on the worker rather than discovered at the boundary. A candidate nobody could price is refused rather than treated as free. There is no per-candidate retry or backoff: a live affordability refusal skips that candidate and closes the world-freshness gate until another collection lands, while a structural native rejection terminates the batch. No decision is taken twice against facts that have moved. Save loads, gameplay-manager restarts, scene changes and NG+ start a new lifecycle; unknown cost, resource, lifecycle or identity state fails closed.

A value that could not be read is not evidence. A candidate whose every cost row prices at zero has not been shown to be affordable, only to be unpriceable, so it is excluded rather than bought — the failure direction that once planned all 180 structures at once after a cold load. One free row on an otherwise priced candidate is different and is simply skipped, because relative to the rows that did price it really is free. A Structure action requests the largest exactly priced positive count at or below the game's live Bulk Development count that the remaining batch ledger can fund; an Upgrade requests one level. The action boundary clamps every request to queue room above the configured reserve, since the game queues one entry per level.

Every active native mutation uses stable identity plus one observable outcome sentinel. Auto Buy checks the requested queued level, Auto Concept checks the requested assignment change, spell leveling checks native mastery advancement, Auto Cast checks the audited `Spell.Fire` hook for a new cast and native casting-state deactivation for a player-requested toggle-off, and Auto Harvest checks for exactly one engaged matching plot action. No boundary captures payment deltas or assembles a general before/after receipt. After a throw it rereads the same sentinel: an observable requested outcome commits; an absent or ambiguous transition blocks that candidate or feature for the current lifecycle. Recovery is deliberately limited to scene, save-load, reset, or NG+ lifecycle invalidation; ordinary evaluation and configuration polling cannot silently retry an ambiguous mutation.

When the game refuses a purchase Auto Buy planned, the boundary first asks *which fact moved*. It reads `IsAvailable()`, `IsMaxLevel()`, `IsMaxQueuedLevel()`, the game's verdict on the price, every live cost row and its spendable amount, plus elapsed collection time, world-generation drift, and earlier same-batch purchases touching those resources. A price-only refusal is expected snapshot staleness: that candidate is skipped before any native mutation, Auto Buy stays enabled, a newer world collection is required before it plans again, and no synchronous bundle is written. A structural contradiction, a refusal with every readable term passing, or a native call whose queued-level sentinel does not advance remains an invariant violation; those terminate the batch, write one full bundle within the fixed eight-file/1 MiB diagnostic envelope, and turn Auto Buy's own setting off through the central configuration path. A refusal writes one actionable log line; bundle-capture failure stays in that line. One prior structural mismatch otherwise repeated 1,988 times.

Feature health publishes Auto Buy, Auto Cast, Auto Concept, Spell Leveling, Auto Harvest, and Auto
Items independently through Common. Controls and tooltips now separate saved configuration from
progression locks, lifecycle readiness, ordinary operation, temporary queue or safety blocks,
unavailable contracts, partial degradation, and verified faults. The projection consumes existing
engine evidence and publishes only canonical condition transitions; it does not add catalog scans,
candidate work, or native mutations.

Automation consumes the shared Common lifecycle monitor. Scene entry/exit, save loading, save completion, gameplay-manager readiness, reset/NG+, and registry-rebuild observations advance one coalesced generation across the suite. Every generation transition cancels prepared Auto Cast and Auto Concept work before another native mutation can start; equivalent callbacks arriving repeatedly within the same frame are idempotent.

All ordinary services wake from the shared immutable world publication. A newer world generation invalidates a normal idle or evaluation wait, including one produced while an older evaluation was still running; it never bypasses start, evaluation, action, or worker-response fault backoff. Configuration publications use the same generation-safe rule. The world collector is the only source service and retains its own 250-millisecond schedule rather than waking on the publication it produces.

`AffordabilityMode` and `UpgradeAffordabilityMode` are independent:

- `BuyAll` accepts any natively affordable action that passes reserves.
- `Excess10`, `Excess100`, and `Excess1000` limit each resource cost to 1/10, 1/100, or 1/1000 of its current amount.

Reserves are an optional second policy. After each level, Auto Buy requires enough resource for that level plus the larger of `AbsoluteReserve` and `cost × RelativeReserveMultiplier`. Because the game deducts each native cost immediately, a repeated or multiplied purchase rechecks the progressively lower live balance before every next level.

Auto Buy always plans the complete ranked candidate list and lets the native boundary continue until only `LeaveQueueSlots` remain. Structures prefer the live Bulk Development count and reduce it when the remaining batch ledger cannot fund that complete exact group; Upgrades always request one level. Exact rising grouped costs are reserved in the batch ledger, missing exact group sums are never approximated, and every emitted group is capped again to usable queue room.

Upgrade submission temporarily forces the native global multi-buy value to one and verifies that the captured value is restored afterward, including when the setter or purchase throws. If restoration cannot be confirmed through the native getter, further automated Upgrade mutations are quarantined for the process and removed from admission, cached ranking, and pending batches. Structure purchases do not use that global and remain independently eligible.

Continuation is always active: after each candidate's group, Auto Buy advances through the remaining prepared ranking while live queue room permits. The prepared next candidate does not require a catalog rescan. Native availability, current cost, affordability, reserves, maximum level, and queue admission are re-read before every level.

### Queue scheduling

World publications provide Auto Buy's planning cadence while it has no immediately
useful work. Its ServiceCycle worker may return a prepared batch larger than one
frame can safely consume. Common gives Auto Buy one turn of at most 16 exact
one-level action callbacks per Unity frame. The turn stops earlier when the batch
completes, an action rejects or faults, or emergency stop becomes active. Every
callback rereads authoritative queue room and candidate state immediately before
mutation; a skipped native no-op advances to the next action.

Native submission remains on Unity's main thread because the game registries,
ScriptableObjects, resources, and action queue are not thread-safe. The current
16-action count limit is a trace-driven safety bound, not yet a wall-clock
budget.

Auto Buy, Auto Harvest, and world collection share the same fair ServiceCycle frame
pump. Auto Buy's 16-action turn cannot consume Auto Harvest's default one-action turn. The fixed
limit is intended to become a measured frame-time budget once traces establish
the appropriate target and accounting.

The suite is designed to be the only auto-buy plugin in the installation. Running another buyer against the same resources and queue is unsupported.

Auto Buy emits at most one affordable purchase group for each candidate and advances through the prepared ranking before refreshing dirty resource and cost state. With the default `BulkDevelopment` policy, every ranked Structure prefers one live group but falls back to the largest positive count with an exact published sum that the remaining ledger can fund; every Upgrade receives one level. This prevents an affordable next Structure level being silently lost merely because the whole preferred group costs more than current stock. World derivation prices every level in the current bounded native group with the ported game curve, so the batch ledger reserves the exact chosen sum before admitting a later candidate that shares its resources. Every mutation is still admitted independently against live native state.

Active membership and ranked recommendation views use reused buffers and deterministic bounded walks; routine evaluations do not rebuild reflected wrappers or sort the complete registry. The slow ten-second registry reconciliation reuses wrappers when native identity is unchanged.

Native completion signals do not discard a safely prepared Fixed, Bulk Development, or action-multiplier group. The group continues through independently revalidated levels within the current bounded burst, then the prepared ranking advances. Completion effects settle before the next repeat pass, so a newly unlocked higher-ranked candidate can pre-empt that pass. Completed Structures and Upgrades retain their native identity and family; a completion burst coalesces into one refresh of each affected family. Manual queue changes still cancel stale prepared work immediately.

Rapid completion signals are generation-coalesced while a bounded lifecycle settlement is already running. They do not restart its cursor or an in-progress candidate scan; one follow-up generation retains eventual broad effect discovery. A recommendation produced across that window is advisory only and must still pass a fresh authoritative candidate and queue validation before its native mutation.

Routine active and locked-content lifecycle probes run on a fixed 250 ms cadence rather than once per purchase evaluation. Each maintenance slice checks at most eight active and sixteen slow-reconciliation entries, so faster queue turnover cannot multiply background reflection work.

Structures must pass native availability before Auto Buy reads costs or calls the purchase contract. The supported native `UpgradeSO.CanPurchase()` contract combines affordability with lifecycle, requirements, and queue admission, so Auto Buy calls it first and then decodes the exact current cost before classifying a false result. A proven reserve or affordability failure remains subscribed to its resource dependencies; a cost-safe false result is parked outside high-frequency quantity updates for bounded lifecycle or completion retry. Native bandwidth costs are explicitly identified through `ResourceSO.IsBandwidthResource()` and remain tracked because their admission uses missing usage rather than ordinary quantity.

Repeated native mutation failures are rate-limited per candidate while aggregate attempt/failure totals remain visible. Reflection metadata for queue room, queued-level verification, and the global multi-buy contract is cached only after exact signature validation. The live multi-buy variable itself is fetched again for every Upgrade level so save or lifecycle replacement cannot leave a stale native reference.

During a prepared ranked pass, each candidate refreshes its own cost immediately before its level. Shared resource-dependent invalidation is coalesced while later candidates are still live-revalidated against current resources and native state. A failed admission skips that candidate for the current pass; an ambiguous native mutation failure ends the pass so dirty state can settle safely. If the queue fills, the next ranked candidate remains prepared across the wait and is live-revalidated when a slot reopens.

Queue capacity is refreshed after that live candidate/cost/reserve validation and immediately before every queued mutation. The supported native adapter reads total capacity from `ActionManager.instance.actionableItems.maxQueuedItems.AsInt()` and live room from `ActionManager.GetRemainingRoom()`. Negative values, remaining room greater than capacity, missing native objects, or an invalid policy input reject the snapshot and submit no purchase. The snapshot derives occupancy and subtracts `LeaveQueueSlots` exactly once before applying the current batch usage limit.

## Auto Scribe

`AutoScribe.Mode=Active` produces the six audited levelled Scroll recipes in semantic cost order:
Advancement, Power, Learning, Excellence, Development, then Echoing. `AutoScribe.Roles` narrows that
set by stable semantic keys; empty selects all and `none` selects none. The selected roles are
pinned to the ServiceCycle configuration generation under the runtime doctrine's bounded-staleness
rule. Actions contain no role key and do not re-read current configuration.

The worker consumes immutable role-to-recipe-to-Scroll-to-enchantment relationships, exact registry
completeness, levelled owned Scrolls, pending Auto Items Scroll uses, native queue work, structure
enchantment coverage, and native target evidence. Player-owned `AutoScribeInstances` count as
external supply; the suite never creates, edits, or removes them. If any enabled producible role
has unknown or contradictory evidence, the entire service blocks for that publication and Runtime
names the exact role and reason. There is no degraded-but-producing state.

The game has no non-UI one-shot craft composite, so one GameAction re-drives the native
`PurchaseQuantity` → `CraftingInstance` construction → `Initiate` → instant-or-queue sequence.
Every type, exact member shape, and constructor is bound for the lifecycle before the action is
available. The boundary then re-resolves and proves the live role relation, queue capacity,
competing supply, target, affordability, exact cost, and ownership before payment. Payment is the
last irreversible risk.

After payment, one sentinel proves either the exact queued recipe or an increase in the exact
instant Scroll stock. Resource charges and `maxStartingLevel` are not recomputed as postconditions.
A native exception at payment, construction, initiation, or admission rereads that same sentinel:
the observable requested outcome commits, while an absent transition faults and quarantines the
Scribe GameAction for the lifecycle. No rollback is attempted.
Every outcome waits for a new world publication; Auto Scribe has no cadence or retry timer.

## Auto Cast

Auto Cast follows equipped spell slot order and fires at most one new spell per evaluation. Empty slots are skipped, active auras are treated as already satisfied, channels pause the rotation, and persistent spells are never turned off automatically. The published world carries both the manager-wide `SpellManager.CanCastASpell()` answer and each slot's `Spell.CanCast()` answer, so busy and unready periods are quiet planning backpressure rather than repeated boundary submissions.

The player-driven `game_cast(mode="toggle_off")` command is separate from rotation policy. It
reproduces the visible spell button's cancel path for one exact active toggle, including the native
Cancellable Spells setting, and verifies only that the exact slot's casting state became inactive.

The rotation cursor advances when a slot is chosen, not when its cast commits. Whether a spell has anything to aim at is an unbounded reflective walk of the live effect graph with no snapshot form, so the worker cannot see it and the boundary is where a targetless spell is refused; a cursor that waited for a commit would re-pick that same spell every cycle and starve every other slot. A refused cast costs itself its turn and comes round again one rotation later.

`FullCharge=true` holds charge-capable spells through the game's native charge-input contract until the full-charge point. While Auto Cast owns that hold, the rest of the rotation pauses. The hold is released when charging completes, Auto Cast is disabled or emergency-blocked, the setting is turned off, manual spell input occurs, or the plugin shuts down. Set `FullCharge=false` to fire charge-capable spells immediately without charging.

A planned cast is advisory. Before firing, Auto Cast rediscovers the slot and requires the stable recipe UUID, exact native Spell reference, runtime type, and slot index to match, then repeats manager and spell readiness. A manager or spell that became unready after publication is recorded with its exact reason as an expected skip; structural identity failures and genuine mutation failures remain rejected or faulted. Scene changes, save implementation, and player-manager restarts discard prepared casts immediately.

Every finite-cap resource used by immediate or drain costs must meet `StartResourcePercent`. Immediate costs also pass the shared reserve policy. Manual casting pauses automation for `ManualPauseSeconds`, and an existing manual target prompt is never replaced.

The button shows desired intent as `AC OFF` or `AC ON`; emergency blocking preserves that intent and renders `AC ON / STOPPED`. Runtime readiness and fault detail remain in the same published status shown by the tooltip and Mods Runtime. Its quick control and Mods rail entry share the static audited Casting Speed attribute glyph, independent of the equipped-spell loadout.

## Auto Concept

Auto Concept uses the shared `OrbModding.Common.AlchemyGameplayDomainClassifier` as its concept-versus-ordinary-alchemy identity boundary. The classifier resolves the exact `ConceptRecipes` UUID/type asset and requires each exact `AlchemyRecipeSO` registry member to carry mutation-grade static-contract, serialized-asset, exact-runtime-type, stable-identity, registry, and relationship evidence; contradictory ordinary-and-Scholar typing fails the lifecycle closed. Auto Concept separately resolves `ActiveConcepts` for native slot and quantity ownership. It never uses the global alchemy recipe registry as a concept catalog and never mutates ordinary alchemy.

`ActiveConcepts`, `ConceptRecipes`, and the spell-level `UnlockLevelAllSpells` upgrade resolve through Common's lifecycle-aware typed registry resolver. Missing or not-yet-registered assets remain retryable, while wrong type, UUID contradiction, or unavailable audited accessors fail closed with structured UUID/type/status/evidence diagnostics. A generation change invalidates the retained native reference before another read or mutation.

`Mode=Active` ranks every discovered concept by mastery level, fractional XP progress, and stable UUID. Locked or undiscovered concepts never enter the candidate set. It assigns one instance to each currently compatible acquired slot before deepening active assignments. Depth is submitted as one native batched quantity change to the recipe's live mastery maximum.

Candidate quantity limits come from the native `AlchemyRecipeSO.GetMaxUsageSlots()` result. The
collector does not interpret the raw `maxUsageSlots` modifier because its `-1` value is a sentinel
that the game resolves to a mastery-derived or unlimited maximum.

The same publication captures native prospective drain vectors for the exact quantity targets the
boundary's halving ladder can choose. Planning compares each positive incremental drain through the
published resource quality, true rate, current drain, capacity, and configured rate/quantity floors.
A target the publication already says would violate a reserve is quiet backpressure and is not
scheduled; a lower safe target may still be selected. The GameAction independently reconstructs the
prospective vector and repeats every check against live native state before mutation. Boundary-time
resource backpressure keeps its exact result code but counts as an expected skip, while an unavailable
projection contract remains a loud rejection.

`SlotManagementMode=TimedCycle` is the default once Auto Concept is enabled. It permits complete settled replacement across concept types, but every assigned concept receives its full configured settled-active period before rotation; catching the current highest mastery never ends that session early, and least-recently-assigned ordering prevents any unlocked concept from being starved. Before removal, the native boundary proves that releasing the exact active assignment will open either a matching typed slot or a typeless slot for the replacement. `RotateAll` instead removes one settled active concept only if a same-type inactive concept has strictly lower mastery, waits for native settlement, and then adds the exact planned replacement. Equal mastery never rotates on UUID ordering alone. `PreserveManual` never removes the quantity present when Auto Concept starts; it can rotate only assignments that Auto Concept added itself.

Every newly assigned lower-mastery concept in `RotateAll` or `PreserveManual` receives a catch-up training session. The session captures the highest eligible mastery level and fractional progress at assignment time, becomes timed only after the native quantity is settled and active, and protects the assignment until it reaches that target or `TrainingPeriodSeconds` elapses. `TimedCycle` uses the same timer but never applies the catch-up shortcut. The default is 30 seconds and the accepted range is 10 through 3600 seconds. Native setup time does not consume the period, and the controller schedules the exact next session deadline in addition to ordinary world/configuration wakes. A verified assignment starts its session from the accepted queued target; a later depth settlement is recorded as suite-owned and cannot restart that deadline.

The registered Auto Concept quick control toggles committed intent with the same `ScreenScholar`
book used by its Mods rail entry. OFF uses the native recessed view-button frame; configured ON
uses the raised frame, including `ON / STOPPED` and faulted states. Runtime health remains in the
tooltip. The legacy `ShowToggleButton` config-file key is retained hidden and ignored because every
registered automation feature always owns one quick control. Spell-leveling state remains on the
suite's Auto Buy tooltip rather than becoming a separate control.

Before every add or rotation, Auto Concept reconstructs that exact prospective native drain vector, rejects every positive drain whose authoritative resource state is zero, converts the remainder through each resource's live quality with `ResourceSO.GetTrueSpend`, and compares the projected rate with `RateReservePercent`. Finite resources must also meet `MinimumResourcePercent`. A replacement whose resource is at zero is skipped without blocking other resource-safe concepts or acquired slots in the timed order. Unknown vectors, identity mismatches, incompatible slots, and changed mastery limits fail closed. Each world publication checks cached active assignments; if the native drain ratio falls below `MinimumDrainRatio`, a drained resource reaches zero, or its live net rate becomes negative, it schedules removal of only the quantity recorded as suite-owned.

The prospective multiplier is not a published fact. The native answer for quantity N exists only after constructing a throwaway `AlchemyInstance`, setting its quantity, and calling `GetDrainCostMod()`, and a published recipe's drain-cost scalar is not evidence that it reproduces that instance method. The halving search, reserve test, quantity floor, and subtraction of the live current drain therefore stay together in the action adapter's preflight, immediately before any add or rotation removal.

A live slot or prospective-drain refusal ends Auto Concept's work on the current world reading. The
engine waits for a strictly newer world before the action result settles and the candidate re-enters
ordinary timed ordering. There is no candidate deferral, retry deadline, fallback poll, or second wake
path. If the collected facts still propose a candidate native refuses, that candidate is rejected
again and may starve the later order; the repeated `BepInEx/LogOutput.log` record is evidence of a
collection gap to audit. Verified quantity changes and rejected preflights name the affected recipe
UUIDs and native reason.

Enabling the feature initializes the scoped shared classifier and snapshots current Active Concept quantities for ownership and rollback accounting. Disabled Auto Concept neither initializes nor rebuilds classifier/catalog evidence. Unexpected settled changes are rebaselined as player-owned. `PreserveManual` never replaces that baseline; `RotateAll` explicitly permits a complete settled assignment to be replaced for mastery balancing, but the drain watchdog still rolls back only Auto Concept-added quantity. Disabling the feature stops work and leaves native quantities unchanged. Save loads, scene changes, and manager lifecycle resets (including reset/NG+ manager restarts) invalidate classifier and runtime references and rebuild a new baseline only after Auto Concept is active again.

## Diagnostics

Transient shared classifier readiness failures use the existing 30-second Auto Concept warning gate. A contradictory or permanently invalid concept-domain contract blocks Auto Concept for that lifecycle and is logged once; `Unknown` evidence never falls back to ordinary names or broad alchemy membership.

Warnings and errors are always emitted. After a problem, use **Create bug report** on Mods Runtime to flush and package the recent-event ring, decision journal, configuration, identifiable save files, and redacted log into one capped zip. **Check game math** remains a separate read-only diagnostic. Schema 5 retires the old global logging switches, rejection cap, and detailed-logging preferences because ServiceCycle does not consume them.

Auto Buy decisions use append-only Common codes rather than parsing diagnostic text. Candidate threshold parking, rejection telemetry, the latest tooltip status, and Runtime presentation all consume the same immutable decision. Observed quantities and wording can change without producing a new condition; stable thresholds, identities, policy, queue limits, and native states do produce a transition. Equivalent conditions are not republished to future Insights subscribers.

Every automation log message includes local date, time to milliseconds, and UTC offset so runtime reports can identify when a failure began.
Successful Auto Concept initialization records separate scoped-recipe, active-loadout, and eligible-candidate counts. While an active assignment is settling or training, the feature tooltip and Mods Runtime report that wait. After training, a timed cycle with no other unlocked assignment reports a progression-locked status. A native rejection ends that service's work on the current world reading; the rejected candidate re-enters ordinary planning after the next collection. Persistent refusal is reported again on every publication and deliberately starves later candidates, because it identifies a world-collection gap rather than a condition to route around. Locked concepts remain excluded rather than delaying or satisfying the rotation. Runtime trace, the decision journal, and the trace dashboard distinguish assignment reservation, settled training start, catch-up or timeout completion, rotation, and an idle balancer from a missed evaluation.

Orb of Creation's LeanTween pool defaults to 400 simultaneous tweens. AutobuyOrb raises that capacity because very large purchase bursts can create enough UI popups to exhaust it. This is separate from queue scheduling; the suite does not currently override the global tween pool. If the BepInEx log reports LeanTween exhaustion or UI animations begin disappearing during unusually large batches, treat a restart-time tween-capacity option as a separate performance feature.
