# Game systems

How Orb of Creation works: mechanics, math, and the gaps. Play advice lives in
[../strategy/](../strategy/README.md).

All constants are read from game version 1.0.5 and are invalidated by a game update.

## Numbers

- [modifiers.md](modifiers.md) — five kinds of modifier, folded in a fixed non-commutative order.
- [per-level-scaling.md](per-level-scaling.md) — whether an effect's levels add or multiply is a
  property of that effect.
- [numbers-and-rounding.md](numbers-and-rounding.md) — how the screen spells a number,
  mantissa/exponent storage, two-significant-digit prices, spend-to-zero.
- [cost-pipeline.md](cost-pipeline.md) — six stages build a price; Quality divides it again at
  payment.
- [lazy-evaluation.md](lazy-evaluation.md) — a value is worked out when something reads it, so
  displays lag.

## Resources

- [growth-terms.md](growth-terms.md) — Rate, Gained, Capacity, Quality, Attribute Cost, Reverb,
  Replenish, Decay.
- [growth-levers.md](growth-levers.md) — missing-% pays you for being empty, interest for being full,
  rest for not transacting.
- [capacity.md](capacity.md) — a price above your cap is unreachable, not slow; resolving it is a
  cross-currency detour.
- [overcap.md](overcap.md) — a one-shot 3 s timer, then a rubber band pulling the excess back to cap.
- [resource-behaviours.md](resource-behaviours.md) — resources with attached effects of their own:
  Spark drains to zero, Arcanum fills on missing %.
- [splash.md](splash.md) — type-targeted gain split by lifetime production rate; it feeds the rich and
  does not conserve.
- [resource-types.md](resource-types.md) — every resource carries type keywords, and effects target
  keywords, not resources.
- [allocations.md](allocations.md) — advancement points, spell capacity and the like: quantity/cap
  reads unspent/earned.
- [emblems.md](emblems.md) — passive token effects; Momentum is the one worked out.

## Casting

- [spells.md](spells.md) — a spell is a cost, a cooldown and an effect list that other purchases can
  edit; three numbers describe that cooldown.
- [spell-types.md](spell-types.md) — fifteen tags; buffs resolve against effective types, not the
  printed name.
- [effect-grammar.md](effect-grammar.md) — every effect is term + statistic + keyword-target.
- [spell-loadout.md](spell-loadout.md) — spots and weight bind independently; a weight-0 spell still
  costs a lane.
- [cast-modes.md](cast-modes.md) — cantrips fire once, charms open buff windows, channels block
  casting and drain, charging trades time for power.
- [output-and-reserve.md](output-and-reserve.md) — two global dials: Output prices the active cast,
  Reserve prices the passive economy.
- [casting-level.md](casting-level.md) — a global track fed by every cast; the displayed +1/s may
  be a windowed average.
- [spell-mastery.md](spell-mastery.md) — per-spell XP that only casting fills, plus the three
  spell-type tracks.
- [spell-levels.md](spell-levels.md) — cost curve 3.3^(L(1+0.007L)); levelling raises output and XP
  together.

## Augments

- [pool-unlockers.md](pool-unlockers.md) — glyphs, a.k.a. recipe books, expand what future rolls can
  contain.
- [augments.md](augments.md) — per-spell-slot sockets and usable copies, weight per copy, and
  global per-level passives.

## Discovery

- [discovery.md](discovery.md) — seven trees, one buy-the-next-thing mechanism; paying is the
  commitment.
- [discovery-pricing.md](discovery-pricing.md) — each tree has its own ladder, advanced only by
  optional picks.

## Purchases

- [attributes-and-upgrades.md](attributes-and-upgrades.md) — the two purchase families, their
  asymmetries, and category migration.
- [development-queue.md](development-queue.md) — buying queues; the queue, not the resource, is the
  early binder.
- [cost-scaling.md](cost-scaling.md) — ≈×1.25 per level, two-currency prices, and retroactive cost
  scaling.
- [auto-buy.md](auto-buy.md) — the native sweeper fires only on empty queue, 5 s idle, and <0.1 % of
  stock.

## Progression

- [tab-and-orb-xp.md](tab-and-orb-xp.md) — every completed attribute level pays +1 tab XP and +1 Orb
  XP.
- [advancement-currencies.md](advancement-currencies.md) — which tabs grant which points, and why
  the supply is run-finite.
- [research.md](research.md) — timed nodes priced in school points, revealing further nodes on
  completion.
- [orbs.md](orbs.md) — one orb per global level, spent on disciplines that gate later content.

## Systems

- [concepts.md](concepts.md) — passive scalers; only a concept in a development slot levels or
  drains.
- [agromancy.md](agromancy.md) — plots, nodes and growth phases; growth runs with the screen closed.
- [aspects.md](aspects.md) — three pedestals through which Workshop, Alchemy and Rituals arrive.
- [disciplines.md](disciplines.md) — four disciplines change name between purchase page and
  advancement tab; several are unplayed.
- [crafting.md](crafting.md) — Workshop recipe rows; crafts pay at submission.
- [scribing.md](scribing.md) — the Scribe drop slot and its Starting Level dial; largely unmapped.
- [artifacts.md](artifacts.md) — an equipment loadout bound by weight and slots at once.
- [consumables.md](consumables.md) — family tags (partly unmapped), preparation times, replenish
  research, and scroll targeting.
- [carry-limits.md](carry-limits.md) — per-item capacity; a weaker arrival is paid for and silently
  discarded.
- [toxicity.md](toxicity.md) — the meter that rate-limits item usage and drains faster the fuller it
  is.

## Time and prestige

- [time-runes.md](time-runes.md) — rune level, persist level and mastery level are three different
  tracks.
- [time-advancements.md](time-advancements.md) — refunded every reset, so the only question is the
  split.
- [achievement-strength.md](achievement-strength.md) — +1 % all resource gain and +1 starting Time
  Advancement per point.
- [challenges.md](challenges.md) — picked at NG+ start, cleared by resets; they modify
  requirements, not only numbers.
- [reset-and-ng-plus.md](reset-and-ng-plus.md) — achievements and TA survive, challenges get
  cleared; only NG+ resets cost curves.

## Interface

- [screens.md](screens.md) — seven screens, their subtabs, and which page holds what.
- [ui-behaviours.md](ui-behaviours.md) — wheel ownership, nested tooltips, red vs grey.
- [vocabulary.md](vocabulary.md) — attribute/statistic, glyph/recipe book/augment, concepts/alchemy.

## Gaps

- [open-questions.md](open-questions.md) — known-unknown mechanics, each with what would settle it.
- [unmapped-systems.md](unmapped-systems.md) — Rituals, Alchemy, Zeal, Dimensional and the other
  unplayed features.
