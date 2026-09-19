# Challenges

Challenges live at **Time > Challenges**. The system is largely unplayed; this page records the
shape and deliberately not the numbers — treat specifics as unknown rather than assumed.

- Challenges are **picked when starting an NG+ run, before performing the reset**. They do not
  carry across resets — a reset commonly clears them.
- New offers are fetched with the **New Challenges** button; they arrive Inactive and are activated
  per row. Multiple challenges run at once, and a fourth challenge slot exists; the exact slot
  count and how slots unlock are unrecorded.
- **Fetching is free once, then costs a reroll.** The first fetch of a run is free; every later one
  spends one of a limited number of challenge rerolls, and a fetch replaces the whole offer list.
  It is one button that renames itself: **New Challenges** until the first fetch, **Reroll
  Challenges** after.
- **There are two offer lists.** The Time > Challenges screen and the reset modal each fetch their
  own, so what the reset modal offers is not the Time screen's list refreshed.
- Any active challenge can be **abandoned**, and a completed one shows a **Passed** state.
- **Some challenges race a clock.** Such a challenge fails the moment the run's own timer — the
  same *Time Played this Reset* the game shows elsewhere — passes its limit, and the tooltip prints
  the elapsed time against that limit. The limit is not fixed: it is scaled for the level the
  challenge is being attempted at, so the same challenge is held to a different clock each time it
  comes back a level higher. Challenges without a time condition are not timed at all.

Challenges do more than scale numbers: they can **modify requirements**, applying as passive
modifiers on the requirement graph — one observed challenge applied `-5` to a research node's
requirements, showing in-game as `leeway 5`. An active challenge can therefore put content within
reach that would otherwise be gated: the requirement itself moved, not just its cost.

What challenges exist, what passing one grants, and the difficulty economy are unrecorded; see
[open-questions.md](open-questions.md).
