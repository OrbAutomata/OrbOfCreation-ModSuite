# Troubleshooting

[Back to documentation](../README.md) · [Installation](installation.md) ·
[Configuration](configuration.md) · [Uninstalling](uninstalling.md)

When the suite loads, `BepInEx/LogOutput.log` records **Orb Of Creation ModSuite** once. Start with
that file for installation problems. For problems after startup, use **Mods > Runtime > Create bug
report** immediately after the issue so the report includes the most useful recent evidence.

## The suite entered compatibility quarantine after a game update

The suite uses audited game assemblies for its economy calculations. An unknown complete assembly
pair opens only the Mods configuration and diagnostic controls while gameplay patches and services
remain stopped.

`BepInEx/LogOutput.log` contains a warning beginning:

```text
Gameplay runtime quarantined: the installed game build does not match an audited baseline.
```

Run **Mods > Runtime > Check game math** and include its result in your report. Waiting for an
audited ModSuite release is the safe choice. If you choose to proceed at your own risk, the
[configuration guide](configuration.md#game-updates-and-compatibility-quarantine) explains the two
exact-build acknowledgement controls.

A refusal beginning `Refusing to load even the diagnostic control plane` means the suite could not
identify the complete assembly pair. Do not try to acknowledge that state. Report the game version
and attach `BepInEx/LogOutput.log` on the
[issue tracker](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/issues).

## The suite does not load

1. Confirm that `BepInEx/LogOutput.log` exists. If it does not, follow
   [the missing-log checklist](#bepinexlogoutputlog-does-not-exist).
2. Confirm that `BepInEx/plugins/OrbModSuite/OrbModSuite.dll` exists and is the only suite DLL
   under `BepInEx/plugins`; remove any stray `OrbModSuite-release.dll` or
   `OrbModSuite-perf-debug.dll`. If extracting created an extra `OrbOfCreation-ModSuite-<version>`
   folder, move its `BepInEx` folder into the game folder.
3. Remove separate `OrbAutomata.dll`, `OrbMentor.dll`, `OrbModConfig.dll`, and
   `OrbModding.Common.dll` files; they are not part of the one-plugin installation.
4. Search `BepInEx/LogOutput.log` for missing dependencies, duplicate plugin identities, assembly
   errors, or the compatibility refusal described above.

If `BepInEx/LogOutput.log` does not exist, BepInEx itself did not start; see the next section.

## BepInEx/LogOutput.log does not exist

BepInEx did not start, so the suite never had a chance to load. Check each of these, then start and
close the game once more:

1. **Files sit directly beside `Orb Of Creation.exe`.** `winhttp.dll`, `doorstop_config.ini`, and
   the `BepInEx` folder must be in the same folder as the game's `.exe`. Windows **Extract All**
   puts them in an extra folder named after the zip; move that folder's contents up one level.
2. **File extensions are visible.** In File Explorer, turn on **View > Show > File name
   extensions** so you can tell `winhttp.dll` and `doorstop_config.ini` apart from other files.
3. **Antivirus left `winhttp.dll` in place.** If the file is missing after extraction, check
   **Windows Security > Virus & threat protection > Protection history** for a quarantine and
   restore it.
4. **On Proton**, the `winhttp` launch option from the
   [installation guide](installation.md#2-install-bepinex-5) is set.

`LogOutput.log` appears only after the game has been started once with BepInEx in place.

## The Mods tab shows only the configuration plugin

Confirm that the BepInEx log lists **Orb Of Creation ModSuite** during startup. Remove duplicate
suite DLLs and test-stub DLLs from the game directory, then restart the game.

## My settings are missing

The suite reads only `BepInEx/config/dev.vojow.orbofcreation.modsuite.cfg`. Settings in separate
per-plugin files are not imported. Reapply the wanted values in the Mods tab and keep the other
files only as personal references.

If the current suite configuration has an unsupported or malformed schema marker, the suite stops
before changing gameplay. Restore an adjacent `.pre-schema-v*.bak` file if one exists, or move the
current file aside so the suite can create defaults; do not lower the schema marker by hand.

## Steam Deck UI problems or severe frame-rate loss

Press **STOP ALL** first. If the problem clears, resume and enable features one at a time to identify
the trigger. Create a bug report after reproducing the problem, then include whether the native
autoqueue and NG+ tabs were unlocked and which features were active.

## A red banner says the game stopped advancing or its menus stopped switching

The same game element threw an error on fifty ticks in a row (one second), so development and
managers that depend on the game's tick are frozen. The suite only observes this; the error comes
from the game. Return to the main menu and reload your save, which usually clears it. If the element
recovers on its own and runs cleanly for five seconds, the banner disappears. When the banner says the
game's menus stopped switching instead, the failing element is a screen: the game still advances, but
screens after it fail to open or close, and the same steps apply.

The suite saves a bug report automatically when this happens, and the banner names its path under
`BepInEx/config/OrbOfCreation-ModSuite/diagnostics/`. That report names the game element that failed
and contains no save files. The folder keeps the five newest automatic reports
(`orb-modsuite-automatic-report-<timestamp>.zip`); reports you create yourself are never deleted. Send it to the mod authors through an
[issue](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/issues). If the banner says the report
could not be saved, send `BepInEx/LogOutput.log` instead.

## Auto Concept repeatedly changes the same concept

Press **STOP ALL**, then create a bug report immediately. Include the affected concept and resource
names with your reproduction steps. The report preserves the suite's recent decisions and the live
refusal reason without requiring you to copy individual log lines.

## Checking the suite against the live game

**Mods > Runtime > Check game math** compares the suite's economy and purchase decisions with the
loaded game without changing the game. Load a save first. The check runs in one frame, so the game
will visibly pause while it completes; its result is written to `BepInEx/LogOutput.log`.

The first word of the first line is the answer. `AGREE` means the suite and the game reached the
same number everywhere they were compared, and nothing follows it but the count and one `window:`
line — a short result is a healthy one, not a truncated one. `DISAGREE` means at least one number
genuinely differs, and only the checks that differ are listed. `INCOMPLETE` names a condition the
suite does not model and therefore refuses to automate. `INCONCLUSIVE` means nothing could be
compared at all, which is not a pass. Include anything but `AGREE` in a problem report.

## Reporting a problem

1. Reproduce the problem, if it is safe to do so.
2. Open **Mods > Runtime** and press **Create bug report** immediately.
3. Inspect the resulting `orb-modsuite-diagnostics-<timestamp>.zip`. If you intend to share its
   included save data publicly, attach the bundle and concise reproduction steps to an
   [issue](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/issues). Otherwise, open the issue
   with the reproduction steps and say that a bundle is available for private transfer.

The zip is created under `BepInEx/config/OrbOfCreation-ModSuite/diagnostics/` and is capped at
10 MiB. It can contain recent activity, settings, a BepInEx log tail, and identifiable top-level
save files; a report saved automatically for a stopped game has none. Text is redacted for known
usernames and absolute paths, but save files remain exact private game data. Inspect the archive and share it only with a recipient you trust.

If the action fails, the Runtime page explains why and no partial report is presented as ready.
Attach `BepInEx/LogOutput.log` instead when it is available.

After recovery, return to [configuration](configuration.md). If you want to remove the suite,
follow [uninstalling](uninstalling.md).
