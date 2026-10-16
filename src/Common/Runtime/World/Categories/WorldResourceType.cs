using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>One resource type as published: the levels it carries, and the flags that decide how it is audited and displayed.</summary>
/// <remarks>
/// The records this type pushes onto its resources are published by the <c>type modifiers</c>
/// category, which carries every type asset's records under one uniform shape — the counts this row
/// used to approximate them with, and the modifier entries behind those counts. See
/// <see cref="WorldTypeModifier"/> for why a total derived from them must not be multiplied into a
/// resource value the suite already publishes.
/// </remarks>
internal readonly struct WorldResourceType : IWorldEntity
{
    internal WorldResourceType(
        Guid resourceTypeId,
        int level,
        int freeLevels,
        bool specialHidden,
        bool ignoreAudit,
        bool ignoreEffects,
        bool auditHasMaxQuantity,
        WorldLevelableDecision levelDecision = default)
    {
        ResourceTypeId = resourceTypeId;
        Level = level;
        FreeLevels = freeLevels;
        SpecialHidden = specialHidden;
        IgnoreAudit = ignoreAudit;
        IgnoreEffects = ignoreEffects;
        AuditHasMaxQuantity = auditHasMaxQuantity;
        LevelDecision = levelDecision;
    }

    internal Guid ResourceTypeId { get; }

    public Guid EntityId => ResourceTypeId;

    /// <summary>Levels bought and levels granted.</summary>
    internal int Level { get; }

    internal int FreeLevels { get; }

    /// <summary>Whether the type is hidden as a special, skipped by the audit, or has its effects ignored.</summary>
    internal bool SpecialHidden { get; }

    internal bool IgnoreAudit { get; }

    internal bool IgnoreEffects { get; }

    /// <summary>Whether the audit treats members of this type as capped.</summary>
    internal bool AuditHasMaxQuantity { get; }

    internal WorldLevelableDecision LevelDecision { get; }
}

internal sealed class WorldResourceTypeBinder : WorldPlainBinder<WorldResourceType>
{
    private readonly Func<string, Type?> _resolveType;
    private Func<object, Guid>? _id;
    private Func<object, int>? _level;
    private Func<object, int>? _freeLevels;
    private Func<object, bool>? _specialHidden;
    private Func<object, bool>? _ignoreAudit;
    private Func<object, bool>? _ignoreEffects;
    private Func<object, bool>? _auditHasMaxQuantity;
    private WorldLevelableDecisionBinding? _levelDecision;

    internal WorldResourceTypeBinder(Func<string, Type?> resolveType) =>
        _resolveType = resolveType ?? throw new ArgumentNullException(nameof(resolveType));

    internal override string Category => "resource types";

    internal override string TypeName => "ResourceTypeSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _level = bind.Field<int>("level");
        _freeLevels = bind.Field<int>("freeLevels");
        _specialHidden = bind.Field<bool>("specialHidden");
        _ignoreAudit = bind.Field<bool>("ignoreAudit");
        _ignoreEffects = bind.Field<bool>("ignoreEffects");
        _auditHasMaxQuantity = bind.Field<bool>("auditHasMaxQuantity");
        _levelDecision = new WorldLevelableDecisionBinding(type, true, _resolveType);
        return Join(bind.Failure, _levelDecision.Failure);
    }

    internal override WorldResourceType Read(object entity) =>
        new(
            _id!(entity),
            _level!(entity),
            _freeLevels!(entity),
            _specialHidden!(entity),
            _ignoreAudit!(entity),
            _ignoreEffects!(entity),
            _auditHasMaxQuantity!(entity),
            _levelDecision!.Read(entity));

    private static string Join(string left, string right) =>
        left.Length == 0 ? right : right.Length == 0 ? left : left + "; " + right;
}
