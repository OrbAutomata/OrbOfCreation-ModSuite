# Spells

Casting is the game's first *active* production loop: resources that have no passive rate exist only
because you cast for them.

Every spell has a **cost**, a **cooldown** and an **effect list**. E.g., the first spell of a run:

| Gather Knowledge, Lv 1 | Value |
|---|---|
| Types | Primary, Divining, Cantrip |
| Cost | 100 mana |
| Cooldown | 7.35 s |
| Effect | +2.01 Knowledge per cast |

Casting is available from the Spellbook and from the hotbar. Hotbar and keyboard casting do not
require the Spellbook screen to be open, so casting is never gated behind having the right tab
visible. Some spells open a **target prompt** when cast and resolve only once you pick a target.

Displayed cost is not the amount debited; see [cost-pipeline.md](cost-pipeline.md).

## Three numbers describe one cooldown

A spell's recharge exists three times over, and the wait you actually sit through is the third:

| Number | What it is | Expand Magic |
|---|---|---|
| Authored recharge | the recipe as written, before the run touches it | 22 s |
| Cooldown Time | the spell you own, after its level and every cooldown modifier | 27.5 s |
| Recharge | Cooldown Time divided by Cooldown Speed | 18.7 s |

A spell's tooltip prints the last two under exactly those names, and the bar under the spell counts
the third down, so a spell whose Cooldown Speed is above 100% waits less than its Cooldown Time
says. The first is on the recipe asset and no screen shows it at all — a cooldown quoted from a
screen is always one of the other two, and which one depends on where it was read.

A recharge does not have to count seconds: it counts whatever the recipe says it counts — time,
spell casts, or attributes developed — and Cooldown Speed divides the count the same way it divides
the seconds.

## A spell's effect list is run state

A whole class of upgrades does not buff a number — it **appends a row to another entity's effect
list**. E.g., "Improve Whirling Sorcery" turned a single-effect charm into a two-effect charm. Some
later spells are designed to do nothing but this.

So two saves can hold the same spell at the same level with different effects, and a spell's printed
description is not a static property of the spell.
