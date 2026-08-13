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

Every game-domain `BigDouble` is one JSON string produced by the shared number formatter, never
a JSON number or a text/mantissa/exponent object. Zero is `"0"`. The formatter follows the screen:
ordinary player-scale values are plain with at most two decimals (`"26"`, `"2.2"`), while large or
small magnitudes use a normalized mantissa and lowercase `e` exponent without a plus sign
(`"1.66e8"`, `"1.23e-3"`). There is one formatter and no precision or verbosity option, and it is
not the wire's alone: the differential check prints magnitudes from the Runtime page in builds this
whole surface is compiled out of, and a second formatter for those builds would be a second
notation.

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
- **Rows are a table**, and its header is three lines — the count, what the rows share, and the
  columns:

  ```
  rows 6/229 next=6
  these 6 share: state=completed, maximum=1, affordable=unpriced
  [id | name | level]
  00246c | Gather Space | 1
  ```

  ```
  rows 20/20
  these 20 share: active=0, add=unverified, remove=inactive
  [plot | action]
  Moon Garden fd0000 | Plant Moondust fe0000
  ```

  `total` and `nextOffset` live on the count line, never on a row — and on the count line of the
  rows they counted. A page that carries a second list beside its rows (the categories a degraded
  search could not read) counts that list on its own, because a total and a resume offset belong to
  the set they were measured over.
  - **The column set is a fact about the category, not about the page.** It is complete, and in the
    same order, on every page of every category — but it is named across *both* header lines. The
    share line takes the columns this page holds one value for, in declaration order; the header
    names the rest, in declaration order. Between the two, every declared column is named exactly
    once, so a reader who has only ever seen this page still learns that the category has them.
    Where the producer declares its columns that declaration is the order, and where it does not,
    the page's widest row settles it — so the row a page happens to start with can never reorder it.
  - **A column the share line settled leaves the rows, and a column that varies is never settled.**
    The two are one rule read from both sides. A page that printed `uncapped` on twenty rows under
    a line that had just said all twenty share it said one fact twenty-one times; a page that
    hoisted a column whose values differ would be lying about them. Neither is a judgement call:
    one value over the whole page means the line, anything else means a column.
  - **The share line adds, it never subtracts.** It is page-scoped (`these 6 share:`, never `all`
    beside `/229`), and it is said only when it is shorter than what it takes off the page, so a
    page never carries a summary longer than the repetition it replaces. It never takes the last
    column either: a page whose every column is constant keeps its table, because rows with nothing
    left in them are not rows. Its values hold no comma, so a reader splits the line on `, ` and
    then the first `=`; free prose stays in its cell, where the column boundary says where it
    ends.
  - **One delimiter.** Every table separates its columns with ` | `, whatever its cells hold.
  - **An empty page is the same table with no rows**: `rows 0/180` and then the column set. It has
    no row to read that set off, so the producer states it, and a portable test holds every
    category's stated set to the columns its full page renders. A list that is not a page still
    answers `spells: -`.
  - **A cell carries one word, never a sentence.** A blocked row names what blocks it with one
    short lowercase fact from the vocabulary below, and a row that is not blocked says so in the
    column that asked. No cell ever holds an `ERR_` class: those say which kind of no a *refusal*
    is, and a table refuses nothing. The `reasonCode` + `reason` pair a refusal carries appears in
    no table at all, so the declared column set is the whole truth — a page holding a blocked row
    is exactly as wide as the same page without one.
  - **A fact is worded once.** Where a column the row already carries states the block, that column
    states it alone: a finished upgrade says `state=completed` and nothing repeats it — not a
    levels-left count of zero, not an `available=no`, not an affordability word standing in for a
    lifecycle one. A word is only added where the row could not otherwise show the fact.
  - Length is relaxed for page constants alone. A value identical on every row makes every row
    equally wide, so a page whose every row carries the same multi-line value is a table with one
    wide column rather than twenty paragraphs — while a varying value that big still costs the page
    its table. A constant the page could only render as a count of properties or elements costs it
    the table too, because that count would leave the value nowhere.
  - **No key with nothing after it.** A value the game published as an empty string reads as `-`,
    the same mark an absent one gets, because `key=` before a delimiter is indistinguishable from a
    truncated line.
  - **One mark for absence, and it is `-`.** A column the game published no value under, a cell
    holding an empty collection, a value that is an empty string, and a table cell with nothing in
    it all read `-`. A round that met five spellings of nothing in one session — `-`, `empty`,
    `unset`, `none`, `uncapped` — made a reader learn which surface spoke which before they could
    tell absence from a fact. Only two of those five were facts, and only those two stay: `empty`
    says *a slot exists here and holds nothing*, so a bar with three of them has three places to
    equip into, and `uncapped` says *no ceiling exists*, which is not the same as a ceiling nobody
    published. `unset` and `none` said only "nothing here" and are the mark.
  - **Outside a table, a documented default is absence.** A block prints `isPercent` only when it is
    `yes` and `order` only when it is not `0`; a missing one is that default rather than an unknown.
    A block also omits `nativeType` where the `category` beside it names exactly one native class —
    the map is what `world_categories` publishes — and keeps it wherever that implication is not
    one-to-one, which is the only case where it says something the category cannot. This applies to
    blocks alone. Inside a table the header already promised the column, so it prints on every row
    including the ones holding the default: `double-variables` and `int-variables` pages carry
    `isPercent` for exactly that reason, and a caller who reads it there never re-fetches two
    hundred blocks to learn one flag per row.

#### The cell vocabulary

One vocabulary across every category, so a word means one thing wherever it is read. Words are
lowercase, hold no spaces, and are facts rather than codes.

| word | what the cell is saying |
|---|---|
| `yes` | nothing stands in the way of what this column asks |
| `no` | no, and the row's other columns are where the why is |
| `locked` | the player has not reached this far; the game shows no row for it |
| `available` | the game shows this, and a purchase is the next thing that could happen to it |
| `completed` | every level is bought, which is also when the game's own row disappears |
| `met` / `unmet` | the next purchase's own per-level conditions hold / do not hold yet |
| `unmodelled` | a condition this suite does not model, so no verdict would be honest |
| `unpriced` | the game publishes no price for this, so affordability cannot be read |
| `unevaluated` | a price is published, with no same-generation holding to compare it against |
| `no_bandwidth` | short, and the ceiling is bandwidth rather than the amount held |
| `not_offered` | the game is showing no offer for this |
| `ambiguous` | the game offers this more than once, so the target is unclear |
| `plot_short` | the plot has no room, or not enough of what the action consumes |
| `list_full` | the list this would join has no empty slot |
| `inactive` | nothing is running, so there is nothing to act on |
| `unverified` | the game only checks this when the action starts; it cannot be read ahead |
| `uncapped` | no ceiling applies |
| `unreadable` | the suite could not read this fact from the game this generation |
| `empty` / `unslotted` / `manual` | a slot exists here and holds nothing / the loadout holds this nowhere / the entry is not automated |
| `-` | nothing here — the one mark for absence, and never a word |

The sentence behind a word is not lost — it is what `get` and a refusal answer with, which is where
a caller who wants prose has asked for it. A decision code with no word here fails the read rather
than printing itself into a cell.
- **A list whose elements are each more than one line puts a blank line between them.** Two answers
  at the same indent with nothing between them read as one; a `world_get` batch is where that got
  loud. One-line elements never had the problem and keep the tighter list.
- **A refusal is one line**: `refused (ERR_NOT_FOUND): The spell Beam Burst you tried to cancel is
  not currently active.` A decision block reads the same way, verdict first and sentence last:
  `equip: no (ERR_LIMIT) maximumAmount=0: Every slot in this loadout is in use.`
- **A canned sentence is said once per response.** The first line carrying a given `ERR_` sentence
  carries the whole of it; every later line in the same response that would repeat that exact
  sentence carries the class alone. A response with one refusal in it is therefore byte-identical
  to what it was, and a batch of two hundred blocked rows stops paying for the same paragraph two
  hundred times. Sentences a producer wrote for the occasion — the ones holding this row's own
  numbers — are not canned and are never deduplicated.
- **A value beside its ceiling is `43/45`**, the way the screen shows it, and **a value that moved
  is `1 -> 2`**. A pair whose halves are equal describes a move that did not happen, so the page
  states the value once: a toggle that was already off answers `on: no`, never `on: no -> no`. The
  pair still rides wherever the fact rides; what it no longer does is spend an arrow on nothing.
- **A press that landed says so even when it has nothing to show for it.** The status word is
  dropped because the page under it already proves the answer — but an action the game exposes no
  read for has no page under it, and there the word is the whole answer: `committed`, and nothing
  invented to sit beneath it.
- **An envelope is not a fact.** One object wrapped in one key that names nothing the caller asked
  about is unwrapped, so an entity appears once per response.
- **One price shape.** Every cost row on the surface says what it asks, what you hold, whether that
  covers it, and the resource it is about — `cost`, `spendableAmount`, `affordable`, `resource` — in
  that order, in a list named `costs`. A producer that published the price under a different member
  has it renamed on the wire, and a rename used to be written where a new member goes, last, so two
  verbs spelled the same three facts in two orders and a reader scanning the second one positionally
  read the wrong column. A row that holds one of the four says all four: hoisting `affordable` out
  to a sibling key left a three-column price beside four-column prices in the same session. The
  sibling key is a different fact and stays — it answers whether the whole purchase is payable,
  where the column answers it per resource and so names the one that is short. A table with more to
  say keeps its extra columns and still spells the price `cost`: research's `investment` carries the
  native fill bar (`invested`, `required`) beside the same words, which is a richer table rather
  than a fourth dialect.
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
published id — the handle a response just printed is always one of those, and the tool schemas say
so: an id parameter is described as the id a row prints, never as a canonical UUID an agent would
then fan out to resolve. A prefix that matches
several answers `ERR_INPUT` and names the ids it matched; nothing is guessed. A handle resolves
against the catalog the current run publishes, so when no catalog is published the refusal says
that — `ERR_UNAVAILABLE`, the same lifecycle fact the whole UUID for the same entity answers with —
rather than calling a handle the same run handed out a malformed argument.

An entity is its name and its handle wherever it appears: `Constitution 006061`. An id the catalog
cannot name renders as `(unnamed 2c20e7)` — marked, never a bare id that reads like a row whose name
happens to be hex. **Refusal sentences are not an exception.** A sentence that names the entity in
the caller's way — the spell already holding the slot, the entity whose action belongs to another
tool — names it the same way every other line does, and never as a raw UUID, a bracketed internal
label, or a native type name.

Where a refusal turns on a *second* entity, that entity is also a field, not only a phrase. A cast
that refuses because another spell holds the slot carries the occupant under `details`, so a caller
can act on it — explain it, unequip it — without parsing the sentence it was named in.

## Tool surface

The registry is exactly 42 tools. It is built once per lifecycle and never changes mid-session, so
there is no `tools/list_changed` notification. The rows below are in `tools/list` order.

Reading is three primitives, organized around what a player is doing rather than around the game's
internal type system:

- **`world_search`** — cross-category, one uniform row, identity plus keywords. It finds the thing
  you heard a word for and did not know you were looking for.
- **`world_list`** — one category, that category's own declared columns, filter nouns. Durable
  planning facts only, with a database-table feel.
- **`world_get`** — one id, or a batch of them: everything. What it is, its description, its state,
  its price, its effects, what is holding it. This is *the* detail verb, and the only read that
  answers prose.

`game_tooltip` is not a fourth: it reads the live screen, which is a different question from what an
id is. `entity_catalog` and `world_categories` are not reads of entities at all — they answer
inventory questions about what this build loaded and what this world published.

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
| `world_get` | Read everything one id says — row, description, gates, requirement graph, exact costs, blockers — for one id or a batch |
| `entity_catalog` | Search every live-registry identity and available player-facing name, including loaded entities hidden by progression |
| `world_search` | Find a term across every entity category at once: name, keywords, category, most relevant first |
| `suite_health` | One compact runtime, feature, service, STOP, scene, and contract-health shape |
| `suite_configuration` | Read every writable setting's committed value; `mode=describe` adds type, domain, and purpose |
| `trace_health` | Read trace-writer health, segment, record, and byte counters |
| `suite_check_game_math` | Run the differential check of the suite's math against the game and answer with one verdict word, one line per check, and one provenance line |
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
| `game_alchemy` | Add or remove uses of one ordinary Alchemy recipe through its visible list |
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
| `game_navigate` | Navigate a catalog screen/subtab and optional published plot UUID; answers in words |
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
not protocol data. `world_get` takes either a `uuids` list or the singular `uuid` alias; supplying
both is a `mutually_exclusive` validation failure, and either form returns the same list shape. It
requires nothing else: an id resolves its own table, so a caller holding one from a search, a
refusal, or an action response reads it without first learning where it lives. `category` stays
accepted and optional, and names which table the row is read from — the only way to reach a row
whose native type belongs to more than one table, and the way to insist on the table you meant. A
named table that does not hold the id answers a miss in that table rather than quietly resolving
into the one the caller did not name.
Composite tables cannot be addressed by an arbitrary related UUID; use `world_list`.

Supply `uuids` with 1–200 canonical UUIDs. Results preserve input order without repeating an index or UUID on
successful rows. A typed not-found or invalid result repeats the implicated UUID because that is
failure evidence. Array reads have no aggregate status: each result owns its availability, while a
category or schema failure that prevents the call from running remains a top-level refusal. Every
row comes from the same pinned publication; the server does not issue a
generation or retain a snapshot token across calls. A call refused as a whole is the one-line
refusal every other reader answers with, with no empty `results` collection beside it; the
collection is a property of an answered batch.
Localized collection gaps mark only the implicated list/search/get row unavailable and attach the
partial row plus exact evidence there; unaffected rows in the same call remain ordinary results.
`world_overview` therefore summarises those gaps in one sentence rather than restating them:
`collection.gap` says how many leaves, of which condition types, on which named owners, and which
read returns them. The per-leaf evidence is the same bytes on every call for a given build and
already lives on the owner's own `world_get`, as `implicatedSkippedRows`.

Every paged read — `world_list`, `world_search`, `entity_catalog`, and `game_tooltips` — pages one
way. Each takes `offset` and `limit`
and answers with `total` plus `rows`; an answered page always carries the collection, including
when it is empty, and a refused one carries the refusal line alone.
`nextOffset` is present exactly when more rows remain, and its value is the input offset plus the
rows actually delivered, so `nextOffset` present means "resume here" and `nextOffset` absent means
"that was the end". There is no `truncated`, `returned`, `hasMore`, `matches`, or `tooltips`. A page
of the three world-backed readers may be shorter than `limit` because the response is bounded at
12 KB, which each of those tool descriptions states;
a short page with a `nextOffset` is that bound, and a short page without one is the end of the set.
The bound is charged against each row as it is built, so a full page is a real 12 KB page rather
than a fraction of one.
`game_tooltips` pages the screen's live hover elements rather than a published table, so `limit` is
its only page bound — and it pages them by the panel they hang off, so its `total` is the screen's
panel count and a row is one panel with its own elements under it. Factored that way a whole screen
is small, so the usual call is one.

**A `world_list` category — or a `world_search` result — of 25 rows or fewer comes back whole**, when
the caller named no `limit` of
its own: neither the default page size nor the 12 KB bound cuts a set that small into pieces, so
there is no `nextOffset` and no second call. The count line still reads `rows N/N` — how many rows a
category holds is a fact whether or not any were withheld. A caller that *does* name a `limit` gets
the page it named, `nextOffset` and all: the rule never raises a page above what was asked for, it
only stops lowering one below the whole of a set nobody asked to have cut up. The threshold is
`GameMcpWorldQuery.WholeCategoryRows`.
`world_search` deduplicates by entity identity before sorting, so one entity that matches in two
categories occupies one row and one page slot.

### What search searches, and what it cannot

A `world_search` row is `id`, `name`, `category`, `keywords`, `matchedOn`, on every row of every
category and with no other column. A search page holds hits from every category at once, so
borrowing each category's own scan columns unioned every heading onto one table and left about nine
cells in ten empty — while `category`, the column that says which read verb can follow the hit up,
was filled only on rows that had no identity of their own. Widening is `world_list`'s job, on a page
whose columns all apply to every row.

`matchedOn` names the field the query hit — `name`, `internalName`, `id`, `keywords`, `category` or
`nativeType` — which is what separates the row a reader meant from a coincidence in a string they
never see. It is the absence mark on a call that ran no query, because nothing was matched.

The `keywords` cell is the entity's authored word line — the type assets whose display names the game
prints as `ITooltipable.GetDisplayType()` — joined with `, ` in the order the game prints them, and
**empty where the game authors none**. Upgrades, challenges, views, achievements, advancements,
recipe books and crafting recipes all spell that line as a constant string with no type field behind
it, so their cell reads `-` and nothing is synthesized from the category, the screen, or the name.
The words come from four published tables: the `entity keywords` collection (thirteen classes),
research's own type rows, the consumable type relations, and the spell relations of kind `SpellType`.
A type asset the game left nameless contributes no word at all — the seven `ChallengeTypeSO` are
effect-targetable but deliberately wordless, and the asset-name fallback other surfaces may walk
would print seven keywords no player has ever seen.

**Search does not search descriptions.** The published world captures no entity description text at
all — for any class — so a word appearing only in an entity's description finds nothing here. A
description is read live, per entity, by `world_get`. This is a real gap and it is stated rather
than papered over: relevance therefore bands in three, not four.

A hit is ranked by *why* it matched, and the reason is a sort key rather than a column: an entity's
own identity (player-facing name, internal asset name, or id) first, then a keyword, then the
category name or the native type behind it. Inside a band, rows are in id order, so two pages of one
result agree. Matching is case-insensitive substring on the whole query, which is the rule the game's
own search box uses — `FilterVariable.MatchesSearchStrings` lowercases both sides and asks
`Contains`, with no tokenising and no whole-word test.

**`query` is optional when a filter is present.** "What have I not unlocked yet" is a whole
question and it names nothing; requiring a word beside the filter made a caller invent one broad
enough to reach everything they meant and then hope it had. A call that names a query, a category, a
state or a run is a call; a call that names none of them is refused and the refusal lists all four.

`state` narrows to one of the three lifecycle words, and it reaches every category that carries the
column: `upgrades`, `research`, `structures`, `alchemy-recipes`, `glyphs`, `rituals`, `plot-nodes`
and `challenges`. The filter reads the word the row's own list page says and never derives one of its
own, so its reach is a consequence of which pages carry the column rather than a list maintained
beside them — extend the column and the filter follows.

`run` narrows the same way on the one category that publishes the column: `idle`, `queued`,
`active`, `passed`, `failed` on `challenges`. It is a different axis from `state` — a challenge whose
last run passed is `available` again at the next level — which is why the two are separate columns
and separate filters. A call that narrows to some other category *and* names a run is refused by
name, because no other category has the column to answer with and an empty page would read as
"there are none".

A category with no lifecycle model still does not match a state filter, and is still not excluded
from an unfiltered search: inventing a word here for rows whose own page never says one would be a
second grammar for the same fact. `challenges` is read now because it now has a lifecycle to read —
its `state` column used to hold the five run words, which moved to `run`. `category` narrows to one
searchable category; naming a composite one is refused by name rather than answering an empty page.

One entity is one hit however many categories publish it, and the first category holding it wins. An
alchemy recipe therefore answers under `alchemy-recipes` rather than under the `concept-recipes`
republication of the same 125 rows.

`keywordHits` appears when the query hit more than one distinct keyword, and says how the whole
result set splits between them: `keywordHits: Charm=2, Charm Focus=1`. It counts only the keywords
the query itself matched, so it is short by construction rather than by a cap, and one keyword is no
split at all. The counts are over the whole result — the `M` of the `rows N/M` line below it — not
over the page.

The two search tools are not interchangeable at the same offset. `world_search` sorts the whole
result by relevance and then by id; `entity_catalog` walks the whole live registry in UUID order.
They also answer different questions: `world_search` sees only entities the published world carries a
row for and additionally matches a category's own name and native type, so a category name selects
every row in it, while `entity_catalog` sees every loaded UUID — including the ones no world row
covers, which read `category=not-world-projected` — and matches only that entity's own identity
fields. Equal totals for one query mean the query happened to select the same set, not that the tools
have the same scope.

`entity_catalog` complements `world_search` with the game's complete live runtime identity registry.
At the first stable Playing world capture after `RuntimeReady`, the suite validates and copies that
registry once for the lifecycle. Searches cover UUID, exact runtime type, Unity asset name, and
player-facing `GetName()`, so loaded entities hidden or not yet revealed by progression are findable
without navigation. Before that bind, or when its declared contracts fail, the tool returns
`unavailable` rather than substituting the build-time TSV fixtures.

A match contains `uuid`, `name`, `nativeType`, and one `category` — this is the surface that still
carries the runtime type unconditionally, because browsing the catalog is the one activity that asks
for it and a browser names no category to imply it from. `category=not-world-projected`
means that the live registry identity has no world row. `name` is present exactly when the game
authors a player-facing word, so its absence is that fact and needs no flag beside it;
`internalName` carries the Unity asset id whenever it is not that same word. An id nobody can name says so in its
`name` cell — `(unnamed 2c20e7)`, the one form this surface has for it — and nowhere else: a second
block saying the same thing put a refusal class in a table cell, and a table refuses nothing. The same immutable
catalog reference is pinned with the answering world and supplies names for every MCP entity
reference; UUID-only joins are unnecessary. Catalog membership and naming do not prove current
visibility, availability, or a world-category row. Lifecycle replacement clears the catalog before
the next bind, so no prior-save Unity reference or label survives.

`world_list` and `world_get` use the same deliberate player-relevant row projection. Every entity
row leads with its primary `uuid` and `name`. A composite row with no identity of its own — a spell
slot, a cost row, a loadout section — never borrows a nested entity's `uuid` as though it were the
row's handle: it carries no `uuid` at all, and its nested references stay under their own role
names. A row with no handle to hand back says nothing about that; a row's `category` is the
category the caller named to reach the page. A composite row that does promote an actionable
primary identity keeps every other reference separately named, and drops the promoted role's own
name only when that reference was the row's only one, because then the promoted `uuid` and `name`
already say which entity it was. No role repeats the row's own identity under a second name.
Rows then carry only the small set
of availability, unambiguous paid/bonus/total level, quantity, occupancy, readiness, or progress
fields useful for comparing rows. Raw capture inputs, cached implementation fields,
resource traits, rate inputs, and modifier structs stay out of world rows. A `world_get` block owns
the deeper evaluated evidence, beside that same row. Purchase-cost rows are composite and therefore remain a `world_list`
surface.

One fact has one name and one shape across every read that carries it. A scan row is a narrower
row, never a differently spelled one: `crafting-recipes` says `startingAmount` in the list exactly
as it does in the detail row, and `discovery-trees` says the named `mode` — `idle`, `crafting`, or
`choice` — rather than the native integer behind it. Whether a decision can be taken is `available`
everywhere it is known.

Narrower never means a caller has to mutate to learn the rest. An `equipment` row carries `created`
beside `equippedCount`, because a zero count means both "own none of this artifact" and "own some,
equipped none", and the only other way to tell those apart was to attempt an equip and read the
refusal. A `resource-types` row carries the same `hidden` gate its detail row does, because a
hidden type refuses every level purchase and a list without it is a list a caller probes row by row.

Narrower also never means a caller has to fetch a detail page per candidate to compare a column the
row already had. A list row carries the columns its category is scanned by, and those columns are
written unconditionally so the header is the same one before and after a lifecycle boundary:

| Category | Scan columns |
| --- | --- |
| `rituals` | `state`, `selected`, `reachedLevel`, `selectedLevel`, `waveTotal`, `affordable` |
| `research` | `state`, `paused`, `totalLevel`, `queuedLevels`, `requirements`, `canDevelop`, `affordable` |
| `upgrades` | `level`, `queuedLevels`, `screen`, `state`, `maximum`, `requirements`, `affordable` |
| `structures` | `level`, `queuedLevels`, `state`, `enabled`, `affordable` |
| `alchemy-recipes` | `state`, `masteryLevel` |
| `glyphs` | `population`, `screen`, `state`, `discovered`, `paidLevel`, `bonusLevel`, `totalLevel` |
| `plot-nodes` | `state`, `masteryLevel`, `quantity`, `availableQuantity` |
| `challenges` | `state`, `run`, `level` |
| `equipment` | `created`, `equippedCount` |
| `resource-types` | `level`, `hidden` |
| `double-variables`, `int-variables` | `value`, `isPercent` |

A number variable's `isPercent` is on the page because reading `25` without it is reading the wrong
number: the same row means twenty-five and twenty-five percent depending on one flag, and a round
spent 22% of its whole wire re-fetching two hundred blocks to learn it per row.

`research` says `state` and never a second `visible`, `available` or `complete` column, because
`state` is derived from exactly those three; `paused` is the player's own saved switch on the entry,
which a pause moves and the lifecycle never does; `canDevelop` is the develop decision the detail row
publishes, and `affordable` is the published cost verdict for the next development, which is a fact
of the row rather than of the develop gate — so the scan row carries it everywhere instead of only
where that gate is open.

#### Which screen shows an upgrade

The Upgrades panel is the persistent right-hand strip, and **what it holds changes with the screen
you are on**: the game swaps one of nine hand-authored membership lists into it as the active screen
changes. Membership in one of those lists is therefore the whole fact about where a row is found,
and `screen` is that fact in the word `game_navigate` takes:

| word | list | rows | where the player finds them |
| --- | --- | --- | --- |
| `Magic` | `MagicScreenUpgrades` | 61 | Magic |
| `Workshop` | `WorkshopScreenUpgrades` | 43 | Workshop |
| `World` | `WorldScreenUpgrades` | 40 | World |
| `Alchemy` | `AlchemyUpgradesList` | 30 | Alchemy |
| `Scholar` | `ScholarScreenUpgrades` | 27 | Scholar |
| `Rituals` | `RItualScreenUpgrades` | 21 | Rituals |
| `World/Aspects` | `AspectUpgradesList` | 3 | World > Aspects, the three pedestals |
| `Time` | `TimeScreenUpgrades` | 0 | Time |
| `all` | `AllUpgrades` | 4 | nowhere in particular — see below |

**The screen grammar is `game_navigate`'s own**, in every column that uses it. A `screen` cell is
either a screen label exactly as the live catalog spells it, or `Screen/Subtab` where the destination
is a subtab, or one of the two documented sentinels — `all` on an upgrade row and `no_page` on a
glyph row. `game_navigate` matches labels with `StringComparison.Ordinal`, so the words used to be
lowercase copies of labels rather than the labels: a reader who pasted `magic` into the tool got a
no-match refusal, and `aspects` named a destination the tool has no top-level entry for at all. Both
sentinels stay lowercase on purpose, because nothing in the catalog is labelled `all` or `no_page`
and that is what keeps them from reading as places to go.

The eight screen lists are disjoint and cover 225 of the 229 upgrades. `all` is **not** a ninth
screen: `AllUpgrades` holds every upgrade in the game, so saying it about a row a screen list also
carries would say the same thing about all 229 rows. It is the word only where it is the whole
truth — the four `Raise …` cap-raisers, which no screen panel groups and which the player meets in
the ungrouped Upgrades list and in the alert badge the game gives them of their own.

Three of the nine lists — Scholar's 27, the 3 aspects, and the empty Time list — are named by **no**
`ViewSO` anywhere in the game's object graph. Their only consumer is prefab `ListViewSwapper` data,
outside both the assembly and the serialized dump, which is why deriving the column from captured
view routes alone would have marked Scholar's 27 upgrades exactly like the 4 that genuinely sit on
no screen. Those three lists are reached instead by the identity they carry: the pinned uuid goes
into the game's own identity registry and an `UpgradeListVariable` has to come back out. That makes
the pairing of list to word a constant of the pinned build rather than a guess, and every authored
upgrade list is pinned, so a build that adds a tenth panel fails the suite's own tests rather than
quietly wording its rows as if it did not exist.

A row the suite could not read the membership of says `unreadable` and never a plausible screen.
Membership is published whole or withheld whole for the same reason: a row missing from a partial
table is indistinguishable from a row on no screen, and one of those is a fact the column is
entitled to state.

#### Which page shows a glyph

A glyph page is bound to a `GlyphListVariable` through `ViewSO.relevantLists`, so the same fact
answers the same question for glyphs, in the same grammar. Four of the nine authored lists are
populations; the other five are the four runtime selections the player fills and `AllGlyphs`, which
would say the same thing about all 47 rows:

| word | list | rows | where the player finds them |
| --- | --- | --- | --- |
| `Magic/Augments` | `AugmentSpellGlyphs` | 22 | Magic > Augments — the Glyphcraft and Upgrade grids |
| `Magic/Spellbook` | `CoreSpellGlyphs` | 10 | Magic > Spellbook — the Unlock page and its core slots |
| `Alchemy/Alchemy` | `CoreAlchemyGlyphs` | 12 | Alchemy > Alchemy — the Learn page |
| `no_page` | `EquipmentGlyphs` | 17 | the game names no page for this list — see below |

The word names the subtab `game_navigate` reaches, not the grid two levels below it: the Augments
strip only exists once Augments is selected, so `Magic/Upgrade` would be a destination a cold
navigation cannot take, while `Magic/Augments` is one it always can.

**Several pages is an answer here, not a refusal.** 61 membership edges cover the 47 glyphs, because
one unlocker opens recipes on several benches — Arcane, Dragon, Expansion, Flow, Nature, Psionic and
Storm are on all three unlocker lists at once. Their cell is the ordered set,
`Magic/Spellbook, Alchemy/Alchemy`, in pinned list order. This is the one place the column differs
from the upgrade column beside it, which refuses a row two screens claim: for an upgrade that is
impossible on the pinned build and worth a tripwire, and for a glyph it is the authored norm, so
refusing would throw away a fact the game plainly states.

**`no_page` is not `unreadable`.** `EquipmentGlyphs` is mentioned by nothing in the serialized object
graph but its own name — no `ViewSO`, no `DiscoveryTreeSO`, no structure — so the ten glyphs it alone
carries (Amulet, Bag, Cloak, Conductor, Gloves, Helm, Ring, Runic, Tool, Weapon) have a membership the
suite read perfectly and a page the game does not name. Guessing Workshop > Artifacts from the
glyphs' own names would be inference wearing a game fact's clothes. `unreadable` stays reserved for
the withheld publication, exactly as on an upgrade row.

#### A list row carries durable facts only

A list read answers a planning question, so the test a column has to pass is whether **two reads
seconds apart, with nobody playing between them, would agree**. A column that turns over on its own —
casting-now, engaged-this-tick, a queue that has not landed yet — fails it, and the answer is to
delete the column rather than to smooth it. Such a column is stale before the caller finishes reading
it, and it does worse than mislead: the page-constant hoist lifts whichever columns a page happens to
agree on, so a fact that flips mid-scan changes the *header* between two pages of one read. A live
round caught exactly that on spell-slots' `casting`, with nothing about the request changed.

Nothing is deleted from the world. The raw-fact scan keeps every one of these facts, `world_get`
keeps them where a reader asked about one row, and the action responses keep them because an action
question is precisely what they answer.

| Category | Deleted column | Why it is not durable | Where the fact still lives |
| --- | --- | --- | --- |
| `spell-slots` | `casting` | `Spell.IsCasting()` — true only while a cast runs | scan, `world_get`, every `game_cast` response |
| `agromancy-processing` | `processing` | `IsEngaged()` — under way rather than merely present | scan, `world_get` |
| `alchemy-instances` | `settled` | `activeCount == queuedCount`; reports only that a change has not landed yet, which the two columns beside it already show | scan, `world_get` |
| `plot-nodes` | `idleQuantity` | the game's `GetQuantity()` is a phase timer's count, and it moves while nobody plays | scan (`reading.idleQuantity`), `world_get` |
| `research` | `development` | `idle`/`active` was "is a level in flight", which drains on its own and which `queuedLevels` counts on the same row | scan (`isDeveloping`), `world_get`, the pause response |

`research`'s column had one durable half and one transient half, so the durable half kept a column
under its own name: **`paused`** is the game's saved `isActive` field inverted — the player's own
switch on the entry. It is strictly better at the job `development` was kept for. `development`
reached `paused` only while a level was in flight, so the one row a planner most wants to find, a
stalled entry with an empty pipeline, read exactly like a healthy one.

Two columns that a strict reading of the test would also condemn are **kept on purpose**, because
deleting them would answer no planning question at all: a `resources` row's `amount` and
`netRatePerSecond`, and every `queuedLevels`. A resource holding is what every purchase decision is
made against — it is the same class of fact as `affordable`, which is ruled in — and the development
queue is what binds progression early, so how much is in flight is the question, not noise around it.
Both accumulate rather than flip: they are live measurements of a standing position, not a report of
what the game is doing at the instant it was asked.

#### The three-state lifecycle

Everything the player buys moves through the same three states, so every purchasable row says one
of the same three words under `state`:

| word | native fact behind it |
| --- | --- |
| `locked` | prerequisites do not hold — `UpgradeSO`'s `prerequisites.Check()` is false, `ResearchSO.IsVisible()` is false, `StructureSO.IsAvailable()` is false |
| `available` | prerequisites hold and nothing is finished — `UpgradeSO.IsAvailable()`, which is also what `UIUpgradeButton` renders its row on |
| `completed` | `IsMaxLevel()`, which is also when the game's own row disappears — completion and hiding are one state, never two words |

**The word is not only for what the player buys.** What the player experiences as lockedness is the
fact, and where the game hides a row, or shows a placeholder in front of it, rather than greying it,
that hiding *is* the locked state. So every category in which a locked thing can be met says the
word, each derived from the member that category's own row renderer decides on:

| category | `locked` means the player sees | native member behind the word | words it reaches |
| --- | --- | --- | --- |
| `alchemy-recipes` | no row on the alchemy screen | `UIAlchemyRecipe.IsVisible()` = `AlchemyRecipeSO.IsAvailable()` = `visibilityType == Discover ? discovered : visibilityPrerequisites.Check()` | two |
| `glyphs` | no row in the glyph picker | `UIGlyphListItem.IsVisible()` = `GlyphSO.IsAvailable()` = `discoverable ? discovered : prerequisites.Check()`, which is also `GlyphSO.IsVisible()` | two |
| `rituals` | the undiscovered placeholder where the ritual would be | `UIRitual.IsVisible()` = `RitualSO.IsDiscovered()`; `IsAvailable()` and `IsVisible()` are the same member again | two |
| `plot-nodes` | no row on the harvest screen | `UIPlotNode.IsVisible()` = `PlotNodeSO.IsVisible()` = the `visible` field the game latches from `visibilityPrereq` | two |
| `challenges` | a challenge the draft will never offer | `ChallengeSO.IsAvailableToRun()` = `!IsMaxLevel() && availabilityPrerequisites.Check(level)` and every previous challenge completed | three |

How many words a category reaches is a fact about the category, not a shape imposed on it. Only
`completed` needs a ceiling, and most of these have none — a structure has no `maxLevel` field,
`GlyphSO.CanLevel()` is the constant `true`, a recipe's `maxLevel` is the level it has *reached*
rather than one it stops at, a ritual is re-run forever, and a plot node's mastery has no top. Two
words is the honest whole of those categories; a third would have to be invented. Only `upgrades`,
`research` and `challenges` reach all three, and each asks its ceiling first, because that is the
order the game's own predicate composes in — `ChallengeSO.IsAvailableToRun()` opens by returning
false the moment `IsMaxLevel()` holds.

Where the new word made a raw column redundant on a list page, that column died and the raw fact
stayed in the category's fact scan: `rituals` dropped `discovered`, `plot-nodes` dropped `visible`,
`glyphs` dropped `available`, and `alchemy-recipes` dropped `discovered` — each was the lifecycle
predicate under its own name. `glyphs` **kept** `discovered`, because for a glyph it is a different
fact: a pool unlocker is available off an authored prerequisite while never having been discovered at
all.

**Every glyph row says which of the two populations it belongs to**, because that is what makes its
other columns readable. `population: augment` is one of the 22 the Magic screen's glyph grid draws
from — discovered, then spent on spells. `population: unlocker` is one of the 25 that carry a recipe
book and open a family of recipes, each held off one authored requirement edge rather than by
discovery. The word comes from `GlyphSO.discoverable`, which splits them exactly and agrees row for
row with membership of the authored `AugmentSpellGlyphs` list and with carrying an
`associatedRecipeBook`. It is **not** `augmentsSpells`: that field reads false for Distinct, Weak and
Wrath — three discoverable, book-less augments — so it splits 19/28 rather than 22/25, and every
surface that gated on it (the compose resolver's core-glyph check, a spell's core-slot verdict, and
the owned-augment options a loadout offers) mistook those three for core glyphs. The `glyphs`
category is **not** split in two: glyphs are one player concept and one verifiable count of 47, and
the population is a column on the row.

**A glyph's `visible` predicate is its `available` predicate.** `GlyphSO.IsVisible()` is a call to
`GlyphSO.IsAvailable()`, and the picker tile's own `IsVisible()` calls `IsAvailable()` too, so the
game cannot show a glyph it will not offer and the two verdicts are one fact with one reason. They
used to disagree on all 25 unlockers, because `visible` was answered from `discovered || currently
offered` — for an unlocker the raw `discovered` field is not what availability reads, and no unlocker
is ever a discovery-tree offer. Whether an undiscovered augment is on offer right now is a real fact
and rides on the row's own `discover` block as `offered`.

**A locked glyph's reason is its own population's gate.** `GlyphSO.IsAvailable()` returns
`discovered` for an augment and `prerequisites.Check()` for an unlocker, so an unlearned augment
answers *this has not been discovered yet* and an unlearned unlocker names the authored condition its
container holds — *Learn Formation unlocks this glyph, and it is not reached yet* — with `blockedBy`
carrying that entity. The 25 containers hold one condition each, a research, an upgrade or a
prerequisite link, and the world publishes them under owner kind `Glyph` (`glyph.prerequisites`). The
honest-unknown sentence — *the game keeps this locked, and says nothing about what would unlock it* —
is what is left when neither gate is readable, which means a condition class this suite does not
model. It used to be the answer on 45 of the 47 rows, because the reason branched on
`GlyphSO.discoveryRequired`; that field is true for exactly two glyphs, and its only reader,
`GlyphSO.IsDiscoverRequired()`, is called by nothing in the game.

**A recipe whose lock this suite cannot read says so.** `AlchemyRecipeSO.IsAvailable()` reads
`discovered` on the `Discover` branch and runs a prerequisite container on the other, and only
`visibilityType` says which — so that selector is captured (`alchemy-recipe.visibility-type`) rather
than the verdict, because asking for the verdict would make the game latch that container during a
per-pass capture. All 125 authored recipes on the pinned build are `Discover`, so no row reaches it
today, but a recipe on the other branch reads `state: unreadable` rather than being quietly called
locked.

**`challenges` says three things, in three columns.** `state` is the lifecycle above; `run` is
`ChallengeSO.state` — `idle`, `queued`, `active`, `passed`, `failed`, the game's own five, one
tooltip per value; `level` is how many times this challenge has been beaten. The two shared the
name `state` before, which is why the run word is the one that moved; a queue commit's post-state
reports `run` for the same reason.

**`run` is in-run status, and only that. The durable "I have beaten this" fact is `level ≥ 1`.**
`ChallengeSO.PassChallenge()` increments `level` and then sets the run word to `passed`, and
`ChallengeListVariable.CycleOut()` calls `EmptyState()`, which puts the run word back to `idle` at
the world-cycle boundary that ends the run. So `passed` survives only the window between winning
and the next cycle — a read of 98 challenges on a save with fourteen at `level: 1` and one at
`level: 2` found `run` reading `idle` or `queued` on every single row. A caller told to look for
`state: available` beside `run: passed` is looking in the column that has already been cleared;
`level` is the one that remembers.

Two rules make that a lifecycle rather than a verdict:

- **The can-purchase question is a separate axis.** `affordable` and `requirements` answer it, and
  neither ever becomes a fourth state word. A row nobody can pay for is still `available`: next
  week it is bought and no state moved. Requirements-unmet is the same — it is the branch
  `UIUpgradeButton` takes when it shows a requirements notice in place of a price, a fact about the
  next press. Fully-queued likewise never becomes a state.
- **The word `purchasable` is banned.** It reads as "you can buy this now" while naming a state
  that says nothing about price, which is the exact confusion the two axes exist to keep apart.

`structures` are one of the two-word categories above: `StructureSO` carries no `maxLevel` field at
all, so there is no level at which a structure is finished. Its soft prerequisites are a development
penalty rather than a gate — a structure with them unmet is bought and simply builds worse — so they
belong to the can-purchase axis and never to `state`.

A dial the player sets is not a lifecycle at all. The casting output and reserve levels are
**allocations** whose maximum is the level of the `Raise …` upgrade that raised the ceiling; they
render as `current`/`maximum` on the casting surfaces, and no allocation dial ever appears as a
column on the upgrade list.

Nothing on this surface reads `UpgradeSO.IsVisible()`. Despite the name it is the prerequisite gate
alone and stays **true** for a maxed upgrade, so reading it as "the player can see this row" calls
a completed upgrade locked. The binder reads `IsAvailable()`, which is the member the game's own
row renders on.

`mastery-experience` answers with a summary rather than the ring behind it. The category is a
fixed-size window the game overwrites, and the sources feeding it repeat on a short cycle, so paging
it row by row cost four full pages to deliver about fifteen distinct facts with a monotone `sequence`
as the only column that varied. The page publishes one row per distinct `domain`/`sourceMastery`/
source with the `count` of window samples it earned, and one `window` block — `samples`,
`firstSequence`, `lastSequence` — so a caller can tell one read's window from the next. `total`,
`offset`, and `nextOffset` count summary rows, and so does the `count` `world_categories` advertises
for it: the count a caller reads to decide whether to list a category is the number that read will
answer with, never the raw ring behind it.

A `structures` row publishes `level` as the number the attribute's own badge shows, the game's
persisted `GetBaseLevel()`, and names work still in flight separately as `queuedLevels`, which is
always present because zero levels in flight is an answer; neither
number is repeated under a second name. Both are exact counts on the wire: the badge draws
`Utils.BeautifyInt`, so routing them through the large-magnitude renderer would round a
2,136-level attribute to `2.14e3`. An `upgrades` row publishes `state`, `maximum`, `requirements`
and `affordable` on every row, in every world state. `maximum` is the honest ceiling and reads one
of three ways: `1` for the 214 one-and-done upgrades, the finite `N` for the 11 repeat-grind lines,
and `uncapped` for the four `Raise …` cap-raisers the game marks with a negative native maximum —
never `0`, which would read as a cap of zero and as nothing left to buy. `screen` says where the
game shows the row. A finished upgrade reads
`affordable: unpriced`, because a level that cannot be bought has no price to be short of, and one
the world publishes no cost for reads the same; `state: completed` is what says it is finished, and
it says it once. `world_list` and `world_get` publish the same vocabulary, so a page of uncapped
upgrades still shows the columns a capped page shows and a detail read says the lifecycle in the
page's word rather than a second grammar of its own.

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
holds on every surface, `game_targeting` candidates and the `world_get` research `cap` block
included, and both name their levels waiting as `queuedLevels` like every other row. A separate
work-in-flight flag beside that count is not published either — it only restates the count.

`purchase-costs` is the only modifier-adjusted live cost category. Spell and alchemy cost rows are
immediate/drain observations and are not mislabeled as purchase prices. Every displayed cost is in
the screen's spend units: ordinary nominal costs are divided by the resource quality percent through
the audited `GetTrueSpend` formula, while bandwidth costs remain nominal. Each structure/upgrade cost row exposes
the screen's `cost`, the matching `spendableAmount`, and the resource identity needed for the
next decision. Every cost row uses `cost` for the screen price and `spendableAmount` for the
same-publication player pool, with `affordable` only when the decision was evaluated.
A row's verdict answers for that row's own resource; the whole price is what the rows fold to, so
no aggregate verdict is published beside them. A short row names no reason code and no sentence:
the price and the holding beside `affordable: false` already say "short of this", and writing that
one bit three ways cost thirty constant bytes on every row of a 744-row category. A shortfall that
says something else — a bandwidth ceiling rather than a quantity — still names itself, and on a
row it names itself in the column that asked: `affordable: no_bandwidth`.
Ordinary resources compare their raw on-screen pool against the quality-adjusted spend; bandwidth
resources compare nominal cost against headroom using the game's integer-snapped comparison. A
counter's `amount` is always the number the screen shows for it, whatever native member happens to
carry that number. Cost rows use `spendableAmount` for the native admission operand, so independent
inverted/bandwidth flags never overload one field with two meanings. These fields use the same exact combiner as Auto Buy and do not
include Auto Buy's configurable reserve or excess policy.

A `resources` row is deliberately only named identity, the counter's on-screen `amount`,
`netRatePerSecond`, `capacity` and `atCapacity`. A resource with no storage ceiling reads `uncapped`
under both of the last two: the game's uncapped marker is a negative native capacity, which is never
serialized as a magnitude, and a bare `atCapacity: no` would answer "is it full" about a counter
that cannot fill. Where a ceiling does apply, `atCapacity`
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
the native fill bar, `cost` is what that bar still owes in the units the player spends — the same
word and the same reading every other price on this surface uses — and `spendableAmount` is what the
player actually holds. Associated
research types carry their remaining free bonus levels and investment caps. Only currently
UI-reachable next verbs appear: `develop`, `pause`, `resume`, `cancel`, and `bonus`. A committed
`develop` answers `queued` and stops — it buys research time, not a finished level, and the queue
counts it used to narrate had all reverted by the time a caller could read them. `pause`, `resume`,
`cancel`, and `bonus` apply at once and each answers the single state or count it moved. Read
detail remains in `world_get`.

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
Each lean row names the queue and recipe, reports its slot counted from 1, current amount, and its
`repetitions`. Only an automated entry repeats, so a manual one reads `repetitions: manual` — the
one column answers both questions, where a separate `automatic` flag beside it spelled the same bit
twice and then went quiet about the count. The same
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
identity deduplication so the total and the pages agree.

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

A spent `offer_reroll` answers in the same shape `time_challenge reroll` does: `rerollsLeft` as a
`{before, after}` pair, an explicit `changed` saying whether the offers actually moved, and the
settled `offers` inline. A bare post-value said nothing about what the press bought, so the caller
re-read the whole tree to find out.

These values are copied during the shared 250-millisecond world capture from lifecycle-bound
delegates for native visibility, immediate-required state, current choices, exact next cost,
affordability, and resource true quantity. The MCP worker only projects the immutable row. A choice
UUID must resolve in the same generation as an alchemy recipe, equipment, glyph, ritual, spell
recipe, or time rune. If it does not, only the implicated tree read returns
`discovery_offer_read_incomplete` with `implicatedOffers`; the UUID is never silently omitted.
Current offers are also resolvable through `world_get`, including authored
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
instead of guessing, and a component that is neither a published glyph nor a published resource, or
that asks for more uses than a glyph the player holds permits, refuses as `component_unavailable`.
A glyph the player does **not** hold is a different answer with a different next move, so it refuses
as `ERR_LOCKED` and says which gate holds it — a discovery for an augment, the named authored
condition for an unlocker — where quoting a usage ceiling of nought read as a clamp on something
already owned. This is why a partial component write can never claim a target
it did not resolve.

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
It reports `activeCount`, the slot the recipe occupies when active, and the next visible add and
remove decisions. An available add includes the live click-sized maximum and named per-use resource
costs with current spendable holdings; an unavailable add carries only its binding reason. Concept
recipes remain on `game_concept`, composed Alchemy discovery remains on `game_discover`, and recipe
leveling belongs to the unified level surface rather than this list lifecycle.

`game_alchemy(mode="add"|"remove", uuid=..., amount=...)` applies the caller's explicit positive
amount through the list's native counted mutation after revalidating live usage capacity. Success
returns only the settled `activeCount` before and after. The action boundary revalidates exact recipe
identity, ordinary-family classification, discovery, and capacity before invoking the explicit-count
core used by the UI wrappers. The global multi-buy strip is never read or changed.

Loadout order carries no gameplay effect — nothing the game computes reads the position a recipe
sits in — so there is no verb that reorders it, no `destination` argument on this surface, and no
`move` decision on the read. The slot a row prints is the address the player sees on the screen.

### Concept slots

A `concept-recipes` row is the pre-decision surface for `game_concept`. It carries `activeCount`,
the slot budget as `usedSlots` and `maximumSlots`, and `canAdd` as a decision rather than a bare
boolean: a refusal names its class and says whether every slot is taken or the game simply will not
take this recipe with room left, which is the difference between freeing a slot and picking another
recipe. The sentence says which of the two situations it is and leaves the counting to the pair
beside it, so the budget is stated once. The budget is published because assignments are only the
filled slots — counting `alchemy-instances` rows can never reveal the capacity behind them.

A Concept recipe is also an alchemy recipe, so `world_get` on one answers
`category: alchemy-recipes` and carries a `concept` block with `assignedCount`, the same slot pair,
and the `canAdd` decision. Answering the id under the one category and dropping the other half
answered a question the caller did not ask. `predicates.canAdd` names that block — its value is the path
`concept.canAdd` — rather than reprinting the verdict beside it, so one decision is published once.

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
count out. When the game's headroom delivered fewer levels than `amount` asked for, the answer adds
`requestedAmount` and `deliveredAmount`; a fully satisfied ask says neither, because an
under-delivery that read exactly like a satisfied `amount=1` let a caller batching its own
progression accumulate drift with no signal.

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
treasure collect pairs. Each row names both handles, shows the active quantity, and answers `add`
and `remove` with one word each: `yes`, or what stands in the way — `not_offered`, `ambiguous`,
`unpriced`, `plot_short`, `list_full`, `inactive`, `unverified`. The detail projection behind a
mutation or an explanation carries the full decision: the plot quantity one instance consumes, the
current maximum additional count (the game's own remaining-instance count, which can exceed the
10,000 this verb accepts in one call), and the sentence for a block. `unverified` is the
prerequisite latch the game evaluates only when the action starts — the action boundary performs
the exact native check instead of a read mutating the latch, so pressing is how you find out.

Call `game_agromancy(mode="add_plot_action"|"remove_plot_action", uuid=..., actionUuid=...,
amount=...)`. Every call requires an explicit positive `amount`.
Add uses the same active plot-action list control as `UIPlotNodeActionList.OnActionClick`.
Remove decrements an existing quantity; at the native minimum it uses that UI handler's distinct
`Cancel()` path, so crossing from several instances through the last one requires two calls.
Success returns the observed active quantity change. The only
postcondition is the exact pair's game-written active quantity moving in the requested direction;
refund behavior on cancellation is neither recomputed nor verified.

`agromancy-processing` is the screen's top processing strip in screen order. Each row reports its
slot, the strip `capacity` and `used` count, and its occupant: the named `plot`, named `action`, and
`amount`. A free slot reads `empty` under all three, which is what a separate `empty` flag used to
say about the three columns beside it; a strip whose queue the world did not publish reads
`unreadable` under `capacity` and `used`. Whether the occupant is engaged right now is not on the
row — it turns over between two reads with nobody playing. The former helper categories for harvest
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
`game_alchemy` with the published recipe pool, capacity-bounded slots, six type-capacity
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
selected loadout, and — when the swap moved the spell bar — a `spellBar` block naming the
`equipped` count as a pair plus the spells `unequipped` and `equippedNow`. The bar is player state
rather than loadout contents, so a select that empties it says so at the top of its own answer
instead of leaving `spells: -` inside the loadout's description to be read as the effect; a
select that leaves the bar alone publishes no `spellBar` at all.

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
`time_challenge`. Every row carries the native idle/queued/active/passed/failed run word, current
and maximum level, native next difficulty/reward, availability/completion verdicts, selection and
offer membership, and explicit `select`, `queue`, and, when active, `abandon` decisions. Challenge
selection has no resource price, so a row does not invent empty costs or affordability.

To read "which of these have I already beaten, and can I run them again?", page `challenges` and
read two columns: `level` — one per win, so `level: 1` is beaten once and `level: 0` is never — and
`state`, which says `available` while the challenge can be selected again. `run` answers a
different question, the one the *current* run is in, and it is `idle` on every row outside a run.

`time_challenge(mode="state")` is the screen itself, answered when a caller asks for it: ordered
fully named `selected` and `offers`, selection capacity, first-draw state, rerolls, and one `reroll`
decision under `challengeState`, with `prestigeState` beside it — the same top-level name and the
same block `time_prestige` returns, said once rather than nested a second time inside the challenge
block. It is a read, and it no longer rides challenge list/get pages — a request
for one row at offset fifty used to come back nine tenths ambient state, repeated on every page.
`resetOffers` appears only in the build where the Reset modal's list and the Time screen's list part
company; they draw from the same asset, so it is normally absent rather than said twice.

`prestigeState` is the persistent-reset pre-decision surface. It reports the reset's starting Time
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
4. Call `world_get` for the candidates that require comparison. No catalog name joins are
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
modifier touches it: `casting`, `authoredCosts` split into `cast` / `upkeep` / `hold` with each
named resource and its unmodified price, and `belongsTo` naming the recipe's `spellTypes`,
`coreGlyphs`, and `recipeBooks`. These are authored facts, so they do not move within a run; the
live price a cast will actually pay is the `castCosts` on the equipped instance, not
`authoredCosts`.

The `casting` block says what it means rather than what the game stores:

| Key | What it says |
| --- | --- |
| `castType` | `instant`, `channel`, or `aura` |
| `rechargeSeconds` | the authored recharge period |
| `rechargeCountsIn` | what the recharge counts down in: `time`, `spell-casts`, or `attributes-developed` |
| `rechargeUnitMultiplier` | what one counted unit is worth against the recharge. The game's own `Duration.Entry.GetMultiplier()` forces exactly `1` whenever `rechargeCountsIn` is `time` |
| `maximumChannelSeconds` | present only where the recipe authors one |
| `repeatEffectSeconds` | present only where the recipe authors one: the seconds **between** re-applications while a toggle is held |

Two of those names are deliberately not the game's. `rechargeProcessorType` named the processor
class the game constructs rather than the question a reader has, and it shipped as the raw ordinal
`0`. `repeatInstantEffectRate` is a naming trap: `Spell.InitializePersistence` hands it straight to
`TickTimer(tickTime, …)` as an interval floored at `0.01`, so the field the game calls a rate is a
period, and the obvious reading of a bare `repeatEffectRate: 1` is the reciprocal of the truth.

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
   player's staged UI selection. It returns named per-resource costs, overall affordability,
   and the named short resource when unaffordable — not the recipe, which is the caller's own
   argument read back. It runs every admission
   `add` runs before `add` stages anything — craftability, glyph duration/toggle requirements,
   usage requirements and budget, a free loadout slot, and loadout uniqueness — so a preview that
   comes back priced is a layout `add` will not refuse on the same arguments. Price stays an answer
   rather than a refusal: an unaffordable layout is priced with `affordable: false` and the short
   resource named.
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
recipe's `loadoutAdd.augmentOptions` names the owned augments — the discoverable population, all 22
of them — only where choosing them is the next decision.

### Spell loadout loop

`spell-slots` is the pre-decision surface for `game_spell_loadout`. Each occupied detail row names
the recipe the equipped spell was baked from, its slot, active cast/ready/attune state when
applicable, the game's current remove verdict, and whether that spell can move at all. Where it can
move is the slot list, which is one read for the whole bar: inlined per spell, explaining eight
spells delivered the same eight-slot roster eight times. Augment choices
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
   change, and a move onto an occupied slot adds `displaced` — the pushed-out spell and its own
   slot pair. A swap that reported only the half the caller named left the other spell at an
   address the caller's model no longer had.
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
is pending, so the row's existence is that fact and there is no column repeating it. The row is two
columns: the requesting effect, named the way the player sees it, and every eligible structure in
native order. Each candidate is fully named and includes current committed/effective level,
availability, and work-in-flight state — a locked candidate says `available: no` and stops there,
because the row is not refusing anything. Whether a random pick would land is whether `candidates`
holds anything, so no column restates it. Costs and affordability are absent because targeting
spends no resource. The requesting object's native class and the class of the selection it opened
are not on the wire: the two decisions this verb offers — submit one candidate, or let the request
pick — turn on neither, so they carried nothing to say in player words.

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

### The detail read

`world_get` accepts one canonical `uuid`, or a batch of them, and pins the latest immutable world
publication before it resolves or evaluates anything. Every block's row, predicates, requirements,
costs, and blockers come from that one publication; the tool neither retains a snapshot token nor
follows a newer publication during the call.

One block per id, in the order they were asked, so nothing echoes an index back. A block that
answered says nothing about having answered — silence is the yes, and inside a batch that silence is
also what separates the blocks that answered from any block that refused beside them. One id failing
refuses that block alone with the ordinary refusal grammar and every other block still answers.

**One block has one shape.** Identity is at the top level, the published row is under `row:`, and
the evaluated sections follow wherever this build has them — for every category, and whether the
call named one id or two hundred. Three layouts used to live at once: a category with no evaluated
sections answered as a bare field dump with no `category` and no lifecycle word while its own list
page said `locked`; an equipment type carried its `uuid`, `name` and `category` *inside* `row:`
because nothing had claimed the top level; and a single-id read whose block held only flat fields
rendered as a one-row table while the same id inside a batch rendered as an indented block. A
category with no predicates simply has no `predicates:` section — the same skeleton, honestly
empty, never a different dialect.

Named identity appears once, on the block, and carries the same `keywords` cell `world_search`
prints for that entity — same words, same order, same `, ` join. The family an entity belongs to is
part of its identity rather than a search-only decoration, and a detail block that omitted it read
as a contradiction of the search row that had just named it. The cell is absent, not empty, where
the game authors no words. The row underneath carries only its own columns, and the
handle, name, category and native type the block already published are not repeated in it.
`nativeType` is itself absent where the block's `category` names exactly one native class, which is
every category on this build but the ones whose rows are of mixed classes. The
`category` a block names is the one the world actually publishes the id in, not the one its runtime
type implies — those disagree exactly where a caller most needs the truth. The `row` is the same
curated player surface `world_list` pages for that category rather than the collector's complete
internal struct, so the lifecycle word a page shows is the word a detail read shows, said once, in
one place.

When the resolved native entity implements the audited `ITooltipable` contract, its authored
`GetDescription()` text leads the block after identity; no description is invented when that source
is absent. That description is a **live read of the game's own tooltip text for that one entity**,
not a captured fact: it is present only while a save is loaded, and only for the categories this
build evaluates detail for. A call with no published world refuses as a whole and names the
lifecycle state, so the menu never yields a half-answered block.

An id resolves its own table, so the categories this build evaluates no verdicts for still answer —
with their row, whose own handle and name are the identity in that case, and no decision blocks.
"There is nothing more to say about this one" and "this one could not be read" are different
answers, and a missing block used to say both.

#### What a keyword is worth

A keyword is a type asset, so "what is Ember worth" and "which spells are Embers" are one object
asked two ways. A detail read on a type carries a `worth` block; every other entity's block is
byte-identical to what it was, because no published type record names it.

`properties` is one entry per modifier record the type carries, and each entry states exactly one
magnitude under the name that says which kind it is. `distributedTotalPercent` is a record that
*hands its bonus down* — the moment a modifier lands, a transformed copy is pushed into every
member, so the member value the surface already publishes **already contains it**. Reading it beside
a member's number and multiplying is how one bonus becomes two. `value` is a record holding a number
of its own, which applies on top of whatever wears the type; all twenty-two `SpellTypeSO` records are
these, and they appear nowhere else on the wire. `howToRead` says the rule that fits the block it
sits on, so a spell type never reads about handed-down totals it has none of. `sources` names every
modifier currently on that record: who placed it, its amount in the game's own notation, which of the
five folds it is (`raw`, `diminishing`, `stacking`, `reduction`, `exponent`), and its order. A
distributor carrying nothing totals to a flat `100`, which is a reading rather than an absence.

`members` says how many things the keyword reaches, per class of thing, with the structure subtype
chain already closed over — a bonus on a parent type reaches every child's members too. It is absent
for a type nothing wears a keyword edge to, which includes every spell type: a spell's types are
published as spell-graph relations instead, and "0 things" would be a count of the wrong table.

```
row: uuid=a0f000, name=Ember, typeLevel=0, typeXp=0, isVisible=yes, isElemental=no
worth:
  howToRead: These are this type's own numbers, and they apply on top of whatever wears the type.
  properties 4:
    property: cooldownSpeed
    value: 100

    property: power
    value: 150
    sources 1
    [amount | effect | order | source]
    50 | diminishing | 0 | Deep Insight a0d000
```

Search rows are untouched by all of this. What a type is worth changes every time anything is
bought, it belongs to the reader who asked a detail question, and a list stays durable facts only.

Detail is per id and nothing is truncated: 200 ids is the ceiling and a 200-id batch answers at 200
ids of detail. Bytes lose to predictability here on purpose — a get has no offset to resume from, so
a budget could only drop answers a caller asked for by name.

The `predicates` and `blockers` blocks are always present, empty or not, so an entity nothing
applies to never reads like an entity nobody evaluated. Only applicable predicate slots are inside:
`visible`, `available`, `canDevelop`, `canPurchase`,
`canDiscover`, and `canUse`. Presence means applicable. Each slot answers under `available`, the same
word every other decision on the surface answers under, and a slot that answered no carries the
stable `reasonCode` saying why; absence means the predicate does not apply, not false. A predicate
points at the block that holds its evidence rather than reprinting it: `canUse` lists the slot
numbers the spell is equipped in, and the same response already carries those slots in full under
`row.equipped`; `canAdd` is the path `concept.canAdd`, where the whole decision is published.
Crafting purchase uses the
published `CraftingRecipeSO.CanBuyAt(GetStartingQuantity())` verdict, spell use uses the equipped
`Spell.CanCast()` reading, and structure/upgrade purchase combines published native availability
with the one exact-cost affordability lineage. No predicate emits implementation provenance or a
permanent never-evaluated apology.

A miss is `ERR_NOT_FOUND`, and the sentence and the `readWith` remedy are what separate the kinds of
miss. A UUID absent from the live identity registry says no entity in this build carries it and
points at `world_categories`, because the caller has no name to search with. A UUID in a table the
caller named that the table does not hold says so and points back at that table's page. A
catalog-known UUID no published table is addressed by says it is loaded in this build and points at
the rows that do carry it, or — when its runtime type belongs to no published table — says its
identity is all there is to read and points at `entity_catalog`. A UUID the asset catalog does not
know but the world published inside a composite row — an equipped spell instance is a runtime
object, not a loaded asset — points at `readWith: {tool: "world_list", category: "spell-slots"}`.
The surface never claims the process is ignorant of a UUID it published, and never points a runtime
instance at the asset registry that cannot resolve it.

Per-level structure, upgrade, and Research requirements preserve the implicit container `AND`,
explicit native `AND`/`OR` nodes, authored order, and recursively expanded prerequisite-link tiers.
Every operator node carries its `children` list, so an entity with no requirements reads as an
empty list rather than as an operator over an unstated set.

**A leaf leads with the four facts a player acts on**, in the order they answer the question: `met`,
what this row `checks`, what is `current`, and what is `required`, then the `verdict` and its class.
Everything else is how the suite reached that answer — where the row sits in the authored tree
(`nodeKind`, `ordinal`, `parentOrdinal`, `depth`), which native class it came from
(`conditionType`, `conditionKind`, `requirementNativeType`), the value the native evaluator selected
(`selectedValueKind`: `purchased_level`, `total_level`, `purchased_quantity`, discovery, mastery,
recipe, advancement, reached, numeric, or link gate), and the three thresholds the scaling passes
through (`baseThreshold`, `scaledThreshold`, `effectiveThreshold`). None of that is a thing a player
does anything about and all of it is what a defect in this evaluation is diagnosed from, so it keeps
every field under `diagnostics`, at the tail of the leaf, where a name says which of the two it is.
A live round met the one usable line in column ten of twenty under a header opening `nodeKind |
ordinal | parentOrdinal | depth | conditionType | …` and wrote down that it was buried.

A `requirements` block therefore leads with `suiteVerdict`, then `unmet` — one entry per unsatisfied
leaf naming the requirement, what it checks, what is held and what is wanted — and only then the
`checkLevel` and the whole authored `root`. `unmet` is absent when nothing is unmet, so its presence
is the answer to "what is stopping this" and its contents are the answer to "by how much".
Unsupported comparisons return a structured unevaluable result.

A leaf says what it compares under `checks`, in words: `at-least-level`, `at-maximum-level`,
`any-level`, `visible`, `discovered`, `at-least-quantity`, `available`, `at-least-mastery-level`,
`at-least-mastery-ready-level`, `at-least-maximum-level`, `at-least-advancement-level`,
`at-least-reached-level`, `at-least-value`, `at-least-count`, `any-visible`, `any-available`,
`first-tier-enabled`, `named-tier-enabled`. The game's own `reqType` ordinal is not published,
because it is not one vocabulary but ten — every condition class declares its own enum, and `2`
means "at least this level" on an upgrade, "at least this mastery level" on a spell, and "any
available" on a list. Each map is pinned from that class's own `InternalIsValid` switch, and an
ordinal outside it throws rather than reaching a cell. Two node kinds carry no `checks` at all: an
unmodelled condition class, whose row has nothing read to word, and an authored empty composite,
where the same slot holds the group's Any/All identity rather than a comparison.

The collector also captures the safe parameterized
`Prerequisites.Container.Check(Requirements.ConditionInfo)` answer at the exact next-purchase level.
The worker compares its graph verdict with that same-publication native answer. Missing inputs,
unevaluable suite math, a different owner/level, or a disagreement makes that id's whole block
`unavailable`; a disagreement returns both verdicts and `native_verdict_mismatch`.

Requirements met while the game still holds the entity shut is not a disagreement — the authored
rows are one gate among several, and the game folds in conditions it never published as rows.
`suiteVerdict` scopes itself to the rows it read, and `predicates.available` says what the game's
own gate answers, with its reason code. There is no `authority` paragraph beside them: it restated
`suiteVerdict: Met` in prose, and eight of its nine appearances in a live round sat under `root: no
conditions`, declaring a set of authored rows met for entities that have no rows at all. The
installed v1.05 contract additionally pins that a structure quantity requirement reads purchased
`quantity`, not `selfBonusLevels` or an effective/total level.

Research blocks separate base, scaled, and native effective requirement thresholds and retain
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
therefore occupy one line, not seven objects. Features and services are named by the id
`suite_automation` takes as an argument — `auto_buy`, not `Auto Buy` — so the nine features health
reports on are recognisably the seven that verb lists plus the two with no on/off button, rather
than a second vocabulary counting a differently sized set. It returns no structured payload because none of those
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

**The lifecycle generation counts lifecycle events, not games.** Every accepted observation bumps it
by one, and there are nine kinds — scene entered, scene exited, runtime ready, save-load started,
save loaded, reset started, reset completed, NG+ started, registry rebuilt — of which a single save
load fires several. One load moving it 2 → 9, and a teardown adding two more, are both correct
readings; the step size is not a count of anything a player did. Only comparison is meaningful: two
readings that agree describe the same run, and two that differ mean every handle, id and world fact
held across them is void. That is why `time_prestige` reports it as `{before, after}` beside a sentence
saying what happened rather than as a number to subtract.

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
not a timeout to lengthen. One verb skips the wait outright: a purchase answers from the
queued-level delta its own native verifier observed, so no later world can add to it, and waiting
for one could only turn a purchase that verifiably committed into a timeout.

A commit answers with the facts its own press changed, each as a `{before, after}` pair, plus any
fact the press produced that has no "before" — a battle's result and spoils, a settled level, the
price it drew. When the settled world cannot prove the change, the answer is `postStateUnavailable`.
No verb re-reads a whole screen and no verb appends what is possible next: the decisions a press
reopened are read with `world_get`, which is where every other caller reads them.

**A mutation the game queues answers differently, and that is the whole rule for it: success,
`queued`, and stop.** A purchase, a research develop, a craft, and a consumable use do not apply
when they are pressed — the game takes them into a queue and drains it over the seconds or minutes
that follow. Every count involved therefore moves again while the answer is being written and has
moved back before the caller can read it, so a `{before, after}` pair over one reports a transition
nobody can confirm: round 9 shipped `level: 0 -> 0` beside `queuedLevels: 0 -> 1` for a purchase
that landed on level 1, and the honest reading of that pair is "it failed". These verbs say what
the press did and nothing else — `queued: N`, the count the mutation's own sentinel observed, and
`queued: yes` only where no count is knowable. The count of one is said as `1` like every other:
these verbs promise "how many", so answering the commonest press with a bare `yes` throws away the
observation it was holding and sends the caller back to re-read the entity. Where the queue now
stands, what the level is, and what the next one costs are reads. A delivery short of the ask says
both numbers on one line (`queued: 1 of 1000 asked; …`), because a partial that looks like a
satisfied `amount=1` is the one shape a caller cannot act on.

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

A sentence names entities the way a player does — display name only for the entity the request
addressed, name plus handle for a *second* entity the sentence points at, which is an address the
caller can act on. The asset name, the whole UUID, and the native member that decided are identity
and evidence, and the same response already carries them as fields, so repeating them inside prose
only made the sentence harder to read. Two exceptions stay deliberate: a sentence falls back to an
id for an entity with no known name, because naming the only handle there is beats naming nothing;
and a `contract_unavailable` or faulted result still names the native member it could not read,
because that result is a defect report and the member is its subject.

That rule reaches the **shared** terminals too, not only the sentences a producer writes. The eight
outcomes every action boundary can end in — committed, emergency stop, lifecycle replaced, service
disabled, native rejected, policy rejected, adapter fault, skipped — say what happened to a player,
in a whole sentence: which side refused, whether anything reached the game, and whether the result
is a fact about the world or a fact about the suite. They used to be internals labels, and
`native_rejected`'s in particular read as an admission notice about a boundary the caller has no
name for. Nothing on the wire ever prints a raw exception message, a stack trace, or a runtime type
as its explanation: a fault says it could not prove what it needed to and that nothing here is a
verdict about the game, and the exception belongs in the log.

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

**One class per fact per response.** A response never carries two classes for one fact. Where a
response answers the same fact twice — `world_get` publishes an entity's row under `row` and its
evaluated verdicts under `predicates` — the predicate block is the authority and the row keeps
only the fact, not a second opinion about it. Two classes for one fact make the taxonomy unusable
for control flow: a caller branching on one runs a different program than a caller branching on the
other.

**A no with no axis is a lock, not a refusal.** A read that publishes `available: false` and names
no reason is the game holding something shut and publishing no condition for it, so it answers
`ERR_LOCKED` and says exactly that. It used to answer `ERR_REFUSED` and a sentence announcing it had
no information, which invented a refusal of an action nobody had asked for and disagreed with the
predicate beside it. Where the world does publish the gate the producer names it instead — an
unlearned glyph says whether a discovery is what stands in the way.

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
| `ERR_INPUT` | `invalid_uuid`, `invalid_offset`, `invalid_limit`, `unknown_category`, `unexpected_for_mode`, `slot_out_of_range`, `configuration_write_rejected`, `screen_match_failed`, `composite_identity_required`, `discovery_surface_ambiguous` |
| `ERR_NOT_FOUND` | `unknown_uuid`, `slot_empty`, `not_active`, `no_pending_target`, `no_current_offers`, `recipe_has_no_core_glyph` |
| `ERR_STATE` | `invalid_state`, `already_maxed`, `already_developing`, `switch_blocked`, `slot_occupied`, `reroll_already_used`, `immediate_required_discovery`, `cast_in_progress`, `charge_unavailable`, `spell_not_chargeable`, `batch_spend_drift`, `resources_uncovered`, `attuning` |
| `ERR_LIMIT` | `amount_unavailable`, `automation_full`, `loadout_full`, `destination_full`, `research_queue_full`, `no_rerolls`, `level_cap_reached`, `artificial_research_cap_reached`, `research_investment_cap_reached` |
| `ERR_UNAFFORDABLE` | `unaffordable`, `usage_unaffordable`, `level_not_affordable`, `insufficient_quantity`, `insufficient_bandwidth` |
| `ERR_LOCKED` | `not_available`, `native_unavailable`, `hidden_or_undiscovered`, `native_hidden`, `hidden_discovery`, `requirements_unmet`, `requirement_unmet`, `native_not_discoverable`, `recipe_not_discovered`, `not_discovered_or_offered`, `prerequisites_unmet`, `core_glyph_not_owned`, `cannot_level`, `research_leeway_exhausted`, `native_leeway_exhausted` |
| `ERR_UNAVAILABLE` | `world_not_published`, `lifecycle_no_game`, `contract_unavailable`, `post_state_timeout`, `category_not_collected`, `configuration_unpublished`, `runtime_not_available`, `price_unavailable`, `affordability_unavailable`, `requirement_unevaluable`, `threshold_scaling_unavailable`, `requirement_cycle`, `requirement_depth_exceeded`, `queue_not_published`, `queue_reading_inconsistent`, `entity_catalog_unavailable`, `topology_not_captured`, `owning_screen_unknown`, `owning_screen_unreadable`, `owning_screen_contradictory`, `owning_screen_status_unmodelled`, `owning_screen_availability_unreadable` |
| `ERR_REFUSED` | `native_rejected`, `native_purchase_refused`, `native_can_develop_refused`, `projection_refused` — the game's own gate said no and reported nothing else |

Four of those placements are worth reading twice, because the obvious guess is wrong.
`slot_out_of_range` is `ERR_INPUT` and not `ERR_LIMIT`: the caller named a slot the list never had,
which is a bad argument rather than a ceiling reached. `configuration_write_rejected` is `ERR_INPUT`
for the same reason a dial value outside the game's range is: one kind of no is one class wherever
it happens, and a class that changed with the verb taught callers it described the tool. `cannot_level` is `ERR_LOCKED` and not
`ERR_LIMIT` for the reason its own row gives — no level list in this game has a ceiling, so a shut
level gate is always a gate rather than an exhausted supply. Both leeway codes are `ERR_LOCKED` and
not `ERR_LIMIT`: research leeway is a gate the game opens as the requirement level moves, not a
supply the caller spent.

`ERR_REFUSED` is the `native_*_refused` family and nothing else a producer can explain, and it is a
**mutation** answer: the game was asked to do something and said no. No read reaches it, because a
read that cannot account for a shut gate has learned a lock rather than witnessed a refusal.
A code that lands there because this map has not met it is a defect in the map, not a new kind of no.

A feature result number is not a wire word: it names no axis a caller can act on, so an unmapped
native result reaches the wire as `ERR_REFUSED` with the producer's own sentence. **No response ever
prints the integer.** A refusal that reaches the last-resort sentence says which boundary refused and
that it gave no reason of its own — the number stays in the log, where a maintainer can trace it.
A boundary that lands there is a defect in that producer, not a new kind of no.

A check that **passed** carries no class at all, and no sentence either. There is no success code:
every affirmative producer word — `requirement_met`, `recipe_discovered`, `visible`, `ready`,
`can_buy`, `below_level_cap`, `below_research_cap`, `native_leeway_available`,
`native_develops_below_caps`, `queue_room_available`, `drain_available`,
`output_capacity_available` — renders as a bare `yes`.

A blocker's reason states the same verdict its `blocked` flag does. Research leeway is the case that
proved the rule needed writing down: the game develops on leeway **or** on being below both caps, so
a spent leeway under open caps blocks nothing. The reason used to be picked off the leeway term
alone while `blocked` was computed from the whole gate, and the block published `blocked: no` beside
"Native leeway exhausted." — a field contradicting its neighbour. That third state now has its own
word, `native_develops_below_caps`, and it is a passing one.

What each internal code means is below; the class is how it reaches the wire.

| Code | Meaning | Surfaces |
| --- | --- | --- |
| `already_maxed` | The target has no level, use, or purchase left to buy | `game_purchase`, read-side develop and purchase decisions |
| `cannot_level` | The game's own per-type level gate is shut. `game_level` has no ceiling code because none of its four types has a ceiling: every one answers `ILevelable.CanLevel()` unconditionally true, so an exhausted level target is not a state this surface can reach | `game_level` |
| `unaffordable` | One or more named resources fall short. The sentence names every one of them: `Needs <cost> <Resource> (have <held>); …` | every purchase-shaped mutation and every read-side cost decision |
| `amount_unavailable` | The exact amount asked for exceeds what this call admits, and a smaller amount is what fixes it. Carries `maximumAmount` | `game_research develop`, `game_concept`, `game_equipment`, `game_alchemy`, `game_agromancy` |
| `not_active` | The target has nothing active to remove, so no amount succeeds. It used to share `amount_unavailable` with three refusals a smaller amount does fix | `game_agromancy` removes |
| `automation_full` | Every automation slot on the queue is in use. Only a queue genuinely out of room answers this; an undiscovered recipe answers `hidden_or_undiscovered` | `world_get` crafting rows, `game_craft automate` |
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
| `owning_screen_unknown` / `owning_screen_unreadable` / `owning_screen_contradictory` / `owning_screen_status_unmodelled` / `owning_screen_availability_unreadable` / `topology_not_captured` | The five distinct ways the purchase-screen admission chain says no, which used to share one number. Only `topology_not_captured` is fixed by waiting for the next lifecycle; its sentence names the epoch the topology is stamped at, the epoch the call asked for, and how many rows it holds | `game_purchase` |
| `destination_full` | Every slot this upgrade would fill is already occupied | `game_purchase` on a slot-filling upgrade |
| `native_rejected` | The game refused and the published world does not explain why | any native mutation, reserved for exactly that case |
| `native_unavailable` | The game keeps this shut and publishes no condition that would open it. The read-side counterpart of `native_rejected`, and the answer a bare `available: false` reaches | every read that publishes availability, and the glyph and component decisions that act on one |

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
at 200; every other `amount`,
`slot`, `offset`, and dial `value` — `game_alchemy` and `game_equipment` among them — declares no
ceiling at all, because the suite has no opinion there and the native bound decides. Where the suite
has no ceiling it publishes none: the schema omits `maximum`, and a below-floor value is refused
with "must be N or greater" rather than with an `int.MaxValue` placeholder printed in the shape of a
bound the game never chose. None of the declared ceilings is read from the game, none of them is a
running budget, and none of them appears in any response. A value inside the schema bound is
therefore not admitted yet: the action boundary re-reads the native bound and refuses with
`amount_unavailable` and the live `maximumAmount` when the two disagree. `game_purchase`'s live
bound is the game's own action queue, read at the boundary: an ask beyond the room it holds above
the operator's reserve is refused with that room as `maximumAmount`, never clamped down to it. Auto
Buy's planned batches still clamp, because a plan that takes what fits is the planner working — but
a caller who names an amount is saying what it wants to have happened, and silently turning 1,000
into 1 is indistinguishable from a satisfied `amount=1`.

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
| `maximumBatch` | how many levels one queued develop would take, the multi-buy target clamped by the queue's own room | `game_research`'s develop block, queue route only |

A decision block carries a bound exactly when the verb it decides takes the input that bound caps.
`game_consumable discard` publishes `maximumAmount` because `discard` takes an `amount`; its sibling
`use` publishes none because `use` takes none — one call, one consumable. Which arguments a mode
accepts is declared once, in that tool's `inputSchema` mode rules, and `use` lists `amount` as
forbidden there; a decision block is where the game's live answer lives, never a second copy of the
call signature.

Two shapes were retired rather than joined: a bound named only in an English sentence, and a
refusal quoting an `int.MaxValue` placeholder as if it were the game's limit. A
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

That rule has one boundary, and it is the screen's. A `level` is a bounded cardinal for every
category that keeps one in an `int`, but the type assets hold theirs as a magnitude, and a magnitude
at or above a thousand is written in the screen's Scientific style — a rounded two-digit reading.
Turning that reading back into a JSON number published two notations for one quantity in a single
response (`level=10300` on the row, `value: 1.03e4` in the worth block below it) and invented a
precision it never had, since every level from 10,250 to 10,349 reads `1.03e4`. So the cardinal
shape is taken only where the published string is already that integer's plain spelling, which is
every count the screen writes plainly and no reading it rounds.

### Presence semantics

A field or collection is absent when the suite did not collect it, and the response says so with a
named `…Unavailable` fact rather than by silence. A collection that was collected and is genuinely
empty is present and empty.

#### List columns are a declared, total set

A `world_list` category's columns are declared, and every row fills every column in every world
state; `world_get` shares the shape, so it follows. A field is either always in that set or deleted
outright — nothing conditional. Absence in a table never means "not applicable": the cell says which
fact does not apply, using a word, never a number that would be read as one.

| Word | The fact the cell names |
| --- | --- |
| `uncapped` | no ceiling applies — the game's marker is a negative native maximum |
| `unpriced` | the publication names no price for this row |
| `unevaluated` | a price is published, but this generation carried no same-generation holding to compare it against |
| `unreadable` | the suite could not read this fact from the game this generation |
| `empty` | the slot holds nothing |
| `manual` | the entry is not automated, so it repeats no number of times |
| `unslotted` | the loadout does not hold this recipe, so it occupies no position |
| `-` | nothing here: no value published, an empty collection, an empty string, or a cell with nothing in it |

This costs almost nothing to read, because a column holding one value across a page is said once on
the share line and then not on any row: a page of uncapped upgrades renders `these 6 share:
maximum=uncapped` and the column is not in the bracket below it. What it buys is that the set of
columns a category has stops shifting with world state — the page that taught nothing about caps was
exactly the page whose every upgrade was uncapped.

Where one column already answered a second column's question, the second is gone rather than
totalized — a flag whose only job was to explain the absence beside it says nothing once the
absence is spelled:

| Category | Column | Says instead of going absent | Column deleted with it |
| --- | --- | --- | --- |
| `upgrades` | `maximum` | `uncapped` | `remainingLevels`, `available` |
| `upgrades` | `affordable` | `unpriced` | — |
| `structures` | `affordable` | `unpriced` | — |
| `resources` | `capacity`, `atCapacity` | `uncapped` | — |
| `purchase-costs` | `spendableAmount`, `affordable` | `unevaluated` | — |
| `alchemy-instances` | `drainRatio` | `unreadable` | `drainReadable` |
| `alchemy-loadout` | `slot` | `unslotted` | — |
| `crafting-queue-entries` | `repetitions` | `manual` | `automatic` |
| `spell-slots` | `spellRecipe` | `empty` | `occupied` |
| `agromancy-processing` | `plot`, `action`, `amount` | `empty` | `empty` (the flag) |
| `agromancy-processing` | `capacity`, `used` | `unreadable` | — |

Categories with no hand-written projection are rendered straight from their declared field list, so
totality there is structural rather than per-category: a declared field the row carries nothing
under reads `-`, and so does one holding the zero identity, because a handle that addresses
nothing is not an entity and dropping it would take the column with it. A reference column keeps
the name a filled one would have had — `selectedLevel`, not `selectedLevelId`.

#### Outside a table, absence is silence

The `-` mark is a table mark and only a table mark. In a table there is a header promising a column
and sibling rows to line up with, so a cell has to say something, and a member the game published as
an empty string gets the same mark an absent one gets, because the column has to survive. A
`world_get` block, the row inside it and a mutation's post-state have none of that, and there the
same fact reads the way absence reads everywhere else on this surface: **the key is simply not
there**. This is the
`game_cast` policy — a spell with nothing to toggle publishes no `toggleOff` — generalized to every
non-table surface, and it also ends a split spelling of one fact, because the wire normalizer already
drops the zero identity from every projection that declares no paths.

For the same reason a block with nothing to say is omitted rather than rendered empty. A targeting
post-state used to carry `targeting: pending=no` on every response where nothing was pending; the
block is the request, so no block is the answer that no request is waiting.

Two things this does **not** touch. An empty *collection* stays present and empty — a collection that
was collected and is genuinely empty is evidence, and that is a separate ruled distinction from a
block that had nothing to say. And a block published with its verdict — `predicates.<slot>`, the
blocker blocks — stays published when the verdict is no: absent there means the predicate does not
apply to this entity, which is a different fact from a predicate that applies and passes.

A row carries no verdict pair at all. `reasonCode` and `reason` are the refusal grammar, and a
table refuses nothing: what blocks a row is one word in the column that asks, or the column that
already states the fact stating it alone. So the declared set is the whole truth — a page holding a
blocked row is exactly as wide as the same page without one, and an undeclared column in a row is
an error with no exceptions.

The declaration is enforced rather than described. Every row a hand-written projection builds is
checked against its category's set as it is built, so a column left off one row or invented for
another refuses the read instead of reaching a page — the same loudness a category with no scan
projection already has. A row the suite could not fully read is a different shape, not a shorter
one: it keeps the whole declared set under `partialRow` and states the incompleteness beside it.

An identity is a handle and a name, and nothing else. The asset name (`internalName`), the runtime
type (`nativeType`) and the category the type implies are catalog-browsing facts: `entity_catalog`
and a `world_get` block publish them, and no world row or reference carries them. Stamped on every
identity they cost 21.1% of one live round for a fact nothing on that round read.

**`name` is the word the game shows a player, or it is not there.** About five hundred of the
catalog's assets — the variables, the list holders, the scaling weights, the tutorials — carry no
authored word at all, and the surface used to stand the Unity asset id in for one: a page of
`int-variables` read `SummonedLevel`, `QuickConsumableSlots`, `MaxRasterizedThoughts` in the column
every other page fills with a real name, and nothing on the row said which kind of label it was.
Now such a row publishes no `name` — `-` in a table, absent in a block — and carries the asset id
under `internalName`, which is where that fact already lived. Nothing is lost: the row is addressed
by its id either way. The `nameSource: asset` flag that used to admit the substitution is gone with
the substitution.

Absence therefore never doubles as a value. Every key that once used it to mean "no" now says so:

| Key | Absent means | Present-and-false/empty means |
| --- | --- | --- |
| `predicates.<slot>` | the predicate does not apply to this entity | published with its `available` verdict, and a `reasonCode` when that verdict is no |
| `discover` | nothing: every glyph carries the block | `available:false` with the reason, including `native_not_discoverable` for a glyph the game never offers |
| `queuedLevels` | nothing: every level-bearing row carries it | `0`: nothing is in flight |
| `lastRun` | the game retains no record of a run: a cleared `wavesCompleted` with no spoils is the state a ritual nobody has played is in, and `RitualSO.IsFailedRun()` is `wavesCompleted < 5`, so an unconditional verdict would call every untouched ritual a failure | a finished run's `result`, `wavesCompleted` and `spoils` |
| `spoils` | on a row that is `inBattle`, that the running battle has banked nothing yet. On one that is not, see `lastRun` | `[]` on `game_ritual end`: the run banked nothing |
| `openModals` | **deliberate progressive disclosure**: no modal is covering the board. `openModalsUnavailable` with a reason appears when the read itself failed, so silence is never a failed read | the game-written title of every open `UIModal` |

`openModals` is the one key whose absence is still a value, and it is a documented choice rather
than a gap: an empty array on every screen read would spend bytes on the ordinary case to describe
the rare one.

A committed purchase reports one count, `queued`, and the queue rule above is why: the two counts
the screen owns, `level` and `queuedLevels`, both move again while the answer is being written, so a
`{before, after}` pair over either reports a transition nobody can confirm. Publishing their sum
under one name — the retired `committedLevel` — was worse still: a number no screen shows, hiding
which of the two the purchase actually did.

A committed purchase reports the levels it bought and nothing about what it charged. It used to
carry `paid[]` and `costPerLevel[]`, and both were bookkeeping rather than an answer: the paid rows
priced the game's own multi-buy setting at capture time rather than the count the call turned out to
commit, so an `amount=25` call reported one level of a rising ladder as though it were the whole
charge — understating spend, and understating it in the direction of believing there is more left.
`queued` is what says how many levels were bought. What a level costs and what the
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

**One transport per kind of answer.** A refusal about what a known tool was asked is a tool result
in the same one-line shape as every other refusal — `refused (ERR_INPUT): …` — never a JSON-RPC
error beside it. A caller branches on what went wrong, not on which of two shapes the answer
arrived in. Only a request that never named a tool this server has is a protocol error: an unknown
method, a malformed envelope, or an unknown tool name. An argument refusal reports every schema
problem it detected in one sentence, each naming its offending field, and there is no parallel
array repeating those same facts in a machine shape — the page is the one representation.

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
  '{"mode":"fire","slot":1,"uuid":"SPELL_UUID","charge":true}'
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
`discoveredCount` and `mode` as pairs, with whether the tree still has discoveries left. The count
travels with the `discoverableCount` it is a count out of, on the tree row and on the confirmation
alike: the two are the game's own cached `totalDiscoveredCount` and the size of the very list
`CountDiscoveredItems()` counts it from, so `3` and `3 of 40` are not the same answer to how far
into a tree a caller is. It does not
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
A settled `fire` says `casting: yes` — the press started a cast now — and carries no cast counter at
all. The game writes that counter when a cast *completes*, frames after the press and sometimes
before the next world is published, so it was the same number whether the press landed or was
dropped. A press at a spell that is already running is refused rather than committed silently — the
game's own button answers it with a warning popup or an end-of-cast, never with a new cast — so a
repeated fire can never look like a firing loop that is doing nothing. `casting` is a `fire`-only
key, and deliberately: it is the one mode with a native delta behind it, a release being a native
call with nothing to verify a cast start against and a toggle-off's own sentinel being a cast
ending, so neither of those may claim the fact in either direction.

Every mode answers with the same keys, and a key that can be `yes` says `no` rather than
disappearing. `active` — the settled running state — and `charging` — whether this press left a
charge held — ride on all three. Both used to vanish when they were false, so a `release` response
was silent about the very hold it had just let go of, and a caller could not tell "the hold is over"
from "this mode does not speak about holds".

The settled response reports the game's own readiness term under the name of that term,
`castReady` — `Spell.CanCast()`, the same fact the `spell-slots` row publishes under the same name.
It is **not** a promise that the next press lands, and must not be read as one: the game's own
`Spell.Fire` asks `IsCasting()` before it asks `CanCast()`, so a spell that is already running
answers `CanCast()` with true while the press starts no cast at all. `casting` beside it is what
says a cast is under way. The field was called `ready` and could not survive the name — a fire
answering `ready: yes, cooldown: 0` invited the press this same tool then refused on the very next
call. Whether a press would land is not answerable from a published world at all: the remaining
terms are a live per-spell target-selector query and a global targeting interaction, and no world
publication holds either.

`fire` takes an optional `charge`, default false, which is the player's held cast button: the spell
charges instead of firing at once, and `release` lets it go, landing more power the longer it was
held. The other two modes reject the argument. A charged fire answers `charging: yes` as well,
because a held input is a thing this call put down that the caller has to pick back up and no
loadout row says one is outstanding. Charging is offered by the game only on spells that scale with
it once Charged Spells is researched, so `charge` at a spell that offers none is refused
`spell_not_chargeable` rather than fired plain under a charged name. That question is asked of the
live spell the named position resolves to, never of the published loadout: a slot that moved,
emptied, or left the bar since publication answers `slot_identity_changed` saying which of those it
was, so a caller is never told a spell has no charged cast when what actually changed was the slot.

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

`pause` and `resume` are UI-reachable only while Research Queue Mode is off, and a call made with it
on is refused as `ERR_STATE`, not `ERR_INPUT`: nothing is wrong with the mode the caller named, and
turning the game's own setting off reopens both controls.

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
and internal nested policy objects. A setting whose stored value is a comma-joined list of whole
UUIDs — an allowlist — is published as that list, so each entry crosses the wire as the named
handle every other entity reference on this surface uses instead of as hundreds of characters of
raw id a caller then has to resolve one by one.

`mode="describe"` is where the rest lives: each setting's type, the values it accepts, and the
sentence saying what it does. Those three do not change between calls, so the ordinary read does not
carry them — a caller reading current values pays for values. The accepted values are said the same
way whichever kind they are, a range for a number and the list of names for an enum, so no caller
has to learn two spellings of "what may I write here". A setting that declares a range also carries
that range as the numbers `minimum` and `maximum`, the same two fields a refused write hands back,
so no caller has to parse a range back out of a sentence before it may write.

`suite_config_set` commits through `AutomataConfigurationStore`, the same single publication path
as the in-game controls. BepInEx
parse/domain validation runs before publication. Compatibility acknowledgements, shortcuts, and
STOP are not generic writable settings. A commit returns `setting.value` as a `{before, after}`
pair, the same shape `suite_automation` returns `on` in, because what a write changed is the pair
and not the endpoint. A write refused for its domain returns the setting, the `requestedValue`, and
the declared range as `minimum` and `maximum` read off the entry itself — BepInEx's own
config-file wording is never spliced into the sentence, so the surface no longer says
"must be From 0 to 60".

One setting's ceiling is not declared on the entry but read off the game: `AutoBuy/LeaveQueueSlots`
reserves native action-queue slots, so a value at or above the live queue capacity leaves Auto Buy
no slot it could ever queue into. The write is refused with the same `minimum`/`maximum` fields, a
sentence naming the capacity that refused it, and no mutation — otherwise the feature would keep
reporting `on: yes` while buying nothing. This is why `suite_config_set` captures the world: the
ceiling is the game's to say. Before a save is loaded no queue is published, no ceiling is known,
and the write is admitted rather than refused against a capacity nobody read.

`suite_automation` is the seven green/gray automation buttons as booleans, because that is what
they are: `auto_buy`, `auto_cast`, `auto_concept`, `auto_harvest`, `auto_items`, `auto_scribe`, and
`mentor` are each a `{Disabled, Active}` setting with no third state. `mode="list"` returns every
feature as `{feature, name, on}` and takes nothing else; it also carries the two suite-wide
switches when either is silencing all seven, because a list of on buttons would otherwise answer a
different question than the caller asked. Every row carries the name the Mods rail renders — "Auto
Buy", "Orb Mentor" — because a page that published one only where it was not the feature id in
title case left `-` on six of seven rows, which reads as a feature the suite could not name, and
asked the reader to derive the rest by a rule the page never stated.

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

1. Settled-delta pairs for a mutation that applies when it is pressed: a commit answers with the
   facts its own press changed, each as `{before, after}`. A mutation the game **queues** answers
   `queued` and stops instead, because a pair over a draining queue reports a transition nobody can
   confirm — *Inline action results*.
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
22. One price shape wherever a price is said — `cost`, `spendableAmount`, `affordable`, then the
    resource — whichever verb built the row and whichever member the producer read it from —
    *How a response reads*.

Four shapes this list used to protect are retired, and a round that reintroduces one is undoing a
ruling rather than restoring a contract:

- **The cast-counter echo.** A cast press answered with a counter that had not moved yet, because
  the game writes it when a cast finishes rather than when one is pressed — so the pair reported no
  change on every landed press. Demoting the pair to a bare total kept the same defect: a landed
  press and a dropped press were byte-identical. The counter is off the fire response entirely, and
  the press answers what it did — `casting: yes`. `{before, after}` remains the idiom for facts that
  did move, and the ones a press always moves ride whether or not the numbers differ — *Inline
  action results*.
- **The `next {…}` affordance block.** A commit answers with what its own press changed; the
  decisions that press reopened are read with `world_get`, where every other caller reads them —
  *Inline action results*.
- **Identity preambles.** The asset name, the runtime type, and the category the type implies rode
  every identity for 21.1% of one live round and nothing read them. They live on
  `entity_catalog` and the `world_get` block now — *Presence semantics*.
- **`paid[]` and `costPerLevel[]`.** A commit reports the levels it bought; what a level costs and
  what the next one asks are read on `world_get` and `purchase-costs`, where the whole curve is —
  *Presence semantics*.

## Screenshots and navigation

`game_screenshot` has no required parameters and returns an MCP `image` content block with
`mimeType: image/png`. A capture costs its reader whole 28-pixel patches — `ceil(width / 28)` x
`ceil(height / 28)` tokens — so pixels are the entire price and neither the image format nor its
compression enters it. That is why the PNG path is unconditional and there is no quality knob:
lossy encoding would buy wire bytes, which are free, at the cost of readability, which is not.
Every capture arrives 896 pixels wide — 32 patch columns, the narrowest width at which every class
of on-screen text stays readable through the suite's resampler. There is no width parameter,
because that choice has one answer: a narrower capture stops being readable and a wider one costs
patch columns for pixels no reader gains anything from. The response reports the encoded `width`
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
settlement bound. A completed close answers `dismissed: <modal title>`, read off the control before
it shut, so the caller confirms which modal went away without a screenshot; an untitled modal
publishes no `dismissed` rather than an empty string. Timeout remains committed, carries the same
`dismissed`, and says the post-state is unavailable because the verified close already began. It
does not click modal-specific confirm, purchase, reset, or destructive buttons.

`game_screen_catalog` reads the live Main-scene UI. Top tabs retain native rail order. Current
subtabs are active `UIViewRadioButton` controls under the current native content area. Inactive
popup templates are excluded. The response is a structured `scene` plus ordered `screens`, each with
its `label` and `active` flag; the active screen additionally carries `subtabStrips`, where every
independent strip names its `active` label and its ordered `labels`. Unity hierarchy paths and
unstable numeric indexes are deliberately absent. Inactive tab content is not instantiated, and the
audited v1.0.5 data and scene assets do not carry an authoritative tab-to-subtab roster. The catalog
therefore omits inactive subtabs rather than navigating speculatively or guessing labels.

`game_navigate(screen, subtab?, uuid?)` accepts exact labels only. It answers in words — the
arrived screen, its strips, and any open modal. `game_screenshot` is the only tool that captures the
framebuffer, so a caller that wants a picture of where it landed asks for one, and a caller that
does not is never charged patch columns for arriving. Name matching is
ordinal and closed-world: zero or multiple matches reject with the exact candidate labels. Plot selection resolves
the supplied UUID as a published `PlotNodeSO` and invokes the one audited active
`UIPlotNodeList.OnNodeClick(PlotNodeSO)`. It is not a hardcoded Fruit Tree command.
For a compound request, the server selects the top screen, waits up to one second for the active
screen and complete live strip set to remain stable across frames, and only then resolves and
selects the requested subtab or plot. Resolving against the settled hierarchy is what makes the
subtab candidates the matcher searched identical to the ones the catalog advertises for that screen.
It then waits for settlement again before answering. A timeout stays committed but returns only `postStateUnavailable`; it never labels a
mid-transition strip set as settled. The whole operation
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
`UIModal.IsOpen()` up each element's ancestry and lists none of them, so what it counts is what the
player can hover rather than what is instantiated.
A screen's elements hang off a handful of panels, so the catalog is a list of panels: each says the
ancestry its own elements share once, as `pathPrefix` and before them, and each element under it
carries only its own last segment. The shared part is computed over the panel rather than over the
page, because one prefix over a mixed page is only as deep as its most distant pair of rows — taken
over a page spanning three panels it collapsed to a canvas name, leaving every row of every panel
repeating some 150 identical characters of its own panel's ancestry. `pathPrefix` is present on a
panel exactly when its elements have ancestry above their own last segment, and it is printed above
the elements it explains rather than below them, which is where a key a reader needs to read row one
belongs. `game_tooltip` therefore resolves a row by the tail it was handed: any tail of a live path,
matched at a segment boundary, up to and including the whole path.
A tail naming more than one live element is refused rather than resolved to the first, and the
refusal says to prepend the `pathPrefix` the catalog returned with that row — two scroll lists on one
screen hand out colliding tails routinely, and the prefix is what tells them apart. A tail naming
none says to re-read the catalog instead, because the screen has moved on.
The reply is compact plain screen text.
The
catalog includes the owning UUID when the assigned tooltip item is itself an identity-bearing game
entity; control-only rows retain the volatile current-screen path and name. The
reader walks the native node, linked-tooltip, nested-tooltip, and currently inspected-panel graph
on Unity's main thread, but its node structure, repeated paint, empty arrays, duplicate authored
text, and identical alternate tree are wire-internal ceremony and never ship. A body whose closing
block repeats the block immediately above it says it once: adjacent duplicate lines were already
dropped one at a time, which never caught a panel that painted its whole last block twice. Nor did
either catch an inspected panel painting the same entity from its own object, which is not the same
reference and so was appended in full — every statistic a second time as a bare value block, 40% of
the response and the half with nothing in it. **A block every line of which the body already says
is not appended at all.** Block is the level this is judged at: dropping a repeated *line* would
take the second statistic that happens to read `0` and leave its label with nothing under it. The
alt tree is one such block rather than a special case — it used to be kept whenever it differed from
the primary block as a *sequence*, which is exactly what a resource pill does: it threads its values
through the nested statistic definitions that explain them and its alt paints the same values bare,
so the two sequences differ line for line while the alt says nothing new. That body ended in the
same five numbers twice with nothing to tell the copies apart, and the closing-block pass could not
reach it because the earlier copy was interleaved rather than adjacent. A cycle or hard
depth/node bound is rendered as one explanatory line rather than recursively expanding forever.
Unity rich-text markup is stripped. The list of tags is closed so that prose holding an angle
bracket survives, which means it has to hold every word the pinned build actually authors: a census
of `data/game-data.json` finds eight — `emph`, `emph2`, `deemph`, `warn`, `lore`, `negative`,
`positive`, `color` — and a portable test holds each of them to being stripped. Computed text delegates run inline; the reader never clicks a
node, renders a panel, or captures the framebuffer.

```sh
tools/game-mcp-client.py tooltips --limit 25
tools/game-mcp-client.py tooltip 'PATH/FROM/CATALOG/ROW'
```

The audited manifest covers the native tooltip carrier/open/nesting shape, while the real-reference
build and installed contracts verify the source node graph that the prose renderer consumes. The
same audited `ITooltipable.GetDescription()` contract supplies authored descriptions for
`world_get` when the resolved entity implements that interface.

## Checking the suite's math against the game

`suite_check_game_math` takes no arguments and runs the same differential check the
**Mods > Runtime > Check game math** action runs: every entity in every registry is compared against
the game's own answer. The answer is plain text with no envelope — it is already one fact per line,
and there is no handle to follow up on.

**The verdict is the first word of the first line**, followed by the count that accounts for
everything the run compared, then one line per check in the order the checks ran, then the
provenance line. An excerpt of an all-agree run — the middle checks are elided here, not by the
tool:

```
AGREE — 8442 facts compared, 8442 agree, 0 differ.
Category binding AGREE: 63 compared.
Category traversal AGREE: 63 compared.
Identities AGREE: 3323 compared, 0 empty, 0 repeated within a table.
Shared identities: 1934 entities, 1389 detail rows filed under one of them (largest: PurchaseViewRelations 409, AlchemyLoadout 125, SpellRecipeAuthoring 65).
Spell type layer AGREE: 6 compared.
window: generation=3 frame=48213 entities=6683 collectors=61 collect=41.213ms ported=118.4ms native=2249.1ms elapsed=2407.741ms memos=5677 drifted=730 dirty=3558 uncalculated=612 widestDrift=2.46e121%@StructureSO.passiveCostMod
```

The rules that make it read that way:

- **One verdict vocabulary, four words, everywhere.** `AGREE` — everything compared agreed, and
  everything in scope was compared. `DISAGREE` — at least one comparison found the two sides
  genuinely different. `INCOMPLETE` — everything compared agreed, but something in scope could not
  be read, so a pass over a subset is not reported as a pass. `INCONCLUSIVE` — nothing could be
  compared, so there is no verdict to have. A response never mixes vocabularies between its summary
  and its checks, and no check says `PASSED`, `FAILED`, `MISMATCH` or "all agree" any more.
- **Every check renders its own line — verdict word and counts — including the ones that agreed.**
  A check that ran and agreed and a check that never ran are otherwise the same silence, and a
  reader who cannot tell them apart cannot tell what the top-line verdict is a verdict over.
  Disagreements, where a check has them, follow its headline indented by two spaces; an agreeing
  check is its one line and nothing else.
- **A check may state its own counts, and may publish one fact it does not score.** Where agreement
  rests on more than one count, the check says which counts in its own words rather than through a
  bare total. A non-scoring line sits directly beneath the headline, unindented, and carries a fact
  about the snapshot's shape rather than a verdict — `Shared identities:` is the one such line
  today.
- **`Identities` asserts uniqueness within one table, not across the snapshot.** Two rows under one
  id in one table make a lookup return an arbitrary member of the pair, so that — and a row
  published with no identity at all — is what `DISAGREE` means here, named with the table it
  happened in. One entity reaching several tables is the design: a dozen per-owner detail tables key
  their rows by the entity they describe, so that sharing is reported on the `Shared identities:`
  line, with the tables holding the most such rows named, and scores nothing.
- **Agreement is a count, disagreement is a row.** A comparison that agreed only within
  floating-point tolerance is agreement; it is counted on the summary line
  (`N agree only within tolerance`) and never given a row, because such a row printed two
  byte-identical numbers behind two full UUIDs.
- **Numbers are the game's own Scientific notation**, both sides of a comparison in the same form,
  so a difference shows in the digits that differ. Two values that agree are written once
  (`ours=theirs=4.4e3`). Two that differ only below the three digits the screen keeps say so
  (`both read 7.46e290, differing below what the screen shows`) rather than printing the same string
  twice under a heading that claims they disagree.
- **One `window:` line, last, the same shape every call.** Everything that moves between two calls
  over an unchanged world lives there and nowhere else: which lifecycle generation and Unity frame
  the numbers were read from, how much was read, what the run cost, and how far the game's own
  modifier memos had drifted from a fresh recompute when it was taken. Memo drift is the game's
  state rather than an error in the suite — the suite reads the memo because the game acts on the
  memo — so it is a condition of the run, not a verdict about it.

The per-category entity census, the bind and cold-collect timings, the per-pass millisecond
breakdown and the per-type drift percentages are not part of the answer and are not printed: each
answers a performance or inventory question this verb is not asked, and each moved between two calls
over an unchanged world.

Two things about it are unlike every other read here, and both are deliberate:

- **It stalls the game and the call.** The whole run happens inside the frame the call is claimed
  in, because a run spread across frames leaves each pass comparing a different frame's game state.
  Seconds of stall is the honest cost; the button pays it too, and the stall is what tells a player
  at the keyboard that it ran. Call it when a number looks wrong, never on a schedule.
- **It refuses rather than doubling up.** A press already queued from the Runtime page runs later in
  the same frame, so a call that arrives while one is queued is refused with `ERR_STATE` and the
  sentence naming the queued press. Running both would measure caches the first run had just warmed.

It compares the suite against a **running** game, so the live game's lifecycle is its precondition
and it answers the same sentence the world reads answer when there is no run to read — the Runtime
button reports it in the log, the tool refuses with the lifecycle class. Its passes resolve their
types from the loaded assembly and read those types' static registries — or, where the entity is a
loadout position rather than a registered asset, the list the identity registry answers for that
loadout — all of which answer in the Start menu because the assets load with the process long before
any save does; asked there without that precondition it dereferenced a game object that does not
exist yet, and answered the runtime's own exception text as a tool error. No finding prints raw exception text either: a check that faults
before it can compare anything answers `INCONCLUSIVE`, says that nothing in it is a verdict about
the game, and names only the exception's type.

Its passes read their comparison worlds through throwaway collectors, which get their own
purchase-view topology because every collector does unless it is built by the session's named
`GameWorldCollector.ForSession(...)` opt-in. That opt-in binds the process-wide owning-view
admission resolver the purchase boundary reads from, and a diagnostic that restamped that resolver
with its own epoch would leave every later purchase refusing on a snapshot this check wrote.

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
