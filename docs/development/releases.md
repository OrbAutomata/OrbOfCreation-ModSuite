# Public release checklist

[Runtime validation](../testing/runtime-validation.md) ·
[Release procedure](../releasing.md)

Every change reaches `main` through a PR. An ordinary merged PR publishes a
game-free beta when the current stable tag exists. A PR titled `release: ...`
that advances `VERSION` and adds its curated changelog section is the full
promotion. GitHub Actions alone creates tags and releases.

## Release PR

The maintainer writes the release PR by hand. It changes the two tracked
version authorities and may also change the release shape itself (published
assets, publication workflows, install docs):

- `VERSION`: one stable SemVer greater than the newest stable `suite-v` tag;
- `CHANGELOG.md`: one matching, dated, player-facing release section.

There is no `Unreleased` section and no promotion helper. The PR title starts
exactly with `release:`. Configure the `release-policy` status check as required
so ordinary PRs cannot change either file. Squash-merge the approved PR without
changing its title; the main-push policy uses the squash subject as a second
ownership check.

## Candidate review

Before approval, record:

- the exact commit and intended stable version;
- audited game, BepInEx, and source-assembly hashes;
- portable, profile, refs-contract, installed-contract, and interactive evidence;
- Release zero-profile-string evidence;
- Release, perf-debug, and install-ZIP asset names and SHA-256 values;
- configuration schema and reconciled test, contract, exemption, entity, and
  compiler-warning counts; and
- known limitations and validation scope.

Run portable tests first and installed contracts second, never in parallel.
Install and playtest with `./script/install release`; it installs the same kind
of refs-built Release artifact that CI publishes. Hand-written test stubs are
never an installation or publication input.

Installed contracts remain the private real-game proof. The
`ReleaseAssemblyCheck` comparison runs only with a regenerated refs closure;
see [reference-change faithfulness](../releasing.md#reference-change-faithfulness).

## CI publication review

For every main push, confirm the workflow:

- passed portable, profile, refs-contract, required promotion-policy, and
  manifest-based hygiene gates without a game installation;
- built `OrbModSuite-release.dll` and `OrbModSuite-perf-debug.dll` from refs;
- attached `OrbOfCreation-ModSuite-<version>.zip`, holding exactly that
  Release DLL as `BepInEx/plugins/OrbModSuite/OrbModSuite.dll`, and listed it
  in `OrbModSuite-SHA256SUMS.txt`;
- proved the Release flavor contains zero profiling components; and
- refused any pre-existing target tag or release.

If `suite-v<VERSION>` exists, verify a prerelease tag
`suite-v<VERSION>+main.<N>` whose count comes from the newest stable tag and
whose notes use the associated PR body or merge-message fallback. If the stable
tag is absent, verify stable tag `suite-v<VERSION>` and notes from the matching
changelog section.

`script/package` remains a local archive rehearsal, not a publication owner.
For partial tags or transient failures, follow the
[recovery runbook](../releasing.md#recovery-runbook).
