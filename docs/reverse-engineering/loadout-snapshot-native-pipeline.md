# Loadout and snapshot native pipeline

The pinned v1.0.5 assembly exposes two different player-facing storage systems. Player
loadouts have stable identities and switch spells plus optional Equipment and Alchemy
sections. Snapshot rows have no identity of their own; the UI addresses them by their
position in one identified Equipment or Alchemy snapshot list.

## Player loadouts

`PlayerLoadout` (`TypeDef 0x00ae`) owns `guidContainer`, `label`, `isSelected`,
`saveEquipment`, `saveAlchemy`, saved `spells`, and stacked Equipment and Alchemy records.
`Fill` creates the missing `GuidContainer`, so a filled runtime row is addressable by
`GetGuid` (`MethodDef 0x0f1e`). `PlayerLoadoutListVariable.GetAll` and
`GetActiveLoadout` are `MethodDef 0x16b1` and `0x16b0`.

`UILoadoutList.OnLoadoutClick` calls `LoadoutManager.SetLoadout` (`MethodDef 0x0610`).
The manager finds the exact target and delegates to `SetLoadoutIndex`
(`MethodDef 0x0611`), whose audited order is:

1. refuse while `CanSwapLoadouts` sees any spell casting or readying;
2. update the selected-index cache and the old/new `isSelected` flags;
3. save the old loadout;
4. deactivate the old spells;
5. load the new spells and enabled Equipment/Alchemy sections;
6. reactivate spells and recalculate spell state.

The identity-and-outcome sentinel is the requested loadout becoming selected. The native
method is used whole; reproducing its ordered save/deactivate/load/reactivate transaction is
not an approved boundary.

There is no independent Save button. `SaveActiveLoadout` (`MethodDef 0x060d`) is called by
the Equipment and Alchemy section toggles. Each toggle first calls
`PlayerLoadout.SetSaveEquipment` (`MethodDef 0x0f0f`) or `SetSaveAlchemy`
(`MethodDef 0x0f10`); enabling then calls `SaveActiveLoadout`. Disabling only clears the
section flag. The MCP surface consequently exposes section toggles, not an invented save
verb. The flag matching the requested value is the sentinel.

`UILoadoutEditor` exposes exactly three metadata controls. Text input calls
`LoadoutLabel.SetName` (`MethodDef 0x3518`) with a 24-character UI limit. The icon and color
buttons advance `(current + 1) % count` through `GlobalVariables.GetCustomSprites`
(`MethodDef 0x0571`) and `GetCustomColors` (`MethodDef 0x056f`), then call
`SetIconIndex` (`MethodDef 0x3519`) or `SetColorIndex` (`MethodDef 0x351a`). The MCP mirrors
rename, next icon, and next color; arbitrary index selection is not a UI verb. Each resulting
label value is its mutation sentinel.

Before a selected loadout is applied, every stored spell, Equipment entry, and ordinary
Alchemy entry is revalidated for stable identity, expected native type, and structural role.
The native selection path does not require a saved glyph to remain an owned construction choice,
so the suite does not impose that stricter rule. The composite boundary additionally checks
whole-list slot limits, Equipment type-slot limits, per-glyph usage, and native
`ResourceCostList` usage capacity. Ordinary Alchemy entries retain
their concrete action's discovery, per-recipe maximum, and usage admission; the v1.0.5 UI exposes
no independent editable type-slot layout for a stored Alchemy record. A stored reference that is
stale, structurally invalid, undiscovered/uncreated, over-stacked, or over capacity refuses before
`SetLoadout`.

## Snapshots

`SnapshotLoadout<T>` (`TypeDef 0x00b1`) owns only a stacked record and observer state. Its
concrete `AlchemySnapshot` (`TypeDef 0x00b2`) and `EquipmentSnapshot` (`TypeDef 0x00b3`) do
not own a UUID. The owning `AlchemySnapshotListVariable` and
`EquipmentSnapshotListVariable` are `IdScriptableObject` descendants, so the truthful stable
address is the list UUID plus the visible zero-based slot. The MCP wire counts those slots from
1, matching the screen; the native index below it is unchanged.

`UISnapshotLoadout.RenderEmpty` enables Save and disables Load/Clear. `RenderContent` enables
Load/Clear and disables Save. The supported lifecycle therefore is:

- save the current active section into an empty slot;
- load a populated slot into the active section;
- clear a populated slot.

The UI does not overwrite a populated slot. Alchemy save/load calls
`AlchemyInstanceListVariable.CreateStackedRecord` / `FromStackedRecord`
(`MethodDef 0x1615` / `0x1616`). Equipment save/load calls
`StackableListVariable<EquipmentSO>.GetStackedRecord` / `SetStack`
(`MethodDef 0x07ba` / `0x07b0`). `SnapshotLoadout<T>.SaveSnapshot`, `GetRecord`, and `Clear`
are `MethodDef 0x0f94`, `0x0f97`, and `0x0f95`.

Save is verified by the target slot becoming populated; clear by it becoming empty; load by
the active section matching the snapshot's identity/count record. Refund or resource
accounting is not part of this family.

## MCP contract

`game_loadout` accepts one of `select`, `set_section`, `rename`, `next_icon`, `next_color`,
`snapshot_save`, `snapshot_load`, or `snapshot_clear`. `uuid` names a player loadout for the
first five modes and a snapshot-list owner for snapshot modes; snapshot modes also require
`slot`. `set_section` requires `section` (`equipment` or `alchemy`) and `enabled`; rename
requires `name`.

World rows publish player loadouts and the two snapshot-list owners. Nested entries retain a
player-facing name and UUID because the caller can act on them through their component tools.
Mutation responses contain only the settled changed fact and the next immediately relevant
state.
