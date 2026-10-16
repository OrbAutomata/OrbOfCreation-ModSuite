# Development setup

[Back to documentation](../README.md) · [Contributing](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/CONTRIBUTING.md) ·
[Engineering doctrine](engineering-doctrine.md)

## Repository flow

Branch from `develop` and keep the pull request focused. Pull requests target
`develop`; `main` is updated by the release flow in
[the release procedure](../releasing.md) and
[release review checklist](releases.md). Use an area-prefixed commit subject such
as `autobuy: ...`, `ui: ...`, `build: ...`, or `docs: ...`.

## Dependencies

The default build requires only the pinned .NET SDK. The repository commits a
metadata-only, full-surface reference closure generated from the audited Orb of
Creation v1.0.5 assemblies. These references preserve native assembly, type,
and member identities but contain no method bodies. They are not the
hand-written test stubs.

With no `OOC_GAME_DIR` set, both configurations build against the committed
references:

```bash
dotnet build src/OrbModSuite.csproj --configuration Debug
dotnet build src/OrbModSuite.csproj --configuration Release
```

`global.json` pins the exact SDK used for release builds. Install that SDK
version rather than allowing the compiler to float.

`VERSION` is the only build-version input. `Directory.Build.props` reads it,
the suite project derives its SDK version, and MSBuild generates the BepInEx
version constants under the active ignored `obj-*` directory. `CHANGELOG.md`
is the only other tracked file carrying the maintained release version.

The canonical publication build also sets
`ContinuousIntegrationBuild=true`. That normalizes SourceLink document roots
to `/_/`, so the full DLL is byte-identical across checkout locations. A plain
development build remains deterministic within its checkout but is not the
cross-checkout release-byte reproduction command.

Set `OOC_GAME_DIR` only for operations that need the real installation: local
installation, installed contracts, refs regeneration, and the per-refs-change
faithfulness comparison. Both CI publication flavors compile from committed
refs without a game. The game is never launched by the contract gate.

On Windows, point `OOC_GAME_DIR` at the game root:

```powershell
$env:OOC_GAME_DIR='C:\Program Files (x86)\Steam\steamapps\common\Orb of Creation'
dotnet test tests/OrbModding.GameContractTests/OrbModding.GameContractTests.csproj -c Release
```

On Linux or macOS, `OOC_GAME_DIR` may point to a staged ignored copy with game
assemblies under `Orb Of Creation_Data/Managed` and BepInEx under `BepInEx/core`,
which the installed-game contracts of [the gate](../testing/README.md#the-gate) then read:

```bash
export OOC_GAME_DIR="$PWD/lib"
```

Keep absolute local paths out of tracked files. Building does not authorize an
installation into the game.

## Documentation preview

From the repository root, serve the published documentation with its locked dependency group:

```bash
NO_MKDOCS_2_WARNING=1 uv run --project tools --locked --only-group docs mkdocs serve --config-file mkdocs.yml
```

The local URL is printed when the server starts. The preview reloads when a file under `docs/` or
the site configuration changes.

## Development gate

Run [the gate](../testing/README.md#the-gate).

Stub-linked outputs live under `bin-stubs/` and `obj-stubs/`; metadata-true
reference builds use the normal `bin/` tree. The canonical publication build
adds the cross-checkout setting above. Never install a
hand-written-stub-linked DLL. Continue with the
[testing hub](../testing/README.md) and
[runtime validation protocol](../testing/runtime-validation.md).

## Supported operator commands

Only after the user explicitly authorizes a local installation:

```bash
./script/install release
./script/install perf-debug
```

Both modes refuse to run while the game is open, run the gates, back up saves and
installed DLLs, reject duplicate or retired DLLs, install the verified output,
and print SHA-256 hashes. `release` installs the same committed-reference build
that ships. The local `perf-debug` installer includes ServiceCycle profiling
and continues to compile against the real game closure; the CI perf-debug asset
uses the matching committed profile refs. Both installers also need the game
directory as the destination and for installed-contract validation. Neither
command tags, publishes, or launches the game.

From a clean commit, rehearse the supported package without installing it:

```bash
./script/package
```

The package rehearsal runs the portable and installed-contract gates, then
builds the canonical Release DLL from committed references before writing an
allowlisted archive and hash manifest under `artifacts/releases/`.

When a new audited game version requires regenerating `lib/game-refs`, run the
`ReleaseAssemblyCheck` comparison in
[the release procedure](../releasing.md#reference-change-faithfulness) in that
same refs change. It is a reference-faithfulness gate, not a per-release gate.
