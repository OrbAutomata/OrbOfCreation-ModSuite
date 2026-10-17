# The run opening

## Spend the carry early

**Apply carried time bonuses as early as the game allows.** They multiply everything downstream, and
the opening minute's rates set the cadence for the whole early game. The reset screen publishes the
shape of the carry before you commit to anything.

You cannot literally spend it first, though. **After a reset every time rune is undiscovered, so
the Time Advancements have nowhere to go** — the bar reads full because none of it is allocated,
not because it is waiting for you. The first rune costs `100` mana, so mana buys the *right* to
allocate the carry. Take step 1 far enough to afford Runecraft, then spend.

## The early chain, as a reference walk

The critical path is deterministic: required rolls are rigged to a single option, so the spine is the
same every run and only optional pool picks are genuinely open. **Treat this as a route, not a
script** — prices move with the carried bonuses.

The first minutes are narrower still: straight after a reset the upgrade list offers exactly one
buyable entry (Infuse Orb, observed), everything else being re-gated rather than merely zeroed (see
[reset-and-ng-plus.md](../game-systems/reset-and-ng-plus.md)). There is no shopping decision at the
opening — do not spend time surveying the list, go to step 1.

1. **Mana only.** Infuse the orb repeatedly. The cost doubles against a fixed capacity until it
   cannot be paid — that is the game forcing the next step, not a wall to beat.
2. **First time rune** (always the Discovery-rarity one) — this opens the cross-run layer.
3. **First glyph** — the first pool unlocker; option space, not rate.
4. **Spellcraft** → first spell roll → the first cast-driven resource. Casting is now a production
   engine, not a button.
5. **First attributes tab** → a capacity attribute. This is the detour that un-gates the infusion
   ladder.
6. **Second glyph → a charm → the loadout upgrade** (spots plus a weight budget).
7. **Spell advancement** → spell levels, which raise both output per cast and XP per cast.
8. **Novice Spells** — the first gate you can neither buy nor save for: it wants spell mastery, and
   only casting produces mastery.
9. **Second produced resource** → the augment table and augments → the global output level dial →
   more spell spots.
10. **Scholarism** → the third resource, on a trickle-rate economy → research, via Innovation.
    Scholarism itself wants exactly two things: **mastery level 2 on your first spell** and
    **Output Lv 2**. The second is the slow half — Output Lv is capped by Max Output Lv, which
    starts at 1 and is raised only after the `Spell Output Level` upgrade is bought, and that
    upgrade is priced in **Thaumaturgy**, the resource step 9's second producer makes, and locked
    until Casting Level 2. So the Scholarism clock is really a race to a second production spell,
    not to a pile of Knowledge. Raise Output before grinding mastery 2, not after: at Output 2 one
    Gather Knowledge cast paid ~560 mastery XP, so the 7.12e3 bar took a dozen casts.
    Once bought, Scholarism opens the Scholar screen, and the trickle economy is one short chain:
    **Novice Study** (a timed Scholar upgrade) → **Focus Aura** levels for Psi → **Learn Psionic**
    at a handful of Psi widens the spell pool to Psychic Blast, the Mental-income charm →
    **Mentality** for Knowledge and Psi capacity, which the next spell roll's price already exceeds.
11. **Storm and the first channelled spell** → orb research → **Concepts**.

Each step's currency is the next step's price. Skipping ahead means saving in a currency you cannot
yet produce.

## Plan through cap gates, not into them

A price above capacity is gated, not slow, so a plan that only tracks stock will chase an unreachable
price for an entire interval. The resolution is always the same multi-hop detour, and it runs several
times in the first hour:

> unlock the pool → take the spell → produce the new currency → buy the capacity attribute → the
> original purchase becomes reachable.

**When a goal is cap-gated, make "raise the cap" the goal** and price it in whatever currency the
capacity attribute wants. The detour is not a distraction from the plan; it *is* the plan.
Requirement chains behave the same way — plan through the gate rather than saving into it.

## A new currency's source is the next marked roll

When a gate asks for a resource you have never produced, don't hunt the attribute lists for it.
Look for the exclamation marks on the screen strip. The tree that owes a required component has
usually just moved its roll price into the newest currency, and its one card is the producer. E.g.,
the World Pillar opened Agromancy, whose chop wants Skill. The spell roll then cost Verdant Energy,
the currency Conjure Life had just started making, and dealt Construct, the Skill spell.
