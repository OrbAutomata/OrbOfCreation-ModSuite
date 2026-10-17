# Advancement currencies

A progression tab level **grants advancement points** — one in each of the two currencies that tab
feeds. Points are never wasted by arriving while you are "full", so there is no urgency to spend one
before the next tab level lands.

**Code shape:** the level adds `+1` to the currency's **maximum quantity** and commits nothing, so a
currency reading `2/2` becomes `3/3`. The number on the counter is the free remainder — earned minus
committed — and the committed total is never drawn. See [allocations.md](allocations.md).

| Progression tab | Advancement points it grants, per level |
|---|---|
| Wizardry | +1 Magical, +1 Glyph |
| Scholar | +1 Cognitive, +1 Technology |
| Alchemy | +1 Cognitive, +1 Materials |
| Artificer | +1 Ability, +1 Artifact Upgrades |
| Construction | +1 Technology, +1 Artifact Upgrades |
| Druid | +1 Ability, +1 Magical |
| Mystic | +1 Technology, +1 Materials |
| Shaper | +1 Technology, +1 Glyph |
| Orb | +1 Orb |

Points also arrive **outside** that table: resource-type levels and various named upgrades feed the
same currencies — e.g., Technology Mastery grants +2 Technology, and Boost Glyphs +50 % to Glyph.
**Code shape:** those are modifiers on the maximum, so the effective cap is their folded result
rather than a stored integer.

## The supply is run-finite

There is no steady income. Advancement points come from tab levels, tab levels come from completed
attribute levels, and attribute costs grow faster than production does. At some point in every run
attributes become unaffordable, the tabs stop levelling, and the supply simply stops.

That makes the layer an **allocation problem with a hard budget**. Only a persistent reset puts the
cost curves back to zero; see [reset-and-ng-plus.md](reset-and-ng-plus.md).
