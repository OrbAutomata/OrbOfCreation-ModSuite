using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>One piece of equipment as published: discovery, its mastery track, and what it is currently doing — equipped, attuning, or neither.</summary>
internal readonly struct WorldEquipment : IWorldEntity
{
    internal WorldEquipment(
        Guid equipmentId,
        bool isCreated,
        int discRarityLevel,
        BigDouble masteryXp,
        int masteryLevel,
        bool isRequiredDiscovery,
        BigDouble power,
        BigDouble baseLevel,
        BigDouble experienceRateMod,
        int equippedLevel,
        int attuningLevel,
        double attunementTimeLeft,
        BigDouble baseXpRate,
        WorldDiscoverableDecision discovery = default,
        WorldEquipmentDecision loadout = default)
    {
        EquipmentId = equipmentId;
        IsCreated = isCreated;
        DiscRarityLevel = discRarityLevel;
        MasteryXp = masteryXp;
        MasteryLevel = masteryLevel;
        IsRequiredDiscovery = isRequiredDiscovery;
        Power = power;
        BaseLevel = baseLevel;
        ExperienceRateMod = experienceRateMod;
        EquippedLevel = equippedLevel;
        AttuningLevel = attuningLevel;
        AttunementTimeLeft = attunementTimeLeft;
        BaseXpRate = baseXpRate;
        Discovery = discovery;
        Loadout = loadout;
    }

    internal Guid EquipmentId { get; }

    public Guid EntityId => EquipmentId;

    internal bool IsCreated { get; }

    internal int DiscRarityLevel { get; }

    internal BigDouble MasteryXp { get; }

    internal int MasteryLevel { get; }

    internal bool IsRequiredDiscovery { get; }

    internal BigDouble Power { get; }

    internal BigDouble BaseLevel { get; }

    internal BigDouble ExperienceRateMod { get; }

    internal int EquippedLevel { get; }

    internal int AttuningLevel { get; }

    internal double AttunementTimeLeft { get; }

    internal BigDouble BaseXpRate { get; }

    internal WorldDiscoverableDecision Discovery { get; }

    internal WorldEquipmentDecision Loadout { get; }
}

internal sealed class WorldEquipmentBinder : WorldPlainBinder<WorldEquipment>
{
    private Func<object, Guid>? _id;
    private Func<object, bool>? _isCreated;
    private Func<object, int>? _discRarityLevel;
    private Func<object, BigDouble>? _masteryXp;
    private Func<object, int>? _masteryLevel;
    private Func<object, bool>? _isRequiredDiscovery;
    private Func<object, BigDouble>? _power;
    private Func<object, BigDouble>? _baseLevel;
    private Func<object, BigDouble>? _experienceRateMod;
    private Func<object, int>? _equippedLevel;
    private Func<object, int>? _attuningLevel;
    private Func<object, double>? _attunementTimeLeft;
    private Func<object, BigDouble>? _baseXpRate;
    private WorldDiscoverableBinding? _discovery;
    private readonly Func<string, Type?> _resolveType;
    private WorldEquipmentDecisionBinding? _loadout;

    internal WorldEquipmentBinder(Func<string, Type?> resolveType) =>
        _resolveType = resolveType ?? throw new ArgumentNullException(nameof(resolveType));

    internal override string Category => "equipment";

    internal override string TypeName => "EquipmentSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _isCreated = bind.Field<bool>("isCreated");
        _discRarityLevel = bind.Field<int>("discRarityLevel");
        _masteryXp = bind.Field<BigDouble>("masteryXp");
        _masteryLevel = bind.Field<int>("masteryLevel");
        _isRequiredDiscovery = bind.Field<bool>("isRequiredDiscovery");
        _power = bind.ModifierRecord("power");
        _baseLevel = bind.ModifierRecord("baseLevel");
        _experienceRateMod = bind.ModifierRecord("experienceRateMod");
        _equippedLevel = bind.Field<int>("equippedLevel");
        _attuningLevel = bind.Field<int>("attuningLevel");
        _attunementTimeLeft = bind.Field<double>("attunementTimeLeft");
        _baseXpRate = bind.Field<BigDouble>("baseXpRate");
        _discovery = new WorldDiscoverableBinding(type, TypeName);
        _loadout = new WorldEquipmentDecisionBinding(type, _resolveType);
        return Join(Join(bind.Failure, _discovery.Failure), _loadout.Failure);
    }

    internal override WorldEquipment Read(object entity) =>
        new(
            _id!(entity),
            _isCreated!(entity),
            _discRarityLevel!(entity),
            _masteryXp!(entity),
            _masteryLevel!(entity),
            _isRequiredDiscovery!(entity),
            _power!(entity),
            _baseLevel!(entity),
            _experienceRateMod!(entity),
            _equippedLevel!(entity),
            _attuningLevel!(entity),
            _attunementTimeLeft!(entity),
            _baseXpRate!(entity),
            _discovery!.Read(entity),
            _loadout!.Read(entity));

    private static string Join(string left, string right) =>
        left.Length == 0 ? right : right.Length == 0 ? left : left + "; " + right;
}
