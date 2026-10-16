# Configuration and safety

[Back to documentation](../README.md) · [Installation](installation.md) ·
[Troubleshooting](troubleshooting.md)

Open the in-game **Mods** tab and select **Orb Of Creation ModSuite**. The left rail contains
Runtime, General, Auto Buy, Auto Cast, Auto Concept, Auto Harvest, Auto Items, Auto Scribe, Mentor,
and Advanced.

The suite stores all settings in
`BepInEx/config/dev.vojow.orbofcreation.modsuite.cfg`. You can edit that file while the game is
closed, but the Mods tab is the recommended route because it validates values and explains them.

## How changes work

Most settings are staged until you press **Apply**. **Revert** discards staged changes for the
selected mod. If a hotkey, quick control, or external file edit changes a value you are editing,
the row asks you to choose **Keep mine** or **Take live** before applying.

Each feature page has an immediate **Turn on** or **Turn off** command. The matching gameplay quick
control changes the same saved value immediately. A feature that is configured On can still show a
separate waiting, blocked, or failed status; its tooltip and the Runtime page explain why.

## Start safely

Auto Buy starts Active. Auto Cast, Auto Concept, Auto Harvest, Auto Items, Auto Scribe, and Mentor
start Disabled. Review Auto Buy's reserves and queue settings before playing, then enable other
features one at a time.

The top-left **STOP ALL** control is always available during gameplay. It immediately stops new
suite actions and discards prepared automation work. Press it again to resume. The **General** page
offers the same **Stop all** or **Resume all** command, plus **Automation enabled** for turning the
whole suite off without editing the configuration file.

Back up saves before risky changes and use only one automatic buyer or other mod responsible for a
given game action.

## Feature settings

- **Auto Buy** controls Structure and Upgrade spending separately. Absolute and relative reserves
  protect chosen resources, and `LeaveQueueSlots` keeps room for manual actions. Spell Leveling is
  enabled by default while Auto Buy is active and can be turned off separately.
- **Auto Cast** starts Disabled, uses `F8` by default, and fully charges charge-capable spells by
  default. Turn off **Full charge** to cast them immediately.
- **Auto Concept** starts Disabled. Its default Timed Cycle rotates acquired concepts after 30
  seconds of settled training. Rotate All may replace a lower-mastery active concept, while
  Preserve Manual keeps concepts that were active when automation began. Rate, quantity, and drain
  protections remain configurable.
- **Auto Harvest** starts Disabled. Fruit-tree and treasure-tree collection are both selected by
  default behind the feature switch.
- **Auto Items** starts Disabled. Scrolls and Relics are selected by default behind the feature
  switch. Temporary Fruits, Potions, and Threads remain unused until you explicitly approve
  individual items in the picker.
- **Auto Scribe** starts Disabled. An empty Roles value selects all supported producible roles and
  `none` selects none. To narrow production, set Roles to a comma-separated selection of
  `scribe.advancement`, `scribe.development`, `scribe.echo`, `scribe.excellence`,
  `scribe.learning`, and `scribe.power`.
- **Mentor** starts Disabled and uses `Left Alt + M` by default. Choose which mastery domains may
  receive shared XP, the source policy, sharing percentages, and whether the percentage is divided
  across recipients or granted to each recipient.

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
