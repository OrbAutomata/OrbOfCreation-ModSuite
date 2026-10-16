# Spell types

Spells carry **types** (the game also calls them keywords), and effects target those types. The
authored taxonomy is fifteen entries:

> Arcane, Corporeal, Dragon, Druidic, Expansion, Flow, Primary, Psionic, Storm, Alteration, Cantrip,
> Charm, Conjuration, Divination, Evocation.

**Divination is displayed as "Divining".** The tag is `Divination` in the data; every player-facing
surface says Divining. They are the same type.

## Types carry a level, and casting is what raises it

**Magic > Spellbook > Spell Types** lists your types with a level and an XP bar each — e.g. `Expansion Lv 0`
over `240/300`. That level is **not for sale and not directly raisable**: it rises as a consequence
of mastery-levelling the spells carrying the type, so the way to raise a type is to cast the spells
that wear it (see [spell-mastery.md](spell-mastery.md)). A type's level then applies on top of
whatever wears it, so it lifts every spell of that type at once.

This is the only level in the glyph/Recipe Book/type family. Recipe Books have no level at all;
augments do, and theirs is bought on a grid of its own (see [augments.md](augments.md)).

## Effective types, not printed names

A spell starts from its authored tags, and glyph setup can add or replace elemental tags before the
spell's types are established. Every tag-targeted buff — "all Cantrips", "Divining Spell Power",
"Psionic power" — resolves against the spell's **effective** type list at runtime. To know whether a
buff hits a spell, read the spell's current types, not its display name. See
[augments.md](augments.md).

## Usage conventions

Divining spells tend to produce Mental and Magical resources, Cantrips are the quickfire workhorses,
and Charms are temporary buffs you toggle to create a burst window.
