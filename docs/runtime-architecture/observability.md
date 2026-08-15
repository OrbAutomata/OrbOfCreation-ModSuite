# Service-cycle observability

The observation products, their artifacts and retention, and how to read a capture.

[Back to dossier](README.md) · [Service-cycle runtime](service-cycle-runtime.md)

## Four systems, four mandates

Per [the north star](../north-star.md), observability is four systems and each has one job. That
document wins wherever this one drifts from it.

1. **Bug-report bundle** — one release-safe Runtime action captures evidence the suite already holds,
   copies the configuration, identifiable save files, and redacted BepInEx log, then fills the remaining
   sharing budget with the newest decision-journal segments. It does not arm a recorder or ask the
   player to reproduce the problem.
2. **Decision log** — always on, high signal, low noise: lifecycle boundaries, strategy changes,
   configuration saves, emergency stops, service health transitions, the one-line-per-run
   announcement of the purchase-screen topology every purchase is admitted against, and one compact
   sentinel per attempted action rather than accounting summaries. The
   mandate is that the suite — BepInEx's own logs included — keeps at most ~100 MB on disk however long
   it runs unattended. The journal has a 64 MiB envelope and routine action success/no-op narration
   is absent; BepInEx still owns `LogOutput.log` retention, so the combined mandate is not a hard
   suite-enforced cap.
3. **Profiler and detailed trace** — debug builds only, compiled out of release composition. Measured
   spans and the correlated full semantic trace start together for developer captures. Extra cost and
   pre-allocation are acceptable here; trace overhead must never appear inside profiler spans. The
   detailed trace records three of its four intended streams; raw world capture remains an explicit
   [deferral](deferrals.md).
4. **Replay** — retired as a runtime system. Hand-crafted scenario fixtures serve its testing value
   without the runtime carrying a line of replay machinery.

The bundle coordinates explicit flush and snapshot ports but does not merge their runtime ownership.
Each underlying product keeps its own pool, queue, writer, format, retention policy, status, and failure
boundary. One product cannot consume another's buffers or backpressure its producer, and a compact
journal never claims to be a complete trace.

| Product | Producer behaviour | Storage behaviour | Disabled behaviour |
|---|---|---|---|
| Bug-report bundle | Player-requested snapshot of past evidence | One capped zip; old zips are retained | No background bundle writer |
| Decision journal | Always-on coalesced decisions | Capped rolling retention | Not normally disabled |
| Performance profile + full trace | Automatic profiling-build capture | Correlated explicit sessions | Absent from release composition |

## Sizing and transport

The release bundle is capped at 10 MiB after compression. Configuration, identifiable save files, and
the log are fixed inputs; the builder measures each compressed zip entry and admits newest journal
segments greedily until the remaining budget is exhausted. Journal selection never reaches beyond four
hours of the newest durable current-run record. If the fixed inputs and manifest cannot fit, the whole
bug-report request fails loudly rather than writing an oversized or misleading file.

Profiling full-trace volume is workload-dependent at schema v7's fixed 288-byte record. It stores every
accepted pump and semantic event and has no byte or time cutoff. Profiling builds start it automatically
so cold-start and sustained-cost evidence is complete. Completed sessions are retained as whole run
folders rather than thinned while live.

The shared transport is a single-producer block lane plus a format-owned writer, encoder, and storage
port — code reuse, not a shared sink. A product with facts from more than one thread owns one lane per
producing thread and merges those ordered lanes on its background writer; it never upgrades the hot
path to a contended multi-producer queue.

Block counts are throughput headroom, not retention — the full trace's ten reusable ~1 MiB blocks are
roughly nine minutes of pending data. Exhausting every empty block is an explicit storage failure
(`AcceptedAndBufferExhausted`, faulting at the next sequence, with the accepted record never retried),
not a reason to block gameplay or grow memory without bound. Backpressure, overwrite, or storage
failure stops only the affected session, commits explicit incomplete or gap evidence where possible,
and never changes gameplay. Unity never waits for diagnostics, telemetry, or I/O, and Orb Mod Config
only invokes each mode's neutral control port and renders status — it never owns a pump, worker,
exporter, or filesystem path.

## Mode 1: bug-report bundle

The Runtime page provides one **Create bug report** action. On the next main-thread tick it flushes
repeat-collapsed suite log messages, seals the decision journal's open coalesced spans, snapshots the
already-running recent-event ring, and returns. Later frames let the journal writer drain up to a
bounded wedge guard; zip measurement and file I/O run off the Unity thread. The result lands under
`BepInEx/config/OrbOfCreation-ModSuite/diagnostics/`. The timestamped file is retained until the user
deletes it. macOS reveals the file with `open -R`, Windows selects it in Explorer, and other platforms
open its containing folder. A reveal failure leaves the zip intact and shows its full path; a build
failure says that no shareable file exists and does not affect gameplay.

`manifest.txt` is first and states suite/game identity, enabled features, runtime health, included
members, actual journal window, and every dropped or unavailable fact. The configuration, every
identifiable top-level `.sav` candidate when no authoritative active-slot identity exists, and a bounded
BepInEx log tail follow. The ring snapshot follows when it fits, then journal segments appear newest
first. Textual inputs pass through one deterministic redactor before entry creation: known user roots
become `<user-path>`, other absolute paths become `<absolute-path>`, and configured usernames become
`<user>`. Save files and numeric binary formats remain byte-exact. Journal records contain value-typed
identity, timing, route, and outcome fields only; strings cannot ride in that format.

## Profiling companion full trace

The detailed trace is composed only with the profiling build and starts automatically beside the
performance profile. It retains every semantic service-cycle event and accepted pump summary; the
configuration and strategy publications a cycle pinned and the outcome of every action it dispatched,
under that cycle's identity; the feature's own numeric projection where one is written; session and
segment fences sufficient to validate order and cross-segment causal parents; and an explicit
completeness classification. It does **not** retain the raw world capture.

Schema v7 names all four generations a cycle pinned. The pump summary carries how many cycles the frame
started and how many services the world-freshness gate held: `0 started / 3 held` is a stalled
collector and `0 started / 0 held` is an idle suite, and the two are indistinguishable without both.
Capture and action facts also name the pump frame they ran inside, on the frame-identity offset the
record already reserved. That field is optional on those kinds, because the same facts can be emitted
from a host control transition between frames — an emergency stop rejecting live batches belongs to no
frame and says so by carrying none. Frame zero is legal, so absence is the field's absence and never a
zero value.

### World-collection spans

Collection is the suite's largest main-thread cost and the only capture whose cost is a distribution
rather than a number: one pass is sixty-odd readers, and the pass total says nothing about which of
them moved. A recording session therefore appends one `WorldCategoryCollected` record per category
per pass, carrying the category identity, what that pass spent on it, how many rows it sampled, and
how many categories the pass reported. The durations are the ones the collector already measures —
the wire converts them to the hundred-nanosecond ticks every other duration on it uses and adds no
second measurement, so a reused structural category charges the pass that read it and nothing to the
passes that reused it. That zero is the fact, not a gap.

The records are an appended kind on the existing wire rather than a second artifact: the segment
consumer requires contiguous semantic sequences, so anything sharing a session's segments has to come
from the one ring that allocates them, and every capture written before the kind existed still reads
without change. The pass width each span carries is the reconciliation denominator — a pass showing
fewer spans than the categories it reported is named in the reader rather than silently
under-counted, because a session that ended `Incomplete` truncates its last pass legitimately.

Emission is gated on a recording session exactly like every other record: with no session attached,
an observed pass returns before it builds anything, so the four-times-a-second path in an ordinary
build carries a null check and no allocation. The category names come from the collector itself,
written into the session roster as `world-category` rows, so a category added to the collector is
named by that alone and no second table can drift from it.

A recording is not the only way to read the distribution's newest point. The per-category cost of the
pass that produced the published world travels on that publication, and `trace_health` serves it —
so a session driving the game reads where a pass went without recording a trace, stopping to open a
dashboard, or being able to reach either. It is one pass: the fold over many is the recording's, and
this publishes what is already measured rather than growing a second accumulator to duplicate it.
`trace_health` names each category with the `world_categories` page name, because a reader holding a
cost line goes to that page next; the recording roster keeps the collector's own names, because a
record is read against the collector that wrote it.

### Artifacts

Format v1 publishes `segment-{ordinal}.oscs` files with a 96-byte header, at most 3,640 unchanged
schema-v7 records, and a 48-byte footer with exact terminal sequence fences and an IEEE CRC32, so each
full segment stays below 1 MiB. The final fixed 160-byte `manifest.oscm` records the two independent
session identities, topology, accepted and durable counts, committed bytes, timestamps, explicit
terminal reason, first missing transport and semantic sequences, and the permanent `DiagnosticOnly`
eligibility. **The manifest publishes only after all accepted blocks drain; its absence means an
interrupted session, never an inferred success.** The writer atomically claims a new `session-{id}`
directory, flushes each file under a temporary name, then publishes it with a no-overwrite rename.
Ordinals are dense, sessions are never resumed or pruned automatically, and initialization or
manifest-publication failure leaves the durable segments unmodified with no manifest — there is no
recovery path that fabricates terminal evidence.

**Completeness is about loss, not about which door the session left by.** A producer that stops
because the runtime is going away seals and publishes its partial block first, so the drain behind it
makes every accepted record durable; that session publishes `Complete` with no first-missing sequence
and reports its terminal reason as the shutdown it was. Equal accepted and durable counts alone do not
earn the word: a session that exhausted its buffers or its sequence space also ends with everything it
accepted on disk, and there the equality means the sink began refusing records, which is the
truncation `Incomplete` exists to report. Faulting a clean shutdown cost one 43-minute capture its
credibility — it read `Incomplete` at a first-missing sequence one past its own last record, a
contradiction only the offline tool could see and only arithmetic could dismiss.

**Generation-keyed publication stores.** The semantic stream says which generation a cycle decided
against, not what that generation held. A recording session writes `configuration-<generation>.oscv`
and `strategy-<generation>.oscv` beside its segments the first time it sees each generation, so three
services deciding on one configuration cost one payload — which is what makes the artifact
self-contained. Store files are UTF-8: a header line of `OSCV <version> <store> <generation-hex>`, then
sorted `path = value` lines. Text and reflected rather than a fixed-width codec, because these
publications are settings trees that grow with the suite and a hand-written codec would silently stop
recording what was added last; sorting means two generations diff to what actually changed. A failed
store write stops storing and does not stop the recording.

**The session roster.** A fixed record identifies a service by a number, and a number tells a reader
nothing, so a recording writes `roster.oscr` once, before the manifest seals the session — UTF-8, a
header line of `OSCR <version> <count>` and `<kind> <identity> <machine-id> = <display name>` rows.
Rows are kinded rather than assumed to be services, because the same question is coming for the
configuration and strategy publications — world-collection categories already use the second kind,
and their machine identity is a phrase with spaces in it, so that field takes whatever is left of the
row before the separator. A service with no display name keeps its registered identity
rather than being left out, so an unnamed feature reads as `orbautomata.auto-agromancy` — true, and
visibly missing a name — instead of "Service 4", which would look finished while saying nothing. A
roster that cannot be written or parsed costs the names and nothing else. The profiling trace and the
in-memory ring snapshot both carry it.

## The recent-event ring

The suite always holds its most recent semantic events — 8,192 of them, a few megabytes — in a fixed
ring attached to the pump at composition. It never writes to disk on its own. A bug-report request
snapshots its accepted prefix as the same validated `OSCS`/`OSCM` family used by the detailed trace and
places it under `recent-events/` in the zip. The bundle manifest names the event count and how many older
events the ring had overwritten. What the ring buys is that the events leading up to a problem exist
when a user notices it; the button captures the past instead of beginning a future recording.

Saved Game MCP screenshots in profiling builds are bounded before they reach the synchronous Unity
capture path: the active run admits at most two owned `mcp-*.png` files. It rejects the third request
before framebuffer capture and rejects an encoded image before file creation when the family would
cross its fixed 6 MiB envelope. Inspection or write failures are command faults, not silent drops.

The profiling trace has no elapsed-time or byte cutoff. It ends on runtime shutdown, storage or
backpressure failure, or semantic corruption; a session is never truncated and no completed session is
pruned in part.

**Retention** bounds the number of `trace/run-<timestamp>/` folders. Full trace and performance profile
write one per process launch; the always-on journal does not. Each launch prunes the oldest until at
most eight remain, counting the folder that launch may write. Folders go whole, because a surviving one
must still be the correlated full/profile pair the analysis tool requires, which pruning by file or
byte budget would destroy. The name is a fixed-width UTC timestamp, so oldest means oldest by name
rather than by a filesystem timestamp a copy would not preserve. A folder that cannot be deleted is
left for the next launch: retention never denies the suite its own recording.

At startup the suite also owns the retired stable `trace/full`, stable `trace/profile`, and
`replay/auto-harvest` layouts. It deletes only files whose exact retired path, extension, and four-byte
format magic agree, then removes empty directories and emits one aggregate line. Unrecognized entries
remain and make that line a warning.

Starting mid-game is valid for diagnosis but does not root the session at a known initial state, so the
manifest marks such a session `DiagnosticOnly`.

## Mode 2: compact service decision journal

The journal records worker and terminal meaning rather than frames. An action-bearing cycle writes one
fixed numeric record per attempted action: service and action ordinal, cycle and monotonic time,
candidate UUID, exact native type ID, list UUID, view UUID, route status, and one packed
disposition/result code. Actions without a native candidate use an explicit `NotApplicable` identity;
an attribution failure does not gate gameplay: the action executes, the record uses the distinct
`AttributionFailed` route, and one repeat-collapsible error line names the service and failure reason.
Native objects and rich strings never enter the journal.

Zero-action decisions retain one outcome kind/code and fault range. Consecutive equivalent decisions
coalesce into one span with first/last time, cycle range, and repeat count; action records never
coalesce. When a cycle has action records its ordinary aggregate terminal decision is omitted, because
each action sentinel already carries the authoritative result and duplicate batch accounting has no
consumer. A fault-bearing terminal remains as a fault-priority decision record beside its action.
Lifecycle, configuration, strategy, emergency, and world-gate transitions remain explicit records.

**Retention.** The fixed budget is 64 MiB. A maximum segment is 80 header bytes plus 128 fixed
80-byte records plus a 40-byte footer, or 10,360 bytes. Production derives a retained limit of 6,476
segments from that byte budget, leaving room for one maximum-sized temporary segment during atomic
commit and oldest-first eviction. Retained full segments occupy 67,091,360 bytes; the maximum write
transition occupies 67,101,720 bytes, below 67,108,864. Partial checkpoint segments only reduce that
total.

**Format.** Journal schema 3 uses one fixed 80-byte numeric record. Each `OSJD` segment has an 80-byte
envelope, at most 128 records, and a 40-byte `OSJF` footer with exact run/ordinal/sequence fences and an
IEEE CRC32 — a separate format and sink from `OSCS`. Decision records retain service ordinal,
lifecycle, cycle/time range, repeat count, one decision outcome, and failure occurrence range. Action
records spend their fixed key space on exact target/routing attribution and one postcondition-backed
outcome. Wake policy, projections, requested/committed/published counts, and native-call/mutation
ledgers are not computed for this artifact; deeper timing and accounting remain in the explicitly
armed semantic trace.

Configuration, strategy, lifecycle, world-gate, and emergency transitions use explicit record kinds
rather than pretending to be cycles. Lifecycle and world-gate transitions carry their service;
configuration, strategy, and emergency transitions carry none, because the thing that changed is the
suite's — and being suite-wide, such a record closes every open decision span before itself. Lifecycle
code `1` means requested and `2` activated. A service the world-freshness gate holds closed reaches the
journal as its own kind: code `1` means the live world was collected before the service's own last
action attempt, `2` that no source could answer. A hold is one record however long it lasts, so a hold
that never ends is one record whose missing successor is the stall itself; without it a stalled suite
would be an absence of evidence rather than evidence.

The journal claims `BepInEx/config/OrbOfCreation-ModSuite/trace/journal` once per process, deliberately
stable across launches rather than nested under a `run-<timestamp>/` folder: the rolling segment cap and
the restart reconciliation both govern one directory, so a per-launch directory would hand every launch
a fresh budget and leave reconciliation nothing to reconcile — which is why the correlated-capture
reader resolves the journal beside a run folder rather than inside it. A store this build cannot
continue is deleted, counted as discarded, and restarted at ordinal zero, because the directory
outlives the process and refusing it would leave the journal permanently dead on that machine; the
discarded count reaches the log once, loudly, and stays on the status card. A storage or observer fault
detaches the journal and leaves scheduling, mutation, and any separately owned semantic trace running.
There is deliberately no configuration toggle for the normal journal, and no restart or fallback path.

## Other owned output paths

Suite shutdown says once that it is stopping automation and leaving it stopped. The runtime engages
the emergency stop as it tears down, deliberately as a non-clearable shutdown episode so that a
resume cannot revive a disposed runtime, which leaves `EmergencyEntered` as the last event of every
recording with no `EmergencyCleared` behind it — indistinguishable, to a reader, from a suite that
died mid-run. The event has always carried its reason on the wire in its code field; the log now
carries it in words.

Every game lifecycle transition writes one line naming the epoch it produced, the kind of transition,
the scene, the source that reported it, and the frame. The log is where that reason has to land: the
suite invalidates every native reference on the epoch number alone, and the number is all the trace
can carry, since its records are numeric and a scene name and a source are strings. Without it,
naming the prestige behind one mid-session epoch change took a purchase-topology line, two
independent clock anchors, and a file timestamp. A field with no fact reads `unnamed` rather than
empty, so an absent fact cannot be mistaken for a broken line.

A full-trace session names itself in the log at both ends: one line when it starts, carrying the
session id and the run-relative path it is writing to, and one when it closes, carrying the records it
had taken. The closing line exists because shutdown is the one boundary no tick follows — the writer
publishes its manifest on its own thread afterwards and Unity does not wait for it — so a completeness
line alone left one 43-minute capture without a single word about itself anywhere in the log, and
pairing it to that log took two independent clock anchors and a file mtime.

Each completed Game MCP operation writes one ledger line naming the verb, the disposition, its own
duration, and the frame it finished on. The frame is what makes the line correlatable: pump and
capture records carry the same counter, so a line resolves to an exact trace offset rather than
needing a wall-clock anchor. The code appears beside the disposition only when it says something the
disposition does not, and the reason only when there is one; a refusal is written as a sentence and
the line does not double its full stop.

Every operation, not only the ones that mutate. A read drew an operation number and wrote no
completion, so the sequence had holes in it and what a read cost was answerable on no surface — one
session sized a two-hundred-id batch by watching the frame counter against a wall clock. A line for
an operation the frame answered itself carries two things a mutation's does not: a summary of what
was asked for — the category, the page, the filters, and the number of ids, never the ids
themselves — and how much came back, as rows when the answer is a page and bytes when it is text. An
answer that is one block claims no size rather than inventing one. Commands answered inside their
claiming frame are written here too and keep the mutation vocabulary; a command that leaves its
frame is written when it completes, so nothing is written twice.

The suite does not use `LogOutput.log` as an action ledger. Verified successes and ordinary preflight
no-actions emit no per-action line; the action journal and Runtime outcome projection own those facts.
A submitted mutation whose postcondition does not hold emits one warning. Lifecycle/startup/shutdown
messages remain, and an actual adapter failure or native refusal emits one actionable line with stable
identity and reason. Auto Buy's classified refusal responder owns the `NotAdmissible` line so narration
cannot duplicate it.

World collection announces what a pass managed when the answer changes, and the sampled population is
part of that answer: a healthy pass repeats only while its entity count stays within a tenth of the
last announced one. The band is measured against what was last spoken rather than bucketed against
fixed boundaries, so ordinary play drifts quietly and a prestige, save load, or vanished category
speaks immediately. Keying a healthy pass on completeness alone kept this line silent through a
session that went from 6,683 entities to 4,051.

Each pass is timed per category and the announce says where the time went: the total, the three
dearest categories by name with their milliseconds, and the remainder as one figure so the named
three are never read as the whole pass. A category read once per lifecycle epoch is charged to the
pass that read it and to no pass that skipped it, so a collection's cost is the sum of its
categories. The per-category figure travels on the published world too, beside the availability
evidence for the same category, because what a category cost is a fact about the collection that
produced the world. Cost stays out of the announce key: collection runs four times a second and no
two passes cost the same, so a key carrying it would announce every pass. The profile artifact still
times the pass as a single span — attribution within it belongs to the collector, because nothing
outside the reader loop can say which category the time went on.

The announce and the offline per-category view both stay, and they answer different questions. The
announce speaks when the population moves and says what that pass cost, which is what a player's log
can carry without becoming a stream; the dashboard speaks over a whole session and says what a
category costs across hundreds of passes, which is the only form in which a spike is visible at all.
Neither derives the other: three named categories in one pass cannot be averaged, and a session
average cannot say which pass was the expensive one.

Auto Buy affordability drift remains a loud refusal but does not synchronously render or write a
bundle. Structural contradictions that disable the feature retain a full text bundle under
`trace/diagnostics`, capped before each write at eight owned files and 1 MiB total. A collision,
oversized bundle, inspection failure, or retention failure leaves the refusal loud and names the
bundle as unavailable rather than overwriting evidence or faulting gameplay.

## Mode 3: opt-in performance profile

Profiling is finite, aggregate-first, and compile-time optional. Ordinary builds do not define the
profiling symbol, so probe call sites and their arguments are omitted: no branch, timestamp read,
counter, allocation, buffer construction, or writer startup. `EnableServiceCycleProfiler=true` is the
only profiling build switch and defines `SERVICE_CYCLE_PROFILE` before every project is evaluated. An
ordinary-build structural test inspects the compiled code and rejects profiler types, composition,
probe calls, buffers, worker startup, or profile-only timestamp reads — compile-time absence is
evidence, not a runtime no-op.

**Format.** Profile v1 is independent of the trace and journal. Each `OSPS` segment has a 128-byte
envelope, at most 4,096 fixed 144-byte numeric records, and a 40-byte `OSPF` footer with dense
session/ordinal/sequence fences and an IEEE CRC32. A terminal 160-byte `OSPM` manifest declares
calibration, build identity, trace/allocation flags, accepted and durable counts, the first missing
sequence, segment bytes, and complete or incomplete termination; no manifest means interrupted
evidence, never inferred complete. Sessions publish as `session-{id}` directories of dense
`segment-{ordinal}.osps` files under
`BepInEx/config/OrbOfCreation-ModSuite/trace/run-<timestamp>/profile/`, in that launch's own run folder
beside the full trace it correlates with.

Records are tagged aggregates or sparse samples, both retaining a neutral numeric stage code, service
ordinal, lifecycle temperature, raw tick range, allocation evidence, and the exact eight-counter
operation signature. Impossible summaries and unknown tags fail closed. When allocation probing is
unavailable the session flag says so, every encoded allocation total must be zero, and reports must
render `Unavailable` rather than measured zero.

**Stage codes** come from one enumeration, `ServiceCycleProfileSpan`, read by the runtime, the services
and the analysis tool alike. The numbers are wire values, so **a retired span's number is burned rather
than reused** and the blocks stay non-contiguous: 1–999 the suite runtime, 1000–1999 Auto Harvest,
2000–2999 Auto Buy. The frame's own phases are measured alongside the whole pump, and a frame
reconciles lifecycle twice, so that span is two occurrences per frame. Worker stages remain unmeasured:
the probe is owner-thread affine and a worker definition may hold no runtime-owned storage.

A span the enumeration marks as observer overhead — the three semantic-emission spans — is subtracted
from every enclosing span before it is recorded. The full trace emits from inside the frame, so without
that fence `Overall pump` would report the cost of recording the frame rather than the cost of the
frame, which is exactly the red herring the full-trace mandate forbids. The subtraction happens in the
measurement recorder, so the probe API is unchanged and no reader has to know to subtract.

Feature stages live on native adapters, where the main-thread cost is. Every capture snapshots one
temperature before binding and uses it for all its stages, so cold, `LifecycleRebind` and warm work are
never relabelled after a clock began.

This is developer tooling, so delivery favours one understandable path over defensive completeness.
Profiling must not alter gameplay, but the profiler itself may stop and report its first fault. It does
not retry a failed measurement, switch clocks or counters, recover an interrupted session, or accumulate
compatibility fallbacks.

## Reading a capture

`./script/trace` has four modes. `--full` and `--dashboard` resolve their input themselves: a
`full/session-<id>` directory, the `full/` folder holding it, the `run-<timestamp>/` folder that run
wrote, or the trace root holding the run folders. A root resolves to its newest run folder and says on
stderr which one it read and which it skipped; a folder holding two full-trace sessions is an error that
lists them. `--journal` and `--performance` take their own directory directly, because neither is
reachable from a full-trace session.

- **`--full <capture> [report.md]`** validates the manual session and emits one report with separate
  service, pump, and worker/service semantic views. It verifies every segment envelope and checksum,
  dense transport and semantic order, topology, exact cross-segment parent identity and timestamp
  ordering, and the terminal manifest fences, holding at most one bounded segment at a time so memory
  does not limit session duration. A missing manifest is reported as `Interrupted` over the validated
  durable prefix and never promoted to complete. Names come from the session roster when the capture
  wrote one; a capture without one is reported under its numbers rather than having names inferred. A
  final view folds the collection spans into one row per category — passes, total, average, median,
  worst, and the sampled counts that explain a change in cost — sorted by total so the first row read
  is the one worth attacking. The spans stay out of the event timeline they would otherwise be, since
  the aggregate is the form they answer in.
- **`--journal <journal-directory> [report.md]`** selects an explicit third decoder route and never
  sniffs or falls through to the OSCS parser. Persistent ordinals must be contiguous; record sequences
  must be contiguous within a run; every adjacent later run begins at sequence one; and a run identity
  cannot reappear after another run. A nonzero first ordinal is reported as absent retained history, not
  corruption; an interior hole fails closed, because production retention removes only the oldest
  prefix. The report explicitly says OSJD has no terminal manifest, cross-run clock, wall time,
  pump/frame timing, physical worker scheduling, service names, or projection schema.
- **`--performance <session-directory> [report.md]`** validates the manifest and dense segment lineage
  and renders stage, service, temperature, count, average/minimum/maximum microseconds, allocation, and
  operation signatures, reading aggregates for totals and keeping sparse samples in the binary session.
- **`--dashboard <capture> <dashboard.html>`** is the correlated offline projection. It runs the same
  strict readers, selects the newest retained journal run, clips its decision spans to the full-trace
  window, and calibrates profile raw timestamps onto the Common monotonic clock, writing one JSON
  dataset and an HTML viewer. Correlation stays a presentation concern: the three formats, writers,
  terminal states, and failure boundaries remain independent runtime products. A cycle is identified
  by service, lifecycle, and cycle id together, because cycle ids restart at one in every lifecycle
  and the pair alone let a later lifecycle overwrite an earlier one row for row. Every cycle the
  trace says started is reconciled against the rows that kept a start, and a shortfall fails the read
  rather than rendering a table a quarter short. The page carries the same per-category collection
  view, and it is whole-trace rather than clipped to the window the other panels use: a structural
  category is charged by a handful of passes in a session, and a window narrow enough to be
  interesting would show it as free. A pass carrying fewer spans than the categories it reported is
  named rather than quietly under-counted, in both readers — a session that ended incomplete
  truncates its last pass legitimately, and more than one short pass is records lost.

Each pump row carries the frame's own wall time beside the suite's cost inside it, differenced from
the previous pump record. That figure is the denominator of every honest statement about what the
suite costs — duty cycle, what share of a collection frame is ours, and whether an expensive capture
landed in a frame that was already slow — and recovering it by hand from consecutive offsets is work
two separate analyses each did. The first pump of a session has no predecessor and carries no ambient
time rather than a zero.

The viewer is organised by service rather than by phase: an overview page spends the pump frame as a
stacked bar per frame — response, capture, action, and whatever the pump measured but did not attribute
— then one page per service, each spending a cycle as capture, handoff, derive, project, and dispatch
from the wire's own timestamps. Derive is labelled *math + allocation*, because no seam exists between
them. A final evidence page keeps the semantic lanes, decision projections, stage aggregates, and the
retained profile samples.

Every pump frame and cycle is classified cold-process, lifecycle-rebind, or warm, and the viewer
defaults to warm only: a first pass carries the JIT and the first touch of every buffer behind it, so
leaving it in an aggregate misreports the steady state by an order of magnitude while excluding it
silently would hide a real cost. Excluded frames are counted and totalled above the charts and one
toggle puts them back. Above roughly 1,500 frames in range the pump chart switches to equal-time
buckets of means and says so. The charting library is vendored and inlined rather than fetched from a
CDN — a dashboard is a file attached to an issue and opened days later on a machine that may have no
network, and a page that renders empty offline is not evidence.

The full trace is the only required evidence. The profile session and the decision journal are optional,
and their absence is a fact about the capture rather than an error — a release build has no `profile/`
session at all. A missing product leaves its panes empty and adds a banner, so the reader is told what
is not there instead of being refused a dashboard.

**What a profile's numbers may and may not be used for.** They are diagnostic elapsed time, not an
uncontaminated CPU or allocation claim, and a profile recorded with the semantic trace active is
measuring both. Aggregate maxima do not retain frame identity, so one wall-clock outlier is not
attributed to game work without a matching sampled record or an external profiler. A trace-off profile
is required before any of it becomes a performance acceptance gate.

## In-game surfaces

Common exposes one owner-thread rolling action-outcome projection for the Runtime page, consuming the
same assembled evidence as the journal before storage coalescing, so it retains exact planned,
committed, skipped, rejected, and faulted totals plus the latest real boundary reason per registered
service. It adds no feature bookkeeping, native read, second service poll, storage path, or disk I/O,
and remains available if journal storage cannot initialize. Its fixed 30-minute action timeline charts
committed actions and fault presence only — planned, skipped, rejected, and waiting evidence never
becomes charted work — and `Source` infrastructure is excluded by typed shape, never by a display-name
match. The Runtime page reduces the pump-timing projection to one average/worst line; full performance
analysis is the offline dashboard.
