using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One position in a list variable an authored <c>ListRequirement</c> compares against.
/// </summary>
/// <remarks>
/// <para>
/// The comparison is over a whole list — <c>GetCount()</c>, <c>IsAnyVisible()</c>, or
/// <c>IsAnyAvailable()</c> — so what a snapshot needs is the membership, not one edge. Each member's
/// own visibility is already a per-pass row in this same snapshot, which is what lets the fold be
/// arithmetic over published facts rather than a native sweep.
/// </para>
/// <para>
/// A <see cref="Position"/> of -1 is the list's header row: it says the list was read and its
/// membership is authored rather than played into. Without it an empty list and an unread one would
/// be the same absence, and only one of those may be answered.
/// </para>
/// </remarks>
internal readonly struct WorldRequirementListMember
{
    internal const int HeaderPosition = -1;

    internal WorldRequirementListMember(Guid listId, int position, Guid memberId)
    {
        ListId = listId;
        Position = position;
        MemberId = memberId;
    }

    internal Guid ListId { get; }

    /// <summary>The index in the list's own order, or -1 for the header row.</summary>
    internal int Position { get; }

    /// <summary>
    /// The member at that position, or <see cref="Guid.Empty"/> for a null element — which the
    /// game's own <c>isinst</c> folds as neither visible nor available rather than skipping.
    /// </summary>
    internal Guid MemberId { get; }
}

internal static class WorldRequirementListLookup
{
    internal static readonly Comparison<WorldRequirementListMember> Order =
        static (left, right) =>
        {
            var byList = left.ListId.CompareTo(right.ListId);
            return byList != 0 ? byList : left.Position.CompareTo(right.Position);
        };

    internal static bool TryFindRange(
        PublicationTable<WorldRequirementListMember> table,
        Guid listId,
        out int start,
        out int count)
    {
        return WorldLookup.TryFindRange(table.AsSpan(), listId,
            static (row, key) => row.ListId.CompareTo(key), out start, out count, resetOnMiss: true);
    }
}
