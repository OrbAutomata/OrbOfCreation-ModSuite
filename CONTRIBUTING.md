# Contributing

Thanks for helping improve OrbOfCreation-ModSuite. This is an unofficial BepInEx
5 project for the Windows Mono build of Orb of Creation.

## Branches and scope

Open small, focused pull requests against `main`; releases are tagged from it.
Release work follows both the
[release procedure](docs/releasing.md) and the
[release review checklist](docs/development/releases.md).

Keep each pull request to one coherent change. Commit subjects use a lowercase
area prefix followed by an imperative summary, for example `autoharvest: validate
prerequisites`, `ui: repair native navigation`, `build: isolate outputs`, or
`docs: remove retired guidance`.

Do not commit game binaries or assemblies, playable asset bundles, BepInEx binaries,
save files, local configuration, decompiler code output, credentials, or user-specific
paths. Product-version-pinned extracted serialized game data under `data/` is allowed with the
game developer's permission and is regenerated only through the repository tools.
Sanitize logs before attaching them.

## Engineering rules

Read the [engineering doctrine](docs/development/engineering-doctrine.md) and the
[game-boundary doctrine](docs/runtime-architecture/game-boundary-doctrine.md)
before changing runtime behavior. Preserve the game's progression, queues, saves,
and player control. Convert a live defect into a portable red-green regression
whenever its contract can be represented faithfully.

## Pull requests

Fill in the pull request template:

- `Why?` — the problem or motivation, in one to three sentences;
- `How?` — a short technical summary of the change;
- `Decisions` — one bullet per meaningful tradeoff, abandoned alternative, or
  scope choice, with the reason; `N/A` if none;
- `Callouts` — one bullet per spot a reviewer should look harder at, or that
  looks innocuous but has knock-on effects; `N/A` if none.

## Verification

Run [the gate](docs/testing/README.md#the-gate) before opening a pull request;
changes that touch native game boundaries also run its installed-game contracts
against your own game copy.

Portable success proves behavior against the source-only stubs, not Unity or the
installed game. Follow the [runtime validation protocol](docs/testing/runtime-validation.md)
for behavior that depends on native state, UI, Harmony, save/load, or player
control. Never install a stub-linked DLL.

By contributing, you confirm that you have the right to submit the contribution
under the repository's license.
