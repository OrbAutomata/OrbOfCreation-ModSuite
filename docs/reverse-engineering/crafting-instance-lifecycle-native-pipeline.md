# Crafting-instance lifecycle native pipeline

Audit target: Orb of Creation v1.0.5 `Assembly-CSharp.dll`, SHA-256
`46b723ad8e3df5adf7186ec32b220c338e26c1cc79369e01213c091155073bdc`.
The UI is the verb authority; this dossier covers the two instance controls exposed by
the crafting page after a recipe has been authored.

## UI routes

`UICraftingPage.ContextRecipeClick(CraftingRecipeSO)` has a distinct automation branch.
Its IL reads `craftingAutomationInstances`, then calls, in order:

1. `CraftingInstanceListVariable.GetQuantity(recipe)` (`0x0600164F`);
2. `CraftingRecipeSO.GetMultiBuyQuantity(current)` (`0x06000A2E`);
3. static `CraftingRecipeSO.CalcAutomatedQuantity(quantity)` (`0x06000A53`);
4. private `UICraftingPage.GetAutoCraftingQuantity(recipe)` (`0x060022F1`);
5. `max(calculated - currentAutomation, 1)` and then
   `min(GlobalVariables.GetMultiBuy(), remaining)`;
6. `CraftingInstanceListVariable.AutomateCraft(recipe, amount)` (`0x06001651`).

`ContextRecipeInteraction` admits this branch only when
`HasSpaceForAutomation(recipe)` is true. That helper permits either an empty automated
slot or an existing instance of the same recipe. It performs no resource-price or
payment check.

`UICraftingInstanceList.OnClickInstance(CraftingInstance)` owns both removal controls.
When `isAutoList` is true it calls
`RemoveAutomation(instance, GlobalVariables.GetMultiBuy())` (`0x06001652`). Otherwise
it calls `CraftingInstance.CancelCraft()` (`0x06000DE9`) and then removes the exact
instance from the list. The intervening cancel sound is UI feedback, not gameplay
state, so the GameAction invokes the same gameplay methods without synthesizing audio.

## Native effects and sentinels

`AutomateCraft` increases an existing instance through `AddAutomationQuantity`, or
constructs a new `CraftingInstance`, marks it automatic with `SetAuto(true)`, initiates
it, sets its automation quantity, and adds it to the automated list. The simplest
game-written sentinel is the exact instance's `GetAutomationQuantity()` increasing.

`RemoveAutomation` decreases that same quantity and removes the instance after it
reaches zero. Its sentinel is a decrease on the exact admitted instance. Manual cancel
calls `CancelCraft`, whose body calls `Remove()`; the UI then removes that exact object
from the manual list. Its sentinel is loss of exact reference membership.

No refund method appears in `CancelCraft` or either UI cancellation route. Refund
boundaries therefore remain unknown and are neither modeled nor verified. The family
checks stable recipe UUID plus exact `CraftingRecipeSO` type, the authored page relation,
the exact instance recipe identity, and the queue's automatic/manual classification.

## MCP contract and live promotion

`game_craft` retains `craft` as its compatibility default and adds explicit `automate`,
`cancel_manual`, and `cancel_automation` modes. The `crafting-recipes` row publishes
manual queued amount plus the automated quantity, capacity, and available controls.
Mutations return only the settled quantity transition.

A supervised disposable-save pass must compare one automation increment and decrement
to the visible multi-buy behavior, cancel one manual in-progress craft, verify automated
zero removal, and observe whether any refund occurs without treating it as a success
condition.
