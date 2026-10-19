# Local runtime validation protocol

[Testing doctrine](README.md) · [Native contract workflow](native-contracts.md)

Portable and metadata tests cannot prove behavior inside Unity. Runtime
validation therefore proceeds in order; a later success never excuses an earlier
failure.

## Safety

- Close the game before copying, restoring, or hashing saves and installed DLLs.
- Never edit an active save; use a disposable save for mutation tests.
- Back up saves, configuration, plugins, and logs before active testing.
- Install only the supported suite and remove overlapping automation mods.
- Start with automation disabled and emergency stop engaged.
- Record commit/archive hash, suite and game versions, assembly hashes, BepInEx
  version, settings, save, and exact commands — the fields of
  [the report template](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/tests/runtime/report-template.md).

## V0 — repository and installed contract gate

Run [the gate](README.md#the-gate) against the audited game copy. Then build
the supported Release project against the same references and inspect the
package allowlist. Stop on any unexplained delta.

## V1 — boundary audit

For each changed native touch, confirm exact owner/member/overload/types, stable
UUID plus native type identity, main-thread ownership, lifecycle invalidation,
fresh preflights, mutation ordering, and verified before/after evidence. Unknown
or contradictory evidence refuses with its own reason and result code.

## V2 — load smoke

With emergency stop engaged, launch to Start, confirm one suite plugin load and
successful configuration/backup state, open Mods and Runtime, load a disposable
save, return to Start, close normally, and inspect the complete log. Duplicate
plugins, schema faults, backup blocks, Harmony faults, and UI construction errors
fail the gate.

## V3 — read-only surfaces

- Confirm configured intent and runtime health remain separate everywhere.
- Confirm lifecycle state settles across Start, load, scene, reset, and NG+.
- Confirm disabled features do not perform gameplay scans or mutations.
- Confirm exactly two closed suite buttons appear below the native help buttons:
  immediate STOP and disclosure. Opening disclosure creates the seven feature
  controls; closing it removes them.
- From Main's first playable boundary, both suite surfaces remain in loading
  while the six native icon sources appear, then become available together
  within one 100 ms observation interval. Absence may remain loading for 30
  seconds; a real mismatch or expiry follows the named three-attempt failure
  path instead of rendering substitute controls.
- Confirm the Mods rail, staged Apply/Revert/Default behavior, scroll position,
  external-edit conflicts, Runtime actions, tooltips, and keyboard navigation.
- Verify native frames remain pointer targets across row gaps and padding, while
  child decoration does not steal input.
- Verify a contained fault has both structural and color cues, and all genuine
  native capture failures name the failing contract in both log and Runtime.
- Confirm the temporary-item editor exposes only discovered exact-item approval,
  an explicit count, and removable unresolved entries; it has no raw, family,
  or bulk approval path and persists only through Apply.
- Confirm the activity timeline counts committed ordinary automation only,
  excludes World collection, Mentor and the Strategist, keeps quiet minutes visible, and marks
  faults without relying on color. Its processing summary remains text.
- Confirm the release build exposes no profiling-only action or player-facing
  debug capability.

Check supported resolution and UI-scale extremes. Portable geometry tests do not
prove Unity text measurement, clipping, raycasts, or native sprite identity.

## V4 — isolated active capabilities

Enable one capability at a time. For each, exercise a normal action, native-not-
ready state, insufficient resource or capacity, emergency stop, manual
interference, and lifecycle replacement. Verify the visible game effect, exact
postcondition, truthful waiting/refusal state, bounded log, and absence of stale
retries.

- **Auto Harvest:** fruit and treasure independently; exact one-action insertion,
  quantity, sibling isolation, and no planting or destructive plot behavior.
- **Auto Buy and Spell Leveling:** Structure/Upgrade separation, reserves, queue
  room, Bulk Development count, rising costs, Single/All capability, and
  queued-versus-completed unlock state.
- **Auto Cast:** resource deduction, charge hold/release, targets, manual pause,
  and audited fire evidence.
- **Auto Concept:** breadth, depth, rotation, training, manual preservation,
  drain rollback, mastery limits, and assignment settlement.
- **Auto Items:** Scroll, Relic, and explicitly approved temporary items;
  targeting/native-busy refusals, exact stock/queue/usage evidence, picker
  whitelist behavior, item-scoped quarantine of an unverified submission, and
  the pause and resume when the player eats an allowlisted item themselves.
- **Auto Scribe:** role selection, cost rank, carry limit, payment-last ordering,
  queue/instant completion, and lifecycle quarantine.
- **Mentor:** spells under both source-selection policies, artifacts, and
  ordinary alchemy independently; exact XP, recursion suppression, source
  ceiling, domain isolation, and persistence.

## V5 — persistence and rollback

Save and reload after verified actions. Exercise Apply/Revert and copied
configuration migrations. Close the game, restore original save/configuration/
plugin backups, verify hashes, remove the suite, and confirm the unmodded game
loads the save.

## V6 — combined suite

Enable representative features together. Confirm one shared runtime, fair action
turns, action-family conflict isolation, lifecycle cancellation, responsive Mods,
bounded logs, manual-action room, and an extended representative session. Use a
profile build only when scheduling or hot-path behavior changed; portable timing
does not replace observed desktop and Steam Deck/Proton evidence.

## V7 — release candidate

From the exact clean candidate, repeat V0, install the exact package into a clean
BepInEx profile, repeat affected V2–V6 gates, and inspect archive entries,
versions, hashes, logs, and backup restoration. Tagging, publishing, or installing
outside the authorized test profile requires explicit owner approval.

## Diagnostic captures

Bug-report bundles, performance profiles, the rolling decision journal, and the
recent-event ring are evidence tools, not gameplay dependencies. The release
button captures evidence already held and produces one zip no larger than 10 MiB;
it does not begin a session. Profiling builds still produce correlated full-trace
and profile sessions for `./script/trace`. Missing manifests, invalid checksums,
writer faults, dropped inputs, reveal failures, or gameplay changes are explicit
diagnostic-product outcomes and must not be inferred away.

### Pace capture

To compare automation paces, install the perf-debug build, load the save, and play
a few minutes at each pace, switching it in Mods > General > **Automation pace**
and pressing Apply. Quit the game normally so the session writes its manifest,
then run `./script/trace --full "<game>/BepInEx/config/OrbOfCreation-ModSuite/trace" report.md`. Its Pace view gives one row per
stretch with the collection rate, collection cost, frame rate, and pump cost.

### Garbage-collection capture

To see how much of the collector's work and the long hitches the suite causes, install the
perf-debug build and play normally. Before quitting, press **Stop profile** on Mods > Runtime so the
profile writes its manifest, then quit the game normally so the full trace writes its own. Run
`./script/trace --full "<game>/BepInEx/config/OrbOfCreation-ModSuite/trace" report.md` and read its
Garbage collection section. If it says the section is unavailable, the BepInEx log names why.
