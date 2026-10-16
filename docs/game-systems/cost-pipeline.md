# The cost pipeline

Costs are built in stages, and the stages are not interchangeable. A spell's cost is assembled as:

1. **Base cost** — the authored price.
2. **Glyph augmentation and conversion** — socketed augments modify or replace cost terms.
3. **Per-level scaling** — the entity's own level curve.
4. **Cost modifiers** — entity-specific, global and type modifiers, folded by the rules in
   [modifiers.md](modifiers.md).
5. **Percentage multiplication** — the remaining percentage terms.
6. **Rounding to two significant digits** — the displayed price.

There is no shortcut that merges modifiers from two stages into one bag. A +10 % at stage 4 and a
+10 % at stage 5 neither add nor commute.

## A ladder can start at zero, and those levels are free

Where a price ladder starts at zero, the levels it covers cost nothing at all: taking one spends no
resource and moves no counter, including the advancement counter the paid levels of that same thing
draw from. Two shapes on screen mean the same thing — a level that names no price, and a level whose
price row reads `0` of a currency. Anything with levels can be authored this way.

## Quality divides at payment

At the moment you pay, **actual spend = displayed cost ÷ the paying resource's Quality**. Quality
never appears in the price you see, only in how much leaves your stock.

Payment works from raw quantities in the authored order of the cost rows, applies Quality, and clamps
at zero. Displayed price × quantity is a good estimate and a bad prediction; when reconciling stock
against a price, reconcile against what actually moved.

## Attributes and upgrades share no cost chain

The two purchase families run separate pipelines. Attribute prices additionally pass through the
paying resource's Attribute Cost term; see [growth-terms.md](growth-terms.md).

Costs are charged **at queue time** and each queued level is priced individually, so a two-level bulk
purchase is the displayed price plus the next level's higher one, not twice the display.
