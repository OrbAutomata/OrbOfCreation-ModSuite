# Changelog

## Orb Of Creation ModSuite 0.6.0 — 2026-10-16

### Install

- Each release now has `OrbOfCreation-ModSuite-<version>.zip`. Extract it into the game folder,
  the one with `Orb Of Creation.exe`, and the suite lands in `BepInEx/plugins/OrbModSuite/`.
  No folders to create, nothing to rename. Updating works the same way.
- The installation guide follows the zip, and a missing BepInEx log has a short checklist in
  troubleshooting.
- The project lives at `github.com/OrbAutomata/OrbOfCreation-ModSuite`; all links point there.

### New

- **Mods > Runtime > Create bug report** packs the recent history of what happened, your
  settings, your save files, and the log into one shareable zip and opens its folder.
- When something in the game keeps failing and the game stops advancing, the suite shows a
  banner telling you to return to the main menu and reload, and writes a bug report for you.
- The documentation is a searchable website, and its game guide covers rituals and alchemy.

### Fixed

- Automation stops retrying actions the game will refuse: spell levels you cannot afford,
  casts while the caster is busy, and Concept training that would break your reserves.
- Auto Buy checks the price again right before it buys, and counts what each purchase in a
  batch really cost before buying the next.

### Improved

- The suite's history of recent decisions stays under 64 MiB while covering up to about 13 hours,
  and the BepInEx log no longer lists every successful action. Old history files are cleaned
  up once, after the save backup.
- Automation planning asks the game far fewer questions each tick.

## Orb Of Creation ModSuite 0.5.3 — 2026-08-02

### Fixed

- Auto Buy bought Artificer structures while the Workshop tab was still locked. It now
  only buys from lists whose every tab is actually open, so nothing unlocks ahead of
  your own progression.

## Orb Of Creation ModSuite 0.5.2 — 2026-08-02

### Fixed

- Auto Buy bought nothing since 0.5.1: the new reachability check mistook every
  structure and upgrade for unreachable. It now buys anything you can see on a screen
  you can reach, and still refuses what you genuinely cannot.
- A feature that loses all of its work to one cause now reports itself as blocked with
  that exact reason, instead of showing "operational" while doing nothing.

## Orb Of Creation ModSuite 0.5.1 — 2026-08-01

### Fixed

- Auto Buy no longer pays for structures it cannot reach or has no empty spot to place.
- Auto Harvest skips a harvest that changed between planning and clicking.
- The false "concept instances: 1 skipped" warning is gone; empty concept slots are
  capacity, not lost data.

### Improved

- Auto Scribe crafts only the Scrolls your structures still need, and never pays for a
  craft the game would silently discard.
- Every release is built in CI from checked-in, hash-locked game references with a
  pinned SDK, and ships with published checksums.

## Orb Of Creation ModSuite 0.5.0 — 2026-08-01

### New

- Automatic save backups: first run and every update copy your saves into `backups/` before
  automation starts; the five newest are kept. A failed backup blocks automation.
- Auto Items: automatic Scroll and Relic use, plus a picker to approve specific temporary Fruits,
  Potions, and Threads.
- Auto Scribe: automatic Scroll crafting.
- Emergency stop below the game's gear and character buttons, with a fold-out panel of per-feature
  switches.
- Runtime activity chart: completed actions per minute, per feature, over the last thirty minutes.

### Fixed

- Auto Buy no longer skips bulk purchases it can afford; it buys every level it can pay for.
- Spell leveling no longer stalls on one unaffordable spell.
- Auto Harvest works with the Agromancy screen closed.
- Auto Concept ranks training by the game's real experience requirements.
- Mouse wheel works on all mod screens; clicks no longer fall through panels.
- Mod controls appear together right after loading; the Mods tab behaves like the game's own tabs.

### Improved

- Auto Concept settles into new training in 30 seconds instead of 5 minutes and rotates through
  all unlocked concepts.
- One shared automation rhythm; the per-feature timing settings are gone. Settings migrate
  automatically.
- Repeated log lines collapse into short summaries.
- The Windows installer refuses to run while the game is open.
- Debug builds replace the F10/F11/F12 shortcuts with a local control server.

## Orb Of Creation ModSuite 0.4.0 Beta 1 — 2026-07-29

- Support Orb of Creation `1.0.5-2` on Windows after checking the current game assemblies and
  comparing ModSuite's costs, rates, modifiers, affordability, and requirements with the running game.
- Move all supported automation and Mentor work onto one shared service cycle. Decisions run from
  captured game state, and every action is checked again immediately before it changes the game.
- Add Auto Harvest, spell leveling, clearer Runtime diagnostics, compact gameplay controls, and a
  Mods screen that uses the game's own visual style.
- Put **Emergency disable** on the General page. Unknown but complete game builds now open in a safe
  compatibility mode, with all gameplay changes stopped until the player chooses to continue.
- Keep incomplete game installations closed, bind an unverified-build choice to the exact two game
  files, and reset that choice automatically after another game update.

## Game-native UI overhaul — 2026-07-28

- Move the five feature controls and STOP out of the expanding spell-slot row into the audited
  empty lane inside `RightSidebar/AttributeBar`. The controls now form a compact 2×3 tray of
  34-pixel native spell frames; STOP keeps a distinct gap without consuming another horizontal
  spell-slot-sized run.
- Use the full Mods frame: the native title panel now spans the top edge, the rail and detail pane
  fill the middle, and the staged Apply/Revert footer occupies the bottom edge with only deliberate
  gutters between them.
- Retain profile-build-only UI validation shortcuts: F12 invokes the audited native Continue
  action on Start, F11 toggles Mods on Main, and F10 advances the Mods rail. These keys do not
  mutate configuration or native gameplay state.
- Correct native visual capture against the production view state: the Magic/Scholar rail is sampled
  while its source view is inactive, and spell frames come only from direct children of the audited
  gameplay `CastingBar/SmallSpellList` instead of requiring unrelated active spell buttons to agree.
- Remove the text quick-control and flat horizontal Mods-shell fallbacks. Native capture now retries
  only for bounded startup timing; a terminal mismatch logs the exact reason at error level and
  publishes a failed Suite UI capability on Runtime. Successful installs self-report both surfaces
  in the BepInEx log before the Mods panel is opened.
- Source Auto Buy's quick icon from audited
  `GlobalVariables.GetGlobalStructureType().GetIcon()` instead of the cloned queue toggle's optional
  image, while preserving the single pixel writer and the existing status contract.
- Advance the unified configuration file to schema 5 and transactionally discard the retired Auto Buy
  scan cap, rejection cap, global logging switches, Mentor detailed logging, mastery event probe, and
  verifier shortcut. ServiceCycle plans from complete published snapshots; maintained diagnostics are
  explicit Runtime actions, traces, journals, warnings, and errors.
- Keep cadence controls, but describe them as minimum service-planning intervals or maximum idle
  fallback delays instead of per-frame scan budgets.
- Audit the game's native nested-navigation and spell-button visual contracts. The suite samples
  the exact inactive-capable `MainContentContainer/SubviewRadio` frame family, exact named top-bar
  icons, and bottom-bar spell-frame structural paths; a mismatch is a suite defect on the audited
  game baseline, not a request to render alternate chrome.
- Replace the cramped quick-strip labels with native spell-frame icon controls for Mentor, Auto
  Harvest, Auto Concept, Auto Cast, and Auto Buy. Desired Off, desired On, unhealthy, and
  emergency-stopped states share one renderer; STOP uses a separated alert control and retains its
  immediate press-to-stop, press-to-resume semantics.
- Reframe the Mods surface with the game's audited vertical subview-radio vocabulary and preserve
  the full `Orb Of Creation ModSuite` title.
- Replace the nine legacy section tabs with a Runtime/General/feature/Advanced rail, merge Mentor's
  spell, artifact, and alchemy policy into one page, and move each feature mode to an immediate
  status-card command backed by the committed configuration store. Policy fields remain staged
  behind the conflict-protected Apply/Revert transaction.
- Lead Runtime with a compact two-column feature-health grid ordered by severity, then present
  recent events and verification before full trace, profile, pump timing, journal, and detailed
  native-framed diagnostics.

## Configuration publication and application ownership — 2026-07-28

- Route Auto Buy, Auto Cast, Auto Concept, Mentor, and emergency-stop quick controls through the
  same saved-configuration publication as the ServiceCycle. The fresh configured intent now reaches
  the cycle before the control returns instead of waiting beside stale per-service status.
- Make status notifications expose the new joined snapshot synchronously instead of briefly
  leaving subscribers to read the prior configured intent. Configured intent no longer travels
  through feature, lifecycle, startup-fault, or deferred-activation status writers at all.
- Give the suite renderer exclusive ownership of cloned quick-button graphics. Native Unity
  hover, press, release, and selection transitions no longer have a second path that can repaint
  Auto Buy, Auto Cast, Auto Concept, Mentor, or emergency-stop button state.
- Remove the one-entry application service registry and its production-composition wrapper. The
  plugin now owns the one deferred ServiceCycle activation directly; Common's typed registry remains
  the sole seven-service ordering and pump boundary.
- Replace the temporary UI generation and per-feature configuration relays with the ServiceCycle's
  one `ConfigGeneration`. Feature bridges publish runtime health only; one status join combines it
  with committed saved intent for every player-facing feature.
- Make one configuration store the application source of truth. BepInEx entries only deserialize and
  persist explicit values; controls, ownership, activation, emergency state, button visibility, status,
  and services all read the store's committed snapshot.
- Absorb binding-time notifications into generation 1, compute quick-control changes from committed
  state even while an external edit is pending, and route STOP/resume and fail-closed Auto Buy
  stand-down through the same synchronous store.
- Centralize host-construction failure and unavailable projection, remove seven startup-failure
  callbacks and the production-composition forwarding file, and require feature diagnostics/status
  dependencies instead of silently running without presentation.
- Preserve trace wire format, assembly shape, native-contract manifest schema 3, and the
  `spell.get-icon` legacy allowlist.

## Mentor on the shared engine — 2026-07-28

- Move Mentor onto ServiceCycle. Exact spell, artifact, and alchemy mastery gains remain deliberate bounded patch inputs, while recipient selection, source policy, unlocks, ordinary-alchemy classification, and sharing arithmetic now run in the background from the shared world snapshot.
- Preserve `EquippedSpells` and `HighestDiscovered`, Shared Pool and Per Recipient economies, opt-in artifact/alchemy sharing, exact mastery ceilings, native postcondition verification, recursion suppression, Alt+M, and the `M ON/OFF` dual-axis status control.
- Retire Mentor's legacy controller, live configuration reads, catalog/reconciliation ledgers, redundant discovery/loadout/reset hooks, and every Mentor legacy native contract. Configuration schema 4 discards its obsolete operations-per-frame and CPU-budget settings.
- Remove the now-vacuous shared performance coordinator, its weighted admission and mutation leases, coordinator evidence tool/profile, and synthetic coordinator fixtures. Mods maintenance keeps one pass per frame; gameplay invalidation keeps its local fixed drain cap.
- Name Mentor in trace rosters and add stable dashboard labels for its input sequence, missed inputs, planned actions, and recipients without changing the trace wire format.

## Auto Concept on the shared engine — 2026-07-28

- Move Auto Concept onto ServiceCycle. It now ranks recipes, balances assignments, tracks training sessions, and owns automated quantities in the background from the shared world snapshot.
- Publish Concept recipe membership and core types, active and queued quantities, drain ratios, and authored/current drain vectors. Retire the legacy controller, reflection planner scans, and all three Auto Concept signal patches.
- Preserve the five-step decision order: unsafe owned-drain rollback, recent-rotation replacement, breadth, mastery or timed rebalance, then depth.
- Keep the game's quantity-dependent prospective drain calculation at the native action boundary. Its halving search, rate reserve, quantity floor, live identity and settledness checks, and verified add/remove postconditions remain authoritative.
- Remove Auto Concept's two legacy CPU-budget work identities and profile rules; the shared budget coordinator remains for Mentor, Mod Config, and gameplay-invalidation work.

## Auto Cast on the shared engine — 2026-07-28

- Move Auto Cast onto ServiceCycle. It now decides which spell to cast in the background from the shared world snapshot, on its own schedule, instead of walking the live loadout on the main thread every time it looks.
- Publish the equipped loadout and what each slot costs to cast, so the rotation is chosen from collected facts rather than by questioning the game about every equipped spell.
- Keep the rotation, the reserve floor, the resource start threshold, the full-charge hold, the channel pause, and the manual-cast pause exactly as they were. One spell per cycle, first eligible slot from where the last cast left off.
- Change one thing you may notice: a slot now gives up its turn when Auto Cast picks it and the game then refuses the cast — most often because the spell has nothing to aim at. Previously that slot was re-picked on the next look and could hold up the rotation; now the next slot goes instead and the refused one comes round again on its next turn.
- Expire the manual-cast pause on a clock rather than by counting down each frame, so a pause taken just before the game is paused or the plugin stops ticking no longer outlives the moment that earned it.
- Auto Cast keeps its settings, its toggle button, its shortcut, and its status line.

## Spell Leveling on the shared engine — 2026-07-28

- Move Spell Leveling onto ServiceCycle. It now picks its spell in the background from the shared world snapshot on its own schedule, instead of waking only when an Auto Buy purchase happened to finish, so a spell that becomes ready while nothing is being bought is levelled within about a second rather than waiting for the next completed purchase.
- Publish each spell's own answer to whether it can buy its next mastery level, so the choice of which spell to level is made from collected facts rather than by questioning the game about every spell.
- Level the lowest-mastery ready spell first, as before. Two spells tied at the same mastery level now resolve in a stable identity order rather than by the text of their internal identifier; which of the two goes first can differ from previous builds.
- Remove the last Harmony hook on the game's purchase-completion path. It existed only to nudge spell leveling, which no longer needs one.
- Spell leveling keeps its settings, its place under Auto Buy's switch, and its Locked, Single and level-all progression exactly as before.

## Auto Buy per-level prerequisites — 2026-07-27

- Stop Auto Buy planning purchases the game refuses because the level being bought has requirements of its own. An upgrade or structure whose next level waits on a research entry, another upgrade, a spell, a ritual, an alchemy recipe or a global count is now left alone until that requirement is met.
- Read each entity's per-level conditions once per game session and publish them on the shared world snapshot, so the verdict is worked out in the background from facts the suite already holds rather than by asking the game about every candidate.
- Refuse a purchase whose conditions the suite cannot evaluate, rather than assuming they hold.
- Extend the differential verification diagnostic with two passes that compare the suite's verdict against the game's own per-level check for every upgrade and structure, and report an incomplete run naming any condition class the suite does not model.
- Move differential verification to `Ctrl + Alt + Y`. Both earlier defaults were built on M, which is Mentor's toggle key: BepInEx fires a shortcut only when the keys held are exactly the ones bound, so `Ctrl + Shift + Alt + M` could never fire at all, and the plain `Alt + M` before it fired a Mentor toggle and a frame-freezing diagnostic together. A configuration file still carrying either default is rebound once, on the first launch that reads it; a chord you chose yourself is kept.

## Orb Of Creation Mod Suite 0.4.0 Beta 1 — 2026-07-23

- **Breaking.** Consolidate Orb Automata, Orb Mentor, Orb Mod Config, and Orb Modding Common into one assembly, `OrbModSuite.dll`, under the new plugin GUID `dev.vojow.orbofcreation.modsuite`. The four retired GUIDs are gone.
- **Breaking.** BepInEx derives a configuration file name from the plugin GUID, so the suite creates a new, empty configuration file on first start. **No settings are migrated from the retired per-plugin configuration files**; they are never read. Reapply your settings after upgrading, and delete the old `OrbAutomata.dll`, `OrbMentor.dll`, `OrbModConfig.dll`, and `OrbModding.Common.dll` before starting the game.
- Refuse to load against a game build that does not match an audited assembly baseline, and log the observed and expected hashes. The suite computes the game's economy math itself, so an unaudited build has no degraded mode; a game update disables the suite until the build is re-audited.
- Add ServiceCycle, a shared engine that handles scheduling, background decisions, save and scene changes, diagnostics, and shutdown for automation features.
- Move Auto Harvest and Auto Buy onto ServiceCycle, and publish one shared world snapshot per frame that both read. Auto Harvest keeps its existing settings and behavior and remains disabled by default.
- Add three separate diagnostics: a detailed full trace, a compact rolling decision journal, and an opt-in performance profile.
- Add a command-line trace reader and an interactive HTML timeline that combines those three recordings.
- Add a Runtime page to the configuration UI with service health, recording controls, journal status, and a graph of the last 1,200 frames.
- Make the main build, test, packaging, and validation scripts work on macOS and Linux while keeping Windows Mono as the game target.
- Read automation settings into one consistent snapshot instead of letting individual features read live configuration entries at different times.

## Auto Buy bounded purchase bursts — 2026-07-20

- Allow one Auto Buy coordinator lease to submit up to 16 exact one-level native purchases when live queue room and the existing 1 ms purchase slice permit, while retaining one mutation-owning feature per suite frame.
- Revalidate mode, emergency state, ownership, lifecycle generation, candidate admission, live costs, reserves, and queue capacity between every level; stop immediately on any boundary, failure, ambiguous result, or operation cap.
- Add deterministic 1/2/4/8/16 burst-cost tests, Bulk-3 and finite-Upgrade fairness, per-Upgrade native multi-buy restoration, queue/reserve/capacity containment, lifecycle/ownership/emergency interruption, ambiguous-mutation quarantine, coordinator accounting, Auto Cast fairness, and eight-completion-per-frame throughput coverage.
- Bump Orb Automata to 0.8.10.

## Auto Buy grouped continuation and rejection fairness — 2026-07-20

- Replace the overlapping `RespectActionMultiplier`, `RepeatWhileAffordable`, `StructureRepeatMode`, and `FixedStructureLevelsPerCandidate` controls with `PurchaseGrouping` (`Single`, `Fixed`, `BulkDevelopment`, or `ActionMultiplier`) plus `FixedGroupSize`.
- Separate group size from continuation: give each ranked Structure its live configured group, give each Upgrade one level except in action-multiplier mode, advance through the prepared ranking, and repeat passes while live queue quota and admission permit. Every individual level still revalidates cost, reserves, completion, ownership, and queue room.
- Migrate Automata configuration schema 1 to 2 with destination-first precedence, preservation of legacy action-multiplier intent, direct Structure-group mapping, and fail-closed malformed legacy values.
- Close a starvation case by advancing past definite pre-mutation rejection and retrying it on bounded 0.25-to-5-second exponential delay; attempted or ambiguous mutations retain lifecycle quarantine.
- Exercise Bulk Development 10/25/100/100 in the synthetic early/mid/late/endgame stages. The endgame model now submits 180,024 purchases in 180,408 frames (3,006.8 simulated seconds), reduces purchasable idle frames from 5,996 to 291, candidate evaluations from 360,072 to 183,645, and observed operations from 1,838,247 to 923,925.
- Add runtime-derived Auto Buy simulations for endgame Bulk Development 1/3/10/100, staggered cost-read outages, Bulk-3 completion storms, exact partial-group reserve boundaries, indivisible heavy-tail reads, and live catalog growth from 28 to 137 candidates.
- Bump Orb Automata to 0.8.9.

## Runtime validation corrections — 2026-07-20

- Make Mentor Artifact XP postconditions level-aware by predicting the exact native `ExperienceContainer` transition on a clone, then verifying the live equipment mastery, container level, residual XP, and saved XP. Multi-level rollover no longer produces the false lifecycle fault the earlier raw-XP verifier reported.
- Complete the configured-versus-runtime presentation boundary: gameplay controls keep stable `ON`/`OFF` intent while waiting, blocking, degradation, and faults remain secondary structured health in tooltips, notices, and the configuration UI.
- Format feature health and Auto Buy reserve evidence as bounded line-oriented tooltip rows, including deterministic per-resource required, available, cost, reserved, and shortfall fields. Every visible line owns a native tooltip node so wrapped reasons cannot collide with separators or later fields.

## Selectable test strategy lanes — 2026-07-20

- Centralize the maintained repository strategy, headless/replay/native-contract/runtime protocols, and test-steering entry points under `docs/testing/`, with compatibility redirects from their former development paths.
- Add owned testing guides for Common, Mentor, Mod Config, suite integration, and an Automata subtree split into Auto Buy, Auto Cast, Auto Concept, spell leveling, and cross-feature integration.
- Preserve the risk-based unit, headless, installed-contract, and runtime-UAT pyramid while adding explicit fast, Auto Buy decision, Auto Buy reliability, Auto Buy performance, complete performance, replay, and external-process development lanes.
- Partition portable CI into fast, deterministic-performance, and external-process scopes, retain TRX evidence for portable, contract, performance, and coverage runs, and stop rerunning headless journeys already owned by the fast partition.
- Establish the first selectable reliability corpus and Auto Buy subset from dirty-resource, native multi-buy, runtime replay, lifecycle, ambiguous-mutation, live-reserve, and live-queue-capacity journeys.
- Report overall and per-assembly branch coverage diagnostically while retaining the existing reviewed line-coverage floors until branch baselines are reviewed.
- Add a maintained Auto Buy negative-simulation matrix covering invalid queue, cost, availability, lifecycle, purchase, completion, and simulator-contract paths, with focused failure, race, and completion suites.
- Add four deterministic 240-event Auto Buy state-machine tapes with per-event invariants, replay comparison, bounded diagnostic tails, and first-failing-prefix reduction.
- Add adverse modeled workloads for scarcity, locked catalogs, live capacity changes, manual bursts, completion bursts, lifecycle replacement, and resource-observation outages; retain the persistent rejecting-leader case as an explicit skipped gate for the unresolved starvation policy.
- Add an Auto Buy reverse-engineering dossier covering the native purchase transaction, shared queue/completion state model, simulation-to-evidence mapping, and the distinction between observed progression facts and synthetic stress profiles.

## Progression-shaped Auto Buy performance simulations — 2026-07-20

- Add deterministic early, mid, late, and endgame Auto Buy stress workloads with increasing Structure/Upgrade catalogs and queue-completion rates from one action per second through one action per simulated frame; these names describe modeled workload shapes rather than observed save populations.
- Exercise exact per-Structure targets of 10 early, 40 midgame, 100 late, and 1,000 endgame levels, all 180 mapped Structures in late/endgame, finite one-level Upgrade purchases, queue saturation and refill depth, bounded candidate work, and idle-room detection.
- Extend the checked performance report with stage composition, repeated-Structure coverage, and deterministic frames/seconds to every stage target so CI can detect workload drift and scheduler throughput regressions.
- Add a focused `AutoBuyDecision` policy contract for current group precedence, rerank/pass behavior, reserve monotonicity, unavailable-candidate isolation, fairness, and deterministic output.
- Compare stage submission time with a one-mutation-per-frame theoretical scheduler and split purchasable-work idle frames into evaluation-only versus other deferred work; the current endgame gap is 5,988 frames at 96.783% submission efficiency.

## Versioned configuration schemas — 2026-07-19

- Add a shared pre-bind configuration transaction with hidden schema markers, exact-byte rollback, verified all-or-nothing first-free sibling backups, ordered reviewed migrations, and fail-closed malformed or future-version handling.
- Migrate Automata's proven schema-zero Concept mode and fallback-interval values, explicitly discard its reviewed obsolete keys, and leave Mentor and Mod Config value interpretation unchanged through marker-only steps.
- Publish sanitized exact-plugin schema outcomes through Common and hand them atomically to Orb Mod Config's Unity tick separately from runtime health and Apply results. Failed/future suite plugins remain selectable as read-only status-only tabs even when no settings were bound.
- Bump Orb Automata to 0.8.8, Orb Mentor to 0.3.8, Orb Mod Config to 0.6.3, and Orb Modding Common to 0.3.7.

## Orb Mod Config 0.6.2 Beta 1 — 2026-07-19

- Size setting rows from their rendered descriptions so complete help, acceptable-value, restart, and saved-versus-runtime text remains readable instead of being ellipsis-clipped in a fixed-height area.
- Preserve the absolute scroll offset when staged edits, Default, Apply, Revert, external refreshes, or responsive width changes rebuild the same page; reset to the top only when selecting another mod or feature section.
- Remeasure visible rows when resolution, window width, or UI scale changes while retaining the separate exact-plugin runtime-status band and configuration-only save confirmation.

## Enforced shared performance profile — 2026-07-19

- Apply the checked V1 profile's exact starvation thresholds to all twelve supported coordinator identities while retaining the constructor fallback for unknown work and allowing test coordinators to select a tighter threshold.
- Make post-construction shared CPU budgets tightening-only, expose the allocation-free remaining soft budget, and clamp coordinated Mentor planning to that remaining time without losing pending plans or XP.
- Promote cooperative timing, combined-frame timing, wait, starvation, abandonment, work-failure, and measurement targets to a CI gate with a distinct target-failure exit; native timing remains observe-only after a complete uncontaminated sample window.
- Bump Orb Mentor to 0.3.7 and Orb Modding Common to 0.3.6.

## Fail-closed automation admission adapters — 2026-07-19

- Normalize stable identity, availability, native readiness, immediate cost, drain cost, and queue requirements before shared Auto Buy and Auto Cast policy evaluates an action.
- Split Structure and Upgrade reflection into exact family adapters while retaining live per-mutation revalidation, native multi-buy restoration, and exact queue-delta postconditions.
- Reject complete spell cost admission when any bounded entry is malformed or contradictory, and disable all Automata native mutation setup when installed game assemblies do not match the audited hashes.
- Reserve the shared Harvest and Scroll action families for future explicit adapters, add cross-adapter equivalence and failure-path coverage, and bump Orb Automata to 0.8.7.

## Action-family ownership isolation — 2026-07-19

- Add atomic, process-local ownership leases for independent native purchase, cast, concept, spell-level, and mastery-XP action families, with synchronous known-conflict revocation and explicit lifecycle/configuration release.
- Detect the exact AutobuyOrb plugin GUID and block only Automata Structure and Upgrade mutations; Auto Cast, Auto Concept, Spell Leveling, and all Mentor domains remain independently available.
- Gate every supported native mutation again after ordinary live validation, cancel prepared work on ownership loss, expose structured conflict health, and warn honestly that unknown unregistered automation cannot be proven absent or controlled.
- Bump Orb Automata to 0.8.6, Orb Mentor to 0.3.6, and Orb Modding Common to 0.3.5.

## Bounded automation failure circuits — 2026-07-19

- Add a shared circuit state machine with allocation-free transition/attempt checks, capped exponential backoff, and explicit authoritative-event, lifecycle, configuration, and process-lifetime recovery contracts.
- Stop retrying contradictory Auto Buy cost schemas, wake transient resource reads from exact changes, and keep attempted-but-unverified purchases blocked until a newer lifecycle while later healthy candidates remain eligible.
- Isolate Mentor's global and three fixed domain failure circuits so lifecycle recovery clears only transient ambiguity and optional-domain faults do not starve healthy siblings.

## Shared gameplay invalidation foundation — 2026-07-19

- Add one bounded, main-thread Common bus for lifecycle-stamped queue, progression, inventory, registry, resource, and configuration invalidations, with delivery charged through the suite's shared CPU coordinator.
- Coalesce completed-frame bursts by stable domain/UUID/type, preserve merged change kinds and first-publication order, and conservatively promote overflow instead of dropping cache work.
- Keep immediate lifecycle, queue, completion, and Mentor XP safety paths direct while Automata, Mentor, and Mod Config mirror or publish bounded cache and scheduling signals.

## Unified feature health reporting — 2026-07-19

- Add a main-thread Common registry for transition-only configured, locked, not-ready, operational, temporarily blocked, contract-unavailable, degraded, and faulted feature status.
- Project Automata capabilities and Mentor domains from their existing cached lifecycle, unlock, decision, and failure evidence without adding native work or coupling sibling domain failures.
- Give Orb Mod Config an exact-plugin-GUID runtime-status band separate from saved configuration, and stop claiming that a saved setting necessarily applies immediately.

## Structured Auto Buy decisions — 2026-07-19

- Replace Auto Buy's private rejection enum and text-derived deduplication with Common append-only decision codes, dispositions, retry triggers, canonical identities, resource constraints, queue facts, and native states.
- Make candidate parking, telemetry, rate-limited logs, and the gameplay tooltip consume the same immutable decision evidence; publish only condition transitions through an exception-isolated Common channel for future Orb Insights consumers.
- Preserve existing queue output and operation counts while avoiding per-decision formatting and redundant blocker-array allocations in the candidate path.

## Generated supported identities — 2026-07-18

- Replace supported Alchemy, Auto Concept, spell-level, and Mentor unlock UUID literals with 16 generated declarations carrying UUID, expected managed type, and diagnostic name.
- Verify generated output against the canonical entity mapping before compilation; duplicate or invalid IDs, unexpected mapping drift, and stale checked-in output now fail the build.

## Typed registry resolution foundation — 2026-07-18

- Centralize lifecycle-stamped stable-UUID and exact-native-type resolution in Common with structured retryable, missing, wrong-type, ambiguous, contract, and stale-generation outcomes.
- Verify scoped registry inclusion or exclusion separately from global lookup and refuse malformed list evidence, same-UUID replacement references, and display-name fallback.
- Adopt the resolver for the Alchemy classifier, Auto Concept registries, spell-level capability upgrade, and Mentor progression views while invalidating retained results across lifecycle generations.

## Evidence strength foundation — 2026-07-18

- Add shared unresolved, inferred, runtime-observed, serialized-asset-verified, and statically-verified evidence levels with named source masks and contradiction handling.
- Require mutation-grade identity, native type, registry relationship, serialized mapping, and static contract evidence before Auto Concept or Mentor Alchemy accepts a classification.
- Expose structured level/source diagnostics and contract tests so game-update evidence changes fail closed and remain reviewable; display names never upgrade evidence.

## Shared lifecycle generation foundation — 2026-07-18

- Add one main-thread-safe Common lifecycle monitor with explicit no-game, initializing, playing, resetting, and scene-exit states plus monotonically increasing generations and bounded structured diagnostics.
- Coalesce equivalent lifecycle callbacks from independently installed suite plugins, expose generation leases for stale-work rejection, and keep progression-domain locks separate from global readiness.
- Move Automata 0.8.4, Mentor 0.3.4, and Mod Config 0.6.1 onto the shared scene, save-load, runtime-ready, reset/NG+, and registry-rebuild boundary.

## Orb Automata 0.8.3 Beta 1 — 2026-07-18

- Require authoritative before/after postconditions for Auto Buy queue additions, Auto Concept assignment changes, single/all spell leveling, and Auto Cast fire submission.
- Preserve structured feature, identity, expectation, before, after, outcome, and failure evidence for ambiguous mutations, then block repeat attempts until a scene, save-load, reset, or NG+ lifecycle recovery.
- Verify instant and sustained casts through the audited `Spell.Fire` hook instead of relying on transient casting flags.

## Orb Mentor 0.3.3 Beta 1 — 2026-07-18

- Verify every spell, artifact, and ordinary-alchemy native XP grant against authoritative before/after XP with the exact expected numeric delta.
- Cancel pending bonus work and block the affected domain for the lifecycle when a native grant is a no-op, partial, unexpectedly large, throwing, or unobservable.

## Orb Modding Common 0.3.2 Beta 1 — 2026-07-18

- Add the shared capture → execute → capture → verify contract and structured mutation evidence used by Automata and Mentor.
- Preserve after-state evidence even when a native invocation throws after partially changing state, and distinguish capture, execution, and postcondition failures.

## Orb Mentor 0.3.2 Beta 1 — 2026-07-18

- Exclude Scholar Concepts from Mentor's Alchemy catalog, mentor ranking, recipient relationships, XP capture, and final native grant path through the shared audited gameplay-domain classifier.
- Keep Harmony XP and progression callbacks cache-only; uncached recipes request bounded cooperative reconciliation without reflecting, allocating classification evidence, or guessing inside the hook.
- Invalidate classifier evidence on scene, save-load, reset, and NG+ transitions, and fail only Alchemy closed when ordinary-domain evidence is unknown or contradictory.

## Orb Mentor 0.3.1 Beta 1 — 2026-07-18

- Gate spell, artifact, and alchemy Mentor work independently on the exact native mastery and domain progression views while preserving the user's configured switches and percentages.
- Report locked progression as a non-error `M WAIT` state, activate within the bounded polling interval after unlock, and cancel stale catalog, relationship, plan, capture, and grant work across lifecycle transitions.
- Keep locked domains out of catalog discovery, mastery ranking, recipient planning, XP capture, equipped-spell inspection, and native grants; isolate a broken unlock contract to the affected domain.

## Orb Modding Common 0.3.1 Beta 1 — 2026-07-18

- Add one fail-closed, lifecycle-scoped classifier for ordinary Alchemy and Scholar Concepts using exact native types, stable UUIDs, the authoritative `ConceptRecipes` snapshot, and audited type identities.
- Make Auto Concept consume the shared classifier for catalog admission and final add/remove identity validation, removing its duplicated Scholar-type boundary.

## Orb Automata 0.8.2 Beta 1 — 2026-07-18

- Keep queue feeding responsive during rapid native completions: finish the current bounded completion-settlement generation, coalesce intervening signals into one follow-up, preserve CPU-sliced scan progress, and wake a prepared candidate immediately when a completion reopens a slot.
- Retain the 10 Hz full-queue poll only as a fallback when no completion signal arrives; every resumed candidate still refreshes native availability, costs, resources, reserves, limits, and queue room before mutation.
- Promote the deterministic four-frame completion storm to an active performance regression gate covering near-full queue depth, purchase count, candidate-evaluation amplification, and idle refill frames.
- Record deterministic queue output, refill latency, modeled reads, scheduler callbacks, and normalized operations per purchase; compare CI runs with the reviewed beta baseline and retain each raw report for 90 days.
- Add a source-level A/B compatibility runner that executes the same queue workload against untouched pre-beta `main` and current beta engines, reporting fairness and refill improvements separately from diagnostic validation work.

## Orb Automata 0.8.1 Beta 1 — 2026-07-18

- Treat completed Structures and Upgrades as typed progression signals: Structure completion immediately schedules a bounded Upgrade-registry refresh, Upgrade completion schedules the corresponding Structure refresh, and bursts coalesce without discarding conservative cross-candidate settlement.
- Add a deterministic headless E2E harness that drives the production Auto Buy engine through simulated native queue, economy, failure, and save/load boundaries; computer-controlled real-game checks remain UAT.
- Add operation-count performance simulations for large candidate sets and shared-queue saturation, plus a documented completion-storm target for the remaining scan-restart regression.
- Prevent one cheap Structure or Upgrade from monopolizing a large shared action queue: when several candidates are ready, queue one independently validated level from each ranked candidate before repeating the pass.
- Continue directly through the prepared ranking on consecutive frames instead of forcing a catalog rescan between candidates; a full queue retains the next candidate and feeds it into the first reopened slot.
- Let a lone eligible candidate still consume all usable queue room, with native availability, current cost, reserves, maximum level, and final purchase validation before every queued level.
- Centralize queue allocation in `OrbModding.Common.QueueCapacitySnapshot`, keeping authoritative native capacity/occupancy, live remaining room, Auto Buy's usage limit, and the manual reservation distinct and provenance-tagged.
- Refresh the complete queue-capacity snapshot after live cost/reserve validation immediately before every Auto Buy mutation; contradictory or missing native values now fail closed.
- Add portable regressions for a 200-level lone-candidate fill and fair Structure/Upgrade handoff across multiple ranked candidates without the idle evaluation interval.
- Validate the `0.8.1` build on a disposable high-resource Slot 3: the visible shared queue rose from `14/304` to `174/304` after five seconds and `302/304` after ten, while 1,797 successful purchases covered 166 distinct candidates with zero native failures.
- Park stable Structure reserve and affordability rejections below their exact ordinary-resource thresholds, ignore quantity-only ticks on already-satisfied dependencies, then wake immediately when a blocker crosses or conservatively when bandwidth, capacity, quality, effective cost, identity, availability, lifecycle, policy, queue, or completion state changes.
- Keep native-first Upgrade rejection handling conservative, suppress identical verbose rejection examples until their blocking signature changes, and describe a zero-reserve shortfall as insufficient cost coverage rather than a reserve violation.

## Orb Automata 0.8.0 Beta 1 — 2026-07-17

- Prepare Orb Automata 0.8.0 with typed Auto Buy rejection telemetry, structured multi-resource blockers, and separate scan-cap, rejection-transition, and native-mutation failure accounting.
- Keep unaffordable Upgrades subscribed to their decoded resource dependencies even when native `CanPurchase()` rejects them; installed IL confirms that contract includes affordability as well as lifecycle, requirements, and queue admission.
- Let the selected Structure or Upgrade fill the usable queue room while it remains safe, revalidating every level and ending the prepared group at the first failed live admission before reranking.
- Cache validated queue, queued-level, and multi-buy reflection metadata while re-fetching the live global multi-buy variable for every Upgrade level, and coalesce shared resource invalidation until the owned repeat group settles.
- Rate-limit repeated native purchase-failure examples per candidate while retaining aggregate attempts, successes, and failures.
- Retain the next ranked candidate while the native queue is full, so each reopened slot is fed without another catalog scan; if queue room remains after a repeat group, settle dirty state and rerank before mutating a different candidate.
- Validate the queue-feeding path against a disposable 13-resource `9e60` save: 150 native purchases completed with zero failures while candidate evaluations fell from 58,973 before the fix to 1,483 after it in comparable sustained runs.

## Orb Of Creation Mod Suite 0.3.0 Beta 1 — 2026-07-17

- Unify disabled-feature configuration across the supported suite. Orb Mod Config 0.6.0 supports multiple staged prerequisites and refreshes enum dependencies immediately; Automata and Mentor now lock inactive tuning while keeping mode, shortcut, status-button, safety, and diagnostic controls usable.
- Prepare Orb Mentor 0.3.0 and Orb Modding Common 0.3.0 for compound spell/artifact/alchemy and nested Auto Buy configuration dependencies.
- Prepare Orb Automata 0.7.0 with `TimedCycle` as the new default Auto Concept slot-management policy, keeping every assignment active for the full settled `TrainingPeriodSeconds` before rotating to its remembered compatible replacement. Existing saved selections remain unchanged.
- Add progression-aware Auto Spell Leveling under Auto Buy, with a separate `AutoLevelSpells` switch. It detects Locked, Single, and All capability without player mode changes, revalidates per-spell prerequisites and live costs, and switches to the game's native level-all action only after the exact Upgrade is completed.
- Prevent Auto Concept from repeatedly removing and re-adding the same concept when its required resource is at zero, which could keep training on one slot even after more slots were acquired. Every positive prospective drain now rejects an authoritative zero resource, replacements must pass one-instance admission before the current assignment is removed, and unsafe candidates no longer block the timed-cycle order.
- Anchor Auto Buy, Auto Cast, Auto Concept, and Mentor outside the native action queue with 12-pixel gaps; remove cloned native view gating, resolve the audited inactive Auto Buy hierarchy without `AutoBuyManager` reference matching, and use an extensible shared ordered-slot layout. The new `CN ON/OFF/!` button toggles Auto Concept directly.
- Rename Auto Concept's technical idle polling control to `FallbackEvaluationIntervalSeconds`, migrate both previous seconds and legacy minutes values, and show it only under Advanced. `TrainingPeriodSeconds` remains the normal gameplay rotation timer.
- Prepare Orb Automata 0.6.0 and Orb Mentor 0.2.0 as an intermediate local candidate before the current beta versions.
- Add Auto Concept `SlotManagementMode`: `RotateAll` replaces a settled active concept when a compatible discovered concept has strictly lower mastery, while `PreserveManual` retains the previous manual-baseline behavior. Rotation uses the verified native remove path, waits for settlement, and revalidates before adding; rate-limited no-change summaries make an idle balancer visible in diagnostics.
- Keep each newly assigned Auto Concept in a settled training session until it reaches the highest effective mastery captured at assignment time or `TrainingPeriodSeconds` elapses, whichever occurs first. The default period is 300 seconds; native setup/settlement time does not consume it.
- Present Auto Concept's mode consistently as `Disabled` or `Active`; active mode still performs mastery balancing.
- Read discovered concepts from the exact runtime `ConceptRecipes` list used by the native UI, migrate the rebalance interval from minutes to seconds with a 300-second default and 10-second minimum, timestamp all Orb Automata messages, and log permanent concept-contract failures once per lifecycle.
- Add disabled-by-default Auto Concept mastery balancing inside Orb Automata. It resolves only the UUID-scoped Scholar concept assets, fills compatible acquired slots breadth-first, batches depth to live native mastery limits, tracks manual and automated quantity separately, shares the suite scheduler, and rolls back only proven automated quantity when the native drain watchdog becomes unsafe.
- Validate every Auto Concept add with an exact prospective native drain vector, live resource-quality conversion, positive-rate reserve, finite-quantity floor, stable UUID/type checks, compatible slots, and final native quantity revalidation. Unknown contracts fail closed and ordinary alchemy remains untouched.
- Add Mentor's `EquippedSpells` source policy as the new default: every equipped spell may share with discovered spells strictly below that source's mastery. Keep `HighestDiscovered` selectable.
- Carry each Mentor event's exclusive source-mastery ceiling through recipient planning, pending consolidation, parking, and final native validation so lower equipped sources cannot grant to equal- or higher-mastery recipients.
- Bump Orb Automata to 0.5.2, Orb Mentor to 0.1.2, Orb Mod Config to 0.5.3, and Orb Modding Common to 0.2.1 for the combined full-charge, coordinator, and Auto Buy performance changes.
- Preserve CPU-sliced Structure repeat groups across coalesced native completion signals, while still settling broad completion effects before selecting the next ranked group.
- Move routine Auto Buy lifecycle probes to fixed 250 ms bounded slices so purchase frequency cannot multiply locked/active reflection work.
- Keep locked Structures out before cost or purchase checks, and park Upgrades rejected by native `CanPurchase()` outside high-frequency resource invalidation until a bounded lifecycle or completion retry.
- Add an opt-in `PrioritizeCostAndQualityStructures` Auto Buy policy. It ranks unlocked, affordable Structures first only when a one-time native effect preview proves a cost reduction or resource-quality increase; unknown effects keep normal cost ordering.
- Give continuously pending Auto Buy work a bounded three-turn coordinator weight before yielding to Mentor, Auto Cast, or UI work.
- Make `DecisionLogLevel=Off` suppress all operational Auto Buy and Auto Cast records, rate-limit summary logging, and reserve per-purchase messages for verbose diagnostics.
- Synchronize clean Mod Config fields with live changes made by native status buttons or shortcuts without overwriting staged edits.
- Add configurable Auto Cast support for charge-capable spells: fully charge through the native hold contract by default, or fire immediately when full charging is disabled.
- Retry Mod Config UI installation on slower Steam Deck/Proton scene startup instead of permanently giving up after one attempt.
- Repair Mod Config when its ScreenContent panel is destroyed even if the Mods button survives, restore a usable native view after failed open/close, and detach the old navigation listeners before reinstalling.
- Throttle missing native-control discovery to avoid scanning the complete Unity object registry every frame before autoqueue unlock.
- Cache Auto Buy's static candidate registry, cap its reflective CPU slice to 1 ms, and poll full queues at 10 Hz instead of every frame.
- Preserve Mentor XP and capture-time recipients when XP arrives during bounded relationship/reconciliation work; use constant-time indexed source exclusion, let active refreshes finish under sustained invalidation, cap and safely compact immutable evidence history, and retain unorderable captures without guessing their route until lifecycle/native-identity cancellation.
- Cache Mentor catalogs and native object lookups, stop repeated inactive-state cleanup, and lower its default grant and CPU budgets.
- Schedule Auto Buy and Auto Cast through the shared suite frame budget, with at most one native automation mutation per frame and resumable multi-level purchase groups.
- Schedule Mentor reconciliation, evidence resolution, planning, and exact native grants through that same frame coordinator; denied or incomplete cooperative work blocks stale grants for that domain, final recipient progression is revalidated inside the mutation lease, and transiently ineligible UUIDs park with exact XP until a later authoritative refresh without retry churn or head-of-line blocking. The parked ledger is bounded and fails the domain closed on overflow. AutoBuy plus Mentor can start only one native mutation in a Unity frame.
- Schedule Mod Config catalog discovery and logging, installation, repair, navigation-event maintenance, and slow integrity checks only when due through the shared cooperative budget.
- Revalidate deferred Auto Cast slots by stable recipe and native identity, and remove Upgrade automation from admission and ranking if native multi-buy restoration cannot be verified while Structures continue independently.

## Orb Of Creation Mod Suite 0.1.0 Beta 1 — 2026-07-15

- Added the supported suite package with Orb Automata, Orb Mentor, Orb Mod Config, and Orb Modding Common. Experimental plugins remain excluded.
- Bundled Orb Mod Config 0.5.1.
- Made the Mods configuration tab available from the start of a new save instead of requiring the NG+-gated Time tab.
- Kept Mods as the final item when native navigation tabs unlock or reorder.

- Default fresh Auto Cast configurations to a 0% resource-fullness threshold while retaining affordability and reserve checks.
- Write release ZIP entries with portable `/` separators and validate their layout for Linux, SteamOS, and Bazzite extraction.
- Add Orb Mod Config 0.5.0 with feature-oriented tabs, contextual labels, hidden compatibility switches, dependency-aware controls, apply indicators, and optional Steam Deck keyboard input.
- Add the Orb Mentor 0.1.0 spells-only MVP with native mastery grants, guarded recursion suppression, Shared Pool and Per Recipient economies, bounded frame processing, `Alt+M`, status control, live typed configuration, installed-game contracts, portable tests, and packaging support.
- Extend opt-in sharing to created artifacts and available alchemy recipes, using separate domain pools and native grant paths.
- Prevent continuously replenished artifact and alchemy batches from starving later recipients by preserving FIFO pending order.
- Add cohesive Mentor, Auto Buy, and Auto Cast status controls with native hover tooltips.
- Keep the logging probe development-only and fresh installations disabled.

All notable user-facing changes are documented here. The project follows semantic versioning per plugin while the suite remains in beta.

- Public-repository documentation, contribution guidance, CI, and release hygiene.

## Orb Automata 0.4.0

- Removed DryRun, runtime-probe, expert-override, per-session purchase-limit, and deprecated Auto Research settings from the release UI and generated configuration.
- Defaulted Auto Buy to Active and Auto Cast to Disabled.
- Added separate structure and upgrade affordability policies.
- Added optional action-multiplier handling capped to available queue room with per-level resource and reserve validation.
- Changed fresh-install reserves to zero so affordability modes are the default spending margin.
- Continued CPU-sliced scans and prepared queue batches every frame, removing the evaluation-interval gap while work is pending.
- Kept normal operational logs opt-in while retaining startup, warning, and error records.
- Isolated stub-linked test output from deployable Release binaries.

## Orb Automata 0.3.5 Beta 1 — 2026-07-14

- Published queue-aware Auto Buy for native structures and upgrades.
- Added Auto Cast rotation, resource thresholds, targeting, aura/channel handling, keyboard control, and a queue-adjacent status button.
- Included Orb Mod Config 0.4.0 and Orb Modding Common in the recommended archive.
