# Discovery

Discovery is how the game hands you new *things* rather than more of a thing you already have. It is
one mechanism on **seven trees**:

| Tree | Where | Produces |
|---|---|---|
| Spellcraft | Magic > Spellbook > Unlock | spell recipes |
| Glyphcraft | Magic > Augments > Glyphcraft | augment glyphs |
| Concepts | Scholar > Concepts > Discover | concepts |
| Alchemy | Alchemy > Alchemy > Learn | alchemy recipes |
| Artifacts | Workshop > Artifacts > Create | artifacts |
| Devote | Rituals > Discover | rituals |
| Runecraft | Time > Time Runes > Create | time runes |

## One button, not a workbench

A discovery page presents a **single purchase: the next thing**. You do not compose anything and you
do not pick the output — the game resolves what comes next; you pay, and where a roll offers options
you choose one. **There is no recipe list anywhere in the game**: no page enumerates what you could
make and lets you select it.

Components are **unlocked, never consumed or slotted**. Once unlocked, a component is active
permanently, and what you have unlocked defines what can come out: an output becomes reachable once
all of its pool unlockers are unlocked, which is why unlocker purchases widen future rolls rather
than doing anything immediately. See [pool-unlockers.md](pool-unlockers.md).

**Code shape:** the data still models discovery as components composing into an output — the shape
the interface exposed before the game's 1.0 release. Code-level readings that speak of composing
describe that data model, not anything the player does.

## Required components versus optional pool picks

| | Required component | Optional pool pick |
|---|---|---|
| Marker | exclamation mark | none |
| Options offered | effectively one | the choice-count statistic |
| Cost | the tree's thematic resources | the tree's pool ladder |
| Advances the price ladder | no | yes |

Required rolls are the critical path: the game is not gambling with your progression, and you receive
the one thing it intends you to have next. Optional picks draw several candidates from that tree's
pool and make you choose one.

## Choices and rerolls are statistics

How many options a roll offers is a **statistic** (observed at 2 early in a run), and so is **Max
Discovery Rerolls** (observed at 1), so upgrades and effects can move both.

- Rerolling replaces the options offered. **Rerolling never rerolls the rarity** of the offer.
- You **gain a reroll back only by taking a choice without having rerolled**.
- Rerolls do **not** advance the pricing ladder.

## The commitment point

**Paying the cost is the commitment, not the pick.** Paying rolls the dice; once rolled you must
choose one of the results, and there is no walking away.

An open choice **blocks the roll queue**: while a discovery choice is unresolved you cannot roll newly
unlocked required components in that tree, so a pending choice can stall the critical path behind an
optional pick you were undecided about.

**Deferral is therefore only valid before paying** — and before paying it is genuinely free:

- **Unpicked options persist into later draws.** Declining an option does not burn it.
- **Nothing about an offer expires on a timer.**
- Waiting *changes* the offer for the better, because unlocking further glyphs enlarges the pool a
  roll draws from.

## Offers are events

Discovery offers are events, not a standing page: there is no inbox that collects pending
discoveries. They surface where and when the game raises them.

## The pool is authored

Which candidates a tree can offer is fixed in the game's data ahead of the roll: a given unlocker
opens a knowable set of outputs, and a roll selects from that authored set. The game deliberately
does not show it — not knowing what is in the pool is part of the design. The per-tree offer tables
are not documented here; see [open-questions.md](open-questions.md).
