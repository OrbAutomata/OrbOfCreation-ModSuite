# Spell augments

Augments are the socketable modifiers attached to a spell. They are crafted at **Glyphcraft**, one
of the discovery surfaces (see [discovery.md](discovery.md)), and the **Augment Table** upgrade
raises your maximum number of augment slots by one each time it is bought.

Levelling an augment is itself gated: **Magic > Augments > Upgrade** exists only once the **Upgrade
Glyphs** upgrade is bought. Before that purchase no augment carries a level button of any kind, so an
early run has augments it can socket but none it can raise: a freshly discovered augment reads `1/1`
in the Loadout page's Augment Glyphs panel, one copy per spell and none free.

The augment catalogue is **22 glyphs** — every glyph whose type line carries a keyword beginning
`Spell ` (`Spell Augment` or `Spell Elemental`); see [vocabulary.md](vocabulary.md) for the test
that separates these from the pool unlockers sharing the word "glyph". They are Accursed, Blooming,
Bright, Dilated, Distinct, Efficient, Extended, Fortunate, Heavy, Important, Magnified, Precise,
Quick, Repeating, Resourceful, Scholar, Small, Strong, Surged, Weak, Weighted and Wrath.

Only the ones already unlocked appear on the **Magic > Augments > Upgrade** grid — observed at 9 of
the 22 on one endgame save, with the other 13 still locked. Where that grid exists, a glyph that is
unlocked but absent from it is an unlocker rather than an augment; the type line decides the same
question on any run, including before the grid is unlocked at all.

Augments are deliberately double-edged. Two worked examples; the remaining effects are unrecorded
(see [open-questions.md](open-questions.md)):

| Augment | Effect on the spell it is socketed into |
|---|---|
| Quick | ×1.15 cost, ×0.70 cooldown |
| Heavy | ×1.40 power, +10 % Special, +100 % cooldown, −10 % cast speed |

(**Special** multiplies the non-scaling multiplicative components of an effect, such as a charm's
×1.12 Magic Resources Gained row.)

## Slots belong to the spell

Augment slots belong to the **spell**, not to a shared board. What you configure is a per-spell
layout.

## Usable copies, not slots

The Augment Glyphs panel's "1/1" reading is a **usable-copy count**, not a slot count and not an
on/off state, and everything it counts is **per spell slot**. Each purchased augment level grants
**+1 Max Usage** and **+1 Free Usage per six levels**; e.g., an augment starting at Max Usage 1 and
Free Usage 0 has Max Usage `1 + L` and Free Usage `floor(L / 6)` at level `L`, absent outside
modifiers.

- **Max Usage** — how many copies of that augment one spell can socket.
- **Free Usage** — that many copies in the spell do not charge the augment's usage cost. Every
  non-free copy costs **one Spell Weight**, and copies are never merged into a single weight
  charge.

## Two level currencies, and some augments cost neither

An augment's level is bought on **Magic > Augments > Upgrade**, and two separate counters sit above
that grid: **Glyph Upgrades** for ordinary paid levels and **Free Glyph Upgrades** for bonus levels.
They are not interchangeable — a bonus level always costs 1 from the free counter regardless of what
the paid level would have cost. An augment's displayed level is the two added together, so an
augment reading `Lv 10` can be nine paid levels plus one bonus level.

Some augments' paid levels are **free outright** — the general rule is in
[cost-pipeline.md](cost-pipeline.md). Observed on Psionic, whose level rose 2 to 3 with both counters
unchanged. Take those first: they are pure gain and they compete for nothing.

Note the same augment shows two different numbers on two screens, and both are correct: the Upgrade
grid shows its **level**, while the Augment Glyphs panel shows **usable copies** (see above), which
is level plus the starting Max Usage. Quick at `Lv 10` reads `11/11` in the panel.

### The two panels print different numbers

Magic > Augments and Magic > Spellbook > Loadout both draw a glyph as `N/M` with a level beside it,
and the pairs are not the same fact.

- **Magic > Augments** draws the augment's **purchased level** and the usable copies above.
- **Magic > Spellbook > Loadout** draws **free usages of maximum** — `1/1` is one free copy out of a
  maximum of one — and a "Lv N" that counts the **copies staged into this layout**, floored at one.

A fresh augment the game already offers therefore sits at purchased level **0** while its Loadout row
reads "Lv 1". Nothing is owned at that point and nothing needs to be: what decides whether a glyph
can be socketed is whether the game offers a copy of it, and how many copies fit is its Max Usage. A
level is neither question's answer.

## The layout is chosen before the spell is added

1. Set the glyph layout for a spell first.
2. **Adding the spell to the loadout bakes those glyphs in** and raises its load cost accordingly.
3. Changing the glyphs on a loaded spell means remove the spell, change the layout, add it back —
   which re-pays that lane's cooldown downtime.

There is no in-place augment change.

## Socketed copies compound

Each socketed copy applies its factors to that spell, and multiple copies of the same augment
compound as powers: `q` copies of a ×1.15-cost, ×0.70-cooldown augment give ×`1.15^q` and ×`0.70^q`.
Socketing a non-free copy adds +1 weight to that spell's load cost, so load-cost growth after an add
is the glyph, not spell levelling.

## Levels grant global passives even unsocketed

Every **purchased level** of an augment applies its own global modifiers regardless of whether the
augment is socketed anywhere — e.g., ×1.04 Cantrip cooldown speed and ×0.94 Cantrip cost per level.
That splits an augment into two value streams: the socket effect (large, targeted, weighted,
double-edged) and the level passive (small, global, weightless, permanent for the run).

## Augments can change effective types

Glyph setup runs before a spell's types are established and can add or replace elemental tags, so a
tag-targeted buff resolves against the changed list; see [spell-types.md](spell-types.md).

## Paying for augment upgrades

Augment upgrading is bought with the **Glyph Upgrades** advancement currency rather than ordinary
resources, and the allocation is permanent until the next reset. See
[advancement-currencies.md](advancement-currencies.md).
