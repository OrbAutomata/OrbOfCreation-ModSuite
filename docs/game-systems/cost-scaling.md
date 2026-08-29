# Cost scaling

## Prices are mixed-currency by default

Two-currency prices are the norm, not an exception. E.g., across 409 purchasable entities on one
observed save, 82 % were priced in two currencies, 18 % in one, and none in three or more.

The two currencies in a single price are usually nowhere near each other in magnitude — a median
ratio around ×783 on that save, with a very long tail. **Comparing cost magnitudes across currencies
is meaningless**; the only comparable quantity is time-to-produce.

## The per-level multiplier

Each level's cost exceeds the previous level's by a factor clustering at **≈×1.25**, with a visible
group around ×1.30 and a real tail *below* 1.0 — a handful of entities actually get **cheaper** per
level. Which entities sit in that tail is unextracted; see [open-questions.md](open-questions.md).

The four `Raise <thing> Level` cap-raisers sit far outside that cluster: one observed level of
Raise Alchemy Level multiplied its own next price by **≈×267**, in both of its currencies at once.
See [attributes-and-upgrades.md](attributes-and-upgrades.md).

Costs span an enormous range: roughly `10⁻¹` through `10⁶⁰` on the observed mid-progression save,
and far beyond it late — endgame saves reach magnitudes on the order of `10⁵⁰⁰⁰`.

## Cost Scaling is retroactive

**Cost Scaling** is a statistic attached to a typed group: it is the per-level cost growth for
everything in that group, and its tooltip states plainly that it is retroactive. Reducing cost
scaling does not only make future levels cheaper — it re-prices the curve you are already on.

The same retroactivity shows up elsewhere in the purchase surface. E.g., *Thaumic Wisdom* adds +5
base Thaumaturgy capacity per Wisdom level and applies it across all existing Wisdom levels the
moment it completes.
