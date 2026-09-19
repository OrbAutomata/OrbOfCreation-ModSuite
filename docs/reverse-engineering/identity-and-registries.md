# Identity and registries

[Back to index](README.md)

## IdScriptableObject

Game entities derive from `IdScriptableObject`, which derives from Unity `ScriptableObject`.

```csharp
static Dictionary<Guid, IdScriptableObject> RuntimeLookup;
GuidContainer guidContainer;

Guid GetGuid();
Guid GetId();
static IdScriptableObject GetInstance(Guid guid);
static T GetInstance<T>(Guid guid);
static List<IdScriptableObject> GetAllInstances();
```

`GetInstance(Guid)` looks the UUID up in `RuntimeLookup` and returns the object; the generic
overload casts the stored object to `T`. `GuidContainer` is what carries the persistent UUID onto
the asset, and it is the same UUID that appears in saves.

```text
persistent UUID → GuidContainer → IdScriptableObject → RegisterObject() → RuntimeLookup
```

### `GetGuid()` is a field read — `StaticallyVerified`

The collector calls `GetGuid()` on roughly every entity in every registry, every pass, so what the
body costs decides whether identity has to be hoisted into a lifecycle-scoped index. It does not:
the whole chain is three non-virtual calls onto one field, with one null guard and no allocation,
no parse, and no write.

```text
IdScriptableObject.GetGuid() : Guid
  return GuidContainer.op_Implicit(this.guidContainer);

GuidContainer.op_Implicit(GuidContainer) : Guid
  return GuidContainer.GetGuid(that);

static GuidContainer.GetGuid(GuidContainer c) : Guid
  return c == null ? Guid.Empty : c._guid;
```

The string form is never touched on this path. `GuidContainer.sg` is parsed into `_guid` once, in
`OnAfterDeserialize()`, which Unity runs at load; a container deserialised from an empty string gets
a fresh `Guid.NewGuid()` there rather than on any later read. So a per-pass `GetGuid()` costs a
field load, and hoisting identity out of the capture loop would buy nothing.

## Why the registry, not the scene

The registry is stable across UI layouts, does not depend on Unity object names, uses the same
UUIDs the save uses, and supports typed lookup. A scene search has none of those properties.

It is not populated during the earliest part of plugin `Awake()`. Wait until game initialization
has run — `GameManager.InitGame()` postfix is the reliable point — and re-resolve after every
boundary in [architecture.md](architecture.md).

Registry presence is not availability and not completion. Locked content stays registered and
becomes active later, so "is it there" and "can I act on it" are two separate questions.

## Resolution rule

The canonical mapping holds 2,818 rows and 2,818 unique UUIDs across 141 managed types — but only
2,777 unique internal names. 39 labels are reused, covering 80 rows: `SpellDuration` names a
`DoubleVariable`, a `ModifierListVariable`, and a `ScalingWeightSO`; `WorkshopStructures` names a
`StructureListVariable`, a `StructureTypeSO`, and a `ViewSO`; `Arcane` and `Dragon` each name both
a `GlyphSO` and a `SpellTypeSO`.

So:

1. **UUID** is identity.
2. **Managed type** is the validation boundary.
3. **Name** is diagnostic metadata, and nothing else.

Configuration stores a UUID plus, optionally, an expected type and name for readable validation
errors. A failed type check disables that one feature and logs the mismatch — it never falls back
to a same-named object, because a same-named object is a different object.

Display names collide harder than internal names; see [naming-traps.md](naming-traps.md) for how
a player's word resolves to exactly one managed type.

For scale before you walk a registry: the mapping holds 229 `UpgradeSO`, 180 `StructureSO`, 80
`ResourceSO` and 68 `ConsumableSO` rows, against an observed live action-queue capacity of 304.
Those are authored populations, not live availability; the per-type census is
[`data/entity-types.tsv`](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/data/entity-types.tsv), and the build-pinned scan census is
[`data/game-data-census.json`](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/data/game-data-census.json).

## Base-class chain

```text
UnityEngine.ScriptableObject
  └─ IdScriptableObject
      └─ TooltipableObject
          └─ UpgradeableObject
              └─ ResourceSO
```

`TooltipableObject` adds display name, description, icon, colour, and search terms — presentation
data hanging off the asset, while identity stays GUID-based. `UpgradeableObject` adds the modifier
accessor surface. What each layer affords is in [type-model.md](type-model.md).
