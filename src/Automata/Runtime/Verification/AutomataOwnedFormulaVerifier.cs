using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>The live, manual-only native oracle for the owned Concept drain formula.</summary>
internal sealed class AutomataConceptDrainVerifier
{
    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly ConstructorInfo? _instanceConstructor;
    private readonly FieldInfo? _quantity;
    private readonly MethodInfo? _nativeModifier;
    private readonly MethodInfo? _maximum;
    private readonly MethodInfo? _recipeId;

    internal AutomataConceptDrainVerifier(Type recipeType, Type? instanceType)
    {
        _recipeId = recipeType.GetMethod("GetGuid", Instance, null, Type.EmptyTypes, null);
        _maximum = recipeType.GetMethod("GetMaxUsageSlots", Instance, null, Type.EmptyTypes, null);
        _instanceConstructor = instanceType?.GetConstructor(
            Instance, null, new[] { recipeType }, null);
        _quantity = instanceType?.GetField("quantity", Instance);
        _nativeModifier = instanceType?.GetMethod(
            "GetDrainCostMod", Instance, null, Type.EmptyTypes, null);
    }

    internal bool IsAvailable =>
        _recipeId?.ReturnType == typeof(Guid) && _maximum?.ReturnType == typeof(int) &&
        _instanceConstructor is not null && _quantity?.FieldType == typeof(int) &&
        _nativeModifier?.ReturnType.Name == "BigDouble";

    internal bool TryVerify(
        object recipe,
        GameWorldState world,
        DifferentialRun run,
        DifferentialVerificationSession timing,
        out string failure)
    {
        try
        {
            var id = (Guid)_recipeId!.Invoke(recipe, null)!;
            if (!WorldAlchemyInstanceLookup.TryFind(world.AlchemyInstances, id, out var active))
            {
                timing.RecordExpectedSkip();
                failure = string.Empty;
                return true;
            }
            if (!WorldConceptDrainBasisDeriver.TryFind(world.ConceptDrainBasis, id, out _) ||
                !WorldAlchemyCostLookup.TryFindRange(
                    world.AlchemyCosts, id, WorldAlchemyCostKind.RecipeDrain,
                    out var drainStart, out var drainCount))
            {
                failure = $"AlchemyRecipeSO {id} had no immutable owned drain basis.";
                return false;
            }

            var maximum = (int)_maximum!.Invoke(recipe, null)!;
            var current = Math.Max(active.Quantity, active.QueuedQuantity);
            if (!WorldModifierProgramMath.TryFoldRecord(
                    world.ModifierPrograms,
                    world.ModifierProgramEntries,
                    id,
                    WorldModifierProgramRole.ConceptFreeUsageSlots,
                    out var freeUsageSlots))
            {
                failure = $"AlchemyRecipeSO {id} had no free-slot modifier program.";
                return false;
            }
            var firstOverdrive = freeUsageSlots.ToInt() < int.MaxValue
                ? freeUsageSlots.ToInt() + 1
                : int.MaxValue;
            var quantities = Quantities(current, firstOverdrive, maximum);
            for (var index = 0; index < quantities.Count; index++)
            {
                var quantity = quantities[index];
                var ourStart = Stopwatch.GetTimestamp();
                if (!OwnedConceptDrainMath.TryComputeModifier(world, id, quantity, out var ours))
                {
                    failure = $"AlchemyRecipeSO {id} quantity={quantity} could not evaluate owned modifier.";
                    return false;
                }
                var ourTicks = Stopwatch.GetTimestamp() - ourStart;

                var native = _instanceConstructor!.Invoke(new[] { recipe });
                _quantity!.SetValue(native, quantity);
                var theirStart = Stopwatch.GetTimestamp();
                var theirs = (BigDouble)_nativeModifier!.Invoke(native, null)!;
                var theirTicks = Stopwatch.GetTimestamp() - theirStart;
                timing.RecordTiming(ourTicks, theirTicks);

                var prefix = $"AlchemyRecipeSO quantity={quantity}";
                run.Compare(id, $"{prefix} term=modifier", ours, theirs);
                for (var offset = 0; offset < drainCount; offset++)
                {
                    var authored = world.AlchemyCosts[drainStart + offset];
                    if (!OwnedConceptDrainMath.TryComputeCost(
                            world, id, quantity, authored.ResourceId, authored.Amount, out var ourCost))
                    {
                        failure = $"AlchemyRecipeSO {id} quantity={quantity} could not multiply drain tuple.";
                        return false;
                    }
                    var nativeCost = authored.Amount * OrbGameMath.AsPercent(theirs);
                    run.Compare(
                        id,
                        $"{prefix} term=drain resource={authored.ResourceId}",
                        ourCost,
                        nativeCost);
                }
            }

            failure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            failure = $"reading AlchemyRecipeSO drain oracle threw: {ex.GetBaseException().Message}";
            return false;
        }
    }

    private static List<int> Quantities(int current, int firstOverdrive, int maximum)
    {
        var values = new List<int>(8);
        Add(values, 1);
        Add(values, current);
        Add(values, current < int.MaxValue ? current + 1 : current);
        Add(values, firstOverdrive);
        Add(values, 8); // representative member of the former halving ladder.
        if (maximum > 0)
        {
            Add(values, maximum / 2);
            Add(values, maximum);
        }
        return values;
    }

    private static void Add(List<int> values, int value)
    {
        if (value > 0 && !values.Contains(value)) values.Add(value);
    }
}

/// <summary>Separately verifies the owned spell cost vector and its holdings-dependent verdict.</summary>
internal sealed class AutomataSpellLevelVerifier
{
    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly MethodInfo? _recipeId;
    private readonly MethodInfo? _getLevelCost;
    private readonly MethodInfo? _hasEnough;
    private readonly Func<object, IList?>? _costs;
    private readonly Func<object, Guid>? _resourceId;
    private readonly Func<object, BigDouble>? _amount;

    internal AutomataSpellLevelVerifier(Type spellType, Type? costListType)
    {
        _recipeId = spellType.GetMethod("GetGuid", Instance, null, Type.EmptyTypes, null);
        _getLevelCost = spellType.GetMethod("GetLevelCost", Instance, null, Type.EmptyTypes, null);
        _hasEnough = costListType?.GetMethod("HasEnough", Instance, null, Type.EmptyTypes, null);
        _costs = NativeAccessorBinder.CollectionField(costListType, "costs");
        var entryType = NativeAccessorBinder.CollectionElementType(costListType, "costs");
        _resourceId = NativeAccessorBinder.ReferenceGuid(entryType, "resource");
        _amount = NativeAccessorBinder.Field<BigDouble>(entryType, "valueBig");
    }

    internal bool IsAvailable =>
        _recipeId?.ReturnType == typeof(Guid) && _getLevelCost is not null &&
        _hasEnough?.ReturnType == typeof(bool) && _costs is not null &&
        _resourceId is not null && _amount is not null;

    internal bool TryVerifyCost(
        object spell,
        GameWorldState world,
        DifferentialRun run,
        DifferentialVerificationSession timing,
        out string failure) =>
        TryRead(spell, world, compareAffordability: false, run, timing, out failure);

    internal bool TryVerifyAffordability(
        object spell,
        GameWorldState world,
        DifferentialRun run,
        DifferentialVerificationSession timing,
        out string failure) =>
        TryRead(spell, world, compareAffordability: true, run, timing, out failure);

    private bool TryRead(
        object spell,
        GameWorldState world,
        bool compareAffordability,
        DifferentialRun run,
        DifferentialVerificationSession timing,
        out string failure)
    {
        try
        {
            var id = (Guid)_recipeId!.Invoke(spell, null)!;
            if (!WorldLookup.TryFind(world.SpellRecipes, id, out var published))
            {
                failure = $"SpellRecipeSO {id} was absent from the immutable world.";
                return false;
            }

            if (compareAffordability)
            {
                var ourStart = Stopwatch.GetTimestamp();
                var ownedAffordable = published.MasteryLevelAffordable;
                var ourTicks = Stopwatch.GetTimestamp() - ourStart;
                var theirStart = Stopwatch.GetTimestamp();
                var nativeCostList = _getLevelCost!.Invoke(spell, null) ??
                    throw new InvalidOperationException("GetLevelCost returned null");
                var nativeAffordable = (bool)_hasEnough!.Invoke(nativeCostList, null)!;
                var theirTicks = Stopwatch.GetTimestamp() - theirStart;
                timing.RecordTiming(ourTicks, theirTicks);
                run.Compare(
                    id, "SpellRecipeSO term=affordability",
                    ownedAffordable ? BigDouble.One : BigDouble.Zero,
                    nativeAffordable ? BigDouble.One : BigDouble.Zero);
                failure = string.Empty;
                return true;
            }

            var costStart = Stopwatch.GetTimestamp();
            var hasOwnedRows = OwnedMasteryCostMath.TryFindRange(
                world.MasteryCosts, id, out var start, out var count);
            var costTicks = Stopwatch.GetTimestamp() - costStart;
            var nativeStart = Stopwatch.GetTimestamp();
            var native = _getLevelCost!.Invoke(spell, null) ??
                throw new InvalidOperationException("GetLevelCost returned null");
            var nativeTicks = Stopwatch.GetTimestamp() - nativeStart;
            timing.RecordTiming(costTicks, nativeTicks);

            var nativeRows = _costs!(native);
            var nativeCount = nativeRows?.Count ?? 0;
            run.Compare(
                id, "SpellRecipeSO term=cost-count",
                new BigDouble(hasOwnedRows ? count : 0), new BigDouble(nativeCount));
            if ((hasOwnedRows ? count : 0) != nativeCount)
            {
                failure = string.Empty;
                return true;
            }
            for (var index = 0; index < nativeCount; index++)
            {
                var row = nativeRows![index] ??
                    throw new InvalidOperationException($"GetLevelCost row {index} was null");
                var ours = world.MasteryCosts[start + index];
                var nativeResource = _resourceId!(row);
                run.Compare(
                    id, $"SpellRecipeSO term=resource-identity position={index}",
                    ours.ResourceId == nativeResource ? BigDouble.One : BigDouble.Zero,
                    BigDouble.One);
                run.Compare(
                    id, $"SpellRecipeSO term=cost position={index} resource={nativeResource}",
                    ours.Amount, _amount!(row));
            }

            failure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            failure = $"reading SpellRecipeSO oracle threw: {ex.GetBaseException().Message}";
            return false;
        }
    }
}

/// <summary>
/// Checks the derived spell type layer against the factor <c>Spell.GetPower()</c> multiplies in.
/// </summary>
/// <remarks>
/// <para>
/// This is the one type total that is a factor, so it is the one that can be checked at all. Every
/// other taxonomy distributes into its members, which leaves the type's contribution already inside
/// the member value the world publishes and nothing independent to compare; <c>SpellTypeSO</c>
/// distributes nothing, so this product stands beside the member value and the game multiplies it in
/// whether or not the suite reproduced it faithfully.
/// </para>
/// <para>
/// One term, deliberately. The oracle is the whole product, and comparing the factors underneath it
/// would mean reading the effective type set natively too — a second reading whose only use is to
/// explain a disagreement that the one number has already reported.
/// </para>
/// </remarks>
internal sealed class AutomataSpellTypeLayerVerifier
{
    private readonly Func<object, BigDouble>? _typePowerPercent;

    internal AutomataSpellTypeLayerVerifier(Type? spellType) =>
        _typePowerPercent = NativeAccessorBinder.Call<BigDouble>(
            spellType, "GetSpellTypePowerPercent");

    internal bool IsAvailable => _typePowerPercent is not null;

    /// <summary>
    /// Compares one loadout position. The position is the key rather than an identity, because that
    /// is what the game addresses a cast by and what the derived rows are keyed on.
    /// </summary>
    internal bool TryVerify(
        object spell,
        int slotIndex,
        GameWorldState world,
        DifferentialRun run,
        DifferentialVerificationSession timing,
        out string failure)
    {
        try
        {
            if (!WorldSpellSlotLookup.TryFind(world.SpellSlots, slotIndex, out var slot))
            {
                failure = $"spell slot {slotIndex} was absent from the immutable world.";
                return false;
            }

            // An empty position has no types to multiply and no row to compare, and asking the game
            // for the product of a spell that is not there is not a reading anyone wants taken.
            if (!slot.Occupied)
            {
                timing.RecordExpectedSkip();
                failure = string.Empty;
                return true;
            }

            // The deriver fails closed: a slot naming a type the world did not publish gets no row
            // at all. That absence is the finding, so it is reported rather than skipped over.
            if (!WorldSpellTypeResonanceLookup.TryFind(
                    world.SpellTypeResonance, slotIndex, out var ours))
            {
                failure = $"spell slot {slotIndex} published no derived spell type layer.";
                return false;
            }

            var ourStart = Stopwatch.GetTimestamp();
            var ourPercent = ours.TypePowerPercent;
            var ourTicks = Stopwatch.GetTimestamp() - ourStart;
            var theirStart = Stopwatch.GetTimestamp();
            var theirPercent = _typePowerPercent!(spell);
            var theirTicks = Stopwatch.GetTimestamp() - theirStart;
            timing.RecordTiming(ourTicks, theirTicks);

            run.Compare(
                ours.SpellRecipeId,
                $"Spell slot={slotIndex} term=type-power-percent",
                ourPercent,
                theirPercent);

            failure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            failure = $"reading the Spell type layer oracle threw: {ex.GetBaseException().Message}";
            return false;
        }
    }
}
