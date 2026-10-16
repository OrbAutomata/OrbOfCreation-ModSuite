# Artifacts

Equipment is artifacts, on **Workshop > Artifacts**, and the page has three tabs: Loadout, Create and
Upgrade.

## The loadout has two budgets

An artifact loadout is bound by **two independent limits at once**:

| Budget | Example reading | Meaning |
|---|---|---|
| Weight | `10/12` | Total weight of the equipped artifacts |
| Slots | `4/4` | Number of artifacts equipped |

Either can bind first: a loadout at `10/12` weight with `4/4` slots is full even though two weight
remain. Each artifact shows its own weight, rendered red when it does not fit, because the page runs
its own fit check before you try. This is the same two-binder shape as
[spell-loadout.md](spell-loadout.md).

A third limit sits behind those two: a new artifact also needs a free slot **for its type**, so a
loadout with room on both budgets can still refuse an artifact whose type is full.

Equipping the same artifact again **stacks** it, up to that artifact's maximum, and the multi-buy
selector decides how many stacks one click adds. The game clamps that request to what is left of the
maximum and to what you can pay for, so a click can add fewer stacks than asked for without saying
so. **Code shape:** each equipped stack reserves the artifact's usage cost again — a standing
reservation that returns when the stack goes, not a purchase.

## Create and Upgrade

**Create** is a discovery surface; see [discovery.md](discovery.md). Creating an artifact does not
equip it — it becomes available to the Loadout tab and nothing else happens.

**Upgrade** raises per-artifact **levels** and is priced in **two gear currencies** rather than
ordinary resources. A total **artifact mastery** figure exists alongside the per-artifact levels.

What artifacts actually do is unrecorded; see [unmapped-systems.md](unmapped-systems.md).
