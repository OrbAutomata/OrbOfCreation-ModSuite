# Game MCP world reference

<a id="what-search-searches-and-what-it-cannot"></a><a id="what-a-thing-does-as-a-search-term"></a><a id="which-screen-shows-an-upgrade"></a><a id="screen-words-two-vocabularies"></a><a id="which-page-shows-a-glyph-and-which-pools-a-book-widens"></a><a id="a-list-row-carries-durable-facts-only"></a><a id="the-three-state-lifecycle"></a><a id="the-statistic-glossary"></a><a id="the-ritual-layers-glossaries"></a><a id="the-stat-groups-and-what-they-distribute"></a><a id="what-a-thing-does-and-what-a-level-of-it-buys"></a><a id="a-glyphs-effects"></a><a id="every-owners-leveleffects"></a><a id="what-moves-a-thing-modifiedby"></a><a id="what-a-keyword-is-worth"></a>

[Game MCP index](mcp-tools.md)

Read one immutable published world per answer. The [world tool descriptors](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Automata/Runtime/GameMcp/GameMcpWorldTools.cs)
own accepted arguments; [capability descriptors](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Automata/Runtime/GameMcp/GameMcpEntityCapabilityMap.cs) own category
names, native types, scan columns and capability projections. Examples below are partial. Discover the
current inventory with `world_categories` rather than relying on a copied category schema.

## Choose a read

Start with `world_overview` for progression, resource-panel and running-state context. Use
`world_search` to find a stable entity, `world_list` to compare one category, and `world_get` for an
entity's full detail. `game_tooltip` answers a separate question: what a live tooltip draws. Its
[explorer](mcp-tooltip-explorer.md) does not replace a published-world query.

```sh
tools/game-mcp-client.py call world_overview
tools/game-mcp-client.py call world_categories
tools/game-mcp-client.py call world_search --arguments '{"query":"Knowledge"}'
tools/game-mcp-client.py call world_list --arguments '{"category":"attributes","affordable":true}'
```

The overview's resource panel follows the game's panel order and visibility. A resource line shows
held amount, capacity when capped, net rate and the fill/empty clock the tooltip offers. A missing
panel publication is unavailable data, not an empty panel. The run and reset clocks refer to game
time; neither is a measurement of the client's session.

Running queues are individually named and measured against their own capacities. World occupancy is
a published fact; live room at the instant of a press is a native-boundary fact. Use the action queue
probe in the [screen reference](mcp-screen-reference.md) when that distinction matters. The overview
also identifies a running ritual battle and the gates it holds shut, and marks required discovery
rolls only on accessible screens that actually owe one.

## Categories and collection health

`world_categories` includes collection reports as well as listable entity categories. A collector
with no list page remains visible and says where its facts can be read, or that it has no page.
An unlistable collector is different from an unknown category. An unavailable or partially read
category carries its reason; an empty clean category means there are no rows in that publication.

The complete category-to-reader and type mapping belongs to the
[descriptor registry](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Automata/Runtime/GameMcp/GameMcpEntityCapabilityMap.cs), and collection order/lifecycle scope to
[the collector](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/Runtime/World/GameWorldCollector.cs). Neither mapping is repeated here.

## Paging and filters

`world_list` requires `category`. `offset` resumes a page; `limit` is an upper bound. Follow
`nextOffset` exactly when present; its absence marks the end. A short page can be a response-budget
boundary rather than the end. Small result sets can be returned whole when no limit was requested.
The [list owner](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Automata/Runtime/GameMcp/GameMcpWorldList.cs) owns page-size and byte-budget constants.

`affordable` and `discovered` narrow only categories carrying the corresponding fact. An unsupported
filter refuses explicitly. Availability is not a substitute for affordability or discovery.

`world_search` accepts a query or a supported filter; naming neither refuses. The query matches
case-insensitive substrings of published names, internal names, IDs, keyword words, categories and
native types. It does not search entity descriptions. `matchedOn` explains a hit whose visible name
does not contain the query. Results prioritize relevant names/keywords and deduplicate stable entity
identity before paging. [Search](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Automata/Runtime/GameMcp/GameMcpWorldSearch.cs) owns matching and ordering.

Representative filter-only calls:

```sh
tools/game-mcp-client.py call world_search --arguments '{"category":"research","state":"available"}'
tools/game-mcp-client.py call world_search --arguments '{"category":"challenges","run":"active"}'
tools/game-mcp-client.py call world_search --arguments '{"category":"spell-recipes","discovered":false}'
```

Lifecycle `state` and challenge `run` describe different facts. A category without a lifecycle model
matches no lifecycle filter; it does not acquire an invented state. `keyword` accepts a word shown
on the entity's type line or its published identity. `modifies` accepts the target identity and walks
published effect sources; the result adds the effect that reaches it. The tool descriptor owns the
complete accepted filter combinations and spelling.

## The world is a graph

A row's references lead to detail reads, and details lead to related published rows. Follow those
references rather than guessing a native type from a label. Use `modifiedBy` to see source counts
and `world_search` with `modifies` to inspect the sources. Effects stay on their owners; reverse
walks are derived from the pinned publication.

Keywords are entity references to the game's type assets. A keyword's detail explains the modifier
records and sources it carries, and what it reaches. The
[one-bonus rule](../runtime-architecture/game-boundary-doctrine.md#a-type-total-and-a-member-value-are-one-bonus-and-the-names-say-so)
defines the distinction between distributed totals and member values. A total already inside a
member value must not be multiplied into it again.

## Entity handles

Use the short ID the response prints or the full UUID. A short handle is a build-bound spelling of
stable identity, not a name or a hierarchy path. Ambiguous or invalid handles refuse. The
[handle codec](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Automata/Runtime/GameMcp/GameMcpEntityHandle.cs) owns spelling and resolution.

`world_get` accepts either `uuid` or `uuids`, never both. A batch answers in request order and one
unreadable entity does not discard the other blocks. Optional `category` selects a particular table
when needed; an ordinary entity ID resolves its own category. The descriptor and list owner define
batch limits; detail is not silently truncated to fit a list-page budget.

```sh
tools/game-mcp-client.py call world_get --arguments '{"uuid":"ENTITY_ID"}'
tools/game-mcp-client.py call world_get --arguments '{"uuids":["FIRST_ID","SECOND_ID"]}'
```

The uppercase IDs in these examples are placeholders for IDs returned by a read.

## The detail read

A detail block identifies the entity and its category, retains that category's durable row columns,
and adds evaluated decisions where the capability supports them. A description is a request-scoped
read for the named entity, not a periodically collected description cache. A fact-only entity remains
a useful detail read without fabricated predicates.

Read the decision relevant to the intended action. Visibility, discovery, availability, level caps,
usage requirements and affordability are separate questions. For example, a queued level is work
accepted by the game, not a completed level; built and queued work retain distinct meanings.
The [detail reader](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Automata/Runtime/GameMcp/GameMcpWorldDetail.cs) and capability projections own those fields.

Requirement explanations retain authored AND/OR structure and order. Finite acyclic groups can nest
at arbitrary depth and use the shared iterative fold. Prerequisite-link reference expansion has its
own cycle/bound guard; it is not an authored-group depth limit. A tier explains the conditions needed
for that tier rather than dumping unrelated rungs.

A missing required container, condition list, group child list or static membership is Unknown,
never an empty program reported Met. Genuine empty groups retain their native truth. An undeclared
program is legitimately absent. The [collection rule](../runtime-architecture/world-collection.md#layout)
and [shared evaluator](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Common/Runtime/World/WorldRequirementEvaluator.cs) own this behavior.
Unexpected native reader exceptions fault the whole world pass; explanation does not catch them and
invent a usable partial world.

Exact prices use the same-generation holding and cost inputs. Unreadable price is not zero price.
Resource meters distinguish held stock from an allowance left to commit; read the meter word before
interpreting amount/capacity. A capacity budget is not a stock that fills. Player mechanics are in
[allocations](../game-systems/allocations.md) and
[numbers and rounding](../game-systems/numbers-and-rounding.md).

## Partial collection

An unmodeled requirement leaf carries its owning identity and capture evidence. When that evidence
fully reconciles the collection shortfall, queries keep unrelated owners authoritative. The affected
owner's detail is explicitly unavailable and its list/search row carries an incomplete explanation.
Unreconciled collection gaps remain category-global. Derived data requires its upstream evidence.
The [requirement projection](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Automata/Runtime/GameMcp/GameMcpRequirementProjection.cs) and
[collection failure policy](../runtime-architecture/world-collection.md#reading-rules) own the exact
scope; the wire does not turn missing data into success.

## Game facts and tool projections

For screen labels and progression, see [Screens](../game-systems/screens.md). Recipe Book pool
unlocks belong to [pool unlockers](../game-systems/pool-unlockers.md); socketable augments belong to
their spell system. [Effect grammar](../game-systems/effect-grammar.md),
[Alchemy](../game-systems/alchemy.md), and [Rituals](../game-systems/rituals.md) own those mechanics.
The category descriptors and projections own how those facts appear in list/detail/search.
No asset census, member roster or native-type table is maintained in this reference.
