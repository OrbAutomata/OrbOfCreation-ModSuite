# Game MCP gameplay reference

[Game MCP index](mcp-tools.md)

Use the same canonical GameActions as automation. [Gameplay descriptors](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Automata/Runtime/GameMcp/GameMcpGameplayTools.cs) own modes, arguments and routes. Examples below illustrate caller workflows; they are not an exhaustive schema. Every press revalidates current native facts.

Fact decisions on `world_get`, `world_list` and `world_search` use verdict words and explanatory
sentences. They do not carry producer refusal codes or `ERR_*` classes. A status-bearing unavailable
read envelope, an action result or a preview result can carry a wire class. The
[wire normalizer](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Automata/Runtime/GameMcp/GameMcpEntityWireNormalizer.cs)
owns that distinction; [response format](mcp-response-format.md) and
[refusal vocabulary](mcp-refusal-vocabulary.md) explain the caller-facing terms.

## Discovery decision loop

`discovery-trees` is the pre-decision surface for `game_discover`'s `offer_*` modes; attempting an
action is never the way to learn its cost or choices. Every row names the tree UUID/type, semantic
mode, rerolls left, discovered count, and whether discoveries remain. A crafting tree also says how long it
has been `craftingFor`, as a duration through the same clock every countdown on this surface uses
(`2.72s`) rather than as a bare number of seconds that a caller has to guess the unit of. The key
is the screen's phase, not the field's: `DiscoveryTreeSO.actionTime` is the native name, and a
reader met it beside a `mode: crafting` that meant the same thing. Authoring/debug members and
duplicate identity (`treeId`, overrides, debug mode, and bonus-level cost) are intentionally absent.
The Discovery Tree is a transient in-game event rather than a standing page, which is why its
lifecycle lives inside the one discovery tool instead of a permanent tool of its own.

In Idle mode, `initiate` reports `available`, a reason sentence when held, and — only
where the price is the thing left to decide — each exact cost line as a named `resource` plus
`cost`, canonical `spendableAmount`, and `affordable`. A tree the game is not showing, or one with
nothing left in reach, carries the reason and no price: quoting one for a press that cannot happen
sent a caller to earn a number it would still not be allowed to spend. In
Choice mode, `offers` contains named handle/category references in native order, each carrying
`discovered: true` where the run already owns the thing offered. A tree owing a required component
offers that one thing whether or not it is owned, and the game draws the same Confirm either way,
so ownership is a fact about the offer rather than a reason to refuse the press.
`selectedOffer` — the named reference every `…Uuid` becomes on the wire — appears only after
selection. The `reroll` decision block appears only in Choice mode, and carries a `note` while a
required component is outstanding: the game's Reroll button is drawn on the reroll budget alone, and
a reroll here spends a reroll to be offered the same one thing again. An empty offer set omits
`offers`.

**A Crafting tree says its roll is still running.** `DiscoveryTreeSO.CraftTime` is three seconds of
game time, the offer list is empty for every one of them, and an empty list reads exactly like a
press that did nothing. The row therefore carries `rolling` beside `craftingFor`, saying that the
roll is running, that offers appear three seconds after the press, and how long this tree has been
rolling.

A spent `offer_reroll` answers in the same shape `time_challenge reroll` does: `rerollsLeft` as a
`{before, after}` pair, an explicit `changed` saying whether the offers actually moved, and the
settled `offers` inline. A bare post-value said nothing about what the press bought, so the caller
re-read the whole tree to find out.

These values are copied during the shared 250-millisecond world capture from lifecycle-bound
delegates for native visibility, immediate-required state, current choices, exact next cost,
affordability, and resource true quantity. The MCP worker only projects the immutable row. A choice
UUID must resolve in the same generation as an alchemy recipe, equipment, glyph, ritual, spell
recipe, or time rune. If it does not, the tree's `world_get` returns
an unavailable read envelope with `implicatedOffers` and the tree's row on a page carries the
`incomplete` sentence instead; the UUID is never silently omitted.
Current offers are also resolvable through `world_get`, including authored
metadata and applicable discovery predicates.

## Discovery decisions

A discoverable entity's fact row carries one `discover` decision, and it never offers a press: the shipped game's only
`IDiscoverable.Discover()` callers are a Discovery Tree's roll (`DiscoveryTreeSO.DiscoverItem`) and
the developer console. An undiscovered row's `discover` decision says the item comes only from a
roll and names that roll and where the item stands in its pool, which is the game's
`!IsDiscovered() && IsDiscoverVisible()`: *Only the roll on Magic > Spellbook > Unlock gives this,
and it is not in that roll's pool until you own the Expansion recipe book.* A `spell-recipes` row
names the book because it publishes its `belongsTo.recipeBooks` edges with `owned`. Where a row has
no published book edge, it says *it is not in that roll's pool yet*. A row a tree is offering
right now says so and points at `offer_select` on that tree. The decision carries no price; the
roll's price is the tree's `initiate` cost.

The screen a row names is the one its tree's authored `viewLocation` draws under.
`AlchemyRecipeSO` is drawn by two trees — ordinary alchemy on Alchemy > Alchemy > Learn, Scholar
concepts on Scholar > Concepts > Discover — and the row tells them apart by the recipe's own
`AlchemyTypeSO`, exactly as the game files them. A recipe whose type is in neither set names a
discovery tree's roll without a screen rather than one that would be wrong half the time.

**A row the tree is already holding for you prints the tree.** While a Discovery Tree carries an
entity as its selected offer, that entity's own `discover` block explains that the tree holds the
offer, names the tree under `offeredBy`, and carries no `costs`: confirming on the tree
charges nothing, because `DiscoveryTreeSO.DiscoverSelectedItem` only discovers.

`game_discover` drives a Discovery Tree's roll and nothing else. The `offer_initiate`,
`offer_select`, `offer_confirm`, and `offer_reroll` modes address a Discovery Tree by `uuid` and take
the chosen offer as `offerUuid`, because the transient offer UI really does show and select those
exact entities. A confirmed spell offer also reports whether the game's own `PostDiscoverRecipe`
loaded it, into which slot, and which spell instance that slot now holds. A recipe can sit on the
bar more than once, so the copy this press minted is the occupant the world before the press did not
carry — not the first slot holding the recipe, which is the older, levelled one a caller has been
casting. When the bar already held the recipe and no new copy joined it, the block says that
instead.

The `equipment` category is also the artifact-loadout pre-decision surface. Each row names the
artifact and its primary equipment type, current/maximum stacks, global and type-slot occupancy,
usage-cost resources with current holdings, and the next equip/unequip admission or refusal. Call
`game_equipment` with `mode:"equip"` or `mode:"unequip"` and an explicit positive `amount`. A
committed call returns the stack count before and after. Usage reservations, effects, and
attunement are post-state evidence, never payment-verification gates.

## Ordinary Alchemy loadout loop

An `alchemy-recipes` detail row carries `alchemyLoadout` only for the six ordinary Alchemy families.
It reports `activeCount`, the slot the recipe occupies when active, and the next visible add and
remove decisions. An available add includes the live click-sized maximum and named per-use resource
costs with current spendable holdings; an unavailable add carries only its binding reason. Concept
recipes remain on `game_concept`, Alchemy discovery is the Learn tree's roll on `game_discover`, and recipe
leveling belongs to the unified level surface rather than this list lifecycle.
A recipe whose usage requirement is unmet while its core type authors a requirement penalty carries
`usageRequirement`, the first unmet condition in its tooltip's words and the speed and drain-cost
penalties in the modifier spelling: `Expert Alchemy at level 7 (at 3) not met: speed x0.250, drain
cost x16 until it is`.

The screen's Alchemy Lv adjuster is `game_alchemy(mode="set_alchemy_level", level=N)`; see the
*Level dial loop*.

`game_alchemy(mode="add"|"remove", uuid=..., amount=...)` applies the caller's explicit positive
amount through the list's native counted mutation after revalidating live usage capacity. Success
returns only the settled `activeCount` before and after. The action boundary revalidates exact recipe
identity, ordinary-family classification, discovery, and capacity before invoking the explicit-count
core used by the UI wrappers.

Loadout order carries no gameplay effect — nothing the game computes reads the position a recipe
sits in — so there is no verb that reorders it, no `destination` argument on this surface, and no
`move` decision on the read. The slot a row prints is the address the player sees on the screen.

## Concept slots

The `world_get` `concept` block is the pre-decision surface for `game_concept`. It carries
`assignedCount`, the slot budget as `usedSlots` and `maximumSlots`, and `canAdd` as a decision rather
than a bare boolean: a held decision explains whether every slot is taken or the game simply will
not take this recipe with room left, which is the difference between freeing a slot and
picking another recipe. The sentence says which of the two situations it is and leaves the counting
to the pair beside it, so the budget is stated once. The budget is published because assignments are
only the filled slots — counting `alchemy-instances` rows can never reveal the capacity behind them.

`world_list category=concepts` is the Scholar screen's Concept list, and it carries the two columns
that screen draws: `state` and `masteryLevel`. The slot budget and the assignment decision are not
columns, because both are facts about the one Active Concepts list rather than about a row, and a
page would say them 46 times over. `world_get` on a Concept answers `category: concepts`, which is
the word `world_list` takes for it. There is no `predicates.canAdd` beside the `concept` block: that
block is where the decision is published, its presence is the answer to "is this assignable at all",
and one decision is published once.

## Ritual lifecycle

Ritual discovery is the Devote tree's roll on `game_discover`. Once discovered, a `rituals` detail
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
to read. For the same reason `activate.available` does not wait on the selection: the verb presses
the selection toggle itself. `setLevel`, `activate`, and
`cancelDuration` each carry only the binding availability or refusal reason that affects the next
decision. `setLevel` has one presence rule for its bounds: every ritual whose starting level is the
caller's to choose publishes `minimum` and `maximum`, selected or not, because the ceiling is a fact
of the ritual and the player rather than of the selection — and it is available on an unselected
ritual too, because the verb performs the selection. Only a `level_locked` ritual — one the
game runs at an authored level — publishes no bounds, because there is no range to choose from. Its
`selectedLevel`, `setLevel.current`, completion price and refusal all name that authored level,
because `RitualSO.GetCurrentLevel()` answers it and the run's enemies, waves and price read
`GetCurrentLevel()`. The Ritual panel draws the same number on a disabled selector; the battle
header alone prints the stored selection (Restoration: header 55, run at 70).

`game_ritual(mode="select"|"deselect"|"activate"|"end"|"cancel_duration", uuid=...)` reproduces the
corresponding visible Ritual control. `mode="set_level"` also requires the `level` the Ritual
screen's starting-level selector shows, which runs from 1 to the row's `setLevel.maximum`:
`UIRitual` clamps that selector to `1..RitualSO.GetMaxSelectedLevel()`, so 1 is a starting level and
0 is not, even though the setter behind the control would accept it. A level outside that range is
refused with `level_out_of_range` carrying `minimumAmount` and `maximumAmount` — the same two
numbers its sentence names. `set_level` and `activate` both act on the selected Ritual, and the
game's selection variable holds whichever ritual its toggle was last pressed with — so both verbs
press that toggle themselves when the named ritual is not the held one, whatever was selected
before, and verify the selection landed before setting the level or paying. A caller is never
refused for a step it could not see. The press overwrites the previous selection and the game keeps
no undo for it, so a press that does not select the named ritual is a `verification_failed` fault
with the attempt counted, and nothing is paid after it. Runestone-selection manager methods outside the declared action path
are empty/null-returning in v1.0.5 and are deliberately absent. Activation revalidates the selected
Ritual and the screen's native price before payment; success is the settled battle transition.
`cancel_duration` ends an already-running duration reward and does not claim to cancel a battle.
`activate` and `end` are the two battle-boundary modes, so both report `activeBattle` and
`wavesCompleted` as observed `{before, after}` changes. **`activate` also says what the battle it
just started holds shut**, as `gates`: while a ritual battle runs no ritual can be activated and no
ritual's starting level can be set, and no other decision on this surface is gated on it. `end`
additionally reports the level the battle reached, the duration rewards it left running, and the two
facts the game's own results modal shows: `result` as `succeeded` or `failed`, read from
`RitualSO.IsFailedRun()`, and the `spoils` the run banked as named resource rows, empty array
included. Both are settled reads and not pre-mutation copies: `RitualSO.End()` writes neither
`wavesCompleted` nor `currentSpoils`, and the next `Initiate()` is what clears them, so the run's
record outlives the transition that ends it — which is why the row can still report it as `lastRun`
long afterwards. An `activate` reports no verdict: it is the mode that clears the record, and
`wavesCompleted < 5` is true of a run that is just starting. The two modes therefore read their
shared `wavesCompleted` pair in opposite directions — `end` reports the waves the finished run
reached, while `activate` reports the previous run's total falling to `0`, the reset the new battle
starts from. Both are the same settled `{before, after}` observation of the same field; which one a
response is saying is the mode it answers, never the shape of the pair. Selection, level, battle,
and duration activity each use one game-written outcome sentinel and never a resource ledger.

## Unified level controls

The `equipment-types`, `augment-glyphs`, `resource-types`, and `time-runes` detail rows are the
complete pre-decision surface for their ordinary level-list buttons. Each row distinguishes paid,
bonus, and total levels and carries a `purchase` decision. Equipment types, augment glyphs, and
resource types also carry `bonus`; time runes do not implement that native control. A time rune
also carries its mastery bar: `masteryLevel`, and `masteryXp` of `masteryXpNeeded` in the screen's
figures, so `Mastery Lv 3 · 1.96/10` reads as `3`, `1.96` and `10`. An augment glyph
carries neither while Magic > Augments > Upgrade is locked, because until the Upgrade Glyphs upgrade
is bought the game draws no level button at all — `GlyphSO.CanLevel()` being the constant `true` is a
fact about the interface, not about a button. **A glyph's levels go with the button.** While that
screen is locked no glyph publishes `paidLevel`, `bonusLevel` or `totalLevel`, on the row or in the
detail block, and the `augment-glyphs` page loses the three columns rather than printing a column of
zeroes: every one of the twenty-two glyphs is on that one screen, so the columns are absent together
or present together. A zero under a level nothing can buy reads as a level standing at zero, which
is a different claim from the game drawing no level at all. `recipe-books` never carry a level: a `RecipeBookSO` has
one instance field and the game draws it as owned or not. Available decisions include
the exact named native usage cost and current spendable amount as `costs`; a control the game
levels for nothing says `free: true` — whether the game names no price at all (`costs: []`, kept
rather than dropped) or names one whose every line is zero, which is the same fact to a caller and
gets the same word. That `free` is about the *next* level, which is the level a row is about; a
press answers about the levels it bought. Inapplicable or unavailable controls do not publish priced
ledgers.

Call `game_level_up(mode="purchase"|"bonus", uuid=..., amount=...)`. The tool derives the exact native type from
the published category, repeats the visible button's live admission on Unity's main thread, and
returns only the settled paid- or bonus-level change plus the resulting total. An augment glyph target
also returns `slots {before, after}` and `freeSlots {before, after}`, because those are the two
numbers the level panel draws — levels buy slots through the mastery requirement, and a response that
named only levels left the screen's own counts out. A Recipe Book uuid is refused in player words:
*Insight is a Recipe Book — there is nothing to level.* When the game's headroom delivered fewer
levels than `amount` asked for, the answer adds
`requestedAmount` and `deliveredAmount`; a fully satisfied ask says neither, because an
under-delivery that read exactly like a satisfied `amount=1` let a caller batching its own
progression accumulate drift with no signal.

**A press says what the whole ask was charged.** `charged` pairs each named resource with the
`cost` that press paid across every level it bought, and `chargedLevels` says how many levels that
covers; `free: true` rides instead, in the same word the row uses, exactly when every level in the
ask asked for nothing. `free` cannot be read off the standing before the press, because the game
prices every rung on its own: a ×5 rune buy off a ladder whose first rung is free answered
`free: true` while Time Advancements fell 94 → 84 → 74 → 64. A settled charge names the `cost` and
the resource and nothing else — `spendableAmount` and `affordable` answer whether you *can* pay, and
this press already did — and the curve is still read from `world_get`, where every level's price
lives. The paid route checks the game's persistent usage cost but
does not perform a one-time payment; the concrete native level callback applies its own
usage/effects. Research development and spell mastery stay on `game_research` and
`game_spell_mastery`, respectively. A landed single mastery press answers its `mastery` pair and
`charged` in the same shape, with no `chargedLevels` because a press buys one level.

**A charge is the price the press paid, never a balance difference.** Every `cost` under `charged`
is the price the game named on the frame that paid it, converted once into the figure the screen
draws (nominal cost over the resource's quality). Two balance readings around a press move with
everything else in the window: a mastery level bought during a Spark channel read
`before=101 after=125` for a 9 Spark price, and one bought beside a purchase read 499 Spark gone
for a 100 Spark price. What a resource holds now is a `world_get` read. A `game_spell_mastery all`
press is the game's own Level All sweep, which names no price to the suite, so it answers
`changedSpells` and no charge. The game draws the Level All button only once Unlock Level All
Spells is bought, so a press before that refuses `ERR_LOCKED`, naming the
upgrade and its price: "Level All Spells is locked until Unlock Level All Spells is bought (5 Skill)."

## Agromancy

Every `agromancy-elements` detail row joins the exact active-element count, the next visible
add/remove decision, its stored output and rate, and the element's offered harvest actions. An available element add includes
its named standing usage costs and current spendable amounts. Each offered action reports its
active/maximum count, its `add.available` and `remove.available` booleans, and the named resource
drain for the **next** instance. An unavailable control carries only the reason that binds the next
decision; no priced ledger is computed for an action the screen cannot run. One plot-action
prerequisite is not readable at all — the game latches it only when the action is attempted — so
that `add` is an unavailable read rather than a false one: it carries `status: "unavailable"`,
the wire class `reasonCode: "ERR_UNAVAILABLE"`, and `checkWith`, and never claims `available: false`.

`agromancy-actions` is the six base verbs themselves — Plant, Woodcutting, Mining, Create,
Transmorgify, Expand — read-only, because nothing is bought or levelled through a verb: an element
offers it, and `game_agromancy` acts on the element/action pair. A row is every number the class
stores, which is the three records `HarvestActionSO.GetScalingInfo()` loads: `power`, `speed` and
`costMod`, the last of which is the drain modifier as well. Those three are exactly what a bonus on
an agromancy action type distributes into, so this page is where a reader sees which of the six a
type bonus landed on, and the `keywords` cell says which types each verb wears.

Call `game_agromancy(mode="add_element"|"remove_element", uuid=..., amount=...)` for one exact
`HarvestElementSO`. The `add_element_action` and `remove_element_action` modes additionally require
`actionUuid` naming an action actually
offered by that element. Every element and action mode requires an explicit positive `amount`; no
mode depends on hidden selector state. The screen's Druidry Lv adjuster is
`game_agromancy(mode="set_druidry_level", level=N)`; see the *Level dial loop*.

An element action reports the changed active count beneath that named action; an element change
reports the element's count. The one sentinel is the corresponding game-written count moving in the
requested direction. Resource reservations and drain calculations are planning inputs, not a
postcondition ledger.

The `plot-nodes` category is the tile catalog. `agromancy-plot-actions` enumerates every
`PlotNodeSO` / `PlotNodeActionSO` pair authored by the game, not only Auto Harvest's fruit and
treasure collect pairs. Each row names both handles, shows the active quantity, and answers `add`
and `remove` with one word each: `yes`, or what stands in the way — `not_offered`, `ambiguous`,
`unpriced`, `plot_short`, `list_full`, `inactive`, `unverified`, or `unreadable` when the
Agromancy queue itself could not be read. The detail projection behind a
mutation or an explanation carries the full decision: the plot quantity one instance consumes, the
current maximum additional count (the game's own remaining-instance count, which can exceed the
10,000 this verb accepts in one call), the sentence for a block, and `drainCostsPerSecond`: each
resource the action takes every second it runs, as the diamond's hover prints it, in the price shape
with what the player holds. The `plot-node-actions` page carries the same list, each row naming its
plot only when the action drains on more than one. `unverified` is the
prerequisite latch the game evaluates only when the action starts — the action boundary performs
the exact native check instead of a read mutating the latch, so pressing is how you find out.

Call `game_agromancy(mode="add_plot_action"|"remove_plot_action", uuid=..., actionUuid=...,
amount=...)`. Every call requires an explicit positive `amount`.
Add uses the same active plot-action list control as `UIPlotNodeActionList.OnActionClick`.
Remove decrements an existing quantity; at the native minimum it uses that UI handler's distinct
`Cancel()` path, so crossing from several instances through the last one requires two calls.
Success returns the observed active quantity change, and an add also returns the
`drainCostsPerSecond` the action now takes. The only
postcondition is the exact pair's game-written active quantity moving in the requested direction;
refund behavior on cancellation is neither recomputed nor verified.

`agromancy-processing` is the screen's top processing strip in screen order. Each row reports its
slot, the strip `capacity` and `used` count, and its occupant: the named `plot`, named `action`, and
`amount`. Whether the occupant is engaged right now is not on the row — it turns over between two
reads with nobody playing. The former helper categories for harvest controls/resources, plot
instances, and raw action-queue internals are not public MCP categories; their facts are joined into
these three player-facing surfaces.

## Structure enable and disable

Every `attributes` detail row reports the attribute's current `enabled` state and the one next
toggle the player can take. An unavailable structure carries only `not_available`; the MCP does
not expose the native callback until the same availability fact the screen uses is true.

Call `game_structure(mode="enable"|"disable", uuid=...)` with a published structure UUID.
The boundary revalidates exact `StructureSO` identity, availability, and current state on Unity's
main thread, then invokes the screen's `ToggleDisabled()` route. Success returns only the settled
`enabled` value before and after. The single postcondition is the game-written `disabled` flag
reaching the requested state; `ApplyEffects` and `RemoveEffects` remain native consequences of
that callback and are not independently replayed or audited by the suite.

## Alchemy screen ownership

Alchemy's Learn side is that tree's roll on `game_discover`. Its Loadout side uses
`game_alchemy` with the published recipe pool, capacity-bounded slots, type-capacity
counters, and the same type identity the screen filter displays. Recipe mastery and Alchemy-type
levels are game-driven progression displays, not direct purchase buttons on this screen.

Alchemy loadout operations use ordinary Alchemy recipes and their published capacity. Scribe crafting
uses its authored crafting page. The [catalog scope](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Automata/Runtime/GameMcp/GameMcpEntityCatalogScope.cs)
owns which internal assets are not addressable player entities; an internal station type does not
supply an additional player verb. Consult the shipping descriptor rather than inferring a tool from
a native class name.

## Player loadouts and snapshots

`player-loadouts` lists the loadouts the player titled, in bar order. A detail row reports whether
the loadout is selected, whether its Equipment and Alchemy sections are enabled, the current icon and
color indexes, whether the native manager can switch now, and the named saved spell, Equipment,
and Alchemy entries. All three sections are always present with their own list: a loadout that
saved nothing publishes empty lists rather than dropping the keys, so empty never reads the same
as unprojected. `game_loadout(mode="select", loadout=...)` invokes the manager's whole
save/deactivate/load/reactivate transaction after revalidating every stored reference's identity,
native type, role, and whole-loadout capacity. Current glyph ownership is deliberately not a
selection precondition: the native screen accepts authored saved layouts whose construction
choices are unavailable. Success returns the observed selection change and the settled
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

## Challenge decision loop

Read `time_challenge(mode="state")` before considering prestige. `time_prestige` has no read mode; it requires `confirm=true` and commits an irreversible reset when admitted.

The `challenges` category is both the per-entity read and the pre-decision surface for
`time_challenge`. Every row carries the native idle/queued/active/passed/failed run word, current
and maximum level, native next difficulty/reward, availability/completion verdicts, selection and
offer membership, and explicit `select`, `queue`, and, when active, `abandon` decisions. Challenge
selection has no resource price, so a row does not invent empty costs or affordability.

**A selection fact does not predict an unread budget.** The published world does not carry the
live challenge-selection budget. Its `select` decision explains known offer membership and authored
restrictions without assigning a refusal class to the fact. The action validates the current budget
and performs the native selection or replacement; an action refusal uses the shared wire vocabulary.

**A timed challenge says what it is racing.** The game fails a timed run when
`Player.GetResetTimePassed()` passes `ChallengeCondition.GetTimeLimit(level)`, and the wire carried
only the elapsed half — `world_overview`'s `timePlayedThisReset`. A challenge whose condition is a
time condition now carries `timeLimit` beside its other row fields, as the game's own answer for
that challenge's current `level`, printed in the ultra-precise clock format
(`timeLimit: 30:30`) because that is what `GetTimeBeforeNodes` draws the limit with — the same
format the elapsed variable's own flags select, so the two numbers a caller compares are in one
notation. A challenge with no time condition carries no `timeLimit` key at all, and a limit the
world did not publish is absent rather than zero.

To read "which of these have I already beaten, and can I run them again?", page `challenges` and
read two columns: `level` — one per win, so `level: 1` is beaten once and `level: 0` is never — and
`state`, which says `available` while the challenge can be selected again. `run` answers a
different question, the one the *current* run is in, and it is `idle` on every row outside a run.

`time_challenge(mode="state")` is the screen itself, answered when a caller asks for it: ordered
fully named `selected`, selection capacity, first-draw state, rerolls, and one `reroll` decision
under `challengeState`, with `prestigeState` beside it — the same top-level name and the same block
`time_prestige` returns, said once rather than nested a second time inside the challenge block.
`resetOffers` appears only in the build where the Reset modal's list and the Time screen's list part
company; they draw from the same asset, so it is normally absent rather than said twice.

**Offers belong to the current fetch.** `offers` is present only while `challengesFetched` is yes.
The native list can retain an earlier draw, so a populated list alone is not evidence of current
offers. Running challenges are identified by the owned row's `run` state, separately from selection
and offers.

`prestigeState` is the persistent-reset pre-decision surface. It reports the reset count, the fully
named persistent resource with its current spendable amount and real capacity semantics, the
challenges queued for the reset, surviving rewards, the exact `reset.available` decision, and a
`timeAdvancements` block. No attempt/refusal is needed to learn whether a reset can run.

`timeAdvancements` carries the game's three Time Advancement figures under the game's own three
display names, **and each figure names the run it belongs to beside its number** — `starting=94
(next reset's start)`, `previous=74 (this run's start)`, `new=20 (gain over this run's start)`. The
three anchors are all noun phrases on purpose: `more than previous` is a claim, and beside a `new=0`
it read as the wire asserting that nothing is more than something. `starting` is "Starting Time
Advancements": what a reset would start with, a live projection that keeps moving during a run
rather than a record of one. `previous` is "Previous Time Advancements": what the previous reset
actually banked — which is what this run started with, so it is also the persistent resource's
`capacity` below — and which right after a reset equals `starting` by construction. `new` is "New
Time Advancements": how many more than the previous reset, read from the game rather than subtracted
here, so a caller comparing the block against the screen never finds a number the screen does not
print.

The MCP-only sequence is:

1. Page `challenges`; compare next difficulty/reward, then call `time_challenge(mode="state")` for
   the named ordered offers and the reroll budget.
2. Call `time_challenge(mode="select", uuid=...)`; its terminal response returns the changed target
   state. When every selection the cycle allows is taken and exactly one is held, the tool performs
   the screen's own first press — giving that one up — before taking the one asked for; when more
   than one is held, which to give up is the caller's choice and the refusal says so. **A swap names
   what it gave up**: the answer carries `displaced` with the given-up challenge's identity and its
   own `selected: yes -> no`, the same key a displaced spell slot answers under, so no follow-up
   `state` call is needed to learn which selection the press cost.
3. Call `queue` to move an offered target between idle and queued, or `abandon` for one the reset
   started. A queued challenge starts running at the next reset, not immediately.
4. Call `reroll` without a UUID. It is the game's one new-challenges button and it carries the
   whole weight of the offer fetch: **the first press of a world cycle costs no reroll, draws this
   cycle's offers, arms every challenge it drew to run at the next reset, and unlocks the reset that
   refuses `challenges_not_fetched` until then.** Every later press costs one reroll, redraws, and
   arms what it draws the same way. The terminal response says which of the two presses it was
   (`press: offer_fetch` or `press: reroll`), what the press cost (`rerollsLeft` and
   `challengesFetched` as `{before, after}`), whether the offers moved (`changed`), the replacement
   offer list, what is now armed (`queuedForReset`), and whether the reset opened (`reset` as
   `{before, after}`). The response distinguishes fetching offers from spending a reroll and reports which challenges are queued for the next reset.
5. When the prestige decision is available, call `time_prestige(confirm=true)`. Success waits for a
   newer world after the native scene reload and returns the new scene, `prestigeState`, and the
   challenge state inline. The explicit boolean prevents an empty or accidental call from
   triggering the irreversible reset.

The description is where that is closed, not a second copy of the block.

The MCP-only offer sequence is seven calls when two offers need explanations:

1. `world_list(category="discovery-trees")` and choose a named Idle row whose
   `initiate.available` is true.
2. Call `game_discover(mode="offer_initiate", uuid=...)`; its terminal response is the running
   craft. Both presses land in the tree's Crafting mode, and the game only rolls that craft into
   Choice mode — filling the offer list — three seconds of game time later, so the offers are read
   with the next `world_get` rather than waited for inside the call.
3. Call `offer_reroll` when `reroll.available` is true; a false one names which of the tree's
   states — the tree not shown, no offers, no rerolls left, a reroll already spent — is refusing.
   Its terminal response is the restarted craft, settled the same way.
4. Call `world_get` for the candidates that require comparison. No catalog name joins are
   needed because every reference already carries its name.
5. Call `offer_select` with that `offerUuid`; its terminal response is the settled tree naming
   `selectedOffer`. It omits `offers`: a selection changes which offer is held, not what is
   offered, and the caller just picked from that list.
6. Call `offer_confirm` with the same UUID. The press is the game's own Confirm button, which is
   drawn on choice mode and a selection and nothing else: it costs nothing — the price was paid to
   roll — and `DiscoveryTreeSO.DiscoverItem` asks no question about the item, so an offer the run
   already owns is confirmed like any other. The postcondition is the transition the game makes:
   `ResetMode()` leaves choice mode and drops the selection. Its terminal response is the Idle tree
   plus the next initiate costs, and — when the confirmed offer is a spell recipe the game
   discovered and loaded — a `loadout` block naming the bar slot it went into and the instance
   uuid of the copy that landed there. There are no post-mutation `world_get`
   calls, snapshot tokens, or receipt polls.

## Spell discovery and loadout-add loop

`spell-recipes` is the pre-decision surface for both Spellcraft discovery and loadout add. Each
named row contains every equipped runtime instance of that recipe, and the shared `loadBudget` of
used/maximum slots plus `fitsAnotherSpell`, and the `usageBudget` rows the add gate weighs a
candidate against — each spell-weight resource with its `headroom`, `used`, and `maximum`. An undiscovered recipe exposes
`discover`, which names the Magic > Spellbook > Unlock roll that gives it and the recipe book its
pool is waiting on; a discovered recipe exposes `loadoutAdd`. Loadout add truthfully reports only structural admission plus
`acceptsAugments:true`: the same call takes an augment layout, and no layout changes what the row
can promise, because socketing a spell has no price. A structural refusal is `screen_locked` when
Magic > Spellbook > Loadout is not unlocked — the game draws no row to press, which is a different
answer from unaffordable and from full — or `loadout_full`. There is no selection step and no
target-first `create`: the game exposes neither.

A discovered recipe's row is also its card on Magic > Spellbook > Spells, which
`game_navigate(screen="Magic", subtab="Spellbook/Spells", uuid=<recipe>)` opens: `masteryLevel`, the
mastery bar as `masteryXp` of `masteryXpNeeded` in the screen's figures, and `confirmMastery` — the
Confirm Mastery button's `costs` (each `cost`, `spendableAmount`, `affordable`) and whether a press
lands. Readiness is the mastery bar alone. A yes carries `notYetChecked`, because the spell's
leveling requirements are the one gate no publication carries; a no carries the sentence
`game_spell_mastery single` refuses with. `grants` names the type XP a confirm adds to each of the
spell's types — the `(+X)` the card prints beside each type track — folded from the per-level amount,
the type's Type Xp and the player's All Spell Types Xp.

A discovered recipe's `weight` is what loading the bare spell charges, in the game's own math:
authored upkeep, plus the penalty while the spell's "use effectively" requirements are unmet (`12
Knowledge, including 6 more until its "use effectively" requirements are met`), less what its spell
types take off. `loadoutAdd` weighs that against the allocation's free room, so a spell that does
not fit reads `available: no` with the sentence the press refuses with: `It weighs 12 Knowledge and
7 is free. Remove loaded spells weighing 5 more, or raise that allocation. → Loaded spells and
their weights: world_list category=spell-slots`. A `preview` that names a usage allocation, and the
`staged` read, carry the same pointer under `loaded`, because the bar a layout is weighed against is
a different category.
Each loaded spell, as a `spell-slots` row and under `equipped`, carries its own `weight`: the number
its slot's badge shows, augments and loadout free uses counted as the game counts them, which is what
removing it frees.

Where the page says `available: yes` it also says under `notYetChecked`, in one sentence, what only
the press settles: the weight and requirements augments add. It is a sentence and not a list of tokens,
because a list of two words a reader has never seen sends them looking for a vocabulary page that
does not exist. The page predicts what it can read and promises nothing about the rest; it never
states a rule the game's add path does not have. The unique-spell rule was a fourth entry until the
world published the fact it reads: every equipped instance of this recipe is on this same row under
`equipped`, each carrying `isLoadoutUnique`, so a caller settles the rule before it calls rather
than by being refused. The verb still re-reads it live and refuses in the same words.
`augmentOptions` is a per-recipe answer, filtered
to the augments this recipe admits and carrying each one's usable ceiling, and it is published
beside a refusal as well as beside a yes — the call the page refuses is still the call it has to
teach.

A detailed row also carries the recipe's authored half, which is what the spell is before any
modifier touches it: `casting`, `authoredCosts` split into `cast` / `upkeep` / `hold` with each
`resource` and its unmodified price — the resource cell carries the player's own word for it, so
there is no `name` column beside it saying the same word again — and `belongsTo` naming the recipe's `spellTypes`,
`coreGlyphs`, and `recipeBooks`. These are authored facts, so they do not move within a run; the
live price a cast will actually pay is the `castCosts` on the equipped instance, not
`authoredCosts`.

The `casting` block says what it means rather than what the game stores:

| Key | What it says |
| --- | --- |
| `castType` | `instant`, `channel`, or `aura` |
| `rechargeCountsIn` | what the recharge counts down in: `time`, `spell-casts`, or `attributes-developed` |
| `rechargeUnitMultiplier` | what one counted unit is worth against the recharge. The game's own `Duration.Entry.GetMultiplier()` forces exactly `1` whenever `rechargeCountsIn` is `time` |
| `maximumChannelSeconds` | present only where the recipe authors one |
| `repeatEffectSeconds` | present only where the recipe authors one: the seconds **between** re-applications while a toggle is held |

Two of those names are deliberately not the game's. `rechargeProcessorType` named the processor
class the game constructs rather than the question a reader has, and the projection renders its player-facing meaning. `repeatInstantEffectRate` is a naming trap: `Spell.InitializePersistence` hands it straight to
`TickTimer(tickTime, …)` as an interval floored at `0.01`, so the field the game calls a rate is a
period, and the obvious reading of a bare `repeatEffectRate: 1` is the reciprocal of the truth.

**The authored block names what the recharge counts, not how long it is.**
`SpellRecipeSO.baseRecharge.duration` is a constant with no level, no cooldown penalty and no
cooldown speed in it, and no screen in the game shows it — a row printing it beside a bar counting
down from a different number is a wrong number rather than an early one. What the bar counts is
published on the equipped instance as `recharge`; the three cooldown numbers a spell has are
[in one place](../game-systems/spells.md#three-numbers-describe-one-cooldown).

The MCP-only base-recipe sequence is:

1. Page or search `spell-recipes`; compare names and recipe books.
2. An undiscovered recipe comes from the Magic > Spellbook > Unlock roll: drive that tree with
   `game_discover`'s `offer_*` modes. Confirming a spell offer reports, when the game loaded the new
   spell, the `loadout` block naming the bar slot it went into and the instance uuid of the copy
   that landed there.
3. If an equipped instance is wanted, call
   `game_spell_loadout(mode="preview", uuid=..., glyphs=[{"uuid": "<augment glyph id>", "count": 1}])`,
   or `glyphs=[]` for no augments; a wrong `glyphs` shape is refused with that shape. This read runs every admission
   `add` runs before `add` stages anything — the screen being unlocked, the glyph ceilings, glyph
   duration/toggle requirements, usage requirements, the weight fitting, a free loadout slot, and
   loadout uniqueness — without touching the player's staged UI selection, so an available preview
   is a layout `add` will not refuse on the same arguments. Loading has no price, so the answer
   carries no cost or `affordable`: it is `status: available` with the `usageAllocation` the loaded
   spell would hold and the `loaded` pointer to `spell-slots`, or `status: refused` with the
   refusal's code and sentence. Augments are baked in, so on a full bar a recipe that is already
   loaded is priced as a replacement of its lowest-slot copy: the free-slot and unique-copy checks
   are skipped, that copy's weight counts as free, and the answer adds
   `replaces: Channel Spark in slot 2 — remove it, then add`. With `replaces`, `add` holds after
   that removal. Augments' loadout-wide free uses are counted as they stand, so when the replaced
   copy holds some, the preview reads heavier than the swap will.
4. Call `game_spell_loadout(mode="add", uuid=..., glyphs=[...])` with that same layout. Adding is
   the only mutation where the layout is chosen; it is baked into the created runtime spell.

Every referenced entity is named inline. No catalog join, world-generation argument, payment
stanza, receipt poll, or post-mutation `world_get` is required.

## Level dial loop

Four global -/+ level dials move a level inside a cap an upgrade raises: Output Lv and Reserve Lv
on Magic > Casting, Druidry Lv on World > Agromancy, and Alchemy Lv on Alchemy > Alchemy > Loadout.
Each is one player `IntVariable`, not a per-spell or per-plot setting, and all four go through one
level-dial action. `world_overview` carries them as `castingDials.outputLevel`,
`castingDials.reserveLevel`, `druidryLevel`, and `alchemyLevel`, each as `current`/`maximum` — the
level the screen shows beside the ceiling it has been raised to, under the words that screen prints.
The casting block is absent until the Output maximum is nonzero; `druidryLevel` and `alchemyLevel`
are present exactly while the view that draws their adjuster is unlocked. **The floor of every dial
is 1**, in every save and on every call, so it is documented here rather than repeated in every
overview a caller reads.
Raising a cap is an ordinary `game_purchase` against the corresponding upgrade UUID, so the dial
verbs only move the level inside the live native range.

Each dial is set on its own screen's verb: `game_casting_dial(dial="output"|"reserve", value=N)`,
`game_agromancy(mode="set_druidry_level", level=N)`, and `game_alchemy(mode="set_alchemy_level",
level=N)`. The level runs from 1 to the live native maximum, which the action boundary reads from
the exact player variable for that dial before verifying the requested level became observable. A
committed result returns the changed `dial` as `before` and `after` plus the `maximum` the read
publishes. The game draws the Druidry Lv adjuster only once Unlock Druidry Levels is bought, and the
Alchemy Lv adjuster only once Level Alchemy is; until then the call is refused `screen_locked`,
naming that upgrade, before anything is written.

There is deliberately no in-place augment editor. The visible game has none: glyph layout is chosen
on the library candidate before add, and changing it is remove → relayout → re-add.

A recipe's `loadoutAdd.augmentOptions` names the owned augments that this candidate admits,
including the allowed count. It is present whether the add decision passes or refuses. A purchased
glyph level is not the socketing verdict: use native availability and usable copies. A preview's
`offset` and `limit` page candidate choices rather than the one preview row itself.

## Spell loadout loop

`spell-slots` is the pre-decision surface for `game_spell_loadout`. Each occupied detail row names
the recipe the equipped spell was baked from, its slot, its `recharge`, active cast/ready/attune
state when applicable, whether it can be removed right now, `isLoadoutUnique`, and whether that
spell can move at all. `recharge` is the number the casting bar counts down — `Spell.GetRecharge()`,
the instance's own cooldown time divided by its cooldown speed — spelled as a duration through the
same clock every countdown on this surface uses (`18.7s`). A spell whose recharge counts something
other than time says the count and its unit (`5 spell-casts`) in the words `rechargeCountsIn`
publishes; where the recipe's recharge kind is unpublished the row carries no `recharge` at all,
because a number in the wrong unit is worse than an absent one.
A blocked `remove` says `screen_locked` when Magic > Spellbook > Loadout is not unlocked, and
otherwise which of the game's own three gates said no. The game answers a blocked removal with
one popup for two of them — *Cannot remove a spell that is still recharging.* — so both sentences
open in its words and then say which: `cast_in_progress` while the spell is casting or readying a
cast, and `spell_recharging` below full charges, which also carries `charges` in the one spelling
every surface uses (`2 of 3`) and `nextChargeIn` as a duration while a cooldown is running, because
the row itself prints no charge count. Where it can
move is the slot list, which is one read for the whole bar: inlined per spell, explaining eight
spells delivered the same eight-slot roster eight times. Augment choices
appear only on a discovered recipe's `loadoutAdd` decision. `loadBudget` — `used`, `maximum`,
`fitsAnotherSpell`, and the `usageBudget` rows — rides on every detailed `spell-recipes` row, so
both budgets are known before add. The spot count and the usage budget are different gates: a
loadout can have an empty slot and still refuse every spell that would fit in it, which is why the
resources the usage gate weighs a candidate against are named with their headroom rather than left
for the caller to guess from the whole resource table.

An equipped spell is a runtime instance, and the catalog publishes assets, so that instance has no
handle any tool can resolve. The row therefore carries no id of its own: the recipe names the spell
and the slot addresses it, which is also what the bar on the screen shows.

`isLoadoutUnique` is the game's own `Spell.IsUniqueSpell()` answer for the occupant, and its scope is
narrower than the word suggests: it is neither one spell per slot nor one spell per type. The fact is
authored on the spell type as `SpellTypeSO.isLoadoutUnique`, the game reads it across the types the
equipped instance actually carries — the recipe's not-types plus the types its own glyph layout
grants — and `Spell.GetEquipRequirements` refuses with "Cannot equip duplicate charms" only when a
loadout-unique candidate finds an already-equipped spell baked from the same recipe. So a `true` here
means one thing: while this spell is equipped, the game will refuse a second spell from this recipe.
It says nothing about any other recipe, and a recipe whose types are not loadout-unique pays nothing
for the rule. No spell type in the published build sets the flag, so every row reads `false` today —
which is the reading the game gives, not a constant this suite substitutes for one.

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
   address the caller's model did not contain.
6. Call `game_spell_loadout(mode="remove", slot=...)` only when that row's `remove.available` is
   true; success returns the full `slot: N -> empty` move, the `loadBudget` it freed with both
   budgets as `before -> after`, and `oneWay` — the game destroys the spell instance, so the way
   back is another add.

`staged` accepts no other field. The `uuid` means a recipe and belongs to `preview`/`add` only;
`slot` addresses the loadout bar for `remove`/`move`, and `destination` belongs to `move` only. A
refused `remove` or `move` echoes that `slot`, never the spell instance behind it, which no catalog
names; its sentence names the spell.
Anything else is refused at validation, naming the field and the mode, rather than ignored.

Add is the Loadout list row's own click, in one call. The game's load path reads the augment
staging and nothing else, so add snapshots the player's staging, clears it, stages exactly the
requested augments, loads the recipe, and puts the staging back. The Recipe Book selection is not
part of it, and neither is "Max Spell Creation Slots": the one creation slot is the verb's problem,
never the caller's, which is why a two- or three-glyph recipe loads in one call.

Admission is the five facts the library row's own button reads, in its order — discovery, non-level
augment requirements, the recipe's usage requirements (which only gate while an augment is
selected), usage-cost affordability, and an empty loadout spot — plus the game's own unique-spell
compatibility and the distinct-augment ceiling ("Max Spell Augment Slots"). Nothing else is
checked, because the game checks nothing else. There is no price: the loadout's usage allocation is
the only budget a load is weighed against, it is settled by the game after the load rather than
paid at it, and the answer re-reads it instead of predicting it.

A staging attempt that does not read back returns `ERR_UNAVAILABLE`, naming what was
written and what came back. The staging write is a press, so that refusal counts one attempt and
answers only once the player's own staging reads back as it was; a staging that cannot be put back
is a fault.

Remove asks the three questions `SpellManager.RemoveSpell` asks itself — full charges, not casting,
not readying a cast — before it calls, because the refused branch is not a no-op: the game switches
the spell to a time-based cooldown on the way out. The refusal names the recipe, quotes the game's
own "Cannot remove a spell that is still recharging.", and states the live charges and the time to
the next one. While Auto Cast is on it casts the spell again as soon as it can, so a removal pressed
from any other screen never finds it full; the refusal then adds the way through: Auto Cast stands
aside while Magic > Spellbook > Loadout is on screen, so `game_navigate(screen="Magic",
subtab="Spellbook/Loadout")`, wait there until it reads full charges, and remove. `Spell.CanRemove()` reads like the rule and is not it — the removal path never calls
it — and gating on it called slots stuck that the game would have cleared. Remove and move
re-resolve the named slot and its live gates or slot range on the Unity main thread.
Every mode acquires the family permit last and verifies only requested identity/outcome. Weight,
glyph usage, drain, and resource accounting are observations, not gates. There is no generation,
payment, receipt, request echo, catalog join, or post-mutation read-back.

## Targeting decision loop

`targeting` is the pre-decision surface for `game_targeting`. It is empty while no target request is
pending, so the row's existence is that fact and there is no column repeating it. The row is two
columns: the requesting effect, named the way the player sees it, and the eligible attributes,
strongest `effectiveLevel` first because that is the column a caller picks by. A caller who names no
limit is handed 25, the page says `candidates 25/180 next=25`, every candidate stays reachable at
the next offset, and an offset past the last candidate answers with an empty page the way every
other category does. Each candidate is fully named and includes current committed/effective level,
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

## Consumable decision loop

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

## One-shot crafting decision loop

`crafting-recipes` carries everything needed to choose a one-shot craft: player-facing identity,
native route, purchase amount, exact named costs and current holdings, affordability, queue
identity/room/current quantity, outputs, and blockers. The MCP-only sequence is:

1. Read `world_list(category="crafting-recipes")` or batch exact recipes through `world_get`.
2. Choose a row whose `canStart` is true after comparing `nextCosts`, outputs, and queue state.
3. Call `game_craft(uuid=..., amount=...)`.

Success returns how many presses queued or completed.
It has no receipt, payment stanza, world-generation argument, or read-back requirement. A timed
recipe without one stable loaded authored page refuses rather than guessing a queue; failure after
native work says the change never showed up on Scholar > Scribe and may or may not have landed.
Manual crafting is its own GameAction beside Auto Scribe's, under the same crafting permit.

## Purchase arguments and results

A committed purchase reports one count, `queued`, and the queue rule above is why: the two counts
the screen owns, `level` and `queuedLevels`, both move again while the answer is being written, so a
`{before, after}` pair over either reports a transition nobody can confirm.

A committed purchase reports the levels it bought and nothing about what it charged. `queued` is
what says how many levels were bought. What a level costs and what the next one asks are read where
the whole curve lives, on `world_get` and the `purchase-costs` category, which is also where Auto
Buy plans from. A refusal that could not afford something still names every short resource, its
price, and what is held, in its own sentence.

`spendableAmount` still rides a cost row on the read surface, in the same spendable coordinate every
cost row uses. It does not have to equal the same resource's `amount` on a `resources` row, and
where the two differ it is not drift: `spendableAmount` is spendable amount, which a **bandwidth**
resource reports as the headroom left rather than the stock held, while a row's `amount` is the
displayed quantity, which an **inverted** resource reports counting down from its cap. The two
traits are independent, so either key can be the larger one depending on which traits the resource
carries. When the settled world publishes no row for that resource the key is absent, because zero
is a balance and "not collected" is not.

There is no delta field: the difference between two worlds also contains every income stream and
every other spender in that window, so it is not a price and is not computed.

Where a target UUID is supplied, the server derives its native type and action kind from that UUID:

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

Every `game_discover` mode requires the tree's `uuid`. `offer_select` and `offer_confirm` require
`offerUuid`; `offer_initiate` and `offer_reroll` reject it. Initiate and reroll verify the exact tree/type and immediate transition to Crafting;
select verifies the requested offered UUID became selected; confirm verifies that exact UUID became
discovered. Payment deltas, reroll values, counters, flags, timers, list cleanup, and selection
cleanup are neither outcome gates nor response data. This matters when a cost is below the ULP of a
very large `BigDouble` amount: an unchanged amount cannot disprove a transition the game visibly
performed. On success, payment is presumed and completely omitted. Initiate/reroll wait for the
ordinary collector to publish the Crafting state their press produces; the offer list is filled by
the tree's own timed increment three seconds of game time later and is therefore outside any settle
budget. Select returns the selected state. Confirm permanently spends a discovery choice, so it
names the discovery it took — the identity the caller passed as `offerUuid` — and moves
`treeDiscovered` and `mode` as pairs, with whether the tree still has discoveries left. The count
travels with the `discoverableCount` it is a count out of, on the tree row and on the confirmation
alike: the two are the game's own cached `totalDiscoveredCount` and the size of the very list
`CountDiscoveredItems()` counts it from, so `3` and `3 of 40` are not the same answer to how far
into a tree a caller is. **The name says whose count it is.** `totalDiscoveredCount` moves only
when this tree's own `DiscoverItem` runs, so a thing discovered any other way is not in it and the
number sits one behind what a caller who just discovered something expects. Named `treeDiscovered`
it is a fact about the tree, which is what it has always been; named `discoveredCount` it read as
the count of everything discovered.

**A tree with no current offer explains whether it is finished or waiting on its pool.** The native
`hasRemainingDiscovery` is false both when all discoveries are made and when undiscovered items
remain but none is visible and in the pool. An idle fact row distinguishes those conditions with its
counts and reason sentence; it does not publish an `ERR_*` class for either fact. A missing Recipe
Book is named where that relation is known. The next initiate price belongs to the initiate decision.
An actual action refusal or unavailable read envelope uses the wire failure vocabulary described
above.

Failures name only the failed admission or missing transition and the fact that explains it: a
reroll refused for a spent budget names that one axis and carries `rerollsLeft`, never a recital of
the preconditions the code believes it enforces.

## Casting arguments and results

`game_cast` uses the visible spell button's native route. `fire` starts a ready spell, `release`
lets go of the suite's charge hold, and `toggle_off` presses an already-active toggle spell again.
The last mode requires the slot still to contain the exact recipe UUID and native `Spell`, the spell
still to be a currently casting toggle, the visible cast button to remain available, and the
player's Cancellable Spells setting to allow the press. Its one outcome sentinel is the native
casting state changing from active to inactive. The settled response is only the named recipe,
slot, and settled `active` state; a refusal names the binding setting or live spell
state. Detailed `spell-slots` rows expose `toggleOff.available` so the setting never has to be
learned by attempting the action, and carry `casts`, the game's own per-spell manual cast counter.
A settled `fire` waits for a world that shows the cast this press started, in either of the two
faces that prove it: the slot readying or casting, or a charge spent since the press. The game
spends the charge at the *end* of a cast — `Spell.Cast()` sets the prep state and only
`EndCasting`/`ExecuteSpell` consume the charge — so a spell slower than the world cadence is still
readying when the next world lands while a faster one has already finished and dropped one. An
instant spell with no recharge to wait out is finished and full again before the next world, so
for it the slot's `casts` count rising since the press is the proof. Requiring both would time out half of all casts; requiring neither answered a world that had not
yet seen the press. When neither face arrives inside the settlement budget, which a heavy save's
late world can cause, the answer is the press's own: `casting: yes` with the slot and `charging`,
and none of the slot facts only a settled world holds. It carries no cast
counter at all. The game writes that counter when a cast *completes*, frames after the press and
sometimes before the next world is published, so it was the same number whether the press landed or
was dropped. A press at a spell that is already running, or still starting its own cast, is refused
rather than committed silently —
the game's own button answers it with a warning popup or an end-of-cast, never with a new cast — so
a repeated fire can never look like a firing loop that is doing nothing. A press while a *different*
spell is still readying is refused the same way and names the spell holding the caster, because
`SpellManager.CanCastASpell()` is exactly "no active spell is readying": the game casts one spell at
a time, and which one is in the way is the only thing a caller can wait on. `casting` is a `fire`-only
key, and deliberately: it is the one mode with a native delta behind it, a release being a native
call with nothing to verify a cast start against and a toggle-off's own sentinel being a cast
ending, so neither of those may claim the fact in either direction.

A settled `fire` also carries `nextCastCosts`: the price of casting this spell **again**, against
the stock the settled world holds now.

Every mode answers with the same keys, and a key that can be `yes` says `no` rather than
disappearing. `active` — the settled running state — and `charging` — whether this press left a
charge held — ride on all three.

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

**A fire says the instant the spell is in.** Where the press left the spell short of full charges
the response carries `charges` in the one spelling every surface uses (`1 of 3`) and `nextChargeIn`
as a duration; at full charges it carries neither. Present exactly when the spell is short is a rule
a caller can predict, unlike a pair that appeared whenever the settlement window happened to catch a
number moving . There is no `cooldown` field: it read no charge timer
of the game's, so it answered `0` on all 144 while the spell was recharging, and a number no screen
shows is worse than silence.

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

A level-dial call — `game_casting_dial` with `dial` and `value`, or a `set_druidry_level` /
`set_alchemy_level` mode with `level` — takes no UUID at all, because every level dial is a single
global variable. The boundary reads the exact variable and its purchased maximum on the Unity main
thread, rejects a level outside that live range, and verifies that the requested level became
observable. A committed result names the `dial` it moved plus its `before`, `after`, and
`maximum`.

## Loadout arguments and results

`game_spell_loadout` requires `mode`. `staged` is a request-scoped main-thread read with no other
arguments; it reports the exact current core/augment selection and never mutates it. For `preview`
and `add`, `uuid` is a spell-recipe identity and an explicit `glyphs` array is required. Preview
answers the same admission the add applies and returns the usage allocation the loaded spell would
occupy, without changing the staged UI selection or acquiring mutation ownership. For `remove` and
`move`, `slot` names the loadout-bar position and `uuid` and `glyphs` are rejected; `move`
additionally requires a `destination`, which no other mode accepts. Add stages the requested
augments into the game's own selection, calls the manager's load route, restores the selection it
found, and verifies the exact requested loadout outcome; there is nothing to pay. Remove rechecks
the three live facts the game's own removal gates on — full charges, not casting, not readying a
cast; move re-resolves the source slot and invokes the same native swap-plus-notify path as the
spellbook. Every mode first reads `ViewSO.IsAvailable()` for Magic > Spellbook > Loadout, because a
screen the game has not unlocked has no button to press and no bar to change. A glyph named in a
layout is admitted on the game's own two facts and no others: `GlyphSO.IsAvailable()`, whether the
Loadout page offers a copy at all, and `GetMaxUsages()`, how many copies one spell may hold. A
refusal from an action or preview for a resolved glyph is never a missing-identity verdict:
unavailable uses `ERR_LOCKED` and names the screen, while a usage over-ask uses `ERR_LIMIT` and
carries `x of y`. Success is the exact added instance, exact target absence, or the exact target at
its destination. A committed result returns only the recipe identity and slot change; a failure
names only the unmet admission or missing outcome.

## Targeting and item arguments

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
`amount` (a positive whole number, default 1, `craft` only) is how many crafts the call queues. A
count recipe queues the whole ask as one craft of that quantity, clamped to what the game's own Mx
press would queue — the most the stock affords. A Scroll recipe's quantity is the scroll's level, so
each scroll is one submission at the level the screen's x1 press takes. `amount="max"` queues what
the screen's Mx press would, through the game's own most-affordable rule. A committed `craft`
answers with the crafts its own sentinel verified: `queued: N` for a queued craft, `completed: N`
for a direct or instant one. Like `game_purchase`, the ask is taken as far as the stock goes, and a
shortfall says both numbers and why on one line: `queued: 3 of 45 asked; the rest stopped: The
cost of another of this craft is not met.` A refused first submission answers as that refusal; a
craft the stock cannot afford refuses `unaffordable` with the unmet price from the recipe row's
`nextCosts`, in the purchase shortfall's words: `Needs 75 Wood (have 0).` One
that faults after earlier crafts landed answers as the fault, and its sentence opens with how many
of the ask went through before it.

The other modes use the same authored page relation and exact recipe identity.
`automate` adds one repetition through `CraftingInstanceListVariable.AutomateCraft`, and
`cancel_automation` removes one; the two cancel modes call the exact manual or automated instance
route shown by the UI. The recipe row publishes manual queued amount and the
automated quantity/capacity needed for the next decision. Success is one settled change, published
as `amount: {before, after}` in the strip's own badge coordinate — one `cancel_automation` on a
doubled entry moves the badge 8 to 4 while the repetitions behind it move 4 to 3, so the response
names the number the screen shows. Refund accounting is neither computed nor used as a gate. A fault
that already moved the native automation quantity reports it under `observed` as `repetitions`,
which is the coordinate that call returns, so a caller is never invited to retry into more damage.

`game_equipment` requires `mode` and one published equipment `uuid`. On Unity's main thread it re-resolves
the exact artifact and repeats creation, current stacks, global and primary-type slot room, maximum
stacks, and native usage-affordability checks before taking the family permit. The caller's explicit
`amount` must fit that live maximum exactly; an oversized equip or unequip refuses with the maximum
the current state permits and never clamps to a different mutation.
Success is only the exact requested target-stack transition. It returns the target's stack count
before and after, with no receipt or payment/usage stanza. A missing transition
faults that attempt; a throw after the exact transition commits.

## Time and research arguments

`time_challenge` requires one of `select`, `queue`, `abandon`, `reroll`, or `state`. The three
target modes require a published `ChallengeSO` `uuid`; `reroll` and `state` reject it. `queue` is
what the screen's "activate" button does — the challenge starts at the next reset, not now — and
`abandon` names the only state a challenge can be abandoned from, the one a reset started. A queue
press at a challenge whose run is over is refused `ERR_STATE` with the standing rule, "A challenge
that has already run cannot be queued again until the next reset": the input was valid and the state
was the blocker, and the older wording — "its queue toggle does nothing" — read as a shrug about
that one press, so the result states that scope. The
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
not clear presses the given-up row back and refuses `selection_restricted`, counting the give-up as
one attempt and saying the row is held again. When the game does not show it held again, the answer
is a `verification_failed` fault instead, because the caller is a selection poorer.

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
type, availability, affordability, and mutable state from scratch. For the same reason,
MCP gameplay tools are registered from runtime capability admission rather than automation
configuration, so disabling Auto Scribe or any other feature cannot make the corresponding manual
verb disappear.

CLI play commands therefore need no generation option:

```sh
tools/game-mcp-client.py purchase UUID
tools/game-mcp-client.py cast 0
tools/game-mcp-client.py call game_agromancy --arguments '{"mode":"add_plot_action","uuid":"PLOT_UUID","actionUuid":"ACTION_UUID","amount":1}'
tools/game-mcp-client.py concept-add UUID
tools/game-mcp-client.py spell-level UUID
```
