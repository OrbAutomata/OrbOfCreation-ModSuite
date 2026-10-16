# OrbOfCreation ModSuite

[![CI](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/actions/workflows/ci.yml/badge.svg)](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/OrbAutomata/OrbOfCreation-ModSuite?include_prereleases)](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

OrbOfCreation ModSuite is an unofficial, single-plugin collection of automation, mastery-sharing,
configuration, and diagnostic tools for the Windows Mono build of
[Orb of Creation](https://store.steampowered.com/app/1910680/Orb_of_Creation/).

**[Read the ModSuite documentation](https://orbautomata.github.io/OrbOfCreation-ModSuite/)** for
the feature overview, installation guide, configuration help, and troubleshooting.

## Get started

Players should download the release marked Latest on the
[Releases page](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/releases), then follow the
[installation guide](https://orbautomata.github.io/OrbOfCreation-ModSuite/user-guide/installation/).
Back up your save before enabling automation.

The supported baseline is Windows 64-bit Mono, Unity `6000.0.70`, BepInEx `5.4.23.x`, and .NET
`netstandard2.1`. Steam Deck is targeted through the Windows game under Proton and requires separate
runtime validation. BepInEx 6 and native Linux builds are not supported.

## Build and test

Production builds use assemblies from a local game installation; those binaries are never stored
in this repository.

```powershell
$env:OOC_GAME_DIR='C:\Program Files (x86)\Steam\steamapps\common\Orb of Creation'
dotnet build src/OrbModSuite.csproj -c Release
```

Run the complete portable development suite with:

```sh
./script/test
```

The portable gate runs on Windows, Linux, and macOS with .NET SDK 10. See the
[development setup](https://orbautomata.github.io/OrbOfCreation-ModSuite/development/setup/) for
real-reference and packaging workflows.

## Contributing, security, and licensing

Read [CONTRIBUTING.md](CONTRIBUTING.md) before submitting code or runtime evidence. Report
save-corruption, code-execution, or local-data vulnerabilities privately according to
[SECURITY.md](SECURITY.md).

Source code is available under the [MIT License](LICENSE). This project is not affiliated with or
endorsed by MarpleGames or the publishers of Orb of Creation. Game names and assets belong to their
respective owners; see [third-party notices](THIRD_PARTY_NOTICES.md).
