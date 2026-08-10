# Open questions

Each entry is a gap and what would settle it. When one is answered, the answer belongs on the page
that owns the topic and the entry here is deleted.

Two caveats apply to every page in this folder rather than to any single gap: all constants are read
from game version 1.0.5 and a patch invalidates them, and rates observed early in a run may include
carried cross-run bonuses (authored constants do not).

## Resources

- **Reverb Rate versus Replenish Ratio** — which term does which job, and whether they are even the
  same kind of mechanism. Both are rare and neither has ever driven a decision. Settle from the
  resource model's fields; play will not separate them.
- **Which resources bear Interest** — how common interest is, whether it is authored per resource or
  granted by upgrades, and when in a run it starts mattering. Soul Shards is the only observed
  carrier. Settle from the authored resource set.
- **Whether interest saturates against a cap** — the observed carrier had no Capacity at all.
- **Missing-percent fill details** — whether the fill reads effective or base capacity, the tick
  cadence behind "/ min", and which effects carry the term beyond food fruits. Settle with one
  before/after against a known capacity multiplier.
- **Which resources carry a resting rate**, and the exact shape of the acceleration.
- **The Decay Ratio mapping** — how the displayed ratio maps onto the overcap loss formula.
- **The Attribute Cost term** — when it changes and whether it is ever a decision. Settle with a
  nested tooltip on a resource where it is nonzero.
- **Overcap outside advancement resources** — the loss constants were measured on advancement
  resources, and whether ordinary resources share them is unestablished. So is whether a purely
  rate-fed resource can overcap at all: none has been observed doing so, but no rule forbidding it
  has been found. Settle with one observed overcap decay on a normal resource.
- **Per-resource behaviour formulas** — Spark's drain toward zero (no coefficients, no equilibrium
  function, no confirmation of the channel exemption) and Arcanum's missing-% fill (no numbers at
  all); see [resource-behaviours.md](resource-behaviours.md).
- **Aura effect-level magnitudes** — the rough ≈×1.7–1.8 estimate for a stack of +12 aura effect
  levels was never confirmed. Settle by capturing an affected rate before and after a single
  effect-level purchase with nothing else changing.

## Casting and augments

- **The Output Level curve** — the per-level cost and power exponents. The one available measurement
  had attribute purchases interleaved with it and is a single point on a dial that runs past 50.
  Settle by stepping the dial one level at a time and recording cost, output and cooldown.
- **The Reserve Level exponent** — whether the per-level factor is applied at the level or level − 1.
- **The augment catalogue** — which augments exist beyond the two worked examples, what each costs in
  weight, and how many copies are available at a given point.
- **The emblem catalogue** — 24 emblem passives exist and one is worked out.
- **Whether a Momentum build is viable** — Momentum and the standard cantrip charm cannot both be
  equipped at observed weight caps, and the pairing with its feeder spell was never tested. Settle by
  running both for a measured window and comparing output.
- **Spell levelling prerequisites** — spell recipes carry levelling prerequisites that the levelling
  button does not consult. Whether they are enforced anywhere a player can hit is unknown.

## Discovery

- **Offer tables and rarities** — what each tree can actually produce, the rarity levels attached to
  offers, and how multi-resource roll prices are composed.
- **The deep-ladder scaling law** — the per-step factor grows with the count, and the formula has not
  been extracted.

## Progression and requirements

- **Challenge requirement adjustments** — the underlying requirement-adjustment value read `0` on a
  node correctly displaying `leeway 5`, so either the display is fed from elsewhere or a second
  mechanism is involved. It decides whether challenge modifiers can be reasoned about before
  activating them. Confirm with a second challenge touching a different node.
- **The challenge catalogue** — what challenges exist, what each does, how difficulty is expressed,
  what rerolling a set costs, and what passing one grants.
- **Whether a purchase can cost three or more resources** — nothing in one full snapshot of priced
  entities exceeded two currencies. Settle across the full authored set, not one save.
- **Which entities get cheaper per level** — the per-level cost-factor census shows a real tail
  below 1.0; which entities sit in it, and whether the shrinking is authored or a modifier
  artifact, is unextracted.

## World and crafting

- **The Scribe's roles** — six scribe recipes are registered against eight authored scribe roles,
  while a live inventory held scroll types for two of the roles that have no recipe. Either those
  arrive from a non-Scribe source or the registry reading is incomplete.
- **Agromancy remaining-instance counts** — the count is computed against idle nodes and is allowed
  to go negative; the player-visible consequence is unrecorded.
- **The six Alchemy pools** — what they represent, what an alchemy effect does, and how the Alchemy
  Level dial interacts with either. The dial's authored shape is written up in
  [attributes-and-upgrades.md](attributes-and-upgrades.md); what a level is worth is not.
- **Whether a bought ceiling drags its dial up** — `Raise Alchemy Level` adds only to
  `MaxAlchemyLevel`, and no authored effect moves `AlchemyOutputLevel`. Play has only ever seen the
  dial sitting at its ceiling, which both an auto-follow and a diligent player would produce.

## Interface

- **The Attribute/Statistic split against the live interface** — the translation in
  [vocabulary.md](vocabulary.md) is derived from the game's serialized data, so a label composed at
  runtime rather than stored with the screen would be absent from it entirely. Settle by opening the
  Statistics panel and one attribute purchase list side by side.
- **Pages nobody has opened** — Spellbook > Spell Types, Concepts' Discover and Loadout, both
  Automate pages, Alchemy's Learn and Manage, Artifact Create, the Time Rune Archive, World >
  Dimensional, and the Statistics, Achievements and Tips panels. The cheapest item here, and it
  blocks several others.
