# Naming traps

[Back to index](README.md)

The words the game shows a player are not the words the code uses, and in one case they are
actively inverted. Anything authored from observation — milestone tables, embargo lists, curated
priorities — arrives in player vocabulary and has to land on the right managed type. Getting the
translation wrong silently targets the wrong two hundred entities.

## The translation

| Managed type | What a player calls it | Where they see it |
|---|---|---|
| `StructureSO` | **Attribute** | levelled things: Cauldron, Machinery, Hydro Aura |
| `StructureTypeSO` | Attribute **category** | Alchemist, Swift, Grove, Workshop |
| `UpgradeSO` | **Upgrade** | the Upgrades screen; one-shot purchases |
| `AttributeSO` | **Statistic** | the Statistics tab |
| `ResearchSO` | Research | the Research screen |
| `ResourceSO` | Resource | the resource bar |

Read the first two rows and the fourth together. `StructureSO` and `AttributeSO` translate to
words that each sound like the other one, so "attribute" in a player's sentence almost always
means `StructureSO` and almost never means `AttributeSO`.

The evidence is serialized, not inferred from naming. `StructureTypeSO.GlobalStructureType` has
`displayName = "All Attribute"`, and every other `StructureTypeSO` describes itself as a group of
attributes — `AlchemistStructures`: *"Attributes related to your ability to transmute and
create."* Individual structures do the same: `Upheaval`: *"Reduces the cost of all attributes."*
The `ActiveActionables` list, which holds structures under development, is displayed as
*"Active Attributes"*. On the other side, the `ViewSO` displayed as "Upgrades" declares
`relevantLists = ["AllUpgrades"]`, an `UpgradeListVariable` — the view is bound to the type
directly. And 113 of 211 `AttributeSO` records carry `displayTypeRef.displayType = "Statistic"`
(63 are `Information`, 34 are `Action`, and one names none), while the `ViewSO` named
`PlayerStatsAttributes` displays as "Statistics". `AttributeSO` has no cost and no prerequisite
fields, so it is never a purchase target. The suite publishes the glossary as the `statistics`
world category.

## Concepts are alchemy recipes

There is no `ConceptSO` class in this build. Player-facing Scholar Concepts are implemented on the
same `AlchemyRecipeSO` and `AlchemyTypeSO` classes as ordinary alchemy, so **runtime type alone
cannot tell you the gameplay domain**.

The only discriminator is the `ConceptRecipes` registry
(`c8ff8e01-c042-49c2-86a2-e374f82c280c`, an `AlchemyRecipeListVariable`): a recipe is a Concept if
and only if it is a member. Live concept instances are in `ActiveConcepts`, an
`AlchemyInstanceListVariable`. Reductive, Reflective, and Conceptualization are `AlchemyTypeSO`
assets alongside Alchemy, Brewing, Dismantle, Enchantment, Refinement, and Transmutation.

Filter those registries. Do not scan every alchemy recipe, and do not classify by class name or by
asset name.

## Smaller traps

- **`Divination` is displayed as "Divining".** The spell tag is `Divination` in the serialized
  taxonomy and never appears that way on screen.
- **Glyph and Recipe Book name the same objects.** The game's own item tooltips call the
  pool-unlocking kind Recipe Books while the upgrade tooltips call the very same things Glyphs.
  [`game-systems/vocabulary.md`](../game-systems/vocabulary.md) records which screen says which.
- **A screen label is not an entity label.** Twelve of the eighteen within-type display-name
  collisions are `ViewSO` assets, including "Upgrade", which names three separate views. Resolve a
  screen through the translation table above, not through the display-name file.

## Resolving a label to an identity

Display names are for human authoring only, they are diagnostics, and they can never upgrade a
claim's evidence. A name resolves to a UUID once, at authoring time, and the UUID is what gets
stored.

```bash
tools/find-entity.py "Improved Alchemy" --costs
```

Display name alone is not a key — 152 labels are shared. Display name **plus managed type**
effectively is: `ResourceSO`, `StructureSO`, `UpgradeSO`, and `ResearchSO` have no within-type
collisions at all. Since the translation table turns a player's word into exactly one managed
type, a screenshot label plus the screen it came from resolves uniquely. The 18 within-type
collisions are 12 `ViewSO`, 5 `AttributeSO`, and 1 `PlotNodeActionSO` — none of those three types
is purchasable, so none of the 18 is ever a strategy target.

The labels themselves live in [`data/entity-display-names.tsv`](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/data/entity-display-names.tsv).
