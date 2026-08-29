using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One heading the Statistics tab groups its numbers under — Agromancy Power, Alchemy Speed,
/// Research Rate — and the thing a bonus "to all Agromancy Power" is actually applied to.
/// </summary>
/// <remarks>
/// <para>
/// A group is an <c>UpgradeableObject</c> like a structure or an upgrade, and it holds exactly one
/// modifier record. What makes it different from every other taxonomy the world publishes is where
/// that record goes: <c>AttributeGroupSO.BindAllMods()</c> walks the group's own
/// <c>recordReferences</c> and calls <c>MergingModifierRecord.AddRecord</c> once per reference, so
/// the group's record is merged <i>into</i> a record that lives on some other entity, scaled by that
/// reference's ratio. The group is a distributor, and its record is deliberately unpublished for the
/// same reason the type rosters' are — a number that has already been distributed into its members
/// would be that bonus counted twice.
/// </para>
/// <para>
/// What is published is the distribution itself, as <see cref="WorldAttributeGroupMember"/> rows
/// carried on the group's own answer. See that type for which way the edge points and why.
/// </para>
/// </remarks>
internal readonly struct WorldAttributeGroup : IWorldEntity
{
    internal WorldAttributeGroup(Guid attributeGroupId, string description)
    {
        AttributeGroupId = attributeGroupId;
        Description = description ?? string.Empty;
    }

    internal Guid AttributeGroupId { get; }

    public Guid EntityId => AttributeGroupId;

    /// <summary>The sentence the tooltip prints under the heading.</summary>
    internal string Description { get; }
}

/// <summary>The Statistics tab's headings: <c>AttributeGroupSO.All</c>.</summary>
internal sealed class WorldAttributeGroupBinder : WorldPlainBinder<WorldAttributeGroup>
{
    private Func<object, Guid>? _id;
    private Func<object, string>? _description;

    internal override string Category => "attribute groups";

    internal override string TypeName => "AttributeGroupSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _description = bind.Field<string>("description");
        return bind.Failure;
    }

    internal override WorldAttributeGroup Read(object entity) =>
        new(_id!(entity), _description!(entity));
}

/// <summary>
/// One thing a stat group's bonus reaches: the entity whose record the group merges into, which of
/// that entity's records it is, and at what ratio.
/// </summary>
/// <remarks>
/// <para>
/// <b>The edge points group → member, because that is the only direction the game stores it.</b>
/// Nothing on the far side names its group: <c>AttributeSO</c> holds no group reference, and neither
/// does any of the fourteen classes these references land on. The whole relation is
/// <c>AttributeGroupSO.recordReferences</c>, a list on the group, and each entry is an
/// <c>UpgradeableObject.UpgradeReference</c> — a target object plus the
/// <see cref="Property"/>/<see cref="PropertyIndex"/> pair that selects one record on it. Publishing
/// a member → group edge would mean inventing one, and it would be wrong on its face: a target can
/// appear under several groups, and the same group can reach one target twice on two different
/// records.
/// </para>
/// <para>
/// <b>The members are not the statistics glossary.</b> This was the shape the group category was
/// expected to have, and the data disagrees. Of the 115 references on the pinned build, forty land on
/// a <c>DoubleVariable</c>, sixteen on an agromancy action type, fourteen on an alchemy type, ten on
/// a resource, and the rest across nine more classes including three that point at another
/// <c>AttributeGroupSO</c>; none points at an <c>AttributeSO</c> record. Thirteen of the eighteen
/// distinct <see cref="Property"/> values do not exist as a statistic's <c>globalDefinition</c> at
/// all. A group groups <i>the things that carry a number</i>, not the word printed above it.
/// </para>
/// <para>
/// <see cref="Ratio"/> and <see cref="RatioExp"/> are the two numbers <c>BindAllMods</c> hands the
/// merge, read from the fields the game's own <c>GetRatio()</c>/<c>GetRatioExp()</c> return
/// unchanged. <see cref="OrderAdjust"/> is the third: it shifts where the merged record sits in the
/// modifier order relative to the ones already on the target.
/// </para>
/// <para>
/// Each reference also carries a <c>Prerequisites.Container</c> that decides whether it is merged at
/// all, and this row does not answer it. Asking is a call to <c>Prerequisites.Container.Check()</c>,
/// which latches <c>available</c> — the per-pass capture write the manifest already carries ten rows
/// of debt for — and 115 more of them four times a second is the wrong way to pay for one boolean.
/// The published rows are therefore the authored distribution, which is what they say.
/// </para>
/// </remarks>
internal readonly struct WorldAttributeGroupMember
{
    internal WorldAttributeGroupMember(
        Guid attributeGroupId,
        int ordinal,
        Guid targetId,
        string property,
        int propertyIndex,
        double ratio,
        double ratioExp,
        int orderAdjust)
    {
        AttributeGroupId = attributeGroupId;
        Ordinal = ordinal;
        TargetId = targetId;
        Property = property ?? string.Empty;
        PropertyIndex = propertyIndex;
        Ratio = ratio;
        RatioExp = ratioExp;
        OrderAdjust = orderAdjust;
    }

    /// <summary>The group the bonus is bought on.</summary>
    internal Guid AttributeGroupId { get; }

    /// <summary>The reference's authored position in the group's list.</summary>
    internal int Ordinal { get; }

    /// <summary>
    /// The entity whose record the group merges into, or <see cref="Guid.Empty"/> where the
    /// reference names nothing.
    /// </summary>
    internal Guid TargetId { get; }

    /// <summary>Which of the target's records this is, under the game's own property name.</summary>
    internal string Property { get; }

    /// <summary>Which entry of that property, where the target holds several under one name.</summary>
    internal int PropertyIndex { get; }

    /// <summary>What the group's record is scaled by before it is merged.</summary>
    internal double Ratio { get; }

    /// <summary>The exponent the same merge applies alongside the ratio.</summary>
    internal double RatioExp { get; }

    /// <summary>How far the merged record is shifted in the target's modifier order.</summary>
    internal int OrderAdjust { get; }
}

/// <summary>
/// Reads every stat group's authored distribution: one row per reference, in authored order.
/// </summary>
/// <remarks>
/// A separate reader rather than part of the group binder, for the reason every relation in this
/// collector is: the group category is one row per asset and this is a list per asset, and the two
/// buffers have different shapes. The walk is the same registry, so a group whose references throw
/// costs its own rows and not the category.
/// </remarks>
internal sealed class WorldAttributeGroupMemberReader : IWorldCategoryReader
{
    private readonly Type? _groupType;
    private readonly string _unavailable;

    private readonly Func<object, Guid>? _groupId;
    private readonly Func<object, IList?>? _references;
    private readonly Func<object, Guid>? _targetId;
    private readonly Func<object, string>? _property;
    private readonly Func<object, int>? _propertyIndex;
    private readonly Func<object, double>? _ratio;
    private readonly Func<object, double>? _ratioExp;
    private readonly Func<object, int>? _orderAdjust;

    internal WorldAttributeGroupMemberReader(Func<string, Type?> resolveType)
    {
        if (resolveType is null) throw new ArgumentNullException(nameof(resolveType));

        _groupType = resolveType("AttributeGroupSO");
        if (_groupType is null)
        {
            _unavailable = "the AttributeGroupSO type was not found on this build";
            return;
        }

        var bind = new WorldMemberBinding(_groupType, "AttributeGroupSO");
        _groupId = bind.Call<Guid>("GetGuid");
        _references = bind.CollectionField("recordReferences");

        var reference = bind.Elements(
            bind.CollectionElementType("recordReferences"),
            "AttributeGroupSO.recordReferences[]");
        _targetId = reference.ReferenceGuid("upgradeableObject");
        _property = reference.Field<string>("propertyType");
        _propertyIndex = reference.Field<int>("propertyIndex");
        _ratio = reference.Field<double>("ratio");
        _ratioExp = reference.Field<double>("ratioExp");
        _orderAdjust = reference.Field<int>("orderAdjust");

        _unavailable = bind.Failure;
    }

    public string Category => "attribute group members";

    public bool IsAvailable => _groupType is not null && _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));

        frame.AttributeGroupMembers.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        var groups = NativeAccessorBinder.StaticList(_groupType, "All");
        if (groups is null)
            return WorldCategoryReport.Missing(Category, "the AttributeGroupSO registry was unreadable");

        var sampled = 0;
        var skipped = 0;
        var firstFailure = string.Empty;

        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            if (group is null) continue;
            try
            {
                sampled += Read(group, frame.AttributeGroupMembers);
            }
            catch (Exception exception)
            {
                skipped++;
                if (firstFailure.Length == 0)
                {
                    firstFailure = "reading a stat group's members threw: " +
                        exception.GetBaseException().Message;
                }
            }
        }

        return new WorldCategoryReport(
            Category, WorldCategoryOutcome.Collected, sampled, skipped, firstFailure);
    }

    private int Read(object group, WorldRelationBuffer<WorldAttributeGroupMember> buffer)
    {
        var groupId = _groupId!(group);
        if (groupId == Guid.Empty) return 0;

        var references = _references!(group);
        var count = references?.Count ?? 0;
        var appended = 0;
        for (var ordinal = 0; ordinal < count; ordinal++)
        {
            var reference = references![ordinal];
            if (reference is null) continue;
            buffer.Append(new WorldAttributeGroupMember(
                groupId,
                ordinal,
                _targetId!(reference),
                _property!(reference),
                _propertyIndex!(reference),
                _ratio!(reference),
                _ratioExp!(reference),
                _orderAdjust!(reference)));
            appended++;
        }

        return appended;
    }
}

/// <summary>Range lookup over the member table, which is keyed by group and then authored order.</summary>
internal static class WorldAttributeGroupMemberLookup
{
    /// <summary>One group's members, as a contiguous range of the sorted table.</summary>
    internal static bool TryFindRange(
        PublicationTable<WorldAttributeGroupMember> table,
        Guid attributeGroupId,
        out int start,
        out int count)
    {
        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (rows[middle].AttributeGroupId.CompareTo(attributeGroupId) < 0) low = middle + 1;
            else high = middle - 1;
        }

        start = low;
        count = 0;
        while (start + count < rows.Length &&
               rows[start + count].AttributeGroupId == attributeGroupId)
        {
            count++;
        }

        return count > 0;
    }
}
