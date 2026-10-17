# How a service gets its data

The canonical statement of what a ServiceCycle service is allowed to read, and where that data comes
from. If anything else in this repository disagrees with this document, this document is right and
the other thing is stale.

[Back to dossier](README.md)

## The rule

**A service receives exactly three objects: configuration, world state, and strategy.** All three are
immutable, published, generation-stamped snapshots. A service does not read the game to make a
decision. It reads those three.

The one exception is the **action boundary**, which is not a data source but a safety net: before
mutating the game, an action re-validates against live native state and refuses if the world moved.
That is not collection. Deciding uses snapshots; acting re-checks reality.

## The two kinds of service

### Source service — publishes one of the three

Reads the game (or the config file) on the main thread and hands back one immutable object that
everyone else consumes. There are three publications, and there will only ever be three:

| Publication | Owner | Lives in |
| --- | --- | --- |
| World state | the collection service | Common — it is the *game's* world, not any feature's |
| Configuration | the runtime, one installation for the whole suite | Common |
| Strategy | the strategist, an ordinary service (below) | Common — the `SuiteStrategy` type is neutral; the bulletin it carries is derived in `src/Strategist` |

A source service keeps a capture, because reading is its job. It is the only shape that has one:
`IServiceCycleSourceDefinition` declares `Capture`, and the ordinary contract it shares a main-thread
half with does not.

### Ordinary service — consumes all three

Everything else. No capture, no native reads at decision time, no per-service collection, and no
per-service idea of what the world is. Its cycle input is the pinned snapshots and nothing else — the
worker's `Evaluate` takes the configuration and the world as arguments, so there is no publisher to
read twice and no place to keep a capture even if a service wanted one.

The strategist is ordinary in shape: it reads the pinned world, derives the bulletin off-thread, and
its one action publishes that bulletin on the main thread. Only a world publication wakes it, so it
judges each world once and never wakes itself.

## World service flow

1. **Main thread** grabs all the raw data and stamps the frame with the pump frame it was collected
   on. Not the frame it publishes on — derivation takes frames, and a publish-time stamp would claim
   the snapshot was newer than an action it is missing.
2. Hands off to the **worker thread**.
3. Allocates a fresh world state. Nothing published is ever written again, so immutability is
   structural rather than a convention a reader has to trust.
4. **Does the expensive math** — every derived number any service needs, computed once, here, off the
   main thread. This is the point of the whole design: the game recomputes derived values from
   scratch on every call, so the suite owns that math instead of asking 400 times a cycle.
5. Publishes **one action** carrying the finished immutable snapshot and its generation.
6. The pump executes that action, which makes the snapshot the newest one. Publication-class actions
   dispatch before mutating ones, so a snapshot handed back this frame is live before any consumer
   decides anything that frame.

## Ordinary service flow

1. The pump asks whether this service may start: its own wake policy, **and** whether the world has
   advanced past its last game-facing action attempt. A commit is absent from the pinned snapshot; a
   skip, rejection, or fault proves the live action boundary disagreed with it. In every case the
   service waits for a later reading instead of planning again from facts just shown unreliable.
2. If it may start, it is handed immutable references to world, configuration and strategy.
3. The worker reads them freely. They are immutable, and anything newer is a *different object*, so
   there is no shared mutable state and nothing to synchronise.
4. The worker decides and emits actions.
5. The pump executes the actions on the main thread, each re-validating against the live game. Then
   back to 1.

## Generations, and what is retained

A generation is a `ulong` stamped on a publication so a consumer can ask "is this newer than what I
last acted on". It is **an identity tag, never a key into storage**.

`ServiceWorldPublisher` holds exactly one field: the latest publication. There is no history, no map,
no ring buffer, and no pool. A worker pins a reference for the duration of its cycle; once it drops
that reference and a newer publication exists, the old one is garbage. Live at any moment: the
latest, plus one per in-flight cycle. A pool is rejected — reuse would have to be proven safe against
every worker that might still hold a reference, which is the invariant nobody can check locally.

## What a service may not do

- Read the game to make a decision. That is what the world snapshot is for.
- Keep its own collection phase because "the snapshot does not have X". X goes in the snapshot.
- Hold a publisher at all. The runtime pins each publication once when the cycle opens and hands the
  snapshot over; a service that could read again mid-cycle would evaluate against one world and act
  against another.
- Carry state between cycles outside its own `TState`. The worker is stateless by contract; pacing
  comes from the wake policy.

The live action queue is published but never decided against. Services compete for the same slots and
consume them with their own actions, so a published reading would be wrong inside a single world
generation; the queue is read at the action boundary, immediately before the mutation that depends on
it ([W53](world-collection-decisions.md)).
