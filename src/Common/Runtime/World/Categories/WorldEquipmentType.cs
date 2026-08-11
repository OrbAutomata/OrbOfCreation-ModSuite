using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>One equipment type as published: the levels it carries, its slot ceiling, and how loaded its composed records are.</summary>
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
internal readonly struct WorldEquipmentType : IWorldEntity
{
    internal WorldEquipmentType(
        Guid equipmentTypeId,
        int level,
        int freeLevels,
        int baseUsage,
        BigDouble masteryLevel,
        BigDouble maxTypeSlots,
        int powerModModifiers,
        int experienceRateModModifiers,
        WorldLevelableDecision levelDecision = default)
    {
        EquipmentTypeId = equipmentTypeId;
        Level = level;
        FreeLevels = freeLevels;
        BaseUsage = baseUsage;
        MasteryLevel = masteryLevel;
        MaxTypeSlots = maxTypeSlots;
        PowerModModifiers = powerModModifiers;
        ExperienceRateModModifiers = experienceRateModModifiers;
        LevelDecision = levelDecision;
    }

    internal Guid EquipmentTypeId { get; }

    public Guid EntityId => EquipmentTypeId;

    /// <summary>Levels bought and levels granted.</summary>
    internal int Level { get; }

    internal int FreeLevels { get; }

    /// <summary>How many slots one piece of this type occupies before modifiers.</summary>
    internal int BaseUsage { get; }

    /// <summary>The type's mastery level, and how many slots of it may be equipped at once.</summary>
    internal BigDouble MasteryLevel { get; }

    internal BigDouble MaxTypeSlots { get; }

    internal int PowerModModifiers { get; }

    internal int ExperienceRateModModifiers { get; }

    internal WorldLevelableDecision LevelDecision { get; }
}

internal sealed class WorldEquipmentTypeBinder : WorldPlainBinder<WorldEquipmentType>
{
    private readonly Func<string, Type?> _resolveType;
    private Func<object, Guid>? _id;
    private Func<object, int>? _level;
    private Func<object, int>? _freeLevels;
    private Func<object, int>? _baseUsage;
    private Func<object, BigDouble>? _masteryLevel;
    private Func<object, BigDouble>? _maxTypeSlots;
    private Func<object, int>? _powerModModifiers;
    private Func<object, int>? _experienceRateModModifiers;
    private WorldLevelableDecisionBinding? _levelDecision;

    internal WorldEquipmentTypeBinder(Func<string, Type?> resolveType) =>
        _resolveType = resolveType ?? throw new ArgumentNullException(nameof(resolveType));

    internal override string Category => "equipment types";

    internal override string TypeName => "EquipmentTypeSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _level = bind.Field<int>("level");
        _freeLevels = bind.Field<int>("freeLevels");
        _baseUsage = bind.Field<int>("baseUsage");
        _masteryLevel = bind.ModifierRecord("masteryLevel");
        _maxTypeSlots = bind.ModifierRecord("maxTypeSlots");
        _powerModModifiers = bind.NestedCollectionCount("powerMod", "activeModifiers");
        _experienceRateModModifiers = bind.NestedCollectionCount("experienceRateMod", "activeModifiers");
        _levelDecision = new WorldLevelableDecisionBinding(type, true, _resolveType);
        return Join(bind.Failure, _levelDecision.Failure);
    }

    internal override WorldEquipmentType Read(object entity) =>
        new(
            _id!(entity),
            _level!(entity),
            _freeLevels!(entity),
            _baseUsage!(entity),
            _masteryLevel!(entity),
            _maxTypeSlots!(entity),
            _powerModModifiers!(entity),
            _experienceRateModModifiers!(entity),
            _levelDecision!.Read(entity));

    private static string Join(string left, string right) =>
        left.Length == 0 ? right : right.Length == 0 ? left : left + "; " + right;
}
