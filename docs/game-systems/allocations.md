# Allocations displayed as resources

Advancement points and their kin play as **a number you gain and spend**: a tab levels, you get a
point, you put it into a node. Two things stand out at the counter, though: it reads like a stock
with a cap (`2/3`), yet sitting "full" never wastes anything, and `0/2` shows you empty while you
are in fact fully invested.

**Code shape:** these are budgets, not stocks, and the number on the counter is derived rather than
stored. The cap is the total you have earned, the stored quantity is what you have already
committed, and the counter draws the difference — what is still free. A level-up raises the cap and
commits nothing, so `2/2` becomes `3/3`; a counter reading `50/80` means 80 earned with 30
committed, and earning one more point makes it `51/81`. That is why `0/2` looks empty when you are
fully invested: the free number is on screen, the committed one never is.

The same quantity/cap counter covers the game's other budgets — Spell Capacity, plot capacity,
Alchemical Capacity and more, 25 in all — but a shared counter is not shared behaviour. Those
budgets hold a standing usage that comes back in full when you take the thing out; an equipped
alchemy recipe returns its Alchemical Capacity the moment it is unequipped (see
[alchemy.md](alchemy.md)). An advancement point does not come back.

Whichever shape you read, advancement allocations are permanent within a run: no refund, no respec,
and no moving a point from one node to another.
