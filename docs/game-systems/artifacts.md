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

Equipping the same artifact again **stacks** it, up to that artifact's maximum (observed: a pair of
gloves capped at two — the same shape as an alchemy recipe's active count, see
[alchemy.md](alchemy.md)), and the multi-buy selector decides how many stacks one click adds. The
game clamps that request to what is left of the maximum and to what you can pay for, so a click
can add fewer stacks than asked for without saying so. **Code shape:** each equipped stack reserves
the artifact's usage cost again — a standing reservation that returns when the stack goes, not a
purchase.

## Create and Upgrade

**Create** is a discovery surface; see [discovery.md](discovery.md). Creating an artifact does not
equip it — it becomes available to the Loadout tab and nothing else happens.

**Upgrade** appears only after the **Upgrade Artifacts** upgrade (Workshop), which itself unlocks
at 3 Artifact Upgrades capacity. It levels artifact **types**, not single artifacts, and is priced in
the **Artifact Upgrades** advancement currency (a free bonus-level currency sits beside it). Each type
level adds effect levels to a whole attribute family — observed at Lv 1 for 3 Artifact Upgrades:
Conductor +5 Infusion, Amulet +5 Aura, Thaumuic +3 Wizardry, Shaper +3 Dimensional. A total
**artifact mastery** figure sits on the Loadout tab.

## What some artifacts do (partial, observed)

| Artifact | Type, weight | Effect |
|---|---|---|
| Blast Staff | Conductor, 4 | every 15 s, the next cast gets ×1.88 Cantrip and ×1.23 Charm power |
| Scholar's Wand | Conductor, 4 | after every cast, a splash of Mental resources divided by rarity — one cast added 6.5e6 Skill, 4e7 Psi and Control past its cap |
| Fundamental Wand | Conductor, 8 | +4.32 Infusion effect levels; weighs double until Expert Artificer 9 |
| Cloak of Casting | Cloak, 1 | ×1.10 Elemental gain, ×1.05 Elemental quality, ×1.10 Cantrip power |

The splash scales with the casting dials (×3.20 per Output Lv, ×1.25 per Reserve Lv on its tooltip),
which is why a conductor that is small early becomes a main income source late in a run.
