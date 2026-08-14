using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>One crafting recipe type as published: what a recipe of this type starts at, and the magnitude curve it follows.</summary>
/// <remarks>
/// Its other records are <c>OrderedMultiplierRecord</c>s and <c>MergingModifierRecord</c>s, and
/// neither is a value at all. They are distributors: they hold modifiers and push them, transformed,
/// into the member records registered with <c>AddRecord</c>. A crafting type's <c>power</c> pushes
/// into every one of its recipes' <c>power</c>, and it is that recipe-level
/// <c>ValueModifierRecord</c> — already collected, cached value and all — that carries the result.
/// <para>
/// So the distributed effect is not missing from the snapshot; it arrives on the members. What is
/// absent from this row is the distributor's own total, the <c>Adjust(100)</c> its tooltip shows.
/// That is pure arithmetic over its two modifier dictionaries, and the entries it needs are
/// variable-size: the <c>type modifier contributions</c> category publishes them and derivation does
/// the fold. How loaded each record is has one home, <see cref="WorldTypeModifier"/>, which carries
/// it for all fourteen taxonomies alike and says why that total must not be multiplied into a member
/// value this snapshot already carries.
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
        BigDouble magnitudeIncrement)
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
            _magnitudeIncrement!(entity));
}
