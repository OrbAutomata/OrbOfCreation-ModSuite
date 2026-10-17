# Time runes

Runes are acquired through **Runecraft** (Time > Time Runes > Create), one of the seven discovery
trees (see [discovery.md](discovery.md)). The first rune of a run costs `100` mana
and is always the Discovery-rarity one. Across a full run you can expect roughly **7 to 10 runes**.

## Three level systems per rune

Each rune carries **three separate level numbers**, and they mean different things.

| Track | Bought with | Cost curve | What it does |
|---|---|---|---|
| **Rune level** | Time Advancements, this run only | `0, 1, 2, 3, 4, …` | Grants persist XP and raises what future picks of that rune give (`+25 %` per level) |
| **Persist level** | Persist XP, across runs | Escalating thresholds: `100`, `250`, `400`, … | Where the permanent effects actually live |
| **Mastery level** | Mastery XP, about `1.08` per rune level bought | Escalating | Each mastery level grants `+1` starting Time Advancement next run |

A rune level costs nothing while your free usages exceed the rune's level — the zero-start rule of
[cost-pipeline.md](cost-pipeline.md), reached here by free usages. Past that the curve is evaluated
at `level + 1 − free usages`, which is what produces the `0, 1, 2, 3, 4` sequence.

- A rune left at **level 0 is inert** — it grants no persist XP at all. The free first level is what
  switches a rune on. A **Superior** rune gets a second free level.
- Persist XP **rolls over**: overshooting a threshold is not wasted.
- E.g., picking an Investment rune grants `100` persist XP by default, and levelling the rune raises
  the XP granted per pick for the rest of that run.
- All three tracks have diminishing returns, and rune levels get more expensive as you buy them.

## Only Persistent runes are cross-run capital

Runes carry a **Persistent** tag: the Investment family has it, while Tempo and Scaling variants do
not. Runes without the tag are run-local — they count towards no persistent-rune rule and leave
nothing behind at reset.

## Persist effects survive without repicking

On a persistent reset the **rune itself is cleared** — its level, its discovery and its discovery
rarity are all wiped, so the rune is not still picked in the new run. What survives is the
**persistent advancement levels the rune already granted**, and those keep applying whether or not
you ever pick that rune again. Repicking is therefore only needed to buy *more* levels and bank
*more* persist XP.

## Meta-runes

Some runes buff whole *classes* of runes rather than a game system. E.g., "Persist Persist" grants
`+12 %` Investment Power, which multiplies its own XP gain because it is itself an Investment rune.
Rune effects can be **reflexive** (a rune affecting itself) and **retroactive** (a class multiplier
applying to a rune of that class picked earlier in the run).

Investment Power stacks additively: `+12 %` per persist level, so three levels turn a base `225` pick
into exactly `306`, not `316` (see [modifiers.md](modifiers.md)). A rune's XP per pick is the base
pick XP times its `+25 %`-per-rune-level bonus times its Investment Power. Rune mastery XP accrues in
fractions and the bar is per rune. E.g., a first mastery level needed `4` XP (reached at rune
Lv 4), and Discovery Rarity's bars for mastery 4 and 5 read `10` and `12`.

## Discovery Rarity

The first rune's levels raise **Discovery Rarity Rating**, the chance for *any* discovery roll to
come out **Superior**, with stronger properties than the plain version. Level `L` adds
`2 + 0.5 (L − 1)` base rating, times a small multiplier (×1.02 observed). The chance bends below
linear and caps at `30 %` (e.g., rating `36.7` → `17.0 %`). Higher tiers open at fixed ratings:
**x2 at `36.2`, x3 at `84.5`**. Rarity is fixed when the roll is paid, and a reroll keeps it.
