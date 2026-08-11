# Type model

[Back to index](README.md)

Every gameplay domain in this assembly repeats one shape. Learn it once and resources, structures,
spells, alchemy, rituals, research, equipment, passives, time runes, harvesting, and combat all
read the same way.

```mermaid
flowchart LR
    Concrete["Concrete asset<br/>recipe / object / action"] -->|"one or more type refs"| Type["Type asset"]
    Type -->|"registered members"| Members["Runtime member collection"]
    List["ListVariable / registry"] --> Concrete
    Effect["Upgrade / research / effect"] --> Ref["ModifierReference"]
    Ref --> Concrete
    Ref --> Type
    Ref --> Group["AttributeGroupSO"]
```

Four consequences carry all the weight:

- A concrete asset holds references to **one or more** type assets, and each type asset holds a
  registered-member collection pointing back. Membership is a set, not a hierarchy.
- Type assets are not global. They affect the instances registered with them — plus, for structures,
  the members of every subtype. `StructureTypeSO.subTypes` is a real type-to-type edge:
  `Initialize()` calls `RegisterSubType` per entry, which wires the parent's thirteen records into
  the child's thirteen. The covered set is the transitive closure over that edge, and on the audited
  build there is exactly one chain (`PrimalStructures` → Arcanist, Flameweaver, Stormshaper).
- List variables are **index surfaces**, not UI lists. They are where you find live instances,
  filtered views, and registries that the type assets do not expose.
- The same statistic can be reached through concrete → type → group → player-global layers, so a
  broad bonus can double-apply — and the exact place it does is now known. See
  [where a type bonus actually lands](#where-a-type-bonus-actually-lands).

## Base-class families and what each affords

| Family | Mapped types | What you can do with it |
|---|---:|---|
| `GenericListVariable<T>` descendants | 45 | enumerate a registry or a filtered collection |
| `UpgradeableObject` descendants | 32 | reach modifier accessors directly |
| `TooltipableObject` descendants | 24 | read/extend display data without an upgrade surface |
| direct `IdScriptableObject` descendants | 18 | variables, definitions, scaling assets, utilities |
| `EmptyTypeListVariable<T>` descendants | 9 | runtime instance, snapshot, and state collections |
| `StackableListVariable<T>` descendants | 5 | stack-aware gameplay collections |
| `AbstractItemRefVariable<T>` descendants | 4 | read the currently selected item |
| `AbstractVariable<T>` descendants | 2 | GUID and upgradeable-object references |
| `NumberVariable` descendants | 2 | `DoubleVariable` / `IntVariable` persistent modifier targets |

UUID lookup works for every registered `IdScriptableObject` descendant. Native upgrade-property
modification needs an `UpgradeableObject` accessor or an effect script aimed at another record.
Tooltip extension reaches `TooltipableObject` descendants even where they are not upgradeable.

The exhaustive per-type census lives in [`data/entity-types.tsv`](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/data/entity-types.tsv).

## Where the shape bends

The pattern is near-universal; these are the variations worth knowing before you traverse.

| Domain | Variation |
|---|---|
| Structures | one primary `structureType` **plus** a `structureSubTypes[]` list, so one structure can be reached by several type-level bonuses at once |
| Resources | `GlobalResourceType` excludes resources marked `excludeFromGlobals`; `GlobalCappedResourceType` takes only resources that report a maximum at registration |
| Research and upgrades | share prerequisite, cost, validation, purchase, save, and visibility concepts through **interfaces**, not a common concrete base — there is no one class to patch |
| Achievements | contribute raw values into an `AchievementStrength` `IntVariable` that then drives persistent effect blocks; it is a derived global, not a per-achievement surface |
| Combat | definitions are ordinary assets, but live state (status effects, engaged effects, targets) exists only in list variables and target containers |
| Concepts | have no class of their own; see [naming-traps.md](naming-traps.md) |

`AchievementSO.ApplyEffects()` adds that raw strength to `Player.GetAchievementLevel()` under the
achievement's own UUID and then applies the achievement's completion effects. An
achievement-driven bonus therefore attaches a narrowly scoped persistent effect to the
`AchievementStrength` `IntVariable`; multiplying `AchievementSO.GetTotalAchievementStrength()`
instead amplifies every existing consumer of the same derived global.

## Modifier layers and double application

A final statistic can receive contributions from the base record, concrete-object modifiers,
type-level merged modifiers, `AttributeGroupSO` merged modifiers, and player-global
`NumberVariable` effects. `AttributeGroupSO.BindAllMods()` binds serialized target records into one
`MergingModifierRecord` with ratio, exponent-ratio, and order-adjust delegates.

That is a list of layers, not an evaluation order — the actual arithmetic and fold order are in
[`game-systems/modifiers.md`](../game-systems/modifiers.md).

The hazard is that a single conceptual bonus ("more casting", "more manufacturing power") often
maps onto several of these layers at once and multiplies with itself. Before shipping a broad
bonus, log:

```text
source UUID and type
target UUID and type
propertyType[propertyIndex]
modifier UUID, ValueModifier type, value, and order
group/type ratio and prerequisites
current record contributors
```

That produces a graph in which double application is visible, rather than a guess based on names.

A worked example of the layering: `Spell.Initialize` loads glyphs and then establishes the spell's
types. `SetupLimitedElementalType` starts from the recipe's authored tags and may add or replace
elemental tags based on the equipped glyphs; `GetAllSpellTypes` combines the negative/exclusion and
augmented type lists. Spell calculations then request Power, CooldownSpeed, Cost and the rest from
**every** applicable `SpellTypeSO` via `SpellTypeSO.GetValueModifierRecord`. So a tag-targeted buff
applies according to the spell's *effective* type set, glyph changes included — never according to
its name or its authored tags alone.

## Where a type bonus actually lands

"All Cantrips +10%" is not a number stored on the Cantrip asset. A type SO carries a *record*, and
there are three kinds:

| Class | What it is | Has a value of its own? |
|---|---|---|
| `ValueModifierRecord` | a value: `baseValue` plus a folded memo | yes — `GetValue()` |
| `MergingModifierRecord` | a distributor with per-entry ratio/exponent/condition/order transforms | no; its total is `Adjust(100)` |
| `OrderedMultiplierRecord` | a distributor that first collapses itself to one multiplier per order | no; its total is `Adjust(100)` |

`MergingModifierRecord.Add` keeps its own copy and then pushes a transformed copy into every member
record registered by `AddRecord`; `OrderedMultiplierRecord` does the same after collapsing itself to
one `StackingRaw` modifier per order. Thirteen `Register*` methods wire those pairs —
`StructureTypeSO.RegisterStructure` alone wires thirteen of them, and the member records the suite
already collects are the identical set.

**So for a distributor, the type's contribution is already inside the member value.** A type total and
a member value are the same bonus counted twice, not two factors to multiply. The one class where
this does not hold is `SpellTypeSO`, which has no distributor field at all: `Spell.GetPower()`
multiplies `Spell.GetSpellTypePowerPercent()` in as an independent layer, aggregating
`GetPower().AsPercent()` over the spell's effective type set.

The distributor's *own* total — the `Adjust(100)` its tooltip prints — is absent from the member side
and from any serialized dump, because it is arithmetic over two dictionaries that are pure runtime
state. The suite folds it on the worker from the captured entries and publishes it as
`TypeModifierTotals`, under names (`DistributedTotal…`) that cannot be mistaken for a member value;
see [world collection](../runtime-architecture/world-collection.md).

A fourth record class is easy to miss: `ResearchTypeSO.levelRequirementAdjust` is a plain
`ModifierRecord`. It holds no value of its own like a distributor, but `RegisterResearch` wires only
`power` and `maxLevelCap` into members, so it distributes to nothing — and its total is therefore the
one that genuinely is *not* already inside a member value.

One boundary inside this stays `Unresolved`: `MergeEntry`'s `mod`, `expMod`, `condition` and
`orderAdjust` are `Func<>` delegates created at `AddRecord` time. IL proves *that* a transform is
applied and *where*; it cannot yield the ratio without invoking it. A claim of the form "this type
gives its members +X%" that assumes a ratio of 1 is a guessed magnitude, not a read one.

`UpgradeableObject.ModifierPropertyRecord` — the `PropertyRefs` member — is a property-*name* schema
rather than authored magnitudes: `propertyNames`, `modifierPropertyNames`, `effectPropertyNames` and
a display/tooltip descriptor per key, built in each class's static constructor. The string keys are
real (`SpellTypeSO.GetValueModifierRecord` is a 21-arm switch mapping `"Power"` → `power`), and they
are how an authored effect names its target property — which is why an authored magnitude lives on
the effect and not on the type asset.

## What IL cannot prove

Two boundaries return `Unresolved` from code alone, no matter how much IL you read:

- **`AttributeGroupSO` membership.** ILSpy proves how a group propagates modifiers into member
  records. It cannot prove which records are in the group — that is serialized asset data. Log the
  membership at runtime before enabling anything that combines overlapping groups.
- **The direction of a "scaling" stat.** "Scaling" is not one stat. Beneficial power/effect scaling
  and harmful cost/time/requirement scaling are reached through *different accessors*. A blanket
  scaling modifier is not safe, and neither is a blanket bonus on a `DoubleVariable` whose
  direction you have not checked.

The mapping proves an asset's UUID, name, and managed type exist. It does not prove which assets
reference each other, whether an entity is unlocked or visible in a loaded save, what a list
variable contains at a given lifecycle phase, or the balance impact of any modifier. Those come
from field metadata, decompiled methods, serialized assets, or a runtime probe.
