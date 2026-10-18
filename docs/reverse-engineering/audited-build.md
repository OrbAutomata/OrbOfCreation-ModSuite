# Audited build

[Back to index](README.md)

Every finding in this folder is scoped to one pinned pair of managed assemblies. A baseline names
its assemblies by SHA-256 and by nothing else — there is no module-id, version-string, or
timestamp admission. Assembly timestamps are diagnostics.

## The pinned baseline

| Item | Value |
|---|---|
| Game build | Orb of Creation v1.0.5-2 |
| Unity | `6000.0.70f1` |
| Runtime | 64-bit Mono / CLR 4.x |
| Mod loader | BepInEx `5.4.23.5` |
| Save format version | `6` |
| Main assembly | `Orb Of Creation_Data/Managed/Assembly-CSharp.dll` |
| Numeric assembly | `Orb Of Creation_Data/Managed/Assembly-CSharp-firstpass.dll` |

The admitted pairs are the `baselines` in
[`data/native-contracts.json`](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/data/native-contracts.json),
one per platform and build. `Assembly-CSharp.dll` differs between platforms;
`Assembly-CSharp-firstpass.dll`, which holds `BigDouble`, is identical.

A finding read from one platform pair does not prove equivalent behaviour on the other. Say which
pair a formula or IL order came from when the two could differ.

## What the manifest proves

[`data/native-contracts.json`](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/data/native-contracts.json) is the reviewable inventory of
every game member the suite resolves by reflection or patches with Harmony. It carries the
admitted hash pairs and, per member, the declaring type, visibility, staticness, return type, and
ordered parameter list. Installed-game tests validate it directly, and the same hashes are checked
against the runtime `GameAssemblyAudit` constants so the two cannot drift apart.

It proves that the selected members exist with the expected shape. It does not prove the internal
IL order of anything — resource deduction, queue insertion, notification dispatch, or completion
effects are behaviour, and behaviour needs IL reading or a runtime observation.

Serialized asset membership is likewise outside it: IL proves which condition types and formula
shapes are possible, not which graph is attached to any particular installed asset.

The active BepInEx chainloader banner establishes the loader runtime. The names of binaries
present in the loader directory do not.

## Re-baselining after a game update

1. Run `script/re-audit --game-dir <game root>`. It hashes the pair, compiles against it, and runs
   the gate and the installed-game contracts. A missing or reshaped member fails closed and names
   itself; that list is the migration work.
2. Re-read any IL-derived formula on this folder's pages that the diff touches. Cost chains,
   admission short-circuits, and rounding are the ones that move.
3. Run the in-game differential verifier against a disposable save to confirm parity for costs,
   rates, modifiers, affordability, accessors, and structure/upgrade requirements.
4. Admit the pair with the checklist `script/re-audit --game-dir <game root> --stamp` prints; it is
   the one admission procedure.
5. Re-import [`data/entity-mappings.tsv`](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/data/entity-mappings.tsv) if assets were added or
   removed; UUIDs are stable across builds, but the population is not.
