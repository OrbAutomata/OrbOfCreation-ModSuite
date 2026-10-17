using System;
using OrbModding.Common.Runtime.GameMath;

namespace OrbModding.Common.Runtime.World;

/// <summary>One alchemy type as published: its level, and the level the player selected.</summary>
/// <remarks>
/// Its other records are <c>OrderedMultiplierRecord</c>s and <c>MergingModifierRecord</c>s, and
/// neither is a value at all. They are distributors: they hold modifiers and push them, transformed,
/// into the member records registered with <c>AddRecord</c>. An alchemy type's <c>power</c> pushes
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
internal readonly struct WorldAlchemyType : IWorldEntity
{
    internal WorldAlchemyType(
        Guid alchemyTypeId,
        Guid selectedLevelId,
        bool maxUsageByMastery,
        BigDouble level,
        GameValueModifier requirementSpeedPenalty = default,
        GameValueModifier requirementCostPenalty = default)
    {
        AlchemyTypeId = alchemyTypeId;
        SelectedLevelId = selectedLevelId;
        MaxUsageByMastery = maxUsageByMastery;
        Level = level;
        RequirementSpeedPenalty = requirementSpeedPenalty;
        RequirementCostPenalty = requirementCostPenalty;
    }

    internal Guid AlchemyTypeId { get; }

    public Guid EntityId => AlchemyTypeId;

    /// <summary>The variable holding the level the player has selected, or Guid.Empty when the type has no such choice. An edge rather than a number, because the value already lives in the global registry. See D17.</summary>
    internal Guid SelectedLevelId { get; }

    /// <summary>Whether the usage ceiling comes from mastery rather than from the type.</summary>
    internal bool MaxUsageByMastery { get; }

    /// <summary>The type's level, which the game keeps as a modifier record rather than an integer.</summary>
    internal BigDouble Level { get; }

    /// <summary>
    /// <c>reqSpeedPenalty</c> and <c>reqCostPenalty</c>: the speed and drain cost a recipe whose core
    /// type this is runs at while its usage requirement is unmet.
    /// </summary>
    internal GameValueModifier RequirementSpeedPenalty { get; }

    internal GameValueModifier RequirementCostPenalty { get; }
}

internal sealed class WorldAlchemyTypeBinder : WorldPlainBinder<WorldAlchemyType>
{
    private Func<object, Guid>? _id;
    private Func<object, Guid>? _selectedLevelId;
    private Func<object, bool>? _maxUsageByMastery;
    private Func<object, BigDouble>? _level;
    private Func<object, GameValueModifier>? _speedPenalty;
    private Func<object, GameValueModifier>? _costPenalty;

    internal override string Category => "alchemy types";

    internal override string TypeName => "AlchemyTypeSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _selectedLevelId = bind.ReferenceGuid("selectedLevel");
        _maxUsageByMastery = bind.Field<bool>("maxUsageByMastery");
        _level = bind.ModifierRecord("level");
        _speedPenalty = Penalty(bind, "reqSpeedPenalty");
        _costPenalty = Penalty(bind, "reqCostPenalty");
        return bind.Failure;
    }

    private static Func<object, GameValueModifier>? Penalty(WorldMemberBinding bind, string field)
    {
        var type = bind.NestedEnumField(field, "type");
        var amount = bind.NestedField<BigDouble>(field, "adjustReal");
        var order = bind.NestedField<int>(field, "order");
        if (type is null || amount is null || order is null) return null;
        return entity => new GameValueModifier(
            (GameValueModifierType)type(entity), amount(entity), order(entity));
    }

    internal override WorldAlchemyType Read(object entity) =>
        new(
            _id!(entity),
            _selectedLevelId!(entity),
            _maxUsageByMastery!(entity),
            _level!(entity),
            _speedPenalty!(entity),
            _costPenalty!(entity));
}
