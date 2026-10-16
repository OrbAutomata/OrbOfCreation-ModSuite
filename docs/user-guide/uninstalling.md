# Uninstalling

[Back to documentation](../README.md) · [Installation](installation.md) ·
[Troubleshooting](troubleshooting.md)

1. Close the game.
2. Remove the `BepInEx/plugins/OrbModSuite` folder.
3. Search `BepInEx/plugins` for any other `OrbModSuite.dll`, `OrbModSuite-release.dll`, or
   `OrbModSuite-perf-debug.dll` and remove it.
4. Remove separate `OrbAutomata.dll`, `OrbMentor.dll`, `OrbModConfig.dll`, and
   `OrbModding.Common.dll` files if they are still present.
5. Keep or delete `BepInEx/config/dev.vojow.orbofcreation.modsuite.cfg`, depending on whether you
   want to preserve your settings for a later reinstall.
6. Keep or delete generated problem reports under
   `BepInEx/config/OrbOfCreation-ModSuite/diagnostics/`.

The suite does not add custom records to save files. Automatic save backups under the game's save
directory at `backups/auto-modsuite-backup-<timestamp>` are ordinary copied files and are not removed
when the plugin is deleted. Keep at least one backup until the unmodified game loads normally; after
that, retain or delete those copies as you prefer.

To reinstall, return to the [installation guide](installation.md) and download the current build
from the [Releases page](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/releases).
