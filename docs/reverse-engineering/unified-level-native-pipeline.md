# Unified level native pipeline

This dossier audits the ordinary level-list controls in the pinned Orb of Creation v1.0.5-2
macOS assembly pair. Research development and spell mastery use their own richer screens and
remain separate capabilities even though their native types also implement `ILevelable`.

## Complete interface matrix

`ILevelable` has exactly six non-abstract implementations in the audited assembly:
`EquipmentTypeSO`, `GlyphSO`, `ResearchSO`, `ResourceTypeSO`, `SpellRecipeSO`, and
`TimeRuneSO`. `ILevelableHasFree` has exactly three: `EquipmentTypeSO`, `GlyphSO`, and
`ResourceTypeSO`. The lifecycle binding asserts both complete rosters before admitting an action;
an added or removed concrete type makes the whole family contract-unavailable rather than silently
leaving a new button unsupported.

The unified capability owns the four ordinary `UILevelableItem` list controls. `ResearchSO` is
delegated to `game_research`, whose develop/queue/cap rules are not an ordinary single-click level.
`SpellRecipeSO` is delegated to `game_spell_mastery`, whose mastery and level-all paths are likewise
different player verbs.

## Visible callbacks and cost semantics

`UILevelableItem.SetupCostButton` displays `ILevelable.GetLevelCost`, configures its
`UICostButton` not to perform a one-time payment, and connects the click to
`UILevelableItem.PurchaseLevel`. That callback invokes `ILevelable.PurchaseLevel`. The four
concrete implementations then apply their level effects and persistent usage costs themselves:
equipment applies level usage, glyphs apply their cost, resource types apply level-cost usage, and
time runes apply usage inside their level-up path.

The paid cost is therefore an admission/capacity requirement, not a ledger deduction for the
suite to perform. The action checks `CanLevel`, reads the current `GetLevelCost`, and requires
`ResourceCostList.HasEnough` immediately before invoking `PurchaseLevel`; it never calls
`PerformCost`.

All four owned implementations compile `CanLevel()` to `ldc.i4.1; ret` — none of these types has a
level ceiling, so the gate is the game's own and it is permanently open in this build. The suite
re-reads it before every level anyway, because the game owns that gate; but no ordinary level target
can be exhausted, which is why this surface has no maxed refusal to give.

`UILevelableItem.RenderFreeLevelButton` displays `ILevelableHasFree.GetFreeLevelCost`, requires
`ResourceCostList.AllResourcesVisible` and `HasEnough`, and connects the click to
`PurchaseFreeLevel`. Bonus levels follow that exact route for the three implementing types.

## Read and action boundary

The four owned world categories publish paid, bonus, and total levels plus only the next control
that can actually run. A purchasable decision includes its named native cost rows and current
spendable amounts; an unavailable decision carries its binding reason without a speculative
ledger. Bonus controls are absent for time runes.

`game_level_up(mode="purchase"|"bonus", uuid=..., amount=...)` resolves stable UUID plus the exact
published native type on Unity's main thread. `amount` is between 1 and 1000. Before each requested
level the action re-reads `CanLevel`, the applicable native cost, resource visibility where needed,
and `HasEnough`; it invokes the matching native callback only while those checks continue to pass
and stops early when one no longer does. The single postcondition sentinel is directional:
`GetLevel` rises for a paid level, or `GetFreeLevels` rises for a bonus level. Success returns the
settled level delta; a complete native no-op is verification failure. Resource balances and
effect-record counts are neither captured nor reconciled.

## Disposable-save live checklist

1. Compare representative equipment-type, glyph, resource-type, and time-rune rows with each
   visible level button, including costs, affordability, paid level, bonus level, and total level.
2. Buy one affordable paid level from every concrete family and verify the screen and settled MCP
   delta show the same directional increase without a separate resource deduction.
3. Buy one bonus level for equipment, glyph, and resource type; verify the bonus and total levels
   rise and the paid level remains stable.
4. Attempt a time-rune bonus level and verify an ordinary refusal before a native call.
5. Exercise a hidden-resource and unaffordable level cost and verify the refusal names the binding
   resource without changing the level.
6. Compare a visible level-zero glyph with any bonus/free levels to the native button's usability;
   the assembly proves the accessors and callbacks, while this edge semantic remains live-only.
7. Cross a lifecycle boundary and verify the old request refuses without invoking a callback.
