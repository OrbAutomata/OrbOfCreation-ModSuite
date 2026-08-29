# Authored game data

This directory stores the product-version-pinned serialized model extracted from Orb of Creation
and the small views consumed by the suite, documentation, and contributor tools. The game developer permits
distribution of extracted serialized data. Game binaries, assemblies, playable assets, decompiler
code output, and saves remain excluded from the repository.

## Provenance

[`game-data-manifest.json`](game-data-manifest.json) names the serialized game and Unity versions,
records the exact asset-file scope, and stores the SHA-256 and byte length of every generated
output. All generated files have one source: `cd tools && uv run orb-gamedata extract`.
Hand-edited or independently imported identity views fail `uv run orb-gamedata verify`.
The dataset does not pin a Windows or macOS assembly hash and does not retain source paths or Unity
path IDs. Equivalent installs of the same product version are expected to produce the same bytes.

The generated set is:

- `game-data.json` — every serialized content ScriptableObject and stat Variable, grouped by managed
  type and internal name, with managed references expanded and game-entity pointers resolved.
  UUID-less records remain present with `id: null` and stable content-derived keys.
- `progression-graph.json` — stable entity references, requirement groups and operators,
  `PrerequisiteLinkSO` tiers, bound owners, and consumers.
- `game-data-census.json` — version-pinned totals and per-type populations cited by tests and docs.
- `entity-mappings.tsv` — UUID, internal name, and managed type.
- `entity-display-names.tsv` — UUID, managed type, internal name, and player-facing display name.
- `entity-types.tsv` — entity count per managed type.
- `source/message.txt` — deterministic arrow-delimited interchange view of the identity catalog.

`known-entities.tsv` is the deliberately curated production subset used to generate runtime identity
declarations; it is validated against the full mapping but is not an extraction output.
`native-contracts.json` separately audits runtime managed-assembly and reflection/Harmony contracts.
It is not extractor admission or dataset provenance; maintain it through the
[native-contract workflow](../docs/testing/native-contracts.md).

## Census and identity rules

The committed full model contains 2,894 serialized content objects across 142 managed classes. The
identity catalog and progression graph contain 2,818 UUID-backed entities across 141 managed types.
The difference is deliberate: the full model preserves 78 UUID-less sound/UI objects, while the
catalog also includes two UUID-backed non-content classes. The identity rows contain 2,818 unique
UUIDs and 2,777 unique internal names: 39 names are duplicated across 80 rows. Identity therefore
has three separate roles:

1. UUID is the stable key.
2. Managed type is the validation boundary.
3. Internal and display names are diagnostics and presentation only.

The model contains player-facing display names on 2,274 rows. Display names collide more often than
internal names: 152 labels cover 363 rows. Display name plus managed type still has 18 collision
groups—12 `ViewSO`, 5 `AttributeSO`, and 1 `PlotNodeActionSO`—so lookup tools report ambiguity
instead of picking a row.

Add entities to the production subset deliberately; the full catalog is never part of the production
runtime API. The shipped plugin embeds neither table: the MCP server reads the live lifecycle
identity snapshot, and these files stay offline research and fixture-builder inputs;
`entity-mappings.tsv` remains the canonical row set, so display-only drift never invents an identity.

The model is authored static state. Live quantities, current visibility, runtime registry
membership, applied modifiers, queues, and save progress require a running-game observation.

## Use

From the repository root:

```bash
tools/find-entity.py "Improved Alchemy" --type UpgradeSO
tools/find-entity.py --uuid 67acd892-8a8a-455a-aa71-3fb06e75bf38
tools/find-entity.py mana --costs
```

The packaged equivalents are available from `tools/`:

```bash
uv run find-entity mana --costs
uv run orb-gamedata query --class ResourceSO
uv run orb-gamedata query --uuid 67acd892-8a8a-455a-aa71-3fb06e75bf38 --requirements
uv run orb-gamedata report atlas
uv run orb-gamedata report discovery-pools
uv run orb-gamedata report cost-curves
```

`tools/import-entity-display-names.py --source path/to/game-data.json` remains a compatibility entry
point for regenerating that one view. Normal refreshes use `extract`, which owns all views together.

## Refresh and verification

Extraction accepts a Windows install root, a macOS `.app`, or either platform's Unity `Data`
directory. It reads the installation and writes only the selected output and log directories.

```bash
cd tools
uv sync --locked --all-groups
uv run orb-gamedata extract --game-dir "/path/to/Orb of Creation"
uv run orb-gamedata verify
uv run orb-gamedata verify --game-dir "/path/to/Orb of Creation"
uv run pytest
```

Offline verification checks manifest hashes, provenance agreement, model/graph UUID equality,
object and class counts, the census, and every TSV/source view. Supplying `--game-dir` adds a fresh
read-only scan and requires byte-for-byte equality with the committed generated set.

Build the production subset using `tools/generate-known-entities.ps1` or
`tools/generate-known-entities.sh`. Every build verifies that its UUID, name, and managed type still
match the canonical mapping.

## Known example

```text
67acd892-8a8a-455a-aa71-3fb06e75bf38    AlchemicScroll    ResourceSO
```
