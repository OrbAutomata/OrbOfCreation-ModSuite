using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>The volatile native gates around one authored prerequisite-link tier.</summary>
internal readonly struct WorldPrerequisiteLinkTier
{
    internal WorldPrerequisiteLinkTier(
        Guid linkId,
        int tierIndex,
        bool activeEnabled,
        bool passiveEnabled,
        long evaluatedFrame,
        long collectedFrame)
    {
        LinkId = linkId;
        TierIndex = tierIndex;
        ActiveEnabled = activeEnabled;
        PassiveEnabled = passiveEnabled;
        EvaluatedFrame = evaluatedFrame;
        CollectedFrame = collectedFrame;
    }

    internal Guid LinkId { get; }
    internal int TierIndex { get; }
    internal bool ActiveEnabled { get; }
    internal bool PassiveEnabled { get; }
    internal long EvaluatedFrame { get; }
    internal long CollectedFrame { get; }
    internal bool EvaluatedThisFrame => EvaluatedFrame == CollectedFrame;
}

internal static class WorldPrerequisiteLinkTierLookup
{
    internal static readonly Comparison<WorldPrerequisiteLinkTier> Order =
        static (left, right) =>
        {
            var byLink = left.LinkId.CompareTo(right.LinkId);
            return byLink != 0 ? byLink : left.TierIndex.CompareTo(right.TierIndex);
        };

    internal static bool TryFind(
        PublicationTable<WorldPrerequisiteLinkTier> table,
        Guid linkId,
        int tierIndex,
        out WorldPrerequisiteLinkTier row)
    {
        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            ref readonly var candidate = ref rows[middle];
            var comparison = candidate.LinkId.CompareTo(linkId);
            if (comparison == 0) comparison = candidate.TierIndex.CompareTo(tierIndex);
            if (comparison == 0)
            {
                row = candidate;
                return true;
            }

            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }

        row = default;
        return false;
    }
}

/// <summary>
/// Reads the live active and passive cache gates around prerequisite-link tiers without calling the
/// native <c>IsEnabled()</c>, whose passive branch can latch prerequisite state.
/// </summary>
internal sealed class WorldPrerequisiteLinkTierReader : IWorldCategoryReader
{
    private readonly Type? _linkType;
    private readonly Func<IList?>? _links;
    private readonly Func<object, Guid>? _identity;
    private readonly Func<object, IList?>? _tiers;
    private readonly Func<object, bool>? _activeEnabled;
    private readonly Func<object, bool>? _passiveEnabled;
    private readonly Func<object, long>? _evaluatedFrame;
    private readonly Func<long>? _currentFrame;
    private readonly string _unavailable;

    internal WorldPrerequisiteLinkTierReader(Type? linkType, Type? gameManagerType)
    {
        _linkType = linkType;
        if (linkType is null || gameManagerType is null)
        {
            _unavailable = linkType is null
                ? "the PrerequisiteLinkSO type was not found on this build"
                : "the GameManager type was not found on this build";
            return;
        }

        var link = new WorldMemberBinding(linkType, "PrerequisiteLinkSO");
        _links = NativeAccessorBinder.StaticListAccessor(linkType, "All");
        _identity = link.StableUuid();
        _tiers = NativeAccessorBinder.CollectionField(linkType, "linkTiers");
        var definitionType = linkType.GetNestedType(
            "LinkDefinition", BindingFlags.Public | BindingFlags.NonPublic);
        _activeEnabled = NativeAccessorBinder.Field<bool>(definitionType, "isActiveEnabled");
        _passiveEnabled = NativeAccessorBinder.Field<bool>(definitionType, "isPassiveEnabled");
        _evaluatedFrame = NativeAccessorBinder.Field<long>(definitionType, "currentFrame");
        _currentFrame = NativeAccessorBinder.StaticField<long>(gameManagerType, "currentFrame");

        _unavailable = _links is null || _identity is null || _tiers is null ||
            _activeEnabled is null || _passiveEnabled is null || _evaluatedFrame is null ||
            _currentFrame is null
                ? "PrerequisiteLinkSO tiers did not expose their complete live gate state on this build"
                : link.Failure;
    }

    public string Category => "prerequisite link states";
    public bool IsAvailable => _linkType is not null && _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        var buffer = frame.PrerequisiteLinkTiers;
        buffer.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        var links = _links!();
        if (links is null) return WorldCategoryReport.Missing(Category, "the link registry was null");

        var sampled = 0;
        var skipped = 0;
        var firstFailure = string.Empty;
        var collectedFrame = _currentFrame!();
        for (var linkIndex = 0; linkIndex < links.Count; linkIndex++)
        {
            var link = links[linkIndex];
            if (link is null) continue;
            try
            {
                var linkId = _identity!(link);
                var tiers = _tiers!(link);
                if (linkId == Guid.Empty || tiers is null) continue;
                for (var tierIndex = 0; tierIndex < tiers.Count; tierIndex++)
                {
                    var tier = tiers[tierIndex];
                    if (tier is null) continue;
                    var row = new WorldPrerequisiteLinkTier(
                        linkId,
                        tierIndex,
                        _activeEnabled!(tier),
                        _passiveEnabled!(tier),
                        _evaluatedFrame!(tier),
                        collectedFrame);
                    buffer.Append(in row);
                    sampled++;
                }
            }
            catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
            {
                WorldSkipTally.Skip(
                    ref skipped,
                    ref firstFailure,
                    "reading a prerequisite-link live gate threw: " +
                        ex.GetBaseException().Message);
            }
        }

        return new WorldCategoryReport(
            Category, WorldCategoryOutcome.Collected, sampled, skipped, firstFailure);
    }
}
