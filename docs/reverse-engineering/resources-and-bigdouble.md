# Resources and BigDouble

[Back to index](README.md)

The game math you have to reproduce **exactly** if you want to price anything without asking the
game. Everything here is IL-derived; parity against a live save is the only proof that a
reimplementation is right.

## BigDouble

`BigDouble` lives in `Assembly-CSharp-firstpass.dll` — identical across both admitted platform
pairs — as a value type of `double mantissa` (`Ma`) and `long exponent` (`Ex`), representing
`mantissa × 10^exponent`; `[1.446, 23]` is roughly `1.446 × 10²³`.

```csharp
new BigDouble(double mantissa, long exponent)
BigDouble.Normalize(double mantissa, long exponent)
BigDouble.Pow10(long exponent)
BigDouble.Parse(string value)
```

Arithmetic and comparison operators are defined. Use constructors and operators; setting `Ma` and
`Ex` independently skips normalization and produces a value the game's own comparisons will
disagree with.

## ResourceSO

Saved state:

```csharp
bool visible;
BigDouble quantity;
BigDouble lifetimeQuantity;
BigDouble discoveryTime;
BigDouble appliedMaxQuantity;
long appliedLevels;
List<QuantityTimer> replenishTimers;
List<QuantityTimer> decayTimers;
```

Alongside it are modifier records for rate, capacity, gain, drain, loss, reservation, decay,
replenishment, and overflow.

| Member | Role |
|---|---|
| `GetQuantity()` | stored quantity |
| `GetTrueQuantity()` | quantity after quality interpretation |
| `GetMissing()` | headroom to the maximum |
| `Gain(...)` | the normal gain path: modifiers, notifications, reverberation |
| `GainInternal(...)` | low-level capped/overflow addition, governed by `canOverflow` |
| `Spend(...)` | the normal spending path |
| `SetQuantity(BigDouble)` | direct assignment, clamped |
| `SetToCap()` | fill to capacity |
| `GetLifeTimeQuantity()` | lifetime accumulated quantity |
| `GetQuantityObservable()` | the UI/system notification source |

`SetQuantity` clamps: for a capped resource `quantity = Max(0, Min(requested, maxQuantity))`,
otherwise `quantity = requested`. It therefore **cannot** exceed capacity, and it does not fire the
gain path.

`Gain` rejects a zero amount, optionally resets loss state, applies `gainRate` unless `isRaw` is
set, registers lifetime gain unless the gain is a splash, calls `GainInternal`, updates the
quantity and channel observables when asked, and optionally accumulates reverberation. For a debug
grant it is the semantically safer of the two; for an exact target value `SetQuantity` is simpler
but still clamped. `ResourceSO.MakeVisible()` is **private** — see
[modding-hooks.md](modding-hooks.md).

## Containers and collection edges

| Type | Bulk-readable contents |
|---|---|
| `ResourceListVariable` | `GetAll()`, which returns `ResourceSO.All` — not a richer snapshot API |
| `ResourceCostList` | `costs : List<ResourceTuple>`; `GetEntries()` returns the same list |
| `ResourceFillList` | partial investment into costs |
| `ResourceManager` | `allResources` plus a generated-resource cache |
| `Prerequisites.Container` | `prerequisites : List<IRequirementCondition>` |
| `AndRequirement` / `OrRequirement` | nested condition lists |
| `ValueModifierList` | `modifiers` and a **separate** `exponents` list |
| `ModifierRecord` | `passiveModifiers` and `activeModifiers` dictionaries |
| `ActionableListVariable` | the queue list and its maximum-queued-item variable |

The three public bulk roots are `StructureSO.All`, `UpgradeSO.All`, and `ResourceSO.All`. Modifier
arithmetic and fold order are player-observable and documented in
[`game-systems/modifiers.md`](../game-systems/modifiers.md); what the code offers
is the operand graph above, in native combination order.

`ValueModifierVariable.GetValue()` is the one `GetValue()` in this assembly that is a plain field
read — no dirty flag, no recalculation, no observable, so it cannot write. Read the field anyway:
the rule that a reader never calls an accessor is worth more than the single call it would save,
and the next `GetValue()` someone reaches for will not be this one.

## Rounding

Two variants, not interchangeable: `RoundToTwoSigsEarly()` closes the Structure cost chain and
`RoundToTwoSigs()` closes the Upgrade chain. Bandwidth affordability uses the separate
`Utils.SnapFloorToInt()` rule documented below, not a significant-digit round.

## Structure cost

Let `q = quantity + queuedQuantity`.

```text
attributeCost = baseCost.AdjustAsAttribute()
scaling       = costPerQuantity.GetModifier()
                    .MultiplyScalar(costScalingMod.AsPercent())
scaledCost    = attributeCost.AdjustWith(scaling.MultiplyScalar(q))
purchaseCost  = scaledCost
                    .Multiply(GetNextCostMod().AsPercent())
                    .RoundToTwoSigsEarly()
```

`AdjustAsAttribute()` multiplies each tuple by `resource.GetAttributeCostMod().AsPercent()`, whose
resource factor is:

```text
attributeCostMod.GetValue()
    / Pow(quality.AsPercent(), Player.AttributeQualityBonus)
```

`GetNextCostMod()` combines a passive floor with the scaled term:

```text
passive = passiveCostMod.GetValue()
scaled  = costPerQuantity.GetMod().MultiplyScalar(q).Adjust(1)
base    = Max(passive / 100, scaled)
active  = activeCostMod.GetValue() * Player.GetStructureCost().AsPercent()
result  = base * active.AsPercent()
```

The `Max(passive / 100, scaled)` term is the one people drop.

`GetNextQuantity()` and the committed quantity are the same number, which their names hide:
`GetNextQuantity()` is `GetBaseLevel() + queuedQuantity`, `GetBaseLevel()` returns `quantity`, and
`q` above is `quantity + queuedQuantity`. Stated because a future reader will assume a bug.

## Upgrade cost

```text
index = level + queuedLevels
if maxLevel > 0:
    index = Min(index, maxLevel - 1)

levelArgument = index + 1
if levelArgument == 1:
    cost = clone(resourceCost)
else:
    modifiers = resourceCostModPerLevel.MultiplyScalar(levelArgument - 1)
    cost      = modifiers.Adjust(each base tuple)

purchaseCost = cost.RoundToTwoSigs()
```

The native object **caches its calculated cost by cost level**. Whether a modifier change
invalidates that cache is not established from IL; treat a cached price as stale after any
modifier movement.

A modifier list is read by content, while a single modifier is read by identity.
`costPerQuantity` is a `ValueModifierRef` pointing at one registered
`ValueModifierVariable`, so it travels as a Guid. `resourceCostModPerLevel` is a
`ModifierListRef` whose `Standard` subclass resolves one of nine shared lists off
`GlobalValues.instance` by a `refType` enum rather than by naming a variable, so carrying an
identity for it would mean reproducing that nine-way mapping.

## Affordability

### Display and spendability use different native classifiers

Statically verified against the audited v1.0.5 Windows assembly: the resource counter and the
transaction boundary do not classify resources with the same flag.

- `ResourceSO.GetDisplayQuantity()` calls `IsInvertedResource()`. It returns `GetMissing()` for an
  inverted resource and `GetQuantity()` otherwise. `UIResourceDisplay.RenderContent()` repeats that
  choice for `trueQuantity`, then floors the displayed quantity only when
  `IsBandwidthResource()` is true.
- `ResourceSO.HasAmount(BigDouble)` branches on the `bandwidthResource` field. Bandwidth delegates
  to `HasUsageMissing(BigDouble)`; ordinary resources compare the stored `quantity` with
  `GetTrueSpend(BigDouble)`.
- `ResourceSO.HasUsageMissing(BigDouble)` compares `GetMissing()` with the nominal cost after both
  operands pass through `Utils.SnapFloorToInt(BigDouble)`.

The two flags are independent in shipped data. A display amount therefore answers only “what does
the counter show?”, while a spendable amount answers “what operand does native admission compare?”
They must retain distinct names rather than sharing one guessed resource-family classifier.

Ordinary resource:

```text
qualityFactor        = quality.GetValue().AsPercent()
trueQuantity         = quantity * qualityFactor
trueSpend(nominal)   = nominal / qualityFactor
hasAmount            = GameManager.DEBUG || quantity >= trueSpend(nominal)
```

`GameManager.DEBUG` short-circuits affordability entirely — a debug build says yes to everything,
so parity runs against it prove nothing about price.

Bandwidth resource — this one is genuinely surprising:

```text
missing   = Max(maxQuantity.GetValue() - quantity, 0)
hasAmount = SnapFloorToInt(missing)
            >= SnapFloorToInt(nominalCost)
SnapFloorToInt(value) = floor(value.ToFloat() + 0.001f)
```

Both sides are snap-floored before the comparison, so a bandwidth cost is admitted or refused at
those integer boundaries rather than at exact `BigDouble` ones.

`ResourceCostList.HasEnough()` checks each tuple **independently**. A cost list naming the same
resource twice is therefore not summed by the game; anything that combines duplicate tuples by
UUID is imposing a stricter rule than the native one, and both behaviours have to be preserved
deliberately rather than merged.

Native spending stays authoritative regardless: spend paths can involve decay and replenishment,
so matching the admission arithmetic does not license predicting or performing the mutation
yourself.
