using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>Which of a loaded spell's three charges a cost row states.</summary>
internal enum WorldSpellCostKind
{
    /// <summary>What the cast subtracts once, the moment it fires.</summary>
    Immediate = 0,

    /// <summary>What the spell keeps costing per second while it is up.</summary>
    Drain = 1,

    /// <summary>
    /// What keeping the spell loaded holds against spell capacity, augments and free uses as the
    /// game counts them for this slot.
    /// </summary>
    Weight = 2,
}

/// <summary>
/// What one equipped spell costs on one resource, as the game prices it in that slot.
/// </summary>
/// <remarks>
/// <para>
/// Keyed by slot position rather than by spell, because the price is the equipped instance's and not
/// the recipe's: the same recipe in two slots is priced through two modifier chains, and a table keyed
/// by recipe would have to pick one of the two answers and be wrong about the other.
/// </para>
/// <para>
/// One row per (slot, kind, resource). A spell costing three resources produces three immediate rows,
/// and its drain rows sit beside them under the same slot — which is why the row is not an
/// <c>IWorldEntity</c>: duplicates are the point, and the identity-keyed table rejects them.
/// </para>
/// </remarks>
internal readonly struct WorldSpellCost
{
    internal WorldSpellCost(int slotIndex, WorldSpellCostKind kind, Guid resourceId, BigDouble amount)
    {
        SlotIndex = slotIndex;
        Kind = kind;
        ResourceId = resourceId;
        Amount = amount;
    }

    /// <summary>The loadout position being priced, matching <see cref="WorldSpellSlot.SlotIndex"/>.</summary>
    internal int SlotIndex { get; }

    /// <summary>Whether this is the one-off cast price or the ongoing upkeep.</summary>
    internal WorldSpellCostKind Kind { get; }

    /// <summary>Which resource is charged.</summary>
    internal Guid ResourceId { get; }

    /// <summary>How much of it, before the resource's own quality conversion.</summary>
    internal BigDouble Amount { get; }
}

/// <summary>
/// Range lookup over the spell-cost table, which is sorted by slot, then kind, then resource.
/// </summary>
/// <remarks>
/// The rows a consumer wants are always "this slot's prices of this kind", and the sort makes that
/// range contiguous, so a binary search for its first row plus a forward walk answers it in
/// <c>O(log n + k)</c> — the shape purchase costs already use for the same reason.
/// </remarks>
internal static class WorldSpellCostLookup
{
    internal static readonly Comparison<WorldSpellCost> Order =
        static (left, right) => {
            var bySlot = left.SlotIndex.CompareTo(right.SlotIndex);
            if (bySlot != 0) return bySlot;
            var byKind = ((int)left.Kind).CompareTo((int)right.Kind);
            return byKind != 0 ? byKind : left.ResourceId.CompareTo(right.ResourceId);
        };

    /// <summary>
    /// The half-open row range belonging to one slot's costs of one kind. Both indices are zero when
    /// the slot published none, which is the honest reading of a spell that costs nothing on that
    /// axis as well as of one whose price could not be read — the caller separates those by asking
    /// whether the slot itself was published.
    /// </summary>
    internal static bool TryFindRange(
        PublicationTable<WorldSpellCost> table,
        int slotIndex,
        WorldSpellCostKind kind,
        out int start,
        out int count)
    {
        return WorldLookup.TryFindRange(table.AsSpan(), (Slot: slotIndex, Kind: kind),
            static (row, key) => Compare(row.SlotIndex, row.Kind, key.Slot, key.Kind), out start, out count, resetOnMiss: true);
    }

    private static int Compare(int leftSlot, WorldSpellCostKind leftKind, int rightSlot, WorldSpellCostKind rightKind)
    {
        var bySlot = leftSlot.CompareTo(rightSlot);
        return bySlot != 0 ? bySlot : ((int)leftKind).CompareTo((int)rightKind);
    }
}
