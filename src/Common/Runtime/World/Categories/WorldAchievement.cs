using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>One achievement as published: its level, whether it has been seen, and the ceiling it counts toward.</summary>
internal readonly struct WorldAchievement : IWorldEntity
{
    internal WorldAchievement(
        Guid achievementId,
        int level,
        bool seen,
        bool logProgress,
        string steamApiName,
        int maxLevels,
        int achievementStrength)
    {
        AchievementId = achievementId;
        Level = level;
        Seen = seen;
        LogProgress = logProgress;
        SteamApiName = steamApiName;
        MaxLevels = maxLevels;
        AchievementStrength = achievementStrength;
    }

    internal Guid AchievementId { get; }

    public Guid EntityId => AchievementId;

    internal int Level { get; }

    internal bool Seen { get; }

    internal bool LogProgress { get; }

    internal string SteamApiName { get; }

    internal int MaxLevels { get; }

    internal int AchievementStrength { get; }
}

internal sealed class WorldAchievementBinder : WorldPlainBinder<WorldAchievement>
{
    private Func<object, Guid>? _id;
    private Func<object, int>? _level;
    private Func<object, bool>? _seen;
    private Func<object, bool>? _logProgress;
    private Func<object, string>? _steamApiName;
    private Func<object, int>? _maxLevels;
    private Func<object, int>? _achievementStrength;

    internal override string Category => "achievements";

    internal override string TypeName => "AchievementSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.StableUuid();
        _level = bind.Field<int>("level");
        _seen = bind.Field<bool>("seen");
        _logProgress = bind.Field<bool>("logProgress");
        _steamApiName = bind.Field<string>("steamApiName");
        _maxLevels = bind.Field<int>("maxLevels");
        _achievementStrength = bind.Field<int>("achievementStrength");
        return bind.Failure;
    }

    internal override WorldAchievement Read(object entity) =>
        new(
            _id!(entity),
            _level!(entity),
            _seen!(entity),
            _logProgress!(entity),
            _steamApiName!(entity),
            _maxLevels!(entity),
            _achievementStrength!(entity));
}
