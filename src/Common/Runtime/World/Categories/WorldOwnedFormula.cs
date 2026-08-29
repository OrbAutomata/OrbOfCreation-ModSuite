using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

internal readonly struct WorldConceptDrainBasis : IWorldEntity
{
    internal WorldConceptDrainBasis(
        Guid recipeId,
        Guid coreTypeId,
        Guid scalingId,
        int advancementLevel,
        int selectedLevel,
        in GameValueModifier requirementCostPenalty,
        in GameValueModifier requirementSpeedPenalty,
        BigDouble rarityMultiplier,
        bool costUsesRarity,
        bool speedUsesRarity)
    {
        RecipeId = recipeId;
        CoreTypeId = coreTypeId;
        ScalingId = scalingId;
        AdvancementLevel = advancementLevel;
        SelectedLevel = selectedLevel;
        RequirementCostPenalty = requirementCostPenalty;
        RequirementSpeedPenalty = requirementSpeedPenalty;
        RarityMultiplier = rarityMultiplier;
        CostUsesRarity = costUsesRarity;
        SpeedUsesRarity = speedUsesRarity;
    }

    internal Guid RecipeId { get; }
    public Guid EntityId => RecipeId;
    internal Guid CoreTypeId { get; }
    internal Guid ScalingId { get; }
    internal int AdvancementLevel { get; }
    internal int SelectedLevel { get; }
    internal GameValueModifier RequirementCostPenalty { get; }
    internal GameValueModifier RequirementSpeedPenalty { get; }
    internal BigDouble RarityMultiplier { get; }
    internal bool CostUsesRarity { get; }
    internal bool SpeedUsesRarity { get; }
}

internal sealed class WorldConceptDrainBasisBuffer
{
    private WorldConceptDrainBasis[] _rows = new WorldConceptDrainBasis[64];
    private int _count;
    internal int Count => _count;
    internal ref readonly WorldConceptDrainBasis this[int index] => ref _rows[index];
    internal void Reset() => _count = 0;
    internal void Append(in WorldConceptDrainBasis row)
    {
        if (_count == _rows.Length) Array.Resize(ref _rows, _rows.Length * 2);
        _rows[_count++] = row;
    }
}

internal readonly struct RawConceptDrainBasis
{
    internal RawConceptDrainBasis(
        Guid recipeId,
        Guid coreTypeId,
        Guid scalingId,
        int advancementLevel,
        in GameValueModifier requirementCostPenalty,
        in GameValueModifier requirementSpeedPenalty,
        bool costUsesRarity,
        bool speedUsesRarity)
    {
        RecipeId = recipeId;
        CoreTypeId = coreTypeId;
        ScalingId = scalingId;
        AdvancementLevel = advancementLevel;
        RequirementCostPenalty = requirementCostPenalty;
        RequirementSpeedPenalty = requirementSpeedPenalty;
        CostUsesRarity = costUsesRarity;
        SpeedUsesRarity = speedUsesRarity;
    }

    internal Guid RecipeId { get; }
    internal Guid CoreTypeId { get; }
    internal Guid ScalingId { get; }
    internal int AdvancementLevel { get; }
    internal GameValueModifier RequirementCostPenalty { get; }
    internal GameValueModifier RequirementSpeedPenalty { get; }
    internal bool CostUsesRarity { get; }
    internal bool SpeedUsesRarity { get; }
}

internal sealed class RawConceptDrainBasisBuffer
{
    private RawConceptDrainBasis[] _rows = new RawConceptDrainBasis[64];
    private int _count;
    internal int Count => _count;
    internal ref readonly RawConceptDrainBasis this[int index] => ref _rows[index];
    internal void Reset() => _count = 0;
    internal void Append(in RawConceptDrainBasis row)
    {
        if (_count == _rows.Length) Array.Resize(ref _rows, _rows.Length * 2);
        _rows[_count++] = row;
    }
}

internal static class WorldConceptDrainBasisDeriver
{
    internal static PublicationTable<WorldConceptDrainBasis> Build(
        RawConceptDrainBasisBuffer buffer,
        PublicationTable<WorldAlchemyType> types,
        PublicationTable<WorldNumberVariable> intVariables,
        PublicationTable<WorldAlchemyCost> costs,
        PublicationTable<WorldResource> resources)
    {
        var rows = new WorldConceptDrainBasis[buffer.Count];
        for (var index = 0; index < buffer.Count; index++)
        {
            var raw = buffer[index];
            var selectedLevel = 1;
            if (WorldLookup.TryFind(types, raw.CoreTypeId, out var type) &&
                type.SelectedLevelId != Guid.Empty &&
                WorldLookup.TryFind(intVariables, type.SelectedLevelId, out var selected))
                selectedLevel = selected.Value.ToInt();

            var rarity = BigDouble.Zero;
            if (WorldAlchemyCostLookup.TryFindRange(
                    costs, raw.RecipeId, WorldAlchemyCostKind.Bandwidth, out var start, out var count))
            {
                for (var offset = 0; offset < count; offset++)
                {
                    var cost = costs[start + offset];
                    if (WorldLookup.TryFind(resources, cost.ResourceId, out var resource))
                        rarity += new BigDouble(resource.Reading.Traits.RarityValue) * cost.Amount;
                }
            }

            var requirementCostPenalty = raw.RequirementCostPenalty;
            var requirementSpeedPenalty = raw.RequirementSpeedPenalty;
            rows[index] = new WorldConceptDrainBasis(
                raw.RecipeId,
                raw.CoreTypeId,
                raw.ScalingId,
                raw.AdvancementLevel,
                Math.Max(1, selectedLevel),
                in requirementCostPenalty,
                in requirementSpeedPenalty,
                rarity,
                raw.CostUsesRarity,
                raw.SpeedUsesRarity);
        }
        Array.Sort(rows, static (left, right) => left.RecipeId.CompareTo(right.RecipeId));
        return PublicationTable<WorldConceptDrainBasis>.Create(rows, rows.Length);
    }

    internal static bool TryFind(
        PublicationTable<WorldConceptDrainBasis> table,
        Guid recipeId,
        out WorldConceptDrainBasis row)
    {
        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = rows[middle].RecipeId.CompareTo(recipeId);
            if (comparison == 0) { row = rows[middle]; return true; }
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }
        row = default;
        return false;
    }
}

internal static class OwnedConceptDrainMath
{
    internal static bool TryComputeModifier(
        GameWorldState world,
        Guid recipeId,
        int quantity,
        out BigDouble modifier)
    {
        modifier = default;
        if (quantity <= 0 ||
            !WorldConceptDrainBasisDeriver.TryFind(world.ConceptDrainBasis, recipeId, out var basis) ||
            !TryRecord(world, recipeId, WorldModifierProgramRole.ConceptDrain, out var drain) ||
            !TryRecord(world, recipeId, WorldModifierProgramRole.ConceptSpeed, out var speed) ||
            !TryRecord(world, recipeId, WorldModifierProgramRole.ConceptFreeUsageSlots, out var free) ||
            !TryRecord(world, recipeId, WorldModifierProgramRole.ConceptOverdriveSpeed, out var overSpeed) ||
            !TryRecord(world, recipeId, WorldModifierProgramRole.ConceptOverdriveDrain, out var overDrain) ||
            !TryList(world, recipeId, WorldModifierProgramRole.ConceptCompletionCost,
                new BigDouble(basis.AdvancementLevel), new BigDouble(100), out var completion) ||
            !TryList(world, recipeId, WorldModifierProgramRole.ConceptDrainLevel,
                new BigDouble(basis.SelectedLevel - 1), BigDouble.One, out var level))
            return false;

        var usageRequirements = WorldRequirementEvaluator.Evaluate(
            world, recipeId, level: 0, WorldRequirementProgramKind.Usage);
        if (usageRequirements == WorldRequirementVerdict.Unevaluable) return false;
        var usageRequirementsMet = usageRequirements == WorldRequirementVerdict.Met;
        var requirementSpeed = usageRequirementsMet
            ? BigDouble.One
            : basis.RequirementSpeedPenalty.Adjust(BigDouble.One);
        var requirementCost = usageRequirementsMet
            ? BigDouble.One
            : basis.RequirementCostPenalty.Adjust(BigDouble.One);
        var baseModifier = drain * OrbGameMath.AsPercent(completion) *
            OrbGameMath.AsPercent(speed * requirementSpeed) * requirementCost * level;

        var q = new BigDouble(quantity);
        var costQuantity = AdjustedQuantity(q, basis.RarityMultiplier, basis.CostUsesRarity);
        var speedQuantity = AdjustedQuantity(q, basis.RarityMultiplier, basis.SpeedUsesRarity);
        if (!TryList(world, basis.ScalingId, WorldModifierProgramRole.InstanceScalingCost,
                BigDouble.Max(costQuantity - BigDouble.One, BigDouble.Zero), BigDouble.One,
                out var costPercent) ||
            !TryList(world, basis.ScalingId, WorldModifierProgramRole.InstanceScalingSpeed,
                BigDouble.Max(speedQuantity - BigDouble.One, BigDouble.Zero), BigDouble.One,
                out var speedPercent))
            return false;

        var overdriveQuantity = Math.Max(0, quantity - free.ToInt());
        var overdriveSpeed = new GameValueModifier(
                GameValueModifierType.MultiDiminishing,
                OrbGameMath.AsPercent(overSpeed) - BigDouble.One)
            .MultiplyScalar(new BigDouble(overdriveQuantity))
            .Adjust(BigDouble.One);
        var overdriveDrain = new GameValueModifier(
                GameValueModifierType.Reduction,
                BigDouble.One / OrbGameMath.AsPercent(overDrain) - BigDouble.One)
            .MultiplyScalar(new BigDouble(overdriveQuantity))
            .Adjust(BigDouble.One);

        modifier = baseModifier * costPercent * speedPercent * overdriveSpeed * overdriveDrain;
        return true;
    }

    internal static bool TryComputeCost(
        GameWorldState world,
        Guid recipeId,
        int quantity,
        Guid resourceId,
        BigDouble authoredAmount,
        out BigDouble amount)
    {
        amount = default;
        if (!TryComputeModifier(world, recipeId, quantity, out var modifier)) return false;
        amount = authoredAmount * OrbGameMath.AsPercent(modifier);
        return true;
    }

    private static BigDouble AdjustedQuantity(
        BigDouble quantity,
        BigDouble rarityMultiplier,
        bool useRarity) =>
        !useRarity || quantity <= 0
            ? quantity
            : quantity + rarityMultiplier * (quantity - BigDouble.One);

    private static bool TryRecord(
        GameWorldState world,
        Guid owner,
        WorldModifierProgramRole role,
        out BigDouble value) =>
        WorldModifierProgramMath.TryFoldRecord(
            world.ModifierPrograms, world.ModifierProgramEntries, owner, role, out value);

    private static bool TryList(
        GameWorldState world,
        Guid owner,
        WorldModifierProgramRole role,
        BigDouble scalar,
        BigDouble baseValue,
        out BigDouble value) =>
        WorldModifierProgramMath.TryAdjustScaledList(
            world.ModifierPrograms, world.ModifierProgramEntries,
            owner, role, scalar, baseValue, out value);
}

internal readonly struct RawMasteryCost
{
    internal RawMasteryCost(Guid recipeId, int position, Guid resourceId, BigDouble amount)
    {
        RecipeId = recipeId;
        Position = position;
        ResourceId = resourceId;
        Amount = amount;
    }
    internal Guid RecipeId { get; }
    internal int Position { get; }
    internal Guid ResourceId { get; }
    internal BigDouble Amount { get; }
}

internal readonly struct WorldMasteryCost : IWorldEntity
{
    internal WorldMasteryCost(
        Guid recipeId,
        int position,
        Guid resourceId,
        BigDouble amount,
        bool affordable)
    {
        RecipeId = recipeId;
        Position = position;
        ResourceId = resourceId;
        Amount = amount;
        Affordable = affordable;
    }
    internal Guid RecipeId { get; }
    public Guid EntityId => RecipeId;

    [WorldRowKeyPart(0)]
    internal int Position { get; }
    internal Guid ResourceId { get; }
    internal BigDouble Amount { get; }
    internal bool Affordable { get; }
}

internal sealed class RawMasteryCostBuffer
{
    private RawMasteryCost[] _rows = new RawMasteryCost[128];
    private int _count;
    internal int Count => _count;
    internal ref readonly RawMasteryCost this[int index] => ref _rows[index];
    internal void Reset() => _count = 0;
    internal void Append(in RawMasteryCost row)
    {
        if (_count == _rows.Length) Array.Resize(ref _rows, _rows.Length * 2);
        _rows[_count++] = row;
    }
}

internal static class OwnedMasteryCostMath
{
    internal static bool HasAmount(in WorldResource resource, BigDouble amount)
        => WorldResourceCoordinate.HasAmount(in resource, amount);

    internal static PublicationTable<WorldMasteryCost> Build(
        RawMasteryCostBuffer buffer,
        WorldSampleBuffer<RawSpellRecipeSample, WorldSpellRecipe> spells,
        Guid standardId,
        PublicationTable<WorldModifierProgram> programs,
        PublicationTable<WorldModifierProgramEntry> entries,
        PublicationTable<WorldResource> resources)
    {
        if (!WorldModifierProgramMath.TryFind(
                programs, standardId, WorldModifierProgramRole.SpellLevelingStandard, out _))
            return PublicationTable<WorldMasteryCost>.Empty;

        var rows = new WorldMasteryCost[buffer.Count];
        var written = 0;
        for (var spellIndex = 0; spellIndex < spells.Count; spellIndex++)
        {
            var spell = spells[spellIndex];
            var count = 0;
            for (var index = 0; index < buffer.Count; index++)
                if (buffer[index].RecipeId == spell.SpellRecipeId) count++;
            if (count == 0) continue;

            var costs = new GameResourceCost[count];
            var positions = new int[count];
            var at = 0;
            for (var index = 0; index < buffer.Count; index++)
            {
                var raw = buffer[index];
                if (raw.RecipeId != spell.SpellRecipeId) continue;
                costs[at] = new GameResourceCost(raw.ResourceId, raw.Amount);
                positions[at++] = raw.Position;
            }

            if (!ScaleCosts(costs, standardId, spell.MasteryLevel + 1, programs, entries))
                return PublicationTable<WorldMasteryCost>.Empty;
            for (var index = 0; index < costs.Length; index++)
            {
                var affordable = WorldLookup.TryFind(resources, costs[index].ResourceId, out var resource) &&
                    HasAmount(in resource, costs[index].Value);
                rows[written++] = new WorldMasteryCost(
                    spell.SpellRecipeId, positions[index], costs[index].ResourceId,
                    costs[index].Value, affordable);
            }
        }

        Array.Sort(rows, 0, written, SpellCostComparer.Instance);
        return PublicationTable<WorldMasteryCost>.Create(rows, written);
    }

    private static bool ScaleCosts(
        Span<GameResourceCost> costs,
        Guid standardId,
        int level,
        PublicationTable<WorldModifierProgram> programs,
        PublicationTable<WorldModifierProgramEntry> entries)
    {
        if (level != 1)
        {
            for (var index = 0; index < costs.Length; index++)
            {
                if (!WorldModifierProgramMath.TryAdjustScaledList(
                        programs, entries, standardId, WorldModifierProgramRole.SpellLevelingStandard,
                        new BigDouble(level - 1), costs[index].Value, out var value))
                    return false;
                costs[index] = costs[index].WithValue(value);
            }
        }
        GameCostMath.RoundToTwoSigs(costs);
        return true;
    }

    internal static bool TryFindRange(
        PublicationTable<WorldMasteryCost> table,
        Guid recipeId,
        out int start,
        out int count)
    {
        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (rows[middle].RecipeId.CompareTo(recipeId) < 0) low = middle + 1;
            else high = middle - 1;
        }
        start = low;
        count = 0;
        while (start + count < rows.Length && rows[start + count].RecipeId == recipeId) count++;
        return count > 0;
    }

    private sealed class SpellCostComparer : IComparer<WorldMasteryCost>
    {
        internal static readonly IComparer<WorldMasteryCost> Instance = new SpellCostComparer();
        public int Compare(WorldMasteryCost left, WorldMasteryCost right)
        {
            var recipe = left.RecipeId.CompareTo(right.RecipeId);
            return recipe != 0 ? recipe : left.Position.CompareTo(right.Position);
        }
    }
}
