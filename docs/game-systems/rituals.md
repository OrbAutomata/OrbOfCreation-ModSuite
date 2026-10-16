# Rituals

A ritual is the game's combat encounter: a run of enemy waves you enter deliberately from
**Rituals > Rituals**, fight to a wave you cannot clear, and are paid for on the way out.

## Entering one

Three separate decisions sit in front of a ritual, and each has its own price.

- **Selecting** a ritual is not activating it. Selection only drives the panel; it costs nothing
  and can be undone.
- **Starting level** is a dial with a maximum, and **the lower bound is `1`**. The panel's `-`
  button stops there; one is the baseline, and a ritual cannot be entered below it. The header
  reads the pair, e.g. `Lv 200/240`. The maximum is your **Ritual level**, the chip at the top left
  of Rituals > Rituals with the Ritual Xp bar beside it, so it rises as runs pay Ritual Xp. A
  higher starting level raises both prices below and the level the run can finish at.
- Starting level moves the completion cost by enormous factors, so it is the main dial on what a
  run costs to sustain. E.g., on Language: `Lv 1` asked 6.33e-18 Paper, `Lv 200` asked 8.83e169,
  and `Lv 240` asked 2.09e227. The Zeal entry price did not move with it.
- **Activation** costs **Zeal**, and that is what Zeal is for. It is a small round number beside the
  Activate button, paid once, at entry. The number is not fixed: it **rises while previous runs'
  duration rewards are still ticking**. Observed on Language: 50 Zeal with nothing running, 70 with
  one duration reward live. Stacking overlapping rituals therefore gets progressively more
  expensive to enter.
- Zeal regenerates against a small cap; a research node governs its recovery, and another extends
  ritual duration.

## The completion cost is a drain, not an entry fee

Separate from the Zeal, a ritual names a **Ritual Completion Cost** in an ordinary resource — "how
many resources will need to be consumed during the ritual process". It is not deducted when you
activate. It appears as a bar in the battle and drains at a per-second rate for as long as the run
lasts, reaching `0` as the run ends (observed: Language at level 200, 8.64e169 Paper at
2.26e167/s, drained to zero across five waves).

Budget it as a sustained supply for the length of the fight, the way a channel's drain is budgeted
rather than its cast cost; see [cast-modes.md](cast-modes.md).

## The battle

- The header names the level you entered at and the current wave: `[Level 200] Aberration Wave: 1`.
- Each wave is a handful of enemies carrying their own levels, and they **step up wave over wave**
  (observed: Lv 1062 in wave 1 to Lv 1071 in wave 5).
- Enemy levels also rise steeply with the **starting level** you chose. Observed on Language:
  entering at `Lv 240` put five Aberrations of Lv 1276-1280 in wave 1 alone, against Lv 1062 for a
  wave-1 entry at `Lv 200`. Wave 1 of a high-level entry can therefore outclass a loadout that
  clears lower entries comfortably: a spell dozens of orders of magnitude short of an enemy's
  health makes no visible dent, and the run stalls rather than losing.
- The enemy mix is **per ritual and re-rolled every run**, so two rituals at the same starting
  level can differ by orders of magnitude, and one ritual can roll an easy run and then a hard one.
  Enemies with a blue **shield** badge take far less damage: e.g. the same cast hit plain enemies
  for ~1e179 and shielded ones for 2e167–5e172.
- Mysticism's Stability meter is your health while one runs; see
  [disciplines.md](disciplines.md).
- **Auto-attack clears waves with no input**, so a ritual well inside your power finishes on its
  own — but slowly. The auto-attack fires only about once every second or two and hits one target,
  so its limit is usually its *rate*, not its damage: a five-wave run at a level far below your
  power still grinds for minutes, and each new wave respawns a fresh set of enemies to chew
  through. Casting into the battle is the lever, and a multi-hit spell is worth more than a big
  single hit — a spell that lands five attacks per cast will finish a run in a handful of casts
  where auto-attack alone takes minutes. At high starting levels auto-attack also stops doing
  damage that matters — it can land thirty orders of magnitude short of an enemy's health — while
  an instant multi-target spell clears the same run in a handful of presses.
- An **End Ritual** button quits a running battle. While a battle is live, every ritual control is
  unavailable — one ritual at a time, and the others grey out.

## Every run ends on a results panel, won or not

**End Ritual is the give-up button, and it always scores the run as a loss.** Quitting opens the
same results panel, and it reads `Ritual failed.` no matter how far you got — observed quitting
after clearing two waves of five, which still printed `Ritual failed.` above a full Spoils list.
You keep the spoils the run had accrued. Those come mostly from cleared waves, but not only: a quit
at `Waves Completed: 0/5` can still list Spoils rows, while other quits at `0/5` list none. What
you forfeit is the win itself, and with it the **New Level Achieved** line and the duration reward
that only a completed run grants.

A run you actually *win* ends on its own — there is no button to press — and the results panel
opens by itself. So the panel is the record of how a run ended rather than a reward screen only.
It is a modal and has to be closed before the ritual list is usable again. The `/5` is the run's
wave target.

## What a completed ritual pays

Success opens a results panel with four things on it:

- **New Level Achieved** — the ritual's own level track advances.
- **Waves Completed**.
- A **Ritual Chain** multiplier. It climbs by one on each consecutive success, across different
  rituals, and has not been seen past ×10. A run quit with End Ritual neither advances nor breaks
  it.
- A **duration**, e.g. `For 34:15`.

Below those sit the rewards the run grants for that duration — e.g. a Language run at level 200:
+3.32e11 % Parchment Resources Gained, +15.8 % Parchment Interest/min, −7.92 % Book Cost Scaling —
plus one-off spoils (Ritual Xp and resources).

## The duration reward runs on its own, and can be ended early

The duration is a live buff, not a printed record. It appears as a **timer chip in the
active-durations strip along the bottom of the window**, and it can be **cancelled early**, which
removes the chip and stops the bonuses.

The results panel keeps showing its original `For 34:15` after a cancel, because that panel is a
static record of what was *awarded*. Read the bottom strip, not the panel, to know what is
currently running.

## Restoration, the final ritual

Restoration ("The final ritual. Restore the old world.") does not follow its level dial: e.g. a
panel reading `Lv 70` ran its battle at `[Level 55]`. Winning it plays the ending: a
cutscene, then a **Game Complete!** panel ("You beat Orb of Creation! Time Unlocked."). Nothing
resets: time played, resources and ritual levels carry on as before, and the game continues once
the panel is closed.
