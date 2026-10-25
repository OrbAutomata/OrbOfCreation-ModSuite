# Game MCP response format

<a id="the-bound-vocabulary-and-its-one-json-type"></a><a id="list-columns-are-a-declared-total-set"></a><a id="outside-a-table-absence-is-silence"></a><a id="shapes-that-must-not-regress"></a>

[Game MCP index](mcp-tools.md)

[The page renderer](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Automata/Runtime/GameMcp/GameMcpTextPage.cs),
[wire normalizer](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Automata/Runtime/GameMcp/GameMcpEntityWireNormalizer.cs), and
[document encoder](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Automata/Runtime/GameMcp/GameMcpDocumentJsonEncoder.cs) own the response layout.
The examples and terms below explain reading it; the code owns exact fields and formatting.

## How a response reads

A tool returns one plain-text page in MCP `content`. Inline media shares that content array; the
text follows it. No duplicate structured payload or output schema repeats the answer. Ordinary
facts use `key: value`; booleans read `yes` and `no`. A successfully served page omits a redundant
available verdict.

Rows are tables. The heading identifies the result count and shared facts before the column names.
A shared fact is stated once, and a row contains its own varying facts. A detail block stays a block
both alone and in a batch. A batch preserves request order without an echoed index.

## The cell vocabulary

A table header promises a column, so every row fills it. Non-applicable facts use words such as
`uncapped`, `unpriced`, `empty` or `unslotted`; unreadable facts identify the missing evidence rather
than using a plausible number. A missing display value reads `-`. The
[column owner](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Automata/Runtime/GameMcp/GameMcpListColumns.cs) and renderer define the complete vocabulary and column
sets; no second per-category field inventory lives here.

Entities are references with a readable name and addressable ID. A reference cell does not repeat
catalog-only metadata. An asset's internal name or native type belongs on the identity/detail read
where it helps identify the entity. A missing authored name does not fabricate a player-facing word.

## Numbers

Game magnitudes travel as strings and follow the game's screen rendering. Player-visible notation
rules belong in [numbers and rounding](../game-systems/numbers-and-rounding.md). The shared formatter
owns the mirrored implementation; the transport has no independent precision mode.

A displayed unmet price/holding pair must show the shortfall even when ordinary rounding would print
the same number for both. That pair can widen its precision enough to reveal the gap. Integer counts,
times, percentages and resource allowances keep their own meaning rather than becoming bare
large-number magnitudes.

## Presence semantics

A genuinely collected empty collection is evidence and remains distinguishable from an unreadable
collection. Unavailable data names its cause. Inside a table an absent value fills its declared cell;
outside a table a block with nothing applicable to say is omitted. A meaningful false verdict remains
present when false is the fact being answered.

A read row does not refuse a press nobody made. Its predicates use verdict words and explanations;
a failed read's unavailable envelope can still carry a failure class. `reason` sits immediately under
the code it explains. [Refusal vocabulary](mcp-refusal-vocabulary.md) owns how to interpret that class.

## Where a bound comes from

A native bound is the game's current limit on the control. An operational input bound limits the
request the server accepts. They are different facts: a schema's allowed input range must not be
presented as the game's capacity. Tool descriptors own input limits, and the canonical action owns
live native admission.

`maximumAmount` is what one call admits now, not a budget retained across calls. The sentence says
which fact limited it. A later call revalidates. Level controls report their live current value and
bounds; a response does not invent a native maximum from a schema constant.

## Inline action results

A committed action says what landed and returns the relevant settled post-state when available.
A purchase reports queued work rather than claiming completed levels. A cast's outcome is the
observable cast start or toggle stop, not a later cast-completion counter. A refusal or fault gives
one actionable reason and only the facts that explain it. Payment accounting, request echoes and
unchanged counters do not become general receipt sections.

Every capability verifies its own simplest outcome through its canonical GameAction. If native code
throws after the change lands, the outcome can still commit and the cause is logged under its
reference. If the requested transition cannot be established after a press, the action faults. The
[boundary doctrine](../runtime-architecture/game-boundary-doctrine.md#postconditions-one-simplest-sentinel)
owns that rule; [GameActionReason](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/GameActionReason.cs) and
[GameActionFaultLog](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/GameActionFaultLog.cs) own cause handling. Raw exception text is
not a wire reason.

The shared settlement path waits for a newer world and applies the capability's readiness predicate.
If settlement cannot supply a new post-state, a committed press remains committed and explicitly
reports unavailable post-state; it does not relabel the admission world as its result. A cast fire
can answer the facts its boundary sentinel already proved. Exact timing and request ownership belong
to [frame operations](../runtime-architecture/game-mcp-frame-operations.md).

## One terminal per request

A validated request not claimed before the admission deadline can be canceled atomically and never
executes. Once claimed, it waits for its real terminal; a client timeout must not precede an unowned
later mutation. No pending receipt substitutes for completion.

A domain refusal or fault is a completed tool invocation, with its status/class/sentence on the page.
Protocol/infrastructure errors before a canonical terminal use their protocol error path instead of
duplicating the domain result. The tool catalog and execution boundary own the exact distinction.

An operation pins one world internally. Callers do not supply world-generation ceremony to authorize
a press: the action revalidates live identity, lifecycle and native mutable facts.
