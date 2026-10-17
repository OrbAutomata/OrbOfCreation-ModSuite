# Automata services

This folder contains the remaining application-level service-cycle lifecycle contract. Feature
implementations live in sibling feature folders under `src/`.

## Registration rules

- `Plugin` owns the one ServiceCycle activation directly; feature order is explicit inside
  `IAutomataServiceCycleFeature[]`: world collection first, then Auto Items, Auto Scribe, Auto
  Harvest, Auto Buy, Spell Leveling, Auto Cast, Auto Concept, Mentor, and the Strategist.
- Registration is explicit. Do not discover services through reflection, filesystem conventions, or static constructors.
- The activation is the application lifecycle surface; feature-specific APIs stay inside their typed
  ServiceCycle composition and diagnostics bridges.
- The Common `ServiceCycleRegistry` owns typed feature registration and deterministic action order.
  Changing that order is a runtime behavior change and requires focused ordering tests.
- New services need a cohesive feature folder under `src/`, an explicit typed registration site, and
  tests that prove their position in the cycle's ordering contract.

## Auto Harvest module

`src/AutoHarvest` is split by dependency direction:

- `Policy` owns stable pair identity, immutable pair facts and decisions, and fail-closed eligibility.
- `Native` owns immutable reflected contracts, lifecycle-bound bindings, live state reads, and verified native mutation behind an opaque token.
- `Runtime/ServiceCycle` owns immutable cycle state, worker evaluation,
  owner-thread adapters, scheduler admission, feature diagnostics projection,
  and native lifecycle hooks.
- `Diagnostics` owns stable health projection and typed runtime snapshots.

The neutral Automata ServiceCycle host owns the reusable service definition,
pump composition, configuration publication, and shutdown. Do not add feature-named copies of those facilities.

Keep the `OrbAutomata` namespace until a deliberate API migration is planned; folders describe ownership without changing existing type identity.
