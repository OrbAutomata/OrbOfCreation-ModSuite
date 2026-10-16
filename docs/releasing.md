# Releasing Orb Of Creation ModSuite

Every change reaches `main` through a pull request. Humans and automation do
not push commits directly to `main`. GitHub Actions is the only owner of
publication tags and GitHub releases.

`VERSION` and `CHANGELOG.md` are the only tracked version sources:

- `VERSION` contains the current stable suite version as one stable SemVer
  value.
- `CHANGELOG.md` contains the curated section for that version and historical
  release sections. It has no `Unreleased` section.

`Directory.Build.props` reads `VERSION`, the suite project derives its SDK
assembly/package version from that property, and MSBuild generates the two
`PluginIds` version constants under the active `obj-*` directory. No generated
version source is checked in. BepInEx, the in-game version, assembly metadata,
packages, and publication assets therefore cannot drift from `VERSION`.

## Beta on each ordinary main merge

On every push to `main`, `.github/workflows/release.yml` asks whether annotated
or lightweight tag `suite-v$(cat VERSION)` already exists.

When it exists, the push is a beta. The workflow:

1. runs portable, profile, refs-contract, release-policy, and manifest-derived
   hygiene gates without a game installation;
2. builds the normal Release and perf-debug flavors from the committed refs;
3. uses `git describe` from the newest existing stable tag, excluding
   `*+main.*` beta tags, to count commits;
4. creates annotated tag `suite-v<VERSION>+main.<N>`; and
5. creates a GitHub prerelease with `OrbModSuite-release.dll`,
   `OrbModSuite-perf-debug.dll`, the install ZIP
   `OrbOfCreation-ModSuite-<VERSION>.main.<N>.zip`, and their checksum file.

The prerelease notes are the associated merged PR body. An empty body, no
associated PR, an API error, or invalid API response falls back to the merge
commit message; notes lookup never blocks publication.

## Full release through a release PR

The release PR is the promotion. The maintainer writes it by hand:

1. edit `VERSION` to the chosen stable SemVer;
2. add the curated, dated section for that exact version at the top of
   `CHANGELOG.md`;
3. run the private gates below; and
4. open a PR whose title starts exactly with `release:`.

There is no drafting helper and no local release script. No local tool commits,
tags, pushes, or creates a release.

The required `release-policy` PR check rejects `VERSION` or `CHANGELOG.md`
changes from every non-`release:` PR. A real version promotion must be valid
stable SemVer, strictly greater than the newest existing stable `suite-v` tag,
and carry exactly one matching changelog section in the same PR. Configure
`release-policy` as a required branch-protection check so violations cannot
merge.

Squash-merge the approved release PR without changing its `release:` title.
The squash commit retains that title as its subject, and the main-push policy
rechecks that every `VERSION`/`CHANGELOG.md` change belongs to a `release:`
commit.

Because `suite-v<new VERSION>` does not exist, the state-based classifier takes
the full-release path. CI builds both flavors once from the committed refs,
then creates annotated tag `suite-v<VERSION>` and the full GitHub release with
notes from the matching changelog section.

Classification never depends on the before/after edge that introduced
`VERSION`. If the stable tag remains absent, every later main merge attempts
that stable release again. Once the tag exists, later merges return to betas.

## Publication inputs

Both publication flavors compile from `lib/game-refs/v1.0.5` with
`OOC_GAME_DIR` absent and the SDK pinned by `global.json`:

- `OrbModSuite-release.dll` is the normal Release build and must contain no
  ServiceCycle profiling components.
- `OrbModSuite-perf-debug.dll` is the Debug profiling build.
- `OrbOfCreation-ModSuite-<version>.zip` is the player install: exactly
  `BepInEx/plugins/OrbModSuite/OrbModSuite.dll`, byte-identical to that run's
  `OrbModSuite-release.dll`. `tools/package-install-zip.sh` zips the CI-built
  DLL, verifies layout and bytes, and appends the ZIP to the checksum file. The
  CI `release-assets` job runs the same build and ZIP step on every PR.

The committed refs are full-surface metadata derivations of audited assemblies,
not the hand-written test stubs. Test stubs are never installed or published.
There is no SHA-in-tag attestation: stable-release trust comes from the
SHA-locked refs, the pinned SDK, and the checksums published with every
release.

## Private pre-release gates

Before approving a release PR, run [the gate](testing/README.md#the-gate) against the audited
game copy, then build the shipped flavor from the committed references:

```bash
PATH=/usr/local/share/dotnet:$PATH \
  env -u OOC_GAME_DIR dotnet build src/OrbModSuite.csproj \
  --configuration Release --disable-build-servers -m:1 --no-incremental \
  -p:EnableServiceCycleProfiler=false -p:ContinuousIntegrationBuild=true
```

Install and playtest the refs-built Release artifact—the refs-built bytes are
what ship:

```bash
./script/install release
```

Installed contracts remain the private proof against the real audited game.
CI does not claim metadata-only refs repeat that evidence. Complete the
applicable V0–V7 [runtime validation protocol](testing/runtime-validation.md)
and record test, contract, warning, configuration-schema, game/BepInEx, native
assembly, interactive, and known-limitation evidence in the release PR.

## Reference-change faithfulness

Real-game versus refs-built faithfulness is a reference-generation gate, not a
per-release ceremony. Whenever `lib/game-refs` is regenerated for a new audited
game version, run `tools/make-game-refs.sh`, clean-build the suite once against
the real audited closure and once against the regenerated refs, retain both
DLLs, and invoke:

```bash
dotnet run \
  --project tools/OrbModding.ReleaseAssemblyCheck/OrbModding.ReleaseAssemblyCheck.csproj \
  --configuration Release -- \
  /path/to/refs-built/OrbModSuite.dll \
  /path/to/game-built/OrbModSuite.dll
```

`ReleaseAssemblyCheck` permits only the known compiler debug-identity regions
to differ. Any IL, metadata, reference identity, resource, attribute, or other
byte difference blocks the refs update. Routine releases do not rerun it.

## Recovery runbook

All source fixes use PRs; recovery never directly pushes a commit to `main`.

- **Transient CI failure before tag creation:** re-run the failed workflow. If
  source changes are needed, merge a normal fix PR. With no stable tag, the
  next main push retries the stable release automatically.
- **Annotated tag pushed but GitHub release creation failed:** the tag makes the
  repository look released, so an explicitly authorized maintainer deletes
  that exact remote tag (`git push origin :refs/tags/suite-v<VERSION>`), and any
  local copy (`git tag -d suite-v<VERSION>`). The next PR merge retries the full
  release. Never move or overwrite the published tag.

An existing target beta tag or GitHub release remains a loud failure. There is
no get-or-create or overwrite path.

Local, publication-free helper checks are:

```bash
bash -n tools/release-common.sh
bash -n tools/check-release-policy.sh
bash -n tools/build-release-assets.sh
bash -n tools/package-install-zip.sh
tools/test-release-helpers.sh
```
