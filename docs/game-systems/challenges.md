# Challenges

Challenges live at **Time > Challenges**. The system is partly played; numbers on this page are
observations, not extracted constants.

- Challenges are **picked before performing the reset**, and the reset is what puts your pick into
  play: every challenge queued when you press the reset starts the new run **active**, and the one
  you had **selected** carries as well. Several run at once (five observed); how slots unlock is
  unrecorded.
- The page has two views, **Active** and **All**. Active lists the current offers; All shows the
  whole grid plus a **Challenges Complete** count and the challenge you currently hold selected.
- Offers are fetched by one button that renames itself: **New Challenges** until the first fetch,
  **Reroll Challenges** after. They arrive Inactive and are queued per row; see below.
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

A challenge card reads **Reach** (the goal), **While** (what holds until the goal is met) and
**Reward**. E.g., *Specialization: Augments* reads Reach Expert Augments Lv 20; While +5 Expert Max
Level, +10 Expert Max Investment and +20 Expert Augments Leeway; Reward +2 Starting Time
Advancements and +1 Expert Augments Base Level. Until Expert Augments reaches 20, every other Expert
node stops at Max Level 5 and the other Expert nodes share 10 levels of investment between them, so
the Expert levels are rationed until the goal is met. Free research bonus levels cost no points but
do not lift those caps.

## The offer list and its reroll budget

The Active view shows a handful of offers at once (five observed) under the fetch button. A fetch
replaces the whole list — you cannot reroll one row.

- The first fetch of a world cycle is **free**; every press after that **spends a challenge
  reroll**.
- The fetch is also the **gate on the reset**: the reset is refused until the cycle's offers have
  been fetched, and fetching arms the **entire fetched list** for the reset — including
  challenges that already ran this cycle. A challenge you had queued or selected survives the
  redraw.
- The budget observed was **3**, and it is **not printed on the page**. The only place the number
  appears is inside the Reroll Challenges button's own tooltip, which reads
  `Challenge Rerolls Left`. If you want to know what a press costs you, hover the button.
- This budget is **separate from the per-tree reroll** each of the seven discovery trees carries;
  they are different systems that happen to share the word (see [discovery.md](discovery.md)).
- Straight after a reset the budget reads **0 of 3 and cannot be spent** — rerolling is refused
  because the world cycle is not complete yet. The budget comes back with the cycle, not with the
  run, so a fresh run is not a fresh set of rerolls.
- A reroll does **not** guarantee a fresh list: an offer can survive into the new draw (one of
  five did, twice).

## Selecting is separate from queueing

Two different states sit on the same page and are easy to conflate.

- Each offer row carries a state button — **Inactive**, **Queued**, or **Passed**. Pressing it
  toggles a not-yet-run challenge in and out of the queue; there is no separate "activate" press.
  A **Passed** row's toggle does nothing: a challenge that already ran this cycle cannot be
  queued again until a reset (though a fresh fetch can still arm it, see above).
- Separately, one challenge can be held as your **selection** for the next run, and the selection
  slot count observed was **1**. That is not the same number as how many challenges can *run* at
  once: five challenges have been seen running together, but how run slots scale is still
  unrecorded.
- The selection **outlives the offer list**. Rerolling away the challenge you had selected leaves
  it selected while it no longer appears among the offers — the All view still shows it, the
  Active view does not.
- With the slot full, **picking another offered challenge swaps it**: one press gives up the held
  one and takes the new one. That is also the only way out of a stale selection, since the
  rerolled-away pick has no row left to press. What you cannot do is empty the slot — there is no
  "select nothing", and a selection that was never activated cannot be abandoned.
- The selection you are holding when you press the reset **carries into the new run** as that run's
  challenge; the offer list does not.

What challenges exist, what passing one grants, and the difficulty economy are unrecorded; see
[open-questions.md](open-questions.md).
