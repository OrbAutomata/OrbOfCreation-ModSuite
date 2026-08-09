# Game MCP tooling

An engineering surface, not a player feature. The suite serves a localhost MCP endpoint from inside
Orb Of Creation when, and only when, it was built with the `perf-debug` profile. Ordinary and
release builds compile the server and every MCP gadget out, so nothing described here is reachable
from a published release.

## Connect

Install a `perf-debug` build with the game closed (see [development setup](setup.md)), then launch
the game through Steam:

```sh
./script/install perf-debug
```

The endpoint starts on the title screen, before a run is loaded:

```text
http://127.0.0.1:19106/mcp
```

A general MCP client can use:

```json
{
  "mcpServers": {
    "orb-of-creation": {
      "type": "http",
      "url": "http://127.0.0.1:19106/mcp"
    }
  }
}
```

The transport accepts streamable-HTTP JSON-RPC POSTs, negotiates MCP versions
`2025-11-25`, `2025-06-18`, and `2025-03-26`, and returns one JSON response per request. Clients
advertise `application/json, text/event-stream`, send `Content-Type: application/json`, and send the
negotiated `MCP-Protocol-Version` after `initialize`. Notifications return HTTP 202 with no body.
The listener is fixed to IPv4 loopback and rejects non-loopback origins.

The Start screen displays a large native-styled ModSuite status card directly beneath the game's
version number in both build modes. Its headline names `PERF-DEBUG · MCP READY` or
`RELEASE BUILD · MCP OFF`; the card also shows audit health, loopback endpoint, suite version, and
process ID. No card means that process did not load ModSuite; differing PIDs expose duplicate game
instances. Red means the control plane failed, amber names an intentional or compatibility-limited
state, and green means the perf-debug agent endpoint and audited game are ready. In perf-debug, an
agent can attach immediately and call `game_continue`; tools that need the Main scene or a
published WORLD reject with an exact not-ready reason until the run loads.

The dependency-free test client performs the handshake automatically:

```sh
tools/game-mcp-client.py doctor
tools/game-mcp-client.py continue
tools/game-mcp-client.py tools
tools/game-mcp-client.py call world_overview
tools/game-mcp-client.py measure-reads
```

`--transcript PATH` records exact request and response JSONL. Evidence belongs under ignored
`artifacts/`; it must not be force-added to Git.

## Architecture and safety

HTTP workers never read Unity objects or suite publications. Every stateful tool submits one
immutable operation to the next Unity-frame boundary. After the ServiceCycle pump, that boundary
atomically claims every pending request, pins one immutable world/configuration context by reference,
and executes the complete claim in submission order. There is no MCP shadow world, timer, polling
loop, priority queue, four-command throttle, or listener-lifetime gameplay lease. With no requests,
Unity performs only the inbox empty check.

Published world facts are preferred. Because this is a localhost-only debugger, a parameterized
tooltip, inspected panel, screen catalog, fixed probe, or framebuffer read may instead run only in
the exact requesting operation on Unity's main thread. Direct queries never mutate gameplay, never
return a pending receipt, and never introduce polling. UI changes are classified separately from
gameplay. A missing or partially collected read returns `unavailable` with an exact `reasonCode`
and reason.

World reads pin one immutable publication for the whole answer and return status/refusal evidence
plus the requested data. They do not expose world/lifecycle/configuration generations, request
fields, capture/respond timestamps, or mailbox internals. Gameplay
operations re-resolve UUID/native type and invoke the same canonical GameAction as features/tests,
including current lifecycle/configuration/emergency/ownership/native admission and one observable
outcome sentinel. Save deletion, save import/export, run reset, arbitrary clicks, arbitrary keys,
caller-supplied reflection/native invocation, and progression unlocking are absent. The complete
frame and data-lifetime contract is in
[Game MCP frame operations](../runtime-architecture/game-mcp-frame-operations.md).

The verb surface is also bounded by what the player can actually click in the pinned build, not by
what compiles. Verbs the shipped UI does not expose are absent even when a reachable native entry
point exists: `game_concept` offers only `add` and `remove_owned`, because rotating one assignment
out for another is automation policy rather than a control; `game_targeting` offers no cancel,
because the visible Close button only dismisses presentation; there is no in-place augment editor
and no way to select a discovery output by UUID; and `game_spell_level` requires
`uuid` for `single` while rejecting it for `all`, because the native Level All button
takes no target.

Every game-domain `BigDouble` is one JSON string produced by the shared MCP number formatter, never
a JSON number or a text/mantissa/exponent object. Zero is `"0"`. The formatter follows the screen:
ordinary player-scale values are plain with at most two decimals (`"26"`, `"2.2"`), while large or
small magnitudes use a normalized mantissa and lowercase `e` exponent without a plus sign
(`"1.66e8"`, `"1.23e-3"`). There is one formatter and no precision or verbosity option.

The game aggressively caches some derived values until their screen has been viewed. That upstream
behavior is not silently worked around here. If a stale cache prevents a native action, the
terminal rejection must name the stale cached fact and the screen-view condition; the MCP server
does not refresh it by hidden navigation.

## How a response reads

This surface is read, not piped. Its callers are agents, and an agent reads an answer the way a
player reads a screen — so every tool answers with one page of plain text in MCP `content`, and
nothing beside it. No tool declares an `outputSchema` and no result carries `structuredContent`: one
answer, said once. Images still ride the same `content` array; the text page follows them.

Layout is decided in one place, after the response document is finished, so every tool inherits the
same idiom and no producer invents its own formatting.

- **One fact per line**, `key: value`. `true`/`false` read as `yes`/`no`. A page that answered at
  all is available, so a bare `status: available` is not said.
- **Rows are a table.** A list of same-shaped rows says its keys once in a header and each row on one
  line: `rows 56/180 next=56; all queuedLevels=0  [id name level]` then `006061be Constitution 2259`.
  `total` and `nextOffset` live in that header, never on a row. Any column holding one value across
  the whole page moves into the header behind `all` — a value repeated on every line is a page fact
  wearing a row's clothes. Columns are separated by a single space, or by ` | ` when any cell on the
  page contains one.
- **A refusal is one line**: `refused (ERR_NOT_FOUND): The spell Beam Burst you tried to cancel is
  not currently active.` A decision block reads the same way, verdict first and sentence last:
  `equip: no (ERR_LIMIT) maximumAmount=0: Every slot in this loadout is in use.`
- **A value beside its ceiling is `43/45`**, the way the screen shows it.
- **An envelope is not a fact.** One object wrapped in one key that names nothing the caller asked
  about is unwrapped, so an entity appears once per response.
- **Constants live here, not in answers.** A number that is the same in every save on every call is
  documentation. The casting dials publish `current` and `maximum` and not the floor, which is 1 for
  both; `suite_configuration` publishes values and not the sentence describing each setting.

### Entity handles

The wire says an entity id as a **handle**: the shortest prefix that is unique across every
published id of the pinned build, floored at six characters (`006061be` → `006061`). One helper
formats every id at every emission site, so every handle on the surface is the same width. Identity
inside the suite is still the whole canonical UUID; this is what an id *looks like*, not what it is.

The length is a property of the id set, and the suite pins the game build, so it is a compile-time
constant with no runtime recompute. A portable test recounts it against `data/entity-mappings.tsv`,
so a build whose published id set moved fails the gate rather than shipping a colliding handle.

Every id argument accepts the whole canonical UUID **or** any prefix that names exactly one
published id — the handle a response just printed is always one of those. A prefix that matches
several answers `ERR_INPUT` and names the ids it matched; nothing is guessed.

An entity is its name and its handle wherever it appears: `Constitution 006061`. An id the catalog
cannot name renders as `(unnamed 2c20e7)` — marked, never a bare id that reads like a row whose name
happens to be hex.

## Tool surface

The registry is exactly 42 tools. It is built once per lifecycle and never changes mid-session, so
there is no `tools/list_changed` notification. The rows below are in `tools/list` order.

A tool's prefix names the screen it acts on: `world_` reads the published world, `suite_` acts on
the mod suite, `time_` acts on the Time tab, and `game_` is everything else the player screen owns.
The one exception is `game_level`, which buys levels from any ordinary level list — including Time
Runes, which live on the Time tab — because a caller reaches it from the entity being levelled
rather than from the screen it is drawn on.

| Tool | Purpose |
|---|---|
| `world_overview` | Compact collection, economy, progression, and running-state summary |
| `world_categories` | Discover every published table and exact collection availability |
| `world_list` | Page compact identity-plus-scan rows in one category |
| `world_get` | Read an ordered 1–200 UUID list from one pinned immutable publication; optional native-type assertion |
| `entity_catalog` | Search every live-registry identity and available player-facing name, including loaded entities hidden by progression |
| `explain_entity` | Evaluate one UUID's gates, requirement graph, exact costs, and blockers from one pinned immutable publication |
| `world_search` | Search stable-UUID entity categories; composite diagnostic rows are excluded |
| `suite_health` | One compact runtime, feature, service, STOP, scene, and contract-health shape |
| `suite_configuration` | Read every writable setting's committed value; `mode=describe` adds type, domain, and purpose |
| `trace_health` | Read trace-writer health, segment, record, and byte counters |
| `game_purchase` | Buy an Attribute (`StructureSO`) or Upgrade derived from its UUID |
| `game_cast` | Fire, release charge, or turn off one equipped toggle spell |
| `game_concept` | Add or remove one owned concept assignment |
| `game_agromancy` | Use the Agromancy screen's plot actions, harvest elements, and processing slots |
| `game_structure` | Enable or disable one available attribute |
| `game_spell_level` | Buy one spell mastery level or invoke level-all |
| `game_casting_dial` | Set the global Output Level or Reserve Level shown on the Casting screen |
| `game_spell_loadout` | Read staged Spellcraft glyphs; preview/add an explicit layout; or remove/move one equipped runtime spell |
| `game_targeting` | Submit one exact eligible target or let the native request choose one |
| `game_consumable` | Use, cancel, discard, randomize, or reorder one published consumable |
| `game_craft` | Craft a recipe or control its manual/automated instance |
| `game_discover` | Preview or confirm one composed discovery on seven surfaces, or drive one Discovery Tree offer lifecycle |
| `game_equipment` | Equip/increase or unequip/decrease an explicit amount of one created artifact |
| `game_alchemy` | Add, remove, or reorder one ordinary Alchemy recipe through its visible list |
| `game_ritual` | Select a Ritual, set its starting level, activate or end its battle, or cancel its duration reward |
| `game_level` | Buy an explicit amount of paid or bonus levels from an ordinary level-list control |
| `game_loadout` | Switch or edit the active player loadout, or save/load/clear an Equipment or Alchemy snapshot slot |
| `time_challenge` | Read the challenge screen, or select, queue, abandon, or reroll its offers |
| `time_prestige` | Confirm and perform the irreversible persistent reset |
| `game_research` | Develop/queue levels (`amount` defaults to 1), pause, resume, cancel, or apply a free research bonus level |
| `suite_automation` | Read the seven automation on/off buttons, or flip exactly one |
| `suite_config_set` | Commit one allowlisted setting through the configuration store |
| `suite_emergency_stop` | Engage or resume the suite's shared emergency stop |
| `game_screenshot` | Return the framebuffer as inline MCP image content |
| `game_continue` | Continue the already-selected save from the Start scene |
| `game_return_to_menu` | Raise the native manual-save event and return from play to the Start scene |
| `game_modal` | Dismiss the one unambiguous open native modal through its close control |
| `game_screen_catalog` | Read the live screens with the active screen and its subtab strips marked |
| `game_navigate` | Navigate a catalog screen/subtab and optional published plot UUID |
| `game_tooltips` | Page through active tooltip-bearing elements by indexed path |
| `game_tooltip` | Read compact plain screen text, including nested/computed and inspected content |
| `game_probe` | Read one fixed native fact not carried by `WORLD` |

`world_overview` deliberately contains only facts a strategist normally wants before choosing a
detailed read: collection completeness with total successfully read and skipped row counts,
unavailable categories, resource-row count, unlocked
structure count, affordable-structure and affordable-upgrade counts, discovered/mastery-ready recipe
counts, available views, visible plots, current action/spell/concept/plot occupancy, and the two
global casting dials with their purchased maximums. Exact rows remain in list/get/search.

The two affordable counts have a matching read: `world_list(category="structures", affordable=true)`
and the same on `upgrades` page only the rows whose price is met right now, so the count and the
rows agree and the offset, `total`, and `nextOffset` all speak in matching rows. `affordable` is
refused as `filter_not_supported` on a category with no price rather than quietly ignored.

`world_categories` is the authoritative inventory. Each row reports `category`, native type,
identity mode, row count, and exact availability. Internal world-property and row-type names are
not protocol data. `world_get` always requires `category` plus either a `uuids` list or the singular
`uuid` alias; supplying both is a `mutually_exclusive` validation failure, and either form returns
the same list shape.
Composite tables cannot be addressed by an arbitrary related UUID; use `world_list`.

Supply `uuids` with 1–200 canonical UUIDs. Results preserve input order without repeating an index or UUID on
successful rows. A typed not-found or invalid result repeats the implicated UUID because that is
failure evidence. Array reads have no aggregate status: each result owns its availability, while a
category or schema failure that prevents the call from running remains a top-level refusal. Every
row comes from the same pinned publication; the server does not issue a
generation or retain a snapshot token across calls.
Localized collection gaps mark only the implicated list/search/get row unavailable and attach the
partial row plus exact evidence there; unaffected rows in the same call remain ordinary results.
`world_overview` therefore summarises those gaps in one sentence rather than restating them:
`collection.gap` says how many leaves, of which condition types, on which named owners, and which
read returns them. The per-leaf evidence is the same bytes on every call for a given build and
already lives on the owner's own `world_get`, as `implicatedSkippedRows`.

Every paged read — `world_list`, `world_search`, `entity_catalog`, and `game_tooltips` — pages one
way. Each takes `offset` and `limit`
and answers with `total` plus `rows`; the collection is always present, including when it is empty.
`nextOffset` is present exactly when more rows remain, and its value is the input offset plus the
rows actually delivered, so `nextOffset` present means "resume here" and `nextOffset` absent means
"that was the end". There is no `truncated`, `returned`, `hasMore`, `matches`, or `tooltips`. A page
of the three world-backed readers may be shorter than `limit` because the response is bounded at
12 KB, which each of those tool descriptions states;
a short page with a `nextOffset` is that bound, and a short page without one is the end of the set.
The bound is charged against each row as it is built, so a full page is a real 12 KB page rather
than a fraction of one.
`game_tooltips` pages the screen's live hover elements rather than a published table, so `limit` is
its only page bound.
`world_search` deduplicates by entity identity before paging, so one entity that matches in two
categories occupies one row and one page slot.

The two search tools page in different orders and are not interchangeable at the same offset.
`world_search` walks the published world category by category in `world_list` order and, inside a
category, in publication order; `entity_catalog` walks the whole live registry in UUID order. They
also answer different questions: `world_search` sees only entities the published world carries a row
for and additionally matches a category's own name and native type, so a category name selects every
row in it, while `entity_catalog` sees every loaded UUID — including the ones no world row covers,
which read `category=not-world-projected` — and matches only that entity's own identity fields.
Equal totals for one query mean the query happened to select the same set, not that the tools have
the same scope.

`entity_catalog` complements `world_search` with the game's complete live runtime identity registry.
At the first stable Playing world capture after `RuntimeReady`, the suite validates and copies that
registry once for the lifecycle. Searches cover UUID, exact runtime type, Unity asset name, and
player-facing `GetName()`, so loaded entities hidden or not yet revealed by progression are findable
without navigation. Before that bind, or when its declared contracts fail, the tool returns
`unavailable` rather than substituting the build-time TSV fixtures.

A match contains `uuid`, `name`, `nativeType`, and one `category` — this and `explain_entity` are the
two surfaces that still carry the asset name and the runtime type, because browsing the catalog is
the one activity that asks for them. `category=not-world-projected`
means that the live registry identity has no world row. `nameSource=asset` appears only when no
player-facing name exists and the Unity asset name supplied the label; absence means the name is
player-facing. `internalName` appears only when it differs. The same immutable
catalog reference is pinned with the answering world and supplies names for every MCP entity
reference; UUID-only joins are unnecessary. Catalog membership and naming do not prove current
visibility, availability, or a world-category row. Lifecycle replacement clears the catalog before
the next bind, so no prior-save Unity reference or label survives.

`world_list` and `world_get` use the same deliberate player-relevant row projection. Every entity
row leads with its primary `uuid` and `name`; composite rows promote their actionable primary
identity while retaining separately named secondary references. A composite row with no identity of
its own — a spell slot, a cost row, a loadout section — never borrows a nested entity's `uuid` as
though it were addressable: it names its `category` and says `addressable: false`, so a caller
cannot hand that UUID back as the row's handle. Rows then carry only the small set
of availability, unambiguous paid/bonus/total level, quantity, occupancy, readiness, or progress
fields useful for comparing rows. Raw capture inputs, cached implementation fields,
resource traits, rate inputs, and modifier structs stay out of world rows. `explain_entity` owns
deeper evaluated evidence. Purchase-cost rows are composite and therefore remain a `world_list`
surface.

One fact has one name and one shape across every read that carries it. A scan row is a narrower
row, never a differently spelled one: `crafting-recipes` says `startingAmount` in the list exactly
as it does in the detail row, and `discovery-trees` says the named `mode` — `idle`, `crafting`, or
`choice` — rather than the native integer behind it. Whether a decision can be taken is `available`
everywhere it is known.

A `structures` row publishes `level` as the number the attribute's own badge shows, the game's
persisted `GetBaseLevel()`, and names work still in flight separately as `queuedLevels`, which is
always present because zero levels in flight is an answer; neither
number is repeated under a second name. Both are exact counts on the wire: the badge draws
`Utils.BeautifyInt`, so routing them through the large-magnitude renderer would round a
2,136-level attribute to `2.14e3`. An `upgrades` row publishes `maxLevel` and `remainingLevels`
only when the upgrade has a ceiling: a negative native maximum is the uncapped sentinel, so both
fields are absent together rather than reading `0`. `world_list` and `world_get` publish that pair
and `queuedLevels` identically, so an absent ceiling means uncapped on either surface and never
means the list dropped it. An exhausted upgrade reads `already_maxed` and
publishes no `affordable`, because a level that cannot be bought has no price to be short of.

Every purchasable counts levels, and no two of them count the same thing. One name means one thing
across the whole surface, reads and commits alike:

| Surface | Field | Native source | What the number is |
| --- | --- | --- | --- |
| `structures` (Attributes) | `level` | `StructureSO.GetBaseLevel()` | the exact count the badge draws |
| `structures` | `queuedLevels` | `StructureSO.GetQueuedQuantity()` | bought and still building; the badge shows these as `+N` |
| `upgrades` | `level` | `UpgradeSO.GetPurchaseLevel()` | levels bought. The upgrade screen labels the first one `Lv 1`, so its badge reads one above this count |
| `upgrades` | `queuedLevels` | `UpgradeSO.queuedLevels` | bought and still developing |
| every `game_level` target (glyphs, equipment types, resource types, time runes) | `paidLevel` / `bonusLevel` / `totalLevel` | the levelable's total and its granted levels | bought, granted, and their sum. `bonusLevel` is absent where the surface has no bonus concept, exactly as its `bonus` block is |
| `glyphs` | `usableCount` | the glyph's maximum usages | the uses the glyph screen counts — what a level buys |
| `research` | `purchasedLevel` / `baseLevel` / `bonusLevel` / `totalLevel` | the game's four distinct level accessors | completion is judged on `baseLevel`, never on `totalLevel` |
| `research` | `queuedLevels` | the develop decision's queue count | levels waiting, including the one in flight |
| `rituals` | `setLevel.current` | the ritual's selected starting level | where the ritual's own starting-level control stands |
| `game_targeting` candidates, `spell-slots` | `effectiveLevel` | the levelable's own current level accessor | what the entity currently *does*, granted levels included. Never a purchase coordinate: the price is set from `level` |

No surface publishes the sum of built and building levels under a single name: the retired
`committedLevel` was exactly that, and a number no screen shows cannot be checked against one. That
holds on every surface, `game_targeting` candidates and the `explain_entity` research `cap` block
included, and both name their levels waiting as `queuedLevels` like every other row. A separate
work-in-flight flag beside that count is not published either — it only restates the count.

`purchase-costs` is the only modifier-adjusted live cost category. Spell and alchemy cost rows are
immediate/drain observations and are not mislabeled as purchase prices. Every displayed cost is in
the screen's spend units: ordinary nominal costs are divided by the resource quality percent through
the audited `GetTrueSpend` formula, while bandwidth costs remain nominal. Each structure/upgrade cost row exposes
the screen's `cost`, the matching `spendableAmount`, and the resource identity needed for the
next decision. Every cost row uses `cost` for the screen price and `spendableAmount` for the
same-publication player pool, with `affordable` and a reason only when the decision was evaluated.
A row's verdict answers for that row's own resource; the whole price is what the rows fold to, so
no aggregate verdict is published beside them.
Ordinary resources compare their raw on-screen pool against the quality-adjusted spend; bandwidth
resources compare nominal cost against headroom using the game's integer-snapped comparison. A
counter's `amount` is always the number the screen shows for it, whatever native member happens to
carry that number. Cost rows use `spendableAmount` for the native admission operand, so independent
inverted/bandwidth flags never overload one field with two meanings. These fields use the same exact combiner as Auto Buy and do not
include Auto Buy's configurable reserve or excess policy.

A `resources` row is deliberately only named identity, the counter's on-screen `amount`,
`netRatePerSecond`, and, when the resource is capped, `capacity` plus `atCapacity`. A negative native
capacity is the game's uncapped sentinel and is never serialized as a magnitude. `atCapacity`
answers in the same coordinate as `amount`: it is true exactly when the published `amount` reached
`capacity`, so an inverted counter reading `amount: 0` is not at capacity and one reading its whole
pool is. Detailed
factor math will belong to a future Details-panel tool; it is not leaked through world rows.

Research rows distinguish the native evaluator's base and effective requirement levels. Their
`requirementLevelAdjustment` is the difference between those two native results, not a suite-owned
recalculation. `requirementAdjustments` carries each directly authored passive or active modifier,
including its amount and source UUID/type. This matters for challenge effects: the game installs
them as persistent/passive modifiers, so an active-only modifier count can be zero even while both
the research tooltip and native availability evaluator apply the challenge adjustment. Type-wide
research modifiers are included in the native effective result but are not misattributed as direct
members of the research row.

The same detailed row is the complete pre-decision surface for `game_research`. Uncapped research
omits `maximumLevel` rather than serializing the game's zero sentinel. It names the immediate or
queue route, live multi-buy maximum, exact number of levels the native cumulative loop will accept,
and ordered named costs paired with each resource's canonical `spendableAmount`. `develop.affordable`
and `develop.costs` appear exactly where the price is what decides: an available decision, or a
refusal whose `reasonCode` is `unaffordable` — a row refused for its price publishes the price that
refused it, rather than naming the blocking resource only inside the sentence.
While development is active it includes elapsed/required/remaining progress and,
per resource, the drain that pays for the research already in flight: `invested` and `required` are
the native fill bar, `remainingCost` is what that bar still owes in the units the player spends, and
`spendableAmount` is what the player actually holds. Associated
research types carry their remaining free bonus levels and investment caps. Only currently
UI-reachable next verbs appear: `develop`, `pause`, `resume`, `cancel`, and `bonus`. A committed
mutation returns the changed level or state; read detail remains in `world_get`.

`crafting-recipe-types` describes the game's crafting families; `crafting-recipes` contains the
actual recipes and is the pre-decision surface for `game_craft`. A recipe row leads with `visible`,
`startingAmount`, `craftTimeSeconds`, and `canStart`; when false, `blockers` names the failed native
visibility, purchase, queue, page-relation, or output-capacity axis. `execution` identifies the
direct, existing-stack, or new-instance route. Page routes include current `queuedAmount` and the
named queue's used/maximum slots. `purchaseAmount` and named `nextCosts` carry the exact next cost,
canonical `spendableAmount`, and affordability before any mutation. Direct costs use the
same `recipeCost.Multiply(purchaseAmount)` lineage as native `Execute`; page costs use the same
`GetTotalCost(previous,purchaseAmount)` lineage as native `QueueCraft`. Named `types`, `inputs`,
`outputs`, and `consumableOutputs` preserve authored order. An input
uses `cost` for the recipe requirement and canonical `spendableAmount` for what is spendable now (bandwidth
headroom for a bandwidth resource); outputs use `yield`. Only failed engagement-drain evidence is
emitted in `drainBlockers`. The category is unavailable unless both recipe and resource collectors
are clean.

`crafting-queue-entries` is the ordered live contents of every loaded manual and automation queue.
Each lean row names the queue and recipe, reports its slot counted from 1, current amount, and whether
the instance is automatic; only automatic entries carry their repetition count. The same
lifecycle-bound crafting reader supplies these rows and recipe decisions, so a malformed instance,
queue-role contradiction, or unstable page roster makes the category unavailable rather than
publishing a partial queue.

A crafting recipe row carries both queues its page owns as first-class facts. `queue` and
`automation` each name their own queue and carry `slots` — the same lean entries in slot order, so a
row that reports a queue is full also reports what is filling it and therefore what to cancel. The
collection is always present: a queue that was read and holds nothing lists no slots rather than
going absent.

An automated recipe has two numbers and they are not the same number. `automation.amount` is the
badge the automation strip draws — the queued instance's own quantity — and `automation.repetitions`
is the exponent behind it, because the game stores `quantity = 2^(repetitions - 1)`. They agree at 1
and 2 and diverge exponentially after that, so they never share a name: the strip's number is always
`amount`, the count of presses is always `repetitions`. The badge is joined from the queue entry
that draws it, so a recipe with repetitions but no collected entry publishes `amountUnavailable`
with that reason instead of an `amount` of `0` beside a positive `repetitions` — a pair the screen
never shows. The committed `automate` and `cancel_automation` deltas follow the same rule.

`world_search` deliberately indexes only categories whose identity mode is
`stable_entity_uuid`. It does not pretend that an owner/resource UUID uniquely identifies a
composite diagnostic row. If an owner UUID exists only in `entity-requirements`, search returns an
authoritative empty entity result; `world_list(entity-requirements)` retains the exact localized
owner, ordinal, and runtime type evidence. If a searchable entity row itself is returned and that
entity owns an unmodeled leaf, the search result is explicitly incomplete for that entity. Its
`total` counts only stable-identity matches that the response can actually return, counted after
identity deduplication so the total and the pages agree. A match is the same row
`world_list(category=...)` returns for that entity — named, scanned, and carrying its category and
native type — rather than a bare pointer that costs a second call to learn anything about what was
just found.

### Discovery decision loop

`discovery-trees` is the pre-decision surface for `game_discover`'s `offer_*` modes; attempting an
action is never the way to learn its cost or choices. Every row names the tree UUID/type, semantic
mode, rerolls left, discovered count, and whether discoveries remain. Authoring/debug members and
duplicate identity (`treeId`, overrides, debug mode, and bonus-level cost) are intentionally absent.
The Discovery Tree is a transient in-game event rather than a standing page, which is why its
lifecycle lives inside the one discovery tool instead of a permanent tool of its own.

In Idle mode, `initiate` reports `available`, a stable false `reasonCode` when needed, and each exact
cost line as a named `resource` plus `cost`, canonical `spendableAmount`, and `affordable`. In
Choice mode, `offers` contains named handle/category references in native order.
`selectedOffer` — the named reference every `…Uuid` becomes on the wire — appears only after
selection. The `reroll` decision block appears only in Choice mode. An empty offer set omits
`offers`.

These values are copied during the shared 250-millisecond world capture from lifecycle-bound
delegates for native visibility, immediate-required state, current choices, exact next cost,
affordability, and resource true quantity. The MCP worker only projects the immutable row. A choice
UUID must resolve in the same generation as an alchemy recipe, equipment, glyph, ritual, spell
recipe, or time rune. If it does not, only the implicated tree read returns
`discovery_offer_read_incomplete` with `implicatedOffers`; the UUID is never silently omitted.
Current offers are also resolvable through `world_get` and `explain_entity`, including authored
metadata and applicable discovery predicates.

### Generic discovery decisions

Every `alchemy-recipes`, `equipment`, `rituals`, `spell-recipes`, `time-runes`, and `glyphs` row has
one `discover` decision from the native `IDiscoverable` evaluator. A pool-unlocker glyph the game
never offers to discover answers the same verb rather than omitting the block: `available: false`
with `native_not_discoverable` and no costs, because a caller cannot tell a missing block from a
glyph nobody evaluated. A discovery decision names whether the entity
is visible, already discovered, required for downstream play, currently discoverable, and
affordable. Its ordered `costs` pair each named resource's screen-formatted `cost` with the same
canonical `spendableAmount` used everywhere else. Failed decision axes carry a stable reason;
attempting a mutation is never required to learn affordability.

`game_discover` is the sole discovery namespace, and it is deliberately component-first. The game's
compose pages let the player select components and then resolve exactly one output; the MCP
reproduces that direction and never accepts the desired output UUID as the decision.
`mode:"preview"` and `mode:"confirm"` take one `surface` from
`spellcraft|glyphcraft|devote|runecraft|alchemy|artifacts|concepts` plus ordered `components` of
`{uuid,count}`. The server derives the target and its native type from the live resolver; there is
no target argument to select with.
Zero or multiple resolutions refuse (`discovery_recipe_unresolved`, `discovery_recipe_ambiguous`)
instead of guessing, and a component that is neither an available glyph nor a published resource,
or that asks for more uses than the glyph permits, refuses as `component_unavailable`. This is why a
partial component write can never claim a target it did not resolve.

Spellcraft resolves core glyphs through the audited spell resolver; the other six surfaces use the
installed `UIDiscoverablePage` count-plus-membership semantics against exactly one published
category — Glyphcraft→`glyphs`, Devote→`rituals`, Runecraft→`time-runes`, Alchemy→`alchemy-recipes`,
Artifacts→`equipment`, Concepts→`alchemy-recipes`. When `surface` is omitted from `preview`, all
seven resolvers are tried and a unique match reports its surface. `preview` is classified read-only and never mutates; `confirm` repeats the
whole resolution live at the action boundary before permit, payment, or discovery. The
`offer_initiate`, `offer_select`, `offer_confirm`, and `offer_reroll` modes take the tree `uuid` instead,
and are the only modes that accept a UUID choice, because the transient offer UI really does show
and select those exact entities.

The `equipment` category is also the artifact-loadout pre-decision surface. Each row names the
artifact and its primary equipment type, current/maximum stacks, global and type-slot occupancy,
usage-cost resources with current holdings, and the next equip/unequip admission or refusal. Call
`game_equipment` with `mode:"equip"` or `mode:"unequip"` and an explicit positive `amount`; the
tool never reads or mirrors the UI multi-buy strip. A committed call returns the stack
count before and after. Usage reservations, effects, and
attunement are post-state evidence, never payment-verification gates.

### Ordinary Alchemy loadout loop

An `alchemy-recipes` detail row carries `alchemyLoadout` only for the six ordinary Alchemy families.
It reports `activeCount`, the ordered slot when active, and the next visible add, remove,
and move decisions. An available add includes the live click-sized maximum and named per-use resource
costs with current spendable holdings; an unavailable add carries only its binding reason. Concept
recipes remain on `game_concept`, composed Alchemy discovery remains on `game_discover`, and recipe
leveling belongs to the unified level surface rather than this list lifecycle.

`game_alchemy(mode="add"|"remove", uuid=..., amount=...)` applies the caller's explicit positive
amount through the list's native counted mutation after revalidating live usage capacity.
`mode="move"` instead requires the
`destination` exposed by the row. Success returns only the settled `activeCount` before and after, or
the ordered slot before and after for a move. The action boundary revalidates exact recipe identity,
ordinary-family classification, discovery, and capacity before invoking the explicit-count core
used by the UI wrappers, or the same list-swap route as the UI. The global multi-buy strip is never
read or changed.

### Ritual lifecycle

Ritual discovery remains `game_discover(surface="devote")`. Once discovered, a `rituals` detail
row reports the selected Ritual, the reached level, the starting-level control as `current` with
both its `minimum` and `maximum`, battle state, and active
duration-reward state. It also carries `waveTotal`, the wave count a run at the staged level has to
clear, read from `RitualSO.GetRequiredWaves()` rather than derived from the ritual's own bounds.
How far a run got is reported in the tense the game's own fields are in: while `inBattle` the row
carries the running battle's `wavesCompleted` and, once it has banked anything, its `spoils`;
afterwards those same two fields are the finished run's and appear as `lastRun`, with the verdict
`result` beside them. A cleared wave count and no spoils is exactly the state a ritual nobody has
played is in, so such a row reports no `lastRun` at all rather than an empty one.
Every row carries its activation and completion prices in the same
player-facing units as the Ritual panel and the eventual resource spend, selected or not:
`RitualSO.GetActivationCost()` scales the ritual's own stored cost by its own level, repeat penalty
and usage gate and consults the selection for none of it, so pricing only the held ritual made
"which of these can I afford" a question a caller answered by selecting each in turn — a mutation,
to read. For the same reason `activate.available` no longer waits on the selection: the verb presses
the selection toggle itself. `setLevel`, `activate`, and
`cancelDuration` each carry only the binding availability or refusal reason that affects the next
decision. `setLevel` has one presence rule for its bounds: every ritual whose starting level is the
caller's to choose publishes `minimum` and `maximum`, selected or not, because the ceiling is a fact
of the ritual and the player rather than of the selection — and it is available on an unselected
ritual too, because the verb performs the selection. Only a `level_locked` ritual — one the
game runs at an authored level — publishes no bounds, because there is no range to choose from.

`game_ritual(mode="select"|"deselect"|"activate"|"end"|"cancel_duration", uuid=...)` reproduces the
corresponding visible Ritual control. `mode="set_level"` also requires the `level` the Ritual
screen's starting-level selector shows, which runs from 1 to the row's `setLevel.maximum`:
`UIRitual` clamps that selector to `1..RitualSO.GetMaxSelectedLevel()`, so 1 is a starting level and
0 is not, even though the setter behind the control would accept it. A level outside that range is
refused with `level_out_of_range` carrying `minimumAmount` and `maximumAmount` — the same two
numbers its sentence names. `set_level` and `activate` both act on the selected Ritual, and the
game's selection variable holds whichever ritual its toggle was last pressed with — so both verbs
press that toggle themselves when the named ritual is not the held one, whatever was selected
before, and verify the selection landed. A caller is never refused for a step it could not see.
The old runestone-selection manager methods are empty/null-returning in
v1.0.5 and are deliberately absent. Activation revalidates the selected Ritual and the screen's
native price before payment; success is the settled battle transition. `cancel_duration` ends an
already-running duration reward and does not claim to cancel a battle. `activate` and `end` are the
two battle-boundary modes, so both report `activeBattle` and `wavesCompleted` as observed
`{before, after}` changes; `end` additionally reports the level the battle reached, the duration rewards it left running, and
the two facts the game's own results modal shows: `result` as `succeeded` or `failed`, read from
`RitualSO.IsFailedRun()`, and the `spoils` the run banked as named resource rows, empty array
included. Both are settled reads and not pre-mutation copies: `RitualSO.End()` writes neither
`wavesCompleted` nor `currentSpoils`, and the next `Initiate()` is what clears them, so the run's
record outlives the transition that ends it — which is why the row can still report it as `lastRun`
long afterwards. An `activate` reports no verdict: it is the mode that clears the record, and
`wavesCompleted < 5` is true of a run that is just starting. The two modes therefore read their
shared `wavesCompleted` pair in opposite directions — `end` reports the waves the finished run
reached, while `activate` reports the previous run's total falling to `0`, the reset the new battle
starts from. Both are the same settled `{before, after}` observation of the same field; which one a
response is saying is the mode it answers, never the shape of the pair.
Selection, level, battle, and duration activity each use one game-written outcome sentinel
and never a resource ledger.

### Unified level controls

The `equipment-types`, `glyphs`, `resource-types`, and `time-runes` detail rows are the complete
pre-decision surface for their ordinary level-list buttons. Each row distinguishes paid, bonus,
and total levels and carries a `purchase` decision. Equipment types, glyphs, and resource types
also carry `bonus`; time runes do not implement that native control. Available decisions include
the exact named native usage cost and current spendable amount as `costs`; a control the game
levels for nothing publishes `costs: []` with `free: true` rather than dropping the array.
Inapplicable or unavailable controls do not publish priced ledgers.

Call `game_level(mode="purchase"|"bonus", uuid=..., amount=...)`. The tool derives the exact native type from
the published category, repeats the visible button's live admission on Unity's main thread, and
returns only the settled paid- or bonus-level change plus the resulting total. A glyph target also
returns `usableCount {before, after}`, because that is the number the glyph screen draws — levels buy
uses through the mastery requirement, and a response that named only levels left the screen's own
count out.

A route whose cost table is empty on both sides says `free: true` rather than staying quiet. No
other pricing rides the answer: what a level cost and what the next one asks are read from
`world_get`, where the whole curve lives. The paid route checks the game's persistent usage cost but
does not perform a one-time payment; the concrete native level callback applies its own
usage/effects. Research development and spell mastery stay on `game_research` and
`game_spell_level`, respectively.

### Agromancy

Every `agromancy-elements` detail row joins the exact active-element count, the next visible
add/remove decision, its stored output and rate, and the element's offered harvest actions. An available element add includes
its named standing usage costs and current spendable amounts. Each offered action reports its
active/maximum count, its `add.available` and `remove.available` booleans, and the named resource
drain for the **next** instance. An unavailable control carries only the reason that binds the next
decision; no priced ledger is computed for an action the screen cannot run. One plot-action
prerequisite is not readable at all — the game latches it only when the action is attempted — so
that `add` is an unavailable read rather than a false one: it carries `status: "unavailable"`,
`reasonCode: "prerequisite_unverified"`, and `checkWith`, and never claims `available: false`.

Call `game_agromancy(mode="add_element"|"remove_element", uuid=..., amount=...)` for one exact
`HarvestElementSO`. The `add_element_action` and `remove_element_action` modes additionally require
`actionUuid` naming an action actually
offered by that element. Every mode requires an explicit positive `amount`; no mode depends on
hidden selector state.

The action boundary revalidates the concrete element/action pair, visibility, active-list room,
standing usage capacity, and mastery-derived action maximum on Unity's main thread. Success returns
only the active count before and after for the affected element or pair. The one mutation
sentinel is that game-written active count moving in the requested
direction; resource reservations and drain math are planning facts, never postcondition ledgers.

The `plot-nodes` category is the tile catalog. `agromancy-plot-actions` enumerates every
`PlotNodeSO` / `PlotNodeActionSO` pair authored by the game, not only Auto Harvest's fruit and
treasure collect pairs. Each row names both handles,
shows the active quantity, and carries add/remove decisions. An available add includes the plot
quantity consumed by one instance and the current maximum additional count, which is the game's own
remaining-instance count and can exceed the 10,000 this verb accepts in one call. An unevaluated
prerequisite latch omits `available` and reports `requiresLiveCheck:true`; the action boundary
performs the exact native check instead of a read mutating the latch.

Call `game_agromancy(mode="add_plot_action"|"remove_plot_action", uuid=..., actionUuid=...,
amount=...)`. Every call requires an explicit positive `amount`.
Add uses the same active plot-action list control as `UIPlotNodeActionList.OnActionClick`.
Remove decrements an existing quantity; at the native minimum it uses that UI handler's distinct
`Cancel()` path, so crossing from several instances through the last one requires two calls.
Success returns the observed active quantity change. The only
postcondition is the exact pair's game-written active quantity moving in the requested direction;
refund behavior on cancellation is neither recomputed nor verified.

`agromancy-processing` is the screen's top processing strip in screen order. Each row reports its
slot, whether it is empty, the strip capacity and occupancy, and—when occupied—the named plot,
named action, amount, and whether it is processing. The former helper categories for harvest
controls/resources, plot instances, and raw action-queue internals are not public MCP categories;
their facts are joined into these three player-facing surfaces.

### Structure enable and disable

Every `structures` detail row reports the attribute's current `enabled` state and the one next
toggle the player can take. An unavailable structure carries only `not_available`; the MCP does
not expose the native callback until the same availability fact the screen uses is true.

Call `game_structure(mode="enable"|"disable", uuid=...)` with a published structure UUID.
The boundary revalidates exact `StructureSO` identity, availability, and current state on Unity's
main thread, then invokes the screen's `ToggleDisabled()` route. Success returns only the settled
`enabled` value before and after. The single postcondition is the game-written `disabled` flag
reaching the requested state; `ApplyEffects` and `RemoveEffects` remain native consequences of
that callback and are not independently replayed or audited by the suite.

### Alchemy screen ownership

Alchemy's Learn side uses `game_discover(surface="alchemy")`. Its Loadout side uses
`game_alchemy` with the published recipe pool, ordered capacity-bounded slots, six type-capacity
counters, and the same type identity the screen filter displays. Recipe mastery and Alchemy-type
levels are game-driven progression displays, not direct purchase buttons on this screen.

There is deliberately no Brewing Station tool or category. The v1.0.5 data contains one unnamed
legacy `CraftingStructureSO` asset, but its runtime instance list is authored empty and the entire
data graph has no unlock/effect edge that creates one. The assembly still contains the unused
`UIBrewingStation` renderer, but no player-facing label or live screen owns it. Publishing its
native selectors as a verb would expose developer-era machinery the shipped UI does not offer.

### Player loadouts and snapshots

`player-loadouts` lists the loadouts the player titled, in bar order. A detail row reports whether
the loadout is selected, whether its Equipment and Alchemy sections are enabled, the current icon and
color indexes, whether the native manager can switch now, and the named saved spell, Equipment,
and Alchemy entries. All three sections are always present with their own list: a loadout that
saved nothing publishes empty lists rather than dropping the keys, so empty never reads the same
as unprojected. `game_loadout(mode="select", loadout=...)` invokes the manager's whole
save/deactivate/load/reactivate transaction after revalidating every stored reference's identity,
native type, role, and whole-loadout capacity. Current glyph ownership is deliberately not a
selection precondition: the native screen accepts authored saved layouts whose construction
choices are no longer available. Success returns the observed selection change and the settled
selected loadout.

The selected player row also owns the three controls visible in the editor:
`set_section` requires `section:"equipment"|"alchemy"` plus `enabled`; `rename` requires a name
of at most 24 characters; `next_icon` and `next_color` advance one step through the same native
lists as the UI. Arbitrary icon/color indexes and a free-standing save verb are absent because the
screen exposes neither. Every one of those modes names its loadout with `loadout`, its position on
the loadout bar counted from 1. A player loadout is a live Unity object the asset catalog never
publishes, so it has no id the wire can carry, and a refusal that cannot find one says how many
loadouts there are.

`snapshot-loadouts` identifies both the Alchemy and Equipment snapshot-list owners; each detail
row exposes its kind, its visible slots counted from 1, populated state, and named saved entries.
Snapshot rows and their owning lists are live objects with no published id, so `snapshot_save`,
`snapshot_load`, and `snapshot_clear` take `section:"equipment"|"alchemy"` plus `slot`. Save accepts only an empty slot, load/clear only a
populated slot, and no overwrite mode exists. Optional native type must match the owning
`AlchemySnapshotListVariable` or `EquipmentSnapshotListVariable`. Success returns the observed
slot or active-section change from the settled world; usage capacity is admission only and no
resource ledger is returned.

A snapshot refusal answers in the snapshot's own words. A slot outside the live list carries
`minimumSlot` and `maximumSlot` read from that list. Nothing staged is answered before the record is
validated, because an empty active section has no entry that could fail a limit. A type slot is one
artifact however deep its stack, matching native `EquipmentListVariable.GetTypesEquipped` against
`EquipmentTypeSO.GetMaxTypeSlots`, which is the pair the game itself equips against.

### Challenge decision loop

The `challenges` category is both the per-entity read and the pre-decision surface for
`time_challenge`. Every row carries the native idle/queued/active/passed/failed state, current and
maximum level, native next difficulty/reward, availability/completion verdicts, selection and offer
membership, and explicit `select`, `queue`, and, when active, `abandon` decisions. Challenge
selection has no resource price, so a row does not invent empty costs or affordability.

`time_challenge(mode="state")` is the screen itself, answered when a caller asks for it: ordered
fully named `selected` and `offers`, selection capacity, first-draw state, rerolls, one `reroll`
decision, and `prestige`. It is a read, and it no longer rides challenge list/get pages — a request
for one row at offset fifty used to come back nine tenths ambient state, repeated on every page.
`resetOffers` appears only in the build where the Reset modal's list and the Time screen's list part
company; they draw from the same asset, so it is normally absent rather than said twice.

`state.prestige` is the persistent-reset pre-decision surface. It reports the reset's starting Time
Advancements against the previous reset's and the difference between them (the screen's own
subtraction), the reset count, the fully named persistent resource with its current spendable
amount and real capacity semantics, the challenges queued for the reset, surviving rewards, and the
exact `reset.available` decision. No attempt/refusal is needed to learn whether a reset can run.

The MCP-only sequence is:

1. Page `challenges`; compare next difficulty/reward, then call `time_challenge(mode="state")` for
   the named ordered offers and the reroll budget.
2. Call `time_challenge(mode="select", uuid=...)`; its terminal response returns the changed target
   state. When every selection the cycle allows is taken and exactly one is held, the tool performs
   the screen's own first press — giving that one up — before taking the one asked for; when more
   than one is held, which to give up is the caller's choice and the refusal says so.
3. Call `queue` to move an offered target between idle and queued, or `abandon` for one the reset
   started. A queued challenge starts running at the next reset, not immediately.
4. Only when a different offer set is wanted, call `reroll` without a UUID. It is the game's one
   new-challenges button: free the first press of a world cycle, one reroll every press after. The
   terminal response returns what the press cost (`rerollsLeft` and `challengesFetched` as
   `{before, after}`), whether the offers moved (`changed`), and the replacement offer list.
5. When the prestige decision is available, call `time_prestige(confirm=true)`. Success waits for a
   newer world after the native scene reload and returns the new scene, `prestigeState`, and the
   challenge state inline. The explicit boolean prevents an empty or accidental call from
   triggering the irreversible reset.

The MCP-only offer sequence is seven calls when two offers need explanations:

1. `world_list(category="discovery-trees")` and choose a named Idle row whose
   `initiate.available` is true.
2. Call `game_discover(mode="offer_initiate", uuid=...)`; its terminal response is the running
   craft. Both presses land in the tree's Crafting mode, and the game only rolls that craft into
   Choice mode — filling the offer list — three seconds of game time later, so the offers are read
   with the next `world_get` rather than waited for inside the call.
3. Call `offer_reroll` when `reroll.available` is true; a false one names which of the tree's
   states — no offers, a discovery to take first, no rerolls left, a reroll already spent — is
   refusing. Its terminal response is the restarted craft, settled the same way.
4. Call `explain_entity` for the candidates that require comparison. No catalog name joins are
   needed because every reference already carries its name.
5. Call `offer_select` with that `offerUuid`; its terminal response is the settled tree naming
   `selectedOffer`. It omits `offers`: a selection changes which offer is held, not what is
   offered, and the caller just picked from that list.
6. Call `offer_confirm` with the same UUID; its terminal response is the Idle tree plus the next
   initiate costs. There are no post-mutation `world_get` calls, snapshot tokens, or receipt polls.

### Spell discovery and loadout-add loop

`spell-recipes` is the pre-decision surface for both Spellcraft discovery and loadout add. Each
named row contains the authored ordered `coreGlyphs` with current owned and bonus levels, every
equipped runtime instance of that recipe, and the shared `loadBudget` of used/maximum slots plus
`fitsAnotherSpell`. An undiscovered recipe exposes `discover`, including the `surface` and the
ordered `components` to submit; a discovered recipe exposes `loadoutAdd`. Discovery carries its
named exact costs, spendable amounts, affordability, and stable false reason. Loadout add truthfully
reports only structural admission plus `requiresGlyphLayout:true`: its price depends on the explicit
augments that have not yet been chosen. A structural refusal is `loadout_full`, or the one thing
that is actually wrong with the core glyph — `recipe_has_no_core_glyph`, `core_glyph_not_published`,
`core_glyph_not_owned`, `core_glyph_not_leveled`, or `core_glyph_augments_only`; the retired
`core_glyphs_unavailable` covered all five under one word. There is no selection step and no
target-first `create`: the game exposes neither.

A detailed row also carries the recipe's authored half, which is what the spell is before any
modifier touches it: `casting` (cast type, recharge seconds and multiplier, recharge processor type,
and — only where the recipe has them — maximum channel seconds and repeat effect rate),
`authoredCosts` split into `cast` / `upkeep` / `hold` with each named resource and its unmodified
price, and `belongsTo` naming the recipe's `spellTypes`, `coreGlyphs`, and `recipeBooks`. These are
authored facts, so they do not move within a run; the live price a cast will actually pay is the
`castCosts` on the equipped instance, not `authoredCosts`.

The MCP-only base-recipe sequence is:

1. Page or search `spell-recipes`; compare names, core-glyph holdings, discovery costs, and
   affordability.
2. For an undiscovered recipe, call
   `game_discover(mode="preview", surface="spellcraft", components=[...])` with that row's
   components and check the resolved output, then repeat the call with `mode:"confirm"`. The
   response reports the resolved target and discovery transition.
3. If an equipped instance is wanted, call
   `game_spell_loadout(mode="preview", uuid=..., glyphs=[...])`. This read resolves and prices the
   submitted layout through the same native manager methods used by add, without touching the
   player's staged UI selection. It returns the named resolved recipe, named per-resource costs,
   overall affordability, and the named short resource when unaffordable.
4. Call `game_spell_loadout(mode="add", uuid=..., glyphs=[...])` with that same layout. Adding is
   the only mutation where the layout is chosen; it is baked into the created runtime spell.

Every referenced entity is named inline. No catalog join, world-generation argument, payment
stanza, receipt poll, or post-mutation `world_get` is required.

### Casting dial loop

Output Level and Reserve Level are the two sibling global steppers on the Casting screen, not
per-spell settings. `world_overview` carries them as `casting.output` and `casting.reserve`, each as
`current`/`maximum`; the block is absent until the Output maximum is nonzero. **The floor of both
dials is 1**, in every save and on every call, so it is documented here rather than repeated in
every overview a caller reads.
Raising a cap is an ordinary `game_purchase` against the corresponding upgrade UUID, so the dial
tool only moves the value inside the live native range.

`game_casting_dial(dial="output"|"reserve", value=N)` is the whole surface. `value` runs from 1 to
the live native maximum, which the action boundary reads from the exact global `IntVariable` for
that dial before verifying the requested value became observable. A committed result returns the
changed dial as `before` and `after` plus the `maximum` the read publishes.

There is deliberately no in-place augment editor. The visible game has none: glyph layout is chosen
on the library candidate before add, and changing it is remove → relayout → re-add. A discovered
recipe's `loadoutAdd.augmentOptions` names owned spell-augment glyphs only where choosing them is the
next decision.

### Spell loadout loop

`spell-slots` is the pre-decision surface for `game_spell_loadout`. Each occupied detail row names
the recipe the equipped spell was baked from, its slot, active cast/ready/attune state when
applicable, the game's current remove verdict, and that spell's move destinations. Augment choices
appear only on a discovered recipe's `loadoutAdd` decision. `loadBudget` — `used`, `maximum`, and
`fitsAnotherSpell` — rides on every detailed `spell-recipes` row, so capacity is known before add.

An equipped spell is a runtime instance, and the catalog publishes assets, so that instance has no
handle any tool can resolve. The row therefore carries no id of its own: the recipe names the spell
and the slot addresses it, which is also what the bar on the screen shows.

The MCP-only loadout sequence is:

1. Call `game_spell_loadout(mode="staged")` when the current Spellcraft core/augment selection is
   relevant. The request-scoped read returns ordered named `core` and `augments` stacks and does
   not acquire mutation ownership or change the UI selection.
2. Read `world_list(category="spell-slots")` and choose one occupied `slot`, or read a discovered
   `spell-recipes` row's `loadoutAdd` decision to add a new spell.
3. Call `game_spell_loadout(mode="preview", uuid=..., glyphs=[{uuid,count}, ...])` to resolve and
   price that exact layout without changing the staged UI selection. `[]` is a valid intentional
   empty augment layout, not "reuse whatever the UI last selected".
4. Call `game_spell_loadout(mode="add", uuid=..., glyphs=[...])` with the previewed layout.
5. Call `game_spell_loadout(mode="move", slot=..., destination=...)`; success returns the slot
   change.
6. Call `game_spell_loadout(mode="remove", slot=...)` only when that row's `remove.available` is
   true; success returns the removed spell's former slot.

`staged` accepts no other field. The `uuid` means a recipe and belongs to `preview`/`add` only;
`slot` addresses the loadout bar for `remove`/`move`, and `destination` belongs to `move` only.
Anything else is a named `unexpected_for_mode` validation failure rather than a silently ignored
field.

Add reproduces the library button's own admission order: it creates the native candidate, applies
the recipe's selected level and the requested glyphs, then requires recipe usage requirements,
computed usage-cost affordability, unique-spell compatibility, loadout capacity, per-glyph usable
counts, and non-level glyph requirements before payment, which is taken last. Remove and move
re-resolve the named slot and the native remove verdict or slot range on the Unity main thread.
Every mode acquires the family permit last and verifies only requested identity/outcome. Weight,
glyph usage, drain, and resource accounting are observations, not gates. There is no generation,
payment, receipt, request echo, catalog join, or post-mutation read-back.

### Targeting decision loop

`targeting` is the pre-decision surface for `game_targeting`. It is empty while no target request
is pending. Its active row names the requesting effect, identifies the native selection kind,
reports the game's own `cancelAvailable` flag, and carries every eligible structure in native order.
Each candidate is fully named and includes current committed/effective level, availability, and
work-in-flight state. Costs and affordability are absent because targeting spends no resource.

The MCP-only targeting sequence is:

1. Read `world_list(category="targeting")` and compare its named ordered candidates.
2. Call `game_targeting(mode="submit", uuid=...)` to submit one exact candidate, or
   `game_targeting(mode="randomize")` to let the native request choose and immediately submit.

There are only those two modes. The visible Close button dismisses the targeting presentation and
does not cancel the gameplay request, so MCP exposes no cancel verb rather than reaching past the UI
into the owning effect result. Submit and randomize success return the named submitted structure.

### Consumable decision loop

`consumables` is the pre-decision surface for `game_consumable`. Each row contains its named
identity, amount and queued amount, level holdings, family types, immediate and held costs with
current resource amounts, native affordability/use admission, pending usages, current inventory and
hotbar placements, and every same-list destination. The row's `use`, `cancel`, `discard`, and
optional `randomization` objects are the next decisions; no trial action is needed to learn them.

The MCP-only consumable sequence is:

1. Read `world_list(category="consumables")` and choose from named costs, holdings, usages, and
   action verdicts.
2. Call `game_consumable(mode="use", uuid=...)` or
   `game_consumable(mode="cancel", uuid=...)`.
3. Call `game_consumable(mode="discard", uuid=..., amount=...)` for a positive amount,
   `game_consumable(mode="set_randomization", uuid=..., enabled=...)`, or
   `game_consumable(mode="move", uuid=..., list="inventory|hotbar",
   destination=...)` for a same-list position counted from 1.

Every committed mode returns the changed amount, flag, or slot. There is no
payment stanza, receipt, world-generation argument, catalog join, or post-mutation read-back.

### One-shot crafting decision loop

`crafting-recipes` carries everything needed to choose a one-shot craft: player-facing identity,
native route, purchase amount, exact named costs and current holdings, affordability, queue
identity/room/current quantity, outputs, and blockers. The MCP-only sequence is:

1. Read `world_list(category="crafting-recipes")` or batch exact recipes through `world_get`.
2. Choose a row whose `canStart` is true after comparing `nextCosts`, outputs, and queue state.
3. Call `game_craft(uuid=...)`.

Success returns the changed recipe quantity or queue fact.
It has no receipt, payment stanza, world-generation argument, or read-back requirement. A timed
recipe without one stable loaded authored page refuses rather than guessing a queue; failure after
native work names the one missing direct, instant-stock, or queued-recipe outcome. Auto Scribe calls the same GameAction with
its own existing planner, so MCP crafting does not create a second Scribe implementation.

### Entity explanation

`explain_entity` accepts one canonical `uuid` and pins the latest immutable world publication before
it resolves or evaluates anything. Its entity row, predicates, requirements, costs, and blockers
all come from that one publication;
the tool neither retains a snapshot token nor follows a newer publication during the call.
Named identity appears once. When the resolved native entity implements the audited `ITooltipable`
contract, its authored `GetDescription()` text leads the explanation after identity; no description
is invented when that source is absent. The `state` row uses the same curated player surface as
world reads rather than serializing the collector's complete internal struct.

The `predicates` and `blockers` blocks are always present, empty or not, so an entity nothing
applies to never reads like an entity nobody evaluated. Only applicable predicate slots are inside:
`visible`, `available`, `canDevelop`, `canPurchase`,
`canDiscover`, and `canUse`. Presence means applicable. Each slot carries `value` and a stable
`reasonCode`; absence means the predicate does not apply, not false. Crafting purchase uses the
published `CraftingRecipeSO.CanBuyAt(GetStartingQuantity())` verdict, spell use uses the equipped
`Spell.CanCast()` reading, and structure/upgrade purchase combines published native availability
with the one exact-cost affordability lineage. No predicate emits implementation provenance or a
permanent never-evaluated apology.

Discovery trees are explainable entities: their explanation carries the same decision row as
`world_get(discovery-trees)`. Both misses are `ERR_NOT_FOUND`, and the sentence and the `readWith`
remedy are what separate them: a UUID absent from the live identity registry says no entity in this
build carries it and points at `world_categories`, because the caller has no name to search with; a
catalog-known UUID with no explainable row says the entity exists without a detailed explanation and
names the read surface that does carry it. A UUID the asset catalog does not know but the world
published inside a composite row — an equipped spell instance is a runtime object, not a loaded
asset — is that second case, not the first: it points at `readWith: {tool: "world_list", category:
"spell-slots"}`. The surface never claims the process is ignorant of a UUID it published, and never
points a runtime instance at the asset registry that cannot resolve it.

Per-level structure, upgrade, and Research requirements preserve the implicit container `AND`,
explicit native `AND`/`OR` nodes, authored order, and recursively expanded prerequisite-link tiers.
Every operator node carries its `children` list, so an entity with no requirements reads as an
empty list rather than as an operator over an unstated set. Every leaf names
the requirement UUID and native type, comparison kind, exact published value selected by the native
evaluator (`purchased_level`, `total_level`, `purchased_quantity`, discovery, mastery, recipe,
advancement, reached, numeric, or link gate), current and required values, met verdict, and base,
scaled, and effective thresholds. Unsupported comparisons return a structured unevaluable result.

The collector also captures the safe parameterized
`Prerequisites.Container.Check(Requirements.ConditionInfo)` answer at the exact next-purchase level.
The worker compares its graph verdict with that same-publication native answer. Missing inputs,
unevaluable suite math, a different owner/level, or a disagreement makes the whole explanation
`unavailable`; a disagreement returns both verdicts and `native_verdict_mismatch`. The installed
v1.05 contract additionally pins that a structure quantity requirement reads purchased `quantity`,
not `selfBonusLevels` or an effective/total level.

Research explanations separate base, scaled, and native effective requirement thresholds and retain
every direct adjustment's UUID, source native type, modifier type, amount, order, and passive state,
including challenge sources. Their `levelPrerequisites` graph uses the native
`GetRequirementLevel()` as its check level. A Research prerequisite leaf selects the target's native
total level because `ResearchRequirement` dispatches the virtual `GetLevel()` accessor; completion
and the maximum-level cap instead use native base level, which includes purchased/base grants but
excludes bonus levels. `visible`, `complete`, `canDevelop`, range, leeway, and both cap predicates are
published native answers rather than MCP-owned reconstructions. Structure/upgrade purchase evidence uses only the published
`WorldExactCostMath.TryCombinedExactCost` lineage and reports base/effective/grouped cost, named
modifier sources, available amount, and affordability. `blockers` contains typed queue, cap,
leeway, recipe-discovery, bandwidth, and drain evidence only when an axis applies. Empty collections,
null domain properties, and inapplicable axes are omitted.

`game_navigate` is classified **UI-only, no gameplay/save mutation**. It is not read-only because
selecting a screen, subtab, or plot commits live UI state. Success returns `activeScreen` and every
independent `subtabStrips[{active,labels}]` state, read exactly once, from the settled destination.
A hierarchy still assembling one frame after the click still carries the departed screen's strip, so
it is never a source. If the navigation shell is gone by the time arrival settles, the response says
`subtabStripsUnavailable` with that reason rather than publishing a strip nobody read. A screen or subtab match refusal returns the exact
live label candidates it compared. A subtab refusal reached its screen before it failed, and its
sentence says so: the screen change is a committed effect the caller can see in `activeScreen`. It carries no static mutation-scope label or counter ceremony. Navigation
never authorizes a gameplay or save mutation.

`suite_health` has no arguments or detail mode. It is exception-shaped compact text. The standing
lines are the leading `available` verdict, the build and its twelve-hex-character DLL fingerprint,
scene, lifecycle state and generation, world publication, and emergency STOP. Everything else
appears only when it is a problem: `runtime:`, `native contracts:`, `game_craft:` and `game_modal:`
each cost a line exactly when they read `unavailable`, followed by the reason that names why,
`agent settings:` costs a line exactly while the last load could not normalize the settings every
documented verb assumes, and feature and service names are grouped by state and reason code. Seven identical NotReady features
therefore occupy one line, not seven objects. It returns no structured payload because none of those
labels is a handle for another call. It reads those owners only for the requested operation and
reports no MCP queue internals.

Runtime availability is a fact about the session, not about the scene, and the report says so.
The ServiceCycle runtime is created once, on the first frame the host admits it, and released only
when the plugin is destroyed, so the same scene reports `runtime: unavailable` before that frame and
stays silent about it ever after; the reason names the session, never a scene property. The same
holds for the `game_craft` and `game_modal` lines, which state whether this build failed to resolve
those bindings at all. Whether a game exists is the `lifecycle:` line — the same state and generation
`game_probe` reports — and whether a world is published is the `world:` line: the live publication's
generation, or `not published`.

A lifecycle boundary trashes the published world, so `world:` returns to `not published` the moment
the run it described ends, and the Start-menu reading before a run and after one are identical. On a
lifecycle that is not `Playing`, every world-backed read answers `status: unavailable` with a
`reasonCode` naming that state — `lifecycle_no_game`, `lifecycle_initializing`, `lifecycle_resetting`,
`lifecycle_scene_exit` — instead of serving the destroyed run. Those payloads, and
`world_not_published` for a playing run whose first collection has not landed, all carry
`lifecycleState`, so `suite_health`, the world readers, and `game_probe` cannot hold three beliefs
about whether a game exists.

An empty clean category means the save has no rows. A skipped native row normally makes exact
queries for the whole category unavailable. The deliberate exception is an unmodeled entity
requirement leaf: the collector publishes that leaf with its owner UUID, container, ordinal, and
runtime condition type. When those rows reconcile exactly with the skipped count, `world_get` and
`world_list` keep other owners authoritative, while `world_search` localizes the evidence only when
a returned stable entity owns the leaf. An entity get/list/search that touches the affected owner
returns that row with `status: unavailable`, `reasonCode: entity_data_incomplete`, and exact
`implicatedSkippedRows`; unaffected rows remain ordinary available results. A UUID found only in a composite row
remains outside search coverage and is diagnosed through `world_list`. If even one skipped read cannot
be tied to a published owner/leaf, the category-global refusal remains. Derived tables also require
every upstream collection report to be clean.

## Inline action results

There are no receipts, pending states, cursors, or polling tool. `action_receipt` does not exist.
Every action, configuration write, STOP transition, and gadget waits for Unity's next frame and
returns its terminal result in the same MCP tool call.

A committed gameplay mutation then goes through one shared settlement rather than a per-tool sleep
or poll: it waits up to one second for a world captured after the action completed and projects the
changed fact from exactly that immutable world. A prompt publication returns immediately, and there
is no routine lag field on the ordinary path. If no such world arrives in time, the mutation stays
committed and the response carries the single exceptional
`postStateUnavailable / post_state_timeout` fact instead of an empty success or the pre-mutation
world. Reaching that path repeatedly in live play means a missing publication trigger to diagnose,
not a timeout to lengthen.

Every action verb settles the same way, and there is exactly one idiom for it. A commit answers
with the facts its own press changed, each as a `{before, after}` pair, plus any fact the press
produced that has no "before" — a battle's result and spoils, a settled level, the price it drew.
When the settled world cannot prove the change, the answer is `postStateUnavailable`. No verb
re-reads a whole screen and no verb appends what is possible next: the decisions a press reopened
are read with `world_get`, which is where every other caller reads them.

A successful read uses `available`; an unavailable domain read uses `unavailable`. A successful
mutation uses `committed`; a refused mutation uses `refused`; infrastructure or native divergence
uses `faulted`. A tool's status word never depends on one of its arguments: `game_screenshot`
answers `committed` whether or not `save` was asked for, because the capture is something the
server performed either way. Success adds only the settled delta and omits a code that would restate
`committed`. Refusals and faults add a stable `reasonCode`, one actionable `reason`, and only the
identity, admission, or missing-outcome facts that made it true. Counters, request echoes,
generations, and decomposed receipts are absent. There is one canonical shape per tool and no
verbosity option.

`reason` is always prose and `reasonCode` is always the machine name, on every surface — a read's
blocked sub-decision follows the same rule as a mutation's refusal. A refusal whose sentence names a
ceiling also carries that ceiling as `maximumAmount`, read from the same admission capture the
sentence was written from, so the two can never disagree.

A request that names two entities keeps the one it addressed at the top of its response, on the
refusal and the commit alike, and names the second in its own block (`game_agromancy` addresses a
plot or element and carries the action as `action`). The same request shape never answers with one
entity's identity when it refuses and the other's when it commits.

A sentence names entities the way a player does — display name only. The asset name, the UUID, and
the native member that decided are identity and evidence, and the same response already carries them
as fields, so repeating them inside prose only made the sentence harder to read. Two exceptions stay
deliberate: a sentence falls back to the UUID for an entity with no known name, because naming the
only handle there is beats naming nothing; and a `contract_unavailable` or faulted result still names
the native member it could not read, because that result is a defect report and the member is its
subject.

One generator writes those sentences and the wire pass every response already crosses reaches it, so
a code that arrives without prose leaves with it. A producer holding the numbers writes the better
sentence itself and keeps it: a shortfall names the resource, the price, and the holding, where the
code alone can only say that something was short. A read block and the refusal of the mutation it
guards therefore answer one gate in one sentence — `game_research develop` says
`Needs 4 Orb Advancement (have 0).` whether it is asked or attempted.

### Refusal vocabulary

A refusal carries **one of eight classes** and one sentence. The class says which kind of no this is
so a caller can branch; the sentence says everything else, and it is the part that names the target,
the number, and the fix.

| Class | The caller should |
| --- | --- |
| `ERR_INPUT` | Fix the argument. It was malformed, out of range, ambiguous, or forbidden for the mode |
| `ERR_NOT_FOUND` | Look elsewhere. The named thing is not there — no such id, no such row, no such offer |
| `ERR_STATE` | Do something else first. The target exists and is in the wrong state for this verb |
| `ERR_LIMIT` | Ask for less, or free something. A ceiling, a capacity, or a budget is reached. Carries `maximumAmount` or `minimumAmount` where a number fixes it |
| `ERR_UNAFFORDABLE` | Earn or spend less. Named resources fall short; the sentence names every one |
| `ERR_LOCKED` | Progress first. Visibility, discovery, or authored requirements are not reached yet |
| `ERR_UNAVAILABLE` | Retry or repair. The suite or the game could not read or serve the fact — no world published, no save loaded, a contract missing, a post-state that never settled |
| `ERR_REFUSED` | Read the sentence. The game refused and the published world does not account for it |

The set is fixed at eight. A private word per refusal is a dialect every caller has to learn before
it can branch, and the sentence beside it already says more. Producers choose a precise internal
code — that is what picks the sentence — and `GameMcpDecisionReason.Class` maps it to the class the
wire says. That method is the whole map; the rows below name the codes each class is reached by
most, so an old code's new class can be looked up here:

| Class | Internal codes that reach it |
| --- | --- |
| `ERR_INPUT` | `invalid_uuid`, `invalid_offset`, `invalid_limit`, `unknown_category`, `unexpected_for_mode`, `slot_out_of_range`, `screen_match_failed`, `composite_identity_required`, `discovery_surface_ambiguous` |
| `ERR_NOT_FOUND` | `unknown_uuid`, `slot_empty`, `not_active`, `no_pending_target`, `no_current_offers`, `recipe_has_no_core_glyph` |
| `ERR_STATE` | `invalid_state`, `already_maxed`, `already_developing`, `switch_blocked`, `slot_occupied`, `reroll_already_used`, `immediate_required_discovery`, `cast_in_progress`, `charge_unavailable`, `resources_uncovered`, `attuning` |
| `ERR_LIMIT` | `amount_unavailable`, `automation_full`, `loadout_full`, `research_queue_full`, `no_rerolls`, `level_cap_reached`, `artificial_research_cap_reached`, `research_investment_cap_reached` |
| `ERR_UNAFFORDABLE` | `unaffordable`, `usage_unaffordable`, `level_not_affordable`, `insufficient_quantity`, `insufficient_bandwidth` |
| `ERR_LOCKED` | `not_available`, `native_unavailable`, `hidden_or_undiscovered`, `native_hidden`, `hidden_discovery`, `requirements_unmet`, `requirement_unmet`, `native_not_discoverable`, `recipe_not_discovered`, `not_discovered_or_offered`, `prerequisites_unmet`, `core_glyph_not_owned`, `cannot_level`, `research_leeway_exhausted`, `native_leeway_exhausted` |
| `ERR_UNAVAILABLE` | `world_not_published`, `lifecycle_no_game`, `contract_unavailable`, `post_state_timeout`, `category_not_collected`, `configuration_unpublished`, `runtime_not_available`, `price_unavailable`, `affordability_unavailable`, `requirement_unevaluable`, `threshold_scaling_unavailable`, `requirement_cycle`, `requirement_depth_exceeded`, `queue_not_published`, `queue_reading_inconsistent`, `entity_catalog_unavailable` |
| `ERR_REFUSED` | `native_rejected`, `native_purchase_refused`, `native_can_develop_refused`, `projection_refused` — the game's own gate said no and reported nothing else |

Three of those placements are worth reading twice, because the obvious guess is wrong.
`slot_out_of_range` is `ERR_INPUT` and not `ERR_LIMIT`: the caller named a slot the list never had,
which is a bad argument rather than a ceiling reached. `cannot_level` is `ERR_LOCKED` and not
`ERR_LIMIT` for the reason its own row gives — no level list in this game has a ceiling, so a shut
level gate is always a gate rather than an exhausted supply. Both leeway codes are `ERR_LOCKED` and
not `ERR_LIMIT`: research leeway is a gate the game opens as the requirement level moves, not a
supply the caller spent.

`ERR_REFUSED` is the `native_*_refused` family and nothing else a producer can explain. A code that
lands there because this map has not met it is a defect in the map, not a new kind of no.

A feature result number is not a wire word: it names no axis a caller can act on, so an unmapped
native result reaches the wire as `ERR_REFUSED` with the producer's own sentence.

A check that **passed** carries no class at all, and no sentence either. There is no success code:
every affirmative producer word — `requirement_met`, `recipe_discovered`, `visible`, `ready`,
`can_buy`, `below_level_cap`, `below_research_cap`, `native_leeway_available`,
`queue_room_available`, `drain_available`, `output_capacity_available` — renders as a bare `yes`.

What each internal code means is below; the class is how it reaches the wire.

| Code | Meaning | Surfaces |
| --- | --- | --- |
| `already_maxed` | The target has no level, use, or purchase left to buy | `game_purchase`, read-side develop and purchase decisions |
| `cannot_level` | The game's own per-type level gate is shut. `game_level` has no ceiling code because none of its four types has a ceiling: every one answers `ILevelable.CanLevel()` unconditionally true, so an exhausted level target is not a state this surface can reach | `game_level` |
| `unaffordable` | One or more named resources fall short. The sentence names every one of them: `Needs <cost> <Resource> (have <held>); …` | every purchase-shaped mutation and every read-side cost decision |
| `amount_unavailable` | The exact amount asked for exceeds what this call admits, and a smaller amount is what fixes it. Carries `maximumAmount` | `game_research develop`, `game_concept`, `game_equipment`, `game_alchemy`, `game_agromancy` |
| `not_active` | The target has nothing active to remove, so no amount succeeds. It used to share `amount_unavailable` with three refusals a smaller amount does fix | `game_agromancy` removes |
| `automation_full` | Every automation slot on the queue is in use. Only a queue genuinely out of room answers this; an undiscovered recipe answers `hidden_or_undiscovered` | `explain_entity`/`world_get` crafting rows, `game_craft automate` |
| `switch_blocked` | The game refuses a loadout swap right now (`LoadoutManager.CanSwapLoadouts()`) | `game_loadout select`, the `canSelect` read |
| `saved_entry_unavailable` | A saved snapshot's stored entry cannot be restored | `game_loadout snapshot_save`, `game_loadout snapshot_load` |
| `slot_empty` | The named snapshot slot holds nothing to load or clear | `game_loadout snapshot_load`, `game_loadout snapshot_clear` |
| `slot_occupied` | The named snapshot slot already holds a record; clear it before saving over it | `game_loadout snapshot_save` |
| `slot_out_of_range` | The slot index is outside the live snapshot list. Carries `minimumSlot` and `maximumSlot` | every `game_loadout` snapshot mode |
| `active_section_empty` | Nothing is staged in the active Equipment or Alchemy section, so there is nothing to save | `game_loadout snapshot_save` |
| `screen_match_failed` / `subtab_match_failed` | The exact label matched zero or several live entries | `game_navigate` |
| `no_pending_target` | No target selection is open. The verb exists and the submitted target was never the problem, so no entity-ownership hint refines it | `game_targeting` |
| `requirements_unmet` / `research_leeway_exhausted` / `already_developing` | The develop gate the read side already names, on the mutation that hit it | `game_research develop` |
| `recipe_has_no_core_glyph` / `core_glyph_not_published` / `core_glyph_not_owned` / `core_glyph_not_leveled` / `core_glyph_augments_only` | The one thing wrong with the recipe's core glyph, replacing the single `core_glyphs_unavailable` that covered all five | `spell-recipes` loadout-add decisions |
| `native_not_discoverable` | The game never offers this entity a discovery action | `discover` decisions, pool-unlocker glyphs |
| `projection_refused` | The suite's own resource-rate policy refuses the assignment; the game did not | `game_concept` |
| `native_rejected` | The game refused and the published world does not explain why | any native mutation, reserved for exactly that case |

`native_rejected` is the last resort, not the default: a refusal the read side can already account
for answers with that account's own code. A mutation refused by a gate the read side already
explains adopts that read's code and words rather than inventing a second name for it —
`game_research develop` answers `already_maxed`, `unaffordable`, `requirements_unmet`,
`research_leeway_exhausted`, or `already_developing`, which are the research row's own.
`investment_unavailable` is retired — the game never
consults the resource fill list for develop admission — and `amount_unavailable` is the name for an
exact-amount over-ask.

`maximumAmount` is the one name for that ceiling on a read and on a refusal alike, so a caller
comparing what a row offers against what a refusal names is comparing one number under one word.

`maximumAmount` is the largest `amount` **this one call** admits, re-derived from live native state
every call. It is never a remaining budget, and a later call routinely admits more: an idle game's
income moves affordability between calls, and some caps are structural per action rather than a
supply — with Research Queue Mode off, one `game_research develop` starts exactly one development,
so its `maximumAmount` is 1 no matter how many levels are actually within reach. Every refusal
carrying the field says in prose which of the two capped it, because a caller that read "at most 1"
as a budget stopped five admissible calls short. Read-side `maximumAmount` (consumable stock, an
agromancy add, an equipment equip or unequip) carries the same meaning for the next call.

### Where a bound comes from

Every numeric input has two different kinds of limit and they are not interchangeable.

A **native bound** is the game's own limit on a control, read live from the native member that owns
it. Native bounds are published in pairs — a value never ships with only its ceiling — and appear in
both the read and the committed response: `casting.output`/`casting.reserve` carry `current` and
`maximum`, a ritual's `setLevel` carries `minimum` and `maximum`, a snapshot slot refusal
carries `minimumSlot` and `maximumSlot` read from the live list the sentence was written from, and
`maximumAmount` is the live per-call admission ceiling described above. A caller can act on these:
they are what the game will accept this instant.

Two floors are the exception and are named here rather than left to look like the rest. The ritual
`setLevel.minimum` and the two `casting` dial minimums are all the constant `1`, held by the suite
and matching the control the player presses rather than read from it each time. Only one of them
still rides an answer, and the split is deliberate: the dial floor left the overview and the dial
commit for the *Casting dial loop* tool doc, because a number that is 1 in every save on every call
is documentation; the ritual `setLevel` pair stays whole on the read, because both ends together are
what tells a caller that 0 is not a starting level the game offers. Both controls are a
`UIValueSelectButton`, whose floor is the `minValue` its `SetClamp(min, max)` stores and whose
decrement is `Math.Max(value - change, minValue)`; the ritual screen passes the literal `1`, which
the contract gate pins, and the casting dials take theirs from a prefab-serialized clamp that no
audit can read without an asset dump. So the shape of the bound is audited and the ritual value is
quoted, while the dial value rests on every value selector in the game flooring at 1. Anything else
the suite publishes as a bound is read live.

**Every slot, position, and destination on the wire counts from 1**, in arguments and in responses
alike: the first spell slot is slot 1, the first snapshot is snapshot 1, the first loadout is
loadout 1, and the first queue entry is slot 1. Every screen in the game counts the same way and no
screen shows an array index, so nothing on the wire does either. Internally every list stays
zero-based; the conversion happens only where an argument arrives and where a response is written.
A slot refusal states both sides — the slot asked for and the slots that exist or hold something —
so the retry needs no second read.

A **schema bound** is the range the JSON input schema declares, and it is the suite's own policy on
what is worth sending in one call — not a native fact. `game_purchase` and `game_level` cap `amount`
at 1,000, `game_concept` at 1,000,000, and `game_agromancy` at 10,000; the paging tools cap `limit`
at 200 and `game_screenshot` caps `maxWidth` at 4,096 for reader cost; every other `amount`,
`slot`, `offset`, and dial `value` — `game_alchemy` and `game_equipment` among them — declares no
ceiling at all, because the suite has no opinion there and the native bound decides. Where the suite
has no ceiling it publishes none: the schema omits `maximum`, and a below-floor value is refused
with "must be N or greater" rather than with an `int.MaxValue` placeholder printed in the shape of a
bound the game never chose. None of the declared ceilings is read from the game, none of them is a
running budget, and none of them appears in any response. A value inside the schema bound is
therefore not admitted yet: the action boundary re-reads the native bound and refuses with
`amount_unavailable` and the live `maximumAmount` when the two disagree.

The two kinds never mix in one number. A published bound quotes the control or it does not ship, and
a schema ceiling is never folded into one: an
agromancy `maximumAmount` is the game's remaining-instance count alone, never that count
clamped by the tool's per-call ceiling, because a blend of the two is a third number that answers
neither question. The consequence is that a published native bound is not always a sendable amount:
a busy plot can advertise a `maximumAmount` above `game_agromancy`'s 10,000 schema cap, and the
over-ask is refused at schema validation rather than admitted and then refused natively. Send the
smaller of the two and call again.

#### The bound vocabulary, and its one JSON type

The table below is the whole vocabulary and no name outside it exists. Each answers a different
question, so none of them is a synonym for another:

| Name | What it bounds | Where it appears |
| --- | --- | --- |
| `minimum` / `maximum` | a control's live range — a native dial, or a writable setting's declared domain | the read, the commit, **and** the refusal, in the same object as the value they bound |
| `minimumAmount` / `maximumAmount` | the `amount` this one call admits, and on agromancy and harvest reads the game's remaining-instance headroom, never clamped by a schema cap | refusals and read-side decision blocks |
| `minimumSlot` / `maximumSlot` | the `slot` index the live list holds | every `game_loadout` snapshot mode |
| `maximumDestination` | the `destination` index a move accepts | `game_alchemy` and `game_spell_loadout`, read and refusal alike |
| `maximumBatch` | how many levels one queued develop would take, the multi-buy target clamped by the queue's own room | `game_research`'s develop block, queue route only |

A decision block carries a bound exactly when the verb it decides takes the input that bound caps.
`game_consumable discard` publishes `maximumAmount` because `discard` takes an `amount`; its sibling
`use` publishes none because `use` takes none — one call, one consumable. Which arguments a mode
accepts is declared once, in that tool's `inputSchema` mode rules, and `use` lists `amount` as
forbidden there; a decision block is where the game's live answer lives, never a second copy of the
call signature.

Two shapes were retired rather than joined: a bound named only in an English sentence, and a
JSON-RPC `-32602` text quoting an `int.MaxValue` placeholder as if it were the game's limit. A
sentence that names a ceiling now ships that ceiling in one of the fields above, and a schema
message states only the floor it actually declares, leaving the ceiling to the game and to the
refusal that names it.

**One quantity, one JSON type.** A key that can carry a `BigDouble` — a resource amount, a price, a
rate, a stored capacity — is the game's Scientific string on every surface, even where one category
happens to hold that value in an `int`: a caller must not have to know which category it is reading
to know the shape of `amount`. A key that can only ever carry a bounded cardinal — a level, a slot,
a stack, an instance count, a per-call admission ceiling — is a JSON number on every surface. A
ceiling follows the value it caps, so `maximumCarry` is a string beside `amount` while
`maximumAmount` is a number beside `minimumAmount`, and a range never states its two ends two ways.
`maximumAmount: "240"` beside `minimumAmount: 1` was that defect and is gone. A ceiling therefore
also takes the *name* of the magnitude it caps rather than the bare bound name: `game_equipment`'s
weight budget publishes `maximumCarry` beside `used`, because `maximum` is spoken as a number
everywhere else and one key cannot be both. Where a declared domain does ship as `minimum`/`maximum`
— a native dial, a writable setting — it states both ends in the setting's own type, so an integer
setting's range is two integers.

### Presence semantics

A field or collection is absent when the suite did not collect it, and the response says so with a
named `…Unavailable` fact rather than by silence. A collection that was collected and is genuinely
empty is present and empty.

An identity is a handle and a name, and nothing else. The asset name (`internalName`), the runtime
type (`nativeType`), the category the type implies, and where a name came from (`nameSource`) are
catalog-browsing facts: `entity_catalog` and `explain_entity` publish them, and no world row or
reference carries them. Stamped on every identity they cost 21.1% of one live round for a fact
nothing on that round read.

Absence therefore never doubles as a value. Every key that once used it to mean "no" now says so:

| Key | Absent means | Present-and-false/empty means |
| --- | --- | --- |
| `predicates.<slot>` | the predicate does not apply to this entity | published with its `value` and `reasonCode`, passing or not |
| `affordable` | the row has no price to be short of — an exhausted upgrade, or a row the world publishes no cost for | `false`: the named resources fall short |
| `discover` | nothing: every glyph carries the block | `available:false` with the reason, including `native_not_discoverable` for a glyph the game never offers |
| `maxLevel` / `remainingLevels` | the entity is uncapped — a negative native maximum — on `world_list` and `world_get` alike | a real ceiling and the distance left to it |
| `queuedLevels` | nothing: every level-bearing row carries it | `0`: nothing is in flight |
| `lastRun` | the game retains no record of a run: a cleared `wavesCompleted` with no spoils is the state a ritual nobody has played is in, and `RitualSO.IsFailedRun()` is `wavesCompleted < 5`, so an unconditional verdict would call every untouched ritual a failure | a finished run's `result`, `wavesCompleted` and `spoils` |
| `spoils` | on a row that is `inBattle`, that the running battle has banked nothing yet. On one that is not, see `lastRun` | `[]` on `game_ritual end`: the run banked nothing |
| `openModals` | **deliberate progressive disclosure**: no modal is covering the board. `openModalsUnavailable` with a reason appears when the read itself failed, so silence is never a failed read | the game-written title of every open `UIModal` |

`openModals` is the one key whose absence is still a value, and it is a documented choice rather
than a gap: an empty array on every screen read would spend bytes on the ordinary case to describe
the rare one.

A committed purchase reports the two counts the screen owns, `level` and `queuedLevels`, each as a
`{before, after}` pair. Which one moved is the answer: a level that lands immediately moves the
badge, a level that has to be built moves the queue and leaves the badge where it was. Publishing
their sum under one name — the retired `committedLevel` — put a number on the wire that no screen
shows and hid which of the two the purchase actually did.

A committed purchase reports the levels it bought and nothing about what it charged. It used to
carry `paid[]` and `costPerLevel[]`, and both were bookkeeping rather than an answer: the paid rows
priced the game's own multi-buy setting at capture time rather than the count the call turned out to
commit, so an `amount=25` call reported one level of a rising ladder as though it were the whole
charge — understating spend, and understating it in the direction of believing there is more left.
`level {before, after}` is what says how many levels were bought. What a level costs and what the
next one asks are read where the whole curve lives, on `world_get` and the `purchase-costs`
category, which is also where Auto Buy plans from. A refusal that could not afford something still
names every short resource, its price, and what is held, in its own sentence.

`spendableAmount` still rides a cost row on the read surface, in the same spendable coordinate every
cost row uses. It does not have to equal the same resource's `amount` on a `resources` row, and
where the two differ it is not drift: `spendableAmount` is spendable amount, which a **bandwidth**
resource reports as the headroom left rather than the stock held, while a row's `amount` is the
displayed quantity, which an **inverted** resource reports counting down from its cap. The two
traits are independent, so either key can be the larger one depending on which traits the resource
carries. When the settled world publishes no row for that resource the key is absent and
`spendableAmountUnavailable` names the reason, because zero is a balance and "not collected" is not.

There is no delta field: the difference between two worlds also contains every income stream and
every other spender in that window, so it is not a price and is not computed.

A tool result is one page of text in `content`, emitted once, beside any inline media such as a
screenshot; success omits the false `isError` default. The server publishes no `structuredContent`
duplicate of the same answer, avoiding a second client-side parse and text-channel truncation.
Invalid arguments return all detected schema
shape errors together under `error.data.validationErrors`, with distinct `missing_required` and
`unexpected_field` codes, and `error.message` names the offending fields because that is the part
most clients show the caller.

A faulted GameAction is still a completed MCP tool invocation: it omits `isError`, and its domain
verdict line — status, class, and the actionable sentence — plus one relevant fact remain on the
page.
`isError=true` is reserved for infrastructure failures that happen before a canonical action
terminal exists. This distinction prevents clients from replacing the domain result with an opaque
generic tool error.

The server waits up to 2,000 ms for Unity to claim a request. If the request is still pending, it is
atomically canceled as `request_canceled_before_claim` and can never execute. Once Unity has claimed
it, the operation owns execution and the worker waits for the real terminal result; a local timeout
cannot precede a hidden later mutation. There is no pending fallback.

No schema accepts `worldGeneration`. An operation pins its current world internally, actions
revalidate live identity and mutable facts at the GameAction boundary, and the generation counter
never becomes caller ceremony.

Where a target UUID is supplied, the server derives its native type and action kind from that UUID;
where components are supplied, it derives the target from the live resolver instead:

```sh
tools/game-mcp-client.py call game_purchase --arguments \
  '{"uuid":"ATTRIBUTE_OR_UPGRADE_UUID","amount":1}'
tools/game-mcp-client.py call game_agromancy --arguments \
  '{"mode":"add_plot_action","uuid":"PLOT_UUID","actionUuid":"PLOT_ACTION_UUID","amount":1}'
tools/game-mcp-client.py call game_agromancy --arguments \
  '{"mode":"add_element_action","uuid":"HARVEST_ELEMENT_UUID","actionUuid":"HARVEST_ACTION_UUID","amount":1}'
tools/game-mcp-client.py call game_cast --arguments \
  '{"mode":"fire","slot":1,"uuid":"SPELL_UUID"}'
tools/game-mcp-client.py call game_cast --arguments \
  '{"mode":"toggle_off","slot":1,"uuid":"SPELL_UUID"}'
tools/game-mcp-client.py call game_discover --arguments \
  '{"mode":"preview","surface":"spellcraft","components":[{"uuid":"GLYPH_UUID","count":2}]}'
tools/game-mcp-client.py call game_discover --arguments \
  '{"mode":"offer_select","uuid":"TREE_UUID","offerUuid":"OFFER_UUID"}'
tools/game-mcp-client.py call game_casting_dial --arguments \
  '{"dial":"output","value":4}'
tools/game-mcp-client.py call game_spell_loadout --arguments \
  '{"mode":"staged"}'
tools/game-mcp-client.py call game_spell_loadout --arguments \
  '{"mode":"preview","uuid":"SPELL_RECIPE_UUID","glyphs":[{"uuid":"GLYPH_UUID","count":2}]}'
tools/game-mcp-client.py call game_spell_loadout --arguments \
  '{"mode":"add","uuid":"SPELL_RECIPE_UUID","glyphs":[{"uuid":"GLYPH_UUID","count":2}]}'
tools/game-mcp-client.py call time_challenge --arguments \
  '{"mode":"select","uuid":"CHALLENGE_UUID"}'
```

`game_discover`'s offer modes require the tree `uuid`, require `offerUuid` for `offer_select` and
`offer_confirm`, and reject it for `offer_initiate` and `offer_reroll`; `surface` and `components`
are rejected for every offer mode, and `uuid`/`offerUuid` are rejected for `preview` and
`confirm`. Initiate and reroll verify the exact tree/type and immediate transition to Crafting;
select verifies the requested offered UUID became selected; confirm verifies that exact UUID became
discovered. Payment deltas, reroll values, counters, flags, timers, list cleanup, and selection
cleanup are neither outcome gates nor response data. This matters when a cost is below the ULP of a
very large `BigDouble` amount: an unchanged amount cannot disprove a transition the game visibly
performed. On success, payment is presumed and completely omitted. Initiate/reroll wait for the
ordinary collector to publish the Crafting state their press produces; the offer list is filled by
the tree's own timed increment three seconds of game time later and is therefore outside any settle
budget. Select returns the selected state. Confirm permanently spends a discovery choice, so it
names the discovery it took — the identity the caller passed as `offerUuid` — and moves
`discoveredCount` and `mode` as pairs, with whether the tree still has discoveries left. It does not
re-send the next initiate price: the tree answers that when a caller asks to initiate again.
Failures name only the failed admission or missing transition and the fact that explains it: a
reroll refused for a spent budget names that one axis and carries `rerollsLeft`, never a recital of
the preconditions the code believes it enforces.

`game_cast` uses the visible spell button's native route. `fire` starts a ready spell, `release`
lets go of the suite's charge hold, and `toggle_off` presses an already-active toggle spell again.
The last mode requires the slot still to contain the exact recipe UUID and native `Spell`, the spell
still to be a currently casting toggle, the visible cast button to remain available, and the
player's Cancellable Spells setting to allow the press. Its one outcome sentinel is the native
casting state changing from active to inactive. The settled response is only the named recipe,
slot, and settled `active` state; a refusal names the binding setting or live spell
state. Detailed `spell-slots` rows expose `toggleOff.available` so the setting never has to be
learned by attempting the action, and carry `casts`, the game's own per-spell manual cast counter.
A settled `fire` carries that counter as the total the game holds, not as a pair: the game writes it
when a cast *completes*, frames after the press, so a pair taken at the press was two readings of
the same number on every fire. A press at a spell that is already running is refused rather than
committed silently — the game's own button answers it with a warning popup or an end-of-cast, never
with a new cast — so a repeated fire can never look like a firing loop that is doing nothing. A
running spell reports `active`, whether it is a toggle or not; an idle one-shot carries no `active`
key, because it has no running state to report.

`game_casting_dial` requires `dial` plus a positive `value` and takes no UUID at all, because both
Output Level and Reserve Level are single global variables. The boundary reads the exact global
variable and its purchased maximum on the Unity main thread, rejects a value outside that live
range, and verifies that the requested value became observable. Success is the exact requested
global value; a committed result names the `dial` it moved — the screen has two — plus its `before`
and `after` value and both bounds.

`game_spell_loadout` requires `mode`. `staged` is a request-scoped main-thread read with no other
arguments; it reports the exact current core/augment selection and never mutates it. For `preview`
and `add`, `uuid` is a spell-recipe
identity and an explicit `glyphs` array is required. Preview combines the recipe's authored core
with those explicit augments and prices the resulting layout through
`SpellManager.GetSpellCreateCost` without changing the staged UI
selection or acquiring mutation ownership. For `remove` and `move`, `slot` names the loadout-bar
position and `uuid` and `glyphs` are rejected; `move` additionally requires a `destination`, which
no other mode accepts. Add builds the native candidate, applies the selected level, bakes the glyph layout
with `Spell.SetAugmentGlyphs` before the manager add route, pays last, and verifies the exact
requested loadout outcome. Remove rechecks the game's live `Spell.CanRemove()` verdict; move
re-resolves the source slot and invokes the same native swap-plus-notify path as the spellbook.
Success is the exact added instance, exact target absence, or the exact target at its destination.
A committed result returns only the recipe identity and slot change; a failure names only the unmet
admission or missing outcome.

`game_targeting` has two conditional shapes. `submit` requires one target `uuid`; `randomize` rejects
it. Submit re-resolves that UUID within the live native candidate list and reruns the request's
native target verdict immediately before mutation. Randomize invokes the game's own random choice
and immediately submits that result; it is not a candidate-only shuffle. Success is exact
submitted-object identity plus retirement of the original request. A committed result names the
submitted target; a failure names only the rejected target or missing submission.

`game_consumable` has five conditional shapes. `use` and `cancel` require only a
consumable `uuid`; `discard` also requires positive `amount`; `set_randomization` requires
`enabled`; and `move` requires `list` plus `destination`, counted from 1. Fields belonging to another
mode are rejected. The boundary re-resolves the exact `ConsumableSO`, all live verb predicates,
and the current list/source/destination on the Unity main thread, then captures the shared
ConsumableUse/MultiBuy permit last. Success is the requested queue, exact usage cancellation,
clamped holding removal, randomization flag, or exact destination. Payment and downstream effect
accounting do not gate success; a committed result returns only the changed amount, flag, or slot.

`game_craft` requires one recipe `uuid`. Its optional mode defaults to `craft` for compatibility and may be
`craft`, `automate`, `cancel_manual`, or `cancel_automation`. `craft` re-resolves the exact
recipe, authored page/queue route, native purchase amount, affordability, and room on Unity's main
thread, then captures the shared crafting permit last. Direct recipes invoke native
`CraftingRecipeSO.Execute`; page recipes re-drive the audited stack/new/instant
`UICraftingPage.QueueCraft` sequence.
A committed `craft` reports only what the settled world shows: a completed direct or instant craft
returns `completed: true`, and a queued craft returns the queue growth the settled world confirms.
When the settled queue count is unchanged, the response stays committed and returns
`postStateUnavailable` with reason code `post_state_not_observed` naming the count it read — an
unmoved count is the same number before and after the craft and is never published as its delta.

The other modes use the same authored page relation and exact recipe identity.
`automate` repeats the UI's native multi-buy and automation-quantity calculation before calling
`CraftingInstanceListVariable.AutomateCraft`; the two cancel modes call the exact manual or
automated instance route shown by the UI. `cancel_automation` passes the negated multi-buy amount,
matching the game's own automation strip, because the native control adds whatever it is given. The
recipe row publishes manual queued amount and the
automated quantity/capacity needed for the next decision. Success is one settled change, published
as `amount: {before, after}` in the strip's own badge coordinate — one `cancel_automation` on a
doubled entry moves the badge 8 to 4 while the repetitions behind it move 4 to 3, so the response
names the number the screen shows. Refund accounting is neither computed nor used as a gate. A fault
that already moved the native automation quantity reports it under `observed` as `repetitions`,
which is the coordinate that call returns, so a caller is never invited to retry into more damage.

`game_discover`'s composition modes require `surface` plus `components` and accept no target UUID.
`preview` resolves the
component multiset against the published roster for that one surface and returns the single named
output, its costs, holdings, affordability, and blockers; it is a read and mutates nothing.
`confirm` derives the target the same way from the admitted immutable world, then rereads both
native recipes and every exact live component before repeating native visibility,
already-discovered, `CanDiscover`, exact cost, and affordability checks on Unity's main thread. It
captures the shared family permit last, then preserves the UI's `PerformCost`-before-`Discover`
ordering. Success is the exact resolved target becoming discovered and returns that named target
plus its discovered transition and surface. It carries no receipt or payment stanza. A refusal occurs before payment and
restores any temporary UI selection staged for native resolution. A fault names the single missing
discovery outcome. A composition that resolves differently than the caller expected is
preview or refusal evidence, never a wrong-target mutation.

`game_equipment` requires `mode` and one published equipment `uuid`. On Unity's main thread it re-resolves
the exact artifact and repeats creation, current stacks, global and primary-type slot room, maximum
stacks, and native usage-affordability checks before taking the family permit. The caller's explicit
`amount` must fit that live maximum exactly; an oversized equip or unequip refuses with the maximum
the current state permits and never clamps to a different mutation.
Success is only the exact requested target-stack transition. It returns the target's stack count
before and after, with no receipt or payment/usage stanza. A missing transition
faults that attempt; a throw after the exact transition commits.

`time_challenge` requires one of `select`, `queue`, `abandon`, `reroll`, or `state`. The three
target modes require a published `ChallengeSO` `uuid`; `reroll` and `state` reject it. `queue` is
what the screen's "activate" button does — the challenge starts at the next reset, not now — and
`abandon` names the only state a challenge can be abandoned from, the one a reset started. The
boundary rereads the exact manager/list graph and target state on Unity's main thread, checks offer
membership, selection room/restrictions, active/queued state, world-cycle completion, and rerolls,
then captures the `ChallengeLifecycle` permit last. Select verifies exact membership inversion;
queue verifies the exact idle/queued toggle; abandon verifies the exact target becomes failed.

Selecting past a full list is two presses on the game's own screen — give up a row, then take the
one you want — so `select` performs the first press itself when exactly one selection is held, and
verifies both halves: the row asked for is held and the row it took over is not. When more than one
is held, which to give up is the caller's choice, and the refusal says that rather than reporting a
full list the caller cannot act on. The type conflict is a question about the selection as it
stands, so on a swap it is asked between the two presses, exactly where the screen asks it: a row
whose one-instance type the given-up row was holding is selectable, and a conflict the give-up does
not clear refuses with the given-up row pressed back, so a refused swap costs the caller nothing.

`reroll` presses the game's one new-challenges button. The Time screen and the Reset modal are two
labels on the same control — one budget, one asset, one list — so there is one reroll mode rather
than two that spent from the same purse. The first press of a world cycle sets the fetched flag and
is free; every press after spends one of the limited rerolls, and `reroll.costsReroll` on
`time_challenge(mode="state")` says which the next press would be without pressing it. The first
press verifies the game's fetched flag; later presses verify that the reroll count decreased, and
both publish `rerollsLeft` and `challengesFetched` as `{before, after}` pairs whether or not they
moved. What the press hands back is not a gate: the eligible pool can be small enough that an honest
redraw returns the same offers in the same order, so `changed` says whether the offer list moved and
an identical redraw is a landed press rather than a failure. A press that was attempted and then
failed carries that same `rerollsLeft` pair, because the game's button spends before it asks for
offers and a caller must never infer a spend from an absent key. A refusal that never pressed
instead turns on one axis and carries that axis as the number it read — a spent budget answers
`rerollsLeft: 0`, the plain integer the read publishes, because nothing moved for a
pair to record. Offer contents, rewards, effects, and other accounting are neither success gates nor
response data. A reroll also returns the new named offer state because it is the next decision; target modes
return the changed challenge state. No success receipt or follow-up read is required.

`time_prestige` requires `confirm:true`. The boundary rereads the reset manager's world-cycle
completion and challenge-fetch flags plus the persistent reset count on Unity's main
thread, then captures `PrestigeLifecycle` ownership last. The public native method merely schedules
the operation behind a screen fade, so MCP invokes the exact audited private transaction directly;
that transaction performs persistent-state preservation/reset, activates queued rewards and
prestige challenges, updates the persistent resource, and reloads the scene. Success is gated only
by the exact lifecycle replacement. Resource and counter movements are not ledger gates. A native
throw or a returned transaction without lifecycle replacement faults that attempt.
After success, the response waits for the newer post-reset world and carries its lifecycle
generation and scene plus complete prestige and challenge next-decision state, with no receipt,
payment stanza, or read-back call. The wait is a lifecycle budget of fifteen seconds rather than the
one second every other verb settles on, because a reset tears the world down behind a native scene
fade and rebuilds it. If the world still has not landed, the answer carries the lifecycle generation
on both sides — the reset's own identity — so a caller learns the reset happened and that only the
republished world is still owed, and it names `world_overview` as where to read it.

`game_research` requires `mode` plus one published `ResearchSO` `uuid`. Modes are `develop`,
`pause`, `resume`, `cancel`, and `bonus`. The boundary re-resolves that exact identity and rereads
queue mode, multi-buy, level/cap/range evaluators, exact cumulative costs, current state,
investment/progress, and free bonus capacity on Unity's main thread before capturing the
`ResearchLifecycle` permit last. Develop calls the same native `PurchaseLevel` dispatch as the UI;
pause, resume, cancel, and bonus call their exact native methods. Success is only the requested
identity/outcome: active development or increased total queued levels, paused/resumed state, idle
with an empty queue, or one added self-bonus level. Cost, investment, resource, type-counter, and
progress-clock movements never gate success. A missing transition faults that attempt; a throw
after the exact outcome commits. A committed `develop`, `pause`, `resume`, or `cancel` returns the
two facts those verbs move — `state` and `queuedLevels`, in the same queue coordinate the research
row publishes — and carries `totalLevel {before, after}` only when the level count genuinely moved,
which is what a level finishing inside the call looks like. A level takes research time, so an
unconditional level pair on a develop reported the same number twice while the queue it grew went
unpublished. `bonus` returns its own `bonusLevel` pair. No mode returns a receipt, payment stanza,
or read-back.

No MCP fault installs a persistent family quarantine. A faulted or refused request leaves nothing
behind: the next call returns to the same action boundary and revalidates current identity, native
type, availability, affordability, and mutable state from scratch. Automation's own quarantine
semantics are unchanged and are not shared with these player-driven wrappers. For the same reason,
MCP gameplay tools are registered from runtime capability admission rather than automation
configuration, so disabling Auto Scribe or any other feature cannot make the corresponding manual
verb disappear.

CLI play commands therefore need no generation option:

```sh
tools/game-mcp-client.py purchase UUID
tools/game-mcp-client.py cast 0
tools/game-mcp-client.py harvest PLOT_UUID
tools/game-mcp-client.py concept-add UUID
tools/game-mcp-client.py spell-level UUID
```

Manual MCP actions do not require a worker policy to be enabled. They do use the same shared
GameAction as any feature consumer, a cooperative action-family lease, live validation, and
mutation proof.
STOP closes MCP native admission exactly as it closes automation. Resume still requires the host's
ordinary fresh-world gate.

`suite_configuration` returns every writable setting as one `section/key: value` line and nothing
else. It never reflectively serializes the runtime configuration record or exposes compiler metadata
and internal nested policy objects.

`mode="describe"` is where the rest lives: each setting's type, the values it accepts, and the
sentence saying what it does. Those three do not change between calls, so the ordinary read does not
carry them — a caller reading current values pays for values. The accepted values are said the same
way whichever kind they are, a range for a number and the list of names for an enum, so no caller
has to learn two spellings of "what may I write here".

`suite_config_set` commits through `AutomataConfigurationStore`, the same single publication path
as the in-game controls. BepInEx
parse/domain validation runs before publication. Compatibility acknowledgements, shortcuts, and
STOP are not generic writable settings. A commit returns `setting.value` as a `{before, after}`
pair, the same shape `suite_automation` returns `on` in, because what a write changed is the pair
and not the endpoint. A write refused for its domain returns the setting, the `requestedValue`, and
the declared range as `minimum` and `maximum` read off the entry itself — BepInEx's own
config-file wording is never spliced into the sentence, so the surface no longer says
"must be From 0 to 60".

`suite_automation` is the seven green/gray automation buttons as booleans, because that is what
they are: `auto_buy`, `auto_cast`, `auto_concept`, `auto_harvest`, `auto_items`, `auto_scribe`, and
`mentor` are each a `{Disabled, Active}` setting with no third state. `mode="list"` returns every
feature as `{feature, name, on}` and takes nothing else; it also carries the two suite-wide
switches when either is silencing all seven, because a list of on buttons would otherwise answer a
different question than the caller asked.

Both switches are present exactly when they are overriding, and never otherwise:
`automationEnabled: false` appears exactly when the suite's global automation toggle is off, and
`emergencyStop: true` exactly when the stop is engaged. Neither key ever ships in its ordinary
state — there is no `automationEnabled: true` and no `emergencyStop: false` on this tool, because
an override that is not overriding is not a fact about the buttons. `suite_health` is the one place
that reports the stop in both states, since its whole job is to say what the suite is doing.

`mode="set"` takes exactly one `feature` and one `on`, writes through the same
`AutomataConfigurationStore` path `suite_config_set` uses, and returns the named feature with its
`on` before/after plus those same two override keys under the same condition — a caller who turns a
feature on under an engaged stop reads it in the answer to the write, not on a later `list`.
Setting a feature to the state it already holds is refused as
`already_in_requested_state` rather than committing nothing. Everything else these features can be
configured with — thresholds, roles, allowlists, reserves — stays on `suite_config_set`, which
writes the same entries the same way, and `suite_emergency_stop` still overrides all seven at once.

## Shapes that must not regress

Every live round ends with a fresh-context critic reading the raw wire, and each one names the
shapes that earned their keep as well as the defects. Those survivors are a standing contract: a
change that would undo one is a regression even when it is locally tidier, and the round that wants
to touch one argues for it first. Each line names where the shape is specified.

1. Settled-delta pairs as the single mutation sentinel: a commit answers with the facts its own
   press changed, each as `{before, after}` — *Inline action results*.
2. Stop-in-same-answer: every action, configuration write, STOP transition, and gadget returns its
   terminal result in the same call. No receipts, no cursors, no polling tool — *Inline action
   results*.
3. Sentences with both sides: a refusal that names a slot, a position, or a ceiling also names what
   exists, so the retry needs no second read — *Where a bound comes from*.
4. Remedy-naming: a refusal names the fix, not only the fault — *Refusal vocabulary*.
5. Needs-and-haves: an unaffordable refusal names every short resource, its price, and what is
   held — *Refusal vocabulary*.
6. Refusals that name the responsible **game** setting rather than a suite number — *Refusal
   vocabulary*.
7. A refusal class from the fixed set of eight, never a private code per refusal, and never a code
   on a check that passed — *Refusal vocabulary*.
8. `lastRun` separation: a ritual nobody has played and a run that finished badly are different
   answers, never one verdict — *Presence semantics*.
9. Lifecycle triple-agreement: `suite_health`, the world readers, and `game_probe` read one
   `lifecycleState` and cannot hold three beliefs about whether a game is running — *Trace health
   and probes*.
10. The four not-running reasons: a lifecycle that is not `Playing` answers `lifecycle_no_game`,
    `lifecycle_initializing`, `lifecycle_resetting`, or `lifecycle_scene_exit` rather than serving a
    destroyed run — *Trace health and probes*.
11. Paging exactness: `nextOffset` present exactly when more rows remain, and nothing else — *How a
    response reads*.
12. One answer, said once: a page of text and no `structuredContent` duplicate of it — *How a
    response reads*.
13. Constant-width entity handles that a caller can send straight back, and an ambiguous prefix that
    lists what it matched instead of guessing — *Entity handles*.
14. Every entity named where it appears, so no caller joins an id to a name — *Presence semantics*.
15. Fail-closed reads that name the tool which can answer (`checkWith`, `readWith`) — *Presence
    semantics*.
16. Honest, named collection gaps rather than silent under-reporting — *Tool surface*.
17. The `cost` / `spendableAmount` / `affordable` triplet in the screen's own spend units — *Where a
    bound comes from*.
18. The terminal discovery loop: initiate → read → select → confirm, each returning the settled tree
    — *Discovery decision loop*.
19. Schema-level guards on irreversible or run-killing inputs (`confirm must be true`, the ritual
    `level` floor), which make a dangerous call unreachable rather than merely refused — *Where a
    bound comes from*.
20. `game_navigate` returning the arrived screen's nested strips, inner to outer and byte-identical
    on a repeat — *Screenshots and navigation*.
21. `game_tooltips` scope discipline: a dismissed modal leaves the catalog, and `total` is stable
    across repeated calls on an unchanged screen — *Tooltip explorer*.

Four shapes this list used to protect are retired, and a round that reintroduces one is undoing a
ruling rather than restoring a contract:

- **Before/after echo pairs.** A pair whose two sides are equal reports no change; a press that
  moved nothing says what it did in one sentence instead — *Inline action results*.
- **The `next {…}` affordance block.** A commit answers with what its own press changed; the
  decisions that press reopened are read with `world_get`, where every other caller reads them —
  *Inline action results*.
- **Identity preambles.** The asset name, the runtime type, and the category the type implies rode
  every identity for 21.1% of one live round and nothing read them. They live on
  `entity_catalog`/`explain_entity` now — *Presence semantics*.
- **`paid[]` and `costPerLevel[]`.** A commit reports the levels it bought; what a level costs and
  what the next one asks are read on `world_get` and `purchase-costs`, where the whole curve is —
  *Presence semantics*.

## Screenshots and navigation

`game_screenshot` has no required parameters and returns an MCP `image` content block with
`mimeType: image/png`. A capture costs its reader whole 28-pixel patches — `ceil(width / 28)` x
`ceil(height / 28)` tokens — so pixels are the entire price and neither the image format nor its
compression enters it. That is why the PNG path is unconditional and there is no quality knob:
lossy encoding would buy wire bytes, which are free, at the cost of readability, which is not.
`maxWidth` bounds the encoded image between 320 and 4,096 pixels and defaults to 896 — 32 patch
columns, the narrowest width at which every class of on-screen text stays readable through the
suite's resampler, and the one control worth reaching for. The response reports the encoded `width`
and `height`, which are therefore the exact cost of what it just sent, plus `scene` and whatever
native modal is covering the board, and echoes nothing else. There is deliberately no crop or
region parameter: cropping risks removing what the caller actually needed, and seeing more of the
board than was asked for is how an agent notices what it did not know to look for.
`game_screenshot` and `game_navigate` both carry `openModals` — the game-written title of every open
`UIModal` — whenever at least one is open, and `openModalsUnavailable` with the reason when that read
is not possible. Neither field appears when the read succeeded and no modal is open.
`{"save":true}` additionally writes a server-generated, collision-resistant name under the current
trace folder. The caller supplies no basename, and there is no per-process filename cap.

```sh
tools/game-mcp-client.py screenshot --output artifacts/current-screen.png
```

`game_continue` is deliberately separate from tab navigation. On `Start` it invokes the audited
native `SaveStateManager.StartGame` method for the save the player has already selected. It cannot
select, delete, reset, import, or rewrite a save, and it accepts no native type, method, or UI input
from the caller. Its success waits for the transition and returns the new `scene` and
`runtimeAvailable` state.

The same load leaves the game in the shape every documented verb assumes: Research Queue Mode on,
Cancellable Spells on, and number notation `Scientific`. Each is the exact write the settings
dropdown performs on the settings variable the game reads back, verified afterwards, and skipped
when the game already holds it; nothing is persisted to the settings file, because the modal's
close is what persists and no modal was opened. Without them a queued develop and a spell toggle-off
refuse for reasons a caller cannot see coming, and the numbers the game draws stop matching the
numbers the wire carries. The response says nothing about any of it — an unattended caller should
never need to know a settings screen exists. A normalization that does not land is the exception:
it costs one `agent settings:` line on `suite_health` naming why, present exactly while the last
load's normalization is the one that failed and gone once a load succeeds or the run ends, because
the refusals it causes are otherwise unexplainable from the wire.

`game_return_to_menu` is the opposite lifecycle boundary. On `Main` it invokes the visible
`UIBackToMenuButton.BackToMenu` callback, which raises the game's authored manual-save event before
requesting the literal `Start` destination. Back to Main Menu lives inside a panel rather than on
the board, so the tool takes the player's whole route: with the panel shut it presses that panel's
own button first — the one panel whose contents actually hold the control, matched by containment
and never by caption — and then presses the control. A panel that will not open, or an opened panel
without one interactable control, refuses in a sentence that also states the panel is now open. The
response is completed as soon as the native screen
fade becomes active, before scene teardown can invalidate the HTTP operation. Its compact success
is `status: committed, scene: Start` — which controls the tool pressed to get there is how it drove
the UI, not a fact about the game; the scene transition then clears every lifecycle-retained
world, identity, binding, and lease through the ordinary lifecycle observer. The tool cannot choose
a save, suppress the save event, select another scene, or run while another transition is active.

There is deliberately no process-exit tool. Both installed quit entry points call
`UnityEngine.Application.Quit` directly and expose no game-written state that can be verified while
the process remains able to deliver an MCP response. See
`docs/reverse-engineering/clean-exit-boundary.md` for the audited drop.

`game_modal(mode="dismiss")` drives the visible close control on the one open native `UIModal`.
It refuses `no_open_modal` when there is no modal, `multiple_modals_open` when more than one makes
the target ambiguous, and `modal_close_not_ready` while the native grace period still disables
closing. The tool names no entity, so the
caller submits no lifecycle: the boundary reads the live lifecycle itself and pins it for the settled
read. The action invokes `UIModal.CloseModal()`, verifies
the game-owned closing flag, then watches that exact modal for up to the shared one-second
settlement bound. A completed close is the commit itself and says nothing further; timeout remains
committed and says the post-state is unavailable because the verified close already began. It does not click modal-specific confirm, purchase, reset, or
destructive buttons.

`game_screen_catalog` reads the live Main-scene UI. Top tabs retain native rail order. Current
subtabs are active `UIViewRadioButton` controls under the current native content area. Inactive
popup templates are excluded. The response is a structured `scene` plus ordered `screens`, each with
its `label` and `active` flag; the active screen additionally carries `subtabStrips`, where every
independent strip names its `active` label and its ordered `labels`. Unity hierarchy paths and
unstable numeric indexes are deliberately absent. Inactive tab content is not instantiated, and the
audited v1.0.5 data and scene assets do not carry an authoritative tab-to-subtab roster. The catalog
therefore omits inactive subtabs rather than navigating speculatively or guessing labels.

`game_navigate(screen, subtab?, uuid?, capture?, maxWidth?)` accepts exact labels only. Name matching is
ordinal and closed-world: zero or multiple matches reject with the exact candidate labels. Plot selection resolves
the supplied UUID as a published `PlotNodeSO` and invokes the one audited active
`UIPlotNodeList.OnNodeClick(PlotNodeSO)`. It is not a hardcoded Fruit Tree command.
For a compound request, the server selects the top screen, waits up to one second for the active
screen and complete live strip set to remain stable across frames, and only then resolves and
selects the requested subtab or plot. Resolving against the settled hierarchy is what makes the
subtab candidates the matcher searched identical to the ones the catalog advertises for that screen.
It then waits for settlement again before answering. A timeout stays committed but returns only `postStateUnavailable`; it never labels a
mid-transition strip set or capture as settled. The whole operation
still returns one terminal tool result; callers never split it into a retry sequence.
Mods is a suite-added screen, not one of the game's — see
[runtime architecture](../runtime-architecture/architecture.md#the-mods-screen-is-ours-not-the-games).
It uses that identical catalog-indexed button path. Selecting Mods while it is already active is
an idempotent screen reselect and leaves its page open; the MCP carries no Mods-only toggle case.

```sh
tools/game-mcp-client.py catalog
tools/game-mcp-client.py navigate World --subtab Agromancy \
  --uuid PLOT_UUID --capture artifacts/agromancy.png
```

When `capture` is true, the server waits until the destination has settled and returns the
PNG inline in the same terminal response, alongside the image's `width`, `height`, and `scene`.
Those fields are absent exactly when the caller asked for no capture: a requested capture that
cannot be encoded or stored fails the whole call loudly (`inline_screenshot_failed`,
`screenshot_budget_unavailable`, or `screenshot_budget_reached`) rather than answering without
them. Compound navigation captures exactly once, after the
final tab/subtab/plot selection; intermediate frames are never encoded. PNG size depends heavily on
the destination's visual entropy: the mostly dark Start screen compresses far smaller than the
dense Main HUD. MCP base64 then adds about one third to the PNG byte count, which explains why a
Main-scene navigation capture can be several megabytes even though it contains only one image.

## Tooltip explorer

The current game build makes the exploration loop feasible. Active `HoverTooltip` components carry
an `ITooltipable`, core name/type/description methods, and a private authored `subTooltips` list;
`OpenTooltip` renders the selected element. `game_tooltips` pages through current-screen elements by
a native hierarchy path whose sibling indices disambiguate repeated Unity clone rows. Its
scope is what the player can hover: the screen's own controls, the persistent chrome that outlives
navigation, and any open modal. Closing a modal only drops its canvas group's alpha and raycasts, so
every panel the session ever opened stays active in the hierarchy — the catalog reads the game's own
`UIModal.IsOpen()` up each element's ancestry and lists none of them, and `total` therefore counts
hoverable elements rather than instantiated ones.
A screen's elements descend from one canvas, so the catalog says the shared leading path once as
`pathPrefix` and each row carries only what that prefix does not already say. `pathPrefix` is
present exactly when the listed paths share leading segments, is the same on every page of one
screen, and always leaves the shortest listed path one segment of its own. `game_tooltip` takes a
row's path as handed out — it derives the same prefix from the same live screen — and accepts a
whole path as well; a refusal that has a prefix names it. The reply is compact plain screen text.
The
catalog includes the owning UUID when the assigned tooltip item is itself an identity-bearing game
entity; control-only rows retain the volatile current-screen path and name. The
reader walks the native node, linked-tooltip, nested-tooltip, and currently inspected-panel graph
on Unity's main thread, but its node structure, repeated paint, empty arrays, duplicate authored
text, and identical alternate tree are wire-internal ceremony and never ship. A cycle or hard
depth/node bound is rendered as one explanatory line rather than recursively expanding forever.
Unity rich-text markup is stripped. Computed text delegates run inline; the reader never clicks a
node, renders a panel, or captures the framebuffer.

```sh
tools/game-mcp-client.py tooltips --limit 25
tools/game-mcp-client.py tooltip 'PATH/FROM/CATALOG/ROW'
```

The audited manifest covers the native tooltip carrier/open/nesting shape, while the real-reference
build and installed contracts verify the source node graph that the prose renderer consumes. The
same audited `ITooltipable.GetDescription()` contract supplies authored descriptions for
`explain_entity` when the resolved entity implements that interface.

## Trace health and probes

`trace_health` answers operational questions that the strategist cannot answer from a world
snapshot: is the writer healthy, how many segments and records are retained, how many bytes are
being produced, and is retention or a writer fault active? It deliberately does not stream
individual automation decisions. The answer is compact text because it exposes no follow-up
handle. Individual decisions belong to the trace folder and offline analysis, where
high-volume repeated decisions can be filtered without spending strategist context.

`game_probe` has exactly three names:

- `runtime`: current Unity scene/frame/time scale, lifecycle state, gameplay readiness, and Mods
  shell liveness;
- `action_queue_room`: live `ActionManager.GetRemainingRoom()`, the native boundary answer that a
  published occupancy snapshot cannot guarantee; and
- `navigation`: live tab and active-subtab counts, useful for diagnosing catalog availability.

To add a probe, add one fixed name to the router schema and closed-world policy, implement its
Unity-main-thread branch without accepting reflection/member input, declare every native type and
member in the schema-3 manifest, add installed-contract and portable behavior tests, and document
why the fact does not belong in published `WORLD`. Facts that workers or strategists generally need
belong in audited world collection instead.

## SDK decision

The server retains its small protocol layer. The official ASP.NET Core MCP HTTP transport targets
modern .NET, while the game plugin is Unity Mono `netstandard2.1`. The low-level package would still
leave the suite owning `HttpListener`, the Unity frame-operation inbox, and inline image/action
plumbing while introducing a transitive runtime dependency set that the locked installer does not
ship. The present layer is therefore narrower and is covered by protocol tests; replacing it is
appropriate only when an official transport supports this runtime without a new deployed
framework.

## Troubleshooting

The log startup marker is:

```text
Game MCP streamable HTTP server listening on http://127.0.0.1:19106/mcp
```

If `doctor` cannot connect, confirm the `perf-debug` install, Steam launch, active run, and absence
of a port collision. Never install a new suite while the game is running.
