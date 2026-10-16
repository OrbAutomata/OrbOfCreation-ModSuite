# Reset and new game plus

Prestige lives at **Time > Reset**, and that page publishes exactly three decision facts: your
starting Time Advancements next run, the delta against the previous run, and a survival list
reading **Challenges, Achievements, Time Advancements**. The Challenges entry means the picks you
made before pressing reset: the selected challenge carries, and every challenge queued at the
reset starts the new run active (see [challenges.md](challenges.md)).

The reset control is gated on two conditions rather than on any price: the world cycle must be
complete and challenges must have been fetched. Until both hold, the button is inert; the page body
does not say so, but the button's own tooltip names the fetch half ("Fetch new challenges before
starting anew"). What completes the world cycle is unrecorded.

Everything not on that survival list is lost: resources, attributes, upgrades, spells, glyphs,
concepts, research, and the runes themselves — though the persistent advancement levels runes granted
keep applying (see [time-runes.md](time-runes.md)).

**Lost means re-gated, not merely zeroed.** An upgrade you owned comes back at level 0 *and* at
unavailable, and the game refuses it without publishing any reason for the refusal, so it has to be
re-unlocked along the normal progression chain rather than simply re-bought at its base price.
Straight after a reset exactly one upgrade, Infuse Orb, can be bought. Structures reset the same way but stay enabled, so their toggles survive while their
levels do not.

A persistent reset also clears each discovery tree's ladder and choice state, so every ladder
restarts at its base price; see [discovery-pricing.md](discovery-pricing.md).

## Two clocks

The game keeps two run clocks. **Time Played** is the save slot's total — "How much time has been
played in this save slot" — and a reset does not touch it. **Time Played this Reset** measures the
current run and starts over at every reset; it exists only once resets are unlocked. Both advance
only while a game is loaded, so time with the game closed is not counted, and both print in the
duration format of [numbers-and-rounding.md](numbers-and-rounding.md).

Two effects belong to NG+ alone:

- **Cost curves reset only at NG+.** Advancement and progression costs climb until attributes become
  unaffordable, and an ordinary reset does not undo that escalation.
- **NG+ starts you with bonus orb levels**, some pre-allocated and some free to place. Free bonus
  levels do not advance the paid cost curve; see
  [attributes-and-upgrades.md](attributes-and-upgrades.md).
