using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>One resource the game's left resource panel draws, in the order it draws them.</summary>
/// <remarks>
/// The panel is the Mana orb above two authored lists, <c>NonManaCoreResources</c> and
/// <c>MaterialResources</c>; together they are exactly the game's own <c>SelectableResources</c>,
/// and every other resource is drawn by the page that owns it — experience on its skill, advancement
/// and capacity budgets on their screens. A member is drawn only while <c>ResourceSO.visible</c> is
/// set, which is what <c>UIResourceDisplay.IsVisible()</c> reads, so readers filter on that flag.
/// </remarks>
internal readonly struct WorldResourceBarEntry
{
    internal WorldResourceBarEntry(Guid resourceId) => ResourceId = resourceId;

    internal Guid ResourceId { get; }
}

internal static class WorldResourceBarDeriver
{
    internal static PublicationTable<WorldResourceBarEntry> Build(
        WorldRelationBuffer<WorldResourceBarEntry> buffer)
    {
        if (buffer.Count == 0) return PublicationTable<WorldResourceBarEntry>.Empty;
        var rows = new WorldResourceBarEntry[buffer.Count];
        for (var index = 0; index < rows.Length; index++) rows[index] = buffer[index];
        return PublicationTable<WorldResourceBarEntry>.Create(rows, rows.Length);
    }
}

/// <summary>
/// Reads the resource panel's membership once per lifecycle: the pinned Mana orb, then each pinned
/// list's authored members in authored order.
/// </summary>
/// <remarks>
/// Published whole or withheld whole: a partial panel reads exactly like a run that has fewer
/// resources, and the overview states what the player has from it.
/// </remarks>
internal sealed class WorldResourceBarReader : IWorldCategoryReader
{
    private static readonly Guid[] Lists =
    {
        KnownEntities.ResourceBarCore.Uuid,
        KnownEntities.ResourceBarMaterials.Uuid,
    };

    private readonly Func<IDictionary?>? _registry;
    private readonly Type? _resourceType;
    private readonly Type? _listType;
    private readonly Func<object, Guid>? _resourceIdentity;
    private readonly Func<object, IList?>? _members;
    private readonly string _unavailable;

    internal WorldResourceBarReader(Func<string, Type?> resolveType)
    {
        if (resolveType is null) throw new ArgumentNullException(nameof(resolveType));
        var identityType = resolveType("IdScriptableObject");
        _registry = NativeAccessorBinder.StaticDictionaryAccessor(identityType, "RuntimeLookup");
        _resourceType = resolveType("ResourceSO");
        _listType = resolveType("ResourceListVariable");
        if (identityType is null || _resourceType is null || _listType is null)
        {
            _unavailable = "the IdScriptableObject, ResourceSO or ResourceListVariable type was " +
                "not found on this build";
            return;
        }

        var resource = new WorldMemberBinding(_resourceType, "ResourceSO");
        _resourceIdentity = resource.StableUuid();
        _members = NativeAccessorBinder.CollectionField(_listType, "value");
        _unavailable = _members is null
            ? "ResourceListVariable did not expose its authored membership on this build"
            : resource.Failure;
    }

    public string Category => "resource bar";

    public bool IsAvailable => _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        var buffer = frame.ResourceBar;
        buffer.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        var registry = _registry?.Invoke();
        if (registry is null) return Withheld(buffer, "the native identity registry was unreadable");

        try
        {
            var orb = registry[KnownEntities.Mana.Uuid];
            if (orb is null || orb.GetType() != _resourceType ||
                _resourceIdentity!(orb) != KnownEntities.Mana.Uuid)
                return Withheld(buffer, "the Mana orb's resource did not resolve to its ResourceSO");
            buffer.Append(new WorldResourceBarEntry(KnownEntities.Mana.Uuid));

            for (var index = 0; index < Lists.Length; index++)
            {
                var listId = Lists[index];
                var list = registry[listId];
                if (list is null || list.GetType() != _listType)
                {
                    return Withheld(
                        buffer,
                        "pinned resource list " + listId.ToString("D") +
                        " did not resolve to a ResourceListVariable");
                }

                var members = _members!(list);
                if (members is null)
                {
                    return Withheld(
                        buffer, "pinned resource list " + listId.ToString("D") + " held no value list");
                }

                for (var member = 0; member < members.Count; member++)
                {
                    var entry = members[member];
                    var resourceId = entry is not null && entry.GetType() == _resourceType
                        ? _resourceIdentity!(entry)
                        : Guid.Empty;
                    if (resourceId == Guid.Empty)
                    {
                        return Withheld(
                            buffer,
                            "pinned resource list " + listId.ToString("D") +
                            " carried a member that is not a ResourceSO with an identity");
                    }
                    buffer.Append(new WorldResourceBarEntry(resourceId));
                }
            }
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return Withheld(
                buffer, "reading the resource panel's lists threw: " + ex.GetBaseException().Message);
        }

        return new WorldCategoryReport(
            Category, WorldCategoryOutcome.Collected, buffer.Count, 0, string.Empty);
    }

    private WorldCategoryReport Withheld(
        WorldRelationBuffer<WorldResourceBarEntry> buffer,
        string reason)
    {
        buffer.Reset();
        return WorldCategoryReport.Missing(Category, "the resource panel's membership was withheld: " + reason);
    }
}
