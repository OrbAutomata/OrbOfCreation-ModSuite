# Harvest element and action native pipeline

Status: **StaticallyVerified** against the audited macOS v1.0.5-2 `Assembly-CSharp.dll`.
Live mutation validation remains required before promotion.

## Player surface

The closed UI inventory maps the same four controls on World / Agromancy, World / Aspects, and
World / Druidry:

| Verb | UI entry | Native list transition |
|---|---|---|
| add/increase element | `UIHarvestList.OnHarvestClick` (`0x0600242C`) absent branch | `HarvestElementListVariable.AddInstance(HarvestElementSO, BigDouble)` (`0x0600168A`) |
| remove/decrease element | `UIHarvestList.OnHarvestClick` present branch | `HarvestElementListVariable.RemoveInstance(HarvestElementSO, BigDouble)` (`0x0600168B`) |
| add/increase action | `UIHarvestActionList.OnActionClick` (`0x0600241F`) absent branch | `HarvestActionInstanceListVariable.AddInstance(HarvestActionInstance, int)` (`0x06001682`) |
| remove/decrease action | `UIHarvestActionList.OnActionClick` present branch | `HarvestActionInstanceListVariable.RemoveInstance(HarvestActionInstance, int)` (`0x06001683`) |

`UIHarvestTypeList.OnHarvestTypeClick` only selects which authored list the page displays. It is
not a separate gameplay verb.

## Exact active lists

The audited asset registry contains three `HarvestElementListVariable` assets and one
`HarvestActionInstanceListVariable` asset. Only these two are dynamic player-active lists:

| Role | Stable UUID |
|---|---|
| `ActiveHarvestElements` | `5a9f8001-3ae2-4799-86b6-5198763e0fe2` |
| `ActiveHarvestActions` | `e4a9d4c3-61cc-4f94-bab9-7bc8e841cc32` |

`AllHarvestElements` and `GardenHarvestElements` are static authored rosters. Binding the two
active lists by exact UUID and native type avoids screen-object and display-name routing.

## Element admission and transition

`UIHarvestItem.GetInteractivity` (`0x06002427`) requires the element's `usageCost.HasEnough()` and
`addSelectionList.ContainsOrHasSpaceFor(element)`. The click path clamps the requested amount by
`HarvestElementSO.MaximumNumberInstances()` and the screen's multi-buy value before calling the
active element list.

`HarvestElementListVariable.SetInstancesForEl` keys the element's modifier/reservation by the
list UUID. A positive count engages the usage/effects; removing the last count releases them.
Those resource and modifier consequences are game-owned side effects. The stable observable owned
by the requested verb is `StackableListVariable<HarvestElementSO>.GetStacks(element)` moving in the
requested direction.

## Action identity, admission, and transition

`HarvestElementSO.SetupHarvestActions` creates one `HarvestActionInstance` prototype for every
authored available action, and `GetActionInstances()` (`0x06000C62`) returns those element-owned
prototypes. A valid pair therefore requires all of:

- exact `HarvestElementSO` and `HarvestActionSO` UUID/type identity;
- a prototype whose `GetElement()` and `GetAction()` are those exact objects;
- `HarvestActionInstance.IsVisible()` (`0x06000E72`);
- active-list room when the pair has no current instance;
- requested count not exceeding `GetMaximumInstances()` (`0x06000E75`), which reads the owning
  element's mastery level plus one in this build.

`HarvestActionInstanceListVariable.FindInstance` (`0x06001680`) compares the element/action pair.
`AddInstance` creates a runtime instance only when absent, then calls `ChangeInstance`; remove
decrements the same pair and removes it at zero. `ChangeInstance` clamps between zero and the live
maximum. The one postcondition is the pair's public `instances` count moving in the requested
direction.

## Next-drain read lineage

The UI tooltip's next-instance resource drain is not the authored base cost by itself.
`HarvestActionInstance.GetInstanceTooltipNodes(int)` (`0x06000E7F`) performs this exact fold:

```text
count -> GetScalingInfo(max(count, 1))
      -> ScalingInfo.GetDrainCostMod()
      -> BigDouble.AsPercent()
base  -> private ComputeResourceCost()
      -> ResourceCostList.Multiply(percent modifier)
```

The MCP reader applies that lineage request-scoped on Unity's main thread and publishes only the
next add's named cost rows. It never treats a drain as a payment delta.

## Completeness and fail-closed boundary

The read side enumerates `HarvestElementSO.All`, then every element-owned action prototype; it has
no element name, page, subtype, fruit, or treasure filter. This proves coverage of the four
element/action controls across all three mapped screens. Plot-node actions are a different native
family (`PlotNodeSO` / `PlotNodeActionInstance`) and remain B-020; combining B-019 with that complete
catalog is what must establish planting coverage for every non-fruit/non-treasure plot type.

Every member in the lifecycle binding set is installed-contract pinned. A missing member disables
the complete action family; there is no execution-time reflection or fallback list selection.

## Disposable-save promotion checklist

1. Compare one Agromancy, one Aspects, and one Druidry element row with the visible active count.
2. Compare element-add availability and standing usage costs with the screen.
3. Add one element, verify exactly that element's count increases, then remove it.
4. Confirm a full element list refuses before any native call.
5. Compare every offered action and its mastery-derived maximum with the selected element panel.
6. Compare the published next-instance drain with the tooltip for counts zero and nonzero.
7. Add an action, verify exactly that element/action pair increases, then remove it to zero.
8. Confirm a mismatched element/action UUID pair and a hidden action refuse before mutation.
9. Confirm an amount above the current element/action maximum refuses without partial application.
10. Cross-check one non-fruit/non-treasure plot after B-020 lands; B-019 alone does not claim that
    separate plot-node lifecycle.
