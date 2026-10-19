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
    internal static bool TryFindRange(
        PublicationTable<WorldRequirementListMember> table,
        Guid listId,
        out int start,
        out int count)
    {
        start = 0;
        count = 0;
        if (listId == Guid.Empty) return false;

        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        var found = -1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = rows[middle].ListId.CompareTo(listId);
            if (comparison == 0)
            {
                found = middle;
                high = middle - 1;
                continue;
            }

            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }

        if (found < 0) return false;

        start = found;
        while (start + count < rows.Length && rows[start + count].ListId == listId) count++;
        return true;
    }
}

internal sealed class WorldRequirementListBuffer
{
    private WorldRequirementListMember[] _samples = new WorldRequirementListMember[32];
    private int _count;

    internal int Count => _count;
    internal ref readonly WorldRequirementListMember this[int index] => ref _samples[index];
    internal void Reset() => _count = 0;

    internal void Append(in WorldRequirementListMember sample)
    {
        if (_count >= _samples.Length) Array.Resize(ref _samples, _samples.Length * 2);
        _samples[_count++] = sample;
    }
}

internal static class WorldRequirementListDeriver
{
    internal static PublicationTable<WorldRequirementListMember> Build(
        WorldRequirementListBuffer buffer)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if (buffer.Count == 0) return PublicationTable<WorldRequirementListMember>.Empty;

        var rows = new WorldRequirementListMember[buffer.Count];
        for (var index = 0; index < buffer.Count; index++) rows[index] = buffer[index];
        Array.Sort(rows, static (left, right) =>
        {
            var byList = left.ListId.CompareTo(right.ListId);
            return byList != 0 ? byList : left.Position.CompareTo(right.Position);
        });
        return PublicationTable<WorldRequirementListMember>.Create(rows, rows.Length);
    }
}
