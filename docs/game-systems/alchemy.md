# Alchemy

**Alchemy > Alchemy** carries a **Learn | Loadout** split. Learn is where recipes are acquired;
Loadout is where the ones you own are put to work.

Learn is the alchemy discovery tree's offer page: an **Alchemize** button carrying a two-resource
price above a type-filter row, an offer area and a Confirm button; see [discovery.md](discovery.md).

## The loadout is an allocation, not a purchase

The Loadout view is an equipped grid on the left and a pool of learned recipe instances on the
right, under a row of six capacity counters.

Equipping an instance is a **standing usage** against one of those capacities, held for as long as
it stays equipped and returned in full when it is removed — observed: one Enchant Spirit instance
charges 11 Alchemical Capacity, and removing it gives all 11 back. It is the same
quantity/cap shape as every other allocation; see [allocations.md](allocations.md).

- **The same recipe can be equipped several times.** The loadout tracks an active count per recipe
  rather than a slotted/not-slotted flag, and each copy costs its usage again.
- **Slots are ordered, and moving inserts rather than swaps.** Dropping a recipe onto an occupied
  position pushes that recipe and everything after it one place along; nothing is displaced out of
  the grid.
- **Slots are typed.** A full grid refuses a recipe for having "no compatible open slot" — so
  whether the loadout is full is a question per recipe, not per grid.
- **Recipe slots bind separately from capacity.** Early in a run the grid holds three different
  recipes (observed), and a fourth is refused with every slot in use even while Alchemical Capacity
  has room — swapping is then the only move.

Two recipe types seen early, observed per equipped copy:

| Type | Example | Effect |
|---|---|---|
| Transmute | Transmute Knowledge / Water / Space | +1.5e4 Knowledge/s, +50 Water/s or +2.5e4 Space/s, each with ×1.30 gain on that resource; 3 Alchemical Capacity |
| Dismantle | Channel Control | consumes Control for +4 Alchemical Capacity; 1 Mechanical Capacity |

A recipe whose `Req.` lines (Expert research levels) are unmet carries a **requirement penalty**:
observed ×2 capacity, ×0.25 speed and ×16 cost (Transmute Control, Brew Experience Elixir), which
can make a superior roll unequippable — Transmute Control asked 20 Alchemical Capacity. Brewing
recipes produce potions on a timer rather than a rate: Brew Mana Potion made one potion every 173 s
(+1.68e6 Mana/s for 4 s) for −20.8 Water/s, and reaching its mastery 1 opens Improved Brewing.
Raise Alchemy Level 1 → 2 took Water income from +595/s to +4.1e3/s (observed).

A Transmute is the first Water source that needs no spell. Alchemy rolls offer one more option than
the other trees (observed: three against two, then four against three after Greater Discovery).

## Types, and the filter above the pool

The pool is filtered by a **type dropdown** and a text search. The types name what a recipe does:
**Brewing**, **Dismantle**, **Enchantment** and **Refinement** are the ones observed, and the list
scrolls past them.

**Brewing is an alchemy type, not a station.** Nothing on this screen is a brewing station, and
recipes whose names start with "Brew" are ordinary alchemy recipes.

## Alchemy Level

An `Alchemy Lv` row with −/+ and a **Raise Alchemy Level** purchase sits with the loadout; the −/+
adjuster is drawn only once **Level Alchemy** is bought. The dial and its ceiling are described in
[attributes-and-upgrades.md](attributes-and-upgrades.md).
How the level interacts with the six capacities is unrecorded; see
[open-questions.md](open-questions.md).
