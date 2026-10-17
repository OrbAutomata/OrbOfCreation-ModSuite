# Resources with behaviours of their own

Beyond stock, rate and capacity, a resource can carry an **attached behaviour** — an extra effect
that belongs to that resource specifically and shows up in its tooltip. Observed shapes include a
natural drain, a missing-percent-driven fill, and effects on another statistic such as maximum mana.
Which resource carries what is recorded here as it is established, not inferred.

## Spark — decays toward an equilibrium

Spark loses a fixed **percentage of what you hold** every second (observed 4 %/s), on top of its
ordinary gain. Its tooltip prints the result as **Equilibrium At**: gain equals loss at
`raw rate × Gain % ÷ Loss %`. E.g., one observed run had a raw rate of 3.30/s and 147 % Gain against
4 %/s Loss, so its tooltip read Equilibrium At 109. Above that point Spark falls back; below it, it
refills fastest when empty. With no passive Spark income the equilibrium is zero. Channel Spark
pours in far above it (observed +470/s), and the channel ends with Spark falling back toward the
equilibrium.

Held Spark also has its own effect: **+0.2 % Cantrip Cooldown Speed per Spark, soft-capped at
100 Spark**.

## Arcanum — fills on missing percent

Arcanum carries a similar attached behaviour driven by **missing %** (see
[growth-levers.md](growth-levers.md) for the mechanic). Its numbers are unextracted; see
[open-questions.md](open-questions.md).
