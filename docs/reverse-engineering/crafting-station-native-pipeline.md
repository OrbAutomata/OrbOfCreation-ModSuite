# Brewing Station native pipeline

This dossier audits the player-visible Brewing Station controls in Orb of Creation v1.0.5. It
covers the two ingredient selectors, output selector, level dial, and Brew/Stop lifecycle. Crafting
recipe queues belong to `game_craft`; ordinary Alchemy recipe instances belong to `game_alchemy`.

## Runtime object, not authoring data

The pinned assembly is
`artifacts/game-v105/Orb Of Creation_Data/Managed/Assembly-CSharp.dll`. The screen binds one
runtime `CraftingStructure` instance held by a `CraftingStructureSO`. The authoring object does not
carry the player's staged selectors or active state.

`CraftingStructureSO.instances` is a `CraftingStructureListVariable`, not a CLR list. Its public
`GetAll()` returns `List<CraftingStructure>`. The suite therefore binds the wrapper and method
explicitly; treating the field itself as `IList` is not an equivalent shortcut. Each admitted
station must also satisfy `station.get_reference() == owning CraftingStructureSO` and retain its own
`CraftingStructure.GetGuid()` identity.

## Selectors and recipe resolution

`UIBrewingStation.PostSetup()` (`0x06002674`) builds the screen from:

- `CraftingStructureSO.ingredientLists[0].GetElements()` for the resource selector;
- `ingredientLists[1].GetElements()` for the glyph selector;
- `CraftingStructure.GetOutputList()` filtered by `IsOutputVisible(TypeElement)` for output;
- `GetIngredient(0/1)`, `GetOutput()`, and `IsLoaded()` for current state.

The two generated ingredient callbacks call
`CraftingStructure.SetIngredient(int, TypeElement)` (`0x06000E12`). The screen's output callback
calls `CraftingStructure.SetOutput(TypeElement)` (`0x06000E14`). These native setters stop an active
brew and recompute the matched recipe. Selecting an output may therefore rebuild both ingredient
selections; the settled station row, rather than the submitted field alone, is the truthful
post-state.

`TypeElement.GetTooltipable()` declares `ITooltipable`, although the supported resource, glyph, and
consumable values are identity-bearing `TooltipableObject` instances. Identity extraction binds
that declared return type and checks the concrete identity base before calling inherited
`IdScriptableObject.GetGuid()`. A future non-entity tooltip value yields no selectable UUID and
fails closed. `CraftingStructure.recipeId` is a `GuidContainer`; it is not a raw `Guid`.

The selector sentinels are the game-written selected element UUID returned by `GetIngredient` or
`GetOutput`. Availability and output visibility are admission facts, not postcondition ledgers.

## Level and activation

`UIBrewingStation.ChangeSelectedLevel(int)` (`0x0600267F`) calls
`CraftingStructure.SetSelectedLevel(int)` (`0x06000E1F`). The screen exposes the native
`GetMinSelectedLevel()` through `GetMaxSelectedLevel()` range. Changing the level stops an active
brew and recalculates the station's scaling and drain. The sentinel is the game-written selected
level.

`UIBrewingStation.ToggleBrewing()` (`0x06002682`) reads `IsActive()` and calls
`SetActive(!IsActive())` (`0x06000E1B`). The Brew control is interactable only for a loaded recipe.
Starting therefore requires `IsLoaded()` and inactive state; stopping requires active state. The
single sentinel is `IsActive()` reaching the requested boolean.

## Read surface and risk

The immutable station row contains current ingredients, output, loaded/active state, level range,
available selector options, and `GetCurrentDrain()` resource rows. Drain amounts are planning data;
they are not payment deltas and never verify a mutation. Each option and resource is rendered from
the same world generation's identity catalog.

Selector and level changes are reversible staging actions but stop an active brew. Stop interrupts
production. Start commits an ongoing resource drain and output loop. Every mutation resolves UUID
plus exact `CraftingStructure`, rechecks the screen-visible admission on Unity's main thread, takes
the family permit last, and faults only when the requested selected value, level, or active state is
absent.

## Disposable-save live checklist

1. On a copied save, compare one Brewing Station row with both ingredient selectors, output,
   loaded state, level range, Brew/Stop label, and displayed drain.
2. Select each ingredient direction and verify the screen and settled row agree, including any
   recipe/output invalidation performed by native recomputation.
3. Select an output and verify the native callback restores its matching ingredient pair and the
   settled row returns all three values.
4. Attempt an unavailable ingredient and hidden output; verify refusal occurs before mutation.
5. Change level at both native bounds; verify the screen dial, settled level, stopped state, and
   recalculated drain.
6. Attempt an out-of-range level and verify an ordinary refusal.
7. Attempt Start with an incomplete recipe and verify no active transition.
8. Start a loaded recipe, verify Brew becomes Stop and the current drain matches the screen.
9. Stop it, verify production becomes inactive without inferring refund or resource deltas.
10. Cross a lifecycle boundary and verify a stale request refuses without a native call.
