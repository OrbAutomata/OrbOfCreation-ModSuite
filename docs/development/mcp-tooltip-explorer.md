# Game MCP tooltip explorer

[Game MCP index](mcp-tools.md)

Read what a tooltip actually draws. [Tooltip projection](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Automata/Runtime/GameMcp/GameMcpTooltipProjector.cs) and [native access](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Automata/Runtime/GameMcp/GameMcpTooltipNativeAccess.cs) own the request-scoped panel/row contract. Examples are partial.

## Tooltip explorer

`game_screen_elements` pages current hoverable elements using hierarchy paths with sibling indices.
[Native access](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Automata/Runtime/GameMcp/GameMcpTooltipNativeAccess.cs) owns the carrier/member
contracts. Scope includes the active screen, persistent chrome and open modals; the returned path is
a volatile screen handle rather than stable entity identity.

**Nothing this game leaves behind is deactivated, so being alive answers neither scope question.**
Closing a modal only drops its canvas group's alpha and raycasts, and leaving a screen only marks
its `ManagedView` invisible — every panel and every screen the session ever opened stays active in
the hierarchy forever. The catalog therefore walks each element's ancestry once and asks the game
both questions: `UIModal.IsOpen()` for the panel, and `UIRenderGroup.IsActive()` for the screen.
The second is the predicate every `UIRenderGroupElement` consults through `IsUIActive()` before it
draws — not disabled, its `ManagedView` visible, its parent group active — and it answers for its
whole subtree. A live object under an inactive screen does not belong in the current catalog. A closed tooltip is the third
leftover: the game pools its `UITooltipContainer`s, and closing one clears its item and hides it but
keeps the `TooltipNode` rows it last drew, so the walk also asks the container's own `IsEmpty()`
and lists a tooltip's rows only while it is drawn.
A screen's elements hang off a handful of panels, so the catalog is a list of panels.

**A control that opens a panel says which panel.** The top-right chrome is a row of
`UIModalActivator` buttons that draw no tooltip of their own, so they carry `name`, `path` and
`opens: modal` instead of an `id`: they are not things in the world, they are ways to see some. The
name is the panel's own authored title, which is the argument `game_modal(mode="open")` takes and
the title a dismissal reports, and the rows are the same two panels `game_modal` offers, from the
same enumeration. They are elements and not screens, so `game_screen_catalog` does not list them
and `game_navigate` does not reach them; a `game_tooltip` by one of their paths answers with the
panel it opens and how to put it up.

**Every path printed here is the shortest tail of that element's path no other live element answers
to.** It starts at the element's own last segment and lengthens one segment at a time, only where
uniqueness requires it, so the address names exactly one element and is a valid `path` argument
exactly as printed — nothing to prepend, and never longer than the whole chain. What went with the
Canvas-rooted chain is `pathRoot` and `pathPrefix`: they existed so a row plus its page's ancestry
would resolve, and a tail that resolves on its own needs neither. Two paths a screen genuinely draws
identically are the one case with no shorter answer, and each states its whole chain rather than a
shorter one that would be a guess.

When every element of a panel is the same component followed by a bracketed index, the panel states
it once as `pathComponent` and each element's `path` is only its own `[index]` — joined onto the
component with no separator, because a component and its index are one segment. The brackets stay on
the row: a bare number in a `path` column beside a `slot` column is the confusion the slot column
exists to end. The fold is per panel and it pays for itself — a panel mixing components, a segment
with no index, and a component too short to earn its own line all keep every row's full address.

Such a panel is printed as the element itself.

`game_tooltip` resolves a row by the tail it was handed: any tail of a live path, matched at a
segment boundary, up to and including the whole path.
A tail naming more than one live element is refused rather than resolved to the first, and the
refusal says to re-read the catalog, whose addresses name one element each — a tail that collides
is one the caller shortened or one the screen has moved on from. A row under a `pathComponent` is
addressed by the component and its index joined, never by the bare index.
A tail naming none says to re-read the catalog instead, because the screen has moved on.
The reply is compact plain screen text.

**`game_tooltip` takes `path` or `uuid`, exactly one per call.** A path names a place on the screen
and a uuid names a thing, so sending both is two questions in one call and is refused at
validation, and so is sending neither. Roughly nine in ten
hoverable elements are bound to an entity, so a caller holding an id from a search, a list, or an
action response reads the screen's words about it with no catalog detour. The remaining tenth is
chrome with no entity at all, including the suite's own controls, and `path` is the only address
those will ever have. A uuid resolves through the same per-element entity binding the panel rows
publish as `id`. One element showing it answers with what that element draws; none showing it
answers with the entity's own tooltip, read from the game exactly as hovering a link line would put
it up, so a `→ … game_tooltip uuid=…` line is a call that answers:

- **Several elements show it** — `ambiguous_element` (`ERR_INPUT`), which lists their addresses
  under `paths` rather than picking one. This is the common case rather than the corner: the Magic
  screen draws every equipped spell twice, once in its own list and once in the casting bar, and the
  two are separate objects with separately read sub-tooltips that may print different text.
- **Nothing on this screen shows it** — the entity's own panel in its resting state (no container
  is up, so there is no more-info key to hold), without the sub-panel row an element adds beside it.
  Reading it changes nothing in the game: an installed-game contract walks the whole call graph
  below every tooltip builder the reader calls and pins that none reaches a sound, a popup, an
  event-log entry, a modal, a spawned or destroyed object, or a modifier write. A uuid read with no
  save loaded is `entity_catalog_unavailable`.
- **Nothing on this screen shows it, and the game hides it** — `not_on_screen` (`ERR_NOT_FOUND`):
  "No element on this screen shows it; world_get reads its facts." Hidden is world_get's own
  `visible` predicate answering no — an undiscovered recipe, rune or ritual, invisible research.
  Reading such an entity's tooltip printed words the player has never been shown as if a screen
  drew them; world_get still answers for it, with `visible` saying no.
- **An id nothing in this build carries** — the existing `unknown_uuid`.
- **An element or entity with no tooltip** — the existing `tooltip_content_unavailable`.
- **A tooltip the suite will not finish reading** — `tooltip_depth_exceeded` (`ERR_UNAVAILABLE`).
  The walk is the container's own shape, so what reaches this is an authored `children` tree deeper
  or wider than the game could lay out either: sixty-four levels, or two thousand nodes in one
  panel. It is a guard on data the suite does not own rather than a budget a caller meets — the
  game's own answer to how deep tooltips go is `UITooltipContainer.MaxTooltipDepth`, five stacked
  panels. Nothing of the text comes back with the refusal on purpose: a walk that gives up inside a
  repeating subgraph has collected a name and a description that read exactly like a short,
  complete tooltip. The screen still shows the real one, and there is no parameter to retry under —
  the panel is the same on the next call. This is not the 200-line notice, which is the opposite
  case: there the walk finished, the body is the tooltip's own first two hundred lines, and its last
  line says `Tooltip truncated after 200 lines.`

A uuid read takes the world, for the catalog and the hidden check when no element shows the id; a
path read reads no world at all, because what the player can hover is a screen fact rather than a save one.

**Every panel says `[id | name | path]`, and a casting-bar row says its slot.** A row's `id` is the
entity the element is about. Most panels are assigned the asset itself, which carries its own id;
three are assigned a live instance instead — the casting bar a `Spell`, the passive bar a
`PassiveAbility`, a plot's action diamond a `PlotNodeActionInstance` — and none is an
`IdScriptableObject`. Each is one audited accessor from the asset it was built from, the same asset
whose name the element already prints, so those rows carry the id every sibling panel carries, and a
diamond's `Drain -X/s` hover answers to its action's uuid. A panel that
builds its tooltip out of several facts — the glyph list, which folds in the equipped quantity —
hands the screen an `OverwriteTooltip` wrapped around the entity rather than the entity itself; the
wrapper forwards the name, and the `id` is read off the thing inside it however many decorators the
panel stacked. A row about
nothing but a control carries no `id`, and a live instance whose reference is gone carries none
either: it keeps its name and path rather than an id nothing answers to.

`slot` is the loadout position every cast verb takes, and it is a **join, never the path**. The
bracket index in a path is a Unity sibling ordinal. What answers instead is the published loadout: the recipe the
row is about, matched against the recipe each occupied slot holds. A row carries `slot` exactly
when one occupied slot holds that recipe. A spell no slot holds — the spellbook, a recipe page — is
silent, and so is a recipe **two** slots hold, because then every button showing it joins to both
and naming either would address the position the reader was not looking at. Passives have no slot
at all: the loadout has positions for spells and none for them, so their panel carries the three
columns and no fourth. With no world published there is no join, and the catalog still lists what
the player can hover, because that is a screen fact rather than a save one.

**The answer is the container the screen drew, and nothing behind it.** One hovered element puts up
one `UITooltipContainer`, and that container draws three things: the core panel (`UITooltip.Render`
on its `item`), one row of sibling sub-panels from `subTooltips`, which carry no sub-panels of their
own, and inside each panel one node list walked through `TooltipNode.children`. The reader renders
exactly that on Unity's main thread — each panel's name, display type, description, then the cost,
usage cost, mastery line and requirement list `Render` paints from the item's own interfaces, then
the nodes. Each of those four is drawn only where the panel prefab
(`GlobalVariables.GetTooltipPrefab()`: its `parentTooltip` for the core panel, `childTooltipPrefab`
for the row beside it) carries the element, and the mastery line only once its view is unlocked or
the item forces it. A cost row is the number the badge prints and the resource its icon shows —
`25 Mana`, `12 Spell Capacity` — without the held amount the badge adds in brackets. So a spell on
Loadout reads its cast cost, its weight, its mastery and lines such as `Not enough Spell Capacity`
and `Req. Expert Storm lv 17/21`, not only its cooldown. A panel draws one node list and not two: `UITooltip.Render` sets
`renderedAlt = IsUsingAltTooltip()` and then renders `GetAltTooltipNodes()` *instead of*
`GetTooltipNodes()`, so the alt list is a toggle rather than an appendix and the read follows the
state the screen is in — the entity having an alt list, and the player holding the more-info key.

**A link is a pointer, never a panel.** `TooltipNode.tooltipable` and `TooltipNode.subTooltips` are
not text: `UITooltipNode.Setup` hands them to `HoverTooltip.Setup` as the core panel and sub-panel
row of the player's *next* hover. Each link is published as the one line the screen offers: `→
<name>: game_tooltip uuid=<id>`. A link about no entity carries its name alone, because the screen
is not drawing it and minting an address for it would be a guess; one definition linked from many
rows is pointed at once, because a line the body already carries says nothing by being carried
again. The one panel drawn inline is a glossary word a row filled with its own numbers — an
`OverwriteTooltip` around an `AttributeSO` carrying its own node list, such as the Rating Change
preview beside a rune's chance statistic (`Rating 2.04 -> 4.59`, `(x2) unlocked at 36.2 rating`).
That text exists only on the row, so a pointer to the word would read its generic definition
instead; it is drawn one hover deep, and links inside it stay pointers.

Node structure, repeated paint and empty arrays are wire-internal ceremony and never ship, and a
body whose closing block repeats the block immediately above it says it once: adjacent duplicate
lines are dropped one at a time, which never catches a panel that paints its whole last block
twice. The smallest repeated suffix is removed so a larger match cannot consume unrelated panel content.
Unity rich-text markup is stripped. The list of tags is closed so that prose holding an angle
bracket survives, which means it has to hold every word the pinned build actually authors: the [rich-text grammar](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Automata/Runtime/GameMcp/GameMcpTooltipProjector.cs) owns accepted markup.

```sh
tools/game-mcp-client.py tooltips --limit 25
tools/game-mcp-client.py tooltip 'PATH/FROM/CATALOG/ROW'
```

The audited manifest covers the native tooltip carrier/open/nesting shape and the two recipe
accessors the casting-bar and passive rows take their id from, while the real-reference
build and installed contracts verify the source node graph that the prose renderer consumes. The
same audited `ITooltipable.GetDescription()` contract supplies authored descriptions for
`world_get` when the resolved entity implements that interface.
