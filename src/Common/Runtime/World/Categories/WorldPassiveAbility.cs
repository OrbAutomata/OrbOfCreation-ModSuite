using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>One passive ability as published: whether it is muted or has been touched, and the token economy it runs on.</summary>
internal readonly struct WorldPassiveAbility : IWorldEntity
{
    internal WorldPassiveAbility(
        Guid passiveAbilityId,
        bool muted,
        bool touched,
        bool hidden,
        bool silent,
        bool global,
        bool startOnCooldown,
        bool ignoreReactionCooldown,
        double reactionTokenCost,
        double maxTokens,
        double minTokenForEffect,
        bool tokenIndividuateDuration,
        bool applyWhileRecharging,
        bool expireAttachedStatusEffect,
        BigDouble tokenRate)
    {
        PassiveAbilityId = passiveAbilityId;
        Muted = muted;
        Touched = touched;
        Hidden = hidden;
        Silent = silent;
        Global = global;
        StartOnCooldown = startOnCooldown;
        IgnoreReactionCooldown = ignoreReactionCooldown;
        ReactionTokenCost = reactionTokenCost;
        MaxTokens = maxTokens;
        MinTokenForEffect = minTokenForEffect;
        TokenIndividuateDuration = tokenIndividuateDuration;
        ApplyWhileRecharging = applyWhileRecharging;
        ExpireAttachedStatusEffect = expireAttachedStatusEffect;
        TokenRate = tokenRate;
    }

    internal Guid PassiveAbilityId { get; }

    public Guid EntityId => PassiveAbilityId;

    internal bool Muted { get; }

    internal bool Touched { get; }

    internal bool Hidden { get; }

    internal bool Silent { get; }

    internal bool Global { get; }

    internal bool StartOnCooldown { get; }

    internal bool IgnoreReactionCooldown { get; }

    internal double ReactionTokenCost { get; }

    internal double MaxTokens { get; }

    internal double MinTokenForEffect { get; }

    internal bool TokenIndividuateDuration { get; }

    internal bool ApplyWhileRecharging { get; }

    internal bool ExpireAttachedStatusEffect { get; }

    internal BigDouble TokenRate { get; }
}

internal sealed class WorldPassiveAbilityBinder : WorldPlainBinder<WorldPassiveAbility>
{
    private Func<object, Guid>? _id;
    private Func<object, bool>? _muted;
    private Func<object, bool>? _touched;
    private Func<object, bool>? _hidden;
    private Func<object, bool>? _silent;
    private Func<object, bool>? _global;
    private Func<object, bool>? _startOnCooldown;
    private Func<object, bool>? _ignoreReactionCooldown;
    private Func<object, double>? _reactionTokenCost;
    private Func<object, double>? _maxTokens;
    private Func<object, double>? _minTokenForEffect;
    private Func<object, bool>? _tokenIndividuateDuration;
    private Func<object, bool>? _applyWhileRecharging;
    private Func<object, bool>? _expireAttachedStatusEffect;
    private Func<object, BigDouble>? _tokenRate;

    internal override string Category => "passive abilities";

    internal override string TypeName => "PassiveAbilitySO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.StableUuid();
        _muted = bind.Field<bool>("muted");
        _touched = bind.Field<bool>("touched");
        _hidden = bind.Field<bool>("hidden");
        _silent = bind.Field<bool>("silent");
        _global = bind.Field<bool>("global");
        _startOnCooldown = bind.Field<bool>("startOnCooldown");
        _ignoreReactionCooldown = bind.Field<bool>("ignoreReactionCooldown");
        _reactionTokenCost = bind.Field<double>("reactionTokenCost");
        _maxTokens = bind.Field<double>("maxTokens");
        _minTokenForEffect = bind.Field<double>("minTokenForEffect");
        _tokenIndividuateDuration = bind.Field<bool>("tokenIndividuateDuration");
        _applyWhileRecharging = bind.Field<bool>("applyWhileRecharging");
        _expireAttachedStatusEffect = bind.Field<bool>("expireAttachedStatusEffect");
        _tokenRate = bind.ModifierRecord("tokenRate");
        return bind.Failure;
    }

    internal override WorldPassiveAbility Read(object entity) =>
        new(
            _id!(entity),
            _muted!(entity),
            _touched!(entity),
            _hidden!(entity),
            _silent!(entity),
            _global!(entity),
            _startOnCooldown!(entity),
            _ignoreReactionCooldown!(entity),
            _reactionTokenCost!(entity),
            _maxTokens!(entity),
            _minTokenForEffect!(entity),
            _tokenIndividuateDuration!(entity),
            _applyWhileRecharging!(entity),
            _expireAttachedStatusEffect!(entity),
            _tokenRate!(entity));
}
