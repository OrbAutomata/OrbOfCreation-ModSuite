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

Three of those pages wait on their screen rather than on anything about discovery: Rituals >
Discover, Workshop > Artifacts > Create and Time > Time Runes > Create do not exist until the
Rituals, Workshop and Time screens are unlocked (see [screens.md](screens.md)). Rituals > Discover
then waits further on the **Principle Stone** upgrade.

Alchemy and Concepts produce one kind of thing between them: a concept is an alchemy recipe by
another name, which is why the Concepts list groups its entries under alchemy recipe types. The two
trees stay separate — separate pools, separate ladders, separate uses; see
[concepts.md](concepts.md).

**Code shape:** both trees are typed `AlchemyRecipeSO`. What separates a concept from an alchemy
recipe is which tree drew it, not the type.

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
| Options offered | effectively one | Discovery Options (+1 on Alchemy) |
| Cost | the tree's thematic resources | the tree's pool ladder |
| Advances the price ladder | no | yes |

Required rolls are the critical path: the game is not gambling with your progression, and you receive
the one thing it intends you to have next. Optional picks draw several candidates from that tree's
pool and make you choose one.

## Choices and rerolls are statistics

How many options an optional pick offers is the **Discovery Options** statistic
(`DiscoveryTreeChoiceCount`, base 2), and the reroll budget is **Max Discovery Rerolls** (observed at
1). A tree offers `max(1, Discovery Options + its own bonus)`; the Alchemy tree's bonus is +1, every
other tree's is 0, so alchemy shows one option more than the rest.

Exactly three authored effects move Discovery Options:

| Source | Kind | Effect |
|---|---|---|
| Greater Discovery | research (Innovation, one level) | +1 |
| Orb Minimalism | achievement | +1 once completed |
| Weakened Choices | challenge | -1 while it runs, through the `ChallengeEffect` weight (and Max Discovery Rerolls -99, unscaled) |

So a third option on an ordinary tree comes from Greater Discovery or Orb Minimalism; Alchemy already
offers three.

- Rerolling replaces the options offered. **Rerolling never rerolls the rarity** of the offer.
- The Reroll button is drawn on the reroll budget alone, so a required component can be rerolled
  too — and it is the one thing that tree can offer, so it comes back.
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
