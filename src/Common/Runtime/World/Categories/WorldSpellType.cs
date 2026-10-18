using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>One spell type as published: the mastery track the game levels by use, and the twenty-two cached records that say what a spell of this type is currently worth.</summary>
internal readonly struct WorldSpellType : IWorldEntity
{
    internal WorldSpellType(
        Guid spellTypeId,
        int typeLevel,
        BigDouble typeXp,
        double typeXpRequiredBase,
        double augmentPowerMod,
        bool hasNoLevels,
        bool isElemental,
        bool isLoadoutUnique,
        bool hasNotTypeSignificance,
        bool isVisible,
        bool debugMode,
        BigDouble typeXpMod,
        BigDouble power,
        BigDouble cooldownSpeed,
        BigDouble cooldownTime,
        BigDouble costMod,
        BigDouble drainCostMod,
        BigDouble durationMod,
        BigDouble elementalResonance,
        BigDouble augmentResonance,
        BigDouble maxStacksMod,
        BigDouble scalingMod,
        BigDouble usageCostReduction,
        BigDouble bonusCritRate,
        BigDouble critEffectMod,
        BigDouble critDurationMod,
        BigDouble bonusDoubleCastRate,
        BigDouble doubleCastEffectMod,
        BigDouble chargeTimeMod,
        BigDouble chargeEffectMod,
        BigDouble chargeSpecialMod,
        BigDouble bonusFlashRate,
        BigDouble flashEffectMod)
    {
        SpellTypeId = spellTypeId;
        TypeLevel = typeLevel;
        TypeXp = typeXp;
        TypeXpRequiredBase = typeXpRequiredBase;
        AugmentPowerMod = augmentPowerMod;
        HasNoLevels = hasNoLevels;
        IsElemental = isElemental;
        IsLoadoutUnique = isLoadoutUnique;
        HasNotTypeSignificance = hasNotTypeSignificance;
        IsVisible = isVisible;
        DebugMode = debugMode;
        TypeXpMod = typeXpMod;
        Power = power;
        CooldownSpeed = cooldownSpeed;
        CooldownTime = cooldownTime;
        CostMod = costMod;
        DrainCostMod = drainCostMod;
        DurationMod = durationMod;
        ElementalResonance = elementalResonance;
        AugmentResonance = augmentResonance;
        MaxStacksMod = maxStacksMod;
        ScalingMod = scalingMod;
        UsageCostReduction = usageCostReduction;
        BonusCritRate = bonusCritRate;
        CritEffectMod = critEffectMod;
        CritDurationMod = critDurationMod;
        BonusDoubleCastRate = bonusDoubleCastRate;
        DoubleCastEffectMod = doubleCastEffectMod;
        ChargeTimeMod = chargeTimeMod;
        ChargeEffectMod = chargeEffectMod;
        ChargeSpecialMod = chargeSpecialMod;
        BonusFlashRate = bonusFlashRate;
        FlashEffectMod = flashEffectMod;
    }

    internal Guid SpellTypeId { get; }

    public Guid EntityId => SpellTypeId;

    /// <summary>Levels earned, and the experience toward the next one.</summary>
    internal int TypeLevel { get; }

    internal BigDouble TypeXp { get; }

    /// <summary>The experience one level costs before modifiers.</summary>
    internal double TypeXpRequiredBase { get; }

    /// <summary>What an augment is worth on this type.</summary>
    internal double AugmentPowerMod { get; }

    /// <summary>Whether the type levels at all, is elemental, or may appear once per loadout.</summary>
    internal bool HasNoLevels { get; }

    internal bool IsElemental { get; }

    internal bool IsLoadoutUnique { get; }

    /// <summary>Whether the game treats the type as significant, and whether it is currently shown.</summary>
    internal bool HasNotTypeSignificance { get; }

    internal bool IsVisible { get; }

    /// <summary>The game's own debug flag for this entry.</summary>
    internal bool DebugMode { get; }

    /// <summary>How fast the type earns experience.</summary>
    internal BigDouble TypeXpMod { get; }

    /// <summary>The core four: power, cooldown speed and time, and cost.</summary>
    internal BigDouble Power { get; }

    internal BigDouble CooldownSpeed { get; }

    internal BigDouble CooldownTime { get; }

    internal BigDouble CostMod { get; }

    /// <summary>What a spell of this type drains, and how long its effect lasts.</summary>
    internal BigDouble DrainCostMod { get; }

    internal BigDouble DurationMod { get; }

    /// <summary>Resonance with the type's element, and with its augments.</summary>
    internal BigDouble ElementalResonance { get; }

    internal BigDouble AugmentResonance { get; }

    /// <summary>How many stacks may run, and how the effect scales with them.</summary>
    internal BigDouble MaxStacksMod { get; }

    internal BigDouble ScalingMod { get; }

    /// <summary>What each use costs after reduction.</summary>
    internal BigDouble UsageCostReduction { get; }

    /// <summary>Crit chance, what a crit is worth, and how long a crit lasts.</summary>
    internal BigDouble BonusCritRate { get; }

    internal BigDouble CritEffectMod { get; }

    internal BigDouble CritDurationMod { get; }

    /// <summary>Double-cast chance and what the second cast is worth.</summary>
    internal BigDouble BonusDoubleCastRate { get; }

    internal BigDouble DoubleCastEffectMod { get; }

    /// <summary>Charging: how long it takes and what it buys.</summary>
    internal BigDouble ChargeTimeMod { get; }

    internal BigDouble ChargeEffectMod { get; }

    internal BigDouble ChargeSpecialMod { get; }

    /// <summary>Flash chance and what a flash is worth.</summary>
    internal BigDouble BonusFlashRate { get; }

    internal BigDouble FlashEffectMod { get; }
}

internal sealed class WorldSpellTypeBinder : WorldPlainBinder<WorldSpellType>
{
    private Func<object, Guid>? _id;
    private Func<object, int>? _typeLevel;
    private Func<object, BigDouble>? _typeXp;
    private Func<object, double>? _typeXpRequiredBase;
    private Func<object, double>? _augmentPowerMod;
    private Func<object, bool>? _hasNoLevels;
    private Func<object, bool>? _isElemental;
    private Func<object, bool>? _isLoadoutUnique;
    private Func<object, bool>? _hasNotTypeSignificance;
    private Func<object, bool>? _isVisible;
    private Func<object, bool>? _debugMode;
    private Func<object, BigDouble>? _typeXpMod;
    private Func<object, BigDouble>? _power;
    private Func<object, BigDouble>? _cooldownSpeed;
    private Func<object, BigDouble>? _cooldownTime;
    private Func<object, BigDouble>? _costMod;
    private Func<object, BigDouble>? _drainCostMod;
    private Func<object, BigDouble>? _durationMod;
    private Func<object, BigDouble>? _elementalResonance;
    private Func<object, BigDouble>? _augmentResonance;
    private Func<object, BigDouble>? _maxStacksMod;
    private Func<object, BigDouble>? _scalingMod;
    private Func<object, BigDouble>? _usageCostReduction;
    private Func<object, BigDouble>? _bonusCritRate;
    private Func<object, BigDouble>? _critEffectMod;
    private Func<object, BigDouble>? _critDurationMod;
    private Func<object, BigDouble>? _bonusDoubleCastRate;
    private Func<object, BigDouble>? _doubleCastEffectMod;
    private Func<object, BigDouble>? _chargeTimeMod;
    private Func<object, BigDouble>? _chargeEffectMod;
    private Func<object, BigDouble>? _chargeSpecialMod;
    private Func<object, BigDouble>? _bonusFlashRate;
    private Func<object, BigDouble>? _flashEffectMod;

    internal override string Category => "spell types";

    internal override string TypeName => "SpellTypeSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.StableUuid();
        _typeLevel = bind.Field<int>("typeLevel");
        _typeXp = bind.Field<BigDouble>("typeXp");
        _typeXpRequiredBase = bind.Field<double>("typeXpRequiredBase");
        _augmentPowerMod = bind.Field<double>("augmentPowerMod");
        _hasNoLevels = bind.Field<bool>("hasNoLevels");
        _isElemental = bind.Field<bool>("isElemental");
        _isLoadoutUnique = bind.Field<bool>("isLoadoutUnique");
        _hasNotTypeSignificance = bind.Field<bool>("hasNotTypeSignificance");
        _isVisible = bind.Field<bool>("isVisible");
        _debugMode = bind.Field<bool>("debugMode");
        _typeXpMod = bind.ModifierRecord("typeXpMod");
        _power = bind.ModifierRecord("power");
        _cooldownSpeed = bind.ModifierRecord("cooldownSpeed");
        _cooldownTime = bind.ModifierRecord("cooldownTime");
        _costMod = bind.ModifierRecord("costMod");
        _drainCostMod = bind.ModifierRecord("drainCostMod");
        _durationMod = bind.ModifierRecord("durationMod");
        _elementalResonance = bind.ModifierRecord("elementalResonance");
        _augmentResonance = bind.ModifierRecord("augmentResonance");
        _maxStacksMod = bind.ModifierRecord("maxStacksMod");
        _scalingMod = bind.ModifierRecord("scalingMod");
        _usageCostReduction = bind.ModifierRecord("usageCostReduction");
        _bonusCritRate = bind.ModifierRecord("bonusCritRate");
        _critEffectMod = bind.ModifierRecord("critEffectMod");
        _critDurationMod = bind.ModifierRecord("critDurationMod");
        _bonusDoubleCastRate = bind.ModifierRecord("bonusDoubleCastRate");
        _doubleCastEffectMod = bind.ModifierRecord("doubleCastEffectMod");
        _chargeTimeMod = bind.ModifierRecord("chargeTimeMod");
        _chargeEffectMod = bind.ModifierRecord("chargeEffectMod");
        _chargeSpecialMod = bind.ModifierRecord("chargeSpecialMod");
        _bonusFlashRate = bind.ModifierRecord("bonusFlashRate");
        _flashEffectMod = bind.ModifierRecord("flashEffectMod");
        return bind.Failure;
    }

    internal override WorldSpellType Read(object entity) =>
        new(
            _id!(entity),
            _typeLevel!(entity),
            _typeXp!(entity),
            _typeXpRequiredBase!(entity),
            _augmentPowerMod!(entity),
            _hasNoLevels!(entity),
            _isElemental!(entity),
            _isLoadoutUnique!(entity),
            _hasNotTypeSignificance!(entity),
            _isVisible!(entity),
            _debugMode!(entity),
            _typeXpMod!(entity),
            _power!(entity),
            _cooldownSpeed!(entity),
            _cooldownTime!(entity),
            _costMod!(entity),
            _drainCostMod!(entity),
            _durationMod!(entity),
            _elementalResonance!(entity),
            _augmentResonance!(entity),
            _maxStacksMod!(entity),
            _scalingMod!(entity),
            _usageCostReduction!(entity),
            _bonusCritRate!(entity),
            _critEffectMod!(entity),
            _critDurationMod!(entity),
            _bonusDoubleCastRate!(entity),
            _doubleCastEffectMod!(entity),
            _chargeTimeMod!(entity),
            _chargeEffectMod!(entity),
            _chargeSpecialMod!(entity),
            _bonusFlashRate!(entity),
            _flashEffectMod!(entity));
}
