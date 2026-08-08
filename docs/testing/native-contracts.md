# Native contract workflow

[Testing doctrine](README.md) ·
[Reverse-engineering audit](../reverse-engineering/audited-build.md)

[`data/native-contracts.json`](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/data/native-contracts.json) is the audited
compatibility boundary for reflection and Harmony. It admits complete
`Assembly-CSharp.dll`/`Assembly-CSharp-firstpass.dll` hash pairs and records each
target's exact metadata, owner, use, source tokens, and place: `capture`,
`action`, `patch`, or `mirrored`.

`usages` says how the suite depends on the member. `reflection`, `harmony`, and
`direct` all touch it. **`mirrored` does not**: it is a member-shape dependency
the suite relies on *without* reaching for the member — a suite constant whose
value is only correct because of what that member holds. The published ritual
starting-level floor and both casting-dial floors are the literal `1` because
`UIValueSelectButton.SetClamp` stores it in `minValue` and the control's
decrement never goes below it. Nothing reflects on either member, so before
`mirrored` existed the dependency could only be written as a comment — and a
comment does not fail when a game update changes the member's shape. A mirrored
row is deliberately excluded from the source audit's declared-target set: it
says nobody selects this, so it must not let a selector elsewhere pass
unaudited, and a test fails if any audited source does select it.

The first three places are where the suite touches the member. `mirrored` is the
place a row sits in when it touches it nowhere, and the two agree by test:
mirrored-only usages means `place: "mirrored"`, and any touching usage means one
of the other three. A row that copies a value pays no capture cost, so it
carries no `capture` block — filing twenty-six of them under `capture` put
twenty-six per-pass readings that never happen into the very census the
discipline exists to make trustworthy. A row that both mirrors and touches keeps
the touching place: its mirrored half is a second obligation, not a second place.

What a mirrored row cannot prove on its own is the **value**. The manifest proves
the member still exists with that shape, and the shape is not what a copied
constant depends on — so each mirrored value is pinned by a contract test that
reads the number out of the audited assembly: the harvest discriminants by their
enum ordinals, and `PlotNodeActionInstance.GetMaximumInstances` by the literal
its body returns.

The manifest proves native shape, not runtime behavior. Adapters still resolve
and validate their complete binding sets and fail closed. The source audit asks
whether every literal selector is declared somewhere; it intentionally does not
couple contracts to source-file paths. Exact-path exemptions are reserved for
generic framework or UI-navigation reflection with a reason, never mixed
gameplay adapters.

The ledger records the grab set, not the math. A suite formula that reproduces a
game formula is proved by the in-game parity checker, which runs both answers
against the live game; a manifest row would claim metadata proves a computation,
which it cannot. Formulas therefore stay out of this file — the members they read
are declared, the arithmetic over them is not.

## The capture block

Shape is not cost. A per-pass sweep over the whole registry and a single field
load looked identical in this manifest, which is how both came to live in
capture unremarked. Every `place: "capture"` contract — every row the suite
actually reads during collection — therefore also answers six questions, in a
`capture` block:

- **`class`** — `grab` reads a stored value, `computes` makes the game run a
  formula, `composite` makes it build an aggregate, `enumerating` makes it walk
  a collection.
- **`cadence`** — `per-pass` (four times a second), `per-epoch` (once per
  lifecycle), or `request-time` (only when something asks).
- **`justification`** — why the suite takes the reading rather than deriving it.
- **`evidence`** — what the classification rests on: a quoted IL body, or the
  signature alone. A method classified from its signature says so, which is how
  the un-audited ones stay visible.
- **`derivable`** — whether the suite could answer this from facts it already
  publishes. True is debt, and it is counted.
- **`sideEffects`** — what the game writes while answering. Empty is the only
  acceptable answer at `per-pass`.

`captureRoots` names the namespaces whose reflection is capture;
`captureStructuralReaders` names the collector fields it runs once per epoch. A
further test decides membership before any of them run: a mirrored-only row sits
at `mirrored` and a touched row never does, so nothing enters the census by
choosing its own place. Four tests then hold the boundary to this:

1. every capture contract declares all six, and nothing else declares any;
2. every native member a capture root selects is named by a capture contract,
   with the dual-place selectors pinned in a list reconciled as an exact set, so
   it only shrinks by test rather than by intention;
3. no `per-pass` capture contract writes, except the nineteen pinned in a list
   that names each one — and each of those must claim `derivable`;
4. the manifest's epoch-scoped reader list is the collector's own marking.

The second walk under-reports and says so: it sees the reflection APIs and the
world binder's helpers, not a feature binding's private `Method(...)` wrapper.
Splitting `LoadoutNativeBindings` into its reading and mutating halves is what
lets it see that file.

It also reconciles by **member name**, so a member already carrying one capture
row satisfies it from every other site selecting that member. That is how
`ResourceCostList.HasEnough()` came to be recorded three times while nine readers
called it. Closing a gap of that kind takes a walk of call sites rather than of
names, and it is why rule 3's list is allowed to grow when someone walks them: a
list that could only shrink would price honesty as a regression. What may never
grow is the set of writes themselves.

**A reading leaves capture only after the replacing suite math has a parity pass
proving it.** Port, prove, then delete — never the other order. Until then it is
a `derivable: true` row, which is a debt that can be counted rather than an
intention that cannot.

## Add or change a native target

1. Inspect the installed assembly and record the exact declaring type, member
   kind, overload, visibility, staticness, return/value type, inheritance, and
   ordered parameters.
2. Change the manifest with the source. Record all owners, how the suite depends
   on the member, boundary place, and every literal source token. A mirrored row
   has no source token by definition; its `owners` name the suite value that
   copies the member instead, and its place is `mirrored` with no `capture`
   block, because nothing reads it on any pass.
3. Use an exemption only when the selector is deliberately framework-generic;
   keep it to one exact path and explain why it is not gameplay authority.
4. Run the contract project without `OOC_GAME_DIR` to prove schema and source
   coverage: `dotnet test tests/OrbModding.GameContractTests/OrbModding.GameContractTests.csproj -p:UseGameStubs=true`.
5. Point `OOC_GAME_DIR` at the audited installation and run the same project to
   verify the admitted pair and every metadata contract. Build production
   projects against those references when their generated bindings changed.
6. Use the runtime protocol for lifecycle safety and native side effects;
   metadata cannot prove either.

## Audit a game update

Treat the update as a reviewed manifest diff:

1. Add one complete platform pair with audit date, build description, and
   platform-relative provenance. Never admit independent hashes that can form an
   untested mixed pair.
2. Run the installed audit even when the hash is unknown so identity and all
   structural differences appear together.
3. Update changed signatures in place, add genuinely new targets, and remove
   contracts nothing depends on any more. "No selector names it" is not that
   test: a mirrored row is named by no selector on the day it is written, and
   deleting it would silently retire the only check on a value the suite
   publishes. Remove one only when the suite value that mirrors the member is
   gone too. Reconcile source exemptions and boundary places in both directions.
4. Compile against the candidate references, then validate affected behavior in
   the game. Hash acceptance does not replace adapter validation or verified
   postconditions.
5. Keep unknown complete pairs in compatibility quarantine. An incomplete or
   undiscoverable pair remains a total refusal; explicit acceptance of one
   unknown pair does not generalize to another.
