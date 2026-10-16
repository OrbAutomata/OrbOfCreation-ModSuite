# Decompiling workflow

[Back to index](README.md)

Four sources answer four different kinds of question. Using the wrong one produces a claim that
cannot be defended.

| Source | Answers | Cannot answer |
|---|---|---|
| `dotnet run --project tools/OrbModding.IlInspect -- --game-dir <path> <verb> <query>` over the installed assemblies | signatures, inheritance, call order inside a method, formula shape | which assets exist, what is attached to them |
| AssetRipper + the `sharedassets0.assets` type tree | serialized field values, asset-to-asset references, authored names and icons | anything computed at runtime |
| [`data/entity-*.tsv`](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/data/README.md) + `tools/find-entity.py` | UUID ↔ managed type ↔ internal name ↔ display name | relationships, state |
| the live Game MCP against a running perf-debug build | current registry population, live values, whether a call is admitted | why the game answered that way |

The inspector reads `Assembly-CSharp.dll` from the game's `Managed` directory unless told
otherwise. `--assembly` names any other DLL the game ships: a file name for one beside it in
`Managed`, or a path from the game directory for one elsewhere —
`--assembly BepInEx/core/BepInEx.dll` pins a loader contract in place, with references still
resolving from `Managed`.

The in-game differential verifier sits across all four: it recomputes costs, rates, modifiers,
affordability, accessors, and structure/upgrade requirements from decompiled formulas and compares
each against the game's own answer, entity by entity, on a live save. It is how a formula read out
of IL becomes a formula you can act on.

## The order to use them in

1. **Start from a UUID, never a name.** `tools/find-entity.py "Improved Alchemy" --costs` resolves
   a label to a UUID once, at authoring time; the UUID is what you carry forward.
2. **Confirm the managed type** against the mapping before believing anything else about the
   object. A name that resolves to the wrong type means you found a different asset.
3. **Read the type's IL** for the members you need. Follow the concrete → type → registry shape
   described in [type-model.md](type-model.md) rather than searching for a convenience API; the
   assembly usually does not have one.
4. **Read the serialized assets** for what IL cannot hold: which types an asset is a member of,
   what a prerequisite graph actually contains, which effect scripts hang off a recipe.
5. **Probe the live registry last**, to confirm population and timing. Registry presence is not
   availability, and a lifecycle boundary can replace the object behind a stable UUID.

## Citing what you found

Cite methods and fields **by name** — `StructureSO.Purchase(bool)`,
`Prerequisites.Container.Check()`. Do not cite metadata tokens (`0x06001292`), IL offset ranges,
or line numbers in a serialized-data dump. All three are baseline-scoped: they change with any
recompile of the game, they cannot be checked without reproducing the exact dump that produced
them, and a reader on a different platform pair cannot use them at all. A name survives a rebuild;
a token does not.

When a finding depends on which platform pair it was read from, say so — see
[audited-build.md](audited-build.md).

## Labelling what a claim rests on

Use one vocabulary for how well a fact is established, so a reader can tell a signature from a
guess without reading the surrounding prose:

| Label | Meaning |
|---|---|
| `Unresolved` | required facts are missing, unknown, or in conflict |
| `Inferred` | follows from other evidence, but has not been observed or audited directly |
| `RuntimeObserved` | the native type, identity, registry, or relationship was read successfully at runtime |
| `SerializedAssetVerified` | verified from the canonical serialized-asset mapping |
| `StaticallyVerified` | the exact managed signature or implementation fact is verified from audited metadata or IL |

Contradiction is independent of level: two strong facts that disagree make the conclusion
`Unresolved`, not "mostly verified". Display names are never evidence — they are diagnostics, and
they cannot upgrade a claim.

## Exploring an unfamiliar system

1. Resolve the known UUID through `IdScriptableObject.RuntimeLookup`.
2. Confirm the runtime type matches the mapping.
3. Traverse explicit code-level references: type lists and registered-member collections.
4. Inspect `GetAllModifierReferences()` and the property accessors on upgradeable objects.
5. Inspect the contributing modifier records and their stable modifier UUIDs.
6. Observe the related list variables for live instances and state.
7. Use names only for logging and human-readable configuration.

That order produces a relationship graph you can check for double application, instead of a
hard-coded per-feature list that nobody can verify.
