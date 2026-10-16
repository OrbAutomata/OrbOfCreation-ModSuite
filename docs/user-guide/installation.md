# Installing the ModSuite

[Back to documentation](../README.md) · [Configuration](configuration.md) ·
[Troubleshooting](troubleshooting.md)

The current release installs as one BepInEx plugin, `OrbModSuite.dll`. See
[what is included](../README.md#whats-in-the-suite) before enabling features.

The supported baseline is the Windows 64-bit Mono build of Orb of Creation on Unity `6000.0.70`
with BepInEx `5.4.23.x`. BepInEx 6 and native Linux packages are not supported. Steam Deck is
targeted through the Windows game under Proton and requires separate runtime validation.

## 1. Back up your save

Close the game and make a manual copy of its save directory before installing or changing
automation. On Windows, saves live under
`%USERPROFILE%\AppData\LocalLow\MarpleGames\Orb of Creation`. Under Proton, the same folder sits
inside the game's `compatdata` prefix.

Before automation first runs for a suite version, the suite also creates and verifies a backup.
Completed automatic backups are stored in the save directory under
`backups/auto-modsuite-backup-<timestamp>`. Keep your manual backup until you have confirmed the
game and suite start normally.

Do not run the suite alongside another mod that automates the same game actions.

## 2. Install BepInEx 5

1. Download `BepInEx_win_x64_5.4.23.5.zip` from the
   [BepInEx releases](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5).
2. In Steam, open **Orb of Creation > Manage > Browse local files**.
3. Extract the archive beside `Orb Of Creation.exe`, not inside `Orb Of Creation_Data`.
4. Start and close the game once. Confirm that `BepInEx/config` and
   `BepInEx/LogOutput.log` exist. If they do not, work through the
   [missing-log checklist](troubleshooting.md#bepinexlogoutputlog-does-not-exist).

On Proton, add this Steam launch option:

```text
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

## 3. Install the current release

Install the release marked **Latest** on the
[Releases page](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/releases). Pre-releases are
test builds; use one only if a maintainer asks.

1. Under **Assets**, download `OrbModSuite-release.dll`. Do not use `OrbModSuite-perf-debug.dll`;
   it is a developer build.
2. In `BepInEx/plugins`, create a folder named `OrbModSuite`.
3. Move the downloaded file into that folder and rename it to `OrbModSuite.dll`. BepInEx loads any
   file name, but one fixed name means the next update replaces the file instead of adding a
   second copy.
4. Confirm the following layout:

```text
Orb of Creation/
|-- Orb Of Creation.exe
|-- winhttp.dll
`-- BepInEx/
    `-- plugins/
        `-- OrbModSuite/
            `-- OrbModSuite.dll
```

Keep exactly one suite DLL anywhere under `BepInEx/plugins`. Remove separate
`OrbAutomata.dll`, `OrbMentor.dll`, `OrbModConfig.dll`, and `OrbModding.Common.dll` files; they are
not part of the one-plugin installation.

To check that it worked, start the game: `BepInEx/LogOutput.log` should list
**Orb Of Creation ModSuite** once without dependency errors, and the game shows a **Mods** tab.

The suite reads settings from
`BepInEx/config/dev.vojow.orbofcreation.modsuite.cfg`. Other per-plugin configuration files are not
imported.

The exact AutobuyOrb plugin identity is detected and its overlapping Structure and Upgrade actions
are disabled. This cannot detect every third-party automation mod, so use only one mod for each
automated action family.

Next, open the game and follow [configuration and safety](configuration.md). If the suite does not
load as described, go directly to [troubleshooting](troubleshooting.md).
