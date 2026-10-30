# Third-party projects and acknowledgements

OrbOfCreation-ModSuite is an unofficial fan project and is not affiliated with or endorsed by MarpleGames or the publishers of Orb of Creation. Orb of Creation names and assets belong to their respective owners.

The project builds on and interoperates with:

- [Orb of Creation](https://store.steampowered.com/app/1910680/Orb_of_Creation/) by MarpleGames.
- [BepInEx](https://github.com/BepInEx/BepInEx), used as the Unity/Mono plugin loader.
- [Harmony](https://github.com/pardeike/Harmony), used for runtime method patching.
- [AutobuyOrb](https://github.com/IngoHHacks/AutobuyOrb) by IngoHHacks, used as a behavioral and reverse-engineering reference for Auto Buy. This suite is an independent implementation and does not depend on AutobuyOrb at runtime.
- [ILSpy](https://github.com/icsharpcode/ILSpy), used during managed-assembly inspection.
- [Refasmer](https://github.com/JetBrains/Refasmer) by JetBrains, used to generate the
  metadata-only reference assemblies described below.

Each third-party project remains governed by its own license.

## Distributed reference assemblies

This repository distributes twelve metadata-only reference assemblies produced by Refasmer
from an audited Orb of Creation v1.0.5 installation. They retain the complete type and member
surface, including private and internal metadata, but contain no executable code or game assets.
Their input and output SHA-256 values and pinned generation tool are recorded in
`lib/game-refs/v1.0.5/manifest.json`.

The reference set covers:

- MarpleGames game assemblies: `Assembly-CSharp.dll` and
  `Assembly-CSharp-firstpass.dll`.
- Unity Technologies engine modules: `UnityEngine.dll`, `UnityEngine.CoreModule.dll`,
  `UnityEngine.UI.dll`, `UnityEngine.UIModule.dll`, `UnityEngine.ScreenCaptureModule.dll`,
  `UnityEngine.ImageConversionModule.dll`, and `UnityEngine.AudioModule.dll`.
- Unity Technologies TextMesh Pro: `Unity.TextMeshPro.dll`.
- BepInEx: `BepInEx.dll`.
- Harmony by Andreas Pardeike: `0Harmony.dll`.
- Newtonsoft.Json by James Newton-King: `Newtonsoft.Json.dll`.

They are committed solely to support game-free compilation and member-exact native-contract
verification, and they are not packaged with the mod release. These reference assemblies are
distributed with the game developer MarpleGames' knowledge and consent.

## Vendored source

`tests/OrbModding.GameStubs/BigDouble.cs` is vendored from
[BreakInfinity.cs](https://github.com/Razenpok/BreakInfinity.cs), MIT License, Copyright (c)
2020 Andrei Andreev. The full license text is kept beside it in
`tests/OrbModding.GameStubs/BreakInfinity.LICENSE` and governs that file; this repository's own
MIT grant does not replace it.

It is included because the game ships this same library in `Assembly-CSharp-firstpass`, so the
test double must match the real type's normalization, arithmetic, and comparison behavior
exactly rather than approximate it. Two faithful adaptations were made: the `BreakInfinity`
namespace wrapper was removed so the type is global, as the game places it, and the game's own
additive accessors were appended near the properties. The arithmetic and normalization are
unmodified upstream code.

`tools/OrbModding.ServiceCycleTrace/Dashboard/vendor/chart.umd.min.js` is
[Chart.js](https://www.chartjs.org) v4.4.9, MIT License, Copyright (c) 2014-2025 Chart.js
Contributors. `tools/OrbModding.ServiceCycleTrace/Dashboard/vendor/chartjs-plugin-zoom.min.js` is
[chartjs-plugin-zoom](https://www.chartjs.org/chartjs-plugin-zoom/) v2.2.0, MIT License,
Copyright (c) 2016-2024 chartjs-plugin-zoom Contributors. Both are unmodified upstream
distribution builds and keep their own license headers in the file; this repository's MIT grant
does not replace theirs.

They are vendored rather than fetched from a CDN because a generated dashboard is a shareable
offline artifact. Loading them at view time made every chart depend on the reader having network
access at the moment they opened the file, and a dashboard opened without it fell back to a raw
JSON dump.

## Extracted game data

`data/entity-display-names.tsv`, `data/entity-mappings.tsv`, and `data/source/message.txt`
contain identifiers, internal asset names, managed type names, and player-visible display
strings extracted from Orb of Creation. They are included so that this project and its tools can
resolve game entities by stable identity rather than by guesswork, which is a prerequisite for
interoperating with the game safely.

That content is not this project's to license. It remains the property of MarpleGames and is
**not** covered by this repository's MIT grant, which applies to the project's own source code.
It is data about the game rather than executable game code or a game asset: no game code, assets,
art, or audio are included in those data files, and none of the data is redistributed in the
release package. The separately distributed metadata-only assemblies are described above.
