# The resource lifecycle

Every resource walks the same path: **scarce, then frontier, then commodity, then meaningless.** At
the start of a run nothing is a commodity, so everything must be saved. Then a milestone lands, and
the milestone is not just an unlock — **it re-partitions the whole resource set.** Yesterday's
frontier drifts to commodity; the newly unlocked system's currency becomes the new frontier.

**Re-derive the partition at every milestone.** A policy attached to a resource goes stale the moment
that resource changes class, and a stale save-stance is indistinguishable from a deliberate one until
the run is already slow.

Two edges are easy to get wrong:

- **Commodities are not free.** You still prioritise among them, because the goal is time, not
  affordability. A commodity that shortens the clock outranks one that does not.
- **"Meaningless" is a real class.** A stock many orders of magnitude away from any price it could
  pay contributes nothing however large it looks. When a target sits 67 orders of magnitude away,
  saving is not a strategy — spend it on anything that gains any benefit at all.

## Which budgets come back, and which do not

Advancement points, spell capacity and spots, plot quantity, the development queue and Time
Advancements all draw as a quantity/cap counter rather than a stock. A shared counter is not shared
behaviour, and the difference decides whether a decision can be taken back.

**Most of them come back.** Spell capacity and spots free up when you evict a spell, alchemy and
artifact loadout capacity returns in full when you unequip, plot quantity regrows on its own, a
development slot clears when its purchase lands, and cancelling a running development releases the
cancelled level's points. Time Advancements are refunded by every world reset, so within a run they
are a question of where they sit and never of whether to consume them. These are **portfolio
problems** (how do I split what I hold), not spend-timing problems: nothing is wasted at cap,
deferring is free, and a bad split is re-split rather than paid for twice.

**Advancement points do not.** Glyph, Artifact, Materials and the five school points are committed
for the rest of the run the moment a level lands — no refund, no respec, no moving a point from one
node to another ([allocations.md](../game-systems/allocations.md)). The supply is run-finite:
attributes go unaffordable, the tabs stop levelling, and the points stop arriving
([advancement-currencies.md](../game-systems/advancement-currencies.md)). So these are spend
decisions after all — you *will* run out, and every level you take is one some other node never
gets. Only half the portfolio reading survives: an uncommitted point is never wasted, so waiting
until you know the best node is free. Committing it is not.

The development queue serializes everything, so ordering matters even in abundance and headroom must
be left rather than filled to the brim.
