# Structure enable/disable native pipeline

Audited target: Orb of Creation v1.0.5 `Assembly-CSharp.dll` from `artifacts/game-v105`.

## Player surface

The structure list exposes `UIStructureList.ToggleDisableStructure(StructureSO)` at metadata token
`0x06002765`. Its body checks only Unity object truthiness and calls
`StructureSO.ToggleDisabled()` (`0x06001787`). No alternate enable/disable choice or parameter is
present, so the MCP surface is one desired-state verb with `enable` and `disable` modes.

## Native transition

`ToggleDisabled()` reads `StructureSO.disabled` (`0x04000AD5`) and dispatches to exactly one branch:

- `DisableStructure()` (`0x06001788`) writes `disabled = true`, then calls `RemoveEffects()`
  (`0x06001790`).
- `EnableStructure()` (`0x06001789`) writes `disabled = false`, then calls `ApplyEffects()`
  (`0x0600178F`).

The field write precedes the effect callback in both method bodies. The portable action invokes
only `ToggleDisabled()` and treats the resulting flag as its one game-written sentinel. It does not
call either effect method itself and does not assemble an effect ledger.

## Admission and lifecycle

The action resolves one exact registry UUID as concrete `StructureSO`, rejects a stale lifecycle,
checks the live `IsAvailable()` fact, and refuses an already-satisfied state before capturing its
mutation permit. The complete action binding set is concrete type, availability method, disabled
field, and toggle method; one missing member makes the family unavailable. The published
`structures` row already captures availability and disabled state from the same native members.

## Later live validation

On a disposable save, compare an available attribute's context control with `world_get`, disable
and enable it through `game_structure`, and verify the visible effect disappears and returns. Also
exercise unavailable and already-in-state refusals and confirm the settled response reports the
same enabled state shown by the game.
