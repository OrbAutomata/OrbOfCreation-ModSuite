# The requirement graph

What gates a purchase, as data. The player-visible residue of this graph is two lines on
[attributes-and-upgrades](../game-systems/attributes-and-upgrades.md); everything else here exists
for chain-planning and admission checks.

## Displayed level is a three-term sum

```
displayed level = purchased level + base levels + bonus levels
```

**Requirement checks do not use the displayed term.** They evaluate purchased and base levels only,
so a node showing a healthy green `+5` while its purchased level is `0` **fails** a requirement of
`≥ 5`, and the interface gives no hint that the number it shows is not the number being tested.
Bonus levels also **do not advance the paid cost curve**: a node at purchased level 2 with 2 bonus
levels costs what level 2 alone costs. Never plan a gate against the number on screen.

## Which level a leaf reads depends on the leaf class

The rule above holds where it was observed; it is not universal. The two leaf classes read at IL
level do not behave the same way, and neither generalizes to a third.

- `StructureRequirement.InternalIsValid` reads `StructureSO.quantity` directly and references
  `selfBonusLevels` nowhere, so granted or effective levels can never satisfy a purchased-quantity
  requirement.
- `ResearchRequirement.InternalIsValid` dispatches the virtual `UpgradeableObject.GetLevel()` slot,
  whose `ResearchSO` override returns the total level — bonus levels **included**.

Research therefore has no single interchangeable "Research level":

| Question | Native path | Bonus levels |
|---|---|---|
| does a prerequisite on this research pass? | `ResearchRequirement.InternalIsValid` → virtual `GetLevel()` | **counted** |
| is this research complete, or at its native maximum? | `ResearchSO.IsMaxLevel()` → `GetBaseLevel()`, referencing neither `GetBonusLevels()` nor `GetLevel()` | **excluded** |

A research node can satisfy another node's prerequisite on the strength of bonus levels while still
being short of its own maximum. Read `GetPurchasedLevels()`, `GetBaseLevel()`, `GetBonusLevels()`
and `GetLevel()` as four separately named values; collapsing them loses exactly the distinction the
game is making. The verdicts that decompose a research refusal — `IsVisible()`, `IsComplete()`,
`CanDevelop()`, `IsWithinDevelopRange()`, `MeetsLevelRequirements()`, `StillHasLeeway()`,
`IsBelowArtificialMaxLevel()`, `IsBelowMaxInvestmentLevel()` — are only comparable with each other
and with those levels when read in the same generation.

`IsWithinDevelopRange()` is the gate the last five of those feed, and its body fixes both the order
they are asked in and how the final three compose:

```
ResearchSO.IsWithinDevelopRange() : bool
  IsComplete()                       → false when true
  GetDevelopmentCost().HasEnough()   → false when false
  MeetsLevelRequirements()           → false when false
  StillHasLeeway()                   → true when true
  IsBelowArtificialMaxLevel() && IsBelowMaxInvestmentLevel()
```

Two consequences a refusal has to respect. **Cost is asked before level requirements**, so a node
that is both unaffordable and short of its requirements is refused for the price — the gate the
game itself reached first. And **leeway is one gate with the two caps, not three**: the method
develops on leeway *or* on being below both caps, so exhausted leeway beside an open cap refuses
nothing, and a cap reached while leeway remains refuses nothing either. `CanDevelop()` is
`IsWithinDevelopRange() && !IsDeveloping()`, which is why a development already running is the last
gate a caller is told about rather than the first.

Research `levelPrerequisites` join the same authored graph as upgrades and structures, with native
`GetRequirementLevel()` as the check level.

## Visibility and availability are separate

A node is **hidden** until its prerequisite tier and its gating levels are met. It then becomes
visible but possibly still unavailable, and only later purchasable. Seeing a node does not mean it
can be acted on, and a node's absence is a data question, not an interface bug.

Requirements can also be **per level**: the same node can demand different things at level 3 than it
did at level 1.

## Containers and reusable link tiers

An authored `Prerequisites.Container` combines its top-level conditions with **AND**. Nested
`OrRequirement` and `AndRequirement` records introduce explicit grouping; flattening the container
into an ungrouped edge list changes its meaning.

`PrerequisiteLinkSO` reuses one gate across several consumers. Each link contains ordered tiers.
A tier becomes active only when every owner bound to that link and tier has at least one purchased
level and every intrinsic condition on the tier passes. Most tiers have no bound owner and depend
only on their intrinsic conditions. `ScribeTiers/Base` is the exceptional multi-owner case: Echoing
Scroll and Scribism Scrolls II through V are all owners, so all five participate in the AND gate in
addition to the tier's intrinsic Scribism condition.

A tier's own conditions are checked at **level zero**, whatever the level of the entity consulting
it — `StaticallyVerified`. `LinkDefinition.CheckPassivesEnabled()` reaches them through the
no-argument `Container.Check()`:

```text
LinkDefinition.IsEnabled()            → isActiveEnabled && CheckPassivesEnabled()
LinkDefinition.CheckPassivesEnabled() → isPassiveEnabled || currentFrame == GameManager.currentFrame
                                          ? isPassiveEnabled
                                          : (isPassiveEnabled = prerequisites.Check(),
                                             currentFrame  = GameManager.currentFrame)
Prerequisites.Container.Check()       → available ||
                                        every condition IsValid(
                                            ConditionInfo.Adjust(adjustValue, 0L))
```

A tier has no level of its own, so a per-level threshold authored on one scales by nothing. Passing
the asking entity's level instead would scale the tier's thresholds by a number the game never
applies there.

The serialized `available` and `gameId` fields inside prerequisite containers are runtime cache
state, not authored conditions. Static analysis reads `prerequisites`; a running-game probe owns any
claim about the current cache value. The container's `adjustValue` shifts every threshold the
no-argument `Check()` evaluates; the parameterized overload used as a differential oracle builds its
`ConditionInfo` from the level alone, so neither side of that comparison sees an authored adjustment.

The committed progression graph preserves exact operators, grouping, link tiers, owners, and
consumers. Query it with `cd tools && uv run orb-gamedata query`, or regenerate the exhaustive local
atlas with `uv run orb-gamedata report atlas`.

## Requirements reach across systems

A requirement is not restricted to the system its node lives in. E.g., *Arcanism II* costs only
`10,000` Arcanum but requires the *Arcane Dominion* **research** to have at least one level — the
research merely being available at level 0 is not enough. Requirements also routinely demand
**possession of a resource** not yet produced, and some are **hard gates** on a specific level
count rather than soft scaling conditions. Model hard and soft requirements separately; they behave
differently.

## A worked chain

```
Improved Scribing            — hidden
├── requires Technology tier              — met
└── requires Scribism >= 1                — NOT met (level 0)
    ├── Scribism requires Improved Concepts >= 1   — met
    └── Scribism requires possession of Ink        — NOT met (0 Ink)
        └── Ink is produced by Refine Ink
            └── Refine Ink is hidden until Research Scribing >= 1
                └── Research Scribing requires:
                    ├── Innovation tier
                    ├── Research Electric >= 1
                    ├── possession of Elementia     — NOT met (0)
                    └── hard gate Expert Items >= 5 — NOT met
```

The last line is the trap: a visible `Expert Items (+5)` bonus still fails the `>= 5` gate, because
purchased level is 0. A chain can run four or five hops deep through hidden nodes before it reaches
something purchasable; walking backwards from the wanted node is the only reliable way to find the
real blocker.

## Asking the game instead of walking the graph

`Prerequisites.Container` carries two `Check` members, and they are not interchangeable:

| Member | Takes | Side effects |
|---|---|---|
| `Check()` | nothing | stamps the frame and latches `available` |
| `Check(Requirements.ConditionInfo)` | the level being bought | none — stamps nothing, latches nothing |

The parameterized overload is the only requirement call safe to make from a read pass, and the level
passed to it is part of the answer: an upgrade is meaningfully checked at purchased plus queued plus
one, a structure at its persisted `quantity`. A verdict recorded without its exact input level and
owner kind cannot be compared with anything later.

Use it as a differential oracle beside your own expanded verdict, in the same generation — never as
an admission result. A boolean cannot name which leaf failed, cannot preserve the authored AND/OR
structure, and cannot expose a chain-planning dependency, which is the entire reason to walk the
graph. Disagreement between the two verdicts, or missing native evidence, is a loud failure of the
explanation rather than a tiebreak in favour of either side.

Bind it exactly or not at all: the exact container field, the exact one-parameter overload, and the
exact public `ConditionInfo(long)` constructor. An ambiguously resolved member is a reason to
withhold the whole comparison, because a mis-shaped call returns a confident wrong verdict instead
of an error.

**`CanPurchase` is not an oracle.** `StructureSO.CanPurchase` and `UpgradeSO.CanPurchase` are
action-admission surfaces with much larger unpublished dependency surfaces, and nothing static
proves them free of side effects under a read pass. Their component terms are readable separately —
see [native-action-surfaces.md](native-action-surfaces.md) — so a reader composes the verdict from
those terms rather than calling the composite.

### `ConsumableSO.CanFire()` — a conjunction, and it writes — `StaticallyVerified`

`CanFire` was in the same paragraph until its body was read. It is not opaque:

```text
ConsumableSO.CanFire() =>
  !IsOnCooldown() && HasEnoughUsage() && HasEnoughCost() && quantity > 0
    IsOnCooldown()   => currentCooldown > 0
    HasEnoughUsage() => usageCost.HasEnough()
    HasEnoughCost()  => consumeCost.HasEnough()
```

Every term is a fact a snapshot can hold, so the composite is derivable rather than absent — and it
has to be, because **calling it writes**:

```text
ResourceCostList.HasEnough() => every tuple: tuple.resource.HasAmount(tuple.valueBig)
ResourceSO.HasAmount(cost)   => bandwidthResource ? HasUsageMissing(cost)
                              : GameManager.DEBUG || quantity >= GetTrueSpend(cost)
ResourceSO.GetTrueSpend(c)   => c / quality.AsPercent()
ValueModifierRecord.AsPercent() => GetValue().AsPercent()
```

`ValueModifierRecord.GetValue()` is the accessor that recalculates and re-stamps its observable —
the one `NativeModifierRecordAccess` exists to route around. So every `ResourceCostList.HasEnough()`
is a write-on-read for each non-bandwidth resource in the list, and that is true of the eight other
`HasEnough()` call sites as well, not only of `CanFire`.

Two further notes for anyone porting the comparison. `HasEnough()` tests each tuple **independently**
and never sums duplicates. And `HasAmount` short-circuits to `true` under `GameManager.DEBUG`, which
a port deliberately does not reproduce: a shipped build never takes that branch, and reproducing it
would make affordability unconditionally true wherever it did.

## Challenges modify requirements

Challenges can modify requirements themselves, applied as passive modifiers — e.g., a challenge
applying `-5` to a research node's requirements, which the node displays as `leeway 5`. See
[challenges](../game-systems/challenges.md).
