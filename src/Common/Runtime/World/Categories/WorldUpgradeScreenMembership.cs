using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One authored membership edge: this upgrade is on this <c>UpgradeListVariable</c>.
/// </summary>
/// <remarks>
/// <para>
/// The game groups upgrades by screen, and it does it with hand-authored membership lists swapped
/// into one panel by <c>ListViewSwapper</c> on the active <c>ViewSO</c>. Membership is therefore the
/// only fact that says which screen shows a row, and it is authored — immutable for the whole
/// lifecycle, like the route table beside it.
/// </para>
/// <para>
/// This is deliberately not a <see cref="WorldPurchaseViewRoute"/>. A route names a view, and its
/// consumer asks that view whether it is available before admitting a purchase. Three of the nine
/// authored upgrade lists are named by no view at all, so they can never carry a route, and adding
/// one for them would hand Auto Buy an admission path the game never published. Membership answers a
/// different question — where the player finds this row — and answers it for all nine lists.
/// </para>
/// </remarks>
internal readonly struct WorldUpgradeListMembership
{
    internal WorldUpgradeListMembership(Guid upgradeId, Guid listId)
    {
        UpgradeId = upgradeId;
        ListId = listId;
    }

    internal Guid UpgradeId { get; }
    internal Guid ListId { get; }
}

/// <summary>
/// Every authored <c>UpgradeListVariable</c> of the pinned build, by stable uuid.
/// </summary>
/// <remarks>
/// Six of the nine are reachable by walking <c>ViewSO.relevantLists</c>, and the walk reads their
/// membership already. The other three — the Scholar screen's 27 upgrades, the three world aspects,
/// and the empty Time screen list — are named only by prefab <c>ListViewSwapper</c> data, which is
/// outside both the assembly and the serialized object graph. Nothing in the game points at them, so
/// the only way to reach them is by the identity they carry, which is what this pins. The build is
/// pinned too, so an authored list that stops answering to its uuid is a defect the read reports
/// rather than a case it works around.
/// </remarks>
internal static class WorldUpgradeScreenLists
{
    internal static readonly Guid[] All =
    {
        KnownEntities.UpgradesAll.Uuid,
        KnownEntities.UpgradesAlchemyScreen.Uuid,
        KnownEntities.UpgradesAspectsScreen.Uuid,
        KnownEntities.UpgradesMagicScreen.Uuid,
        KnownEntities.UpgradesRitualScreen.Uuid,
        KnownEntities.UpgradesScholarScreen.Uuid,
        KnownEntities.UpgradesTimeScreen.Uuid,
        KnownEntities.UpgradesWorkshopScreen.Uuid,
        KnownEntities.UpgradesWorldScreen.Uuid,
    };
}

internal static class WorldUpgradeListMembershipDeriver
{
    internal static PublicationTable<WorldUpgradeListMembership> Build(
        WorldRelationBuffer<WorldUpgradeListMembership> buffer) =>
        WorldScribeRelationDeriver.Build(
            buffer,
            static (left, right) =>
            {
                var upgrade = left.UpgradeId.CompareTo(right.UpgradeId);
                return upgrade != 0 ? upgrade : left.ListId.CompareTo(right.ListId);
            });
}

internal static class WorldUpgradeListMembershipLookup
{
    /// <summary>
    /// The lists one upgrade is authored onto, as a contiguous range of the sorted table.
    /// </summary>
    internal static bool TryFindRange(
        PublicationTable<WorldUpgradeListMembership> table,
        Guid upgradeId,
        out int start,
        out int count)
    {
        var low = 0;
        var high = table.Count - 1;
        var found = -1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = table[middle].UpgradeId.CompareTo(upgradeId);
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
        while (end < table.Count && table[end].UpgradeId == upgradeId) end++;
        count = end - found;
        return true;
    }
}
