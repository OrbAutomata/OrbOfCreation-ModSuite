# Vocabulary

Every name the game uses for two different things, and every thing it gives two names.

## Attribute versus Statistic

| On screen | Purchasable? | What it is |
|---|---|---|
| **Attribute** | yes | The levelled thing with a cost line on a purchase list |
| **Statistic** | **never** | A value moved by attributes, upgrades, research and effects — Cost Scaling, Effect Levels, Spell Mastery Rate and friends |
| **Attribute category** | no | A grouping (Container, Aura, Talent, …) that effects can target |

When someone says "attribute" they almost always mean the purchasable levelled thing. Statistics are
never bought directly. Tooltip and requirement text occasionally leaks other names for both;
everywhere else, use the on-screen words.

## Glyph, Recipe Book, Augment

Three names, two systems:

| Name you will see | What it is | Where it appears |
|---|---|---|
| **Glyph** | A pool unlocker: a rollable enters the pool only once all of its glyphs are unlocked | Upgrade tooltips, the Glyph Discoveries tree |
| **Recipe Book** | The same thing | Item tooltips |
| **Spell Augment** / **Augment Glyph** | The socketable modifier you attach to a spell | Magic > Augments, the Augment Glyphs panel |

The reliable test is the **keyword on the glyph's own type line**: a keyword beginning `Spell `
(`Spell Augment`, `Spell Elemental`) marks a socketable augment; `Elemental`, `Forging`,
`Alchemical` and `Manifestation` mark a pool unlocker. Behaviour agrees — unlockers expand what you
can later roll, augments change a spell you already own — but the keyword decides it at a glance.
Observed split on one endgame save: 47 glyphs, 22 augments and 25 unlockers. See
[pool-unlockers.md](pool-unlockers.md) and [augments.md](augments.md).

## Concepts versus Alchemy

**Concepts** (a Scholar feature) and **Alchemy** (its own late screen, with Learn and Loadout pages)
are separate features arriving milestones apart. The Alchemy screen has nothing to do with Concepts.

## Divination versus Divining

The spell type is `Divination` in the data; every player-facing surface says Divining. Same type.

## The spell weight budget

One budget, three labels: the early Spellbook Loadout upgrade presents it as **Spell Power 2/3**, the
augment surfaces call it **Spell Capacity**, and the Spell Loadout page renders it as a **load bar**
with a load cost printed per spell.

## Advance

A concept's **advance** is one concept level: `25 s advance` means one level every 25 seconds.
