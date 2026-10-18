using System;
using System.Collections;

namespace OrbModding.Common.Runtime.World;

/// <summary>One upgrade reading — a bounded, usually one-shot purchase.</summary>
internal readonly struct RawUpgradeSample : IWorldEntity
{
    internal RawUpgradeSample(
        Guid upgradeId,
        int level,
        int maxLevel,
        bool available,
        int queuedLevels,
        BigDouble buildTime,
        double developmentTime,
        int cachedCostLevel,
        bool rewardDestinationFull = false)
    {
        UpgradeId = upgradeId;
        Level = level;
        MaxLevel = maxLevel;
        Available = available;
        QueuedLevels = queuedLevels;
        BuildTime = buildTime;
        DevelopmentTime = developmentTime;
        CachedCostLevel = cachedCostLevel;
        RewardDestinationFull = rewardDestinationFull;
    }

    internal Guid UpgradeId { get; }

    /// <summary>The identity every category-generic lookup and traversal reads.</summary>
    public Guid EntityId => UpgradeId;

    internal int Level { get; }

    /// <summary>The level ceiling, or a non-positive value when the upgrade is unbounded.</summary>
    internal int MaxLevel { get; }

    /// <summary>Whether prerequisites currently permit purchasing.</summary>
    internal bool Available { get; }

    /// <summary>Levels bought and still developing.</summary>
    internal int QueuedLevels { get; }

    /// <summary>Time already spent developing, against the <see cref="DevelopmentTime"/> one level takes.</summary>
    internal BigDouble BuildTime { get; }

    internal double DevelopmentTime { get; }

    /// <summary>
    /// The level the game last computed a cost for, or <c>-1</c> before it has computed one. A cost
    /// read while this disagrees with <see cref="Level"/> is a cost for a different level.
    /// </summary>
    internal int CachedCostLevel { get; }

    /// <summary>
    /// Whether a list this upgrade's reward is added to answers <c>HasEmptySpot()</c> false — the
    /// World > Aspects pedestals when all three hold an aspect. The game charges such a purchase
    /// and has nowhere to put what it bought.
    /// </summary>
    internal bool RewardDestinationFull { get; }
}

/// <summary>One upgrade as published.</summary>
internal readonly struct WorldUpgrade : IWorldEntity
{
    internal WorldUpgrade(
        in RawUpgradeSample reading,
        bool isBounded,
        bool isExhausted,
        int remainingLevels,
        int committedLevel,
        bool isDeveloping,
        double developmentProgress)
    {
        Reading = reading;
        IsBounded = isBounded;
        IsExhausted = isExhausted;
        RemainingLevels = remainingLevels;
        CommittedLevel = committedLevel;
        IsDeveloping = isDeveloping;
        DevelopmentProgress = developmentProgress;
    }

    internal RawUpgradeSample Reading { get; }

    public Guid EntityId => Reading.UpgradeId;

    /// <summary>Whether a level ceiling applies at all.</summary>
    internal bool IsBounded { get; }

    /// <summary>Whether every available level has been bought. Always false when unbounded.</summary>
    internal bool IsExhausted { get; }

    /// <summary>Levels left to buy, never negative. Zero when unbounded — read with <see cref="IsBounded"/>.</summary>
    internal int RemainingLevels { get; }

    /// <summary>Levels owned plus levels queued, matching the game's <c>GetQueuedPurchaseLevel()</c>.</summary>
    internal int CommittedLevel { get; }

    /// <summary>Whether a level is in flight. The game's own test is <c>queuedLevels &gt; 0</c>.</summary>
    internal bool IsDeveloping { get; }

    /// <summary>How far the level in flight has come, in <c>[0, 1]</c>; zero when nothing is developing.</summary>
    internal double DevelopmentProgress { get; }
}

/// <summary>Upgrades — bounded, usually one-shot purchases.</summary>
internal sealed class WorldUpgradeBinder : WorldRowBinder<RawUpgradeSample, WorldUpgrade>
{
    private Func<object, Guid>? _id;
    private Func<object, int>? _level;
    private Func<object, int>? _maxLevel;
    private Func<object, bool>? _available;
    private Func<object, int>? _queuedLevels;
    private Func<object, BigDouble>? _buildTime;
    private Func<object, double>? _developmentTime;
    private Func<object, int>? _cachedCostLevel;
    private Func<object, IList?>? _rewardDestinations;
    private Func<object, bool>? _destinationHasRoom;

    internal override string Category => "upgrades";

    internal override string TypeName => "UpgradeSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.StableUuid();
        _level = bind.Call<int>("GetPurchaseLevel");

        // maxLevel is read as a field because the game exposes no accessor for it, only the predicate
        // HasFiniteLevels(), which is `maxLevel > 0` — the same non-positive-means-unbounded
        // convention the world row already documents.
        _maxLevel = bind.Field<int>("maxLevel");
        _available = bind.Call<bool>("IsAvailable");
        _queuedLevels = bind.Field<int>("queuedLevels");

        // buildTime is the seconds left on the level in flight, counting down; the game reaches it
        // through an `actionTime` property that is nothing but this field.
        _buildTime = bind.Field<BigDouble>("buildTime");
        _developmentTime = bind.Field<double>("developmentTime");
        _cachedCostLevel = bind.Field<int>("cachedCostLevel");
        _rewardDestinations = bind.CollectionField("viewListAdditions");
        _destinationHasRoom = bind
            .Elements(bind.CollectionElementType("viewListAdditions"), "viewListAdditions")
            .Through("list")
            .Call<bool>("HasEmptySpot");
        return bind.Failure;
    }

    internal override RawUpgradeSample Read(object entity) =>
        new(
            _id!(entity),
            _level!(entity),
            _maxLevel!(entity),
            _available!(entity),
            _queuedLevels!(entity),
            _buildTime!(entity),
            _developmentTime!(entity),
            _cachedCostLevel!(entity),
            RewardDestinationFull(entity));

    private bool RewardDestinationFull(object entity)
    {
        var additions = _rewardDestinations!(entity);
        if (additions is null) return false;
        for (var index = 0; index < additions.Count; index++)
        {
            var addition = additions[index];
            if (addition is not null && !_destinationHasRoom!(addition)) return true;
        }
        return false;
    }
}
