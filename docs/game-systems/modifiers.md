# Modifiers

Every number in the game — a rate, a cost, a capacity, a cooldown, a spell's power — is a base value
with modifiers folded onto it. The same machinery runs everywhere.

## The five kinds

| Kind | What it does to the current value `v` | How several of the same kind combine |
|---|---|---|
| Raw | `v + a` | adjustments add |
| MultiDiminishing | `v × (1 + a)` | adjustments **add**, then one multiply |
| MultiStacking | `v × f` | factors **multiply** |
| Reduction | `v / (1 + a)` | adjustments add into one denominator |
| Exponent | `v ^ e` (an exponent below 1 is applied as its inverse) | exponents multiply |

Tooltips do not name the kind, and the kind decides the arithmetic: two "+12 %" bonuses are +24 %
under MultiDiminishing and ×1.2544 under MultiStacking.

MultiDiminishing is why percentage bonuses feel weaker than expected — ten sources of +10 % are
+100 %, not ×2.59. MultiStacking is the compounding kind and is comparatively rare; the per-level
cost growth on Attributes is one of the few places it appears.

## Fold order

Modifiers carry an order number. Within one order the game folds in kind sequence:

```
Raw → MultiDiminishing → MultiStacking → Reduction → Exponent
```

and then folds orders ascending. This is not commutative: a Raw addition placed in a later order
lands on top of the multipliers instead of underneath them. A tooltip listing contributing effects
shows the contributions, not the sequence.

## What a type's own total is, and why you must not multiply it in

A keyword is a type asset, and a type asset carries modifier records of its own. "All Cantrips +10 %"
lands on the Cantrip type, not on each Cantrip.

For almost every keyword, the type then *hands the bonus down*. The moment a modifier lands, the type
pushes a transformed copy into every member registered with it, so each member's own record already
carries it. The type's record keeps no value of its own; what its tooltip prints is that record
applied to 100 — a percentage, or the same number written as a multiplier.

**So a type's total and a member's number are one bonus written twice, not two things to multiply.**
Reading "Workshop structures +80 %" beside a Workshop whose power already reads 450 and concluding
810 is the mistake this section exists to prevent — the 450 is already the +80 %.

Two facts complicate the obvious reading of "which things does this bonus cover":

- **Structure types can nest.** A bonus on a parent type reaches the members of every type below it,
  so the covered set is the whole chain and not the parent's own list. The one authored chain is
  Primal → Arcanist, Flameweaver, Stormshaper.
- **Spell types are the exception, and they multiply.** Spell types hand nothing down. A spell's
  power is multiplied by the product of every type it currently resonates with — and that set is
  rewritten by the glyphs in the spell, so it is not the list the recipe was authored with. Here,
  and only here, the type's number really is a separate factor.

## Same-kind addition, worked

MultiDiminishing adjustments add. E.g., Investment Power tiers on a time rune read +12 % each; with
three tiers a base pick of 225 previews as 306:

```
225 × (1 + 0.12 + 0.12 + 0.12) = 306      if it multiplied: 225 × 1.12³ = 316.1
```

A stacking claim requires knowing the modifier's kind, and nothing in the display states it.
