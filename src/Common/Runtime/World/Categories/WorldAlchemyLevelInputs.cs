using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>One active ordinary recipe's native-free inputs to AlchemyInstance's drain formula.</summary>
internal readonly struct WorldAlchemyLevelSample
{
    internal WorldAlchemyLevelSample(
        Guid recipeId,
        int position,
        int quantity,
        int selectedLevel,
        int advancement,
        BigDouble drain,
        BigDouble speed,
        GameValueModifier requirementSpeed,
        GameValueModifier requirementCost,
        int freeUses,
        BigDouble overdriveSpeed,
        BigDouble overdriveDrain,
        WorldInstanceScalingBasis scaling,
        BigDouble appliedRatio,
        bool applied,
        PublicationTable<WorldLevelPrice> authored,
        PublicationTable<WorldLevelPrice> nativeCurrent,
        PublicationTable<WorldLevelPrice> bandwidth)
    {
        RecipeId = recipeId;
        Position = position;
        Quantity = quantity;
        SelectedLevel = selectedLevel;
        Advancement = advancement;
        Drain = drain;
        Speed = speed;
        RequirementSpeed = requirementSpeed;
        RequirementCost = requirementCost;
        FreeUses = freeUses;
        OverdriveSpeed = overdriveSpeed;
        OverdriveDrain = overdriveDrain;
        Scaling = scaling;
        AppliedRatio = appliedRatio;
        Applied = applied;
        Authored = authored;
        NativeCurrent = nativeCurrent;
        Bandwidth = bandwidth;
    }
    internal Guid RecipeId { get; }
    internal int Position { get; }
    internal int Quantity { get; }
    internal int SelectedLevel { get; }
    internal int Advancement { get; }
    internal BigDouble Drain { get; }
    internal BigDouble Speed { get; }
    internal GameValueModifier RequirementSpeed { get; }
    internal GameValueModifier RequirementCost { get; }
    internal int FreeUses { get; }
    internal BigDouble OverdriveSpeed { get; }
    internal BigDouble OverdriveDrain { get; }
    internal WorldInstanceScalingBasis Scaling { get; }
    internal BigDouble AppliedRatio { get; }
    internal bool Applied { get; }
    internal PublicationTable<WorldLevelPrice> Authored { get; }
    internal PublicationTable<WorldLevelPrice> NativeCurrent { get; }
    internal PublicationTable<WorldLevelPrice> Bandwidth { get; }
}

/// <summary>Captured ordinary Alchemy, including exact modifier programs for prospective levels.</summary>
internal sealed class WorldAlchemyLevelInputs
{
    private readonly string _failure;
    private readonly string _futureFailure;
    private readonly PublicationTable<WorldAlchemyLevelSample> _samples;
    private readonly PublicationTable<WorldModifierProgram> _programs;
    private readonly PublicationTable<WorldModifierProgramEntry> _entries;

    internal WorldAlchemyLevelInputs(string failure, PublicationTable<WorldAlchemyLevelSample> samples,
        PublicationTable<WorldModifierProgram> programs, PublicationTable<WorldModifierProgramEntry> entries, string futureFailure = "")
    {
        _failure = failure;
        _futureFailure = futureFailure;
        _samples = samples;
        _programs = programs;
        _entries = entries;
    }

    internal static WorldAlchemyLevelInputs Unavailable(string reason) => new(reason,
        PublicationTable<WorldAlchemyLevelSample>.Empty,
        PublicationTable<WorldModifierProgram>.Empty, PublicationTable<WorldModifierProgramEntry>.Empty);

    internal WorldLevelDomainQuote Build(PublicationTable<WorldResource> resources, int current, int maximum,
        GameWorldState? requirementWorld = null)
    {
        if (_failure.Length != 0) return WorldLevelDomainQuote.Unavailable(_failure);
        if (current < 1 || maximum < current)
            return WorldLevelDomainQuote.Unavailable("The Alchemy level range is unavailable.");
        var actual = new Dictionary<Guid, BigDouble>();
        // Attribute every sample before attempting any forecast: a future-price failure must not
        // erase another recipe's real drain or prevent a justified reduction.
        foreach (var sample in _samples.AsSpan())
        {
            if (!Finite(sample.AppliedRatio) || sample.AppliedRatio < 0)
                return WorldLevelDomainQuote.Unavailable("An ordinary Alchemy applied drain ratio is unreadable.");
            foreach (var price in sample.NativeCurrent.AsSpan())
            {
                if (!WorldLookup.TryFind(resources, price.ResourceId, out _) || !Finite(price.Amount) || price.Amount < 0)
                    return WorldLevelDomainQuote.Unavailable("An ordinary Alchemy drain named an unreadable resource or amount.");
                Add(actual, price.ResourceId, sample.Applied ? price.Amount * sample.AppliedRatio : BigDouble.Zero);
            }
        }
        var prospective = new Dictionary<Guid, BigDouble>();
        var nextReason = _futureFailure;
        foreach (var entry in _entries.AsSpan())
        {
            if (!Enum.IsDefined(typeof(GameValueModifierType), entry.Type) || !Finite(entry.Amount))
            { nextReason = "An ordinary Alchemy prospective modifier has an unsupported kind or amount."; break; }
        }
        foreach (var sample in _samples.AsSpan())
        {
            if (nextReason.Length != 0) break;
            if (sample.SelectedLevel != current)
            { nextReason = "An ordinary recipe and the Alchemy dial disagree on their selected level."; break; }
            if (!sample.Applied && sample.Authored.Count != 0)
            { nextReason = "An active ordinary recipe has not applied its drain yet."; break; }
            if (requirementWorld is null)
            { nextReason = "The ordinary Alchemy usage prerequisite snapshot is unavailable."; break; }
            var verdict = WorldRequirementEvaluator.Evaluate(requirementWorld, sample.RecipeId,
                WorldRequirementEvaluator.UnlockCheckLevel, WorldRequirementProgramKind.Usage);
            if (verdict == WorldRequirementVerdict.Unevaluable)
            { nextReason = "An ordinary Alchemy usage prerequisite cannot be evaluated from the captured world."; break; }
            var usageMet = verdict == WorldRequirementVerdict.Met;
            var rarity = BigDouble.Zero;
            foreach (var price in sample.Bandwidth.AsSpan())
            {
                if (!WorldLookup.TryFind(resources, price.ResourceId, out var resource))
                { nextReason = "An ordinary Alchemy bandwidth price named an unreadable resource."; break; }
                rarity += new BigDouble(resource.Reading.Traits.RarityValue) * price.Amount;
            }
            if (nextReason.Length != 0) break;
            if (!TryModifier(sample, rarity, current, usageMet, out var currentModifier) ||
                !TryModifier(sample, rarity, current < maximum ? current + 1 : current, usageMet, out var nextModifier))
            { nextReason = "An ordinary recipe's complete drain modifier program is unavailable."; break; }
            var computedCurrent = new Dictionary<Guid, BigDouble>();
            foreach (var price in sample.Authored.AsSpan())
            {
                if (!WorldLookup.TryFind(resources, price.ResourceId, out _) || !Finite(price.Amount) || price.Amount < 0)
                { nextReason = "An ordinary Alchemy authored drain named an unreadable resource or amount."; break; }
                Add(computedCurrent, price.ResourceId, price.Amount * OrbGameMath.AsPercent(currentModifier));
                // Quote the requested drain; future shortage throttling must not make an expensive level look safe.
                Add(prospective, price.ResourceId, price.Amount * OrbGameMath.AsPercent(nextModifier));
            }
            if (nextReason.Length != 0) break;
            var nativeCurrent = new Dictionary<Guid, BigDouble>();
            foreach (var price in sample.NativeCurrent.AsSpan()) Add(nativeCurrent, price.ResourceId, price.Amount);
            if (!Same(computedCurrent, nativeCurrent))
            { nextReason = "The ordinary Alchemy drain has not settled to its captured selected level and modifiers."; break; }
        }
        return new WorldLevelDomainQuote(true, string.Empty, _samples.Count > 0,
            current, maximum, Prices(actual), nextReason.Length == 0 ? Prices(prospective) : PublicationTable<WorldLevelPrice>.Empty,
            nextComplete: nextReason.Length == 0, nextReason: nextReason);
    }

    private bool TryModifier(in WorldAlchemyLevelSample sample, BigDouble rarity, int level, bool usageMet, out BigDouble modifier)
    {
        modifier = default;
        if (!Valid(sample.RequirementSpeed) || !Valid(sample.RequirementCost) ||
            !Finite(sample.Drain) || !Finite(sample.Speed) || !Finite(sample.OverdriveSpeed) || !Finite(sample.OverdriveDrain)) return false;
        if (!WorldModifierProgramMath.TryAdjustScaledList(_programs, _entries,
                sample.RecipeId, WorldModifierProgramRole.ConceptCompletionCost,
                new BigDouble(sample.Advancement), new BigDouble(100), out var completion) ||
            !WorldModifierProgramMath.TryAdjustScaledList(_programs, _entries,
                sample.RecipeId, WorldModifierProgramRole.ConceptDrainLevel,
                new BigDouble(level - 1), BigDouble.One, out var levelFactor)) return false;
        var quantity = new BigDouble(sample.Quantity);
        var costQuantity = GameAlchemyDrainMath.AdjustedQuantity(quantity, rarity, sample.Scaling.CostUsesRarity);
        var speedQuantity = GameAlchemyDrainMath.AdjustedQuantity(quantity, rarity, sample.Scaling.SpeedUsesRarity);
        if (!WorldModifierProgramMath.TryAdjustScaledList(_programs, _entries,
                sample.Scaling.ScalingId, WorldModifierProgramRole.InstanceScalingCost,
                BigDouble.Max(costQuantity - BigDouble.One, BigDouble.Zero), BigDouble.One, out var costFactor) ||
            !WorldModifierProgramMath.TryAdjustScaledList(_programs, _entries,
                sample.Scaling.ScalingId, WorldModifierProgramRole.InstanceScalingSpeed,
                BigDouble.Max(speedQuantity - BigDouble.One, BigDouble.Zero), BigDouble.One, out var speedFactor)) return false;
        var speedPenalty = usageMet ? BigDouble.One : sample.RequirementSpeed.Adjust(BigDouble.One);
        var costPenalty = usageMet ? BigDouble.One : sample.RequirementCost.Adjust(BigDouble.One);
        modifier = GameAlchemyDrainMath.ComputeModifier(sample.Drain, completion, sample.Speed * speedPenalty,
            costPenalty, levelFactor, costFactor, speedFactor, sample.Quantity,
            sample.FreeUses, sample.OverdriveSpeed, sample.OverdriveDrain);
        return Finite(modifier) && modifier >= 0 && Finite(sample.AppliedRatio) && sample.AppliedRatio >= 0;
    }

    private static bool Same(Dictionary<Guid, BigDouble> expected, Dictionary<Guid, BigDouble> native)
    {
        if (expected.Count != native.Count) return false;
        foreach (var pair in expected)
            if (!native.TryGetValue(pair.Key, out var value) || pair.Value != value) return false;
        return true;
    }

    private static bool Valid(GameValueModifier modifier) =>
        Enum.IsDefined(typeof(GameValueModifierType), modifier.Type) && Finite(modifier.Amount);
    private static bool Finite(BigDouble value) => !double.IsNaN(value.Mantissa) && !double.IsInfinity(value.Mantissa);
    private static void Add(Dictionary<Guid, BigDouble> rows, Guid id, BigDouble amount) =>
        rows[id] = rows.TryGetValue(id, out var previous) ? previous + amount : amount;
    private static PublicationTable<WorldLevelPrice> Prices(Dictionary<Guid, BigDouble> values)
    {
        var rows = new WorldLevelPrice[values.Count];
        var position = 0;
        foreach (var pair in values) rows[position++] = new WorldLevelPrice(-1, pair.Key, pair.Value, WorldLevelPriceKind.Drain);
        Array.Sort(rows, static (left, right) => left.ResourceId.CompareTo(right.ResourceId));
        return PublicationTable<WorldLevelPrice>.Create(rows, rows.Length);
    }
}
