using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>One crafting recipe type as published: what a recipe of this type starts at, the magnitude curve it follows, and how loaded its composed records are.</summary>
/// <remarks>
/// The counted records are <c>OrderedMultiplierRecord</c>s and <c>MergingModifierRecord</c>s, and
/// neither is a value at all. They are distributors: they hold modifiers and push them, transformed,
/// into the member records registered with <c>AddRecord</c>. An alchemy type's <c>power</c> pushes
/// into every one of its recipes' <c>power</c>, and it is that recipe-level
/// <c>ValueModifierRecord</c> — already collected, cached value and all — that carries the result.
/// <para>
/// So the distributed effect is not missing from the snapshot; it arrives on the members. What is
/// absent from this row is the distributor's own total, the <c>Adjust(100)</c> its tooltip shows.
/// That is pure arithmetic over its two modifier dictionaries, and the entries it needs are
/// variable-size: the <c>type modifier contributions</c> category publishes them and derivation does
/// the fold. The count here is the game's own <c>HasActiveElements()</c>, and
/// <see cref="WorldTypeModifier"/> says why that total must not be multiplied into a member value
/// this snapshot already carries.
/// </para>
/// </remarks>
internal readonly struct WorldCraftingRecipeType : IWorldEntity
{
    internal WorldCraftingRecipeType(
        Guid craftingRecipeTypeId,
        int startingLevel,
        int maxStartingLevel,
        string craftVerb,
        bool isLevelType,
        bool initiated,
        double magnitudeLoss,
        double magnitudeTime,
        BigDouble magnitudeIncrement,
        int powerModifiers,
        int speedModifiers,
        int costModModifiers,
        int costIncrementModModifiers,
        int efficiencyModModifiers,
        int autoPenaltyModModifiers,
        int multiPenaltyModModifiers)
    {
        CraftingRecipeTypeId = craftingRecipeTypeId;
        StartingLevel = startingLevel;
        MaxStartingLevel = maxStartingLevel;
        CraftVerb = craftVerb;
        IsLevelType = isLevelType;
        Initiated = initiated;
        MagnitudeLoss = magnitudeLoss;
        MagnitudeTime = magnitudeTime;
        MagnitudeIncrement = magnitudeIncrement;
        PowerModifiers = powerModifiers;
        SpeedModifiers = speedModifiers;
        CostModModifiers = costModModifiers;
        CostIncrementModModifiers = costIncrementModModifiers;
        EfficiencyModModifiers = efficiencyModModifiers;
        AutoPenaltyModModifiers = autoPenaltyModModifiers;
        MultiPenaltyModModifiers = multiPenaltyModModifiers;
    }

    internal Guid CraftingRecipeTypeId { get; }

    public Guid EntityId => CraftingRecipeTypeId;

    /// <summary>The level a recipe of this type starts at, and the ceiling that start may reach.</summary>
    internal int StartingLevel { get; }

    internal int MaxStartingLevel { get; }

    /// <summary>The verb the game uses for crafting this type.</summary>
    internal string CraftVerb { get; }

    /// <summary>Whether the type levels at all, and whether it has been initiated.</summary>
    internal bool IsLevelType { get; }

    internal bool Initiated { get; }

    /// <summary>How much magnitude a step loses and how long a step takes.</summary>
    internal double MagnitudeLoss { get; }

    internal double MagnitudeTime { get; }

    /// <summary>How much magnitude one step adds.</summary>
    internal BigDouble MagnitudeIncrement { get; }

    internal int PowerModifiers { get; }

    internal int SpeedModifiers { get; }

    internal int CostModModifiers { get; }

    internal int CostIncrementModModifiers { get; }

    internal int EfficiencyModModifiers { get; }

    internal int AutoPenaltyModModifiers { get; }

    internal int MultiPenaltyModModifiers { get; }
}

internal sealed class WorldCraftingRecipeTypeBinder : WorldPlainBinder<WorldCraftingRecipeType>
{
    private Func<object, Guid>? _id;
    private Func<object, int>? _startingLevel;
    private Func<object, int>? _maxStartingLevel;
    private Func<object, string>? _craftVerb;
    private Func<object, bool>? _isLevelType;
    private Func<object, bool>? _initiated;
    private Func<object, double>? _magnitudeLoss;
    private Func<object, double>? _magnitudeTime;
    private Func<object, BigDouble>? _magnitudeIncrement;
    private Func<object, int>? _powerModifiers;
    private Func<object, int>? _speedModifiers;
    private Func<object, int>? _costModModifiers;
    private Func<object, int>? _costIncrementModModifiers;
    private Func<object, int>? _efficiencyModModifiers;
    private Func<object, int>? _autoPenaltyModModifiers;
    private Func<object, int>? _multiPenaltyModModifiers;

    internal override string Category => "crafting recipe types";

    internal override string TypeName => "CraftingRecipeTypeSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _startingLevel = bind.Field<int>("startingLevel");
        _maxStartingLevel = bind.Field<int>("maxStartingLevel");
        _craftVerb = bind.Field<string>("craftVerb");
        _isLevelType = bind.Field<bool>("isLevelType");
        _initiated = bind.Field<bool>("initiated");
        _magnitudeLoss = bind.Field<double>("magnitudeLoss");
        _magnitudeTime = bind.Field<double>("magnitudeTime");
        _magnitudeIncrement = bind.ModifierRecord("magnitudeIncrement");
        _powerModifiers = bind.NestedCollectionCount("power", "activeModifiers");
        _speedModifiers = bind.NestedCollectionCount("speed", "activeModifiers");
        _costModModifiers = bind.NestedCollectionCount("costMod", "activeModifiers");
        _costIncrementModModifiers = bind.NestedCollectionCount("costIncrementMod", "activeModifiers");
        _efficiencyModModifiers = bind.NestedCollectionCount("efficiencyMod", "activeModifiers");
        _autoPenaltyModModifiers = bind.NestedCollectionCount("autoPenaltyMod", "activeModifiers");
        _multiPenaltyModModifiers = bind.NestedCollectionCount("multiPenaltyMod", "activeModifiers");
        return bind.Failure;
    }

    internal override WorldCraftingRecipeType Read(object entity) =>
        new(
            _id!(entity),
            _startingLevel!(entity),
            _maxStartingLevel!(entity),
            _craftVerb!(entity),
            _isLevelType!(entity),
            _initiated!(entity),
            _magnitudeLoss!(entity),
            _magnitudeTime!(entity),
            _magnitudeIncrement!(entity),
            _powerModifiers!(entity),
            _speedModifiers!(entity),
            _costModModifiers!(entity),
            _costIncrementModModifiers!(entity),
            _efficiencyModModifiers!(entity),
            _autoPenaltyModModifiers!(entity),
            _multiPenaltyModModifiers!(entity));
}
