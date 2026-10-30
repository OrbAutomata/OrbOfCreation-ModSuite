# Configuration and safety

[Back to documentation](../README.md) · [Installation](installation.md) ·
[Troubleshooting](troubleshooting.md)

Open the in-game **Mods** tab. Its left rail scrolls independently of the selected page,
so every page remains reachable in a small window. **Strategist**
shows progression advice; **Runtime** shows automation health; the feature pages contain settings.

The suite stores all settings in
`BepInEx/config/dev.vojow.orbofcreation.modsuite.cfg`. You can edit that file while the game is
closed, but the Mods tab is the recommended route because it validates values and explains them.

## How changes work

Most settings are staged until you press **Apply**. **Revert** discards staged changes for the
selected mod. If a hotkey, quick control, or external file edit changes a value you are editing,
the row asks you to choose **Keep mine** or **Take live** before applying.

Setting names show **[Unsaved]** while an edit is staged, **[Invalid]** when its value needs fixing,
or **[Conflict]** when an outside edit needs a decision. The footer explains the validation error or
reminds you to apply unsaved edits. A conflict takes precedence over the other row markers.

Each feature page has an immediate **Turn on** or **Turn off** command. The matching gameplay quick
control changes the same saved value immediately. A feature that is configured On can still show a
separate waiting, blocked, or failed status; its tooltip and the Runtime page explain why. The page
labels the immediate control separately from staged settings. Its status updates while the page is
open without discarding your input. Longer policy details expand the header and reflow following
rows while retaining editor widgets and drafts; a shorter page clamps the scroll only when needed.

## Start safely

Auto Buy starts Active. Review its reserves and queue settings before playing, then review each
feature's mode and enable additional automation one feature at a time.

The top-left **STOP ALL** control is always available during gameplay. It immediately stops new
suite actions and discards prepared automation work. Press it again to resume. The **General** page
offers the same **Stop all** or **Resume all** command, plus **Automation enabled** for turning the
whole suite off without editing the configuration file. **Automation pace** sets how often the suite
re-reads the game, from Fastest (4 times a second) to CpuSaver (every 30 seconds); slower paces cost
the game less time and make automation act less often.

Back up saves before risky changes and use only one automatic buyer or other mod responsible for a
given game action.

## Feature settings

- **Auto Levels** starts Disabled and adjusts global level dials within regeneration and drain
  budgets. [Automatic levels](automatic-levels.md) explains its choices and per-dial status.
- **Game Fixes** starts Active, with **Sound pool guard** on and **Merge repeated sounds** off. Its
  switches take effect immediately, and **STOP ALL** and **Automation enabled** leave them on. [Game fixes](game-fixes.md) explains what each fix changes.
- **Quality of Life** starts Disabled. **Mute passive notifications** is selected behind that switch
  and silences encountered reflective Learning mutagen popups while their resource effects continue.
  The game saves these mute choices. While enabled, the feature reapplies them if you unmute a
  supported passive. Turn the feature or the option off before unmuting through the game's own
  control; disabling automation does not unmute anything.
- **Auto Buy** controls Structure and Upgrade spending separately. Absolute and relative reserves
  protect chosen resources, and `LeaveQueueSlots` keeps room for manual actions. Spell Leveling is
  enabled by default while Auto Buy is active and can be turned off separately. **Prioritize strategist
  goal** starts off. With Auto Buy and Upgrade spending enabled, it puts the strategist's eligible,
  affordable goal first. It does not reserve resources or save toward an unaffordable goal.
- **Auto Cast** starts Disabled, uses `F8` by default, and fully charges charge-capable spells by
  default. Turn off **Full charge** to cast them immediately. **Ritual aware** is on by default:
  attack spells wait for a ritual battle, and during one no channel starts while an attack spell is
  equipped. Turn it off to cast every ready spell in turn; your own casts are never held back. It
  pauses while Magic > Spellbook > Loadout is open, so spells can recharge fully and be removed.
- **Auto Concept** starts Disabled. Its default Timed Cycle rotates acquired concepts after 30
  seconds of settled training. Rotate All may replace a lower-mastery active concept, while
  Preserve Manual keeps concepts that were active when automation began. Rate and drain
  protections remain configurable.
- **Auto Harvest** starts Disabled. Fruit-tree and treasure-tree collection are both selected by
  default behind the feature switch.
- **Auto Items** starts Disabled. Scrolls and Relics are selected by default behind the feature
  switch. Temporary Fruits, Potions, and Threads remain unused until you explicitly approve
  individual items in the picker.
- **Auto Scribe** starts Disabled. An empty Roles value selects all supported producible roles and
  `none` selects none. To narrow production, set Roles to a comma-separated selection of
  `scribe.advancement`, `scribe.development`, `scribe.echo`, `scribe.excellence`,
  `scribe.learning`, and `scribe.power`. A misspelled or unsupported key selects nothing, and the
  status line names it.
- **Mentor** starts Disabled and uses `Left Alt + M` by default. Choose which mastery domains may
  receive shared XP, the source policy, sharing percentages, and whether the percentage is divided
  across recipients or granted to each recipient.

## Strategist advice

The **Strategist** page shows a suggested next step, the active goal's costs and shortfalls, and
alternative progression gates. An uncovered cost above storage capacity asks you to raise the relevant
cap; one with no measured income asks you to find a source. **Referenced by prerequisite rules** names
content related to that goal. Those rules can include alternatives, and other requirements can still
block the content. Allocation advice asks for more points without promising when they will arrive.

Advice updates about once a minute and shows the run time of its snapshot. Income estimates use
sustained gross gains, including bursts, and assume other spending stops. They are guidance rather
than promises of when a purchase becomes affordable. Loading and retained-snapshot messages explain
when advice is unavailable or waiting for a fresh reading. A retry failure shows its recorded reason;
any retained advice keeps its original capture time until a fresh bulletin arrives. Auto Buy returns
to ordinary ordering after a strategist failure and resumes goal priority only when a fresh bulletin
arrives. The page itself performs no purchases.

## Runtime and problem reports

The **Runtime** page shows feature health, current waits and blockers, completed automation activity,
and the recent decision journal. It also provides two immediate actions:

- **Create bug report** packages recent activity, settings, the BepInEx log, and identifiable save
  files into one zip. It captures evidence already held by the suite; it does not start recording.
- **Check game math** performs a read-only comparison against the loaded game. The game visibly
  pauses while this check runs.

Use **Create bug report** immediately after a problem, then follow
[reporting a problem](troubleshooting.md#reporting-a-problem) before sharing the file.

## Game updates and compatibility quarantine

An unknown but complete game assembly pair opens the Mods control plane in compatibility quarantine.
Gameplay patches and services remain stopped, but **Check game math** is available. Waiting for an
audited ModSuite release is the safe choice.

If you choose to proceed at your own risk, **Resume all** or the top-left STOP button acknowledges
only the exact installed assembly pair and resumes in the same action. **Advanced > Allow this
unverified game build** records the same acknowledgement while leaving STOP engaged. Either game
assembly changing resets the acknowledgement.

Next, keep [troubleshooting](troubleshooting.md) available while you try your configuration. To
remove the suite and keep or discard its settings, follow [uninstalling](uninstalling.md).
