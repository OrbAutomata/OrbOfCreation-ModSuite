using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>One advancement as published: levels, experience, and what a level of it is worth.</summary>
internal readonly struct WorldAdvancement : IWorldEntity
{
    internal WorldAdvancement(
        Guid advancementId,
        BigDouble levels,
        BigDouble xp,
        bool isPersistent,
        double baseRequiredXp,
        BigDouble power)
    {
        AdvancementId = advancementId;
        Levels = levels;
        Xp = xp;
        IsPersistent = isPersistent;
        BaseRequiredXp = baseRequiredXp;
        Power = power;
    }

    internal Guid AdvancementId { get; }

    public Guid EntityId => AdvancementId;

    internal BigDouble Levels { get; }

    internal BigDouble Xp { get; }

    internal bool IsPersistent { get; }

    internal double BaseRequiredXp { get; }

    internal BigDouble Power { get; }
}

internal sealed class WorldAdvancementBinder : WorldPlainBinder<WorldAdvancement>
{
    private Func<object, Guid>? _id;
    private Func<object, BigDouble>? _levels;
    private Func<object, BigDouble>? _xp;
    private Func<object, bool>? _isPersistent;
    private Func<object, double>? _baseRequiredXp;
    private Func<object, BigDouble>? _power;

    internal override string Category => "advancements";

    internal override string TypeName => "AdvancementSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.StableUuid();
        _levels = bind.Field<BigDouble>("levels");
        _xp = bind.Field<BigDouble>("xp");
        _isPersistent = bind.Field<bool>("isPersistent");
        _baseRequiredXp = bind.Field<double>("baseRequiredXp");
        _power = bind.ModifierRecord("power");
        return bind.Failure;
    }

    internal override WorldAdvancement Read(object entity) =>
        new(
            _id!(entity),
            _levels!(entity),
            _xp!(entity),
            _isPersistent!(entity),
            _baseRequiredXp!(entity),
            _power!(entity));
}
