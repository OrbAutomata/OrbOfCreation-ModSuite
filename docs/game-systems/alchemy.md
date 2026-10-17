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
- **Recipe slots bind separately from capacity.** The grid holds three different recipes
  (observed, early and late in a run), with all copies of one recipe in its one slot; a fourth
  recipe is refused with every slot in use even while Alchemical Capacity has room — swapping is
  then the only move.

Two recipe types seen early, observed per equipped copy:

| Type | Example | Effect |
|---|---|---|
| Transmute | Transmute Knowledge / Water / Space | +1.5e4 Knowledge/s, +50 Water/s or +2.5e4 Space/s, each with ×1.30 gain on that resource; 3 Alchemical Capacity |
| Dismantle | Channel Control | consumes Control for +4 Alchemical Capacity; 1 Mechanical Capacity |
| Dismantle | Extract Wood | −4.9e3 Wood/s for +5.38 Biotic Capacity (superior roll); 1 Mechanical Capacity |
| Refinement | Refine Organic | −325 Water/s for +0.379 Organic Essence/s; 3 Alchemical + 4 Biotic Capacity |
| Dismantle | Extract Magebloom | −0.076 Magebloom/s for +8 Occultic and +1.6 Biotic Capacity; 1 Mechanical Capacity |
| Refinement | Refine Occultic | −4.88e4 Thaumaturgy/s for +0.124 Occult Essence/s; 4 Alchemical + 4 Occultic Capacity |
| Transmute | Transmute Amber | −14.8 Control/s for +12 Amber/s (×1.30 Amber gain); 4 Biotic + 8 Occultic Capacity |
| Transmute | Transmute Ironwood | −4.37e4 Force Bark/s for +431 Ironwood/s (×1.30 Ironwood gain) at Alchemy Lv 5 under the requirement penalty; 10 Biotic + 10 Occultic Capacity (×2 penalized) |

Copies of one recipe scale faster than linearly: Refine Organic ×1, ×2, ×3 made 0.38, 0.85 and
1.59 Organic Essence/s, and Refine Occultic ×1, ×2 made 0.124 and 0.281 Occult Essence/s
(observed). A Dismantle recipe's capacity is functional: it holds only while the recipe is fed.
With Magebloom at zero, Extract Magebloom's Occultic Capacity lapses and Refine Occultic stops with
it, so Magebloom supply caps Occult Essence even though Refine Occultic itself drains only
Thaumaturgy. Alchemy Lv raises a Dismantle's drain but not its capacity (observed: Extract Magebloom
+8 Occultic for −0.076 Magebloom/s at Lv 1, −4.59/s for the same capacity at Lv 5).

Learn Biology (1e3 Alchemic Scroll + 5e8 Knowledge) opens Learn Refinement (5e4 Control), and the
two together put Extract Wood and Refine Organic in the roll pool. Organic Essence in turn opens
Grow Magic Herbs (the Magebloom unlock) and Imbue Organic. **Learn Occultic** (1e3 Magebloom + 1e4
Alchemic Scroll) makes Extract Magebloom and then Refine Occultic the next required rolls, and the
roll price switches to Magebloom (observed 300, then 100, beside a Skill cost). **Discover Amber**
(1e4 Occult + 9.09e3 Organic Essence) makes Transmute Amber the next required roll (observed at
2e4 Control).

A recipe whose `Req.` lines (Expert research levels) are unmet carries a **requirement penalty**:
observed ×2 capacity, ×0.25 speed and ×16 cost (Transmute Control, Brew Experience Elixir), which
can make a superior roll unequippable — Transmute Control asked 20 Alchemical Capacity. Brewing
recipes produce potions on a timer rather than a rate: Brew Mana Potion made one potion every 173 s
(+1.68e6 Mana/s for 4 s) for −20.8 Water/s, and reaching its mastery 1 opens Improved Brewing.

A Transmute is the first Water source that needs no spell. Alchemy rolls offer one more option than
the other trees (observed: three against two, then four against three after Greater Discovery).
The roll price climbs ×10 per roll, and a glyph purchase that widens the pool drops it back
(observed: 1e6 Control → 1e4 after Learn Refinement); the next rolls were required ones.

## Types, and the filter above the pool

The pool is filtered by a **type dropdown** and a text search. The types name what a recipe does:
**Brewing**, **Dismantle**, **Enchantment** and **Refinement** are the ones observed, and the list
scrolls past them.

**Brewing is an alchemy type, not a station.** Nothing on this screen is a brewing station, and
recipes whose names start with "Brew" are ordinary alchemy recipes.

## Alchemy Level

An `Alchemy Lv` row with −/+ and a **Raise Alchemy Level** purchase sits with the loadout; the
dial and its ceiling are described in [attributes-and-upgrades.md](attributes-and-upgrades.md).
Each level is ×2 Alchemy Speed and ×2 Alchemy Drain Cost (its tooltip), so a recipe's output
doubles while its inputs go ×4: every level halves the yield per input (observed Lv 4 → 5:
Occult Essence 1.18 → 2.37/s, Magebloom drain 1.16 → 4.63/s). Its price is paid in Alchemic
Scroll and climbs steeply: 100, 330, 1.1e3, 3.9e3, 1.4e4 for Lv 2 to 6 (observed).
How the level interacts with the six capacities is unrecorded; see
[open-questions.md](open-questions.md).
