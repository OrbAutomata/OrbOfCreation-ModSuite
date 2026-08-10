# Attributes and upgrades

Attributes and upgrades are the game's main purchase surface: the rows with a cost line and a buy
button that make numbers go up. On screen an **Attribute** is the levelled, priced thing; a
**Statistic** is never bought directly. See [vocabulary.md](vocabulary.md).

## Names and categories are both targetable keywords

An attribute carries its own name *and* a category, and **both are things effects can target**.
Observed categories include Container ("attributes that hold resources"), Machine, Tool, Aura,
Talent, Purity, Radiant, Innate and Infusion. An effect reading "+X % Aura Effect Levels" hits every
Aura you own, present and future; an effect naming a single attribute hits only that one. This is the
ordinary effect grammar; see [effect-grammar.md](effect-grammar.md).

## Bonus levels are power, not progress

A row's displayed level includes bonus levels granted by effects, but requirement gates test only
the levels you actually purchased — a node showing a green `+5` can still fail a `≥ 5` requirement,
with no hint that the shown number is not the tested one. Bonus levels also do not advance the paid
cost curve: purchased level 2 with 2 bonus levels costs what level 2 alone costs.

## Upgrades are one-shot; attributes are not

Most upgrades are genuine one-shot milestones: they either unlock something or make something
stronger, once. E.g., on one observed save 207 of 222 upgrades had a maximum level of 1 and were
already bought out.

## Entities can change category mid-line

Some purchases start life as an Upgrade and become an Attribute once they run past their upgrade
levels. E.g., Infuse Orb is an upgrade for its first few levels, then transitions into a structure
with infinite levels — and **scales more slowly** as a structure than it did as an upgrade. The
`Infuse <resource>` family generally behaves this way.

Two consequences: the same thing appears on two different kinds of list over its lifetime, and its
cost curve visibly changes shape at the boundary.

## A `Raise …` upgrade buys a ceiling, and the level it caps is a separate dial

Some levels are two things wearing one name. The screen shows one number like `264 / 264`, but the
game holds it as a **dial** and a **cap**, and the buy button beside it touches only the cap. Alchemy
Level is the worked example, and the same shape carries the Casting page's two dials — see
[output-and-reserve.md](output-and-reserve.md).

| Aspect | Authored as | What it is |
|---|---|---|
| The dial | `AlchemyOutputLevel`, an `IntVariable` shown as **Alchemy Lv** | The level actually in effect; its persistent effects scale the alchemy speed and drain groups |
| The cap | `MaxAlchemyLevel`, an `IntVariable` shown as **Max Alchemy Level** | The ceiling the dial may be set to, starting at 1 |
| The purchase | `RaiseAlchemyLevel`, an `UpgradeSO` shown as **Raise Alchemy Level** | Adds `+1` to `MaxAlchemyLevel` per level bought, and nothing else |

Three consequences worth stating plainly:

- **The upgrade has no maximum.** `RaiseAlchemyLevel` is authored `maxLevel: -1`, so it is never
  maxed and never cap-blocked. A dial reading `264 / 264` says the *dial* is at its ceiling; it says
  nothing at all about whether the upgrade beside it can be bought.
- **The purchase's whole authored effect is the ceiling.** `RaiseAlchemyLevel` carries one permanent
  effect, `MaxAlchemyLevel +1`, and no effect on the dial. Whether the dial follows its ceiling on
  its own or has to be set is not in the authored data; the Casting dials have to be set by hand.
- **The two numbers are never the same number.** The upgrade's own level counts purchases made; the
  cap starts at 1 and the dial starts wherever it was left. Reading one as the other is the mistake
  the shape invites.

An attribute flagged as disabled has its **effect** killed, not its purchasability: it can still be
bought while contributing nothing.

## The frontier is small

The buy surface is enormous and the actionable part of it is tiny at any instant. E.g., on one
observed save with 409 purchasable entities, 224 were unavailable and roughly 181 were unaffordable,
leaving a handful of actually actionable rows.
