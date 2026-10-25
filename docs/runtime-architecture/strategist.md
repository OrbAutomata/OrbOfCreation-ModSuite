# Strategist advice

[Back to architecture](README.md) · [Player controls](../user-guide/configuration.md)

The strategist derives progression advice from a pinned world and a sustained-income window. Its
[service](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Strategist/ServiceCycle/StrategistService.cs)
owns publication and cadence; its
[derivation](https://github.com/OrbAutomata/OrbOfCreation-ModSuite/blob/main/src/Strategist/StrategistDerivation.cs)
owns goal ordering and the next-step judgment. It judges about once a minute, measuring income over
at least a minute and one full cycle of the slowest running producer. Display text and automation
preference consume that judgment rather than deriving independent advice.

## What the advice establishes

Offered one-shot gates related to captured Restoration prerequisite references rank ahead of other
gates. Within that rank, their cost bottleneck determines the wait order. References can belong to
alternative or unknown rules, so they establish possible relevance rather than mandatory steps or
predicted unlocks. Additional requirements can still stand between a purchase and dependent content.

The active goal's advice distinguishes an affordable price from missing resources, a capacity gate,
an income window still being measured, no measured income, or an allocation-point shortage.
An uncovered cost above capacity names the resource and the required ceiling; advice does not predict
which purchase will raise it enough. Held costs are covered even if that stock is above capacity.
Allocation advice asks for points without assigning a refill time or promising a refund.

Income is measured from gross lifetime gains over the producer window, with cap-clipped readings
identified as lower bounds. A price's ETA divides its shortfall by that measured income. Competing
spenders, changing production and incomplete measurements prevent this from promising wall-clock
affordability. A zero measured rate describes the captured window, not proof that no source exists.

## Publication and consumers

The full bulletin is an immutable latest-value publication for Mods > Strategist and the existing MCP
read surface. The ordinary strategy publication carries only the active goal identity, with no
resource stances. A changed goal advances strategy generation; a replacement judgment for the same
goal does not. A lifecycle replacement clears the displayed bulletin and restores neutral strategy.
The next service cycle pins the current policy alongside configuration and world state.

The feature projects faults from its existing service runner through the shared diagnostic formatter.
Before the first bulletin, the page shows the retry category, failed-attempt count and exact recorded
reason. With a bulletin present, it retains the captured snapshot and displays the same fault beside
its capture time. A runner fault restores neutral strategy, so Auto Buy uses ordinary ordering while
the page retains that snapshot. Recovery clears the fault status; only a successful new bulletin
publication restores a goal preference. Repeated faults do not advance unchanged neutral policy.
Lifecycle replacement clears both the status and the snapshot. A retained bulletin does not establish
that its advice still matches the current world.

Auto Buy can opt in through **Prioritize strategist goal**. It moves the preferred Upgrade ahead of
ordinary cost ordering only after current captured admission and affordability allow that candidate.
The preference cannot bypass configured reserves, feature selection, ownership, queue room or live
native validation. Unsupported, unavailable or unaffordable advice leaves ordinary purchasing in
place. It does not save resources for the goal or coordinate other spending features.

Resource reservations, cross-domain spend constraints, production-value ranking and predicted
capacity detours remain unimplemented. The strategist's advice is not mutation authority; each
capability retains its canonical GameAction and current native checks.
