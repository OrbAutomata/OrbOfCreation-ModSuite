# Plot action native pipeline

Status: **StaticallyVerified** against the audited macOS v1.0.5-2
`Assembly-CSharp.dll`. Live mutation validation remains required before promotion.

## Player surface

`UIPlotNodePage.SetupActionList` (`0x060025A9`) renders
`PlotNodeSO.GetActionInstances()` for the selected plot. The selected plot itself is UI state:
`UIPlotNodeList.OnNodeClick` (`0x060025A5`) only changes which authored action list the page shows.
The gameplay controls are the two branches of
`UIPlotNodeActionList.OnActionClick` (`0x06002596`):

| Player verb | UI branch | Native transition |
|---|---|---|
| add or increase an action | no active pair, or active quantity below maximum | `PlotNodeActionInstanceListVariable.AddInstance(PlotNodeActionInstance, int)` (`0x060016B7`) |
| remove or cancel an action | active pair | `RemoveInstance(PlotNodeActionInstance, int)` (`0x060016B8`), or `PlotNodeActionInstance.Cancel()` (`0x06000F3E`) at the native minimum |

There is no separate planting-specific callback. Planting is an authored plot/action pair on this
surface, so the complete pair catalog is the truthful generalization.

## Pair identity and complete catalog

Every `PlotNodeSO` owns authored `availableActions`. Its `GetActionInstances()` returns the
corresponding runtime prototypes. A submitted pair is admitted only when one and only one prototype
returns the exact plot from `GetElement()` and the exact action from `GetAction()`. The active list
is the exact `PlotNodeActionInstanceListVariable` asset
`70871e86-100b-4ae0-ba9b-fc96e09b7e1f`; `FindInstance` compares that same pair.

The world reader enumerates `PlotNodeSO.All`, then every plot's `availableActions` and action
instances. It has no fruit, treasure, planting, screen, internal-name, or subtype filter. That is
the coverage proof for non-fruit/non-treasure plots: every pair the UI can render is in
`plot-actions`, and the action boundary accepts any exact pair from that catalog. B-019 covers the
separate `HarvestElementSO`/`HarvestActionSO` lists; together the two catalogs cover both harvest
interaction archetypes without guessing from names.

## Add admission and transition

`UIPlotNodeAction.CanInteract` (`0x0600258C`) checks the prototype's
`HasEnoughForOneInstance()` and, for a new pair, the active list's `HasEmptySpot()`. The click path
also clamps against `GetMaximumInstances()` and `GetMaximumRemInstances()`. The boundary rechecks
those facts, the plot's `IsVisible()`, and the prototype's `IsVisible()` immediately before taking
the shared Harvest mutation permit.

`AddInstance` finds an existing active pair and changes its quantity. If none exists and the list
has room, it creates and initializes the runtime instance before changing quantity. The one
postcondition is `GetActualQuantity()` moving upward for the requested pair.

The published row exposes the current active quantity and an `add` decision. An available add may
include `maximumAdditional` and the plot-quantity cost. A blocked add omits costs and names only the
binding constraint. A prerequisite latch which the immutable world cannot prove is reported as
requiring the action boundary's live native check; the read does not mutate to manufacture a
verdict.

## Remove and cancel boundary

`RemoveInstance` resolves the active pair and calls `PlayerChangeInstanceQuantity(-amount)`. The
native quantity path clamps to `GetMinimumInstances() == 1`, so decrement never silently crosses
the last-instance boundary. When `IsAtMinimumQuantity()` is true, the UI instead calls `Cancel()`.
The MCP keeps that semantic: an oversized remove is clamped by the game to the minimum, and a later
remove at the minimum cancels the pair regardless of the screen's multi-buy amount. Refunds and
released effects are game-owned side effects, not verification ledgers. The one postcondition is
pair quantity moving downward.

## Contract posture

The lifecycle binds plot, action, instance, active-list, pair-identity, visibility, admission,
maximum, quantity, minimum, cancel, add, and remove members as one complete set during lifecycle
composition. Any missing member makes the family `ContractUnavailable`; no execution-time
reflection or fallback list selection exists. UUID plus expected native type is identity, and all
Unity/game reads and writes stay on the Unity main thread.

## Disposable-save promotion checklist

1. Compare every visible action on one ordinary plot and one non-fruit/non-treasure plot with its
   named `plot-actions` rows.
2. Verify current active quantities and add/remove availability match the selected plot panel.
3. Compare `maximumAdditional` and plot-quantity cost with the UI for a zero-active pair.
4. Add one planting pair and verify exactly that pair's quantity rises in the screen and settled
   MCP state.
5. Increase then decrement a pair above its minimum; verify both transitions preserve pair
   identity.
6. Cancel the last instance and verify the pair becomes inactive.
7. Refuse a hidden plot, invisible action, mismatched pair, full active list, insufficient plot
   quantity, and amount above either native maximum before a native callback.
8. Confirm a prerequisite which was unknown in publication is rechecked by the native action
   instance at submission time.
9. Cross a lifecycle boundary and verify a stale submission cannot touch the replacement list.
10. Spot-check that no authored action rendered on the plot page is absent from the catalog,
    including a non-fruit/non-treasure planting surface.
