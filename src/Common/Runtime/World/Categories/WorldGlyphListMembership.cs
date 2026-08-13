using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One authored membership edge: this glyph is on this <c>GlyphListVariable</c>.
/// </summary>
/// <remarks>
/// A glyph list is what a page is bound to, so membership is the fact that says where the player
/// meets a glyph. It is authored and immutable for the whole lifecycle, like the upgrade membership
/// beside it, and unlike that one a glyph may sit on several lists at once — a core unlocker is
/// offered by the spellbook, the alchemy lab and the artifact bench alike.
/// </remarks>
internal readonly struct WorldGlyphListMembership
{
    internal WorldGlyphListMembership(Guid glyphId, Guid listId)
    {
        GlyphId = glyphId;
        ListId = listId;
    }

    internal Guid GlyphId { get; }
    internal Guid ListId { get; }
}

/// <summary>
/// The authored <c>GlyphListVariable</c> assets of the pinned build, by stable uuid.
/// </summary>
/// <remarks>
/// Nine ship. Four of them are runtime selections the player fills and empty when authored, and
/// <c>AllGlyphs</c> holds every glyph in the game, so membership in it says the same thing about all
/// 47 rows. What is left is the four authored populations a page draws from, which is this list. No
/// walk reaches them — a glyph list is named by <c>ViewSO.relevantLists</c> and by prefab data, and
/// neither is a registry — so each is reached by the identity it carries, against the pinned build.
/// </remarks>
internal static class WorldGlyphLists
{
    internal static readonly Guid[] All =
    {
        KnownEntities.GlyphsAugmentSpell.Uuid,
        KnownEntities.GlyphsCoreSpell.Uuid,
        KnownEntities.GlyphsCoreAlchemy.Uuid,
        KnownEntities.GlyphsEquipment.Uuid,
    };
}

internal static class WorldGlyphListMembershipDeriver
{
    internal static PublicationTable<WorldGlyphListMembership> Build(
        WorldRelationBuffer<WorldGlyphListMembership> buffer) =>
        WorldScribeRelationDeriver.Build(
            buffer,
            static (left, right) =>
            {
                var glyph = left.GlyphId.CompareTo(right.GlyphId);
                return glyph != 0 ? glyph : left.ListId.CompareTo(right.ListId);
            });
}

internal static class WorldGlyphListMembershipLookup
{
    /// <summary>The lists one glyph is authored onto, as a contiguous range of the sorted table.</summary>
    internal static bool TryFindRange(
        PublicationTable<WorldGlyphListMembership> table,
        Guid glyphId,
        out int start,
        out int count)
    {
        var low = 0;
        var high = table.Count - 1;
        var found = -1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = table[middle].GlyphId.CompareTo(glyphId);
            if (comparison < 0) low = middle + 1;
            else
            {
                if (comparison == 0) found = middle;
                high = middle - 1;
            }
        }
        if (found < 0)
        {
            start = 0;
            count = 0;
            return false;
        }
        start = found;
        var end = found + 1;
        while (end < table.Count && table[end].GlyphId == glyphId) end++;
        count = end - found;
        return true;
    }
}

/// <summary>
/// Reads the four authored glyph lists once per lifecycle.
/// </summary>
/// <remarks>
/// Membership is published whole or withheld whole, exactly as the upgrade membership beside it is:
/// a partial table cannot be told apart from a glyph that genuinely sits on no authored list, and
/// one of those two is a fact a consumer is entitled to state. So the first list that will not read
/// empties the table and names why, and the category still reports itself collected — the
/// withholding is the answer, and a consumer reading an empty table says so rather than guessing.
/// </remarks>
internal sealed class WorldGlyphListMembershipReader : IWorldCategoryReader
{
    private readonly Type? _identityType;
    private readonly Type? _listType;
    private readonly Type? _glyphType;
    private readonly string _unavailable;
    private readonly Func<object, Guid>? _listIdentity;
    private readonly Func<object, Guid>? _glyphIdentity;
    private readonly Func<object, System.Collections.IList?>? _members;

    internal WorldGlyphListMembershipReader(Func<string, Type?> resolveType)
    {
        if (resolveType is null) throw new ArgumentNullException(nameof(resolveType));
        _identityType = resolveType("IdScriptableObject");
        _listType = resolveType(KnownEntities.GlyphsAugmentSpell.ManagedTypeName);
        _glyphType = resolveType("GlyphSO");
        if (_identityType is null || _listType is null || _glyphType is null)
        {
            _unavailable = _identityType is null
                ? "the IdScriptableObject type was not found on this build"
                : _listType is null
                    ? "the GlyphListVariable type was not found on this build"
                    : "the GlyphSO type was not found on this build";
            return;
        }

        var list = new WorldMemberBinding(_listType, "GlyphListVariable");
        _listIdentity = list.Call<Guid>("GetGuid");
        var glyph = new WorldMemberBinding(_glyphType, "GlyphSO");
        _glyphIdentity = glyph.Call<Guid>("GetGuid");
        _members = NativeAccessorBinder.CollectionField(_listType, "value");
        _unavailable = _members is null
            ? "GlyphListVariable did not expose its authored membership on this build"
            : list.Failure.Length > 0
                ? list.Failure
                : glyph.Failure;
    }

    public string Category => "glyph lists";

    public bool IsAvailable => _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        var buffer = frame.GlyphListMemberships;
        buffer.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        var registry = NativeAccessorBinder.StaticDictionary(_identityType, "RuntimeLookup");
        if (registry is null) return Withheld(buffer, "the native identity registry was unreadable");

        var sampled = 0;
        for (var index = 0; index < WorldGlyphLists.All.Length; index++)
        {
            var listId = WorldGlyphLists.All[index];
            var list = registry[listId];
            if (list is null || list.GetType() != _listType)
            {
                return Withheld(
                    buffer,
                    "pinned glyph list " + listId.ToString("D") +
                    " did not resolve to a GlyphListVariable");
            }

            try
            {
                if (_listIdentity!(list) != listId)
                {
                    return Withheld(
                        buffer,
                        "pinned glyph list " + listId.ToString("D") +
                        " answered to a different identity");
                }

                var members = _members!(list);
                var seen = new HashSet<Guid>();
                for (var member = 0; member < (members?.Count ?? 0); member++)
                {
                    var entry = members![member];
                    if (entry is null || entry.GetType() != _glyphType)
                    {
                        return Withheld(
                            buffer,
                            "pinned glyph list " + listId.ToString("D") +
                            " carried a member that is not a GlyphSO");
                    }

                    var memberId = _glyphIdentity!(entry);
                    if (memberId == Guid.Empty || !seen.Add(memberId))
                    {
                        return Withheld(
                            buffer,
                            "pinned glyph list " + listId.ToString("D") +
                            " carried a member with no usable identity");
                    }

                    buffer.Append(new WorldGlyphListMembership(memberId, listId));
                    sampled++;
                }
            }
            catch (Exception ex)
            {
                return Withheld(
                    buffer,
                    "pinned glyph list " + listId.ToString("D") + " was unreadable: " +
                    ex.GetBaseException().Message);
            }
        }

        return new WorldCategoryReport(
            Category, WorldCategoryOutcome.Collected, sampled, 0, string.Empty);
    }

    private WorldCategoryReport Withheld(
        WorldRelationBuffer<WorldGlyphListMembership> buffer,
        string reason)
    {
        buffer.Reset();
        return new WorldCategoryReport(
            Category,
            WorldCategoryOutcome.Collected,
            sampled: 0,
            skipped: 0,
            "authored glyph list membership was withheld: " + reason);
    }
}
