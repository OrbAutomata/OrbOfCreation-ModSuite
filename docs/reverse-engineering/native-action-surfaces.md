# Native action surfaces

[Back to index](README.md)

Each decompiled action path is given as the call sequence, the admission short-circuit in its real
order, the stable identities involved, and the constraints that make a naive call fail. Metadata
tokens are from the audited build ([audited-build.md](audited-build.md)) and die with a game update;
they identify a member, they do not replace resolving it.

The costs these paths charge are in [resources-and-bigdouble.md](resources-and-bigdouble.md).

---

## Active toggle-spell deactivation

`UISpellList.OnSpellFire(Spell)` is the visible spell-row button path. It checks the row's
interaction state, calls `Spell.CanFire()`, and then calls `Spell.Fire()`. The indexed
`SpellManager.FireSpellIndex(int)` route performs the same `CanFire()` then `Fire()` pair for the
spell occupying that loadout position, so the action can retain stable slot addressing without
inventing another deactivation path.

`Spell.Fire()` branches on `Spell.IsCasting()`. The inactive branch casts; the active branch calls
the static `SettingsManager.CanCancelSpells()` setting. When cancellation is allowed it calls
`Spell.EndCasting()` and `Spell.CancelDoubleCast()`; otherwise it only shows the game's settings
message and does not end the spell. `Spell.IsToggledSpell()` classifies the visible toggle/charm
cast types. Therefore an honest toggle-off action revalidates exact slot and recipe identity,
`IsToggledSpell`, current `IsCasting`, `CanCancelSpells`, and `CanFire`, invokes
`FireSpellIndex`, and accepts only `IsCasting` changing from true to false. No resource or cast
counter participates in that outcome.

Installed contracts pin the UI and indexed-wrapper calls to `CanFire`/`Fire`, and pin the native
`Fire` body references to `IsCasting`, `CanCancelSpells`, and `EndCasting`.

---

## The game counts manual casts per spell

`Spell.numCasts` is a private `System.Int32` on the runtime spell instance. `Spell.ExecuteSpell()`
is the only body that writes it: after the cast sound, it reads `Spell.activeData`, calls
`Spell.SpellCastData.IsManual()`, and on true loads `Spell.numCasts`, adds one, and stores it back
before granting casting experience through `Player.GetCastingExperience()` and mastery through
`SpellRecipeSO.GainMasteryExp(BigDouble)`. The unconditional counter next to it is global, not
per-spell: `ExecuteSpell` also calls `Player.GetSpellsCasted()` and `NumberVariable.Add(1)` for
every execution regardless of who triggered it.

`Spell.SpellCastData.IsManual()` compares the instance's cast-type field against zero, and
`Spell.Cast()` builds its data through `CreateCastData` with that same zero constant. So the cast
type the visible spell button produces is exactly the one `numCasts` counts, and every press that
reaches `Cast()` → `ExecuteSpell()` adds exactly one. Echoes and delayed repeats do not: they come
from `Spell.AddNewCastData(System.Double, ScalingInfo)`, which builds its data with cast type `2`,
and `Spell.Increment(System.Single, BigDouble)` loads those through `Spell.LoadSpellData` before the
execution that replays them.

That makes `numCasts` the per-press fact the game itself writes, which is why the world capture
reads it and `game_cast fire` publishes it as a before/after pair. It is a capture-only field: no
action writes it, and the toggle-off outcome above still turns on `IsCasting` alone.

---

## Two routers sit above several families

`UICostButton.OnClick` (`0x06002204`) is the paid-action router. It checks
`ResourceCostList.HasEnough()` (`0x06001E0F`), invokes `PerformCost()` (`0x06001E19`), and only then
invokes its configured callback. Every family reached through a cost button therefore pays **before**
the domain call, and the domain method itself neither prices nor charges. Re-driving such a path at
the data layer means reproducing that order on purpose: read and decide, then `PerformCost`, then the
domain method. The order is not universal — read the terminal method before assuming payment came
first, because at least one family commits before it pays.

`UILevelableItem.PurchaseLevel` (`0x06002467`) routes to `ILevelable.PurchaseLevel`, and
`PurchaseFreeLevel` (`0x06002468`) routes to `ILevelableHasFree.PurchaseFreeLevel`. One control
therefore levels several unrelated native types. Audited implementers are a partial list —
`GlyphSO`, `EquipmentTypeSO` (`0x06000B66`), `ResourceTypeSO`, `TimeRuneSO` (`0x06001847`) — so
enumerate the interface's implementations in the assembly rather than assuming this set.

---

## Purchasing structures and upgrades

The UI calls the levelled rows **Attributes**, but their concrete purchase target is
`StructureSO`. `UIStructureList.PostSetupItem` wires each row to
`UIStructureList.PurchaseStructure(StructureSO)` (`0x06002763`); its ordinary branch invokes the
parameterless `StructureSO.Purchase()` (`0x06001783`), which delegates to
`StructureSO.Purchase(bool)` (`0x06001784`) with `false`. The separate native `AttributeSO` type is
the Statistics-tab descriptor: it has no purchase cost, level, admission, or mutation method and is
not a player purchase target. A tool that added an `AttributeSO` purchase branch would therefore
invent a verb the UI does not expose.

### Surface

| Concern | Structure | Upgrade |
|---|---|---|
| Registry | `StructureSO.All` | `UpgradeSO.All` |
| UI entry | `UIStructureList.PurchaseStructure` (`0x06002763`) | `UIUpgradeButton.ClickUpgradeButton` (`0x06002944`) |
| Availability | `IsAvailable()` | `IsAvailable()` |
| Admission | `CanPurchase()` | `CanPurchase()` |
| Cost | `GetPurchaseCost()` → `ResourceCostList` | `GetPurchaseCost()` → `ResourceCostList` |
| Current level | `GetPurchaseLevel()` | `GetPurchaseLevel()` |
| Queued state | `GetQueuedQuantity()` | `GetQueuedPurchaseLevel()` |
| Mutation | `Purchase(bool)` (`0x06001784`) | `Purchase()` (`0x060018A6`) |
| Queue signal | `QueueBuild(int)` | `Purchase()` |
| Completion | `CompleteAction()` | `CompleteAction()` |
| Finite lifecycle | — | `HasFiniteLevels()`, `IsMaxLevel()`, `IsMaxQueuedLevel()` |

`QueueBuild(int)`, `Purchase()`, and `CompleteAction()` are the three viable Harmony targets on
this path: the first two are where queue state is mutated, the third is where it settles.

### `CanPurchase()` does less than its name suggests

This is the most misread member on the surface. It folds in **live requirements and queue
admission** — and neither availability nor cost.

```text
Structure:
    IsAvailable()  = prerequisites.Check()
    CanPurchase()  = prerequisitesPerLevel.Check(ConditionInfo(quantity))
                     && queue.HasRoom()

Upgrade:
    CanPurchase()  = !IsMaxQueuedLevel()
                     && purchaseCost.HasEnough()
                     && !IsMaxLevel()
                     && prerequisites.Check()
                     && prerequisitesPerLevel.Check(ConditionInfo(level + queuedLevels + 1))
                     && queue.HasRoom()
```

The orders matter because they short-circuit: an Upgrade that is at its queued maximum never
evaluates cost, and a Structure never evaluates cost at all. Availability and affordability are
your job on the Structure path.

Note that both `prerequisitesPerLevel` checks take the level as an argument — the level a purchase
would *reach*, which is `quantity` for a structure and `level + queuedLevels + 1` for an upgrade.
That is the one admission term that cannot be answered from a snapshot taken earlier.

A player never meets any of this, because the buy button runs its own preflight — per-level
prerequisite, affordability, queue room — and renders the cost line red instead of firing. Red is
the interface's check, not the data layer's. Driving the data layer directly, an attribute purchase
that cannot be paid for silently does nothing: admission passes (cost is untested there), and the
purchase's own per-level payment commits zero levels — no error, no queue entry, no message.

### Diagnosing a refusal

When `CanPurchase()` says no, decompose it by asking the game the readable terms separately:
`IsAvailable()`, `IsMaxLevel()`, `IsMaxQueuedLevel()`, and `GetPurchaseCost().HasEnough()` — the
game's own verdict on the price, not a re-pricing.

If only `HasEnough()` refuses, quantities moved after you read them; that is ordinary staleness.
If availability or a level cap refuses, your model of the entity is wrong. **If every readable
term passes, the parameterized per-level prerequisite is the refusing term by elimination.** You can
also ask that term directly: its one-parameter `Check(ConditionInfo)` overload is safe to call from
a read pass, unlike the parameterless `Check()` — see [requirements.md](requirements.md).

### The owning-view reachability chain

Player-facing reachability is a property of the owning `ViewSO`, not of the item: a natively
purchasable entity can sit inside a tab whose view is unavailable, and item-only reads cannot see
that gate. The authored ownership edge is indirect:

```text
candidate exact UUID + exact native type
  → exact StructureListVariable / UpgradeListVariable membership
  → ViewSO.relevantLists or ViewSO.availableLists
  → exact owning ViewSO
  → ViewSO.IsAvailable()   (driven by ViewSO.prerequisites)
```

For a structure, the resolver additionally proves `StructureSO.structureType` is an exact
`StructureTypeSO` containing that exact structure reference exactly once in its private
`structures` list. Both view list fields participate; the same view/list route appearing in both
collapses to one; different matching routes are ambiguous. A missing, unreadable, ambiguous, or
contradictory route is a named status, not an empty gap. The proving incident: `ConstructionAura`
(`6a361a01-8405-4fbc-9af1-42f471911d9e`) present and purchasable in the `ArtificerStructures` list
(`2c3b16bc-1eb4-4382-9d93-0d20f81f07a9`) while its owning `WorkshopArtificer` view
(`b8ebce37-ba04-42bc-b36d-63f7a7766a21`) was unavailable — the item itself carried no tab
prerequisite.

The authored route shapes: most content carries two. Either a parent tab and its subtab show the
same list (the *Witchcraft* attribute sits in a single Wizardry list shown by both Magic and
Wizardry), or a candidate sits in an aggregate list and a screen list at once (a Wizardry upgrade
in both the all-upgrades aggregate and Magic's own upgrade list — aggregates are legitimate routes,
not summaries). The persistent right-panel Upgrades / Inventory strip is genuinely global and
always available, a route in its own right. Single-route content is where things go missing:
*Life Weaver* is reachable only through World > Druidry, because World does not co-reference its
subtabs the way Magic co-references Wizardry.

### Making the mutation

`StructureSO.Purchase(true)` forces exactly **one** level and consults no multiplier;
`UpgradeSO.Purchase()` honours the global multi-buy. The same intent therefore produces two
shapes: a bulk structure buy is **N calls**, re-checking `CanPurchase()` before each level past
the first; a bulk upgrade buy is **one call** with `GlobalVariables.GetMultiBuy()` set to N and
restored afterwards (protocol in [modding-hooks.md](modding-hooks.md)). Both accept a queued delta
in `[1, N]` — either can commit fewer levels than asked for, any committed level is a success, and
only a zero delta is a miss.

### Disabling a structure

`UIStructureList.ToggleDisableStructure` (`0x06002765`) calls `StructureSO.ToggleDisabled`, and each
half is ordered: `DisableStructure` sets the disabled flag and then calls `RemoveEffects`, while
`EnableStructure` clears the flag and then calls `ApplyEffects`. The UI callback performs no other
admission or state write. The stable `disabled` flag is therefore the action's one outcome sentinel;
effect application is the native callback's consequence, not a second suite-owned ledger.

### Where an upgrade's reward lands

An `UpgradeSO` can carry authored `viewListAdditions`: `ViewListVariable.ListTuple` values whose
inherited `list` and `element` fields `UpgradeSO.ApplyListAdditions()` applies. Neither
`CanPurchase()` nor `Purchase()` inspects that destination, so a full one is admitted and charged
like any other purchase.

Traverse every tuple before paying. A tuple targeting an unbounded list needs no gate; a
capacity-bound one must match an audited exact list/max identity pair and answer live
`HasEmptySpot()`. The audited pair is the world-aspect destination `CreatedWorldAspects`
(`74ec1f90-e94c-4cd7-a1d0-7b35016b57ff`), whose `AbstractListVariable<ViewSO>.maxSizeVariable` is
the `WorldAspectSlots` `IntVariable` (`4b1bb2de-723a-4360-827c-8e4483f3ff8d`). An unknown pair, a
malformed tuple, an identity contradiction, or a full destination refuses before payment. The rule
has an origin: an aspect bought against zero free pedestals without this gate made the game
manifest an extra slot, and a later slot grant left one permanently empty.

### Queue and completion

Queue authority is `ActionManager.GetRemainingRoom()` together with
`ActionManager.instance.actionableItems.maxQueuedItems.AsInt()`. Read them as a pair immediately
before acting; capacity grows through ordinary progression without any lifecycle boundary firing,
and `Player.GetBulkDevelopment()` is a separate value that only shapes grouping.

Payment lands at queue time, not completion: the game prices the levels it is queueing at that
moment and charges as it queues, so queueing a large group spends its whole sum at once.

**The upgrade path never checks room per level.** One committed level is one queue slot —
`UpgradeSO.Purchase()` loops the multi-buy multiplier and `QueueAction` stacks once per committed
level — and that loop consults `GetRemainingRoom()` nowhere; its only per-level term is a
`HasEnough()` that breaks the loop once the next level is unaffordable. A caller holding slots back
must clamp its own request to `remainingRoom - reservedSlots`: with room 5, a reserve of 3 and a
request of 4, the admission check passes, four levels queue, and one slot is left. The structure
path is one slot per call, because `Purchase(true)` queues exactly one level.

A completion can unlock unrelated content — including content in the *other* registry. So
`CompleteAction()` returning invalidates more than the entity that completed, and a completion
postfix should trigger a broader re-read rather than a targeted one.

### Prerequisite breadth

Before traversing a prerequisite graph, know how wide it is. `Prerequisites.Container` holds
nested AND/OR graphs over conditions on Upgrades, Structures, Research, Resources, Numbers, Lists,
Views, Spells, Alchemy, Rituals, Equipment, prerequisite links, and generic upgradeable values.
Measure the subtypes actually attached to your candidates from the serialized assets; IL proves
only which are possible.

---

## Consumables

### Surface

Five verbs share the inventory list. Each one's UI route ends in a `ConsumableSO` member:

| Verb | UI route | Terminal member |
|---|---|---|
| Use | `UIConsumableRefList.ClickConsumable` (`0x0600229B`) | `SelectAndFire()` |
| Cancel one pending use | `UIConsumableRefList.CancelConsumable` (`0x0600229D`) | `CancelUsage` (`0x060009D2`) |
| Discard stock | `UIConsumableRefList.DiscardConsumable` (`0x0600229E`) | `Discard` (`0x060009D3`) |
| Randomize on/off | `UIConsumableRefItem.TurnRandomizationOn/Off` (`0x06002296`/`0x06002297`) | `SetRandomization` (`0x060009D1`) |
| Reorder | `UIConsumableRefList.OnDrop` (`0x0600229A`) | list `SwapPositions` → `UpdateObservable` |

Discard reads `GlobalVariables.GetMultiBuy()` for its amount and the native call clamps to
`min(requested, live amount)` — asking for more than you hold is not a refusal.

`CancelUsage` is identity work, not counter work: it obtains `ConsumableUsage.GetResultInfo`
(`0x06000DD1`), calls `EffectResultInfo.Cancel` (`0x06001BFC`), removes that usage, then calls
`PrepNextUsage`. The thing cancelled is one exact `ConsumableUsage`, so a queue count that fell by
one proves nothing about *which* pending use ended.

Reorder is same-list only. `OnDrop` guards on same-list and differing indices, swaps within the
authored `ConsumableRefListVariable`, and the hotbar then applies `SetAt`. Moving an item between
the inventory and the hotbar is not a native verb; there is no member to drive for it.

### Family is a set, not a subtype

There is no per-family class. Family is membership in `ConsumableSO.consumableTypes`, a set of
`ConsumableTypeSO` references:

| Family | UUID | Native name |
|---|---|---|
| Fruit | `46e0ab83-df7c-4f35-8012-3d9a3c97b753` | `Fruit` |
| Potion | `8103dae4-6945-4d18-b562-d2ffcd7ef49e` | `Potion` |
| Relic | `5d27b76e-eed3-49cc-a069-b9106000ede4` | `Relic` |
| Scroll | `70b36536-64e5-4f70-ad6f-af5787d719cc` | `ScrollConsumable` |
| Thread | `66a50127-5210-4a3a-93f4-952287858b90` | `ThreadConsumable` |

The set is not a discriminator, but an item's *operation* is single. The asset census finds 68
authored consumables across exactly eight membership patterns. In every multi-membership pattern
the second family records an acquisition channel or category — Fruit, Treasure, Modification,
Resource, Food — rather than a second operation, so a treasure relic and a fruit relic are both
just Relics. The only two-operation topology authored is the four permanent fruits (Blitz Berry,
Continuous Coconut, Frugal Fig, Power Pear) carried as both `Fruit` and `Relic`; they behave as
Relics. Encode that as the exact authored set — do not invent a general precedence rule for
combinations the game does not contain.

### Call sequence

```text
ConsumableSO.SelectAndFire()
  → CollectQuantity(GlobalVariables.GetMultiBuy())
    → CanFire()
    → consumeCost.PerformCost()
  → queuedQuantity++
  → Inventory.QueueConsumable(this)
  → an idle inventory begins preparing the queued item
```

Payment happens inside `CollectQuantity`, before the queue increment — so the observable edge of a
successful submission is stock −1 and queued quantity +1. Everything after that (preparation,
random target selection, the actual effect) is game-owned and is not evidence that the effect
landed.

`Inventory.CanUseConsumable()` is a **global** single-preparation gate: one item prepares at a
time, across every family, so any pending preparation refuses every other submission.

### Scrolls target structures

A Scroll needs `canBeRandomized`, then `SetRandomization(true)` confirmed by `IsRandomized()`. Its
authored graph contains exactly one `RequestTargetEffectScript`; that script's
`TargetSelectOptions.GetTargeting()` resolves to `Targeting.TargetStructure`, and
`TargetStructure.GetRandomList(ScalingInfo.Basic(level))` computes candidates against the
strongest owned level — filtering visible eligible structures, applying the serialized enchantment
matcher, and ordering deficient targets. **An empty list is a normal outcome**, meaning the Scroll
would not upgrade anything; it is not an error and not a reason to enter the manual targeting
branch.

### `Gain()` admission runs after payment

`ConsumableSO.Gain()` first clamps its positive incoming amount to `maximumCarryLoad` — a
non-positive carry limit therefore guarantees the entire gain is lost. At capacity, "weakest" is
defined by level only: an incoming level strictly above the weakest owned unit replaces it; an
equal level replaces a unit without changing level coverage; a strictly weaker level is
decremented to zero and silently dropped. Native `UICraftingPage.QueueCraft` pays
`CraftingRecipeSO.PurchaseQuantity` before construction, and instant and queued completion run the
identical `ConsumableGainEffect → ConsumableSO.Gain()` path — so the capacity decision always
happens after payment.

### Levelled stock

`ConsumableSO.consumableCounts` is a list of `ConsumableCount` rows, each with `GetLevel()` and
`GetQuantity()`. Aggregate quantity is the wrong question: use selects the **strongest** count and
carries that count's scaling through the effect. Read `GetStrongestLevel()`, `GetStrongest()`, and
`GetCountScalingInfo()` instead of summing.

---

## Harvest availability

Plot and action availability on the Agromancy screen is cached behind the interface: the plot
list's own render pass is what re-evaluates it, and a passed check latches on rather than being
re-tested. Growth and timers advance with the screen closed; only the availability evaluation is
screen-bound. An actually-available action can therefore read unavailable until the screen has
been opened once — the reason a published prerequisite latch is evidence only, and the exact
current action still gets one live validation at the action boundary.

---

## Crafting and scribing

### Identities

The recipe type is `ScribeCrafting`, `ee001474-8209-4238-9566-84899a877226`, a
`CraftingRecipeTypeSO`. The registry is `ScribeCraftingRecipes`,
`2917516f-34a5-47b7-85b2-0b2f9ab3a29f`, a `CraftingRecipeListVariable`, holding exactly six
serialized recipes: `CraftScrollAdvancement`, `CraftScrollDevelopment`, `CraftScrollEcho`,
`CraftScrollExcellence`, `CraftScrollLearning`, `CraftScrollPower`.

Each role is a recipe / Scroll / enchantment triple:

| Role | Recipe | Scroll | Enchantment |
|---|---|---|---|
| Advancement | `a4a02a8f-6573-411c-a30c-6d9bcee12605` | `5f6aa08d-7da6-4c7a-89c9-aabcfe48e886` | `0796ee25-e1f6-4c5c-abba-aad46e02318b` |
| Power | `9c0a2b96-45fa-4aca-83ba-8efad8895608` | `4bb8af50-fc7d-44a7-b1fc-937c390f8aec` | `b9d5f0f7-43fd-4bad-a8e2-8a73f2f1d1d6` |
| Learning | `49da8d21-0f6a-492e-bd9a-15531b1737d5` | `ec14ee5d-66a3-4b28-a271-25dca2414387` | `b74c2058-4113-4b6c-b11e-1c97304d236c` |
| Excellence | `6c5c36ea-4736-46d2-b961-6227d4cce5d3` | `49057abe-fe54-481e-99bc-2b82c3995c6b` | `7b17670e-b3b6-401f-83f7-9c0e6d157852` |
| Development | `b15690ab-828c-42b9-ad69-70f169a45961` | `09d6101a-460d-4ce9-b7d4-46c4abaeadb7` | `cb354ece-fd8c-4ffc-a67e-b24cc3fe5fa5` |
| Echo | `008ccaa9-da26-4b55-95a5-5bc5df9c62f0` | `164dbfa9-8b9f-4976-9d17-ad3ad6b07a62` | `d854b177-865f-45ee-97a3-23d904df1ba1` |
| Investment | no audited recipe | `da5eab6d-ab4c-4b32-aca1-2e83b6d3a64b` | `f75cea6e-5d21-439f-bce4-79199b22434d` |
| Speed | no audited recipe | `b2232a7d-5c97-44c9-9520-686e99fa8293` | `9f068bad-f3a0-47de-84f4-407e67622fe1` |

Investment and Speed are real Scroll and enchantment identities with no recipe in the registry —
the code-side half of the open question in
[`game-systems/open-questions.md`](../game-systems/open-questions.md). Do not
infer a production path for them from the existence of the identities.

A triple is proved by edges, never by names: exactly six exact `CraftingRecipeSO` values in the
registry, exactly one `ScribeCrafting` type reference on the recipe, `useQuantityAsLevel = true`
alongside `CraftingRecipeTypeSO.isLevelType = true`, exactly one `ConsumableGainEffect` output
naming that role's Scroll, exactly one `RequestTargetEffectScript` on the Scroll behind an exact
`TargetStructure` selector, and exactly one `EnchantItemScript` naming the role's enchantment. A
missing, extra, wrong-typed or contradictory edge leaves the relationship unknown; it never
degrades to a name-based mapping.

### Two routes, chosen by whether a page owns the recipe

`UICraftingRecipeList.ClickCraft` (`0x060022E1`) invokes the installed page callback when there is
one and otherwise calls `CraftingRecipeSO.Execute` (`0x06000A32`). Which route a recipe takes is a
property of the authored page relations, not a caller decision.

`Execute` is the synchronous direct composite — the one place crafting has a non-UI entry point:

```text
CraftingRecipeSO.Execute()
  → CanBuy()                          0x06000A2A
  → GetPurchaseQuantity()             0x06000A30
  → recipeCost                        0x040005F1
  → ResourceCostList.Multiply()       0x06001E3B
  → ResourceCostList.PerformCost()    0x06001E19
  → PassiveObservable.Channel.Update()
```

`GetEffectChannel().Observe("craft").GetObservableId()` is the direct route's directional outcome
sentinel: the audited composite increments it only after cost admission, so a strictly larger id
proves the synchronous native action completed without reconstructing a resource ledger.

The page route has no equivalent. `UICraftingPage.UIStart` installs `ContextRecipeClick` and
`ContextRecipeInteraction` and owns the authored recipe-list, queue, mode and main-type relations;
`ContextRecipeClick` reads the recipe's current queue quantity, obtains
`GetPurchaseQuantity(previous)`, then calls `UICraftingPage.QueueCraft` (`0x060022F0`), whose
sequence and consequences are in [ui-internals.md](ui-internals.md). Automating a timed recipe means
re-driving that sequence below the handler.

Three constraints follow, and each breaks a naive call:

- **The page relation is the queue.** A timed recipe with no authored page has no queue to join, and
  a recipe reachable from two loaded pages is ambiguous rather than a free choice. Prove that exactly
  one page holds the same recipe object before choosing a route.
- **Stack versus new instance is a native read.** `QueueCraft` reads the native craft mode and either
  calls `CraftingInstance.AddQuantity` (`0x06000DE2`) on the exact existing instance for that recipe
  or constructs a new one; the caller does not pick.
- **Queue room is branch-dependent.** `HasSpaceForCraft` permits a full queue in stack mode when the
  exact recipe already has an instance, and requires native list room otherwise. A blanket capacity
  check refuses work the game accepts.

Cost lineage differs by route too: the direct route prices `recipeCost.Multiply(purchaseAmount)`,
the page route `GetTotalCost(previousQuantity, purchaseAmount)`. Quoting one route's price for the
other misprices the craft.

### Level authority

`CraftingRecipeTypeSO` saves two distinct values: `startingLevel`, the manual-crafting selector,
and `maxStartingLevel`, the highest unlocked starting level.
`CraftingRecipeSO.GetStartingQuantity()` reads the **former**, while a `CraftingInstance` carries
its own `quantity`. Supply the level to the instance; do not write `startingLevel`.

Payment and progression are one composite:

```text
CraftingRecipeSO.PurchaseQuantity(purchasedQuantity, previousQuantity)
  → pay GetTotalCost(previousQuantity, purchasedQuantity)
  → CraftingRecipeTypeSO.SetMaxStartingLevel(purchasedQuantity + previousQuantity)
```

`SetMaxStartingLevel` keeps the **larger** of the old and proposed values, so for a one-shot
purchase with `previousQuantity = 0` the transition is `max(before, purchasedLevel)`. That ceiling
is shared across recipes; a cheap recipe raising it does not advance any other Scroll, whose own
frontier is its `maxCreatedLv`.

`CraftingRecipeSO.CanBuyAt(BigDouble)` is **monotonic in level** — bracket the first unaffordable
level and binary-search it rather than probing linearly.

### Instances and enchantments

Two `CraftingInstanceListVariable`s hold the live work: `ActiveScribeInstances`
(`b557060a-e109-40de-9a7d-f2b02bc9766d`) and `AutoScribeInstances`
(`f6cb65a8-a959-477c-9293-ff66f646c95d`). The game's own automation creates or updates one
repeating instance per recipe in the second; neither list retains caller ownership, so those
instances are observable but not editable by anyone else.

Queue ownership and `CraftingInstance.IsAuto()` normally agree, but the game does not make that
an invariant at insertion. `CraftingInstanceListVariable.Initialize()` normalizes existing items
with `SetAuto(isAutoList)`, `UICraftingPage.QueueCraft()` creates a non-auto instance, and
`CraftingInstanceListVariable.AutomateCraft()` calls `SetAuto()` before adding an automatic one.
However, inherited `GenericListVariable<CraftingInstance>.Add()` does not set the flag and the
public `CraftingInstance.SetAuto(bool)` remains mutable after insertion. World capture therefore
verifies both facts and fails loudly if an instance contradicts its containing queue.

The visible automated add route is
`UICraftingPage.ContextRecipeClick` → the recipe's multi-buy/automation helpers →
`CraftingInstanceListVariable.AutomateCraft`. Its increment is capped by the global
multi-buy. The instance-list click route calls `RemoveAutomation` for automated work;
manual work calls `CancelCraft` and then removes the exact instance from the queue.
`CancelCraft` contains no resource-cost or refund call, so cancellation success is queue
state only and refund behavior remains a live-validation question.

This mechanism was audited from Orb of Creation v1.0.5 `Assembly-CSharp.dll`, SHA-256
`46b723ad8e3df5adf7186ec32b220c338e26c1cc79369e01213c091155073bdc`, decompiled with
ILSpy 10.1.1.8388.

New queued work is proved by reference membership of the exact newly constructed instance, not by
finding any instance with the same recipe and quantity. Instant work has no queue destination;
`CraftingInstance.InstantCraft()` reaches `CompleteCraft`, whose monotonic sentinel for a one-shot
instance is `IsExpired()` changing to true.

Every `StructureSO` owns an `EnchantmentSO.EnchantTable`. A native enchantment upgrade keeps a
stronger existing instance and replaces an equal-or-lower one only when the proposed scaling is
**strictly** stronger under `CanUpgradeEnchantment`. An apply that appears to do nothing usually
proposed equal scaling.

---

## Research development

### Surface

| Concern | Member |
|---|---|
| Identity | `ResearchSO` |
| Develop | `UIResearchItem.DevelopResearch` (`0x060025F0`) → `ResearchSO.PurchaseLevel` (`0x060011B4`) |
| Route switch | `SettingsManager.IsResearchQueueMode` → `Develop` (`0x060011B5`) or `QueueDevelopment` (`0x060011B6`) |
| Pause / resume | `UIResearchItem.PauseResearch` / `ResumeResearch` (`0x060025F1`/`0x060025F2`) → the same-named `ResearchSO` members |
| Cancel | `UIResearchItem.CancelDevelopment` (`0x060025F7`) → `ResearchSO.CancelDevelopment` (`0x060011B7`) |
| Free bonus level | `UIResearchItem.AddBonusLevel` (`0x060025F8`) → `CanApplyBonusLevels` → `SubmitBonusLevel` (`0x060011BA`) |

`PurchaseLevel` is a dispatcher, not a verb: one setting decides whether the same click develops now
or queues.

### The queued route accepts a prefix, and its cost probe lies

```text
QueueDevelopment()
  → read native multi-buy
  → clamp against the authored maximum
  → per candidate level:
      GetDevelopmentCostAtLevel(level)
      ResourceCostList.Add(cumulative)
      cumulative HasEnough()
      IsWithinDevelopRangeAt(level)
  → ApplyResearchCost for the accepted levels only
  → an idle target immediately starts the first accepted level
```

Affordability is cumulative across the batch, so the accepted set is a prefix ending at the first
level that fails `HasEnough` or `IsWithinDevelopRangeAt`. **`ResourceCostList.Add` mutates the
cumulative list before the failing candidate is rejected**, so the object left behind after the loop
includes a level the game refused. Quoting it overstates the price; rebuild the accepted prefix in a
second pass through the same native cost members instead of returning the last probe.

Waiting levels live inside the research node. They are not entries in the global action queue, and
queue room is not a term in this admission.

### The other four verbs

The immediate route sets developing and active state, applies the research cost, recalculates derived
research data, and establishes its drain. Pause and resume change the active-drain state only, and
the interface exposes them **only with Research Queue Mode disabled**. Cancel clears active,
developing and waiting state, reapplies the cost calculation, recalculates, and calls
`ResourceFillList.ClearInvestment`. Bonus submission applies one self-bonus level and updates its
research-type usage; the compiler-generated predicate behind `CanApplyBonusLevels` asks each
associated `ResearchTypeSO.HasFreeBonusLevelsLeft`.

Which level a research node reports depends on the accessor called, and the prerequisite, completion
and cap paths do not agree — see [requirements.md](requirements.md).

---

## Ordinary Alchemy list lifecycle

The ordinary Alchemy page is one global ordered `AlchemyManager.activeAlchemy` list. It is not the
concept-assignment surface and it is not a per-recipe level selector. Cecil evidence from the pinned
v1.0.5 assembly fixes the visible routes:

| UI gesture | Native route |
|---|---|
| Click an ordinary recipe | `UIAlchemyRecipeList.ClickItem` (`0x0600217B`) → `AlchemyInstanceListVariable.EngageAlchemy` (`0x06001604`) |
| Click an active instance | `UIAlchemyInstanceList.ClickItem` (`0x06002167`) → `DisengageAlchemy` (`0x06001606`) |
| Drop an active instance | `UIAlchemyInstanceList.OnDrop` (`0x06002168`) → `AbstractListVariable<AlchemyInstance>.SwapPositions(int,int)` → `UpdateObservable()` |

`EngageAlchemy` first requires either list room or an existing instance. It then derives remaining
free and maximum uses from `GetFreeUsageSlots` / `GetMaxUsageSlots` for a new recipe, or the instance's
remaining-use accessors for an existing one. `GetUsageCost().MaximumCostTimes()` contributes the
resource ceiling, and `GlobalVariables.GetMultiBuy()` clamps the UI click before the private
`AddAlchemyInstances(AlchemyRecipeSO,int)` call. `DisengageAlchemy` similarly forwards its strip
value to `RemoveAlchemyInstances(AlchemyRecipeSO,int)`. The MCP deliberately does not mirror that
global UI preference: its explicit `amount` is admitted against the same live capacity facts and
then passed to these counted core methods, which own instance creation/removal and quantity change.

The list's game-written queued quantity is the simplest directional outcome sentinel: it must rise
after engage and fall after disengage. Reordering requires the same exact recipe to appear at the
requested index after the swap. Resource debits and free-use accounting are admission inputs, not
postcondition ledgers. The shared `AlchemyGameplayDomainClassifier` is the boundary between these six
ordinary families and recipes in the native `ConceptRecipes` list.

---

## Discovering a discoverable

`IDiscoverable` has exactly six implementers in this build:

| Type | `Discover` |
|---|---|
| `AlchemyRecipeSO` | `0x06000850` |
| `EquipmentSO` | `0x06000B10` |
| `GlyphSO` | `0x06000BFB` |
| `RitualSO` | `0x06001366` |
| `SpellRecipeSO` | `0x06001432` |
| `TimeRuneSO` | `0x06001858` |

The interface owns the whole read side of the decision: `Discover` (`0x06001C97`),
`GetDiscoverCost` (`0x06001C92`), `IsDiscoverVisible` (`0x06001C93`), `CanDiscover` (`0x06001C94`),
`IsDiscovered` (`0x06001C95`) and `IsDiscoverRequired` (`0x06001C96`).

`UIDiscoverablePage.HandleClick` (`0x0600231C`) hands the selected object to a `UICostButton` whose
terminal callback is that object's own `Discover`, so payment precedes discovery on this path.

Two implementers are not plain. `EquipmentSO.Discover` immediately calls `EquipmentSO.Create`
(`0x06000B11`) and equips nothing. `SpellRecipeSO` is driven through the spell manager's state
machine below, whose payment ordering is the reverse of this one.

---

## Discovery tree offers

### Surface

| Concern | Member |
|---|---|
| Registry | `DiscoveryTreeSO.All` |
| Visibility | `IsVisible()` (`0x06000AD5`) |
| Mode gates | `IsInIdleMode()`, `IsInCraftingMode()`, `IsInChoiceMode()` |
| Anything to offer | `HasCurrentlyRemMainPoolDiscoveries()`, `HasImmediateRequiredDiscover()` (`0x06000AC6`) |
| Start | `InitiateCraftingMode()` (`0x06000AA8`) |
| Select | `SelectItemId(Guid)` (`0x06000AAB`) |
| Confirm | `DiscoverSelectedItem()` (`0x06000AAC`) → `DiscoverItem(IDiscoverable)` (`0x06000AAD`) |
| Reroll | `RerollChoices()` (`0x06000AB1`), capped by `GetMaxRerolls()` (`0x06000ACB`) |
| Next price | `GetNextItemCost()` (`0x06000AB8`) |
| Offer resolution | `GetItemFromGuid(Guid)` (`0x06000ABF`) |
| State | `actionMode` (`0x04000643`), `actionTime` (`0x04000644`), `rerollsLeft` (`0x04000645`), `usedRerollsLastDiscover` (`0x04000646`), `currentChoiceIds` (`0x04000647`), `nextExcludedIds`, `selectedChoiceId` |

Offer UUIDs arrive wrapped: `currentChoiceIds` holds `GuidContainer` values read through
`get_guid()` (`0x06001B44`). The UI edges are `UIDiscoveryTreePage.OnDiscoveryClick`
(`0x0600232B`), `OnDiscoveryItemClick` (`0x0600232C`), `OnConfirmClick` (`0x0600232D`) and
`OnRerollClick` (`0x0600232E`).

### Initiating is synchronous; the offers are not

`InitiateCraftingMode` refreshes the earned reroll under a `Math.Min` against `GetMaxRerolls`,
clears `usedRerollsLastDiscover`, calls `FetchRarityLevels` (`0x06000AB3`), then `EnterCraftingMode`
(`0x06000AA9`) → `EnterMode` (`0x06000AA7`), which writes `actionMode` and `actionTime` and calls
`PassiveObservable.UpdateObservable` (`0x06001DD9`). That observable only increments an observed id
and invokes no UI code, so Idle → Crafting has no render dependency whatsoever: **a returning
initiate that leaves the tree Idle is a contract divergence, not a missing screen.**

Offers materialize later inside the game's own increment loop, where `IncrementCrafting`
(`0x06000AA0`) calls `EnterChoiceMode` (`0x06000AAA`) to create `currentChoiceIds` — and resets to
Idle if no choice can be made. Crafting is the outcome of initiating; a populated offer list is not.

`InitiateCraftingMode` does not pay. The cost button does, so a data-layer re-drive invokes
`PerformCost` itself, first.

### Selection and confirmation

`SelectItemId` assigns `selectedChoiceId` and nothing else. **Membership in `currentChoiceIds` is
the eligibility gate, not `CanDiscover`**: `CollectDiscoveryChoices` deliberately offers future
choices whose ordinary prerequisite verdict is false, so gating a selection on `CanDiscover` refuses
offers the game means you to be able to take.

`DiscoverItem` increments counts, removes rarity, resets mode, offers and selection, and calls the
target's `Discover` **last**. An exception part-way therefore leaves a real partial commit — counters
moved and tree reset, target not discovered — and the tree's own state cannot tell you which side of
that line you are on. Read the target's `IsDiscovered`.

### Reroll leaves the old selection behind

`RerollChoices` copies the current offers into `nextExcludedIds`, clears the offers, debits a reroll,
sets the used-reroll flag, and re-enters Crafting. It does **not** clear `selectedChoiceId`, so a
stale selection survives until new offers appear; drive `SelectItemId(Guid.Empty)` yourself if that
matters. Reroll is not offered on the immediate-required path.

### The ledger cannot verify this path

An audited initiate charged `4.4e3` and `8.9e6` against holdings of `2.1e19` and `5.7e23`.
`BigDouble` could represent neither subtraction, so both holdings stayed byte-for-byte unchanged
while the same call moved the tree from Idle to Crafting and offers duly appeared. A resource delta
is evidence about the ledger, never proof about the action — see
[resources-and-bigdouble.md](resources-and-bigdouble.md).

---

## Spell discovery and creation

### Surface

| Concern | Member |
|---|---|
| Manager | `SpellManager.instance` (`0x040004AD`) |
| Selection lists | `selectedCoreGlyphs` (`0x04000499`), `selectedAugmentGlyphs` (`0x0400049A`) |
| Loadout | `activeSpells` (`0x0400049C`), `AddSpell` (`0x0600074B`) |
| Recipe registry | `SpellRecipeSO.All` (`0x04000A32`) |
| Authored cores | `SpellRecipeSO.GetGlyphRecipe()` (`0x06001447`) |
| Selection resolution | `SpellManager.GetSpellFromRecipe(List<GlyphSO>)` (`0x06000747`) |
| Discover | `SpellManager.DiscoverSpell()` (`0x06000741`) → `SpellRecipeSO.Discover()` (`0x06001432`) |
| Create | `SpellManager.CreateSpell()` (`0x0600073F`) → `CreateRecipe` → `SpellRecipeSO.CreateWith(...)` |
| Costs | Discovery: `SpellRecipeSO.GetDiscoverCost()` (`0x06001442`); screen-priced creation: `SpellManager.GetSpellCreateCost(List<GlyphSO>)` (`0x0600074A`); lower-level modifier fold: `GlyphSO.GetCreationCostOfList(ResourceCostList, IEnumerable<GlyphSO>)` |
| Verdicts | `SpellRecipeSO.CanDiscover()` (`0x06001451`), `IsCreatable()` (`0x0600144F`) |
| Room | `EmptyTypeListVariable<T>.HasEmptySpot()` (`0x0600155C`), `GenericListVariable<T>.Empty()` (`0x06001569`) |
| Instance identity | `Spell.guidContainer` (`0x040007DF`) |

### Selection has no method

No native member represents the selection gesture. The UI drives list operations, and so must
anything else:

```text
resolve exactly one SpellRecipeSO from SpellRecipeSO.All
read its ordered GetGlyphRecipe() core sequence
require every entry to be an exact, available, non-augment GlyphSO
empty selectedCoreGlyphs and selectedAugmentGlyphs
append the authored core sequence in order
GetSpellFromRecipe() must resolve to the requested recipe, with zero selected augments
```

`GetSpellFromRecipe` is the only proof that a selection means what you think it means; a list of the
right length is not.

### Discovery commits before it pays

```text
SpellManager.DiscoverSpell()
  → GetSpellFromRecipe(selectedCoreGlyphs)
  → SpellRecipeSO.Discover()
  → ResourceCostList.PerformCost()
```

This is the audited counterexample to the cost-button order: the game itself can leave a recipe
discovered and unpaid. Preflight everything you can before entering it, do not reorder it, and do
not read a missing charge as a failed discovery.

`PostDiscoverRecipe` may auto-equip an instance, so a discovery can change the loadout without being
asked to.

### Creation is instance work

`CreateSpell()` resolves the current core selection and delegates to `CreateRecipe`, which consumes
the selected augments, calls `SpellRecipeSO.CreateWith(...)`, adds the result through `AddSpell`, and
then clears the selection. The interface's own gate is `GetSpellCreateCost(...).HasEnough()` beside a
discovered, `IsCreatable()` recipe and `activeSpells.HasEmptySpot()`.

**StaticallyVerified (macOS v1.0.5-2 baseline):** `UICreateSpellButton.Render` concatenates the
selected core and augment lists, resolves that full list with `GetSpellFromRecipe`, and passes the
same list to `SpellManager.GetSpellCreateCost`. The manager returns an empty cost when resolution
fails; otherwise it filters that list with `GlyphSO.IsSpellAugment` and folds only those augment
glyphs through `GlyphSO.GetCreationCostOfList`, starting from a new empty `ResourceCostList`. The
lower-level static combiner is therefore an implementation step, not an equivalent screen-pricing
entry point for an unfiltered core-plus-augment list.

**StaticallyVerified (macOS v1.0.5-2 baseline):** the Loadout list's row is a different button on a
different screen and reaches `CreateRecipe(recipe)` directly. That path reads only
`selectedAugmentGlyphs.GetStackedRecord()` — never the Recipe Book selection — and touches no
creation cost at all: `UISpellRecipeButton.RenderContent` disables the row on
`meetsNonLvReq && (item.HasMetUsageRequirements() || selectedAugmentGlyphs.Count <= 0) &&
usageCostList.HasEnough() && (activeSpells == null || activeSpells.HasEmptySpot()) &&
loadoutCompatible`, where `usageCostList` is `SpellManager.GetUsageCostOfSpell(currentSpell)`. So
loading a discovered spell into the loadout is priced by the usage allocation and by nothing else.

The result is a runtime `Spell` carrying its own non-empty `guidContainer` UUID. **Recipe UUID and
name are not instance identity**: two instances of one recipe are separate targets for every later
verb, and a loadout count that grew does not name what was created.

---

## Spell output level and augment composition

### Output is one global dial

`Spell.GetOutputLevel()` reads `Player.GetSpellOutputLevel()` (`0x06000690`), and `Spell.GetLevel()`
derives from that output plus the base-effect level. `UISpellInformation.SetSpellLevel()` calls
`Spell.SetLevel()`, but that member only recomputes cost — it owns no persistent per-spell level.
The truthful mutation is `IntVariable.SetValue(int)` (`0x060015AC`) on the one variable behind
`Player.GetSpellOutputLevel()`, read with `AsInt()` (`0x060015AE`) and bounded by
`Player.maxSpellOutputLevel` (`0x04000420`). Valid input is `1..maximum`, and it moves every spell at
once; a per-spell output level does not exist to be set.

### Augments are replaced whole

`Spell.SetAugmentGlyphs(StackedIdRecord<GlyphSO>)` (`0x06000FAC`) replaces both the UUID-reference
stack and the resolved-glyph stack, invokes `SpellRecipeSO.LoadGlyphs`, then recomputes spell cost.
There is no add-one or remove-one member: pass the complete intended stack, built through
`AbstractStackedRecord<GlyphSO, IdReference<GlyphSO>>.Set(GlyphSO, int)` (`0x060029E8`), and an empty
record clears. Read it back through `Spell.GetAugmentGlyphs()` (`0x06001075`) and
`Spell.GetQuantityOfGlyph(GlyphSO)` (`0x06001049`).

Admission is per glyph and then combined:

| Term | Member |
|---|---|
| exact glyph | `GlyphSO.All` (`0x040006D9`) |
| available | `GlyphSO.IsAvailable()` (`0x06000BB6`): when `discoverable` is true it returns the raw `discovered` field; otherwise it evaluates `prerequisites.Check()` |
| is an augment | `GlyphSO.IsSpellAugment()` (`0x06000BB8`) |
| count ceiling | `GlyphSO.GetMaxUsages()` (`0x06000BCE`) |
| combined compatibility | `GlyphSO.MeetsNonLvRequirements(List<GlyphSO>, Spell)` (`0x06000C0E`) |
| required mastery | `GlyphSO.GetMasterReqOfList(List<GlyphSO>)` (`0x06000C09`) against `Spell.GetRecipeMasteryLevel()` (`0x06001047`) |

The two combined predicates take the **expanded** list — counts unrolled into repeated entries — not
a distinct set. Passing distinct glyphs understates the stack and admits compositions the game
refuses.

### A glyph's visibility is its availability, and its list is its page

`GlyphSO.IsVisible()` is a call to `GlyphSO.IsAvailable()` and nothing else, and
`UIGlyphListItem.IsVisible()` loads its item and calls the same method (`StaticallyVerified`). The
game therefore cannot show a glyph it will not offer, and any surface reporting available ≠ visible
for a glyph is reporting its own defect.

`GlyphSO.IsDiscoverRequired()` has **no callers anywhere in the assembly** (`StaticallyVerified`), so
the authored `discoveryRequired` field — true for exactly two glyphs, Quick and Heavy, the only two
carrying a `discoveryCost` — decides nothing on this build and is not a population discriminator.

The population split is `GlyphSO.discoverable`, 22 true against 25 false, and it agrees row for row
with two other authored facts (`SerializedAssetVerified`): membership of `AugmentSpellGlyphs`, and
carrying an `associatedRecipeBook` (null for all 22, set for all 25). `GlyphSO.augmentsSpells` is
**not** the split — Distinct, Weak and Wrath are discoverable, book-less members of the augment list
that carry it false, so it divides 19/28. No `DiscoveryTreeSO` names a glyph directly — the one tree
that offers glyphs names the whole `AugmentSpellGlyphs` list — and each of the 25 non-discoverable
glyphs carries exactly one authored condition in `GlyphSO.prerequisites`: a
`ResearchRequirement`, an `UpgradeRequirement` or a `PrerequisiteLinkRequirement` — which
`Prerequisites.Container.Check()` evaluates at level 0, that being what the no-argument overload
passes to `ConditionInfo.Adjust`.

Which page shows a glyph is the list it is on, through the same `ViewSO.relevantLists` edge the
owning-view chain above uses. Nine `GlyphListVariable` assets ship; four are authored populations and
the rest are runtime selections plus the whole-catalogue `AllGlyphs` (`SerializedAssetVerified`):

| list | members | named by |
|---|---|---|
| `AugmentSpellGlyphs` | 22 | `MagicGlyphsUpgrade`, `MagicSpellbookLoadout`, `GlyphDiscoveryTree.discoverList` |
| `CoreSpellGlyphs` | 10 | `MagicSpellbook`, `MagicSpellbookLearn`, `BrewingStation.ingredientLists` |
| `CoreAlchemyGlyphs` | 12 | `AlchAlchemyDiscover` |
| `EquipmentGlyphs` | 17 | **nothing** — its only mention in the serialized graph is its own name |

The four hold 61 edges over the 47 glyphs, and no glyph is off all of them. Seven — Arcane, Dragon,
Expansion, Flow, Nature, Psionic and Storm — are on all three unlocker lists at once, and ten are on
`EquipmentGlyphs` alone. A view names a glyph list and never a glyph, so a glyph's page cannot be
read from the item: it is read from the list, exactly as a structure's tab is.

---

## Equipped-spell removal and reorder

**StaticallyVerified (macOS v1.0.5-2 baseline):** `SpellManager.RemoveSpell(Spell)` (`0x0600074C`)
carries the whole rule and applies it to itself. It returns immediately on `Spell.IsEmpty()`; then,
unless `Spell.IsAtMaxCharges() && !Spell.IsCasting() && !Spell.IsReadyingCast()`, it takes the
refused branch — error sfx, the popup "Cannot remove a spell that is still recharging.", and, when
`GetRechargeProcessor().HasType(0)` is false, the popup "Switching to time based cooldown." plus
`Duration.Processor.AddFlags(2)` and a new `MicroProcessor` core module — and returns without
removing anything. So a refused removal is not a no-op: it changes how the spell recharges. Only on
the admitted branch does it remove the instance from `activeSpells`, call `Spell.Destroy()`, then
`SpellManager.RecomputeSpellWeight()`.

`Spell.CanRemove()` (`0x06001038`) reads like that rule and is not it: its IL consults
`Spell.IsChargeAvailable()` and then `Spell.IsCasting()`, and no removal path calls it. Ask the
manager's own three questions before calling; answering from `CanRemove` refuses removals the game
would have allowed.

Reorder is a swap, not an insert. `UISpellList.OnDrop` (`0x06002701`) checks
`DragDropContext.ListsMatch()` and `IndicesMatch()`, then calls
`AbstractListVariable<Spell>.SwapPositions(source, destination)` and `UpdateObservable()`
(`0x060014ED`). Empty slots take part, so moving into a hole exchanges the hole rather than
compacting the list.

Whether a removal leaves a hole or compacts is not an assumed invariant: read the raw slot sequence
including empties and compare identities, not counts. `Spell.IsEmpty()` (`0x06001027`) distinguishes
an empty slot, and `Spell.GetName()` (`0x06001087`) is diagnostics.

---

## Targeting requests

`TargetingManager` exposes one queue-head request at a time through `IsTargeting()` (`0x06000770`)
and `GetTargetingLink()` (`0x06000771`); `TargetingManager.RequestTarget` (`0x0600076E`) is what
opens one. `TargetLink.GetAllTargets()` (`0x06003268`) evaluates the authored selection and scaling,
and in this build `StructureSO` is the only direct `Targeting.ITargetable` implementer.

| Verb | Route | Constraint |
|---|---|---|
| submit | `TargetingManager.SubmitTarget` (`0x06000775`) | re-resolve the UUID inside the current `GetAllTargets` result and re-run `TargetLink.CheckTarget` (`0x0600326B`) immediately before calling |
| randomize | `UITargetingInterface.Randomize` (`0x0600276E`) → `TargetLink.GetRandom` → `SubmitTarget` | terminal submission, not a candidate shuffle |
| cancel | link `resultInfo` (`0x04001A96`) → `EffectResultInfo.Cancel` (`0x06001BFC`) | `UITargetingInterface.Close` (`0x0600276C`) closes presentation and calls no `RemoveRequest` |

`SubmitTarget` assigns the supplied object **before** removing the queue head, so the settled shape
is the link's private `target` (`0x04001A94`) holding that exact object, `HasTarget` (`0x06003265`)
true, and the original link no longer current. Cancel marks the owning result cancelled and retires
its target links — one result can own several, and all of them go.

The candidate list and `CheckTarget` are the only authority here, and a request is transient by
construction: both have to be re-read at the moment of submission.

---

## Equipment loadout

`EquipmentSO.Discover` (`0x06000B10`) calls `EquipmentSO.Create` (`0x06000B11`), which sets the
asset's `isCreated` state and emits the game's observables, audio and popup. It equips nothing;
creation and equipping are separate transactions.

The loadout interface reaches `EquipmentManager.ToggleItem(Guid/EquipmentSO)`
(`0x06000514`/`0x06000515`) and from there the exact object overloads `EquipItem(EquipmentSO)`
(`0x06000517`) or `UnEquipItem(EquipmentSO)` (`0x06000519`).

### Equip clamps three ways

```text
EquipItem(EquipmentSO)
  → equipment.GetUsageCost().HasEnough()
  → for an absent target, reject a globally full equippedEquipment list
  → remaining stacks  = GetMaxLevel() - GetStacks(target)
  → affordable stacks = Floor(GetUsageCost().MaximumCostTimes()).ToInt()
  → amount = min(GlobalVariables.GetMultiBuy(), remaining stacks, affordable stacks)
  → equippedEquipment.Stack(target, amount)
  → refresh the native stack observer
  → equipment.Equip(equippedEquipment.GetStacks(target))
  → native audio
```

The manager commits whatever that clamp allows, which can be fewer stacks than asked for.

**`UIEquipmentItem.CanEquip` (`0x060023C3`) owns one admission rule the manager does not.** For a new
target, `HasTypeRoom` (`0x060023C4`) requires both a global slot and
`GetTypesEquipped(primaryType) < primaryType.GetMaxTypeSlots()`. Calling the manager without
reproducing that guard reaches a loadout the interface refuses to produce.

`EquipmentSO.Equip(int)` (`0x06000B12`) applies the resulting **total** stack level, not a delta: a
positive level reserves `GetUsageCost() * equippedLevel`, applies effects, and starts or
quick-completes attunement, while zero removes the UUID-keyed usage reservation and effects and
clears attunement. Usage is a standing reservation, not a purchase ledger.

Unequip checks `IsEquipped`, clamps the removal by live multi-buy and current stacks, un-stacks the
exact target, refreshes the observer, calls `EquipmentSO.Equip(remaining)` and plays audio. It
accepts no arbitrary amount; the native shape is one player click.

---

## Challenges

### Identity and state

`ChallengeSO.ChallengeState` is a fixed five-value enum:

| Value | Member | Meaning |
|---:|---|---|
| 0 | `None` | idle, neither queued nor active |
| 1 | `QueuedStart` | selected to activate at the next applicable transition |
| 2 | `CurrentlyActive` | active and applying its authored effects |
| 3 | `Passed` | completed successfully |
| 4 | `Failed` | abandoned or otherwise failed |

The read side of a challenge row is `IsAvailableToRun()`, `IsCompletedOnce()` and `IsMaxLevel()`
beside its level, cap, seen and reward flags, and its next difficulty and next base reward. The
selection cap, reroll count and fetched flags belong to the managers and lists, not to the
challenge.

### Surface

| Verb | Route | Admission |
|---|---|---|
| select / unselect | `UIChallengeItem.ToggleSelection` (`0x0600224E`) → `ChallengeListVariable.Toggle` on the preferred list | target is in the current Time or prestige offer list, `HasEmptySpot`, and `IsChallengeRestricted(target)` false |
| queue / unqueue | `UIChallengeItem.ToggleActivate` (`0x0600224C`) → `ChallengeSO.ToggleQueueActivation` (`0x06000936`) | currently offered, and state `None` or `QueuedStart` |
| abandon | `UIChallengeItem.AbandonActivation` (`0x0600224D`) → `ChallengeSO.AbandonChallenge` (`0x06000937`) | state `CurrentlyActive`; the requested terminal state is `Failed` |
| fetch Time offers | `UITimeScreenManager.FetchNewChallenges` (`0x06002444`) → `ChallengeManager.LoadNewActiveChallenges` (`0x060004DA`) | first fetch, or a positive reroll count |
| fetch prestige offers | `UIPersistentResetModal.FetchNewChallenges` (`0x060024EE`) → `PersistentResetManager.FetchNewChallenges` (`0x06000653`) | first fetch, or a positive reroll count |

`UIChallengeItem.FireActionButton` (`0x0600224B`) is the row's one button: its inactive branch
queues and its active branch abandons. Unselecting stays available when the selection list is full or
the target would now conflict — those gates admit the selecting direction only.

`IsChallengeRestricted` asks about the list as it stands, not about the challenge. It calls
`GetRestrictedTypes()`, which rebuilds `restrictedTypes` through an `ObservingCache` over the list
itself whenever the list changed, and `BuildRestrictedTypes()` enumerates that list's own members,
adding every `challengeTypes` entry whose `ChallengeTypeSO.IsLimitedToOneInstance()` is true. The
answer is then `target.challengeTypes.Any(restricted.Contains)`, and an empty restricted set answers
false without reading the target at all. A row leaving the list therefore takes its restriction with
it, which is why the screen's two-press swap can select a row conflicting with the one given up: the
give-up press lands first, and the conflict is asked of what remains.

### The fetch bookkeeping lives in the UI, not the manager

Both UI fetch methods set `hasFetchedChallenges` on the first fetch, or decrement
`challengeRerollsLeft` on later ones, **before** calling their native offer callback. The manager
pipelines contain no such bookkeeping, so re-driving a manager alone silently hands out free fetches.

`UITimeScreenManager.RenderNewChallengeButton` reads that same flag and relabels the one button:
`newChallengesAttr` while `hasFetchedChallenges` is false, `rerollChallengesAttr` after. The player
concept is therefore a single control whose first press per world cycle is free and whose every
later press spends a reroll — there is no separate free-fetch control to model, which is why the MCP
names both modes `reroll_*` and publishes `challengesFetched` and `rerollsLeft` as pairs on every
press. `StaticallyVerified` against the audited build.

Both native fetchers call `ChallengeListVariable.CycleOut` (`0x06001634`) before `Instantiate`
(`0x06001631`); `CycleOut` calls `ChallengeSO.EmptyState()` on every outgoing offer and
`Instantiate` calls `ChallengeSO.QueueActivation` (`0x06000935`) on every incoming one, so a
materialized offer is in `QueuedStart`.

### A redraw is not promised to be different

`ChallengeManager.LoadNewActiveChallenges` fills the list from `GenerateNewChallengeList`, which
draws from `challengesByType` through two `WeightedTable` picks seeded by `challengeRandom`, blocks
only `ChallengeSO.incompatibleChallenges` and types flagged `restrictedInstances`, and finishes with
`List.Sort(SortChallenges)`. Nothing in that body excludes the challenges currently on offer, and the
final sort makes the ordering deterministic — so drawing the same challenges in the same order is an
ordinary outcome, not a failed press. (`DiscoveryTreeSO.RerollChoices` is the contrast: it *does*
copy the current offers into `nextExcludedIds` first.) A press therefore lands on its own settled
bookkeeping — the UI flag on the first press, the reroll decrement on every later one — and the offer
identity says only whether the redraw moved. `StaticallyVerified` against the audited build.

Neither fetch needs its screen rendered; the UI methods are only the flag-and-reroll wrapper around
the manager pipeline.

---

## Persistent reset

`PersistentResetManager.PersistentReset` (`0x06000651`) is presentation plus a callback: it
references `UIScreenFlash.FadeIn` and installs `PersistentResetLogic` (`0x06000652`) as the
animation-complete callback. The fade is neither gameplay admission nor part of the transaction
identity. The private method is the transaction:

```text
PersistentResetLogic()
  → SetupPersistentValues()
  → GameManager.PersistentResetGameState()
  → ChallengeListVariable.ActivateRewards()
  → ChallengeListVariable.Activate()
  → SetPersistentResource()
  → GameManager.CleanGame()          // reloads the active scene
```

Every native reference dies inside that call. The reset also has no price: there is no cost or
affordability term anywhere on the path.

Its admission is two authored booleans, and their binding is **unproven**.
`UIPersistentResetModal.ResetWorldInteractable` reads two authored `BoolVariable` fields named
`hasCompleteWorldCycle` and `hasFetchedChallenges`, and `PersistentResetManager` exposes fields with
the same names and types — but assembly metadata cannot prove the prefab references point at the
same two assets. A manager-side read is the screen-independent *analogue* of the modal's gate, not a
proven equivalent; settling it needs the shipped prefab or a comparison observed in game.

---

## Back to Menu

`UIBackToMenuButton.BackToMenu` (`0x0600280D`) raises its authored `manualSave` event before calling
`SaveStateManager.BackToMainMenu` (`0x060006FF`). The manager method passes the literal `Start` to
`AnimateChangeScene` (`0x06000700`). Calling the manager directly is not UI-equivalent because it
omits the save event.

`AnimateChangeScene` starts `UIScreenFlash.FadeIn`, enables the loading animation, and registers the
actual scene load as the animation-complete callback. `FadeIn` synchronously writes private
`UIScreenFlash.isActive` before animating alpha. That directional false-to-true fact is the only
stable transition sentinel available before the callback tears down the current lifecycle. The
complete contract and validation list live in
[`return-to-menu-native-pipeline.md`](return-to-menu-native-pipeline.md).

---

## Clean exit

The visible `UIQuitButton.QuitApplication` callback (`0x06002507`) and
`SaveStateManager.QuitGame` (`0x06000702`) are byte-identical direct calls to
`UnityEngine.Application.Quit`; neither carries a game-defined save edge or a post-call state.
Because process termination cannot both be invoked and verified before a terminal MCP response,
there is deliberately no clean-exit GameAction or MCP tool. The audited drop and its precise
revisit condition are recorded in
[`clean-exit-boundary.md`](clean-exit-boundary.md).
