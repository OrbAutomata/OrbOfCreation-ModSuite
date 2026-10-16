using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>One challenge as published: its level, the state it is in, and the shape of its reward.</summary>
internal readonly struct WorldChallenge : IWorldEntity
{
    internal WorldChallenge(
        Guid challengeId,
        int level,
        int state,
        bool seen,
        bool rewardQueued,
        int maxLevel,
        int weight,
        double difficulty,
        double baseReward,
        bool availableToRun = false,
        bool completedOnce = false,
        bool maximumLevelReached = false,
        BigDouble nextDifficulty = default,
        BigDouble nextReward = default,
        int conditionBeforeType = 0,
        BigDouble timeLimit = default)
    {
        ChallengeId = challengeId;
        Level = level;
        State = state;
        Seen = seen;
        RewardQueued = rewardQueued;
        MaxLevel = maxLevel;
        Weight = weight;
        Difficulty = difficulty;
        BaseReward = baseReward;
        AvailableToRun = availableToRun;
        CompletedOnce = completedOnce;
        MaximumLevelReached = maximumLevelReached;
        NextDifficulty = nextDifficulty;
        NextReward = nextReward;
        ConditionBeforeType = conditionBeforeType;
        TimeLimit = timeLimit;
    }

    /// <summary>
    /// The <c>ChallengeCondition.BeforeType</c> value that means the run is racing a clock, so
    /// <see cref="TimeLimit"/> carries what it races. The game switches on this enum as
    /// <c>None</c>, <c>Time</c>, <c>Prereq</c>.
    /// </summary>
    internal const int TimedCondition = 1;

    internal Guid ChallengeId { get; }

    public Guid EntityId => ChallengeId;

    internal int Level { get; }

    internal int State { get; }

    internal bool Seen { get; }

    internal bool RewardQueued { get; }

    internal int MaxLevel { get; }

    internal int Weight { get; }

    internal double Difficulty { get; }

    internal double BaseReward { get; }

    internal bool AvailableToRun { get; }

    internal bool CompletedOnce { get; }

    internal bool MaximumLevelReached { get; }

    internal BigDouble NextDifficulty { get; }

    internal BigDouble NextReward { get; }

    /// <summary>Which failure condition this challenge's run is held to, if any.</summary>
    internal int ConditionBeforeType { get; }

    /// <summary>
    /// How long the run may take at <see cref="Level"/>, in seconds, as the game itself computes it.
    /// Meaningful only while <see cref="ConditionBeforeType"/> is <see cref="TimedCondition"/>.
    /// </summary>
    internal BigDouble TimeLimit { get; }
}

internal sealed class WorldChallengeBinder : WorldPlainBinder<WorldChallenge>
{
    private Func<object, Guid>? _id;
    private Func<object, int>? _level;
    private Func<object, int>? _state;
    private Func<object, bool>? _seen;
    private Func<object, bool>? _rewardQueued;
    private Func<object, int>? _maxLevel;
    private Func<object, int>? _weight;
    private Func<object, double>? _difficulty;
    private Func<object, double>? _baseReward;
    private Func<object, bool>? _availableToRun;
    private Func<object, bool>? _completedOnce;
    private Func<object, bool>? _maximumLevelReached;
    private Func<object, BigDouble>? _nextDifficulty;
    private Func<object, BigDouble>? _nextReward;
    private Func<object, int>? _conditionBeforeType;
    private Func<object, int, BigDouble>? _timeLimit;

    internal override string Category => "challenges";

    internal override string TypeName => "ChallengeSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _level = bind.Field<int>("level");
        _state = bind.EnumField("state");
        _seen = bind.Field<bool>("hasBeenSeen");
        _rewardQueued = bind.Field<bool>("rewardQueued");
        _maxLevel = bind.Field<int>("maxLevel");
        _weight = bind.Field<int>("weight");
        _difficulty = bind.Field<double>("difficulty");
        _baseReward = bind.Field<double>("baseReward");
        _availableToRun = bind.Call<bool>("IsAvailableToRun");
        _completedOnce = bind.Call<bool>("IsCompletedOnce");
        _maximumLevelReached = bind.Call<bool>("IsMaxLevel");
        _nextDifficulty = bind.Call<BigDouble>("GetDifficulty");
        _nextReward = bind.Call<BigDouble>("GetNextInstanceBaseReward");

        // The clock a run races is the game's own arithmetic — the condition's authored `TimeValue`
        // folded through its `timeLimitScaling` modifier list at the level being attempted — so the
        // limit is asked for rather than re-derived here. `SlowIncrement` and `GetTooltipNodes` both
        // pass `ChallengeSO.level`, which is the level this row already publishes.
        var condition = bind.Through("challengeCondition");
        _conditionBeforeType = condition.EnumField("beforeType");
        _timeLimit = condition.Call<int, BigDouble>("GetTimeLimit");
        return bind.Failure;
    }

    internal override WorldChallenge Read(object entity)
    {
        var level = _level!(entity);
        var beforeType = _conditionBeforeType!(entity);
        return new(
            _id!(entity),
            level,
            _state!(entity),
            _seen!(entity),
            _rewardQueued!(entity),
            _maxLevel!(entity),
            _weight!(entity),
            _difficulty!(entity),
            _baseReward!(entity),
            _availableToRun!(entity),
            _completedOnce!(entity),
            _maximumLevelReached!(entity),
            _nextDifficulty!(entity),
            _nextReward!(entity),
            beforeType,
            beforeType == WorldChallenge.TimedCondition
                ? _timeLimit!(entity, level)
                : default);
    }
}
