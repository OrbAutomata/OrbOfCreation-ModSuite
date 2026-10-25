# Auto Levels

An opt-in ordinary ServiceCycle feature that adjusts global level dials through the shared
[Spell Composition GameAction](../SpellComposition/Runtime/SpellCompositionGameAction.cs).
[The player guide](../../docs/user-guide/automatic-levels.md) owns dial behavior and budget defaults;
[configuration declarations](AutoLevelsConfiguration.cs) own the setting contract.

## Policy and evidence

[AutoLevelsPolicy](Policy/AutoLevelsPolicy.cs) evaluates immutable current and prospective cost
quotes from the shared world. Each domain's complete quote uses its captured native operands and
owned cost arithmetic; the feature does not infer next prices from a guessed multiplier or change a
dial to measure the result. Current and next evidence have separate completeness, so an unavailable
next quote can prevent a raise without hiding a current-level reduction.

Resource comparisons retain the game's magnitude and quality coordinates. Per-spell immediate
costs are checked independently. Sustained-drain comparisons use the domain's actual current share,
its prospective drain delta, the resource's net depletion and available stock. Missing or
contradictory evidence holds the affected dial with a typed explanation.

[The worker](ServiceCycle/AutoLevelsWorkerDefinition.cs) evaluates the dials once per world generation
and emits at most one one-level adjustment, preferring reductions and rotating eligible raises. It
carries the expected current level and lifecycle into the canonical action. On Unity, that boundary
rechecks the current level, native range, control availability and mutation ownership, then verifies
the requested value. A manual change invalidates that prepared action. Native faults use the shared
per-target retry gate; disable, emergency stop and lifecycle replacement discard prepared work.

[The contribution](AutoLevelsFeatureContribution.cs) declares settings, icon identity, configured
intent and action-family demand beside the feature. Shared registration supplies the Mods page,
immediate mode command, gameplay control and MCP metadata. Policy decisions travel as operational
status details, leaving blocking reasons for actual readiness or fault conditions.

## Verification

[Policy tests](../../tests/OrbModding.Tests/Services/AutoLevels) drive the shipping policy;
[world tests](../../tests/OrbModding.Tests/Runtime/World) cover prospective economy derivation;
[boundary tests](../../tests/OrbModding.Tests/Services/SpellComposition) cover the shared dial action.
Installed-game metadata contracts and [live validation](../../docs/testing/runtime-validation.md)
cover native shape and visible gameplay respectively; portable tests do not establish runtime parity.
