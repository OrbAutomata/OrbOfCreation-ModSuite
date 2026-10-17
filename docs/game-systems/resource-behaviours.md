# Resources with behaviours of their own

Beyond stock, rate and capacity, a resource can carry an **attached behaviour** — an extra effect
that belongs to that resource specifically and shows up in its tooltip. Observed shapes include a
natural drain, a missing-percent-driven fill, and effects on another statistic such as maximum mana.
Which resource carries what is recorded here as it is established, not inferred.

## Spark — decays toward an equilibrium

Spark loses **4 % of what you hold** every second (`lossPercent` 4), on top of its ordinary gain.
Its tooltip prints the balance point as **Equilibrium At** (e.g., `109` on one observed run with a
3.30/s raw rate and 147 % Gain). Above that point Spark falls back; below it, it
refills fastest when empty. With no passive Spark income the equilibrium is zero. Channel Spark
pours in far above it (observed +470/s), and the channel ends with Spark falling back toward the
equilibrium.

Held Spark also has its own effect: **+0.2 % Cantrip Cooldown Speed per Spark, soft-capped at
100 Spark**.

## Arcanum — fills on missing percent

Arcanum carries a similar attached behaviour driven by **missing %** (see
[growth-levers.md](growth-levers.md) for the mechanic). Its numbers are unextracted; see
[open-questions.md](open-questions.md).
