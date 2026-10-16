# Cantrips, charms, channels and charging

## Cantrips

Cantrips are the plain one-shot spells: cast once, the effect lands, the cooldown runs. "Cantrip"
is also a targetable category in its own right — many effects name "all Cantrips", and the global
tracks carry per-level Cantrip multipliers (see [casting-level.md](casting-level.md) and
[output-and-reserve.md](output-and-reserve.md)).

## Charms

Charms are toggled temporary buffs. They occupy a spot and weight like anything else, and their value
is entirely in the window they open: toggle the charm, then spend the window casting the spells it
buffs.

E.g., one observed charm — Whirling Sorcery, Primary/Expansion: 200 mana + 2 Thaumaturgy, 30 s toggle
duration, 30 s cooldown, +43.3 % Cantrip Spell Power. Against a 7 s cooldown on the buffed spell,
three to four casts fit inside one window, and a duration equal to the cooldown means the charm can
run at effectively full uptime.

Charms are also the usual target of per-spell upgrade lines, which append rows to the charm's effect
list; see [spells.md](spells.md).

**A toggle is one-way unless you turn a setting on.** With **Cancellable Spells** off in the game
settings, an active charm or toggle spell cannot be switched back off; it runs until its window
expires. Turning that setting on is what makes the toggle work in both directions, and it is worth
doing before committing to a long-running toggle.

## Channels

A channeled spell holds the caster for its duration and behaves unlike anything else in the loadout:

- **A channel blocks all other casting.** Casting anything else aborts the channel early.
- **A channel drains on top of its cast cost.** You pay the cast cost to start, then a per-second
  drain for as long as it runs, so affordability has to be checked against the sustained rate rather
  than the entry price.
- The real cost of a channel is **loadout downtime**, not the mana.

E.g., one observed channel — Channel Spark, Lv 2: weight 1, 39.8 s cooldown, up to 16 s of channel,
−110 mana/s while channelling, +5.37 Spark/s.

## Charging

Some spells can be **held to charge**: you trade cast time for power on that cast. The mechanic is
unlocked by the **Charged Spells** research (e.g., observed at ×1.40 Cantrip and ×1.10 Charm charge
effect). Charging costs time rather than resources.
