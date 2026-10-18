using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>One harvest element as published: its mastery track and the ten records that drive the resource it owns. That resource is its own category; see WorldHarvestResource.</summary>
internal readonly struct WorldHarvestElement : IWorldEntity
{
    internal WorldHarvestElement(
        Guid harvestElementId,
        BigDouble masteryXp,
        int masteryLevel,
        double harvestTime,
        double growthTime,
        double rarityValue,
        double initialMaxQuantity,
        double requiredXpToLevel,
        BigDouble instances,
        BigDouble power,
        BigDouble harvestSpeedMod,
        BigDouble drainCostMod,
        BigDouble autoGenerationMod,
        BigDouble experienceRateMod,
        BigDouble actionXpRate,
        BigDouble actionPower,
        BigDouble actionSpeed,
        BigDouble actionCostMod,
        BigDouble harvestRate,
        BigDouble lastOutputRate)
    {
        HarvestElementId = harvestElementId;
        MasteryXp = masteryXp;
        MasteryLevel = masteryLevel;
        HarvestTime = harvestTime;
        GrowthTime = growthTime;
        RarityValue = rarityValue;
        InitialMaxQuantity = initialMaxQuantity;
        RequiredXpToLevel = requiredXpToLevel;
        Instances = instances;
        Power = power;
        HarvestSpeedMod = harvestSpeedMod;
        DrainCostMod = drainCostMod;
        AutoGenerationMod = autoGenerationMod;
        ExperienceRateMod = experienceRateMod;
        ActionXpRate = actionXpRate;
        ActionPower = actionPower;
        ActionSpeed = actionSpeed;
        ActionCostMod = actionCostMod;
        HarvestRate = harvestRate;
        LastOutputRate = lastOutputRate;
    }

    internal Guid HarvestElementId { get; }

    public Guid EntityId => HarvestElementId;

    internal BigDouble MasteryXp { get; }

    internal int MasteryLevel { get; }

    internal double HarvestTime { get; }

    internal double GrowthTime { get; }

    internal double RarityValue { get; }

    internal double InitialMaxQuantity { get; }

    internal double RequiredXpToLevel { get; }

    internal BigDouble Instances { get; }

    internal BigDouble Power { get; }

    internal BigDouble HarvestSpeedMod { get; }

    internal BigDouble DrainCostMod { get; }

    internal BigDouble AutoGenerationMod { get; }

    internal BigDouble ExperienceRateMod { get; }

    internal BigDouble ActionXpRate { get; }

    internal BigDouble ActionPower { get; }

    internal BigDouble ActionSpeed { get; }

    internal BigDouble ActionCostMod { get; }

    internal BigDouble HarvestRate { get; }

    internal BigDouble LastOutputRate { get; }
}

internal sealed class WorldHarvestElementBinder : WorldPlainBinder<WorldHarvestElement>
{
    private Func<object, Guid>? _id;
    private Func<object, BigDouble>? _masteryXp;
    private Func<object, int>? _masteryLevel;
    private Func<object, double>? _harvestTime;
    private Func<object, double>? _growthTime;
    private Func<object, double>? _rarityValue;
    private Func<object, double>? _initialMaxQuantity;
    private Func<object, double>? _requiredXpToLevel;
    private Func<object, BigDouble>? _instances;
    private Func<object, BigDouble>? _power;
    private Func<object, BigDouble>? _harvestSpeedMod;
    private Func<object, BigDouble>? _drainCostMod;
    private Func<object, BigDouble>? _autoGenerationMod;
    private Func<object, BigDouble>? _experienceRateMod;
    private Func<object, BigDouble>? _actionXpRate;
    private Func<object, BigDouble>? _actionPower;
    private Func<object, BigDouble>? _actionSpeed;
    private Func<object, BigDouble>? _actionCostMod;
    private Func<object, BigDouble>? _harvestRate;
    private Func<object, BigDouble>? _lastOutputRate;

    internal override string Category => "harvest elements";

    internal override string TypeName => "HarvestElementSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.StableUuid();
        _masteryXp = bind.Field<BigDouble>("masteryXp");
        _masteryLevel = bind.Field<int>("masteryLevel");
        _harvestTime = bind.Field<double>("harvestTime");
        _growthTime = bind.Field<double>("growthTime");
        _rarityValue = bind.Field<double>("rarityValue");
        _initialMaxQuantity = bind.Field<double>("initialMaxQuantity");
        _requiredXpToLevel = bind.Field<double>("requiredXpToLevel");
        _instances = bind.ModifierRecord("instances");
        _power = bind.ModifierRecord("power");
        _harvestSpeedMod = bind.ModifierRecord("harvestSpeedMod");
        _drainCostMod = bind.ModifierRecord("drainCostMod");
        _autoGenerationMod = bind.ModifierRecord("autoGenerationMod");
        _experienceRateMod = bind.ModifierRecord("experienceRateMod");
        _actionXpRate = bind.ModifierRecord("actionXpRate");
        _actionPower = bind.ModifierRecord("actionPower");
        _actionSpeed = bind.ModifierRecord("actionSpeed");
        _actionCostMod = bind.ModifierRecord("actionCostMod");
        _harvestRate = bind.Field<BigDouble>("harvestRate");
        _lastOutputRate = bind.Field<BigDouble>("lastOutputRate");
        return bind.Failure;
    }

    internal override WorldHarvestElement Read(object entity) =>
        new(
            _id!(entity),
            _masteryXp!(entity),
            _masteryLevel!(entity),
            _harvestTime!(entity),
            _growthTime!(entity),
            _rarityValue!(entity),
            _initialMaxQuantity!(entity),
            _requiredXpToLevel!(entity),
            _instances!(entity),
            _power!(entity),
            _harvestSpeedMod!(entity),
            _drainCostMod!(entity),
            _autoGenerationMod!(entity),
            _experienceRateMod!(entity),
            _actionXpRate!(entity),
            _actionPower!(entity),
            _actionSpeed!(entity),
            _actionCostMod!(entity),
            _harvestRate!(entity),
            _lastOutputRate!(entity));
}
