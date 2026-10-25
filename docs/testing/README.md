# Testing doctrine

[Native contract workflow](native-contracts.md) ·
[Runtime validation protocol](runtime-validation.md)

Tests are the executable specification. Prose earns its place by explaining how
to choose evidence, not by restating test classes or enumerating their cases.
When prose and a test disagree, inspect the product contract and fix the wrong
one; do not preserve both stories.

## The gate

Run these one after the other, never in parallel:

```bash
./script/test
OOC_GAME_DIR=/path/to/audited/game dotnet test \
  tests/OrbModding.GameContractTests/OrbModding.GameContractTests.csproj \
  --configuration Release
```

`./script/test` is the complete portable gate: the ordinary suite, the IL inspector tests, the
compile-time profile suite, and the profiled trace-tool build. It runs once under a 600-second
deadline that only catches a hung suite, and a red run is a real failure; `ORB_TEST_TIMEOUT_SECONDS`
overrides the deadline. Native-boundary changes also run the installed-game contracts, which read
PE metadata from the audited game copy without launching Unity. Without `OOC_GAME_DIR` they skip;
with an `OOC_GAME_DIR` that holds no game copy, every one fails. Compiler warnings are counted by
`tools/build-release-assets.sh`, which builds `src` against the committed references with warnings
as errors, as CI does. Reconcile every test, contract, manifest, entity, and warning count; an
unexplained delta stops the change.

## Evidence has boundaries

| Evidence | What it can prove | What it cannot prove |
|---|---|---|
| Portable tests against stubs | Policy, state transitions, ServiceCycle journeys, failure containment, and native-adapter behavior for the modeled contract | Installed metadata, Unity wiring, Harmony order, save behavior, layout, or real frame cost |
| Installed-game contracts | The admitted assembly pair and exact native types, members, signatures, visibility, inheritance, and source coverage | That a call has the intended runtime effect or that its postcondition is observable |
| Live runtime evidence | Actual wiring, mutations, persistence, player control, UI behavior, and performance on the recorded build/save/configuration | Broad deterministic regression coverage or behavior outside the observed scenario |

Collector fixtures use the shared game stubs through the shipping native-type resolver. A missing
contract overrides only the deliberately malformed type; a second complete fake game is not a
collector fixture. Stub identities keep their string setup form synchronized with the native GUID,
and glyph availability follows the game's discovered-or-prerequisite rule. A native alchemy
admission search is allowed to throw on a literal null list element; that is different from an
`AlchemyInstance(null)` capacity placeholder or the collector rejecting an unexpected row type.
Fixtures that author list rows rebuild the native count cache before testing admission; count
getters read that cache, and rebuilding it does not settle queued quantities. Admission accepts an already
held recipe before asking about free capacity; a zero maximum-add value is a separate usage limit.
Reader fixtures also exercise an unheld recipe with no free slots to pin actual admission refusal.

The ordinary suite resets the shared native model before its first fixture and after every case.
The reset discovers static registries, including closed generic list registries, and restores the
singleton and fault-switch defaults. It runs after each case because xUnit constructs the next
fixture before invoking a method's `Before` hook; resetting there would erase constructor setup.
Profile tests install the same cleanup only on the serial native-registry collection. Other serial
collections retain their explicit setup, and no global reset runs beneath parallel profile tests.
Fixture setup resets before constructing native objects and installing hooks. Within a case,
closing a targeting prompt or scene transition changes that state directly, preserving the other
native objects. A test that needs to remove an entity halfway through a scenario does so explicitly.

A claim stops at the edge of its evidence. A self-consistent stub that disagrees
with Unity is a defective model, not permission to weaken production behavior.
Installed metadata proves shape, not semantics. Live observation proves one
run, so preserve enough provenance to reproduce it.

The tests themselves show ownership and available scopes. While iterating, use
one generic selector and replace the token with the behavior or contract at
issue: `dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "FullyQualifiedName~<scope>"`.

## Design scenarios around authority

- Exercise the production engine through its real seams; a simulator that
  reimplements the decision algorithm can only agree with itself.
- Keep the simulated game authoritative for identity, availability, resources,
  cost, queue room, and mutation acceptance. Recreate native objects on
  lifecycle changes while retaining stable UUID plus expected type.
- Include manual or external interference wherever capacity, ownership, or
  freshness matters. Model exceptions, no-ops, partial results, and ambiguous
  postconditions, not only accepted actions.
- Cross a boundary deliberately: delayed callbacks go through the lifecycle
  kernel, mutation requests carry stable identities, and mixed-feature journeys
  assert uniqueness rather than relying on timing luck.
- Prefer several small journeys plus a bounded stress invariant over one large
  scenario whose failure explains nothing.
- Keep trace fixtures inside the versioned schema. Opaque payload bags, private
  save fields, and free-text log parsing create unreviewable second protocols.

## Waiting without starving the thing you wait for

A spinning wait competes with the work it is waiting for. `SpinWait.SpinUntil` and
`while (pump.PumpFrame(frame++).CapturesAttempted == 0) { }` both hold the calling core at full
tilt against the background worker whose progress they need: instrumented on a machine loaded to
eight busy cores, the wait for the capture after a faulted batch took 86 to 1908 frames, and 2
every time with a `Thread.Yield()` in the loop body. It also destroys evidence — every pumped frame
emits semantic trace events into the fixture's 256-entry ring, so a spinning wait overruns the ring
and a test asserting the *absence* of an event can no longer prove its drain was complete. Reproduce
by running the single test in isolation under `nproc - 2` busy-loop processes; beside its neighbours
it passes, because xunit's own thread churn supplies the yield the loop does not.

The shared `ServiceCycleTestDeadline` allows 30 seconds for a thread to satisfy a condition and
names the condition when it expires. Its polling waits sleep between checks; multi-step waits share
one budget. Completion APIs still receive a positive timeout when their contract requires one.
Temporary filesystem fixtures compose `TempDirectory`, which owns cleanup while allowing a test to
reserve a path without creating it.

A wall-clock deadline is a hang detector or it is a bug. Sized to how long the work takes today, it
makes every future slowdown look like a broken feature: one test allowed a background writer 500 ms
for a commit that takes about a second on a cold process, so it failed deterministically when run
alone and passed only beside neighbours that had warmed the runtime up. Give the deadline a name
that says "hang" and a ceiling nothing legitimate reaches; assert latency in an assertion instead,
where a change to it is visible.

## Turn traces into regressions

A trace is a hypothesis generator, not a test. Preserve the trace or recent-event
dump with the log, exact DLL hash, effective configuration, and audited game
identity. Reconstruct the causal sequence using stable entity identity, action
kind, receipt, planned and settled quantities, lifecycle/world/configuration
generations, and monotonic time.

First encode the smallest evaluator or boundary case that fails for the same
reason. Add a headless journey only when the defect crosses a receipt,
settlement, lifecycle edge, or multiple native actions. Add a deterministic
long-run invariant only when the claim concerns churn, fairness, retry bounds,
or total action count. The regression must fail against a faithful model before
the fix; if the relevant native fact cannot be modeled honestly, keep the claim
in runtime evidence instead of manufacturing certainty.
