# OrbOfCreation ModSuite

OrbOfCreation ModSuite is an unofficial collection of automation and quality-of-life tools for
Orb of Creation. It installs as one BepInEx plugin and adds a native-styled **Mods** tab where you
can configure features, see their current status, and create a diagnostic bundle when something
goes wrong.

## Start here

1. [Install the current release](user-guide/installation.md) and confirm that the suite loads once.
2. [Configure the features and safety controls](user-guide/configuration.md) in the in-game
   **Mods** tab.
3. Keep [troubleshooting](user-guide/troubleshooting.md) handy for startup, compatibility, or
   behavior problems.
4. Follow [uninstalling](user-guide/uninstalling.md) when you want to remove the suite.

## What's in the suite

- **Auto Buy**, including optional progression-aware Spell Leveling, manages attributes and
  upgrades while respecting configured spending and queue reserves.
- **Auto Cast** casts selected spells, with optional full charging.
- **Auto Concept** trains acquired Scholar Active Concepts.
- **Auto Harvest** collects supported fruit and treasure trees.
- **Auto Items** uses approved Scrolls, Relics, and explicitly selected temporary items.
- **Auto Scribe** produces supported Scroll roles while yielding to the game's own Scribe work.
- **Mentor** shares a configured portion of earned mastery XP with eligible spells, artifacts, and
  ordinary alchemy recipes.
- The **Mods** UI provides configuration, immediate safety controls, runtime status, activity
  history, and a one-click bug-report bundle.

All automation remains subject to the game's live rules and the suite's safety checks. Start with
the defaults, enable one feature at a time, and keep only one mod responsible for each automated
action.

## Learn more

- [User guide](user-guide/README.md) — install, configure, troubleshoot, and uninstall.
- [Game systems](game-systems/README.md) — facts about Orb of Creation itself, version 1.0.5.
- [Strategy](strategy/README.md) — opinions and policies for playing the game well.
- [Runtime architecture](runtime-architecture/README.md) — how the suite is designed.
- [Testing](testing/README.md) — evidence layers and runtime validation.
- [Reverse engineering](reverse-engineering/README.md) — how native game contracts are established.
- [Development](development/README.md) — contributor setup, engineering doctrine, and releases.
- [Contributing](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/CONTRIBUTING.md) — the contributor workflow.
- [Releasing](releasing.md) — the owner publication procedure.
- [The north star](north-star.md) — the goal that guides the project.
