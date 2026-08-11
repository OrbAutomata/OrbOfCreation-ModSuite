using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>One alchemy type as published: its level, the level the player selected, and how loaded each of its composed records is.</summary>
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
internal readonly struct WorldAlchemyType : IWorldEntity
{
    internal WorldAlchemyType(
        Guid alchemyTypeId,
        Guid selectedLevelId,
        bool maxUsageByMastery,
        BigDouble level,
        int powerModifiers,
        int speedModifiers,
        int specialModifiers,
        int drainCostModModifiers,
        int experienceRateModifiers,
        int overdrivePowerModifiers,
        int overdriveSpeedModifiers,
        int overdriveDrainCostModModifiers,
        int overdriveXpRateModifiers,
        int timeReqModModifiers,
        int timeScalingModModifiers,
        int freeUsageSlotsModifiers,
        int effectLevelsModifiers)
    {
        AlchemyTypeId = alchemyTypeId;
        SelectedLevelId = selectedLevelId;
        MaxUsageByMastery = maxUsageByMastery;
        Level = level;
        PowerModifiers = powerModifiers;
        SpeedModifiers = speedModifiers;
        SpecialModifiers = specialModifiers;
        DrainCostModModifiers = drainCostModModifiers;
        ExperienceRateModifiers = experienceRateModifiers;
        OverdrivePowerModifiers = overdrivePowerModifiers;
        OverdriveSpeedModifiers = overdriveSpeedModifiers;
        OverdriveDrainCostModModifiers = overdriveDrainCostModModifiers;
        OverdriveXpRateModifiers = overdriveXpRateModifiers;
        TimeReqModModifiers = timeReqModModifiers;
        TimeScalingModModifiers = timeScalingModModifiers;
        FreeUsageSlotsModifiers = freeUsageSlotsModifiers;
        EffectLevelsModifiers = effectLevelsModifiers;
    }

    internal Guid AlchemyTypeId { get; }

    public Guid EntityId => AlchemyTypeId;

    /// <summary>The variable holding the level the player has selected, or Guid.Empty when the type has no such choice. An edge rather than a number, because the value already lives in the global registry. See D17.</summary>
    internal Guid SelectedLevelId { get; }

    /// <summary>Whether the usage ceiling comes from mastery rather than from the type.</summary>
    internal bool MaxUsageByMastery { get; }

    /// <summary>The type's level, which the game keeps as a modifier record rather than an integer.</summary>
    internal BigDouble Level { get; }

    internal int PowerModifiers { get; }

    internal int SpeedModifiers { get; }

    internal int SpecialModifiers { get; }

    internal int DrainCostModModifiers { get; }

    internal int ExperienceRateModifiers { get; }

    internal int OverdrivePowerModifiers { get; }

    internal int OverdriveSpeedModifiers { get; }

    internal int OverdriveDrainCostModModifiers { get; }

    internal int OverdriveXpRateModifiers { get; }

    internal int TimeReqModModifiers { get; }

    internal int TimeScalingModModifiers { get; }

    internal int FreeUsageSlotsModifiers { get; }

    internal int EffectLevelsModifiers { get; }
}

internal sealed class WorldAlchemyTypeBinder : WorldPlainBinder<WorldAlchemyType>
{
    private Func<object, Guid>? _id;
    private Func<object, Guid>? _selectedLevelId;
    private Func<object, bool>? _maxUsageByMastery;
    private Func<object, BigDouble>? _level;
    private Func<object, int>? _powerModifiers;
    private Func<object, int>? _speedModifiers;
    private Func<object, int>? _specialModifiers;
    private Func<object, int>? _drainCostModModifiers;
    private Func<object, int>? _experienceRateModifiers;
    private Func<object, int>? _overdrivePowerModifiers;
    private Func<object, int>? _overdriveSpeedModifiers;
    private Func<object, int>? _overdriveDrainCostModModifiers;
    private Func<object, int>? _overdriveXpRateModifiers;
    private Func<object, int>? _timeReqModModifiers;
    private Func<object, int>? _timeScalingModModifiers;
    private Func<object, int>? _freeUsageSlotsModifiers;
    private Func<object, int>? _effectLevelsModifiers;

    internal override string Category => "alchemy types";

    internal override string TypeName => "AlchemyTypeSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _selectedLevelId = bind.ReferenceGuid("selectedLevel");
        _maxUsageByMastery = bind.Field<bool>("maxUsageByMastery");
        _level = bind.ModifierRecord("level");
        _powerModifiers = bind.NestedCollectionCount("power", "activeModifiers");
        _speedModifiers = bind.NestedCollectionCount("speed", "activeModifiers");
        _specialModifiers = bind.NestedCollectionCount("special", "activeModifiers");
        _drainCostModModifiers = bind.NestedCollectionCount("drainCostMod", "activeModifiers");
        _experienceRateModifiers = bind.NestedCollectionCount("experienceRate", "activeModifiers");
        _overdrivePowerModifiers = bind.NestedCollectionCount("overdrivePower", "activeModifiers");
        _overdriveSpeedModifiers = bind.NestedCollectionCount("overdriveSpeed", "activeModifiers");
        _overdriveDrainCostModModifiers = bind.NestedCollectionCount("overdriveDrainCostMod", "activeModifiers");
        _overdriveXpRateModifiers = bind.NestedCollectionCount("overdriveXpRate", "activeModifiers");
        _timeReqModModifiers = bind.NestedCollectionCount("timeReqMod", "activeModifiers");
        _timeScalingModModifiers = bind.NestedCollectionCount("timeScalingMod", "activeModifiers");
        _freeUsageSlotsModifiers = bind.NestedCollectionCount("freeUsageSlots", "activeModifiers");
        _effectLevelsModifiers = bind.NestedCollectionCount("effectLevels", "activeModifiers");
        return bind.Failure;
    }

    internal override WorldAlchemyType Read(object entity) =>
        new(
            _id!(entity),
            _selectedLevelId!(entity),
            _maxUsageByMastery!(entity),
            _level!(entity),
            _powerModifiers!(entity),
            _speedModifiers!(entity),
            _specialModifiers!(entity),
            _drainCostModModifiers!(entity),
            _experienceRateModifiers!(entity),
            _overdrivePowerModifiers!(entity),
            _overdriveSpeedModifiers!(entity),
            _overdriveDrainCostModModifiers!(entity),
            _overdriveXpRateModifiers!(entity),
            _timeReqModModifiers!(entity),
            _timeScalingModModifiers!(entity),
            _freeUsageSlotsModifiers!(entity),
            _effectLevelsModifiers!(entity));
}
