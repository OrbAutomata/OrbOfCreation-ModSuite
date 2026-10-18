using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One treasure pool as published. The pool's random state is not carried: reproducing a draw is not
/// something the suite does, and an <c>OrbRandom</c> is a mutable object rather than a fact.
/// </summary>
internal readonly struct WorldTreasurePool : IWorldEntity
{
    internal WorldTreasurePool(Guid poolId, int treasuresFound, BigDouble partialReward,
        bool forceLevel,
        int treasureLevel,
        bool calculatedTreasureLevel)
    {
        PoolId = poolId;
        TreasuresFound = treasuresFound;
        PartialReward = partialReward;
        ForceLevel = forceLevel;
        TreasureLevel = treasureLevel;
        CalculatedTreasureLevel = calculatedTreasureLevel;
    }

    internal Guid PoolId { get; }

    public Guid EntityId => PoolId;

    internal int TreasuresFound { get; }

    /// <summary>Progress toward the next treasure.</summary>
    internal BigDouble PartialReward { get; }

    /// <summary>The rest of the pool's level state: whether the level is forced, and what it resolved to.</summary>
    internal bool ForceLevel { get; }

    internal int TreasureLevel { get; }

    internal bool CalculatedTreasureLevel { get; }
}

internal sealed class WorldTreasurePoolBinder : WorldPlainBinder<WorldTreasurePool>
{
    private Func<object, Guid>? _id;
    private Func<object, int>? _treasuresFound;
    private Func<object, BigDouble>? _partialReward;
    private Func<object, bool>? _forceLevel;
    private Func<object, int>? _treasureLevel;
    private Func<object, bool>? _calculatedTreasureLevel;

    internal override string Category => "treasure pools";

    internal override string TypeName => "TreasurePoolSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.StableUuid();
        _treasuresFound = bind.Field<int>("treasuresFound");
        _partialReward = bind.Field<BigDouble>("partialTreasureReward");
        _forceLevel = bind.Field<bool>("forceLevel");
        _treasureLevel = bind.Field<int>("treasureLevel");
        _calculatedTreasureLevel = bind.Field<bool>("calculatedTreasureLevel");
        return bind.Failure;
    }

    internal override WorldTreasurePool Read(object entity) =>
        new(_id!(entity), _treasuresFound!(entity), _partialReward!(entity),
            _forceLevel!(entity),
            _treasureLevel!(entity),
            _calculatedTreasureLevel!(entity));
}
