# Decision rules

The rules that survive a change of era. Everything else in this folder is an application of one of
them.

## Hidden-magnitude one-way doors

**When a decision is irreversible and its true value is hidden by design, buy information or defer.
Never score off the visible fraction** — the visible part is not a sample of the whole, it is the
part the game chose to show you.

Orb allocations are the canonical case: each level advertises a small buff while the dominant value
is the gates it opens later, and those thresholds are not displayed. A confident +8 read off the
visible buffs is really a +0.8 against what you cannot see. Hold the orbs, read the gates out of the
game's own data, then allocate.

**Free bonus levels carry extra option value — spend them last.** They do not advance the paid cost
curve, so holding one is free and spending one is irreversible. Spend a bonus level only against a
gate you can actually see, remembering that gates read purchased levels, so a visible bonus does not
satisfy one.

**A cap-raiser is one purchase, not a line.** The four `Raise <thing> Level` rows re-price
themselves by roughly ×267 per level, so the level you can afford is normally the only one you can
afford. Buy it when it comes into reach and re-plan from the new price rather than budgeting a run
of them.

## The watermark problem

**Cheapest-first never crosses a price watermark while cheap dribbles exist.** Early on, attributes
cost around 3 Knowledge, a spell level 10, the next system unlock 15; a greedy policy buys attribute
levels until they cost more than 15 before the unlock ever ranks, and by then the run is behind on
every track the unlock feeds.

Rank by effect on time-to-goal and let the goal's price set the watermark. Anything priced above the
cheap frontier must be evaluated, never filtered out for being unaffordable *now*.

## Find the binding constraint

**Name what actually limits the loop before valuing anything.** Filling 100 mana at 27/s takes 3.6 s
against a 7.35 s cooldown: that loop is cooldown-bound, so extra mana rate has *zero* marginal value
at that instant, however good the tooltip looks.

Efficiency multipliers win while the shared input binds; throughput adders win when spots or
cooldowns bind. **The answer flips as rates grow**, so re-check it inside an era, not only at
milestones.

## Commitment points

**Paying the roll is the commitment — the pick is already spent.** Deferral is only free *before*
paying.

- Defer the payment, never the pick. "Buy nothing this turn" is a real move; "leave it open" is not.
- **Delay optional pool draws until pool-unlocking purchases have landed.** A richer pool is a
  strictly better roll at the same price, and prices climb per tree with every optional pick. Land
  the unlockers, then roll.

## More base beats a multiplier on a small base

Under real scarcity, buy rate and flat base additions before percentage multipliers. Base additions
multiply through the entire modifier stack, so face-value ranking systematically undervalues them,
while a percentage of a tiny base stays tiny. The multiplier's turn comes when several sources feed
the term it targets.

## Cheap information compounds

**Purchases that only reveal state are near-automatic buys**: they cost a few units of a commodity
and improve every decision that follows for the rest of the run. Unlock purchases for a system you
are going to use anyway are the same class — milestones wearing a purchase's clothes.

## Buy is not equip

**Acquiring a spell and giving it a loadout spot are separate decisions against separate budgets.**
Buy now, bench it, socket it when weight capacity arrives. Acquisition windows close as pool prices
climb; loadout spots do not.

## Preferences are legitimate inputs

A run whose challenges were deliberately picked easy is a chill run, and that legitimately shifts
every fork toward permanent cross-run bonuses and away from run-local power. A hard run inverts it.
Neither run intent nor a taste for long-term bonuses appears anywhere in game data, so state the
posture once, up front, as a standing prior.
